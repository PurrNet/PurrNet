#!/usr/bin/env python3
"""Reference reader for PurrNet match recordings (.purrrec). See FORMAT.md.

    python purrrec.py summary  match.purrrec
    python purrrec.py export   match.purrrec out_dir      # one table per row type (.parquet if pyarrow, else .npz)
    python purrrec.py dump     match.purrrec STREAM_ID [N]
    python purrrec.py input    match.purrrec [N]            # raw device input rows, keys and buttons by name

Client parts (the files clients write and upload themselves) are merged into the server's recording by adding
`--parts part1.purrrec part2.purrrec ...` to any command; their streams then show up like any other custom stream.
    python purrrec.py ui       match.purrrec [TICK] [PLAYER]   # the UI a player saw at a tick (default: the last)

Only numpy is required.
"""
import hashlib
import json
import struct
import sys
import zlib
from collections import defaultdict
from pathlib import Path

import numpy as np

KINDS = {1: "HEADER", 2: "TYPE", 3: "STREAM", 4: "ENTITY", 5: "DESPAWN", 6: "CHUNK",
         7: "COLLIDERS", 8: "PLAYER", 9: "FOOTER", 10: "MESH", 11: "BATCH", 12: "ATTACH", 13: "DETACH", 14: "PART"}

SCALARS = {"bool": "?", "u8": "u1", "i8": "i1", "u16": "<u2", "i16": "<i2", "char": "<u2",
           "u32": "<u4", "i32": "<i4", "u64": "<u8", "i64": "<i8", "f32": "<f4", "f64": "<f8"}


class Reader:
    def __init__(self, data, pos=0):
        self.data = data
        self.pos = pos

    def byte(self):
        b = self.data[self.pos]
        self.pos += 1
        return b

    def varuint(self):
        result = shift = 0
        while True:
            b = self.data[self.pos]
            self.pos += 1
            result |= (b & 0x7F) << shift
            if b < 0x80:
                return result
            shift += 7

    def varint(self):
        v = self.varuint()
        return (v >> 1) ^ -(v & 1)

    def take(self, n):
        out = self.data[self.pos:self.pos + n]
        self.pos += n
        return out

    def string(self):
        return self.take(self.varuint()).decode("utf-8")


class Recording:
    def __init__(self, path):
        self.path = Path(path)
        raw = self.path.read_bytes()
        if raw[:8] != b"PURRREC\0":
            raise ValueError("not a .purrrec file")
        self.version = struct.unpack_from("<H", raw, 8)[0]
        if self.version != 1:
            raise ValueError(f"unsupported version {self.version}")

        self.header = None
        self.footer = None
        self.types = {}
        self.streams = {}
        self.entities = {}
        self.despawns = {}
        self.attaches = []          # ATTACH records: another source's components on an existing entity
        self.detaches = []
        self.players = []
        self.parts = []            # PART records: the players a client part was offered to, with their tokens
        self.merged = []           # client parts merged into this recording (see merge)
        self.colliders = []
        self.meshes = {}
        self.chunks = []           # (stored size, raw payload)
        self.record_bytes = defaultdict(int)
        self.record_counts = defaultdict(int)
        self.file_size = len(raw)
        self.truncated = False

        r = Reader(raw, 10)
        while r.pos < len(raw):
            start = r.pos
            try:
                kind = r.byte()
                length = r.varuint()
            except IndexError:
                self.truncated = True
                break
            if r.pos + length > len(raw):
                self.truncated = True
                break
            payload = r.take(length)
            compressed = bool(kind & 0x80)
            kind &= 0x7F
            name = KINDS.get(kind, f"kind{kind}")
            self.record_bytes[name] += r.pos - start
            self.record_counts[name] += 1
            if compressed:
                payload = zlib.decompress(payload, -15)
            self._record(kind, payload, length)
        self._rows = None

    def _record(self, kind, payload, stored):
        if kind == 11:
            # BATCH: compressed run of metadata records, same framing without the compression bit.
            b = Reader(payload)
            while b.pos < len(payload):
                inner = b.byte()
                n = b.varuint()
                self.record_counts[KINDS.get(inner, f"kind{inner}")] += 1
                self._record(inner, b.take(n), n)
            return
        if kind == 6:
            self.chunks.append((stored, payload))
            return
        if kind == 10:
            m = Reader(payload)
            mid = m.varuint()
            name = m.string()
            nv = m.varuint()
            vertices = np.frombuffer(m.take(nv * 12), dtype="<f4").reshape(nv, 3)
            ni = m.varuint()
            indices = np.frombuffer(m.take(ni * 4), dtype="<u4")
            self.meshes[mid] = {"mesh": mid, "name": name, "vertices": vertices, "indices": indices}
            return
        if kind not in KINDS:
            return
        obj = json.loads(payload)
        if kind == 1:
            self.header = obj
        elif kind == 2:
            self.types[obj["id"]] = obj
        elif kind == 3:
            self.streams[obj["id"]] = obj
        elif kind == 4:
            self.entities[obj["entity"]] = obj
        elif kind == 5:
            self.despawns[obj["entity"]] = obj["tick"]
        elif kind == 7:
            self.colliders.append(obj)
        elif kind == 8:
            self.players.append(obj)
        elif kind == 9:
            self.footer = obj
        elif kind == 12:
            self.attaches.append(obj)
        elif kind == 13:
            self.detaches.append(obj)
        elif kind == 14:
            self.parts.append(obj)

    # ---- client parts ----------------------------------------------------------------------------------

    @property
    def complete(self):
        """False for a file that was cut off (the game was killed or crashed): it has no FOOTER, or ends inside a
        record. Everything before that point is read as usual; nothing is made up for what is missing."""
        return self.footer is not None and not self.truncated

    @property
    def is_part(self):
        return bool((self.header or {}).get("part"))

    def merge(self, part):
        """Adds a client part's streams to this (main) recording; returns {part stream id: merged stream id}.

        The part must carry this recording's id and the token the server gave that player (the recording's PART
        record has the token's SHA-256).
        Merged streams and types get new ids after this recording's own; a merged STREAM also has "part" (the
        part's file name). Their ticks are the server ticks the client was presenting.
        """
        if not isinstance(part, Recording):
            part = Recording(part)
        own, theirs = self.header or {}, part.header or {}
        if self.is_part:
            raise ValueError(f"{self.path.name} is a client part; merge parts into the server's recording")
        if not part.is_part:
            raise ValueError(f"{part.path.name} is not a client part")
        if not own.get("recordingId") or theirs.get("recordingId") != own["recordingId"]:
            raise ValueError(f"{part.path.name} belongs to recording {theirs.get('recordingId')}, "
                             f"not {own.get('recordingId')}")
        player = theirs.get("player")
        hashes = {p["player"]: p["tokenHash"] for p in self.parts}
        if hashes.get(player) != hashlib.sha256(str(theirs.get("token")).encode("ascii", "replace")).hexdigest():
            raise ValueError(f"{part.path.name}: the token does not match the one the server gave player {player}")

        rows = self.rows()
        part_rows = part.rows()
        type_base = max(self.types, default=0)
        stream_base = max(self.streams, default=0)
        for tid, t in part.types.items():
            self.types[type_base + tid] = dict(t, id=type_base + tid)
        mapping = {}
        for sid, s in part.streams.items():
            new = stream_base + sid
            self.streams[new] = dict(s, id=new, type=type_base + s["type"], part=part.path.name)
            if sid in part_rows:
                rows[new] = part_rows[sid]
            self.stream_bytes[new] = part.stream_bytes.get(sid, 0)
            mapping[sid] = new
        self.merged.append({"file": part.path.name, "player": player, "streams": mapping,
                            "complete": part.complete})
        return mapping

    def custom_streams(self, sample, player=None):
        """Ids of the custom streams named `sample` ("camera", "input", ...), optionally of one player."""
        return [sid for sid, s in self.streams.items()
                if s.get("kind") == "custom" and s.get("sample") == sample and (player is None or s.get("player") == player)]

    # ---- rows ------------------------------------------------------------------------------------------

    def dtype(self, type_id):
        t = self.types[type_id]
        return np.dtype({
            "names": [c["name"] for c in t["columns"]],
            "formats": [SCALARS[c["type"]] for c in t["columns"]],
            "offsets": [c["offset"] for c in t["columns"]],
            "itemsize": t["rowSize"],
        })

    def rows(self):
        """{stream id: (ticks uint64[], fixed bytes, [managed values per field])} for the whole file.

        Per-tick streams come back dense: one row for every tick from their first to their last row in each chunk.
        Every per-tick block is checked against its CRC-32; a mismatch raises.
        """
        if self._rows is not None:
            return self._rows
        ticks = defaultdict(list)
        fixed = defaultdict(list)
        managed = {}
        self.stream_bytes = defaultdict(int)
        self.block_spans = []  # (stream id, chunk payload, start, end): for size analysis
        self.verified_blocks = 0
        for _, payload in self.chunks:
            for sid, t, rows, values in self._decode_chunk(payload):
                ticks[sid].append(t)
                fixed[sid].append(rows.tobytes())
                if values is not None:
                    if sid not in managed:
                        managed[sid] = [[] for _ in values]
                    for f, v in enumerate(values):
                        managed[sid][f].extend(v)
        self._rows = {sid: (np.concatenate(ticks[sid]), b"".join(fixed[sid]), managed.get(sid)) for sid in ticks}
        return self._rows

    def _decode_chunk(self, payload):
        r = Reader(payload)
        first = r.varuint()
        r.varuint()  # tick count
        decoded = {}
        for _ in range(r.varuint()):
            start = r.pos
            sid = r.varuint()
            stream = self.streams[sid]
            type_ = self.types[stream["type"]]
            fill = bool(stream.get("fill"))
            k = r.varuint()
            t = first
            stored = np.empty(k, np.int64)
            for i in range(k):
                t += r.varuint()
                stored[i] = t
            span = r.varuint() if fill else 0
            size = type_["rowSize"]
            planes = np.frombuffer(r.take(k * size), dtype=np.uint8).reshape(size, k) if size else np.zeros((0, k), np.uint8)
            rows = decode_columns(np.ascontiguousarray(planes.T), type_)
            values = None
            if type_.get("managed"):
                values = []
                for _ in type_["managed"]:
                    vals, last = [], None
                    for _ in range(k):
                        ln = r.varuint()
                        if ln:
                            last = decode_tagged(Reader(r.take(ln - 1)), self.types)
                        vals.append(last)
                    values.append(vals)
            crc = int.from_bytes(r.take(4), "little") if fill else None
            if fill and k:
                dense = np.arange(stored[0], stored[0] + span + 1, dtype=np.int64)
                idx = np.searchsorted(stored, dense, side="right") - 1
                rows = rows[idx]
                if values is not None:
                    values = [[v[i] for i in idx] for v in values]
                stored = dense
            decoded[sid] = [stored, rows, values, crc]
            self.stream_bytes[sid] += r.pos - start
            self.block_spans.append((sid, payload, start, r.pos))

        # Undo the XOR against base streams (duplicate poses), then prove every per-tick block exact.
        for sid, item in decoded.items():
            base = self.streams[sid].get("base")
            if base and base["stream"] in decoded:
                own_t, own_rows = item[0], item[1]
                base_t, base_rows = decoded[base["stream"]][0], decoded[base["stream"]][1]
                j = np.searchsorted(base_t, own_t, side="right") - 1
                ok = j >= 0
                own_type = self.types[self.streams[sid]["type"]]
                base_type = self.types[self.streams[base["stream"]]["type"]]
                own_cols = {c["name"]: c["offset"] for c in own_type["columns"]}
                base_cols = {c["name"]: c["offset"] for c in base_type["columns"]}
                own_rows = own_rows.copy()
                for a, b in base["columns"].items():
                    oa, ob = own_cols[a], base_cols[b]
                    own_rows[ok, oa:oa + 4] ^= base_rows[j[ok], ob:ob + 4]
                item[1] = own_rows
        for sid, (t, rows, values, crc) in decoded.items():
            if crc is not None:
                if zlib.crc32(rows.tobytes()) != crc:
                    raise ValueError(f"stream {sid} ({self.streams[sid]['name']}): checksum mismatch in chunk at tick {first}")
                self.verified_blocks += 1
            yield sid, t, rows, values

    def table(self, stream_id):
        """Structured numpy array of a stream's fixed columns, plus ticks and managed values."""
        ticks, data, managed = self.rows().get(stream_id, (np.zeros(0, np.uint64), b"", None))
        dt = self.dtype(self.streams[stream_id]["type"])
        arr = np.frombuffer(data, dtype=dt) if dt.itemsize else np.zeros(len(ticks), dt)
        return ticks, arr, managed


WIDE = {"f32": 4, "i32": 4, "u32": 4, "f64": 8, "i64": 8, "u64": 8}


def decode_columns(coded, type_):
    """Inverse of the encoder's column coding: zig-zag second differences for 4/8-byte numerics, XOR elsewhere."""
    rows = coded.copy()
    n = len(rows)
    if n == 0:
        return rows
    numeric = np.zeros(type_["rowSize"], bool)
    for c in type_["columns"]:
        w = WIDE.get(c["type"])
        if not w:
            continue
        numeric[c["offset"]:c["offset"] + w] = True
        dt = np.uint32 if w == 4 else np.uint64
        z = np.ascontiguousarray(coded[:, c["offset"]:c["offset"] + w]).view(dt).ravel()
        dd = (z >> dt(1)) ^ (dt(0) - (z & dt(1)))
        with np.errstate(over="ignore"):
            v = np.cumsum(np.cumsum(dd, dtype=dt), dtype=dt)
        rows[:, c["offset"]:c["offset"] + w] = v.view(np.uint8).reshape(n, w)
    other = ~numeric
    if other.any():
        rows[:, other] = np.bitwise_xor.accumulate(coded[:, other], axis=0)
    return rows


def decode_tagged(r, types):
    tag = r.byte()
    if tag == 0:
        return None
    if tag in (1, 2):
        return tag == 2
    if tag == 3:
        return r.varint()
    if tag == 4:
        return r.varuint()
    if tag == 5:
        return struct.unpack("<f", r.take(4))[0]
    if tag == 6:
        return struct.unpack("<d", r.take(8))[0]
    if tag == 7:
        return r.string()
    if tag == 8:
        return [decode_tagged(r, types) for _ in range(r.varuint())]
    if tag == 9:
        t = types[r.varuint()]
        obj = {"$type": t["name"]}
        for name in t["fields"]:
            obj[name] = decode_tagged(r, types)
        return obj
    if tag == 10:
        return {"$unity": r.string(), "id": r.varint()}
    if tag == 11:
        return {str(decode_tagged(r, types)): decode_tagged(r, types) for _ in range(r.varuint())}
    if tag == 12:
        return {"$omitted": True}
    if tag == 13:
        return r.take(r.varuint())
    raise ValueError(f"unknown tag {tag}")


# ---- raw input ---------------------------------------------------------------------------------------------

# Key numbers of the "input" stream: bit n of keys0 (0-63) / keys1 (64-127).
INPUT_KEYS = (["None", "Space", "Enter", "Tab", "Backquote", "Quote", "Semicolon", "Comma", "Period", "Slash",
               "Backslash", "LeftBracket", "RightBracket", "Minus", "Equals"]
              + [chr(c) for c in range(ord("A"), ord("Z") + 1)]
              + [f"Digit{d}" for d in (1, 2, 3, 4, 5, 6, 7, 8, 9, 0)]
              + ["LeftShift", "RightShift", "LeftAlt", "RightAlt", "LeftCtrl", "RightCtrl", "LeftMeta", "RightMeta",
                 "ContextMenu", "Escape", "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "Backspace", "PageDown",
                 "PageUp", "Home", "End", "Insert", "Delete", "CapsLock", "NumLock", "PrintScreen", "ScrollLock",
                 "Pause", "NumpadEnter", "NumpadDivide", "NumpadMultiply", "NumpadPlus", "NumpadMinus",
                 "NumpadPeriod", "NumpadEquals"]
              + [f"Numpad{d}" for d in range(10)]
              + [f"F{n}" for n in range(1, 13)]
              + ["OEM1", "OEM2", "OEM3", "OEM4", "OEM5", "IMESelected"]
              + [f"F{n}" for n in range(13, 25)]
              + ["MediaPlayPause", "MediaRewind", "MediaForward"])
INPUT_FLAGS = ["typing", "keyboard", "pointer", "gamepad", "focused", "inputSystem", "legacyInput"]
MOUSE_BUTTONS = ["left", "right", "middle", "back", "forward"]
GAMEPAD_BUTTONS = ["dpadUp", "dpadDown", "dpadLeft", "dpadRight", "north", "east", "south", "west", "leftStick",
                   "rightStick", "leftShoulder", "rightShoulder", "start", "select", "leftTrigger", "rightTrigger"]


def _bits(value, names):
    value = int(value)
    return [names[i] if i < len(names) else f"#{i}" for i in range(value.bit_length()) if value >> i & 1]


def decode_input(rec, stream_id):
    """The rows of an "input" stream as dicts: clocks, pointer, scroll, sticks, and keys/buttons/flags by name."""
    ticks, arr, _ = rec.table(stream_id)
    out = []
    for i in range(len(ticks)):
        r = arr[i]
        keys = int(r["sample.keys0"]) | int(r["sample.keys1"]) << 64
        out.append({
            "tick": int(ticks[i]), "clientTick": int(r["clientTick"]), "viewTick": float(r["viewTick"]),
            "clientTime": float(r["clientTime"]),
            "flags": _bits(r["sample.flags"], INPUT_FLAGS),
            "keys": _bits(keys, INPUT_KEYS),
            "mouseButtons": _bits(r["sample.mouseButtons"], MOUSE_BUTTONS),
            "mouseDelta": [float(r["sample.mouseDelta.x"]), float(r["sample.mouseDelta.y"])],
            "pointer": [float(r["sample.pointer.x"]), float(r["sample.pointer.y"])],
            "scroll": [float(r["sample.scroll.x"]), float(r["sample.scroll.y"])],
            "leftStick": [float(r["sample.leftStick.x"]), float(r["sample.leftStick.y"])],
            "rightStick": [float(r["sample.rightStick.x"]), float(r["sample.rightStick.y"])],
            "leftTrigger": float(r["sample.leftTrigger"]), "rightTrigger": float(r["sample.rightTrigger"]),
            "gamepadButtons": _bits(r["sample.gamepadButtons"], GAMEPAD_BUTTONS),
            "joystickButtons": _bits(r["sample.joystickButtons"], []),
        })
    return out


# ---- UI stream (FORMAT.md, "UI stream") -------------------------------------------------------------------

def ui_streams(rec):
    """{player id: stream id} of the recorded UI streams."""
    return {s.get("player"): sid for sid, s in rec.streams.items()
            if s["kind"] == "custom" and s.get("sample") == "ui"}


def ui_rows(rec, stream_id, chunk=None):
    """The rows of a UI stream in file order, as dicts: enums by name, flags as a set of names, colours and
    vectors as lists, plus `tick` and the writer's clocks. `chunk` (an index into rec.chunks) reads that chunk alone."""
    type_ = rec.types[rec.streams[stream_id]["type"]]
    if chunk is None:
        ticks, arr, managed = rec.table(stream_id)
    else:
        ticks, arr, managed = np.zeros(0, np.int64), np.zeros(0, rec.dtype(type_["id"])), None
        if not hasattr(rec, "stream_bytes"):
            rec.rows()
        for sid, t, fixed, values in rec._decode_chunk(rec.chunks[chunk][1]):
            if sid == stream_id:
                ticks, arr, managed = t, np.frombuffer(fixed.tobytes(), dtype=rec.dtype(type_["id"])), values
    columns, groups, enums = [], defaultdict(list), {}
    for c in type_["columns"]:
        key = c["name"][7:] if c["name"].startswith("sample.") else c["name"]
        if "values" in c:
            enums[key] = {int(k): v for k, v in c["values"].items()}
        if "." in key:
            groups[key.split(".")[0]].append(c["name"])
        else:
            columns.append((key, c["name"]))
    data = {c["name"]: arr[c["name"]].tolist() for c in type_["columns"]}
    for i in range(len(ticks)):
        row = {"tick": int(ticks[i])}
        for key, name in columns:
            v = data[name][i]
            if key == "flags":
                v = {n for bit, n in enums.get(key, {}).items() if v & bit}
            elif key in enums:
                v = enums[key].get(v, v)
            row[key] = v
        for key, names in groups.items():
            row[key] = [data[n][i] for n in names]
        for f, m in enumerate(type_.get("managed", [])):
            row[m["name"][7:] if m["name"].startswith("sample.") else m["name"]] = managed[f][i]
        yield row


class UITree:
    """The UI at one moment: `screen` (width, height in pixels) and `elements` {id: latest row}."""

    def __init__(self):
        self.screen = None
        self.elements = {}

    def apply(self, row):
        if row["op"] == "Screen":
            self.screen = (row["width"], row["height"])
            if "Snapshot" in row["flags"]:
                self.elements.clear()
        elif row["op"] == "Remove":
            self.elements.pop(row["id"], None)
        else:
            self.elements[row["id"]] = row

    def children(self, parent=0):
        """Elements under `parent` (0: the roots) in paint order."""
        return sorted((e for e in self.elements.values() if e["parent"] == parent), key=lambda e: (e["order"], e["id"]))

    def find(self, name):
        return [e for e in self.elements.values() if e["name"] == name]


def ui_at(rec, tick, stream_id=None):
    """The UI tree at the end of `tick` (None: the end of the recording)."""
    if stream_id is None:
        streams = ui_streams(rec)
        if not streams:
            return UITree()
        stream_id = streams[min(streams, key=lambda p: (p is None, p))]
    tree = UITree()
    for row in ui_rows(rec, stream_id):
        if tick is not None and row["tick"] > tick:
            break
        tree.apply(row)
    return tree


def ui(path, tick=None, player=None, parts=()):
    rec = load(path, parts)
    streams = ui_streams(rec)
    if not streams:
        print("no ui stream")
        return
    sid = streams[player] if player is not None else None
    tree = ui_at(rec, tick, sid)
    print(f"screen {tree.screen}, {len(tree.elements)} elements")

    def show(element, depth):
        flags = ",".join(sorted(element["flags"]))
        extra = ""
        if element["text"] is not None:
            extra += f" text={element['text']!r}"
        if element["sprite"]:
            extra += f" sprite={element['sprite']!r}"
        if element["widget"] != "None":
            extra += f" {element['widget']}={element['value']:.3g}"
        if element["fill"] != "None":
            extra += f" fill={element['fillAmount']:.3g}"
        print(f"{'  ' * depth}#{element['id']} {element['kind']} {element['name']!r} <{element['type']}> "
              f"[{element['x']:.3f} {element['y']:.3f} {element['width']:.3f} {element['height']:.3f}]"
              f"{extra} ({flags})")
        for child in tree.children(element["id"]):
            show(child, depth + 1)

    for root in tree.children(0):
        show(root, 0)


# ---- commands ----------------------------------------------------------------------------------------------

def load(path, parts=()):
    rec = Recording(path)
    for p in parts:
        rec.merge(p)
    return rec


def summary(path, parts=()):
    rec = load(path, parts)
    h = rec.header or {}
    rate = h.get("tickRate", 0) or 1
    rows = rec.rows()
    ticks_all = [t for t, _, _ in rows.values() if len(t)]
    first = min(int(t.min()) for t in ticks_all) if ticks_all else 0
    last = max(int(t.max()) for t in ticks_all) if ticks_all else 0
    seconds = (last - first + 1) / rate
    print(f"{rec.path.name}: {rec.file_size / 1024:.1f} KiB, game={h.get('game')} scene={h.get('scene')} "
          f"tickRate={rate} ticks={last - first + 1} ({seconds:.1f}s){'' if rec.complete else ' INCOMPLETE (cut off: no FOOTER)'}")
    if rec.is_part:
        print(f"  client part of recording {h.get('recordingId')}, player {h.get('player')}")
    elif h.get("recordingId"):
        offered = sorted({p["player"] for p in rec.parts})
        print(f"  recording {h['recordingId']}; client parts offered to players {offered}; "
              f"merged: {[m['file'] for m in rec.merged] or 'none'}")
    print(f"  entities={len(rec.entities)} streams={len(rec.streams)} types={len(rec.types)} "
          f"players={len({p['player'] for p in rec.players})} colliderSets={len(rec.colliders)} meshes={len(rec.meshes)} "
          f"chunks={len(rec.chunks)}")
    if seconds > 0:
        print(f"  {rec.file_size / seconds / 1024:.2f} KiB/s on disk -> {rec.file_size / seconds * 3600 / 2**20:.1f} MiB per match-hour")
    print("  bytes on disk by record kind:")
    for k, v in sorted(rec.record_bytes.items(), key=lambda kv: -kv[1]):
        print(f"    {k:10s} {v / 1024:10.1f} KiB  ({rec.record_counts[k]} records)")

    # Uncompressed contribution per row type, to see what dominates before compression.
    by_type = defaultdict(lambda: [0, 0, 0])
    for sid, s in rec.streams.items():
        t = rec.types[s["type"]]
        n = len(rows.get(sid, ([],))[0])
        key = (s["kind"], t["name"])
        by_type[key][0] += n
        by_type[key][1] += rec.stream_bytes.get(sid, 0)
        by_type[key][2] += 1
    total = sum(v[1] for v in by_type.values()) or 1
    print("  chunk payload (before deflate) by stream kind/type:")
    for (kind, name), (n, b, count) in sorted(by_type.items(), key=lambda kv: -kv[1][1])[:25]:
        print(f"    {b / 1024:9.1f} KiB {100 * b / total:5.1f}%  {n:8d} rows {count:5d} streams  {kind:9s} {name}")
    raw_chunks = sum(len(p) for _, p in rec.chunks)
    stored_chunks = sum(s for s, _ in rec.chunks)
    if raw_chunks:
        print(f"  chunks: {raw_chunks / 1024:.1f} KiB raw -> {stored_chunks / 1024:.1f} KiB deflated ({raw_chunks / max(stored_chunks, 1):.1f}x)")
    print(f"  lossless check: {rec.verified_blocks} per-tick blocks decoded and matched their CRC-32")


def export(path, out_dir, parts=()):
    rec = load(path, parts)
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    try:
        import pyarrow as pa
        import pyarrow.parquet as pq
    except ImportError:
        pa = None

    groups = defaultdict(list)
    for sid, s in rec.streams.items():
        groups[(s["kind"], s["type"])].append(sid)

    meta = {"header": rec.header, "footer": rec.footer, "types": rec.types, "streams": rec.streams,
            "entities": rec.entities, "despawns": rec.despawns, "players": rec.players, "parts": rec.merged,
            "tables": {}}
    for (kind, type_id), sids in groups.items():
        type_ = rec.types[type_id]
        columns = defaultdict(list)
        for sid in sids:
            ticks, arr, managed = rec.table(sid)
            if not len(ticks):
                continue
            s = rec.streams[sid]
            columns["tick"].append(ticks)
            columns["stream"].append(np.full(len(ticks), sid, np.int32))
            columns["entity"].append(np.full(len(ticks), s.get("entity", -1) or -1, np.int32))
            columns["player"].append(np.full(len(ticks), s.get("player", -1) if s.get("player") is not None else -1, np.int64))
            for name in arr.dtype.names or ():
                columns[name].append(arr[name])
            for i, m in enumerate(type_.get("managed", [])):
                columns[m["name"]].append(np.array([json.dumps(v, default=str) for v in managed[i]], dtype=object))
        if not columns:
            continue
        table = {k: np.concatenate(v) for k, v in columns.items()}
        short = type_["name"].split(".")[-1].replace("+", "_").replace("`", "_").replace("[", "_").replace("]", "").replace(",", "_")
        stem = f"{kind}__{short}__t{type_id}"
        if pa is not None:
            pq.write_table(pa.table({k: v.tolist() if v.dtype == object else v for k, v in table.items()}), out / f"{stem}.parquet")
            meta["tables"][stem] = f"{stem}.parquet"
        else:
            np.savez_compressed(out / f"{stem}.npz", **{k: (v.astype(str) if v.dtype == object else v) for k, v in table.items()})
            meta["tables"][stem] = f"{stem}.npz"
        print(f"{stem}: {len(table['tick'])} rows, {len(table)} columns")
    (out / "recording.json").write_text(json.dumps(meta, indent=1, default=str))
    print(f"wrote {out}")


def dump(path, stream_id, count=5, parts=()):
    rec = load(path, parts)
    ticks, arr, managed = rec.table(stream_id)
    s = rec.streams[stream_id]
    print(json.dumps(s))
    names = rec.types[s["type"]].get("managed", [])
    for i in range(min(count, len(ticks))):
        row = {n: arr[n][i].item() for n in (arr.dtype.names or ())}
        for f, m in enumerate(names):
            row[m["name"]] = managed[f][i]
        print(int(ticks[i]), json.dumps(row, default=str))


def show_input(path, count=20, parts=()):
    """Prints the first `count` rows of every "input" stream in which a device did something."""
    rec = load(path, parts)
    sids = rec.custom_streams("input")
    if not sids:
        print("no input stream (clients write it to their own part: add --parts)")
    for sid in sids:
        rows = decode_input(rec, sid)
        typing = sum("typing" in r["flags"] for r in rows)
        print(f"{json.dumps(rec.streams[sid])}\n  {len(rows)} rows, {typing} while typing")
        shown = 0
        for r in rows:
            busy = (r["keys"] or r["mouseButtons"] or r["gamepadButtons"] or r["joystickButtons"]
                    or "typing" in r["flags"]
                    or any(r["mouseDelta"] + r["scroll"] + r["leftStick"] + r["rightStick"])
                    or r["leftTrigger"] or r["rightTrigger"])
            if busy and shown < count:
                print(" ", json.dumps(r))
                shown += 1


if __name__ == "__main__":
    argv = sys.argv[1:]
    parts = []
    if "--parts" in argv:
        at = argv.index("--parts")
        argv, parts = argv[:at], argv[at + 1:]
    if len(argv) < 2:
        print(__doc__)
        sys.exit(1)
    cmd = argv[0]
    if cmd == "summary":
        summary(argv[1], parts)
    elif cmd == "export":
        export(argv[1], argv[2], parts)
    elif cmd == "dump":
        dump(argv[1], int(argv[2]), int(argv[3]) if len(argv) > 3 else 5, parts)
    elif cmd == "input":
        show_input(argv[1], int(argv[2]) if len(argv) > 2 else 20, parts)
    elif cmd == "ui":
        ui(argv[1], int(argv[2]) if len(argv) > 2 else None, int(argv[3]) if len(argv) > 3 else None, parts)
    else:
        print(__doc__)
        sys.exit(1)

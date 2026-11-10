#!/usr/bin/env python3
"""Reference reader for PurrNet match recordings (.purrrec). See FORMAT.md.

    python purrrec.py summary  match.purrrec
    python purrrec.py export   match.purrrec out_dir      # one table per row type (.parquet if pyarrow, else .npz)
    python purrrec.py dump     match.purrrec STREAM_ID [N]

Only numpy is required.
"""
import json
import struct
import sys
import zlib
from collections import defaultdict
from pathlib import Path

import numpy as np

KINDS = {1: "HEADER", 2: "TYPE", 3: "STREAM", 4: "ENTITY", 5: "DESPAWN", 6: "CHUNK",
         7: "COLLIDERS", 8: "PLAYER", 9: "FOOTER", 10: "MESH", 11: "BATCH", 12: "ATTACH", 13: "DETACH"}

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


# ---- commands ----------------------------------------------------------------------------------------------

def summary(path):
    rec = Recording(path)
    h = rec.header or {}
    rate = h.get("tickRate", 0) or 1
    rows = rec.rows()
    ticks_all = [t for t, _, _ in rows.values() if len(t)]
    first = min(int(t.min()) for t in ticks_all) if ticks_all else 0
    last = max(int(t.max()) for t in ticks_all) if ticks_all else 0
    seconds = (last - first + 1) / rate
    print(f"{rec.path.name}: {rec.file_size / 1024:.1f} KiB, game={h.get('game')} scene={h.get('scene')} "
          f"tickRate={rate} ticks={last - first + 1} ({seconds:.1f}s){' TRUNCATED' if rec.truncated else ''}")
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


def export(path, out_dir):
    rec = Recording(path)
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
            "entities": rec.entities, "despawns": rec.despawns, "players": rec.players, "tables": {}}
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


def dump(path, stream_id, count=5):
    rec = Recording(path)
    ticks, arr, managed = rec.table(stream_id)
    s = rec.streams[stream_id]
    print(json.dumps(s))
    names = rec.types[s["type"]].get("managed", [])
    for i in range(min(count, len(ticks))):
        row = {n: arr[n][i].item() for n in (arr.dtype.names or ())}
        for f, m in enumerate(names):
            row[m["name"]] = managed[f][i]
        print(int(ticks[i]), json.dumps(row, default=str))


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    cmd = sys.argv[1]
    if cmd == "summary":
        summary(sys.argv[2])
    elif cmd == "export":
        export(sys.argv[2], sys.argv[3])
    elif cmd == "dump":
        dump(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]) if len(sys.argv) > 4 else 5)
    else:
        print(__doc__)
        sys.exit(1)

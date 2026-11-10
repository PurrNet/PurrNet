// Reader for PurrNet .purrrec recordings (see ../FORMAT.md). Browser and Node 18+.

const KINDS = { 1: "HEADER", 2: "TYPE", 3: "STREAM", 4: "ENTITY", 5: "DESPAWN", 6: "CHUNK", 7: "COLLIDERS", 8: "PLAYER", 9: "FOOTER", 10: "MESH", 11: "BATCH", 12: "ATTACH", 13: "DETACH" };

class Reader {
  constructor(bytes, pos = 0) { this.b = bytes; this.pos = pos; this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength); }
  byte() { return this.b[this.pos++]; }
  varuint() {
    let result = 0, mul = 1, b;
    do { b = this.b[this.pos++]; result += (b & 0x7f) * mul; mul *= 128; } while (b >= 0x80);
    return result; // exact up to 2^53, plenty for ticks and ids
  }
  varint() { const v = this.varuint(); return v % 2 ? -(v + 1) / 2 : v / 2; }
  take(n) { const out = this.b.subarray(this.pos, this.pos + n); this.pos += n; return out; }
  string() { return new TextDecoder().decode(this.take(this.varuint())); }
}

async function inflateRaw(bytes) {
  if (typeof DecompressionStream !== "undefined") {
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }
  const zlib = await import("node:zlib");
  return new Uint8Array(zlib.inflateRawSync(bytes));
}

const SCALAR = {
  bool: [1, (v, o) => v.getUint8(o) !== 0], u8: [1, (v, o) => v.getUint8(o)], i8: [1, (v, o) => v.getInt8(o)],
  u16: [2, (v, o) => v.getUint16(o, true)], i16: [2, (v, o) => v.getInt16(o, true)], char: [2, (v, o) => v.getUint16(o, true)],
  u32: [4, (v, o) => v.getUint32(o, true)], i32: [4, (v, o) => v.getInt32(o, true)],
  u64: [8, (v, o) => Number(v.getBigUint64(o, true))], i64: [8, (v, o) => Number(v.getBigInt64(o, true))],
  f32: [4, (v, o) => v.getFloat32(o, true)], f64: [8, (v, o) => v.getFloat64(o, true)],
};

export function decodeTagged(r, types) {
  const tag = r.byte();
  switch (tag) {
    case 0: return null;
    case 1: return false;
    case 2: return true;
    case 3: return r.varint();
    case 4: return r.varuint();
    case 5: { const v = r.view.getFloat32(r.pos, true); r.pos += 4; return v; }
    case 6: { const v = r.view.getFloat64(r.pos, true); r.pos += 8; return v; }
    case 7: return r.string();
    case 8: { const n = r.varuint(); const out = []; for (let i = 0; i < n; i++) out.push(decodeTagged(r, types)); return out; }
    case 9: { const t = types.get(r.varuint()); const o = { $type: t.name }; for (const f of t.fields) o[f] = decodeTagged(r, types); return o; }
    case 10: return { $unity: r.string(), id: r.varint() };
    case 11: { const n = r.varuint(); const o = {}; for (let i = 0; i < n; i++) { const k = decodeTagged(r, types); o[String(k)] = decodeTagged(r, types); } return o; }
    case 12: return { $omitted: true };
    case 13: return r.take(r.varuint());
    default: throw new Error("unknown tag " + tag);
  }
}

/**
 * Stream rows for the whole file: ticks (Float64Array), rows (Uint8Array, rowSize each, un-delta'd),
 * managed (array per managed field of raw tagged bytes per row, null = same as previous).
 */
class StreamData {
  constructor(stream, type) { this.stream = stream; this.type = type; this.tickParts = []; this.rowParts = []; this.managedParts = []; }
  finish() {
    const n = this.tickParts.reduce((a, p) => a + p.length, 0);
    this.ticks = new Float64Array(n);
    this.rows = new Uint8Array(n * this.type.rowSize);
    let at = 0;
    for (let i = 0; i < this.tickParts.length; i++) {
      this.ticks.set(this.tickParts[i], at);
      this.rows.set(this.rowParts[i], at * this.type.rowSize);
      at += this.tickParts[i].length;
    }
    const fields = (this.type.managed || []).length;
    this.managed = [];
    for (let f = 0; f < fields; f++) this.managed.push(this.managedParts.flatMap(p => p[f]));
    this.view = new DataView(this.rows.buffer);
    this.tickParts = this.rowParts = this.managedParts = null;
  }
  get length() { return this.ticks.length; }
  /** Index of the last row with tick <= t, or -1. */
  indexAt(t) {
    let lo = 0, hi = this.ticks.length - 1, best = -1;
    while (lo <= hi) { const mid = (lo + hi) >> 1; if (this.ticks[mid] <= t) { best = mid; lo = mid + 1; } else hi = mid - 1; }
    return best;
  }
  value(i, column) { const c = this.type.columnsByName[column]; return SCALAR[c.type][1](this.view, i * this.type.rowSize + c.offset); }
  row(i, types) {
    const o = {};
    for (const c of this.type.columns) {
      let v = SCALAR[c.type][1](this.view, i * this.type.rowSize + c.offset);
      if (c.values && c.values[String(v)] !== undefined) v = c.values[String(v)];
      o[c.name] = v;
    }
    (this.type.managed || []).forEach((m, f) => {
      let j = i; while (j > 0 && this.managed[f][j] === null) j--;
      const raw = this.managed[f][j];
      o[m.name] = raw ? decodeTagged(new Reader(raw), types) : null;
    });
    return o;
  }
}

const WIDE = { f32: 4, i32: 4, u32: 4, f64: 8, i64: 8, u64: 8 };
const MASK64 = (1n << 64n) - 1n;

// Inverse of the encoder's column coding: zig-zag second differences for 4/8-byte numerics, XOR elsewhere.
function decodeColumns(coded, n, type) {
  const size = type.rowSize, rows = coded.slice(), numeric = new Uint8Array(size);
  const view = new DataView(rows.buffer), cview = new DataView(coded.buffer);
  for (const c of type.columns) {
    const w = WIDE[c.type];
    if (!w) continue;
    numeric.fill(1, c.offset, c.offset + w);
    if (w === 4) {
      let prev = 0, delta = 0;
      for (let r = 0; r < n; r++) {
        const z = cview.getUint32(r * size + c.offset, true);
        const dd = ((z >>> 1) ^ -(z & 1)) >>> 0;
        delta = (delta + dd) >>> 0; prev = (prev + delta) >>> 0;
        view.setUint32(r * size + c.offset, prev, true);
      }
    } else {
      let prev = 0n, delta = 0n;
      for (let r = 0; r < n; r++) {
        const z = cview.getBigUint64(r * size + c.offset, true);
        const dd = (z >> 1n) ^ ((-(z & 1n)) & MASK64);
        delta = (delta + dd) & MASK64; prev = (prev + delta) & MASK64;
        view.setBigUint64(r * size + c.offset, prev, true);
      }
    }
  }
  for (let b = 0; b < size; b++) {
    if (numeric[b]) continue;
    let acc = 0;
    for (let r = 0; r < n; r++) { acc ^= coded[r * size + b]; rows[r * size + b] = acc; }
  }
  return rows;
}

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let i = 0; i < 256; i++) { let c = i; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; t[i] = c >>> 0; }
  return t;
})();

function crc32(bytes) {
  let c = 0xFFFFFFFF;
  for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xFF] ^ (c >>> 8);
  return (c ^ 0xFFFFFFFF) >>> 0;
}

export async function readRecording(buffer) {
  const bytes = new Uint8Array(buffer);
  const magic = new TextDecoder().decode(bytes.subarray(0, 7));
  if (magic !== "PURRREC") throw new Error("not a .purrrec file");
  const version = bytes[8] | (bytes[9] << 8);
  if (version !== 1) throw new Error("unsupported version " + version);

  const rec = { header: null, footer: null, types: new Map(), streams: new Map(), entities: new Map(), despawns: new Map(),
    players: [], colliders: [], meshes: new Map(), data: new Map(), sizes: {}, truncated: false };
  const chunks = [];
  // BATCH (11) carries a compressed run of metadata records with the same framing, minus the compression bit.
  function handle(kind, payload) {
    if (kind === 11) {
      const b = new Reader(payload);
      while (b.pos < payload.length) { const k = b.byte(), n = b.varuint(); handle(k, b.take(n)); }
      return;
    }
    if (kind === 6) { chunks.push(payload); return; }
    if (kind === 10) {
      const m = new Reader(payload);
      const id = m.varuint(), name = m.string();
      const nv = m.varuint();
      const vertices = new Float32Array(payload.slice(m.pos, m.pos + nv * 12).buffer); m.pos += nv * 12;
      const ni = m.varuint();
      const indices = new Uint32Array(payload.slice(m.pos, m.pos + ni * 4).buffer); m.pos += ni * 4;
      rec.meshes.set(id, { mesh: id, name, vertices, indices });
      return;
    }
    if (!KINDS[kind]) return;
    const obj = JSON.parse(new TextDecoder().decode(payload));
    switch (kind) {
      case 1: rec.header = obj; break;
      case 2:
        if (obj.kind === "row") obj.columnsByName = Object.fromEntries(obj.columns.map(c => [c.name, c]));
        rec.types.set(obj.id, obj); break;
      case 3: rec.streams.set(obj.id, obj); break;
      case 4: rec.entities.set(obj.entity, obj); break;
      case 5: rec.despawns.set(obj.entity, obj.tick); break;
      case 7: rec.colliders.push(obj); break;
      case 8: rec.players.push(obj); break;
      case 9: rec.footer = obj; break;
      case 12: (rec.attaches ??= []).push(obj); break;
      case 13: (rec.detaches ??= []).push(obj); break;
    }
  }

  const r = new Reader(bytes, 10);
  while (r.pos < bytes.length) {
    const start = r.pos;
    let kind, length;
    try { kind = r.byte(); length = r.varuint(); } catch { rec.truncated = true; break; }
    if (r.pos + length > bytes.length) { rec.truncated = true; break; }
    let payload = r.take(length);
    const compressed = (kind & 0x80) !== 0;
    kind &= 0x7f;
    const name = KINDS[kind] || "kind" + kind;
    rec.sizes[name] = (rec.sizes[name] || 0) + (r.pos - start);
    if (compressed) payload = await inflateRaw(payload);
    handle(kind, payload);
  }

  rec.verifiedBlocks = 0; rec.checksumErrors = 0;
  for (const payload of chunks) {
    const c = new Reader(payload);
    const first = c.varuint();
    c.varuint(); // tick count
    const blocks = c.varuint();
    const decoded = new Map();
    for (let b = 0; b < blocks; b++) {
      const sid = c.varuint();
      const stream = rec.streams.get(sid);
      const type = rec.types.get(stream.type);
      const fill = !!stream.fill;
      const k = c.varuint();
      let stored = new Float64Array(k);
      let t = first;
      for (let i = 0; i < k; i++) { t += c.varuint(); stored[i] = t; }
      const span = fill ? c.varuint() : 0;
      const size = type.rowSize;
      const planes = c.take(k * size);
      const coded = new Uint8Array(k * size);
      for (let bi = 0; bi < size; bi++)
        for (let ri = 0; ri < k; ri++) coded[ri * size + bi] = planes[bi * k + ri];
      let rows = decodeColumns(coded, k, type);
      let managed = [];
      for (let f = 0; f < (type.managed || []).length; f++) {
        const values = [];
        let last = null;
        for (let i = 0; i < k; i++) {
          const len = c.varuint();
          if (len !== 0) last = c.take(len - 1).slice();
          values.push(last);
        }
        managed.push(values);
      }
      let crc = null;
      if (fill) { crc = (payload[c.pos] | payload[c.pos + 1] << 8 | payload[c.pos + 2] << 16 | payload[c.pos + 3] << 24) >>> 0; c.pos += 4; }
      if (fill && k) {
        // Per-tick streams: a tick without a stored row repeats the previous one.
        const m = span + 1, dense = new Float64Array(m), denseRows = new Uint8Array(m * size);
        const denseManaged = managed.map(() => new Array(m));
        let j = 0;
        for (let i = 0; i < m; i++) {
          const tick = stored[0] + i;
          while (j + 1 < k && stored[j + 1] <= tick) j++;
          dense[i] = tick;
          denseRows.set(rows.subarray(j * size, j * size + size), i * size);
          for (let f = 0; f < managed.length; f++) denseManaged[f][i] = managed[f][j];
        }
        stored = dense; rows = denseRows; managed = denseManaged;
      }
      decoded.set(sid, { stream, type, ticks: stored, rows, managed, crc });
    }

    // Undo the XOR against base streams (duplicate poses), then check every per-tick block against its CRC-32.
    for (const item of decoded.values()) {
      const base = item.stream.base;
      if (!base || !decoded.has(base.stream)) continue;
      const b = decoded.get(base.stream);
      const size = item.type.rowSize, bsize = b.type.rowSize;
      const pairs = Object.entries(base.columns).map(([own, other]) => [item.type.columnsByName[own].offset, b.type.columnsByName[other].offset]);
      let j = -1;
      for (let i = 0; i < item.ticks.length; i++) {
        while (j + 1 < b.ticks.length && b.ticks[j + 1] <= item.ticks[i]) j++;
        if (j < 0) continue;
        for (const [oa, ob] of pairs)
          for (let x = 0; x < 4; x++) item.rows[i * size + oa + x] ^= b.rows[j * bsize + ob + x];
      }
    }
    for (const [sid, item] of decoded) {
      if (item.crc !== null) {
        if (crc32(item.rows) !== item.crc) { rec.checksumErrors++; console.warn("checksum mismatch", item.stream.name, "chunk", first); }
        else rec.verifiedBlocks++;
      }
      let d = rec.data.get(sid);
      if (!d) { d = new StreamData(item.stream, item.type); rec.data.set(sid, d); }
      d.tickParts.push(item.ticks); d.rowParts.push(item.rows); d.managedParts.push(item.managed);
    }
  }
  for (const d of rec.data.values()) d.finish();

  let first = Infinity, last = -Infinity;
  for (const d of rec.data.values()) if (d.length) { first = Math.min(first, d.ticks[0]); last = Math.max(last, d.ticks[d.length - 1]); }
  rec.firstTick = first; rec.lastTick = last;
  rec.tickRate = rec.header?.tickRate || 30;
  return rec;
}

export { Reader };

# PurrNet match recording format (`.purrrec`), version 1 (draft)

A recording is everything the server (or host) knew about a match, tick by tick: each object's world transform,
collider shapes, the streams its recorder adds (with PurrDiction: the state of every predicted component and the
input every player applied), and any extra streams the game or its clients add (for example each client's real
camera). It is meant to be read outside
the game: every file carries the names, types and memory layout of everything inside it, so a reader needs
this document and nothing else.

Status: prototype. Version 0 may change without notice; readers must reject versions they do not know.

## Timeline

Everything is keyed by **server tick** (`u64`). The header gives `tickRate`, so `seconds = (tick - firstTick) / tickRate`.

PurrDiction semantics: the row for tick `T` in a `state` stream is the state *before* tick `T` was simulated,
and the row for tick `T` in an `input` stream is the input that was applied *during* tick `T`. So
`(state[T], input[T]) -> state[T+1]`, which is the (observation, action) pair a model learns from.

A file has one clock. While a PredictionManager is spawned it is PurrDiction's prediction tick, and everything else
(network identities' `transform` and `sync` rows, colliders) is sampled at the same moment, before the tick is
simulated. Without one it is PurrNet's network tick, sampled after the tick. When the clock would change (a
PredictionManager spawns or despawns mid-match), the recorder ends the file and starts a new one.

Data that originates on a client (custom client streams such as the camera) is stamped with the server tick at
which the server received it, plus the client's own clocks inside the row (see "Client samples").

## Container

```
magic   "PURRREC\0"   8 bytes
version u16 LE        currently 1
records ...           until end of file
```

Each record:

```
kind    u8            low 7 bits = record kind, high bit (0x80) = payload is raw-deflate compressed
length  varuint       payload length in bytes, as stored (after compression)
payload bytes
```

`varuint` is unsigned LEB128. `varint` is zig-zag encoded then LEB128.

A file that was cut off (crash, killed process) is still valid up to the last complete record.

### Record kinds

| kind | name      | payload | meaning |
|------|-----------|---------|---------|
| 1    | HEADER    | JSON    | first record; match-level metadata |
| 2    | TYPE      | JSON    | describes a type used by later records |
| 3    | STREAM    | JSON    | declares a stream id, its type and what it belongs to |
| 4    | ENTITY    | JSON    | an object appeared |
| 5    | DESPAWN   | JSON    | an object disappeared |
| 6    | CHUNK     | binary  | rows for a range of ticks |
| 7    | COLLIDERS | JSON    | collider shapes for an entity, or for the static world |
| 8    | PLAYER    | JSON    | a player joined or left |
| 9    | FOOTER    | JSON    | last record of a cleanly closed file |
| 10   | MESH      | binary  | vertex/index data referenced by mesh colliders |
| 11   | BATCH     | binary  | a run of other records (usually compressed), see below |
| 12   | ATTACH    | JSON    | another source's components were added to an existing entity |
| 13   | DETACH    | JSON    | a source's components left an entity that other sources still hold |

A TYPE is written before the STREAM using it, and a STREAM before the CHUNK holding its rows. An entity's STREAM
records come right before its ENTITY record (they name the entity id the ENTITY record then introduces). Readers
should index the whole file before resolving references, and ignore unknown record kinds.

### BATCH

Small metadata records (TYPE, STREAM, ENTITY, DESPAWN, PLAYER, COLLIDERS, MESH) are collected and written as one
BATCH record, normally compressed, right before the CHUNK whose rows use them. Its payload is a sequence of records
with the normal framing (`kind u8`, `length varuint`, payload), never compressed individually. Readers handle the
inner records exactly as if they were top-level.

## JSON records

### HEADER

```json
{ "format": "purrrec", "version": 1, "game": "CRACKED", "gameVersion": "0.9.1",
  "unityVersion": "6000.3.8f1", "purrdictionVersion": "1.4.2-beta.1",
  "tickRate": 30, "firstTick": 1234, "startedUtc": "2026-10-28T15:01:02Z",
  "scene": "SampleScene-VBot", "recorder": "host", "serverBuild": false, "labels": { } }
```

`recorder` is `host` when the recording machine also plays and `server` when it does not. `serverBuild` says whether
it runs a dedicated server build (or a headless player, which counts as one); files written before it existed do not
have it. `labels` holds free-form key/value metadata the game adds (map, mode, build hash, ...).

### TYPE

Two kinds of type.

**Row types** describe fixed-size rows: the unmanaged part of a struct, flattened. Nested structs become dotted
column names; every column is a little-endian scalar at a byte offset within the row. Columns are packed with no
padding, in declaration order.

```json
{ "id": 3, "kind": "row", "name": "Purrguelike.PlayerMovement+State", "rowSize": 61,
  "columns": [
    { "name": "timeSinceJump", "type": "f32", "offset": 0 },
    { "name": "velocity.x",    "type": "f32", "offset": 12 },
    { "name": "isGrounded",    "type": "bool", "offset": 20 },
    { "name": "mode",          "type": "i32", "offset": 21, "enum": "Purrguelike.Mode", "values": { "0": "Idle", "1": "Run" } }
  ],
  "managed": [ { "name": "playerEffects" } ] }
```

Scalar types: `bool` (1 byte, 0/1), `u8 i8 u16 i16 u32 i32 u64 i64 f32 f64`, `char` (u16 UTF-16 unit).
`Nullable<T>` appears as `name.hasValue` and `name.value.*`.

`managed` lists fields that cannot be laid out as fixed bytes (classes, interfaces, strings, arrays, lists, and
struct collections such as `DisposableList<T>`). Each row carries one tagged value per managed field (see below).

**Object types** describe classes and structs that appear inside tagged values:

```json
{ "id": 9, "kind": "object", "name": "Purrguelike.HeatSeekingModifier", "fields": ["strength", "delayTicks"] }
```

### STREAM

```json
{ "id": 12, "name": "e5.c0.state", "kind": "state", "type": 3, "entity": 5, "component": 0,
  "componentType": "Purrguelike.PlayerMovement" }
```

Stream kinds:

| kind        | rows | owner |
|-------------|------|-------|
| `state`     | one per tick while the entity exists | `entity` + `component` |
| `input`     | one per tick while the entity exists | `entity` + `component` |
| `transform` | one per tick while the entity exists, type `RecordedTransform` | `entity` |
| `player`    | one per tick while the player is connected, type `RecordedPlayerTick` | `player` |
| `custom`    | whenever the writer adds a sample | optional `player` (who sent it) |
| `collider`  | one per tick while the collider moves within its entity, type `RecordedColliderPose` | `entity` (null: static) + `collider` |
| `sync`      | one per tick while the entity exists, type `RecordedValue<T>` (or `RecordedTimer`) | `entity` + `component` + `module` |

A `sync` stream is one of a network identity's sync types (PurrNet): `module` is its index in the component and
`field` its name. `RecordedValue<T>` has one column or managed field, `value`: a SyncVar's value, or a SyncList's
items as a list. `RecordedTimer` has `state` (enum) and `remaining` (f32, seconds). The values are the server's.

### ENTITY / DESPAWN

An entity is one GameObject that a recorder source tracks: one with predicted components (PurrDiction), with network
identities (PurrNet), or both (a spawned prefab, a piece of one, or a scene object). Entity ids are assigned by the
recorder and never reused within a file. The source that saw the object first writes its ENTITY record with its own
components; other sources add theirs with ATTACH records.

```json
{ "entity": 5, "tick": 1240, "objectId": 17, "name": "Player", "prefabId": 2, "prefabName": "Player",
  "owner": 1, "parent": null, "path": "Player",
  "components": [ { "index": 0, "type": "Purrguelike.PlayerMovement", "state": 12, "input": 13 },
                  { "index": 1, "type": "PurrNet.Prediction.PredictedTransform", "state": 14 } ],
  "transform": 15 }
```

`prefabId` is -1 for scene objects. `owner` is a player id or null. `parent` is the nearest entity above this one,
from any source.

Written by PurrNet's recorder, components are network identities (each NetworkBehaviour of the object) with their
`sync` streams, and `networkId` replaces `objectId`:

```json
{ "entity": 7, "tick": 310, "networkId": 12, "name": "Player(Clone)", "prefabId": 0, "prefabName": "Player",
  "owner": 1, "parent": null, "path": "Player(Clone)",
  "components": [ { "index": 0, "type": "Game.Health", "networkId": 12,
                    "syncs": [ { "module": 0, "field": "health", "stream": 31 } ] } ],
  "transform": 30 }
```

```json
{ "entity": 5, "tick": 1902 }
```

The entity existed for ticks `[ENTITY.tick, DESPAWN.tick)`.

### ATTACH / DETACH

```json
{ "entity": 5, "tick": 1240, "source": "purrnet", "components": [ ... ] }
{ "entity": 5, "tick": 1902, "source": "purrnet" }
```

ATTACH adds a source's components (same shape as in ENTITY) to an entity another source created; DETACH says that
source stopped tracking it while others still do. When the last source lets go, the entity gets a DESPAWN instead.

### PLAYER

```json
{ "player": 1, "tick": 1234, "event": "joined", "isBot": false, "isLocal": true, "stream": 40 }
{ "player": 1, "tick": 5000, "event": "left" }
```

`stream` is only there when the recorder writes a per-tick player stream (PurrDiction's does; PurrNet's does not).

`RecordedPlayerTick` row: `viewOffsetTicks` (f32): how many ticks behind the server tick this player's screen was
when they produced the input for that tick (0 for the host's own player); `hasViewOffset` (bool).
So what player `p` saw while acting on tick `T` is roughly the world at tick `T - viewOffsetTicks`.

### COLLIDERS

Shapes are in the entity's local space: world = entity transform (from its `transform` stream) x `pose` x shape.
`entity` is null for static world geometry, in which case poses are in world space.

A COLLIDERS record is the entity's complete set of enabled colliders from `tick` on, until the next COLLIDERS record
for the same entity (or for `entity: null`). The recorder re-checks sets every few ticks, so colliders that are
added, removed, toggled or resized after the entity spawned show up as new records. An empty `colliders` array means
the entity has no colliders from that tick.

Colliders on a dynamic (non-kinematic) rigidbody that no predicted object drives, such as debris, are simulated by
each machine on its own and are not part of the game's simulation; like other visual effects they are not recorded.

Each collider has an `id`, stable for the whole recording. A collider that moves within its entity (a drawbridge
tilting, a door swinging, pieces scaling in) does not cause new records: it gets a `collider` stream (`entity`,
`collider` = the id) of `RecordedColliderPose` rows (`position`, `rotation`, `scale` in the same space as `pose`).
From its first row on, the stream's latest row at or before a tick replaces that collider's `pose`; it is sampled
every tick while the collider moves and holds its last row while it is still.

```json
{ "entity": 5, "tick": 1240, "colliders": [
  { "shape": "box",     "id": 41672, "pose": { "p": [0,1,0], "r": [0,0,0,1], "s": [1,1,1] }, "center": [0,0,0], "size": [1,2,1], "trigger": false, "layer": 8 },
  { "shape": "sphere",  "pose": {...}, "center": [0,0,0], "radius": 0.5 },
  { "shape": "capsule", "pose": {...}, "center": [0,0,0], "radius": 0.5, "height": 2, "direction": 1 },
  { "shape": "mesh",    "pose": {...}, "mesh": 4, "convex": false },
  { "shape": "bounds",  "pose": {...}, "center": [0,0,0], "size": [4,1,4] } ] }
```

`bounds` stands in for a mesh whose data could not be read. Quaternions are `[x, y, z, w]`. Units and axes are Unity's
(left-handed, Y up, metres).

### MESH

Binary, usually compressed. Written once per distinct mesh.

```
mesh        varuint   id referenced by COLLIDERS
name        varuint length + UTF-8
vertexCount varuint
vertices    vertexCount x 3 x f32   (x, y, z in the mesh's local space)
indexCount  varuint
indices     indexCount x u32        (triangle list)
```

### FOOTER

```json
{ "lastTick": 9001, "entities": 812, "streams": 2411, "chunks": 31, "summary": { } }
```

Usually compressed. `summary` (PurrDiction's recorder, from 1.5.0-beta.3) is what the recorder collected while recording, so a
dashboard can describe the match without reading the file. Everything in it can also be derived from the records:

```json
{ "version": 1,
  "players": [ { "id": 1, "isBot": false, "connected": true, "isLocal": true, "firstTick": 8, "lastTick": 2658,
                 "spawns": 3, "viewOffsetAvgTicks": 0.17, "viewOffsetMaxTicks": 4 } ],
  "spawns": { "Bullet": 506, "Magician": 24 },
  "stateMachines": [ { "name": "GameStateManager",
                       "states": [ { "index": 1, "name": "Purrguelike.WarmupState", "ticks": 543, "entered": 3 } ] } ],
  "heatmap": { "axes": "xz", "min": [-40.06, -22.08], "cell": 1.5, "width": 48, "height": 28, "sampleSeconds": 1,
               "humans": [ 130, 2, 131, 5 ], "bots": [ ] } }
```

- `players`: every player that was connected or owned an entity (bots included). `firstTick`/`lastTick` span the
  ticks they were seen; `spawns` counts root entities they owned (respawns, rounds); view offsets are over their
  `player` stream rows that had one.
- `spawns`: entities spawned per prefab name.
- `stateMachines`: per `PredictedStateMachine`, ticks spent in each state and how often it was entered.
- `heatmap`: once a second, each player's avatar (their oldest live owned root entity) position, binned in square
  cells on the two axes with the most spread. Cells are flat `[index, count, ...]` pairs with
  `index = row * width + column`; humans and bots are counted apart.

## CHUNK

Compressed (kind `0x86`). After decompression:

```
firstTick   varuint
tickCount   varuint   ticks this chunk covers: firstTick .. firstTick + tickCount - 1
blockCount  varuint
blocks...
```

Each block holds the rows of one stream in this chunk:

```
stream      varuint
rowCount    varuint   stored rows
ticks       rowCount x varuint   first is (tick - firstTick), the rest are (tick - previous stored tick)
span        varuint   per-tick streams only: (last tick of the block - first stored tick)
fixed       rowCount x rowSize bytes, column-coded and byte-plane transposed (below)
managed     for each managed field of the row type, for each stored row: varuint n, then n-1 bytes of tagged value;
            n = 0 means "same value as the previous stored row of this field in this block"
crc         per-tick streams only: u32 LE, CRC-32 (IEEE, as zlib) of the block's raw rows after decoding,
            one row per tick from the first stored tick to first + span
```

Every block starts its coding from scratch, so a reader can start at any chunk. Nothing is quantised: decoding gives
back the exact bytes the recorder captured, which the CRC proves.

**Per-tick streams** (STREAM `"fill": true`: `state`, `input`, `transform`, `player`) have one row per tick while their
owner exists. Only rows that differ from the previous stored row are written; a tick with no stored row repeats the
previous one. Expand a block to one row per tick over `[first stored tick, first stored tick + span]`.

**Column coding.** Rows are stored as `rowCount x rowSize` bytes, then transposed byte-plane-wise:
`stored[b * rowCount + r] = coded[r][b]`. Within the coded rows:

- 4-byte numeric columns (`f32 i32 u32`) and 8-byte numeric columns (`f64 i64 u64`) hold the zig-zag encoded second
  difference of their bit patterns, read as unsigned integers of that width with wrap-around:
  `d[r] = v[r] - v[r-1]`, `dd[r] = d[r] - d[r-1]` (with `v[-1] = d[-1] = 0`), stored `(dd << 1) ^ (dd >> (w - 1))`
  (arithmetic shift). Decode with `dd = (z >> 1) ^ -(z & 1)` and two running sums.
- Every other byte (bools, small integers, chars, enums of those) holds `coded[r][b] = raw[r][b] ^ raw[r-1][b]`
  (`raw[-1] = 0`).

**Base streams.** A STREAM may name a `base`:

```json
"base": { "stream": 14, "columns": { "position.x": "unityPosition.x", "rotation.w": "unityRotation.w" } }
```

Each listed 4-byte column is stored XOR-ed with the named column of the base stream's row at the same tick (the base
stream's latest row at or before that tick, after expanding it; zero if it has none yet). The recorder uses this to
store an entity's transform against its `PredictedTransform` pose, which is usually identical. Decode the base stream
first, expand both, then XOR the columns back. The block CRC is over the fully restored rows.

### Tagged values (managed fields)

```
0  null
1  false            2  true
3  varint (i64)     4  varuint (u64)
5  f32 (4 bytes)    6  f64 (8 bytes)
7  string           varuint byte length + UTF-8
8  list             varuint count + count tagged values
9  object           varuint TYPE id ("object" kind) + one tagged value per field, in TYPE order
10 unity object     string name (tag-less: varuint length + UTF-8) + varint instance id
11 map              varuint count + count x (key, value) tagged values
12 omitted          cycle or depth limit reached
13 bytes            varuint length + raw bytes
```

## Client samples

A client can add rows to a custom stream (the camera, for instance). Its row type is
`ClientSample<T>` with columns `clientTick` (u64, the client's predicted tick), `viewTick` (f64, the server tick
the client was presenting, with fraction), `clientTime` (f64, seconds since the client's recorder started) and
`sample.*` (the game's struct). The chunk tick is when the server received it. The camera is the only stream
clients send; it is sampled once per tick.

## Streams the recorder writes by default

- `state` and `input` for every predicted component (modules' own state is not recorded yet).
- `transform` per entity: `position` (3 x f32), `rotation` (4 x f32, x y z w), `scale` (3 x f32, lossy world scale),
  `visible` (u8): 1 when any mesh, skinned-mesh or sprite renderer of the entity is enabled on an active object,
  0 when it has such renderers but all are hidden (for example a projectile whose body is hidden while its hit
  effect plays), 2 when it has none. Particles, trails and lines do not count. Nothing is ever dropped from a
  recording because it is hidden; readers decide what to show.
- `player` per connected player (PurrDiction).
- `sync` for every recorded sync type of every spawned network identity (PurrNet): SyncVar, SyncList, SyncTimer.
  Other sync types and RPCs are not recorded yet.
- COLLIDERS for every entity when it appears, and once for the static scene.

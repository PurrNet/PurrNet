# PurrNet match recording format (`.purrrec`), version 1 (draft)

A recording is everything the server (or host) knew about a match, tick by tick: each object's world transform,
collider shapes, the streams its recorder adds (with PurrDiction: the state of every predicted component and the
input every player applied), and any extra streams the game adds. What only a client has (the camera it rendered
with, raw device input, its UI) is in a small file that client writes itself, a part of the recording (see
"Client parts"). It is meant to be read outside
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

Data that originates on a client (its camera, raw input, UI) never goes through the server: each client writes it
to a file of its own, a part of the recording (see "Client parts"), keyed by the server tick that client was
showing, with the client's own clocks inside each row (see "The time model of client samples").

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

A file that was cut off (the game crashed or was killed) is still valid up to its last complete record: it has no
FOOTER, and may end in the middle of a record, which is ignored. Readers must accept such a file, read what is
there, and report the recording as incomplete; nothing is to be made up for the missing end (no DESPAWN records, no
"left" for players). The recorder hands every record to the operating system as soon as it is written, so what is
lost is the chunk that was being collected: at most the recorder's chunk length (10 s by default) of rows, and the
metadata that came with them.

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
| 14   | PART      | JSON    | a player's client was invited to record its own part of this recording |

A TYPE is written before the STREAM using it, and a STREAM before the CHUNK holding its rows. An entity's STREAM
records come right before its ENTITY record (they name the entity id the ENTITY record then introduces). Readers
should index the whole file before resolving references, and ignore unknown record kinds.

### BATCH

Small metadata records (TYPE, STREAM, ENTITY, DESPAWN, PLAYER, PART, COLLIDERS, MESH) are collected and written as one
BATCH record, normally compressed, right before the CHUNK whose rows use them. Its payload is a sequence of records
with the normal framing (`kind u8`, `length varuint`, payload), never compressed individually. Readers handle the
inner records exactly as if they were top-level.

## JSON records

### HEADER

```json
{ "format": "purrrec", "version": 1, "game": "CRACKED", "gameVersion": "0.9.1",
  "unityVersion": "6000.3.8f1", "purrdictionVersion": "1.4.2-beta.1",
  "tickRate": 30, "firstTick": 1234, "startedUtc": "2026-10-28T15:01:02Z",
  "recordingId": "5f0c2a9e41d7b3806e1f4a7c9d2b8e35",
  "scene": "SampleScene-VBot", "recorder": "host", "serverBuild": false, "labels": { } }
```

`recordingId` (32 hex digits, random per file) names this recording; client parts carry the same id. Files written
before it existed do not have it. The HEADER is always the first record and never compressed, so the id can be read
without decoding anything else. A client part's HEADER has more fields, see "Client parts".

`recorder` is `host` when the recording machine also plays and `server` when it does not. (Files written before client parts say `server` for most hosts, see "Files written before
client parts".) `serverBuild` says whether
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

### PART

```json
{ "player": 2, "tick": 1240, "tokenHash": "3f6c1e0b7a9d42c58e1f0a6b2d7c9e4310b5a8f7c6d2e1903a4b5c6d7e8f9012" }
```

Written by the server when it tells a player's client that a recording is running (at the start for connected
players, on join for the others; never for bots or the host's own player). `tick` is the server's tick at that
moment; a player who reconnects is told again but keeps the one record. Each such player gets a token: 32 hex
digits, random per player and file, sent to that client only. The recording keeps only `tokenHash`, the SHA-256 of
those 32 ASCII characters as 64 hex digits, so having the recording does not let anyone produce a part for it. A
client part carries the token itself in its HEADER; a part belongs to a player of this match when the hash of its
token equals that player's `tokenHash`. A player has at most one PART record per file, however often they reconnect.

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

`bounds` stands in for a mesh whose data could not be read. A mesh without Read/Write is read back from the GPU, which
takes a few frames: its colliders are `bounds` until then, and the set is written again with `mesh` once the data is
there. Without a graphics device (a dedicated server) such a mesh stays `bounds`. Quaternions are `[x, y, z, w]`. Units and axes are Unity's
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

Rows of a `custom` stream written through `WriteSample`: a player's camera, raw input, UI, or anything the game
adds. The row type is `ClientSample<T>`: three clocks (below) and `sample.*`, the game's struct. A sample may have
managed fields (strings, lists), and a stream may have several rows on one tick; they keep the order they were
written in.

Where the rows are depends on who wrote them. A **host's** own samples, and samples the server writes on a
player's behalf, are in the recording itself. A **remote client's** samples are in that client's part. Nothing a
client records is sent to the server.

### Files written before client parts

Recordings made before client parts existed have no `recordingId` in their HEADER, no PART records, and no parts.
They are complete as they are and are read the same way as before:

- Every remote client's camera is **in the recording itself**: a `custom` stream `camera` per player
  (`"player"` set to that player), with the same `ClientSample<CameraSample>` rows. The clients sent these rows to
  the server; their chunk tick is the server tick at which the server **received** them, so on the timeline they
  sit later than `viewTick` by about the client's latency. Use the row's `viewTick` to place the camera on the
  world it was showing.
- There are no `input` or `ui` streams for remote players.
- `recorder` says `server` also for a host whose recording started before its own client had connected, which is
  the usual case with automatic recording. Tell a host from a dedicated server by a PLAYER record with
  `"isLocal": true`.

A reader decides per file: a `camera` stream found in the recording is that player's camera, whatever the file's
age; parts only add streams.

### The time model of client samples

Every row has four times. They answer different questions.

| | what it is | use it for |
|---|---|---|
| chunk tick | In a part: the server tick this client was showing when it wrote the row, i.e. the whole part of `viewTick`, held to "not before the tick the server announced the recording on" and "not before the previous row". In the recording (a host's rows): the server's tick. | Putting the row on the recording's timeline. Indexing: "the rows of tick T". |
| `viewTick` (f64) | The server tick this client was showing at that moment, with its fraction: the client's running estimate of the server's tick. On the recording's clock, so with PurrDiction it is a prediction tick. | Pairing **what the player did with what the player saw**: a row at `viewTick` v was produced while looking at the world of about tick v (see below for how far off). |
| `clientTick` (u64) | The client's own tick counter. It starts when the client connects, so it has no fixed relation to server ticks and starts over after a reconnect. | Ordering and spacing rows of one client within one part; telling whether two rows were written during the same client tick. Not for placing anything on the server's timeline. |
| `clientTime` (f64) | Seconds on the client's real-time clock since its recorder was created, read once per frame: every sample written in a frame has the same value. | Real elapsed time between a client's rows (frame pacing, reaction times), also across a reconnect within one run of the game. Not comparable between machines. |

**What the player saw.** A client shows the server's state a little late. Measured on one machine at 60 ticks per
second, the state a client had when it wrote a row was that of the row's tick or up to 2 ticks before it (7 at
worst); with 100 ms of added latency each way, 6 to 10 ticks before. To get the exact world a player was looking
at, do not rely on the tick alone: record what the client had (with PurrDiction, the `player` stream's
`viewOffsetTicks` gives the offset per tick).

**When the server applied it.** An input is applied by the server when it arrives, which is later than the row's
tick by the time it takes to get there: 1 to 2 ticks on one machine, 7 to 10 with 100 ms of added latency each way.
To pair an input with the tick that applied it, use what the server recorded for that tick (PurrDiction's `input`
stream is exactly that: the input the server applied on each tick), and use the part's `input` rows for what the
player physically did around `viewTick`.

**How good the estimate is.** `viewTick` is set from a ping (the server's tick plus half the round trip, rounded
to a tick) and corrected whenever the next ping answers, in steps. It can therefore be a tick or two off either
way, and it can step back. When the client's frames stall (a loading hitch, a starved machine) the estimate falls
behind by about the length of the stall until the next ping corrects it: in one test run on a loaded machine rows
were up to 52 ticks early for a moment. `clientTime` shows such stalls as gaps. A row whose own `viewTick` is below its chunk tick was held: either at the tick of the
previous row (the estimate stepped back; at most a few rows, 2 on one tick in the test runs), or at `offeredTick`
(the row was written before the client's first estimate had arrived; rows from that short window all sit on
`offeredTick`). In the test runs, including the ones with added latency, late joiners and reconnects, no row had
to be held at `offeredTick`: the first estimate arrives before the server's announcement does.

## Client parts

Some data only exists on a client and is of no use to the game's server: the camera the player looked through,
what their devices did, what their UI showed. Each client writes it to a small recording of its own, a **part**,
and uploads that file itself. The game's connection carries none of it: the server only tells its clients when a
recording starts and stops.

A part is a normal `.purrrec` file (same container, records and chunk coding) that holds nothing but `custom`
streams of `ClientSample<T>` rows, each with `player` set to the client's own player id. It has its own TYPE and
STREAM ids, starting from 1. Its HEADER:

```json
{ "format": "purrrec", "version": 1, "part": true,
  "recordingId": "5f0c2a9e41d7b3806e1f4a7c9d2b8e35", "player": 2, "token": "9b1d6c0fe2a8473c5d90b7e6f1a2c384",
  "game": "CRACKED", "gameVersion": "0.9.1", "unityVersion": "6000.3.8f1",
  "tickRate": 30, "firstTick": 1300, "serverFirstTick": 1234, "offeredTick": 1290,
  "startedUtc": "2026-10-28T15:01:05Z", "recorder": "client", "labels": { } }
```

- `part: true` and `recorder: "client"` mark the file as a part. `recordingId` is the server file's, `player` the
  client's player id there, `token` the one the server gave this client (its SHA-256 is in that player's PART
  record in the server file).
- `serverFirstTick` is the server file's `firstTick`. `offeredTick` is the server's tick when it told this client
  about the recording: the recording's first tick for a client that was there from the start, the tick it joined on
  for one that came later or came back. `firstTick` is the tick of the part's first row; no row is before
  `offeredTick`.
- A part is opened by the first sample written after the server's announcement, so a client that records nothing
  writes no file. It ends when the server announces the end of the recording, when the client disconnects or when
  the game quits.
- A client that reconnects during a recording writes a new part with the same token, so a recording can have
  several parts per player. Parts of one player do not overlap in ticks. Ids inside rows (a UI element's `id`)
  belong to one file: the next part numbers its elements anew.
- A part's chunks end between two frames, never in the middle of one, and every stream that only writes changes
  (the UI) starts each chunk with its full state, so any chunk of a part can be read on its own.

**Against the recording's own records.** A part's rows start at or after its player's "joined" tick in the
recording and end at or before the "left" tick, give or take the estimate's error (within 3 ticks in the test
runs). Two things to expect:

- A player who comes back before the server has noticed that the old connection is gone never leaves the server's
  player list: the recording then has no "left"/"joined" pair for that reconnect, while there are two parts.
  The parts are the record of when a client was recording.
- When the server stops without saying so (it crashed or was killed), its clients go on recording until they
  notice the connection is gone, a few seconds later: their parts then run past the recording's last row. Rows
  after the recording's last tick have nothing to be paired with.

To merge a part into its recording: check `recordingId` and that the SHA-256 of `token` is the player's
`tokenHash`, give the part's types and streams new ids after the recording's own (remapping each STREAM's `type`),
and add its chunks' rows as they are: the ticks are already the recording's. `purrrec.py ... --parts` and the
viewer (drop the recording together with its parts) do this. A part that was cut off merges like any other.

## The camera stream

Custom stream `camera`, row type `ClientSample<CameraSample>`: the camera a player's machine rendered with, one
row per tick, taken right before it renders. `sample.position` (3 x f32), `sample.rotation` (4 x f32, x y z w),
`sample.fieldOfView`, `sample.aspect`, `sample.orthographic` (bool), `sample.orthographicSize`. A remote player's
is in that player's part; a host's own is in the recording. A reader that only has the server's file of a
dedicated server therefore has no cameras.

## The input stream

Custom stream `input`, row type `ClientSample<RawInputSample>`: what the player's devices did, one row per tick
(a frame that spans several ticks writes one row). On a client it is in the client's part; a host's own rows are
in the recording itself. The source is the Input System package when the project has it enabled, otherwise the
legacy Input Manager.

| column | type | meaning |
|--------|------|---------|
| `sample.flags` | u16 | bit 0 (1) a text field had keyboard focus during the row: no keys are recorded; 1 (2) a keyboard is present; 2 (4) a mouse or other pointer is present; 3 (8) a gamepad is present; 4 (16) the game had focus; 5 (32) read from the Input System; 6 (64) read from the legacy Input Manager |
| `sample.mouseButtons` | u8 | bit 0 left, 1 right, 2 middle, 3 back, 4 forward |
| `sample.mouseDelta.x/y` | f32 | pointer movement since the previous row, in pixels, summed over its frames |
| `sample.pointer.x/y` | f32 | pointer position divided by the screen size: (0,0) bottom left, (1,1) top right; not clamped |
| `sample.scroll.x/y` | f32 | scroll since the previous row, in the source's units |
| `sample.keys0`, `sample.keys1` | u64 | held keys: bit `n` of `keys0` is key `n`, bit `n` of `keys1` is key `64 + n` |
| `sample.leftStick.x/y`, `sample.rightStick.x/y` | f32 | gamepad sticks when the row was written, -1..1, after the Input System's deadzone (what the game reads) |
| `sample.leftTrigger`, `sample.rightTrigger` | f32 | 0..1 |
| `sample.gamepadButtons` | u32 | bit 0 d-pad up, 1 down, 2 left, 3 right, 4 north, 5 east, 6 south, 7 west, 8 left stick, 9 right stick, 10 left shoulder, 11 right shoulder, 12 start, 13 select, 14 left trigger, 15 right trigger |
| `sample.joystickButtons` | u32 | legacy Input Manager only: joystick buttons 0-19 by its own numbering |

Keys, mouse buttons and gamepad buttons are set when they were down at any moment since the previous row, so a
press and release between two rows still shows in the next one.

While a text field has keyboard focus (uGUI and TextMeshPro input fields, UI Toolkit text fields, an IMGUI control
taking keys, the on-screen keyboard) the keys of the whole row are left out and bit 0 of `flags` is set: what a
player types is never recorded. Mouse and gamepad are recorded as usual.

Key numbers are the Input System's `Key` values, which name physical positions on a US layout:

```
1 Space  2 Enter  3 Tab  4 Backquote  5 Quote  6 Semicolon  7 Comma  8 Period  9 Slash  10 Backslash
11 LeftBracket  12 RightBracket  13 Minus  14 Equals  15-40 A-Z  41-49 Digit1-Digit9  50 Digit0
51 LeftShift  52 RightShift  53 LeftAlt  54 RightAlt  55 LeftCtrl  56 RightCtrl  57 LeftMeta  58 RightMeta
59 ContextMenu  60 Escape  61 LeftArrow  62 RightArrow  63 UpArrow  64 DownArrow  65 Backspace  66 PageDown
67 PageUp  68 Home  69 End  70 Insert  71 Delete  72 CapsLock  73 NumLock  74 PrintScreen  75 ScrollLock
76 Pause  77 NumpadEnter  78 NumpadDivide  79 NumpadMultiply  80 NumpadPlus  81 NumpadMinus  82 NumpadPeriod
83 NumpadEquals  84-93 Numpad0-Numpad9  94-105 F1-F12  106-110 OEM1-OEM5  112-123 F13-F24
124 MediaPlayPause  125 MediaRewind  126 MediaForward
```

With the legacy Input Manager the same numbers are used, mapped from its `KeyCode`s (which follow the active
keyboard layout rather than key positions); there are no gamepad sticks, triggers or named gamepad buttons, only
`joystickButtons`; and before Unity 6 `mouseDelta` is the pointer's movement on screen, which is zero while the
cursor is locked.

## UI stream

What a player's UI showed, as data: a custom stream named `ui` per player (`"sample": "ui"`), written by
`RecordLocalUI`. A client's is in that client's part (see "Client parts"); a host's own is in the recording
itself. It covers uGUI (Canvas, Image, RawImage, legacy Text, any other Graphic as a plain box),
TextMeshPro UI text and UI Toolkit runtime panels (UIDocument). IMGUI and third-party UI are not recorded. It is
not a pixel-exact copy of Unity's rendering: a reader can draw each element's rectangle, name its sprite and place
its text, and read the values exactly.

Rows are `ClientSample<UISample>` (see "Client samples"; the columns below are prefixed `sample.`). The stream is
a change log: a row is written when an element appears or changes and carries the element's **complete** state, so
the tree at a tick is "the latest row of every id that has not been removed". Several rows share a tick.

| column | type | meaning |
|--------|------|---------|
| `op` | u8 enum | `Update`: the element appeared or changed. `Remove`: it is gone (only `id` is set). `Screen`: screen size, see below |
| `id` | i32 | the element; kept while its object lives (also while it is hidden or deactivated) and never reused in a stream |
| `parent` | i32 | nearest recorded element above it; 0 for a root |
| `root` | i32 | the canvas or panel it is drawn by (its own id for a root) |
| `kind` | u8 enum | `Canvas`, `Panel` (UI Toolkit), `Image`, `RawImage`, `Text`, `Element` (a box with no content of its own) |
| `widget` | u8 enum | `None`, `Button`, `Toggle`, `Slider`, `Scrollbar`, `Dropdown`, `InputField`, `ProgressBar`, `Other` |
| `flags` | u16 bits | `Visible` 1, `Interactable` 2, `Pickable` 4, `WorldSpace` 8, `Redacted` 16, `Snapshot` 32, `UIToolkit` 64 |
| `order` | i32 | paint order within the root: higher is drawn later, on top. On a root: its order among roots (the canvas's or panel's sorting order) |
| `x` `y` `width` `height` | f32 | rectangle before rotation, as fractions of the screen, origin top left, y down |
| `rotation` | f32 | degrees clockwise around the rectangle's centre |
| `color` | 4 x u8 | `r g b a`: what fills the rectangle or tints the sprite. Alpha 0: nothing is filled |
| `textColor` | 4 x u8 | colour of `text` |
| `fontSize` | f32 | text height as a fraction of the screen's height |
| `align` | u8 enum | where text sits in the rectangle, `UpperLeft` .. `LowerRight` (Unity's `TextAnchor`) |
| `fill` `fillOrigin` `fillAmount` | u8 enum, u8, f32 | a filled image (health bar, cooldown): `Horizontal`, `Vertical` or `Radial`, Unity's origin number, and the part drawn 0..1. `None` and 1 otherwise |
| `value` | f32 | a control's value: slider, scrollbar and progress bar 0..1, toggle 0 or 1 |
| `worldPosition` `worldRotation` `worldSize` | 3 + 4 + 2 x f32 | on a `WorldSpace` root only: centre, rotation (x y z w) and size of its rectangle in the world |
| `name` | string | the GameObject's name, or the UI Toolkit element's |
| `type` | string | class of the component or element: `Image`, `TextMeshProUGUI`, `Label`, `Button`... |
| `sprite` | string | name of the sprite or texture drawn; null without one |
| `text` | string | the text shown; null when the element has none or it is redacted |

**Reading.** Keep `screen` and a map `id -> row`. For each row in order: `Screen` sets the screen size (`width`,
`height` in pixels) and, with the `Snapshot` flag, empties the map; `Remove` deletes `id`; `Update` stores the row
under `id`. A row may name a parent that is written later in the same tick, so resolve parents after a tick's rows.
Every chunk starts with a `Screen` row with `Snapshot` followed by every element, so a reader can start at any
chunk. `purrrec.py` has this as `ui_at(rec, tick)`, the viewer's `purrrec.js` as `UITimeline`.

**Drawing.** In pixels: `w = width * W`, `h = height * H`, centre `(x * W + w / 2, y * H + h / 2)`, rotated by
`rotation`. Elements of a root are drawn in ascending `order`, roots in ascending `order` among themselves (the order
between a canvas and a UI Toolkit panel with the same number is not known). An element is drawn when `Visible` is
set: fill the rectangle with `color` (or draw `sprite` tinted with it) when its alpha is not 0, cut to `fillAmount`
for a filled image, then draw `text` in `textColor`. `order` means nothing on elements that draw nothing (kind
`Element` in uGUI, nested canvases).

**What the flags mean.** `Visible` is cleared when the element is in the tree but not drawn: fully transparent
(its own alpha, a CanvasGroup above it, UI Toolkit opacity), culled by a RectMask2D, `visibility: hidden`, or a Mask
that hides its own graphic. An element that is deactivated, whose canvas is disabled, or that has `display: none`
is removed instead, and comes back with the same id (a UI Toolkit element taken out of its panel and added again
gets a new one). `Interactable` is set on controls that react to input. `Pickable` marks raycast targets (uGUI) and
elements with `pickingMode: Position` (UI Toolkit).

**World-space canvases.** A root with `WorldSpace` is a rectangle in the world, given by `worldPosition` (its
centre), `worldRotation` and `worldSize` (Unity's axes: +x right, +y up along the rectangle). Its elements' `x`,
`y`, `width`, `height` and `fontSize` are fractions of that rectangle instead of the screen, so nothing is written
when only the camera moves; a reader projects the rectangle with the player's camera (the `camera` stream).
The corner at fractions `(u, v)` is `worldPosition + worldRotation * ((u - 0.5) * worldSize.x, (0.5 - v) * worldSize.y, 0)`.
Screen Space Overlay and Screen Space Camera canvases are both in screen fractions. UI Toolkit panels rendered in
world space (Unity 6.2+) are not recorded.

**Rotation and depth.** Rotation is the element's rotation within its root's plane. Tilt out of that plane
(rotation around x or y inside a canvas), skew and mirroring are not represented: the rectangle is the element's
edges projected onto the root's plane. Clipping by masks is not recorded beyond the `Visible` flag, so an element
half inside a scroll view has its whole rectangle.

**When rows are written.** Changes the UI systems announce (text, colour, sprite, size, fill amount; anything that
rebuilds a uGUI graphic, and text changes in UI Toolkit) are written on the tick they happen. Changes they do not
announce (an element moved by its transform, a CanvasGroup fade, a UI Toolkit style or layout change, a control's
value) are found by going over the elements, as many per tick as the recorder's UI check budget allows; an element
found changing is then read every tick until it stops. So with a very large UI the start of such a change can be
written a few ticks late, and what follows is on time. Rectangles are compared with a tolerance of 0.00002 (a tenth
of a pixel at 4K). The stream is sampled at most once per tick.

**Privacy.** Password and PIN fields (uGUI and TextMeshPro input fields with those content types, UI Toolkit
`isPasswordField`) are never read: their text element has `text` null and `Redacted`. With the recorder's "redact
text input" option (on by default) that holds for every text-entry field. A `DontRecordUI` component keeps the
uGUI objects under it, or the UIDocument it sits on, out of the stream entirely; the USS class `dont-record` does
the same for a UI Toolkit element and everything in it.

**Not recorded.** Fonts, sprites and textures themselves (only names), materials and shaders, text styling beyond
size, colour and alignment, how much of a text is revealed (`maxVisibleCharacters`), nine-slice borders, the
caret and selection of input fields, and UI Toolkit borders and rounded corners.

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
- `camera`, `input` and `ui` per player: the camera they looked through, what their devices did and what their UI
  showed (in each client's part; a host's own in the recording), see "The camera stream", "The input stream" and
  "UI stream".

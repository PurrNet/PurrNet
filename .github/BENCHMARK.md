# Distributed Network Benchmark

Measures PurrNet under sustained load with up to ~100 connections spread across separate
GitHub-hosted runners, connected over a free [Tailscale](https://tailscale.com) overlay.
Each runner is an isolated NAT'd VM, so peers reach each other through Tailscale's WireGuard
mesh (direct UDP, with DERP relay fallback) rather than a public IP.

## What it reports

- **Per-connection bandwidth** — downstream/upstream bytes/sec, server-aggregate and per measured client.
- **Server CPU / frame time** — process CPU%, avg/max frame time, peak RSS while carrying the load.
- **RPC round-trip latency** — p50/p95/p99 RTT, measured from single-instance clients.
- **Scaling curve** — the above charted at 10 / 25 / 50 / ~100 connections.

Only **single-instance "measured" runners** feed the stats. To reach 100 connections within the
Team plan's 60-concurrent-job cap, a few "loadgen" runners host multiple client processes each;
these apply load but are excluded from the numbers (their CPU is shared, so their stats are noise).

## Workflows

- **`benchmark.yml`** — one run at a fixed size. `Actions → Benchmark → Run workflow`.
  Inputs: `measured_clients`, `loadgen_jobs`, `procs_per_loadgen`, `bench_seconds`, `bench_objects`, `ping_rate`.
  Job budget: `build (1) → server (1) + measured (N) + loadgen (M)`; keep `N + M ≤ 58`.
- **`benchmark-scaling.yml`** — runs `benchmark.yml` sequentially at 10/25/50/~100 and renders a combined table.

Results render to the run's **job summary**; raw per-peer JSON is uploaded as artifacts.

## One-time setup (required before the workflow can run)

The benchmark needs a Tailscale tailnet so runners can see each other.

1. Create a free Tailscale account (the **Personal** plan is enough; ephemeral CI nodes are free).
2. **Admin console → Settings → OAuth clients →** create a client with the **`auth_keys`** write scope,
   assigned the tag **`tag:ci`**.
3. **Admin console → Access controls (ACL):** define the tag and allow it to talk to itself, e.g.

   ```jsonc
   "tagOwners": { "tag:ci": ["autogroup:admin"] },
   "acls": [ { "action": "accept", "src": ["tag:ci"], "dst": ["tag:ci:*"] } ]
   ```

4. **GitHub → repo Settings → Secrets and variables → Actions** add:
   - `TS_OAUTH_CLIENT_ID`
   - `TS_OAUTH_SECRET`

   (The Unity build secrets `UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD` / `UNITY_SERIAL`
   are already used by the existing test workflows.)

CI nodes join as **ephemeral, tagged** devices and are removed automatically when the job ends.

## Cost

The repo is public, so GitHub-hosted runner minutes are **free**. Tailscale ephemeral usage at this
scale is within the free tier. The only spend would be on a private fork (Linux runners ~$0.006/min).

## Relay fallback (instead of Tailscale)

If you'd rather not run an overlay, PurrNet's relay transport (`PurrTransport`) is the alternative:
every peer dials **out** to a hosted relay, so no inbound connectivity or Tailscale is needed. This
path needs a reachable relay endpoint (hosted = paid). The harness is transport-agnostic — the client
just needs `-serverHost`/room wiring — but the provided workflows assume the free Tailscale + UDP path.

## How the harness fits together

- Benchmark scenarios are **authored components**, like the correctness tests. They live under a
  separate **`Benchmarks`** object in `Bootstrap.unity`, linked to `Bootstrap._benchmarkScenarios`.
  Reorder, disable (deactivate the GameObject), or add child `BenchmarkScenario`-derived components
  to change which benchmarks run and in what order — no code changes.
- `Bootstrap` gains `-bench`, `-benchSeconds`, `-benchObjects`, `-benchPingRate`, `-serverHost`,
  `-port`, `-connectTimeout`, and `-loadgen`. With `-bench` it runs the connection scenario + the
  scenarios under `Benchmarks`; without it, it discovers its own correctness children as before
  (the `Benchmarks` object is a root sibling, so it's never picked up by the normal suite).
- `-benchObjects` / `-benchPingRate` **override** the per-scenario inspector values when supplied;
  otherwise the authored values are used. `-benchSeconds` overrides the window on `Bootstrap`.
- `BenchmarkScenario` spawns N `NetworkTransform` objects on the server and mutates them every frame
  for the measurement window; clients observe and (if measured) sample RTT via `BenchmarkPing`.
- `ServerLoadSampler` reads `/proc/self/stat` + frame time for CPU/tick metrics (Linux only).
- Metrics land in `ScenarioDetails.benchmark`; aggregation is done by
  `.github/scripts/bench-aggregate.sh` (single run) and `.github/scripts/bench-scaling.sh`
  (curve), which read only `measured == true` blocks and render to the job summary.

## Debugging in the editor

To iterate on a benchmark without GitHub Actions:

1. On the `Bootstrap` component, tick **`Editor Benchmark Mode`** (forces `-bench` in the editor).
   Optionally shorten **`Bench Seconds`** for faster loops.
2. Open **Multiplayer Play Mode** (Window → Multiplayer Play Mode) and enable **one** virtual player.
3. Press Play. The main editor runs as **Host** (`Editor Role`) and the virtual player joins as a
   **Client** — i.e. host + 1 other client. `Editor Expected Connections` (2) gates the connection
   scenario.

Results are logged as JSON to the Console (`Bootstrap.WriteResults`). To re-run, exit and re-enter
Play mode. Untick `Editor Benchmark Mode` to go back to the correctness suite.

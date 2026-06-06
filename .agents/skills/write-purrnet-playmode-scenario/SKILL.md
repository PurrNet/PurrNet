---
name: write-purrnet-playmode-scenario
description: >-
  Scaffold a PurrNet PlayMode test scenario in the PurrNet Unity repo — the Scenario subclass plus
  its own networked identity types, following every harness convention, and wired into the Bootstrap
  scene so it runs in the server+host CI matrix. Use this whenever the user wants to add or write a
  PlayMode test, a network/multiplayer test scenario, regression coverage for a PurrNet
  spawn/despawn/visibility/RPC/sync bug, or says "turn this into a test" / "add a test for this"
  while working in PurrNet — even if they don't say the words "PlayMode" or "scenario". Do not
  hand-roll a test from scratch; this harness has subtle conventions (static-reset race, host
  double-callbacks, barrier sync) that are easy to get wrong.
---

# Write a PurrNet PlayMode test scenario

PurrNet's integration tests live in `Assets/PlayModeTests/`. Each test is a **scenario**: a
`Scenario` MonoBehaviour discovered by `Bootstrap` (`GetComponentsInChildren<Scenario>()`), run
across multiple real processes (1 server/host + N clients) by `ScenarioSequencer`, twice via a CI
matrix (`server` mode and `host` mode). A scenario builds its own networked prefab at runtime, drives
it from the server, and asserts an invariant on **every peer**.

A scenario is **three files** (plus `.meta`s):
- `<Name>Scenario.cs` — the test driver (`Scenario` subclass).
- `<Name>Root.cs` / `<Name>Child.cs` — its **own** `NetworkIdentity` types with static trackers.

The identity types must be unique per scenario. `Setup()` runs once for *all* scenarios at startup,
so two scenarios sharing an identity type would clobber each other's static counters.

## Procedure

1. **Pin down the test.** What behavior/bug? What prefab shape exercises it (how many identities,
   how deep)? What's the invariant to assert each phase? Prefer **count-based** invariants (e.g.
   "every peer sees exactly N children with valid ids") — they're uniform across server/host/client
   and catch missing/garbage spawns directly.

2. **Write the identity types** (`references/template.md` has the full annotated code). Each:
   - extends `NetworkIdentity`, lives in its own file with its own statics + `ResetAll()`,
   - root activates itself in `OnEarlySpawn()` (the prefab root is built `SetActive(false)`),
   - tracks liveness in a `static HashSet<NetworkID>` keyed by `id` — see the host gotcha below,
   - trips a `SawBadId` flag if a child ever spawns with a default/`Server:0` id (the classic
     "unspawned identity shipped" symptom).

3. **Write the scenario** (`references/template.md`): `CreatePrefab()` builds the tree with
   `new GameObject()` + `AddComponent` and resets statics; `Setup()` registers it via
   `manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject)`; `RunScenario()`
   drives it on the server and verifies on every peer with `WaitWithTimeout` + `ScenarioBarrier`.

4. **Create the `.cs.meta` files** for all three scripts (Unity may auto-generate them if it's
   running; otherwise write minimal ones — `fileFormatVersion: 2` + a unique `guid`).

5. **Wire the scenario into the scene** (see "Wiring" below) — add the `<Name>Scenario` component to
   the `Lifetimes` GameObject under `Bootstrap` in `Assets/PlayModeTests/Bootstrap.unity`, and save.

6. **Sanity-check** the assertions are real invariants (a red must mean a real bug, not a contrived
   setup) and run the `Network Tests` workflow / PlayMode tests.

## Conventions that bite (get these right)

- **Reset statics in `CreatePrefab()`, never at the top of `RunScenario()`.** All scenarios are
  `Setup()`-ed up front; resetting in `RunScenario` races cross-peer spawn timing.
  *(memory: feedback_scenario_static_reset_race)*
- **No informational `Debug.Log`.** Only failure detail, via `ScenarioResult.Fail(...)`. Use
  `Debug.LogError` only on genuine error paths. *(memory: feedback_no_informational_logs)*
- **Host fires lifecycle callbacks twice** (server-side + client-side, same process). Dedupe by
  keying liveness on `NetworkID` in a `HashSet`, so `OnSpawned()` adding the same id twice is
  idempotent. The no-arg `OnSpawned()`/`OnDespawned()` fire once; the `(bool asServer)` overloads
  fire per side. *(memory: host-process-double-fire)*
- **Count-based assertions are host-uniform.** Each peer independently waits for its own
  `AliveCount == expected`. Only use connection-count math (and exclude the host's local player)
  where you genuinely need it. *(memory: playmodetests-host-server-matrix)*
- **`ScenarioBarrier.Wait(ctx, id, timeout)`** between phases so the server doesn't despawn before
  clients have observed the spawn. Pick a `BarrierBase` not used by other scenarios (grep existing
  scenarios; e.g. 5200/5300/5400 are taken by the respawn/restore/chunk trio) and derive per-phase
  ids like `BarrierBase + cycle*10 + phase`. All peers must hit the same barrier ids in the same
  order — keep any conditional-phase logic identical across server and clients.
- **Server-authority is the default**, so a server-driven spawn needs no `NetworkRules` asset. Only
  wire a `NetworkRules` (`spawnAuth=Everyone`) `[SerializeField]` if a *client* must spawn (see
  `ClientSpawnScenario` / `DestroyDuringSpawnScenario`).
- Every `WaitWithTimeout` is wrapped in `try { … } catch (TimeoutException) { return
  ScenarioResult.Fail(<diagnostic with the actual counts>); }` so a failure reports fast and
  legibly instead of hanging the run.

## Wiring into the scene

Scenarios only run if they're a component on a child of `Bootstrap`. The lifecycle scenarios hang off
the `Lifetimes` GameObject. **Prefer the Unity MCP** (it serializes the GUID/fileID correctly and
avoids racing Unity's own writes):

1. `Unity_ManageGameObject` → `action: add_component`, `target: "Lifetimes"`, `search_method:
   by_name`, `component_name: "<Name>Scenario"`.
2. Save so it persists for CI — `Unity_RunCommand`:
   ```csharp
   using UnityEditor.SceneManagement;
   internal class CommandScript : IRunCommand {
       public void Execute(ExecutionResult result) =>
           result.Log("saved={0}", EditorSceneManager.SaveOpenScenes());
   }
   ```

If the Unity MCP isn't connected, hand-edit `Bootstrap.unity`: add a `--- !u!114 &<freeFileID>`
`MonoBehaviour` block (`m_GameObject: {fileID: 1680343622}` is `Lifetimes`; `m_Script` guid = the
scenario's `.meta` guid; copy a sibling scenario block's field layout) and add
`  - component: {fileID: <freeFileID>}` to the `Lifetimes` `m_Component` list. Pick a fileID not
already present in the file.

## Reference

- `references/template.md` — full annotated, copy-and-adapt template for all three files.
- Live examples in the repo (read these as the canonical, maintained pattern):
  `Assets/PlayModeTests/Scripts/LifecycleTests/HierarchyRespawnScenario.cs` (+ `…Root`/`…Child`) for
  spawn/despawn cycles; `SameFrameChildDestroyScenario.cs` for same-frame destroy;
  `ChunkRestoreScenario.cs` for repeated load/unload. For client-spawn or RPC tests, mirror
  `ClientSpawnScenario.cs` / the `RPCTests/` scenarios; for sync-collection tests, `SyncTypesTests/`.
- Harness internals worth knowing: `Scenario.cs` (`RunSplit` for per-role halves), `ScenarioBarrier.cs`,
  `ScenarioContext.cs` (`ctx.role`/`isServer`/`isClient`/`expectedConnections`), `UniTaskUtils.cs`.

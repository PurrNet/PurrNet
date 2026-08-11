using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Faithful repro of the open-world chunk load/unload cycle. Every "chunk load" the server
// Instantiates the PRISTINE prefab (never a snapshot) and re-applies the saved removal by destroying
// the children that the saved state says are gone — same frame, before the spawn settles — then the
// chunk unloads (despawn). This repeats across many cycles on a deep, POI-like tree.
//
// Every assertion is a real invariant, so a red here means a genuine bug, not a contrived setup:
//   - after each load every peer observes exactly the survivors, with valid (non-Server:0) ids,
//   - no child ever spawns with a default/unassigned id,
//   - despawn leaves zero orphans before the next load.
public class ChunkRestoreScenario : Scenario
{
    [SerializeField] private int _cycles = 4;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5400;
    private const int ExpectedChildren = 12;
    private const int DisposableCount = 4;
    private const int Survivors = ExpectedChildren - DisposableCount;

    private ChunkRestoreRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(ChunkRestoreScenario) + "Root");
        _prefab = rootGo.AddComponent<ChunkRestoreRoot>();

        var c1 = AddChild(rootGo, "Container1");
        AddChild(c1, "SlotA", disposable: true);
        AddChild(c1, "SlotB");
        var n1 = AddChild(c1, "Nested1");
        AddChild(n1, "SlotC", disposable: true);
        AddChild(n1, "SlotD");

        var c2 = AddChild(rootGo, "Container2");
        AddChild(c2, "SlotE", disposable: true);
        AddChild(c2, "SlotF");
        var n2 = AddChild(c2, "Nested2");
        AddChild(n2, "SlotG", disposable: true);
        AddChild(n2, "SlotH");

        rootGo.SetActive(false);
        ChunkRestoreRoot.ResetAll();
        ChunkRestoreChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName, bool disposable = false)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        var child = go.AddComponent<ChunkRestoreChild>();
        child.isDisposable = disposable;
        return go;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        // Ensure the first load tests the staged Spawn + child Despawn transaction on every peer.
        await ScenarioBarrier.Wait(ctx, BarrierBase, _barrierTimeoutSeconds);

        for (int cycle = 0; cycle < _cycles; cycle++)
        {
            int barrier = BarrierBase + cycle * 10;
            ChunkRestoreRoot instance = null;

            // Chunk load: instantiate the pristine prefab, then re-apply the saved removal in the
            // same frame (before the spawn settles).
            if (ctx.isServer)
            {
                instance = Instantiate(_prefab);
                instance.DestroyAllDisposableChildren();
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ChunkRestoreRoot.LocalInstance != null
                          && ChunkRestoreChild.AliveCount == Survivors,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: load incomplete: root={ChunkRestoreRoot.LocalInstance != null}, " +
                    $"survivors={ChunkRestoreChild.AliveCount}/{Survivors}, badId={ChunkRestoreChild.SawBadId}");
            }

            if (ChunkRestoreChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: a child spawned with a default/unassigned id (Server:0)");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            // Chunk unload.
            if (ctx.isServer && instance)
                instance.Despawn();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ChunkRestoreRoot.LocalInstance == null
                          && ChunkRestoreChild.AliveCount == 0,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: unload incomplete: root={ChunkRestoreRoot.LocalInstance != null}, " +
                    $"leftover children={ChunkRestoreChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok(
            $"{_cycles} chunk load/unload cycles, {Survivors} survivors each load, no orphans");
    }
}

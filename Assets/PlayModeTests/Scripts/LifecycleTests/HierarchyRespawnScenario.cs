using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Regression coverage for the despawn -> respawn cycle of a multi-child prefab (e.g. an open-world
// "point of interest" with several nested networked children). Each cycle the server spawns the
// prefab and every peer must observe the FULL child set with valid ids. In cycle 0 the server also
// destroys one leaf child mid-life; the subsequent respawn must still bring that child back — a
// destroyed child must not linger as an id=0 orphan that aborts a later spawn batch with a
// "Identity with id `Server:0` already exists" collision.
public class HierarchyRespawnScenario : Scenario
{
    [SerializeField] private int _cycles = 3;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5200;
    private const int ExpectedChildren = 5;

    private HierarchyRespawnRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(HierarchyRespawnRoot));
        _prefab = rootGo.AddComponent<HierarchyRespawnRoot>();

        // root -> A -> A1(leaf), root -> B -> B1(leaf), root -> Disposable(leaf)
        var a = AddChild(rootGo, "A");
        AddChild(a, "A1");
        var b = AddChild(rootGo, "B");
        AddChild(b, "B1");
        var disposable = AddChild(rootGo, "Disposable");
        disposable.GetComponent<HierarchyRespawnChild>().isDisposable = true;

        rootGo.SetActive(false);
        HierarchyRespawnRoot.ResetAll();
        HierarchyRespawnChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<HierarchyRespawnChild>();
        return go;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        for (int cycle = 0; cycle < _cycles; cycle++)
        {
            int barrier = BarrierBase + cycle * 10;
            HierarchyRespawnRoot instance = null;

            if (ctx.isServer)
                instance = Instantiate(_prefab);

            // Every peer must observe the full child set, every cycle.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => HierarchyRespawnRoot.LocalInstance != null
                          && HierarchyRespawnChild.AliveCount == ExpectedChildren,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: spawn incomplete: root={HierarchyRespawnRoot.LocalInstance != null}, " +
                    $"children={HierarchyRespawnChild.AliveCount}/{ExpectedChildren}, " +
                    $"badId={HierarchyRespawnChild.SawBadId}");
            }

            if (HierarchyRespawnChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: a child spawned with a default/unassigned id (Server:0)");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            // cycle 0 only: destroy one leaf child mid-life. The remaining children must stay, and
            // the NEXT cycle's respawn must still bring the full set back.
            if (cycle == 0)
            {
                if (ctx.isServer && instance)
                    instance.DestroyDisposableChild();

                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => HierarchyRespawnChild.AliveCount == ExpectedChildren - 1,
                        _despawnTimeoutSeconds,
                        ctx.cancellationToken);
                }
                catch (TimeoutException)
                {
                    return ScenarioResult.Fail(
                        $"cycle 0: child destroy didn't replicate: " +
                        $"children={HierarchyRespawnChild.AliveCount}/{ExpectedChildren - 1}");
                }

                await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
            }

            if (ctx.isServer && instance)
                instance.Despawn();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => HierarchyRespawnRoot.LocalInstance == null
                          && HierarchyRespawnChild.AliveCount == 0,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: despawn incomplete: root={HierarchyRespawnRoot.LocalInstance != null}, " +
                    $"children={HierarchyRespawnChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 3, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok($"{_cycles} despawn/respawn cycles, full child set ({ExpectedChildren}) each cycle");
    }
}

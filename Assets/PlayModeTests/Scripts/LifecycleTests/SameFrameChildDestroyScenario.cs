using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Mirrors the real-world pattern that triggered the Server:0 collision: the server instantiates a
// deep multi-child prefab and, in the SAME frame (before the spawn settles), destroys several
// children — like "Instantiate(POI); Destroy(active ItemViews);". The surviving children must
// replicate everywhere with valid ids, and a later despawn -> respawn must bring the FULL set back
// (a child destroyed mid-spawn must not linger as an id=0 orphan that aborts a respawn batch).
public class SameFrameChildDestroyScenario : Scenario
{
    [SerializeField] private int _cycles = 3;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5300;
    private const int ExpectedChildren = 10;
    private const int DisposableCount = 3;

    private SameFrameDestroyRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SameFrameChildDestroyScenario) + "Root");
        _prefab = rootGo.AddComponent<SameFrameDestroyRoot>();

        var groupA = AddChild(rootGo, "GroupA");
        AddChild(groupA, "ItemA1", disposable: true);
        AddChild(groupA, "ItemA2");
        var subA = AddChild(groupA, "SubA");
        AddChild(subA, "ItemA3", disposable: true);

        var groupB = AddChild(rootGo, "GroupB");
        AddChild(groupB, "ItemB1");
        AddChild(groupB, "ItemB2", disposable: true);

        var groupC = AddChild(rootGo, "GroupC");
        AddChild(groupC, "ItemC1");

        rootGo.SetActive(false);
        SameFrameDestroyRoot.ResetAll();
        SameFrameDestroyChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName, bool disposable = false)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        var child = go.AddComponent<SameFrameDestroyChild>();
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
        for (int cycle = 0; cycle < _cycles; cycle++)
        {
            int barrier = BarrierBase + cycle * 10;
            SameFrameDestroyRoot instance = null;

            // cycle 0 mirrors the bug repro: instantiate, then destroy children SAME FRAME (no await
            // in between) so the destroy lands while the spawn is still in flight. Later cycles
            // re-spawn untouched and must show the full set again.
            bool destroyThisCycle = cycle == 0;
            int expected = destroyThisCycle ? ExpectedChildren - DisposableCount : ExpectedChildren;

            if (ctx.isServer)
            {
                instance = Instantiate(_prefab);
                if (destroyThisCycle)
                    instance.DestroyAllDisposableChildren();
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SameFrameDestroyRoot.LocalInstance != null
                          && SameFrameDestroyChild.AliveCount == expected,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: spawn incomplete: root={SameFrameDestroyRoot.LocalInstance != null}, " +
                    $"children={SameFrameDestroyChild.AliveCount}/{expected}, " +
                    $"badId={SameFrameDestroyChild.SawBadId}");
            }

            if (SameFrameDestroyChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: a child spawned with a default/unassigned id (Server:0)");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            if (ctx.isServer && instance)
                instance.Despawn();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SameFrameDestroyRoot.LocalInstance == null
                          && SameFrameDestroyChild.AliveCount == 0,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: despawn incomplete: root={SameFrameDestroyRoot.LocalInstance != null}, " +
                    $"children={SameFrameDestroyChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok(
            $"{_cycles} cycles: same-frame destroy left {ExpectedChildren - DisposableCount}, respawns restored {ExpectedChildren}");
    }
}

using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Regression coverage for pooled prefab reconstruction: the cached prefab prototype must include
// every nested NetworkIdentity, and every spawn cycle must rebuild the full child hierarchy.
public class PooledHierarchyRespawnScenario : Scenario
{
    [SerializeField] private int _cycles = 10;
    [SerializeField] private int _instancesPerCycle = 8;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 7000;
    private const int ExpectedChildren = 5;

    private PooledHierarchyRespawnRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PooledHierarchyRespawnRoot));
        _prefab = rootGo.AddComponent<PooledHierarchyRespawnRoot>();

        var a = AddChild(rootGo, "A");
        AddChild(a, "A1");
        var b = AddChild(rootGo, "B");
        AddChild(b, "B1");
        AddChild(rootGo, "C");

        rootGo.SetActive(false);
        PooledHierarchyRespawnRoot.ResetAll();
        PooledHierarchyRespawnChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<PooledHierarchyRespawnChild>();
        return go;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject, true, 2);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        for (int cycle = 0; cycle < _cycles; cycle++)
        {
            int barrier = BarrierBase + cycle * 10;
            var instances = ctx.isServer ? new PooledHierarchyRespawnRoot[_instancesPerCycle] : null;

            if (ctx.isServer)
            {
                for (var i = 0; i < _instancesPerCycle; i++)
                    instances[i] = Instantiate(_prefab);
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PooledHierarchyRespawnRoot.AliveCount == _instancesPerCycle
                          && PooledHierarchyRespawnChild.AliveCount == _instancesPerCycle * ExpectedChildren,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: pooled spawn incomplete: roots={PooledHierarchyRespawnRoot.AliveCount}/{_instancesPerCycle}, " +
                    $"children={PooledHierarchyRespawnChild.AliveCount}/{_instancesPerCycle * ExpectedChildren}, " +
                    $"rootBadId={PooledHierarchyRespawnRoot.SawBadId}, " +
                    $"badId={PooledHierarchyRespawnChild.SawBadId}, " +
                    $"wrongParent={PooledHierarchyRespawnChild.SawWrongParent}");
            }

            if (PooledHierarchyRespawnRoot.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: pooled root spawned with a default/unassigned id");

            if (PooledHierarchyRespawnChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: pooled child spawned with a default/unassigned id");

            if (PooledHierarchyRespawnChild.SawWrongParent)
                return ScenarioResult.Fail($"cycle {cycle}: pooled child spawned outside the root hierarchy");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            if (ctx.isServer)
            {
                for (var i = 0; i < instances.Length; i++)
                {
                    if (instances[i])
                        instances[i].Despawn();
                }
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PooledHierarchyRespawnRoot.AliveCount == 0
                          && PooledHierarchyRespawnChild.AliveCount == 0,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: pooled despawn incomplete: roots={PooledHierarchyRespawnRoot.AliveCount}, " +
                    $"children={PooledHierarchyRespawnChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok($"{_cycles} pooled respawn cycles, {_instancesPerCycle} instances, full child set ({ExpectedChildren}) each instance");
    }
}

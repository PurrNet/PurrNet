using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class NonPooledRuntimeHierarchyScenario : Scenario
{
    [SerializeField] private int _cycles = 6;
    [SerializeField] private int _instancesPerCycle = 6;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 7300;
    private const int ExpectedChildren = 5;

    private NonPooledRuntimeHierarchyRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NonPooledRuntimeHierarchyRoot));
        _prefab = rootGo.AddComponent<NonPooledRuntimeHierarchyRoot>();

        var a = AddChild(rootGo, "A");
        AddChild(a, "A1");
        var b = AddChild(rootGo, "B");
        AddChild(b, "B1");
        AddChild(rootGo, "C");

        rootGo.SetActive(false);
        NonPooledRuntimeHierarchyRoot.ResetAll();
        NonPooledRuntimeHierarchyChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<NonPooledRuntimeHierarchyChild>();
        return go;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject, false);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        for (int cycle = 0; cycle < _cycles; cycle++)
        {
            int barrier = BarrierBase + cycle * 10;
            var instances = ctx.isServer ? new NonPooledRuntimeHierarchyRoot[_instancesPerCycle] : null;

            if (ctx.isServer)
            {
                for (int i = 0; i < _instancesPerCycle; i++)
                    instances[i] = Instantiate(_prefab);
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => NonPooledRuntimeHierarchyRoot.AliveCount == _instancesPerCycle
                          && NonPooledRuntimeHierarchyChild.AliveCount == _instancesPerCycle * ExpectedChildren,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: non-pooled spawn incomplete: " +
                    $"roots={NonPooledRuntimeHierarchyRoot.AliveCount}/{_instancesPerCycle}, " +
                    $"children={NonPooledRuntimeHierarchyChild.AliveCount}/{_instancesPerCycle * ExpectedChildren}, " +
                    $"rootBadId={NonPooledRuntimeHierarchyRoot.SawBadId}, " +
                    $"childBadId={NonPooledRuntimeHierarchyChild.SawBadId}, " +
                    $"wrongParent={NonPooledRuntimeHierarchyChild.SawWrongParent}");
            }

            if (NonPooledRuntimeHierarchyRoot.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: non-pooled root spawned with a default/unassigned id");

            if (NonPooledRuntimeHierarchyChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: non-pooled child spawned with a default/unassigned id");

            if (NonPooledRuntimeHierarchyChild.SawWrongParent)
                return ScenarioResult.Fail($"cycle {cycle}: non-pooled child spawned outside the root hierarchy");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            if (ctx.isServer)
            {
                for (int i = 0; i < instances.Length; i++)
                {
                    if (instances[i])
                        instances[i].Despawn();
                }
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => NonPooledRuntimeHierarchyRoot.AliveCount == 0
                          && NonPooledRuntimeHierarchyChild.AliveCount == 0,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: non-pooled despawn incomplete: " +
                    $"roots={NonPooledRuntimeHierarchyRoot.AliveCount}, " +
                    $"children={NonPooledRuntimeHierarchyChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok(
            $"{_cycles} non-pooled respawn cycles, {_instancesPerCycle} instances, full child set ({ExpectedChildren}) each instance");
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Base for all benchmark scenarios. Owns everything shared: the measurement window wiring,
// LastMetrics, the ping rate, prefab creation, despawn, and auto-assigned barrier ids.
// A new benchmark only implements OnSetup (build its prefab), Spawn and Tick.
public abstract class BenchmarkScenarioBase : Scenario, IBenchmarkScenario
{
    [SerializeField] protected float _pingsPerSecond = 10f;

    protected readonly List<NetworkTransform> _spawned = new();
    protected NetworkTransform _prefab;

    public BenchmarkMetrics? LastMetrics { get; private set; }

    // Unique, peer-stable barrier ids assigned in scenario-discovery order so adding a scenario
    // needs no hand-picked ids. Bootstrap calls Setup in the same order on every peer.
    private static int _slotCounter;
    private int _barrierStart;
    private int _barrierEnd;

    public virtual void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        if (pingsPerSecond is > 0)
            _pingsPerSecond = pingsPerSecond.Value;
    }

    public sealed override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        int slot = _slotCounter++;
        _barrierStart = 9001 + slot * 100;
        _barrierEnd = _barrierStart + 1;
        OnSetup(ctx, manager);
    }

    public sealed override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var metrics = await BenchmarkHarness.RunWindow(
            ctx, _barrierStart, _barrierEnd, _pingsPerSecond,
            onSpawn: () => Spawn(ctx),
            onTick: (elapsed, dt) => Tick(ctx, elapsed, dt),
            onDespawn: () => Despawn(ctx),
            objectCount: () => ObjectCount(ctx));

        LastMetrics = metrics;
        return ScenarioResult.Ok(BenchmarkHarness.Describe(metrics));
    }

    protected abstract void OnSetup(ScenarioContext ctx, NetworkManager manager);
    protected abstract UniTask Spawn(ScenarioContext ctx);
    protected abstract void Tick(ScenarioContext ctx, float elapsed, float dt);

    protected virtual int ObjectCount(ScenarioContext ctx) => _spawned.Count;

    protected virtual void Despawn(ScenarioContext ctx)
    {
        if (ctx.isServer)
            DespawnAll();
    }

    // Spawns ownerless (no auto-owner rule) so the scenario can assign ownership explicitly.
    protected void SpawnSuppressed(Action body)
    {
        HierarchyV2.SupressAutoOwner();
        try
        {
            body();
        }
        finally
        {
            HierarchyV2.ResumeAutoOwner();
        }
    }

    protected NetworkTransform CreatePrefab(NetworkManager manager, string name, params Type[] extraComponents)
    {
        var go = new GameObject(name);
        _prefab = go.AddComponent<NetworkTransform>();
        if (extraComponents != null)
        {
            foreach (var t in extraComponents)
                go.AddComponent(t);
        }
        go.SetActive(false);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, go);
        return _prefab;
    }

    protected void DespawnAll()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i])
                Destroy(_spawned[i].gameObject);
        }

        _spawned.Clear();
    }
}

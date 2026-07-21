using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Pure server-side rollback CPU load: N locally-created (non-networked) boxes with
// ColliderRollback orbit their grid slots so every tick snapshots N colliders, while the
// server fires Q rollback raycasts + sphere casts per frame at a tick ~100ms in the past
// (alternating aimed-to-hit and aimed-to-miss). Frame p95/p99 + GC in BenchmarkMetrics is
// the regression signal; clients idle. -benchObjects overrides the collider count.
public class ColliderRollbackBenchmarkScenario : BenchmarkScenarioBase
{
    [SerializeField] private int _colliderCount = 256;
    [SerializeField] private int _queriesPerFrame = 64;

    private readonly List<Transform> _boxes = new();
    private RollbackModule _module;
    private TickManager _tick;

    public override void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        base.ApplyOverrides(objectCount, pingsPerSecond);
        if (objectCount is > 0)
            _colliderCount = objectCount.Value;
    }

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager) { }

    protected override UniTask Spawn(ScenarioContext ctx)
    {
        if (!ctx.isServer)
            return UniTask.CompletedTask;

        var nm = ctx.networkManager;
        _module = null;
        _tick = null;

        if (nm.TryGetModule<TickManager>(true, out _tick) &&
            nm.TryGetModule<ScenesModule>(true, out var scenes) &&
            scenes.TryGetSceneID(gameObject.scene, out var sceneId) &&
            nm.TryGetModule<ColliderRollbackFactory>(true, out var factory))
            factory.TryGetModule(sceneId, out _module);

        for (int i = 0; i < _colliderCount; i++)
        {
            var go = new GameObject($"RollbackBench_{i}");
            go.transform.position = Slot(i);
            go.AddComponent<BoxCollider>();
            go.AddComponent<ColliderRollback>();
            _boxes.Add(go.transform);
        }

        return UniTask.CompletedTask;
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
        if (!ctx.isServer || _module == null || _tick == null)
            return;

        for (int i = 0; i < _boxes.Count; i++)
        {
            float phase = elapsed * 2f + i * 0.618f;
            _boxes[i].position = Slot(i) + new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase)) * 0.5f;
        }

        double pastTick = _tick.localTick - _tick.tickRate * 0.1;
        if (pastTick < 0)
            pastTick = 0;

        for (int q = 0; q < _queriesPerFrame; q++)
        {
            var target = Slot(q * 31 % Mathf.Max(1, _boxes.Count));
            bool aimToMiss = (q & 1) == 1;
            var origin = target + new Vector3(aimToMiss ? 50f : 0f, 0f, -10f);
            var ray = new Ray(origin, Vector3.forward);

            if ((q & 3) == 2)
                _module.SphereCast(pastTick, ray, 0.25f, out _, 20f);
            else
                _module.Raycast(pastTick, ray, out _, 20f);
        }
    }

    protected override int ObjectCount(ScenarioContext ctx) => ctx.isServer ? _boxes.Count : 0;

    protected override void Despawn(ScenarioContext ctx)
    {
        for (int i = 0; i < _boxes.Count; i++)
        {
            if (_boxes[i])
                Destroy(_boxes[i].gameObject);
        }

        _boxes.Clear();
        _module = null;
        _tick = null;
    }

    private static Vector3 Slot(int i)
    {
        return new Vector3(600f + (i % 16) * 4f, 2f, 600f + (i / 16) * 4f);
    }
}

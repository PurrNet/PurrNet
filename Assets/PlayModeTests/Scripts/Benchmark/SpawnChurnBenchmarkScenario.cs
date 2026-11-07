using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Spawn/despawn churn under fan-out: the server keeps N resting NetworkTransforms alive and
// replaces N/_churnDivisor of them every tick, like the community benchmark's SpawnChurn test.
// Stresses per-(object, observer) spawn, visibility and despawn work.
public class SpawnChurnBenchmarkScenario : BenchmarkScenarioBase
{
    [SerializeField] private int _objectCount = 200;
    [SerializeField] private int _churnDivisor = 50;

    private int _next;
    private uint _lastTick;

    public override void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        base.ApplyOverrides(objectCount, pingsPerSecond);
        if (objectCount is > 0)
            _objectCount = objectCount.Value;
    }

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab(manager, nameof(SpawnChurnBenchmarkScenario) + "_Obj");
    }

    protected override UniTask Spawn(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            _lastTick = ctx.networkManager.tickModule.localTick;
            SpawnSuppressed(() =>
            {
                for (int i = 0; i < _objectCount; i++)
                    _spawned.Add(SpawnOne(i));
            });
        }

        return UniTask.CompletedTask;
    }

    private NetworkTransform SpawnOne(int i)
    {
        var inst = Instantiate(_prefab);
        inst.transform.position = new Vector3(i % 100, 0, i / 100);
        inst.gameObject.SetActive(true);
        return inst;
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
        if (!ctx.isServer || _spawned.Count == 0)
            return;

        uint tick = ctx.networkManager.tickModule.localTick;
        if (tick == _lastTick)
            return;
        _lastTick = tick;

        int churn = Mathf.Max(1, _objectCount / _churnDivisor);
        SpawnSuppressed(() =>
        {
            for (int c = 0; c < churn; c++)
            {
                _next = (_next + 1) % _spawned.Count;
                if (_spawned[_next])
                    Destroy(_spawned[_next].gameObject);
                _spawned[_next] = SpawnOne(_next);
            }
        });
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Idle cost of many resting NetworkTransforms: the server spawns N server-owned NetworkTransforms
// nested three levels deep (props under an environment hierarchy) and never moves them. Clients
// only observe; their CPU should not scale with how many objects sit still.
// -benchBodies gives every transform a dynamic Rigidbody resting on a floor collider.
public class StaticTransformsBenchmarkScenario : BenchmarkScenarioBase
{
    private const string NT_NAME = "StaticNT";
    private const float SPAWN_READY_TIMEOUT = 60f;

    [SerializeField] private int _objectCount = 5000;
    [SerializeField] private int _perGroup = 100;

    private GameObject _groupPrefab;
    private readonly List<GameObject> _groups = new();

    private int groupCount => (_objectCount + _perGroup - 1) / _perGroup;

    public override void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        base.ApplyOverrides(objectCount, pingsPerSecond);
        if (objectCount is > 0)
            _objectCount = objectCount.Value;
    }

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        _groupPrefab = new GameObject(nameof(StaticTransformsBenchmarkScenario) + "_Group");
        _groupPrefab.SetActive(false);
        _groupPrefab.AddComponent<NetworkIdentity>();

        var a = new GameObject("A").transform;
        a.SetParent(_groupPrefab.transform, false);
        var b = new GameObject("B").transform;
        b.SetParent(a, false);

        bool bodies = CommandLineUtils.HasFlag("-benchBodies");
        if (bodies)
        {
            var floor = a.gameObject.AddComponent<BoxCollider>();
            floor.center = new Vector3(4.5f, -0.9f, 4.5f);
            floor.size = new Vector3(12f, 1f, 12f);
        }

        for (int i = 0; i < _perGroup; i++)
        {
            var go = new GameObject(NT_NAME);
            go.transform.SetParent(b, false);
            go.transform.localPosition = new Vector3(i % 10, 0f, i / 10);
            if (bodies)
            {
                go.AddComponent<BoxCollider>().size = Vector3.one * 0.8f;
                go.AddComponent<Rigidbody>();
            }
            go.AddComponent<NetworkTransform>();
        }

        manager.prefabProvider.AddRuntimePrefab(_groupPrefab.name, _groupPrefab);
    }

    protected override async UniTask Spawn(ScenarioContext ctx)
    {
        int groups = groupCount;

        if (ctx.isServer)
        {
            SpawnSuppressed(() =>
            {
                for (int g = 0; g < groups; g++)
                {
                    var inst = Instantiate(_groupPrefab, new Vector3(0f, 0f, g * 12f), Quaternion.identity);
                    inst.SetActive(true);
                    _groups.Add(inst);
                }
            });
        }
        else
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(() => CountSpawned() >= groups * _perGroup,
                    SPAWN_READY_TIMEOUT, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                Debug.LogError($"[StaticTransformsBenchmark] only {CountSpawned()}/{groups * _perGroup} transforms spawned");
            }
        }
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
    }

    protected override int ObjectCount(ScenarioContext ctx) => ctx.isServer ? CountSpawned() : groupCount * _perGroup;

    protected override void Despawn(ScenarioContext ctx)
    {
        if (!ctx.isServer)
            return;

        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i])
                Destroy(_groups[i]);
        }

        _groups.Clear();
    }

    private static int CountSpawned()
    {
        int count = 0;
        var all = FindObjectsByType<NetworkTransform>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].isSpawned && all[i].name == NT_NAME)
                count++;
        }

        return count;
    }
}

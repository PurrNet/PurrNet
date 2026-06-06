# Annotated scenario template

Copy these three files, rename `Foo` → your scenario name, and adapt the prefab shape + assertions.
This mirrors `HierarchyRespawnScenario` (a server-driven spawn/despawn cycle over a multi-child
prefab). Files go in `Assets/PlayModeTests/Scripts/LifecycleTests/` (or a sibling category folder).

## FooChild.cs

```csharp
using System.Collections.Generic;
using PurrNet;

// Own type + own statics so it never shares live-set state with another scenario (all scenarios are
// Setup()-ed once up front).
public class FooChild : NetworkIdentity
{
    public bool isDisposable;                       // mark leaves the scenario will destroy mid-test

    static readonly HashSet<NetworkID> _alive = new();   // keyed by id => host double-callbacks dedupe

    public static int AliveCount => _alive.Count;
    public static bool SawBadId;                    // tripped if a child ever spawns at Server:0

    NetworkID? _trackedId;

    public static void ResetAll()                   // called from CreatePrefab(), never RunScenario()
    {
        _alive.Clear();
        SawBadId = false;
    }

    protected override void OnSpawned()             // no-arg => fires once per instance
    {
        if (!id.HasValue || id.Value == default)    // default NetworkID == Server:0 == unassigned
        {
            SawBadId = true;
            return;
        }
        _trackedId = id.Value;
        _alive.Add(id.Value);
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue) _alive.Remove(_trackedId.Value);
        _trackedId = null;
    }
}
```

## FooRoot.cs

```csharp
using PurrNet;
using UnityEngine;

public class FooRoot : NetworkIdentity
{
    public static FooRoot LocalInstance;

    public static void ResetAll() => LocalInstance = null;

    // The prefab root is built SetActive(false); activate on spawn so children come alive.
    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => LocalInstance = this;

    protected override void OnDespawned()
    {
        if (LocalInstance == this) LocalInstance = null;   // guard: don't null a newer instance
    }

    // Server-side helper, if the test destroys specific children mid-life.
    public void DestroyDisposableChildren()
    {
        var children = GetComponentsInChildren<FooChild>(true);
        for (int i = 0; i < children.Length; i++)
            if (children[i].isDisposable)
                Destroy(children[i].gameObject);
    }
}
```

## FooScenario.cs

```csharp
using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// One sentence on what this proves, and why a red here is a real bug.
public class FooScenario : Scenario
{
    [SerializeField] private int _cycles = 3;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5500;          // unique across scenarios (grep existing ones)
    private const int ExpectedChildren = 5;

    private FooRoot _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(FooRoot));
        _prefab = rootGo.AddComponent<FooRoot>();

        var a = AddChild(rootGo, "A");  AddChild(a, "A1");
        var b = AddChild(rootGo, "B");  AddChild(b, "B1");
        AddChild(rootGo, "Disposable").GetComponent<FooChild>().isDisposable = true;

        rootGo.SetActive(false);       // root inactive; OnEarlySpawn re-activates it
        FooRoot.ResetAll();            // reset statics HERE, not in RunScenario
        FooChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform);
        go.AddComponent<FooChild>();
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
            FooRoot instance = null;

            if (ctx.isServer) instance = Instantiate(_prefab);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => FooRoot.LocalInstance != null && FooChild.AliveCount == ExpectedChildren,
                    _spawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: spawn incomplete: root={FooRoot.LocalInstance != null}, " +
                    $"children={FooChild.AliveCount}/{ExpectedChildren}, badId={FooChild.SawBadId}");
            }

            if (FooChild.SawBadId)
                return ScenarioResult.Fail($"cycle {cycle}: a child spawned with a default/Server:0 id");

            await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

            if (ctx.isServer && instance) instance.Despawn();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => FooRoot.LocalInstance == null && FooChild.AliveCount == 0,
                    _despawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"cycle {cycle}: despawn incomplete: root={FooRoot.LocalInstance != null}, " +
                    $"children={FooChild.AliveCount}");
            }

            await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
        }

        return ScenarioResult.Ok($"{_cycles} cycles, full child set each time");
    }
}
```

## Variations

- **Client-driven spawn / RPC permission:** add `[SerializeField] private NetworkRules _rules;`,
  apply it to the prefab + `manager.SetNetworkRules(_rules)` in `Setup`, server-pick a non-host
  spawner and broadcast its id (`[ObserversRpc(bufferLast:true)]`). See `DestroyDuringSpawnScenario`.
- **Per-role split logic:** use `Scenario.RunSplit(ctx, clientFn, serverFn)` instead of inline
  `ctx.isServer` branches when client and server do genuinely different things concurrently.
- **Exact-count lifecycle assertions:** on host a callback fires twice; derive expected counts from
  `ctx.role` (host = server-side + client-side), don't assume 1. See `HierarchyDespawnScenario`.
- **Deferred server-side events** (e.g. `OnObserverAdded`) flush next-frame — assert them with
  `WaitWithTimeout`, not immediately.

## .meta files

If Unity isn't running, write each `<File>.cs.meta` as:

```
fileFormatVersion: 2
guid: <32 hex chars, unique>
```

Unity will fill in the `MonoImporter` block on import; a minimal meta is valid.

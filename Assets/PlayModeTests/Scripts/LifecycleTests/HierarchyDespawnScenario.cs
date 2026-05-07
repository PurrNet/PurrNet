using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class HierarchyDespawnScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;

    private HierarchyDespawnParent _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(HierarchyDespawnScenario));
        _prefab = rootGo.AddComponent<HierarchyDespawnParent>();

        var childGo = new GameObject("Child");
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<HierarchyDespawnChild>();

        rootGo.SetActive(false);
        HierarchyDespawnParent.ResetAll();
        HierarchyDespawnChild.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HierarchyDespawnParent.LocalInstance != null
                      && HierarchyDespawnChild.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"hierarchy spawn timeout: parent={HierarchyDespawnParent.LocalInstance != null}, " +
                $"child={HierarchyDespawnChild.LocalInstance != null}");
        }

        if (ctx.isClient)
            HierarchyDespawnParent.LocalInstance.SignalReady();

        var failures = new List<string>();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => HierarchyDespawnParent.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"server-ready timeout: got {HierarchyDespawnParent.ServerReadyCount}/{ctx.expectedConnections}");
            }

            // Despawn only the parent. The child must despawn as part of the cascade — that's
            // the contract this scenario validates.
            HierarchyDespawnParent.LocalInstance.Despawn();
        }

        bool host = ctx.role == NetworkRole.Host;
        bool serverOnly = ctx.role == NetworkRole.Server;
        bool clientOnly = ctx.role == NetworkRole.Client;

        int expectedServer = host || serverOnly ? 1 : 0;
        int expectedClient = host || clientOnly ? 1 : 0;
        int expectedNoArg = 1;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HierarchyDespawnChild.OnDespawnedServer >= expectedServer
                      && HierarchyDespawnChild.OnDespawnedClient >= expectedClient
                      && HierarchyDespawnChild.OnDespawnedNoArg >= expectedNoArg
                      && HierarchyDespawnParent.OnDespawnedNoArg >= expectedNoArg,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"child-despawn timeout: child noArg={HierarchyDespawnChild.OnDespawnedNoArg}/{expectedNoArg}, " +
                $"asServer={HierarchyDespawnChild.OnDespawnedServer}/{expectedServer}, " +
                $"asClient={HierarchyDespawnChild.OnDespawnedClient}/{expectedClient}, " +
                $"parent noArg={HierarchyDespawnParent.OnDespawnedNoArg}/{expectedNoArg}");
        }

        if (HierarchyDespawnChild.OnDespawnedServer != expectedServer)
            failures.Add(
                $"child OnDespawned(asServer:true) expected {expectedServer}, got {HierarchyDespawnChild.OnDespawnedServer}");
        if (HierarchyDespawnChild.OnDespawnedClient != expectedClient)
            failures.Add(
                $"child OnDespawned(asServer:false) expected {expectedClient}, got {HierarchyDespawnChild.OnDespawnedClient}");
        if (HierarchyDespawnChild.OnDespawnedNoArg != expectedNoArg)
            failures.Add(
                $"child OnDespawned() expected {expectedNoArg}, got {HierarchyDespawnChild.OnDespawnedNoArg}");
        if (HierarchyDespawnParent.OnDespawnedNoArg != expectedNoArg)
            failures.Add(
                $"parent OnDespawned() expected {expectedNoArg}, got {HierarchyDespawnParent.OnDespawnedNoArg}");

        if (!HierarchyDespawnChild.ChildSawParentBeforeAnyCallback)
            failures.Add("child OnSpawned ran without parent.LocalInstance set — parent must spawn before child");

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

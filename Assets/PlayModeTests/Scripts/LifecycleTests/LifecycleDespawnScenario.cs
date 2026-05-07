using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class LifecycleDespawnScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;

    private LifecycleDespawnIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(LifecycleDespawnScenario));
        _prefab = go.AddComponent<LifecycleDespawnIdentity>();
        go.SetActive(false);
        LifecycleDespawnIdentity.ResetAll();
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

        await UniTaskUtils.WaitWithTimeout(
            () => LifecycleDespawnIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            LifecycleDespawnIdentity.LocalInstance.SignalReady();

        var failures = new List<string>();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => LifecycleDespawnIdentity.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"server-ready timeout: got {LifecycleDespawnIdentity.ServerReadyCount}/{ctx.expectedConnections}");
            }

            LifecycleDespawnIdentity.LocalInstance.Despawn();
        }

        bool host = ctx.role == NetworkRole.Host;
        bool serverOnly = ctx.role == NetworkRole.Server;
        bool clientOnly = ctx.role == NetworkRole.Client;

        int expectedServer = host || serverOnly ? 1 : 0;
        int expectedClient = host || clientOnly ? 1 : 0;
        // OnDespawned() (no-arg) fires once per peer regardless of host mode.
        int expectedNoArg = 1;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LifecycleDespawnIdentity.OnDespawnedServer >= expectedServer
                      && LifecycleDespawnIdentity.OnDespawnedClient >= expectedClient
                      && LifecycleDespawnIdentity.OnDespawnedNoArg >= expectedNoArg,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"despawn-callbacks timeout: noArg={LifecycleDespawnIdentity.OnDespawnedNoArg}/{expectedNoArg}, " +
                $"asServer={LifecycleDespawnIdentity.OnDespawnedServer}/{expectedServer}, " +
                $"asClient={LifecycleDespawnIdentity.OnDespawnedClient}/{expectedClient}");
        }

        if (LifecycleDespawnIdentity.OnDespawnedServer != expectedServer)
            failures.Add($"OnDespawned(asServer:true) expected {expectedServer}, got {LifecycleDespawnIdentity.OnDespawnedServer}");
        if (LifecycleDespawnIdentity.OnDespawnedClient != expectedClient)
            failures.Add($"OnDespawned(asServer:false) expected {expectedClient}, got {LifecycleDespawnIdentity.OnDespawnedClient}");
        if (LifecycleDespawnIdentity.OnDespawnedNoArg != expectedNoArg)
            failures.Add($"OnDespawned() expected {expectedNoArg}, got {LifecycleDespawnIdentity.OnDespawnedNoArg}");

        // The no-arg variant must fire after at least one asServer overload has fired on this peer.
        bool sawAsServerOrClient = LifecycleDespawnIdentity.OnDespawnedServer > 0 ||
                                   LifecycleDespawnIdentity.OnDespawnedClient > 0;
        if (LifecycleDespawnIdentity.OnDespawnedNoArg == 1 && !sawAsServerOrClient)
            failures.Add("OnDespawned() fired without any OnDespawned(asServer) firing first");

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

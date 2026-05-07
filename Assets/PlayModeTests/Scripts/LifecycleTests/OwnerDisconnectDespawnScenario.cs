using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class OwnerDisconnectDespawnScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1.0f;

    private OwnerDisconnectDespawnIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(OwnerDisconnectDespawnScenario));
        _prefab = go.AddComponent<OwnerDisconnectDespawnIdentity>();
        go.SetActive(false);
        // Intentionally use default network rules: despawnIfOwnerDisconnects = true.
        OwnerDisconnectDespawnIdentity.ResetAll();
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
            () => OwnerDisconnectDespawnIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            OwnerDisconnectDespawnIdentity.LocalInstance.SignalReady();

        if (ctx.isServer)
            return await RunAsServer(ctx);

        return await RunAsClient(ctx);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = OwnerDisconnectDespawnIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectDespawnIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {OwnerDisconnectDespawnIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to act as the disconnecting owner");

        var victimId = victim.Value.id.value;
        inst.GiveOwnership(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        inst.BroadcastVictim(victimId);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectDespawnIdentity.DisconnectCalls.Contains(victimId),
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"OnOwnerDisconnected for victim {victimId} did not fire within {_disconnectTimeoutSeconds}s");
        }

        // Default rules: despawnIfOwnerDisconnects=true, so the identity should despawn server-side
        // immediately after the owner disconnects.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectDespawnIdentity.OnDespawnedServer >= 1
                      && OwnerDisconnectDespawnIdentity.OnDespawnedNoArg >= 1,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-despawn timeout: noArg={OwnerDisconnectDespawnIdentity.OnDespawnedNoArg}, asServer={OwnerDisconnectDespawnIdentity.OnDespawnedServer}");
        }

        // OnOwnerReconnected must NOT fire — the identity was destroyed before victim returned.
        if (OwnerDisconnectDespawnIdentity.ReconnectCalls.Count != 0)
            failures.Add($"OnOwnerReconnected fired unexpectedly (count={OwnerDisconnectDespawnIdentity.ReconnectCalls.Count}); identity should have been despawned");

        // Wait for the victim to return so the inter-scenario barrier doesn't strand.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsPlayerConnected(ctx, victimId),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"victim {victimId} did not reconnect within {_reconnectTimeoutSeconds}s");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}, despawnNoArg={OwnerDisconnectDespawnIdentity.OnDespawnedNoArg}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectDespawnIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastVictim");
        }

        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == OwnerDisconnectDespawnIdentity.VictimPlayerId;

        if (isVictim)
        {
            await PerformDisconnectReconnect(ctx);
            // After reconnect, the identity has been globally despawned; we should NOT see it
            // re-appear. We deliberately don't validate despawn counters here — the local instance
            // was torn down as part of the disconnect, and counters may include disconnect-time fires.
        }
        else
        {
            // Bystander: we should observe a single OnDespawned via the default-rule despawn that
            // the server triggers when the owner disconnects.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => OwnerDisconnectDespawnIdentity.OnDespawnedClient >= 1
                          && OwnerDisconnectDespawnIdentity.OnDespawnedNoArg >= 1,
                    _despawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"bystander despawn timeout: noArg={OwnerDisconnectDespawnIdentity.OnDespawnedNoArg}, asClient={OwnerDisconnectDespawnIdentity.OnDespawnedClient}");
            }

            if (OwnerDisconnectDespawnIdentity.OnDespawnedClient != 1)
                failures.Add($"bystander OnDespawned(asServer:false) expected 1, got {OwnerDisconnectDespawnIdentity.OnDespawnedClient}");
            if (OwnerDisconnectDespawnIdentity.OnDespawnedNoArg != 1)
                failures.Add($"bystander OnDespawned() expected 1, got {OwnerDisconnectDespawnIdentity.OnDespawnedNoArg}");
            if (OwnerDisconnectDespawnIdentity.ReconnectCalls.Count != 0)
                failures.Add($"bystander OnOwnerReconnected fired unexpectedly (count={OwnerDisconnectDespawnIdentity.ReconnectCalls.Count})");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "bystander")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask PerformDisconnectReconnect(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;

        manager.StopClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.clientState == ConnectionState.Disconnected,
            _disconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTask.WaitForSeconds(_stayDisconnectedSeconds, cancellationToken: ctx.cancellationToken);

        manager.StartClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.isClient && manager.isLocalPlayerReady,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);
    }

    private static PlayerID? PickVictim(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;

            if (!best.HasValue || p.id.value < best.Value.id.value)
                best = p;
        }

        return best;
    }

    private static bool IsPlayerConnected(ScenarioContext ctx, ulong playerId)
    {
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
            if (players[i].id.value == playerId) return true;
        return false;
    }
}

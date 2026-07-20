using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class OwnerDisconnectScenario : Scenario
{
    [Tooltip("NetworkRules asset used for the runtime prefab. Must have despawnIfOwnerDisconnects=false " +
             "so the identity survives the disconnect and OnOwnerReconnected can fire.")]
    [SerializeField] private NetworkRules _rules;

    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1.0f;

    private OwnerDisconnectIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(OwnerDisconnectScenario));
        _prefab = go.AddComponent<OwnerDisconnectIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[OwnerDisconnectScenario] _rules is not assigned; the default manager rules likely have " +
                           "despawnIfOwnerDisconnects=true and OnOwnerReconnected will never fire.");

        OwnerDisconnectIdentity.ResetAll();
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
            () => OwnerDisconnectIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            OwnerDisconnectIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = OwnerDisconnectIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {OwnerDisconnectIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to act as the disconnecting owner");

        var victimId = victim.Value.id.value;
        inst.GiveOwnership(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        // Owner is assigned and connected: the cached hasConnectedOwner must reflect that.
        if (!inst.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=false right after ownership was given to a connected player");

        // Tell every observer (including the victim) who is disconnecting.
        inst.BroadcastVictim(victimId);

        // Wait for OnOwnerDisconnected to fire for the victim.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.DisconnectCalls.Contains(victimId),
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"OnOwnerDisconnected for victim {victimId} did not fire within {_disconnectTimeoutSeconds}s");
        }

        // Identity survives the disconnect (despawnIfOwnerDisconnects=false); its cached
        // hasConnectedOwner must have flipped to false now that the owner is gone.
        if (inst && inst.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=true while the owner is disconnected");

        // Wait for the victim to come back and signal it returned.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.VictimReturnedCount >= 1
                      && OwnerDisconnectIdentity.ReconnectCalls.Contains(victimId),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"victim {victimId} did not return / OnOwnerReconnected did not fire within {_reconnectTimeoutSeconds}s (returned={OwnerDisconnectIdentity.VictimReturnedCount}, reconnectCalls=[{string.Join(",", OwnerDisconnectIdentity.ReconnectCalls)}])");
        }

        // Owner is back: the cached hasConnectedOwner must have flipped to true again.
        if (inst && !inst.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=false after the owner reconnected");

        // Tell every client (and host's client side) that the disconnect/reconnect phase is done.
        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {OwnerDisconnectIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        // Server-only: callback fires once (server side). Host: fires twice (server + host's client side
        // both run in this process, each side's GlobalOwnershipModule re-triggers the identity).
        int expectedCallbacks = ctx.role == NetworkRole.Host ? 2 : 1;

        int disconnectsForVictim = 0;
        for (int i = 0; i < OwnerDisconnectIdentity.DisconnectCalls.Count; i++)
            if (OwnerDisconnectIdentity.DisconnectCalls[i] == victimId) disconnectsForVictim++;

        int reconnectsForVictim = 0;
        for (int i = 0; i < OwnerDisconnectIdentity.ReconnectCalls.Count; i++)
            if (OwnerDisconnectIdentity.ReconnectCalls[i] == victimId) reconnectsForVictim++;

        if (disconnectsForVictim != expectedCallbacks)
            failures.Add($"OnOwnerDisconnected for victim {victimId}: count={disconnectsForVictim}, expected {expectedCallbacks}");
        if (reconnectsForVictim != expectedCallbacks)
            failures.Add($"OnOwnerReconnected for victim {victimId}: count={reconnectsForVictim}, expected {expectedCallbacks}");

        if (OwnerDisconnectIdentity.DisconnectCacheWrong)
            failures.Add("server: hasConnectedOwner was true inside OnOwnerDisconnected (stale cache)");
        if (OwnerDisconnectIdentity.ReconnectCacheWrong)
            failures.Add("server: hasConnectedOwner was false inside OnOwnerReconnected (stale cache)");

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}, Done={OwnerDisconnectIdentity.ServerDoneCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastVictim");
        }

        var victimId = OwnerDisconnectIdentity.VictimPlayerId;
        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == victimId;

        if (isVictim)
        {
            await PerformDisconnectReconnect(ctx);

            // After reconnect, the static LocalInstance has been re-assigned from the new spawn.
            // Tell the server we returned.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => OwnerDisconnectIdentity.LocalInstance != null,
                    _reconnectTimeoutSeconds,
                    ctx.cancellationToken);
                OwnerDisconnectIdentity.LocalInstance.SignalVictimReturned();
            }
            catch (TimeoutException)
            {
                failures.Add("post-reconnect spawn (LocalInstance) timeout");
            }
        }
        else
        {
            // Bystander: must have observed both OnOwnerDisconnected and OnOwnerReconnected for the
            // victim. The server validates its own list separately; without this check a regression
            // that fires server-side but skips the client-side callback would pass silently.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => OwnerDisconnectIdentity.DisconnectCalls.Contains(victimId)
                          && OwnerDisconnectIdentity.ReconnectCalls.Contains(victimId),
                    _reconnectTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"bystander did not observe OnOwnerDisconnected+Reconnected for victim {victimId} " +
                    $"(disconnects=[{string.Join(",", OwnerDisconnectIdentity.DisconnectCalls)}], " +
                    $"reconnects=[{string.Join(",", OwnerDisconnectIdentity.ReconnectCalls)}])");
            }

            // Client-side cache must track the owner's connection state just like the server's.
            if (OwnerDisconnectIdentity.DisconnectCacheWrong)
                failures.Add("bystander: hasConnectedOwner was true inside OnOwnerDisconnected (stale cache)");
            if (OwnerDisconnectIdentity.ReconnectCacheWrong)
                failures.Add("bystander: hasConnectedOwner was false inside OnOwnerReconnected (stale cache)");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerDisconnectIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (OwnerDisconnectIdentity.LocalInstance != null)
            OwnerDisconnectIdentity.LocalInstance.SignalDone();

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
}

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

        if (ctx.isServer)
            return await RunAsServer(ctx);

        return await RunAsClient(ctx);
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

        // Verify exactly one disconnect and one reconnect for the victim.
        int disconnectsForVictim = 0;
        for (int i = 0; i < OwnerDisconnectIdentity.DisconnectCalls.Count; i++)
            if (OwnerDisconnectIdentity.DisconnectCalls[i] == victimId) disconnectsForVictim++;

        int reconnectsForVictim = 0;
        for (int i = 0; i < OwnerDisconnectIdentity.ReconnectCalls.Count; i++)
            if (OwnerDisconnectIdentity.ReconnectCalls[i] == victimId) reconnectsForVictim++;

        if (disconnectsForVictim != 1)
            failures.Add($"OnOwnerDisconnected for victim {victimId}: count={disconnectsForVictim}, expected 1");
        if (reconnectsForVictim != 1)
            failures.Add($"OnOwnerReconnected for victim {victimId}: count={reconnectsForVictim}, expected 1");

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

        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == OwnerDisconnectIdentity.VictimPlayerId;

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
        var hostLocal = manager.isLocalPlayerReady ? manager.localPlayer : (PlayerID?)null;

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

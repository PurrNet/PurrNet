using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Owner reconnect coverage for collection SyncTypes. A client-owned identity survives owner
/// disconnect, the owner reconnects and immediately writes fresh collection state, then every peer
/// must converge to that post-reconnect state.
/// </summary>
public class SyncCollectionsOwnerReconnectScenario : Scenario
{
    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _preStateTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _finalStateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;

    private SyncCollectionsOwnerReconnectIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncCollectionsOwnerReconnectScenario));
        _prefab = go.AddComponent<SyncCollectionsOwnerReconnectIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[SyncCollectionsOwnerReconnectScenario] _rules is not assigned; the identity must survive owner disconnect.");

        SyncCollectionsOwnerReconnectIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncCollectionsOwnerReconnectIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncCollectionsOwnerReconnectIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncCollectionsOwnerReconnectIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {SyncCollectionsOwnerReconnectIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the SyncTypes");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.ServerPreConvergedCount >= ctx.expectedConnections,
                _preStateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"pre-reconnect convergence timeout: got {SyncCollectionsOwnerReconnectIdentity.ServerPreConvergedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        if (!inst.MatchesPreReconnectState())
            failures.Add($"server did not reach pre-reconnect state before disconnect: {inst.Describe()}");

        inst.BroadcastDisconnectCommand();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.VictimReturnedCount >= 1,
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"owner {owner.Value.id.value} did not reconnect and signal return within {_reconnectTimeoutSeconds}s");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesFinalState(),
                _finalStateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server did not receive post-reconnect collection state: {inst.Describe()}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {SyncCollectionsOwnerReconnectIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncCollectionsOwnerReconnectIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool isOwnerPeer = ctx.networkManager.isLocalPlayerReady
                           && ctx.networkManager.localPlayer.id.value == SyncCollectionsOwnerReconnectIdentity.OwnerId;

        if (isOwnerPeer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst != null && inst.isOwner,
                    _preStateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.RunPreReconnectState();
            }
            catch (TimeoutException)
            {
                failures.Add("owner peer never became isOwner before pre-reconnect writes");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst != null && inst.MatchesPreReconnectState(),
                _preStateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalPreConverged();
        }
        catch (TimeoutException)
        {
            failures.Add($"client did not converge to pre-reconnect state: {(inst ? inst.Describe() : "<missing>")}");
        }

        if (isOwnerPeer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SyncCollectionsOwnerReconnectIdentity.DisconnectCommandReceived,
                    _disconnectTimeoutSeconds,
                    ctx.cancellationToken);
                await PerformDisconnectReconnect(ctx);
            }
            catch (TimeoutException)
            {
                failures.Add("owner peer did not complete disconnect/reconnect phase");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsOwnerReconnectIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        inst = SyncCollectionsOwnerReconnectIdentity.LocalInstance;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst != null && inst.MatchesFinalState(),
                _finalStateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"client did not converge to post-reconnect final state: {(inst ? inst.Describe() : "<missing>")}");
        }

        if (SyncCollectionsOwnerReconnectIdentity.LocalInstance != null)
            SyncCollectionsOwnerReconnectIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(isOwnerPeer ? "owner" : "observer")
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

        SyncCollectionsOwnerReconnectIdentity.RunFinalOnNextOwnerSpawn = true;
        SyncCollectionsOwnerReconnectIdentity.FinalRanAfterReconnect = false;

        manager.StartClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.isClient && manager.isLocalPlayerReady,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTaskUtils.WaitWithTimeout(
            () => SyncCollectionsOwnerReconnectIdentity.LocalInstance != null
                  && SyncCollectionsOwnerReconnectIdentity.FinalRanAfterReconnect,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);

        SyncCollectionsOwnerReconnectIdentity.LocalInstance.SignalVictimReturned();
    }

    private static PlayerID? PickOwner(ScenarioContext ctx)
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

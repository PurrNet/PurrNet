using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class LateJoinScenario : Scenario
{
    [Tooltip("NetworkRules asset used for the runtime prefab. Must have despawnIfOwnerDisconnects=false " +
             "so the identity survives the victim's disconnect; the late-rejoining client should then " +
             "see a fresh spawn that already carries the previous owner data.")]
    [SerializeField] private NetworkRules _rules;

    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1.0f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierEnd = 4200;

    private LateJoinIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(LateJoinScenario));
        _prefab = go.AddComponent<LateJoinIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[LateJoinScenario] _rules is not assigned; default rules likely have " +
                           "despawnIfOwnerDisconnects=true and the identity will not survive the victim's disconnect.");

        LateJoinIdentity.ResetAll();
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
                () => LateJoinIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        if (ctx.isClient)
            LateJoinIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = LateJoinIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {LateJoinIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to act as the late-joiner");

        var victimId = victim.Value.id.value;
        inst.GiveOwnership(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        inst.BroadcastVictim(victimId);

        // Wait until the victim has disconnected (OnOwnerDisconnected fired server-side).
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.DisconnectCalls.Contains(victimId),
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"OnOwnerDisconnected for victim {victimId} did not fire within {_disconnectTimeoutSeconds}s");
        }

        // The identity must NOT have despawned (since the rules say despawnIfOwnerDisconnects=false).
        if (LateJoinIdentity.LocalInstance == null)
            failures.Add("server-side LocalInstance went null after victim disconnect — identity despawned unexpectedly");

        // Server must have observed the victim being removed as observer.
        bool serverSawObserverRemoved = false;
        for (int i = 0; i < LateJoinIdentity.ObserverEvents.Count; i++)
        {
            var ev = LateJoinIdentity.ObserverEvents[i];
            if (!ev.added && ev.playerId == victimId) { serverSawObserverRemoved = true; break; }
        }
        if (!serverSawObserverRemoved)
            failures.Add($"server did not record OnObserverRemoved for victim {victimId} after disconnect");

        // Wait for victim to reconnect (player back in the list with same id).
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

        // OnOwnerReconnected must have fired for the victim (same PlayerID via process cookie).
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.ReconnectCalls.Contains(victimId),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"OnOwnerReconnected for victim {victimId} did not fire within {_reconnectTimeoutSeconds}s — " +
                $"PlayerID likely changed across reconnect (cookie not preserved)");
        }

        // After reconnect, server must record OnObserverAdded for victim again (re-added as observer).
        // OnOwnerReconnected fires synchronously inside RemoteClientLoadedScene (via
        // GlobalOwnershipModule.OnPlayerLoadedScene), but the observer-add callback is queued
        // in HierarchyV2._triggerLateObserverAdded and only flushed in the next frame's
        // PreNetworkMessages → SendDelayedObserverEvents. Wait for the deferred flush.
        bool serverSawObserverReadd = false;
        int firstRemovedIdx = -1;
        int latestAddedIdx = -1;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    firstRemovedIdx = -1;
                    latestAddedIdx = -1;
                    for (int i = 0; i < LateJoinIdentity.ObserverEvents.Count; i++)
                    {
                        var ev = LateJoinIdentity.ObserverEvents[i];
                        if (ev.playerId != victimId) continue;
                        if (!ev.added && firstRemovedIdx < 0) firstRemovedIdx = i;
                        if (ev.added) latestAddedIdx = i;
                    }
                    return firstRemovedIdx >= 0 && latestAddedIdx > firstRemovedIdx;
                },
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
            serverSawObserverReadd = true;
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server did not record OnObserverAdded for victim {victimId} after their reconnect " +
                $"(removedIdx={firstRemovedIdx}, latestAddedIdx={latestAddedIdx})");
        }

        // Tell clients we're done so the bystanders/victim can finalize.
        inst.BroadcastRejoinPhase();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.ServerRejoinedAckCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"rejoin-ack timeout: got {LateJoinIdentity.ServerRejoinedAckCount}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}, observerReadd={serverSawObserverReadd}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastVictim");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        var victimId = LateJoinIdentity.VictimPlayerId;
        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == victimId;

        if (isVictim)
        {
            int spawnsBeforeDisconnect = LateJoinIdentity.Spawns.Count;
            await PerformDisconnectReconnect(ctx);

            // After reconnect, the new spawn arrives with the previous owner already attached.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => LateJoinIdentity.LocalInstance != null
                          && LateJoinIdentity.Spawns.Count > spawnsBeforeDisconnect,
                    _reconnectTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add("post-reconnect spawn (LocalInstance / Spawns) did not arrive in time");
            }

            // Validate the post-reconnect spawn record: owner data should be present immediately,
            // and isOwner / isController should be true on the OnSpawned snapshot.
            if (LateJoinIdentity.Spawns.Count > spawnsBeforeDisconnect)
            {
                var rec = LateJoinIdentity.Spawns[LateJoinIdentity.Spawns.Count - 1];
                if (rec.asServer)
                    failures.Add($"post-reconnect spawn record on victim should be asServer=false, got true");
                if (!rec.ownerHasValue)
                    failures.Add($"post-reconnect spawn on victim: owner missing — server lost owner data");
                if (rec.ownerId != victimId)
                    failures.Add(
                        $"post-reconnect spawn on victim: ownerId={rec.ownerId}, expected {victimId} (cookie should preserve PlayerID)");
                if (!rec.isOwner)
                    failures.Add($"post-reconnect spawn on victim: isOwner=false, expected true");
                if (!rec.isController)
                    failures.Add($"post-reconnect spawn on victim: isController=false, expected true");
                if (!rec.hasConnectedOwner)
                    failures.Add($"post-reconnect spawn on victim: hasConnectedOwner=false, expected true");
            }
        }
        else
        {
            // Bystander client: the original instance stays alive across the victim's disconnect.
            // OnObserverAdded/Removed are server-only callbacks (see NetworkIdentity.cs:740-766),
            // so we can't assert them from here. Verify the identity stayed alive instead.
            int expectedMaxSpawns = ctx.role == NetworkRole.Host ? 2 : 1;
            if (LateJoinIdentity.Spawns.Count > expectedMaxSpawns)
                failures.Add(
                    $"bystander recorded {LateJoinIdentity.Spawns.Count} spawns, expected at most {expectedMaxSpawns} " +
                    $"(identity should not have re-spawned across victim disconnect)");
        }

        // Wait for the server's done signal so the scenario closes cleanly.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateJoinIdentity.RejoinPhaseSignal,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastRejoinPhase");
        }

        if (LateJoinIdentity.LocalInstance != null)
            LateJoinIdentity.LocalInstance.SignalRejoinedAck();

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

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

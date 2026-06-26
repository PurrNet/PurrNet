using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Removes one observer from a server-authoritative collection identity, mutates every collection
/// while that peer is absent, then re-adds the observer and verifies full-state catch-up.
/// </summary>
public class SyncCollectionsObserverReaddScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _observerTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;

    private SyncCollectionsObserverReaddIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncCollectionsObserverReaddScenario));
        _prefab = go.AddComponent<SyncCollectionsObserverReaddIdentity>();
        go.SetActive(false);
        SyncCollectionsObserverReaddIdentity.ResetAll();
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
                () => SyncCollectionsObserverReaddIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        if (ctx.isClient)
            SyncCollectionsObserverReaddIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncCollectionsObserverReaddIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.ServerReadyCount >= ctx.expectedConnections
                      && SyncCollectionsObserverReaddIdentity.ServerSeedConvergedCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/seed timeout: ready={SyncCollectionsObserverReaddIdentity.ServerReadyCount}, " +
                $"seed={SyncCollectionsObserverReaddIdentity.ServerSeedConvergedCount}, expected={ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to re-add as observer");

        ulong victimId = victim.Value.id.value;
        inst.BroadcastVictim(victimId);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!inst.BlacklistPlayer(victim.Value))
            failures.Add($"BlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.RemovedObservers.Contains(victimId),
                _observerTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-side OnObserverRemoved({victimId}) did not fire");
        }

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        inst.RunFinalState();
        inst.SimulateStaleSerializedHashSetMirror();

        if (!inst.MatchesFinalState())
            failures.Add($"server local collections != expected final: {inst.Describe()}");

        if (!inst.RemoveBlacklistPlayer(victim.Value))
            failures.Add($"RemoveBlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: done={SyncCollectionsObserverReaddIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"victim={victimId}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.LocalInstance != null
                      && SyncCollectionsObserverReaddIdentity.LocalInstance.MatchesSeedState(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);

            SyncCollectionsObserverReaddIdentity.LocalInstance.SignalSeedConverged();
        }
        catch (TimeoutException)
        {
            var inst = SyncCollectionsObserverReaddIdentity.LocalInstance;
            failures.Add($"client did not converge to seed state: {(inst ? inst.Describe() : "<missing>")}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive victim id");
        }

        bool isVictim = SyncCollectionsObserverReaddIdentity.VictimIdReceived
                        && ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == SyncCollectionsObserverReaddIdentity.VictimPlayerId;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncCollectionsObserverReaddIdentity.LocalInstance != null
                      && SyncCollectionsObserverReaddIdentity.LocalInstance.MatchesFinalState(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var inst = SyncCollectionsObserverReaddIdentity.LocalInstance;
            failures.Add(
                $"{(isVictim ? "victim" : "observer")} did not converge to final state: " +
                $"{(inst ? inst.Describe() : "<missing>")}");
        }

        var finalInst = SyncCollectionsObserverReaddIdentity.LocalInstance;
        if (isVictim && finalInst != null && !finalInst.SawVictimCatchupChangesForEveryCollection())
        {
            failures.Add(
                "victim reached final state without catch-up callbacks for every collection: " +
                finalInst.DescribeCatchupChanges());
        }

        if (SyncCollectionsObserverReaddIdentity.LocalInstance != null)
            SyncCollectionsObserverReaddIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "observer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
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

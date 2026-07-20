using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Verifies that FlushImmediately can deliver the first real SyncVar delta. This covers both
/// server-authoritative deltas and owner-authoritative deltas sent by a newly assigned owner.
/// </summary>
public class SyncVarImmediateFlushScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncVarImmediateFlushIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarImmediateFlushScenario));
        _prefab = go.AddComponent<SyncVarImmediateFlushIdentity>();
        go.SetActive(false);
        SyncVarImmediateFlushIdentity.ResetAll();
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

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("spawn timeout");
        }

        if (ctx.isClient)
            SyncVarImmediateFlushIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarImmediateFlushIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.ServerReadyCount == ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncVarImmediateFlushIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        inst.RunServerFlush();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.ServerFlushSeenCount == ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-auth flush convergence timeout: " +
                $"{SyncVarImmediateFlushIdentity.ServerFlushSeenCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the var");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwnerFlush(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesOwnerFlush(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"owner-auth first flush did not reach server: owner={owner.Value.id.value}, server={inst.Describe()}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.OwnerFlushSeenCount == ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"owner-auth flush convergence timeout: " +
                $"{SyncVarImmediateFlushIdentity.OwnerFlushSeenCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.ServerDoneCount == ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: {SyncVarImmediateFlushIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, {inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarImmediateFlushIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesServerFlush(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalServerFlushSeen();
        }
        catch (TimeoutException)
        {
            failures.Add($"never saw server-auth first flush; got {inst.Describe()}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.OwnerFlushCommandReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive owner flush command");
        }

        bool designatedOwner = ctx.networkManager.isLocalPlayerReady
                               && ctx.networkManager.localPlayer.id.value == SyncVarImmediateFlushIdentity.OwnerId;

        if (designatedOwner)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.isOwner,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.RunOwnerFlush();
            }
            catch (TimeoutException)
            {
                failures.Add("designated owner never became isOwner before flushing");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesOwnerFlush(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalOwnerFlushSeen();
        }
        catch (TimeoutException)
        {
            failures.Add($"never saw owner-auth first flush; owner={designatedOwner}, got {inst.Describe()}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarImmediateFlushIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncVarImmediateFlushIdentity.LocalInstance != null)
            SyncVarImmediateFlushIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(designatedOwner ? "owner" : "observer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
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

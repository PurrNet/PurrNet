using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncList contract: an owning client drives every operation, the server and
/// all other observers converge to the same contents, and IsController(ownerAuth:true) is true only
/// on the owner.
/// </summary>
public class SyncListOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncListOwnerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncListOwnerAuthScenario));
        _prefab = go.AddComponent<SyncListOwnerAuthIdentity>();
        go.SetActive(false);
        SyncListOwnerAuthIdentity.ResetAll();
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
            () => SyncListOwnerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncListOwnerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncListOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListOwnerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncListOwnerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the list");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListOwnerAuthIdentity.ConvergedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"convergence timeout: converged={SyncListOwnerAuthIdentity.ConvergedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        if (!inst.MatchesFinal())
            failures.Add($"server local list != expected final: {inst.Describe()}");

        if (inst.IsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for a client-owned list");

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListOwnerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: {SyncListOwnerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncListOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListOwnerAuthIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == SyncListOwnerAuthIdentity.OwnerId;

        if (designated)
        {
            // Wait until ownership actually propagated to us, then drive the full op sequence.
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.isOwner,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.RunOwnerSequence();
            }
            catch (TimeoutException)
            {
                failures.Add("designated owner never became isOwner");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesFinal(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalConverged();
        }
        catch (TimeoutException)
        {
            failures.Add($"never converged to final; got {inst.Describe()}");
        }

        // IsController(ownerAuth:true) must be true exactly on the owner (holds on every peer).
        if (inst.IsController(true) != inst.isOwner)
            failures.Add($"IsController(true)={inst.IsController(true)} but isOwner={inst.isOwner}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListOwnerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncListOwnerAuthIdentity.LocalInstance != null)
            SyncListOwnerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(designated ? "owner" : "observer")
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

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncVar contract: an owning client drives a sequence of changes, the server
/// and all other observers converge, and IsController(ownerAuth:true) is true only on the owner.
/// </summary>
public class SyncVarOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncVarOwnerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarOwnerAuthScenario));
        _prefab = go.AddComponent<SyncVarOwnerAuthIdentity>();
        go.SetActive(false);
        SyncVarOwnerAuthIdentity.ResetAll();
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
            () => SyncVarOwnerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncVarOwnerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncVarOwnerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the var");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerAuthIdentity.ConvergedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"convergence timeout: converged={SyncVarOwnerAuthIdentity.ConvergedCount}/{ctx.expectedConnections}; server={inst.Describe()}");
        }

        if (!inst.MatchesFinal())
            failures.Add($"server local value != expected final: {inst.Describe()}");

        if (inst.IsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for a client-owned var");

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: {SyncVarOwnerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerAuthIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == SyncVarOwnerAuthIdentity.OwnerId;

        if (designated)
        {
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

        if (inst.IsController(true) != inst.isOwner)
            failures.Add($"IsController(true)={inst.IsController(true)} but isOwner={inst.isOwner}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncVarOwnerAuthIdentity.LocalInstance != null)
            SyncVarOwnerAuthIdentity.LocalInstance.SignalDone();

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

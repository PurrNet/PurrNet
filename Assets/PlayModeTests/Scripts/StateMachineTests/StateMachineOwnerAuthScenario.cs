using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class StateMachineOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private StateMachineTestIdentity _prefab;

    void CreatePrefab()
    {
        _prefab = StateMachineTestPrefabBuilder.Create(nameof(StateMachineOwnerAuthScenario), ownerAuth: true);
        StateMachineTestIdentity.ResetAll();
    }

    /// <summary>Creates and registers the owner-authoritative state machine test prefab.</summary>
    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    /// <summary>Runs state machine list mutation checks with owner authority.</summary>
    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        StateMachineTestIdentity.ResetAll();

        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => StateMachineTestIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            StateMachineTestIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = StateMachineTestIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => StateMachineTestIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {StateMachineTestIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the state machine");

        inst.GiveOwnership(owner.Value, propagateToChildren: true);
        inst.BroadcastOwner(owner.Value.id.value);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.PhaseOneMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"phase-one convergence timeout: phaseOne={StateMachineTestIdentity.PhaseOneMatchCount}/{ctx.expectedConnections}; server={inst.Describe()}");

        inst.BroadcastPhaseOneReleased();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.FinalMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"final convergence timeout: final={StateMachineTestIdentity.FinalMatchCount}/{ctx.expectedConnections}; server={inst.Describe()}");

        if (!inst.MatchesFinal())
            failures.Add($"server local state machine != expected final: {inst.Describe()}");

        if (inst.MachineIsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for a client-owned state machine");

        inst.BroadcastPhaseDone();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.ServerDoneCount >= ctx.expectedConnections,
            _doneTimeoutSeconds,
            failures,
            () => $"done timeout: done={StateMachineTestIdentity.ServerDoneCount}/{ctx.expectedConnections}");

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = StateMachineTestIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => StateMachineTestIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        var designated = ctx.networkManager.isLocalPlayerReady &&
                         ctx.networkManager.localPlayer.id.value == StateMachineTestIdentity.OwnerId;

        if (designated)
        {
            await StateMachineScenarioOps.WaitOrFail(
                ctx,
                () => inst.isOwner && inst.MachineIsController(true),
                _stateTimeoutSeconds,
                failures,
                () => "designated owner never became controller");

            if (failures.Count == 0)
            {
                await StateMachineScenarioOps.RunPhaseOne(ctx, inst, failures, _stateTimeoutSeconds);

                if (inst.MatchesPhaseOne())
                    inst.SignalPhaseOneMatched();

                await StateMachineScenarioOps.WaitOrFail(
                    ctx,
                    () => StateMachineTestIdentity.PhaseOneReleased,
                    _stateTimeoutSeconds,
                    failures,
                    () => "owner did not receive phase-one release");

                await StateMachineScenarioOps.RunFinalPhase(ctx, inst, failures, _stateTimeoutSeconds);

                if (inst.MatchesFinal())
                    inst.SignalFinalMatched();
            }
        }
        else
        {
            await StateMachineScenarioOps.WaitOrFail(
                ctx,
                inst.MatchesPhaseOne,
                _stateTimeoutSeconds,
                failures,
                () => $"never saw phase-one remap; got {inst.Describe()}");

            if (inst.MatchesPhaseOne())
                inst.SignalPhaseOneMatched();

            await StateMachineScenarioOps.WaitOrFail(
                ctx,
                inst.MatchesFinal,
                _stateTimeoutSeconds,
                failures,
                () => $"never saw final remap; got {inst.Describe()}");

            if (inst.MatchesFinal())
                inst.SignalFinalMatched();
        }

        if (inst.MachineIsController(true) != inst.isOwner)
            failures.Add($"IsController(true)={inst.MachineIsController(true)} but isOwner={inst.isOwner}");

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.PhaseDoneReceived,
            _doneTimeoutSeconds,
            failures,
            () => "client did not receive BroadcastPhaseDone");

        if (StateMachineTestIdentity.LocalInstance != null)
            StateMachineTestIdentity.LocalInstance.SignalDone();

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
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;

            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }
}

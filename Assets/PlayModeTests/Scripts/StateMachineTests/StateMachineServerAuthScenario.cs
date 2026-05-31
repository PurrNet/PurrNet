using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class StateMachineServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private StateMachineTestIdentity _prefab;

    void CreatePrefab()
    {
        _prefab = StateMachineTestPrefabBuilder.Create(nameof(StateMachineServerAuthScenario), ownerAuth: false);
        StateMachineTestIdentity.ResetAll();
    }

    /// <summary>Creates and registers the server-authoritative state machine test prefab.</summary>
    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    /// <summary>Runs state machine list mutation checks with server authority.</summary>
    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        StateMachineTestIdentity.ResetAll();

        if (ctx.isServer)
            Instantiate(_prefab);

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
                () => StateMachineTestIdentity.ServerReadyCount >= ctx.expectedConnections &&
                      StateMachineTestIdentity.InitialMatchCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/initial timeout: ready={StateMachineTestIdentity.ServerReadyCount}, " +
                $"initial={StateMachineTestIdentity.InitialMatchCount}, expected={ctx.expectedConnections}");
        }

        await StateMachineScenarioOps.RunPhaseOne(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.PhaseOneMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"phase-one convergence timeout: phaseOne={StateMachineTestIdentity.PhaseOneMatchCount}/{ctx.expectedConnections}");

        await StateMachineScenarioOps.RunFinalPhase(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.FinalMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"final convergence timeout: final={StateMachineTestIdentity.FinalMatchCount}/{ctx.expectedConnections}");

        if (!inst.MatchesFinal())
            failures.Add($"server local state machine != expected final: {inst.Describe()}");

        inst.BroadcastPhaseDone();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.ServerDoneCount >= ctx.expectedConnections,
            _doneTimeoutSeconds,
            failures,
            () => $"done timeout: done={StateMachineTestIdentity.ServerDoneCount}/{ctx.expectedConnections}");

        return failures.Count == 0
            ? ScenarioResult.Ok($"final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = StateMachineTestIdentity.LocalInstance;

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesInitial,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw initial state list; got {inst.Describe()}");

        if (failures.Count == 0)
            inst.SignalInitialMatched();

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

        if (ctx.role == NetworkRole.Client && inst.MachineIsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth state machine");

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestIdentity.PhaseDoneReceived,
            _doneTimeoutSeconds,
            failures,
            () => "client did not receive BroadcastPhaseDone");

        if (StateMachineTestIdentity.LocalInstance != null)
            StateMachineTestIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

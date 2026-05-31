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

    private StateMachineTestRig _prefab;

    void CreatePrefab()
    {
        _prefab = StateMachineTestPrefabBuilder.Create(nameof(StateMachineServerAuthScenario), ownerAuth: false);
        StateMachineTestRig.ResetAll();
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
        StateMachineTestRig.ResetAll();

        if (ctx.isServer)
            Instantiate(_prefab).gameObject.SetActive(true);

        await UniTaskUtils.WaitWithTimeout(
            () => StateMachineTestRig.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            StateMachineTestSignals.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = StateMachineTestRig.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => StateMachineTestRig.ServerReadyCount >= ctx.expectedConnections &&
                      StateMachineTestRig.InitialMatchCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/initial timeout: ready={StateMachineTestRig.ServerReadyCount}, " +
                $"initial={StateMachineTestRig.InitialMatchCount}, expected={ctx.expectedConnections}");
        }

        await StateMachineScenarioOps.RunPhaseOne(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestRig.PhaseOneMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"phase-one convergence timeout: phaseOne={StateMachineTestRig.PhaseOneMatchCount}/{ctx.expectedConnections}");

        await StateMachineScenarioOps.RunFinalPhase(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestRig.FinalMatchCount >= ctx.expectedConnections,
            _stateTimeoutSeconds,
            failures,
            () => $"final convergence timeout: final={StateMachineTestRig.FinalMatchCount}/{ctx.expectedConnections}");

        if (!inst.MatchesFinal())
            failures.Add($"server local state machine != expected final: {inst.Describe()}");

        StateMachineTestSignals.BroadcastPhaseDone();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestRig.ServerDoneCount >= ctx.expectedConnections,
            _doneTimeoutSeconds,
            failures,
            () => $"done timeout: done={StateMachineTestRig.ServerDoneCount}/{ctx.expectedConnections}");

        return failures.Count == 0
            ? ScenarioResult.Ok($"final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = StateMachineTestRig.LocalInstance;

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesInitial,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw initial state list; got {inst.Describe()}");

        if (failures.Count == 0)
            StateMachineTestSignals.SignalInitialMatched();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesPhaseOne,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw phase-one remap; got {inst.Describe()}");

        if (inst.MatchesPhaseOne())
            StateMachineTestSignals.SignalPhaseOneMatched();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesFinal,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw final remap; got {inst.Describe()}");

        if (inst.MatchesFinal())
            StateMachineTestSignals.SignalFinalMatched();

        if (ctx.role == NetworkRole.Client && inst.MachineIsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth state machine");

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestRig.PhaseDoneReceived,
            _doneTimeoutSeconds,
            failures,
            () => "client did not receive BroadcastPhaseDone");

        if (StateMachineTestRig.LocalInstance != null)
            StateMachineTestSignals.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

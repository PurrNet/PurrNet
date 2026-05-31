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

    private const int BarrierInitial = 5500;
    private const int BarrierPhaseOne = 5501;
    private const int BarrierFinal = 5502;
    private const float BarrierTimeoutSeconds = 60f;

    private StateMachineTestRig _prefab;

    void CreatePrefab()
    {
        StateMachineTestRig.ResetAll(nameof(StateMachineServerAuthScenario));
        _prefab = StateMachineTestPrefabBuilder.Create(nameof(StateMachineServerAuthScenario), ownerAuth: false);
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
        if (ctx.isServer)
            Instantiate(_prefab);

        var failures = new List<string>();

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            () => StateMachineTestRig.GetLocalInstance(_prefab.name) != null,
            _spawnTimeoutSeconds,
            failures,
            () => $"spawn timeout: role={ctx.role}, players={ctx.networkManager.playerCount}/{ctx.expectedConnections}");

        var inst = StateMachineTestRig.GetLocalInstance(_prefab.name);

        if (inst == null)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesInitial,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw initial state list; got {inst.Describe()}");

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierInitial,
            BarrierTimeoutSeconds,
            failures,
            () => $"initial barrier timeout: {inst.Describe()}");

        if (ctx.isServer)
            await StateMachineScenarioOps.RunPhaseOne(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesPhaseOne,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw phase-one remap; got {inst.Describe()}");

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierPhaseOne,
            BarrierTimeoutSeconds,
            failures,
            () => $"phase-one barrier timeout: {inst.Describe()}");

        if (ctx.isServer)
            await StateMachineScenarioOps.RunFinalPhase(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesFinal,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw final remap; got {inst.Describe()}");

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierFinal,
            BarrierTimeoutSeconds,
            failures,
            () => $"final barrier timeout: {inst.Describe()}");

        if (ctx.role == NetworkRole.Client && inst.MachineIsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth state machine");

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

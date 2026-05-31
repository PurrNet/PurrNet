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
            Instantiate(_prefab);

        await UniTaskUtils.WaitWithTimeout(
            () => StateMachineTestRig.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var failures = new List<string>();
        var inst = StateMachineTestRig.LocalInstance;

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesInitial,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw initial state list; got {inst.Describe()}");

        await ScenarioBarrier.Wait(ctx, BarrierInitial, BarrierTimeoutSeconds);

        if (ctx.isServer)
            await StateMachineScenarioOps.RunPhaseOne(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesPhaseOne,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw phase-one remap; got {inst.Describe()}");

        await ScenarioBarrier.Wait(ctx, BarrierPhaseOne, BarrierTimeoutSeconds);

        if (ctx.isServer)
            await StateMachineScenarioOps.RunFinalPhase(ctx, inst, failures, _stateTimeoutSeconds);

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesFinal,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw final remap; got {inst.Describe()}");

        await ScenarioBarrier.Wait(ctx, BarrierFinal, BarrierTimeoutSeconds);

        if (ctx.role == NetworkRole.Client && inst.MachineIsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth state machine");

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

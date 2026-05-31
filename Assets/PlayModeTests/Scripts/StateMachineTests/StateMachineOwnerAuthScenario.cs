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

    private const int BarrierReady = 5510;
    private const int BarrierInsertedCurrent = 5511;
    private const int BarrierPhaseOne = 5512;
    private const int BarrierAddedCurrent = 5513;
    private const int BarrierFinal = 5514;
    private const int BarrierExpandedBase = 5620;
    private const float BarrierTimeoutSeconds = 60f;

    private StateMachineTestRig _prefab;

    void CreatePrefab()
    {
        StateMachineTestRig.ResetAll(nameof(StateMachineOwnerAuthScenario));
        _prefab = StateMachineTestPrefabBuilder.Create(nameof(StateMachineOwnerAuthScenario), ownerAuth: true);
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
        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

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

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierReady,
            BarrierTimeoutSeconds,
            failures,
            () => $"ready barrier timeout: {inst.Describe()}");

        if (ctx.isServer)
        {
            var owner = PickOwner(ctx);
            if (!owner.HasValue)
                return ScenarioResult.Fail("no eligible non-server / non-host client to own the state machine");

            inst.GiveOwnership(owner.Value, propagateToChildren: true);
            BroadcastOwner(owner.Value.id.value);
        }

        if (ctx.isClient)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => StateMachineTestRig.OwnerIdReceived,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add("client did not receive BroadcastOwner");
            }
        }

        var designated = ctx.networkManager.isLocalPlayerReady &&
                         ctx.networkManager.localPlayer.id.value == StateMachineTestRig.OwnerId;

        if (designated)
        {
            await StateMachineScenarioOps.WaitOrFail(
                ctx,
                () => inst.machine.isOwner && inst.MachineIsController(true),
                _stateTimeoutSeconds,
                failures,
                () => "designated owner never became controller");

            if (failures.Count == 0)
            {
                inst.InsertRegressionState();

                if (!inst.SetStateToOriginalLast())
                    failures.Add("SetState to original last state returned false");
            }
        }

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesInsertedCurrent,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw inserted-current state; got {inst.Describe()}");

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierInsertedCurrent,
            BarrierTimeoutSeconds,
            failures,
            () => $"inserted-current barrier timeout: {inst.Describe()}");

        if (designated && !inst.RemoveRegressionState())
            failures.Add("RemoveState for inserted state returned false");

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

        if (designated)
        {
            inst.AddExtraState();

            if (!inst.SetStateToAdded())
                failures.Add("SetState to added state returned false");
        }

        await StateMachineScenarioOps.WaitOrFail(
            ctx,
            inst.MatchesAddedCurrent,
            _stateTimeoutSeconds,
            failures,
            () => $"never saw added-current state; got {inst.Describe()}");

        await StateMachineScenarioOps.WaitBarrierOrFail(
            ctx,
            BarrierAddedCurrent,
            BarrierTimeoutSeconds,
            failures,
            () => $"added-current barrier timeout: {inst.Describe()}");

        if (designated)
            inst.RemoveFirstState();

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

        await StateMachineScenarioOps.RunExpandedChecks(
            ctx,
            inst,
            designated,
            BarrierExpandedBase,
            _stateTimeoutSeconds,
            BarrierTimeoutSeconds,
            failures);

        if (ctx.isServer && inst.MachineIsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for a client-owned state machine");

        if (ctx.isClient && inst.MachineIsController(true) != inst.machine.isOwner)
            failures.Add($"IsController(true)={inst.MachineIsController(true)} but isOwner={inst.machine.isOwner}");

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

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private static void BroadcastOwner(ulong ownerId)
    {
        StateMachineTestRig.OwnerId = ownerId;
        StateMachineTestRig.OwnerIdReceived = true;
    }
}

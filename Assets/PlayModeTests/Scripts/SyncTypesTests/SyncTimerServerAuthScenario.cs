using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncTimer contract: the server drives Start -> Pause -> Resume -> Stop and
/// every observer converges on the matching timer state at each phase.
/// </summary>
public class SyncTimerServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncTimerServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncTimerServerAuthScenario));
        _prefab = go.AddComponent<SyncTimerServerAuthIdentity>();
        go.SetActive(false);
        SyncTimerServerAuthIdentity.ResetAll();
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

        await UniTaskUtils.WaitWithTimeout(
            () => SyncTimerServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncTimerServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncTimerServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTimerServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncTimerServerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        for (int phase = 1; phase <= SyncTimerServerAuthIdentity.PhaseCount; phase++)
        {
            inst.DoPhaseOp(phase);
            SyncTimerServerAuthIdentity.PhaseAckCount = 0;
            inst.BroadcastPhase(phase);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SyncTimerServerAuthIdentity.PhaseAckCount >= ctx.expectedConnections,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"phase {phase} (expected state {SyncTimerServerAuthIdentity.ExpectedState(phase)}) ack timeout: " +
                    $"{SyncTimerServerAuthIdentity.PhaseAckCount}/{ctx.expectedConnections}; serverState={inst.StateCode()}");
                break;
            }
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTimerServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: {SyncTimerServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncTimerServerAuthIdentity.LocalInstance;

        for (int phase = 1; phase <= SyncTimerServerAuthIdentity.PhaseCount; phase++)
        {
            int expected = SyncTimerServerAuthIdentity.ExpectedState(phase);
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SyncTimerServerAuthIdentity.CurrentPhase >= phase && inst.StateCode() == expected,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.AckPhase();
            }
            catch (TimeoutException)
            {
                failures.Add($"phase {phase}: expected state {expected}, got {inst.StateCode()}");
                break;
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTimerServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncTimerServerAuthIdentity.LocalInstance != null)
            SyncTimerServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

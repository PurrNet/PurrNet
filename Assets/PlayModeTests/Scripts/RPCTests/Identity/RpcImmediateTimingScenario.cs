using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Pins both immediate-RPC timing contracts over the real transport: each round the server
// sends, from a single Update frame, an immediate and a plain unreliable ObserversRpc. On a
// pure client the plain one must dispatch during the tick receive phase (dispatch frame must
// equal the frame recorded by an onPreTick subscriber, exact because deferred drains run in
// the onTick chain after onPreTick completes), and the immediate one must dispatch on a
// strictly earlier frame in at least one valid round (if the early lane dies they tie every
// round). Host loopback bypasses the transport, so hosts only assert delivery. A dropped
// unreliable probe voids its round; a majority of rounds must deliver both probes.
public class RpcImmediateTimingScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _roundIntervalSeconds = 0.5f;
    [SerializeField] private float _deliveryTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5790;

    private IdentityImmediateTimingRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(RpcImmediateTimingScenario));
        go.SetActive(false);
        _prefab = go.AddComponent<IdentityImmediateTimingRpcs>();
        IdentityImmediateTimingRpcs.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    static async UniTask WaitSeconds(float seconds, CancellationToken ct)
    {
        float t = 0f;
        while (t < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            t += Time.unscaledDeltaTime;
        }
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        IdentityImmediateTimingRpcs instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateTimingRpcs.localInstance != null
                      && IdentityImmediateTimingRpcs.localInstance.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={IdentityImmediateTimingRpcs.localInstance != null}");
        }

        if (ctx.isClient)
            IdentityImmediateTimingRpcs.localInstance.SignalReady();

        var result = await RunSplit(ctx, RunAsClient, RunAsServer);

        if (!result.success)
            return result;

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateTimingRpcs.localInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        return ScenarioResult.Ok(
            "immediate dispatched ahead of deferred; deferred stayed inside the tick phase");
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var inst = IdentityImmediateTimingRpcs.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateTimingRpcs.serverReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {IdentityImmediateTimingRpcs.serverReadyCount}/{ctx.expectedConnections}");
        }

        for (int round = 0; round < IdentityImmediateTimingRpcs.Rounds; round++)
        {
            inst.QueueRound(round);
            await WaitSeconds(_roundIntervalSeconds, ctx.cancellationToken);
        }

        inst.BroadcastProbesDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateTimingRpcs.serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: {IdentityImmediateTimingRpcs.serverDoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok(
            $"timing probes acknowledged by {IdentityImmediateTimingRpcs.serverDoneCount} clients");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        float probeWindow = _deliveryTimeoutSeconds
                            + IdentityImmediateTimingRpcs.Rounds * _roundIntervalSeconds;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateTimingRpcs.probesDoneReceived,
                probeWindow,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastProbesDone");
        }

        await WaitSeconds(0.5f, ctx.cancellationToken);

        IdentityImmediateTimingRpcs.CountRounds(out int valid, out int wins);

        if (valid < IdentityImmediateTimingRpcs.MinValidRounds)
            return ScenarioResult.Fail(
                $"only {valid}/{IdentityImmediateTimingRpcs.Rounds} rounds delivered both probes");

        if (!ctx.isServer)
        {
            if (IdentityImmediateTimingRpcs.tickPhaseViolations > 0)
                return ScenarioResult.Fail(
                    $"deferred RPC dispatched outside the tick phase {IdentityImmediateTimingRpcs.tickPhaseViolations} times");

            if (wins == 0)
                return ScenarioResult.Fail(
                    $"immediate never dispatched on an earlier frame than deferred across {valid} valid rounds");
        }

        IdentityImmediateTimingRpcs.localInstance.SignalDone();

        return ScenarioResult.Ok($"{wins}/{valid} rounds immediate-first");
    }
}

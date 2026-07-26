using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Proves immediate: true end to end over the real transport: an unreliable ServerRpc
// (client -> server), an unreliable ObserversRpc and an unreliable TargetRpc
// (server -> client) all flagged immediate must arrive intact, and an interleaved
// plain ReliableOrdered RPC must be unaffected by the immediate lane (immediate on a
// reliable channel is a compile error by contract). All assertions are positive
// deliveries, so they hold on host loopback as well as on pure clients; 1s resend
// loops de-flake unreliable loss.
public class RpcImmediateScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _deliveryTimeoutSeconds = 20f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 5780;

    private IdentityImmediateRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(RpcImmediateScenario));
        go.SetActive(false);
        _prefab = go.AddComponent<IdentityImmediateRpcs>();
        IdentityImmediateRpcs.ResetAll();
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
        IdentityImmediateRpcs instance = null;

        if (ctx.isServer)
            instance = Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateRpcs.localInstance != null
                      && IdentityImmediateRpcs.localInstance.isSpawned,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn incomplete: root={IdentityImmediateRpcs.localInstance != null}");
        }

        if (ctx.isClient)
            IdentityImmediateRpcs.localInstance.SignalReady();

        var result = await RunSplit(ctx, RunAsClient, RunAsServer);

        if (!result.success)
            return result;

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateRpcs.localInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        return ScenarioResult.Ok(
            "immediate RPCs delivered on all legs; interleaved reliable control arrived unaffected");
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var inst = IdentityImmediateRpcs.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateRpcs.serverReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {IdentityImmediateRpcs.serverReadyCount}/{ctx.expectedConnections}");
        }

        var observersPayload = IdentityImmediateRpcs.MakePayload(IdentityImmediateRpcs.ObserversSeed);
        var targetPayload = IdentityImmediateRpcs.MakePayload(IdentityImmediateRpcs.TargetSeed);
        var reliablePayload = IdentityImmediateRpcs.MakePayload(IdentityImmediateRpcs.ReliableSeed);

        // resend every second so a rare dropped unreliable packet can't flake the run;
        // clients each answer with SendToServerImmediate once their own checks pass
        float waited = 0f;
        while (IdentityImmediateRpcs.serverImmediateSenders.Count < ctx.expectedConnections)
        {
            if (waited >= _deliveryTimeoutSeconds)
                return ScenarioResult.Fail(
                    $"client->server immediate timeout: {IdentityImmediateRpcs.serverImmediateSenders.Count}/{ctx.expectedConnections}");

            inst.SendObserversImmediate(observersPayload);
            inst.SendReliableImmediate(reliablePayload);
            foreach (var player in IdentityImmediateRpcs.readyPlayers)
                inst.SendTargetImmediate(player, targetPayload);

            await WaitSeconds(1f, ctx.cancellationToken);
            waited += 1f;
        }

        if (IdentityImmediateRpcs.serverImmediateCorrupt)
            return ScenarioResult.Fail("client->server immediate payload arrived corrupted");

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateRpcs.serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: {IdentityImmediateRpcs.serverDoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok(
            $"immediate RPCs from {IdentityImmediateRpcs.serverImmediateSenders.Count} clients");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var inst = IdentityImmediateRpcs.localInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityImmediateRpcs.observersReceived
                      && IdentityImmediateRpcs.targetReceived
                      && IdentityImmediateRpcs.reliableReceived,
                _deliveryTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"immediate delivery timeout: observers={IdentityImmediateRpcs.observersReceived}, " +
                $"target={IdentityImmediateRpcs.targetReceived}, " +
                $"reliable={IdentityImmediateRpcs.reliableReceived}");
        }

        if (IdentityImmediateRpcs.observersCorrupt)
            return ScenarioResult.Fail("immediate observers payload arrived corrupted");

        if (IdentityImmediateRpcs.targetCorrupt)
            return ScenarioResult.Fail("immediate target payload arrived corrupted");

        if (IdentityImmediateRpcs.reliableCorrupt)
            return ScenarioResult.Fail("reliable immediate payload arrived corrupted");

        var toServerPayload = IdentityImmediateRpcs.MakePayload(IdentityImmediateRpcs.ToServerSeed);

        float waited = 0f;
        while (!IdentityImmediateRpcs.phaseDoneReceived)
        {
            if (waited >= _deliveryTimeoutSeconds)
                return ScenarioResult.Fail("client did not receive BroadcastPhaseDone");

            inst.SendToServerImmediate(toServerPayload);
            await WaitSeconds(1f, ctx.cancellationToken);
            waited += 1f;
        }

        IdentityImmediateRpcs.localInstance.SignalDone();

        return ScenarioResult.Ok();
    }
}

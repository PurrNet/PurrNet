using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class LateSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1.0f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierEnd = 4350;

    private LateSerializeIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(LateSerializeScenario));
        _prefab = go.AddComponent<LateSerializeIdentity>();
        go.SetActive(false);
        LateSerializeIdentity.ResetAll();
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

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateSerializeIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to act as the late-joiner");

        var victimId = victim.Value.id.value;
        LateSerializeIdentity.LocalInstance.BroadcastVictim(victimId);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsPlayerConnected(ctx, victimId),
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"victim {victimId} never disconnected within {_disconnectTimeoutSeconds}s");
        }

        if (LateSerializeIdentity.LocalInstance == null)
            failures.Add("server-side identity despawned after victim disconnect (should be server-owned and survive)");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsPlayerConnected(ctx, victimId),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"victim {victimId} did not reconnect within {_reconnectTimeoutSeconds}s");
        }

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (LateSerializeIdentity.LocalInstance != null)
            LateSerializeIdentity.LocalInstance.BroadcastDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateSerializeIdentity.ServerOkCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done-ack timeout: got {LateSerializeIdentity.ServerOkCount}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateSerializeIdentity.VictimIdReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastVictim");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        var victimId = LateSerializeIdentity.VictimPlayerId;
        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == victimId;

        if (isVictim)
        {
            int deserializeBefore = LateSerializeIdentity.DeserializeCount;

            await PerformDisconnectReconnect(ctx);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => LateSerializeIdentity.LocalInstance != null
                          && LateSerializeIdentity.DeserializeCount > deserializeBefore,
                    _reconnectTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"OnDeserialize did not re-run after reconnect: count={LateSerializeIdentity.DeserializeCount}, " +
                    $"before={deserializeBefore} (late observer never received fresh OnSerialize custom data)");
            }

            var inst = LateSerializeIdentity.LocalInstance;
            if (inst != null && !inst.ReadValuesMatch)
                failures.Add(
                    $"post-reconnect deserialize values mismatch: int={inst.readInt}, str='{inst.readString}'");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LateSerializeIdentity.DonePhaseSignal,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastDone");
        }

        if (LateSerializeIdentity.LocalInstance != null)
            LateSerializeIdentity.LocalInstance.SignalOk();

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "bystander")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask PerformDisconnectReconnect(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;

        manager.StopClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.clientState == ConnectionState.Disconnected,
            _disconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTask.WaitForSeconds(_stayDisconnectedSeconds, cancellationToken: ctx.cancellationToken);

        manager.StartClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.isClient && manager.isLocalPlayerReady,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);
    }

    private static PlayerID? PickVictim(ScenarioContext ctx)
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

    private static bool IsPlayerConnected(ScenarioContext ctx, ulong playerId)
    {
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
            if (players[i].id.value == playerId) return true;
        return false;
    }
}

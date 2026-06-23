using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class SpawnBufferedObserversRpcScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _observerTimeoutSeconds = 30f;
    [SerializeField] private float _reportTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _duplicateWindowSeconds = 1f;

    private SpawnBufferedObserversRpcIdentity _prefab;

    private void CreatePrefab()
    {
        var go = new GameObject(nameof(SpawnBufferedObserversRpcIdentity));
        _prefab = go.AddComponent<SpawnBufferedObserversRpcIdentity>();
        go.SetActive(false);
        SpawnBufferedObserversRpcIdentity.ResetAll();
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

        if (ctx.isClient)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SpawnBufferedObserversRpcIdentity.LocalInstance != null
                          && SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount > 0,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"client did not receive spawn-time buffered ObserversRpc: " +
                    $"instance={SpawnBufferedObserversRpcIdentity.LocalInstance != null}, " +
                    $"count={SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount}, " +
                    $"seed={SpawnBufferedObserversRpcIdentity.LocalLastSeed}");
            }

            SpawnBufferedObserversRpcIdentity.LocalInstance.SignalReady();
        }

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.ServerReadyCount >= ctx.expectedConnections
                      && SpawnBufferedObserversRpcIdentity.ServerSpawnReportCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server did not receive initial ready/spawn reports: " +
                $"ready={SpawnBufferedObserversRpcIdentity.ServerReadyCount}/{ctx.expectedConnections}, " +
                $"spawn={SpawnBufferedObserversRpcIdentity.ServerSpawnReportCount}/{ctx.expectedConnections}, " +
                $"reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]");
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

        if (!ValidateServerReports(SpawnBufferedObserversRpcIdentity.SpawnSeed, ctx.expectedConnections, "spawn-time", out var failure))
            return ScenarioResult.Fail(failure);

        var inst = SpawnBufferedObserversRpcIdentity.LocalInstance;
        if (!inst)
            return ScenarioResult.Fail("server lost spawned identity before replay phase");

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to re-add as observer");

        ulong victimId = victim.Value.id.value;
        inst.BroadcastVictim(victimId);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!inst.BlacklistPlayer(victim.Value))
            return ScenarioResult.Fail($"BlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.RemovedObservers.Contains(victimId),
                _observerTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server-side OnObserverRemoved({victimId}) did not fire before replay send");
        }

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        inst.TriggerReplay();
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!inst.RemoveBlacklistPlayer(victim.Value))
            return ScenarioResult.Fail($"RemoveBlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.ServerReplayReportCount >= ctx.expectedConnections,
                _reportTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            inst.BroadcastPhaseDone();
            return ScenarioResult.Fail(
                $"server did not receive all replay Initialize reports after re-adding victim={victimId}: " +
                $"{SpawnBufferedObserversRpcIdentity.ServerReplayReportCount}/{ctx.expectedConnections}, " +
                $"reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]");
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

        if (!ValidateServerReports(SpawnBufferedObserversRpcIdentity.ReplaySeed, ctx.expectedConnections, "replay", out failure))
        {
            inst.BroadcastPhaseDone();
            return ScenarioResult.Fail(failure);
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"done timeout: done={SpawnBufferedObserversRpcIdentity.ServerDoneCount}/{ctx.expectedConnections}, " +
                $"reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]");
        }

        return ScenarioResult.Ok(
            $"spawn and replay bufferLast ObserversRpc invoked exactly once per player; " +
            $"victim={victimId}, reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive replay victim id");
        }

        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == SpawnBufferedObserversRpcIdentity.VictimPlayerId;

        if (isVictim)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SpawnBufferedObserversRpcIdentity.LocalInstance == null,
                    _observerTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    "victim was not removed as observer before replay send; " +
                    $"spawn={SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount}, " +
                    $"replay={SpawnBufferedObserversRpcIdentity.LocalReplayReceiveCount}");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnBufferedObserversRpcIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"client did not receive replay phase completion; victim={isVictim}, " +
                $"instance={SpawnBufferedObserversRpcIdentity.LocalInstance != null}, " +
                $"spawn={SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount}, " +
                $"replay={SpawnBufferedObserversRpcIdentity.LocalReplayReceiveCount}, " +
                $"lastSeed={SpawnBufferedObserversRpcIdentity.LocalLastSeed}");
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

        if (ctx.isClient)
        {
            if (SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount != 1)
            {
                return ScenarioResult.Fail(
                    $"spawn-time buffered ObserversRpc invoked {SpawnBufferedObserversRpcIdentity.LocalSpawnReceiveCount} times locally; " +
                    $"expected exactly 1");
            }

            if (SpawnBufferedObserversRpcIdentity.LocalReplayReceiveCount != 1)
            {
                return ScenarioResult.Fail(
                    $"replay buffered ObserversRpc invoked {SpawnBufferedObserversRpcIdentity.LocalReplayReceiveCount} times locally; " +
                    $"expected exactly 1; victim={isVictim}");
            }
        }

        var inst = SpawnBufferedObserversRpcIdentity.LocalInstance;
        if (!inst)
            return ScenarioResult.Fail($"client has no identity after replay phase; victim={isVictim}");

        inst.SignalDone();

        return ScenarioResult.Ok(isVictim ? "victim replayed exactly once" : "bystander received exactly once");
    }

    private static bool ValidateServerReports(int seed, int expectedConnections, string phase, out string failure)
    {
        if (!SpawnBufferedObserversRpcIdentity.ServerHasExactlyOneReportPerPlayer(seed, expectedConnections))
        {
            failure =
                $"server saw wrong {phase} Initialize report count: " +
                $"expected exactly one report per {expectedConnections} players, " +
                $"reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]";
            return false;
        }

        if (SpawnBufferedObserversRpcIdentity.ServerSawDuplicateReport)
        {
            failure = $"server saw duplicate Initialize report during {phase}: reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]";
            return false;
        }

        if (SpawnBufferedObserversRpcIdentity.ServerSawWrongSeed)
        {
            failure = $"server saw wrong Initialize seed during {phase}: reports=[{SpawnBufferedObserversRpcIdentity.ServerReports}]";
            return false;
        }

        failure = null;
        return true;
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
}

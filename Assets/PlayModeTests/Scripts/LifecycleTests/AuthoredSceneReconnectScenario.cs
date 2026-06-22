using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class AuthoredSceneReconnectScenario : Scenario
{
    private const string SceneName = "Bootstrap";
    private const int BarrierBase = 6900;
    private const int ExpectedChildren = 1;

    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _disconnectCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _victimReturnedCount;
    private static int _doneCount;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        AuthoredSceneReconnectRoot.ResetAll();
        AuthoredSceneReconnectChild.ResetAll();
        _victimId = 0;
        _victimReceived = false;
        _disconnectCommandReceived = false;
        _phaseDoneReceived = false;
        _victimReturnedCount = 0;
        _doneCount = 0;
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var initial = await WaitForAuthoredSceneState(ctx, "initial server scene object", false, 0);
        if (!initial.success)
            return initial;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial barrier timeout: {DescribeState(ctx)}");
        }

        var root = AuthoredSceneReconnectRoot.LocalInstance;
        if (!root)
            return ScenarioResult.Fail($"server root missing before ownership assignment: {DescribeState(ctx)}");

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client for authored scene reconnect");

        var victimId = victim.Value.id.value;
        root.GiveOwnership(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!root.hasConnectedOwner)
            return ScenarioResult.Fail($"server root hasConnectedOwner=false after ownership assignment: {DescribeState(ctx)}");

        BroadcastVictim(victimId);
        BroadcastDisconnectCommand();

        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AuthoredSceneReconnectRoot.DisconnectCalls.Contains(victimId),
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server did not observe OnOwnerDisconnected({victimId}): {DescribeState(ctx)}");
        }

        if (root && root.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=true while authored scene owner is disconnected");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReturnedCount >= 1
                      && AuthoredSceneReconnectRoot.ReconnectCalls.Contains(victimId),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"victim {victimId} did not reconnect and restore authored scene object: {DescribeState(ctx)}");
        }

        var restored = await WaitForAuthoredSceneState(ctx, "post-reconnect server scene object", false, 0);
        if (!restored.success)
            failures.Add(restored.message);

        if (root && !root.hasConnectedOwner)
            failures.Add("server: hasConnectedOwner=false after authored scene owner reconnected");

        BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _doneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: got {_doneCount}/{ctx.expectedConnections}");
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add($"final barrier timeout: {DescribeState(ctx)}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var initial = await WaitForAuthoredSceneState(ctx, "initial client scene object", false, 0);
        if (!initial.success)
            return initial;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial barrier timeout: {DescribeState(ctx)}");
        }

        int rootSpawnsBeforeScenario = AuthoredSceneReconnectRoot.ClientSpawnCount;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReceived && _disconnectCommandReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client did not receive victim/disconnect command: {DescribeState(ctx)}");
        }

        var failures = new List<string>();
        bool isVictim = IsLocalVictim(ctx);

        if (isVictim)
        {
            int rootSpawnsBeforeReconnect = AuthoredSceneReconnectRoot.ClientSpawnCount;
            int childSpawnsBeforeReconnect = AuthoredSceneReconnectChild.ClientSpawnCount;

            var ownerReady = await WaitForVictimOwnership(ctx, "pre-disconnect owner state");
            if (!ownerReady.success)
                failures.Add(ownerReady.message);

            if (failures.Count == 0)
            {
                try
                {
                    await PerformDisconnectReconnect(ctx);
                }
                catch (TimeoutException e)
                {
                    failures.Add($"victim disconnect/reconnect timeout: {e.Message}; {DescribeState(ctx)}");
                }
            }

            if (failures.Count == 0)
            {
                var restored = await WaitForAuthoredSceneState(
                    ctx, "post-reconnect victim scene object", true, rootSpawnsBeforeReconnect);
                if (!restored.success)
                    failures.Add(restored.message);

                if (AuthoredSceneReconnectChild.ClientSpawnCount <= childSpawnsBeforeReconnect)
                {
                    failures.Add(
                        "post-reconnect victim scene object: authored child did not respawn " +
                        $"(spawns={AuthoredSceneReconnectChild.ClientSpawnCount}, before={childSpawnsBeforeReconnect})");
                }
            }

            if (failures.Count == 0)
            {
                var ownerRestored = await WaitForVictimOwnership(ctx, "post-reconnect owner state");
                if (!ownerRestored.success)
                    failures.Add(ownerRestored.message);
            }

            if (failures.Count == 0 && AuthoredSceneReconnectRoot.LocalInstance)
                SignalVictimReturned();
        }
        else
        {
            if (AuthoredSceneReconnectRoot.ClientSpawnCount > rootSpawnsBeforeScenario)
            {
                failures.Add(
                    "bystander authored scene root respawned during this scenario " +
                    $"(spawns={AuthoredSceneReconnectRoot.ClientSpawnCount}, before={rootSpawnsBeforeScenario}); " +
                    "only the reconnecting client should receive a fresh scene-object spawn");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"client did not receive phase done: {DescribeState(ctx)}");
        }

        var final = await WaitForAuthoredSceneState(ctx, "final client scene object", false, 0);
        if (!final.success)
            failures.Add(final.message);

        if (AuthoredSceneReconnectRoot.LocalInstance)
            SignalDone();

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add($"final barrier timeout: {DescribeState(ctx)}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim restored authored scene object" : "bystander preserved authored scene object")
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

    private async UniTask<ScenarioResult> WaitForAuthoredSceneState(
        ScenarioContext ctx, string phase, bool requireFreshClientSpawn, int rootSpawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasAuthoredSceneState(ctx, requireFreshClientSpawn, rootSpawnsBefore),
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (AuthoredSceneReconnectRoot.SawBadId || AuthoredSceneReconnectChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: authored scene identity spawned without an assigned id: {DescribeState(ctx)}");

        if (AuthoredSceneReconnectRoot.SawNonSceneObject || AuthoredSceneReconnectChild.SawNonSceneObject)
            return ScenarioResult.Fail($"{phase}: authored scene identity was not marked as a scene object: {DescribeState(ctx)}");

        var rec = ctx.isClient
            ? AuthoredSceneReconnectRoot.LastClientSpawn
            : AuthoredSceneReconnectRoot.LastServerSpawn;

        if (!rec.isSceneObject || rec.sceneName != SceneName)
        {
            return ScenarioResult.Fail(
                $"{phase}: authored root spawn record invalid: " +
                $"isSceneObject={rec.isSceneObject}, scene={rec.sceneName}, {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private bool HasAuthoredSceneState(ScenarioContext ctx, bool requireFreshClientSpawn, int rootSpawnsBefore)
    {
        if (!AuthoredSceneReconnectRoot.LocalInstance)
            return false;

        if (ctx.isServer && AuthoredSceneReconnectRoot.ServerAliveCount != 1)
            return false;

        if (ctx.isServer && AuthoredSceneReconnectChild.ServerAliveCount != ExpectedChildren)
            return false;

        if (ctx.isClient && AuthoredSceneReconnectRoot.ClientAliveCount != 1)
            return false;

        if (ctx.isClient && AuthoredSceneReconnectChild.ClientAliveCount != ExpectedChildren)
            return false;

        if (ctx.isClient && AuthoredSceneReconnectRoot.ClientSceneName != SceneName)
            return false;

        if (ctx.isServer && AuthoredSceneReconnectRoot.ServerSceneName != SceneName)
            return false;

        return !requireFreshClientSpawn || AuthoredSceneReconnectRoot.ClientSpawnCount > rootSpawnsBefore;
    }

    private async UniTask<ScenarioResult> WaitForVictimOwnership(ScenarioContext ctx, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () =>
                {
                    var root = AuthoredSceneReconnectRoot.LocalInstance;
                    return root
                           && root.owner.HasValue
                           && root.owner.Value.id.value == _victimId
                           && root.isOwner
                           && root.isController
                           && root.hasConnectedOwner;
                },
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool IsLocalVictim(ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == _victimId;
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

    private static string DescribeState(ScenarioContext ctx)
    {
        var root = AuthoredSceneReconnectRoot.LocalInstance;
        return $"role={ctx.role}, victim={_victimId}, victimReceived={_victimReceived}, " +
               $"disconnect={_disconnectCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"returned={_victimReturnedCount}, done={_doneCount}, " +
               $"serverRoots={AuthoredSceneReconnectRoot.ServerAliveCount}, " +
               $"serverChildren={AuthoredSceneReconnectChild.ServerAliveCount}/{ExpectedChildren}, " +
               $"clientRoots={AuthoredSceneReconnectRoot.ClientAliveCount}, " +
               $"clientChildren={AuthoredSceneReconnectChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"serverSpawns={AuthoredSceneReconnectRoot.ServerSpawnCount}, " +
               $"clientSpawns={AuthoredSceneReconnectRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={AuthoredSceneReconnectChild.ClientSpawnCount}, " +
               $"rootScene={(root ? root.gameObject.scene.name : "<none>")}, " +
               $"clientScene={AuthoredSceneReconnectRoot.ClientSceneName ?? "<none>"}, " +
               $"serverScene={AuthoredSceneReconnectRoot.ServerSceneName ?? "<none>"}, " +
               $"rootBadId={AuthoredSceneReconnectRoot.SawBadId}, childBadId={AuthoredSceneReconnectChild.SawBadId}, " +
               $"rootNonScene={AuthoredSceneReconnectRoot.SawNonSceneObject}, childNonScene={AuthoredSceneReconnectChild.SawNonSceneObject}, " +
               $"owner={(root && root.owner.HasValue ? root.owner.Value.id.value.ToString() : "<none>")}, " +
               $"isOwner={(root && root.isOwner)}, isController={(root && root.isController)}, " +
               $"hasConnectedOwner={(root && root.hasConnectedOwner)}, " +
               $"disconnectCalls=[{string.Join(",", AuthoredSceneReconnectRoot.DisconnectCalls)}], " +
               $"reconnectCalls=[{string.Join(",", AuthoredSceneReconnectRoot.ReconnectCalls)}]";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastDisconnectCommand()
    {
        _disconnectCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhaseDone()
    {
        _phaseDoneReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalVictimReturned(RPCInfo info = default)
    {
        _victimReturnedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _doneCount++;
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneObjectBufferedObserversRpcScenario : Scenario
{
    private const string TargetSceneName = "ServerConnectedSceneSpawnTarget";
    private const string TargetScenePath = "Assets/PlayModeTests/ServerConnectedSceneSpawnTarget.unity";
    private const int BarrierEnd = 7610;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _reportTimeoutSeconds = 30f;
    [SerializeField] private float _unloadTimeoutSeconds = 30f;
    [SerializeField] private float _duplicateWindowSeconds = 1f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static bool _modeReceived;
    private static bool _shouldRun;
    private static ulong _hostLocalPlayerId;

    private AsyncOperation _loadOperation;
    private bool _loadRequested;
    private bool _loadReturnedNull;
    private readonly Dictionary<string, int> _sentSpawnPackets = new();

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _modeReceived = false;
        _shouldRun = false;
        _hostLocalPlayerId = 0;
        _loadOperation = null;
        _loadRequested = false;
        _loadReturnedNull = false;
        _sentSpawnPackets.Clear();
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        ServerConnectedSceneSpawnRoot.ResetAll();
        ServerConnectedSceneSpawnChild.ResetAll();
        ServerConnectedSceneSpawnRoot.ClearBufferedProbeState();
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        bool shouldRun = ctx.role == NetworkRole.Host && ctx.networkManager.isLocalPlayerReady;
        ulong hostLocalPlayerId = shouldRun ? ctx.networkManager.localPlayer.id.value : 0;
        BroadcastMode(shouldRun, hostLocalPlayerId);

        if (!shouldRun)
        {
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Ok("scene-object buffered ObserverRpc scenario only runs in host mode");
        }

        ServerConnectedSceneSpawnRoot.ResetAll();
        ServerConnectedSceneSpawnChild.ResetAll();
        ServerConnectedSceneSpawnRoot.BufferedProbeEnabled = true;
        ServerConnectedSceneSpawnRoot.ClearBufferedProbeState();

        try
        {
            SubscribeSentSpawnPackets(ctx);
            RequestLoad(ctx);

            var loaded = await WaitForServerScene(ctx);
            if (!loaded.success)
                return loaded;

            var spawned = await WaitForServerSpawn(ctx);
            if (!spawned.success)
                return spawned;

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ServerConnectedSceneSpawnRoot.ServerBufferedReportCount >= ctx.expectedConnections,
                    _reportTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"server did not receive all scene-object buffered Initialize reports: " +
                    $"{ServerConnectedSceneSpawnRoot.ServerBufferedReportCount}/{ctx.expectedConnections}, " +
                    $"hostLocal={_hostLocalPlayerId}, reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");
            }

            await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

            var validated = ValidateServer(ctx);
            if (!validated.success)
                return validated;

            validated = ValidateSentSpawnPacketCount(ctx);
            if (!validated.success)
                return validated;

            return ScenarioResult.Ok(
                $"scene-object buffered ObserverRpc invoked exactly once; hostLocal={_hostLocalPlayerId}, " +
                $"reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");
        }
        finally
        {
            UnsubscribeSentSpawnPackets(ctx);
            ServerConnectedSceneSpawnRoot.BufferedProbeEnabled = false;
            RequestUnload(ctx);
            await WaitForUnloadBestEffort(ctx);
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
        }
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _modeReceived,
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("scene-object buffered RPC mode broadcast not received");
        }

        if (!_shouldRun)
        {
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Ok("scene-object buffered ObserverRpc scenario only runs in host mode");
        }

        var spawned = await WaitForClientSpawn(ctx);
        if (!spawned.success)
            return spawned;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.LocalBufferedReceiveCount > 0,
                _reportTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"client did not receive scene-object buffered ObserverRpc: " +
                $"count={ServerConnectedSceneSpawnRoot.LocalBufferedReceiveCount}, " +
                $"lastSeed={ServerConnectedSceneSpawnRoot.LocalBufferedLastSeed}, " +
                $"hostLocal={_hostLocalPlayerId}");
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

        var validated = ValidateLocal(ctx);
        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
        return validated;
    }

    private void RequestLoad(ScenarioContext ctx)
    {
        if (_loadRequested)
            return;

        _loadRequested = true;
        _loadOperation = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (_loadOperation == null)
            _loadReturnedNull = true;
    }

    private static void RequestUnload(ScenarioContext ctx)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex >= 0 && ctx.networkManager.sceneModule != null && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex))
            ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
        else
            SceneManager.UnloadSceneAsync(scene);
    }

    private async UniTask<ScenarioResult> WaitForServerScene(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"scene-object buffered target scene missing from build settings: {TargetScenePath}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _loadRequested
                      && !_loadReturnedNull
                      && _loadOperation != null
                      && _loadOperation.isDone
                      && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene-object buffered scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForServerSpawn(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.ServerAliveCount == 1
                      && ServerConnectedSceneSpawnRoot.ServerSpawnCount == 1
                      && ServerConnectedSceneSpawnRoot.ServerSceneName == TargetSceneName,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene-object buffered server spawn timeout: {DescribeState(ctx)}");
        }

        if (ServerConnectedSceneSpawnRoot.SawBadId)
            return ScenarioResult.Fail($"scene-object buffered server saw missing/default id: {DescribeState(ctx)}");

        if (ServerConnectedSceneSpawnRoot.SawNonSceneObject)
            return ScenarioResult.Fail($"scene-object buffered server saw non-scene object: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientSpawn(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.ClientAliveCount == 1
                      && ServerConnectedSceneSpawnRoot.ClientSpawnCount == 1
                      && ServerConnectedSceneSpawnRoot.ClientSceneName == TargetSceneName,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene-object buffered client spawn timeout: {DescribeState(ctx)}");
        }

        if (ServerConnectedSceneSpawnRoot.SawBadId)
            return ScenarioResult.Fail($"scene-object buffered client saw missing/default id: {DescribeState(ctx)}");

        if (ServerConnectedSceneSpawnRoot.SawNonSceneObject)
            return ScenarioResult.Fail($"scene-object buffered client saw non-scene object: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private static ScenarioResult ValidateLocal(ScenarioContext ctx)
    {
        if (ServerConnectedSceneSpawnRoot.LocalBufferedReceiveCount != 1)
        {
            return ScenarioResult.Fail(
                $"scene-object buffered ObserverRpc invoked {ServerConnectedSceneSpawnRoot.LocalBufferedReceiveCount} times locally; " +
                $"expected exactly 1; localPlayer={ctx.networkManager.localPlayer.id.value}, hostLocal={_hostLocalPlayerId}, " +
                $"lastSeed={ServerConnectedSceneSpawnRoot.LocalBufferedLastSeed}");
        }

        if (ServerConnectedSceneSpawnRoot.LocalBufferedLastSeed != ServerConnectedSceneSpawnRoot.BufferedProbeSeed)
        {
            return ScenarioResult.Fail(
                $"scene-object buffered ObserverRpc seed mismatch: expected {ServerConnectedSceneSpawnRoot.BufferedProbeSeed}, " +
                $"got {ServerConnectedSceneSpawnRoot.LocalBufferedLastSeed}");
        }

        return ScenarioResult.Ok();
    }

    private static ScenarioResult ValidateServer(ScenarioContext ctx)
    {
        if (ServerConnectedSceneSpawnRoot.ServerBufferedReportCount != ctx.expectedConnections)
        {
            return ScenarioResult.Fail(
                $"server saw wrong scene-object buffered report count: " +
                $"{ServerConnectedSceneSpawnRoot.ServerBufferedReportCount}/{ctx.expectedConnections}, " +
                $"hostLocal={_hostLocalPlayerId}, reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");
        }

        int hostLocalReports = ServerConnectedSceneSpawnRoot.BufferedReportCountForPlayer(_hostLocalPlayerId);
        if (hostLocalReports != 1)
        {
            return ScenarioResult.Fail(
                $"server saw host-local scene-object buffered report {hostLocalReports} times; expected exactly 1; " +
                $"hostLocal={_hostLocalPlayerId}, reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");
        }

        if (ServerConnectedSceneSpawnRoot.ServerSawBufferedDuplicate)
            return ScenarioResult.Fail($"server saw duplicate scene-object buffered report: reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");

        if (ServerConnectedSceneSpawnRoot.ServerSawWrongBufferedSeed)
            return ScenarioResult.Fail($"server saw wrong scene-object buffered seed: reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]");

        return ScenarioResult.Ok();
    }

    private void SubscribeSentSpawnPackets(ScenarioContext ctx)
    {
        if (ctx.networkManager.TryGetModule<HierarchyFactory>(true, out var factory))
            factory.onSentSpawnPacket += OnSentSpawnPacket;
    }

    private void UnsubscribeSentSpawnPackets(ScenarioContext ctx)
    {
        if (ctx.networkManager.TryGetModule<HierarchyFactory>(true, out var factory))
            factory.onSentSpawnPacket -= OnSentSpawnPacket;
    }

    private void OnSentSpawnPacket(PlayerID player, SceneID scene, NetworkID identity)
    {
        var key = SentSpawnPacketKey(player, scene, identity);
        _sentSpawnPackets.TryGetValue(key, out int count);
        _sentSpawnPackets[key] = count + 1;
    }

    private ScenarioResult ValidateSentSpawnPacketCount(ScenarioContext ctx)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (!scene.IsValid() || !ctx.networkManager.sceneModule.TryGetSceneID(scene, out var sceneId))
            return ScenarioResult.Fail($"scene-object buffered sent-spawn validation could not resolve scene id: {DescribeState(ctx)}");

        var key = SentSpawnPacketKey(ctx.networkManager.localPlayer, sceneId, ServerConnectedSceneSpawnRoot.ServerLastId);
        _sentSpawnPackets.TryGetValue(key, out int count);
        if (count != 1)
        {
            return ScenarioResult.Fail(
                $"scene-object buffered spawn notification count was {count}; expected exactly 1; " +
                $"key={key}, all=[{FormatSentSpawnPackets()}]");
        }

        return ScenarioResult.Ok();
    }

    private static string SentSpawnPacketKey(PlayerID player, SceneID scene, NetworkID identity) =>
        $"{player.id.value}|{scene.id}|{identity}";

    private string FormatSentSpawnPackets()
    {
        var parts = new List<string>(_sentSpawnPackets.Count);
        foreach (var pair in _sentSpawnPackets)
            parts.Add($"{pair.Key}:{pair.Value}");
        return string.Join(",", parts);
    }

    private async UniTask WaitForUnloadBestEffort(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            Debug.LogError($"scene-object buffered unload timeout: {DescribeState(ctx)}");
        }
    }

    private static int GetBuildIndex(string scenePath) => SceneUtility.GetBuildIndexByScenePath(scenePath);

    private static bool IsNetworkSceneLoaded(ScenarioContext ctx, int buildIndex)
    {
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex);
    }

    private static bool IsSceneLoaded(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static string DescribeState(ScenarioContext ctx) =>
        $"role={ctx.role}, serverState={ctx.networkManager.serverState}, clientState={ctx.networkManager.clientState}, " +
        $"networkLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}, sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
        $"serverAlive={ServerConnectedSceneSpawnRoot.ServerAliveCount}, clientAlive={ServerConnectedSceneSpawnRoot.ClientAliveCount}, " +
        $"serverBuffered={ServerConnectedSceneSpawnRoot.ServerBufferedReportCount}, localBuffered={ServerConnectedSceneSpawnRoot.LocalBufferedReceiveCount}, " +
        $"reports=[{ServerConnectedSceneSpawnRoot.BufferedReports}]";

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastMode(bool shouldRun, ulong hostLocalPlayerId)
    {
        _shouldRun = shouldRun;
        _hostLocalPlayerId = hostLocalPlayerId;
        _modeReceived = true;
    }
}

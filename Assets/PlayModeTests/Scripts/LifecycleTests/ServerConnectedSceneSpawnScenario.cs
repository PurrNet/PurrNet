using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ServerConnectedSceneSpawnScenario : Scenario
{
    private const string TargetSceneName = "ServerConnectedSceneSpawnTarget";
    private const string TargetScenePath = "Assets/PlayModeTests/ServerConnectedSceneSpawnTarget.unity";
    private const int BarrierBase = 7100;
    private const int ExpectedChildren = 1;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _unloadTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _connectionTimeoutSeconds = 20f;

    private NetworkManager _manager;
    private AsyncOperation _loadOperation;
    private bool _loadRequested;
    private bool _loadReturnedNull;
    private bool _subscribedConnectionState;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _manager = manager;
        _loadOperation = null;
        _loadRequested = false;
        _loadReturnedNull = false;
        _subscribedConnectionState = false;
        ServerConnectedSceneSpawnRoot.ResetAll();
        ServerConnectedSceneSpawnChild.ResetAll();
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        ServerConnectedSceneSpawnRoot.ResetAll();
        ServerConnectedSceneSpawnChild.ResetAll();
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private void OnServerConnectionState(ConnectionState state)
    {
        if (state != ConnectionState.Connected || _loadRequested)
            return;

        RequestLoad();
    }

    private void RequestLoad()
    {
        if (_loadRequested)
            return;

        _loadRequested = true;
        _loadOperation = _manager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (_loadOperation == null)
            _loadReturnedNull = true;
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        try
        {
            if (ctx.role == NetworkRole.Host)
            {
                var prepared = await LoadSceneBeforeHostClientReconnect(ctx);
                if (!prepared.success)
                    return prepared;
            }
            else
            {
                SubscribeConnectionState();

                if (ctx.networkManager.serverState == ConnectionState.Connected)
                    OnServerConnectionState(ConnectionState.Connected);
            }

            var loaded = await WaitForServerScene(ctx);
            if (!loaded.success)
                return loaded;

            var spawned = await WaitForServerSpawn(ctx);
            if (!spawned.success)
                return spawned;

            try
            {
                await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"server connected scene spawn barrier timeout: {DescribeState(ctx)}");
            }

            RequestUnload(ctx);

            var unloaded = await WaitForServerUnload(ctx);
            if (!unloaded.success)
                return unloaded;

            try
            {
                await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"server connected scene unload barrier timeout: {DescribeState(ctx)}");
            }

            return ScenarioResult.Ok();
        }
        finally
        {
            if (_manager && _subscribedConnectionState)
            {
                _manager.onServerConnectionState -= OnServerConnectionState;
                _subscribedConnectionState = false;
            }
        }
    }

    private void SubscribeConnectionState()
    {
        if (_subscribedConnectionState)
            return;

        _manager.onServerConnectionState += OnServerConnectionState;
        _subscribedConnectionState = true;
    }

    private async UniTask<ScenarioResult> LoadSceneBeforeHostClientReconnect(ScenarioContext ctx)
    {
        ctx.networkManager.StopClient();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected,
                _connectionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"host client did not disconnect before scene load: {DescribeState(ctx)}");
        }

        RequestLoad();

        var loaded = await WaitForServerScene(ctx);
        if (!loaded.success)
            return loaded;

        var spawned = await WaitForServerSpawn(ctx);
        if (!spawned.success)
            return spawned;

        ctx.networkManager.StartClient();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Connected,
                _connectionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"host client did not reconnect after scene load: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var spawned = await WaitForClientSpawn(ctx);
        if (!spawned.success)
            return spawned;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client connected scene spawn barrier timeout: {DescribeState(ctx)}");
        }

        var unloaded = await WaitForClientUnload(ctx);
        if (!unloaded.success)
            return unloaded;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client connected scene unload barrier timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForServerScene(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"server connected scene missing from build settings: {TargetScenePath}");

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
            return ScenarioResult.Fail($"server connected scene load timeout: {DescribeState(ctx)}");
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
                      && ServerConnectedSceneSpawnChild.ServerAliveCount == ExpectedChildren
                      && ServerConnectedSceneSpawnChild.ServerSpawnCount == ExpectedChildren
                      && ServerConnectedSceneSpawnRoot.ServerSceneName == TargetSceneName,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server connected scene server spawn timeout: {DescribeState(ctx)}");
        }

        if (ServerConnectedSceneSpawnRoot.SawBadId || ServerConnectedSceneSpawnChild.SawBadId)
            return ScenarioResult.Fail($"server connected scene server saw missing/default id: {DescribeState(ctx)}");

        if (ServerConnectedSceneSpawnRoot.SawNonSceneObject || ServerConnectedSceneSpawnChild.SawNonSceneObject)
            return ScenarioResult.Fail($"server connected scene server saw non-scene object: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientSpawn(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.ClientAliveCount == 1
                      && ServerConnectedSceneSpawnRoot.ClientSpawnCount == 1
                      && ServerConnectedSceneSpawnChild.ClientAliveCount == ExpectedChildren
                      && ServerConnectedSceneSpawnChild.ClientSpawnCount == ExpectedChildren
                      && ServerConnectedSceneSpawnRoot.ClientSceneName == TargetSceneName,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server connected scene client spawn timeout: {DescribeState(ctx)}");
        }

        if (ServerConnectedSceneSpawnRoot.SawBadId || ServerConnectedSceneSpawnChild.SawBadId)
            return ScenarioResult.Fail($"server connected scene client saw missing/default id: {DescribeState(ctx)}");

        if (ServerConnectedSceneSpawnRoot.SawNonSceneObject || ServerConnectedSceneSpawnChild.SawNonSceneObject)
            return ScenarioResult.Fail($"server connected scene client saw non-scene object: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private void RequestUnload(ScenarioContext ctx)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath)))
            ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
        else
            SceneManager.UnloadSceneAsync(scene);
    }

    private async UniTask<ScenarioResult> WaitForServerUnload(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.ServerAliveCount == 0
                      && ServerConnectedSceneSpawnChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))
                      && !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server connected scene server unload timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientUnload(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerConnectedSceneSpawnRoot.ClientAliveCount == 0
                      && ServerConnectedSceneSpawnChild.ClientAliveCount == 0
                      && !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server connected scene client unload timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
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

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, loadRequested={IsLoadRequested(ctx)}, " +
               $"sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}, " +
               $"clientRoots={ServerConnectedSceneSpawnRoot.ClientAliveCount}, " +
               $"clientChildren={ServerConnectedSceneSpawnChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={ServerConnectedSceneSpawnRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={ServerConnectedSceneSpawnChild.ClientSpawnCount}, " +
               $"clientScene={ServerConnectedSceneSpawnRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={ServerConnectedSceneSpawnRoot.ServerAliveCount}, " +
               $"serverChildren={ServerConnectedSceneSpawnChild.ServerAliveCount}/{ExpectedChildren}, " +
               $"serverRootSpawns={ServerConnectedSceneSpawnRoot.ServerSpawnCount}, " +
               $"serverChildSpawns={ServerConnectedSceneSpawnChild.ServerSpawnCount}, " +
               $"serverScene={ServerConnectedSceneSpawnRoot.ServerSceneName ?? "<none>"}, " +
               $"rootBadId={ServerConnectedSceneSpawnRoot.SawBadId}, childBadId={ServerConnectedSceneSpawnChild.SawBadId}, " +
               $"rootNonScene={ServerConnectedSceneSpawnRoot.SawNonSceneObject}, childNonScene={ServerConnectedSceneSpawnChild.SawNonSceneObject}";
    }

    private static bool IsLoadRequested(ScenarioContext ctx)
    {
        return ctx.isServer && IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath));
    }
}

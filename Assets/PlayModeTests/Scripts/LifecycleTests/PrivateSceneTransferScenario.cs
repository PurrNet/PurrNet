using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrivateSceneTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetA";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const int BarrierBase = 6500;
    private const int ExpectedChildren = 1;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _transferCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private PrivateSceneTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PrivateSceneTransferRoot));
        _prefab = rootGo.AddComponent<PrivateSceneTransferRoot>();

        var childGo = new GameObject(nameof(PrivateSceneTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<PrivateSceneTransferChild>();

        rootGo.SetActive(false);
        PrivateSceneTransferRoot.ResetAll();
        PrivateSceneTransferChild.ResetAll();
        _victimId = 0;
        _victimReceived = false;
        _transferCommandReceived = false;
        _phaseDoneReceived = false;
        _initialObservedCount = 0;
        _victimReturnedCount = 0;
        _doneCount = 0;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"private transfer: target scene missing from build settings: {TargetScenePath}");

        PrivateSceneTransferRoot instance = null;
        bool cleanupNeeded = false;

        try
        {
            var load = await LoadPrivateScene(ctx, buildIndex);
            if (!load.success) return load;
            cleanupNeeded = true;

            if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
                return ScenarioResult.Fail($"private transfer: network scene id missing after load: {DescribeState(ctx)}");

            var victim = PickNonHostClient(ctx);
            if (!victim.HasValue)
                return ScenarioResult.Fail("private transfer: no eligible non-server / non-host client");

            BroadcastVictim(victim.Value.id.value);

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            scenePlayers.AddPlayerToScene(victim.Value, sceneId);

            var loaded = await WaitForPlayerLoaded(ctx, scenePlayers, victim.Value, sceneId, "private transfer membership");
            if (!loaded.success) return loaded;

            instance = SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PrivateSceneTransferRoot.ServerAliveCount == 1
                          && PrivateSceneTransferChild.ServerAliveCount == ExpectedChildren,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"private transfer server spawn timeout: {DescribeState(ctx)}");
            }

            if (PrivateSceneTransferRoot.SawBadId || PrivateSceneTransferChild.SawBadId)
                return ScenarioResult.Fail($"private transfer server spawn saw default id: {DescribeState(ctx)}");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _initialObservedCount >= 1,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"private transfer victim did not observe initial hierarchy: {DescribeState(ctx)}");
            }

            BroadcastTransferCommand();

            var failures = string.Empty;
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _victimReturnedCount >= 1,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures = $"private transfer victim did not reconnect and restore: {DescribeState(ctx)}";
            }

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
                var message = $"private transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            var cleanup = await UnloadTarget(ctx, buildIndex, instance);
            if (!cleanup.success)
                return cleanup;

            cleanupNeeded = false;

            try
            {
                await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"private transfer cleanup barrier timeout: {DescribeState(ctx)}");
            }

            return string.IsNullOrEmpty(failures)
                ? ScenarioResult.Ok($"victim={victim.Value.id.value}")
                : ScenarioResult.Fail(failures);
        }
        finally
        {
            if (cleanupNeeded)
                await UnloadTarget(ctx, buildIndex, instance);
        }
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private transfer victim id timeout: {DescribeState(ctx)}");
        }

        bool isVictim = IsLocalVictim(ctx);

        var initial = isVictim
            ? await WaitForClientScene(ctx, TargetSceneName, "private transfer initial")
            : await VerifyBystanderExcluded(ctx, "private transfer initial");
        if (!initial.success) return initial;

        if (isVictim)
            SignalInitialObserved();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _transferCommandReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (isVictim)
        {
            int rootSpawnsBeforeTransfer = PrivateSceneTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = PrivateSceneTransferChild.ClientSpawnCount;

            ctx.networkManager.TransferToNewServer();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures = $"private transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForClientScene(ctx, TargetSceneName, "private transfer restore");
                if (!restored.success)
                    failures = restored.message;
            }

            if (string.IsNullOrEmpty(failures)
                && (PrivateSceneTransferRoot.ClientSpawnCount <= rootSpawnsBeforeTransfer ||
                    PrivateSceneTransferChild.ClientSpawnCount <= childSpawnsBeforeTransfer))
            {
                failures = "private transfer restore did not produce fresh client spawns: " +
                           $"rootSpawns={PrivateSceneTransferRoot.ClientSpawnCount} (was {rootSpawnsBeforeTransfer}), " +
                           $"childSpawns={PrivateSceneTransferChild.ClientSpawnCount} (was {childSpawnsBeforeTransfer}), " +
                           DescribeState(ctx);
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            var excluded = await VerifyBystanderExcluded(ctx, "private transfer bystander after transfer");
            if (!excluded.success)
                failures = excluded.message;
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
            var message = $"private transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        var cleanup = await WaitForClientCleanup(ctx);
        if (!cleanup.success)
            failures = string.IsNullOrEmpty(failures) ? cleanup.message : $"{failures} | {cleanup.message}";

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            var message = $"private transfer cleanup barrier timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(isVictim ? "victim private transfer restored" : "bystander excluded")
            : ScenarioResult.Fail(failures);
    }

    private PrivateSceneTransferRoot SpawnInScene(Scene targetScene)
    {
        var previous = SceneManager.GetActiveScene();
        bool changed = SceneManager.SetActiveScene(targetScene);
        try
        {
            return Instantiate(_prefab);
        }
        finally
        {
            if (changed && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }
    }

    private async UniTask<ScenarioResult> LoadPrivateScene(ScenarioContext ctx, int buildIndex)
    {
        if (IsNetworkSceneLoaded(ctx, buildIndex))
            return ScenarioResult.Ok();

        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = false
        });

        if (op == null)
            return ScenarioResult.Fail($"private transfer: LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(
        ScenarioContext ctx, int buildIndex, PrivateSceneTransferRoot instance = null)
    {
        if (instance)
            instance.Despawn();

        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            if (IsNetworkSceneLoaded(ctx, buildIndex))
                _ = ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
            else
                _ = SceneManager.UnloadSceneAsync(scene);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneTransferRoot.ServerAliveCount == 0
                      && PrivateSceneTransferChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private transfer cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForPlayerLoaded(
        ScenarioContext ctx, ScenePlayersModule scenePlayers, PlayerID player, SceneID sceneId, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => scenePlayers.IsPlayerInScene(player, sceneId)
                      && scenePlayers.IsPlayerLoadedInScene(player, sceneId),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: player did not finish loading scene: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx, string sceneName, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneTransferRoot.ClientAliveCount == 1
                      && PrivateSceneTransferChild.ClientAliveCount == ExpectedChildren
                      && PrivateSceneTransferRoot.ClientSceneName == sceneName,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (PrivateSceneTransferRoot.SawBadId || PrivateSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> VerifyBystanderExcluded(ScenarioContext ctx, string phase)
    {
        if (ctx.role == NetworkRole.Host)
            return ScenarioResult.Ok();

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        if (PrivateSceneTransferRoot.ClientAliveCount != 0 || PrivateSceneTransferChild.ClientAliveCount != 0)
            return ScenarioResult.Fail($"{phase}: bystander received private scene hierarchy: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientCleanup(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneTransferRoot.ClientAliveCount == 0
                      && PrivateSceneTransferChild.ClientAliveCount == 0,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private transfer client cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static int GetBuildIndex(string scenePath) => SceneUtility.GetBuildIndexByScenePath(scenePath);

    private static bool TryGetSceneId(ScenarioContext ctx, int buildIndex, out SceneID sceneId)
    {
        sceneId = default;
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.TryGetScene(buildIndex, out sceneId);
    }

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

    private static bool IsLocalVictim(ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == _victimId;
    }

    private static PlayerID? PickNonHostClient(ScenarioContext ctx)
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
        return $"role={ctx.role}, victim={_victimId}, victimReceived={_victimReceived}, " +
               $"transfer={_transferCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"initial={_initialObservedCount}, returned={_victimReturnedCount}, done={_doneCount}, " +
               $"client={ctx.networkManager.isClient}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"clientRoots={PrivateSceneTransferRoot.ClientAliveCount}, " +
               $"clientChildren={PrivateSceneTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={PrivateSceneTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={PrivateSceneTransferChild.ClientSpawnCount}, " +
               $"clientScene={PrivateSceneTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={PrivateSceneTransferRoot.ServerAliveCount}, " +
               $"serverChildren={PrivateSceneTransferChild.ServerAliveCount}, " +
               $"rootBadId={PrivateSceneTransferRoot.SawBadId}, childBadId={PrivateSceneTransferChild.SawBadId}, " +
               $"sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastTransferCommand()
    {
        _transferCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhaseDone()
    {
        _phaseDoneReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalInitialObserved(RPCInfo info = default)
    {
        _initialObservedCount++;
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

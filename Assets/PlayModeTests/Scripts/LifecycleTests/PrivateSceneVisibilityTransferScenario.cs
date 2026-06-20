using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrivateSceneVisibilityTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetA";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _transferCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private PrivateSceneVisibilityTransferRoot _prefab;

    private void CreatePrefab(NetworkManager manager)
    {
        var rootGo = new GameObject(nameof(PrivateSceneVisibilityTransferRoot));
        _prefab = rootGo.AddComponent<PrivateSceneVisibilityTransferRoot>();

        var hiddenRules = ScriptableObject.CreateInstance<NetworkVisibilityRuleSet>();
        hiddenRules.AddRule(manager, ScriptableObject.CreateInstance<NoVisibilityRule>());

        var childGo = new GameObject(nameof(PrivateSceneVisibilityTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        var child = childGo.AddComponent<PrivateSceneVisibilityTransferChild>();
        child.SetNetworkRules(_rules);
        child.SetVisibilityRules(hiddenRules);

        if (!_rules)
            Debug.LogError("[PrivateSceneVisibilityTransferScenario] _rules is not assigned; old host-migration visibility bypass would not be exercised.");

        rootGo.SetActive(false);
        PrivateSceneVisibilityTransferRoot.ResetAll();
        PrivateSceneVisibilityTransferChild.ResetAll();
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
        CreatePrefab(manager);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        if (!_rules)
            return ScenarioResult.Fail("visibility transfer: _rules is not assigned");

        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"visibility transfer: target scene missing from build settings: {TargetScenePath}");

        PrivateSceneVisibilityTransferRoot instance = null;
        bool cleanupNeeded = false;

        try
        {
            var load = await LoadPrivateScene(ctx, buildIndex);
            if (!load.success) return load;
            cleanupNeeded = true;

            if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
                return ScenarioResult.Fail($"visibility transfer: network scene id missing after load: {DescribeState(ctx)}");

            var victim = PickNonHostClient(ctx);
            if (!victim.HasValue)
                return ScenarioResult.Fail("visibility transfer: no eligible non-server / non-host client");

            BroadcastVictim(victim.Value.id.value);

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            scenePlayers.AddPlayerToScene(victim.Value, sceneId);

            var loaded = await WaitForPlayerLoaded(ctx, scenePlayers, victim.Value, sceneId, "visibility transfer membership");
            if (!loaded.success) return loaded;

            instance = SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PrivateSceneVisibilityTransferRoot.ServerAliveCount == 1
                          && PrivateSceneVisibilityTransferChild.ServerAliveCount == 1,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"visibility transfer server spawn timeout: {DescribeState(ctx)}");
            }

            if (PrivateSceneVisibilityTransferRoot.SawBadId || PrivateSceneVisibilityTransferChild.SawBadId)
                return ScenarioResult.Fail($"visibility transfer server spawn saw default id: {DescribeState(ctx)}");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _initialObservedCount >= 1,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"visibility transfer victim did not observe initial visible root: {DescribeState(ctx)}");
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
                failures = $"visibility transfer victim did not reconnect and restore visible root: {DescribeState(ctx)}";
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
                var message = $"visibility transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            var cleanup = await UnloadTarget(ctx, buildIndex, instance);
            if (!cleanup.success)
                return cleanup;

            cleanupNeeded = false;

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
            return ScenarioResult.Fail($"visibility transfer victim id timeout: {DescribeState(ctx)}");
        }

        bool isVictim = IsLocalVictim(ctx);

        var initial = isVictim
            ? await WaitForVisibleRootOnly(ctx, TargetSceneName, "visibility transfer initial")
            : await VerifyBystanderExcluded(ctx, "visibility transfer initial");
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
            return ScenarioResult.Fail($"visibility transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (isVictim)
        {
            int rootSpawnsBeforeTransfer = PrivateSceneVisibilityTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = PrivateSceneVisibilityTransferChild.ClientSpawnCount;

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
                failures = $"visibility transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForVisibleRootOnly(ctx, TargetSceneName, "visibility transfer restore");
                if (!restored.success)
                    failures = restored.message;
            }

            if (string.IsNullOrEmpty(failures)
                && PrivateSceneVisibilityTransferRoot.ClientSpawnCount <= rootSpawnsBeforeTransfer)
            {
                failures = "visibility transfer restore did not produce fresh root spawn: " +
                           $"rootSpawns={PrivateSceneVisibilityTransferRoot.ClientSpawnCount} (was {rootSpawnsBeforeTransfer}), " +
                           DescribeState(ctx);
            }

            if (string.IsNullOrEmpty(failures)
                && PrivateSceneVisibilityTransferChild.ClientSpawnCount != childSpawnsBeforeTransfer)
            {
                failures = "visibility transfer hidden child spawned during transfer restore: " +
                           $"childSpawns={PrivateSceneVisibilityTransferChild.ClientSpawnCount} (was {childSpawnsBeforeTransfer}), " +
                           DescribeState(ctx);
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            var excluded = await VerifyBystanderExcluded(ctx, "visibility transfer bystander after transfer");
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
            var message = $"visibility transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        var cleanup = await WaitForClientCleanup(ctx);
        if (!cleanup.success)
            failures = string.IsNullOrEmpty(failures) ? cleanup.message : $"{failures} | {cleanup.message}";

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(isVictim ? "victim saw root only" : "bystander excluded")
            : ScenarioResult.Fail(failures);
    }

    private PrivateSceneVisibilityTransferRoot SpawnInScene(Scene targetScene)
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
            return ScenarioResult.Fail($"visibility transfer: LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"visibility transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(
        ScenarioContext ctx, int buildIndex, PrivateSceneVisibilityTransferRoot instance = null)
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
                () => PrivateSceneVisibilityTransferRoot.ServerAliveCount == 0
                      && PrivateSceneVisibilityTransferChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"visibility transfer cleanup timeout: {DescribeState(ctx)}");
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

    private async UniTask<ScenarioResult> WaitForVisibleRootOnly(
        ScenarioContext ctx, string sceneName, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneVisibilityTransferRoot.ClientAliveCount == 1
                      && PrivateSceneVisibilityTransferChild.ClientAliveCount == 0
                      && PrivateSceneVisibilityTransferRoot.ClientSceneName == sceneName,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (PrivateSceneVisibilityTransferRoot.SawBadId || PrivateSceneVisibilityTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        if (PrivateSceneVisibilityTransferChild.ClientAliveCount != 0)
            return ScenarioResult.Fail($"{phase}: hidden child became visible after settling: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> VerifyBystanderExcluded(ScenarioContext ctx, string phase)
    {
        if (ctx.role == NetworkRole.Host)
            return ScenarioResult.Ok();

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        if (PrivateSceneVisibilityTransferRoot.ClientAliveCount != 0 ||
            PrivateSceneVisibilityTransferChild.ClientAliveCount != 0)
        {
            return ScenarioResult.Fail($"{phase}: bystander received private visibility hierarchy: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientCleanup(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneVisibilityTransferRoot.ClientAliveCount == 0
                      && PrivateSceneVisibilityTransferChild.ClientAliveCount == 0,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"visibility transfer client cleanup timeout: {DescribeState(ctx)}");
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
               $"clientRoots={PrivateSceneVisibilityTransferRoot.ClientAliveCount}, " +
               $"clientChildren={PrivateSceneVisibilityTransferChild.ClientAliveCount}, " +
               $"clientRootSpawns={PrivateSceneVisibilityTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={PrivateSceneVisibilityTransferChild.ClientSpawnCount}, " +
               $"clientScene={PrivateSceneVisibilityTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={PrivateSceneVisibilityTransferRoot.ServerAliveCount}, " +
               $"serverChildren={PrivateSceneVisibilityTransferChild.ServerAliveCount}, " +
               $"rootBadId={PrivateSceneVisibilityTransferRoot.SawBadId}, " +
               $"childBadId={PrivateSceneVisibilityTransferChild.SawBadId}, " +
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

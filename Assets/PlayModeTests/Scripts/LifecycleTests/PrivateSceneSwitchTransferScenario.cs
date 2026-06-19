using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrivateSceneSwitchTransferScenario : Scenario
{
    private const string TargetSceneAName = "SceneMembershipTargetA";
    private const string TargetSceneBName = "SceneMembershipTargetB";
    private const string TargetSceneAPath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const string TargetSceneBPath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const int BarrierBase = 6600;
    private const int ExpectedChildren = 1;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _transferCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _phase;
    private static int _sceneAObservedCount;
    private static int _sceneBObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private PrivateSceneSwitchTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PrivateSceneSwitchTransferRoot));
        _prefab = rootGo.AddComponent<PrivateSceneSwitchTransferRoot>();

        var childGo = new GameObject(nameof(PrivateSceneSwitchTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<PrivateSceneSwitchTransferChild>();

        rootGo.SetActive(false);
        PrivateSceneSwitchTransferRoot.ResetAll();
        PrivateSceneSwitchTransferChild.ResetAll();
        _victimId = 0;
        _victimReceived = false;
        _transferCommandReceived = false;
        _phaseDoneReceived = false;
        _phase = 0;
        _sceneAObservedCount = 0;
        _sceneBObservedCount = 0;
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
        int buildA = GetBuildIndex(TargetSceneAPath);
        int buildB = GetBuildIndex(TargetSceneBPath);
        if (buildA < 0 || buildB < 0)
            return ScenarioResult.Fail($"private switch transfer: target scenes missing: A={buildA}, B={buildB}");

        PrivateSceneSwitchTransferRoot instanceA = null;
        PrivateSceneSwitchTransferRoot instanceB = null;
        bool cleanupNeeded = false;

        try
        {
            var loadA = await LoadPrivateScene(ctx, TargetSceneAName, buildA, "load A");
            if (!loadA.success) return loadA;
            cleanupNeeded = true;

            var loadB = await LoadPrivateScene(ctx, TargetSceneBName, buildB, "load B");
            if (!loadB.success) return loadB;

            if (!TryGetSceneId(ctx, buildA, out var sceneAId) || !TryGetSceneId(ctx, buildB, out var sceneBId))
                return ScenarioResult.Fail($"private switch transfer: network scene ids missing: {DescribeState(ctx)}");

            instanceA = SpawnInScene(SceneManager.GetSceneByName(TargetSceneAName));
            instanceB = SpawnInScene(SceneManager.GetSceneByName(TargetSceneBName));

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PrivateSceneSwitchTransferRoot.ServerAliveCount == 2
                          && PrivateSceneSwitchTransferChild.ServerAliveCount == ExpectedChildren * 2,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"private switch transfer server spawn timeout: {DescribeState(ctx)}");
            }

            if (PrivateSceneSwitchTransferRoot.SawBadId || PrivateSceneSwitchTransferChild.SawBadId)
                return ScenarioResult.Fail($"private switch transfer server spawn saw default id: {DescribeState(ctx)}");

            var victim = PickNonHostClient(ctx);
            if (!victim.HasValue)
                return ScenarioResult.Fail("private switch transfer: no eligible non-server / non-host client");

            BroadcastVictim(victim.Value.id.value);

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            scenePlayers.AddPlayerToScene(victim.Value, sceneAId);
            var loadedA = await WaitForPlayerLoaded(ctx, scenePlayers, victim.Value, sceneAId, "scene A membership");
            if (!loadedA.success) return loadedA;

            BroadcastPhase(1);
            var observedA = await WaitForObserved(ctx, () => _sceneAObservedCount >= 1, "scene A observed");
            if (!observedA.success) return observedA;

            var barrierA = await WaitAtBarrier(ctx, BarrierBase + 1, "scene A");
            if (!barrierA.success) return barrierA;

            scenePlayers.RemovePlayerFromScene(victim.Value, sceneAId);
            var removedA = await WaitForPlayerRemoved(ctx, scenePlayers, victim.Value, sceneAId, "scene A removal");
            if (!removedA.success) return removedA;

            BroadcastPhase(2);
            var barrierRemoveA = await WaitAtBarrier(ctx, BarrierBase + 2, "scene A removal");
            if (!barrierRemoveA.success) return barrierRemoveA;

            scenePlayers.AddPlayerToScene(victim.Value, sceneBId);
            var loadedB = await WaitForPlayerLoaded(ctx, scenePlayers, victim.Value, sceneBId, "scene B membership");
            if (!loadedB.success) return loadedB;

            BroadcastPhase(3);
            var observedB = await WaitForObserved(ctx, () => _sceneBObservedCount >= 1, "scene B observed");
            if (!observedB.success) return observedB;

            var barrierB = await WaitAtBarrier(ctx, BarrierBase + 3, "scene B");
            if (!barrierB.success) return barrierB;

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
                failures = $"private switch transfer victim did not reconnect and restore: {DescribeState(ctx)}";
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
                var message = $"private switch transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            var cleanup = await UnloadTargets(ctx, buildA, buildB, instanceA, instanceB);
            if (!cleanup.success)
                return cleanup;

            cleanupNeeded = false;

            var cleanupBarrier = await WaitAtBarrier(ctx, BarrierBase + 4, "cleanup");
            if (!cleanupBarrier.success) return cleanupBarrier;

            return string.IsNullOrEmpty(failures)
                ? ScenarioResult.Ok($"victim={victim.Value.id.value}")
                : ScenarioResult.Fail(failures);
        }
        finally
        {
            if (cleanupNeeded)
                await UnloadTargets(ctx, buildA, buildB, instanceA, instanceB);
        }
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReceived,
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private switch transfer victim id timeout: {DescribeState(ctx)}");
        }

        bool isVictim = IsLocalVictim(ctx);

        var phaseA = await WaitForPhase(ctx, 1, "scene A membership");
        if (!phaseA.success) return phaseA;

        var stateA = isVictim
            ? await WaitForClientScene(ctx, TargetSceneAName, "scene A membership")
            : await VerifyBystanderExcluded(ctx, "scene A membership");
        if (!stateA.success) return stateA;
        if (isVictim) SignalSceneAObserved();

        var barrierA = await WaitAtBarrier(ctx, BarrierBase + 1, "scene A");
        if (!barrierA.success) return barrierA;

        var phaseRemoveA = await WaitForPhase(ctx, 2, "scene A removal");
        if (!phaseRemoveA.success) return phaseRemoveA;

        var removedA = isVictim
            ? await WaitForClientEmpty(ctx, "scene A removal")
            : await VerifyBystanderExcluded(ctx, "scene A removal");
        if (!removedA.success) return removedA;

        var barrierRemoveA = await WaitAtBarrier(ctx, BarrierBase + 2, "scene A removal");
        if (!barrierRemoveA.success) return barrierRemoveA;

        var phaseB = await WaitForPhase(ctx, 3, "scene B membership");
        if (!phaseB.success) return phaseB;

        var stateB = isVictim
            ? await WaitForClientScene(ctx, TargetSceneBName, "scene B membership")
            : await VerifyBystanderExcluded(ctx, "scene B membership");
        if (!stateB.success) return stateB;
        if (isVictim) SignalSceneBObserved();

        var barrierB = await WaitAtBarrier(ctx, BarrierBase + 3, "scene B");
        if (!barrierB.success) return barrierB;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _transferCommandReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private switch transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (isVictim)
        {
            int rootSpawnsBeforeTransfer = PrivateSceneSwitchTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = PrivateSceneSwitchTransferChild.ClientSpawnCount;

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
                failures = $"private switch transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForClientScene(ctx, TargetSceneBName, "scene B restore");
                if (!restored.success)
                    failures = restored.message;
            }

            if (string.IsNullOrEmpty(failures)
                && (PrivateSceneSwitchTransferRoot.ClientSpawnCount <= rootSpawnsBeforeTransfer ||
                    PrivateSceneSwitchTransferChild.ClientSpawnCount <= childSpawnsBeforeTransfer))
            {
                failures = "private switch transfer restore did not produce fresh client spawns: " +
                           $"rootSpawns={PrivateSceneSwitchTransferRoot.ClientSpawnCount} (was {rootSpawnsBeforeTransfer}), " +
                           $"childSpawns={PrivateSceneSwitchTransferChild.ClientSpawnCount} (was {childSpawnsBeforeTransfer}), " +
                           DescribeState(ctx);
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            var excluded = await VerifyBystanderExcluded(ctx, "private switch transfer bystander after transfer");
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
            var message = $"private switch transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        var cleanup = await WaitForClientEmpty(ctx, "cleanup");
        if (!cleanup.success)
            failures = string.IsNullOrEmpty(failures) ? cleanup.message : $"{failures} | {cleanup.message}";

        var cleanupBarrier = await WaitAtBarrier(ctx, BarrierBase + 4, "cleanup");
        if (!cleanupBarrier.success)
            failures = string.IsNullOrEmpty(failures) ? cleanupBarrier.message : $"{failures} | {cleanupBarrier.message}";

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(isVictim ? "victim private switch transfer restored" : "bystander excluded")
            : ScenarioResult.Fail(failures);
    }

    private PrivateSceneSwitchTransferRoot SpawnInScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> LoadPrivateScene(
        ScenarioContext ctx, string sceneName, int buildIndex, string phase)
    {
        if (IsNetworkSceneLoaded(ctx, buildIndex))
            return ScenarioResult.Ok();

        var op = ctx.networkManager.sceneModule.LoadSceneAsync(sceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = false
        });

        if (op == null)
            return ScenarioResult.Fail($"{phase}: LoadSceneAsync returned null for {sceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTargets(
        ScenarioContext ctx, int buildA, int buildB,
        PrivateSceneSwitchTransferRoot instanceA = null,
        PrivateSceneSwitchTransferRoot instanceB = null)
    {
        if (instanceA)
            instanceA.Despawn();
        if (instanceB)
            instanceB.Despawn();

        if (IsSceneLoaded(TargetSceneAName))
            _ = ctx.networkManager.sceneModule.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneAName));
        if (IsSceneLoaded(TargetSceneBName))
            _ = ctx.networkManager.sceneModule.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneBName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneSwitchTransferRoot.ServerAliveCount == 0
                      && PrivateSceneSwitchTransferChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildA)
                      && !IsNetworkSceneLoaded(ctx, buildB)
                      && !IsSceneLoaded(TargetSceneAName)
                      && !IsSceneLoaded(TargetSceneBName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private switch transfer cleanup timeout: {DescribeState(ctx)}");
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
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: player did not finish loading scene: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForPlayerRemoved(
        ScenarioContext ctx, ScenePlayersModule scenePlayers, PlayerID player, SceneID sceneId, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !scenePlayers.IsPlayerInScene(player, sceneId)
                      && !scenePlayers.IsPlayerLoadedInScene(player, sceneId),
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: player membership was not removed: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForObserved(
        ScenarioContext ctx, Func<bool> predicate, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(predicate, _membershipTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForPhase(ScenarioContext ctx, int phase, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phase >= phase, _membershipTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private switch transfer {label} phase timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitAtBarrier(ScenarioContext ctx, int barrierId, string phase)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"private switch transfer {phase} barrier timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx, string sceneName, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneSwitchTransferRoot.ClientAliveCount == 1
                      && PrivateSceneSwitchTransferChild.ClientAliveCount == ExpectedChildren
                      && PrivateSceneSwitchTransferRoot.ClientSceneName == sceneName,
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (PrivateSceneSwitchTransferRoot.SawBadId || PrivateSceneSwitchTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientEmpty(ScenarioContext ctx, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneSwitchTransferRoot.ClientAliveCount == 0
                      && PrivateSceneSwitchTransferChild.ClientAliveCount == 0,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> VerifyBystanderExcluded(ScenarioContext ctx, string phase)
    {
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        if (PrivateSceneSwitchTransferRoot.ClientAliveCount != 0 ||
            PrivateSceneSwitchTransferChild.ClientAliveCount != 0)
        {
            return ScenarioResult.Fail($"{phase}: bystander received private scene hierarchy: {DescribeState(ctx)}");
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
        return $"role={ctx.role}, phase={_phase}, victim={_victimId}, victimReceived={_victimReceived}, " +
               $"transfer={_transferCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"aObserved={_sceneAObservedCount}, bObserved={_sceneBObservedCount}, " +
               $"returned={_victimReturnedCount}, done={_doneCount}, " +
               $"client={ctx.networkManager.isClient}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"clientRoots={PrivateSceneSwitchTransferRoot.ClientAliveCount}, " +
               $"clientChildren={PrivateSceneSwitchTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={PrivateSceneSwitchTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={PrivateSceneSwitchTransferChild.ClientSpawnCount}, " +
               $"clientScene={PrivateSceneSwitchTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={PrivateSceneSwitchTransferRoot.ServerAliveCount}, " +
               $"serverChildren={PrivateSceneSwitchTransferChild.ServerAliveCount}, " +
               $"rootBadId={PrivateSceneSwitchTransferRoot.SawBadId}, childBadId={PrivateSceneSwitchTransferChild.SawBadId}, " +
               $"sceneA={IsSceneLoaded(TargetSceneAName)}, sceneB={IsSceneLoaded(TargetSceneBName)}, " +
               $"networkSceneA={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetSceneAPath))}, " +
               $"networkSceneB={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetSceneBPath))}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhase(int phase)
    {
        _phase = phase;
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
    private static void SignalSceneAObserved(RPCInfo info = default)
    {
        _sceneAObservedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalSceneBObserved(RPCInfo info = default)
    {
        _sceneBObservedCount++;
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

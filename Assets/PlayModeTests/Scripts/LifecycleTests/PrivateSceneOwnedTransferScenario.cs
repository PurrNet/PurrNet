using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrivateSceneOwnedTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetB";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const int BarrierBase = 6700;
    private const int ExpectedChildren = 1;

    [SerializeField] private NetworkRules _rules;
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
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private PrivateSceneOwnedTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PrivateSceneOwnedTransferRoot));
        _prefab = rootGo.AddComponent<PrivateSceneOwnedTransferRoot>();

        var childGo = new GameObject(nameof(PrivateSceneOwnedTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<PrivateSceneOwnedTransferChild>();

        if (_rules)
        {
            var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
            for (int i = 0; i < identities.Length; i++)
                identities[i].SetNetworkRules(_rules);
        }
        else
        {
            Debug.LogError("[PrivateSceneOwnedTransferScenario] _rules is not assigned; the owned identity must survive owner disconnect during transfer.");
        }

        rootGo.SetActive(false);
        PrivateSceneOwnedTransferRoot.ResetAll();
        PrivateSceneOwnedTransferChild.ResetAll();
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
            return ScenarioResult.Fail($"owned private transfer: target scene missing from build settings: {TargetScenePath}");

        PrivateSceneOwnedTransferRoot instance = null;
        bool cleanupNeeded = false;

        try
        {
            var load = await LoadPrivateScene(ctx, buildIndex);
            if (!load.success) return load;
            cleanupNeeded = true;

            if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
                return ScenarioResult.Fail($"owned private transfer: network scene id missing after load: {DescribeState(ctx)}");

            var victim = PickNonHostClient(ctx);
            if (!victim.HasValue)
                return ScenarioResult.Fail("owned private transfer: no eligible non-server / non-host client");

            BroadcastVictim(victim.Value.id.value);

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            scenePlayers.AddPlayerToScene(victim.Value, sceneId);
            var loaded = await WaitForPlayerLoaded(ctx, scenePlayers, victim.Value, sceneId, "victim private scene membership");
            if (!loaded.success) return loaded;

            HierarchyV2.SupressAutoOwner();
            try
            {
                instance = SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));
            }
            finally
            {
                HierarchyV2.ResumeAutoOwner();
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PrivateSceneOwnedTransferRoot.ServerAliveCount == 1
                          && PrivateSceneOwnedTransferChild.ServerAliveCount == ExpectedChildren,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"owned private transfer server spawn timeout: {DescribeState(ctx)}");
            }

            if (PrivateSceneOwnedTransferRoot.SawBadId || PrivateSceneOwnedTransferChild.SawBadId)
                return ScenarioResult.Fail($"owned private transfer server spawn saw default id: {DescribeState(ctx)}");

            instance.GiveOwnership(victim.Value);
            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

            var observed = await WaitForObserved(ctx, () => _initialObservedCount >= 1, "initial owned private scene observed");
            if (!observed.success) return observed;

            var initialBarrier = await WaitAtBarrier(ctx, BarrierBase + 1, "initial owned private scene");
            if (!initialBarrier.success) return initialBarrier;

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
                failures = $"owned private transfer victim did not reconnect and report restored state: {DescribeState(ctx)}";
            }

            if (!PrivateSceneOwnedTransferRoot.DisconnectCalls.Contains(victim.Value.id.value))
            {
                var message = $"owned private transfer: server did not observe OnOwnerDisconnected({victim.Value.id.value})";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            if (!PrivateSceneOwnedTransferRoot.ReconnectCalls.Contains(victim.Value.id.value))
            {
                var message = $"owned private transfer: server did not observe OnOwnerReconnected({victim.Value.id.value})";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
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
                var message = $"owned private transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            var cleanup = await UnloadTarget(ctx, buildIndex, instance);
            if (!cleanup.success)
                return cleanup;

            cleanupNeeded = false;

            var cleanupBarrier = await WaitAtBarrier(ctx, BarrierBase + 2, "owned private transfer cleanup");
            if (!cleanupBarrier.success) return cleanupBarrier;

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
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"owned private transfer client did not receive victim id: {DescribeState(ctx)}");
        }

        bool isVictim = IsLocalVictim(ctx);
        var failures = string.Empty;

        if (isVictim)
        {
            var initial = await WaitForOwnedClientState(ctx, "initial owned private scene", false, 0, 0);
            if (!initial.success)
                return initial;

            SignalInitialObserved();
        }
        else
        {
            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
            if (ctx.role != NetworkRole.Host
                && (PrivateSceneOwnedTransferRoot.ClientAliveCount != 0 ||
                    PrivateSceneOwnedTransferChild.ClientAliveCount != 0))
            {
                return ScenarioResult.Fail($"owned private transfer bystander received private hierarchy: {DescribeState(ctx)}");
            }
        }

        var initialBarrier = await WaitAtBarrier(ctx, BarrierBase + 1, "initial owned private scene");
        if (!initialBarrier.success)
            return initialBarrier;

        if (isVictim)
        {
            int rootSpawnsBeforeTransfer = PrivateSceneOwnedTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = PrivateSceneOwnedTransferChild.ClientSpawnCount;

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _transferCommandReceived,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);

                ctx.networkManager.TransferToNewServer();

                await UniTaskUtils.WaitWithTimeout(
                    () => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);

                var restored = await WaitForOwnedClientState(
                    ctx,
                    "post-transfer owned private scene restore",
                    true,
                    rootSpawnsBeforeTransfer,
                    childSpawnsBeforeTransfer);
                if (!restored.success)
                    throw new TimeoutException(restored.message);
            }
            catch (TimeoutException ex)
            {
                var message = $"owned private transfer victim restore timeout: {ex.Message} {DescribeState(ctx)}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
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
            var message = $"owned private transfer client did not receive phase done: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        if (!isVictim && ctx.role != NetworkRole.Host
            && (PrivateSceneOwnedTransferRoot.ClientAliveCount != 0 ||
                PrivateSceneOwnedTransferChild.ClientAliveCount != 0))
        {
            var message = $"owned private transfer bystander received private hierarchy after victim transfer: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        var cleanup = await WaitForClientCleanup(ctx);
        if (!cleanup.success)
            failures = string.IsNullOrEmpty(failures) ? cleanup.message : $"{failures} | {cleanup.message}";

        var cleanupBarrier = await WaitAtBarrier(ctx, BarrierBase + 2, "owned private transfer cleanup");
        if (!cleanupBarrier.success)
            failures = string.IsNullOrEmpty(failures) ? cleanupBarrier.message : $"{failures} | {cleanupBarrier.message}";

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(isVictim ? "owned private transfer restored" : "owned private bystander excluded")
            : ScenarioResult.Fail(failures);
    }

    private PrivateSceneOwnedTransferRoot SpawnInScene(Scene targetScene)
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
            return ScenarioResult.Fail($"owned private transfer LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"owned private transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(
        ScenarioContext ctx, int buildIndex, PrivateSceneOwnedTransferRoot instance = null)
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
                () => PrivateSceneOwnedTransferRoot.ServerAliveCount == 0
                      && PrivateSceneOwnedTransferChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"owned private transfer cleanup timeout: {DescribeState(ctx)}");
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
            return ScenarioResult.Fail($"owned private transfer {phase} timeout: {DescribeState(ctx)}");
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
            return ScenarioResult.Fail($"owned private transfer {phase} timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForOwnedClientState(
        ScenarioContext ctx,
        string phase,
        bool requireFreshSpawn,
        int rootSpawnsBefore,
        int childSpawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneOwnedTransferRoot.ClientAliveCount == 1
                      && PrivateSceneOwnedTransferChild.ClientAliveCount == ExpectedChildren
                      && PrivateSceneOwnedTransferRoot.ClientSceneName == TargetSceneName
                      && PrivateSceneOwnedTransferRoot.LocalClientInstance != null
                      && PrivateSceneOwnedTransferRoot.LocalClientInstance.isOwner
                      && PrivateSceneOwnedTransferRoot.LocalClientInstance.isController
                      && PrivateSceneOwnedTransferRoot.LocalClientInstance.hasConnectedOwner
                      && (!requireFreshSpawn ||
                          (PrivateSceneOwnedTransferRoot.ClientSpawnCount > rootSpawnsBefore
                           && PrivateSceneOwnedTransferChild.ClientSpawnCount > childSpawnsBefore)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (PrivateSceneOwnedTransferRoot.SawBadId || PrivateSceneOwnedTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        if (!requireFreshSpawn)
            return ScenarioResult.Ok();

        var root = CheckRootSpawnRecord(phase);
        if (!root.success)
            return root;

        var child = CheckChildSpawnRecord(phase);
        if (!child.success)
            return child;

        return ScenarioResult.Ok();
    }

    private static ScenarioResult CheckRootSpawnRecord(string phase)
    {
        if (!PrivateSceneOwnedTransferRoot.HasLastClientSpawn)
            return ScenarioResult.Fail($"{phase}: missing root client spawn record");

        var rec = PrivateSceneOwnedTransferRoot.LastClientSpawn;
        if (rec.ownerId != _victimId || !rec.ownerHasValue || !rec.isOwner || !rec.isController || !rec.hasConnectedOwner)
        {
            return ScenarioResult.Fail(
                $"{phase}: root owner state missing from client spawn record: " +
                $"ownerId={rec.ownerId}, expected={_victimId}, ownerHasValue={rec.ownerHasValue}, " +
                $"isOwner={rec.isOwner}, isController={rec.isController}, hasConnectedOwner={rec.hasConnectedOwner}, " +
                $"scene={rec.sceneName}");
        }

        return ScenarioResult.Ok();
    }

    private static ScenarioResult CheckChildSpawnRecord(string phase)
    {
        if (!PrivateSceneOwnedTransferChild.HasLastClientSpawn)
            return ScenarioResult.Fail($"{phase}: missing child client spawn record");

        var rec = PrivateSceneOwnedTransferChild.LastClientSpawn;
        if (rec.ownerId != _victimId || !rec.ownerHasValue || !rec.isOwner || !rec.isController || !rec.hasConnectedOwner)
        {
            return ScenarioResult.Fail(
                $"{phase}: child owner state missing from client spawn record: " +
                $"ownerId={rec.ownerId}, expected={_victimId}, ownerHasValue={rec.ownerHasValue}, " +
                $"isOwner={rec.isOwner}, isController={rec.isController}, hasConnectedOwner={rec.hasConnectedOwner}, " +
                $"scene={rec.sceneName}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientCleanup(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PrivateSceneOwnedTransferRoot.ClientAliveCount == 0
                      && PrivateSceneOwnedTransferChild.ClientAliveCount == 0,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"owned private transfer client cleanup timeout: {DescribeState(ctx)}");
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
            return ScenarioResult.Fail($"owned private transfer {phase} barrier timeout: {DescribeState(ctx)}");
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
               $"clientRoots={PrivateSceneOwnedTransferRoot.ClientAliveCount}, " +
               $"clientChildren={PrivateSceneOwnedTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={PrivateSceneOwnedTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={PrivateSceneOwnedTransferChild.ClientSpawnCount}, " +
               $"clientScene={PrivateSceneOwnedTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={PrivateSceneOwnedTransferRoot.ServerAliveCount}, " +
               $"serverChildren={PrivateSceneOwnedTransferChild.ServerAliveCount}, " +
               $"rootOwned={PrivateSceneOwnedTransferRoot.LocalClientInstance != null && PrivateSceneOwnedTransferRoot.LocalClientInstance.isOwner}, " +
               $"rootController={PrivateSceneOwnedTransferRoot.LocalClientInstance != null && PrivateSceneOwnedTransferRoot.LocalClientInstance.isController}, " +
               $"disconnectCalls=[{string.Join(",", PrivateSceneOwnedTransferRoot.DisconnectCalls)}], " +
               $"reconnectCalls=[{string.Join(",", PrivateSceneOwnedTransferRoot.ReconnectCalls)}], " +
               $"rootBadId={PrivateSceneOwnedTransferRoot.SawBadId}, childBadId={PrivateSceneOwnedTransferChild.SawBadId}, " +
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

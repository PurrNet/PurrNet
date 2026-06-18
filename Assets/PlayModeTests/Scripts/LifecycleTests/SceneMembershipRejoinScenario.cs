using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneMembershipRejoinScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetB";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const int BarrierBase = 6400;
    private const int ExpectedChildren = 2;

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
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
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private SceneMembershipRejoinRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SceneMembershipRejoinRoot));
        _prefab = rootGo.AddComponent<SceneMembershipRejoinRoot>();
        AddChild(rootGo, "RejoinChildA");
        AddChild(rootGo, "RejoinChildB");

        if (_rules)
        {
            var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
            for (int i = 0; i < identities.Length; i++)
                identities[i].SetNetworkRules(_rules);
        }
        else
        {
            Debug.LogError("[SceneMembershipRejoinScenario] _rules is not assigned; the identity must survive owner disconnect.");
        }

        rootGo.SetActive(false);
        SceneMembershipRejoinRoot.ResetAll();
        SceneMembershipRejoinChild.ResetAll();
        _victimId = 0;
        _victimReceived = false;
        _disconnectCommandReceived = false;
        _phaseDoneReceived = false;
        _initialObservedCount = 0;
        _victimReturnedCount = 0;
        _doneCount = 0;
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<SceneMembershipRejoinChild>();
        return go;
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
            return ScenarioResult.Fail($"target scene missing from build settings: {TargetScenePath}");

        var load = await LoadPrivateScene(ctx, buildIndex);
        if (!load.success) return load;

        if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
            return ScenarioResult.Fail($"network scene id missing after load: {DescribeState(ctx)}");

        var victim = PickNonHostClient(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client for private scene rejoin");

        BroadcastVictim(victim.Value.id.value);

        var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
        scenePlayers.AddPlayerToScene(victim.Value, sceneId);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => scenePlayers.IsPlayerLoadedInScene(victim.Value, sceneId),
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"victim did not load private scene before spawn: {DescribeState(ctx)}");
        }

        HierarchyV2.SupressAutoOwner();
        SceneMembershipRejoinRoot instance;
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
                () => SceneMembershipRejoinRoot.ServerAliveCount == 1
                      && SceneMembershipRejoinChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server spawn timeout: {DescribeState(ctx)}");
        }

        instance.GiveOwnership(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _initialObservedCount >= 1,
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"victim did not observe owned private scene hierarchy: {DescribeState(ctx)}");
        }

        BroadcastDisconnectCommand();

        var failures = string.Empty;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReturnedCount >= 1,
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures = $"victim {victim.Value.id.value} did not reconnect and report restored state: {DescribeState(ctx)}";
        }

        if (!SceneMembershipRejoinRoot.DisconnectCalls.Contains(victim.Value.id.value))
        {
            var message = $"server did not observe OnOwnerDisconnected({victim.Value.id.value})";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        if (!SceneMembershipRejoinRoot.ReconnectCalls.Contains(victim.Value.id.value))
        {
            var message = $"server did not observe OnOwnerReconnected({victim.Value.id.value})";
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
            var message = $"done timeout: got {_doneCount}/{ctx.expectedConnections}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        if (instance)
            instance.Despawn();

        var cleanup = await UnloadTarget(ctx, buildIndex);
        if (!cleanup.success)
            return cleanup;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup barrier timeout: {DescribeState(ctx)}");
        }

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok($"victim={victim.Value.id.value}")
            : ScenarioResult.Fail(failures);
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
            return ScenarioResult.Fail("client did not receive private scene rejoin victim id");
        }

        bool isVictim = IsLocalVictim(ctx);
        var failures = string.Empty;

        if (isVictim)
        {
            var initial = await WaitForOwnedClientState(ctx, "initial private scene membership", requireFreshSpawn: false, 0);
            if (!initial.success)
                failures = initial.message;

            if (string.IsNullOrEmpty(failures))
                SignalInitialObserved();

            int spawnsBeforeReconnect = SceneMembershipRejoinRoot.ClientSpawnCount;

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _disconnectCommandReceived,
                    _disconnectTimeoutSeconds,
                    ctx.cancellationToken);
                await PerformDisconnectReconnect(ctx, spawnsBeforeReconnect);
            }
            catch (TimeoutException)
            {
                var message = $"victim did not complete disconnect/reconnect restore: {DescribeState(ctx)}";
                failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
            if (ctx.role != NetworkRole.Host
                && (SceneMembershipRejoinRoot.ClientAliveCount != 0 || SceneMembershipRejoinChild.ClientAliveCount != 0))
            {
                failures = $"bystander received private scene hierarchy before reconnect: {DescribeState(ctx)}";
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
            var message = $"client did not receive phase done: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        if (!isVictim && ctx.role != NetworkRole.Host
            && (SceneMembershipRejoinRoot.ClientAliveCount != 0 || SceneMembershipRejoinChild.ClientAliveCount != 0))
        {
            var message = $"bystander received private scene hierarchy after victim rejoin: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        var cleanup = await WaitForClientCleanup(ctx);
        if (!cleanup.success)
            failures = string.IsNullOrEmpty(failures) ? cleanup.message : $"{failures} | {cleanup.message}";

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            var message = $"cleanup barrier timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(isVictim ? "victim rejoined private scene" : "bystander excluded")
            : ScenarioResult.Fail(failures);
    }

    private SceneMembershipRejoinRoot SpawnInScene(Scene targetScene)
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
        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = false
        });

        if (op == null)
            return ScenarioResult.Fail($"LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(ScenarioContext ctx, int buildIndex)
    {
        if (IsSceneLoaded(TargetSceneName))
            ctx.networkManager.sceneModule.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipRejoinRoot.ServerAliveCount == 0
                      && SceneMembershipRejoinChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask PerformDisconnectReconnect(ScenarioContext ctx, int spawnsBeforeReconnect)
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

        var restored = await WaitForOwnedClientState(
            ctx, "post-reconnect private scene restore", requireFreshSpawn: true, spawnsBeforeReconnect);
        if (!restored.success)
            throw new TimeoutException(restored.message);
    }

    private async UniTask<ScenarioResult> WaitForOwnedClientState(
        ScenarioContext ctx, string phase, bool requireFreshSpawn, int spawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipRejoinRoot.ClientAliveCount == 1
                      && SceneMembershipRejoinChild.ClientAliveCount == ExpectedChildren
                      && SceneMembershipRejoinRoot.ClientSceneName == TargetSceneName
                      && SceneMembershipRejoinRoot.LocalClientInstance != null
                      && SceneMembershipRejoinRoot.LocalClientInstance.isOwner
                      && (!requireFreshSpawn || SceneMembershipRejoinRoot.ClientSpawnCount > spawnsBefore),
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (SceneMembershipRejoinRoot.SawBadId || SceneMembershipRejoinChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: default/unassigned id observed: {DescribeState(ctx)}");

        if (!requireFreshSpawn)
            return ScenarioResult.Ok();

        if (!SceneMembershipRejoinRoot.HasLastClientSpawn)
            return ScenarioResult.Fail($"{phase}: missing client spawn record: {DescribeState(ctx)}");

        var rec = SceneMembershipRejoinRoot.LastClientSpawn;
        if (rec.ownerId != _victimId || !rec.ownerHasValue || !rec.isOwner || !rec.isController || !rec.hasConnectedOwner)
        {
            return ScenarioResult.Fail(
                $"{phase}: owner state missing from client spawn record: " +
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
                () => SceneMembershipRejoinRoot.ClientAliveCount == 0
                      && SceneMembershipRejoinChild.ClientAliveCount == 0
                      && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client cleanup timeout: {DescribeState(ctx)}");
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
               $"disconnect={_disconnectCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"initial={_initialObservedCount}, returned={_victimReturnedCount}, done={_doneCount}, " +
               $"clientRoots={SceneMembershipRejoinRoot.ClientAliveCount}, " +
               $"clientChildren={SceneMembershipRejoinChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={SceneMembershipRejoinRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={SceneMembershipRejoinChild.ClientSpawnCount}, " +
               $"clientScene={SceneMembershipRejoinRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={SceneMembershipRejoinRoot.ServerAliveCount}, " +
               $"serverChildren={SceneMembershipRejoinChild.ServerAliveCount}, " +
               $"disconnectCalls=[{string.Join(",", SceneMembershipRejoinRoot.DisconnectCalls)}], " +
               $"reconnectCalls=[{string.Join(",", SceneMembershipRejoinRoot.ReconnectCalls)}], " +
               $"rootBadId={SceneMembershipRejoinRoot.SawBadId}, childBadId={SceneMembershipRejoinChild.SawBadId}, " +
               $"sceneLoaded={IsSceneLoaded(TargetSceneName)}";
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

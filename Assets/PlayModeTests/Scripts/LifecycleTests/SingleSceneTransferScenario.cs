using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SingleSceneTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneTransferTarget";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneTransferTarget.unity";
    private const int ExpectedChildren = 1;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _transferCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _initialObservedCount;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private SingleSceneTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SingleSceneTransferRoot));
        _prefab = rootGo.AddComponent<SingleSceneTransferRoot>();

        var childGo = new GameObject(nameof(SingleSceneTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<SingleSceneTransferChild>();

        rootGo.SetActive(false);
        SingleSceneTransferRoot.ResetAll();
        SingleSceneTransferChild.ResetAll();
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
        PreserveSingleSceneHarness(ctx);
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private void PreserveSingleSceneHarness(ScenarioContext ctx)
    {
        UnityEngine.Object.DontDestroyOnLoad(transform.root.gameObject);
        UnityEngine.Object.DontDestroyOnLoad(ctx.networkManager.gameObject);
        PreserveRuntimePrefabs(ctx.networkManager.prefabProvider);
    }

    private static void PreserveRuntimePrefabs(IPrefabProvider provider)
    {
        if (provider == null)
            return;

        foreach (var data in provider.allPrefabs)
        {
            var prefab = data.prefab;
            if (!prefab)
                continue;

            var root = prefab.transform.root ? prefab.transform.root.gameObject : prefab;
            if (root && root.scene.IsValid() && root.scene.isLoaded)
                UnityEngine.Object.DontDestroyOnLoad(root);
        }
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"single transfer: target scene missing from build settings: {TargetScenePath}");

        var victim = PickNonHostClient(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("single transfer: no eligible non-server / non-host client");

        BroadcastVictim(victim.Value.id.value);

        var load = await LoadSingleScene(ctx, buildIndex);
        if (!load.success) return load;

        var targetScene = SceneManager.GetSceneByName(TargetSceneName);
        if (!targetScene.IsValid() || !targetScene.isLoaded)
            return ScenarioResult.Fail($"single transfer: target scene not loaded after Single load: {DescribeState(ctx)}");

        SpawnInScene(targetScene);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SingleSceneTransferRoot.ServerAliveCount == 1
                      && SingleSceneTransferChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single transfer server spawn timeout: {DescribeState(ctx)}");
        }

        if (SingleSceneTransferRoot.SawBadId || SingleSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"single transfer server spawn saw default id: {DescribeState(ctx)}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _initialObservedCount >= ctx.expectedConnections,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"single transfer initial observation timeout: got {_initialObservedCount}/{ctx.expectedConnections}; {DescribeState(ctx)}");
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
            failures = $"single transfer victim did not reconnect and restore: {DescribeState(ctx)}";
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
            var message = $"single transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
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
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single transfer victim id timeout: {DescribeState(ctx)}");
        }

        var initial = await WaitForClientScene(ctx, "single transfer initial", requireFreshSpawn: false, 0, 0);
        if (!initial.success) return initial;

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
            return ScenarioResult.Fail($"single transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (IsLocalVictim(ctx))
        {
            int rootSpawnsBeforeTransfer = SingleSceneTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = SingleSceneTransferChild.ClientSpawnCount;

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
                failures = $"single transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForClientScene(
                    ctx,
                    "single transfer restore",
                    requireFreshSpawn: true,
                    rootSpawnsBeforeTransfer,
                    childSpawnsBeforeTransfer);
                if (!restored.success)
                    failures = restored.message;
            }

            if (string.IsNullOrEmpty(failures))
                SignalVictimReturned();
        }
        else
        {
            var retained = await WaitForClientScene(ctx, "single transfer non-victim retained", false, 0, 0);
            if (!retained.success)
                failures = retained.message;
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
            var message = $"single transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(IsLocalVictim(ctx) ? "victim single transfer restored" : "non-victim retained")
            : ScenarioResult.Fail(failures);
    }

    private SingleSceneTransferRoot SpawnInScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> LoadSingleScene(ScenarioContext ctx, int buildIndex)
    {
        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (op == null)
            return ScenarioResult.Fail($"single transfer: LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx,
        string phase,
        bool requireFreshSpawn,
        int rootSpawnsBefore,
        int childSpawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SingleSceneTransferRoot.ClientAliveCount == 1
                      && SingleSceneTransferChild.ClientAliveCount == ExpectedChildren
                      && SingleSceneTransferRoot.ClientSceneName == TargetSceneName
                      && ctx.networkManager.isLocalPlayerReady
                      && (!requireFreshSpawn ||
                          (SingleSceneTransferRoot.ClientSpawnCount > rootSpawnsBefore
                           && SingleSceneTransferChild.ClientSpawnCount > childSpawnsBefore)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (SingleSceneTransferRoot.SawBadId || SingleSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

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
               $"clientState={ctx.networkManager.clientState}, serverState={ctx.networkManager.serverState}, " +
               $"client={ctx.networkManager.isClient}, server={ctx.networkManager.isServer}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"clientRoots={SingleSceneTransferRoot.ClientAliveCount}, " +
               $"clientChildren={SingleSceneTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={SingleSceneTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={SingleSceneTransferChild.ClientSpawnCount}, " +
               $"clientScene={SingleSceneTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={SingleSceneTransferRoot.ServerAliveCount}, " +
               $"serverChildren={SingleSceneTransferChild.ServerAliveCount}, " +
               $"rootBadId={SingleSceneTransferRoot.SawBadId}, childBadId={SingleSceneTransferChild.SawBadId}, " +
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

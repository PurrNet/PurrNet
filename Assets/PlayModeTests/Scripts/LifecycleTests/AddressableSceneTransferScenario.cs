using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class AddressableSceneTransferScenario : Scenario
{
    private const string TargetSceneName = "AddressableSceneTransferTarget";
    private const string TargetSceneGuid = "c41b9a783d694893a97bc1cae588df22";
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

    private AddressableSceneTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(AddressableSceneTransferRoot));
        _prefab = rootGo.AddComponent<AddressableSceneTransferRoot>();

        var childGo = new GameObject(nameof(AddressableSceneTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<AddressableSceneTransferChild>();

        var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
        for (int i = 0; i < identities.Length; i++)
            identities[i].skipSceneAutoSpawning = true;

        rootGo.SetActive(false);
        AddressableSceneTransferRoot.ResetAll();
        AddressableSceneTransferChild.ResetAll();
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
        var victim = PickNonHostClient(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("addressable transfer: no eligible non-server / non-host client");

        BroadcastVictim(victim.Value.id.value);

        var load = await LoadAddressableTarget(ctx);
        if (!load.success)
            return load;

        if (!TryGetAddressableTargetScene(ctx, out var targetScene))
            return ScenarioResult.Fail($"addressable transfer: target scene not registered after load: {DescribeState(ctx)}");

        SpawnInScene(targetScene);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AddressableSceneTransferRoot.ServerAliveCount == 1
                      && AddressableSceneTransferChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"addressable transfer server spawn timeout: {DescribeState(ctx)}");
        }

        if (AddressableSceneTransferRoot.SawBadId || AddressableSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"addressable transfer server spawn saw default id: {DescribeState(ctx)}");

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
                $"addressable transfer initial observation timeout: got {_initialObservedCount}/{ctx.expectedConnections}; {DescribeState(ctx)}");
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
            failures = $"addressable transfer victim did not reconnect and restore: {DescribeState(ctx)}";
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
            var message = $"addressable transfer done timeout: got {_doneCount}/{ctx.expectedConnections}";
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
            return ScenarioResult.Fail($"addressable transfer victim id timeout: {DescribeState(ctx)}");
        }

        var initial = await WaitForClientScene(ctx, "addressable transfer initial", false, 0, 0);
        if (!initial.success)
            return initial;

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
            return ScenarioResult.Fail($"addressable transfer command timeout: {DescribeState(ctx)}");
        }

        var failures = string.Empty;
        if (IsLocalVictim(ctx))
        {
            int rootSpawnsBeforeTransfer = AddressableSceneTransferRoot.ClientSpawnCount;
            int childSpawnsBeforeTransfer = AddressableSceneTransferChild.ClientSpawnCount;

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
                failures = $"addressable transfer reconnect timeout: {DescribeState(ctx)}";
            }

            if (string.IsNullOrEmpty(failures))
            {
                var restored = await WaitForClientScene(
                    ctx,
                    "addressable transfer restore",
                    true,
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
            var retained = await WaitForClientScene(ctx, "addressable transfer non-victim retained", false, 0, 0);
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
            var message = $"addressable transfer phase done timeout: {DescribeState(ctx)}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        SignalDone();

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok(IsLocalVictim(ctx) ? "victim addressable transfer restored" : "non-victim retained")
            : ScenarioResult.Fail(failures);
    }

    private async UniTask<ScenarioResult> LoadAddressableTarget(ScenarioContext ctx)
    {
        var handle = ctx.networkManager.sceneModule.LoadAddressableSceneAsync(TargetSceneGuid, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (!handle.IsValid())
            return ScenarioResult.Fail($"addressable transfer: LoadAddressableSceneAsync returned invalid handle for {TargetSceneGuid}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => handle.IsDone
                      && handle.Status == AsyncOperationStatus.Succeeded
                      && IsNetworkAddressableTargetLoaded(ctx),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"addressable transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private AddressableSceneTransferRoot SpawnInScene(Scene targetScene)
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
                () => AddressableSceneTransferRoot.ClientAliveCount == 1
                      && AddressableSceneTransferChild.ClientAliveCount == ExpectedChildren
                      && AddressableSceneTransferRoot.ClientSceneName == TargetSceneName
                      && IsNetworkAddressableTargetLoaded(ctx)
                      && ctx.networkManager.isLocalPlayerReady
                      && (!requireFreshSpawn ||
                          (AddressableSceneTransferRoot.ClientSpawnCount > rootSpawnsBefore
                           && AddressableSceneTransferChild.ClientSpawnCount > childSpawnsBefore)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (AddressableSceneTransferRoot.SawBadId || AddressableSceneTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private static bool IsNetworkAddressableTargetLoaded(ScenarioContext ctx)
    {
        return IsAddressableTargetSceneLoaded()
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsAddressableSceneLoaded(TargetSceneGuid)
               && ctx.networkManager.sceneModule.TryGetSceneIdByAddressableGuid(TargetSceneGuid, out _);
    }

    private static bool TryGetAddressableTargetScene(ScenarioContext ctx, out Scene scene)
    {
        scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid()
               && scene.isLoaded
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.TryGetSceneIdByAddressableGuid(TargetSceneGuid, out _);
    }

    private static bool IsAddressableTargetSceneLoaded()
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
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
               $"clientRoots={AddressableSceneTransferRoot.ClientAliveCount}, " +
               $"clientChildren={AddressableSceneTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={AddressableSceneTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={AddressableSceneTransferChild.ClientSpawnCount}, " +
               $"clientScene={AddressableSceneTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={AddressableSceneTransferRoot.ServerAliveCount}, " +
               $"serverChildren={AddressableSceneTransferChild.ServerAliveCount}, " +
               $"rootBadId={AddressableSceneTransferRoot.SawBadId}, childBadId={AddressableSceneTransferChild.SawBadId}, " +
               $"sceneLoaded={IsAddressableTargetSceneLoaded()}, " +
               $"networkSceneLoaded={IsNetworkAddressableTargetLoaded(ctx)}, " +
               $"addressableLoading={ctx.networkManager.sceneModule != null && ctx.networkManager.sceneModule.IsAddressableSceneLoading(TargetSceneGuid)}";
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

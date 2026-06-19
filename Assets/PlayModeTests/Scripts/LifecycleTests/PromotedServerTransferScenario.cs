using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PromotedServerTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetA";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const int ExpectedChildren = 1;

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _promotionTimeoutSeconds = 45f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _promotionStartupDelaySeconds = 1f;
    [SerializeField] private float _flushDelaySeconds = 0.2f;

    private static ulong _promotedId;
    private static ulong _ownerId;
    private static int _expectedTransfers;
    private static bool _planReceived;
    private static bool _promotionCommandReceived;
    private static int _initialObservedCount;
    private static int _transferRestoredCount;

    private PromotedServerTransferRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PromotedServerTransferRoot));
        _prefab = rootGo.AddComponent<PromotedServerTransferRoot>();

        var childGo = new GameObject(nameof(PromotedServerTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<PromotedServerTransferChild>();

        if (_rules)
        {
            var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
            for (int i = 0; i < identities.Length; i++)
                identities[i].SetNetworkRules(_rules);
        }
        else
        {
            Debug.LogError("[PromotedServerTransferScenario] _rules is not assigned; the owned identity must survive promotion.");
        }

        rootGo.SetActive(false);
        PromotedServerTransferRoot.ResetAll();
        PromotedServerTransferChild.ResetAll();
        _promotedId = 0;
        _ownerId = 0;
        _expectedTransfers = 0;
        _planReceived = false;
        _promotionCommandReceived = false;
        _initialObservedCount = 0;
        _transferRestoredCount = 0;
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
            return ScenarioResult.Fail($"promotion transfer: target scene missing from build settings: {TargetScenePath}");

        var load = await LoadPublicScene(ctx, buildIndex);
        if (!load.success) return load;

        var promoted = PickPromotedClient(ctx);
        if (!promoted.HasValue)
            return ScenarioResult.Fail("promotion transfer: no eligible promoted client");

        var owner = PickTransferClient(ctx, promoted.Value);
        if (!owner.HasValue)
            return ScenarioResult.Fail("promotion transfer: no eligible transfer/owner client");

        int expectedTransfers = CountTransferClients(ctx, promoted.Value);
        if (expectedTransfers <= 0)
            return ScenarioResult.Fail("promotion transfer: expected transfer count was zero");

        BroadcastPlan(promoted.Value.id.value, owner.Value.id.value, expectedTransfers);

        var instance = SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PromotedServerTransferRoot.ServerAliveCount == 1
                      && PromotedServerTransferChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer server spawn timeout: {DescribeState(ctx)}");
        }

        if (PromotedServerTransferRoot.SawBadId || PromotedServerTransferChild.SawBadId)
            return ScenarioResult.Fail($"promotion transfer server spawn saw default id: {DescribeState(ctx)}");

        instance.GiveOwnership(owner.Value, propagateToChildren: true);

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
                $"promotion transfer initial observation timeout: got {_initialObservedCount}/{ctx.expectedConnections}; {DescribeState(ctx)}");
        }

        BroadcastPromotionCommand();

        await UniTask.WaitForSeconds(_flushDelaySeconds, cancellationToken: ctx.cancellationToken);

        ctx.networkManager.StopClient();
        ctx.networkManager.StopServer();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected
                      && ctx.networkManager.serverState == ConnectionState.Disconnected
                      && ctx.networkManager.playerCount == 0,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer original server shutdown timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok(
            $"promoted={promoted.Value.id.value}, owner={owner.Value.id.value}, transfers={expectedTransfers}");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var plan = await WaitForPlan(ctx);
        if (!plan.success) return plan;

        bool isPromoted = IsLocal(_promotedId, ctx);
        bool isOriginalHostLocal = ctx.role == NetworkRole.Host && !isPromoted;
        bool shouldTransfer = ctx.role == NetworkRole.Client && !isPromoted;

        var initial = await WaitForClientScene(
            ctx,
            "initial promoted transfer scene",
            requireFreshSpawn: false,
            rootSpawnsBefore: 0,
            childSpawnsBefore: 0,
            requireOwnerState: IsLocal(_ownerId, ctx));
        if (!initial.success) return initial;

        SignalInitialObserved();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _promotionCommandReceived,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer command timeout: {DescribeState(ctx)}");
        }

        if (isOriginalHostLocal)
            return await WaitForOriginalHostShutdown(ctx);

        if (isPromoted)
            return await RunPromotedClient(ctx);

        if (shouldTransfer)
            return await RunTransferClient(ctx);

        return ScenarioResult.Fail($"promotion transfer: unhandled client role: {DescribeState(ctx)}");
    }

    private async UniTask<ScenarioResult> RunPromotedClient(ScenarioContext ctx)
    {
        ctx.networkManager.PromoteToServer();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.isServer
                      && ctx.networkManager.serverState == ConnectionState.Connected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer promoted client did not become server: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PromotedServerTransferRoot.ServerAliveCount == 1
                      && PromotedServerTransferChild.ServerAliveCount == ExpectedChildren,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer promoted server hierarchy timeout: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _transferRestoredCount >= _expectedTransfers,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"promotion transfer promoted server restored timeout: got {_transferRestoredCount}/{_expectedTransfers}; {DescribeState(ctx)}");
        }

        if (!PromotedServerTransferRoot.DisconnectCalls.Contains(_ownerId))
            return ScenarioResult.Fail($"promotion transfer promoted server did not mark owner disconnected: {DescribeState(ctx)}");

        if (!PromotedServerTransferRoot.ReconnectCalls.Contains(_ownerId))
            return ScenarioResult.Fail($"promotion transfer promoted server did not mark owner reconnected: {DescribeState(ctx)}");

        ScenarioSequencer.IssueSequenceComplete();
        await UniTask.NextFrame(ctx.cancellationToken);
        await UniTask.NextFrame(ctx.cancellationToken);

        return ScenarioResult.Ok($"promoted server restored {_transferRestoredCount}/{_expectedTransfers}");
    }

    private async UniTask<ScenarioResult> RunTransferClient(ScenarioContext ctx)
    {
        int rootSpawnsBefore = PromotedServerTransferRoot.ClientSpawnCount;
        int childSpawnsBefore = PromotedServerTransferChild.ClientSpawnCount;
        bool isOwner = IsLocal(_ownerId, ctx);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer client did not detach from old server: {DescribeState(ctx)}");
        }

        await UniTask.WaitForSeconds(_promotionStartupDelaySeconds, cancellationToken: ctx.cancellationToken);

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
            return ScenarioResult.Fail($"promotion transfer reconnect timeout: {DescribeState(ctx)}");
        }

        var restored = await WaitForClientScene(
            ctx,
            "post-promotion transfer restore",
            requireFreshSpawn: true,
            rootSpawnsBefore: rootSpawnsBefore,
            childSpawnsBefore: childSpawnsBefore,
            requireOwnerState: isOwner);
        if (!restored.success) return restored;

        SignalTransferRestored();
        await UniTask.WaitForSeconds(_flushDelaySeconds, cancellationToken: ctx.cancellationToken);

        return ScenarioResult.Ok(isOwner ? "owner restored through promoted server" : "peer restored through promoted server");
    }

    private async UniTask<ScenarioResult> WaitForOriginalHostShutdown(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected
                      && ctx.networkManager.serverState == ConnectionState.Disconnected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer original host did not shut down: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok("original host shut down");
    }

    private PromotedServerTransferRoot SpawnInScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> LoadPublicScene(ScenarioContext ctx, int buildIndex)
    {
        if (IsNetworkSceneLoaded(ctx, buildIndex))
            return ScenarioResult.Ok();

        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (op == null)
            return ScenarioResult.Fail($"promotion transfer LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForPlan(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _planReceived,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"promotion transfer plan timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx,
        string phase,
        bool requireFreshSpawn,
        int rootSpawnsBefore,
        int childSpawnsBefore,
        bool requireOwnerState)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PromotedServerTransferRoot.ClientAliveCount == 1
                      && PromotedServerTransferChild.ClientAliveCount == ExpectedChildren
                      && PromotedServerTransferRoot.ClientSceneName == TargetSceneName
                      && PromotedServerTransferRoot.LocalClientInstance != null
                      && (!requireFreshSpawn ||
                          (PromotedServerTransferRoot.ClientSpawnCount > rootSpawnsBefore
                           && PromotedServerTransferChild.ClientSpawnCount > childSpawnsBefore))
                      && (!requireOwnerState ||
                          (PromotedServerTransferRoot.LocalClientInstance.isOwner
                           && PromotedServerTransferRoot.LocalClientInstance.isController
                           && PromotedServerTransferRoot.LocalClientInstance.hasConnectedOwner)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (PromotedServerTransferRoot.SawBadId || PromotedServerTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        if (!requireFreshSpawn || !requireOwnerState)
            return ScenarioResult.Ok();

        var root = CheckRootSpawnRecord(phase);
        if (!root.success) return root;

        var child = CheckChildSpawnRecord(phase);
        if (!child.success) return child;

        return ScenarioResult.Ok();
    }

    private static ScenarioResult CheckRootSpawnRecord(string phase)
    {
        if (!PromotedServerTransferRoot.HasLastClientSpawn)
            return ScenarioResult.Fail($"{phase}: missing root client spawn record");

        var rec = PromotedServerTransferRoot.LastClientSpawn;
        if (rec.ownerId != _ownerId || !rec.ownerHasValue || !rec.isOwner || !rec.isController || !rec.hasConnectedOwner)
        {
            return ScenarioResult.Fail(
                $"{phase}: root owner state missing from client spawn record: " +
                $"ownerId={rec.ownerId}, expected={_ownerId}, ownerHasValue={rec.ownerHasValue}, " +
                $"isOwner={rec.isOwner}, isController={rec.isController}, hasConnectedOwner={rec.hasConnectedOwner}, " +
                $"scene={rec.sceneName}");
        }

        return ScenarioResult.Ok();
    }

    private static ScenarioResult CheckChildSpawnRecord(string phase)
    {
        if (!PromotedServerTransferChild.HasLastClientSpawn)
            return ScenarioResult.Fail($"{phase}: missing child client spawn record");

        var rec = PromotedServerTransferChild.LastClientSpawn;
        if (rec.ownerId != _ownerId || !rec.ownerHasValue || !rec.isOwner || !rec.isController || !rec.hasConnectedOwner)
        {
            return ScenarioResult.Fail(
                $"{phase}: child owner state missing from client spawn record: " +
                $"ownerId={rec.ownerId}, expected={_ownerId}, ownerHasValue={rec.ownerHasValue}, " +
                $"isOwner={rec.isOwner}, isController={rec.isController}, hasConnectedOwner={rec.hasConnectedOwner}, " +
                $"scene={rec.sceneName}");
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

    private static PlayerID? PickPromotedClient(ScenarioContext ctx)
    {
        PlayerID? best = null;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
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

    private static PlayerID? PickTransferClient(ScenarioContext ctx, PlayerID promoted)
    {
        PlayerID? best = null;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer || player == promoted)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }

    private static int CountTransferClients(ScenarioContext ctx, PlayerID promoted)
    {
        int count = 0;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer || player == promoted)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            count++;
        }

        return count;
    }

    private static PlayerID? GetHostLocalPlayer(ScenarioContext ctx)
    {
        return ctx.role == NetworkRole.Host && ctx.networkManager.isLocalPlayerReady
            ? ctx.networkManager.localPlayer
            : (PlayerID?)null;
    }

    private static bool IsLocal(ulong playerId, ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == playerId;
    }

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, promoted={_promotedId}, owner={_ownerId}, expectedTransfers={_expectedTransfers}, " +
               $"plan={_planReceived}, command={_promotionCommandReceived}, " +
               $"initial={_initialObservedCount}, restored={_transferRestoredCount}, " +
               $"clientState={ctx.networkManager.clientState}, serverState={ctx.networkManager.serverState}, " +
               $"client={ctx.networkManager.isClient}, server={ctx.networkManager.isServer}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"playerCount={ctx.networkManager.playerCount}, " +
               $"clientRoots={PromotedServerTransferRoot.ClientAliveCount}, " +
               $"clientChildren={PromotedServerTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={PromotedServerTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={PromotedServerTransferChild.ClientSpawnCount}, " +
               $"clientScene={PromotedServerTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={PromotedServerTransferRoot.ServerAliveCount}, " +
               $"serverChildren={PromotedServerTransferChild.ServerAliveCount}, " +
               $"rootOwned={PromotedServerTransferRoot.LocalClientInstance != null && PromotedServerTransferRoot.LocalClientInstance.isOwner}, " +
               $"rootController={PromotedServerTransferRoot.LocalClientInstance != null && PromotedServerTransferRoot.LocalClientInstance.isController}, " +
               $"disconnectCalls=[{string.Join(",", PromotedServerTransferRoot.DisconnectCalls)}], " +
               $"reconnectCalls=[{string.Join(",", PromotedServerTransferRoot.ReconnectCalls)}], " +
               $"rootBadId={PromotedServerTransferRoot.SawBadId}, childBadId={PromotedServerTransferChild.SawBadId}, " +
               $"sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastPlan(ulong promotedId, ulong ownerId, int expectedTransfers)
    {
        _promotedId = promotedId;
        _ownerId = ownerId;
        _expectedTransfers = expectedTransfers;
        _planReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPromotionCommand()
    {
        _promotionCommandReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalInitialObserved(RPCInfo info = default)
    {
        _initialObservedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalTransferRestored(RPCInfo info = default)
    {
        _transferRestoredCount++;
    }
}

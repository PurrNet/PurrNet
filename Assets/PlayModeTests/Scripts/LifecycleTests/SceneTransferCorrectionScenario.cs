using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransferCorrectionScenario : Scenario
{
    internal const string TargetSceneName = "SceneTransferTarget";

    private const string TargetScenePath = "Assets/PlayModeTests/SceneTransferTarget.unity";
    private const int BarrierBase = 5900;
    private const int ExpectedChildren = 1;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private SceneTransferCorrectionRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SceneTransferCorrectionRoot));
        _prefab = rootGo.AddComponent<SceneTransferCorrectionRoot>();

        var childGo = new GameObject(nameof(SceneTransferCorrectionChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<SceneTransferCorrectionChild>();

        rootGo.SetActive(false);
        SceneTransferCorrectionRoot.ResetAll();
        SceneTransferCorrectionChild.ResetAll();
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
        int targetBuildIndex = GetTargetBuildIndex();
        if (targetBuildIndex < 0)
            return ScenarioResult.Fail($"initial load: target scene is missing from build settings: {TargetScenePath}");

        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (op == null)
            return ScenarioResult.Fail($"initial load: sceneModule.LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && TryGetTargetScene(ctx.networkManager, targetBuildIndex, out _),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial load timeout: {DescribeState(ctx)}");
        }

        if (!TryGetTargetScene(ctx.networkManager, targetBuildIndex, out var targetScene))
            return ScenarioResult.Fail($"initial load: target scene not registered after load: {DescribeState(ctx)}");

        var instance = SpawnInTargetScene(targetScene);
        var initial = await WaitForLocalTargetState(ctx, "initial hierarchy", _spawnTimeoutSeconds);
        if (!initial.success)
            return initial;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial load barrier timeout: {DescribeState(ctx)}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("victim selection: no eligible non-host client");

        instance.BroadcastVictim(victim.Value.id.value);
        instance.BroadcastTransferCommand();

        var failures = string.Empty;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneTransferCorrectionRoot.VictimReturnedCount >= 1,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures = $"victim reconnect timeout: victim={victim.Value.id.value}, " +
                       $"returned={SceneTransferCorrectionRoot.VictimReturnedCount}, {DescribeState(ctx)}";
        }

        if (SceneTransferCorrectionRoot.LocalInstance)
            SceneTransferCorrectionRoot.LocalInstance.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneTransferCorrectionRoot.ServerDoneCount >= ctx.expectedConnections,
                _barrierTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            var message = $"done timeout: done={SceneTransferCorrectionRoot.ServerDoneCount}/{ctx.expectedConnections}";
            failures = string.IsNullOrEmpty(failures) ? message : $"{failures} | {message}";
        }

        return string.IsNullOrEmpty(failures)
            ? ScenarioResult.Ok($"victim={victim.Value.id.value}")
            : ScenarioResult.Fail(failures);
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var initial = await WaitForLocalTargetState(ctx, "initial hierarchy", _spawnTimeoutSeconds);
        if (!initial.success)
            return initial;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial load barrier timeout: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneTransferCorrectionRoot.VictimIdReceived
                      && SceneTransferCorrectionRoot.TransferCommandReceived,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"victim selection timeout: {DescribeState(ctx)}");
        }

        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == SceneTransferCorrectionRoot.VictimId;

        if (isVictim)
        {
            int rootSpawnCountBeforeTransfer = SceneTransferCorrectionRoot.SpawnCount;
            int childSpawnCountBeforeTransfer = SceneTransferCorrectionChild.SpawnCount;

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
                return ScenarioResult.Fail($"reconnect timeout: {DescribeState(ctx)}");
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => HasLocalTargetState(ctx)
                          && SceneTransferCorrectionRoot.SpawnCount > rootSpawnCountBeforeTransfer
                          && SceneTransferCorrectionChild.SpawnCount > childSpawnCountBeforeTransfer,
                    _transferTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    "scene reload/hierarchy respawn timeout: " +
                    $"rootSpawns={SceneTransferCorrectionRoot.SpawnCount} (was {rootSpawnCountBeforeTransfer}), " +
                    $"childSpawns={SceneTransferCorrectionChild.SpawnCount} (was {childSpawnCountBeforeTransfer}), " +
                    DescribeState(ctx));
            }

            var afterTransfer = CheckBadIds("scene reload/hierarchy respawn");
            if (!afterTransfer.success)
                return afterTransfer;

            if (!SceneTransferCorrectionRoot.LocalInstance)
                return ScenarioResult.Fail($"hierarchy respawn: root missing before victim report: {DescribeState(ctx)}");

            SceneTransferCorrectionRoot.LocalInstance.SignalVictimReturned();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneTransferCorrectionRoot.PhaseDoneReceived,
                _barrierTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"phase done timeout: {DescribeState(ctx)}");
        }

        if (SceneTransferCorrectionRoot.LocalInstance)
            SceneTransferCorrectionRoot.LocalInstance.SignalDone();

        return ScenarioResult.Ok(isVictim ? "victim transfer corrected" : "observer");
    }

    private SceneTransferCorrectionRoot SpawnInTargetScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> WaitForLocalTargetState(
        ScenarioContext ctx, string phase, float timeoutSeconds)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasLocalTargetState(ctx),
                timeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        return CheckBadIds(phase);
    }

    private static ScenarioResult CheckBadIds(string phase)
    {
        if (SceneTransferCorrectionRoot.SawBadId)
            return ScenarioResult.Fail($"{phase}: root spawned without a network id");

        if (SceneTransferCorrectionChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: child spawned with a default/unassigned id (Server:0)");

        return ScenarioResult.Ok();
    }

    private static bool HasLocalTargetState(ScenarioContext ctx)
    {
        var root = SceneTransferCorrectionRoot.LocalInstance;
        return IsTargetSceneLoaded()
               && IsNetworkTargetSceneLoaded(ctx.networkManager)
               && root != null
               && root.IsInScene(TargetSceneName)
               && SceneTransferCorrectionRoot.AliveCount == 1
               && SceneTransferCorrectionRoot.AliveInTargetSceneCount == 1
               && SceneTransferCorrectionChild.AliveCount == ExpectedChildren
               && SceneTransferCorrectionChild.AliveInTargetSceneCount == ExpectedChildren;
    }

    private static bool TryGetTargetScene(NetworkManager manager, int targetBuildIndex, out Scene scene)
    {
        scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid()
               && scene.isLoaded
               && manager.sceneModule != null
               && manager.sceneModule.TryGetScene(targetBuildIndex, out _);
    }

    private static bool IsTargetSceneLoaded()
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsNetworkTargetSceneLoaded(NetworkManager manager)
    {
        int buildIndex = GetTargetBuildIndex();
        return buildIndex >= 0
               && manager.sceneModule != null
               && manager.sceneModule.IsSceneLoaded(buildIndex);
    }

    private static string DescribeState(ScenarioContext ctx)
    {
        var root = SceneTransferCorrectionRoot.LocalInstance;
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        return $"client={ctx.networkManager.isClient}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"targetSceneLoaded={scene.IsValid() && scene.isLoaded}, " +
               $"networkSceneLoaded={IsNetworkTargetSceneLoaded(ctx.networkManager)}, " +
               $"root={root != null}, rootScene={(root ? root.gameObject.scene.name : "<none>")}, " +
               $"roots={SceneTransferCorrectionRoot.AliveCount}/1, " +
               $"rootsInTarget={SceneTransferCorrectionRoot.AliveInTargetSceneCount}/1, " +
               $"rootBadId={SceneTransferCorrectionRoot.SawBadId}, " +
               $"children={SceneTransferCorrectionChild.AliveCount}/{ExpectedChildren}, " +
               $"childrenInTarget={SceneTransferCorrectionChild.AliveInTargetSceneCount}/{ExpectedChildren}, " +
               $"childBadId={SceneTransferCorrectionChild.SawBadId}";
    }

    private static int GetTargetBuildIndex()
    {
        return SceneUtility.GetBuildIndexByScenePath(TargetScenePath);
    }

    private static PlayerID? PickVictim(ScenarioContext ctx)
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
}

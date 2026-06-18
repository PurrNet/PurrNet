using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneMembershipSwitchScenario : Scenario
{
    private const string TargetSceneAName = "SceneMembershipTargetA";
    private const string TargetSceneBName = "SceneMembershipTargetB";
    private const string TargetSceneAPath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const string TargetSceneBPath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const int BarrierBase = 6200;
    private const int ExpectedChildren = 2;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static ulong _victimId;
    private static int _phase;

    private SceneMembershipSwitchRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SceneMembershipSwitchRoot));
        _prefab = rootGo.AddComponent<SceneMembershipSwitchRoot>();

        AddChild(rootGo, "SwitchChildA");
        AddChild(rootGo, "SwitchChildB");

        rootGo.SetActive(false);
        SceneMembershipSwitchRoot.ResetAll();
        SceneMembershipSwitchChild.ResetAll();
        _victimId = 0;
        _phase = 0;
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<SceneMembershipSwitchChild>();
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
        int buildA = GetBuildIndex(TargetSceneAPath);
        int buildB = GetBuildIndex(TargetSceneBPath);
        if (buildA < 0 || buildB < 0)
            return ScenarioResult.Fail($"target scenes missing from build settings: A={buildA}, B={buildB}");

        var loadA = await LoadPrivateScene(ctx, TargetSceneAName, buildA, "load A");
        if (!loadA.success) return loadA;
        var sceneA = SceneManager.GetSceneByName(TargetSceneAName);

        var loadB = await LoadPrivateScene(ctx, TargetSceneBName, buildB, "load B");
        if (!loadB.success) return loadB;
        var sceneB = SceneManager.GetSceneByName(TargetSceneBName);

        if (!TryGetSceneId(ctx, buildA, out var sceneAId) || !TryGetSceneId(ctx, buildB, out var sceneBId))
            return ScenarioResult.Fail($"network scene ids missing after load: {DescribeState(ctx)}");

        var instanceA = SpawnInScene(sceneA);
        var instanceB = SpawnInScene(sceneB);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipSwitchRoot.ServerAliveCount == 2
                      && SceneMembershipSwitchChild.ServerAliveCount == ExpectedChildren * 2,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server spawn timeout: {DescribeState(ctx)}");
        }

        if (SceneMembershipSwitchRoot.SawBadId || SceneMembershipSwitchChild.SawBadId)
            return ScenarioResult.Fail($"server spawn saw default/unassigned id: {DescribeState(ctx)}");

        var victim = PickNonHostClient(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client for scene membership switch");

        var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
        BroadcastPhase(victim.Value.id.value, 1);
        scenePlayers.AddPlayerToScene(victim.Value, sceneAId);

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene A membership barrier timeout: {DescribeState(ctx)}");
        }

        scenePlayers.RemovePlayerFromScene(victim.Value, sceneAId);
        BroadcastPhase(victim.Value.id.value, 2);

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene A removal barrier timeout: {DescribeState(ctx)}");
        }

        scenePlayers.AddPlayerToScene(victim.Value, sceneBId);
        BroadcastPhase(victim.Value.id.value, 3);

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene B membership barrier timeout: {DescribeState(ctx)}");
        }

        if (instanceA)
            instanceA.Despawn();
        if (instanceB)
            instanceB.Despawn();

        var cleanup = await UnloadTargets(ctx, buildA, buildB);
        if (!cleanup.success)
            return cleanup;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 4, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup barrier timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok($"victim={victim.Value.id.value}");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phase >= 1, _membershipTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive scene A membership phase");
        }

        bool isVictim = IsLocalVictim(ctx);
        var phaseA = isVictim
            ? await WaitForClientScene(ctx, TargetSceneAName, ExpectedChildren, "scene A membership")
            : await VerifyBystanderExcluded(ctx, "scene A membership");
        if (!phaseA.success) return phaseA;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene A membership barrier timeout: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phase >= 2, _membershipTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive scene A removal phase");
        }

        var removed = isVictim
            ? await WaitForClientEmpty(ctx, TargetSceneAName, "scene A removal")
            : await VerifyBystanderExcluded(ctx, "scene A removal");
        if (!removed.success) return removed;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene A removal barrier timeout: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phase >= 3, _membershipTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive scene B membership phase");
        }

        var phaseB = isVictim
            ? await WaitForClientScene(ctx, TargetSceneBName, ExpectedChildren, "scene B membership")
            : await VerifyBystanderExcluded(ctx, "scene B membership");
        if (!phaseB.success) return phaseB;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene B membership barrier timeout: {DescribeState(ctx)}");
        }

        var cleanup = await WaitForClientEmpty(ctx, null, "cleanup");
        if (!cleanup.success) return cleanup;

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 4, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup barrier timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok(isVictim ? "victim switched A->B" : "bystander excluded");
    }

    private SceneMembershipSwitchRoot SpawnInScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> UnloadTargets(ScenarioContext ctx, int buildA, int buildB)
    {
        if (IsSceneLoaded(TargetSceneAName))
            ctx.networkManager.sceneModule.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneAName));
        if (IsSceneLoaded(TargetSceneBName))
            ctx.networkManager.sceneModule.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneBName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipSwitchRoot.ServerAliveCount == 0
                      && SceneMembershipSwitchChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildA)
                      && !IsNetworkSceneLoaded(ctx, buildB),
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx, string sceneName, int expectedChildren, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipSwitchRoot.ClientAliveCount == 1
                      && SceneMembershipSwitchChild.ClientAliveCount == expectedChildren
                      && SceneMembershipSwitchRoot.ClientSceneName == sceneName,
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (SceneMembershipSwitchRoot.SawBadId || SceneMembershipSwitchChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: default/unassigned id observed: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientEmpty(
        ScenarioContext ctx, string unloadedSceneName, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneMembershipSwitchRoot.ClientAliveCount == 0
                      && SceneMembershipSwitchChild.ClientAliveCount == 0
                      && (string.IsNullOrEmpty(unloadedSceneName) || !IsSceneLoaded(unloadedSceneName)),
                _despawnTimeoutSeconds,
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
        if (ctx.role == NetworkRole.Host)
            return ScenarioResult.Ok();

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        if (SceneMembershipSwitchRoot.ClientAliveCount != 0 || SceneMembershipSwitchChild.ClientAliveCount != 0)
            return ScenarioResult.Fail($"{phase}: bystander received private scene hierarchy: {DescribeState(ctx)}");

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
        return $"role={ctx.role}, phase={_phase}, victim={_victimId}, " +
               $"clientRoots={SceneMembershipSwitchRoot.ClientAliveCount}, " +
               $"clientChildren={SceneMembershipSwitchChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientScene={SceneMembershipSwitchRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={SceneMembershipSwitchRoot.ServerAliveCount}, " +
               $"serverChildren={SceneMembershipSwitchChild.ServerAliveCount}, " +
               $"rootBadId={SceneMembershipSwitchRoot.SawBadId}, childBadId={SceneMembershipSwitchChild.SawBadId}, " +
               $"sceneA={IsSceneLoaded(TargetSceneAName)}, sceneB={IsSceneLoaded(TargetSceneBName)}";
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhase(ulong victimId, int phase)
    {
        _victimId = victimId;
        _phase = phase;
    }
}

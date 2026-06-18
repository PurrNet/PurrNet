using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoadUnloadCycleScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetA";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const int BarrierBase = 6300;
    private const int ExpectedChildren = 3;
    private const int Cycles = 2;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
    [SerializeField] private float _unloadTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private SceneLoadUnloadCycleRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SceneLoadUnloadCycleRoot));
        _prefab = rootGo.AddComponent<SceneLoadUnloadCycleRoot>();

        var branch = AddChild(rootGo, "CycleBranch");
        AddChild(branch, "CycleLeafA");
        AddChild(rootGo, "CycleLeafB");

        rootGo.SetActive(false);
        SceneLoadUnloadCycleRoot.ResetAll();
        SceneLoadUnloadCycleChild.ResetAll();
    }

    private static GameObject AddChild(GameObject parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.AddComponent<SceneLoadUnloadCycleChild>();
        return go;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"target scene missing from build settings: {TargetScenePath}");

        bool cleanupNeeded = false;
        try
        {
            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                int rootSpawnsBefore = SceneLoadUnloadCycleRoot.ClientSpawnCount;
                int childSpawnsBefore = SceneLoadUnloadCycleChild.ClientSpawnCount;
                int barrier = BarrierBase + cycle * 10;

                if (ctx.isServer)
                {
                    var loaded = await LoadPrivateScene(ctx, buildIndex, cycle);
                    if (!loaded.success) return loaded;
                    cleanupNeeded = true;

                    if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
                        return ScenarioResult.Fail($"cycle {cycle}: network scene id missing after load: {DescribeState(ctx)}");

                    var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
                    var players = GetClientPlayers(ctx);
                    if (players.Count == 0)
                        return ScenarioResult.Fail("no client players available for scene load/unload cycle");

                    for (int i = 0; i < players.Count; i++)
                        scenePlayers.AddPlayerToScene(players[i], sceneId);

                    try
                    {
                        await UniTaskUtils.WaitWithTimeout(
                            () => AllPlayersLoaded(scenePlayers, players, sceneId),
                            _membershipTimeoutSeconds,
                            ctx.cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        return ScenarioResult.Fail(
                            $"cycle {cycle}: players did not load private scene: {DescribeState(ctx)}");
                    }

                    SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));
                }

                var spawn = ctx.isServer
                    ? await WaitForServerSpawn(ctx, cycle)
                    : await WaitForClientSpawn(ctx, cycle, rootSpawnsBefore, childSpawnsBefore);
                if (!spawn.success) return spawn;

                try
                {
                    await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);
                }
                catch (TimeoutException)
                {
                    return ScenarioResult.Fail($"cycle {cycle}: spawn barrier timeout: {DescribeState(ctx)}");
                }

                if (ctx.isServer)
                    RequestUnloadTarget(ctx, buildIndex);

                var unload = ctx.isServer
                    ? await WaitForServerUnload(ctx, buildIndex, cycle)
                    : await WaitForClientUnload(ctx, cycle);
                if (!unload.success) return unload;

                if (ctx.isServer)
                    cleanupNeeded = false;

                try
                {
                    await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);
                }
                catch (TimeoutException)
                {
                    return ScenarioResult.Fail($"cycle {cycle}: unload barrier timeout: {DescribeState(ctx)}");
                }
            }

            return ScenarioResult.Ok($"{Cycles} private scene load/unload cycles");
        }
        finally
        {
            if (ctx.isServer && cleanupNeeded)
                await CleanupTarget(ctx, buildIndex);
        }
    }

    private SceneLoadUnloadCycleRoot SpawnInScene(Scene targetScene)
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

    private async UniTask<ScenarioResult> LoadPrivateScene(ScenarioContext ctx, int buildIndex, int cycle)
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
            return ScenarioResult.Fail($"cycle {cycle}: LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cycle {cycle}: scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private void RequestUnloadTarget(ScenarioContext ctx, int buildIndex)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (IsNetworkSceneLoaded(ctx, buildIndex))
            ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
        else
            SceneManager.UnloadSceneAsync(scene);
    }

    private async UniTask CleanupTarget(ScenarioContext ctx, int buildIndex)
    {
        RequestUnloadTarget(ctx, buildIndex);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneLoadUnloadCycleRoot.ServerAliveCount == 0
                      && SceneLoadUnloadCycleChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    private async UniTask<ScenarioResult> WaitForServerSpawn(ScenarioContext ctx, int cycle)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneLoadUnloadCycleRoot.ServerAliveCount == 1
                      && SceneLoadUnloadCycleChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cycle {cycle}: server spawn timeout: {DescribeState(ctx)}");
        }

        if (SceneLoadUnloadCycleRoot.SawBadId || SceneLoadUnloadCycleChild.SawBadId)
            return ScenarioResult.Fail($"cycle {cycle}: server saw missing/default id: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientSpawn(
        ScenarioContext ctx, int cycle, int rootSpawnsBefore, int childSpawnsBefore)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneLoadUnloadCycleRoot.ClientAliveCount == 1
                      && SceneLoadUnloadCycleChild.ClientAliveCount == ExpectedChildren
                      && SceneLoadUnloadCycleRoot.ClientSceneName == TargetSceneName
                      && SceneLoadUnloadCycleRoot.ClientSpawnCount > rootSpawnsBefore
                      && SceneLoadUnloadCycleChild.ClientSpawnCount >= childSpawnsBefore + ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cycle {cycle}: client spawn timeout: {DescribeState(ctx)}");
        }

        if (SceneLoadUnloadCycleRoot.SawBadId || SceneLoadUnloadCycleChild.SawBadId)
            return ScenarioResult.Fail($"cycle {cycle}: client saw missing/default id: {DescribeState(ctx)}");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForServerUnload(ScenarioContext ctx, int buildIndex, int cycle)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneLoadUnloadCycleRoot.ServerAliveCount == 0
                      && SceneLoadUnloadCycleChild.ServerAliveCount == 0
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cycle {cycle}: server unload timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientUnload(ScenarioContext ctx, int cycle)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneLoadUnloadCycleRoot.ClientAliveCount == 0
                      && SceneLoadUnloadCycleChild.ClientAliveCount == 0
                      && !IsSceneLoaded(TargetSceneName),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cycle {cycle}: client unload timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static List<PlayerID> GetClientPlayers(ScenarioContext ctx)
    {
        var result = new List<PlayerID>();
        var hostLocal = ctx.networkManager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? ctx.networkManager.localPlayer
            : (PlayerID?)null;
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].isServer)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == players[i])
                continue;
            result.Add(players[i]);
        }

        return result;
    }

    private static bool AllPlayersLoaded(ScenePlayersModule scenePlayers, List<PlayerID> players, SceneID sceneId)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (!scenePlayers.IsPlayerLoadedInScene(players[i], sceneId))
                return false;
        }

        return true;
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

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}, " +
               $"clientRoots={SceneLoadUnloadCycleRoot.ClientAliveCount}, " +
               $"clientChildren={SceneLoadUnloadCycleChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={SceneLoadUnloadCycleRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={SceneLoadUnloadCycleChild.ClientSpawnCount}, " +
               $"clientScene={SceneLoadUnloadCycleRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={SceneLoadUnloadCycleRoot.ServerAliveCount}, " +
               $"serverChildren={SceneLoadUnloadCycleChild.ServerAliveCount}, " +
               $"rootBadId={SceneLoadUnloadCycleRoot.SawBadId}, childBadId={SceneLoadUnloadCycleChild.SawBadId}";
    }
}

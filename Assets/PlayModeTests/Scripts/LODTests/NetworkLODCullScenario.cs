using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// LOD culling contract: with cullBeyondLastTier + a LODVisibilityRule override, moving beyond the
/// last band removes every observer (clients despawn) and moving back re-adds them (clients respawn).
/// </summary>
public class NetworkLODCullScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _visibilityTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 120f;

    private const int BarrierBase = 5700;

    private static readonly Vector3 BasePos = new Vector3(-1000f, 0f, 0f);

    private NetworkLODCullTarget _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(NetworkLODCullTarget));
        _prefab = go.AddComponent<NetworkLODCullTarget>();
        var lod = go.AddComponent<NetworkLOD>();

        var profile = ScriptableObject.CreateInstance<NetworkLODProfile>();
        profile.Configure(new[]
        {
            new NetworkLODTier { maxDistance = 30f, hysteresis = 5f, sendIntervalTicks = 1 }
        }, true);
        lod.profile = profile;

        go.SetActive(false);
        NetworkLODCullTarget.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        var ruleSet = ScriptableObject.CreateInstance<NetworkVisibilityRuleSet>();
        ruleSet.Setup(manager);
        ruleSet.AddRule(manager, ScriptableObject.CreateInstance<LODVisibilityRule>());

        var identities = _prefab.GetComponents<NetworkIdentity>();
        for (var i = 0; i < identities.Length; i++)
            identities[i].SetVisibilityRules(ruleSet);

        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkLODCullTarget instance = null;
        GameObject anchorGo = null;
        NetworkLODFactory lodFactory = null;

        if (ctx.isServer)
        {
            lodFactory = ctx.networkManager.lodModule;
            if (lodFactory == null)
                return ScenarioResult.Fail("NetworkLODFactory module not found");

            anchorGo = new GameObject("NetworkLODCullScenarioAnchor");
            anchorGo.transform.position = BasePos;

            var players = ctx.networkManager.players;
            for (var i = 0; i < players.Count; i++)
            {
                if (!players[i].isServer)
                    lodFactory.RegisterAnchor(players[i], anchorGo.transform);
            }

            HierarchyV2.SupressAutoOwner();
            try { instance = Instantiate(_prefab, BasePos + new Vector3(10f, 0f, 0f), Quaternion.identity); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkLODCullTarget.localInstance != null && NetworkLODCullTarget.aliveCount == 1,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"initial spawn never observed: alive={NetworkLODCullTarget.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        if (ctx.isServer)
            instance.transform.position = BasePos + new Vector3(100f, 0f, 0f);

        try
        {
            if (ctx.isServer)
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => instance.observers.Count == 0,
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
            else
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => NetworkLODCullTarget.localInstance == null && NetworkLODCullTarget.aliveCount == 0,
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(ctx.isServer
                ? $"cull: observers never emptied: {instance.observers.Count}"
                : $"cull: client never saw the despawn: alive={NetworkLODCullTarget.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (ctx.isServer)
            instance.transform.position = BasePos + new Vector3(10f, 0f, 0f);

        try
        {
            if (ctx.isServer)
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => instance.observers.Count == ctx.expectedConnections,
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
            else
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => NetworkLODCullTarget.localInstance != null && NetworkLODCullTarget.aliveCount == 1,
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(ctx.isServer
                ? $"un-cull: observers={instance.observers.Count}/{ctx.expectedConnections}"
                : $"un-cull: client never saw the respawn: alive={NetworkLODCullTarget.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            var players = ctx.networkManager.players;
            for (var i = 0; i < players.Count; i++)
            {
                if (!players[i].isServer)
                    lodFactory.UnregisterAnchor(players[i], anchorGo.transform);
            }

            Destroy(anchorGo);
            instance.Despawn();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkLODCullTarget.localInstance == null && NetworkLODCullTarget.aliveCount == 0,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cleanup incomplete: alive={NetworkLODCullTarget.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 4, _barrierTimeoutSeconds);

        return ScenarioResult.Ok("cull/un-cull cycle drove observer removal and re-add");
    }
}

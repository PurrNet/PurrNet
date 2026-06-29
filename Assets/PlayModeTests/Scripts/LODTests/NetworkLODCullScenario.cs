using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// LOD culling contract: cullBeyondLastTier send-culls LOD-aware traffic, but it does not change
/// observer membership or despawn the object on clients.
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
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkLODCullTarget instance = null;
        NetworkLOD lod = null;
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
            lod = instance.GetComponent<NetworkLOD>();
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
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => instance.observers.Count == ctx.expectedConnections,
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"initial observers={instance.observers.Count}/{ctx.expectedConnections}");
            }

            instance.transform.position = BasePos + new Vector3(100f, 0f, 0f);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => AllPlayersSendCulled(ctx, instance, lod),
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"send-cull: observers={instance.observers.Count}/{ctx.expectedConnections}, " +
                    $"tiers={DescribePlayerTiers(ctx, lod)}");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (!ctx.isServer && (NetworkLODCullTarget.localInstance == null || NetworkLODCullTarget.aliveCount != 1))
            return ScenarioResult.Fail($"send-cull despawned client object: alive={NetworkLODCullTarget.aliveCount}");

        if (ctx.isServer)
        {
            instance.transform.position = BasePos + new Vector3(10f, 0f, 0f);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => AllPlayersSendActive(ctx, instance, lod),
                    _visibilityTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"un-cull: observers={instance.observers.Count}/{ctx.expectedConnections}, " +
                    $"tiers={DescribePlayerTiers(ctx, lod)}");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        if (!ctx.isServer && (NetworkLODCullTarget.localInstance == null || NetworkLODCullTarget.aliveCount != 1))
            return ScenarioResult.Fail($"un-cull lost client object: alive={NetworkLODCullTarget.aliveCount}");

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

        return ScenarioResult.Ok("send-cull/un-cull preserved observers and client liveness");
    }

    private static bool AllPlayersSendCulled(ScenarioContext ctx, NetworkIdentity identity, NetworkLOD lod)
    {
        if (!identity || !lod || identity.observers.Count != ctx.expectedConnections)
            return false;

        var players = ctx.networkManager.players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            if (!lod.IsCulled(player))
                return false;

            if (identity.ShouldSendLODToPlayer(player))
                return false;
        }

        return true;
    }

    private static bool AllPlayersSendActive(ScenarioContext ctx, NetworkIdentity identity, NetworkLOD lod)
    {
        if (!identity || !lod || identity.observers.Count != ctx.expectedConnections)
            return false;

        var players = ctx.networkManager.players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            if (lod.GetTier(player) != 0)
                return false;

            if (!identity.ShouldSendLODToPlayer(player))
                return false;
        }

        return true;
    }

    private static string DescribePlayerTiers(ScenarioContext ctx, NetworkLOD lod)
    {
        if (!lod)
            return "missing-lod";

        var players = ctx.networkManager.players;
        string result = "";

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            if (result.Length > 0)
                result += ",";

            result += $"{player.id.value}:{lod.GetTier(player)}:{lod.ShouldSendToPlayer(player)}";
        }

        return result;
    }
}

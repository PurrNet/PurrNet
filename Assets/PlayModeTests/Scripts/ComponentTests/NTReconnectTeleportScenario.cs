using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

// Reproduces reconnecting players restored to a saved position while an observer stays connected.
// Both ordinary respawns and pooled reuse must restore the stationary pose before motion resumes.
// A delayed owner restore establishes an origin anchor first, exercising adaptive seam correction.
public class NTReconnectTeleportScenario : Scenario
{
    private enum RestoreMode { ServerSpawnAtSavedPosition, OwnerOnSpawn, OwnerAfterOriginSync }

    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _convergeTimeoutSeconds = 6f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10000;
    private const float PositionEpsilon = 0.1f;
    private static readonly Vector3 Movement = new(8f, 0f, -3f);

    public static ulong ownerId { get; private set; }
    public static int spawnPlan { get; private set; }
    public static Vector3 savedPosition { get; private set; }
    public static bool restoreOwnerOnSpawn { get; private set; }

    private static bool _ownerReceived;
    private static int _disconnectPlan;
    private static readonly HashSet<int> _returnedPlans = new();
    private readonly NTReconnectTeleportIdentity[] _prefabs = new NTReconnectTeleportIdentity[4];

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        ownerId = 0;
        spawnPlan = -1;
        restoreOwnerOnSpawn = false;
        _ownerReceived = false;
        _disconnectPlan = -1;
        _returnedPlans.Clear();
        NTReconnectTeleportIdentity.ResetAll();

        for (int i = 0; i < _prefabs.Length; i++)
        {
            bool pooled = (i & 1) != 0;
            bool adaptive = i < 2;
            var go = new GameObject($"NTReconnectTeleport_{(pooled ? "Pooled" : "Fresh")}_{(adaptive ? "Adaptive" : "EveryTick")}");
            go.SetActive(false);
            _prefabs[i] = go.AddComponent<NTReconnectTeleportIdentity>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.adaptiveSync = adaptive;
            manager.prefabProvider.AddRuntimePrefab(go.name, go, pooled, 0);
        }
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();
        PlayerID? owner = null;
        if (ctx.isServer)
        {
            var remotePlayers = new List<PlayerID>();
            foreach (var player in ctx.networkManager.players)
            {
                if (player.isServer || (ctx.role == NetworkRole.Host && player == ctx.networkManager.localPlayer))
                    continue;
                remotePlayers.Add(player);
            }
            remotePlayers.Sort((a, b) => a.id.value.CompareTo(b.id.value));
            if (remotePlayers.Count < 2)
                return ScenarioResult.Fail($"need an external owner and observer, got {remotePlayers.Count} external clients");
            owner = remotePlayers[0];
            BroadcastOwner(owner.Value.id.value);
        }

        await UniTaskUtils.WaitWithTimeout(() => _ownerReceived, _spawnTimeoutSeconds, ctx.cancellationToken);
        bool isOwnerPeer = ctx.isClient && ctx.networkManager.localPlayer.id.value == ownerId;

        // Each mode runs fresh and pooled. The every-tick control shares the delayed restore flow.
        for (int testCase = 0; testCase < 8; testCase++)
        {
            bool pooled = (testCase & 1) != 0;
            bool adaptive = testCase < 6;
            var mode = (RestoreMode)Math.Min(testCase / 2, (int)RestoreMode.OwnerAfterOriginSync);
            int prefabIndex = (adaptive ? 0 : 2) + (pooled ? 1 : 0);
            string caseName = $"{mode}, pooled={pooled}, adaptive={adaptive}";

            for (int life = 0; life < 2; life++)
            {
                int plan = testCase * 2 + life;
                int barrier = BarrierBase + plan * 10;
                var expected = life == 0 ? new Vector3(512f, 4f, -128f) : new Vector3(-480f, 8f, 192f);
                string phase = $"{caseName}, {(life == 0 ? "initial" : "reconnect")}";

                if (ctx.isServer)
                    BroadcastSpawnPlan(plan, expected, mode == RestoreMode.OwnerOnSpawn);

                await UniTaskUtils.WaitWithTimeout(() => spawnPlan == plan,
                    _reconnectTimeoutSeconds, ctx.cancellationToken);
                // Ensure the restore plan is installed before any client's OnSpawned callback.
                await ScenarioBarrier.Wait(ctx, barrier, _barrierTimeoutSeconds);

                if (ctx.isServer)
                {
                    Debug.Log($"[NTReconnectTeleportScenario] {phase}, savedPosition={expected:F3}");
                    NTReconnectTeleportIdentity inst;
                    HierarchyV2.SupressAutoOwner();
                    // PlayerSpawner supplies the spawn pose to Instantiate before the spawn snapshot
                    // is flushed. Keep that supported initialization path as the non-teleport control.
                    var spawnPosition = mode == RestoreMode.ServerSpawnAtSavedPosition ? expected : Vector3.zero;
                    try { inst = Instantiate(_prefabs[prefabIndex], spawnPosition, Quaternion.identity); }
                    finally { HierarchyV2.ResumeAutoOwner(); }
                    inst.GiveOwnership(owner.Value, propagateToChildren: true);
                }

                await UniTaskUtils.WaitWithTimeout(
                    () => NTReconnectTeleportIdentity.localInstance &&
                          NTReconnectTeleportIdentity.localInstance.isSpawned &&
                          (!isOwnerPeer || NTReconnectTeleportIdentity.localInstance.GetComponent<NetworkTransform>().isOwner),
                    _spawnTimeoutSeconds, ctx.cancellationToken);

                if (mode == RestoreMode.OwnerAfterOriginSync)
                {
                    await CheckPosition(ctx, Vector3.zero, phase + " origin", failures);
                    // Establish a stationary received anchor before restoring asynchronously loaded save data.
                    await UniTask.WaitForSeconds(0.3f, cancellationToken: ctx.cancellationToken);
                }
                await ScenarioBarrier.Wait(ctx, barrier + 1, _barrierTimeoutSeconds);

                if (isOwnerPeer && mode == RestoreMode.OwnerAfterOriginSync)
                    NTReconnectTeleportIdentity.localInstance.transform.position = expected;

                if (isOwnerPeer && mode == RestoreMode.OwnerOnSpawn &&
                    NTReconnectTeleportIdentity.lastOwnerSpawnTeleport != plan)
                    failures.Add($"{phase}: saved position was not assigned in owner OnSpawned");

                // Check the saved pose before doing any motion that could hide the spawn/restore defect.
                await CheckPosition(ctx, expected, phase + " stationary restore", failures);
                await ScenarioBarrier.Wait(ctx, barrier + 2, _barrierTimeoutSeconds);

                if (isOwnerPeer)
                {
                    var trs = NTReconnectTeleportIdentity.localInstance.transform;
                    var start = trs.position;
                    var target = expected + Movement;
                    float startedAt = Time.unscaledTime;
                    const float duration = 1f;
                    while (Time.unscaledTime - startedAt < duration)
                    {
                        float progress = (Time.unscaledTime - startedAt) / duration;
                        trs.position = Vector3.Lerp(start, target, progress);
                        await UniTask.NextFrame(ctx.cancellationToken);
                    }
                    trs.position = target;
                }
                await CheckPosition(ctx, expected + Movement, phase + " subsequent owner movement", failures);
                await ScenarioBarrier.Wait(ctx, barrier + 3, _barrierTimeoutSeconds);

                if (life == 0)
                {
                    if (ctx.isServer)
                        BroadcastDisconnect(plan);
                    await UniTaskUtils.WaitWithTimeout(() => _disconnectPlan == plan,
                        _reconnectTimeoutSeconds, ctx.cancellationToken);

                    if (isOwnerPeer)
                    {
                        var manager = ctx.networkManager;
                        manager.StopClient();
                        await UniTaskUtils.WaitWithTimeout(() => manager.clientState == ConnectionState.Disconnected,
                            _reconnectTimeoutSeconds, ctx.cancellationToken);
                        await UniTask.WaitForSeconds(0.5f, cancellationToken: ctx.cancellationToken);
                        manager.StartClient();
                        await UniTaskUtils.WaitWithTimeout(() => manager.isClient && manager.isLocalPlayerReady,
                            _reconnectTimeoutSeconds, ctx.cancellationToken);
                        ReportReturned(plan);
                    }
                    if (ctx.isServer)
                        await UniTaskUtils.WaitWithTimeout(() => _returnedPlans.Contains(plan),
                            _reconnectTimeoutSeconds, ctx.cancellationToken);
                }
                else if (ctx.isServer)
                {
                    NTReconnectTeleportIdentity.localInstance.Despawn();
                }

                await UniTaskUtils.WaitWithTimeout(() => !NTReconnectTeleportIdentity.localInstance,
                    _spawnTimeoutSeconds, ctx.cancellationToken);
                await ScenarioBarrier.Wait(ctx, barrier + 4, _barrierTimeoutSeconds);
            }
        }

        return failures.Count == 0
            ? ScenarioResult.Ok("saved-position restores and movement converged on owner/server/observer across 8 fresh/pooled reconnect cases")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask CheckPosition(ScenarioContext ctx, Vector3 expected, string phase, List<string> failures)
    {
        try
        {
            double stableSince = -1;
            await UniTaskUtils.WaitWithTimeout(() =>
                {
                    if (!PositionMatches(expected))
                    {
                        stableSince = -1;
                        return false;
                    }
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (stableSince < 0)
                        stableSince = now;
                    return now - stableSince >= 0.25;
                },
                _convergeTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"{phase}: pose did not converge and remain stable: {DescribePosition(expected)}");
        }
    }

    private static bool PositionMatches(Vector3 expected) => NTReconnectTeleportIdentity.localInstance &&
        Vector3.Distance(NTReconnectTeleportIdentity.localInstance.transform.position, expected) <= PositionEpsilon;

    private static string DescribePosition(Vector3 expected)
    {
        var inst = NTReconnectTeleportIdentity.localInstance;
        return inst
            ? $"position={inst.transform.position:F3}, expected={expected:F3}, error={Vector3.Distance(inst.transform.position, expected):F3}m, owner={inst.GetComponent<NetworkTransform>().isOwner}"
            : $"missing instance, expected={expected:F3}";
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastOwner(ulong playerId)
    {
        ownerId = playerId;
        _ownerReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastSpawnPlan(int plan, Vector3 position, bool ownerOnSpawn)
    {
        spawnPlan = plan;
        savedPosition = position;
        restoreOwnerOnSpawn = ownerOnSpawn;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastDisconnect(int plan) => _disconnectPlan = plan;

    [ServerRpc(requireOwnership: false)]
    private static void ReportReturned(int plan) => _returnedPlans.Add(plan);
}

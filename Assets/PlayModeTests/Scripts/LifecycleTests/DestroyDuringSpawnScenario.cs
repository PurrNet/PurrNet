using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Regression coverage for the destroy-during-spawn pipeline wedge: a networked object destroyed
// while its client->server spawn handshake is still in flight must not leave the spawn pipeline
// broken. A picked client spawns a burst of churn objects (each destroyed on the server the instant
// it is created), then one marker object. The marker must still replicate to every peer.
public class DestroyDuringSpawnScenario : Scenario
{
    [Tooltip("NetworkRules asset with spawnAuth=Everyone so a non-server client can spawn. " +
             "Same asset ClientSpawnScenario uses.")]
    [SerializeField] private NetworkRules _rules;

    [SerializeField] private int _churnCount = 16;
    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierStart = 4200;
    private const int BarrierEnd = 4201;

    private DestroyDuringSpawnIdentity _churnPrefab;
    private DestroyDuringSpawnIdentity _nestedChurnPrefab;
    private DestroyDuringSpawnIdentity _immediateDestroyPrefab;
    private DestroyDuringSpawnIdentity[] _callbackDestroyPrefabs;
    private DestroyDuringSpawnMarkerIdentity _markerPrefab;

    private static ulong _spawnerIdBroadcast;
    private static bool _spawnerIdReceived;

    void CreatePrefab()
    {
        var churnGo = new GameObject(nameof(DestroyDuringSpawnIdentity));
        _churnPrefab = churnGo.AddComponent<DestroyDuringSpawnIdentity>();
        churnGo.SetActive(false);

        // A multi-identity (parent + child) churn variant so destroy-mid-spawn also exercises
        // cleanup of a spawn entry with more than one identity.
        var nestedGo = new GameObject(nameof(DestroyDuringSpawnIdentity) + "Nested");
        _nestedChurnPrefab = nestedGo.AddComponent<DestroyDuringSpawnIdentity>();
        var nestedChildGo = new GameObject("Child");
        nestedChildGo.transform.SetParent(nestedGo.transform);
        var nestedChild = nestedChildGo.AddComponent<DestroyDuringSpawnChildIdentity>();
        nestedGo.SetActive(false);

        var markerGo = new GameObject(nameof(DestroyDuringSpawnMarkerIdentity));
        _markerPrefab = markerGo.AddComponent<DestroyDuringSpawnMarkerIdentity>();
        markerGo.SetActive(false);

        _immediateDestroyPrefab = CreateDestroyPrefab("Immediate", DestroyDuringSpawnStage.None);
        _callbackDestroyPrefabs = new[]
        {
            CreateDestroyPrefab("Awake", DestroyDuringSpawnStage.Awake),
            CreateDestroyPrefab("EarlySpawn", DestroyDuringSpawnStage.EarlySpawn),
            CreateDestroyPrefab("EarlySpawnAsServer", DestroyDuringSpawnStage.EarlySpawnAsServer),
            CreateDestroyPrefab("Spawned", DestroyDuringSpawnStage.Spawned),
            CreateDestroyPrefab("SpawnedAsServer", DestroyDuringSpawnStage.SpawnedAsServer),
            CreateDestroyPrefab("Despawned", DestroyDuringSpawnStage.Despawned),
            CreateDestroyPrefab("DespawnedAsServer", DestroyDuringSpawnStage.DespawnedAsServer)
        };

        if (_rules)
        {
            ApplyRules(_churnPrefab);
            ApplyRules(_nestedChurnPrefab);
            nestedChild.SetNetworkRules(_rules);
            ApplyRules(_immediateDestroyPrefab);
            for (int i = 0; i < _callbackDestroyPrefabs.Length; i++)
                ApplyRules(_callbackDestroyPrefabs[i]);
            _markerPrefab.SetNetworkRules(_rules);
        }
        else
        {
            Debug.LogError("[DestroyDuringSpawnScenario] _rules is not assigned; the default rules likely " +
                           "have spawnAuth=Server and the client-side spawn will be rejected.");
        }

        DestroyDuringSpawnIdentity.ResetAll();
        DestroyDuringSpawnMarkerIdentity.ResetAll();
        _spawnerIdBroadcast = 0;
        _spawnerIdReceived = false;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        if (_rules)
            manager.SetNetworkRules(_rules);

        manager.prefabProvider.AddRuntimePrefab(_churnPrefab.name, _churnPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_nestedChurnPrefab.name, _nestedChurnPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_immediateDestroyPrefab.name, _immediateDestroyPrefab.gameObject);
        for (int i = 0; i < _callbackDestroyPrefabs.Length; i++)
            manager.prefabProvider.AddRuntimePrefab(_callbackDestroyPrefabs[i].name, _callbackDestroyPrefabs[i].gameObject);
        manager.prefabProvider.AddRuntimePrefab(_markerPrefab.name, _markerPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.players.Count >= ctx.expectedConnections,
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"players-sync timeout: have {ctx.networkManager.players.Count}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierStart, _barrierTimeoutSeconds);

        // Server-only pick + broadcast: pure clients can't identify the host-local id themselves.
        if (ctx.isServer)
        {
            var picked = PickSpawner(ctx);
            if (!picked.HasValue)
                return ScenarioResult.Fail("no eligible non-host client to act as spawner");
            BroadcastSpawnerId(picked.Value.id.value);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _spawnerIdReceived,
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("spawner-id broadcast not received");
        }

        var spawnerId = _spawnerIdBroadcast;
        bool isLocalSpawner = ctx.networkManager.isLocalPlayerReady
                              && ctx.networkManager.localPlayer.id.value == spawnerId;

        // The spawner spawns each churn object and destroys it ~a frame later, while its spawn
        // handshake is still completing on the server. Then it spawns one marker that must still
        // replicate everywhere: a destroy mid-handshake must not wedge the pipeline.
        if (isLocalSpawner)
        {
            await RunDestroyStageCases();

            for (int i = 0; i < _churnCount; i++)
            {
                // alternate flat (single identity) and nested (parent + child) churn objects
                var churn = (i % 2 == 0) ? Instantiate(_churnPrefab) : Instantiate(_nestedChurnPrefab);
                await UniTask.NextFrame();
                Destroy(churn.gameObject);
                await UniTask.NextFrame();
            }

            Instantiate(_markerPrefab);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => DestroyDuringSpawnMarkerIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            Debug.LogError(
                $"[DestroyDuringSpawn] marker never replicated (pipeline wedged?): role={ctx.role} " +
                $"isLocalSpawner={isLocalSpawner} serverSaw={DestroyDuringSpawnIdentity.ServerSawCount} " +
                $"players={ctx.networkManager.players.Count}/{ctx.expectedConnections}");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail("marker never replicated after destroy-during-spawn churn");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return ScenarioResult.Ok(
            $"marker replicated after {_churnCount} destroy-during-spawn cycles " +
            $"(serverSaw={DestroyDuringSpawnIdentity.ServerSawCount}, destroyCalls={DestroyDuringSpawnIdentity.DestroyCallCount})");
    }

    private DestroyDuringSpawnIdentity CreateDestroyPrefab(string suffix, DestroyDuringSpawnStage stage)
    {
        var go = new GameObject($"{nameof(DestroyDuringSpawnIdentity)}{suffix}");
        var identity = go.AddComponent<DestroyDuringSpawnIdentity>();
        identity.Configure(stage);
        go.SetActive(false);
        return identity;
    }

    private void ApplyRules(DestroyDuringSpawnIdentity identity)
    {
        identity.SetNetworkRules(_rules);
    }

    private async UniTask RunDestroyStageCases()
    {
        var immediate = Instantiate(_immediateDestroyPrefab);
        Destroy(immediate.gameObject);
        await UniTask.NextFrame();
        await UniTask.NextFrame();

        for (int i = 0; i < _callbackDestroyPrefabs.Length; i++)
        {
            var spawned = Instantiate(_callbackDestroyPrefabs[i]);
            await UniTask.NextFrame();

            if (spawned && ShouldDestroyAfterSpawn(spawned))
                Destroy(spawned.gameObject);

            await UniTask.NextFrame();
            await UniTask.NextFrame();
        }
    }

    private static bool ShouldDestroyAfterSpawn(DestroyDuringSpawnIdentity identity)
    {
        return identity.Stage is DestroyDuringSpawnStage.Despawned or DestroyDuringSpawnStage.DespawnedAsServer;
    }

    private static PlayerID? PickSpawner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;
            if (!best.HasValue || p.id.value < best.Value.id.value)
                best = p;
        }
        return best;
    }

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private static void BroadcastSpawnerId(ulong spawnerId)
    {
        _spawnerIdBroadcast = spawnerId;
        _spawnerIdReceived = true;
    }
}

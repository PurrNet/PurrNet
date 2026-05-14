using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class ClientSpawnScenario : Scenario
{
    [Tooltip("NetworkRules asset used for both the manager and the runtime prefab. Must have " +
             "spawnAuth=Everyone so a non-server client can spawn the identity.")]
    [SerializeField] private NetworkRules _rules;

    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _validateTimeoutSeconds = 15f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierStart = 4100;
    private const int BarrierEnd = 4101;

    private ClientSpawnIdentity _prefab;

    private static ulong _spawnerIdBroadcast;
    private static bool _spawnerIdReceived;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ClientSpawnScenario));
        _prefab = go.AddComponent<ClientSpawnIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[ClientSpawnScenario] _rules is not assigned; the default rules likely have " +
                           "spawnAuth=Server and the client-side spawn will be rejected.");

        ClientSpawnIdentity.ResetAll();
        _spawnerIdBroadcast = 0;
        _spawnerIdReceived = false;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        // The manager-level rules are what the server consults to authorise a client-initiated
        // spawn (HierarchyV2 line ~797). Per-prefab rules alone are not enough.
        if (_rules)
            manager.SetNetworkRules(_rules);

        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        // Wait until every peer's player list is fully populated so the deterministic spawner
        // pick agrees across processes.
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

        // Only the server can correctly exclude the host's local player from the
        // candidate set; pure clients have no way to identify the host-local id.
        // Server picks and broadcasts; every peer waits for the broadcast.
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

        if (isLocalSpawner)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ClientSpawnIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("LocalInstance never assigned (spawn never reached this peer)");
            Debug.LogError(
                $"[ClientSpawn] timeout: spawnerId={spawnerId} isLocalSpawner={isLocalSpawner} " +
                $"role={ctx.role} localReady={ctx.networkManager.isLocalPlayerReady} " +
                $"localId={(ctx.networkManager.isLocalPlayerReady ? ctx.networkManager.localPlayer.id.value.ToString() : "<unready>")} " +
                $"players={ctx.networkManager.players.Count}/{ctx.expectedConnections} " +
                $"ownerChanges={ClientSpawnIdentity.ChangeRecords.Count} observerAdds={ClientSpawnIdentity.ObserverAdds.Count}");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        // Give a beat for OnOwnerChanged + OnObserverAdded to land after OnSpawned. OnObserverAdded
        // fires server-side only, so a pure-client peer waits on the owner-change record alone.
        // Host has both perspectives in one process, and reliable host-loopback is now deferred by
        // a tick (PurrTransportLayer), so the server-side record arrives first and the client-side
        // record on the next ReceiveMessages drain — wait for both before validating.
        int expectedOwnerChanges = ctx.isServer && ctx.isClient ? 2 : 1;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ClientSpawnIdentity.ChangeRecords.Count >= expectedOwnerChanges
                      && (!ctx.isServer || ClientSpawnIdentity.ObserverAdds.Count >= 1),
                _validateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"callbacks did not fire in time: ownerChanges={ClientSpawnIdentity.ChangeRecords.Count}, " +
                $"observerAdds={ClientSpawnIdentity.ObserverAdds.Count}");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        ValidateOwnerChange(ctx, spawnerId, isLocalSpawner, failures);
        ValidateObserverAdded(ctx, spawnerId, failures);

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Spawner={spawnerId}, isLocalSpawner={isLocalSpawner}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void ValidateOwnerChange(ScenarioContext ctx, ulong spawnerId, bool isLocalSpawner,
        List<string> failures)
    {
        // Find the initial null->spawner transition. Multiple records may exist when a peer plays
        // both server and client roles (host); the *first* record on each side is the initial grant.
        bool foundClientSide = false;
        bool foundServerSide = false;

        for (int i = 0; i < ClientSpawnIdentity.ChangeRecords.Count; i++)
        {
            var rec = ClientSpawnIdentity.ChangeRecords[i];
            if (rec.oldOwnerHasValue) continue; // not the initial null->X transition
            if (!rec.newOwnerHasValue) continue;
            if (rec.newOwnerId != spawnerId) continue;

            if (rec.asServer)
            {
                foundServerSide = true;
                // isOwner == (owner == localPlayer). On a pure server localPlayer is unset, so
                // isOwner is always false. In host mode the server's localPlayer can equal the
                // spawner (when the host's local client initiated the spawn), in which case
                // isOwner is legitimately true server-side.
                bool serverIsSpawner = isLocalSpawner;
                if (rec.isOwnerAfter != serverIsSpawner)
                    failures.Add(
                        $"server-side OnOwnerChanged for spawner: isOwner={rec.isOwnerAfter}, expected {serverIsSpawner}");
            }
            else
            {
                foundClientSide = true;

                if (isLocalSpawner && !rec.isOwnerAfter)
                    failures.Add(
                        $"spawner peer expected isOwner=true after own spawn, got false");
                if (!isLocalSpawner && rec.isOwnerAfter)
                    failures.Add(
                        $"non-spawner peer expected isOwner=false, got true");
            }
        }

        // Server (host or pure-server) must have seen the asServer=true record.
        if (ctx.isServer && !foundServerSide)
            failures.Add($"server did not record initial null->spawner OnOwnerChanged for spawner {spawnerId}");

        // Every peer with a client side (host or pure-client) must have seen the asServer=false record.
        if (ctx.isClient && !foundClientSide)
            failures.Add($"client side did not record initial null->spawner OnOwnerChanged for spawner {spawnerId}");
    }

    private static void ValidateObserverAdded(ScenarioContext ctx, ulong spawnerId, List<string> failures)
    {
        // OnObserverAdded fires only on the server side. Pure-client peers must record zero
        // observer adds; server-side peers must have observed the spawner specifically.
        if (!ctx.isServer)
        {
            if (ClientSpawnIdentity.ObserverAdds.Count != 0)
                failures.Add(
                    $"client peer expected 0 OnObserverAdded records (server-only callback), got {ClientSpawnIdentity.ObserverAdds.Count}");
            return;
        }

        bool sawSpawner = false;
        for (int i = 0; i < ClientSpawnIdentity.ObserverAdds.Count; i++)
        {
            if (ClientSpawnIdentity.ObserverAdds[i].playerId == spawnerId)
            {
                sawSpawner = true;
                break;
            }
        }
        if (!sawSpawner)
            failures.Add($"server did not record OnObserverAdded(player={spawnerId})");
    }

    private static PlayerID? PickSpawner(ScenarioContext ctx)
    {
        // Server-only pick: choose the lowest-id non-server, non-host-local player.
        // The result is broadcast to all peers via BroadcastSpawnerId so pure clients
        // don't have to identify the host-local id themselves.
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

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

        var spawner = PickSpawner(ctx);
        if (!spawner.HasValue)
            return ScenarioResult.Fail("no eligible non-host client to act as spawner");

        var spawnerId = spawner.Value.id.value;

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
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        // Give a beat for OnOwnerChanged + OnObserverAdded to land after OnSpawned.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ClientSpawnIdentity.ChangeRecords.Count >= 1
                      && ClientSpawnIdentity.ObserverAdds.Count >= 1,
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
        ValidateObserverAdded(ctx, spawnerId, isLocalSpawner, failures);

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
                if (!rec.selfRequest)
                    failures.Add(
                        $"server-side OnOwnerChanged for spawner expected selfRequest=true (new owner is the spawner)");
                if (rec.isOwnerAfter)
                    failures.Add(
                        $"server-side OnOwnerChanged for spawner expected isOwner=false on server (server is not the new owner)");
            }
            else
            {
                foundClientSide = true;
                if (!rec.selfRequest)
                    failures.Add(
                        $"client-side OnOwnerChanged for spawner expected selfRequest=true (new owner is the spawner)");

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

    private static void ValidateObserverAdded(ScenarioContext ctx, ulong spawnerId, bool isLocalSpawner,
        List<string> failures)
    {
        // On the server side, the spawner client must show isSpawner=true and every other observer
        // must show isSpawner=false. On a non-host client, OnObserverAdded fires only for the local
        // player; that record's isSpawner reflects whether the local player IS the spawner.
        for (int i = 0; i < ClientSpawnIdentity.ObserverAdds.Count; i++)
        {
            var rec = ClientSpawnIdentity.ObserverAdds[i];
            bool shouldBeSpawner = rec.playerId == spawnerId;
            if (rec.isSpawner != shouldBeSpawner)
                failures.Add(
                    $"OnObserverAdded for player {rec.playerId} expected isSpawner={shouldBeSpawner}, got {rec.isSpawner}");
        }

        // On a pure-client peer, we should have at most a single OnObserverAdded for the local
        // player (server-spawned identities follow this pattern).
        if (ctx.role == NetworkRole.Client)
        {
            if (ClientSpawnIdentity.ObserverAdds.Count != 1)
                failures.Add(
                    $"client peer expected exactly 1 OnObserverAdded record, got {ClientSpawnIdentity.ObserverAdds.Count}");
            else if (ClientSpawnIdentity.ObserverAdds[0].playerId != ctx.networkManager.localPlayer.id.value)
                failures.Add(
                    $"client peer's single OnObserverAdded should be for the local player, got {ClientSpawnIdentity.ObserverAdds[0].playerId}");
        }

        // On the server side we must have observed the spawner specifically with isSpawner=true.
        if (ctx.isServer)
        {
            bool sawSpawnerWithFlag = false;
            for (int i = 0; i < ClientSpawnIdentity.ObserverAdds.Count; i++)
            {
                var rec = ClientSpawnIdentity.ObserverAdds[i];
                if (rec.playerId == spawnerId && rec.isSpawner)
                {
                    sawSpawnerWithFlag = true;
                    break;
                }
            }
            if (!sawSpawnerWithFlag)
                failures.Add($"server did not record OnObserverAdded(player={spawnerId}, isSpawner=true)");
        }
    }

    private static PlayerID? PickSpawner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        // If we're in host mode, exclude the host's local player so the spawner is a *pure* client.
        // (This matches what tests want to validate: a non-server client initiating the spawn.)
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
}

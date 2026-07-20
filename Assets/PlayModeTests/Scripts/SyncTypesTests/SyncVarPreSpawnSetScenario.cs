using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// A non-host client instantiates a prefab without auto-spawn, sets an owner-auth SyncVar,
/// then calls Spawn + GiveOwnership. The server and every other peer must converge to the
/// value set before Spawn, not the prefab default.
/// </summary>
public class SyncVarPreSpawnSetScenario : Scenario
{
    [Tooltip("NetworkRules asset used for both the manager and the runtime prefab. Must have " +
             "spawnAuth=Everyone so a non-server client can spawn the identity.")]
    [SerializeField] private NetworkRules _rules;

    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierStart = 4150;
    private const int BarrierEnd = 4151;

    private SyncVarPreSpawnSetIdentity _prefab;

    private static ulong _spawnerIdBroadcast;
    private static bool _spawnerIdReceived;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarPreSpawnSetScenario));
        _prefab = go.AddComponent<SyncVarPreSpawnSetIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[SyncVarPreSpawnSetScenario] _rules is not assigned; the default rules likely " +
                           "have spawnAuth=Server and the client-side spawn will be rejected.");

        SyncVarPreSpawnSetIdentity.ResetAll();
        _spawnerIdBroadcast = 0;
        _spawnerIdReceived = false;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        // The manager-level rules are what the server consults to authorise a client-initiated
        // spawn. Per-prefab rules alone are not enough.
        if (_rules)
            manager.SetNetworkRules(_rules);

        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

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

        bool isLocalSpawner = ctx.networkManager.isLocalPlayerReady
                              && ctx.networkManager.localPlayer.id.value == _spawnerIdBroadcast;

        if (isLocalSpawner)
        {
            var identity = UnityProxy.InstantiateDirectly(_prefab);
            identity.SetPayload(SyncVarPreSpawnSetIdentity.payloadValue);
            identity.Spawn(_prefab.gameObject, ctx.networkManager);
            identity.GiveOwnership(ctx.networkManager.localPlayer);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarPreSpawnSetIdentity.localInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);

            var inst = SyncVarPreSpawnSetIdentity.localInstance;

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.currentValue == SyncVarPreSpawnSetIdentity.payloadValue,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"pre-spawn SyncVar value never converged: expected {SyncVarPreSpawnSetIdentity.payloadValue}, " +
                    $"got {inst.currentValue}");
            }
        }
        catch (TimeoutException)
        {
            failures.Add("localInstance never assigned (spawn never reached this peer)");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        // The spawner must still hold its pre-spawn value after everyone settled (no clobber
        // back to the prefab default).
        if (isLocalSpawner && SyncVarPreSpawnSetIdentity.localInstance != null
            && SyncVarPreSpawnSetIdentity.localInstance.currentValue != SyncVarPreSpawnSetIdentity.payloadValue)
        {
            failures.Add(
                $"spawner lost its own pre-spawn value: got {SyncVarPreSpawnSetIdentity.localInstance.currentValue}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok(isLocalSpawner ? "spawner" : "observer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static PlayerID? PickSpawner(ScenarioContext ctx)
    {
        // Server-only pick: choose the lowest-id non-server, non-host-local player. The result
        // is broadcast to all peers so pure clients don't have to identify the host-local id.
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

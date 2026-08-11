using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// A client-spawned owner-authoritative Animator starts with the controller's default overlay
/// weight. Its normal post-spawn setup changes that weight to zero, while the server proxy was
/// already initialized to zero. The ownership callback must not publish the pre-OnSpawned value
/// and overwrite the server/observers.
/// </summary>
public class NetworkAnimatorOwnerReconcileScenario : Scenario
{
    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _settleSeconds = 1f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierStart = 5580;
    private const int BarrierValidated = 5581;
    private const int BarrierDespawned = 5582;

    private static ulong _spawnerId;
    private static bool _spawnerIdReceived;

    private NetworkAnimatorOwnerReconcileRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NetworkAnimatorOwnerReconcileRoot));
        rootGo.SetActive(false);

        _prefab = rootGo.AddComponent<NetworkAnimatorOwnerReconcileRoot>();
        if (_rules)
            _prefab.SetNetworkRules(_rules);

        var animator = rootGo.AddComponent<Animator>();
        animator.runtimeAnimatorController =
            Resources.Load<RuntimeAnimatorController>("NetAnimLayerTestController");

        var networkAnimator = rootGo.AddComponent<NetworkAnimator>();
        networkAnimator.SetAnimator(animator);
        if (_rules)
            networkAnimator.SetNetworkRules(_rules);

        _prefab.animator = animator;
        _prefab.networkAnimator = networkAnimator;

        NetworkAnimatorOwnerReconcileRoot.ResetAll();
        _spawnerId = 0;
        _spawnerIdReceived = false;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        if (_rules)
            manager.SetNetworkRules(_rules);
        else
            Debug.LogError("[NetworkAnimatorOwnerReconcileScenario] _rules is not assigned.");

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
                $"players-sync timeout: {ctx.networkManager.players.Count}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierStart, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            var spawner = PickSpawner(ctx);
            if (!spawner.HasValue)
                return ScenarioResult.Fail("requires a non-host client that can spawn the Animator");

            BroadcastSpawnerId(spawner.Value.id.value);
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
            return ScenarioResult.Fail("spawner-id broadcast was not received");
        }

        bool isLocalSpawner = ctx.isClient
                              && ctx.networkManager.isLocalPlayerReady
                              && ctx.networkManager.localPlayer.id.value == _spawnerId;

        if (isLocalSpawner)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAnimatorOwnerReconcileRoot.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client-spawned Animator did not reach this peer");
        }

        await UniTask.WaitForSeconds(_settleSeconds, cancellationToken: ctx.cancellationToken);

        var instance = NetworkAnimatorOwnerReconcileRoot.LocalInstance;
        if (!instance || instance.animator.layerCount < 2)
        {
            failures.Add("layer test Animator was not initialized");
        }
        else
        {
            float actual = instance.animator.GetLayerWeight(1);
            if (!Mathf.Approximately(actual, 0f))
            {
                failures.Add(
                    $"overlay weight settled at {actual}; the server/observer snapshot expected 0");
            }
        }

        if (ctx.isServer &&
            !Mathf.Approximately(NetworkAnimatorOwnerReconcileRoot.ServerWeightBeforeOwnership, 0f))
        {
            failures.Add(
                $"server proxy began at {NetworkAnimatorOwnerReconcileRoot.ServerWeightBeforeOwnership}, expected 0");
        }

        if (isLocalSpawner)
        {
            if (!NetworkAnimatorOwnerReconcileRoot.OwnerAppliedPostSpawnSetup)
                failures.Add("owner post-spawn Animator setup did not execute");

            if (!Mathf.Approximately(
                    NetworkAnimatorOwnerReconcileRoot.OwnerWeightBeforePostSpawnSetup,
                    1f))
            {
                failures.Add(
                    "regression precondition failed: owner did not have the controller default weight before OnSpawned");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierValidated, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkAnimatorOwnerReconcileRoot.LocalInstance == null,
                _despawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("despawn incomplete");
        }

        await ScenarioBarrier.Wait(ctx, BarrierDespawned, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok(isLocalSpawner ? "owner retained post-spawn state" : "server/observer retained snapshot")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static PlayerID? PickSpawner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;
            if (hostLocal.HasValue && player == hostLocal.Value)
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private static void BroadcastSpawnerId(ulong spawnerId)
    {
        _spawnerId = spawnerId;
        _spawnerIdReceived = true;
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class LifecycleSpawnScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private LifecycleSpawnIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(LifecycleSpawnScenario));
        _prefab = go.AddComponent<LifecycleSpawnIdentity>();
        go.SetActive(false);
        LifecycleSpawnIdentity.ResetCounters();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
            Instantiate(_prefab);

        await UniTaskUtils.WaitWithTimeout(
            () => LifecycleSpawnIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var failures = new List<string>();
        VerifyLocalCallbacks(ctx, failures);

        if (ctx.role == NetworkRole.Server)
            return await RunAsServerOnly(ctx, failures);

        LifecycleSpawnIdentity.LocalInstance.SignalDone();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => LifecycleSpawnIdentity.ServerDoneCount >= ctx.expectedConnections,
                    _doneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"server-done timeout: got {LifecycleSpawnIdentity.ServerDoneCount}/{ctx.expectedConnections}");
            }
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx, List<string> failures)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LifecycleSpawnIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {LifecycleSpawnIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"Done={LifecycleSpawnIdentity.ServerDoneCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void VerifyLocalCallbacks(ScenarioContext ctx, List<string> failures)
    {
        bool host = ctx.role == NetworkRole.Host;
        bool serverOnly = ctx.role == NetworkRole.Server;
        bool clientOnly = ctx.role == NetworkRole.Client;

        // OnEarlySpawn() / OnSpawned() (no-arg) fire exactly once on every peer, even in host mode.
        if (LifecycleSpawnIdentity.OnEarlySpawnNoArg != 1)
            failures.Add($"OnEarlySpawn() expected 1, got {LifecycleSpawnIdentity.OnEarlySpawnNoArg}");
        if (LifecycleSpawnIdentity.OnSpawnedNoArg != 1)
            failures.Add($"OnSpawned() expected 1, got {LifecycleSpawnIdentity.OnSpawnedNoArg}");

        // (asServer) variants fire twice in host mode (once per side), once otherwise.
        int expectedServer = host || serverOnly ? 1 : 0;
        int expectedClient = host || clientOnly ? 1 : 0;

        if (LifecycleSpawnIdentity.OnEarlySpawnServer != expectedServer)
            failures.Add($"OnEarlySpawn(asServer:true) expected {expectedServer}, got {LifecycleSpawnIdentity.OnEarlySpawnServer}");
        if (LifecycleSpawnIdentity.OnEarlySpawnClient != expectedClient)
            failures.Add($"OnEarlySpawn(asServer:false) expected {expectedClient}, got {LifecycleSpawnIdentity.OnEarlySpawnClient}");
        if (LifecycleSpawnIdentity.OnSpawnedServer != expectedServer)
            failures.Add($"OnSpawned(asServer:true) expected {expectedServer}, got {LifecycleSpawnIdentity.OnSpawnedServer}");
        if (LifecycleSpawnIdentity.OnSpawnedClient != expectedClient)
            failures.Add($"OnSpawned(asServer:false) expected {expectedClient}, got {LifecycleSpawnIdentity.OnSpawnedClient}");

        // OnSpawned must run after OnEarlySpawn on the same instance.
        if (LifecycleSpawnIdentity.OnSpawnedNoArgFiredAfterEarly != 1)
            failures.Add("OnSpawned() did not fire after OnEarlySpawn()");
        if (LifecycleSpawnIdentity.OnSpawnedAsServerFiredAfterEarlyAsServer != expectedServer)
            failures.Add(
                $"OnSpawned(asServer:true) did not fire after OnEarlySpawn(asServer:true): expected {expectedServer}, got {LifecycleSpawnIdentity.OnSpawnedAsServerFiredAfterEarlyAsServer}");

        // The server-spawned object has no owner; isOwner should be false everywhere inside OnSpawned().
        if (LifecycleSpawnIdentity.IsOwnerInOnSpawnedNoArg)
            failures.Add("isOwner was true inside OnSpawned() for an ownerless server-spawned object");

        // isSpawned should be true by the time OnSpawned() runs.
        if (!LifecycleSpawnIdentity.IsSpawnedInOnSpawnedNoArg)
            failures.Add("isSpawned was false inside OnSpawned()");

        // isController for an ownerless identity == isServer. The no-arg OnSpawned fires from the
        // first TriggerSpawnEvent (server-side first on host), so host/server-only see true and
        // pure clients see false.
        bool expectedIsController = host || serverOnly;
        if (LifecycleSpawnIdentity.IsControllerInOnSpawnedNoArg != expectedIsController)
            failures.Add(
                $"isController in OnSpawned(): got {LifecycleSpawnIdentity.IsControllerInOnSpawnedNoArg}, expected {expectedIsController}");
    }
}

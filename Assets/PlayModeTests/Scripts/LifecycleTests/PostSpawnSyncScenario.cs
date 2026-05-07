using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class PostSpawnSyncScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _valueTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private PostSpawnSyncIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(PostSpawnSyncScenario));
        _prefab = go.AddComponent<PostSpawnSyncIdentity>();
        go.SetActive(false);
        PostSpawnSyncIdentity.ResetAll();
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
            () => PostSpawnSyncIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var failures = new List<string>();
        var inst = PostSpawnSyncIdentity.LocalInstance;

        // Every peer's local instance must observe the server's post-spawn write as part of
        // initial state (or as an immediate first delta). InitialValue is the construction
        // default; seeing it here means the post-spawn write either didn't propagate or got
        // overwritten back to default during initial-state delivery.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.currentValue == PostSpawnSyncIdentity.PostSpawnValue,
                _valueTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"local currentValue did not become PostSpawnValue: got {inst.currentValue}, " +
                $"expected {PostSpawnSyncIdentity.PostSpawnValue}");
        }

        if (ctx.isClient)
        {
            // Tell the server the client observed the post-spawn value (or that the wait
            // timed out — server still gets a count so it doesn't strand on the barrier).
            inst.SignalReady();
            if (inst.currentValue == PostSpawnSyncIdentity.PostSpawnValue)
                inst.SignalSawNewValue();
        }

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PostSpawnSyncIdentity.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"server-ready timeout: got {PostSpawnSyncIdentity.ServerReadyCount}/{ctx.expectedConnections}");
            }

            // Every connected client (including host's client side) must have observed the
            // post-spawn value. ExpectedConnections covers both server-only (no clients = 0)
            // and host (host's client side counts) cases.
            if (PostSpawnSyncIdentity.ServerSawNewValueCount != PostSpawnSyncIdentity.ServerReadyCount)
                failures.Add(
                    $"server saw {PostSpawnSyncIdentity.ServerSawNewValueCount} clients with new value, " +
                    $"expected all {PostSpawnSyncIdentity.ServerReadyCount} ready clients");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"value={inst.currentValue}, sawNew={PostSpawnSyncIdentity.ServerSawNewValueCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

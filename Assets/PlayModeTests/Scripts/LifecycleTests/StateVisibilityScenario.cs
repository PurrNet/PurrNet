using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class StateVisibilityScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierEnd = 4500;

    private StateVisibilityIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(StateVisibilityScenario));
        _prefab = go.AddComponent<StateVisibilityIdentity>();
        go.SetActive(false);
        StateVisibilityIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            // Instantiating the prefab and immediately calling SetSentinelValue ensures the
            // SyncVar is dirty before the spawn packet ships. Recipients should see the sentinel
            // value inside their OnSpawned, not the InitialValue.
            var inst = Instantiate(_prefab);
            inst.SetSentinelValue();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => StateVisibilityIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        var failures = new List<string>();

        if (!StateVisibilityIdentity.ValueAvailableInOnSpawned)
            failures.Add(
                $"OnSpawned saw value={StateVisibilityIdentity.ValueObservedInOnSpawned}, " +
                $"expected sentinel {StateVisibilityIdentity.ServerSetValue} — SyncVar value not " +
                $"visible inside OnSpawned");

        if (ctx.isServer && !StateVisibilityIdentity.AsServerSnapshotMatched)
            failures.Add("server-side OnSpawned did not capture sentinel value (was set before spawn)");

        // Quickly verify the post-spawn read still returns the sentinel.
        if (StateVisibilityIdentity.LocalInstance.currentValue != StateVisibilityIdentity.ServerSetValue)
            failures.Add(
                $"post-spawn currentValue={StateVisibilityIdentity.LocalInstance.currentValue}, " +
                $"expected {StateVisibilityIdentity.ServerSetValue}");

        if (ctx.isClient)
            StateVisibilityIdentity.LocalInstance.SignalReady();

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => StateVisibilityIdentity.ServerReadyCount >= ctx.expectedConnections,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"server-ready timeout: got {StateVisibilityIdentity.ServerReadyCount}/{ctx.expectedConnections}");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Value={StateVisibilityIdentity.ValueObservedInOnSpawned}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

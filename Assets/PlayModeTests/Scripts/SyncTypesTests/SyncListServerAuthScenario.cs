using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncList contract:
///  - initial contents seeded on the server are visible on every observer,
///  - every list operation (Add/Insert/Set/Remove/RemoveAt/Clear/refill) converges everywhere,
///  - a pure client is not the controller (server holds authority).
/// </summary>
public class SyncListServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncListServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncListServerAuthScenario));
        _prefab = go.AddComponent<SyncListServerAuthIdentity>();
        go.SetActive(false);
        SyncListServerAuthIdentity.ResetAll();
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
            () => SyncListServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncListServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncListServerAuthIdentity.LocalInstance;

        // Wait for every client to connect and confirm they saw the seeded initial state, so the
        // ops below can't race ahead of initial-state delivery.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections
                      && SyncListServerAuthIdentity.InitialMatchCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/initial timeout: ready={SyncListServerAuthIdentity.ServerReadyCount}, " +
                $"initialMatched={SyncListServerAuthIdentity.InitialMatchCount}, expected={ctx.expectedConnections}");
        }

        inst.RunServerOps();

        if (!inst.MatchesFinal())
            failures.Add($"server local list != expected final: {inst.Describe()}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListServerAuthIdentity.ConvergedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"convergence timeout: converged={SyncListServerAuthIdentity.ConvergedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: done={SyncListServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncListServerAuthIdentity.LocalInstance;

        // Initial state must reach this observer.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesInitial(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalInitialMatched();
        }
        catch (TimeoutException)
        {
            failures.Add($"never saw initial state; got {inst.Describe()}");
        }

        // After the server's op sequence, every observer must converge to the same final contents.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.MatchesFinal(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalConverged();
        }
        catch (TimeoutException)
        {
            failures.Add($"never converged to final; got {inst.Describe()}");
        }

        // A pure client must not be the controller of a server-authoritative list. (On host the
        // client side IS the server, so this only holds for dedicated clients.)
        if (ctx.role == NetworkRole.Client && inst.IsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth list");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncListServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncListServerAuthIdentity.LocalInstance != null)
            SyncListServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

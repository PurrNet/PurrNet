using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncHashSet contract: initial state, Add/duplicate/Remove/Clear/refill,
/// set convergence on every observer, and the controller-authority predicate on pure clients.
/// </summary>
public class SyncHashsetServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncHashsetServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncHashsetServerAuthScenario));
        _prefab = go.AddComponent<SyncHashsetServerAuthIdentity>();
        go.SetActive(false);
        SyncHashsetServerAuthIdentity.ResetAll();
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
            () => SyncHashsetServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncHashsetServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncHashsetServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncHashsetServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections
                      && SyncHashsetServerAuthIdentity.InitialMatchCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/initial timeout: ready={SyncHashsetServerAuthIdentity.ServerReadyCount}, " +
                $"initialMatched={SyncHashsetServerAuthIdentity.InitialMatchCount}, expected={ctx.expectedConnections}");
        }

        inst.RunServerOps();

        if (!inst.MatchesFinal())
            failures.Add($"server local set != expected final: {inst.Describe()}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncHashsetServerAuthIdentity.ConvergedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"convergence timeout: converged={SyncHashsetServerAuthIdentity.ConvergedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncHashsetServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: done={SyncHashsetServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncHashsetServerAuthIdentity.LocalInstance;

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

        if (ctx.role == NetworkRole.Client && inst.IsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth set");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncHashsetServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncHashsetServerAuthIdentity.LocalInstance != null)
            SyncHashsetServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

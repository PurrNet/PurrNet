using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncVar contract: initial value is visible on every observer, a sequence of
/// post-spawn changes converges everywhere, and a pure client is not the controller.
/// </summary>
public class SyncVarServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncVarServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarServerAuthScenario));
        _prefab = go.AddComponent<SyncVarServerAuthIdentity>();
        go.SetActive(false);
        SyncVarServerAuthIdentity.ResetAll();
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
            () => SyncVarServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncVarServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections
                      && SyncVarServerAuthIdentity.InitialMatchCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready/initial timeout: ready={SyncVarServerAuthIdentity.ServerReadyCount}, " +
                $"initialMatched={SyncVarServerAuthIdentity.InitialMatchCount}, expected={ctx.expectedConnections}");
        }

        inst.RunServerOps();

        if (!inst.MatchesFinal())
            failures.Add($"server local value != expected final: {inst.Describe()}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarServerAuthIdentity.ConvergedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"convergence timeout: converged={SyncVarServerAuthIdentity.ConvergedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: done={SyncVarServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"final={inst.Describe()}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarServerAuthIdentity.LocalInstance;

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
            failures.Add($"never saw initial value; got {inst.Describe()}");
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
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth var");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncVarServerAuthIdentity.LocalInstance != null)
            SyncVarServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

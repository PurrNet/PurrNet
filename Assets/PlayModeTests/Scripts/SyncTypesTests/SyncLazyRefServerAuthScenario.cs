using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncLazyRef contract: the server points the reference at a networked target,
/// and every observer resolves it to its own local copy of that target by NetworkID.
/// </summary>
public class SyncLazyRefServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncLazyRefTargetIdentity _targetPrefab;
    private SyncLazyRefCarrierIdentity _carrierPrefab;

    void CreatePrefab()
    {
        var tgo = new GameObject(nameof(SyncLazyRefTargetIdentity));
        _targetPrefab = tgo.AddComponent<SyncLazyRefTargetIdentity>();
        tgo.SetActive(false);

        var cgo = new GameObject(nameof(SyncLazyRefServerAuthScenario));
        _carrierPrefab = cgo.AddComponent<SyncLazyRefCarrierIdentity>();
        cgo.SetActive(false);

        SyncLazyRefTargetIdentity.ResetAll();
        SyncLazyRefCarrierIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_targetPrefab.name, _targetPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_carrierPrefab.name, _carrierPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            Instantiate(_targetPrefab);
            Instantiate(_carrierPrefab);
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncLazyRefCarrierIdentity.LocalInstance != null
                  && SyncLazyRefTargetIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncLazyRefCarrierIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncLazyRefCarrierIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncLazyRefCarrierIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncLazyRefCarrierIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        inst.SetRef(SyncLazyRefTargetIdentity.LocalInstance);

        if (!inst.Resolved())
            failures.Add("server's own reference did not resolve to its local target");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncLazyRefCarrierIdentity.ResolvedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"resolve timeout: resolved={SyncLazyRefCarrierIdentity.ResolvedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncLazyRefCarrierIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: {SyncLazyRefCarrierIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncLazyRefCarrierIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.Resolved(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalResolved();
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"reference never resolved to local target (refNull={inst.RefValue == null})");
        }

        if (ctx.role == NetworkRole.Client && inst.IsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth lazy ref");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncLazyRefCarrierIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncLazyRefCarrierIdentity.LocalInstance != null)
            SyncLazyRefCarrierIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

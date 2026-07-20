using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncBigData contract: the server sets a byte payload and every observer
/// receives the identical bytes via the chunked transfer; a pure client is not the controller.
/// </summary>
public class SyncBigDataServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _transferTimeoutSeconds = 60f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncBigDataServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncBigDataServerAuthScenario));
        _prefab = go.AddComponent<SyncBigDataServerAuthIdentity>();
        go.SetActive(false);
        SyncBigDataServerAuthIdentity.ResetAll();
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
            () => SyncBigDataServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncBigDataServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncBigDataServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncBigDataServerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        inst.Send();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataServerAuthIdentity.ReceivedCount >= ctx.expectedConnections,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"transfer timeout: received={SyncBigDataServerAuthIdentity.ReceivedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: {SyncBigDataServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncBigDataServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.Received(),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalReceived();
        }
        catch (TimeoutException)
        {
            failures.Add("payload never received / mismatched");
        }

        if (ctx.role == NetworkRole.Client && inst.IsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for server-auth big data");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncBigDataServerAuthIdentity.LocalInstance != null)
            SyncBigDataServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

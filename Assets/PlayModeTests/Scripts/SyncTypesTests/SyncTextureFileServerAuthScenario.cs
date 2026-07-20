using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative SyncTextureFile contract: the server sends a PNG-encoded texture and every
/// observer decodes it into a Texture2D of matching dimensions; a pure client is not the controller.
/// </summary>
public class SyncTextureFileServerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _transferTimeoutSeconds = 60f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncTextureFileServerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncTextureFileServerAuthScenario));
        _prefab = go.AddComponent<SyncTextureFileServerAuthIdentity>();
        go.SetActive(false);
        SyncTextureFileServerAuthIdentity.ResetAll();
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
            () => SyncTextureFileServerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncTextureFileServerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncTextureFileServerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTextureFileServerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncTextureFileServerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        inst.Send();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTextureFileServerAuthIdentity.ReceivedCount >= ctx.expectedConnections,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"transfer timeout: received={SyncTextureFileServerAuthIdentity.ReceivedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTextureFileServerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: {SyncTextureFileServerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncTextureFileServerAuthIdentity.LocalInstance;

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
            failures.Add("texture never received / wrong dimensions");
        }

        if (ctx.role == NetworkRole.Client && inst.IsController(false))
            failures.Add("pure client reports IsController(ownerAuth:false)=true for a server-auth texture file");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncTextureFileServerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncTextureFileServerAuthIdentity.LocalInstance != null)
            SyncTextureFileServerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

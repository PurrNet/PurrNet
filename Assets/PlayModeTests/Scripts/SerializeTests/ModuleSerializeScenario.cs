using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class ModuleSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _serializeTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private ModuleSerializeIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ModuleSerializeScenario));
        _prefab = go.AddComponent<ModuleSerializeIdentity>();
        go.SetActive(false);
        ModuleSerializeIdentity.ResetAll();
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
            () => ModuleSerializeIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var failures = new List<string>();

        if (ctx.role == NetworkRole.Client)
            await VerifyClient(ctx, failures);

        if (ctx.isServer)
            return await RunAsServer(ctx, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask VerifyClient(ScenarioContext ctx, List<string> failures)
    {
        var inst = ModuleSerializeIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleSerializeModule.DeserializeCount >= 1
                      && ModuleSerializeIdentity.IdentityDeserializeCount >= 1,
                _serializeTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"OnDeserialize never ran: module={ModuleSerializeModule.DeserializeCount}, " +
                $"identity={ModuleSerializeIdentity.IdentityDeserializeCount}");
        }

        if (ModuleSerializeModule.DeserializeCount != 1)
            failures.Add($"module OnDeserialize ran {ModuleSerializeModule.DeserializeCount} time(s), expected 1");
        if (ModuleSerializeIdentity.IdentityDeserializeCount != 1)
            failures.Add($"identity OnDeserialize ran {ModuleSerializeIdentity.IdentityDeserializeCount} time(s), expected 1");

        if (!inst.module.ReadValuesMatch)
            failures.Add($"module values mismatch: int={inst.module.readValue}, str='{inst.module.readString}'");
        if (inst.readValue != ModuleSerializeIdentity.Sentinel)
            failures.Add($"identity value mismatch: int={inst.readValue}, expected {ModuleSerializeIdentity.Sentinel}");

        if (failures.Count == 0)
            inst.SignalDeserializedOk();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expected = ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

        if (ModuleSerializeModule.DeserializeCount != 0 || ModuleSerializeIdentity.IdentityDeserializeCount != 0)
            failures.Add(
                $"OnDeserialize ran on the spawner (module={ModuleSerializeModule.DeserializeCount}, " +
                $"identity={ModuleSerializeIdentity.IdentityDeserializeCount}), expected 0");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleSerializeIdentity.ServerOkCount >= expected,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-ok timeout: got {ModuleSerializeIdentity.ServerOkCount}/{expected}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"ok={ModuleSerializeIdentity.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

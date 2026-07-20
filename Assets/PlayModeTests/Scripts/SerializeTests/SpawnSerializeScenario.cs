using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class SpawnSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _serializeTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SpawnSerializeIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SpawnSerializeScenario));
        _prefab = go.AddComponent<SpawnSerializeIdentity>();
        go.SetActive(false);
        SpawnSerializeIdentity.ResetAll();
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
            () => SpawnSerializeIdentity.LocalInstance != null,
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
        var inst = SpawnSerializeIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnSerializeIdentity.DeserializeCount >= 1,
                _serializeTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("OnDeserialize never ran on the client");
        }

        if (SpawnSerializeIdentity.DeserializeCount != 1)
            failures.Add($"OnDeserialize ran {SpawnSerializeIdentity.DeserializeCount} time(s), expected 1");

        if (!inst.ReadValuesMatch)
            failures.Add(
                $"deserialized values mismatch: int={inst.readInt}, str='{inst.readString}', " +
                $"float={inst.readFloat}, vec={inst.readVector}");

        inst.SignalDeserializedOk();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expectedDeserializers = ExpectedDeserializers(ctx);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnSerializeIdentity.SerializeCount >= expectedDeserializers,
                _serializeTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"OnSerialize fired {SpawnSerializeIdentity.SerializeCount} time(s), expected >= {expectedDeserializers}");
        }

        if (SpawnSerializeIdentity.DeserializeCount != 0)
            failures.Add(
                $"OnDeserialize ran {SpawnSerializeIdentity.DeserializeCount} time(s) on the spawner, expected 0");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SpawnSerializeIdentity.ServerOkCount >= expectedDeserializers,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-ok timeout: got {SpawnSerializeIdentity.ServerOkCount}/{expectedDeserializers}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"serialize={SpawnSerializeIdentity.SerializeCount}, ok={SpawnSerializeIdentity.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static int ExpectedDeserializers(ScenarioContext ctx) =>
        ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;
}

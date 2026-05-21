using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class MultiSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private MultiSerializeA _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(MultiSerializeScenario));
        _prefab = go.AddComponent<MultiSerializeA>();
        go.AddComponent<MultiSerializeB>();
        go.SetActive(false);
        MultiSerializeA.ResetAll();
        MultiSerializeB.ResetAll();
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

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => MultiSerializeA.LocalInstance != null && MultiSerializeB.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"spawn timeout: A={MultiSerializeA.LocalInstance != null}, B={MultiSerializeB.LocalInstance != null}");
        }

        var failures = new List<string>();

        if (ctx.role == NetworkRole.Client)
            VerifyClient(failures);

        if (ctx.isServer)
            return await RunAsServer(ctx, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void VerifyClient(List<string> failures)
    {
        var a = MultiSerializeA.LocalInstance;
        var b = MultiSerializeB.LocalInstance;

        if (MultiSerializeA.DeserializeCount != 1)
            failures.Add($"A OnDeserialize ran {MultiSerializeA.DeserializeCount} time(s), expected 1");
        if (MultiSerializeB.DeserializeCount != 1)
            failures.Add($"B OnDeserialize ran {MultiSerializeB.DeserializeCount} time(s), expected 1");

        if (!a.ReadValuesMatch)
            failures.Add($"A values mismatch: int={a.readValue}, str='{a.readString}'");
        if (!b.ReadValuesMatch)
            failures.Add($"B values mismatch: int={b.readValue}, bool={b.readBool}");

        if (failures.Count == 0)
            a.SignalDeserializedOk();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expected = ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

        if (MultiSerializeA.DeserializeCount != 0 || MultiSerializeB.DeserializeCount != 0)
            failures.Add(
                $"OnDeserialize ran on the spawner (A={MultiSerializeA.DeserializeCount}, B={MultiSerializeB.DeserializeCount}), expected 0");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => MultiSerializeA.ServerOkCount >= expected,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-ok timeout: got {MultiSerializeA.ServerOkCount}/{expected}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"ok={MultiSerializeA.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

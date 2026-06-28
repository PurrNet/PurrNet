using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class NestedSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private NestedSerializeParent _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(NestedSerializeScenario));
        _prefab = rootGo.AddComponent<NestedSerializeParent>();

        var childGo = new GameObject("Child");
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<NestedSerializeChild>();

        rootGo.SetActive(false);
        NestedSerializeParent.ResetAll();
        NestedSerializeChild.ResetAll();
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
                () => NestedSerializeParent.LocalInstance != null
                      && NestedSerializeChild.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"hierarchy spawn timeout: parent={NestedSerializeParent.LocalInstance != null}, " +
                $"child={NestedSerializeChild.LocalInstance != null}");
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
        var parent = NestedSerializeParent.LocalInstance;
        var child = NestedSerializeChild.LocalInstance;

        if (NestedSerializeParent.DeserializeCount != 1)
            failures.Add($"parent OnDeserialize ran {NestedSerializeParent.DeserializeCount} time(s), expected 1");
        if (NestedSerializeChild.DeserializeCount != 1)
            failures.Add($"child OnDeserialize ran {NestedSerializeChild.DeserializeCount} time(s), expected 1");
        if (NestedSerializeParent.EarlySpawnBeforeChildDeserialize)
            failures.Add("parent OnEarlySpawn ran before child OnDeserialize");

        if (!parent.ReadValuesMatch)
            failures.Add($"parent values mismatch: int={parent.readValue}, str='{parent.readString}'");
        if (!child.ReadValuesMatch)
            failures.Add($"child values mismatch: int={child.readValue}, float={child.readFloat}");

        if (failures.Count == 0)
            parent.SignalDeserializedOk();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expected = ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

        if (NestedSerializeParent.DeserializeCount != 0)
            failures.Add($"parent OnDeserialize ran {NestedSerializeParent.DeserializeCount} time(s) on the spawner, expected 0");
        if (NestedSerializeChild.DeserializeCount != 0)
            failures.Add($"child OnDeserialize ran {NestedSerializeChild.DeserializeCount} time(s) on the spawner, expected 0");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NestedSerializeParent.ServerOkCount >= expected,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-ok timeout: got {NestedSerializeParent.ServerOkCount}/{expected}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"ok={NestedSerializeParent.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

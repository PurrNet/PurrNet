using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class EmptySerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _settleSeconds = 1f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private EmptySerializeIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(EmptySerializeScenario));
        _prefab = go.AddComponent<EmptySerializeIdentity>();
        go.SetActive(false);
        EmptySerializeIdentity.ResetAll();
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
                () => EmptySerializeIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("identity never spawned");
        }

        await UniTask.WaitForSeconds(_settleSeconds, cancellationToken: ctx.cancellationToken);

        var failures = new List<string>();

        if (ctx.role == NetworkRole.Client)
        {
            if (EmptySerializeIdentity.DeserializeCount != 0)
                failures.Add(
                    $"OnDeserialize ran {EmptySerializeIdentity.DeserializeCount} time(s) for an empty OnSerialize, expected 0 (hasCustomData gate)");

            if (failures.Count == 0)
                EmptySerializeIdentity.LocalInstance.SignalOk();
        }

        if (ctx.isServer)
            return await RunAsServer(ctx, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx, List<string> failures)
    {
        int expected = ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => EmptySerializeIdentity.ServerOkCount >= expected,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-ok timeout: got {EmptySerializeIdentity.ServerOkCount}/{expected}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"ok={EmptySerializeIdentity.ServerOkCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Coverage for generic RPC codegen on NetworkIdentity instances:
//   - Generic NetworkIdentity class (IdentityGenericRpcs<T>) closed over int + string,
//     each spawned as its own prefab. Exercises class-T RPCs, generic struct payloads,
//     and class-T + method-T combinations.
//   - Multi-parameter generic METHODS on the same identity.
public class IdentityGenericRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private IdentityGenericRpcsInt _intPrefab;
    private IdentityGenericRpcsString _stringPrefab;

    void CreatePrefabs()
    {
        var intGo = new GameObject(nameof(IdentityGenericRpcsInt));
        _intPrefab = intGo.AddComponent<IdentityGenericRpcsInt>();
        intGo.SetActive(false);

        var stringGo = new GameObject(nameof(IdentityGenericRpcsString));
        _stringPrefab = stringGo.AddComponent<IdentityGenericRpcsString>();
        stringGo.SetActive(false);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefabs();
        manager.prefabProvider.AddRuntimePrefab(_intPrefab.name, _intPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_stringPrefab.name, _stringPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.role == NetworkRole.Server)
        {
            Instantiate(_intPrefab);
            Instantiate(_stringPrefab);
            return await RunAsServerOnly(ctx);
        }

        if (ctx.isServer)
        {
            Instantiate(_intPrefab);
            Instantiate(_stringPrefab);
        }

        await UniTaskUtils.WaitWithTimeout(
            () => IdentityGenericRpcs<int>.LocalInstance != null
               && IdentityGenericRpcs<string>.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx);
        // Signal via the int-typed instance; one signal per client is enough.
        IdentityGenericRpcs<int>.LocalInstance.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityGenericRpcs<int>.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {IdentityGenericRpcs<int>.DoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={IdentityGenericRpcs<int>.DoneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var intInst = IdentityGenericRpcs<int>.LocalInstance;
        var strInst = IdentityGenericRpcs<string>.LocalInstance;

        // ---- Class-T RPCs on IdentityGenericRpcs<int> ----
        await Try(failures, "IntInst_Echo_T", async () =>
        {
            var r = await intInst.Echo_T(123);
            if (r != 123) throw new Exception($"expected 123, got {r}");
        });

        await Try(failures, "IntInst_Echo_PairOfT", async () =>
        {
            var input = new IdentityGenericRpcs<int>.GenericPair<int> { first = 4, second = 5 };
            var r = await intInst.Echo_PairOfT(input);
            if (r.first != 4 || r.second != 5)
                throw new Exception($"expected (4, 5), got ({r.first}, {r.second})");
        });

        await Try(failures, "IntInst_Echo_ListOfTCount", async () =>
        {
            var n = await intInst.Echo_ListOfTCount(new List<int> { 10, 20, 30 });
            if (n != 3) throw new Exception($"expected 3, got {n}");
        });

        // ---- Class-T RPCs on IdentityGenericRpcs<string> ----
        await Try(failures, "StrInst_Echo_T", async () =>
        {
            var r = await strInst.Echo_T("hello-T");
            if (r != "hello-T") throw new Exception($"expected 'hello-T', got '{r}'");
        });

        await Try(failures, "StrInst_Echo_PairOfT", async () =>
        {
            var input = new IdentityGenericRpcs<string>.GenericPair<string> { first = "alpha", second = "beta" };
            var r = await strInst.Echo_PairOfT(input);
            if (r.first != "alpha" || r.second != "beta")
                throw new Exception($"expected ('alpha', 'beta'), got ('{r.first}', '{r.second}')");
        });

        await Try(failures, "StrInst_Echo_ListOfTCount", async () =>
        {
            var n = await strInst.Echo_ListOfTCount(new List<string> { "x", "y" });
            if (n != 2) throw new Exception($"expected 2, got {n}");
        });

        // ---- Multi-param generic methods (same regardless of class T) ----
        await Try(failures, "Two_IntString_OnIntInst", async () =>
        {
            var r = await intInst.Echo_Two<int, string>(7, "hello");
            if (r != "7|hello") throw new Exception($"expected '7|hello', got '{r}'");
        });

        await Try(failures, "Two_StringInt_OnIntInst", async () =>
        {
            var r = await intInst.Echo_Two<string, int>("hi", 5);
            if (r != "hi|5") throw new Exception($"expected 'hi|5', got '{r}'");
        });

        await Try(failures, "Two_BoolString_OnStrInst", async () =>
        {
            var r = await strInst.Echo_Two<bool, string>(true, "ok");
            if (r != "True|ok") throw new Exception($"expected 'True|ok', got '{r}'");
        });

        await Try(failures, "Three_IntStringBool_OnIntInst", async () =>
        {
            var r = await intInst.Echo_Three<int, string, bool>(1, "x", true);
            if (r != "1|x|True") throw new Exception($"expected '1|x|True', got '{r}'");
        });

        await Try(failures, "Three_StringIntInt_OnStrInst", async () =>
        {
            var r = await strInst.Echo_Three<string, int, int>("a", 2, 3);
            if (r != "a|2|3") throw new Exception($"expected 'a|2|3', got '{r}'");
        });

        // ---- Class-T mixed with method-level generic ----
        await Try(failures, "Mixed_IntInst_MethodString", async () =>
        {
            var r = await intInst.Echo_Mixed<string>(42, "bar");
            if (r != "42|bar") throw new Exception($"expected '42|bar', got '{r}'");
        });

        await Try(failures, "Mixed_StrInst_MethodInt", async () =>
        {
            var r = await strInst.Echo_Mixed<int>("foo", 99);
            if (r != "foo|99") throw new Exception($"expected 'foo|99', got '{r}'");
        });

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));
        return ScenarioResult.Ok();
    }

    private static async UniTask Try(List<string> failures, string label, Func<UniTask> action)
    {
        try { await action(); }
        catch (Exception e)
        {
            failures.Add($"{label}: {e.Message}");
            Debug.LogError($"[IdentityGenericRpcsScenario] {label} failed: {e}");
        }
    }
}

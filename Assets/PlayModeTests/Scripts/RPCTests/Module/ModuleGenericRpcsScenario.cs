using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Coverage for generic RPC codegen on NetworkModules:
//   - Generic NetworkModule (ModuleGenericRpcsModule<T>) with two closed instantiations
//     (<int> + <string>) hosted on a single NetworkIdentity. Exercises class-T RPCs,
//     generic struct payloads, and class-T + method-T combinations.
//   - Multi-parameter generic METHODS on the module.
public class ModuleGenericRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private ModuleGenericRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ModuleGenericRpcsScenario));
        _prefab = go.AddComponent<ModuleGenericRpcs>();
        go.SetActive(false);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.role == NetworkRole.Server)
        {
            Instantiate(_prefab);
            return await RunAsServerOnly(ctx);
        }

        if (ctx.isServer)
            Instantiate(_prefab);

        await UniTaskUtils.WaitWithTimeout(
            () => ModuleGenericRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx);
        // Signal done via the int module; one signal per client is sufficient.
        ModuleGenericRpcs.LocalInstance.intModule.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleGenericRpcsModule<int>.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {ModuleGenericRpcsModule<int>.DoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={ModuleGenericRpcsModule<int>.DoneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var host = ModuleGenericRpcs.LocalInstance;
        var intMod = host.intModule;
        var strMod = host.stringModule;

        // ---- Class-T RPCs on ModuleGenericRpcsModule<int> ----
        await Try(failures, "IntMod_Echo_T", async () =>
        {
            var r = await intMod.Echo_T(123);
            if (r != 123) throw new Exception($"expected 123, got {r}");
        });

        await Try(failures, "IntMod_Echo_PairOfT", async () =>
        {
            var input = new ModuleGenericRpcsModule<int>.GenericPair<int> { first = 4, second = 5 };
            var r = await intMod.Echo_PairOfT(input);
            if (r.first != 4 || r.second != 5)
                throw new Exception($"expected (4, 5), got ({r.first}, {r.second})");
        });

        await Try(failures, "IntMod_Echo_ListOfTCount", async () =>
        {
            var n = await intMod.Echo_ListOfTCount(new List<int> { 10, 20, 30 });
            if (n != 3) throw new Exception($"expected 3, got {n}");
        });

        // ---- Class-T RPCs on ModuleGenericRpcsModule<string> ----
        await Try(failures, "StrMod_Echo_T", async () =>
        {
            var r = await strMod.Echo_T("hello-T");
            if (r != "hello-T") throw new Exception($"expected 'hello-T', got '{r}'");
        });

        await Try(failures, "StrMod_Echo_PairOfT", async () =>
        {
            var input = new ModuleGenericRpcsModule<string>.GenericPair<string> { first = "alpha", second = "beta" };
            var r = await strMod.Echo_PairOfT(input);
            if (r.first != "alpha" || r.second != "beta")
                throw new Exception($"expected ('alpha', 'beta'), got ('{r.first}', '{r.second}')");
        });

        await Try(failures, "StrMod_Echo_ListOfTCount", async () =>
        {
            var n = await strMod.Echo_ListOfTCount(new List<string> { "x", "y" });
            if (n != 2) throw new Exception($"expected 2, got {n}");
        });

        // ---- Multi-param generic methods ----
        await Try(failures, "Two_IntString_OnIntMod", async () =>
        {
            var r = await intMod.Echo_Two<int, string>(7, "hello");
            if (r != "7|hello") throw new Exception($"expected '7|hello', got '{r}'");
        });

        await Try(failures, "Two_StringInt_OnIntMod", async () =>
        {
            var r = await intMod.Echo_Two<string, int>("hi", 5);
            if (r != "hi|5") throw new Exception($"expected 'hi|5', got '{r}'");
        });

        await Try(failures, "Two_BoolString_OnStrMod", async () =>
        {
            var r = await strMod.Echo_Two<bool, string>(true, "ok");
            if (r != "True|ok") throw new Exception($"expected 'True|ok', got '{r}'");
        });

        await Try(failures, "Three_IntStringBool_OnIntMod", async () =>
        {
            var r = await intMod.Echo_Three<int, string, bool>(1, "x", true);
            if (r != "1|x|True") throw new Exception($"expected '1|x|True', got '{r}'");
        });

        await Try(failures, "Three_StringIntInt_OnStrMod", async () =>
        {
            var r = await strMod.Echo_Three<string, int, int>("a", 2, 3);
            if (r != "a|2|3") throw new Exception($"expected 'a|2|3', got '{r}'");
        });

        // ---- Class-T mixed with method-level generic ----
        await Try(failures, "Mixed_IntMod_MethodString", async () =>
        {
            var r = await intMod.Echo_Mixed<string>(42, "bar");
            if (r != "42|bar") throw new Exception($"expected '42|bar', got '{r}'");
        });

        await Try(failures, "Mixed_StrMod_MethodInt", async () =>
        {
            var r = await strMod.Echo_Mixed<int>("foo", 99);
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
            Debug.LogError($"[ModuleGenericRpcsScenario] {label} failed: {e}");
        }
    }
}

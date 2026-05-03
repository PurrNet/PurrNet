using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Coverage for generic RPC codegen on static methods:
//   - Multi-parameter generic METHODS (Echo_Two<T1, T2>, Echo_Three<T1, T2, T3>)
//   - Generic struct payloads (GenericPair<T>) — exercises nested generic serializer codegen
//   - Generic ObserversRpc with multiple type parameters
public class StaticGenericRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _broadcastTimeoutSeconds = 10f;

    private static int _doneCount;
    private static float _staticBroadcastTimeoutSeconds = 10f;

    private static readonly Dictionary<ulong, string> _broadcastTwoReceived = new();
    private static readonly Dictionary<ulong, string> _broadcastThreeReceived = new();

    [Serializable]
    public struct GenericPair<T>
    {
        public T first;
        public T second;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _staticBroadcastTimeoutSeconds = _broadcastTimeoutSeconds;

        if (ctx.role == NetworkRole.Server)
            return await RunAsServerOnly(ctx);

        var clientResult = await RunClientChecks(ctx);
        SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _doneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {_doneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={_doneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var corr = ctx.networkManager.localPlayer.id.value;
        var timeout = _staticBroadcastTimeoutSeconds;

        // Multi-param generic methods, mixed type combinations.
        await Try(failures, "Two_IntString", async () =>
        {
            var r = await Echo_Two<int, string>(7, "hello");
            if (r != "7|hello") throw new Exception($"expected '7|hello', got '{r}'");
        });

        await Try(failures, "Two_StringInt", async () =>
        {
            var r = await Echo_Two<string, int>("hi", 5);
            if (r != "hi|5") throw new Exception($"expected 'hi|5', got '{r}'");
        });

        await Try(failures, "Two_IntInt", async () =>
        {
            var r = await Echo_Two<int, int>(1, 2);
            if (r != "1|2") throw new Exception($"expected '1|2', got '{r}'");
        });

        await Try(failures, "Two_BoolString", async () =>
        {
            var r = await Echo_Two<bool, string>(true, "ok");
            if (r != "True|ok") throw new Exception($"expected 'True|ok', got '{r}'");
        });

        await Try(failures, "Three_IntStringBool", async () =>
        {
            var r = await Echo_Three<int, string, bool>(1, "x", true);
            if (r != "1|x|True") throw new Exception($"expected '1|x|True', got '{r}'");
        });

        await Try(failures, "Three_StringIntInt", async () =>
        {
            var r = await Echo_Three<string, int, int>("a", 2, 3);
            if (r != "a|2|3") throw new Exception($"expected 'a|2|3', got '{r}'");
        });

        // Generic struct round-trip — exercises nested generic serializer codegen for
        // GenericPair<int> and GenericPair<string>.
        await Try(failures, "GenericPair_Int", async () =>
        {
            var input = new GenericPair<int> { first = 11, second = 22 };
            var r = await Echo_GenericPair<int>(input);
            if (r.first != 11 || r.second != 22)
                throw new Exception($"expected (11, 22), got ({r.first}, {r.second})");
        });

        await Try(failures, "GenericPair_String", async () =>
        {
            var input = new GenericPair<string> { first = "alpha", second = "beta" };
            var r = await Echo_GenericPair<string>(input);
            if (r.first != "alpha" || r.second != "beta")
                throw new Exception($"expected ('alpha', 'beta'), got ('{r.first}', '{r.second}')");
        });

        // List<T> param — verifies codegen for closed-generic collection wire types.
        await Try(failures, "List_Int_Count", async () =>
        {
            var list = new List<int> { 1, 2, 3, 4 };
            var n = await Echo_ListCount<int>(list);
            if (n != 4) throw new Exception($"expected 4, got {n}");
        });

        await Try(failures, "List_String_Count", async () =>
        {
            var list = new List<string> { "a", "b", "c" };
            var n = await Echo_ListCount<string>(list);
            if (n != 3) throw new Exception($"expected 3, got {n}");
        });

        // Multi-param generic ObserversRpc. Each client triggers and waits for its own corr key.
        await Try(failures, "BroadcastTwo_IntString", async () =>
        {
            _broadcastTwoReceived.Remove(corr);
            TriggerBroadcastTwo<int, string>(corr, 9, "world");
            await UniTaskUtils.WaitWithTimeout(
                () => _broadcastTwoReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_broadcastTwoReceived[corr] != "9|world")
                throw new Exception($"expected '9|world', got '{_broadcastTwoReceived[corr]}'");
        });

        await Try(failures, "BroadcastThree_StringIntBool", async () =>
        {
            _broadcastThreeReceived.Remove(corr);
            TriggerBroadcastThree<string, int, bool>(corr, "z", 8, false);
            await UniTaskUtils.WaitWithTimeout(
                () => _broadcastThreeReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_broadcastThreeReceived[corr] != "z|8|False")
                throw new Exception($"expected 'z|8|False', got '{_broadcastThreeReceived[corr]}'");
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
            Debug.LogError($"[StaticGenericRpcsScenario] {label} failed: {e}");
        }
    }

    [ServerRpc(requireOwnership: false)]
    private static Task<string> Echo_Two<T1, T2>(T1 a, T2 b, RPCInfo info = default) =>
        Task.FromResult($"{a}|{b}");

    [ServerRpc(requireOwnership: false)]
    private static Task<string> Echo_Three<T1, T2, T3>(T1 a, T2 b, T3 c, RPCInfo info = default) =>
        Task.FromResult($"{a}|{b}|{c}");

    [ServerRpc(requireOwnership: false)]
    private static Task<GenericPair<T>> Echo_GenericPair<T>(GenericPair<T> p, RPCInfo info = default) =>
        Task.FromResult(p);

    [ServerRpc(requireOwnership: false)]
    private static Task<int> Echo_ListCount<T>(List<T> list, RPCInfo info = default) =>
        Task.FromResult(list?.Count ?? 0);

    [ServerRpc(requireOwnership: false)]
    private static void TriggerBroadcastTwo<T1, T2>(ulong corr, T1 a, T2 b, RPCInfo info = default) =>
        BroadcastTwo(corr, a, b);

    [ServerRpc(requireOwnership: false)]
    private static void TriggerBroadcastThree<T1, T2, T3>(ulong corr, T1 a, T2 b, T3 c, RPCInfo info = default) =>
        BroadcastThree(corr, a, b, c);

    [ObserversRpc]
    private static void BroadcastTwo<T1, T2>(ulong corr, T1 a, T2 b) =>
        _broadcastTwoReceived[corr] = $"{a}|{b}";

    [ObserversRpc]
    private static void BroadcastThree<T1, T2, T3>(ulong corr, T1 a, T2 b, T3 c) =>
        _broadcastThreeReceived[corr] = $"{a}|{b}|{c}";

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _doneCount++;
        Debug.Log($"[StaticGenericRpcsScenario] SignalDone received from sender {info.sender.id.value} (total {_doneCount})");
    }
}

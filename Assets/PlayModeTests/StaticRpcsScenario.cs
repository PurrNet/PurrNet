using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;

public class StaticRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private static int _doneCount;
    private static int _fireAndForgetReceivedCount;

    [Serializable]
    public struct TestPayload
    {
        public int id;
        public string label;
        public float weight;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
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

        if (_fireAndForgetReceivedCount < ctx.expectedConnections)
        {
            return ScenarioResult.Fail(
                $"Fire-and-forget RPC received only {_fireAndForgetReceivedCount}/{ctx.expectedConnections} times");
        }

        return ScenarioResult.Ok($"Done={_doneCount}, FireAndForget={_fireAndForgetReceivedCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx)
    {
        var failures = new List<string>();

        await Try(failures, "Echo_Int", async () =>
        {
            var r = await Echo_Int(42);
            if (r != 42) throw new Exception($"expected 42, got {r}");
        });

        await Try(failures, "Echo_String", async () =>
        {
            var r = await Echo_String("hello world");
            if (r != "hello world") throw new Exception($"expected 'hello world', got '{r}'");
        });

        await Try(failures, "Echo_Bool", async () =>
        {
            var r = await Echo_Bool(true);
            if (!r) throw new Exception("expected true");
        });

        await Try(failures, "Echo_FloatUni", async () =>
        {
            var r = await Echo_FloatUni(3.14f);
            if (Mathf.Abs(r - 3.14f) > 0.0001f) throw new Exception($"expected ~3.14, got {r}");
        });

        await Try(failures, "Echo_VoidUni", async () =>
        {
            await Echo_VoidUni(123);
        });

        await Try(failures, "Echo_GenericInt", async () =>
        {
            var r = await Echo_Generic<int>(7);
            if (r != 7) throw new Exception($"expected 7, got {r}");
        });

        await Try(failures, "Echo_GenericString", async () =>
        {
            var r = await Echo_Generic<string>("generic");
            if (r != "generic") throw new Exception($"expected 'generic', got '{r}'");
        });

        await Try(failures, "Echo_Struct", async () =>
        {
            var input = new TestPayload { id = 9, label = "payload", weight = 1.5f };
            var r = await Echo_Struct(input);
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct round-trip mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "Echo_MultiArg", async () =>
        {
            var sum = await Echo_Sum(2, 3, 5);
            if (sum != 10) throw new Exception($"expected 10, got {sum}");
        });

        await Try(failures, "Echo_Unreliable", async () =>
        {
            var r = await Echo_Unreliable(99);
            if (r != 99) throw new Exception($"expected 99, got {r}");
        });

        await Try(failures, "Echo_WithInfo", async () =>
        {
            // Server returns the sender id it observed. For a pure client this
            // must equal the client's own localPlayer id and must be non-zero
            // (PlayerID.Server has _id == 0, which is also default(PlayerID),
            // so a simple `== Server` comparison can't distinguish "uninitialized"
            // from "actually the server").
            var seenSenderId = await Echo_WithInfo(0);

            if (ctx.role == NetworkRole.Client)
            {
                var localId = ctx.networkManager.localPlayer.id.value;
                if (seenSenderId == 0UL)
                    throw new Exception($"server observed sender id 0 (default/Server) for a client RPC; expected client id {localId}");
                if (localId != 0UL && seenSenderId != localId)
                    throw new Exception($"server observed sender id {seenSenderId}; expected client localPlayer id {localId}");
            }
        });

        // Fire-and-forget — server tracks how many it received in _fireAndForgetReceivedCount.
        await Try(failures, "FireAndForget", async () =>
        {
            FireAndForget(123);
            await UniTask.CompletedTask;
        });

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static async UniTask Try(List<string> failures, string label, Func<UniTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            failures.Add($"{label}: {e.Message}");
            Debug.LogError($"[StaticRpcsScenario] {label} failed: {e}");
        }
    }

    // ---- RPC definitions ----

    [ServerRpc(requireOwnership: false)]
    private static Task<int> Echo_Int(int x) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false)]
    private static Task<string> Echo_String(string s) => Task.FromResult(s);

    [ServerRpc(requireOwnership: false)]
    private static Task<bool> Echo_Bool(bool b) => Task.FromResult(b);

    [ServerRpc(requireOwnership: false)]
    private static UniTask<float> Echo_FloatUni(float f) => UniTask.FromResult(f);

    [ServerRpc(requireOwnership: false)]
    private static async UniTask Echo_VoidUni(int dummy)
    {
        await UniTask.Yield();
    }

    [ServerRpc(requireOwnership: false)]
    private static Task<T> Echo_Generic<T>(T value) => Task.FromResult(value);

    [ServerRpc(requireOwnership: false)]
    private static Task<TestPayload> Echo_Struct(TestPayload p) => Task.FromResult(p);

    [ServerRpc(requireOwnership: false)]
    private static Task<int> Echo_Sum(int a, int b, int c) => Task.FromResult(a + b + c);

    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)]
    private static Task<int> Echo_Unreliable(int x) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false)]
    private static Task<ulong> Echo_WithInfo(int dummy, RPCInfo info = default)
    {
        return Task.FromResult(info.sender.id.value);
    }

    [ServerRpc(requireOwnership: false)]
    private static void FireAndForget(int x)
    {
        _fireAndForgetReceivedCount++;
        Debug.Log($"[StaticRpcsScenario] FireAndForget received: {x} (total {_fireAndForgetReceivedCount})");
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone()
    {
        _doneCount++;
        Debug.Log($"[StaticRpcsScenario] SignalDone received (total {_doneCount})");
    }
}

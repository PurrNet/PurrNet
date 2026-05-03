using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class IdentityTargetRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _targetTimeoutSeconds = 10f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private IdentityTargetRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(IdentityTargetRpcsScenario));
        _prefab = go.AddComponent<IdentityTargetRpcs>();
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
            () => IdentityTargetRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx, _targetTimeoutSeconds);
        IdentityTargetRpcs.LocalInstance.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityTargetRpcs.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {IdentityTargetRpcs.DoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={IdentityTargetRpcs.DoneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx, float timeout)
    {
        var failures = new List<string>();
        var inst = IdentityTargetRpcs.LocalInstance;

        await Try(failures, "Int", async () =>
        {
            IdentityTargetRpcs.IntReceived = null;
            inst.TriggerTargetInt(42);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.IntReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.IntReceived.HasValue) throw new Exception("int payload did not arrive");
            if (IdentityTargetRpcs.IntReceived.Value != 42) throw new Exception($"expected 42, got {IdentityTargetRpcs.IntReceived.Value}");
        });

        await Try(failures, "String", async () =>
        {
            IdentityTargetRpcs.StringReceived = null;
            inst.TriggerTargetString("tgt hello");
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.StringReceived != null, timeout, ctx.cancellationToken);
            if (IdentityTargetRpcs.StringReceived == null) throw new Exception("string payload did not arrive");
            if (IdentityTargetRpcs.StringReceived != "tgt hello") throw new Exception($"expected 'tgt hello', got '{IdentityTargetRpcs.StringReceived}'");
        });

        await Try(failures, "Struct", async () =>
        {
            IdentityTargetRpcs.StructReceived = null;
            var input = new IdentityTargetRpcs.TestPayload { id = 21, label = "tgt payload", weight = 3.5f };
            inst.TriggerTargetStruct(input);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.StructReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.StructReceived.HasValue) throw new Exception("struct payload did not arrive");
            var r = IdentityTargetRpcs.StructReceived.Value;
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "GenericInt", async () =>
        {
            IdentityTargetRpcs.GenericIntReceived = null;
            inst.TriggerTargetGeneric<int>(91);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.GenericIntReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.GenericIntReceived.HasValue) throw new Exception("generic int payload did not arrive");
            if (IdentityTargetRpcs.GenericIntReceived.Value != 91) throw new Exception($"expected 91, got {IdentityTargetRpcs.GenericIntReceived.Value}");
        });

        await Try(failures, "Compression_None", async () =>
        {
            IdentityTargetRpcs.CompNoneReceived = null;
            inst.TriggerTargetCompNone(2001);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.CompNoneReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.CompNoneReceived.HasValue) throw new Exception("compression(None) payload did not arrive");
            if (IdentityTargetRpcs.CompNoneReceived.Value != 2001) throw new Exception($"expected 2001, got {IdentityTargetRpcs.CompNoneReceived.Value}");
        });

        await Try(failures, "Compression_Fast", async () =>
        {
            IdentityTargetRpcs.CompFastReceived = null;
            inst.TriggerTargetCompFast(2002);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.CompFastReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.CompFastReceived.HasValue) throw new Exception("compression(Fast) payload did not arrive");
            if (IdentityTargetRpcs.CompFastReceived.Value != 2002) throw new Exception($"expected 2002, got {IdentityTargetRpcs.CompFastReceived.Value}");
        });

        await Try(failures, "Compression_Balanced", async () =>
        {
            IdentityTargetRpcs.CompBalancedReceived = null;
            inst.TriggerTargetCompBalanced(2003);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.CompBalancedReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.CompBalancedReceived.HasValue) throw new Exception("compression(Balanced) payload did not arrive");
            if (IdentityTargetRpcs.CompBalancedReceived.Value != 2003) throw new Exception($"expected 2003, got {IdentityTargetRpcs.CompBalancedReceived.Value}");
        });

        await Try(failures, "Compression_Best", async () =>
        {
            IdentityTargetRpcs.CompBestReceived = null;
            inst.TriggerTargetCompBest(2004);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.CompBestReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.CompBestReceived.HasValue) throw new Exception("compression(Best) payload did not arrive");
            if (IdentityTargetRpcs.CompBestReceived.Value != 2004) throw new Exception($"expected 2004, got {IdentityTargetRpcs.CompBestReceived.Value}");
        });

        await Try(failures, "Unreliable", async () =>
        {
            IdentityTargetRpcs.UnreliableReceived = null;
            for (var i = 0; i < 5; i++)
                inst.TriggerTargetUnreliable(777);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.UnreliableReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.UnreliableReceived.HasValue) throw new Exception("unreliable payload did not arrive");
            if (IdentityTargetRpcs.UnreliableReceived.Value != 777) throw new Exception($"expected 777, got {IdentityTargetRpcs.UnreliableReceived.Value}");
        });

        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            IdentityTargetRpcs.DeltaSequence.Clear();
            int[] seq = { 5, 5, 6, 6, 11 };
            for (var i = 0; i < seq.Length; i++)
                inst.TriggerTargetDeltaOff(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.DeltaSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (IdentityTargetRpcs.DeltaSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {IdentityTargetRpcs.DeltaSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (IdentityTargetRpcs.DeltaSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {IdentityTargetRpcs.DeltaSequence[i]}");
        });

        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            IdentityTargetRpcs.DeltaPackedSequence.Clear();
            int[] seq = { 300, 300, 301, 301, 400 };
            for (var i = 0; i < seq.Length; i++)
                inst.TriggerTargetDeltaOn(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.DeltaPackedSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (IdentityTargetRpcs.DeltaPackedSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {IdentityTargetRpcs.DeltaPackedSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (IdentityTargetRpcs.DeltaPackedSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {IdentityTargetRpcs.DeltaPackedSequence[i]}");
        });

        await Try(failures, "MultiTarget_Array", async () =>
        {
            IdentityTargetRpcs.MultiArrayReceived = null;
            inst.TriggerTargetArray(5101);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.MultiArrayReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.MultiArrayReceived.HasValue) throw new Exception("array-target payload did not arrive");
            if (IdentityTargetRpcs.MultiArrayReceived.Value != 5101) throw new Exception($"expected 5101, got {IdentityTargetRpcs.MultiArrayReceived.Value}");
        });

        await Try(failures, "MultiTarget_List", async () =>
        {
            IdentityTargetRpcs.MultiListReceived = null;
            inst.TriggerTargetList(5102);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.MultiListReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.MultiListReceived.HasValue) throw new Exception("list-target payload did not arrive");
            if (IdentityTargetRpcs.MultiListReceived.Value != 5102) throw new Exception($"expected 5102, got {IdentityTargetRpcs.MultiListReceived.Value}");
        });

        await Try(failures, "MultiTarget_Enumerable", async () =>
        {
            IdentityTargetRpcs.MultiEnumReceived = null;
            inst.TriggerTargetEnumerable(5103);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.MultiEnumReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.MultiEnumReceived.HasValue) throw new Exception("enumerable-target payload did not arrive");
            if (IdentityTargetRpcs.MultiEnumReceived.Value != 5103) throw new Exception($"expected 5103, got {IdentityTargetRpcs.MultiEnumReceived.Value}");
        });

        await Try(failures, "AsyncTarget_Echo", async () =>
        {
            IdentityTargetRpcs.AsyncTargetReceived = null;
            var result = await inst.TriggerAsyncTarget(99);
            if (!IdentityTargetRpcs.AsyncTargetReceived.HasValue) throw new Exception("async-target payload did not arrive at receiver");
            if (IdentityTargetRpcs.AsyncTargetReceived.Value != 99) throw new Exception($"receiver got {IdentityTargetRpcs.AsyncTargetReceived.Value}, expected 99");
            if (result != 100) throw new Exception($"async target reply expected 100, got {result}");
        });

        await Try(failures, "TargetServer_DefaultPlayerID_Rejected", async () =>
        {
            try
            {
                await inst.SendTargetServer(default, 8888);
                throw new Exception("expected RpcRejectedException, none thrown");
            }
            catch (RpcRejectedException ex)
            {
                if (ex.error != RpcError.TargetServerNotAllowed)
                    throw new Exception($"expected TargetServerNotAllowed, got {ex.error}");
            }
        });

        await Try(failures, "TargetP2P_RequireServerFalse", async () =>
        {
            IdentityTargetRpcs.P2PTargetReceived = null;
            inst.SendTargetP2P(ctx.networkManager.localPlayer, 7777);
            await UniTaskUtils.WaitWithTimeout(() => IdentityTargetRpcs.P2PTargetReceived.HasValue, timeout, ctx.cancellationToken);
            if (!IdentityTargetRpcs.P2PTargetReceived.HasValue) throw new Exception("p2p target payload did not arrive");
            if (IdentityTargetRpcs.P2PTargetReceived.Value != 7777) throw new Exception($"expected 7777, got {IdentityTargetRpcs.P2PTargetReceived.Value}");
        });

        await Try(failures, "PingAllPeers", async () =>
        {
            var snapshot = new List<PlayerID>(ctx.networkManager.players);
            var pinged = 0;
            foreach (var p in snapshot)
            {
                if (p.isServer) continue;
                var reply = await inst.PingPlayer(p);
                if (reply != p.id.value)
                    throw new Exception($"asked player {p.id.value}, got reply {reply}");
                pinged++;
            }
            if (pinged == 0) throw new Exception("no peers visible to ping");
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
            Debug.LogError($"[IdentityTargetRpcsScenario] {label} failed: {e}");
        }
    }
}

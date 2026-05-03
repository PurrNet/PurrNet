using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class ModuleTargetRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _targetTimeoutSeconds = 10f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private ModuleTargetRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ModuleTargetRpcsScenario));
        _prefab = go.AddComponent<ModuleTargetRpcs>();
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
            () => ModuleTargetRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx, _targetTimeoutSeconds);
        ModuleTargetRpcs.LocalInstance.module.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetRpcsModule.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {ModuleTargetRpcsModule.DoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={ModuleTargetRpcsModule.DoneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx, float timeout)
    {
        var failures = new List<string>();
        var module = ModuleTargetRpcs.LocalInstance.module;

        await Try(failures, "Int", async () =>
        {
            ModuleTargetRpcsModule.IntReceived = null;
            module.TriggerTargetInt(42);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.IntReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.IntReceived.HasValue) throw new Exception("int payload did not arrive");
            if (ModuleTargetRpcsModule.IntReceived.Value != 42) throw new Exception($"expected 42, got {ModuleTargetRpcsModule.IntReceived.Value}");
        });

        await Try(failures, "String", async () =>
        {
            ModuleTargetRpcsModule.StringReceived = null;
            module.TriggerTargetString("tgt hello");
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.StringReceived != null, timeout, ctx.cancellationToken);
            if (ModuleTargetRpcsModule.StringReceived == null) throw new Exception("string payload did not arrive");
            if (ModuleTargetRpcsModule.StringReceived != "tgt hello") throw new Exception($"expected 'tgt hello', got '{ModuleTargetRpcsModule.StringReceived}'");
        });

        await Try(failures, "Struct", async () =>
        {
            ModuleTargetRpcsModule.StructReceived = null;
            var input = new ModuleTargetRpcsModule.TestPayload { id = 21, label = "tgt payload", weight = 3.5f };
            module.TriggerTargetStruct(input);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.StructReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.StructReceived.HasValue) throw new Exception("struct payload did not arrive");
            var r = ModuleTargetRpcsModule.StructReceived.Value;
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "GenericInt", async () =>
        {
            ModuleTargetRpcsModule.GenericIntReceived = null;
            module.TriggerTargetGeneric<int>(91);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.GenericIntReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.GenericIntReceived.HasValue) throw new Exception("generic int payload did not arrive");
            if (ModuleTargetRpcsModule.GenericIntReceived.Value != 91) throw new Exception($"expected 91, got {ModuleTargetRpcsModule.GenericIntReceived.Value}");
        });

        await Try(failures, "Compression_None", async () =>
        {
            ModuleTargetRpcsModule.CompNoneReceived = null;
            module.TriggerTargetCompNone(2001);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.CompNoneReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.CompNoneReceived.HasValue) throw new Exception("compression(None) payload did not arrive");
            if (ModuleTargetRpcsModule.CompNoneReceived.Value != 2001) throw new Exception($"expected 2001, got {ModuleTargetRpcsModule.CompNoneReceived.Value}");
        });

        await Try(failures, "Compression_Fast", async () =>
        {
            ModuleTargetRpcsModule.CompFastReceived = null;
            module.TriggerTargetCompFast(2002);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.CompFastReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.CompFastReceived.HasValue) throw new Exception("compression(Fast) payload did not arrive");
            if (ModuleTargetRpcsModule.CompFastReceived.Value != 2002) throw new Exception($"expected 2002, got {ModuleTargetRpcsModule.CompFastReceived.Value}");
        });

        await Try(failures, "Compression_Balanced", async () =>
        {
            ModuleTargetRpcsModule.CompBalancedReceived = null;
            module.TriggerTargetCompBalanced(2003);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.CompBalancedReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.CompBalancedReceived.HasValue) throw new Exception("compression(Balanced) payload did not arrive");
            if (ModuleTargetRpcsModule.CompBalancedReceived.Value != 2003) throw new Exception($"expected 2003, got {ModuleTargetRpcsModule.CompBalancedReceived.Value}");
        });

        await Try(failures, "Compression_Best", async () =>
        {
            ModuleTargetRpcsModule.CompBestReceived = null;
            module.TriggerTargetCompBest(2004);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.CompBestReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.CompBestReceived.HasValue) throw new Exception("compression(Best) payload did not arrive");
            if (ModuleTargetRpcsModule.CompBestReceived.Value != 2004) throw new Exception($"expected 2004, got {ModuleTargetRpcsModule.CompBestReceived.Value}");
        });

        await Try(failures, "Unreliable", async () =>
        {
            ModuleTargetRpcsModule.UnreliableReceived = null;
            for (var i = 0; i < 5; i++)
                module.TriggerTargetUnreliable(777);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.UnreliableReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.UnreliableReceived.HasValue) throw new Exception("unreliable payload did not arrive");
            if (ModuleTargetRpcsModule.UnreliableReceived.Value != 777) throw new Exception($"expected 777, got {ModuleTargetRpcsModule.UnreliableReceived.Value}");
        });

        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            ModuleTargetRpcsModule.DeltaSequence.Clear();
            int[] seq = { 5, 5, 6, 6, 11 };
            for (var i = 0; i < seq.Length; i++)
                module.TriggerTargetDeltaOff(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.DeltaSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (ModuleTargetRpcsModule.DeltaSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {ModuleTargetRpcsModule.DeltaSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (ModuleTargetRpcsModule.DeltaSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {ModuleTargetRpcsModule.DeltaSequence[i]}");
        });

        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            ModuleTargetRpcsModule.DeltaPackedSequence.Clear();
            int[] seq = { 300, 300, 301, 301, 400 };
            for (var i = 0; i < seq.Length; i++)
                module.TriggerTargetDeltaOn(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.DeltaPackedSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (ModuleTargetRpcsModule.DeltaPackedSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {ModuleTargetRpcsModule.DeltaPackedSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (ModuleTargetRpcsModule.DeltaPackedSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {ModuleTargetRpcsModule.DeltaPackedSequence[i]}");
        });

        await Try(failures, "MultiTarget_Array", async () =>
        {
            ModuleTargetRpcsModule.MultiArrayReceived = null;
            module.TriggerTargetArray(5101);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.MultiArrayReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.MultiArrayReceived.HasValue) throw new Exception("array-target payload did not arrive");
            if (ModuleTargetRpcsModule.MultiArrayReceived.Value != 5101) throw new Exception($"expected 5101, got {ModuleTargetRpcsModule.MultiArrayReceived.Value}");
        });

        await Try(failures, "MultiTarget_List", async () =>
        {
            ModuleTargetRpcsModule.MultiListReceived = null;
            module.TriggerTargetList(5102);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.MultiListReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.MultiListReceived.HasValue) throw new Exception("list-target payload did not arrive");
            if (ModuleTargetRpcsModule.MultiListReceived.Value != 5102) throw new Exception($"expected 5102, got {ModuleTargetRpcsModule.MultiListReceived.Value}");
        });

        await Try(failures, "MultiTarget_Enumerable", async () =>
        {
            ModuleTargetRpcsModule.MultiEnumReceived = null;
            module.TriggerTargetEnumerable(5103);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.MultiEnumReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.MultiEnumReceived.HasValue) throw new Exception("enumerable-target payload did not arrive");
            if (ModuleTargetRpcsModule.MultiEnumReceived.Value != 5103) throw new Exception($"expected 5103, got {ModuleTargetRpcsModule.MultiEnumReceived.Value}");
        });

        await Try(failures, "AsyncTarget_Echo", async () =>
        {
            ModuleTargetRpcsModule.AsyncTargetReceived = null;
            var result = await module.TriggerAsyncTarget(99);
            if (!ModuleTargetRpcsModule.AsyncTargetReceived.HasValue) throw new Exception("async-target payload did not arrive at receiver");
            if (ModuleTargetRpcsModule.AsyncTargetReceived.Value != 99) throw new Exception($"receiver got {ModuleTargetRpcsModule.AsyncTargetReceived.Value}, expected 99");
            if (result != 100) throw new Exception($"async target reply expected 100, got {result}");
        });

        await Try(failures, "TargetServer_DefaultPlayerID_Rejected", async () =>
        {
            try
            {
                await module.SendTargetServer(default, 8888);
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
            ModuleTargetRpcsModule.P2PTargetReceived = null;
            module.SendTargetP2P(ctx.networkManager.localPlayer, 7777);
            await UniTaskUtils.WaitWithTimeout(() => ModuleTargetRpcsModule.P2PTargetReceived.HasValue, timeout, ctx.cancellationToken);
            if (!ModuleTargetRpcsModule.P2PTargetReceived.HasValue) throw new Exception("p2p target payload did not arrive");
            if (ModuleTargetRpcsModule.P2PTargetReceived.Value != 7777) throw new Exception($"expected 7777, got {ModuleTargetRpcsModule.P2PTargetReceived.Value}");
        });

        await Try(failures, "PingAllPeers", async () =>
        {
            var snapshot = new List<PlayerID>(ctx.networkManager.players);
            var pinged = 0;
            foreach (var p in snapshot)
            {
                if (p.isServer) continue;
                var reply = await module.PingPlayer(p);
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
            Debug.LogError($"[ModuleTargetRpcsScenario] {label} failed: {e}");
        }
    }
}

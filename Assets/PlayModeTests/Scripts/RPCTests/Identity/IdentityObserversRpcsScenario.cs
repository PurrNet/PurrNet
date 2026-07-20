using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class IdentityObserversRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _broadcastTimeoutSeconds = 10f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private IdentityObserversRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(IdentityObserversRpcsScenario));
        _prefab = go.AddComponent<IdentityObserversRpcs>();
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
            () => IdentityObserversRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx, _broadcastTimeoutSeconds);
        IdentityObserversRpcs.LocalInstance.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {IdentityObserversRpcs.DoneCount}/{ctx.expectedConnections}");
        }

        return ScenarioResult.Ok($"Done={IdentityObserversRpcs.DoneCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx, float timeout)
    {
        var failures = new List<string>();
        var corr = ctx.networkManager.localPlayer.id.value;
        var inst = IdentityObserversRpcs.LocalInstance;

        await Try(failures, "Int", async () =>
        {
            inst.TriggerBroadcastInt(corr, 42);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.IntReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.IntReceived[corr] != 42)
                throw new Exception($"expected 42, got {IdentityObserversRpcs.IntReceived[corr]}");
        });

        await Try(failures, "String", async () =>
        {
            inst.TriggerBroadcastString(corr, "obs hello");
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.StringReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.StringReceived[corr] != "obs hello")
                throw new Exception($"expected 'obs hello', got '{IdentityObserversRpcs.StringReceived[corr]}'");
        });

        await Try(failures, "Struct", async () =>
        {
            var input = new IdentityObserversRpcs.TestPayload { id = 11, label = "obs payload", weight = 2.25f };
            inst.TriggerBroadcastStruct(corr, input);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.StructReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            var r = IdentityObserversRpcs.StructReceived[corr];
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "GenericInt", async () =>
        {
            inst.TriggerBroadcastGeneric<int>(corr, 77);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.GenericIntReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.GenericIntReceived[corr] != 77)
                throw new Exception($"expected 77, got {IdentityObserversRpcs.GenericIntReceived[corr]}");
        });

        await Try(failures, "Compression_None", async () =>
        {
            inst.TriggerBroadcastCompNone(corr, 1001);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.CompNoneReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.CompNoneReceived[corr] != 1001)
                throw new Exception($"expected 1001, got {IdentityObserversRpcs.CompNoneReceived[corr]}");
        });

        await Try(failures, "Compression_Fast", async () =>
        {
            inst.TriggerBroadcastCompFast(corr, 1002);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.CompFastReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.CompFastReceived[corr] != 1002)
                throw new Exception($"expected 1002, got {IdentityObserversRpcs.CompFastReceived[corr]}");
        });

        await Try(failures, "Compression_Balanced", async () =>
        {
            inst.TriggerBroadcastCompBalanced(corr, 1003);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.CompBalancedReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.CompBalancedReceived[corr] != 1003)
                throw new Exception($"expected 1003, got {IdentityObserversRpcs.CompBalancedReceived[corr]}");
        });

        await Try(failures, "Compression_Best", async () =>
        {
            inst.TriggerBroadcastCompBest(corr, 1004);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.CompBestReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.CompBestReceived[corr] != 1004)
                throw new Exception($"expected 1004, got {IdentityObserversRpcs.CompBestReceived[corr]}");
        });

        await Try(failures, "Unreliable", async () =>
        {
            for (var i = 0; i < 5; i++)
                inst.TriggerBroadcastUnreliable(corr, 555);

            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.UnreliableReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (IdentityObserversRpcs.UnreliableReceived[corr] != 555)
                throw new Exception($"expected 555, got {IdentityObserversRpcs.UnreliableReceived[corr]}");
        });

        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            int[] seq = { 7, 7, 8, 8, 13 };
            for (var i = 0; i < seq.Length; i++)
                inst.TriggerBroadcastDeltaOff(corr, seq[i]);

            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.DeltaSequence.TryGetValue(corr, out var l) && l.Count >= seq.Length,
                timeout, ctx.cancellationToken);

            var got = IdentityObserversRpcs.DeltaSequence[corr];
            for (var i = 0; i < seq.Length; i++)
                if (got[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {got[i]}");
        });

        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            int[] seq = { 100, 100, 101, 101, 200 };
            for (var i = 0; i < seq.Length; i++)
                inst.TriggerBroadcastDeltaOn(corr, seq[i]);

            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.DeltaPackedSequence.TryGetValue(corr, out var l) && l.Count >= seq.Length,
                timeout, ctx.cancellationToken);

            var got = IdentityObserversRpcs.DeltaPackedSequence[corr];
            for (var i = 0; i < seq.Length; i++)
                if (got[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {got[i]}");
        });

        await Try(failures, "Broadcast_AsyncPackable", async () =>
        {
            inst.TriggerBroadcastAsyncPackable(corr, 42);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.AsyncObservedStamps.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            var observed = IdentityObserversRpcs.AsyncObservedStamps[corr];
            if (observed == null || observed.Length != 3)
                throw new Exception("observer did not capture all three stamps");
            if (observed[0] != 42) throw new Exception($"seed: expected 42, got {observed[0]}");
            if (observed[1] != 43) throw new Exception($"PrepareForPackAsync did not run on server (sender): expected 43, got {observed[1]}");
            if (observed[2] != 53) throw new Exception($"PrepareAfterUnpackAsync did not run on observer: expected 53, got {observed[2]}");
        });

        await Try(failures, "ObserversP2P_RequireServerFalse", async () =>
        {
            inst.BroadcastP2P(corr, 6789);
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversRpcs.P2PBroadcastReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (!IdentityObserversRpcs.P2PBroadcastReceived.TryGetValue(corr, out var got))
                throw new Exception("p2p broadcast did not arrive");
            if (got != 6789) throw new Exception($"expected 6789, got {got}");
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
            Debug.LogError($"[IdentityObserversRpcsScenario] {label} failed: {e}");
        }
    }
}

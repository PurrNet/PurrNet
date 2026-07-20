using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;
using CompressionLevel = PurrNet.CompressionLevel;

public class StaticObserversRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _broadcastTimeoutSeconds = 10f;

    private static int _doneCount;
    private static float _staticBroadcastTimeoutSeconds = 10f;

    // Per-process receive state, keyed by correlation id (the calling client's localPlayer.id)
    // so a client can wait for the broadcast that resulted from its own trigger, regardless of
    // broadcasts that other clients are firing concurrently.
    private static readonly Dictionary<ulong, int> _intReceived = new();
    private static readonly Dictionary<ulong, string> _stringReceived = new();
    private static readonly Dictionary<ulong, TestPayload> _structReceived = new();
    private static readonly Dictionary<ulong, int> _genericIntReceived = new();
    private static readonly Dictionary<ulong, int> _compNoneReceived = new();
    private static readonly Dictionary<ulong, int> _compFastReceived = new();
    private static readonly Dictionary<ulong, int> _compBalancedReceived = new();
    private static readonly Dictionary<ulong, int> _compBestReceived = new();
    private static readonly Dictionary<ulong, int> _unreliableReceived = new();
    private static readonly Dictionary<ulong, List<int>> _deltaSequence = new();
    private static readonly Dictionary<ulong, List<int>> _deltaPackedSequence = new();
    private static readonly Dictionary<ulong, int> _p2pBroadcastReceived = new();
    private static readonly Dictionary<ulong, int[]> _asyncObservedStamps = new();

    [Serializable]
    public struct TestPayload
    {
        public int id;
        public string label;
        public float weight;
    }

    [Serializable]
    public struct AsyncPayload : IAsyncPackable
    {
        public int seed;
        public int packStamp;
        public int unpackStamp;

        public async ValueTask<IAsyncPackable> PrepareForPackAsync()
        {
            await Task.Yield();
            packStamp = seed + 1;
            return this;
        }

        public async ValueTask<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            await Task.Yield();
            unpackStamp = packStamp + 10;
            return this;
        }
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

        await Try(failures, "Int", async () =>
        {
            TriggerBroadcastInt(corr, 42);
            await UniTaskUtils.WaitWithTimeout(
                () => _intReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_intReceived[corr] != 42)
                throw new Exception($"expected 42, got {_intReceived[corr]}");
        });

        await Try(failures, "String", async () =>
        {
            TriggerBroadcastString(corr, "obs hello");
            await UniTaskUtils.WaitWithTimeout(
                () => _stringReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_stringReceived[corr] != "obs hello")
                throw new Exception($"expected 'obs hello', got '{_stringReceived[corr]}'");
        });

        await Try(failures, "Struct", async () =>
        {
            var input = new TestPayload { id = 11, label = "obs payload", weight = 2.25f };
            TriggerBroadcastStruct(corr, input);
            await UniTaskUtils.WaitWithTimeout(
                () => _structReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            var r = _structReceived[corr];
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "GenericInt", async () =>
        {
            TriggerBroadcastGeneric<int>(corr, 77);
            await UniTaskUtils.WaitWithTimeout(
                () => _genericIntReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_genericIntReceived[corr] != 77)
                throw new Exception($"expected 77, got {_genericIntReceived[corr]}");
        });

        await Try(failures, "Compression_None", async () =>
        {
            TriggerBroadcastCompNone(corr, 1001);
            await UniTaskUtils.WaitWithTimeout(
                () => _compNoneReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_compNoneReceived[corr] != 1001)
                throw new Exception($"expected 1001, got {_compNoneReceived[corr]}");
        });

        await Try(failures, "Compression_Fast", async () =>
        {
            TriggerBroadcastCompFast(corr, 1002);
            await UniTaskUtils.WaitWithTimeout(
                () => _compFastReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_compFastReceived[corr] != 1002)
                throw new Exception($"expected 1002, got {_compFastReceived[corr]}");
        });

        await Try(failures, "Compression_Balanced", async () =>
        {
            TriggerBroadcastCompBalanced(corr, 1003);
            await UniTaskUtils.WaitWithTimeout(
                () => _compBalancedReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_compBalancedReceived[corr] != 1003)
                throw new Exception($"expected 1003, got {_compBalancedReceived[corr]}");
        });

        await Try(failures, "Compression_Best", async () =>
        {
            TriggerBroadcastCompBest(corr, 1004);
            await UniTaskUtils.WaitWithTimeout(
                () => _compBestReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_compBestReceived[corr] != 1004)
                throw new Exception($"expected 1004, got {_compBestReceived[corr]}");
        });

        await Try(failures, "Unreliable", async () =>
        {
            // Bursting reduces flakiness on lossy unreliable channels for this smoke test.
            for (var i = 0; i < 5; i++)
                TriggerBroadcastUnreliable(corr, 555);

            await UniTaskUtils.WaitWithTimeout(
                () => _unreliableReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (_unreliableReceived[corr] != 555)
                throw new Exception($"expected 555, got {_unreliableReceived[corr]}");
        });

        // deltaPacked: false baseline. Send a 5-value sequence and verify all values arrive in order.
        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            int[] seq = { 7, 7, 8, 8, 13 };
            for (var i = 0; i < seq.Length; i++)
                TriggerBroadcastDeltaOff(corr, seq[i]);

            await UniTaskUtils.WaitWithTimeout(
                () => _deltaSequence.TryGetValue(corr, out var l) && l.Count >= seq.Length,
                timeout, ctx.cancellationToken);

            var got = _deltaSequence[corr];
            for (var i = 0; i < seq.Length; i++)
                if (got[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {got[i]}");
        });

        // deltaPacked: true. Same kind of sequence — exercises the no-diff and diff paths.
        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            int[] seq = { 100, 100, 101, 101, 200 };
            for (var i = 0; i < seq.Length; i++)
                TriggerBroadcastDeltaOn(corr, seq[i]);

            await UniTaskUtils.WaitWithTimeout(
                () => _deltaPackedSequence.TryGetValue(corr, out var l) && l.Count >= seq.Length,
                timeout, ctx.cancellationToken);

            var got = _deltaPackedSequence[corr];
            for (var i = 0; i < seq.Length; i++)
                if (got[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {got[i]}");
        });

        // requireServer:false ObserversRpc — any client can originate the broadcast
        // directly (no Trigger ServerRpc). The client batches to the server, the server
        // re-broadcasts to all observers. Each client receives at least its own broadcast
        // (and any other client's broadcast that uses the same corr key, but corr is the
        // local player id so receivers see distinct entries).
        await Try(failures, "Broadcast_AsyncPackable", async () =>
        {
            TriggerBroadcastAsyncPackable(corr, 42);
            await UniTaskUtils.WaitWithTimeout(
                () => _asyncObservedStamps.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            var observed = _asyncObservedStamps[corr];
            if (observed == null || observed.Length != 3)
                throw new Exception("observer did not capture all three stamps");
            if (observed[0] != 42) throw new Exception($"seed: expected 42, got {observed[0]}");
            if (observed[1] != 43) throw new Exception($"PrepareForPackAsync did not run on server (sender): expected 43, got {observed[1]}");
            if (observed[2] != 53) throw new Exception($"PrepareAfterUnpackAsync did not run on observer: expected 53, got {observed[2]}");
        });

        await Try(failures, "ObserversP2P_RequireServerFalse", async () =>
        {
            BroadcastP2P(corr, 6789);
            await UniTaskUtils.WaitWithTimeout(
                () => _p2pBroadcastReceived.ContainsKey(corr),
                timeout, ctx.cancellationToken);
            if (!_p2pBroadcastReceived.TryGetValue(corr, out var got))
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
            Debug.LogError($"[StaticObserversRpcsScenario] {label} failed: {e}");
        }
    }

    // ----- Triggers (client → server) -----

    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastInt(ulong corr, int payload, RPCInfo info = default) => BroadcastInt(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastString(ulong corr, string payload, RPCInfo info = default) => BroadcastString(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastStruct(ulong corr, TestPayload payload, RPCInfo info = default) => BroadcastStruct(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastGeneric<T>(ulong corr, T payload, RPCInfo info = default) => BroadcastGeneric(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastCompNone(ulong corr, int payload, RPCInfo info = default) => BroadcastCompNone(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastCompFast(ulong corr, int payload, RPCInfo info = default) => BroadcastCompFast(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastCompBalanced(ulong corr, int payload, RPCInfo info = default) => BroadcastCompBalanced(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastCompBest(ulong corr, int payload, RPCInfo info = default) => BroadcastCompBest(corr, payload);
    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)] private static void TriggerBroadcastUnreliable(ulong corr, int payload, RPCInfo info = default) => BroadcastUnreliable(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastDeltaOff(ulong corr, int payload, RPCInfo info = default) => BroadcastDeltaOff(corr, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerBroadcastDeltaOn(ulong corr, int payload, RPCInfo info = default) => BroadcastDeltaOn(corr, payload);

    // ----- ObserversRpc receivers -----

    [ObserversRpc]
    private static void BroadcastInt(ulong corr, int payload) => _intReceived[corr] = payload;

    [ObserversRpc]
    private static void BroadcastString(ulong corr, string payload) => _stringReceived[corr] = payload;

    [ObserversRpc]
    private static void BroadcastStruct(ulong corr, TestPayload payload) => _structReceived[corr] = payload;

    [ObserversRpc]
    private static void BroadcastGeneric<T>(ulong corr, T payload)
    {
        if (typeof(T) == typeof(int))
            _genericIntReceived[corr] = (int)(object)payload;
    }

    [ObserversRpc(compressionLevel: CompressionLevel.None)]
    private static void BroadcastCompNone(ulong corr, int payload) => _compNoneReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Fast)]
    private static void BroadcastCompFast(ulong corr, int payload) => _compFastReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Balanced)]
    private static void BroadcastCompBalanced(ulong corr, int payload) => _compBalancedReceived[corr] = payload;

    [ObserversRpc(compressionLevel: CompressionLevel.Best)]
    private static void BroadcastCompBest(ulong corr, int payload) => _compBestReceived[corr] = payload;

    [ObserversRpc(channel: Channel.Unreliable)]
    private static void BroadcastUnreliable(ulong corr, int payload) => _unreliableReceived[corr] = payload;

    [ObserversRpc(deltaPacked: false)]
    private static void BroadcastDeltaOff(ulong corr, int payload)
    {
        if (!_deltaSequence.TryGetValue(corr, out var list))
            _deltaSequence[corr] = list = new List<int>();
        list.Add(payload);
    }

    [ObserversRpc(deltaPacked: true)]
    private static void BroadcastDeltaOn(ulong corr, int payload)
    {
        if (!_deltaPackedSequence.TryGetValue(corr, out var list))
            _deltaPackedSequence[corr] = list = new List<int>();
        list.Add(payload);
    }

    [ObserversRpc(requireServer: false)]
    private static void BroadcastP2P(ulong corr, int payload) => _p2pBroadcastReceived[corr] = payload;

    [ServerRpc(requireOwnership: false)]
    private static void TriggerBroadcastAsyncPackable(ulong corr, int seed, RPCInfo info = default)
    {
        BroadcastAsyncPackable(corr, new AsyncPayload { seed = seed });
    }

    [ObserversRpc]
    private static void BroadcastAsyncPackable(ulong corr, AsyncPayload p)
    {
        _asyncObservedStamps[corr] = new[] { p.seed, p.packStamp, p.unpackStamp };
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _doneCount++;
    }
}

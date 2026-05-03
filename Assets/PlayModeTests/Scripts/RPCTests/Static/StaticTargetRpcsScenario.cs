using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;
using CompressionLevel = PurrNet.CompressionLevel;

public class StaticTargetRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _targetTimeoutSeconds = 10f;

    private static int _doneCount;
    private static float _staticTargetTimeoutSeconds = 10f;

    // The target is always the originating client itself, so per-process state is a single
    // value per test slot. Nullable so we can wait for "value arrived" instead of polling
    // a default. Every test resets its slot to null before triggering, then waits, then
    // explicitly verifies HasValue before reading .Value.
    private static int? _intReceived;
    private static string _stringReceived;
    private static TestPayload? _structReceived;
    private static int? _genericIntReceived;
    private static int? _compNoneReceived;
    private static int? _compFastReceived;
    private static int? _compBalancedReceived;
    private static int? _compBestReceived;
    private static int? _unreliableReceived;
    private static int? _multiArrayReceived;
    private static int? _multiListReceived;
    private static int? _multiEnumReceived;
    private static int? _asyncTargetReceived;
    private static int? _p2pTargetReceived;
    private static readonly List<int> _deltaSequence = new();
    private static readonly List<int> _deltaPackedSequence = new();

    [Serializable]
    public struct TestPayload
    {
        public int id;
        public string label;
        public float weight;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _staticTargetTimeoutSeconds = _targetTimeoutSeconds;

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
        var timeout = _staticTargetTimeoutSeconds;

        await Try(failures, "Int", async () =>
        {
            _intReceived = null;
            TriggerTargetInt(42);
            await UniTaskUtils.WaitWithTimeout(() => _intReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_intReceived.HasValue) throw new Exception("int payload did not arrive");
            if (_intReceived.Value != 42) throw new Exception($"expected 42, got {_intReceived.Value}");
        });

        await Try(failures, "String", async () =>
        {
            _stringReceived = null;
            TriggerTargetString("tgt hello");
            await UniTaskUtils.WaitWithTimeout(() => _stringReceived != null, timeout, ctx.cancellationToken);
            if (_stringReceived == null) throw new Exception("string payload did not arrive");
            if (_stringReceived != "tgt hello") throw new Exception($"expected 'tgt hello', got '{_stringReceived}'");
        });

        await Try(failures, "Struct", async () =>
        {
            _structReceived = null;
            var input = new TestPayload { id = 21, label = "tgt payload", weight = 3.5f };
            TriggerTargetStruct(input);
            await UniTaskUtils.WaitWithTimeout(() => _structReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_structReceived.HasValue)
                throw new Exception("struct payload did not arrive");
            var r = _structReceived.Value;
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "GenericInt", async () =>
        {
            _genericIntReceived = null;
            TriggerTargetGeneric<int>(91);
            await UniTaskUtils.WaitWithTimeout(() => _genericIntReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_genericIntReceived.HasValue) throw new Exception("generic int payload did not arrive");
            if (_genericIntReceived.Value != 91) throw new Exception($"expected 91, got {_genericIntReceived.Value}");
        });

        await Try(failures, "Compression_None", async () =>
        {
            _compNoneReceived = null;
            TriggerTargetCompNone(2001);
            await UniTaskUtils.WaitWithTimeout(() => _compNoneReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_compNoneReceived.HasValue) throw new Exception("compression(None) payload did not arrive");
            if (_compNoneReceived.Value != 2001) throw new Exception($"expected 2001, got {_compNoneReceived.Value}");
        });

        await Try(failures, "Compression_Fast", async () =>
        {
            _compFastReceived = null;
            TriggerTargetCompFast(2002);
            await UniTaskUtils.WaitWithTimeout(() => _compFastReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_compFastReceived.HasValue) throw new Exception("compression(Fast) payload did not arrive");
            if (_compFastReceived.Value != 2002) throw new Exception($"expected 2002, got {_compFastReceived.Value}");
        });

        await Try(failures, "Compression_Balanced", async () =>
        {
            _compBalancedReceived = null;
            TriggerTargetCompBalanced(2003);
            await UniTaskUtils.WaitWithTimeout(() => _compBalancedReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_compBalancedReceived.HasValue) throw new Exception("compression(Balanced) payload did not arrive");
            if (_compBalancedReceived.Value != 2003) throw new Exception($"expected 2003, got {_compBalancedReceived.Value}");
        });

        await Try(failures, "Compression_Best", async () =>
        {
            _compBestReceived = null;
            TriggerTargetCompBest(2004);
            await UniTaskUtils.WaitWithTimeout(() => _compBestReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_compBestReceived.HasValue) throw new Exception("compression(Best) payload did not arrive");
            if (_compBestReceived.Value != 2004) throw new Exception($"expected 2004, got {_compBestReceived.Value}");
        });

        await Try(failures, "Unreliable", async () =>
        {
            _unreliableReceived = null;
            for (var i = 0; i < 5; i++)
                TriggerTargetUnreliable(777);
            await UniTaskUtils.WaitWithTimeout(() => _unreliableReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_unreliableReceived.HasValue) throw new Exception("unreliable payload did not arrive");
            if (_unreliableReceived.Value != 777) throw new Exception($"expected 777, got {_unreliableReceived.Value}");
        });

        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            _deltaSequence.Clear();
            int[] seq = { 5, 5, 6, 6, 11 };
            for (var i = 0; i < seq.Length; i++)
                TriggerTargetDeltaOff(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => _deltaSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (_deltaSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {_deltaSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (_deltaSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {_deltaSequence[i]}");
        });

        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            _deltaPackedSequence.Clear();
            int[] seq = { 300, 300, 301, 301, 400 };
            for (var i = 0; i < seq.Length; i++)
                TriggerTargetDeltaOn(seq[i]);
            await UniTaskUtils.WaitWithTimeout(() => _deltaPackedSequence.Count >= seq.Length, timeout, ctx.cancellationToken);
            if (_deltaPackedSequence.Count < seq.Length)
                throw new Exception($"expected {seq.Length} values, got {_deltaPackedSequence.Count}");
            for (var i = 0; i < seq.Length; i++)
                if (_deltaPackedSequence[i] != seq[i])
                    throw new Exception($"seq[{i}] expected {seq[i]}, got {_deltaPackedSequence[i]}");
        });

        // Multi-target collection variants. The trigger ServerRpc builds a collection containing
        // only the originating client (info.sender) so each client can verify its own receipt
        // without cross-client coordination. Exercises the codegen paths for PlayerID[], IList,
        // and IEnumerable target argument types.
        await Try(failures, "MultiTarget_Array", async () =>
        {
            _multiArrayReceived = null;
            TriggerTargetArray(5101);
            await UniTaskUtils.WaitWithTimeout(() => _multiArrayReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_multiArrayReceived.HasValue) throw new Exception("array-target payload did not arrive");
            if (_multiArrayReceived.Value != 5101) throw new Exception($"expected 5101, got {_multiArrayReceived.Value}");
        });

        await Try(failures, "MultiTarget_List", async () =>
        {
            _multiListReceived = null;
            TriggerTargetList(5102);
            await UniTaskUtils.WaitWithTimeout(() => _multiListReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_multiListReceived.HasValue) throw new Exception("list-target payload did not arrive");
            if (_multiListReceived.Value != 5102) throw new Exception($"expected 5102, got {_multiListReceived.Value}");
        });

        await Try(failures, "MultiTarget_Enumerable", async () =>
        {
            _multiEnumReceived = null;
            TriggerTargetEnumerable(5103);
            await UniTaskUtils.WaitWithTimeout(() => _multiEnumReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_multiEnumReceived.HasValue) throw new Exception("enumerable-target payload did not arrive");
            if (_multiEnumReceived.Value != 5103) throw new Exception($"expected 5103, got {_multiEnumReceived.Value}");
        });

        // Async single-target. The trigger ServerRpc awaits the TargetRpc's Task<int> and
        // returns it back to the originating client, exercising the async TargetRpc reply path.
        await Try(failures, "AsyncTarget_Echo", async () =>
        {
            _asyncTargetReceived = null;
            var result = await TriggerAsyncTarget(99);
            if (!_asyncTargetReceived.HasValue) throw new Exception("async-target payload did not arrive at receiver");
            if (_asyncTargetReceived.Value != 99) throw new Exception($"receiver got {_asyncTargetReceived.Value}, expected 99");
            if (result != 100) throw new Exception($"async target reply expected 100, got {result}");
        });

        await Try(failures, "TargetServer_DefaultPlayerID_Rejected", async () =>
        {
            try
            {
                await SendTargetServer(default, 8888);
                throw new Exception("expected RpcRejectedException, none thrown");
            }
            catch (RpcRejectedException ex)
            {
                if (ex.error != RpcError.TargetServerNotAllowed)
                    throw new Exception($"expected TargetServerNotAllowed, got {ex.error}");
            }
        });

        // requireServer:false TargetRpc — client originates directly without going through
        // a Trigger ServerRpc. The client batches to the server, the server forwards to
        // targetPlayerId. Targeting localPlayer keeps verification deterministic per-process
        // while still exercising the full client → server → client routing.
        await Try(failures, "TargetP2P_RequireServerFalse", async () =>
        {
            _p2pTargetReceived = null;
            SendTargetP2P(ctx.networkManager.localPlayer, 7777);
            await UniTaskUtils.WaitWithTimeout(() => _p2pTargetReceived.HasValue, timeout, ctx.cancellationToken);
            if (!_p2pTargetReceived.HasValue) throw new Exception("p2p target payload did not arrive");
            if (_p2pTargetReceived.Value != 7777) throw new Exception($"expected 7777, got {_p2pTargetReceived.Value}");
        });

        // Fan out an async TargetRpc to every visible non-server player and verify each
        // reply carries that player's own localPlayer id. Confirms we can reach every
        // connected peer round-trip.
        await Try(failures, "PingAllPeers", async () =>
        {
            var snapshot = new List<PlayerID>(ctx.networkManager.players);
            var pinged = 0;
            foreach (var p in snapshot)
            {
                if (p.isServer) continue;
                var reply = await PingPlayer(p);
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
            Debug.LogError($"[StaticTargetRpcsScenario] {label} failed: {e}");
        }
    }

    // ----- Triggers (client → server). Server receives RPCInfo so it knows whom to TargetRpc back. -----

    [ServerRpc(requireOwnership: false)] private static void TriggerTargetInt(int payload, RPCInfo info = default) => SendTargetInt(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetString(string payload, RPCInfo info = default) => SendTargetString(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetStruct(TestPayload payload, RPCInfo info = default) => SendTargetStruct(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetGeneric<T>(T payload, RPCInfo info = default) => SendTargetGeneric(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetCompNone(int payload, RPCInfo info = default) => SendTargetCompNone(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetCompFast(int payload, RPCInfo info = default) => SendTargetCompFast(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetCompBalanced(int payload, RPCInfo info = default) => SendTargetCompBalanced(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetCompBest(int payload, RPCInfo info = default) => SendTargetCompBest(info.sender, payload);
    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)] private static void TriggerTargetUnreliable(int payload, RPCInfo info = default) => SendTargetUnreliable(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetDeltaOff(int payload, RPCInfo info = default) => SendTargetDeltaOff(info.sender, payload);
    [ServerRpc(requireOwnership: false)] private static void TriggerTargetDeltaOn(int payload, RPCInfo info = default) => SendTargetDeltaOn(info.sender, payload);

    [ServerRpc(requireOwnership: false)]
    private static void TriggerTargetArray(int payload, RPCInfo info = default)
    {
        var targets = new[] { info.sender };
        SendTargetArray(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    private static void TriggerTargetList(int payload, RPCInfo info = default)
    {
        var targets = new List<PlayerID> { info.sender };
        SendTargetList(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    private static void TriggerTargetEnumerable(int payload, RPCInfo info = default)
    {
        var targets = new HashSet<PlayerID> { info.sender };
        SendTargetEnumerable(targets, payload);
    }

    [ServerRpc(requireOwnership: false)]
    private static async Task<int> TriggerAsyncTarget(int payload, RPCInfo info = default)
    {
        return await SendTargetEchoAsync(info.sender, payload);
    }

    // ----- TargetRpc receivers. First parameter is always the target PlayerID (or collection). -----

    [TargetRpc] private static void SendTargetInt(PlayerID target, int payload) => _intReceived = payload;
    [TargetRpc] private static void SendTargetString(PlayerID target, string payload) => _stringReceived = payload;
    [TargetRpc] private static void SendTargetStruct(PlayerID target, TestPayload payload) => _structReceived = payload;

    [TargetRpc]
    private static void SendTargetGeneric<T>(PlayerID target, T payload)
    {
        if (typeof(T) == typeof(int))
            _genericIntReceived = (int)(object)payload;
    }

    [TargetRpc(compressionLevel: CompressionLevel.None)] private static void SendTargetCompNone(PlayerID target, int payload) => _compNoneReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Fast)] private static void SendTargetCompFast(PlayerID target, int payload) => _compFastReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Balanced)] private static void SendTargetCompBalanced(PlayerID target, int payload) => _compBalancedReceived = payload;
    [TargetRpc(compressionLevel: CompressionLevel.Best)] private static void SendTargetCompBest(PlayerID target, int payload) => _compBestReceived = payload;
    [TargetRpc(channel: Channel.Unreliable)] private static void SendTargetUnreliable(PlayerID target, int payload) => _unreliableReceived = payload;

    [TargetRpc(deltaPacked: false)]
    private static void SendTargetDeltaOff(PlayerID target, int payload) => _deltaSequence.Add(payload);

    [TargetRpc(deltaPacked: true)]
    private static void SendTargetDeltaOn(PlayerID target, int payload) => _deltaPackedSequence.Add(payload);

    [TargetRpc] private static void SendTargetArray(PlayerID[] targets, int payload) => _multiArrayReceived = payload;
    [TargetRpc] private static void SendTargetList(IList<PlayerID> targets, int payload) => _multiListReceived = payload;
    [TargetRpc] private static void SendTargetEnumerable(ICollection<PlayerID> targets, int payload) => _multiEnumReceived = payload;

    [TargetRpc]
    private static Task<int> SendTargetEchoAsync(PlayerID target, int payload)
    {
        _asyncTargetReceived = payload;
        return Task.FromResult(payload + 1);
    }

    [TargetRpc(requireServer: false)]
    private static Task SendTargetServer(PlayerID target, int payload)
    {
        Debug.Log($"[StaticTargetRpcsScenario] SendTargetServer received: {payload}");
        return Task.CompletedTask;
    }

    [TargetRpc(requireServer: false)]
    private static void SendTargetP2P(PlayerID target, int payload) => _p2pTargetReceived = payload;

    [TargetRpc(requireServer: false)]
    private static Task<ulong> PingPlayer(PlayerID target)
    {
        return Task.FromResult(NetworkManager.main.localPlayer.id.value);
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone()
    {
        _doneCount++;
        Debug.Log($"[StaticTargetRpcsScenario] SignalDone received (total {_doneCount})");
    }
}

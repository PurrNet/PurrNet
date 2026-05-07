using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;
using CompressionLevel = PurrNet.CompressionLevel;

public class StaticTargetBufferedRpcsScenario : Scenario
{
    [SerializeField] private float _kickoffTimeoutSeconds = 30f;
    [SerializeField] private float _phaseACompleteTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 60f;
    [SerializeField] private float _disconnectTimeoutSeconds = 15f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _replayTimeoutSeconds = 30f;

    private const int FinalInt = 99;
    private const string FinalString = "buffered-final";
    private const int FinalGenericInt = 77;
    private const int FinalDeltaOff = 11;
    private const int FinalCompNone = 3001;
    private const int FinalCompFast = 3002;
    private const int FinalCompBalanced = 3003;
    private const int FinalCompBest = 3004;
    private const int FinalAsyncSeed = 42;
    private const int FinalIEnumerator = 555;
    private const int FinalStructId = 99;
    private const string FinalStructLabel = "buffered";
    private const float FinalStructWeight = 9.99f;

    private static int _serverDoneCount;
    private static readonly List<PlayerID> _kickoffPlayers = new();
    private static int _phaseACompleteCount;

    private static int? _lastIntReceived;
    private static int _intReceiveCount;
    private static string _lastStringReceived;
    private static int _stringReceiveCount;
    private static TestPayload? _lastStructReceived;
    private static int _structReceiveCount;
    private static int? _lastGenericIntReceived;
    private static int _genericIntReceiveCount;
    private static int? _lastDeltaOffReceived;
    private static int _deltaOffReceiveCount;
    private static int? _lastCompNoneReceived;
    private static int _compNoneReceiveCount;
    private static int? _lastCompFastReceived;
    private static int _compFastReceiveCount;
    private static int? _lastCompBalancedReceived;
    private static int _compBalancedReceiveCount;
    private static int? _lastCompBestReceived;
    private static int _compBestReceiveCount;
    private static int? _lastAsyncSeedReceived;
    private static int? _lastAsyncPackStampReceived;
    private static int? _lastAsyncUnpackStampReceived;
    private static int _asyncReceiveCount;
    private static int? _lastIEnumeratorReceived;
    private static int _iEnumeratorReceiveCount;

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

    private static void ResetClientState()
    {
        _lastIntReceived = null; _intReceiveCount = 0;
        _lastStringReceived = null; _stringReceiveCount = 0;
        _lastStructReceived = null; _structReceiveCount = 0;
        _lastGenericIntReceived = null; _genericIntReceiveCount = 0;
        _lastDeltaOffReceived = null; _deltaOffReceiveCount = 0;
        _lastCompNoneReceived = null; _compNoneReceiveCount = 0;
        _lastCompFastReceived = null; _compFastReceiveCount = 0;
        _lastCompBalancedReceived = null; _compBalancedReceiveCount = 0;
        _lastCompBestReceived = null; _compBestReceiveCount = 0;
        _lastAsyncSeedReceived = null; _lastAsyncPackStampReceived = null; _lastAsyncUnpackStampReceived = null;
        _asyncReceiveCount = 0;
        _lastIEnumeratorReceived = null; _iEnumeratorReceiveCount = 0;
        _phaseACompleteCount = 0;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.role == NetworkRole.Server)
            return await RunAsServerOnly(ctx);

        if (ctx.isServer)
            return await RunAsHost(ctx);

        return await RunAsClient(ctx);
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _kickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Server timed out waiting for client kickoff; got {_kickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(_kickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Server timed out waiting for client done; got {_serverDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok($"Done={_serverDoneCount}");
    }

    private async UniTask<ScenarioResult> RunAsHost(ScenarioContext ctx)
    {
        var failures = new List<string>();

        Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _kickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Host timed out on server-side kickoff count; got {_kickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(_kickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phaseACompleteCount > 0 && AllVariantsReceived(),
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,$"Host did not receive PhaseAComplete locally; {ReceiveCountSummary()}");
        }

        VerifyPhaseAValues(failures);

        SignalDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _serverDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Host timed out on server-side done count; got {_serverDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phaseACompleteCount > 0 && AllVariantsReceived(),
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail($"Client did not receive PhaseAComplete; {ReceiveCountSummary()}");
        }

        VerifyPhaseAValues(failures);

        ResetClientState();

        ctx.networkManager.StopClient();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected,
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,$"Client did not reach Disconnected state after StopClient (state={ctx.networkManager.clientState})");
        }

        await UniTask.WaitForSeconds(0.5f, cancellationToken: ctx.cancellationToken);

        ctx.networkManager.StartClient();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.isClient,
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,"Client did not reconnect after StartClient");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(AllVariantsReceived, _replayTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,$"Buffered static TargetRpc replay incomplete after reconnect: {ReceiveCountSummary()}. " +
                         "Note: TargetRpc bufferLast replay requires the client's PlayerID to be preserved across reconnect (cookie scope LiveWithProcess or StorePersistently).");
        }

        VerifyPhaseDValues(failures);
        VerifyBufferLastKeptOnlyOne(failures);

        SignalDone();

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static void FireBufferedSequence(List<PlayerID> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];

            SendTargetBufferedInt(t, 1);
            SendTargetBufferedInt(t, 2);
            SendTargetBufferedInt(t, FinalInt);

            SendTargetBufferedString(t, "stale-1");
            SendTargetBufferedString(t, "stale-2");
            SendTargetBufferedString(t, FinalString);

            SendTargetBufferedStruct(t, new TestPayload { id = 1, label = "stale-1", weight = 1f });
            SendTargetBufferedStruct(t, new TestPayload { id = 2, label = "stale-2", weight = 2f });
            SendTargetBufferedStruct(t, new TestPayload { id = FinalStructId, label = FinalStructLabel, weight = FinalStructWeight });

            SendTargetBufferedGeneric<int>(t, 11);
            SendTargetBufferedGeneric<int>(t, 22);
            SendTargetBufferedGeneric<int>(t, FinalGenericInt);

            SendTargetBufferedDeltaOff(t, 1);
            SendTargetBufferedDeltaOff(t, 7);
            SendTargetBufferedDeltaOff(t, FinalDeltaOff);

            SendTargetBufferedCompNone(t, 1);
            SendTargetBufferedCompNone(t, 2);
            SendTargetBufferedCompNone(t, FinalCompNone);

            SendTargetBufferedCompFast(t, 1);
            SendTargetBufferedCompFast(t, 2);
            SendTargetBufferedCompFast(t, FinalCompFast);

            SendTargetBufferedCompBalanced(t, 1);
            SendTargetBufferedCompBalanced(t, 2);
            SendTargetBufferedCompBalanced(t, FinalCompBalanced);

            SendTargetBufferedCompBest(t, 1);
            SendTargetBufferedCompBest(t, 2);
            SendTargetBufferedCompBest(t, FinalCompBest);

            SendTargetBufferedAsyncPackable(t, new AsyncPayload { seed = 1 });
            SendTargetBufferedAsyncPackable(t, new AsyncPayload { seed = 2 });
            SendTargetBufferedAsyncPackable(t, new AsyncPayload { seed = FinalAsyncSeed });

            SendTargetBufferedIEnumerator(t, 11);
            SendTargetBufferedIEnumerator(t, 22);
            SendTargetBufferedIEnumerator(t, FinalIEnumerator);
        }

        NotifyPhaseAComplete();
    }

    private static bool AllVariantsReceived()
    {
        return _intReceiveCount > 0
            && _stringReceiveCount > 0
            && _structReceiveCount > 0
            && _genericIntReceiveCount > 0
            && _deltaOffReceiveCount > 0
            && _compNoneReceiveCount > 0
            && _compFastReceiveCount > 0
            && _compBalancedReceiveCount > 0
            && _compBestReceiveCount > 0
            && _asyncReceiveCount > 0
            && _iEnumeratorReceiveCount > 0;
    }

    private static string ReceiveCountSummary()
    {
        return $"int={_intReceiveCount}, string={_stringReceiveCount}, struct={_structReceiveCount}, " +
               $"generic={_genericIntReceiveCount}, deltaOff={_deltaOffReceiveCount}, " +
               $"compNone={_compNoneReceiveCount}, compFast={_compFastReceiveCount}, " +
               $"compBalanced={_compBalancedReceiveCount}, compBest={_compBestReceiveCount}, " +
               $"async={_asyncReceiveCount}, ienum={_iEnumeratorReceiveCount}";
    }

    private static void VerifyPhaseAValues(List<string> failures) => VerifyValues(failures, "PhaseA");
    private static void VerifyPhaseDValues(List<string> failures) => VerifyValues(failures, "PhaseD");

    private static void VerifyValues(List<string> failures, string phase)
    {
        if (_lastIntReceived != FinalInt)
            LogFail(failures,$"{phase}.Int: expected {FinalInt}, got {_lastIntReceived}");

        if (_lastStringReceived != FinalString)
            LogFail(failures,$"{phase}.String: expected '{FinalString}', got '{_lastStringReceived}'");

        var s = _lastStructReceived;
        if (!s.HasValue || s.Value.id != FinalStructId || s.Value.label != FinalStructLabel || Mathf.Abs(s.Value.weight - FinalStructWeight) > 0.0001f)
            LogFail(failures,$"{phase}.Struct: expected (id={FinalStructId}, label='{FinalStructLabel}'), got {(s.HasValue ? $"(id={s.Value.id}, label='{s.Value.label}')" : "<null>")}");

        if (_lastGenericIntReceived != FinalGenericInt)
            LogFail(failures,$"{phase}.Generic: expected {FinalGenericInt}, got {_lastGenericIntReceived}");

        if (_lastDeltaOffReceived != FinalDeltaOff)
            LogFail(failures,$"{phase}.DeltaOff: expected {FinalDeltaOff}, got {_lastDeltaOffReceived}");

        if (_lastCompNoneReceived != FinalCompNone)
            LogFail(failures,$"{phase}.CompNone: expected {FinalCompNone}, got {_lastCompNoneReceived}");

        if (_lastCompFastReceived != FinalCompFast)
            LogFail(failures,$"{phase}.CompFast: expected {FinalCompFast}, got {_lastCompFastReceived}");

        if (_lastCompBalancedReceived != FinalCompBalanced)
            LogFail(failures,$"{phase}.CompBalanced: expected {FinalCompBalanced}, got {_lastCompBalancedReceived}");

        if (_lastCompBestReceived != FinalCompBest)
            LogFail(failures,$"{phase}.CompBest: expected {FinalCompBest}, got {_lastCompBestReceived}");

        if (_lastAsyncSeedReceived != FinalAsyncSeed)
            LogFail(failures,$"{phase}.AsyncSeed: expected {FinalAsyncSeed}, got {_lastAsyncSeedReceived}");

        if (_lastAsyncPackStampReceived != FinalAsyncSeed + 1)
            LogFail(failures,$"{phase}.AsyncPack: PrepareForPackAsync did not run on sender — expected {FinalAsyncSeed + 1}, got {_lastAsyncPackStampReceived}");

        if (_lastAsyncUnpackStampReceived != FinalAsyncSeed + 11)
            LogFail(failures,$"{phase}.AsyncUnpack: PrepareAfterUnpackAsync did not run on receiver — expected {FinalAsyncSeed + 11}, got {_lastAsyncUnpackStampReceived}");

        if (_lastIEnumeratorReceived != FinalIEnumerator)
            LogFail(failures,$"{phase}.IEnumerator: expected {FinalIEnumerator}, got {_lastIEnumeratorReceived}");
    }

    private static void VerifyBufferLastKeptOnlyOne(List<string> failures)
    {
        AssertOne(failures, "Int", _intReceiveCount);
        AssertOne(failures, "String", _stringReceiveCount);
        AssertOne(failures, "Struct", _structReceiveCount);
        AssertOne(failures, "Generic", _genericIntReceiveCount);
        AssertOne(failures, "DeltaOff", _deltaOffReceiveCount);
        AssertOne(failures, "CompNone", _compNoneReceiveCount);
        AssertOne(failures, "CompFast", _compFastReceiveCount);
        AssertOne(failures, "CompBalanced", _compBalancedReceiveCount);
        AssertOne(failures, "CompBest", _compBestReceiveCount);
        AssertOne(failures, "Async", _asyncReceiveCount);
        AssertOne(failures, "IEnumerator", _iEnumeratorReceiveCount);
    }

    private static void AssertOne(List<string> failures, string label, int count)
    {
        if (count != 1)
            LogFail(failures,$"BufferLast.{label}: expected exactly 1 replay after reconnect, got {count}");
    }

    private static void LogFail(List<string> failures, string msg)
    {
        Debug.LogError($"[StaticTargetBufferedRpcsScenario] {msg}");
        failures.Add(msg);
    }

    private static ScenarioResult LogAndFail(string msg)
    {
        Debug.LogError($"[StaticTargetBufferedRpcsScenario] {msg}");
        return ScenarioResult.Fail(msg);
    }

    [ServerRpc(requireOwnership: false)]
    private static void Kickoff(RPCInfo info = default)
    {
        if (!_kickoffPlayers.Contains(info.sender))
            _kickoffPlayers.Add(info.sender);
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _serverDoneCount++;
    }

    [ObserversRpc]
    private static void NotifyPhaseAComplete() => _phaseACompleteCount++;

    [TargetRpc(bufferLast: true)]
    private static void SendTargetBufferedInt(PlayerID target, int payload) { _lastIntReceived = payload; _intReceiveCount++; }

    [TargetRpc(bufferLast: true)]
    private static void SendTargetBufferedString(PlayerID target, string payload) { _lastStringReceived = payload; _stringReceiveCount++; }

    [TargetRpc(bufferLast: true)]
    private static void SendTargetBufferedStruct(PlayerID target, TestPayload payload) { _lastStructReceived = payload; _structReceiveCount++; }

    [TargetRpc(bufferLast: true)]
    private static void SendTargetBufferedGeneric<T>(PlayerID target, T payload)
    {
        if (typeof(T) == typeof(int))
        {
            _lastGenericIntReceived = (int)(object)payload;
            _genericIntReceiveCount++;
        }
    }

    [TargetRpc(bufferLast: true, deltaPacked: false)]
    private static void SendTargetBufferedDeltaOff(PlayerID target, int payload) { _lastDeltaOffReceived = payload; _deltaOffReceiveCount++; }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.None)]
    private static void SendTargetBufferedCompNone(PlayerID target, int payload) { _lastCompNoneReceived = payload; _compNoneReceiveCount++; }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Fast)]
    private static void SendTargetBufferedCompFast(PlayerID target, int payload) { _lastCompFastReceived = payload; _compFastReceiveCount++; }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Balanced)]
    private static void SendTargetBufferedCompBalanced(PlayerID target, int payload) { _lastCompBalancedReceived = payload; _compBalancedReceiveCount++; }

    [TargetRpc(bufferLast: true, compressionLevel: CompressionLevel.Best)]
    private static void SendTargetBufferedCompBest(PlayerID target, int payload) { _lastCompBestReceived = payload; _compBestReceiveCount++; }

    [TargetRpc(bufferLast: true)]
    private static void SendTargetBufferedAsyncPackable(PlayerID target, AsyncPayload p)
    {
        _lastAsyncSeedReceived = p.seed;
        _lastAsyncPackStampReceived = p.packStamp;
        _lastAsyncUnpackStampReceived = p.unpackStamp;
        _asyncReceiveCount++;
    }

    [TargetRpc(bufferLast: true)]
    private static IEnumerator SendTargetBufferedIEnumerator(PlayerID target, int payload)
    {
        yield return null;
        yield return null;
        _lastIEnumeratorReceived = payload;
        _iEnumeratorReceiveCount++;
    }
}

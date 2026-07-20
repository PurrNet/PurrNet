using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class IdentityObserversBufferedRpcsScenario : Scenario
{
    [SerializeField] private float _kickoffTimeoutSeconds = 30f;
    [SerializeField] private float _phaseACompleteTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 60f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
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
    private const int FinalStructId = 99;
    private const string FinalStructLabel = "buffered";
    private const float FinalStructWeight = 9.99f;

    private IdentityObserversBufferedRpcs _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(nameof(IdentityObserversBufferedRpcsScenario));
        _prefab = go.AddComponent<IdentityObserversBufferedRpcs>();
        go.SetActive(false);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
            Instantiate(_prefab);

        if (ctx.role == NetworkRole.Server)
            return await RunAsServerOnly(ctx);

        await UniTaskUtils.WaitWithTimeout(
            () => IdentityObserversBufferedRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

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
                () => IdentityObserversBufferedRpcs.ServerKickoffCount >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Server timed out waiting for client kickoff; got {IdentityObserversBufferedRpcs.ServerKickoffCount}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(IdentityObserversBufferedRpcs.LocalInstance);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Server timed out waiting for client done; got {IdentityObserversBufferedRpcs.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok($"Done={IdentityObserversBufferedRpcs.ServerDoneCount}");
    }

    private async UniTask<ScenarioResult> RunAsHost(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = IdentityObserversBufferedRpcs.LocalInstance;

        inst.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.ServerKickoffCount >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Host timed out on server-side kickoff count; got {IdentityObserversBufferedRpcs.ServerKickoffCount}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(inst);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.PhaseACompleteCount > 0 && AllVariantsReceived(),
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,"Host did not receive PhaseAComplete locally");
        }

        VerifyPhaseAValues(failures);

        inst.SignalDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Host timed out on server-side done count; got {IdentityObserversBufferedRpcs.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var inst = IdentityObserversBufferedRpcs.LocalInstance;
        inst.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.PhaseACompleteCount > 0 && AllVariantsReceived(),
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail("Client did not receive PhaseAComplete; server may not have fired the buffered set");
        }

        VerifyPhaseAValues(failures);

        IdentityObserversBufferedRpcs.ResetClientState();
        IdentityObserversBufferedRpcs.LocalInstance = null;

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
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityObserversBufferedRpcs.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,"Identity did not respawn on client after reconnect");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(AllVariantsReceived, _replayTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,$"Buffered replay incomplete after reconnect: {ReceiveCountSummary()}");
        }

        VerifyPhaseDValues(failures);
        VerifyBufferLastKeptOnlyOne(failures);

        if (IdentityObserversBufferedRpcs.LocalInstance != null)
            IdentityObserversBufferedRpcs.LocalInstance.SignalDone();
        else
            LogFail(failures,"Cannot send SignalDone — LocalInstance is null after reconnect");

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static void FireBufferedSequence(IdentityObserversBufferedRpcs inst)
    {
        inst.BroadcastBufferedInt(1);
        inst.BroadcastBufferedInt(2);
        inst.BroadcastBufferedInt(FinalInt);

        inst.BroadcastBufferedString("stale-1");
        inst.BroadcastBufferedString("stale-2");
        inst.BroadcastBufferedString(FinalString);

        inst.BroadcastBufferedStruct(new IdentityObserversBufferedRpcs.TestPayload { id = 1, label = "stale-1", weight = 1f });
        inst.BroadcastBufferedStruct(new IdentityObserversBufferedRpcs.TestPayload { id = 2, label = "stale-2", weight = 2f });
        inst.BroadcastBufferedStruct(new IdentityObserversBufferedRpcs.TestPayload { id = FinalStructId, label = FinalStructLabel, weight = FinalStructWeight });

        inst.BroadcastBufferedGeneric<int>(11);
        inst.BroadcastBufferedGeneric<int>(22);
        inst.BroadcastBufferedGeneric<int>(FinalGenericInt);

        inst.BroadcastBufferedDeltaOff(1);
        inst.BroadcastBufferedDeltaOff(7);
        inst.BroadcastBufferedDeltaOff(FinalDeltaOff);

        inst.BroadcastBufferedCompNone(1);
        inst.BroadcastBufferedCompNone(2);
        inst.BroadcastBufferedCompNone(FinalCompNone);

        inst.BroadcastBufferedCompFast(1);
        inst.BroadcastBufferedCompFast(2);
        inst.BroadcastBufferedCompFast(FinalCompFast);

        inst.BroadcastBufferedCompBalanced(1);
        inst.BroadcastBufferedCompBalanced(2);
        inst.BroadcastBufferedCompBalanced(FinalCompBalanced);

        inst.BroadcastBufferedCompBest(1);
        inst.BroadcastBufferedCompBest(2);
        inst.BroadcastBufferedCompBest(FinalCompBest);

        inst.BroadcastBufferedAsyncPackable(new IdentityObserversBufferedRpcs.AsyncPayload { seed = 1 });
        inst.BroadcastBufferedAsyncPackable(new IdentityObserversBufferedRpcs.AsyncPayload { seed = 2 });
        inst.BroadcastBufferedAsyncPackable(new IdentityObserversBufferedRpcs.AsyncPayload { seed = FinalAsyncSeed });

        inst.NotifyPhaseAComplete();
    }

    private static bool AllVariantsReceived()
    {
        return IdentityObserversBufferedRpcs.IntReceiveCount > 0
            && IdentityObserversBufferedRpcs.StringReceiveCount > 0
            && IdentityObserversBufferedRpcs.StructReceiveCount > 0
            && IdentityObserversBufferedRpcs.GenericIntReceiveCount > 0
            && IdentityObserversBufferedRpcs.DeltaOffReceiveCount > 0
            && IdentityObserversBufferedRpcs.CompNoneReceiveCount > 0
            && IdentityObserversBufferedRpcs.CompFastReceiveCount > 0
            && IdentityObserversBufferedRpcs.CompBalancedReceiveCount > 0
            && IdentityObserversBufferedRpcs.CompBestReceiveCount > 0
            && IdentityObserversBufferedRpcs.AsyncReceiveCount > 0;
    }

    private static string ReceiveCountSummary()
    {
        return $"int={IdentityObserversBufferedRpcs.IntReceiveCount}, " +
               $"string={IdentityObserversBufferedRpcs.StringReceiveCount}, " +
               $"struct={IdentityObserversBufferedRpcs.StructReceiveCount}, " +
               $"generic={IdentityObserversBufferedRpcs.GenericIntReceiveCount}, " +
               $"deltaOff={IdentityObserversBufferedRpcs.DeltaOffReceiveCount}, " +
               $"compNone={IdentityObserversBufferedRpcs.CompNoneReceiveCount}, " +
               $"compFast={IdentityObserversBufferedRpcs.CompFastReceiveCount}, " +
               $"compBalanced={IdentityObserversBufferedRpcs.CompBalancedReceiveCount}, " +
               $"compBest={IdentityObserversBufferedRpcs.CompBestReceiveCount}, " +
               $"async={IdentityObserversBufferedRpcs.AsyncReceiveCount}";
    }

    private static void VerifyPhaseAValues(List<string> failures)
    {
        VerifyValues(failures, "PhaseA");
    }

    private static void VerifyPhaseDValues(List<string> failures)
    {
        VerifyValues(failures, "PhaseD");
    }

    private static void VerifyValues(List<string> failures, string phase)
    {
        if (IdentityObserversBufferedRpcs.LastIntReceived != FinalInt)
            LogFail(failures,$"{phase}.Int: expected {FinalInt}, got {IdentityObserversBufferedRpcs.LastIntReceived}");

        if (IdentityObserversBufferedRpcs.LastStringReceived != FinalString)
            LogFail(failures,$"{phase}.String: expected '{FinalString}', got '{IdentityObserversBufferedRpcs.LastStringReceived}'");

        var s = IdentityObserversBufferedRpcs.LastStructReceived;
        if (!s.HasValue || s.Value.id != FinalStructId || s.Value.label != FinalStructLabel || Mathf.Abs(s.Value.weight - FinalStructWeight) > 0.0001f)
            LogFail(failures,$"{phase}.Struct: expected (id={FinalStructId}, label='{FinalStructLabel}'), got {(s.HasValue ? $"(id={s.Value.id}, label='{s.Value.label}')" : "<null>")}");

        if (IdentityObserversBufferedRpcs.LastGenericIntReceived != FinalGenericInt)
            LogFail(failures,$"{phase}.Generic: expected {FinalGenericInt}, got {IdentityObserversBufferedRpcs.LastGenericIntReceived}");

        if (IdentityObserversBufferedRpcs.LastDeltaOffReceived != FinalDeltaOff)
            LogFail(failures,$"{phase}.DeltaOff: expected {FinalDeltaOff}, got {IdentityObserversBufferedRpcs.LastDeltaOffReceived}");

        if (IdentityObserversBufferedRpcs.LastCompNoneReceived != FinalCompNone)
            LogFail(failures,$"{phase}.CompNone: expected {FinalCompNone}, got {IdentityObserversBufferedRpcs.LastCompNoneReceived}");

        if (IdentityObserversBufferedRpcs.LastCompFastReceived != FinalCompFast)
            LogFail(failures,$"{phase}.CompFast: expected {FinalCompFast}, got {IdentityObserversBufferedRpcs.LastCompFastReceived}");

        if (IdentityObserversBufferedRpcs.LastCompBalancedReceived != FinalCompBalanced)
            LogFail(failures,$"{phase}.CompBalanced: expected {FinalCompBalanced}, got {IdentityObserversBufferedRpcs.LastCompBalancedReceived}");

        if (IdentityObserversBufferedRpcs.LastCompBestReceived != FinalCompBest)
            LogFail(failures,$"{phase}.CompBest: expected {FinalCompBest}, got {IdentityObserversBufferedRpcs.LastCompBestReceived}");

        if (IdentityObserversBufferedRpcs.LastAsyncSeedReceived != FinalAsyncSeed)
            LogFail(failures,$"{phase}.AsyncSeed: expected {FinalAsyncSeed}, got {IdentityObserversBufferedRpcs.LastAsyncSeedReceived}");

        if (IdentityObserversBufferedRpcs.LastAsyncPackStampReceived != FinalAsyncSeed + 1)
            LogFail(failures,$"{phase}.AsyncPack: PrepareForPackAsync did not run on sender — expected {FinalAsyncSeed + 1}, got {IdentityObserversBufferedRpcs.LastAsyncPackStampReceived}");

        if (IdentityObserversBufferedRpcs.LastAsyncUnpackStampReceived != FinalAsyncSeed + 11)
            LogFail(failures,$"{phase}.AsyncUnpack: PrepareAfterUnpackAsync did not run on receiver — expected {FinalAsyncSeed + 11}, got {IdentityObserversBufferedRpcs.LastAsyncUnpackStampReceived}");
    }

    private static void VerifyBufferLastKeptOnlyOne(List<string> failures)
    {
        AssertOne(failures, "Int", IdentityObserversBufferedRpcs.IntReceiveCount);
        AssertOne(failures, "String", IdentityObserversBufferedRpcs.StringReceiveCount);
        AssertOne(failures, "Struct", IdentityObserversBufferedRpcs.StructReceiveCount);
        AssertOne(failures, "Generic", IdentityObserversBufferedRpcs.GenericIntReceiveCount);
        AssertOne(failures, "DeltaOff", IdentityObserversBufferedRpcs.DeltaOffReceiveCount);
        AssertOne(failures, "CompNone", IdentityObserversBufferedRpcs.CompNoneReceiveCount);
        AssertOne(failures, "CompFast", IdentityObserversBufferedRpcs.CompFastReceiveCount);
        AssertOne(failures, "CompBalanced", IdentityObserversBufferedRpcs.CompBalancedReceiveCount);
        AssertOne(failures, "CompBest", IdentityObserversBufferedRpcs.CompBestReceiveCount);
        AssertOne(failures, "Async", IdentityObserversBufferedRpcs.AsyncReceiveCount);
    }

    private static void AssertOne(List<string> failures, string label, int count)
    {
        if (count != 1)
            LogFail(failures, $"BufferLast.{label}: expected exactly 1 replay after reconnect, got {count}");
    }

    private static void LogFail(List<string> failures, string msg)
    {
        Debug.LogError($"[IdentityObserversBufferedRpcsScenario] {msg}");
        failures.Add(msg);
    }

    private static ScenarioResult LogAndFail(string msg)
    {
        Debug.LogError($"[IdentityObserversBufferedRpcsScenario] {msg}");
        return ScenarioResult.Fail(msg);
    }
}

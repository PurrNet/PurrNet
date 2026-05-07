using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class ModuleObserversBufferedRpcsScenario : Scenario
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

    private ModuleObserversBufferedRpcs _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(nameof(ModuleObserversBufferedRpcsScenario));
        _prefab = go.AddComponent<ModuleObserversBufferedRpcs>();
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
            () => ModuleObserversBufferedRpcs.LocalInstance != null,
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
                () => ModuleObserversBufferedRpcsModule.ServerKickoffCount >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Server timed out waiting for client kickoff; got {ModuleObserversBufferedRpcsModule.ServerKickoffCount}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(ModuleObserversBufferedRpcs.LocalInstance.module);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleObserversBufferedRpcsModule.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Server timed out waiting for client done; got {ModuleObserversBufferedRpcsModule.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok($"Done={ModuleObserversBufferedRpcsModule.ServerDoneCount}");
    }

    private async UniTask<ScenarioResult> RunAsHost(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var module = ModuleObserversBufferedRpcs.LocalInstance.module;

        module.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleObserversBufferedRpcsModule.ServerKickoffCount >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Host timed out on server-side kickoff count; got {ModuleObserversBufferedRpcsModule.ServerKickoffCount}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(module);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleObserversBufferedRpcsModule.PhaseACompleteCount > 0,
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,"Host did not receive PhaseAComplete locally");
        }

        VerifyPhaseAValues(failures);

        module.SignalDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleObserversBufferedRpcsModule.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Host timed out on server-side done count; got {ModuleObserversBufferedRpcsModule.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var module = ModuleObserversBufferedRpcs.LocalInstance.module;
        module.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleObserversBufferedRpcsModule.PhaseACompleteCount > 0,
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail("Client did not receive PhaseAComplete; server may not have fired the buffered set");
        }

        VerifyPhaseAValues(failures);

        ModuleObserversBufferedRpcsModule.ResetClientState();
        ModuleObserversBufferedRpcs.LocalInstance = null;

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
                () => ModuleObserversBufferedRpcs.LocalInstance != null,
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
            LogFail(failures,$"Buffered child-RPC replay incomplete after reconnect: {ReceiveCountSummary()}");
        }

        VerifyPhaseDValues(failures);
        VerifyBufferLastKeptOnlyOne(failures);

        if (ModuleObserversBufferedRpcs.LocalInstance != null)
            ModuleObserversBufferedRpcs.LocalInstance.module.SignalDone();
        else
            LogFail(failures,"Cannot send SignalDone — LocalInstance is null after reconnect");

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static void FireBufferedSequence(ModuleObserversBufferedRpcsModule m)
    {
        m.BroadcastBufferedInt(1);
        m.BroadcastBufferedInt(2);
        m.BroadcastBufferedInt(FinalInt);

        m.BroadcastBufferedString("stale-1");
        m.BroadcastBufferedString("stale-2");
        m.BroadcastBufferedString(FinalString);

        m.BroadcastBufferedStruct(new ModuleObserversBufferedRpcsModule.TestPayload { id = 1, label = "stale-1", weight = 1f });
        m.BroadcastBufferedStruct(new ModuleObserversBufferedRpcsModule.TestPayload { id = 2, label = "stale-2", weight = 2f });
        m.BroadcastBufferedStruct(new ModuleObserversBufferedRpcsModule.TestPayload { id = FinalStructId, label = FinalStructLabel, weight = FinalStructWeight });

        m.BroadcastBufferedGeneric<int>(11);
        m.BroadcastBufferedGeneric<int>(22);
        m.BroadcastBufferedGeneric<int>(FinalGenericInt);

        m.BroadcastBufferedDeltaOff(1);
        m.BroadcastBufferedDeltaOff(7);
        m.BroadcastBufferedDeltaOff(FinalDeltaOff);

        m.BroadcastBufferedCompNone(1);
        m.BroadcastBufferedCompNone(2);
        m.BroadcastBufferedCompNone(FinalCompNone);

        m.BroadcastBufferedCompFast(1);
        m.BroadcastBufferedCompFast(2);
        m.BroadcastBufferedCompFast(FinalCompFast);

        m.BroadcastBufferedCompBalanced(1);
        m.BroadcastBufferedCompBalanced(2);
        m.BroadcastBufferedCompBalanced(FinalCompBalanced);

        m.BroadcastBufferedCompBest(1);
        m.BroadcastBufferedCompBest(2);
        m.BroadcastBufferedCompBest(FinalCompBest);

        m.BroadcastBufferedAsyncPackable(new ModuleObserversBufferedRpcsModule.AsyncPayload { seed = 1 });
        m.BroadcastBufferedAsyncPackable(new ModuleObserversBufferedRpcsModule.AsyncPayload { seed = 2 });
        m.BroadcastBufferedAsyncPackable(new ModuleObserversBufferedRpcsModule.AsyncPayload { seed = FinalAsyncSeed });

        m.NotifyPhaseAComplete();
    }

    private static bool AllVariantsReceived()
    {
        return ModuleObserversBufferedRpcsModule.IntReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.StringReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.StructReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.GenericIntReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.DeltaOffReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.CompNoneReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.CompFastReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.CompBalancedReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.CompBestReceiveCount > 0
            && ModuleObserversBufferedRpcsModule.AsyncReceiveCount > 0;
    }

    private static string ReceiveCountSummary()
    {
        return $"int={ModuleObserversBufferedRpcsModule.IntReceiveCount}, " +
               $"string={ModuleObserversBufferedRpcsModule.StringReceiveCount}, " +
               $"struct={ModuleObserversBufferedRpcsModule.StructReceiveCount}, " +
               $"generic={ModuleObserversBufferedRpcsModule.GenericIntReceiveCount}, " +
               $"deltaOff={ModuleObserversBufferedRpcsModule.DeltaOffReceiveCount}, " +
               $"compNone={ModuleObserversBufferedRpcsModule.CompNoneReceiveCount}, " +
               $"compFast={ModuleObserversBufferedRpcsModule.CompFastReceiveCount}, " +
               $"compBalanced={ModuleObserversBufferedRpcsModule.CompBalancedReceiveCount}, " +
               $"compBest={ModuleObserversBufferedRpcsModule.CompBestReceiveCount}, " +
               $"async={ModuleObserversBufferedRpcsModule.AsyncReceiveCount}";
    }

    private static void VerifyPhaseAValues(List<string> failures) => VerifyValues(failures, "PhaseA");
    private static void VerifyPhaseDValues(List<string> failures) => VerifyValues(failures, "PhaseD");

    private static void VerifyValues(List<string> failures, string phase)
    {
        if (ModuleObserversBufferedRpcsModule.LastIntReceived != FinalInt)
            LogFail(failures,$"{phase}.Int: expected {FinalInt}, got {ModuleObserversBufferedRpcsModule.LastIntReceived}");

        if (ModuleObserversBufferedRpcsModule.LastStringReceived != FinalString)
            LogFail(failures,$"{phase}.String: expected '{FinalString}', got '{ModuleObserversBufferedRpcsModule.LastStringReceived}'");

        var s = ModuleObserversBufferedRpcsModule.LastStructReceived;
        if (!s.HasValue || s.Value.id != FinalStructId || s.Value.label != FinalStructLabel || Mathf.Abs(s.Value.weight - FinalStructWeight) > 0.0001f)
            LogFail(failures,$"{phase}.Struct: expected (id={FinalStructId}, label='{FinalStructLabel}'), got {(s.HasValue ? $"(id={s.Value.id}, label='{s.Value.label}')" : "<null>")}");

        if (ModuleObserversBufferedRpcsModule.LastGenericIntReceived != FinalGenericInt)
            LogFail(failures,$"{phase}.Generic: expected {FinalGenericInt}, got {ModuleObserversBufferedRpcsModule.LastGenericIntReceived}");

        if (ModuleObserversBufferedRpcsModule.LastDeltaOffReceived != FinalDeltaOff)
            LogFail(failures,$"{phase}.DeltaOff: expected {FinalDeltaOff}, got {ModuleObserversBufferedRpcsModule.LastDeltaOffReceived}");

        if (ModuleObserversBufferedRpcsModule.LastCompNoneReceived != FinalCompNone)
            LogFail(failures,$"{phase}.CompNone: expected {FinalCompNone}, got {ModuleObserversBufferedRpcsModule.LastCompNoneReceived}");

        if (ModuleObserversBufferedRpcsModule.LastCompFastReceived != FinalCompFast)
            LogFail(failures,$"{phase}.CompFast: expected {FinalCompFast}, got {ModuleObserversBufferedRpcsModule.LastCompFastReceived}");

        if (ModuleObserversBufferedRpcsModule.LastCompBalancedReceived != FinalCompBalanced)
            LogFail(failures,$"{phase}.CompBalanced: expected {FinalCompBalanced}, got {ModuleObserversBufferedRpcsModule.LastCompBalancedReceived}");

        if (ModuleObserversBufferedRpcsModule.LastCompBestReceived != FinalCompBest)
            LogFail(failures,$"{phase}.CompBest: expected {FinalCompBest}, got {ModuleObserversBufferedRpcsModule.LastCompBestReceived}");

        if (ModuleObserversBufferedRpcsModule.LastAsyncSeedReceived != FinalAsyncSeed)
            LogFail(failures,$"{phase}.AsyncSeed: expected {FinalAsyncSeed}, got {ModuleObserversBufferedRpcsModule.LastAsyncSeedReceived}");

        if (ModuleObserversBufferedRpcsModule.LastAsyncPackStampReceived != FinalAsyncSeed + 1)
            LogFail(failures,$"{phase}.AsyncPack: expected {FinalAsyncSeed + 1}, got {ModuleObserversBufferedRpcsModule.LastAsyncPackStampReceived}");

        if (ModuleObserversBufferedRpcsModule.LastAsyncUnpackStampReceived != FinalAsyncSeed + 11)
            LogFail(failures,$"{phase}.AsyncUnpack: expected {FinalAsyncSeed + 11}, got {ModuleObserversBufferedRpcsModule.LastAsyncUnpackStampReceived}");
    }

    private static void VerifyBufferLastKeptOnlyOne(List<string> failures)
    {
        AssertOne(failures, "Int", ModuleObserversBufferedRpcsModule.IntReceiveCount);
        AssertOne(failures, "String", ModuleObserversBufferedRpcsModule.StringReceiveCount);
        AssertOne(failures, "Struct", ModuleObserversBufferedRpcsModule.StructReceiveCount);
        AssertOne(failures, "Generic", ModuleObserversBufferedRpcsModule.GenericIntReceiveCount);
        AssertOne(failures, "DeltaOff", ModuleObserversBufferedRpcsModule.DeltaOffReceiveCount);
        AssertOne(failures, "CompNone", ModuleObserversBufferedRpcsModule.CompNoneReceiveCount);
        AssertOne(failures, "CompFast", ModuleObserversBufferedRpcsModule.CompFastReceiveCount);
        AssertOne(failures, "CompBalanced", ModuleObserversBufferedRpcsModule.CompBalancedReceiveCount);
        AssertOne(failures, "CompBest", ModuleObserversBufferedRpcsModule.CompBestReceiveCount);
        AssertOne(failures, "Async", ModuleObserversBufferedRpcsModule.AsyncReceiveCount);
    }

    private static void AssertOne(List<string> failures, string label, int count)
    {
        if (count != 1)
            LogFail(failures,$"BufferLast.{label}: expected exactly 1 replay after reconnect, got {count}");
    }

    private static void LogFail(List<string> failures, string msg)
    {
        Debug.LogError($"[ModuleObserversBufferedRpcsScenario] {msg}");
        failures.Add(msg);
    }

    private static ScenarioResult LogAndFail(string msg)
    {
        Debug.LogError($"[ModuleObserversBufferedRpcsScenario] {msg}");
        return ScenarioResult.Fail(msg);
    }
}

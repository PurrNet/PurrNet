using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class ModuleTargetBufferedRpcsScenario : Scenario
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
    private const int FinalIEnumerator = 555;
    private const int FinalStructId = 99;
    private const string FinalStructLabel = "buffered";
    private const float FinalStructWeight = 9.99f;

    private ModuleTargetBufferedRpcs _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(nameof(ModuleTargetBufferedRpcsScenario));
        _prefab = go.AddComponent<ModuleTargetBufferedRpcs>();
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
            () => ModuleTargetBufferedRpcs.LocalInstance != null,
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
                () => ModuleTargetBufferedRpcsModule.KickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Server timed out waiting for client kickoff; got {ModuleTargetBufferedRpcsModule.KickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(ModuleTargetBufferedRpcs.LocalInstance, ModuleTargetBufferedRpcsModule.KickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetBufferedRpcsModule.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Server timed out waiting for client done; got {ModuleTargetBufferedRpcsModule.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok($"Done={ModuleTargetBufferedRpcsModule.ServerDoneCount}");
    }

    private async UniTask<ScenarioResult> RunAsHost(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = ModuleTargetBufferedRpcs.LocalInstance;

        inst.module.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetBufferedRpcsModule.KickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Host timed out on server-side kickoff count; got {ModuleTargetBufferedRpcsModule.KickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(inst, ModuleTargetBufferedRpcsModule.KickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetBufferedRpcsModule.PhaseACompleteCount > 0,
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,"Host did not receive PhaseAComplete locally");
        }

        VerifyPhaseAValues(failures);

        inst.module.SignalDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetBufferedRpcsModule.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Host timed out on server-side done count; got {ModuleTargetBufferedRpcsModule.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var inst = ModuleTargetBufferedRpcs.LocalInstance;
        inst.module.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ModuleTargetBufferedRpcsModule.PhaseACompleteCount > 0,
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail("Client did not receive PhaseAComplete; server may not have fired the buffered set");
        }

        VerifyPhaseAValues(failures);

        ModuleTargetBufferedRpcsModule.ResetClientState();
        ModuleTargetBufferedRpcs.LocalInstance = null;

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
                () => ModuleTargetBufferedRpcs.LocalInstance != null,
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
            LogFail(failures,$"Buffered TargetRpc replay incomplete after reconnect: {ReceiveCountSummary()}. " +
                         "Note: TargetRpc bufferLast replay requires the client's PlayerID to be preserved across reconnect (cookie scope LiveWithProcess or StorePersistently).");
        }

        VerifyPhaseDValues(failures);
        VerifyBufferLastKeptOnlyOne(failures);

        if (ModuleTargetBufferedRpcs.LocalInstance != null)
            ModuleTargetBufferedRpcs.LocalInstance.module.SignalDone();
        else
            LogFail(failures,"Cannot send SignalDone — LocalInstance is null after reconnect");

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static void FireBufferedSequence(ModuleTargetBufferedRpcs inst, List<PlayerID> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var m = inst.module;

            m.SendTargetBufferedInt(t, 1);
            m.SendTargetBufferedInt(t, 2);
            m.SendTargetBufferedInt(t, FinalInt);

            m.SendTargetBufferedString(t, "stale-1");
            m.SendTargetBufferedString(t, "stale-2");
            m.SendTargetBufferedString(t, FinalString);

            m.SendTargetBufferedStruct(t, new ModuleTargetBufferedRpcsModule.TestPayload { id = 1, label = "stale-1", weight = 1f });
            m.SendTargetBufferedStruct(t, new ModuleTargetBufferedRpcsModule.TestPayload { id = 2, label = "stale-2", weight = 2f });
            m.SendTargetBufferedStruct(t, new ModuleTargetBufferedRpcsModule.TestPayload { id = FinalStructId, label = FinalStructLabel, weight = FinalStructWeight });

            m.SendTargetBufferedGeneric<int>(t, 11);
            m.SendTargetBufferedGeneric<int>(t, 22);
            m.SendTargetBufferedGeneric<int>(t, FinalGenericInt);

            m.SendTargetBufferedDeltaOff(t, 1);
            m.SendTargetBufferedDeltaOff(t, 7);
            m.SendTargetBufferedDeltaOff(t, FinalDeltaOff);

            m.SendTargetBufferedCompNone(t, 1);
            m.SendTargetBufferedCompNone(t, 2);
            m.SendTargetBufferedCompNone(t, FinalCompNone);

            m.SendTargetBufferedCompFast(t, 1);
            m.SendTargetBufferedCompFast(t, 2);
            m.SendTargetBufferedCompFast(t, FinalCompFast);

            m.SendTargetBufferedCompBalanced(t, 1);
            m.SendTargetBufferedCompBalanced(t, 2);
            m.SendTargetBufferedCompBalanced(t, FinalCompBalanced);

            m.SendTargetBufferedCompBest(t, 1);
            m.SendTargetBufferedCompBest(t, 2);
            m.SendTargetBufferedCompBest(t, FinalCompBest);

            m.SendTargetBufferedAsyncPackable(t, new ModuleTargetBufferedRpcsModule.AsyncPayload { seed = 1 });
            m.SendTargetBufferedAsyncPackable(t, new ModuleTargetBufferedRpcsModule.AsyncPayload { seed = 2 });
            m.SendTargetBufferedAsyncPackable(t, new ModuleTargetBufferedRpcsModule.AsyncPayload { seed = FinalAsyncSeed });

            m.SendTargetBufferedIEnumerator(t, 11);
            m.SendTargetBufferedIEnumerator(t, 22);
            m.SendTargetBufferedIEnumerator(t, FinalIEnumerator);
        }

        inst.module.NotifyPhaseAComplete();
    }

    private static bool AllVariantsReceived()
    {
        return ModuleTargetBufferedRpcsModule.IntReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.StringReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.StructReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.GenericIntReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.DeltaOffReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.CompNoneReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.CompFastReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.CompBalancedReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.CompBestReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.AsyncReceiveCount > 0
            && ModuleTargetBufferedRpcsModule.IEnumeratorReceiveCount > 0;
    }

    private static string ReceiveCountSummary()
    {
        return $"int={ModuleTargetBufferedRpcsModule.IntReceiveCount}, " +
               $"string={ModuleTargetBufferedRpcsModule.StringReceiveCount}, " +
               $"struct={ModuleTargetBufferedRpcsModule.StructReceiveCount}, " +
               $"generic={ModuleTargetBufferedRpcsModule.GenericIntReceiveCount}, " +
               $"deltaOff={ModuleTargetBufferedRpcsModule.DeltaOffReceiveCount}, " +
               $"compNone={ModuleTargetBufferedRpcsModule.CompNoneReceiveCount}, " +
               $"compFast={ModuleTargetBufferedRpcsModule.CompFastReceiveCount}, " +
               $"compBalanced={ModuleTargetBufferedRpcsModule.CompBalancedReceiveCount}, " +
               $"compBest={ModuleTargetBufferedRpcsModule.CompBestReceiveCount}, " +
               $"async={ModuleTargetBufferedRpcsModule.AsyncReceiveCount}, " +
               $"ienum={ModuleTargetBufferedRpcsModule.IEnumeratorReceiveCount}";
    }

    private static void VerifyPhaseAValues(List<string> failures) => VerifyValues(failures, "PhaseA");
    private static void VerifyPhaseDValues(List<string> failures) => VerifyValues(failures, "PhaseD");

    private static void VerifyValues(List<string> failures, string phase)
    {
        if (ModuleTargetBufferedRpcsModule.LastIntReceived != FinalInt)
            LogFail(failures,$"{phase}.Int: expected {FinalInt}, got {ModuleTargetBufferedRpcsModule.LastIntReceived}");

        if (ModuleTargetBufferedRpcsModule.LastStringReceived != FinalString)
            LogFail(failures,$"{phase}.String: expected '{FinalString}', got '{ModuleTargetBufferedRpcsModule.LastStringReceived}'");

        var s = ModuleTargetBufferedRpcsModule.LastStructReceived;
        if (!s.HasValue || s.Value.id != FinalStructId || s.Value.label != FinalStructLabel || Mathf.Abs(s.Value.weight - FinalStructWeight) > 0.0001f)
            LogFail(failures,$"{phase}.Struct: expected (id={FinalStructId}, label='{FinalStructLabel}'), got {(s.HasValue ? $"(id={s.Value.id}, label='{s.Value.label}')" : "<null>")}");

        if (ModuleTargetBufferedRpcsModule.LastGenericIntReceived != FinalGenericInt)
            LogFail(failures,$"{phase}.Generic: expected {FinalGenericInt}, got {ModuleTargetBufferedRpcsModule.LastGenericIntReceived}");

        if (ModuleTargetBufferedRpcsModule.LastDeltaOffReceived != FinalDeltaOff)
            LogFail(failures,$"{phase}.DeltaOff: expected {FinalDeltaOff}, got {ModuleTargetBufferedRpcsModule.LastDeltaOffReceived}");

        if (ModuleTargetBufferedRpcsModule.LastCompNoneReceived != FinalCompNone)
            LogFail(failures,$"{phase}.CompNone: expected {FinalCompNone}, got {ModuleTargetBufferedRpcsModule.LastCompNoneReceived}");

        if (ModuleTargetBufferedRpcsModule.LastCompFastReceived != FinalCompFast)
            LogFail(failures,$"{phase}.CompFast: expected {FinalCompFast}, got {ModuleTargetBufferedRpcsModule.LastCompFastReceived}");

        if (ModuleTargetBufferedRpcsModule.LastCompBalancedReceived != FinalCompBalanced)
            LogFail(failures,$"{phase}.CompBalanced: expected {FinalCompBalanced}, got {ModuleTargetBufferedRpcsModule.LastCompBalancedReceived}");

        if (ModuleTargetBufferedRpcsModule.LastCompBestReceived != FinalCompBest)
            LogFail(failures,$"{phase}.CompBest: expected {FinalCompBest}, got {ModuleTargetBufferedRpcsModule.LastCompBestReceived}");

        if (ModuleTargetBufferedRpcsModule.LastAsyncSeedReceived != FinalAsyncSeed)
            LogFail(failures,$"{phase}.AsyncSeed: expected {FinalAsyncSeed}, got {ModuleTargetBufferedRpcsModule.LastAsyncSeedReceived}");

        if (ModuleTargetBufferedRpcsModule.LastAsyncPackStampReceived != FinalAsyncSeed + 1)
            LogFail(failures,$"{phase}.AsyncPack: PrepareForPackAsync did not run on sender — expected {FinalAsyncSeed + 1}, got {ModuleTargetBufferedRpcsModule.LastAsyncPackStampReceived}");

        if (ModuleTargetBufferedRpcsModule.LastAsyncUnpackStampReceived != FinalAsyncSeed + 11)
            LogFail(failures,$"{phase}.AsyncUnpack: PrepareAfterUnpackAsync did not run on receiver — expected {FinalAsyncSeed + 11}, got {ModuleTargetBufferedRpcsModule.LastAsyncUnpackStampReceived}");

        if (ModuleTargetBufferedRpcsModule.LastIEnumeratorReceived != FinalIEnumerator)
            LogFail(failures,$"{phase}.IEnumerator: expected {FinalIEnumerator}, got {ModuleTargetBufferedRpcsModule.LastIEnumeratorReceived}");
    }

    private static void VerifyBufferLastKeptOnlyOne(List<string> failures)
    {
        AssertOne(failures, "Int", ModuleTargetBufferedRpcsModule.IntReceiveCount);
        AssertOne(failures, "String", ModuleTargetBufferedRpcsModule.StringReceiveCount);
        AssertOne(failures, "Struct", ModuleTargetBufferedRpcsModule.StructReceiveCount);
        AssertOne(failures, "Generic", ModuleTargetBufferedRpcsModule.GenericIntReceiveCount);
        AssertOne(failures, "DeltaOff", ModuleTargetBufferedRpcsModule.DeltaOffReceiveCount);
        AssertOne(failures, "CompNone", ModuleTargetBufferedRpcsModule.CompNoneReceiveCount);
        AssertOne(failures, "CompFast", ModuleTargetBufferedRpcsModule.CompFastReceiveCount);
        AssertOne(failures, "CompBalanced", ModuleTargetBufferedRpcsModule.CompBalancedReceiveCount);
        AssertOne(failures, "CompBest", ModuleTargetBufferedRpcsModule.CompBestReceiveCount);
        AssertOne(failures, "Async", ModuleTargetBufferedRpcsModule.AsyncReceiveCount);
        AssertOne(failures, "IEnumerator", ModuleTargetBufferedRpcsModule.IEnumeratorReceiveCount);
    }

    private static void AssertOne(List<string> failures, string label, int count)
    {
        if (count != 1)
            LogFail(failures,$"BufferLast.{label}: expected exactly 1 replay after reconnect, got {count}");
    }

    private static void LogFail(List<string> failures, string msg)
    {
        Debug.LogError($"[ModuleTargetBufferedRpcsScenario] {msg}");
        failures.Add(msg);
    }

    private static ScenarioResult LogAndFail(string msg)
    {
        Debug.LogError($"[ModuleTargetBufferedRpcsScenario] {msg}");
        return ScenarioResult.Fail(msg);
    }
}

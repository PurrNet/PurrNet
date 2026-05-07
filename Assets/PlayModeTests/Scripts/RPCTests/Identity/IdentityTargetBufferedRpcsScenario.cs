using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

public class IdentityTargetBufferedRpcsScenario : Scenario
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

    private IdentityTargetBufferedRpcs _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var go = new GameObject(nameof(IdentityTargetBufferedRpcsScenario));
        _prefab = go.AddComponent<IdentityTargetBufferedRpcs>();
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
            () => IdentityTargetBufferedRpcs.LocalInstance != null,
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
                () => IdentityTargetBufferedRpcs.KickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Server timed out waiting for client kickoff; got {IdentityTargetBufferedRpcs.KickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(IdentityTargetBufferedRpcs.LocalInstance, IdentityTargetBufferedRpcs.KickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityTargetBufferedRpcs.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Server timed out waiting for client done; got {IdentityTargetBufferedRpcs.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok($"Done={IdentityTargetBufferedRpcs.ServerDoneCount}");
    }

    private async UniTask<ScenarioResult> RunAsHost(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = IdentityTargetBufferedRpcs.LocalInstance;

        inst.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityTargetBufferedRpcs.KickoffPlayers.Count >= ctx.expectedConnections,
                _kickoffTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail(
                $"Host timed out on server-side kickoff count; got {IdentityTargetBufferedRpcs.KickoffPlayers.Count}/{ctx.expectedConnections}");
        }

        FireBufferedSequence(inst, IdentityTargetBufferedRpcs.KickoffPlayers);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityTargetBufferedRpcs.PhaseACompleteCount > 0,
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
                () => IdentityTargetBufferedRpcs.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            LogFail(failures,
                $"Host timed out on server-side done count; got {IdentityTargetBufferedRpcs.ServerDoneCount}/{ctx.expectedConnections}");
        }

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        var inst = IdentityTargetBufferedRpcs.LocalInstance;
        inst.Kickoff();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityTargetBufferedRpcs.PhaseACompleteCount > 0,
                _phaseACompleteTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return LogAndFail("Client did not receive PhaseAComplete; server may not have fired the buffered set");
        }

        VerifyPhaseAValues(failures);

        IdentityTargetBufferedRpcs.ResetClientState();
        IdentityTargetBufferedRpcs.LocalInstance = null;

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
                () => IdentityTargetBufferedRpcs.LocalInstance != null,
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

        if (IdentityTargetBufferedRpcs.LocalInstance != null)
            IdentityTargetBufferedRpcs.LocalInstance.SignalDone();
        else
            LogFail(failures,"Cannot send SignalDone — LocalInstance is null after reconnect");

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static void FireBufferedSequence(IdentityTargetBufferedRpcs inst, List<PlayerID> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];

            inst.SendTargetBufferedInt(t, 1);
            inst.SendTargetBufferedInt(t, 2);
            inst.SendTargetBufferedInt(t, FinalInt);

            inst.SendTargetBufferedString(t, "stale-1");
            inst.SendTargetBufferedString(t, "stale-2");
            inst.SendTargetBufferedString(t, FinalString);

            inst.SendTargetBufferedStruct(t, new IdentityTargetBufferedRpcs.TestPayload { id = 1, label = "stale-1", weight = 1f });
            inst.SendTargetBufferedStruct(t, new IdentityTargetBufferedRpcs.TestPayload { id = 2, label = "stale-2", weight = 2f });
            inst.SendTargetBufferedStruct(t, new IdentityTargetBufferedRpcs.TestPayload { id = FinalStructId, label = FinalStructLabel, weight = FinalStructWeight });

            inst.SendTargetBufferedGeneric<int>(t, 11);
            inst.SendTargetBufferedGeneric<int>(t, 22);
            inst.SendTargetBufferedGeneric<int>(t, FinalGenericInt);

            inst.SendTargetBufferedDeltaOff(t, 1);
            inst.SendTargetBufferedDeltaOff(t, 7);
            inst.SendTargetBufferedDeltaOff(t, FinalDeltaOff);

            inst.SendTargetBufferedCompNone(t, 1);
            inst.SendTargetBufferedCompNone(t, 2);
            inst.SendTargetBufferedCompNone(t, FinalCompNone);

            inst.SendTargetBufferedCompFast(t, 1);
            inst.SendTargetBufferedCompFast(t, 2);
            inst.SendTargetBufferedCompFast(t, FinalCompFast);

            inst.SendTargetBufferedCompBalanced(t, 1);
            inst.SendTargetBufferedCompBalanced(t, 2);
            inst.SendTargetBufferedCompBalanced(t, FinalCompBalanced);

            inst.SendTargetBufferedCompBest(t, 1);
            inst.SendTargetBufferedCompBest(t, 2);
            inst.SendTargetBufferedCompBest(t, FinalCompBest);

            inst.SendTargetBufferedAsyncPackable(t, new IdentityTargetBufferedRpcs.AsyncPayload { seed = 1 });
            inst.SendTargetBufferedAsyncPackable(t, new IdentityTargetBufferedRpcs.AsyncPayload { seed = 2 });
            inst.SendTargetBufferedAsyncPackable(t, new IdentityTargetBufferedRpcs.AsyncPayload { seed = FinalAsyncSeed });

            inst.SendTargetBufferedIEnumerator(t, 11);
            inst.SendTargetBufferedIEnumerator(t, 22);
            inst.SendTargetBufferedIEnumerator(t, FinalIEnumerator);
        }

        inst.NotifyPhaseAComplete();
    }

    private static bool AllVariantsReceived()
    {
        return IdentityTargetBufferedRpcs.IntReceiveCount > 0
            && IdentityTargetBufferedRpcs.StringReceiveCount > 0
            && IdentityTargetBufferedRpcs.StructReceiveCount > 0
            && IdentityTargetBufferedRpcs.GenericIntReceiveCount > 0
            && IdentityTargetBufferedRpcs.DeltaOffReceiveCount > 0
            && IdentityTargetBufferedRpcs.CompNoneReceiveCount > 0
            && IdentityTargetBufferedRpcs.CompFastReceiveCount > 0
            && IdentityTargetBufferedRpcs.CompBalancedReceiveCount > 0
            && IdentityTargetBufferedRpcs.CompBestReceiveCount > 0
            && IdentityTargetBufferedRpcs.AsyncReceiveCount > 0
            && IdentityTargetBufferedRpcs.IEnumeratorReceiveCount > 0;
    }

    private static string ReceiveCountSummary()
    {
        return $"int={IdentityTargetBufferedRpcs.IntReceiveCount}, " +
               $"string={IdentityTargetBufferedRpcs.StringReceiveCount}, " +
               $"struct={IdentityTargetBufferedRpcs.StructReceiveCount}, " +
               $"generic={IdentityTargetBufferedRpcs.GenericIntReceiveCount}, " +
               $"deltaOff={IdentityTargetBufferedRpcs.DeltaOffReceiveCount}, " +
               $"compNone={IdentityTargetBufferedRpcs.CompNoneReceiveCount}, " +
               $"compFast={IdentityTargetBufferedRpcs.CompFastReceiveCount}, " +
               $"compBalanced={IdentityTargetBufferedRpcs.CompBalancedReceiveCount}, " +
               $"compBest={IdentityTargetBufferedRpcs.CompBestReceiveCount}, " +
               $"async={IdentityTargetBufferedRpcs.AsyncReceiveCount}, " +
               $"ienum={IdentityTargetBufferedRpcs.IEnumeratorReceiveCount}";
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
        if (IdentityTargetBufferedRpcs.LastIntReceived != FinalInt)
            LogFail(failures,$"{phase}.Int: expected {FinalInt}, got {IdentityTargetBufferedRpcs.LastIntReceived}");

        if (IdentityTargetBufferedRpcs.LastStringReceived != FinalString)
            LogFail(failures,$"{phase}.String: expected '{FinalString}', got '{IdentityTargetBufferedRpcs.LastStringReceived}'");

        var s = IdentityTargetBufferedRpcs.LastStructReceived;
        if (!s.HasValue || s.Value.id != FinalStructId || s.Value.label != FinalStructLabel || Mathf.Abs(s.Value.weight - FinalStructWeight) > 0.0001f)
            LogFail(failures,$"{phase}.Struct: expected (id={FinalStructId}, label='{FinalStructLabel}'), got {(s.HasValue ? $"(id={s.Value.id}, label='{s.Value.label}')" : "<null>")}");

        if (IdentityTargetBufferedRpcs.LastGenericIntReceived != FinalGenericInt)
            LogFail(failures,$"{phase}.Generic: expected {FinalGenericInt}, got {IdentityTargetBufferedRpcs.LastGenericIntReceived}");

        if (IdentityTargetBufferedRpcs.LastDeltaOffReceived != FinalDeltaOff)
            LogFail(failures,$"{phase}.DeltaOff: expected {FinalDeltaOff}, got {IdentityTargetBufferedRpcs.LastDeltaOffReceived}");

        if (IdentityTargetBufferedRpcs.LastCompNoneReceived != FinalCompNone)
            LogFail(failures,$"{phase}.CompNone: expected {FinalCompNone}, got {IdentityTargetBufferedRpcs.LastCompNoneReceived}");

        if (IdentityTargetBufferedRpcs.LastCompFastReceived != FinalCompFast)
            LogFail(failures,$"{phase}.CompFast: expected {FinalCompFast}, got {IdentityTargetBufferedRpcs.LastCompFastReceived}");

        if (IdentityTargetBufferedRpcs.LastCompBalancedReceived != FinalCompBalanced)
            LogFail(failures,$"{phase}.CompBalanced: expected {FinalCompBalanced}, got {IdentityTargetBufferedRpcs.LastCompBalancedReceived}");

        if (IdentityTargetBufferedRpcs.LastCompBestReceived != FinalCompBest)
            LogFail(failures,$"{phase}.CompBest: expected {FinalCompBest}, got {IdentityTargetBufferedRpcs.LastCompBestReceived}");

        if (IdentityTargetBufferedRpcs.LastAsyncSeedReceived != FinalAsyncSeed)
            LogFail(failures,$"{phase}.AsyncSeed: expected {FinalAsyncSeed}, got {IdentityTargetBufferedRpcs.LastAsyncSeedReceived}");

        if (IdentityTargetBufferedRpcs.LastAsyncPackStampReceived != FinalAsyncSeed + 1)
            LogFail(failures,$"{phase}.AsyncPack: PrepareForPackAsync did not run on sender — expected {FinalAsyncSeed + 1}, got {IdentityTargetBufferedRpcs.LastAsyncPackStampReceived}");

        if (IdentityTargetBufferedRpcs.LastAsyncUnpackStampReceived != FinalAsyncSeed + 11)
            LogFail(failures,$"{phase}.AsyncUnpack: PrepareAfterUnpackAsync did not run on receiver — expected {FinalAsyncSeed + 11}, got {IdentityTargetBufferedRpcs.LastAsyncUnpackStampReceived}");

        if (IdentityTargetBufferedRpcs.LastIEnumeratorReceived != FinalIEnumerator)
            LogFail(failures,$"{phase}.IEnumerator: expected {FinalIEnumerator}, got {IdentityTargetBufferedRpcs.LastIEnumeratorReceived}");
    }

    private static void VerifyBufferLastKeptOnlyOne(List<string> failures)
    {
        AssertOne(failures, "Int", IdentityTargetBufferedRpcs.IntReceiveCount);
        AssertOne(failures, "String", IdentityTargetBufferedRpcs.StringReceiveCount);
        AssertOne(failures, "Struct", IdentityTargetBufferedRpcs.StructReceiveCount);
        AssertOne(failures, "Generic", IdentityTargetBufferedRpcs.GenericIntReceiveCount);
        AssertOne(failures, "DeltaOff", IdentityTargetBufferedRpcs.DeltaOffReceiveCount);
        AssertOne(failures, "CompNone", IdentityTargetBufferedRpcs.CompNoneReceiveCount);
        AssertOne(failures, "CompFast", IdentityTargetBufferedRpcs.CompFastReceiveCount);
        AssertOne(failures, "CompBalanced", IdentityTargetBufferedRpcs.CompBalancedReceiveCount);
        AssertOne(failures, "CompBest", IdentityTargetBufferedRpcs.CompBestReceiveCount);
        AssertOne(failures, "Async", IdentityTargetBufferedRpcs.AsyncReceiveCount);
        AssertOne(failures, "IEnumerator", IdentityTargetBufferedRpcs.IEnumeratorReceiveCount);
    }

    private static void AssertOne(List<string> failures, string label, int count)
    {
        if (count != 1)
            LogFail(failures,$"BufferLast.{label}: expected exactly 1 replay after reconnect, got {count}");
    }

    private static void LogFail(List<string> failures, string msg)
    {
        Debug.LogError($"[IdentityTargetBufferedRpcsScenario] {msg}");
        failures.Add(msg);
    }

    private static ScenarioResult LogAndFail(string msg)
    {
        Debug.LogError($"[IdentityTargetBufferedRpcsScenario] {msg}");
        return ScenarioResult.Fail(msg);
    }
}

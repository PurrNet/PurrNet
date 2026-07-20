using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Re-adds an observer, lets SyncVar.OnObserverAdded seed the current value, then immediately
/// flushes a SyncVar update from a later module. The update must not be dropped just because it
/// shares the seed packet id.
/// </summary>
public class SyncVarObserverAddedDeltaScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _observerTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;

    private SyncVarObserverAddedDeltaIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarObserverAddedDeltaScenario));
        _prefab = go.AddComponent<SyncVarObserverAddedDeltaIdentity>();
        go.SetActive(false);
        SyncVarObserverAddedDeltaIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        if (ctx.isClient)
            SyncVarObserverAddedDeltaIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarObserverAddedDeltaIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {SyncVarObserverAddedDeltaIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to re-add as observer");

        ulong victimId = victim.Value.id.value;
        inst.BroadcastVictim(victimId);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!inst.BlacklistPlayer(victim.Value))
            failures.Add($"BlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.RemovedObservers.Contains(victimId),
                _observerTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"server-side OnObserverRemoved({victimId}) did not fire");
        }

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        inst.ArmPostSeedProbe(victimId);
        if (!inst.RemoveBlacklistPlayer(victim.Value))
            failures.Add($"RemoveBlacklistPlayer({victimId}) returned false on server");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.PostSeedProbeRan,
                _observerTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"post-seed probe did not run for re-added observer {victimId}");
            inst.BroadcastPhaseDone();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: done={SyncVarObserverAddedDeltaIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive victim id");
        }

        bool isVictim = SyncVarObserverAddedDeltaIdentity.VictimIdReceived
                        && ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == SyncVarObserverAddedDeltaIdentity.VictimPlayerId;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarObserverAddedDeltaIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive phase done");
        }

        if (isVictim)
        {
            var inst = SyncVarObserverAddedDeltaIdentity.LocalInstance;
            if (inst == null)
            {
                failures.Add("victim was not re-added before phase completion");
            }
            else
            {
                if (inst.currentValue != SyncVarObserverAddedDeltaIdentity.FirstDeltaValue)
                {
                    failures.Add(
                        $"victim missed first SyncVar delta after observer seed; " +
                        $"expected={SyncVarObserverAddedDeltaIdentity.FirstDeltaValue}, " +
                        $"{inst.DescribeLocalState()}");
                }
            }
        }

        if (SyncVarObserverAddedDeltaIdentity.LocalInstance != null)
            SyncVarObserverAddedDeltaIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "bystander")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static PlayerID? PickVictim(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;
            if (!best.HasValue || p.id.value < best.Value.id.value)
                best = p;
        }

        return best;
    }
}

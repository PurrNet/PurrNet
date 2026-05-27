using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Regression test for the SyncVar send-side re-arm freeze.
///
/// A continuously-changing owner-authoritative SyncVar is owned by a client. We then churn the
/// ownership away-and-back several times. Each ownerless window spans multiple ticks, so the
/// owner's perpetually-dirty SyncVar gets its tick subscription dropped while a change is pending.
/// When ownership returns, the owner is the controller again and keeps mutating the value, so its
/// updates MUST resume reaching the rest of the network.
///
/// On the buggy build, SetDirty short-circuits on the stuck `_isDirty` flag and never re-subscribes
/// to the tick loop, so the value keeps changing locally on the owner but the server's copy freezes.
/// </summary>
public class SyncVarReSyncScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _ownerlessWindowSeconds = 0.6f;
    [SerializeField] private float _observeTimeoutSeconds = 5f;
    [SerializeField] private int _blipCycles = 3;
    [SerializeField] private float _advanceMargin = 2f;

    private SyncVarReSyncIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarReSyncScenario));
        _prefab = go.AddComponent<SyncVarReSyncIdentity>();
        go.SetActive(false);
        SyncVarReSyncIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            // Spawn ownerless; we assign ownership explicitly. Otherwise the host's default
            // SpawnerIfClientOnly rule would make the host-local player the owner.
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncVarReSyncIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncVarReSyncIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarReSyncIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarReSyncIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {SyncVarReSyncIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the SyncVar");

        inst.GiveOwnership(owner.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        // Baseline sanity: the owner is mutating and the server is receiving the updates.
        if (!await WaitForAdvance(ctx, inst, _advanceMargin, _observeTimeoutSeconds))
        {
            failures.Add(
                $"pre-blip: server value did not advance (stuck at {inst.currentValue}); " +
                "the owner is not propagating its updates");
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        for (int i = 0; i < _blipCycles; i++)
        {
            inst.RemoveOwnership();
            await UniTask.WaitForSeconds(_ownerlessWindowSeconds, cancellationToken: ctx.cancellationToken);
            inst.GiveOwnership(owner.Value);
            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        }

        // The crux: after the churn, the owner is the controller again and still mutating the value
        // every frame. If SetDirty cannot re-arm the tick subscription, the owner's value keeps
        // changing locally but stops reaching the server -> frozen here.
        if (!await WaitForAdvance(ctx, inst, _advanceMargin, _observeTimeoutSeconds))
        {
            failures.Add(
                $"post-blip: server value froze at {inst.currentValue} after ownership churn " +
                "(owner still mutating locally but its updates stopped propagating)");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarReSyncIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {SyncVarReSyncIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, value={inst.currentValue}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarReSyncIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncVarReSyncIdentity.LocalInstance != null)
            SyncVarReSyncIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    /// <summary>
    /// Returns true if the server-observed value climbs by at least <paramref name="margin"/>
    /// within the timeout. The owner increments every frame, so a healthy stream advances quickly;
    /// a frozen stream never moves.
    /// </summary>
    private static async UniTask<bool> WaitForAdvance(
        ScenarioContext ctx, SyncVarReSyncIdentity inst, float margin, float timeout)
    {
        float start = inst.currentValue;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.currentValue >= start + margin,
                timeout,
                ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static PlayerID? PickOwner(ScenarioContext ctx)
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

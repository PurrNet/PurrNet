using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class OwnershipPropagationScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _propagationTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierEnd = 4400;

    private OwnershipPropagationParent _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(OwnershipPropagationScenario));
        _prefab = rootGo.AddComponent<OwnershipPropagationParent>();

        var childGo = new GameObject("Child");
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<OwnershipPropagationChild>();

        rootGo.SetActive(false);
        OwnershipPropagationParent.ResetAll();
        OwnershipPropagationChild.ResetAll();
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
                () => OwnershipPropagationParent.LocalInstance != null
                      && OwnershipPropagationChild.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"hierarchy spawn timeout: parent={OwnershipPropagationParent.LocalInstance != null}, " +
                $"child={OwnershipPropagationChild.LocalInstance != null}");
        }

        if (ctx.isClient)
            OwnershipPropagationParent.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var parent = OwnershipPropagationParent.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnershipPropagationParent.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {OwnershipPropagationParent.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to grant ownership");

        var victimId = victim.Value.id.value;
        parent.BroadcastVictim(victimId);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        // Default ServerStrict rules have propagateOwnershipByDefault=true, so giving
        // the parent ownership should also flow to the child.
        parent.GiveOwnership(victim.Value);

        // On host, the asServer=false callback fires only after the OwnershipChange
        // RPC round-trips through host-loopback transport; wait for it here so the
        // synchronous ValidateChange below doesn't race the inbound message.
        bool waitForClientSide = ctx.role == NetworkRole.Host;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasInitialChange(OwnershipPropagationParent.Changes, victimId, asServer: true)
                      && HasInitialChange(OwnershipPropagationChild.Changes, victimId, asServer: true)
                      && (!waitForClientSide
                          || (HasInitialChange(OwnershipPropagationParent.Changes, victimId, asServer: false)
                              && HasInitialChange(OwnershipPropagationChild.Changes, victimId, asServer: false))),
                _propagationTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-side propagation timeout: parentChanges={OwnershipPropagationParent.Changes.Count}, " +
                $"childChanges={OwnershipPropagationChild.Changes.Count}");
        }

        ValidateChange(ctx, OwnershipPropagationParent.Changes, victimId, asServer: true, "parent", failures);
        ValidateChange(ctx, OwnershipPropagationChild.Changes, victimId, asServer: true, "child", failures);

        // Host mode: also validate the host's client-side records, since they share static state.
        if (ctx.role == NetworkRole.Host)
        {
            ValidateChange(ctx, OwnershipPropagationParent.Changes, victimId, asServer: false, "parent (host-client)", failures);
            ValidateChange(ctx, OwnershipPropagationChild.Changes, victimId, asServer: false, "child (host-client)", failures);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnershipPropagationParent.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {OwnershipPropagationParent.ServerDoneCount}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnershipPropagationParent.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastVictim");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        var victimId = OwnershipPropagationParent.VictimPlayerId;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasInitialChange(OwnershipPropagationParent.Changes, victimId, asServer: false)
                      && HasInitialChange(OwnershipPropagationChild.Changes, victimId, asServer: false),
                _propagationTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"client-side propagation timeout: parentChanges={OwnershipPropagationParent.Changes.Count}, " +
                $"childChanges={OwnershipPropagationChild.Changes.Count}");
        }

        ValidateChange(ctx, OwnershipPropagationParent.Changes, victimId, asServer: false, "parent", failures);
        ValidateChange(ctx, OwnershipPropagationChild.Changes, victimId, asServer: false, "child", failures);

        if (OwnershipPropagationParent.LocalInstance != null)
            OwnershipPropagationParent.LocalInstance.SignalDone();

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static bool HasInitialChange(List<OwnershipPropagationParent.ChangeRecord> records,
        ulong victimId, bool asServer)
    {
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.asServer != asServer) continue;
            if (r.oldHasValue) continue; // not the initial null->X
            if (!r.newHasValue) continue;
            if (r.newOwnerId == victimId) return true;
        }
        return false;
    }

    private static void ValidateChange(ScenarioContext ctx, List<OwnershipPropagationParent.ChangeRecord> records,
        ulong victimId, bool asServer, string label, List<string> failures)
    {
        bool found = false;
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.asServer != asServer) continue;
            if (r.oldHasValue || !r.newHasValue || r.newOwnerId != victimId) continue;
            found = true;

            if (asServer)
            {
                // Server is never the new owner here (we always grant to a non-server client).
                if (r.isOwnerAfter)
                    failures.Add($"{label} server-side change: isOwner=true unexpected on server");
                // hasConnectedOwner -> isController = isOwner (= false). No-owner branch doesn't apply
                // to this transition since newOwner is always set.
                if (r.isControllerAfter)
                    failures.Add($"{label} server-side change: isController=true unexpected (server is not the owner)");
            }
            else
            {
                // Client side: isOwner is true iff the local player is the victim.
                bool isVictimPeer = ctx.networkManager.isLocalPlayerReady
                                    && ctx.networkManager.localPlayer.id.value == victimId;
                if (r.isOwnerAfter != isVictimPeer)
                    failures.Add(
                        $"{label} client-side change: isOwner={r.isOwnerAfter}, expected {isVictimPeer} " +
                        $"(local={(ctx.networkManager.isLocalPlayerReady ? ctx.networkManager.localPlayer.id.value : 0)}, victim={victimId})");
                // On a non-server peer, isController == isOwner (no-owner branch doesn't apply).
                if (r.isControllerAfter != isVictimPeer)
                    failures.Add(
                        $"{label} client-side change: isController={r.isControllerAfter}, expected {isVictimPeer}");
            }
            break;
        }
        if (!found)
            failures.Add($"{label} did not record initial null->{victimId} change with asServer={asServer}");
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

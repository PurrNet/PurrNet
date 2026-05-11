using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class OwnerChangeScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;

    private OwnerChangeIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(OwnerChangeScenario));
        _prefab = go.AddComponent<OwnerChangeIdentity>();
        go.SetActive(false);
        OwnerChangeIdentity.ResetAll();
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
            // Suppress the default SpawnerIfClientOnly auto-owner rule so the spawn is
            // truly ownerless on host. Otherwise the host's local player gets an extra
            // null->hostLocal record before our explicit transitions, breaking the
            // exact-chain assertions on the client side.
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => OwnerChangeIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        // Each client (and the host's client side) signals it has the local instance.
        if (ctx.isClient)
            OwnerChangeIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = OwnerChangeIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerChangeIdentity.ServerReadyCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {OwnerChangeIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        // Discard any spawn-time owner changes (none expected with defaultOwner=None,
        // but make the test robust to rule changes).
        OwnerChangeIdentity.ResetTransitionRecords();

        // Pick targets: the lowest non-server PlayerIDs.
        var clients = new List<PlayerID>();
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].isServer)
                clients.Add(players[i]);
        }
        clients.Sort((a, b) => a.id.value.CompareTo(b.id.value));

        if (clients.Count == 0)
            return ScenarioResult.Fail("no non-server clients to assign ownership to");

        var transitions = new List<PlayerID?>();
        transitions.Add(clients[0]); // give to A
        if (clients.Count >= 2)
            transitions.Add(clients[1]); // transfer A -> B
        transitions.Add(null); // remove

        for (int i = 0; i < transitions.Count; i++)
        {
            var next = transitions[i];
            if (next.HasValue)
                inst.GiveOwnership(next.Value);
            else
                inst.RemoveOwnership();

            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        }

        inst.BroadcastExpectedTransitions(transitions.Count);

        // Wait until our own server-side records reflect the full sequence.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerChangeIdentity.ThreeArgRecords.Count >= transitions.Count
                      && OwnerChangeIdentity.FourArgRecords.Count >= transitions.Count,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-side records timeout: 3-arg={OwnerChangeIdentity.ThreeArgRecords.Count}, 4-arg={OwnerChangeIdentity.FourArgRecords.Count} expected={transitions.Count}");
        }

        VerifyChain(ctx, transitions, failures);

        // Wait for clients to validate and report done.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerChangeIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {OwnerChangeIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"Transitions={transitions.Count}, Done={OwnerChangeIdentity.ServerDoneCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = OwnerChangeIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OwnerChangeIdentity.ExpectedTransitions > 0
                      && OwnerChangeIdentity.ThreeArgRecords.Count >= OwnerChangeIdentity.ExpectedTransitions
                      && OwnerChangeIdentity.FourArgRecords.Count >= OwnerChangeIdentity.ExpectedTransitions,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"client records timeout: expected={OwnerChangeIdentity.ExpectedTransitions}, 3-arg={OwnerChangeIdentity.ThreeArgRecords.Count}, 4-arg={OwnerChangeIdentity.FourArgRecords.Count}");
        }

        // Build the expected transition chain from our records (we don't know server's intent
        // ahead of time, so we just assert the chain is internally consistent and isOwner is correct).
        VerifyClientRecords(ctx, failures);

        inst.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void VerifyChain(ScenarioContext ctx, List<PlayerID?> transitions, List<string> failures)
    {
        // Server-side: asServer=true should match our transitions exactly.
        FilterAndCheckChain("server-3arg", OwnerChangeIdentity.ThreeArgRecords, true, transitions,
            ctx.networkManager.localPlayer, failures);
        FilterAndCheckChain("server-4arg", OwnerChangeIdentity.FourArgRecords, true, transitions,
            ctx.networkManager.localPlayer, failures);

        // Host mode: the server identity also runs as a client; assert client-side records too.
        if (ctx.role == NetworkRole.Host)
        {
            FilterAndCheckChain("host-client-3arg", OwnerChangeIdentity.ThreeArgRecords, false, transitions,
                ctx.networkManager.localPlayer, failures);
            FilterAndCheckChain("host-client-4arg", OwnerChangeIdentity.FourArgRecords, false, transitions,
                ctx.networkManager.localPlayer, failures);
        }
    }

    private static void VerifyClientRecords(ScenarioContext ctx, List<string> failures)
    {
        // On a pure client, all records have asServer=false.
        if (OwnerChangeIdentity.ThreeArgRecords.Count != OwnerChangeIdentity.FourArgRecords.Count)
        {
            failures.Add(
                $"3-arg/4-arg counts differ: {OwnerChangeIdentity.ThreeArgRecords.Count} vs {OwnerChangeIdentity.FourArgRecords.Count}");
        }

        var localId = ctx.networkManager.localPlayer.id.value;
        var localHasId = ctx.networkManager.isLocalPlayerReady;

        // The chain must be internally consistent: each record's old must equal previous record's new.
        OwnerChangeIdentity.ChangeRecord? prev = null;
        for (int i = 0; i < OwnerChangeIdentity.ThreeArgRecords.Count; i++)
        {
            var r = OwnerChangeIdentity.ThreeArgRecords[i];
            if (r.asServer)
            {
                failures.Add($"client 3-arg[{i}] had asServer=true (expected false on a pure client)");
                continue;
            }

            if (prev.HasValue)
            {
                if (r.oldOwnerHasValue != prev.Value.newOwnerHasValue ||
                    (r.oldOwnerHasValue && r.oldOwnerId != prev.Value.newOwnerId))
                {
                    failures.Add(
                        $"client 3-arg[{i}] chain break: old={(r.oldOwnerHasValue ? r.oldOwnerId.ToString() : "null")}, expected={(prev.Value.newOwnerHasValue ? prev.Value.newOwnerId.ToString() : "null")}");
                }
            }

            // hasOwner must reflect newOwner.HasValue.
            if (r.hasOwnerAfter != r.newOwnerHasValue)
                failures.Add($"client 3-arg[{i}] hasOwner mismatch: hasOwner={r.hasOwnerAfter}, newOwner.HasValue={r.newOwnerHasValue}");

            // No disconnects in this scenario, so hasConnectedOwner must equal hasOwner.
            if (r.hasConnectedOwnerAfter != r.hasOwnerAfter)
                failures.Add($"client 3-arg[{i}] hasConnectedOwner mismatch: hasConnectedOwner={r.hasConnectedOwnerAfter}, hasOwner={r.hasOwnerAfter}");

            // isOwner must be true iff localPlayer == newOwner.
            bool expectedIsOwner = localHasId && r.newOwnerHasValue && r.newOwnerId == localId;
            if (r.isOwnerAfter != expectedIsOwner)
            {
                failures.Add(
                    $"client 3-arg[{i}] isOwner mismatch: got {r.isOwnerAfter}, expected {expectedIsOwner} (localId={localId}, newOwnerId={(r.newOwnerHasValue ? r.newOwnerId.ToString() : "null")})");
            }

            // isController on a pure client: true iff localPlayer is the connected owner.
            // No-owner case → false (we're not the server).
            bool expectedIsController = expectedIsOwner;
            if (r.isControllerAfter != expectedIsController)
            {
                failures.Add(
                    $"client 3-arg[{i}] isController mismatch: got {r.isControllerAfter}, expected {expectedIsController}");
            }

            prev = r;
        }
    }

    private static void FilterAndCheckChain(string label, List<OwnerChangeIdentity.ChangeRecord> records, bool asServer,
        List<PlayerID?> expected, PlayerID localPlayer, List<string> failures)
    {
        var filtered = new List<OwnerChangeIdentity.ChangeRecord>(records.Count);
        for (int i = 0; i < records.Count; i++)
            if (records[i].asServer == asServer) filtered.Add(records[i]);

        if (filtered.Count != expected.Count)
        {
            failures.Add($"{label} count mismatch: got {filtered.Count}, expected {expected.Count}");
            return;
        }

        ulong? expectedPrevNew = null;
        for (int i = 0; i < expected.Count; i++)
        {
            var rec = filtered[i];
            var nextNew = expected[i];

            // Verify oldOwner matches expectedPrevNew.
            bool prevHas = expectedPrevNew.HasValue;
            if (rec.oldOwnerHasValue != prevHas ||
                (prevHas && rec.oldOwnerId != expectedPrevNew.Value))
            {
                failures.Add(
                    $"{label}[{i}] oldOwner mismatch: got {(rec.oldOwnerHasValue ? rec.oldOwnerId.ToString() : "null")}, expected {(prevHas ? expectedPrevNew.Value.ToString() : "null")}");
            }

            // Verify newOwner matches the transition target.
            bool nextHas = nextNew.HasValue;
            ulong nextId = nextHas ? nextNew.Value.id.value : 0;
            if (rec.newOwnerHasValue != nextHas ||
                (nextHas && rec.newOwnerId != nextId))
            {
                failures.Add(
                    $"{label}[{i}] newOwner mismatch: got {(rec.newOwnerHasValue ? rec.newOwnerId.ToString() : "null")}, expected {(nextHas ? nextId.ToString() : "null")}");
            }

            // hasOwner reflects newOwner.HasValue.
            if (rec.hasOwnerAfter != nextHas)
                failures.Add($"{label}[{i}] hasOwner={rec.hasOwnerAfter}, expected {nextHas}");

            // No disconnects in this scenario, so hasConnectedOwner must equal hasOwner.
            if (rec.hasConnectedOwnerAfter != rec.hasOwnerAfter)
                failures.Add($"{label}[{i}] hasConnectedOwner={rec.hasConnectedOwnerAfter}, expected {rec.hasOwnerAfter}");

            // For the server side, isOwner is true on host iff localPlayer == newOwner; on a dedicated server localPlayer may be default(0).
            // We don't assert isOwner on the server-side records (asServer=true); it's a server perspective and not user-meaningful.
            if (!asServer)
            {
                bool expectedIsOwner = nextHas && nextId == localPlayer.id.value;
                if (rec.isOwnerAfter != expectedIsOwner)
                {
                    failures.Add(
                        $"{label}[{i}] isOwner mismatch: got {rec.isOwnerAfter}, expected {expectedIsOwner}");
                }

                // isController on a non-server record: true iff localPlayer is the connected owner.
                // No-owner case → false (this side is not the server).
                if (rec.isControllerAfter != expectedIsOwner)
                {
                    failures.Add(
                        $"{label}[{i}] isController mismatch: got {rec.isControllerAfter}, expected {expectedIsOwner}");
                }
            }
            else
            {
                // isController on a server-side record: hasConnectedOwner ? isOwner : true.
                // Our test gives ownership to clients (never the server), so when an owner is set
                // the server is not the owner → expected false. When no owner, expected true.
                bool expectedIsController = nextHas ? rec.isOwnerAfter : true;
                if (rec.isControllerAfter != expectedIsController)
                {
                    failures.Add(
                        $"{label}[{i}] isController mismatch: got {rec.isControllerAfter}, expected {expectedIsController} (newOwner={(nextHas ? nextId.ToString() : "null")})");
                }
            }

            expectedPrevNew = nextHas ? (ulong?)nextId : null;
        }
    }
}

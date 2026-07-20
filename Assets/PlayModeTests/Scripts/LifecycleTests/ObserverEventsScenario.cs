using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class ObserverEventsScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _observersTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private ObserverEventsIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ObserverEventsScenario));
        _prefab = go.AddComponent<ObserverEventsIdentity>();
        go.SetActive(false);
        ObserverEventsIdentity.ResetAll();
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

        await UniTaskUtils.WaitWithTimeout(
            () => ObserverEventsIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();

        // Build the set of expected observer PlayerIDs: every player currently known to the server.
        // In host mode this includes the host's local player. expectedConnections is the number of
        // distinct connections including the host as a client when applicable.
        var expectedIds = new HashSet<ulong>();
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
            expectedIds.Add(players[i].id.value);

        // Wait until we've seen OnObserverAdded fire for every expected observer (no-arg variant).
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => CoversAll(ObserverEventsIdentity.AddedNoArg, expectedIds)
                      && CoversAll(ObserverEventsIdentity.AddedWithSpawner, expectedIds)
                      && CoversAll(ObserverEventsIdentity.PreAddedNoArg, expectedIds)
                      && CoversAll(ObserverEventsIdentity.PreAddedWithSpawner, expectedIds),
                _observersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(BuildCoverageReport(expectedIds));
        }

        VerifyServerRecords(expectedIds, failures);

        // Tell every client they can finish.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ObserverEventsIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {ObserverEventsIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"Observers={expectedIds.Count}, Done={ObserverEventsIdentity.ServerDoneCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        // Observer-add/remove callbacks fire only on the server side. A pure client just signals
        // that it has seen the spawn so the server's expectedConnections counter advances.
        ObserverEventsIdentity.LocalInstance.SignalDone();
        await UniTask.Yield();
        return ScenarioResult.Ok();
    }

    private static bool CoversAll(List<ObserverEventsIdentity.ObserverRecord> records, HashSet<ulong> expectedIds)
    {
        var seen = new HashSet<ulong>();
        for (int i = 0; i < records.Count; i++)
            seen.Add(records[i].playerId);

        foreach (var id in expectedIds)
            if (!seen.Contains(id)) return false;
        return true;
    }

    private static void VerifyServerRecords(HashSet<ulong> expectedIds, List<string> failures)
    {
        // Each observer must produce exactly one of each of the four add records.
        VerifyOnePerObserver("OnPreObserverAdded(player)", ObserverEventsIdentity.PreAddedNoArg, expectedIds, failures);
        VerifyOnePerObserver("OnPreObserverAdded(player, isSpawner)", ObserverEventsIdentity.PreAddedWithSpawner, expectedIds, failures);
        VerifyOnePerObserver("OnObserverAdded(player)", ObserverEventsIdentity.AddedNoArg, expectedIds, failures);
        VerifyOnePerObserver("OnObserverAdded(player, isSpawner)", ObserverEventsIdentity.AddedWithSpawner, expectedIds, failures);

        // The isSpawner overloads must report hasIsSpawner=true on every record.
        VerifyHasIsSpawner("OnPreObserverAdded(player, isSpawner)", ObserverEventsIdentity.PreAddedWithSpawner, failures);
        VerifyHasIsSpawner("OnObserverAdded(player, isSpawner)", ObserverEventsIdentity.AddedWithSpawner, failures);

        // The server-spawned identity has no spawner client (server spawned it), so isSpawner must be
        // false for every observer record.
        VerifyIsSpawnerFalse("OnPreObserverAdded(player, isSpawner)", ObserverEventsIdentity.PreAddedWithSpawner, failures);
        VerifyIsSpawnerFalse("OnObserverAdded(player, isSpawner)", ObserverEventsIdentity.AddedWithSpawner, failures);

        // No observer should have been removed during the scenario.
        if (ObserverEventsIdentity.Removed.Count != 0)
            failures.Add($"OnObserverRemoved fired unexpectedly {ObserverEventsIdentity.Removed.Count} time(s)");
    }

    private static void VerifyOnePerObserver(string label, List<ObserverEventsIdentity.ObserverRecord> records,
        HashSet<ulong> expectedIds, List<string> failures)
    {
        var counts = new Dictionary<ulong, int>();
        for (int i = 0; i < records.Count; i++)
        {
            var id = records[i].playerId;
            counts.TryGetValue(id, out var c);
            counts[id] = c + 1;
        }

        foreach (var id in expectedIds)
        {
            counts.TryGetValue(id, out var c);
            if (c != 1)
                failures.Add($"{label}: player {id} fired {c} time(s), expected 1");
        }

        // Anything in records not in expectedIds is an extra fire.
        foreach (var kv in counts)
        {
            if (!expectedIds.Contains(kv.Key))
                failures.Add($"{label}: unexpected fire for player {kv.Key} ({kv.Value} time(s))");
        }
    }

    private static void VerifyHasIsSpawner(string label, List<ObserverEventsIdentity.ObserverRecord> records,
        List<string> failures)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (!records[i].hasIsSpawner)
                failures.Add($"{label}[{i}] hasIsSpawner was false; expected the isSpawner overload to be invoked");
        }
    }

    private static void VerifyIsSpawnerFalse(string label, List<ObserverEventsIdentity.ObserverRecord> records,
        List<string> failures)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].isSpawner)
                failures.Add($"{label}[{i}] isSpawner was true for a server-spawned identity (player {records[i].playerId})");
        }
    }

    private static string BuildCoverageReport(HashSet<ulong> expectedIds)
    {
        return $"observer coverage timeout: expected={expectedIds.Count}, " +
               $"PreAddedNoArg={ObserverEventsIdentity.PreAddedNoArg.Count}, " +
               $"PreAddedWithSpawner={ObserverEventsIdentity.PreAddedWithSpawner.Count}, " +
               $"AddedNoArg={ObserverEventsIdentity.AddedNoArg.Count}, " +
               $"AddedWithSpawner={ObserverEventsIdentity.AddedWithSpawner.Count}";
    }
}

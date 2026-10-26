using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Two NetworkIdentity components on one GameObject each keep their own observers. Hiding only
// one of them from a player must leave the object in place for that player and silence just
// that component: no despawn, no second spawn, and no observer callbacks on the component whose
// observers did not change. Showing it again must catch that component up without a respawn.
// A component that was silenced before the whole object leaves is not reported twice, and
// by the time the object is destroyed every OnObserverAdded has had its OnObserverRemoved.
public class SiblingObserversScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10200;
    private const int BarrierHidden = BarrierBase + 1;
    private const int BarrierRestored = BarrierBase + 2;
    private const int BarrierRespawned = BarrierBase + 3;

    private const int PhaseHidden = 1;
    private const int PhaseRestored = 2;
    private const int PhaseRespawned = 3;

    private const int HiddenValue = 1;
    private const int RestoredPing = 2;
    private const int RespawnedPing = 3;

    private const int First = 0;
    private const int Second = 1;

    private static int _phase;
    private static ulong _victimId;
    private static readonly List<string> _loggedErrors = new();

    private SiblingObserversIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SiblingObserversScenario));
        _prefab = go.AddComponent<SiblingObserversIdentity>();
        _prefab.slot = First;
        go.AddComponent<SiblingObserversIdentity>().slot = Second;
        go.SetActive(false);
        SiblingObserversIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _phase = 0;
        _victimId = 0;

        if (ctx.isServer)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SiblingObserversIdentity.LocalInstances[First] != null
                      && SiblingObserversIdentity.LocalInstances[Second] != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"initial spawn never reached this peer: first={SiblingObserversIdentity.LocalInstances[First] != null}, " +
                $"second={SiblingObserversIdentity.LocalInstances[Second] != null}");
        }

        if (ctx.isClient)
            SiblingObserversIdentity.LocalInstances[First].SignalReady();

        _loggedErrors.Clear();
        Application.logMessageReceived += OnLogMessage;
        try
        {
            return WithLoggedErrors(await RunSplit(ctx, RunAsClient, RunAsServer));
        }
        finally
        {
            Application.logMessageReceived -= OnLogMessage;
        }
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var first = SiblingObserversIdentity.LocalInstances[First];
        var second = SiblingObserversIdentity.LocalInstances[Second];

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SiblingObserversIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {SiblingObserversIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to blacklist");

        var victimId = victim.Value.id.value;

        // Hide only the first component. The second one still lists the victim, so the
        // GameObject has to stay where it is for them: nothing to despawn, nothing to spawn.
        var since = Snapshot();

        if (!first.BlacklistPlayer(victim.Value))
            failures.Add($"BlacklistPlayer({victimId}) returned false on server");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        first.counter = HiddenValue;
        second.counter = HiddenValue;
        first.Ping(HiddenValue);
        second.Ping(HiddenValue);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (first.IsObserver(victim.Value))
            failures.Add("hidden: first component still lists the victim as observer");
        if (!second.IsObserver(victim.Value))
            failures.Add("hidden: second component lost the victim although only the first was blacklisted");

        ExpectCalls(failures, "hidden", "first OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[First], since.removed[First], 1, victimId);
        ExpectCalls(failures, "hidden", "first OnObserverAdded",
            SiblingObserversIdentity.ObserverAddedCalls[First], since.added[First], 0, victimId);
        ExpectCalls(failures, "hidden", "second OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[Second], since.removed[Second], 0, victimId);
        ExpectCalls(failures, "hidden", "second OnObserverAdded",
            SiblingObserversIdentity.ObserverAddedCalls[Second], since.added[Second], 0, victimId);

        BroadcastPhase(PhaseHidden, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierHidden, _barrierTimeoutSeconds);

        // Show it again. Only the first component gains an observer; the second never lost it.
        // The counters are left alone so the victim can only learn the first one's value from
        // the catch-up that comes with observing it again.
        since = Snapshot();

        if (!first.RemoveBlacklistPlayer(victim.Value))
            failures.Add($"RemoveBlacklistPlayer({victimId}) returned false on server");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        first.Ping(RestoredPing);
        second.Ping(RestoredPing);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!first.IsObserver(victim.Value))
            failures.Add("restored: first component did not get the victim back as observer");
        if (!second.IsObserver(victim.Value))
            failures.Add("restored: second component lost the victim");

        ExpectCalls(failures, "restored", "first OnObserverAdded",
            SiblingObserversIdentity.ObserverAddedCalls[First], since.added[First], 1, victimId);
        ExpectCalls(failures, "restored", "first OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[First], since.removed[First], 0, victimId);
        ExpectCalls(failures, "restored", "second OnObserverAdded",
            SiblingObserversIdentity.ObserverAddedCalls[Second], since.added[Second], 0, victimId);
        ExpectCalls(failures, "restored", "second OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[Second], since.removed[Second], 0, victimId);

        BroadcastPhase(PhaseRestored, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierRestored, _barrierTimeoutSeconds);

        // Silence the first component, hide the whole object, then show it again through the
        // second component only. The first one already reported the victim as removed when it
        // was silenced, the object leaving must not report it a second time.
        since = Snapshot();

        first.BlacklistPlayer(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
        second.BlacklistPlayer(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (first.IsObserver(victim.Value) || second.IsObserver(victim.Value))
            failures.Add("respawned: victim still observes a component after both were blacklisted");

        second.RemoveBlacklistPlayer(victim.Value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        first.Ping(RespawnedPing);
        second.Ping(RespawnedPing);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (first.IsObserver(victim.Value))
            failures.Add("respawned: first component lists the victim although it is still blacklisted");
        if (!second.IsObserver(victim.Value))
            failures.Add("respawned: second component did not get the victim back as observer");

        ExpectCalls(failures, "respawned", "first OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[First], since.removed[First], 1, victimId);
        ExpectCalls(failures, "respawned", "second OnObserverRemoved",
            SiblingObserversIdentity.ObserverRemovedCalls[Second], since.removed[Second], 1, victimId);
        ExpectCalls(failures, "respawned", "second OnObserverAdded",
            SiblingObserversIdentity.ObserverAddedCalls[Second], since.added[Second], 1, victimId);

        BroadcastPhase(PhaseRespawned, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierRespawned, _barrierTimeoutSeconds);

        if (first)
            Destroy(first.gameObject);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        for (var slot = 0; slot < SiblingObserversIdentity.Slots; slot++)
            ExpectBalanced(failures, slot);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        await WaitForPhase(ctx, PhaseHidden, failures);
        bool isVictim = IsVictim(ctx);

        ExpectLifetime(failures, "hidden", 1, 0);

        // Each component only talks to its own observers, so the victim keeps hearing from
        // the second component while the first one goes quiet.
        int firstPing = SiblingObserversIdentity.LastPing[First];
        int firstCounter = SiblingObserversIdentity.LocalInstances[First]
            ? SiblingObserversIdentity.LocalInstances[First].counter
            : -1;

        if (isVictim)
        {
            if (firstPing == HiddenValue)
                failures.Add("hidden: victim received an ObserversRpc from the component it was blacklisted on");
            if (firstCounter == HiddenValue)
                failures.Add("hidden: victim received a SyncVar change from the component it was blacklisted on");
        }
        else
        {
            if (firstPing != HiddenValue)
                failures.Add($"hidden: bystander missed the first component's ObserversRpc (last={firstPing})");
            if (firstCounter != HiddenValue)
                failures.Add($"hidden: bystander missed the first component's SyncVar change (value={firstCounter})");
        }

        ExpectDelivered(failures, "hidden", Second, HiddenValue, HiddenValue);

        await ScenarioBarrier.Wait(ctx, BarrierHidden, _barrierTimeoutSeconds);

        await WaitForPhase(ctx, PhaseRestored, failures);

        ExpectLifetime(failures, "restored", 1, 0);
        ExpectDelivered(failures, "restored", First, RestoredPing, HiddenValue);
        ExpectDelivered(failures, "restored", Second, RestoredPing, HiddenValue);

        await ScenarioBarrier.Wait(ctx, BarrierRestored, _barrierTimeoutSeconds);

        await WaitForPhase(ctx, PhaseRespawned, failures);

        if (isVictim)
        {
            ExpectLifetime(failures, "respawned", 2, 1);

            if (SiblingObserversIdentity.LastPing[First] == RespawnedPing)
                failures.Add("respawned: victim received an ObserversRpc from the component that is still blacklisted");
        }
        else
        {
            ExpectLifetime(failures, "respawned", 1, 0);
            ExpectDelivered(failures, "respawned", First, RespawnedPing, HiddenValue);
        }

        ExpectDelivered(failures, "respawned", Second, RespawnedPing, HiddenValue);

        await ScenarioBarrier.Wait(ctx, BarrierRespawned, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "bystander")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask WaitForPhase(ScenarioContext ctx, int phase, List<string> failures)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phase >= phase,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"phase {phase} was never announced (at {_phase})");
        }
    }

    private static bool IsVictim(ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == _victimId;
    }

    // Changing the observers of a single component must never spawn or despawn the object on a
    // peer that still has it through the other component.
    private static void ExpectLifetime(List<string> failures, string phase, int expectedSpawned, int expectedDespawned)
    {
        for (var slot = 0; slot < SiblingObserversIdentity.Slots; slot++)
        {
            int spawned = SiblingObserversIdentity.ClientSpawned[slot];
            int despawned = SiblingObserversIdentity.ClientDespawned[slot];
            int live = SiblingObserversIdentity.CountLive(slot);

            if (spawned != expectedSpawned || despawned != expectedDespawned || live != 1)
                failures.Add(
                    $"{phase}: component {slot} OnSpawned={spawned}, OnDespawned={despawned}, live={live}, " +
                    $"expected {expectedSpawned}/{expectedDespawned}/1");
        }
    }

    private static void ExpectBalanced(List<string> failures, int slot)
    {
        var added = new List<ulong>(SiblingObserversIdentity.ObserverAddedCalls[slot]);
        var removed = new List<ulong>(SiblingObserversIdentity.ObserverRemovedCalls[slot]);
        added.Sort();
        removed.Sort();

        bool balanced = added.Count == removed.Count;
        for (var i = 0; balanced && i < added.Count; i++)
            balanced = added[i] == removed[i];

        if (!balanced)
            failures.Add(
                $"destroyed: component {slot} OnObserverAdded [{string.Join(",", added)}] " +
                $"does not match OnObserverRemoved [{string.Join(",", removed)}]");
    }

    private static void ExpectDelivered(List<string> failures, string phase, int slot, int ping, int counter)
    {
        int gotPing = SiblingObserversIdentity.LastPing[slot];
        if (gotPing != ping)
            failures.Add($"{phase}: missed component {slot}'s ObserversRpc (last={gotPing}, expected {ping})");

        var instance = SiblingObserversIdentity.LocalInstances[slot];
        int gotCounter = instance ? instance.counter : -1;
        if (gotCounter != counter)
            failures.Add($"{phase}: component {slot}'s SyncVar is {gotCounter}, expected {counter}");
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
            _loggedErrors.Add(condition);
    }

    // Changing who observes a component is routine, no peer should log an error over it.
    private static ScenarioResult WithLoggedErrors(ScenarioResult result)
    {
        if (_loggedErrors.Count == 0)
            return result;

        int shown = Mathf.Min(_loggedErrors.Count, 3);
        var errors = $"{_loggedErrors.Count} error(s) logged: {string.Join(" || ", _loggedErrors.GetRange(0, shown))}";
        return ScenarioResult.Fail(result.success ? errors : $"{result.message} | {errors}");
    }

    private struct CallSnapshot
    {
        public int[] added;
        public int[] removed;
    }

    private static CallSnapshot Snapshot()
    {
        var snapshot = new CallSnapshot
        {
            added = new int[SiblingObserversIdentity.Slots],
            removed = new int[SiblingObserversIdentity.Slots]
        };

        for (var slot = 0; slot < SiblingObserversIdentity.Slots; slot++)
        {
            snapshot.added[slot] = SiblingObserversIdentity.ObserverAddedCalls[slot].Count;
            snapshot.removed[slot] = SiblingObserversIdentity.ObserverRemovedCalls[slot].Count;
        }

        return snapshot;
    }

    private static void ExpectCalls(List<string> failures, string phase, string what, List<ulong> calls, int since,
        int expected, ulong victimId)
    {
        int fresh = calls.Count - since;

        if (fresh != expected)
        {
            failures.Add(
                $"{phase}: {what} fired {fresh} time(s), expected {expected} [{string.Join(",", calls.GetRange(since, fresh))}]");
            return;
        }

        for (var i = since; i < calls.Count; i++)
        {
            if (calls[i] != victimId)
                failures.Add($"{phase}: {what} fired for bystander {calls[i]}");
        }
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

    // Phases are announced through a static RPC so they reach every peer no matter which
    // identities it can currently see.
    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhase(int phase, ulong victimId)
    {
        _victimId = victimId;
        _phase = phase;
    }
}

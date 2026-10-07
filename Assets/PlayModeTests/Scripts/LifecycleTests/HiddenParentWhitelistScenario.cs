using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// A child identity is never visible to a player who cannot see its parent. Whitelisting the
// child while the root is hidden must not hand the victim a child with no parent to sit under,
// and the hierarchy has to come back in one piece once the root is shown again.
public class HiddenParentWhitelistScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10220;
    private const int BarrierParentHidden = BarrierBase + 1;
    private const int BarrierChildWhitelisted = BarrierBase + 2;
    private const int BarrierParentRestored = BarrierBase + 3;

    private const int PhaseParentHidden = 1;
    private const int PhaseChildWhitelisted = 2;
    private const int PhaseParentRestored = 3;

    private const int Root = HiddenParentWhitelistIdentity.Root;
    private const int Child = HiddenParentWhitelistIdentity.Child;

    private static int _phase;
    private static ulong _victimId;
    private static readonly List<string> _loggedErrors = new();

    private HiddenParentWhitelistIdentity _prefab;

    void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(HiddenParentWhitelistScenario));
        _prefab = rootGo.AddComponent<HiddenParentWhitelistIdentity>();
        _prefab.slot = Root;

        var childGo = new GameObject("Child");
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<HiddenParentWhitelistIdentity>().slot = Child;

        rootGo.SetActive(false);
        HiddenParentWhitelistIdentity.ResetAll();
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
                () => HiddenParentWhitelistIdentity.LocalInstances[Root] != null
                      && HiddenParentWhitelistIdentity.LocalInstances[Child] != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"hierarchy spawn timeout: root={HiddenParentWhitelistIdentity.LocalInstances[Root] != null}, " +
                $"child={HiddenParentWhitelistIdentity.LocalInstances[Child] != null}");
        }

        if (ctx.isClient)
            HiddenParentWhitelistIdentity.LocalInstances[Root].SignalReady();

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
        var root = HiddenParentWhitelistIdentity.LocalInstances[Root];
        var child = HiddenParentWhitelistIdentity.LocalInstances[Child];

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HiddenParentWhitelistIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {HiddenParentWhitelistIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to blacklist");

        var victimId = victim.Value.id.value;

        // Hide the root. The child goes with it.
        var since = Snapshot();

        if (!root.BlacklistPlayer(victim.Value))
            failures.Add($"BlacklistPlayer({victimId}) returned false on server");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (root.IsObserver(victim.Value))
            failures.Add("parent hidden: root still lists the victim as observer");
        if (child.IsObserver(victim.Value))
            failures.Add("parent hidden: child still lists the victim as observer");

        ExpectCalls(failures, "parent hidden", "root OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Root], since.removed[Root], 1, victimId);
        ExpectCalls(failures, "parent hidden", "child OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Child], since.removed[Child], 1, victimId);
        ExpectCalls(failures, "parent hidden", "root OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Root], since.added[Root], 0, victimId);
        ExpectCalls(failures, "parent hidden", "child OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Child], since.added[Child], 0, victimId);

        BroadcastPhase(PhaseParentHidden, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierParentHidden, _barrierTimeoutSeconds);

        // Whitelist the child while the root stays hidden. The hidden parent wins: the child
        // must not become visible on its own.
        since = Snapshot();

        if (!child.WhitelistPlayer(victim.Value))
            failures.Add($"WhitelistPlayer({victimId}) returned false on server");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (root.IsObserver(victim.Value))
            failures.Add("child whitelisted: root lists the victim although it is still blacklisted");
        if (child.IsObserver(victim.Value))
            failures.Add("child whitelisted: child lists the victim as observer while its parent is hidden from them");

        ExpectCalls(failures, "child whitelisted", "root OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Root], since.added[Root], 0, victimId);
        ExpectCalls(failures, "child whitelisted", "child OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Child], since.added[Child], 0, victimId);
        ExpectCalls(failures, "child whitelisted", "root OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Root], since.removed[Root], 0, victimId);
        ExpectCalls(failures, "child whitelisted", "child OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Child], since.removed[Child], 0, victimId);

        BroadcastPhase(PhaseChildWhitelisted, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierChildWhitelisted, _barrierTimeoutSeconds);

        // Show the root again. Root and child come back together, once.
        since = Snapshot();

        if (!root.RemoveBlacklistPlayer(victim.Value))
            failures.Add($"RemoveBlacklistPlayer({victimId}) returned false on server");

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        if (!root.IsObserver(victim.Value))
            failures.Add("parent restored: root did not get the victim back as observer");
        if (!child.IsObserver(victim.Value))
            failures.Add("parent restored: child did not get the victim back as observer");

        ExpectCalls(failures, "parent restored", "root OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Root], since.added[Root], 1, victimId);
        ExpectCalls(failures, "parent restored", "child OnObserverAdded",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Child], since.added[Child], 1, victimId);
        ExpectCalls(failures, "parent restored", "root OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Root], since.removed[Root], 0, victimId);
        ExpectCalls(failures, "parent restored", "child OnObserverRemoved",
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Child], since.removed[Child], 0, victimId);

        BroadcastPhase(PhaseParentRestored, victimId);
        await ScenarioBarrier.Wait(ctx, BarrierParentRestored, _barrierTimeoutSeconds);

        if (root)
            Destroy(root.gameObject);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        VisibilityScenarioUtils.ExpectBalanced(failures, "root",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Root],
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Root]);
        VisibilityScenarioUtils.ExpectBalanced(failures, "child",
            HiddenParentWhitelistIdentity.ObserverAddedCalls[Child],
            HiddenParentWhitelistIdentity.ObserverRemovedCalls[Child]);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        await WaitForPhase(ctx, PhaseParentHidden, failures);
        bool isVictim = IsVictim(ctx);

        // Bystanders keep their one instance for the whole scenario.
        if (isVictim)
            ExpectLifetime(failures, "parent hidden", spawned: 1, despawned: 1, live: 0);
        else
            ExpectLifetime(failures, "parent hidden", spawned: 1, despawned: 0, live: 1);

        await ScenarioBarrier.Wait(ctx, BarrierParentHidden, _barrierTimeoutSeconds);

        await WaitForPhase(ctx, PhaseChildWhitelisted, failures);

        // Nothing may show up on the victim while the root is still hidden from them.
        if (isVictim)
            ExpectLifetime(failures, "child whitelisted", spawned: 1, despawned: 1, live: 0);
        else
            ExpectLifetime(failures, "child whitelisted", spawned: 1, despawned: 0, live: 1);

        await ScenarioBarrier.Wait(ctx, BarrierChildWhitelisted, _barrierTimeoutSeconds);

        await WaitForPhase(ctx, PhaseParentRestored, failures);

        if (isVictim)
            ExpectLifetime(failures, "parent restored", spawned: 2, despawned: 1, live: 1);
        else
            ExpectLifetime(failures, "parent restored", spawned: 1, despawned: 0, live: 1);

        var liveRoot = HiddenParentWhitelistIdentity.FirstLive(Root);
        var liveChild = HiddenParentWhitelistIdentity.FirstLive(Child);

        if (liveRoot && liveChild && liveChild.transform.parent != liveRoot.transform)
            failures.Add("parent restored: child is not parented to the root");

        if (HiddenParentWhitelistIdentity.OrphanChildSpawns != 0)
            failures.Add(
                $"child spawned {HiddenParentWhitelistIdentity.OrphanChildSpawns} time(s) without its root on this peer");

        await ScenarioBarrier.Wait(ctx, BarrierParentRestored, _barrierTimeoutSeconds);

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

    private static void ExpectLifetime(List<string> failures, string phase, int spawned, int despawned, int live)
    {
        for (var slot = 0; slot < HiddenParentWhitelistIdentity.Slots; slot++)
        {
            int gotSpawned = HiddenParentWhitelistIdentity.ClientSpawned[slot];
            int gotDespawned = HiddenParentWhitelistIdentity.ClientDespawned[slot];
            int gotLive = HiddenParentWhitelistIdentity.CountLive(slot);

            if (gotSpawned != spawned || gotDespawned != despawned || gotLive != live)
                failures.Add(
                    $"{phase}: {(slot == Root ? "root" : "child")} OnSpawned={gotSpawned}, OnDespawned={gotDespawned}, " +
                    $"live={gotLive}, expected {spawned}/{despawned}/{live}");
        }
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
            _loggedErrors.Add(condition);
    }

    // Changing who observes an identity is routine, no peer should log an error over it.
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
            added = new int[HiddenParentWhitelistIdentity.Slots],
            removed = new int[HiddenParentWhitelistIdentity.Slots]
        };

        for (var slot = 0; slot < HiddenParentWhitelistIdentity.Slots; slot++)
        {
            snapshot.added[slot] = HiddenParentWhitelistIdentity.ObserverAddedCalls[slot].Count;
            snapshot.removed[slot] = HiddenParentWhitelistIdentity.ObserverRemovedCalls[slot].Count;
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

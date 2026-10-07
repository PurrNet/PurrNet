using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// A container starts out hidden from one player: it is blacklisted in OnEarlySpawn, so that
// player never receives it, not even for a tick. Moving a visible item under that container
// takes the item away from the player as well, cleanly despawned instead of left behind as a
// stale copy, and moving it back out returns it.
public class ReparentVisibilityScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _phaseTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10260;

    private const int PhaseSpawned = 1;
    private const int PhaseParented = 2;
    private const int PhaseUnparented = 3;

    private const int Container = ReparentVisibilityIdentity.Container;
    private const int Item = ReparentVisibilityIdentity.Item;

    private static int _phase;
    private static ulong _victimId;

    private ReparentVisibilityIdentity _containerPrefab;
    private ReparentVisibilityIdentity _itemPrefab;

    private static ReparentVisibilityIdentity CreatePrefab(string suffix, int slot)
    {
        var go = new GameObject($"{nameof(ReparentVisibilityScenario)}-{suffix}");
        var identity = go.AddComponent<ReparentVisibilityIdentity>();
        identity.slot = slot;
        go.SetActive(false);
        return identity;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _containerPrefab = CreatePrefab("Container", Container);
        _itemPrefab = CreatePrefab("Item", Item);
        ReparentVisibilityIdentity.ResetAll();

        manager.prefabProvider.AddRuntimePrefab(_containerPrefab.name, _containerPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_itemPrefab.name, _itemPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _phase = 0;
        _victimId = 0;

        VisibilityScenarioUtils.BeginErrorCapture();
        ScenarioResult result;

        try
        {
            // Everyone has to be listening before the server announces the first phase.
            await ScenarioBarrier.Wait(ctx, BarrierBase, _barrierTimeoutSeconds);
            result = await RunSplit(ctx, RunAsClient, RunAsServer);
        }
        catch (Exception e)
        {
            result = ScenarioResult.Fail($"{e.GetType().Name}: {e.Message}");
        }

        return VisibilityScenarioUtils.EndErrorCapture(result);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var clients = VisibilityScenarioUtils.PickClients(ctx);

        if (clients.Count == 0)
            return ScenarioResult.Fail("no remote client to hide the container from");

        var victim = clients[0];
        _victimId = victim.id.value;

        ReparentVisibilityIdentity.HideContainerFrom = victim;
        Instantiate(_containerPrefab);
        Instantiate(_itemPrefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ReparentVisibilityIdentity.LocalInstances[Container] != null
                      && ReparentVisibilityIdentity.LocalInstances[Item] != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("the prefabs never spawned on the server");
        }

        var container = ReparentVisibilityIdentity.LocalInstances[Container];
        var item = ReparentVisibilityIdentity.LocalInstances[Item];
        var itemAdded = ReparentVisibilityIdentity.ObserverAddedCalls[Item];
        var itemRemoved = ReparentVisibilityIdentity.ObserverRemovedCalls[Item];

        if (!ctx.networkManager.GetModule<HierarchyFactory>(true).TryGetHierarchy(item.sceneId, out var hierarchy))
            return ScenarioResult.Fail("no server hierarchy for the item's scene");

        await Settle(ctx);

        if (container.IsObserver(victim))
            failures.Add("spawned: the container lists the victim although it was blacklisted in OnEarlySpawn");
        if (ReparentVisibilityIdentity.ObserverAddedCalls[Container].Contains(_victimId))
            failures.Add("spawned: the container fired OnObserverAdded for the victim before the blacklist applied");
        if (!item.IsObserver(victim))
            failures.Add("spawned: the item does not list the victim");

        await Announce(ctx, PhaseSpawned);

        // Under a parent the victim cannot see, the item is no longer theirs to see either.
        int addedSince = itemAdded.Count;
        int removedSince = itemRemoved.Count;
        item.transform.SetParent(container.transform);
        hierarchy.OnParentChanged(item, container.transform);
        await Settle(ctx);

        if (item.IsObserver(victim))
            failures.Add("parented: the item still lists the victim under a parent hidden from them");

        VisibilityScenarioUtils.ExpectCalls(failures, "parented", "item OnObserverRemoved", itemRemoved, removedSince, 1, _victimId);
        VisibilityScenarioUtils.ExpectCalls(failures, "parented", "item OnObserverAdded", itemAdded, addedSince, 0, _victimId);

        await Announce(ctx, PhaseParented);

        addedSince = itemAdded.Count;
        removedSince = itemRemoved.Count;
        item.transform.SetParent(null);
        hierarchy.OnParentChanged(item, null);
        await Settle(ctx);

        if (!item.IsObserver(victim))
            failures.Add("unparented: the item did not get the victim back once it left the hidden parent");

        VisibilityScenarioUtils.ExpectCalls(failures, "unparented", "item OnObserverAdded", itemAdded, addedSince, 1, _victimId);
        VisibilityScenarioUtils.ExpectCalls(failures, "unparented", "item OnObserverRemoved", itemRemoved, removedSince, 0, _victimId);

        await Announce(ctx, PhaseUnparented);

        Destroy(item.gameObject);
        Destroy(container.gameObject);
        await Settle(ctx);

        VisibilityScenarioUtils.ExpectBalanced(failures, "item", itemAdded, itemRemoved);
        VisibilityScenarioUtils.ExpectBalanced(failures, "container",
            ReparentVisibilityIdentity.ObserverAddedCalls[Container],
            ReparentVisibilityIdentity.ObserverRemovedCalls[Container]);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={_victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        for (var phase = PhaseSpawned; phase <= PhaseUnparented; phase++)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(() => _phase >= phase, _phaseTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"phase {phase} was never announced (at {_phase})");
            }

            // The host shares the server's instances, nothing is spawned or despawned for it.
            if (!ctx.isServer)
                ExpectPhase(ctx, failures, phase);

            await ScenarioBarrier.Wait(ctx, BarrierBase + phase, _barrierTimeoutSeconds);
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void ExpectPhase(ScenarioContext ctx, List<string> failures, int phase)
    {
        if (VisibilityScenarioUtils.IsLocalPlayer(ctx, _victimId))
        {
            ExpectLifetime(failures, phase, "victim", Container, 0, 0);
            ExpectLifetime(failures, phase, "victim", Item, phase == PhaseUnparented ? 2 : 1, phase == PhaseSpawned ? 0 : 1);
            return;
        }

        ExpectLifetime(failures, phase, "bystander", Container, 1, 0);
        ExpectLifetime(failures, phase, "bystander", Item, 1, 0);

        var container = ReparentVisibilityIdentity.LocalInstances[Container];
        var item = ReparentVisibilityIdentity.LocalInstances[Item];

        if (!container || !item)
            return;

        var expectedParent = phase == PhaseParented ? container.transform : null;
        if (item.transform.parent != expectedParent)
            failures.Add($"phase {phase} (bystander): item parent is '{item.transform.parent}', expected '{expectedParent}'");
    }

    private static void ExpectLifetime(List<string> failures, int phase, string role, int slot, int spawned, int despawned)
    {
        int gotSpawned = ReparentVisibilityIdentity.ClientSpawned[slot];
        int gotDespawned = ReparentVisibilityIdentity.ClientDespawned[slot];

        if (gotSpawned != spawned || gotDespawned != despawned)
            failures.Add(
                $"phase {phase} ({role}): {(slot == Container ? "container" : "item")} OnSpawned={gotSpawned}, " +
                $"OnDespawned={gotDespawned}, expected {spawned}/{despawned}");
    }

    private UniTask Settle(ScenarioContext ctx)
    {
        return UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
    }

    private async UniTask Announce(ScenarioContext ctx, int phase)
    {
        BroadcastPhase(phase, _victimId);
        await ScenarioBarrier.Wait(ctx, BarrierBase + phase, _barrierTimeoutSeconds);
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

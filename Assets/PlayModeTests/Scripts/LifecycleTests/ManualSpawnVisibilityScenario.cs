using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// A manually spawned identity gets its observers from whoever spawned it, through
// ManualAddObserver. Visibility evaluation must leave it alone: blacklisting a player on it or
// calling EvaluateVisibility must not strip that player nor send them a regular despawn.
public class ManualSpawnVisibilityScenario : Scenario
{
    [SerializeField] private float _phaseTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10300;

    private const int PhaseSpawned = 1;
    private const int PhaseEvaluated = 2;

    private const int PingValue = 7;

    private static int _phase;
    private static ulong _networkId;
    private static ulong _victimId;

    private ManualSpawnVisibilityIdentity _identity;

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _phase = 0;
        _networkId = 0;
        _victimId = 0;
        ManualSpawnVisibilityIdentity.ResetAll();

        _identity = new GameObject(nameof(ManualSpawnVisibilityScenario)).AddComponent<ManualSpawnVisibilityIdentity>();

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

        result = VisibilityScenarioUtils.EndErrorCapture(result);

        if (_identity)
        {
            if (_identity.isSpawned && TryGetHierarchy(ctx, ctx.isServer, out var hierarchy))
                hierarchy.ManualDespawn(_identity);
            Destroy(_identity.gameObject);
        }

        return result;
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var clients = VisibilityScenarioUtils.PickClients(ctx);

        if (clients.Count == 0)
            return ScenarioResult.Fail("no remote client to blacklist");

        var victim = clients[0];
        _victimId = victim.id.value;

        if (!TryGetHierarchy(ctx, true, out var hierarchy))
            return ScenarioResult.Fail("no server hierarchy for the scenario's scene");

        var id = hierarchy.ReserveNetworkID();
        _networkId = id.id.value;

        hierarchy.ManualEarlySpawn(_identity, id);
        hierarchy.ManualFinalizeSpawn(_identity);

        // Every client spawns its own copy under the same id before anyone is added as observer.
        BroadcastPhase(PhaseSpawned, _networkId, _victimId);
        await ScenarioBarrier.Wait(ctx, BarrierBase + PhaseSpawned, _barrierTimeoutSeconds);

        var players = ctx.networkManager.players;
        for (var i = 0; i < players.Count; i++)
            hierarchy.ManualAddObserver(_identity, players[i]);

        await Settle(ctx);

        int observers = _identity.observers.Count;

        _identity.BlacklistPlayer(victim);
        _identity.EvaluateVisibility();
        await Settle(ctx);

        if (!_identity.IsObserver(victim))
            failures.Add("the blacklist removed an observer from a manually spawned identity");
        if (_identity.observers.Count != observers)
            failures.Add($"evaluating visibility changed the observers of a manually spawned identity: {observers} -> {_identity.observers.Count}");
        if (ManualSpawnVisibilityIdentity.ObserverRemovedCalls.Count != 0)
            failures.Add(
                $"OnObserverRemoved fired for [{string.Join(",", ManualSpawnVisibilityIdentity.ObserverRemovedCalls)}] on a manually spawned identity");

        _identity.Ping(PingValue);
        await Settle(ctx);

        BroadcastPhase(PhaseEvaluated, _networkId, _victimId);
        await ScenarioBarrier.Wait(ctx, BarrierBase + PhaseEvaluated, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok($"Victim={_victimId}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        await WaitForPhase(ctx, PhaseSpawned, failures);

        // The host's client side shares the identity the server half just spawned.
        if (!ctx.isServer && _phase >= PhaseSpawned)
        {
            if (TryGetHierarchy(ctx, false, out var hierarchy))
            {
                hierarchy.ManualEarlySpawn(_identity, new NetworkID(_networkId));
                hierarchy.ManualFinalizeSpawn(_identity);
            }
            else failures.Add("no client hierarchy for the scenario's scene");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + PhaseSpawned, _barrierTimeoutSeconds);

        await WaitForPhase(ctx, PhaseEvaluated, failures);

        string role = VisibilityScenarioUtils.IsLocalPlayer(ctx, _victimId) ? "victim" : "bystander";

        if (!_identity || !_identity.isSpawned)
            failures.Add($"{role}: the manually spawned identity is no longer spawned here");
        if (ManualSpawnVisibilityIdentity.ClientDespawned != 0)
            failures.Add($"{role}: the manually spawned identity was despawned {ManualSpawnVisibilityIdentity.ClientDespawned} time(s)");
        if (ManualSpawnVisibilityIdentity.LastPing != PingValue)
            failures.Add($"{role}: missed the ObserversRpc sent after the evaluation (last={ManualSpawnVisibilityIdentity.LastPing})");

        await ScenarioBarrier.Wait(ctx, BarrierBase + PhaseEvaluated, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok(role)
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private bool TryGetHierarchy(ScenarioContext ctx, bool asServer, out HierarchyV2 hierarchy)
    {
        hierarchy = null;
        return ctx.networkManager.TryGetModule<HierarchyFactory>(asServer, out var factory) &&
               factory.TryGetHierarchy(_identity.gameObject.scene, out hierarchy);
    }

    private async UniTask WaitForPhase(ScenarioContext ctx, int phase, List<string> failures)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phase >= phase, _phaseTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"phase {phase} was never announced (at {_phase})");
        }
    }

    private UniTask Settle(ScenarioContext ctx)
    {
        return UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhase(int phase, ulong networkId, ulong victimId)
    {
        _networkId = networkId;
        _victimId = victimId;
        _phase = phase;
    }
}

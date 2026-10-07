using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// A visibility override belongs to the prefab, so a pooled instance must still have it on its
// next life, and a rule set swapped in after the spawn has to work like one assigned before it.
// Also covers adding a rule to a NetworkManager that has no rule set: an error, not an exception.
public class OverrideLifetimeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _phaseTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierBase = 10240;

    private const int PhaseHidden = 1;
    private const int PhaseSecondLife = 2;
    private const int PhaseRulesSwapped = 3;

    private const string MissingRuleSetError = "Can't add a visibility rule";

    private static int _phase;

    private OverrideLifetimeIdentity _prefab;
    private NetworkVisibilityRuleSet _alwaysVisible;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(OverrideLifetimeScenario));
        _prefab = go.AddComponent<OverrideLifetimeIdentity>();

        // A rule set without rules lets nobody in.
        _prefab.SetVisibilityRules(ScriptableObject.CreateInstance<NetworkVisibilityRuleSet>());
        go.SetActive(false);
        OverrideLifetimeIdentity.ResetAll();

        // Built like an authored asset: the rules sit in the serialized list and only become
        // active once the rule set is set up.
        _alwaysVisible = ScriptableObject.CreateInstance<NetworkVisibilityRuleSet>();
        typeof(NetworkVisibilityRuleSet)
            .GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(_alwaysVisible, new NetworkVisibilityRule[] { ScriptableObject.CreateInstance<AlwaysVisibleRule>() });
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject, true, 1);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        _phase = 0;

        VisibilityScenarioUtils.BeginErrorCapture(MissingRuleSetError);
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

        ExpectMissingRuleSetError(ctx, failures);

        var instance = await Spawn(ctx);
        if (!instance)
            return ScenarioResult.Fail("the prefab never spawned on the server");

        if (instance.observers.Count != 0)
            failures.Add($"hidden: {instance.observers.Count} observer(s) although the rule set lets nobody in");

        await Announce(ctx, PhaseHidden);

        // The pool hands the same instance back. Its override must have survived the reset.
        Destroy(instance.gameObject);
        await Settle(ctx);

        instance = await Spawn(ctx);
        if (!instance)
            return ScenarioResult.Fail("the prefab never spawned a second time on the server");

        if (instance.observers.Count != 0)
            failures.Add($"second life: {instance.observers.Count} observer(s), the pooled instance lost its visibility override");

        await Announce(ctx, PhaseSecondLife);

        instance.SetVisibilityRules(_alwaysVisible);
        instance.EvaluateVisibility();
        await Settle(ctx);

        if (instance.observers.Count != ctx.expectedConnections)
            failures.Add(
                $"rules swapped: {instance.observers.Count} observer(s), expected all {ctx.expectedConnections} " +
                "after assigning an always visible rule set");

        await Announce(ctx, PhaseRulesSwapped);

        Destroy(instance.gameObject);
        await Settle(ctx);

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void ExpectMissingRuleSetError(ScenarioContext ctx, List<string> failures)
    {
        var manager = ctx.networkManager;

        if (manager.visibilityRules)
            return;

        var rule = ScriptableObject.CreateInstance<AlwaysVisibleRule>();

        try
        {
            manager.AddVisibilityRule(manager, rule);
            manager.RemoveVisibilityRule(rule);
        }
        catch (Exception e)
        {
            failures.Add($"adding a visibility rule without a rule set threw {e.GetType().Name}");
        }

        if (!VisibilityScenarioUtils.sawExpectedError)
            failures.Add("adding a visibility rule without a rule set did not log an error");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        for (var phase = PhaseHidden; phase <= PhaseRulesSwapped; phase++)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(() => _phase >= phase, _phaseTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"phase {phase} was never announced (at {_phase})");
            }

            // The host shares the server's instance, nothing is spawned or despawned for it.
            if (!ctx.isServer)
            {
                int expected = phase == PhaseRulesSwapped ? 1 : 0;

                if (OverrideLifetimeIdentity.ClientSpawned != expected || OverrideLifetimeIdentity.ClientDespawned != 0)
                    failures.Add(
                        $"phase {phase}: OnSpawned={OverrideLifetimeIdentity.ClientSpawned}, " +
                        $"OnDespawned={OverrideLifetimeIdentity.ClientDespawned}, expected {expected}/0");
            }

            await ScenarioBarrier.Wait(ctx, BarrierBase + phase, _barrierTimeoutSeconds);
        }

        return failures.Count == 0
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<OverrideLifetimeIdentity> Spawn(ScenarioContext ctx)
    {
        Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => OverrideLifetimeIdentity.ServerInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }

        await Settle(ctx);

        // Depending on the active network rules a fresh object may already belong to its spawner,
        // and an owner sees what it owns. Changing ownership does not re-evaluate visibility.
        var instance = OverrideLifetimeIdentity.ServerInstance;
        if (instance && instance.owner.HasValue)
        {
            instance.RemoveOwnership();
            instance.EvaluateVisibility();
            await Settle(ctx);
        }

        return instance;
    }

    private UniTask Settle(ScenarioContext ctx)
    {
        return UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
    }

    private async UniTask Announce(ScenarioContext ctx, int phase)
    {
        BroadcastPhase(phase);
        await ScenarioBarrier.Wait(ctx, BarrierBase + phase, _barrierTimeoutSeconds);
    }

    // Phases are announced through a static RPC so they reach every peer no matter which
    // identities it can currently see.
    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhase(int phase)
    {
        _phase = phase;
    }
}

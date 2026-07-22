using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Core network LOD contract: distance bands resolve per-player tiers (owned identities as
/// fallback, registered anchors taking precedence), hysteresis holds at band edges, tier changes
/// fire the virtual + event, and ShouldSendToPlayer gates at exactly the tier's interval.
/// </summary>
public class NetworkLODTierScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _tierTimeoutSeconds = 20f;
    [SerializeField] private float _cadenceTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 120f;
    [SerializeField] private float _settleSeconds = 0.75f;

    private const int BarrierBase = 5800;
    private const int CadenceTicks = 40;

    private static readonly Vector3 BasePos = new Vector3(1000f, 0f, 0f);

    private NetworkLODTierTarget _prefabTarget;
    private NetworkLODOwnedAnchor _prefabOwned;

    void CreatePrefabs()
    {
        var targetGo = new GameObject(nameof(NetworkLODTierTarget));
        _prefabTarget = targetGo.AddComponent<NetworkLODTierTarget>();
        var lod = targetGo.AddComponent<NetworkLOD>();

        var profile = ScriptableObject.CreateInstance<NetworkLODProfile>();
        profile.Configure(new[]
        {
            new NetworkLODTier { maxDistance = 10f, hysteresis = 5f, sendIntervalTicks = 1 },
            new NetworkLODTier { maxDistance = 30f, hysteresis = 5f, sendIntervalTicks = 2 },
            new NetworkLODTier { maxDistance = 100f, hysteresis = 10f, sendIntervalTicks = 4 }
        }, false);
        lod.profile = profile;

        targetGo.SetActive(false);

        var ownedGo = new GameObject(nameof(NetworkLODOwnedAnchor));
        _prefabOwned = ownedGo.AddComponent<NetworkLODOwnedAnchor>();
        ownedGo.SetActive(false);

        NetworkLODTierTarget.ResetAll();
        NetworkLODOwnedAnchor.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefabs();
        manager.prefabProvider.AddRuntimePrefab(_prefabTarget.name, _prefabTarget.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_prefabOwned.name, _prefabOwned.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        NetworkLODTierTarget instance = null;

        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { instance = Instantiate(_prefabTarget, BasePos, Quaternion.identity); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkLODTierTarget.localInstance != null,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("target spawn never observed");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);

        ScenarioResult serverResult = default;
        bool serverRan = false;

        if (ctx.isServer)
        {
            serverResult = await RunServerPhases(ctx, instance);
            serverRan = true;
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);

        if (ctx.isServer && instance)
            instance.Despawn();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NetworkLODTierTarget.localInstance == null && NetworkLODOwnedAnchor.localInstance == null,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"cleanup incomplete: target={NetworkLODTierTarget.localInstance != null}, " +
                $"owned={NetworkLODOwnedAnchor.localInstance != null}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 3, _barrierTimeoutSeconds);

        if (serverRan && !serverResult.success)
            return serverResult;

        return ScenarioResult.Ok(ctx.isServer ? serverResult.message : "observer");
    }

    private async UniTask<ScenarioResult> RunServerPhases(ScenarioContext ctx, NetworkLODTierTarget instance)
    {
        var failures = new List<string>();
        var manager = ctx.networkManager;
        var lod = instance.GetComponent<NetworkLOD>();

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host-local player");

        var lodFactory = manager.lodModule;
        if (lodFactory == null)
            return ScenarioResult.Fail("NetworkLODFactory module not found");

        NetworkLODOwnedAnchor ownedInstance = null;
        GameObject anchorGo = null;

        try
        {
            /* settle first: leftovers owned by the victim can add a pre-phase transition */
            await UniTask.Delay(TimeSpan.FromSeconds(_settleSeconds), cancellationToken: ctx.cancellationToken);
            int baselineVirtual = NetworkLODTierTarget.GetVirtualCount(victim.Value);
            int baselineEvent = NetworkLODTierTarget.GetEventCount(victim.Value);

            ownedInstance = Instantiate(_prefabOwned, BasePos + new Vector3(20f, 0f, 0f), Quaternion.identity);
            ownedInstance.GiveOwnership(victim.Value);

            if (!await WaitTier(ctx, lod, victim.Value, 1, failures, "owned-identity fallback"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            anchorGo = new GameObject("NetworkLODTierScenarioAnchor");
            anchorGo.transform.position = BasePos + new Vector3(5f, 0f, 0f);
            lodFactory.RegisterAnchor(victim.Value, anchorGo.transform);

            if (!await WaitTier(ctx, lod, victim.Value, 0, failures, "anchor precedence"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            instance.transform.position = BasePos + new Vector3(0f, 0f, 50f);

            if (!await WaitTier(ctx, lod, victim.Value, 2, failures, "band move"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            int virtualCount = NetworkLODTierTarget.GetVirtualCount(victim.Value) - baselineVirtual;
            int eventCount = NetworkLODTierTarget.GetEventCount(victim.Value) - baselineEvent;
            var lastChange = NetworkLODTierTarget.GetLastChange(victim.Value);

            if (virtualCount != 3)
                failures.Add($"expected 3 virtual tier changes for victim, got {virtualCount}");
            if (eventCount != 3)
                failures.Add($"expected 3 event tier changes for victim, got {eventCount}");
            if (lastChange != (0, 2))
                failures.Add($"last change mismatch: {lastChange?.previous}->{lastChange?.next}, expected 0->2");

            /* distances below are measured from the anchor at x=5 */
            instance.transform.position = BasePos + new Vector3(37f, 0f, 0f);
            await UniTask.Delay(TimeSpan.FromSeconds(_settleSeconds), cancellationToken: ctx.cancellationToken);
            if (lod.GetTier(victim.Value) != 2)
                failures.Add($"hysteresis: dist 32 demoted early to tier {lod.GetTier(victim.Value)}, expected 2");

            instance.transform.position = BasePos + new Vector3(33f, 0f, 0f);
            if (!await WaitTier(ctx, lod, victim.Value, 1, failures, "promotion to tier 1"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            instance.transform.position = BasePos + new Vector3(38f, 0f, 0f);
            await UniTask.Delay(TimeSpan.FromSeconds(_settleSeconds), cancellationToken: ctx.cancellationToken);
            if (lod.GetTier(victim.Value) != 1)
                failures.Add($"hysteresis: dist 33 flapped to tier {lod.GetTier(victim.Value)}, expected 1");

            instance.transform.position = BasePos + new Vector3(45f, 0f, 0f);
            if (!await WaitTier(ctx, lod, victim.Value, 2, failures, "demotion to tier 2"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            int sends = await CountSends(ctx, lod, victim.Value);
            if (sends != CadenceTicks / 4)
                failures.Add($"tier 2 cadence: {sends}/{CadenceTicks} ticks sent, expected {CadenceTicks / 4}");

            instance.transform.position = BasePos + new Vector3(33f, 0f, 0f);
            if (!await WaitTier(ctx, lod, victim.Value, 1, failures, "cadence re-promotion"))
                return ScenarioResult.Fail(string.Join(" | ", failures));

            sends = await CountSends(ctx, lod, victim.Value);
            if (sends != CadenceTicks / 2)
                failures.Add($"tier 1 cadence: {sends}/{CadenceTicks} ticks sent, expected {CadenceTicks / 2}");

            await RunPlainTargetPhases(ctx, lodFactory, instance.sceneId, victim.Value, failures);
        }
        finally
        {
            if (anchorGo)
            {
                lodFactory.UnregisterAnchor(victim.Value, anchorGo.transform);
                Destroy(anchorGo);
            }

            if (ownedInstance)
                ownedInstance.Despawn();
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"victim={victim.Value.id.value}, tiers/hysteresis/cadence verified")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask RunPlainTargetPhases(ScenarioContext ctx, NetworkLODFactory factory, SceneID scene,
        PlayerID player, List<string> failures)
    {
        if (!factory.TryGetModule(scene, out var module))
        {
            failures.Add("plain target: scene LOD module not found");
            return;
        }

        var profile = ScriptableObject.CreateInstance<NetworkLODProfile>();
        profile.Configure(new[]
        {
            new NetworkLODTier { maxDistance = 10f, hysteresis = 2f, sendIntervalTicks = 1 },
            new NetworkLODTier { maxDistance = 30f, hysteresis = 5f, sendIntervalTicks = 4 }
        }, true);

        var targetGo = new GameObject(nameof(PlainLODTarget));
        var target = targetGo.AddComponent<PlainLODTarget>();
        target.staggerSeed = 3;

        try
        {
            target.transform.position = BasePos + new Vector3(55f, 0f, 0f);
            if (!module.Register(target, profile))
            {
                failures.Add("plain target: direct registration failed");
                return;
            }

            if (!await WaitTier(ctx, module, target, player, NetworkLODProfile.CulledTier, failures,
                    "plain target distance cull"))
                return;

            target.transform.position = BasePos + new Vector3(33f, 0f, 0f);
            if (!await WaitTier(ctx, module, target, player, 1, failures, "plain target reentry"))
                return;

            int changesAfterReentry = target.GetChangeCount(player);
            target.transform.position = BasePos + new Vector3(38f, 0f, 0f);
            await UniTask.Delay(TimeSpan.FromSeconds(_settleSeconds), cancellationToken: ctx.cancellationToken);

            if (module.GetTier(target, player) != 1)
                failures.Add($"plain target hysteresis: tier {module.GetTier(target, player)}, expected 1");
            if (target.GetChangeCount(player) != changesAfterReentry)
                failures.Add("plain target hysteresis emitted a tier change inside the hold band");

            int sends = 0;
            int firstSendTick = -1;
            for (uint tick = 0; tick < 16; tick++)
            {
                if (!LODIntervalScheduler.instance.ShouldSendThisTick(target, profile, player, 1, tick))
                    continue;

                if (firstSendTick < 0)
                    firstSendTick = (int)tick;
                sends++;
            }

            if (sends != 4 || firstSendTick != 1)
                failures.Add($"plain target stagger: sends={sends}, first={firstSendTick}, expected 4 and 1");

            target.transform.position = BasePos + new Vector3(42f, 0f, 0f);
            if (!await WaitTier(ctx, module, target, player, NetworkLODProfile.CulledTier, failures,
                    "plain target hysteretic recull"))
                return;

            if (target.GetAppliedTier(player) != NetworkLODProfile.CulledTier)
                failures.Add($"plain target callback tier {target.GetAppliedTier(player)}, expected culled");
            if (target.GetChangeCount(player) != changesAfterReentry + 1)
                failures.Add($"plain target callback count {target.GetChangeCount(player)}, expected {changesAfterReentry + 1}");
        }
        finally
        {
            module.Unregister(target);
            Destroy(targetGo);
            Destroy(profile);
        }
    }

    private async UniTask<bool> WaitTier(ScenarioContext ctx, NetworkLOD lod, PlayerID player, byte expected,
        List<string> failures, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => lod.GetTier(player) == expected,
                _tierTimeoutSeconds, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            failures.Add($"{phase}: tier stuck at {lod.GetTier(player)}, expected {expected}");
            return false;
        }
    }

    private async UniTask<bool> WaitTier(ScenarioContext ctx, NetworkLODModule module, ILODTarget target,
        PlayerID player, byte expected, List<string> failures, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => module.GetTier(target, player) == expected,
                _tierTimeoutSeconds, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            failures.Add($"{phase}: tier stuck at {module.GetTier(target, player)}, expected {expected}");
            return false;
        }
    }

    private async UniTask<int> CountSends(ScenarioContext ctx, NetworkLOD lod, PlayerID player)
    {
        int ticks = 0;
        int sends = 0;
        var tickManager = ctx.networkManager.tickModule;

        void OnTick()
        {
            if (ticks >= CadenceTicks)
                return;
            if (lod.ShouldSendToPlayer(player))
                sends++;
            ticks++;
        }

        tickManager.onTick += OnTick;
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ticks >= CadenceTicks,
                _cadenceTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            sends = -1;
        }
        finally
        {
            tickManager.onTick -= OnTick;
        }

        return sends;
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

public sealed class PlainLODTarget : MonoBehaviour, ILODTarget
{
    private readonly Dictionary<PlayerID, byte> _tiers = new Dictionary<PlayerID, byte>();
    private readonly Dictionary<PlayerID, int> _changeCounts = new Dictionary<PlayerID, int>();

    public Vector3 position => transform.position;

    public uint staggerSeed { get; set; }

    public void ApplyTier(PlayerID player, byte tier)
    {
        _tiers[player] = tier;
        _changeCounts[player] = GetChangeCount(player) + 1;
    }

    public byte GetAppliedTier(PlayerID player)
    {
        return _tiers.GetValueOrDefault(player, (byte)0);
    }

    public int GetChangeCount(PlayerID player)
    {
        return _changeCounts.GetValueOrDefault(player, 0);
    }
}

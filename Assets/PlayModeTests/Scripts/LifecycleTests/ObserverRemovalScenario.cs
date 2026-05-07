using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class ObserverRemovalScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _removalTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierEnd = 4300;

    private ObserverRemovalIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(ObserverRemovalScenario));
        _prefab = go.AddComponent<ObserverRemovalIdentity>();
        go.SetActive(false);
        ObserverRemovalIdentity.ResetAll();
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
                () => ObserverRemovalIdentity.LocalInstance != null,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("initial spawn never reached this peer");
        }

        if (ctx.isClient)
            ObserverRemovalIdentity.LocalInstance.SignalReady();

        if (ctx.isServer)
            return await RunAsServer(ctx);

        return await RunAsClient(ctx);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = ObserverRemovalIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ObserverRemovalIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {ObserverRemovalIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var victim = PickVictim(ctx);
        if (!victim.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to blacklist");

        var victimId = victim.Value.id.value;
        inst.BroadcastVictim(victimId);

        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        // Blacklist the victim. Server-side this strips the victim from observers; the victim
        // sees the identity despawn locally (observer-driven despawn).
        if (!inst.BlacklistPlayer(victim.Value))
            failures.Add($"BlacklistPlayer({victimId}) returned false on server");

        // Server should fire OnObserverRemoved(victim).
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ObserverRemovalIdentity.ObserverRemovedCalls.Contains(victimId),
                _removalTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-side OnObserverRemoved({victimId}) did not fire within {_removalTimeoutSeconds}s");
        }

        // Server-side identity must NOT despawn (only the victim's view despawns).
        if (ObserverRemovalIdentity.LocalInstance == null)
            failures.Add("server-side LocalInstance went null after blacklist — identity despawned unexpectedly");
        if (ObserverRemovalIdentity.OnDespawnedServer != 0)
            failures.Add(
                $"server expected 0 OnDespawned(asServer:true), got {ObserverRemovalIdentity.OnDespawnedServer}");

        // The barrier alone synchronizes the end of the scenario. We can't use a follow-up
        // ObserversRpc here because the victim has been stripped from observers and would never
        // receive it; and the victim's local instance has despawned so it can't reach back via
        // a ServerRpc on this identity either.
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
                () => ObserverRemovalIdentity.VictimIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastVictim");
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        var victimId = ObserverRemovalIdentity.VictimPlayerId;
        bool isVictim = ctx.networkManager.isLocalPlayerReady
                        && ctx.networkManager.localPlayer.id.value == victimId;

        if (isVictim)
        {
            // Victim must observe a local despawn (asServer=false on a pure client; both on host
            // bystander cases don't apply here since the victim is always a non-host client).
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ObserverRemovalIdentity.OnDespawnedClient >= 1
                          && ObserverRemovalIdentity.OnDespawnedNoArg >= 1,
                    _removalTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"victim despawn timeout: noArg={ObserverRemovalIdentity.OnDespawnedNoArg}, " +
                    $"asClient={ObserverRemovalIdentity.OnDespawnedClient}");
            }
        }
        else
        {
            // Bystander: identity stays alive locally, no local despawn fires. We don't expect
            // OnObserverRemoved to fire on a bystander client — server's blacklist only affects
            // the victim's observer relationship.
            if (ObserverRemovalIdentity.OnDespawnedClient != 0)
                failures.Add(
                    $"bystander expected 0 OnDespawned(asServer:false), got {ObserverRemovalIdentity.OnDespawnedClient}");
        }

        // Bystander's LocalInstance must still be alive; victim's must be null post-despawn.
        if (!isVictim && ObserverRemovalIdentity.LocalInstance == null)
            failures.Add("bystander's LocalInstance is null but should still be alive");
        if (isVictim && ObserverRemovalIdentity.LocalInstance != null)
            failures.Add("victim's LocalInstance should be null after blacklist despawn");

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim" : "bystander")
            : ScenarioResult.Fail(string.Join(" | ", failures));
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

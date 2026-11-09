using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

// A client whose NetworkRules keep spawned objects on disconnect (cleanupSpawnedObjects = false) leaves and
// rejoins. Its runtime-spawned objects stay behind, but authored scene objects must still despawn on disconnect
// and spawn fresh on reconnect, otherwise they keep the old session's state and never see OnSpawned again.
public class KeepSpawnedObjectsReconnectScenario : Scenario
{
    private const int BarrierBase = 10320;
    private const string PrefabName = "KeepSpawnedReconnect_Kept";

    [SerializeField] private float _timeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _victimReturned;
    private static bool _phaseDone;

    private GameObject _prefab;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _victimId = 0;
        _victimReceived = false;
        _victimReturned = false;
        _phaseDone = false;
        KeepSpawnedReconnectIdentity.ResetAll();

        _prefab = new GameObject(PrefabName);
        _prefab.SetActive(false);
        _prefab.AddComponent<KeepSpawnedReconnectIdentity>();
        manager.prefabProvider.AddRuntimePrefab(PrefabName, _prefab, false, 0);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        await ScenarioBarrier.Wait(ctx, BarrierBase, _barrierTimeoutSeconds);
        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        PlayerID? victim = null;
        foreach (var player in manager.players)
        {
            if (player.isServer || (ctx.role == NetworkRole.Host && player == manager.localPlayer))
                continue;
            if (!victim.HasValue || player.id.value < victim.Value.id.value)
                victim = player;
        }

        if (!victim.HasValue)
            return ScenarioResult.Fail("no external client to reconnect");

        var kept = UnityEngine.Object.Instantiate(_prefab);
        kept.SetActive(true);
        BroadcastVictim(victim.Value.id.value);

        var failures = new List<string>();
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _victimReturned, _timeoutSeconds * 2, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("victim never reported back after reconnecting");
        }

        if (kept)
            UnityEngine.Object.Destroy(kept);
        BroadcastPhaseDone();

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        return failures.Count == 0 ? ScenarioResult.Ok($"victim={victim.Value.id.value}") : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _victimReceived, _timeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("victim id never arrived");
        }

        if (manager.localPlayer.id.value == _victimId)
            await RunVictim(ctx, failures);

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _phaseDone, _timeoutSeconds * 3, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("phase done never arrived");
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        return failures.Count == 0 ? ScenarioResult.Ok() : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask RunVictim(ScenarioContext ctx, List<string> failures)
    {
        var manager = ctx.networkManager;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AuthoredSceneReconnectRoot.ClientAliveCount == 1 && SpawnedKeptCount() == 1,
                _timeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"initial state never reached: {Describe()}");
            SignalVictimReturned();
            return;
        }

        int spawnsBefore = AuthoredSceneReconnectRoot.ClientSpawnCount;
        var rules = manager.networkRules;
        bool previous = SetCleanupSpawnedObjects(rules, false);

        try
        {
            manager.StopClient();
            await UniTaskUtils.WaitWithTimeout(() => manager.clientState == ConnectionState.Disconnected,
                _timeoutSeconds, ctx.cancellationToken);
            await UniTask.WaitForSeconds(_stayDisconnectedSeconds, cancellationToken: ctx.cancellationToken);

            if (AuthoredSceneReconnectRoot.ClientAliveCount != 0)
                failures.Add($"authored scene object still spawned after disconnect: {Describe()}");
            if (KeepSpawnedReconnectIdentity.Alive.Count == 0)
                failures.Add("runtime-spawned object was destroyed on disconnect although cleanupSpawnedObjects is off");

            foreach (var kept in new List<KeepSpawnedReconnectIdentity>(KeepSpawnedReconnectIdentity.Alive))
            {
                if (kept)
                    UnityEngine.Object.Destroy(kept.gameObject);
            }

            manager.StartClient();
            await UniTaskUtils.WaitWithTimeout(() => manager.isClient && manager.isLocalPlayerReady,
                _timeoutSeconds, ctx.cancellationToken);
            await UniTaskUtils.WaitWithTimeout(
                () => AuthoredSceneReconnectRoot.ClientAliveCount == 1 &&
                      AuthoredSceneReconnectRoot.ClientSpawnCount > spawnsBefore,
                _timeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"authored scene object did not spawn again after reconnecting: {Describe()}");
        }
        finally
        {
            SetCleanupSpawnedObjects(rules, previous);
        }

        SignalVictimReturned();
    }

    private static int SpawnedKeptCount()
    {
        int count = 0;
        foreach (var kept in KeepSpawnedReconnectIdentity.Alive)
        {
            if (kept && kept.isSpawned)
                count++;
        }
        return count;
    }

    private static string Describe() =>
        $"clientRoots={AuthoredSceneReconnectRoot.ClientAliveCount}, clientSpawns={AuthoredSceneReconnectRoot.ClientSpawnCount}, " +
        $"kept={KeepSpawnedReconnectIdentity.Alive.Count}, spawnedKept={SpawnedKeptCount()}";

    private static bool SetCleanupSpawnedObjects(NetworkRules rules, bool value)
    {
        var field = typeof(NetworkRules).GetField("_defaultSpawnRules", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var spawnRules = (SpawnRules)field.GetValue(rules);
        bool previous = spawnRules.cleanupSpawnedObjects;
        spawnRules.cleanupSpawnedObjects = value;
        field.SetValue(rules, spawnRules);
        return previous;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhaseDone() => _phaseDone = true;

    [ServerRpc(requireOwnership: false)]
    private static void SignalVictimReturned() => _victimReturned = true;
}

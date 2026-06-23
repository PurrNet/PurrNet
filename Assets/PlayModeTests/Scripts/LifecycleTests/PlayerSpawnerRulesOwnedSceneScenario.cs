using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class PlayerSpawnerRulesOwnedSceneScenario : Scenario
{
    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _duplicateWindowSeconds = 1f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierStart = 7200;
    private const int BarrierEnd = 7201;
    private const int BarrierDuplicateEnd = 7202;

    private static readonly FieldInfo PlayerPrefabField =
        typeof(PlayerSpawner).GetField("_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo OnPlayerLoadedSceneMethod =
        typeof(PlayerSpawner).GetMethod("OnPlayerLoadedScene", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo NetworkManagerField =
        typeof(NetworkIdentity).GetField("<networkManager>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SceneIdField =
        typeof(NetworkIdentity).GetField("<sceneId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo InternalOwnerServerField =
        typeof(NetworkIdentity).GetField("internalOwnerServer", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo NetworkRulesField =
        typeof(NetworkManager).GetField("_networkRules", BindingFlags.Instance | BindingFlags.NonPublic);

    private PlayerSpawnerRulesPlayer _playerPrefab;
    private PlayerSpawner _serverSpawner;
    private PlayerID _serverTargetPlayer;
    private SceneID _serverSceneId;

    private void CreatePrefab()
    {
        var root = new GameObject(nameof(PlayerSpawnerRulesPlayer));
        _playerPrefab = root.AddComponent<PlayerSpawnerRulesPlayer>();

        if (_rules)
            _playerPrefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[PlayerSpawnerRulesOwnedSceneScenario] _rules is not assigned; the scenario must use dont-destroy-on-disconnect rules.");

        root.SetActive(false);
        PlayerSpawnerRulesPlayer.ResetAll();
        PlayerSpawnerRulesOwnedSceneIdentity.ResetAll();
        _serverSpawner = null;
        _serverTargetPlayer = default;
        _serverSceneId = default;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();

        manager.prefabProvider.AddRuntimePrefab(_playerPrefab.name, _playerPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.players.Count >= ctx.expectedConnections,
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"players-sync timeout: have {ctx.networkManager.players.Count}/{ctx.expectedConnections}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierStart, _barrierTimeoutSeconds);

        ulong targetPlayer = 0;
        if (ctx.isServer)
        {
            var result = SpawnForPlayerWithOwnedSceneIdentity(ctx);
            if (!result.success)
            {
                BroadcastTargetPlayer(0);
                await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
                return result.result;
            }

            targetPlayer = result.targetPlayer;
            BroadcastTargetPlayer(targetPlayer);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PlayerSpawnerRulesPlayer.TargetPlayerReceived,
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("target player broadcast not received");
        }

        targetPlayer = PlayerSpawnerRulesPlayer.TargetPlayer;
        if (targetPlayer == 0)
            return ScenarioResult.Ok("PlayerSpawner rules scenario requires a non-server player");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PlayerSpawnerRulesPlayer.AliveCount == 1,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
            return ScenarioResult.Fail(
                $"player prefab did not spawn with owned scene identity present: " +
                $"role={ctx.role}, target={targetPlayer}, alive={PlayerSpawnerRulesPlayer.AliveCount}, " +
                $"serverOwners=[{PlayerSpawnerRulesPlayer.ServerOwners}], clientOwners=[{PlayerSpawnerRulesPlayer.ClientOwners}], " +
                $"ownedSceneSeeded={PlayerSpawnerRulesOwnedSceneIdentity.Seeded}");
        }

        if (PlayerSpawnerRulesPlayer.SawBadId)
            return ScenarioResult.Fail("player prefab spawned with a default/unassigned id");

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PlayerSpawnerRulesPlayer.ServerOwnerCount(targetPlayer) == 1,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"server did not assign player prefab ownership to {targetPlayer}: " +
                    $"serverOwners=[{PlayerSpawnerRulesPlayer.ServerOwners}]");
            }
        }

        if (ctx.isClient)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => PlayerSpawnerRulesPlayer.ClientOwnerCount(targetPlayer) == 1,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"client did not observe player prefab ownership for {targetPlayer}: " +
                    $"clientOwners=[{PlayerSpawnerRulesPlayer.ClientOwners}]");
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => ctx.networkManager.TryGetModule(out GlobalOwnershipModule ownership, true)
                          && ownership.PlayerOwnsSomething(_serverTargetPlayer),
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"ownership module did not register ownership for {_serverTargetPlayer.id.value}: " +
                    $"serverOwners=[{PlayerSpawnerRulesPlayer.ServerOwners}]");
            }

            var duplicateResult = InvokeDuplicatePlayerLoadedScene(ctx);
            BroadcastDuplicateCheckStarted();
            if (!duplicateResult.success)
                return duplicateResult;
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => PlayerSpawnerRulesPlayer.DuplicateCheckStarted,
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("duplicate-spawn guard check was not started");
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_duplicateWindowSeconds), cancellationToken: ctx.cancellationToken);

        if (PlayerSpawnerRulesPlayer.AliveCount != 1)
        {
            return ScenarioResult.Fail(
                $"duplicate player prefab was spawned: role={ctx.role}, target={targetPlayer}, " +
                $"alive={PlayerSpawnerRulesPlayer.AliveCount}, serverOwners=[{PlayerSpawnerRulesPlayer.ServerOwners}], " +
                $"clientOwners=[{PlayerSpawnerRulesPlayer.ClientOwners}]");
        }

        if (PlayerSpawnerRulesPlayer.SawBadId)
            return ScenarioResult.Fail("player prefab spawned with a default/unassigned id during duplicate check");

        if (ctx.isServer && PlayerSpawnerRulesPlayer.ServerOwnerCount(targetPlayer) != 1)
        {
            return ScenarioResult.Fail(
                $"server owner count changed during duplicate check for {targetPlayer}: " +
                $"serverOwners=[{PlayerSpawnerRulesPlayer.ServerOwners}]");
        }

        if (ctx.isClient && PlayerSpawnerRulesPlayer.ClientOwnerCount(targetPlayer) != 1)
        {
            return ScenarioResult.Fail(
                $"client owner count changed during duplicate check for {targetPlayer}: " +
                $"clientOwners=[{PlayerSpawnerRulesPlayer.ClientOwners}]");
        }

        await ScenarioBarrier.Wait(ctx, BarrierDuplicateEnd, _barrierTimeoutSeconds);

        return ScenarioResult.Ok(
            $"spawned exactly one player prefab for {targetPlayer}; scene-owned identity did not suppress, real ownership did");
    }

    private (bool success, ScenarioResult result, ulong targetPlayer) SpawnForPlayerWithOwnedSceneIdentity(ScenarioContext ctx)
    {
        if (!_rules)
            return (false, ScenarioResult.Fail("PlayerSpawner rules scenario: _rules is not assigned"), 0);

        if (!_playerPrefab)
            return (false, ScenarioResult.Fail("PlayerSpawner rules scenario: player prefab was not created"), 0);

        if (PlayerPrefabField == null || OnPlayerLoadedSceneMethod == null ||
            NetworkManagerField == null || SceneIdField == null || InternalOwnerServerField == null ||
            NetworkRulesField == null)
            return (false, ScenarioResult.Fail("PlayerSpawner rules scenario: required reflection member was not found"), 0);

        if (!ctx.networkManager.TryGetModule(out ScenesModule scenes, true))
            return (false, ScenarioResult.Fail("PlayerSpawner rules scenario: missing ScenesModule"), 0);

        var unityScene = gameObject.scene;
        if (!scenes.TryGetSceneID(unityScene, out var sceneId))
            return (false, ScenarioResult.Fail($"PlayerSpawner rules scenario: scene id missing for {unityScene.name}"), 0);

        var player = PickNonServerPlayer(ctx);
        if (!player.HasValue)
            return (true, ScenarioResult.Ok("PlayerSpawner rules scenario requires a non-server player"), 0);

        SeedOwnedSceneIdentity(ctx.networkManager, sceneId, player.Value);

        var spawnerGo = new GameObject(nameof(PlayerSpawnerRulesOwnedSceneScenario) + "_Spawner");
        spawnerGo.SetActive(false);
        var spawner = spawnerGo.AddComponent<PlayerSpawner>();
        PlayerPrefabField.SetValue(spawner, _playerPrefab.gameObject);

        var previousRules = NetworkRulesField.GetValue(ctx.networkManager);
        NetworkRulesField.SetValue(ctx.networkManager, _rules);
        try
        {
            OnPlayerLoadedSceneMethod.Invoke(spawner, new object[] { player.Value, sceneId, true });
        }
        finally
        {
            NetworkRulesField.SetValue(ctx.networkManager, previousRules);
        }

        _serverSpawner = spawner;
        _serverTargetPlayer = player.Value;
        _serverSceneId = sceneId;

        return (true, ScenarioResult.Ok(), player.Value.id.value);
    }

    private ScenarioResult InvokeDuplicatePlayerLoadedScene(ScenarioContext ctx)
    {
        if (!_serverSpawner)
            return ScenarioResult.Fail("PlayerSpawner rules scenario: server spawner missing for duplicate check");

        var previousRules = NetworkRulesField.GetValue(ctx.networkManager);
        NetworkRulesField.SetValue(ctx.networkManager, _rules);
        try
        {
            OnPlayerLoadedSceneMethod.Invoke(_serverSpawner, new object[] { _serverTargetPlayer, _serverSceneId, true });
        }
        catch (TargetInvocationException e)
        {
            return ScenarioResult.Fail($"duplicate OnPlayerLoadedScene threw: {e.InnerException ?? e}");
        }
        finally
        {
            NetworkRulesField.SetValue(ctx.networkManager, previousRules);
        }

        return ScenarioResult.Ok();
    }

    private static PlayerID? PickNonServerPlayer(ScenarioContext ctx)
    {
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.isServer)
                return player;
        }

        return null;
    }

    private static void SeedOwnedSceneIdentity(NetworkManager manager, SceneID sceneId, PlayerID player)
    {
        var go = new GameObject(nameof(PlayerSpawnerRulesOwnedSceneIdentity));
        var identity = go.AddComponent<PlayerSpawnerRulesOwnedSceneIdentity>();
        identity.SetID(new NetworkID(991731));
        identity.SetIsSpawned(true, true);
        NetworkManagerField.SetValue(identity, manager);
        SceneIdField.SetValue(identity, sceneId);
        InternalOwnerServerField.SetValue(identity, player);
        PlayerSpawnerRulesOwnedSceneIdentity.Seeded = true;
    }

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private static void BroadcastTargetPlayer(ulong playerId)
    {
        PlayerSpawnerRulesPlayer.TargetPlayer = playerId;
        PlayerSpawnerRulesPlayer.TargetPlayerReceived = true;
    }

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private static void BroadcastDuplicateCheckStarted()
    {
        PlayerSpawnerRulesPlayer.DuplicateCheckStarted = true;
    }
}

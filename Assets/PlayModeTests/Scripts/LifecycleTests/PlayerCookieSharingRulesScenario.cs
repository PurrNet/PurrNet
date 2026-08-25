using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

public class PlayerCookieSharingRulesScenario : Scenario
{
    private const int BarrierEnd = 6800;

    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _playersTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private NetworkRules _rulesWithoutSharing;

    private static readonly FieldInfo HostMigrationRulesField =
        typeof(NetworkRules).GetField("_hostMigrationRules", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo GetPlayerJoinEventMethod =
        typeof(PlayersManager).GetMethod("GetPlayerJoinEvent", BindingFlags.Instance | BindingFlags.NonPublic);

    private PlayerCookieSharingRulesRoot _prefab;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(PlayerCookieSharingRulesRoot));
        _prefab = rootGo.AddComponent<PlayerCookieSharingRulesRoot>();

        var childGo = new GameObject(nameof(PlayerCookieSharingRulesChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<PlayerCookieSharingRulesChild>();

        rootGo.SetActive(false);
        PlayerCookieSharingRulesRoot.ResetAll();
        PlayerCookieSharingRulesChild.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (!_rulesWithoutSharing)
            return ScenarioResult.Fail("cookie sharing rules: _rulesWithoutSharing is not assigned");

        if (_rulesWithoutSharing.ShouldSharePlayerCookiesWithPeers())
            return ScenarioResult.Fail("cookie sharing rules: ordinary rules asset has cookie sharing enabled");

        if (ctx.isServer)
            Instantiate(_prefab);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasExpectedSpawn(ctx),
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cookie sharing rules spawn timeout: {DescribeState(ctx)}");
        }

        string failure = null;
        if (ctx.isServer)
        {
            var validation = await ValidateServerJoinEventGate(ctx);
            if (!validation.success)
                failure = validation.message;
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"cookie sharing rules barrier timeout: {DescribeState(ctx)}");
        }

        return failure == null
            ? ScenarioResult.Ok()
            : ScenarioResult.Fail(failure);
    }

    private async UniTask<ScenarioResult> ValidateServerJoinEventGate(ScenarioContext ctx)
    {
        if (HostMigrationRulesField == null)
            return ScenarioResult.Fail("cookie sharing rules: NetworkRules._hostMigrationRules field missing");

        if (GetPlayerJoinEventMethod == null)
            return ScenarioResult.Fail("cookie sharing rules: PlayersManager.GetPlayerJoinEvent method missing");

        if (!ctx.networkManager.networkRules)
            return ScenarioResult.Fail("cookie sharing rules: manager has no NetworkRules asset");

        if (!ctx.networkManager.TryGetModule<PlayersManager>(true, out var players))
            return ScenarioResult.Fail("cookie sharing rules: server PlayersManager missing");

        PlayerID player = default;
        Connection connection = default;
        string cookie = null;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => TryPickPlayerWithCookie(players, out player, out connection, out cookie),
                _playersTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"cookie sharing rules: no connected player with a server cookie; players={players.players.Count}");
        }

        var rules = ctx.networkManager.networkRules;
        bool original = rules.ShouldSharePlayerCookiesWithPeers();

        try
        {
            SetSharePlayerCookiesWithPeers(rules, false);
            var disabledJoin = BuildJoinEvent(players, player, connection);
            if (!string.IsNullOrEmpty(disabledJoin.cookie))
            {
                return ScenarioResult.Fail(
                    $"cookie sharing rules: disabled join packet exposed a cookie for player {player.id.value}");
            }

            SetSharePlayerCookiesWithPeers(rules, true);
            var enabledJoin = BuildJoinEvent(players, player, connection);
            if (enabledJoin.cookie != cookie)
            {
                return ScenarioResult.Fail(
                    $"cookie sharing rules: enabled join packet omitted the opted-in cookie for player {player.id.value}");
            }
        }
        finally
        {
            SetSharePlayerCookiesWithPeers(rules, original);
        }

        return ScenarioResult.Ok();
    }

    private static bool TryPickPlayerWithCookie(
        PlayersManager players,
        out PlayerID player,
        out Connection connection,
        out string cookie)
    {
        var list = players.players;
        for (int i = 0; i < list.Count; i++)
        {
            player = list[i];
            if (!players.TryGetConnection(player, out connection))
                continue;

            if (!players.TryGetCookie(player, out cookie) || string.IsNullOrEmpty(cookie))
                continue;

            return true;
        }

        player = default;
        connection = default;
        cookie = null;
        return false;
    }

    private static PlayerJoinedEvent BuildJoinEvent(PlayersManager players, PlayerID player, Connection connection)
    {
        return (PlayerJoinedEvent)GetPlayerJoinEventMethod.Invoke(players, new object[] { player, connection });
    }

    private static void SetSharePlayerCookiesWithPeers(NetworkRules rules, bool value)
    {
        var hostRules = (HostMigrationRules)HostMigrationRulesField.GetValue(rules);
        hostRules.sharePlayerCookiesWithPeers = value;
        HostMigrationRulesField.SetValue(rules, hostRules);
    }

    private static bool HasExpectedSpawn(ScenarioContext ctx)
    {
        bool serverOk = !ctx.isServer
                        || (PlayerCookieSharingRulesRoot.ServerAliveCount == 1
                            && PlayerCookieSharingRulesChild.ServerAliveCount == 1);
        bool clientOk = !ctx.isClient
                        || (PlayerCookieSharingRulesRoot.ClientAliveCount == 1
                            && PlayerCookieSharingRulesChild.ClientAliveCount == 1);
        return serverOk && clientOk
               && !PlayerCookieSharingRulesRoot.SawBadId
               && !PlayerCookieSharingRulesChild.SawBadId;
    }

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, " +
               $"serverRoot={PlayerCookieSharingRulesRoot.ServerAliveCount}, " +
               $"serverChild={PlayerCookieSharingRulesChild.ServerAliveCount}, " +
               $"clientRoot={PlayerCookieSharingRulesRoot.ClientAliveCount}, " +
               $"clientChild={PlayerCookieSharingRulesChild.ClientAliveCount}, " +
               $"rootBadId={PlayerCookieSharingRulesRoot.SawBadId}, " +
               $"childBadId={PlayerCookieSharingRulesChild.SawBadId}";
    }
}

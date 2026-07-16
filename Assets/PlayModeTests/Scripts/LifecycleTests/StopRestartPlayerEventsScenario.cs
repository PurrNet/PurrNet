using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Simulates an editor play-mode exit + re-entry without domain reload:
/// the server (or host) fully stops in-process, then starts again while every
/// static survives. onPlayerLeft must fire locally for every connected player
/// during the shutdown, and onPlayerJoined must fire again for every player
/// after the restart (Discord report: "Player Left Not Firing With Domain Reload Off").
/// </summary>
public class StopRestartPlayerEventsScenario : Scenario
{
    private const int BarrierBase = 7500;

    [SerializeField] private float _planTimeoutSeconds = 30f;
    [SerializeField] private float _planFlushSeconds = 1f;
    [SerializeField] private float _stopTimeoutSeconds = 30f;
    [SerializeField] private float _stopEventGraceSeconds = 2f;
    [SerializeField] private float _stayStoppedSeconds = 1f;
    [SerializeField] private float _restartTimeoutSeconds = 60f;
    [SerializeField] private float _reconnectRetrySeconds = 2f;
    [SerializeField] private float _rejoinTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 90f;

    private static bool _planReceived;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _planReceived = false;
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var failures = new List<string>();

        var playersBeforeStop = new List<PlayerID>(manager.players);
        if (playersBeforeStop.Count == 0)
            return ScenarioResult.Fail("no players connected before stop");

        BroadcastRestartPlan();
        await UniTask.WaitForSeconds(_planFlushSeconds, cancellationToken: ctx.cancellationToken);

        var leftPlayers = new HashSet<PlayerID>();
        var moduleBeforeStop = manager.playerModule;

        void OnLeft(PlayerID player, bool asServer)
        {
            if (asServer)
                leftPlayers.Add(player);
        }

        moduleBeforeStop.onPlayerLeft += OnLeft;

        try
        {
            if (ctx.isClient)
                manager.StopClient();
            manager.StopServer();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => manager.serverState == ConnectionState.Disconnected &&
                          manager.clientState == ConnectionState.Disconnected,
                    _stopTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"shutdown timeout: serverState={manager.serverState}, clientState={manager.clientState}");
            }

            await UniTask.WaitForSeconds(_stopEventGraceSeconds, cancellationToken: ctx.cancellationToken);

            for (var i = 0; i < playersBeforeStop.Count; i++)
            {
                if (!leftPlayers.Contains(playersBeforeStop[i]))
                    failures.Add($"onPlayerLeft did not fire for {playersBeforeStop[i]} during local shutdown");
            }
        }
        finally
        {
            moduleBeforeStop.onPlayerLeft -= OnLeft;
        }

        await UniTask.WaitForSeconds(_stayStoppedSeconds, cancellationToken: ctx.cancellationToken);

        manager.StartServer();

        var moduleAfterStart = manager.playerModule;
        if (moduleAfterStart == null)
        {
            failures.Add("playerModule is null right after StartServer");
            return ScenarioResult.Fail(string.Join(" | ", failures));
        }

        var joinedPlayers = new HashSet<PlayerID>();

        void OnJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer)
                joinedPlayers.Add(player);
        }

        moduleAfterStart.onPlayerJoined += OnJoined;

        try
        {
            if (ctx.isClient)
                manager.StartClient();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => manager.isServer && (!ctx.isClient || manager.isClient) &&
                          manager.playerCount >= ctx.expectedConnections,
                    _restartTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"restart timeout: serverState={manager.serverState}, clientState={manager.clientState}, " +
                    $"players={manager.playerCount}/{ctx.expectedConnections}");
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => AllCurrentPlayersJoined(manager, joinedPlayers),
                    _rejoinTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    "onPlayerJoined did not fire for every player after restart: " +
                    $"joined=[{string.Join(",", joinedPlayers)}], players=[{string.Join(",", manager.players)}]");
            }
        }
        finally
        {
            moduleAfterStart.onPlayerJoined -= OnJoined;
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add("final barrier timeout");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"players={playersBeforeStop.Count} left and rejoined")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static bool AllCurrentPlayersJoined(NetworkManager manager, HashSet<PlayerID> joined)
    {
        var players = manager.players;
        if (players.Count == 0)
            return false;

        for (var i = 0; i < players.Count; i++)
        {
            if (!joined.Contains(players[i]))
                return false;
        }

        return true;
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        if (ctx.isServer)
            return ScenarioResult.Ok();

        var manager = ctx.networkManager;
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _planReceived, _planTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client never received the restart plan");
        }

        var joinedPlayers = new HashSet<PlayerID>();

        void OnJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!asServer)
                joinedPlayers.Add(player);
        }

        manager.onPlayerJoined += OnJoined;

        try
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => manager.clientState == ConnectionState.Disconnected,
                    _stopTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"client was never disconnected by the server restart (state={manager.clientState})");
            }

            await UniTask.WaitForSeconds(_stayStoppedSeconds, cancellationToken: ctx.cancellationToken);

            var deadline = Time.realtimeSinceStartupAsDouble + _restartTimeoutSeconds;
            while (!(manager.isClient && manager.isLocalPlayerReady))
            {
                if (Time.realtimeSinceStartupAsDouble > deadline)
                {
                    failures.Add($"client failed to reconnect (state={manager.clientState})");
                    break;
                }

                if (manager.clientState == ConnectionState.Disconnected)
                    manager.StartClient();

                await UniTask.WaitForSeconds(_reconnectRetrySeconds, cancellationToken: ctx.cancellationToken);
            }

            if (failures.Count == 0)
            {
                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => joinedPlayers.Contains(manager.localPlayer),
                        _rejoinTimeoutSeconds,
                        ctx.cancellationToken);
                }
                catch (TimeoutException)
                {
                    failures.Add(
                        "client-side onPlayerJoined did not fire for the local player after reconnect: " +
                        $"joined=[{string.Join(",", joinedPlayers)}]");
                }
            }
        }
        finally
        {
            manager.onPlayerJoined -= OnJoined;
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add("final barrier timeout");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok("client reconnected after server restart")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastRestartPlan()
    {
        _planReceived = true;
    }
}

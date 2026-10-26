using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public static class VisibilityScenarioUtils
{
    private static readonly List<string> _errors = new();
    private static string _expectedError;
    private static bool _capturing;

    public static bool sawExpectedError { get; private set; }

    /// <summary>
    /// Remote clients ordered by id. Neither the server nor the host's own player is in there,
    /// so every entry is a peer whose copy of an object really is spawned and despawned.
    /// </summary>
    public static List<PlayerID> PickClients(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var result = new List<PlayerID>();
        var players = manager.players;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;
            if (ctx.role == NetworkRole.Host && manager.isLocalPlayerReady && manager.localPlayer == player)
                continue;
            result.Add(player);
        }

        result.Sort((a, b) => a.id.value.CompareTo(b.id.value));
        return result;
    }

    public static bool IsLocalPlayer(ScenarioContext ctx, ulong playerId)
    {
        return ctx.networkManager.isLocalPlayerReady && ctx.networkManager.localPlayer.id.value == playerId;
    }

    public static void ExpectCalls(List<string> failures, string phase, string what, List<ulong> calls, int since,
        int expected, ulong player)
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
            if (calls[i] != player)
                failures.Add($"{phase}: {what} fired for {calls[i]}, expected {player}");
        }
    }

    /// <summary>
    /// Once an object is gone, every OnObserverAdded must have been answered by exactly one
    /// OnObserverRemoved for the same player.
    /// </summary>
    public static void ExpectBalanced(List<string> failures, string what, List<ulong> added, List<ulong> removed)
    {
        var sortedAdded = new List<ulong>(added);
        var sortedRemoved = new List<ulong>(removed);
        sortedAdded.Sort();
        sortedRemoved.Sort();

        bool balanced = sortedAdded.Count == sortedRemoved.Count;
        for (var i = 0; balanced && i < sortedAdded.Count; i++)
            balanced = sortedAdded[i] == sortedRemoved[i];

        if (!balanced)
            failures.Add(
                $"destroyed: {what} OnObserverAdded [{string.Join(",", sortedAdded)}] " +
                $"does not match OnObserverRemoved [{string.Join(",", sortedRemoved)}]");
    }

    /// <summary>
    /// Starts recording errors logged on this peer. Visibility changes are routine, none of them
    /// should log one. An error containing <paramref name="expectedError"/> is noted instead.
    /// </summary>
    public static void BeginErrorCapture(string expectedError = null)
    {
        _errors.Clear();
        _expectedError = expectedError;
        sawExpectedError = false;

        if (_capturing)
            return;

        _capturing = true;
        Application.logMessageReceived += OnLogMessage;
    }

    public static ScenarioResult EndErrorCapture(ScenarioResult result)
    {
        if (_capturing)
        {
            _capturing = false;
            Application.logMessageReceived -= OnLogMessage;
        }

        if (_errors.Count == 0)
            return result;

        int shown = Mathf.Min(_errors.Count, 3);
        var errors = $"{_errors.Count} error(s) logged: {string.Join(" || ", _errors.GetRange(0, shown))}";
        return ScenarioResult.Fail(result.success ? errors : $"{result.message} | {errors}");
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception)
            return;

        if (_expectedError != null && condition.Contains(_expectedError))
        {
            sawExpectedError = true;
            return;
        }

        _errors.Add(condition);
    }
}

using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SpawnBufferedObserversRpcIdentity : NetworkIdentity
{
    public const int SpawnSeed = 64013;
    public const int ReplaySeed = 64014;

    private static readonly Dictionary<ulong, Dictionary<int, int>> ServerReportsByPlayer = new();
    public static readonly List<ulong> RemovedObservers = new();

    public static SpawnBufferedObserversRpcIdentity LocalInstance;
    public static int LocalSpawnReceiveCount;
    public static int LocalReplayReceiveCount;
    public static int LocalLastSeed;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static int ServerReportCount;
    public static bool ServerSawDuplicateReport;
    public static bool ServerSawWrongSeed;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;
    public static bool PhaseDoneReceived;
    public static string ServerReports => FormatReports();
    public static int ServerSpawnReportCount => CountReportsForSeed(SpawnSeed);
    public static int ServerReplayReportCount => CountReportsForSeed(ReplaySeed);

    public static void ResetAll()
    {
        LocalInstance = null;
        LocalSpawnReceiveCount = 0;
        LocalReplayReceiveCount = 0;
        LocalLastSeed = 0;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        ServerReportCount = 0;
        ServerSawDuplicateReport = false;
        ServerSawWrongSeed = false;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        PhaseDoneReceived = false;
        RemovedObservers.Clear();
        ServerReportsByPlayer.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;

        if (!asServer)
            return;

        Initialize(SpawnSeed);
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        RemovedObservers.Add(player.id.value);
    }

    public void TriggerReplay()
    {
        Initialize(ReplaySeed);
    }

    [ObserversRpc(bufferLast: true)]
    public void Initialize(int seed)
    {
        int receiveCount;
        if (seed == SpawnSeed)
        {
            LocalSpawnReceiveCount++;
            receiveCount = LocalSpawnReceiveCount;
        }
        else if (seed == ReplaySeed)
        {
            LocalReplayReceiveCount++;
            receiveCount = LocalReplayReceiveCount;
        }
        else
        {
            receiveCount = 0;
        }

        LocalLastSeed = seed;
        ReportInitialized(receiveCount, seed);
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    public static bool ServerHasExactlyOneReportPerPlayer(int seed, int expectedPlayers)
    {
        if (CountReportsForSeed(seed) != expectedPlayers)
            return false;

        int playersWithSeed = 0;
        foreach (var playerReports in ServerReportsByPlayer.Values)
        {
            if (!playerReports.TryGetValue(seed, out int count))
                continue;

            playersWithSeed++;
            if (count != 1)
                return false;
        }

        return playersWithSeed == expectedPlayers;
    }

    [ServerRpc(requireOwnership: false)]
    private void ReportInitialized(int receiveCount, int seed, RPCInfo info = default)
    {
        ServerReportCount++;

        if (seed != SpawnSeed && seed != ReplaySeed)
            ServerSawWrongSeed = true;

        ulong sender = info.sender.id.value;
        if (!ServerReportsByPlayer.TryGetValue(sender, out var playerReports))
        {
            playerReports = new Dictionary<int, int>();
            ServerReportsByPlayer[sender] = playerReports;
        }

        playerReports.TryGetValue(seed, out int previous);
        playerReports[seed] = previous + 1;

        if (previous > 0)
            ServerSawDuplicateReport = true;

        if (receiveCount != 1)
            ServerSawDuplicateReport = true;
    }

    private static int CountReportsForSeed(int seed)
    {
        int count = 0;
        foreach (var playerReports in ServerReportsByPlayer.Values)
        {
            if (playerReports.TryGetValue(seed, out int playerCount))
                count += playerCount;
        }

        return count;
    }

    private static string FormatReports()
    {
        var parts = new List<string>(ServerReportsByPlayer.Count);
        foreach (var pair in ServerReportsByPlayer)
        {
            pair.Value.TryGetValue(SpawnSeed, out int spawnCount);
            pair.Value.TryGetValue(ReplaySeed, out int replayCount);
            parts.Add($"{pair.Key}:spawn={spawnCount},replay={replayCount}");
        }

        return string.Join(",", parts);
    }
}

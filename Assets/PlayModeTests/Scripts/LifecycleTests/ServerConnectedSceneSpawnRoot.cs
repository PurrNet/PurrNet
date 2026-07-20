using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class ServerConnectedSceneSpawnRoot : NetworkIdentity
{
    public const int BufferedProbeSeed = 81203;

    private static readonly HashSet<NetworkID> _serverAlive = new();
    private static readonly HashSet<NetworkID> _clientAlive = new();
    private static readonly Dictionary<ulong, int> _bufferedReportsByPlayer = new();

    public static bool BufferedProbeEnabled;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ServerSpawnCount;
    public static int ClientSpawnCount;
    public static string ServerSceneName;
    public static string ClientSceneName;
    public static bool SawBadId;
    public static bool SawNonSceneObject;
    public static int LocalBufferedReceiveCount;
    public static int LocalBufferedLastSeed;
    public static int ServerBufferedReportCount;
    public static bool ServerSawBufferedDuplicate;
    public static bool ServerSawWrongBufferedSeed;
    public static string BufferedReports => FormatBufferedReports();
    public static NetworkID ServerLastId;
    public static NetworkID ClientLastId;

    private NetworkID? _serverTrackedId;
    private NetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        BufferedProbeEnabled = false;
        _serverAlive.Clear();
        _clientAlive.Clear();
        _bufferedReportsByPlayer.Clear();
        ServerSpawnCount = 0;
        ClientSpawnCount = 0;
        ServerSceneName = null;
        ClientSceneName = null;
        SawBadId = false;
        SawNonSceneObject = false;
        LocalBufferedReceiveCount = 0;
        LocalBufferedLastSeed = 0;
        ServerBufferedReportCount = 0;
        ServerSawBufferedDuplicate = false;
        ServerSawWrongBufferedSeed = false;
        ServerLastId = default;
        ClientLastId = default;
    }

    public static int BufferedReportCountForPlayer(ulong playerId) =>
        _bufferedReportsByPlayer.TryGetValue(playerId, out int count) ? count : 0;

    public static void ClearBufferedProbeState()
    {
        _bufferedReportsByPlayer.Clear();
        LocalBufferedReceiveCount = 0;
        LocalBufferedLastSeed = 0;
        ServerBufferedReportCount = 0;
        ServerSawBufferedDuplicate = false;
        ServerSawWrongBufferedSeed = false;
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!id.HasValue)
        {
            SawBadId = true;
            return;
        }

        if (!isSceneObject)
            SawNonSceneObject = true;

        if (asServer)
        {
            _serverTrackedId = id.Value;
            ServerLastId = id.Value;
            _serverAlive.Add(id.Value);
            ServerSpawnCount++;
            ServerSceneName = gameObject.scene.name;
            if (BufferedProbeEnabled)
                InitializeSceneObjectBuffered(BufferedProbeSeed);
            return;
        }

        _clientTrackedId = id.Value;
        ClientLastId = id.Value;
        _clientAlive.Add(id.Value);
        ClientSpawnCount++;
        ClientSceneName = gameObject.scene.name;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
        {
            if (_serverTrackedId.HasValue)
                _serverAlive.Remove(_serverTrackedId.Value);
            _serverTrackedId = null;
            if (_serverAlive.Count == 0)
                ServerSceneName = null;
            return;
        }

        if (_clientTrackedId.HasValue)
            _clientAlive.Remove(_clientTrackedId.Value);
        _clientTrackedId = null;
        if (_clientAlive.Count == 0)
            ClientSceneName = null;
    }

    [ObserversRpc(bufferLast: true)]
    public void InitializeSceneObjectBuffered(int seed)
    {
        LocalBufferedReceiveCount++;
        LocalBufferedLastSeed = seed;
        ReportSceneObjectBuffered(LocalBufferedReceiveCount, seed);
    }

    [ServerRpc(requireOwnership: false)]
    private void ReportSceneObjectBuffered(int receiveCount, int seed, RPCInfo info = default)
    {
        ServerBufferedReportCount++;

        if (seed != BufferedProbeSeed)
            ServerSawWrongBufferedSeed = true;

        ulong sender = info.sender.id.value;
        _bufferedReportsByPlayer.TryGetValue(sender, out int previous);
        _bufferedReportsByPlayer[sender] = previous + 1;

        if (previous > 0 || receiveCount != 1)
            ServerSawBufferedDuplicate = true;
    }

    private static string FormatBufferedReports()
    {
        var parts = new List<string>(_bufferedReportsByPlayer.Count);
        foreach (var pair in _bufferedReportsByPlayer)
            parts.Add($"{pair.Key}:{pair.Value}");
        return string.Join(",", parts);
    }
}

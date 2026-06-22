using System.Collections.Generic;
using PurrNet;

public class AuthoredSceneReconnectChild : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _serverAlive = new();
    private static readonly HashSet<NetworkID> _clientAlive = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ServerSpawnCount;
    public static int ClientSpawnCount;
    public static bool SawBadId;
    public static bool SawNonSceneObject;

    private NetworkID? _serverTrackedId;
    private NetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        ServerSpawnCount = 0;
        ClientSpawnCount = 0;
        SawBadId = false;
        SawNonSceneObject = false;
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
            _serverAlive.Add(id.Value);
            ServerSpawnCount++;
            return;
        }

        _clientTrackedId = id.Value;
        _clientAlive.Add(id.Value);
        ClientSpawnCount++;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
        {
            if (_serverTrackedId.HasValue)
                _serverAlive.Remove(_serverTrackedId.Value);
            _serverTrackedId = null;
            return;
        }

        if (_clientTrackedId.HasValue)
            _clientAlive.Remove(_clientTrackedId.Value);
        _clientTrackedId = null;
    }
}

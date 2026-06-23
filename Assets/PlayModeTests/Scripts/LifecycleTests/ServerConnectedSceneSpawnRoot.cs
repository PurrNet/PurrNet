using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class ServerConnectedSceneSpawnRoot : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _serverAlive = new();
    private static readonly HashSet<NetworkID> _clientAlive = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ServerSpawnCount;
    public static int ClientSpawnCount;
    public static string ServerSceneName;
    public static string ClientSceneName;
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
        ServerSceneName = null;
        ClientSceneName = null;
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
            ServerSceneName = gameObject.scene.name;
            return;
        }

        _clientTrackedId = id.Value;
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
}

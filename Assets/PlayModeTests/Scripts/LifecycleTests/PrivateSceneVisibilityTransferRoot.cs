using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class PrivateSceneVisibilityTransferRoot : NetworkIdentity
{
    private static readonly HashSet<GlobalNetworkID> _serverAlive = new();
    private static readonly HashSet<GlobalNetworkID> _clientAlive = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static string ClientSceneName;
    public static bool SawBadId;

    private GlobalNetworkID? _serverTrackedId;
    private GlobalNetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        ClientSpawnCount = 0;
        ClientSceneName = null;
        SawBadId = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!id.HasValue || id.Value == default)
        {
            SawBadId = true;
            return;
        }

        var globalId = new GlobalNetworkID(sceneId, id.Value);
        if (asServer)
        {
            _serverTrackedId = globalId;
            _serverAlive.Add(globalId);
            return;
        }

        _clientTrackedId = globalId;
        _clientAlive.Add(globalId);
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
            return;
        }

        if (_clientTrackedId.HasValue)
            _clientAlive.Remove(_clientTrackedId.Value);
        _clientTrackedId = null;

        if (_clientAlive.Count == 0)
            ClientSceneName = null;
    }
}

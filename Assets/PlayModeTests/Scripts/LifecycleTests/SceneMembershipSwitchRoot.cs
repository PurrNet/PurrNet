using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SceneMembershipSwitchRoot : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _serverAlive = new();
    private static readonly HashSet<NetworkID> _clientAlive = new();

    public static SceneMembershipSwitchRoot LocalClientInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static string ClientSceneName;
    public static bool SawBadId;

    private NetworkID? _serverTrackedId;
    private NetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        LocalClientInstance = null;
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
        if (!id.HasValue)
        {
            SawBadId = true;
            return;
        }

        if (asServer)
        {
            _serverTrackedId = id.Value;
            _serverAlive.Add(id.Value);
            return;
        }

        _clientTrackedId = id.Value;
        _clientAlive.Add(id.Value);
        ClientSpawnCount++;
        ClientSceneName = gameObject.scene.name;
        LocalClientInstance = this;
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

        if (LocalClientInstance == this)
            LocalClientInstance = null;
        if (_clientAlive.Count == 0)
            ClientSceneName = null;
    }
}

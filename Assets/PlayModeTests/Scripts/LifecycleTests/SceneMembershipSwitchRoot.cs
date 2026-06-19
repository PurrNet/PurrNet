using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SceneMembershipSwitchRoot : NetworkIdentity
{
    private static readonly HashSet<(SceneID scene, NetworkID id)> _serverAlive = new();
    private static readonly HashSet<(SceneID scene, NetworkID id)> _clientAlive = new();

    public static SceneMembershipSwitchRoot LocalClientInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static string ClientSceneName;
    public static bool SawBadId;

    private (SceneID scene, NetworkID id)? _serverTrackedId;
    private (SceneID scene, NetworkID id)? _clientTrackedId;

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
            _serverTrackedId = (sceneId, id.Value);
            _serverAlive.Add(_serverTrackedId.Value);
            return;
        }

        _clientTrackedId = (sceneId, id.Value);
        _clientAlive.Add(_clientTrackedId.Value);
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

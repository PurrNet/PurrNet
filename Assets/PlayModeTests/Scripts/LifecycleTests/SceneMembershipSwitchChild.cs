using System.Collections.Generic;
using PurrNet;

public class SceneMembershipSwitchChild : NetworkIdentity
{
    private static readonly HashSet<(SceneID scene, NetworkID id)> _serverAlive = new();
    private static readonly HashSet<(SceneID scene, NetworkID id)> _clientAlive = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static bool SawBadId;

    private (SceneID scene, NetworkID id)? _serverTrackedId;
    private (SceneID scene, NetworkID id)? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        ClientSpawnCount = 0;
        SawBadId = false;
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!id.HasValue || id.Value == default)
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

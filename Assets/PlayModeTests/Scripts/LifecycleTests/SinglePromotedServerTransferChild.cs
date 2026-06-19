using System.Collections.Generic;
using PurrNet;

public class SinglePromotedServerTransferChild : NetworkIdentity
{
    public struct SpawnRecord
    {
        public bool asServer;
        public bool isOwner;
        public bool isController;
        public bool hasConnectedOwner;
        public bool hasOwner;
        public bool ownerHasValue;
        public ulong ownerId;
        public string sceneName;
    }

    private static readonly HashSet<GlobalNetworkID> _serverAlive = new();
    private static readonly HashSet<GlobalNetworkID> _clientAlive = new();

    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static bool SawBadId;
    public static bool HasLastClientSpawn;
    public static SpawnRecord LastClientSpawn;

    private GlobalNetworkID? _serverTrackedId;
    private GlobalNetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        ClientSpawnCount = 0;
        SawBadId = false;
        HasLastClientSpawn = false;
        LastClientSpawn = default;
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
        HasLastClientSpawn = true;
        LastClientSpawn = new SpawnRecord
        {
            asServer = asServer,
            isOwner = isOwner,
            isController = isController,
            hasConnectedOwner = hasConnectedOwner,
            hasOwner = hasOwner,
            ownerHasValue = owner.HasValue,
            ownerId = owner.HasValue ? owner.Value.id.value : 0,
            sceneName = gameObject.scene.name
        };
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

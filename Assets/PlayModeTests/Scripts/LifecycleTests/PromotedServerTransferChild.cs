using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class PromotedServerTransferChild : NetworkIdentity
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

    [SerializeField] private SyncVar<int> _serverValue = new(0, sendIntervalInSeconds: 0f, ownerAuth: false);
    [SerializeField] private SyncVar<int> _ownerValue = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    public static PromotedServerTransferChild ServerInstance;
    public static PromotedServerTransferChild LocalClientInstance;
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
        ServerInstance = null;
        LocalClientInstance = null;
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
            ServerInstance = this;
            return;
        }

        _clientTrackedId = globalId;
        _clientAlive.Add(globalId);
        ClientSpawnCount++;
        LocalClientInstance = this;
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
            if (ServerInstance == this)
                ServerInstance = null;
            return;
        }

        if (_clientTrackedId.HasValue)
            _clientAlive.Remove(_clientTrackedId.Value);
        _clientTrackedId = null;

        if (LocalClientInstance == this)
            LocalClientInstance = null;
    }

    public void SetServerValue(int value)
    {
        _serverValue.value = value;
    }

    public void SetOwnerValue(int value)
    {
        _ownerValue.value = value;
    }

    public bool HasState(int expectedServerValue, int expectedOwnerValue)
    {
        return _serverValue.value == expectedServerValue && _ownerValue.value == expectedOwnerValue;
    }

    public string DescribeState()
    {
        return $"serverValue={_serverValue.value}, ownerValue={_ownerValue.value}";
    }
}

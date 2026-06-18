using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SceneMembershipRejoinRoot : NetworkIdentity
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

    private static readonly HashSet<NetworkID> _serverAlive = new();
    private static readonly HashSet<NetworkID> _clientAlive = new();

    public static SceneMembershipRejoinRoot LocalClientInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ClientSpawnCount;
    public static string ClientSceneName;
    public static bool SawBadId;
    public static bool HasLastClientSpawn;
    public static SpawnRecord LastClientSpawn;
    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();

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
        HasLastClientSpawn = false;
        LastClientSpawn = default;
        DisconnectCalls.Clear();
        ReconnectCalls.Clear();
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

        if (LocalClientInstance == this)
            LocalClientInstance = null;
        if (_clientAlive.Count == 0)
            ClientSceneName = null;
    }

    protected override void OnOwnerDisconnected(PlayerID ownerId)
    {
        DisconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnOwnerReconnected(PlayerID ownerId)
    {
        ReconnectCalls.Add(ownerId.id.value);
    }
}

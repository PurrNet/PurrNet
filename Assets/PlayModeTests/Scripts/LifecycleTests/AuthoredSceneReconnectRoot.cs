using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class AuthoredSceneReconnectRoot : NetworkIdentity
{
    public struct SpawnRecord
    {
        public bool asServer;
        public bool isSceneObject;
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

    public static AuthoredSceneReconnectRoot LocalInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ServerSpawnCount;
    public static int ClientSpawnCount;
    public static string ServerSceneName;
    public static string ClientSceneName;
    public static bool SawBadId;
    public static bool SawNonSceneObject;
    public static bool HasLastServerSpawn;
    public static bool HasLastClientSpawn;
    public static SpawnRecord LastServerSpawn;
    public static SpawnRecord LastClientSpawn;
    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();

    private NetworkID? _serverTrackedId;
    private NetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        LocalInstance = null;
        ServerSpawnCount = 0;
        ClientSpawnCount = 0;
        ServerSceneName = null;
        ClientSceneName = null;
        SawBadId = false;
        SawNonSceneObject = false;
        HasLastServerSpawn = false;
        HasLastClientSpawn = false;
        LastServerSpawn = default;
        LastClientSpawn = default;
        DisconnectCalls.Clear();
        ReconnectCalls.Clear();
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

        LocalInstance = this;
        var record = BuildRecord(asServer);

        if (asServer)
        {
            _serverTrackedId = id.Value;
            _serverAlive.Add(id.Value);
            ServerSpawnCount++;
            ServerSceneName = gameObject.scene.name;
            HasLastServerSpawn = true;
            LastServerSpawn = record;
            return;
        }

        _clientTrackedId = id.Value;
        _clientAlive.Add(id.Value);
        ClientSpawnCount++;
        ClientSceneName = gameObject.scene.name;
        HasLastClientSpawn = true;
        LastClientSpawn = record;
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
        }
        else
        {
            if (_clientTrackedId.HasValue)
                _clientAlive.Remove(_clientTrackedId.Value);
            _clientTrackedId = null;
            if (_clientAlive.Count == 0)
                ClientSceneName = null;
        }

        if (LocalInstance == this && !_serverTrackedId.HasValue && !_clientTrackedId.HasValue)
            LocalInstance = null;
    }

    protected override void OnOwnerDisconnected(PlayerID ownerId)
    {
        DisconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnOwnerReconnected(PlayerID ownerId)
    {
        ReconnectCalls.Add(ownerId.id.value);
    }

    private SpawnRecord BuildRecord(bool asServer)
    {
        return new SpawnRecord
        {
            asServer = asServer,
            isSceneObject = isSceneObject,
            isOwner = isOwner,
            isController = isController,
            hasConnectedOwner = hasConnectedOwner,
            hasOwner = hasOwner,
            ownerHasValue = owner.HasValue,
            ownerId = owner.HasValue ? owner.Value.id.value : 0,
            sceneName = gameObject.scene.name
        };
    }
}

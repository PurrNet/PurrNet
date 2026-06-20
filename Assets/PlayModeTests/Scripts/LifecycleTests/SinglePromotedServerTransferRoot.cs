using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class SinglePromotedServerTransferRoot : NetworkIdentity
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

    public static SinglePromotedServerTransferRoot ServerInstance;
    public static SinglePromotedServerTransferRoot LocalClientInstance;
    public static int ServerAliveCount => _serverAlive.Count;
    public static int ClientAliveCount => _clientAlive.Count;
    public static int ServerDirectChildCount => ServerInstance && ServerInstance.directChildren != null
        ? ServerInstance.directChildren.Count
        : -1;
    public static string ServerObservers => FormatObservers(ServerInstance);
    public static string ServerId => FormatId(ServerInstance);
    public static string ServerPrefabInfo => FormatPrefabInfo(ServerInstance);
    public static int ClientSpawnCount;
    public static string ClientSceneName;
    public static bool SawBadId;
    public static bool HasLastClientSpawn;
    public static SpawnRecord LastClientSpawn;
    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();

    private GlobalNetworkID? _serverTrackedId;
    private GlobalNetworkID? _clientTrackedId;

    public static void ResetAll()
    {
        _serverAlive.Clear();
        _clientAlive.Clear();
        ServerInstance = null;
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
            if (ServerInstance == this)
                ServerInstance = null;
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

    public static string FormatObservers(NetworkIdentity identity)
    {
        if (!identity)
            return "<none>";

        var observers = identity.observers;
        if (observers == null || observers.Count == 0)
            return "";

        var ids = new List<ulong>(observers.Count);
        for (int i = 0; i < observers.Count; i++)
            ids.Add(observers[i].id.value);
        return string.Join(",", ids);
    }

    private static string FormatId(NetworkIdentity identity)
    {
        if (!identity || !identity.id.HasValue)
            return "<none>";

        return identity.id.Value.id.value.ToString();
    }

    public static string FormatPrefabInfo(NetworkIdentity identity)
    {
        if (!identity)
            return "<none>";

        return $"{identity.prefabId}:{identity.componentIndex}";
    }
}

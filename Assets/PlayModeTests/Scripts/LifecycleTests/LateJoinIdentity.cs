using System.Collections.Generic;
using PurrNet;

public class LateJoinIdentity : NetworkIdentity
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
    }

    public struct ObserverRecord
    {
        public ulong playerId;
        public bool added; // true=added, false=removed
        public bool isSpawner; // only meaningful when added
    }

    public static LateJoinIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerRejoinedAckCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;
    public static bool RejoinPhaseSignal;

    public static readonly List<SpawnRecord> Spawns = new();
    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();
    public static readonly List<ObserverRecord> ObserverEvents = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerRejoinedAckCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        RejoinPhaseSignal = false;
        Spawns.Clear();
        DisconnectCalls.Clear();
        ReconnectCalls.Clear();
        ObserverEvents.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        Spawns.Add(new SpawnRecord
        {
            asServer = asServer,
            isOwner = isOwner,
            isController = isController,
            hasConnectedOwner = hasConnectedOwner,
            hasOwner = hasOwner,
            ownerHasValue = owner.HasValue,
            ownerId = owner.HasValue ? owner.Value.id.value : 0,
        });
    }

    protected override void OnOwnerDisconnected(PlayerID ownerId)
    {
        DisconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnOwnerReconnected(PlayerID ownerId)
    {
        ReconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnObserverAdded(PlayerID player, bool isSpawner)
    {
        ObserverEvents.Add(new ObserverRecord
        {
            playerId = player.id.value,
            added = true,
            isSpawner = isSpawner,
        });
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        ObserverEvents.Add(new ObserverRecord
        {
            playerId = player.id.value,
            added = false,
        });
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalRejoinedAck(RPCInfo info = default) => ServerRejoinedAckCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastRejoinPhase()
    {
        RejoinPhaseSignal = true;
    }
}

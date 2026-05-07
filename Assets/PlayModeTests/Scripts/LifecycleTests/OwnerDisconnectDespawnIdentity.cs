using System.Collections.Generic;
using PurrNet;

public class OwnerDisconnectDespawnIdentity : NetworkIdentity
{
    public static OwnerDisconnectDespawnIdentity LocalInstance;
    public static int ServerReadyCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;

    public static int OnDespawnedNoArg;
    public static int OnDespawnedServer;
    public static int OnDespawnedClient;

    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        OnDespawnedNoArg = 0;
        OnDespawnedServer = 0;
        OnDespawnedClient = 0;
        DisconnectCalls.Clear();
        ReconnectCalls.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnOwnerDisconnected(PlayerID ownerId)
    {
        DisconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnOwnerReconnected(PlayerID ownerId)
    {
        ReconnectCalls.Add(ownerId.id.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer) OnDespawnedServer++;
        else OnDespawnedClient++;
    }

    protected override void OnDespawned()
    {
        OnDespawnedNoArg++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }
}

using System.Collections.Generic;
using PurrNet;

public class ObserverRemovalIdentity : NetworkIdentity
{
    public static ObserverRemovalIdentity LocalInstance;
    public static int ServerReadyCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;

    public static readonly List<ulong> ObserverRemovedCalls = new();
    public static int OnDespawnedNoArg;
    public static int OnDespawnedServer;
    public static int OnDespawnedClient;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        ObserverRemovedCalls.Clear();
        OnDespawnedNoArg = 0;
        OnDespawnedServer = 0;
        OnDespawnedClient = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnObserverRemoved(PlayerID player)
    {
        ObserverRemovedCalls.Add(player.id.value);
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

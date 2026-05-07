using System.Collections.Generic;
using PurrNet;

public class OwnerDisconnectIdentity : NetworkIdentity
{
    public static OwnerDisconnectIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;
    public static bool PhaseDoneReceived;
    public static int VictimReturnedCount;

    public static readonly List<ulong> DisconnectCalls = new();
    public static readonly List<ulong> ReconnectCalls = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        PhaseDoneReceived = false;
        VictimReturnedCount = 0;
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

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default)
    {
        ServerReadyCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        ServerDoneCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default)
    {
        VictimReturnedCount++;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastPhaseDone()
    {
        PhaseDoneReceived = true;
    }
}

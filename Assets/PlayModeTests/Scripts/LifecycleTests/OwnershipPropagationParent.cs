using System.Collections.Generic;
using PurrNet;

public class OwnershipPropagationParent : NetworkIdentity
{
    public struct ChangeRecord
    {
        public bool oldHasValue;
        public ulong oldOwnerId;
        public bool newHasValue;
        public ulong newOwnerId;
        public bool asServer;
        public bool isOwnerAfter;
        public bool isControllerAfter;
    }

    public static OwnershipPropagationParent LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static ulong VictimPlayerId;
    public static bool VictimIdReceived;

    public static readonly List<ChangeRecord> Changes = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        VictimPlayerId = 0;
        VictimIdReceived = false;
        Changes.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        Changes.Add(new ChangeRecord
        {
            oldHasValue = oldOwner.HasValue,
            oldOwnerId = oldOwner.HasValue ? oldOwner.Value.id.value : 0,
            newHasValue = newOwner.HasValue,
            newOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0,
            asServer = asServer,
            isOwnerAfter = isOwner,
            isControllerAfter = isController,
        });
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastVictim(ulong victimId)
    {
        VictimPlayerId = victimId;
        VictimIdReceived = true;
    }
}

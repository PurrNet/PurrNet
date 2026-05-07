using System.Collections.Generic;
using PurrNet;

public class OwnerChangeIdentity : NetworkIdentity
{
    public struct ChangeRecord
    {
        public bool oldOwnerHasValue;
        public ulong oldOwnerId;
        public bool newOwnerHasValue;
        public ulong newOwnerId;
        public bool selfRequest;
        public bool asServer;
        public bool isOwnerAfter;
        public bool hasOwnerAfter;
    }

    public static OwnerChangeIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static int ExpectedTransitions;

    public static readonly List<ChangeRecord> ThreeArgRecords = new();
    public static readonly List<ChangeRecord> FourArgRecords = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        ExpectedTransitions = 0;
        ResetTransitionRecords();
    }

    public static void ResetTransitionRecords()
    {
        ThreeArgRecords.Clear();
        FourArgRecords.Clear();
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
        ThreeArgRecords.Add(new ChangeRecord
        {
            oldOwnerHasValue = oldOwner.HasValue,
            oldOwnerId = oldOwner.HasValue ? oldOwner.Value.id.value : 0,
            newOwnerHasValue = newOwner.HasValue,
            newOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0,
            asServer = asServer,
            isOwnerAfter = isOwner,
            hasOwnerAfter = hasOwner,
        });
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool selfRequest, bool asServer)
    {
        FourArgRecords.Add(new ChangeRecord
        {
            oldOwnerHasValue = oldOwner.HasValue,
            oldOwnerId = oldOwner.HasValue ? oldOwner.Value.id.value : 0,
            newOwnerHasValue = newOwner.HasValue,
            newOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0,
            selfRequest = selfRequest,
            asServer = asServer,
            isOwnerAfter = isOwner,
            hasOwnerAfter = hasOwner,
        });
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

    [ObserversRpc(runLocally: true)]
    public void BroadcastExpectedTransitions(int count)
    {
        ExpectedTransitions = count;
    }
}

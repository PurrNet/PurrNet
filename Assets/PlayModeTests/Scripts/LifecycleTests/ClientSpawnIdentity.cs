using System.Collections.Generic;
using PurrNet;

public class ClientSpawnIdentity : NetworkIdentity
{
    public struct OwnerChangeRecord
    {
        public bool oldOwnerHasValue;
        public ulong oldOwnerId;
        public bool newOwnerHasValue;
        public ulong newOwnerId;
        public bool selfRequest;
        public bool asServer;
        public bool isOwnerAfter;
    }

    public struct ObserverRecord
    {
        public ulong playerId;
        public bool isSpawner;
    }

    public static ClientSpawnIdentity LocalInstance;

    public static readonly List<OwnerChangeRecord> ChangeRecords = new();
    public static readonly List<ObserverRecord> ObserverAdds = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ChangeRecords.Clear();
        ObserverAdds.Clear();
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool selfRequest, bool asServer)
    {
        ChangeRecords.Add(new OwnerChangeRecord
        {
            oldOwnerHasValue = oldOwner.HasValue,
            oldOwnerId = oldOwner.HasValue ? oldOwner.Value.id.value : 0,
            newOwnerHasValue = newOwner.HasValue,
            newOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0,
            selfRequest = selfRequest,
            asServer = asServer,
            isOwnerAfter = isOwner,
        });
    }

    protected override void OnObserverAdded(PlayerID player, bool isSpawner)
    {
        ObserverAdds.Add(new ObserverRecord
        {
            playerId = player.id.value,
            isSpawner = isSpawner,
        });
    }
}

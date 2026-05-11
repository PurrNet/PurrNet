using System.Collections.Generic;
using PurrNet;

public class ClientSpawnIdentity : NetworkIdentity
{
    public struct OwnerChangeRecord
    {
        public bool oldOwnerHasValue;
        public bool newOwnerHasValue;
        public ulong newOwnerId;
        public bool asServer;
        public bool isOwnerAfter;
    }

    public struct ObserverRecord
    {
        public ulong playerId;
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

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        ChangeRecords.Add(new OwnerChangeRecord
        {
            oldOwnerHasValue = oldOwner.HasValue,
            newOwnerHasValue = newOwner.HasValue,
            newOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0,
            asServer = asServer,
            isOwnerAfter = isOwner,
        });
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        ObserverAdds.Add(new ObserverRecord
        {
            playerId = player.id.value,
        });
    }
}

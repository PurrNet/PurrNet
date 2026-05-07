using System.Collections.Generic;
using PurrNet;

public class OwnershipPropagationChild : NetworkIdentity
{
    public static OwnershipPropagationChild LocalInstance;

    public static readonly List<OwnershipPropagationParent.ChangeRecord> Changes = new();

    public static void ResetAll()
    {
        LocalInstance = null;
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
        Changes.Add(new OwnershipPropagationParent.ChangeRecord
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
}

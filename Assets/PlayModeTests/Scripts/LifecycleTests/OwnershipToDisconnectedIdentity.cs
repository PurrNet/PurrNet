using PurrNet;

public class OwnershipToDisconnectedIdentity : NetworkIdentity
{
    public static OwnershipToDisconnectedIdentity LocalInstance;
    public static int ServerReadyCount;

    public static int OwnerChangedCount;
    public static bool LastNewOwnerHasValue;
    public static ulong LastNewOwnerId;
    public static bool LastHasConnectedOwnerAfter;
    public static bool LastIsControllerAfter;
    public static bool LastIsOwnerAfter;
    public static bool LastAsServer;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        OwnerChangedCount = 0;
        LastNewOwnerHasValue = false;
        LastNewOwnerId = 0;
        LastHasConnectedOwnerAfter = false;
        LastIsControllerAfter = false;
        LastIsOwnerAfter = false;
        LastAsServer = false;
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
        OwnerChangedCount++;
        LastNewOwnerHasValue = newOwner.HasValue;
        LastNewOwnerId = newOwner.HasValue ? newOwner.Value.id.value : 0;
        LastHasConnectedOwnerAfter = hasConnectedOwner;
        LastIsControllerAfter = isController;
        LastIsOwnerAfter = isOwner;
        LastAsServer = asServer;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;
}

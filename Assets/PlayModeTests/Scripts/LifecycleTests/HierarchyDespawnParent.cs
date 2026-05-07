using PurrNet;

public class HierarchyDespawnParent : NetworkIdentity
{
    public static HierarchyDespawnParent LocalInstance;
    public static int ServerReadyCount;

    public static int OnDespawnedNoArg;
    public static int OnDespawnedServer;
    public static int OnDespawnedClient;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
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
}

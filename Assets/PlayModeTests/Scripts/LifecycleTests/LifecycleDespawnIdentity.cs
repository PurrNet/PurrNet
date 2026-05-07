using PurrNet;

public class LifecycleDespawnIdentity : NetworkIdentity
{
    public static LifecycleDespawnIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;

    public static int OnDespawnedNoArg;
    public static int OnDespawnedServer;
    public static int OnDespawnedClient;
    public static int OnDespawnedNoArgFiredAfterAsServer;
    public static int OnDespawnedNoArgFiredAfterAsClient;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        OnDespawnedNoArg = 0;
        OnDespawnedServer = 0;
        OnDespawnedClient = 0;
        OnDespawnedNoArgFiredAfterAsServer = 0;
        OnDespawnedNoArgFiredAfterAsClient = 0;
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
        if (OnDespawnedServer > 0) OnDespawnedNoArgFiredAfterAsServer++;
        if (OnDespawnedClient > 0) OnDespawnedNoArgFiredAfterAsClient++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;
}

using PurrNet;

public class LifecycleSpawnIdentity : NetworkIdentity
{
    public static LifecycleSpawnIdentity LocalInstance;
    public static int ServerDoneCount;

    public static int OnEarlySpawnNoArg;
    public static int OnEarlySpawnServer;
    public static int OnEarlySpawnClient;
    public static int OnSpawnedNoArg;
    public static int OnSpawnedServer;
    public static int OnSpawnedClient;

    public static bool IsOwnerInOnSpawnedNoArg;
    public static bool IsSpawnedInOnSpawnedNoArg;
    public static bool IsControllerInOnSpawnedNoArg;
    public static int OnSpawnedNoArgFiredAfterEarly;
    public static int OnSpawnedAsServerFiredAfterEarlyAsServer;

    public static void ResetCounters()
    {
        OnEarlySpawnNoArg = 0;
        OnEarlySpawnServer = 0;
        OnEarlySpawnClient = 0;
        OnSpawnedNoArg = 0;
        OnSpawnedServer = 0;
        OnSpawnedClient = 0;
        IsOwnerInOnSpawnedNoArg = false;
        IsSpawnedInOnSpawnedNoArg = false;
        IsControllerInOnSpawnedNoArg = false;
        OnSpawnedNoArgFiredAfterEarly = 0;
        OnSpawnedAsServerFiredAfterEarlyAsServer = 0;
        ServerDoneCount = 0;
        LocalInstance = null;
    }

    protected override void OnEarlySpawn()
    {
        OnEarlySpawnNoArg++;
        gameObject.SetActive(true);
    }

    protected override void OnEarlySpawn(bool asServer)
    {
        if (asServer) OnEarlySpawnServer++;
        else OnEarlySpawnClient++;
    }

    protected override void OnSpawned()
    {
        OnSpawnedNoArg++;
        IsOwnerInOnSpawnedNoArg = isOwner;
        IsSpawnedInOnSpawnedNoArg = isSpawned;
        IsControllerInOnSpawnedNoArg = isController;
        if (OnEarlySpawnNoArg > 0)
            OnSpawnedNoArgFiredAfterEarly++;
        LocalInstance = this;
    }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
        {
            OnSpawnedServer++;
            if (OnEarlySpawnServer > 0)
                OnSpawnedAsServerFiredAfterEarlyAsServer++;
        }
        else
        {
            OnSpawnedClient++;
        }
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        ServerDoneCount++;
    }
}

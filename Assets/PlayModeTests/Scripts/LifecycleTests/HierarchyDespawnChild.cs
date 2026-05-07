using PurrNet;

public class HierarchyDespawnChild : NetworkIdentity
{
    public static HierarchyDespawnChild LocalInstance;

    public static int OnDespawnedNoArg;
    public static int OnDespawnedServer;
    public static int OnDespawnedClient;

    // Captured at the time OnDespawned(asServer:true) fires on the child. Used to assert the
    // child's despawn fires while the parent is still being torn down (parent.isSpawned should
    // already be false by the time child callbacks run, since the despawn cascade processes
    // the parent first, but the child must still see them).
    public static bool ChildSawParentBeforeAnyCallback;

    public static void ResetAll()
    {
        LocalInstance = null;
        OnDespawnedNoArg = 0;
        OnDespawnedServer = 0;
        OnDespawnedClient = 0;
        ChildSawParentBeforeAnyCallback = false;
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        if (HierarchyDespawnParent.LocalInstance != null)
            ChildSawParentBeforeAnyCallback = true;
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
}

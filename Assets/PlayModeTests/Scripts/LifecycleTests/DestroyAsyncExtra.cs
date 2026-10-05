using PurrNet;

public class DestroyAsyncExtra : NetworkIdentity
{
    public static int OnDespawnedNoArg;
    public static int Destroyed;

    public static void ResetAll()
    {
        OnDespawnedNoArg = 0;
        Destroyed = 0;
    }

    protected override void OnDespawned()
    {
        OnDespawnedNoArg++;
    }

    protected override void OnDestroy()
    {
        Destroyed++;
        base.OnDestroy();
    }
}

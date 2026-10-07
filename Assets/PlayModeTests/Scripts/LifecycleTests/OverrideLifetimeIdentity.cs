using PurrNet;

public class OverrideLifetimeIdentity : NetworkIdentity
{
    public static OverrideLifetimeIdentity ServerInstance;
    public static int ClientSpawned;
    public static int ClientDespawned;

    public static void ResetAll()
    {
        ServerInstance = null;
        ClientSpawned = 0;
        ClientDespawned = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            ServerInstance = this;
        else ClientSpawned++;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
        {
            if (ServerInstance == this)
                ServerInstance = null;
        }
        else ClientDespawned++;
    }
}

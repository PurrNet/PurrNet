using PurrNet;

/// <summary>
/// Identity given to the victim player to verify the owned-identity distance fallback.
/// </summary>
public class NetworkLODOwnedAnchor : NetworkIdentity
{
    public static NetworkLODOwnedAnchor localInstance;

    public static void ResetAll() => localInstance = null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => localInstance = this;

    protected override void OnDespawned()
    {
        if (localInstance == this)
            localInstance = null;
    }
}

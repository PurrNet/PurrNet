using PurrNet;

/// <summary>The referent that a <see cref="SyncLazyRefCarrierIdentity"/> points at.</summary>
public class SyncLazyRefTargetIdentity : NetworkIdentity
{
    public static SyncLazyRefTargetIdentity LocalInstance;

    public static void ResetAll() => LocalInstance = null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;
}

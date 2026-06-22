using PurrNet;
using UnityEngine;

// Root of the pooled multi-child prefab spawned/despawned by PooledHierarchyRespawnScenario.
public class PooledHierarchyRespawnRoot : NetworkIdentity
{
    public static PooledHierarchyRespawnRoot LocalInstance;

    public static void ResetAll()
    {
        LocalInstance = null;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        LocalInstance = this;
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}

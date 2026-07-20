using PurrNet;
using UnityEngine;

// Root of the multi-child prefab spawned/despawned each cycle by HierarchyRespawnScenario.
public class HierarchyRespawnRoot : NetworkIdentity
{
    public static HierarchyRespawnRoot LocalInstance;

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

    // Server-side: destroy the one designated leaf child so the next respawn must bring it back
    // fresh — a destroyed child must not linger as an id=0 orphan that aborts a later spawn batch.
    public void DestroyDisposableChild()
    {
        var children = GetComponentsInChildren<HierarchyRespawnChild>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].isDisposable)
            {
                Destroy(children[i].gameObject);
                return;
            }
        }
    }
}

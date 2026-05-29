using PurrNet;
using UnityEngine;

// Root for SameFrameChildDestroyScenario (own type, own statics).
public class SameFrameDestroyRoot : NetworkIdentity
{
    public static SameFrameDestroyRoot LocalInstance;

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

    // Server-side: destroy every disposable leaf child, mirroring "Instantiate(prefab);
    // Destroy(some children);" before the spawn settles.
    public int DestroyAllDisposableChildren()
    {
        var children = GetComponentsInChildren<SameFrameDestroyChild>(true);
        int destroyed = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].isDisposable)
            {
                Destroy(children[i].gameObject);
                destroyed++;
            }
        }
        return destroyed;
    }
}

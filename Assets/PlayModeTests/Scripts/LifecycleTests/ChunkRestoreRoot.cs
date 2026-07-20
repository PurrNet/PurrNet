using PurrNet;
using UnityEngine;

// Root for ChunkRestoreScenario (own type/statics).
public class ChunkRestoreRoot : NetworkIdentity
{
    public static ChunkRestoreRoot LocalInstance;

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

    // Server-side: re-apply the "saved removal" by destroying every disposable leaf, mirroring a
    // chunk that instantiates the pristine prefab then strips the children its saved state says
    // are gone.
    public int DestroyAllDisposableChildren()
    {
        var children = GetComponentsInChildren<ChunkRestoreChild>(true);
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

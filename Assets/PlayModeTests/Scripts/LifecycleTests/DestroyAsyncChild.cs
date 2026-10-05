using PurrNet;
using UnityEngine;

public class DestroyAsyncChild : NetworkIdentity
{
    public static DestroyAsyncChild First;
    public static DestroyAsyncChild Second;
    public static int SpawnedCount;
    public static int OnDespawnedNoArg;
    public static int DespawnFrame;
    public static int DestroyFrame;
    public static bool DetachedAtDestroy;

    public static void ResetAll()
    {
        First = null;
        Second = null;
        SpawnedCount = 0;
        OnDespawnedNoArg = 0;
        DespawnFrame = -1;
        DestroyFrame = -1;
        DetachedAtDestroy = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        SpawnedCount++;

        if (SpawnedCount == 1)
            First = this;
        else Second = this;
    }

    protected override void OnDespawned()
    {
        if (!ReferenceEquals(First, this))
            return;

        OnDespawnedNoArg++;
        DespawnFrame = Time.frameCount;
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(First, this))
        {
            DestroyFrame = Time.frameCount;
            DetachedAtDestroy = !transform.parent;
        }

        base.OnDestroy();
    }
}

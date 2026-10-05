using PurrNet;
using UnityEngine;

public class DestroyAsyncRoot : NetworkIdentity
{
    public static DestroyAsyncRoot LocalInstance;
    public static int ServerReadyCount;
    public static int OnDespawnedNoArg;
    public static int DespawnFrame;
    public static int DestroyFrame;
    public static int ChildrenAtDestroy;
    public static bool ActiveAtDestroy;
    public static bool FlaggedAtDestroy;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        OnDespawnedNoArg = 0;
        DespawnFrame = -1;
        DestroyFrame = -1;
        ChildrenAtDestroy = -1;
        ActiveAtDestroy = false;
        FlaggedAtDestroy = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnDespawned()
    {
        OnDespawnedNoArg++;
        DespawnFrame = Time.frameCount;
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(LocalInstance, this))
        {
            DestroyFrame = Time.frameCount;
            ChildrenAtDestroy = transform.childCount;
            ActiveAtDestroy = gameObject.activeSelf;
            FlaggedAtDestroy = isDestroyingAsync;
        }

        base.OnDestroy();
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;
}

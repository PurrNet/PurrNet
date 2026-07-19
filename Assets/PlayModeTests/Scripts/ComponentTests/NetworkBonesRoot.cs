using PurrNet;
using UnityEngine;

public class NetworkBonesRoot : NetworkIdentity
{
    public static NetworkBonesRoot LocalInstance;

    public NetworkBones networkBones;
    public Transform[] skinnedBones;
    public Transform extraBone;

    public static void ResetAll() => LocalInstance = null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => LocalInstance = this;

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}

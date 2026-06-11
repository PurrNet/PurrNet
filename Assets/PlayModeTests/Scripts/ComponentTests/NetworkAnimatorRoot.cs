using PurrNet;
using UnityEngine;

public class NetworkAnimatorRoot : NetworkIdentity
{
    public static NetworkAnimatorRoot LocalInstance;

    public Animator animator;
    public NetworkAnimator networkAnimator;

    public static void ResetAll() => LocalInstance = null;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned() => LocalInstance = this;

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}

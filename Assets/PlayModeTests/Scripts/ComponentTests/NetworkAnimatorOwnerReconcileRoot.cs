using PurrNet;
using UnityEngine;

public class NetworkAnimatorOwnerReconcileRoot : NetworkIdentity
{
    public static NetworkAnimatorOwnerReconcileRoot LocalInstance;
    public static float ServerWeightBeforeOwnership;
    public static float OwnerWeightBeforePostSpawnSetup;
    public static bool OwnerAppliedPostSpawnSetup;

    public Animator animator;
    public NetworkAnimator networkAnimator;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerWeightBeforeOwnership = float.NaN;
        OwnerWeightBeforePostSpawnSetup = float.NaN;
        OwnerAppliedPostSpawnSetup = false;
    }

    protected override void OnEarlySpawn(bool asServer)
    {
        gameObject.SetActive(true);
        animator.Update(0f);

        if (asServer)
        {
            animator.SetLayerWeight(1, 0f);
            ServerWeightBeforeOwnership = animator.GetLayerWeight(1);
        }
    }

    protected override void OnSpawned()
    {
        LocalInstance = this;

        if (!isOwner)
            return;

        OwnerWeightBeforePostSpawnSetup = animator.GetLayerWeight(1);
        animator.SetLayerWeight(1, 0f);
        OwnerAppliedPostSpawnSetup = true;
    }

    protected override void OnDespawned()
    {
        if (LocalInstance == this)
            LocalInstance = null;
    }
}

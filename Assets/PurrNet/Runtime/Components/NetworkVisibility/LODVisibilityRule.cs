using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Hides identities whose LOD tier for the player is culled.
    /// Only applies to identities with a <see cref="NetworkLOD"/> component whose profile
    /// has cullBeyondLastTier enabled; everything else stays visible.
    /// </summary>
    [CreateAssetMenu(menuName = "PurrNet/NetworkVisibility/LOD Rule")]
    public class LODVisibilityRule : NetworkVisibilityRule
    {
        public override int complexity => 10;

        public override bool CanSee(PlayerID player, NetworkIdentity target)
        {
            var lod = target.networkLOD;

            if (!lod || !lod.profile || !lod.profile.cullBeyondLastTier)
                return true;

            return !lod.IsCulled(player);
        }
    }
}

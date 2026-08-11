using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(menuName = "PurrNet/NetworkVisibility/DistanceRule")]
    public class DistanceRule : NetworkVisibilityRule
    {
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField, Min(0)] private float _distance = 30f;
        [SerializeField, Min(0)] private float _deadZone = 5f;

        public override int complexity => 100;

        public override bool CanSee(PlayerID player, NetworkIdentity networkIdentity)
        {
            var myPos = networkIdentity.transform.position;
            bool wasPreviouslyVisible = networkIdentity.IsObserverOrPending(player);

            foreach (var playerIdentity in manager.EnumerateAllPlayerOwnedIds(player, true))
            {
                var layer = playerIdentity.layer;

                if ((_layerMask & (1 << layer)) == 0)
                    continue;

                if (!playerIdentity.isActiveAndEnabled)
                    continue;

                var playerPos = playerIdentity.transform.position;
                var distance = Vector3.Distance(myPos, playerPos);

                if (wasPreviouslyVisible)
                {
                    if (!(distance <= _distance + _deadZone))
                        continue;
                }
                else if (!(distance <= _distance))
                    continue;

                return true;
            }

            return false;
        }
    }
}

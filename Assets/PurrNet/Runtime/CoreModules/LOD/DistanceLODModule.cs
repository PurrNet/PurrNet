using System.Collections.Generic;
using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.LOD
{

    /// <summary>
    /// Responsible for determining network LOD based on distance.
    /// </summary>
    [System.Serializable]
    public abstract class DistanceLODModule : NetworkLODModule
    {
        
        [System.Serializable]
        public struct LODTier
        {
            public float maxDistance;

            [Min(0f)] public float sendInterval;

            public LODTier(float maxDistance, float sendInterval)
            {
                this.maxDistance = maxDistance;
                this.sendInterval = sendInterval;
            }
        }

        [Tooltip("These should be in order of ascending distance. ")]
        public LODTier[] tiers = new[]
        {
            new LODTier(15f, 0f),
            new LODTier(40f, 0.1f),
            new LODTier(80f, 0.5f),
            new LODTier(float.MaxValue, 2f),
        };

        private GlobalOwnershipModule _globalOwnershipModule;

        /// <summary>
        /// We cache the nearest transform since target RPCs are sent per PlayerID
        /// </summary>
        private readonly Dictionary<PlayerID, Transform> _nearestObserverTransforms = new();

        public override void OnSpawn(bool asServer)
        {
            if (asServer)
                networkManager.TryGetModule(true, out _globalOwnershipModule);
        }

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            base.OnObserverAdded(player, isSpawner);

            _nearestObserverTransforms[player] = null;
        }

        public override void OnObserverRemoved(PlayerID player)
        {
            base.OnObserverRemoved(player);

            _nearestObserverTransforms.Remove(player);
        }

        public override void OnDespawned()
        {
            base.OnDespawned();

            _nearestObserverTransforms.Clear();
            _globalOwnershipModule = null;
        }

        protected override float GetSendInterval(PlayerID observer)
        {
            if (!_nearestObserverTransforms.TryGetValue(observer, out var t) || !t)
            {
                t = FindNearestOwnedTransform(observer);
                _nearestObserverTransforms[observer] = t;
            }

            if (!t)
                return tiers[^1].sendInterval;

            float dist = Vector3.Distance(parent.transform.position, t.position);
            return GetTierForDistance(dist).sendInterval;
        }

        private Transform FindNearestOwnedTransform(PlayerID player)
        {
            var pos = parent.transform.position;
            Transform nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var nid in _globalOwnershipModule.EnumerateAllPlayerOwnedIds(player))
            {
                float dist = Vector3.Distance(pos, nid.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = nid.transform;
                }
            }

            return nearest;
        }

        private LODTier GetTierForDistance(float distance)
        {
            for (int i = 0; i < tiers.Length - 1; i++)
            {
                var tierForDistance = tiers[i];
                if (distance <= tierForDistance.maxDistance)
                {
                    return tierForDistance;
                }
            }
            
            return tiers[^1];
        }

    }

}
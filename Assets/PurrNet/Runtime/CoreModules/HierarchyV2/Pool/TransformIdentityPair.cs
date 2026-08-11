using System.Collections.Generic;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public readonly struct TransformIdentityPair
    {
        public readonly Transform transform;
        public readonly NetworkIdentity identity;

        public TransformIdentityPair(Transform transform, NetworkIdentity identity)
        {
            this.transform = transform;
            this.identity = identity;
        }

        public bool HasObserver(PlayerID playerID)
        {
            bool hasObserver = false;
            var components = ListPool<NetworkIdentity>.Instantiate();

            transform.GetComponents(components);
            var count = components.Count;

            for (var i = 0; i < count; i++)
            {
                if (components[i].IsObserverOrPending(playerID))
                {
                    hasObserver = true;
                    break;
                }
            }

            ListPool<NetworkIdentity>.Destroy(components);
            return hasObserver;
        }
    }
}

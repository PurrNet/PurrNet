using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct MinimalIdentityHeader : IPackedAuto, IEquatable<MinimalIdentityHeader>
    {
        public const int MAX_SIZE = 6 + 6 + 6;

        public NetworkID networkId;
        public SceneID sceneId;

        public bool Equals(MinimalIdentityHeader other)
        {
            return networkId.id.value == other.networkId.id.value && networkId.scope.Equals(other.networkId.scope) &&
                   sceneId.id.value == other.sceneId.id.value;
        }

        public override bool Equals(object obj)
        {
            return obj is MinimalIdentityHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(networkId, sceneId);
        }
    }
}

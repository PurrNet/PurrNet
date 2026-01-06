using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct MinimalModuleHeader : IPackedAuto, IEquatable<MinimalModuleHeader>
    {
        public Size childId;

        public bool Equals(MinimalModuleHeader other)
        {
            return childId.value == other.childId.value;
        }

        public override bool Equals(object obj)
        {
            return obj is MinimalModuleHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)childId.value;
        }
    }
}

using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct MinimalStaticHeader : IPackedAuto, IEquatable<MinimalStaticHeader>
    {
        public PackedUInt typeHash;

        public bool Equals(MinimalStaticHeader other)
        {
            return typeHash.value == other.typeHash.value;
        }

        public override bool Equals(object obj)
        {
            return obj is MinimalStaticHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return typeHash.GetHashCode();
        }
    }
}
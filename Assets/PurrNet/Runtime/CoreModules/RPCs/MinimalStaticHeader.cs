using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct MinimalStaticHeader : IPackedAuto, IEquatable<MinimalStaticHeader>
    {
        public uint typeHash;

        public bool Equals(MinimalStaticHeader other)
        {
            return typeHash == other.typeHash;
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
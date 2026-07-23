using System;
using UnityEngine;

namespace PurrNet.Packing
{
    public struct PackedQuaternion : IEquatable<PackedQuaternion>
    {
        public NormalizedFloat x;
        public NormalizedFloat y;
        public NormalizedFloat z;
        public NormalizedFloat w;

        public PackedQuaternion(Quaternion value)
        {
            value.Normalize();
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }

        public override string ToString()
        {
            return $"PackedQuaternion({x}, {y}, {z}, {w})";
        }

        public static implicit operator PackedQuaternion(Quaternion value)
        {
            return new PackedQuaternion(value);
        }

        public static implicit operator Quaternion(PackedQuaternion angle) => new Quaternion(
            angle.x.GetValue(), angle.y.GetValue(), angle.z.GetValue(), angle.w.GetValue()
        ).normalized;

        public bool Equals(PackedQuaternion other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        }

        public override bool Equals(object obj)
        {
            return obj is PackedQuaternion other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y, z, w);
        }
    }
}

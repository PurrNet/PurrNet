using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Packing
{
    struct QuaternionEqualityComparer : IEqualityComparer<Quaternion>
    {
        public bool Equals(Quaternion a, Quaternion b)
        {
            return a.x == b.x &&
                   a.y == b.y &&
                   a.z == b.z &&
                   a.w == b.w;
        }

        public int GetHashCode(Quaternion obj)
        {
            unchecked
            {
                int hashCode = obj.x.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.y.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.z.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.w.GetHashCode();
                return hashCode;
            }
        }
    }
}

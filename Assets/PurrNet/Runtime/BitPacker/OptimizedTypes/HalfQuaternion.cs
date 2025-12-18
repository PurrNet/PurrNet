using System;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    public struct HalfQuaternion : IEquatable<HalfQuaternion>, IPackedAuto
    {
        public Half x;
        public Half y;
        public Half z;
        public Half w;

        public HalfQuaternion(Half x, Half y, Half z, Half w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public HalfQuaternion(Quaternion quaternion)
        {
            x = new Half(quaternion.x);
            y = new Half(quaternion.y);
            z = new Half(quaternion.z);
            w = new Half(quaternion.w);
        }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(x, y, z, w);
        }

        public bool Equals(HalfQuaternion other)
        {
            return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
        }

        public void Normalize()
        {
            Quaternion quaternion = this;
            quaternion.Normalize();

            x = new Half(quaternion.x);
            y = new Half(quaternion.y);
            z = new Half(quaternion.z);
            w = new Half(quaternion.w);
        }

        public static implicit operator Quaternion(HalfQuaternion value)
        {
            return value.ToQuaternion();
        }

        public static implicit operator HalfQuaternion(Quaternion value)
        {
            return new HalfQuaternion(value);
        }
    }
}

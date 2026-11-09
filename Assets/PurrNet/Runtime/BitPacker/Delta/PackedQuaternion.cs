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

            int k = Largest();
            long d = DeriveAt(k);
            Set(k, Get(k) < 0 ? -d : d);
        }

        const long ONE = 1L << (NormalizedFloat.BIT_RESOLUTION - 1);

        internal long Get(int i) => i switch { 0 => x.value, 1 => y.value, 2 => z.value, _ => w.value };

        internal void Set(int i, long v)
        {
            switch (i)
            {
                case 0: x.value = v; break;
                case 1: y.value = v; break;
                case 2: z.value = v; break;
                default: w.value = v; break;
            }
        }

        int Largest()
        {
            int k = 0;
            for (int i = 1; i < 4; i++)
            {
                if (Math.Abs(Get(i)) > Math.Abs(Get(k)))
                    k = i;
            }
            return k;
        }

        // The magnitude of component i, rebuilt from the other three.
        internal long DeriveAt(int i)
        {
            long s = ONE * ONE;
            for (int j = 0; j < 4; j++)
            {
                if (j != i)
                    s -= Get(j) * Get(j);
            }
            return s <= 0 ? 0 : Math.Min((long)Math.Round(Math.Sqrt(s)), ONE - 1);
        }

        // A component the others rebuild exactly, -1 if none.
        internal int DerivedIndex()
        {
            int k = Largest();
            if (Math.Abs(Get(k)) == DeriveAt(k))
                return k;
            for (int i = 0; i < 4; i++)
            {
                if (i != k && Math.Abs(Get(i)) == DeriveAt(i))
                    return i;
            }
            return -1;
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

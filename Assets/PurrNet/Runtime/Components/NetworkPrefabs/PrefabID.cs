using System;
using PurrNet.Packing;

namespace PurrNet
{
    public readonly struct PrefabID : IEquatable<PrefabID>, IPackedAuto
    {
        public readonly PackedInt value;

        public static PrefabID invalid => new PrefabID(-1);

        public bool isValid => value >= 0;

        public PrefabID(int value)
        {
            this.value = value;
        }

        public static implicit operator PrefabID(int value) => new PrefabID(value);

        public static explicit operator int(PrefabID id) => id.value;

        public bool Equals(PrefabID other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is PrefabID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value;
        }

        public static bool operator ==(PrefabID a, PrefabID b) => a.value == b.value;

        public static bool operator !=(PrefabID a, PrefabID b) => a.value != b.value;

        public override string ToString()
        {
            return value.ToString();
        }
    }
}

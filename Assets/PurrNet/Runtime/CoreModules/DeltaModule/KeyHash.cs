using System;

namespace PurrNet.Modules
{
    internal readonly struct KeyHash : IEquatable<KeyHash>
    {
        public readonly Type type;
        public readonly uint hash;

        public KeyHash(
            Type type,
            uint hash)
        {
            this.type = type;
            this.hash = hash;
        }

        public bool Equals(KeyHash other)
        {
            return type == other.type && hash == other.hash;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyHash other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(type, hash);
        }
    }
}

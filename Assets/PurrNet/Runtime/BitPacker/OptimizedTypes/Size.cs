using System;

namespace PurrNet.Packing
{
    [Serializable]
    public struct Size : IEquatable<Size>, IPackedAuto
    {
        public uint value;

        public Size(uint value)
        {
            this.value = value;
        }

        public static implicit operator Size(ushort value) => new Size(value);

        public static implicit operator Size(uint value) => new Size(value);

        public static implicit operator uint(Size value) => value.value;

        public static implicit operator int(Size value) => (int)value.value;

        public static implicit operator Size(long value) => new Size((uint)value);

        public static implicit operator Size(int value) => new Size((uint)value);

        public bool Equals(Size other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is Size other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return $"{value}";
        }
    }
}

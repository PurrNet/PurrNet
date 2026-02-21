using System.Text;
using PurrNet.Packing;

namespace PurrNet
{
    public static class StringUTF8Extensions
    {
        public static void Write(this BitPacker packer, StringUTF8 value)
        {
            packer.WriteString(Encoding.UTF8, value.value);
        }

        public static void Read(this BitPacker packer, ref StringUTF8 value)
        {
            value = new StringUTF8(packer.ReadString(Encoding.UTF8));
        }
    }

    public readonly struct StringUTF8 : IPackedAuto
    {
        public readonly string value;

        public StringUTF8(string value)
        {
            this.value = value;
        }

        public override string ToString()
        {
            return value;
        }

        public bool Equals(StringUTF8 other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is StringUTF8 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public static implicit operator string(StringUTF8 value)
        {
            return value.value;
        }

        public static implicit operator StringUTF8(string value)
        {
            return new StringUTF8(value);
        }
    }
}

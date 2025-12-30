using System;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class PackIntegers
    {
        [UsedByIL]
        public static void Write(this BitPacker packer, long value)
        {
            packer.WriteBits((ulong)value, 64);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref long value)
        {
            value = (long)packer.ReadBits(64);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, int value)
        {
            packer.WriteBits((ulong)value, 32);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref int value)
        {
            value = (int)packer.ReadBits(32);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, short value)
        {
            packer.WriteBits((ulong)value, 16);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref short value)
        {
            value = (short)packer.ReadBits(16);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, sbyte value)
        {
            packer.WriteBits((ulong)value, 8);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref sbyte value)
        {
            value = (sbyte)packer.ReadBits(8);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, bool value)
        {
            packer.WriteBit(value);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref bool value)
        {
            value = packer.ReadBit();
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, TimeSpan value)
        {
            packer.WriteBits((ulong)value.Ticks, 64);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref TimeSpan value)
        {
            value = new TimeSpan((long)packer.ReadBits(64));
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, DateTime value)
        {
            packer.WriteBits((ulong)value.Ticks, 64);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref DateTime value)
        {
            value = new DateTime((long)packer.ReadBits(64));
        }
    }
}

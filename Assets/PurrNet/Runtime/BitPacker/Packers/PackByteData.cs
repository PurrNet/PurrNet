using System;
using PurrNet.Modules;
using PurrNet.Transports;

namespace PurrNet.Packing
{
    public static class PackByteData
    {
        [UsedByIL]
        public static void Write(this BitPacker packer, ByteData data)
        {
            Packer<Size>.Write(packer, data.length);
            packer.WriteBytes(data.span);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref ByteData data)
        {
            Size length = default;
            Packer<Size>.Read(packer, ref length);

            if (length.value == 0)
            {
                data = new ByteData(Array.Empty<byte>(), 0, 0);
                return;
            }

            byte[] buffer = new byte[length];
            packer.ReadBytes(buffer);
            data = new ByteData(buffer, 0, (int)length.value);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, BitPacker data)
        {
            if (data == null)
            {
                Write(packer, new ByteData());
                return;
            }

            Write(packer, data.ToByteData());
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref BitPacker data)
        {
            Size length = default;
            Packer<Size>.Read(packer, ref length);

            if (data == null)
                data = BitPackerPool.Get();
            else data.ResetPositionAndMode(false);

            var dest = data.GetSpan(length);
            packer.ReadBytes(dest);
            data.ResetPositionAndMode(true);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, BitPackerWithLength data)
        {
            Write(packer, data.packer.ToByteData());
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref BitPackerWithLength data)
        {
            Size length = default;
            Packer<Size>.Read(packer, ref length);

            var dataPacker = BitPackerPool.Get();
            var span = dataPacker.GetSpan(length);
            packer.ReadBytes(span);
            dataPacker.ResetPosition();

            data = new BitPackerWithLength(length, dataPacker);
        }

        [UsedByIL]
        public static void Write(this BitPacker packer, BitData data)
        {
            Packer<Size>.Write(packer, data.bitLength);
            packer.WriteBitDataWithoutConsumingIt(data);
        }

        [UsedByIL]
        public static void Read(this BitPacker packer, ref BitData data)
        {
            Size length = default;
            Packer<Size>.Read(packer, ref length);
            int lengthInt = (int)length.value;
            int origin = packer.AdvanceBits(lengthInt);
            data = new BitData(packer, origin, lengthInt);
            packer.EnsurePadding();
        }
    }
}

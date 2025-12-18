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

            data = BitPackerPool.Get();
            packer.ReadBytes(data, length);
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
            packer.ReadBytes(dataPacker, length);
            dataPacker.ResetPosition();

            data = new BitPackerWithLength(length, dataPacker);
        }
    }
}

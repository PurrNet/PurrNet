using System;
using System.Buffers;

namespace PurrNet.Packing
{
    public readonly struct BitPackerWrapper : IBufferWriter<byte>, IDisposable
    {
        public readonly BitPacker packer;

        public BitPackerWrapper(BitPacker packer)
        {
            this.packer = packer;
        }

        public void Advance(int count)
        {
            packer.AdvanceBits(count * 8);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            packer.EnsureBitsExist(sizeHint * 8);
            return new Memory<byte>(packer.buffer, packer.positionInBytes, sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            packer.EnsureBitsExist(sizeHint * 8);
            return new Span<byte>(packer.buffer, packer.positionInBytes, sizeHint);
        }

        public void Dispose()
        {
            packer?.Dispose();
        }
    }
}
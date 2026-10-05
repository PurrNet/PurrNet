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
            packer.AdvanceBytes(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            return packer.GetMemory(sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return packer.GetSpan(sizeHint);
        }

        public void Dispose()
        {
            packer?.Dispose();
        }
    }
}

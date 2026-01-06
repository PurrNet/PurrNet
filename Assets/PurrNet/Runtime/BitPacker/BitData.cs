using System;

namespace PurrNet.Packing
{
    public struct BitData : IDisposable
    {
        public BitPacker packer;
        public Size bitLength;

        public BitData(BitPacker packer)
        {
            this.packer = packer;
            this.bitLength = packer.positionInBits;
        }

        public void Dispose()
        {
            packer.Dispose();
        }

        public void UpdateLength()
        {
            bitLength = packer.positionInBits;
        }

        public ArraySegment<byte> AsSegment()
        {
            return packer.AsSegment((int)bitLength.value);
        }
    }
}

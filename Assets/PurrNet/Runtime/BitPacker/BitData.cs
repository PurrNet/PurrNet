using System;

namespace PurrNet.Packing
{
    public struct BitData : IDisposable
    {
        public readonly BitPacker packer;
        public Size bitOrigin;
        public Size bitLength;

        public int byteLength
        {
            get
            {
                int bitPos = (int)bitLength.value;
                int pos = bitPos / 8;
                int len = pos + (bitPos % 8 == 0 ? 0 : 1);
                return len;
            }
        }

        public BitData(BitPacker packer)
        {
            this.packer = packer;
            this.bitOrigin = 0;
            this.bitLength = packer.positionInBits;
        }

        public BitData(BitPacker packer, int bitOrigin, int bitLength)
        {
            this.packer = packer;
            this.bitOrigin = bitOrigin;
            this.bitLength = bitLength;
        }

        public void Dispose()
        {
            packer.Dispose();
        }

        public BitDataScope AutoScope()
        {
            return new BitDataScope(this);
        }
    }
}

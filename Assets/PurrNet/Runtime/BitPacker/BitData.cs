using System;

namespace PurrNet.Packing
{
    public struct BitData : IDisposable, IDuplicate<BitData>
    {
        public readonly BitPacker packer;
        public Size bitOrigin;
        public Size bitLength;

        public int byteLength => ((int)bitLength.value + 7) >> 3;

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
            packer?.Dispose();
        }

        public BitDataScope AutoScope()
        {
            return new BitDataScope(this);
        }

        public BitData Duplicate()
        {
            if (bitLength.value == 0 || packer == null)
                return default;
            var newpacker = BitPackerPool.Get();
            newpacker.Write(this);
            return new BitData(newpacker);
        }
    }
}

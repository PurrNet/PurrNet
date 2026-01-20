using System;

namespace PurrNet.Packing
{
    public readonly struct BitPackerWithLength : IDisposable
    {
        public readonly int originalLength;
        public readonly BitPacker packer;

        public BitPackerWithLength(int ogLength, BitPacker packer)
        {
            originalLength = ogLength;
            this.packer = packer;
        }

        public void Dispose()
        {
            packer.Dispose();
        }
    }
}
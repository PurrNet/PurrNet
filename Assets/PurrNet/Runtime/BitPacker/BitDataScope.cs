using System;

namespace PurrNet.Packing
{
    public readonly struct BitDataScope : IDisposable
    {
        private readonly int lastBitPosition;
        private readonly BitPacker packer;

        public BitDataScope(BitData data)
        {
            if (data.packer == null)
            {
                lastBitPosition = 0;
                packer = default;
                return;
            }

            packer = data.packer;
            lastBitPosition = packer.positionInBits;
            packer.SetBitPosition(data.bitOrigin);
        }

        public void Dispose()
        {
            packer?.SetBitPosition(lastBitPosition);
        }
    }
}

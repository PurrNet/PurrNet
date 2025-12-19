using System;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Packing
{
    public class BitPackerPool : GenericPool<BitPacker>
    {
        [ThreadStatic]
        private static readonly BitPackerPool _instance;

        [ThreadStatic]
        private static readonly BitPackerPool _instanceTmp;

        static BitPackerPool()
        {
            _instance = new BitPackerPool();
            _instanceTmp = new BitPackerPool();
        }

        static BitPacker Factory() => new BitPacker();

        static void Reset(BitPacker list) => list.ResetPosition();

        private BitPackerPool() : base(Factory, Reset) {}

        public static BitPacker Get(bool readMode = false)
        {
            var packer = _instance.Allocate();
            packer.ResetMode(readMode);
            return packer;
        }

        public static void Free(BitPacker packer)
        {
            if (packer.isWrapper)
                _instanceTmp.Delete(packer);
            else _instance.Delete(packer);
        }

        public static BitPacker Get(byte[] from)
        {
            return Get(new ByteData(from, 0, from.Length));
        }

        public static BitPacker Get(ByteData from)
        {
            var packer = _instanceTmp.Allocate();
            packer.ResetMode(true);
            packer.MakeWrapper(from);
            return packer;
        }
    }
}

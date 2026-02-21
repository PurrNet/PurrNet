using System;
using PurrNet.Packing;
using PurrNet.Pooling;

namespace PurrNet.Modules
{
    internal struct DeltaAcknowledgeBatch : IPackedAuto, IDisposable
    {
        public PlayerID playerId;
        public DisposableList<DeltaAcknowledge> entries;

        public void Dispose()
        {
            entries.Dispose();
        }
    }

    internal struct DeltaBatch : IPackedAuto
    {
        public PackedInt ogBitCount;
        public PackedInt dataBitCount;
        public BitPacker data;
    }

    internal struct DeltaAcknowledge : IPackedAuto
    {
        public PackedUInt keyType;
        public PackedUInt keyHash;
        public PackedUInt valueId;
    }

    internal struct DeltaCleanup : IPackedAuto
    {
        public PackedUInt keyType;
        public PackedUInt keyHash;
        public PackedUInt upToId;
    }

    public interface IStableHashable
    {
        uint GetStableHash();
    }
}

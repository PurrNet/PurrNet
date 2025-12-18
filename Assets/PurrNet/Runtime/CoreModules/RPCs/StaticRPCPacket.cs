using System;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public struct StaticRPCHeader : IPackedAuto, IEquatable<StaticRPCHeader>
    {
        public PackedUInt typeHash;
        public Size rpcId;
        public PlayerID senderId;
        public PlayerID? targetId;

        public override string ToString()
        {
            return $"NetworkModuleRPCHeader: {{ typeHash: {typeHash}, rpcId: {rpcId}, senderId: {senderId}, targetId: {targetId} }}";
        }

        public bool Equals(StaticRPCHeader other)
        {
            return typeHash.Equals(other.typeHash) &&
                   rpcId.Equals(other.rpcId) &&
                   senderId.Equals(other.senderId) &&
                   Nullable.Equals(targetId, other.targetId);
        }

        public override bool Equals(object obj)
        {
            return obj is StaticRPCHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(typeHash, rpcId, senderId, targetId);
        }
    }

    public struct StaticRPCPacket : IPackedAuto, IRpc
    {
        public StaticRPCHeader header;
        [DontDeltaCompress] public ByteData data;

        public ByteData rpcData
        {
            get { return data; }
            set { data = value; }
        }

        public PlayerID senderPlayerId => header.senderId;
        public PlayerID targetPlayerId
        {
            get => header.targetId ?? default;
            set => header.targetId = value;
        }

        public uint GetStableHeaderHash()
        {
            ulong nid = header.typeHash.value;
            ulong rpc = header.rpcId.value;

            ulong hash = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;

            hash ^= Hasher<StaticRPCPacket>.stableHash;
            hash *= prime;

            hash ^= nid;
            hash *= prime;

            hash ^= rpc;
            hash *= prime;

            return (uint)(hash ^ (hash >> 32));
        }
    }
}

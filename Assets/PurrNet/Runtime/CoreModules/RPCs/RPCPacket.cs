using System;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Utils;

namespace PurrNet
{
    public struct RPCPacket : IPackedAuto, IRpc
    {
        public NetworkIdentityRPCHeader header;
        [DontDeltaCompress, UsedByIL] public BitData data;

        public BitData rpcData
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
            ulong nid = header.networkId.id.value;
            ulong nscope = header.networkId.scope.id.value;
            ulong sceneScope = header.sceneId.id.value;
            ulong rpc = header.rpcId.value;

            ulong hash = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;

            hash ^= Hasher<RPCPacket>.stableHash;
            hash *= prime;

            hash ^= nid;
            hash *= prime;

            hash ^= nscope;
            hash *= prime;

            hash ^= sceneScope;
            hash *= prime;

            hash ^= rpc;
            hash *= prime;

            return (uint)(hash ^ (hash >> 32));
        }
    }

    internal readonly struct RPC_ID : IEquatable<RPC_ID>
    {
        public readonly uint typeHash;
        public readonly SceneID sceneId;
        public readonly NetworkID networkId;
        private readonly Size rpcId;
        private readonly Size childId;

        public RPC_ID(RPCPacket packet)
        {
            sceneId = packet.header.sceneId;
            networkId = packet.header.networkId;
            rpcId = packet.header.rpcId;
            typeHash = default;
            childId = default;
        }

        public RPC_ID(StaticRPCPacket packet)
        {
            sceneId = default;
            networkId = default;
            rpcId = packet.header.rpcId;
            typeHash = packet.header.typeHash;
            childId = default;
        }

        public RPC_ID(ChildRPCPacket packet)
        {
            sceneId = packet.header.sceneId;
            networkId = packet.header.networkId;
            rpcId = packet.header.rpcId;
            typeHash = default;
            childId = packet.header.childId;
        }

        public override int GetHashCode()
        {
            return sceneId.GetHashCode() ^
                   networkId.GetHashCode() ^
                   rpcId.GetHashCode() ^
                   typeHash.GetHashCode() ^
                   childId.GetHashCode();
        }

        public bool Equals(RPC_ID other)
        {
            return typeHash == other.typeHash &&
                   sceneId.Equals(other.sceneId) &&
                   networkId.Equals(other.networkId) &&
                   rpcId == other.rpcId &&
                   childId == other.childId;
        }

        public override bool Equals(object obj)
        {
            return obj is RPC_ID other && Equals(other);
        }
    }

    internal class RPC_DATA_BASE<T>
    {
        public RPC_ID rpcid;
        public T header;
        public RPCSignature sig;
        public BitPacker stream;
    }

    internal class RPC_DATA
    {
        public RPC_ID rpcid;
        public NetworkIdentityRPCHeader header;
        public RPCSignature sig;
        public BitPacker stream;
    }

    internal class CHILD_RPC_DATA
    {
        public RPC_ID rpcid;
        public NetworkModuleRPCHeader header;
        public RPCSignature sig;
        public BitPacker stream;
    }

    internal class STATIC_RPC_DATA
    {
        public RPC_ID rpcid;
        public StaticRPCHeader header;
        public RPCSignature sig;
        public BitPacker stream;
    }
}

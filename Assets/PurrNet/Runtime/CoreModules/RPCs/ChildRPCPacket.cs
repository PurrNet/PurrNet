using System;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public struct NetworkModuleRPCHeader : IPackedAuto, IEquatable<NetworkModuleRPCHeader>
    {
        public NetworkID networkId;
        public SceneID sceneId;
        public PlayerID senderId;
        public PlayerID? targetId;
        public Size rpcId;
        public Size childId;

        public override string ToString()
        {
            return $"NetworkModuleRPCHeader: {{ sceneId: {sceneId}, networkId: {networkId}, senderId: {senderId}, targetId: {targetId}, childId: {childId}, rpcId: {rpcId} }}";
        }

        public bool Equals(NetworkModuleRPCHeader other)
        {
            return networkId.Equals(other.networkId) &&
                   sceneId.Equals(other.sceneId) &&
                   senderId.Equals(other.senderId) &&
                   Nullable.Equals(targetId, other.targetId) &&
                   rpcId.Equals(other.rpcId) &&
                   childId.Equals(other.childId);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkModuleRPCHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(networkId, sceneId, senderId, targetId, rpcId, childId);
        }
    }

    public struct ChildRPCPacket : IPackedAuto, IRpc
    {
        public NetworkModuleRPCHeader header;
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
            ulong nid = header.networkId.id.value;
            ulong nscope = header.networkId.scope.id.value;
            ulong sceneScope = header.sceneId.id.value;
            ulong rpc = header.rpcId.value;

            ulong hash = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;

            hash ^= Hasher<ChildRPCPacket>.stableHash;
            hash *= prime;

            hash ^= nid;
            hash *= prime;

            hash ^= nscope;
            hash *= prime;

            hash ^= sceneScope;
            hash *= prime;

            hash ^= rpc;
            hash *= prime;

            hash ^= header.childId.value;
            hash *= prime;

            return (uint)(hash ^ (hash >> 32));
        }
    }
}

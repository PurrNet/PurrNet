using PurrNet.Packing;
using PurrNet.Utils;

namespace PurrNet.Modules
{
    internal readonly struct NetworkIdentityRpcHash<T, PACKET> : IStableHashable
    {
        private readonly NetworkID id;
        private readonly SceneID scene;
        private readonly PackedUInt typeId;
        private readonly Size childId;
        private readonly Size rpcId;
        private readonly ulong offset;

        public NetworkIdentityRpcHash(RPCPacket context, ulong offset)
        {
            id = context.header.networkId;
            scene = context.header.sceneId;
            this.typeId = default;
            this.childId = default;
            this.rpcId = context.header.rpcId;
            this.offset = offset;
        }

        public NetworkIdentityRpcHash(ChildRPCPacket context, ulong offset)
        {
            id = context.header.networkId;
            scene = context.header.sceneId;
            this.typeId = default;
            this.childId = context.header.childId;
            this.rpcId = context.header.rpcId;
            this.offset = offset;
        }

        public NetworkIdentityRpcHash(StaticRPCPacket context, ulong offset)
        {
            id = default;
            scene = default;
            this.typeId = context.header.typeHash;
            this.childId = default;
            this.rpcId = context.header.rpcId;
            this.offset = offset;
        }

        public uint GetStableHash()
        {
            ulong nid = id.id.value;
            ulong nscope = id.scope.id.value;
            ulong sceneScope = scene.id.value;
            ulong rpc = rpcId.value;

            ulong hash = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;

            hash ^= Hasher<T>.stableHash;
            hash *= prime;

            hash ^= Hasher<PACKET>.stableHash;
            hash *= prime;

            hash ^= nid;
            hash *= prime;

            hash ^= nscope;
            hash *= prime;

            hash ^= sceneScope;
            hash *= prime;

            hash ^= rpc;
            hash *= prime;

            hash ^= typeId.value;
            hash *= prime;

            hash ^= childId.value;
            hash *= prime;

            hash ^= offset;
            hash *= prime;

            return (uint)(hash ^ (hash >> 32));
        }
    }
}

using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct UnionRPCHeader : IPackedAuto, IEquatable<UnionRPCHeader>
    {
        public const int MAX_SIZE = 6 + 6 + 6 + MinimalIdentityHeader.MAX_SIZE;

        public MinimalIdentityHeader? identityRpc;
        public MinimalModuleHeader? moduleRpc;
        public MinimalStaticHeader? staticRpc;

        public Size rpcId;
        public PlayerID senderId;
        public PlayerID? targetId;

        public NetworkModuleRPCHeader ToModuleHeader()
        {
            return new NetworkModuleRPCHeader
            {
                childId = moduleRpc!.Value.childId,
                sceneId = identityRpc!.Value.sceneId,
                networkId = identityRpc!.Value.networkId,
                rpcId = rpcId,
                senderId = senderId,
                targetId = targetId
            };
        }

        public NetworkIdentityRPCHeader ToIdentityHeader()
        {
            return new NetworkIdentityRPCHeader
            {
                sceneId = identityRpc!.Value.sceneId,
                networkId = identityRpc!.Value.networkId,
                rpcId = rpcId,
                senderId = senderId,
                targetId = targetId
            };
        }

        public StaticRPCHeader ToStaticHeader()
        {
            return new StaticRPCHeader
            {
                typeHash = staticRpc!.Value.typeHash,
                rpcId = rpcId,
                senderId = senderId,
                targetId = targetId
            };
        }

        public UnionRPCHeader(NetworkIdentityRPCHeader identityRpc)
        {
            this.identityRpc = new MinimalIdentityHeader
            {
                sceneId = identityRpc.sceneId,
                networkId = identityRpc.networkId
            };
            rpcId = identityRpc.rpcId;
            senderId = identityRpc.senderId;
            targetId = identityRpc.targetId;
            moduleRpc = null;
            staticRpc = null;
        }

        public UnionRPCHeader(NetworkModuleRPCHeader moduleRpc)
        {
            this.moduleRpc = new MinimalModuleHeader
            {
                childId = moduleRpc.childId,
            };

            this.identityRpc = new MinimalIdentityHeader
            {
                sceneId = moduleRpc.sceneId,
                networkId = moduleRpc.networkId
            };

            rpcId = moduleRpc.rpcId;
            senderId = moduleRpc.senderId;
            targetId = moduleRpc.targetId;
            staticRpc = null;
        }

        public UnionRPCHeader(StaticRPCHeader staticRpc)
        {
            this.staticRpc = new MinimalStaticHeader
            {
                typeHash = staticRpc.typeHash,
            };
            rpcId = staticRpc.rpcId;
            senderId = staticRpc.senderId;
            targetId = staticRpc.targetId;
            identityRpc = null;
            moduleRpc = null;
        }

        public bool Equals(UnionRPCHeader other)
        {
            if (PurrEquality<MinimalIdentityHeader?>.Default.Equals(identityRpc, other.identityRpc) &&
                PurrEquality<MinimalModuleHeader?>.Default.Equals(moduleRpc, other.moduleRpc) &&
                PurrEquality<MinimalStaticHeader?>.Default.Equals(staticRpc, other.staticRpc) &&
                rpcId.value == other.rpcId.value && senderId == other.senderId && targetId == other.targetId)
            {
                return true;
            }
            return false;
        }

        public override bool Equals(object obj)
        {
            return obj is UnionRPCHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(identityRpc, moduleRpc, staticRpc);
        }
    }
}

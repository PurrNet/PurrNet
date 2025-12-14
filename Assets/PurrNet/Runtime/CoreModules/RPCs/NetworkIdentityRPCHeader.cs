using System;
using PurrNet.Packing;

namespace PurrNet
{
    public struct NetworkIdentityRPCHeader : IPackedAuto, IEquatable<NetworkIdentityRPCHeader>
    {
        public NetworkID networkId;
        public SceneID sceneId;
        public PlayerID senderId;
        public PlayerID? targetId;
        public Size rpcId;

        public override string ToString()
        {
            return $"NetworkIdentityRPCHeader: {{ sceneId: {sceneId}, networkId: {networkId}, senderId: {senderId}, targetId: {targetId}, rpcId: {rpcId} }}";
        }

        public bool Equals(NetworkIdentityRPCHeader other)
        {
            return networkId.Equals(other.networkId) &&
                   sceneId.Equals(other.sceneId) &&
                   senderId.Equals(other.senderId) &&
                   Nullable.Equals(targetId, other.targetId) &&
                   rpcId.Equals(other.rpcId);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkIdentityRPCHeader other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(networkId, sceneId, senderId, targetId, rpcId);
        }
    }
}

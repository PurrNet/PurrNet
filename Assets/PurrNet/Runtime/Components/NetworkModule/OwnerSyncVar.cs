using System;
using PurrNet.Packing;
using PurrNet.Transports;

namespace PurrNet
{
    public class LocalSyncVar<T> : SyncVar<T>
    {
        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawnEvent, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, isSpawnEvent, asServer);
    
            if (!isSpawnEvent && newOwner.HasValue)
                SendLatestState(newOwner.Value, _id, _value);
        }
        
        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            // Only send to the owner, not to all observers
            if (owner == null || owner != player)
                return;

            if (isSpawner && ownerAuth)
                return;

            SendLatestState(player, _id, _value);
        }
        
        protected override void ForceSendUnreliable()
        {
            if (!isServer)
                SendToServer(_id++, _value);
            else if (owner.HasValue)
                SendToTarget(owner.Value, _id++, _value);
        }
        
        protected override void ForceSendReliable()
        {
            if (!isServer)
                SendToServerReliably(_id++, _value);
            else if (owner.HasValue)
                SendToTargetReliably(owner.Value, _id++, _value);
        }
        
        [TargetRpc(Channel.Unreliable)]
        private void SendToTarget(PlayerID playerID, PackedULong packetId, T newValue)
        {
            if (isServer)
                return;
            OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void SendToTargetReliably(PlayerID playerID, PackedULong packetId, T newValue)
        {
            if (isServer)
                return;
            OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }
        
        [ServerRpc(Channel.Unreliable, requireOwnership: true)]
        protected override void SendToServer(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
                return;

            OnReceivedValue(packetId, newValue);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        protected override void SendToServerReliably(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
                return;

            OnReceivedValue(packetId, newValue);
        }
    }
}
using System;
using PurrNet.Packing;
using PurrNet.Transports;

namespace PurrNet
{
    public class LocalSyncVar<T> : SyncVar<T>
    {
        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            // Only send to the owner, not to all observers
            if (owner != player)
                return;

            if (isSpawner && ownerAuth)
                return;

            SendLatestState(player, _id, _value);
        }
        
        protected new void ForceSendUnreliable()
        {
            if (!isServer)
                SendToServer(_id++, _value);
            else if (owner.HasValue)
                SendToTarget(owner.Value, _id++, _value);
        }
        
        protected new void ForceSendReliable()
        {
            if (!isServer)
                SendToServerReliably(_id++, _value);
            else if (owner.HasValue)
                SendToTargetReliably(owner.Value, _id++, _value);
        }
        
        [TargetRpc(Channel.Unreliable)]
        private void SendToTarget(PlayerID playerID, PackedULong packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void SendToTargetReliably(PlayerID playerID, PackedULong packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
            if (newValue is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
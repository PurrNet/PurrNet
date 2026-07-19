using System.Collections.Generic;
using PurrNet.Transports;

namespace PurrNet
{
    public interface IPlayerBroadcaster
    {
        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null);

        public void Send<T>(IReadOnlyList<PlayerID> players, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null);

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null);

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null);

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new();

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new();
    }
}

using System;
using System.Runtime.CompilerServices;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    internal struct BatchKey : IEquatable<BatchKey>
    {
        public PlayerID playerId;
        public Channel channel;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEquals(BatchKey a, BatchKey b)
        {
            return a.playerId.id.value == b.playerId.id.value && a.channel == b.channel;
        }

        public bool Equals(BatchKey other)
        {
            return playerId.Equals(other.playerId) && channel == other.channel;
        }

        public override bool Equals(object obj)
        {
            return obj is BatchKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(playerId, (int)channel);
        }
    }
}

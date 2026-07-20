using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    internal struct BatchKey : IEquatable<BatchKey>
    {
        public PlayerID playerId;
        public Channel channel;

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
            unchecked
            {
                return (playerId.GetHashCode() * 397) ^ (int)channel;
            }
        }
    }

    internal sealed class BatchIndexMap : IDisposable
    {
        private const int ChannelCount = (int)Channel.Unreliable + 1;
        private readonly Dictionary<ulong, int>[] _channels;

        public BatchIndexMap(int initialCapacity)
        {
            _channels = new Dictionary<ulong, int>[ChannelCount];
            for (int i = 0; i < _channels.Length; i++)
                _channels[i] = new Dictionary<ulong, int>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(ulong playerId, Channel channel, out int value)
        {
            return _channels[(int)channel].TryGetValue(playerId, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(ulong playerId, Channel channel, int value)
        {
            int channelIndex = (int)channel;
            if ((uint)channelIndex >= (uint)_channels.Length)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
            _channels[channelIndex][playerId] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(ulong playerId, Channel channel)
        {
            int channelIndex = (int)channel;
            return (uint)channelIndex < (uint)_channels.Length &&
                   _channels[channelIndex].Remove(playerId);
        }

        public void Clear()
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i].Clear();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

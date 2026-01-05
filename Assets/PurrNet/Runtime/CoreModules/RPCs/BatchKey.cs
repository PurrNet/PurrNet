using System.Runtime.CompilerServices;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    internal struct BatchKey
    {
        public PlayerID playerId;
        public Channel channel;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEquals(BatchKey a, BatchKey b)
        {
            return a.playerId.id.value == b.playerId.id.value && a.channel == b.channel;
        }
    }
}

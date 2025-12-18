using PurrNet.Transports;

namespace PurrNet
{
    public interface IRpc
    {
        public ByteData rpcData { get; set; }

        PlayerID senderPlayerId { get; }

        PlayerID targetPlayerId { get; set; }

        uint GetStableHeaderHash();
    }
}

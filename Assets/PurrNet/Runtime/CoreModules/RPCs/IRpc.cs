using PurrNet.Packing;

namespace PurrNet
{
    public interface IRpc
    {
        public BitData rpcData { get; set; }

        PlayerID senderPlayerId { get; }

        PlayerID targetPlayerId { get; set; }

        uint GetStableHeaderHash();
    }
}

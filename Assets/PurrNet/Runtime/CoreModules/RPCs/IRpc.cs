using PurrNet.Packing;

namespace PurrNet
{
    public interface IRpc
    {
        public BitPacker rpcData { get; set; }

        PlayerID senderPlayerId { get; }

        PlayerID targetPlayerId { get; set; }

        uint GetStableHeaderHash();
    }
}

#if ADDRESSABLES_PURRNET_SUPPORT
using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct AddressableLoadStatePacket : IPackedAuto
    {
        public StringUTF8 guid;
        public bool loaded;
    }

    public struct AddressableLoadRequestPacket : IPackedAuto
    {
        public StringUTF8 guid;
    }
}
#endif

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

    /// <summary>
    /// Server to client: the server no longer counts on the client having this prefab loaded.
    /// Sent in reply to a load state of "not loaded", after any spawn that still relied on it.
    /// </summary>
    public struct AddressableReleaseAckPacket : IPackedAuto
    {
        public StringUTF8 guid;
    }
}
#endif

using PurrNet.Packing;

namespace PurrNet.Modules
{
    /// <summary>
    /// Confirms that a receiver has finished its native asynchronous instantiation and
    /// applied the corresponding spawn packet. The server does not promote the receiver
    /// to a confirmed observer until this packet succeeds.
    /// </summary>
    public struct AsyncSpawnReadyPacket : IPackedAuto
    {
        public SceneID sceneId;
        public SpawnID packetIdx;
        public bool success;
    }
}

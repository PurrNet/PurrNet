using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct DespawnPacket : IPackedAuto
    {
        public SceneID sceneId;
        public NetworkID parentId;
        public bool destroyAsync;
    }
}
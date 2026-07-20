using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct SceneSpawnReconcilePacket : IPackedAuto
    {
        public SceneID sceneId;

        public override string ToString()
        {
            return $"SceneSpawnReconcilePacket: {{ sceneId: {sceneId} }}";
        }
    }
}

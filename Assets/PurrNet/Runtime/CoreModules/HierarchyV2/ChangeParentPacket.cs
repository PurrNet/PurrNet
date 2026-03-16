using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct ChangeParentPacket : IPackedAuto
    {
        public SceneID sceneId;

        public NetworkID childId;
        public NetworkID? newParentId;
        public int[] path;
        public bool worldPositionStays;

        public override string ToString()
        {
            return $"ChangeParentPacket: {{ sceneId: {sceneId}, childId: {childId}, newParentId: {newParentId}, worldPositionStays: {worldPositionStays} }}";
        }
    }
}
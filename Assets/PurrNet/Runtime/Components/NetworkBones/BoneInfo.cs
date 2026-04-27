using UnityEngine;

namespace PurrNet
{
    public struct BoneInfo
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public NetworkBoneID posHash;
        public NetworkBoneID rotHash;
        public NetworkBoneID scaleHash;
        public uint posKeyHash;
        public uint rotKeyHash;
        public uint scaleKeyHash;
    }
}

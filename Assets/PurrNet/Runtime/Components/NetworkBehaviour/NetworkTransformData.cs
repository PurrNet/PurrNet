using System;
using PurrNet.Packing;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace PurrNet
{
    public struct NetworkTransformData : IEquatable<NetworkTransformData>
    {
        public CompressedVector3 position;
        public PackedQuaternion rotation;
        public CompressedVector3 scale;

        public NetworkTransformData(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkTransformData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(position, rotation, scale);
        }

        public unsafe bool Equals(NetworkTransformData other)
        {
            fixed (NetworkTransformData* self = &this)
            {
                return UnsafeUtility.MemCmp(self, &other, sizeof(NetworkTransformData)) == 0;
            }
        }

        public override string ToString()
        {
            return $"Position: {position}, Rotation: {rotation}, Scale: {scale}";
        }
    }
}

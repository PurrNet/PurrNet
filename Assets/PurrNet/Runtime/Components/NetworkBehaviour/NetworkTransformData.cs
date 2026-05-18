using System;
using PurrNet.Packing;
using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public struct NetworkTransformData : IEquatable<NetworkTransformData>
    {
        /// <summary>
        /// Quantized world/parent-local position. Carries the value on the legacy
        /// path; null when the absolute frame is in use.
        /// </summary>
        public CompressedVector3? position;

        /// <summary>
        /// Peer-agnostic absolute position (via an <see cref="INetworkTransformPositionTransform"/>).
        /// Carries the value on the absolute path; null otherwise. The nullable's
        /// has-value bit is the frame discriminator — there is no separate flag.
        /// </summary>
        public double3? absolutePosition;

        public PackedQuaternion rotation;
        public CompressedVector3 scale;

        public NetworkTransformData(CompressedVector3? position, double3? absolutePosition, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.absolutePosition = absolutePosition;
            this.rotation = rotation;
            this.scale = scale;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkTransformData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(position, absolutePosition, rotation, scale);
        }

        public bool Equals(NetworkTransformData other)
        {
            // Explicit comparison — the struct holds Nullable<> fields and so cannot
            // be safely MemCmp'd (a null nullable still has a value slot).
            return Nullable.Equals(position, other.position) &&
                   Nullable.Equals(absolutePosition, other.absolutePosition) &&
                   rotation.Equals(other.rotation) &&
                   scale.Equals(other.scale);
        }

        public override string ToString()
        {
            var pos = absolutePosition.HasValue ? absolutePosition.Value.ToString() : position.ToString();
            return $"Position: {pos}, Rotation: {rotation}, Scale: {scale}";
        }
    }
}

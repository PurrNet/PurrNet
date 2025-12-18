using UnityEngine;

namespace PurrNet
{
    public interface INetworkTransform
    {
        /// <summary>
        /// Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentiy.
        /// </summary>
        bool syncParent { get; }

        /// <summary>
        /// Whether to sync the position of the transform.
        /// </summary>
        bool syncPosition { get; }

        /// <summary>
        /// Whether to sync the rotation of the transform.
        /// </summary>
        bool syncRotation { get; }

        /// <summary>
        /// Whether to sync the scale of the transform.
        /// </summary>
        bool syncScale { get; }

        /// <summary>
        /// Whether to interpolate the position of the transform.
        /// </summary>
        bool interpolatePosition { get; }

        /// <summary>
        /// Whether to interpolate the rotation of the transform.
        /// </summary>
        bool interpolateRotation { get; }

        /// <summary>
        /// Whether to interpolate the scale of the transform.
        /// </summary>
        bool interpolateScale { get; }

        /// <summary>
        /// Whether the client controls the transform if they are the owner.
        /// </summary>
        bool ownerAuth { get; }

        /// <summary>
        /// Forces the latest NT state to target player, voiding compression and other optimizations
        /// </summary>
        void ForceSync(PlayerID target);

        /// <summary>
        /// Forces the latest NT state to everyone, voiding compression and other optimizations
        /// </summary>
        void ForceSync();

        /// <summary>
        /// Clears interpolation and teleports the transform to the target position, rotation and scale.
        /// Works on both owner and non-owner clients.
        /// </summary>
        void ClearInterpolation(Vector3? targetPos, Quaternion? targetRot, Vector3? targetScale);

        /// <summary>
        /// Indicates whether the current state of the network transform has deviated from its last transmitted state.
        /// </summary>
        /// <returns>
        /// True if there are differences between the current state and the last sent state; otherwise, false.
        /// </returns>
        bool HasChanges();
    }
}

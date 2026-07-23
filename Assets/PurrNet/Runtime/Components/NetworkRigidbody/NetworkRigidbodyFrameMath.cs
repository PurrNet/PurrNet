using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Frame conversions shared by NetworkRigidbody and its regression tests. A velocity paired
    /// with a parent-local position is always the derivative of that local position.
    /// </summary>
    internal static class NetworkRigidbodyFrameMath
    {
        internal static Vector3 ToParentLinearVelocity(
            Vector3 worldVelocity,
            Vector3 parentPointVelocity,
            Quaternion parentRotation,
            Vector3 parentScale)
        {
            var relativeWorldVelocity = worldVelocity - parentPointVelocity;
            return InverseScale(Quaternion.Inverse(parentRotation) * relativeWorldVelocity, parentScale);
        }

        internal static Vector3 ToWorldLinearVelocity(
            Vector3 parentLocalVelocity,
            Vector3 parentPointVelocity,
            Quaternion parentRotation,
            Vector3 parentScale)
        {
            return parentPointVelocity + parentRotation * Vector3.Scale(parentScale, parentLocalVelocity);
        }

        internal static Vector3 ToParentAngularVelocity(
            Vector3 worldAngularVelocity,
            Vector3 parentAngularVelocity,
            Quaternion parentRotation)
        {
            return Quaternion.Inverse(parentRotation) * (worldAngularVelocity - parentAngularVelocity);
        }

        internal static Vector3 ToWorldAngularVelocity(
            Vector3 parentLocalAngularVelocity,
            Vector3 parentWorldAngularVelocity,
            Quaternion parentRotation)
        {
            return parentWorldAngularVelocity + parentRotation * parentLocalAngularVelocity;
        }

        internal static Vector3 GetPointVelocity(
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            Vector3 worldCenterOfMass,
            Vector3 worldPoint)
        {
            return linearVelocity + Vector3.Cross(angularVelocity, worldPoint - worldCenterOfMass);
        }

        internal static Vector3 InverseScale(Vector3 value, Vector3 scale)
        {
            return new Vector3(
                scale.x != 0f ? value.x / scale.x : value.x,
                scale.y != 0f ? value.y / scale.y : value.y,
                scale.z != 0f ? value.z / scale.z : value.z);
        }
    }
}

#if UNITY_PHYSICS_2D
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// 2D counterpart of <see cref="RigidbodyCorrectionContext"/>. Rotations are in degrees and
    /// angular velocities in degrees per second, matching <see cref="Rigidbody2D"/>.
    /// </summary>
    public struct Rigidbody2DCorrectionContext
    {
        public Rigidbody2D rigidbody;
        public Vector2 previousPosition;
        public float previousRotation;
        public Vector2 targetPosition;
        public float targetRotation;
        public Vector2 targetLinearVelocity;
        public float targetAngularVelocity;
        public float positionError;
        public float rotationError;
        public float drag;
        public float positionStrength;
        public float correctionRange;
        public float rotationStrength;
        public float hardSnapDistance;
        public float hardSnapAngle;
        public float acceptableRotationError;
        public bool useKinematicRotation;
    }

    public abstract class NetworkRigidbody2DSettings : ScriptableObject
    {
        public abstract NetworkRigidbody2DSettingsInstance Create(NetworkRigidbody2D networkRigidbody);
    }

    public abstract class NetworkRigidbody2DSettings<T> : NetworkRigidbody2DSettings
        where T : NetworkRigidbody2DSettingsInstance
    {
        public sealed override NetworkRigidbody2DSettingsInstance Create(NetworkRigidbody2D networkRigidbody)
            => CreateTyped(networkRigidbody);

        protected abstract T CreateTyped(NetworkRigidbody2D networkRigidbody);
    }
}
#endif

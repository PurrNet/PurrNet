using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// All the context needed by <see cref="NetworkRigidbodySettings"/> virtual methods
    /// to make correction decisions. Includes the rigidbody, interpolated targets,
    /// pre-computed errors, and the component's default tuning values.
    /// </summary>
    public struct RigidbodyCorrectionContext
    {
        /// <summary>The rigidbody being corrected.</summary>
        public Rigidbody rigidbody;

        /// <summary>Target position from network interpolation.</summary>
        public Vector3 targetPosition;

        /// <summary>Target rotation from network interpolation.</summary>
        public Quaternion targetRotation;

        /// <summary>Target linear velocity from network interpolation.</summary>
        public Vector3 targetLinearVelocity;

        /// <summary>Target angular velocity from network interpolation.</summary>
        public Vector3 targetAngularVelocity;

        /// <summary>Euclidean distance between current and target position.</summary>
        public float positionError;

        /// <summary>Angle in degrees between current and target rotation.</summary>
        public float rotationError;

        /// <summary>Rigidbody linear drag (version-independent convenience value).</summary>
        public float drag;

        /// <summary>Default position correction strength from the component.</summary>
        public float positionStrength;

        /// <summary>Default distance over which position correction ramps from zero to full.</summary>
        public float correctionRange;

        /// <summary>Default rotation correction strength from the component.</summary>
        public float rotationStrength;

        /// <summary>Default distance threshold for teleport (hard position snap).</summary>
        public float hardSnapDistance;

        /// <summary>Default angle threshold (degrees) for hard rotation snap. Negative disables.</summary>
        public float hardSnapAngle;

        /// <summary>Default rotation error (degrees) below which soft correction stops. Negative disables.</summary>
        public float acceptableRotationError;
    }

    /// <summary>
    /// Abstract settings layer for <see cref="NetworkRigidbody"/>. Subclass this ScriptableObject
    /// and override any virtual method to customize correction behavior. Call <c>base</c> for any
    /// method you don't need to override — the default implementations reproduce the standard
    /// critically-damped spring correction using the component's serialized values.
    /// </summary>
    public abstract class NetworkRigidbodySettings : ScriptableObject
    {
        /// <summary>
        /// Whether the position error is large enough to warrant a full teleport (hard snap).
        /// Default: teleport when <see cref="RigidbodyCorrectionContext.positionError"/> >=
        /// <see cref="RigidbodyCorrectionContext.hardSnapDistance"/>.
        /// </summary>
        public virtual bool ShouldTeleport(in RigidbodyCorrectionContext ctx)
        {
            return ctx.positionError >= ctx.hardSnapDistance;
        }

        /// <summary>
        /// Whether the rotation error is large enough to warrant an instant rotation snap.
        /// Default: snap when hardSnapAngle >= 0, acceptableRotationError >= 0,
        /// and rotationError > hardSnapAngle.
        /// </summary>
        public virtual bool ShouldSnapRotation(in RigidbodyCorrectionContext ctx)
        {
            return ctx.hardSnapAngle >= 0
                && ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.hardSnapAngle;
        }

        /// <summary>
        /// Whether to apply soft rotation correction via torque.
        /// Default: correct when acceptableRotationError >= 0
        /// and rotationError > acceptableRotationError.
        /// </summary>
        public virtual bool ShouldCorrectRotation(in RigidbodyCorrectionContext ctx)
        {
            return ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.acceptableRotationError;
        }

        /// <summary>
        /// Called when <see cref="ShouldTeleport"/> returns true.
        /// Instantly snaps position, rotation, and velocities to the target.
        /// </summary>
        public virtual void ApplyHardCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            rb.MovePosition(ctx.targetPosition);
            rb.MoveRotation(NormalizeQuaternion(ctx.targetRotation));
            SetLinearVelocity(rb, ctx.targetLinearVelocity);
            rb.angularVelocity = ctx.targetAngularVelocity;
        }

        /// <summary>
        /// Applies soft position correction forces using a critically-damped spring model.
        /// Override to implement per-axis control, custom ramp curves, or any other strategy.
        /// </summary>
        public virtual void ApplyPositionCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            float m = rb.mass;
            float range = Mathf.Max(ctx.correctionRange, 0.01f);
            float ratio = Mathf.Clamp01(ctx.positionError / range);
            float w = ctx.positionStrength;

            Vector3 posError = ctx.targetPosition - rb.position;
            Vector3 velError = ctx.targetLinearVelocity - GetLinearVelocity(rb);

            Vector3 positionalPull = posError * (w * w) * ratio;
            Vector3 velocityDamping = velError * (2f * w);
            Vector3 dragCompensation = GetLinearVelocity(rb) * ctx.drag;

            rb.AddForce((positionalPull + velocityDamping + dragCompensation) * m);
        }

        /// <summary>
        /// Applies soft rotation correction torque using a critically-damped spring model.
        /// Override to implement per-axis control, custom thresholds, or any other strategy.
        /// </summary>
        public virtual void ApplyRotationCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            float m = rb.mass;
            float w = ctx.rotationStrength;

            Quaternion rotError = NormalizeQuaternion(ctx.targetRotation) * Quaternion.Inverse(rb.rotation);
            rotError.ToAngleAxis(out float angle, out Vector3 axis);

            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f)
                return;

            if (angle > 180f) angle -= 360f;

            Vector3 angError = axis * (angle * Mathf.Deg2Rad);
            Vector3 angVelError = ctx.targetAngularVelocity - rb.angularVelocity;
            Vector3 torque = (angError * (w * w) + angVelError * (2f * w)) * m;

            float maxTorque = w * w * m;
            float torqueMag = torque.magnitude;
            if (torqueMag > maxTorque)
                torque *= maxTorque / torqueMag;

            rb.AddTorque(torque);
        }

        #region Helpers

        protected static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float dot = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (dot < 0.0001f)
                return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(dot);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        protected static Vector3 GetLinearVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        protected static void SetLinearVelocity(Rigidbody rb, Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }

        protected static float GetDrag(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }

        #endregion
    }
}

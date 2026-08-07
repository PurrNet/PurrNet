using UnityEngine;

namespace PurrNet
{
    public abstract class NetworkRigidbodySettingsInstance
    {
        public virtual bool ShouldTeleport(in RigidbodyCorrectionContext ctx)
        {
            return ctx.positionError >= ctx.hardSnapDistance;
        }

        public virtual bool ShouldSnapRotation(in RigidbodyCorrectionContext ctx)
        {
            return !ctx.useKinematicRotation
                && ctx.hardSnapAngle >= 0
                && ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.hardSnapAngle;
        }

        public virtual bool ShouldCorrectRotation(in RigidbodyCorrectionContext ctx)
        {
            return ctx.useKinematicRotation
                || (ctx.acceptableRotationError >= 0
                    && ctx.rotationError > ctx.acceptableRotationError);
        }

        public virtual void ApplyHardCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            rb.MovePosition(ctx.targetPosition);
            rb.MoveRotation(NormalizeQuaternion(ctx.targetRotation));
            SetLinearVelocity(rb, ctx.targetLinearVelocity);
            SetAngularVelocity(rb, ctx.targetAngularVelocity);
        }

        public virtual void ApplyPositionCorrection(in RigidbodyCorrectionContext ctx)
        {
            NetworkRigidbodyPhysics.ApplyPositionSpring(
                ctx.rigidbody,
                ctx.targetPosition,
                ctx.targetLinearVelocity,
                ctx.positionError,
                ctx.positionStrength,
                ctx.correctionRange,
                ctx.drag);
        }

        public virtual void ApplyRotationCorrection(in RigidbodyCorrectionContext ctx)
        {
            NetworkRigidbodyPhysics.ApplyRotationSpring(
                ctx.rigidbody,
                NormalizeQuaternion(ctx.targetRotation),
                ctx.targetAngularVelocity,
                ctx.rotationStrength,
                ctx.useKinematicRotation);
        }

        public virtual void OnReset(in RigidbodyCorrectionContext ctx) { }

        public virtual void OnDespawned() { }

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
            return NetworkRigidbodyPhysics.GetLinearVelocity(rb);
        }

        protected static void SetLinearVelocity(Rigidbody rb, Vector3 value)
        {
            NetworkRigidbodyPhysics.SetLinearVelocity(rb, value);
        }

        protected static void SetAngularVelocity(Rigidbody rb, Vector3 value)
        {
            NetworkRigidbodyPhysics.SetAngularVelocity(rb, value);
        }

        protected static void AddForce(Rigidbody rb, Vector3 force, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddForce(rb, force, mode);
        }

        protected static void AddTorque(Rigidbody rb, Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddTorque(rb, torque, mode);
        }

        /// <summary>
        /// Converts a desired world-space angular acceleration into the torque that produces it,
        /// using the rigidbody's inertia tensor. Passing an angular acceleration straight to
        /// <see cref="AddTorque"/> scales it by the inverse inertia and will oscillate.
        /// </summary>
        protected static Vector3 AngularAccelerationToTorque(Rigidbody rb, Vector3 angularAcceleration)
        {
            return NetworkRigidbodyPhysics.AngularAccelerationToTorque(rb, angularAcceleration);
        }

        /// <summary>
        /// Clamps a critically-damped spring frequency to what the current fixed timestep can
        /// integrate without oscillating. Values above the limit diverge instead of converging.
        /// </summary>
        protected static float StableSpringFrequency(float frequency)
        {
            return NetworkRigidbodyPhysics.StableSpringFrequency(frequency);
        }

        /// <summary>
        /// Applies the built-in inertia-correct rotation spring. Follows the target rotation with
        /// <see cref="Rigidbody.MoveRotation"/> instead when <paramref name="kinematic"/> is set or
        /// the body has all rotation axes frozen, since torque cannot move a constrained axis.
        /// </summary>
        protected static void ApplyRotationSpring(Rigidbody rb, Quaternion targetRotation, Vector3 targetAngularVelocity, float rotationStrength, bool kinematic = false)
        {
            NetworkRigidbodyPhysics.ApplyRotationSpring(rb, targetRotation, targetAngularVelocity, rotationStrength, kinematic);
        }

        protected static bool CanApplyDynamicMotion(Rigidbody rb)
        {
            return NetworkRigidbodyPhysics.CanApplyDynamicMotion(rb);
        }

        protected static float GetDrag(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }
    }

    internal static class NetworkRigidbodyPhysics
    {
        private const float STATIC_BREAKAWAY_BODY_SPEED_SQR = 0.0004f;
        private const float STATIC_BREAKAWAY_TARGET_SPEED_SQR = 0.01f;

        internal static bool CanApplyDynamicMotion(Rigidbody rb)
        {
            return rb && !rb.isKinematic;
        }

        internal static Vector3 GetLinearVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        internal static void SetLinearVelocity(Rigidbody rb, Vector3 value)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }

        internal static void SetAngularVelocity(Rigidbody rb, Vector3 value)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.angularVelocity = value;
        }

        internal static void AddForce(Rigidbody rb, Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddForce(force, mode);
        }

        internal static void AddForceAtPosition(Rigidbody rb, Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddForceAtPosition(force, position, mode);
        }

        internal static void AddTorque(Rigidbody rb, Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddTorque(torque, mode);
        }

        internal static Vector3 AngularAccelerationToTorque(Rigidbody rb, Vector3 angularAcceleration)
        {
            var basis = rb.rotation * rb.inertiaTensorRotation;
            var local = Quaternion.Inverse(basis) * angularAcceleration;
            var tensor = rb.inertiaTensor;
            return basis * new Vector3(local.x * tensor.x, local.y * tensor.y, local.z * tensor.z);
        }

        internal static float StableSpringFrequency(float frequency)
        {
            if (frequency <= 0f)
                return 0f;

            var delta = Time.fixedDeltaTime;
            if (delta <= 0f)
                return frequency;

            return Mathf.Min(frequency, 0.5f / delta);
        }

        internal static void ApplyPositionSpring(
            Rigidbody rb,
            Vector3 targetPosition,
            Vector3 targetLinearVelocity,
            float positionError,
            float positionStrength,
            float correctionRange,
            float drag)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            var w = StableSpringFrequency(positionStrength);
            var range = Mathf.Max(correctionRange, 0.01f);
            var ratio = Mathf.Clamp01(positionError / range);
            var velocity = GetLinearVelocity(rb);

            if (velocity.sqrMagnitude < STATIC_BREAKAWAY_BODY_SPEED_SQR
                && targetLinearVelocity.sqrMagnitude > STATIC_BREAKAWAY_TARGET_SPEED_SQR)
            {
                SetLinearVelocity(rb, targetLinearVelocity);
                velocity = targetLinearVelocity;
            }

            var positionalPull = (targetPosition - rb.position) * (w * w * ratio);
            var velocityDamping = (targetLinearVelocity - velocity) * (2f * w);
            var dragCompensation = velocity * drag;

            rb.AddForce((positionalPull + velocityDamping + dragCompensation) * rb.mass, ForceMode.Force);
        }

        internal static void ApplyRotationSpring(
            Rigidbody rb,
            Quaternion targetRotation,
            Vector3 targetAngularVelocity,
            float rotationStrength,
            bool kinematic = false)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            if (kinematic || rb.freezeRotation)
            {
                rb.MoveRotation(targetRotation);
                rb.angularVelocity = Vector3.zero;
                return;
            }

            var rotationError = targetRotation * Quaternion.Inverse(rb.rotation);
            rotationError.ToAngleAxis(out var angle, out var axis);

            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f)
                return;

            if (angle > 180f)
                angle -= 360f;

            var w = StableSpringFrequency(rotationStrength);
            var angularError = axis * (angle * Mathf.Deg2Rad);
            var angularVelocityError = targetAngularVelocity - rb.angularVelocity;
            var acceleration = angularError * (w * w) + angularVelocityError * (2f * w);

            var maxAcceleration = w * w * Mathf.PI;
            var magnitude = acceleration.magnitude;
            if (magnitude > maxAcceleration)
                acceleration *= maxAcceleration / magnitude;

            rb.AddTorque(AngularAccelerationToTorque(rb, acceleration), ForceMode.Force);
        }
    }
}

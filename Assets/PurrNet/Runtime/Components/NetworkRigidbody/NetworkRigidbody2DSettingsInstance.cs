#if UNITY_PHYSICS_2D
using UnityEngine;

namespace PurrNet
{
    public abstract class NetworkRigidbody2DSettingsInstance : IRigidbodyCorrectionInstance
    {
        internal static Rigidbody2DCorrectionContext ToContext(in RigidbodyCorrectionData data)
        {
            var rb = data.body as Rigidbody2D;
            float reference = rb ? rb.rotation : 0f;

            return new Rigidbody2DCorrectionContext
            {
                rigidbody = rb,
                previousPosition = data.previousPosition,
                previousRotation = NetworkRigidbody2DPhysics.ToAngle(data.previousRotation, reference),
                targetPosition = data.targetPosition,
                targetRotation = NetworkRigidbody2DPhysics.ToAngle(data.targetRotation, reference),
                targetLinearVelocity = data.targetLinearVelocity,
                targetAngularVelocity = NetworkRigidbody2DPhysics.ToDegreesPerSecond(data.targetAngularVelocity),
                positionError = data.positionError,
                rotationError = data.rotationError,
                drag = data.drag,
                positionStrength = data.positionStrength,
                correctionRange = data.correctionRange,
                rotationStrength = data.rotationStrength,
                hardSnapDistance = data.hardSnapDistance,
                hardSnapAngle = data.hardSnapAngle,
                acceptableRotationError = data.acceptableRotationError,
                useKinematicRotation = data.useKinematicRotation
            };
        }

        bool IRigidbodyCorrectionInstance.ShouldTeleport(in RigidbodyCorrectionData data) => ShouldTeleport(ToContext(in data));
        bool IRigidbodyCorrectionInstance.ShouldSnapRotation(in RigidbodyCorrectionData data) => ShouldSnapRotation(ToContext(in data));
        bool IRigidbodyCorrectionInstance.ShouldCorrectRotation(in RigidbodyCorrectionData data) => ShouldCorrectRotation(ToContext(in data));
        void IRigidbodyCorrectionInstance.ApplyHardCorrection(in RigidbodyCorrectionData data) => ApplyHardCorrection(ToContext(in data));
        void IRigidbodyCorrectionInstance.ApplyPositionCorrection(in RigidbodyCorrectionData data) => ApplyPositionCorrection(ToContext(in data));
        void IRigidbodyCorrectionInstance.ApplyRotationCorrection(in RigidbodyCorrectionData data) => ApplyRotationCorrection(ToContext(in data));
        void IRigidbodyCorrectionInstance.OnReset(in RigidbodyCorrectionData data) => OnReset(ToContext(in data));
        void IRigidbodyCorrectionInstance.OnDespawned() => OnDespawned();

        public virtual bool ShouldTeleport(in Rigidbody2DCorrectionContext ctx)
        {
            return ctx.positionError >= ctx.hardSnapDistance;
        }

        public virtual bool ShouldSnapRotation(in Rigidbody2DCorrectionContext ctx)
        {
            return !ctx.useKinematicRotation
                && ctx.hardSnapAngle >= 0
                && ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.hardSnapAngle;
        }

        public virtual bool ShouldCorrectRotation(in Rigidbody2DCorrectionContext ctx)
        {
            return ctx.useKinematicRotation
                || (ctx.acceptableRotationError >= 0
                    && ctx.rotationError > ctx.acceptableRotationError);
        }

        public virtual void ApplyHardCorrection(in Rigidbody2DCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            rb.MovePosition(ctx.targetPosition);
            rb.MoveRotation(ctx.targetRotation);
            SetLinearVelocity(rb, ctx.targetLinearVelocity);
            SetAngularVelocity(rb, ctx.targetAngularVelocity);
        }

        public virtual void ApplyPositionCorrection(in Rigidbody2DCorrectionContext ctx)
        {
            NetworkRigidbody2DPhysics.ApplyPositionSpring(
                ctx.rigidbody,
                ctx.targetPosition,
                ctx.targetLinearVelocity,
                ctx.positionError,
                ctx.positionStrength,
                ctx.correctionRange,
                ctx.drag);
        }

        public virtual void ApplyRotationCorrection(in Rigidbody2DCorrectionContext ctx)
        {
            NetworkRigidbody2DPhysics.ApplyRotationSpring(
                ctx.rigidbody,
                ctx.targetRotation,
                ctx.targetAngularVelocity,
                ctx.rotationStrength,
                ctx.useKinematicRotation);
        }

        public virtual void OnReset(in Rigidbody2DCorrectionContext ctx) { }

        public virtual void OnDespawned() { }

        protected static Vector2 GetLinearVelocity(Rigidbody2D rb)
        {
            return NetworkRigidbody2DPhysics.GetLinearVelocity(rb);
        }

        protected static void SetLinearVelocity(Rigidbody2D rb, Vector2 value)
        {
            NetworkRigidbody2DPhysics.SetLinearVelocity(rb, value);
        }

        protected static void SetAngularVelocity(Rigidbody2D rb, float degreesPerSecond)
        {
            NetworkRigidbody2DPhysics.SetAngularVelocity(rb, degreesPerSecond);
        }

        protected static void AddForce(Rigidbody2D rb, Vector2 force, ForceMode2D mode = ForceMode2D.Force)
        {
            NetworkRigidbody2DPhysics.AddForce(rb, force, mode);
        }

        protected static void AddTorque(Rigidbody2D rb, float torque, ForceMode2D mode = ForceMode2D.Force)
        {
            NetworkRigidbody2DPhysics.AddTorque(rb, torque, mode);
        }

        /// <summary>
        /// Clamps a critically-damped spring frequency to what the current fixed timestep can
        /// integrate without oscillating. Values above the limit diverge instead of converging.
        /// </summary>
        protected static float StableSpringFrequency(float frequency)
        {
            return NetworkRigidbodyMath.StableSpringFrequency(frequency);
        }

        /// <summary>
        /// Applies the built-in inertia-correct rotation spring. Follows the target rotation with
        /// <see cref="Rigidbody2D.MoveRotation(float)"/> instead when <paramref name="kinematic"/> is set or
        /// the body has rotation frozen, since torque cannot move a constrained axis.
        /// </summary>
        protected static void ApplyRotationSpring(Rigidbody2D rb, float targetRotation, float targetAngularVelocity, float rotationStrength, bool kinematic = false)
        {
            NetworkRigidbody2DPhysics.ApplyRotationSpring(rb, targetRotation, targetAngularVelocity, rotationStrength, kinematic);
        }

        protected static bool CanApplyDynamicMotion(Rigidbody2D rb)
        {
            return NetworkRigidbody2DPhysics.CanApplyDynamicMotion(rb);
        }

        protected static float GetDrag(Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }
    }

    internal static class NetworkRigidbody2DPhysics
    {
        internal static Quaternion ToQuaternion(float degrees)
        {
            return Quaternion.Euler(0f, 0f, degrees);
        }

        internal static float ToAngle(Quaternion rotation)
        {
            return Mathf.Atan2(rotation.z, rotation.w) * 2f * Mathf.Rad2Deg;
        }

        internal static float ToAngle(Quaternion rotation, float reference)
        {
            return reference + Mathf.DeltaAngle(reference, ToAngle(rotation));
        }

        internal static Vector3 ToAngularVelocity(float degreesPerSecond)
        {
            return new Vector3(0f, 0f, degreesPerSecond * Mathf.Deg2Rad);
        }

        internal static float ToDegreesPerSecond(Vector3 angularVelocity)
        {
            return angularVelocity.z * Mathf.Rad2Deg;
        }

        internal static NetworkForceMode ToNetworkForceMode(ForceMode2D mode)
        {
            return mode == ForceMode2D.Impulse ? NetworkForceMode.Impulse : NetworkForceMode.Force;
        }

        internal static ForceMode2D ToForceMode2D(NetworkForceMode mode, out bool massIndependent)
        {
            switch (mode)
            {
                case NetworkForceMode.Impulse:
                    massIndependent = false;
                    return ForceMode2D.Impulse;
                case NetworkForceMode.VelocityChange:
                    massIndependent = true;
                    return ForceMode2D.Impulse;
                case NetworkForceMode.Acceleration:
                    massIndependent = true;
                    return ForceMode2D.Force;
                default:
                    massIndependent = false;
                    return ForceMode2D.Force;
            }
        }

        internal static bool CanApplyDynamicMotion(Rigidbody2D rb)
        {
            return rb && rb.bodyType == RigidbodyType2D.Dynamic;
        }

        internal static Vector2 GetLinearVelocity(Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        internal static void SetLinearVelocity(Rigidbody2D rb, Vector2 value)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }

        internal static void SetAngularVelocity(Rigidbody2D rb, float degreesPerSecond)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.angularVelocity = degreesPerSecond;
        }

        internal static void AddForce(Rigidbody2D rb, Vector2 force, ForceMode2D mode = ForceMode2D.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddForce(force, mode);
        }

        internal static void AddForceAtPosition(Rigidbody2D rb, Vector2 force, Vector2 position, ForceMode2D mode = ForceMode2D.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddForceAtPosition(force, position, mode);
        }

        internal static void AddTorque(Rigidbody2D rb, float torque, ForceMode2D mode = ForceMode2D.Force)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            rb.AddTorque(torque, mode);
        }

        internal static void AddForce(Rigidbody2D rb, Vector2 force, NetworkForceMode mode)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            var mode2D = ToForceMode2D(mode, out var massIndependent);
            rb.AddForce(massIndependent ? force * rb.mass : force, mode2D);
        }

        internal static void AddForceAtPosition(Rigidbody2D rb, Vector2 force, Vector2 position, NetworkForceMode mode)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            var mode2D = ToForceMode2D(mode, out var massIndependent);
            rb.AddForceAtPosition(massIndependent ? force * rb.mass : force, position, mode2D);
        }

        internal static void AddTorque(Rigidbody2D rb, float torque, NetworkForceMode mode)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            var mode2D = ToForceMode2D(mode, out var massIndependent);
            rb.AddTorque(massIndependent ? torque * rb.inertia : torque, mode2D);
        }

        internal static void ApplyPositionSpring(
            Rigidbody2D rb,
            Vector2 targetPosition,
            Vector2 targetLinearVelocity,
            float positionError,
            float positionStrength,
            float correctionRange,
            float drag)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            var w = NetworkRigidbodyMath.StableSpringFrequency(positionStrength);
            var range = Mathf.Max(correctionRange, 0.01f);
            var ratio = Mathf.Clamp01(positionError / range);
            var velocity = GetLinearVelocity(rb);

            if (velocity.sqrMagnitude < NetworkRigidbodyMath.STATIC_BREAKAWAY_BODY_SPEED_SQR
                && targetLinearVelocity.sqrMagnitude > NetworkRigidbodyMath.STATIC_BREAKAWAY_TARGET_SPEED_SQR)
            {
                SetLinearVelocity(rb, targetLinearVelocity);
                velocity = targetLinearVelocity;
            }

            var positionalPull = (targetPosition - rb.position) * (w * w * ratio);
            var velocityDamping = (targetLinearVelocity - velocity) * (2f * w);
            var dragCompensation = velocity * drag;

            rb.AddForce((positionalPull + velocityDamping + dragCompensation) * rb.mass, ForceMode2D.Force);
        }

        internal static void ApplyRotationSpring(
            Rigidbody2D rb,
            float targetRotation,
            float targetAngularVelocity,
            float rotationStrength,
            bool kinematic = false)
        {
            if (!CanApplyDynamicMotion(rb))
                return;

            if (kinematic || rb.freezeRotation)
            {
                rb.MoveRotation(rb.rotation + Mathf.DeltaAngle(rb.rotation, targetRotation));
                rb.angularVelocity = 0f;
                return;
            }

            var w = NetworkRigidbodyMath.StableSpringFrequency(rotationStrength);
            var angularError = Mathf.DeltaAngle(rb.rotation, targetRotation) * Mathf.Deg2Rad;
            var angularVelocityError = (targetAngularVelocity - rb.angularVelocity) * Mathf.Deg2Rad;
            var acceleration = NetworkRigidbodyMath.ClampSpringAcceleration(
                angularError * (w * w) + angularVelocityError * (2f * w),
                w);

            rb.AddTorque(acceleration * rb.inertia, ForceMode2D.Force);
        }
    }
}
#endif

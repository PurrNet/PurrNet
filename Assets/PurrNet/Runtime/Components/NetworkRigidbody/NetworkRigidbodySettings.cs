using System;
using UnityEngine;

namespace PurrNet
{
    public struct RigidbodyCorrectionContext
    {
        public Rigidbody rigidbody;
        public Vector3 previousPosition;
        public Quaternion previousRotation;
        public Vector3 targetPosition;
        public Quaternion targetRotation;
        public Vector3 targetLinearVelocity;
        public Vector3 targetAngularVelocity;
        public float positionError;
        public float rotationError;
        public float drag;
        public float positionStrength;
        public float correctionRange;
        public float rotationStrength;
        public float hardSnapDistance;
        public float hardSnapAngle;
        public float acceptableRotationError;
        /// <summary>
        /// Resolved rotation mode for this receiver. When true the target rotation should be
        /// followed with MoveRotation instead of torque, because the controller authors the
        /// rotation rather than simulating it.
        /// </summary>
        public bool useKinematicRotation;
    }

    public abstract class NetworkRigidbodySettings : ScriptableObject
    {
        public virtual NetworkRigidbodySettingsInstance Create(NetworkRigidbody networkRigidbody)
        {
            return new LegacySettingsInstance(this);
        }

        [Obsolete("Override NetworkRigidbodySettingsInstance.ShouldTeleport on the instance returned from Create() instead.")]
        public virtual bool ShouldTeleport(in RigidbodyCorrectionContext ctx)
        {
            return ctx.positionError >= ctx.hardSnapDistance;
        }

        [Obsolete("Override NetworkRigidbodySettingsInstance.ShouldSnapRotation on the instance returned from Create() instead.")]
        public virtual bool ShouldSnapRotation(in RigidbodyCorrectionContext ctx)
        {
            return !ctx.useKinematicRotation
                && ctx.hardSnapAngle >= 0
                && ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.hardSnapAngle;
        }

        [Obsolete("Override NetworkRigidbodySettingsInstance.ShouldCorrectRotation on the instance returned from Create() instead.")]
        public virtual bool ShouldCorrectRotation(in RigidbodyCorrectionContext ctx)
        {
            return ctx.useKinematicRotation
                || (ctx.acceptableRotationError >= 0
                    && ctx.rotationError > ctx.acceptableRotationError);
        }

        [Obsolete("Override NetworkRigidbodySettingsInstance.ApplyHardCorrection on the instance returned from Create() instead.")]
        public virtual void ApplyHardCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            rb.MovePosition(ctx.targetPosition);
            rb.MoveRotation(NormalizeQuaternion(ctx.targetRotation));
            SetLinearVelocity(rb, ctx.targetLinearVelocity);
            SetAngularVelocity(rb, ctx.targetAngularVelocity);
        }

        [Obsolete("Override NetworkRigidbodySettingsInstance.ApplyPositionCorrection on the instance returned from Create() instead.")]
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

        [Obsolete("Override NetworkRigidbodySettingsInstance.ApplyRotationCorrection on the instance returned from Create() instead.")]
        public virtual void ApplyRotationCorrection(in RigidbodyCorrectionContext ctx)
        {
            NetworkRigidbodyPhysics.ApplyRotationSpring(
                ctx.rigidbody,
                NormalizeQuaternion(ctx.targetRotation),
                ctx.targetAngularVelocity,
                ctx.rotationStrength,
                ctx.useKinematicRotation);
        }

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

        private sealed class LegacySettingsInstance : NetworkRigidbodySettingsInstance
        {
            private readonly NetworkRigidbodySettings _settings;

            public LegacySettingsInstance(NetworkRigidbodySettings settings)
            {
                _settings = settings;
            }

#pragma warning disable CS0618
            public override bool ShouldTeleport(in RigidbodyCorrectionContext ctx) => _settings.ShouldTeleport(in ctx);
            public override bool ShouldSnapRotation(in RigidbodyCorrectionContext ctx) => _settings.ShouldSnapRotation(in ctx);
            public override bool ShouldCorrectRotation(in RigidbodyCorrectionContext ctx) => _settings.ShouldCorrectRotation(in ctx);
            public override void ApplyHardCorrection(in RigidbodyCorrectionContext ctx) => _settings.ApplyHardCorrection(in ctx);
            public override void ApplyPositionCorrection(in RigidbodyCorrectionContext ctx) => _settings.ApplyPositionCorrection(in ctx);
            public override void ApplyRotationCorrection(in RigidbodyCorrectionContext ctx) => _settings.ApplyRotationCorrection(in ctx);
#pragma warning restore CS0618
        }
    }

    public abstract class NetworkRigidbodySettings<T> : NetworkRigidbodySettings
        where T : NetworkRigidbodySettingsInstance
    {
        public sealed override NetworkRigidbodySettingsInstance Create(NetworkRigidbody networkRigidbody)
            => CreateTyped(networkRigidbody);

        protected abstract T CreateTyped(NetworkRigidbody networkRigidbody);
    }
}

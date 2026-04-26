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
            return ctx.hardSnapAngle >= 0
                && ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.hardSnapAngle;
        }

        public virtual bool ShouldCorrectRotation(in RigidbodyCorrectionContext ctx)
        {
            return ctx.acceptableRotationError >= 0
                && ctx.rotationError > ctx.acceptableRotationError;
        }

        public virtual void ApplyHardCorrection(in RigidbodyCorrectionContext ctx)
        {
            var rb = ctx.rigidbody;
            rb.MovePosition(ctx.targetPosition);
            rb.MoveRotation(NormalizeQuaternion(ctx.targetRotation));
            SetLinearVelocity(rb, ctx.targetLinearVelocity);
            rb.angularVelocity = ctx.targetAngularVelocity;
        }

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
    }
}

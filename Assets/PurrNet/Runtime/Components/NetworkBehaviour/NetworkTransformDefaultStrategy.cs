using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Default adaptive sync strategy. Straight motion is detected first and handled by
    /// the linear fallback paths; anything else is reconstructed along locally fitted
    /// circular arcs, covering motion with roughly constant curvature over one send
    /// interval: orbits, turns, arches, projectile arcs and spline-like paths. Rotation
    /// is continued at constant angular rate whenever the segment meaningfully rotates.
    /// </summary>
    public class NetworkTransformDefaultStrategy : NetworkTransformSyncStrategy
    {
        private const float LINEAR_NOISE_FLOOR = 4f * CompressedFloat.PRECISION;
        private const float LINEAR_REL_FRACTION = 1f / 64f;
        private const float ROTATION_SHAPE_MAX_DOT = 0.99999f;
        private const float ARC_EXTRAP_MAX_DEGREES = 120f;

        private Vector3 _fitPrev;
        private Vector3 _fitFrom;
        private Vector3 _fitTo;
        private Vector3 _fitCenter;
        private bool _fitValid;
        private bool _hasFit;

        protected override bool TryReconstruct(in NetworkTransformSample prev, in NetworkTransformSample from,
            in NetworkTransformSample to, float t, ref NetworkTransformSample result)
        {
            bool shaped = false;

            if (Mathf.Abs(Quaternion.Dot(from.rotation, to.rotation)) < ROTATION_SHAPE_MAX_DOT)
            {
                result.rotation = Quaternion.SlerpUnclamped(from.rotation, to.rotation, t);
                shaped = true;
            }

            var pPrev = prev.position;
            var pFrom = from.position;
            var pTo = to.position;

            if (!_hasFit || _fitPrev != pPrev || _fitFrom != pFrom || _fitTo != pTo)
            {
                _fitValid = !IsNearlyLinear(pPrev, pFrom, pTo) && TryFitCircle(pPrev, pFrom, pTo, out _fitCenter);
                _fitPrev = pPrev;
                _fitFrom = pFrom;
                _fitTo = pTo;
                _hasFit = true;
            }

            if (_fitValid)
            {
                result.position = ArcPoint(_fitCenter, pFrom, pTo, t);
                shaped = true;
            }

            return shaped;
        }

        private static bool IsNearlyLinear(Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            float acSq = ac.sqrMagnitude;

            if (acSq < LINEAR_NOISE_FLOOR * LINEAR_NOISE_FLOOR)
                return true;

            float hSq = Vector3.Cross(ab, ac).sqrMagnitude / acSq;
            float relThresh = acSq * (LINEAR_REL_FRACTION * LINEAR_REL_FRACTION);
            float thresh = Mathf.Max(LINEAR_NOISE_FLOOR * LINEAR_NOISE_FLOOR, relThresh);

            return hSq < thresh;
        }

        private static bool TryFitCircle(Vector3 a, Vector3 b, Vector3 c, out Vector3 center)
        {
            var ab = b - a;
            var ac = c - a;
            var cross = Vector3.Cross(ab, ac);
            float d = 2f * cross.sqrMagnitude;

            if (d < 1e-8f)
            {
                center = default;
                return false;
            }

            var toCenter = (Vector3.Cross(cross, ab) * ac.sqrMagnitude +
                            Vector3.Cross(ac, cross) * ab.sqrMagnitude) / d;
            center = a + toCenter;
            return true;
        }

        private static Vector3 ArcPoint(Vector3 center, Vector3 from, Vector3 to, float t)
        {
            var va = from - center;
            var vb = to - center;
            var axis = Vector3.Cross(va, vb);

            if (axis.sqrMagnitude < 1e-10f)
                return Vector3.LerpUnclamped(from, to, t);

            axis.Normalize();
            float angle = Vector3.SignedAngle(va, vb, axis);
            float absAngle = Mathf.Abs(angle);

            if (t > 1f && absAngle > 1e-5f)
            {
                float tCap = 1f + ARC_EXTRAP_MAX_DEGREES / absAngle;
                if (t > tCap)
                {
                    float radiusCap = Mathf.Lerp(va.magnitude, vb.magnitude, tCap);
                    var dirCap = Quaternion.AngleAxis(angle * tCap, axis) * va.normalized;
                    var capPoint = center + dirCap * radiusCap;
                    var tangent = Vector3.Cross(axis, dirCap) * (angle * Mathf.Deg2Rad * radiusCap);
                    return capPoint + tangent * (t - tCap);
                }
            }

            var dir = Quaternion.AngleAxis(angle * t, axis) * va.normalized;
            float radius = Mathf.Lerp(va.magnitude, vb.magnitude, t);
            return center + dir * radius;
        }
    }
}

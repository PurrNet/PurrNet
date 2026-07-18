using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkTransformArcStrategy",
        menuName = "PurrNet/Network Transform Arc Strategy")]
    public class NetworkTransformArcStrategy : NetworkTransformStrategySettings
    {
        private Vector3 _fitPrev;
        private Vector3 _fitFrom;
        private Vector3 _fitTo;
        private Vector3 _fitCenter;
        private bool _fitValid;
        private bool _hasFit;

        internal override bool TryReconstruct(in NetworkTransformState prev, in NetworkTransformState from,
            in NetworkTransformState to, float t, out NetworkTransformState result)
        {
            result = default;

            if (prev.frame != from.frame || from.frame != to.frame ||
                !prev.parentId.Equals(from.parentId) || !from.parentId.Equals(to.parentId))
                return false;

            if (!TryGetPosition(prev, out var pPrev) || !TryGetPosition(from, out var pFrom) ||
                !TryGetPosition(to, out var pTo))
                return false;

            if (!_hasFit || _fitPrev != pPrev || _fitFrom != pFrom || _fitTo != pTo)
            {
                _fitValid = TryFitCircle(pPrev, pFrom, pTo, out _fitCenter);
                _fitPrev = pPrev;
                _fitFrom = pFrom;
                _fitTo = pTo;
                _hasFit = true;
            }

            if (!_fitValid)
                return false;

            result = NetworkTransformVelocity.Lerp(from, to, t);
            result.data.position = (CompressedVector3)ArcPoint(_fitCenter, pFrom, pTo, t);
            return true;
        }

        private static bool TryGetPosition(in NetworkTransformState state, out Vector3 position)
        {
            if (state.data.position.HasValue)
            {
                var p = state.data.position.Value;
                position = new Vector3(p.x.value, p.y.value, p.z.value);
                return true;
            }

            position = default;
            return false;
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
            var dir = Quaternion.AngleAxis(angle * t, axis) * va.normalized;
            float radius = Mathf.Lerp(va.magnitude, vb.magnitude, t);
            return center + dir * radius;
        }
    }
}

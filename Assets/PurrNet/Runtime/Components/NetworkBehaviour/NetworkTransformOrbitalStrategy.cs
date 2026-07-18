using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkTransformOrbitalStrategy",
        menuName = "PurrNet/Network Transform Orbital Strategy")]
    public class NetworkTransformOrbitalStrategy : NetworkTransformStrategySettings
    {
        internal override bool CanSkip(NetworkTransform nt, in NetworkTransformState from, ushort fromTick,
            ushort currentTick, in NetworkTransformState current)
        {
            int gap = (short)(currentTick - fromTick);
            if (gap <= 1)
                return true;

            if (current.frame != from.frame || !current.parentId.Equals(from.parentId))
                return false;

            if (!TryGetPosition(from, out var pFrom) || !TryGetPosition(current, out var pCurrent))
                return base.CanSkip(nt, from, fromTick, currentTick, current);

            if (!nt.TryGetCapturedAt((ushort)(fromTick + gap / 2), out var midState) ||
                !TryGetPosition(midState, out var pMid))
                return false;

            if (!TryFitCircle(pFrom, pMid, pCurrent, out var center))
                return base.CanSkip(nt, from, fromTick, currentTick, current);

            var chordVelocity = NetworkTransformVelocity.Derive(from, current, gap);

            for (int step = 1; step < gap; step++)
            {
                if (!nt.TryGetCapturedAt((ushort)(fromTick + step), out var actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                float t = step / (float)gap;
                var expected = NetworkTransformVelocity.Lerp(from, current, t);
                expected.data.position = (CompressedVector3)ArcPoint(center, pFrom, pCurrent, t);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity))
                    return false;
            }

            return true;
        }

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

            if (!TryFitCircle(pPrev, pFrom, pTo, out var center))
                return false;

            result = NetworkTransformVelocity.Lerp(from, to, t);
            result.data.position = (CompressedVector3)ArcPoint(center, pFrom, pTo, t);
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

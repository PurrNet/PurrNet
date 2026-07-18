using PurrNet.Modules;
using UnityEngine;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkTransformStrategySettings",
        menuName = "PurrNet/Network Transform Strategy Settings")]
    public class NetworkTransformStrategySettings : ScriptableObject
    {
        [Tooltip("Maximum time between sends while motion stays reconstructible by receivers. " +
                 "Higher values save more bandwidth but add more interpolation delay at low " +
                 "extrapolation values and larger corrections at high ones.")]
        [Range(0.05f, 1f)]
        public float maxSendInterval = 0.2f;

        [Tooltip("How far receivers project received motion toward real time. " +
                 "0 renders only received data, adding delay but never showing a guess. " +
                 "1 projects fully to real time, causing rubberbanding when motion changes.")]
        [Range(0f, 1f)]
        public float extrapolation;

        internal bool CanSkip(NetworkTransform nt, in NetworkTransformState prev, bool hasPrev,
            in NetworkTransformState from, ushort fromTick, ushort currentTick, in NetworkTransformState current)
        {
            int gap = (short)(currentTick - fromTick);
            if (gap <= 1)
                return true;

            if (current.frame != from.frame || !current.parentId.Equals(from.parentId))
                return false;

            var chordVelocity = NetworkTransformVelocity.Derive(from, current, gap);

            for (int step = 1; step < gap; step++)
            {
                if (!nt.TryGetCapturedAt((ushort)(fromTick + step), out var actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                float t = step / (float)gap;

                if (!hasPrev || !TryReconstruct(prev, from, current, t, out var expected))
                    expected = NetworkTransformVelocity.Lerp(from, current, t);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity))
                    return false;
            }

            return true;
        }

        internal virtual bool TryReconstruct(in NetworkTransformState prev, in NetworkTransformState from,
            in NetworkTransformState to, float t, out NetworkTransformState result)
        {
            result = default;
            return false;
        }
    }
}

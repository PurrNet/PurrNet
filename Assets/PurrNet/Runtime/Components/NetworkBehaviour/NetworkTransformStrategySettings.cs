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

        [Tooltip("How far receivers project past verified motion toward real time. " +
                 "With confirmations enabled, 0 renders at the confirmed frontier, roughly one " +
                 "network latency behind real time. With confirmations disabled, 0 stays a full " +
                 "send interval behind. 1 projects fully to real time, causing rubberbanding " +
                 "when motion changes.")]
        [Range(0f, 1f)]
        public float extrapolation;

        [Tooltip("While sends are being skipped, include a tiny per-tick confirmation so receivers " +
                 "can render verified motion close to real time instead of waiting out the full " +
                 "send interval. Disable to save the confirmation traffic and rely purely on the " +
                 "extrapolation value above.")]
        public bool sendConfirmations = true;

        internal bool CanSkip(NetworkTransform nt, in NTLastPredictiveWrite lastWrite, ushort currentTick,
            in NetworkTransformState current)
        {
            var from = lastWrite.state;
            ushort fromTick = lastWrite.tick;

            int gap = (short)(currentTick - fromTick);
            if (gap < 1 || (gap == 1 && !sendConfirmations))
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

                if (!lastWrite.hasPrev || !TryReconstruct(lastWrite.prevState, from, current, t, out var expected))
                    expected = NetworkTransformVelocity.Lerp(from, current, t);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity))
                    return false;
            }

            if (!sendConfirmations)
                return true;

            int span = (short)(fromTick - lastWrite.prevTick);
            bool canArc = lastWrite.hasPrevPrev && span >= 2;
            var anchorVelocity = lastWrite.hasPrev && span >= 1 && span <= NTUnreliable.PREDICTIVE_MAX_BACKFILL
                ? NetworkTransformVelocity.Derive(lastWrite.prevState, from, span)
                : default;

            for (int step = 1; step <= gap; step++)
            {
                NetworkTransformState actual;

                if (step == gap)
                    actual = current;
                else if (!nt.TryGetCapturedAt((ushort)(fromTick + step), out actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                if (!canArc || !TryReconstruct(lastWrite.prevPrevState, lastWrite.prevState, from,
                        (span + step) / (float)span, out var expected))
                    expected = NetworkTransformVelocity.Predict(from, anchorVelocity, step);

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

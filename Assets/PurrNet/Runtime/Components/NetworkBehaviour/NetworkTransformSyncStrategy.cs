using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// A transform state snapshot in world-space floats, as seen by sync strategies.
    /// </summary>
    public struct NetworkTransformSample
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    /// <summary>
    /// Base class for predictive sync strategies. Strategies are plain classes injected at
    /// runtime via <see cref="NetworkTransform.SetSyncStrategy"/>; when none is injected,
    /// NetworkTransform uses a shared built-in default strategy with default settings.
    /// On its own this base class reconstructs skipped motion linearly; subclasses override
    /// <see cref="TryReconstruct"/> to shape it differently.
    /// </summary>
    public class NetworkTransformSyncStrategy
    {
        public const float DEFAULT_MAX_SEND_INTERVAL = 0.2f;

        /// <summary>
        /// Maximum time between sends while motion stays reconstructible by receivers.
        /// Bounds how long a lost packet can stall the render frontier and how stale delta
        /// baselines can get. Does not affect render delay.
        /// </summary>
        public float maxSendInterval = DEFAULT_MAX_SEND_INTERVAL;

        /// <summary>
        /// Reconstructs the state at normalized time t given three consecutive known states.
        /// t is 0 at <paramref name="from"/> and 1 at <paramref name="to"/>; values above 1
        /// extrapolate beyond <paramref name="to"/>. <paramref name="result"/> arrives
        /// pre-filled with the linear reconstruction — modify only what the strategy shapes
        /// and return true, or return false to keep the linear result. Implementations must
        /// be pure and deterministic: the sender runs the same function to verify what
        /// receivers will render, and any hidden state or randomness breaks that contract.
        /// </summary>
        protected virtual bool TryReconstruct(in NetworkTransformSample prev, in NetworkTransformSample from,
            in NetworkTransformSample to, float t, ref NetworkTransformSample result)
        {
            return false;
        }

        /// <summary>
        /// Invokes this strategy's <see cref="TryReconstruct"/>. Exists so composing
        /// strategies can delegate to other strategy instances.
        /// </summary>
        public bool TryReconstructSample(in NetworkTransformSample prev, in NetworkTransformSample from,
            in NetworkTransformSample to, float t, ref NetworkTransformSample result)
        {
            return TryReconstruct(prev, from, to, t, ref result);
        }

        internal bool TryReconstructState(in NetworkTransformState prev, in NetworkTransformState from,
            in NetworkTransformState to, float t, out NetworkTransformState result)
        {
            result = default;

            if (prev.frame != from.frame || from.frame != to.frame ||
                !prev.parentId.Equals(from.parentId) || !from.parentId.Equals(to.parentId))
                return false;

            if (!prev.data.position.HasValue || !from.data.position.HasValue || !to.data.position.HasValue)
                return false;

            var baseline = NetworkTransformVelocity.Lerp(from, to, t);
            var sample = ToSample(baseline);
            var prefill = sample;

            if (!TryReconstruct(ToSample(prev), ToSample(from), ToSample(to), t, ref sample))
                return false;

            result = baseline;

            if (Differs(sample.position, prefill.position))
                result.data.position = (CompressedVector3)sample.position;

            if (Differs(sample.rotation, prefill.rotation))
            {
                var r = result.data.rotation;
                r.x = new NormalizedFloat(sample.rotation.x);
                r.y = new NormalizedFloat(sample.rotation.y);
                r.z = new NormalizedFloat(sample.rotation.z);
                r.w = new NormalizedFloat(sample.rotation.w);
                result.data.rotation = r;
            }

            if (Differs(sample.scale, prefill.scale))
                result.data.scale = (CompressedVector3)sample.scale;

            return true;
        }

        private static NetworkTransformSample ToSample(in NetworkTransformState state)
        {
            var p = state.data.position.Value;
            var r = state.data.rotation;
            var s = state.data.scale;

            return new NetworkTransformSample
            {
                position = new Vector3(p.x.value, p.y.value, p.z.value),
                rotation = new Quaternion(r.x, r.y, r.z, r.w),
                scale = new Vector3(s.x.value, s.y.value, s.z.value)
            };
        }

        private static bool Differs(Vector3 a, Vector3 b)
        {
            return a.x != b.x || a.y != b.y || a.z != b.z;
        }

        private static bool Differs(Quaternion a, Quaternion b)
        {
            return a.x != b.x || a.y != b.y || a.z != b.z || a.w != b.w;
        }

        internal bool CanSkip(NetworkTransform nt, in NTLastPredictiveWrite lastWrite, ushort currentTick,
            in NetworkTransformState current)
        {
            var from = lastWrite.state;
            ushort fromTick = lastWrite.tick;

            int gap = (short)(currentTick - fromTick);
            if (gap < 1)
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

                if (!lastWrite.hasPrev ||
                    !TryReconstructState(lastWrite.prevState, from, current, t, out var expected))
                    expected = NetworkTransformVelocity.Lerp(from, current, t);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity))
                    return false;
            }

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

                if (!canArc || !TryReconstructState(lastWrite.prevPrevState, lastWrite.prevState, from,
                        (span + step) / (float)span, out var expected))
                    expected = NetworkTransformVelocity.Predict(from, anchorVelocity, step);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity))
                    return false;
            }

            return true;
        }
    }
}

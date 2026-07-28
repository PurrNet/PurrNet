using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// How aggressively adaptive sync skips sends. More aggressive levels tolerate larger
    /// deviations between predicted and actual motion before breaking a skip streak, saving
    /// more bandwidth at the cost of reconstruction precision on receivers.
    /// </summary>
    public enum AdaptiveSyncLevel : byte
    {
        Off = 0,
        Conservative = 1,
        Balanced = 2,
        Aggressive = 3,
        VeryAggressive = 4
    }

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
    /// Base class for adaptive sync strategies. Strategies are plain classes injected at
    /// runtime via <see cref="NetworkTransform.SetSyncStrategy"/>; when none is injected,
    /// NetworkTransform uses a shared built-in default strategy tuned for the selected
    /// <see cref="AdaptiveSyncLevel"/>.
    /// On its own this base class reconstructs skipped motion linearly; subclasses override
    /// <see cref="TryReconstruct"/> to shape it differently.
    /// </summary>
    public class NetworkTransformSyncStrategy
    {
        public const float DEFAULT_MAX_SEND_INTERVAL = 0.3f;

        /// <summary>
        /// Maximum time between sends while motion stays reconstructible by receivers.
        /// Bounds how long a lost packet can stall the render frontier and how stale delta
        /// baselines can get. Does not affect render delay.
        /// </summary>
        public float maxSendInterval = DEFAULT_MAX_SEND_INTERVAL;

        /// <summary>
        /// Velocity-proportional part of the skip tolerance: per verified tick of horizon, the
        /// per-axis tolerance grows by the per-tick displacement right-shifted by this many
        /// extra bits (0 = a full tick of motion, 1 = half, 2 = a quarter). Slow model drift
        /// is tolerated in proportion to how far ahead the prediction reaches, while sharp
        /// deviations still break immediately. Lower is more optimistic.
        /// </summary>
        public int toleranceVelocityShift = 2;

        /// <summary>
        /// Upper bound on the velocity-scaled tolerance, as a multiple of the base tolerance
        /// (2 position quanta = 2mm). Zero or negative disables the cap.
        /// </summary>
        public long toleranceCapMultiplier = 64;

        /// <summary>
        /// How many intermediate ticks are verified against captured motion before a skip
        /// window may be interpolated by receivers. Spacing is at most 6 ticks at the default
        /// send interval, so 5 probes verifies every skipped tick.
        /// </summary>
        public int interpVerifyProbes = 3;

        /// <summary>
        /// How many times a prediction-breaking send is repeated (once per
        /// <see cref="NTUnreliable.REDUNDANCY_INTERVAL"/> ticks) to survive packet loss.
        /// Lower values save bandwidth but lengthen the wrong-path window when a break
        /// packet is lost.
        /// </summary>
        public byte breakRedundancy = NTUnreliable.BREAK_REDUNDANCY;

        /// <summary>
        /// Sets the tolerance, verification, and send-interval knobs to the preset for the
        /// given level. <see cref="AdaptiveSyncLevel.Off"/> is ignored — disabling adaptive
        /// sync is the caller's decision, not the strategy's. Longer send intervals raise the
        /// suppression ceiling (a keepalive is forced once per interval) but widen the window
        /// receivers reconstruct on their own.
        /// </summary>
        public void ApplyLevel(AdaptiveSyncLevel level)
        {
            switch (level)
            {
                case AdaptiveSyncLevel.Conservative:
                    toleranceVelocityShift = 4;
                    toleranceCapMultiplier = 8;
                    interpVerifyProbes = 5;
                    maxSendInterval = 0.3f;
                    breakRedundancy = NTUnreliable.BREAK_REDUNDANCY;
                    break;
                case AdaptiveSyncLevel.Balanced:
                    toleranceVelocityShift = 3;
                    toleranceCapMultiplier = 32;
                    interpVerifyProbes = 3;
                    maxSendInterval = 0.3f;
                    breakRedundancy = NTUnreliable.BREAK_REDUNDANCY;
                    break;
                case AdaptiveSyncLevel.Aggressive:
                    toleranceVelocityShift = 2;
                    toleranceCapMultiplier = 64;
                    interpVerifyProbes = 3;
                    maxSendInterval = 0.3f;
                    breakRedundancy = NTUnreliable.BREAK_REDUNDANCY;
                    break;
                case AdaptiveSyncLevel.VeryAggressive:
                    toleranceVelocityShift = 0;
                    toleranceCapMultiplier = 0;
                    interpVerifyProbes = 1;
                    maxSendInterval = 0.6f;
                    breakRedundancy = 1;
                    break;
            }
        }

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

        internal bool CanSkip(NetworkTransform nt, NTLastAdaptiveWrite lastWrite, ushort currentTick,
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

            int interpSteps = gap - 1;
            int probes = interpSteps <= interpVerifyProbes
                ? interpSteps
                : interpVerifyProbes;

            for (int i = 1; i <= probes; i++)
            {
                int step = interpSteps <= interpVerifyProbes
                    ? i
                    : gap * i / (probes + 1);

                if (step < 1 || step >= gap)
                    continue;

                if (!nt.TryGetCapturedAt((ushort)(fromTick + step), out var actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                float t = step / (float)gap;

                if (!lastWrite.hasPrev ||
                    !TryReconstructState(lastWrite.prevState, from, current, t, out var expected))
                    expected = NetworkTransformVelocity.Lerp(from, current, t);

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity,
                        toleranceVelocityShift, toleranceCapMultiplier, Mathf.Min(step, gap - step)))
                    return false;
            }

            int span = (short)(fromTick - lastWrite.prevTick);
            bool canArc = lastWrite.hasPrevPrev && span >= 2;
            var anchorVelocity = lastWrite.hasPrev && span >= 1 && span <= NTUnreliable.ADAPTIVE_MAX_BACKFILL
                ? NetworkTransformVelocity.Derive(lastWrite.prevState, from, span)
                : default;

            byte chainFlags = (byte)((lastWrite.hasPrev ? 1 : 0) | (lastWrite.hasPrevPrev ? 2 : 0));

            int startStep = 1;
            if (nt.hasExtrapVerify && nt.extrapVerifyBase == fromTick &&
                nt.extrapVerifyPrev == lastWrite.prevTick &&
                nt.extrapVerifyPrevPrev == lastWrite.prevPrevTick &&
                nt.extrapVerifyFlags == chainFlags)
            {
                int done = (short)(nt.extrapVerifyThrough - fromTick);
                if (done >= 1 && done < gap)
                    startStep = done + 1;
                else if (done >= gap)
                    startStep = gap;
            }

            for (int step = startStep; step <= gap; step++)
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

                if (!NTUnreliable.PredictionMatches(expected, actual, chordVelocity,
                        toleranceVelocityShift, toleranceCapMultiplier, step))
                    return false;
            }

            nt.extrapVerifyBase = fromTick;
            nt.extrapVerifyPrev = lastWrite.prevTick;
            nt.extrapVerifyPrevPrev = lastWrite.prevPrevTick;
            nt.extrapVerifyFlags = chainFlags;
            nt.extrapVerifyThrough = currentTick;
            nt.hasExtrapVerify = true;

            return true;
        }
    }
}

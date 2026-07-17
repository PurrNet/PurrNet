using System;
using PurrNet.Packing;

namespace PurrNet
{
    internal enum NetworkTransformFrame : byte
    {
        World = 0,
        LocalStatic = 1,
        LocalIdentity = 2
    }

    /// <summary>
    /// A frame-tagged transform sample used by the unreliable sync path.
    /// The frame travels with the sample so decoding never depends on the
    /// receiver's hierarchy timing.
    /// </summary>
    internal struct NetworkTransformState : IEquatable<NetworkTransformState>
    {
        public NetworkTransformData data;
        public NetworkTransformFrame frame;
        public NetworkID parentId;

        public bool Equals(NetworkTransformState other)
        {
            return frame == other.frame && parentId.Equals(other.parentId) && data.Equals(other.data);
        }

        public override bool Equals(object obj) => obj is NetworkTransformState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(data, (byte)frame, parentId);
    }

    /// <summary>
    /// Per-field velocity in quantized units per sequence step, derived identically on both
    /// peers from decoded states — never sent on the wire. Enables second-order deltas:
    /// diffs are encoded against baseline + velocity * distance.
    /// </summary>
    internal struct NetworkTransformVelocity
    {
        public int posX, posY, posZ;
        public int scaleX, scaleY, scaleZ;
        public short rotX, rotY, rotZ, rotW;

        public bool isZero => posX == 0 && posY == 0 && posZ == 0 &&
                              scaleX == 0 && scaleY == 0 && scaleZ == 0 &&
                              rotX == 0 && rotY == 0 && rotZ == 0 && rotW == 0;

        // Velocities are fixed-point: raw quantization units per tick scaled by 1 << FRACTION_BITS.
        // Keeps the worst-case rotation diff inside the NormalizedFloat 15-bit prefix budget:
        // |pred| <= 1024 + (MAX_ROT >> FRACTION_BITS)*dist, zigzag(|diff|) must stay <= 32767,
        // which caps MAX_PREDICTED_BASELINE_AGE at 55 with MAX_ROT >> FRACTION_BITS = 256.
        // Revisit BOTH if either changes.
        public const int FRACTION_BITS = 6;
        const long HALF_STEP = 1L << (FRACTION_BITS - 1);
        const long MAX_ROT = 256L << FRACTION_BITS;
        const long MAX_POS = 1L << (20 + FRACTION_BITS);

        static short ClampRot(long v) => (short)Math.Clamp(v, -MAX_ROT, MAX_ROT);
        static int ClampPos(long v) => (int)Math.Clamp(v, -MAX_POS, MAX_POS);
        static int ClampInt(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);

        static long RoundDiv(long value, long divisor)
        {
            long half = divisor / 2;
            return (value + (value >= 0 ? half : -half)) / divisor;
        }

        static long Scale(long velocity, int dist)
        {
            return (velocity * dist + HALF_STEP) >> FRACTION_BITS;
        }

        public static NetworkTransformVelocity Derive(in NetworkTransformState from, in NetworkTransformState to, int dist)
        {
            if (dist <= 0)
                return default;

            if (from.frame != to.frame || !from.parentId.Equals(to.parentId))
                return default;

            var v = default(NetworkTransformVelocity);

            // Absolute-frame (double3) positions stay first-order; bit-pattern diffs don't linearize.
            if (from.data.position.HasValue && to.data.position.HasValue)
            {
                var a = from.data.position.Value;
                var b = to.data.position.Value;
                v.posX = ClampPos(RoundDiv((b.x.rounded - (long)a.x.rounded) << FRACTION_BITS, dist));
                v.posY = ClampPos(RoundDiv((b.y.rounded - (long)a.y.rounded) << FRACTION_BITS, dist));
                v.posZ = ClampPos(RoundDiv((b.z.rounded - (long)a.z.rounded) << FRACTION_BITS, dist));
            }

            v.rotX = ClampRot(RoundDiv((to.data.rotation.x.value - from.data.rotation.x.value) << FRACTION_BITS, dist));
            v.rotY = ClampRot(RoundDiv((to.data.rotation.y.value - from.data.rotation.y.value) << FRACTION_BITS, dist));
            v.rotZ = ClampRot(RoundDiv((to.data.rotation.z.value - from.data.rotation.z.value) << FRACTION_BITS, dist));
            v.rotW = ClampRot(RoundDiv((to.data.rotation.w.value - from.data.rotation.w.value) << FRACTION_BITS, dist));

            v.scaleX = ClampPos(RoundDiv((to.data.scale.x.rounded - (long)from.data.scale.x.rounded) << FRACTION_BITS, dist));
            v.scaleY = ClampPos(RoundDiv((to.data.scale.y.rounded - (long)from.data.scale.y.rounded) << FRACTION_BITS, dist));
            v.scaleZ = ClampPos(RoundDiv((to.data.scale.z.rounded - (long)from.data.scale.z.rounded) << FRACTION_BITS, dist));

            return v;
        }

        public static NetworkTransformState Predict(in NetworkTransformState baseline, in NetworkTransformVelocity v, int dist)
        {
            var s = baseline;

            if (s.data.position.HasValue)
            {
                var p = s.data.position.Value;
                s.data.position = new CompressedVector3(
                    new CompressedFloat(ClampInt(p.x.rounded + Scale(v.posX, dist))),
                    new CompressedFloat(ClampInt(p.y.rounded + Scale(v.posY, dist))),
                    new CompressedFloat(ClampInt(p.z.rounded + Scale(v.posZ, dist))));
            }

            var r = s.data.rotation;
            r.x = new NormalizedFloat(r.x.value + Scale(v.rotX, dist));
            r.y = new NormalizedFloat(r.y.value + Scale(v.rotY, dist));
            r.z = new NormalizedFloat(r.z.value + Scale(v.rotZ, dist));
            r.w = new NormalizedFloat(r.w.value + Scale(v.rotW, dist));
            s.data.rotation = r;

            var sc = s.data.scale;
            s.data.scale = new CompressedVector3(
                new CompressedFloat(ClampInt(sc.x.rounded + Scale(v.scaleX, dist))),
                new CompressedFloat(ClampInt(sc.y.rounded + Scale(v.scaleY, dist))),
                new CompressedFloat(ClampInt(sc.z.rounded + Scale(v.scaleZ, dist))));

            return s;
        }
    }
}

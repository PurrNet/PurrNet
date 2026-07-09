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

        // Keeps the worst-case rotation diff inside the NormalizedFloat 15-bit prefix budget:
        // |pred| <= 1024 + MAX_ROT*dist, zigzag(|diff|) must stay <= 32767, which caps
        // MAX_PREDICTED_BASELINE_AGE at 55 with MAX_ROT = 256. Revisit BOTH if either changes.
        const long MAX_ROT = 256;
        const long MAX_POS = 1L << 20;

        static short ClampRot(long v) => (short)Math.Clamp(v, -MAX_ROT, MAX_ROT);
        static int ClampPos(long v) => (int)Math.Clamp(v, -MAX_POS, MAX_POS);
        static int ClampInt(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);

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
                v.posX = ClampPos((b.x.rounded - (long)a.x.rounded) / dist);
                v.posY = ClampPos((b.y.rounded - (long)a.y.rounded) / dist);
                v.posZ = ClampPos((b.z.rounded - (long)a.z.rounded) / dist);
            }

            v.rotX = ClampRot((to.data.rotation.x.value - from.data.rotation.x.value) / dist);
            v.rotY = ClampRot((to.data.rotation.y.value - from.data.rotation.y.value) / dist);
            v.rotZ = ClampRot((to.data.rotation.z.value - from.data.rotation.z.value) / dist);
            v.rotW = ClampRot((to.data.rotation.w.value - from.data.rotation.w.value) / dist);

            v.scaleX = ClampPos((to.data.scale.x.rounded - (long)from.data.scale.x.rounded) / dist);
            v.scaleY = ClampPos((to.data.scale.y.rounded - (long)from.data.scale.y.rounded) / dist);
            v.scaleZ = ClampPos((to.data.scale.z.rounded - (long)from.data.scale.z.rounded) / dist);

            return v;
        }

        public static NetworkTransformState Predict(in NetworkTransformState baseline, in NetworkTransformVelocity v, int dist)
        {
            var s = baseline;

            if (s.data.position.HasValue)
            {
                var p = s.data.position.Value;
                s.data.position = new CompressedVector3(
                    new CompressedFloat(ClampInt(p.x.rounded + (long)v.posX * dist)),
                    new CompressedFloat(ClampInt(p.y.rounded + (long)v.posY * dist)),
                    new CompressedFloat(ClampInt(p.z.rounded + (long)v.posZ * dist)));
            }

            var r = s.data.rotation;
            r.x = new NormalizedFloat(r.x.value + (long)v.rotX * dist);
            r.y = new NormalizedFloat(r.y.value + (long)v.rotY * dist);
            r.z = new NormalizedFloat(r.z.value + (long)v.rotZ * dist);
            r.w = new NormalizedFloat(r.w.value + (long)v.rotW * dist);
            s.data.rotation = r;

            var sc = s.data.scale;
            s.data.scale = new CompressedVector3(
                new CompressedFloat(ClampInt(sc.x.rounded + (long)v.scaleX * dist)),
                new CompressedFloat(ClampInt(sc.y.rounded + (long)v.scaleY * dist)),
                new CompressedFloat(ClampInt(sc.z.rounded + (long)v.scaleZ * dist)));

            return s;
        }
    }
}

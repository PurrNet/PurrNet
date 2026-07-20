using System;
using PurrNet.Transports;

namespace PurrNet.Profiler
{
    public readonly struct DroppedSample : IEquatable<DroppedSample>
    {
        /// <summary>Resolved payload type of the dropped message, or null when it could not be recovered.</summary>
        public readonly Type type;
        public readonly FragmentDropReason reason;
        public readonly int bytes;

        public DroppedSample(Type type, FragmentDropReason reason, int bytes)
        {
            this.type = type;
            this.reason = reason;
            this.bytes = bytes;
        }

        public bool Equals(DroppedSample other)
        {
            return type == other.type && reason == other.reason && bytes == other.bytes;
        }

        public override bool Equals(object obj)
        {
            return obj is DroppedSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(type, (int)reason, bytes);
        }
    }
}

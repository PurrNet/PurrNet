using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    /// <summary>
    /// Legacy NetworkTransform packet retained for source and binary compatibility.
    /// The built-in NetworkTransform module now uses the acknowledged unreliable stream below.
    /// </summary>
    public struct NetworkTransformDelta : IPackedAuto
    {
        public SceneID scene;
        public readonly ByteData packet;

        public NetworkTransformDelta(SceneID context, BitPacker packer)
        {
            scene = context;
            packet = packer.ToByteData();
        }
    }

    internal struct NetworkTransformUnreliableDelta : IPackedAuto
    {
        public SceneID scene;
        public ushort seq;
        public NetworkTransformUnreliableAckHeader? ack;
        public readonly ByteData packet;

        public NetworkTransformUnreliableDelta(SceneID context, ushort seq, BitPacker packer)
        {
            scene = context;
            this.seq = seq;
            ack = null;
            packet = packer.ToByteData();
        }
    }

    /// <summary>
    /// Compact piggybacked acknowledgement. Keeping the sequence and mask as their
    /// natural widths saves 16 bits over encoding them together in a nullable ulong.
    /// </summary>
    internal struct NetworkTransformUnreliableAckHeader : IPackedAuto
    {
        public ushort seq;
        public uint ackBits;
    }

    internal struct NetworkTransformUnreliableAck : IPackedAuto
    {
        public SceneID scene;
        public ushort seq;
        public uint ackBits;
    }

    internal struct NetworkTransformUnreliableNack : IPackedAuto
    {
        public SceneID scene;
        public NetworkID id;
    }

    internal struct NTUnreliableEntry
    {
        public NetworkID nid;
        public NetworkTransformState state;
        public NetworkTransformVelocity velocity;
        public byte gen;
        // Send-side only: non-wrapping epoch behind the byte gen.
        public uint genEpoch;
        // Send-side only: revision of the captured state, used for a cheap unchanged check.
        public uint revision;
    }

    internal struct NTUnreliableBaseline
    {
        public NetworkTransformState state;
        public NetworkTransformVelocity velocity;
        public byte gen;
        public uint genEpoch;
        public uint revision;
        // Monotonic packet order — ushort seq wraps, so age math uses this instead.
        public uint order;
    }

    internal struct NTUnreliableGeneration
    {
        public byte gen;
        public uint epoch;
    }

    internal struct NTUnreliableSlot
    {
        public bool used;
        public bool acked;
        public ushort seq;
        public uint order;
        public List<NTUnreliableEntry> entries;
    }

    internal class NTUnreliableSendStream
    {
        public ushort nextSeq = 1;
        public uint nextOrder = 1;
        public long budgetBits;
        // Sorted by NetworkID. Once initialized, only transforms with an unacknowledged
        // revision remain here, avoiding a full visible-transform scan every tick.
        public bool pendingInitialized;
        public readonly List<NetworkTransform> pending = new();
        // NACK barrier: only packets written AFTER the NACK may re-establish a baseline,
        // else the ack covering the NACKed packet resurrects the phantom (acks are cumulative).
        public readonly Dictionary<NetworkID, uint> nackFloor = new();
        public readonly Dictionary<NetworkID, NTUnreliableBaseline> acked = new();
        // A targeted reliable reset advances the NetworkTransform's global generation, while
        // unaffected peers remain on their existing wire generation until the next global reset.
        public readonly Dictionary<NetworkID, NTUnreliableGeneration> generationOverrides = new();
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal class NTUnreliableRecvStream
    {
        public bool ackInit;
        public ushort latestSeq;
        public long latestOrder;
        public uint ackBits;
        public bool ackDirty;
        public byte ackDelayTicks;
        public byte packetsSinceAck;
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal static class NTUnreliable
    {
        public const int DISTANCE_BITS = 8;
        public const int RING_SIZE = 1 << DISTANCE_BITS;
        // A baseline remains usable for the full receive history. Prediction has a smaller bound:
        // beyond it, rotation extrapolation can overflow NormalizedFloat's delta prefix budget, so
        // both peers deterministically encode against the raw baseline instead.
        public const int MAX_BASELINE_AGE = RING_SIZE;
        public const int MAX_PREDICTED_BASELINE_AGE = 48;
        // Low-volume streams do not need an application-level ACK every network tick. High-volume
        // streams flush before the 32-packet selective-ACK window can leave a permanent blind spot.
        public const int ACK_INTERVAL_TICKS = 4;
        public const int ACK_PACKET_THRESHOLD = 24;

        public static NetworkTransformState GetDeltaPrediction(in NetworkTransformState baseline,
            in NetworkTransformVelocity velocity, int distance)
        {
            return distance <= MAX_PREDICTED_BASELINE_AGE
                ? NetworkTransformVelocity.Predict(baseline, velocity, distance)
                : baseline;
        }

        public static bool ShouldApplyOrder(bool hasApplied, long lastApplied, long incoming)
        {
            return !hasApplied || incoming > lastApplied;
        }

        public static void Release(NTUnreliableSlot[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i].entries != null)
                    ListPool<NTUnreliableEntry>.Destroy(ring[i].entries);
                ring[i] = default;
            }
        }
    }
}

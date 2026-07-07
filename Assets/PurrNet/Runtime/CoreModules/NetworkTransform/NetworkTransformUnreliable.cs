using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    public struct NetworkTransformUnreliableDelta : IPackedAuto
    {
        public SceneID scene;
        public ushort seq;
        // Piggybacked ack for the reverse stream: (latestSeq << 32) | ackBits.
        public ulong? ack;
        public readonly ByteData packet;

        public NetworkTransformUnreliableDelta(SceneID context, ushort seq, BitPacker packer)
        {
            scene = context;
            this.seq = seq;
            ack = null;
            packet = packer.ToByteData();
        }
    }

    public struct NetworkTransformUnreliableAck : IPackedAuto
    {
        public SceneID scene;
        public ushort seq;
        public uint ackBits;
    }

    public struct NetworkTransformUnreliableNack : IPackedAuto
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
    }

    internal struct NTUnreliableBaseline
    {
        public NetworkTransformState state;
        public NetworkTransformVelocity velocity;
        public byte gen;
        // Monotonic packet order — ushort seq wraps, so age math uses this instead.
        public uint order;
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
        public int budgetBits;
        // NACK barrier: only packets written AFTER the NACK may re-establish a baseline,
        // else the ack covering the NACKed packet resurrects the phantom (acks are cumulative).
        public readonly Dictionary<NetworkID, uint> nackFloor = new();
        public readonly Dictionary<NetworkID, NTUnreliableBaseline> acked = new();
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal class NTUnreliableRecvStream
    {
        public bool ackInit;
        public ushort latestSeq;
        public uint ackBits;
        public bool ackDirty;
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal static class NTUnreliable
    {
        public const int RING_SIZE = 64;
        // Must stay below RING_SIZE, fit the 6-bit dist-1 wire field (1..64), AND stay <= 55
        // or predicted rotation diffs overflow the NormalizedFloat prefix budget (see MAX_ROT).
        public const int MAX_BASELINE_AGE = 48;

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

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.TestTools;

public class DeltaModuleBaselineResyncTests
{
    const uint KeyHash = 4711;

    static readonly Regex MissingBaselineLog = new Regex("Delta baseline \\d+ missing");

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
        NetworkManager.CallAllRegisters();
    }

    static PlayerID Player(int i) => new PlayerID((ulong)i, false);

    sealed class Wire<T>
    {
        public readonly DeltaModule sender;
        public DeltaModule receiver = new DeltaModule(null, null);
        public readonly PlayerID target;
        public uint lastReceivedId;
        private readonly Dictionary<uint, T> _history = new();

        public Wire(DeltaModule sender = null, PlayerID? target = null)
        {
            this.sender = sender ?? new DeltaModule(null, null);
            this.target = target ?? Player(2);
        }

        public BitPacker Write(T value)
        {
            var packer = BitPackerPool.Get();
            PackedUInt cachedKey = default;
            sender.Write(packer, target, KeyHash, value, ref cachedKey);
            packer.ResetPositionAndMode(true);
            return packer;
        }

        public static uint PeekBaseline(BitPacker packer)
        {
            int pos = packer.positionInBits;
            PackedUInt baseline = default;
            DeltaPacker<PackedUInt>.Read(packer, default, ref baseline);
            packer.SetBitPosition(pos);
            return baseline.value;
        }

        public uint PeekValueId(BitPacker packer)
        {
            int pos = packer.positionInBits;
            PackedUInt baseline = default;
            DeltaPacker<PackedUInt>.Read(packer, default, ref baseline);
            bool changed = false;
            Packer<bool>.Read(packer, ref changed);
            uint id = 0;
            if (changed)
            {
                T old = baseline.value != 0 ? _history[baseline.value] : default;
                T decoded = default;
                DeltaPacker<T>.Read(packer, old, ref decoded);
                PackedUInt valueId = default;
                DeltaPacker<PackedUInt>.Read(packer, baseline, ref valueId);
                id = valueId.value;
                _history[id] = decoded;
            }
            packer.SetBitPosition(pos);
            return id;
        }

        public T Read(BitPacker packer)
        {
            T value = default;
            PackedUInt cachedKey = default;
            receiver.Read(packer, KeyHash, PlayerID.Server, ref value, ref cachedKey);
            return value;
        }

        public T Deliver(T value, bool ack = true)
        {
            using var packer = Write(value);
            uint id = PeekValueId(packer);
            var decoded = Read(packer);
            if (id != 0)
                lastReceivedId = id;
            if (ack && lastReceivedId != 0)
                sender.ConfirmDeliveryForTests<T>(target, KeyHash, new PackedUInt(lastReceivedId));
            return decoded;
        }

        public void LoseReceiverHistory()
        {
            receiver = new DeltaModule(null, null);
            _history.Clear();
        }
    }

    static void AssertRejected<T>(Wire<T> wire, BitPacker packer, uint expectedMissing)
    {
        Assert.That(Wire<T>.PeekBaseline(packer), Is.EqualTo(expectedMissing),
            "Sender should still reference the baseline the receiver lost");

        LogAssert.Expect(LogType.Error, MissingBaselineLog);
        Assert.Throws<BypassLoggingException>(() => wire.Read(packer),
            "A delta against a baseline the receiver does not have must be dropped, not decoded against default");
    }

    [Test]
    public void PerPlayer_ChangedValue_AgainstMissingBaseline_IsDropped()
    {
        Assert.That(DeltaSharedEncodeInfo<string>.eligible, Is.False);

        var wire = new Wire<string>();
        Assert.That(wire.Deliver("rigidbody state A"), Is.EqualTo("rigidbody state A"));
        Assert.That(wire.Deliver("rigidbody state B"), Is.EqualTo("rigidbody state B"));

        uint acked = wire.lastReceivedId;
        wire.LoseReceiverHistory();

        using var packer = wire.Write("rigidbody state C");
        AssertRejected(wire, packer, acked);
    }

    [Test]
    public void PerPlayer_UnchangedValue_AgainstMissingBaseline_IsDropped()
    {
        var wire = new Wire<string>();
        wire.Deliver("idle");

        uint acked = wire.lastReceivedId;
        wire.LoseReceiverHistory();

        using var packer = wire.Write("idle");
        AssertRejected(wire, packer, acked);
    }

    [Test]
    public void PerPlayer_ReportedMissingBaseline_ResyncsWithFullWrite()
    {
        var wire = new Wire<string>();
        wire.Deliver("first");
        wire.Deliver("second");

        uint lost = wire.lastReceivedId;
        wire.LoseReceiverHistory();

        using (var rejected = wire.Write("third"))
            AssertRejected(wire, rejected, lost);

        wire.sender.ReportMissingBaselineForTests<string>(wire.target, KeyHash, lost);

        using (var full = wire.Write("fourth"))
        {
            Assert.That(Wire<string>.PeekBaseline(full), Is.EqualTo(0u),
                "After the receiver reports a missing baseline the next write must be a full write");
            Assert.That(wire.Read(full), Is.EqualTo("fourth"));
        }

        wire.sender.ConfirmDeliveryForTests<string>(wire.target, KeyHash, new PackedUInt(lost));
        using (var afterStaleAck = wire.Write("fifth"))
        {
            Assert.That(Wire<string>.PeekBaseline(afterStaleAck), Is.EqualTo(0u),
                "A late ack of the reported baseline must not revive it");
            Assert.That(wire.Read(afterStaleAck), Is.EqualTo("fifth"));
        }

        Assert.That(wire.Deliver("sixth"), Is.EqualTo("sixth"));
        Assert.That(wire.Deliver("seventh"), Is.EqualTo("seventh"));
    }

    [Test]
    public void Shared_ChangedValue_AgainstMissingBaseline_IsDropped()
    {
        Assert.That(DeltaSharedEncodeInfo<Vector3>.eligible, Is.True);

        var wire = new Wire<Vector3>();
        wire.Deliver(new Vector3(1, 2, 3));
        wire.Deliver(new Vector3(4, 5, 6));

        uint acked = wire.lastReceivedId;
        wire.LoseReceiverHistory();

        using var packer = wire.Write(new Vector3(7, 8, 9));
        AssertRejected(wire, packer, acked);
    }

    [Test]
    public void Shared_ReportedMissingBaseline_ResyncsWithFullWrite()
    {
        var wire = new Wire<Vector3>();
        wire.Deliver(new Vector3(1, 2, 3));
        wire.Deliver(new Vector3(4, 5, 6));

        uint lost = wire.lastReceivedId;
        wire.LoseReceiverHistory();

        using (var rejected = wire.Write(new Vector3(7, 8, 9)))
            AssertRejected(wire, rejected, lost);

        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);

        using (var full = wire.Write(new Vector3(10, 11, 12)))
        {
            Assert.That(Wire<Vector3>.PeekBaseline(full), Is.EqualTo(0u));
            Assert.That(wire.Read(full), Is.EqualTo(new Vector3(10, 11, 12)));
        }

        wire.sender.ConfirmDeliveryForTests<Vector3>(wire.target, KeyHash, new PackedUInt(lost));
        using (var afterStaleAck = wire.Write(new Vector3(13, 14, 15)))
        {
            Assert.That(Wire<Vector3>.PeekBaseline(afterStaleAck), Is.EqualTo(0u));
            Assert.That(wire.Read(afterStaleAck), Is.EqualTo(new Vector3(13, 14, 15)));
        }

        Assert.That(wire.Deliver(new Vector3(16, 17, 18)), Is.EqualTo(new Vector3(16, 17, 18)));
        Assert.That(wire.Deliver(new Vector3(19, 20, 21)), Is.EqualTo(new Vector3(19, 20, 21)));
    }

    [Test]
    public void ReportForSupersededBaseline_IsIgnored()
    {
        var wire = new Wire<string>();
        wire.Deliver("one");
        uint stale = wire.lastReceivedId;
        wire.Deliver("two");
        uint current = wire.lastReceivedId;
        Assert.That(current, Is.GreaterThan(stale));

        wire.sender.ReportMissingBaselineForTests<string>(wire.target, KeyHash, stale);

        using var packer = wire.Write("three");
        Assert.That(Wire<string>.PeekBaseline(packer), Is.EqualTo(current),
            "A late report for an already-superseded baseline must not force a full write");
        Assert.That(wire.Read(packer), Is.EqualTo("three"));
    }

    [Test]
    public void Shared_UnchangedResync_RejectsStaleAckAndResumesDeltaCompression()
    {
        var value = new Vector3(1, 2, 3);
        var wire = new Wire<Vector3>();
        wire.Deliver(value);
        uint lost = wire.lastReceivedId;
        wire.LoseReceiverHistory();
        using (var rejected = wire.Write(value))
            AssertRejected(wire, rejected, lost);

        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        wire.sender.ConfirmDeliveryForTests<Vector3>(wire.target, KeyHash, lost);
        Assert.That(wire.Deliver(value, false), Is.EqualTo(value));
        uint restored = wire.lastReceivedId;
        Assert.That(restored, Is.GreaterThan(lost), "A restored baseline needs a fresh ID to distinguish its ACK from a stale ACK");

        wire.sender.ConfirmDeliveryForTests<Vector3>(wire.target, KeyHash, restored);
        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        using var next = wire.Write(value);
        Assert.That(Wire<Vector3>.PeekBaseline(next), Is.EqualTo(restored));
        Assert.That(wire.Read(next), Is.EqualTo(value));
    }

    [Test]
    public void Shared_LostResyncAck_RetriesUnchangedValueWithSameFreshId()
    {
        var value = new Vector3(1, 2, 3);
        var wire = new Wire<Vector3>();
        wire.Deliver(value);
        uint lost = wire.lastReceivedId;
        wire.LoseReceiverHistory();
        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        wire.Deliver(value, false);
        uint restored = wire.lastReceivedId;
        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        wire.Deliver(value);
        Assert.That(wire.lastReceivedId, Is.EqualTo(restored), "A duplicate NACK must not allocate another ID on every retry");
        using var next = wire.Write(value);
        Assert.That(Wire<Vector3>.PeekBaseline(next), Is.EqualTo(restored), "A retried full write must become an acknowledged baseline");
    }

    [Test]
    public void Shared_ResyncWithNewerUnackedHead_ReusesThatHead()
    {
        var wire = new Wire<Vector3>();
        wire.Deliver(new Vector3(1, 2, 3));
        uint lost = wire.lastReceivedId;
        var nextValue = new Vector3(4, 5, 6);
        wire.Deliver(nextValue, false);
        uint pending = wire.lastReceivedId;
        wire.LoseReceiverHistory();
        wire.sender.ReportMissingBaselineForTests<Vector3>(wire.target, KeyHash, lost);
        wire.Deliver(nextValue);
        Assert.That(wire.lastReceivedId, Is.EqualTo(pending));
        using var next = wire.Write(nextValue);
        Assert.That(Wire<Vector3>.PeekBaseline(next), Is.EqualTo(pending));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownMissingId_DoesNotBlockFutureAcks(bool shared)
    {
        if (shared)
            AssertUnknownMissingIdIgnored(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        else
            AssertUnknownMissingIdIgnored("first", "second");
    }

    static void AssertUnknownMissingIdIgnored<T>(T first, T second)
    {
        var wire = new Wire<T>();
        wire.Deliver(first);
        wire.sender.ReportMissingBaselineForTests<T>(wire.target, KeyHash, uint.MaxValue);
        wire.Deliver(second);
        uint confirmed = wire.lastReceivedId;
        using var next = wire.Write(second);
        Assert.That(Wire<T>.PeekBaseline(next), Is.EqualTo(confirmed), "A report for an ID never used as a baseline must not poison the ACK floor");
    }

    [Test]
    public void Shared_ResyncForOnePlayer_PreservesOtherPlayersBaseline()
    {
        var sender = new DeltaModule(null, null);
        var recovering = new Wire<Vector3>(sender, Player(2));
        var healthy = new Wire<Vector3>(sender, Player(3));
        var value = new Vector3(1, 2, 3);
        recovering.Deliver(value);
        healthy.Deliver(value);
        uint healthyBaseline = healthy.lastReceivedId;
        recovering.LoseReceiverHistory();
        sender.ReportMissingBaselineForTests<Vector3>(recovering.target, KeyHash, recovering.lastReceivedId);
        recovering.Deliver(value);
        using (var next = healthy.Write(value))
        {
            Assert.That(Wire<Vector3>.PeekBaseline(next), Is.EqualTo(healthyBaseline));
            Assert.That(healthy.Read(next), Is.EqualTo(value));
        }
        using var recovered = recovering.Write(value);
        Assert.That(Wire<Vector3>.PeekBaseline(recovered), Is.EqualTo(recovering.lastReceivedId));
    }

    [Test]
    public void Shared_ResyncAfterPlayerSlotArrayGrowth_ResumesDeltaCompression()
    {
        var sender = new DeltaModule(null, null);
        var value = new Vector3(1, 2, 3);
        Wire<Vector3> last = null;
        for (int i = 1; i <= 24; i++)
        {
            last = new Wire<Vector3>(sender, Player(i));
            last.Deliver(value);
        }
        uint lost = last.lastReceivedId;
        last.LoseReceiverHistory();
        sender.ReportMissingBaselineForTests<Vector3>(last.target, KeyHash, lost);
        last.Deliver(value);
        using var next = last.Write(value);
        Assert.That(Wire<Vector3>.PeekBaseline(next), Is.EqualTo(last.lastReceivedId));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResyncToDefault_ThenNonDefault_DecodesAndAcknowledges(bool shared)
    {
        if (shared)
            AssertResyncToDefault(new Vector3(1, 2, 3));
        else
            AssertResyncToDefault("non-default");
    }

    static void AssertResyncToDefault<T>(T value)
    {
        var wire = new Wire<T>();
        wire.Deliver(value);
        wire.LoseReceiverHistory();
        wire.sender.ReportMissingBaselineForTests<T>(wire.target, KeyHash, wire.lastReceivedId);
        Assert.That(wire.Deliver(default), Is.EqualTo(default(T)));
        Assert.That(wire.Deliver(value), Is.EqualTo(value));
        using var next = wire.Write(value);
        Assert.That(Wire<T>.PeekBaseline(next), Is.EqualTo(wire.lastReceivedId));
        Assert.That(wire.Read(next), Is.EqualTo(value));
    }

    // These roles use real broadcast serialization, subscription routing and ACK batching.
    // Only the transport is controlled, so packet loss/reordering is deterministic.
    sealed class Role : IDisposable
    {
        public readonly GameObject gameObject;
        public readonly DeltaResyncTestTransport transport;
        public readonly BroadcastModule broadcast;
        public readonly PlayersBroadcaster playerBroadcast;
        public readonly DeltaModule delta;
        public readonly bool asServer;
        public readonly Connection connection = new Connection(42);

        public Role(bool asServer)
        {
            this.asServer = asServer;
            gameObject = new GameObject("Delta resync test role");
            gameObject.SetActive(false);
            transport = gameObject.AddComponent<DeltaResyncTestTransport>();
            var manager = gameObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            manager.transport = transport;
            broadcast = new BroadcastModule(manager, asServer);
            var players = new PlayersManager(manager, null, broadcast);
            typeof(PlayersManager).GetField("_asServer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(players, asServer);
            // Authentication itself is covered by PlayersBroadcastAuthenticationTests.
            ((Dictionary<Connection, PlayerID>)typeof(PlayersManager).GetField("_connectionToPlayerId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(players))[connection] = Player(2);
            ((Dictionary<PlayerID, Connection>)typeof(PlayersManager).GetField("_playerToConnection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(players))[Player(2)] = connection;
            playerBroadcast = new PlayersBroadcaster(broadcast, players);
            playerBroadcast.Enable(asServer);
            delta = new DeltaModule(players, playerBroadcast);
            delta.Enable(asServer);
        }

        public void SendControlsTo(Role receiver)
        {
            var packets = transport.sent.ToArray();
            transport.sent.Clear();
            foreach (var packet in packets)
                receiver.broadcast.OnDataReceived(receiver.connection, new ByteData(packet.bytes), receiver.asServer);
        }

        public void Dispose()
        {
            delta.Disable(asServer);
            playerBroadcast.Disable(asServer);
            broadcast.Disable(asServer);
            gameObject.GetComponent<NetworkManager>().transport = null;
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void MissingBaseline_RequestSurvivesUnreliableLoss_AndRoutesToSender(bool receiverAsServer, bool shared)
    {
        if (shared)
            AssertControlRecovery(receiverAsServer, new Vector3(1, 2, 3));
        else
            AssertControlRecovery(receiverAsServer, "unchanged value");
    }

    static void AssertControlRecovery<T>(bool receiverAsServer, T value)
    {
        using var sender = new Role(!receiverAsServer);
        using var receiver = new Role(receiverAsServer);
        var target = sender.asServer ? Player(2) : default;
        var origin = sender.asServer ? PlayerID.Server : Player(2);
        using (var initial = BitPackerPool.Get())
        {
            sender.delta.Write(initial, target, KeyHash, value);
            initial.ResetPositionAndMode(true);
            var decoded = default(T);
            receiver.delta.Read(initial, KeyHash, origin, ref decoded);
        }
        receiver.delta.PostFixedUpdate();
        receiver.SendControlsTo(sender);
        receiver.delta.Disable(receiver.asServer);
        receiver.delta.Enable(receiver.asServer);

        using (var rejected = BitPackerPool.Get())
        {
            sender.delta.Write(rejected, target, KeyHash, value);
            rejected.ResetPositionAndMode(true);
            LogAssert.Expect(LogType.Error, MissingBaselineLog);
            var decoded = default(T);
            Assert.Throws<BypassLoggingException>(() => receiver.delta.Read(rejected, KeyHash, origin, ref decoded));
        }
        Assert.That(receiver.transport.sent.Count, Is.EqualTo(1));
        Assert.That(receiver.transport.sent[0].channel, Is.EqualTo(Channel.ReliableUnordered), "The only reset request must survive unreliable packet loss even if updates stop");
        if (receiver.asServer)
            Assert.That(receiver.transport.sent[0].connection, Is.EqualTo(receiver.connection));
        receiver.transport.sent.RemoveAll(packet => packet.channel == Channel.Unreliable || packet.channel == Channel.UnreliableSequenced);
        receiver.SendControlsTo(sender);

        using (var full = BitPackerPool.Get())
        {
            sender.delta.Write(full, target, KeyHash, value);
            full.ResetPositionAndMode(true);
            Assert.That(Wire<T>.PeekBaseline(full), Is.Zero);
            var decoded = default(T);
            receiver.delta.Read(full, KeyHash, origin, ref decoded);
            Assert.That(decoded, Is.EqualTo(value));
        }
        receiver.delta.PostFixedUpdate();
        receiver.SendControlsTo(sender);
        using var next = BitPackerPool.Get();
        sender.delta.Write(next, target, KeyHash, value);
        next.ResetPositionAndMode(true);
        Assert.That(Wire<T>.PeekBaseline(next), Is.GreaterThan(0u), "The real post-resync ACK must restore delta compression");
    }

    [Test]
    public void AckBatches_SplitAtMtu_ResetAllDeltaCaches()
    {
        using var sender = new Role(true);
        using var receiver = new Role(false);
        receiver.transport.mtu = 64;
        const uint count = 32;
        for (uint key = 1; key <= count; key++)
        {
            using var packet = BitPackerPool.Get();
            sender.delta.Write(packet, Player(2), key, new Vector3(key, 2, 3));
            packet.ResetPositionAndMode(true);
            var decoded = default(Vector3);
            receiver.delta.Read(packet, key, PlayerID.Server, ref decoded);
        }
        receiver.delta.PostFixedUpdate();
        Assert.That(receiver.transport.sent.Count, Is.GreaterThan(1), "The scenario must actually cross a packet boundary");
        receiver.transport.sent.Reverse();
        receiver.SendControlsTo(sender);
        for (uint key = 1; key <= count; key++)
        {
            using var packet = BitPackerPool.Get();
            sender.delta.Write(packet, Player(2), key, new Vector3(key, 2, 3));
            packet.ResetPositionAndMode(true);
            Assert.That(Wire<Vector3>.PeekBaseline(packet), Is.GreaterThan(0u), $"ACK for key {key} was corrupted by the MTU split");
        }
    }
}

public class DeltaResyncTestTransport : LocalTransport, ITransport
{
    public struct Packet
    {
        public Connection connection;
        public Channel channel;
        public byte[] bytes;
    }

    public int mtu = 1024;
    public readonly List<Packet> sent = new();
    bool ITransport.SupportsChannel(Channel channel) => true;
    int ITransport.GetMTU(Connection target, Channel channel, bool asServer) => mtu;
    void ITransport.SendToClient(Connection target, ByteData data, Channel method) => Capture(target, data, method);
    void ITransport.SendToServer(ByteData data, Channel method) => Capture(default, data, method);

    void Capture(Connection connection, ByteData data, Channel channel)
    {
        sent.Add(new Packet { connection = connection, channel = channel, bytes = data.span.ToArray() });
    }
}

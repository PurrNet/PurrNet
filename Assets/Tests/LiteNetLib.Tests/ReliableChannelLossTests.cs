using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Layers;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public class ReliableChannelLossTests
    {
        private const string ConnectionKey = "reliable-loss-tests";

        private sealed class Receiver : INetEventListener
        {
            public readonly List<int> Sequences = new List<int>();
            public int Errors;

            public void OnPeerConnected(NetPeer peer) { }
            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }
            public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
            public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(ConnectionKey);
            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Errors++;
            public void OnNetworkReceiveUnconnected(IPEndPoint endPoint, NetPacketReader reader,
                UnconnectedMessageType messageType) => Errors++;

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
                DeliveryMethod deliveryMethod)
            {
                int sequence = reader.GetInt();
                int length = reader.AvailableBytes;
                for (int i = 0; i < length; i++)
                {
                    if (reader.RawData[reader.Position + i] != (byte)(sequence + i))
                    {
                        Errors++;
                        break;
                    }
                }
                Sequences.Add(sequence);
            }
        }

        // Drops inbound datagrams the rule rejects once enabled: a deterministic stand-in for a path.
        private sealed class DropLayer : PacketLayerBase
        {
            private readonly Func<byte[], int, bool> _drop;
            public bool Enabled;

            public DropLayer(Func<byte[], int, bool> drop) : base(0) => _drop = drop;

            public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int length)
            {
                if (Enabled && _drop(data, length))
                    length = 0;
            }

            public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length) { }
        }

        // Two-state loss: long clean stretches broken by bursts that drop most datagrams.
        private static Func<int, bool> BurstLoss(int seed)
        {
            var random = new Random(seed);
            bool bad = false;
            return _ =>
            {
                bad = bad ? random.NextDouble() >= 0.3 : random.NextDouble() < 0.05;
                return random.NextDouble() < (bad ? 0.6 : 0.02);
            };
        }

        private sealed class Pair : IDisposable
        {
            public readonly Receiver ServerListener = new Receiver();
            public readonly Receiver ClientListener = new Receiver();
            public readonly NetManager Server;
            public readonly NetManager Client;
            public readonly NetPeer ServerPeer;
            public readonly NetPeer ClientPeer;

            private readonly DropLayer _serverLayer;
            private readonly DropLayer _clientLayer;

            public Pair(int lossChance, bool mtuDiscovery = false, Func<int, bool> serverDrops = null,
                Func<int, bool> clientDrops = null, bool threaded = false, bool serverRepairs = true,
                bool clientRepairs = true, Func<byte[], int, bool> serverInspects = null,
                Func<byte[], int, bool> clientInspects = null)
            {
                _serverLayer = serverInspects != null ? new DropLayer(serverInspects)
                    : serverDrops == null ? null : new DropLayer((_, length) => serverDrops(length));
                _clientLayer = clientInspects != null ? new DropLayer(clientInspects)
                    : clientDrops == null ? null : new DropLayer((_, length) => clientDrops(length));
                Server = Create(ServerListener, lossChance, mtuDiscovery, serverRepairs, _serverLayer);
                Client = Create(ClientListener, lossChance, mtuDiscovery, clientRepairs, _clientLayer);
                try
                {
                    Assert.IsTrue(threaded ? Server.Start(0) : Server.StartInManualMode(0), "Server failed to bind.");
                    Assert.IsTrue(threaded ? Client.Start(0) : Client.StartInManualMode(0), "Client failed to bind.");
                    Client.Connect(new IPEndPoint(IPAddress.Loopback, Server.LocalPort), ConnectionKey);
                    for (int i = 0; i < 5000 && (Server.ConnectedPeersCount == 0 || Client.ConnectedPeersCount == 0); i++)
                    {
                        Pump();
                        Thread.Sleep(1);
                    }
                    Assert.AreEqual(1, Server.ConnectedPeersCount, "Loopback connection timed out.");
                    ServerPeer = Server.FirstPeer;
                    ClientPeer = Client.FirstPeer;
                    if (_serverLayer != null) _serverLayer.Enabled = true;
                    if (_clientLayer != null) _clientLayer.Enabled = true;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private static NetManager Create(Receiver listener, int lossChance, bool mtuDiscovery, bool repairs,
                PacketLayerBase layer) =>
                new NetManager(listener, layer)
                {
                    MtuDiscovery = mtuDiscovery,
                    ReliableRepairs = repairs,
                    AutoRecycle = true,
                    UseNativeSockets = false,
                    EnableStatistics = true,
                    PingInterval = 10000000,
                    DisconnectTimeout = 10000000,
                    SimulatePacketLoss = lossChance > 0,
                    SimulationPacketLossChance = Math.Max(1, lossChance)
                };

            public void Pump(float elapsedMilliseconds = 1)
            {
                Client.ManualUpdate(elapsedMilliseconds);
                Server.ManualUpdate(elapsedMilliseconds);
                Server.PollEvents();
                Client.PollEvents();
            }

            public void Stream(int count, int size) => Stream(count, _ => size);

            // pumpMilliseconds advances the MTU discovery timer; resends follow the real clock.
            public void Stream(int count, Func<int, int> sizeOf, float pumpMilliseconds = 1,
                DeliveryMethod method = DeliveryMethod.ReliableOrdered, NetPeer from = null)
            {
                for (int sequence = 0; sequence < count; sequence++)
                {
                    int size = sizeOf(sequence);
                    var payload = new byte[size];
                    BitConverter.TryWriteBytes(new Span<byte>(payload, 0, 4), sequence);
                    for (int i = 4; i < size; i++)
                        payload[i] = (byte)(sequence + i - 4);
                    (from ?? ServerPeer).Send(payload, method);
                    Pump(pumpMilliseconds);
                    Thread.Sleep(1);
                }
            }

            public bool Drain(int expected, int timeoutMs, Receiver listener = null)
            {
                listener = listener ?? ClientListener;
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (listener.Sequences.Count < expected && DateTime.UtcNow < deadline)
                {
                    Pump();
                    Thread.Sleep(1);
                }
                return listener.Sequences.Count >= expected;
            }

            public void Dispose()
            {
                Client?.Stop();
                Server?.Stop();
            }
        }

        private static void AssertExactlyOnceInOrder(Pair pair, int count) =>
            AssertExactlyOnceInOrder(pair.ClientListener, count);

        private static void AssertExactlyOnceInOrder(Receiver listener, int count)
        {
            Assert.Zero(listener.Errors, "a payload arrived corrupted");
            Assert.AreEqual(count, listener.Sequences.Count, "every message arrives exactly once");
            for (int i = 0; i < count; i++)
                Assert.AreEqual(i, listener.Sequences[i], "messages arrive in send order");
        }

        private static int MixedSize(int sequence) => sequence % 10 == 9 ? 3000 : 64 + sequence * 37 % 300;

        // A path that silently drops datagrams over 1200 bytes: discovery must settle below it, and
        // nothing that was already sized for the smaller MTU may be lost.
        [Test]
        public void ReliableOrdered_DiscoveryStopsBelowAPathThatDropsLargeDatagrams()
        {
            const int count = 150;
            using (var pair = new Pair(0, true, length => length > 1200, length => length > 1200))
            {
                pair.Stream(count, MixedSize, 100);
                Assert.IsTrue(pair.Drain(count, 15000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.LessOrEqual(pair.ServerPeer.Mtu, 1200, "discovery must not settle above what the path carries");
                Assert.Greater(pair.ServerPeer.Mtu, 1024, "discovery must still use the larger size the path allows");
            }
        }

        // Loss arrives in bursts on real links; the same seeded patterns drop data and acks alike.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void ReliableOrdered_DeliversUnderSeededBurstLoss(int seed)
        {
            const int count = 200;
            using (var pair = new Pair(0, false, BurstLoss(seed * 2), BurstLoss(seed * 2 + 1)))
            {
                pair.Stream(count, MixedSize);
                Assert.IsTrue(pair.Drain(count, 20000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "burst losses must be repaired");
            }
        }

        [Test]
        public void ReliableUnordered_DeliversEveryMessageOnceUnderLoss()
        {
            const int count = 200;
            using (var pair = new Pair(20))
            {
                pair.Stream(count, MixedSize, 1, DeliveryMethod.ReliableUnordered);
                Assert.IsTrue(pair.Drain(count, 20000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                Assert.Zero(pair.ClientListener.Errors, "a payload arrived corrupted");
                var received = new List<int>(pair.ClientListener.Sequences);
                received.Sort();
                for (int i = 0; i < count; i++)
                    Assert.AreEqual(i, received[i], "every message arrives exactly once");
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "the unordered channel is repaired too");
            }
        }

        // Packet properties as stock LiteNetLib numbers them (PacketProperty is internal).
        private const int ChanneledProperty = 1, ReliableMergedProperty = 2, MergedProperty = 13, RepairProperty = 19;

        // True when a datagram carries something a stock LiteNetLib peer would reject: a repair, or a
        // reliable packet whose sequence uses the resend marker bit.
        private static bool UsesRepairFormat(byte[] data, int length)
        {
            int property = data[0] & 0x1F;
            if (property != MergedProperty)
                return UsesRepairFormat(data, 0, length);
            for (int position = 1; position + 2 <= length;)
            {
                int size = BitConverter.ToUInt16(data, position);
                if (size == 0 || position + 2 + size > length)
                    break;
                if (UsesRepairFormat(data, position + 2, size))
                    return true;
                position += 2 + size;
            }
            return false;
        }

        private static bool UsesRepairFormat(byte[] data, int offset, int length)
        {
            int property = data[offset] & 0x1F;
            if (property == RepairProperty)
                return true;
            return (property == ChanneledProperty || property == ReliableMergedProperty) && length >= 3 &&
                   (BitConverter.ToUInt16(data, offset + 1) & 0x8000) != 0;
        }

        // The handshake settles repairs per connection: they flow both ways only when both ends enable
        // them. Otherwise the wire format stays stock LiteNetLib, which is what a peer running it (such
        // as an older relay or client) needs, and lost packets are still recovered by proven resends.
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Handshake_UsesRepairsOnlyWhenBothEndsEnableThem(bool serverRepairs, bool clientRepairs)
        {
            const int count = 150;
            bool agreed = serverRepairs && clientRepairs;
            int repairFormatToServer = 0, repairFormatToClient = 0;
            var random = new Random(5);
            using (var pair = new Pair(0, serverRepairs: serverRepairs, clientRepairs: clientRepairs,
                       serverInspects: (data, length) =>
                       {
                           if (UsesRepairFormat(data, length))
                               repairFormatToServer++;
                           return random.NextDouble() < 0.15;
                       },
                       clientInspects: (data, length) =>
                       {
                           if (UsesRepairFormat(data, length))
                               repairFormatToClient++;
                           return random.NextDouble() < 0.15;
                       }))
            {
                pair.Stream(count, MixedSize);
                pair.Stream(count, MixedSize, from: pair.ClientPeer);
                Assert.IsTrue(pair.Drain(count, 20000), $"only {pair.ClientListener.Sequences.Count}/{count} reached the client");
                Assert.IsTrue(pair.Drain(count, 20000, pair.ServerListener), $"only {pair.ServerListener.Sequences.Count}/{count} reached the server");
                AssertExactlyOnceInOrder(pair.ClientListener, count);
                AssertExactlyOnceInOrder(pair.ServerListener, count);
                Assert.AreEqual(agreed, repairFormatToClient > 0, "the server uses repairs exactly when both ends agreed");
                Assert.AreEqual(agreed, repairFormatToServer > 0, "the client uses repairs exactly when both ends agreed");
                Assert.AreEqual(agreed, pair.Server.Statistics.RepairsSent > 0);
                Assert.AreEqual(agreed, pair.Client.Statistics.RepairsSent > 0);
                Assert.Greater(pair.Server.Statistics.PacketLoss, 0, "losses are still proven and resent");
            }
        }

        // LiteNetLib's own receive and logic threads touch the channel concurrently.
        [Test]
        public void ReliableOrdered_DeliversUnderLossWithLibraryThreads()
        {
            const int count = 200;
            using (var pair = new Pair(15, threaded: true))
            {
                pair.Stream(count, MixedSize);
                Assert.IsTrue(pair.Drain(count, 20000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "repairs work off the main thread");
            }
        }

        // Resends carry a marker in the spare sequence bit and repairs rebuild packets out of order;
        // the receiver must still deliver every message once, in order.
        [TestCase(64)]
        [TestCase(3000)]
        public void ReliableOrdered_DeliversEveryMessageOnceInOrder_UnderHeavyLoss(int size)
        {
            const int count = 150;
            using (var pair = new Pair(25))
            {
                pair.Stream(count, size);
                Assert.IsTrue(pair.Drain(count, 15000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0,
                    "repairs must rebuild lost packets instead of waiting for every resend");
            }
        }

        // Unusually large packets are repaired on their own stream; ordinary ones must still be covered.
        [Test]
        public void ReliableOrdered_RepairsMixedSizes()
        {
            const int count = 200;
            using (var pair = new Pair(15))
            {
                pair.Stream(count, sequence => sequence % 10 == 9 ? 3000 : 64 + sequence * 37 % 300);
                Assert.IsTrue(pair.Drain(count, 15000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "mixed-size traffic must be repairable");
            }
        }

        // Path MTU discovery grows the packet size mid-stream; fragments, repairs and the receiver's
        // cached packets must all survive packets of the old and new sizes being in flight together.
        [Test]
        public void ReliableOrdered_SurvivesMtuGrowthMidStream()
        {
            const int count = 200;
            // 5% per side keeps four lost probes in a row (discovery then stops) out of the test.
            using (var pair = new Pair(5, mtuDiscovery: true))
            {
                int initialMtu = pair.ServerPeer.Mtu;
                pair.Stream(count, sequence => sequence % 10 == 9 ? 3000 : 64 + sequence * 37 % 300, 100);
                Assert.IsTrue(pair.Drain(count, 15000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.ServerPeer.Mtu, initialMtu, "discovery must raise the MTU during the stream");
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "repairs must keep working across the change");
            }
        }

        // Reliable packets stay a repair's overhead under the MTU, so even the largest one is covered.
        [Test]
        public void ReliableOrdered_RepairsFullSizePackets()
        {
            const int count = 150;
            using (var pair = new Pair(15))
            {
                int size = pair.ServerPeer.GetMaxSinglePacketSize(DeliveryMethod.ReliableOrdered);
                Assert.Less(size, pair.ServerPeer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable),
                    "reliable packets leave room for a repair header");
                pair.Stream(count, size);
                Assert.IsTrue(pair.Drain(count, 15000), $"only {pair.ClientListener.Sequences.Count}/{count} arrived");
                AssertExactlyOnceInOrder(pair, count);
                Assert.Greater(pair.Client.Statistics.PacketsRepaired, 0, "full-size packets must be repairable");
            }
        }

        // Loss protection must cost nothing while nothing is lost: one datagram per message.
        [Test]
        public void ReliableOrdered_SendsNoExtraPacketsOnACleanPath()
        {
            const int count = 300;
            using (var pair = new Pair(0))
            {
                long before = pair.Server.Statistics.PacketsSent;
                pair.Stream(count, 64);
                Assert.IsTrue(pair.Drain(count, 5000));
                for (int i = 0; i < 50; i++)
                {
                    pair.Pump();
                    Thread.Sleep(1);
                }

                AssertExactlyOnceInOrder(pair, count);
                long sent = pair.Server.Statistics.PacketsSent - before;
                Assert.LessOrEqual(sent, count, "a clean path must not send redundant packets");
                Assert.Zero(pair.Server.Statistics.RepairsSent, "a clean path must not send repairs");
            }
        }
    }
}

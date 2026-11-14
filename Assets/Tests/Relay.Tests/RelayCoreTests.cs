using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Essentials.Relay;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PurrNet.Relay.Tests
{
    public class RelayCoreTests
    {
        sealed class HostEnd
        {
            public RelayHost host;
            public readonly List<int> connected = new List<int>();
            public readonly List<int> disconnected = new List<int>();
            public readonly List<(int client, byte[] data)> received = new List<(int, byte[])>();
        }

        sealed class ClientEnd
        {
            public RelayClient client;
            public readonly List<byte[]> received = new List<byte[]>();
        }

        static readonly RelayDelivery[] Deliveries =
        {
            RelayDelivery.ReliableOrdered, RelayDelivery.ReliableUnordered, RelayDelivery.Unreliable,
            RelayDelivery.Sequenced, RelayDelivery.ReliableSequenced
        };

        readonly List<RelaySession> _sessions = new List<RelaySession>();
        readonly List<PurrTransport> _transports = new List<PurrTransport>();
        string _room;
#if UNITY_EDITOR
        UnityEditor.InsecureHttpOption _previousHttpOption;
#endif

        [OneTimeSetUp]
        public void StartRelay()
        {
            LocalRelay.EnsureRunning();
            typeof(NetworkManager).GetMethod("CalculateHashes", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
#if UNITY_EDITOR
            _previousHttpOption = UnityEditor.PlayerSettings.insecureHttpOption;
            UnityEditor.PlayerSettings.insecureHttpOption = UnityEditor.InsecureHttpOption.AlwaysAllowed;
#endif
        }

        [OneTimeTearDown]
        public void StopRelay()
        {
#if UNITY_EDITOR
            UnityEditor.PlayerSettings.insecureHttpOption = _previousHttpOption;
#endif
            LocalRelay.Stop();
        }

        [SetUp]
        public void SetUp()
        {
            _room = "relay-core-" + Guid.NewGuid().ToString("N");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _sessions.Count; i++)
                _sessions[i].Stop();
            _sessions.Clear();

            for (int i = 0; i < _transports.Count; i++)
            {
                if (_transports[i])
                    Object.Destroy(_transports[i].gameObject);
            }

            _transports.Clear();
            yield return null;
        }

        RelayOptions Options()
        {
            return new RelayOptions { masterServer = LocalRelay.balancerUrl, roomName = _room };
        }

        HostEnd CreateHost()
        {
            var end = new HostEnd { host = new RelayHost(Options()) };
            end.host.onClientConnected += client => end.connected.Add(client);
            end.host.onClientDisconnected += client => end.disconnected.Add(client);
            end.host.onData += (client, data) => end.received.Add((client, Copy(data)));
            _sessions.Add(end.host);
            return end;
        }

        ClientEnd CreateClient()
        {
            var end = new ClientEnd { client = new RelayClient(Options()) };
            end.client.onData += data => end.received.Add(Copy(data));
            _sessions.Add(end.client);
            return end;
        }

        PurrTransport CreateTransport()
        {
            var transport = new GameObject("RelayInteropTransport").AddComponent<PurrTransport>();
            transport.masterServer = LocalRelay.balancerUrl;
            transport.roomName = _room;
            _transports.Add(transport);
            return transport;
        }

        static byte[] Copy(ArraySegment<byte> data)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            return copy;
        }

        static byte[] Payload(int length, int seed)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(seed * 31 + i * 7);
            return data;
        }

        IEnumerator Pump(Func<bool> until, float timeout = 15f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!until() && Time.realtimeSinceStartup < deadline)
            {
                float delta = Time.unscaledDeltaTime;

                for (int i = 0; i < _sessions.Count; i++)
                {
                    _sessions[i].Poll();
                    _sessions[i].Update(delta);
                }

                for (int i = 0; i < _transports.Count; i++)
                {
                    if (!_transports[i])
                        continue;
                    _transports[i].ReceiveMessages(delta);
                    _transports[i].SendMessages(delta);
                }

                yield return null;
            }
        }

        IEnumerator PumpFor(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            yield return Pump(() => Time.realtimeSinceStartup >= end, seconds + 1f);
        }

        IEnumerator StartHost(HostEnd end)
        {
            end.host.Start();
            yield return Pump(() => end.host.state == RelayState.Connected);
            Assert.AreEqual(RelayState.Connected, end.host.state, $"host did not reach the relay: {end.host.lastError}");
        }

        IEnumerator StartClient(HostEnd host, ClientEnd end)
        {
            int before = host.connected.Count;
            end.client.Start();
            yield return Pump(() => end.client.state == RelayState.Connected && host.connected.Count > before);
            Assert.AreEqual(RelayState.Connected, end.client.state, $"client did not connect: {end.client.lastError}");
            Assert.AreEqual(before + 1, host.connected.Count, "host did not see the client join");
        }

        [UnityTest]
        public IEnumerator HostAndClientConnect()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            Assert.AreEqual(1, host.host.clients.Count);
            Assert.AreEqual(host.connected[0], host.host.clients[0]);
            Assert.IsTrue(host.host.isUdp);
            Assert.IsTrue(client.client.isUdp);
        }

        [UnityTest]
        public IEnumerator DataFlowsBothWaysOnEveryDelivery()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            int id = host.connected[0];

            for (int d = 0; d < Deliveries.Length; d++)
            {
                var delivery = Deliveries[d];
                int[] sizes = { 1, 200, host.host.GetMaxPacketSize(delivery) - 16 };

                for (int s = 0; s < sizes.Length; s++)
                {
                    var toClient = Payload(sizes[s], d * 10 + s);
                    var toHost = Payload(sizes[s], d * 10 + s + 100);
                    int clientBefore = client.received.Count;
                    int hostBefore = host.received.Count;

                    host.host.Send(id, new ArraySegment<byte>(toClient), delivery);
                    client.client.Send(new ArraySegment<byte>(toHost), delivery);

                    yield return Pump(() => client.received.Count > clientBefore && host.received.Count > hostBefore);

                    Assert.AreEqual(clientBefore + 1, client.received.Count, $"client got nothing on {delivery} ({sizes[s]} bytes)");
                    Assert.AreEqual(hostBefore + 1, host.received.Count, $"host got nothing on {delivery} ({sizes[s]} bytes)");
                    CollectionAssert.AreEqual(toClient, client.received[clientBefore], $"{delivery} to client ({sizes[s]} bytes)");
                    CollectionAssert.AreEqual(toHost, host.received[hostBefore].data, $"{delivery} to host ({sizes[s]} bytes)");
                    Assert.AreEqual(id, host.received[hostBefore].client);
                }
            }
        }

        [UnityTest]
        public IEnumerator ReliableOrderedKeepsOrder()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            const int count = 200;
            int id = host.connected[0];

            for (int i = 0; i < count; i++)
            {
                var data = new ArraySegment<byte>(BitConverter.GetBytes(i));
                host.host.Send(id, data);
                client.client.Send(data);
            }

            yield return Pump(() => client.received.Count >= count && host.received.Count >= count);

            Assert.AreEqual(count, client.received.Count);
            Assert.AreEqual(count, host.received.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, BitConverter.ToInt32(client.received[i], 0), "order to client");
                Assert.AreEqual(i, BitConverter.ToInt32(host.received[i].data, 0), "order to host");
            }
        }

        [UnityTest]
        public IEnumerator TwoClientsAreRoutedSeparately()
        {
            var host = CreateHost();
            var first = CreateClient();
            var second = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, first);
            yield return StartClient(host, second);

            int firstId = host.connected[0];
            int secondId = host.connected[1];
            Assert.AreNotEqual(firstId, secondId);

            var toFirst = Payload(64, 1);
            var toSecond = Payload(64, 2);
            var fromFirst = Payload(32, 3);
            var fromSecond = Payload(32, 4);

            host.host.Send(firstId, new ArraySegment<byte>(toFirst));
            host.host.Send(secondId, new ArraySegment<byte>(toSecond));
            first.client.Send(new ArraySegment<byte>(fromFirst));
            second.client.Send(new ArraySegment<byte>(fromSecond));

            yield return Pump(() => first.received.Count >= 1 && second.received.Count >= 1 && host.received.Count >= 2);
            yield return PumpFor(0.5f);

            Assert.AreEqual(1, first.received.Count);
            Assert.AreEqual(1, second.received.Count);
            CollectionAssert.AreEqual(toFirst, first.received[0]);
            CollectionAssert.AreEqual(toSecond, second.received[0]);

            Assert.AreEqual(2, host.received.Count);
            foreach (var (client, data) in host.received)
                CollectionAssert.AreEqual(client == firstId ? fromFirst : fromSecond, data);
        }

        [UnityTest]
        public IEnumerator ClientLeavingIsReportedToHost()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            int id = host.connected[0];
            client.client.Stop();

            yield return Pump(() => host.disconnected.Count > 0);

            Assert.AreEqual(1, host.disconnected.Count, "host was not told the client left");
            Assert.AreEqual(id, host.disconnected[0]);
            Assert.AreEqual(0, host.host.clients.Count);
            Assert.AreEqual(RelayState.Connected, host.host.state);
            Assert.AreEqual(RelayCloseReason.Stopped, client.client.closeReason);
        }

        [UnityTest]
        public IEnumerator KickedClientIsDisconnected()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            host.host.Kick(host.connected[0]);

            yield return Pump(() => client.client.state == RelayState.Disconnected && host.disconnected.Count > 0);

            Assert.AreEqual(RelayState.Disconnected, client.client.state, "kicked client stayed connected");
            Assert.AreEqual(RelayCloseReason.ConnectionLost, client.client.closeReason);
            Assert.AreEqual(1, host.disconnected.Count, "host was not told the kicked client left");
        }

        [UnityTest]
        public IEnumerator HostStoppingDisconnectsClients()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            host.host.Stop();

            yield return Pump(() => client.client.state == RelayState.Disconnected);

            Assert.AreEqual(RelayState.Disconnected, host.host.state);
            Assert.AreEqual(RelayState.Disconnected, client.client.state, "client stayed connected after the host stopped");
        }

        [UnityTest]
        public IEnumerator ClientCanRejoinAfterLeaving()
        {
            var host = CreateHost();
            var client = CreateClient();

            yield return StartHost(host);
            yield return StartClient(host, client);

            client.client.Stop();
            yield return Pump(() => host.disconnected.Count > 0);
            yield return StartClient(host, client);

            var data = Payload(48, 9);
            client.client.Send(new ArraySegment<byte>(data));
            yield return Pump(() => host.received.Count > 0);

            Assert.AreEqual(1, host.received.Count);
            CollectionAssert.AreEqual(data, host.received[0].data);
            Assert.AreEqual(host.connected[1], host.received[0].client);
        }

        [UnityTest]
        public IEnumerator JoiningMissingRoomFails()
        {
            var client = CreateClient();
            client.client.Start();

            yield return Pump(() => client.client.state == RelayState.Disconnected, 30f);

            Assert.AreEqual(RelayState.Disconnected, client.client.state);
            Assert.AreEqual(RelayCloseReason.Error, client.client.closeReason);
            Assert.IsFalse(string.IsNullOrEmpty(client.client.lastError));
        }

        [UnityTest]
        public IEnumerator CoreClientTalksToPurrTransportHost()
        {
            var transport = CreateTransport();
            var received = new List<(Connection conn, byte[] data)>();
            var joined = new List<Connection>();
            transport.onConnected += (conn, asServer) => { if (asServer) joined.Add(conn); };
            transport.onDataReceived += (conn, data, asServer) => { if (asServer) received.Add((conn, data.span.ToArray())); };

            transport.Listen(0);
            yield return Pump(() => transport.listenerState == ConnectionState.Connected);
            Assert.AreEqual(ConnectionState.Connected, transport.listenerState);

            var client = CreateClient();
            client.client.Start();
            yield return Pump(() => client.client.state == RelayState.Connected && joined.Count > 0);
            Assert.AreEqual(RelayState.Connected, client.client.state, client.client.lastError);
            Assert.AreEqual(1, joined.Count);

            foreach (var channel in new[] { Channel.ReliableOrdered, Channel.Unreliable })
            {
                var toClient = Payload(300, (int)channel + 1);
                var toHost = Payload(300, (int)channel + 50);
                int clientBefore = client.received.Count;
                int hostBefore = received.Count;

                transport.SendToClient(joined[0], new ByteData(toClient, 0, toClient.Length), channel);
                client.client.Send(new ArraySegment<byte>(toHost), (RelayDelivery)UDPTransport.ToDeliveryMethod(channel));

                yield return Pump(() => client.received.Count > clientBefore && received.Count > hostBefore);

                Assert.AreEqual(clientBefore + 1, client.received.Count, $"core client got nothing on {channel}");
                Assert.AreEqual(hostBefore + 1, received.Count, $"PurrTransport host got nothing on {channel}");
                CollectionAssert.AreEqual(toClient, client.received[clientBefore]);
                CollectionAssert.AreEqual(toHost, received[hostBefore].data);
            }
        }

        [UnityTest]
        public IEnumerator PurrTransportClientTalksToCoreHost()
        {
            var host = CreateHost();
            yield return StartHost(host);

            var transport = CreateTransport();
            var received = new List<byte[]>();
            transport.onDataReceived += (_, data, asServer) => { if (!asServer) received.Add(data.span.ToArray()); };

            transport.Connect(null, 0);
            yield return Pump(() => transport.clientState == ConnectionState.Connected && host.connected.Count > 0);
            Assert.AreEqual(ConnectionState.Connected, transport.clientState);
            Assert.AreEqual(1, host.connected.Count);

            foreach (var channel in new[] { Channel.ReliableOrdered, Channel.Unreliable })
            {
                var toClient = Payload(300, (int)channel + 1);
                var toHost = Payload(300, (int)channel + 50);
                int clientBefore = received.Count;
                int hostBefore = host.received.Count;

                host.host.Send(host.connected[0], new ArraySegment<byte>(toClient),
                    (RelayDelivery)UDPTransport.ToDeliveryMethod(channel));
                transport.SendToServer(new ByteData(toHost, 0, toHost.Length), channel);

                yield return Pump(() => received.Count > clientBefore && host.received.Count > hostBefore);

                Assert.AreEqual(clientBefore + 1, received.Count, $"PurrTransport client got nothing on {channel}");
                Assert.AreEqual(hostBefore + 1, host.received.Count, $"core host got nothing on {channel}");
                CollectionAssert.AreEqual(toClient, received[clientBefore]);
                CollectionAssert.AreEqual(toHost, host.received[hostBefore].data);
            }
        }
    }
}

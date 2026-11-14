using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PurrNet.Relay.Tests
{
    public class PurrTransportRelayTests
    {
        sealed class Endpoint
        {
            public GameObject gameObject;
            public PurrTransport transport;
            public readonly List<Connection> connected = new List<Connection>();
            public readonly List<Connection> disconnected = new List<Connection>();
            public readonly List<(Connection conn, byte[] data)> received = new List<(Connection, byte[])>();
            public bool clientConnected;
            public bool clientDisconnected;
        }

        static readonly Channel[] Channels =
        {
            Channel.ReliableOrdered, Channel.ReliableUnordered, Channel.Unreliable, Channel.UnreliableSequenced
        };

        readonly List<Endpoint> _endpoints = new List<Endpoint>();
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
            _room = "relay-test-" + Guid.NewGuid().ToString("N");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _endpoints.Count - 1; i >= 0; i--)
            {
                if (_endpoints[i].gameObject)
                    Object.Destroy(_endpoints[i].gameObject);
            }

            _endpoints.Clear();
            yield return null;
        }

        Endpoint Create(bool asServer)
        {
            var endpoint = new Endpoint { gameObject = new GameObject(asServer ? "RelayTestHost" : "RelayTestClient") };
            endpoint.transport = endpoint.gameObject.AddComponent<PurrTransport>();
            endpoint.transport.masterServer = LocalRelay.balancerUrl;
            endpoint.transport.roomName = _room;

            endpoint.transport.onConnected += (conn, server) =>
            {
                if (server)
                    endpoint.connected.Add(conn);
                else
                    endpoint.clientConnected = true;
            };
            endpoint.transport.onDisconnected += (conn, _, server) =>
            {
                if (server)
                    endpoint.disconnected.Add(conn);
                else
                    endpoint.clientDisconnected = true;
            };
            endpoint.transport.onDataReceived += (conn, data, server) =>
            {
                if (server == asServer)
                    endpoint.received.Add((conn, data.span.ToArray()));
            };

            _endpoints.Add(endpoint);
            return endpoint;
        }

        IEnumerator Pump(Func<bool> until, float timeout = 15f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!until() && Time.realtimeSinceStartup < deadline)
            {
                float delta = Time.unscaledDeltaTime;
                for (int i = 0; i < _endpoints.Count; i++)
                {
                    var transport = _endpoints[i].transport;
                    if (!transport)
                        continue;
                    transport.ReceiveMessages(delta);
                    transport.SendMessages(delta);
                }

                yield return null;
            }
        }

        IEnumerator PumpFor(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            yield return Pump(() => Time.realtimeSinceStartup >= end, seconds + 1f);
        }

        IEnumerator StartHost(Endpoint host)
        {
            host.transport.Listen(0);
            yield return Pump(() => host.transport.listenerState == ConnectionState.Connected);
            Assert.AreEqual(ConnectionState.Connected, host.transport.listenerState, "host did not reach the relay");
        }

        IEnumerator StartClient(Endpoint host, Endpoint client)
        {
            int before = host.connected.Count;
            client.transport.Connect(null, 0);
            yield return Pump(() => client.transport.clientState == ConnectionState.Connected &&
                                    host.connected.Count > before);
            Assert.AreEqual(ConnectionState.Connected, client.transport.clientState, "client did not connect");
            Assert.IsTrue(client.clientConnected, "client did not report onConnected");
            Assert.AreEqual(before + 1, host.connected.Count, "host did not see the client join");
        }

        static byte[] Payload(int length, int seed)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(seed * 31 + i * 7);
            return data;
        }

        [UnityTest]
        public IEnumerator HostAndClientConnect()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            Assert.AreEqual(1, host.transport.connections.Count);
            Assert.AreEqual(host.connected[0], host.transport.connections[0]);
            Assert.IsTrue(host.connected[0].isValid);
        }

        [UnityTest]
        public IEnumerator DataFlowsBothWaysOnEveryChannel()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            var conn = host.connected[0];

            for (int c = 0; c < Channels.Length; c++)
            {
                var channel = Channels[c];
                int[] sizes = { 1, 200, host.transport.GetMTU(conn, channel, true) - 16 };

                for (int s = 0; s < sizes.Length; s++)
                {
                    var toClient = Payload(sizes[s], c * 10 + s);
                    var toHost = Payload(sizes[s], c * 10 + s + 100);
                    int clientBefore = client.received.Count;
                    int hostBefore = host.received.Count;

                    host.transport.SendToClient(conn, new ByteData(toClient, 0, toClient.Length), channel);
                    client.transport.SendToServer(new ByteData(toHost, 0, toHost.Length), channel);

                    yield return Pump(() => client.received.Count > clientBefore && host.received.Count > hostBefore);

                    Assert.AreEqual(clientBefore + 1, client.received.Count, $"client got nothing on {channel} ({sizes[s]} bytes)");
                    Assert.AreEqual(hostBefore + 1, host.received.Count, $"host got nothing on {channel} ({sizes[s]} bytes)");
                    CollectionAssert.AreEqual(toClient, client.received[clientBefore].data, $"{channel} to client ({sizes[s]} bytes)");
                    CollectionAssert.AreEqual(toHost, host.received[hostBefore].data, $"{channel} to host ({sizes[s]} bytes)");
                    Assert.AreEqual(conn, host.received[hostBefore].conn);
                }
            }
        }

        [UnityTest]
        public IEnumerator ReliableOrderedKeepsOrder()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            const int count = 200;
            var conn = host.connected[0];

            for (int i = 0; i < count; i++)
            {
                var data = BitConverter.GetBytes(i);
                host.transport.SendToClient(conn, new ByteData(data, 0, data.Length));
                client.transport.SendToServer(new ByteData(data, 0, data.Length));
            }

            yield return Pump(() => client.received.Count >= count && host.received.Count >= count);

            Assert.AreEqual(count, client.received.Count);
            Assert.AreEqual(count, host.received.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, BitConverter.ToInt32(client.received[i].data, 0), "order to client");
                Assert.AreEqual(i, BitConverter.ToInt32(host.received[i].data, 0), "order to host");
            }
        }

        [UnityTest]
        public IEnumerator TwoClientsAreRoutedSeparately()
        {
            var host = Create(true);
            var first = Create(false);
            var second = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, first);
            yield return StartClient(host, second);

            var firstConn = host.connected[0];
            var secondConn = host.connected[1];
            Assert.AreNotEqual(firstConn, secondConn);

            var toFirst = Payload(64, 1);
            var toSecond = Payload(64, 2);
            host.transport.SendToClient(firstConn, new ByteData(toFirst, 0, toFirst.Length));
            host.transport.SendToClient(secondConn, new ByteData(toSecond, 0, toSecond.Length));

            var fromFirst = Payload(32, 3);
            var fromSecond = Payload(32, 4);
            first.transport.SendToServer(new ByteData(fromFirst, 0, fromFirst.Length));
            second.transport.SendToServer(new ByteData(fromSecond, 0, fromSecond.Length));

            yield return Pump(() => first.received.Count >= 1 && second.received.Count >= 1 && host.received.Count >= 2);
            yield return PumpFor(0.5f);

            Assert.AreEqual(1, first.received.Count);
            Assert.AreEqual(1, second.received.Count);
            CollectionAssert.AreEqual(toFirst, first.received[0].data);
            CollectionAssert.AreEqual(toSecond, second.received[0].data);

            Assert.AreEqual(2, host.received.Count);
            foreach (var (conn, data) in host.received)
                CollectionAssert.AreEqual(conn == firstConn ? fromFirst : fromSecond, data);
        }

        [UnityTest]
        public IEnumerator ClientLeavingIsReportedToHost()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            var conn = host.connected[0];
            client.transport.Disconnect();

            yield return Pump(() => host.disconnected.Count > 0);

            Assert.AreEqual(1, host.disconnected.Count, "host was not told the client left");
            Assert.AreEqual(conn, host.disconnected[0]);
            Assert.AreEqual(0, host.transport.connections.Count);
            Assert.AreEqual(ConnectionState.Connected, host.transport.listenerState);
        }

        [UnityTest]
        public IEnumerator KickedClientIsDisconnected()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            host.transport.CloseConnection(host.connected[0]);

            yield return Pump(() => client.transport.clientState == ConnectionState.Disconnected &&
                                    host.disconnected.Count > 0);

            Assert.AreEqual(ConnectionState.Disconnected, client.transport.clientState, "kicked client stayed connected");
            Assert.IsTrue(client.clientDisconnected);
            Assert.AreEqual(1, host.disconnected.Count, "host was not told the kicked client left");
        }

        [UnityTest]
        public IEnumerator HostStoppingDisconnectsClients()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            host.transport.StopListening();

            yield return Pump(() => client.transport.clientState == ConnectionState.Disconnected);

            Assert.AreEqual(ConnectionState.Disconnected, host.transport.listenerState);
            Assert.AreEqual(ConnectionState.Disconnected, client.transport.clientState, "client stayed connected after the host stopped");
            Assert.IsTrue(client.clientDisconnected);
        }

        [UnityTest]
        public IEnumerator ClientCanRejoinAfterLeaving()
        {
            var host = Create(true);
            var client = Create(false);

            yield return StartHost(host);
            yield return StartClient(host, client);

            client.transport.Disconnect();
            yield return Pump(() => host.disconnected.Count > 0);

            client.clientConnected = false;
            yield return StartClient(host, client);

            var data = Payload(48, 9);
            client.transport.SendToServer(new ByteData(data, 0, data.Length));
            yield return Pump(() => host.received.Count > 0);

            Assert.AreEqual(1, host.received.Count);
            CollectionAssert.AreEqual(data, host.received[0].data);
            Assert.AreEqual(host.connected[1], host.received[0].conn);
        }
    }
}

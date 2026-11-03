#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet.Transports;
using Steamworks;

namespace PurrNet.Steam.Tests
{
    // SteamClient and SteamServer run against a stand-in for Steam's sockets, so a test chooses what a failed send sees
    // and when the status callback arrives. Steam does not have to be running.
    public sealed class SteamDisconnectReasonTests
    {
        const ESteamNetworkingConnectionState Connected =
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
        const ESteamNetworkingConnectionState ClosedByPeer =
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer;
        const ESteamNetworkingConnectionState ProblemDetectedLocally =
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;

        static readonly HSteamNetConnection Handle = new HSteamNetConnection(42);
        static readonly ByteData Payload = new ByteData(new byte[] { 1, 2, 3 }, 0, 3);

        sealed class FakeSockets : ISteamSockets
        {
            public EResult sendResult = EResult.k_EResultOK;
            public EResult statusResult = EResult.k_EResultOK;
            public ESteamNetworkingConnectionState state = Connected;
            public readonly List<HSteamNetConnection> closed = new List<HSteamNetConnection>();

            public EResult SendMessageToConnection(HSteamNetConnection conn, IntPtr data, uint length, int flags) =>
                sendResult;

            public EResult GetConnectionRealTimeStatus(HSteamNetConnection conn,
                ref SteamNetConnectionRealTimeStatus_t status)
            {
                status.m_eState = state;
                return statusResult;
            }

            public void CloseConnection(HSteamNetConnection conn, string reason) => closed.Add(conn);
        }

        static SteamNetConnectionStatusChangedCallback_t StatusChanged(ESteamNetworkingConnectionState state)
        {
            var args = new SteamNetConnectionStatusChangedCallback_t();
            args.m_hConn = Handle;
            args.m_info.m_eState = state;
            return args;
        }

        static SteamClient ConnectedClient(FakeSockets sockets)
        {
            var client = new SteamClient(sockets);
            client.Open(Handle);
            client.OnLocalConnectionState(StatusChanged(Connected));
            Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Connected));
            return client;
        }

        static SteamServer ServerWithClient(FakeSockets sockets, List<(int id, DisconnectReason why)> disconnects,
            out int id)
        {
            var server = new SteamServer(sockets);
            int connected = -1;
            server.onRemoteConnected += conn => connected = conn;
            server.onRemoteDisconnectedWithReason += (conn, why) => disconnects.Add((conn, why));
            server.AddConnection(Handle, 76561197960287930UL);
            id = connected;
            Assert.That(id, Is.Not.EqualTo(-1));
            return server;
        }

        // What a failed send can see before Steam posts the status callback: a local problem already in the status, a
        // status that still reads Connected, or no status at all. None of them says the other side chose to leave.
        static readonly object[] UnconfirmedSendFailures =
        {
            new object[] { ProblemDetectedLocally, EResult.k_EResultOK },
            new object[] { Connected, EResult.k_EResultOK },
            new object[] { Connected, EResult.k_EResultNoConnection },
        };

        [TestCaseSource(nameof(UnconfirmedSendFailures))]
        public void Client_SendFailsBeforeStatusCallback_IsTimeout(ESteamNetworkingConnectionState stateAtSend,
            EResult statusResult)
        {
            var sockets = new FakeSockets
            {
                sendResult = EResult.k_EResultNoConnection, state = stateAtSend, statusResult = statusResult
            };
            var client = ConnectedClient(sockets);
            try
            {
                client.Send(Payload, Channel.ReliableOrdered);
                client.OnLocalConnectionState(StatusChanged(ProblemDetectedLocally));
                client.ReceiveMessages();

                Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That(client.disconnectReason, Is.EqualTo(DisconnectReason.Timeout));
                Assert.That(sockets.closed, Is.EqualTo(new[] { Handle }));
            }
            finally
            {
                client.Stop();
            }
        }

        [Test]
        public void Client_SendFailsAfterHostClosed_IsServerRequest()
        {
            var sockets = new FakeSockets { sendResult = EResult.k_EResultNoConnection, state = ClosedByPeer };
            var client = ConnectedClient(sockets);
            try
            {
                client.Send(Payload, Channel.ReliableOrdered);
                client.OnLocalConnectionState(StatusChanged(ClosedByPeer));
                client.ReceiveMessages();

                Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That(client.disconnectReason, Is.EqualTo(DisconnectReason.ServerRequest));
            }
            finally
            {
                client.Stop();
            }
        }

        [Test]
        public void Client_HostClosesConnection_IsServerRequest()
        {
            var sockets = new FakeSockets();
            var client = ConnectedClient(sockets);
            try
            {
                client.OnLocalConnectionState(StatusChanged(ClosedByPeer));

                Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That(client.disconnectReason, Is.EqualTo(DisconnectReason.ServerRequest));
                Assert.That(sockets.closed, Is.EqualTo(new[] { Handle }));
            }
            finally
            {
                client.Stop();
            }
        }

        [Test]
        public void Client_ProblemDetectedLocally_IsTimeout()
        {
            var sockets = new FakeSockets();
            var client = ConnectedClient(sockets);
            try
            {
                client.OnLocalConnectionState(StatusChanged(ProblemDetectedLocally));

                Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Disconnected));
                Assert.That(client.disconnectReason, Is.EqualTo(DisconnectReason.Timeout));
            }
            finally
            {
                client.Stop();
            }
        }

        [Test]
        public void Client_Stop_IsClientRequest()
        {
            var client = ConnectedClient(new FakeSockets());
            client.Stop();

            Assert.That(client.connectionState, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(client.disconnectReason, Is.EqualTo(DisconnectReason.ClientRequest));
        }

        [TestCaseSource(nameof(UnconfirmedSendFailures))]
        public void Server_SendFailsBeforeStatusCallback_IsTimeout(ESteamNetworkingConnectionState stateAtSend,
            EResult statusResult)
        {
            var sockets = new FakeSockets
            {
                sendResult = EResult.k_EResultNoConnection, state = stateAtSend, statusResult = statusResult
            };
            var disconnects = new List<(int id, DisconnectReason why)>();
            var server = ServerWithClient(sockets, disconnects, out var id);
            try
            {
                server.SendToConnection(id, Payload, Channel.ReliableOrdered);
                server.OnRemoteConnectionState(StatusChanged(ProblemDetectedLocally));
                server.ReceiveMessages();

                Assert.That(disconnects, Is.EqualTo(new[] { (id, DisconnectReason.Timeout) }));
                Assert.That(sockets.closed, Does.Contain(Handle));
            }
            finally
            {
                server.Stop();
            }
        }

        [Test]
        public void Server_SendFailsAfterClientClosed_IsClientRequest()
        {
            var sockets = new FakeSockets { sendResult = EResult.k_EResultNoConnection, state = ClosedByPeer };
            var disconnects = new List<(int id, DisconnectReason why)>();
            var server = ServerWithClient(sockets, disconnects, out var id);
            try
            {
                server.SendToConnection(id, Payload, Channel.ReliableOrdered);
                server.OnRemoteConnectionState(StatusChanged(ClosedByPeer));
                server.ReceiveMessages();

                Assert.That(disconnects, Is.EqualTo(new[] { (id, DisconnectReason.ClientRequest) }));
            }
            finally
            {
                server.Stop();
            }
        }

        [Test]
        public void Server_ClientClosesConnection_IsClientRequest()
        {
            var sockets = new FakeSockets();
            var disconnects = new List<(int id, DisconnectReason why)>();
            var server = ServerWithClient(sockets, disconnects, out var id);
            try
            {
                server.OnRemoteConnectionState(StatusChanged(ClosedByPeer));

                Assert.That(disconnects, Is.EqualTo(new[] { (id, DisconnectReason.ClientRequest) }));
                Assert.That(sockets.closed, Is.EqualTo(new[] { Handle }));
            }
            finally
            {
                server.Stop();
            }
        }

        [Test]
        public void Server_ProblemDetectedLocally_IsTimeout()
        {
            var disconnects = new List<(int id, DisconnectReason why)>();
            var server = ServerWithClient(new FakeSockets(), disconnects, out var id);
            try
            {
                server.OnRemoteConnectionState(StatusChanged(ProblemDetectedLocally));

                Assert.That(disconnects, Is.EqualTo(new[] { (id, DisconnectReason.Timeout) }));
            }
            finally
            {
                server.Stop();
            }
        }

        [Test]
        public void Server_Kick_IsServerRequest()
        {
            var disconnects = new List<(int id, DisconnectReason why)>();
            var server = ServerWithClient(new FakeSockets(), disconnects, out var id);
            try
            {
                server.Kick(id);
                server.ReceiveMessages();

                Assert.That(disconnects, Is.EqualTo(new[] { (id, DisconnectReason.ServerRequest) }));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
#endif

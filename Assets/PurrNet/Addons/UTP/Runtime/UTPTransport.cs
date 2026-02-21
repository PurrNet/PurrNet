#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID)
#define DISABLEUTPWORKS
#endif

#if UTP_LOBBYRELAY
#define UTP_NET_PACKAGE
#if UTP_SERVICES
using Unity.Services.Relay.Models;
#endif
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
#endif

using System;
using System.Collections.Generic;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.UTP
{
    /// <summary>
    /// Unity Transport Package (UTP) implementation for PurrNet networking.
    /// Provides cross-platform networking with support for both direct connection and Unity Relay-based peer-to-peer connectivity.
    /// Supports multiple network channels (reliable, unreliable) and can function as both client and server.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("PurrNet/Transport/UTP Transport")]
    public partial class UTPTransport : GenericTransport, ITransport
    {
        [Header("Server Settings")] [SerializeField]
        private ushort _serverPort = 5004;

        [SerializeField] private bool _dedicatedServer;
        [SerializeField] private bool _peerToPeer = true;

        [Header("Client Settings")] [SerializeField]
        private string _address = "127.0.0.1";

        /// <summary>
        /// Gets or sets the port number for the server to listen on.
        /// </summary>
        public ushort serverPort
        {
            get => _serverPort;
            set => _serverPort = value;
        }

        /// <summary>
        /// Gets or sets whether this instance is configured as a dedicated server.
        /// </summary>
        public bool dedicatedServer
        {
            get => _dedicatedServer;
            set => _dedicatedServer = value;
        }

        /// <summary>
        /// Gets or sets whether peer-to-peer mode is enabled using Unity Relay.
        /// </summary>
        public bool peerToPeer
        {
            get => _peerToPeer;
            set => _peerToPeer = value;
        }

        /// <summary>
        /// Gets or sets the IP address or hostname for the client to connect to.
        /// </summary>
        public string address
        {
            get => _address;
            set => _address = value;
        }

        /// <summary>
        /// Determines if the specified network channel is supported by this transport.
        /// </summary>
        /// <param name="channel">The channel type to check.</param>
        /// <returns>True if the channel is supported; otherwise, false.</returns>
        public bool SupportsChannel(Channel channel)
        {
            return channel switch
            {
                Channel.Unreliable => true,
                Channel.UnreliableSequenced => true, // Mapped to unreliable pipeline
                Channel.ReliableOrdered => true,
                Channel.ReliableUnordered => true, // Mapped to reliable pipeline

                _ => false
            };
        }

        /// <summary>
        /// Gets the Maximum Transmission Unit (MTU) size for the specified channel and connection.
        /// </summary>
        /// <param name="target">The target connection.</param>
        /// <param name="channel">The network channel.</param>
        /// <param name="asServer">Whether this is from the server perspective.</param>
        /// <returns>The MTU size in bytes.</returns>
        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return channel switch
            {
                Channel.Unreliable => 1024,
                Channel.UnreliableSequenced or Channel.ReliableUnordered or Channel.ReliableOrdered => 8192 * 2,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        public override bool isSupported => true;
#else
        public override bool isSupported => false;
#endif

        public override ITransport transport => this;

        private readonly List<Connection> _connections = new List<Connection>();

        /// <summary>
        /// Gets the read-only list of all active connections.
        /// </summary>
        public IReadOnlyList<Connection> connections => _connections;

        private ConnectionState _listenerState = ConnectionState.Disconnected;

        /// <summary>
        /// Gets the current state of the server listener.
        /// </summary>
        public ConnectionState listenerState
        {
            get => _listenerState;
            private set
            {
                if (_listenerState == value)
                    return;

                _listenerState = value;
                onConnectionState?.Invoke(_listenerState, true);
            }
        }

        private ConnectionState _clientState = ConnectionState.Disconnected;

        /// <summary>
        /// Gets the current connection state of the client.
        /// </summary>
        public ConnectionState clientState
        {
            get => _clientState;
            private set
            {
                if (_clientState == value)
                    return;

                _clientState = value;
                onConnectionState?.Invoke(_clientState, false);
            }
        }

        /// <summary>
        /// Event raised when a connection is established (either as client or when a client connects to server).
        /// </summary>
        public event OnConnected onConnected;

        /// <summary>
        /// Event raised when a connection is terminated.
        /// </summary>
        public event OnDisconnected onDisconnected;

        /// <summary>
        /// Event raised when data is received from a connection.
        /// </summary>
        public event OnDataReceived onDataReceived;

        /// <summary>
        /// Event raised when data is sent to a connection.
        /// </summary>
        public event OnDataSent onDataSent;

        /// <summary>
        /// Event raised when the connection state changes.
        /// </summary>
        public event OnConnectionState onConnectionState;

        private UTPServer _server;

        private UTPClient _client;

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private RelayServerData? _relayServerData;
        private RelayServerData? _relayClientData;
#endif

        protected override void StartClientInternal()
        {
            Connect(_address, _serverPort);
        }

        protected override void StartServerInternal()
        {
            Listen(_serverPort);
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        /// <summary>
        /// Initializes the Unity Relay server data using a relay allocation.
        /// This must be called before starting the server in peer-to-peer mode.
        /// </summary>
        /// <param name="allocation">The Unity Relay allocation containing server endpoint information.</param>
        /// <returns>True if initialization was successful; otherwise, false.</returns>
        public bool InitializeRelayServer(Allocation allocation)
        {
            // Store relay server data for use when starting server
            // Find the dtls endpoint
            var serverEndpoint = allocation.ServerEndpoints.Find(e => e.ConnectionType == "dtls");
            if (serverEndpoint == null)
            {
                Debug.LogError("No DTLS endpoint found in allocation");
                return false;
            }

            _relayServerData = new RelayServerData(
                serverEndpoint.Host,
                (ushort)serverEndpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData,
                allocation.Key,
                serverEndpoint.Secure,
                false // isWebSocket
            );
            return true;
        }

        /// <summary>
        /// Initializes the Unity Relay client data using a join code.
        /// This must be called before connecting to a server in peer-to-peer mode.
        /// </summary>
        /// <param name="joinCode">The join code provided by the relay server host.</param>
        /// <returns>A task that returns true if initialization was successful; otherwise, false.</returns>
        public async System.Threading.Tasks.Task<bool> InitializeRelayClient(string joinCode)
        {
            // Convert join code to relay client data
            try
            {
                var joinAllocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode);

                // Find the dtls endpoint
                var serverEndpoint = joinAllocation.ServerEndpoints.Find(e => e.ConnectionType == "dtls");
                if (serverEndpoint == null)
                {
                    Debug.LogError("No DTLS endpoint found in join allocation");
                    return false;
                }

                _relayClientData = new RelayServerData(
                    serverEndpoint.Host,
                    (ushort)serverEndpoint.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData,
                    joinAllocation.Key,
                    serverEndpoint.Secure,
                    false // isWebSocket
                );
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize relay client: {e.Message}");
                return false;
            }
        }
#endif

        /// <summary>
        /// Starts listening for incoming client connections on the specified port.
        /// Can operate in direct connection mode or peer-to-peer mode using Unity Relay.
        /// </summary>
        /// <param name="port">The port number to listen on.</param>
        public void Listen(ushort port)
        {
            if (_server != null)
                StopListening();

            listenerState = ConnectionState.Connecting;

            _server = new UTPServer();
            _connections.Clear();

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (_peerToPeer)
                _server.ListenP2P(_dedicatedServer, _relayServerData);
            else _server.Listen(port, _dedicatedServer, _relayServerData);
#elif UTP_LOBBYRELAY && UTP_SERVICES
            if (_peerToPeer)
                _server.ListenP2P(_dedicatedServer);
            else _server.Listen(port, _dedicatedServer);
#endif

            if (_server.listening)
            {
                listenerState = ConnectionState.Connected;

                _server.onDataReceived += OnServerData;
                _server.onRemoteConnected += OnRemoteConnected;
                _server.onRemoteDisconnected += OnRemoteDisconnected;
            }
            else
            {
                listenerState = ConnectionState.Disconnecting;
                listenerState = ConnectionState.Disconnected;
                _server = null;
                return;
            }
        }

        private void OnRemoteConnected(int obj)
        {
            _connections.Add(new Connection(obj));
            onConnected?.Invoke(new Connection(obj), true);
        }

        private void OnRemoteDisconnected(int obj)
        {
            _connections.Remove(new Connection(obj));
            onDisconnected?.Invoke(new Connection(obj), DisconnectReason.ClientRequest, true);
        }

        private void OnServerData(int conn, ByteData data)
        {
            onDataReceived?.Invoke(new Connection(conn), data, true);
        }

        /// <summary>
        /// Stops the server from listening for new connections and disconnects all clients.
        /// </summary>
        public void StopListening()
        {
            if (listenerState != ConnectionState.Disconnected)
                listenerState = ConnectionState.Disconnecting;

            if (_server != null)
            {
                _server.onDataReceived -= OnServerData;
                _server.onRemoteConnected -= OnRemoteConnected;
                _server.onRemoteDisconnected -= OnRemoteDisconnected;
            }

            _server?.Stop();
            listenerState = ConnectionState.Disconnected;
            _server = null;
        }

        private Coroutine _connectClientCoroutine;

        /// <summary>
        /// Connects to a server at the specified IP address and port.
        /// Can operate in direct connection mode or peer-to-peer mode using Unity Relay.
        /// </summary>
        /// <param name="ip">The IP address or hostname of the server.</param>
        /// <param name="port">The port number to connect to.</param>
        public void Connect(string ip, ushort port)
        {
            if (_client != null)
                Disconnect();


            _client = new UTPClient();
            _client.onConnectionState += OnClientStateChanged;
            _client.onDataReceived += OnClientDataReceived;

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            _connectClientCoroutine = StartCoroutine(_peerToPeer
                ? _client.ConnectP2P(ip, _dedicatedServer, _relayClientData)
                : _client.Connect(ip, port, _dedicatedServer, _relayClientData));
#elif UTP_LOBBYRELAY && UTP_SERVICES
            _connectClientCoroutine = StartCoroutine(_peerToPeer
                ? _client.ConnectP2P(ip, _dedicatedServer)
                : _client.Connect(ip, port, _dedicatedServer));
#endif
        }

        private void OnClientDataReceived(ByteData data)
        {
            onDataReceived?.Invoke(new Connection(-1), data, false);
        }

        private void OnClientStateChanged(ConnectionState state)
        {
			// Update clientState BEFORE firing events to prevent race condition
    		// where authentication tries to send before clientState is updated
    		clientState = state;

            if (state == ConnectionState.Connected)
                onConnected?.Invoke(new Connection(0), false);

            if (state == ConnectionState.Disconnected)
                onDisconnected?.Invoke(new Connection(0), DisconnectReason.ClientRequest, false);
			
        }

        /// <summary>
        /// Disconnects the client from the server.
        /// </summary>
        public void Disconnect()
        {
            if (_connectClientCoroutine != null)
            {
                StopCoroutine(_connectClientCoroutine);
                _connectClientCoroutine = null;
            }

            if (_client == null)
                return;

            _client.onConnectionState -= OnClientStateChanged;
            _client.onDataReceived -= OnClientDataReceived;

            _client.Stop();
            _client = null;
        }

        /// <summary>
        /// Raises the data received event for external listeners.
        /// </summary>
        /// <param name="conn">The connection that received the data.</param>
        /// <param name="data">The received data.</param>
        /// <param name="asServer">Whether this was received on the server side.</param>
        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        /// <summary>
        /// Raises the data sent event for external listeners.
        /// </summary>
        /// <param name="conn">The connection that sent the data.</param>
        /// <param name="data">The sent data.</param>
        /// <param name="asServer">Whether this was sent from the server side.</param>
        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        /// <summary>
        /// Sends data from the server to a specific client.
        /// </summary>
        /// <param name="target">The target client connection.</param>
        /// <param name="data">The data to send.</param>
        /// <param name="method">The network channel to use for sending.</param>
        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_server == null)
            {
                Debug.LogWarning("Cannot send to client: Server is not initialized");
                return;
            }

            if (listenerState is not ConnectionState.Connected)
            {
                Debug.LogWarning($"Cannot send to client: Server is not connected (state: {listenerState})");
                return;
            }

            if (!target.isValid)
                return;

            _server.SendToConnection(target.connectionId, data, method);
            RaiseDataSent(target, data, true);
        }

        /// <summary>
        /// Sends data from the client to the server.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="method">The network channel to use for sending.</param>
        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_client == null)
            {
                Debug.LogWarning("Cannot send to server: Client is not initialized");
                return;
            }

            if (clientState is not ConnectionState.Connected)
            {
                Debug.LogWarning($"Cannot send to server: Client is not connected (state: {clientState})");
                return;
            }

            _client.Send(data, method);
            RaiseDataSent(default, data, false);
        }

        /// <summary>
        /// Closes a specific client connection from the server side.
        /// </summary>
        /// <param name="conn">The connection to close.</param>
        public void CloseConnection(Connection conn)
        {
            _server?.Kick(conn.connectionId);
        }

        /// <summary>
        /// Processes incoming network messages for both server and client.
        /// Should be called regularly (typically each frame) to handle network events.
        /// </summary>
        /// <param name="delta">The time delta since the last call.</param>
        public void ReceiveMessages(float delta)
        {
            _server?.ReceiveMessages();
            _client?.ReceiveMessages();
        }

        /// <summary>
        /// Processes outgoing network messages for both server and client.
        /// Should be called regularly (typically each frame) to flush pending sends.
        /// </summary>
        /// <param name="delta">The time delta since the last call.</param>
        public void SendMessages(float delta)
        {
            _server?.SendMessages();
            _client?.SendMessages();
        }
    }
}

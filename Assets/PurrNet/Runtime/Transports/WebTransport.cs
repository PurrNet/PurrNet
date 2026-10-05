using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Authentication;
using JamesFrowen.SimpleWeb;
using PurrNet.Edgegap.Runtime;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Transports
{
    [AddComponentMenu("PurrNet/Transport/Web Transport")]
    public partial class WebTransport : GenericTransport, ITransport
    {
        [SerializeField] private AutomaticCloudSetups _automaticCloudSetups;

        [Header("Server Settings")]
        [SerializeField] private ushort _serverPort = 5001;
        [SerializeField] private int _maxConnections = 100;
        [SerializeField] private bool _forceIpv4;

        [Header("Client Settings")] [SerializeField]
        private string _address = "127.0.0.1";

        [Tooltip("The query to add to the address.\nEx: 'name=John' results in ws://localhost:5001?name=John")]
        [SerializeField]
        private string _query = "";

        [Tooltip("The path to add to the address.\nEx: '/game' results in ws://localhost:5001/game")] [SerializeField]
        private string _path = "";

        [Header("Shared Settings")]
        [Tooltip("The amount of time in seconds before socket is disconnected due to no data being received.")]
        [SerializeField] private float _timeoutInSeconds = 5f;

        [Header("SSL Settings")] [SerializeField]
        private bool _enableSSL;

        [SerializeField] private string _certPath;
        [SerializeField] private string _certPassword;
        [SerializeField] private SslProtocols _sslProtocols;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        public string address
        {
            get => _address;
            set => _address = value;
        }

        public string query
        {
            get => _query;
            set => _query = value;
        }

        public string path
        {
            get => _path;
            set => _path = value;
        }

        public ushort serverPort
        {
            get => _serverPort;
            set => _serverPort = value;
        }

        public int maxConnections
        {
            get => _maxConnections;
            set => _maxConnections = value;
        }

        public bool enableSSL
        {
            get => _enableSSL;
            set => _enableSSL = value;
        }

        public string certPath
        {
            get => _certPath;
            set => _certPath = value;
        }

        public string certPassword
        {
            get => _certPassword;
            set => _certPassword = value;
        }

        public SslProtocols sslProtocols
        {
            get => _sslProtocols;
            set => _sslProtocols = value;
        }

        public IReadOnlyList<Connection> connections => _connections;

        public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;

        public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;

        private SimpleWebServer _server;
        private SimpleWebClient _client;

        // SimpleWebTransport has no ping/pong frames, so send a 1 byte marker every timeout/3 seconds and filter it out on receive
        private const byte HEART_BEAT_MARKER = 0xFF;
        private static readonly ArraySegment<byte> _heartbeat = new ArraySegment<byte>(new byte[] { HEART_BEAT_MARKER });
        private static bool IsHeartbeat(ArraySegment<byte> data) => data.Count == 1 && data.Array[data.Offset] == HEART_BEAT_MARKER;

        private float _lastHeartbeatSent;
        private float _lastClientReceive;
        private bool _clientTimedOut;
        private bool _clientDisconnectRequested;
        private readonly HashSet<int> _timedOutConnections = new HashSet<int>();

        public bool shouldClientSendKeepAlive => true;

        private readonly List<Connection> _connections = new List<Connection>();

        public override ITransport transport => this;

        public override bool isSupported => true;

        TcpConfig _tcpConfig;

        public bool SupportsChannel(Channel channel)
        {
            if (channel != Channel.ReliableOrdered)
                return false;
            return true;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return 8192 * 2;
        }

        private void Awake()
        {
            CleanupServer();

            var timeoutMs = Mathf.RoundToInt(_timeoutInSeconds * 1000);
            _tcpConfig = new TcpConfig(noDelay: true, sendTimeout: timeoutMs, receiveTimeout: timeoutMs);
            SetupCloud();
        }

        private void ConstructClient()
        {
            _client = SimpleWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);
            _client.onConnect += OnClientConnected;
            _client.onDisconnect += OnClientDisconnected;
            _client.onData += OnClientReceivedData;
            _client.onError += OnClientError;
            _clientTimedOut = false;
            _clientDisconnectRequested = false;
        }

        private void CleanupClient()
        {
            if (_client == null)
                return;

            _client.onConnect -= OnClientConnected;
            _client.onDisconnect -= OnClientDisconnected;
            _client.onData -= OnClientReceivedData;
            _client.onError -= OnClientError;
            _client = null;
        }

        private void SetupCloud()
        {
            if (_automaticCloudSetups == null)
                return;

            if (_automaticCloudSetups.adaptToEdgegap)
            {
                var arbitrium = EdgegapUtils.GetArbitrium();
                if (arbitrium.TryGetPort("WS", 0, out var port))
                {
                    _serverPort = (ushort)port;
                    _address = "0.0.0.0";
                    _enableSSL = false;
                    PurrLogger.Log($"Edgegap Auto-Setup: 0.0.0.0:{port} ssl: false");
                }
                else if (arbitrium.TryGetPort("WSS", 0, out var sslport))
                {
                    _serverPort = (ushort)sslport;
                    _address = "0.0.0.0";
                    _enableSSL = true;

                    PurrLogger.Log($"Edgegap Auto-Setup: 0.0.0.0:{sslport} ssl: true");
                }
                else PurrLogger.Log("Edgegap Auto-Setup: No WebSocket port");
            }
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        private void ConstructServer()
        {
            CleanupServer();

            var sslConfig = new SslConfig(_enableSSL, _certPath, _certPassword, _sslProtocols);
            _server = new SimpleWebServer(5000, _tcpConfig, ushort.MaxValue, 5000, sslConfig);
            _server.onConnect += OnClientConnectedToServer;
            _server.onDisconnect += OnClientDisconnectedFromServer;
            _server.onData += OnServerReceivedData;
            _server.onError += OnServerError;
        }

        private void CleanupServer()
        {
            _connections.Clear();
            _timedOutConnections.Clear();

            if (_server != null)
            {
                if (_server.Active)
                    _server.Stop();

                _server.onConnect -= OnClientConnectedToServer;
                _server.onDisconnect -= OnClientDisconnectedFromServer;
                _server.onData -= OnServerReceivedData;
                _server.onError -= OnServerError;
            }

            _server = null;
        }

        private void OnClientReceivedData(ArraySegment<byte> data)
        {
            _lastClientReceive = Time.realtimeSinceStartup;
            if (IsHeartbeat(data)) return;

            var byteData = new ByteData(data.Array, data.Offset, data.Count);
            onDataReceived?.Invoke(new Connection(0), byteData, false);
        }

        private void OnClientDisconnected()
        {
            if (clientState == ConnectionState.Disconnected)
                return;

            var wasConnected = clientState == ConnectionState.Connected;
            var reason = _clientTimedOut ? DisconnectReason.Timeout : DisconnectReason.ClientRequest;
            _clientTimedOut = false;

            // A browser close callback can arrive long after a timeout. Detach this
            // client so it cannot disconnect a subsequent connection or deliver stale data.
            CleanupClient();

            clientState = ConnectionState.Disconnecting;
            TriggerConnectionStateEvent(false);

            if (wasConnected)
                onDisconnected?.Invoke(new Connection(0), reason, false);

            clientState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(false);
        }

        private void OnClientError(Exception exception)
        {
            if (IsTimeout(exception))
            {
                if (!_clientDisconnectRequested)
                    _clientTimedOut = true;
                return;
            }

            Debug.LogException(exception);
        }

        private static bool IsTimeout(Exception exception)
        {
            // NetworkStream wraps socket receive timeouts in IOException. Blocking
            // sockets can report WouldBlock for a receive timeout on Unix.
            for (var error = exception; error != null; error = error.InnerException)
            {
                if (error is SocketException socketError &&
                    socketError.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
                    return true;
            }

            return false;
        }

        private void OnClientConnected()
        {
            _lastClientReceive = Time.realtimeSinceStartup;
            _clientTimedOut = false;

            clientState = ConnectionState.Connected;
            TriggerConnectionStateEvent(false);

            onConnected?.Invoke(new Connection(0), false);
        }

        public void SendMessages(float delta) { }

        public void ReceiveMessages(float delta)
        {
            _server?.ProcessMessageQueue();
            _client?.ProcessMessageQueue();
        }

        public void UnityUpdate(float delta)
        {
            _server?.ProcessMessageQueue();
            _client?.ProcessMessageQueue();
            CheckClientTimeout();
            SendHeartbeatsIfDue();
        }

        private void SendHeartbeatsIfDue()
        {
            if (_timeoutInSeconds <= 0f) return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastHeartbeatSent < _timeoutInSeconds / 3f) return;
            _lastHeartbeatSent = now;

            if (clientState == ConnectionState.Connected && !_clientDisconnectRequested)
            {
                _client.Send(_heartbeat);
            }

            if (listenerState == ConnectionState.Connected)
            {
                for (int i = 0; i < _connections.Count; i++)
                {
                    _server.SendOne(_connections[i].connectionId, _heartbeat);
                }
            }
        }

        // WebGL clients use the browser WebSocket and never get the TcpConfig timeouts,
        // so the receive timeout is enforced manually on all platforms
        private void CheckClientTimeout()
        {
            if (_timeoutInSeconds <= 0f || _clientDisconnectRequested || clientState != ConnectionState.Connected)
                return;

            if (Time.realtimeSinceStartup - _lastClientReceive <= _timeoutInSeconds)
                return;

            // WebSocket.close() starts a handshake; an unreachable peer may not
            // finish it promptly. Complete the transport disconnect now.
            _clientTimedOut = true;
            _client.Disconnect();
            OnClientDisconnected();
        }

        public void Listen(ushort port)
        {
            if (listenerState is ConnectionState.Connected or ConnectionState.Connecting)
                return;

            listenerState = ConnectionState.Connecting;
            TriggerConnectionStateEvent(true);

            ConstructServer();
            _server.Start(port, _forceIpv4);

            listenerState = ConnectionState.Connected;
            TriggerConnectionStateEvent(true);
        }

        protected override void StartServerInternal()
        {
            Listen(_serverPort);
        }

        private void OnServerError(int clientId, Exception exception)
        {
            if (IsTimeout(exception))
            {
                _timedOutConnections.Add(clientId);
                return;
            }

            Debug.LogException(exception);
        }

        private void OnServerReceivedData(int clientId, ArraySegment<byte> data)
        {
            if (IsHeartbeat(data)) return;

            var byteData = new ByteData(data.Array, data.Offset, data.Count);
            var conn = new Connection(clientId);
            onDataReceived?.Invoke(conn, byteData, true);
        }

        private void OnClientDisconnectedFromServer(int clientId)
        {
            var conn = new Connection(clientId);

            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i] == conn)
                {
                    _connections.RemoveAt(i);
                    break;
                }
            }

            var reason = _timedOutConnections.Remove(clientId) ? DisconnectReason.Timeout : DisconnectReason.ClientRequest;
            onDisconnected?.Invoke(conn, reason, true);
        }

        private void OnClientConnectedToServer(int clientId)
        {
            if (_connections.Count >= _maxConnections)
            {
                Debug.LogWarning("Max connections reached. Kicking client.");
                _server.KickClient(clientId);
                return;
            }

            var conn = new Connection(clientId);
            _connections.Add(conn);
            onConnected?.Invoke(conn, true);
        }

        public void StopListening()
        {
            if (listenerState is ConnectionState.Disconnecting or ConnectionState.Disconnected)
                return;

            listenerState = ConnectionState.Disconnecting;
            TriggerConnectionStateEvent(true);

            _server.Stop();

            listenerState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(true);

            CleanupServer();
        }

        public void Connect(string ip, ushort port)
        {
            if (clientState != ConnectionState.Disconnected)
                return;

            var builder = new UriBuilder
            {
                Scheme = _enableSSL ? "wss" : "ws",
                Host = ip,
                Port = port,
                Query = _query,
                Path = _path
            };

            ConstructClient();
            clientState = ConnectionState.Connecting;
            TriggerConnectionStateEvent(false);

            _client.Connect(builder.Uri);

            clientState = _client.ConnectionState switch
            {
                ClientState.Connected => ConnectionState.Connected,
                ClientState.Connecting => ConnectionState.Connecting,
                ClientState.Disconnecting => ConnectionState.Disconnecting,
                _ => ConnectionState.Disconnected
            };

            TriggerConnectionStateEvent(false);
        }

        ConnectionState _prevClientState = ConnectionState.Disconnected;
        ConnectionState _prevServerState = ConnectionState.Disconnected;

        private void TriggerConnectionStateEvent(bool asServer)
        {
            if (asServer)
            {
                if (_prevServerState != listenerState)
                {
                    _prevServerState = listenerState;
                    onConnectionState?.Invoke(listenerState, true);
                }
            }
            else
            {
                if (_prevClientState != clientState)
                {
                    _prevClientState = clientState;
                    onConnectionState?.Invoke(clientState, false);
                }
            }
        }

        protected override void StartClientInternal()
        {
            Connect(_address, _serverPort);
        }

        public void Disconnect()
        {
            if (clientState is ConnectionState.Disconnecting or ConnectionState.Disconnected)
                return;

            _clientDisconnectRequested = true;
            _clientTimedOut = false;
            _client.Disconnect();
            TriggerConnectionStateEvent(false);
        }

        private void OnDisable()
        {
            StopListening();
            Disconnect();
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (listenerState != ConnectionState.Connected)
                return;

            if (!target.isValid)
                return;

            _server.SendOne(target.connectionId, new ArraySegment<byte>(data.data, data.offset, data.length));
            RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (clientState != ConnectionState.Connected || _clientDisconnectRequested)
                return;

            _client.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            RaiseDataSent(default, data, false);
        }

        public void CloseConnection(Connection conn)
        {
            if (listenerState != ConnectionState.Connected)
                return;

            if (!conn.isValid)
                return;

            _server.KickClient(conn.connectionId);
        }
    }
}

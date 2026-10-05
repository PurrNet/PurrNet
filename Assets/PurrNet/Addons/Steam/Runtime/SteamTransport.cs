#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System;
using System.Collections.Generic;
using PurrNet.Transports;
using PurrConnectionState = PurrNet.Transports.ConnectionState;
using UnityEngine;

namespace PurrNet.Steam
{
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("PurrNet/Transport/Steam Transport")]
    public partial class SteamTransport : GenericTransport, ITransport
    {
        [Header("Server Settings")] [SerializeField]
        private ushort _serverPort = 5003;

        [SerializeField] private bool _dedicatedServer;
        [SerializeField] private bool _peerToPeer = true;

        [Header("Client Settings")] [SerializeField]
        private string _address = "127.0.0.1";

        [Header("Send Settings (Per Connection)")]
        [SerializeField, Min(1), InspectorName("Send Rate (KiB/s)")]
        [Tooltip("Per-connection Steam send rate in KiB/s. Must be positive. Applies on the next connect or listen.")]
        private int _sendRateKiBPerSecond = SteamSendSettings.DefaultRateKiBPerSecond;

        [SerializeField, Min(SteamSendSettings.MinBufferSizeKiB), InspectorName("Send Buffer Size (KiB)")]
        [Tooltip("Per-connection Steam send buffer in KiB. Minimum 16 KiB to fit a reliable packet. " +
                 "Applies on the next connect or listen.")]
        private int _sendBufferSizeKiB = SteamSendSettings.DefaultBufferSizeKiB;

        public int sendRateKiBPerSecond
        {
            get => _sendRateKiBPerSecond;
            set
            {
                SteamSendSettings.ToBytes(value, nameof(sendRateKiBPerSecond));
                _sendRateKiBPerSecond = value;
            }
        }

        public int sendBufferSizeKiB
        {
            get => _sendBufferSizeKiB;
            set
            {
                SteamSendSettings.BufferSizeToBytes(value, nameof(sendBufferSizeKiB));
                _sendBufferSizeKiB = value;
            }
        }

        public ushort serverPort
        {
            get => _serverPort;
            set => _serverPort = value;
        }

        public bool dedicatedServer
        {
            get => _dedicatedServer;
            set => _dedicatedServer = value;
        }

        public bool peerToPeer
        {
            get => _peerToPeer;
            set => _peerToPeer = value;
        }

        public string address
        {
            get => _address;
            set => _address = value;
        }

        public bool SupportsChannel(Channel channel)
        {
            if (channel is Channel.ReliableOrdered or Channel.Unreliable)
                return true;
            return false;
        }

        public bool measuresRoundTripTime => true;

        public int GetRoundTripTime(Connection conn, bool asServer)
        {
            if (asServer)
                return _server?.GetRoundTripTime(conn.connectionId) ?? -1;
            return _client?.GetRoundTripTime() ?? -1;
        }

        public SteamSendStatistics GetSendStatistics(Connection conn, bool asServer)
        {
            return asServer
                ? _server?.GetSendStatistics(conn.connectionId) ?? default
                : _client?.sendStatistics ?? default;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return channel switch
            {
                Channel.Unreliable => 1024,
                Channel.UnreliableSequenced or Channel.ReliableUnordered or Channel.ReliableOrdered => SteamSendSettings.ReliableMessageSizeBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        public override bool isSupported => true;
#else
        public override bool isSupported => false;
#endif

        public override ITransport transport => this;

        private readonly List<Connection> _connections = new List<Connection>();

        public IReadOnlyList<Connection> connections => _connections;

        private PurrConnectionState _listenerState = PurrConnectionState.Disconnected;

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

        private PurrConnectionState _clientState = PurrConnectionState.Disconnected;

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

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        private SteamServer _server;
        private SteamClient _client;

        protected override void StartClientInternal()
        {
            SetTelemetryAppId();
            Connect(_address, _serverPort);
        }

        protected override void StartServerInternal()
        {
            SetTelemetryAppId();
            Listen(_serverPort);
        }

        public void Listen(ushort port)
        {
            var sendSettings = new SteamSendSettings(_sendRateKiBPerSecond, _sendBufferSizeKiB);
            if (_server != null)
                StopListening();

            listenerState = PurrConnectionState.Connecting;

            _server = new SteamServer();
            _connections.Clear();

            if (_peerToPeer)
                _server.ListenP2P(_dedicatedServer, sendSettings);
            else _server.Listen(port, _dedicatedServer, sendSettings);

            if (_server.listening)
            {
                listenerState = PurrConnectionState.Connected;
            }
            else
            {
                listenerState = PurrConnectionState.Disconnecting;
                listenerState = PurrConnectionState.Disconnected;
            }

            _server.onDataReceived += OnServerData;
            _server.onRemoteConnected += OnRemoteConnected;
            _server.onRemoteDisconnected += OnRemoteDisconnected;
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

        public void StopListening()
        {
            if (listenerState != PurrConnectionState.Disconnected)
                listenerState = PurrConnectionState.Disconnecting;
            _server?.Stop();
            listenerState = PurrConnectionState.Disconnected;
            _server = null;
        }

        private Coroutine _connectClientCoroutine;

        public void Connect(string ip, ushort port)
        {
            var sendSettings = new SteamSendSettings(_sendRateKiBPerSecond, _sendBufferSizeKiB);
            if (_client != null)
                Disconnect();

            _client = new SteamClient();
            _client.onConnectionState += OnClientStateChanged;
            _client.onDataReceived += OnClientDataReceived;

            _connectClientCoroutine = StartCoroutine(_peerToPeer
                ? _client.ConnectP2P(ip, _dedicatedServer, sendSettings)
                : _client.Connect(ip, port, _dedicatedServer, sendSettings));
        }

        private void OnClientDataReceived(ByteData data)
        {
            onDataReceived?.Invoke(new Connection(-1), data, false);
        }

        private void OnClientStateChanged(PurrConnectionState state)
        {
            if (state == PurrConnectionState.Connected)
                onConnected?.Invoke(new Connection(0), false);

            if (state == PurrConnectionState.Disconnected)
                onDisconnected?.Invoke(new Connection(0), DisconnectReason.ClientRequest, false);

            clientState = state;
        }

        public void Disconnect()
        {
            if (_connectClientCoroutine != null)
            {
                StopCoroutine(_connectClientCoroutine);
                _connectClientCoroutine = null;
            }

            if (_client == null)
                return;

            _client.Stop();
            _client = null;
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_server == null)
                return;

            if (listenerState is not PurrConnectionState.Connected)
                return;

            if (!target.isValid)
                return;

            if (_server.TrySendToConnection(target.connectionId, data, method))
                RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_client == null)
                return;

            if (_client.TrySend(data, method))
                RaiseDataSent(default, data, false);
        }

        public void CloseConnection(Connection conn)
        {
            _server?.Kick(conn.connectionId);
        }

        public void ReceiveMessages(float delta)
        {
            _server?.ReceiveMessages();
            _client?.ReceiveMessages();
        }

        public void UnityUpdate(float delta)
        {
            ReceiveMessages(delta);
        }

        public void SendMessages(float delta)
        {
            _server?.SendMessages();
            _client?.SendMessages();
        }

        public bool FlushConnection(Connection conn, bool asServer)
        {
            if (asServer)
            {
                if (_server == null || listenerState is not PurrConnectionState.Connected)
                    return false;

                _server.FlushConnection(conn.connectionId);
                return true;
            }

            if (_client == null)
                return false;

            _client.SendMessages();
            return true;
        }

        public ulong GetSteamID(Connection conn)
        {
            return _server?.GetSteamID(conn.connectionId) ?? 0;
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        void SetTelemetryAppId()
        {
            try
            {
                var appId = Steamworks.SteamUtils.GetAppID();
                PurrInternalTelemetry.TransportMetadata["steam_app_id"] = appId.m_AppId.ToString();
            }
            catch
            {
                // Steamworks may not be initialized
            }
        }
#else
        void SetTelemetryAppId() { }
#endif
    }
}

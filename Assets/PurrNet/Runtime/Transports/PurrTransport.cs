using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using JamesFrowen.SimpleWeb;
using JetBrains.Annotations;
using LiteNetLib;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Transports
{
    public partial class PurrTransport : GenericTransport, ITransport
    {
        enum SERVER_PACKET_TYPE : byte
        {
            SERVER_CLIENT_CONNECTED = 0,
            SERVER_CLIENT_DISCONNECTED = 1,
            SERVER_CLIENT_DATA = 2,
            SERVER_AUTHENTICATED = 3,
            SERVER_AUTHENTICATION_FAILED = 4
        }

        enum HOST_PACKET_TYPE : byte
        {
            SEND_KEEPALIVE = 0,
            SEND_ONE = 1,
            KICK_PLAYER = 2
        }

        [Serializable, UsedImplicitly]
        private struct ClientAuthenticate
        {
            public string roomName;
            public string clientSecret;
        }

        [Header("Remote Settings")]
        [SerializeField, HideInInspector] private string _masterServer = "https://purrtransport.purrservers.com/";
        [SerializeField, HideInInspector] private string _roomName;
        [SerializeField, HideInInspector] private string _region = "eu-central";
        [SerializeField, HideInInspector] private string _host;

        [Header("Shared Settings")]
        [Tooltip("The amount of time in seconds before socket is disconnected due to no data being received.")]
        [SerializeField, HideInInspector] private float _timeoutInSeconds = 5f;
        [SerializeField, HideInInspector] private bool _pollEventsInUpdate;

        public string region
        {
            get => _region;
            set => _region = value;
        }

        public string host
        {
            get => _host;
            set => _host = value;
        }

        public string roomName
        {
            get => _roomName;
            set => _roomName = value;
        }

        public bool hasRegionAndHost => !string.IsNullOrEmpty(_region) && !string.IsNullOrEmpty(_host);

        public override bool isSupported => true;

        public override ITransport transport => this;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        private ConnectionState _listenerState = ConnectionState.Disconnected;
        private ConnectionState _clientState = ConnectionState.Disconnected;

        public bool shouldServerSendKeepAlive => true;

        public bool shouldClientSendKeepAlive => true;

        public ConnectionState listenerState
        {
            get => _listenerState;
            private set
            {
                if (_listenerState == value)
                    return;

                _listenerState = value;
                onConnectionState?.Invoke(value, true);
            }
        }

        public ConnectionState clientState
        {
            get => _clientState;
            private set
            {
                if (_clientState == value)
                    return;

                _clientState = value;
                onConnectionState?.Invoke(value, false);
            }
        }

        public bool SupportsChannel(Channel channel)
        {
            return true;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            if (_isUsingUDP)
            {
                try
                {
                    var method = UDPTransport.ToDeliveryMethod(channel);
                    var result = asServer ?
                        _udpServer.FirstPeer.GetMaxSinglePacketSize(method) :
                        _udpClient.FirstPeer.GetMaxSinglePacketSize(method);
                    return result - 16; // give the relay some space for metadata
                }
                catch
                {
                    return 1024;
                }
            }

            return 8192 * 2;
        }


        public IReadOnlyList<Connection> connections => _connections;
        private readonly List<Connection> _connections = new List<Connection>();

        private void Reset()
        {
            _roomName = Guid.NewGuid().ToString().Replace("-", "");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_masterServer == "https://purrbalancer.riten.dev:8080/")
            {
                _masterServer = "https://purrtransport.purrservers.com/";
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
            }
        }
#endif

        readonly List<CancellationTokenSource> _cancellationTokenSourcesServer = new List<CancellationTokenSource>();
        readonly List<CancellationTokenSource> _cancellationTokenSourcesClient = new List<CancellationTokenSource>();

        private void CancelAll(bool asServer)
        {
            var sources = asServer ? _cancellationTokenSourcesServer : _cancellationTokenSourcesClient;
            for (var i = 0; i < sources.Count; i++)
                sources[i].Cancel();
            sources.Clear();
        }

        private void AddCancellation(CancellationTokenSource token, bool asServer)
        {
            if (asServer)
                _cancellationTokenSourcesServer.Add(token);
            else _cancellationTokenSourcesClient.Add(token);
        }

        protected override void StartClientInternal()
        {
            Connect(null, 0);
        }

        private EventBasedNetListener _serverListener;
        private EventBasedNetListener _clientListener;
        private NetManager _udpClient;
        private NetManager _udpServer;

        private bool _isUsingUDP;

        private void OnEnable()
        {
            _serverListener = new EventBasedNetListener();
            _clientListener = new EventBasedNetListener();

            _udpClient = new NetManager(_clientListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                EnableStatistics = false,
                IPv6Enabled = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _udpServer = new NetManager(_serverListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                EnableStatistics = false,
                IPv6Enabled = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _clientListener.PeerConnectedEvent += OnClientConnectedUDP;
            _clientListener.PeerDisconnectedEvent += OnClientDisconnectedUDP;
            _clientListener.NetworkReceiveEvent += OnClientDataUDP;

            _serverListener.PeerConnectedEvent += OnHostConnectedUDP;
            _serverListener.PeerDisconnectedEvent += OnHostDisconnectedUDP;
            _serverListener.NetworkReceiveEvent += OnHostDataUDP;
        }

        private void CleanupUdp()
        {
            _udpClient.Stop();
            _udpServer?.Stop();
        }

        private SimpleWebClient _server;
        private SimpleWebClient _client;
        private HostJoinInfo _hostJoinInfo;
        readonly TcpConfig _tcpConfig = new(noDelay: true, sendTimeout: 0, receiveTimeout: 0);

        protected override void StartServerInternal()
        {
            Listen(0);
        }

        private void OnHostDataUDP(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var data = new ByteData(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            OnHostData(data.segment);
        }

        private void OnHostData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0)
                return;

            var type = (SERVER_PACKET_TYPE)data.Array[data.Offset];

            switch (type)
            {
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATED:
                    listenerState = ConnectionState.Connected;
                    break;
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATION_FAILED:
                    StopListening();
                    break;
                case SERVER_PACKET_TYPE.SERVER_CLIENT_CONNECTED:
                {
                    _packer.ResetPositionAndMode(false);
                    _packer.WriteBytes(new ByteData(data.Array, data.Offset + 1, data.Count - 1));
                    _packer.ResetPositionAndMode(true);

                    int clientId = default;
                    int connectionCount = (data.Count - 1) / 4;

                    for (var i = 0; i < connectionCount; i++)
                    {
                        Packer<int>.Read(_packer, ref clientId);
                        var conn = new Connection(clientId);
                        _connections.Add(conn);
                        onConnected?.Invoke(new Connection(clientId), true);
                    }

                    break;
                }
                case SERVER_PACKET_TYPE.SERVER_CLIENT_DISCONNECTED:
                {
                    var subdata = new ByteData(data.Array, data.Offset + 1, data.Count - 1);

                    _packer.ResetPositionAndMode(false);
                    _packer.WriteBytes(subdata);
                    _packer.ResetPositionAndMode(true);

                    int clientId = default;
                    Packer<int>.Read(_packer, ref clientId);

                    var conn = new Connection(clientId);
                    _connections.Remove(conn);
                    onDisconnected?.Invoke(conn, DisconnectReason.ClientRequest, true);
                    break;
                }
                case SERVER_PACKET_TYPE.SERVER_CLIENT_DATA:
                {
                    if (data.Count <= 5)
                        return;

                    int connId = data.Array[data.Offset + 1] |
                                 data.Array[data.Offset + 2] << 8 |
                                 data.Array[data.Offset + 3] << 16 |
                                 data.Array[data.Offset + 4] << 24;

                    RaiseDataReceived(new Connection(connId), new ByteData(data.Array, data.Offset + 5, data.Count - 5),
                        true);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(type.ToString());
            }
        }

        private void OnClientDataUDP(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var data = reader.GetRemainingBytesSegment();
            OnClientData(data);
        }

        private void OnClientData(ArraySegment<byte> data)
        {
            if (clientState == ConnectionState.Connected)
            {
                var bdata = new ByteData(data.Array, data.Offset, data.Count);
                RaiseDataReceived(new Connection(0), bdata, false);
            }
            else
            {
                if (data.Array == null || data.Count == 0)
                    return;

                var type = (SERVER_PACKET_TYPE)data.Array[data.Offset];

                switch (type)
                {
                    case SERVER_PACKET_TYPE.SERVER_AUTHENTICATED:
                        clientState = ConnectionState.Connected;
                        onConnected?.Invoke(new Connection(0), false);
                        break;
                    case SERVER_PACKET_TYPE.SERVER_AUTHENTICATION_FAILED:
                        Disconnect();
                        break;
                    default: throw new ArgumentOutOfRangeException(type.ToString());
                }
            }
        }

        private void OnHostConnected()
        {
            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _hostJoinInfo.secret
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _server.Send(data);
        }

        private void OnHostConnectedUDP(NetPeer peer)
        {
            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _hostJoinInfo.secret
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _udpServer.SendToAll(data, DeliveryMethod.ReliableOrdered);
        }

        private void OnClientConnected()
        {
            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _clientJoinInfo.secret
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _client.Send(data);
        }

        private void OnClientConnectedUDP(NetPeer peer)
        {
            var authenticate = new ClientAuthenticate
            {
                roomName = _roomName,
                clientSecret = _clientJoinInfo.secret
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _udpClient.SendToAll(data, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostDisconnectedUDP(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            StopListening();
        }

        private void OnHostDisconnected()
        {
            StopListening();
        }

        private void OnClientDisconnected()
        {
            Disconnect();
        }

        private void OnClientDisconnectedUDP(NetPeer peer, DisconnectInfo disconnectinfo)
        {
            Disconnect();
        }

        public async void Listen(ushort port)
        {
            try
            {
                if (listenerState != ConnectionState.Disconnected)
                    StopListening();

                listenerState = ConnectionState.Connecting;

                _server = SimpleWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);

                _server.onConnect += OnHostConnected;
                _server.onData += OnHostData;
                _server.onDisconnect += OnHostDisconnected;
                _server.onError += OnError;

                try
                {
                    var token = new CancellationTokenSource();
                    AddCancellation(token, true);

                    if (!hasRegionAndHost)
                    {
                        var relayServer = await PurrTransportUtils.GetRelayServerAsync(_masterServer, token);
                        _region = relayServer.region;
                        _host = relayServer.host;
                    }

                    if (token.IsCancellationRequested)
                        return;

                    _hostJoinInfo = await PurrTransportUtils.Alloc(_masterServer, _region, _roomName, token);

                    if (token.IsCancellationRequested)
                        return;

                    if (Application.platform != RuntimePlatform.WebGLPlayer)
                    {
                        _isUsingUDP = true;
                        _udpServer.StartInManualMode(0);
                        var addresses = await Dns.GetHostAddressesAsync(_host);
                        var ipv4 = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                                   ?? IPAddress.Any;
                        _udpServer.Connect(ipv4.ToString(), _hostJoinInfo.udpPort, "PurrNet");
                    }
                    else
                    {
                        _isUsingUDP = false;
                        var builder = new UriBuilder
                        {
                            Scheme = _hostJoinInfo.ssl ? "wss" : "ws",
                            Host = _host,
                            Port = _hostJoinInfo.port,
                            Query = string.Empty,
                            Path = string.Empty
                        };

                        _server.Connect(builder.Uri);
                    }
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                catch (Exception e)
                {
                    StopListening();
                    PurrLogger.LogException(e);
                }
            }
            catch (Exception e)
            {
                StopListening();
                PurrLogger.LogException(e.Message);
            }
        }

        private static void OnError(Exception obj)
        {
            PurrLogger.LogException(obj);
        }

        public void StopListening()
        {
            _connections.Clear();
            CancelAll(true);

            if (_server != null)
            {
                _server.onConnect -= OnHostConnected;
                _server.onData -= OnHostData;
                _server.onDisconnect -= OnHostDisconnected;
                _server.onError -= OnError;
                _server.Disconnect();
            }

            _udpServer?.Stop();

            _server = null;

            if (listenerState is ConnectionState.Connecting or ConnectionState.Connected)
                listenerState = ConnectionState.Disconnecting;
            listenerState = ConnectionState.Disconnected;
        }

        public void Disconnect()
        {
            if (clientState != ConnectionState.Disconnected)
                onDisconnected?.Invoke(default, DisconnectReason.ClientRequest, false);

            CancelAll(false);

            if (_client != null)
            {
                _client.onConnect -= OnClientConnected;
                _client.onData -= OnClientData;
                _client.onDisconnect -= OnClientDisconnected;
                _client.onError -= OnError;
                _client.Disconnect();
            }

            _udpClient?.Stop();

            _client = null;

            if (clientState is ConnectionState.Connecting or ConnectionState.Connected)
                clientState = ConnectionState.Disconnecting;
            clientState = ConnectionState.Disconnected;
        }

        private ClientJoinInfo _clientJoinInfo;

        public async void Connect(string ip, ushort port)
        {
            try
            {
                if (clientState != ConnectionState.Disconnected)
                    Disconnect();

                clientState = ConnectionState.Connecting;

                var token = new CancellationTokenSource();

                while (listenerState == ConnectionState.Connecting)
                    await UnityLatestUpdate.Yield();

                if (token.IsCancellationRequested)
                    return;

                _client = SimpleWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);
                _client.onConnect += OnClientConnected;
                _client.onData += OnClientData;
                _client.onDisconnect += OnClientDisconnected;
                _client.onError += OnError;

                AddCancellation(token, false);

                _clientJoinInfo = await PurrTransportUtils.Join(_masterServer, _roomName, token);

                if (token.IsCancellationRequested)
                    return;

                if (Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    _isUsingUDP = true;
                    _udpClient.StartInManualMode(0);

                    var addresses = await Dns.GetHostAddressesAsync(_clientJoinInfo.host);
                    var ipv4 = addresses.FirstOrDefault(ipArd => ipArd.AddressFamily == AddressFamily.InterNetwork)
                               ?? IPAddress.Any;

                    _udpClient.Connect(ipv4.ToString(), _clientJoinInfo.udpPort, "PurrNet");
                }
                else
                {
                    var builder = new UriBuilder
                    {
                        Scheme = _clientJoinInfo.ssl ? "wss" : "ws",
                        Host = _clientJoinInfo.host,
                        Port = _clientJoinInfo.port
                    };

                    _client.Connect(builder.Uri);
                }
            }
            catch (Exception e)
            {
                Disconnect();
                PurrLogger.LogException(e.Message);
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

        static readonly BitPacker _packer = new BitPacker();

        public void SendServerKeepAlive()
        {
            if (_server == null)
                return;

            _packer.ResetPositionAndMode(false);
            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.SEND_KEEPALIVE);
            var data = _packer.ToByteData();
            _server.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
        }

        public void SendToClient(Connection target, ByteData odata, Channel method = Channel.ReliableOrdered)
        {
            if (listenerState != ConnectionState.Connected)
                return;

            if (!target.isValid)
                return;

            _packer.ResetPositionAndMode(false);

            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.SEND_ONE);
            Packer<int>.Write(_packer, target.connectionId);

            if (_isUsingUDP)
                Packer<byte>.Write(_packer, (byte)UDPTransport.ToDeliveryMethod(method));

            _packer.WriteBytes(odata);

            var data = _packer.ToByteData();

            if (_isUsingUDP)
            {
                var deliveryMethod = UDPTransport.ToDeliveryMethod(method);
                _udpServer.SendToAll(data.data, data.offset, data.length, deliveryMethod);
            }
            else _server.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (clientState != ConnectionState.Connected)
                return;

            if (_isUsingUDP)
            {
                var deliveryMethod = UDPTransport.ToDeliveryMethod(method);
                _packer.ResetPositionAndMode(false);
                Packer<byte>.Write(_packer, (byte)deliveryMethod);
                _packer.WriteBytes(data);
                var byteData = _packer.ToByteData();
                _udpClient.SendToAll(byteData.data, byteData.offset, byteData.length, deliveryMethod);
            }
            else
            {
                _client.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            }

            RaiseDataSent(default, data, false);
        }

        public void CloseConnection(Connection conn)
        {
            _packer.ResetPositionAndMode(false);

            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.KICK_PLAYER);
            Packer<int>.Write(_packer, conn.connectionId);

            var data = _packer.ToByteData();

            if (_isUsingUDP)
                _udpServer.SendToAll(data.data, data.offset, data.length, DeliveryMethod.ReliableSequenced);
            else _server.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            RaiseDataSent(conn, data, true);
        }

        public void ReceiveMessages(float delta)
        {
            if (!_pollEventsInUpdate)
            {
                if (_isUsingUDP)
                {
                    _udpClient.PollEvents();
                    _udpServer.PollEvents();
                }
                else
                {
                    _server?.ProcessMessageQueue();
                    _client?.ProcessMessageQueue();
                }
            }
        }

        public void UnityUpdate(float delta)
        {
            if (_pollEventsInUpdate)
            {
                if (_isUsingUDP)
                {
                    _udpClient.PollEvents();
                    _udpServer.PollEvents();
                }
                else
                {
                    _server?.ProcessMessageQueue();
                    _client?.ProcessMessageQueue();
                }
            }
        }

        public void SendMessages(float delta)
        {
            if (_isUsingUDP)
            {
                var dInMs = Mathf.FloorToInt(delta * 1000);

                _udpClient.ManualUpdate(dInMs);
                _udpServer.ManualUpdate(dInMs);
            }
        }

        private void OnDisable()
        {
            StopListening();
            Disconnect();
            CleanupUdp();
        }
    }
}

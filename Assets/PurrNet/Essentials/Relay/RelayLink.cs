using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using JamesFrowen.SimpleWeb;
using LiteNetLib;
using UnityEngine;

namespace PurrNet.Essentials.Relay
{
    internal sealed class RelayLink
    {
        private const string ConnectionKey = "PurrNet";

        private readonly float _timeoutSeconds;
        private EventBasedNetListener _listener;
        private NetManager _udp;
        private NetPeer _peer;
        private SimpleWebClient _web;
        private bool _closed;

        public event Action onConnected;
        public event Action onDisconnected;
        public event Action<ArraySegment<byte>> onData;
        public event Action<Exception> onError;

        public bool isUdp { get; private set; }

        public int roundTripTime => _peer != null ? _peer.RoundTripTime : -1;

        public RelayLink(float timeoutSeconds)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public static bool prefersWebSocket => Application.platform == RuntimePlatform.WebGLPlayer;

        public async Task Connect(string host, RelayJoinDetails details, bool webSocket)
        {
            if (webSocket)
            {
                isUdp = false;
                _web = SimpleWebClient.Create(ushort.MaxValue, 5000, new TcpConfig(true, 0, 0));
                _web.onConnect += RaiseConnected;
                _web.onDisconnect += RaiseDisconnected;
                _web.onData += RaiseData;
                _web.onError += RaiseError;

                var builder = new UriBuilder
                {
                    Scheme = details.ssl ? "wss" : "ws",
                    Host = host,
                    Port = details.port
                };
                _web.Connect(builder.Uri);
                return;
            }

            isUdp = true;
            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnPeerData;

            _udp = new NetManager(_listener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                EnableStatistics = false,
                IPv6Enabled = false,
                ReliableRepairs = true,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutSeconds * 1000)
            };
            _udp.StartInManualMode(0);

            var addresses = await Dns.GetHostAddressesAsync(host);
            if (_closed)
                return;

            var address = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Any;
            _peer = _udp.Connect(new IPEndPoint(address, details.udpPortV2), ConnectionKey);
        }

        public int GetMaxPacketSize(RelayDelivery delivery)
        {
            if (!isUdp)
                return 8192 * 2;

            try
            {
                return _peer.GetMaxSinglePacketSize((DeliveryMethod)delivery);
            }
            catch
            {
                return 1024;
            }
        }

        public void Send(byte[] data, int offset, int length, RelayDelivery delivery)
        {
            if (_closed)
                return;

            if (isUdp)
                _peer?.Send(data, offset, length, (DeliveryMethod)delivery);
            else
                _web?.Send(new ArraySegment<byte>(data, offset, length));
        }

        public void Poll()
        {
            if (_closed)
                return;

            if (isUdp)
            {
                if (_udp != null && _udp.IsRunning)
                    _udp.PollEvents();
            }
            else
                _web?.ProcessMessageQueue();
        }

        public void Update(float deltaSeconds)
        {
            if (_closed || !isUdp)
                return;

            if (_udp != null && _udp.IsRunning)
                _udp.ManualUpdate(deltaSeconds * 1000f);
        }

        public void Flush()
        {
            if (!_closed && isUdp)
                _peer?.FlushSends();
        }

        public void Close()
        {
            if (_closed)
                return;

            _closed = true;

            if (_web != null)
            {
                _web.onConnect -= RaiseConnected;
                _web.onDisconnect -= RaiseDisconnected;
                _web.onData -= RaiseData;
                _web.onError -= RaiseError;
                _web.Disconnect();
                _web = null;
            }

            if (_udp != null)
            {
                _listener.PeerConnectedEvent -= OnPeerConnected;
                _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
                _listener.NetworkReceiveEvent -= OnPeerData;
                _udp.Stop();
                _udp = null;
            }

            _peer = null;
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (ReferenceEquals(peer, _peer))
                RaiseConnected();
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (ReferenceEquals(peer, _peer))
                RaiseDisconnected();
        }

        private void OnPeerData(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            if (ReferenceEquals(peer, _peer))
                RaiseData(reader.GetRemainingBytesSegment());
        }

        private void RaiseConnected()
        {
            if (!_closed)
                onConnected?.Invoke();
        }

        private void RaiseDisconnected()
        {
            if (!_closed)
                onDisconnected?.Invoke();
        }

        private void RaiseData(ArraySegment<byte> data)
        {
            if (!_closed)
                onData?.Invoke(data);
        }

        private void RaiseError(Exception exception)
        {
            if (!_closed)
                onError?.Invoke(exception);
        }
    }
}

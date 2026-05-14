using System;
using System.Buffers;
using System.Collections.Generic;

namespace PurrNet.Transports
{
    public sealed class PurrTransportLayer : ITransport, IDisposable
    {
        readonly ITransport _inner;

        readonly HashSet<int> _serverSideConnIds = new();
        int? _clientSideConnId;
        Connection? _loopbackConn;

        struct PendingPacket
        {
            public Connection conn;
            public byte[] buffer;
            public int length;
        }

        readonly Queue<PendingPacket> _deferredAsServer = new();
        readonly Queue<PendingPacket> _deferredAsClient = new();

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        public IReadOnlyList<Connection> connections => _inner.connections;

        public ConnectionState clientState => _inner.clientState;
        public ConnectionState listenerState => _inner.listenerState;

        public bool shouldServerSendKeepAlive => _inner.shouldServerSendKeepAlive;
        public bool shouldClientSendKeepAlive => _inner.shouldClientSendKeepAlive;

        /// <summary>Inner transport being wrapped. Exposed for diagnostics;
        /// PurrNet should not call it directly.</summary>
        public ITransport innerTransport => _inner;

        /// <summary>True when a host-loopback target has been resolved
        /// (same connection id seen as both server-side and client-side connect).</summary>
        public bool hasLoopback => _loopbackConn.HasValue;

        public PurrTransportLayer(ITransport inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _inner.onConnected += OnInnerConnected;
            _inner.onDisconnected += OnInnerDisconnected;
            _inner.onDataReceived += OnInnerDataReceived;
            _inner.onDataSent += OnInnerDataSent;
            _inner.onConnectionState += OnInnerConnectionState;
        }

        public void Dispose()
        {
            _inner.onConnected -= OnInnerConnected;
            _inner.onDisconnected -= OnInnerDisconnected;
            _inner.onDataReceived -= OnInnerDataReceived;
            _inner.onDataSent -= OnInnerDataSent;
            _inner.onConnectionState -= OnInnerConnectionState;
            DropDeferred(_deferredAsServer);
            DropDeferred(_deferredAsClient);
            _serverSideConnIds.Clear();
            _clientSideConnId = null;
            _loopbackConn = null;
        }

        static void DropDeferred(Queue<PendingPacket> queue)
        {
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (p.buffer is { Length: > 0 })
                    ArrayPool<byte>.Shared.Return(p.buffer);
            }
        }

        void OnInnerConnected(Connection conn, bool asServer)
        {
            if (asServer)
                _serverSideConnIds.Add(conn.connectionId);
            else
                _clientSideConnId = conn.connectionId;

            RecomputeLoopback();
            onConnected?.Invoke(conn, asServer);
        }

        void OnInnerDisconnected(Connection conn, DisconnectReason reason, bool asServer)
        {
            if (asServer)
                _serverSideConnIds.Remove(conn.connectionId);
            else if (_clientSideConnId == conn.connectionId)
                _clientSideConnId = null;

            RecomputeLoopback();
            onDisconnected?.Invoke(conn, reason, asServer);
        }

        void RecomputeLoopback()
        {
            if (_clientSideConnId.HasValue && _serverSideConnIds.Contains(_clientSideConnId.Value))
                _loopbackConn = new Connection(_clientSideConnId.Value);
            else
                _loopbackConn = null;
        }

        void OnInnerDataReceived(Connection conn, ByteData data, bool asServer) =>
            onDataReceived?.Invoke(conn, data, asServer);

        void OnInnerDataSent(Connection conn, ByteData data, bool asServer) =>
            onDataSent?.Invoke(conn, data, asServer);

        void OnInnerConnectionState(ConnectionState state, bool asServer) =>
            onConnectionState?.Invoke(state, asServer);

        public void Connect(string ip, ushort port) => _inner.Connect(ip, port);
        public void Disconnect() => _inner.Disconnect();
        public void Listen(ushort port) => _inner.Listen(port);
        public void StopListening() => _inner.StopListening();

        public bool SupportsChannel(Channel channel) => _inner.SupportsChannel(channel);

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            if (_loopbackConn.HasValue && target.connectionId == _loopbackConn.Value.connectionId)
                return int.MaxValue;
            return _inner.GetMTU(target, channel, asServer);
        }

        public void SendServerKeepAlive() => _inner.SendServerKeepAlive();

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) =>
            _inner.RaiseDataReceived(conn, data, asServer);

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer) =>
            _inner.RaiseDataSent(conn, data, asServer);

        public void CloseConnection(Connection conn) => _inner.CloseConnection(conn);

        public void UnityUpdate(float delta) => _inner.UnityUpdate(delta);

        static bool IsInlineChannel(Channel channel) =>
            channel == Channel.Unreliable || channel == Channel.UnreliableSequenced;

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_loopbackConn.HasValue && target.connectionId == _loopbackConn.Value.connectionId)
            {
                if (IsInlineChannel(method))
                {
                    onDataReceived?.Invoke(target, data, false);
                }
                else
                {
                    Enqueue(_deferredAsClient, target, data);
                }

                onDataSent?.Invoke(target, data, true);
                return;
            }

            _inner.SendToClient(target, data, method);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (_loopbackConn.HasValue)
            {
                var target = _loopbackConn.Value;

                if (IsInlineChannel(method))
                {
                    onDataReceived?.Invoke(target, data, true);
                }
                else
                {
                    Enqueue(_deferredAsServer, target, data);
                }

                onDataSent?.Invoke(default, data, false);
                return;
            }

            _inner.SendToServer(data, method);
        }

        static void Enqueue(Queue<PendingPacket> queue, Connection conn, ByteData data)
        {
            var buf = data.length > 0 ? ArrayPool<byte>.Shared.Rent(data.length) : Array.Empty<byte>();
            if (data.length > 0)
                Buffer.BlockCopy(data.data, data.offset, buf, 0, data.length);

            queue.Enqueue(new PendingPacket
            {
                conn = conn,
                buffer = buf,
                length = data.length
            });
        }

        public void ReceiveMessages(float delta)
        {
            DrainDeferred(_deferredAsServer, true);
            DrainDeferred(_deferredAsClient, false);
            _inner.ReceiveMessages(delta);
        }

        public void SendMessages(float delta) => _inner.SendMessages(delta);

        void DrainDeferred(Queue<PendingPacket> queue, bool asServerReceive)
        {
            int toProcess = queue.Count;
            while (toProcess-- > 0)
            {
                var p = queue.Dequeue();
                var byteData = new ByteData(p.buffer, 0, p.length);
                try
                {
                    onDataReceived?.Invoke(p.conn, byteData, asServerReceive);
                }
                finally
                {
                    if (p.buffer is { Length: > 0 })
                        ArrayPool<byte>.Shared.Return(p.buffer);
                }
            }
        }
    }
}

using System.Collections.Generic;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Transports
{
    [DefaultExecutionOrder(-100)]
    public class LocalTransport : GenericTransport, ITransport
    {
        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        static readonly List<Connection> connectedList = new List<Connection>() { new Connection(1) };
        static readonly List<Connection> disconnectedList = new List<Connection>();

        public IReadOnlyList<Connection> connections =>
            clientState == ConnectionState.Connected ? connectedList : disconnectedList;

        public override bool isSupported => true;
        public override ITransport transport => this;

        public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;

        public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;

        public bool SupportsChannel(Channel channel)
        {
            if (channel != Channel.ReliableOrdered)
                return false;
            return true;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return int.MaxValue;
        }

        public void Listen(ushort port)
        {
            listenerState = ConnectionState.Connecting;
            TriggerConnectionStateEvent(true);

            listenerState = ConnectionState.Connected;
            TriggerConnectionStateEvent(true);

            if (clientState == ConnectionState.Connecting)
            {
                clientState = ConnectionState.Connected;
                TriggerConnectionStateEvent(false);

                onConnected?.Invoke(new Connection(1), true);
                onConnected?.Invoke(new Connection(1), false);
            }
        }

        protected override void StartServerInternal()
        {
            Listen(default);
        }

        readonly Queue<BitPacker> _serverQueue = new Queue<BitPacker>();
        readonly Queue<BitPacker> _clientQueue = new Queue<BitPacker>();

        private void QueuePacket(ByteData data, bool asServer)
        {
            var datac = BitPackerPool.Get();
            datac.WriteBytes(data);

            if (asServer)
                _serverQueue.Enqueue(datac);
            else _clientQueue.Enqueue(datac);
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        public void StopListening()
        {
            listenerState = ConnectionState.Disconnecting;
            TriggerConnectionStateEvent(true);

            listenerState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(true);

            if (clientState == ConnectionState.Connected)
                Disconnect();
        }


        public void Connect(string ip, ushort port)
        {
            if (clientState == ConnectionState.Connected)
                return;

            clientState = ConnectionState.Connecting;
            TriggerConnectionStateEvent(false);

            if (listenerState == ConnectionState.Connected)
            {
                clientState = ConnectionState.Connected;
                TriggerConnectionStateEvent(false);

                onConnected?.Invoke(new Connection(1), true);
                onConnected?.Invoke(new Connection(1), false);
            }
        }

        protected override void StartClientInternal()
        {
            Connect(default, default);
        }

        public void Disconnect()
        {
            switch (clientState)
            {
                case ConnectionState.Connecting:
                    clientState = ConnectionState.Disconnected;
                    TriggerConnectionStateEvent(false);
                    return;
                case ConnectionState.Disconnected:
                    return;
            }

            clientState = ConnectionState.Disconnecting;
            TriggerConnectionStateEvent(false);

            clientState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(false);

            onDisconnected?.Invoke(new Connection(1), DisconnectReason.ServerRequest, true);
            onDisconnected?.Invoke(new Connection(1), DisconnectReason.ClientRequest, false);
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.Unreliable)
        {
            if (clientState != ConnectionState.Connected ||
                listenerState != ConnectionState.Connected)
                return;

            QueuePacket(data, false);
            // onDataReceived?.Invoke(default, data, false);
            RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.Unreliable)
        {
            if (clientState != ConnectionState.Connected ||
                listenerState != ConnectionState.Connected)
                return;

            QueuePacket(data, true);
            //onDataReceived?.Invoke(conn, data, true);
            RaiseDataSent(default, data, false);
        }

        public void CloseConnection(Connection conn)
        {
            StopClientInternal();
        }

        public void ReceiveMessages(float delta)
        {
            while (_serverQueue.Count > 0)
            {
                using var data = _serverQueue.Dequeue();
                onDataReceived?.Invoke(new Connection(1), data.ToByteData(), true);
            }

            while (_clientQueue.Count > 0)
            {
                using var data = _clientQueue.Dequeue();
                onDataReceived?.Invoke(default, data.ToByteData(), false);
            }
        }

        public void SendMessages(float delta) { }

        ConnectionState _prevClientState = ConnectionState.Disconnected;
        ConnectionState _prevServerState = ConnectionState.Disconnected;

        private void TriggerConnectionStateEvent(bool asServer)
        {
            if (asServer)
            {
                if (_prevServerState != listenerState)
                {
                    onConnectionState?.Invoke(listenerState, true);
                    _prevServerState = listenerState;
                }
            }
            else
            {
                if (_prevClientState != clientState)
                {
                    onConnectionState?.Invoke(clientState, false);
                    _prevClientState = clientState;
                }
            }
        }
    }
}

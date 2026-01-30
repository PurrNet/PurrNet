#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLEUTPWORKS
#endif

#if UTP_LOBBYRELAY
#define UTP_NET_PACKAGE
#endif

using System;
using PurrNet.Transports;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
using System.Collections.Generic;
using PurrNet.Logging;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Collections;
using Unity.Networking.Transport.Error;
#endif

namespace PurrNet.UTP
{
    public class UTPServer
    {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private NetworkPipeline _unreliablePipeline;

        private byte[] _buffer = new byte[1024];

        private readonly List<NetworkConnection> _connections = new List<NetworkConnection>();
        private readonly Dictionary<int, NetworkConnection> _connectionById = new Dictionary<int, NetworkConnection>();
        private readonly Dictionary<NetworkConnection, int> _idByConnection = new Dictionary<NetworkConnection, int>();
#endif

#pragma warning disable CS0067 // Event is never used
        public event Action<int> onRemoteConnected;
        public event Action<int> onRemoteDisconnected;
        public event Action<int, ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        public bool listening => _driver.IsCreated && _driver.Bound;
#else
        public bool listening => false;
#endif

        public void Listen(ushort port, bool dedicated = false, RelayServerData? relayData = null)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (relayData.HasValue)
            {
                var relayDataValue = relayData.Value;
                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayDataValue);
                _driver = NetworkDriver.Create(settings);
            }
            else
            {
                _driver = NetworkDriver.Create();
            }
            
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _unreliablePipeline = NetworkPipeline.Null;

            NetworkEndpoint endpoint;
            if (relayData.HasValue)
            {
                endpoint = relayData.Value.Endpoint;
            }
            else
            {
                endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            }

            if (_driver.Bind(endpoint) != 0)
            {
                PurrLogger.LogError("Failed to bind to endpoint");
                _driver.Dispose();
                return;
            }

            if (_driver.Listen() != 0)
            {
                PurrLogger.LogError("Failed to listen on endpoint");
                _driver.Dispose();
                return;
            }

            PostListen();
#endif
        }

        public void ListenP2P(bool dedicated = false, RelayServerData? relayData = null)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!relayData.HasValue)
            {
                PurrLogger.LogError("Relay data is required for P2P listen");
                return;
            }

            var relayDataValue = relayData.Value;
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayDataValue);
            _driver = NetworkDriver.Create(settings);
            
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _unreliablePipeline = NetworkPipeline.Null;

            if (_driver.Bind(relayData.Value.Endpoint) != 0)
            {
                PurrLogger.LogError("Failed to bind to relay endpoint");
                _driver.Dispose();
                return;
            }

            if (_driver.Listen() != 0)
            {
                PurrLogger.LogError("Failed to listen on relay endpoint");
                _driver.Dispose();
                return;
            }

            PostListen();
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void PostListen()
        {
            if (!_driver.IsCreated || !_driver.Bound)
            {
                PurrLogger.LogError("Failed to create listen socket.");
            }
        }
#endif

        public void SendMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (_driver.IsCreated)
                _driver.ScheduleUpdate().Complete();
#endif
        }

        public void ReceiveMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_driver.IsCreated)
                return;

            _driver.ScheduleUpdate().Complete();

            // Accept new connections
            NetworkConnection connection;
            while ((connection = _driver.Accept()) != default)
            {
                AddConnection(connection);
            }

            // Process events for existing connections
            for (var i = 0; i < _connections.Count; i++)
            {
                var conn = _connections[i];

                if (!_idByConnection.TryGetValue(conn, out var connId))
                    continue;

                NetworkEvent.Type cmd;
                while ((cmd = _driver.PopEventForConnection(conn, out var stream)) != NetworkEvent.Type.Empty)
                {
                    if (cmd == NetworkEvent.Type.Data)
                    {
                        int packetLength = stream.Length;
                        MakeSureBufferCanFit(packetLength);
                        
                        unsafe
                        {
                            fixed (byte* bufferPtr = _buffer)
                            {
                                var span = new Span<byte>(bufferPtr, packetLength);
                                stream.ReadBytes(span);
                            }
                        }
                        
                        var byteData = new ByteData(_buffer, 0, packetLength);
                        onDataReceived?.Invoke(connId, byteData);
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        RemoveConnection(conn);
                    }
                }
            }
#endif
        }

        public void Kick(int id)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_connectionById.TryGetValue(id, out var conn))
                return;

            _driver.Disconnect(conn);
            RemoveConnection(conn);
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void MakeSureBufferCanFit(int packetLength)
        {
            if (_buffer.Length < packetLength)
                Array.Resize(ref _buffer, packetLength);
        }
#endif
        public void SendToConnection(int connId, ByteData data, Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            MakeSureBufferCanFit(data.length);

            NetworkPipeline pipeline = channel switch {
                Channel.Unreliable => _unreliablePipeline,
                Channel.UnreliableSequenced => _unreliablePipeline,
                Channel.ReliableOrdered => _reliablePipeline,
                Channel.ReliableUnordered => _reliablePipeline,
                _ => NetworkPipeline.Null
            };

            try
            {
                var result = _driver.BeginSend(pipeline, conn, out var writer);
                if (result == (int)StatusCode.Success)
                {
                    unsafe
                    {
                        fixed (byte* dataPtr = &data.data[data.offset])
                        {
                            var span = new Span<byte>(dataPtr, data.length);
                            writer.WriteBytes(span);
                        }
                    }
                    _driver.EndSend(writer);
                }
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
#endif
        }


#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private int _nextConnectionId;

        private void AddConnection(NetworkConnection connection)
        {
            int id = _nextConnectionId++;
            _connections.Add(connection);
            _connectionById.Add(id, connection);
            _idByConnection.Add(connection, id);

            onRemoteConnected?.Invoke(id);
        }

        private void RemoveConnection(NetworkConnection connection)
        {
            if (_connections.Remove(connection) && _idByConnection.Remove(connection, out var _id))
            {
                _connectionById.Remove(_id);
                onRemoteDisconnected?.Invoke(_id);
            }
        }
#endif

        public void Stop()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_driver.IsCreated)
                return;

            for (var o = 0; o < _connections.Count; o++)
            {
                try
                {
                    var conn = _connections[o];
                    _driver.Disconnect(conn);
                }
                catch
                {
                    // ignored
                }
            }

            _connections.Clear();
            _connectionById.Clear();
            _idByConnection.Clear();

            try
            {
                _driver.Dispose();
            }
            catch
            {
                // ignored
            }
#endif
        }
    }
}
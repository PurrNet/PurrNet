#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID)
#define DISABLEUTPWORKS
#endif

using System;
using System.Collections.Generic;
using PurrNet.Transports;
using UnityEngine;
`#if` UTP_NET_PACKAGE && !DISABLEUTPWORKS
using PurrNet.Logging;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
#endif

#if UTP_NET_PACKAGE
using Unity.Networking.Transport.Relay;
#endif

namespace PurrNet.UTP
{
    /// <summary>
    /// Unity Transport Package (UTP) server implementation.
    /// Handles server-side network connectivity including listening for connections, managing multiple clients,
    /// data transmission, and support for Unity Relay-based peer-to-peer hosting.
    /// </summary>
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

        // Fragment reassembly
        private struct FragmentedMessage
        {
            public byte[][] fragments;
            public int[] fragmentSizes;
            public int receivedCount;
            public float creationTime;
        }

        private struct FragmentKey
        {
            public int connectionId;
            public uint fragmentId;

            public FragmentKey(int connId, uint fragId)
            {
                connectionId = connId;
                fragmentId = fragId;
            }

            public override bool Equals(object obj) => obj is FragmentKey key && key.connectionId == connectionId && key.fragmentId == fragmentId;
            public override int GetHashCode() => connectionId.GetHashCode() ^ fragmentId.GetHashCode();
        }

        private readonly Dictionary<FragmentKey, FragmentedMessage> _fragmentBuffer = new Dictionary<FragmentKey, FragmentedMessage>();
        private readonly Dictionary<int, uint> _nextFragmentIdByConnection = new Dictionary<int, uint>();
        private const float FRAGMENT_TIMEOUT = 30f;
        private const byte FRAGMENT_MAGIC = 0xFF;
        private const int FRAGMENT_HEADER_SIZE = 7; // 1 (magic) + 1 (count) + 1 (index) + 4 (id)
#endif

#pragma warning disable CS0067 // Event is never used
        /// <summary>
        /// Event raised when a remote client connects to the server.
        /// </summary>
        public event Action<int> onRemoteConnected;

        /// <summary>
        /// Event raised when a remote client disconnects from the server.
        /// </summary>
        public event Action<int> onRemoteDisconnected;

        /// <summary>
        /// Event raised when data is received from a connected client.
        /// </summary>
        public event Action<int, ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        /// <summary>
        /// Gets a value indicating whether the server is currently listening for connections.
        /// </summary>
        public bool listening => _driver.IsCreated && _driver.Bound;
#else
        /// <summary>
        /// Gets a value indicating whether the server is currently listening for connections.
        /// </summary>
        public bool listening => false;
#endif

#if UTP_NET_PACKAGE
        /// <summary>
        /// Starts listening for incoming client connections on the specified port.
        /// Can operate in direct connection mode or via Unity Relay if relay data is provided.
        /// </summary>
        /// <param name="port">The port number to listen on.</param>
        /// <param name="dedicated">Whether this is a dedicated server.</param>
        /// <param name="relayData">Optional Unity Relay server data for relay-based hosting.</param>
        public void Listen(ushort port, bool dedicated = false, RelayServerData? relayData = null)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"Listen requested port={port} dedicated={dedicated} relay={relayData.HasValue}");

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
                // When using Unity Relay, bind to 0.0.0.0:0 (AnyIpv4)
				endpoint = NetworkEndpoint.AnyIpv4;
            }
            else
            {
                endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            }

            if (_driver.Bind(endpoint) != 0)
            {
                // LogTransportTrace("Listen failed: bind failed");
                PurrLogger.LogError("Failed to bind to endpoint");
                _driver.Dispose();
                return;
            }

            if (_driver.Listen() != 0)
            {
                // LogTransportTrace("Listen failed: listen call failed");
                PurrLogger.LogError("Failed to listen on endpoint");
                _driver.Dispose();
                return;
            }

            PostListen();
            // LogTransportTrace("Listen succeeded");
#endif
        }

        /// <summary>
        /// Starts listening for peer-to-peer connections using Unity Relay.
        /// Requires relay data to establish the hosting endpoint.
        /// </summary>
        /// <param name="dedicated">Whether this is a dedicated server.</param>
        /// <param name="relayData">Unity Relay server data required for P2P hosting.</param>
        public void ListenP2P(bool dedicated = false, RelayServerData? relayData = null)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"ListenP2P requested dedicated={dedicated} relay={relayData.HasValue}");

            if (!relayData.HasValue)
            {
                // LogTransportTrace("ListenP2P failed: relay data missing");
                PurrLogger.LogError("Relay data is required for P2P listen");
                return;
            }

            var relayDataValue = relayData.Value;
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayDataValue);
            _driver = NetworkDriver.Create(settings);

            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _unreliablePipeline = NetworkPipeline.Null;

            // When using Unity Relay, bind to 0.0.0.0:0 (AnyIpv4) as required by Unity Transport
            if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                // LogTransportTrace("ListenP2P failed: bind failed");
                PurrLogger.LogError("Failed to bind to relay endpoint");
                _driver.Dispose();
                return;
            }

            if (_driver.Listen() != 0)
            {
                // LogTransportTrace("ListenP2P failed: listen call failed");
                PurrLogger.LogError("Failed to listen on relay endpoint");
                _driver.Dispose();
                return;
            }

            PostListen();
            // LogTransportTrace("ListenP2P succeeded");
#endif
        }
#endif

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void PostListen()
        {
            if (!_driver.IsCreated || !_driver.Bound)
            {
                // LogTransportTrace("PostListen failed: driver not created or not bound");
                PurrLogger.LogError("Failed to create listen socket.");
            }
            else
            {
                // LogTransportTrace("PostListen: driver ready and bound");
            }
        }
#endif

        public void SendMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // if (_driver.IsCreated)
            //    _driver.ScheduleUpdate().Complete();
            // Update is handled in ReceiveMessages
#endif
        }

        public void ReceiveMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_driver.IsCreated)
                return;

            _driver.ScheduleUpdate().Complete();

            CleanupExpiredFragments();

            // Accept new connections
            NetworkConnection connection;
            while ((connection = _driver.Accept()) != default)
            {
                // LogTransportTrace("ReceiveMessages accepted new connection");
                AddConnection(connection);
            }

            // Process events for existing connections
            for (var i = _connections.Count -1; i >= 0; i--)
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

                        // Check if this is a fragmented packet
                        if (packetLength > 0 && _buffer[0] == FRAGMENT_MAGIC)
                        {
                            ProcessFragment(connId, _buffer, packetLength);
                        }
                        else
                        {
                            var byteData = new ByteData(_buffer, 0, packetLength);
                            // LogTransportTrace($"Receive data conn={connId} len={packetLength}");
                            onDataReceived?.Invoke(connId, byteData);
                        }
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        // LogTransportTrace($"Receive disconnect event conn={connId}");
                        RemoveConnection(conn);
                    }
                }
            }
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void ProcessFragment(int connId, byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_HEADER_SIZE)
                return;

            byte totalFragments = packetData[1];
            byte fragmentIndex = packetData[2];
            uint fragmentId = (uint)packetData[3] | ((uint)packetData[4] << 8) | ((uint)packetData[5] << 16) | ((uint)packetData[6] << 24);
            int payloadSize = packetLength - FRAGMENT_HEADER_SIZE;

            var key = new FragmentKey(connId, fragmentId);

            if (!_fragmentBuffer.TryGetValue(key, out var message))
            {
                message = new FragmentedMessage
                {
                    fragments = new byte[totalFragments][],
                    fragmentSizes = new int[totalFragments],
                    receivedCount = 0,
                    creationTime = UnityEngine.Time.realtimeSinceStartup
                };
            }

            // Store fragment if not already received
            if (message.fragments[fragmentIndex] == null)
            {
                message.fragments[fragmentIndex] = new byte[payloadSize];
                Buffer.BlockCopy(packetData, FRAGMENT_HEADER_SIZE, message.fragments[fragmentIndex], 0, payloadSize);
                message.fragmentSizes[fragmentIndex] = payloadSize;
                message.receivedCount++;
            }

            _fragmentBuffer[key] = message;

            // If all fragments received, reassemble and pass to application
            if (message.receivedCount == totalFragments)
            {
                int totalSize = 0;
                for (int i = 0; i < totalFragments; i++)
                    totalSize += message.fragmentSizes[i];

                MakeSureBufferCanFit(totalSize);
                int offset = 0;
                for (int i = 0; i < totalFragments; i++)
                {
                    Buffer.BlockCopy(message.fragments[i], 0, _buffer, offset, message.fragmentSizes[i]);
                    offset += message.fragmentSizes[i];
                }

                var byteData = new ByteData(_buffer, 0, totalSize);
                onDataReceived?.Invoke(connId, byteData);

                _fragmentBuffer.Remove(key);
            }
        }

        private void CleanupExpiredFragments()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            var expiredKeys = new List<FragmentKey>();

            foreach (var kvp in _fragmentBuffer)
            {
                if (currentTime - kvp.Value.creationTime > FRAGMENT_TIMEOUT)
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
            {
                _fragmentBuffer.Remove(key);
                PurrLogger.LogWarning($"[UTP] Fragment assembly timeout for connection {key.connectionId} (Fragment ID: {key.fragmentId}). Incomplete fragments discarded.");
            }
        }
#endif

        /// <summary>
        /// Forcibly disconnects a client from the server.
        /// </summary>
        /// <param name="id">The connection ID of the client to disconnect.</param>
        public void Kick(int id)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"Kick requested conn={id}");

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
        /// <summary>
        /// Gets the Maximum Transmission Unit (MTU) size for the specified connection and channel.
        /// </summary>
        /// <param name="connId">The connection ID.</param>
        /// <param name="channel">The network channel.</param>
        /// <returns>The MTU size in bytes.</returns>
        public int GetMTU(int connId, Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            try
            {
                if (!_connectionById.TryGetValue(connId, out NetworkConnection connection))
                    return 1024; // Fallback if can't find connection.

                NetworkPipeline pipeline = channel switch {
                    Channel.Unreliable => _unreliablePipeline,
                    Channel.UnreliableSequenced => _unreliablePipeline,
                    Channel.ReliableOrdered => _reliablePipeline,
                    Channel.ReliableUnordered => _reliablePipeline,
                    _ => NetworkPipeline.Null
                };

                if (pipeline == NetworkPipeline.Null || !_driver.IsCreated)
                    return 1024;

                return _driver.GetMaxSupportedPayloadSize(connection, pipeline);
            }
            catch
            {
                return 1024;
            }
#else
            return 1024;
#endif
        }

        public void SendToConnection(int connId, ByteData data, Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"SendToConnection attempt conn={connId} len={data.length} channel={channel}");

            if (!_connectionById.TryGetValue(connId, out var conn))
            {
                // LogTransportTrace($"SendToConnection skipped: conn={connId} not found");
                return;
            }

            int mtu = GetMTU(connId, channel);
            int maxPayloadSize = mtu - FRAGMENT_HEADER_SIZE;

            // Warn if packet is larger than MTU (will be fragmented)
            if (data.length > mtu)
            {
                PurrLogger.LogWarning($"[UTP] Packet size ({data.length} bytes) exceeds MTU ({mtu} bytes) for connection {connId}. " +
                    $"Packet will be fragmented into {(int)Math.Ceiling(data.length / (float)maxPayloadSize)} fragments. " +
                    $"Consider splitting large packets in application code for better performance.");

                SendFragmented(connId, conn, data, channel, maxPayloadSize);
                return;
            }

            SendSinglePacketToConnection(conn, data, channel);
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void SendSinglePacketToConnection(NetworkConnection conn, ByteData data, Channel channel)
        {
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
                    // LogTransportTrace($"SendToConnection success conn={connId} len={data.length} channel={channel}");
                }
                else
                {
                    // LogTransportTrace($"SendToConnection failed conn={connId} status={(StatusCode)result}");
                    PurrLogger.LogError($"SendToConnection failed: {(StatusCode)result}");
                }
            }
            catch (Exception e)
            {
                // LogTransportTrace($"SendToConnection exception conn={connId}: {e.GetType().Name}: {e.Message}");
                PurrLogger.LogException(e);
            }
        }

        private void SendFragmented(int connId, NetworkConnection conn, ByteData data, Channel channel, int maxPayloadSize)
        {
            if (!_nextFragmentIdByConnection.TryGetValue(connId, out var nextId))
                nextId = 1;

            uint fragmentId = nextId++;
            _nextFragmentIdByConnection[connId] = nextId;

            int totalFragments = (int)Math.Ceiling(data.length / (float)maxPayloadSize);

            for (int i = 0; i < totalFragments; i++)
            {
                int offset = i * maxPayloadSize;
                int payloadSize = Math.Min(maxPayloadSize, data.length - offset);
                int packetSize = FRAGMENT_HEADER_SIZE + payloadSize;

                byte[] packet = new byte[packetSize];
                packet[0] = FRAGMENT_MAGIC;
                packet[1] = (byte)totalFragments;
                packet[2] = (byte)i;
                packet[3] = (byte)(fragmentId & 0xFF);
                packet[4] = (byte)((fragmentId >> 8) & 0xFF);
                packet[5] = (byte)((fragmentId >> 16) & 0xFF);
                packet[6] = (byte)((fragmentId >> 24) & 0xFF);

                Buffer.BlockCopy(data.data, data.offset + offset, packet, FRAGMENT_HEADER_SIZE, payloadSize);

                SendSinglePacketToConnection(conn, new ByteData(packet, 0, packetSize), channel);
            }
        }
#endif


#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private int _nextConnectionId;

        private void AddConnection(NetworkConnection connection)
        {
            int id = _nextConnectionId++;
            _connections.Add(connection);
            _connectionById.Add(id, connection);
            _idByConnection.Add(connection, id);
            _nextFragmentIdByConnection[id] = 1;

            // LogTransportTrace($"AddConnection conn={id} total={_connections.Count}");

            onRemoteConnected?.Invoke(id);
        }

        private void RemoveConnection(NetworkConnection connection)
        {
            if (_connections.Remove(connection) && _idByConnection.Remove(connection, out var _id))
            {
                _connectionById.Remove(_id);
                _nextFragmentIdByConnection.Remove(_id);

                // Clean up any pending fragments for this connection
                var keysToRemove = new List<FragmentKey>();
                foreach (var kvp in _fragmentBuffer)
                {
                    if (kvp.Key.connectionId == _id)
                        keysToRemove.Add(kvp.Key);
                }
                foreach (var key in keysToRemove)
                    _fragmentBuffer.Remove(key);

                // LogTransportTrace($"RemoveConnection conn={_id} total={_connections.Count}");
                onRemoteDisconnected?.Invoke(_id);
            }
        }
#endif

        /// <summary>
        /// Stops the server, disconnects all clients, and releases all resources.
        /// </summary>
        public void Stop()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"Stop requested activeConnections={_connections.Count}");

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

            // LogTransportTrace("Stop completed");
#endif
        }

        private void LogTransportTrace(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PurrLogger.Log($"[TransportTrace][UTPServer] {message}");
#endif
        }
    }
}

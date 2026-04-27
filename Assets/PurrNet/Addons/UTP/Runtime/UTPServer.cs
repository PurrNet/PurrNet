#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID)
#define DISABLEUTPWORKS
#endif

using System;
using PurrNet.Transports;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
using System.Collections.Generic;
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

        private readonly struct FragmentKey : IEquatable<FragmentKey>
        {
            public readonly int connectionId;
            public readonly uint fragmentId;

            public FragmentKey(int connId, uint fragId)
            {
                connectionId = connId;
                fragmentId = fragId;
            }

            public override bool Equals(object obj) => obj is FragmentKey key && key.connectionId == connectionId && key.fragmentId == fragmentId;
            public override int GetHashCode() => connectionId.GetHashCode() ^ fragmentId.GetHashCode();

            public bool Equals(FragmentKey other)
            {
                return connectionId == other.connectionId && fragmentId == other.fragmentId;
            }
        }

        private readonly Dictionary<FragmentKey, FragmentedMessage> _fragmentBuffer = new Dictionary<FragmentKey, FragmentedMessage>();
        private readonly Dictionary<int, uint> _nextFragmentIdByConnection = new Dictionary<int, uint>();
        private const float FRAGMENT_TIMEOUT = 60f;
        private const float OUTBOUND_FRAGMENT_TIMEOUT = 60f;
        private const byte FRAGMENT_MAGIC = 0xFF;
        private const byte FRAGMENT_TYPE_DATA = 0;
        private const byte FRAGMENT_TYPE_NACK = 1;
        private const byte FRAGMENT_TYPE_ACK = 2;
        private const int FRAGMENT_CONTROL_HEADER_SIZE = 6; // 1 (magic) + 1 (type) + 4 (id)
        private const int FRAGMENT_NACK_HEADER_SIZE = 7; // control header + 1 missing-count byte
        private const int FRAGMENT_DATA_HEADER_SIZE = 8; // control header + 1 (count) + 1 (index)
        private const int FRAGMENT_LEGACY_HEADER_SIZE = 7; // 1 (magic) + 1 (count) + 1 (index) + 4 (id)
        private const int MAX_FRAGMENTS_PER_PACKET = 255; // uint8 limit for fragment count
        private const int MAX_FRAGMENT_SENDS_PER_UPDATE = 16;
		private const float FRAGMENT_RESEND_COOLDOWN = 0.05f;

        private class OutboundFragmentedMessage
        {
            public byte totalFragments;
            public byte[][] packets;
            public Channel channel;
            public float creationTime;
            public float lastResendTime;
        }

        private readonly Dictionary<FragmentKey, OutboundFragmentedMessage> _outboundFragmentBuffer = new Dictionary<FragmentKey, OutboundFragmentedMessage>();

        private struct PendingFragmentSend
        {
            public int connectionId;
            public NetworkConnection connection;
            public ByteData data;
            public Channel channel;

            public PendingFragmentSend(int connectionId, NetworkConnection connection, ByteData data, Channel channel)
            {
                this.connectionId = connectionId;
                this.connection = connection;
                this.data = data;
                this.channel = channel;
            }
        }

        private readonly Queue<PendingFragmentSend> _pendingFragmentSends = new Queue<PendingFragmentSend>();
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
            FlushPendingFragmentSends();
#endif
        }

        public void ReceiveMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_driver.IsCreated)
                return;

            _driver.ScheduleUpdate().Complete();
            FlushPendingFragmentSends();

            CleanupExpiredFragments();
            CleanupExpiredOutboundFragments();

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
                            ProcessFragmentPacket(connId, _buffer, packetLength);
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
        private void ProcessFragmentPacket(int connId, byte[] packetData, int packetLength)
        {
            if (packetLength < 2)
                return;

            byte fragmentType = packetData[1];
            if (fragmentType == FRAGMENT_TYPE_DATA)
            {
                ProcessFragmentData(connId, packetData, packetLength);
                return;
            }

            if (fragmentType == FRAGMENT_TYPE_NACK)
            {
                ProcessFragmentResendRequest(connId, packetData, packetLength);
                return;
            }

            if (fragmentType == FRAGMENT_TYPE_ACK)
            {
                ProcessFragmentAck(connId, packetData, packetLength);
                return;
            }

            if (packetLength >= FRAGMENT_LEGACY_HEADER_SIZE)
            {
                // Compatibility path for old header format from older peers.
                ProcessLegacyFragmentData(connId, packetData, packetLength);
            }
        }

        private void ProcessFragmentData(int connId, byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_DATA_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            byte totalFragments = packetData[6];
            byte fragmentIndex = packetData[7];
            int payloadSize = packetLength - FRAGMENT_DATA_HEADER_SIZE;

            if (totalFragments == 0 || fragmentIndex >= totalFragments)
                return;

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
			else if (message.fragments.Length != totalFragments)
			{
				return;
			}

            // Store fragment if not already received
            if (message.fragments[fragmentIndex] == null)
            {
                message.fragments[fragmentIndex] = new byte[payloadSize];
                Buffer.BlockCopy(packetData, FRAGMENT_DATA_HEADER_SIZE, message.fragments[fragmentIndex], 0, payloadSize);
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

                SendFragmentAck(connId, fragmentId);

                _fragmentBuffer.Remove(key);
            }
        }

        private void ProcessLegacyFragmentData(int connId, byte[] packetData, int packetLength)
        {
            byte totalFragments = packetData[1];
            byte fragmentIndex = packetData[2];
            uint fragmentId = packetData[3] | ((uint)packetData[4] << 8) | ((uint)packetData[5] << 16) | ((uint)packetData[6] << 24);
            int payloadSize = packetLength - FRAGMENT_LEGACY_HEADER_SIZE;

            if (totalFragments == 0 || fragmentIndex >= totalFragments)
                return;

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
			else if (message.fragments.Length != totalFragments || message.fragmentSizes.Length != totalFragments)
			{
				return;
			}

            if (message.fragments[fragmentIndex] == null)
            {
                message.fragments[fragmentIndex] = new byte[payloadSize];
                Buffer.BlockCopy(packetData, FRAGMENT_LEGACY_HEADER_SIZE, message.fragments[fragmentIndex], 0, payloadSize);
                message.fragmentSizes[fragmentIndex] = payloadSize;
                message.receivedCount++;
            }

            _fragmentBuffer[key] = message;

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

                SendFragmentAck(connId, fragmentId);

                _fragmentBuffer.Remove(key);
            }
        }

        private void ProcessFragmentResendRequest(int connId, byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_NACK_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            int missingCount = packetData[6];

            if (missingCount <= 0)
                return;

            if (packetLength < FRAGMENT_NACK_HEADER_SIZE + missingCount)
                return;

            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            var key = new FragmentKey(connId, fragmentId);
            if (!_outboundFragmentBuffer.TryGetValue(key, out var sentMessage))
                return;

			float now = UnityEngine.Time.realtimeSinceStartup;
			if (sentMessage.lastResendTime > 0f && now - sentMessage.lastResendTime < FRAGMENT_RESEND_COOLDOWN)
				return;

            int enqueuedCount = 0;
            int resendLimit = Math.Min(missingCount, MAX_FRAGMENT_SENDS_PER_UPDATE);
            for (int i = 0; i < resendLimit; i++)
            {
                byte fragmentIndex = packetData[FRAGMENT_NACK_HEADER_SIZE + i];
                if (fragmentIndex >= sentMessage.totalFragments)
                    continue;

                byte[] packet = sentMessage.packets[fragmentIndex];
                if (packet == null)
                    continue;

                _pendingFragmentSends.Enqueue(new PendingFragmentSend(connId, conn, new ByteData(packet, 0, packet.Length), sentMessage.channel));
                enqueuedCount++;
            }

            if (enqueuedCount > 0)
            {
                sentMessage.lastResendTime = now;
                _outboundFragmentBuffer[key] = sentMessage;
                FlushPendingFragmentSends();
            }
        }

        private void ProcessFragmentAck(int connId, byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_CONTROL_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            var key = new FragmentKey(connId, fragmentId);
            _outboundFragmentBuffer.Remove(key);
        }

        private void CleanupExpiredFragments()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            var expiredKeys = new List<FragmentKey>();

            foreach (var kvp in _fragmentBuffer)
            {
                if (currentTime - kvp.Value.creationTime > FRAGMENT_TIMEOUT)
                {
                    expiredKeys.Add(kvp.Key);
                    int receivedCount = kvp.Value.receivedCount;
                    int totalCount = kvp.Value.fragments.Length;
                    PurrLogger.LogWarning($"[UTP] Fragment assembly timeout for connection {kvp.Key.connectionId} (Fragment ID: {kvp.Key.fragmentId}). Received {receivedCount}/{totalCount} fragments. Incomplete fragments discarded.");
                }
            }

            foreach (var key in expiredKeys)
            {
                _fragmentBuffer.Remove(key);
            }
        }

        private void CleanupExpiredOutboundFragments()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            var expiredKeys = new List<FragmentKey>();

            foreach (var kvp in _outboundFragmentBuffer)
            {
                if (currentTime - kvp.Value.creationTime > OUTBOUND_FRAGMENT_TIMEOUT)
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
            {
                _outboundFragmentBuffer.Remove(key);
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
            int maxPayloadSize = mtu - FRAGMENT_DATA_HEADER_SIZE;

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

            // Validate fragment count doesn't exceed uint8 max
            if (totalFragments > MAX_FRAGMENTS_PER_PACKET)
            {
                PurrLogger.LogError($"[UTP] Packet too large to fragment ({data.length} bytes would require {totalFragments} fragments, max {MAX_FRAGMENTS_PER_PACKET}). Dropping packet.");
                return;
            }

            byte[][] sentPackets = new byte[totalFragments][];

            for (int i = 0; i < totalFragments; i++)
            {
                int offset = i * maxPayloadSize;
                int payloadSize = Math.Min(maxPayloadSize, data.length - offset);
                int packetSize = FRAGMENT_DATA_HEADER_SIZE + payloadSize;

                byte[] packet = new byte[packetSize];
                packet[0] = FRAGMENT_MAGIC;
                packet[1] = FRAGMENT_TYPE_DATA;
                packet[2] = (byte)(fragmentId & 0xFF);
                packet[3] = (byte)((fragmentId >> 8) & 0xFF);
                packet[4] = (byte)((fragmentId >> 16) & 0xFF);
                packet[5] = (byte)((fragmentId >> 24) & 0xFF);
                packet[6] = (byte)totalFragments;
                packet[7] = (byte)i;

                Buffer.BlockCopy(data.data, data.offset + offset, packet, FRAGMENT_DATA_HEADER_SIZE, payloadSize);
                sentPackets[i] = packet;

                _pendingFragmentSends.Enqueue(new PendingFragmentSend(connId, conn, new ByteData(packet, 0, packetSize), channel));
            }

            var key = new FragmentKey(connId, fragmentId);
            _outboundFragmentBuffer[key] = new OutboundFragmentedMessage
            {
                totalFragments = (byte)totalFragments,
                packets = sentPackets,
                channel = channel,
                creationTime = UnityEngine.Time.realtimeSinceStartup,
                lastResendTime = 0f
            };

            FlushPendingFragmentSends();
        }

        private bool SendSinglePacketToConnectionWithValidation(NetworkConnection conn, ByteData data, Channel channel)
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
                int beginResult = _driver.BeginSend(pipeline, conn, out var writer);
                if (beginResult == (int)StatusCode.Success)
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
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private void SendFragmentAck(int connId, uint fragmentId)
        {
            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            byte[] packet = new byte[FRAGMENT_CONTROL_HEADER_SIZE];
            packet[0] = FRAGMENT_MAGIC;
            packet[1] = FRAGMENT_TYPE_ACK;
            packet[2] = (byte)(fragmentId & 0xFF);
            packet[3] = (byte)((fragmentId >> 8) & 0xFF);
            packet[4] = (byte)((fragmentId >> 16) & 0xFF);
            packet[5] = (byte)((fragmentId >> 24) & 0xFF);

            SendSinglePacketToConnectionWithValidation(conn, new ByteData(packet, 0, packet.Length), Channel.ReliableOrdered);
        }

        private void FlushPendingFragmentSends()
        {
            if (!_driver.IsCreated)
                return;

            int sends = 0;
            while (sends < MAX_FRAGMENT_SENDS_PER_UPDATE && _pendingFragmentSends.Count > 0)
            {
                var pending = _pendingFragmentSends.Peek();

                if (!_connectionById.TryGetValue(pending.connectionId, out var activeConn) || activeConn != pending.connection)
                {
                    _pendingFragmentSends.Dequeue();
                    continue;
                }

                if (!SendSinglePacketToConnectionWithValidation(pending.connection, pending.data, pending.channel))
                    break;

                _pendingFragmentSends.Dequeue();
                sends++;
            }
        }

        private void RemovePendingFragmentsForConnection(int connId)
        {
            if (_pendingFragmentSends.Count == 0)
                return;

            var remaining = new Queue<PendingFragmentSend>();
            while (_pendingFragmentSends.Count > 0)
            {
                var pending = _pendingFragmentSends.Dequeue();
                if (pending.connectionId != connId)
                    remaining.Enqueue(pending);
            }

            while (remaining.Count > 0)
                _pendingFragmentSends.Enqueue(remaining.Dequeue());
        }

        private void ClearPendingFragments()
        {
            _pendingFragmentSends.Clear();
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
                RemovePendingFragmentsForConnection(_id);

                var outboundKeysToRemove = new List<FragmentKey>();
                foreach (var kvp in _outboundFragmentBuffer)
                {
                    if (kvp.Key.connectionId == _id)
                        outboundKeysToRemove.Add(kvp.Key);
                }
                foreach (var key in outboundKeysToRemove)
                    _outboundFragmentBuffer.Remove(key);

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
            ClearPendingFragments();
            _fragmentBuffer.Clear();
            _nextFragmentIdByConnection.Clear();
            _outboundFragmentBuffer.Clear();

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
    }
}

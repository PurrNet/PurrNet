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
using System.Collections;
#endif

namespace PurrNet.UTP
{
    /// <summary>
    /// Unity Transport Package (UTP) client implementation.
    /// Handles client-side network connectivity including connection management, data transmission,
    /// and support for Unity Relay-based peer-to-peer connections.
    /// </summary>
    public class UTPClient
    {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private NetworkDriver _driver;
        private NetworkConnection _connection;
        private NetworkPipeline _reliablePipeline;
        private NetworkPipeline _unreliablePipeline;

        private byte[] _buffer = new byte[1024];

        // Fragment reassembly
        private struct FragmentedMessage
        {
            public byte[][] fragments;
            public int[] fragmentSizes;
            public int receivedCount;
            public float creationTime;
            public float lastNackTime;
        }

        private readonly Dictionary<uint, FragmentedMessage> _fragmentBuffer = new Dictionary<uint, FragmentedMessage>();
        private uint _nextFragmentId = 1;
        private const float FRAGMENT_TIMEOUT = 60f;
        private const float OUTBOUND_FRAGMENT_TIMEOUT = 60f;
        private const float FRAGMENT_REQUEST_INTERVAL = 0.25f;
        private const int MAX_MISSING_INDICES_PER_REQUEST = 64;
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
            public float[] fragmentLastResendTimes;
            public Channel channel;
            public float creationTime;
        }

        private readonly Dictionary<uint, OutboundFragmentedMessage> _outboundFragmentBuffer = new Dictionary<uint, OutboundFragmentedMessage>();

        private struct PendingFragmentSend
        {
            public ByteData data;
            public Channel channel;

            public PendingFragmentSend(ByteData data, Channel channel)
            {
                this.data = data;
                this.channel = channel;
            }
        }

        private readonly Queue<PendingFragmentSend> _pendingFragmentSends = new Queue<PendingFragmentSend>();
#endif

#pragma warning disable CS0067 // Event is never used
        /// <summary>
        /// Event raised when data is received from the server.
        /// </summary>
        public event Action<ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used

        /// <summary>
        /// Event raised when the connection state changes.
        /// </summary>
        public event Action<ConnectionState> onConnectionState;

        private ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>
        /// Gets or sets the current connection state of the client.
        /// </summary>
        public ConnectionState connectionState
        {
            get => _state;
            set
            {
                if (_state == value)
                    return;

                _state = value;
                onConnectionState?.Invoke(_state);
            }
        }
#if UTP_NET_PACKAGE
        /// <summary>
        /// Connects to a server using a direct IP address and port, or via Unity Relay if relay data is provided.
        /// </summary>
        /// <param name="address">The IP address or hostname of the server.</param>
        /// <param name="port">The port number to connect to.</param>
        /// <param name="dedicated">Whether connecting to a dedicated server.</param>
        /// <param name="relayData">Optional Unity Relay server data for relay-based connections.</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        public IEnumerator Connect(string address, ushort port, bool dedicated = false, RelayServerData? relayData = null)
        {
            yield return null;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS

            if (relayData.HasValue)
            {
                RelayServerData relayDataValue = relayData.Value;
                NetworkSettings settings = new NetworkSettings();
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
                if (!NetworkEndpoint.TryParse(address, port, out endpoint))
                {
                    PurrLogger.LogError($"Failed to parse address: {address}:{port}");
                    connectionState = ConnectionState.Disconnected;
					if (_driver.IsCreated)
						_driver.Dispose();
                    yield break;
                }
            }

            _connection = _driver.Connect(endpoint);

            PostConnect();
#endif
        }

        /// <summary>
        /// Connects to a peer-to-peer session using Unity Relay.
        /// Requires relay data to establish the connection.
        /// </summary>
        /// <param name="lobbyId">The lobby ID for the P2P session.</param>
        /// <param name="dedicated">Whether connecting to a dedicated server.</param>
        /// <param name="relayData">Unity Relay server data required for P2P connections.</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        public IEnumerator ConnectP2P(string lobbyId, bool dedicated = false, RelayServerData? relayData = null)
        {
            yield return null;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS

            if (!relayData.HasValue)
            {
                PurrLogger.LogError("Relay data is required for P2P connection");
                yield break;
            }

            RelayServerData relayDataValue = relayData.Value;
            NetworkSettings settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayDataValue);
            _driver = NetworkDriver.Create(settings);

            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _unreliablePipeline = NetworkPipeline.Null;

            _connection = _driver.Connect(relayData.Value.Endpoint);

            PostConnect();
#endif
        }

#endif

        /// <summary>
        /// Gets the Maximum Transmission Unit (MTU) size for the specified channel.
        /// </summary>
        /// <param name="channel">The network channel.</param>
        /// <returns>The MTU size in bytes.</returns>
        public int GetMTU(Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            try
            {
                if (!_connection.IsCreated)
                    return 1024; // Fallback MTU size if connection is not established

                NetworkPipeline pipeline = channel switch {
                    Channel.Unreliable => _unreliablePipeline,
                    Channel.UnreliableSequenced => _unreliablePipeline,
                    Channel.ReliableOrdered => _reliablePipeline,
                    Channel.ReliableUnordered => _reliablePipeline,
                    _ => NetworkPipeline.Null
                };

                if (pipeline == NetworkPipeline.Null || !_driver.IsCreated)
                    return 1024;

                return _driver.GetMaxSupportedPayloadSize(_connection, pipeline);
            }
            catch
            {
                return 1024;
            }
#else
            return 1024;
#endif
        }

        /// <summary>
        /// Sends data to the server using the specified network channel.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="channel">The network channel to use for transmission.</param>
        public void Send(ByteData data, Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            // LogTransportTrace($"Send attempt len={data.length} channel={channel} state={connectionState}");

            if (!_connection.IsCreated || _driver.GetConnectionState(_connection) != NetworkConnection.State.Connected)
            {
                return;
            }

            int mtu = GetMTU(channel);
            int maxPayloadSize = mtu - FRAGMENT_DATA_HEADER_SIZE;

            // Warn if packet is larger than MTU (will be fragmented)
            if (data.length > mtu)
            {
                PurrLogger.LogWarning($"[UTP] Packet size ({data.length} bytes) exceeds MTU ({mtu} bytes). " +
                    $"Packet will be fragmented into {(int)Math.Ceiling(data.length / (float)maxPayloadSize)} fragments. " +
                    $"Consider splitting large packets in application code for better performance.");

                SendFragmented(data, channel, maxPayloadSize);
                return;
            }

            // If there are pending fragments queued and this is an ordered channel, enqueue this packet
            // to preserve ordering instead of sending it immediately
            if (_pendingFragmentSends.Count > 0 && (channel == Channel.ReliableOrdered || channel == Channel.UnreliableSequenced))
            {
                byte[] packet = new byte[data.length];
                Buffer.BlockCopy(data.data, data.offset, packet, 0, data.length);
                _pendingFragmentSends.Enqueue(new PendingFragmentSend(new ByteData(packet, 0, packet.Length), channel));
                return;
            }

            SendSinglePacket(data, channel);
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void SendSinglePacket(ByteData data, Channel channel)
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
                int beginResult = _driver.BeginSend(pipeline, _connection, out var writer);
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
                    // LogTransportTrace($"Send success len={data.length} channel={channel}");
                }
                else
                {
                    // LogTransportTrace($"Send failed beginResult={(StatusCode)beginResult}");
                    PurrLogger.LogError($"Failed to begin send: {(StatusCode)beginResult}");
                }
            }
            catch (Exception e)
            {
                // LogTransportTrace($"Send exception: {e.GetType().Name}: {e.Message}");
                PurrLogger.LogException(e);
            }
        }

        private void SendFragmented(ByteData data, Channel channel, int maxPayloadSize)
        {
            uint fragmentId = _nextFragmentId++;
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

                _pendingFragmentSends.Enqueue(new PendingFragmentSend(new ByteData(packet, 0, packetSize), channel));
            }

            _outboundFragmentBuffer[fragmentId] = new OutboundFragmentedMessage
            {
                totalFragments = (byte)totalFragments,
                packets = sentPackets,
                fragmentLastResendTimes = new float[totalFragments],
                channel = channel,
                creationTime = UnityEngine.Time.realtimeSinceStartup,
            };

            FlushPendingFragmentSends();
        }

        private bool SendSinglePacketWithValidation(ByteData data, Channel channel)
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
                int beginResult = _driver.BeginSend(pipeline, _connection, out var writer);
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

        private void FlushPendingFragmentSends()
        {
            if (!_driver.IsCreated || !_connection.IsCreated)
                return;

            if (_driver.GetConnectionState(_connection) != NetworkConnection.State.Connected)
                return;

            int sends = 0;
            while (sends < MAX_FRAGMENT_SENDS_PER_UPDATE && _pendingFragmentSends.Count > 0)
            {
                var pending = _pendingFragmentSends.Peek();
                if (!SendSinglePacketWithValidation(pending.data, pending.channel))
                    break;

                _pendingFragmentSends.Dequeue();
                sends++;
            }
        }

        private void ClearPendingFragments()
        {
            _pendingFragmentSends.Clear();
            _fragmentBuffer.Clear();
            _outboundFragmentBuffer.Clear();
        }
#endif

        /// <summary>
        /// Flushes outgoing network messages to the server.
        /// Should be called regularly to ensure timely message delivery.
        /// </summary>
        public void SendMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            FlushPendingFragmentSends();
#endif
        }

        /// <summary>
        /// Processes incoming network messages from the server.
        /// Should be called regularly (typically each frame) to handle connection events and data reception.
        /// </summary>
        public void ReceiveMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_driver.IsCreated)
                return;

            _driver.ScheduleUpdate().Complete();
            FlushPendingFragmentSends();

            CleanupExpiredFragments();
            CleanupExpiredOutboundFragments();

            NetworkEvent.Type cmd;
            while ((cmd = _driver.PopEventForConnection(_connection, out var stream)) != NetworkEvent.Type.Empty)
            {
                switch (cmd)
                {
                    case NetworkEvent.Type.Data:
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
                            ProcessFragmentPacket(_buffer, packetLength);
                        }
                        else
                        {
                            ByteData byteData = new ByteData(_buffer, 0, packetLength);
                            // LogTransportTrace($"Receive data len={packetLength}");
                            onDataReceived?.Invoke(byteData);
                        }
                        break;
                    }
                    case NetworkEvent.Type.Connect:
                        // LogTransportTrace("Receive connect event");
                        connectionState = ConnectionState.Connected;
                        break;
                    case NetworkEvent.Type.Disconnect:
                        // LogTransportTrace("Receive disconnect event");
                        ClearPendingFragments();
                        connectionState = ConnectionState.Disconnecting;
                        connectionState = ConnectionState.Disconnected;
                        break;
                    case NetworkEvent.Type.Empty:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
#endif
        }

#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
        private void ProcessFragmentPacket(byte[] packetData, int packetLength)
        {
            if (packetData == null)
                return;

            if (packetLength < 2 || packetLength > packetData.Length)
                return;

            byte fragmentType = packetData[1];
            if (fragmentType == FRAGMENT_TYPE_DATA)
            {
                ProcessFragmentData(packetData, packetLength);
                return;
            }

            if (fragmentType == FRAGMENT_TYPE_NACK)
            {
                ProcessFragmentResendRequest(packetData, packetLength);
                return;
            }

            if (fragmentType == FRAGMENT_TYPE_ACK)
            {
                ProcessFragmentAck(packetData, packetLength);
                return;
            }

            if (packetLength >= FRAGMENT_LEGACY_HEADER_SIZE)
            {
                // Compatibility path for old header format from older peers.
                ProcessLegacyFragmentData(packetData, packetLength);
            }
        }

        private void ProcessFragmentData(byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_DATA_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            byte totalFragments = packetData[6];
            byte fragmentIndex = packetData[7];
            int payloadSize = packetLength - FRAGMENT_DATA_HEADER_SIZE;

            if (totalFragments == 0 || fragmentIndex >= totalFragments)
                return;

            if (!_fragmentBuffer.TryGetValue(fragmentId, out var message))
            {
                message = new FragmentedMessage
                {
                    fragments = new byte[totalFragments][],
                    fragmentSizes = new int[totalFragments],
                    receivedCount = 0,
                    creationTime = UnityEngine.Time.realtimeSinceStartup,
                    lastNackTime = UnityEngine.Time.realtimeSinceStartup
                };
            }
            else if (message.fragments.Length != totalFragments)
            {
                // FragmentId reused with different totalFragments; rebuild the FragmentedMessage
                message = new FragmentedMessage
                {
                    fragments = new byte[totalFragments][],
                    fragmentSizes = new int[totalFragments],
                    receivedCount = 0,
                    creationTime = UnityEngine.Time.realtimeSinceStartup,
                    lastNackTime = UnityEngine.Time.realtimeSinceStartup
                };
            }

            int fragmentIdx = fragmentIndex;
            if (fragmentIdx >= message.fragments.Length || fragmentIdx >= message.fragmentSizes.Length)
                return;

            // Store fragment if not already received
            if (message.fragments[fragmentIdx] == null)
            {
                message.fragments[fragmentIdx] = new byte[payloadSize];
                Buffer.BlockCopy(packetData, FRAGMENT_DATA_HEADER_SIZE, message.fragments[fragmentIdx], 0, payloadSize);
                message.fragmentSizes[fragmentIdx] = payloadSize;
                message.receivedCount++;
            }

            _fragmentBuffer[fragmentId] = message;

            if (message.receivedCount < totalFragments)
            {
                RequestMissingFragments(fragmentId, totalFragments, ref message);
                _fragmentBuffer[fragmentId] = message;
            }

            // If all fragments received, reassemble and pass to application
            if (message.receivedCount == totalFragments)
            {
                int totalSize = 0;
                for (int i = 0; i < totalFragments; i++)
                {
                    if (message.fragments[i] == null)
                        return;

                    totalSize += message.fragmentSizes[i];
                }

                MakeSureBufferCanFit(totalSize);
                int offset = 0;
                for (int i = 0; i < totalFragments; i++)
                {
                    Buffer.BlockCopy(message.fragments[i], 0, _buffer, offset, message.fragmentSizes[i]);
                    offset += message.fragmentSizes[i];
                }

                ByteData byteData = new ByteData(_buffer, 0, totalSize);
                onDataReceived?.Invoke(byteData);

                SendFragmentAck(fragmentId);

                _fragmentBuffer.Remove(fragmentId);
            }
        }

        private void ProcessLegacyFragmentData(byte[] packetData, int packetLength)
        {
            byte totalFragments = packetData[1];
            byte fragmentIndex = packetData[2];
            uint fragmentId = packetData[3] | ((uint)packetData[4] << 8) | ((uint)packetData[5] << 16) | ((uint)packetData[6] << 24);
            int payloadSize = packetLength - FRAGMENT_LEGACY_HEADER_SIZE;

            if (totalFragments == 0 || fragmentIndex >= totalFragments)
                return;

            if (!_fragmentBuffer.TryGetValue(fragmentId, out var message))
            {
                message = new FragmentedMessage
                {
                    fragments = new byte[totalFragments][],
                    fragmentSizes = new int[totalFragments],
                    receivedCount = 0,
                    creationTime = UnityEngine.Time.realtimeSinceStartup,
                    lastNackTime = UnityEngine.Time.realtimeSinceStartup
                };
            }
            else if (message.fragments.Length != totalFragments)
            {
                // FragmentId reused with different totalFragments; rebuild the FragmentedMessage
                message = new FragmentedMessage
                {
                    fragments = new byte[totalFragments][],
                    fragmentSizes = new int[totalFragments],
                    receivedCount = 0,
                    creationTime = UnityEngine.Time.realtimeSinceStartup,
                    lastNackTime = UnityEngine.Time.realtimeSinceStartup
                };
            }

            int fragmentIdx = fragmentIndex;
            if (fragmentIdx >= message.fragments.Length || fragmentIdx >= message.fragmentSizes.Length)
                return;

            if (message.fragments[fragmentIdx] == null)
            {
                message.fragments[fragmentIdx] = new byte[payloadSize];
                Buffer.BlockCopy(packetData, FRAGMENT_LEGACY_HEADER_SIZE, message.fragments[fragmentIdx], 0, payloadSize);
                message.fragmentSizes[fragmentIdx] = payloadSize;
                message.receivedCount++;
            }

            _fragmentBuffer[fragmentId] = message;

            if (message.receivedCount == totalFragments)
            {
                int totalSize = 0;
                for (int i = 0; i < totalFragments; i++)
                {
                    if (message.fragments[i] == null)
                        return;

                    totalSize += message.fragmentSizes[i];
                }

                MakeSureBufferCanFit(totalSize);
                int offset = 0;
                for (int i = 0; i < totalFragments; i++)
                {
                    Buffer.BlockCopy(message.fragments[i], 0, _buffer, offset, message.fragmentSizes[i]);
                    offset += message.fragmentSizes[i];
                }

                ByteData byteData = new ByteData(_buffer, 0, totalSize);
                onDataReceived?.Invoke(byteData);
                _fragmentBuffer.Remove(fragmentId);
            }
        }

        private void ProcessFragmentResendRequest(byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_NACK_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            int missingCount = packetData[6];

            if (missingCount <= 0)
                return;

            if (packetLength < FRAGMENT_NACK_HEADER_SIZE + missingCount)
                return;

            if (!_outboundFragmentBuffer.TryGetValue(fragmentId, out var sentMessage))
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;

            int enqueuedCount = 0;
            int resendLimit = Math.Min(missingCount, MAX_FRAGMENT_SENDS_PER_UPDATE);
            for (int i = 0; i < resendLimit; i++)
            {
                byte fragmentIndex = packetData[FRAGMENT_NACK_HEADER_SIZE + i];
                if (fragmentIndex >= sentMessage.totalFragments)
                    continue;

                if (sentMessage.fragmentLastResendTimes == null || fragmentIndex >= sentMessage.fragmentLastResendTimes.Length)
                    continue;

                if (sentMessage.fragmentLastResendTimes[fragmentIndex] > 0f && now - sentMessage.fragmentLastResendTimes[fragmentIndex] < FRAGMENT_RESEND_COOLDOWN)
                    continue;

                byte[] packet = sentMessage.packets[fragmentIndex];
                if (packet == null)
                    continue;

                _pendingFragmentSends.Enqueue(new PendingFragmentSend(new ByteData(packet, 0, packet.Length), sentMessage.channel));
                sentMessage.fragmentLastResendTimes[fragmentIndex] = now;
                enqueuedCount++;
            }

            if (enqueuedCount > 0)
            {
                _outboundFragmentBuffer[fragmentId] = sentMessage;
                FlushPendingFragmentSends();
            }
        }

        private void ProcessFragmentAck(byte[] packetData, int packetLength)
        {
            if (packetLength < FRAGMENT_CONTROL_HEADER_SIZE)
                return;

            uint fragmentId = packetData[2] | ((uint)packetData[3] << 8) | ((uint)packetData[4] << 16) | ((uint)packetData[5] << 24);
            _outboundFragmentBuffer.Remove(fragmentId);
        }

        private void RequestMissingFragments(uint fragmentId, byte totalFragments, ref FragmentedMessage message)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - message.lastNackTime < FRAGMENT_REQUEST_INTERVAL)
                return;

            if (message.fragments == null || message.fragments.Length == 0)
                return;

            int fragmentCount = Math.Min(totalFragments, message.fragments.Length);
            if (fragmentCount <= 0)
                return;

            var missingIndices = new List<byte>();
            for (byte i = 0; i < fragmentCount; i++)
            {
                if (message.fragments[i] == null)
                    missingIndices.Add(i);

                if (missingIndices.Count >= MAX_MISSING_INDICES_PER_REQUEST)
                    break;
            }

            if (missingIndices.Count == 0)
                return;

            int packetSize = FRAGMENT_NACK_HEADER_SIZE + missingIndices.Count;
            byte[] packet = new byte[packetSize];
            packet[0] = FRAGMENT_MAGIC;
            packet[1] = FRAGMENT_TYPE_NACK;
            packet[2] = (byte)(fragmentId & 0xFF);
            packet[3] = (byte)((fragmentId >> 8) & 0xFF);
            packet[4] = (byte)((fragmentId >> 16) & 0xFF);
            packet[5] = (byte)((fragmentId >> 24) & 0xFF);
            packet[6] = (byte)missingIndices.Count;

            for (int i = 0; i < missingIndices.Count; i++)
                packet[FRAGMENT_NACK_HEADER_SIZE + i] = missingIndices[i];

            bool sent = SendSinglePacketWithValidation(new ByteData(packet, 0, packetSize), Channel.ReliableOrdered);
            if (sent)
                message.lastNackTime = now;
        }

        private void SendFragmentAck(uint fragmentId)
        {
            byte[] packet = new byte[FRAGMENT_CONTROL_HEADER_SIZE];
            packet[0] = FRAGMENT_MAGIC;
            packet[1] = FRAGMENT_TYPE_ACK;
            packet[2] = (byte)(fragmentId & 0xFF);
            packet[3] = (byte)((fragmentId >> 8) & 0xFF);
            packet[4] = (byte)((fragmentId >> 16) & 0xFF);
            packet[5] = (byte)((fragmentId >> 24) & 0xFF);

            SendSinglePacketWithValidation(new ByteData(packet, 0, packet.Length), Channel.ReliableOrdered);
        }

        private void CleanupExpiredFragments()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            var expiredIds = new List<uint>();
            var keys = new List<uint>(_fragmentBuffer.Keys);

            foreach (var key in keys)
            {
                if (!_fragmentBuffer.TryGetValue(key, out var message))
                    continue;

                // Keep requesting missing fragments for incomplete assemblies while waiting for timeout.
                if (message.receivedCount > 0 && message.receivedCount < message.fragments.Length)
                {
                    byte totalFragments = (byte)message.fragments.Length;
                    RequestMissingFragments(key, totalFragments, ref message);
                    _fragmentBuffer[key] = message;
                }

                if (currentTime - message.creationTime > FRAGMENT_TIMEOUT)
                {
                    expiredIds.Add(key);
                    int receivedCount = message.receivedCount;
                    int totalCount = message.fragments.Length;
                    PurrLogger.LogWarning($"[UTP] Fragment assembly timeout (ID: {key}). Received {receivedCount}/{totalCount} fragments. Incomplete fragments discarded.");
                }
            }

            foreach (var id in expiredIds)
            {
                _fragmentBuffer.Remove(id);
            }
        }

        private void CleanupExpiredOutboundFragments()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            var expiredIds = new List<uint>();

            foreach (var kvp in _outboundFragmentBuffer)
            {
                if (currentTime - kvp.Value.creationTime > OUTBOUND_FRAGMENT_TIMEOUT)
                    expiredIds.Add(kvp.Key);
            }

            foreach (var id in expiredIds)
            {
                _outboundFragmentBuffer.Remove(id);
            }
        }
#endif
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS

        private void MakeSureBufferCanFit(int packetLength)
        {
            if (_buffer.Length < packetLength)
                Array.Resize(ref _buffer, packetLength);
        }

        private void PostConnect()
        {
            if (!_connection.IsCreated)
            {
                connectionState = ConnectionState.Disconnecting;
                connectionState = ConnectionState.Disconnected;
                PurrLogger.LogError("Failed to connect to host");
                return;
            }

            connectionState = ConnectionState.Connecting;
        }

        void Disconnect()
        {
            if (!_connection.IsCreated)
                return;

            ClearPendingFragments();

            if (connectionState != ConnectionState.Disconnected)
                connectionState = ConnectionState.Disconnecting;

            try
            {
                _driver.Disconnect(_connection);
            }
            catch
            {
                // ignored
            }

            connectionState = ConnectionState.Disconnected;
            _connection = default;
        }
#endif

        /// <summary>
        /// Stops the client, disconnects from the server, and releases all resources.
        /// </summary>
        public void Stop()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            ClearPendingFragments();
            Disconnect();

            if (_driver.IsCreated)
                _driver.Dispose();
#endif
        }
    }
}

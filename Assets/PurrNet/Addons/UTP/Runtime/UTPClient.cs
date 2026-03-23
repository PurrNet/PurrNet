#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID)
#define DISABLEUTPWORKS
#endif

using System;
#if UTP_NET_PACKAGE
using System.Collections;
#endif
using PurrNet.Transports;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
using PurrNet.Logging;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Networking.Transport.Error;
#endif

#if UTP_NET_PACKAGE
using Unity.Networking.Transport.Relay;
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
#endif
        }

        /// <summary>
        /// Flushes outgoing network messages to the server.
        /// Should be called regularly to ensure timely message delivery.
        /// </summary>
        public void SendMessages()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            //if (_driver.IsCreated)
            //    _driver.ScheduleUpdate().Complete();
            // Update is handled in ReceiveMessages
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

                        ByteData byteData = new ByteData(_buffer, 0, packetLength);
                        // LogTransportTrace($"Receive data len={packetLength}");
                        onDataReceived?.Invoke(byteData);
                        break;
                    }
                    case NetworkEvent.Type.Connect:
                        // LogTransportTrace("Receive connect event");
                        connectionState = ConnectionState.Connected;
                        break;
                    case NetworkEvent.Type.Disconnect:
                        // LogTransportTrace("Receive disconnect event");
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

        private void OnLocalConnectionState(NetworkEvent.Type eventType)
        {
            switch (eventType)
            {
                case NetworkEvent.Type.Connect:
                    connectionState = ConnectionState.Connected;
                    break;
                case NetworkEvent.Type.Disconnect:
                    connectionState = ConnectionState.Disconnecting;
                    connectionState = ConnectionState.Disconnected;
                    break;
            }
        }

        void Disconnect()
        {
            if (!_connection.IsCreated) 
                return;
            
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
            Disconnect();

            if (_driver.IsCreated)
                _driver.Dispose();
#endif
        }

        private void LogTransportTrace(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PurrLogger.Log($"[TransportTrace][UTPClient] {message}");
#endif
        }
    }
}

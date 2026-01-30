#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLEUTPWORKS
#endif

#if UTP_LOBBYRELAY
#define UTP_NET_PACKAGE
#endif

using System;
using System.Collections;
using PurrNet.Transports;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
using PurrNet.Logging;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Collections;
using Unity.Networking.Transport.Error;
#endif

namespace PurrNet.UTP
{
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
        public event Action<ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used
        public event Action<ConnectionState> onConnectionState;

        private ConnectionState _state = ConnectionState.Disconnected;

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

        public IEnumerator Connect(string address, ushort port, bool dedicated = false, RelayServerData? relayData = null)
        {
            yield return null;
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
                if (!NetworkEndpoint.TryParse(address, port, out endpoint))
                {
                    PurrLogger.LogError($"Failed to parse address: {address}:{port}");
                    connectionState = ConnectionState.Disconnected;
                    yield break;
                }
            }

            _connection = _driver.Connect(endpoint);
            
            PostConnect();
#endif
        }

        public IEnumerator ConnectP2P(string lobbyId, bool dedicated = false, RelayServerData? relayData = null)
        {
            yield return null;
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!relayData.HasValue)
            {
                PurrLogger.LogError("Relay data is required for P2P connection");
                yield break;
            }

            var relayDataValue = relayData.Value;
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayDataValue);
            _driver = NetworkDriver.Create(settings);
            
            _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _unreliablePipeline = NetworkPipeline.Null;

            _connection = _driver.Connect(relayData.Value.Endpoint);
            
            PostConnect();
#endif
        }

        public void Send(ByteData data, Channel channel)
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            if (!_connection.IsCreated || _driver.GetConnectionState(_connection) != NetworkConnection.State.Connected)
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
                var result = _driver.BeginSend(pipeline, _connection, out var writer);
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

            NetworkEvent.Type cmd;
            while ((cmd = _driver.PopEventForConnection(_connection, out var stream)) != NetworkEvent.Type.Empty)
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
                    onDataReceived?.Invoke(byteData);
                }
                else if (cmd == NetworkEvent.Type.Connect)
                {
                    connectionState = ConnectionState.Connected;
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    connectionState = ConnectionState.Disconnecting;
                    connectionState = ConnectionState.Disconnected;
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
            if (_connection.IsCreated)
            {
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
        }
#endif

        public void Stop()
        {
#if UTP_NET_PACKAGE && !DISABLEUTPWORKS
            Disconnect();
            
            if (_driver.IsCreated)
                _driver.Dispose();
#endif
        }
    }
}
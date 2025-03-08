using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using PurrNet.Transports;

#if UTP_TRANSPORT
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
#endif
#if UTP_RELAY
using Unity.Services.Relay.Models;
#endif

namespace PurrNet.UTP {
#if UTP_TRANSPORT
    [BurstCompile]
    struct ClientUpdateJob : IJob {
        /// <summary>
        /// Used to bind, listen, and send data to connections.
        /// </summary>
        public NetworkDriver driver;

        /// <summary>
        /// client connections to this server.
        /// </summary>
        public NetworkConnection connection;

        /// <summary>
        /// Temporary storage for connection events that occur on job threads so they may be dequeued on the main thread.
        /// </summary>
        public NativeQueue<UTPConnectionEvent>.ParallelWriter connectionEventsQueue;

        /// <summary>
        /// Process all incoming events/messages on this connection.
        /// </summary>
        public void Execute() {
            //Back out if connection is invalid
            if(!connection.IsCreated) {
                return;
            }

            NetworkEvent.Type netEvent;
            while((netEvent = connection.PopEvent(driver, out DataStreamReader stream)) != NetworkEvent.Type.Empty) {
                //Create new event
                UTPConnectionEvent connectionEvent = default(UTPConnectionEvent);

                switch(netEvent) {
                    //Connect event
                    case (NetworkEvent.Type.Connect): {

                            connectionEvent = new UTPConnectionEvent() {
                                eventType = (byte)UTPConnectionEventType.OnConnected,
                                connectionId = connection.GetHashCode()
                            };

                            //Queue event
                            connectionEventsQueue.Enqueue(connectionEvent);

                            break;
                        }

                    //Data recieved event
                    case (NetworkEvent.Type.Data): {
                            //Create managed array of data
                            NativeArray<byte> nativeMessage = new NativeArray<byte>(stream.Length, Allocator.Temp);

                            //Read data from stream
                            stream.ReadBytes(nativeMessage);

                            connectionEvent = new UTPConnectionEvent() {
                                eventType = (byte)UTPConnectionEventType.OnReceivedData,
                                connectionId = connection.GetHashCode(),
                                eventData = GetFixedList(nativeMessage)
                            };

                            //Queue event
                            connectionEventsQueue.Enqueue(connectionEvent);

                            break;
                        }

                    //Disconnect event
                    case (NetworkEvent.Type.Disconnect): {
                            connectionEvent = new UTPConnectionEvent() {
                                eventType = (byte)UTPConnectionEventType.OnDisconnected,
                                connectionId = connection.GetHashCode()
                            };

                            //Queue event
                            connectionEventsQueue.Enqueue(connectionEvent);

                            break;
                        }

                }
            }
        }

        public FixedList4096Bytes<byte> GetFixedList(NativeArray<byte> data) {
            FixedList4096Bytes<byte> retVal = new FixedList4096Bytes<byte>();

            if(data.Length > 0) {
                unsafe {
                    retVal.AddRange(NativeArrayUnsafeUtility.GetUnsafePtr(data), data.Length);
                }
            }

            return retVal;
        }
    }

    [BurstCompile]
    struct ClientSendJob : IJob {

        /// <summary>
        /// Used to bind, listen, and send data to connections.
        /// </summary>
        public NetworkDriver driver;

        /// <summary>
        /// The network pipeline to stream data.
        /// </summary>
        public NetworkPipeline pipeline;

        /// <summary>
        /// The client's network connection instance.
        /// </summary>
        public NetworkConnection connection;


        /// <summary>
        /// The segment of data to send over (deallocates after use).
        /// </summary>
        [DeallocateOnJobCompletion]
        public NativeArray<byte> data;

        public void Execute() {
            //Back out if connection is invalid
            if(!connection.IsCreated) {
                return;
            }

            DataStreamWriter writer;
            int writeStatus = driver.BeginSend(pipeline, connection, out writer);

            //If endpoint was success, write data to stream
            if(writeStatus == (int)Unity.Networking.Transport.Error.StatusCode.Success) {
                writer.WriteBytes(data);
                driver.EndSend(writer);
            }

        }
    }
#endif

    /// <summary>
    /// A client for PurrNet using UTP.
    /// </summary>
    public class UTPClient : UTPEntity {
        /// <summary>
        /// Invokes when connected to a server.
        /// </summary>
        public Action<int> OnConnected;

        /// <summary>
        /// Invokes when data has been received.
        /// </summary>
        public Action<int, ArraySegment<byte>> OnReceivedData;

        /// <summary>
        /// Invokes when disconnected from a server.
        /// </summary>
        public Action<int> OnDisconnected;

#if UTP_TRANSPORT
        /// <summary>
        /// Used alongside a driver to connect, send, and receive data from a listen server.
        /// </summary>
        private NetworkConnection connection;

        /// <summary>
        /// The number of pipelines tracked in the header size array.
        /// </summary>
        private const int NUM_PIPELINES = 2;

        /// <summary>
        /// The driver's max header size for UTP transport.
        /// </summary>
        private int[] driverMaxHeaderSize = new int[NUM_PIPELINES];
#endif

        /// <summary>
        /// Whether the client is connected to the server or not.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Constructor for UTP client.
        /// </summary>
        /// <param name="OnConnected">Action that is invoked when connected.</param>
        /// <param name="OnReceivedData">Action that is invoked when receiving data.</param>
        /// <param name="OnDisconnected">Action that is invoked when disconnected.</param>
        public UTPClient(Action<int> OnConnected, Action<int, ArraySegment<byte>> OnReceivedData, Action<int> OnDisconnected) {
            this.OnConnected = OnConnected;
            this.OnReceivedData = OnReceivedData;
            this.OnDisconnected = OnDisconnected;
        }

        /// <summary>
        /// Attempt to connect to a listen server at a given IP/port. Currently only supports IPV4.
        /// </summary>
        /// <param name="address">The host address at which the listen server is running.</param>
        /// <param name="port">The port which the listen server is listening on.</param>
        public bool Connect(string address, ushort port, int timeoutMs = 1000) {
#if UTP_TRANSPORT
            if(IsConnected) {
                UTPLog.Warning($"Abandoning connection attempt, this client is already connected to a server.");
                return false;
            }

            if(string.IsNullOrEmpty(address)) {
                UTPLog.Error("Abandoning connection attempt, a null or empty address was provided.");
                return false;
            }

            if(address == "localhost") {
                address = "127.0.0.1";
            }

            // TODO: Support for IPv6.
            NetworkEndpoint endpoint;
            if(!NetworkEndpoint.TryParse(address, port, out endpoint)) {
                UTPLog.Error($"Abandoning connection attempt, failed to convert {address}:{port} into a valid NetworkEndpoint.");
                return false;
            }

            connectionsEventsQueue = new NativeQueue<UTPConnectionEvent>(Allocator.Persistent);

            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(disconnectTimeoutMS: timeoutMs);

            driver = NetworkDriver.Create(settings);
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            unreliablePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            connection = driver.Connect(endpoint);

            if(!IsValidConnection(connection)) {
                UTPLog.Error($"Client failed to connect to Server: {address}:{port}");
                return false;
            }

            UTPLog.Info($"Client connected to Server: {address}:{port}");
            return true;
#else
            UTPLog.Error($"Client failed to connect to Server: UTP assembly references are missing.");
            return false;
#endif
        }

        /// <summary>
        /// Attempt to connect to a Relay host given a join allocation.
        /// </summary>
        /// <param name="joinAllocation"></param>
#if UTP_RELAY
        public bool RelayConnect(JoinAllocation joinAllocation) {
#else
        public bool RelayConnect(object joinAllocation) {
#endif
#if UTP_RELAY
            if(IsConnected) {
                UTPLog.Warning($"Abandoning connection attempt, this client is already connected to a server.");
                return false;
            }

            connectionsEventsQueue = new NativeQueue<UTPConnectionEvent>(Allocator.Persistent);

            RelayServerData relayServerData = PlayerRelayData(joinAllocation, RelayServerEndpoint.NetworkOptions.Udp);

            var networkSettings = new NetworkSettings();
            networkSettings.WithRelayParameters(ref relayServerData);

            driver = NetworkDriver.Create(networkSettings);
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            unreliablePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            connection = driver.Connect(relayServerData.Endpoint);

            if(!IsValidConnection(connection)) {
                UTPLog.Error($"Client failed to connect to Relay Server: {relayServerData.Endpoint.Address}");
                return false;
            }

            UTPLog.Info($"Client connected to Relay Server: {relayServerData.Endpoint.Address}");
            return true;
#else
            UTPLog.Error($"Client failed to connect to Relay Server: UTP assembly references are missing.");
            return false;
#endif
        }

#if UTP_RELAY
        /// <summary>
        /// Construct the ServerData needed to create a RelayNetworkParameter for a player.
        /// </summary>
        /// <param name="allocation">The JoinAllocation for the Relay Server.</param>
        /// <param name="connectionType">The type of connection to the Relay Server.</param>
        /// <returns>The RelayServerData.</returns>
        private RelayServerData PlayerRelayData(JoinAllocation allocation, RelayServerEndpoint.NetworkOptions connectionType) {
            //Get string from connection
            string connectionTypeString = GetStringFromConnectionType(connectionType);

            if(string.IsNullOrEmpty(connectionTypeString)) {
                throw new ArgumentException($"ConnectionType {connectionType} is invalid");
            }

            // Select endpoint based on desired connectionType
            var endpoint = GetEndpointForConnectionType(allocation.ServerEndpoints, connectionTypeString);

            if(endpoint == null) {
                throw new ArgumentException($"endpoint for connectionType {connectionType} not found");
            }

            // Prepare the server endpoint using the Relay server IP and port
            var serverEndpoint = NetworkEndpoint.Parse(endpoint.Host, (ushort)endpoint.Port);

            // UTP uses pointers instead of managed arrays for performance reasons, so we use these helper functions to convert them
            var allocationIdBytes = ConvertFromAllocationIdBytes(allocation.AllocationIdBytes);
            var connectionData = ConvertConnectionData(allocation.ConnectionData);
            var hostConnectionData = ConvertConnectionData(allocation.HostConnectionData);
            var key = ConvertFromHMAC(allocation.Key);

            // Prepare the Relay server data and compute the nonce values
            // A player joining the host passes its own connectionData as well as the host's
            var relayServerData = new RelayServerData(ref serverEndpoint, 0, ref allocationIdBytes, ref connectionData, ref hostConnectionData, ref key, connectionTypeString == "dtls");

            return relayServerData;
        }
#endif

        /// <summary>
        /// Disconnect from a listen server.
        /// </summary>
        public void Disconnect() {
#if UTP_TRANSPORT
            jobHandle.Complete();

            //If there is an existing connection, force a disconnect
            if(connection.IsCreated) {
                UTPLog.Info("Client disconnecting from server");

                //Disconnect from server
                connection.Disconnect(driver);
                connection = default(Unity.Networking.Transport.NetworkConnection);

                //We need to ensure the driver has the opportunity to send a disconnect event to the server
                driver.ScheduleUpdate().Complete();

                //Invoke disconnect action
                OnDisconnected?.Invoke(0); //todo: Can't get connectionId here? So just set to 0, might be wrong but maybe wont matter
            }

            //Flush the event queue
            if(connectionsEventsQueue.IsCreated) {
                ProcessIncomingEvents();
                connectionsEventsQueue.Dispose();
            }

            //Dispose of existing network driver
            if(driver.IsCreated) {
                driver.Dispose();
                driver = default(NetworkDriver);
            }
#endif
        }

        /// <summary>
        /// Tick the client, creating the client job and scheduling it. Processes incoming events 
        /// </summary>
        public void Tick() {
#if UTP_TRANSPORT
            // First complete the job that was initialized in the previous frame
            jobHandle.Complete();

            // Trigger callbacks for events that resulted in the last jobs work
            ProcessIncomingEvents();

            //Cache driver & connection info
            cacheConnectionInfo();

            // Need to ensure the driver did not become inactive
            if(!IsNetworkDriverInitialized()) {
                driverMaxHeaderSize = new int[NUM_PIPELINES];
                return;
            }

            // Create a new job
            var job = new ClientUpdateJob {
                driver = driver,
                connection = connection,
                connectionEventsQueue = connectionsEventsQueue.AsParallelWriter()
            };

            // Schedule job
            driver.ScheduleUpdate().Complete();
            jobHandle = job.Schedule();
#endif
        }

        /// <summary>
        /// Send data to the listen server over a particular channel.
        /// </summary>
        /// <param name="segment">The data to send.</param>
        /// <param name="channel">The channel to send the data over.</param>
        public void Send(ArraySegment<byte> segment, Channel channel) {
#if UTP_TRANSPORT
            jobHandle.Complete();

            //Get pipeline for job
            NetworkPipeline pipeline = (channel == Channel.ReliableOrdered || channel == Channel.ReliableUnordered) ? reliablePipeline : unreliablePipeline;

            //Convert ArraySegment to NativeArray for burst compile
            NativeArray<byte> segmentArray = new NativeArray<byte>(segment.Count, Allocator.Persistent);
            NativeArray<byte>.Copy(segment.Array, segment.Offset, segmentArray, 0, segment.Count);

            // Create a new job
            var job = new ClientSendJob {
                driver = driver,
                pipeline = pipeline,
                connection = connection,
                data = segmentArray
            };

            // Schedule job
            jobHandle = job.Schedule();
            jobHandle.Complete();
#endif
        }

#if UTP_TRANSPORT
        /// <summary>
        /// Processes connection events from the queue.
        /// </summary>
        public void ProcessIncomingEvents() {
            // Exit if the driver is not active or connection isn't ready
            if(!IsNetworkDriverInitialized() || !connection.IsCreated) {
                return;
            }

            //Process event queue
            while(connectionsEventsQueue.IsCreated && connectionsEventsQueue.TryDequeue(out UTPConnectionEvent connectionEvent)) {
                switch(connectionEvent.eventType) {
                    //Connect action 
                    case ((byte)UTPConnectionEventType.OnConnected): {
                            OnConnected?.Invoke(connectionEvent.connectionId);
                            break;
                        }

                    //Receive data action
                    case ((byte)UTPConnectionEventType.OnReceivedData): {
                            OnReceivedData?.Invoke(connectionEvent.connectionId, new ArraySegment<byte>(connectionEvent.eventData.ToArray()));
                            break;
                        }

                    //Disconnect action
                    case ((byte)UTPConnectionEventType.OnDisconnected): {
                            OnDisconnected?.Invoke(connectionEvent.connectionId);
                            break;
                        }

                    //Invalid action
                    default: {
                            UTPLog.Warning($"Invalid connection event: {connectionEvent.eventType}");
                            break;
                        }

                }
            }
        }

        /// <summary>
        /// Returns this client's driver's max header size based on the requested channel.
        /// </summary>
        /// <param name="channel">The channel to check.</param>
        /// <returns>This client's max header size.</returns>
        public int GetMaxHeaderSize(Channel channel = Channel.ReliableOrdered) {
            if(IsConnected && IsNetworkDriverInitialized()) {
                return driverMaxHeaderSize[ChannelToDriverIndex(channel)];
            }

            return 0;
        }

        /// <summary>
        /// Caches important properties to allow for getter methods to be called without interfering with the job system.
        /// </summary>
        private void cacheConnectionInfo() {
            //Check for an active connection from this client
            if(IsValidConnection(connection)) {
                bool isInitialized = IsNetworkDriverInitialized();

                //If driver is active, cache its max header size for UTP transport
                if(isInitialized) {
                    driverMaxHeaderSize[ChannelToDriverIndex(Channel.ReliableOrdered)] = driver.MaxHeaderSize(reliablePipeline);
                    driverMaxHeaderSize[ChannelToDriverIndex(Channel.UnreliableSequenced)] = driver.MaxHeaderSize(unreliablePipeline);
                }

                //Set connection state
                IsConnected = isInitialized && connection.GetState(driver) == Unity.Networking.Transport.NetworkConnection.State.Connected;
            } else {
                //If there is no valid connection, set values accordingly
                driverMaxHeaderSize[ChannelToDriverIndex(Channel.ReliableOrdered)] = 0;
                driverMaxHeaderSize[ChannelToDriverIndex(Channel.UnreliableSequenced)] = 0;
                IsConnected = false;
            }
        }

        private int ChannelToDriverIndex(Channel channel) {
            return (channel == Channel.ReliableOrdered || channel == Channel.ReliableUnordered) ? 0 : 1;
        }
#endif
    }
}

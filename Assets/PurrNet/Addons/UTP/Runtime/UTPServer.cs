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
    /// <summary>
    /// Job used to update connections. 
    /// </summary>
    [BurstCompile]
    struct ServerUpdateConnectionsJob : IJob {
        /// <summary>
        /// Used to bind, listen, and send data to connections.
        /// </summary>
        public NetworkDriver driver;

        /// <summary>
        /// Client connections to this server.
        /// </summary>
        public NativeList<NetworkConnection> connections;

        /// <summary>
        /// Temporary storage for connection events that occur on job threads so they may be dequeued on the main thread.
        /// </summary>
        public NativeQueue<UTPConnectionEvent>.ParallelWriter connectionsEventsQueue;

        public void Execute() {
            //Iterate through connections list
            for(int i = 0; i < connections.Length; i++) {
                //If a connection is no longer established, remove it
                if(driver.GetConnectionState(connections[i]) == NetworkConnection.State.Disconnected) {
                    connections.RemoveAtSwapBack(i--);
                }
            }

            // Accept new connections
            NetworkConnection networkConnection;
            while((networkConnection = driver.Accept()) != default(NetworkConnection)) {
                //Set up connection event
                UTPConnectionEvent connectionEvent = new UTPConnectionEvent() {
                    eventType = (byte)UTPConnectionEventType.OnConnected,
                    connectionId = networkConnection.GetHashCode()
                };

                //Queue connection event
                connectionsEventsQueue.Enqueue(connectionEvent);

                //Add connection to connection list
                connections.Add(networkConnection);
            }
        }

        /// <summary>
        /// Disconnect and remove a connection via it's ID.
        /// </summary>
        /// <param name="connectionId">The ID of the connection to disconnect.</param>
        public void Disconnect(int connectionId) {
            foreach(NetworkConnection connection in connections) {
                if(connection.GetHashCode() == connectionId) {
                    connection.Disconnect(driver);

                    //Set up connection event
                    UTPConnectionEvent connectionEvent = new UTPConnectionEvent() {
                        eventType = (byte)UTPConnectionEventType.OnDisconnected,
                        connectionId = connection.GetHashCode()
                    };

                    //Queue connection event
                    connectionsEventsQueue.Enqueue(connectionEvent);

                    return;
                }
            }
        }
    }

    /// <summary>
    /// Job to query incoming events for all connections. 
    /// </summary>
    [BurstCompile]
    struct ServerUpdateJob : IJobParallelForDefer {
        /// <summary>
        /// Used to bind, listen, and send data to connections.
        /// </summary>
        public NetworkDriver.Concurrent driver;

        /// <summary>
        /// client connections to this server.
        /// </summary>
        public NativeArray<NetworkConnection> connections;

        /// <summary>
        /// Temporary storage for connection events that occur on job threads so they may be dequeued on the main thread.
        /// </summary>
        public NativeQueue<UTPConnectionEvent>.ParallelWriter connectionsEventsQueue;

        /// <summary>
        /// Process all incoming events/messages on this connection.
        /// </summary>
        /// <param name="index">The current index being accessed in the array.</param>
        public void Execute(int index) {
            NetworkEvent.Type netEvent;
            while((netEvent = driver.PopEventForConnection(connections[index], out DataStreamReader stream)) != NetworkEvent.Type.Empty) {
                if(netEvent == NetworkEvent.Type.Data) {
                    NativeArray<byte> nativeMessage = new NativeArray<byte>(stream.Length, Allocator.Temp);
                    stream.ReadBytes(nativeMessage);

                    //Set up connection event
                    UTPConnectionEvent connectionEvent = new UTPConnectionEvent() {
                        eventType = (byte)UTPConnectionEventType.OnReceivedData,
                        eventData = GetFixedList(nativeMessage),
                        connectionId = connections[index].GetHashCode()
                    };

                    //Queue connection event
                    connectionsEventsQueue.Enqueue(connectionEvent);
                } else if(netEvent == NetworkEvent.Type.Disconnect) {
                    //Set up disconnect event
                    UTPConnectionEvent connectionEvent = new UTPConnectionEvent() {
                        eventType = (byte)UTPConnectionEventType.OnDisconnected,
                        connectionId = connections[index].GetHashCode()
                    };

                    //Queue disconnect event
                    connectionsEventsQueue.Enqueue(connectionEvent);
                }
            }
        }

        /// <summary>
        /// Convert unmanaged native array to 4096 Byte list. Uses unsafe code.
        /// </summary>
        /// <param name="data">The data to convert.</param>
        /// <returns>An unmanaged fixed list of data.</returns>
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
    struct ServerSendJob : IJob {
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
            DataStreamWriter writer;
            int writeStatus = driver.BeginSend(pipeline, connection, out writer);

            //If Acquire was success
            if(writeStatus == (int)Unity.Networking.Transport.Error.StatusCode.Success) {
                writer.WriteBytes(data);
                driver.EndSend(writer);
            }
        }
    }
#endif

    /// <summary>
    /// A listen server for PurrNet using UTP. 
    /// </summary>
    public class UTPServer : UTPEntity {
        /// <summary>
        /// Invokes when a client has connected to the server.
        /// </summary>
        public Action<int> OnConnected;

        /// <summary>
        /// Invokes when data has been received by a third party.
        /// </summary>
        public Action<int, ArraySegment<byte>> OnReceivedData;

        /// <summary>
        /// Invokes when a client has disconnected.
        /// </summary>
        public Action<int> OnDisconnected;

#if UTP_TRANSPORT
        /// <summary>
        /// Client connections to this server.
        /// </summary>
        private NativeList<NetworkConnection> connections;

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
        /// Constructor for UTP server.
        /// </summary>
        /// <param name="OnConnected">Action that is invoked when connected.</param>
        /// <param name="OnReceivedData">Action that is invoked when receiving data.</param>
        /// <param name="OnDisconnected">Action that is invoked when disconnected.</param>
        public UTPServer(Action<int> OnConnected, Action<int, ArraySegment<byte>> OnReceivedData, Action<int> OnDisconnected) {
            this.OnConnected = OnConnected;
            this.OnReceivedData = OnReceivedData;
            this.OnDisconnected = OnDisconnected;
        }

        /// <summary>
        /// Initialize the server. Currently only supports IPV4.
        /// </summary>
        /// <param name="port">The port to listen for connections on.</param>
        /// <param name="useP2P">Whether to start server using P2P connection or Unity Relay Service.</param>
        /// <param name="allocation">The Relay allocation, if using Relay.</param>
#if UTP_RELAY
        public bool Start(ushort port, bool useP2P = false, Allocation allocation = null, int timeoutMs = 1000) {
#else
        public bool Start(ushort port, bool useP2P = false, object allocation = null, int timeoutMs = 1000) {
#endif
#if UTP_TRANSPORT
            if(IsNetworkDriverInitialized()) {
                UTPLog.Warning("Attempting to start a server that is already active.");
                return false;
            }

            //Instantiate network settings
            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(disconnectTimeoutMS: timeoutMs);

            //Create IPV4 endpoint
            NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4;
            endpoint.Port = port;

            if(useP2P) {
                //Initialize network settings
                NetworkSettings networkSettings = new NetworkSettings();

                //Instantiate network driver
                driver = NetworkDriver.Create(networkSettings);
#if UTP_RELAY
            } else {
                //Instantiate relay network data
                RelayServerData relayServerData = HostRelayData(allocation, RelayServerEndpoint.NetworkOptions.Udp);
                RelayNetworkParameter relayNetworkParameter = new RelayNetworkParameter { ServerData = relayServerData };
                NetworkSettings networkSettings = new NetworkSettings();
                //settings.WithNetworkConfigParameters(disconnectTimeoutMS: timeoutInMilliseconds);

                //Initialize relay network
                RelayParameterExtensions.WithRelayParameters(ref networkSettings, ref relayServerData);

                //Instantiate network driver
                driver = NetworkDriver.Create(networkSettings);
#endif
            }

            //Initialize connections list & event queue
            connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            connectionsEventsQueue = new NativeQueue<UTPConnectionEvent>(Allocator.Persistent);

            //Create network pipelines
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            unreliablePipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));

            int bindReturnCode = driver.Bind(endpoint);
            if(!driver.Bound) {
                UTPLog.Error($"Unable to start server, failed to bind the specified port {endpoint.Port}. {nameof(NetworkDriver.Bind)}() returned {bindReturnCode}.");
                return false;
            }

            int listenReturnCode = driver.Listen();
            if(!driver.Listening) {
                UTPLog.Error($"Unable to start server, failed to listen. {nameof(NetworkDriver.Listen)} returned {listenReturnCode}.");
                return false;
            }

            if(useP2P) {
                UTPLog.Info($"P2P Server Started on Port: {endpoint.Port}");
                return true;
            } else {
#if UTP_RELAY
                UTPLog.Info($"Relay Server Started: {allocation.RelayServer.IpV4}:{allocation.RelayServer.Port}");
                return true;
#else
                UTPLog.Error($"Server failed to start: Relay is unavailable & P2P was disabled.");
                return false;
#endif
            }
#else
            UTPLog.Error($"Server failed to start: UTP assembly references are missing.");
            return false;
#endif
        }

#if UTP_RELAY
        /// <summary>
        /// Construct the ServerData needed to create a RelayNetworkParameter for a host.
        /// </summary>
        /// <param name="allocation">The Allocation for the Relay Server.</param>
        /// <param name="connectionType">The type of connection to the Relay Server.</param>
        /// <returns>The RelayServerData.</returns>
        private RelayServerData HostRelayData(Allocation allocation, RelayServerEndpoint.NetworkOptions connectionType) {
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
            var key = ConvertFromHMAC(allocation.Key);

            // Prepare the Relay server data and compute the nonce value
            // The host passes its connectionData twice into this function
            var relayServerData = new RelayServerData(ref serverEndpoint, 0, ref allocationIdBytes, ref connectionData,
                ref connectionData, ref key, connectionTypeString == "dtls");

            return relayServerData;
        }
#endif

        /// <summary>
        /// Tick the server, creating the server jobs and scheduling them. Processes events created by the jobs.
        /// </summary>
        public void Tick() {
#if UTP_TRANSPORT
            //If the network driver has shut down, back out
            if(!IsNetworkDriverInitialized()) {
                return;
            }

            // First complete the job that was initialized in the previous frame
            jobHandle.Complete();

            // Trigger callbacks for events that resulted in the last jobs work
            ProcessIncomingEvents();

            //Cache driver & connection info
            cacheConnectionInfo();

            // Create a new jobs
            var serverUpdateJob = new ServerUpdateJob {
                driver = driver.ToConcurrent(),
                connections = connections.AsDeferredJobArray(),
                connectionsEventsQueue = connectionsEventsQueue.AsParallelWriter()
            };
            
            var connectionJob = new ServerUpdateConnectionsJob {
                driver = driver,
                connections = connections,
                connectionsEventsQueue = connectionsEventsQueue.AsParallelWriter()
            };

            // Schedule jobs
            driver.ScheduleUpdate().Complete();

            jobHandle = serverUpdateJob.Schedule(connections, 1, default);
            jobHandle = connectionJob.Schedule(jobHandle);

            //jobHandle = driver.ScheduleUpdate();

            // We are explicitly scheduling ServerUpdateJob before ServerUpdateConnectionsJob so that disconnect events are enqueued before the corresponding NetworkConnection is removed
            //jobHandle = serverUpdateJob.Schedule(connections, 1, jobHandle);
            //jobHandle = connectionJob.Schedule(jobHandle);
#endif
        }

        /// <summary>
        /// Stop a running server.
        /// </summary>
        public void Stop() {
#if UTP_TRANSPORT
            UTPLog.Info("Stopping server");

            jobHandle.Complete();

            //Dispose of event queue
            if(connectionsEventsQueue.IsCreated) {
                connectionsEventsQueue.Dispose();
            }

            //Dispose of connections
            if(connections.IsCreated) {
                connections.Dispose();
            }

            //Dispose of driver
            if(driver.IsCreated) {
                driver.Dispose();
                driver = default(NetworkDriver);
            }
#endif
        }

        /// <summary>
        /// Disconnect and remove a connection via it's ID.
        /// </summary>
        /// <param name="connectionId">The ID of the connection to disconnect.</param>
        public void Disconnect(int connectionId) {
#if UTP_TRANSPORT
            jobHandle.Complete();

            //Continue if connection was found
            if(TryGetConnection(connectionId, out NetworkConnection connection)) {
                UTPLog.Info($"Disconnecting connection with ID: {connectionId}");
                connection.Disconnect(driver);

                // When disconnecting, we need to ensure the driver has the opportunity to send a disconnect event to the client
                driver.ScheduleUpdate().Complete();

                //Invoke disconnect action
                OnDisconnected?.Invoke(connectionId);
            } else {
                UTPLog.Warning($"Connection not found: {connectionId}");
            }
#endif
        }

        /// <summary>
        /// Send data to a connection over a particular channel.
        /// </summary>
        /// <param name="connectionId">The ID of the connection to send data to.</param>
        /// <param name="segment">The data to send.</param>
        /// <param name="channel">The channel to send the data over.</param>
        public void Send(int connectionId, ArraySegment<byte> segment, Channel channel) {
#if UTP_TRANSPORT
            jobHandle.Complete();

            //Continue if connection was found
            if(TryGetConnection(connectionId, out NetworkConnection connection)) {
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

                jobHandle = job.Schedule();
                jobHandle.Complete();
            }
#endif
        }

        /// <summary>
        /// Determine whether the server is running or not.
        /// </summary>
        /// <returns>True if running, false otherwise.</returns>
        public bool IsActive() {
#if UTP_TRANSPORT
            return IsNetworkDriverInitialized();
#else
            return false;
#endif
        }

        /// <summary>
        /// Look up a client's address via it's ID. If using Relay, this will always return the address of the Relay server.
        /// </summary>
        /// <param name="connectionId">The ID of the connection.</param>
        /// <returns>The client address, or Relay server if using Relay.</returns>
        public string GetClientAddress(int connectionId) {
#if UTP_TRANSPORT
            //If a connection was found, get its address
            if(TryGetConnection(connectionId, out NetworkConnection connection)) {
                NetworkEndpoint endpoint = driver.GetRemoteEndpoint(connection);
                return endpoint.Address;
            } else {
                UTPLog.Warning($"Connection not found: {connectionId}");
                return string.Empty;
            }
#else
            return string.Empty;
#endif
        }

#if UTP_TRANSPORT
        public int GetMaxHeaderSize(Channel channel = Channel.ReliableOrdered) {
            if(IsNetworkDriverInitialized()) {
                return driverMaxHeaderSize[ChannelToDriverIndex(channel)];
            }

            return 0;
        }

        /// <summary>
        /// Processes connection events from the queue.
        /// </summary>
        public void ProcessIncomingEvents() {
            //Check if the server is active
            if(!IsNetworkDriverInitialized()) {
                return;
            }

            //Process the events in the event list
            UTPConnectionEvent connectionEvent;
            while(connectionsEventsQueue.TryDequeue(out connectionEvent)) {
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
        /// Processes connection events from the queue.
        /// </summary>
        /// <param name="connectionId">The ID of the connection to find.</param>
        /// <returns>The connection if found in the list, a default connection otherwise.</returns>
        public NetworkConnection FindConnection(int connectionId) {
            jobHandle.Complete();

            if(connections.IsCreated) {
                foreach(NetworkConnection connection in connections) {
                    if(connection.GetHashCode() == connectionId) {
                        return connection;
                    }
                }
            }

            return default(NetworkConnection);
        }

        /// <summary>
        /// Returns whether a connection is valid.
        /// </summary>
        /// <param name="connectionId">The id of the connection to check.</param>
        /// <returns>Whether the connection is valid.</returns>
        private bool TryGetConnection(int connectionId, out NetworkConnection connection) {
            connection = FindConnection(connectionId);
            return connection.GetHashCode() == connectionId;
        }



        private void cacheConnectionInfo() {
            bool isInitialized = IsNetworkDriverInitialized();

            //If driver is active, cache its max header size for UTP transport
            if(isInitialized) {
                driverMaxHeaderSize[ChannelToDriverIndex(Channel.ReliableOrdered)] = driver.MaxHeaderSize(reliablePipeline);
                driverMaxHeaderSize[ChannelToDriverIndex(Channel.UnreliableSequenced)] = driver.MaxHeaderSize(unreliablePipeline);
            }

        }

        private int ChannelToDriverIndex(Channel channel) {
            return (channel == Channel.ReliableOrdered || channel == Channel.ReliableUnordered) ? 0 : 1;
        }
#endif
    }
}

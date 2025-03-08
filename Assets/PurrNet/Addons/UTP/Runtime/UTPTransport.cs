using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using PurrNet.Transports;
using PurrNet.Utils;

#if UTP_AUTH
using Unity.Services.Authentication;
using Unity.Services.Core;
#endif
#if UTP_TRANSPORT
using Unity.Networking.Transport;
#endif
#if UTP_RELAY
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

namespace PurrNet.UTP {
    [DisallowMultipleComponent]
    public class UTPTransport : GenericTransport, ITransport {
        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        readonly List<Connection> _connections = new List<Connection>();
        public IReadOnlyList<Connection> connections => _connections;

#if !UTP_TRANSPORT
        public override bool isSupported => false;
#else
        public override bool isSupported => Application.platform != RuntimePlatform.WebGLPlayer;
#endif
        public override ITransport transport => this;

        private ConnectionState _listenerState = ConnectionState.Disconnected;
        private ConnectionState _clientState = ConnectionState.Disconnected;
        public ConnectionState listenerState {
            get => _listenerState;
            private set {
                if(_listenerState == value) { return; }

                _listenerState = value;
                onConnectionState?.Invoke(value, true);
            }
        }
        public ConnectionState clientState {
            get => _clientState;
            private set {
                if(_clientState == value) { return; }

                _clientState = value;
                onConnectionState?.Invoke(value, false);
            }
        }

        [Header("Relay")]
        /// <summary>
        /// Join Code for connecting to a Relay server, must be assigned prior to starting client (obtained via UnityLobbyProvider or UTPTransport.AllocateRelayClientAsync).
        /// </summary>
        [Tooltip("Join Code for connecting to a Relay server, must be assigned prior to starting client (obtained via UnityLobbyProvider or UTPTransport.AllocateRelayClientAsync).")]
        public string RelayJoinCode = "";

        [Header("P2P")]

        [Tooltip("Use a direct P2P connection instead of the Unity Relay Service.")]
        public bool UseP2P = false;

        /// <summary>
        /// The IP Address to connect to.
        /// </summary>
        public string Address = "127.0.0.1";

        /// <summary>
        /// The port at which to connect.
        /// </summary>
        public ushort Port = 7777;

        /// <summary>
        /// Timeout in milliseconds for P2P connection.
        /// </summary>
        [Tooltip("Timeout in milliseconds for P2P connection.")]
        public int Timeout = 1000;

        [Header("Debugging")]
        public LogLevel LoggerLevel = LogLevel.Info;

        private UTPServer Server;
        private UTPClient Client;

        /// <summary>
        /// The Relay Allocation for a server/host which initiates connection to the relay.
        /// </summary>
#if UTP_RELAY
        private Allocation RelayServerAllocation { get; set; }
#else
        private object RelayServerAllocation { get; set; }
#endif

        /// <summary>
        /// The Relay JoinAllocation for a client who is connecting to a server.
        /// </summary>
#if UTP_RELAY
        private JoinAllocation RelayClientAllocation { get; set; }
#else
        private object RelayClientAllocation { get; set; }
#endif

        private void Awake() {
            UTPLog.LoggerLevel = LoggerLevel;
        }

        #region Server

        public bool ServerActive() => Server.IsActive();
        public string ServerGetClientAddress(int connectionId) => Server.GetClientAddress(connectionId);

        protected override void StartServerInternal() {
            if(listenerState is ConnectionState.Connecting or ConnectionState.Connected) { return; }

            listenerState = ConnectionState.Connecting;

            Server = new UTPServer(
                (connectionId) => { _connections.Add(new Connection(connectionId)); onConnected?.Invoke(new Connection(connectionId), true); },
                (connectionId, message) => onDataReceived?.Invoke(new Connection(connectionId), new ByteData(message), true),
                (connectionId) => { _connections.Remove(new Connection(connectionId)); onDisconnected?.Invoke(new Connection(connectionId), DisconnectReason.ServerRequest, true); }
            );

            Listen(Port);
        }
        public void Listen(ushort port) {
            if(Server.Start(port, UseP2P, RelayServerAllocation, Timeout)) {
                listenerState = ConnectionState.Connected;
            } else {
                listenerState = ConnectionState.Disconnecting;
                listenerState = ConnectionState.Disconnected;
                Server = null;
            }
        }
        public void StopListening() {
            if(Server != null && Server.IsNetworkDriverInitialized()) {
                Disconnect();

                listenerState = ConnectionState.Disconnecting;

                foreach(var conn in connections) {
                    CloseConnection(conn);
                }
                _connections.Clear();

                Server.Stop();
                listenerState = ConnectionState.Disconnected;
                Server = null;
                RelayServerAllocation = null;
            }
        }

        public void CloseConnection(Connection conn) {
            if(listenerState != ConnectionState.Connected) { return; }

            Server.Disconnect(conn.connectionId);
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered) {
            if(listenerState != ConnectionState.Connected) { return; }
            if(!target.isValid) { return; }

            Server.Send(target.connectionId, data.segment, method);

            RaiseDataSent(target, data, true);
        }

        #endregion

        #region Client

        public bool IsClientConnected => Client != null && Client.IsConnected;

        protected override async void StartClientInternal() {
            if(clientState is ConnectionState.Connecting or ConnectionState.Connected) { return; }

            clientState = ConnectionState.Connecting;

            Client = new UTPClient(
                (connectionId) => { _connections.Add(new Connection(connectionId)); onConnected?.Invoke(new Connection(connectionId), false); },
                (connectionId, message) => onDataReceived?.Invoke(new Connection(connectionId), new ByteData(message), false),
                (connectionId) => { _connections.Remove(new Connection(connectionId)); onDisconnected?.Invoke(new Connection(connectionId), DisconnectReason.ClientRequest, false); }
            );

            if(UseP2P) {
                UTPLog.Info($"Connecting to {Address}:{Port}");

                Connect(Address, Port);
            } else if(!string.IsNullOrWhiteSpace(RelayJoinCode)) {
                if(await AllocateRelayClientAsync(RelayJoinCode)) {
                    UTPLog.Info($"Connecting to Relay Server with Join Code: {RelayJoinCode}");

                    if(Client.RelayConnect(RelayClientAllocation)) {
                        clientState = ConnectionState.Connected;
                    } else {
                        clientState = ConnectionState.Disconnecting;
                        clientState = ConnectionState.Disconnected;
                        RelayClientAllocation = null;
                        Client = null;
                    }
                } else {
                    clientState = ConnectionState.Disconnecting;
                    clientState = ConnectionState.Disconnected;
                    Client = null;
                }
            } else {
                UTPLog.Error($"Failed to allocate Relay Client, RelayJoinCode was not set.");
                clientState = ConnectionState.Disconnecting;
                clientState = ConnectionState.Disconnected;
                Client = null;
            }
        }

        public void Connect(string ip, ushort port) {
            if(Client.Connect(ip, port, Timeout)) {
                clientState = ConnectionState.Connected;
            } else {
                clientState = ConnectionState.Disconnecting;
                clientState = ConnectionState.Disconnected;
                Client = null;
            }
        }

        public void Disconnect() {
            if(IsClientConnected) {
                clientState = ConnectionState.Disconnecting;
                Client.Disconnect();
                clientState = ConnectionState.Disconnected;
                Client = null;

                RelayClientAllocation = null;
            }
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered) {
            if(!IsClientConnected) { return; }

            Client.Send(data.segment, method);

            RaiseDataSent(new Connection(0), data, false);
        }

        #endregion

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) {
            onDataReceived?.Invoke(conn, data, asServer);
        }
        public void RaiseDataSent(Connection conn, ByteData data, bool asServer) {
            onDataSent?.Invoke(conn, data, asServer);
        }

        public void TickUpdate(float delta) {
            if(enabled) {
                if(Server != null) { Server.Tick(); }
                if(Client != null) { Client.Tick(); }
            }
        }

        public void Shutdown() {
            Disconnect();
            StopListening();

            _connections.Clear();
        }

        private void OnDisable() {
            Shutdown();
        }

#pragma warning disable CS1998
        public async Task<bool> InitializeUnityServicesAsync() {
#if UTP_AUTH
            try {
                if(UnityServices.State == ServicesInitializationState.Uninitialized) {
                    //Must initialize with different profiles when connecting multiple clients
                    //Same as AuthenticationService.Instance.SwitchProfile
                    var options = new InitializationOptions();
                    if(ApplicationContext.isClone) {
                        options.SetProfile($"{Random.Range(1, 10000)}");
                    }
                    await UnityServices.InitializeAsync(options);
                    UTPLog.Info($"UnityServices Initialized with Profile: {AuthenticationService.Instance.Profile}");
                }

                if(!AuthenticationService.Instance.IsSignedIn) {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    UTPLog.Info($"UnityServices SignIn: {AuthenticationService.Instance.PlayerId}");
                }
            } catch {
                UTPLog.Error("UnityServices Initialization failed.");
            }

            return UnityServices.State != ServicesInitializationState.Uninitialized && AuthenticationService.Instance.IsSignedIn;
#else
            UTPLog.Error($"Failed to initialize Unity Services: UTP assembly references are missing.");
            return false;
#endif
        }

#if UTP_RELAY
        public void InitializeRelayServer(Allocation serverAllocation) {
#else
        public void InitializeRelayServer(object serverAllocation) {
#endif
            RelayServerAllocation = serverAllocation;
        }
        public void InitializeRelayClient(string joinCode) {
            RelayJoinCode = joinCode;
        }

        /// <summary>
        /// Allocates a Relay Server in a given Region. If no valid RegionId is provided, the most optimal Region will be automatically used instead.
        /// </summary>
        /// <param name="maxPlayers">The max number of players that may connect to this server.</param>
        /// <param name="regionId">The region to allocate the server in. May be null.</param>
        public async Task<bool> AllocateRelayServerAsync(int maxPlayers, string regionId) {
#if UTP_RELAY
            if(!await InitializeUnityServicesAsync()) { return false; }

            //Note: List of regions here https://docs.unity.com/ugs/manual/relay/manual/locations-and-regions
            if(!string.IsNullOrWhiteSpace(regionId)) {
                List<Region> listRegions = await RelayService.Instance.ListRegionsAsync();
                if(listRegions == null || listRegions.Count == 0) {
                    regionId = "";
                    UTPLog.Warning($"Unable to retrieve the list of Relay regions, will use most optimal region instead.");
                } else if(listRegions.Find(x => x.Id == regionId) == null) {
                    regionId = "";
                    UTPLog.Warning($"Invalid Relay Region ID, will use most optimal region instead.");
                }
            }

            RelayServerAllocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers, regionId);
            if(RelayServerAllocation == null) {
                UTPLog.Error($"Unable to allocate Relay Server.");
                return false;
            }
            
            RelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(RelayServerAllocation.AllocationId);
            if(string.IsNullOrWhiteSpace(RelayJoinCode)) {
                RelayServerAllocation = null;
                UTPLog.Error($"Unable to allocate Relay Server, encountered an error retrieving the Join Code.");
                return false;
            }

            UTPLog.Info($"Relay Server Allocated | Region: {RelayServerAllocation.Region} | Join Code: {RelayJoinCode}");

            return true;
#else
            UTPLog.Error($"Failed to allocate Relay Server: UTP assembly references are missing.");
            return false;
#endif
        }

        /// <summary>
        /// Retrieves the <seealso cref="JoinAllocation"/> corresponding to the specified join code.
        /// </summary>
        /// <param name="joinCode">The join code that will be used to retrieve the JoinAllocation.</param>
        public async Task<bool> AllocateRelayClientAsync(string joinCode) {
#if UTP_RELAY
            if(!await InitializeUnityServicesAsync()) { return false; }

            RelayClientAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            if(RelayClientAllocation == null) {
                UTPLog.Error($"Failed to allocate Relay Client from Join Code: {joinCode}");
                return false;
            }

            return true;
#else
            UTPLog.Error($"Failed to allocate Relay Client: UTP assembly references are missing.");
            return false;
#endif
        }
#pragma warning restore
    }
}

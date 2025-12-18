using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    [Serializable]
    public struct ServerLoginResponse : IPackedAuto
    {
        [JsonProperty]
        public PlayerID playerId { get; }

        [JsonProperty]
        public NetworkID lastNidId { get; }

        public ServerLoginResponse(PlayerID playerId, NetworkID lastNidId)
        {
            this.playerId = playerId;
            this.lastNidId = lastNidId;
        }
    }

    [Serializable]
    public struct PlayerJoinedEvent : IPackedAuto
    {
        [JsonProperty]
        public PlayerID playerId { get; }

        [JsonProperty]
        public Connection connection { get; }

        [JsonProperty]
        public NetworkID? lastNidId { get; }

        [JsonProperty]
        public string cookie { get; }

        public PlayerJoinedEvent(PlayerID playerId, Connection connection, NetworkID? lastNid, string cookie)
        {
            this.playerId = playerId;
            this.connection = connection;
            this.lastNidId = lastNid;
            this.cookie = cookie;
        }
    }

    [Serializable]
    public struct PlayerLeftEvent : IPackedAuto
    {
        [JsonProperty]
        public PlayerID playerId { get; }

        public PlayerLeftEvent(PlayerID playerId)
        {
            this.playerId = playerId;
        }
    }

    [Serializable]
    public struct PlayerSnapshotEvent : IPackedAuto
    {
        [JsonProperty]
        public DisposableList<PlayerJoinedEvent> events { get; }

        public PlayerSnapshotEvent(DisposableList<PlayerJoinedEvent> snapshot)
        {
            this.events = snapshot;
        }
    }

    public delegate void OnPlayerJoinedEvent(PlayerID player, bool isReconnect, bool asServer);

    public delegate void OnPlayerLeftEvent(PlayerID player, bool asServer);

    public delegate void OnPlayerEvent(PlayerID player);

    public class PlayersManager : INetworkModule, IConnectionListener, IPlayerBroadcaster, IPromoteToServerModule, ITransferToNewServer, IPostTransferToNewServer
    {
        private readonly AuthModule _authModule;
        private readonly BroadcastModule _broadcastModule;
        private readonly ITransport _transport;
        private readonly NetworkManager _networkManager;

        private readonly Dictionary<string, PlayerID> _cookieToPlayerId = new Dictionary<string, PlayerID>();
        private readonly Dictionary<PlayerID, string> _playerIdToCookie = new Dictionary<PlayerID, string>();
        private ulong _playerIdCounter;

        private readonly Dictionary<Connection, PlayerID>
            _connectionToPlayerId = new Dictionary<Connection, PlayerID>();

        private readonly Dictionary<PlayerID, Connection> _playerToConnection = new Dictionary<PlayerID, Connection>();

        private readonly List<PlayerID> _players = new List<PlayerID>();
        private readonly HashSet<PlayerID> _allSeenPlayers = new HashSet<PlayerID>();

        public IReadOnlyList<PlayerID> players => _players;

        public PlayerID? localPlayerId { get; private set; }

        public NetworkID? lastNid { get; private set; }

        public int GetMTU(PlayerID player, Channel channel, bool asServer)
        {
            if (_playerToConnection.TryGetValue(player, out var p))
                return _networkManager.transport.transport.GetMTU(p, channel, asServer);
            return 1024;
        }

        /// <summary>
        /// First callback for whne a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPrePlayerJoined;

        /// <summary>
        /// Callback for when a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPlayerJoined;

        /// <summary>
        /// Last callback for when a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPostPlayerJoined;

        /// <summary>
        /// First callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPrePlayerLeft;

        /// <summary>
        /// Callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPlayerLeft;

        /// <summary>
        /// Last callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPostPlayerLeft;

        /// <summary>
        /// Callback for when the local player has received their PlayerID
        /// </summary>
        public event OnPlayerEvent onLocalPlayerReceivedID;

        public event Action<NetworkID> onNetworkIDReceived;

        private bool _asServer;

        private PlayersBroadcaster _playerBroadcaster;

        internal void SetBroadcaster(PlayersBroadcaster broadcaster)
        {
            _playerBroadcaster = broadcaster;
        }

        public void SendRaw(PlayerID player, ByteData data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.SendRaw(player, data, method);

        public void SendRaw(IReadOnlyList<PlayerID> player, ByteData data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.SendRaw(player, data, method);

        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.Send(player, data, method);

        public void Send<T>(IReadOnlyList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.Send(collection, data, method);

        public void SendList<T>(IList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.Send(collection, data, method);

        public void Send<T>(IEnumerable<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.Send(collection, data, method);

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.SendToServer(data, method);

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered)
            => _playerBroadcaster.SendToAll(data, method);

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Unsubscribe(callback);

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Subscribe(callback);

        public PlayersManager(NetworkManager nm, AuthModule auth, BroadcastModule broadcaster)
        {
            _networkManager = nm;
            _transport = nm.transport.transport;
            _authModule = auth;
            _broadcastModule = broadcaster;
        }

        /// <summary>
        /// Try to get the connection of a playerId.
        /// For bots, this will always return false.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="conn"></param>
        /// <returns>The network connection tied to this player</returns>
        public bool TryGetConnection(PlayerID playerId, out Connection conn)
        {
            if (playerId.isBot)
            {
                conn = default;
                return false;
            }

            return _playerToConnection.TryGetValue(playerId, out conn);
        }

        /// <summary>
        /// Check if a playerId is connected to the server.
        /// </summary>
        /// <param name="playerId">PlayerID to check</param>
        /// <returns>Whether the player is connected</returns>
        public bool IsPlayerConnected(PlayerID playerId)
        {
            return _playerToConnection.ContainsKey(playerId);
        }

        /// <summary>
        /// Try to get the playerId of a connection.
        /// </summary>
        public bool TryGetPlayer(Connection conn, out PlayerID playerId)
        {
            return _connectionToPlayerId.TryGetValue(conn, out playerId);
        }

        /// <summary>
        /// Check if a playerId is the local player.
        /// </summary>
        public bool IsLocalPlayer(PlayerID playerId)
        {
            return localPlayerId == playerId;
        }

        /// <summary>
        /// Check if a playerId is the local player.
        /// </summary>
        public bool IsLocalPlayer(PlayerID? playerId)
        {
            return localPlayerId == playerId;
        }

        /// <summary>
        /// Check if a playerId is a valid player.
        /// A valid player is a player that is connected to the server.
        /// </summary>
        public bool IsValidPlayer(PlayerID playerId)
        {
            return _players.Contains(playerId);
        }

        /// <summary>
        /// Check if a playerId is a valid player.
        /// A valid player is a player that is connected to the server.
        /// </summary>
        public bool IsValidPlayer(PlayerID? playerId)
        {
            if (!playerId.HasValue)
                return false;
            return _players.Contains(playerId.Value);
        }

        /// <summary>
        /// Create a new bot player and add it to the connected players list.
        /// </summary>
        /// <returns>The playerId of the new bot player</returns>
        public PlayerID CreateBot()
        {
            if (!_asServer)
                throw new InvalidOperationException("Cannot create a bot from a client.");

            var playerId = new PlayerID(++_playerIdCounter, true);
            if (RegisterPlayer(default, playerId, out var isReconnect))
            {
                SendNewUserToAllClients(default, playerId);
                TriggerOnJoinedEvent(playerId, isReconnect);
            }
            return playerId;
        }

        /// <summary>
        /// Kick a player from the server.
        /// If the user has a connection, it will be closed.
        /// </summary>
        /// <param name="playerId"></param>
        public void KickPlayer(PlayerID playerId)
        {
            if (_playerToConnection.TryGetValue(playerId, out var conn))
                _transport.CloseConnection(conn);
            UnregisterPlayer(playerId);
            SendUserLeftToAllClients(playerId);
        }

        public void PromoteToServerModule()
        {
            Disable(false);
            _asServer = true;
            Enable(true);

            lastNid = null;
            localPlayerId = null;
        }

        public void TransferToNewServer()
        {
            lastNid = null;
            localPlayerId = null;
            for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);
        }

        public void PostTransferToNewServer()
        {
            /*for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);*/
        }

        public void PostPromoteToServerModule()
        {
            using var keys = DisposableList<Connection>.Create(_connectionToPlayerId.Keys);
            for (var i = 0; i < keys.Count; i++)
                _networkManager.TriggerConnectionLeft(keys[i], true);
            _connectionToPlayerId.Clear();
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (asServer)
            {
                _authModule.onConnection += OnClientAuthed;
            }
            else
            {
                _broadcastModule.Subscribe<ServerLoginResponse>(OnClientLoginResponse);
                _broadcastModule.Subscribe<PlayerSnapshotEvent>(OnPlayerSnapshotEvent);
                _broadcastModule.Subscribe<PlayerJoinedEvent>(OnPlayerJoinedEvent);
                _broadcastModule.Subscribe<PlayerLeftEvent>(OnPlayerLeftEvent);
            }
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _authModule.onConnection -= OnClientAuthed;
            }
            else
            {
                _broadcastModule.Unsubscribe<ServerLoginResponse>(OnClientLoginResponse);
                _broadcastModule.Unsubscribe<PlayerSnapshotEvent>(OnPlayerSnapshotEvent);
                _broadcastModule.Unsubscribe<PlayerJoinedEvent>(OnPlayerJoinedEvent);
                _broadcastModule.Unsubscribe<PlayerLeftEvent>(OnPlayerLeftEvent);
            }
        }

        /// <summary>
        /// Try to get the cookie of a playerId.
        /// Good for session management.
        /// </summary>
        public bool TryGetCookie(PlayerID playerId, out string cookie)
        {
            return _playerIdToCookie.TryGetValue(playerId, out cookie);
        }

        private void OnClientAuthed(Connection conn, AuthenticationResponse data)
        {
            if (data.cookie == null || !_cookieToPlayerId.TryGetValue(data.cookie, out var playerId))
            {
                playerId = new PlayerID(++_playerIdCounter, false);

                if (data.cookie != null)
                {
                    _cookieToPlayerId.Add(data.cookie, playerId);
                    _playerIdToCookie.Add(playerId, data.cookie);
                }
            }

            if (_players.Contains(playerId))
            {
                // Player is already connected?
                _transport.CloseConnection(conn);
                PurrLogger.LogError(
                    "Client connected using a cookie from an already connected player; closing their connection.");
                return;
            }

            var lastNidId = new NetworkID(0, playerId);
            if (_lastNidId.TryGetValue(playerId, out var lastNidRes))
                lastNidId = lastNidRes;

            _broadcastModule.Send(conn, new ServerLoginResponse(playerId, lastNidId));

            SendSnapshotToClient(conn);
            if (RegisterPlayer(conn, playerId, out var isReconnect))
            {
                SendNewUserToAllClients(conn, playerId);
                TriggerOnJoinedEvent(playerId, isReconnect);
            }
        }

        private void OnPlayerJoinedEvent(Connection conn, PlayerJoinedEvent data, bool asServer)
        {
            if (RegisterPlayer(data.connection, data.playerId, out var isReconnect))
            {
                if (data.cookie != null)
                {
                    _playerIdToCookie[data.playerId] = data.cookie;
                    _cookieToPlayerId[data.cookie] = data.playerId;
                }

                if (data.lastNidId.HasValue)
                    _lastNidId[data.playerId] = data.lastNidId.Value;

                _playerIdCounter = Math.Max(_playerIdCounter, data.playerId.id.value);

                TriggerOnJoinedEvent(data.playerId, isReconnect);
            }
        }

        private void OnPlayerLeftEvent(Connection conn, PlayerLeftEvent data, bool asServer)
        {
            UnregisterPlayer(data.playerId);
        }

        private void OnPlayerSnapshotEvent(Connection conn, PlayerSnapshotEvent data, bool asServer)
        {
            using (data.events)
            {
                for (var i = 0; i < data.events.Count; i++)
                {
                    var evt = data.events[i];
                    OnPlayerJoinedEvent(conn, evt, asServer);
                }
            }
        }

        private void OnClientLoginResponse(Connection conn, ServerLoginResponse data, bool asServer)
        {
            localPlayerId = data.playerId;
            lastNid = data.lastNidId;
            onLocalPlayerReceivedID?.Invoke(data.playerId);
            onNetworkIDReceived?.Invoke(data.lastNidId);
        }

        private void SendNewUserToAllClients(Connection conn, PlayerID playerId)
        {
            _broadcastModule.SendToAll(GetPlayerJoinEvent(playerId, conn));
        }

        private PlayerJoinedEvent GetPlayerJoinEvent(PlayerID playerId, Connection conn)
        {
            string cookie = null;
            NetworkID? playerLastNid = null;

            if (_networkManager.networkRules.IsHostMigrationEnabled())
            {
                if (_playerIdToCookie.TryGetValue(playerId, out var playerCookie))
                    cookie = playerCookie;

                if (_lastNidId.TryGetValue(playerId, out var lastNidId))
                    playerLastNid = lastNidId;
            }

            return new PlayerJoinedEvent(playerId, conn, playerLastNid, cookie);
        }

        private void SendUserLeftToAllClients(PlayerID playerId)
        {
            _broadcastModule.SendToAll(new PlayerLeftEvent(playerId));
        }

        private void SendSnapshotToClient(Connection conn)
        {
            using var batch = DisposableList<PlayerJoinedEvent>.Create(_players.Count);
            foreach (var (playerId, playerConn) in _playerToConnection)
                batch.Add(GetPlayerJoinEvent(playerId, playerConn));
            _broadcastModule.Send(conn, new PlayerSnapshotEvent(batch));
        }

        private bool RegisterPlayer(Connection conn, PlayerID player, out bool isReconnect)
        {
            if (_connectionToPlayerId.ContainsKey(conn))
            {
                isReconnect = false;
                return false;
            }

            _players.Add(player);

            if (conn.isValid)
            {
                _connectionToPlayerId.Add(conn, player);
                _playerToConnection.Add(player, conn);
            }

            isReconnect = !_allSeenPlayers.Add(player);
            return true;
        }

        private void TriggerOnJoinedEvent(PlayerID player, bool isReconnect)
        {
            onPrePlayerJoined?.Invoke(player, isReconnect, _asServer);
            onPlayerJoined?.Invoke(player, isReconnect, _asServer);
            onPostPlayerJoined?.Invoke(player, isReconnect, _asServer);
        }

        private void UnregisterPlayer(Connection conn)
        {
            if (!_connectionToPlayerId.TryGetValue(conn, out var player))
                return;

            onPrePlayerLeft?.Invoke(player, _asServer);

            _players.Remove(player);
            _playerToConnection.Remove(player);
            _connectionToPlayerId.Remove(conn);

            onPlayerLeft?.Invoke(player, _asServer);
            onPostPlayerLeft?.Invoke(player, _asServer);
        }

        private void UnregisterPlayer(PlayerID playerId)
        {
            onPrePlayerLeft?.Invoke(playerId, _asServer);

            if (_playerToConnection.TryGetValue(playerId, out var conn))
                _connectionToPlayerId.Remove(conn);
            _players.Remove(playerId);
            _playerToConnection.Remove(playerId);

            onPlayerLeft?.Invoke(playerId, _asServer);
            onPostPlayerLeft?.Invoke(playerId, _asServer);
        }

        public void OnConnected(Connection conn, bool asServer)
        {
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
            if (!asServer) return;

            if (_connectionToPlayerId.TryGetValue(conn, out var playerId))
                SendUserLeftToAllClients(playerId);

            UnregisterPlayer(conn);
        }

        readonly Dictionary<PlayerID, NetworkID> _lastNidId = new Dictionary<PlayerID, NetworkID>();

        public void RegisterClientLastId(PlayerID player, NetworkID lastNidID)
        {
            _lastNidId[player] = lastNidID;
        }
    }
}

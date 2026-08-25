using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    internal struct HostMigrationClientReadyPacket : IPackedAuto
    {
        public string sessionId;
        public uint epoch;
    }

    internal struct HostMigrationClientReadyAcceptedPacket : IPackedAuto
    {
        public string sessionId;
        public uint epoch;
    }

    internal struct HostMigrationSessionAdvertisement : IPackedAuto
    {
        public string sessionId;
        public uint epoch;
    }

    [Serializable]
    public struct ServerLoginResponse : IPackedAuto
    {
        [JsonProperty]
        public PlayerID playerId { get; }

        [JsonProperty]
        public NetworkID lastNidId { get; }

        [JsonProperty]
        public string cookie { get; }

        public ServerLoginResponse(PlayerID playerId, NetworkID lastNidId, string cookie = null)
        {
            this.playerId = playerId;
            this.lastNidId = lastNidId;
            this.cookie = cookie;
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

        /// <summary>
        /// Optional application authentication cookie. It is shared only when the server's
        /// host-migration rules explicitly opt in.
        /// </summary>
        [JsonProperty]
        public string cookie { get; }

        public PlayerJoinedEvent(PlayerID playerId, Connection connection, NetworkID? lastNid,
            string cookie = null)
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

    public class PlayersManager : INetworkModule, IConnectionListener, IConnectionStateListener, IPlayerBroadcaster, IPromoteToServerModule, ITransferToNewServer, IPostTransferToNewServer
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
        private readonly HashSet<int> _promotedStaleConnectionIds = new HashSet<int>();
        private readonly HashSet<PlayerID> _expectedHostMigrationPlayers = new HashSet<PlayerID>();
        private readonly HashSet<PlayerID> _readyHostMigrationPlayers = new HashSet<PlayerID>();
        private readonly List<PlayerID> _pendingHostMigrationPlayers = new List<PlayerID>();
        private readonly ReadOnlyCollection<PlayerID> _pendingHostMigrationPlayersView;
        private readonly List<PlayerID> _retainedHostMigrationPlayers = new List<PlayerID>();
        private readonly ReadOnlyCollection<PlayerID> _retainedHostMigrationPlayersView;
        private HostMigrationTransitionOptions _hostMigrationRosterTransition;
        private HostMigrationTransitionOptions _transferReconciliationTransition;
        private HostMigrationTransitionOptions _hostMigrationReadyAcceptedTransition;
        private PlayerID? _retainedTransferLocalPlayerId;
        private string _transferReconciliationFailure;
        private bool _hasValidatedTransferSnapshot;
        private PlayerID? _promotedLocalPlayerId;

        public IReadOnlyList<PlayerID> players => _players;

        internal IReadOnlyList<PlayerID> pendingHostMigrationPlayers =>
            _pendingHostMigrationPlayersView;

        internal int readyHostMigrationPlayerCount => _readyHostMigrationPlayers.Count;

        internal IReadOnlyList<PlayerID> retainedHostMigrationPlayers =>
            _retainedHostMigrationPlayersView;

        internal bool IsActiveRetainedHostMigrationPlayer(
            PlayerID player, HostMigrationTransitionOptions transition) =>
            transition.canReconcile &&
            transition == _hostMigrationRosterTransition &&
            _expectedHostMigrationPlayers.Contains(player) &&
            _retainedHostMigrationPlayers.Contains(player);

        internal bool IsPendingRetainedHostMigrationPlayer(
            PlayerID player, HostMigrationTransitionOptions transition) =>
            IsActiveRetainedHostMigrationPlayer(player, transition) &&
            _pendingHostMigrationPlayers.Contains(player);

        internal bool TryBeginExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!IsPendingRetainedHostMigrationPlayer(player, transition))
            {
                failure = $"player {player} is not pending in migration {transition}";
                return false;
            }

            if (_playerBroadcaster == null)
            {
                failure = "the server player broadcaster is unavailable";
                return false;
            }

            return _playerBroadcaster.BeginExactOutboundBarrier(player, transition, out failure);
        }

        internal void DropExactOutboundBarrier(PlayerID player)
        {
            _playerBroadcaster?.DropExactOutboundBarrier(player);
        }

        internal void RejectExactOutboundConnection(PlayerID player,
            HostMigrationTransitionOptions transition, string failure)
        {
            RejectUnfencedHostMigrationConnectionRebound(player, transition, failure);
        }

        internal bool SendExactBarrierBypass<T>(PlayerID player,
            HostMigrationTransitionOptions transition, T data,
            Channel method = Channel.ReliableOrdered)
        {
            if (_playerBroadcaster == null ||
                !_playerBroadcaster.HasExactOutboundBarrier(player, transition))
                return false;

            _playerBroadcaster.SendExactBarrierBypass(player, data, method);
            return true;
        }

        internal bool HasExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition) =>
            _playerBroadcaster != null &&
            _playerBroadcaster.HasExactOutboundBarrier(player, transition);

        internal bool BeginExactPackageBaselineCapture(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!IsPendingRetainedHostMigrationPlayer(player, transition) ||
                _playerBroadcaster == null)
            {
                failure = $"player {player} is not an active exact package-baseline target for {transition}";
                return false;
            }

            return _playerBroadcaster.BeginExactPackageBaselineCapture(
                player, transition, out failure);
        }

        internal bool FinishExactPackageBaselineCapture(PlayerID player,
            HostMigrationTransitionOptions transition, bool commit, out string failure)
        {
            if (_playerBroadcaster == null)
            {
                failure = "the server player broadcaster is unavailable";
                return false;
            }

            return _playerBroadcaster.FinishExactPackageBaselineCapture(
                player, transition, commit, out failure);
        }

        internal bool PublishExactPackageBaselines(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (_playerBroadcaster == null)
            {
                failure = "the server player broadcaster is unavailable";
                return false;
            }

            return _playerBroadcaster.PublishExactPackageBaselines(
                player, transition, out failure);
        }

        internal bool RunExactOutboundBarrierBypass(PlayerID player,
            HostMigrationTransitionOptions transition, Action action) =>
            _playerBroadcaster != null &&
            _playerBroadcaster.RunExactOutboundBarrierBypass(player, transition, action);

        internal void ReleaseExactOutboundBarrier(PlayerID player,
            HostMigrationTransitionOptions transition)
        {
            _playerBroadcaster?.ReleaseExactOutboundBarrier(player, transition);
        }

        public PlayerID? localPlayerId { get; private set; }

        internal PlayerID? promotedLocalPlayerId => _promotedLocalPlayerId;

        internal void ClearPromotedLocalPlayerId() => _promotedLocalPlayerId = null;

        internal PlayerID? retainedTransferLocalPlayerId => _retainedTransferLocalPlayerId;

        internal bool TryGetOutgoingHostMigrationPlayerClaim(
            out HostMigrationPlayerClaim claim)
        {
            claim = default;
            if (_asServer || !_transferReconciliationTransition.canReconcile ||
                _transferReconciliationTransition !=
                _networkManager.expectedHostMigrationSession ||
                !_retainedTransferLocalPlayerId.HasValue ||
                !string.IsNullOrEmpty(_transferReconciliationFailure))
                return false;

            claim = new HostMigrationPlayerClaim
            {
                sessionId = _transferReconciliationTransition.sessionId,
                epoch = _transferReconciliationTransition.epoch,
                playerId = _retainedTransferLocalPlayerId.Value
            };
            return true;
        }

        public NetworkID? lastNid { get; private set; }

        public MTUExceededBehaviour mtuExceededBehaviour => _networkManager.mtuExceededBehaviour;

        public int GetMTU(PlayerID player, Channel channel, bool asServer)
        {
            if (!asServer)
            {
                return _networkManager.rawTransport.GetMTU(default, channel, false);
            }

            if (_playerToConnection.TryGetValue(player, out var p))
                return _networkManager.rawTransport.GetMTU(p, channel, true);

            return 500;
        }

        /// <summary>
        /// First callback for whne a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPrePlayerJoined;

        internal event OnPlayerJoinedEvent onPreHostMigrationConnectionRebound;

        /// <summary>
        /// Callback for when a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPlayerJoined;

        internal event OnPlayerJoinedEvent onHostMigrationConnectionRebound;

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

        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(player, data, method, mtuOverride);

        public void Send<T>(IReadOnlyList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void SendList<T>(IList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void Send<T>(IEnumerable<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.SendToServer(data, method, mtuOverride);

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.SendToAll(data, method, mtuOverride);

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Unsubscribe(callback);

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Subscribe(callback);

        internal void SendHostMigrationClientReady(HostMigrationTransitionOptions transition)
        {
            if (_asServer || !transition.canReconcile ||
                transition != _transferReconciliationTransition ||
                !string.IsNullOrEmpty(_transferReconciliationFailure) ||
                !_hasValidatedTransferSnapshot)
                return;

            SendToServer(new HostMigrationClientReadyPacket
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch
            });
        }

        internal bool HasHostMigrationClientReadyAcceptance(
            HostMigrationTransitionOptions transition) =>
            transition.canReconcile && transition == _hostMigrationReadyAcceptedTransition;

        internal bool HasValidatedHostMigrationTransferSnapshot(
            HostMigrationTransitionOptions transition) =>
            transition.canReconcile && transition == _transferReconciliationTransition &&
            _hasValidatedTransferSnapshot && string.IsNullOrEmpty(_transferReconciliationFailure);

        internal bool TryGetHostMigrationTransferFailure(
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (transition.canReconcile && transition == _transferReconciliationTransition &&
                !string.IsNullOrEmpty(_transferReconciliationFailure))
            {
                failure = _transferReconciliationFailure;
                return true;
            }

            failure = null;
            return false;
        }

        internal void ResetHostMigrationTransferReconciliation()
        {
            _transferReconciliationTransition = default;
            _hostMigrationReadyAcceptedTransition = default;
            _retainedTransferLocalPlayerId = null;
            _transferReconciliationFailure = null;
            _hasValidatedTransferSnapshot = false;
        }

        private void RecordHostMigrationTransferFailure(string failure)
        {
            if (!_transferReconciliationTransition.canReconcile ||
                !string.IsNullOrEmpty(_transferReconciliationFailure))
                return;

            _transferReconciliationFailure = failure;
        }

        private void OnHostMigrationClientReady(PlayerID player,
            HostMigrationClientReadyPacket packet, bool asServer)
        {
            if (!_asServer || !asServer)
                return;

            var transition = new HostMigrationTransitionOptions(packet.sessionId, packet.epoch);
            if (!transition.canReconcile || transition != _networkManager.hostMigrationSession)
                return;

            if (!_networkManager.NotifyHostMigrationPlayerReady(player, transition))
                return;

            Send(player, new HostMigrationClientReadyAcceptedPacket
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch
            }, Channel.ReliableOrdered);
        }

        private void OnHostMigrationClientReadyAccepted(PlayerID player,
            HostMigrationClientReadyAcceptedPacket packet, bool asServer)
        {
            if (_asServer || asServer)
                return;

            var transition = new HostMigrationTransitionOptions(packet.sessionId, packet.epoch);
            if (!transition.canReconcile || transition != _transferReconciliationTransition ||
                transition != _networkManager.expectedHostMigrationSession ||
                transition != _networkManager.hostMigrationSession ||
                !string.IsNullOrEmpty(_transferReconciliationFailure))
                return;

            _hostMigrationReadyAcceptedTransition = transition;
        }

        internal bool ValidateExpectedHostMigrationRoster(
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!ValidateExpectedHostMigrationTransferRoster(transition, out failure))
                return false;

            if (!transition.canReconcile)
                return true;

            var localPlayer = localPlayerId;
            if (!localPlayer.HasValue || !_players.Contains(localPlayer.Value))
            {
                failure = "A scoped host promotion requires the candidate's retained local PlayerID state.";
                return false;
            }

            return true;
        }

        internal bool ValidateExpectedHostMigrationTransferRoster(
            HostMigrationTransitionOptions transition, out string failure)
        {
            return ValidateExpectedHostMigrationTransferRoster(transition, localPlayerId, out failure);
        }

        private static bool ValidateExpectedHostMigrationTransferRoster(
            HostMigrationTransitionOptions transition, PlayerID? retainedLocalPlayer, out string failure)
        {
            failure = null;
            if (!transition.canReconcile)
                return true;

            if (!retainedLocalPlayer.HasValue)
            {
                failure =
                    "A scoped host migration requires this client's retained PlayerID.";
                return false;
            }

            for (var i = 0; i < transition.expectedPlayers.Count; i++)
            {
                if (transition.expectedPlayers[i] == retainedLocalPlayer.Value)
                    return true;
            }

            failure = $"The migration handoff roster does not contain this client's retained " +
                      $"PlayerID {retainedLocalPlayer.Value}.";
            return false;
        }

        private void BeginHostMigrationRoster(HostMigrationTransitionOptions transition)
        {
            _playerBroadcaster?.DropAllExactOutboundBarriers();
            _hostMigrationRosterTransition = transition.canReconcile ? transition : default;
            _expectedHostMigrationPlayers.Clear();
            _readyHostMigrationPlayers.Clear();
            _pendingHostMigrationPlayers.Clear();
            _retainedHostMigrationPlayers.Clear();

            if (!transition.canReconcile)
                return;

            for (var i = 0; i < transition.expectedPlayers.Count; i++)
            {
                var player = transition.expectedPlayers[i];
                if (!player.isServer && !player.isBot &&
                    player != _promotedLocalPlayerId && !_players.Contains(player))
                    continue;

                if (!_expectedHostMigrationPlayers.Add(player))
                    continue;

                _retainedHostMigrationPlayers.Add(player);

                if (!player.isServer && !player.isBot)
                    _pendingHostMigrationPlayers.Add(player);
            }

            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if ((player.isServer || player.isBot) &&
                    !_retainedHostMigrationPlayers.Contains(player))
                    _retainedHostMigrationPlayers.Add(player);
            }

        }

        internal bool AcceptHostMigrationPlayerReady(PlayerID player,
            HostMigrationTransitionOptions transition, out bool becameReady)
        {
            becameReady = false;
            if (!_hostMigrationRosterTransition.canReconcile ||
                transition != _hostMigrationRosterTransition)
                return false;

            if (!_expectedHostMigrationPlayers.Contains(player))
                return false;

            becameReady = _readyHostMigrationPlayers.Add(player);
            if (becameReady)
                _pendingHostMigrationPlayers.Remove(player);

            return true;
        }

        internal bool ConfirmHostMigrationPlayerDeparture(PlayerID player,
            HostMigrationTransitionOptions transition)
        {
            if (!_hostMigrationRosterTransition.canReconcile ||
                transition != _hostMigrationRosterTransition ||
                !_expectedHostMigrationPlayers.Contains(player))
                return false;

            _expectedHostMigrationPlayers.Remove(player);
            _readyHostMigrationPlayers.Remove(player);
            _pendingHostMigrationPlayers.Remove(player);
            _retainedHostMigrationPlayers.Remove(player);

            if (_playerToConnection.TryGetValue(player, out var connection))
                _transport.CloseConnection(connection);

            if (_players.Contains(player))
            {
                UnregisterPlayer(player);
                SendUserLeftToAllClients(player);
            }

            return true;
        }

        internal int FinalizeHostMigrationRoster(HostMigrationTransitionOptions transition,
            IReadOnlyList<PlayerID> activePlayers)
        {
            if (!_hostMigrationRosterTransition.canReconcile ||
                transition != _hostMigrationRosterTransition)
                return 0;

            var active = new HashSet<PlayerID>();
            for (var i = 0; i < activePlayers.Count; i++)
                active.Add(activePlayers[i]);

            var expected = new List<PlayerID>(_expectedHostMigrationPlayers);
            var removed = 0;
            for (var i = 0; i < expected.Count; i++)
            {
                if (!active.Contains(expected[i]) &&
                    ConfirmHostMigrationPlayerDeparture(expected[i], transition))
                    removed++;
            }

            return removed;
        }

        internal void RegisterImmediateType<T>()
            => _broadcastModule.RegisterImmediateType<T>();

        internal void UnregisterImmediateType<T>()
            => _broadcastModule.UnregisterImmediateType<T>();

        public PlayersManager(NetworkManager nm, AuthModule auth, BroadcastModule broadcaster)
        {
            _networkManager = nm;
            _transport = nm.transport.transport;
            _authModule = auth;
            _broadcastModule = broadcaster;
            _pendingHostMigrationPlayersView = _pendingHostMigrationPlayers.AsReadOnly();
            _retainedHostMigrationPlayersView = _retainedHostMigrationPlayers.AsReadOnly();
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
            _promotedLocalPlayerId = localPlayerId;
            BeginHostMigrationRoster(_networkManager.hostMigrationSession);
            Disable(false);
            _asServer = true;
            Enable(true);

            lastNid = null;
            localPlayerId = null;
        }

        public void TransferToNewServer()
        {
            var retainedLocalPlayerId = localPlayerId;
            PlayerID? promotedServerLocalPlayerId = null;
            if (_networkManager.isPromotingToServer &&
                _networkManager.TryGetModule(out PlayersManager promotedServerPlayers, true))
            {
                promotedServerLocalPlayerId = promotedServerPlayers.promotedLocalPlayerId;
            }
            retainedLocalPlayerId = ResolveRetainedTransferLocalPlayer(
                retainedLocalPlayerId,
                _networkManager.isPromotingToServer,
                promotedServerLocalPlayerId);
            ResetHostMigrationTransferReconciliation();

            var transition = _networkManager.expectedHostMigrationSession;
            _transferReconciliationTransition = transition.canReconcile ? transition : default;
            _retainedTransferLocalPlayerId = retainedLocalPlayerId;

            if (transition.canReconcile)
            {
                if (!ValidateExpectedHostMigrationTransferRoster(
                        transition, retainedLocalPlayerId, out var validationFailure))
                {
                    RecordHostMigrationTransferFailure(validationFailure);
                    return;
                }
            }

            lastNid = null;
            localPlayerId = null;

            if (!transition.canReconcile)
            {
                for (var i = _players.Count - 1; i >= 0; i--)
                    UnregisterPlayer(_players[i]);
                return;
            }

            _connectionToPlayerId.Clear();
            _playerToConnection.Clear();
        }

        internal static PlayerID? ResolveRetainedTransferLocalPlayer(
            PlayerID? currentLocalPlayer,
            bool isPromotingToServer,
            PlayerID? promotedServerLocalPlayer)
        {
            if (currentLocalPlayer.HasValue || !isPromotingToServer)
                return currentLocalPlayer;

            return promotedServerLocalPlayer;
        }

        public void PostTransferToNewServer()
        {
            _retainedTransferLocalPlayerId = null;
        }

        public void PostPromoteToServerModule()
        {
            using var keys = DisposableList<Connection>.Create(_connectionToPlayerId.Keys);
            for (var i = 0; i < keys.Count; i++)
            {
                if (_connectionToPlayerId.TryGetValue(keys[i], out var playerId) &&
                    ((_promotedLocalPlayerId.HasValue && playerId == _promotedLocalPlayerId.Value) ||
                     (_hostMigrationRosterTransition.canReconcile &&
                      _expectedHostMigrationPlayers.Contains(playerId))))
                {
                    _connectionToPlayerId.Remove(keys[i]);
                    _playerToConnection.Remove(playerId);
                    _promotedStaleConnectionIds.Add(keys[i].connectionId);
                    continue;
                }

                _networkManager.TriggerConnectionLeft(keys[i], true);
            }

            _connectionToPlayerId.Clear();

            if (_hostMigrationRosterTransition.canReconcile)
            {
                for (var i = _players.Count - 1; i >= 0; i--)
                {
                    var player = _players[i];
                    if (player.isServer || player.isBot ||
                        _expectedHostMigrationPlayers.Contains(player))
                        continue;

                    UnregisterPlayer(player);
                    SendUserLeftToAllClients(player);
                }
            }
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (asServer)
            {
                _authModule.onConnection += OnClientAuthed;
                Subscribe<HostMigrationClientReadyPacket>(OnHostMigrationClientReady);
            }
            else
            {
                Subscribe<HostMigrationClientReadyAcceptedPacket>(OnHostMigrationClientReadyAccepted);
                _broadcastModule.Subscribe<HostMigrationSessionAdvertisement>(
                    OnHostMigrationSessionAdvertisement);
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
                Unsubscribe<HostMigrationClientReadyPacket>(OnHostMigrationClientReady);
            }
            else
            {
                Unsubscribe<HostMigrationClientReadyAcceptedPacket>(OnHostMigrationClientReadyAccepted);
                _broadcastModule.Unsubscribe<HostMigrationSessionAdvertisement>(
                    OnHostMigrationSessionAdvertisement);
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
            PlayerID playerId = default;
            var hasMigrationClaim = TryResolveHostMigrationPlayerClaim(
                conn, data.cookie, out playerId, out var claimedCurrentSession);
            var hasKnownCookie = !hasMigrationClaim && data.cookie != null &&
                                 _cookieToPlayerId.TryGetValue(data.cookie, out playerId);
            var cookieBelongsToPendingPlayer = hasKnownCookie &&
                                               _pendingHostMigrationPlayers.Contains(playerId);

            if (cookieBelongsToPendingPlayer)
                hasKnownCookie = false;

            if (!hasKnownCookie && !hasMigrationClaim)
            {
                playerId = new PlayerID(++_playerIdCounter, false);
            }

            var cookieReservedForOtherPendingPlayer = data.cookie != null &&
                _cookieToPlayerId.TryGetValue(data.cookie, out var reservedOwner) &&
                reservedOwner != playerId &&
                _pendingHostMigrationPlayers.Contains(reservedOwner);

            if (data.cookie != null && !cookieBelongsToPendingPlayer &&
                !cookieReservedForOtherPendingPlayer)
            {
                _cookieToPlayerId[data.cookie] = playerId;
                _playerIdToCookie[playerId] = data.cookie;
            }

            if (_players.Contains(playerId))
            {
                if (_playerToConnection.TryGetValue(playerId, out var oldConn) && oldConn != conn)
                {
                    if (_pendingHostMigrationPlayers.Contains(playerId))
                    {
                        _connectionToPlayerId.Remove(oldConn);
                        _playerToConnection.Remove(playerId);
                    }
                    else
                    {
                        PurrLogger.LogWarning(
                            "Client reconnected with the cookie of a still-connected player; closing their previous connection.");
                        UnregisterPlayer(playerId);
                        SendUserLeftToAllClients(playerId);
                    }
                    _transport.CloseConnection(oldConn);
                }
                else if (_playerToConnection.ContainsKey(playerId))
                {
                    _transport.CloseConnection(conn);
                    PurrLogger.LogError(
                        "Client connected using a cookie from an already connected player; closing their connection.");
                    return;
                }
            }

            var lastNidId = new NetworkID(0, playerId);
            if (_lastNidId.TryGetValue(playerId, out var lastNidRes))
                lastNidId = lastNidRes;

            var migrationSession = _networkManager.hostMigrationSession;
            if (migrationSession.canReconcile && claimedCurrentSession)
            {
                _broadcastModule.Send(conn, new HostMigrationSessionAdvertisement
                {
                    sessionId = migrationSession.sessionId,
                    epoch = migrationSession.epoch
                }, Channel.ReliableOrdered);
            }

            _broadcastModule.Send(conn,
                new ServerLoginResponse(playerId, lastNidId, data.cookie),
                Channel.ReliableOrdered);

            SendSnapshotToClient(conn);
            var retainedLogicalMember = _hostMigrationRosterTransition.canReconcile &&
                                        _expectedHostMigrationPlayers.Contains(playerId) &&
                                        _players.Contains(playerId);
            if (IsPlayerConnection(conn, playerId))
            {
                SendNewUserToAllClients(conn, playerId);
                if (!retainedLogicalMember)
                    TriggerOnJoinedEvent(playerId, true);
            }
            else if (RegisterPlayer(conn, playerId, out var isReconnect))
            {
                SendNewUserToAllClients(conn, playerId);
                if (retainedLogicalMember)
                    TriggerHostMigrationConnectionRebound(playerId);
                else
                    TriggerOnJoinedEvent(playerId, isReconnect);
            }
        }

        private bool TryResolveHostMigrationPlayerClaim(Connection conn, string presentedCookie,
            out PlayerID playerId, out bool claimedCurrentSession)
        {
            playerId = default;
            claimedCurrentSession = false;
            if (!_authModule.TryTakeHostMigrationPlayerClaim(conn, out var claim))
                return false;

            var transition = new HostMigrationTransitionOptions(claim.sessionId, claim.epoch);
            if (!transition.canReconcile || transition != _hostMigrationRosterTransition ||
                transition != _networkManager.hostMigrationSession)
                return false;

            claimedCurrentSession = true;

            if (claim.playerId.isServer || claim.playerId.isBot ||
                !_expectedHostMigrationPlayers.Contains(claim.playerId) ||
                !_pendingHostMigrationPlayers.Contains(claim.playerId) ||
                _playerToConnection.TryGetValue(claim.playerId, out var activeConnection) &&
                activeConnection != conn)
                return false;

            if (_playerIdToCookie.TryGetValue(claim.playerId, out var expectedCookie) &&
                !string.IsNullOrEmpty(expectedCookie) && expectedCookie != presentedCookie)
            {
                PurrLogger.LogWarning(
                    $"Rejected a host migration claim for {claim.playerId}: the connection's " +
                    "cookie does not match the retained player's cookie.");
                return false;
            }

            playerId = claim.playerId;
            return true;
        }

        private void OnPlayerJoinedEvent(Connection conn, PlayerJoinedEvent data, bool asServer)
        {
            if (_transferReconciliationTransition.canReconcile &&
                (!_hasValidatedTransferSnapshot ||
                 !string.IsNullOrEmpty(_transferReconciliationFailure)))
                return;

            var suppressLifecycleReplay = _players.Contains(data.playerId);
            if (!string.IsNullOrEmpty(data.cookie))
            {
                _cookieToPlayerId[data.cookie] = data.playerId;
                _playerIdToCookie[data.playerId] = data.cookie;
            }

            if (RegisterPlayer(data.connection, data.playerId, out var isReconnect))
            {
                if (data.lastNidId.HasValue)
                    _lastNidId[data.playerId] = data.lastNidId.Value;

                _playerIdCounter = Math.Max(_playerIdCounter, data.playerId.id.value);

                if (!suppressLifecycleReplay)
                    TriggerOnJoinedEvent(data.playerId, isReconnect);
            }
        }

        private void OnPlayerLeftEvent(Connection conn, PlayerLeftEvent data, bool asServer)
        {
            if (_networkManager.isPromotingToServer || _networkManager.isTranferingToNewServer ||
                _networkManager.isPreservingClientStateForHostMigration)
                return;

            if (_transferReconciliationTransition.canReconcile &&
                (!_hasValidatedTransferSnapshot ||
                 !string.IsNullOrEmpty(_transferReconciliationFailure)))
                return;

            UnregisterPlayer(data.playerId);
        }

        private void OnPlayerSnapshotEvent(Connection conn, PlayerSnapshotEvent data, bool asServer)
        {
            using (data.events)
            {
                if (_transferReconciliationTransition.canReconcile &&
                    !_hasValidatedTransferSnapshot)
                {
                    if (!string.IsNullOrEmpty(_transferReconciliationFailure))
                        return;

                    var snapshotPlayers = new HashSet<PlayerID>();
                    for (var i = 0; i < data.events.Count; i++)
                    {
                        var snapshotPlayer = data.events[i].playerId;
                        if (snapshotPlayers.Add(snapshotPlayer))
                            continue;

                        RecordHostMigrationTransferFailure(
                            $"The new host's first authoritative player snapshot contained " +
                            $"duplicate PlayerID {snapshotPlayer}.");
                        return;
                    }

                    if (_retainedTransferLocalPlayerId.HasValue &&
                        !snapshotPlayers.Contains(_retainedTransferLocalPlayerId.Value))
                    {
                        RecordHostMigrationTransferFailure(
                            $"The new host's first authoritative player snapshot omitted this " +
                            $"client's assigned PlayerID {_retainedTransferLocalPlayerId.Value}.");
                        return;
                    }

                    for (var i = _players.Count - 1; i >= 0; i--)
                    {
                        var retainedPlayer = _players[i];
                        if (!snapshotPlayers.Contains(retainedPlayer))
                            UnregisterPlayer(retainedPlayer);
                    }

                    _hasValidatedTransferSnapshot = true;
                }

                if (!string.IsNullOrEmpty(_transferReconciliationFailure))
                    return;

                for (var i = 0; i < data.events.Count; i++)
                {
                    var evt = data.events[i];
                    OnPlayerJoinedEvent(conn, evt, asServer);
                }
            }
        }

        private void OnHostMigrationSessionAdvertisement(Connection conn,
            HostMigrationSessionAdvertisement data, bool asServer)
        {
            if (asServer)
                return;

            var advertisedTransition = new HostMigrationTransitionOptions(
                data.sessionId, data.epoch);
            _networkManager.ReceiveHostMigrationSession(advertisedTransition);

            if (_transferReconciliationTransition.canReconcile &&
                advertisedTransition != _transferReconciliationTransition)
            {
                RecordHostMigrationTransferFailure(
                    $"The new host advertised migration session {advertisedTransition}, but this " +
                    $"client requires {_transferReconciliationTransition}.");
                return;
            }
        }

        private void OnClientLoginResponse(Connection conn, ServerLoginResponse data, bool asServer)
        {
            if (!string.IsNullOrEmpty(_transferReconciliationFailure))
                return;

            if (_transferReconciliationTransition.canReconcile &&
                _retainedTransferLocalPlayerId.HasValue &&
                data.playerId != _retainedTransferLocalPlayerId.Value)
            {
                RecordHostMigrationTransferFailure(
                    $"The new host assigned PlayerID {data.playerId}, but exact migration " +
                    $"continuity requires retained PlayerID {_retainedTransferLocalPlayerId.Value}.");
                return;
            }

            if (!string.IsNullOrEmpty(data.cookie))
                _authModule.SetClientConnectionCookie(data.cookie);

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
            NetworkID? playerLastNid = _lastNidId.TryGetValue(playerId, out var lastNidId)
                ? lastNidId
                : (NetworkID?)null;

            string cookie = null;
            if (_networkManager.networkRules &&
                _networkManager.networkRules.ShouldSharePlayerCookiesWithPeers())
                _playerIdToCookie.TryGetValue(playerId, out cookie);

            return new PlayerJoinedEvent(playerId, conn, playerLastNid, cookie);
        }

        private void SendUserLeftToAllClients(PlayerID playerId)
        {
            _broadcastModule.SendToAll(new PlayerLeftEvent(playerId));
        }

        private void SendSnapshotToClient(Connection conn)
        {
            using var batch = DisposableList<PlayerJoinedEvent>.Create(_players.Count);
            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                _playerToConnection.TryGetValue(player, out var playerConnection);
                batch.Add(GetPlayerJoinEvent(player, playerConnection));
            }
            _broadcastModule.Send(conn, new PlayerSnapshotEvent(batch));
        }

        private bool IsPlayerConnection(Connection conn, PlayerID playerId)
        {
            return _connectionToPlayerId.TryGetValue(conn, out var registeredPlayer) &&
                   registeredPlayer == playerId;
        }

        private bool RegisterPlayer(Connection conn, PlayerID player, out bool isReconnect)
        {
            if (_connectionToPlayerId.ContainsKey(conn))
            {
                isReconnect = false;
                return false;
            }

            if (!_players.Contains(player))
                _players.Add(player);

            if (conn.isValid)
            {
                if (_playerToConnection.TryGetValue(player, out var staleConn) && staleConn != conn)
                    _connectionToPlayerId.Remove(staleConn);

                _connectionToPlayerId[conn] = player;
                _playerToConnection[player] = conn;
            }

            isReconnect = !_allSeenPlayers.Add(player);
            return true;
        }

        private void TriggerOnJoinedEvent(PlayerID player, bool isReconnect)
        {
            try
            {
                onPrePlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        private void TriggerHostMigrationConnectionRebound(PlayerID player)
        {
            var transition = _hostMigrationRosterTransition;
            if (!InvokeHostMigrationConnectionRebound(
                    onPreHostMigrationConnectionRebound, player))
            {
                RejectUnfencedHostMigrationConnectionRebound(
                    player, transition, "a pre-rebound scene manifest callback failed");
                return;
            }

            if (!TryBeginExactOutboundBarrier(player, transition, out var barrierFailure))
            {
                RejectUnfencedHostMigrationConnectionRebound(
                    player, transition, barrierFailure);
                return;
            }

            if (!InvokeHostMigrationConnectionRebound(
                    onHostMigrationConnectionRebound, player))
            {
                RejectUnfencedHostMigrationConnectionRebound(
                    player, transition, "a fenced rebound snapshot callback failed");
            }
        }

        private bool InvokeHostMigrationConnectionRebound(
            OnPlayerJoinedEvent callbacks, PlayerID player)
        {
            if (callbacks == null)
                return true;

            var succeeded = true;
            var invocationList = callbacks.GetInvocationList();
            for (var i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((OnPlayerJoinedEvent)invocationList[i]).Invoke(player, true, _asServer);
                }
                catch (Exception e)
                {
                    succeeded = false;
                    PurrLogger.LogException(e);
                }
            }

            return succeeded;
        }

        private void RejectUnfencedHostMigrationConnectionRebound(
            PlayerID player,
            HostMigrationTransitionOptions transition,
            string failure)
        {
            PurrLogger.LogError(
                $"Closing retained migration player {player} for {transition}: {failure}.");
            _playerBroadcaster?.DropExactOutboundBarrier(player);
            if (_playerToConnection.TryGetValue(player, out var connection))
                _transport.CloseConnection(connection);
        }

        private void UnregisterPlayer(Connection conn)
        {
            if (!_connectionToPlayerId.TryGetValue(conn, out var playerID))
                return;

            ForgetPlayerMigrationState(playerID);

            try
            {
                onPrePlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            _players.Remove(playerID);
            _playerToConnection.Remove(playerID);
            _connectionToPlayerId.Remove(conn);

            try
            {
                onPlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        private void UnregisterPlayer(PlayerID playerId)
        {
            ForgetPlayerMigrationState(playerId);

            try
            {
                onPrePlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            if (_playerToConnection.TryGetValue(playerId, out var conn))
                _connectionToPlayerId.Remove(conn);
            _players.Remove(playerId);
            _playerToConnection.Remove(playerId);

            try
            {
                onPlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        private void ForgetPlayerMigrationState(PlayerID player)
        {
            _playerBroadcaster?.DropExactOutboundBarrier(player);
            _expectedHostMigrationPlayers.Remove(player);
            _readyHostMigrationPlayers.Remove(player);
            _pendingHostMigrationPlayers.Remove(player);
            _retainedHostMigrationPlayers.Remove(player);
        }

        public void OnConnected(Connection conn, bool asServer)
        {
            if (asServer)
                _promotedStaleConnectionIds.Remove(conn.connectionId);
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (!asServer || state != ConnectionState.Disconnected)
                return;

            for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
            if (!asServer) return;

            if (_promotedStaleConnectionIds.Remove(conn.connectionId))
                return;

            if (_connectionToPlayerId.TryGetValue(conn, out var playerId) &&
                _pendingHostMigrationPlayers.Contains(playerId))
            {
                _connectionToPlayerId.Remove(conn);
                _playerToConnection.Remove(playerId);
                return;
            }

            if (_connectionToPlayerId.TryGetValue(conn, out playerId))
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

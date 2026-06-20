using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.HostMigration
{
    [DisallowMultipleComponent]
    [AddComponentMenu("PurrNet/Host Migration/Host Migration Orchestrator")]
    public sealed class HostMigrationOrchestrator : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private HostMigrationStrategy _strategy;
        [SerializeField] private HostMigrationEndpointProvider _endpointProvider;

        [Header("Automation")]
        [SerializeField] private bool _migrateAutomaticallyOnDisconnect = true;
        [SerializeField] private bool _ignoreClientRequestedDisconnects = true;
        [SerializeField] private bool _requireEndpointForTransfer;
        [SerializeField, Min(0f)] private float _migrationDelaySeconds = 0.25f;

        [Header("Events")]
        [SerializeField] private HostMigrationPlanEvent _onPlanSelected = new HostMigrationPlanEvent();
        [SerializeField] private HostMigrationPlanEvent _onBeforePromote = new HostMigrationPlanEvent();
        [SerializeField] private HostMigrationPlanEvent _onBeforeTransfer = new HostMigrationPlanEvent();
        [SerializeField] private HostMigrationFailureEvent _onMigrationFailed = new HostMigrationFailureEvent();

        private readonly List<PlayerID> _cachedPlayers = new List<PlayerID>();

        private NetworkManager _boundNetworkManager;
        private ITransport _boundTransport;
        private Coroutine _migrationRoutine;
        private PlayerID _cachedLocalPlayer;
        private bool _hasLocalPlayer;
        private bool _hadConnectedClient;
        private bool _wasHost;
        private bool _hasLastClientDisconnectReason;
        private DisconnectReason _lastClientDisconnectReason;

        public event Action<HostMigrationPlan> planSelected;
        public event Action<HostMigrationPlan> beforePromote;
        public event Action<HostMigrationPlan> beforeTransfer;
        public event Action<string> migrationFailed;

        public HostMigrationPlan lastPlan { get; private set; }

        public bool isMigrating
        {
            get
            {
                if (_migrationRoutine != null)
                    return true;

                return _networkManager &&
                       (_networkManager.isPromotingToServer || _networkManager.isTranferingToNewServer);
            }
        }

        private void Awake()
        {
            BindNetworkManager();
        }

        private void OnEnable()
        {
            BindNetworkManager();
        }

        private void Start()
        {
            BindNetworkManager();
            CaptureSnapshot();
        }

        private void Update()
        {
            BindNetworkManager();
            BindTransport();

            if (_networkManager && _networkManager.clientState == ConnectionState.Connected)
                CaptureSnapshot();
        }

        private void OnDisable()
        {
            if (_migrationRoutine != null)
            {
                StopCoroutine(_migrationRoutine);
                _migrationRoutine = null;
            }

            UnbindTransport();
            UnbindNetworkManager();
        }

        public void BeginMigration()
        {
            if (!isActiveAndEnabled || isMigrating)
                return;

            BindNetworkManager();
            _migrationRoutine = StartCoroutine(RunMigration());
        }

        public bool TryCreatePlan(out HostMigrationPlan plan)
        {
            return TryCreatePlan(out plan, out _);
        }

        private IEnumerator RunMigration()
        {
            if (_migrationDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(_migrationDelaySeconds);

            if (!TryCreatePlan(out var plan, out var failure))
            {
                ReportFailure(failure);
                _migrationRoutine = null;
                yield break;
            }

            lastPlan = plan;
            RaisePlanSelected(plan);

            if (plan.shouldPromote)
            {
                RaiseBeforePromote(plan);
                _networkManager.PromoteToServer();
            }
            else if (plan.shouldTransfer)
            {
                RaiseBeforeTransfer(plan);
                _networkManager.TransferToNewServer();
            }
            else
            {
                ReportFailure("Host migration plan did not select a migration action.");
            }

            _migrationRoutine = null;
        }

        private bool TryCreatePlan(out HostMigrationPlan plan, out string failure)
        {
            plan = null;
            failure = null;

            BindNetworkManager();
            CaptureSnapshot();

            if (!_networkManager)
            {
                failure = "Host migration cannot start because no NetworkManager is assigned.";
                return false;
            }

            if (!_hasLocalPlayer)
            {
                failure = "Host migration cannot start because the local player ID was not cached.";
                return false;
            }

            var players = BuildPlayerSnapshot();
            var context = new HostMigrationContext(_networkManager, _cachedLocalPlayer, _hasLocalPlayer,
                players, _wasHost, _networkManager.clientState, _networkManager.serverState);

            if (!TrySelectPromotedPlayer(context, out var promotedPlayer))
            {
                failure = "Host migration could not select a promoted player.";
                return false;
            }

            if (promotedPlayer.isServer)
            {
                failure = "Host migration selected the server pseudo-player instead of a client.";
                return false;
            }

            var decision = promotedPlayer == _cachedLocalPlayer
                ? HostMigrationDecision.PromoteToServer
                : HostMigrationDecision.TransferToServer;

            var endpoint = default(HostMigrationEndpoint);
            if (_endpointProvider)
                _endpointProvider.TryGetEndpoint(context, promotedPlayer, out endpoint);

            if (decision == HostMigrationDecision.TransferToServer &&
                _requireEndpointForTransfer && !endpoint.isValid)
            {
                failure = $"Host migration selected player {promotedPlayer}, but no endpoint was available.";
                return false;
            }

            plan = new HostMigrationPlan(decision, _cachedLocalPlayer, promotedPlayer, endpoint,
                _strategy ? _strategy.name : "Lowest player ID");
            return true;
        }

        private bool TrySelectPromotedPlayer(HostMigrationContext context, out PlayerID promotedPlayer)
        {
            if (_strategy)
                return _strategy.TrySelectPromotedPlayer(context, out promotedPlayer);

            return HostMigrationStrategy.TrySelectLowestClientPlayer(context, out promotedPlayer);
        }

        private PlayerID[] BuildPlayerSnapshot()
        {
            var players = new List<PlayerID>(_cachedPlayers.Count + 1);

            for (int i = 0; i < _cachedPlayers.Count; i++)
            {
                var player = _cachedPlayers[i];

                if (!Contains(players, player))
                    players.Add(player);
            }

            if (_hasLocalPlayer && !_cachedLocalPlayer.isServer && !Contains(players, _cachedLocalPlayer))
                players.Add(_cachedLocalPlayer);

            return players.ToArray();
        }

        private void BindNetworkManager()
        {
            if (!_networkManager)
                _networkManager = GetComponent<NetworkManager>() ?? GetComponentInParent<NetworkManager>() ?? NetworkManager.main;

            if (_boundNetworkManager == _networkManager)
                return;

            UnbindNetworkManager();
            _boundNetworkManager = _networkManager;

            if (!_boundNetworkManager)
                return;

            _boundNetworkManager.onClientConnectionState += OnClientConnectionState;
            _boundNetworkManager.onPlayerJoined += OnPlayerJoined;
            _boundNetworkManager.onPlayerLeft += OnPlayerLeft;
            _boundNetworkManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedId;

            BindTransport();
            CaptureSnapshot();
        }

        private void UnbindNetworkManager()
        {
            if (!_boundNetworkManager)
                return;

            _boundNetworkManager.onClientConnectionState -= OnClientConnectionState;
            _boundNetworkManager.onPlayerJoined -= OnPlayerJoined;
            _boundNetworkManager.onPlayerLeft -= OnPlayerLeft;
            _boundNetworkManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedId;
            _boundNetworkManager = null;
        }

        private void BindTransport()
        {
            if (!_networkManager)
                return;

            var transport = _networkManager.rawTransport ?? _networkManager.currentTransport;
            if (_boundTransport == transport)
                return;

            UnbindTransport();
            _boundTransport = transport;

            if (_boundTransport != null)
                _boundTransport.onDisconnected += OnTransportDisconnected;
        }

        private void UnbindTransport()
        {
            if (_boundTransport == null)
                return;

            _boundTransport.onDisconnected -= OnTransportDisconnected;
            _boundTransport = null;
        }

        private void OnClientConnectionState(ConnectionState state)
        {
            if (state == ConnectionState.Connected)
            {
                _hadConnectedClient = true;
                _hasLastClientDisconnectReason = false;
                CaptureSnapshot();
                return;
            }

            if (state != ConnectionState.Disconnected)
                return;

            if (!_migrateAutomaticallyOnDisconnect || isMigrating || !_hadConnectedClient || !_hasLocalPlayer)
                return;

            if (_ignoreClientRequestedDisconnects &&
                _hasLastClientDisconnectReason &&
                _lastClientDisconnectReason == DisconnectReason.ClientRequest)
                return;

            BeginMigration();
        }

        private void OnTransportDisconnected(Connection conn, DisconnectReason reason, bool asServer)
        {
            if (asServer)
                return;

            _lastClientDisconnectReason = reason;
            _hasLastClientDisconnectReason = true;
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            CaptureSnapshot();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            CaptureSnapshot();
        }

        private void OnLocalPlayerReceivedId(PlayerID player)
        {
            _cachedLocalPlayer = player;
            _hasLocalPlayer = !player.isServer;
            CaptureSnapshot();
        }

        private void CaptureSnapshot()
        {
            if (!_networkManager)
                return;

            if (_networkManager.isLocalPlayerReady)
            {
                _cachedLocalPlayer = _networkManager.localPlayer;
                _hasLocalPlayer = !_cachedLocalPlayer.isServer;
            }

            if (_networkManager.clientState != ConnectionState.Connected &&
                !_networkManager.isLocalPlayerReady)
                return;

            _hadConnectedClient = true;
            _wasHost = _networkManager.isServer && _networkManager.isClient;

            var players = _networkManager.players;
            if (players == null || players.Count == 0)
                return;

            _cachedPlayers.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];

                if (!Contains(_cachedPlayers, player))
                    _cachedPlayers.Add(player);
            }

            if (_hasLocalPlayer && !_cachedLocalPlayer.isServer && !Contains(_cachedPlayers, _cachedLocalPlayer))
                _cachedPlayers.Add(_cachedLocalPlayer);
        }

        private void RaisePlanSelected(HostMigrationPlan plan)
        {
            planSelected?.Invoke(plan);
            _onPlanSelected?.Invoke(plan);
        }

        private void RaiseBeforePromote(HostMigrationPlan plan)
        {
            beforePromote?.Invoke(plan);
            _onBeforePromote?.Invoke(plan);
        }

        private void RaiseBeforeTransfer(HostMigrationPlan plan)
        {
            beforeTransfer?.Invoke(plan);
            _onBeforeTransfer?.Invoke(plan);
        }

        private void ReportFailure(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Host migration failed.";

            Debug.LogWarning(reason, this);
            migrationFailed?.Invoke(reason);
            _onMigrationFailed?.Invoke(reason);
        }

        private static bool Contains(List<PlayerID> players, PlayerID player)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == player)
                    return true;
            }

            return false;
        }
    }
}

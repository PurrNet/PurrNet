#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    public delegate void OnTickDelegate(bool asServer);

    /// <summary>
    /// The classified reason for a completed client disconnect. Unlike the transport-level
    /// callbacks, this value also records whether PurrNet itself initiated the disconnect.
    /// </summary>
    public readonly struct ClientDisconnectInfo
    {
        public Connection connection { get; }
        public DisconnectReason reason { get; }
        public bool wasLocalRequest { get; }

        public ClientDisconnectInfo(Connection connection, DisconnectReason reason, bool wasLocalRequest)
        {
            this.connection = connection;
            this.reason = reason;
            this.wasLocalRequest = wasLocalRequest;
        }
    }

    public enum HostMigrationTransitionStatus
    {
        Succeeded,
        AlreadyInProgress,
        InvalidState,
        TimedOut,
        Cancelled,
        /// <summary>
        /// An external activation may have committed. Local roles remain ready and the
        /// caller must reconcile the authoritative service before choosing success or
        /// rollback.
        /// </summary>
        Indeterminate,
        Failed
    }

    /// <summary>
    /// Result of a host-migration role transition.
    /// </summary>
    public readonly struct HostMigrationTransitionResult
    {
        public HostMigrationTransitionStatus status { get; }
        public bool succeeded => status == HostMigrationTransitionStatus.Succeeded;
        public string message { get; }
        public Exception exception { get; }

        internal HostMigrationTransitionResult(HostMigrationTransitionStatus status, string message = null,
            Exception exception = null)
        {
            this.status = status;
            this.message = message;
            this.exception = exception;
        }
    }

    /// <summary>
    /// Identifies one authoritative host-migration session. The scope must remain stable for
    /// the lifetime of a relay/session incarnation and the epoch must advance for each host.
    /// A default value deliberately disables cross-host state reconciliation.
    /// </summary>
    public readonly struct HostMigrationTransitionOptions : IEquatable<HostMigrationTransitionOptions>
    {
        private static readonly IReadOnlyList<PlayerID> EmptyExpectedPlayers =
            Array.AsReadOnly(Array.Empty<PlayerID>());
        private readonly ReadOnlyCollection<PlayerID> _expectedPlayers;

        /// <summary>
        /// Default for <see cref="playerReclaimTimeoutSeconds"/>.
        /// </summary>
        public const float DefaultPlayerReclaimTimeoutSeconds = 60f;

        public string sessionId { get; }
        public uint epoch { get; }

        /// <summary>
        /// How long the promoted host waits for a retained player to reclaim their identity
        /// before automatically confirming their departure (releasing their identity, scene
        /// membership, and owned state to normal player-left policies). Values &lt;= 0 disable
        /// the timeout; the roster then waits until <see cref="NetworkManager.ConfirmHostMigrationPlayerDeparture"/>
        /// or <see cref="NetworkManager.FinalizeHostMigrationRoster"/> is called explicitly.
        /// Not part of session identity/equality.
        /// </summary>
        public float playerReclaimTimeoutSeconds { get; }
        /// <summary>
        /// Human-player membership observed by the leaving host at the migration cut. A promoted
        /// candidate retains only the members it also knows locally; a transferring client uses
        /// this as a continuity hint until the candidate's first authoritative snapshot arrives.
        /// Server identities and bots remain process-authoritative.
        /// </summary>
        public IReadOnlyList<PlayerID> expectedPlayers => _expectedPlayers ?? EmptyExpectedPlayers;

        /// <summary>True only when this descriptor can safely identify a migration session.</summary>
        public bool canReconcile => !string.IsNullOrWhiteSpace(sessionId) && epoch != 0;

        internal bool isDefault => string.IsNullOrEmpty(sessionId) && epoch == 0;

        public HostMigrationTransitionOptions(string sessionId, uint epoch)
            : this(sessionId, epoch, null)
        {
        }

        public HostMigrationTransitionOptions(string sessionId, uint epoch,
            IReadOnlyList<PlayerID> expectedPlayers,
            float playerReclaimTimeoutSeconds = DefaultPlayerReclaimTimeoutSeconds)
        {
            this.sessionId = sessionId;
            this.epoch = epoch;
            this.playerReclaimTimeoutSeconds = playerReclaimTimeoutSeconds;
            if (expectedPlayers == null || expectedPlayers.Count == 0)
            {
                _expectedPlayers = null;
            }
            else
            {
                var snapshot = new PlayerID[expectedPlayers.Count];
                for (var i = 0; i < expectedPlayers.Count; i++)
                    snapshot[i] = expectedPlayers[i];
                _expectedPlayers = Array.AsReadOnly(snapshot);
            }
        }

        public bool Equals(HostMigrationTransitionOptions other)
        {
            return epoch == other.epoch &&
                   string.Equals(sessionId, other.sessionId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) =>
            obj is HostMigrationTransitionOptions other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((sessionId != null ? StringComparer.Ordinal.GetHashCode(sessionId) : 0) * 397) ^
                       (int)epoch;
            }
        }

        public static bool operator ==(HostMigrationTransitionOptions left,
            HostMigrationTransitionOptions right) => left.Equals(right);

        public static bool operator !=(HostMigrationTransitionOptions left,
            HostMigrationTransitionOptions right) => !left.Equals(right);

        public override string ToString() =>
            canReconcile ? $"{sessionId}@{epoch}" : "none";
    }

    /// <summary>
    /// Optional package-level reconciliation lifecycle for a retained identity or NetworkModule.
    /// The synchronous begin hook runs before the new transport can dispatch authoritative state,
    /// while the returned task completes only after the package has consumed enough of that state
    /// to resume safely. PurrNet waits for it before transfer succeeds.
    /// </summary>
    public interface IHostMigrationReconciliationParticipant
    {
        void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition);

        Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition);
    }

    /// <summary>
    /// Optional package-level readiness barrier for an identity or NetworkModule that is retained
    /// while this client is promoted in place. PurrNet does not admit remote clients or report the
    /// promoted host ready until every participant has completed successfully.
    /// </summary>
    public interface IHostMigrationPromotionParticipant
    {
        Task ReconcileHostMigrationPromotionAsync(HostMigrationTransitionOptions transition);
    }

    /// <summary>
    /// Optional server-side package baseline emitted for one retained migration player. PurrNet
    /// invokes this only while that player's exact outbound barrier is in its pre-topology capture
    /// phase. Targeted messages queued synchronously by the hook are fully serialized and staged;
    /// they are published ReliableOrdered after every topology body and before public replay.
    /// </summary>
    /// <remarks>
    /// Implementations must emit only state for <paramref name="player"/>, must not await or retain
    /// the call, and should throw if they cannot produce a complete authoritative baseline. Normal
    /// gameplay sends remain outside this contract and stay behind the readiness barrier.
    /// </remarks>
    public interface IHostMigrationServerBaselineParticipant
    {
        void PrepareHostMigrationServerBaseline(PlayerID player,
            HostMigrationTransitionOptions transition);
    }

    /// <summary>
    /// Root-scoped contract for a reconciliation participant whose readiness task authoritatively
    /// reconciles the package-managed/manual NetworkIdentity roots it claims. PurrNet keeps only
    /// claimed roots out of the automatic spawn/prune manifest and fails a scoped transfer when a
    /// manual root has no owner.
    /// </summary>
    public interface IHostMigrationManualHierarchyParticipant :
        IHostMigrationReconciliationParticipant
    {
        bool OwnsHostMigrationManualRoot(NetworkIdentity root);
    }

    [DefaultExecutionOrder(-999)]
    [AddComponentMenu("PurrNet/Network Manager")]
    public sealed partial class NetworkManager : MonoBehaviour, IRegisterModules, INetworkManager
    {
        private PlayerID? _preparingHostMigrationBaselinePlayer;
        private HostMigrationTransitionOptions _preparingHostMigrationBaselineTransition;

        /// <summary>
        /// The main instance of the network manager.
        /// </summary>
        [UsedImplicitly]
        public static NetworkManager main { get; private set; }

        [Header("Misc Settings")]
        [Tooltip("Whether the client should stop playing when it disconnects from the server.")]
        [SerializeField]
        private bool _stopPlayingOnDisconnect;

        [Header("Auto Start Settings")]
        [Tooltip("The flags to determine when the server should automatically start.")]
        [SerializeField]
        private StartFlags _startServerFlags = StartFlags.ServerBuild | StartFlags.Editor;

        [Tooltip("The flags to determine when the client should automatically start.")] [SerializeField]
        private StartFlags _startClientFlags = StartFlags.ClientBuild | StartFlags.Editor | StartFlags.Clone;

        [Header("Persistence Settings")] [PurrDocs("systems-and-modules/network-manager"), PurrLock] [SerializeField]
        private CookieScope _cookieScope = CookieScope.LiveWithProcess;

        [Header("Network Settings")]
        [Tooltip("Whether the network manager should not be destroyed on load. " +
                 "If true, the network manager will be moved to the DontDestroyOnLoad scene.")]
        [SerializeField]
        private bool _dontDestroyOnLoad;

        [PurrDocs("systems-and-modules/transports")] [SerializeField]
        private GenericTransport _transport;

        [PurrDocs("systems-and-modules/network-manager/network-prefabs")] [SerializeField]
        private NetworkPrefabs _networkPrefabs;

#if ADDRESSABLES_PURRNET_SUPPORT
        //[PurrDocs("systems-and-modules/addressables/addressable-spawning-and-despawning")] //TODO: Add this in the future
        [SerializeField]
        private AddressableNetworkPrefabs _addressableNetworkPrefabs;
#endif

        [PurrDocs("systems-and-modules/network-manager/network-assets")] [SerializeField]
        private NetworkAssets _networkAssets;

        [PurrDocs("systems-and-modules/network-manager/network-rules")] [SerializeField]
        private NetworkRules _networkRules;

        [PurrDocs("systems-and-modules/network-manager/network-visibility")] [SerializeField]
        private NetworkVisibilityRuleSet _visibilityRules;

        [PurrDocs("systems-and-modules/network-manager/authentication")] [SerializeField]
        private AuthenticationLayer _authenticator;

        [Tooltip("Number of target ticks per second.")] [SerializeField]
        private int _tickRate = 20;

        [Tooltip("What to do when a packet exceeds the MTU on an unreliable channel.")]
        [SerializeField]
        private MTUExceededBehaviour _mtuExceededBehaviour = MTUExceededBehaviour.Fragment;

        [SerializeField, UsedImplicitly] private bool _patchLingeringProcessBug;

        /// <summary>
        /// The local client connection.
        /// Null if the client is not connected.
        /// </summary>
        public Connection? clientToServerConn { [UsedImplicitly] get; private set; }

        /// <summary>
        /// The cookie scope of the network manager.
        /// This is used to determine when the cookies should be cleared.
        /// This detemines the lifetime of the cookies which are used to remember connections and their PlayerID.
        /// </summary>
        public CookieScope cookieScope
        {
            get => _cookieScope;
            set
            {
                if (isOffline)
                    _cookieScope = value;
                else
                    PurrLogger.LogError("Failed to update cookie scope since a connection is active.");
            }
        }

        /// <summary>
        /// What to do when a packet exceeds the MTU on an unreliable channel.
        /// </summary>
        public MTUExceededBehaviour mtuExceededBehaviour
        {
            get => _mtuExceededBehaviour;
            set => _mtuExceededBehaviour = value;
        }

        /// <summary>
        /// Number of target ticks per second.
        /// </summary>
        public int tickRate
        {
            get => _tickRate;
            set
            {
                if (value < 1)
                {
                    PurrLogger.LogError("Failed to update tick rate since it must be greater than zero.");
                    return;
                }

                if (_serverTickManager != null || _clientTickManager != null)
                {
                    PurrLogger.LogError("Failed to update tick rate since a tick manager is already running.");
                    return;
                }

                _tickRate = value;
            }
        }

        /// <summary>
        /// The start flags of the server.
        /// This is used to determine when the server should automatically start.
        /// </summary>
        public StartFlags startServerFlags
        {
            get => _startServerFlags;
            set => _startServerFlags = value;
        }

        /// <summary>
        /// The start flags of the client.
        /// This is used to determine when the client should automatically start.
        /// </summary>
        public StartFlags startClientFlags
        {
            get => _startClientFlags;
            set => _startClientFlags = value;
        }

        /// <summary>
        /// The Network Assets of the network manager.
        /// </summary>
        public NetworkAssets networkAssets
        {
            get => _networkAssets;
            set
            {
                if (isOffline)
                {
                    _networkAssets = value;
                }
                else PurrLogger.LogError("Failed to update network assets since a connection is active.");
            }
        }

        /// <summary>
        /// The prefab provider of the network manager.
        /// </summary>
        public IPrefabProvider prefabProvider { get; private set; }

        public bool TryGetPrefabPersistentId(int prefabId, out string persistentId)
        {
            if (prefabProvider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId(prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabPersistentId(GameObject prefab, out string persistentId)
        {
            if (prefabProvider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId(prefab, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (prefabProvider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPrefabDataByPersistentId(persistentId, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetNetworkAssetPersistentId(UnityEngine.Object asset, out string persistentId)
        {
            if (_networkAssets)
                return _networkAssets.TryGetPersistentId(asset, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetNetworkAssetByPersistentId(string persistentId, out UnityEngine.Object asset)
        {
            if (_networkAssets)
                return _networkAssets.TryGetAssetByPersistentId(persistentId, out asset);

            asset = null;
            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        /// <summary>
        /// The Addressable network prefabs configuration, if assigned.
        /// </summary>
        public AddressableNetworkPrefabs addressableNetworkPrefabs => _addressableNetworkPrefabs;
#endif

        /// <summary>
        /// The visibility rules of the network manager.
        /// </summary>
        public NetworkVisibilityRuleSet visibilityRules => _visibilityRules;

        /// <summary>
        /// The original scene of the network manager.
        /// This is the scene the network manager was created in.
        /// </summary>
        public Scene originalScene { get; private set; }

        public int originalSceneBuildIndex { get; private set; }

        /// <summary>
        /// Occurs when the server connection state changes.
        /// </summary>
        public event Action<ConnectionState> onServerConnectionState;

        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public event Action<ConnectionState> onClientConnectionState;

        /// <summary>
        /// Occurs once for each completed client disconnect, after its transport reason and
        /// PurrNet's local stop intent have been combined into one ordered notification.
        /// </summary>
        public event Action<ClientDisconnectInfo> onClientDisconnected;

        /// <summary>
        /// Occurs when the server connection state changes.
        /// </summary>
        public static event Action<ConnectionState> onAnyServerConnectionState;

        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public static event Action<ConnectionState> onAnyClientConnectionState;

        /// <summary>
        /// Server-side: fires when a client is rejected by either a built-in denier
        /// (version mismatch, ack timeout, missing authenticator) or by the active
        /// <see cref="Authentication.AuthenticationLayer"/>. <c>reason</c> is <c>null</c> for
        /// built-ins and only carries bytes for typed denials produced by an
        /// <see cref="Authentication.AuthenticationBehaviour{TRequest,TDenial}"/>.
        /// </summary>
        public event Action<Connection, DenialKind, ByteData?> onAuthenticationDenied;

        private ITransport _transportLayer;

        public ITransport rawTransport
        {
            get
            {
                if (_transport)
                    return _transport.transport;
                return null;
            }
        }

        private bool _ready;

        /// <summary>
        /// Unsubscribes all listeners and any other internal state.
        /// This is meant to be called manually if you encounter any caching issues due to bad unsubscribing.
        /// </summary>
        public void ResetInternalState()
        {
            onServerConnectionState = null;
            onClientConnectionState = null;
            onClientDisconnected = null;
            onAnyServerConnectionState = null;
            onAnyClientConnectionState = null;
            onAuthenticationDenied = null;

            onPreTick = null;
            onTick = null;
            onPostTick = null;

            onPlayerJoined = null;
            onPlayerLeft = null;
            onPlayerJoinedScene = null;
            onPlayerLeftScene = null;
            onPlayerLoadedScene = null;
            onPlayerReboundScene = null;
            onPlayerUnloadedScene = null;
            onLocalPlayerReceivedID = null;

            onNetworkStarted = null;
            onNetworkShutdown = null;
            onNetworkStartedSimple = null;
            onNetworkShutdownSimple = null;

            _serverPendingSubscriptions.Clear();
            _clientPendingSubscriptions.Clear();
        }

        public ITransport currentTransport => _transport ? _transport.transport : null;

        /// <summary>
        /// The transport of the network manager.
        /// This is the main transport used when starting the server or client.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when trying to change the transport while it is being used.</exception>
        [NotNull]
        public GenericTransport transport
        {
            get => _transport;
            set
            {
                if (_transport)
                {
                    if (serverState != ConnectionState.Disconnected ||
                        clientState != ConnectionState.Disconnected)
                    {
                        throw new InvalidOperationException(
                            PurrLogger.FormatMessage("Cannot change transport while it is being used."));
                    }

                    TeardownTransportLayer();
                }

                _transport = value;

                if (_transport)
                {
                    BuildTransportLayer();
                    _subscribed = true;
                }
                else
                {
                    _subscribed = false;
                }
            }
        }

        private void BuildTransportLayer()
        {
            _transportLayer = _transport.transport;
            if (_transportLayer == null)
                throw new InvalidOperationException(PurrLogger.FormatMessage("Transport is not set (null)."));

            _transportLayer.onConnected += OnNewConnection;
            _transportLayer.onDisconnected += OnLostConnection;
            _transportLayer.onConnectionState += OnConnectionState;
            _transportLayer.onDataReceived += OnDataReceived;
        }

        private void TeardownTransportLayer()
        {
            if (_transportLayer == null)
                return;

            _transportLayer.onConnected -= OnNewConnection;
            _transportLayer.onDisconnected -= OnLostConnection;
            _transportLayer.onConnectionState -= OnConnectionState;
            _transportLayer.onDataReceived -= OnDataReceived;
            _transportLayer = null;
        }

        /// <summary>
        /// Whether the server should automatically start.
        /// </summary>
        public bool shouldAutoStartServer => transport && ShouldStart(_startServerFlags);

        /// <summary>
        /// Whether the client should automatically start.
        /// </summary>
        public bool shouldAutoStartClient => transport && ShouldStart(_startClientFlags);

        private bool _isCleaningClient;
        private bool _isCleaningServer;
        private bool _preserveClientStateForHostMigration;

        internal bool isPreservingClientStateForHostMigration =>
            _preserveClientStateForHostMigration;
        private HostMigrationTransitionOptions _hostMigrationSession;
        private HostMigrationTransitionOptions _expectedHostMigrationSession;
        private double _hostMigrationRosterDeadline;
        private HostMigrationTransitionOptions _advertisedHostMigrationSession;
        private bool _receivedHostMigrationSession;
        private bool _hostMigrationSessionMatched;
        private bool _clientDisconnectWasLocallyRequested;
        private bool _clientDisconnectWasNotified;
        private bool _hasPendingClientDisconnectReason;
        private Connection _pendingClientDisconnectConnection;
        private DisconnectReason _pendingClientDisconnectReason;
        private bool _pendingClientDisconnectWasLocallyRequested;
        private bool _hostMigrationRollbackInProgress;

        private readonly struct DeferredPromotedServerAdmissionEvent
        {
            public readonly Connection connection;
            public readonly ByteData data;
            public readonly bool isData;

            public DeferredPromotedServerAdmissionEvent(Connection connection)
            {
                this.connection = connection;
                data = default;
                isData = false;
            }

            public DeferredPromotedServerAdmissionEvent(Connection connection, ByteData data)
            {
                this.connection = connection;
                this.data = data;
                isData = true;
            }
        }

        private bool _deferPromotedServerAdmission;
        private readonly List<DeferredPromotedServerAdmissionEvent>
            _deferredPromotedServerAdmission = new();
        private int _deferredPromotedServerConnectionCount;
        private int _deferredPromotedServerAdmissionBytes;
        private const int MaxDeferredPromotedServerConnections = 1024;
        private const int MaxDeferredPromotedServerAdmissionBytes = 8 * 1024 * 1024;

        /// <summary>
        /// The state of the server connection.
        /// This is based on the transport listener state.
        /// </summary>
        public ConnectionState serverState
        {
            get
            {
                var state = _transportLayer?.listenerState ?? ConnectionState.Disconnected;
                var result = state == ConnectionState.Disconnected && _isCleaningServer
                    ? ConnectionState.Disconnecting
                    : state;
                return result;
            }
        }

        /// <summary>
        /// The state of the client connection.
        /// This is based on the transport client state.
        /// </summary>
        public ConnectionState clientState
        {
            get
            {
                var state = _transportLayer?.clientState ?? ConnectionState.Disconnected;
                return state == ConnectionState.Disconnected && _isCleaningClient
                    ? ConnectionState.Disconnecting
                    : state;
            }
        }

        /// <summary>
        /// Whether the network manager is a server.
        /// </summary>
        public bool isServer { get; private set; }

        [UsedByIL] public static bool isServerStatic => main && main.isServer;

        [UsedByIL] public static bool isClientStatic => main && main.isClient;

        /// <summary>
        /// Whether the network manager is a client.
        /// </summary>
        public bool isClient { get; private set; }

        /// <summary>
        /// Whether the network manager is offline.
        /// Not a server or a client.
        /// </summary>
        public bool isOffline => !isServer && !isClient;

        /// <summary>
        /// Whether the network manager is a planned host.
        /// This is true even if the server or client is not yet connected or ready.
        /// </summary>
        public bool isPlannedHost => ShouldStart(_startServerFlags) && ShouldStart(_startClientFlags);

        /// <summary>
        /// Whether the network manager is a host.
        /// This is true only if the server and client are connected and ready.
        /// </summary>
        public bool isHost => isServer && isClient;

        /// <summary>
        /// Whether the network manager is a server only.
        /// </summary>
        public bool isServerOnly => isServer && !isClient;

        public bool pendingHost =>
            clientState != ConnectionState.Disconnected && serverState != ConnectionState.Disconnected;

        public bool isPlannedServerOnly => ShouldStart(_startServerFlags) && !ShouldStart(_startClientFlags);

        /// <summary>
        /// Whether the network manager is a client only.
        /// </summary>
        public bool isClientOnly => !isServer && isClient;

        /// <summary>
        /// The network rules of the network manager.
        /// </summary>
        public NetworkRules networkRules => _networkRules;

        private ModulesCollection _serverModules;
        private ModulesCollection _clientModules;

        private bool _subscribed;

        /// <summary>
        /// Sets the main instance of the network manager.
        /// This is used for convinience but also for static RPCs and other static functionality.
        /// </summary>
        /// <param name="instance">The instance to set as the main instance.</param>
        public static void SetMainInstance(NetworkManager instance)
        {
            if (instance)
                main = instance;
        }

        /// <summary>
        /// Overrides the manager's <see cref="NetworkRules"/> asset. Must be called while the
        /// manager is offline; runtime rule swaps mid-connection are not supported because
        /// in-flight authority checks would observe inconsistent rules across peers.
        /// </summary>
        public void SetNetworkRules(NetworkRules rules)
        {
            if (!isOffline)
            {
                PurrLogger.LogError("Failed to update network rules since a connection is active.");
                return;
            }

            _networkRules = rules;
        }

        /// <summary>
        /// Sets the prefab provider.
        /// </summary>
        /// <param name="provider">The provider to set.</param>
        public void SetPrefabProvider(IPrefabProvider provider)
        {
            if (!isOffline)
            {
                PurrLogger.LogError("Failed to update prefab provider since a connection is active.");
                return;
            }

            if (prefabProvider == provider)
                return;

            prefabProvider = provider;
            prefabProvider.Refresh();
        }

        /// <summary>
        /// Prepares the prefab info for the given instance.
        /// This needs to be ready before the object is spawned.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="pid">The prefab index in the network prefabs list.</param>
        /// <param name="shouldBePooled">Whether the object should be pooled.</param>
        public static void SetupPrefabInfo(GameObject instance, int pid, bool shouldBePooled)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();

            if (!instance.GetComponent<NetworkIdentity>())
                instance.AddComponent<NetworkIdentity>();

            instance.GetComponentsInChildren(true, children);
            SetupPrefabInfo(instance, pid, shouldBePooled, children);
            ListPool<NetworkIdentity>.Destroy(children);
        }

        /// <summary>
        /// Prepares the prefab info for the given instance, reusing an already collected identity list.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="pid">The prefab index in the network prefabs list.</param>
        /// <param name="shouldBePooled">Whether the object should be pooled.</param>
        /// <param name="children">The result of GetComponentsInChildren(true) on the instance.</param>
        public static void SetupPrefabInfo(GameObject instance, int pid, bool shouldBePooled,
            List<NetworkIdentity> children)
        {
            if (!instance.GetComponent<NetworkIdentity>())
            {
                instance.AddComponent<NetworkIdentity>();
                children.Clear();
                instance.GetComponentsInChildren(true, children);
            }

            Transform runTransform = null;
            int runStart = 0;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var trs = child.transform;

                if (!ReferenceEquals(trs, runTransform))
                {
                    runTransform = trs;
                    runStart = i;
                }

                child.PreparePrefabInfo(
                    pid,
                    i == runStart ? i : children[runStart].componentIndex,
                    shouldBePooled,
                    false
                );
            }
        }

        static bool ReferencesAssembly(Assembly asm, string targetSimpleName)
        {
            try
            {
                if (asm == null) return false;

                // If it's the same assembly
                if (string.Equals(asm.GetName().Name, targetSimpleName,
                        StringComparison.Ordinal)) return true;

                // Check direct references
                var refs = asm.GetReferencedAssemblies();
                for (int i = 0; i < refs.Length; i++)
                {
                    if (string.Equals(refs[i].Name, targetSimpleName,
                            StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        static string GetSimpleNameOf(Type t) => t.Assembly.GetName().Name;

        struct TypeRegistrer
        {
            public MethodInfo method;
            public int priority;
        }

        public static void CallAllRegisters()
        {
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var attrAssemblyName = GetSimpleNameOf(typeof(RegisterPackersAttribute));
            using var methodsToCall = DisposableList<TypeRegistrer>.Create(128);

            for (var index = 0; index < allAssemblies.Length; index++)
            {
                var assembly = allAssemblies[index];

                if (!ReferencesAssembly(assembly, attrAssemblyName))
                    continue;

                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types ?? Array.Empty<Type>();
                }
                catch
                {
                    continue;
                }

                for (var j = 0; j < types.Length; j++)
                {
                    var type = types[j];
                    if (type == null)
                        continue;

                    if (!type.IsAbstract || !type.IsSealed)
                        continue;

                    MethodInfo[] methods;

                    try
                    {
                        methods = type.GetMethods(BindingFlags.Static |
                                                  BindingFlags.Public |
                                                  BindingFlags.NonPublic);
                    }
                    catch
                    {
                        continue; // skip bad type
                    }

                    for (var m = 0; m < methods.Length; m++)
                    {
                        var method = methods[m];
                        if (!method.IsStatic)
                            continue;

                        try
                        {
                            var attributes = method.GetCustomAttributes(false);
                            for (var i = 0; i < attributes.Length; i++)
                            {
                                var attribute = attributes[i];

                                if (attribute is not RegisterPackersAttribute registerPackersAttribute)
                                    continue;

                                methodsToCall.Add(new TypeRegistrer
                                {
                                    method = method,
                                    priority = registerPackersAttribute.priority
                                });

                                // method.Invoke(null, null);
                                break;
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }

            methodsToCall.list.Sort((a, b) => a.priority.CompareTo(b.priority));

            for (var i = 0; i < methodsToCall.Count; i++)
                methodsToCall[i].method.Invoke(null, null);
        }

        private static bool _hasGeneratedAlready;

        public static void LoadOrGenerateHashes()
        {
            CalculateHashes();
        }

        [UsedImplicitly]
        static void CalculateHashes()
        {
            if (_hasGeneratedAlready)
                return;

            _hasGeneratedAlready = true;

            Hasher.ClearState();
            Manifest.Clear();
            CallAllRegisters();
        }

#if !UNITY_EDITOR
        private void OnApplicationQuit()
        {
            if (_patchLingeringProcessBug)
                Environment.FailFast("Applying patch for lingering process bug.");
        }
#endif

        public static string version { get; private set; }

        public static bool VerifyVersion(string va)
        {
            if (va == "v?" || version == "v?")
                return true;
            return va == version;
        }

        private void Awake()
        {
            if (_ready)
                return;

            version ??= PurrMetadata.version;

            if (main && main != this)
            {
                if (main.isOffline)
                {
                    Destroy(gameObject);
                    return;
                }

                Destroy(this);
                return;
            }

            if (!networkRules)
                throw new InvalidOperationException(PurrLogger.FormatMessage("NetworkRules is not set (null)."));

            originalScene = gameObject.scene;
            originalSceneBuildIndex = originalScene.buildIndex;

            if (_visibilityRules)
            {
                var ogName = _visibilityRules.name;
                _visibilityRules = Instantiate(_visibilityRules);
                _visibilityRules.name = "Copy of " + ogName;
                _visibilityRules.Setup(this);
            }

            main = this;

            LoadOrGenerateHashes();

            Application.runInBackground = true;

            if (_networkPrefabs)
            {
                if (_networkPrefabs.autoGenerate)
                    _networkPrefabs.Generate();

                if (prefabProvider == null)
                    SetPrefabProvider(_networkPrefabs);
            }

            if (!_subscribed)
                transport = _transport;

            _serverModules = new ModulesCollection(this, true);
            _clientModules = new ModulesCollection(this, false);
            UnityLatestUpdate.onPostLatestUpdate += FlushImmediateRPCsLate;
            _ready = true;

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            if (TryGetComponent(out GenericTransport _) || transport)
                return;
            transport = gameObject.AddComponent<UDPTransport>();
        }
#endif

        public bool HasModule<T>(bool asServer) where T : INetworkModule
        {
            return TryGetModule<T>(out _, asServer);
        }

        /// <summary>
        /// Gets the module of the given type.
        /// Throws an exception if the module is not found.
        /// </summary>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <typeparam name="T">The type of the module.</typeparam>
        /// <returns>The module of the given type.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the module is not found.</exception>
        public T GetModule<T>(bool asServer) where T : INetworkModule
        {
            if (TryGetModule(out T module, asServer))
                return module;

            throw new InvalidOperationException(
                PurrLogger.FormatMessage($"Module {typeof(T).Name} not found - asServer : {asServer}."));
        }

        /// <summary>
        /// Tries to get the module of the given type.
        /// </summary>
        /// <param name="module">The module if found, otherwise the default value of the type.</param>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        public bool TryGetModule<T>(out T module, bool asServer) where T : INetworkModule
        {
            return asServer ? _serverModules.TryGetModule(out module) : _clientModules.TryGetModule(out module);
        }

        /// <summary>
        /// Gets all the objects owned by the given player.
        /// This creates a new list every time it's called.
        /// So it's recommended to cache the result if you're going to use it multiple times.
        /// </summary>
        public List<NetworkIdentity> GetAllPlayerOwnedIds(PlayerID player, bool asServer)
        {
            var ownershipModule = GetModule<GlobalOwnershipModule>(asServer);
            return ownershipModule.GetAllPlayerOwnedIds(player);
        }

        /// <summary>
        /// Gets all the objects owned by the given player.
        /// Adds the result to the given list.
        /// </summary>
        public void GetAllPlayerOwnedIds(PlayerID player, bool asServer, List<NetworkIdentity> result)
        {
            var ownershipModule = GetModule<GlobalOwnershipModule>(asServer);
            ownershipModule.GetAllPlayerOwnedIds(player, result);
        }

        /// <summary>
        /// Gets the current player count.
        /// </summary>
        public int playerCount => playerModule?.players.Count ?? 0;

        /// <summary>
        /// Gets the current player list.
        /// This will be update every time a player joins or leaves.
        /// </summary>
        public IReadOnlyList<PlayerID> players
        {
            get
            {
                if (TryGetModule<PlayersManager>(isServer, out var playersManager))
                    return playersManager.players;
                return Array.Empty<PlayerID>();
            }
        }

        /// <summary>
        /// Enumerates all the objects owned by the given player.
        /// </summary>
        /// <param name="player">The player to enumerate the objects of.</param>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <returns>An enumerable of all the objects owned by the given player.</returns>
        public IEnumerable<NetworkIdentity> EnumerateAllPlayerOwnedIds(PlayerID player, bool asServer)
        {
            if (!TryGetModule<GlobalOwnershipModule>(out var ownershipModule, asServer))
                return Array.Empty<NetworkIdentity>();
            return ownershipModule.EnumerateAllPlayerOwnedIds(player);
        }

        /// <summary>
        /// Adds a visibility rule to the rule set.
        /// </summary>
        /// <param name="manager">The network manager to add the rule to.</param>
        /// <param name="rule">The rule to add.</param>
        public void AddVisibilityRule(NetworkManager manager, INetworkVisibilityRule rule)
        {
            _visibilityRules.AddRule(manager, rule);
        }

        /// <summary>
        /// Removes a visibility rule from the rule set.
        /// </summary>
        /// <param name="rule">The rule to remove.</param>
        public void RemoveVisibilityRule(INetworkVisibilityRule rule)
        {
            _visibilityRules.RemoveRule(rule);
        }

        /// <summary>
        /// The scene module of the network manager.
        /// Defaults to the server scene module if the server is active.
        /// Otherwise it defaults to the client scene module.
        /// </summary>
        public ScenesModule sceneModule => _serverSceneModule ?? _clientSceneModule;

        /// <summary>
        /// The players manager of the network manager.
        /// Defaults to the server players manager if the server is active.
        /// Otherwise it defaults to the client players manager.
        /// </summary>
        public PlayersManager playerModule => _serverPlayersManager ?? _clientPlayersManager;

        /// <summary>
        /// The tick manager of the network manager.
        /// Defaults to the server tick manager if the server is active.
        /// Otherwise it defaults to the client tick manager.
        /// </summary>
        public TickManager tickModule => _serverTickManager ?? _clientTickManager;

        /// <summary>
        /// The players broadcaster of the network manager.
        /// Defaults to the server players broadcaster if the server is active.
        /// Otherwise it defaults to the client players broadcaster.
        /// </summary>
        public PlayersBroadcaster broadcastModule => _serverPlayersBroadcast ?? _clientPlayersBroadcast;

        public BroadcastModule connectionBroadcaster => _serverBroadcast ?? _clientBroadcast;

        public BroadcastModule GetConnectionBroadcaster(bool asServer)
        {
            return asServer ? _serverBroadcast : _clientBroadcast;
        }

        /// <summary>
        /// The scene players module of the network manager.
        /// Defaults to the server scene players module if the server is active.
        /// Otherwise it defaults to the client scene players module.
        /// </summary>
        public ScenePlayersModule scenePlayersModule => _serverScenePlayersModule ?? _clientScenePlayersModule;

        public DeltaModule deltaModule => _serverDeltaModule ?? _clientDeltaModule;

        /// <summary>
        /// The network LOD factory of the network manager.
        /// Defaults to the server module if the server is active.
        /// Otherwise it defaults to the client module.
        /// </summary>
        public NetworkLODFactory lodModule => _serverLODModule ?? _clientLODModule;

        /// <summary>
        /// The local player of the network manager.
        /// If the local player is not set, this will return the default value of the player id.
        /// </summary>
        public PlayerID localPlayer => _clientPlayersManager?.localPlayerId ?? default;

        public bool isLocalPlayerReady => _clientPlayersManager?.localPlayerId.HasValue == true;

        public AuthenticationLayer authenticator
        {
            get { return _authenticator; }
            set
            {
                if (!isOffline)
                {
                    PurrLogger.LogError("Failed to update authenticator since a connection is active");
                    return;
                }

                _authenticator = value;
            }
        }

        private ScenesModule _clientSceneModule;
        private ScenesModule _serverSceneModule;

        private PlayersManager _clientPlayersManager;
        private PlayersManager _serverPlayersManager;

        private TickManager _clientTickManager;
        private TickManager _serverTickManager;

        private BroadcastModule _clientBroadcast;
        private BroadcastModule _serverBroadcast;

        private PlayersBroadcaster _clientPlayersBroadcast;
        private PlayersBroadcaster _serverPlayersBroadcast;

        private ScenePlayersModule _clientScenePlayersModule;
        private ScenePlayersModule _serverScenePlayersModule;

        internal DeltaModule _clientDeltaModule;
        internal DeltaModule _serverDeltaModule;

        private NetworkLODFactory _clientLODModule;
        private NetworkLODFactory _serverLODModule;

        private AuthModule _serverAuthModule;
        private string _promotedListenClientConnectionCookie;

        /// <summary>
        /// This event is triggered before the tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onPreTick;

        /// <summary>
        /// This event is triggered on tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onTick;

        /// <summary>
        /// This event is triggered after the tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onPostTick;

        /// <summary>
        /// This event is triggered when a player joins.
        /// Note that before a player joins it has a connection step.
        /// </summary>
        public event OnPlayerJoinedEvent onPlayerJoined;

        private bool _telemetrySentClient;

        void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            onPlayerJoined?.Invoke(player, isReconnect, asServer);

            if (!asServer && !_telemetrySentClient)
            {
                _telemetrySentClient = true;
                StartCoroutine(SendConnectionTelemetryDelayed());
            }
        }

        private IEnumerator SendConnectionTelemetryDelayed()
        {
            yield return null;
            PurrInternalTelemetry.SendConnectionEvent(this);
        }

        /// <summary>
        /// This event is triggered when a player leaves.
        /// </summary>
        public event OnPlayerLeftEvent onPlayerLeft;

        void OnPlayerLeft(PlayerID player, bool asServer) => onPlayerLeft?.Invoke(player, asServer);

        /// <summary>
        /// This event is triggered when the local player receives an ID.
        /// </summary>
        public event OnPlayerEvent onLocalPlayerReceivedID;

        void OnLocalPlayerReceivedID(PlayerID player) => onLocalPlayerReceivedID?.Invoke(player);

        void OnAuthenticationDenied(Connection conn, DenialKind kind, ByteData? reason) =>
            onAuthenticationDenied?.Invoke(conn, kind, reason);

        /// <summary>
        /// This event is triggered when a player joins the scene.
        /// It might not be triggered if the user reconnects but was already in the scene due to persistence.
        /// For that use onPlayerLoadedScene instead.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        void OnPlayerJoinedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerJoinedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when a player loads the scene.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerLoadedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when exact host migration rebinds a player's retained,
        /// already-loaded scene to the new authority. It is distinct from
        /// <see cref="onPlayerLoadedScene"/> so gameplay load lifecycle remains one-shot.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerReboundScene;

        void OnPlayerReboundScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (onPlayerReboundScene == null)
                return;

            var callbacks = onPlayerReboundScene.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((OnPlayerSceneEvent)callbacks[i]).Invoke(player, scene, asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }
        }

        /// <summary>
        /// This event is triggered when a player unloads the scene.
        /// Or when they leave the server and had it loaded.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerUnloadedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when a player leaves the scene.
        /// This might not be triggered if the network rules keep the player in the scene.
        /// In that case, you want to use onPlayerUnloadedScene.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLeftScene;

        void OnPlayerLeftScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerLeftScene?.Invoke(player, scene, asServer);

        private bool _isServerTicking;

        readonly List<ValidateSpawnAction> _clientSpawnValidators = new List<ValidateSpawnAction>();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value == null)
                    return;

                _clientSpawnValidators.Add(value);
                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate += value;
            }
            remove
            {
                if (value == null)
                    return;

                for (int i = _clientSpawnValidators.Count - 1; i >= 0; i--)
                {
                    if (_clientSpawnValidators[i] != value)
                        continue;

                    _clientSpawnValidators.RemoveAt(i);
                    break;
                }

                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate -= value;
            }
        }

        public void RegisterModules(ModulesCollection modules, bool asServer)
        {
            switch (asServer)
            {
                case true when isPromotingToServer:
                    RegisterPromotedServerModules(modules);
                    return;
                case false when isTranferingToNewServer && modules.hasModules:
                    modules.TransferToNewServer();
                    return;
            }

            var connBroadcaster = new BroadcastModule(this, asServer);
            var tickManager = new TickManager(_tickRate, this, connBroadcaster, asServer);

            if (asServer)
            {
                if (_serverTickManager != null)
                {
                    _serverTickManager.onPreTick -= OnServerPreTick;
                    _serverTickManager.onTick -= OnServerTick;
                    _serverTickManager.onPostTick -= OnServerPostTick;
                }

                _serverTickManager = tickManager;
                _isServerTicking = true;
                _serverTickManager.onPreTick += OnServerPreTick;
                _serverTickManager.onTick += OnServerTick;
                _serverTickManager.onPostTick += OnServerPostTick;
            }
            else
            {
                if (_clientTickManager != null)
                {
                    _clientTickManager.onPreTick -= OnClientPreTick;
                    _clientTickManager.onTick -= OnClientTick;
                    _clientTickManager.onPostTick -= OnClientPostTick;
                }

                _clientTickManager = tickManager;
                _clientTickManager.onPreTick += OnClientPreTick;
                _clientTickManager.onTick += OnClientTick;
                _clientTickManager.onPostTick += OnClientPostTick;
            }


            if (asServer)
                _serverBroadcast = connBroadcaster;
            else _clientBroadcast = connBroadcaster;

            var networkCookies = new CookiesModule(_cookieScope, asServer);
            var authModule = new AuthModule(this, connBroadcaster, networkCookies);
            if (!asServer && isPromotingToServer &&
                !string.IsNullOrEmpty(_promotedListenClientConnectionCookie))
            {
                authModule.SetClientConnectionCookie(_promotedListenClientConnectionCookie);
            }
            var playersManager = new PlayersManager(this, authModule, connBroadcaster);
            authModule.SetPlayerModule(playersManager);

            if (asServer)
            {
                if (_serverAuthModule != null)
                    _serverAuthModule.onAuthenticationDenied -= OnAuthenticationDenied;

                _serverAuthModule = authModule;
                _serverAuthModule.onAuthenticationDenied += OnAuthenticationDenied;

                if (_serverPlayersManager != null)
                {
                    _serverPlayersManager.onPlayerJoined -= OnPlayerJoined;
                    _serverPlayersManager.onPlayerLeft -= OnPlayerLeft;
                    _serverPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                }

                _serverPlayersManager = playersManager;

                _serverPlayersManager.onPlayerJoined += OnPlayerJoined;
                _serverPlayersManager.onPlayerLeft += OnPlayerLeft;
                _serverPlayersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
            }
            else
            {
                if (_clientPlayersManager != null)
                {
                    _clientPlayersManager.onPlayerJoined -= OnPlayerJoined;
                    _clientPlayersManager.onPlayerLeft -= OnPlayerLeft;
                    _clientPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                }

                _clientPlayersManager = playersManager;

                _clientPlayersManager.onPlayerJoined += OnPlayerJoined;
                _clientPlayersManager.onPlayerLeft += OnPlayerLeft;
                _clientPlayersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
            }

            var playersBroadcast = new PlayersBroadcaster(connBroadcaster, playersManager);

            if (asServer)
                _serverPlayersBroadcast = playersBroadcast;
            else _clientPlayersBroadcast = playersBroadcast;

            var scenesModule = new ScenesModule(this, playersManager);

            if (asServer)
                _serverSceneModule = scenesModule;
            else _clientSceneModule = scenesModule;

            var scenePlayers = new ScenePlayersModule(this, scenesModule, playersManager);

            if (asServer)
            {
                if (_serverScenePlayersModule != null)
                {
                    _serverScenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
                    _serverScenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
                    _serverScenePlayersModule.onPlayerReboundScene -= OnPlayerReboundScene;
                    _serverScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _serverScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _serverScenePlayersModule = scenePlayers;

                _serverScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _serverScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
                _serverScenePlayersModule.onPlayerReboundScene += OnPlayerReboundScene;
                _serverScenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
                _serverScenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
            }
            else
            {
                if (_clientScenePlayersModule != null)
                {
                    _clientScenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
                    _clientScenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
                    _clientScenePlayersModule.onPlayerReboundScene -= OnPlayerReboundScene;
                    _clientScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _clientScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _clientScenePlayersModule = scenePlayers;

                _clientScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _clientScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
                _clientScenePlayersModule.onPlayerReboundScene += OnPlayerReboundScene;
                _clientScenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
                _clientScenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
            }

            var newDeltaModule = new DeltaModule(playersManager, playersBroadcast);
            if (asServer) _serverDeltaModule = newDeltaModule;
            else _clientDeltaModule = newDeltaModule;

            scenesModule.SetScenePlayers(scenePlayers);
            playersManager.SetBroadcaster(playersBroadcast);

            modules.AddModule(playersManager);
            modules.AddModule(playersBroadcast);
            modules.AddModule(tickManager);
            modules.AddModule(connBroadcaster);
            modules.AddModule(authModule);
            modules.AddModule(networkCookies);
            modules.AddModule(newDeltaModule);

            modules.AddModule(scenesModule);
            modules.AddModule(scenePlayers);

            var hierarchyV2 = new HierarchyFactory(this, scenesModule, scenePlayers, playersManager);
            var ownershipModule =
                new GlobalOwnershipModule(this, hierarchyV2, playersManager, scenePlayers, scenesModule);
            var rpcModule = new RPCModule(this, playersManager, hierarchyV2, ownershipModule, scenesModule);
            var networkTransform =
                new NetworkTransformFactory(scenesModule, scenePlayers, playersBroadcast, this, hierarchyV2);
            var colliderRollback = new ColliderRollbackFactory(this, tickManager, scenesModule);
            var networkLOD = new NetworkLODFactory(this, scenesModule, scenePlayers);

            if (asServer) _serverLODModule = networkLOD;
            else _clientLODModule = networkLOD;

            if (asServer)
                _serverRpcModule = rpcModule;
            else _clientRpcModule = rpcModule;

            if (asServer)
                for (int i = 0; i < _clientSpawnValidators.Count; i++)
                    hierarchyV2.onClientSpawnValidate += _clientSpawnValidators[i];

            modules.AddModule(networkTransform);
            modules.AddModule(hierarchyV2);
            modules.AddModule(ownershipModule);
            modules.AddModule(rpcModule);
            modules.AddModule(new RpcRequestResponseModule(this, playersManager, asServer));
            modules.AddModule(colliderRollback);
            modules.AddModule(networkLOD);

#if ADDRESSABLES_PURRNET_SUPPORT
            if (_addressableNetworkPrefabs && _addressableNetworkPrefabs.count > 0 &&
                networkRules && networkRules.AddressablesSyncLoadState)
            {
                modules.AddModule(new AddressablesSyncModule(this, playersManager));
            }
#endif

            RenewSubscriptions(asServer);
        }

        private void RegisterPromotedServerModules(ModulesCollection modules)
        {
            modules.MigrateFrom(_clientModules);
            RebindPromotedServerModules();
            RenewSubscriptions(true);
        }

        private void RebindPromotedServerModules()
        {
            if (_clientTickManager != null)
            {
                _clientTickManager.onPreTick -= OnClientPreTick;
                _clientTickManager.onTick -= OnClientTick;
                _clientTickManager.onPostTick -= OnClientPostTick;
            }

            if (_serverTickManager != null)
            {
                _serverTickManager.onPreTick -= OnServerPreTick;
                _serverTickManager.onTick -= OnServerTick;
                _serverTickManager.onPostTick -= OnServerPostTick;
            }

            if (_serverModules.TryGetModule(out TickManager tickManager))
            {
                _serverTickManager = tickManager;
                _isServerTicking = true;
                _serverTickManager.onPreTick += OnServerPreTick;
                _serverTickManager.onTick += OnServerTick;
                _serverTickManager.onPostTick += OnServerPostTick;
            }
            else
            {
                _serverTickManager = null;
                _isServerTicking = false;
            }

            UnsubscribePlayerModuleEvents(_clientPlayersManager);
            UnsubscribePlayerModuleEvents(_serverPlayersManager);

            if (_serverModules.TryGetModule(out PlayersManager playersManager))
            {
                _serverPlayersManager = playersManager;
                SubscribePlayerModuleEvents(_serverPlayersManager);
            }
            else
            {
                _serverPlayersManager = null;
            }

            UnsubscribeScenePlayersModuleEvents(_clientScenePlayersModule);
            UnsubscribeScenePlayersModuleEvents(_serverScenePlayersModule);

            if (_serverModules.TryGetModule(out ScenePlayersModule scenePlayersModuleInstance))
            {
                _serverScenePlayersModule = scenePlayersModuleInstance;
                SubscribeScenePlayersModuleEvents(_serverScenePlayersModule);
            }
            else
            {
                _serverScenePlayersModule = null;
            }

            if (_serverAuthModule != null)
                _serverAuthModule.onAuthenticationDenied -= OnAuthenticationDenied;

            if (_serverModules.TryGetModule(out AuthModule authModule))
            {
                _serverAuthModule = authModule;
                _serverAuthModule.onAuthenticationDenied += OnAuthenticationDenied;
            }
            else
            {
                _serverAuthModule = null;
            }

            if (!_serverModules.TryGetModule(out _serverBroadcast))
                _serverBroadcast = null;
            if (!_serverModules.TryGetModule(out _serverPlayersBroadcast))
                _serverPlayersBroadcast = null;
            if (!_serverModules.TryGetModule(out _serverSceneModule))
                _serverSceneModule = null;
            if (!_serverModules.TryGetModule(out _serverDeltaModule))
                _serverDeltaModule = null;
            if (!_serverModules.TryGetModule(out _serverRpcModule))
                _serverRpcModule = null;
            if (!_serverModules.TryGetModule(out _serverLODModule))
                _serverLODModule = null;

            if (_clientSpawnValidators.Count > 0 &&
                _serverModules.TryGetModule(out HierarchyFactory hierarchyFactory))
            {
                for (int i = 0; i < _clientSpawnValidators.Count; i++)
                {
                    var validate = _clientSpawnValidators[i];
                    hierarchyFactory.onClientSpawnValidate -= validate;
                    hierarchyFactory.onClientSpawnValidate += validate;
                }
            }

            _clientTickManager = null;
            _clientBroadcast = null;
            _clientPlayersManager = null;
            _clientPlayersBroadcast = null;
            _clientSceneModule = null;
            _clientScenePlayersModule = null;
            _clientDeltaModule = null;
            _clientRpcModule = null;
            _clientLODModule = null;
            _isCleaningClient = false;
        }

        private void SubscribePlayerModuleEvents(PlayersManager playersManager)
        {
            if (playersManager == null)
                return;

            playersManager.onPlayerJoined -= OnPlayerJoined;
            playersManager.onPlayerLeft -= OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;

            playersManager.onPlayerJoined += OnPlayerJoined;
            playersManager.onPlayerLeft += OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
        }

        private void UnsubscribePlayerModuleEvents(PlayersManager playersManager)
        {
            if (playersManager == null)
                return;

            playersManager.onPlayerJoined -= OnPlayerJoined;
            playersManager.onPlayerLeft -= OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
        }

        private void SubscribeScenePlayersModuleEvents(ScenePlayersModule scenePlayersModule)
        {
            if (scenePlayersModule == null)
                return;

            scenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
            scenePlayersModule.onPlayerReboundScene -= OnPlayerReboundScene;
            scenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;

            scenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
            scenePlayersModule.onPlayerReboundScene += OnPlayerReboundScene;
            scenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
        }

        private void UnsubscribeScenePlayersModuleEvents(ScenePlayersModule scenePlayersModule)
        {
            if (scenePlayersModule == null)
                return;

            scenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
            scenePlayersModule.onPlayerReboundScene -= OnPlayerReboundScene;
            scenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
        }

        private void OnServerPreTick() => onPreTick?.Invoke(true);

        private void OnServerTick()
        {
            OnTick();
            onTick?.Invoke(true);
        }

        private void OnServerPostTick() => onPostTick?.Invoke(true);

        private void OnClientPreTick() => onPreTick?.Invoke(false);

        private void OnClientTick()
        {
            if (!_isServerTicking)
                OnTick();
            onTick?.Invoke(false);
        }

        private void OnClientPostTick() => onPostTick?.Invoke(false);

        private static int _flagsDisableCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFlagsDisableCount()
        {
            _flagsDisableCount = 0;
        }

        /// <summary>
        /// Whether auto start flags are currently globally disabled.
        /// While disabled, <see cref="ShouldStart"/> always returns false regardless of the provided flags.
        /// </summary>
        public static bool areFlagsDisabled => _flagsDisableCount > 0;

        /// <summary>
        /// Globally disables auto start flags from working. Each call increments a counter;
        /// a matching number of <see cref="EnableFlags"/> calls is required to re-enable.
        /// </summary>
        public static void DisableFlags()
        {
            _flagsDisableCount++;
        }

        /// <summary>
        /// Decrements the global auto start flags disable counter. Flags are only re-enabled
        /// once the counter reaches zero (matching every <see cref="DisableFlags"/> call).
        /// </summary>
        public static void EnableFlags()
        {
            if (_flagsDisableCount <= 0)
            {
                PurrLogger.LogWarning($"{nameof(EnableFlags)} called without a matching {nameof(DisableFlags)}; ignoring.");
                return;
            }

            _flagsDisableCount--;
        }

        public static bool ShouldStart(StartFlags flags)
        {
            if (_flagsDisableCount > 0)
                return false;

            return (flags.HasFlag(StartFlags.Editor) && ApplicationContext.isMainEditor) ||
                   (flags.HasFlag(StartFlags.Clone) && ApplicationContext.isClone) ||
                   (flags.HasFlag(StartFlags.ClientBuild) && ApplicationContext.isClientBuild) ||
                   (flags.HasFlag(StartFlags.ServerBuild) && ApplicationContext.isServerBuild);
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        /// <summary>
        /// Sets up a composite prefab provider that merges the regular NetworkPrefabs
        /// and the AddressableNetworkPrefabs into a single unified provider.
        /// Called after Addressable prefabs have been loaded.
        /// </summary>
        private void SetupCompositePrefabProvider()
        {
            if (!_addressableNetworkPrefabs || _addressableNetworkPrefabs.count == 0)
                return;

            var composite = new CompositePrefabProvider();

            if (_networkPrefabs)
                composite.AddProvider(_networkPrefabs);

            composite.AddProvider(_addressableNetworkPrefabs);
            SetPrefabProvider(composite);
        }

        private async void Start()
        {
            try
            {
                if (_addressableNetworkPrefabs && _addressableNetworkPrefabs.count > 0)
                {
                    try
                    {
                        if (_addressableNetworkPrefabs.preloadAtStartup)
                        {
                            await _addressableNetworkPrefabs.LoadAllAsync();
                        }
                        SetupCompositePrefabProvider();
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"Failed to load Addressable network prefabs: {e.Message}\n{e.StackTrace}");
                    }
                }

                AutoStart();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
#else
        private void Start()
        {
            AutoStart();
        }
#endif

        private void AutoStart()
        {
            bool shouldStartServer = transport && ShouldStart(_startServerFlags);
            bool shouldStartClient = transport && ShouldStart(_startClientFlags);

            if (shouldStartServer)
            {
#if !UNITY_EDITOR
                PurrLogger.Log("Auto-Starting server...");
#endif
                if (ApplicationContext.isServerBuild)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = _tickRate;
                }

                StartServer();
            }

            if (shouldStartClient)
            {
#if !UNITY_EDITOR
                PurrLogger.Log("Auto-Starting client...");
#endif
                StartClient();
            }
        }

        private void Update()
        {
            if (!_isPromotionSimulationPaused)
            {
                _serverModules.TriggerOnUpdate();
                _clientModules.TriggerOnUpdate();
            }

            if (_hostMigrationRollbackInProgress)
                ProcessPendingNetworkCleanup();

            ProcessHostMigrationRosterTimeout();

            if (_transportLayer == null)
                return;

            SetReceiveDeferral(true);

            try
            {
                _transportLayer.UnityUpdate(Time.deltaTime);
            }
            finally
            {
                SetReceiveDeferral(false);
            }
        }

        private void SetReceiveDeferral(bool defer)
        {
            _serverBroadcast?.SetDeferNonImmediate(defer);
            _clientBroadcast?.SetDeferNonImmediate(defer);
        }

        private bool _sendFlushRequested;

        internal void RequestSendFlushThisFrame()
        {
            _sendFlushRequested = true;
        }

        // Runs from UnityLatestUpdate's post phase (execution order 32000, after every
        // onLatestUpdate subscriber) so immediate RPCs queued by gameplay LateUpdate or
        // latest-update callbacks still flush this frame; this manager's own LateUpdate
        // would run before them at -999.
        private void FlushImmediateRPCsLate()
        {
            bool flushedAny = _sendFlushRequested;
            _sendFlushRequested = false;

            if (serverState == ConnectionState.Connected)
                flushedAny |= _serverModules.FlushImmediateRPCs();

            if (clientState == ConnectionState.Connected)
                flushedAny |= _clientModules.FlushImmediateRPCs();

            if (flushedAny)
                SendMessagesNow();
        }

        private void OnDrawGizmos()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.TriggerOnDrawGizmos();

            if (clientConnected)
                _clientModules.TriggerOnDrawGizmos();
        }

        static readonly ProfilerMarker _preFixedUpdateMarker = new ProfilerMarker($"NetworkManager.OnPreFixedUpdate");
        static readonly ProfilerMarker _receiveMessagesMarker = new ProfilerMarker($"NetworkManager.ReceiveMessages");
        static readonly ProfilerMarker _receiveFixedUpdateMarker = new ProfilerMarker($"NetworkManager.OnFixedUpdate");

        static readonly ProfilerMarker _receivePostFixedUpdateMarker =
            new ProfilerMarker($"NetworkManager.OnPostFixedUpdate");

        static readonly ProfilerMarker _onBatchMarker = new ProfilerMarker($"NetworkManager.OnBatch");
        static readonly ProfilerMarker _onPostBatchMarker = new ProfilerMarker($"NetworkManager.OnPostBatch");
        static readonly ProfilerMarker _onSendMessagesMarker = new ProfilerMarker($"NetworkManager.SendMessages");

        private double _lastSendTime;

        private void SendMessagesNow(float fallbackDelta = 0f)
        {
            if (_transportLayer == null)
                return;

            var now = Time.unscaledTimeAsDouble;
            var sendDelta = _lastSendTime > 0 ? (float)(now - _lastSendTime) : fallbackDelta;
            _lastSendTime = now;
            _transportLayer.SendMessages(sendDelta);
        }

        private void OnTick()
        {
            var delta = tickModule?.tickDelta ?? Time.fixedUnscaledDeltaTime;
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            using (_preFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPreFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnPreFixedUpdate();
            }

            using (_receiveMessagesMarker.Auto())
            {
                _serverBroadcast?.DrainDeferred();
                _clientBroadcast?.DrainDeferred();

                if (_transportLayer != null)
                    _transportLayer.ReceiveMessages(delta);
            }

            using (_receiveFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnFixedUpdate();
            }

            using (_receivePostFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPostFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnPostFixedUpdate();
            }

            using (_onBatchMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnBatch();

                if (clientConnected)
                    _clientModules.TriggerOnBatch();
            }

            using (_onPostBatchMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPostBatch();

                if (clientConnected)
                    _clientModules.TriggerOnPostBatch();
            }

            using (_onSendMessagesMarker.Auto())
            {
                SendMessagesNow(delta);
            }

            ProcessPendingNetworkCleanup();

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            Statistics.MarkEndOfSampling();
#endif
        }

        private void ProcessPendingNetworkCleanup()
        {
            if (_isCleaningClient)
            {
                if (_preserveClientStateForHostMigration ||
                    ((isPromotingToServer || isTranferingToNewServer) && !_hostMigrationRollbackInProgress))
                {
                    _isCleaningClient = false;
                }
                else if (_clientModules.Cleanup())
                {
                    _clientModules.UnregisterModules();
                    CleanupClientModules();
                    TriggerUnsubscribeEvents(false);
                    _isCleaningClient = false;
                }
            }

            if (_isCleaningServer)
            {
                if (isPromotingToServer && !_hostMigrationRollbackInProgress)
                {
                    _isCleaningServer = false;
                }
                else if (_serverModules.Cleanup())
                {
                    _isServerTicking = false;
                    _serverModules.UnregisterModules();
                    CleanupServerModules();
                    TriggerUnsubscribeEvents(true);
                    _isCleaningServer = false;
                }
            }

            if (_hostMigrationRollbackInProgress &&
                !_isCleaningClient && !_isCleaningServer &&
                clientState == ConnectionState.Disconnected &&
                serverState == ConnectionState.Disconnected)
                _hostMigrationRollbackInProgress = false;
        }

        public void FlushBatchedRPCs()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.FlushBatchRPCs();

            if (clientConnected)
                _clientModules.FlushBatchRPCs();
        }

        private void OnDestroy()
        {
            UnityLatestUpdate.onPostLatestUpdate -= FlushImmediateRPCsLate;

            if (_transport)
            {
                StopClient();
                StopServer();

                if (_clientModules.hasModules)
                {
                    // drain while the PlayersBroadcaster bridge is still attached;
                    // module Disable order would detach it before the broadcaster's own drain
                    _clientBroadcast?.DrainDeferred();
                    _clientModules.UnregisterModules();
                }

                if (_serverModules.hasModules)
                {
                    _serverBroadcast?.DrainDeferred();
                    _isServerTicking = false;
                    _serverModules.UnregisterModules();
                }
            }

#if ADDRESSABLES_PURRNET_SUPPORT
            if (_addressableNetworkPrefabs)
                _addressableNetworkPrefabs.ReleaseAll();
#endif

            TeardownTransportLayer();
        }

        /// <summary>
        /// Compares the scene with the scene ID.
        /// Scene is a unity scene and SceneID is a network scene.
        /// </summary>
        /// <param name="scene">Unity scene to compare.</param>
        /// <param name="sceneID">Network scene to compare.</param>
        /// <returns>Whether the sceneID is linked to the unity scene.</returns>
        public bool MatchesSceneID(Scene scene, SceneID sceneID)
        {
            if (sceneModule.TryGetSceneID(scene, out var id))
                return id == sceneID;
            return false;
        }

        /// <summary>
        /// Tries to get the scene ID of the given scene.
        /// </summary>
        /// <param name="scene">Unity scene to get the scene ID of.</param>
        /// <param name="sceneID">The scene ID if found.</param>
        /// <returns>Whether the scene ID was found.</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneID)
        {
            return sceneModule.TryGetSceneID(scene, out sceneID);
        }

        /// <summary>
        /// Tries to get the scene of the given scene ID.
        /// </summary>
        /// <param name="sceneID">The scene ID to get the scene of.</param>
        /// <param name="scene">The scene if found.</param>
        /// <returns>Whether the scene was found.</returns>
        public bool TryGetScene(SceneID sceneID, out Scene scene)
        {
            if (sceneModule.TryGetSceneState(sceneID, out var state))
            {
                scene = state.scene;
                return true;
            }

            scene = default;
            return false;
        }

        /// <summary>
        /// Returns all the scenes of a given player
        /// </summary>
        /// <param name="playerId">PlayerID for the player whose scenes you want</param>
        /// <param name="scenes">An array of the scenes</param>
        /// <returns></returns>
        public bool TryGetPlayerScenes(PlayerID playerId, out SceneID[] scenes)
        {
            scenes = null;

            if (scenePlayersModule == null || playerId == default)
                return false;

            if (scenePlayersModule.TryGetScenesForPlayer(playerId, out scenes))
                return true;

            return false;
        }

        /// <summary>
        /// Tries to get the scene state of the given scene ID.
        /// </summary>
        /// <param name="sceneID">The scene ID to get the state of.</param>
        /// <param name="state">The state if found.</param>
        /// <returns>Whether the state was found.</returns>
        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            return sceneModule.TryGetSceneState(sceneID, out state);
        }

        /// <summary>
        /// Starts the server.
        /// This will start the transport server.
        /// </summary>
        public void StartServer()
        {
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");
            
            _lastSendTime = 0d;
            _transport.StartServer(this);
        }

        private const float PromoteToServerStartRetryIntervalSeconds = 0.1f;

        public bool isPromotingToServer { get; private set; }

        private bool _isPromotionSimulationPaused;

        /// <summary>
        /// The authoritative migration session advertised by this manager. On a client this is
        /// populated only after the server's login response matches the requested transition.
        /// </summary>
        public HostMigrationTransitionOptions hostMigrationSession => _hostMigrationSession;

        /// <summary>
        /// A configured migration session always forces denial on version mismatch: mixed
        /// builds cannot reconcile exact retained state. Single source of truth for every
        /// authentication path.
        /// </summary>
        public static bool ShouldDenyVersionMismatch(VersionMismatchBehaviour behaviour,
            NetworkManager manager)
        {
            return behaviour == VersionMismatchBehaviour.Deny ||
                   (manager != null && manager.hostMigrationSession.canReconcile);
        }

        /// <summary>
        /// Raised by the authoritative server only after a scoped client has completed all
        /// scene manifests, FinishSpawn transactions, package readiness barriers, and post-transfer work.
        /// </summary>
        public event Action<PlayerID, HostMigrationTransitionOptions> onHostMigrationPlayerReady;

        public delegate void HostMigrationStartedDelegate(
            HostMigrationTransitionOptions options, bool promoting);

        public delegate void HostMigrationCompletedDelegate(
            HostMigrationTransitionOptions options, bool promoting,
            HostMigrationTransitionResult result);

        public delegate void HostMigrationRosterProgressDelegate(
            int readyPlayers, int totalPlayers);

        /// <summary>
        /// UI-facing: a promotion (promoting=true) or transfer (false) actually engaged, after
        /// all validation. Show your "migrating host" overlay here. Every started event is
        /// followed by exactly one <see cref="onHostMigrationCompleted"/> for the same attempt.
        /// </summary>
        public event HostMigrationStartedDelegate onHostMigrationStarted;

        /// <summary>
        /// UI-facing: the engaged promotion/transfer finished with the given result. An
        /// <see cref="HostMigrationTransitionStatus.Indeterminate"/> result means "call the
        /// same method again"; everything else is terminal for this attempt.
        /// </summary>
        public event HostMigrationCompletedDelegate onHostMigrationCompleted;

        /// <summary>
        /// UI-facing, promoted-server side: roster progress changed ("X of Y players ready").
        /// Fires when a retained player finishes migrating or is confirmed departed. Totals
        /// count reconnecting human peers only (the promoted local player included).
        /// </summary>
        public event HostMigrationRosterProgressDelegate onHostMigrationRosterProgress;

        private bool _hostMigrationEventEngaged;
        private HostMigrationTransitionOptions _hostMigrationEventOptions;
        private bool _hostMigrationEventPromoting;

        private void FireHostMigrationStarted(HostMigrationTransitionOptions options, bool promoting)
        {
            _hostMigrationEventEngaged = true;
            _hostMigrationEventOptions = options;
            _hostMigrationEventPromoting = promoting;
            try
            {
                onHostMigrationStarted?.Invoke(options, promoting);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        private HostMigrationTransitionResult FireHostMigrationCompleted(
            HostMigrationTransitionResult result)
        {
            if (!_hostMigrationEventEngaged)
                return result;

            _hostMigrationEventEngaged = false;
            try
            {
                onHostMigrationCompleted?.Invoke(
                    _hostMigrationEventOptions, _hostMigrationEventPromoting, result);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            return result;
        }

        private void FireHostMigrationRosterProgress()
        {
            var handler = onHostMigrationRosterProgress;
            if (handler == null || _serverPlayersManager == null)
                return;

            var ready = _serverPlayersManager.readyHostMigrationPlayerCount;
            var total = ready + _serverPlayersManager.pendingHostMigrationPlayers.Count;
            try
            {
                handler(ready, total);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        /// <summary>
        /// Expected players that have not yet completed this host's scoped migration, or been
        /// authoritatively declared departed. This list is never aged out by a timeout.
        /// </summary>
        public IReadOnlyList<PlayerID> pendingHostMigrationPlayers =>
            _serverPlayersManager?.pendingHostMigrationPlayers ?? Array.Empty<PlayerID>();

        /// <summary>
        /// The authoritative retained migration membership, including ready peers, pending peers,
        /// bots, and the promoted local identity. Entries leave this list only through an exact
        /// departure/final-roster decision or the normal authoritative player-left path.
        /// </summary>
        public IReadOnlyList<PlayerID> retainedHostMigrationPlayers =>
            _serverPlayersManager?.retainedHostMigrationPlayers ?? Array.Empty<PlayerID>();

        /// <summary>
        /// True while the promoted server is preserving membership for at least one expected
        /// player whose migration outcome is not yet authoritative.
        /// </summary>
        public bool isHostMigrationRosterPending => pendingHostMigrationPlayers.Count > 0;

        internal bool NotifyHostMigrationPlayerReady(PlayerID player,
            HostMigrationTransitionOptions transition)
        {
            if (!isServer || !transition.canReconcile || transition != _hostMigrationSession)
                return false;

            if (_serverPlayersManager == null ||
                !_serverPlayersManager.AcceptHostMigrationPlayerReady(
                    player, transition, out var becameReady))
                return false;

            if (becameReady)
            {
                _serverPlayersManager.ReleaseExactOutboundBarrier(player, transition);
                onHostMigrationPlayerReady?.Invoke(player, transition);
                FireHostMigrationRosterProgress();
            }
            return true;
        }

        internal bool TryBeginHostMigrationServerBaselineCapture(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            failure = null;
            if (!isServer || !transition.canReconcile || transition != _hostMigrationSession ||
                _serverPlayersManager == null ||
                !_serverPlayersManager.IsPendingRetainedHostMigrationPlayer(player, transition) ||
                !_serverPlayersManager.HasExactOutboundBarrier(player, transition))
            {
                failure = $"player {player} has no active exact server-baseline scope for {transition}";
                return false;
            }

            try
            {
                FlushBatchedRPCs();
            }
            catch (Exception exception)
            {
                failure = $"ordinary RPC staging before package baselines failed: {exception.Message}";
                return false;
            }

            if (!_serverPlayersManager.BeginExactPackageBaselineCapture(
                    player, transition, out failure))
                return false;

            return true;
        }

        internal bool TryPrepareHostMigrationServerBaselines(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            failure = null;

            List<Exception> failures = null;
            var commit = false;
            var playersManager = _serverPlayersManager;
            try
            {
                if (!isServer || !transition.canReconcile || transition != _hostMigrationSession ||
                    playersManager == null ||
                    !playersManager.IsPendingRetainedHostMigrationPlayer(player, transition) ||
                    !playersManager.HasExactOutboundBarrier(player, transition))
                {
                    failures = new List<Exception>
                    {
                        new InvalidOperationException(
                            $"player {player} lost its active exact server-baseline scope for {transition}")
                    };
                }
                else
                {
                    _preparingHostMigrationBaselinePlayer = player;
                    _preparingHostMigrationBaselineTransition = transition;
                    var identities = _serverModules.TryGetModule(out HierarchyFactory hierarchyFactory)
                        ? hierarchyFactory.CaptureSpawnedIdentitySnapshot()
                        : new List<NetworkIdentity>();
                    for (var i = 0; i < identities.Count; i++)
                    {
                        var identity = identities[i];
                        if (!identity || !ReferenceEquals(identity.networkManager, this) ||
                            !identity.IsSpawned(true))
                            continue;

                        identity.TriggerPrepareHostMigrationServerBaseline(
                            player, transition, ref failures);
                    }

                    commit = failures == null;
                }
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
            finally
            {
                _preparingHostMigrationBaselinePlayer = null;
                _preparingHostMigrationBaselineTransition = default;
                try
                {
                    FlushBatchedRPCs();
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                    commit = false;
                }

                string finishFailure = null;
                if (playersManager == null ||
                    !playersManager.FinishExactPackageBaselineCapture(
                        player, transition, commit && failures == null, out finishFailure))
                {
                    failures ??= new List<Exception>();
                    failures.Add(new InvalidOperationException(finishFailure ??
                        "the server player module disappeared before baseline capture could finish"));
                    commit = false;
                }
            }

            if (failures == null && commit)
                return true;

            failure = failures == null
                ? "package baseline capture did not commit"
                : new AggregateException(
                    $"One or more package baselines failed for player {player} in {transition}.",
                    failures).Message;
            return false;
        }

        internal bool IsPreparingHostMigrationServerBaseline(PlayerID player) =>
            _preparingHostMigrationBaselinePlayer == player &&
            _preparingHostMigrationBaselineTransition.canReconcile &&
            _preparingHostMigrationBaselineTransition == _hostMigrationSession;

        internal bool TryPublishHostMigrationServerBaselines(PlayerID player,
            HostMigrationTransitionOptions transition, out string failure)
        {
            if (!isServer || transition != _hostMigrationSession ||
                _serverPlayersManager == null ||
                !_serverPlayersManager.IsPendingRetainedHostMigrationPlayer(player, transition))
            {
                failure = $"player {player} is no longer a pending baseline target for {transition}";
                return false;
            }

            return _serverPlayersManager.PublishExactPackageBaselines(
                player, transition, out failure);
        }

        /// <summary>
        /// Applies an exact-scoped, authoritative departure. Normal player-left, scene, ownership,
        /// and despawn policies run only at this boundary. By default stragglers are confirmed
        /// automatically after <see cref="HostMigrationTransitionOptions.playerReclaimTimeoutSeconds"/>;
        /// disable that timeout to require this explicit call.
        /// </summary>
        public bool ConfirmHostMigrationPlayerDeparture(PlayerID player,
            HostMigrationTransitionOptions transition)
        {
            if (!isServer)
                throw new InvalidOperationException(
                    "Host-migration departures can only be confirmed by the active server.");

            var departed = transition.canReconcile && transition == _hostMigrationSession &&
                           _serverPlayersManager != null &&
                           _serverPlayersManager.ConfirmHostMigrationPlayerDeparture(player, transition);
            if (departed)
                FireHostMigrationRosterProgress();
            return departed;
        }

        /// <summary>
        /// Reconciles the original expected roster against an authoritative active-player list.
        /// Only players from that migration's expected roster can be removed; players who joined
        /// after promotion are not affected. Returns the number of confirmed departures.
        /// </summary>
        public int FinalizeHostMigrationRoster(HostMigrationTransitionOptions transition,
            IReadOnlyList<PlayerID> activePlayers)
        {
            if (!isServer)
                throw new InvalidOperationException(
                    "A host-migration roster can only be finalized by the active server.");
            if (activePlayers == null)
                throw new ArgumentNullException(nameof(activePlayers));
            if (!transition.canReconcile || transition != _hostMigrationSession ||
                _serverPlayersManager == null)
                return 0;

            var removed = _serverPlayersManager.FinalizeHostMigrationRoster(transition, activePlayers);
            if (removed > 0)
                FireHostMigrationRosterProgress();
            return removed;
        }

        internal HostMigrationTransitionOptions expectedHostMigrationSession =>
            _expectedHostMigrationSession;

        internal bool isHostMigrationSessionValidated =>
            _receivedHostMigrationSession && _hostMigrationSessionMatched;

        internal bool hasReceivedHostMigrationSession => _receivedHostMigrationSession;

        /// <summary>
        /// Configures the authoritative migration descriptor for an already-running host.
        /// Promotion overloads configure this automatically. Pass default to disable migration
        /// reconciliation; partially specified descriptors are rejected.
        /// </summary>
        public void ConfigureHostMigrationSession(HostMigrationTransitionOptions options)
        {
            ValidateHostMigrationTransitionOptions(options);
            _hostMigrationSession = options;
        }

        private void ValidateHostMigrationTransitionOptions(HostMigrationTransitionOptions options)
        {
            if (!options.isDefault && !options.canReconcile)
            {
                throw new ArgumentException(
                    "Host migration options must provide both a non-empty sessionId and a non-zero epoch, or be default.",
                    nameof(options));
            }

            if (options.canReconcile && _networkRules && !_networkRules.IsHostMigrationEnabled())
            {
                throw new ArgumentException(
                    "Host migration is disabled. Enable it in your NetworkRules asset " +
                    "(Host Migration Rules > Enable Host Migration).",
                    nameof(options));
            }
        }

        internal void ReceiveHostMigrationSession(HostMigrationTransitionOptions session)
        {
            _advertisedHostMigrationSession = session;
            _receivedHostMigrationSession = true;
            _hostMigrationSessionMatched = _expectedHostMigrationSession.canReconcile &&
                                           session.canReconcile &&
                                           session == _expectedHostMigrationSession;

            if (!isServer)
                _hostMigrationSession = _hostMigrationSessionMatched ? session : default;

            if (_clientModules.TryGetModule(out HierarchyFactory hierarchyFactory))
                hierarchyFactory.ReceiveHostMigrationSession(session, _hostMigrationSessionMatched);
        }

        private bool IsClientHostMigrationReconciliationComplete()
        {
            if (!_expectedHostMigrationSession.canReconcile)
                return true;
            if (!_receivedHostMigrationSession)
                return false;
            if (!_hostMigrationSessionMatched)
                return true;

            if (_clientPlayersManager != null &&
                !_clientPlayersManager.HasValidatedHostMigrationTransferSnapshot(
                    _expectedHostMigrationSession))
                return false;

            if (_clientModules.TryGetModule(out ScenesModule scenesModule) &&
                !scenesModule.isTransferReconciliationComplete)
                return false;

            if (_clientModules.TryGetModule(out GlobalOwnershipModule ownershipModule) &&
                !ownershipModule.isTransferReconciliationComplete)
                return false;

            return !_clientModules.TryGetModule(out HierarchyFactory hierarchyFactory) ||
                   hierarchyFactory.isTransferReconciliationComplete;
        }

        private async Task<HostMigrationTransitionResult?> WaitForClientHostMigrationReconciliation(
            double deadline, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_expectedHostMigrationSession.canReconcile &&
                    _receivedHostMigrationSession && !_hostMigrationSessionMatched)
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        $"The connected server advertised host-migration session " +
                        $"{_advertisedHostMigrationSession}, but this transfer requires " +
                        $"{_expectedHostMigrationSession}.");
                }

                if (_clientPlayersManager != null &&
                    _clientPlayersManager.TryGetHostMigrationTransferFailure(
                        _expectedHostMigrationSession, out var playerFailure))
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        $"Player continuity from the new host failed: {playerFailure}");
                }

                if (_clientModules.TryGetModule(out ScenesModule scenesModule))
                    scenesModule.DriveTransferReconciliation();

                if (scenesModule != null &&
                    scenesModule.TryGetTransferReconciliationFailure(out var sceneFailure))
                {
                    return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Failed,
                        $"Scene reconciliation from the new host failed: {sceneFailure.Message}",
                        sceneFailure);
                }

                if (_clientModules.TryGetModule(out GlobalOwnershipModule ownershipModule) &&
                    ownershipModule.TryGetTransferReconciliationFailure(
                        _expectedHostMigrationSession, out var ownershipFailure))
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        $"Ownership reconciliation from the new host failed: {ownershipFailure}");
                }

                if (_clientModules.TryGetModule(out HierarchyFactory hierarchyFactory) &&
                    hierarchyFactory.TryGetTransferReconciliationFailure(out var failure))
                {
                    return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Failed,
                        $"A package failed to reconcile authoritative state from the new host: {failure.Message}",
                        failure);
                }

                if (IsClientHostMigrationReconciliationComplete())
                    return null;

                if (TryGetHostMigrationInterruption(deadline, cancellationToken,
                        "Timed out waiting for the new host's authoritative scene manifests and package readiness.",
                        out var interruption))
                    return interruption;

                await UnityLatestUpdate.Yield();
            }
        }

        /// <summary>
        /// Keeps the current client modules alive while an external host migration system
        /// prepares transport state before calling PromoteToServer or TransferToNewServer.
        /// </summary>
        public void PreserveClientStateForHostMigration()
        {
            _preserveClientStateForHostMigration = true;
            _isCleaningClient = false;
        }

        /// <summary>
        /// Releases a pending host migration preservation request when migration preparation fails.
        /// </summary>
        public void ReleaseClientStateForHostMigration()
        {
            _preserveClientStateForHostMigration = false;

            if (_transportLayer != null && _transportLayer.clientState == ConnectionState.Disconnected)
                _isCleaningClient = true;
        }

        /// <summary>
        /// Transitions the current NetworkManager instance into acting as a server.
        /// This method is used to promote the local instance from a client state
        /// into a server state, enabling server-specific functionalities.
        /// Great for host migration.
        /// It's your responsibility to prepare the transport for this transition.
        /// </summary>
        [ContextMenu("Promote To Server"), PurrContextButton]
        public async void PromoteToServer()
        {
            var result = await PromoteToServerAsync();
            LogLegacyHostMigrationFailure(nameof(PromoteToServer), result);
        }

        /// <summary>
        /// Promotes this client to a server and completes with a bounded, typed result.
        /// The timeout covers shutdown, server startup, transport authentication, and—when
        /// configured as a listen host—the promoted host client's local-player readiness.
        /// </summary>
        public Task<HostMigrationTransitionResult> PromoteToServerAsync(float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default) =>
            PromoteToServerAsync(default, timeoutSeconds, cancellationToken);

        /// <summary>
        /// Promotes this client and advertises an authoritative scoped migration epoch to
        /// reconnecting peers. Default options preserve legacy behavior without reconciliation.
        /// </summary>
        public async Task<HostMigrationTransitionResult> PromoteToServerAsync(
            HostMigrationTransitionOptions options, float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            return FireHostMigrationCompleted(
                await PromoteToServerAsyncCore(options, timeoutSeconds, cancellationToken));
        }

        private async Task<HostMigrationTransitionResult> PromoteToServerAsyncCore(
            HostMigrationTransitionOptions options, float timeoutSeconds,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateHostMigrationTransitionOptions(options);
            }
            catch (ArgumentException e)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState, e.Message, e);
            }

            if (isPromotingToServer || isTranferingToNewServer || _hostMigrationRollbackInProgress)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.AlreadyInProgress,
                    "Another host migration transition is already in progress.");
            }

            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0f)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState,
                    "Host migration timeout must be greater than zero.");
            }

            if (_transportLayer is IHostMigrationTransport
                {
                    hasIndeterminateHostMigrationActivation: true
                })
            {
                if (!options.isDefault && options != _hostMigrationSession)
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.InvalidState,
                        $"The pending activation belongs to host-migration session " +
                        $"{_hostMigrationSession}; it cannot be resumed as {options}.");
                }

                return await ResumeIndeterminateHostPromotion(timeoutSeconds, cancellationToken);
            }

            string rosterFailure = null;
            if (options.canReconcile &&
                (_clientPlayersManager == null ||
                 !_clientPlayersManager.ValidateExpectedHostMigrationRoster(options,
                     out rosterFailure)))
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState,
                    _clientPlayersManager == null
                        ? "A scoped host promotion requires retained client player state."
                        : rosterFailure);
            }

            string sceneMembershipFailure = null;
            if (options.canReconcile &&
                (_clientScenePlayersModule == null ||
                 !_clientScenePlayersModule.ValidateExactPromotionSceneMembership(
                     options, out sceneMembershipFailure)))
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.InvalidState,
                    _clientScenePlayersModule == null
                        ? "A scoped host promotion requires retained client scene state."
                        : sceneMembershipFailure);
            }

            if (options.canReconcile &&
                !TryValidateExactAuthoritySwitchPreflight(
                    promotion: true, out var authoritySwitchFailure))
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.InvalidState,
                    authoritySwitchFailure);
            }

            string retainedConnectionCookie = null;
            if (_clientModules.TryGetModule(out AuthModule retainedClientAuth))
            {
                retainedConnectionCookie = retainedClientAuth.clientConnectionCookie;
            }

            if (options.canReconcile && _networkRules && _networkRules.ShouldMigrateAsHost() &&
                string.IsNullOrEmpty(retainedConnectionCookie))
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.InvalidState,
                    "Exact listen-host promotion requires the retained client's authentication cookie.");
            }

            if (serverState != ConnectionState.Disconnected)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState,
                    "Cannot promote to server while the server role is already active.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                CancelPreparedHostMigrationTransport();
                ReleaseClientStateForHostMigration();
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Cancelled,
                    "Server promotion was cancelled before it started.");
            }

            isPromotingToServer = true;
            _isPromotionSimulationPaused = true;
            FireHostMigrationStarted(options, promoting: true);
            bool succeeded = false;
            bool preserveForActivationReconciliation = false;
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;

            try
            {
                _preserveClientStateForHostMigration = false;
                _hostMigrationSession = options;
                _promotedListenClientConnectionCookie = retainedConnectionCookie;

                StopServer();
                StopClient();

                var interrupted = await WaitForHostMigrationCondition(
                    () => (_transportLayer == null ||
                           _transportLayer.clientState == ConnectionState.Disconnected) &&
                          (_transportLayer == null ||
                           _transportLayer.listenerState == ConnectionState.Disconnected),
                    deadline, cancellationToken, "Timed out waiting for existing network roles to stop.");
                if (interrupted.HasValue)
                    return interrupted.Value;

                ProcessPendingNetworkCleanup();

                BeginPromotedServerAdmissionGate();
                StartServer();

                interrupted = await RetryHostMigrationRoleAsync(
                    () => serverState == ConnectionState.Connected,
                    () => TryGetHostMigrationTransportFailure(true, out var transportFailure)
                        ? transportFailure
                        : (HostMigrationTransitionResult?)null,
                    () =>
                    {
                        if (serverState == ConnectionState.Disconnected)
                            _transport.StartServerInternalOnly();
                        return Task.FromResult<HostMigrationTransitionResult?>(null);
                    },
                    PromoteToServerStartRetryIntervalSeconds,
                    deadline, cancellationToken,
                    "Timed out starting the promoted server.");
                if (interrupted.HasValue)
                    return interrupted.Value;

                _serverModules.PostPromoteToServer();
                var packageReadinessFailure = await WaitForPromotedPackageReadiness(
                    deadline, cancellationToken);
                if (packageReadinessFailure.HasValue)
                    return packageReadinessFailure.Value;

                if (_serverModules.TryGetModule(out TickManager promotedTickManager))
                    promotedTickManager.RebaseAfterSimulationPause();
                _isPromotionSimulationPaused = false;
                _isCleaningClient = false;
                ReleasePromotedServerAdmissionGate();

                if (_networkRules && _networkRules.ShouldMigrateAsHost())
                {
                    _expectedHostMigrationSession = options;
                    _advertisedHostMigrationSession = default;
                    _receivedHostMigrationSession = false;
                    _hostMigrationSessionMatched = false;
                    StartClient();

                    interrupted = await RetryHostMigrationRoleAsync(
                        () => clientState == ConnectionState.Connected && isLocalPlayerReady,
                        () =>
                        {
                            if (serverState != ConnectionState.Connected)
                            {
                                return new HostMigrationTransitionResult(
                                    HostMigrationTransitionStatus.Failed,
                                    "The provisional server disconnected while its listen client was becoming ready.");
                            }

                            if (TryGetHostMigrationTransportFailure(true, out var serverTransportFailure))
                                return serverTransportFailure;
                            if (TryGetHostMigrationTransportFailure(false, out var clientTransportFailure))
                                return clientTransportFailure;
                            return null;
                        },
                        () => RestartHostMigrationClientAttempt(deadline, cancellationToken,
                            "Timed out stopping a failed promoted-host client connection attempt."),
                        TransferToNewServerConnectRetryIntervalSeconds,
                        deadline, cancellationToken,
                        "Timed out reconnecting the promoted host client.");
                    if (interrupted.HasValue)
                        return interrupted.Value;

                    if (options.canReconcile &&
                        !TryValidatePromotedListenClientIdentity(out var identityFailure))
                    {
                        return new HostMigrationTransitionResult(
                            HostMigrationTransitionStatus.Failed, identityFailure);
                    }

                    if (options.canReconcile)
                    {
                        interrupted = await WaitForClientHostMigrationReconciliation(
                            deadline, cancellationToken);
                        if (interrupted.HasValue)
                            return interrupted.Value;

                        _clientModules.PostTransferToNewServer();
                        if (_hostMigrationSessionMatched)
                        {
                            _clientPlayersManager?.SendHostMigrationClientReady(options);
                            interrupted = await WaitForHostMigrationReadyAcceptance(
                                options, deadline, cancellationToken);
                            if (interrupted.HasValue)
                                return interrupted.Value;

                            _clientPlayersManager?.ResetHostMigrationTransferReconciliation();
                        }
                    }
                }

                if (!ArePromotedHostRolesReady())
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "A promoted host role disconnected before transport activation could start.");
                }

                var activationFailure = await ActivateHostMigrationTransportBestEffort(
                    deadline, cancellationToken);
                if (activationFailure.HasValue)
                {
                    preserveForActivationReconciliation =
                        activationFailure.Value.status == HostMigrationTransitionStatus.Indeterminate;
                    return activationFailure.Value;
                }

                if (!ArePromotedHostRolesReady())
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "A promoted host role disconnected while transport activation was in flight.");
                }

                MarkPromotedLocalPlayerReady(options);
                ClearHostMigrationSessionExpectations(clearConfiguredSession: false);
                succeeded = true;
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Succeeded);
            }
            catch (Exception e)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Failed,
                    "Server promotion failed.", e);
            }
            finally
            {
                if (!succeeded && !preserveForActivationReconciliation)
                {
                    ClearPromotedServerAdmissionGate();
                    _hostMigrationRollbackInProgress = true;
                }
                try
                {
                    if (!succeeded && !preserveForActivationReconciliation)
                        await CleanupFailedHostMigrationTransition();
                }
                finally
                {
                    _promotedListenClientConnectionCookie = null;
                    _isPromotionSimulationPaused = false;
                    isPromotingToServer = false;
                    if (!succeeded && !_isCleaningClient && !_isCleaningServer &&
                        clientState == ConnectionState.Disconnected &&
                        serverState == ConnectionState.Disconnected)
                        _hostMigrationRollbackInProgress = false;
                }
            }
        }

        private async Task<HostMigrationTransitionResult> ResumeIndeterminateHostPromotion(
            float timeoutSeconds, CancellationToken cancellationToken)
        {
            isPromotingToServer = true;
            FireHostMigrationStarted(_hostMigrationSession, promoting: true);
            bool succeeded = false;
            bool preserveForActivationReconciliation = false;
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;

            try
            {
                if (!ArePromotedHostRolesReady())
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "The preserved provisional host roles were lost before transport activation could be reconciled.");
                }

                var activationFailure = await ActivateHostMigrationTransportBestEffort(
                    deadline, cancellationToken);
                if (activationFailure.HasValue)
                {
                    preserveForActivationReconciliation =
                        activationFailure.Value.status == HostMigrationTransitionStatus.Indeterminate;
                    return activationFailure.Value;
                }

                if (!ArePromotedHostRolesReady())
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "A preserved promoted host role disconnected while transport activation was in flight.");
                }

                MarkPromotedLocalPlayerReady(_hostMigrationSession);
                ClearHostMigrationSessionExpectations(clearConfiguredSession: false);
                succeeded = true;
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Succeeded);
            }
            catch (Exception e)
            {
                preserveForActivationReconciliation = true;
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.Indeterminate,
                    "Relay activation reconciliation failed without an authoritative outcome.", e);
            }
            finally
            {
                if (!succeeded && !preserveForActivationReconciliation)
                    _hostMigrationRollbackInProgress = true;
                try
                {
                    if (!succeeded && !preserveForActivationReconciliation)
                        await CleanupFailedHostMigrationTransition();
                }
                finally
                {
                    isPromotingToServer = false;
                    if (!succeeded && !preserveForActivationReconciliation &&
                        !_isCleaningClient && !_isCleaningServer &&
                        clientState == ConnectionState.Disconnected &&
                        serverState == ConnectionState.Disconnected)
                        _hostMigrationRollbackInProgress = false;
                }
            }
        }

        private bool ArePromotedHostRolesReady()
        {
            if (serverState != ConnectionState.Connected)
                return false;

            return !_networkRules || !_networkRules.ShouldMigrateAsHost() ||
                   (clientState == ConnectionState.Connected && isLocalPlayerReady);
        }

        private async Task<HostMigrationTransitionResult?> WaitForPromotedPackageReadiness(
            double deadline, CancellationToken cancellationToken)
        {
            if (!_serverModules.TryGetModule(out HierarchyFactory hierarchyFactory))
                return null;

            var readiness = hierarchyFactory.GetPromotionReadinessTask();
            while (!readiness.IsCompleted)
            {
                if (serverState != ConnectionState.Connected)
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "The promoted server disconnected while retained packages were reconciling authority.");
                }

                if (TryGetHostMigrationTransportFailure(true, out var transportFailure))
                    return transportFailure;

                if (TryGetHostMigrationInterruption(deadline, cancellationToken,
                        "Timed out waiting for retained packages to complete server promotion.",
                        out var interruption))
                    return interruption;

                await UnityLatestUpdate.Yield();
            }

            if (readiness.IsCanceled)
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.Failed,
                    "A retained package cancelled server-promotion reconciliation.");
            }

            if (!readiness.IsFaulted)
                return null;

            var exception = readiness.Exception?.Flatten();
            return new HostMigrationTransitionResult(
                HostMigrationTransitionStatus.Failed,
                $"A retained package could not safely complete server promotion: " +
                $"{exception?.GetBaseException().Message ?? "unknown package failure"}",
                exception);
        }

        private void BeginPromotedServerAdmissionGate()
        {
            _deferredPromotedServerAdmission.Clear();
            _deferredPromotedServerConnectionCount = 0;
            _deferredPromotedServerAdmissionBytes = 0;
            _deferPromotedServerAdmission = true;
        }

        private void ReleasePromotedServerAdmissionGate()
        {
            if (!_deferPromotedServerAdmission)
                return;

            _deferPromotedServerAdmission = false;
            for (var i = 0; i < _deferredPromotedServerAdmission.Count; i++)
            {
                var deferred = _deferredPromotedServerAdmission[i];
                if (!deferred.isData)
                    _serverModules.OnNewConnection(deferred.connection, true);
            }

            for (var i = 0; i < _deferredPromotedServerAdmission.Count; i++)
            {
                var deferred = _deferredPromotedServerAdmission[i];
                if (deferred.isData)
                    _serverModules.OnDataReceived(deferred.connection, deferred.data, true);
            }

            _deferredPromotedServerAdmission.Clear();
            _deferredPromotedServerConnectionCount = 0;
            _deferredPromotedServerAdmissionBytes = 0;
        }

        private void ClearPromotedServerAdmissionGate()
        {
            _deferPromotedServerAdmission = false;
            _deferredPromotedServerAdmission.Clear();
            _deferredPromotedServerConnectionCount = 0;
            _deferredPromotedServerAdmissionBytes = 0;
        }

        private bool HasDeferredPromotedServerConnection(Connection connection)
        {
            for (var i = 0; i < _deferredPromotedServerAdmission.Count; i++)
            {
                var deferred = _deferredPromotedServerAdmission[i];
                if (!deferred.isData && deferred.connection == connection)
                    return true;
            }

            return false;
        }

        private void DropDeferredPromotedServerConnection(Connection connection)
        {
            for (var i = _deferredPromotedServerAdmission.Count - 1; i >= 0; i--)
            {
                var deferred = _deferredPromotedServerAdmission[i];
                if (deferred.connection != connection)
                    continue;

                if (deferred.isData)
                    _deferredPromotedServerAdmissionBytes -= deferred.data.length;
                else
                    _deferredPromotedServerConnectionCount--;

                _deferredPromotedServerAdmission.RemoveAt(i);
            }
        }

        private void MarkPromotedLocalPlayerReady(HostMigrationTransitionOptions transition)
        {
            var retainedPlayer = _serverPlayersManager?.promotedLocalPlayerId;
            if (transition.canReconcile && retainedPlayer.HasValue)
            {
                NotifyHostMigrationPlayerReady(retainedPlayer.Value, transition);
            }

            _serverPlayersManager?.ClearPromotedLocalPlayerId();

            if (transition.canReconcile && transition.playerReclaimTimeoutSeconds > 0f &&
                isHostMigrationRosterPending)
            {
                _hostMigrationRosterDeadline = Time.realtimeSinceStartupAsDouble +
                                               transition.playerReclaimTimeoutSeconds;
            }
        }

        private void ProcessHostMigrationRosterTimeout()
        {
            if (_hostMigrationRosterDeadline <= 0d)
                return;

            if (!isServer)
            {
                _hostMigrationRosterDeadline = 0d;
                return;
            }

            var pending = pendingHostMigrationPlayers;
            if (pending.Count == 0)
            {
                _hostMigrationRosterDeadline = 0d;
                return;
            }

            if (Time.realtimeSinceStartupAsDouble < _hostMigrationRosterDeadline)
                return;

            _hostMigrationRosterDeadline = 0d;
            var stragglers = new List<PlayerID>(pending);
            for (var i = 0; i < stragglers.Count; i++)
            {
                PurrLogger.LogWarning(
                    $"Host migration: player {stragglers[i]} did not reclaim their identity within " +
                    $"{_hostMigrationSession.playerReclaimTimeoutSeconds:0.#}s; confirming their departure.");
                ConfirmHostMigrationPlayerDeparture(stragglers[i], _hostMigrationSession);
            }
        }

        private bool TryValidatePromotedListenClientIdentity(out string failure)
        {
            var retainedPlayer = _serverPlayersManager?.promotedLocalPlayerId;
            var connectedPlayer = _clientPlayersManager?.localPlayerId;
            if (!retainedPlayer.HasValue)
            {
                failure = "The promoted server lost its retained local PlayerID before listen-client login.";
                return false;
            }

            if (!connectedPlayer.HasValue)
            {
                failure = "The promoted listen client did not receive a PlayerID.";
                return false;
            }

            if (connectedPlayer.Value != retainedPlayer.Value)
            {
                failure = $"The promoted listen client received PlayerID {connectedPlayer.Value}, but exact " +
                          $"continuity requires retained PlayerID {retainedPlayer.Value}.";
                return false;
            }

            failure = null;
            return true;
        }

        public bool isTranferingToNewServer { get; private set; }

        /// <summary>Correctly-spelled alias for <see cref="isTranferingToNewServer"/>.</summary>
        public bool isTransferringToNewServer => isTranferingToNewServer;

        /// <summary>True while a migration role transition or its rollback still owns the manager.</summary>
        public bool isHostMigrationTransitionInProgress =>
            isPromotingToServer || isTranferingToNewServer || _hostMigrationRollbackInProgress;

        private const float TransferToNewServerConnectRetryIntervalSeconds = 2f;
        private const double HostMigrationReadyResendIntervalSeconds = 0.5d;

        /// <summary>
        /// Transfers the current connection to a new server. This operation is asynchronous
        /// and is typically used to migrate a client to a different server while maintaining
        /// the connection state and relevant session data.
        /// It's your responsiblity to prepare the transport for the new server.
        /// </summary>
        [ContextMenu("TransferToNewServer"), PurrContextButton]
        public async void TransferToNewServer()
        {
            var result = await TransferToNewServerAsync();
            LogLegacyHostMigrationFailure(nameof(TransferToNewServer), result);
        }

        /// <summary>
        /// Transfers this client to a new server and completes after the local player is ready.
        /// The timeout covers shutdown, retries, transport authentication, and player login.
        /// </summary>
        public Task<HostMigrationTransitionResult> TransferToNewServerAsync(float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default) =>
            TransferToNewServerAsync(default, timeoutSeconds, cancellationToken);

        /// <summary>
        /// Transfers this client with an expected scoped migration epoch. Retained client state
        /// is eligible for in-place reconciliation only after the new server proves an exact match.
        /// </summary>
        public async Task<HostMigrationTransitionResult> TransferToNewServerAsync(
            HostMigrationTransitionOptions options, float timeoutSeconds = 30f,
            CancellationToken cancellationToken = default)
        {
            return FireHostMigrationCompleted(
                await TransferToNewServerAsyncCore(options, timeoutSeconds, cancellationToken));
        }

        private async Task<HostMigrationTransitionResult> TransferToNewServerAsyncCore(
            HostMigrationTransitionOptions options, float timeoutSeconds,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateHostMigrationTransitionOptions(options);
            }
            catch (ArgumentException e)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState, e.Message, e);
            }

            if (isPromotingToServer || isTranferingToNewServer || _hostMigrationRollbackInProgress)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.AlreadyInProgress,
                    "Another host migration transition is already in progress.");
            }

            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0f)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.InvalidState,
                    "Host migration timeout must be greater than zero.");
            }

            if (_transportLayer is IHostMigrationTransport
                {
                    hasIndeterminateHostMigrationActivation: true
                })
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.Indeterminate,
                    "Reconcile the possibly-active local host before transferring to a different server.");
            }

            string rosterFailure = null;
            if (options.canReconcile &&
                (_clientPlayersManager == null ||
                 !_clientPlayersManager.ValidateExpectedHostMigrationTransferRoster(
                     options, out rosterFailure)))
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.InvalidState,
                    _clientPlayersManager == null
                        ? "A scoped host transfer requires retained client player state."
                        : rosterFailure);
            }

            if (options.canReconcile &&
                !TryValidateExactAuthoritySwitchPreflight(
                    promotion: false, out var authoritySwitchFailure))
            {
                return new HostMigrationTransitionResult(
                    HostMigrationTransitionStatus.InvalidState,
                    authoritySwitchFailure);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                CancelPreparedHostMigrationTransport();
                ReleaseClientStateForHostMigration();
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Cancelled,
                    "Client transfer was cancelled before it started.");
            }

            isTranferingToNewServer = true;
            FireHostMigrationStarted(options, promoting: false);
            bool succeeded = false;
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;

            try
            {
                _preserveClientStateForHostMigration = false;
                _expectedHostMigrationSession = options;
                _advertisedHostMigrationSession = default;
                _hostMigrationSession = default;
                _receivedHostMigrationSession = false;
                _hostMigrationSessionMatched = false;

                StopClient();
                StopServer();

                var interrupted = await WaitForHostMigrationCondition(
                    () => (_transportLayer == null ||
                           _transportLayer.clientState == ConnectionState.Disconnected) &&
                          (_transportLayer == null ||
                           _transportLayer.listenerState == ConnectionState.Disconnected),
                    deadline, cancellationToken, "Timed out waiting for existing network roles to stop.");
                if (interrupted.HasValue)
                    return interrupted.Value;

                ProcessPendingNetworkCleanup();

                StartClient();

                interrupted = await RetryHostMigrationRoleAsync(
                    () => clientState == ConnectionState.Connected && isLocalPlayerReady,
                    () =>
                    {
                        if (options.canReconcile && _clientPlayersManager != null &&
                            _clientPlayersManager.TryGetHostMigrationTransferFailure(
                                options, out var playerFailure))
                        {
                            return new HostMigrationTransitionResult(
                                HostMigrationTransitionStatus.Failed,
                                $"Player continuity from the new host failed: {playerFailure}");
                        }

                        if (TryGetHostMigrationTransportFailure(false, out var transportFailure))
                            return transportFailure;
                        return null;
                    },
                    () => RestartHostMigrationClientAttempt(deadline, cancellationToken,
                        "Timed out stopping a failed client connection attempt."),
                    TransferToNewServerConnectRetryIntervalSeconds,
                    deadline, cancellationToken,
                    "Timed out connecting to the new server.");
                if (interrupted.HasValue)
                    return interrupted.Value;

                if (options.canReconcile)
                {
                    interrupted = await WaitForClientHostMigrationReconciliation(
                        deadline, cancellationToken);
                    if (interrupted.HasValue)
                        return interrupted.Value;
                }

                _clientModules.PostTransferToNewServer();
                if (options.canReconcile && _hostMigrationSessionMatched)
                {
                    _clientPlayersManager?.SendHostMigrationClientReady(options);
                    interrupted = await WaitForHostMigrationReadyAcceptance(
                        options, deadline, cancellationToken);
                    if (interrupted.HasValue)
                        return interrupted.Value;

                    _clientPlayersManager?.ResetHostMigrationTransferReconciliation();
                }

                ClearHostMigrationSessionExpectations(clearConfiguredSession: true);
                succeeded = true;
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Succeeded);
            }
            catch (Exception e)
            {
                return new HostMigrationTransitionResult(HostMigrationTransitionStatus.Failed,
                    "Client transfer failed.", e);
            }
            finally
            {
                if (!succeeded)
                    _hostMigrationRollbackInProgress = true;
                try
                {
                    if (!succeeded)
                        await CleanupFailedHostMigrationTransition();
                }
                finally
                {
                    isTranferingToNewServer = false;
                    if (!succeeded && !_isCleaningClient && !_isCleaningServer &&
                        clientState == ConnectionState.Disconnected &&
                        serverState == ConnectionState.Disconnected)
                        _hostMigrationRollbackInProgress = false;
                }
            }
        }

        private async Task<HostMigrationTransitionResult?> WaitForHostMigrationReadyAcceptance(
            HostMigrationTransitionOptions transition, double deadline,
            CancellationToken cancellationToken)
        {
            var nextReadySendAt = Time.realtimeSinceStartupAsDouble + HostMigrationReadyResendIntervalSeconds;
            while (true)
            {
                if (_clientPlayersManager == null)
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "The client player module was unavailable while waiting for migration-ready acceptance.");
                }

                if (_clientPlayersManager.TryGetHostMigrationTransferFailure(
                        transition, out var playerFailure))
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        $"Player continuity from the new host failed: {playerFailure}");
                }

                if (_clientPlayersManager.HasHostMigrationClientReadyAcceptance(transition))
                    return null;

                if (clientState != ConnectionState.Connected)
                {
                    return new HostMigrationTransitionResult(
                        HostMigrationTransitionStatus.Failed,
                        "The new host disconnected before accepting this client's migration readiness.");
                }

                if (TryGetHostMigrationTransportFailure(false, out var transportFailure))
                    return transportFailure;

                if (TryGetHostMigrationInterruption(deadline, cancellationToken,
                        "Timed out waiting for the new host to accept this client's exact migration readiness.",
                        out var interruption))
                    return interruption;

                if (Time.realtimeSinceStartupAsDouble >= nextReadySendAt)
                {
                    _clientPlayersManager.SendHostMigrationClientReady(transition);
                    nextReadySendAt = Time.realtimeSinceStartupAsDouble + HostMigrationReadyResendIntervalSeconds;
                }

                await UnityLatestUpdate.Yield();
            }
        }

        private async Task<HostMigrationTransitionResult?> RetryHostMigrationRoleAsync(
            Func<bool> isReady,
            Func<HostMigrationTransitionResult?> failureProbe,
            Func<Task<HostMigrationTransitionResult?>> restart,
            float retryIntervalSeconds,
            double deadline,
            CancellationToken cancellationToken,
            string timeoutMessage)
        {
            while (!isReady())
            {
                var failure = failureProbe();
                if (failure.HasValue)
                    return failure;
                if (TryGetHostMigrationInterruption(deadline, cancellationToken, timeoutMessage,
                        out var interrupted))
                    return interrupted;

                var nextRetryAt = Math.Min(deadline,
                    Time.realtimeSinceStartupAsDouble + retryIntervalSeconds);
                while (!isReady() && Time.realtimeSinceStartupAsDouble < nextRetryAt)
                {
                    failure = failureProbe();
                    if (failure.HasValue)
                        return failure;
                    if (TryGetHostMigrationInterruption(deadline, cancellationToken, timeoutMessage,
                            out interrupted))
                        return interrupted;
                    await UnityLatestUpdate.Yield();
                }

                if (isReady())
                    return null;

                var restartFailure = await restart();
                if (restartFailure.HasValue)
                    return restartFailure;
            }

            return null;
        }

        private async Task<HostMigrationTransitionResult?> RestartHostMigrationClientAttempt(
            double deadline, CancellationToken cancellationToken, string stopTimeoutMessage)
        {
            MarkClientDisconnectAsLocal();
            _transport.StopClientInternalOnly();
            _isCleaningClient = false;

            var interrupted = await WaitForHostMigrationCondition(
                () => _transportLayer == null ||
                      _transportLayer.clientState == ConnectionState.Disconnected,
                deadline, cancellationToken, stopTimeoutMessage);
            if (interrupted.HasValue)
                return interrupted;

            _transport.StartClientInternalOnly();
            return null;
        }

        private static bool TryGetHostMigrationInterruption(double deadline,
            CancellationToken cancellationToken, string timeoutMessage,
            out HostMigrationTransitionResult? interruption)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                interruption = new HostMigrationTransitionResult(HostMigrationTransitionStatus.Cancelled,
                    "Host migration transition was cancelled.");
                return true;
            }

            if (Time.realtimeSinceStartupAsDouble >= deadline)
            {
                interruption = new HostMigrationTransitionResult(HostMigrationTransitionStatus.TimedOut,
                    timeoutMessage);
                return true;
            }

            interruption = null;
            return false;
        }

        private static async Task<HostMigrationTransitionResult?> WaitForHostMigrationCondition(
            Func<bool> condition, double deadline, CancellationToken cancellationToken, string timeoutMessage)
        {
            while (!condition())
            {
                if (TryGetHostMigrationInterruption(deadline, cancellationToken, timeoutMessage,
                        out var interruption))
                    return interruption;
                await UnityLatestUpdate.Yield();
            }

            return null;
        }

        private bool TryGetHostMigrationTransportFailure(bool asServer,
            out HostMigrationTransitionResult result)
        {
            if (_transportLayer is IHostMigrationTransport migrationTransport &&
                migrationTransport.TryGetHostMigrationFailure(asServer, out var failure))
            {
                result = new HostMigrationTransitionResult(HostMigrationTransitionStatus.Failed, failure);
                return true;
            }

            result = default;
            return false;
        }

        private const float HostMigrationActivationRetryIntervalSeconds = 1f;

        private async Task<HostMigrationTransitionResult?> ActivateHostMigrationTransportBestEffort(
            double deadline, CancellationToken cancellationToken)
        {
            while (true)
            {
                var failure = await ActivatePreparedHostMigrationTransport(
                    deadline, cancellationToken);
                if (!failure.HasValue ||
                    failure.Value.status != HostMigrationTransitionStatus.Indeterminate)
                    return failure;

                if (cancellationToken.IsCancellationRequested ||
                    Time.realtimeSinceStartupAsDouble >= deadline ||
                    !ArePromotedHostRolesReady())
                    return failure;

                var retryAt = Math.Min(deadline,
                    Time.realtimeSinceStartupAsDouble + HostMigrationActivationRetryIntervalSeconds);
                while (Time.realtimeSinceStartupAsDouble < retryAt &&
                       !cancellationToken.IsCancellationRequested)
                    await UnityLatestUpdate.Yield();
            }
        }

        private async Task<HostMigrationTransitionResult?> ActivatePreparedHostMigrationTransport(
            double deadline, CancellationToken cancellationToken)
        {
            if (_transportLayer is not IHostMigrationTransport migrationTransport)
                return null;

            var hasPriorIndeterminateActivation =
                migrationTransport.hasIndeterminateHostMigrationActivation;
            if (cancellationToken.IsCancellationRequested)
                return new HostMigrationTransitionResult(
                    hasPriorIndeterminateActivation
                        ? HostMigrationTransitionStatus.Indeterminate
                        : HostMigrationTransitionStatus.Cancelled,
                    hasPriorIndeterminateActivation
                        ? "A previous transport activation may have committed; cancelling this replay does not make rollback safe."
                        : "Host migration activation was cancelled.");

            var remainingSeconds = (float)(deadline - Time.realtimeSinceStartupAsDouble);
            if (remainingSeconds <= 0f)
                return new HostMigrationTransitionResult(
                    hasPriorIndeterminateActivation
                        ? HostMigrationTransitionStatus.Indeterminate
                        : HostMigrationTransitionStatus.TimedOut,
                    hasPriorIndeterminateActivation
                        ? "A previous transport activation may have committed; an expired replay budget does not make rollback safe."
                        : "Timed out before the provisional host could be activated.");

            var activation = await migrationTransport.ActivatePreparedHostMigrationAsync(
                remainingSeconds, cancellationToken);
            if (activation.succeeded)
                return null;

            var status = activation.status switch
            {
                HostMigrationTransportActivationStatus.TimedOut => HostMigrationTransitionStatus.TimedOut,
                HostMigrationTransportActivationStatus.Cancelled => HostMigrationTransitionStatus.Cancelled,
                HostMigrationTransportActivationStatus.Indeterminate => HostMigrationTransitionStatus.Indeterminate,
                _ => HostMigrationTransitionStatus.Failed
            };
            return new HostMigrationTransitionResult(status,
                string.IsNullOrWhiteSpace(activation.message)
                    ? "The transport could not activate the provisional host."
                    : activation.message);
        }

        private const float HostMigrationRollbackTimeoutSeconds = 5f;

        private void ClearHostMigrationSessionExpectations(bool clearConfiguredSession)
        {
            _expectedHostMigrationSession = default;
            _advertisedHostMigrationSession = default;
            _receivedHostMigrationSession = false;
            _hostMigrationSessionMatched = false;
            if (clearConfiguredSession)
                _hostMigrationSession = default;
        }

        private async Task CleanupFailedHostMigrationTransition()
        {
            _promotedListenClientConnectionCookie = null;
            _hostMigrationRosterDeadline = 0d;
            _preserveClientStateForHostMigration = false;
            _clientPlayersManager?.ResetHostMigrationTransferReconciliation();
            ClearHostMigrationSessionExpectations(clearConfiguredSession: true);
            CancelPreparedHostMigrationTransport();

            try
            {
                StopClient();
                StopServer();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            var rollbackDeadline = Time.realtimeSinceStartupAsDouble + HostMigrationRollbackTimeoutSeconds;
            while (((_transportLayer != null &&
                     _transportLayer.clientState != ConnectionState.Disconnected) ||
                    (_transportLayer != null &&
                     _transportLayer.listenerState != ConnectionState.Disconnected)) &&
                   Time.realtimeSinceStartupAsDouble < rollbackDeadline)
                await UnityLatestUpdate.Yield();

            if (_transportLayer == null || _transportLayer.clientState == ConnectionState.Disconnected)
                _isCleaningClient = _clientModules.hasModules;
            if (_transportLayer == null || _transportLayer.listenerState == ConnectionState.Disconnected)
                _isCleaningServer = _serverModules.hasModules;

            while ((_isCleaningClient || _isCleaningServer) &&
                   Time.realtimeSinceStartupAsDouble < rollbackDeadline)
            {
                ProcessPendingNetworkCleanup();
                if (_isCleaningClient || _isCleaningServer)
                    await UnityLatestUpdate.Yield();
            }
        }

        private void CancelPreparedHostMigrationTransport()
        {
            if (_transportLayer is IHostMigrationTransport migrationTransport)
                migrationTransport.CancelPreparedHostMigration();
        }

        private static void LogLegacyHostMigrationFailure(string operation,
            HostMigrationTransitionResult result)
        {
            if (result.succeeded || result.status == HostMigrationTransitionStatus.AlreadyInProgress)
                return;

            if (result.exception != null)
                PurrLogger.LogException(result.exception);
            else
                PurrLogger.LogError($"{operation} failed: {result.message}");
        }

        /// <summary>
        /// Starts as both a server and a client.
        /// isServer and isClient will both be true after connection is established.
        /// </summary>
        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        /// <summary>
        /// Internal method to register the server modules.
        /// Avoid calling this method directly if you're not sure what you're doing.
        /// </summary>
        public void InternalRegisterServerModules()
        {
            if (!_ready)
                Awake();

            _isServerTicking = false;
            _serverModules.RegisterModules();
            _isSubscribedServer = true;
            TriggerSubscribeEvents(true);
        }

        /// <summary>
        /// Internal method to register the client modules.
        /// Avoid calling this method directly if you're not sure what you're doing.
        /// </summary>
        public void InternalRegisterClientModules()
        {
            if (!_ready)
                Awake();

            _clientModules.RegisterModules();
            _isSubscribedClient = true;
            TriggerSubscribeEvents(false);
        }

        internal void TriggerConnectionLeft(Connection connection, bool asServer)
        {
            if (asServer)
                _serverModules.OnLostConnection(connection, true);
            else _clientModules.OnLostConnection(connection, false);
        }

        bool _isSubscribedClient;
        bool _isSubscribedServer;

        public void InternalUnregisterServerModules()
        {
            if (!_isSubscribedServer)
                return;

            _isSubscribedServer = false;
            TriggerUnsubscribeEvents(true);
        }

        private void CleanupServerModules()
        {
            if (_serverTickManager != null)
            {
                _serverTickManager.onPreTick -= OnServerPreTick;
                _serverTickManager.onTick -= OnServerTick;
                _serverTickManager.onPostTick -= OnServerPostTick;
                _serverTickManager = null;
            }

            if (_serverPlayersManager != null)
            {
                _serverPlayersManager.onPlayerJoined -= OnPlayerJoined;
                _serverPlayersManager.onPlayerLeft -= OnPlayerLeft;
                _serverPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                _serverPlayersManager = null;
            }

            _serverSceneModule = null;
            _serverScenePlayersModule = null;
            _serverDeltaModule = null;
            _serverRpcModule = null;
            _serverLODModule = null;
        }

        public void InternalUnregisterClientModules()
        {
            if (!_isSubscribedClient)
                return;

            _isSubscribedClient = false;
            TriggerUnsubscribeEvents(false);
        }

        private void CleanupClientModules()
        {
            if (_clientTickManager != null)
            {
                _clientTickManager.onPreTick -= OnClientPreTick;
                _clientTickManager.onTick -= OnClientTick;
                _clientTickManager.onPostTick -= OnClientPostTick;
                _clientTickManager = null;
            }

            if (_clientPlayersManager != null)
            {
                _clientPlayersManager.onPlayerJoined -= OnPlayerJoined;
                _clientPlayersManager.onPlayerLeft -= OnPlayerLeft;
                _clientPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                _clientPlayersManager = null;
            }

            _clientSceneModule = null;
            _clientScenePlayersModule = null;
            _clientDeltaModule = null;
            _clientRpcModule = null;
            _clientLODModule = null;
        }

        private Coroutine _clientCoroutine;

        /// <summary>
        /// Starts the client.
        /// This will start the transport client.
        /// </summary>
        public void StartClient()
        {
            clientToServerConn = null;
            if (!isTranferingToNewServer && !isPromotingToServer)
                ClearHostMigrationSessionExpectations(clearConfiguredSession: !isServer);
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");

            if (_clientCoroutine != null)
            {
                StopCoroutine(_clientCoroutine);
                _clientCoroutine = null;
            }

            _clientCoroutine = StartCoroutine(StartClientCoroutine());
        }

        IEnumerator StartClientCoroutine()
        {
            yield return null;
            while (clientState is ConnectionState.Disconnecting or ConnectionState.Connecting)
                yield return null;
            while (_isCleaningClient)
                yield return null;
            
            _lastSendTime = 0d;
            _transport.StartClient(this);
        }

        private void OnNewConnection(Connection conn, bool asServer)
        {
            if (asServer)
            {
                if (_deferPromotedServerAdmission)
                {
                    if (HasDeferredPromotedServerConnection(conn) ||
                        _deferredPromotedServerConnectionCount >=
                        MaxDeferredPromotedServerConnections)
                    {
                        PurrLogger.LogWarning(
                            $"Rejected connection {conn} during promotion: the admission gate " +
                            $"is at its {MaxDeferredPromotedServerConnections}-connection cap " +
                            "(or the connection was already deferred).");
                        _transportLayer?.CloseConnection(conn);
                        return;
                    }

                    _deferredPromotedServerAdmission.Add(
                        new DeferredPromotedServerAdmissionEvent(conn));
                    _deferredPromotedServerConnectionCount++;
                    return;
                }

                _serverModules.OnNewConnection(conn, true);
            }
            else
            {
                clientToServerConn = conn;
                _clientModules.OnNewConnection(conn, false);
            }
        }

        private void OnLostConnection(Connection conn, DisconnectReason reason, bool asServer)
        {
            if (asServer)
            {
                if (_deferPromotedServerAdmission)
                {
                    DropDeferredPromotedServerConnection(conn);
                    return;
                }

                _serverBroadcast?.DrainDeferred(conn);
                _serverModules.OnLostConnection(conn, true);
            }
            else
            {
                QueueClientDisconnected(conn, reason);
                _clientBroadcast?.DrainDeferred();
                clientToServerConn = null;
                _clientModules.OnLostConnection(conn, false);
            }
#if UNITY_EDITOR
            if (isOffline && networkRules && _stopPlayingOnDisconnect)
                EditorApplication.isPlaying = false;
#endif
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            if (asServer)
            {
                if (_deferPromotedServerAdmission)
                {
                    if (!HasDeferredPromotedServerConnection(conn) || data.length < 0 ||
                        data.length > MaxDeferredPromotedServerAdmissionBytes -
                        _deferredPromotedServerAdmissionBytes)
                    {
                        PurrLogger.LogWarning(
                            $"Dropped connection {conn} during promotion: its buffered data " +
                            "exceeded the admission gate budget (or it was never admitted).");
                        DropDeferredPromotedServerConnection(conn);
                        _transportLayer?.CloseConnection(conn);
                        return;
                    }

                    var copy = new byte[data.length];
                    Buffer.BlockCopy(data.data, data.offset, copy, 0, data.length);
                    _deferredPromotedServerAdmission.Add(
                        new DeferredPromotedServerAdmissionEvent(
                            conn, new ByteData(copy, 0, copy.Length)));
                    _deferredPromotedServerAdmissionBytes += copy.Length;
                    return;
                }

                _serverModules.OnDataReceived(conn, data, true);
            }
            else _clientModules.OnDataReceived(conn, data, false);
        }

        private void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (asServer)
            {
                isServer = state == ConnectionState.Connected;
                _serverModules.OnConnectionState(state, true);
                onServerConnectionState?.Invoke(state);
                onAnyServerConnectionState?.Invoke(state);
            }
            else
            {
                if (state == ConnectionState.Connecting)
                {
                    _clientDisconnectWasLocallyRequested = false;
                    _clientDisconnectWasNotified = false;
                    _hasPendingClientDisconnectReason = false;
                    _pendingClientDisconnectWasLocallyRequested = false;
                }

                isClient = state == ConnectionState.Connected;
                _clientModules.OnConnectionState(state, false);
                onClientConnectionState?.Invoke(state);
                onAnyClientConnectionState?.Invoke(state);

                if (state == ConnectionState.Disconnected)
                {
                    if (_hasPendingClientDisconnectReason)
                    {
                        NotifyClientDisconnected(_pendingClientDisconnectConnection,
                            _pendingClientDisconnectReason,
                            _pendingClientDisconnectWasLocallyRequested);
                    }
                    else if (_clientDisconnectWasLocallyRequested)
                    {
                        NotifyClientDisconnected(clientToServerConn ?? default,
                            DisconnectReason.ClientRequest, true);
                    }
                }
            }

            if (state == ConnectionState.Disconnected)
            {
                if (!asServer)
                    _telemetrySentClient = false;

                switch (asServer)
                {
                    case false:
                        _isCleaningClient = !_preserveClientStateForHostMigration;
                        break;
                    case true:
                        _isCleaningServer = true;
                        break;
                }
            }
        }

        private bool TryValidateExactAuthoritySwitchPreflight(bool promotion, out string failure)
        {
            if (!_clientModules.TryGetModule(out HierarchyFactory hierarchy))
            {
                failure = "An exact authority switch requires retained hierarchy state.";
                return false;
            }

            if (!hierarchy.TryValidateExactAuthoritySwitchPreflight(
                    promotion, out var queueFailure))
            {
                failure = $"Cannot begin an exact authority switch while old-authority " +
                          $"hierarchy state is unsafe: {queueFailure}.";
                return false;
            }

            if (!_clientModules.TryGetModule(out ScenesModule scenes))
            {
                failure = "An exact authority switch requires retained scene state.";
                return false;
            }

            if (!scenes.TryValidateExactAuthoritySwitchPreflight(promotion, out var sceneFailure))
            {
                failure = $"Cannot begin an exact authority switch: {sceneFailure}";
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary>
        /// Tries to get the module of the given type.
        /// </summary>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <param name="module">The module if found, otherwise the default value of the type.</param>
        /// <typeparam name="T">The type of the module.</typeparam>
        /// <returns>Whether the module was found.</returns>
        public bool TryGetModule<T>(bool asServer, out T module) where T : INetworkModule
        {
            return asServer ? _serverModules.TryGetModule(out module) : _clientModules.TryGetModule(out module);
        }

        /// <summary>
        /// Stops the server.
        /// This will stop the transport server.
        /// </summary>
        public void StopServer()
        {
            _transport.StopServer(this);

            if ((_transportLayer == null ||
                 _transportLayer.listenerState == ConnectionState.Disconnected) &&
                _serverModules.hasModules)
            {
                _isCleaningServer = true;
            }
        }

        /// <summary>
        /// Stops the client.
        /// This will stop the transport client.
        /// </summary>
        public void StopClient()
        {
            if (clientState != ConnectionState.Disconnected)
                MarkClientDisconnectAsLocal();

            if (_clientCoroutine != null)
            {
                StopCoroutine(_clientCoroutine);
                _clientCoroutine = null;
            }

            _transport.StopClient(this);

            if (!_preserveClientStateForHostMigration &&
                (_transportLayer == null ||
                 _transportLayer.clientState == ConnectionState.Disconnected) &&
                _clientModules.hasModules)
            {
                _isCleaningClient = true;
            }
        }

        private void MarkClientDisconnectAsLocal()
        {
            _clientDisconnectWasLocallyRequested = true;
        }

        private void NotifyClientDisconnected(Connection connection, DisconnectReason transportReason,
            bool wasLocallyRequested)
        {
            if (_clientDisconnectWasNotified)
                return;

            _clientDisconnectWasNotified = true;
            _hasPendingClientDisconnectReason = false;
            var reason = !wasLocallyRequested && transportReason == DisconnectReason.ClientRequest
                ? DisconnectReason.ServerRequest
                : transportReason;
            onClientDisconnected?.Invoke(new ClientDisconnectInfo(connection, reason, wasLocallyRequested));
        }

        private void QueueClientDisconnected(Connection connection, DisconnectReason reason)
        {
            if (_clientDisconnectWasNotified)
                return;

            if (!_hasPendingClientDisconnectReason)
            {
                _hasPendingClientDisconnectReason = true;
                _pendingClientDisconnectConnection = connection;
                _pendingClientDisconnectReason = reason;
                _pendingClientDisconnectWasLocallyRequested = _clientDisconnectWasLocallyRequested;
            }

            if (_transportLayer == null || _transportLayer.clientState == ConnectionState.Disconnected)
                NotifyClientDisconnected(_pendingClientDisconnectConnection,
                    _pendingClientDisconnectReason, _pendingClientDisconnectWasLocallyRequested);
        }

        public void ResetOriginalScene(Scene activeScene)
        {
            originalScene = activeScene;
            originalSceneBuildIndex = activeScene.buildIndex;
        }

        public bool IsDontDestroyOnLoad()
        {
            var scene = gameObject.scene;
            if (scene.name == "DontDestroyOnLoad")
                return true;
            return false;
        }

        public void Spawn(GameObject entry)
        {
            if (!entry)
                return;

            if (TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                TryGetSceneID(entry.scene, out var sceneID) &&
                factory.TryGetHierarchy(sceneID, out var hierarchy))
            {
                hierarchy.InternalSpawn(entry);
            }
        }

        public void CloseConnection(Connection conn)
        {
            if (isServer && _transportLayer != null)
                _transportLayer.CloseConnection(conn);
        }

        private RPCModule _clientRpcModule;
        private RPCModule _serverRpcModule;

        public int GetMTU(PlayerID playerId, Channel channel, bool asServer)
        {
            return asServer ? _serverPlayersManager.GetMTU(playerId, channel, true) : _clientPlayersManager.GetMTU(playerId, channel, false);
        }

        public bool TryGetRpcModule(bool asServer, out RPCModule module)
        {
            module = asServer ? _serverRpcModule : _clientRpcModule;
            return module != null;
        }
    }
}

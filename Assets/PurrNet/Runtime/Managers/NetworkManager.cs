#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    public delegate void OnTickDelegate(bool asServer);

    [DefaultExecutionOrder(-999)]
    public sealed partial class NetworkManager : MonoBehaviour, IRegisterModules, INetworkManager
    {
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

        [PurrDocs("systems-and-modules/network-manager/transports")] [SerializeField]
        private GenericTransport _transport;

        [PurrDocs("systems-and-modules/network-manager/network-prefabs")] [SerializeField]
        private NetworkPrefabs _networkPrefabs;

        [PurrDocs("systems-and-modules/network-manager/network-assets")] [SerializeField]
        private NetworkAssets _networkAssets;

        [PurrDocs("systems-and-modules/network-manager/network-rules")] [SerializeField]
        private NetworkRules _networkRules;

        [PurrDocs("systems-and-modules/network-manager/network-visibility")] [SerializeField]
        private NetworkVisibilityRuleSet _visibilityRules;

        [PurrDocs("systems-and-modules/network-manager/authentication")]
        [SerializeField] private AuthenticationLayer _authenticator;

        [Tooltip("Number of target ticks per second.")] [SerializeField]
        private int _tickRate = 20;

        [SerializeField, UsedImplicitly]
        private bool _patchLingeringProcessBug;

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
        public NetworkAssets networkAssets => _networkAssets;

        /// <summary>
        /// The prefab provider of the network manager.
        /// </summary>
        public IPrefabProvider prefabProvider { get; private set; }

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
        /// Occurs when the server connection state changes.
        /// </summary>
        public static event Action<ConnectionState> onAnyServerConnectionState;

        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public static event  Action<ConnectionState> onAnyClientConnectionState;

        public ITransport rawTransport => _transport ? _transport.transport : null;

        /// <summary>
        /// Unsubscribes all listeners and any other internal state.
        /// This is meant to be called manually if you encounter any caching issues due to bad unsubscribing.
        /// </summary>
        public void ResetInternalState()
        {
            onServerConnectionState = null;
            onClientConnectionState = null;
            onAnyServerConnectionState = null;
            onAnyClientConnectionState = null;

            onPreTick = null;
            onTick = null;
            onPostTick = null;

            onPlayerJoined = null;
            onPlayerLeft = null;
            onPlayerJoinedScene = null;
            onPlayerLeftScene = null;
            onPlayerLoadedScene = null;
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

                    _transport.transport.onConnected -= OnNewConnection;
                    _transport.transport.onDisconnected -= OnLostConnection;
                    _transport.transport.onConnectionState -= OnConnectionState;
                    _transport.transport.onDataReceived -= OnDataReceived;
                }

                _transport = value;

                if (_transport)
                {
                    _transport.transport.onConnected += OnNewConnection;
                    _transport.transport.onDisconnected += OnLostConnection;
                    _transport.transport.onConnectionState += OnConnectionState;
                    _transport.transport.onDataReceived += OnDataReceived;
                    _subscribed = true;
                }
                else
                {
                    _subscribed = false;
                }
            }
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

        /// <summary>
        /// The state of the server connection.
        /// This is based on the transport listener state.
        /// </summary>
        public ConnectionState serverState
        {
            get
            {
                var state = !_transport ? ConnectionState.Disconnected : _transport.transport.listenerState;
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
                var state = !_transport ? ConnectionState.Disconnected : _transport.transport.clientState;
                return state == ConnectionState.Disconnected && _isCleaningClient
                    ? ConnectionState.Disconnecting
                    : state;
            }
        }

        /// <summary>
        /// Whether the network manager is a server.
        /// </summary>
        public bool isServer { get; private set; }

        [UsedByIL]
        public static bool isServerStatic => main && main.isServer;

        [UsedByIL]
        public static bool isClientStatic => main && main.isClient;

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

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var trs = child.transform;

                var first = trs.GetComponent<NetworkIdentity>();

                child.PreparePrefabInfo(
                    pid,
                    child == first ? i : first.componentIndex,
                    shouldBePooled,
                    false
                );
            }

            ListPool<NetworkIdentity>.Destroy(children);
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

        [UsedImplicitly]
        public static void CalculateHashes()
        {
            if (_hasGeneratedAlready)
                return;

            _hasGeneratedAlready = true;

            Hasher.ClearState();
            CallAllRegisters();
        }

        [UsedImplicitly]
        static void RefreshHashes()
        {
            if (_hasGeneratedAlready)
                return;

            _hasGeneratedAlready = true;

            Hasher.ClearState();
            CallAllRegisters();

            // ReSharper disable once Unity.UnknownResource
            var hashes = Resources.Load<TextAsset>("PurrHashes");

            if (hashes == null)
            {
                PurrLogger.LogError("Failed to load PurrHashes.");
                return;
            }

            Hasher.ClearState();

            var lines = hashes.text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length != 2)
                    continue;

                var fullTypeName = parts[0];
                var hash = uint.Parse(parts[1]);

                var type = Type.GetType(fullTypeName);

                if (type == null)
                    continue;

                Hasher.Load(type, hash);
            }

            Hasher.FinishLoad(lines.Length);
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
#if UNITY_EDITOR
            static string TryFindVersion()
            {
                var packagePath = AssetDatabase.GUIDToAssetPath("0ec978dbed50a6f4b9a57580867f1fae");

                if (string.IsNullOrEmpty(packagePath))
                    return "v?";

                var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packagePath);

                if (textAsset == null)
                    return "v?";

                var json = JObject.Parse(textAsset.text);
                return 'v' + (json["version"]?.ToString() ?? "?");
            }

            version ??= TryFindVersion();
#else
            if (version == null)
            {
                var versionJson = Resources.Load<TextAsset>("PurrVersion");
                version = versionJson ? versionJson.text : "v?";
            }
#endif

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

#if !UNITY_EDITOR
            RefreshHashes();
#else
            CalculateHashes();
#endif

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
        /// The local player of the network manager.
        /// If the local player is not set, this will return the default value of the player id.
        /// </summary>
        public PlayerID localPlayer => _clientPlayersManager?.localPlayerId ?? default;

        public AuthenticationLayer authenticator => _authenticator;

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

        void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer) =>
            onPlayerJoined?.Invoke(player, isReconnect, asServer);

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

        event ValidateSpawnAction _onClientSpawnValidate;

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                _onClientSpawnValidate += value;
                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate += value;
            }
            remove
            {
                _onClientSpawnValidate -= value;
                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate -= value;
            }
        }

        public void RegisterModules(ModulesCollection modules, bool asServer)
        {
            switch (asServer)
            {
                case true when isPromotingToServer:
                    modules.MigrateFrom(_clientModules);
                    return;
                case false when isTranferingToNewServer:
                    modules.TransferToNewServer();
                    return;
            }

            var tickManager = new TickManager(_tickRate, this);

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

            var connBroadcaster = new BroadcastModule(this, asServer);

            if (asServer)
                _serverBroadcast = connBroadcaster;
            else _clientBroadcast = connBroadcaster;

            var networkCookies = new CookiesModule(_cookieScope, asServer);
            var authModule = new AuthModule(this, connBroadcaster, networkCookies);
            var playersManager = new PlayersManager(this, authModule, connBroadcaster);
            authModule.SetPlayerModule(playersManager);

            if (asServer)
            {
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
                    _serverScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _serverScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _serverScenePlayersModule = scenePlayers;

                _serverScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _serverScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
                _serverScenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
                _serverScenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
            }
            else
            {
                if (_clientScenePlayersModule != null)
                {
                    _clientScenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
                    _clientScenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
                    _clientScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _clientScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _clientScenePlayersModule = scenePlayers;

                _clientScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _clientScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
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
            var ownershipModule = new GlobalOwnershipModule(this, hierarchyV2, playersManager, scenePlayers, scenesModule);
            var rpcModule = new RPCModule(this, playersManager, hierarchyV2, ownershipModule, scenesModule);
            var networkTransform = new NetworkTransformFactory(scenesModule, scenePlayers, playersBroadcast, this, hierarchyV2);
            var colliderRollback = new ColliderRollbackFactory(tickManager, scenesModule);

            if (asServer)
            {
                if (_onClientSpawnValidate != null)
                {
                    foreach (var del in _onClientSpawnValidate.GetInvocationList())
                        hierarchyV2.onClientSpawnValidate += (ValidateSpawnAction)del;
                }
            }

            modules.AddModule(networkTransform);
            modules.AddModule(hierarchyV2);
            modules.AddModule(ownershipModule);
            modules.AddModule(rpcModule);
            modules.AddModule(new RpcRequestResponseModule(playersManager));
            modules.AddModule(colliderRollback);

            RenewSubscriptions(asServer);
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

        public static bool ShouldStart(StartFlags flags)
        {
            return (flags.HasFlag(StartFlags.Editor) && ApplicationContext.isMainEditor) ||
                   (flags.HasFlag(StartFlags.Clone) && ApplicationContext.isClone) ||
                   (flags.HasFlag(StartFlags.ClientBuild) && ApplicationContext.isClientBuild) ||
                   (flags.HasFlag(StartFlags.ServerBuild) && ApplicationContext.isServerBuild);
        }

        private void Start()
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
            _serverModules.TriggerOnUpdate();
            _clientModules.TriggerOnUpdate();

            if (_transport)
                _transport.transport.UnityUpdate(Time.deltaTime);
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

        private void OnTick()
        {
            var delta = tickModule?.tickDelta ?? Time.fixedUnscaledDeltaTime;
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.TriggerOnPreFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnPreFixedUpdate();

            if (_transport)
                _transport.transport.ReceiveMessages(delta);

            if (serverConnected)
                _serverModules.TriggerOnFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnFixedUpdate();

            if (serverConnected)
                _serverModules.TriggerOnPostFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnPostFixedUpdate();

            if (serverConnected)
                _serverModules.TriggerOnBatch();

            if (clientConnected)
                _clientModules.TriggerOnBatch();

            if (serverConnected)
                _serverModules.TriggerOnPostBatch();

            if (clientConnected)
                _clientModules.TriggerOnPostBatch();

            if (_transport)
                _transport.transport.SendMessages(delta);

            if (_isCleaningClient)
            {
                if (isPromotingToServer || isTranferingToNewServer)
                {
                    _isCleaningClient = false;
                }
                else if (_clientModules.Cleanup())
                {
                    _clientModules.UnregisterModules();
                    CleanupClientModules();
                    _isCleaningClient = false;
                }
            }

            if (_isCleaningServer && _serverModules.Cleanup())
            {
                _isServerTicking = false;
                _serverModules.UnregisterModules();
                CleanupServerModules();
                _isCleaningServer = false;
            }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            Statistics.MarkEndOfSampling();
#endif
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
            if (_transport)
            {
                StopClient();
                StopServer();

                if (clientState != ConnectionState.Disconnected)
                    _clientModules.UnregisterModules();

                if (serverState != ConnectionState.Disconnected)
                {
                    _isServerTicking = false;
                    _serverModules.UnregisterModules();
                }
            }
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
            _transport.StartServer(this);
        }

        public bool isPromotingToServer { get; private set; }

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
            try
            {
                if (isPromotingToServer)
                    return;

                if (serverState != ConnectionState.Disconnected)
                {
                    PurrLogger.LogError("Cannot promote to server, you already are a server.");
                    return;
                }

                isPromotingToServer = true;

                StopServer();
                StopClient();

                while (clientState != ConnectionState.Disconnected ||
                       serverState != ConnectionState.Disconnected)
                    await UnityLatestUpdate.Yield();

                StartServer();
                _serverModules.PostPromoteToServer();

                if (_networkRules && _networkRules.ShouldMigrateAsHost())
                    StartClient();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
                isPromotingToServer = false;
                StopClient();
                StopServer();
            }
            finally
            {
                isPromotingToServer = false;
            }
        }

        public bool isTranferingToNewServer { get; private set; }

        /// <summary>
        /// Transfers the current connection to a new server. This operation is asynchronous
        /// and is typically used to migrate a client to a different server while maintaining
        /// the connection state and relevant session data.
        /// It's your responsiblity to prepare the transport for the new server.
        /// </summary>
        [ContextMenu("TransferToNewServer"), PurrContextButton]
        public async void TransferToNewServer()
        {
            try
            {
                if (isTranferingToNewServer)
                    return;

                isTranferingToNewServer = true;

                StopClient();
                StopServer();

                while (clientState != ConnectionState.Disconnected ||
                       serverState != ConnectionState.Disconnected)
                    await UnityLatestUpdate.Yield();

                StartClient();

                while (clientState != ConnectionState.Connected)
                    await UnityLatestUpdate.Yield();

                _clientModules.PostTransferToNewServer();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
                isTranferingToNewServer = false;
                StopClient();
                StopServer();
            }
            finally
            {
                isTranferingToNewServer = false;
            }
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
                _clientTickManager.onPreTick -= OnServerPreTick;
                _clientTickManager.onTick -= OnServerTick;
                _clientTickManager.onPostTick -= OnServerPostTick;
                _clientTickManager = null;
            }

            if (_clientPlayersManager != null)
            {
                _clientPlayersManager.onPlayerJoined -= OnPlayerJoined;
                _clientPlayersManager.onPlayerLeft -= OnPlayerLeft;
                _clientPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                _clientPlayersManager = null;
            }
        }

        private Coroutine _clientCoroutine;

        /// <summary>
        /// Starts the client.
        /// This will start the transport client.
        /// </summary>
        public void StartClient()
        {
            clientToServerConn = null;
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
            _transport.StartClient(this);
        }

        private void OnNewConnection(Connection conn, bool asServer)
        {
            if (asServer)
                _serverModules.OnNewConnection(conn, true);
            else
            {
                clientToServerConn = conn;
                _clientModules.OnNewConnection(conn, false);
            }
        }

        private void OnLostConnection(Connection conn, DisconnectReason reason, bool asServer)
        {
            if (asServer)
                _serverModules.OnLostConnection(conn, true);
            else
            {
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
                _serverModules.OnDataReceived(conn, data, true);
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
                isClient = state == ConnectionState.Connected;
                _clientModules.OnConnectionState(state, false);
                onClientConnectionState?.Invoke(state);
                onAnyClientConnectionState?.Invoke(state);
            }

            if (state == ConnectionState.Disconnected)
            {
                switch (asServer)
                {
                    case false:
                        _isCleaningClient = true;
                        break;
                    case true:
                        _isCleaningServer = true;
                        break;
                }
            }
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
        }

        /// <summary>
        /// Stops the client.
        /// This will stop the transport client.
        /// </summary>
        public void StopClient()
        {
            if (_clientCoroutine != null)
            {
                StopCoroutine(_clientCoroutine);
                _clientCoroutine = null;
            }

            _transport.StopClient(this);
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
            if (isServer && _transport)
                _transport.transport.CloseConnection(conn);
        }
    }
}

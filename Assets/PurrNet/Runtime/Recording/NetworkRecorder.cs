using System;
using System.Collections.Generic;
using System.Reflection;
using PurrNet.Modules;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// Records a match to a .purrrec file (see recording/FORMAT.md) on the server or host: every spawned network
    /// identity's transform, colliders and recorded sync types (SyncVar, SyncList, SyncTimer) per tick, and the players.
    /// Add one next to the <see cref="NetworkManager"/>. Other packages add their data through
    /// <see cref="RecorderExtension"/>s (with PurrDiction: every predicted component's state and input), so a match is
    /// one file whatever it is made of. On clients it forwards their camera and anything written through
    /// <see cref="WriteSample{T}"/>. State that only exists on clients is not recorded.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("PurrNet/Recording/Network Recorder")]
    public sealed class NetworkRecorder : MonoBehaviour, IRecordingContext
    {
        [SerializeField] private NetworkManager _networkManager;
        [Tooltip("Start recording as soon as this machine is the server. Never on CI machines (the CI environment variable and the like).")]
        [SerializeField] private bool _recordOnServer = true;
        [Tooltip("Also record play sessions in the editor (kept on disk, never uploaded). Off: editor sessions are development, not gameplay.")]
        [SerializeField] private bool _recordInEditor;
        [Tooltip("Record the camera each player renders with.")]
        [SerializeField] private bool _recordCamera = true;
        [Tooltip("Folder for recordings. Relative paths are under Application.persistentDataPath.")]
        [SerializeField] private string _directory = "Recordings";
        [SerializeField] private float _chunkSeconds = 10f;
        [Tooltip("How often clients send their samples to the server.")]
        [SerializeField] private float _clientSendInterval = 0.1f;
        [Tooltip("Send finished recordings to the PurrNet recordings service, then delete them. Needs a project key: " +
                 "the one below, or the project linked through PurrServices.")]
        [SerializeField] private bool _upload = true;
        [SerializeField] private string _uploadEndpoint = "https://purrnet.dev/api/services/recordings";
        [Tooltip("Project public key. Empty uses the project linked through PurrServices.")]
        [SerializeField] private string _projectKey = "";
        [Tooltip("Recordings that could not be sent yet are kept up to this size; the oldest are deleted first.")]
        [SerializeField] private int _maxLocalMegabytes = 1024;
        [Tooltip("Most time per tick, in milliseconds, spent looking for collider changes (colliders added, removed, " +
                 "resized or moved on entities and in the scene). Rows are always recorded in full; in very large " +
                 "scenes a change is only noticed a little later.")]
        [SerializeField] private float _colliderCheckBudgetMs = 0.1f;
        [Tooltip("Memory reserved when recording starts for one chunk's rows, in KiB (two such buffers, plus the " +
                 "writer's working memory of about the same size). A chunk ends early when its buffer is full, so " +
                 "recording never allocates during play; bigger buffers make slightly smaller files for big games.")]
        [SerializeField] private int _chunkBufferKilobytes = 2048;

        static readonly List<Type> ExtensionTypes = new List<Type>();
        static readonly List<Func<RecorderExtension>> ExtensionFactories = new List<Func<RecorderExtension>>();

        /// <summary>Makes every recorder created from now on carry a <typeparamref name="T"/>. Call it once, at load.</summary>
        public static void RegisterExtension<T>() where T : RecorderExtension, new()
        {
            if (ExtensionTypes.Contains(typeof(T)))
                return;
            ExtensionTypes.Add(typeof(T));
            ExtensionFactories.Add(() => new T());
        }

        public static NetworkRecorder instance { get; private set; }

        /// <summary>Free-form labels written into the header (map, mode, build hash...). Set before recording starts.</summary>
        public readonly Dictionary<string, string> labels = new Dictionary<string, string>();

        public RecordingSession session => _session;
        public NetworkManager networkManager => _networkManager;
        public bool isRecording => _session != null && _session.isRecording;
        public string currentPath => _session?.currentPath;
        public long bytesWritten => _session?.bytesWritten ?? 0;

        public event Action<string> onRecordingFinished;

        /// <summary>What the recorder keeps about one object.</summary>
        sealed class EntityData
        {
            public RecordedEntity entity;
            public readonly List<Component> components = new List<Component>(2);
            // Kept between collider checks so Unity's C# objects for them are not made again (see RecordedEntity).
            public readonly List<NetworkIdentity> found = new List<NetworkIdentity>(2);
        }

        /// <summary>One network identity (component) of an object and its recorded modules.</summary>
        sealed class Component
        {
            public NetworkIdentity identity;
            public EntityData data;
            public int index;
            public readonly List<NetworkModule> modules = new List<NetworkModule>(4);
            public readonly List<StreamRows> rows = new List<StreamRows>(4);
        }

        sealed class IdentityComparer : IEqualityComparer<NetworkIdentity>
        {
            public static readonly IdentityComparer instance = new IdentityComparer();
            public bool Equals(NetworkIdentity a, NetworkIdentity b) => ReferenceEquals(a, b);
            public int GetHashCode(NetworkIdentity identity) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(identity);
        }

        /// <summary>What <see cref="DontRecordAttribute"/> says about an identity class.</summary>
        sealed class TypeRules
        {
            public bool excluded;
            public HashSet<string> excludedFields;
        }

        static readonly Dictionary<Type, TypeRules> Rules = new Dictionary<Type, TypeRules>();

        static TypeRules RulesOf(Type type)
        {
            if (Rules.TryGetValue(type, out var rules))
                return rules;
            rules = new TypeRules { excluded = Attribute.IsDefined(type, typeof(DontRecordAttribute), true) };
            for (var t = type; t != null && t != typeof(NetworkIdentity); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.IsDefined(typeof(DontRecordAttribute), false))
                        (rules.excludedFields ??= new HashSet<string>()).Add(field.Name);
            }
            return Rules[type] = rules;
        }

        static bool Excluded(NetworkIdentity identity) => RulesOf(identity.GetType()).excluded;

        sealed class Source : RecordingSource
        {
            readonly NetworkRecorder _owner;
            readonly List<NetworkIdentity> _scratch = new List<NetworkIdentity>();

            public Source(NetworkRecorder owner) => _owner = owner;

            public override string name => "purrnet";

            /// <summary>Whether <paramref name="go"/> has a network identity that is recorded.</summary>
            bool Records(GameObject go)
            {
                if (!go.TryGetComponent<NetworkIdentity>(out var first))
                    return false;
                if (!Excluded(first))
                    return true;
                go.GetComponents(_scratch);
                bool any = false;
                foreach (var identity in _scratch)
                    any |= !Excluded(identity);
                _scratch.Clear();
                return any;
            }

            public override bool IsEntityObject(GameObject go) => Records(go);

            public override Transform NearestEntity(Transform t, bool includeInactive)
            {
                var identity = t.GetComponentInParent<NetworkIdentity>(includeInactive);
                while (identity)
                {
                    var found = identity.transform;
                    if (Records(identity.gameObject))
                        return found;
                    var parent = found.parent;
                    identity = parent ? parent.GetComponentInParent<NetworkIdentity>(includeInactive) : null;
                }
                return null;
            }

            public override bool HasNestedEntities(RecordedEntity entity)
            {
                var list = entity.DataOf(this) is EntityData data ? data.found : _scratch;
                var root = entity.gameObject;
                root.GetComponentsInChildren(true, list);
                bool nested = false;
                foreach (var identity in list)
                {
                    if (!ReferenceEquals(identity.gameObject, root) && !Excluded(identity))
                    {
                        nested = true;
                        break;
                    }
                }
                _scratch.Clear();
                return nested;
            }

            public override void OnEntityReleased(RecordedEntity entity, object data) => _owner.ReleaseData((EntityData)data);

#if UNITY_PHYSICS_3D
            public override bool DrivesBody(Rigidbody body) => Records(body.gameObject);
#endif
        }

        RecordingSession _session;
        Source _source;
        readonly List<RecorderExtension> _extensions = new List<RecorderExtension>();
        // The clock in use, and the one the current file started with (null: network ticks).
        IRecordingContext _clock, _clockAtStart;
        HierarchyFactory _hierarchy;
        TickManager _tickManager;
        // By reference: destroyed Unity objects all compare equal to each other.
        readonly Dictionary<NetworkIdentity, Component> _components = new Dictionary<NetworkIdentity, Component>(IdentityComparer.instance);
        readonly List<NetworkIdentity> _identityList = new List<NetworkIdentity>();
        readonly Stack<EntityData> _dataPool = new Stack<EntityData>(256);
        readonly Stack<Component> _componentPool = new Stack<Component>(256);
        readonly HashSet<ulong> _players = new HashSet<ulong>();
        readonly List<ulong> _playersLeft = new List<ulong>();
        readonly Dictionary<int, string> _prefabNames = new Dictionary<int, string>();
        RecorderStats.Section _statPlayers, _statIdentities;
        bool _stoppedThisSession;
        bool _extensionPlayers;

        bool networkReady => _networkManager && _networkManager.tickModule != null;

        bool IRecordingContext.isReady => _clock != null ? _clock.isReady : networkReady;
        ulong IRecordingContext.localTick => _clock != null ? _clock.localTick : _networkManager.tickModule.localTick;
        double IRecordingContext.viewTick => _clock != null ? _clock.viewTick : _networkManager.tickModule.syncedPreciseTick;
        bool IRecordingContext.isServer => _clock != null ? _clock.isServer : _networkManager.isServer;
        bool IRecordingContext.isClient => _clock != null ? _clock.isClient : _networkManager.isClient;
        int IRecordingContext.tickRate => _clock != null ? _clock.tickRate : _networkManager.tickModule.tickRate;
        PlayerID? IRecordingContext.localPlayer => _clock != null
            ? _clock.localPlayer
            : _networkManager.isClient ? _networkManager.localPlayer : (PlayerID?)null;

        void Awake()
        {
            instance = this;
            if (!_networkManager)
                _networkManager = GetComponent<NetworkManager>();
            _source = new Source(this);
            _session = new RecordingSession(this, nameof(NetworkRecorder), labels, "players", "identities");
            _session.onRecordingFinished += path => onRecordingFinished?.Invoke(path);
            _session.AddSource(_source);
            _statPlayers = _session.Section("players");
            _statIdentities = _session.Section("identities");

            foreach (var factory in ExtensionFactories)
            {
                var extension = factory();
                extension.recorder = this;
                _extensions.Add(extension);
                _session.AddSource(extension.source);
                extension.OnCreated();
            }

            if (_recordCamera && !GetComponent<RecordLocalCamera>())
                gameObject.AddComponent<RecordLocalCamera>();
            ConfigureUploads();
        }

        void OnDestroy()
        {
            StopFile();
            foreach (var extension in _extensions)
                extension.OnDestroyed();
            _extensions.Clear();
            _session?.Dispose();
            if (instance == this)
                instance = null;
        }

        void OnApplicationQuit() => StopFile();

        public T GetExtension<T>() where T : RecorderExtension
        {
            foreach (var extension in _extensions)
                if (extension is T found)
                    return found;
            return null;
        }

        /// <summary>The clock an extension brings, if any has one right now.</summary>
        IRecordingContext CurrentClock()
        {
            foreach (var extension in _extensions)
            {
                var clock = extension.clock;
                if (clock != null)
                    return clock;
            }
            return null;
        }

        bool ExtensionsReady()
        {
            foreach (var extension in _extensions)
                if (!extension.isReady)
                    return false;
            return true;
        }

        void Update()
        {
            if (!_networkManager)
                _networkManager = NetworkManager.main;

            foreach (var extension in _extensions)
                extension.OnUpdate();
            _clock = CurrentClock();

            IRecordingContext context = this;
            if (!context.isReady)
                return;

            // A file has one clock from start to end.
            if (isRecording && !ReferenceEquals(_clock, _clockAtStart))
                StopFile();

            if (_networkManager && !_session.subscribedNetwork)
                _session.SubscribeNetwork(_networkManager);

            bool server = context.isServer;
            if (!server)
                _stoppedThisSession = false;

            if (_recordOnServer && RecordingSession.RecordsAutomatically(_recordInEditor) && server && !isRecording &&
                !_stoppedThisSession && ExtensionsReady())
                StartRecording();
            else if (isRecording && !server)
                StopFile();

            _session.clientSendInterval = _clientSendInterval;
            _session.Update(server);
        }

        /// <summary>Folder for the next recording. Relative paths are under Application.persistentDataPath.</summary>
        public void SetDirectory(string directory)
        {
            _directory = directory;
            ConfigureUploads();
        }

        /// <summary>
        /// Where finished recordings are sent and with which project key (empty uses the project linked through
        /// PurrServices). An empty endpoint turns uploads off.
        /// </summary>
        public void SetUpload(string endpoint, string projectKey)
        {
            _upload = !string.IsNullOrEmpty(endpoint);
            _uploadEndpoint = endpoint;
            _projectKey = projectKey;
            ConfigureUploads();
        }

        void ConfigureUploads() =>
            RecordingSession.ConfigureUploads(_upload, _uploadEndpoint, _projectKey, _directory, _maxLocalMegabytes);

        /// <summary>Starts a new recording file. Called automatically on the server when <c>recordOnServer</c> is set.</summary>
        public void StartRecording(string fileName = null)
        {
            if (isRecording)
                return;
            _clock = CurrentClock();
            IRecordingContext context = this;
            if (!context.isReady || !context.isServer)
                return;

            _clockAtStart = _clock;
            _extensionPlayers = false;
            foreach (var extension in _extensions)
                _extensionPlayers |= extension.recordsPlayers;
            _hierarchy = null;
            _session.chunkSeconds = _chunkSeconds;
            _session.clientSendInterval = _clientSendInterval;
            _session.maxLocalMegabytes = _maxLocalMegabytes;
            _session.colliderCheckBudgetMs = _colliderCheckBudgetMs;
            _session.chunkBufferKilobytes = _chunkBufferKilobytes;
            _session.Start(_directory, fileName, context.tickRate, context.localTick, gameObject.scene.name, context.isClient);
            foreach (var extension in _extensions)
                extension.OnRecordingStarted();

            // On network ticks the recorder ticks itself; an extension's clock calls RecordTick.
            if (_clockAtStart == null)
            {
                _tickManager = _networkManager.tickModule;
                _tickManager.onPostTick += OnPostTick;
            }
        }

        /// <summary>Flushes and closes the current recording; the next one starts when this machine is the server again.</summary>
        public void StopRecording()
        {
            if (!isRecording)
                return;
            _stoppedThisSession = true;
            StopFile();
        }

        void StopFile()
        {
            if (!isRecording)
                return;

            if (_tickManager != null)
                _tickManager.onPostTick -= OnPostTick;
            _tickManager = null;
            foreach (var extension in _extensions)
                extension.OnRecordingStopping();
            _session.Stop(json =>
            {
                foreach (var extension in _extensions)
                    extension.WriteFooter(json);
            });
            _players.Clear();
            _prefabNames.Clear();
        }

        // ---- Samples --------------------------------------------------------------------------------

        /// <summary>
        /// Adds a sample to a custom stream. On the server it is written directly; on a client it is sent to the
        /// server. Samples sent from clients must be unmanaged (no classes, strings, arrays or lists) and at most
        /// 512 bytes with their envelope.
        /// </summary>
        public void WriteSample<T>(string stream, in T sample) where T : struct => _session.WriteSample(stream, sample);

        /// <summary>
        /// Server only. Adds a sample to a custom stream on behalf of <paramref name="player"/>, for players that have
        /// no client of their own (bots) or data the server observes about a player.
        /// </summary>
        public void WriteSampleFor<T>(string stream, PlayerID player, in T sample) where T : struct =>
            _session.WriteSampleFor(stream, player, sample);

        /// <summary>The last finished recording's size and the recorder's cost per section, as text.</summary>
        public string DescribeLastRecording() => _session.DescribeLastRecording();

        // ---- Ticks ------------------------------------------------------------------------------------

        void OnPostTick() => RecordTick(_tickManager.localTick);

        /// <summary>
        /// Records one tick: players, the extensions' rows, network identities and colliders. Called by the recorder on
        /// network ticks, or by the extension whose clock the recording runs on.
        /// </summary>
        public void RecordTick(ulong tick)
        {
            if (!isRecording)
                return;

            _session.BeginTick(tick);
            var stats = _session.stats;
            if (!_extensionPlayers)
            {
                stats.Begin(_statPlayers);
                RecordPlayers(tick);
                stats.End(_statPlayers);
            }
            _session.RecordSources(tick, _source);
            stats.Begin(_statIdentities);
            RecordIdentities(tick);
            stats.End(_statIdentities);
            _session.RecordColliders(tick);
            _session.EndTick();
        }

        void RecordPlayers(ulong tick)
        {
            if (!_networkManager)
                return;
            var players = _networkManager.players;
            for (int i = 0; i < players.Count; i++)
            {
                var id = players[i];
                if (!_players.Add(id.id.value))
                    continue;
                _session.WriteMeta(RecordKind.Player, _session.json.Clear().BeginObject()
                    .Prop("player", id.id.value).Prop("tick", tick).Prop("event", "joined")
                    .Prop("isBot", id.isBot).Prop("isLocal", _networkManager.isClient && _networkManager.localPlayer == id)
                    .EndObject());
            }

            if (_players.Count == players.Count)
                return;
            _playersLeft.Clear();
            foreach (var id in _players)
            {
                bool connected = false;
                for (int i = 0; i < players.Count && !connected; i++)
                    connected = players[i].id.value == id;
                if (!connected)
                    _playersLeft.Add(id);
            }
            foreach (var id in _playersLeft)
            {
                _players.Remove(id);
                _session.WriteMeta(RecordKind.Player, _session.json.Clear().BeginObject()
                    .Prop("player", id).Prop("tick", tick).Prop("event", "left").EndObject());
            }
        }

        void RecordIdentities(ulong tick)
        {
            if (!_networkManager || !_networkManager.isServer)
                return;
            if (_hierarchy == null && !_networkManager.TryGetModule(true, out _hierarchy))
                return;

            var hierarchies = _hierarchy.hierarchies;
            for (int h = 0; h < hierarchies.Count; h++)
            {
                var spawned = hierarchies[h].spawnedIdentities;
                for (int i = 0; i < spawned.Count; i++)
                {
                    var identity = spawned[i];
                    if (identity is null || !identity || !identity.isSpawned)
                        continue;

                    if (!_components.TryGetValue(identity, out var component))
                    {
                        if (Excluded(identity))
                            continue;
                        component = Track(identity, tick);
                        if (component == null)
                            continue;
                    }

                    _session.RecordTransform(component.data.entity, _source, tick, out _);
                    var modules = component.modules;
                    var rows = component.rows;
                    for (int m = 0; m < modules.Count; m++)
                        modules[m].Record(_session, rows[m], tick);
                }
            }

            _session.DespawnUnseen(_source, tick);
        }

        /// <summary>Starts recording an identity: a new entity, our part of another source's entity, or one more component.</summary>
        Component Track(NetworkIdentity identity, ulong tick)
        {
            var go = identity.gameObject;
            if (_session.TryGetEntity(go, out var entity))
            {
                if (entity.DataOf(_source) is EntityData ours)
                {
                    // Spawned after the others on its object.
                    var added = Declare(ours, identity, ours.components.Count);
                    var json = _session.BeginAttachRecord(entity, _source, tick);
                    WriteComponent(json.Key("components").BeginArray(), added);
                    json.EndArray();
                    _session.EndAttachRecord(json);
                    return added;
                }

                var data = TakeData(entity);
                _session.Attach(entity, _source, data);
                DeclareAll(data, go);
                var attach = _session.BeginAttachRecord(entity, _source, tick);
                WriteComponents(attach, data);
                _session.EndAttachRecord(attach);
                return _components.TryGetValue(identity, out var c) ? c : null;
            }

            var created = TakeData(null);
            entity = _session.BeginSpawn(_source, go, created);
            created.entity = entity;
            DeclareAll(created, go);
            _session.CreateTransformStream(entity);

            int prefabId = identity.isSceneObject ? -1 : identity.prefabId;
            var parent = _session.ParentEntity(go.transform.parent, true);
            entity.spawnTick = tick;
            entity.owner = identity.owner;
            entity.isRoot = parent == null;

            _session.FlushPendingTypes();
            var record = _session.json.Clear().BeginObject()
                .Prop("entity", entity.id).Prop("tick", tick)
                .Prop("networkId", identity.id.HasValue ? (ulong)identity.id.Value.id.value : 0)
                .Prop("name", _session.Name(go)).Prop("prefabId", prefabId).Prop("prefabName", PrefabName(prefabId))
                .Key("owner");
            if (entity.owner.HasValue) record.Value(entity.owner.Value.id.value); else record.Null();
            record.Key("parent");
            if (parent != null) record.Value(parent.id); else record.Null();
            _session.WritePath(record.Key("path"), go.transform);
            WriteComponents(record, created);
            record.Prop("transform", entity.transform.id).EndObject();
            _session.WriteMeta(RecordKind.Entity, record);
            _session.EndSpawn(entity, tick);

            return _components.TryGetValue(identity, out var tracked) ? tracked : null;
        }

        EntityData TakeData(RecordedEntity entity)
        {
            var data = _dataPool.Count > 0 ? _dataPool.Pop() : new EntityData();
            data.entity = entity;
            return data;
        }

        /// <summary>Declares the streams of every spawned, recorded network identity on the object.</summary>
        void DeclareAll(EntityData data, GameObject go)
        {
            go.GetComponents(_identityList);
            foreach (var identity in _identityList)
                if (identity.isSpawned && !Excluded(identity) && !_components.ContainsKey(identity))
                    Declare(data, identity, data.components.Count);
            _identityList.Clear();
        }

        Component Declare(EntityData data, NetworkIdentity identity, int index)
        {
            var component = _componentPool.Count > 0 ? _componentPool.Pop() : new Component();
            component.identity = identity;
            component.data = data;
            component.index = index;
            var excludedFields = RulesOf(identity.GetType()).excludedFields;
            var modules = identity.modules;
            for (int m = 0; m < modules.Count; m++)
            {
                var module = modules[m];
                var type = module?.recordedType;
                if (type == null || (excludedFields != null && excludedFields.Contains(module.name)))
                    continue;
                component.modules.Add(module);
                component.rows.Add(_session.CreateStream(type, new RecordedStreamInfo
                {
                    kind = RecordedStreamKind.Sync, entity = data.entity.id, component = (ulong)index,
                    componentType = identity.GetType(), module = module.index, field = module.name
                }, true));
            }
            data.components.Add(component);
            _components[identity] = component;
            return component;
        }

        void WriteComponents(JsonBuilder json, EntityData data)
        {
            json.Key("components").BeginArray();
            foreach (var c in data.components)
                WriteComponent(json, c);
            json.EndArray();
        }

        void WriteComponent(JsonBuilder json, Component c)
        {
            json.BeginObject().Prop("index", c.index).Prop("type", _session.TypeName(c.identity.GetType()))
                .Prop("networkId", c.identity.id.HasValue ? (ulong)c.identity.id.Value.id.value : 0)
                .Key("syncs").BeginArray();
            for (int m = 0; m < c.modules.Count; m++)
                json.BeginObject().Prop("module", c.modules[m].index).Prop("field", c.modules[m].name)
                    .Prop("stream", c.rows[m].id).EndObject();
            json.EndArray().EndObject();
        }

        /// <summary>Retires the components' streams (dropped after their last flush) and pools them.</summary>
        void ReleaseData(EntityData data)
        {
            if (data == null)
                return;
            foreach (var c in data.components)
            {
                foreach (var rows in c.rows)
                    rows.Retire();
                if (c.identity is not null)
                    _components.Remove(c.identity);
                c.identity = null;
                c.data = null;
                c.modules.Clear();
                c.rows.Clear();
                _componentPool.Push(c);
            }
            data.components.Clear();
            data.found.Clear();
            data.entity = null;
            _dataPool.Push(data);
        }

        string PrefabName(int prefabId)
        {
            if (prefabId < 0)
                return null;
            if (_prefabNames.TryGetValue(prefabId, out var name))
                return name;
            var provider = _networkManager.prefabProvider;
            name = provider != null && provider.TryGetPrefabData(prefabId, out var data) && data.prefab ? data.prefab.name : null;
            return _prefabNames[prefabId] = name;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using PurrNet.Transports;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PurrNet.Recording
{
    public sealed partial class RecordingSession
    {
        readonly struct CustomKey : IEquatable<CustomKey>
        {
            readonly string _stream;
            readonly ulong _player;
            readonly bool _hasPlayer;

            public CustomKey(string stream, PlayerID? player)
            {
                _stream = stream;
                _hasPlayer = player.HasValue;
                _player = player.HasValue ? player.Value.id.value : 0;
            }

            public bool Equals(CustomKey other) =>
                _player == other._player && _hasPlayer == other._hasPlayer && string.Equals(_stream, other._stream);

            public override bool Equals(object obj) => obj is CustomKey other && Equals(other);
            public override int GetHashCode() => (_stream?.GetHashCode() ?? 0) * 31 + _player.GetHashCode() + (_hasPlayer ? 1 : 0);
        }

        sealed class ChunkJob : IStepwiseRecordProducer
        {
            public readonly RecordingSession owner;
            public readonly ChunkLog log;
            bool _stepping;

            public ChunkJob(RecordingSession owner)
            {
                this.owner = owner;
                log = new ChunkLog(Shelf);
            }

            public void Begin()
            {
                owner._encoder.Begin(log);
                _stepping = true;
            }

            public bool Step(long deadline) => owner._encoder.Step(deadline);

            public RecordingBuffer Produce()
            {
                if (!_stepping)
                    return owner._encoder.Encode(log);
                _stepping = false;
                owner._encoder.Step(long.MaxValue);
                return owner._encoder.result;
            }

            public void Release()
            {
                _stepping = false;
                log.Clear();
                lock (owner._jobPool)
                    owner._jobPool.Push(this);
            }
        }

        sealed class MetaBatch : IRecordProducer
        {
            public RecordingSession owner;
            public RecordingBuffer buffer = new RecordingBuffer(Shelf, 4096);

            public RecordingBuffer Produce() => buffer;

            public void Release()
            {
                buffer.Clear();
                lock (owner._metaPool)
                    owner._metaPool.Push(this);
            }
        }

        public static RecordingSession active { get; private set; }

        readonly IRecordingContext _context;
        readonly string _logName;

        public readonly Dictionary<string, string> labels;

        public float chunkSeconds = 10f;
        public int maxLocalMegabytes = 1024;
        public float colliderCheckBudgetMs = 0.1f;
        public int chunkBufferKilobytes = 2048;
        public ulong localTick => _context.isReady ? _context.localTick : 0;

        public bool isRecording => _file != null;
        public bool chunkStart { get; private set; }
        public int sampleEpoch => !_context.isReady ? 0
            : _context.isServer ? (isRecording && !isPart && _context.localPlayer.HasValue ? _sampleEpoch : 0)
            : _partOffered ? _sampleEpoch : 0;
        int _sampleEpoch;
        public string currentPath => _file?.path;
        public long bytesWritten => _file?.bytesWritten ?? 0;
        public ulong ticksRecorded => _ticksRecorded;
        public RecorderStats stats => _stats;

        public event Action<string> onRecordingFinished;

        RecordingTypeRegistry _types = new RecordingTypeRegistry();
        readonly List<StreamRows> _streams = new List<StreamRows>();
        readonly Dictionary<long, RecordedEntity> _entitiesByGameObject = new Dictionary<long, RecordedEntity>();
        readonly List<RecordedEntity> _entities = new List<RecordedEntity>();
        readonly List<RecordingSource> _sources = new List<RecordingSource>(2);
        readonly Dictionary<CustomKey, StreamRows> _customStreams = new Dictionary<CustomKey, StreamRows>();
        readonly HashSet<object> _visiting = new HashSet<object>(ReferenceComparer.instance);
        readonly List<string> _pendingTypes = new List<string>();
        readonly RecordingBuffer _scratch = new RecordingBuffer(256);
        readonly RecordingBuffer _check = new RecordingBuffer(256);
        readonly JsonBuilder _json = new JsonBuilder();

        readonly Stack<RecordedEntity> _entityPool = new Stack<RecordedEntity>(256);
        readonly Stack<StreamRows> _streamPool = new Stack<StreamRows>(1024);
        readonly Stack<ChunkJob> _jobPool = new Stack<ChunkJob>();
        static readonly ByteShelf Shelf = ByteShelf.shared;
        readonly Stack<MetaBatch> _metaPool = new Stack<MetaBatch>();
        readonly ChunkEncoder _encoder = new ChunkEncoder();
        ChunkJob _job;
        readonly List<StreamRows> _slots = new List<StreamRows>();

        const int MaxCachedNames = 4096;
        readonly Dictionary<long, string> _names = new Dictionary<long, string>(MaxCachedNames);
        readonly Dictionary<Type, string> _typeNames = new Dictionary<Type, string>();
        readonly Dictionary<(RowLayout, (string, string)[]), int[]> _baseColumns = new Dictionary<(RowLayout, (string, string)[]), int[]>();

        NetworkManager _networkManager;

        readonly RecorderStats _stats;
        readonly RecorderStats.Section _statTick, _statSpawn, _statManaged, _statColliders, _statChecks, _statFlush;

        RecordingFile _file;
        RecordingBuffer _meta = new RecordingBuffer(Shelf, 4096);
        readonly float _startTime;
        int _nextEntityId = 1;
        int _nextStreamId = 1;
        ulong _chunkFirstTick;
        ulong _lastTick;
        ulong _ticksRecorded;
        int _lastEntities;
        int _chunkTicks;
        int _chunks;
        int _peakLogBytes;
        int _logCapacity;
        int _maxTickBytes;
        int _logStart;

        public RecordingSession(IRecordingContext context, string logName, Dictionary<string, string> labels = null,
            params string[] recorderSections)
        {
            _context = context;
            _logName = logName;
            this.labels = labels ?? new Dictionary<string, string>();
            _startTime = Time.realtimeSinceStartup;
            active = this;

            _stats = new RecorderStats(logName);
            _statTick = _stats.Add("tick");
            foreach (var name in recorderSections)
                _stats.Add(name);
            _statSpawn = _stats.Add(" spawn");
            _statManaged = _stats.Add(" managed");
            _statColliders = _stats.Add("colliders");
            _statChecks = _stats.Add(" checks");
            _statFlush = _stats.Add("flush");
        }

        public RecorderStats.Section Section(string name) => _stats.Find(name);

        public bool isDisposed { get; private set; }

        public void Dispose()
        {
            isDisposed = true;
            Stop(null);
            UnsubscribeNetwork();
            if (active == this)
                active = null;
        }

        public void AddSource(RecordingSource source)
        {
            if (!_sources.Contains(source))
                _sources.Add(source);
        }

        public void SubscribeNetwork(NetworkManager manager)
        {
            if (_networkManager == manager)
                return;
            UnsubscribeNetwork();
            _networkManager = manager;
            if (!_networkManager)
                return;
            SubscribeParts();
        }

        public void UnsubscribeNetwork()
        {
            if (_networkManager)
                UnsubscribeParts();
            _networkManager = null;
        }

        public bool subscribedNetwork => _networkManager;

        public void Update(bool isServer)
        {
            UpdatePart(isServer);

            _file?.Pump();
        }

        public static bool RecordsAutomatically(bool recordInDevelopment) =>
            !RecordingUploader.runningOnCI && (!Debug.isDebugBuild || recordInDevelopment);

        public static string ResolveDirectory(string directory) => Path.IsPathRooted(directory)
            ? directory
            : Path.Combine(Application.persistentDataPath, directory);

        public static void ConfigureUploads(bool upload, string endpoint, string projectKey, string directory, int maxLocalMegabytes)
        {
            string key = string.IsNullOrWhiteSpace(projectKey) ? PurrServicesProjectLink.projectKey : projectKey.Trim();
            if (upload && !Application.isEditor && !RecordingUploader.runningOnCI && !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(endpoint))
                RecordingUploader.Configure(endpoint, key, ResolveDirectory(directory), (long)maxLocalMegabytes * 1024 * 1024);
        }

        public void Start(string directory, string fileName, int tickRate, ulong firstTick, string scene, bool host)
        {
            if (isRecording)
                return;

            directory = ResolveDirectory(directory);
            fileName ??= $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Application.productName}.purrrec";
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            _types = new RecordingTypeRegistry { onError = AddError };
            _baseColumns.Clear();
            _file = new RecordingFile(Path.Combine(directory, fileName), (long)maxLocalMegabytes * 1024 * 1024);
            RecordingUploader.SetActiveRecording(_file.path);
            _chunkTicks = Mathf.Max(1, Mathf.RoundToInt(chunkSeconds * tickRate));
            _chunkFirstTick = firstTick;
            _lastTick = 0;
            _ticksRecorded = 0;
            _chunks = 0;
            _peakLogBytes = 0;
            _maxTickBytes = 0;
            _logCapacity = Mathf.NextPowerOfTwo(Mathf.Max(64, chunkBufferKilobytes) * 1024);
            ReserveChunkMemory();
            _stats.Reset();
            _stats.file = _file;
            _slots.Clear();
            chunkStart = true;
            _sampleEpoch++;

            BeginParts(firstTick);
            WriteHeader(tickRate, firstTick, scene, host);
#if UNITY_PHYSICS_3D
            _hasStaticColliderKey = false;
            ResetStaticCheck();
            RefreshStaticColliders(firstTick);
#endif
        }

        public void Stop(Action<JsonBuilder> footer)
        {
            if (_file == null)
                return;

            EndParts();
            FlushChunk();
            foreach (var entity in _entities)
                WriteMeta(RecordKind.Despawn, _json.Clear().BeginObject()
                    .Prop("entity", entity.id).Prop("tick", _lastTick + 1).EndObject());

            FlushPendingTypes();
            FlushMeta();
            _json.Clear().BeginObject()
                .Prop("lastTick", _lastTick).Prop("ticks", _ticksRecorded).Prop("entities", _nextEntityId - 1)
                .Prop("streams", _nextStreamId - 1).Prop("chunks", _chunks);
            footer?.Invoke(_json);
            if (_errors.Count > 0)
            {
                _json.Key("errors").BeginArray();
                foreach (var (kind, detail) in _errors)
                    _json.BeginObject().Prop("kind", kind).Prop("detail", detail).EndObject();
                _json.EndArray();
                _errors.Clear();
            }
            EnqueueJson(RecordKind.Footer, _json.EndObject(), true);

            var file = _file;
            _file = null;
            file.Dispose();
            _lastEntities = _nextEntityId - 1;
            _stats.file = null;
            _lastFile = file;

            foreach (var entity in _entities)
                ReleaseEntity(entity);
#if UNITY_PHYSICS_3D
            ReleaseColliders(_staticColliders);
            ResetStaticCheck();
            _staticMovingColliders = 0;
            _meshIds.Clear();
            _nextMeshId = 1;
#endif
            _streams.Clear();
            _entities.Clear();
            _entitiesByGameObject.Clear();
            _customStreams.Clear();
            _names.Clear();
            _nextEntityId = 1;
            _nextStreamId = 1;
            RecordingUploader.SetActiveRecording(null);
            RecordingUploader.NotifyFinished();
            onRecordingFinished?.Invoke(file.path);
        }

        readonly List<(string kind, string detail)> _errors = new List<(string, string)>();

        public void AddError(string kind, string detail)
        {
            if (_file != null && !_errors.Contains((kind, detail)))
                _errors.Add((kind, detail));
        }

        RecordingFile _lastFile;

        public string DescribeLastRecording()
        {
            var file = _lastFile;
            if (file == null)
                return "";
            return $"[{_logName}] Finished {file.path}: {_ticksRecorded} ticks, {_lastEntities} entities, " +
                   $"{file.bytesWritten / 1024f:0.0} KiB on disk ({file.rawBytes / 1024f:0.0} KiB before compression); " +
                   (RecordingFile.encodesOnMainThread
                       ? $"main thread {file.produceMs:0}ms encoding (longest frame {file.maxRecordMs:0.0}ms), worker {file.compressMs:0}ms compressing, "
                       : $"writer thread {file.produceMs:0}ms encoding, {file.compressMs:0}ms compressing, longest record {file.maxRecordMs:0.0}ms, ") +
                   $"largest chunk log {_peakLogBytes / 1048576f:0.0} MiB{(file.nativeDeflate ? "" : ", DeflateStream fallback")}\n" +
                   _stats.Summary(_ticksRecorded, _logName) + _types.DescribeReflection();
        }

        void WriteHeader(int tickRate, ulong firstTick, string scene, bool host)
        {
            var json = _json.Clear().BeginObject()
                .Prop("format", "purrrec").Prop("version", 1)
                .Prop("game", Application.productName).Prop("gameVersion", Application.version)
                .Prop("unityVersion", Application.unityVersion)
                .Prop("tickRate", tickRate)
                .Prop("firstTick", firstTick)
                .Prop("startedUtc", DateTime.UtcNow.ToString("o"))
                .Prop("recordingId", recordingId)
                .Prop("scene", scene)
                .Prop("recorder", host ? "host" : "server")
                .Prop("serverBuild", PurrNet.Utils.ApplicationContext.isServerBuild)
                .Key("labels").BeginObject();
            foreach (var pair in labels)
                json.Prop(pair.Key, pair.Value);
            json.EndObject().EndObject();
            EnqueueJson(RecordKind.Header, json, false);
        }

        void EnqueueJson(RecordKind kind, JsonBuilder json, bool compress)
        {
            MetaBatch batch;
            lock (_metaPool)
                batch = _metaPool.Count > 0 ? _metaPool.Pop() : null;
            batch ??= new MetaBatch { owner = this };
            batch.buffer.Clear();
            batch.buffer.WriteBytes(json.data, 0, json.length);
            _file.Enqueue(kind, batch, compress);
        }

        public void BeginTick(ulong tick)
        {
            _stats.Begin(_statTick);
            var log = _job.log.bytes;
            if (tick - _chunkFirstTick >= (ulong)_chunkTicks || (log.length > 0 && log.length + 2 * _maxTickBytes > _logCapacity))
            {
                _stats.Begin(_statFlush);
                FlushChunk();
                _stats.End(_statFlush);
                log = _job.log.bytes;
            }
            _logStart = log.length;
            if (_ticksRecorded == 0 && _slots.Count == 0)
                _chunkFirstTick = tick;

            _lastTick = tick;
            _ticksRecorded++;
        }

        public void EndTick()
        {
            _maxTickBytes = Math.Max(_maxTickBytes, _job.log.bytes.length - _logStart);
            if (_maxTickBytes * 2 > _logCapacity)
            {
                _logCapacity = Mathf.NextPowerOfTwo(_maxTickBytes * 4);
                ReserveChunkMemory();
            }
            chunkStart = false;
            _stats.End(_statTick);
            _stats.CommitTick();
        }

        void ReserveChunkMemory()
        {
            _job ??= TakeJob();
            _job.log.bytes.Reserve(_logCapacity);
            _meta.Reserve(_logCapacity / 8);
            lock (_jobPool)
            {
                if (_jobPool.Count == 0)
                    _jobPool.Push(new ChunkJob(this));
                foreach (var job in _jobPool)
                    job.log.bytes.Reserve(_logCapacity);
            }
            if (!_file.busy)
            {
                _encoder.Reserve(_logCapacity);
                _file.ReserveOutput(_logCapacity / 2);
            }
        }

        public int entityCount => _entities.Count;
        public RecordedEntity GetEntityAt(int index) => _entities[index];

        public bool TryGetEntity(GameObject go, out RecordedEntity entity) =>
            _entitiesByGameObject.TryGetValue(RecordingObjectId.Of(go), out entity);

        public RecordedEntity BeginSpawn(RecordingSource source, GameObject go, object data)
        {
            _stats.Begin(_statSpawn);
            var entity = _entityPool.Count > 0 ? _entityPool.Pop() : new RecordedEntity();
            entity.id = _nextEntityId++;
            entity.Add(source, data);
            entity.gameObject = go;
            entity.root = go.transform;
            entity.poseTick = 0;
#if UNITY_PHYSICS_3D
            entity.hasColliderKey = false;
#endif
            _entitiesByGameObject[RecordingObjectId.Of(go)] = entity;
            _entities.Add(entity);
            return entity;
        }

        public void Attach(RecordedEntity entity, RecordingSource source, object data)
        {
            if (!entity.Has(source))
                entity.Add(source, data);
        }

        public JsonBuilder BeginAttachRecord(RecordedEntity entity, RecordingSource source, ulong tick)
        {
            FlushPendingTypes();
            return _json.Clear().BeginObject().Prop("entity", entity.id).Prop("tick", tick).Prop("source", source.name);
        }

        public void EndAttachRecord(JsonBuilder json) => WriteMeta(RecordKind.Attach, json.EndObject());

        public void CreateTransformStream(RecordedEntity entity, StreamRows baseStream = null, (string, string)[] baseColumns = null)
        {
            entity.transform = CreateStream(typeof(RecordedTransform),
                new RecordedStreamInfo { kind = RecordedStreamKind.Transform, entity = entity.id }, true,
                baseColumns != null ? baseStream : null, baseColumns);
        }

        public void EndSpawn(RecordedEntity entity, ulong tick)
        {
#if UNITY_PHYSICS_3D
            RefreshEntityColliders(entity, tick);
#else
            RefreshEntityRenderers(entity);
#endif
            _stats.End(_statSpawn);
        }

        public unsafe bool RecordTransform(RecordedEntity entity, RecordingSource source, ulong tick, out Vector3 position)
        {
            entity.Touch(source, tick);
            if (entity.poseTick == tick)
            {
                position = default;
                return false;
            }
            entity.poseTick = tick;
            var pose = new RecordedTransform();
            var t = entity.root;
            t.GetPositionAndRotation(out pose.position, out pose.rotation);
            pose.scale = t.lossyScale;
            pose.visible = Visibility(entity);
            position = pose.position;
            var rows = entity.transform;
            if (rows.slot >= 0 && pose.visible == entity.lastPose.visible)
            {
                fixed (RecordedTransform* last = &entity.lastPose)
                    if (UnsafeUtility.MemCmp(&pose, last, 40) == 0)
                    {
                        rows.aliveTick = tick;
                        return true;
                    }
            }
            entity.lastPose = pose;
            AppendRow(rows, tick, ref pose);
            return true;
        }

        public void DespawnUnseen(RecordingSource source, ulong tick)
        {
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                var entity = _entities[i];
                if (entity.Unseen(source, tick))
                {
                    _stats.Begin(_statSpawn);
                    Release(entity, source, tick);
                    _stats.End(_statSpawn);
                }
            }
        }

        public void Release(RecordedEntity entity, RecordingSource source, ulong tick)
        {
            if (!entity.Has(source))
                return;
            if (entity.partCount == 1)
            {
                Despawn(entity, tick);
                return;
            }

            source.OnEntityReleased(entity, entity.Remove(source));
            WriteMeta(RecordKind.Detach, _json.Clear().BeginObject()
                .Prop("entity", entity.id).Prop("tick", tick).Prop("source", source.name).EndObject());
        }

        public void Despawn(RecordedEntity entity, ulong tick)
        {
            int index = _entities.IndexOf(entity);
            if (index >= 0)
            {
                _entities.RemoveAt(index);
                if (index < _entityCheckCursor)
                    _entityCheckCursor--;
            }
            if (_entitiesByGameObject.TryGetValue(entity.gameObject ? RecordingObjectId.Of(entity.gameObject) : 0, out var current) && current == entity)
                _entitiesByGameObject.Remove(RecordingObjectId.Of(entity.gameObject));
            else
            {
                long stale = 0;
                bool found = false;
                foreach (var pair in _entitiesByGameObject)
                    if (pair.Value == entity) { stale = pair.Key; found = true; break; }
                if (found)
                    _entitiesByGameObject.Remove(stale);
            }

            WriteMeta(RecordKind.Despawn, _json.Clear().BeginObject()
                .Prop("entity", entity.id).Prop("tick", tick).EndObject());
            ReleaseEntity(entity);
        }

        void ReleaseEntity(RecordedEntity entity)
        {
            entity.transform?.Retire();
            while (entity.source0 != null)
            {
                var source = entity.source0;
                source.OnEntityReleased(entity, entity.Remove(source));
            }
#if UNITY_PHYSICS_3D
            ReleaseColliders(entity.colliders);
            entity.movingColliders = 0;
            entity.foundColliders.Clear();
#endif
            entity.renderers.Clear();
            entity.rendererObjects.Clear();
            entity.foundRenderers.Clear();
            entity.foundGraphics.Clear();
            entity.detachedGraphics.Clear();
            entity.transform = null;
            entity.gameObject = null;
            entity.root = null;
            _entityPool.Push(entity);
        }

        public void RecordSources(ulong tick, RecordingSource driver)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i] != driver)
                    _sources[i].RecordTick(tick);
        }

        public void RemoveSource(RecordingSource source)
        {
            if (!_sources.Remove(source) || !isRecording)
                return;
            for (int i = _entities.Count - 1; i >= 0; i--)
                if (_entities[i].Has(source))
                    Release(_entities[i], source, _lastTick + 1);
        }

        public bool HasSource(RecordingSource source) => _sources.Contains(source);

        public Transform NearestEntity(Transform t, bool includeInactive)
        {
            Transform nearest = null;
            for (int i = 0; i < _sources.Count; i++)
            {
                var found = _sources[i].NearestEntity(t, includeInactive);
                if (found && (!nearest || found.IsChildOf(nearest)))
                    nearest = found;
            }
            return nearest;
        }

        public RecordedEntity ParentEntity(Transform parent, bool includeInactive)
        {
            if (!parent)
                return null;
            var nearest = NearestEntity(parent, includeInactive);
            return nearest && TryGetEntity(nearest.gameObject, out var entity) ? entity : null;
        }

        bool IsUnderEntity(UnityEngine.Component component) => NearestEntity(component.transform, true);

        bool OwnedBy(UnityEngine.Component component, GameObject root)
        {
            var nearest = NearestEntity(component.transform, true);
            return nearest && nearest.gameObject == root;
        }

        bool HasNestedEntities(RecordedEntity entity)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].HasNestedEntities(entity))
                    return true;
            return false;
        }

        public StreamRows CreateStream(Type type, in RecordedStreamInfo info, bool forwardFill = false,
            StreamRows baseStream = null, (string, string)[] baseColumns = null)
        {
            var layout = _types.GetRow(type);
            var rows = _streamPool.Count > 0 ? _streamPool.Pop() : new StreamRows();
            rows.id = _nextStreamId++;
            rows.layout = layout;
            rows.forwardFill = forwardFill;
            rows.retired = false;
            rows.box = null;
            rows.baseStream = null;
            rows.baseColumns = null;
            rows.slot = -1;
            rows.rowCount = 0;
            rows.life = null;
            rows.aliveTick = 0;
            if (rows.lastBytes.Length != layout.managed.Count)
            {
                rows.lastObject = new object[layout.managed.Count];
                rows.lastBytes = new RecordingBuffer[layout.managed.Count];
                rows.lastVersion = new int[layout.managed.Count];
            }
            if (baseStream != null && baseColumns != null)
            {
                rows.baseStream = baseStream;
                rows.baseColumns = BaseColumnsFor(layout, baseStream.layout, baseColumns);
            }
            _streams.Add(rows);

            FlushPendingTypes();
            var json = _json.Clear().BeginObject().Prop("id", rows.id).Prop("type", layout.id).Key("name");
            switch (info.kind)
            {
                case RecordedStreamKind.State:
                case RecordedStreamKind.Input:
                    json.BeginString().Part("e").Part(info.entity).Part(".c").Part((long)info.component)
                        .Part(info.kind == RecordedStreamKind.State ? ".state" : ".input").EndString()
                        .Prop("kind", info.kind == RecordedStreamKind.State ? "state" : "input")
                        .Prop("entity", info.entity).Prop("component", info.component)
                        .Prop("componentType", TypeName(info.componentType));
                    break;
                case RecordedStreamKind.Sync:
                    json.BeginString().Part("e").Part(info.entity).Part(".c").Part((long)info.component)
                        .Part(".").Part(info.field).EndString()
                        .Prop("kind", "sync").Prop("entity", info.entity).Prop("component", info.component)
                        .Prop("componentType", TypeName(info.componentType)).Prop("module", info.module)
                        .Prop("field", info.field);
                    break;
                case RecordedStreamKind.Transform:
                    json.BeginString().Part("e").Part(info.entity).Part(".transform").EndString()
                        .Prop("kind", "transform").Prop("entity", info.entity);
                    break;
                case RecordedStreamKind.Player:
                    json.BeginString().Part("p").Part((long)info.player).EndString()
                        .Prop("kind", "player").Prop("player", info.player);
                    break;
                case RecordedStreamKind.Collider:
                    json.BeginString().Part(info.entity >= 0 ? "e" : "static");
                    if (info.entity >= 0)
                        json.Part(info.entity);
                    json.Part(".collider").Part(info.collider).EndString()
                        .Prop("kind", "collider").Key("entity");
                    if (info.entity >= 0) json.Value(info.entity); else json.Null();
                    json.Prop("collider", info.collider);
                    break;
                default:
                    json.BeginString();
                    if (info.hasPlayer)
                        json.Part("p").Part((long)info.player).Part(".");
                    json.Part(info.sample).EndString()
                        .Prop("kind", "custom").Prop("sample", info.sample)
                        .Key("player").Value(info.hasPlayer ? (long)info.player : -1);
                    break;
            }
            if (forwardFill)
                json.Prop("fill", true);
            if (rows.baseStream != null)
            {
                json.Key("base").BeginObject().Prop("stream", rows.baseStream.id).Key("columns").BeginObject();
                foreach (var (own, other) in baseColumns)
                    json.Prop(own, other);
                json.EndObject().EndObject();
            }
            WriteMeta(RecordKind.Stream, json.EndObject());
            return rows;
        }

        int[] BaseColumnsFor(RowLayout own, RowLayout other, (string, string)[] names)
        {
            if (_baseColumns.TryGetValue((other, names), out var columns))
                return columns;
            columns = new int[names.Length * 2];
            for (int i = 0; i < names.Length; i++)
            {
                columns[i * 2] = ColumnOffset(own, names[i].Item1);
                columns[i * 2 + 1] = ColumnOffset(other, names[i].Item2);
            }
            _baseColumns[(other, names)] = columns;
            return columns;
        }

        static int ColumnOffset(RowLayout layout, string name)
        {
            foreach (var c in layout.columns)
                if (c.name == name)
                    return c.offset;
            throw new ArgumentException($"{layout.type} has no column {name}");
        }

        public unsafe void AppendRow<T>(StreamRows rows, ulong tick, ref T value) where T : struct
        {
            rows.aliveTick = tick;
            var layout = rows.layout;
            bool first = rows.slot < 0;
            if (!first && layout.rowSize == 0 && rows.forwardFill && SameSources(rows, ref value))
                return;
            var chunk = _job.log;
            var log = chunk.bytes;
            int start = log.length;
            bool hadTick = chunk.hasTick;
            ulong lastTick = chunk.lastTick;
            BeginRow(rows, tick, chunk);
            int at = log.length;
            layout.WriteFixed(ref value, log);
            bool write = !rows.forwardFill || first || !SameBytes(log.data, at, rows.lastOffset, layout.rowSize);
            if (layout.hasManaged)
                write |= WriteManaged(rows, ref value, first, log);
            if (write)
            {
                rows.lastOffset = at;
                return;
            }
            log.length = start;
            chunk.hasTick = hadTick;
            chunk.lastTick = lastTick;
            rows.rowCount--;
        }

        unsafe bool SameSources<T>(StreamRows rows, ref T value) where T : struct
        {
            var writers = _types.GetManagedWriters(rows.layout);
            byte* row = (byte*)UnsafeUtility.AddressOf(ref value);
            for (int i = 0; i < writers.Length; i++)
            {
                var writer = writers[i];
                if (writer == null || writer.disabled || !writer.Identify(row, out var source, out int version) ||
                    !ReferenceEquals(source, rows.lastObject[i]) || version != rows.lastVersion[i])
                    return false;
            }
            return writers.Length > 0;
        }

        unsafe bool WriteManaged<T>(StreamRows rows, ref T value, bool first, RecordingBuffer log) where T : struct
        {
            bool measured = _stats.enabled;
            if (measured)
                _stats.Begin(_statManaged);
            var layout = rows.layout;
            var writers = _types.GetManagedWriters(layout);
            bool changed = false;
            byte* row = (byte*)UnsafeUtility.AddressOf(ref value);
            for (int i = 0; i < writers.Length; i++)
            {
                var writer = writers[i];
                object source = null;
                int version = 0;
                bool known = writer != null && !writer.disabled && writer.Identify(row, out source, out version);
                var kept = rows.lastBytes[i];
                if (known && ReferenceEquals(source, rows.lastObject[i]) && version == rows.lastVersion[i])
                {
                    if (first)
                    {
                        log.WriteInt32(kept.length);
                        log.WriteBytes(kept);
                    }
                    else
                        log.WriteInt32(-1);
                    continue;
                }
                rows.lastObject[i] = null;

                _scratch.Clear();
                _visiting.Clear();
                try
                {
                    if (writer != null && !writer.disabled)
                    {
                        writer.Write(row, _types, _scratch, _visiting);
                        if (writer.NeedsSelfCheck(_scratch.length <= 2))
                            CheckManagedField(rows, ref value, i, writer);
                    }
                    else
                    {
                        _types.WriteTagged(_scratch, RowLayout.ReadManaged(Box(rows, ref value), layout.managed[i]), 0, _visiting);
                    }
                }
                catch (Exception)
                {
                    _scratch.Clear();
                    _scratch.WriteByte(12);
                    known = false;
                }
                if (known)
                {
                    rows.lastObject[i] = source;
                    rows.lastVersion[i] = version;
                }
                if (!first && kept != null && kept.SameBytes(_scratch))
                {
                    log.WriteInt32(-1);
                    continue;
                }
                changed = true;
                (kept ?? (rows.lastBytes[i] = new RecordingBuffer(_scratch.length))).CopyFrom(_scratch);
                log.WriteInt32(_scratch.length);
                log.WriteBytes(_scratch);
            }
            if (measured)
                _stats.End(_statManaged);
            return changed;
        }

        static unsafe bool SameBytes(byte[] data, int a, int b, int length)
        {
            if (length == 0)
                return true;
            fixed (byte* p = data)
                return UnsafeUtility.MemCmp(p + a, p + b, length) == 0;
        }

        void BeginRow(StreamRows rows, ulong tick, ChunkLog log)
        {
            if (rows.slot < 0)
            {
                rows.slot = _slots.Count;
                _slots.Add(rows);
            }
            rows.rowCount++;
            if (log.hasTick && log.lastTick == tick)
                log.bytes.WriteInt32(rows.slot);
            else
            {
                log.bytes.WriteInt32(rows.slot | ChunkLog.TickFollows);
                log.bytes.WriteUInt64(tick);
                log.lastTick = tick;
                log.hasTick = true;
            }
        }

        void CheckManagedField<T>(StreamRows rows, ref T value, int field, ManagedFieldWriter writer) where T : struct
        {
            _check.Clear();
            _visiting.Clear();
            _types.WriteGeneric(_check, RowLayout.ReadManaged(Box(rows, ref value), rows.layout.managed[field]), 0, _visiting);
            if (_check.SameBytes(_scratch))
                return;
            writer.disabled = true;
            AddError("field_writer_mismatch", $"{rows.layout.managed[field].name} of {TypeName(rows.layout.type)}");
            _scratch.CopyFrom(_check);
        }

        static object Box<T>(StreamRows rows, ref T value) where T : struct
        {
            object boxed = rows.box;
            if (boxed is T)
                UnsafeUtility.As<object, BoxedValue<T>>(ref boxed).value = value;
            else
                rows.box = boxed = value;
            return boxed;
        }

        sealed class BoxedValue<T> where T : struct
        {
            public T value;
        }

        public void FlushPendingTypes()
        {
            _pendingTypes.Clear();
            if (!_types.TakePending(_pendingTypes))
                return;
            foreach (var json in _pendingTypes)
                WriteMeta(RecordKind.Type, json);
        }

        ChunkJob TakeJob()
        {
            ChunkJob job;
            lock (_jobPool)
                job = _jobPool.Count > 0 ? _jobPool.Pop() : null;
            job ??= new ChunkJob(this);
            job.log.Clear();
            job.log.bytes.Reserve(_logCapacity);
            return job;
        }

        void FlushChunk()
        {
            if (_file == null)
                return;

            if (_slots.Count > 0)
            {
                FlushPendingTypes();
                FlushMeta();

                var log = _job.log;
                _peakLogBytes = Math.Max(_peakLogBytes, log.bytes.length);
                log.firstTick = _chunkFirstTick;
                log.tickCount = _lastTick + 1 - _chunkFirstTick;
                if (log.streams.Capacity < _slots.Count)
                    log.streams.Capacity = Mathf.NextPowerOfTwo(_slots.Count);
                foreach (var s in _slots)
                {
                    log.streams.Add(new ChunkStream
                    {
                        id = s.id, layout = s.layout, forwardFill = s.forwardFill, baseColumns = s.baseColumns,
                        baseSlot = s.baseStream != null ? s.baseStream.slot : -1, lastTick = s.lastTick
                    });
                }
                _file.Enqueue(RecordKind.Chunk, _job, true);
                _job = TakeJob();
                _chunks++;
            }

            foreach (var s in _slots)
            {
                s.slot = -1;
                s.rowCount = 0;
            }
            _slots.Clear();

            int kept = 0;
            for (int i = 0; i < _streams.Count; i++)
            {
                var s = _streams[i];
                if (s.retired)
                {
                    s.baseStream = null;
                    s.box = null;
                    Array.Clear(s.lastObject, 0, s.lastObject.Length);
                    _streamPool.Push(s);
                    continue;
                }
                _streams[kept++] = s;
            }
            _streams.RemoveRange(kept, _streams.Count - kept);
            _chunkFirstTick = _lastTick + 1;
            chunkStart = true;
            _sampleEpoch++;
        }

        public const string CameraStream = "camera";

        public void WriteSample<T>(string stream, in T sample) where T : struct
        {
            if (!_context.isReady)
                return;

            var envelope = new ClientSample<T>
            {
                clientTick = _context.localTick,
                viewTick = _context.viewTick,
                clientTime = ClientTime(),
                sample = sample
            };

            if (_context.isServer)
            {
                if (_context.localPlayer.HasValue)
                    WriteServerSample(stream, _context.localPlayer, ref envelope);
                return;
            }

            WritePartSample(stream, ref envelope);
        }

        public void WriteSampleFor<T>(string stream, PlayerID player, in T sample) where T : struct
        {
            if (!_context.isReady || !_context.isServer)
                return;

            var envelope = new ClientSample<T>
            {
                clientTick = _context.localTick,
                viewTick = _context.viewTick,
                clientTime = ClientTime(),
                sample = sample
            };
            WriteServerSample(stream, player, ref envelope);
        }

        int _clientTimeFrame = -1;
        double _clientTime;

        double ClientTime()
        {
            int frame = Time.frameCount;
            if (frame != _clientTimeFrame)
            {
                _clientTimeFrame = frame;
                _clientTime = Time.realtimeSinceStartupAsDouble - _startTime;
            }
            return _clientTime;
        }

        void WriteServerSample<T>(string stream, PlayerID? player, ref ClientSample<T> envelope) where T : struct
        {
            if (!isRecording)
                return;
            var rows = GetCustomStream(stream, typeof(ClientSample<T>), player);
            AppendRow(rows, SampleTick(), ref envelope);
        }

        ulong SampleTick() => Math.Max(_context.localTick, _chunkFirstTick);

        StreamRows GetCustomStream(string stream, Type type, PlayerID? player)
        {
            var key = new CustomKey(stream, player);
            if (_customStreams.TryGetValue(key, out var rows))
                return rows;

            rows = CreateStream(type, new RecordedStreamInfo
            {
                kind = RecordedStreamKind.Custom, sample = stream,
                hasPlayer = player.HasValue, player = player.HasValue ? player.Value.id.value : 0
            });
            _customStreams[key] = rows;
            return rows;
        }

        public JsonBuilder json => _json;

        public void WriteMeta(RecordKind kind, JsonBuilder json)
        {
            _meta.WriteByte((byte)kind);
            _meta.WriteVarUInt((ulong)json.length);
            _meta.WriteBytes(json.data, 0, json.length);
        }

        void WriteMeta(RecordKind kind, string json)
        {
            _meta.WriteByte((byte)kind);
            _meta.WriteVarUInt((ulong)System.Text.Encoding.UTF8.GetByteCount(json));
            _meta.WriteUtf8(json);
        }

        void WriteMeta(RecordKind kind, byte[] payload, int length)
        {
            _meta.WriteByte((byte)kind);
            _meta.WriteVarUInt((ulong)length);
            _meta.WriteBytes(payload, 0, length);
        }

        void FlushMeta()
        {
            if (_meta.length == 0)
                return;
            MetaBatch batch;
            lock (_metaPool)
                batch = _metaPool.Count > 0 ? _metaPool.Pop() : null;
            batch ??= new MetaBatch { owner = this };
            (batch.buffer, _meta) = (_meta, batch.buffer);
            _meta.Reserve(_logCapacity / 8);
            _file.Enqueue(RecordKind.Batch, batch, true);
        }

        public string Name(UnityEngine.Object o)
        {
            long id = RecordingObjectId.Of(o);
            if (_names.TryGetValue(id, out var name))
                return name;
            if (_names.Count >= MaxCachedNames)
                _names.Clear();
            return _names[id] = o.name;
        }

        readonly List<string> _pathParts = new List<string>(16);

        public void WritePath(JsonBuilder json, Transform t)
        {
            for (var p = t; p; p = p.parent)
                _pathParts.Add(Name(p.gameObject));
            json.BeginString();
            for (int i = _pathParts.Count - 1; i >= 0; i--)
            {
                json.Part(_pathParts[i]);
                if (i > 0)
                    json.Part("/");
            }
            json.EndString();
            _pathParts.Clear();
        }

        public string TypeName(Type type)
        {
            if (!_typeNames.TryGetValue(type, out var name))
                _typeNames[type] = name = RecordingTypeRegistry.TypeName(type);
            return name;
        }

        static bool IsBodyRenderer(Renderer renderer) =>
            renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is SpriteRenderer;

        static byte Visibility(RecordedEntity entity)
        {
            var renderers = entity.renderers;
            if (renderers.Count == 0)
                return 2;
            var objects = entity.rendererObjects;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r && r.enabled && objects[i].activeInHierarchy)
                    return 1;
            }
            return 0;
        }

        readonly List<Renderer> _rendererList = new List<Renderer>();

        bool RefreshEntityRenderers(RecordedEntity entity)
        {
            var root = entity.gameObject;
            bool nested = HasNestedEntities(entity);

            entity.renderers.Clear();
            entity.rendererObjects.Clear();
            root.GetComponentsInChildren(true, entity.foundRenderers);
            foreach (var renderer in entity.foundRenderers)
            {
                if (!IsBodyRenderer(renderer))
                    continue;
                if (!nested || OwnedBy(renderer, root))
                {
                    entity.renderers.Add(renderer);
                    entity.rendererObjects.Add(renderer.gameObject);
                }
            }

            entity.detachedGraphics.Clear();
            for (int i = 0, parts = entity.partCount; i < parts; i++)
                entity.SourceAt(i).GetDetachedGraphics(entity, entity.detachedGraphics);
            entity.foundGraphics.Clear();
            foreach (var graphics in entity.detachedGraphics)
            {
                if (!graphics || graphics.IsChildOf(root.transform))
                    continue;
                graphics.GetComponentsInChildren(true, _rendererList);
                entity.foundGraphics.AddRange(_rendererList);
                foreach (var renderer in _rendererList)
                {
                    if (IsBodyRenderer(renderer))
                    {
                        entity.renderers.Add(renderer);
                        entity.rendererObjects.Add(renderer.gameObject);
                    }
                }
            }
            _rendererList.Clear();
            return nested;
        }

#if UNITY_PHYSICS_3D

        const ulong ColliderCheckTicks = 10;
        const ulong StaticColliderCheckTicks = 60;
        readonly RecordingBuffer _staticColliderKey = new RecordingBuffer(256);
        bool _hasStaticColliderKey;
        readonly RecordingBuffer _colliderKey = new RecordingBuffer(256);
        readonly List<Collider> _colliderList = new List<Collider>();
        readonly List<Collider> _colliderScratch = new List<Collider>();
        readonly List<GameObject> _sceneRoots = new List<GameObject>();
        readonly Dictionary<long, int> _meshIds = new Dictionary<long, int>();
        int _nextMeshId = 1;

        long _checkDeadline;
        int _entityCheckCursor;
        float _entityChecksOwed;

        bool OverBudget => Stopwatch.GetTimestamp() > _checkDeadline;

        bool IsEntityObject(GameObject go)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].IsEntityObject(go))
                    return true;
            return false;
        }

        bool IsLocalPhysics(Collider collider)
        {
            var body = collider.attachedRigidbody;
            if (!body || body.isKinematic)
                return false;
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].DrivesBody(body))
                    return false;
            return true;
        }

        public void RecordColliders(ulong tick)
        {
            _stats.Begin(_statColliders);

            _checkDeadline = Stopwatch.GetTimestamp() + (long)(colliderCheckBudgetMs * 0.001 * Stopwatch.Frequency);
            _stats.Begin(_statChecks);
            CheckEntityColliders(tick);
            if (_staticCheck == StaticCheck.Idle && tick % StaticColliderCheckTicks == 0)
                BeginStaticCheck();
            if (_staticCheck != StaticCheck.Idle)
                ContinueStaticCheck(tick);
            _stats.End(_statChecks);

            foreach (var entity in _entities)
            {
                if (entity.movingColliders > 0 && entity.gameObject)
                    SampleMovingColliders(entity.colliders, entity.gameObject.transform.worldToLocalMatrix, tick);
            }
            if (_staticMovingColliders > 0)
                SampleMovingColliders(_staticColliders, Matrix4x4.identity, tick);
            _stats.End(_statColliders);
        }

        void CheckEntityColliders(ulong tick)
        {
            int count = _entities.Count;
            if (count == 0)
                return;
            _entityChecksOwed = Math.Min(count, _entityChecksOwed + (float)count / ColliderCheckTicks);
            bool first = true;
            while (_entityChecksOwed >= 1f && (first || !OverBudget))
            {
                first = false;
                if (_entityCheckCursor >= _entities.Count)
                    _entityCheckCursor = 0;
                var entity = _entities[_entityCheckCursor++];
                if (entity.spawnTick != tick)
                    RefreshEntityColliders(entity, tick);
                _entityChecksOwed -= 1f;
            }
        }

        void RefreshStaticColliders(ulong tick)
        {
            _colliderList.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                scene.GetRootGameObjects(_sceneRoots);
                foreach (var root in _sceneRoots)
                {
                    if (!root.activeInHierarchy)
                        continue;
                    root.GetComponentsInChildren(false, _colliderScratch);
                    foreach (var collider in _colliderScratch)
                        if (collider.enabled && !IsLocalPhysics(collider) && !IsUnderEntity(collider))
                            _colliderList.Add(collider);
                }
            }
            _sceneRoots.Clear();
            _colliderScratch.Clear();

            RefreshColliderSet(_colliderList, null, Matrix4x4.identity, _staticColliderKey, ref _hasStaticColliderKey,
                _staticColliders, ref _staticMovingColliders, -1, tick);
            _colliderList.Clear();
        }

        readonly List<TrackedCollider> _staticColliders = new List<TrackedCollider>();
        int _staticMovingColliders;

        const int StaticCheckSpreadTicks = 45;
        const int StaticCheckMinimumWork = 32;
        enum StaticCheck { Idle, Scan, Poses }

        StaticCheck _staticCheck;
        readonly List<Transform> _staticStack = new List<Transform>();
        readonly List<Collider> _staticFound = new List<Collider>();
        readonly List<Collider> _nodeColliders = new List<Collider>();
        List<UnityEngine.Object> _scanned = new List<UnityEngine.Object>(256), _scanning = new List<UnityEngine.Object>(256);
        int _staticNext;
        int _staticBudget;
        int _staticKeyMatched;
        bool _staticChanged;
        int _staticWork;
        int _lastStaticWork;

        void BeginStaticCheck()
        {
            _staticStack.Clear();
            _staticFound.Clear();
            for (int s = SceneManager.sceneCount - 1; s >= 0; s--)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                scene.GetRootGameObjects(_sceneRoots);
                for (int r = _sceneRoots.Count - 1; r >= 0; r--)
                    _staticStack.Add(_sceneRoots[r].transform);
            }
            _sceneRoots.Clear();

            _staticCheck = StaticCheck.Scan;
            _staticKeyMatched = 0;
            _staticChanged = false;
            _staticWork = 0;
            _staticBudget = Math.Max(256, (_lastStaticWork + _staticColliders.Count) / StaticCheckSpreadTicks + 1);
        }

        void ContinueStaticCheck(ulong tick)
        {
            int budget = _staticBudget;
            int minimum = budget - StaticCheckMinimumWork;
            if (_staticCheck == StaticCheck.Scan)
            {
                var key = _colliderKey;
                int walked = 0;
                while (budget > 0 && _staticStack.Count > 0 && (budget > minimum || (++walked & 7) != 0 || !OverBudget))
                {
                    var t = _staticStack[_staticStack.Count - 1];
                    _staticStack.RemoveAt(_staticStack.Count - 1);
                    budget--;
                    _staticWork++;
                    if (!t)
                        continue;
                    var go = t.gameObject;
                    _scanning.Add(t);
                    _scanning.Add(go);
                    if (!go.activeSelf || IsEntityObject(go))
                        continue;

                    go.GetComponents(_nodeColliders);
                    foreach (var collider in _nodeColliders)
                    {
                        _scanning.Add(collider);
                        budget--;
                        _staticWork++;
                        if (!collider.enabled || IsLocalPhysics(collider))
                            continue;
                        _staticFound.Add(collider);
                        if (_staticChanged)
                            continue;
                        key.Clear();
                        ShapeKey(key, collider);
                        if (_staticKeyMatched + key.length > _staticColliderKey.length ||
                            !_staticColliderKey.SameBytes(_staticKeyMatched, key.length, key.data, 0, key.length))
                            _staticChanged = true;
                        _staticKeyMatched += key.length;
                    }
                    _nodeColliders.Clear();

                    for (int c = t.childCount - 1; c >= 0; c--)
                        _staticStack.Add(t.GetChild(c));
                }
                if (_staticStack.Count > 0)
                    return;

                (_scanned, _scanning) = (_scanning, _scanned);
                _scanning.Clear();
                _lastStaticWork = _staticWork;
                bool changed = _hasStaticColliderKey
                    ? _staticChanged || _staticKeyMatched != _staticColliderKey.length
                    : _staticFound.Count > 0;
                if (changed)
                {
                    for (int i = _staticFound.Count - 1; i >= 0; i--)
                        if (!_staticFound[i])
                            _staticFound.RemoveAt(i);
                    RefreshColliderSet(_staticFound, null, Matrix4x4.identity, _staticColliderKey, ref _hasStaticColliderKey,
                        _staticColliders, ref _staticMovingColliders, -1, tick);
                    _staticFound.Clear();
                    _staticCheck = StaticCheck.Idle;
                    return;
                }
                _staticFound.Clear();
                _staticCheck = StaticCheck.Poses;
                _staticNext = 0;
                return;
            }

            int end = Math.Min(_staticColliders.Count, _staticNext + budget);
            int to = Math.Min(end, _staticNext + StaticCheckMinimumWork);
            CheckColliderPoses(_staticColliders, _staticNext, to, Matrix4x4.identity, ref _staticMovingColliders, -1);
            while (to < end && !OverBudget)
            {
                int from = to;
                to = Math.Min(end, to + 16);
                CheckColliderPoses(_staticColliders, from, to, Matrix4x4.identity, ref _staticMovingColliders, -1);
            }
            _staticNext = to;
            if (_staticNext >= _staticColliders.Count)
                _staticCheck = StaticCheck.Idle;
        }

        void ResetStaticCheck()
        {
            _staticCheck = StaticCheck.Idle;
            _staticStack.Clear();
            _staticFound.Clear();
            _scanned.Clear();
            _scanning.Clear();
            _lastStaticWork = 0;
            _entityCheckCursor = 0;
            _entityChecksOwed = 0;
        }

        readonly List<TrackedCollider> _trackedScratch = new List<TrackedCollider>();
        readonly Dictionary<long, TrackedCollider> _trackedById = new Dictionary<long, TrackedCollider>();
        readonly Stack<TrackedCollider> _trackedPool = new Stack<TrackedCollider>(256);
        const int StillChecksBeforeIdle = 2;

        void RefreshColliderSet(List<Collider> colliders, Transform space, Matrix4x4 worldToSpace, RecordingBuffer lastKey,
            ref bool hasKey, List<TrackedCollider> tracked, ref int moving, int entity, ulong tick)
        {
            var key = _colliderKey;
            key.Clear();
            foreach (var collider in colliders)
                ShapeKey(key, collider);
            bool changed = !hasKey ? key.length != 0 : !key.SameBytes(lastKey);

            if (changed)
            {
                lastKey.CopyFrom(key);
                hasKey = true;

                _trackedScratch.Clear();
                _trackedById.Clear();
                foreach (var t in tracked)
                    _trackedById[t.id] = t;
                foreach (var collider in colliders)
                {
                    long id = RecordingObjectId.Of(collider);
                    if (_trackedById.TryGetValue(id, out var t) && ReferenceEquals(t.collider, collider))
                        _trackedById.Remove(id);
                    else
                        t = null;
                    if (t == null)
                    {
                        t = _trackedPool.Count > 0 ? _trackedPool.Pop() : new TrackedCollider();
                        t.collider = collider;
                        t.id = RecordingObjectId.Of(collider);
                        t.stream = null;
                        t.moving = false;
                        t.stillChecks = 0;
                    }
                    t.streamable = HasShapePose(collider) && !ReferenceEquals(collider.transform, space);
                    RoundedPose(collider, worldToSpace, out t.position, out t.rotation, out t.scale);
                    _trackedScratch.Add(t);
                }
                tracked.Clear();
                foreach (var pair in _trackedById)
                    tracked.Add(pair.Value);
                _trackedById.Clear();
                ReleaseColliders(tracked);
                tracked.AddRange(_trackedScratch);
                _trackedScratch.Clear();
                moving = 0;
                foreach (var t in tracked)
                    if (t.moving)
                        moving++;

                var json = _json.Clear().BeginObject().Key("entity");
                if (entity >= 0) json.Value(entity); else json.Null();
                json.Prop("tick", tick).Key("colliders").BeginArray();
                foreach (var collider in colliders)
                    WriteCollider(json, collider, worldToSpace);
                json.EndArray().EndObject();
                WriteMeta(RecordKind.Colliders, json);
                return;
            }

            CheckColliderPoses(tracked, 0, tracked.Count, worldToSpace, ref moving, entity);
        }

        void CheckColliderPoses(List<TrackedCollider> tracked, int from, int to, Matrix4x4 worldToSpace, ref int moving,
            int entity)
        {
            for (int i = from; i < to; i++)
            {
                var t = tracked[i];
                if (!t.streamable || !t.collider)
                    continue;
                RoundedPose(t.collider, worldToSpace, out var position, out var rotation, out var scale);
                bool moved = !position.Equals(t.position) || !rotation.Equals(t.rotation) || !scale.Equals(t.scale);
                t.position = position;
                t.rotation = rotation;
                t.scale = scale;
                if (t.moving)
                {
                    t.stillChecks = moved ? 0 : t.stillChecks + 1;
                    if (t.stillChecks >= StillChecksBeforeIdle)
                    {
                        t.moving = false;
                        moving--;
                    }
                }
                else if (moved)
                {
                    t.moving = true;
                    t.stillChecks = 0;
                    moving++;
                    t.stream ??= CreateStream(typeof(RecordedColliderPose),
                        new RecordedStreamInfo { kind = RecordedStreamKind.Collider, entity = entity, collider = t.id }, true);
                }
            }
        }

        void SampleMovingColliders(List<TrackedCollider> tracked, Matrix4x4 worldToSpace, ulong tick)
        {
            foreach (var t in tracked)
            {
                if (!t.moving || !t.collider)
                    continue;
                var local = worldToSpace * t.collider.transform.localToWorldMatrix;
                var pose = new RecordedColliderPose
                {
                    position = local.GetColumn(3),
                    rotation = local.ValidTRS() ? local.rotation : Quaternion.identity,
                    scale = local.lossyScale
                };
                AppendRow(t.stream, tick, ref pose);
            }
        }

        void ReleaseColliders(List<TrackedCollider> tracked)
        {
            foreach (var t in tracked)
            {
                if (t.stream != null)
                    t.stream.retired = true;
                t.stream = null;
                t.collider = null;
                t.moving = false;
                _trackedPool.Push(t);
            }
            tracked.Clear();
        }

        static void RoundedPose(Collider collider, Matrix4x4 worldToSpace, out Vector3 position, out Quaternion rotation,
            out Vector3 scale)
        {
            var pose = worldToSpace * collider.transform.localToWorldMatrix;
            Vector3 p = pose.GetColumn(3);
            var r = pose.ValidTRS() ? pose.rotation : Quaternion.identity;
            var s = pose.lossyScale;
            position = new Vector3(Round(p.x), Round(p.y), Round(p.z));
            rotation = new Quaternion(Round(r.x), Round(r.y), Round(r.z), Round(r.w));
            scale = new Vector3(Round(s.x), Round(s.y), Round(s.z));
        }

        static bool HasShapePose(Collider collider) =>
            collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
            collider is CharacterController || collider is MeshCollider;

        void ShapeKey(RecordingBuffer key, Collider collider)
        {
            key.WriteVarInt(RecordingObjectId.Of(collider));
            key.WriteByte(collider.isTrigger ? (byte)1 : (byte)0);
            key.WriteVarInt(collider.gameObject.layer);
            switch (collider)
            {
                case BoxCollider box:
                    key.WriteByte(1); KeyVector(key, box.center); KeyVector(key, box.size);
                    break;
                case SphereCollider sphere:
                    key.WriteByte(2); KeyVector(key, sphere.center); key.WriteFloat(sphere.radius);
                    break;
                case CapsuleCollider capsule:
                    key.WriteByte(3); KeyVector(key, capsule.center); key.WriteFloat(capsule.radius);
                    key.WriteFloat(capsule.height); key.WriteVarInt(capsule.direction);
                    break;
                case CharacterController controller:
                    key.WriteByte(4); KeyVector(key, controller.center); key.WriteFloat(controller.radius);
                    key.WriteFloat(controller.height);
                    break;
                case MeshCollider meshCollider:
                {
                    var mesh = meshCollider.sharedMesh;
                    key.WriteByte(5); key.WriteVarInt(mesh ? RecordingObjectId.Of(mesh) : 0);
                    key.WriteByte(meshCollider.convex ? (byte)1 : (byte)0);
                    key.WriteByte(mesh && WriteMesh(mesh) > 0 ? (byte)1 : (byte)0);
                    break;
                }
                default:
                {
                    var b = collider.bounds;
                    key.WriteByte(6); KeyVector(key, b.center); KeyVector(key, b.size);
                    break;
                }
            }
        }

        static void KeyVector(RecordingBuffer key, Vector3 v)
        {
            key.WriteFloat(v.x);
            key.WriteFloat(v.y);
            key.WriteFloat(v.z);
        }

        void RefreshEntityColliders(RecordedEntity entity, ulong tick)
        {
            var root = entity.gameObject;
            if (!root)
                return;

            bool nested = RefreshEntityRenderers(entity);

            root.GetComponentsInChildren(false, entity.foundColliders);
            _colliderList.Clear();
            foreach (var collider in entity.foundColliders)
            {
                if (collider.enabled && !IsLocalPhysics(collider) && (!nested || OwnedBy(collider, root)))
                    _colliderList.Add(collider);
            }

            RefreshColliderSet(_colliderList, entity.root, entity.root.worldToLocalMatrix, entity.colliderKey,
                ref entity.hasColliderKey, entity.colliders, ref entity.movingColliders, entity.id, tick);
            _colliderList.Clear();
        }

        void WriteCollider(JsonBuilder json, Collider collider, Matrix4x4 worldToSpace)
        {
            var local = worldToSpace * collider.transform.localToWorldMatrix;
            switch (collider)
            {
                case BoxCollider box:
                    BeginCollider(json, "box", collider, local);
                    json.Floats("center", box.center.x, box.center.y, box.center.z)
                        .Floats("size", box.size.x, box.size.y, box.size.z);
                    break;
                case SphereCollider sphere:
                    BeginCollider(json, "sphere", collider, local);
                    json.Floats("center", sphere.center.x, sphere.center.y, sphere.center.z).Prop("radius", sphere.radius);
                    break;
                case CapsuleCollider capsule:
                    BeginCollider(json, "capsule", collider, local);
                    json.Floats("center", capsule.center.x, capsule.center.y, capsule.center.z)
                        .Prop("radius", capsule.radius).Prop("height", capsule.height).Prop("direction", capsule.direction);
                    break;
                case CharacterController controller:
                    BeginCollider(json, "capsule", collider, local);
                    json.Floats("center", controller.center.x, controller.center.y, controller.center.z)
                        .Prop("radius", controller.radius).Prop("height", controller.height).Prop("direction", 1);
                    break;
                case MeshCollider meshCollider:
                {
                    var mesh = meshCollider.sharedMesh;
                    if (!mesh)
                        return;
                    int meshId = WriteMesh(mesh);
                    if (meshId > 0)
                    {
                        BeginCollider(json, "mesh", collider, local);
                        json.Prop("mesh", meshId).Prop("convex", meshCollider.convex);
                    }
                    else
                    {
                        var b = mesh.bounds;
                        BeginCollider(json, "bounds", collider, local);
                        json.Floats("center", b.center.x, b.center.y, b.center.z).Floats("size", b.size.x, b.size.y, b.size.z);
                    }
                    break;
                }
                default:
                {
                    var b = collider.bounds;
                    BeginCollider(json, "bounds", collider, worldToSpace);
                    json.Floats("center", b.center.x, b.center.y, b.center.z).Floats("size", b.size.x, b.size.y, b.size.z);
                    break;
                }
            }

            json.EndObject();
        }

        void BeginCollider(JsonBuilder json, string shape, Collider collider, Matrix4x4 pose)
        {
            Vector3 p = pose.GetColumn(3);
            var r = pose.ValidTRS() ? pose.rotation : Quaternion.identity;
            var s = pose.lossyScale;
            json.BeginObject().Prop("shape", shape).Prop("id", RecordingObjectId.Of(collider))
                .Key("pose").BeginObject()
                .Floats("p", Round(p.x), Round(p.y), Round(p.z)).Floats("r", Round(r.x), Round(r.y), Round(r.z), Round(r.w))
                .Floats("s", Round(s.x), Round(s.y), Round(s.z))
                .EndObject()
                .Prop("trigger", collider.isTrigger).Prop("layer", collider.gameObject.layer)
                .Prop("name", Name(collider));
        }

        static float Round(float value) => (float)Math.Round(value, 4) + 0f;

        readonly List<Vector3> _meshVertices = new List<Vector3>();
        readonly List<int> _meshTriangles = new List<int>();
        readonly List<int> _meshIndices = new List<int>();
        readonly RecordingBuffer _meshBuffer = new RecordingBuffer(1024);

        static int VarLength(ulong value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        unsafe bool WriteMeshNative(Mesh mesh, int id)
        {
            using var meshes = Mesh.AcquireReadOnlyMeshData(mesh);
            var data = meshes[0];
            int indexCount = 0;
            for (int sub = 0; sub < data.subMeshCount; sub++)
            {
                var desc = data.GetSubMesh(sub);
                if (desc.topology != MeshTopology.Triangles)
                    return false;
                indexCount += desc.indexCount;
            }

            int vertexCount = data.vertexCount;
            using var vertices = new NativeArray<Vector3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            using var indices = new NativeArray<int>(indexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            data.GetVertices(vertices);
            for (int sub = 0, at = 0; sub < data.subMeshCount; sub++)
            {
                int count = data.GetSubMesh(sub).indexCount;
                data.GetIndices(indices.GetSubArray(at, count), sub);
                at += count;
            }

            WriteMeshRecord(id, mesh.name, vertices, indices);
            return true;
        }

        unsafe void WriteMeshRecord(int id, string name, NativeArray<Vector3> vertices, NativeArray<int> indices)
        {
            int vertexCount = vertices.Length;
            int indexCount = indices.Length;
            int nameBytes = System.Text.Encoding.UTF8.GetByteCount(name);
            int length = VarLength((ulong)id) + VarLength((ulong)nameBytes) + nameBytes +
                         VarLength((ulong)vertexCount) + vertexCount * 12 + VarLength((ulong)indexCount) + indexCount * 4;
            _meta.WriteByte((byte)RecordKind.Mesh);
            _meta.WriteVarUInt((ulong)length);
            _meta.WriteVarUInt((ulong)id);
            _meta.WriteString(name);
            _meta.WriteVarUInt((ulong)vertexCount);
            _meta.WriteBytes((byte*)vertices.GetUnsafeReadOnlyPtr(), vertexCount * 12);
            _meta.WriteVarUInt((ulong)indexCount);
            _meta.WriteBytes((byte*)indices.GetUnsafeReadOnlyPtr(), indexCount * 4);
        }

        sealed class MeshReadback
        {
            public RecordingFile file;
            public long key;
            public string name;
            public GraphicsBuffer vertexBuffer, indexBuffer;
            public int offset, stride, indexStride;
            public bool half;
            public int[] subMeshes;
            public NativeArray<Vector3> vertices;
            public NativeArray<int> indices;
            public int done;
            public bool failed;
        }

        int ReadMeshFromGpu(Mesh mesh, long key)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback)
                return 0;

            var read = new MeshReadback { file = _file, key = key };
            try
            {
                int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                var format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
                if (stream < 0 || mesh.GetVertexAttributeDimension(VertexAttribute.Position) != 3 ||
                    (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16))
                    return 0;

                int subMeshes = mesh.subMeshCount;
                read.subMeshes = new int[subMeshes * 3];
                for (int sub = 0; sub < subMeshes; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                        return 0;
                    read.subMeshes[sub * 3] = (int)mesh.GetIndexStart(sub);
                    read.subMeshes[sub * 3 + 1] = (int)mesh.GetIndexCount(sub);
                    read.subMeshes[sub * 3 + 2] = (int)mesh.GetBaseVertex(sub);
                }

                read.name = mesh.name;
                read.half = format == VertexAttributeFormat.Float16;
                read.offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                read.vertexBuffer = mesh.GetVertexBuffer(stream);
                read.indexBuffer = mesh.GetIndexBuffer();
                if (read.vertexBuffer == null || read.indexBuffer == null)
                {
                    read.vertexBuffer?.Dispose();
                    read.indexBuffer?.Dispose();
                    return 0;
                }
                read.stride = read.vertexBuffer.stride;
                read.indexStride = read.indexBuffer.stride;
                AsyncGPUReadback.Request(read.vertexBuffer, request => OnMeshVertices(read, request));
                AsyncGPUReadback.Request(read.indexBuffer, request => OnMeshIndices(read, request));
                return -1;
            }
            catch (Exception)
            {
                read.failed = true;
                read.vertexBuffer?.Dispose();
                read.indexBuffer?.Dispose();
                return 0;
            }
        }

        unsafe void OnMeshVertices(MeshReadback read, AsyncGPUReadbackRequest request)
        {
            if (request.hasError || read.failed)
                read.failed = true;
            else
            {
                var data = request.GetData<byte>();
                int count = data.Length / read.stride;
                read.vertices = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                byte* source = (byte*)data.GetUnsafeReadOnlyPtr() + read.offset;
                if (!read.half)
                    UnsafeUtility.MemCpyStride(read.vertices.GetUnsafePtr(), 12, source, read.stride, 12, count);
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        ushort* p = (ushort*)(source + i * read.stride);
                        read.vertices[i] = new Vector3(Mathf.HalfToFloat(p[0]), Mathf.HalfToFloat(p[1]), Mathf.HalfToFloat(p[2]));
                    }
                }
            }
            FinishMeshReadback(read);
        }

        unsafe void OnMeshIndices(MeshReadback read, AsyncGPUReadbackRequest request)
        {
            if (request.hasError || read.failed)
                read.failed = true;
            else
            {
                var data = request.GetData<byte>();
                int available = data.Length / read.indexStride;
                int total = 0;
                for (int sub = 0; sub < read.subMeshes.Length; sub += 3)
                {
                    total += read.subMeshes[sub + 1];
                    if (read.subMeshes[sub] + read.subMeshes[sub + 1] > available)
                        read.failed = true;
                }
                if (!read.failed)
                {
                    read.indices = new NativeArray<int>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    byte* source = (byte*)data.GetUnsafeReadOnlyPtr();
                    int at = 0;
                    for (int sub = 0; sub < read.subMeshes.Length; sub += 3)
                    {
                        int start = read.subMeshes[sub], baseVertex = read.subMeshes[sub + 2];
                        for (int i = 0, count = read.subMeshes[sub + 1]; i < count; i++)
                            read.indices[at++] = baseVertex + (read.indexStride == 2
                                ? ((ushort*)source)[start + i]
                                : ((int*)source)[start + i]);
                    }
                }
            }
            FinishMeshReadback(read);
        }

        void FinishMeshReadback(MeshReadback read)
        {
            if (++read.done < 2)
                return;
            read.vertexBuffer.Dispose();
            read.indexBuffer.Dispose();
            if (ReferenceEquals(_file, read.file))
            {
                if (read.failed)
                    _meshIds[read.key] = 0;
                else
                {
                    int id = _nextMeshId++;
                    WriteMeshRecord(id, read.name, read.vertices, read.indices);
                    _meshIds[read.key] = id;
                }
            }
            if (read.vertices.IsCreated)
                read.vertices.Dispose();
            if (read.indices.IsCreated)
                read.indices.Dispose();
        }

        int WriteMesh(Mesh mesh)
        {
            long key = RecordingObjectId.Of(mesh);
            if (_meshIds.TryGetValue(key, out int existing))
                return existing;

            if (!mesh.isReadable && !Application.isEditor)
                return _meshIds[key] = ReadMeshFromGpu(mesh, key);

            int id = _nextMeshId++;
            _meshIds[key] = id;
            try
            {
                bool written;
                try
                {
                    written = WriteMeshNative(mesh, id);
                }
                catch (Exception)
                {
                    written = false;
                }
                if (written)
                    return id;

                mesh.GetVertices(_meshVertices);
                _meshIndices.Clear();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    mesh.GetTriangles(_meshTriangles, sub);
                    _meshIndices.AddRange(_meshTriangles);
                }
                var buffer = _meshBuffer;
                buffer.Clear();
                buffer.WriteVarUInt((ulong)id);
                buffer.WriteString(mesh.name);
                buffer.WriteVarUInt((ulong)_meshVertices.Count);
                foreach (var v in _meshVertices)
                {
                    buffer.WriteFloat(v.x);
                    buffer.WriteFloat(v.y);
                    buffer.WriteFloat(v.z);
                }
                buffer.WriteVarUInt((ulong)_meshIndices.Count);
                buffer.Ensure(_meshIndices.Count * 4);
                foreach (var i in _meshIndices)
                {
                    buffer.data[buffer.length++] = (byte)i;
                    buffer.data[buffer.length++] = (byte)(i >> 8);
                    buffer.data[buffer.length++] = (byte)(i >> 16);
                    buffer.data[buffer.length++] = (byte)(i >> 24);
                }
                WriteMeta(RecordKind.Mesh, buffer.data, buffer.length);
                return id;
            }
            catch (Exception)
            {
                _meshIds[key] = 0;
                return 0;
            }
        }
#else
        int _entityCheckCursor;

        public void RecordColliders(ulong tick) { }
#endif
    }
}

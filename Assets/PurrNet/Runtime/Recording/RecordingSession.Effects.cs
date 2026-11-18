using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// The animators, particle systems and audio sources of one object, and what each was last seen doing. The
    /// object is an entity of the recording, or one that is not: a network identity on a client (which has no
    /// entities), or an object that only exists on this machine, like an effect the game instantiated.
    /// </summary>
    internal sealed class EntityEffects
    {
        public struct Layer
        {
#if UNITY_ANIMATION
            public Animator animator;
#endif
            public long instance;
            public int id;
            public int layer;
            public int layers;
            public int state, from;
            public float time;
            public byte flags;
            public bool known;
        }

        public struct Particles
        {
#if PURRNET_PARTICLES
            public ParticleSystem system;
#endif
            public long instance;
            public int id;
            public float time;
            public byte flags;
            public bool loop;
        }

        public struct Audio
        {
#if PURRNET_AUDIO
            public AudioSource source;
#endif
            public long instance;
            public int id;
            public byte flags;
            public bool loop;
        }

        static readonly Layer[] NoLayers = new Layer[0];
        static readonly Particles[] NoParticles = new Particles[0];
        static readonly Audio[] NoAudio = new Audio[0];

        public RecordedEntity entity;
        public GameObject root;
        public Transform rootTransform;
        public long rootId;
        /// <summary>Not an entity: what its EFFECTS records call it.</summary>
        public int objectId;
        public ulong networkId;
        public bool hasNetworkId;
        public int hierarchy;
        /// <summary>Where it is among the objects that are asked in turn, or -1 while it has nothing to ask about.</summary>
        public int index = -1;
        public int epoch;
        public long key;
        public bool hasKey;
        public bool hot;
        public Layer[] layers = NoLayers;
        public Particles[] particles = NoParticles;
        public Audio[] audio = NoAudio;
        public int layerCount, particleCount, audioCount;

        /// <summary>Only on this machine: neither an entity nor a network identity.</summary>
        public bool loose => entity == null && !hasNetworkId;

        public void ClearComponents()
        {
            Array.Clear(layers, 0, layerCount);
            Array.Clear(particles, 0, particleCount);
            Array.Clear(audio, 0, audioCount);
            layerCount = particleCount = audioCount = 0;
        }

        public void Clear()
        {
            ClearComponents();
            entity = null;
            root = null;
            rootTransform = null;
            rootId = 0;
            objectId = 0;
            networkId = 0;
            hasNetworkId = false;
            hierarchy = 0;
            index = -1;
            key = 0;
            hasKey = false;
            hot = false;
        }

        public ref Layer AddLayer()
        {
            if (layerCount == layers.Length)
                Array.Resize(ref layers, Math.Max(2, layerCount * 2));
            return ref layers[layerCount++];
        }

        public ref Particles AddParticles()
        {
            if (particleCount == particles.Length)
                Array.Resize(ref particles, Math.Max(2, particleCount * 2));
            return ref particles[particleCount++];
        }

        public ref Audio AddAudio()
        {
            if (audioCount == audio.Length)
                Array.Resize(ref audio, Math.Max(2, audioCount * 2));
            return ref audio[audioCount++];
        }

        public int AnimatorId(long instance)
        {
            for (int i = 0; i < layerCount; i++)
                if (layers[i].instance == instance)
                    return layers[i].id;
            return 0;
        }

        public int ParticlesAt(long instance)
        {
            for (int i = 0; i < particleCount; i++)
                if (particles[i].instance == instance)
                    return i;
            return -1;
        }

        public int AudioAt(long instance)
        {
            for (int i = 0; i < audioCount; i++)
                if (audio[i].instance == instance)
                    return i;
            return -1;
        }
    }

    public sealed partial class RecordingSession
    {
        /// <summary>Whether animation state changes, particle systems and audio sources starting and stopping are recorded.</summary>
        public bool recordEffects = true;

        /// <summary>
        /// Most time per tick, in milliseconds, spent asking animators, particle systems and audio sources what they
        /// are doing and looking at objects the game created. With more of them than fit, each is asked every few
        /// ticks instead of every tick.
        /// </summary>
        public float effectsBudgetMs = 0.02f;

        /// <summary>How many times an object's effects waited for a later tick, to keep within the budget.</summary>
        public long effectChecksDeferred { get; private set; }

        /// <summary>Objects the game created that were never looked at, because more were waiting than can wait.</summary>
        public int effectObjectsSkipped { get; private set; }

        /// <summary>
        /// An entity's components are looked for again when its hierarchy changed, and now and then in any case
        /// (a component added to an object that was already there, an animator given another controller).
        /// </summary>
        const int EffectChecksBetweenSearches = 8;
        const int MaxWaitingObjects = 4096;

        struct WaitingObject
        {
            public GameObject go;
            public ulong networkId;
            public bool identity;
        }

        /// <summary>Objects with something to ask about, asked in turn.</summary>
        readonly List<EntityEffects> _effects = new List<EntityEffects>();
        readonly Stack<EntityEffects> _effectsPool = new Stack<EntityEffects>();
        readonly EntityEffects _noEffects = new EntityEffects(), _previousEffects = new EntityEffects();
        /// <summary>
        /// Objects that are not entities, by GameObject; and every followed component's object, by component.
        /// </summary>
        readonly Dictionary<long, EntityEffects> _effectRoots = new Dictionary<long, EntityEffects>(256);
        readonly Dictionary<long, EntityEffects> _effectComponents = new Dictionary<long, EntityEffects>(1024);
        /// <summary>Objects something was just called on: asked first, on the tick of the call.</summary>
        List<EntityEffects> _hotEffects = new List<EntityEffects>(), _hotNow = new List<EntityEffects>();
        readonly List<WaitingObject> _waitingObjects = new List<WaitingObject>();
        int _waitingHead;
        int _effectCursor;
        int _effectEpoch;
        int _effectChunk;
        int _nextEffectId, _nextEffectObject;
        bool _effectsActive;
        StreamRows _animationRows, _particleRows, _audioRows, _animatorCallRows;

        const long KeySeed = 1469598103934665603L, KeyPrime = 1099511628211L;
        const string AnimationStream = "effect.animation", ParticlesStream = "effect.particles",
            AudioStream = "effect.audio", AnimatorCallStream = "effect.calls";

        internal bool effectsActive => _effectsActive;
        static bool _effectsRehearsed;

        /// <summary>
        /// A recording starts on this machine. The effect streams are declared now, so that the first animation,
        /// particle system or sound of a match costs what any later one does.
        /// </summary>
        void BeginEffects()
        {
            ClearEffects();
            effectChecksDeferred = 0;
            effectObjectsSkipped = 0;
            if (!recordEffects)
                return;
#if UNITY_ANIMATION
            _animationRows = EffectStream(typeof(RecordedAnimation), "animation");
            _animatorCallRows = EffectStream(typeof(RecordedAnimatorCall), "calls");
#endif
#if PURRNET_PARTICLES
            _particleRows = EffectStream(typeof(RecordedParticles), "particles");
#endif
#if PURRNET_AUDIO
            _audioRows = EffectStream(typeof(RecordedAudio), "audio");
#endif
            RehearseEffects();
            _effectsActive = true;
            RecordedEffects.Add(this);
        }

        /// <summary>A client was offered a part: its effects go there, as samples of the player.</summary>
        internal void BeginClientEffects()
        {
            ClearEffects();
            effectChecksDeferred = 0;
            effectObjectsSkipped = 0;
            if (!recordEffects)
                return;
            _effectsActive = true;
            RecordedEffects.Add(this);
        }

        internal void EndEffects()
        {
            RecordedEffects.Remove(this);
            ClearEffects();
        }

        internal void EffectsFailed(Exception e)
        {
            EndEffects();
            AddError("effects", e.GetType().Name + ": " + e.Message);
        }

        void ClearEffects()
        {
            _effectsActive = false;
            foreach (var owner in _effects)
            {
                if (owner.entity != null)
                    owner.entity.effects = null;
                else
                    _effectRoots.Remove(owner.rootId);
                owner.Clear();
                _effectsPool.Push(owner);
            }
            foreach (var owner in _effectRoots.Values)
            {
                owner.Clear();
                _effectsPool.Push(owner);
            }
            _effects.Clear();
            _effectRoots.Clear();
            _effectComponents.Clear();
            _hotEffects.Clear();
            _hotNow.Clear();
            _waitingObjects.Clear();
            _waitingHead = 0;
            _effectCursor = 0;
            _effectChunk = _chunks;
            _nextEffectId = 1;
            _nextEffectObject = 1;
            _animationRows = _particleRows = _audioRows = _animatorCallRows = null;
        }

        StreamRows EffectStream(Type type, string name)
        {
            var rows = CreateStream(type, new RecordedStreamInfo { kind = RecordedStreamKind.Effect, sample = name });
            _types.GetManagedWriters(rows.layout);
            return rows;
        }

        /// <summary>
        /// A row of an effect stream: into the recording's own stream on the server or host, and on a client into
        /// its part, as a sample of the player.
        /// </summary>
        void EffectRow<T>(StreamRows rows, string stream, ulong tick, ref T row) where T : struct
        {
            if (rows != null)
                AppendRow(rows, tick, ref row);
            else
                WriteSample(stream, row);
        }

        /// <summary>The file effects are written to: the recording, or the part a client was offered, opened if needed.</summary>
        bool EffectsFile()
        {
            if (_file != null)
                return true;
            if (!_partOffered || !_context.isReady)
                return false;
            OpenPart(PartTick(_context.viewTick));
            return _file != null;
        }

        ulong EffectTick() => _file != null && !isPart ? SampleTick() : PartTick(_context.viewTick);

        /// <summary>
        /// Where methods are compiled on their first call (Mono), that call is slow, and it would land in the middle
        /// of a match. So everything the check uses runs once here: the check itself on nothing, a row of each
        /// stream that is taken back while the chunk is still empty (none of it reaches the file), and the engine's
        /// methods on an inactive object that is gone again before anything else can see it.
        /// </summary>
        [Conditional("ENABLE_MONO")]
        void RehearseEffects()
        {
            if (_effectsRehearsed)
                return;
            _effectsRehearsed = true;
            CheckEffects(_noEffects, _chunkFirstTick);
            var log = _job.log;
            bool empty = _slots.Count == 0 && log.bytes.length == 0;
            if (empty)
            {
#if UNITY_ANIMATION
                var animation = new RecordedAnimation { clip = string.Empty };
                AppendRow(_animationRows, _chunkFirstTick, ref animation);
                var call = new RecordedAnimatorCall { name = string.Empty };
                AppendRow(_animatorCallRows, _chunkFirstTick, ref call);
#endif
#if PURRNET_PARTICLES
                var particles = default(RecordedParticles);
                AppendRow(_particleRows, _chunkFirstTick, ref particles);
#endif
#if PURRNET_AUDIO
                var audio = new RecordedAudio { clip = string.Empty };
                AppendRow(_audioRows, _chunkFirstTick, ref audio);
#endif
            }

            var probe = new GameObject("PurrNet recorder rehearsal") { hideFlags = HideFlags.HideAndDontSave };
            probe.SetActive(false);
#if UNITY_ANIMATION
            var graph = default(UnityEngine.Playables.PlayableGraph);
#endif
            try
            {
#if UNITY_ANIMATION
                var animator = probe.AddComponent<Animator>();
                graph = UnityEngine.Playables.PlayableGraph.Create();
                UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "rehearsal", animator);
                _ = LayerCount(animator) + animator.layerCount + animator.speed;
                _ = animator.hasBoundPlayables && animator.IsInTransition(0);
                var info = animator.GetCurrentAnimatorStateInfo(0);
                info = animator.GetNextAnimatorStateInfo(0);
                _ = info.fullPathHash + info.shortNameHash + info.normalizedTime + info.speed * info.speedMultiplier + info.length;
                _ = info.loop;
                animator.GetCurrentAnimatorClipInfo(0, _clipInfos);
                animator.GetNextAnimatorClipInfo(0, _clipInfos);
                var clipInfo = default(AnimatorClipInfo);
                _ = clipInfo.weight;
                _ = clipInfo.clip;
                _clipInfos.Clear();
#endif
#if PURRNET_PARTICLES
                var system = probe.AddComponent<ParticleSystem>();
                var main = system.main;
                _ = system.isPlaying && system.isEmitting && main.loop;
                _ = system.time + main.duration;
#endif
#if PURRNET_AUDIO
                var source = probe.AddComponent<AudioSource>();
                _ = source.isPlaying && source.loop && source.clip;
                _ = source.volume + source.pitch + source.spatialBlend;
#endif
                if (empty)
                {
                    int meta = _meta.length;
                    var owner = NewRoot(probe, RecordingObjectId.Of(probe), false, 0);
                    SearchEffects(owner, null, probe, false, _chunkFirstTick);
                    CheckEffects(owner, _chunkFirstTick);
                    ReleaseRoot(owner, _chunkFirstTick);
                    _meta.length = meta;
                    _nextEffectId = _nextEffectObject = 1;
                }
            }
            finally
            {
#if UNITY_ANIMATION
                if (graph.IsValid())
                    graph.Destroy();
#endif
                UnityEngine.Object.DestroyImmediate(probe);
                if (empty)
                {
                    foreach (var rows in _slots)
                    {
                        rows.slot = -1;
                        rows.rowCount = 0;
                        Array.Clear(rows.lastObject, 0, rows.lastObject.Length);
                    }
                    _slots.Clear();
                    log.Clear();
                }
            }
        }

        EntityEffects NewOwner() => _effectsPool.Count > 0 ? _effectsPool.Pop() : new EntityEffects();

        EntityEffects NewRoot(GameObject go, long id, bool identity, ulong networkId)
        {
            var owner = NewOwner();
            owner.root = go;
            owner.rootTransform = go.transform;
            owner.rootId = id;
            owner.objectId = _nextEffectObject++;
            owner.hasNetworkId = identity;
            owner.networkId = networkId;
            owner.hierarchy = owner.rootTransform.hierarchyCount;
            owner.epoch = _effectEpoch;
            _effectRoots[id] = owner;
            return owner;
        }

        void Unlist(EntityEffects owner)
        {
            if (owner.index < 0)
                return;
            int last = _effects.Count - 1;
            var moved = _effects[last];
            _effects[owner.index] = moved;
            moved.index = owner.index;
            _effects.RemoveAt(last);
            owner.index = -1;
        }

        /// <summary>The components in <paramref name="held"/> are no longer followed by <paramref name="owner"/>.</summary>
        void Forget(EntityEffects held, EntityEffects owner)
        {
            for (int i = 0; i < held.layerCount; i++)
                ForgetComponent(held.layers[i].instance, owner);
            for (int i = 0; i < held.particleCount; i++)
                ForgetComponent(held.particles[i].instance, owner);
            for (int i = 0; i < held.audioCount; i++)
                ForgetComponent(held.audio[i].instance, owner);
        }

        void ForgetComponent(long instance, EntityEffects owner)
        {
            if (_effectComponents.TryGetValue(instance, out var current) && ReferenceEquals(current, owner))
                _effectComponents.Remove(instance);
        }

        void ReleaseEffects(RecordedEntity entity)
        {
            var owner = entity.effects;
            if (owner == null)
                return;
            entity.effects = null;
            Forget(owner, owner);
            Unlist(owner);
            owner.Clear();
            _effectsPool.Push(owner);
        }

        /// <summary>An object that is not an entity is gone: its record says from when.</summary>
        void ReleaseRoot(EntityEffects owner, ulong tick)
        {
            if (owner.hasKey && _file != null)
                WriteMeta(RecordKind.Effects, _json.Clear().BeginObject()
                    .Prop("object", owner.objectId).Prop("tick", tick).Prop("gone", true).EndObject());
            _effectRoots.Remove(owner.rootId);
            Forget(owner, owner);
            Unlist(owner);
            owner.Clear();
            _effectsPool.Push(owner);
        }

        /// <summary>The nearest object on or above <paramref name="t"/> that effects are followed for.</summary>
        EntityEffects NearestOwner(Transform t, out RecordedEntity entity)
        {
            bool entities = _entitiesByGameObject.Count > 0, roots = _effectRoots.Count > 0;
            for (; t; t = t.parent)
            {
                long id = RecordingObjectId.Of(t.gameObject);
                if (roots && _effectRoots.TryGetValue(id, out var owner))
                {
                    entity = null;
                    return owner;
                }
                if (entities && _entitiesByGameObject.TryGetValue(id, out entity))
                    return entity.effects;
            }
            entity = null;
            return null;
        }

        static class Found<T>
        {
            public static readonly List<T> all = new List<T>(8), scratch = new List<T>(8);
        }

        /// <summary>
        /// The components of one type that belong to an object: those of an entity or followed object nested in
        /// it are that one's, an entity's detached graphics count, and a component that another object already
        /// follows stays with it.
        /// </summary>
        List<T> FindEffects<T>(EntityEffects owner, RecordedEntity entity, GameObject root, bool nested) where T : Component
        {
            var all = Found<T>.all;
            root.GetComponentsInChildren(true, all);
            if (entity != null)
            {
                if (nested)
                {
                    for (int i = all.Count - 1; i >= 0; i--)
                        if (!OwnedBy(all[i], root))
                            all.RemoveAt(i);
                }
                var detached = entity.detachedGraphics;
                for (int i = 0; i < detached.Count; i++)
                {
                    var graphics = detached[i];
                    if (!graphics || graphics.IsChildOf(entity.root))
                        continue;
                    var scratch = Found<T>.scratch;
                    graphics.GetComponentsInChildren(true, scratch);
                    all.AddRange(scratch);
                    scratch.Clear();
                }
            }
            else
            {
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var t = all[i].transform;
                    if (!ReferenceEquals(t, owner.rootTransform) && !ReferenceEquals(NearestOwner(t, out _), owner))
                        all.RemoveAt(i);
                }
            }

            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (_effectComponents.TryGetValue(RecordingObjectId.Of(all[i]), out var other) && !ReferenceEquals(other, owner))
                    all.RemoveAt(i);
            }
            return all;
        }

        /// <summary>
        /// Looks for the entity's animators, particle systems and audio sources when it enters the recording and at
        /// its periodic checks: then only when its hierarchy changed, and now and then in any case.
        /// </summary>
        void RefreshEntityEffects(RecordedEntity entity, bool nested, ulong tick)
        {
            if (!_effectsActive)
                return;

            int hierarchy = entity.root.hierarchyCount;
            if (entity.effectsChecks != 0 && hierarchy == entity.effectsHierarchy)
            {
                if (++entity.effectsChecks <= EffectChecksBetweenSearches)
                    return;
            }
            entity.effectsHierarchy = hierarchy;
            entity.effectsChecks = 1;
            SearchEffects(entity.effects, entity, entity.gameObject, nested, tick);
        }

        /// <summary>
        /// Finds an object's animators, particle systems and audio sources, and writes an EFFECTS record naming
        /// them when the set changed. A component that was there before keeps its id and what it was last seen
        /// doing, so a changed set writes no rows of its own.
        /// </summary>
        void SearchEffects(EntityEffects owner, RecordedEntity entity, GameObject root, bool nested, ulong tick)
        {
            long key = KeySeed;
            int found = 0;
#if UNITY_ANIMATION
            var animators = FindEffects<Animator>(owner, entity, root, nested);
            for (int i = 0; i < animators.Count; i++)
            {
                var controller = animators[i].runtimeAnimatorController;
                key = unchecked((key ^ RecordingObjectId.Of(animators[i])) * KeyPrime);
                key = unchecked((key ^ (controller ? RecordingObjectId.Of(controller) : 0)) * KeyPrime);
                key = unchecked((key ^ LayerCount(animators[i])) * KeyPrime);
            }
            found += animators.Count;
#endif
#if PURRNET_PARTICLES
            var systems = FindEffects<ParticleSystem>(owner, entity, root, nested);
            for (int i = 0; i < systems.Count; i++)
                key = unchecked((key ^ RecordingObjectId.Of(systems[i])) * KeyPrime);
            found += systems.Count;
#endif
#if PURRNET_AUDIO
            var sources = FindEffects<AudioSource>(owner, entity, root, nested);
            for (int i = 0; i < sources.Count; i++)
                key = unchecked((key ^ RecordingObjectId.Of(sources[i])) * KeyPrime);
            found += sources.Count;
#endif
            if (owner == null || !owner.hasKey ? found == 0 : owner.key == key)
                return;
            if (!EffectsFile())
                return;

            if (owner == null)
            {
                owner = NewOwner();
                owner.entity = entity;
                owner.root = root;
                owner.rootTransform = entity.root;
                owner.epoch = _effectEpoch;
                entity.effects = owner;
            }

            var previous = _previousEffects;
            (previous.layers, owner.layers) = (owner.layers, previous.layers);
            (previous.particles, owner.particles) = (owner.particles, previous.particles);
            (previous.audio, owner.audio) = (owner.audio, previous.audio);
            previous.layerCount = owner.layerCount;
            previous.particleCount = owner.particleCount;
            previous.audioCount = owner.audioCount;
            owner.layerCount = owner.particleCount = owner.audioCount = 0;
            Forget(previous, owner);
            owner.key = key;
            owner.hasKey = true;

            var json = _json.Clear().BeginObject();
            if (entity != null)
                json.Prop("entity", entity.id).Prop("tick", tick);
            else
            {
                json.Prop("object", owner.objectId).Prop("tick", tick).Prop("name", Name(root));
                if (owner.hasNetworkId)
                    json.Prop("networkId", owner.networkId);
                else
                {
                    var position = owner.rootTransform.position;
                    json.Floats("position", position.x, position.y, position.z);
                }
            }
#if UNITY_ANIMATION
            json.Key("animators").BeginArray();
            for (int i = 0; i < animators.Count; i++)
            {
                var animator = animators[i];
                long instance = RecordingObjectId.Of(animator);
                var controller = animator.runtimeAnimatorController;
                int layers = LayerCount(animator);
                int id = previous.AnimatorId(instance);
                if (id == 0)
                    id = _nextEffectId++;
                json.BeginObject().Prop("id", id).Prop("name", Name(animator.gameObject)).Key("controller");
                if (controller) json.Value(Name(controller)); else json.Null();
                json.Prop("layers", layers).EndObject();
                for (int layer = 0; layer < Mathf.Max(1, layers); layer++)
                {
                    ref var tracked = ref owner.AddLayer();
                    tracked = default;
                    for (int p = 0; p < previous.layerCount; p++)
                        if (previous.layers[p].instance == instance && previous.layers[p].layer == layer)
                            tracked = previous.layers[p];
                    tracked.animator = animator;
                    tracked.instance = instance;
                    tracked.id = id;
                    tracked.layer = layer;
                    tracked.layers = layers;
                }
                _effectComponents[instance] = owner;
            }
            json.EndArray();
            animators.Clear();
#endif
#if PURRNET_PARTICLES
            json.Key("particles").BeginArray();
            for (int i = 0; i < systems.Count; i++)
            {
                var system = systems[i];
                long instance = RecordingObjectId.Of(system);
                var main = system.main;
                ref var tracked = ref owner.AddParticles();
                int at = previous.ParticlesAt(instance);
                tracked = at >= 0 ? previous.particles[at] : default;
                if (tracked.id == 0)
                    tracked.id = _nextEffectId++;
                tracked.system = system;
                tracked.instance = instance;
                tracked.loop = main.loop;
                json.BeginObject().Prop("id", tracked.id).Prop("name", Name(system.gameObject))
                    .Prop("loop", tracked.loop).Prop("duration", main.duration).EndObject();
                _effectComponents[instance] = owner;
            }
            json.EndArray();
            systems.Clear();
#endif
#if PURRNET_AUDIO
            json.Key("audio").BeginArray();
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                long instance = RecordingObjectId.Of(source);
                ref var tracked = ref owner.AddAudio();
                int at = previous.AudioAt(instance);
                tracked = at >= 0 ? previous.audio[at] : default;
                if (tracked.id == 0)
                    tracked.id = _nextEffectId++;
                tracked.source = source;
                tracked.instance = instance;
                var clip = source.clip;
                json.BeginObject().Prop("id", tracked.id).Prop("name", Name(source.gameObject)).Key("clip");
                if (clip) json.Value(Name(clip)); else json.Null();
                json.Prop("spatial", source.spatialBlend).EndObject();
                _effectComponents[instance] = owner;
            }
            json.EndArray();
            sources.Clear();
#endif
            WriteMeta(RecordKind.Effects, json.EndObject());
            previous.ClearComponents();

            if (found > 0)
            {
                if (owner.index < 0)
                {
                    owner.index = _effects.Count;
                    _effects.Add(owner);
                }
            }
            else if (entity != null)
                ReleaseEffects(entity);
            else
                Unlist(owner);
        }

        /// <summary>A client's network identity: its effects are followed under its network id.</summary>
        internal void TrackIdentityObject(GameObject go, ulong networkId)
        {
            if (_effectsActive)
                Wait(new WaitingObject { go = go, networkId = networkId, identity = true });
        }

        internal void UntrackIdentityObject(GameObject go)
        {
            if (_effectsActive && go && _effectRoots.TryGetValue(RecordingObjectId.Of(go), out var owner) && owner.hasNetworkId)
                ReleaseRoot(owner, EffectTick());
        }

        /// <summary>The game created an object. It is looked at on the next tick, within the budget.</summary>
        internal void ObjectCreated(GameObject go)
        {
            if (_effectsActive)
                Wait(new WaitingObject { go = go });
        }

        /// <summary>The game destroys an object: when it is one only this machine has, its effects end here.</summary>
        internal void ObjectDestroyed(GameObject go)
        {
            if (_effectsActive && _effectRoots.Count > 0 && _effectRoots.TryGetValue(RecordingObjectId.Of(go), out var owner) && owner.loose)
                ReleaseRoot(owner, EffectTick());
        }

        void Wait(WaitingObject waiting)
        {
            if (_waitingObjects.Count - _waitingHead >= MaxWaitingObjects)
            {
                effectObjectsSkipped++;
                return;
            }
            _waitingObjects.Add(waiting);
        }

        void Consider(in WaitingObject waiting, ulong tick)
        {
            var go = waiting.go;
            long id = RecordingObjectId.Of(go);
            if (waiting.identity)
            {
                if (!_effectRoots.ContainsKey(id))
                    SearchEffects(NewRoot(go, id, true, waiting.networkId), null, go, false, tick);
                return;
            }

            var nearest = NearestOwner(go.transform, out var entity);
            if (entity != null)
                SearchEffects(entity.effects, entity, entity.gameObject, HasNestedEntities(entity), tick);
            else if (nearest != null)
                SearchEffects(nearest, null, nearest.root, false, tick);
            else
                TryLoose(go, id, tick);
        }

        /// <summary>
        /// An object that only this machine has (an effect the game instantiated, a sound source in the scene) is
        /// followed when it has something to follow, on a machine that plays. What is part of a network identity
        /// is not such an object: it is followed with that identity, which on a client starts here and on the
        /// recording's own machine when its entity enters the recording.
        /// </summary>
        void TryLoose(GameObject go, long id, ulong tick)
        {
            if (!_context.isClient)
                return;
            var identity = go.GetComponentInParent<NetworkIdentity>(true);
            if (identity)
            {
                var spawned = identity.gameObject;
                long spawnedId = RecordingObjectId.Of(spawned);
                if ((_file == null || isPart) && identity.isSpawned && identity.id.HasValue && !_effectRoots.ContainsKey(spawnedId))
                    SearchEffects(NewRoot(spawned, spawnedId, true, identity.id.Value.id.value), null, spawned, false, tick);
                return;
            }
            var owner = NewRoot(go, id, false, 0);
            SearchEffects(owner, null, go, false, tick);
            if (owner.hasKey)
                return;
            _effectRoots.Remove(id);
            owner.Clear();
            _effectsPool.Push(owner);
        }

        /// <summary>
        /// The object that follows a component something was just called on. A component nobody follows yet is
        /// looked for: in the nearest followed object, or it makes its own object one.
        /// </summary>
        EntityEffects OwnerOf(Component component, long instance)
        {
            if (_effectComponents.TryGetValue(instance, out var owner))
                return owner;
            ulong tick = EffectTick();
            var nearest = NearestOwner(component.transform, out var entity);
            if (entity != null)
                SearchEffects(entity.effects, entity, entity.gameObject, HasNestedEntities(entity), tick);
            else if (nearest != null)
                SearchEffects(nearest, null, nearest.root, false, tick);
            else
            {
                var go = component.gameObject;
                TryLoose(go, RecordingObjectId.Of(go), tick);
            }
            return _effectComponents.TryGetValue(instance, out owner) ? owner : null;
        }

        void MarkHot(EntityEffects owner)
        {
            if (owner.hot || owner.index < 0)
                return;
            owner.hot = true;
            _hotEffects.Add(owner);
        }

        /// <summary>
        /// Writes what animators, particle systems and audio sources started doing since they were last asked: a row
        /// per change, nothing while they carry on. Objects the game created since the last tick are looked at
        /// first, then the ones something was called on, then the rest in turn. All of it stops at
        /// <see cref="effectsBudgetMs"/> and continues from there on the next tick; the first two get no more than
        /// half of it, so that a busy game cannot keep the rest from ever being asked.
        /// </summary>
        public void RecordEffects(ulong tick)
        {
            if (!_effectsActive || (_effects.Count == 0 && _waitingObjects.Count == 0))
                return;

            _stats.Begin(_statEffects);
            try
            {
                CheckEffectsInTurn(tick);
            }
            catch (Exception e)
            {
                EffectsFailed(e);
            }
            _stats.End(_statEffects);
        }

        void CheckEffectsInTurn(ulong tick)
        {
            if (_file == null || isPart)
                tick = PartTick(_context.viewTick);
            if (_effectChunk != _chunks)
            {
                _effectChunk = _chunks;
                _effectEpoch++;
            }
            long budget = (long)(effectsBudgetMs * 0.001 * Stopwatch.Frequency);
            long deadline = Stopwatch.GetTimestamp() + budget, half = deadline - budget / 2;
            bool inTime = true;

            while (_waitingHead < _waitingObjects.Count && inTime)
            {
                var waiting = _waitingObjects[_waitingHead++];
                if (waiting.go)
                    Consider(waiting, tick);
                inTime = Stopwatch.GetTimestamp() <= half;
            }
            if (_waitingHead == _waitingObjects.Count)
            {
                _waitingObjects.Clear();
                _waitingHead = 0;
            }

            (_hotNow, _hotEffects) = (_hotEffects, _hotNow);
            int hot = 0;
            for (; hot < _hotNow.Count && inTime; hot++)
            {
                var owner = _hotNow[hot];
                owner.hot = false;
                if (owner.index >= 0)
                    CheckEffects(owner, tick);
                if ((hot & 3) == 3)
                    inTime = Stopwatch.GetTimestamp() <= half;
            }
            for (; hot < _hotNow.Count; hot++)
                _hotEffects.Add(_hotNow[hot]);
            _hotNow.Clear();

            int count = _effects.Count, done = 0;
            while (done < count && _effects.Count > 0)
            {
                if (_effectCursor >= _effects.Count)
                    _effectCursor = 0;
                var owner = _effects[_effectCursor];
                CheckEffects(owner, tick);
                if (_effectCursor < _effects.Count && ReferenceEquals(_effects[_effectCursor], owner))
                    _effectCursor++;
                done++;
                if ((done & 3) == 0 && Stopwatch.GetTimestamp() > deadline)
                    break;
            }
            effectChecksDeferred += Math.Max(0, count - done);
        }

#if UNITY_ANIMATION
        readonly List<AnimatorClipInfo> _clipInfos = new List<AnimatorClipInfo>(4);

        /// <summary>
        /// An animator that never had a controller logs a warning each time it is asked for its layers or its state;
        /// hasBoundPlayables is what tells it apart without one. An inactive animator has no layers until it is active.
        /// </summary>
        static int LayerCount(Animator animator) =>
            animator.isActiveAndEnabled && animator.hasBoundPlayables ? Mathf.Min(animator.layerCount, 255) : 0;
#endif

        void CheckEffects(EntityEffects owner, ulong tick)
        {
            int entity = 0;
            bool loose = false;
            if (owner.entity != null)
                entity = owner.entity.id;
            else if (!ReferenceEquals(owner, _noEffects))
            {
                if (!owner.root)
                {
                    ReleaseRoot(owner, tick);
                    return;
                }
                int hierarchy = owner.rootTransform.hierarchyCount;
                if (hierarchy != owner.hierarchy)
                {
                    owner.hierarchy = hierarchy;
                    SearchEffects(owner, null, owner.root, false, tick);
                }
                loose = !owner.hasNetworkId;
            }

            bool restate = owner.epoch != _effectEpoch;
            owner.epoch = _effectEpoch;
            bool busy = false;
#if UNITY_ANIMATION
            var layers = owner.layers;
            for (int i = 0; i < owner.layerCount; i++)
            {
                ref var t = ref layers[i];
                var animator = t.animator;
                int layerCount = animator ? LayerCount(animator) : 0;
                if (t.layer == 0 && layerCount != 0 && layerCount != t.layers)
                {
                    if (owner.entity != null)
                        owner.entity.effectsChecks = 0;
                    else
                        owner.hierarchy = -1;
                }
                if (t.layer >= layerCount)
                {
                    bool was = t.known && (t.flags & RecordedAnimation.Stopped) != 0;
                    if (was && !restate)
                        continue;
                    t.known = true;
                    t.flags = RecordedAnimation.Stopped;
                    t.state = t.from = 0;
                    t.time = 0;
                    var stopped = new RecordedAnimation
                    {
                        entity = entity, animator = t.id, layer = (byte)t.layer,
                        flags = was ? (byte)(RecordedAnimation.Stopped | RecordedAnimation.Continued) : RecordedAnimation.Stopped
                    };
                    EffectRow(_animationRows, AnimationStream, tick, ref stopped);
                    continue;
                }

                var info = animator.GetCurrentAnimatorStateInfo(t.layer);
                int from = 0;
                bool transition = animator.IsInTransition(t.layer);
                if (transition)
                {
                    from = info.fullPathHash;
                    info = animator.GetNextAnimatorStateInfo(t.layer);
                }
                int state = info.fullPathHash;
                float time = info.normalizedTime;
                bool changed = !t.known || state != t.state || from != t.from || (t.flags & RecordedAnimation.Stopped) != 0;
                float stateSpeed = info.speed * info.speedMultiplier;
                if (!changed && time < t.time && stateSpeed * animator.speed >= 0)
                    changed = true;
                t.time = time;
                busy |= transition || (!info.loop && time < 1f);
                if (!changed && !restate)
                    continue;

                if (changed && owner.entity != null)
                    owner.entity.checkWait = 0;
                t.known = true;
                t.state = state;
                t.from = from;
                t.flags = (byte)((transition ? RecordedAnimation.Transition : 0) | (info.loop ? RecordedAnimation.Loop : 0));
                if (transition)
                    animator.GetNextAnimatorClipInfo(t.layer, _clipInfos);
                else
                    animator.GetCurrentAnimatorClipInfo(t.layer, _clipInfos);
                AnimationClip clip = null;
                float weight = float.NegativeInfinity;
                for (int c = 0; c < _clipInfos.Count; c++)
                {
                    if (_clipInfos[c].weight > weight)
                    {
                        weight = _clipInfos[c].weight;
                        clip = _clipInfos[c].clip;
                    }
                }
                _clipInfos.Clear();
                var row = new RecordedAnimation
                {
                    entity = entity, animator = t.id, layer = (byte)t.layer,
                    flags = changed ? t.flags : (byte)(t.flags | RecordedAnimation.Continued), state = state, from = from,
                    shortName = info.shortNameHash, time = time, length = info.length,
                    speed = stateSpeed * animator.speed, clip = clip ? Name(clip) : null
                };
                EffectRow(_animationRows, AnimationStream, tick, ref row);
            }
#endif
#if PURRNET_PARTICLES
            var particles = owner.particles;
            for (int i = 0; i < owner.particleCount; i++)
            {
                ref var t = ref particles[i];
                var system = t.system;
                byte flags = 0;
                float time = 0;
                if (system && system.isPlaying)
                {
                    flags = system.isEmitting
                        ? (byte)(RecordedParticles.Playing | RecordedParticles.Emitting)
                        : RecordedParticles.Playing;
                    time = system.time;
                }
                bool changed = flags != t.flags || (flags != 0 && !t.loop && time < t.time);
                t.time = time;
                busy |= flags != 0 && !t.loop;
                if (!changed && (!restate || flags == 0))
                    continue;
                t.flags = flags;
                var row = new RecordedParticles
                {
                    entity = entity, system = t.id, time = time,
                    flags = changed ? flags : (byte)(flags | RecordedParticles.Continued)
                };
                if (loose && system)
                    row.position = system.transform.position;
                EffectRow(_particleRows, ParticlesStream, tick, ref row);
            }
#endif
#if PURRNET_AUDIO
            var audio = owner.audio;
            for (int i = 0; i < owner.audioCount; i++)
            {
                ref var t = ref audio[i];
                var source = t.source;
                byte flags = source && source.isPlaying ? RecordedAudio.Playing : (byte)0;
                bool changed = flags != t.flags;
                if (changed && flags != 0)
                    t.loop = source.loop;
                busy |= flags != 0 && !t.loop;
                if (!changed && (!restate || flags == 0))
                    continue;
                t.flags = flags;
                var row = new RecordedAudio
                {
                    entity = entity, source = t.id,
                    flags = changed ? flags : (byte)(flags | RecordedAudio.Continued)
                };
                if (flags != 0)
                {
                    var clip = source.clip;
                    if (source.loop)
                        row.flags |= RecordedAudio.Loop;
                    row.volume = source.volume;
                    row.pitch = source.pitch;
                    row.clip = clip ? Name(clip) : null;
                    if (loose)
                        row.position = source.transform.position;
                }
                EffectRow(_audioRows, AudioStream, tick, ref row);
            }
#endif
            if (busy)
                MarkHot(owner);
        }

#if PURRNET_AUDIO
        /// <summary>A source was played, or played a clip of the caller's choosing (<paramref name="clip"/>) once.</summary>
        internal void SoundPlayed(AudioSource source, AudioClip clip, float volumeScale, byte flags)
        {
            if (!source.isPlaying)
                return;
            long instance = RecordingObjectId.Of(source);
            var owner = OwnerOf(source, instance);
            int at = owner != null ? owner.AudioAt(instance) : -1;
            if (at < 0)
                return;
            ref var t = ref owner.audio[at];
            t.flags = RecordedAudio.Playing;
            t.loop = flags == 0 && source.loop;
            MarkHot(owner);
            if (!clip)
                clip = source.clip;
            var row = new RecordedAudio
            {
                entity = owner.entity != null ? owner.entity.id : 0, source = t.id,
                flags = (byte)(RecordedAudio.Playing | flags), volume = source.volume * volumeScale, pitch = source.pitch,
                clip = clip ? Name(clip) : null
            };
            if (flags == 0 && source.loop)
                row.flags |= RecordedAudio.Loop;
            if (owner.loose)
                row.position = source.transform.position;
            EffectRow(_audioRows, AudioStream, EffectTick(), ref row);
        }

        internal void SoundStopped(AudioSource source)
        {
            long instance = RecordingObjectId.Of(source);
            if (!_effectComponents.TryGetValue(instance, out var owner))
                return;
            int at = owner.AudioAt(instance);
            if (at < 0 || owner.audio[at].flags == 0)
                return;
            owner.audio[at].flags = 0;
            var row = new RecordedAudio { entity = owner.entity != null ? owner.entity.id : 0, source = owner.audio[at].id };
            EffectRow(_audioRows, AudioStream, EffectTick(), ref row);
        }

        /// <summary>A clip played at a point in the world, from no source of the game's.</summary>
        internal void SoundAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            if (!_context.isClient || !clip || !EffectsFile())
                return;
            var row = new RecordedAudio
            {
                flags = RecordedAudio.Playing | RecordedAudio.OneShot | RecordedAudio.AtPoint, volume = volume, pitch = 1,
                position = position, clip = Name(clip)
            };
            EffectRow(_audioRows, AudioStream, EffectTick(), ref row);
        }
#endif

#if PURRNET_PARTICLES
        internal void ParticlesPlayed(ParticleSystem system)
        {
            if (!system.isPlaying)
                return;
            long instance = RecordingObjectId.Of(system);
            var owner = OwnerOf(system, instance);
            int at = owner != null ? owner.ParticlesAt(instance) : -1;
            if (at < 0)
                return;
            ref var t = ref owner.particles[at];
            t.flags = RecordedParticles.Playing | RecordedParticles.Emitting;
            t.time = system.time;
            var row = new RecordedParticles
            {
                entity = owner.entity != null ? owner.entity.id : 0, system = t.id, flags = t.flags, time = t.time
            };
            if (owner.loose)
                row.position = system.transform.position;
            EffectRow(_particleRows, ParticlesStream, EffectTick(), ref row);
            MarkHot(owner);
        }

        internal void ParticlesStopped(ParticleSystem system)
        {
            if (_effectComponents.TryGetValue(RecordingObjectId.Of(system), out var owner))
                MarkHot(owner);
        }

        internal void ParticlesEmitted(ParticleSystem system, int count)
        {
            long instance = RecordingObjectId.Of(system);
            var owner = OwnerOf(system, instance);
            int at = owner != null ? owner.ParticlesAt(instance) : -1;
            if (at < 0)
                return;
            ref var t = ref owner.particles[at];
            var row = new RecordedParticles
            {
                entity = owner.entity != null ? owner.entity.id : 0, system = t.id,
                flags = (byte)(t.flags | RecordedParticles.Emitted), time = system.time, count = count
            };
            if (owner.loose)
                row.position = system.transform.position;
            EffectRow(_particleRows, ParticlesStream, EffectTick(), ref row);
        }
#endif

#if UNITY_ANIMATION
        /// <summary>The game told an animator to play or blend into a state, or set a trigger, by name or by hash.</summary>
        internal void AnimatorCalled(Animator animator, byte kind, int hash, string name)
        {
            long instance = RecordingObjectId.Of(animator);
            var owner = OwnerOf(animator, instance);
            int id = owner != null ? owner.AnimatorId(instance) : 0;
            if (id == 0)
                return;
            var row = new RecordedAnimatorCall
            {
                entity = owner.entity != null ? owner.entity.id : 0, animator = id, kind = kind, hash = hash, name = name
            };
            EffectRow(_animatorCallRows, AnimatorCallStream, EffectTick(), ref row);
            MarkHot(owner);
            if (owner.entity != null)
                owner.entity.checkWait = 0;
        }

        /// <summary>The game set one of an animator's parameters: what that leads to is looked for on this tick.</summary>
        internal void AnimatorTouched(Animator animator)
        {
            if (_effectComponents.TryGetValue(RecordingObjectId.Of(animator), out var owner))
                MarkHot(owner);
        }
#endif
    }
}

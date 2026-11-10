using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// Something that puts entities into a <see cref="RecordingSession"/> (predicted objects, network identities). The
    /// session asks its sources which scene objects belong to an entity, so their colliders and renderers are recorded
    /// with that entity and not as part of the static world. Several sources can share one entity (a GameObject with
    /// both a network identity and predicted components): the first creates it, the others attach to it.
    /// </summary>
    public abstract class RecordingSource
    {
        /// <summary>Written in ATTACH and DETACH records ("purrnet", "purrdiction").</summary>
        public abstract string name { get; }

        /// <summary>Whether <paramref name="go"/> is the root of one of this source's entities.</summary>
        public abstract bool IsEntityObject(GameObject go);

        /// <summary>The nearest object on or above <paramref name="t"/> that is one of this source's entities.</summary>
        public abstract Transform NearestEntity(Transform t, bool includeInactive);

        /// <summary>Whether entities of this source are nested inside <paramref name="entity"/>.</summary>
        public abstract bool HasNestedEntities(RecordedEntity entity);

        /// <summary>Graphics that show <paramref name="entity"/> but were moved out of its hierarchy.</summary>
        public virtual void GetDetachedGraphics(RecordedEntity entity, List<Transform> graphics) { }

        /// <summary>The session dropped this source's part of <paramref name="entity"/>; release <paramref name="data"/>.</summary>
        public virtual void OnEntityReleased(RecordedEntity entity, object data) { }

        /// <summary>
        /// Records this source's rows for <paramref name="tick"/> when another recorder drives the session (see
        /// <see cref="RecordingSession.RecordSources"/>).
        /// </summary>
        public virtual void RecordTick(ulong tick) { }

#if UNITY_PHYSICS_3D
        /// <summary>Whether this source simulates <paramref name="body"/> (otherwise a dynamic body is local physics).</summary>
        public virtual bool DrivesBody(Rigidbody body) => false;
#endif
    }

    /// <summary>One object of the recording: its transform stream, colliders and renderers.</summary>
    public sealed class RecordedEntity
    {
        internal struct Part
        {
            public RecordingSource source;
            public object data;
            public ulong lastSeenTick;
        }

        public int id;
        public GameObject gameObject;
        public Transform root;
        public ulong spawnTick;
        public PlayerID? owner;
        public bool isRoot;
        public StreamRows transform;
        /// <summary>Tick whose pose was last written.</summary>
        internal ulong poseTick;
        // Nearly every entity has one source, and its part is read for every entity on every tick: it is kept inline.
        // The parts of other sources sharing the entity are in a list made when the first one attaches.
        internal RecordingSource source0;
        internal object data0;
        internal ulong seen0;
        internal List<Part> more;

        internal int partCount => source0 == null ? 0 : 1 + (more?.Count ?? 0);

        public bool Has(RecordingSource source) => ReferenceEquals(source0, source) || MoreIndex(source) >= 0;

        /// <summary>What <paramref name="source"/> keeps about this entity, or null when it is not attached.</summary>
        public object DataOf(RecordingSource source)
        {
            if (ReferenceEquals(source0, source))
                return data0;
            int index = MoreIndex(source);
            return index >= 0 ? more[index].data : null;
        }

        int MoreIndex(RecordingSource source)
        {
            if (more != null)
                for (int i = 0; i < more.Count; i++)
                    if (ReferenceEquals(more[i].source, source))
                        return i;
            return -1;
        }

        internal void Add(RecordingSource source, object data)
        {
            if (source0 == null)
            {
                source0 = source;
                data0 = data;
                seen0 = 0;
            }
            else
                (more ??= new List<Part>(1)).Add(new Part { source = source, data = data });
        }

        /// <summary>Marks <paramref name="source"/>'s part seen on <paramref name="tick"/>.</summary>
        internal void Touch(RecordingSource source, ulong tick)
        {
            if (ReferenceEquals(source0, source))
            {
                seen0 = tick;
                return;
            }
            int index = MoreIndex(source);
            if (index < 0)
                return;
            var part = more[index];
            part.lastSeenTick = tick;
            more[index] = part;
        }

        /// <summary>Whether <paramref name="source"/> holds a part that was not seen on <paramref name="tick"/>.</summary>
        internal bool Unseen(RecordingSource source, ulong tick)
        {
            if (ReferenceEquals(source0, source))
                return seen0 != tick;
            int index = MoreIndex(source);
            return index >= 0 && more[index].lastSeenTick != tick;
        }

        /// <summary>Takes <paramref name="source"/>'s part out and returns its data.</summary>
        internal object Remove(RecordingSource source)
        {
            object data;
            if (ReferenceEquals(source0, source))
            {
                data = data0;
                if (more != null && more.Count > 0)
                {
                    var next = more[0];
                    more.RemoveAt(0);
                    source0 = next.source;
                    data0 = next.data;
                    seen0 = next.lastSeenTick;
                }
                else
                {
                    source0 = null;
                    data0 = null;
                }
                return data;
            }
            int index = MoreIndex(source);
            if (index < 0)
                return null;
            data = more[index].data;
            more.RemoveAt(index);
            return data;
        }

        /// <summary>The source of part <paramref name="index"/> (0 is the inline one).</summary>
        internal RecordingSource SourceAt(int index) => index == 0 ? source0 : more[index - 1].source;

        /// <summary>Body renderers and their GameObjects (read every tick for visibility).</summary>
        internal readonly List<Renderer> renderers = new List<Renderer>(8);
        internal readonly List<GameObject> rendererObjects = new List<GameObject>(8);
        // Everything the last collider check found under the entity, kept between checks: Unity creates a C#
        // object the first time a component is handed to C# and drops it once nothing references it, so a check
        // that let go of them would make garbage every time.
        internal readonly List<Renderer> foundRenderers = new List<Renderer>(8);
        internal readonly List<Renderer> foundGraphics = new List<Renderer>(4);
        internal readonly List<Transform> detachedGraphics = new List<Transform>(2);
#if UNITY_PHYSICS_3D
        /// <summary>Binary form of the last written collider set; the JSON is only built when this changes.</summary>
        internal readonly RecordingBuffer colliderKey = new RecordingBuffer(ByteShelf.shared, 256);
        internal bool hasColliderKey;
        internal readonly List<TrackedCollider> colliders = new List<TrackedCollider>(8);
        /// <summary>Colliders currently moving relative to the entity (sampled every tick).</summary>
        internal int movingColliders;
        internal readonly List<Collider> foundColliders = new List<Collider>(8);
#endif
    }

#if UNITY_PHYSICS_3D
    /// <summary>
    /// A collider of the current set. Its pose relative to the entity is checked with the set; once it moves, it
    /// gets a pose stream sampled every tick until it has been still for a few checks.
    /// </summary>
    internal sealed class TrackedCollider
    {
        public Collider collider;
        public long id;
        public bool streamable;
        /// <summary>Last checked pose, rounded like the poses in COLLIDERS records.</summary>
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public StreamRows stream;
        public bool moving;
        public int stillChecks;
    }
#endif

    /// <summary>How a stream is described in its STREAM record.</summary>
    public enum RecordedStreamKind { State, Input, Transform, Player, Custom, Collider, Sync }

    public struct RecordedStreamInfo
    {
        public RecordedStreamKind kind;
        public int entity;
        public ulong component;
        public System.Type componentType;
        public ulong player;
        public bool hasPlayer;
        public string sample;
        public long collider;
        /// <summary>Sync streams: the module's index and field name in its component.</summary>
        public int module;
        public string field;
    }

    /// <summary>The clocks and identity of the machine a session runs on.</summary>
    public interface IRecordingContext
    {
        /// <summary>Whether the clocks below can be read (the game's tick source exists).</summary>
        bool isReady { get; }
        /// <summary>This machine's tick: the server tick on the server.</summary>
        ulong localTick { get; }
        /// <summary>The server tick this machine is showing, written with client samples.</summary>
        double viewTick { get; }
        bool isServer { get; }
        /// <summary>Whether this machine also plays (a host, or a client).</summary>
        bool isClient { get; }
        PlayerID? localPlayer { get; }
        int tickRate { get; }
    }
}

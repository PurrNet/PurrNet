using UnityEngine;

namespace PurrNet.Recording
{
    public struct RecordedTransform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public byte visible;
    }

    public struct RecordedColliderPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    public struct RecordedValue<T>
    {
        public T value;
    }

    public struct RecordedTimer
    {
        public TimerState state;
        public float remaining;
    }

    public struct RecordedPlayerTick
    {
        public float viewOffsetTicks;
        public bool hasViewOffset;
    }

    public struct ClientSample<T> where T : struct
    {
        public ulong clientTick;
        public double viewTick;
        public double clientTime;
        public T sample;
    }

    /// <summary>One RPC the server received or sent. <c>args</c> holds the arguments by parameter name.</summary>
    public struct RecordedRpc
    {
        public const byte Received = 1, Sent = 2;

        public int entity;
        public int component;
        public ulong networkId;
        public int module;
        public ulong sender;
        public byte flags;
        public object args;
    }

    /// <summary>One broadcast the server received from a player or sent.</summary>
    public struct RecordedBroadcast<T>
    {
        public const byte Received = 1, Sent = 2, ToAll = 4, HasPlayer = 8;

        public int connection;
        public ulong player;
        public byte flags;
        public T value;
    }

    /// <summary>
    /// What one layer of an animator started doing: the state it plays (or is blending into, from <c>from</c>), how
    /// far into it and how fast, and the heaviest clip of that state. <c>state</c> and <c>from</c> are the states'
    /// full path hashes (<c>Animator.StringToHash("Base Layer.Run")</c>).
    /// </summary>
    public struct RecordedAnimation
    {
        public const byte Transition = 1, Loop = 2, Stopped = 4, Continued = 8;

        public int entity;
        public int animator;
        public byte layer;
        public byte flags;
        public int state;
        public int from;
        public int shortName;
        public float time;
        public float length;
        public float speed;
        public string clip;
    }

    /// <summary>
    /// The game told an animator something by calling it: to play or blend into a state, or a trigger. <c>hash</c> is
    /// what it passed (<c>Animator.StringToHash</c> of the name), <c>name</c> the name when it passed one.
    /// </summary>
    public struct RecordedAnimatorCall
    {
        public const byte Trigger = 1, Play = 2, CrossFade = 3;

        public int entity;
        public int animator;
        public byte kind;
        public int hash;
        public string name;
    }

    /// <summary>
    /// A particle system started or stopped playing or emitting, or the game made it emit <c>count</c> particles.
    /// <c>time</c> is how far into its run it is; <c>position</c> where it is, for an object that is not an entity.
    /// </summary>
    public struct RecordedParticles
    {
        public const byte Playing = 1, Emitting = 2, Continued = 4, Emitted = 8;

        public int entity;
        public int system;
        public byte flags;
        public float time;
        public int count;
        public Vector3 position;
    }

    /// <summary>
    /// An audio source started or stopped playing, or a clip was played once on it or at a point in the world.
    /// <c>position</c> is where, for a source that is not part of an entity.
    /// </summary>
    public struct RecordedAudio
    {
        public const byte Playing = 1, Loop = 2, Continued = 4, OneShot = 8, AtPoint = 16;

        public int entity;
        public int source;
        public byte flags;
        public float volume;
        public float pitch;
        public Vector3 position;
        public string clip;
    }

    public struct CameraSample
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
        public float aspect;
        public bool orthographic;
        public float orthographicSize;
    }
}

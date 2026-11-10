using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>World pose of an entity's GameObject at the start of a tick.</summary>
    public struct RecordedTransform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        /// <summary>0 hidden, 1 visible, 2 no renderers. See RecordingSession.Visibility.</summary>
        public byte visible;
    }

    /// <summary>
    /// Pose of a collider relative to its entity (world space for static colliders), for colliders that move within
    /// their entity. Overrides the pose in the COLLIDERS record from its first row on.
    /// </summary>
    public struct RecordedColliderPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    /// <summary>Row of a recorded sync type (a SyncVar's value, a SyncList's items).</summary>
    public struct RecordedValue<T>
    {
        public T value;
    }

    /// <summary>Row of a recorded SyncTimer.</summary>
    public struct RecordedTimer
    {
        public TimerState state;
        public float remaining;
    }

    /// <summary>Per-tick row of a connected player.</summary>
    public struct RecordedPlayerTick
    {
        public float viewOffsetTicks;
        public bool hasViewOffset;
    }

    /// <summary>
    /// Envelope of a sample written through <see cref="RecordingSession.WriteSample{T}"/>, stamped with
    /// the writer's clocks.
    /// </summary>
    public struct ClientSample<T> where T : struct
    {
        public ulong clientTick;
        public double viewTick;
        public double clientTime;
        public T sample;
    }

    /// <summary>A camera as the player saw it.</summary>
    public struct CameraSample
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
        public float aspect;
        public bool orthographic;
        public float orthographicSize;
    }

    /// <summary>Client to server, once per stream and connection: what a stream key means.</summary>
    public struct RecordedStreamDeclare : IPackedAuto
    {
        public ushort key;
        public string stream;
        public string type;
    }

    /// <summary>
    /// Client to server: whole fixed rows of one declared stream, inline. Only unmanaged fields, so receiving one
    /// allocates nothing.
    /// </summary>
    public struct RecordedSamplesPacket : IPackedAuto
    {
        public ushort key;
        public ushort rowSize;
        public byte rowCount;
        public RecordedSamplesPayload payload;
    }

    /// <summary>512 bytes of row data.</summary>
    public struct RecordedSamplesPayload : IPackedAuto
    {
        public const int Size = 512;

        public ulong a00, a01, a02, a03, a04, a05, a06, a07, a08, a09, a10, a11, a12, a13, a14, a15;
        public ulong a16, a17, a18, a19, a20, a21, a22, a23, a24, a25, a26, a27, a28, a29, a30, a31;
        public ulong a32, a33, a34, a35, a36, a37, a38, a39, a40, a41, a42, a43, a44, a45, a46, a47;
        public ulong a48, a49, a50, a51, a52, a53, a54, a55, a56, a57, a58, a59, a60, a61, a62, a63;
    }
}

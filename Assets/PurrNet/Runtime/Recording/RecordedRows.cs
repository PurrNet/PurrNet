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
}

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

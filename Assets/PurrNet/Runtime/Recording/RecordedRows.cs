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

using Unity.Collections;

namespace PurrNet.UTP {
    /// <summary>
    /// Connection events seen on a connection.
    /// </summary>
    public enum UTPConnectionEventType {
        OnConnected,
        OnReceivedData,
        OnDisconnected
    }

    /// <summary>
    /// Struct to store events and the related data for that event.
    /// </summary>
    public struct UTPConnectionEvent {
        /// <summary>
        /// The event type.
        /// </summary>
        public byte eventType;

        /// <summary>
        /// Event data, only used for OnReceived event.
        /// </summary>
        public FixedList4096Bytes<byte> eventData;

        /// <summary>
        /// The connection ID of the connection corresponding to this event.
        /// </summary>
        public int connectionId;
    }
}

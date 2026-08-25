using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PurrNet.Packing;

namespace PurrNet.Transports
{
    public delegate void OnConnectionState(ConnectionState state, bool asServer);

    public delegate void OnDataReceived(Connection conn, ByteData data, bool asServer);

    public delegate void OnDataSent(Connection conn, ByteData data, bool asServer); //Cannot send from clients

    public delegate void OnConnected(Connection conn, bool asServer);

    public delegate void OnDisconnected(Connection conn, DisconnectReason reason, bool asServer);

    public enum ConnectionState
    {
        Connecting,
        Connected,

        Disconnected,
        Disconnecting
    }

    [Serializable]
    public readonly struct ByteData : IEquatable<ByteData>, IDuplicate<ByteData>
    {
        public readonly byte[] data;
        public readonly int length;
        public readonly int offset;

        public ReadOnlySpan<byte> span => new(data, offset, length);

        public ArraySegment<byte> segment => new(data, offset, length);

        public static readonly ByteData empty = new(Array.Empty<byte>(), 0, 0);

        public ByteData(ArraySegment<byte> segment)
        {
            data = segment.Array;
            offset = segment.Offset;
            length = segment.Count;
        }

        public ByteData(byte[] data, int offset, int length)
        {
            this.data = data;
            this.offset = offset;
            this.length = length;
        }

        public ByteData Duplicate()
        {
            var newData = new byte[length];
            Buffer.BlockCopy(data, offset, newData, 0, length);
            return new ByteData(newData);
        }

        public override bool Equals(object obj)
        {
            if (obj is not ByteData other)
                return false;

            if (length != other.length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (data[i + offset] != other.data[i + other.offset])
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            for (int i = 0; i < length; i++)
                hash = hash * 31 + data[i + offset];
            return hash;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(16 + length * 3);
            sb.Append("LENGTH: ").Append(length).Append(" DATA: ");
            for (int i = 0; i < length; i++)
                sb.Append(data[i + offset].ToString("X2")).Append(' ');
            return sb.ToString();
        }

        public bool Equals(ByteData other)
        {
            if (length != other.length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (data[i + offset] != other.data[i + other.offset])
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Defines what happens when a packet exceeds the MTU on an unreliable channel.
    /// </summary>
    public enum MTUExceededBehaviour : byte
    {
        /// <summary>
        /// Automatically upgrade the channel to ReliableOrdered so the packet is
        /// fragmented and delivered reliably. Logs a warning.
        /// </summary>
        UpgradeToReliable = 0,

        /// <summary>
        /// Drop the packet and log a warning. Preserves unreliable semantics.
        /// </summary>
        Drop = 1,

        /// <summary>
        /// Split the message into unreliable MTU-sized fragments. The message is only
        /// delivered when every fragment arrives; missing fragments are not retransmitted.
        /// </summary>
        Fragment = 2
    }

    /// <summary>
    /// Per-RPC override for what happens when the RPC exceeds the MTU on an unreliable channel.
    /// Ignored on <see cref="Channel.UnreliableSequenced"/>: sequencing is a channel-wide
    /// property, so the NetworkManager setting governs that whole channel.
    /// </summary>
    public enum MTUBehaviour : byte
    {
        /// <summary>
        /// Follow the behaviour configured on the NetworkManager.
        /// </summary>
        NetworkManager = 0,

        /// <inheritdoc cref="MTUExceededBehaviour.UpgradeToReliable"/>
        UpgradeToReliable = 1,

        /// <inheritdoc cref="MTUExceededBehaviour.Drop"/>
        Drop = 2,

        /// <inheritdoc cref="MTUExceededBehaviour.Fragment"/>
        Fragment = 3
    }

    public static class MTUBehaviourExtensions
    {
        public static MTUExceededBehaviour Resolve(this MTUBehaviour value, MTUExceededBehaviour fallback)
        {
            switch (value)
            {
                case MTUBehaviour.UpgradeToReliable: return MTUExceededBehaviour.UpgradeToReliable;
                case MTUBehaviour.Drop: return MTUExceededBehaviour.Drop;
                case MTUBehaviour.Fragment: return MTUExceededBehaviour.Fragment;
                default: return fallback;
            }
        }

        public static MTUExceededBehaviour? AsOverride(this MTUBehaviour value)
        {
            if (value == MTUBehaviour.NetworkManager)
                return null;
            return value.Resolve(default);
        }
    }

    public enum Channel : byte
    {
        /// <summary>
        /// It ensures that the data is received but the order is not guaranteed.
        /// </summary>
        ReliableUnordered,

        /// <summary>
        /// Packets are guaranteed to be in order but not guaranteed to be received.
        /// </summary>
        UnreliableSequenced,

        /// <summary>
        /// Packets are guaranteed to be received in order.
        /// </summary>
        ReliableOrdered,

        /// <summary>
        /// Packets are not guaranteed to be received nor in order.
        /// </summary>
        Unreliable
    }

    public interface IConnectable
    {
        ConnectionState clientState { get; }

        void Connect(string ip, ushort port);

        void Disconnect();
    }

    public interface IListener
    {
        ConnectionState listenerState { get; }

        void Listen(ushort port);

        void StopListening();
    }

    public interface ITransport : IListener, IConnectable
    {
        event OnConnected onConnected;
        event OnDisconnected onDisconnected;
        event OnDataReceived onDataReceived;
        event OnDataSent onDataSent;
        event OnConnectionState onConnectionState;

        public IReadOnlyList<Connection> connections { get; }

        bool SupportsChannel(Channel channel)
        {
            return true;
        }

        int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return channel switch
            {
                Channel.Unreliable or Channel.UnreliableSequenced or Channel.ReliableUnordered => 1024,
                Channel.ReliableOrdered => 8192,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }

        bool shouldServerSendKeepAlive => false;

        bool shouldClientSendKeepAlive => false;

        void SendServerKeepAlive()
        {
        }

        void RaiseDataReceived(Connection conn, ByteData data, bool asServer);

        void RaiseDataSent(Connection conn, ByteData data, bool asServer);

        void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered);

        void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered);

        void CloseConnection(Connection conn);

        void ReceiveMessages(float delta);

        void SendMessages(float delta);

        void UnityUpdate(float delta)
        {
        }
    }

    public enum HostMigrationTransportActivationStatus
    {
        Succeeded,
        TimedOut,
        Cancelled,
        /// <summary>
        /// The activation request may have committed at the relay, but no exact response
        /// was observed. Callers must reconcile relay state and must not roll back the
        /// fully-ready local roles while the outcome is unknown.
        /// </summary>
        Indeterminate,
        Failed
    }

    public readonly struct HostMigrationTransportActivationResult
    {
        public HostMigrationTransportActivationStatus status { get; }
        public bool succeeded => status == HostMigrationTransportActivationStatus.Succeeded;
        public bool mayHaveActivated => succeeded ||
                                        status == HostMigrationTransportActivationStatus.Indeterminate;
        public string message { get; }

        public HostMigrationTransportActivationResult(HostMigrationTransportActivationStatus status,
            string message = null)
        {
            this.status = status;
            this.message = message;
        }
    }

    /// <summary>
    /// Optional two-phase publication contract used by bounded host-migration transitions.
    /// A transport whose server is usable as soon as it reports Connected does not implement
    /// this interface; PurrNet completes promotion without an external activation phase.
    /// Provider-specific claims and endpoint descriptors stay on the concrete transport or its
    /// host-migration adapter rather than becoming part of this contract.
    /// </summary>
    public interface IHostMigrationTransport
    {
        /// <summary>
        /// True after an activation request was dispatched without an authoritative
        /// outcome. The fully-ready local roles must be preserved for reconciliation.
        /// </summary>
        bool hasIndeterminateHostMigrationActivation { get; }

        /// <summary>
        /// Requests cleanup of one-use credentials prepared for a pending promotion.
        /// An exact activation descriptor must remain replayable while its outcome is unknown.
        /// </summary>
        void CancelPreparedHostMigration();

        /// <summary>
        /// Returns a terminal transport failure for the current migration attempt.
        /// A true result prevents PurrNet from retrying credentials that cannot succeed.
        /// </summary>
        bool TryGetHostMigrationFailure(bool asServer, out string failure);

        /// <summary>
        /// Publishes a provisionally available host only after PurrNet is fully ready.
        /// Transports without a pending external activation return Succeeded immediately.
        /// </summary>
        Task<HostMigrationTransportActivationResult> ActivatePreparedHostMigrationAsync(
            float timeoutSeconds, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Opaque peer route captured while the current host is healthy. Address semantics belong to
    /// the transport (for example an IP address, platform user ID, or lobby member identity).
    /// A zero port is valid for transports whose address fully identifies the peer route.
    /// </summary>
    [Serializable]
    public struct PeerMigrationEndpoint
    {
        public string address;
        public ushort port;
        public bool isValid => !string.IsNullOrWhiteSpace(address);

        public PeerMigrationEndpoint(string address, ushort port = 0)
        {
            this.address = address;
            this.port = port;
        }
    }

    /// <summary>
    /// Optional transport-neutral seam for peer-addressed host migration. The orchestration
    /// package maps PlayerIDs to live server connections and replicates the resulting opaque
    /// endpoints before a crash; the transport only extracts and applies its own route format.
    /// </summary>
    public interface IHostMigrationPeerEndpointTransport
    {
        /// <summary>
        /// True when the transport's current configuration uses peer-addressed routing.
        /// A transport can implement this interface while offering a separate direct/dedicated mode.
        /// </summary>
        bool supportsPeerHostMigration { get; }

        bool TryGetPeerMigrationEndpoint(Connection connection,
            out PeerMigrationEndpoint endpoint);

        /// <summary>
        /// Prepares the route before PurrNet stops and restarts roles. Promotion may receive an
        /// empty endpoint, in which case a transport can select its native self/loopback route.
        /// Transfer receives the last healthy-host snapshot when one was available.
        /// </summary>
        bool TryPreparePeerMigrationEndpoint(PeerMigrationEndpoint endpoint,
            bool isPromotion, out string failure);
    }

    public enum DisconnectReason
    {
        Timeout,
        ClientRequest,
        ServerRequest,
    }
}

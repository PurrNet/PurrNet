using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet.Modules
{
    public class BroadcastModule : INetworkModule, IDataListener, IConnectionListener, IFixedUpdate,
        IPromoteToServerModule, ITransferToNewServer
    {
        public const int MAX_HEADER_SIZE = 5;
        const int FRAGMENT_FRAME_PREFIX = 5; // 4-byte type marker + original channel
        const int FRAGMENT_TIMEOUT_MS = 5000;
        const int FRAGMENT_CLEANUP_INTERVAL_MS = 500;

        sealed class UnreliableFragmentFrameMarker
        {
        }

        sealed class ExactPackageBaselineFrameMarker
        {
        }

        static class BroadcastType<T>
        {
            public static readonly uint id = ResolveBroadcastTypeId<T>();
        }

        static readonly uint _fragmentFrameTypeId = Hasher<UnreliableFragmentFrameMarker>.stableHash;
        static readonly uint _exactPackageBaselineFrameTypeId =
            Hasher<ExactPackageBaselineFrameMarker>.stableHash;
        static readonly FragmentationLayer.FragmentCallback<FragmentSendState> _sendFragment = SendFragment;

        private readonly ITransport _transport;
        private readonly INetworkManager _networkManager;
        private readonly FragmentationLayer _fragmentation = new FragmentationLayer();

        private readonly Dictionary<uint, List<IBroadcastCallback>> _actions =
            new Dictionary<uint, List<IBroadcastCallback>>();

        private readonly HashSet<uint> _immediateTypeIds = new HashSet<uint>();
        private readonly List<DeferredMessage> _deferredMessages = new List<DeferredMessage>();
        private const int MaxExactOutboundBarrierMessages = 8192;
        private const int MaxExactOutboundBarrierBytes = 16 * 1024 * 1024;
        private readonly Dictionary<Connection, OutboundBarrierState>
            _deferredOutboundMessages = new Dictionary<Connection, OutboundBarrierState>();
        private bool _deferNonImmediate;
        private bool _draining;

        private bool _asServer;

        readonly struct DeferredMessage
        {
            public readonly Connection conn;
            public readonly BitPacker data;

            public DeferredMessage(Connection conn, BitPacker data)
            {
                this.conn = conn;
                this.data = data;
            }
        }

        readonly struct DeferredOutboundMessage
        {
            public readonly byte[] data;

            public DeferredOutboundMessage(byte[] data)
            {
                this.data = data;
            }
        }

        sealed class OutboundBarrierState
        {
            public readonly List<DeferredOutboundMessage> messages = new();
            public readonly List<DeferredOutboundMessage> packageBaselines = new();
            public int bytes;
            public bool overflowed;
            public bool capturingPackageBaselines;
            public bool packageBaselinesPrepared;
            public bool packageBaselinesPublished;
        }

        readonly struct FragmentSendState
        {
            public readonly BroadcastModule module;
            public readonly Connection connection;
            public readonly Channel channel;

            public FragmentSendState(BroadcastModule module, Connection connection, Channel channel)
            {
                this.module = module;
                this.connection = connection;
                this.channel = channel;
            }
        }

        internal event Action<Connection, uint, BitPacker> onRawDataReceived;

        public BroadcastModule(INetworkManager manager, bool asServer)
        {
            _transport = manager.rawTransport;
            _networkManager = manager;
            _asServer = asServer;
            _fragmentation.onMessageDropped = OnFragmentedMessageDropped;
        }

        static void OnFragmentedMessageDropped(FragmentDropInfo info)
        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            Type type = null;
            if (info.hasFirstWord && Hasher.TryGetType(info.firstWord, out var resolved))
                type = resolved;
            Statistics.DroppedMessage(type, info.reason, info.totalLength);
#endif
        }

        public void Enable(bool asServer)
        {
        }

        public void Disable(bool asServer)
        {
            DrainDeferred();
            DisposeDeferred();
            _deferredOutboundMessages.Clear();
            _fragmentation.Reset();
            _deferNonImmediate = false;
        }

        internal void BeginReliableOrderedOutboundBarrier(Connection connection)
        {
            AssertIsServer("Cannot begin an outbound client barrier from a client module.");
            if (!_deferredOutboundMessages.ContainsKey(connection))
            {
                _deferredOutboundMessages.Add(
                    connection, new OutboundBarrierState());
            }
        }

        internal void ReleaseReliableOrderedOutboundBarrier(Connection connection)
        {
            AssertIsServer("Cannot release an outbound client barrier from a client module.");
            if (!_deferredOutboundMessages.Remove(connection, out var state))
                return;

            if (state.overflowed)
            {
                _transport.CloseConnection(connection);
                return;
            }

            if (state.capturingPackageBaselines ||
                state.packageBaselinesPrepared && !state.packageBaselinesPublished)
            {
                PurrLogger.LogError(
                    $"Exact outbound barrier for connection {connection} was released before " +
                    "its prepared package baselines were published; closing instead of " +
                    "reordering the authority stream.");
                _transport.CloseConnection(connection);
                return;
            }

            for (var i = 0; i < state.messages.Count; i++)
            {
                var bytes = state.messages[i].data;
                _transport.SendToClient(connection,
                    new ByteData(bytes, 0, bytes.Length), Channel.ReliableOrdered);
            }
        }

        internal void DropReliableOrderedOutboundBarrier(Connection connection)
        {
            _deferredOutboundMessages.Remove(connection);
        }

        internal bool BeginPackageBaselineCapture(Connection connection, out string failure)
        {
            AssertIsServer("Cannot capture an outbound package baseline from a client module.");
            failure = null;
            if (!_deferredOutboundMessages.TryGetValue(connection, out var state))
            {
                failure = $"connection {connection} has no exact outbound barrier";
                return false;
            }

            if (state.overflowed)
            {
                failure = $"connection {connection}'s exact outbound barrier has overflowed";
                return false;
            }

            if (state.capturingPackageBaselines || state.packageBaselinesPrepared ||
                state.packageBaselinesPublished)
            {
                failure = $"connection {connection}'s package baseline phase has already started";
                return false;
            }

            state.capturingPackageBaselines = true;
            return true;
        }

        internal bool FinishPackageBaselineCapture(Connection connection, bool commit,
            out string failure)
        {
            AssertIsServer("Cannot finish an outbound package baseline from a client module.");
            failure = null;
            if (!_deferredOutboundMessages.TryGetValue(connection, out var state) ||
                !state.capturingPackageBaselines)
            {
                failure = $"connection {connection} has no active package baseline capture";
                return false;
            }

            state.capturingPackageBaselines = false;
            if (!commit || state.overflowed)
            {
                DiscardPackageBaselines(state);
                if (state.overflowed)
                    failure = $"connection {connection}'s exact outbound barrier overflowed";
                return !state.overflowed;
            }

            state.packageBaselinesPrepared = true;
            return true;
        }

        internal bool PublishPackageBaselines(Connection connection, out string failure)
        {
            AssertIsServer("Cannot publish an outbound package baseline from a client module.");
            failure = null;
            if (!_deferredOutboundMessages.TryGetValue(connection, out var state))
            {
                failure = $"connection {connection} has no exact outbound barrier";
                return false;
            }

            if (state.overflowed || state.capturingPackageBaselines ||
                !state.packageBaselinesPrepared || state.packageBaselinesPublished)
            {
                failure = $"connection {connection}'s package baseline is not publishable";
                return false;
            }

            for (var i = 0; i < state.packageBaselines.Count; i++)
            {
                var bytes = state.packageBaselines[i].data;
                _transport.SendToClient(connection,
                    new ByteData(bytes, 0, bytes.Length), Channel.ReliableOrdered);
                state.bytes -= bytes.Length;
            }

            state.packageBaselines.Clear();
            state.packageBaselinesPublished = true;
            return true;
        }

        private static void DiscardPackageBaselines(OutboundBarrierState state)
        {
            for (var i = 0; i < state.packageBaselines.Count; i++)
                state.bytes -= state.packageBaselines[i].data.Length;
            state.packageBaselines.Clear();
            state.packageBaselinesPrepared = false;
        }

        private bool TryDeferOutbound(Connection connection, ByteData data)
        {
            if (!_deferredOutboundMessages.TryGetValue(connection, out var state))
                return false;

            if (state.overflowed)
                return true;

            bool packageBaseline = state.capturingPackageBaselines;
            var destination = packageBaseline
                ? state.packageBaselines
                : state.messages;
            int capturedLength = data.length + (packageBaseline ? sizeof(uint) : 0);
            if (state.messages.Count + state.packageBaselines.Count >=
                    MaxExactOutboundBarrierMessages ||
                capturedLength > MaxExactOutboundBarrierBytes - state.bytes)
            {
                state.messages.Clear();
                state.packageBaselines.Clear();
                state.bytes = 0;
                state.overflowed = true;
                PurrLogger.LogError(
                    $"Exact outbound barrier for connection {connection} exceeded its bounded " +
                    $"{MaxExactOutboundBarrierMessages}-message/{MaxExactOutboundBarrierBytes}-byte budget; " +
                    "closing the connection instead of releasing a partial authority stream.");
                _transport.CloseConnection(connection);
                return true;
            }

            var copy = new byte[capturedLength];
            int payloadOffset = 0;
            if (packageBaseline)
            {
                WriteUInt32(copy, 0, _exactPackageBaselineFrameTypeId);
                payloadOffset = sizeof(uint);
            }

            Buffer.BlockCopy(data.data, data.offset, copy, payloadOffset, data.length);
            destination.Add(new DeferredOutboundMessage(copy));
            state.bytes += copy.Length;
            return true;
        }

        /// <summary>
        /// Marks a broadcast type as immediate: while the defer window is active,
        /// messages of this type dispatch on arrival while everything else is queued
        /// for the next fixed receive phase.
        /// </summary>
        public void RegisterImmediateType<T>()
        {
            _immediateTypeIds.Add(BroadcastType<T>.id);
        }

        public void UnregisterImmediateType<T>()
        {
            _immediateTypeIds.Remove(BroadcastType<T>.id);
        }

        internal void SetDeferNonImmediate(bool defer)
        {
            _deferNonImmediate = defer;
        }

        internal void DrainDeferred()
        {
            if (_draining || _deferredMessages.Count == 0)
                return;

            _draining = true;

            try
            {
                for (int i = 0; i < _deferredMessages.Count; i++)
                {
                    var message = _deferredMessages[i];

                    try
                    {
                        ProcessData(message.conn, message.data.ToByteData());
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogException(e);
                    }
                    finally
                    {
                        message.data.Dispose();
                    }
                }

                _deferredMessages.Clear();
            }
            finally
            {
                _draining = false;
            }
        }

        /// <summary>
        /// Drains only the given connection's deferred packets (preserving their order),
        /// leaving other connections' traffic queued for the tick receive phase.
        /// </summary>
        internal void DrainDeferred(Connection conn)
        {
            if (_draining || _deferredMessages.Count == 0)
                return;

            _draining = true;

            try
            {
                for (int i = 0; i < _deferredMessages.Count; i++)
                {
                    var message = _deferredMessages[i];

                    if (message.conn != conn)
                        continue;

                    _deferredMessages.RemoveAt(i--);

                    try
                    {
                        ProcessData(message.conn, message.data.ToByteData());
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogException(e);
                    }
                    finally
                    {
                        message.data.Dispose();
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private void DisposeDeferred()
        {
            for (int i = 0; i < _deferredMessages.Count; i++)
                _deferredMessages[i].data.Dispose();
            _deferredMessages.Clear();
        }

        void AssertIsServer(string message)
        {
            if (!_asServer)
                throw new InvalidOperationException(PurrLogger.FormatMessage(message));
        }

        private static ByteData GetData<T>(T data)
        {
            using var stream = BitPackerPool.Get();
            uint typeId = BroadcastType<T>.id;

            Packer<uint>.WriteFunc(stream, typeId);
            Packer<T>.WriteFunc(stream, data);

            return stream.ToByteData();
        }

        static uint ResolveBroadcastTypeId<T>()
        {
            uint typeId = Hasher.GetStableHashU32<T>();
            if (typeId == _fragmentFrameTypeId ||
                typeId == _exactPackageBaselineFrameTypeId)
                throw new InvalidOperationException(PurrLogger.FormatMessage(
                    $"Broadcast type `{typeof(T)}` collides with a reserved PurrNet frame id."));
            return typeId;
        }

        static bool ShouldTrackType(Type type)
        {
            return type != typeof(RPCPacket) && type != typeof(ChildRPCPacket) && type != typeof(StaticRPCPacket)
                   && type != typeof(RPCBatchPacket) && type != typeof(ImmediateRPCBatchPacket);
        }

        private static bool IsUnreliable(Channel channel)
        {
            return channel is Channel.Unreliable or Channel.UnreliableSequenced;
        }

        private bool HandleMTUExceeded<T>(Connection conn, ByteData byteData, ref Channel method,
            MTUExceededBehaviour? mtuOverride)
        {
            if (!IsUnreliable(method))
                return true;

            // sequencing is a channel-wide property; per-message overrides only apply to Unreliable
            var behaviour = mtuOverride.HasValue && method != Channel.UnreliableSequenced
                ? mtuOverride.Value
                : _networkManager.mtuExceededBehaviour;
            var mtu = _transport.GetMTU(conn, method, _asServer);
            bool sequenceThroughFragmentation = method == Channel.UnreliableSequenced &&
                                                behaviour == MTUExceededBehaviour.Fragment;

            if (sequenceThroughFragmentation)
            {
                SendFragmented<T>(conn, byteData, method, mtu, true);
                return false;
            }

            if (byteData.length <= mtu)
                return true;

            switch (behaviour)
            {
                case MTUExceededBehaviour.UpgradeToReliable:
                    PurrLogger.LogWarning(
                        $"MTU exceeded by `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}). " +
                        $"Upgrading {method} to {Channel.ReliableOrdered}.");
                    method = Channel.ReliableOrdered;
                    return true;
                case MTUExceededBehaviour.Drop:
                    PurrLogger.LogError(
                        $"MTU exceeded by `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}). " +
                        $"Dropping {method} packet.");
                    return false;
                case MTUExceededBehaviour.Fragment:
                    SendFragmented<T>(conn, byteData, method, mtu, false);
                    return false;
                default:
                    return true;
            }
        }

        void SendFragmented<T>(Connection conn, ByteData byteData, Channel method, int mtu, bool sequenced)
        {
            int maxMessageSize = sequenced
                ? FragmentationLayer.GetMaxSequencedMessageSize(mtu, FRAGMENT_FRAME_PREFIX)
                : FragmentationLayer.GetMaxMessageSize(mtu, FRAGMENT_FRAME_PREFIX);
            if (byteData.length > maxMessageSize)
            {
                PurrLogger.LogError(
                    $"Cannot fragment `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}). " +
                    $"Maximum unreliable message size is {maxMessageSize} bytes. Dropping packet.");
                return;
            }

            try
            {
                var state = new FragmentSendState(this, conn, method);
                if (sequenced)
                    _fragmentation.SendSequenced(byteData, mtu, FRAGMENT_FRAME_PREFIX, state, _sendFragment);
                else
                    _fragmentation.Send(byteData, mtu, FRAGMENT_FRAME_PREFIX, state, _sendFragment);
            }
            catch (ArgumentException e)
            {
                PurrLogger.LogError(
                    $"Cannot fragment `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}): {e.Message}");
            }
        }

        static void SendFragment(ByteData fragment, FragmentSendState state)
        {
            byte[] data = fragment.data;
            int offset = fragment.offset;
            uint marker = _fragmentFrameTypeId;
            data[offset] = (byte)marker;
            data[offset + 1] = (byte)(marker >> 8);
            data[offset + 2] = (byte)(marker >> 16);
            data[offset + 3] = (byte)(marker >> 24);
            data[offset + 4] = (byte)state.channel;

            // Sequenced fragments must not ride the drop-anything-older transport channel: a
            // reordered fragment would be discarded there and the message could never complete.
            // The layer's own message ids already sequence the stream, so plain Unreliable is safe.
            var wireChannel = state.channel == Channel.UnreliableSequenced ? Channel.Unreliable : state.channel;

            if (state.module._asServer)
                state.module._transport.SendToClient(state.connection, fragment, wireChannel);
            else
                state.module._transport.SendToServer(fragment, wireChannel);
        }

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            AssertIsServer("Cannot send data to all clients from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            bool shouldTrack = ShouldTrackType(type);
#endif
            int connCount = _transport.connections.Count;
            bool hasOutboundBarriers = _deferredOutboundMessages.Count != 0;
            for (int i = 0; i < connCount; i++)
            {
                var conn = _transport.connections[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                if (shouldTrack)
                    Statistics.SentBroadcast(type, byteData.segment);
#endif
                if (hasOutboundBarriers && TryDeferOutbound(conn, byteData))
                    continue;
                var connMethod = method;
                if (!HandleMTUExceeded<T>(conn, byteData, ref connMethod, mtuOverride))
                    continue;
                _transport.SendToClient(conn, byteData, connMethod);
            }
        }

        public void Send<T>(Connection conn, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            Send(conn, data, method, mtuOverride, false);
        }

        internal void SendBarrierBypass<T>(Connection conn, T data,
            Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            Send(conn, data, method, mtuOverride, true);
        }

        private void Send<T>(Connection conn, T data, Channel method,
            MTUExceededBehaviour? mtuOverride, bool bypassOutboundBarrier)
        {
            AssertIsServer("Cannot send data to player from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            if (ShouldTrackType(type))
                Statistics.SentBroadcast(type, byteData.segment);
#endif
            if (!bypassOutboundBarrier && _deferredOutboundMessages.Count != 0 &&
                TryDeferOutbound(conn, byteData))
                return;
            if (!HandleMTUExceeded<T>(conn, byteData, ref method, mtuOverride))
                return;
            _transport.SendToClient(conn, byteData, method);
        }

        public void Send<T>(IReadOnlyList<Connection> conn, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            AssertIsServer("Cannot send data to player from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            var shouldTrack = ShouldTrackType(type);
#endif

            bool hasOutboundBarriers = _deferredOutboundMessages.Count != 0;
            for (var i = 0; i < conn.Count; i++)
            {
                var connection = conn[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                if (shouldTrack)
                    Statistics.SentBroadcast(type, byteData.segment);
#endif
                if (hasOutboundBarriers && TryDeferOutbound(connection, byteData))
                    continue;
                var connMethod = method;
                if (!HandleMTUExceeded<T>(connection, byteData, ref connMethod, mtuOverride))
                    continue;
                _transport.SendToClient(connection, byteData, connMethod);
            }
        }

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (_asServer)
                return;

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            if (ShouldTrackType(type))
                Statistics.SentBroadcast(type, byteData.segment);
#endif
            if (!HandleMTUExceeded<T>(default, byteData, ref method, mtuOverride))
                return;
            _transport.SendToServer(byteData, method);
        }

        public void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            try
            {
                if (_asServer != asServer)
                    return;

                ProcessData(conn, data);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        void ProcessData(Connection conn, ByteData data)
        {
            if (data.length < sizeof(uint))
                return;

            uint typeId = ReadUInt32(data.data, data.offset);
            if (typeId == _fragmentFrameTypeId)
            {
                ProcessFragment(conn, data);
                return;
            }

            if (_deferNonImmediate && !_draining && !_immediateTypeIds.Contains(typeId))
            {
                var copy = BitPackerPool.Get();
                copy.WriteBytes(data);
                _deferredMessages.Add(new DeferredMessage(conn, copy));
                return;
            }

            if (typeId == _exactPackageBaselineFrameTypeId)
            {
                if (data.length <= sizeof(uint))
                    return;

                var payload = new ByteData(
                    data.data,
                    data.offset + sizeof(uint),
                    data.length - sizeof(uint));
                if (payload.length >= sizeof(uint) &&
                    ReadUInt32(payload.data, payload.offset) == _exactPackageBaselineFrameTypeId)
                {
                    PurrLogger.LogError(
                        "Nested exact package-baseline envelopes are not valid authority traffic.");
                    return;
                }

                ProcessData(conn, payload);
                return;
            }

            using (var stream = BitPackerPool.Get(data))
            {
                stream.SkipBits(sizeof(uint) * 8);

                if (!Hasher.TryGetType(typeId, out var typeInfo))
                {
                    PurrLogger.LogError(
                        $"Cannot find type with id {typeId}; type must not have been registered properly.\nData: {data.ToString()}");
                    return;
                }

                TriggerCallback(conn, typeId, stream);

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                if (ShouldTrackType(typeInfo))
                    Statistics.ReceivedBroadcast(typeInfo, data.segment);
#endif
            }
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return data[offset] |
                   ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) |
                   ((uint)data[offset + 3] << 24);
        }

        static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        void ProcessFragment(Connection conn, ByteData data)
        {
            if (data.length <= FRAGMENT_FRAME_PREFIX)
                return;

            var channel = (Channel)data.data[data.offset + 4];
            if (!IsUnreliable(channel))
                return;

            var fragment = new ByteData(data.data, data.offset + FRAGMENT_FRAME_PREFIX,
                data.length - FRAGMENT_FRAME_PREFIX);
            bool sequenced = channel == Channel.UnreliableSequenced;
            if (_fragmentation.Receive(conn.connectionId, (byte)channel, sequenced, fragment, out var assembled))
                ProcessData(conn, assembled);
        }

        public void FixedUpdate()
        {
            _fragmentation.CleanupStaleIfDue(FRAGMENT_TIMEOUT_MS, FRAGMENT_CLEANUP_INTERVAL_MS);
        }

        public void OnConnected(Connection conn, bool asServer)
        {
            if (_asServer == asServer)
                _fragmentation.RemoveSender(conn.connectionId);
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
            if (_asServer == asServer)
            {
                _fragmentation.RemoveSender(conn.connectionId);
                DropReliableOrderedOutboundBarrier(conn);
            }
        }

        public void Subscribe<T>(BroadcastDelegate<T> callback)
        {
            uint hash = BroadcastType<T>.id;

            if (_actions.TryGetValue(hash, out var actions))
            {
                actions.Add(new BroadcastCallback<T>(callback));
                return;
            }

            _actions.Add(hash, new List<IBroadcastCallback>
            {
                new BroadcastCallback<T>(callback)
            });
        }

        public void Unsubscribe<T>(BroadcastDelegate<T> callback)
        {
            uint hash = BroadcastType<T>.id;
            if (!_actions.TryGetValue(hash, out var actions))
                return;

            object boxed = callback;

            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].IsSame(boxed))
                {
                    actions.RemoveAt(i);
                    return;
                }
            }
        }

        private void TriggerCallback(Connection conn, uint hash, BitPacker packer)
        {
            var startPos = packer.positionInBits;

            if (_actions.TryGetValue(hash, out var actions))
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    actions[i].TriggerCallback(conn, packer, _asServer);
                    packer.SetBitPosition(startPos);
                }
            }

            onRawDataReceived?.Invoke(conn, hash, packer);
        }

        public void PromoteToServerModule()
        {
            _fragmentation.Reset();
            DisposeDeferred();
            _deferredOutboundMessages.Clear();
            _asServer = true;
        }

        public void TransferToNewServer()
        {
            _fragmentation.Reset();
            DisposeDeferred();
            _deferredOutboundMessages.Clear();
        }

        public void PostPromoteToServerModule() { }
    }
}

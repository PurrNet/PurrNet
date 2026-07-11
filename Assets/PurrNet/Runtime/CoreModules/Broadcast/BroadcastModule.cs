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
        IPromoteToServerModule
    {
        public const int MAX_HEADER_SIZE = 5;
        const int FRAGMENT_FRAME_PREFIX = 5; // 4-byte type marker + original channel
        const int FRAGMENT_TIMEOUT_MS = 5000;
        const int FRAGMENT_CLEANUP_INTERVAL_MS = 500;

        sealed class UnreliableFragmentFrameMarker
        {
        }

        static class BroadcastType<T>
        {
            public static readonly uint id = ResolveBroadcastTypeId<T>();
        }

        static readonly uint _fragmentFrameTypeId = Hasher<UnreliableFragmentFrameMarker>.stableHash;
        static readonly FragmentationLayer.FragmentCallback<FragmentSendState> _sendFragment = SendFragment;

        private readonly ITransport _transport;
        private readonly INetworkManager _networkManager;
        private readonly FragmentationLayer _fragmentation = new FragmentationLayer();

        private readonly Dictionary<uint, List<IBroadcastCallback>> _actions =
            new Dictionary<uint, List<IBroadcastCallback>>();

        private bool _asServer;

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
        }

        public void Enable(bool asServer)
        {
        }

        public void Disable(bool asServer)
        {
            _fragmentation.Reset();
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
            if (typeId == _fragmentFrameTypeId)
                throw new InvalidOperationException(PurrLogger.FormatMessage(
                    $"Broadcast type `{typeof(T)}` collides with PurrNet's reserved fragmentation frame id."));
            return typeId;
        }

        static bool ShouldTrackType(Type type)
        {
            return type != typeof(RPCPacket) && type != typeof(ChildRPCPacket) && type != typeof(StaticRPCPacket)
                   && type != typeof(RPCBatchPacket);
        }

        private static bool IsUnreliable(Channel channel)
        {
            return channel is Channel.Unreliable or Channel.UnreliableSequenced;
        }

        private bool HandleMTUExceeded<T>(Connection conn, ByteData byteData, ref Channel method)
        {
            if (!IsUnreliable(method))
                return true;

            var mtu = _transport.GetMTU(conn, method, _asServer);
            if (byteData.length <= mtu)
                return true;

            switch (_networkManager.mtuExceededBehaviour)
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
                    int maxMessageSize = FragmentationLayer.GetMaxMessageSize(mtu, FRAGMENT_FRAME_PREFIX);
                    if (byteData.length > maxMessageSize)
                    {
                        PurrLogger.LogError(
                            $"Cannot fragment `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}). " +
                            $"Maximum unreliable message size is {maxMessageSize} bytes. Dropping packet.");
                        return false;
                    }

                    try
                    {
                        var state = new FragmentSendState(this, conn, method);
                        _fragmentation.Send(byteData, mtu, FRAGMENT_FRAME_PREFIX, state, _sendFragment);
                    }
                    catch (ArgumentException e)
                    {
                        PurrLogger.LogError(
                            $"Cannot fragment `{typeof(T)}` ({byteData.length} bytes, MTU {mtu}): {e.Message}");
                    }
                    return false;
                default:
                    return true;
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

            if (state.module._asServer)
                state.module._transport.SendToClient(state.connection, fragment, state.channel);
            else
                state.module._transport.SendToServer(fragment, state.channel);
        }

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered)
        {
            AssertIsServer("Cannot send data to all clients from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            bool shouldTrack = ShouldTrackType(type);
#endif
            int connCount = _transport.connections.Count;
            for (int i = 0; i < connCount; i++)
            {
                var conn = _transport.connections[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                if (shouldTrack)
                    Statistics.SentBroadcast(type, byteData.segment);
#endif
                var connMethod = method;
                if (!HandleMTUExceeded<T>(conn, byteData, ref connMethod))
                    continue;
                _transport.SendToClient(conn, byteData, connMethod);
            }
        }

        public void Send<T>(Connection conn, T data, Channel method = Channel.ReliableOrdered)
        {
            AssertIsServer("Cannot send data to player from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            if (ShouldTrackType(type))
                Statistics.SentBroadcast(type, byteData.segment);
#endif
            if (!HandleMTUExceeded<T>(conn, byteData, ref method))
                return;
            _transport.SendToClient(conn, byteData, method);
        }

        public void Send<T>(IReadOnlyList<Connection> conn, T data, Channel method = Channel.ReliableOrdered)
        {
            AssertIsServer("Cannot send data to player from client.");

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            var shouldTrack = ShouldTrackType(type);
#endif

            for (var i = 0; i < conn.Count; i++)
            {
                var connection = conn[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                if (shouldTrack)
                    Statistics.SentBroadcast(type, byteData.segment);
#endif
                var connMethod = method;
                if (!HandleMTUExceeded<T>(connection, byteData, ref connMethod))
                    continue;
                _transport.SendToClient(connection, byteData, connMethod);
            }
        }

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered)
        {
            if (_asServer)
                return;

            var byteData = GetData(data);
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            var type = typeof(T);
            if (ShouldTrackType(type))
                Statistics.SentBroadcast(type, byteData.segment);
#endif
            if (!HandleMTUExceeded<T>(default, byteData, ref method))
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
                _fragmentation.RemoveSender(conn.connectionId);
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
            _asServer = true;
        }

        public void PostPromoteToServerModule() { }
    }
}

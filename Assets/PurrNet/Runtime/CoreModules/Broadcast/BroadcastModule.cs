using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet.Modules
{
    public class BroadcastModule : INetworkModule, IDataListener, IPromoteToServerModule
    {
        public const int MAX_HEADER_SIZE = 5;
        private readonly ITransport _transport;

        private readonly Dictionary<uint, List<IBroadcastCallback>> _actions =
            new Dictionary<uint, List<IBroadcastCallback>>();

        private bool _asServer;

        internal event Action<Connection, uint, BitPacker> onRawDataReceived;

        public BroadcastModule(INetworkManager manager, bool asServer)
        {
            _transport = manager.rawTransport;
            _asServer = asServer;
        }

        public void Enable(bool asServer)
        {
        }

        public void Disable(bool asServer)
        {
        }

        void AssertIsServer(string message)
        {
            if (!_asServer)
                throw new InvalidOperationException(PurrLogger.FormatMessage(message));
        }

        private static ByteData GetData<T>(T data)
        {
            using var stream = BitPackerPool.Get();
            var typeId = Hasher.GetStableHashU32<T>();

            Packer<uint>.WriteFunc(stream, typeId);
            Packer<T>.WriteFunc(stream, data);

            return stream.ToByteData();
        }

        static bool ShouldTrackType(Type type)
        {
            return type != typeof(RPCPacket) && type != typeof(ChildRPCPacket) && type != typeof(StaticRPCPacket)
                   && type != typeof(RPCBatchPacket);
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
#if PURR_MTU_DEBUGGING
                var mtu = _transport.GetMTU(conn, method, _asServer);
                if (byteData.length > mtu)
                    PurrLogger.LogError($"MTU exceeded by `{typeof(T)}` with {byteData.length} bytes when MTU is {mtu} bytes.");
#endif
                _transport.SendToClient(conn, byteData, method);
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
#if PURR_MTU_DEBUGGING
            var mtu = _transport.GetMTU(conn, method, _asServer);
            if (byteData.length > mtu)
                PurrLogger.LogError($"MTU exceeded by `{typeof(T)}` with {byteData.length} bytes when MTU is {mtu} bytes.");
#endif
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
#if PURR_MTU_DEBUGGING
                var mtu = _transport.GetMTU(connection, method, _asServer);
                if (byteData.length > mtu)
                    PurrLogger.LogError($"MTU exceeded by `{typeof(T)}` with {byteData.length} bytes when MTU is {mtu} bytes.");
#endif
                _transport.SendToClient(connection, byteData, method);
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
#if PURR_MTU_DEBUGGING
            var mtu = _transport.GetMTU(default, method, _asServer);
            if (byteData.length > mtu)
                PurrLogger.LogError($"MTU exceeded by `{typeof(T)}` with {byteData.length} bytes when MTU is {mtu} bytes.");
#endif
            _transport.SendToServer(byteData, method);
        }

        public void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            try
            {
                if (_asServer != asServer)
                    return;

                using var stream = BitPackerPool.Get(data);
                var typeId = Packer<uint>.Read(stream);

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
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        public void Subscribe<T>(BroadcastDelegate<T> callback)
        {
            var hash = Hasher.GetStableHashU32(typeof(T));

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
            var hash = Hasher.GetStableHashU32(typeof(T));
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
            _asServer = true;
        }

        public void PostPromoteToServerModule() { }
    }
}

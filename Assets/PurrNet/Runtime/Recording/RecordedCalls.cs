using System;
using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Transports;

namespace PurrNet.Recording
{
    /// <summary>
    /// Where generated RPC code and the broadcast module hand the calls the server sees to the recorder. Every entry
    /// point is a no-op unless a recording is running on this machine as the server.
    /// </summary>
    public static class RecordedCalls
    {
        /// <summary>True while a server recording takes calls. Generated code reads it before doing anything else.</summary>
        [UsedByIL] public static bool enabled;

        static NetworkRecorder _recorder;

        internal static void SetRecorder(NetworkRecorder recorder)
        {
            _recorder = recorder;
            enabled = recorder != null;
        }

        internal static void ClearRecorder(NetworkRecorder recorder)
        {
            if (_recorder == recorder)
                SetRecorder(null);
        }

        /// <summary>An RPC that arrived. Recorded when it arrived on the server.</summary>
        [UsedByIL]
        public static bool BeginReceived(object target, string name, int type, RPCInfo info, bool asServer) =>
            asServer && _recorder != null && _recorder.BeginCall(target, name, type, true, info.sender);

        /// <summary>
        /// An RPC called on this machine. Recorded when this machine is the server: what it sends, and a host's own
        /// ServerRpc, which runs in place and is filed as received from the host's player.
        /// </summary>
        [UsedByIL]
        public static bool BeginSent(object target, string name, int type) =>
            _recorder != null && _recorder.BeginCall(target, name, type, false, default);

        [UsedByIL]
        public static void Arg<T>(string name, T value)
        {
            if (_recorder != null)
                _recorder.CallArg(name, ref value);
        }

        [UsedByIL]
        public static void End()
        {
            if (_recorder != null)
                _recorder.EndCall();
        }

        /// <summary>The manager whose server is handing a received broadcast to its subscribers right now.</summary>
        internal static object receiving;

        internal static void BroadcastReceived<T>(Connection conn, in T data)
        {
            var manager = receiving;
            receiving = null;
            if (_recorder != null)
                _recorder.RecordBroadcast(manager, conn, null, data, RecordedBroadcast<T>.Received);
        }

        internal static void BroadcastReceived<T>(PlayerID player, in T data)
        {
            var manager = receiving;
            receiving = null;
            if (_recorder != null)
                _recorder.RecordBroadcast(manager, default, player, data, RecordedBroadcast<T>.Received);
        }

        internal static void BroadcastSent<T>(object manager, Connection conn, in T data, bool toAll)
        {
            if (_recorder != null)
                _recorder.RecordBroadcast(manager, conn, null, data,
                    toAll ? (byte)(RecordedBroadcast<T>.Sent | RecordedBroadcast<T>.ToAll) : RecordedBroadcast<T>.Sent);
        }
    }

    static class RecordedBroadcastType<T>
    {
        // PurrNet's own packets (spawns, RPCs, ticks) travel as broadcasts too; they are plumbing.
        public static readonly bool excluded = typeof(T).IsDefined(typeof(DontRecordAttribute), false) ||
                                               typeof(T).Assembly.IsDefined(typeof(DontRecordAttribute), false);
    }

    public sealed partial class NetworkRecorder
    {
        sealed class CallStream
        {
            public StreamRows rows;
            public int layoutId;
            public bool described;
            public readonly List<string> names = new List<string>(4);
        }

        readonly Dictionary<string, CallStream> _calls = new Dictionary<string, CallStream>();
        readonly Dictionary<Type, StreamRows> _broadcasts = new Dictionary<Type, StreamRows>();
        readonly RecordingBuffer _callArgs = new RecordingBuffer(256);
        readonly HashSet<object> _callVisiting = new HashSet<object>(ReferenceComparer.instance);
        CallStream _call;
        string _callName;
        RecordedRpc _callRow;
        int _callArgCount;
        bool _callFailed;
        PlayersManager _playersManager;

        void BeginCalls()
        {
            _call = null;
            _playersManager = null;
            RecordedCalls.SetRecorder(this);
        }

        void EndCalls()
        {
            RecordedCalls.ClearRecorder(this);
            _call = null;
            _calls.Clear();
            _broadcasts.Clear();
        }

        internal bool BeginCall(object target, string name, int type, bool received, PlayerID sender)
        {
            _call = null;
            if (!_session.recordsEvents)
                return false;

            _session.stats.Begin(_statCalls);
            try
            {
                NetworkIdentity identity = null;
                int module = -1;
                if (target is NetworkIdentity asIdentity)
                    identity = asIdentity;
                else if (target is NetworkModule asModule)
                {
                    identity = asModule.parent;
                    module = asModule.index;
                }

                bool hasIdentity = identity is not null && identity;
                if (!received && !(hasIdentity ? identity.isServer : _networkManager && _networkManager.isServer))
                    return false;
                if (hasIdentity && Excluded(identity))
                    return false;

                if (!_calls.TryGetValue(name, out var call))
                {
                    call = new CallStream { layoutId = _session.types.ReserveId() };
                    call.rows = _session.CreateStream(typeof(RecordedRpc), new RecordedStreamInfo
                    {
                        kind = RecordedStreamKind.Rpc, sample = name, field = ((RPCType)type).ToString(),
                        componentType = hasIdentity ? identity.GetType() : null
                    });
                    _calls[name] = call;
                }

                var row = new RecordedRpc { component = -1, module = module };
                if (hasIdentity)
                {
                    if (_components.TryGetValue(identity, out var component))
                    {
                        row.entity = component.data.entity.id;
                        row.component = component.index;
                    }
                    if (identity.id.HasValue)
                        row.networkId = identity.id.Value.id.value;
                }
                if (!received && type == (int)RPCType.ServerRPC && _networkManager && _networkManager.isClient)
                {
                    received = true;
                    sender = _networkManager.localPlayer;
                }
                if (received)
                {
                    row.sender = sender.id.value;
                    row.flags = RecordedRpc.Received;
                }
                else
                    row.flags = RecordedRpc.Sent;

                _callRow = row;
                _callName = name;
                _callArgCount = 0;
                _callFailed = false;
                _callArgs.Clear();
                _callArgs.WriteByte(9);
                _callArgs.WriteVarUInt((ulong)call.layoutId);
                _call = call;
                return true;
            }
            catch (Exception e)
            {
                _session.AddError("rpc_record_failed", $"{name}: {e.GetType().Name}");
                _call = null;
                return false;
            }
            finally
            {
                _session.stats.End(_statCalls);
            }
        }

        internal void CallArg<T>(string name, ref T value)
        {
            var call = _call;
            if (call == null || _callFailed)
                return;

            int start = _callArgs.length;
            _session.stats.Begin(_statCalls);
            try
            {
                if (!call.described)
                    call.names.Add(name);
                _callArgCount++;
                _callVisiting.Clear();
                var types = _session.types;
                if (typeof(T).IsValueType)
                    ElementWriter<T>.Write(types.GetValuePlan(typeof(T)), types, _callArgs, ref value, 1, _callVisiting);
                else
                    types.WriteTagged(_callArgs, value, 1, _callVisiting);
            }
            catch (Exception)
            {
                _callArgs.length = start;
                _callArgs.WriteByte(12);
            }
            _session.stats.End(_statCalls);
        }

        internal void EndCall()
        {
            var call = _call;
            _call = null;
            if (call == null || _callFailed || !_session.recordsEvents)
                return;

            _session.stats.Begin(_statCalls);
            try
            {
                if (!call.described)
                {
                    call.described = true;
                    _session.types.DescribeObject(call.layoutId, _callName, call.names);
                }
                if (_callArgCount != call.names.Count)
                {
                    _session.AddError("rpc_argument_mismatch", _callName);
                    return;
                }
                _session.AppendEventRow(call.rows, ref _callRow, _callArgs);
            }
            catch (Exception e)
            {
                _session.AddError("rpc_record_failed", $"{_callName}: {e.GetType().Name}");
            }
            finally
            {
                _session.stats.End(_statCalls);
            }
        }

        internal void RecordBroadcast<T>(object manager, Connection conn, PlayerID? known, in T data, byte flags)
        {
            if (RecordedBroadcastType<T>.excluded || !_session.recordsEvents || !ReferenceEquals(manager, _networkManager))
                return;

            _session.stats.Begin(_statCalls);
            try
            {
                if (!_broadcasts.TryGetValue(typeof(T), out var rows))
                {
                    rows = _session.CreateStream(typeof(RecordedBroadcast<T>), new RecordedStreamInfo
                    {
                        kind = RecordedStreamKind.Broadcast, sample = _session.TypeName(typeof(T))
                    });
                    _broadcasts[typeof(T)] = rows;
                }

                var row = new RecordedBroadcast<T> { connection = conn.connectionId, flags = flags, value = data };
                if ((flags & RecordedBroadcast<T>.ToAll) == 0)
                {
                    if (_playersManager == null)
                        _networkManager.TryGetModule(true, out _playersManager);
                    if (known.HasValue)
                    {
                        if (_playersManager != null && _playersManager.TryGetConnection(known.Value, out var from))
                            row.connection = from.connectionId;
                    }
                    else if (_playersManager != null && _playersManager.TryGetPlayer(conn, out var player))
                        known = player;
                    if (known.HasValue)
                    {
                        row.player = known.Value.id.value;
                        row.flags |= RecordedBroadcast<T>.HasPlayer;
                    }
                }
                _session.AppendEventRow(rows, ref row);
            }
            catch (Exception e)
            {
                _session.AddError("broadcast_record_failed", $"{typeof(T).Name}: {e.GetType().Name}");
            }
            finally
            {
                _session.stats.End(_statCalls);
            }
        }
    }
}

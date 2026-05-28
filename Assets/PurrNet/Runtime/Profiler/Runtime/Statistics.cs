using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Packing;

namespace PurrNet.Profiler
{
    public static class Statistics
    {
        public const int MAX_SAMPLES = 256;

        public static readonly List<TickSample> samples = new ();
        private static TickSample _currentSample = new ();

        public static event Action onSampleEnded;

        public static event Action<TickSample> onSample;

        public static bool paused;

        public static int inspecting;


        public static int aggregating;

        public static bool shouldTrack => !paused && (inspecting > 0 || aggregating > 0);

        private static bool shouldSample => !paused && inspecting > 0;

        private struct Counter
        {
            public long sentCount, sentBytes, recvCount, recvBytes;
        }

        private readonly struct RpcKey : IEquatable<RpcKey>
        {
            public readonly Type type;
            public readonly string method;
            public readonly RPCType rpcType;

            public RpcKey(Type type, RPCType rpcType, string method)
            {
                this.type = type;
                this.rpcType = rpcType;
                this.method = method;
            }

            public bool Equals(RpcKey other) =>
                type == other.type && rpcType == other.rpcType && method == other.method;

            public override bool Equals(object obj) => obj is RpcKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(type, method, (int)rpcType);
        }

        private static readonly Dictionary<RpcKey, Counter> _rpcAgg = new();
        private static readonly Dictionary<Type, Counter> _broadcastAgg = new();
        private static long _forwardedCount;
        private static long _forwardedBytes;

        public static string GetFriendlyTypeName(this Type type)
        {
            if (!type.IsGenericType)
                return type.Name;

            var genericArguments = type.GetGenericArguments();
            var genericTypeName = type.Name;

            // Remove the `n from the generic type name
            int backtickIndex = genericTypeName.IndexOf('`');
            if (backtickIndex > 0)
                genericTypeName = genericTypeName.Substring(0, backtickIndex);

            // Build the generic type name with parameters
            return genericTypeName + "<" +
                   string.Join(", ", genericArguments.Select(GetFriendlyTypeName)) +
                   ">";
        }

        public static void ReceivedBroadcast(Type type, ArraySegment<byte> data)
        {
            if (aggregating > 0)
                AggregateBroadcast(type, data.Count, sent: false);
            if (!shouldSample) return;
            _currentSample.receivedBroadcasts.Add(new BroadcastSample(type, data));
        }

        public static void SentBroadcast(Type type, ArraySegment<byte> data)
        {
            if (aggregating > 0)
                AggregateBroadcast(type, data.Count, sent: true);
            if (!shouldSample) return;
            _currentSample.sentBroadcasts.Add(new BroadcastSample(type, data));
        }

        public static void ForwardedBytes(int bytesSent)
        {
            if (aggregating > 0)
            {
                _forwardedCount++;
                _forwardedBytes += bytesSent;
            }
            if (!shouldSample) return;
            _currentSample.forwardedBytes.Add(bytesSent);
        }

        public static void ReceivedRPC(Type type, RPCType rpcType, string method, BitData data, UnityEngine.Object context)
        {
            if (aggregating > 0)
                AggregateRpc(type, rpcType, method, data.byteLength, sent: false);
            if (!shouldSample) return;
            _currentSample.receivedRpcs.Add(new RpcsSample(type, rpcType, method, data, context));
        }

        public static void SentRPC(Type type, RPCType rpcType, string method, BitData data, UnityEngine.Object context)
        {
            if (aggregating > 0)
                AggregateRpc(type, rpcType, method, data.byteLength, sent: true);
            if (!shouldSample) return;
            _currentSample.sentRpcs.Add(new RpcsSample(type, rpcType, method, data, context));
        }

        public static void MarkEndOfSampling()
        {
            if (!shouldSample) return;

            if (samples.Count >= MAX_SAMPLES)
            {
                samples[0].Dispose();
                samples.RemoveAt(0);
            }

            samples.Add(_currentSample);
            onSample?.Invoke(_currentSample);

            _currentSample = new TickSample();
            onSampleEnded?.Invoke();
        }

        private static void AggregateRpc(Type type, RPCType rpcType, string method, long bytes, bool sent)
        {
            var key = new RpcKey(type, rpcType, method);
            _rpcAgg.TryGetValue(key, out var c);
            if (sent) { c.sentCount++; c.sentBytes += bytes; }
            else { c.recvCount++; c.recvBytes += bytes; }
            _rpcAgg[key] = c;
        }

        private static void AggregateBroadcast(Type type, long bytes, bool sent)
        {
            _broadcastAgg.TryGetValue(type, out var c);
            if (sent) { c.sentCount++; c.sentBytes += bytes; }
            else { c.recvCount++; c.recvBytes += bytes; }
            _broadcastAgg[type] = c;
        }

        public static void BeginAggregation()
        {
            _rpcAgg.Clear();
            _broadcastAgg.Clear();
            _forwardedCount = 0;
            _forwardedBytes = 0;
            aggregating++;
        }

        public static List<BandwidthEntry> EndAggregation()
        {
            if (aggregating > 0)
                aggregating--;

            var list = new List<BandwidthEntry>(_rpcAgg.Count + _broadcastAgg.Count + 1);

            foreach (var kv in _rpcAgg)
            {
                var typeName = kv.Key.type != null ? kv.Key.type.Name : "?";
                list.Add(new BandwidthEntry
                {
                    kind = "RPC",
                    name = $"{typeName}.{kv.Key.method} [{kv.Key.rpcType}]",
                    sentCount = kv.Value.sentCount,
                    sentBytes = kv.Value.sentBytes,
                    recvCount = kv.Value.recvCount,
                    recvBytes = kv.Value.recvBytes
                });
            }

            foreach (var kv in _broadcastAgg)
            {
                list.Add(new BandwidthEntry
                {
                    kind = "Broadcast",
                    name = kv.Key != null ? kv.Key.Name : "?",
                    sentCount = kv.Value.sentCount,
                    sentBytes = kv.Value.sentBytes,
                    recvCount = kv.Value.recvCount,
                    recvBytes = kv.Value.recvBytes
                });
            }

            if (_forwardedCount > 0 || _forwardedBytes > 0)
            {
                list.Add(new BandwidthEntry
                {
                    kind = "Forwarded",
                    name = "(forwarded)",
                    sentCount = _forwardedCount,
                    sentBytes = _forwardedBytes
                });
            }

            list.Sort((a, b) => (b.sentBytes + b.recvBytes).CompareTo(a.sentBytes + a.recvBytes));
            return list;
        }
    }

    [Serializable]
    public struct BandwidthEntry
    {
        public string kind;
        public string name;
        public long sentCount;
        public long sentBytes;
        public long recvCount;
        public long recvBytes;
    }
}

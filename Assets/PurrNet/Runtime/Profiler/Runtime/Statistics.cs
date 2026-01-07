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

        static bool shouldTrack => !paused && inspecting > 0;

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
            if (!shouldTrack) return;
            _currentSample.receivedBroadcasts.Add(new BroadcastSample(type, data));
        }

        public static void SentBroadcast(Type type, ArraySegment<byte> data)
        {
            if (!shouldTrack) return;
            _currentSample.sentBroadcasts.Add(new BroadcastSample(type, data));
        }

        public static void ForwardedBytes(int bytesSent)
        {
            if (!shouldTrack) return;
            _currentSample.forwardedBytes.Add(bytesSent);
        }

        public static void ReceivedRPC(Type type, RPCType rpcType, string method, BitData data, UnityEngine.Object context)
        {
            if (!shouldTrack) return;
            _currentSample.receivedRpcs.Add(new RpcsSample(type, rpcType, method, data, context));
        }

        public static void SentRPC(Type type, RPCType rpcType, string method, BitData data, UnityEngine.Object context)
        {
            if (!shouldTrack) return;
            _currentSample.sentRpcs.Add(new RpcsSample(type, rpcType, method, data, context));
        }

        public static void MarkEndOfSampling()
        {
            if (!shouldTrack) return;

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
    }
}

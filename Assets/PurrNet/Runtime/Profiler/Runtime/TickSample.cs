using System;
using System.Collections.Generic;
using PurrNet.Pooling;

namespace PurrNet.Profiler
{
    public class TickSample : IDisposable
    {
        public readonly List<RpcsSample> receivedRpcs = ListPool<RpcsSample>.Instantiate();
        public readonly List<RpcsSample> sentRpcs = ListPool<RpcsSample>.Instantiate();
        public readonly List<BroadcastSample> receivedBroadcasts = ListPool<BroadcastSample>.Instantiate();
        public readonly List<BroadcastSample> sentBroadcasts = ListPool<BroadcastSample>.Instantiate();
        public readonly List<int> forwardedBytes = ListPool<int>.Instantiate();
        public readonly List<DroppedSample> droppedMessages = ListPool<DroppedSample>.Instantiate();

        public void Dispose()
        {
            for (var i = 0; i < receivedRpcs.Count; i++) receivedRpcs[i].Dispose();
            for (var i = 0; i < sentRpcs.Count; i++) sentRpcs[i].Dispose();
            for (var i = 0; i < receivedBroadcasts.Count; i++) receivedBroadcasts[i].Dispose();
            for (var i = 0; i < sentBroadcasts.Count; i++) sentBroadcasts[i].Dispose();

            ListPool<RpcsSample>.Destroy(receivedRpcs);
            ListPool<RpcsSample>.Destroy(sentRpcs);
            ListPool<BroadcastSample>.Destroy(receivedBroadcasts);
            ListPool<BroadcastSample>.Destroy(sentBroadcasts);
            ListPool<int>.Destroy(forwardedBytes);
            ListPool<DroppedSample>.Destroy(droppedMessages);
        }
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    internal static class AsyncDestroyer
    {
        const int MAX_PENDING = 64;
        static readonly Unity.Profiling.ProfilerMarker _queuedMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.Queued");
        static readonly Unity.Profiling.ProfilerMarker _queueFullMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.QueueFull");
        static readonly Unity.Profiling.ProfilerMarker _tickMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.Tick");
        static readonly Unity.Profiling.ProfilerMarker _completedRootMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.CompletedRoot");
        static readonly Unity.Profiling.ProfilerMarker _prepareWalkMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.Prepare.Walk");
        static readonly Unity.Profiling.ProfilerMarker _prepareDeactivateMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.Prepare.Deactivate");
        static readonly Unity.Profiling.ProfilerMarker _prepareUnparentMarker = new Unity.Profiling.ProfilerMarker("PurrNet.AsyncDestroy.Prepare.Unparent");


        struct Entry
        {
            public GameObject gameObject;
            public float msPerFrame;
            public int frame;
        }

        static readonly List<Entry> _pending = new List<Entry>();
        static bool _subscribed;

        internal static int pendingCount => _pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            _pending.Clear();
            _subscribed = false;
        }

        internal static bool IsPending(GameObject go)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].gameObject == go)
                    return true;
            }

            return false;
        }

        internal static void Enqueue(GameObject go, float msPerFrame)
        {
            if (!go)
                return;

            var trs = go.transform;

            for (var i = 0; i < _pending.Count; i++)
            {
                var pending = _pending[i].gameObject;
                if (pending && trs.IsChildOf(pending.transform))
                    return;
            }

            if (_pending.Count >= MAX_PENDING)
            {
                using (_queueFullMarker.Auto())
                    UnityProxy.DestroyDirectly(go);
                return;
            }

            if (!Application.isPlaying || ApplicationContext.isQuitting ||
                !TryPrepare(go))
            {
                UnityProxy.DestroyDirectly(go);
                return;
            }

            using (_queuedMarker.Auto())
                _pending.Add(new Entry
                {
                    gameObject = go,
                    msPerFrame = msPerFrame,
                    frame = Time.frameCount
                });

            if (_subscribed)
                return;

            _subscribed = true;
            UnityLatestUpdate.onUpdate += Tick;
        }

        static bool TryPrepare(GameObject go)
        {
            var identities = ListPool<NetworkIdentity>.Instantiate();
            using (_prepareWalkMarker.Auto())
                go.GetComponentsInChildren(true, identities);

            for (var i = 0; i < identities.Count; i++)
            {
                if (identities[i].isSpawned)
                {
                    ListPool<NetworkIdentity>.Destroy(identities);
                    return false;
                }
            }

            var trs = go.transform;

            using (_prepareDeactivateMarker.Auto())
            {
                if (go.activeSelf)
                    go.SetActive(false);
            }

            using (_prepareUnparentMarker.Auto())
            {
                if (trs.parent)
                    trs.SetParent(null, false);
            }

            if (go.activeSelf || trs.parent)
            {
                ListPool<NetworkIdentity>.Destroy(identities);
                return false;
            }

            for (var i = 0; i < identities.Count; i++)
                identities[i].PrepareForAsyncDestroy();

            ListPool<NetworkIdentity>.Destroy(identities);

            if (!go.TryGetComponent<PurrNetPoolRoot>(out _))
                go.AddComponent<PurrNetPoolRoot>();

            return true;
        }

        static void Tick()
        {
            using var tickScope = _tickMarker.Auto();
            if (ApplicationContext.isQuitting)
            {
                _pending.Clear();
                Unsubscribe();
                return;
            }

            long start = Stopwatch.GetTimestamp();
            int frame = Time.frameCount;

            while (_pending.Count > 0)
            {
                var entry = _pending[0];

                if (!entry.gameObject)
                {
                    _pending.RemoveAt(0);
                    continue;
                }

                if (entry.frame >= frame)
                    break;

                long budget = (long)(entry.msPerFrame * 0.001 * Stopwatch.Frequency);

                if (!DestroySlice(entry.gameObject, start, budget))
                    break;

                using (_completedRootMarker.Auto())
                    _pending.RemoveAt(0);

                if (Stopwatch.GetTimestamp() - start >= budget)
                    break;
            }

            if (_pending.Count == 0)
                Unsubscribe();
        }

        static void Unsubscribe()
        {
            if (!_subscribed)
                return;

            _subscribed = false;
            UnityLatestUpdate.onUpdate -= Tick;
        }

        static bool DestroySlice(GameObject root, long start, long budget)
        {
            var rootTrs = root.transform;

            do
            {
                var current = rootTrs;
                int childCount = current.childCount;

                while (childCount > 0)
                {
                    current = current.GetChild(childCount - 1);
                    childCount = current.childCount;
                }

                bool isRoot = ReferenceEquals(current, rootTrs);
                var target = current.gameObject;
                UnityProxy.DestroyImmediateDirectly(target);

                if (target)
                {
                    UnityProxy.DestroyDirectly(root);
                    return true;
                }

                if (isRoot)
                    return true;
            }
            while (Stopwatch.GetTimestamp() - start < budget);

            return false;
        }
    }
}

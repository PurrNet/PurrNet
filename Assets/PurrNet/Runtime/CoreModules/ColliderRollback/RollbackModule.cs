using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public partial class RollbackModule : INetworkModule
    {
        readonly TickManager _tickManager;
        readonly HashSet<Component> _trackedColliders = new();

        ulong _lastWrittenTick = ulong.MaxValue;

        const double MaxSampleGapTicks = RollbackBroadphase.ChunkTicks;

#if UNITY_PHYSICS_3D
        PhysicsScene _physicsScene;
        private readonly List<Collider> _colliders3D = new();
        private readonly List<SimpleHistory<Collider3DState>> _histories3D = new();
        private readonly List<TrackedBounds3D> _bounds3D = new();
        readonly Dictionary<Collider, SimpleHistory<Collider3DState>> _collider3DStates = new();
#endif

#if UNITY_PHYSICS_2D
        PhysicsScene2D _physicsScene2D;
        private readonly List<Collider2D> _colliders2D = new();
        private readonly List<SimpleHistory<Collider2DState>> _histories2D = new();
        private readonly List<TrackedBounds2D> _bounds2D = new();
        readonly Dictionary<Collider2D, SimpleHistory<Collider2DState>> _collider2DStates = new();
#endif

        public RollbackModule(TickManager tick, Scene scene)
        {
            _tickManager = tick;
#if UNITY_PHYSICS_3D
            _physicsScene = scene.GetPhysicsScene();
#endif
#if UNITY_PHYSICS_2D
            _physicsScene2D = scene.GetPhysicsScene2D();
#endif
        }

        public void Enable(bool asServer)
        {
        }

        public void Disable(bool asServer)
        {
        }

#if UNITY_PHYSICS_2D
        /// <summary>
        /// Tries to get the state of a collider at a precise tick in the past.
        /// </summary>
        [UsedImplicitly]
        public bool TryGetColliderState(double preciseTick, Collider2D collider, out Collider2DState state)
        {
            if (_collider2DStates.TryGetValue(collider, out var history))
                return Sample(history, preciseTick, out state);

            state = default;
            return false;
        }

        static bool Sample(SimpleHistory<Collider2DState> history, double preciseTick, out Collider2DState state)
        {
            uint tick = (uint)preciseTick;
            float tickFraction = (float)(preciseTick - tick);

            bool hasStateA = history.TryGet(tick, out var stateA);
            bool hasStateB = history.TryGet(tick + 1, out var stateB);

            switch (hasStateA)
            {
                case true when hasStateB:
                    stateA = stateA.Interpolate(stateB, tickFraction);
                    break;
                case false when hasStateB:
                    stateA = stateB;
                    break;
                case false:
                    return SampleClosest(history, preciseTick, out state);
                case true:
                    break;
            }

            state = stateA;
            return true;
        }

        // Ticks skipped during hitch catch-up have no snapshot; reach for the nearest entries
        // within MaxSampleGapTicks instead of silently making the collider unhittable.
        static bool SampleClosest(SimpleHistory<Collider2DState> history, double preciseTick, out Collider2DState state)
        {
            state = default;

            if (history.Count == 0)
                return false;

            history.Find((ulong)preciseTick, out var index);

            bool hasBefore = index > 0;
            bool hasAfter = index < history.Count;

            ulong beforeTick = hasBefore ? history.GetEntryTick(index - 1) : 0;
            ulong afterTick = hasAfter ? history.GetEntryTick(index) : 0;

            double beforeGap = hasBefore ? preciseTick - beforeTick : double.MaxValue;
            double afterGap = hasAfter ? afterTick - preciseTick : double.MaxValue;

            if (beforeGap <= MaxSampleGapTicks && afterGap <= MaxSampleGapTicks)
            {
                float fraction = (float)((preciseTick - beforeTick) / (afterTick - beforeTick));
                state = history[index - 1].Interpolate(history[index], fraction);
                return true;
            }

            if (beforeGap <= MaxSampleGapTicks && beforeGap <= afterGap)
            {
                state = history[index - 1];
                return true;
            }

            if (afterGap <= MaxSampleGapTicks)
            {
                state = history[index];
                return true;
            }

            return false;
        }
#endif


#if UNITY_PHYSICS_3D
        /// <summary>
        /// Tries to get the state of a collider at a precise tick in the past.
        /// </summary>
        [UsedImplicitly]
        public bool TryGetColliderState(double preciseTick, Collider collider, out Collider3DState state)
        {
            if (_collider3DStates.TryGetValue(collider, out var history))
                return Sample(history, preciseTick, out state);

            state = default;
            return false;
        }

        static bool Sample(SimpleHistory<Collider3DState> history, double preciseTick, out Collider3DState state)
        {
            uint tick = (uint)preciseTick;
            float tickFraction = (float)(preciseTick - tick);

            bool hasStateA = history.TryGet(tick, out var stateA);
            bool hasStateB = history.TryGet(tick + 1, out var stateB);

            switch (hasStateA)
            {
                case true when hasStateB:
                    stateA = stateA.Interpolate(stateB, tickFraction);
                    break;
                case false when hasStateB:
                    stateA = stateB;
                    break;
                case false:
                    return SampleClosest(history, preciseTick, out state);
                case true:
                    break;
            }

            state = stateA;
            return true;
        }

        // Ticks skipped during hitch catch-up have no snapshot; reach for the nearest entries
        // within MaxSampleGapTicks instead of silently making the collider unhittable.
        static bool SampleClosest(SimpleHistory<Collider3DState> history, double preciseTick, out Collider3DState state)
        {
            state = default;

            if (history.Count == 0)
                return false;

            history.Find((ulong)preciseTick, out var index);

            bool hasBefore = index > 0;
            bool hasAfter = index < history.Count;

            ulong beforeTick = hasBefore ? history.GetEntryTick(index - 1) : 0;
            ulong afterTick = hasAfter ? history.GetEntryTick(index) : 0;

            double beforeGap = hasBefore ? preciseTick - beforeTick : double.MaxValue;
            double afterGap = hasAfter ? afterTick - preciseTick : double.MaxValue;

            if (beforeGap <= MaxSampleGapTicks && afterGap <= MaxSampleGapTicks)
            {
                float fraction = (float)((preciseTick - beforeTick) / (afterTick - beforeTick));
                state = history[index - 1].Interpolate(history[index], fraction);
                return true;
            }

            if (beforeGap <= MaxSampleGapTicks && beforeGap <= afterGap)
            {
                state = history[index - 1];
                return true;
            }

            if (afterGap <= MaxSampleGapTicks)
            {
                state = history[index];
                return true;
            }

            return false;
        }
#endif

        public void OnPostTick()
        {
            var tick = _tickManager.localTick;

            // on host the server and client factories share the same module instance
            if (tick == _lastWrittenTick)
                return;

            _lastWrittenTick = tick;

#if UNITY_PHYSICS_3D
            for (var i = 0; i < _colliders3D.Count; i++)
            {
                var col = _colliders3D[i];

                if (!col)
                {
                    _collider3DStates.Remove(col);
                    _trackedColliders.Remove(col);
                    _colliders3D.RemoveAt(i);
                    _histories3D.RemoveAt(i);
                    _bounds3D.RemoveAt(i--);
                    continue;
                }

                var state = new Collider3DState(col);
                _histories3D[i].Write(tick, state);
                _bounds3D[i].Record(tick, col, state);
            }
#endif

#if UNITY_PHYSICS_2D
            for (var i = 0; i < _colliders2D.Count; i++)
            {
                var col = _colliders2D[i];

                if (!col)
                {
                    _collider2DStates.Remove(col);
                    _trackedColliders.Remove(col);
                    _colliders2D.RemoveAt(i);
                    _histories2D.RemoveAt(i);
                    _bounds2D.RemoveAt(i--);
                    continue;
                }

                var state = new Collider2DState(col);
                _histories2D[i].Write(tick, state);
                _bounds2D[i].Record(tick, col, state);
            }
#endif
        }

        public void Register(ColliderRollback component)
        {
#if UNITY_PHYSICS_3D
            var colliders3d = component.colliders3D;
            if (colliders3d != null)
            {
                for (var i = 0; i < colliders3d.Length; i++)
                    Register(colliders3d[i], component.storeHistoryInSeconds);
            }
#endif

#if UNITY_PHYSICS_2D
            var colliders2d = component.colliders2D;
            if (colliders2d != null)
            {
                for (var i = 0; i < colliders2d.Length; i++)
                    Register(colliders2d[i], component.storeHistoryInSeconds);
            }
#endif
        }

#if UNITY_PHYSICS_3D
        public void Register(Collider collider, float storeHistoryInSeconds)
        {
            if (!collider)
                return;

            if (!_trackedColliders.Add(collider))
                return;

            int maxEntries = Mathf.CeilToInt(_tickManager.tickRate * storeHistoryInSeconds);
            var history = new SimpleHistory<Collider3DState>(maxEntries);
            _collider3DStates.Add(collider, history);
            _colliders3D.Add(collider);
            _histories3D.Add(history);
            _bounds3D.Add(new TrackedBounds3D(maxEntries));
        }

        public void Unregister(Collider collider)
        {
            if (!collider)
                return;

            if (!_trackedColliders.Remove(collider))
                return;

            _collider3DStates.Remove(collider);

            int index = _colliders3D.IndexOf(collider);
            if (index >= 0)
            {
                _colliders3D.RemoveAt(index);
                _histories3D.RemoveAt(index);
                _bounds3D.RemoveAt(index);
            }
        }
#endif

#if UNITY_PHYSICS_2D
        public void Register(Collider2D collider, float storeHistoryInSeconds)
        {
            if (!collider)
                return;

            if (!_trackedColliders.Add(collider))
                return;

            int maxEntries = Mathf.CeilToInt(_tickManager.tickRate * storeHistoryInSeconds);
            var history = new SimpleHistory<Collider2DState>(maxEntries);
            _collider2DStates.Add(collider, history);
            _colliders2D.Add(collider);
            _histories2D.Add(history);
            _bounds2D.Add(new TrackedBounds2D(maxEntries));
        }

        public void Unregister(Collider2D collider)
        {
            if (!collider)
                return;

            if (!_trackedColliders.Remove(collider))
                return;

            _collider2DStates.Remove(collider);

            int index = _colliders2D.IndexOf(collider);
            if (index >= 0)
            {
                _colliders2D.RemoveAt(index);
                _histories2D.RemoveAt(index);
                _bounds2D.RemoveAt(index);
            }
        }
#endif

        public void Unregister(ColliderRollback component)
        {
#if UNITY_PHYSICS_3D
            var colliders3d = component.colliders3D;
            if (colliders3d != null)
            {
                for (var i = 0; i < colliders3d.Length; i++)
                {
                    if (colliders3d[i])
                        Unregister(colliders3d[i]);
                }
            }
#endif

#if UNITY_PHYSICS_2D
            var colliders2d = component.colliders2D;
            if (colliders2d != null)
            {
                for (var i = 0; i < colliders2d.Length; i++)
                {
                    if (colliders2d[i])
                        Unregister(colliders2d[i]);
                }
            }
#endif
        }
    }
}

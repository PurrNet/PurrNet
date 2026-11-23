using System;
using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Recording
{
    /// <summary>
    /// What an entity looks like changes rarely, and almost always because the game said so: it activated an
    /// object, enabled a renderer or a collider, instantiated or destroyed something. PurrNet's code generation
    /// makes those calls report here (see <see cref="RecordedEffects"/>), so an entity is only looked at again when
    /// one of them touched it. Changes from code that is not woven (the engine, an animation that toggles an
    /// object) are found by the periodic check of every entity instead, a few ticks later.
    /// </summary>
    public sealed partial class RecordingSession
    {
        /// <summary>
        /// An entity nothing happens to is checked less and less often, down to once in this many rounds.
        /// </summary>
        const int MostCheckRounds = 4;

        int _visibleEpoch = 1;
        /// <summary>Hearing from the game failed once: every entity is looked at on every tick again.</summary>
        bool _alwaysLook;
        int _visibleCursor;

        internal void WatchFailed(Exception e)
        {
            _alwaysLook = true;
            AddError("changes", e.GetType().Name + ": " + e.Message);
        }

        /// <summary>
        /// Without the physics module nothing checks entities periodically: this alone has each of them looked at
        /// now and then, for what changed without the game's code saying so.
        /// </summary>
        void SweepVisibility()
        {
            int count = _entities.Count;
            for (int i = Math.Max(1, count / 10); i > 0 && count > 0; i--)
            {
                if (_visibleCursor >= count)
                    _visibleCursor = 0;
                LookAgain(_entities[_visibleCursor++]);
            }
        }

        /// <summary>
        /// One of an entity's periodic checks: its colliders, renderers and effects. An entity nothing has changed
        /// on is checked half as often after each check, until something does.
        /// </summary>
        void CheckEntity(RecordedEntity entity, ulong tick)
        {
#if UNITY_PHYSICS_3D
            if (entity.root && entity.root.hierarchyCount >= SlicedCheckFrom)
            {
                QueueSlicedCheck(entity);
                return;
            }
#endif
            int records = _metaRecords, moving = entity.movingColliders;
            int hierarchy = entity.root ? entity.root.hierarchyCount : -1;
            bool changed = hierarchy != entity.checkHierarchy;
            entity.checkHierarchy = hierarchy;
#if UNITY_PHYSICS_3D
            RefreshEntityColliders(entity, tick);
#else
            if (entity.gameObject)
                RefreshEntityRenderers(entity, tick);
#endif
            changed |= _metaRecords != records || entity.movingColliders != moving;
            entity.checkRounds = changed || entity.movingColliders > 0 ? 1 : Math.Min(entity.checkRounds * 2, MostCheckRounds);
            entity.checkWait = entity.checkRounds - 1;
        }
        /// <summary>
        /// The objects above an entity, and how many entities each is above: activating one of them changes what is
        /// visible below it.
        /// </summary>
        readonly Dictionary<long, int> _entityAncestors = new Dictionary<long, int>(64);
        /// <summary>Graphics an entity is drawn with that are not inside it, by GameObject.</summary>
        readonly Dictionary<long, RecordedEntity> _graphicsRoots = new Dictionary<long, RecordedEntity>();
        readonly List<RecordedEntity> _checkSoon = new List<RecordedEntity>();
        int _metaRecords;

        internal bool watchesObjects => _file != null && !isPart;

        /// <summary>
        /// Whether any of the entity's renderers shows (see <see cref="Visibility"/>), as it was when the entity
        /// was last looked at: again after something touched it, and at each of its periodic checks.
        /// </summary>
        byte VisibleNow(RecordedEntity entity)
        {
            if (entity.visibleEpoch == _visibleEpoch && entity.visibleLooks == 0 && !_alwaysLook)
                return entity.visible;
            if (entity.visibleLooks > 0)
                entity.visibleLooks--;
            entity.visibleEpoch = _visibleEpoch;
            return entity.visible = Visibility(entity);
        }

        /// <summary>The entity is looked at again on its next <paramref name="ticks"/> ticks.</summary>
        void LookAgain(RecordedEntity entity, int ticks = 1)
        {
            if (entity.visibleLooks < ticks)
                entity.visibleLooks = (byte)ticks;
        }

        /// <summary>Its colliders, renderers and effects are checked on the next tick, and often again after.</summary>
        void CheckSoon(RecordedEntity entity)
        {
            entity.checkRounds = 1;
            entity.checkWait = 0;
            if (entity.checkSoon)
                return;
            entity.checkSoon = true;
            _checkSoon.Add(entity);
        }

        void BeginChanges(RecordedEntity entity)
        {
            entity.visibleEpoch = 0;
            entity.visibleLooks = 0;
            entity.checkRounds = 1;
            entity.checkWait = 0;
            entity.checkSoon = false;
            entity.checkHierarchy = entity.root.hierarchyCount;
            for (var t = entity.root.parent; t; t = t.parent)
            {
                long id = RecordingObjectId.Of(t.gameObject);
                _entityAncestors.TryGetValue(id, out int count);
                _entityAncestors[id] = count + 1;
                entity.ancestors.Add(id);
            }
        }

        void EndChanges(RecordedEntity entity)
        {
            foreach (long id in entity.ancestors)
            {
                if (!_entityAncestors.TryGetValue(id, out int count))
                    continue;
                if (count <= 1)
                    _entityAncestors.Remove(id);
                else
                    _entityAncestors[id] = count - 1;
            }
            entity.ancestors.Clear();
            ForgetGraphicsRoots(entity);
            entity.checkSoon = false;
        }

        void ForgetGraphicsRoots(RecordedEntity entity)
        {
            foreach (long id in entity.graphicsRoots)
                if (_graphicsRoots.TryGetValue(id, out var owner) && ReferenceEquals(owner, entity))
                    _graphicsRoots.Remove(id);
            entity.graphicsRoots.Clear();
        }

        void ClearChanges()
        {
            _entityAncestors.Clear();
            _graphicsRoots.Clear();
            _checkSoon.Clear();
            _visibleEpoch = 1;
            _alwaysLook = false;
#if UNITY_PHYSICS_3D
            ResetSlice();
            foreach (var entity in _sliceQueue)
                entity.sliceQueued = false;
            _sliceQueue.Clear();
#endif
        }

#if UNITY_PHYSICS_3D
        /// <summary>
        /// An entity with this many objects in its hierarchy (a whole building, say) is checked a part at a time:
        /// looking at all of it at once, and writing its records, can take several milliseconds.
        /// </summary>
        internal static int SlicedCheckFrom = 256;
        /// <summary>A big entity's check moves on by at least this many steps a tick, over budget or not.</summary>
        const int SlicedCheckMinimumWork = 16;

        enum SliceStep { Walk, Renderers, Effects, ColliderKey, Track, Colliders }

        /// <summary>
        /// The check of one big entity, spread over ticks: its hierarchy is walked, then its RENDERERS record is
        /// written, its effects looked at, its colliders compared and, if they changed, their record written. The
        /// records are the ones a check done at once writes, and carry the tick the check began on.
        /// </summary>
        sealed class SlicedCheck
        {
            public RecordedEntity entity;
            public GameObject root;
            public ulong tick, since;
            public int moving;
            public bool nested, again, changed, wrote;
            public bool rendererKeyTaken, colliderKeyTaken;
            public SliceStep step;
            public int at;
            public readonly List<(Transform transform, bool active)> stack = new List<(Transform, bool)>(64);
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly List<GameObject> rendererObjects = new List<GameObject>();
            public readonly List<Collider> colliders = new List<Collider>();
            public readonly JsonBuilder json = new JsonBuilder();
            public readonly RecordingBuffer key = new RecordingBuffer(256);
            public readonly Dictionary<long, TrackedCollider> byId = new Dictionary<long, TrackedCollider>();
            public readonly List<TrackedCollider> found = new List<TrackedCollider>();
            public Dictionary<long, List<string>> names;
            public List<string> nameTemplate;
            public int nameIndex;
            public bool nameFill;
        }

        readonly SlicedCheck _slice = new SlicedCheck();
        readonly List<RecordedEntity> _sliceQueue = new List<RecordedEntity>();
        readonly List<Renderer> _sliceRenderers = new List<Renderer>();
        readonly List<Collider> _sliceColliders = new List<Collider>();

        void QueueSlicedCheck(RecordedEntity entity)
        {
            if (ReferenceEquals(_slice.entity, entity))
            {
                _slice.again = true;
                return;
            }
            if (entity.sliceQueued)
                return;
            entity.sliceQueued = true;
            _sliceQueue.Add(entity);
        }

        void ContinueSlicedChecks(ulong tick)
        {
            for (int work = 0; work < SlicedCheckMinimumWork || !OverBudget; work++)
            {
                if (_slice.entity == null && !StartSlice(tick))
                    return;
                SliceStepOnce();
            }
        }

        /// <summary>The recording ends: the check under way is done at once.</summary>
        void FinishSlicedCheck()
        {
            while (_slice.entity != null)
                SliceStepOnce();
            foreach (var entity in _sliceQueue)
                entity.sliceQueued = false;
            _sliceQueue.Clear();
        }

        bool StartSlice(ulong tick)
        {
            while (_sliceQueue.Count > 0)
            {
                var entity = _sliceQueue[0];
                _sliceQueue.RemoveAt(0);
                if (!entity.sliceQueued)
                    continue;
                var root = entity.gameObject;
                if (!root || !entity.root)
                {
                    entity.sliceQueued = false;
                    continue;
                }

                var s = _slice;
                ResetSlice();
                s.entity = entity;
                s.root = root;
                s.tick = tick;
                s.since = entity.shapeSince != 0 ? entity.shapeSince : tick;
                s.moving = entity.movingColliders;
                int hierarchy = entity.root.hierarchyCount;
                s.changed = hierarchy != entity.checkHierarchy;
                entity.checkHierarchy = hierarchy;
                s.nested = HasNestedEntities(entity);
                s.step = SliceStep.Walk;
                s.stack.Add((entity.root, root.activeInHierarchy));
                return true;
            }
            return false;
        }

        void ResetSlice()
        {
            var s = _slice;
            s.entity = null;
            s.root = null;
            s.again = s.changed = s.wrote = s.rendererKeyTaken = s.colliderKeyTaken = false;
            s.at = 0;
            s.stack.Clear();
            s.renderers.Clear();
            s.rendererObjects.Clear();
            s.colliders.Clear();
            s.byId.Clear();
            s.found.Clear();
            s.names = null;
            s.nameTemplate = null;
        }

        void SaveNames(SlicedCheck s)
        {
            s.nameTemplate = _nameTemplate;
            s.nameIndex = _nameIndex;
            s.nameFill = _nameFill;
            _nameTemplate = null;
        }

        void RestoreNames(SlicedCheck s)
        {
            _nameTemplate = s.nameTemplate;
            _nameIndex = s.nameIndex;
            _nameFill = s.nameFill;
        }

        /// <summary>One step of the big entity's check: one object of its hierarchy, or one part of a record.</summary>
        void SliceStepOnce()
        {
            var s = _slice;
            var entity = s.entity;
            if (!s.root || !entity.root)
            {
                AbortSlice(false);
                return;
            }

            switch (s.step)
            {
                case SliceStep.Walk:
                {
                    if (s.stack.Count > 0)
                    {
                        var (t, active) = s.stack[s.stack.Count - 1];
                        s.stack.RemoveAt(s.stack.Count - 1);
                        if (!t)
                            return;
                        t.GetComponents(_sliceRenderers);
                        foreach (var renderer in _sliceRenderers)
                            AddEntityRenderer(s.renderers, s.rendererObjects, renderer, s.root, s.nested);
                        _sliceRenderers.Clear();
                        if (active)
                        {
                            t.GetComponents(_sliceColliders);
                            foreach (var collider in _sliceColliders)
                                if (collider.enabled && !IsLocalPhysics(collider) && (!s.nested || OwnedBy(collider, s.root)))
                                    s.colliders.Add(collider);
                            _sliceColliders.Clear();
                        }
                        for (int c = t.childCount - 1; c >= 0; c--)
                        {
                            var child = t.GetChild(c);
                            s.stack.Add((child, active && child.gameObject.activeSelf));
                        }
                        return;
                    }

                    entity.renderers.Clear();
                    entity.rendererObjects.Clear();
                    entity.renderers.AddRange(s.renderers);
                    entity.rendererObjects.AddRange(s.rendererObjects);
                    AddDetachedGraphics(entity);
                    if (RenderersChanged(entity))
                    {
                        s.rendererKeyTaken = true;
                        BeginRenderersRecord(s.json, entity, s.since);
                        s.names = _rendererNames;
                        SaveNames(s);
                        s.at = 0;
                        s.step = SliceStep.Renderers;
                    }
                    else
                        s.step = SliceStep.Effects;
                    return;
                }
                case SliceStep.Renderers:
                {
                    if (s.at < entity.renderers.Count)
                    {
                        var renderer = entity.renderers[s.at++];
                        if (!renderer)
                        {
                            AbortSlice(true);
                            return;
                        }
                        RestoreNames(s);
                        WriteRenderer(s.json, renderer, entity.root.worldToLocalMatrix);
                        SaveNames(s);
                        return;
                    }
                    RestoreNames(s);
                    EndRenderersRecord(s.json);
                    s.rendererKeyTaken = false;
                    s.names = null;
                    s.wrote = true;
                    s.step = SliceStep.Effects;
                    return;
                }
                case SliceStep.Effects:
                    RefreshEntityEffects(entity, s.nested, s.tick);
                    s.key.Clear();
                    s.at = 0;
                    s.step = SliceStep.ColliderKey;
                    return;
                case SliceStep.ColliderKey:
                {
                    if (s.at < s.colliders.Count)
                    {
                        var collider = s.colliders[s.at++];
                        if (!collider)
                        {
                            AbortSlice(true);
                            return;
                        }
                        ShapeKey(s.key, collider);
                        return;
                    }
                    if (!ColliderKeyChanged(s.key, entity.colliderKey, ref entity.hasColliderKey))
                    {
                        CheckColliderPoses(entity.colliders, 0, entity.colliders.Count, entity.root.worldToLocalMatrix,
                            ref entity.movingColliders, entity.id);
                        FinishSlice();
                        return;
                    }
                    s.colliderKeyTaken = true;
                    BeginTracking(entity.colliders, s.byId, s.found);
                    s.at = 0;
                    s.step = SliceStep.Track;
                    return;
                }
                case SliceStep.Track:
                {
                    if (s.at < s.colliders.Count)
                    {
                        var collider = s.colliders[s.at++];
                        if (!collider)
                        {
                            AbortSlice(true);
                            return;
                        }
                        Track(s.byId, s.found, collider, entity.root, entity.root.worldToLocalMatrix);
                        return;
                    }
                    EndTracking(entity.colliders, s.byId, s.found, ref entity.movingColliders);
                    BeginCollidersRecord(s.json, entity.id, entity.template, s.colliders.Count, s.since);
                    s.names = _colliderNames;
                    SaveNames(s);
                    s.at = 0;
                    s.step = SliceStep.Colliders;
                    return;
                }
                case SliceStep.Colliders:
                {
                    if (s.at < s.colliders.Count)
                    {
                        var collider = s.colliders[s.at++];
                        if (!collider)
                        {
                            AbortSlice(true);
                            return;
                        }
                        RestoreNames(s);
                        WriteCollider(s.json, collider, entity.root.worldToLocalMatrix);
                        SaveNames(s);
                        return;
                    }
                    RestoreNames(s);
                    EndCollidersRecord(s.json);
                    s.colliderKeyTaken = false;
                    s.names = null;
                    s.wrote = true;
                    FinishSlice();
                    return;
                }
            }
        }

        void FinishSlice()
        {
            var s = _slice;
            var entity = s.entity;
            entity.shapeSince = 0;
            bool changed = s.changed || s.wrote || entity.movingColliders != s.moving;
            entity.checkRounds = changed || entity.movingColliders > 0 ? 1 : Math.Min(entity.checkRounds * 2, MostCheckRounds);
            entity.checkWait = entity.checkRounds - 1;
            entity.sliceQueued = false;
            bool again = s.again;
            ResetSlice();
            if (again)
                CheckSoon(entity);
        }

        /// <summary>
        /// The check cannot go on: the entity is gone, or a part of it was destroyed while its record was being
        /// written. What was taken as recorded is undone, so that the next check writes it.
        /// </summary>
        void AbortSlice(bool checkAgain)
        {
            var s = _slice;
            var entity = s.entity;
            if (entity == null)
                return;
            if (s.step == SliceStep.Track)
                EndTracking(entity.colliders, s.byId, s.found, ref entity.movingColliders);
            if (s.rendererKeyTaken)
                entity.rendererKey ^= 1;
            if (s.colliderKeyTaken)
            {
                entity.colliderKey.Clear();
                entity.colliderKey.WriteByte(0xEE);
                entity.hasColliderKey = true;
            }
            if (s.names != null && s.nameFill && s.nameTemplate != null)
                s.names.Remove(entity.template);
            _nameTemplate = null;
            entity.sliceQueued = false;
            ResetSlice();
            if (checkAgain && entity.gameObject)
                CheckSoon(entity);
        }
#endif

        /// <summary>The entity <paramref name="t"/> belongs to: the nearest one on or above it, or the one whose graphics it is part of.</summary>
        RecordedEntity EntityOf(Transform t)
        {
            bool graphics = _graphicsRoots.Count > 0;
            for (; t; t = t.parent)
            {
                long id = RecordingObjectId.Of(t.gameObject);
                if (_entitiesByGameObject.TryGetValue(id, out var entity) || (graphics && _graphicsRoots.TryGetValue(id, out entity)))
                    return entity;
            }
            return null;
        }

        /// <summary>The game activated or deactivated an object.</summary>
        internal void ActiveChanged(GameObject go)
        {
            if (!watchesObjects || _entities.Count == 0)
                return;
            var entity = EntityOf(go.transform);
            if (entity != null)
            {
                LookAgain(entity);
                CheckSoon(entity);
            }
            if (_entityAncestors.Count > 0 && _entityAncestors.ContainsKey(RecordingObjectId.Of(go)) && ++_visibleEpoch == 0)
                _visibleEpoch = 1;
        }

        /// <summary>The game enabled or disabled a renderer.</summary>
        internal void RendererChanged(Renderer renderer)
        {
            if (!watchesObjects || _entities.Count == 0)
                return;
            var entity = EntityOf(renderer.transform);
            if (entity != null)
                LookAgain(entity);
        }

        /// <summary>The game enabled or disabled a collider, or changed something else its entity is checked for.</summary>
        internal void ShapeChanged(Component component)
        {
            if (!watchesObjects || _entities.Count == 0)
                return;
            var entity = EntityOf(component.transform);
            if (entity != null)
                CheckSoon(entity);
        }

        /// <summary>
        /// The game destroys an object or a component. It is gone at the end of the frame: its entity is looked at
        /// on this tick and the next.
        /// </summary>
        internal void PartDestroyed(UnityEngine.Object instance)
        {
            if (!watchesObjects || _entities.Count == 0)
                return;
            var t = instance is GameObject go ? go.transform : instance is Component component ? component.transform : null;
            if (!t)
                return;
            var entity = EntityOf(t);
            if (entity == null)
                return;
            LookAgain(entity, 2);
            CheckSoon(entity);
        }
    }
}

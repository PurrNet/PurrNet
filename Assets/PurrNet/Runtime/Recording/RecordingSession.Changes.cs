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
        }

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

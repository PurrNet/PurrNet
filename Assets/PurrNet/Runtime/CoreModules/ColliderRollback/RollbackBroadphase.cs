using UnityEngine;

namespace PurrNet.Modules
{
    // Per-collider historical broadphase: a cached bounding sphere (radius around the transform
    // pivot, rotation-invariant, refreshed only when scale changes) plus a ring of time-chunked
    // AABBs of the pivot position. Queries slab-test the chunk AABB inflated by radius + cast
    // inflation using pure math — no interop, no history lookup — before any narrow-phase work.
    // A chunk with no recorded data answers conservatively (true) and lets the history sample decide.
    internal static class RollbackBroadphase
    {
        public const uint ChunkTicks = 8;

        public static bool Slab(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (dir > -1e-8f && dir < 1e-8f)
                return origin >= min && origin <= max;

            float inv = 1f / dir;
            float t0 = (min - origin) * inv;
            float t1 = (max - origin) * inv;

            if (t0 > t1)
                (t0, t1) = (t1, t0);

            if (t0 > tMin) tMin = t0;
            if (t1 < tMax) tMax = t1;
            return tMin <= tMax;
        }
    }

#if UNITY_PHYSICS_3D
    internal sealed class TrackedBounds3D
    {
        float _radius;
        bool _hasRadius;
        Vector3 _lastScale;

        readonly uint[] _chunkKeys;
        readonly Vector3[] _chunkMin;
        readonly Vector3[] _chunkMax;

        public TrackedBounds3D(int maxEntries)
        {
            int ringLength = Mathf.Max(2, maxEntries / (int)RollbackBroadphase.ChunkTicks + 2);
            _chunkKeys = new uint[ringLength];
            _chunkMin = new Vector3[ringLength];
            _chunkMax = new Vector3[ringLength];

            for (var i = 0; i < ringLength; i++)
                _chunkKeys[i] = uint.MaxValue;
        }

        public void Record(uint tick, Collider col, in Collider3DState state)
        {
            if (state.enabled && (!_hasRadius || _lastScale != state.scale))
            {
                var bounds = col.bounds;
                _radius = bounds.extents.magnitude + (bounds.center - state.position).magnitude;
                _lastScale = state.scale;
                _hasRadius = true;
            }

            uint chunk = tick / RollbackBroadphase.ChunkTicks;
            int slot = (int)(chunk % (uint)_chunkKeys.Length);

            if (_chunkKeys[slot] != chunk)
            {
                _chunkKeys[slot] = chunk;
                _chunkMin[slot] = state.position;
                _chunkMax[slot] = state.position;
            }
            else
            {
                _chunkMin[slot] = Vector3.Min(_chunkMin[slot], state.position);
                _chunkMax[slot] = Vector3.Max(_chunkMax[slot], state.position);
            }
        }

        bool TryGetChunk(uint chunk, out Vector3 min, out Vector3 max)
        {
            int slot = (int)(chunk % (uint)_chunkKeys.Length);
            if (_chunkKeys[slot] != chunk)
            {
                min = default;
                max = default;
                return false;
            }

            min = _chunkMin[slot];
            max = _chunkMax[slot];
            return true;
        }

        public bool MayHitRay(double preciseTick, Ray ray, float maxDistance, float inflation)
        {
            if (!_hasRadius)
                return true;

            float r = _radius + inflation;
            uint tick = (uint)preciseTick;
            uint chunkA = tick / RollbackBroadphase.ChunkTicks;
            uint chunkB = (tick + 1) / RollbackBroadphase.ChunkTicks;
            bool anyValid = false;

            if (TryGetChunk(chunkA, out var min, out var max))
            {
                anyValid = true;
                if (RayAabb(ray, maxDistance, min, max, r))
                    return true;
            }

            if (chunkB != chunkA && TryGetChunk(chunkB, out min, out max))
            {
                anyValid = true;
                if (RayAabb(ray, maxDistance, min, max, r))
                    return true;
            }

            return !anyValid;
        }

        public bool MayOverlap(double preciseTick, Vector3 origin, float inflation)
        {
            if (!_hasRadius)
                return true;

            float r = _radius + inflation;
            uint tick = (uint)preciseTick;
            uint chunkA = tick / RollbackBroadphase.ChunkTicks;
            uint chunkB = (tick + 1) / RollbackBroadphase.ChunkTicks;
            bool anyValid = false;

            if (TryGetChunk(chunkA, out var min, out var max))
            {
                anyValid = true;
                if (PointAabb(origin, min, max, r))
                    return true;
            }

            if (chunkB != chunkA && TryGetChunk(chunkB, out min, out max))
            {
                anyValid = true;
                if (PointAabb(origin, min, max, r))
                    return true;
            }

            return !anyValid;
        }

        static bool RayAabb(Ray ray, float maxDistance, Vector3 min, Vector3 max, float r)
        {
            var o = ray.origin;
            var d = ray.direction;

            float tMin = 0f, tMax = maxDistance;

            return RollbackBroadphase.Slab(o.x, d.x, min.x - r, max.x + r, ref tMin, ref tMax) &&
                   RollbackBroadphase.Slab(o.y, d.y, min.y - r, max.y + r, ref tMin, ref tMax) &&
                   RollbackBroadphase.Slab(o.z, d.z, min.z - r, max.z + r, ref tMin, ref tMax);
        }

        static bool PointAabb(Vector3 point, Vector3 min, Vector3 max, float r)
        {
            float dx = Mathf.Max(Mathf.Max(min.x - point.x, point.x - max.x), 0f);
            float dy = Mathf.Max(Mathf.Max(min.y - point.y, point.y - max.y), 0f);
            float dz = Mathf.Max(Mathf.Max(min.z - point.z, point.z - max.z), 0f);
            return dx * dx + dy * dy + dz * dz <= r * r;
        }
    }
#endif

#if UNITY_PHYSICS_2D
    internal sealed class TrackedBounds2D
    {
        float _radius;
        bool _hasRadius;
        Vector2 _lastScale;

        readonly uint[] _chunkKeys;
        readonly Vector2[] _chunkMin;
        readonly Vector2[] _chunkMax;

        public TrackedBounds2D(int maxEntries)
        {
            int ringLength = Mathf.Max(2, maxEntries / (int)RollbackBroadphase.ChunkTicks + 2);
            _chunkKeys = new uint[ringLength];
            _chunkMin = new Vector2[ringLength];
            _chunkMax = new Vector2[ringLength];

            for (var i = 0; i < ringLength; i++)
                _chunkKeys[i] = uint.MaxValue;
        }

        public void Record(uint tick, Collider2D col, in Collider2DState state)
        {
            if (state.enabled && (!_hasRadius || _lastScale != state.scale))
            {
                var bounds = col.bounds;
                var extents = (Vector2)bounds.extents;
                var offset = (Vector2)bounds.center - state.position;
                _radius = extents.magnitude + offset.magnitude;
                _lastScale = state.scale;
                _hasRadius = true;
            }

            uint chunk = tick / RollbackBroadphase.ChunkTicks;
            int slot = (int)(chunk % (uint)_chunkKeys.Length);

            if (_chunkKeys[slot] != chunk)
            {
                _chunkKeys[slot] = chunk;
                _chunkMin[slot] = state.position;
                _chunkMax[slot] = state.position;
            }
            else
            {
                _chunkMin[slot] = Vector2.Min(_chunkMin[slot], state.position);
                _chunkMax[slot] = Vector2.Max(_chunkMax[slot], state.position);
            }
        }

        bool TryGetChunk(uint chunk, out Vector2 min, out Vector2 max)
        {
            int slot = (int)(chunk % (uint)_chunkKeys.Length);
            if (_chunkKeys[slot] != chunk)
            {
                min = default;
                max = default;
                return false;
            }

            min = _chunkMin[slot];
            max = _chunkMax[slot];
            return true;
        }

        public bool MayHitRay(double preciseTick, Ray2D ray, float maxDistance, float inflation)
        {
            if (!_hasRadius)
                return true;

            float r = _radius + inflation;
            uint tick = (uint)preciseTick;
            uint chunkA = tick / RollbackBroadphase.ChunkTicks;
            uint chunkB = (tick + 1) / RollbackBroadphase.ChunkTicks;
            bool anyValid = false;

            if (TryGetChunk(chunkA, out var min, out var max))
            {
                anyValid = true;
                if (RayAabb(ray, maxDistance, min, max, r))
                    return true;
            }

            if (chunkB != chunkA && TryGetChunk(chunkB, out min, out max))
            {
                anyValid = true;
                if (RayAabb(ray, maxDistance, min, max, r))
                    return true;
            }

            return !anyValid;
        }

        public bool MayOverlap(double preciseTick, Vector2 origin, float inflation)
        {
            if (!_hasRadius)
                return true;

            float r = _radius + inflation;
            uint tick = (uint)preciseTick;
            uint chunkA = tick / RollbackBroadphase.ChunkTicks;
            uint chunkB = (tick + 1) / RollbackBroadphase.ChunkTicks;
            bool anyValid = false;

            if (TryGetChunk(chunkA, out var min, out var max))
            {
                anyValid = true;
                if (PointAabb(origin, min, max, r))
                    return true;
            }

            if (chunkB != chunkA && TryGetChunk(chunkB, out min, out max))
            {
                anyValid = true;
                if (PointAabb(origin, min, max, r))
                    return true;
            }

            return !anyValid;
        }

        static bool RayAabb(Ray2D ray, float maxDistance, Vector2 min, Vector2 max, float r)
        {
            var o = ray.origin;
            var d = ray.direction;

            float tMin = 0f, tMax = maxDistance;

            return RollbackBroadphase.Slab(o.x, d.x, min.x - r, max.x + r, ref tMin, ref tMax) &&
                   RollbackBroadphase.Slab(o.y, d.y, min.y - r, max.y + r, ref tMin, ref tMax);
        }

        static bool PointAabb(Vector2 point, Vector2 min, Vector2 max, float r)
        {
            float dx = Mathf.Max(Mathf.Max(min.x - point.x, point.x - max.x), 0f);
            float dy = Mathf.Max(Mathf.Max(min.y - point.y, point.y - max.y), 0f);
            return dx * dx + dy * dy <= r * r;
        }
    }
#endif
}

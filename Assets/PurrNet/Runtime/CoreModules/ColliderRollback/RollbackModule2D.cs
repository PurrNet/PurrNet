#if UNITY_PHYSICS_2D

using UnityEngine;

namespace PurrNet.Modules
{
    public partial class RollbackModule
    {
        static readonly RaycastHit2D[] _raycastHits2D = new RaycastHit2D[1024];
        static readonly RaycastHit2D[] _raycastHits2DCache = new RaycastHit2D[1024];

        /// <summary>
        /// Casts a ray, from point origin, in direction direction, of length maxDistance, against all colliders in the scene.
        /// </summary>
        public int Raycast(double preciseTick, Ray2D ray, RaycastHit2D[] raycastHits,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.Raycast(ray.origin, ray.direction, maxDistance, contactFilter, raycastHits);
            int colliderCount = _colliders2D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, raycastHits);

            // handle raycast hits manually
            hitCount = DoManualRaycasts(ray, raycastHits, maxDistance, colliderCount, hitCount, preciseTick,
                contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Casts a ray, from point origin, in direction direction, of length maxDistance, against all colliders in the scene.
        /// </summary>
        public bool Raycast(double preciseTick, Ray2D ray, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = Raycast(preciseTick, ray, _raycastHits2D, maxDistance, contactFilter);

            // return the closest hit
            if (hitCount > 0)
            {
                hit = _raycastHits2D[0];
                for (var i = 1; i < hitCount; i++)
                {
                    if (_raycastHits2D[i].distance < hit.distance)
                        hit = _raycastHits2D[i];
                }

                return true;
            }

            hit = default;
            return false;
        }

        private bool RaycastOnly(Collider2D target, Ray2D ray, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = _physicsScene2D.Raycast(ray.origin, ray.direction, maxDistance, contactFilter,
                _raycastHits2DCache);

            hit = default;
            bool found = false;

            // return the closest hit on the target collider
            for (var i = 0; i < hitCount; i++)
            {
                var result = _raycastHits2DCache[i];

                if (result.collider != target)
                    continue;

                if (!found || result.distance < hit.distance)
                {
                    hit = result;
                    found = true;
                }
            }

            return found;
        }

        private int DoManualRaycasts(Ray2D ray, RaycastHit2D[] hits, float maxDistance, int colliderCount,
            int hitCount, double preciseTick, ContactFilter2D contactFilter)
        {
            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayHitRay(preciseTick, ray, maxDistance, 0f))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                var invRotation = Quaternion.Euler(0, 0, -state.rotation);
                var localOrigin = invRotation * (Vector3)(ray.origin - state.position);
                var localDir = invRotation * (Vector3)ray.direction;
                localOrigin = new Vector3(localOrigin.x / state.scale.x, localOrigin.y / state.scale.y, 0f);
                localDir = new Vector3(localDir.x / state.scale.x, localDir.y / state.scale.y, 0f);

                var currentWorldMatrix = trs.localToWorldMatrix;
                var rayCurrentWorld = new Ray2D(
                    currentWorldMatrix.MultiplyPoint3x4(localOrigin),
                    currentWorldMatrix.MultiplyVector(localDir)
                );

                if (RaycastOnly(col, rayCurrentWorld, out var hit, maxDistance, contactFilter))
                    hits[hitCount++] = hit;
            }

            return hitCount;
        }

        static bool PassesFilters(Collider2D col, ContactFilter2D filter)
        {
            if (!filter.isFiltering)
                return true;

            return !filter.IsFilteringTrigger(col) &&
                   !filter.IsFilteringLayerMask(col.gameObject);
        }

        private int FilterColliders(int hitCount, RaycastHit2D[] hits)
        {
            for (var i = 0; i < hitCount; i++)
            {
                var col = hits[i].collider;
                if (col && _trackedColliders.Contains(col))
                    hits[i--] = hits[--hitCount];
            }

            return hitCount;
        }
    }
}

#endif

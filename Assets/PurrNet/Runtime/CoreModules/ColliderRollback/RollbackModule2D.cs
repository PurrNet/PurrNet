#if UNITY_PHYSICS_2D

using UnityEngine;

namespace PurrNet.Modules
{
    public partial class RollbackModule
    {
        static readonly RaycastHit2D[] _raycastHits2D = new RaycastHit2D[1024];
        static readonly RaycastHit2D[] _raycastHits2DCache = new RaycastHit2D[1024];
        static readonly Collider2D[] _colliderHits2D = new Collider2D[1024];
        static readonly Collider2D[] _collider2DCache = new Collider2D[1024];

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
            return SelectClosestHit(_raycastHits2D, hitCount, out hit);
        }

        /// <summary>
        /// Casts a circle along the ray against all colliders in the scene.
        /// </summary>
        public int CircleCast(double preciseTick, Ray2D ray, float radius, RaycastHit2D[] raycastHits,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.CircleCast(ray.origin, radius, ray.direction, maxDistance, contactFilter,
                raycastHits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, raycastHits);
            hitCount = DoManualCircleCasts(ray, radius, raycastHits, maxDistance, colliderCount, hitCount, preciseTick,
                contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Casts a circle along the ray against all colliders in the scene.
        /// </summary>
        public bool CircleCast(double preciseTick, Ray2D ray, float radius, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = CircleCast(preciseTick, ray, radius, _raycastHits2D, maxDistance, contactFilter);
            return SelectClosestHit(_raycastHits2D, hitCount, out hit);
        }

        /// <summary>
        /// Casts a box along the ray against all colliders in the scene.
        /// </summary>
        public int BoxCast(double preciseTick, Ray2D ray, Vector2 size, float angle, RaycastHit2D[] raycastHits,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.BoxCast(ray.origin, size, angle, ray.direction, maxDistance, contactFilter,
                raycastHits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, raycastHits);
            hitCount = DoManualBoxCasts(ray, size, angle, raycastHits, maxDistance, colliderCount, hitCount,
                preciseTick, contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Casts a box along the ray against all colliders in the scene.
        /// </summary>
        public bool BoxCast(double preciseTick, Ray2D ray, Vector2 size, float angle, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = BoxCast(preciseTick, ray, size, angle, _raycastHits2D, maxDistance, contactFilter);
            return SelectClosestHit(_raycastHits2D, hitCount, out hit);
        }

        /// <summary>
        /// Casts a capsule along the ray against all colliders in the scene.
        /// </summary>
        public int CapsuleCast(double preciseTick, Ray2D ray, Vector2 size, CapsuleDirection2D capsuleDirection,
            float angle, RaycastHit2D[] raycastHits,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.CapsuleCast(ray.origin, size, capsuleDirection, angle, ray.direction,
                maxDistance, contactFilter, raycastHits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, raycastHits);
            hitCount = DoManualCapsuleCasts(ray, size, capsuleDirection, angle, raycastHits, maxDistance, colliderCount,
                hitCount, preciseTick, contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Casts a capsule along the ray against all colliders in the scene.
        /// </summary>
        public bool CapsuleCast(double preciseTick, Ray2D ray, Vector2 size, CapsuleDirection2D capsuleDirection,
            float angle, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = CapsuleCast(preciseTick, ray, size, capsuleDirection, angle, _raycastHits2D, maxDistance,
                contactFilter);
            return SelectClosestHit(_raycastHits2D, hitCount, out hit);
        }

        /// <summary>
        /// Performs a circle overlap check at the given position with the given radius.
        /// </summary>
        public int CircleOverlap(double preciseTick, Vector2 origin, float radius, Collider2D[] hits,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.OverlapCircle(origin, radius, contactFilter, hits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, hits);
            hitCount = DoManualCircleOverlaps(origin, radius, hits, colliderCount, hitCount, preciseTick,
                contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Performs a circle overlap check at the given position with the given radius.
        /// </summary>
        public bool CheckCircle(double preciseTick, Vector2 origin, float radius,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return false;

            return CircleOverlap(preciseTick, origin, radius, _colliderHits2D, contactFilter) > 0;
        }

        /// <summary>
        /// Performs a box overlap check at the given position.
        /// </summary>
        public int BoxOverlap(double preciseTick, Vector2 origin, Vector2 size, float angle, Collider2D[] hits,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.OverlapBox(origin, size, angle, contactFilter, hits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, hits);
            hitCount = DoManualBoxOverlaps(origin, size, angle, hits, colliderCount, hitCount, preciseTick,
                contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Performs a box overlap check at the given position.
        /// </summary>
        public bool CheckBox(double preciseTick, Vector2 origin, Vector2 size, float angle,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return false;

            return BoxOverlap(preciseTick, origin, size, angle, _colliderHits2D, contactFilter) > 0;
        }

        /// <summary>
        /// Performs a capsule overlap check at the given position.
        /// </summary>
        public int CapsuleOverlap(double preciseTick, Vector2 origin, Vector2 size,
            CapsuleDirection2D capsuleDirection, float angle, Collider2D[] hits,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return 0;

            int hitCount = _physicsScene2D.OverlapCapsule(origin, size, capsuleDirection, angle, contactFilter, hits);
            int colliderCount = _colliders2D.Count;

            hitCount = FilterColliders(hitCount, hits);
            hitCount = DoManualCapsuleOverlaps(origin, size, capsuleDirection, angle, hits, colliderCount, hitCount,
                preciseTick, contactFilter);

            return hitCount;
        }

        /// <summary>
        /// Performs a capsule overlap check at the given position.
        /// </summary>
        public bool CheckCapsule(double preciseTick, Vector2 origin, Vector2 size,
            CapsuleDirection2D capsuleDirection, float angle,
            ContactFilter2D contactFilter = default)
        {
            if (!_physicsScene2D.IsValid())
                return false;

            return CapsuleOverlap(preciseTick, origin, size, capsuleDirection, angle, _colliderHits2D, contactFilter) > 0;
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
                TransformRay(ray, state, trs, out var rayCurrentWorld);

                if (RaycastOnly(col, rayCurrentWorld, out var hit, maxDistance, contactFilter))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualCircleCasts(Ray2D ray, float radius, RaycastHit2D[] hits, float maxDistance,
            int colliderCount, int hitCount, double preciseTick, ContactFilter2D contactFilter)
        {
            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayHitRay(preciseTick, ray, maxDistance, radius))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var rayCurrentWorld);

                if (CircleCastOnly(col, rayCurrentWorld, radius, out var hit, maxDistance, contactFilter))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualBoxCasts(Ray2D ray, Vector2 size, float angle, RaycastHit2D[] hits, float maxDistance,
            int colliderCount, int hitCount, double preciseTick, ContactFilter2D contactFilter)
        {
            float inflation = (size * 0.5f).magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayHitRay(preciseTick, ray, maxDistance, inflation))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var rayCurrentWorld);
                float transformedAngle = angle + trs.eulerAngles.z - state.rotation;

                if (BoxCastOnly(col, rayCurrentWorld, size, transformedAngle, out var hit, maxDistance, contactFilter))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualCapsuleCasts(Ray2D ray, Vector2 size, CapsuleDirection2D capsuleDirection, float angle,
            RaycastHit2D[] hits, float maxDistance, int colliderCount, int hitCount, double preciseTick,
            ContactFilter2D contactFilter)
        {
            float inflation = (size * 0.5f).magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayHitRay(preciseTick, ray, maxDistance, inflation))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var rayCurrentWorld);
                float transformedAngle = angle + trs.eulerAngles.z - state.rotation;

                if (CapsuleCastOnly(col, rayCurrentWorld, size, capsuleDirection, transformedAngle, out var hit,
                        maxDistance, contactFilter))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualCircleOverlaps(Vector2 origin, float radius, Collider2D[] hits, int colliderCount,
            int hitCount, double preciseTick, ContactFilter2D contactFilter)
        {
            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayOverlap(preciseTick, origin, radius))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformPos(origin, state, trs, out var transformedOrigin);

                if (OverlapCircleOnly(col, transformedOrigin, radius, contactFilter))
                    hits[hitCount++] = col;
            }

            return hitCount;
        }

        private int DoManualBoxOverlaps(Vector2 origin, Vector2 size, float angle, Collider2D[] hits,
            int colliderCount, int hitCount, double preciseTick, ContactFilter2D contactFilter)
        {
            float inflation = (size * 0.5f).magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayOverlap(preciseTick, origin, inflation))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformPos(origin, state, trs, out var transformedOrigin);
                float transformedAngle = angle + trs.eulerAngles.z - state.rotation;

                if (OverlapBoxOnly(col, transformedOrigin, size, transformedAngle, contactFilter))
                    hits[hitCount++] = col;
            }

            return hitCount;
        }

        private int DoManualCapsuleOverlaps(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection,
            float angle, Collider2D[] hits, int colliderCount, int hitCount, double preciseTick,
            ContactFilter2D contactFilter)
        {
            float inflation = (size * 0.5f).magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders2D[i];
                if (!col)
                    continue;

                if (!_bounds2D[i].MayOverlap(preciseTick, origin, inflation))
                    continue;

                if (!PassesFilters(col, contactFilter))
                    continue;

                if (!Sample(_histories2D[i], preciseTick, out var state) || !state.enabled)
                    continue;

                var trs = col.transform;
                TransformPos(origin, state, trs, out var transformedOrigin);
                float transformedAngle = angle + trs.eulerAngles.z - state.rotation;

                if (OverlapCapsuleOnly(col, transformedOrigin, size, capsuleDirection, transformedAngle, contactFilter))
                    hits[hitCount++] = col;
            }

            return hitCount;
        }

        private bool RaycastOnly(Collider2D target, Ray2D ray, out RaycastHit2D hit,
            float maxDistance = float.PositiveInfinity,
            ContactFilter2D contactFilter = default)
        {
            int hitCount = _physicsScene2D.Raycast(ray.origin, ray.direction, maxDistance, contactFilter,
                _raycastHits2DCache);
            return SelectClosestTargetHit(target, hitCount, out hit);
        }

        private bool CircleCastOnly(Collider2D target, Ray2D ray, float radius, out RaycastHit2D hit,
            float maxDistance, ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.CircleCast(ray.origin, radius, ray.direction, maxDistance, contactFilter,
                _raycastHits2DCache);
            return SelectClosestTargetHit(target, hitCount, out hit);
        }

        private bool BoxCastOnly(Collider2D target, Ray2D ray, Vector2 size, float angle, out RaycastHit2D hit,
            float maxDistance, ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.BoxCast(ray.origin, size, angle, ray.direction, maxDistance, contactFilter,
                _raycastHits2DCache);
            return SelectClosestTargetHit(target, hitCount, out hit);
        }

        private bool CapsuleCastOnly(Collider2D target, Ray2D ray, Vector2 size, CapsuleDirection2D capsuleDirection,
            float angle, out RaycastHit2D hit, float maxDistance, ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.CapsuleCast(ray.origin, size, capsuleDirection, angle, ray.direction,
                maxDistance, contactFilter, _raycastHits2DCache);
            return SelectClosestTargetHit(target, hitCount, out hit);
        }

        private bool OverlapCircleOnly(Collider2D target, Vector2 origin, float radius, ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.OverlapCircle(origin, radius, contactFilter, _collider2DCache);
            return ContainsTarget(target, hitCount);
        }

        private bool OverlapBoxOnly(Collider2D target, Vector2 origin, Vector2 size, float angle,
            ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.OverlapBox(origin, size, angle, contactFilter, _collider2DCache);
            return ContainsTarget(target, hitCount);
        }

        private bool OverlapCapsuleOnly(Collider2D target, Vector2 origin, Vector2 size,
            CapsuleDirection2D capsuleDirection, float angle, ContactFilter2D contactFilter)
        {
            int hitCount = _physicsScene2D.OverlapCapsule(origin, size, capsuleDirection, angle, contactFilter,
                _collider2DCache);
            return ContainsTarget(target, hitCount);
        }

        private static bool SelectClosestHit(RaycastHit2D[] hits, int hitCount, out RaycastHit2D hit)
        {
            if (hitCount <= 0)
            {
                hit = default;
                return false;
            }

            hit = hits[0];
            for (var i = 1; i < hitCount; i++)
            {
                if (hits[i].distance < hit.distance)
                    hit = hits[i];
            }

            return true;
        }

        private static bool SelectClosestTargetHit(Collider2D target, int hitCount, out RaycastHit2D hit)
        {
            hit = default;
            bool found = false;

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

        private static bool ContainsTarget(Collider2D target, int hitCount)
        {
            for (var i = 0; i < hitCount; i++)
            {
                if (_collider2DCache[i] == target)
                    return true;
            }

            return false;
        }

        static bool PassesFilters(Collider2D col, ContactFilter2D filter)
        {
            if (!filter.isFiltering)
                return true;

            return !filter.IsFilteringTrigger(col) &&
                   !filter.IsFilteringLayerMask(col.gameObject);
        }

        static Vector2 ToHistoricalLocalPoint(Collider2DState state, Quaternion invRotation, Vector2 point)
        {
            var p = invRotation * (Vector3)(point - state.position);
            return new Vector2(p.x / state.scale.x, p.y / state.scale.y);
        }

        static Vector2 ToHistoricalLocalVector(Collider2DState state, Quaternion invRotation, Vector2 vector)
        {
            var v = invRotation * (Vector3)vector;
            return new Vector2(v.x / state.scale.x, v.y / state.scale.y);
        }

        private static void TransformPos(Vector2 origin, Collider2DState state, Transform trs,
            out Vector2 transformedOrigin)
        {
            var local = ToHistoricalLocalPoint(state, Quaternion.Euler(0f, 0f, -state.rotation), origin);
            transformedOrigin = trs.localToWorldMatrix.MultiplyPoint3x4(local);
        }

        private static void TransformRay(Ray2D ray, Collider2DState state, Transform trs, out Ray2D rayCurrentWorld)
        {
            var invRotation = Quaternion.Euler(0f, 0f, -state.rotation);
            var localOrigin = ToHistoricalLocalPoint(state, invRotation, ray.origin);
            var localDir = ToHistoricalLocalVector(state, invRotation, ray.direction);

            var currentWorldMatrix = trs.localToWorldMatrix;
            rayCurrentWorld = new Ray2D(
                currentWorldMatrix.MultiplyPoint3x4(localOrigin),
                currentWorldMatrix.MultiplyVector(localDir)
            );
        }

        private static void TransformHitBack(ref RaycastHit2D hit, Collider2DState state, Transform trs)
        {
            var toLocal = trs.worldToLocalMatrix;
            Vector3 localPoint = toLocal.MultiplyPoint3x4(hit.point);
            Vector3 localNormal = toLocal.MultiplyVector(hit.normal);

            var rotation = Quaternion.Euler(0f, 0f, state.rotation);
            var scaledPoint = new Vector3(localPoint.x * state.scale.x, localPoint.y * state.scale.y, 0f);
            var scaledNormal = new Vector3(localNormal.x * state.scale.x, localNormal.y * state.scale.y, 0f);

            hit.point = (Vector2)(rotation * scaledPoint) + state.position;

            var normal = (Vector2)(rotation * scaledNormal);
            if (normal.sqrMagnitude > 1e-12f)
                hit.normal = normal.normalized;
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

        private int FilterColliders(int hitCount, Collider2D[] hits)
        {
            for (var i = 0; i < hitCount; i++)
            {
                var col = hits[i];
                if (col && _trackedColliders.Contains(col))
                    hits[i--] = hits[--hitCount];
            }

            return hitCount;
        }
    }
}

#endif

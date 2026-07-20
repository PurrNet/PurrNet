#if UNITY_PHYSICS_3D
using UnityEngine;

namespace PurrNet.Modules
{
    public partial class RollbackModule
    {
        static readonly RaycastHit[] _raycastHits = new RaycastHit[1024];
        static readonly Collider[] _colliderHits = new Collider[1024];

        /// <summary>
        /// Performs a sphere overlap check at the given position with the given radius.
        /// </summary>
        public int SphereCast(double preciseTick, Ray ray, float radius,
            RaycastHit[] raycastHits, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;
            int hitCount = _physicsScene.SphereCast(ray.origin, radius, ray.direction, raycastHits, maxDistance, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, raycastHits);

            // handle sphere cast hits manually
            hitCount = DoManualSphereCasts(ray, radius, raycastHits, maxDistance, layerMask, colliderCount, hitCount, preciseTick, queryTriggers);

            return hitCount;
        }

        /// <summary>
        /// Performs a sphere cast at the given position with the given radius.
        /// </summary>
        public bool SphereCast(double preciseTick, Ray ray, float radius,
            out RaycastHit hit, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = SphereCast(preciseTick, ray, radius, _raycastHits, maxDistance, layerMask, queryTriggers);

            // return the closest hit
            if (hitCount > 0)
            {
                hit = _raycastHits[0];
                for (var i = 1; i < hitCount; i++)
                {
                    if (_raycastHits[i].distance < hit.distance)
                        hit = _raycastHits[i];
                }

                return true;
            }

            hit = default;
            return false;
        }

        public int BoxCast(double preciseTick, Ray ray, Vector3 halfExtents, Quaternion orientation,
            RaycastHit[] raycastHits, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.BoxCast(ray.origin, halfExtents, ray.direction, raycastHits, orientation, maxDistance, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, raycastHits);

            // handle box cast hits manually
            hitCount = DoManualBoxCasts(ray, halfExtents, orientation, raycastHits, maxDistance, layerMask, colliderCount, hitCount, preciseTick, queryTriggers);

            return hitCount;
        }

        public bool BoxCast(double preciseTick, Ray ray, Vector3 halfExtents, Quaternion orientation,
            out RaycastHit hit, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = BoxCast(preciseTick, ray, halfExtents, orientation, _raycastHits, maxDistance, layerMask, queryTriggers);

            // return the closest hit
            if (hitCount > 0)
            {
                hit = _raycastHits[0];
                for (var i = 1; i < hitCount; i++)
                {
                    if (_raycastHits[i].distance < hit.distance)
                        hit = _raycastHits[i];
                }

                return true;
            }

            hit = default;
            return false;
        }

        public int CapsuleCast(double preciseTick, Vector3 point1, Vector3 point2, Vector3 direction, float radius,
            RaycastHit[] raycastHits, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.CapsuleCast(point1, point2, radius, direction, raycastHits, maxDistance, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, raycastHits);

            // handle capsule cast hits manually
            hitCount = DoManualCapsuleCasts(point1, point2, radius, direction, raycastHits, maxDistance, layerMask, colliderCount, hitCount, preciseTick, queryTriggers);

            return hitCount;
        }

        public bool CapsuleCast(double preciseTick, Vector3 point1, Vector3 point2, Vector3 direction, float radius,
            out RaycastHit hit, float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = CapsuleCast(preciseTick, point1, point2, direction, radius, _raycastHits, maxDistance, layerMask, queryTriggers);

            // return the closest hit
            if (hitCount > 0)
            {
                hit = _raycastHits[0];
                for (var i = 1; i < hitCount; i++)
                {
                    if (_raycastHits[i].distance < hit.distance)
                        hit = _raycastHits[i];
                }

                return true;
            }

            hit = default;
            return false;
        }

        /// <summary>
        /// Performs a sphere overlap check at the given position with the given radius.
        /// </summary>
        public int SphereOverlap(double preciseTick, Vector3 origin, float radius,
            Collider[] hits, int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.OverlapSphere(origin, radius, hits, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, hits);

            // handle sphere overlaps manually
            hitCount = DoManualSphereOverlaps(origin, radius, hits, layerMask, colliderCount, hitCount, preciseTick,
                queryTriggers);

            return hitCount;
        }

        /// <summary>
        /// Performs a sphere overlap check at the given position with the given radius.
        /// </summary>
        public bool CheckSphere(double preciseTick, Vector3 origin, float radius, int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return false;

            int hitCount = SphereOverlap(preciseTick, origin, radius, _colliderHits, layerMask, queryTriggers);

            // return the first hit
            if (hitCount > 0)
                return true;

            return false;
        }

        public int BoxOverlap(double preciseTick, Vector3 origin, Vector3 halfExtents, Quaternion orientation,
            Collider[] hits, int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.OverlapBox(origin, halfExtents, hits, orientation, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, hits);

            // handle box overlaps manually
            hitCount = DoManualBoxOverlaps(origin, halfExtents, orientation, hits, layerMask, colliderCount, hitCount,
                preciseTick, queryTriggers);

            return hitCount;
        }

        public bool CheckBox(double preciseTick, Vector3 origin, Vector3 halfExtents, Quaternion orientation,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return false;

            int hitCount = BoxOverlap(preciseTick, origin, halfExtents, orientation, _colliderHits, layerMask,
                queryTriggers);

            // return the first hit
            if (hitCount > 0)
                return true;

            return false;
        }

        public int CapsuleOverlap(double preciseTick, Vector3 point1, Vector3 point2, float radius,
            Collider[] hits, int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.OverlapCapsule(point1, point2, radius, hits, layerMask, queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, hits);

            // handle capsule overlaps manually
            hitCount = DoManualCapsuleOverlaps(point1, point2, radius, hits, layerMask, colliderCount, hitCount,
                preciseTick, queryTriggers);

            return hitCount;
        }

        public bool CheckCapsule(double preciseTick, Vector3 point1, Vector3 point2, float radius,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return false;

            int hitCount = CapsuleOverlap(preciseTick, point1, point2, radius, _colliderHits, layerMask, queryTriggers);

            // return the first hit
            if (hitCount > 0)
                return true;

            return false;
        }

        /// <summary>
        /// Casts a ray, from point origin, in direction direction, of length maxDistance, against all colliders in the scene.
        /// </summary>
        public int Raycast(double preciseTick, Ray ray, RaycastHit[] raycastHits,
            float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
                return 0;

            int hitCount = _physicsScene.Raycast(ray.origin, ray.direction, raycastHits, maxDistance, layerMask,
                queryTriggers);
            int colliderCount = _colliders3D.Count;

            // remove any colliders that we are handling manually
            hitCount = FilterColliders(hitCount, raycastHits);

            // handle raycast hits manually
            hitCount = DoManualRaycasts(ray, raycastHits, maxDistance, layerMask, colliderCount, hitCount, preciseTick,
                queryTriggers);

            return hitCount;
        }

        /// <summary>
        /// Casts a ray, from point origin, in direction direction, of length maxDistance, against all colliders in the scene.
        /// </summary>
        public bool Raycast(double preciseTick, Ray ray, out RaycastHit hit,
            float maxDistance = float.PositiveInfinity,
            int layerMask = Physics.AllLayers,
            QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.UseGlobal)
        {
            if (!_physicsScene.IsValid())
            {
                hit = default;
                return false;
            }

            int hitCount = Raycast(preciseTick, ray, _raycastHits, maxDistance, layerMask, queryTriggers);

            // return the closest hit
            if (hitCount > 0)
            {
                hit = _raycastHits[0];
                for (var i = 1; i < hitCount; i++)
                {
                    if (_raycastHits[i].distance < hit.distance)
                        hit = _raycastHits[i];
                }

                return true;
            }

            hit = default;
            return false;
        }

        bool TryGetState(double preciseTick, Collider col, SimpleHistory<Collider3DState> history,
            QueryTriggerInteraction queryTriggers, int layerMask, out Collider3DState state)
        {
            state = default;

            if (col.isTrigger && queryTriggers == QueryTriggerInteraction.Ignore)
                return false;

            bool isPartOfLayerMask = ((1 << col.gameObject.layer) & layerMask) != 0;

            if (!isPartOfLayerMask)
                return false;

            if (!Sample(history, preciseTick, out state))
                return false;

            return state.enabled;
        }

        private int DoManualSphereOverlaps(Vector3 origin, float radius, Collider[] hits, int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayOverlap(preciseTick, origin, radius))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformPos(origin, state, trs, out var transformedOrigin);

                if (col.OverlapSphere(transformedOrigin, radius))
                {
                    hits[hitCount++] = col;
                }
            }

            return hitCount;
        }

        private int DoManualBoxOverlaps(Vector3 origin, Vector3 halfExtents, Quaternion orientation, Collider[] hits,
            int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            float inflation = halfExtents.magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayOverlap(preciseTick, origin, inflation))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformPos(origin, state, trs, out var transformedOrigin);
                var transformedOrientation = trs.rotation * Quaternion.Inverse(state.rotation) * orientation;

                if (col.OverlapBox(transformedOrigin, halfExtents, transformedOrientation))
                {
                    hits[hitCount++] = col;
                }
            }

            return hitCount;
        }

        private int DoManualCapsuleOverlaps(Vector3 point1, Vector3 point2, float radius, Collider[] hits,
            int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            var mid = (point1 + point2) * 0.5f;
            float inflation = radius + (point2 - point1).magnitude * 0.5f;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayOverlap(preciseTick, mid, inflation))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformPos(point1, state, trs, out var p1);
                TransformPos(point2, state, trs, out var p2);

                if (col.OverlapCapsule(p1, p2, radius))
                {
                    hits[hitCount++] = col;
                }
            }

            return hitCount;
        }

        private int DoManualSphereCasts(Ray ray, float radius, RaycastHit[] hits, float maxDistance, int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayHitRay(preciseTick, ray, maxDistance, radius))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var transformedRay);

                if (col.SphereCast(transformedRay, radius, out var hit, maxDistance))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualBoxCasts(Ray ray, Vector3 halfExtents, Quaternion orientation, RaycastHit[] hits, float maxDistance, int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            float inflation = halfExtents.magnitude;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayHitRay(preciseTick, ray, maxDistance, inflation))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var transformedRay);
                var transformedOrientation = trs.rotation * Quaternion.Inverse(state.rotation) * orientation;

                if (col.BoxCast(transformedRay, halfExtents, transformedOrientation, out var hit, maxDistance))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualCapsuleCasts(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] hits, float maxDistance, int layerMask, int colliderCount, int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            var checkRay = new Ray((point1 + point2) * 0.5f, direction);
            float inflation = radius + (point2 - point1).magnitude * 0.5f;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayHitRay(preciseTick, checkRay, maxDistance, inflation))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformCapsule(point1, point2, direction, state, trs, out var p1, out var p2, out var dir);

                if (col.CapsuleCast(p1, p2, radius, dir, out var hit, maxDistance))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int DoManualRaycasts(Ray ray, RaycastHit[] hits, float maxDistance, int layerMask, int colliderCount,
            int hitCount, double preciseTick, QueryTriggerInteraction queryTriggers)
        {
            if (queryTriggers == QueryTriggerInteraction.UseGlobal)
                queryTriggers = Physics.queriesHitTriggers
                    ? QueryTriggerInteraction.Collide
                    : QueryTriggerInteraction.Ignore;

            for (var i = 0; i < colliderCount; i++)
            {
                if (hitCount >= hits.Length)
                    break;

                var col = _colliders3D[i];

                if (!col)
                    continue;

                if (!_bounds3D[i].MayHitRay(preciseTick, ray, maxDistance, 0f))
                    continue;

                if (!TryGetState(preciseTick, col, _histories3D[i], queryTriggers, layerMask, out var state))
                    continue;

                var trs = col.transform;
                TransformRay(ray, state, trs, out var transformedRay);

                if (col.Raycast(transformedRay, out var hit, maxDistance))
                {
                    TransformHitBack(ref hit, state, trs);
                    hits[hitCount++] = hit;
                }
            }

            return hitCount;
        }

        private int FilterColliders(int hitCount, RaycastHit[] hits)
        {
            for (var i = 0; i < hitCount; i++)
            {
                var col = hits[i].collider;
                if (col && _trackedColliders.Contains(col))
                    hits[i--] = hits[--hitCount];
            }

            return hitCount;
        }

        private int FilterColliders(int hitCount, Collider[] hits)
        {
            for (var i = 0; i < hitCount; i++)
            {
                var col = hits[i];
                if (col && _trackedColliders.Contains(col))
                    hits[i--] = hits[--hitCount];
            }

            return hitCount;
        }

        static Vector3 ToHistoricalLocalPoint(Collider3DState state, Quaternion invRotation, Vector3 point)
        {
            var p = invRotation * (point - state.position);
            return new Vector3(p.x / state.scale.x, p.y / state.scale.y, p.z / state.scale.z);
        }

        static Vector3 ToHistoricalLocalVector(Collider3DState state, Quaternion invRotation, Vector3 vector)
        {
            var v = invRotation * vector;
            return new Vector3(v.x / state.scale.x, v.y / state.scale.y, v.z / state.scale.z);
        }

        private static void TransformPos(Vector3 origin, Collider3DState state, Transform trs, out Vector3 transformedOrigin)
        {
            var local = ToHistoricalLocalPoint(state, Quaternion.Inverse(state.rotation), origin);
            transformedOrigin = trs.localToWorldMatrix.MultiplyPoint3x4(local);
        }

        private static void TransformCapsule(Vector3 point1, Vector3 point2, Vector3 direction, Collider3DState state, Transform trs, out Vector3 p1, out Vector3 p2, out Vector3 dir)
        {
            var invRotation = Quaternion.Inverse(state.rotation);
            var localP1 = ToHistoricalLocalPoint(state, invRotation, point1);
            var localP2 = ToHistoricalLocalPoint(state, invRotation, point2);
            var localDir = ToHistoricalLocalVector(state, invRotation, direction);

            var currentWorldMatrix = trs.localToWorldMatrix;
            p1 = currentWorldMatrix.MultiplyPoint3x4(localP1);
            p2 = currentWorldMatrix.MultiplyPoint3x4(localP2);
            dir = currentWorldMatrix.MultiplyVector(localDir);
        }

        private static void TransformRay(Ray ray, Collider3DState state, Transform trs, out Ray rayCurrentWorld)
        {
            var invRotation = Quaternion.Inverse(state.rotation);
            var localOrigin = ToHistoricalLocalPoint(state, invRotation, ray.origin);
            var localDir = ToHistoricalLocalVector(state, invRotation, ray.direction);

            var currentWorldMatrix = trs.localToWorldMatrix;
            rayCurrentWorld = new Ray(
                currentWorldMatrix.MultiplyPoint3x4(localOrigin),
                currentWorldMatrix.MultiplyVector(localDir)
            );
        }

        private static void TransformHitBack(ref RaycastHit hit, Collider3DState state, Transform trs)
        {
            var toHistorical = Matrix4x4.TRS(state.position, state.rotation, state.scale) * trs.worldToLocalMatrix;
            hit.point = toHistorical.MultiplyPoint3x4(hit.point);
            hit.normal = toHistorical.MultiplyVector(hit.normal);
        }
    }
}
#endif

#if UNITY_PHYSICS_3D
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using PhysicsObjectId = UnityEngine.EntityId;
#else
using PhysicsObjectId = System.Int32;
#endif

namespace PurrNet
{
    [Flags]
    internal enum NetworkTransformContactDominance : byte
    {
        None = 0,
        Linear = 1 << 0,
        Angular = 1 << 1,
        Full = Linear | Angular
    }

    /// <summary>
    /// Makes observer-driven rigidbodies dominant in their contact constraints without changing
    /// the rigidbody configuration. Contact callbacks can run on physics worker threads, so they
    /// only read an immutable snapshot of body IDs and never access Unity objects.
    /// </summary>
    internal static class NetworkTransformContactManager
    {
        internal sealed class Registration : IDisposable
        {
            internal readonly PhysicsObjectId bodyId;
            internal readonly NetworkTransformContactDominance dominance;
            internal readonly PhysicsObjectId[] colliderIds;
            internal readonly int generation;

            private int _disposed;

            internal Registration(PhysicsObjectId bodyId, NetworkTransformContactDominance dominance,
                PhysicsObjectId[] colliderIds, int generation)
            {
                this.bodyId = bodyId;
                this.dominance = dominance;
                this.colliderIds = colliderIds;
                this.generation = generation;
            }

            internal bool Matches(Rigidbody body, NetworkTransformContactDominance value)
            {
                return body && GetObjectId(body).Equals(bodyId) && dominance == value &&
                       generation == _generation && Volatile.Read(ref _disposed) == 0;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                Release(this);
            }
        }

        private sealed class BodyClaim
        {
            internal int references;
            internal int linearReferences;
            internal int angularReferences;

            internal NetworkTransformContactDominance dominance
            {
                get
                {
                    var value = NetworkTransformContactDominance.None;
                    if (linearReferences > 0)
                        value |= NetworkTransformContactDominance.Linear;
                    if (angularReferences > 0)
                        value |= NetworkTransformContactDominance.Angular;
                    return value;
                }
            }
        }

        private sealed class ColliderClaim
        {
            internal readonly Collider collider;
            internal readonly bool restoreToFalse;
            internal int references;

            internal ColliderClaim(Collider collider)
            {
                this.collider = collider;
                restoreToFalse = !collider.hasModifiableContacts;
                references = 1;
            }
        }

        private static readonly object _gate = new object();
        private static readonly Dictionary<PhysicsObjectId, BodyClaim> _bodyClaims =
            new Dictionary<PhysicsObjectId, BodyClaim>();
        private static readonly Dictionary<PhysicsObjectId, ColliderClaim> _colliderClaims =
            new Dictionary<PhysicsObjectId, ColliderClaim>();

        private static Dictionary<PhysicsObjectId, NetworkTransformContactDominance> _dominanceSnapshot =
            new Dictionary<PhysicsObjectId, NetworkTransformContactDominance>();

        private static bool _subscribed;
        private static int _generation;

        internal static Registration Register(Rigidbody body, NetworkTransformContactDominance dominance)
        {
            if (!body || dominance == NetworkTransformContactDominance.None)
                return null;

            var allColliders = body.GetComponentsInChildren<Collider>(true);
            var ownedColliders = new List<Collider>(allColliders.Length);

            for (int i = 0; i < allColliders.Length; i++)
            {
                var collider = allColliders[i];
                if (collider && collider.attachedRigidbody == body)
                    ownedColliders.Add(collider);
            }

            if (ownedColliders.Count == 0)
                return null;

            var bodyId = GetObjectId(body);
            var colliderIds = new PhysicsObjectId[ownedColliders.Count];

            lock (_gate)
            {
                EnsureSubscribed();

                if (!_bodyClaims.TryGetValue(bodyId, out var bodyClaim))
                {
                    bodyClaim = new BodyClaim();
                    _bodyClaims.Add(bodyId, bodyClaim);
                }

                bodyClaim.references++;
                if ((dominance & NetworkTransformContactDominance.Linear) != 0)
                    bodyClaim.linearReferences++;
                if ((dominance & NetworkTransformContactDominance.Angular) != 0)
                    bodyClaim.angularReferences++;

                PublishSnapshot();

                for (int i = 0; i < ownedColliders.Count; i++)
                {
                    var collider = ownedColliders[i];
                    var colliderId = GetObjectId(collider);
                    colliderIds[i] = colliderId;

                    if (_colliderClaims.TryGetValue(colliderId, out var colliderClaim))
                    {
                        colliderClaim.references++;
                        continue;
                    }

                    colliderClaim = new ColliderClaim(collider);
                    _colliderClaims.Add(colliderId, colliderClaim);

                    if (colliderClaim.restoreToFalse)
                        collider.hasModifiableContacts = true;
                }

                return new Registration(bodyId, dominance, colliderIds, _generation);
            }
        }

        private static void Release(Registration registration)
        {
            lock (_gate)
            {
                if (registration.generation != _generation)
                    return;

                for (int i = 0; i < registration.colliderIds.Length; i++)
                {
                    var colliderId = registration.colliderIds[i];
                    if (!_colliderClaims.TryGetValue(colliderId, out var colliderClaim))
                        continue;

                    colliderClaim.references--;
                    if (colliderClaim.references > 0)
                        continue;

                    if (colliderClaim.restoreToFalse && colliderClaim.collider &&
                        colliderClaim.collider.hasModifiableContacts)
                        colliderClaim.collider.hasModifiableContacts = false;

                    _colliderClaims.Remove(colliderId);
                }

                if (_bodyClaims.TryGetValue(registration.bodyId, out var bodyClaim))
                {
                    bodyClaim.references--;
                    if ((registration.dominance & NetworkTransformContactDominance.Linear) != 0)
                        bodyClaim.linearReferences--;
                    if ((registration.dominance & NetworkTransformContactDominance.Angular) != 0)
                        bodyClaim.angularReferences--;

                    if (bodyClaim.references <= 0)
                        _bodyClaims.Remove(registration.bodyId);
                }

                PublishSnapshot();

                if (_bodyClaims.Count == 0)
                    Unsubscribe();
            }
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            Physics.ContactModifyEvent += ModifyContacts;
            Physics.ContactModifyEventCCD += ModifyContacts;
            _subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed)
                return;

            Physics.ContactModifyEvent -= ModifyContacts;
            Physics.ContactModifyEventCCD -= ModifyContacts;
            _subscribed = false;
        }

        private static void PublishSnapshot()
        {
            var snapshot =
                new Dictionary<PhysicsObjectId, NetworkTransformContactDominance>(_bodyClaims.Count);

            foreach (var pair in _bodyClaims)
                snapshot.Add(pair.Key, pair.Value.dominance);

            Volatile.Write(ref _dominanceSnapshot, snapshot);
        }

        private static void ModifyContacts(PhysicsScene _, NativeArray<ModifiableContactPair> pairs)
        {
            var snapshot = Volatile.Read(ref _dominanceSnapshot);
            if (snapshot.Count == 0)
                return;

            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
#if UNITY_6000_3_OR_NEWER
                snapshot.TryGetValue(pair.bodyEntityId, out var first);
                snapshot.TryGetValue(pair.otherBodyEntityId, out var second);
#else
                snapshot.TryGetValue(pair.bodyInstanceID, out var first);
                snapshot.TryGetValue(pair.otherBodyInstanceID, out var second);
#endif

                if (first == NetworkTransformContactDominance.None &&
                    second == NetworkTransformContactDominance.None)
                    continue;

                var massProperties = pair.massProperties;
                if (ApplyDominance(ref massProperties, first, second))
                {
                    for (int contactIndex = 0; contactIndex < pair.contactCount; contactIndex++)
                        pair.IgnoreContact(contactIndex);
                    continue;
                }

                pair.massProperties = massProperties;
            }
        }

        internal static bool ApplyDominance(ref ModifiableMassProperties massProperties,
            NetworkTransformContactDominance first, NetworkTransformContactDominance second)
        {
            if (first == NetworkTransformContactDominance.Full &&
                second == NetworkTransformContactDominance.Full)
                return true;

            if ((first & NetworkTransformContactDominance.Linear) != 0)
                massProperties.inverseMassScale = 0f;
            if ((first & NetworkTransformContactDominance.Angular) != 0)
                massProperties.inverseInertiaScale = 0f;
            if ((second & NetworkTransformContactDominance.Linear) != 0)
                massProperties.otherInverseMassScale = 0f;
            if ((second & NetworkTransformContactDominance.Angular) != 0)
                massProperties.otherInverseInertiaScale = 0f;

            return false;
        }

        internal static NetworkTransformContactDominance GetDominance(PhysicsObjectId bodyId)
        {
            var snapshot = Volatile.Read(ref _dominanceSnapshot);
            return snapshot.GetValueOrDefault(bodyId, NetworkTransformContactDominance.None);
        }

        private static PhysicsObjectId GetObjectId(UnityEngine.Object value)
        {
#if UNITY_6000_3_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            lock (_gate)
            {
                Physics.ContactModifyEvent -= ModifyContacts;
                Physics.ContactModifyEventCCD -= ModifyContacts;
                _subscribed = false;

                foreach (var pair in _colliderClaims)
                {
                    var claim = pair.Value;
                    if (claim.restoreToFalse && claim.collider && claim.collider.hasModifiableContacts)
                        claim.collider.hasModifiableContacts = false;
                }

                _colliderClaims.Clear();
                _bodyClaims.Clear();
                Volatile.Write(ref _dominanceSnapshot,
                    new Dictionary<PhysicsObjectId, NetworkTransformContactDominance>());

                unchecked
                {
                    _generation++;
                }
            }
        }
    }
}
#endif

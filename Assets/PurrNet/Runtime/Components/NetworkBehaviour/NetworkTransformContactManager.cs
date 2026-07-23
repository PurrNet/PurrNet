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
            internal readonly int generation;

            private int _disposed;

            internal Registration(PhysicsObjectId bodyId, NetworkTransformContactDominance dominance,
                int generation)
            {
                this.bodyId = bodyId;
                this.dominance = dominance;
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

        private static readonly object _gate = new object();
        private static readonly Dictionary<PhysicsObjectId, BodyClaim> _bodyClaims =
            new Dictionary<PhysicsObjectId, BodyClaim>();

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

            lock (_gate)
            {
                EnsureSubscribed();

                for (int i = 0; i < ownedColliders.Count; i++)
                {
                    var collider = ownedColliders[i];
                    if (!collider.hasModifiableContacts)
                        collider.hasModifiableContacts = true;
                }

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

                return new Registration(bodyId, dominance, _generation);
            }
        }

        private static void Release(Registration registration)
        {
            lock (_gate)
            {
                if (registration.generation != _generation)
                    return;

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
                ApplyDominance(ref massProperties, first, second);
                pair.massProperties = massProperties;
            }
        }

        internal static void ApplyDominance(ref ModifiableMassProperties massProperties,
            NetworkTransformContactDominance first, NetworkTransformContactDominance second)
        {
            if ((first & NetworkTransformContactDominance.Linear) != 0)
                massProperties.inverseMassScale = 0f;
            if ((first & NetworkTransformContactDominance.Angular) != 0)
                massProperties.inverseInertiaScale = 0f;
            if ((second & NetworkTransformContactDominance.Linear) != 0)
                massProperties.otherInverseMassScale = 0f;
            if ((second & NetworkTransformContactDominance.Angular) != 0)
                massProperties.otherInverseInertiaScale = 0f;
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

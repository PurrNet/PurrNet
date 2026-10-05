#if UNITY_PHYSICS_3D
using System;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Rigidbody")]
    public class NetworkRigidbody : NetworkRigidbodyBase
    {
        [Header("Settings Override")]
        [Tooltip("Optional. When assigned, this asset's Create() builds a per-instance correction object that controls all correction decisions. The correction fields above are passed as defaults via the correction context.")]
        [SerializeField] private NetworkRigidbodySettings _settingsOverride;

        /// <summary>
        /// Fired after this rigidbody applies a hard position correction teleport.
        /// </summary>
        public event Action<RigidbodyCorrectionContext> onTeleportCorrection;

        private Rigidbody _cachedRigidbody;
        private Rigidbody _rigidbody => _cachedRigidbody ? _cachedRigidbody : (_cachedRigidbody = GetComponent<Rigidbody>());

        private Transform _parentPoseTransform;
        private Rigidbody _parentPoseRigidbody;

        private void Awake()
        {
            _cachedRigidbody = GetComponent<Rigidbody>();
        }

        public NetworkRigidbodySettings settingsOverride
        {
            get => _settingsOverride;
            set
            {
                if (_settingsOverride == value)
                    return;
                _settingsOverride = value;
                DisposeSettingsInstance();
                EnsureSettingsInstance();
            }
        }

        public NetworkRigidbodySettingsInstance settingsInstance => settingsInstanceInternal as NetworkRigidbodySettingsInstance;

        protected override UnityEngine.Object settingsOverrideAsset => _settingsOverride;

        internal override IRigidbodyCorrectionInstance CreateSettingsInstance() => _settingsOverride.Create(this);

        internal override void RaiseTeleportCorrection(in RigidbodyCorrectionData data)
        {
            onTeleportCorrection?.Invoke(NetworkRigidbodySettingsInstance.ToContext(in data));
        }

        protected override Component bodyComponent => _rigidbody;

        protected override Vector3 bodyPosition
        {
            get => _rigidbody.position;
            set => _rigidbody.position = value;
        }

        protected override Quaternion bodyRotation
        {
            get => _rigidbody.rotation;
            set => _rigidbody.rotation = value;
        }

        protected override Vector3 bodyLinearVelocity
        {
            get => NetworkRigidbodyPhysics.GetLinearVelocity(_rigidbody);
            set => NetworkRigidbodyPhysics.SetLinearVelocity(_rigidbody, value);
        }

        protected override Vector3 bodyAngularVelocity
        {
            get => _rigidbody.angularVelocity;
            set => NetworkRigidbodyPhysics.SetAngularVelocity(_rigidbody, value);
        }

        protected override float bodyMass
        {
            get => _rigidbody.mass;
            set => _rigidbody.mass = value;
        }

        protected override float bodyDrag
        {
#if UNITY_6000_0_OR_NEWER
            get => _rigidbody.linearDamping;
            set => _rigidbody.linearDamping = value;
#else
            get => _rigidbody.drag;
            set => _rigidbody.drag = value;
#endif
        }

        protected override float bodyAngularDrag
        {
#if UNITY_6000_0_OR_NEWER
            get => _rigidbody.angularDamping;
            set => _rigidbody.angularDamping = value;
#else
            get => _rigidbody.angularDrag;
            set => _rigidbody.angularDrag = value;
#endif
        }

        protected override bool bodyIsKinematic
        {
            get => _rigidbody.isKinematic;
            set => _rigidbody.isKinematic = value;
        }

        protected override bool bodyIsSleeping => _rigidbody.IsSleeping();

        protected override bool bodyHasFrozenRotation => (_rigidbody.constraints & RigidbodyConstraints.FreezeRotation) != 0;

        protected override bool canApplyDynamicMotion => NetworkRigidbodyPhysics.CanApplyDynamicMotion(_rigidbody);

        protected override void MoveBodyPosition(Vector3 position) => _rigidbody.MovePosition(position);

        protected override void MoveBodyRotation(Quaternion rotation) => _rigidbody.MoveRotation(rotation);

        protected override void AddBodyForce(Vector3 force, NetworkForceMode mode)
        {
            NetworkRigidbodyPhysics.AddForce(_rigidbody, force, (ForceMode)mode);
        }

        protected override void AddBodyForceAtPosition(Vector3 force, Vector3 position, NetworkForceMode mode)
        {
            NetworkRigidbodyPhysics.AddForceAtPosition(_rigidbody, force, position, (ForceMode)mode);
        }

        protected override void AddBodyTorque(Vector3 torque, NetworkForceMode mode)
        {
            NetworkRigidbodyPhysics.AddTorque(_rigidbody, torque, (ForceMode)mode);
        }

        protected override void ApplyBodyPositionSpring(Vector3 targetPosition, Vector3 targetLinearVelocity, float positionError, float positionStrength, float correctionRange)
        {
            NetworkRigidbodyPhysics.ApplyPositionSpring(
                _rigidbody,
                targetPosition,
                targetLinearVelocity,
                positionError,
                positionStrength,
                correctionRange,
                bodyDrag);
        }

        protected override void ApplyBodyRotationSpring(Quaternion targetRotation, Vector3 targetAngularVelocity, float rotationStrength, bool kinematic)
        {
            NetworkRigidbodyPhysics.ApplyRotationSpring(_rigidbody, targetRotation, targetAngularVelocity, rotationStrength, kinematic);
        }

        protected override RigidbodySettingsData GetCurrentSettings()
        {
            if (!_rigidbody)
                return default;

            return new RigidbodySettingsData
            {
                mass = (Half)_rigidbody.mass,
                drag = (Half)bodyDrag,
                angularDrag = (Half)bodyAngularDrag,
                useGravity = _rigidbody.useGravity,
                isKinematic = _rigidbody.isKinematic
            };
        }

        protected override void ApplySettings(in RigidbodySettingsData settings)
        {
            _rigidbody.mass = settings.mass;
            bodyDrag = settings.drag;
            bodyAngularDrag = settings.angularDrag;
            _rigidbody.useGravity = settings.useGravity;
            _rigidbody.isKinematic = settings.isKinematic;
        }

        private Rigidbody ResolveParentRigidbody(Transform parent)
        {
            if (_parentPoseTransform != parent)
            {
                _parentPoseTransform = parent;
                _parentPoseRigidbody = parent ? parent.GetComponentInParent<Rigidbody>() : null;
                if (_parentPoseRigidbody == _rigidbody)
                    _parentPoseRigidbody = null;
            }

            return _parentPoseRigidbody;
        }

        protected override bool TryGetParentBody(Transform parent, out Transform bodyTransform, out Vector3 position, out Quaternion rotation)
        {
            var rb = ResolveParentRigidbody(parent);
            if (!rb)
            {
                bodyTransform = null;
                position = default;
                rotation = default;
                return false;
            }

            bodyTransform = rb.transform;
            position = rb.position;
            rotation = rb.rotation;
            return true;
        }

        protected override bool TryGetParentBodyMotion(Transform parent, out Vector3 linearVelocity, out Vector3 angularVelocity, out Vector3 worldCenterOfMass)
        {
            var rb = ResolveParentRigidbody(parent);
            if (!rb)
            {
                linearVelocity = default;
                angularVelocity = default;
                worldCenterOfMass = default;
                return false;
            }

            linearVelocity = NetworkRigidbodyPhysics.GetLinearVelocity(rb);
            angularVelocity = rb.angularVelocity;
            worldCenterOfMass = rb.worldCenterOfMass;
            return true;
        }

        protected override void ClearParentBodyCache()
        {
            _parentPoseTransform = null;
            _parentPoseRigidbody = null;
        }

        public Vector3 linearVelocity
        {
            get => _rigidbody ? bodyLinearVelocity : Vector3.zero;
            set { if (_rigidbody) bodyLinearVelocity = value; }
        }

        /// <summary>Pre-Unity 6 alias for linearVelocity.</summary>
        public Vector3 velocity
        {
            get => linearVelocity;
            set => linearVelocity = value;
        }

        public Vector3 angularVelocity
        {
            get => _rigidbody ? _rigidbody.angularVelocity : Vector3.zero;
            set { if (_rigidbody) bodyAngularVelocity = value; }
        }

        public Vector3 position
        {
            get => _rigidbody ? _rigidbody.position : transform.position;
            set { if (_rigidbody) _rigidbody.position = value; }
        }

        public Quaternion rotation
        {
            get => _rigidbody ? _rigidbody.rotation : transform.rotation;
            set { if (_rigidbody) _rigidbody.rotation = value; }
        }

        public bool useGravity
        {
            get => _rigidbody && _rigidbody.useGravity;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.useGravity = value;
                SyncSettingsIfChanged();
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            AddForceInternal(force, (NetworkForceMode)mode);
        }

        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            AddForceAtPositionInternal(force, position, (NetworkForceMode)mode);
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            AddTorqueInternal(torque, (NetworkForceMode)mode);
        }

        public void MovePosition(Vector3 position)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MovePosition(position);
        }

        public void MoveRotation(Quaternion rotation)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MoveRotation(rotation);
        }
    }
}
#endif

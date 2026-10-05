#if UNITY_PHYSICS_2D
using System;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Rigidbody 2D")]
    public class NetworkRigidbody2D : NetworkRigidbodyBase
    {
        [Header("Settings Override")]
        [Tooltip("Optional. When assigned, this asset's Create() builds a per-instance correction object that controls all correction decisions. The correction fields above are passed as defaults via the correction context.")]
        [SerializeField] private NetworkRigidbody2DSettings _settingsOverride;

        /// <summary>
        /// Fired after this rigidbody applies a hard position correction teleport.
        /// </summary>
        public event Action<Rigidbody2DCorrectionContext> onTeleportCorrection;

        private Rigidbody2D _cachedRigidbody;
        private Rigidbody2D _rigidbody => _cachedRigidbody ? _cachedRigidbody : (_cachedRigidbody = GetComponent<Rigidbody2D>());

        private Transform _parentPoseTransform;
        private Rigidbody2D _parentPoseRigidbody;

        private void Awake()
        {
            _cachedRigidbody = GetComponent<Rigidbody2D>();
        }

        public NetworkRigidbody2DSettings settingsOverride
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

        public NetworkRigidbody2DSettingsInstance settingsInstance => settingsInstanceInternal as NetworkRigidbody2DSettingsInstance;

        protected override UnityEngine.Object settingsOverrideAsset => _settingsOverride;

        internal override IRigidbodyCorrectionInstance CreateSettingsInstance() => _settingsOverride.Create(this);

        internal override void RaiseTeleportCorrection(in RigidbodyCorrectionData data)
        {
            onTeleportCorrection?.Invoke(NetworkRigidbody2DSettingsInstance.ToContext(in data));
        }

        protected override Component bodyComponent => _rigidbody;

        protected override Vector3 bodyPosition
        {
            get => _rigidbody.position;
            set => _rigidbody.position = value;
        }

        protected override Quaternion bodyRotation
        {
            get => NetworkRigidbody2DPhysics.ToQuaternion(_rigidbody.rotation);
            set => _rigidbody.rotation = NetworkRigidbody2DPhysics.ToAngle(value, _rigidbody.rotation);
        }

        protected override Vector3 bodyLinearVelocity
        {
            get => NetworkRigidbody2DPhysics.GetLinearVelocity(_rigidbody);
            set => NetworkRigidbody2DPhysics.SetLinearVelocity(_rigidbody, value);
        }

        protected override Vector3 bodyAngularVelocity
        {
            get => NetworkRigidbody2DPhysics.ToAngularVelocity(_rigidbody.angularVelocity);
            set => NetworkRigidbody2DPhysics.SetAngularVelocity(_rigidbody, NetworkRigidbody2DPhysics.ToDegreesPerSecond(value));
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
            get => _rigidbody.bodyType != RigidbodyType2D.Dynamic;
            set
            {
                if (!value)
                    _rigidbody.bodyType = RigidbodyType2D.Dynamic;
                else if (_rigidbody.bodyType == RigidbodyType2D.Dynamic)
                    _rigidbody.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        protected override bool bodyIsSleeping => _rigidbody.IsSleeping();

        protected override bool bodyHasFrozenRotation => (_rigidbody.constraints & RigidbodyConstraints2D.FreezeRotation) != 0;

        protected override bool canApplyDynamicMotion => NetworkRigidbody2DPhysics.CanApplyDynamicMotion(_rigidbody);

        protected override void MoveBodyPosition(Vector3 position) => _rigidbody.MovePosition(position);

        protected override void MoveBodyRotation(Quaternion rotation)
        {
            _rigidbody.MoveRotation(NetworkRigidbody2DPhysics.ToAngle(rotation, _rigidbody.rotation));
        }

        protected override void AddBodyForce(Vector3 force, NetworkForceMode mode)
        {
            NetworkRigidbody2DPhysics.AddForce(_rigidbody, force, mode);
        }

        protected override void AddBodyForceAtPosition(Vector3 force, Vector3 position, NetworkForceMode mode)
        {
            NetworkRigidbody2DPhysics.AddForceAtPosition(_rigidbody, force, position, mode);
        }

        protected override void AddBodyTorque(Vector3 torque, NetworkForceMode mode)
        {
            NetworkRigidbody2DPhysics.AddTorque(_rigidbody, torque.z, mode);
        }

        protected override void ApplyBodyPositionSpring(Vector3 targetPosition, Vector3 targetLinearVelocity, float positionError, float positionStrength, float correctionRange)
        {
            NetworkRigidbody2DPhysics.ApplyPositionSpring(
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
            NetworkRigidbody2DPhysics.ApplyRotationSpring(
                _rigidbody,
                NetworkRigidbody2DPhysics.ToAngle(targetRotation, _rigidbody.rotation),
                NetworkRigidbody2DPhysics.ToDegreesPerSecond(targetAngularVelocity),
                rotationStrength,
                kinematic);
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
                gravityScale = (Half)_rigidbody.gravityScale,
                isKinematic = bodyIsKinematic
            };
        }

        protected override void ApplySettings(in RigidbodySettingsData settings)
        {
            _rigidbody.mass = settings.mass;
            bodyDrag = settings.drag;
            bodyAngularDrag = settings.angularDrag;
            _rigidbody.gravityScale = settings.gravityScale;
            bodyIsKinematic = settings.isKinematic;
        }

        private Rigidbody2D ResolveParentRigidbody(Transform parent)
        {
            if (_parentPoseTransform != parent)
            {
                _parentPoseTransform = parent;
                _parentPoseRigidbody = parent ? parent.GetComponentInParent<Rigidbody2D>() : null;
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
            var bodyPosition2D = rb.position;
            position = new Vector3(bodyPosition2D.x, bodyPosition2D.y, bodyTransform.position.z);
            rotation = NetworkRigidbody2DPhysics.ToQuaternion(rb.rotation);
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

            linearVelocity = NetworkRigidbody2DPhysics.GetLinearVelocity(rb);
            angularVelocity = NetworkRigidbody2DPhysics.ToAngularVelocity(rb.angularVelocity);
            worldCenterOfMass = rb.worldCenterOfMass;
            return true;
        }

        protected override void ClearParentBodyCache()
        {
            _parentPoseTransform = null;
            _parentPoseRigidbody = null;
        }

        public Vector2 linearVelocity
        {
            get => _rigidbody ? NetworkRigidbody2DPhysics.GetLinearVelocity(_rigidbody) : Vector2.zero;
            set { if (_rigidbody) NetworkRigidbody2DPhysics.SetLinearVelocity(_rigidbody, value); }
        }

        /// <summary>Pre-Unity 6 alias for linearVelocity.</summary>
        public Vector2 velocity
        {
            get => linearVelocity;
            set => linearVelocity = value;
        }

        /// <summary>Angular velocity in degrees per second, matching <see cref="Rigidbody2D.angularVelocity"/>.</summary>
        public float angularVelocity
        {
            get => _rigidbody ? _rigidbody.angularVelocity : 0f;
            set { if (_rigidbody) NetworkRigidbody2DPhysics.SetAngularVelocity(_rigidbody, value); }
        }

        public Vector2 position
        {
            get => _rigidbody ? _rigidbody.position : (Vector2)transform.position;
            set { if (_rigidbody) _rigidbody.position = value; }
        }

        /// <summary>Rotation in degrees, matching <see cref="Rigidbody2D.rotation"/>.</summary>
        public float rotation
        {
            get => _rigidbody ? _rigidbody.rotation : transform.eulerAngles.z;
            set { if (_rigidbody) _rigidbody.rotation = value; }
        }

        public float gravityScale
        {
            get => _rigidbody ? _rigidbody.gravityScale : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.gravityScale = value;
                SyncSettingsIfChanged();
            }
        }

        public RigidbodyType2D bodyType
        {
            get => _rigidbody ? _rigidbody.bodyType : RigidbodyType2D.Dynamic;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.bodyType = value;
                SyncSettingsIfChanged();
            }
        }

        public void AddForce(Vector2 force, ForceMode2D mode = ForceMode2D.Force)
        {
            AddForceInternal(force, NetworkRigidbody2DPhysics.ToNetworkForceMode(mode));
        }

        public void AddForceAtPosition(Vector2 force, Vector2 position, ForceMode2D mode = ForceMode2D.Force)
        {
            AddForceAtPositionInternal(force, position, NetworkRigidbody2DPhysics.ToNetworkForceMode(mode));
        }

        public void AddTorque(float torque, ForceMode2D mode = ForceMode2D.Force)
        {
            AddTorqueInternal(new Vector3(0f, 0f, torque), NetworkRigidbody2DPhysics.ToNetworkForceMode(mode));
        }

        public void MovePosition(Vector2 position)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MovePosition(position);
        }

        public void MoveRotation(float rotation)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MoveRotation(rotation);
        }

        /// <summary>2D overload of <see cref="NetworkRigidbodyBase.TeleportTo(Vector3, Quaternion)"/>; rotation in degrees.</summary>
        public void TeleportTo(Vector2 position, float rotation)
        {
            TeleportTo((Vector3)position, NetworkRigidbody2DPhysics.ToQuaternion(rotation));
        }

        /// <summary>2D overload of <see cref="NetworkRigidbodyBase.TeleportLocal(Vector3, Quaternion)"/>; rotation in degrees.</summary>
        public void TeleportLocal(Vector2 position, float rotation)
        {
            TeleportLocal((Vector3)position, NetworkRigidbody2DPhysics.ToQuaternion(rotation));
        }
    }
}
#endif

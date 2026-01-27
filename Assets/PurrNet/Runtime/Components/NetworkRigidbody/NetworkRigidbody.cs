using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public struct AppliedForce
    {
        public Vector3 force;
        public Vector3? position;
        public ForceMode mode;
        public bool isTorque;
    }

    public struct RigidbodyStateData
    {
        public Vector3 position;
        public Quaternion rotation;
        public AppliedForce[] recentForces;
    }

    public struct RigidbodyTeleportData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
    }

    public struct RigidbodySettingsData
    {
        public float mass;
        public float drag;
        public float angularDrag;
        public bool useGravity;
        public bool isKinematic;
    }

    [RequireComponent(typeof(Rigidbody))]
    public partial class NetworkRigidbody : NetworkIdentity, ITick
    {
        [Header("Authority")]
        [SerializeField] private bool _ownerAuth;

        [Header("Correction Settings")]
        [SerializeField] private float _acceptableError = 0.05f;
        [SerializeField] private float _hardCorrectionThreshold = 2f;
        [SerializeField] private float _maxCorrectionTime = 3f;
        [SerializeField] private float _springConstant = 50f;
        [SerializeField] private float _dampingConstant = 5f;

        [Header("Sync Settings")]
        [SerializeField] private int _maxForcesPerTick = 8;
        [SerializeField] private float _positionChangeThreshold = 0.001f;
        [SerializeField] private float _rotationChangeThreshold = 0.001f;
        [SerializeField] private float _velocityStopThreshold = 0.001f;

        private Rigidbody _rigidbody;
        private List<AppliedForce> _pendingForces = new();
        
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        
        private float _correctionTimer;
        private bool _isCorreting;
        private bool _hasPendingTeleport;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                PurrLogger.LogError($"NetworkRigidbody requires a Rigidbody component on {gameObject.name}", this);
                enabled = false;
            }
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            _targetPosition = _rigidbody.position;
            _targetRotation = _rigidbody.rotation;
            _lastSyncedPosition = _rigidbody.position;
            _lastSyncedRotation = _rigidbody.rotation;

            if (IsController(_ownerAuth))
                SyncSettings(GetCurrentSettings());
        }

        public void OnTick(float delta)
        {
            if (IsController(_ownerAuth))
                ControllerTick();
            else
                NonControllerTick(delta);
        }

        private void ControllerTick()
        {
            if (!HasStateChanged() && _pendingForces.Count == 0 && !ShouldSyncWhenStopped())
                return;

            var stateData = new RigidbodyStateData
            {
                position = _rigidbody.position,
                rotation = _rigidbody.rotation,
                recentForces = _pendingForces.ToArray()
            };

            _targetPosition = _rigidbody.position;
            _targetRotation = _rigidbody.rotation;

            if (isServer)
                SyncState(stateData);
            else
                SendStateToServer(stateData);

            _lastSyncedPosition = _rigidbody.position;
            _lastSyncedRotation = _rigidbody.rotation;
            _pendingForces.Clear();
        }

        private void NonControllerTick(float delta)
        {
            if (_hasPendingTeleport)
                return;

            float error = Vector3.Distance(_rigidbody.position, _targetPosition);

            if (error < _acceptableError)
            {
                _isCorreting = false;
                _correctionTimer = 0f;
                return;
            }

            if (error >= _hardCorrectionThreshold)
            {
                HardCorrect();
                return;
            }

            _isCorreting = true;
            _correctionTimer += delta;

            if (_maxCorrectionTime >= 0 && _correctionTimer >= _maxCorrectionTime)
            {
                HardCorrect();
                return;
            }

            ApplySoftCorrection();
        }

        private void ApplySoftCorrection()
        {
            Vector3 positionError = _targetPosition - _rigidbody.position;
            Vector3 springForce = positionError * _springConstant * _rigidbody.mass;
            Vector3 dampingForce = -_rigidbody.linearVelocity * _dampingConstant * _rigidbody.mass;
            _rigidbody.AddForce(springForce + dampingForce);

            Quaternion rotationError = _targetRotation * Quaternion.Inverse(_rigidbody.rotation);
            rotationError.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) > 0.1f)
            {
                Vector3 torque = axis * (angle * Mathf.Deg2Rad) * _springConstant * _rigidbody.mass;
                Vector3 angularDamping = -_rigidbody.angularVelocity * _dampingConstant * _rigidbody.mass;
                _rigidbody.AddTorque(torque + angularDamping);
            }
        }

        private void HardCorrect()
        {
            _rigidbody.MovePosition(_targetPosition);
            _rigidbody.MoveRotation(_targetRotation);
            _isCorreting = false;
            _correctionTimer = 0f;
        }

        private bool HasStateChanged()
        {
            float positionDelta = Vector3.Distance(_rigidbody.position, _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(_rigidbody.rotation, _lastSyncedRotation);

            return positionDelta > _positionChangeThreshold || rotationDelta > _rotationChangeThreshold;
        }

        private bool ShouldSyncWhenStopped()
        {
            return _rigidbody.linearVelocity.magnitude < _velocityStopThreshold &&
                   _rigidbody.angularVelocity.magnitude < _velocityStopThreshold &&
                   !_rigidbody.IsSleeping();
        }

        private RigidbodySettingsData GetCurrentSettings()
        {
            return new RigidbodySettingsData
            {
                mass = _rigidbody.mass,
                drag = _rigidbody.linearDamping,
                angularDrag = _rigidbody.angularDamping,
                useGravity = _rigidbody.useGravity,
                isKinematic = _rigidbody.isKinematic
            };
        }

        private void QueueForce(AppliedForce force)
        {
            if (_pendingForces.Count >= _maxForcesPerTick)
            {
                PurrLogger.LogError($"NetworkRigidbody on {gameObject.name} exceeded max forces per tick ({_maxForcesPerTick})", this);
                return;
            }
            _pendingForces.Add(force);
        }

        private void ApplyForce(AppliedForce force)
        {
            if (force.isTorque)
                _rigidbody.AddTorque(force.force, force.mode);
            else if (force.position.HasValue)
                _rigidbody.AddForceAtPosition(force.force, force.position.Value, force.mode);
            else
                _rigidbody.AddForce(force.force, force.mode);
        }

        #region Public API

        public Vector3 linearVelocity
        {
            get => _rigidbody.linearVelocity;
            set => _rigidbody.linearVelocity = value;
        }

        public Vector3 angularVelocity
        {
            get => _rigidbody.angularVelocity;
            set => _rigidbody.angularVelocity = value;
        }

        public Vector3 position
        {
            get => _rigidbody.position;
            set => MovePosition(value);
        }

        public Quaternion rotation
        {
            get => _rigidbody.rotation;
            set => MoveRotation(value);
        }

        public float mass
        {
            get => _rigidbody.mass;
            set
            {
                _rigidbody.mass = value;
                if (IsController(_ownerAuth))
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float drag
        {
            get => _rigidbody.linearDamping;
            set
            {
                _rigidbody.linearDamping = value;
                if (IsController(_ownerAuth))
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float angularDrag
        {
            get => _rigidbody.angularDamping;
            set
            {
                _rigidbody.angularDamping = value;
                if (IsController(_ownerAuth))
                    SyncSettings(GetCurrentSettings());
            }
        }

        public bool useGravity
        {
            get => _rigidbody.useGravity;
            set
            {
                _rigidbody.useGravity = value;
                if (IsController(_ownerAuth))
                    SyncSettings(GetCurrentSettings());
            }
        }

        public bool isKinematic
        {
            get => _rigidbody.isKinematic;
            set
            {
                _rigidbody.isKinematic = value;
                if (IsController(_ownerAuth))
                    SyncSettings(GetCurrentSettings());
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned)
                return;
            
            var appliedForce = new AppliedForce { force = force, mode = mode };

            if (IsController(_ownerAuth))
            {
                _rigidbody.AddForce(force, mode);
                if(mode != ForceMode.Force)
                    QueueForce(appliedForce);
            }
            else
            {
                BroadcastForce(appliedForce);
            }
        }

        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned)
                return;

            var appliedForce = new AppliedForce { force = force, position = position, mode = mode };

            if (IsController(_ownerAuth))
            {
                _rigidbody.AddForceAtPosition(force, position, mode);
                QueueForce(appliedForce);
            }
            else
            {
                BroadcastForce(appliedForce);
            }
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned)
                return;

            var appliedForce = new AppliedForce { force = torque, mode = mode, isTorque = true };

            if (IsController(_ownerAuth))
            {
                _rigidbody.AddTorque(torque, mode);
                QueueForce(appliedForce);
            }
            else
            {
                BroadcastForce(appliedForce);
            }
        }

        public void MovePosition(Vector3 position)
        {
            if (IsController(_ownerAuth))
            {
                _rigidbody.MovePosition(position);
                BroadcastTeleport();
            }
            else
            {
                RequestTeleport(position, _rigidbody.rotation);
            }
        }

        public void MoveRotation(Quaternion rotation)
        {
            if (IsController(_ownerAuth))
            {
                _rigidbody.MoveRotation(rotation);
                BroadcastTeleport();
            }
            else
            {
                RequestTeleport(_rigidbody.position, rotation);
            }
        }

        #endregion

        #region RPCs

        [ObserversRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SyncState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            _targetPosition = data.position;
            _targetRotation = data.rotation;

            if (data.recentForces != null)
            {
                foreach (var force in data.recentForces)
                    ApplyForce(force);
            }
        }

        [ServerRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SendStateToServer(RigidbodyStateData data)
        {
            _targetPosition = data.position;
            _targetRotation = data.rotation;

            if (data.recentForces != null)
            {
                foreach (var force in data.recentForces)
                    ApplyForce(force);
            }

            SyncState(data);
        }

        [ObserversRpc(runLocally: true, channel: Channel.Unreliable, deltaPacked: true)]
        private void BroadcastForce(AppliedForce force)
        {
            ApplyForce(force);

            if (IsController(_ownerAuth))
                QueueForce(force);
        }

        [ObserversRpc(deltaPacked: true)]
        private void Teleport(RigidbodyTeleportData data)
        {
            if (IsController(_ownerAuth))
                return;

            _hasPendingTeleport = true;
            _rigidbody.MovePosition(data.position);
            _rigidbody.MoveRotation(data.rotation);
            _rigidbody.linearVelocity = data.linearVelocity;
            _rigidbody.angularVelocity = data.angularVelocity;
            _targetPosition = data.position;
            _targetRotation = data.rotation;
            _isCorreting = false;
            _correctionTimer = 0f;
            _hasPendingTeleport = false;
        }

        [ObserversRpc(bufferLast: true, deltaPacked: true)]
        private void SyncSettings(RigidbodySettingsData data)
        {
            if (IsController(_ownerAuth))
                return;

            _rigidbody.mass = data.mass;
            _rigidbody.linearDamping = data.drag;
            _rigidbody.angularDamping = data.angularDrag;
            _rigidbody.useGravity = data.useGravity;
            _rigidbody.isKinematic = data.isKinematic;
        }

        [ServerRpc(requireOwnership: false, deltaPacked: true)]
        private void RequestTeleport(Vector3 position, Quaternion rotation)
        {
            if (_ownerAuth && owner.HasValue)
            {
                ForwardTeleportRequest(owner.Value, position, rotation);
                return;
            }

            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            BroadcastTeleport();
        }

        [TargetRpc(deltaPacked: true)]
        private void ForwardTeleportRequest(PlayerID target, Vector3 position, Quaternion rotation)
        {
            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            BroadcastTeleport();
        }

        private void BroadcastTeleport()
        {
            var teleportData = new RigidbodyTeleportData
            {
                position = _rigidbody.position,
                rotation = _rigidbody.rotation,
                linearVelocity = _rigidbody.linearVelocity,
                angularVelocity = _rigidbody.angularVelocity
            };
            Teleport(teleportData);
        }

        #endregion
    }
}
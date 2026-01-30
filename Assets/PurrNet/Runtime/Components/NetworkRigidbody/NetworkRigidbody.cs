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
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public float? senderPing;
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
        [Tooltip("If true, the client owning the object calculates physics (Client Auth). If false, the server calculates physics (Server Auth).")]
        [SerializeField] private bool _ownerAuth;

        [Header("Correction Settings")]
        [Tooltip("The distance error below which we stop applying correction forces. Prevents micro-jitter when very close to target.")]
        [SerializeField] private float _acceptableError = 0.05f;

        [Tooltip("If the distance error exceeds this threshold, the object will immediately teleport (snap) to the target.")]
        [SerializeField] private float _hardCorrectionThreshold = 2f;

        [Tooltip("Maximum duration (in seconds) to attempt soft correction forces before forcing a hard teleport. Set to -1 to disable.")]
        [SerializeField] private float _maxCorrectionTime = -1f;

        [Tooltip("The stiffness of the force pulling the object towards the target position. Higher values mean a tighter sync but more potential for jitter.")]
        [SerializeField] private float _springConstant = 80f;

        [Tooltip("The resistance applied to smooth out the movement and match the target's velocity. Helps prevent oscillation.")]
        [SerializeField] private float _dampingConstant = 5f;
        
        [Header("Rotation Correction")]
        [Tooltip("Multiplier for rotation correction strength relative to position correction.")]
        [SerializeField] private float _rotationSpringMultiplier = 0.3f;

        [Tooltip("Minimum angle error (degrees) before applying rotation correction.")]
        [SerializeField] private float _minRotationCorrectionAngle = 1f;
        
        [Header("Dynamic Spring Scaling")]
        [Tooltip("How much to reduce spring strength based on recent acceleration. Higher = more reduction during collisions.")]
        [SerializeField] private float _uncertaintySpringDampening = 0.5f;

        [Tooltip("How quickly the tracked acceleration decays back to zero (per 50ms).")]
        [SerializeField] private float _accelerationDecay = 0.85f;

        [Header("Dynamic Hard Correction")]
        [Tooltip("How much to increase hard correction threshold based on recent acceleration.")]
        [SerializeField] private float _hardCorrectionAccelerationScale = 0.1f;

        [Tooltip("Maximum multiplier for the hard correction threshold.")]
        [SerializeField] private float _maxHardCorrectionMultiplier = 5f;

        [Header("Stabilization & Prediction")]
        [Tooltip("Speed under which we relax the spring to prevent jitter while rolling slowly.")]
        [SerializeField] private float _lowSpeedThreshold = 1.5f;

        [Tooltip("How much to weaken the spring at low speeds (0.1 = 10% strength). helps smoother stops.")]
        [SerializeField] private float _lowSpeedSpringMultiplier = 0.1f;

        [Tooltip("Whether the target position should extrapolate based on local ping")]
        [SerializeField] private bool _extrapolateBasedOnPing;

        [Header("Sync Settings")]
        [Tooltip("Minimum distance moved required to trigger a network update.")]
        [SerializeField] private float _positionChangeThreshold = 0.001f;

        [Tooltip("Minimum angle rotated required to trigger a network update.")]
        [SerializeField] private float _rotationChangeThreshold = 0.001f;

        [Tooltip("If linear and angular velocities are below this value, the object is considered stopped and will stop sending updates.")]
        [SerializeField] private float _velocityStopThreshold = 0.001f;

        private Rigidbody _rigidbody;
        
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _targetLinearVelocity;
        private Vector3 _targetAngularVelocity;
        private Vector3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        private Vector3 _lastSyncedLinearVelocity;
        private Vector3 _lastSyncedAngularVelocity;
        private float _lastExtrapolation;
        private Vector3 _previousVelocity;
        private float _recentAccelerationMagnitude;
        
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
            _targetLinearVelocity = _rigidbody.linearVelocity;
            _targetAngularVelocity = _rigidbody.angularVelocity;
            
            _lastSyncedPosition = _rigidbody.position;
            _lastSyncedRotation = _rigidbody.rotation;
            _lastSyncedLinearVelocity = _rigidbody.linearVelocity;
            _lastSyncedAngularVelocity = _rigidbody.angularVelocity;

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
            if (!HasStateChanged() && !ShouldSyncWhenStopped())
                return;
            float? myPing = (!isServer && _extrapolateBasedOnPing) ? (float)NetworkManager.main.tickModule.rtt : null;
            var stateData = new RigidbodyStateData
            {
                position = _rigidbody.position,
                rotation = _rigidbody.rotation,
                linearVelocity = _rigidbody.linearVelocity,
                angularVelocity = _rigidbody.angularVelocity,
                senderPing = myPing
            };

            _targetPosition = _rigidbody.position;
            _targetRotation = _rigidbody.rotation;
            _targetLinearVelocity = _rigidbody.linearVelocity;
            _targetAngularVelocity = _rigidbody.angularVelocity;

            if (isServer)
                SyncState(stateData);
            else
                SendStateToServer(stateData);

            _lastSyncedPosition = _rigidbody.position;
            _lastSyncedRotation = _rigidbody.rotation;
            _lastSyncedLinearVelocity = _rigidbody.linearVelocity;
            _lastSyncedAngularVelocity = _rigidbody.angularVelocity;
        }

        private void NonControllerTick(float delta)
        {
            if (_hasPendingTeleport)
                return;

            TrackAcceleration(delta);

            float error = Vector3.Distance(_rigidbody.position, _targetPosition);
            float dynamicHardThreshold = GetDynamicHardCorrectionThreshold();

            if (error < _acceptableError)
            {
                _isCorreting = false;
                _correctionTimer = 0f;
                return;
            }

            if (error >= dynamicHardThreshold)
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

        private float GetDynamicHardCorrectionThreshold()
        {
            float scale = 1f + _recentAccelerationMagnitude * _hardCorrectionAccelerationScale;
            scale = Mathf.Min(scale, _maxHardCorrectionMultiplier);
            return _hardCorrectionThreshold * scale;
        }

        private void ApplySoftCorrection()
        {
            float currentSpeed = _rigidbody.linearVelocity.magnitude;
            float springScale = GetDynamicSpringScale();
            float baseSpring = _springConstant;
            float dynamicDamping = _dampingConstant;

            if (currentSpeed < _lowSpeedThreshold)
            {
                float factor = Mathf.Clamp01(currentSpeed / _lowSpeedThreshold);
                baseSpring = Mathf.Lerp(_springConstant * _lowSpeedSpringMultiplier, _springConstant, factor);
            }
            else
            {
                baseSpring = _springConstant * springScale;
                dynamicDamping = _dampingConstant * springScale;
            }

            Vector3 positionError = _targetPosition - _rigidbody.position;
            Vector3 springForce = positionError * (baseSpring * _rigidbody.mass);
    
            Vector3 velocityError = _targetLinearVelocity - _rigidbody.linearVelocity;
            Vector3 dampingForce = velocityError * (dynamicDamping * _rigidbody.mass);
    
            _rigidbody.AddForce(springForce + dampingForce);

            Quaternion rotationError = _targetRotation * Quaternion.Inverse(_rigidbody.rotation);
            rotationError.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
    
            if (Mathf.Abs(angle) > _minRotationCorrectionAngle)
            {
                float angularVelocityDiff = Vector3.Distance(_targetAngularVelocity, _rigidbody.angularVelocity);
                float rotationUrgency = Mathf.Clamp01(angularVelocityDiff / 5f);
    
                float rotationSpring = baseSpring * _rotationSpringMultiplier * (0.2f + 0.8f * rotationUrgency);
                Vector3 torque = axis * (angle * Mathf.Deg2Rad * rotationSpring * _rigidbody.mass);
    
                Vector3 angularVelocityError = _targetAngularVelocity - _rigidbody.angularVelocity;
                Vector3 angularDamping = angularVelocityError * (dynamicDamping * _rotationSpringMultiplier * _rigidbody.mass);
    
                _rigidbody.AddTorque(torque + angularDamping);
            }
        }
        
        private float GetDynamicSpringScale()
        {
            float uncertainty = _recentAccelerationMagnitude;
            return 1f / (1f + uncertainty * _uncertaintySpringDampening);
        }

        private float GetDynamicSpringConstant()
        {
            return _springConstant * GetDynamicSpringScale();
        }

        private void HardCorrect()
        {
            _rigidbody.MovePosition(_targetPosition);
            _rigidbody.MoveRotation(_targetRotation);
            _isCorreting = false;
            _correctionTimer = 0f;
        }
        
        private void TrackAcceleration(float delta)
        {
            if (delta <= 0) return;
    
            Vector3 currentAccel = (_rigidbody.linearVelocity - _previousVelocity) / delta;
            float decay = Mathf.Pow(_accelerationDecay, delta / 0.05f);
            _recentAccelerationMagnitude = Mathf.Max(
                _recentAccelerationMagnitude * decay, 
                currentAccel.magnitude
            );
            _previousVelocity = _rigidbody.linearVelocity;
        }
        
        private bool HasStateChanged()
        {
            float positionDelta = Vector3.Distance(_rigidbody.position, _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(_rigidbody.rotation, _lastSyncedRotation);
            float linearVelocityDelta = Vector3.Distance(_rigidbody.linearVelocity, _lastSyncedLinearVelocity);
            float angularVelocityDelta = Vector3.Distance(_rigidbody.angularVelocity, _lastSyncedAngularVelocity);

            return positionDelta > _positionChangeThreshold || rotationDelta > _rotationChangeThreshold || linearVelocityDelta > _velocityStopThreshold || angularVelocityDelta > _velocityStopThreshold;
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
                BroadcastForceToOthers(appliedForce);
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
                BroadcastForceToOthers(appliedForce);
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
                BroadcastForceToOthers(appliedForce);
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

            float extrapolationTime = 0f;

            if (_extrapolateBasedOnPing)
            {
                if (isServer)
                {
                    if (data.senderPing.HasValue)
                        extrapolationTime = data.senderPing.Value / 2f;
                }
                else
                {
                    extrapolationTime = (float)NetworkManager.main.tickModule.rtt / 2f;
                }
            }
            _lastExtrapolation = extrapolationTime;
            _targetPosition = data.position + (data.linearVelocity * extrapolationTime);
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
        }

        [ServerRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SendStateToServer(RigidbodyStateData data)
        {
            float extrapolationTime = 0f;

            if (_extrapolateBasedOnPing)
            {
                if (data.senderPing.HasValue)
                    extrapolationTime = data.senderPing.Value / 2f;
            }
            _lastExtrapolation = extrapolationTime;
            _targetPosition = data.position + (data.linearVelocity * extrapolationTime);
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;

            SyncState(data);
        }

        [ObserversRpc(runLocally: true, channel: Channel.Unreliable)]
        private void BroadcastForce(AppliedForce force)
        {
            ApplyForce(force);
        }
        
        [ObserversRpc(excludeOwner: true, channel: Channel.Unreliable)]
        private void BroadcastForceToOthers(AppliedForce force)
        {
            ApplyForce(force);
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
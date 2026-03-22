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

    struct TimestampedSnapshot
    {
        public double time;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
    }

    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("PurrNet/Network Rigidbody")]
    public partial class NetworkRigidbody : NetworkIdentity, ITick
    {
        [Header("Authority")]
        [Tooltip("If true, the client owning the object calculates physics (Client Auth). If false, the server calculates physics (Server Auth).")]
        [SerializeField] private bool _ownerAuth;

        [Header("Correction")]
        [Tooltip("How far behind real-time (in seconds) the interpolation target sits. Higher values absorb more jitter but add latency.")]
        [SerializeField] private float _interpolationDelay = 0.1f;

        [Tooltip("How aggressively the rigidbody chases the target position. Acts as the natural frequency of a critically-damped spring.")]
        [SerializeField] private float _positionStrength = 5f;

        [Tooltip("How aggressively the rigidbody corrects rotation. Lower than position strength to avoid jitter after collisions.")]
        [SerializeField] private float _rotationStrength = 3f;

        [Tooltip("If the position error exceeds this distance, teleport instead of using forces.")]
        [SerializeField] private float _hardSnapDistance = 3f;

        [Tooltip("If the rotation error (degrees) exceeds this threshold, snap rotation instead of using torque.")]
        [SerializeField] private float _hardSnapAngle = 210f;

        [Tooltip("Position error below which correction forces stop. Prevents micro-jitter at rest.")]
        [SerializeField] private float _acceptablePositionError = 0.05f;

        [Tooltip("Rotation error (degrees) below which rotation correction stops.")]
        [SerializeField] private float _acceptableRotationError = 1f;

        [Header("Sync")]
        [Tooltip("Minimum distance moved required to trigger a network update.")]
        [SerializeField] private float _positionChangeThreshold = 0.001f;

        [Tooltip("Minimum angle rotated required to trigger a network update.")]
        [SerializeField] private float _rotationChangeThreshold = 0.001f;

        [Tooltip("If linear and angular velocities are below this value, the object is considered stopped and will stop sending updates.")]
        [SerializeField] private float _velocityStopThreshold = 0.001f;

        private Rigidbody _rigidbody;

        private const int BUFFER_SIZE = 32;
        private readonly TimestampedSnapshot[] _snapshotBuffer = new TimestampedSnapshot[BUFFER_SIZE];
        private int _bufferHead;
        private int _bufferCount;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Vector3 _targetLinearVelocity;
        private Vector3 _targetAngularVelocity;

        private Vector3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        private Vector3 _lastSyncedLinearVelocity;
        private Vector3 _lastSyncedAngularVelocity;

        private bool _hasPendingTeleport;
        private bool _receivedFirstSnapshot;
        private string _lastCorrectionReason = "No";
        private Vector3 _latestRawSnapshotPos;

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

            var pos = _rigidbody.position;
            var rot = _rigidbody.rotation;
            var linVel = GetLinearVelocity();
            var angVel = _rigidbody.angularVelocity;

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;

            _latestRawSnapshotPos = pos;
            _receivedFirstSnapshot = IsController(_ownerAuth);

            ClearBuffer();

            if (IsController(_ownerAuth))
            {
                SyncSettings(GetCurrentSettings());
                SyncInitialState(new RigidbodyStateData
                {
                    position = pos,
                    rotation = rot,
                    linearVelocity = linVel,
                    angularVelocity = angVel
                });
            }
        }

        public void OnTick(float delta)
        {
            if (!isActiveAndEnabled)
                return;

            if (IsController(_ownerAuth))
                ControllerTick();
            else
                NonControllerTick();
        }

        private void ControllerTick()
        {
            if (!HasStateChanged() && !ShouldSyncWhenStopped())
                return;

            var stateData = new RigidbodyStateData
            {
                position = _rigidbody.position,
                rotation = _rigidbody.rotation,
                linearVelocity = GetLinearVelocity(),
                angularVelocity = _rigidbody.angularVelocity
            };

            _targetPosition = _rigidbody.position;
            _targetRotation = _rigidbody.rotation;
            _targetLinearVelocity = GetLinearVelocity();
            _targetAngularVelocity = _rigidbody.angularVelocity;

            if (isServer)
                SyncState(stateData);
            else
                SendStateToServer(stateData);

            _lastSyncedPosition = _rigidbody.position;
            _lastSyncedRotation = _rigidbody.rotation;
            _lastSyncedLinearVelocity = GetLinearVelocity();
            _lastSyncedAngularVelocity = _rigidbody.angularVelocity;
        }

        private void NonControllerTick()
        {
            if (_hasPendingTeleport)
                return;

            SampleBuffer();
        }

        private void FixedUpdate()
        {
            if (!isSpawned || IsController(_ownerAuth) || _hasPendingTeleport || !_receivedFirstSnapshot)
                return;

            float positionError = Vector3.Distance(_rigidbody.position, _targetPosition);
            float rotationError = Quaternion.Angle(_rigidbody.rotation, NormalizeQuaternion(_targetRotation));

            if (positionError >= _hardSnapDistance)
            {
                _lastCorrectionReason = "Hard (Distance)";
                HardCorrect();
                return;
            }

            bool hardSnapRotation = rotationError > _hardSnapAngle;
            if (hardSnapRotation)
            {
                _lastCorrectionReason = "Hard (Rotation)";
                _rigidbody.MoveRotation(NormalizeQuaternion(_targetRotation));
                _rigidbody.angularVelocity = _targetAngularVelocity;
            }

            bool correctPosition = positionError > _acceptablePositionError;
            bool correctRotation = !hardSnapRotation && rotationError > _acceptableRotationError;

            if (!correctPosition && !correctRotation)
            {
                if (!hardSnapRotation)
                    _lastCorrectionReason = "No";
                MatchVelocity();
                return;
            }

            if (hardSnapRotation)
                _lastCorrectionReason = correctPosition ? "Hard (Rotation) + Position" : "Hard (Rotation)";
            else if (correctPosition && correctRotation)
                _lastCorrectionReason = "Position+Rotation";
            else if (correctPosition)
                _lastCorrectionReason = "Position";
            else
                _lastCorrectionReason = "Rotation";

            ApplyCorrection(correctPosition, correctRotation);
        }

        private void ApplyCorrection(bool correctPosition, bool correctRotation)
        {
            float m = _rigidbody.mass;

            if (correctPosition)
            {
                float w = _positionStrength;
                Vector3 posError = _targetPosition - _rigidbody.position;
                Vector3 velError = _targetLinearVelocity - GetLinearVelocity();
                _rigidbody.AddForce((posError * (w * w) + velError * (2f * w)) * m);
            }

            if (correctRotation)
            {
                float w = _rotationStrength;
                Quaternion rotError = NormalizeQuaternion(_targetRotation) * Quaternion.Inverse(_rigidbody.rotation);
                rotError.ToAngleAxis(out float angle, out Vector3 axis);

                if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f)
                    return;

                if (angle > 180f) angle -= 360f;

                if (Mathf.Abs(angle) > _acceptableRotationError)
                {
                    Vector3 angError = axis * (angle * Mathf.Deg2Rad);
                    Vector3 angVelError = _targetAngularVelocity - _rigidbody.angularVelocity;
                    Vector3 torque = (angError * (w * w) + angVelError * (2f * w)) * m;

                    float maxTorque = w * w * m;
                    float torqueMag = torque.magnitude;
                    if (torqueMag > maxTorque)
                        torque *= maxTorque / torqueMag;

                    _rigidbody.AddTorque(torque);
                }
            }
        }

        private void MatchVelocity()
        {
            float m = _rigidbody.mass;

            Vector3 velError = _targetLinearVelocity - GetLinearVelocity();
            if (velError.sqrMagnitude > 0.001f)
                _rigidbody.AddForce(velError * (_positionStrength * m));

            Vector3 angVelError = _targetAngularVelocity - _rigidbody.angularVelocity;
            if (angVelError.sqrMagnitude > 0.001f)
                _rigidbody.AddTorque(angVelError * (_rotationStrength * m));
        }

        private void HardCorrect()
        {
            _rigidbody.MovePosition(_targetPosition);
            _rigidbody.MoveRotation(NormalizeQuaternion(_targetRotation));
            SetLinearVelocity(_targetLinearVelocity);
            _rigidbody.angularVelocity = _targetAngularVelocity;
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float dot = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (dot < 0.0001f)
                return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(dot);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        #region Snapshot Buffer

        private void ClearBuffer()
        {
            _bufferHead = 0;
            _bufferCount = 0;
        }

        private void PushSnapshot(RigidbodyStateData data)
        {
            _snapshotBuffer[_bufferHead] = new TimestampedSnapshot
            {
                time = Time.unscaledTimeAsDouble,
                position = data.position,
                rotation = data.rotation,
                linearVelocity = data.linearVelocity,
                angularVelocity = data.angularVelocity
            };

            _bufferHead = (_bufferHead + 1) % BUFFER_SIZE;
            if (_bufferCount < BUFFER_SIZE)
                _bufferCount++;

            _receivedFirstSnapshot = true;
            _latestRawSnapshotPos = data.position;
        }

        private TimestampedSnapshot GetSnapshot(int logicalIndex)
        {
            int start = (_bufferHead - _bufferCount + BUFFER_SIZE) % BUFFER_SIZE;
            int actual = (start + logicalIndex) % BUFFER_SIZE;
            return _snapshotBuffer[actual];
        }

        private void SampleBuffer()
        {
            if (_bufferCount == 0)
                return;

            if (_bufferCount == 1)
            {
                var only = GetSnapshot(0);
                _targetPosition = only.position;
                _targetRotation = only.rotation;
                _targetLinearVelocity = only.linearVelocity;
                _targetAngularVelocity = only.angularVelocity;
                return;
            }

            double renderTime = Time.unscaledTimeAsDouble - _interpolationDelay;

            var oldest = GetSnapshot(0);
            var newest = GetSnapshot(_bufferCount - 1);

            if (renderTime <= oldest.time)
            {
                _targetPosition = oldest.position;
                _targetRotation = oldest.rotation;
                _targetLinearVelocity = oldest.linearVelocity;
                _targetAngularVelocity = oldest.angularVelocity;
                return;
            }

            if (renderTime >= newest.time)
            {
                float overshoot = (float)(renderTime - newest.time);
                float decay = Mathf.Exp(-2f * overshoot);
                _targetPosition = newest.position + newest.linearVelocity * overshoot * decay;
                _targetRotation = newest.rotation;
                _targetLinearVelocity = newest.linearVelocity * decay;
                _targetAngularVelocity = newest.angularVelocity * decay;
                return;
            }

            for (int i = 0; i < _bufferCount - 1; i++)
            {
                var a = GetSnapshot(i);
                var b = GetSnapshot(i + 1);

                if (renderTime >= a.time && renderTime <= b.time)
                {
                    float span = (float)(b.time - a.time);
                    if (span < 0.0001f)
                    {
                        _targetPosition = b.position;
                        _targetRotation = b.rotation;
                        _targetLinearVelocity = b.linearVelocity;
                        _targetAngularVelocity = b.angularVelocity;
                        return;
                    }

                    float t = (float)(renderTime - a.time) / span;
                    HermiteInterpolate(a, b, span, t);
                    return;
                }
            }

            _targetPosition = newest.position;
            _targetRotation = newest.rotation;
            _targetLinearVelocity = newest.linearVelocity;
            _targetAngularVelocity = newest.angularVelocity;
        }

        private void HermiteInterpolate(TimestampedSnapshot a, TimestampedSnapshot b, float dt, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            _targetPosition = h00 * a.position
                            + h10 * a.linearVelocity * dt
                            + h01 * b.position
                            + h11 * b.linearVelocity * dt;

            _targetLinearVelocity = Vector3.Lerp(a.linearVelocity, b.linearVelocity, t);
            _targetRotation = Quaternion.Slerp(a.rotation, b.rotation, t);
            _targetAngularVelocity = Vector3.Lerp(a.angularVelocity, b.angularVelocity, t);
        }

        #endregion

        #region Helpers

        private Vector3 GetLinearVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return _rigidbody.linearVelocity;
#else
            return _rigidbody.velocity;
#endif
        }

        private void SetLinearVelocity(Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearVelocity = value;
#else
            _rigidbody.velocity = value;
#endif
        }

        private bool HasStateChanged()
        {
            float positionDelta = Vector3.Distance(_rigidbody.position, _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(_rigidbody.rotation, _lastSyncedRotation);
            float linearVelocityDelta = Vector3.Distance(GetLinearVelocity(), _lastSyncedLinearVelocity);
            float angularVelocityDelta = Vector3.Distance(_rigidbody.angularVelocity, _lastSyncedAngularVelocity);

            return positionDelta > _positionChangeThreshold
                || rotationDelta > _rotationChangeThreshold
                || linearVelocityDelta > _velocityStopThreshold
                || angularVelocityDelta > _velocityStopThreshold;
        }

        private bool ShouldSyncWhenStopped()
        {
            return GetLinearVelocity().magnitude < _velocityStopThreshold
                && _rigidbody.angularVelocity.magnitude < _velocityStopThreshold
                && !_rigidbody.IsSleeping();
        }

        private RigidbodySettingsData GetCurrentSettings()
        {
            return new RigidbodySettingsData
            {
                mass = _rigidbody.mass,
#if UNITY_6000_0_OR_NEWER
                drag = _rigidbody.linearDamping,
                angularDrag = _rigidbody.angularDamping,
#else
                drag = _rigidbody.drag,
                angularDrag = _rigidbody.angularDrag,
#endif
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

        #endregion

        #region Public API

        public Vector3 linearVelocity
        {
            get => GetLinearVelocity();
            set => SetLinearVelocity(value);
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
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float drag
        {
#if UNITY_6000_0_OR_NEWER
            get => _rigidbody.linearDamping;
            set
            {
                _rigidbody.linearDamping = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
#else
            get => _rigidbody.drag;
            set
            {
                _rigidbody.drag = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
#endif
        }

        public float angularDrag
        {
#if UNITY_6000_0_OR_NEWER
            get => _rigidbody.angularDamping;
            set
            {
                _rigidbody.angularDamping = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
#else
            get => _rigidbody.angularDrag;
            set
            {
                _rigidbody.angularDrag = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
#endif
        }

        public bool useGravity
        {
            get => _rigidbody.useGravity;
            set
            {
                _rigidbody.useGravity = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public bool isKinematic
        {
            get => _rigidbody.isKinematic;
            set
            {
                _rigidbody.isKinematic = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
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
                if (isActiveAndEnabled)
                    BroadcastForceToOthers(appliedForce);
            }
            else if (isActiveAndEnabled)
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
                if (isActiveAndEnabled)
                    BroadcastForceToOthers(appliedForce);
            }
            else if (isActiveAndEnabled)
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
                if (isActiveAndEnabled)
                    BroadcastForceToOthers(appliedForce);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
        }

        public void MovePosition(Vector3 position)
        {
            if (IsController(_ownerAuth))
            {
                _rigidbody.MovePosition(position);
                if (isActiveAndEnabled)
                    BroadcastTeleport();
            }
            else if (isActiveAndEnabled)
            {
                RequestTeleport(position, _rigidbody.rotation);
            }
        }

        public void MoveRotation(Quaternion rotation)
        {
            if (IsController(_ownerAuth))
            {
                _rigidbody.MoveRotation(rotation);
                if (isActiveAndEnabled)
                    BroadcastTeleport();
            }
            else if (isActiveAndEnabled)
            {
                RequestTeleport(_rigidbody.position, rotation);
            }
        }

        #endregion

        #region RPCs

        [ObserversRpc(channel: Channel.ReliableOrdered, bufferLast: true, deltaPacked: true)]
        private void SyncInitialState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);

            _rigidbody.MovePosition(data.position);
            _rigidbody.MoveRotation(NormalizeQuaternion(data.rotation));
            SetLinearVelocity(data.linearVelocity);
            _rigidbody.angularVelocity = data.angularVelocity;

            _targetPosition = data.position;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
        }

        [ObserversRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SyncState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);
        }

        [ServerRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SendStateToServer(RigidbodyStateData data)
        {
            PushSnapshot(data);
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

            _lastCorrectionReason = "Teleport";
            _hasPendingTeleport = true;

            _rigidbody.MovePosition(data.position);
            _rigidbody.MoveRotation(NormalizeQuaternion(data.rotation));
            SetLinearVelocity(data.linearVelocity);
            _rigidbody.angularVelocity = data.angularVelocity;

            _targetPosition = data.position;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;

            ClearBuffer();
            _hasPendingTeleport = false;
        }

        [ServerRpc(deltaPacked: true)]
        private void SyncSettings(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
            SyncSettings_Observer(data);
        }

        [ObserversRpc(bufferLast: true, deltaPacked: true, excludeSender: true)]
        private void SyncSettings_Observer(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
        }

        private void SyncSettings_Internal(RigidbodySettingsData data)
        {
            if (IsController(_ownerAuth))
                return;

            _rigidbody.mass = data.mass;
#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearDamping = data.drag;
            _rigidbody.angularDamping = data.angularDrag;
#else
            _rigidbody.drag = data.drag;
            _rigidbody.angularDrag = data.angularDrag;
#endif
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
            Teleport(new RigidbodyTeleportData
            {
                position = _rigidbody.position,
                rotation = _rigidbody.rotation,
                linearVelocity = GetLinearVelocity(),
                angularVelocity = _rigidbody.angularVelocity
            });
        }

        #endregion
    }
}

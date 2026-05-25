using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public enum RigidbodyTransformSpace : byte
    {
        Local = 0,
        World = 1
    }

    /// <summary>
    /// Reference frame a wire position was encoded in. Travels with the state so
    /// the receiver decodes the correct field regardless of whether the parent
    /// identity reference has resolved yet, or whether a position transform is
    /// installed on the receiving peer.
    /// </summary>
    public enum RigidbodyPositionFrame : byte
    {
        /// <summary>Parent-local; the value lives in the <c>position</c> field.</summary>
        ParentLocal = 0,
        /// <summary>Peer-agnostic absolute (via a position transform); the value lives in <c>absolutePosition</c>.</summary>
        Absolute = 1,
        /// <summary>Raw Unity world space; the value lives in the <c>position</c> field.</summary>
        World = 2
    }

    public struct AppliedForce
    {
        public HalfVector3 force;
        public CompressedVector3? position;
        public ForceMode mode;
        public bool isTorque;
    }

    public struct RigidbodyStateData
    {
        /// <summary>Quantized position. Carries the value for the ParentLocal and
        /// World frames; default otherwise.</summary>
        public CompressedVector3 position;
        /// <summary>Absolute peer-agnostic position. Carries the value for the
        /// Absolute frame; default otherwise. Delta-packed, so it costs nothing
        /// on the legacy path.</summary>
        public double3 absolutePosition;
        /// <summary>Frame the position fields were encoded in. Decode keys on this
        /// rather than on parent-reference resolution or receiver-side state.</summary>
        public RigidbodyPositionFrame positionFrame;
        public PackedQuaternion rotation;
        public HalfVector3 linearVelocity;
        public HalfVector3 angularVelocity;
        public NetworkIdentity parent;
    }

    public struct RigidbodyTeleportData
    {
        public CompressedVector3 position;
        public double3 absolutePosition;
        public RigidbodyPositionFrame positionFrame;
        public PackedQuaternion rotation;
        public HalfVector3 linearVelocity;
        public HalfVector3 angularVelocity;
        public NetworkIdentity parent;
    }

    public struct RigidbodySettingsData
    {
        public Half mass;
        public Half drag;
        public Half angularDrag;
        public bool useGravity;
        public bool isKinematic;
    }

    struct TimestampedSnapshot
    {
        public double time;
        /// <summary>Position in the sync frame: parent-local when parented,
        /// otherwise the absolute peer-agnostic frame (origin-invariant either
        /// way, so it survives a local origin shift).</summary>
        public double3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public Transform parent;
    }

    [AddComponentMenu("PurrNet/Network Rigidbody")]
    public partial class NetworkRigidbody : NetworkIdentity, ITick
    {
        [Header("Authority")]
        [Tooltip("If true, the client owning the object calculates physics (Client Auth). If false, the server calculates physics (Server Auth).")]
        [SerializeField] private bool _ownerAuth = true;

        [Tooltip("Space used to sync position and rotation. Local is relative to the current parent, World is absolute.")]
        [SerializeField] private RigidbodyTransformSpace _space = RigidbodyTransformSpace.Local;

        [Tooltip("Whether to sync parent changes (SetParent) through the hierarchy. Only works when the new parent has a NetworkIdentity.")]
        [SerializeField] private bool _syncParent = true;

        [Header("Settings Override")]
        [Tooltip("Optional. When assigned, this asset's Create() builds a per-instance correction object that controls all correction decisions. The fields below are passed as defaults via the correction context.")]
        [SerializeField] private NetworkRigidbodySettings _settingsOverride;

        private NetworkRigidbodySettingsInstance _settingsInstance;
        private NetworkRigidbodySettings _settingsInstanceSource;

        [Header("Correction")]
        [Tooltip("How far behind real-time (in seconds) the interpolation target sits. Higher values absorb more jitter but add latency.")]
        [SerializeField] private float _interpolationDelay = 0.05f;

        [Tooltip("Pushes the target position forward using velocity and estimated acceleration. The offset is interpolationDelay * predictionFactor, making it identical on all machines regardless of network role. 0 = no prediction, 1 = compensate for interpolation delay, >1 = predict further ahead.")]
        [SerializeField] private float _predictionFactor;

        [Tooltip("Maximum time in seconds a receiver can extrapolate from the newest snapshot before holding the target near that snapshot.")]
        [SerializeField] private float _maxExtrapolationDuration = 0.25f;

        [Tooltip("How aggressively the rigidbody chases the target position. Acts as the natural frequency of a critically-damped spring.")]
        [SerializeField] private float _positionStrength = 5f;

        [Tooltip("The distance over which position correction ramps from zero to full strength. Larger values give softer correction, letting local collisions play out before being pulled back.")]
        [SerializeField] private float _correctionRange = 2f;

        [Tooltip("How aggressively the rigidbody corrects rotation.")]
        [SerializeField] private float _rotationStrength = 3f;

        [Tooltip("If the position error exceeds this distance, teleport instead of using forces.")]
        [SerializeField] private float _hardSnapDistance = 3f;

        [Tooltip("If true, resets the rigidbody linear velocity once the hard snap distance is exceeded.")]
        [SerializeField] private bool _resetLinearVelocityOnSnap = false;

        [Tooltip("If the rotation error (degrees) exceeds this threshold, snap rotation instead of using torque. Negative to disable.")]
        [SerializeField] private float _hardSnapAngle = 210f;

        [Tooltip("If true, resets the rigidbody angular velocity once the hard snap angle is exceeded.")]
        [SerializeField] private bool _resetAngularVelocityOnSnap = false;

        [Tooltip("Rotation error (degrees) below which rotation correction stops. Negative to disable rotation correction entirely.")]
        [SerializeField] private float _acceptableRotationError = 1f;

        [Header("Sync")]
        [Tooltip("Minimum distance moved required to trigger a network update.")]
        [SerializeField] private float _positionChangeThreshold = 0.001f;

        [Tooltip("Minimum angle rotated required to trigger a network update.")]
        [SerializeField] private float _rotationChangeThreshold = 0.001f;

        [Tooltip("If linear and angular velocities are below this value, the object is considered stopped and will stop sending updates.")]
        [SerializeField] private float _velocityStopThreshold = 0.001f;

        private Rigidbody _cachedRigidbody;
        private Rigidbody _rigidbody => _cachedRigidbody ? _cachedRigidbody : (_cachedRigidbody = GetComponent<Rigidbody>());

        private const int BUFFER_SIZE = 32;
        private readonly TimestampedSnapshot[] _snapshotBuffer = new TimestampedSnapshot[BUFFER_SIZE];
        private int _bufferHead;
        private int _bufferCount;

        private double3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Vector3 _targetLinearVelocity;
        private Vector3 _targetAngularVelocity;
        /// <summary>Reference frame for _target* values. Null means world-space.</summary>
        private Transform _targetParent;

        private double3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        private Vector3 _lastSyncedLinearVelocity;
        private Vector3 _lastSyncedAngularVelocity;
        private Transform _lastSyncedParent;
        private bool _lastSyncedWasSettled;

        private bool _hasPendingTeleport;
        private bool _isIgnoringParentChanges;

        private double _forceSyncWindowEndTime = double.NegativeInfinity;
        private bool _forceSyncOneShot;
        private bool _wasInForceSyncWindow;

        private string _lastCorrectionReason = "No";
        private double3 _latestRawSnapshotPos;
        private Transform _latestRawSnapshotParent;
        private string _bufferSampleMode = "None";
        private double _lastLogTime;
        private float _predictionOffset;

        /// <summary>
        /// Process-wide fallback used when a NetworkRigidbody has no runtime override
        /// and no sibling component implementing <see cref="INetworkRigidbodyPositionTransform"/>.
        /// Default null preserves legacy wire behaviour.
        /// </summary>
        public static INetworkRigidbodyPositionTransform defaultPositionTransform { get; set; }

        private INetworkRigidbodyPositionTransform _positionTransform;
        private bool _positionTransformExplicit;

        private void Awake()
        {
            _cachedRigidbody = GetComponent<Rigidbody>();
        }

        protected override void OnEarlySpawn()
        {
            base.OnEarlySpawn();
            ResolvePositionTransform();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            ResolvePositionTransform();

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = ReadSyncPosition(parentTrs);
            var rot = ReadRotation(parentTrs);
            var linVel = ReadLinearVelocity(parentTrs);
            var angVel = ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledForSync();

            _latestRawSnapshotPos = pos;
            _latestRawSnapshotParent = parentTrs;
            ClearBuffer();

            EnsureSettingsInstance();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if (!_rigidbody)
                return;

            if (player == localPlayer)
                return;

            if (_ownerAuth && owner.HasValue && player == owner.Value)
                return;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var stateData = new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity
            };
            SendInitialStateToObserver(player, stateData, GetCurrentSettings());
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            DisposeSettingsInstance();
            _positionTransform = null;
            _positionTransformExplicit = false;
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);
            DisposeSettingsInstance();
            EnsureSettingsInstance();

            if (!isSpawned || !_rigidbody)
                return;

            if (!_ownerAuth)
                return;

            if (asServer)
            {
                var handoff = CaptureCurrentState();

                if (newOwner.HasValue && newOwner != localPlayer)
                    SendHandoffState(newOwner.Value, handoff);

                if (oldOwner.HasValue && newOwner != oldOwner && oldOwner != localPlayer)
                    SendHandoffState(oldOwner.Value, handoff);

                return;
            }

            if (newOwner == localPlayer && !isServer)
            {
                AdoptControllerStateFromRigidbody();
                return;
            }

            if (oldOwner == localPlayer && newOwner != localPlayer)
            {
                ClearBuffer();
                _forceSyncOneShot = false;
                _forceSyncWindowEndTime = double.NegativeInfinity;
                _wasInForceSyncWindow = false;
            }
        }

        private RigidbodyStateData CaptureCurrentState()
        {
            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            return new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity
            };
        }

        private void AdoptControllerStateFromRigidbody()
        {
            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var rot = ReadRotation(parentTrs);
            var linVel = ReadLinearVelocity(parentTrs);
            var angVel = ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;

            _latestRawSnapshotPos = pos;
            _latestRawSnapshotParent = parentTrs;
            ClearBuffer();

            if (!isActiveAndEnabled)
                return;

            SendStateToServer(new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = rot,
                linearVelocity = linVel,
                angularVelocity = angVel,
                parent = parentIdentity
            });
        }

        private void EnsureSettingsInstance()
        {
            if (!_settingsOverride)
            {
                if (_settingsInstance != null)
                    DisposeSettingsInstance();
                return;
            }

            if (_settingsInstance != null && _settingsInstanceSource == _settingsOverride)
                return;

            DisposeSettingsInstance();
            _settingsInstance = _settingsOverride.Create(this);
            _settingsInstanceSource = _settingsOverride;
        }

        private void DisposeSettingsInstance()
        {
            if (_settingsInstance == null)
                return;

            _settingsInstance.OnDespawned();
            _settingsInstance = null;
            _settingsInstanceSource = null;
        }

        public void OnTick(float delta)
        {
            if (!isActiveAndEnabled)
                return;

            if (IsController(_ownerAuth))
                ControllerTick();
            else
                NonControllerTick();

            TickForceSyncWindow();
        }

        private void ControllerTick()
        {
            if (!_rigidbody)
                return;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;
            bool isSettled = IsSettledForSync();
            bool shouldSendSettledState = isSettled && !_lastSyncedWasSettled;

            if (!isInForceSyncWindow && !shouldSendSettledState && !HasStateChanged(parentTrs) && !ShouldSyncWhenStopped())
                return;

            SendCurrentState(shouldSendSettledState, isSettled);
        }
        
        private void SendCurrentState(bool reliable, bool zeroVelocities)
        {
            if (!_rigidbody)
                return;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var rot = ReadRotation(parentTrs);
            var linVel = zeroVelocities ? Vector3.zero : ReadLinearVelocity(parentTrs);
            var angVel = zeroVelocities ? Vector3.zero : ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;

            var stateData = new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = rot,
                linearVelocity = linVel,
                angularVelocity = angVel,
                parent = parentIdentity
            };

            if (reliable)
                SendReliableState(stateData);
            else
                SendUnreliableState(stateData);

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(linVel, angVel);
        }

        private void SendUnreliableState(RigidbodyStateData stateData)
        {
            if (isServer)
                SyncState(stateData);
            else
                SendStateToServer(stateData);
        }

        private void SendReliableState(RigidbodyStateData stateData)
        {
            if (isServer)
                SyncReliableState(stateData);
            else
                SendReliableStateToServer(stateData);
        }

        private void NonControllerTick()
        {
            if (_hasPendingTeleport)
                return;

            SampleBuffer();
            _prePredictionTarget = _targetPosition;

            if (_predictionFactor > 0f)
            {
                float compensation = _interpolationDelay * _predictionFactor;
                _targetPosition += ToD3(_targetLinearVelocity * compensation);
                _predictionOffset = compensation;
            }
            else
            {
                _predictionOffset = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (!isFullySpawned || IsController(_ownerAuth) || _hasPendingTeleport)
                return;

            if (!_rigidbody)
                return;

            Vector3 worldTargetPos = ToWorldPosition(_targetPosition, _targetParent);
            Quaternion worldTargetRot = ToWorldRotation(_targetRotation, _targetParent);
            Vector3 worldTargetLinVel = ToWorldLinearVelocity(_targetLinearVelocity, _targetParent);
            Vector3 worldTargetAngVel = ToWorldAngularVelocity(_targetAngularVelocity, _targetParent);

            float positionError = Vector3.Distance(_rigidbody.position, worldTargetPos);
            float rotationError = Quaternion.Angle(_rigidbody.rotation, NormalizeQuaternion(worldTargetRot));

            EnsureSettingsInstance();

            if (_settingsInstance != null)
            {
                var ctx = BuildCorrectionContext(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel, positionError, rotationError);

                if (_settingsInstance.ShouldTeleport(in ctx))
                {
                    _lastCorrectionReason = "Hard (Distance)";
                    _settingsInstance.ApplyHardCorrection(in ctx);
                    _settingsInstance.OnReset(in ctx);
                    return;
                }

                bool hardSnapRotation = _settingsInstance.ShouldSnapRotation(in ctx);
                if (hardSnapRotation)
                {
                    _lastCorrectionReason = "Hard (Rotation)";
                    _rigidbody.MoveRotation(NormalizeQuaternion(worldTargetRot));
                    SetAngularVelocity(worldTargetAngVel);
                    _settingsInstance.OnReset(in ctx);
                }

                _settingsInstance.ApplyPositionCorrection(in ctx);

                if (!hardSnapRotation && _settingsInstance.ShouldCorrectRotation(in ctx))
                    _settingsInstance.ApplyRotationCorrection(in ctx);

                if (!hardSnapRotation)
                {
                    bool correctingRot = _settingsInstance.ShouldCorrectRotation(in ctx);
                    _lastCorrectionReason = correctingRot
                        ? "Position+Rotation (Override)"
                        : positionError > 0.001f ? "Position (Override)" : "No";
                }
                else if (positionError > 0.001f)
                {
                    _lastCorrectionReason = "Hard (Rotation) + Position";
                }
            }
            else
            {
                if (positionError >= _hardSnapDistance)
                {
                    _lastCorrectionReason = "Hard (Distance)";
                    HardCorrect(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel);
                    return;
                }

                bool hardSnapRotation = _hardSnapAngle >= 0 && _acceptableRotationError >= 0 && rotationError > _hardSnapAngle;
                if (hardSnapRotation)
                {
                    _lastCorrectionReason = "Hard (Rotation)";
                    _rigidbody.MoveRotation(NormalizeQuaternion(worldTargetRot));
                    SetAngularVelocity(_resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel);
                }

                bool correctRotation = !hardSnapRotation
                                     && _acceptableRotationError >= 0
                                     && rotationError > _acceptableRotationError;

                _lastCorrectionReason = hardSnapRotation
                    ? (positionError > 0.001f ? "Hard (Rotation) + Position" : "Hard (Rotation)")
                    : correctRotation
                        ? "Position+Rotation"
                        : positionError > 0.001f ? "Position" : "No";

                ApplyCorrection(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel, positionError, correctRotation);
            }
        }

        private void ApplyCorrection(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel, float positionError, bool correctRotation)
        {
            if (!CanApplyDynamicMotion())
                return;

            float m = _rigidbody.mass;
            float range = Mathf.Max(_correctionRange, 0.01f);
            float ratio = Mathf.Clamp01(positionError / range);

            {
                float w = _positionStrength;
                Vector3 posError = worldTargetPos - _rigidbody.position;
                Vector3 velError = worldTargetLinVel - GetLinearVelocity();

                Vector3 positionalPull = posError * (w * w * ratio);
                Vector3 velocityDamping = velError * (2f * w);

                Vector3 dragCompensation = GetLinearVelocity() * GetDrag();

                ApplyForceToRigidbody((positionalPull + velocityDamping + dragCompensation) * m);
            }

            if (correctRotation)
            {
                float w = _rotationStrength;
                Quaternion rotError = NormalizeQuaternion(worldTargetRot) * Quaternion.Inverse(_rigidbody.rotation);
                rotError.ToAngleAxis(out float angle, out Vector3 axis);

                if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f)
                    return;

                if (angle > 180f) angle -= 360f;

                Vector3 angError = axis * (angle * Mathf.Deg2Rad);
                Vector3 angVelError = worldTargetAngVel - _rigidbody.angularVelocity;
                Vector3 torque = (angError * (w * w) + angVelError * (2f * w)) * m;

                float maxTorque = w * w * m;
                float torqueMag = torque.magnitude;
                if (torqueMag > maxTorque)
                    torque *= maxTorque / torqueMag;

                ApplyTorqueToRigidbody(torque);
            }
        }

        private RigidbodyCorrectionContext BuildCorrectionContext(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel, float positionError, float rotationError)
        {
            return new RigidbodyCorrectionContext
            {
                rigidbody = _rigidbody,
                targetPosition = worldTargetPos,
                targetRotation = worldTargetRot,
                targetLinearVelocity = worldTargetLinVel,
                targetAngularVelocity = worldTargetAngVel,
                positionError = positionError,
                rotationError = rotationError,
                drag = GetDrag(),
                positionStrength = _positionStrength,
                correctionRange = _correctionRange,
                rotationStrength = _rotationStrength,
                hardSnapDistance = _hardSnapDistance,
                hardSnapAngle = _hardSnapAngle,
                acceptableRotationError = _acceptableRotationError
            };
        }

        private void HardCorrect(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel)
        {
            _rigidbody.position = worldTargetPos;
            _rigidbody.rotation = NormalizeQuaternion(worldTargetRot);
            SetLinearVelocity(_resetLinearVelocityOnSnap ? Vector3.zero : worldTargetLinVel);
            SetAngularVelocity(_resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel);
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
            var now = Time.unscaledTimeAsDouble;

            if (_bufferCount > 0)
            {
                int lastIndex = (_bufferHead - 1 + BUFFER_SIZE) % BUFFER_SIZE;
                double maxGap = Math.Max(0.5, _interpolationDelay * 4.0);
                if (now - _snapshotBuffer[lastIndex].time > maxGap)
                    ClearBuffer();
            }

            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);
            var parentTrs = ResolveParentTransform(data.parent, data.positionFrame);
            _snapshotBuffer[_bufferHead] = new TimestampedSnapshot
            {
                time = now,
                position = syncPos,
                rotation = data.rotation,
                linearVelocity = data.linearVelocity,
                angularVelocity = data.angularVelocity,
                parent = parentTrs
            };

            _bufferHead = (_bufferHead + 1) % BUFFER_SIZE;
            if (_bufferCount < BUFFER_SIZE)
                _bufferCount++;

            _latestRawSnapshotPos = syncPos;
            _latestRawSnapshotParent = parentTrs;
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
                AdoptSnapshot(only);
                _bufferSampleMode = "Single";
                return;
            }

            double renderTime = Time.unscaledTimeAsDouble - _interpolationDelay;

            var oldest = GetSnapshot(0);
            var newest = GetSnapshot(_bufferCount - 1);

            if (renderTime <= oldest.time)
            {
                AdoptSnapshot(oldest);
                _bufferSampleMode = "Clamp-Old";
                return;
            }

            if (renderTime >= newest.time)
            {
                float overshoot = (float)(renderTime - newest.time);
                float maxExtrapolation = Mathf.Max(0f, _maxExtrapolationDuration);
                float extrapolationTime = Mathf.Min(overshoot, maxExtrapolation);
                bool clamped = overshoot > maxExtrapolation;

                _targetPosition = newest.position + ToD3(newest.linearVelocity * extrapolationTime);
                _targetRotation = newest.rotation;
                _targetLinearVelocity = clamped ? Vector3.zero : newest.linearVelocity;
                _targetAngularVelocity = clamped ? Vector3.zero : newest.angularVelocity;
                _targetParent = newest.parent;
                _bufferSampleMode = clamped ? $"Extrap-Clamped ({overshoot:F3}s)" : $"Extrap ({overshoot:F3}s)";
                _predictionOffset = extrapolationTime;
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
                        AdoptSnapshot(b);
                        return;
                    }

                    float t = (float)(renderTime - a.time) / span;
                    HermiteInterpolate(a, b, span, t);
                    _bufferSampleMode = a.parent == b.parent ? $"Interp ({t:F2})" : $"Interp-Reparent ({t:F2})";
                    _predictionOffset = 0f;
                    return;
                }
            }

            AdoptSnapshot(newest);
        }

        private void AdoptSnapshot(TimestampedSnapshot snap)
        {
            _targetPosition = snap.position;
            _targetRotation = snap.rotation;
            _targetLinearVelocity = snap.linearVelocity;
            _targetAngularVelocity = snap.angularVelocity;
            _targetParent = snap.parent;
        }

        private void HermiteInterpolate(TimestampedSnapshot a, TimestampedSnapshot b, float dt, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            if (a.parent == b.parent)
            {
                _targetPosition = h00 * a.position
                                + ToD3(a.linearVelocity * (h10 * dt))
                                + h01 * b.position
                                + ToD3(b.linearVelocity * (h11 * dt));

                _targetLinearVelocity = Vector3.Lerp(a.linearVelocity, b.linearVelocity, t);
                _targetRotation = Quaternion.Slerp(a.rotation, b.rotation, t);
                _targetAngularVelocity = Vector3.Lerp(a.angularVelocity, b.angularVelocity, t);
                _targetParent = a.parent;
            }
            else
            {
                Vector3 aWorldPos = ToWorldPosition(a.position, a.parent);
                Vector3 bWorldPos = ToWorldPosition(b.position, b.parent);
                Vector3 aWorldLinVel = ToWorldLinearVelocity(a.linearVelocity, a.parent);
                Vector3 bWorldLinVel = ToWorldLinearVelocity(b.linearVelocity, b.parent);
                Quaternion aWorldRot = ToWorldRotation(a.rotation, a.parent);
                Quaternion bWorldRot = ToWorldRotation(b.rotation, b.parent);
                Vector3 aWorldAngVel = ToWorldAngularVelocity(a.angularVelocity, a.parent);
                Vector3 bWorldAngVel = ToWorldAngularVelocity(b.angularVelocity, b.parent);

                Vector3 worldResult = h00 * aWorldPos
                                    + aWorldLinVel * (h10 * dt)
                                    + h01 * bWorldPos
                                    + bWorldLinVel * (h11 * dt);
                _targetPosition = WorldToSyncNoParent(worldResult);

                _targetLinearVelocity = Vector3.Lerp(aWorldLinVel, bWorldLinVel, t);
                _targetRotation = Quaternion.Slerp(aWorldRot, bWorldRot, t);
                _targetAngularVelocity = Vector3.Lerp(aWorldAngVel, bWorldAngVel, t);
                _targetParent = null;
            }
        }

        #endregion

        #region Parent sync

        public bool syncParent => _syncParent;
        public bool ownerAuth => _ownerAuth;
        public new bool isController => IsController(_ownerAuth);

        public void StartIgnoringParentChanges()
        {
            _isIgnoringParentChanges = true;
        }

        public void StopIgnoringParentChanges()
        {
            _isIgnoringParentChanges = false;
        }

        private void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (!_syncParent)
                return;

            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                hierarchy.OnParentChanged(this, transform.parent);
            }
        }

        #endregion

        #region Helpers

        private NetworkIdentity GetSyncParentIdentity()
        {
            if (_space != RigidbodyTransformSpace.Local)
                return null;
            var parentTrs = transform.parent;
            if (!parentTrs)
                return null;
            return parentTrs.GetComponent<NetworkIdentity>();
        }

        private static double3 ToD3(Vector3 v) => new double3(v.x, v.y, v.z);
        private static Vector3 ToV3(double3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);

        /// <summary>
        /// Reads the rigidbody position into the origin-invariant sync frame:
        /// parent-local when parented, absolute (via the position transform) when
        /// unparented and a transform is installed, otherwise raw Unity world space.
        /// </summary>
        private double3 ReadSyncPosition(Transform parent)
        {
            var p = _rigidbody ? _rigidbody.position : transform.position;
            if (parent)
                return ToD3(parent.InverseTransformPoint(p));
            if (_positionTransform != null)
                return _positionTransform.ToAbsolute(this, p);
            return ToD3(p);
        }

        /// <summary>
        /// Reads the rigidbody position and fills the wire fields of a state struct.
        /// Exactly one of <paramref name="wirePos"/> / <paramref name="wireAbs"/>
        /// carries the value; the other stays default so it delta-packs away.
        /// <paramref name="frame"/> records which one, making the payload
        /// self-describing. Returns the same value in the sync frame.
        /// </summary>
        private double3 WriteWirePosition(Transform parent, out CompressedVector3 wirePos, out double3 wireAbs, out RigidbodyPositionFrame frame)
        {
            var p = _rigidbody ? _rigidbody.position : transform.position;
            if (parent)
            {
                var local = parent.InverseTransformPoint(p);
                wirePos = local;
                wireAbs = default;
                frame = RigidbodyPositionFrame.ParentLocal;
                return ToD3(local);
            }
            if (_positionTransform != null)
            {
                wirePos = default;
                wireAbs = _positionTransform.ToAbsolute(this, p);
                frame = RigidbodyPositionFrame.Absolute;
                return wireAbs;
            }
            wirePos = p;
            wireAbs = default;
            frame = RigidbodyPositionFrame.World;
            return ToD3(p);
        }

        /// <summary>Converts a Unity world-space position into the wire fields (unparented).</summary>
        private void WorldToWire(Vector3 worldPos, out CompressedVector3 wirePos, out double3 wireAbs, out RigidbodyPositionFrame frame)
        {
            if (_positionTransform != null)
            {
                wirePos = default;
                wireAbs = _positionTransform.ToAbsolute(this, worldPos);
                frame = RigidbodyPositionFrame.Absolute;
            }
            else
            {
                wirePos = worldPos;
                wireAbs = default;
                frame = RigidbodyPositionFrame.World;
            }
        }

        /// <summary>
        /// Decodes the wire fields of a received state into the sync frame, keyed
        /// purely on the encoded <paramref name="frame"/> so it never depends on
        /// the parent reference resolving or on receiver-side state.
        /// </summary>
        private double3 ExtractSyncPosition(RigidbodyPositionFrame frame, CompressedVector3 wirePos, double3 wireAbs)
        {
            return frame == RigidbodyPositionFrame.Absolute ? wireAbs : ToD3(wirePos);
        }

        /// <summary>
        /// Resolves the parent transform for a received state. Prefers the networked
        /// parent reference, but falls back to the local Unity parent when that
        /// reference has not resolved yet (the parent identity can register a frame
        /// or two after the child), so a ParentLocal payload still decodes correctly.
        /// </summary>
        private Transform ResolveParentTransform(NetworkIdentity wireParent, RigidbodyPositionFrame frame)
        {
            if (wireParent)
                return wireParent.transform;
            if (frame == RigidbodyPositionFrame.ParentLocal)
                return transform.parent;
            return null;
        }

        /// <summary>Converts an unparented Unity world-space position into the sync frame.</summary>
        private double3 WorldToSyncNoParent(Vector3 worldPos)
        {
            if (_positionTransform != null)
                return _positionTransform.ToAbsolute(this, worldPos);
            return ToD3(worldPos);
        }

        private Quaternion ReadRotation(Transform parent)
        {
            var r = _rigidbody ? _rigidbody.rotation : transform.rotation;
            return parent ? Quaternion.Inverse(parent.rotation) * r : r;
        }

        private Vector3 ReadLinearVelocity(Transform parent)
        {
            if (!_rigidbody)
                return Vector3.zero;
            var v = GetLinearVelocity();
            return parent ? parent.InverseTransformVector(v) : v;
        }

        private Vector3 ReadAngularVelocity(Transform parent)
        {
            if (!_rigidbody)
                return Vector3.zero;
            var v = _rigidbody.angularVelocity;
            return parent ? Quaternion.Inverse(parent.rotation) * v : v;
        }

        /// <summary>
        /// Converts a sync-frame position back into this peer's Unity world space:
        /// parent transform when parented, the position transform's inverse when
        /// unparented and a transform is installed, otherwise the value as-is.
        /// </summary>
        private Vector3 ToWorldPosition(double3 pos, Transform parent)
        {
            if (parent)
                return parent.TransformPoint(ToV3(pos));
            if (_positionTransform != null)
                return _positionTransform.ToLocal(this, pos);
            return ToV3(pos);
        }

        private static Quaternion ToWorldRotation(Quaternion rot, Transform parent)
        {
            return parent ? parent.rotation * rot : rot;
        }

        private static Vector3 ToWorldLinearVelocity(Vector3 v, Transform parent)
        {
            return parent ? parent.TransformVector(v) : v;
        }

        private static Vector3 ToWorldAngularVelocity(Vector3 v, Transform parent)
        {
            return parent ? parent.rotation * v : v;
        }

        private Vector3 GetLinearVelocity()
        {
            return NetworkRigidbodyPhysics.GetLinearVelocity(_rigidbody);
        }

        private void SetLinearVelocity(Vector3 value)
        {
            NetworkRigidbodyPhysics.SetLinearVelocity(_rigidbody, value);
        }

        private void SetAngularVelocity(Vector3 value)
        {
            NetworkRigidbodyPhysics.SetAngularVelocity(_rigidbody, value);
        }

        private void ApplyForceToRigidbody(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddForce(_rigidbody, force, mode);
        }

        private void ApplyForceAtPositionToRigidbody(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddForceAtPosition(_rigidbody, force, position, mode);
        }

        private void ApplyTorqueToRigidbody(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddTorque(_rigidbody, torque, mode);
        }

        private bool CanApplyDynamicMotion()
        {
            return NetworkRigidbodyPhysics.CanApplyDynamicMotion(_rigidbody);
        }

        private float GetDrag()
        {
#if UNITY_6000_0_OR_NEWER
            return _rigidbody.linearDamping;
#else
            return _rigidbody.drag;
#endif
        }

        private void SetDrag(float value)
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearDamping = value;
#else
            _rigidbody.drag = value;
#endif
        }

        private float GetAngularDrag()
        {
#if UNITY_6000_0_OR_NEWER
            return _rigidbody.angularDamping;
#else
            return _rigidbody.angularDrag;
#endif
        }

        private void SetAngularDrag(float value)
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.angularDamping = value;
#else
            _rigidbody.angularDrag = value;
#endif
        }

        private bool HasStateChanged(Transform parent)
        {
            if (parent != _lastSyncedParent)
                return true;

            double positionDelta = math.distance(ReadSyncPosition(parent), _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(ReadRotation(parent), _lastSyncedRotation);
            float linearVelocityDelta = Vector3.Distance(ReadLinearVelocity(parent), _lastSyncedLinearVelocity);
            float angularVelocityDelta = Vector3.Distance(ReadAngularVelocity(parent), _lastSyncedAngularVelocity);

            return positionDelta > _positionChangeThreshold
                || rotationDelta > _rotationChangeThreshold
                || linearVelocityDelta > _velocityStopThreshold
                || angularVelocityDelta > _velocityStopThreshold;
        }

        private bool IsSettledForSync()
        {
            if (!_rigidbody)
                return true;

            if (_rigidbody.isKinematic || _rigidbody.IsSleeping())
                return true;

            return IsSettledState(GetLinearVelocity(), _rigidbody.angularVelocity);
        }

        private bool IsSettledState(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            float threshold = Mathf.Max(0f, _velocityStopThreshold);
            float thresholdSqr = threshold * threshold;
            return linearVelocity.sqrMagnitude < thresholdSqr
                && angularVelocity.sqrMagnitude < thresholdSqr;
        }

        private bool ShouldSyncWhenStopped()
        {
            if (!_rigidbody)
                return false;
            return IsSettledState(GetLinearVelocity(), _rigidbody.angularVelocity)
                && !_rigidbody.IsSleeping();
        }

        private RigidbodySettingsData GetCurrentSettings()
        {
            if (!_rigidbody)
                return default;
            return new RigidbodySettingsData
            {
                mass = (Half)_rigidbody.mass,
                drag = (Half)GetDrag(),
                angularDrag = (Half)GetAngularDrag(),
                useGravity = _rigidbody.useGravity,
                isKinematic = _rigidbody.isKinematic
            };
        }

        private void ApplyForce(AppliedForce force)
        {
            if (!_rigidbody)
                return;

            if (force.isTorque)
                ApplyTorqueToRigidbody(force.force, force.mode);
            else if (force.position.HasValue)
                ApplyForceAtPositionToRigidbody(force.force, force.position.Value, force.mode);
            else
                ApplyForceToRigidbody(force.force, force.mode);
        }

        #endregion

        #region Public API

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

        public NetworkRigidbodySettingsInstance settingsInstance => _settingsInstance;

        /// <summary>
        /// Active position transform for this rigidbody, or null when positions
        /// travel on the wire in this peer's own Unity world space (legacy behaviour).
        /// </summary>
        public INetworkRigidbodyPositionTransform positionTransform => _positionTransform;

        /// <summary>
        /// Install a position transform at runtime, overriding any sibling component
        /// or static default. Pass null to fall back to the resolution chain on the
        /// next spawn.
        /// </summary>
        public void SetPositionTransform(INetworkRigidbodyPositionTransform transform)
        {
            _positionTransform = transform;
            _positionTransformExplicit = transform != null;
        }

        private void ResolvePositionTransform()
        {
            if (_positionTransformExplicit)
                return;

            var sibling = GetComponent<INetworkRigidbodyPositionTransform>();
            _positionTransform = sibling ?? defaultPositionTransform;
        }

        public Vector3 linearVelocity
        {
            get => _rigidbody ? GetLinearVelocity() : Vector3.zero;
            set { if (_rigidbody) SetLinearVelocity(value); }
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
            set { if (_rigidbody) SetAngularVelocity(value); }
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

        public float mass
        {
            get => _rigidbody ? _rigidbody.mass : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.mass = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float drag
        {
            get => _rigidbody ? GetDrag() : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                SetDrag(value);
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float linearDamping
        {
            get => drag;
            set => drag = value;
        }

        public float angularDrag
        {
            get => _rigidbody ? GetAngularDrag() : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                SetAngularDrag(value);
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public float angularDamping
        {
            get => angularDrag;
            set => angularDrag = value;
        }

        public bool useGravity
        {
            get => _rigidbody && _rigidbody.useGravity;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.useGravity = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public bool isKinematic
        {
            get => _rigidbody && _rigidbody.isKinematic;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.isKinematic = value;
                if (IsController(_ownerAuth) && isActiveAndEnabled)
                    SyncSettings(GetCurrentSettings());
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            var appliedForce = new AppliedForce { force = force, mode = mode };

            if (IsController(_ownerAuth))
            {
                ApplyForceToRigidbody(force, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
        }

        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            var appliedForce = new AppliedForce { force = force, position = (CompressedVector3)position, mode = mode };

            if (IsController(_ownerAuth))
            {
                ApplyForceAtPositionToRigidbody(force, position, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            var appliedForce = new AppliedForce { force = torque, mode = mode, isTorque = true };

            if (IsController(_ownerAuth))
            {
                ApplyTorqueToRigidbody(torque, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
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

        /// <summary>
        /// Instantly teleports the rigidbody to a new position and rotation, clearing the
        /// interpolation buffer and syncing to all observers. Use this for respawns, portals,
        /// or any instant repositioning. For regular physics movement, use position/rotation
        /// setters, MovePosition/MoveRotation, or AddForce instead.
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (!_rigidbody)
                return;

            ApplyLocalTeleport(position, rotation);

            if (IsController(_ownerAuth))
            {
                if (isActiveAndEnabled)
                    BroadcastTeleport();
            }
            else if (isActiveAndEnabled)
            {
                WorldToWire(position, out var wirePos, out var wireAbs, out var wireFrame);
                RequestTeleport(wirePos, wireAbs, wireFrame, rotation);
            }
        }

        /// <summary>
        /// Instantly teleports the rigidbody to a new position, clearing the interpolation
        /// buffer and syncing to all observers. Preserves current rotation and velocity.
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            if (!_rigidbody)
                return;
            TeleportTo(position, _rigidbody.rotation);
        }

        /// <summary>
        /// Locally repositions the rigidbody and resets all interpolation/correction state
        /// (target pose, lastSynced mirrors, snapshot buffer) without sending any RPCs.
        /// Use this when the caller is already handling network sync separately, or to fix
        /// up a single peer's view (e.g. a late-joining client snapping to a known pose).
        /// </summary>
        public void TeleportLocal(Vector3 position, Quaternion rotation)
        {
            if (!_rigidbody)
                return;

            ApplyLocalTeleport(position, rotation);
        }

        /// <summary>
        /// Locally repositions the rigidbody and resets interpolation/correction state without
        /// sending any RPCs. Preserves current rotation.
        /// </summary>
        public void TeleportLocal(Vector3 position)
        {
            if (!_rigidbody)
                return;
            TeleportLocal(position, _rigidbody.rotation);
        }

        private void ApplyLocalTeleport(Vector3 position, Quaternion rotation)
        {
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
            SetLinearVelocity(Vector3.zero);
            SetAngularVelocity(Vector3.zero);

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var syncPos = WriteWirePosition(parentTrs, out _, out _, out _);
            var syncRot = ReadRotation(parentTrs);
            var syncLinVel = ReadLinearVelocity(parentTrs);
            var syncAngVel = ReadAngularVelocity(parentTrs);

            _targetPosition = syncPos;
            _targetRotation = syncRot;
            _targetLinearVelocity = syncLinVel;
            _targetAngularVelocity = syncAngVel;
            _targetParent = parentTrs;

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = syncRot;
            _lastSyncedLinearVelocity = syncLinVel;
            _lastSyncedAngularVelocity = syncAngVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(syncLinVel, syncAngVel);

            ClearBuffer();
        }

        /// <summary>
        /// True while a force-sync window is active. The controller uses it to bypass
        /// change-threshold gating on outgoing state; receivers can query it from their
        /// correction code (e.g. to bypass weak-axis logic) for the duration.
        /// </summary>
        public bool isInForceSyncWindow
        {
            get
            {
                if (_forceSyncOneShot)
                    return true;
                return Time.unscaledTimeAsDouble < _forceSyncWindowEndTime;
            }
        }

        /// <summary>Seconds remaining in the active force-sync window, or 0 if inactive / one-shot.</summary>
        public float forceSyncWindowRemaining
        {
            get
            {
                if (_forceSyncOneShot || _forceSyncWindowEndTime <= 0)
                    return 0f;
                return Mathf.Max(0f, (float)(_forceSyncWindowEndTime - Time.unscaledTimeAsDouble));
            }
        }

        /// <summary>Fired when <see cref="isInForceSyncWindow"/> transitions from false to true.</summary>
        public event Action onForceSyncWindowOpened;

        /// <summary>Fired when <see cref="isInForceSyncWindow"/> transitions from true to false.</summary>
        public event Action onForceSyncWindowClosed;

        /// <summary>
        /// Opens a force-sync window. While the window is open, the controller bypasses
        /// the change thresholds and ships state every tick, and observers can query
        /// <see cref="isInForceSyncWindow"/> to adjust local correction behaviour.
        /// Opening the window also sends the current state reliably once.
        /// </summary>
        /// <param name="seconds">Window duration in seconds. Pass -1 (default) for a one-tick window.</param>
        public void ForceSyncFor(float seconds = -1f)
        {
            if (!isSpawned)
                return;

            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogWarning($"ForceSyncFor called on {gameObject.name} from a non-controller. Ignored.", this);
                return;
            }

            OpenForceSyncWindowLocal(seconds);

            if (isActiveAndEnabled)
            {
                SyncForceSyncWindow(seconds);
                SendCurrentState(true, IsSettledForSync());
            }
        }

        private void OpenForceSyncWindowLocal(float seconds)
        {
            bool wasOpen = isInForceSyncWindow;

            if (seconds < 0f)
            {
                _forceSyncOneShot = true;
            }
            else
            {
                double newEnd = Time.unscaledTimeAsDouble + seconds;
                if (newEnd > _forceSyncWindowEndTime)
                    _forceSyncWindowEndTime = newEnd;
            }

            bool isOpen = isInForceSyncWindow;
            if (!wasOpen && isOpen)
                onForceSyncWindowOpened?.Invoke();
            _wasInForceSyncWindow = isOpen;
        }

        private void TickForceSyncWindow()
        {
            bool wasOpen = _wasInForceSyncWindow;

            if (_forceSyncOneShot)
                _forceSyncOneShot = false;

            bool isOpen = isInForceSyncWindow;

            if (wasOpen && !isOpen)
                onForceSyncWindowClosed?.Invoke();

            _wasInForceSyncWindow = isOpen;
        }

        #endregion

        #region RPCs

        [TargetRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendInitialStateToObserver(PlayerID player, RigidbodyStateData data, RigidbodySettingsData settings)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            _rigidbody.mass = settings.mass;
            SetDrag(settings.drag);
            SetAngularDrag(settings.angularDrag);
            _rigidbody.useGravity = settings.useGravity;
            _rigidbody.isKinematic = settings.isKinematic;

            var parentTrs = ResolveParentTransform(data.parent, data.positionFrame);
            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);

            _rigidbody.position = ToWorldPosition(syncPos, parentTrs);
            _rigidbody.rotation = NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs));
            SetAngularVelocity(ToWorldAngularVelocity(data.angularVelocity, parentTrs));

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = data.rotation;
            _lastSyncedLinearVelocity = data.linearVelocity;
            _lastSyncedAngularVelocity = data.angularVelocity;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(data.linearVelocity, data.angularVelocity);

            ClearBuffer();
            PushSnapshot(data);
        }

        [TargetRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendHandoffState(PlayerID player, RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            var parentTrs = ResolveParentTransform(data.parent, data.positionFrame);
            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = data.rotation;
            _lastSyncedLinearVelocity = data.linearVelocity;
            _lastSyncedAngularVelocity = data.angularVelocity;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(data.linearVelocity, data.angularVelocity);

            ClearBuffer();
            PushSnapshot(data);
        }

        [ObserversRpc(channel: Channel.Unreliable, deltaPacked: true, runLocally: true)]
        private void SyncState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);
        }

        [ServerRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SendStateToServer(RigidbodyStateData data)
        {
            SyncState(data);
        }

        [ObserversRpc(channel: Channel.ReliableOrdered, deltaPacked: true, runLocally: true)]
        private void SyncReliableState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);
        }

        [ServerRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendReliableStateToServer(RigidbodyStateData data)
        {
            SyncReliableState(data);
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

        [ObserversRpc(deltaPacked: true, runLocally: true)]
        private void Teleport(RigidbodyTeleportData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            _lastCorrectionReason = "Teleport";
            _hasPendingTeleport = true;

            var parentTrs = ResolveParentTransform(data.parent, data.positionFrame);
            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);

            _rigidbody.position = ToWorldPosition(syncPos, parentTrs);
            _rigidbody.rotation = NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs));
            SetAngularVelocity(ToWorldAngularVelocity(data.angularVelocity, parentTrs));

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;

            ClearBuffer();
            _hasPendingTeleport = false;

            if (_settingsInstance != null)
            {
                Vector3 worldTargetPos = ToWorldPosition(_targetPosition, _targetParent);
                Quaternion worldTargetRot = ToWorldRotation(_targetRotation, _targetParent);
                Vector3 worldTargetLinVel = ToWorldLinearVelocity(_targetLinearVelocity, _targetParent);
                Vector3 worldTargetAngVel = ToWorldAngularVelocity(_targetAngularVelocity, _targetParent);
                var ctx = BuildCorrectionContext(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel, 0f, 0f);
                _settingsInstance.OnReset(in ctx);
            }
        }

        [ServerRpc(deltaPacked: true)]
        private void SyncSettings(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
            SyncSettings_Observer(data);
        }

        [ObserversRpc(bufferLast: true, excludeSender: true)]
        private void SyncSettings_Observer(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
        }

        private void SyncSettings_Internal(RigidbodySettingsData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            _rigidbody.mass = data.mass;
            SetDrag(data.drag);
            SetAngularDrag(data.angularDrag);
            _rigidbody.useGravity = data.useGravity;
            _rigidbody.isKinematic = data.isKinematic;
        }

        [ServerRpc(requireOwnership: false, deltaPacked: true)]
        private void RequestTeleport(CompressedVector3 position, double3 absolutePosition, RigidbodyPositionFrame frame, PackedQuaternion rotation)
        {
            if (_ownerAuth && owner.HasValue)
            {
                // position stays in the peer-agnostic wire frame; the owner converts it.
                ForwardTeleportRequest(owner.Value, position, absolutePosition, frame, rotation);
                return;
            }

            if (!_rigidbody)
                return;

            _rigidbody.position = ToWorldPosition(ExtractSyncPosition(frame, position, absolutePosition), null);
            _rigidbody.rotation = rotation;
            BroadcastTeleport();
        }

        [TargetRpc(deltaPacked: true)]
        private void ForwardTeleportRequest(PlayerID target, CompressedVector3 position, double3 absolutePosition, RigidbodyPositionFrame frame, PackedQuaternion rotation)
        {
            if (!_rigidbody)
                return;

            _rigidbody.position = ToWorldPosition(ExtractSyncPosition(frame, position, absolutePosition), null);
            _rigidbody.rotation = rotation;
            BroadcastTeleport();
        }

        private void BroadcastTeleport()
        {
            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            Teleport(new RigidbodyTeleportData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity
            });
        }

        [ServerRpc(channel: Channel.ReliableOrdered)]
        private void SyncForceSyncWindow(float seconds)
        {
            SyncForceSyncWindow_Internal(seconds);
            SyncForceSyncWindow_Observer(seconds);
        }

        [ObserversRpc(channel: Channel.ReliableOrdered, excludeSender: true)]
        private void SyncForceSyncWindow_Observer(float seconds)
        {
            SyncForceSyncWindow_Internal(seconds);
        }

        private void SyncForceSyncWindow_Internal(float seconds)
        {
            if (IsController(_ownerAuth))
                return;

            OpenForceSyncWindowLocal(seconds);
        }

        #endregion
    }
}

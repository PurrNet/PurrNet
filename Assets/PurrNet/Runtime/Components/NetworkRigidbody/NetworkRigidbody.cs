using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public enum RigidbodyTransformSpace : byte
    {
        Local = 0,
        World = 1
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
        public CompressedVector3 position;
        public PackedQuaternion rotation;
        public HalfVector3 linearVelocity;
        public HalfVector3 angularVelocity;
        public NetworkIdentity parent;
    }

    public struct RigidbodyTeleportData
    {
        public CompressedVector3 position;
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
        public Vector3 position;
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

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Vector3 _targetLinearVelocity;
        private Vector3 _targetAngularVelocity;
        /// <summary>Reference frame for _target* values. Null means world-space.</summary>
        private Transform _targetParent;

        private Vector3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        private Vector3 _lastSyncedLinearVelocity;
        private Vector3 _lastSyncedAngularVelocity;
        private Transform _lastSyncedParent;

        private bool _hasPendingTeleport;
        private bool _isIgnoringParentChanges;

        private double _forceSyncWindowEndTime = double.NegativeInfinity;
        private bool _forceSyncOneShot;
        private bool _wasInForceSyncWindow;

        private string _lastCorrectionReason = "No";
        private Vector3 _latestRawSnapshotPos;
        private Transform _latestRawSnapshotParent;
        private string _bufferSampleMode = "None";
        private double _lastLogTime;
        private float _predictionOffset;

        private void Awake()
        {
            _cachedRigidbody = GetComponent<Rigidbody>();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = ReadPosition(parentTrs);
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

            EnsureSettingsInstance();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if (player == localPlayer)
                return;

            if (_ownerAuth && owner.HasValue && player == owner.Value)
                return;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var stateData = new RigidbodyStateData
            {
                position = ReadPosition(parentTrs),
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
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);
            DisposeSettingsInstance();
            EnsureSettingsInstance();
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

            if (!isInForceSyncWindow && !HasStateChanged(parentTrs) && !ShouldSyncWhenStopped())
                return;

            var pos = ReadPosition(parentTrs);
            var rot = ReadRotation(parentTrs);
            var linVel = ReadLinearVelocity(parentTrs);
            var angVel = ReadAngularVelocity(parentTrs);

            var stateData = new RigidbodyStateData
            {
                position = pos,
                rotation = rot,
                linearVelocity = linVel,
                angularVelocity = angVel,
                parent = parentIdentity
            };

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;

            if (isServer)
                SyncState(stateData);
            else
                SendStateToServer(stateData);

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;
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
                _targetPosition += _targetLinearVelocity * compensation;
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
                    _rigidbody.angularVelocity = worldTargetAngVel;
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
                    _rigidbody.angularVelocity = _resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel;
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

                _rigidbody.AddForce((positionalPull + velocityDamping + dragCompensation) * m);
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

                _rigidbody.AddTorque(torque);
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
            _rigidbody.MovePosition(worldTargetPos);
            _rigidbody.MoveRotation(NormalizeQuaternion(worldTargetRot));
            SetLinearVelocity(_resetLinearVelocityOnSnap ? Vector3.zero : worldTargetLinVel);
            _rigidbody.angularVelocity = _resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel;
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
                angularVelocity = data.angularVelocity,
                parent = data.parent ? data.parent.transform : null
            };

            _bufferHead = (_bufferHead + 1) % BUFFER_SIZE;
            if (_bufferCount < BUFFER_SIZE)
                _bufferCount++;

            _latestRawSnapshotPos = data.position;
            _latestRawSnapshotParent = data.parent ? data.parent.transform : null;
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
                _targetPosition = newest.position + newest.linearVelocity * overshoot;
                _targetRotation = newest.rotation;
                _targetLinearVelocity = newest.linearVelocity;
                _targetAngularVelocity = newest.angularVelocity;
                _targetParent = newest.parent;
                _bufferSampleMode = $"Extrap ({overshoot:F3}s)";
                _predictionOffset = overshoot;
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
                                + a.linearVelocity * (h10 * dt)
                                + h01 * b.position
                                + b.linearVelocity * (h11 * dt);

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

                _targetPosition = h00 * aWorldPos
                                + aWorldLinVel * (h10 * dt)
                                + h01 * bWorldPos
                                + bWorldLinVel * (h11 * dt);

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

        private Vector3 ReadPosition(Transform parent)
        {
            var p = _rigidbody ? _rigidbody.position : transform.position;
            return parent ? parent.InverseTransformPoint(p) : p;
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

        private static Vector3 ToWorldPosition(Vector3 pos, Transform parent)
        {
            return parent ? parent.TransformPoint(pos) : pos;
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

            float positionDelta = Vector3.Distance(ReadPosition(parent), _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(ReadRotation(parent), _lastSyncedRotation);
            float linearVelocityDelta = Vector3.Distance(ReadLinearVelocity(parent), _lastSyncedLinearVelocity);
            float angularVelocityDelta = Vector3.Distance(ReadAngularVelocity(parent), _lastSyncedAngularVelocity);

            return positionDelta > _positionChangeThreshold
                || rotationDelta > _rotationChangeThreshold
                || linearVelocityDelta > _velocityStopThreshold
                || angularVelocityDelta > _velocityStopThreshold;
        }

        private bool ShouldSyncWhenStopped()
        {
            if (!_rigidbody)
                return false;
            return GetLinearVelocity().magnitude < _velocityStopThreshold
                && _rigidbody.angularVelocity.magnitude < _velocityStopThreshold
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
                _rigidbody.AddTorque(force.force, force.mode);
            else if (force.position.HasValue)
                _rigidbody.AddForceAtPosition(force.force, force.position.Value, force.mode);
            else
                _rigidbody.AddForce(force.force, force.mode);
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
            set { if (_rigidbody) _rigidbody.angularVelocity = value; }
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
                _rigidbody.AddForce(force, mode);
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
                _rigidbody.AddForceAtPosition(force, position, mode);
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
                _rigidbody.AddTorque(torque, mode);
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

            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            SetLinearVelocity(Vector3.zero);
            _rigidbody.angularVelocity = Vector3.zero;

            if (IsController(_ownerAuth))
            {
                if (isActiveAndEnabled)
                    BroadcastTeleport();
            }
            else if (isActiveAndEnabled)
            {
                RequestTeleport(position, rotation);
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
                SyncForceSyncWindow(seconds);
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

            var parentTrs = data.parent ? data.parent.transform : null;

            _rigidbody.MovePosition(ToWorldPosition(data.position, parentTrs));
            _rigidbody.MoveRotation(NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs)));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs));
            _rigidbody.angularVelocity = ToWorldAngularVelocity(data.angularVelocity, parentTrs);

            _targetPosition = data.position;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;

            _lastSyncedPosition = data.position;
            _lastSyncedRotation = data.rotation;
            _lastSyncedLinearVelocity = data.linearVelocity;
            _lastSyncedAngularVelocity = data.angularVelocity;
            _lastSyncedParent = parentTrs;

            ClearBuffer();
            PushSnapshot(data);
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

            if (!_rigidbody)
                return;

            _lastCorrectionReason = "Teleport";
            _hasPendingTeleport = true;

            var parentTrs = data.parent ? data.parent.transform : null;

            _rigidbody.MovePosition(ToWorldPosition(data.position, parentTrs));
            _rigidbody.MoveRotation(NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs)));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs));
            _rigidbody.angularVelocity = ToWorldAngularVelocity(data.angularVelocity, parentTrs);

            _targetPosition = data.position;
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
        private void RequestTeleport(CompressedVector3 position, PackedQuaternion rotation)
        {
            if (_ownerAuth && owner.HasValue)
            {
                ForwardTeleportRequest(owner.Value, position, rotation);
                return;
            }

            if (!_rigidbody)
                return;

            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            BroadcastTeleport();
        }

        [TargetRpc(deltaPacked: true)]
        private void ForwardTeleportRequest(PlayerID target, CompressedVector3 position, PackedQuaternion rotation)
        {
            if (!_rigidbody)
                return;

            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
            BroadcastTeleport();
        }

        private void BroadcastTeleport()
        {
            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            Teleport(new RigidbodyTeleportData
            {
                position = ReadPosition(parentTrs),
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

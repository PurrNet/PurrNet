using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet
{
    public sealed class NetworkTransform : NetworkIdentity, INetworkTransform
    {
        [Header("What to Sync")]
        [Tooltip("Whether to sync the position of the transform. And if so, in what space.")]
        [SerializeField, PurrLock]
        private SyncMode _syncPosition = SyncMode.World;

        [Tooltip("Whether to sync the rotation of the transform. And if so, in what space.")] [SerializeField, PurrLock]
        private SyncMode _syncRotation = SyncMode.World;

        [Tooltip("Whether to sync the scale of the transform.")] [SerializeField, PurrLock]
        private bool _syncScale = true;

        [Tooltip("Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentiy.")]
        [SerializeField, PurrLock]
        private bool _syncParent = true;

        [Header("How to Sync")] [Tooltip("What to interpolate when syncing the transform.")] [SerializeField, PurrLock]
        private TransformSyncMode _interpolateSettings =
            TransformSyncMode.Position | TransformSyncMode.Rotation | TransformSyncMode.Scale;
        [Tooltip("The minimum amount of buffered ticks to store.\nThis is used for interpolation.")]
        [SerializeField, PurrLock, Min(1)] private int _minBufferSize = 1;
        [Tooltip("The maximum amount of buffered ticks to store.\nThis is used for interpolation.")]
        [SerializeField, PurrLock, Min(1)] private int _maxBufferSize = 2;
#if UNITY_PHYSICS_3D
        [Tooltip("Will enforce the character controller getting enabled and disabled when attempting to sync the transform - CAUTION - Physics events can/will be called multiple times")]
        [SerializeField]
        private bool _characterControllerPatch;
#endif
        [Header("When to Sync")]
        [FormerlySerializedAs("_clientAuth")]
        [Tooltip(
            "If true, the client can send transform data to the server. If false, the client can't send transform data to the server.")]
        [SerializeField, PurrLock]
        private bool _ownerAuth = true;

        [SerializeField]
        private InterpolationTiming _interpolationTiming = InterpolationTiming.Update;

        /// <summary>
        /// Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentiy.
        /// </summary>
        public bool syncParent => _syncParent;

        public int ticksBehind
        {
            get
            {
                if (syncPosition)
                    return _position.bufferSize;
                if (syncRotation)
                    return _rotation.bufferSize;
                if (syncScale)
                    return _scale.bufferSize;
                return 0;
            }
        }

        /// <summary>
        /// Whether to sync the position of the transform.
        /// </summary>
        public bool syncPosition => _syncPosition != SyncMode.No;

        /// <summary>
        /// Whether to sync the rotation of the transform.
        /// </summary>
        public bool syncRotation => _syncRotation != SyncMode.No;

        /// <summary>
        /// Whether to sync the scale of the transform.
        /// </summary>
        public bool syncScale => _syncScale;

        /// <summary>
        /// Whether to interpolate the position of the transform.
        /// </summary>
        public bool interpolatePosition => _interpolateSettings.HasFlag(TransformSyncMode.Position);

        /// <summary>
        /// Whether to interpolate the rotation of the transform.
        /// </summary>
        public bool interpolateRotation => _interpolateSettings.HasFlag(TransformSyncMode.Rotation);

        /// <summary>
        /// Whether to interpolate the scale of the transform.
        /// </summary>
        public bool interpolateScale => _interpolateSettings.HasFlag(TransformSyncMode.Scale);

        /// <summary>
        /// Whether the client controls the transform if they are the owner.
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        Interpolated<Vector3WithParent> _position;
        Interpolated<QuaternionWithParent> _rotation;
        Interpolated<ScaleWithParent> _scale;

        private Transform _trs;
#if UNITY_PHYSICS_3D
        private Rigidbody _rb;
#endif
#if UNITY_PHYSICS_2D
        private Rigidbody2D _rb2d;
#endif
#if UNITY_PHYSICS_3D
        private CharacterController _controller;
#endif

        private bool _prevWasController;

        public Vector3 position { get; private set; }
        public Quaternion rotation { get; private set; }
        public Vector3 localScale { get; private set; }

        private void Awake()
        {
            _trs = transform;
#if UNITY_PHYSICS_3D
            _rb = GetComponent<Rigidbody>();
            _controller = GetComponent<CharacterController>();
#endif
#if UNITY_PHYSICS_2D
            _rb2d = GetComponent<Rigidbody2D>();
#endif
        }

        private void OnEnable()
        {
            UnityLatestUpdate.onLatestUpdate += LateLateUpdate;

            if (_trs == null)
                return;

            // Reset delta compression state to prevent stale reference points
            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            _lastReadData = _currentData;
            _lastSentDelta = _currentData;

            // Force sync if we're the controller and spawned
            if (_wasOnSpawnedCalled && isController) {
                ForceSync();
            }
        }

        private void OnDisable()
        {
            UnityLatestUpdate.onLatestUpdate -= LateLateUpdate;
        }

        protected override void OnEarlySpawn()
        {
            _trs = transform;

            float sendDelta = networkManager.tickModule.tickDelta;
            var p = _trs.parent;

            if (syncPosition)
            {
                var currentPos = _syncPosition == SyncMode.World ?
                    new Vector3WithParent(p, false, _trs.position) :
                    new Vector3WithParent(p, true, _trs.localPosition);
                _position = new Interpolated<Vector3WithParent>(interpolatePosition ? Vector3WithParent.Lerp : Vector3WithParent.NoLerp,
                    sendDelta, currentPos, _maxBufferSize, _minBufferSize);
            }

            if (syncRotation)
            {
                var currentRot = _syncRotation == SyncMode.World ?
                    new QuaternionWithParent(p, false, _trs.rotation) :
                    new QuaternionWithParent(p, true, _trs.localRotation);
                _rotation = new Interpolated<QuaternionWithParent>(
                    interpolateRotation ? QuaternionWithParent.Lerp : QuaternionWithParent.NoLerp,
                    sendDelta, currentRot, _maxBufferSize, _minBufferSize);
            }

            if (syncScale)
            {
                var currentScale = new ScaleWithParent(p, _trs.localScale);
                _scale = new Interpolated<ScaleWithParent>(interpolateScale ? ScaleWithParent.Lerp : ScaleWithParent.NoLerp,
                    sendDelta, currentScale, _maxBufferSize, _minBufferSize);
            }

            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            _lastReadData = _currentData;
            _lastSentDelta = _currentData;
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            OnOwnerChanged(ownerId, ownerId, isServer);
        }

        private bool _cachedIsController;

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            _cachedIsController = IsController(_ownerAuth);

            if (!enabled) {
                return;
            }

            if (!_wasOnSpawnedCalled)
                return;

            if (!_ownerAuth)
                return;

            if (asServer)
            {
                if (newOwner.HasValue && newOwner != localPlayer)
                    SendLatestState(newOwner.Value, _currentData, false);

                if (oldOwner.HasValue && newOwner != oldOwner && oldOwner != localPlayer)
                    SendLatestState(oldOwner.Value, _currentData, false);
            }
            else if (newOwner == localPlayer && !isServer)
            {
                _currentData = GetCurrentTransformData();
                _latestData = _currentData;
                SendLatestStateToServer(_currentData);
                _lastSentDelta = _currentData;
            }
        }

        private bool _wasOnSpawnedCalled;

        protected override void OnSpawned(bool asServer)
        {
            _cachedIsController = IsController(_ownerAuth);
            _wasOnSpawnedCalled = true;

            if (!networkManager.TryGetModule<NetworkTransformFactory>(asServer, out var factory))
                return;

            if (!factory.TryGetModule(sceneId, out var ntModule))
                return;

            if (!asServer && !isServer && IsController(localPlayerForced, _ownerAuth, false))
            {
                _currentData = GetCurrentTransformData();
                _latestData = _currentData;
                SendLatestStateToServer(_currentData);
                _lastSentDelta = _currentData;
            }

            ntModule.Register(this);
        }

        protected override void OnDespawned(bool asServer)
        {
            _wasOnSpawnedCalled = false;

            if (!networkManager.TryGetModule<NetworkTransformFactory>(asServer, out var factory))
            {
                if (!networkManager.TryGetModule<NetworkTransformFactory>(true, out factory))
                    return;
            }

            if (!factory.TryGetModule(sceneId, out var ntModule))
                return;

            ntModule.Unregister(this);
        }

        protected override void OnSpawned()
        {
            int ticksPerSec = networkManager.tickModule.tickRate;
            int ticksPerBuffer = Mathf.CeilToInt(ticksPerSec * 0.15f) * 2;

            if (syncPosition) _position.maxBufferSize = ticksPerBuffer;
            if (syncRotation) _rotation.maxBufferSize = ticksPerBuffer;
            if (syncScale) _scale.maxBufferSize = ticksPerBuffer;
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if (!enabled) {
                return;
            }

            if (player == localPlayer)
                return;

            if (!_ownerAuth || player != owner)
                SendLatestState(player, _currentData, true);
        }

        /// <summary>
        /// Forces the latest NT state to target player, voiding compression and other optimizations
        /// </summary>
        public void ForceSync(PlayerID target)
        {
            if (target == localPlayer)
                return;

            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            SendLatestState(target, _currentData, true);
        }

        /// <summary>
        /// Forces the latest NT state to everyone, voiding compression and other optimizations
        /// </summary>
        public void ForceSync()
        {
            if (!_cachedIsController)
                return;

            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            _lastSentDelta = _currentData;

            if (isServer)
            {
                int obCount = observers.Count;
                for (var i = 0; i < obCount; i++)
                {
                    var observer = observers[i];

                    if (owner == observer)
                        continue;

                    SendLatestState(observer, _currentData, true);
                }
            }
            else
            {
                ForceSyncServer(_currentData);
            }
        }

        [ServerRpc]
        private void ForceSyncServer(NetworkTransformData data)
        {
            if (!_ownerAuth)
                return;

            _lastReadData = data;
            _currentData = data;

            TeleportToData(data);
            ApplyLerpedPosition();

            int obCount = observers.Count;
            for (var i = 0; i < obCount; i++)
            {
                var observer = observers[i];

                if (owner == observer)
                    continue;

                SendLatestState(observer, data, true);
            }
        }

        /// <summary>
        /// Clears interpolation and teleports the transform to the target position, rotation and scale.
        /// Works on both owner and non-owner clients.
        /// </summary>
        public void ClearInterpolation(Vector3? targetPos, Quaternion? targetRot, Vector3? targetScale)
        {
            var p = _trs.parent;
            if (syncPosition && targetPos.HasValue)
                _position.Teleport(new Vector3WithParent(p, _syncPosition == SyncMode.Local, targetPos.Value));
            if (syncRotation && targetRot.HasValue)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, targetRot.Value));
            if (syncScale && targetScale.HasValue)
                _scale.Teleport(new ScaleWithParent(p, targetScale.Value));
        }

        [ServerRpc]
        private void SendLatestStateToServer(NetworkTransformData data, RPCInfo info = default)
        {
            _lastReadData = data;
            _currentData = data;
            TeleportToData(data);
            ApplyLerpedPosition();
        }

        [TargetRpc]
        private void SendLatestState(PlayerID player, NetworkTransformData data, bool applyPosition)
        {
            _lastReadData = data;
            _currentData = data;

            if (applyPosition)
            {
                TeleportToData(data);
                ApplyLerpedPosition();
            }
        }

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private void FixedUpdate()
        {
            if (!isSpawned)
                return;

            bool isNotController = !_cachedIsController;

            if (isNotController)
            {
#if UNITY_PHYSICS_3D
                if (_rb) _rb.Sleep();
#endif
#if UNITY_PHYSICS_2D
                if (_rb2d) _rb2d.Sleep();
#endif
            }
        }
#endif

        private void Update()
        {
            if (_interpolationTiming == InterpolationTiming.Update)
                UpdateNT();
        }

        private void LateUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.LateUpdate)
                UpdateNT();
        }

        private void LateLateUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.LateLateUpdate)
                UpdateNT();
        }

        private void UpdateNT()
        {
            if (!isSpawned)
                return;

            bool isLocalController = _cachedIsController;

            if (!isLocalController)
                ApplyLerpedPosition();

            _latestData = GetCurrentTransformData();
            if (isLocalController)
                TeleportToData(_latestData);

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
            if (_prevWasController != isLocalController)
            {
#if UNITY_PHYSICS_3D
                if (isLocalController && _rb)
                    _rb.WakeUp();
#endif

#if UNITY_PHYSICS_2D
                if (isLocalController && _rb2d)
                    _rb2d.WakeUp();
#endif

                _prevWasController = isLocalController;
            }
#endif
        }

        private void ApplyLerpedPosition()
        {
#if UNITY_PHYSICS_3D
            bool disableController = _controller && _controller.enabled;

            if (disableController && _characterControllerPatch)
                _controller.enabled = false;
#endif

            if (syncPosition)
            {
                var worldPos = _position.Advance(Time.unscaledDeltaTime).position;
                _trs.position = worldPos;
                position = worldPos;
            }

            if (syncRotation)
            {
                var worldRot = _rotation.Advance(Time.unscaledDeltaTime).rotation;
                _trs.rotation = worldRot;
                rotation = worldRot;
            }

            if (syncScale)
            {
                var worldScale = _scale.Advance(Time.unscaledDeltaTime).scale;
                var parentTrs = _trs.parent;
                var ls = parentTrs ? parentTrs.GetLocalScale(worldScale) : worldScale;
                _trs.localScale = ls;
                this.localScale = ls;
            }

#if UNITY_PHYSICS_3D
            if (disableController && _characterControllerPatch)
                _controller.enabled = true;
#endif
        }

        private NetworkTransformData GetCurrentTransformData()
        {
            var pos = _syncPosition switch
            {
                SyncMode.World => _trs.position,
                SyncMode.Local => _trs.localPosition,
                _ => Vector3.zero
            };

            var rot = _syncRotation switch
            {
                SyncMode.World => _trs.rotation,
                SyncMode.Local => _trs.localRotation,
                _ => Quaternion.identity
            };


            var ntScale = _syncScale ? _trs.localScale : default;
            return new NetworkTransformData(pos, rot, ntScale);
        }

        void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (!_syncParent)
                return;

            HandleParentChanged(_trs.parent);
        }

        private void HandleParentChanged(Transform parent)
        {
            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                hierarchy.OnParentChanged(this, parent);
            }
        }

        private bool _isIgnoringParentChanges;

        public void StartIgnoringParentChanges()
        {
            _isIgnoringParentChanges = true;
        }

        public void StopIgnoringParentChanges()
        {
            _isIgnoringParentChanges = false;
        }

        private void TeleportToData(NetworkTransformData data)
        {
            var p = _trs.parent;

            if (syncPosition)
                _position.Teleport(new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, data.scale));
        }

        private void ApplyData(NetworkTransformData data)
        {
            var p = _trs.parent;
            if (syncPosition)
                _position.Add(new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, data.scale));
        }

        private NetworkTransformData _latestData;

        private NetworkTransformData _currentData;
        private NetworkTransformData _lastReadData;
        private NetworkTransformData _lastSentDelta;

        public bool HasChanges()
        {
            return !_currentData.Equals(_lastSentDelta);
        }

        public bool DeltaWrite(BitPacker packer)
        {
            bool hasChanged = false;

            if (syncPosition)
                hasChanged = DeltaPacker<CompressedVector3>.Write(packer, _lastSentDelta.position, _currentData.position);

            if (syncRotation)
            {
                hasChanged = DeltaPacker<PackedQuaternion>.Write(packer, _lastSentDelta.rotation, _currentData.rotation) ||
                          hasChanged;
            }

            if (syncScale)
            {
                hasChanged = DeltaPacker<CompressedVector3>.Write(packer, _lastSentDelta.scale, _currentData.scale) ||
                          hasChanged;
            }

            return hasChanged;
        }

        public void DeltaRead(BitPacker packet)
        {
            _lastReadData = DeltaRead(packet, _lastReadData);
            ApplyData(_lastReadData);
        }

        NetworkTransformData DeltaRead(BitPacker packet, NetworkTransformData oldValue)
        {
            var pos = oldValue.position;
            var rot = oldValue.rotation;
            var ntScale = oldValue.scale;

            if (syncPosition)
                DeltaPacker<CompressedVector3>.Read(packet, pos, ref oldValue.position);

            if (syncRotation)
                DeltaPacker<PackedQuaternion>.Read(packet, rot, ref oldValue.rotation);

            if (syncScale)
                DeltaPacker<CompressedVector3>.Read(packet, ntScale, ref oldValue.scale);

            return oldValue;
        }

        public void GatherState()
        {
            _currentData = _latestData;
            if (_cachedIsController)
                TeleportToData(_currentData);
        }

        public void DeltaSave()
        {
            _lastSentDelta = _currentData;
        }

        private bool _cachedConnectedOwner;

        protected override void OnOwnerDisconnected(PlayerID ownerId)
        {
            _cachedConnectedOwner = false;
        }

        public bool IsControlling(PlayerID player, bool asServer)
        {
            if (!_ownerAuth || !_cachedConnectedOwner)
                return asServer;

            if (player == owner)
                return true;

            return asServer;
        }
    }
}

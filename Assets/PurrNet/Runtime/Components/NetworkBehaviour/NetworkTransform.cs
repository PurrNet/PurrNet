using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Transform")]
    public sealed class NetworkTransform : NetworkIdentity, INetworkTransform
    {
        public static INetworkTransformPositionTransform defaultPositionTransform { get; set; }

        [Header("What to Sync")]
        [Tooltip("Whether to sync the position of the transform. And if so, in what space.")]
        [SerializeField, PurrLock]
        private SyncMode _syncPosition = SyncMode.World;

        [Tooltip("Whether to sync the rotation of the transform. And if so, in what space.")]
        [SerializeField, PurrLock]
        private SyncMode _syncRotation = SyncMode.World;

        [Tooltip("Whether to sync the scale of the transform.")]
        [SerializeField, PurrLock]
        private bool _syncScale = true;

        [Tooltip("Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentity.")]
        [SerializeField, PurrLock]
        private bool _syncParent = true;

        [Tooltip("Forces any attached Rigidbody to sleep if not the controller, to ensure better syncing when RB is present.")]
        [SerializeField, PurrLock]
        private bool _forceSleepRb = true;

        [Header("How to Sync")]
        [Tooltip("What to interpolate when syncing the transform.")]
        [SerializeField, PurrLock]
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

        [Tooltip("Skips sends while motion stays reconstructible by receivers, greatly reducing " +
                 "bandwidth for steady motion (linear or curved) without adding render delay. " +
                 "Erratic motion falls back to normal per-tick syncing automatically.")]
        [FormerlySerializedAs("_predictiveSync")]
        [SerializeField, PurrLock]
        private bool _adaptiveSync = true;

        private NetworkTransformSyncStrategy _customStrategy;
        private NetworkTransformSyncStrategy _activeStrategy;
        private bool _hasStrategy;

        private static readonly NetworkTransformDefaultStrategy _defaultStrategy = new NetworkTransformDefaultStrategy();

        /// <summary>
        /// Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentity.
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

        /// <summary>
        /// Whether adaptive reduced-rate syncing is active.
        /// </summary>
        public bool hasSyncStrategy => _hasStrategy;

        /// <summary>
        /// Whether adaptive reduced-rate syncing is enabled. Uses the built-in default strategy
        /// unless a custom strategy is injected via <see cref="SetSyncStrategy"/>.
        /// </summary>
        public bool adaptiveSync
        {
            get => _adaptiveSync;
            set
            {
                _adaptiveSync = value;
                ApplyStrategySettings();
            }
        }

        private ushort _skipCacheFrom;
        private ushort _skipCacheCurrent;
        private ushort _skipCachePrev;
        private bool _skipCacheHasPrev;
        private bool _skipCacheResult;
        private bool _hasSkipCache;

        internal bool CanSkipCached(in NTLastAdaptiveWrite lastWrite, ushort currentTick,
            in NetworkTransformState current)
        {
            if (_hasSkipCache && _skipCacheFrom == lastWrite.tick && _skipCacheCurrent == currentTick &&
                _skipCacheHasPrev == lastWrite.hasPrev && _skipCachePrev == lastWrite.prevTick)
                return _skipCacheResult;

            _skipCacheResult = _activeStrategy.CanSkip(this, lastWrite, currentTick, current);
            _skipCacheFrom = lastWrite.tick;
            _skipCacheCurrent = currentTick;
            _skipCachePrev = lastWrite.prevTick;
            _skipCacheHasPrev = lastWrite.hasPrev;
            _hasSkipCache = true;
            return _skipCacheResult;
        }

        /// <summary>
        /// Injects a custom sync strategy. Pass null to revert to the built-in default strategy.
        /// Call before spawning for full effect; when called on a spawned transform the new
        /// strategy applies immediately. Sharing one instance across transforms is safe as
        /// long as any strategy state is input-keyed memoization, as in the built-in strategies.
        /// </summary>
        public void SetSyncStrategy(NetworkTransformSyncStrategy strategy)
        {
            _customStrategy = strategy;
            ApplyStrategySettings();
        }

        private void ApplyStrategySettings()
        {
            _activeStrategy = !_adaptiveSync ? null : _customStrategy ?? _defaultStrategy;
            _hasStrategy = _activeStrategy != null;

            if (!_hasStrategy)
                return;

            var nm = networkManager;
            if (nm && nm.tickModule != null)
                _adaptiveSpacing = Mathf.Clamp(
                    Mathf.RoundToInt(nm.tickModule.tickRate * _activeStrategy.maxSendInterval), 2,
                    CAPTURE_HISTORY_SIZE - 2);
        }

        Interpolated<Vector3WithParent> _position;
        Interpolated<QuaternionWithParent> _rotation;
        Interpolated<ScaleWithParent> _scale;

        public Vector3 latestReadPosition
        {
            get
            {
                if (_lastReadData.absolutePosition.HasValue &&
                    TryResolvePositionTransform(out var trs))
                    return trs.ToLocal(this, _lastReadData.absolutePosition.Value);
                return _lastReadData.position.GetValueOrDefault();
            }
        }

        public Quaternion latestReadRotation => _lastReadData.rotation;

        public Vector3 latestReadScale => _lastReadData.scale;

        private Transform _trs;
#if UNITY_PHYSICS_3D
        private Rigidbody _rb;
        private bool _hasRigidbody;
#endif
#if UNITY_PHYSICS_2D
        private Rigidbody2D _rb2d;
        private bool _hasRigidbody2D;
#endif
#if UNITY_PHYSICS_3D
        private CharacterController _controller;
#endif

        public Vector3 position { get; private set; }
        public Quaternion rotation { get; private set; }
        public Vector3 localScale { get; private set; }

        private Action _onLateLateUpdate;

        private bool _positionTransformExplicit;
        private bool _useAbsoluteFrame;

#if UNITY_PHYSICS_3D
        private Vector3 _pendingRbPosition;
        private Quaternion _pendingRbRotation;
        private bool _pendingRbHasPosition;
        private bool _pendingRbHasRotation;
#endif

        public INetworkTransformPositionTransform positionTransform { get; private set; }

        public void SetPositionTransform(INetworkTransformPositionTransform transform)
        {
            positionTransform = transform;
            _positionTransformExplicit = transform != null;
        }

        private void ResolvePositionTransform()
        {
            if (!_positionTransformExplicit)
                positionTransform = defaultPositionTransform;

            _useAbsoluteFrame = positionTransform != null &&
                                (_syncPosition == SyncMode.World ||
                                 (_syncPosition == SyncMode.Local && _trs && !_trs.parent));
        }

        private void Awake()
        {
            _onLateLateUpdate = LateLateUpdate;
            _trs = transform;
#if UNITY_PHYSICS_3D
            _rb = GetComponent<Rigidbody>();
            _hasRigidbody = _rb;
            _controller = GetComponent<CharacterController>();
#endif
#if UNITY_PHYSICS_2D
            _rb2d = GetComponent<Rigidbody2D>();
            _hasRigidbody2D = _rb2d;
#endif
        }

        private void OnEnable()
        {
            UnityLatestUpdate.onLatestUpdate += _onLateLateUpdate;

            if (!_trs)
                return;

            if (_wasOnSpawnedCalled)
            {
                if (_cachedIsController)
                {
                    ForceSync();
                }
                else
                {
                    RefreshCurrentState();
                    TeleportToData(_currentData);
                }
            }
        }

        private void OnDisable()
        {
            UnityLatestUpdate.onLatestUpdate -= _onLateLateUpdate;
        }

        protected override void OnEarlySpawn()
        {
            _trs = transform;
            ReCacheIsController();
            ResolvePositionTransform();

            float sendDelta = networkManager.tickModule.tickDelta;
            var p = _trs.parent;

            var data = GetCurrentTransformData();

            if (syncPosition)
            {
                var currentPos = MakePositionSample(p, data);
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

            _currentData = data;
            _latestData = data;
            _lastReadData = data;
            _lastSentDelta = data;

            ResetUnreliableRecvState();
            BumpSendGen();
#if UNITY_PHYSICS_3D
            _pendingRbHasPosition = false;
            _pendingRbHasRotation = false;
#endif
            RefreshLatestFrame();
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
            CaptureUnreliableState();
        }

        private Vector3WithParent MakePositionSample(Transform p, NetworkTransformData data)
        {
            if (data.absolutePosition.HasValue)
            {
                if (TryResolvePositionTransform(out var trs))
                    return new Vector3WithParent(this, trs, data.absolutePosition.Value);

                PurrLogger.LogError(
                    $"'{name}' received an absolute-frame position but has no {nameof(INetworkTransformPositionTransform)} " +
                    $"to decode it. Assign one via {nameof(SetPositionTransform)} or {nameof(defaultPositionTransform)}.", this);
            }
            if (!data.position.HasValue)
            {
                PurrLogger.LogError(
                    $"'{name}' received a {nameof(NetworkTransformData)} with no position in either frame. " +
                    $"Holding the current transform instead of snapping to the parent origin.", this);
                bool local = _syncPosition == SyncMode.Local;
                return new Vector3WithParent(p, local, local ? _trs.localPosition : _trs.position);
            }

            return new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position.Value);
        }

        private bool TryResolvePositionTransform(out INetworkTransformPositionTransform transform)
        {
            transform = positionTransform ?? defaultPositionTransform;
            return transform != null;
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            OnOwnerChanged(ownerId, ownerId, isServer);
        }

        private bool _cachedIsController;

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            ReCacheIsController();
            BumpSendGen();

            if (isServer)
                ResetUnreliableRecvState();

            if (!enabled)
            {
                return;
            }

            if (!_wasOnSpawnedCalled)
                return;

            if (!_ownerAuth)
                return;

            if (asServer)
            {
                var state = currentState;

                if (newOwner.HasValue && newOwner != localPlayer)
                    SendLatestState(newOwner.Value, state, false, _sendGen);

                if (oldOwner.HasValue && newOwner != oldOwner && oldOwner != localPlayer)
                    SendLatestState(oldOwner.Value, state, false, _sendGen);
            }
            else if (newOwner == localPlayer && !isServer)
            {
                RefreshCurrentState();
                SendLatestStateToServer(currentState, _sendGen);
            }
        }

        private void ReCacheIsController()
        {
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
        }

        private bool _wasOnSpawnedCalled;

        protected override void OnSpawned(bool asServer)
        {
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
            _wasOnSpawnedCalled = true;

            if (!networkManager.TryGetModule<NetworkTransformFactory>(asServer, out var factory))
                return;

            if (!factory.TryGetModule(sceneId, out var ntModule))
                return;

            if (!asServer && !isServer && IsController(localPlayerForced, _ownerAuth, false))
            {
                RefreshCurrentState();
                SendLatestStateToServer(currentState, _sendGen);
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

        private int _adaptiveSpacing = 2;

        internal int adaptiveSendSpacing => _adaptiveSpacing;

        protected override void OnSpawned()
        {
            int ticksPerSec = networkManager.tickModule.tickRate;
            int ticksPerBuffer = Mathf.CeilToInt(ticksPerSec * 0.15f) * 2;

            if (syncPosition) _position.maxBufferSize = ticksPerBuffer;
            if (syncRotation) _rotation.maxBufferSize = ticksPerBuffer;
            if (syncScale) _scale.maxBufferSize = ticksPerBuffer;

            ApplyStrategySettings();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            InvalidateObserverBaseline(player, true);

            if (!enabled)
            {
                return;
            }

            if (player == localPlayer)
                return;

            if (!_ownerAuth || player != owner)
                SendLatestState(player, currentState, true, _sendGen);
        }

        protected override void OnObserverRemoved(PlayerID player)
        {
            InvalidateObserverBaseline(player, false);
        }

        private void InvalidateObserverBaseline(PlayerID player, bool enqueue)
        {
            if (!networkManager || !id.HasValue)
                return;

            if (networkManager.TryGetModule<NetworkTransformFactory>(isServer, out var factory) &&
                factory.TryGetModule(sceneId, out var ntModule))
                ntModule.InvalidateSendBaseline(player, id.Value, enqueue);
        }

        /// <summary>
        /// Forces the latest NT state to target player, voiding compression and other optimizations
        /// </summary>
        public void ForceSync(PlayerID target)
        {
            if (target == localPlayer)
                return;

            BumpSendGen(target);
            RefreshCurrentState();
            SendLatestState(target, currentState, true, _sendGen);
        }

        /// <summary>
        /// Forces the latest NT state to everyone, voiding compression and other optimizations
        /// </summary>
        public void ForceSync()
        {
            if (!_cachedIsController)
                return;

            BumpSendGen();
            RefreshCurrentState();
            _lastSentDelta = _currentData;
            var state = currentState;

            if (isServer)
            {
                int obCount = observers.Count;
                var localP = localPlayer;

                for (var i = 0; i < obCount; i++)
                {
                    var observer = observers[i];

                    if ((_ownerAuth && owner == observer) || observer == localP)
                        continue;

                    SendLatestState(observer, state, true, _sendGen);
                }
            }
            else
            {
                ForceSyncServer(state, _sendGen);
            }
        }

        [ServerRpc]
        private void ForceSyncServer(NetworkTransformState state, byte gen, RPCInfo info = default)
        {
            if (!_ownerAuth || !IsControlling(info.sender, false))
                return;

            if (!ForceAdoptRecvGen(gen))
                return;

            BumpSendGen();
            AdoptState(state);
            _lastSentDelta = state.data;
            TeleportToState(state);
            ApplyLerpedPosition();

            int obCount = observers.Count;
            var localP = localPlayer;

            for (var i = 0; i < obCount; i++)
            {
                var observer = observers[i];

                if (owner == observer || observer == localP)
                    continue;

                SendLatestState(observer, state, true, _sendGen);
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
            {
                if (_useAbsoluteFrame)
                    _position.Teleport(new Vector3WithParent(this, positionTransform,
                        positionTransform.ToAbsolute(this, targetPos.Value)));
                else
                    _position.Teleport(new Vector3WithParent(p, _syncPosition == SyncMode.Local, targetPos.Value));
            }
            if (syncRotation && targetRot.HasValue)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, targetRot.Value));
            if (syncScale && targetScale.HasValue)
                _scale.Teleport(new ScaleWithParent(p, targetScale.Value));
        }

        [ServerRpc]
        private void SendLatestStateToServer(NetworkTransformState state, byte gen, RPCInfo info = default)
        {
            if (!_ownerAuth || !IsControlling(info.sender, false))
                return;

            if (!ForceAdoptRecvGen(gen))
                return;

            BumpSendGen();
            AdoptState(state);
            _lastSentDelta = state.data;
            TeleportToState(state);
            ApplyLerpedPosition();
        }

        [TargetRpc]
        private void SendLatestState(PlayerID player, NetworkTransformState state, bool applyPosition, byte gen)
        {
            TryApplyTargetedState(state, applyPosition, gen);
        }

        internal bool TryApplyTargetedState(in NetworkTransformState state, bool applyPosition, byte gen)
        {
            if (!ForceAdoptRecvGen(gen))
                return false;

            AdoptState(state);

            if (applyPosition)
            {
                TeleportToState(state);
                ApplyLerpedPosition();
            }

            return true;
        }

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private void FixedUpdate()
        {
            if (!isSpawned)
                return;

            if (_cachedIsController)
                return;

#if UNITY_PHYSICS_3D
            ApplyPendingRigidbodyPose();
#endif

            if (_forceSleepRb)
            {
#if UNITY_PHYSICS_3D
                if (_hasRigidbody && _rb) _rb.Sleep();
#endif
#if UNITY_PHYSICS_2D
                if (_hasRigidbody2D && _rb2d) _rb2d.Sleep();
#endif
            }
        }
#endif

#if UNITY_PHYSICS_3D
        private void ApplyPendingRigidbodyPose()
        {
            if (!_hasRigidbody || !_rb)
                return;

            if (_pendingRbHasPosition)
            {
                _pendingRbHasPosition = false;
                if ((_rb.position - _pendingRbPosition).sqrMagnitude > 0.00000001f)
                {
                    if (_forceSleepRb)
                        _rb.position = _pendingRbPosition;
                    else
                        _rb.MovePosition(_pendingRbPosition);
                }
            }

            if (_pendingRbHasRotation)
            {
                _pendingRbHasRotation = false;
                if (Quaternion.Angle(_rb.rotation, _pendingRbRotation) > 0.005f)
                {
                    if (_forceSleepRb)
                        _rb.rotation = _pendingRbRotation;
                    else
                        _rb.MoveRotation(_pendingRbRotation);
                }
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

        private void OnIsControlledChanged(bool isController)
        {
            if (!isController)
            {
                _latestData = GetCurrentTransformData();
                RefreshLatestFrame();
                TeleportToData(_latestData);
            }
            else
            {
#if UNITY_PHYSICS_3D
                if (_rb) _rb.WakeUp();
#endif
#if UNITY_PHYSICS_2D
                if (_rb2d) _rb2d.WakeUp();
#endif
            }
        }

        private void UpdateNT()
        {
            if (!isSpawned)
                return;

            bool isLocalController = _cachedIsController;

            if (!isLocalController)
                ApplyLerpedPosition();
            _latestData = GetCurrentTransformData();
            RefreshLatestFrame();
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
#if UNITY_PHYSICS_3D
                if (_hasRigidbody && _rb)
                {
                    _pendingRbPosition = worldPos;
                    _pendingRbHasPosition = true;
                }
                else
#endif
                {
                    _trs.position = worldPos;
                }
                position = worldPos;
            }

            if (syncRotation)
            {
                var worldRot = _rotation.Advance(Time.unscaledDeltaTime).rotation;
#if UNITY_PHYSICS_3D
                if (_hasRigidbody && _rb)
                {
                    _pendingRbRotation = worldRot;
                    _pendingRbHasRotation = true;
                }
                else
#endif
                {
                    _trs.rotation = worldRot;
                }
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
            Vector3 pos;
            Quaternion rot;

            if (_syncPosition == _syncRotation)
            {
                switch (_syncPosition)
                {
                    case SyncMode.World:
                        _trs.GetPositionAndRotation(out pos, out rot);
                        break;
                    case SyncMode.Local:
                        _trs.GetLocalPositionAndRotation(out pos, out rot);
                        break;
                    case SyncMode.No:
                    default:
                        pos = Vector3.zero;
                        rot = Quaternion.identity;
                        break;
                }
            }
            else
            {
                pos = _syncPosition switch
                {
                    SyncMode.World => _trs.position,
                    SyncMode.Local => _trs.localPosition,
                    _ => Vector3.zero
                };

                rot = _syncRotation switch
                {
                    SyncMode.World => _trs.rotation,
                    SyncMode.Local => _trs.localRotation,
                    _ => Quaternion.identity
                };
            }

            var ntScale = _syncScale ? _trs.localScale : default;

            if (_useAbsoluteFrame)
                return new NetworkTransformData(null, positionTransform.ToAbsolute(this, pos), rot, ntScale);

            return new NetworkTransformData((CompressedVector3)pos, null, rot, ntScale);
        }

        void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (_cachedIsController)
            {
                _latestData = GetCurrentTransformData();
                RefreshLatestFrame();
            }

            if (_syncPosition == SyncMode.Local && positionTransform != null)
            {
                bool wasAbsolute = _useAbsoluteFrame;
                ResolvePositionTransform();
                if (wasAbsolute != _useAbsoluteFrame)
                    ForceSync();
            }

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
                _position.Teleport(MakePositionSample(p, data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, data.scale));
        }

        private NetworkTransformData _latestData;
        private NetworkTransformData _currentData;
        private NetworkTransformData _lastReadData;
        private NetworkTransformData _lastSentDelta;

        public void GatherState()
        {
            _currentData = _latestData;
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
        }

        public bool HasChanges()
        {
            return !_currentData.Equals(_lastSentDelta);
        }

        public void DeltaSave()
        {
            _lastSentDelta = _currentData;
        }

        private NetworkTransformState _capturedState;
        private bool _hasCapturedState;
        private uint _capturedRevision;
        private byte _sendGen;
        private byte _recvGen;
        private bool _hasRecvGen;
        // A reliable snapshot is an authoritative epoch anchor. After one arrives, a negative
        // signed byte delta is stale; normal byte wrap still presents as a positive delta.
        private bool _hasAuthoritativeRecvGen;
        private long _lastAppliedOrder;
        private bool _hasAppliedSeq;

        internal NetworkTransformState capturedState => _capturedState;

        internal uint capturedRevision => _capturedRevision;

        internal byte sendGen => _sendGen;

        private uint _sendGenEpoch;

        internal uint sendGenEpoch => _sendGenEpoch;

        private void BumpSendGen()
        {
            _sendGen++;
            _sendGenEpoch++;

            if (TryGetNetworkTransformModule(out var ntModule) && id.HasValue)
                ntModule.ClearGenerationOverrides(id.Value);
        }

        private void BumpSendGen(PlayerID target)
        {
            if (TryGetNetworkTransformModule(out var ntModule) && id.HasValue)
                ntModule.PrepareTargetedReset(target, id.Value, _sendGen, _sendGenEpoch);

            _sendGen++;
            _sendGenEpoch++;
        }

        private bool TryGetNetworkTransformModule(out NetworkTransformModule ntModule)
        {
            ntModule = null;

            return networkManager &&
                   networkManager.TryGetModule<NetworkTransformFactory>(isServer, out var factory) &&
                   factory.TryGetModule(sceneId, out ntModule);
        }

        private void ResetUnreliableRecvState()
        {
            _hasRecvGen = false;
            _hasAuthoritativeRecvGen = false;
            _hasAppliedSeq = false;
            _hasLastAppliedState = false;
            ClearAdaptiveAnchors();
        }

        private NetworkTransformState _lastAppliedState;
        private ushort _lastAppliedSenderTick;
        private bool _hasLastAppliedState;

        private const float CORRECTION_DECAY = 0.65f;
        private const float RENDER_RATE_GAIN = 0.1f;
        private const float RENDER_RATE_MAX_ADJUST = 0.5f;

        private float _renderRel;
        private bool _hasRenderTimeline;

        private Vector3 _corrPosOffset;
        private Quaternion _corrRotOffset = Quaternion.identity;
        private Vector3 _corrScaleOffset;
        private float _corrWeight;
        private NetworkTransformFrame _corrFrame;
        private NetworkID _corrParentId;
        private bool _hasCorrOffset;
        private bool _corrPending;

        private NetworkTransformState _anchorState;
        private NetworkTransformVelocity _anchorVelocity;
        private uint _anchorLocalTick;
        private ushort _anchorSenderTick;
        private int _anchorGap;
        private bool _hasAdaptiveAnchor;

        private NetworkTransformState _prevAnchorState;
        private NetworkTransformVelocity _prevAnchorVelocity;
        private uint _prevAnchorLocalTick;
        private ushort _prevAnchorSenderTick;
        private int _prevAnchorGap;
        private bool _hasPrevAnchor;

        private uint _lastAdaptiveTick;

        private const int RECV_HISTORY_SIZE = 32;

        private readonly NetworkTransformState[] _recvStates = new NetworkTransformState[RECV_HISTORY_SIZE];
        private readonly ushort[] _recvTicks = new ushort[RECV_HISTORY_SIZE];
        private int _recvCount;
        private int _recvHead;

        private void PushReceivedSample(ushort senderTick, in NetworkTransformState state)
        {
            _recvHead = (_recvHead + 1) % RECV_HISTORY_SIZE;
            _recvStates[_recvHead] = state;
            _recvTicks[_recvHead] = senderTick;
            if (_recvCount < RECV_HISTORY_SIZE)
                _recvCount++;
        }

        private NetworkTransformState SampleReceivedHistory(ushort targetTick)
        {
            NetworkTransformState upperState = default;
            NetworkTransformState lowerState = default;
            ushort upperTick = 0;
            ushort lowerTick = 0;
            bool hasUpper = false;
            bool hasLower = false;

            NetworkTransformState prevState = default;
            ushort prevTick = 0;
            bool hasPrev = false;

            for (int i = 0; i < _recvCount; i++)
            {
                int idx = (_recvHead - i + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
                short diff = (short)(_recvTicks[idx] - targetTick);

                if (diff <= 0)
                {
                    lowerState = _recvStates[idx];
                    lowerTick = _recvTicks[idx];
                    hasLower = true;

                    if (i + 1 < _recvCount)
                    {
                        int prevIdx = (_recvHead - i - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
                        prevState = _recvStates[prevIdx];
                        prevTick = _recvTicks[prevIdx];
                        hasPrev = true;
                    }

                    break;
                }

                upperState = _recvStates[idx];
                upperTick = _recvTicks[idx];
                hasUpper = true;
            }

            if (!hasLower)
                return hasUpper ? upperState : _anchorState;

            if (!hasUpper || lowerTick == upperTick)
                return lowerState;

            short span = (short)(upperTick - lowerTick);
            if (span <= 0)
                return upperState;

            short into = (short)(targetTick - lowerTick);

            if (lowerState.frame != upperState.frame || !lowerState.parentId.Equals(upperState.parentId))
            {
                if (!hasPrev || prevState.frame != lowerState.frame ||
                    !prevState.parentId.Equals(lowerState.parentId))
                    return lowerState;

                int prevGap = (short)(lowerTick - prevTick);
                if (prevGap < 1)
                    return lowerState;

                var chord = NetworkTransformVelocity.Derive(prevState, lowerState, prevGap);
                return NetworkTransformVelocity.Predict(lowerState, chord, into);
            }

            float t = into / (float)span;

            if (hasPrev && _activeStrategy != null &&
                _activeStrategy.TryReconstructState(prevState, lowerState, upperState, t, out var shaped))
                return shaped;

            return NetworkTransformVelocity.Lerp(lowerState, upperState, t);
        }

        private bool TryStrategyExtrapolation(int back, ushort targetTick, out NetworkTransformState result)
        {
            result = default;

            if (_recvCount < back + 3)
                return false;

            int i0 = (_recvHead - back + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
            int i1 = (i0 - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
            int i2 = (i1 - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;

            int span = (short)(_recvTicks[i0] - _recvTicks[i1]);
            if (span < 2)
                return false;

            int rel = (short)(targetTick - _recvTicks[i0]);
            if (rel <= 0)
                return false;

            float t = (span + rel) / (float)span;
            return _activeStrategy.TryReconstructState(_recvStates[i2], _recvStates[i1], _recvStates[i0], t,
                out result);
        }

        private void ClearAdaptiveAnchors()
        {
            _hasAdaptiveAnchor = false;
            _hasPrevAnchor = false;
            _recvCount = 0;
            _hasLastSample = false;
            _hasRenderTimeline = false;
            _hasCorrOffset = false;
            _corrPending = false;
            _seamOffset = Vector3.zero;
        }

        private void SetAdaptiveAnchor(in NetworkTransformState state, in NetworkTransformVelocity velocity, int gap)
        {
            var nm = networkManager;
            uint localTick = nm && nm.tickModule != null ? nm.tickModule.localTick : 0u;

            if (_hasAdaptiveAnchor)
            {
                _prevAnchorState = _anchorState;
                _prevAnchorVelocity = _anchorVelocity;
                _prevAnchorLocalTick = _anchorLocalTick;
                _prevAnchorSenderTick = _anchorSenderTick;
                _prevAnchorGap = _anchorGap;
                _hasPrevAnchor = true;
                _corrPending = true;
            }

            _anchorState = state;
            _anchorVelocity = velocity;
            _anchorLocalTick = localTick;
            _anchorSenderTick = _lastAppliedSenderTick;
            _anchorGap = gap;
            _hasAdaptiveAnchor = true;
        }

        internal bool TryTickAdaptiveRender(uint localTick, ushort vouchedTick, bool hasVouched,
            out NetworkTransformState state)
        {
            state = default;

            if (!_hasStrategy || _cachedIsController || !_hasAdaptiveAnchor)
                return false;

            if (_lastAdaptiveTick == localTick)
                return false;

            _lastAdaptiveTick = localTick;

            long maxAhead = _adaptiveSpacing + 2;
            float targetRel;

            if (hasVouched)
            {
                targetRel = (short)(vouchedTick - _lastAppliedSenderTick);
            }
            else
            {
                long age = (long)localTick - _anchorLocalTick;
                if (age < 0)
                    age = 0;
                targetRel = age - _adaptiveSpacing;
            }

            if (targetRel > maxAhead)
                targetRel = maxAhead;

            if (!_hasRenderTimeline || Mathf.Abs(targetRel - _renderRel) > maxAhead + _adaptiveSpacing)
            {
                _renderRel = targetRel;
                _hasRenderTimeline = true;
            }
            else
            {
                float rate = 1f + Mathf.Clamp((targetRel - _renderRel) * RENDER_RATE_GAIN,
                    -RENDER_RATE_MAX_ADJUST, RENDER_RATE_MAX_ADJUST);
                _renderRel += rate;
                if (_renderRel > maxAhead)
                    _renderRel = maxAhead;
            }

            int relFloor = Mathf.FloorToInt(_renderRel);
            float frac = _renderRel - relFloor;
            ushort tickA = (ushort)(_lastAppliedSenderTick + relFloor);
            ushort tickB = (ushort)(tickA + 1);

            var target = SampleAdaptiveAt(relFloor, tickA);
            if (frac > 0f)
            {
                var next = SampleAdaptiveAt(relFloor + 1, tickB);
                if (next.frame == target.frame && next.parentId.Equals(target.parentId))
                    target = NetworkTransformVelocity.Lerp(target, next, frac);
            }

            if (_hasPrevAnchor &&
                (_prevAnchorState.frame != _anchorState.frame ||
                 !_prevAnchorState.parentId.Equals(_anchorState.parentId)))
            {
                _hasPrevAnchor = false;
                _corrPending = false;
            }

            if (_corrPending && _hasPrevAnchor &&
                target.frame == _anchorState.frame && target.parentId.Equals(_anchorState.parentId))
            {
                long prevAge = (short)(tickA - _prevAnchorSenderTick);
                if (prevAge < 0)
                    prevAge = 0;
                if (prevAge > maxAhead + _adaptiveSpacing)
                    prevAge = maxAhead + _adaptiveSpacing;

                var old = SampleOldAnchorAt(tickA, (int)prevAge);
                if (frac > 0f)
                {
                    var oldNext = SampleOldAnchorAt(tickB, (int)prevAge + 1);
                    if (oldNext.frame == old.frame && oldNext.parentId.Equals(old.parentId))
                        old = NetworkTransformVelocity.Lerp(old, oldNext, frac);
                }

                if (old.frame == target.frame && old.parentId.Equals(target.parentId))
                    CaptureCorrectionOffset(old, target);
            }

            _corrPending = false;
            _hasPrevAnchor = false;

            if (_hasCorrOffset && (target.frame != _corrFrame || !target.parentId.Equals(_corrParentId)))
                _hasCorrOffset = false;

            if (_hasCorrOffset)
            {
                _corrWeight *= CORRECTION_DECAY;
                if (_corrWeight < 0.02f)
                    _hasCorrOffset = false;
                else
                    ApplyCorrectionOffset(ref target);
            }

            state = target;
            return true;
        }

        private void CaptureCorrectionOffset(in NetworkTransformState old, in NetworkTransformState target)
        {
            var posDelta = Vector3.zero;
            if (old.data.position.HasValue && target.data.position.HasValue)
            {
                var op = old.data.position.Value;
                var tp = target.data.position.Value;
                posDelta = new Vector3(op.x.value - tp.x.value, op.y.value - tp.y.value, op.z.value - tp.z.value);
            }

            var oRot = old.data.rotation;
            var tRot = target.data.rotation;
            var rotDelta = new Quaternion(oRot.x, oRot.y, oRot.z, oRot.w).normalized *
                           Quaternion.Inverse(new Quaternion(tRot.x, tRot.y, tRot.z, tRot.w).normalized);

            var oScale = old.data.scale;
            var tScale = target.data.scale;
            var scaleDelta = new Vector3(oScale.x.value - tScale.x.value, oScale.y.value - tScale.y.value,
                oScale.z.value - tScale.z.value);

            if (_hasCorrOffset)
            {
                posDelta += _corrPosOffset * _corrWeight;
                rotDelta = Quaternion.Slerp(Quaternion.identity, _corrRotOffset, _corrWeight) * rotDelta;
                scaleDelta += _corrScaleOffset * _corrWeight;
            }

            _corrPosOffset = posDelta;
            _corrRotOffset = rotDelta;
            _corrScaleOffset = scaleDelta;
            _corrWeight = 1f;
            _corrFrame = target.frame;
            _corrParentId = target.parentId;
            _hasCorrOffset = true;
        }

        private void ApplyCorrectionOffset(ref NetworkTransformState target)
        {
            if (target.data.position.HasValue && _corrPosOffset != Vector3.zero)
            {
                var p = target.data.position.Value;
                var pos = new Vector3(p.x.value, p.y.value, p.z.value) + _corrPosOffset * _corrWeight;
                target.data.position = (CompressedVector3)pos;
            }

            if (Mathf.Abs(_corrRotOffset.w) < 0.9999999f)
            {
                var r = target.data.rotation;
                var rot = Quaternion.Slerp(Quaternion.identity, _corrRotOffset, _corrWeight) *
                          new Quaternion(r.x, r.y, r.z, r.w);
                r.x = new NormalizedFloat(rot.x);
                r.y = new NormalizedFloat(rot.y);
                r.z = new NormalizedFloat(rot.z);
                r.w = new NormalizedFloat(rot.w);
                target.data.rotation = r;
            }

            if (_corrScaleOffset != Vector3.zero)
            {
                var s = target.data.scale;
                var scale = new Vector3(s.x.value, s.y.value, s.z.value) + _corrScaleOffset * _corrWeight;
                target.data.scale = (CompressedVector3)scale;
            }
        }

        private NetworkTransformState SampleAdaptiveAt(int rel, ushort tick)
        {
            if (rel < 0)
                return SampleReceivedHistory(tick);

            return TryStrategyExtrapolation(0, tick, out var shaped)
                ? shaped
                : NetworkTransformVelocity.Predict(_anchorState, _anchorVelocity, rel);
        }

        private NetworkTransformState SampleOldAnchorAt(ushort tick, int prevAge)
        {
            return TryStrategyExtrapolation(1, tick, out var shaped)
                ? shaped
                : NetworkTransformVelocity.Predict(_prevAnchorState, _prevAnchorVelocity, prevAge);
        }

        private const float SEAM_OFFSET_DECAY = 0.8f;

        private NetworkTransformFrame _lastSampleFrame;
        private NetworkID _lastSampleParentId;
        private Vector3 _lastSampleWorldPos;
        private Vector3 _seamOffset;
        private bool _hasLastSample;

        internal void ApplyAdaptiveSample(in NetworkTransformState state, NetworkIdentity frameParent)
        {
            var p = state.frame switch
            {
                NetworkTransformFrame.LocalIdentity => frameParent.transform,
                NetworkTransformFrame.LocalStatic => _trs.parent,
                _ => null
            };

            if (syncPosition && state.data.position.HasValue)
            {
                var quantized = state.data.position.Value;
                var localPos = new Vector3(quantized.x.value, quantized.y.value, quantized.z.value);
                bool isLocal = _syncPosition == SyncMode.Local && p;
                var world = isLocal ? p.TransformPoint(localPos) : localPos;

                if (_hasLastSample &&
                    (state.frame != _lastSampleFrame || !state.parentId.Equals(_lastSampleParentId)))
                    _seamOffset += _lastSampleWorldPos - world;

                _lastSampleFrame = state.frame;
                _lastSampleParentId = state.parentId;

                if (_seamOffset.sqrMagnitude > 0.000001f)
                {
                    _seamOffset *= SEAM_OFFSET_DECAY;
                    world += _seamOffset;

                    var adjusted = state;
                    adjusted.data.position = (CompressedVector3)(isLocal ? p.InverseTransformPoint(world) : world);
                    _lastSampleWorldPos = world;
                    _hasLastSample = true;
                    AddStateToBuffers(adjusted, p);
                    return;
                }

                _seamOffset = Vector3.zero;
                _lastSampleWorldPos = world;
                _hasLastSample = true;
            }

            AddStateToBuffers(state, p);
        }

        private void TeleportBuffers(in NetworkTransformState state, Transform p)
        {
            if (syncPosition)
                _position.Teleport(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, state.data.scale));
        }

        private void AddStateToBuffers(in NetworkTransformState state, Transform p)
        {
            if (syncPosition)
                _position.Add(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, state.data.scale));
        }

        internal void ResetUnreliableStream()
        {
            BumpSendGen();
            ResetUnreliableRecvState();
        }

        private bool ForceAdoptRecvGen(byte gen)
        {
            bool alreadyAhead = _hasRecvGen && _hasAppliedSeq && gen == _recvGen;

            _recvGen = gen;
            _hasRecvGen = true;
            _hasAuthoritativeRecvGen = true;

            if (!alreadyAhead)
                _hasAppliedSeq = false;

            return !alreadyAhead;
        }

        private Transform _cachedParentTrs;
        private NetworkIdentity _cachedParentIdentity;
        private NetworkTransformFrame _latestFrame;
        private NetworkID _latestParentId;
        private NetworkTransformFrame _currentFrame;
        private NetworkID _currentParentId;

        private void RefreshLatestFrame()
        {
            var p = _trs.parent;

            if (!ReferenceEquals(p, _cachedParentTrs))
            {
                _cachedParentTrs = p;
                _cachedParentIdentity = p && p.TryGetComponent<NetworkIdentity>(out var found) ? found : null;
            }

            var parentIdentity = _cachedParentIdentity;

            if (_syncParent && parentIdentity && parentIdentity.isSpawned && parentIdentity.id.HasValue)
            {
                _latestFrame = NetworkTransformFrame.LocalIdentity;
                _latestParentId = parentIdentity.id.Value;
            }
            else
            {
                _latestFrame = p ? NetworkTransformFrame.LocalStatic : NetworkTransformFrame.World;
                _latestParentId = default;
            }
        }

        private const int CAPTURE_HISTORY_SIZE = 32;

        private readonly NetworkTransformState[] _historyStates = new NetworkTransformState[CAPTURE_HISTORY_SIZE];
        private readonly ushort[] _historyTicks = new ushort[CAPTURE_HISTORY_SIZE];
        private readonly bool[] _historyUsed = new bool[CAPTURE_HISTORY_SIZE];

        internal bool TryGetCapturedAt(ushort tick, out NetworkTransformState state)
        {
            int slot = tick % CAPTURE_HISTORY_SIZE;
            if (_historyUsed[slot] && _historyTicks[slot] == tick)
            {
                state = _historyStates[slot];
                return true;
            }

            state = default;
            return false;
        }

        internal bool IsChordInterpolable(in NetworkTransformState from, ushort fromTick, ushort currentTick,
            in NetworkTransformState current)
        {
            int gap = (short)(currentTick - fromTick);
            if (gap <= 1)
                return true;

            if (current.frame != from.frame || !current.parentId.Equals(from.parentId))
                return false;

            var chord = NetworkTransformVelocity.Derive(from, current, gap);

            for (int step = 1; step < gap; step++)
            {
                if (!TryGetCapturedAt((ushort)(fromTick + step), out var actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                var expected = NetworkTransformVelocity.Predict(from, chord, step);
                if (!NTUnreliable.PredictionMatches(expected, actual, chord))
                    return false;
            }

            return true;
        }

        internal void CaptureUnreliableState()
        {
            var nm = networkManager;
            ushort tick = nm && nm.tickModule != null ? (ushort)nm.tickModule.localTick : (ushort)0;
            CaptureUnreliableState(tick);
        }

        internal void CaptureUnreliableState(ushort tick)
        {
            var state = currentState;

            if (!syncPosition)
            {
                state.data.position = default(CompressedVector3);
                state.data.absolutePosition = null;
            }

            if (!syncRotation)
                state.data.rotation = Quaternion.identity;

            if (!syncScale)
                state.data.scale = default;

            if (!usesNetworkFrame)
            {
                state.frame = NetworkTransformFrame.World;
                state.parentId = default;
            }

            if (!_hasCapturedState || !_capturedState.Equals(state))
            {
                _capturedState = state;
                _capturedRevision++;
                _hasCapturedState = true;
            }

            int slot = tick % CAPTURE_HISTORY_SIZE;
            _historyStates[slot] = _capturedState;
            _historyTicks[slot] = tick;
            _historyUsed[slot] = true;
        }

        private bool usesNetworkFrame => _syncPosition == SyncMode.Local ||
                                         _syncRotation == SyncMode.Local ||
                                         _syncScale;

        private NetworkTransformState currentState => new NetworkTransformState
        {
            data = _currentData,
            frame = _currentFrame,
            parentId = _currentParentId
        };

        private void RefreshCurrentState()
        {
            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            RefreshLatestFrame();
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
        }

        private void AdoptState(in NetworkTransformState state)
        {
            _hasLastAppliedState = false;
            ClearAdaptiveAnchors();
            _lastReadData = state.data;
            _currentData = state.data;
            _latestData = state.data;
            _latestFrame = state.frame;
            _latestParentId = state.parentId;
            _currentFrame = state.frame;
            _currentParentId = state.parentId;
        }

        private Transform ResolveFrameParent(in NetworkTransformState state)
        {
            switch (state.frame)
            {
                case NetworkTransformFrame.LocalIdentity:
                    if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                        factory.TryGetIdentity(sceneId, state.parentId, out var identity) && identity)
                        return identity.transform;
                    return _trs.parent;
                case NetworkTransformFrame.LocalStatic:
                    return _trs.parent;
                case NetworkTransformFrame.World:
                default:
                    return null;
            }
        }

        private void TeleportToState(in NetworkTransformState state)
        {
            var p = ResolveFrameParent(state);

            if (syncPosition)
                _position.Teleport(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, state.data.scale));
        }

        internal bool CanDeltaAgainst(in NetworkTransformState baseline)
        {
            return baseline.data.absolutePosition.HasValue == _capturedState.data.absolutePosition.HasValue;
        }

        internal void WriteAbsoluteState(BitPacker packer)
        {
            var state = _capturedState;

            if (usesNetworkFrame)
            {
                packer.WriteBits((ulong)state.frame, 2);
                if (state.frame == NetworkTransformFrame.LocalIdentity)
                    Packer<NetworkID>.Write(packer, state.parentId);
            }

            if (syncPosition)
            {
                bool isAbsolute = state.data.absolutePosition.HasValue;
                packer.WriteBits(isAbsolute ? 1UL : 0UL, 1);

                if (isAbsolute)
                    Packer<double3>.Write(packer, state.data.absolutePosition.Value);
                else
                    Packer<CompressedVector3>.Write(packer, state.data.position.GetValueOrDefault());
            }

            if (syncRotation)
                Packer<PackedQuaternion>.Write(packer, state.data.rotation);

            if (syncScale)
                Packer<CompressedVector3>.Write(packer, state.data.scale);
        }

        internal NetworkTransformState ReadAbsoluteState(BitPacker packer)
        {
            var state = default(NetworkTransformState);

            if (usesNetworkFrame)
            {
                state.frame = (NetworkTransformFrame)packer.ReadBits(2);
                if (state.frame == NetworkTransformFrame.LocalIdentity)
                    Packer<NetworkID>.Read(packer, ref state.parentId);
            }
            else
            {
                state.frame = NetworkTransformFrame.World;
            }

            if (syncPosition)
            {
                bool isAbsolute = packer.ReadBits(1) == 1;
                if (isAbsolute)
                {
                    double3 pos = default;
                    Packer<double3>.Read(packer, ref pos);
                    state.data.absolutePosition = pos;
                }
                else
                {
                    CompressedVector3 pos = default;
                    Packer<CompressedVector3>.Read(packer, ref pos);
                    state.data.position = pos;
                }
            }
            else
            {
                state.data.position = default(CompressedVector3);
            }

            if (syncRotation)
                Packer<PackedQuaternion>.Read(packer, ref state.data.rotation);
            else
                state.data.rotation = Quaternion.identity;

            if (syncScale)
                Packer<CompressedVector3>.Read(packer, ref state.data.scale);

            return state;
        }

        internal void WriteDeltaState(BitPacker packer, in NetworkTransformState baseline, in NetworkTransformState predicted)
        {
            var state = _capturedState;

            if (usesNetworkFrame)
            {
                bool sameFrame = state.frame == baseline.frame && state.parentId.Equals(baseline.parentId);
                packer.WriteBits(sameFrame ? 1UL : 0UL, 1);
                if (!sameFrame)
                {
                    packer.WriteBits((ulong)state.frame, 2);
                    if (state.frame == NetworkTransformFrame.LocalIdentity)
                        DeltaPacker<NetworkID>.Write(packer, baseline.parentId, state.parentId);
                }
            }

            if (syncPosition)
            {
                if (state.data.absolutePosition.HasValue)
                {
                    var oldPos = baseline.data.absolutePosition.GetValueOrDefault();
                    var newPos = state.data.absolutePosition.GetValueOrDefault();
                    bool changed = !oldPos.Equals(newPos);
                    packer.WriteBits(changed ? 1UL : 0UL, 1);
                    if (changed)
                        DeltaPacker<double3>.Write(packer, oldPos, newPos);
                }
                else
                {
                    var newPos = state.data.position.GetValueOrDefault();
                    bool changed = !baseline.data.position.GetValueOrDefault().Equals(newPos);
                    packer.WriteBits(changed ? 1UL : 0UL, 1);
                    if (changed)
                        DeltaPacker<CompressedVector3>.Write(packer, predicted.data.position.GetValueOrDefault(), newPos);
                }
            }

            if (syncRotation)
            {
                bool changed = !state.data.rotation.Equals(baseline.data.rotation);
                packer.WriteBits(changed ? 1UL : 0UL, 1);
                if (changed)
                    DeltaPacker<PackedQuaternion>.Write(packer, predicted.data.rotation, state.data.rotation);
            }

            if (syncScale)
            {
                bool changed = !state.data.scale.Equals(baseline.data.scale);
                packer.WriteBits(changed ? 1UL : 0UL, 1);
                if (changed)
                    DeltaPacker<CompressedVector3>.Write(packer, predicted.data.scale, state.data.scale);
            }
        }

        internal NetworkTransformState ReadDeltaState(BitPacker packer, in NetworkTransformState baseline, in NetworkTransformState predicted)
        {
            var state = default(NetworkTransformState);

            if (usesNetworkFrame)
            {
                bool sameFrame = packer.ReadBits(1) == 1;
                if (sameFrame)
                {
                    state.frame = baseline.frame;
                    state.parentId = baseline.parentId;
                }
                else
                {
                    state.frame = (NetworkTransformFrame)packer.ReadBits(2);
                    if (state.frame == NetworkTransformFrame.LocalIdentity)
                    {
                        state.parentId = baseline.parentId;
                        DeltaPacker<NetworkID>.Read(packer, baseline.parentId, ref state.parentId);
                    }
                }
            }
            else
            {
                state.frame = NetworkTransformFrame.World;
            }

            state.data = baseline.data;

            if (syncPosition && packer.ReadBits(1) == 1)
            {
                if (baseline.data.absolutePosition.HasValue)
                {
                    var oldPos = baseline.data.absolutePosition.Value;
                    var newPos = oldPos;
                    DeltaPacker<double3>.Read(packer, oldPos, ref newPos);
                    state.data.absolutePosition = newPos;
                }
                else
                {
                    var refPos = predicted.data.position.GetValueOrDefault();
                    var newPos = refPos;
                    DeltaPacker<CompressedVector3>.Read(packer, refPos, ref newPos);
                    state.data.position = newPos;
                }
            }

            if (syncRotation && packer.ReadBits(1) == 1)
            {
                state.data.rotation = predicted.data.rotation;
                DeltaPacker<PackedQuaternion>.Read(packer, predicted.data.rotation, ref state.data.rotation);
            }

            if (syncScale && packer.ReadBits(1) == 1)
            {
                var refScale = predicted.data.scale;
                var newScale = refScale;
                DeltaPacker<CompressedVector3>.Read(packer, refScale, ref newScale);
                state.data.scale = newScale;
            }

            return state;
        }

        internal bool TryApplyUnreliableState(in NetworkTransformState state, byte gen, long packetOrder,
            ushort senderTick, NetworkIdentity frameParent, bool isAbsolute)
        {
            if (_cachedIsController)
                return true;

            if (_hasRecvGen)
            {
                var genDiff = (sbyte)(gen - _recvGen);

                switch (genDiff)
                {
                    case < 0 when _hasAuthoritativeRecvGen || !isAbsolute || genDiff >= -8:
                        return false;
                    case < 0:
                    case > 0:
                        _recvGen = gen;
                        _hasAppliedSeq = false;
                        break;
                }
            }
            else
            {
                _recvGen = gen;
                _hasRecvGen = true;
                _hasAuthoritativeRecvGen = false;
                _hasAppliedSeq = false;
            }

            if (!NTUnreliable.ShouldApplyOrder(_hasAppliedSeq, _lastAppliedOrder, packetOrder))
                return true;

            if (state.frame == NetworkTransformFrame.LocalIdentity && !frameParent)
                return false;

            var p = state.frame switch
            {
                NetworkTransformFrame.LocalIdentity => frameParent.transform,
                NetworkTransformFrame.LocalStatic => _trs.parent,
                _ => null
            };

            int gap = 0;

            if (_hasStrategy && !isAbsolute && _hasLastAppliedState &&
                state.frame == _lastAppliedState.frame && state.parentId.Equals(_lastAppliedState.parentId))
                gap = (short)(senderTick - _lastAppliedSenderTick);

            var previous = _lastAppliedState;

            if (_hasRenderTimeline && _hasLastAppliedState)
                _renderRel -= (short)(senderTick - _lastAppliedSenderTick);

            _lastAppliedOrder = packetOrder;
            _hasAppliedSeq = true;
            _lastReadData = state.data;
            _lastAppliedState = state;
            _lastAppliedSenderTick = senderTick;
            _hasLastAppliedState = true;

            if (_hasStrategy)
            {
                if (isAbsolute)
                {
                    ClearAdaptiveAnchors();
                    SetAdaptiveAnchor(state, default, 0);
                    TeleportBuffers(state, p);
                }
                else
                {
                    int velocityGap = gap >= 1 && gap <= NTUnreliable.ADAPTIVE_MAX_BACKFILL ? gap : 0;
                    var velocity = velocityGap >= 1
                        ? NetworkTransformVelocity.Derive(previous, state, velocityGap)
                        : default;
                    SetAdaptiveAnchor(state, velocity, velocityGap);
                }

                PushReceivedSample(senderTick, state);
                return true;
            }

            AddStateToBuffers(state, p);

            return true;
        }

        private bool _cachedConnectedOwner;

        protected override void OnOwnerDisconnected(PlayerID ownerId)
        {
            _cachedConnectedOwner = false;
            BumpSendGen();
            if (isServer)
                ResetUnreliableRecvState();
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
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

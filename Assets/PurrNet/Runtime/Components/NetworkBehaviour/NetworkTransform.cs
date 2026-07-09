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

            // Only the server's inbound sender changes on ownership swaps; observers keep
            // receiving the server's monotonic gen and resetting their gate here would
            // re-open it to stale in-flight samples.
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
            // No caller reaches this on a host (all are !isServer-guarded), so the codegen
            // host shortcut with its default RPCInfo never hits this gate.
            if (!_ownerAuth || !IsControlling(info.sender, false))
                return;

            // Stale RPC (newer same-gen owner samples already applied): forwarding it with a
            // fresh gen would teleport every observer backward.
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

            // Stale RPC (newer same-gen owner samples already applied): adopting would let the
            // same-tick relay ship the older pose to observers under a fresh gen.
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
            bool apply = ForceAdoptRecvGen(gen) && applyPosition;
            AdoptState(state);

            if (apply)
            {
                TeleportToState(state);
                ApplyLerpedPosition();
            }
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
            // Unconditional: a dedicated server relaying an owner-auth NT sends without being its controller.
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

        private void ApplyData(NetworkTransformData data)
        {
            var p = _trs.parent;

            if (syncPosition)
                _position.Add(MakePositionSample(p, data));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, data.scale));
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

        /// <summary>
        /// Compatibility entry point for the legacy reliable delta path.
        /// </summary>
        public bool HasChanges()
        {
            return !_currentData.Equals(_lastSentDelta);
        }

        /// <summary>
        /// Compatibility entry point for consumers that still use the legacy delta API.
        /// </summary>
        public void DeltaWrite(BitPacker packer)
        {
            if (syncPosition)
            {
                if (_useAbsoluteFrame)
                    DeltaPacker<double3>.Write(packer, _lastSentDelta.absolutePosition.GetValueOrDefault(),
                        _currentData.absolutePosition.GetValueOrDefault());
                else
                    DeltaPacker<CompressedVector3>.Write(packer, _lastSentDelta.position.GetValueOrDefault(),
                        _currentData.position.GetValueOrDefault());
            }

            if (syncRotation)
                DeltaPacker<PackedQuaternion>.Write(packer, _lastSentDelta.rotation, _currentData.rotation);

            if (syncScale)
                DeltaPacker<CompressedVector3>.Write(packer, _lastSentDelta.scale, _currentData.scale);
        }

        /// <summary>
        /// Compatibility entry point for consumers that still use the legacy delta API.
        /// </summary>
        public void DeltaRead(BitPacker packet)
        {
            var data = _lastReadData;

            if (syncPosition)
            {
                if (data.absolutePosition.HasValue)
                {
                    var oldPosition = data.absolutePosition.Value;
                    var newPosition = oldPosition;
                    DeltaPacker<double3>.Read(packet, oldPosition, ref newPosition);
                    data.absolutePosition = newPosition;
                }
                else
                {
                    var oldPosition = data.position.GetValueOrDefault();
                    var newPosition = oldPosition;
                    DeltaPacker<CompressedVector3>.Read(packet, oldPosition, ref newPosition);
                    data.position = newPosition;
                }
            }

            if (syncRotation)
            {
                var oldRotation = data.rotation;
                DeltaPacker<PackedQuaternion>.Read(packet, oldRotation, ref data.rotation);
            }

            if (syncScale)
            {
                var oldScale = data.scale;
                DeltaPacker<CompressedVector3>.Read(packet, oldScale, ref data.scale);
            }

            _lastReadData = data;
            ApplyData(data);
        }

        /// <summary>
        /// Compatibility entry point for consumers that still use the legacy delta API.
        /// </summary>
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
        private long _lastAppliedOrder;
        private bool _hasAppliedSeq;

        internal NetworkTransformState capturedState => _capturedState;

        internal uint capturedRevision => _capturedRevision;

        internal byte sendGen => _sendGen;

        // Wire gen is a byte and wraps; sender-side baseline validity compares this instead.
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
            _hasAppliedSeq = false;
        }

        internal void ResetUnreliableStream()
        {
            BumpSendGen();
            ResetUnreliableRecvState();
        }

        // Reliable resets are authoritative: always adopt (a rejected 'older' gen from a new
        // sender incarnation would deadlock the stream). Returns false only when newer samples
        // of the SAME gen were already applied — then the RPC payload is stale and must not
        // teleport backward or reopen the seq gate.
        private bool ForceAdoptRecvGen(byte gen)
        {
            bool alreadyAhead = _hasRecvGen && _hasAppliedSeq && gen == _recvGen;

            _recvGen = gen;
            _hasRecvGen = true;

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

        // Must run at the same moment _latestData is sampled: a stale-but-coherent (data, frame)
        // pair is correct, a mixed pair resolves the sample in the wrong space.
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

        internal void CaptureUnreliableState()
        {
            var state = currentState;

            // Canonicalize fields that are not part of this NetworkTransform's wire contract.
            // This keeps sender and receiver baselines byte-for-byte equivalent while allowing
            // absolute packets to omit disabled fields entirely.
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
                    double3 position = default;
                    Packer<double3>.Read(packer, ref position);
                    state.data.absolutePosition = position;
                }
                else
                {
                    CompressedVector3 position = default;
                    Packer<CompressedVector3>.Read(packer, ref position);
                    state.data.position = position;
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

        // Masks compare vs the raw baseline (untouched fields must never velocity-drift);
        // diffs encode vs the second-order prediction.
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
                    double3 newPos = oldPos;
                    DeltaPacker<double3>.Read(packer, oldPos, ref newPos);
                    state.data.absolutePosition = newPos;
                }
                else
                {
                    var refPos = predicted.data.position.GetValueOrDefault();
                    CompressedVector3 newPos = refPos;
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
                CompressedVector3 newScale = refScale;
                DeltaPacker<CompressedVector3>.Read(packer, refScale, ref newScale);
                state.data.scale = newScale;
            }

            return state;
        }

        /// <summary>
        /// Applies a decoded unreliable sample gated by generation and sequence.
        /// Returns true when the state may be recorded as a receive baseline; false when it
        /// must be discarded (older generation, or unapplicable and unsafe to ack-adopt).
        /// </summary>
        internal bool TryApplyUnreliableState(in NetworkTransformState state, byte gen, long packetOrder,
            NetworkIdentity frameParent, bool isAbsolute)
        {
            // Loopback/handoff echo: a controller records baselines but never adopts remote
            // gens or feeds its own interpolation from them.
            if (_cachedIsController)
                return true;

            if (_hasRecvGen)
            {
                var genDiff = (sbyte)(gen - _recvGen);

                if (genDiff < 0)
                {
                    // Slightly-behind = a stale in-flight sample: discard. Far-behind absolutes
                    // mean the gen spaces desynced (sbyte wrap during a stall, incarnation swap)
                    // and rejecting them would freeze the stream with no recovery path.
                    if (!isAbsolute || genDiff >= -8)
                        return false;

                    _recvGen = gen;
                    _hasAppliedSeq = false;
                }

                if (genDiff > 0)
                {
                    _recvGen = gen;
                    _hasAppliedSeq = false;
                }
            }
            else
            {
                _recvGen = gen;
                _hasRecvGen = true;
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

            _lastAppliedOrder = packetOrder;
            _hasAppliedSeq = true;
            _lastReadData = state.data;

            if (syncPosition)
                _position.Add(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, state.data.scale));

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

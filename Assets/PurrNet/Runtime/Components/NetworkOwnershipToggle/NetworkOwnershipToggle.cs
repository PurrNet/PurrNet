using JetBrains.Annotations;
using PurrNet.Contributors;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Ownership Toggle")]
    [Contributor("RoxDevvv", "https://github.com/RoxDevvv")]
    public sealed class NetworkOwnershipToggle : NetworkIdentity
    {
        [Tooltip("Components to toggle from the owner's perspective")]
        [SerializeField] private OwnershipComponentToggle[] _components;
        [Tooltip("GameObjects to toggle from the owner's perspective")]
        [SerializeField] private OwnershipGameObjectToggle[] _gameObjects;
        [Tooltip("Apply the final controller state in LateUpdate to avoid transient ownership changes during spawning")]
        [SerializeField] private bool _applyInLateUpdate;

        [SerializeField, HideInInspector] private GameObject[] _toActivate;
        [SerializeField, HideInInspector] private GameObject[] _toDeactivate;
        [SerializeField, HideInInspector] private Behaviour[] _toEnable;
        [SerializeField, HideInInspector] private Behaviour[] _toDisable;

        private bool _lastIsController;
        private bool _refreshPending;
        private bool _forceRefreshPending;

        private void Awake()
        {
            Setup(false);
        }

        protected override void OnSpawned()
        {
            RequestRefresh(true);
        }

        protected override void OnDespawned()
        {
            _refreshPending = false;
            _forceRefreshPending = false;
        }

        private void LateUpdate()
        {
            if (!_refreshPending)
                return;

            _refreshPending = false;

            bool force = _forceRefreshPending;
            _forceRefreshPending = false;

            if (!isSpawned)
                return;

            Refresh(force);
        }

        private void RequestRefresh(bool force = false)
        {
            if (_applyInLateUpdate)
            {
                _refreshPending = true;
                _forceRefreshPending |= force;
                return;
            }

            Refresh(force);
        }

        private void Refresh(bool force = false)
        {
            bool controller = isController;
            if (force || controller != _lastIsController)
                Setup(controller);
        }

        // migrate old data to _components and _gameObjects
        private void OnValidate()
        {
            if (_toActivate is { Length: > 0 } || _toDeactivate is { Length: > 0 })
            {
                int totalLength = _toActivate?.Length ?? 0 + _toDeactivate?.Length ?? 0;
                int offset = _toActivate?.Length ?? 0;

                _gameObjects = new OwnershipGameObjectToggle[totalLength];

                if (_toActivate is { Length: > 0 })
                {
                    for (var i = 0; i < _toActivate.Length; i++)
                    {
                        _gameObjects[i].target = _toActivate[i];
                        _gameObjects[i].activeAsOwner = true;
                    }
                }

                if (_toDeactivate is { Length: > 0 })
                {
                    for (var i = 0; i < _toDeactivate.Length; i++)
                    {
                        _gameObjects[i + offset].target = _toDeactivate[i];
                        _gameObjects[i + offset].activeAsOwner = false;
                    }
                }

                _toActivate = null;
                _toDeactivate = null;
            }

            if (_toEnable is { Length: > 0 } || _toDisable is { Length: > 0 })
            {
                int totalLength = _toEnable?.Length ?? 0 + _toDisable?.Length ?? 0;
                int offset = _toEnable?.Length ?? 0;

                _components = new OwnershipComponentToggle[totalLength];

                if (_toEnable is { Length: > 0 })
                {
                    for (var i = 0; i < _toEnable.Length; i++)
                    {
                        _components[i].target = _toEnable[i];
                        _components[i].activeAsOwner = true;
                    }
                }

                if (_toDisable is { Length: > 0 })
                {
                    for (var i = 0; i < _toDisable.Length; i++)
                    {
                        _components[i + offset].target = _toDisable[i];
                        _components[i + offset].activeAsOwner = false;
                    }
                }

                _toEnable = null;
                _toDisable = null;
            }
        }

        [UsedImplicitly]
        public void Setup(bool asOwner)
        {
            _lastIsController = asOwner;

            for (var i = 0; i < _components.Length; i++)
            {
                var target = _components[i].target;
                if (!target) continue;

                bool targetState = _components[i].activeAsOwner == asOwner;
                SetComponentState(target, targetState);
            }

            for (var i = 0; i < _gameObjects.Length; i++)
            {
                var target = _gameObjects[i].target;
                if (!target) continue;

                bool targetState = _gameObjects[i].activeAsOwner == asOwner;
                target.SetActive(targetState);
            }
        }

        private static void SetComponentState(Component target, bool targetState)
        {
            switch (target)
            {
                case Transform go:
                    go.gameObject.SetActive(targetState);
                    break;
                case Behaviour behaviour:
                    behaviour.enabled = targetState;
                    break;
#if UNITY_PHYSICS_3D
                case Collider col:
                    col.enabled = targetState;
                    break;
#endif
                case Renderer r:
                    r.enabled = targetState;
                    break;
            }
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            RequestRefresh();
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            RequestRefresh();
        }
    }
}

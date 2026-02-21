using PurrNet.Modules;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Collider Rollback")]
    public class ColliderRollback : PurrMonoBehaviour
    {
        [Tooltip("How long to store the collider state for rollback in seconds.\n" +
                 "This should be long enough to account for ping and jitter.")]
        [SerializeField, PurrLock, HideInInspector]
        float _storeHistoryInSeconds = 5f;

        [SerializeField, PurrLock, HideInInspector]
        bool _autoAddAllChildren = true;

#if UNITY_PHYSICS_3D
        [SerializeField, PurrLock, HideInInspector]
        Collider[] _colliders3D;
#endif

#if UNITY_PHYSICS_2D
        [SerializeField, PurrLock, HideInInspector]
        Collider2D[] _colliders2D;
#endif

#if UNITY_PHYSICS_3D
        public Collider[] colliders3D => _colliders3D;
#endif

#if UNITY_PHYSICS_2D
        public Collider2D[] colliders2D => _colliders2D;
#endif

        public float storeHistoryInSeconds => _storeHistoryInSeconds;

        private RollbackModule _moduleServer;
        private RollbackModule _moduleClient;

        private void Awake()
        {
            if (_autoAddAllChildren)
            {
#if UNITY_PHYSICS_3D
                _colliders3D = GetComponentsInChildren<Collider>(true);
#endif
#if UNITY_PHYSICS_2D
                _colliders2D = GetComponentsInChildren<Collider2D>(true);
#endif
            }
        }

        public override void Subscribe(NetworkManager manager, bool asServer)
        {
            if (!manager.TryGetModule<ScenesModule>(asServer, out var scenesModule))
                return;

            scenesModule.onSceneLoaded -= SceneLoaded;

            if (!scenesModule.TryGetSceneID(gameObject.scene, out var sceneID))
            {
                scenesModule.onSceneLoaded += SceneLoaded;
                return;
            }

            if (manager.TryGetModule<ColliderRollbackFactory>(asServer, out var factory) &&
                factory.TryGetModule(sceneID, out var module))
            {
                if (asServer)
                    _moduleServer = module;
                else _moduleClient = module;
                module.Register(this);
            }
        }

        private void SceneLoaded(SceneID scene, bool asServer)
        {
            Subscribe(manager, asServer);
        }

        public override void Unsubscribe(NetworkManager manager, bool asServer)
        {
            if (manager.TryGetModule<ScenesModule>(asServer, out var scenesModule))
                scenesModule.onSceneLoaded -= SceneLoaded;

            if (_moduleServer != null)
            {
                _moduleServer.Unregister(this);
                _moduleServer = null;
            }

            if (_moduleClient != null)
            {
                _moduleClient.Unregister(this);
                _moduleClient = null;
            }
        }

#if UNITY_PHYSICS_3D
        /// <summary>
        /// Registers a 3D collider for rollback.
        /// Duplicate registrations are ignored.
        /// </summary>
        /// <param name="collider">The collider to register.</param>
        /// <param name="asServer">Whether to register on the server or client.</param>
        public void RegisterCollider(Collider collider, bool asServer)
        {
            if (asServer)
                _moduleServer?.Register(collider, _storeHistoryInSeconds);
            else _moduleClient?.Register(collider, _storeHistoryInSeconds);
        }

        /// <summary>
        /// Unregisters a 3D collider from rollback.
        /// Duplicate unregistrations are ignored.
        /// </summary>
        /// <param name="collider">The collider to unregister.</param>
        /// <param name="asServer">Whether to unregister on the server or client.</param>
        public void UnregisterCollider(Collider collider, bool asServer)
        {
            if (asServer)
                _moduleServer?.Unregister(collider);
            else _moduleClient?.Unregister(collider);
        }
#endif

#if UNITY_PHYSICS_2D
        /// <summary>
        /// Registers a 2D collider for rollback.
        /// Duplicate registrations are ignored.
        /// </summary>
        /// <param name="collider">The collider to register.</param>
        /// <param name="asServer">Whether to register on the server or client.</param>
        public void RegisterCollider(Collider2D collider, bool asServer)
        {
            if (asServer)
                _moduleServer?.Register(collider, _storeHistoryInSeconds);
            else _moduleClient?.Register(collider, _storeHistoryInSeconds);
        }

        /// <summary>
        /// Unregisters a 2D collider from rollback.
        /// Duplicate unregistrations are ignored.
        /// </summary>
        /// <param name="collider">The collider to unregister.</param>
        /// <param name="asServer">Whether to unregister on the server or client.</param>
        public void UnregisterCollider(Collider2D collider, bool asServer)
        {
            if (asServer)
                _moduleServer?.Unregister(collider);
            else _moduleClient?.Unregister(collider);
        }
#endif
    }
}

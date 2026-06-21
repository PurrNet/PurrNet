using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    public struct SpawnPoint
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public interface IProvideSpawnPoints
    {
        public SpawnPoint NextSpawnPoint(PlayerID player, SceneID scene);
    }

    public interface IProvidePrefabInstantiated
    {
        public void OnPrefabInstantiated(GameObject prefabInstance, PlayerID player, SceneID scene);
    }

    [AddComponentMenu("PurrNet/Player Spawner")]
    public class PlayerSpawner : PurrMonoBehaviour
    {
        [SerializeField, HideInInspector] private NetworkIdentity playerPrefab;
        [SerializeField] private GameObject _playerPrefab;
        [Tooltip("Even if rules are to not despawn on disconnect, this will ignore that and always spawn a player.")]
        [SerializeField] private bool _ignoreNetworkRules;

        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
        private int _currentSpawnPoint;

        private IProvideSpawnPoints _spawnPointProvider;
        private IProvidePrefabInstantiated _prefabInstantiatedProvider;
        private bool _queuedLoadedPlayersReplay;
        private bool _usePromotedOwnershipFallback;
        private int _loadedPlayersReplayVersion;

        /// <summary>
        /// Sets a provider that will be used to provide spawn points for players.
        /// Spawn points lists will be ignored.
        /// </summary>
        public void SetRespawnPointProvider(IProvideSpawnPoints provider)
        {
            _spawnPointProvider = provider;
        }

        /// <summary>
        /// Resets the spawn point provider.
        /// Uses the spawn points list instead.
        /// </summary>
        public void ResetSpawnPointProvider()
        {
            _spawnPointProvider = null;
        }

        /// <summary>
        /// Sets a provider that will be used to notify when a player prefab has been instantiated.
        /// </summary>
        public void SetPrefabInstantiatedProvider(IProvidePrefabInstantiated provider)
        {
            _prefabInstantiatedProvider = provider;
        }

        /// <summary>
        /// Resets the prefab instantiated provider.
        /// </summary>
        public void ResetPrefabInstantiatedProvider()
        {
            _prefabInstantiatedProvider = null;
        }

        private void Awake()
        {
            CleanupSpawnPoints();
        }

        private void CleanupSpawnPoints()
        {
            bool hadNullEntry = false;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (!spawnPoints[i])
                {
                    hadNullEntry = true;
                    spawnPoints.RemoveAt(i);
                    i--;
                }
            }

            if (hadNullEntry)
                PurrLogger.LogWarning($"Some spawn points were invalid and have been cleaned up.", this);
        }

        private void OnValidate()
        {
            if (playerPrefab)
            {
                _playerPrefab = playerPrefab.gameObject;
                playerPrefab = null;
            }
        }

        public override void Subscribe(NetworkManager manager, bool asServer)
        {
            if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
            {
                scenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;

                if (manager.isPromotingToServer)
                    QueueLoadedPlayersReplay(manager);
                else
                    ReplayLoadedPlayers(manager);
            }
        }

        public override void Unsubscribe(NetworkManager manager, bool asServer)
        {
            _queuedLoadedPlayersReplay = false;
            _loadedPlayersReplayVersion++;

            if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
        }

        private void OnDestroy()
        {
            _queuedLoadedPlayersReplay = false;
            _loadedPlayersReplayVersion++;

            if (NetworkManager.main &&
                NetworkManager.main.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
        }

        private async void QueueLoadedPlayersReplay(NetworkManager manager)
        {
            if (_queuedLoadedPlayersReplay)
                return;

            _usePromotedOwnershipFallback = true;
            _queuedLoadedPlayersReplay = true;
            int version = ++_loadedPlayersReplayVersion;

            while (manager && manager.isPromotingToServer)
                await UnityLatestUpdate.Yield();

            if (!this || !isActiveAndEnabled || version != _loadedPlayersReplayVersion)
                return;

            _queuedLoadedPlayersReplay = false;
            ReplayLoadedPlayers(manager);
        }

        private void ReplayLoadedPlayers(NetworkManager manager)
        {
            if (!manager || !manager.isServer)
                return;

            if (!manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                return;

            if (!manager.TryGetModule(out ScenesModule scenes, true))
                return;

            if (!scenes.TryGetSceneID(gameObject.scene, out var sceneID))
                return;

            if (scenePlayersModule.TryGetPlayersInScene(sceneID, out var players))
            {
                foreach (var player in players)
                    OnPlayerLoadedScene(player, sceneID, true);
            }
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            var main = NetworkManager.main;

            if (!main || !main.TryGetModule(out ScenesModule scenes, true))
                return;

            if (main.isPromotingToServer)
            {
                QueueLoadedPlayersReplay(main);
                return;
            }

            var unityScene = gameObject.scene;

            if (!scenes.TryGetSceneID(unityScene, out var sceneID))
                return;

            if (sceneID != scene)
                return;

            if (!asServer)
                return;

            bool isDestroyOnDisconnectEnabled = main.networkRules.ShouldDespawnOnOwnerDisconnect();
            if (!_ignoreNetworkRules && !isDestroyOnDisconnectEnabled)
            {
                bool ownershipModuleHasExistingOwner =
                    main.TryGetModule(out GlobalOwnershipModule ownership, true) && ownership.PlayerOwnsSomething(player);
                if (ownershipModuleHasExistingOwner ||
                    PlayerOwnsIdentityInScene(player, unityScene) ||
                    (_usePromotedOwnershipFallback &&
                     (PlayerOwnsIdentityAnywhere(player) || HasEnoughExistingPrefabInstances(main, scene))))
                    return;
            }

            GameObject newPlayer;

            CleanupSpawnPoints();

            if (_spawnPointProvider != null)
            {
                var point = _spawnPointProvider.NextSpawnPoint(player, scene);
                newPlayer = UnityProxy.Instantiate(_playerPrefab, point.position, point.rotation, unityScene);
            }
            else if (spawnPoints.Count > 0)
            {
                var spawnPoint = spawnPoints[_currentSpawnPoint];
                _currentSpawnPoint = (_currentSpawnPoint + 1) % spawnPoints.Count;
                newPlayer = UnityProxy.Instantiate(_playerPrefab, spawnPoint.position, spawnPoint.rotation, unityScene);
            }
            else
            {
                _playerPrefab.transform.GetPositionAndRotation(out var position, out var rotation);
                newPlayer = UnityProxy.Instantiate(_playerPrefab, position, rotation, unityScene);
            }

            _prefabInstantiatedProvider?.OnPrefabInstantiated(newPlayer, player, scene);

            if (newPlayer.TryGetComponent(out NetworkIdentity identity))
                identity.GiveOwnership(player);
        }

        private static bool PlayerOwnsIdentityInScene(PlayerID player, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var identities = roots[i].GetComponentsInChildren<NetworkIdentity>(true);
                for (int j = 0; j < identities.Length; j++)
                {
                    var identity = identities[j];
                    if (IdentityHasOwner(identity, player))
                        return true;
                }
            }

            return false;
        }

        private static bool PlayerOwnsIdentityAnywhere(PlayerID player)
        {
            var identities = UnityEngine.Object.FindObjectsByType<NetworkIdentity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < identities.Length; i++)
            {
                if (IdentityHasOwner(identities[i], player))
                    return true;
            }

            return false;
        }

        private bool HasEnoughExistingPrefabInstances(NetworkManager manager, SceneID scene)
        {
            if (!_playerPrefab || manager.prefabProvider == null)
                return false;

            if (!manager.prefabProvider.TryGetPrefabData(_playerPrefab, out var prefabData))
                return false;

            if (!manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                return false;

            if (!scenePlayersModule.TryGetPlayersInScene(scene, out var players))
                return false;

            int expected = players.Count;
            if (expected <= 0)
                return false;

            int count = 0;
            var identities = UnityEngine.Object.FindObjectsByType<NetworkIdentity>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < identities.Length; i++)
            {
                var identity = identities[i];
                if (!identity || !identity.isSpawned)
                    continue;

                if (identity.prefabId == prefabData.prefabId && identity.componentIndex == 0)
                    count++;
            }

            return count >= expected;
        }

        private static bool IdentityHasOwner(NetworkIdentity identity, PlayerID player)
        {
            if (!identity)
                return false;

            var serverOwner = identity.GetOwner(true);
            if (serverOwner.HasValue && serverOwner.Value == player)
                return true;

            var clientOwner = identity.GetOwner(false);
            return clientOwner.HasValue && clientOwner.Value == player;
        }
    }
}

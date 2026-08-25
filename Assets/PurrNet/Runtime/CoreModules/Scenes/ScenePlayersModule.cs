using System;
using System.Collections.Generic;
using PurrNet.Logging;

namespace PurrNet.Modules
{
    internal struct ClientFinishedLoadingScene
    {
        public SceneID scene;
    }

    internal struct ClientFinishedRebindingScene
    {
        public SceneID scene;
        public string hostMigrationSessionId;
        public uint hostMigrationEpoch;

        public HostMigrationTransitionOptions hostMigrationTransition =>
            new HostMigrationTransitionOptions(hostMigrationSessionId, hostMigrationEpoch);
    }

    public delegate void OnPlayerSceneEvent(PlayerID player, SceneID scene, bool asServer);

    public class ScenePlayersModule : INetworkModule, IPromoteToServerModule, ITransferToNewServer
    {
        private readonly Dictionary<SceneID, List<PlayerID>> _scenePlayers = new ();
        private readonly Dictionary<SceneID, List<PlayerID>> _sceneLoadedPlayers = new ();
        private readonly HashSet<SceneID> _pendingClientReboundScenes = new ();
        private readonly HashSet<SceneID> _acknowledgedClientReboundScenes = new ();

        readonly ScenesModule _scenes;
        readonly PlayersManager _players;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPostPlayerLoadedScene;

        /// <summary>
        /// Called when an exact host-migration peer confirms that its already-loaded scene was
        /// rebound to this authority. Ordinary player-loaded callbacks are not replayed.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerReboundScene;

        internal event OnPlayerSceneEvent onPrePlayerSceneReboundInternal;
        internal event OnPlayerSceneEvent onPlayerSceneReboundInternal;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        private bool _asServer;

        private readonly NetworkManager _manager;

        public ScenePlayersModule(NetworkManager manager, ScenesModule scenes, PlayersManager players)
        {
            _manager = manager;
            _scenes = scenes;
            _players = players;
        }

        public void PromoteToServerModule()
        {
            var retainedLocalScenes = new List<SceneID>();
            foreach (var pair in _scenes.sceneStates)
            {
                var state = pair.Value;
                if (state.scene.IsValid() && state.scene.isLoaded)
                    retainedLocalScenes.Add(pair.Key);
            }

            var retainedLocalPlayer =
                _players.promotedLocalPlayerId ?? _players.localPlayerId;
            Disable(false);
            _asServer = true;
            Enable(true);

            if (retainedLocalPlayer.HasValue)
            {
                RestorePromotedLocalSceneMembership(
                    retainedLocalPlayer.Value, retainedLocalScenes, _scenePlayers);
            }
        }

        public void PostPromoteToServerModule()
        {
        }

        public void TransferToNewServer()
        {
            _pendingClientReboundScenes.Clear();
            _acknowledgedClientReboundScenes.Clear();
        }

        internal bool ValidateExactPromotionSceneMembership(
            HostMigrationTransitionOptions transition, out string failure)
        {
            failure = null;
            return true;
        }

        internal static void RestorePromotedLocalSceneMembership(
            PlayerID localPlayer, IReadOnlyList<SceneID> retainedLoadedScenes,
            IDictionary<SceneID, List<PlayerID>> scenePlayers)
        {
            if (retainedLoadedScenes == null || scenePlayers == null)
                return;

            for (var i = 0; i < retainedLoadedScenes.Count; i++)
            {
                if (!scenePlayers.TryGetValue(retainedLoadedScenes[i], out var players) ||
                    players.Contains(localPlayer))
                    continue;

                players.Add(localPlayer);
            }
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (asServer)
            {
                var scenes = _scenes.sceneStates;

                foreach (var (id, sceneState) in scenes)
                {
                    if (sceneState.scene.isLoaded)
                        OnSceneLoaded(id, true);
                }

                _scenes.onSceneLoaded += OnSceneLoaded;
                _scenes.onSceneUnloaded += OnSceneUnloaded;
                _scenes.onSceneRegistrationRemoved += OnSceneRegistrationRemoved;
                _scenes.onSceneVisibilityChanged += OnSceneVisibilityChanged;
                _players.onPlayerJoined += OnPlayerJoined;
                _players.onHostMigrationConnectionRebound += OnPlayerJoined;
                _players.onPlayerLeft += OnPlayerLeft;

                _players.Subscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
                _players.Subscribe<ClientFinishedRebindingScene>(RemoteClientReboundScene);
            }
            else
            {
                if (_players.localPlayerId.HasValue)
                    OnLocalPlayerReady(_players.localPlayerId.Value);
                else _players.onLocalPlayerReceivedID += OnLocalPlayerReady;

                _scenes.onSceneLoaded += OnClientSceneLoaded;
                _scenes.onRetainedSceneRebound += OnClientSceneRebound;
                _scenes.onSceneUnloaded += OnClientSceneUnloaded;
                _scenes.onSceneRegistrationRemoved += OnSceneRegistrationRemoved;
            }
        }

        private void OnLocalPlayerReady(PlayerID player)
        {
            var scenes = _scenes.sceneStates;
            var exactReconciliation =
                _manager.expectedHostMigrationSession.canReconcile &&
                _manager.isHostMigrationSessionValidated;

            foreach (var (id, sceneState) in scenes)
            {
                if (sceneState.scene.isLoaded)
                {
                    if (exactReconciliation)
                    {
                        if (_pendingClientReboundScenes.Contains(id))
                            OnClientSceneRebound(id, _asServer);
                    }
                    else
                        OnClientSceneLoaded(id, _asServer);
                }
            }

            _pendingClientReboundScenes.Clear();
            _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _scenes.onSceneLoaded -= OnSceneLoaded;
                _scenes.onSceneUnloaded -= OnSceneUnloaded;
                _scenes.onSceneRegistrationRemoved -= OnSceneRegistrationRemoved;
                _scenes.onSceneVisibilityChanged -= OnSceneVisibilityChanged;
                _players.onPlayerJoined -= OnPlayerJoined;
                _players.onHostMigrationConnectionRebound -= OnPlayerJoined;
                _players.onPlayerLeft -= OnPlayerLeft;

                _players.Unsubscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
                _players.Unsubscribe<ClientFinishedRebindingScene>(RemoteClientReboundScene);
            }
            else
            {
                _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
                _scenes.onSceneLoaded -= OnClientSceneLoaded;
                _scenes.onRetainedSceneRebound -= OnClientSceneRebound;
                _scenes.onSceneUnloaded -= OnClientSceneUnloaded;
                _scenes.onSceneRegistrationRemoved -= OnSceneRegistrationRemoved;
            }
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!_manager.networkRules.ShouldRemovePlayerFromSceneOnLeave())
            {
                foreach (var (scene, players) in _sceneLoadedPlayers)
                {
                    if (players.Remove(player))
                        onPlayerUnloadedScene?.Invoke(player, scene, _asServer);
                }

                return;
            }

            foreach (var (scene, players) in _scenePlayers)
            {
                if (!players.Contains(player))
                    continue;

                RemovePlayerFromScene(player, scene);
            }
        }

        private void OnClientSceneLoaded(SceneID scene, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPrePlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPostPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);

            _players.SendToServer(new ClientFinishedLoadingScene { scene = scene });
        }

        private void OnClientSceneRebound(SceneID scene, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
            {
                _pendingClientReboundScenes.Add(scene);
                return;
            }

            var transition = _manager.expectedHostMigrationSession;
            if (!transition.canReconcile || !_manager.isHostMigrationSessionValidated)
            {
                PurrLogger.LogError(
                    $"Cannot acknowledge retained SceneID {scene}: the new authority did not " +
                    "advertise a valid host-migration session.");
                return;
            }

            if (!_acknowledgedClientReboundScenes.Add(scene))
                return;

            _pendingClientReboundScenes.Remove(scene);

            _players.SendToServer(new ClientFinishedRebindingScene
            {
                scene = scene,
                hostMigrationSessionId = transition.sessionId,
                hostMigrationEpoch = transition.epoch
            });
        }

        private void OnClientSceneUnloaded(SceneID scene, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPlayerLeftScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPlayerUnloadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
        }

        private void RemoteClientLoadedScene(PlayerID player, ClientFinishedLoadingScene data, bool asServer)
        {
            if (!_scenePlayers.TryGetValue(data.scene, out var playersInScene))
                return;

            if (!playersInScene.Contains(player))
                return;

            if (_sceneLoadedPlayers.TryGetValue(data.scene, out var loadedPlayers))
            {
                if (loadedPlayers.Contains(player))
                {
                    TryReacknowledgeLoadedExactScene(
                        player, data.scene, _manager.hostMigrationSession,
                        ExactSceneAcknowledgementKind.Loaded);
                    return;
                }

                loadedPlayers.Add(player);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{data.scene}' not found in scene loaded players dictionary");
                return;
            }

            if (TryDeferExactSceneCallbacks(
                    player, data.scene, _manager.hostMigrationSession,
                    ExactSceneAcknowledgementKind.Loaded))
                return;

            onPrePlayerLoadedScene?.Invoke(player, data.scene, asServer);
            onPlayerLoadedScene?.Invoke(player, data.scene, asServer);
            onPostPlayerLoadedScene?.Invoke(player, data.scene, asServer);
        }

        private void RemoteClientReboundScene(
            PlayerID player, ClientFinishedRebindingScene data, bool asServer)
        {
            if (!_scenePlayers.TryGetValue(data.scene, out var playersInScene) ||
                !playersInScene.Contains(player))
                return;

            var transition = data.hostMigrationTransition;
            if (!_players.IsActiveRetainedHostMigrationPlayer(player, transition))
            {
                PurrLogger.LogWarning(
                    $"Ignoring retained-scene acknowledgement from {player} for SceneID " +
                    $"{data.scene}: migration marker {transition} is not active for that player.");
                return;
            }

            if (!_sceneLoadedPlayers.TryGetValue(data.scene, out var loadedPlayers))
            {
                PurrLogger.LogError($"SceneID '{data.scene}' not found in scene loaded players dictionary");
                return;
            }

            if (loadedPlayers.Contains(player))
            {
                TryReacknowledgeLoadedExactScene(
                    player, data.scene, transition,
                    ExactSceneAcknowledgementKind.Rebound);
                return;
            }

            loadedPlayers.Add(player);
            if (TryDeferExactSceneCallbacks(
                    player, data.scene, transition,
                    ExactSceneAcknowledgementKind.Rebound))
                return;

            TriggerPlayerSceneRebound(player, data.scene, asServer);
        }

        private bool TryReacknowledgeLoadedExactScene(PlayerID player, SceneID scene,
            HostMigrationTransitionOptions transition, ExactSceneAcknowledgementKind kind)
        {
            if (!_players.IsPendingRetainedHostMigrationPlayer(player, transition) ||
                !_manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
                !factory.IsAwaitingExactSceneAcknowledgement(player, scene, transition))
                return false;

            return TryDeferExactSceneCallbacks(player, scene, transition, kind);
        }

        private bool TryDeferExactSceneCallbacks(PlayerID player, SceneID scene,
            HostMigrationTransitionOptions transition, ExactSceneAcknowledgementKind kind)
        {
            if (!_players.IsPendingRetainedHostMigrationPlayer(player, transition))
                return false;

            string failure = null;
            if (!_manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
                !factory.TryRecordExactSceneAcknowledgement(
                    player, scene, transition, kind, out failure))
            {
                PurrLogger.LogError(
                    $"Exact scene acknowledgement for {player}, SceneID {scene}, {transition} " +
                    $"failed closed: {failure ?? "the server hierarchy factory is unavailable"}.");
            }

            return true;
        }

        internal void ReplayExactSceneCallbacks(PlayerID player,
            IReadOnlyList<SceneID> orderedScenes,
            IReadOnlyDictionary<SceneID, ExactSceneAcknowledgementKind> acknowledgements,
            bool asServer)
        {
            for (var i = 0; i < orderedScenes.Count; i++)
            {
                var scene = orderedScenes[i];
                if (!acknowledgements.TryGetValue(scene, out var kind))
                    continue;

                if (kind == ExactSceneAcknowledgementKind.Loaded)
                {
                    onPlayerLoadedScene?.Invoke(player, scene, asServer);
                    onPostPlayerLoadedScene?.Invoke(player, scene, asServer);
                }
                else
                {
                    onPlayerSceneReboundInternal?.Invoke(player, scene, asServer);
                    InvokePublicPlayerSceneRebound(player, scene, asServer);
                }
            }
        }

        internal void TriggerPlayerSceneRebound(PlayerID player, SceneID scene, bool asServer)
        {
            onPrePlayerSceneReboundInternal?.Invoke(player, scene, asServer);
            onPlayerSceneReboundInternal?.Invoke(player, scene, asServer);
            InvokePublicPlayerSceneRebound(player, scene, asServer);
        }

        private void InvokePublicPlayerSceneRebound(PlayerID player, SceneID scene, bool asServer)
        {
            if (onPlayerReboundScene == null)
                return;

            var callbacks = onPlayerReboundScene.GetInvocationList();
            for (var i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    ((OnPlayerSceneEvent)callbacks[i]).Invoke(player, scene, asServer);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }
        }

        /// <summary>
        /// Notify bot has loaded in a scene (matches RemoteClientLoadedScene behavior)
        /// </summary>
        private void NotifyBotSceneLoaded(PlayerID bot, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("NotifyBotSceneLoaded can only be called on server");
                return;
            }

            if (_sceneLoadedPlayers.TryGetValue(scene, out var loadedPlayers))
            {
                if (loadedPlayers.Contains(bot))
                    return;

                loadedPlayers.Add(bot);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scene loaded players dictionary");
                return;
            }

            onPrePlayerLoadedScene?.Invoke(bot, scene, _asServer);
            onPlayerLoadedScene?.Invoke(bot, scene, _asServer);
            onPostPlayerLoadedScene?.Invoke(bot, scene, _asServer);
        }

        /// <summary>
        /// Get all players that are both part of the scene and have finished loading the scene
        /// </summary>
        public bool TryGetPlayersInScene(SceneID scene, out IReadOnlyList<PlayerID> players)
        {
            if (_sceneLoadedPlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        /// <summary>
        /// Get all players attached to a scene, regardless of whether they have finished loading the scene or not
        /// </summary>
        public bool TryGetPlayersAttachedToScene(SceneID scene, out IReadOnlyList<PlayerID> players)
        {
            if (_scenePlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        private void OnSceneVisibilityChanged(SceneID scene, bool isPublic, bool asServer)
        {
            if (!isPublic) return;

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
                return;

            // if the scene is public, add all connected players to the scene
            int connectedPlayersCount = _players.players.Count;

            for (int i = 0; i < connectedPlayersCount; i++)
            {
                var player = _players.players[i];

                if (!playersInScene.Contains(player))
                    playersInScene.Add(player);

                onPlayerJoinedScene?.Invoke(player, scene, asServer);

                if(player.isBot)
                    NotifyBotSceneLoaded(player, scene);
            }
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            for (var i = 0; i < _scenes.scenes.Count; i++)
            {
                var scene = _scenes.scenes[i];

                if (!_scenes.TryGetSceneState(scene, out var state))
                    continue;

                if (!state.settings.isPublic)
                    continue;

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                AddPlayerToScene(player, scene);
            }
        }

        public bool IsPlayerLoadedInScene(PlayerID player, SceneID scene)
        {
            return _sceneLoadedPlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public bool IsPlayerInScene(PlayerID player, SceneID scene)
        {
            return _scenePlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public IEnumerator<SceneID> GetPlayerScenes(PlayerID player)
        {
            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(player))
                    yield return scene;
            }
        }

        internal bool TryValidateExactPlayerSceneSet(
            PlayerID player,
            IReadOnlyList<SceneID> expectedScenes,
            out string failure)
        {
            failure = null;
            if (expectedScenes == null || expectedScenes.Count == 0)
            {
                failure = $"player {player}'s exact scene set is empty";
                return false;
            }

            var remaining = new HashSet<SceneID>();
            for (var i = 0; i < expectedScenes.Count; i++)
            {
                if (!remaining.Add(expectedScenes[i]))
                {
                    failure = $"SceneID {expectedScenes[i]} appears more than once in player " +
                              $"{player}'s exact scene set";
                    return false;
                }
            }

            var currentCount = 0;
            foreach (var pair in _scenePlayers)
            {
                if (!pair.Value.Contains(player))
                    continue;

                currentCount++;
                if (!remaining.Remove(pair.Key))
                {
                    failure = $"player {player} joined unexpected SceneID {pair.Key} after its " +
                              "exact scene manifest was captured";
                    return false;
                }
            }

            if (currentCount != expectedScenes.Count || remaining.Count != 0)
            {
                failure = $"player {player}'s authoritative scene membership changed after its " +
                          "exact scene manifest was captured";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Remove the player from all scenes and add them to the new scene
        /// </summary>
        public void MovePlayerToSingleScene(PlayerID player, SceneID scene)
        {
            if (_scenePlayers.TryGetValue(scene, out var playersInScene) && !playersInScene.Contains(player))
                AddPlayerToScene(player, scene);

            foreach (var (existingScene, players) in _scenePlayers)
            {
                if (scene == existingScene)
                    continue;

                if (!players.Contains(player))
                    continue;

                RemovePlayerFromScene(player, existingScene);
            }
        }

        public void AddPlayerToScene(PlayerID player, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("AddPlayerToScene can only be called on the server; for now ;)");
                return;
            }

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module; aborting AddPlayerToScene");
                return;
            }

            if (playersInScene.Contains(player))
            {
                if (!IsPlayerLoadedInScene(player, scene))
                {
                    if (CanRestoreLoadedStateWithoutSceneAction(scene))
                        MarkPlayerLoadedInScene(player, scene);
                }

                return;
            }

            playersInScene.Add(player);
            onPlayerJoinedScene?.Invoke(player, scene, _asServer);
            if(player.isBot)
                NotifyBotSceneLoaded(player, scene);
        }

        private bool CanRestoreLoadedStateWithoutSceneAction(SceneID scene)
        {
            if (!_scenes.TryGetSceneState(scene, out var state))
                return false;

            if (!state.settings.isPublic)
                return false;

            if (!state.scene.IsValid() || !state.scene.isLoaded)
                return false;

            if (_manager.gameObject.scene.handle == state.scene.handle)
                return true;

            var originalScene = _manager.originalScene;
            return originalScene.IsValid() && originalScene.handle == state.scene.handle;
        }

        private void MarkPlayerLoadedInScene(PlayerID player, SceneID scene)
        {
            var transition = _manager.hostMigrationSession;
            if (_players.IsPendingRetainedHostMigrationPlayer(player, transition))
            {
                return;
            }

            if (!_sceneLoadedPlayers.TryGetValue(scene, out var loadedPlayers))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scene loaded players dictionary");
                return;
            }

            if (loadedPlayers.Contains(player))
                return;

            loadedPlayers.Add(player);
            onPrePlayerLoadedScene?.Invoke(player, scene, _asServer);
            onPlayerLoadedScene?.Invoke(player, scene, _asServer);
            onPostPlayerLoadedScene?.Invoke(player, scene, _asServer);
        }

        public bool TryGetScenesForPlayer(PlayerID playerId, out SceneID[] scenes)
        {
            var playerScenes = new List<SceneID>();

            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(playerId))
                    playerScenes.Add(scene);
            }

            if (playerScenes.Count > 0)
            {
                scenes = playerScenes.ToArray();
                return true;
            }

            scenes = null;
            return false;
        }

        public void RemovePlayerFromScene(PlayerID player, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("RemovePlayerFromScene can only be called on the server; for now ;)");
                return;
            }

            RemovePlayerFromLoadedScene(player, scene);

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module; aborting RemovePlayerFromScene");
                return;
            }

            if (playersInScene.Remove(player))
                onPlayerLeftScene?.Invoke(player, scene, _asServer);
        }

        private void RemovePlayerFromLoadedScene(PlayerID player, SceneID scene)
        {
            if (!_sceneLoadedPlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError(
                    $"SceneID '{scene}' not found in scene loaded players dictionary; aborting RemovePlayerFromLoadedScene");
                return;
            }

            if (playersInScene.Remove(player))
                onPlayerUnloadedScene?.Invoke(player, scene, _asServer);
        }

        private void OnSceneLoaded(SceneID scene, bool asServer)
        {
            if (!_scenes.TryGetSceneState(scene, out var state))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module");
                return;
            }

            _scenePlayers.Add(scene, new List<PlayerID>());
            _sceneLoadedPlayers.Add(scene, new List<PlayerID>());

            OnSceneVisibilityChanged(scene, state.settings.isPublic, asServer);
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            if (_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                // remove all players from the scene
                foreach (var player in playersInScene)
                {
                    onPlayerLeftScene?.Invoke(player, scene, asServer);
                    onPlayerUnloadedScene?.Invoke(player, scene, asServer);
                }

                _scenePlayers.Remove(scene);
                _sceneLoadedPlayers.Remove(scene);
            }
        }

        private void OnSceneRegistrationRemoved(SceneID scene, bool asServer)
        {
            _scenePlayers.Remove(scene);
            _sceneLoadedPlayers.Remove(scene);
            _pendingClientReboundScenes.Remove(scene);
            _acknowledgedClientReboundScenes.Remove(scene);
        }
    }
}

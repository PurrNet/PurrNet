using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public struct PendingSceneOperation
    {
        public int buildIndex;
        public SceneID idToAssign;
        public PurrSceneSettings settings;
        [UsedImplicitly]
        public AsyncOperation operation;
    }

    public struct SceneState
    {
        /// <summary>
        /// The unity scene object this ID is associated with
        /// </summary>
        public Scene scene;

        /// <summary>
        /// The network settings for this scene
        /// </summary>
        public PurrSceneSettings settings;

        public SceneState(Scene scene, PurrSceneSettings settings)
        {
            this.scene = scene;
            this.settings = settings;
        }
    }

    public struct PurrSceneSettings
    {
        public LoadSceneMode mode;
        public LocalPhysicsMode physicsMode;
        public bool isPublic;
    }

    public delegate void OnSceneActionEvent(SceneID scene, bool asServer);

    public delegate void OnSceneVisibilityEvent(SceneID scene, bool isVisible, bool asServer);

    public class ScenesModule : INetworkModule, IFixedUpdate, ICleanup, IConnectionStateListener, ITransferToNewServer, IPromoteToServerModule
    {
        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _players;

        private readonly SceneHistory _history;
        private bool _asServer;

        private readonly List<PendingSceneOperation> _pendingOperations = new List<PendingSceneOperation>();
        private readonly Queue<SceneAction> _actionsQueue = new Queue<SceneAction>();

        private readonly Dictionary<SceneID, SceneState> _scenes = new Dictionary<SceneID, SceneState>();
        private readonly Dictionary<Scene, SceneID> _idToScene = new Dictionary<Scene, SceneID>();
        private readonly List<SceneID> _rawScenes = new List<SceneID>();

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneLoaded;

        /// <summary>
        /// Callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for after onSceneLoaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneLoaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Callback for when a scene's visibility changes
        /// </summary>
        public event OnSceneVisibilityEvent onSceneVisibilityChanged;

        private ushort _nextSceneID = 1;
        private ScenePlayersModule _scenePlayers;

        public IReadOnlyList<SceneID> scenes => _rawScenes;
        public IReadOnlyDictionary<SceneID, SceneState> sceneStates => _scenes;

        private SceneID GetNextID() => new(_nextSceneID++);

        public ScenesModule(NetworkManager manager, PlayersManager players)
        {
            _networkManager = manager;
            _players = players;
            _history = new SceneHistory();
        }

        internal void SetScenePlayers(ScenePlayersModule scenePlayersModule)
        {
            _scenePlayers = scenePlayersModule;
        }

        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            return _scenes.TryGetValue(sceneID, out state);
        }

        private void AddScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (_scenes.TryGetValue(id, out var state))
            {
                PurrLogger.LogError($"Scene with ID {id} already exists under {state.scene.name}");
                return;
            }

            _scenes.Add(id, new SceneState(scene, settings));
            _idToScene.Add(scene, id);
            _rawScenes.Add(id);

            PlayLoadEventsForScene(id);
        }

        private void PlayLoadEventsForScene(SceneID id)
        {
            onPreSceneLoaded?.Invoke(id, _asServer);
            onSceneLoaded?.Invoke(id, _asServer);
            onPostSceneLoaded?.Invoke(id, _asServer);
        }

        /// <summary>
        /// Used to modify whether the given scene is public or not
        /// </summary>
        /// <param name="scene">The SceneID of the scene to modify</param>
        /// <param name="isPublic">Whether the given scene should be public</param>
        public void UpdateSceneVisibility(SceneID scene, bool isPublic)
        {
            if (_asServer)
            {
                PurrLogger.LogError("Only clients can change scene visibility; for now at least ;)");
                return;
            }

            if (!_scenes.TryGetValue(scene, out var state))
            {
                PurrLogger.LogError($"Scene with ID {scene} not found");
                return;
            }

            if (!isPublic)
            {
                var rules = _networkManager.networkRules;
                if (rules && rules.ShouldForceSceneToAlwaysPublic())
                {
                    PurrLogger.LogWarning("Host migration is enabled, so scenes cannot be set to private");
                    isPublic = true;
                }
            }

            state.settings.isPublic = isPublic;
            _scenes[scene] = state;

            onSceneVisibilityChanged?.Invoke(scene, isPublic, _asServer);
        }

        private readonly List<SceneID> _scenesToTriggerUnloadEvent = new List<SceneID>();

        private void RemoveScene(Scene scene)
        {
            if (!_idToScene.TryGetValue(scene, out var id))
                return;

            _scenes.Remove(id);
            _idToScene.Remove(scene);
            _rawScenes.Remove(id);
            _scenesToTriggerUnloadEvent.Add(id);
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (state != ConnectionState.Connected)
                return;

            if (!_wasSetup)
                Setup(asServer);
        }

        private bool _wasSetup;

        static GameObject _dontDestroyOnLoad;

        private static Scene GetDontDestroyOnLoadScene()
        {
            if (_dontDestroyOnLoad)
                return _dontDestroyOnLoad.scene;
            _dontDestroyOnLoad = new GameObject("PurrNet:DontDestroyOnLoad")
            {
                hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
            };
            Object.DontDestroyOnLoad(_dontDestroyOnLoad);
            return _dontDestroyOnLoad.scene;
        }

        static bool IsDontDestroyOnLoadScene(Scene scene)
        {
            return scene.name is "DontDestroyOnLoad";
        }


        public void PromoteToServerModule()
        {
            _asServer = true;
            _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
            _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            _players.onPrePlayerJoined += OnPlayerJoined;
            _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
            _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
        }

        private bool _isTransferingToNewServer;

        public void PostPromoteToServerModule()
        {

        }

        public void TransferToNewServer()
        {
            _isTransferingToNewServer = true;
        }

        private void Setup(bool asServer)
        {
            _wasSetup = true;
            _asServer = asServer;

            var currentScene = _networkManager.gameObject.scene;
            var originalScene = _networkManager.originalScene;

            var hasDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(currentScene) ||
                                          IsDontDestroyOnLoadScene(originalScene);

            AddScene(currentScene, new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,
                isPublic = true,
                physicsMode = LocalPhysicsMode.None
            }, GetNextID());

            if (currentScene != originalScene && originalScene.IsValid())
            {
                AddScene(originalScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            var rules = _networkManager.networkRules;

            if (!hasDontDestroyOnLoadScene && rules && rules.ShouldAlwaysIncludeDontDestroyOnLoadScene())
            {
                var dontDestroyScene = GetDontDestroyOnLoadScene();
                AddScene(dontDestroyScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            if (!asServer)
            {
                _players.Subscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Subscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined += OnPlayerJoined;
                _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
            }

            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        }

        public void Enable(bool asServer)
        {
            // Setup(asServer);
        }

        public void Disable(bool asServer)
        {
            if (!asServer)
            {
                _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined -= OnPlayerJoined;
                _scenePlayers.onPlayerJoinedScene -= OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene -= OnPlayerLeftScene;
            }

            SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!asServer)
                return;

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            for (var i = 0; i < history.actions.Count; i++)
            {
                var action = history.actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (_scenePlayers.IsPlayerInScene(player, target))
                    _playerFilteredActions.Add(action);
            }

            _players.Send(player, new FirstSceneActionsBatch { actions = _playerFilteredActions });
        }

        private void OnPlayerLeftScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            bool isSceneStillValid = _scenes.TryGetValue(scene, out var state) && state.scene.IsValid();

            if (!isSceneStillValid)
                return;

            _playerFilteredActions.Clear();
            _playerFilteredActions.Add(new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction
                {
                    sceneID = scene,
                    options = UnloadSceneOptions.None
                }
            });

            _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        private void OnPlayerJoinedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            // send all actions for the scene
            FilterActionsForPlayerBySceneID(player, scene, history.actions, _playerFilteredActions);

            if (_playerFilteredActions.Count > 0)
                _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        /// <summary>
        /// Returns the pending operations for this module.
        /// This allows you to check if a scene is still loading or unloading and the progress of the operation.
        /// </summary>
        /// <returns>List of pending operations</returns>
        public IReadOnlyList<PendingSceneOperation> GetPendingOperations()
        {
            return _pendingOperations;
        }

        private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];

                if (operation.buildIndex == scene.buildIndex && operation.settings.mode == mode)
                {
                    AddScene(scene, operation.settings, operation.idToAssign);
                    _pendingOperations.RemoveAt(i);
                    break;
                }
            }
        }

        private bool IsScenePending(SceneID sceneId)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                if (_pendingOperations[i].idToAssign == sceneId)
                    return true;
            }

            return false;
        }

        int GetCurrentLoadedScenes()
        {
            int count = 0;

            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (IsDontDestroyOnLoadScene(state.scene))
                        continue;

                    if (state.scene.isLoaded)
                        count++;
                }
            }

            return count;
        }

        private void HandleNextSceneAction()
        {
            if (_actionsQueue.Count == 0) return;

            var action = _actionsQueue.Peek();
            switch (action.type)
            {
                case SceneActionType.Load:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var loadAction = action.loadSceneAction;
                        AsyncOperation operation;

                        try
                        {
                            operation = SceneManager.LoadSceneAsync(loadAction.buildIndex, loadAction.GetLoadSceneParameters());
                        }
                        catch (System.Exception e)
                        {
                            PurrLogger.LogError($"Error loading scene: {e}");
                            break;
                        }

                        if (loadAction.parameters.mode == LoadSceneMode.Single)
                        {
                            for (int i = 0; i < _rawScenes.Count; i++)
                            {
                                if (!IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene))
                                    RemoveScene(_scenes[_rawScenes[i]].scene);
                            }
                        }

                        _pendingOperations.Add(new PendingSceneOperation
                        {
                            buildIndex = loadAction.buildIndex,
                            settings = loadAction.parameters,
                            idToAssign = loadAction.sceneID,
                            operation = operation
                        });

                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.Unload:
                    {
                        var currentlyLoadedCount = GetCurrentLoadedScenes();
                        if (currentlyLoadedCount == 1)
                        {
                            // wait for the next load action
                            break;
                        }

                        var idx = action.unloadSceneAction.sceneID;

                        if (_networkManager.isHost && !_asServer)
                        {
                            _scenesToTriggerUnloadEvent.Add(idx);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        // if the scene is pending, don't do anything for now
                        if (IsScenePending(idx)) break;

                        if (!_scenes.TryGetValue(idx, out var sceneState))
                        {
                            PurrLogger.LogError($"Couldn't find scene with index {idx} to unload");
                            break;
                        }

                        SceneManager.UnloadSceneAsync(sceneState.scene, action.unloadSceneAction.options);
                        RemoveScene(sceneState.scene);
                        _actionsQueue.Dequeue();
                        break;
                    }
            }
        }

        private void OnSceneActionsBatch(PlayerID player, FirstSceneActionsBatch data, bool asServer)
        {
            if (!_isTransferingToNewServer)
            {
                HandleScenes(data.actions);
                return;
            }

            _isTransferingToNewServer = false;

            // TODO: reconcile with current scenes, data.actions is the correct target

            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var scene = _rawScenes[i];
                PlayLoadEventsForScene(scene);
            }
        }

        private void OnSceneActionsBatch(PlayerID player, SceneActionsBatch data, bool asServer)
        {
            HandleScenes(data.actions);
        }

        private void HandleScenes(List<SceneAction> actions)
        {
            if (_networkManager.isServer || _asServer)
            {
                var serverModule = _networkManager.GetModule<ScenesModule>(true);
                for (var i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];

                    switch (action.type)
                    {
                        case SceneActionType.Load:
                        {
                            if (_scenes.ContainsKey(action.loadSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.loadSceneAction.sceneID, out var state))
                                AddScene(state.scene, state.settings, action.loadSceneAction.sceneID);
                            break;
                        }
                        case SceneActionType.Unload:
                        {
                            if (!_scenes.ContainsKey(action.unloadSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.unloadSceneAction.sceneID, out var state))
                                RemoveScene(state.scene);
                            break;
                        }

                        case SceneActionType.SetActive:
                        default:
                            break;
                    }
                }

                return;
            }

            for (var i = 0; i < actions.Count; i++)
                _actionsQueue.Enqueue(actions[i]);

            HandleNextSceneAction();
        }

        private static int SceneNameToBuildIndex(string name)
        {
            var bIdxCount = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < bIdxCount; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                if (sceneName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(sceneIndex, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        public AsyncOperation LoadSceneAsync(string sceneName, PurrSceneSettings settings)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, settings);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneParameters parameters)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            return LoadSceneAsync(sceneIndex, new PurrSceneSettings
            {
                mode = parameters.loadSceneMode,
                physicsMode = parameters.localPhysicsMode,
                isPublic = true
            });
        }

        public SceneID lastSceneId => new((ushort)(_nextSceneID - 1));

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            HostMigrationCompatibility(ref settings);

            var idToAssign = GetNextID();
            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);

            if (settings.mode == LoadSceneMode.Single)
            {
                if (TryGetSceneID(_networkManager.gameObject.scene, out var nmId) &&
                    TryGetSceneState(nmId, out var nmScene))
                {
                    if (!IsDontDestroyOnLoadScene(nmScene.scene))
                    {
                        PurrLogger.LogError("Network manager scene is not DontDestroyOnLoad and you are trying to" +
                                            " load a new scene with LoadSceneMode.Single");
                    }
                }

                for (int i = 0; i < _rawScenes.Count; i++)
                {
                    bool isDontDestroyOnLoad = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoad)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            _history.AddLoadAction(new LoadSceneAction
            {
                buildIndex = sceneIndex,
                sceneID = idToAssign,
                parameters = settings
            });

            var op = SceneManager.LoadSceneAsync(sceneIndex, parameters);
            var operation = new PendingSceneOperation
            {
                buildIndex = sceneIndex,
                settings = settings,
                idToAssign = idToAssign,
                operation = op
            };

            _pendingOperations.Add(operation);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingOperations.Add(operation);
            }

            return op;
        }

        private void HostMigrationCompatibility(ref PurrSceneSettings settings)
        {
            if (!settings.isPublic)
            {
                var rules = _networkManager.networkRules;
                if (rules && rules.ShouldForceSceneToAlwaysPublic())
                {
                    PurrLogger.LogWarning("Host migration is enabled, so scenes cannot be set to private");
                    settings.isPublic = true;
                }
            }
        }

        /// <summary>
        /// Unloads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">Name of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with name '{sceneName}' not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(int buildIndex, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with build index {buildIndex} not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its Scene object - Must be in build settings
        /// </summary>
        /// <param name="scene">The Scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (_networkManager.gameObject.scene == scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            if (!_idToScene.TryGetValue(scene, out var sceneIndex))
            {
                PurrLogger.LogError($"Scene {scene.name} not found in scenes list");
                return null;
            }

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneIndex, options = options });
            var op = SceneManager.UnloadSceneAsync(scene, options);
            RemoveScene(scene);

            return op;
        }

        static readonly List<SceneAction> _playerFilteredActions = new List<SceneAction>();

        private void FilterActionsForPlayer(PlayerID player, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        private void FilterActionsForPlayerBySceneID(PlayerID player, SceneID id, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (target != id)
                    continue;

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        public void FixedUpdate()
        {
            HandleNextSceneAction();

            if (_history.hasUnflushedActions)
                FlushActions();

            if (_scenesToTriggerUnloadEvent.Count > 0)
            {
                for (var i = 0; i < _scenesToTriggerUnloadEvent.Count; i++)
                    onSceneUnloaded?.Invoke(_scenesToTriggerUnloadEvent[i], _asServer);
                _scenesToTriggerUnloadEvent.Clear();
            }
        }

        private void FlushActions()
        {
            var delta = _history.GetDelta();

            for (var i = 0; i < _players.players.Count; i++)
            {
                var player = _players.players[i];

                _playerFilteredActions.Clear();

                FilterActionsForPlayer(player, delta.actions, _playerFilteredActions);

                if (_playerFilteredActions.Count > 0)
                {
                    _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
                }
            }

            _history.Flush();
        }

        private readonly List<AsyncOperation> _pendingUnloads = new List<AsyncOperation>();
        private CleanupStage _cleanupStage;

        enum CleanupStage
        {
            None,
            Skip,
            LoadEmptyScene,
            WaitOneFrame,
            UnloadScenes,
            UnloadScenesOnly,
            LoadOGScene,
            LoadOGSceneOnly,
            UnloadEmptyScene,
            ResetScene,
            Done
        }

        private Scene? _emptyScene;
        private AsyncOperation _ogSceneLoad;

        public bool Cleanup()
        {
            if (!_wasSetup)
                return true;

            var rules = _networkManager.networkRules;

            if (rules && !rules.ShouldCleanupScenesOnDisconnect())
                return true;

            if (ApplicationContext.isQuitting)
                return true;

            if (!_networkManager.isOffline)
                return true;

            if (_pendingOperations.Count > 0)
                return false;

            switch (_cleanupStage)
            {
                case CleanupStage.None:
                    {
                        if (rules.SceneCleanupModeOnDisconnect() == SceneCleanupMode.All)
                        {
                            if (_networkManager.originalSceneBuildIndex == -1)
                            {
                                PurrLogger.LogError("Unable to load original scene on cleanup because its index is invalid");
                                _cleanupStage = CleanupStage.Skip;
                            }
                            else
                                _cleanupStage = CleanupStage.LoadOGSceneOnly;
                        }
                        else
                        {
                            _cleanupStage = _networkManager.IsDontDestroyOnLoad()
                                ? CleanupStage.LoadEmptyScene
                                : CleanupStage.UnloadScenesOnly;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module) && module._wasSetup)
                            module._cleanupStage = CleanupStage.Skip;

                        return false;
                    }
                case CleanupStage.Skip: return false;
                case CleanupStage.Done: return true;
                case CleanupStage.LoadEmptyScene:
                    {
                        _cleanupStage = CleanupStage.WaitOneFrame;
                        _emptyScene = SceneManager.CreateScene("EmptyScene");
                        return false;
                    }
                case CleanupStage.WaitOneFrame:
                    {
                        _cleanupStage = CleanupStage.UnloadScenes;
                        return false;
                    }
                case CleanupStage.UnloadScenes:
                    {
                        if (UnloadAllScenesCleanup(false))
                            _cleanupStage = CleanupStage.LoadOGScene;
                        return false;
                    }
                case CleanupStage.UnloadScenesOnly:
                    {
                        if (UnloadAllScenesCleanup(true))
                        {
                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;
                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGScene:
                    {
                        if (_ogSceneLoad == null)
                        {
                            if (_networkManager.originalSceneBuildIndex != -1)
                            {
                                _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex,
                                    LoadSceneMode.Additive);

                                if (_ogSceneLoad != null)
                                    _ogSceneLoad.allowSceneActivation = true;
                            }
                            else
                            {
                                _cleanupStage = CleanupStage.UnloadEmptyScene;
                            }
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _cleanupStage = CleanupStage.ResetScene;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGSceneOnly:
                    {
                        if (_ogSceneLoad == null)
                        {
                            _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex);

                            if (_ogSceneLoad != null)
                                _ogSceneLoad.allowSceneActivation = true;
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _scenes.Clear();

                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;

                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.ResetScene:
                    {
                        var activeScene = SceneManager.GetSceneByBuildIndex(_networkManager.originalSceneBuildIndex);
                        _networkManager.ResetOriginalScene(activeScene);
                        _cleanupStage = CleanupStage.UnloadEmptyScene;
                        return false;
                    }
                case CleanupStage.UnloadEmptyScene:
                    {
                        if (_emptyScene != null)
                        {
                            if (_emptyScene.Value.IsValid())
                                SceneManager.UnloadSceneAsync(_emptyScene.Value);
                            _emptyScene = null;
                            return false;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                            module._cleanupStage = CleanupStage.Done;
                        _cleanupStage = CleanupStage.Done;
                        return false;
                    }
                default: return true;
            }
        }

        private bool UnloadAllScenesCleanup(bool keepNetworkManager)
        {
            // unload all scenes that aren't the network manager scene
            if (_scenes.Count > 0)
            {
                _pendingUnloads.Clear();

                foreach (var (_, scene) in _scenes)
                {
                    var unityScene = scene.scene;

                    if (keepNetworkManager && _networkManager.gameObject.scene.handle == unityScene.handle)
                        continue;

                    if (!unityScene.IsValid())
                        continue;

                    if (!unityScene.isLoaded)
                        continue;

                    if (IsDontDestroyOnLoadScene(unityScene))
                        continue;

                    _pendingUnloads.Add(SceneManager.UnloadSceneAsync(unityScene));
                }

                _scenes.Clear();
            }

            if (_pendingUnloads.Count > 0)
            {
                for (int i = 0; i < _pendingUnloads.Count; i++)
                {
                    if (_pendingUnloads[i] != null && !_pendingUnloads[i].isDone)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="scene">Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneId)
        {
            return _idToScene.TryGetValue(scene, out sceneId);
        }


        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="buildIndex">BuildIndex of Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetScene(int buildIndex, out SceneID sceneId)
        {
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                    {
                        sceneId = _rawScenes[i];
                        return true;
                    }
                }
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Checks whether a scene is loaded on the network
        /// </summary>
        /// <param name="buildIndex">Build index of scene to check</param>
        /// <returns>Whether the scene is loaded on the network or not</returns>
        public bool IsSceneLoaded(int buildIndex)
        {
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                        return true;
                }
            }

            return false;
        }
    }
}

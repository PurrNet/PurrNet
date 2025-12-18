using System.Collections.Generic;
using PurrNet.Logging;

namespace PurrNet.Modules
{
    public abstract class SceneScopedFactory<T> : INetworkModule where T : INetworkModule
    {
        protected readonly ScenesModule scenes;

        protected readonly List<T> modules = new();
        readonly Dictionary<SceneID, T> _modules = new();

        protected bool asServer;

        protected SceneScopedFactory(ScenesModule scenes)
        {
            this.scenes = scenes;
        }

        public void Enable(bool asServer)
        {
            this.asServer = asServer;
            var allScenes = this.scenes.sceneStates;

            foreach (var (id, sceneState) in allScenes)
            {
                if (sceneState.scene.isLoaded)
                    OnPreSceneLoaded(id, asServer);
            }

            this.scenes.onPreSceneLoaded += OnPreSceneLoaded;
            this.scenes.onSceneUnloaded += OnSceneUnloaded;
        }

        public void Disable(bool asServer)
        {
            for (var i = 0; i < modules.Count; i++)
                modules[i].Disable(asServer);

            scenes.onPreSceneLoaded -= OnPreSceneLoaded;
            scenes.onSceneUnloaded -= OnSceneUnloaded;
        }

        protected abstract T CreateModule(SceneID scene, bool asServer);

        private void OnPreSceneLoaded(SceneID scene, bool asServer)
        {
            if (_modules.ContainsKey(scene))
                return;

            var module = CreateModule(scene, asServer);

            module.Enable(asServer);

            modules.Add(module);
            _modules.Add(scene, module);
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            if (!_modules.TryGetValue(scene, out var hierarchy))
            {
                PurrLogger.LogError($"Hierarchy module for scene {scene} doesn't exist; trying to remove it?");
                return;
            }

            hierarchy.Disable(asServer);

            modules.Remove(hierarchy);
            _modules.Remove(scene);
        }

        public bool TryGetModule(SceneID sceneId, out T module)
        {
            return _modules.TryGetValue(sceneId, out module);
        }
    }
}

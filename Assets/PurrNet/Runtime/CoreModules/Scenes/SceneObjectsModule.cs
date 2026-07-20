using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public static class SceneObjectsModule
    {
        public static event Action<Scene> onPreSceneLoad;

        public static event Action<Scene> onPostSceneLoad;

        private static readonly List<NetworkIdentity> _sceneIdentities = new List<NetworkIdentity>();

        public static void GetSceneIdentities(Scene scene, List<NetworkIdentity> networkIdentities, bool includeInstantiated = false)
        {
            onPreSceneLoad?.Invoke(scene);

            IList<GameObject> rootGameObjects = scene.GetRootGameObjects();

            PurrSceneInfo sceneInfo = null;

            for (var i = 0; i < rootGameObjects.Count; i++)
            {
                var rootObject = rootGameObjects[i];
                if (rootObject.TryGetComponent<PurrSceneInfo>(out var si))
                {
                    sceneInfo = si;
                    break;
                }
            }

            if (sceneInfo)
            {
                var orderedRoot = new List<GameObject>(sceneInfo.rootGameObjects);

                if (includeInstantiated)
                {
                    var existing = new HashSet<GameObject>(orderedRoot);

                    for (var i = 0; i < rootGameObjects.Count; i++)
                    {
                        var rootObject = rootGameObjects[i];
                        if (rootObject && existing.Add(rootObject))
                            orderedRoot.Add(rootObject);
                    }
                }

                rootGameObjects = orderedRoot;
            }

            for (var i = 0; i < rootGameObjects.Count; i++)
            {
                var rootObject = rootGameObjects[i];

                if (!rootObject || rootObject.scene.handle != scene.handle) continue;
                if (rootObject.TryGetComponent<PurrNetPoolRoot>(out _)) continue;

                rootObject.gameObject.GetComponentsInChildren(true, _sceneIdentities);

                if (_sceneIdentities.Count == 0) continue;

                rootObject.gameObject.MakeSureAwakeIsCalled();
                networkIdentities.AddRange(_sceneIdentities);
            }

            onPostSceneLoad?.Invoke(scene);
        }
    }
}

using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace AddressablesTest
{
    public class AddressableSceneTester : MonoBehaviour
    {
        [SerializeField] private AssetReference _sceneToLoadTo;
        private SceneID? _loadedAddressableSceneId;
        
        [PurrButton, ContextMenu("Load scene")]
        private void LoadAddressableScene()
        {
            if (!NetworkManager.main || _sceneToLoadTo == null)
                return;

            if (!NetworkManager.main.isServer)
            {
                Debug.LogError($"Only the server can load scenes. For now ;)");
                return;
            }

            var sceneSettings = new PurrSceneSettings
            {
                isPublic = true,
                mode = LoadSceneMode.Additive
            };

            NetworkManager.main.sceneModule.onSceneLoaded += OnAddressableSceneLoaded;
            NetworkManager.main.sceneModule.LoadAddressableSceneAsync(_sceneToLoadTo, sceneSettings);
        }

        private void OnAddressableSceneLoaded(SceneID sceneId, bool asServer)
        {
            if (!asServer) return;
            _loadedAddressableSceneId = sceneId;
            NetworkManager.main.sceneModule.onSceneLoaded -= OnAddressableSceneLoaded;
        }

        [PurrButton, ContextMenu("Unload scene")]
        private void UnloadAddressableScene()
        {
            if (!NetworkManager.main || _sceneToLoadTo == null || !_loadedAddressableSceneId.HasValue)
                return;

            if (!NetworkManager.main.isServer)
            {
                Debug.LogError($"Only the server can unload scenes. For now ;)");
                return;
            }
            
            NetworkManager.main.sceneModule.UnloadSceneAsync(_loadedAddressableSceneId.Value);
            _loadedAddressableSceneId = null;
        }
    }
}

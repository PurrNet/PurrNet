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

            NetworkManager.main.sceneModule.LoadAddressableSceneAsync(_sceneToLoadTo, sceneSettings);
        }

        [PurrButton, ContextMenu("Unload scene")]
        private void UnloadAddressableScene()
        {
            if (!NetworkManager.main || _sceneToLoadTo == null)
                return;

            if (!NetworkManager.main.isServer)
            {
                Debug.LogError($"Only the server can unload scenes. For now ;)");
                return;
            }

            var count = NetworkManager.main.sceneModule.UnloadAddressableSceneByGuid(_sceneToLoadTo.AssetGUID);
            if(count > 0)
                Debug.Log($"Successfully unloaded {count} scene{(count > 1 ? "s" : "")} with asset GUID: {_sceneToLoadTo.AssetGUID}");
        }
    }
}

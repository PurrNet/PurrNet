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
            Debug.LogError($"Not yet implemented!");
        }
    }
}

using System.IO;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Debug/Save Hasher In File")]
    public class SaveHasherInFile : MonoBehaviour
    {
        private void Start()
        {
            var hashes = Resources.Load<TextAsset>($"PurrHashes");
            if (hashes == null)
            {
                File.WriteAllText("hashes.txt", "its not here bro");
                return;
            }

            File.WriteAllText("hashes.txt", hashes.text);

            var hashesRuntume = Hasher.GetAllHashesAsText();
            File.WriteAllText("myhashes.txt", hashesRuntume);

            string names = "";

            var rootGameObjects = gameObject.scene.GetRootGameObjects();

            PurrSceneInfo sceneInfo = null;

            foreach (var rootObject in rootGameObjects)
            {
                if (rootObject.TryGetComponent<PurrSceneInfo>(out var si))
                {
                    sceneInfo = si;
                    break;
                }
            }

            if (!sceneInfo)
            {
                File.WriteAllText("scene.txt", "No PurrSceneInfo found");
                return;
            }

            for (var i = 0; i < sceneInfo.rootGameObjects.Count; i++)
            {
                var rootObject = sceneInfo.rootGameObjects[i];
                names += rootObject.name + "\n";
            }

            File.WriteAllText("scene.txt", names);
        }
    }
}

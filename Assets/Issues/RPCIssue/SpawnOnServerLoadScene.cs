using PurrNet;
using UnityEngine;

public class SpawnOnServerLoadScene : PurrMonoBehaviour
{
    [SerializeField] private GameObject _prefab;

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        manager.sceneModule.onSceneLoaded += OnSceneLoaded;
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (manager && manager.sceneModule != null)
            manager.sceneModule.onSceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(SceneID scene, bool asserver)
    {
        if (manager.sceneModule.TryGetSceneState(scene, out var sceneState) && sceneState.scene != gameObject.scene)
        {
            return;
        }

        if (asserver)
        {
            Debug.Log("Server loaded scene, instantiating prefab");
            UnityProxy.Instantiate(_prefab);
        }
    }
}

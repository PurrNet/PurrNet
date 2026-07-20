using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

public class SceneObjectsModuleTests
{
    private static ObjectId GetObjectId(Object obj)
    {
#if UNITY_6000_3_OR_NEWER
        return obj.GetEntityId();
#else
        return obj.GetInstanceID();
#endif
    }

    [Test]
    public void GetSceneIdentitiesIgnoresPurrNetPoolRoots()
    {
        var scene = SceneManager.CreateScene(nameof(GetSceneIdentitiesIgnoresPurrNetPoolRoots));
        var poolRoot = new GameObject("PurrNetPool-Test");
        var regularRoot = new GameObject("RegularSceneRoot");

        try
        {
            SceneManager.MoveGameObjectToScene(poolRoot, scene);
            SceneManager.MoveGameObjectToScene(regularRoot, scene);

            var markerType = typeof(SceneObjectsModule).Assembly.GetType("PurrNet.Modules.PurrNetPoolRoot");
            Assert.IsNotNull(markerType, "PurrNet pool root marker type was not found.");
            poolRoot.AddComponent(markerType);

            var pooledPiece = new GameObject("PooledNestedIdentity");
            pooledPiece.transform.SetParent(poolRoot.transform);
            pooledPiece.AddComponent<NetworkIdentity>();

            var regularIdentity = regularRoot.AddComponent<NetworkIdentity>();

            var identities = new List<NetworkIdentity>();
            SceneObjectsModule.GetSceneIdentities(scene, identities, true);

            Assert.That(identities, Does.Contain(regularIdentity));
            Assert.That(identities, Has.No.Member(pooledPiece.GetComponent<NetworkIdentity>()));
        }
        finally
        {
            if (poolRoot)
                UnityEngine.Object.DestroyImmediate(poolRoot);
            if (regularRoot)
                UnityEngine.Object.DestroyImmediate(regularRoot);
#if UNITY_EDITOR
            if (scene.IsValid())
            {
                if (Application.isPlaying)
                    SceneManager.UnloadSceneAsync(scene);
                else
                    EditorSceneManager.CloseScene(scene, true);
            }
#else
            if (scene.IsValid())
                SceneManager.UnloadSceneAsync(scene);
#endif
        }
    }

    [Test]
    public void NetworkPoolWarmupRootsAreIgnoredBySceneScan()
    {
        var markerType = typeof(SceneObjectsModule).Assembly.GetType("PurrNet.Modules.PurrNetPoolRoot");
        Assert.IsNotNull(markerType, "PurrNet pool root marker type was not found.");

        var existingMarkers = Resources.FindObjectsOfTypeAll(markerType)
            .Select(GetObjectId)
            .ToHashSet();

        var managerRoot = new GameObject("PoolWarmupManager");
        var prefab = new GameObject("PooledNestedPrefab");
        var nestedPiece = new GameObject("NestedPooledPiece");
        NetworkPrefabs prefabs = null;
        GameObject poolRoot = null;

        try
        {
            managerRoot.SetActive(false);
            var manager = managerRoot.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;

            prefabs = ScriptableObject.CreateInstance<NetworkPrefabs>();
            prefabs.autoGenerate = false;

            prefab.AddComponent<NetworkIdentity>();
            nestedPiece.transform.SetParent(prefab.transform);
            nestedPiece.AddComponent<NetworkIdentity>();

            prefabs.AddRuntimePrefab("pooled-nested-prefab", prefab, true, 1);
            manager.SetPrefabProvider(prefabs);

            var pool = NetworkPoolManager.GetPool(manager);
            Assert.IsNotNull(pool);

            var newMarkers = Resources.FindObjectsOfTypeAll(markerType)
                .OfType<Component>()
                .Where(marker => !existingMarkers.Contains(GetObjectId(marker)))
                .ToArray();

            Assert.That(newMarkers, Has.Length.EqualTo(1));
            poolRoot = newMarkers[0].gameObject;

            var pooledIdentities = poolRoot.GetComponentsInChildren<NetworkIdentity>(true);
            Assert.That(pooledIdentities, Has.Length.GreaterThanOrEqualTo(2));

            var sceneIdentities = new List<NetworkIdentity>();
            SceneObjectsModule.GetSceneIdentities(poolRoot.scene, sceneIdentities, true);

            foreach (var identity in pooledIdentities)
                Assert.That(sceneIdentities, Has.No.Member(identity));
        }
        finally
        {
            if (prefabs)
                NetworkPoolManager.RemovePool(prefabs);
            if (poolRoot)
                UnityEngine.Object.DestroyImmediate(poolRoot);
            if (managerRoot)
                UnityEngine.Object.DestroyImmediate(managerRoot);
            if (prefab)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (prefabs)
                UnityEngine.Object.DestroyImmediate(prefabs);
        }
    }
}

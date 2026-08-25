using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class HierarchyPoolPrototypeTests
{
    [Test]
    public void PrefabPrototypeIncludesUnspawnedNestedIdentities()
    {
        var root = CreateRootWithNestedChildren(nameof(PrefabPrototypeIncludesUnspawnedNestedIdentities));

        try
        {
            NetworkManager.SetupPrefabInfo(root, 123, true);

            using (var livePrototype = HierarchyPool.GetFullPrototype(root.transform))
            {
                Assert.That(livePrototype.framework.Count, Is.EqualTo(1));
            }

            using (var prefabPrototype = HierarchyPool.GetFullPrototype(root.transform, null, true))
            {
                Assert.That(prefabPrototype.framework.Count, Is.EqualTo(4));
            }
        }
        finally
        {
            if (root)
                Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryBuildPrototypeFailsWhenChildPieceIsMissing()
    {
        var poolRoot = new GameObject(nameof(TryBuildPrototypeFailsWhenChildPieceIsMissing) + "Pool");
        var rootOnly = new GameObject("RootOnly");
        var fullRoot = new GameObject("FullRoot");

        try
        {
            var prefabPool = new HierarchyPool(poolRoot.transform);
            var pair = new PoolPair(null, prefabPool);

            rootOnly.AddComponent<NetworkIdentity>();
            NetworkManager.SetupPrefabInfo(rootOnly, 321, true);
            prefabPool.PutBackInPool(rootOnly);
            rootOnly = null;

            fullRoot.AddComponent<NetworkIdentity>();
            AddChild(fullRoot, "MissingChild");
            NetworkManager.SetupPrefabInfo(fullRoot, 321, true);

            using var prototype = HierarchyPool.GetFullPrototype(fullRoot.transform, null, true);
            var created = new List<NetworkIdentity>();

            var success = HierarchyPool.TryBuildPrototype(pair, prototype, created, out var rebuilt, out _);

            Assert.IsFalse(success);
            Assert.IsFalse(rebuilt);
        }
        finally
        {
            if (rootOnly)
                Object.DestroyImmediate(rootOnly);
            if (fullRoot)
                Object.DestroyImmediate(fullRoot);
            if (poolRoot)
                Object.DestroyImmediate(poolRoot);
        }
    }

    [Test]
    public void SceneDefaultPrototypeRestoresNestedUnspawnedIdentities()
    {
        var poolRoot = new GameObject(nameof(SceneDefaultPrototypeRestoresNestedUnspawnedIdentities) + "Pool");
        var root = CreateRootWithNestedChildren(nameof(SceneDefaultPrototypeRestoresNestedUnspawnedIdentities));
        GameObject rebuilt = null;

        try
        {
            var scenePool = new HierarchyPool(poolRoot.transform);
            var pair = new PoolPair(scenePool, null);

            PrepareScenePieces(root, -42);

            var activePieces = new List<NetworkIdentity>();
            root.GetComponentsInChildren(true, activePieces);

            foreach (var piece in activePieces)
                scenePool.RegisterActiveScenePiece(piece);

            using var defaultPrototype = HierarchyPool.GetFullPrototype(root.transform, null, true);
            Assert.That(defaultPrototype.framework.Count, Is.EqualTo(activePieces.Count));

            var created = new List<NetworkIdentity>();
            var success = HierarchyPool.TryBuildPrototype(pair, defaultPrototype, created, out rebuilt, out _);

            Assert.IsTrue(success);
            Assert.IsTrue(rebuilt);

            var restoredPieces = new List<NetworkIdentity>();
            rebuilt.GetComponentsInChildren(true, restoredPieces);
            Assert.That(restoredPieces.Count, Is.EqualTo(activePieces.Count));
        }
        finally
        {
            if (rebuilt)
                Object.DestroyImmediate(rebuilt);
            else if (root)
                Object.DestroyImmediate(root);

            if (poolRoot)
                Object.DestroyImmediate(poolRoot);
        }
    }

    [UnityTest]
    public IEnumerator ScenePoolLease_IsolatesTheSameLogicalIdAcrossPhysicalScenes()
    {
        var managerObject = new GameObject(
            nameof(ScenePoolLease_IsolatesTheSameLogicalIdAcrossPhysicalScenes));
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        var firstScene = managerObject.scene;
        var secondScene = SceneManager.CreateScene(
            nameof(ScenePoolLease_IsolatesTheSameLogicalIdAcrossPhysicalScenes) +
            SceneManager.sceneCount);
        var sceneId = new SceneID(64000);
        NetworkPoolManager.ScenePoolLease firstLease = null;
        NetworkPoolManager.ScenePoolLease secondLease = null;
        AsyncOperation unload = null;

        try
        {
            firstLease = NetworkPoolManager.AcquireScenePool(manager, firstScene, sceneId);
            secondLease = NetworkPoolManager.AcquireScenePool(manager, secondScene, sceneId);

            Assert.That(secondLease.pool, Is.Not.SameAs(firstLease.pool));
        }
        finally
        {
            secondLease?.Dispose();
            firstLease?.Dispose();
            Object.DestroyImmediate(managerObject);
            if (secondScene.IsValid() && secondScene.isLoaded)
                unload = SceneManager.UnloadSceneAsync(secondScene);
        }

        if (unload != null)
            yield return unload;
    }

    [Test]
    public void ScenePoolLease_IsolatesManagersAndRejectsBroadActiveRemoval()
    {
        var firstManagerObject = new GameObject(
            nameof(ScenePoolLease_IsolatesManagersAndRejectsBroadActiveRemoval) + "A");
        var secondManagerObject = new GameObject(
            nameof(ScenePoolLease_IsolatesManagersAndRejectsBroadActiveRemoval) + "B");
        firstManagerObject.SetActive(false);
        secondManagerObject.SetActive(false);
        var firstManager = firstManagerObject.AddComponent<NetworkManager>();
        var secondManager = secondManagerObject.AddComponent<NetworkManager>();
        firstManager.startServerFlags = StartFlags.None;
        firstManager.startClientFlags = StartFlags.None;
        secondManager.startServerFlags = StartFlags.None;
        secondManager.startClientFlags = StartFlags.None;
        var scene = firstManagerObject.scene;
        var sceneId = new SceneID(64001);
        NetworkPoolManager.ScenePoolLease firstLease = null;
        NetworkPoolManager.ScenePoolLease secondLease = null;
        NetworkPoolManager.ScenePoolLease firstFollowup = null;
        NetworkPoolManager.ScenePoolLease secondFollowup = null;

        try
        {
            firstLease = NetworkPoolManager.AcquireScenePool(firstManager, scene, sceneId);
            secondLease = NetworkPoolManager.AcquireScenePool(secondManager, scene, sceneId);
            Assert.That(secondLease.pool, Is.Not.SameAs(firstLease.pool));

            NetworkPoolManager.RemovePool(sceneId);
            Assert.That(NetworkPoolManager.RemovePool(firstManager, scene, sceneId), Is.False,
                "An ownership-scoped cleanup must not dispose an active hierarchy lease.");

            firstFollowup = NetworkPoolManager.AcquireScenePool(firstManager, scene, sceneId);
            secondFollowup = NetworkPoolManager.AcquireScenePool(secondManager, scene, sceneId);
            Assert.That(firstFollowup.pool, Is.SameAs(firstLease.pool));
            Assert.That(secondFollowup.pool, Is.SameAs(secondLease.pool));
        }
        finally
        {
            secondFollowup?.Dispose();
            firstFollowup?.Dispose();
            secondLease?.Dispose();
            firstLease?.Dispose();
            Object.DestroyImmediate(secondManagerObject);
            Object.DestroyImmediate(firstManagerObject);
        }
    }

    [Test]
    public void ScenePoolLease_SharesListenRolesUntilTheLastRoleReleases()
    {
        var managerObject = new GameObject(
            nameof(ScenePoolLease_SharesListenRolesUntilTheLastRoleReleases));
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        var scene = managerObject.scene;
        var sceneId = new SceneID(64002);
        NetworkPoolManager.ScenePoolLease serverLease = null;
        NetworkPoolManager.ScenePoolLease clientLease = null;
        NetworkPoolManager.ScenePoolLease survivingRoleLease = null;
        NetworkPoolManager.ScenePoolLease replacementLease = null;

        try
        {
            serverLease = NetworkPoolManager.AcquireScenePool(manager, scene, sceneId);
            clientLease = NetworkPoolManager.AcquireScenePool(manager, scene, sceneId);
            var sharedPool = serverLease.pool;
            Assert.That(clientLease.pool, Is.SameAs(sharedPool));

            serverLease.Dispose();
            serverLease = null;
            survivingRoleLease = NetworkPoolManager.AcquireScenePool(manager, scene, sceneId);
            Assert.That(survivingRoleLease.pool, Is.SameAs(sharedPool),
                "Releasing one listen role must preserve the other role's pool.");

            survivingRoleLease.Dispose();
            survivingRoleLease = null;
            clientLease.Dispose();
            clientLease = null;

            replacementLease = NetworkPoolManager.AcquireScenePool(manager, scene, sceneId);
            Assert.That(replacementLease.pool, Is.Not.SameAs(sharedPool),
                "The pool must retire when its final hierarchy role releases ownership.");
        }
        finally
        {
            replacementLease?.Dispose();
            survivingRoleLease?.Dispose();
            clientLease?.Dispose();
            serverLease?.Dispose();
            Object.DestroyImmediate(managerObject);
        }
    }

    private static GameObject CreateRootWithNestedChildren(string name)
    {
        var root = new GameObject(name);
        root.AddComponent<NetworkIdentity>();
        AddChild(root, "A");
        var b = AddChild(root, "B");
        AddChild(b, "B1");
        return root;
    }

    private static GameObject AddChild(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform);
        child.AddComponent<NetworkIdentity>();
        return child;
    }

    private static void PrepareScenePieces(GameObject root, int prefabId)
    {
        var pieces = new List<NetworkIdentity>();
        root.GetComponentsInChildren(true, pieces);

        for (int i = 0; i < pieces.Count; i++)
            pieces[i].PreparePrefabInfo(prefabId, i, true, true);
    }
}

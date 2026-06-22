using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

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

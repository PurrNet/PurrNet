using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.TestTools;

public class HierarchyPoolPrototypeTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SinglePiecePrototypeMatchesFilteredHierarchy(bool parented, bool observeSibling)
    {
        NetworkManager.LoadOrGenerateHashes();
        var parent = new GameObject("Parent");
        var root = new GameObject("Root");
        var player = new PlayerID(1, false);

        try
        {
            var parentIdentity = parent.AddComponent<NetworkIdentity>();
            parentIdentity.PreparePrefabInfo(122, 0, false, true);
            parentIdentity.SetID(new NetworkID(10));
            if (parented)
                root.transform.SetParent(parent.transform);
            var first = root.AddComponent<NetworkIdentity>();
            var sibling = root.AddComponent<NetworkIdentity>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform);
            var hidden = AddChild(visual, "UnobservedNetworkChild");
            first.PreparePrefabInfo(123, 0, false, true);
            sibling.PreparePrefabInfo(123, 1, false, true);
            first.SetID(new NetworkID(20));
            sibling.SetID(new NetworkID(21));
            hidden.GetComponent<NetworkIdentity>().SetID(new NetworkID(22));
            (observeSibling ? sibling : first).TryAddObserver(player);
            root.transform.localPosition = new Vector3(3, 4, 5);
            root.transform.localRotation = Quaternion.Euler(10, 20, 30);
            root.transform.localScale = new Vector3(2, 3, 4);
            root.SetActive(false);

            var expectedIdentities = new List<NetworkIdentity> { parentIdentity };
            Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, expectedIdentities, out var expected));
            using (expected)
            {
                Object.DestroyImmediate(hidden);
                var actualIdentities = new List<NetworkIdentity> { parentIdentity };
                Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, actualIdentities, out var actual));
                using (actual)
                {
                    Assert.That(actual.framework.Count, Is.EqualTo(1));
                    Assert.That(actual.parentID.HasValue, Is.EqualTo(parented));
                    Assert.That(actual.path, parented ? Is.EqualTo(new[] { root.transform.GetSiblingIndex() }) : Is.Null);
                    CollectionAssert.AreEqual(new[] { parentIdentity, first, sibling }, actualIdentities);
                    CollectionAssert.AreEqual(expectedIdentities, actualIdentities);
                    using var expectedBits = BitPackerPool.Get();
                    using var actualBits = BitPackerPool.Get();
                    Packer<GameObjectPrototype>.Write(expectedBits, expected);
                    Packer<GameObjectPrototype>.Write(actualBits, actual);
                    Assert.IsTrue(new BitData(expectedBits).Equals(new BitData(actualBits)),
                        "Single-piece capture must preserve the exact prototype wire data.");
                }
            }

            Assert.IsFalse(HierarchyPool.TryGetPrototype(root.transform, new PlayerID(2, false), null, out var invisible));
            invisible.Dispose();
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent);
        }
    }

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

            var missingPiece = prototype.framework[1].pid;
            LogAssert.Expect(LogType.Error, $"[HierarchyPool] Cannot warm up piece '{missingPiece}': this pool has no prefab resolver");
            LogAssert.Expect(LogType.Error, $"[HierarchyPool] Piece '{missingPiece}' is still missing from the pool after warmup");
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

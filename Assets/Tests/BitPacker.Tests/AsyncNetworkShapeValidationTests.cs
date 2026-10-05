using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Run with Unity's sequential EditMode runner; these fixtures inspect shared static state.
public class AsyncNetworkShapeValidationTests
{
    private static readonly MethodInfo ValidateMethod = typeof(HierarchyV2).GetMethod(
        "HasMatchingAsyncNetworkShape", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo CacheField = typeof(HierarchyV2).GetField(
        "_cachedPrefabAsyncShapes", BindingFlags.NonPublic | BindingFlags.Static);
    private readonly List<GameObject> _roots = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        var cache = CacheField?.GetValue(null) as IDictionary;
        foreach (var root in _roots)
        {
            cache?.Remove(root);
            if (root)
                UnityProxy.DestroyImmediateDirectly(root);
        }
        _roots.Clear();
    }

    [Test]
    public void AcceptsDeepInactiveHierarchyWithStackedAndMixedIdentityTypes()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        Assert.That(instance.activeSelf, Is.False);
        Assert.That(Leaf(instance).GetComponents<NetworkIdentity>(), Has.Length.EqualTo(3));

        AssertMatch(prefab, instance, true); // Captures the cold prefab shape.
        AssertMatch(prefab, instance, true); // Reuses the cached prefab shape.
    }

    [Test]
    public void AcceptsDifferentRootNameAndExternalParent()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var externalParent = new GameObject("ExternalParent");
        _roots.Add(externalParent);
        instance.name = "RuntimeRootName";
        instance.transform.SetParent(externalParent.transform, false);
        AssertMatch(prefab, instance, true);
    }

    [Test]
    public void RejectsIdentityTypeChangeWithSameComponentCount()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var leaf = Leaf(instance);
        UnityProxy.DestroyImmediateDirectly(leaf.GetComponent<NetworkTransform>());
        leaf.gameObject.AddComponent<NetworkIdentity>();
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsStackedComponentOrderChange()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var leaf = Leaf(instance);
        UnityProxy.DestroyImmediateDirectly(leaf.GetComponent<NetworkIdentity>());
        leaf.gameObject.AddComponent<NetworkIdentity>();
        Assert.That(leaf.GetComponents<NetworkIdentity>()[1], Is.TypeOf<NetworkTransform>());
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsSameTypeSiblingSwap()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var twins = instance.transform.Find("Twins");
        Assert.That(twins.GetChild(0).GetComponent<NetworkIdentity>().GetType(),
            Is.EqualTo(twins.GetChild(1).GetComponent<NetworkIdentity>().GetType()));
        twins.GetChild(1).SetSiblingIndex(0);
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsChangedSiblingIndexWithUnchangedIdentityOrder()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        instance.transform.Find("Decoration").SetSiblingIndex(0);
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsRenamedNonNetworkAncestor()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        instance.transform.Find("Branch/Level0/Level1").name = "RenamedAncestor";
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsAddedTransformDepthWithoutChangingIdentityCount()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var leaf = Leaf(instance);
        var wrapper = AddChild(leaf.parent, "NewWrapper");
        leaf.SetParent(wrapper, false);
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void RejectsChangedParentPath()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        Leaf(instance).SetParent(instance.transform.Find("Decoration"), false);
        AssertMatch(prefab, instance, false);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RejectsChangedIdentityCount(bool add)
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        if (add)
            Leaf(instance).gameObject.AddComponent<NetworkIdentity>();
        else
            UnityProxy.DestroyImmediateDirectly(Leaf(instance).GetComponent<NetworkIdentity>());
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void DoesNotReuseInstanceValidationAfterMutation()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        AssertMatch(prefab, instance, true);
        instance.transform.Find("Branch/Level0/Level1").name = "ChangedAfterFirstCheck";
        AssertMatch(prefab, instance, false);
    }

    [Test]
    public void IgnoresNullAndDestroyedEntriesOutsideComponentRuns()
    {
        var prefab = CreateTemplate();
        var instance = Clone(prefab);
        var identities = new List<NetworkIdentity>();
        instance.GetComponentsInChildren(true, identities);
        var temporary = new GameObject("DestroyedEntry");
        var destroyedIdentity = temporary.AddComponent<NetworkIdentity>();
        UnityProxy.DestroyImmediateDirectly(temporary);
        identities.Insert(0, null);
        identities.Add(destroyedIdentity);
        AssertMatch(prefab, instance, true, identities);
    }

    [Test]
    public void AcceptsHierarchyWithoutIdentities()
    {
        var prefab = new GameObject("EmptyPrefab");
        prefab.SetActive(false);
        _roots.Add(prefab);
        AddChild(prefab.transform, "EmptyChild");
        AssertMatch(prefab, Clone(prefab), true);
    }

    private static void AssertMatch(GameObject prefab, GameObject instance, bool expected,
        List<NetworkIdentity> identities = null)
    {
        Assert.IsNotNull(ValidateMethod);
        identities ??= new List<NetworkIdentity>();
        if (identities.Count == 0)
            instance.GetComponentsInChildren(true, identities);
        var arguments = new object[] { prefab, instance, identities, null };
        var matches = (bool)ValidateMethod.Invoke(null, arguments);
        Assert.That(matches, Is.EqualTo(expected), arguments[3] as string);
        if (expected)
            Assert.That(arguments[3], Is.Null);
        else
            Assert.That(arguments[3], Is.Not.Null);
    }

    private GameObject CreateTemplate()
    {
        var root = new GameObject("ShapePrefab");
        root.SetActive(false);
        _roots.Add(root);
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<NetworkIdentity>();
        var current = AddChild(root.transform, "Branch");
        for (var depth = 0; depth < 8; depth++)
            current = AddChild(current, "Level" + depth);
        current = AddChild(current, "Leaf");
        current.gameObject.AddComponent<NetworkIdentity>();
        current.gameObject.AddComponent<NetworkIdentity>();
        current.gameObject.AddComponent<NetworkTransform>();
        var twins = AddChild(root.transform, "Twins");
        AddChild(twins, "First").gameObject.AddComponent<NetworkIdentity>();
        AddChild(twins, "Second").gameObject.AddComponent<NetworkIdentity>();
        AddChild(root.transform, "Decoration");
        return root;
    }

    private GameObject Clone(GameObject prefab)
    {
        var instance = UnityProxy.InstantiateDirectly(prefab);
        _roots.Add(instance);
        return instance;
    }

    private static Transform Leaf(GameObject root) =>
        root.transform.Find("Branch/Level0/Level1/Level2/Level3/Level4/Level5/Level6/Level7/Leaf");

    private static Transform AddChild(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }
}

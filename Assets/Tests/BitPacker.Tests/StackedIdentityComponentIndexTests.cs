using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class StackedIdentityComponentIndexTests
{
    [Test]
    public void SetupPrefabInfoMatchesPerGameObjectLookup()
    {
        var root = CreateStackedHierarchy(nameof(SetupPrefabInfoMatchesPerGameObjectLookup));

        try
        {
            var identities = new List<NetworkIdentity>();
            root.GetComponentsInChildren(true, identities);

            var expected = new int[identities.Count];
            var sawStackedIdentity = false;

            for (var i = 0; i < identities.Count; i++)
            {
                var first = identities[i].transform.GetComponent<NetworkIdentity>();
                expected[i] = identities.IndexOf(first);

                if (expected[i] != i)
                    sawStackedIdentity = true;
            }

            Assert.IsTrue(sawStackedIdentity, "the hierarchy never stacked identities on one GameObject");

            NetworkManager.SetupPrefabInfo(root, 555, false);

            for (var i = 0; i < identities.Count; i++)
            {
                Assert.That(identities[i].componentIndex, Is.EqualTo(expected[i]),
                    $"{identities[i].gameObject.name} identity {i}");
            }
        }
        finally
        {
            if (root)
                UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SetupPrefabInfoWithSharedListMatchesTheAllocatingOverload()
    {
        var root = CreateStackedHierarchy(nameof(SetupPrefabInfoWithSharedListMatchesTheAllocatingOverload));
        var shared = CreateStackedHierarchy(nameof(SetupPrefabInfoWithSharedListMatchesTheAllocatingOverload) + "Shared");

        try
        {
            NetworkManager.SetupPrefabInfo(root, 555, false);

            var sharedIdentities = new List<NetworkIdentity>();
            shared.GetComponentsInChildren(true, sharedIdentities);
            NetworkManager.SetupPrefabInfo(shared, 555, false, sharedIdentities);

            var identities = new List<NetworkIdentity>();
            root.GetComponentsInChildren(true, identities);

            Assert.That(sharedIdentities.Count, Is.EqualTo(identities.Count));

            for (var i = 0; i < identities.Count; i++)
            {
                Assert.That(sharedIdentities[i].componentIndex, Is.EqualTo(identities[i].componentIndex),
                    $"{identities[i].gameObject.name} identity {i}");
            }
        }
        finally
        {
            if (root)
                UnityEngine.Object.DestroyImmediate(root);
            if (shared)
                UnityEngine.Object.DestroyImmediate(shared);
        }
    }

    [Test]
    public void AsyncShapeCaptureMatchesPerGameObjectLookup()
    {
        var root = CreateStackedHierarchy(nameof(AsyncShapeCaptureMatchesPerGameObjectLookup));

        try
        {
            var identities = new List<NetworkIdentity>();
            root.GetComponentsInChildren(true, identities);

            var captured = CaptureAsyncShapeComponentIndices(root);
            Assert.That(captured.Count, Is.EqualTo(identities.Count));

            var siblings = new List<NetworkIdentity>();
            var sawStackedIdentity = false;

            for (var i = 0; i < identities.Count; i++)
            {
                identities[i].gameObject.GetComponents(siblings);
                var expected = siblings.IndexOf(identities[i]);

                if (expected != 0)
                    sawStackedIdentity = true;

                Assert.That(captured[i], Is.EqualTo(expected), $"{identities[i].gameObject.name} identity {i}");
            }

            Assert.IsTrue(sawStackedIdentity, "the hierarchy never stacked identities on one GameObject");
        }
        finally
        {
            if (root)
                UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static List<int> CaptureAsyncShapeComponentIndices(GameObject root)
    {
        MethodInfo capture = null;
        var methods = typeof(HierarchyV2).GetMethods(BindingFlags.NonPublic | BindingFlags.Static);

        for (var i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != "CaptureAsyncNetworkShape")
                continue;

            var parameters = methods[i].GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(GameObject) ||
                !parameters[1].ParameterType.IsGenericType)
                continue;

            capture = methods[i];
            break;
        }

        Assert.IsNotNull(capture, "CaptureAsyncNetworkShape(GameObject, List<shape entry>) was not found");

        var listType = capture.GetParameters()[1].ParameterType;
        var entryType = listType.GetGenericArguments()[0];
        var result = Activator.CreateInstance(listType);

        capture.Invoke(null, new object[] { root, result });

        var field = entryType.GetField("componentIndex");
        Assert.IsNotNull(field, $"{entryType.Name}.componentIndex was renamed or removed");

        var indices = new List<int>();
        foreach (var entry in (IEnumerable)result)
            indices.Add((int)field.GetValue(entry));

        return indices;
    }

    private static GameObject CreateStackedHierarchy(string name)
    {
        var root = new GameObject(name);
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<NetworkIdentity>();

        var a = AddChild(root, "A", 1);
        AddChild(a, "A1", 2);
        AddChild(root, "B", 4);
        return root;
    }

    private static GameObject AddChild(GameObject parent, string name, int identityCount)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform);

        for (var i = 0; i < identityCount; i++)
            child.AddComponent<NetworkIdentity>();

        return child;
    }
}

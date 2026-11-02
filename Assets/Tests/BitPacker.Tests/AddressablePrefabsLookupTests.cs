#if ADDRESSABLES_PURRNET_SUPPORT
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using UnityEngine.TestTools;

public class AddressablePrefabsLookupTests
{
    const string SHARED_NAME = "SharedAddressablePrefab";

    readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _created.Count; i++)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
    }

    AddressableNetworkPrefabs CreateProvider()
    {
        var provider = ScriptableObject.CreateInstance<AddressableNetworkPrefabs>();
        _created.Add(provider);
        return provider;
    }

    GameObject CreateNetworkedPrefab(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<NetworkIdentity>();
        _created.Add(go);
        return go;
    }

    GameObject CreatePlainPrefab(string name)
    {
        var go = new GameObject(name);
        _created.Add(go);
        return go;
    }

    [Test]
    public void ReferenceMatchResolvesWithoutWarning()
    {
        var provider = CreateProvider();
        var registered = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", registered);

        Assert.That(provider.TryGetPrefabData(registered, out var data), Is.True);
        Assert.That(data.prefab, Is.EqualTo(registered));
    }

    [Test]
    public void BundleCopyResolvesByNameAndWarns()
    {
        var provider = CreateProvider();
        var registered = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", registered);

        var bundleCopy = CreateNetworkedPrefab(SHARED_NAME);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(SHARED_NAME));

        Assert.That(provider.TryGetPrefabData(bundleCopy, out var data), Is.True);
        Assert.That(data.prefab, Is.EqualTo(registered));
    }

    [Test]
    public void NonNetworkedPrefabSharingNameIsNotResolved()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered", CreateNetworkedPrefab(SHARED_NAME));

        var foreign = CreatePlainPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(foreign, out _), Is.False);
    }

    [Test]
    public void AmbiguousNameIsNotResolved()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered-a", CreateNetworkedPrefab(SHARED_NAME));
        provider.AddRuntimePrefab("registered-b", CreateNetworkedPrefab(SHARED_NAME));

        var bundleCopy = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(bundleCopy, out _), Is.False);
    }

#if UNITY_EDITOR
    [Test]
    public void CountIncludesLinkedEntriesWhenRootIsEmpty()
    {
        var root = CreateProvider();
        var linked = CreateProvider();
        var nested = CreateProvider();
        linked.AddEntry(new UnityEngine.AddressableAssets.AssetReferenceGameObject("00000000000000000000000000000001"));
        nested.AddEntry(new UnityEngine.AddressableAssets.AssetReferenceGameObject("00000000000000000000000000000002"));

        root.linkedAddressablePrefabs.Add(linked);
        linked.linkedAddressablePrefabs.Add(nested);
        nested.linkedAddressablePrefabs.Add(root);

        Assert.That(root.count, Is.EqualTo(2));
    }
#endif
}
#endif

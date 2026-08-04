using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using UnityEngine;

public class NetworkPrefabsLookupTests
{
    const string SHARED_NAME = "SharedPrefabName";

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

    NetworkPrefabs CreateProvider()
    {
        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
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
    public void ReferenceMatchResolvesRegisteredPrefab()
    {
        var provider = CreateProvider();
        var networked = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", networked);

        Assert.That(provider.TryGetPrefabData(networked, out var data), Is.True);
        Assert.That(data.prefab, Is.EqualTo(networked));
    }

    [Test]
    public void NonNetworkedPrefabSharingNameIsNotResolved()
    {
        var provider = CreateProvider();
        var networked = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", networked);

        var foreign = CreatePlainPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(foreign, out var data), Is.False,
            $"An unregistered prefab with no NetworkIdentity resolved to `{data.prefab}` purely because of a name collision.");
    }

    [Test]
    public void NetworkedPrefabCopySharingNameStillResolves()
    {
        var provider = CreateProvider();
        var networked = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", networked);

        var bundleCopy = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(bundleCopy, out var data), Is.True);
        Assert.That(data.prefab, Is.EqualTo(networked));
    }

    [Test]
    public void NetworkedPrefabWithDifferentIdentityCountIsNotResolved()
    {
        var provider = CreateProvider();
        var networked = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", networked);

        var unrelated = CreateNetworkedPrefab(SHARED_NAME);
        var child = new GameObject("Nested");
        child.AddComponent<NetworkIdentity>();
        child.transform.SetParent(unrelated.transform);

        Assert.That(provider.TryGetPrefabData(unrelated, out _), Is.False);
    }

    [Test]
    public void UnregisteredNetworkedPrefabSharingNameIsNotResolved()
    {
        var provider = CreateProvider();
        var registered = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered", registered);

        var unrelated = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(unrelated, out var data), Is.False,
            $"An unregistered networked prefab resolved to `{data.prefab}` purely because of a name collision.");
    }

    [Test]
    public void BundleCopyResolvesEvenWhenAnotherPrefabSharesTheName()
    {
        var provider = CreateProvider();
        var registeredA = CreateNetworkedPrefab(SHARED_NAME);
        var registeredB = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered-a", registeredA);
        provider.AddRuntimePrefab("registered-b", registeredB);

        var bundleCopyOfA = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(bundleCopyOfA, out var data), Is.True);
        Assert.That(data.prefab, Is.EqualTo(registeredA));
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
}

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
    public void EachRegisteredPrefabResolvesToItselfDespiteSharedNames()
    {
        var provider = CreateProvider();
        var first = CreateNetworkedPrefab(SHARED_NAME);
        var second = CreateNetworkedPrefab(SHARED_NAME);
        provider.AddRuntimePrefab("registered-a", first);
        provider.AddRuntimePrefab("registered-b", second);

        Assert.That(provider.TryGetPrefabData(first, out var firstData), Is.True);
        Assert.That(firstData.prefab, Is.EqualTo(first));

        Assert.That(provider.TryGetPrefabData(second, out var secondData), Is.True);
        Assert.That(secondData.prefab, Is.EqualTo(second));
    }

    [Test]
    public void NonNetworkedPrefabSharingNameIsNotResolved()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered", CreateNetworkedPrefab(SHARED_NAME));

        var foreign = CreatePlainPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(foreign, out var data), Is.False,
            $"An unregistered prefab with no NetworkIdentity resolved to `{data.prefab}` purely because of a name collision.");
    }

    [Test]
    public void UnregisteredNetworkedPrefabSharingNameIsNotResolved()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered", CreateNetworkedPrefab(SHARED_NAME));

        var unrelated = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(unrelated, out var data), Is.False,
            $"An unregistered networked prefab resolved to `{data.prefab}` purely because of a name collision.");
    }

    [Test]
    public void IdenticalUnregisteredCopyIsNotResolved()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered", CreateNetworkedPrefab(SHARED_NAME));

        var copy = CreateNetworkedPrefab(SHARED_NAME);

        Assert.That(provider.TryGetPrefabData(copy, out _), Is.False);
    }

    [Test]
    public void UnregisteredPrefabIsNotResolvedWhenNothingShareItsName()
    {
        var provider = CreateProvider();
        provider.AddRuntimePrefab("registered", CreateNetworkedPrefab(SHARED_NAME));

        var unrelated = CreateNetworkedPrefab("SomethingElse");

        Assert.That(provider.TryGetPrefabData(unrelated, out _), Is.False);
    }
}

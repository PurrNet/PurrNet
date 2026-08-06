using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class UnresolvedNetworkPrefabWarningTests
{
    const string PREFAB_NAME = "BundledMonster";

    readonly List<Object> _created = new();
    NetworkManager _manager;

    [SetUp]
    public void SetUp()
    {
        var go = new GameObject("TestNetworkManager");
        go.SetActive(false);
        _manager = go.AddComponent<NetworkManager>();
        _manager.SetNetworkRules(ScriptableObject.CreateInstance<NetworkRules>());
        go.SetActive(true);
        _created.Add(go);

        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        _manager.SetPrefabProvider(provider);
    }

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

    GameObject CreateNetworkedPrefab(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.AddComponent<NetworkIdentity>();
        _created.Add(go);
        return go;
    }

    [Test]
    public void InstantiatingUnregisteredNetworkedPrefabWarns()
    {
        var registered = CreateNetworkedPrefab(PREFAB_NAME);
        _manager.prefabProvider.AddRuntimePrefab("registered", registered);

        var bundleDuplicate = CreateNetworkedPrefab(PREFAB_NAME);

        LogAssert.Expect(LogType.Warning, new Regex(PREFAB_NAME));

        var instance = (GameObject)UnityProxy.Instantiate((Object)bundleDuplicate);
        _created.Add(instance);

        Assert.That(instance, Is.Not.Null);
    }

    [Test]
    public void InstantiatingNonNetworkedObjectStaysSilent()
    {
        var plain = new GameObject("PlainObject");
        plain.SetActive(false);
        _created.Add(plain);

        var instance = (GameObject)UnityProxy.Instantiate((Object)plain);
        _created.Add(instance);

        Assert.That(instance, Is.Not.Null);
        LogAssert.NoUnexpectedReceived();
    }

}

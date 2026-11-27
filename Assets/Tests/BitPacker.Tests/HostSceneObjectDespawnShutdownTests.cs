using System.Collections;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class HostSceneObjectDespawnShutdownTests
{
    const string SceneName = "ServerConnectedSceneSpawnTarget";
    const string SceneObjectName = "ServerConnectedSceneSpawnRoot";

    GameObject _go;
    NetworkManager _manager;
    NetworkRules _rules;
    NetworkPrefabs _prefabs;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_manager)
        {
            _manager.StopClient();
            _manager.StopServer();
            for (int i = 0; i < 10; i++)
                yield return null;
        }

        var scene = SceneManager.GetSceneByName(SceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            var op = SceneManager.UnloadSceneAsync(scene);
            while (op != null && !op.isDone)
                yield return null;
        }

        if (_go)
            Object.DestroyImmediate(_go);
        if (_rules)
            Object.DestroyImmediate(_rules);
        if (_prefabs)
            Object.DestroyImmediate(_prefabs);
        _manager = null;
    }

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    static NetworkIdentity FindSceneObject()
    {
        var scene = SceneManager.GetSceneByName(SceneName);
        if (!scene.IsValid())
            return null;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == SceneObjectName && root.activeSelf && root.TryGetComponent<NetworkIdentity>(out var identity))
                return identity;
        }
        return null;
    }

    [UnityTest]
    public IEnumerator DespawnSceneObject_ThenStopClientFirst_ShutsDownCleanly()
    {
        yield return DespawnThenStop(clientFirst: true);
    }

    [UnityTest]
    public IEnumerator DespawnSceneObject_ThenStopBothSameFrame_ShutsDownCleanly()
    {
        yield return DespawnThenStop(clientFirst: null);
    }

    [UnityTest]
    public IEnumerator DespawnSceneObject_ThenStopServerFirst_ShutsDownCleanly()
    {
        yield return DespawnThenStop(clientFirst: false);
    }

    [UnityTest]
    public IEnumerator DespawnSceneObject_ServerOnly_ShutsDownCleanly()
    {
        yield return DespawnThenStop(clientFirst: false, host: false);
    }

    IEnumerator DespawnThenStop(bool? clientFirst, bool host = true)
    {
        _go = new GameObject("TestNetworkManager");
        _go.SetActive(false);
        var transport = _go.AddComponent<LocalTransport>();
        _manager = _go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;
        _rules = ScriptableObject.CreateInstance<NetworkRules>();
        _manager.SetNetworkRules(_rules);
        _prefabs = ScriptableObject.CreateInstance<NetworkPrefabs>();
        _prefabs.autoGenerate = false;
        _manager.SetPrefabProvider(_prefabs);
        _manager.transport = transport;
        _go.SetActive(true);

        if (host)
        {
            _manager.StartHost();
            yield return WaitUntil(() => _manager.isServer && _manager.isClient && _manager.isLocalPlayerReady);
            Assert.That(_manager.isLocalPlayerReady, Is.True, "host did not start");
        }
        else
        {
            _manager.StartServer();
            yield return WaitUntil(() => _manager.serverState == ConnectionState.Connected);
            Assert.That(_manager.isServer, Is.True, "server did not start");
        }

        var load = _manager.sceneModule.LoadSceneAsync(SceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });
        Assert.That(load, Is.Not.Null, "test scene missing from build settings");

        NetworkIdentity identity = null;
        yield return WaitUntil(() => (identity = FindSceneObject()) && identity.isSpawned);
        Assert.That(identity && identity.isSpawned && identity.isSceneObject, Is.True, "scene identity was not spawned on the host");

        identity.Despawn();
        yield return WaitUntil(() => !FindSceneObject());
        Assert.That(FindSceneObject(), Is.Null, "scene identity was not despawned");
        for (int i = 0; i < 5; i++)
            yield return null;

        if (clientFirst == null)
        {
            _manager.StopClient();
            _manager.StopServer();
        }
        else if (clientFirst.Value)
        {
            _manager.StopClient();
            yield return WaitUntil(() => _manager.clientState == ConnectionState.Disconnected);
            for (int i = 0; i < 5; i++)
                yield return null;
            _manager.StopServer();
        }
        else
        {
            _manager.StopServer();
            for (int i = 0; i < 5; i++)
                yield return null;
            _manager.StopClient();
        }

        yield return WaitUntil(() => _manager.clientState == ConnectionState.Disconnected && _manager.serverState == ConnectionState.Disconnected);
        for (int i = 0; i < 10; i++)
            yield return null;

        LogAssert.NoUnexpectedReceived();
    }
}

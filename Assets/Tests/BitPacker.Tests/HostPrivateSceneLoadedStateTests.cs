using System.Collections;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class HostPrivateSceneLoadedStateTests
{
    const string SceneName = "SceneMembershipTargetA";
    const string ScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";

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

    [UnityTest]
    public IEnumerator AddHostToAlreadyLoadedPrivateScene_MarksHostLoaded()
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

        _manager.StartHost();
        yield return WaitUntil(() => _manager.isServer && _manager.isClient && _manager.isLocalPlayerReady);
        Assert.That(_manager.isLocalPlayerReady, Is.True, "host did not start");

        var buildIndex = SceneUtility.GetBuildIndexByScenePath(ScenePath);
        Assert.That(buildIndex, Is.GreaterThanOrEqualTo(0), "test scene missing from build settings");

        var load = _manager.sceneModule.LoadSceneAsync(SceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = false
        });
        Assert.That(load, Is.Not.Null);

        yield return WaitUntil(() => load.isDone && _manager.sceneModule.IsSceneLoaded(buildIndex));
        Assert.That(_manager.sceneModule.TryGetScene(buildIndex, out var sceneId), Is.True, "scene not registered");

        // let the host client's ClientFinishedLoadingScene reach the server before the host is attached
        for (int i = 0; i < 10; i++)
            yield return null;

        var scenePlayers = _manager.GetModule<ScenePlayersModule>(true);
        var host = _manager.localPlayer;
        Assert.That(scenePlayers.IsPlayerInScene(host, sceneId), Is.False, "host should not be attached to a private scene yet");

        scenePlayers.AddPlayerToScene(host, sceneId);
        yield return null;

        Assert.That(scenePlayers.IsPlayerInScene(host, sceneId), Is.True, "host was not attached");
        Assert.That(scenePlayers.IsPlayerLoadedInScene(host, sceneId), Is.True,
            "host attached to an already-loaded private scene was never marked as loaded");
    }
}

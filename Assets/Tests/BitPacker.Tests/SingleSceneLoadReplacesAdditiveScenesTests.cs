using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SingleSceneLoadReplacesAdditiveScenesTests
{
    const string AdditiveSceneA = "SceneMembershipTargetA";
    const string AdditiveScenePathA = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    const string AdditiveSceneB = "SceneMembershipTargetB";
    const string AdditiveScenePathB = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    const string SingleScene = "SceneTransferTarget";
    const string SingleScenePath = "Assets/PlayModeTests/SceneTransferTarget.unity";

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

    static PurrSceneSettings Settings(LoadSceneMode mode) => new PurrSceneSettings
    {
        mode = mode,
        physicsMode = LocalPhysicsMode.None,
        isPublic = true
    };

    IEnumerator LoadAdditive(string sceneName, int buildIndex)
    {
        var op = _manager.sceneModule.LoadSceneAsync(sceneName, Settings(LoadSceneMode.Additive));
        Assert.That(op, Is.Not.Null, $"LoadSceneAsync returned null for {sceneName}");
        yield return WaitUntil(() => op.isDone && _manager.sceneModule.IsSceneLoaded(buildIndex));
        Assert.That(_manager.sceneModule.IsSceneLoaded(buildIndex), Is.True, $"{sceneName} was never registered");
    }

    [UnityTest]
    public IEnumerator SingleLoad_ForgetsEveryAdditiveScene()
    {
        var buildIndexA = SceneUtility.GetBuildIndexByScenePath(AdditiveScenePathA);
        var buildIndexB = SceneUtility.GetBuildIndexByScenePath(AdditiveScenePathB);
        var buildIndexSingle = SceneUtility.GetBuildIndexByScenePath(SingleScenePath);
        Assert.That(buildIndexA, Is.GreaterThanOrEqualTo(0), "test scene A missing from build settings");
        Assert.That(buildIndexB, Is.GreaterThanOrEqualTo(0), "test scene B missing from build settings");
        Assert.That(buildIndexSingle, Is.GreaterThanOrEqualTo(0), "single target scene missing from build settings");

        _go = new GameObject("TestNetworkManager");
        _go.SetActive(false);
        // A Single load must not take the manager with it.
        Object.DontDestroyOnLoad(_go);
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

        _manager.StartServer();
        yield return WaitUntil(() => _manager.isServer && _manager.sceneModule != null);
        Assert.That(_manager.isServer, Is.True, "server did not start");

        var scenes = _manager.sceneModule;

        // Two additive scenes next to each other in the tracked list is what the old removal loop skipped over.
        yield return LoadAdditive(AdditiveSceneA, buildIndexA);
        yield return LoadAdditive(AdditiveSceneB, buildIndexB);

        Assert.That(scenes.TryGetScene(buildIndexA, out var idA), Is.True);
        Assert.That(scenes.TryGetScene(buildIndexB, out var idB), Is.True);

        var unloaded = new List<SceneID>();
        void OnUnloaded(SceneID id, bool asServer)
        {
            if (asServer)
                unloaded.Add(id);
        }

        scenes.onSceneUnloaded += OnUnloaded;

        try
        {
            var single = scenes.LoadSceneAsync(SingleScene, Settings(LoadSceneMode.Single));
            Assert.That(single, Is.Not.Null, "Single LoadSceneAsync returned null");

            Assert.That(scenes.IsSceneLoaded(buildIndexA), Is.False, $"{AdditiveSceneA} is still tracked after a Single load");
            Assert.That(scenes.IsSceneLoaded(buildIndexB), Is.False, $"{AdditiveSceneB} is still tracked after a Single load");

            yield return WaitUntil(() => single.isDone && scenes.IsSceneLoaded(buildIndexSingle) &&
                                         unloaded.Contains(idA) && unloaded.Contains(idB));

            Assert.That(scenes.IsSceneLoaded(buildIndexSingle), Is.True, $"{SingleScene} was never registered");
            Assert.That(unloaded, Does.Contain(idA), $"onSceneUnloaded never fired for {AdditiveSceneA}");
            Assert.That(unloaded, Does.Contain(idB), $"onSceneUnloaded never fired for {AdditiveSceneB}");

            for (var i = 0; i < scenes.scenes.Count; i++)
            {
                Assert.That(scenes.TryGetSceneState(scenes.scenes[i], out var state), Is.True);
                Assert.That(state.scene.IsValid() && state.scene.isLoaded, Is.True,
                    $"scene {scenes.scenes[i]} is still tracked although Unity unloaded it");
            }
        }
        finally
        {
            scenes.onSceneUnloaded -= OnUnloaded;
        }
    }
}

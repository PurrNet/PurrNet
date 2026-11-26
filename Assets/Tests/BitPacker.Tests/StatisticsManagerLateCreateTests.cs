using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;

public class StatisticsManagerLateCreateTests
{
    GameObject _go;
    GameObject _statsGo;
    NetworkManager _manager;
    NetworkRules _rules;
    NetworkPrefabs _prefabs;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_statsGo)
            Object.Destroy(_statsGo);

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

    IEnumerator StartNetwork(bool host)
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
            yield return WaitUntil(() => _manager.isServer);
            Assert.That(_manager.isServer, Is.True, "server did not start");
        }

        for (int i = 0; i < 5; i++)
            yield return null;
    }

    [UnityTest]
    public IEnumerator CreatedWhileHosting_PicksUpConnectionAndPing()
    {
        yield return StartNetwork(host: true);

        _statsGo = new GameObject("Stats");
        var stats = _statsGo.AddComponent<StatisticsManager>();
        yield return null;

        Assert.That(stats.enabled, Is.True);
        Assert.That(stats.connectedServer, Is.True, "connectedServer");
        Assert.That(stats.connectedClient, Is.True, "connectedClient");

        yield return WaitUntil(() => stats.hasPingEstimate);
        Assert.That(stats.hasPingEstimate, Is.True, "no ping estimate");

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator CreatedWhileServerOnly_PicksUpServerConnection()
    {
        yield return StartNetwork(host: false);

        _statsGo = new GameObject("Stats");
        var stats = _statsGo.AddComponent<StatisticsManager>();
        yield return null;

        Assert.That(stats.connectedServer, Is.True, "connectedServer");
        Assert.That(stats.connectedClient, Is.False, "connectedClient");
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator CreatedWithoutNetworkManager_DisablesWithoutException()
    {
        Assert.That((bool)NetworkManager.main, Is.False, "a NetworkManager leaked from another test");

        LogAssert.Expect(LogType.Error, new Regex("failed to find a NetworkManager"));
        _statsGo = new GameObject("Stats");
        var stats = _statsGo.AddComponent<StatisticsManager>();
        yield return null;
        yield return null;

        Assert.That(stats.enabled, Is.False);
        LogAssert.NoUnexpectedReceived();
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class CompositeTransportChildStopTests
{
    static readonly FieldInfo TransportsField =
        typeof(CompositeTransport).GetField("_transports", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly List<Object> _created = new();
    NetworkManager _manager;
    CompositeTransport _composite;
    UDPTransport _udpB;

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

        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
        _manager = null;
        _composite = null;
        _udpB = null;
    }

    IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    IEnumerator StartHostThroughSecondChild(ushort portA, ushort portB)
    {
        var go = new GameObject("CompositeNetworkManager");
        go.SetActive(false);
        _created.Add(go);

        var udpA = go.AddComponent<UDPTransport>();
        udpA.serverPort = portA;
        _udpB = go.AddComponent<UDPTransport>();
        _udpB.serverPort = portB;
        _composite = go.AddComponent<CompositeTransport>();
        TransportsField.SetValue(_composite, new GenericTransport[] { udpA, _udpB });

        _manager = go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;

        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        _manager.SetNetworkRules(rules);

        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        _manager.SetPrefabProvider(provider);

        _manager.transport = _composite;
        go.SetActive(true);

        _composite.SetClientTransport(_udpB);
        _manager.StartHost();
        yield return WaitUntil(() => _manager.isServer && _manager.isClient && _manager.playerCount == 1);

        Assert.That(_manager.playerCount, Is.EqualTo(1), "host client did not join through the second child");
        Assert.That(_composite.connections.Count, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ChildStopsListening_DisconnectsItsConnections()
    {
        yield return StartHostThroughSecondChild(47910, 47911);

        _udpB.transport.StopListening();
        yield return WaitUntil(() => _composite.connections.Count == 0 && _manager.playerCount == 0, 3f);

        Assert.That(_composite.connections.Count, Is.EqualTo(0), "composite kept the stopped child's connection");
        Assert.That(_manager.playerCount, Is.EqualTo(0), "server kept the player that joined through the stopped child");
    }

    [UnityTest]
    public IEnumerator ChildStopsListening_ListensAgainAndClientRejoins()
    {
        yield return StartHostThroughSecondChild(47912, 47913);

        _udpB.transport.StopListening();
        yield return WaitUntil(() => _udpB.transport.clientState == ConnectionState.Disconnected &&
                                     _manager.playerCount == 0);
        Assert.That(_udpB.transport.clientState, Is.EqualTo(ConnectionState.Disconnected));

        yield return WaitUntil(() => _udpB.transport.listenerState == ConnectionState.Connected, 5f);
        Assert.That(_udpB.transport.listenerState, Is.EqualTo(ConnectionState.Connected),
            "composite did not listen on the stopped child again");
        Assert.That(_manager.isServer, Is.True);

        _manager.StartClient();
        yield return WaitUntil(() => _manager.playerCount == 1);
        Assert.That(_manager.playerCount, Is.EqualTo(1), "client could not rejoin through the restarted child");
    }
}

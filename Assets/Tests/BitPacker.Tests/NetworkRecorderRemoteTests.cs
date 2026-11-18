using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Recording;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// A dedicated server recording a remote client: what the client calls arrives through the generated RPC handlers,
/// which a host never uses for its own calls.
/// </summary>
public class NetworkRecorderRemoteTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    readonly List<Object> _created = new List<Object>();
    NetworkManager _server, _client;
    NetworkRecorder _recorder;
    GameObject _prefab;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_recorder && _recorder.isRecording)
            _recorder.StopRecording();
        if (_client)
            _client.StopClient();
        if (_server)
            _server.StopServer();
        for (int i = 0; i < 10; i++)
            yield return null;

#if UNITY_6000_4_OR_NEWER
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include))
#else
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
            if (identity)
                Object.DestroyImmediate(identity.gameObject);
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        _created.Clear();
        _server = _client = null;
    }

    static IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    static IEnumerator Ticks(NetworkManager manager, int count)
    {
        var tickModule = manager.tickModule;
        uint until = tickModule.localTick + (uint)count;
        yield return WaitUntil(() => tickModule.localTick >= until);
    }

    static void ClearMainInstance() =>
        typeof(NetworkManager).GetProperty(nameof(NetworkManager.main), BindingFlags.Public | BindingFlags.Static)!
            .GetSetMethod(true).Invoke(null, new object[] { null });

    PairedTestTransport CreateManager(string name, out NetworkManager manager)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        _created.Add(go);
        var transport = go.AddComponent<PairedTestTransport>();
        manager = go.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        manager.SetNetworkRules(rules);
        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        manager.SetPrefabProvider(provider);
        provider.AddRuntimePrefab("recorded-actor", _prefab);
        manager.transport = transport;
        return transport;
    }

    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Fields)!.SetValue(target, value);

    RecordedActor ClientCopy(RecordedActor serverCopy)
    {
#if UNITY_6000_4_OR_NEWER
        foreach (var actor in Object.FindObjectsByType<RecordedActor>(FindObjectsInactive.Include))
#else
        foreach (var actor in Object.FindObjectsByType<RecordedActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
            if (actor != serverCopy && actor.isSpawned && actor.networkManager == _client && actor.id == serverCopy.id)
                return actor;
        return null;
    }

    [UnityTest]
    public IEnumerator ADedicatedServerRecordsWhatARemoteClientCalls()
    {
        _prefab = new GameObject("RemoteActor");
        _prefab.SetActive(false);
        _prefab.AddComponent<RecordedActor>();
        _created.Add(_prefab);

        var clientTransport = CreateManager("RecorderRemoteClient", out _client);
        var serverTransport = CreateManager("RecorderRemoteServer", out _server);
        serverTransport.peer = clientTransport;
        clientTransport.peer = serverTransport;

        _recorder = _server.gameObject.AddComponent<NetworkRecorder>();
        Set(_recorder, "_recordOnServer", false);
        Set(_recorder, "_recordCamera", false);
        Set(_recorder, "_recordInput", false);
        Set(_recorder, "_recordUI", false);
        Set(_recorder, "_upload", false);
        Set(_recorder, "_directory", Path.GetFullPath(Path.Combine(Application.dataPath, "../test-results/recorder-tests")));
        Set(_recorder, "_networkManager", _server);

        ClearMainInstance();
        _client.gameObject.SetActive(true);
        ClearMainInstance();
        _server.gameObject.SetActive(true);
        NetworkManager.SetMainInstance(_server);

        int pings = 0;
        PlayerBroadcastDelegate<RecordedPing> onPing = (player, data, asServer) => pings++;
        _server.Subscribe(onPing, true);
        _client.Subscribe(onPing, false);

        _server.StartServer();
        yield return WaitUntil(() => _server.serverState == ConnectionState.Connected);
        _client.StartClient();
        yield return WaitUntil(() => _client.isLocalPlayerReady && _server.players.Count == 1);
        Assert.That(_client.isLocalPlayerReady && _server.players.Count == 1, Is.True, "the client joined");
        yield return Ticks(_server, 3);

        _recorder.StartRecording("network-recorder-remote.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");

        var serverCopy = Object.Instantiate(_prefab).GetComponent<RecordedActor>();
        serverCopy.gameObject.SetActive(true);
        _server.Spawn(serverCopy.gameObject);
        RecordedActor clientCopy = null;
        yield return WaitUntil(() => (clientCopy = ClientCopy(serverCopy)) != null);
        Assert.That(clientCopy, Is.Not.Null, "the client spawned its copy");
        yield return Ticks(_server, 3);

        var player = _client.localPlayer;
        clientCopy.Shoot(new RecordedShot { weapon = 2, direction = Vector3.forward }, "remote");
        serverCopy.Announce(5, new List<int> { 9 });
        serverCopy.Hidden(1);
        _client.SendToServer(new RecordedPing { value = 3, text = "from client" });
        _server.Send(player, new RecordedPing { value = 4, text = "to client" });
        yield return WaitUntil(() => serverCopy.shots == 1 && clientCopy.announced == 1 && clientCopy.hidden == 1 && pings == 2);
        Assert.That(serverCopy.shots, Is.EqualTo(1), "the ServerRpc ran on the server");
        Assert.That(clientCopy.announced, Is.EqualTo(1), "the ObserversRpc ran on the client");
        Assert.That(pings, Is.EqualTo(2), "both broadcasts arrived");

        serverCopy.GiveOwnership(player);
        yield return Ticks(_server, 3);

        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        _server.Unsubscribe(onPing, true);
        _client.Unsubscribe(onPing, false);

        Assert.That(RecordedCalls.enabled, Is.False, "nothing is handed to a stopped recorder");
    }
}

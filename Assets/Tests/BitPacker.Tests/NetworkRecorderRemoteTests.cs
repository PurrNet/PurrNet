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

    NetworkRecorder AddRecorder(NetworkManager manager)
    {
        var recorder = manager.gameObject.AddComponent<NetworkRecorder>();
        Set(recorder, "_recordOnServer", false);
        Set(recorder, "_recordInEditor", true);
        Set(recorder, "_recordCamera", false);
        Set(recorder, "_recordInput", false);
        Set(recorder, "_recordUI", false);
        Set(recorder, "_upload", false);
        Set(recorder, "_directory", Path.GetFullPath(Path.Combine(Application.dataPath, "../test-results/recorder-tests")));
        Set(recorder, "_networkManager", manager);
        return recorder;
    }

    [UnityTest]
    public IEnumerator AClientRecordsTheEffectsItPlaysInItsPart()
    {
        var shot = AudioClip.Create("Shot", 22050, 1, 44100, false);
        _created.Add(shot);
        _prefab = new GameObject("RemoteActor");
        _prefab.SetActive(false);
        _prefab.AddComponent<RecordedActor>();
        var main = _prefab.AddComponent<ParticleSystem>().main;
        main.playOnAwake = false;
        var gun = _prefab.AddComponent<AudioSource>();
        gun.playOnAwake = false;
        gun.clip = shot;
        _created.Add(_prefab);

        var clientTransport = CreateManager("RecorderRemoteClient", out _client);
        var serverTransport = CreateManager("RecorderRemoteServer", out _server);
        serverTransport.peer = clientTransport;
        clientTransport.peer = serverTransport;
        var clientRecorder = AddRecorder(_client);
        _recorder = AddRecorder(_server);

        ClearMainInstance();
        _client.gameObject.SetActive(true);
        ClearMainInstance();
        _server.gameObject.SetActive(true);
        NetworkManager.SetMainInstance(_server);

        _server.StartServer();
        yield return WaitUntil(() => _server.serverState == ConnectionState.Connected);
        _client.StartClient();
        yield return WaitUntil(() => _client.isLocalPlayerReady && _server.players.Count == 1);
        Assert.That(_client.isLocalPlayerReady && _server.players.Count == 1, Is.True, "the client joined");
        yield return Ticks(_server, 3);

        _recorder.StartRecording("network-recorder-remote-effects.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");

        var serverCopy = Object.Instantiate(_prefab).GetComponent<RecordedActor>();
        serverCopy.gameObject.SetActive(true);
        _server.Spawn(serverCopy.gameObject);
        RecordedActor clientCopy = null;
        yield return WaitUntil(() => (clientCopy = ClientCopy(serverCopy)) != null);
        Assert.That(clientCopy, Is.Not.Null, "the client spawned its copy");
        yield return Ticks(_client, 4);

        clientCopy.gameObject.SetActive(true);
        clientCopy.GetComponent<ParticleSystem>().Play();
        clientCopy.GetComponent<AudioSource>().PlayOneShot(shot);
        AudioSource.PlayClipAtPoint(shot, new Vector3(7, 8, 9));
        yield return Ticks(_client, 6);

        string partPath = clientRecorder.session.currentPath;
        Assert.That(clientRecorder.session.isPart, Is.True, "the client writes a part");
        string path = _recorder.currentPath;
        _recorder.StopRecording();
        yield return WaitUntil(() => File.Exists(path) && new FileInfo(path).Length > 0);
        yield return WaitUntil(() => !clientRecorder.session.isPart && File.Exists(partPath) && new FileInfo(partPath).Length > 0);

        var part = NetworkRecorderTests.ReadMeta(partPath);
        string dump = NetworkRecorderTests.Dump(part);
        ulong networkId = serverCopy.id!.Value.id.value;
        Assert.That(part.Exists(m => m.kind == RecordKind.Effects && m.json.Contains($"\"networkId\":{networkId},") &&
                                     m.json.Contains("\"clip\":\"Shot\"")), "the part names the object's effects by its network id:\n" + dump);
        foreach (var stream in new[] { "effect.particles", "effect.audio" })
            Assert.That(part.Exists(m => m.kind == RecordKind.Stream && m.json.Contains($"\"sample\":\"{stream}\"")),
                stream + " is one of the player's streams:\n" + dump);
        Assert.That(part.Find(m => m.kind == RecordKind.Footer).json, Does.Not.Contain("\"errors\""), "no errors in the part");

        var server = NetworkRecorderTests.ReadMeta(path);
        Assert.That(server.Exists(m => m.kind == RecordKind.Effects && m.json.Contains("\"object\":")), Is.False,
            "a dedicated server does not follow what only a client plays:\n" + NetworkRecorderTests.Dump(server));
    }

    [UnityTest]
    public IEnumerator AClientsPartsFollowTheServerFromPieceToPiece()
    {
        _prefab = new GameObject("RemoteActor");
        _prefab.SetActive(false);
        _prefab.AddComponent<RecordedActor>();
        _created.Add(_prefab);

        var clientTransport = CreateManager("RecorderRemoteClient", out _client);
        var serverTransport = CreateManager("RecorderRemoteServer", out _server);
        serverTransport.peer = clientTransport;
        clientTransport.peer = serverTransport;
        var clientRecorder = AddRecorder(_client);
        _recorder = AddRecorder(_server);

        ClearMainInstance();
        _client.gameObject.SetActive(true);
        ClearMainInstance();
        _server.gameObject.SetActive(true);
        NetworkManager.SetMainInstance(_server);

        _server.StartServer();
        yield return WaitUntil(() => _server.serverState == ConnectionState.Connected);
        _client.StartClient();
        yield return WaitUntil(() => _client.isLocalPlayerReady && _server.players.Count == 1);
        Assert.That(_client.isLocalPlayerReady && _server.players.Count == 1, Is.True, "the client joined");
        yield return Ticks(_server, 3);

        static bool HasFooter(string path) => NetworkRecorderTests.ReadMeta(path).Exists(m => m.kind == RecordKind.Footer);
        var pieces = new List<string>();
        var parts = new List<string>();
        var closed = new List<bool>();
        _recorder.onRecordingFinished += pieces.Add;
        clientRecorder.onRecordingFinished += path =>
        {
            parts.Add(path);
            closed.Add(HasFooter(path));
        };

        Set(_recorder, "_splitMinutes", 20f / (60f * _server.tickModule.tickRate));
        _recorder.StartRecording("network-recorder-remote-split.purrrec");
        Assert.That(_recorder.isRecording, Is.True, "recording started");
        int notes = 0;
        uint noted = 0;
        void Note()
        {
            uint tick = _client.tickModule.localTick;
            if (tick == noted)
                return;
            noted = tick;
            clientRecorder.WriteSample("note", notes++);
        }
        yield return WaitUntil(() =>
        {
            Note();
            return _recorder.splits >= 2;
        }, 30f);
        Assert.That(_recorder.splits, Is.GreaterThanOrEqualTo(2), "the recording was split");
        uint until = _server.tickModule.localTick + 6;
        string lastPart = null;
        yield return WaitUntil(() =>
        {
            Note();
            lastPart = clientRecorder.session.isPart ? clientRecorder.session.currentPath : null;
            return lastPart != null && !parts.Contains(lastPart) && parts.Count >= _recorder.splits && _server.tickModule.localTick >= until;
        });
        _recorder.StopRecording();
        int expected = _recorder.splits + 1;
        yield return WaitUntil(() => pieces.Count >= expected && parts.Count >= expected);
        Assert.That(pieces.Count, Is.EqualTo(expected), "every piece was finished");
        Assert.That(parts.Count, Is.EqualTo(expected), "and the client wrote a part for each of them");
        Assert.That(closed, Has.None.False, "each complete and closed when it was reported");

        static string Id(string path) => System.Text.RegularExpressions.Regex.Match(
            NetworkRecorderTests.ReadMeta(path).Find(m => m.kind == RecordKind.Header).json, "\"recordingId\":\"([^\"]*)\"").Groups[1].Value;
        ulong player = _client.localPlayer.id.value;
        for (int i = 0; i < expected; i++)
        {
            Assert.That(Id(parts[i]), Is.EqualTo(Id(pieces[i])), $"part {i + 1} belongs to piece {i + 1}");
            var part = NetworkRecorderTests.ReadMeta(parts[i]);
            Assert.That(part.Exists(m => m.kind == RecordKind.Stream && m.json.Contains("\"sample\":\"note\"")),
                $"part {i + 1} holds what the client wrote during that piece:\n" + NetworkRecorderTests.Dump(part));
            var piece = NetworkRecorderTests.ReadMeta(pieces[i]);
            Assert.That(piece.Exists(m => m.kind == RecordKind.Part && m.json.Contains($"\"player\":{player},")),
                $"piece {i + 1} vouches for the client's part:\n" + NetworkRecorderTests.Dump(piece));
        }
    }
}

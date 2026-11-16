using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class SyncListOrderingTestIdentity : NetworkIdentity
{
    public SyncList<int> serverAuthList = new SyncList<int>();
    public SyncList<int> ownerAuthList = new SyncList<int>(ownerAuth: true);
}

/// <summary>
/// In-memory transport for a server-only and a client-only NetworkManager in the same process.
/// One instance listens, the other connects to it; packets are delivered in order on the next pump.
/// </summary>
public class PairedTestTransport : GenericTransport, ITransport
{
    static readonly Connection Conn = new Connection(1);
    static readonly List<Connection> ConnectedList = new List<Connection> { Conn };
    static readonly List<Connection> EmptyList = new List<Connection>();

    public PairedTestTransport peer;

    public event OnConnected onConnected;
    public event OnDisconnected onDisconnected;
    public event OnDataReceived onDataReceived;
    public event OnDataSent onDataSent;
    public event OnConnectionState onConnectionState;

    readonly Queue<ByteData> _serverInbox = new Queue<ByteData>();
    readonly Queue<ByteData> _clientInbox = new Queue<ByteData>();

    public override bool isSupported => true;
    public override ITransport transport => this;

    public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;
    public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;

    bool clientLinked => peer && peer.clientState == ConnectionState.Connected;

    public IReadOnlyList<Connection> connections =>
        listenerState == ConnectionState.Connected && clientLinked ? ConnectedList :
        clientState == ConnectionState.Connected ? ConnectedList : EmptyList;

    protected override void StartServerInternal() => Listen(default);

    protected override void StartClientInternal() => Connect(default, default);

    public void Listen(ushort port)
    {
        SetListenerState(ConnectionState.Connected);
    }

    public void StopListening()
    {
        if (clientLinked)
            peer.DropClient(DisconnectReason.ServerRequest);
        SetListenerState(ConnectionState.Disconnected);
    }

    public void Connect(string ip, ushort port)
    {
        if (clientState == ConnectionState.Connected)
            return;

        SetClientState(ConnectionState.Connecting);

        if (!peer || peer.listenerState != ConnectionState.Connected)
        {
            SetClientState(ConnectionState.Disconnected);
            return;
        }

        SetClientState(ConnectionState.Connected);
        peer.onConnected?.Invoke(Conn, true);
        onConnected?.Invoke(Conn, false);
    }

    public void Disconnect()
    {
        if (clientState != ConnectionState.Connected)
        {
            SetClientState(ConnectionState.Disconnected);
            return;
        }

        DropClient(DisconnectReason.ClientRequest);
    }

    void DropClient(DisconnectReason reason)
    {
        SetClientState(ConnectionState.Disconnected);
        _clientInbox.Clear();
        onDisconnected?.Invoke(Conn, reason, false);
        if (peer)
        {
            peer._serverInbox.Clear();
            peer.onDisconnected?.Invoke(Conn, reason, true);
        }
    }

    public void CloseConnection(Connection conn)
    {
        if (clientLinked)
            peer.DropClient(DisconnectReason.ServerRequest);
    }

    public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
    {
        if (listenerState != ConnectionState.Connected || !clientLinked)
            return;
        peer._clientInbox.Enqueue(data.Duplicate());
        RaiseDataSent(target, data, true);
    }

    public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
    {
        if (clientState != ConnectionState.Connected || !peer)
            return;
        peer._serverInbox.Enqueue(data.Duplicate());
        RaiseDataSent(default, data, false);
    }

    public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) =>
        onDataReceived?.Invoke(conn, data, asServer);

    public void RaiseDataSent(Connection conn, ByteData data, bool asServer) =>
        onDataSent?.Invoke(conn, data, asServer);

    public void ReceiveMessages(float delta)
    {
        while (_serverInbox.Count > 0)
            onDataReceived?.Invoke(Conn, _serverInbox.Dequeue(), true);

        while (_clientInbox.Count > 0)
            onDataReceived?.Invoke(default, _clientInbox.Dequeue(), false);
    }

    public void SendMessages(float delta) { }

    void SetListenerState(ConnectionState state)
    {
        if (listenerState == state)
            return;
        listenerState = state;
        onConnectionState?.Invoke(state, true);
    }

    void SetClientState(ConnectionState state)
    {
        if (clientState == state)
            return;
        clientState = state;
        onConnectionState?.Invoke(state, false);
    }
}

/// <summary>
/// Runs a server-only and a client-only NetworkManager in one process so a SyncList can be observed
/// on the peer that did not modify it. Covers index-based removal with duplicate values and the
/// ordering of an indexer set against operations queued earlier in the same tick.
/// </summary>
public class SyncListOrderingTests
{
    const float Timeout = 10f;

    readonly List<Object> _created = new List<Object>();
    NetworkManager _server;
    NetworkManager _client;
    GameObject _prefab;

    public enum Scenario
    {
        RemoveAtDuplicateValue,
        RemoveValueWithDuplicate,
        RemoveAtThenSet
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_client)
            _client.StopClient();
        if (_server)
            _server.StopServer();
        for (int i = 0; i < 5; i++)
            yield return null;

#if UNITY_6000_4_OR_NEWER
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include))
#else
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
        {
            if (identity)
                Object.DestroyImmediate(identity.gameObject);
        }

        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
        _server = null;
        _client = null;
        _prefab = null;
    }

    [UnityTest]
    public IEnumerator ServerAuth_RemoteMatchesWriter([Values] Scenario scenario)
    {
        yield return RunScenario(scenario, ownerAuth: false);
    }

    [UnityTest]
    public IEnumerator OwnerAuth_ServerMatchesOwner([Values] Scenario scenario)
    {
        yield return RunScenario(scenario, ownerAuth: true);
    }

    IEnumerator RunScenario(Scenario scenario, bool ownerAuth)
    {
        yield return StartPair();

        var serverCopy = Object.Instantiate(_prefab).GetComponent<SyncListOrderingTestIdentity>();
        serverCopy.gameObject.SetActive(true);
        _server.Spawn(serverCopy.gameObject);
        Assert.That(serverCopy.isSpawned && serverCopy.networkManager == _server, Is.True, "server did not spawn the test identity");

        SyncListOrderingTestIdentity clientCopy = null;
        yield return WaitUntil(() => (clientCopy = FindClientCopy(serverCopy)) != null);
        Assert.That(clientCopy, Is.Not.Null, "client never spawned its copy of the test identity");

        if (ownerAuth)
        {
            serverCopy.GiveOwnership(_client.localPlayer);
            yield return WaitUntil(() => clientCopy.isOwner);
            Assert.That(clientCopy.isOwner, Is.True, "client never became owner");
        }

        var writer = ownerAuth ? clientCopy.ownerAuthList : serverCopy.serverAuthList;
        var reader = ownerAuth ? serverCopy.ownerAuthList : clientCopy.serverAuthList;

        int[] seed = scenario == Scenario.RemoveAtThenSet ? new[] { 1, 2 } : new[] { 5, 3, 5 };
        foreach (var value in seed)
            writer.Add(value);

        yield return WaitUntil(() => Matches(reader, seed));
        Assert.That(reader.list, Is.EqualTo(seed), "reader never received the seed");
        // Let the full-state resend that follows every flush go through before recording.
        yield return Ticks(_server, 5);

        var events = new List<string>();
        reader.onChanged += change => events.Add(Describe(change));

        int[] expected;
        string[] expectedEvents;

        switch (scenario)
        {
            case Scenario.RemoveAtDuplicateValue:
                writer.RemoveAt(2);
                expected = new[] { 5, 3 };
                expectedEvents = new[] { "Removed@2=5" };
                break;
            case Scenario.RemoveValueWithDuplicate:
                writer.Remove(5);
                expected = new[] { 3, 5 };
                expectedEvents = new[] { "Removed@0=5" };
                break;
            default:
                writer.RemoveAt(0);
                writer[0] = 9;
                expected = new[] { 9 };
                expectedEvents = new[] { "Removed@0=1", "Set@0=9" };
                break;
        }

        Assert.That(writer.list, Is.EqualTo(expected), "writer's own list is wrong");

        // Wait for the ops to arrive, then for the full-state resend, so late corrections show up as extra events.
        yield return WaitUntil(() => events.Count >= expectedEvents.Length);
        yield return Ticks(_server, 5);

        Assert.That(reader.list, Is.EqualTo(expected), "reader's list diverged from the writer");
        Assert.That(string.Join(" ", events), Is.EqualTo(string.Join(" ", expectedEvents)),
            "reader's onChanged events do not match the writer's operations");
    }

    IEnumerator StartPair()
    {
        _prefab = new GameObject("SyncListOrderingTestPrefab");
        _prefab.SetActive(false);
        _prefab.AddComponent<SyncListOrderingTestIdentity>();
        _created.Add(_prefab);

        var clientTransport = CreateManager("SyncListOrderingClient", out _client);
        var serverTransport = CreateManager("SyncListOrderingServer", out _server);
        serverTransport.peer = clientTransport;
        clientTransport.peer = serverTransport;

        // NetworkManager.Awake destroys any manager that wakes while another one is main,
        // so clear main before waking each of the two.
        ClearMainInstance();
        _client.gameObject.SetActive(true);
        ClearMainInstance();
        _server.gameObject.SetActive(true);
        NetworkManager.SetMainInstance(_server);

        _server.StartServer();
        yield return WaitUntil(() => _server.serverState == ConnectionState.Connected);
        _client.StartClient();
        yield return WaitUntil(() => _client.isLocalPlayerReady && _server.players.Count == 1);
        Assert.That(_client.isLocalPlayerReady && _server.players.Count == 1, Is.True,
            $"client did not join the server: server={_server.serverState}, client={_client.clientState}, " +
            $"players={_server.players.Count}, localPlayerReady={_client.isLocalPlayerReady}");
        yield return Ticks(_server, 5);
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
        provider.AddRuntimePrefab("synclist-ordering-test", _prefab);

        manager.transport = transport;
        return transport;
    }

    SyncListOrderingTestIdentity FindClientCopy(SyncListOrderingTestIdentity serverCopy)
    {
#if UNITY_6000_4_OR_NEWER
        foreach (var identity in Object.FindObjectsByType<SyncListOrderingTestIdentity>(FindObjectsInactive.Include))
#else
        foreach (var identity in Object.FindObjectsByType<SyncListOrderingTestIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#endif
        {
            if (identity != serverCopy && identity.isSpawned && identity.networkManager == _client &&
                identity.id == serverCopy.id)
                return identity;
        }

        return null;
    }

    static string Describe(SyncListChange<int> change) => $"{change.operation}@{change.index}={change.value}";

    static bool Matches(SyncList<int> list, int[] expected)
    {
        if (list.Count != expected.Length)
            return false;
        for (int i = 0; i < expected.Length; i++)
            if (list[i] != expected[i])
                return false;
        return true;
    }

    static IEnumerator WaitUntil(System.Func<bool> condition)
    {
        var deadline = Time.realtimeSinceStartup + Timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    static IEnumerator Ticks(NetworkManager manager, int count)
    {
        var tickModule = manager.tickModule;
        uint until = tickModule.localTick + (uint)count;
        var deadline = Time.realtimeSinceStartup + Timeout;
        while (tickModule.localTick < until && Time.realtimeSinceStartup < deadline)
            yield return null;
    }
}

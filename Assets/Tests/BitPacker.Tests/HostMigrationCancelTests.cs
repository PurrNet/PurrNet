using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class HostMigrationCancelTests
{
    readonly List<Object> _created = new();
    NetworkManager _manager;
    HostMigrationTestTransport _transport;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_manager)
        {
            _manager.CancelHostMigration();
            _transport.disconnectSeconds = 0;
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
        _transport = null;
    }

    IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            yield return null;
    }

    IEnumerator WaitSeconds(float seconds)
    {
        var until = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < until)
            yield return null;
    }

    // Promotion moves the client's modules over to the server, so every test starts from a connected client.
    IEnumerator StartConnectedClient()
    {
        var go = new GameObject("HostMigrationNetworkManager");
        go.SetActive(false);
        _created.Add(go);

        _transport = go.AddComponent<HostMigrationTestTransport>();
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

        _manager.transport = _transport;
        go.SetActive(true);

        _manager.StartClient();
        yield return WaitUntil(() => _manager.clientState == ConnectionState.Connected, 5f);
        Assert.That(_manager.clientState, Is.EqualTo(ConnectionState.Connected), "test client did not connect");
    }

    IEnumerator AssertStoppedAndReusable()
    {
        yield return WaitUntil(() => _manager.clientState == ConnectionState.Disconnected &&
                                     _manager.serverState == ConnectionState.Disconnected, 6f);
        Assert.That(_manager.clientState, Is.EqualTo(ConnectionState.Disconnected));
        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Disconnected));

        _transport.listenFailuresLeft = 0;
        _manager.StartServer();
        yield return WaitUntil(() => _manager.serverState == ConnectionState.Connected, 3f);
        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Connected), "the manager could not host again");
    }

    [UnityTest]
    public IEnumerator PromoteToServer_Succeeds()
    {
        yield return StartConnectedClient();

        // Also shows a cancel made while nothing is migrating does not reach a migration that starts later.
        _manager.CancelHostMigration();
        _transport.listenFailuresLeft = 1;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);

        Assert.That(_manager.isPromotingToServer, Is.False);
        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(_transport.listenAttempts, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator PromoteToServer_SecondCallThenCancel_StopsTheRunningPromotion()
    {
        yield return StartConnectedClient();

        _transport.listenFailuresLeft = int.MaxValue;
        _manager.PromoteToServer();
        _manager.PromoteToServer();
        Assert.That(_manager.isPromotingToServer, Is.True, "the second call cleared the running promotion's flag");

        yield return WaitUntil(() => _transport.listenAttempts >= 2, 3f);
        Assert.That(_transport.listenAttempts, Is.GreaterThanOrEqualTo(2), "the promotion was not retrying");

        _manager.CancelHostMigration();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 1f);
        Assert.That(_manager.isPromotingToServer, Is.False, "the cancel did not reach the running promotion");

        var attempts = _transport.listenAttempts;
        yield return WaitSeconds(0.5f);
        Assert.That(_transport.listenAttempts, Is.EqualTo(attempts), "the promotion kept retrying after the cancel");

        yield return AssertStoppedAndReusable();
    }

    [UnityTest]
    public IEnumerator TransferToNewServer_SecondCallThenCancel_StopsTheRunningTransfer()
    {
        yield return StartConnectedClient();

        _transport.clientConnects = false;
        var connectsBefore = _transport.connectAttempts;
        _manager.TransferToNewServer();
        _manager.TransferToNewServer();
        Assert.That(_manager.isTranferingToNewServer, Is.True, "the second call cleared the running transfer's flag");

        yield return WaitUntil(() => _transport.connectAttempts > connectsBefore, 3f);
        Assert.That(_transport.connectAttempts, Is.GreaterThan(connectsBefore), "the transfer never tried to connect");

        _manager.CancelHostMigration();
        yield return WaitUntil(() => !_manager.isTranferingToNewServer, 1f);
        Assert.That(_manager.isTranferingToNewServer, Is.False, "the cancel did not reach the running transfer");

        var attempts = _transport.connectAttempts;
        yield return WaitSeconds(2.5f);
        Assert.That(_transport.connectAttempts, Is.EqualTo(attempts), "the transfer kept retrying after the cancel");

        yield return AssertStoppedAndReusable();
    }

    [UnityTest]
    public IEnumerator TransferToNewServer_TimeoutShorterThanRetryInterval_GivesUpOnTime()
    {
        yield return StartConnectedClient();

        _transport.clientConnects = false;
        _manager.hostMigrationTimeout = 0.5f;
        var connectsBefore = _transport.connectAttempts;
        var startedAt = Time.realtimeSinceStartupAsDouble;
        _manager.TransferToNewServer();

        yield return WaitUntil(() => !_manager.isTranferingToNewServer, 5f);
        var elapsed = Time.realtimeSinceStartupAsDouble - startedAt;

        Assert.That(_manager.isTranferingToNewServer, Is.False);
        // The transfer retries every 2 seconds; it must not wait for that retry to notice the deadline.
        Assert.That(elapsed, Is.LessThan(1.5), "the transfer only gave up at the next retry");
        Assert.That(_transport.connectAttempts - connectsBefore, Is.AtMost(1),
            "the transport was restarted after the deadline");

        yield return AssertStoppedAndReusable();
    }

    [UnityTest]
    public IEnumerator PromoteToServer_CancelDuringRetryWait_DoesNotRestartOrAcceptTheServer()
    {
        yield return StartConnectedClient();

        // The first listen fails and the retry 0.1 seconds later would succeed.
        _transport.listenFailuresLeft = 1;
        _manager.PromoteToServer();
        yield return WaitUntil(() => _transport.listenAttempts == 1, 3f);
        Assert.That(_transport.listenAttempts, Is.EqualTo(1));

        _manager.CancelHostMigration();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 1f);
        yield return WaitSeconds(0.3f);

        Assert.That(_transport.listenAttempts, Is.EqualTo(1), "the promotion restarted the server after the cancel");
        Assert.That(_manager.isServer, Is.False, "the promotion accepted a server after the cancel");

        yield return AssertStoppedAndReusable();
    }

    [UnityTest]
    public IEnumerator TransferToNewServer_CancelWhileDisconnectCompletesLater_StopsWithoutReconnecting()
    {
        yield return StartConnectedClient();

        _transport.clientConnects = false;
        var connectsBefore = _transport.connectAttempts;
        _manager.TransferToNewServer();
        yield return WaitUntil(() => _transport.connectAttempts == connectsBefore + 1, 3f);
        Assert.That(_transport.connectAttempts, Is.EqualTo(connectsBefore + 1));

        // The retry two seconds in stops the transport, and this one takes a second to report Disconnected.
        _transport.disconnectSeconds = 1f;
        yield return WaitUntil(() => _transport.clientState == ConnectionState.Disconnecting, 4f);
        Assert.That(_transport.clientState, Is.EqualTo(ConnectionState.Disconnecting), "the transfer never retried");

        _manager.CancelHostMigration();
        yield return null;
        yield return null;
        Assert.That(_manager.isTranferingToNewServer, Is.False, "the cancel waited for the transport to disconnect");

        yield return WaitUntil(() => _transport.clientState == ConnectionState.Disconnected, 3f);
        yield return WaitSeconds(0.3f);
        Assert.That(_transport.connectAttempts, Is.EqualTo(connectsBefore + 1),
            "the transfer reconnected after the cancel");

        _transport.disconnectSeconds = 0;
        yield return AssertStoppedAndReusable();
    }
}

// A transport whose new host is slow or missing: listening can fail a set number of times, connecting can stay in
// Connecting for good, and a client disconnect can take a while to report Disconnected.
public class HostMigrationTestTransport : GenericTransport, ITransport
{
    public int listenFailuresLeft;
    public bool clientConnects = true;
    public float disconnectSeconds;

    public int listenAttempts { get; private set; }
    public int connectAttempts { get; private set; }

    static readonly Connection ServerConnection = new Connection(0);
    bool _connected;
    double _disconnectAt = -1;

#pragma warning disable CS0067
    public event OnConnected onConnected;
    public event OnDisconnected onDisconnected;
    public event OnDataReceived onDataReceived;
    public event OnDataSent onDataSent;
#pragma warning restore CS0067
    public event OnConnectionState onConnectionState;

    public override bool isSupported => true;
    public override ITransport transport => this;

    public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;
    public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;
    public IReadOnlyList<Connection> connections => Array.Empty<Connection>();

    protected override void StartServerInternal() => Listen(default);

    protected override void StartClientInternal() => Connect(default, default);

    public void Listen(ushort port)
    {
        listenAttempts++;
        SetListenerState(ConnectionState.Connecting);

        if (listenFailuresLeft > 0)
        {
            listenFailuresLeft--;
            SetListenerState(ConnectionState.Disconnected);
        }
        else SetListenerState(ConnectionState.Connected);
    }

    public void StopListening() => SetListenerState(ConnectionState.Disconnected);

    public void Connect(string ip, ushort port)
    {
        connectAttempts++;
        SetClientState(ConnectionState.Connecting);

        if (!clientConnects)
            return;

        _connected = true;
        SetClientState(ConnectionState.Connected);
        onConnected?.Invoke(ServerConnection, false);
    }

    public void Disconnect()
    {
        if (clientState is ConnectionState.Disconnected or ConnectionState.Disconnecting)
            return;

        SetClientState(ConnectionState.Disconnecting);

        if (disconnectSeconds > 0)
            _disconnectAt = Time.unscaledTimeAsDouble + disconnectSeconds;
        else FinishDisconnect();
    }

    public void UnityUpdate(float delta)
    {
        if (_disconnectAt >= 0 && Time.unscaledTimeAsDouble >= _disconnectAt)
            FinishDisconnect();
    }

    void FinishDisconnect()
    {
        _disconnectAt = -1;
        SetClientState(ConnectionState.Disconnected);

        if (_connected)
        {
            _connected = false;
            onDisconnected?.Invoke(ServerConnection, DisconnectReason.ClientRequest, false);
        }
    }

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

    public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) =>
        onDataReceived?.Invoke(conn, data, asServer);

    public void RaiseDataSent(Connection conn, ByteData data, bool asServer) =>
        onDataSent?.Invoke(conn, data, asServer);

    public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered) { }

    public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered) { }

    public void CloseConnection(Connection conn) { }

    public void ReceiveMessages(float delta) { }

    public void SendMessages(float delta) { }
}

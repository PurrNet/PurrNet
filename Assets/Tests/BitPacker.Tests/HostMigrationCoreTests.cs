using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public sealed class HostMigrationCoreTestTransport : GenericTransport, ITransport, IHostMigrationTransport
{
    public readonly struct CapturedClientPacket
    {
        public readonly Connection connection;
        public readonly Channel channel;
        public readonly byte[] data;

        public CapturedClientPacket(Connection connection, Channel channel, byte[] data)
        {
            this.connection = connection;
            this.channel = channel;
            this.data = data;
        }
    }

    private readonly List<Connection> _connections = new();
    private ConnectionState _clientState = ConnectionState.Disconnected;
    private ConnectionState _listenerState = ConnectionState.Disconnected;

    public bool holdClientConnecting;
    public bool holdServerConnecting;
    public bool prepared = true;
    public int cancelPreparedCount;
    public int activatePreparedCount;
    public bool hasIndeterminateHostMigrationActivation { get; private set; }
    public HostMigrationTransportActivationResult activationResult { get; set; } =
        new(HostMigrationTransportActivationStatus.Succeeded);
    public Action beforeActivationCompletes;
    public bool captureClientPackets;
    public readonly List<CapturedClientPacket> sentToClients = new();

    public override bool isSupported => true;
    public override ITransport transport => this;
    public ConnectionState clientState => _clientState;
    public ConnectionState listenerState => _listenerState;
    public IReadOnlyList<Connection> connections => _connections;

    public event OnConnected onConnected;
    public event OnDisconnected onDisconnected;
#pragma warning disable CS0067
    public event OnDataReceived onDataReceived;
    public event OnDataSent onDataSent;
#pragma warning restore CS0067
    public event OnConnectionState onConnectionState;

    protected override void StartClientInternal() => Connect(null, 0);
    protected override void StartServerInternal() => Listen(0);

    public void Connect(string ip, ushort port)
    {
        SetClientState(ConnectionState.Connecting);
        if (holdClientConnecting)
            return;
        SetClientState(ConnectionState.Connected);
        onConnected?.Invoke(new Connection(1), false);
    }

    public void Disconnect()
    {
        if (_clientState == ConnectionState.Disconnected)
            return;
        SetClientState(ConnectionState.Disconnecting);
        SetClientState(ConnectionState.Disconnected);
        onDisconnected?.Invoke(new Connection(1), DisconnectReason.ClientRequest, false);
    }

    public void Listen(ushort port)
    {
        SetListenerState(ConnectionState.Connecting);
        if (holdServerConnecting)
            return;
        SetListenerState(ConnectionState.Connected);
    }

    public void StopListening()
    {
        if (_listenerState == ConnectionState.Disconnected)
            return;
        SetListenerState(ConnectionState.Disconnecting);
        SetListenerState(ConnectionState.Disconnected);
    }

    public void SimulateRemoteDisconnect(DisconnectReason reason)
    {
        SetClientState(ConnectionState.Disconnecting);
        SetClientState(ConnectionState.Disconnected);
        onDisconnected?.Invoke(new Connection(1), reason, false);
    }

    public void SimulateRemoteDisconnectReasonBeforeState(DisconnectReason reason)
    {
        onDisconnected?.Invoke(new Connection(1), reason, false);
    }

    private void SetClientState(ConnectionState state)
    {
        if (_clientState == state)
            return;
        _clientState = state;
        onConnectionState?.Invoke(state, false);
    }

    private void SetListenerState(ConnectionState state)
    {
        if (_listenerState == state)
            return;
        _listenerState = state;
        onConnectionState?.Invoke(state, true);
    }

    public void CancelPreparedHostMigration()
    {
        prepared = false;
        hasIndeterminateHostMigrationActivation = false;
        cancelPreparedCount++;
    }

    public bool TryGetHostMigrationFailure(bool asServer, out string failure)
    {
        failure = null;
        return false;
    }

    public Task<HostMigrationTransportActivationResult> ActivatePreparedHostMigrationAsync(
        float timeoutSeconds, CancellationToken cancellationToken = default)
    {
        activatePreparedCount++;
        beforeActivationCompletes?.Invoke();
        hasIndeterminateHostMigrationActivation =
            activationResult.status == HostMigrationTransportActivationStatus.Indeterminate;
        return Task.FromResult(activationResult);
    }

    public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) { }
    public void RaiseDataSent(Connection conn, ByteData data, bool asServer) { }
    public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
    {
        if (!captureClientPackets)
            return;

        var copy = new byte[data.length];
        Buffer.BlockCopy(data.data, data.offset, copy, 0, data.length);
        sentToClients.Add(new CapturedClientPacket(target, method, copy));
    }
    public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered) { }
    public void CloseConnection(Connection conn) { }
    public void ReceiveMessages(float delta) { }
    public void SendMessages(float delta) { }
}

public sealed class HostMigrationCleanupProbeModule : INetworkModule
{
    public int disableCount;

    public void Enable(bool asServer) { }
    public void Disable(bool asServer) => disableCount++;
}

public class HostMigrationCoreTests
{
    [Test]
    public void PeerMigrationEndpoint_AllowsOpaqueAddressWithoutPort()
    {
        var endpoint = new PeerMigrationEndpoint("platform-user-42");

        Assert.That(endpoint.isValid, Is.True);
        Assert.That(endpoint.address, Is.EqualTo("platform-user-42"));
        Assert.That(endpoint.port, Is.Zero);
        Assert.That(default(PeerMigrationEndpoint).isValid, Is.False);
    }

    private readonly List<UnityEngine.Object> _created = new();
    private readonly List<Scene> _loadedTestScenes = new();
    private Scene? _activeSceneToRestore;

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                UnityEngine.Object.DestroyImmediate(_created[i]);
        }
        _created.Clear();
    }

    [UnityTearDown]
    public IEnumerator UnloadTestScenes()
    {
        if (_activeSceneToRestore.HasValue)
        {
            var restore = _activeSceneToRestore.Value;
            if (restore.IsValid() && restore.isLoaded)
                SceneManager.SetActiveScene(restore);
            _activeSceneToRestore = null;
        }

        for (var i = _loadedTestScenes.Count - 1; i >= 0; i--)
        {
            var scene = _loadedTestScenes[i];
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorSceneManager.CloseScene(scene, true);
                continue;
            }
#endif
            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null)
                yield return unload;
        }

        _loadedTestScenes.Clear();
    }

    [Test]
    public void ClientDisconnectClassification_IsOrderedAndExactlyOnce()
    {
        var (manager, transport) = CreateManager();
        ClientDisconnectInfo? received = null;
        int count = 0;
        manager.onClientDisconnected += info =>
        {
            received = info;
            count++;
        };

        transport.Connect(null, 0);
        transport.SimulateRemoteDisconnect(DisconnectReason.ClientRequest);
        transport.SimulateRemoteDisconnect(DisconnectReason.ClientRequest);

        Assert.That(count, Is.EqualTo(1));
        Assert.That(received.HasValue, Is.True);
        Assert.That(received.Value.wasLocalRequest, Is.False);
        Assert.That(received.Value.reason, Is.EqualTo(DisconnectReason.ServerRequest),
            "A UTP-style ClientRequest without a PurrNet StopClient intent is remote.");
    }

    [Test]
    public void ListenHostClientLogin_DoesNotEraseAuthoritativeMigrationSession()
    {
        var (manager, _) = CreateManager();
        var authoritative = new HostMigrationTransitionOptions("room-incarnation", 8);
        manager.ConfigureHostMigrationSession(authoritative);
        typeof(NetworkManager).GetField("<isServer>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, true);

        manager.ReceiveHostMigrationSession(authoritative);

        Assert.That(manager.hostMigrationSession, Is.EqualTo(authoritative));
        Assert.That(manager.isHostMigrationSessionValidated, Is.False,
            "The fresh listen client has no transfer expectation, but it must not mutate server scope.");
    }

    [Test]
    public void PromotedListenClient_SeedsRetainedLiveConnectionCookieBeforeAuthentication()
    {
        var (manager, _) = CreateManager();
        manager.cookieScope = CookieScope.LiveWithConnection;
        typeof(NetworkManager).GetField("<isPromotingToServer>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, true);
        typeof(NetworkManager).GetField("_promotedListenClientConnectionCookie",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, "retained-connection-cookie");
        var modules = new ModulesCollection(manager, false);
        manager.RegisterModules(modules, false);

        Assert.That(modules.TryGetModule(out AuthModule auth), Is.True);
        Assert.That(auth.clientConnectionCookie, Is.EqualTo("retained-connection-cookie"));
    }

    [Test]
    public void MigrationAuthentication_AllowsMissingScopedClaimAsOrdinaryAdmission()
    {
        var (manager, _) = CreateManager();
        manager.ConfigureHostMigrationSession(new HostMigrationTransitionOptions(
            "room-incarnation", 3, new[] { new PlayerID(1, false) }));
        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var denied = 0;
        var connected = 0;
        auth.onAuthenticationDenied += (_, _, _) => denied++;
        auth.onConnection += (_, _) => connected++;

        var onRequest = typeof(AuthModule).GetMethod("OnNonAuthRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onRequest, Is.Not.Null);
        onRequest.Invoke(auth, new object[]
        {
            new Connection(17),
            new PurrNet.Authentication.AuthenticationRequest
            {
                version = NetworkManager.version,
                cookie = "application-cookie"
            },
            true
        });

        Assert.That(denied, Is.Zero);
        Assert.That(connected, Is.EqualTo(1),
            "A missing migration claim must not let one pending peer block unrelated joins.");
    }

    [Test]
    public void OrdinaryAuthentication_RetainsApplicationCookieContinuity()
    {
        var (manager, _) = CreateManager();
        var players = CreatePlayersManager(manager, true);
        var onClientAuthed = typeof(PlayersManager).GetMethod("OnClientAuthed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onClientAuthed, Is.Not.Null);

        onClientAuthed.Invoke(players, new object[]
        {
            new Connection(21),
            new PurrNet.Authentication.AuthenticationResponse
            {
                success = true,
                cookie = "legacy-application-cookie"
            }
        });

        var cookieMap = typeof(PlayersManager).GetField("_cookieToPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary;

        Assert.That(cookieMap, Is.Not.Null);
        Assert.That(cookieMap.Contains("legacy-application-cookie"), Is.True,
            "Ordinary authentication must retain legacy application-cookie continuity.");
    }

    [Test]
    public void ExactPromotedListenClient_RejectsDifferentPlayerId()
    {
        var (manager, _) = CreateManager();
        var serverPlayers = CreatePlayersManager(manager, true);
        var clientPlayers = CreatePlayersManager(manager, false);
        var retained = new PlayerID(4, false);
        var replacement = new PlayerID(5, false);

        typeof(PlayersManager).GetField("_promotedLocalPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(serverPlayers, retained);
        typeof(PlayersManager).GetField("<localPlayerId>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(clientPlayers, replacement);
        typeof(NetworkManager).GetField("_serverPlayersManager",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, serverPlayers);
        typeof(NetworkManager).GetField("_clientPlayersManager",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, clientPlayers);

        var validate = typeof(NetworkManager).GetMethod("TryValidatePromotedListenClientIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(validate, Is.Not.Null);
        var args = new object[] { null };
        Assert.That(validate.Invoke(manager, args), Is.False);
        Assert.That(args[0]?.ToString(), Does.Contain(retained.ToString()));
    }

    [Test]
    public void ExpectedRoster_RequiresExactReadyOrExplicitAuthoritativeDeparture()
    {
        var (manager, _) = CreateManager();
        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var first = new PlayerID(4, false);
        var second = new PlayerID(7, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 9,
            new[] { first, second });
        AddRetainedPlayers(players, first, second);

        var begin = typeof(PlayersManager).GetMethod("BeginHostMigrationRoster",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(begin, Is.Not.Null);
        begin.Invoke(players, new object[] { transition });

        Assert.That(players.pendingHostMigrationPlayers, Is.EquivalentTo(new[] { first, second }));
        Assert.That(players.retainedHostMigrationPlayers,
            Is.EquivalentTo(new[] { first, second }));
        Assert.That(players.IsPendingRetainedHostMigrationPlayer(first, transition), Is.True);
        Assert.That(players.AcceptHostMigrationPlayerReady(
            first, transition, out var becameReady), Is.True);
        Assert.That(becameReady, Is.True);
        Assert.That(players.AcceptHostMigrationPlayerReady(
            first, transition, out becameReady), Is.True);
        Assert.That(becameReady, Is.False,
            "Duplicate scoped readiness must be idempotent.");
        Assert.That(players.IsActiveRetainedHostMigrationPlayer(first, transition), Is.True);
        Assert.That(players.IsPendingRetainedHostMigrationPlayer(first, transition), Is.False,
            "A ready retained player must return to ordinary scene-load behavior even while " +
            "the manager keeps advertising the completed migration session.");
        Assert.That(players.IsPendingRetainedHostMigrationPlayer(
            new PlayerID(99, false), transition), Is.False,
            "A post-migration fresh join is never an exact retained snapshot target.");
        Assert.That(players.pendingHostMigrationPlayers, Is.EqualTo(new[] { second }));
        Assert.That(players.retainedHostMigrationPlayers,
            Is.EquivalentTo(new[] { first, second }),
            "Readiness is not an authoritative membership departure.");

        var removed = players.FinalizeHostMigrationRoster(transition,
            new[] { first, new PlayerID(99, false) });

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(players.pendingHostMigrationPlayers, Is.Empty);
        Assert.That(players.retainedHostMigrationPlayers, Is.EqualTo(new[] { first }));
        Assert.That(players.ConfirmHostMigrationPlayerDeparture(second, transition), Is.False,
            "A finalized departure cannot be replayed.");
        Assert.That(players.ConfirmHostMigrationPlayerDeparture(first,
            new HostMigrationTransitionOptions("room-incarnation", 10)), Is.False,
            "A stale epoch cannot mutate the retained roster.");
    }

    [Test]
    public void SameTransitionReconnectReplacesTransactionAndReacknowledgesLoadedScene()
    {
        var (manager, _) = CreateManager();
        var players = CreatePlayersManager(manager, true);
        var player = new PlayerID(21, false);
        var transition = new HostMigrationTransitionOptions(
            "reconnect-topology", 6, new[] { player });
        AddRetainedPlayers(players, player);
        var firstScene = new SceneID(71);
        var secondScene = new SceneID(72);

        var beginRoster = typeof(PlayersManager).GetMethod("BeginHostMigrationRoster",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(beginRoster, Is.Not.Null);
        beginRoster.Invoke(players, new object[] { transition });
        typeof(NetworkManager).GetField("_hostMigrationSession",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, transition);

        var scenes = new ScenesModule(manager, players);
        var scenePlayers = new ScenePlayersModule(manager, scenes, players);
        scenes.SetScenePlayers(scenePlayers);
        var factory = new HierarchyFactory(manager, scenes, scenePlayers, players);
        var hierarchies = typeof(HierarchyFactory).GetField("_hierarchies",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(factory) as IDictionary<SceneID, HierarchyV2>;
        Assert.That(hierarchies, Is.Not.Null);
        hierarchies.Add(firstScene, null);
        hierarchies.Add(secondScene, null);

        var serverModules = new ModulesCollection(manager, true);
        serverModules.AddModule(factory);
        typeof(NetworkManager).GetField("_serverModules",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, serverModules);

        Assert.That(factory.RegisterExactOutboundSceneSet(player, transition,
            new[] { firstScene, secondScene }, out var registerFailure),
            Is.True, registerFailure);
        Assert.That(factory.TryRecordExactSceneAcknowledgement(player, firstScene,
            transition, ExactSceneAcknowledgementKind.Rebound, out var acknowledgeFailure),
            Is.True, acknowledgeFailure);

        Assert.That(factory.RegisterExactOutboundSceneSet(player, transition,
            new[] { firstScene, secondScene }, out registerFailure),
            Is.True, registerFailure,
            "A new same-transition pre-manifest is the physical reconnect reset boundary.");
        Assert.That(factory.IsAwaitingExactSceneAcknowledgement(
            player, firstScene, transition), Is.True);
        Assert.That(factory.IsAwaitingExactSceneAcknowledgement(
            player, secondScene, transition), Is.True);

        var sceneMembership = typeof(ScenePlayersModule).GetField("_scenePlayers",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(scenePlayers) as IDictionary<SceneID, List<PlayerID>>;
        var loadedMembership = typeof(ScenePlayersModule).GetField("_sceneLoadedPlayers",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(scenePlayers) as IDictionary<SceneID, List<PlayerID>>;
        Assert.That(sceneMembership, Is.Not.Null);
        Assert.That(loadedMembership, Is.Not.Null);
        sceneMembership.Add(firstScene, new List<PlayerID> { player });
        sceneMembership.Add(secondScene, new List<PlayerID> { player });
        loadedMembership.Add(firstScene, new List<PlayerID> { player });
        loadedMembership.Add(secondScene, new List<PlayerID> { player });

        var rebound = typeof(ScenePlayersModule).GetMethod("RemoteClientReboundScene",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(rebound, Is.Not.Null);
        var packet = new ClientFinishedRebindingScene
        {
            scene = firstScene,
            hostMigrationSessionId = transition.sessionId,
            hostMigrationEpoch = transition.epoch
        };
        rebound.Invoke(scenePlayers, new object[] { player, packet, true });

        Assert.That(factory.IsAwaitingExactSceneAcknowledgement(
            player, firstScene, transition), Is.False,
            "Persisted logical loaded membership must not suppress the fresh connection ack.");
        Assert.That(factory.IsAwaitingExactSceneAcknowledgement(
            player, secondScene, transition), Is.True);

        rebound.Invoke(scenePlayers, new object[] { player, packet, true });
        Assert.That(factory.IsAwaitingExactSceneAcknowledgement(
            player, secondScene, transition), Is.True,
            "A duplicate packet in the same connection transaction remains suppressed.");
    }

    [Test]
    public void ExactOutboundBarrier_ScopedRpcFlushBypassesButLaterTrafficRemainsCaptured()
    {
        var (manager, transport) = CreateManager();
        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var broadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(broadcaster);
        broadcaster.Enable(true);

        var player = new PlayerID(31, false);
        var connection = new Connection(71);
        var transition = new HostMigrationTransitionOptions("barrier-scope", 2);
        var playerConnections = typeof(PlayersManager).GetField("_playerToConnection",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<PlayerID, Connection>;
        var connectionPlayers = typeof(PlayersManager).GetField("_connectionToPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<Connection, PlayerID>;
        Assert.That(playerConnections, Is.Not.Null);
        Assert.That(connectionPlayers, Is.Not.Null);
        playerConnections.Add(player, connection);
        connectionPlayers.Add(connection, player);
        transport.captureClientPackets = true;
        transport.sentToClients.Clear();

        Assert.That(broadcaster.BeginExactOutboundBarrier(
            player, transition, out var failure), Is.True, failure);
        broadcaster.Send(player, "ordinary-before-end", Channel.Unreliable);
        Assert.That(broadcaster.RunExactOutboundBarrierBypass(
            player, transition,
            () => broadcaster.Send(player, "snapshot-rpc-baseline", Channel.ReliableOrdered)),
            Is.True);
        broadcaster.Send(player, "ordinary-during-readiness", Channel.UnreliableSequenced);

        Assert.That(transport.sentToClients, Has.Count.EqualTo(1),
            "Only the scoped snapshot RPC baseline may cross the active barrier.");
        Assert.That(broadcaster.ReleaseExactOutboundBarrier(player, transition), Is.True);
        Assert.That(transport.sentToClients, Has.Count.EqualTo(3));
        Assert.That(transport.sentToClients[0].channel, Is.EqualTo(Channel.ReliableOrdered));
        Assert.That(transport.sentToClients[1].channel, Is.EqualTo(Channel.ReliableOrdered));
        Assert.That(transport.sentToClients[2].channel, Is.EqualTo(Channel.ReliableOrdered));

        var receiver = new BroadcastModule(manager, false);
        var received = new List<string>();
        receiver.Subscribe<string>((_, value, _) => received.Add(value));
        for (var i = 0; i < transport.sentToClients.Count; i++)
        {
            var sent = transport.sentToClients[i];
            receiver.OnDataReceived(sent.connection,
                new ByteData(sent.data, 0, sent.data.Length), false);
        }

        Assert.That(received, Is.EqualTo(new[]
        {
            "snapshot-rpc-baseline",
            "ordinary-before-end",
            "ordinary-during-readiness"
        }));
        broadcaster.Disable(true);
    }

    [Test]
    public void ExactBaselineCapture_StagesPostProofCallbackTrafficAheadOfReadiness()
    {
        var (manager, transport) = CreateManager();
        transport.Listen(0);
        Assert.That(manager.isServer, Is.True);

        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        auth.SetPlayerModule(players);
        var broadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(broadcaster);
        broadcaster.Enable(true);

        var player = new PlayerID(33, false);
        var connection = new Connection(73);
        var transition = new HostMigrationTransitionOptions(
            "post-proof-baseline-cut", 4, new[] { player });
        AddRetainedPlayers(players, player);
        var beginRoster = typeof(PlayersManager).GetMethod("BeginHostMigrationRoster",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var playerConnections = typeof(PlayersManager).GetField("_playerToConnection",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<PlayerID, Connection>;
        var connectionPlayers = typeof(PlayersManager).GetField("_connectionToPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<Connection, PlayerID>;
        Assert.That(beginRoster, Is.Not.Null);
        Assert.That(playerConnections, Is.Not.Null);
        Assert.That(connectionPlayers, Is.Not.Null);

        beginRoster.Invoke(players, new object[] { transition });
        playerConnections.Add(player, connection);
        connectionPlayers.Add(connection, player);
        typeof(NetworkManager).GetField("_serverPlayersManager",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, players);
        typeof(NetworkManager).GetField("_hostMigrationSession",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, transition);

        transport.captureClientPackets = true;
        transport.sentToClients.Clear();
        Assert.That(broadcaster.BeginExactOutboundBarrier(
            player, transition, out var barrierFailure), Is.True, barrierFailure);

        broadcaster.Send(player, "ordinary-before-callback-cut", Channel.Unreliable);
        Assert.That(manager.TryBeginHostMigrationServerBaselineCapture(
            player, transition, out var beginFailure), Is.True, beginFailure);

        broadcaster.Send(player, "post-proof-callback-baseline", Channel.UnreliableSequenced);
        Assert.That(manager.TryPrepareHostMigrationServerBaselines(
            player, transition, out var prepareFailure), Is.True, prepareFailure);

        Assert.That(broadcaster.RunExactOutboundBarrierBypass(
            player, transition,
            () => broadcaster.Send(player, 17, Channel.ReliableOrdered)), Is.True);
        Assert.That(manager.TryPublishHostMigrationServerBaselines(
            player, transition, out var publishFailure), Is.True, publishFailure);

        Assert.That(transport.sentToClients, Has.Count.EqualTo(2),
            "Topology and sealed callback state publish before readiness; ordinary traffic stays fenced.");
        Assert.That(broadcaster.ReleaseExactOutboundBarrier(player, transition), Is.True);
        Assert.That(transport.sentToClients, Has.Count.EqualTo(3));

        var receiver = new BroadcastModule(manager, false);
        var received = new List<string>();
        receiver.Subscribe<int>((_, value, _) => received.Add($"topology:{value}"));
        receiver.Subscribe<string>((_, value, _) => received.Add(value));
        for (var i = 0; i < transport.sentToClients.Count; i++)
        {
            var sent = transport.sentToClients[i];
            receiver.OnDataReceived(sent.connection,
                new ByteData(sent.data, 0, sent.data.Length), false);
        }

        Assert.That(received, Is.EqualTo(new[]
        {
            "topology:17",
            "post-proof-callback-baseline",
            "ordinary-before-callback-cut"
        }));
        broadcaster.Disable(true);
    }

    [Test]
    public void ExactConnectionRebound_BeginsBarrierBetweenManifestAndMainCallbacks()
    {
        var (manager, transport) = CreateManager();
        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var broadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(broadcaster);
        broadcaster.Enable(true);

        var player = new PlayerID(32, false);
        var connection = new Connection(72);
        var transition = new HostMigrationTransitionOptions(
            "rebound-barrier-scope", 3, new[] { player });
        AddRetainedPlayers(players, player);
        var beginRoster = typeof(PlayersManager).GetMethod("BeginHostMigrationRoster",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var triggerRebound = typeof(PlayersManager).GetMethod(
            "TriggerHostMigrationConnectionRebound",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var playerConnections = typeof(PlayersManager).GetField("_playerToConnection",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<PlayerID, Connection>;
        var connectionPlayers = typeof(PlayersManager).GetField("_connectionToPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IDictionary<Connection, PlayerID>;
        Assert.That(beginRoster, Is.Not.Null);
        Assert.That(triggerRebound, Is.Not.Null);
        Assert.That(playerConnections, Is.Not.Null);
        Assert.That(connectionPlayers, Is.Not.Null);

        beginRoster.Invoke(players, new object[] { transition });
        playerConnections.Add(player, connection);
        connectionPlayers.Add(connection, player);
        players.onPreHostMigrationConnectionRebound += (joined, _, _) =>
            broadcaster.Send(joined, "scene-manifest", Channel.ReliableOrdered);
        players.onHostMigrationConnectionRebound += (joined, _, _) =>
            broadcaster.Send(joined, "buffered-static-rpc", Channel.ReliableOrdered);
        transport.captureClientPackets = true;
        transport.sentToClients.Clear();

        triggerRebound.Invoke(players, new object[] { player });

        Assert.That(transport.sentToClients, Has.Count.EqualTo(1),
            "The pre-rebound scene manifest must cross before the exact outbound fence starts.");
        Assert.That(players.HasExactOutboundBarrier(player, transition), Is.True,
            "The main rebound callbacks must run inside the exact outbound fence.");
        players.ReleaseExactOutboundBarrier(player, transition);
        Assert.That(transport.sentToClients, Has.Count.EqualTo(2));

        var receiver = new BroadcastModule(manager, false);
        var received = new List<string>();
        receiver.Subscribe<string>((_, value, _) => received.Add(value));
        for (var i = 0; i < transport.sentToClients.Count; i++)
        {
            var sent = transport.sentToClients[i];
            receiver.OnDataReceived(sent.connection,
                new ByteData(sent.data, 0, sent.data.Length), false);
        }

        Assert.That(received, Is.EqualTo(new[]
        {
            "scene-manifest",
            "buffered-static-rpc"
        }));
        broadcaster.Disable(true);
    }

    [Test]
    public void StopClient_ClassifiesLocalIntentAndEmitsExactlyOnce()
    {
        var (manager, transport) = CreateManager();
        ClientDisconnectInfo? received = null;
        int count = 0;
        manager.onClientDisconnected += info =>
        {
            received = info;
            count++;
        };

        transport.Connect(null, 0);
        manager.StopClient();

        Assert.That(count, Is.EqualTo(1));
        Assert.That(received.HasValue, Is.True);
        Assert.That(received.Value.wasLocalRequest, Is.True);
        Assert.That(received.Value.reason, Is.EqualTo(DisconnectReason.ClientRequest));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StopAlreadyDisconnectedRole_StillCleansRetainedModules(bool asServer)
    {
        var (manager, _) = CreateManager();
        var probe = new HostMigrationCleanupProbeModule();
        var modules = new ModulesCollection(manager, asServer);
        modules.AddModule(probe);
        typeof(NetworkManager).GetField(
                asServer ? "_serverModules" : "_clientModules",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, modules);

        if (asServer)
            manager.StopServer();
        else
            manager.StopClient();

        typeof(NetworkManager).GetMethod("ProcessPendingNetworkCleanup",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(manager, null);

        var retained = (ModulesCollection)typeof(NetworkManager).GetField(
                asServer ? "_serverModules" : "_clientModules",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager);
        Assert.That(retained.hasModules, Is.False,
            "A terminal raw transport state must not strand its module graph.");
        Assert.That(probe.disableCount, Is.EqualTo(1));
    }

    [Test]
    public void RemoteDisconnectCause_IsNotRewrittenByReactiveLocalStop()
    {
        var (manager, transport) = CreateManager();
        ClientDisconnectInfo? received = null;
        int count = 0;
        manager.onClientDisconnected += info =>
        {
            received = info;
            count++;
        };

        transport.Connect(null, 0);
        transport.SimulateRemoteDisconnectReasonBeforeState(DisconnectReason.ClientRequest);
        manager.StopClient();

        Assert.That(count, Is.EqualTo(1));
        Assert.That(received.HasValue, Is.True);
        Assert.That(received.Value.wasLocalRequest, Is.False);
        Assert.That(received.Value.reason, Is.EqualTo(DisconnectReason.ServerRequest));
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_TimesOutAndCleansPreparedState()
    {
        var (manager, transport) = CreateManager();
        transport.holdServerConnecting = true;

        var task = manager.PromoteToServerAsync(0.001f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.TimedOut));
        Assert.That(manager.isPromotingToServer, Is.False);
        Assert.That(manager.isHostMigrationTransitionInProgress, Is.False);
        Assert.That(transport.prepared, Is.False);
        Assert.That(transport.cancelPreparedCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_WaitsForPromotedHostClientAndRollsBackBothRoles()
    {
        var (manager, transport) = CreateManager();
        transport.holdClientConnecting = true;

        var task = manager.PromoteToServerAsync(0.001f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.TimedOut));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Disconnected),
            "A failed listen-host promotion must not leave a partial server running.");
        Assert.That(manager.clientState, Is.EqualTo(ConnectionState.Disconnected));
        Assert.That(manager.isPromotingToServer, Is.False);
        Assert.That(manager.isHostMigrationTransitionInProgress, Is.False);
        Assert.That(transport.prepared, Is.False);
        Assert.That(transport.cancelPreparedCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_ActivatesOnlyAfterServerReadiness()
    {
        var (manager, transport) = CreateManager(migrateAsHost: false);

        var task = manager.PromoteToServerAsync(1f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Succeeded));
        Assert.That(transport.activatePreparedCount, Is.EqualTo(1));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(transport.cancelPreparedCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_ActivationFailureRollsBackProvisionalServer()
    {
        var (manager, transport) = CreateManager(migrateAsHost: false);
        transport.activationResult = new HostMigrationTransportActivationResult(
            HostMigrationTransportActivationStatus.Failed, "activation fence expired");

        var task = manager.PromoteToServerAsync(1f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Failed));
        Assert.That(transport.activatePreparedCount, Is.EqualTo(1));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Disconnected));
        Assert.That(manager.clientState, Is.EqualTo(ConnectionState.Disconnected));
        Assert.That(transport.cancelPreparedCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_RoleLossDuringActivationCannotReportSuccess()
    {
        var (manager, transport) = CreateManager(migrateAsHost: false);
        transport.beforeActivationCompletes = transport.StopListening;

        var task = manager.PromoteToServerAsync(1f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Failed));
        Assert.That(transport.activatePreparedCount, Is.EqualTo(1));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Disconnected));
        Assert.That(transport.cancelPreparedCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PromoteToServerAsync_IndeterminateActivationPreservesReadyRolesForReconciliation()
    {
        const string stableScenePath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
        var stableBuildIndex = SceneUtility.GetBuildIndexByScenePath(stableScenePath);
        Assert.That(stableBuildIndex, Is.GreaterThanOrEqualTo(0),
            $"The promotion fixture scene must remain enabled in build settings: {stableScenePath}");

        var stableScene = SceneManager.GetSceneByBuildIndex(stableBuildIndex);
        if (!stableScene.IsValid() || !stableScene.isLoaded)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                stableScene = EditorSceneManager.OpenScene(
                    stableScenePath, OpenSceneMode.Additive);
            }
            else
#endif
            {
                var load = SceneManager.LoadSceneAsync(stableBuildIndex, LoadSceneMode.Additive);
                Assert.That(load, Is.Not.Null);
                yield return load;
                stableScene = SceneManager.GetSceneByBuildIndex(stableBuildIndex);
            }
            _loadedTestScenes.Add(stableScene);
        }

        Assert.That(stableScene.IsValid() && stableScene.isLoaded, Is.True);
        var previousActive = SceneManager.GetActiveScene();
        if (previousActive != stableScene)
        {
            _activeSceneToRestore = previousActive;
            Assert.That(SceneManager.SetActiveScene(stableScene), Is.True);
        }

        var (manager, transport) = CreateManager(migrateAsHost: false);
        var retainedPlayer = PrimeExactClientForPromotion(manager, transport);
        var preservedScope = new HostMigrationTransitionOptions(
            "room-incarnation", 12, new[] { retainedPlayer });
        transport.activationResult = new HostMigrationTransportActivationResult(
            HostMigrationTransportActivationStatus.Indeterminate,
            "activation response was lost");

        var task = manager.PromoteToServerAsync(preservedScope, 1f);
        yield return WaitForTask(task, 1000);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Indeterminate),
            task.Result.message);
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(transport.prepared, Is.True);
        Assert.That(transport.cancelPreparedCount, Is.Zero);
        Assert.That(manager.isHostMigrationTransitionInProgress, Is.False);

        var mismatchedReplay = manager.PromoteToServerAsync(
            new HostMigrationTransitionOptions("different-incarnation", 1), 1f);
        yield return WaitForTask(mismatchedReplay, 1000);
        Assert.That(mismatchedReplay.Result.status,
            Is.EqualTo(HostMigrationTransitionStatus.InvalidState));
        Assert.That(manager.hostMigrationSession, Is.EqualTo(preservedScope));
        Assert.That(transport.activatePreparedCount, Is.EqualTo(1),
            "A retry with a different fence must never dispatch activation.");

        var conflictingTransfer = manager.TransferToNewServerAsync(1f);
        yield return WaitForTask(conflictingTransfer, 1000);
        Assert.That(conflictingTransfer.Result.status,
            Is.EqualTo(HostMigrationTransitionStatus.Indeterminate));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(transport.cancelPreparedCount, Is.Zero);

        using (var cancelledReplay = new CancellationTokenSource())
        {
            cancelledReplay.Cancel();
            var cancelled = manager.PromoteToServerAsync(1f, cancelledReplay.Token);
            yield return WaitForTask(cancelled, 1000);
            Assert.That(cancelled.Result.status,
                Is.EqualTo(HostMigrationTransitionStatus.Indeterminate));
            Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
            Assert.That(transport.activatePreparedCount, Is.EqualTo(1));
            Assert.That(transport.cancelPreparedCount, Is.Zero);
        }

        var expiredBudget = manager.PromoteToServerAsync(float.Epsilon);
        yield return WaitForTask(expiredBudget, 1000);
        Assert.That(expiredBudget.Result.status,
            Is.EqualTo(HostMigrationTransitionStatus.Indeterminate));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(transport.cancelPreparedCount, Is.Zero);

        transport.activationResult = new HostMigrationTransportActivationResult(
            HostMigrationTransportActivationStatus.Succeeded);
        var replay = manager.PromoteToServerAsync(1f);
        yield return WaitForTask(replay, 1000);

        Assert.That(replay.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Succeeded));
        Assert.That(manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(transport.activatePreparedCount, Is.EqualTo(2));
        Assert.That(transport.cancelPreparedCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator TransferToNewServerAsync_CancelsAndCleansPreparedState()
    {
        var (manager, transport) = CreateManager();
        transport.holdClientConnecting = true;
        using var cancellation = new CancellationTokenSource();

        var task = manager.TransferToNewServerAsync(5f, cancellation.Token);
        cancellation.Cancel();
        yield return WaitForTask(task, 120);

        Assert.That(task.Result.status, Is.EqualTo(HostMigrationTransitionStatus.Cancelled));
        Assert.That(manager.isTransferringToNewServer, Is.False);
        Assert.That(manager.isHostMigrationTransitionInProgress, Is.False);
        Assert.That(transport.prepared, Is.False);
        Assert.That(transport.cancelPreparedCount, Is.EqualTo(1));
    }

    [Test]
    public void PurrTransport_AuthenticationRejectClearsPreparedCredentialAndBecomesTerminal()
    {
        var root = new GameObject("PurrTransport auth reject test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<PurrTransport>();
        transport.PrepareHostMigration(default, new HostJoinInfo { secret = "one-use-host-secret" }, "room");

        var handler = typeof(PurrTransport).GetMethod("OnHostData",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(ArraySegment<byte>) }, null);
        Assert.That(handler, Is.Not.Null);
        handler.Invoke(transport, new object[] { new ArraySegment<byte>(new byte[] { 4 }) });

        Assert.That(transport.hasPreparedHostMigration, Is.False);
        Assert.That(transport.TryGetHostMigrationFailure(true, out var failure), Is.True);
        Assert.That(failure, Does.Contain("rejected"));
    }

    [Test]
    public void PurrTransport_HostAuthenticationRetainsActivationFenceUntilReadiness()
    {
        var root = new GameObject("PurrTransport provisional activation test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<PurrTransport>();
        transport.PrepareHostMigration(default,
            new HostJoinInfo { secret = "one-use-host-secret" },
            "room",
            new PurrTransport.HostMigrationActivationRequest
            {
                masterServer = "https://relay.example/",
                roomName = "room",
                incarnation = "incarnation",
                generation = 1,
                claimId = "claim",
                fencingToken = "fence",
                promotedPlayerId = "2",
                activationExpiresAt = "2026-08-22T12:00:10Z"
            });

        var handler = typeof(PurrTransport).GetMethod("OnHostData",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(ArraySegment<byte>) }, null);
        Assert.That(handler, Is.Not.Null);
        handler.Invoke(transport, new object[] { new ArraySegment<byte>(new byte[] { 3 }) });

        Assert.That(transport.hasPreparedHostMigration, Is.False);
        Assert.That(transport.hasPendingHostMigrationActivation, Is.True);
        Assert.That(transport.TryGetPreparedHostMigrationActivation(out var pending), Is.True);
        Assert.That(pending.claimId, Is.EqualTo("claim"));

        var disconnectHandler = typeof(PurrTransport).GetMethod("OnHostDisconnected",
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        Assert.That(disconnectHandler, Is.Not.Null);
        disconnectHandler.Invoke(transport, null);
        Assert.That(transport.TryGetHostMigrationFailure(true, out var disconnectFailure), Is.True);
        Assert.That(disconnectFailure, Does.Contain("provisional"));

        transport.CancelPreparedHostMigration();
        Assert.That(transport.hasPendingHostMigrationActivation, Is.False);
    }

    [Test]
    public void PurrTransport_ActivationSuccessRequiresExactActiveFence()
    {
        var expected = new PurrTransport.HostMigrationActivationRequest
        {
            roomName = "room",
            incarnation = "incarnation",
            generation = 3,
            claimId = "claim",
            fencingToken = "fence",
            promotedPlayerId = "2"
        };
        const string exact = "{\"roomName\":\"room\",\"incarnation\":\"incarnation\"," +
                             "\"generation\":3,\"claimId\":\"claim\",\"fencingToken\":\"fence\"," +
                             "\"promotedPlayerId\":\"2\",\"hostPhase\":\"active\"," +
                             "\"hostActive\":true,\"claimPending\":false}";

        Assert.That(PurrTransportUtils.TryValidateMigrationActivationSuccess(
            exact, expected, out var failure), Is.True, failure);

        var wrongClaim = exact.Replace("\"claimId\":\"claim\"", "\"claimId\":\"other\"");
        Assert.That(PurrTransportUtils.TryValidateMigrationActivationSuccess(
            wrongClaim, expected, out failure), Is.False);
        Assert.That(failure, Does.Contain("different migration fence"));

        var stillPending = exact.Replace("\"hostPhase\":\"active\"", "\"hostPhase\":\"pending\"")
            .Replace("\"hostActive\":true", "\"hostActive\":false")
            .Replace("\"claimPending\":false", "\"claimPending\":true");
        Assert.That(PurrTransportUtils.TryValidateMigrationActivationSuccess(
            stillPending, expected, out _), Is.False);

        Assert.That(PurrTransportUtils.IsTerminalActivationFenceError(
            "host_connection_lost"), Is.True);
        Assert.That(PurrTransportUtils.IsTerminalActivationFenceError(
            "host_not_connected"), Is.False);
    }

    [Test]
    public void PurrTransport_ActivationErrorsRequireAnExactAuthoritativeTerminalCode()
    {
        Assert.That(PurrTransportUtils.TryGetTerminalMigrationActivationError(
            "{\"error\":\"internal failure\"}", false, out _), Is.False,
            "A generic service error cannot prove that a dispatched activation was rejected.");
        Assert.That(PurrTransportUtils.TryGetTerminalMigrationActivationError(
            "{\"code\":\"unknown_failure\",\"error\":\"unknown\",\"retryable\":false}",
            false, out _), Is.False,
            "Unknown codes must not become terminal merely because retryable defaults to false.");
        Assert.That(PurrTransportUtils.TryGetTerminalMigrationActivationError(
            "{\"code\":\"host_connection_lost\",\"error\":\"gone\",\"retryable\":false}",
            false, out var terminalFailure), Is.True);
        Assert.That(terminalFailure, Is.EqualTo("gone"));

        const string roomNotFound =
            "{\"code\":\"room_not_found\",\"error\":\"missing\",\"retryable\":false}";
        Assert.That(PurrTransportUtils.TryGetTerminalMigrationActivationError(
            roomNotFound, false, out _), Is.True,
            "Before any ambiguous attempt, missing balancer routing proves this POST did not commit.");
        Assert.That(PurrTransportUtils.TryGetTerminalMigrationActivationError(
            roomNotFound, true, out _), Is.False,
            "Missing balancer routing cannot disprove an earlier relay commit.");
    }

    [UnityTest]
    public IEnumerator PurrTransport_CancelDuringActivationPreservesExactReplayDescriptor()
    {
        var root = new GameObject("PurrTransport activation cancellation race test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<PurrTransport>();
        var activation = new PurrTransport.HostMigrationActivationRequest
        {
            masterServer = "https://relay.example/",
            roomName = "room",
            incarnation = "incarnation",
            generation = 7,
            claimId = "claim",
            fencingToken = "fence",
            promotedPlayerId = "2",
            activationExpiresAt = "2026-08-22T12:00:10Z"
        };
        transport.PrepareHostMigration(default,
            new HostJoinInfo { secret = "one-use-host-secret" }, "room", activation);
        typeof(PurrTransport).GetField("_hostJoinInfo",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(transport, new HostJoinInfo { secret = "one-use-host-secret" });

        var pendingResponse = new TaskCompletionSource<HostMigrationTransportActivationResult>();
        transport.hostMigrationActivationOverrideForTests =
            (_, _, _, _, _) => pendingResponse.Task;
        var activationTask = transport.ActivatePreparedHostMigrationAsync(10f);
        Assert.That(activationTask.IsCompleted, Is.False);

        transport.CancelPreparedHostMigration();
        Assert.That(transport.hasPendingHostMigrationActivation, Is.True);
        Assert.That(transport.TryGetPreparedHostMigrationActivation(out var preserved), Is.True);
        Assert.That(preserved.claimId, Is.EqualTo(activation.claimId));
        Assert.That(preserved.fencingToken, Is.EqualTo(activation.fencingToken));

        var refreshedActivation = activation;
        refreshedActivation.activationExpiresAt = "2026-08-22T12:01:00Z";
        Assert.DoesNotThrow(() => transport.PrepareHostMigration(default,
            new HostJoinInfo { secret = "replacement-must-not-overwrite" }, "room",
            refreshedActivation));
        Assert.That(transport.TryGetPreparedHostMigrationActivation(out preserved), Is.True);
        Assert.That(preserved.claimId, Is.EqualTo(activation.claimId),
            "Advisory expiry metadata must not turn an exact fence replay into a conflict.");

        var conflictingActivation = activation;
        conflictingActivation.claimId = "different-claim";
        Assert.Throws<InvalidOperationException>(() => transport.PrepareHostMigration(default,
            new HostJoinInfo { secret = "different-secret" }, "room", conflictingActivation));
        Assert.That(transport.TryGetPreparedHostMigrationActivation(out preserved), Is.True);
        Assert.That(preserved.claimId, Is.EqualTo(activation.claimId));

        pendingResponse.SetResult(new HostMigrationTransportActivationResult(
            HostMigrationTransportActivationStatus.Indeterminate,
            "activation response was lost"));
        yield return WaitForTask(activationTask, 1000);

        Assert.That(activationTask.Result.status,
            Is.EqualTo(HostMigrationTransportActivationStatus.Indeterminate));
        Assert.That(transport.hasIndeterminateHostMigrationActivation, Is.True);
        transport.CancelPreparedHostMigration();
        Assert.That(transport.TryGetPreparedHostMigrationActivation(out preserved), Is.True,
            "Repeated cancellation must not erase a descriptor whose outcome is unknown.");
        Assert.That(preserved.claimId, Is.EqualTo(activation.claimId));

        bool replayWasMarkedIndeterminate = false;
        transport.hostMigrationActivationOverrideForTests =
            (_, _, _, mayHaveActivated, _) =>
            {
                replayWasMarkedIndeterminate = mayHaveActivated;
                return Task.FromResult(new HostMigrationTransportActivationResult(
                    HostMigrationTransportActivationStatus.Succeeded));
            };
        var replayTask = transport.ActivatePreparedHostMigrationAsync(10f);
        yield return WaitForTask(replayTask, 1000);

        Assert.That(replayTask.Result.succeeded, Is.True);
        Assert.That(replayWasMarkedIndeterminate, Is.True);
        Assert.That(transport.hasPendingHostMigrationActivation, Is.False);
        Assert.That(transport.hasIndeterminateHostMigrationActivation, Is.False);
        Assert.That(transport.hasPreparedHostMigration, Is.False,
            "An authoritative replay should finally honor the deferred cancellation.");
    }

    [Test]
    public void PurrTransport_AllocationRequiresCompleteAuthoritativeCapability()
    {
        var valid = new HostJoinInfo
        {
            host = "relay-b.example",
            secret = "host-capability",
            port = 6942,
            udpPortV2 = 7778
        };

        Assert.That(PurrTransportUtils.HasCompleteAllocationCapability(valid), Is.True);

        valid.host = "   ";
        Assert.That(PurrTransportUtils.HasCompleteAllocationCapability(valid), Is.False);
        valid.host = "relay-b.example";
        valid.secret = null;
        Assert.That(PurrTransportUtils.HasCompleteAllocationCapability(valid), Is.False);
        valid.secret = "host-capability";
        valid.port = 0;
        Assert.That(PurrTransportUtils.HasCompleteAllocationCapability(valid), Is.False);
        valid.port = 6942;
        valid.udpPortV2 = 65536;
        Assert.That(PurrTransportUtils.HasCompleteAllocationCapability(valid), Is.False);
    }

    [Test]
    public void PurrTransport_JoinRequiresCompleteAuthoritativeCapability()
    {
        var valid = new ClientJoinInfo
        {
            host = "relay-b.example",
            secret = "client-capability",
            port = 6942,
            udpPortV2 = 7778
        };

        Assert.That(PurrTransportUtils.HasCompleteJoinCapability(valid), Is.True);
        valid.secret = "";
        Assert.That(PurrTransportUtils.HasCompleteJoinCapability(valid), Is.False);
    }

    [Test]
    public void PurrTransport_PreDispatchActivationConfigurationFailureIsNotIndeterminate()
    {
        var malformed = new PurrTransport.HostMigrationActivationRequest
        {
            masterServer = "https://relay.example/",
            roomName = "room",
            incarnation = "incarnation",
            generation = 1,
            claimId = "invalid\r\nheader",
            fencingToken = "fence",
            promotedPlayerId = "2"
        };

        var initial = PurrTransportUtils.ActivateHostMigration(
            malformed, "host-secret", 1f, false, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.That(initial.status, Is.EqualTo(HostMigrationTransportActivationStatus.Failed));

        var afterUnknownDispatch = PurrTransportUtils.ActivateHostMigration(
            malformed, "host-secret", 1f, true, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.That(afterUnknownDispatch.status,
            Is.EqualTo(HostMigrationTransportActivationStatus.Indeterminate));
    }

    [Test]
    public void PurrTransport_ActivationMasterServerAcceptsHttpAndHttpsBaseUrls()
    {
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "https://relay.example/api", out var failure), Is.True, failure);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "http://localhost:8080/", out failure), Is.True, failure);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "http://127.0.0.1:8080/", out failure), Is.True, failure);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "http://relay.internal:8080/", out failure), Is.True, failure);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "http://relay.example/", out failure), Is.True, failure);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "https://relay.example/?target=other", out _), Is.False);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "https://relay.example/#other", out _), Is.False);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "relay.example", out _), Is.False);
        Assert.That(PurrTransportUtils.TryValidateHostMigrationMasterServerUrl(
            "ftp://relay.example/", out _), Is.False);
    }

    [Test]
    public void PurrTransport_DropsServerDataUntilConnectionSnapshotIsApplied()
    {
        var root = new GameObject("PurrTransport connection ordering test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<PurrTransport>();
        int received = 0;
        transport.onDataReceived += (_, _, _) => received++;

        var handler = typeof(PurrTransport).GetMethod("OnHostData",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(ArraySegment<byte>) }, null);
        Assert.That(handler, Is.Not.Null);

        var data = new ArraySegment<byte>(new byte[] { 2, 7, 0, 0, 0, 42 });
        handler.Invoke(transport, new object[] { data });
        Assert.That(received, Is.Zero);

        var connected = new ArraySegment<byte>(new byte[] { 0, 7, 0, 0, 0 });
        handler.Invoke(transport, new object[] { connected });
        handler.Invoke(transport, new object[] { data });
        Assert.That(received, Is.EqualTo(1));
    }

#if ADDRESSABLES_PURRNET_SUPPORT
    [Test]
    public void AddressablesSyncModule_PromotionSwitchesClientSubscriptionsToServer()
    {
        var (manager, _) = CreateManager();
        var addressables = ScriptableObject.CreateInstance<AddressableNetworkPrefabs>();
        _created.Add(addressables);
        typeof(NetworkManager).GetField("_addressableNetworkPrefabs",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, addressables);
        var broadcast = new BroadcastModule(manager, false);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, false);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var playersBroadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(playersBroadcaster);
        auth.SetPlayerModule(players);
        var module = new AddressablesSyncModule(manager, players);

        module.Enable(false);
        Assert.That(module.isClientModeEnabled, Is.True);
        Assert.That(module.isServerModeEnabled, Is.False);
        Assert.That(GetLocalPlayerReceivedIdSubscriptionCount(players), Is.EqualTo(1));

        module.PromoteToServerModule();

        Assert.That(module.isClientModeEnabled, Is.False);
        Assert.That(module.isServerModeEnabled, Is.True);
        Assert.That(GetLocalPlayerReceivedIdSubscriptionCount(players), Is.Zero);
        Assert.That(GetPlayerSubscriptionCount(playersBroadcaster), Is.EqualTo(1));

        module.Disable(true);
        Assert.That(GetPlayerSubscriptionCount(playersBroadcaster), Is.Zero);
    }
#endif

    private (NetworkManager manager, HostMigrationCoreTestTransport transport) CreateManager(
        bool migrateAsHost = true)
    {
        var root = new GameObject("Host migration core test");
        root.SetActive(false);
        _created.Add(root);

        var transport = root.AddComponent<HostMigrationCoreTestTransport>();
        root.AddComponent<UnityLatestUpdate>();
        var manager = root.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        manager.transport = transport;

        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        typeof(NetworkRules).GetField("_hostMigrationRules",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(rules, new HostMigrationRules
            {
                enableHostMigration = true,
                migrateAsHost = migrateAsHost
            });
        typeof(NetworkRules).GetField("_defaultSceneRules",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(rules, new NetworkSceneRules
            {
                sceneCleanupModeOnDisconnect = SceneCleanupMode.Off
            });
        manager.SetNetworkRules(rules);
        root.SetActive(true);
        return (manager, transport);
    }

    private static PlayersManager CreatePlayersManager(NetworkManager manager, bool asServer)
    {
        var broadcast = new BroadcastModule(manager, asServer);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, asServer);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        auth.SetPlayerModule(players);
        return players;
    }

    private static void AddRetainedPlayers(PlayersManager players,
        params PlayerID[] retainedPlayers)
    {
        var playersField = typeof(PlayersManager).GetField("_players",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(playersField, Is.Not.Null);
        var currentPlayers = playersField.GetValue(players) as IList<PlayerID>;
        Assert.That(currentPlayers, Is.Not.Null);
        for (var i = 0; i < retainedPlayers.Length; i++)
        {
            if (!currentPlayers.Contains(retainedPlayers[i]))
                currentPlayers.Add(retainedPlayers[i]);
        }
    }

    private static PlayerID PrimeExactClientForPromotion(
        NetworkManager manager, HostMigrationCoreTestTransport transport)
    {
        manager.InternalRegisterClientModules();
        transport.Connect(null, 0);
        Assert.That(manager.clientState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(manager.TryGetModule(out PlayersManager players, false), Is.True);

        var retainedPlayer = new PlayerID(4, false);
        typeof(PlayersManager).GetField("<localPlayerId>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(players, (PlayerID?)retainedPlayer);
        var retainedPlayers = typeof(PlayersManager).GetField("_players",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as IList<PlayerID>;
        Assert.That(retainedPlayers, Is.Not.Null);
        retainedPlayers.Add(retainedPlayer);
        return retainedPlayer;
    }

    private static IEnumerator WaitForTask(System.Threading.Tasks.Task task, int maxFrames)
    {
        var realtimeDeadline = Time.realtimeSinceStartup + 5f;
        for (int i = 0;
             (i < maxFrames || Time.realtimeSinceStartup < realtimeDeadline) && !task.IsCompleted;
             i++)
            yield return null;
        Assert.That(task.IsCompleted, Is.True, "Host migration task did not complete in time.");
    }

#if ADDRESSABLES_PURRNET_SUPPORT
    private static int GetPlayerSubscriptionCount(PlayersBroadcaster broadcaster)
    {
        var field = typeof(PlayersBroadcaster).GetField("_actions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(broadcaster) is not IDictionary value)
            return 0;

        int count = 0;
        foreach (DictionaryEntry entry in value)
        {
            if (entry.Value is ICollection callbacks)
                count += callbacks.Count;
        }
        return count;
    }

    private static int GetLocalPlayerReceivedIdSubscriptionCount(PlayersManager players)
    {
        var callback = typeof(PlayersManager).GetField("onLocalPlayerReceivedID",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players) as Delegate;
        return callback?.GetInvocationList().Length ?? 0;
    }
#endif
}

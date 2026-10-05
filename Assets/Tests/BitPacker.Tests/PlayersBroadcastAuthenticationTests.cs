using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Authentication;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class PlayersBroadcastAuthenticationTests
{
    struct DecodeProbe
    {
        public int value;
    }

    static int _decoderReads;
    readonly List<GameObject> _created = new();
    readonly List<Role> _roles = new();

    sealed class Role
    {
        public readonly NetworkManager manager;
        public readonly BroadcastModule broadcast;
        public readonly CookiesModule cookies;
        public readonly AuthModule auth;
        public readonly PlayersManager players;
        public readonly PlayersBroadcaster playerBroadcast;
        public bool asServer;

        public Role(NetworkManager manager, bool asServer)
        {
            this.manager = manager;
            this.asServer = asServer;
            broadcast = new BroadcastModule(manager, asServer);
            cookies = new CookiesModule(CookieScope.LiveWithConnection, asServer);
            auth = new AuthModule(manager, broadcast, cookies);
            players = new PlayersManager(manager, auth, broadcast);
            playerBroadcast = new PlayersBroadcaster(broadcast, players);
            auth.SetPlayerModule(players);
            players.SetBroadcaster(playerBroadcast);
            broadcast.Enable(asServer);
            cookies.Enable(asServer);
            auth.Enable(asServer);
            players.Enable(asServer);
            playerBroadcast.Enable(asServer);
        }

        public void Receive<T>(Connection connection, T value, bool? receivedAsServer = null)
        {
            using var packet = BitPackerPool.Get();
            Packer<uint>.Write(packet, Hasher.GetStableHashU32<T>());
            Packer<T>.Write(packet, value);
            broadcast.OnDataReceived(connection, packet.ToByteData(), receivedAsServer ?? asServer);
        }

        public void Authenticate(Connection connection, string cookie)
        {
            broadcast.OnConnected(connection, true);
            players.OnConnected(connection, true);
            auth.OnConnected(connection, true);
            Receive(connection, new AuthenticationRequest { cookie = cookie, version = NetworkManager.version });
        }

        public void Disconnect(Connection connection)
        {
            broadcast.OnDisconnected(connection, true);
            auth.OnDisconnected(connection, true);
            players.OnDisconnected(connection, true);
        }

        public void Promote()
        {
            broadcast.PromoteToServerModule();
            cookies.PromoteToServerModule();
            auth.PromoteToServerModule();
            players.PromoteToServerModule();
            playerBroadcast.PromoteToServerModule();
            asServer = true;
        }

        public void PostPromote()
        {
            broadcast.PostPromoteToServerModule();
            cookies.PostPromoteToServerModule();
            auth.PostPromoteToServerModule();
            players.PostPromoteToServerModule();
            playerBroadcast.PostPromoteToServerModule();
        }

        public void Disable()
        {
            playerBroadcast.Disable(asServer);
            players.Disable(asServer);
            auth.Disable(asServer);
            if (asServer)
                auth.Disable(false);
            cookies.Disable(asServer);
            broadcast.Disable(asServer);
        }
    }

    [OneTimeSetUp]
    public void RegisterPackers()
    {
        NetworkManager.CallAllRegisters();
        Hasher.PrepareType<int>();
        Hasher.PrepareType<AuthenticationRequest>();
        Hasher.PrepareType<ServerLoginResponse>();
        Hasher.PrepareType<PlayerJoinedEvent>();
        Hasher.PrepareType<PlayerLeftEvent>();
        Hasher.PrepareType<PlayerSnapshotEvent>();
        Packer<DecodeProbe>.RegisterWriter(ProbePacker.Write);
        Packer<DecodeProbe>.RegisterReader(ProbePacker.Read);
#if ADDRESSABLES_PURRNET_SUPPORT
        Hasher.PrepareType<AddressableLoadStatePacket>();
#endif
    }

    static class ProbePacker
    {
        public static void Write(BitPacker packer, DecodeProbe value) => Packer<int>.Write(packer, value.value);

        public static void Read(BitPacker packer, ref DecodeProbe value)
        {
            _decoderReads++;
            Packer<int>.Read(packer, ref value.value);
        }
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _roles.Count - 1; i >= 0; i--)
            _roles[i].Disable();
        _roles.Clear();
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].GetComponent<NetworkManager>().transport = null;
            Object.DestroyImmediate(_created[i]);
        }
        _created.Clear();
    }

    Role CreateRole(bool asServer, NetworkManager manager = null)
    {
        if (!manager)
        {
            var go = new GameObject("Players broadcast authentication test");
            go.SetActive(false);
            _created.Add(go);
            var transport = go.AddComponent<LocalTransport>();
            manager = go.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            manager.transport = transport;
            typeof(NetworkManager).GetField("_serverModules", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new ModulesCollection(manager, true));
        }
        var role = new Role(manager, asServer);
        _roles.Add(role);
        return role;
    }

    [Test]
    public void UnauthenticatedConnection_ReachesConnectionCallbacksOnly()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        var connectionMessages = new List<(Connection, int, bool)>();
        int playerMessages = 0;
        server.broadcast.Subscribe<int>((sender, value, role) => connectionMessages.Add((sender, value, role)));
        server.players.Subscribe<int>((_, _, _) => playerMessages++);

        server.Receive(connection, 123);

        Assert.That(connectionMessages, Is.EqualTo(new[] { (connection, 123, true) }));
        Assert.That(playerMessages, Is.Zero);
        Assert.That(server.players.TryGetPlayer(connection, out _), Is.False);
    }

    [Test]
    public void AcceptedAuthentication_ReachesPlayerCallbacksWithRegisteredIdentity()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        var received = new List<(PlayerID, int, bool)>();
        server.players.Subscribe<int>((sender, value, role) => received.Add((sender, value, role)));

        server.Authenticate(connection, "accepted-cookie");
        Assert.That(server.players.TryGetPlayer(connection, out var player), Is.True);
        Assert.That(player.isServer, Is.False);
        server.Receive(connection, 123);

        Assert.That(received, Is.EqualTo(new[] { (player, 123, true) }));
    }

    [Test]
    public void Reconnect_RequiresFreshAuthenticationAndRejectsOldConnection()
    {
        var server = CreateRole(true);
        var oldConnection = new Connection(42);
        var newConnection = new Connection(43);
        var received = new List<(PlayerID, int, bool)>();
        server.players.Subscribe<int>((sender, value, role) => received.Add((sender, value, role)));
        server.Authenticate(oldConnection, "reconnect-cookie");
        Assert.That(server.players.TryGetPlayer(oldConnection, out var player), Is.True);

        server.Disconnect(oldConnection);
        server.Receive(oldConnection, 1);
        server.Receive(newConnection, 2);
        Assert.That(received, Is.Empty);
        server.Authenticate(newConnection, "reconnect-cookie");
        Assert.That(server.players.TryGetPlayer(newConnection, out var reconnectedPlayer), Is.True);
        Assert.That(reconnectedPlayer, Is.EqualTo(player));
        server.Receive(oldConnection, 3);
        server.Receive(newConnection, 4);

        Assert.That(received, Is.EqualTo(new[] { (player, 4, true) }));
    }

    [Test]
    public void ServerShutdown_RemovesAuthenticatedConnectionMapping()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        int received = 0;
        server.players.Subscribe<int>((_, _, _) => received++);
        server.Authenticate(connection, "shutdown-cookie");

        server.players.OnConnectionState(ConnectionState.Disconnected, true);
        server.Receive(connection, 123);

        Assert.That(server.players.TryGetPlayer(connection, out _), Is.False);
        Assert.That(received, Is.Zero);
    }

    [Test]
    public void ClientDispatch_UsesServerIdentityDespitePlayerTopologyCollision()
    {
        var client = CreateRole(false);
        var connection = new Connection(42);
        var peer = new PlayerID(17UL, false);
        var received = new List<(PlayerID, int, bool)>();
        client.Receive(connection, new PlayerJoinedEvent(peer, connection, null, null));
        Assert.That(client.players.TryGetPlayer(connection, out var mappedPlayer), Is.True);
        Assert.That(mappedPlayer, Is.EqualTo(peer));
        client.players.Subscribe<int>((sender, value, role) => received.Add((sender, value, role)));

        client.Receive(connection, 123);
        client.Receive(default, 456);

        Assert.That(received, Is.EqualTo(new[] { (PlayerID.Server, 123, false), (PlayerID.Server, 456, false) }));
    }

    [Test]
    public void HostRoles_KeepServerPlayerAndClientServerIdentitiesSeparate()
    {
        var server = CreateRole(true);
        var client = CreateRole(false, server.manager);
        var connection = new Connection(1);
        var received = new List<(PlayerID, int, bool)>();
        server.Authenticate(connection, "host-cookie");
        Assert.That(server.players.TryGetPlayer(connection, out var player), Is.True);
        client.Receive(connection, new PlayerJoinedEvent(player, connection, null, null));
        server.players.Subscribe<int>((sender, value, role) => received.Add((sender, value, role)));
        client.players.Subscribe<int>((sender, value, role) => received.Add((sender, value, role)));

        server.Receive(connection, 1);
        client.Receive(connection, 2);
        server.Receive(new Connection(99), 3);

        Assert.That(received, Is.EqualTo(new[] { (player, 1, true), (PlayerID.Server, 2, false) }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OppositeRoleReceive_DoesNotReachEitherCallback(bool asServer)
    {
        var role = CreateRole(asServer);
        var connection = new Connection(42);
        if (asServer)
            role.Authenticate(connection, "role-cookie");
        int connectionMessages = 0;
        int playerMessages = 0;
        role.broadcast.Subscribe<int>((_, _, _) => connectionMessages++);
        role.players.Subscribe<int>((_, _, _) => playerMessages++);

        role.Receive(connection, 123, !asServer);

        Assert.That(connectionMessages, Is.Zero);
        Assert.That(playerMessages, Is.Zero);
    }

    [Test]
    public void ServerReceive_PlayerTopologyEventCannotAuthenticateConnection()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        int received = 0;
        server.players.Subscribe<int>((_, _, _) => received++);

        server.Receive(connection, new PlayerJoinedEvent(PlayerID.Server, connection, null, "forged-cookie"));
        server.Receive(connection, new ServerLoginResponse(PlayerID.Server, new NetworkID(0, PlayerID.Server)));
        server.Receive(connection, 123);

        Assert.That(server.players.TryGetPlayer(connection, out _), Is.False);
        Assert.That(server.players.localPlayerId, Is.Null);
        Assert.That(received, Is.Zero);
    }

    [Test]
    public void ReservedServerMapping_CannotGiveClientConnectionServerIdentity()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        var mappings = (Dictionary<Connection, PlayerID>)typeof(PlayersManager)
            .GetField("_connectionToPlayerId", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(server.players);
        mappings.Add(connection, PlayerID.Server);
        int received = 0;
        server.players.Subscribe<int>((_, _, _) => received++);

        server.Receive(connection, 123);

        Assert.That(server.players.TryGetPlayer(connection, out var mappedPlayer), Is.True);
        Assert.That(mappedPlayer, Is.EqualTo(PlayerID.Server));
        Assert.That(received, Is.Zero);
    }

    [Test]
    public void Promotion_RejectsSnapshotConnectionsBeforeAndAfterCleanupUntilAuthentication()
    {
        var role = CreateRole(false);
        var connection = new Connection(42);
        var peer = new PlayerID(17UL, false);
        var received = new List<(PlayerID, int, bool)>();
        role.Receive(connection, new PlayerJoinedEvent(peer, connection, null, null));
        role.players.Subscribe<int>((sender, value, asServer) => received.Add((sender, value, asServer)));

        role.Promote();
        Assert.That(role.players.TryGetPlayer(connection, out _), Is.True, "snapshot mapping should still exist until PostPromote");
        role.Receive(connection, 1);
        role.PostPromote();
        Assert.That(role.players.TryGetPlayer(connection, out _), Is.False);
        role.Receive(connection, 2);
        Assert.That(received, Is.Empty);
        var newConnection = new Connection(43);
        role.Authenticate(newConnection, "new-server-cookie");
        Assert.That(role.players.TryGetPlayer(newConnection, out var player), Is.True);
        role.Receive(newConnection, 3);

        Assert.That(received, Is.EqualTo(new[] { (player, 3, true) }));
    }

    [Test]
    public void Promotion_ReusedLocalConnectionIdDisconnectRemovesNewAuthentication()
    {
        var role = CreateRole(false);
        var connection = new Connection(1);
        var localPlayer = new PlayerID(17UL, false);
        int received = 0;
        role.Receive(connection, new PlayerJoinedEvent(localPlayer, connection, null, "local-cookie"));
        role.Receive(connection, new ServerLoginResponse(localPlayer, new NetworkID(0, localPlayer)));
        role.Promote();
        role.PostPromote();
        Assert.That(role.players.TryGetPlayer(connection, out _), Is.False);

        role.Authenticate(connection, "local-cookie");
        Assert.That(role.players.TryGetPlayer(connection, out var player), Is.True);
        Assert.That(player, Is.EqualTo(localPlayer));
        role.players.Subscribe<int>((_, _, _) => received++);
        role.Receive(connection, 1);
        Assert.That(received, Is.EqualTo(1));
        role.Disconnect(connection);
        role.Receive(connection, 2);

        Assert.That(role.players.TryGetPlayer(connection, out _), Is.False);
        Assert.That(received, Is.EqualTo(1));
    }

    [Test]
    public void UnauthenticatedMalformedPlayerPacket_IsDroppedBeforePlayerDecoder()
    {
        var server = CreateRole(true);
        var connection = new Connection(42);
        _decoderReads = 0;
        int received = 0;
        server.players.Subscribe<DecodeProbe>((_, _, _) => received++);
        using (var packet = BitPackerPool.Get())
        {
            Packer<uint>.Write(packet, Hasher.GetStableHashU32<DecodeProbe>());
            server.broadcast.OnDataReceived(connection, packet.ToByteData(), true);
        }

        Assert.That(_decoderReads, Is.Zero);
        Assert.That(received, Is.Zero);
        LogAssert.NoUnexpectedReceived();
        server.Authenticate(connection, "decoder-cookie");
        server.Receive(connection, new DecodeProbe { value = 123 });
        Assert.That(_decoderReads, Is.EqualTo(1));
        Assert.That(received, Is.EqualTo(1));
    }

#if ADDRESSABLES_PURRNET_SUPPORT
    [Test]
    public void AddressablesState_RequiresAuthenticatedPlayerAndNeverUsesServerKey()
    {
        const string assetGuid = "0123456789abcdef0123456789abcdef";
        var server = CreateRole(true);
        var connection = new Connection(42);
        var prefabs = ScriptableObject.CreateInstance<AddressableNetworkPrefabs>();
        ((List<AddressableNetworkPrefabs.Entry>)typeof(AddressableNetworkPrefabs)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(prefabs))
            .Add(new AddressableNetworkPrefabs.Entry
            {
                asset = new UnityEngine.AddressableAssets.AssetReferenceGameObject(assetGuid)
            });
        prefabs.Refresh();
        typeof(NetworkManager).GetField("_addressableNetworkPrefabs", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(server.manager, prefabs);
        var sync = new AddressablesSyncModule(server.manager, server.players);
        var received = new List<(PlayerID, string, bool)>();
        sync.onClientLoadStateChanged += (sender, guid, loaded) => received.Add((sender, guid, loaded));
        sync.Enable(true);
        try
        {
            var packet = new AddressableLoadStatePacket { guid = new StringUTF8(assetGuid), loaded = true };
            server.Receive(connection, packet);
            Assert.That(sync.GetLoadedGuidsForPlayer(PlayerID.Server), Is.Empty);
            Assert.That(received, Is.Empty);

            server.Authenticate(connection, "addressables-cookie");
            Assert.That(server.players.TryGetPlayer(connection, out var player), Is.True);
            server.Receive(connection, packet);
            Assert.That(sync.GetLoadedGuidsForPlayer(player), Is.EquivalentTo(new[] { assetGuid }));
            Assert.That(sync.GetLoadedGuidsForPlayer(PlayerID.Server), Is.Empty);
            Assert.That(received, Is.EqualTo(new[] { (player, assetGuid, true) }));

            server.Receive(connection,
                new AddressableLoadStatePacket { guid = new StringUTF8("unknown-guid"), loaded = true });
            Assert.That(sync.GetLoadedGuidsForPlayer(player), Is.EquivalentTo(new[] { assetGuid }));
            Assert.That(received.Count, Is.EqualTo(1));

            server.Disconnect(connection);
            server.Receive(connection, packet);
            Assert.That(sync.GetLoadedGuidsForPlayer(player), Is.Empty);
            Assert.That(sync.GetLoadedGuidsForPlayer(PlayerID.Server), Is.Empty);
            Assert.That(received.Count, Is.EqualTo(1));
        }
        finally
        {
            sync.Disable(true);
            Object.DestroyImmediate(prefabs);
        }
    }
#endif
}

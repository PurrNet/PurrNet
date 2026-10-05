using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.TestTools;

public class OwnershipAuthorizationTests
{
    private static readonly PlayerID Actor = new PlayerID(1, false);
    private static readonly PlayerID Recipient = new PlayerID(2, false);
    private static readonly PlayerID Other = new PlayerID(3, false);
    private static readonly SceneID Scene = new SceneID(123);
    private static readonly Connection ActorConnection = new Connection(101);
    private static readonly Connection RecipientConnection = new Connection(102);
    private static readonly Connection OtherConnection = new Connection(103);
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly List<NetworkRules> _rules = new List<NetworkRules>();
    private NetworkManager _manager;
    private PlayersManager _players;
    private ScenePlayersModule _scenePlayers;
    private HierarchyV2 _hierarchy;
    private GlobalOwnershipModule _ownership;
    private SceneOwnership _sceneOwnership;
    private OwnershipAuthorizationTestTransport _transport;
    private bool _asServer;

    private sealed class OwnershipUpdate
    {
        public Connection recipient;
        public NetworkID identity;
        public PlayerID? owner;
        public bool isSpawner;
    }

    [OneTimeSetUp]
    public void RegisterPackers()
    {
        NetworkManager.CallAllRegisters();
    }

    [SetUp]
    public void SetUp()
    {
        var managerObject = CreateObject("Ownership authorization manager");
        managerObject.SetActive(false);
        _manager = managerObject.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;
        var adapter = managerObject.AddComponent<OwnershipAuthorizationTestTransportAdapter>();
        _transport = new OwnershipAuthorizationTestTransport();
        adapter.value = _transport;
        SetField(_manager, "_transport", adapter);

        var broadcast = new BroadcastModule(_manager, true);
        _players = new PlayersManager(_manager, null, broadcast);
        _players.SetBroadcaster(new PlayersBroadcaster(broadcast, _players));
        AddPlayer(Actor, ActorConnection);
        AddPlayer(Recipient, RecipientConnection);
        AddPlayer(Other, OtherConnection);
        var scenes = new ScenesModule(_manager, _players);
        _scenePlayers = new ScenePlayersModule(_manager, scenes, _players);
        GetField<Dictionary<SceneID, List<PlayerID>>>(_scenePlayers, "_scenePlayers")
            .Add(Scene, new List<PlayerID> { Actor, Recipient, Other });
        GetField<Dictionary<SceneID, List<PlayerID>>>(_scenePlayers, "_sceneLoadedPlayers")
            .Add(Scene, new List<PlayerID> { Actor, Recipient, Other });
        var factory = new HierarchyFactory(_manager, scenes, _scenePlayers, _players);

        _hierarchy = (HierarchyV2)FormatterServices.GetUninitializedObject(typeof(HierarchyV2));
        SetField(_hierarchy, "_spawnedIdentitiesMap", new Dictionary<NetworkID, NetworkIdentity>());
        SetField(_hierarchy, "_manager", _manager);
        SetField(_hierarchy, "_sceneId", Scene);
        GetField<Dictionary<SceneID, HierarchyV2>>(factory, "_hierarchies").Add(Scene, _hierarchy);
        _ownership = new GlobalOwnershipModule(_manager, factory, _players, _scenePlayers, scenes);

        var serverModules = new ModulesCollection(_manager, true);
        serverModules.AddModule(_players);
        serverModules.AddModule(_scenePlayers);
        serverModules.AddModule(factory);
        serverModules.AddModule(_ownership);
        SetField(_manager, "_serverModules", serverModules);
        SetField(_manager, "_clientModules", new ModulesCollection(_manager, false));
        SetRole(true);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var gameObject in _objects)
        {
            if (!gameObject)
                continue;
            foreach (var identity in gameObject.GetComponents<NetworkIdentity>())
            {
                identity.SetIsSpawned(false, true);
                identity.SetIsSpawned(false, false);
            }
        }
        if (_manager)
            SetField(_manager, "_transport", null);
        for (var i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i])
                UnityEngine.Object.DestroyImmediate(_objects[i]);
        foreach (var rules in _rules)
            if (rules)
                UnityEngine.Object.DestroyImmediate(rules);
        _objects.Clear();
        _rules.Clear();
    }

    [Test]
    public void ClientBatch_IsNeitherAppliedNorRelayed()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);

        ReceiveBatch(Actor, Info(identity.id.Value, Recipient), Info(new NetworkID(2, PlayerID.Server), Recipient));

        Assert.That(identity.GetOwner(true), Is.Null);
        Assert.That(_transport.sent, Is.Empty);
        Assert.That(PendingCount, Is.Zero);
    }

    [Test]
    public void ServerOnlyAssignment_IsNotAppliedAndOnlyCorrectsRequester()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Server);

        IgnoreRejectionLogs(() => ReceiveChange(Actor, identity.id.Value, true, Recipient));

        Assert.That(identity.GetOwner(true), Is.Null);
        AssertCorrected(identity.id.Value, null);
        Assert.That(PendingCount, Is.Zero);
    }

    [Test]
    public void MixedChange_RelaysAcceptedAndCorrectsRequesterForRejected()
    {
        var denied = CreateIdentity(1, ConnectionAuth.Server);
        var allowed = CreateIdentity(2, ConnectionAuth.Everyone);
        using var identities = DisposableList<NetworkID>.Create(2);
        identities.Add(denied.id.Value);
        identities.Add(allowed.id.Value);
        var change = new OwnershipChange
        {
            sceneId = Scene, identities = identities, isAdding = true, player = Recipient
        };

        IgnoreRejectionLogs(() => InvokeReceive(Actor, change));

        Assert.That(denied.GetOwner(true), Is.Null);
        Assert.That(allowed.GetOwner(true), Is.EqualTo(Recipient));
        var updates = ReadUpdates();
        Assert.That(updates.FindAll(update => update.identity == denied.id.Value)
            .ConvertAll(update => update.recipient), Is.EquivalentTo(new[] { ActorConnection }));
        Assert.That(updates.FindAll(update => update.identity == allowed.id.Value)
            .ConvertAll(update => update.recipient), Is.EquivalentTo(new[] { RecipientConnection, OtherConnection }));
    }

    [Test]
    public void ServerPending_IsBoundedForClientRequests()
    {
        using var identities = DisposableList<NetworkID>.Create(4096);
        for (ulong i = 1; i <= 4096; i++)
            identities.Add(new NetworkID(i, PlayerID.Server));

        InvokeReceive(Actor, new OwnershipChange
        {
            sceneId = Scene, identities = identities, isAdding = true, player = Recipient
        });

        Assert.That(PendingCount, Is.EqualTo(1024));
        Assert.That(_transport.sent, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExistingOwner_UsesTransferAuthority(bool actorIsOwner)
    {
        var identity = CreateIdentity(1, ConnectionAuth.Server);
        _sceneOwnership.GiveOwnership(identity, actorIsOwner ? Actor : Other);

        if (actorIsOwner)
            ReceiveChange(Actor, identity.id.Value, true, Recipient);
        else
            IgnoreRejectionLogs(() => ReceiveChange(Actor, identity.id.Value, true, Recipient));

        Assert.That(identity.GetOwner(true), Is.EqualTo(actorIsOwner ? Recipient : Other));
        if (actorIsOwner)
            AssertRelayed(identity.id.Value, Recipient, false);
        else
            AssertCorrected(identity.id.Value, Other);
    }

    [Test]
    public void EveryoneAssignment_DoesNotOverrideServerOnlyTransferRule()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone, ActionAuth.Server);
        _sceneOwnership.GiveOwnership(identity, Actor);

        IgnoreRejectionLogs(() => ReceiveChange(Actor, identity.id.Value, true, Recipient));

        Assert.That(identity.GetOwner(true), Is.EqualTo(Actor));
        AssertCorrected(identity.id.Value, Actor);
    }

    [Test]
    public void ObserverTransferRule_AllowsTransferByAnotherScenePlayer()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.Observer);
        _sceneOwnership.GiveOwnership(identity, Other);

        ReceiveChange(Actor, identity.id.Value, true, Recipient);

        Assert.That(identity.GetOwner(true), Is.EqualTo(Recipient));
        AssertRelayed(identity.id.Value, Recipient, false);
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void ClientRequest_RequiresConnectedActorInScene(bool batch, bool disconnect)
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        if (disconnect)
            GetField<List<PlayerID>>(_players, "_players").Remove(Actor);
        else
            GetField<Dictionary<SceneID, List<PlayerID>>>(_scenePlayers, "_scenePlayers")[Scene].Remove(Actor);

        if (batch)
            ReceiveBatch(Actor, Info(identity.id.Value, Recipient));
        else
            ReceiveChange(Actor, identity.id.Value, true, Recipient);

        Assert.That(identity.GetOwner(true), Is.Null);
        Assert.That(PendingCount, Is.Zero);
        Assert.That(_transport.sent, Is.Empty);
    }

    [Test]
    public void ServerAuthoritativeOwner_TakesPrecedenceOverOptimisticHostClientOwner()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        _sceneOwnership.GiveOwnership(identity, Other);
        identity.internalOwnerClient = Actor;

        IgnoreRejectionLogs(() => ReceiveChange(Actor, identity.id.Value, true, Recipient));

        Assert.That(identity.GetOwner(true), Is.EqualTo(Other));
        AssertCorrected(identity.id.Value, Other);
    }

    [Test]
    public void TrustedClientSnapshot_AppliesDespiteLocalAssignmentAndTransferRules()
    {
        SetRole(false);
        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.None);
        _sceneOwnership.GiveOwnership(identity, Actor);

        ReceiveBatch(PlayerID.Server, Info(identity.id.Value, Recipient));

        Assert.That(identity.GetOwner(false), Is.EqualTo(Recipient));
        Assert.That(ReadUpdates(), Is.Empty);
    }

    [Test]
    public void TrustedClientSingleChange_AppliesOwnerOnlyTransferFromServer()
    {
        SetRole(false);
        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.Owner);
        _sceneOwnership.GiveOwnership(identity, Actor);

        ReceiveChange(PlayerID.Server, identity.id.Value, true, Recipient);

        Assert.That(identity.GetOwner(false), Is.EqualTo(Recipient));
        Assert.That(_transport.sent, Is.Empty);
    }

    [Test]
    public void TrustedClientSnapshot_WaitsForMissingIdentityThenAppliesServerState()
    {
        SetRole(false);
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveBatch(PlayerID.Server, Info(id, Recipient));
        Assert.That(PendingCount, Is.EqualTo(1));

        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.None);
        _ownership.FixedUpdate();

        Assert.That(identity.GetOwner(false), Is.EqualTo(Recipient));
        Assert.That(PendingCount, Is.Zero);
        Assert.That(ReadUpdates(), Is.Empty);
    }

    [Test]
    public void TrustedClientPendingRemoval_PreservesOperationDespiteLocalRules()
    {
        SetRole(false);
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(PlayerID.Server, id, false, default);
        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.None, ActionAuth.None);
        _sceneOwnership.GiveOwnership(identity, Actor);

        _ownership.FixedUpdate();

        Assert.That(identity.GetOwner(false), Is.Null);
        Assert.That(identity.ownerChanges, Has.Count.EqualTo(1));
        Assert.That(identity.ownerChanges[0].newOwner, Is.Null);
        Assert.That(PendingCount, Is.Zero);
        Assert.That(_transport.sent, Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NewerTrustedResolvedOperation_SupersedesOlderPendingSnapshot(bool removal)
    {
        SetRole(false);
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveBatch(PlayerID.Server, Info(id, Recipient));
        Assert.That(PendingCount, Is.EqualTo(1));
        var identity = CreateIdentity(1, ConnectionAuth.Server, ActionAuth.None, ActionAuth.None);
        _sceneOwnership.GiveOwnership(identity, Actor);

        if (removal)
            ReceiveChange(PlayerID.Server, id, false, default);
        else
            ReceiveBatch(PlayerID.Server, Info(id, Other));
        _ownership.FixedUpdate();

        Assert.That(identity.GetOwner(false), Is.EqualTo(removal ? (PlayerID?)null : Other));
        Assert.That(PendingCount, Is.Zero);
        Assert.That(_transport.sent, Is.Empty);
    }

    [Test]
    public void ServerPendingAddition_AppliesAndRelaysAfterIdentityAppears()
    {
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(Actor, id, true, Recipient, true);
        Assert.That(PendingCount, Is.EqualTo(1));
        Assert.That(ReadUpdates(), Is.Empty);

        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        _ownership.FixedUpdate();

        Assert.That(identity.GetOwner(true), Is.EqualTo(Recipient));
        Assert.That(identity.ownerChanges, Has.Count.EqualTo(1));
        Assert.That(identity.ownerChanges[0].isSpawner, Is.True);
        Assert.That(PendingCount, Is.Zero);
        AssertRelayed(id, Recipient, false);
        foreach (var update in ReadUpdates())
            Assert.That(update.isSpawner, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ServerPendingAddition_RechecksActorConnectionAndSceneMembership(bool disconnect)
    {
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(Actor, id, true, Recipient);
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        if (disconnect)
            GetField<List<PlayerID>>(_players, "_players").Remove(Actor);
        else
            GetField<Dictionary<SceneID, List<PlayerID>>>(_scenePlayers, "_scenePlayers")[Scene].Remove(Actor);

        IgnoreRejectionLogs(() => _ownership.FixedUpdate());

        Assert.That(identity.GetOwner(true), Is.Null);
        Assert.That(PendingCount, Is.Zero);
        Assert.That(ReadUpdates(), Is.Empty);
    }

    [Test]
    public void ServerPendingAddition_RechecksResolvedIdentityAuthority()
    {
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(Actor, id, true, Recipient);
        var identity = CreateIdentity(1, ConnectionAuth.Server);

        IgnoreRejectionLogs(() => _ownership.FixedUpdate());

        Assert.That(identity.GetOwner(true), Is.Null);
        Assert.That(PendingCount, Is.Zero);
        Assert.That(ReadUpdates(), Is.Empty);
    }

    [Test]
    public void ServerPendingTransfer_RechecksActorAgainstCurrentOwner()
    {
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(Actor, id, true, Recipient);
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        _sceneOwnership.GiveOwnership(identity, Other);

        IgnoreRejectionLogs(() => _ownership.FixedUpdate());

        Assert.That(identity.GetOwner(true), Is.EqualTo(Other));
        Assert.That(PendingCount, Is.Zero);
        Assert.That(ReadUpdates(), Is.Empty);
    }

    [Test]
    public void ServerPendingRemoval_PreservesRemovalOperationAndActor()
    {
        var id = new NetworkID(1, PlayerID.Server);
        ReceiveChange(Actor, id, false, default);
        var identity = CreateIdentity(1, ConnectionAuth.Server);
        _sceneOwnership.GiveOwnership(identity, Actor);

        _ownership.FixedUpdate();

        Assert.That(identity.GetOwner(true), Is.Null);
        Assert.That(identity.ownerChanges, Has.Count.EqualTo(1));
        Assert.That(identity.ownerChanges[0].newOwner, Is.Null);
        Assert.That(PendingCount, Is.Zero);
        AssertRelayed(id, null, false);
    }

    [Test]
    public void RejectedRemoval_OnlyCorrectsRequester()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone,
            ActionAuth.Owner | ActionAuth.Server, ActionAuth.Server);
        _sceneOwnership.GiveOwnership(identity, Actor);

        IgnoreRejectionLogs(() => ReceiveChange(Actor, identity.id.Value, false, default));

        Assert.That(identity.GetOwner(true), Is.EqualTo(Actor));
        AssertCorrected(identity.id.Value, Actor);
    }

    [Test]
    public void AcceptedChange_RelaysAuthoritativeStateAfterOwnerCallback()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        identity.afterOwnerChanged = () => _sceneOwnership.GiveOwnership(identity, Other);

        ReceiveChange(Actor, identity.id.Value, true, Recipient);

        Assert.That(identity.GetOwner(true), Is.EqualTo(Other));
        AssertRelayed(identity.id.Value, Other);
    }

    [Test]
    public void AcceptedChange_CallbackRemovalIsRelayedAsRemoval()
    {
        var identity = CreateIdentity(1, ConnectionAuth.Everyone);
        identity.afterOwnerChanged = () => _sceneOwnership.RemoveOwnership(identity);

        ReceiveChange(Actor, identity.id.Value, true, Recipient);

        Assert.That(identity.GetOwner(true), Is.Null);
        AssertRelayed(identity.id.Value, null);
    }

    private void SetRole(bool asServer)
    {
        _asServer = asServer;
        SetProperty(_manager, "isServer", asServer);
        SetField(_ownership, "_asServer", asServer);
        SetField(_hierarchy, "_asServer", asServer);
        SetField(_players, "_asServer", asServer);
        _sceneOwnership = new SceneOwnership(asServer);
        var ownerships = GetField<Dictionary<SceneID, SceneOwnership>>(_ownership, "_sceneOwnerships");
        ownerships.Clear();
        ownerships.Add(Scene, _sceneOwnership);
    }

    private void AddPlayer(PlayerID player, Connection connection)
    {
        GetField<List<PlayerID>>(_players, "_players").Add(player);
        GetField<Dictionary<PlayerID, Connection>>(_players, "_playerToConnection").Add(player, connection);
        GetField<Dictionary<Connection, PlayerID>>(_players, "_connectionToPlayerId").Add(connection, player);
    }

    private GameObject CreateObject(string name)
    {
        var result = new GameObject(name);
        _objects.Add(result);
        return result;
    }

    private OwnershipAuthorizationTestIdentity CreateIdentity(ulong id, ConnectionAuth assign,
        ActionAuth transfer = ActionAuth.Owner | ActionAuth.Server,
        ActionAuth remove = ActionAuth.Owner | ActionAuth.Server)
    {
        var gameObject = CreateObject("Ownership identity " + id);
        gameObject.SetActive(false);
        var identity = gameObject.AddComponent<OwnershipAuthorizationTestIdentity>();
        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _rules.Add(rules);
        SetField(rules, "_defaultOwnershipRules", new OwnershipRules
        {
            assignAuth = assign,
            transferAuth = transfer,
            removeAuth = remove,
            overrideWhenPropagating = true
        });
        identity.SetNetworkRules(rules);
        identity.SetID(new NetworkID(id, PlayerID.Server));
        SetProperty(identity, "networkManager", _manager);
        SetProperty(identity, "sceneId", Scene);
        identity.SetIsSpawned(true, _asServer);
        GetField<Dictionary<NetworkID, NetworkIdentity>>(_hierarchy, "_spawnedIdentitiesMap")
            .Add(identity.id.Value, identity);
        return identity;
    }

    private static OwnershipInfo Info(NetworkID identity, PlayerID player)
        => new OwnershipInfo { identity = identity, player = player };

    private void ReceiveBatch(PlayerID actor, params OwnershipInfo[] entries)
    {
        using var state = DisposableList<OwnershipInfo>.Create(entries.Length);
        foreach (var entry in entries)
            state.Add(entry);
        InvokeReceive(actor, new OwnershipChangeBatch { scene = Scene, state = state });
    }

    private void ReceiveChange(PlayerID actor, NetworkID identity, bool adding, PlayerID player,
        bool isSpawner = false)
    {
        using var identities = DisposableList<NetworkID>.Create(1);
        identities.Add(identity);
        InvokeReceive(actor, new OwnershipChange
        {
            sceneId = Scene, identities = identities, isAdding = adding,
            player = player, isSpawner = isSpawner
        });
    }

    private void InvokeReceive<T>(PlayerID actor, T data)
    {
        var method = typeof(GlobalOwnershipModule).GetMethod("OnOwnershipChange",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(PlayerID), typeof(T), typeof(bool) }, null);
        Assert.That(method, Is.Not.Null);
        method.Invoke(_ownership, new object[] { actor, data, _asServer });
    }

    private int PendingCount => GetField<IList>(_ownership, "_pendingOwnership").Count;

    private List<OwnershipUpdate> ReadUpdates()
    {
        var result = new List<OwnershipUpdate>();
        foreach (var packet in _transport.sent)
        {
            using var stream = BitPackerPool.Get(packet.data);
            uint type = default;
            Packer<uint>.Read(stream, ref type);
            if (type == Hasher.GetStableHashU32<OwnershipChangeBatch>())
            {
                var batch = default(OwnershipChangeBatch);
                Packer<OwnershipChangeBatch>.Read(stream, ref batch);
                try
                {
                    Assert.That(batch.scene, Is.EqualTo(Scene));
                    for (var i = 0; i < batch.state.Count; i++)
                        result.Add(new OwnershipUpdate
                        {
                            recipient = packet.recipient,
                            identity = batch.state[i].identity,
                            owner = batch.state[i].player
                        });
                }
                finally { batch.Dispose(); }
            }
            else
            {
                Assert.That(type, Is.EqualTo(Hasher.GetStableHashU32<OwnershipChange>()));
                var change = default(OwnershipChange);
                Packer<OwnershipChange>.Read(stream, ref change);
                try
                {
                    Assert.That(change.sceneId, Is.EqualTo(Scene));
                    for (var i = 0; i < change.identities.Count; i++)
                        result.Add(new OwnershipUpdate
                        {
                            recipient = packet.recipient, identity = change.identities[i],
                            owner = change.isAdding ? change.player : (PlayerID?)null,
                            isSpawner = change.isSpawner
                        });
                }
                finally { change.Dispose(); }
            }
        }
        return result;
    }

    private void AssertRelayed(NetworkID identity, PlayerID? owner, bool includeRequester = true)
    {
        var updates = ReadUpdates();
        var expected = includeRequester
            ? new[] { ActorConnection, RecipientConnection, OtherConnection }
            : new[] { RecipientConnection, OtherConnection };
        Assert.That(updates, Has.Count.EqualTo(expected.Length));
        Assert.That(updates.ConvertAll(update => update.recipient),
            Is.EquivalentTo(expected));
        foreach (var update in updates)
        {
            Assert.That(update.identity, Is.EqualTo(identity));
            Assert.That(update.owner, Is.EqualTo(owner));
        }
    }

    private void AssertCorrected(NetworkID identity, PlayerID? owner)
    {
        var updates = ReadUpdates();
        Assert.That(updates, Has.Count.EqualTo(1));
        Assert.That(updates[0].recipient, Is.EqualTo(ActorConnection));
        Assert.That(updates[0].identity, Is.EqualTo(identity));
        Assert.That(updates[0].owner, Is.EqualTo(owner));
    }

    private static void IgnoreRejectionLogs(Action action)
    {
        var previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try { action(); }
        finally { LogAssert.ignoreFailingMessages = previous; }
    }

    private static T GetField<T>(object target, string name)
        => (T)FindField(target.GetType(), name).GetValue(target);

    private static void SetField(object target, string name, object value)
        => FindField(target.GetType(), name).SetValue(target, value);

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field;
            type = type.BaseType;
        }
        throw new MissingFieldException(name);
    }

    private static void SetProperty(object target, string name, object value)
    {
        var type = typeof(NetworkIdentity).IsAssignableFrom(target.GetType())
            ? typeof(NetworkIdentity) : target.GetType();
        type.GetProperty(name).SetValue(target, value);
    }
}

public class OwnershipAuthorizationTestIdentity : NetworkIdentity
{
    public struct OwnerChange
    {
        public PlayerID? newOwner;
        public bool isSpawner;
    }

    public readonly List<OwnerChange> ownerChanges = new List<OwnerChange>();
    public Action afterOwnerChanged;

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawner, bool asServer)
    {
        ownerChanges.Add(new OwnerChange { newOwner = newOwner, isSpawner = isSpawner });
        afterOwnerChanged?.Invoke();
    }
}

public class OwnershipAuthorizationTestTransportAdapter : GenericTransport
{
    public ITransport value;
    public override bool isSupported => true;
    public override ITransport transport => value;
    protected override void StartClientInternal() { }
    protected override void StartServerInternal() { }
}

public class OwnershipAuthorizationTestTransport : ITransport
{
    public sealed class Packet
    {
        public Connection recipient;
        public byte[] data;
    }

    public readonly List<Packet> sent = new List<Packet>();
#pragma warning disable CS0067
    public event OnConnected onConnected;
    public event OnDisconnected onDisconnected;
    public event OnDataReceived onDataReceived;
    public event OnDataSent onDataSent;
    public event OnConnectionState onConnectionState;
#pragma warning restore CS0067
    public ConnectionState clientState => ConnectionState.Disconnected;
    public ConnectionState listenerState => ConnectionState.Disconnected;
    public IReadOnlyList<Connection> connections => Array.Empty<Connection>();
    public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
    {
        var copy = new byte[data.length];
        Array.Copy(data.data, data.offset, copy, 0, data.length);
        sent.Add(new Packet { recipient = target, data = copy });
    }
    public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        => SendToClient(default, data, method);
    public void Connect(string ip, ushort port) { }
    public void Disconnect() { }
    public void Listen(ushort port) { }
    public void StopListening() { }
    public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) { }
    public void RaiseDataSent(Connection conn, ByteData data, bool asServer) { }
    public void CloseConnection(Connection conn) { }
    public void ReceiveMessages(float delta) { }
    public void SendMessages(float delta) { }
}

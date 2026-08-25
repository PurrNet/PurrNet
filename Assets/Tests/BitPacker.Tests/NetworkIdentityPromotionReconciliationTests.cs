using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NetworkIdentityPromotionReconciliationTests
{
    private readonly List<UnityEngine.Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                UnityEngine.Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
    }

    [Test]
    public void ReconcileClientRoleAsServer_PreservesLifetimeModulesAndRoleBindings()
    {
        var manager = CreateManager();
        var roleModules = CreateMigratedRoleModules(manager);
        SetModules(manager, "_clientModules", roleModules.collection);

        var identityRoot = new GameObject("Promotion reconciliation identity");
        _created.Add(identityRoot);
        var identity = identityRoot.AddComponent<PromotionReconciliationIdentity>();
        var scene = new SceneID(7);
        var id = new NetworkID(42);
        var owner = new PlayerID(9, false);

        identity.SetID(id);
        identity.SetIdentity(manager, null, scene, false, false);
        identity.internalOwnerClient = owner;
        identity.TriggerEarlySpawnEvent(false);
        identity.TriggerSpawnEvent(false);
        identity.module.value = 123;

        var retainedModule = identity.module;
        Assert.That(identity.globalEarlySpawnCount, Is.EqualTo(1));
        Assert.That(identity.globalSpawnCount, Is.EqualTo(1));
        Assert.That(identity.clientRoleSpawnCount, Is.EqualTo(1));
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(1));
        Assert.That(GetField<bool>(identity, "_wasEarlySpawned"), Is.True);

        var promotedModules = new ModulesCollection(manager, true);
        promotedModules.AddModule(roleModules.tick);
        promotedModules.AddModule(roleModules.players);
        promotedModules.AddModule(roleModules.scenePlayers);
        SetModules(manager, "_serverModules", promotedModules);
        SetModules(manager, "_clientModules", new ModulesCollection(manager, false));

        identity.ReconcileClientRoleAsServer(null);
        SetAutoProperty(manager, "isServer", true);
        identity.TriggerPromoteToServer();

        Assert.That(identity.IsSpawned(false), Is.False);
        Assert.That(identity.IsSpawned(true), Is.True);
        Assert.That(identity.GetNetworkID(true), Is.EqualTo(id));
        Assert.That(identity.internalOwnerServer, Is.EqualTo(owner));
        Assert.That(identity.internalOwnerClient, Is.Null);
        Assert.That(identity.globalEarlySpawnCount, Is.EqualTo(1));
        Assert.That(identity.globalSpawnCount, Is.EqualTo(1));
        Assert.That(identity.globalDespawnCount, Is.Zero);
        Assert.That(identity.clientRoleDespawnCount, Is.Zero);
        Assert.That(identity.serverRoleSpawnCount, Is.Zero);
        Assert.That(identity.promoteCount, Is.EqualTo(1));
        Assert.That(identity.moduleInitCount, Is.EqualTo(1));
        Assert.That(identity.module, Is.SameAs(retainedModule));
        Assert.That(identity.module.value, Is.EqualTo(123));
        Assert.That(identity.module.promoteCount, Is.EqualTo(1));
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(1));
        Assert.That(GetField<bool>(identity, "_wasEarlySpawned"), Is.True);

        Assert.That(GetField<PlayersManager>(identity, "_clientPlayerEventsSource"), Is.Null);
        Assert.That(GetField<PlayersManager>(identity, "_serverPlayerEventsSource"),
            Is.SameAs(roleModules.players));
        Assert.That(GetField<ScenePlayersModule>(identity, "_clientSceneEventsSource"), Is.Null);
        Assert.That(GetField<ScenePlayersModule>(identity, "_serverSceneEventsSource"),
            Is.SameAs(roleModules.scenePlayers));

        RaisePlayerJoined(roleModules.players, owner);
        Assert.That(identity.playerConnectedCount, Is.EqualTo(1));
        Assert.That(identity.lastPlayerEventAsServer, Is.True);

        RaiseSceneLoaded(roleModules.scenePlayers, owner, scene);
        Assert.That(identity.playerLoadedSceneCount, Is.EqualTo(1));

        SetField(roleModules.tick, "_lastTickTime",
            Time.unscaledTimeAsDouble - roleModules.tick.tickDeltaDouble * 1.1d);
        roleModules.tick.Update();
        Assert.That(identity.tickCount, Is.EqualTo(1),
            "The migrated tick source should invoke only the server role callback once.");

        var listenRoleModules = CreateMigratedRoleModules(manager);
        SetModules(manager, "_clientModules", listenRoleModules.collection);
        SetAutoProperty(manager, "isClient", true);
#pragma warning disable SYSLIB0050
        var freshClientHierarchy = (HierarchyV2)FormatterServices.GetUninitializedObject(
            typeof(HierarchyV2));
#pragma warning restore SYSLIB0050
        Assert.That(identity.AttachPromotedListenClientRole(freshClientHierarchy,
            out var newlyAttached, out var attachFailure), Is.True, attachFailure);
        Assert.That(newlyAttached, Is.True);

        Assert.That(identity.IsSpawned(false), Is.True);
        Assert.That(identity.IsSpawned(true), Is.True);
        Assert.That(identity.globalEarlySpawnCount, Is.EqualTo(1));
        Assert.That(identity.globalSpawnCount, Is.EqualTo(1));
        Assert.That(identity.clientRoleSpawnCount, Is.EqualTo(1));
        Assert.That(identity.moduleInitCount, Is.EqualTo(1));
        Assert.That(identity.module, Is.SameAs(retainedModule));
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(2));
        Assert.That(GetField<PlayersManager>(identity, "_clientPlayerEventsSource"),
            Is.SameAs(listenRoleModules.players));
        Assert.That(GetField<ScenePlayersModule>(identity, "_clientSceneEventsSource"),
            Is.SameAs(listenRoleModules.scenePlayers));

        SetField(listenRoleModules.tick, "_lastTickTime",
            Time.unscaledTimeAsDouble - listenRoleModules.tick.tickDeltaDouble * 1.1d);
        listenRoleModules.tick.Update();
        Assert.That(identity.tickCount, Is.EqualTo(2),
            "The attached listen role should own ticking while both host roles are live.");

        identity.TriggerDespawnEvent(false);
        Assert.That(identity.globalDespawnCount, Is.Zero);
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(1));
        Assert.That(GetField<bool>(identity, "_wasEarlySpawned"), Is.True,
            "Detaching one host role must not reset the identity-wide early-spawn lifetime.");

        Assert.That(identity.AttachPromotedListenClientRole(freshClientHierarchy,
            out newlyAttached, out attachFailure), Is.True, attachFailure);
        Assert.That(newlyAttached, Is.True);

        Assert.That(identity.globalEarlySpawnCount, Is.EqualTo(1));
        Assert.That(identity.globalSpawnCount, Is.EqualTo(1));
        Assert.That(identity.clientRoleSpawnCount, Is.EqualTo(1));
        Assert.That(identity.moduleInitCount, Is.EqualTo(1));
        Assert.That(identity.module, Is.SameAs(retainedModule));
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(2));

        identity.TriggerDespawnEvent(false);
        Assert.That(identity.globalDespawnCount, Is.Zero);
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.EqualTo(1));
        Assert.That(GetField<bool>(identity, "_wasEarlySpawned"), Is.True);

        identity.TriggerDespawnEvent(true);
        Assert.That(identity.globalDespawnCount, Is.EqualTo(1));
        Assert.That(GetField<int>(identity, "_spawnedCount"), Is.Zero);
        Assert.That(GetField<bool>(identity, "_wasEarlySpawned"), Is.False);

        SetField(identity, "_clientHierarchy", null);
    }

    [Test]
    public void PromotionSimulationPause_DoesNotTickOrCatchUpPausedTime()
    {
        var manager = CreateManager();
        var broadcast = new BroadcastModule(manager, true);
        var tick = new TickManager(20, manager, broadcast, true);
        var serverModules = new ModulesCollection(manager, true);
        serverModules.AddModule(tick);
        GetField<List<IUpdate>>(serverModules, "_updateListeners").Add(tick);
        SetModules(manager, "_serverModules", serverModules);

        var ticks = 0;
        tick.onTick += () => ticks++;
        SetField(tick, "_lastTickTime", Time.unscaledTimeAsDouble - tick.tickDeltaDouble * 1.1d);
        SetField(manager, "_isPromotionSimulationPaused", true);

        InvokePrivate(manager, "Update");
        Assert.That(ticks, Is.Zero);

        tick.RebaseAfterSimulationPause();
        SetField(manager, "_isPromotionSimulationPaused", false);
        InvokePrivate(manager, "Update");
        Assert.That(ticks, Is.Zero, "Resuming must not replay ticks accumulated during the pause.");

        SetField(tick, "_lastTickTime", Time.unscaledTimeAsDouble - tick.tickDeltaDouble * 1.1d);
        InvokePrivate(manager, "Update");
        Assert.That(ticks, Is.EqualTo(1));
    }

    [Test]
    public void TickTransfer_PreservesTickEstimatesAndClearsOldPathState()
    {
        var manager = CreateManager();
        var broadcast = new BroadcastModule(manager, false);
        var tick = new TickManager(20, manager, broadcast, false);

        SetField(tick, "<localTick>k__BackingField", 91u);
        SetField(tick, "_syncedTick", 73u);
        SetField(tick, "<rtt>k__BackingField", 0.25d);
        SetField(tick, "_lastSyncTime", 42f);
        tick.tickPacingScale = TickManager.maxTickPacingScale;

        tick.TransferToNewServer();

        Assert.That(tick.localTick, Is.EqualTo(91u));
        Assert.That(tick.syncedTick, Is.EqualTo(73u));
        Assert.That(tick.rtt, Is.Zero);
        Assert.That(tick.tickPacingScale, Is.EqualTo(1d));
        Assert.That(GetField<float>(tick, "_lastSyncTime"), Is.EqualTo(-99f));
    }

    [Test]
    public void TickTransfer_RejectsLateResponsesFromThePreviousAuthority()
    {
        var manager = CreateManager();
        var broadcast = new BroadcastModule(manager, false);
        var tick = new TickManager(20, manager, broadcast, false);
        SetField(tick, "_syncedTick", 73u);
        SetField(tick, "_latestSyncRequestTime", 12f);

        tick.TransferToNewServer();

        InvokePrivate(tick, "OnServerRespondedPing", default(Connection),
            new TickManagerResponseLocalTick
            {
                requestTime = 12f,
                tick = 500
            }, false);

        Assert.That(tick.syncedTick, Is.EqualTo(73u));
        Assert.That(tick.rtt, Is.Zero);
    }

    [Test]
    public void ReboundCallbackFailure_FaultsReadinessAfterParticipantsRun()
    {
        var root = new GameObject("Throwing rebound identity");
        _created.Add(root);
        var identity = root.AddComponent<ThrowingReboundIdentity>();
        var transition = new HostMigrationTransitionOptions("session", 4);

        LogAssert.Expect(LogType.Exception,
            new Regex("InvalidOperationException: rebound failed"));
        var exception = Assert.ThrowsAsync<AggregateException>(async () =>
            await identity.TriggerOnHostMigrationRebound(transition));

        Assert.That(identity.readinessCount, Is.EqualTo(1));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ToString(), Does.Contain("OnHostMigrationRebound failed"));
    }

    [Test]
    public async Task DualRoleFreshListenRebound_CompletesAfterAuthoritativePromotionBarrier()
    {
        var root = new GameObject("Dual-role migration identity");
        _created.Add(root);
        var identity = root.AddComponent<DualRoleMigrationIdentity>();
        var transition = new HostMigrationTransitionOptions("session", 5);

        var promotion = identity.TriggerPromoteToServer(transition);
        Assert.That(identity.isAuthoritative, Is.True);
        Assert.That(promotion.IsCompleted, Is.False,
            "Package promotion readiness remains the authority-transition barrier.");

        identity.CompletePromotion();
        await promotion;

        var failures = new List<Exception>();
        identity.TriggerBeginHostMigrationReconciliation(transition, failures);
        var rebound = identity.TriggerOnHostMigrationRebound(transition);

        Assert.That(failures, Is.Empty);
        Assert.That(rebound.IsCompletedSuccessfully, Is.True,
            "A fresh listen-client rebound must not wait for a second client-authority frame " +
            "from an identity that is already authoritative in the server role.");
        Assert.That(identity.beginCount, Is.EqualTo(1));
        Assert.That(identity.reboundCount, Is.EqualTo(1));
        Assert.That(identity.clientReadinessCount, Is.EqualTo(1));
    }

    [Test]
    public void ServerBaselineParticipant_IsDispatchedWithExactPlayerAndTransition()
    {
        var root = new GameObject("Server baseline participant");
        _created.Add(root);
        var identity = root.AddComponent<ServerBaselineParticipantIdentity>();
        var player = new PlayerID(27, false);
        var transition = new HostMigrationTransitionOptions("baseline-dispatch", 9);
        List<Exception> failures = null;

        identity.TriggerPrepareHostMigrationServerBaseline(
            player, transition, ref failures);

        Assert.That(failures, Is.Null);
        Assert.That(identity.callCount, Is.EqualTo(1));
        Assert.That(identity.lastPlayer, Is.EqualTo(player));
        Assert.That(identity.lastTransition, Is.EqualTo(transition));
    }

    [Test]
    public async Task HostMigrationModuleRoster_IsFrozenBeforeEveryApplicationCallbackPhase()
    {
        var transition = new HostMigrationTransitionOptions("module-roster-snapshot", 10);
        var player = new PlayerID(28, false);

        var promotion = CreateRosterMutationProbe("promotion roster");
        await promotion.identity.TriggerPromoteToServer(transition);
        Assert.That(promotion.first.promoteCount, Is.EqualTo(1));
        Assert.That(promotion.removed.promoteCount, Is.EqualTo(1));
        Assert.That(promotion.first.promotionReadinessCount, Is.EqualTo(1));
        Assert.That(promotion.removed.promotionReadinessCount, Is.EqualTo(1));
        Assert.That(promotion.added.promoteCount, Is.Zero);
        Assert.That(promotion.added.promotionReadinessCount, Is.Zero);

        var rebound = CreateRosterMutationProbe("rebound roster");
        await rebound.identity.TriggerOnHostMigrationRebound(transition);
        Assert.That(rebound.first.reboundCount, Is.EqualTo(1));
        Assert.That(rebound.removed.reboundCount, Is.EqualTo(1));
        Assert.That(rebound.first.reconciliationReadinessCount, Is.EqualTo(1));
        Assert.That(rebound.removed.reconciliationReadinessCount, Is.EqualTo(1));
        Assert.That(rebound.added.reboundCount, Is.Zero);
        Assert.That(rebound.added.reconciliationReadinessCount, Is.Zero);

        var begin = CreateRosterMutationProbe("begin roster");
        var beginFailures = new List<Exception>();
        begin.identity.TriggerBeginHostMigrationReconciliation(transition, beginFailures);
        Assert.That(beginFailures, Is.Empty);
        Assert.That(begin.first.beginCount, Is.EqualTo(1));
        Assert.That(begin.removed.beginCount, Is.EqualTo(1));
        Assert.That(begin.added.beginCount, Is.Zero);

        var baseline = CreateRosterMutationProbe("baseline roster");
        List<Exception> baselineFailures = null;
        baseline.identity.TriggerPrepareHostMigrationServerBaseline(
            player, transition, ref baselineFailures);
        Assert.That(baselineFailures, Is.Null);
        Assert.That(baseline.first.baselineCount, Is.EqualTo(1));
        Assert.That(baseline.removed.baselineCount, Is.EqualTo(1));
        Assert.That(baseline.added.baselineCount, Is.Zero);

        var manual = CreateRosterMutationProbe("manual-root roster");
        var manualFailures = new List<Exception>();
        Assert.That(manual.identity.OwnsHostMigrationManualRoot(
            manual.identity, manualFailures), Is.False);
        Assert.That(manualFailures, Is.Empty);
        Assert.That(manual.first.manualRootCount, Is.EqualTo(1));
        Assert.That(manual.removed.manualRootCount, Is.EqualTo(1));
        Assert.That(manual.added.manualRootCount, Is.Zero);
    }

    private (RosterMutatingMigrationIdentity identity, RosterMigrationModule first,
        RosterMigrationModule removed, RosterMigrationModule added)
        CreateRosterMutationProbe(string name)
    {
        var root = new GameObject(name);
        _created.Add(root);
        var identity = root.AddComponent<RosterMutatingMigrationIdentity>();
        var first = new RosterMigrationModule();
        var removed = new RosterMigrationModule();
        var added = new RosterMigrationModule();
        identity.RegisterModuleInternal("first", typeof(RosterMigrationModule).FullName,
            first, false);
        identity.RegisterModuleInternal("removed", typeof(RosterMigrationModule).FullName,
            removed, false);

        identity.mutateRoster = () =>
        {
            GetField<List<NetworkModule>>(identity, "_externalModulesView").Remove(removed);
            identity.RegisterModuleInternal("added", typeof(RosterMigrationModule).FullName,
                added, false);
        };
        return (identity, first, removed, added);
    }

    private NetworkManager CreateManager()
    {
        var root = new GameObject("Promotion reconciliation manager");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<HostMigrationCoreTestTransport>();
        var manager = root.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        manager.transport = transport;
        SetModules(manager, "_serverModules", new ModulesCollection(manager, true));
        SetModules(manager, "_clientModules", new ModulesCollection(manager, false));
        return manager;
    }

    private static (ModulesCollection collection, TickManager tick, PlayersManager players,
        ScenePlayersModule scenePlayers) CreateMigratedRoleModules(NetworkManager manager)
    {
        var broadcast = new BroadcastModule(manager, false);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, false);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var playersBroadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(playersBroadcaster);
        auth.SetPlayerModule(players);
        var scenes = new ScenesModule(manager, players);
        var scenePlayers = new ScenePlayersModule(manager, scenes, players);
        scenes.SetScenePlayers(scenePlayers);
        var tick = new TickManager(20, manager, broadcast, false);

        var collection = new ModulesCollection(manager, false);
        collection.AddModule(tick);
        collection.AddModule(players);
        collection.AddModule(scenePlayers);
        return (collection, tick, players, scenePlayers);
    }

    private static void RaiseSceneLoaded(ScenePlayersModule module, PlayerID player, SceneID scene)
    {
        var callback = GetField<OnPlayerSceneEvent>(module, "onPlayerLoadedScene");
        callback?.Invoke(player, scene, true);
    }

    private static void RaisePlayerJoined(PlayersManager module, PlayerID player)
    {
        var callback = GetField<OnPlayerJoinedEvent>(module, "onPlayerJoined");
        callback?.Invoke(player, false, true);
    }

    private static void SetModules(NetworkManager manager, string name, ModulesCollection modules) =>
        typeof(NetworkManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, modules);

    private static void SetAutoProperty(object target, string property, object value) =>
        FindField(target.GetType(), $"<{property}>k__BackingField").SetValue(target, value);

    private static T GetField<T>(object target, string name) =>
        (T)FindField(target.GetType(), name).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        FindField(target.GetType(), name).SetValue(target, value);

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        throw new MissingFieldException(name);
    }

    private static void InvokePrivate(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, null);

    private static void InvokePrivate(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);
}

public sealed class PromotionReconciliationIdentity : NetworkIdentity, ITick, IPlayerEvents, IServerSceneEvents
{
    [NonSerialized]
    public PromotionReconciliationModule module;
    public int moduleInitCount;
    public int globalEarlySpawnCount;
    public int globalSpawnCount;
    public int globalDespawnCount;
    public int clientRoleSpawnCount;
    public int serverRoleSpawnCount;
    public int clientRoleDespawnCount;
    public int serverRoleDespawnCount;
    public int promoteCount;
    public int tickCount;
    public int playerConnectedCount;
    public int playerLoadedSceneCount;
    public bool lastPlayerEventAsServer;

    protected override void OnInitializeModules()
    {
        moduleInitCount++;
        module ??= new PromotionReconciliationModule();
    }

    protected override void OnEarlySpawn() => globalEarlySpawnCount++;

    protected override void OnSpawned() => globalSpawnCount++;

    protected override void OnDespawned() => globalDespawnCount++;

    protected override void OnSpawned(bool asServer)
    {
        if (asServer) serverRoleSpawnCount++;
        else clientRoleSpawnCount++;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer) serverRoleDespawnCount++;
        else clientRoleDespawnCount++;
    }

    protected override void PromoteToServer() => promoteCount++;

    public void OnTick(float delta) => tickCount++;

    public void OnPlayerConnected(PlayerID playerId, bool isReconnect, bool asServer)
    {
        playerConnectedCount++;
        lastPlayerEventAsServer = asServer;
    }

    public void OnPlayerDisconnected(PlayerID playerId, bool asServer) { }

    public void OnPlayerLoadedScene(PlayerID playerId) => playerLoadedSceneCount++;

    public void OnPlayerUnloadedScene(PlayerID playerId) { }
}

public sealed class PromotionReconciliationModule : NetworkModule
{
    public int value;
    public int promoteCount;

    public override void PromoteToServer() => promoteCount++;
}

public sealed class ThrowingReboundIdentity : NetworkIdentity, IHostMigrationReconciliationParticipant
{
    public int readinessCount;

    protected override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        throw new InvalidOperationException("rebound failed");

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) { }

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition)
    {
        readinessCount++;
        return Task.CompletedTask;
    }
}

public sealed class DualRoleMigrationIdentity : NetworkIdentity,
    IHostMigrationPromotionParticipant, IHostMigrationReconciliationParticipant
{
    private readonly TaskCompletionSource<bool> _promotionReady = new();

    public bool isAuthoritative { get; private set; }
    public int beginCount { get; private set; }
    public int reboundCount { get; private set; }
    public int clientReadinessCount { get; private set; }

    protected override void PromoteToServer() => isAuthoritative = true;

    protected override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        reboundCount++;

    public Task ReconcileHostMigrationPromotionAsync(HostMigrationTransitionOptions transition) =>
        _promotionReady.Task;

    public void CompletePromotion() => _promotionReady.TrySetResult(true);

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition)
    {
        beginCount++;
        if (!isAuthoritative)
            throw new InvalidOperationException("The package has not crossed its promotion barrier.");
    }

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition)
    {
        clientReadinessCount++;
        return isAuthoritative
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException(
                "A dual-role rebound cannot manufacture a second client-authority frame."));
    }
}

public sealed class ServerBaselineParticipantIdentity : NetworkIdentity,
    IHostMigrationServerBaselineParticipant
{
    public int callCount { get; private set; }
    public PlayerID lastPlayer { get; private set; }
    public HostMigrationTransitionOptions lastTransition { get; private set; }

    public void PrepareHostMigrationServerBaseline(PlayerID player,
        HostMigrationTransitionOptions transition)
    {
        callCount++;
        lastPlayer = player;
        lastTransition = transition;
    }
}

public sealed class RosterMutatingMigrationIdentity : NetworkIdentity,
    IHostMigrationManualHierarchyParticipant, IHostMigrationPromotionParticipant,
    IHostMigrationServerBaselineParticipant
{
    public Action mutateRoster;

    protected override void PromoteToServer() => mutateRoster?.Invoke();

    protected override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        mutateRoster?.Invoke();

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) =>
        mutateRoster?.Invoke();

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition) =>
        Task.CompletedTask;

    public Task ReconcileHostMigrationPromotionAsync(
        HostMigrationTransitionOptions transition) => Task.CompletedTask;

    public void PrepareHostMigrationServerBaseline(PlayerID player,
        HostMigrationTransitionOptions transition) => mutateRoster?.Invoke();

    public bool OwnsHostMigrationManualRoot(NetworkIdentity root)
    {
        mutateRoster?.Invoke();
        return false;
    }
}

public sealed class RosterMigrationModule : NetworkModule,
    IHostMigrationManualHierarchyParticipant, IHostMigrationPromotionParticipant,
    IHostMigrationServerBaselineParticipant
{
    public int promoteCount { get; private set; }
    public int promotionReadinessCount { get; private set; }
    public int reboundCount { get; private set; }
    public int beginCount { get; private set; }
    public int reconciliationReadinessCount { get; private set; }
    public int baselineCount { get; private set; }
    public int manualRootCount { get; private set; }

    public override void PromoteToServer() => promoteCount++;

    public Task ReconcileHostMigrationPromotionAsync(
        HostMigrationTransitionOptions transition)
    {
        promotionReadinessCount++;
        return Task.CompletedTask;
    }

    public override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        reboundCount++;

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) =>
        beginCount++;

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition)
    {
        reconciliationReadinessCount++;
        return Task.CompletedTask;
    }

    public void PrepareHostMigrationServerBaseline(PlayerID player,
        HostMigrationTransitionOptions transition) => baselineCount++;

    public bool OwnsHostMigrationManualRoot(NetworkIdentity root)
    {
        manualRootCount++;
        return false;
    }
}

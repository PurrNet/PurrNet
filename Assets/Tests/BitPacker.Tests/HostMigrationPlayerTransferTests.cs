using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Authentication;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class HostMigrationPlayerTransferTests
{
    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
    }

    [Test]
    public void FreshPromotedListenClient_SeedsRetainedServerPlayerId()
    {
        var retained = new PlayerID(12, false);

        Assert.That(PlayersManager.ResolveRetainedTransferLocalPlayer(
            null, true, retained), Is.EqualTo(retained));
        Assert.That(PlayersManager.ResolveRetainedTransferLocalPlayer(
            null, false, retained), Is.Null,
            "An ordinary fresh client must wait for its server-assigned PlayerID.");
        Assert.That(PlayersManager.ResolveRetainedTransferLocalPlayer(
            new PlayerID(3, false), true, retained),
            Is.EqualTo(new PlayerID(3, false)),
            "An existing transfer graph keeps its own retained PlayerID.");
    }

    [Test]
    public void ScopedTransfer_RejectsRosterWithoutRetainedLocalPlayerBeforeMutation()
    {
        var (manager, players) = CreatePlayersManager();
        var localPlayer = new PlayerID(5, false);
        var otherPlayer = new PlayerID(7, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 4,
            new[] { otherPlayer });
        SetExpectedTransition(manager, transition);
        SetLocalPlayer(players, localPlayer);
        GetPlayers(players).Add(localPlayer);
        var leftCount = 0;
        players.onPlayerLeft += (_, _) => leftCount++;

        players.TransferToNewServer();

        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out var failure),
            Is.True);
        StringAssert.Contains(localPlayer.ToString(), failure);
        Assert.That(players.localPlayerId, Is.EqualTo(localPlayer));
        Assert.That(players.retainedTransferLocalPlayerId, Is.EqualTo(localPlayer));
        Assert.That(players.players, Is.EqualTo(new[] { localPlayer }));
        Assert.That(leftCount, Is.Zero);

        players.ResetHostMigrationTransferReconciliation();
        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out _), Is.False);
        Assert.That(players.retainedTransferLocalPlayerId, Is.Null);
        Assert.That(players.HasHostMigrationClientReadyAcceptance(transition), Is.False);
    }

    [Test]
    public void ScopedTransfer_RejectsDifferentLoginPlayerIdWithoutPublishingIt()
    {
        var (manager, players) = CreatePlayersManager();
        var retainedPlayer = new PlayerID(5, false);
        var reassignedPlayer = new PlayerID(8, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 5,
            new[] { retainedPlayer });
        SetExpectedTransition(manager, transition);
        SetLocalPlayer(players, retainedPlayer);
        GetPlayers(players).Add(retainedPlayer);
        var receivedIdCount = 0;
        players.onLocalPlayerReceivedID += _ => receivedIdCount++;
        players.TransferToNewServer();

        AdvertiseMigrationSession(players, transition);
        Invoke(players, "OnClientLoginResponse", new Connection(1),
            new ServerLoginResponse(reassignedPlayer,
                new NetworkID(0, reassignedPlayer)), false);

        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out var failure),
            Is.True);
        StringAssert.Contains(reassignedPlayer.ToString(), failure);
        StringAssert.Contains(retainedPlayer.ToString(), failure);
        Assert.That(players.localPlayerId, Is.Null);
        Assert.That(receivedIdCount, Is.Zero);
    }

    [Test]
    public void ScopedTransfer_RejectsMismatchedAdvertisementBeforeLogin()
    {
        var (manager, players) = CreatePlayersManager();
        var retainedPlayer = new PlayerID(5, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 5,
            new[] { retainedPlayer });
        SetExpectedTransition(manager, transition);
        SetLocalPlayer(players, retainedPlayer);
        GetPlayers(players).Add(retainedPlayer);
        players.TransferToNewServer();

        AdvertiseMigrationSession(players,
            new HostMigrationTransitionOptions("other-incarnation", 5));
        Invoke(players, "OnClientLoginResponse", new Connection(1),
            new ServerLoginResponse(retainedPlayer,
                new NetworkID(0, retainedPlayer)), false);

        Assert.That(manager.hasReceivedHostMigrationSession, Is.True);
        Assert.That(manager.isHostMigrationSessionValidated, Is.False);
        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out var failure),
            Is.True);
        StringAssert.Contains("other-incarnation", failure);
        Assert.That(players.localPlayerId, Is.Null,
            "The ordered session advertisement must fence the following login response.");
    }

    [Test]
    public void OrdinaryLoginResponse_DoesNotTouchMigrationSessionState()
    {
        var (manager, players) = CreatePlayersManager();
        var assignedPlayer = new PlayerID(4, false);

        Invoke(players, "OnClientLoginResponse", new Connection(1),
            new ServerLoginResponse(assignedPlayer,
                new NetworkID(0, assignedPlayer), "application-cookie"), false);

        Assert.That(manager.hasReceivedHostMigrationSession, Is.False);
        Assert.That(manager.isHostMigrationSessionValidated, Is.False);
        Assert.That(players.localPlayerId, Is.EqualTo(assignedPlayer));
    }

    [Test]
    public void ScopedTransfer_FirstSnapshotPrunesOmittedRemoteAndAcceptsExtras()
    {
        var (manager, players) = CreatePlayersManager();
        var localPlayer = new PlayerID(3, false);
        var retainedPeer = new PlayerID(9, false);
        var unexpectedExtra = new PlayerID(11, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 6,
            new[] { localPlayer, retainedPeer });
        SetExpectedTransition(manager, transition);
        SetLocalPlayer(players, localPlayer);
        GetPlayers(players).Add(localPlayer);
        GetPlayers(players).Add(retainedPeer);
        var joinedCount = 0;
        var leftCount = 0;
        players.onPlayerJoined += (_, _, _) => joinedCount++;
        players.onPlayerLeft += (_, _) => leftCount++;
        players.TransferToNewServer();

        InvokeLogin(players, transition, localPlayer);
        var snapshot = DisposableList<PlayerJoinedEvent>.Create(2);
        snapshot.Add(new PlayerJoinedEvent(localPlayer, new Connection(1), null));
        snapshot.Add(new PlayerJoinedEvent(unexpectedExtra, new Connection(2), null));
        Invoke(players, "OnPlayerSnapshotEvent", new Connection(1),
            new PlayerSnapshotEvent(snapshot), false);

        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out _), Is.False);
        Assert.That(players.players, Is.EquivalentTo(new[] { localPlayer, unexpectedExtra }));
        Assert.That(players.IsValidPlayer(retainedPeer), Is.False,
            "The new host's authoritative omission must prune stale local roster state.");
        Assert.That(players.IsValidPlayer(unexpectedExtra), Is.True);
        Assert.That(joinedCount, Is.EqualTo(1));
        Assert.That(leftCount, Is.EqualTo(1));
        Assert.That(players.HasValidatedHostMigrationTransferSnapshot(transition), Is.True);
    }

    [Test]
    public void PromotionRoster_UsesCandidateKnownIntersection()
    {
        var (_, players) = CreatePlayersManager();
        var localPlayer = new PlayerID(5, false);
        var knownRemote = new PlayerID(7, false);
        var unknownRemote = new PlayerID(9, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 6,
            new[] { localPlayer, knownRemote, unknownRemote });
        SetLocalPlayer(players, localPlayer);
        GetPlayers(players).Add(localPlayer);
        GetPlayers(players).Add(knownRemote);

        Assert.That(players.ValidateExpectedHostMigrationRoster(transition, out var failure),
            Is.True, failure);
        typeof(PlayersManager).GetField("_promotedLocalPlayerId",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(players, (PlayerID?)localPlayer);
        Invoke(players, "BeginHostMigrationRoster", transition);

        Assert.That(players.retainedHostMigrationPlayers,
            Is.EquivalentTo(new[] { localPlayer, knownRemote }));
        Assert.That(players.pendingHostMigrationPlayers,
            Is.EquivalentTo(new[] { localPlayer, knownRemote }));
    }

    [Test]
    public void ScopedTransfer_CompleteSnapshotAllowsExtrasAndTracksExactReadyAck()
    {
        var (manager, players) = CreatePlayersManager();
        var localPlayer = new PlayerID(3, false);
        var retainedPeer = new PlayerID(9, false);
        var unexpectedExtra = new PlayerID(11, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 7,
            new[] { localPlayer, retainedPeer });
        SetExpectedTransition(manager, transition);
        SetLocalPlayer(players, localPlayer);
        GetPlayers(players).Add(localPlayer);
        GetPlayers(players).Add(retainedPeer);
        var joinedCount = 0;
        players.onPlayerJoined += (_, _, _) => joinedCount++;
        players.TransferToNewServer();

        InvokeLogin(players, transition, localPlayer);
        var snapshot = DisposableList<PlayerJoinedEvent>.Create(3);
        snapshot.Add(new PlayerJoinedEvent(localPlayer, new Connection(1), null));
        snapshot.Add(new PlayerJoinedEvent(retainedPeer, new Connection(2), null));
        snapshot.Add(new PlayerJoinedEvent(unexpectedExtra, new Connection(3), null));
        Invoke(players, "OnPlayerSnapshotEvent", new Connection(1),
            new PlayerSnapshotEvent(snapshot), false);

        Assert.That(players.TryGetHostMigrationTransferFailure(transition, out _), Is.False);
        Assert.That(players.HasValidatedHostMigrationTransferSnapshot(transition), Is.True);
        Assert.That(players.IsValidPlayer(unexpectedExtra), Is.True);
        Assert.That(joinedCount, Is.EqualTo(1),
            "Only the snapshot extra is a new logical join; retained members must not replay it.");

        players.PostTransferToNewServer();
        Invoke(players, "OnHostMigrationClientReadyAccepted", PlayerID.Server,
            new HostMigrationClientReadyAcceptedPacket
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch - 1
            }, false);
        Assert.That(players.HasHostMigrationClientReadyAcceptance(transition), Is.False);

        Invoke(players, "OnHostMigrationClientReadyAccepted", PlayerID.Server,
            new HostMigrationClientReadyAcceptedPacket
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch
            }, false);
        Assert.That(players.HasHostMigrationClientReadyAcceptance(transition), Is.True);
    }

    [Test]
    public void PendingRetainedPeer_DoesNotBlockUnrelatedOrdinaryJoin()
    {
        var (_, players) = CreateServerPlayersManager();
        var retainedPlayer = new PlayerID(13, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 8,
            new[] { retainedPlayer });
        GetPlayers(players).Add(retainedPlayer);
        Invoke(players, "BeginHostMigrationRoster", transition);

        var joined = new List<PlayerID>();
        var retainedRebounds = 0;
        players.onPlayerJoined += (player, _, asServer) =>
        {
            Assert.That(asServer, Is.True);
            joined.Add(player);
        };
        players.onHostMigrationConnectionRebound += (_, _, _) => retainedRebounds++;

        var ordinaryConnection = new Connection(41);
        Invoke(players, "OnClientAuthed", ordinaryConnection,
            new AuthenticationResponse { success = true, cookie = "new-player-cookie" });

        Assert.That(players.pendingHostMigrationPlayers, Is.EqualTo(new[] { retainedPlayer }),
            "The unrelated join must not consume the old peer's reconnect reservation.");
        Assert.That(joined, Has.Count.EqualTo(1));
        Assert.That(joined[0], Is.Not.EqualTo(retainedPlayer));
        Assert.That(retainedRebounds, Is.Zero,
            "Only a known retained credential may enter the exact per-player rebound path.");
        Assert.That(players.TryGetConnection(joined[0], out var actualConnection), Is.True);
        Assert.That(actualConnection, Is.EqualTo(ordinaryConnection));
    }

    [Test]
    public void PendingRetainedCookie_ClaimlessJoinDoesNotStealFallbackMapping()
    {
        var (manager, players) = CreateServerPlayersManager();
        var retainedPlayer = new PlayerID(13, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 8,
            new[] { retainedPlayer });
        manager.ConfigureHostMigrationSession(transition);
        GetPlayers(players).Add(retainedPlayer);
        GetCookieToPlayerId(players)["shared-app-cookie"] = retainedPlayer;
        GetPlayerIdToCookie(players)[retainedPlayer] = "shared-app-cookie";
        Invoke(players, "BeginHostMigrationRoster", transition);

        var joined = new List<PlayerID>();
        players.onPlayerJoined += (player, _, _) => joined.Add(player);
        Invoke(players, "OnClientAuthed", new Connection(41),
            new AuthenticationResponse { success = true, cookie = "shared-app-cookie" });

        Assert.That(joined, Has.Count.EqualTo(1));
        Assert.That(joined[0], Is.Not.EqualTo(retainedPlayer));
        Assert.That(GetCookieToPlayerId(players)["shared-app-cookie"],
            Is.EqualTo(retainedPlayer));
        Assert.That(players.TryGetCookie(retainedPlayer, out var retainedCookie), Is.True);
        Assert.That(retainedCookie, Is.EqualTo("shared-app-cookie"));
        Assert.That(players.TryGetCookie(joined[0], out _), Is.False);
    }

    [Test]
    public void ScopedTransfer_ValidatesRosterAgainstResolvedRetainedPlayer()
    {
        var retained = new PlayerID(12, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 4,
            new[] { retained });

        var validator = typeof(PlayersManager).GetMethod(
            "ValidateExpectedHostMigrationTransferRoster",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(validator, Is.Not.Null);

        var args = new object[] { transition, (PlayerID?)retained, null };
        Assert.That(validator.Invoke(null, args), Is.True,
            "A resolved retained PlayerID inside the roster must validate even with no local login.");

        args = new object[] { transition, (PlayerID?)null, null };
        Assert.That(validator.Invoke(null, args), Is.False,
            "No resolved retained PlayerID must still fail closed.");

        args = new object[] { transition, (PlayerID?)new PlayerID(99, false), null };
        Assert.That(validator.Invoke(null, args), Is.False,
            "A resolved PlayerID outside the roster must fail.");
    }

    [Test]
    public void RetainedClaim_RejectedWhenCookieEvidenceMismatches()
    {
        var (manager, players) = CreateServerPlayersManager();
        var retainedPlayer = new PlayerID(13, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 8,
            new[] { retainedPlayer });
        manager.ConfigureHostMigrationSession(transition);
        GetPlayers(players).Add(retainedPlayer);
        GetCookieToPlayerId(players)["shared-app-cookie"] = retainedPlayer;
        GetPlayerIdToCookie(players)[retainedPlayer] = "shared-app-cookie";
        Invoke(players, "BeginHostMigrationRoster", transition);

        var rebounds = 0;
        players.onPreHostMigrationConnectionRebound += (_, _, _) => rebounds++;
        var joined = new List<PlayerID>();
        players.onPlayerJoined += (player, _, _) => joined.Add(player);

        LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("Rejected a host migration claim"));
        var conn = new Connection(42);
        OfferHostMigrationClaim(players, conn, transition, retainedPlayer);
        Invoke(players, "OnClientAuthed", conn,
            new AuthenticationResponse { success = true, cookie = "attacker-cookie" });

        Assert.That(rebounds, Is.Zero,
            "A claim without the retained player's cookie must not enter the rebound path.");
        Assert.That(joined, Has.Count.EqualTo(1));
        Assert.That(joined[0], Is.Not.EqualTo(retainedPlayer));
        Assert.That(players.pendingHostMigrationPlayers, Is.EqualTo(new[] { retainedPlayer }),
            "The retained identity must stay reserved for its rightful owner.");
        Assert.That(GetCookieToPlayerId(players)["shared-app-cookie"],
            Is.EqualTo(retainedPlayer),
            "The retained player's cookie mapping must survive the failed hijack.");
    }

    [Test]
    public void RetainedClaim_CannotRebindAnotherPendingPlayersCookie()
    {
        var (manager, players) = CreateServerPlayersManager();
        var claimant = new PlayerID(13, false);
        var victim = new PlayerID(14, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 8,
            new[] { claimant, victim });
        manager.ConfigureHostMigrationSession(transition);
        GetPlayers(players).Add(claimant);
        GetPlayers(players).Add(victim);
        GetCookieToPlayerId(players)["peer-cookie"] = victim;
        GetPlayerIdToCookie(players)[victim] = "peer-cookie";
        Invoke(players, "BeginHostMigrationRoster", transition);

        var conn = new Connection(42);
        OfferHostMigrationClaim(players, conn, transition, claimant);
        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex(
                "Closing retained migration player 013 .*exact outbound barrier"));
        Invoke(players, "OnClientAuthed", conn,
            new AuthenticationResponse { success = true, cookie = "peer-cookie" });

        Assert.That(GetCookieToPlayerId(players)["peer-cookie"], Is.EqualTo(victim),
            "A valid claim for one identity must not rebind another pending member's cookie.");
        Assert.That(players.TryGetCookie(victim, out var victimCookie), Is.True);
        Assert.That(victimCookie, Is.EqualTo("peer-cookie"));
    }

    [Test]
    public void RetainedPhysicalReconnect_PreCatchUpFailureStopsBeforeFencedPhaseWithoutJoinReplay()
    {
        var (manager, players) = CreateServerPlayersManager();
        var retainedPlayer = new PlayerID(13, false);
        var transition = new HostMigrationTransitionOptions("room-incarnation", 8,
            new[] { retainedPlayer });
        manager.ConfigureHostMigrationSession(transition);
        GetPlayers(players).Add(retainedPlayer);
        Invoke(players, "BeginHostMigrationRoster", transition);

        var throwingPreCatchUpCount = 0;
        var laterPreCatchUpCount = 0;
        var catchUpCount = 0;
        var publicPreJoinCount = 0;
        var publicJoinCount = 0;
        var publicPostJoinCount = 0;
        players.onPreHostMigrationConnectionRebound += (player, isReconnect, asServer) =>
        {
            Assert.That(player, Is.EqualTo(retainedPlayer));
            Assert.That(isReconnect, Is.True);
            Assert.That(asServer, Is.True);
            throwingPreCatchUpCount++;
            throw new System.InvalidOperationException("catch-up subscriber failed");
        };
        players.onPreHostMigrationConnectionRebound += (player, isReconnect, asServer) =>
        {
            Assert.That(player, Is.EqualTo(retainedPlayer));
            Assert.That(isReconnect, Is.True);
            Assert.That(asServer, Is.True);
            laterPreCatchUpCount++;
        };
        players.onHostMigrationConnectionRebound += (player, isReconnect, asServer) =>
        {
            Assert.That(player, Is.EqualTo(retainedPlayer));
            Assert.That(isReconnect, Is.True);
            Assert.That(asServer, Is.True);
            catchUpCount++;
        };
        players.onPrePlayerJoined += (_, _, _) => publicPreJoinCount++;
        players.onPlayerJoined += (_, _, _) => publicJoinCount++;
        players.onPostPlayerJoined += (_, _, _) => publicPostJoinCount++;

        LogAssert.Expect(LogType.Exception,
            new System.Text.RegularExpressions.Regex("catch-up subscriber failed"));
        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex(
                "Closing retained migration player 013 .*pre-rebound scene manifest callback failed"));
        var reboundConnection = new Connection(42);
        OfferHostMigrationClaim(players, reboundConnection, transition, retainedPlayer);
        Invoke(players, "OnClientAuthed", reboundConnection,
            new AuthenticationResponse { success = true, cookie = "ordinary-app-cookie" });

        Assert.That(throwingPreCatchUpCount, Is.EqualTo(1));
        Assert.That(laterPreCatchUpCount, Is.EqualTo(1),
            "One failing internal subscriber must not suppress later subscribers.");
        Assert.That(catchUpCount, Is.Zero,
            "A failed pre-rebound scene manifest must reject the connection before package/RPC catch-up.");
        Assert.That(publicPreJoinCount, Is.Zero);
        Assert.That(publicJoinCount, Is.Zero,
            "A retained logical member must not replay the public join lifecycle.");
        Assert.That(publicPostJoinCount, Is.Zero);
        Assert.That(players.TryGetConnection(retainedPlayer, out var actualConnection), Is.True);
        Assert.That(actualConnection, Is.EqualTo(reboundConnection));
    }

    [Test]
    public void RpcPromotion_FaultsAllPendingRequestsAndKeepsRequestIdNamespace()
    {
        AssertRpcAuthorityChangeFaultsPendingRequests(promote: true);
    }

    [Test]
    public void RpcTransfer_FaultsAllPendingRequestsAndKeepsRequestIdNamespace()
    {
        AssertRpcAuthorityChangeFaultsPendingRequests(promote: false);
    }

    private (NetworkManager manager, PlayersManager players) CreatePlayersManager()
    {
        var root = new GameObject("Host migration player transfer test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<HostMigrationCoreTestTransport>();
        var manager = root.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        manager.transport = transport;

        var broadcast = new BroadcastModule(manager, false);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, false);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var playerBroadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(playerBroadcaster);
        auth.SetPlayerModule(players);
        return (manager, players);
    }

    private (NetworkManager manager, PlayersManager players) CreateServerPlayersManager()
    {
        var root = new GameObject("Host migration retained reconnect test");
        root.SetActive(false);
        _created.Add(root);
        var transport = root.AddComponent<HostMigrationCoreTestTransport>();
        var manager = root.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        manager.transport = transport;

        var broadcast = new BroadcastModule(manager, true);
        var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
        var auth = new AuthModule(manager, broadcast, cookies);
        var players = new PlayersManager(manager, auth, broadcast);
        var playerBroadcaster = new PlayersBroadcaster(broadcast, players);
        players.SetBroadcaster(playerBroadcaster);
        auth.SetPlayerModule(players);
        typeof(NetworkManager).GetField("_hasGeneratedAlready",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, false);
        NetworkManager.LoadOrGenerateHashes();
        players.Enable(true);
        return (manager, players);
    }

    private void AssertRpcAuthorityChangeFaultsPendingRequests(bool promote)
    {
        var (manager, players) = CreatePlayersManager();
        var module = new RpcRequestResponseModule(manager, players, false);
        var firstTask = module.GetNextId(null, 60f, out var firstRequest);
        var secondTask = module.GetNextId(new PlayerID(21, false), 60f,
            out var secondRequest);
        Assert.That(firstRequest.id, Is.Zero);
        Assert.That(secondRequest.id, Is.EqualTo(1));

        if (promote)
            module.PromoteToServerModule();
        else
            module.TransferToNewServer();

        AssertHostMigrated(firstTask);
        AssertHostMigrated(secondTask);

        var afterTransitionTask = module.GetNextId(null, 60f, out var afterTransition);
        Assert.That(afterTransition.id, Is.EqualTo(2),
            "The request-id namespace must survive an authority change so a stale " +
            "pre-migration response can never complete a new request that reused its id.");
        Assert.That(afterTransitionTask.IsCompleted, Is.False);

        module.TransferToNewServer();
        AssertHostMigrated(afterTransitionTask);
    }

    private static void AssertHostMigrated(System.Threading.Tasks.Task task)
    {
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception, Is.Not.Null);
        Assert.That(task.Exception.InnerException,
            Is.TypeOf<HostMigratedException>());
    }

    private static void InvokeLogin(PlayersManager players,
        HostMigrationTransitionOptions transition, PlayerID player)
    {
        AdvertiseMigrationSession(players, transition);
        Invoke(players, "OnClientLoginResponse", new Connection(1),
            new ServerLoginResponse(player, new NetworkID(0, player)), false);
    }

    private static void AdvertiseMigrationSession(PlayersManager players,
        HostMigrationTransitionOptions transition)
    {
        Invoke(players, "OnHostMigrationSessionAdvertisement", new Connection(1),
            new HostMigrationSessionAdvertisement
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch
            }, false);
    }

    private static void SetExpectedTransition(NetworkManager manager,
        HostMigrationTransitionOptions transition)
    {
        typeof(NetworkManager).GetField("_expectedHostMigrationSession",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(manager, transition);
    }

    private static void SetLocalPlayer(PlayersManager players, PlayerID player)
    {
        typeof(PlayersManager).GetField("<localPlayerId>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(players, (PlayerID?)player);
    }

    private static List<PlayerID> GetPlayers(PlayersManager players)
    {
        return (List<PlayerID>)typeof(PlayersManager).GetField("_players",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players);
    }

    private static Dictionary<string, PlayerID> GetCookieToPlayerId(PlayersManager players)
    {
        return (Dictionary<string, PlayerID>)typeof(PlayersManager).GetField(
                "_cookieToPlayerId", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players);
    }

    private static Dictionary<PlayerID, string> GetPlayerIdToCookie(PlayersManager players)
    {
        return (Dictionary<PlayerID, string>)typeof(PlayersManager).GetField(
                "_playerIdToCookie", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(players);
    }

    private static AuthModule GetAuth(PlayersManager players)
    {
        var field = typeof(PlayersManager).GetField("_authModule",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (AuthModule)field.GetValue(players);
    }

    private static void Invoke(PlayersManager players, string method, params object[] args)
    {
        var target = typeof(PlayersManager).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(target, Is.Not.Null);
        target.Invoke(players, args);
    }

    private static void OfferHostMigrationClaim(
        PlayersManager players, Connection connection,
        HostMigrationTransitionOptions transition, PlayerID player)
    {
        var auth = GetAuth(players);
        var offer = typeof(AuthModule).GetMethod("OnHostMigrationPlayerClaim",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(offer, Is.Not.Null);
        offer.Invoke(auth, new object[]
        {
            connection,
            new HostMigrationPlayerClaim
            {
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                playerId = player
            },
            true
        });
    }
}

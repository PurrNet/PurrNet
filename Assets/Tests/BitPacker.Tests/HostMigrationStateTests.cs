using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class HostMigrationStateTests
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
            _manager.ReleaseClientStateForHostMigration();
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

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            yield return null;
    }

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

    [UnityTest]
    public IEnumerator StopClient_IsReportedAsLocal()
    {
        yield return StartConnectedClient();

        var reports = new List<ClientDisconnectInfo>();
        _manager.onClientDisconnected += reports.Add;
        _manager.StopClient();
        yield return WaitUntil(() => reports.Count > 0, 2f);

        Assert.That(reports.Count, Is.EqualTo(1));
        Assert.That(reports[0].wasLocalRequest, Is.True);
    }

    [UnityTest]
    public IEnumerator LostServer_IsReportedAsRemote()
    {
        yield return StartConnectedClient();

        var reports = new List<ClientDisconnectInfo>();
        _manager.onClientDisconnected += reports.Add;
        _transport.DropFromServer();
        yield return WaitUntil(() => reports.Count > 0, 2f);

        Assert.That(reports.Count, Is.EqualTo(1));
        Assert.That(reports[0].wasLocalRequest, Is.False);
        Assert.That(reports[0].reason, Is.EqualTo(DisconnectReason.Timeout));
    }

    [UnityTest]
    public IEnumerator FailedPromotion_KeepsClientState_WhenAsked_AndTheRetrySucceeds()
    {
        yield return StartConnectedClient();

        _manager.PreserveClientStateForHostMigration();
        _transport.DropFromServer();
        yield return null;
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var before), Is.True);

        _manager.keepClientStateOnFailedHostMigration = true;
        _manager.hostMigrationTimeout = 0.4f;
        _transport.listenFailuresLeft = int.MaxValue;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);
        yield return WaitUntil(() => _manager.serverState == ConnectionState.Disconnected, 3f);
        for (int i = 0; i < 5; i++)
            yield return null;

        Assert.That(_manager.isClientStatePreservedForHostMigration, Is.True, "the failed attempt released the state");
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var kept), Is.True, "the client modules were cleaned up");
        Assert.That(kept, Is.SameAs(before), "the client modules were replaced");
        Assert.That(_manager.TryGetModule<PlayersManager>(true, out _), Is.False, "server modules were left registered");

        _transport.listenFailuresLeft = 0;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);

        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Connected), "the retry did not host");
        Assert.That(_manager.TryGetModule<PlayersManager>(true, out var promoted), Is.True);
        Assert.That(promoted, Is.SameAs(before), "the retry did not promote the kept client modules");
    }

    [UnityTest]
    public IEnumerator ThrowingListen_RollsBackCleanly_AndKeepsClientState()
    {
        yield return StartConnectedClient();
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var before), Is.True);

        _manager.keepClientStateOnFailedHostMigration = true;
        _transport.listenThrows = true;
        LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("listen failed"));
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer &&
                                     _manager.serverState == ConnectionState.Disconnected, 3f);
        for (int i = 0; i < 5; i++)
            yield return null;

        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Disconnected), "the server did not finish stopping");
        Assert.That(_manager.isClientStatePreservedForHostMigration, Is.True);
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var kept), Is.True);
        Assert.That(kept, Is.SameAs(before));

        _transport.listenThrows = false;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);
        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Connected), "the retry did not host");
    }

    [UnityTest]
    public IEnumerator FailedTransfer_KeepsClientState_WhenAsked()
    {
        yield return StartConnectedClient();
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var before), Is.True);

        _manager.keepClientStateOnFailedHostMigration = true;
        _manager.hostMigrationTimeout = 0.4f;
        _transport.clientConnects = false;
        _manager.TransferToNewServer();
        yield return WaitUntil(() => !_manager.isTranferingToNewServer, 3f);
        for (int i = 0; i < 5; i++)
            yield return null;

        Assert.That(_manager.isClientStatePreservedForHostMigration, Is.True, "the failed attempt released the state");
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out var kept), Is.True, "the client modules were cleaned up");
        Assert.That(kept, Is.SameAs(before));

        _manager.ReleaseClientStateForHostMigration();
        yield return WaitUntil(() => !_manager.TryGetModule<PlayersManager>(false, out _), 3f);
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out _), Is.False, "releasing did not clean up");
    }

    [UnityTest]
    public IEnumerator FailedPromotion_WithoutTheOption_StillCleansUp()
    {
        yield return StartConnectedClient();

        _manager.hostMigrationTimeout = 0.4f;
        _transport.listenFailuresLeft = int.MaxValue;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);
        yield return WaitUntil(() => !_manager.TryGetModule<PlayersManager>(false, out _), 3f);

        Assert.That(_manager.isClientStatePreservedForHostMigration, Is.False);
        Assert.That(_manager.TryGetModule<PlayersManager>(false, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator PromotionWithNobodyElse_SettlesTheRosterAtOnce()
    {
        yield return StartConnectedClient();

        int settled = 0;
        _manager.onHostMigrationRosterSettled += () => settled++;
        _manager.PromoteToServer();
        yield return WaitUntil(() => !_manager.isPromotingToServer, 3f);

        Assert.That(_manager.serverState, Is.EqualTo(ConnectionState.Connected));
        Assert.That(settled, Is.EqualTo(1));
        Assert.That(_manager.isHostMigrationRosterPending, Is.False);
        Assert.That(_manager.ConfirmHostMigrationPlayerDeparture(new PlayerID(42, false)), Is.False);
    }
}

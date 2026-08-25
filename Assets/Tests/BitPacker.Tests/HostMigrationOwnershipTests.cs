using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Pooling;
using UnityEngine;

public sealed class HostMigrationOwnershipTests
{
    [Test]
    public void FreshRetainedSceneRebound_CreatesOwnershipStateBeforeSnapshot()
    {
        var managerObject = new GameObject(nameof(FreshRetainedSceneRebound_CreatesOwnershipStateBeforeSnapshot));
        managerObject.SetActive(false);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            var transition = new HostMigrationTransitionOptions("room-incarnation", 5);
            typeof(NetworkManager).GetField("_expectedHostMigrationSession",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(manager, transition);

            var hierarchy = new HierarchyFactory(null, null, null, null);
            var module = new GlobalOwnershipModule(manager, hierarchy, null, null, null);
            module.TransferToNewServer();
            var scene = new SceneID(9);

            typeof(GlobalOwnershipModule).GetMethod("OnSceneRebound",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(module, new object[] { scene, false });

            var sceneOwnerships = typeof(GlobalOwnershipModule)
                .GetField("_sceneOwnerships", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(module) as IDictionary;
            Assert.That(sceneOwnerships, Is.Not.Null);
            Assert.That(sceneOwnerships.Contains(scene), Is.True,
                "The reliable-ordered ownership snapshot needs a destination before rebound ack.");
            Assert.That(module.isTransferReconciliationComplete, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void TransferToNewServer_ClearsAuthorityEraOwnershipQueues()
    {
        var managerObject = new GameObject(nameof(TransferToNewServer_ClearsAuthorityEraOwnershipQueues));
        managerObject.SetActive(false);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            var hierarchy = new HierarchyFactory(null, null, null, null);
            var module = new GlobalOwnershipModule(manager, hierarchy, null, null, null);
            var moduleType = typeof(GlobalOwnershipModule);

            var asyncPending = (IList)moduleType
                .GetField("_pendingOwnership", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(module);
            Assert.That(asyncPending, Is.Not.Null);

            var handleBatch = moduleType.GetMethod("HandleOwnershipBatch",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handleBatch, Is.Not.Null);
            handleBatch.Invoke(module, new object[]
            {
                new SceneID(3),
                new OwnershipInfo
                {
                    identity = new NetworkID(4, new PlayerID(2, false)),
                    player = new PlayerID(7, false)
                },
                true
            });
            Assert.That(asyncPending, Has.Count.EqualTo(1));

            var outboundPending = (IDictionary)moduleType
                .GetField("_pendingOwnershipChanges", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(module);
            Assert.That(outboundPending, Is.Not.Null);

            var exactPlayers = (HashSet<PlayerID>)moduleType
                .GetField("_exactOwnershipReboundPlayers", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(module);
            Assert.That(exactPlayers, Is.Not.Null);
            exactPlayers.Add(new PlayerID(2, false));

            module.TransferToNewServer();

            Assert.That(asyncPending, Is.Empty,
                "An unresolved additive packet from the old authority must never apply after transfer.");
            Assert.That(outboundPending, Is.Empty);
            Assert.That(exactPlayers, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void RemoveOwnershipById_ClearsAuthoritativeAndReverseIndexes()
    {
        var ownership = new SceneOwnership(false);
        var id = new NetworkID(11, new PlayerID(2, false));
        var owner = new PlayerID(7, false);
        var type = typeof(SceneOwnership);

        var owners = (Dictionary<NetworkID, PlayerID>)type
            .GetField("_owners", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(ownership);
        var playerOwnedIds = (Dictionary<PlayerID, HashSet<NetworkID>>)type
            .GetField("_playerOwnedIds", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(ownership);
        Assert.That(owners, Is.Not.Null);
        Assert.That(playerOwnedIds, Is.Not.Null);

        owners[id] = owner;
        playerOwnedIds[owner] = new HashSet<NetworkID> { id };

        Assert.That(ownership.RemoveOwnership(id), Is.True);
        Assert.That(ownership.GetState(), Is.Empty);
        Assert.That(ownership.TryGetOwnedObjects(owner), Is.Empty);
        Assert.That(ownership.RemoveOwnership(id), Is.False,
            "Repeating an authoritative empty snapshot must be idempotent.");
    }

    [Test]
    public void EmptyExactSnapshot_CompletesSceneOwnershipBarrier()
    {
        var managerObject = new GameObject(nameof(EmptyExactSnapshot_CompletesSceneOwnershipBarrier));
        managerObject.SetActive(false);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            var transition = new HostMigrationTransitionOptions("room-incarnation", 6);
            typeof(NetworkManager).GetField("_expectedHostMigrationSession",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(manager, transition);

            var hierarchy = new HierarchyFactory(null, null, null, null);
            var module = new GlobalOwnershipModule(manager, hierarchy, null, null, null);
            module.TransferToNewServer();
            manager.ReceiveHostMigrationSession(transition);

            var scene = new SceneID(3);
            typeof(GlobalOwnershipModule).GetMethod("OnSceneLoaded",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(module, new object[] { scene, false });

            Assert.That(module.isTransferReconciliationComplete, Is.False,
                "A retained/loaded scene must wait for ownership even when its exact state is empty.");

            var state = DisposableList<OwnershipInfo>.Create(0);
            var packet = new OwnershipSnapshot
            {
                scene = scene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                state = state
            };
            typeof(GlobalOwnershipModule).GetMethod("OnOwnershipSnapshot",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(module, new object[] { PlayerID.Server, packet, false });

            Assert.That(module.isTransferReconciliationComplete, Is.True);
            Assert.That(module.TryGetTransferReconciliationFailure(transition, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }
}

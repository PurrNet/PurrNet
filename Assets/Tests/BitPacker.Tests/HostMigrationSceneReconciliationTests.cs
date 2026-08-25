using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Pooling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class HostMigrationSceneReconciliationTests
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void ManualDespawn_RemovesClientRoleManualIdentityAndIsIdempotent()
    {
        var managerObject = new GameObject("client manual identity manager");
        var identityObject = new GameObject("client manual identity");
        managerObject.SetActive(false);
        var scene = new SceneID(40);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var identity = identityObject.AddComponent<NetworkIdentity>();
            var hierarchy = CreateBareHierarchy(manager, scene, false);

            var removedCount = 0;
            hierarchy.onIdentityRemoved += _ => removedCount++;
            var networkId = new NetworkID(401);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            Assert.That(identity.IsSpawned(false), Is.True);
            Assert.That(hierarchy.TryGetIdentity(networkId, out var registered), Is.True);
            Assert.That(registered, Is.SameAs(identity));

            hierarchy.ManualDespawn(identity);
            hierarchy.ManualDespawn(identity);

            Assert.That(identity.IsSpawned(false), Is.False);
            Assert.That(hierarchy.TryGetIdentity(networkId, out _), Is.False);
            Assert.That(removedCount, Is.EqualTo(1));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExactSnapshotPlan_ReproofAcceptsManualDespawnAndReaddWhenFinalGraphMatches()
    {
        var managerObject = new GameObject("exact snapshot registry revision manager");
        var identityObject = new GameObject("exact snapshot recycled manual identity");
        managerObject.SetActive(false);
        var scene = new SceneID(41);
        HierarchyV2 hierarchy = null;
        HierarchyV2.ExactSceneSnapshotPlan plan = null;
        NetworkRules rules = null;
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            rules = ScriptableObject.CreateInstance<NetworkRules>();
            manager.SetNetworkRules(rules);
            hierarchy = new HierarchyV2(
                manager, scene, managerObject.scene, null, null, true);

            var identity = identityObject.AddComponent<NetworkIdentity>();
            identity.PreparePrefabInfo(0, 0, false, false);
            var networkId = new NetworkID(411);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            var transition = new HostMigrationTransitionOptions(
                "exact-snapshot-registry-revision", 2);
            if (!hierarchy.TryPrepareExactSceneSnapshot(
                    new PlayerID(41, false), transition, null, null,
                    out plan, out var failure))
            {
                Assert.Fail($"Could not prepare the exact snapshot plan: {failure ?? "<null>"}");
            }

            hierarchy.ManualDespawn(identity);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            Assert.That(hierarchy.TryValidateExactSceneSnapshotPlan(plan, out failure),
                Is.True,
                "Best-effort reconciliation should accept the final graph as reality when " +
                "identity, ID, parents, roots, and topology still match.");
            Assert.That(failure, Is.Null);
        }
        finally
        {
            plan?.Dispose();
            NetworkPoolManager.RemovePool(scene);
            if (rules)
                UnityEngine.Object.DestroyImmediate(rules);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void LoadedSceneCompatibility_RequiresImmutableLocalPhysicsOnly()
    {
        var retained = new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.Physics3D,
            isPublic = false
        };
        var authoritative = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.Physics3D,
            isPublic = true
        };

        Assert.That(ScenesModule.AreLoadedSceneSettingsCompatible(retained, authoritative), Is.True,
            "Historical load mode and visibility can be reconciled after load.");

        authoritative.physicsMode = LocalPhysicsMode.None;
        Assert.That(ScenesModule.AreLoadedSceneSettingsCompatible(retained, authoritative), Is.False,
            "A loaded scene cannot change its local physics scene in place.");
    }

    [Test]
    public void ExactMissingSingleBase_IsStagedAdditiveWithoutChangingAuthorityDescriptor()
    {
        var build = BuildSceneAction(17, 42, LoadSceneMode.Single);
        build.loadSceneAction.parameters.physicsMode = LocalPhysicsMode.Physics3D;
        build.loadSceneAction.parameters.isPublic = true;
        var stagedBuild = ScenesModule.NormalizeExactMissingLoadForStaging(build);

        Assert.That(build.loadSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(stagedBuild.loadSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(stagedBuild.loadSceneAction.parameters.physicsMode,
            Is.EqualTo(LocalPhysicsMode.Physics3D));
        Assert.That(stagedBuild.loadSceneAction.parameters.isPublic, Is.True);

        var addressable = new SceneAction
        {
            type = SceneActionType.LoadAddressable,
            loadAddressableSceneAction = new LoadAddressableSceneAction
            {
                guid = "best-effort-base",
                sceneID = new SceneID(43),
                parameters = new PurrSceneSettings
                {
                    mode = LoadSceneMode.Single,
                    physicsMode = LocalPhysicsMode.Physics2D,
                    isPublic = false
                }
            }
        };
        var stagedAddressable = ScenesModule.NormalizeExactMissingLoadForStaging(addressable);

        Assert.That(addressable.loadAddressableSceneAction.parameters.mode,
            Is.EqualTo(LoadSceneMode.Single));
        Assert.That(stagedAddressable.loadAddressableSceneAction.parameters.mode,
            Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(stagedAddressable.loadAddressableSceneAction.parameters.physicsMode,
            Is.EqualTo(LocalPhysicsMode.Physics2D));
    }

    [Test]
    public void ExactLoadedTargetAmbiguity_UsesStableAuthoritativeSceneIdBinding()
    {
        Assert.That(ScenesModule.IsLoadedTargetSelectionAmbiguous(2, false), Is.True,
            "Multiple indistinguishable loaded instances still fail closed.");
        Assert.That(ScenesModule.IsLoadedTargetSelectionAmbiguous(2, true), Is.False,
            "A stable authoritative SceneID binding deterministically selects one instance.");
        Assert.That(ScenesModule.IsLoadedTargetSelectionAmbiguous(1, false), Is.False);
    }

    [Test]
    public void ExactOutboundSceneSet_FailsWhenAuthoritativeMembershipDrifts()
    {
        var module = new ScenePlayersModule(null, null, null);
        var player = new PlayerID(77, false);
        var first = new SceneID(801);
        var second = new SceneID(802);
        var unexpected = new SceneID(803);
        var memberships = typeof(ScenePlayersModule).GetField("_scenePlayers", InstanceFields)
            ?.GetValue(module) as IDictionary<SceneID, List<PlayerID>>;
        Assert.That(memberships, Is.Not.Null);
        memberships.Add(first, new List<PlayerID> { player });
        memberships.Add(second, new List<PlayerID> { player });
        memberships.Add(unexpected, new List<PlayerID>());

        Assert.That(module.TryValidateExactPlayerSceneSet(
            player, new[] { first, second }, out var failure), Is.True, failure);

        memberships[unexpected].Add(player);
        Assert.That(module.TryValidateExactPlayerSceneSet(
            player, new[] { first, second }, out failure), Is.False);
        Assert.That(failure, Does.Contain("unexpected"));

        memberships[unexpected].Clear();
        memberships[second].Clear();
        Assert.That(module.TryValidateExactPlayerSceneSet(
            player, new[] { first, second }, out failure), Is.False);
        Assert.That(failure, Does.Contain("changed"));
    }

    [Test]
    public void ExactSnapshotAbort_RestoresObserverLifecycleAndRetryTopology()
    {
        NetworkManager.CallAllRegisters();
        var managerObject = new GameObject("exact snapshot retry manager");
        var identityObject = new GameObject("exact snapshot retry identity");
        managerObject.SetActive(false);
        var scene = new SceneID(804);
        HierarchyV2 hierarchy = null;
        NetworkRules rules = null;
        try
        {
            var transport = managerObject.AddComponent<HostMigrationCoreTestTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            manager.transport = transport;
            rules = ScriptableObject.CreateInstance<NetworkRules>();
            manager.SetNetworkRules(rules);

            var broadcast = new BroadcastModule(manager, true);
            var cookies = new CookiesModule(CookieScope.LiveWithConnection, true);
            var auth = new AuthModule(manager, broadcast, cookies);
            var players = new PlayersManager(manager, auth, broadcast);
            auth.SetPlayerModule(players);
            var playerBroadcaster = new PlayersBroadcaster(broadcast, players);
            players.SetBroadcaster(playerBroadcaster);

            var scenePlayers = new ScenePlayersModule(manager, null, players);
            var player = new PlayerID(81, false);
            var sceneMembership = GetScenePlayersField<Dictionary<SceneID, List<PlayerID>>>(
                scenePlayers, "_scenePlayers");
            var loadedMembership = GetScenePlayersField<Dictionary<SceneID, List<PlayerID>>>(
                scenePlayers, "_sceneLoadedPlayers");
            sceneMembership.Add(scene, new List<PlayerID> { player });
            loadedMembership.Add(scene, new List<PlayerID> { player });

            hierarchy = new HierarchyV2(
                manager, scene, managerObject.scene, scenePlayers, players, true);
            hierarchy.Enable();

            var identity = identityObject.AddComponent<NetworkIdentity>();
            identity.PreparePrefabInfo(15, 0, false, false);
            PrepareServerIdentity(identity, manager, hierarchy, scene, new NetworkID(805));
            RegisterBareServerIdentity(hierarchy, identity);

            var observerAdded = 0;
            var observerRemoved = 0;
            hierarchy.onObserverAdded += (_, observed) =>
            {
                if (observed == identity)
                    observerAdded++;
            };
            hierarchy.onObserverRemoved += (_, observed) =>
            {
                if (observed == identity)
                    observerRemoved++;
            };

            var transition = new HostMigrationTransitionOptions("exact-snapshot-retry", 9);
            Assert.That(hierarchy.TryPrepareExactSceneSnapshot(
                    player, transition, null, null, out var firstPlan, out var failure),
                Is.True, failure);
            Assert.That(firstPlan.preamble.spawns.Count, Is.EqualTo(1));
            Assert.That(firstPlan.ownsBatch, Is.True);
            Assert.That(firstPlan.batch.spawnPackets.Count, Is.EqualTo(1));
            Assert.That(identity.IsObserver(player), Is.True);
            Assert.That(observerAdded, Is.EqualTo(1));

            firstPlan.Dispose();

            Assert.That(identity.IsObserverOrPending(player), Is.False,
                "Aborting before the collection commit must restore pre-stage membership.");
            Assert.That(observerRemoved, Is.EqualTo(1),
                "The eager pre-observer lifecycle must receive its compensating removal.");
            Assert.That(GetHierarchyCollectionCount(hierarchy, "_triggerLateObserverAdded"), Is.Zero,
                "An aborted exact lifecycle cannot escape through the next-frame late queue.");

            SetHierarchyField(hierarchy, "_asyncVisibilityDepth", 1);
            Assert.That(hierarchy.TryPrepareExactSceneSnapshot(
                    player, transition, null, null, out var asyncPlan, out failure),
                Is.True, failure);
            Assert.That(asyncPlan.batch.spawnPackets.Count, Is.EqualTo(1));
            Assert.That(identity.IsObserver(player), Is.False);
            Assert.That(identity.IsObserverOrPending(player), Is.True);
            Assert.That(GetHierarchyCollectionCount(hierarchy, "_pendingAsyncObservers"),
                Is.EqualTo(1));
            asyncPlan.Dispose();

            Assert.That(identity.IsObserverOrPending(player), Is.False);
            Assert.That(GetHierarchyCollectionCount(hierarchy, "_pendingAsyncObservers"), Is.Zero,
                "An aborted async exact spawn cannot suppress its retry.");
            Assert.That(observerAdded, Is.EqualTo(1),
                "Async observer lifecycles remain deferred until receiver readiness.");

            Assert.That(hierarchy.TryPrepareExactSceneSnapshot(
                    player, transition, null, null, out var asyncRetryPlan, out failure),
                Is.True, failure);
            Assert.That(asyncRetryPlan.batch.spawnPackets.Count, Is.EqualTo(1),
                "A same-session async retry must rebuild the same retained root topology.");
            asyncRetryPlan.Dispose();

            SetHierarchyField(hierarchy, "_asyncVisibilityDepth", 0);
            Assert.That(hierarchy.TryPrepareExactSceneSnapshot(
                    player, transition, null, null, out var committedRetryPlan, out failure),
                Is.True, failure);
            Assert.That(committedRetryPlan.batch.spawnPackets.Count, Is.EqualTo(1));
            committedRetryPlan.AcceptStaging();
            committedRetryPlan.Dispose();

            Assert.That(observerAdded, Is.EqualTo(2));
            Assert.That(identity.IsObserver(player), Is.True,
                "Disposal after the collection-wide accept must preserve committed visibility.");
            Assert.That(GetHierarchyCollectionCount(hierarchy, "_triggerLateObserverAdded"),
                Is.EqualTo(1));
        }
        finally
        {
            hierarchy?.Disable();
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(rules);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

#if UNITY_PHYSICS_3D
    [UnityTest]
    public IEnumerator ExactSceneCompatibility_VerifiesPhysicalLocalPhysicsWorld()
    {
        var scene = SceneManager.CreateScene(
            $"HostMigrationPhysics-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));

        var probed = ScenesModule.TryGetPhysicalLocalPhysicsMode(
            scene, out var physicalMode, out var probeFailure);
        var compatible = ScenesModule.ArePhysicalSceneSettingsCompatible(
            scene,
            new PurrSceneSettings { physicsMode = LocalPhysicsMode.None },
            out var compatibilityFailure);

        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;

        Assert.That(probed, Is.True, probeFailure);
        Assert.That(physicalMode, Is.EqualTo(LocalPhysicsMode.Physics3D));
        Assert.That(compatible, Is.False);
        Assert.That(compatibilityFailure, Does.Contain("does not match"));
    }

    [UnityTest]
    public IEnumerator ExactSceneCompatibility_RepairsStaleRegistryPhysicsMetadataFromPhysicalTruth()
    {
        var scene = SceneManager.CreateScene(
            $"HostMigrationPhysicsMetadata-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var retained = new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.None,
            isPublic = false
        };
        var authoritative = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.Physics3D,
            isPublic = true
        };

        var compatible = ScenesModule.TryReconcileLoadedSceneSettings(
            scene, retained, authoritative, out var reconciled,
            out var repairedMetadata, out var usedLocalPhysicsFallback, out var failure);

        var divergentAuthority = authoritative;
        divergentAuthority.physicsMode = LocalPhysicsMode.None;
        var fallbackCompatible = ScenesModule.TryReconcileLoadedSceneSettings(
            scene, authoritative, divergentAuthority, out var fallbackSettings,
            out var fallbackRepairedMetadata, out var usedFallback, out var fallbackFailure);

        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;

        Assert.That(compatible, Is.True, failure);
        Assert.That(repairedMetadata, Is.True);
        Assert.That(usedLocalPhysicsFallback, Is.False);
        Assert.That(reconciled.mode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(reconciled.physicsMode, Is.EqualTo(LocalPhysicsMode.Physics3D));
        Assert.That(reconciled.isPublic, Is.True);
        Assert.That(fallbackCompatible, Is.True,
            "A stable loaded scene should survive an immutable authority mismatch in best-effort " +
            $"mode: {fallbackFailure}");
        Assert.That(fallbackRepairedMetadata, Is.False);
        Assert.That(usedFallback, Is.True);
        Assert.That(fallbackSettings.physicsMode, Is.EqualTo(LocalPhysicsMode.Physics3D),
            "The local descriptor must continue to report the physical Unity world it actually uses.");
    }
#endif

    [Test]
    public void ExactLoadedTargetReplacementGuard_RejectsUnreconciledMismatch()
    {
        var retained = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.Physics3D
        };
        var authoritative = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None
        };
        var immutableSettingsMatch =
            ScenesModule.AreLoadedSceneSettingsCompatible(retained, authoritative);

        Assert.That(ScenesModule.ShouldRejectLoadedTargetReplacement(
            true,
            true,
            true,
            immutableSettingsMatch,
            true), Is.True,
            "Callers must probe and reconcile physical settings before treating a loaded target as reusable.");
        Assert.That(ScenesModule.ShouldRejectLoadedTargetReplacement(
            true,
            true,
            true,
            true,
            false), Is.True,
            "Ambiguous or changing loaded topology must fail without unloading either candidate.");
        Assert.That(ScenesModule.ShouldRejectLoadedTargetReplacement(
            true,
            false,
            false,
            false,
            false), Is.False,
            "A truly missing target remains eligible to load.");
        Assert.That(ScenesModule.ShouldRejectLoadedTargetReplacement(
            false,
            true,
            true,
            false,
            true), Is.False,
            "Legacy non-exact reconciliation keeps its replacement behavior.");
    }

    [Test]
    public void PendingSceneCompatibility_RequiresLoadModeAndLocalPhysics()
    {
        var pending = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.Physics2D,
            isPublic = false
        };
        var authoritative = pending;
        authoritative.isPublic = true;

        Assert.That(ScenesModule.ArePendingSceneSettingsCompatible(pending, authoritative), Is.True);

        authoritative.mode = LoadSceneMode.Single;
        Assert.That(ScenesModule.ArePendingSceneSettingsCompatible(pending, authoritative), Is.False);

        authoritative = pending;
        authoritative.physicsMode = LocalPhysicsMode.None;
        Assert.That(ScenesModule.ArePendingSceneSettingsCompatible(pending, authoritative), Is.False);
    }

    [Test]
    public void RetainedSceneRebound_DoesNotReplayOrdinarySceneLoadLifecycle()
    {
        var scenes = new ScenesModule(null, null);
        var expectedScene = new SceneID(42);
        var ordinaryLoadCallbacks = 0;
        var internalReboundCallbacks = 0;
        var publicReboundCallbacks = 0;

        scenes.onPreSceneLoaded += (_, _) => ordinaryLoadCallbacks++;
        scenes.onSceneLoaded += (_, _) => ordinaryLoadCallbacks++;
        scenes.onPostSceneLoaded += (_, _) => ordinaryLoadCallbacks++;
        scenes.onRetainedSceneRebound += (scene, asServer) =>
        {
            Assert.That(scene, Is.EqualTo(expectedScene));
            Assert.That(asServer, Is.False);
            internalReboundCallbacks++;
        };
        scenes.onSceneRebound += (scene, asServer) =>
        {
            Assert.That(scene, Is.EqualTo(expectedScene));
            Assert.That(asServer, Is.False);
            publicReboundCallbacks++;
        };

        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.True);
        scenes.PlayRetainedSceneReboundForScene(expectedScene);

        Assert.That(ordinaryLoadCallbacks, Is.Zero,
            "Retaining a compatible Unity scene must not pretend that it loaded again.");
        Assert.That(internalReboundCallbacks, Is.EqualTo(1),
            "Bootstrap mirroring and FirstSceneActionsBatch describe one rebound boundary.");
        Assert.That(publicReboundCallbacks, Is.EqualTo(1));
    }

    [Test]
    public void Promotion_AdvancesSceneIdCounterPastRetainedServerAssignedIds()
    {
        var scenes = new ScenesModule(null, null);
        var registry = GetSceneField<System.Collections.Generic.Dictionary<SceneID, SceneState>>(
            scenes, "_scenes");
        registry.Add(new SceneID(1), default);
        registry.Add(new SceneID(7), default);

        var advance = typeof(ScenesModule).GetMethod(
            "AdvanceNextSceneIdPastRetainedScenes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(advance, Is.Not.Null);

        advance.Invoke(scenes, null);
        Assert.That(GetSceneField<ushort>(scenes, "_nextSceneID"), Is.EqualTo((ushort)8));

        advance.Invoke(scenes, null);
        Assert.That(GetSceneField<ushort>(scenes, "_nextSceneID"), Is.EqualTo((ushort)8),
            "The counter only moves forward past retained ids.");
    }

    [Test]
    public void RetainedScenePreReboundFailure_DoesNotAcknowledgeOrMarkSceneComplete()
    {
        var scenes = new ScenesModule(null, null);
        var expectedScene = new SceneID(43);
        var failingPreCallbacks = 0;
        var laterPreCallbacks = 0;
        var retainedReboundCallbacks = 0;
        var publicReboundCallbacks = 0;

        OnSceneActionEvent failingPreCallback = (_, _) =>
        {
            failingPreCallbacks++;
            throw new InvalidOperationException("expected retained scene pre-rebound failure");
        };
        scenes.onPreRetainedSceneRebound += failingPreCallback;
        scenes.onPreRetainedSceneRebound += (_, _) => laterPreCallbacks++;
        scenes.onRetainedSceneRebound += (_, _) => retainedReboundCallbacks++;
        scenes.onSceneRebound += (_, _) => publicReboundCallbacks++;

        LogAssert.Expect(LogType.Exception,
            new Regex("expected retained scene pre-rebound failure"));
        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.False);

        Assert.That(failingPreCallbacks, Is.EqualTo(1));
        Assert.That(laterPreCallbacks, Is.Zero,
            "Core processing must stop at the first failed pre-rebound participant.");
        Assert.That(retainedReboundCallbacks, Is.Zero,
            "ScenePlayersModule acknowledges the scene from this phase, so it must stay hidden on failure.");
        Assert.That(publicReboundCallbacks, Is.Zero);

        scenes.onPreRetainedSceneRebound -= failingPreCallback;
        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.True);
        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.True);

        Assert.That(laterPreCallbacks, Is.EqualTo(1),
            "A failed attempt must not mark the rebound as successfully completed.");
        Assert.That(retainedReboundCallbacks, Is.EqualTo(1));
        Assert.That(publicReboundCallbacks, Is.EqualTo(1));
    }

    [Test]
    public void RetainedSceneRebound_FailedCommitDoesNotReplaySuccessfulPreparation()
    {
        var scenes = new ScenesModule(null, null);
        var expectedScene = new SceneID(44);
        var preReboundCallbacks = 0;
        var reboundCallbacks = 0;

        scenes.onPreRetainedSceneRebound += (_, _) => preReboundCallbacks++;
        OnSceneActionEvent failingRebound = (_, _) =>
        {
            reboundCallbacks++;
            throw new InvalidOperationException("expected retained scene commit failure");
        };
        scenes.onRetainedSceneRebound += failingRebound;

        LogAssert.Expect(LogType.Exception,
            new Regex("expected retained scene commit failure"));
        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.False);

        scenes.onRetainedSceneRebound -= failingRebound;
        scenes.onRetainedSceneRebound += (_, _) => reboundCallbacks++;
        Assert.That(scenes.PlayRetainedSceneReboundForScene(expectedScene), Is.True);

        Assert.That(preReboundCallbacks, Is.EqualTo(1),
            "A successful prepare remains valid when only the later commit phase fails.");
        Assert.That(reboundCallbacks, Is.EqualTo(2));
    }

    [Test]
    public void RetainedScenePreparation_RegistrationRollbackAllowsFreshPreparation()
    {
        var scenes = new ScenesModule(null, null);
        var sceneId = new SceneID(45);
        var unityScene = SceneManager.GetActiveScene();
        var settings = new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        };
        var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
        var prepare = typeof(ScenesModule).GetMethod(
            "PrepareRetainedSceneReboundForScene", InstanceFields);
        Assert.That(addScene, Is.Not.Null);
        Assert.That(prepare, Is.Not.Null);

        var preReboundCallbacks = 0;
        scenes.onPreRetainedSceneRebound += (_, _) => preReboundCallbacks++;
        addScene.Invoke(scenes, new object[] { unityScene, settings, sceneId });
        Assert.That((bool)prepare.Invoke(scenes, new object[] { sceneId }), Is.True);
        Assert.That(preReboundCallbacks, Is.EqualTo(1));

        Assert.That(scenes.DetachRetainedPhysicalSceneRegistration(unityScene), Is.True);
        addScene.Invoke(scenes, new object[] { unityScene, settings, sceneId });
        Assert.That((bool)prepare.Invoke(scenes, new object[] { sceneId }), Is.True);
        Assert.That(preReboundCallbacks, Is.EqualTo(2),
            "Rolling back a scene registration must also roll back its preparation marker.");
    }

    [Test]
    public void ExactPromotedListenSceneSetupDecision_IsNarrowlyScoped()
    {
        var exact = new HostMigrationTransitionOptions("room-incarnation", 4);

        Assert.That(ScenesModule.ShouldUseExactPromotedListenSetup(
            false, true, true, exact), Is.True);
        Assert.That(ScenesModule.ShouldUseExactPromotedListenSetup(
            true, true, true, exact), Is.False,
            "The promoted server graph keeps its in-place promotion path.");
        Assert.That(ScenesModule.ShouldUseExactPromotedListenSetup(
            false, false, true, exact), Is.False,
            "An ordinary listen client keeps bootstrap/load lifecycle behavior.");
        Assert.That(ScenesModule.ShouldUseExactPromotedListenSetup(
            false, true, true, default), Is.False,
            "Legacy unscoped promotion must not opt into exact reconciliation.");
    }

    [Test]
    public void RetainedPlayerSceneRebound_UsesDedicatedCoreAndPublicLifecycle()
    {
        var scenePlayers = new ScenePlayersModule(null, null, null);
        var player = new PlayerID(7, false);
        var scene = new SceneID(12);
        var ordinaryCallbacks = 0;
        var phases = new List<string>();

        scenePlayers.onPrePlayerLoadedScene += (_, _, _) => ordinaryCallbacks++;
        scenePlayers.onPlayerLoadedScene += (_, _, _) => ordinaryCallbacks++;
        scenePlayers.onPostPlayerLoadedScene += (_, _, _) => ordinaryCallbacks++;
        scenePlayers.onPrePlayerSceneReboundInternal += (_, _, _) => phases.Add("pre-core");
        scenePlayers.onPlayerSceneReboundInternal += (_, _, _) => phases.Add("core");
        scenePlayers.onPlayerReboundScene += (_, _, _) => phases.Add("public");

        scenePlayers.TriggerPlayerSceneRebound(player, scene, true);

        Assert.That(ordinaryCallbacks, Is.Zero);
        Assert.That(phases, Is.EqualTo(new[] { "pre-core", "core", "public" }));
    }

    [Test]
    public void RetainedSceneCompletion_HasDistinctWireMessageFromOrdinaryLoad()
    {
        var ordinaryFields = typeof(ClientFinishedLoadingScene).GetFields();
        Assert.That(ordinaryFields, Has.Length.EqualTo(1));
        Assert.That(ordinaryFields[0].Name, Is.EqualTo(nameof(ClientFinishedLoadingScene.scene)));

        var transition = new HostMigrationTransitionOptions("room-incarnation", 8);
        var rebound = new ClientFinishedRebindingScene
        {
            scene = new SceneID(3),
            hostMigrationSessionId = transition.sessionId,
            hostMigrationEpoch = transition.epoch
        };

        Assert.That(rebound.hostMigrationTransition, Is.EqualTo(transition));
    }

    [Test]
    public void PromotionBaseSelection_AcceptsUniqueAddressableSingle_AndOrdersItFirst()
    {
        var candidates = new List<PromotionSceneCandidate>
        {
            new PromotionSceneCandidate(new SceneID(1), LoadSceneMode.Additive,
                LocalPhysicsMode.None, true),
            new PromotionSceneCandidate(new SceneID(2), LoadSceneMode.Single,
                LocalPhysicsMode.None, false, true),
            new PromotionSceneCandidate(new SceneID(3), LoadSceneMode.Additive,
                LocalPhysicsMode.Physics3D, false)
        };

        Assert.That(ScenesModule.TrySelectPromotionBaseScene(
            candidates, out var selected, out var failure), Is.True, failure);
        Assert.That(selected, Is.EqualTo(new SceneID(2)));

        var ordered = ScenesModule.OrderPromotionSceneCandidates(candidates, selected);
        Assert.That(ordered, Has.Count.EqualTo(3));
        Assert.That(ordered[0].id, Is.EqualTo(selected));
        Assert.That(ordered[0].isAddressable, Is.True);
        Assert.That(ordered[1].id, Is.EqualTo(new SceneID(1)));
        Assert.That(ordered[2].id, Is.EqualTo(new SceneID(3)));
    }

    [Test]
    public void PromotionBaseSelection_UsesOriginalBuildFallback_AndRejectsAmbiguity()
    {
        var fallbackCandidates = new List<PromotionSceneCandidate>
        {
            new PromotionSceneCandidate(new SceneID(4), LoadSceneMode.Additive,
                LocalPhysicsMode.None, false, true),
            new PromotionSceneCandidate(new SceneID(5), LoadSceneMode.Additive,
                LocalPhysicsMode.None, true)
        };

        Assert.That(ScenesModule.TrySelectPromotionBaseScene(
            fallbackCandidates, out var fallback, out var fallbackFailure),
            Is.True, fallbackFailure);
        Assert.That(fallback, Is.EqualTo(new SceneID(5)));

        var ambiguousCandidates = new List<PromotionSceneCandidate>
        {
            new PromotionSceneCandidate(new SceneID(6), LoadSceneMode.Single,
                LocalPhysicsMode.None, false),
            new PromotionSceneCandidate(new SceneID(7), LoadSceneMode.Single,
                LocalPhysicsMode.None, false, true)
        };

        Assert.That(ScenesModule.TrySelectPromotionBaseScene(
            ambiguousCandidates, out _, out var ambiguity), Is.False);
        Assert.That(ambiguity, Does.Contain("2 retained scene descriptors"));
    }

    [Test]
    public void PromotionActiveSelection_UsesAuthoritativeBaseForDescriptorlessBootstrap()
    {
        var candidates = new List<PromotionSceneCandidate>
        {
            new PromotionSceneCandidate(new SceneID(61), LoadSceneMode.Single,
                LocalPhysicsMode.None, false),
            new PromotionSceneCandidate(new SceneID(62), LoadSceneMode.Additive,
                LocalPhysicsMode.None, false)
        };

        Assert.That(ScenesModule.TrySelectPromotionActiveScene(
            candidates, new SceneID(61), new SceneID(99),
            out var active, out var usedFallback, out var failure), Is.True, failure);
        Assert.That(active, Is.EqualTo(new SceneID(61)));
        Assert.That(usedFallback, Is.True,
            "A descriptorless active bootstrap must remain physical but not authoritative.");

        Assert.That(ScenesModule.TrySelectPromotionActiveScene(
            candidates, new SceneID(61), new SceneID(62),
            out active, out usedFallback, out failure), Is.True, failure);
        Assert.That(active, Is.EqualTo(new SceneID(62)));
        Assert.That(usedFallback, Is.False,
            "An already-authoritative additive active scene should remain active.");
    }

    [Test]
    public void PromotionManifestSettings_NormalizeOnlyExactMigration()
    {
        var retained = new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.Physics3D,
            isPublic = false
        };

        var legacy = ScenesModule.GetPromotionManifestSettings(retained, false, false);
        Assert.That(legacy.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(legacy.physicsMode, Is.EqualTo(LocalPhysicsMode.Physics3D));
        Assert.That(legacy.isPublic, Is.False);

        var exactBase = ScenesModule.GetPromotionManifestSettings(retained, true, true);
        var exactAdditive = ScenesModule.GetPromotionManifestSettings(retained, true, false);
        Assert.That(exactBase.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(exactAdditive.mode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(exactAdditive.physicsMode, Is.EqualTo(retained.physicsMode));
        Assert.That(exactAdditive.isPublic, Is.EqualTo(retained.isPublic));
    }

    [Test]
    public void ExactPromotion_PrivateSceneWithRemoteHuman_UsesCandidateReality()
    {
        var module = (ScenePlayersModule)FormatterServices.GetUninitializedObject(
            typeof(ScenePlayersModule));
        var transition = new HostMigrationTransitionOptions(
            "best-effort-private-membership", 1,
            new[] { new PlayerID(1, false), new PlayerID(2, false) });

        Assert.That(module.ValidateExactPromotionSceneMembership(
            transition, out var failure), Is.True, failure);
    }

    [Test]
    public void Promotion_PreservesLocalPrivateSceneMembership_Idempotently()
    {
        var local = new PlayerID(1, false);
        var publicScene = new SceneID(8);
        var privateScene = new SceneID(9);
        var memberships = new Dictionary<SceneID, List<PlayerID>>
        {
            [publicScene] = new List<PlayerID> { local },
            [privateScene] = new List<PlayerID>()
        };

        ScenePlayersModule.RestorePromotedLocalSceneMembership(
            local, new[] { publicScene, privateScene }, memberships);
        ScenePlayersModule.RestorePromotedLocalSceneMembership(
            local, new[] { publicScene, privateScene }, memberships);

        Assert.That(memberships[publicScene], Is.EqualTo(new[] { local }));
        Assert.That(memberships[privateScene], Is.EqualTo(new[] { local }));
    }

    [Test]
    public void ExactSceneManifest_AllowsRepeatedBuildSceneWithDistinctSceneIds()
    {
        var actions = new List<SceneAction>
        {
            BuildSceneAction(17, 1),
            BuildSceneAction(17, 2)
        };

        Assert.That(
            ScenesModule.TryValidateExactSceneManifestUniqueness(actions, out var failure),
            Is.True, failure);
        Assert.That(failure, Is.Null);
    }

    [Test]
    public void ExactSceneManifest_AllowsRepeatedAddressableGuidWithDistinctSceneIds()
    {
        var actions = new List<SceneAction>
        {
            AddressableSceneAction("abcdef", 3),
            AddressableSceneAction("ABCDEF", 4)
        };

        Assert.That(
            ScenesModule.TryValidateExactSceneManifestUniqueness(actions, out var failure),
            Is.True, failure);
        Assert.That(failure, Is.Null);
    }

    [Test]
    public void ExactSceneManifest_StillRequiresUniqueSceneIds()
    {
        var actions = new List<SceneAction>
        {
            BuildSceneAction(17, 3),
            AddressableSceneAction("abcdef", 3)
        };

        Assert.That(
            ScenesModule.TryValidateExactSceneManifestUniqueness(actions, out var failure),
            Is.False);
        Assert.That(failure, Does.Contain($"SceneID {new SceneID(3)}"));
        Assert.That(failure, Does.Contain("more than once"));
    }

    [Test]
    public void ExactSceneManifest_AllowsDistinctBuildAndAddressableIdentities()
    {
        var actions = new List<SceneAction>
        {
            BuildSceneAction(17, 1),
            BuildSceneAction(18, 2),
            AddressableSceneAction("abcdef", 3),
            AddressableSceneAction("fedcba", 4)
        };

        Assert.That(
            ScenesModule.TryValidateExactSceneManifestUniqueness(actions, out var failure),
            Is.True);
        Assert.That(failure, Is.Null);
    }

    [Test]
    public void ExactSceneManifest_RequiresOneSingleBaseBeforeAnyAdditives()
    {
        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            Array.Empty<SceneAction>(), out var emptyFailure), Is.False);
        Assert.That(emptyFailure, Does.Contain("empty"));

        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            new[] { BuildSceneAction(17, 1) }, out var additiveFailure), Is.False);
        Assert.That(additiveFailure, Does.Contain("first scene descriptor"));

        var normalized = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            BuildSceneAction(18, 2, LoadSceneMode.Additive)
        };
        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            normalized, out var normalizedFailure), Is.True, normalizedFailure);

        var repeatedSingle = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            BuildSceneAction(18, 2, LoadSceneMode.Single)
        };
        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            repeatedSingle, out var repeatedFailure), Is.False);
        Assert.That(repeatedFailure, Does.Contain("Single-mode load"));
    }

    [Test]
    public void ExactSceneManifest_RejectsUnsupportedFirstBatchActionsBeforeCleanup()
    {
        var actions = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction { sceneID = new SceneID(9) }
            }
        };

        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            actions, out var failure), Is.False);
        Assert.That(failure, Does.Contain("unsupported action"));
    }

    [Test]
    public void ExactSceneManifest_AllowsOneTrailingActiveSceneTarget()
    {
        var valid = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            BuildSceneAction(18, 2, LoadSceneMode.Additive),
            ActiveSceneAction(2)
        };

        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            valid, out var validFailure), Is.True, validFailure);

        var missingTarget = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            ActiveSceneAction(9)
        };
        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            missingTarget, out var missingFailure), Is.False);
        Assert.That(missingFailure, Does.Contain("not described"));

        var nonTrailing = new[]
        {
            BuildSceneAction(17, 1, LoadSceneMode.Single),
            ActiveSceneAction(1),
            BuildSceneAction(18, 2, LoadSceneMode.Additive)
        };
        Assert.That(ScenesModule.TryValidateExactSceneManifestShape(
            nonTrailing, out var orderFailure), Is.False);
        Assert.That(orderFailure, Does.Contain("final action"));
    }

    [Test]
    public void ExactPlayerSceneManifest_RebasesFilteredLoadsWithoutReplacingThem()
    {
        var filtered = new List<SceneAction>
        {
            BuildSceneAction(18, 2, LoadSceneMode.Additive, LocalPhysicsMode.Physics3D),
            BuildSceneAction(19, 3, LoadSceneMode.Additive, LocalPhysicsMode.None),
            ActiveSceneAction(2)
        };

        Assert.That(ScenesModule.TryNormalizeExactSceneManifestForPlayer(
            filtered, out var failure), Is.True, failure);
        Assert.That(filtered, Has.Count.EqualTo(3));
        Assert.That(filtered[0].loadSceneAction.sceneID, Is.EqualTo(new SceneID(3)));
        Assert.That(filtered[0].loadSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(filtered[1].loadSceneAction.sceneID, Is.EqualTo(new SceneID(2)));
        Assert.That(filtered[1].loadSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(filtered[1].loadSceneAction.parameters.physicsMode,
            Is.EqualTo(LocalPhysicsMode.Physics3D),
            "Normalizing visibility must not rewrite immutable local-physics state.");
        Assert.That(filtered[2].type, Is.EqualTo(SceneActionType.SetActive));
        Assert.That(filtered[2].setActiveSceneAction.sceneID, Is.EqualTo(new SceneID(2)));
    }

    [Test]
    public void ExactPlayerSceneManifest_FailsWhenEveryFilteredTargetUsesLocalPhysics()
    {
        var filtered = new List<SceneAction>
        {
            BuildSceneAction(18, 2, LoadSceneMode.Additive, LocalPhysicsMode.Physics3D),
            BuildSceneAction(19, 3, LoadSceneMode.Additive, LocalPhysicsMode.Physics2D)
        };

        Assert.That(ScenesModule.TryNormalizeExactSceneManifestForPlayer(
            filtered, out var failure), Is.False);
        Assert.That(failure, Does.Contain("all retained targets use local physics"));
    }

    [Test]
    public void ExactPlayerSceneManifest_UsesNormalizedBaseWhenActiveTargetWasFiltered()
    {
        var filtered = new List<SceneAction>
        {
            BuildSceneAction(19, 3, LoadSceneMode.Additive, LocalPhysicsMode.None),
            BuildSceneAction(20, 4, LoadSceneMode.Additive, LocalPhysicsMode.Physics2D)
        };

        Assert.That(ScenesModule.TryNormalizeExactSceneManifestForPlayer(
            filtered, out var failure), Is.True, failure);
        Assert.That(filtered, Has.Count.EqualTo(3));
        Assert.That(filtered[0].loadSceneAction.sceneID, Is.EqualTo(new SceneID(3)));
        Assert.That(filtered[0].loadSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(filtered[2].type, Is.EqualTo(SceneActionType.SetActive));
        Assert.That(filtered[2].setActiveSceneAction.sceneID, Is.EqualTo(new SceneID(3)),
            "A filtered authoritative active target must not leave stale or arbitrary active-scene state.");
    }

    [Test]
    public void SceneHistory_RetainsOnlyTheFinalValidActiveSceneAction()
    {
        var history = new SceneHistory();
        history.AddLoadAction(BuildSceneAction(17, 1, LoadSceneMode.Single).loadSceneAction);
        history.AddLoadAction(BuildSceneAction(18, 2, LoadSceneMode.Additive).loadSceneAction);
        history.AddSetActiveAction(ActiveSceneAction(1).setActiveSceneAction);
        history.AddSetActiveAction(ActiveSceneAction(2).setActiveSceneAction);
        history.Flush();

        var actions = history.GetFullHistory().actions;
        Assert.That(actions, Has.Count.EqualTo(3));
        Assert.That(actions[2].type, Is.EqualTo(SceneActionType.SetActive));
        Assert.That(actions[2].setActiveSceneAction.sceneID, Is.EqualTo(new SceneID(2)));
    }

    [Test]
    public void ExactSceneReconciliation_RequiresStableSceneIdAndDescriptorKind()
    {
        var retained = new SceneID(4);

        Assert.That(ScenesModule.IsExactSceneDescriptorIdentityMatch(
            retained, retained, false, false), Is.True);
        Assert.That(ScenesModule.IsExactSceneDescriptorIdentityMatch(
            retained, new SceneID(5), false, false), Is.False,
            "Re-keying a retained scene would replay unload/spawn lifecycle state.");
        Assert.That(ScenesModule.IsExactSceneDescriptorIdentityMatch(
            retained, retained, true, false), Is.False,
            "A live Addressable scene cannot silently become a build-scene descriptor.");
    }

    [Test]
    public void ExactSceneReconciliation_RetiresEveryStaleKeptSceneHierarchy()
    {
        Assert.That(ScenesModule.RequiresStaleKeptSceneHierarchyRetirement(false, true), Is.True,
            "A retained bootstrap scene must retire its hierarchy before unregistering.");
        Assert.That(ScenesModule.RequiresStaleKeptSceneHierarchyRetirement(true, true), Is.False,
            "An authoritative target remains registered and is reconciled in place.");
        Assert.That(ScenesModule.RequiresStaleKeptSceneHierarchyRetirement(false, false), Is.False,
            "A stale ordinary scene can be physically unloaded after the manifest commits.");
    }

    [Test]
    public void StaleKeptSceneRetirement_PreflightIsPureAndManualRootSurvivesUnspawned()
    {
        var managerObject = new GameObject("stale bootstrap retirement manager");
        var identityObject = new GameObject("package managed bootstrap root");
        managerObject.SetActive(false);
        var sceneId = new SceneID(401);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, sceneId, false);
            var identity = identityObject.AddComponent<NetworkIdentity>();
            var networkId = new NetworkID(4010);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            var factory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies")
                .Add(hierarchy);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                factory, "_hierarchies").Add(sceneId, hierarchy);

            Assert.That(factory.TryPreflightExactStaleSceneRetirement(
                sceneId, managerObject.scene, false, out var preflightFailure), Is.True,
                preflightFailure);
            Assert.That(identity.IsSpawned(false), Is.True,
                "Pure preflight must not run package despawn lifecycle.");
            Assert.That(hierarchy.TryGetIdentity(networkId, out var retained), Is.True);
            Assert.That(retained, Is.SameAs(identity));

            Assert.That(factory.TryRetireExactStaleSceneHierarchy(
                sceneId, managerObject.scene, false, out var retirementFailure), Is.True,
                retirementFailure);
            Assert.That(identity, Is.Not.Null,
                "Package-managed physical state survives when its stale network role retires.");
            Assert.That(identity.IsSpawned(false), Is.False);
            Assert.That(hierarchy.TryGetIdentity(networkId, out _), Is.False,
                "The retained physical scene must not be detached with a live hierarchy entry.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(sceneId);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void StaleKeptSceneRetirement_PreflightRejectsUnregisteredLiveRoleWithoutMutation()
    {
        var managerObject = new GameObject("stale bootstrap orphan preflight manager");
        var identityObject = new GameObject("unregistered live bootstrap identity");
        managerObject.SetActive(false);
        var sceneId = new SceneID(402);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, sceneId, false);
            var identity = identityObject.AddComponent<NetworkIdentity>();
            var networkId = new NetworkID(4020);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            GetHierarchyField<List<NetworkIdentity>>(hierarchy, "_spawnedIdentities")
                .Clear();
            GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(
                hierarchy, "_spawnedIdentitiesMap").Clear();

            var factory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies")
                .Add(hierarchy);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                factory, "_hierarchies").Add(sceneId, hierarchy);

            Assert.That(factory.TryPreflightExactStaleSceneRetirement(
                sceneId, managerObject.scene, false, out var failure), Is.False);
            Assert.That(failure, Does.Contain("outside SceneID"));
            Assert.That(identity.IsSpawned(false), Is.True,
                "A rejected pure preflight must leave the live role untouched.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(sceneId);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void StaleKeptSceneRetirement_TargetsPromotedServerHierarchyBeforeDetachment()
    {
        var managerObject = new GameObject("promoted stale bootstrap retirement manager");
        var identityObject = new GameObject("promoted package managed bootstrap root");
        managerObject.SetActive(false);
        var sceneId = new SceneID(403);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, sceneId, true);
            var identity = identityObject.AddComponent<NetworkIdentity>();
            var networkId = new NetworkID(4030);
            hierarchy.ManualEarlySpawn(identity, networkId);
            hierarchy.ManualFinalizeSpawn(identity);

            var factory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies")
                .Add(hierarchy);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                factory, "_hierarchies").Add(sceneId, hierarchy);

            Assert.That(factory.TryPreflightExactStaleSceneRetirement(
                sceneId, managerObject.scene, true, out var preflightFailure), Is.True,
                preflightFailure);
            Assert.That(factory.TryRetireExactStaleSceneHierarchy(
                sceneId, managerObject.scene, true, out var retirementFailure), Is.True,
                retirementFailure);
            Assert.That(identity.IsSpawned(true), Is.False);
            Assert.That(hierarchy.TryGetIdentity(networkId, out _), Is.False,
                "The promoted server factory must not detach with a live stale hierarchy role.");
            Assert.That(identity, Is.Not.Null,
                "Package-managed state survives physical bootstrap preservation.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(sceneId);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExactSceneReconciliation_BuffersIncrementalActionsBehindPostBoundary()
    {
        var scenes = new ScenesModule(null, null);
        var deferred = new List<SceneAction>();
        SetSceneField(scenes, "_requiresTransferReconciliation", true);
        SetSceneField(scenes, "_deferredExactIncrementalActions", deferred);

        var incoming = new List<SceneAction>
        {
            new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction { sceneID = new SceneID(9) }
            }
        };
        var handler = typeof(ScenesModule).GetMethod(
            "OnSceneActionsBatch",
            InstanceFields,
            null,
            new[] { typeof(PlayerID), typeof(SceneActionsBatch), typeof(bool) },
            null);
        Assert.That(handler, Is.Not.Null);

        handler.Invoke(scenes, new object[]
        {
            default(PlayerID),
            new SceneActionsBatch { actions = incoming },
            false
        });
        incoming.Clear();

        Assert.That(deferred, Has.Count.EqualTo(1),
            "The migration snapshot must finish before a later scene transition is applied.");
        Assert.That(deferred[0].unloadSceneAction.sceneID, Is.EqualTo(new SceneID(9)));
    }

    [Test]
    public void ExactSceneReconciliation_RejectsDuplicateInitialManifest()
    {
        var scenes = new ScenesModule(null, null);
        SetSceneField(scenes, "_requiresTransferReconciliation", true);
        SetSceneField(scenes, "_exactTransferBaselineReceived", true);
        SetSceneField(scenes, "_isTransferingToNewServer", false);

        var handler = typeof(ScenesModule).GetMethod(
            "OnSceneActionsBatch",
            InstanceFields,
            null,
            new[] { typeof(PlayerID), typeof(FirstSceneActionsBatch), typeof(bool) },
            null);
        Assert.That(handler, Is.Not.Null);
        LogAssert.Expect(LogType.Error,
            "[ScenesModule] The replacement authority sent more than one initial scene manifest.");
        handler.Invoke(scenes, new object[]
        {
            default(PlayerID),
            new FirstSceneActionsBatch { actions = new List<SceneAction>() },
            false
        });

        var failure = typeof(ScenesModule).GetField(
            "_transferReconciliationFailure", InstanceFields)?.GetValue(scenes) as Exception;
        Assert.That(failure, Is.Not.Null);
        Assert.That(failure.Message, Does.Contain("more than one initial scene manifest"));
    }

    [Test]
    public void RetainedBootstrapDetachment_DoesNotReplayPhysicalUnloadLifecycle()
    {
        var scenes = new ScenesModule(null, null);
        var unityScene = SceneManager.GetActiveScene();
        var sceneId = new SceneID(77);
        var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
        Assert.That(addScene, Is.Not.Null);
        addScene.Invoke(scenes, new object[]
        {
            unityScene,
            new PurrSceneSettings
            {
                mode = LoadSceneMode.Additive,
                physicsMode = LocalPhysicsMode.None
            },
            sceneId
        });

        var publicUnloadCallbacks = 0;
        var registrationCallbacks = 0;
        scenes.onPreSceneUnloaded += (_, _) => publicUnloadCallbacks++;
        scenes.onSceneUnloaded += (_, _) => publicUnloadCallbacks++;
        scenes.onPostSceneUnloaded += (_, _) => publicUnloadCallbacks++;
        scenes.onSceneRegistrationRemoved += (id, asServer) =>
        {
            Assert.That(id, Is.EqualTo(sceneId));
            Assert.That(asServer, Is.False);
            registrationCallbacks++;
        };

        Assert.That(scenes.DetachRetainedPhysicalSceneRegistration(unityScene), Is.True);

        Assert.That(publicUnloadCallbacks, Is.Zero);
        Assert.That(registrationCallbacks, Is.EqualTo(1));
        Assert.That(scenes.TryGetSceneState(sceneId, out _), Is.False);
        Assert.That(unityScene.IsValid() && unityScene.isLoaded, Is.True,
            "Detaching the network registration must leave the physical bootstrap scene alive.");
    }

    [Test]
    public void StagedExactScene_IsHiddenFromPublicQueriesUntilStructuralCommit()
    {
        var scenes = new ScenesModule(null, null);
        var unityScene = SceneManager.GetActiveScene();
        var sceneId = new SceneID(78);
        var settings = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None
        };
        var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
        Assert.That(addScene, Is.Not.Null);
        addScene.Invoke(scenes, new object[] { unityScene, settings, sceneId });

        var staged = GetSceneField<Dictionary<SceneID, SceneState>>(
            scenes, "_stagedExactScenes");
        staged.Add(sceneId, new SceneState(unityScene, settings));

        Assert.That(scenes.scenes, Is.Empty);
        Assert.That(scenes.sceneStates, Is.Empty);
        Assert.That(scenes.TryGetSceneState(sceneId, out _), Is.False);
        Assert.That(scenes.TryGetSceneID(unityScene, out _), Is.False);
        Assert.That(scenes.TryGetScene(unityScene.buildIndex, out _), Is.False);
        Assert.That(scenes.IsSceneLoaded(unityScene.buildIndex), Is.False);
        Assert.That(scenes.TryGetRegisteredOrStagedSceneState(sceneId, out var internalState),
            Is.True, "Core factories must still resolve the scene before Unity Start runs.");
        Assert.That(internalState.scene.handle, Is.EqualTo(unityScene.handle));

        var publish = typeof(ScenesModule).GetMethod(
            "PublishStagedExactScenesAfterStructuralCommit", InstanceFields);
        Assert.That(publish, Is.Not.Null);
        publish.Invoke(scenes, null);

        Assert.That(scenes.scenes, Is.EqualTo(new[] { sceneId }));
        Assert.That(scenes.TryGetSceneState(sceneId, out _), Is.True);
        Assert.That(scenes.TryGetSceneID(unityScene, out var publishedId), Is.True);
        Assert.That(publishedId, Is.EqualTo(sceneId));
    }

#if ADDRESSABLES_PURRNET_SUPPORT
    [Test]
    public void StagedExactAddressableScene_IsHiddenFromGuidQueriesUntilStructuralCommit()
    {
        var scenes = new ScenesModule(null, null);
        var unityScene = SceneManager.GetActiveScene();
        var sceneId = new SceneID(79);
        const string guid = "0123456789abcdef0123456789abcdef";
        var settings = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None
        };
        var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
        var registerGuid = typeof(ScenesModule).GetMethod(
            "RegisterAddressableSceneGuid", InstanceFields);
        Assert.That(addScene, Is.Not.Null);
        Assert.That(registerGuid, Is.Not.Null);
        addScene.Invoke(scenes, new object[] { unityScene, settings, sceneId });
        registerGuid.Invoke(scenes, new object[] { sceneId, guid });
        GetSceneField<Dictionary<SceneID, SceneState>>(scenes, "_stagedExactScenes")
            .Add(sceneId, new SceneState(unityScene, settings));

        Assert.That(scenes.IsAddressableScene(sceneId), Is.False);
        Assert.That(scenes.IsAddressableSceneLoaded(guid), Is.False);
        Assert.That(scenes.TryGetSceneIdByAddressableGuid(guid, out _), Is.False);
        Assert.That(scenes.GetSceneIdsByAddressableGuid(guid), Is.Empty);

        var publish = typeof(ScenesModule).GetMethod(
            "PublishStagedExactScenesAfterStructuralCommit", InstanceFields);
        Assert.That(publish, Is.Not.Null);
        publish.Invoke(scenes, null);

        Assert.That(scenes.IsAddressableScene(sceneId), Is.True);
        Assert.That(scenes.IsAddressableSceneLoaded(guid), Is.True);
        Assert.That(scenes.TryGetSceneIdByAddressableGuid(guid, out var publishedId), Is.True);
        Assert.That(publishedId, Is.EqualTo(sceneId));
    }

    [UnityTest]
    public IEnumerator ExactAddressableRetirement_PreservesNetworkManagerScene()
    {
        var managerObject = new GameObject("protected addressable retirement manager");
        managerObject.SetActive(false);
        var unrelatedScene = SceneManager.CreateScene(
            $"UnprotectedAddressableRetirement-{Guid.NewGuid():N}");
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var scenes = new ScenesModule(manager, null);

            Assert.That(scenes.ShouldPreserveExactAddressableRetirementScene(
                managerObject.scene), Is.True,
                "A failed exact load must not unload the scene containing its NetworkManager.");
            Assert.That(scenes.ShouldPreserveExactAddressableRetirementScene(
                unrelatedScene), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        var unload = SceneManager.UnloadSceneAsync(unrelatedScene);
        if (unload != null)
            yield return unload;
    }
#endif

    [Test]
    public void PromotedListenSceneBinding_PreflightsWholeManifestBeforeMutation()
    {
        var server = new ScenesModule(null, null);
        var listenClient = new ScenesModule(null, null);
        var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
        Assert.That(addScene, Is.Not.Null);

        var validId = new SceneID(81);
        var invalidId = new SceneID(82);
        var settings = new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None
        };
        addScene.Invoke(server, new object[] { SceneManager.GetActiveScene(), settings, validId });
        addScene.Invoke(server, new object[] { default(Scene), settings, invalidId });

        var actionScenes = typeof(ScenesModule).GetField(
            "_sceneActionScenes", InstanceFields)?.GetValue(server) as HashSet<SceneID>;
        Assert.That(actionScenes, Is.Not.Null);
        actionScenes.Add(validId);
        actionScenes.Add(invalidId);

        Assert.That(listenClient.TryBuildPromotedListenSceneBindingPlan(
            server, out var bindings, out var failure), Is.False);
        Assert.That(failure, Does.Contain("unloaded"));
        Assert.That(bindings, Has.Count.EqualTo(1),
            "The local plan may be partial, but it must remain non-authoritative until proof succeeds.");
        Assert.That(listenClient.scenes, Is.Empty);
        Assert.That(listenClient.sceneStates, Is.Empty,
            "A late invalid descriptor must not leave a partially bound listen-client graph.");
    }

    [Test]
    public void PromotedListenSceneBinding_PreparesOnceAndCommitsWithoutReplay()
    {
        var managerObject = new GameObject("promoted listen scene prepare manager");
        managerObject.SetActive(false);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;

            var server = new ScenesModule(manager, null);
            var listenClient = new ScenesModule(manager, null);
            var serverModules = new ModulesCollection(manager, true);
            serverModules.AddModule(server);
            SetNetworkManagerField(manager, "_serverModules", serverModules);
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));

            var sceneId = new SceneID(83);
            var settings = new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,
                physicsMode = LocalPhysicsMode.None,
                isPublic = true
            };
            var addScene = typeof(ScenesModule).GetMethod("AddScene", InstanceFields);
            Assert.That(addScene, Is.Not.Null);
            addScene.Invoke(server,
                new object[] { SceneManager.GetActiveScene(), settings, sceneId });
            GetSceneField<HashSet<SceneID>>(server, "_sceneActionScenes").Add(sceneId);

            var preReboundCallbacks = 0;
            var reboundCallbacks = 0;
            var publicCallbacks = 0;
            listenClient.onPreRetainedSceneRebound += (_, _) => preReboundCallbacks++;
            listenClient.onRetainedSceneRebound += (_, _) => reboundCallbacks++;
            listenClient.onSceneRebound += (_, _) => publicCallbacks++;

            var setup = typeof(ScenesModule).GetMethod(
                "SetupExactPromotedListenClientScenes", InstanceFields);
            Assert.That(setup, Is.Not.Null);
            setup.Invoke(listenClient, null);

            Assert.That(preReboundCallbacks, Is.EqualTo(1),
                "Binding must prepare core factories before the wire baseline is committed.");
            Assert.That(reboundCallbacks, Is.Zero);
            Assert.That(publicCallbacks, Is.Zero);
            Assert.That(listenClient.PlayRetainedSceneReboundForScene(sceneId), Is.True);
            Assert.That(listenClient.PlayRetainedSceneReboundForScene(sceneId), Is.True);

            Assert.That(preReboundCallbacks, Is.EqualTo(1),
                "The promoted-listen commit must consume, not replay, its prepared pre phase.");
            Assert.That(reboundCallbacks, Is.EqualTo(1));
            Assert.That(publicCallbacks, Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ReconciliationBegin_IsolatesParticipantFailuresAndArmsLaterParticipants()
    {
        var throwingObject = new GameObject("throwing migration participant");
        var recordingObject = new GameObject("recording migration participant");
        try
        {
            var throwing = throwingObject.AddComponent<ThrowingMigrationParticipantIdentity>();
            var recording = recordingObject.AddComponent<RecordingMigrationParticipantIdentity>();
            var failures = new List<Exception>();
            var transition = new HostMigrationTransitionOptions("room-incarnation", 3);

            throwing.TriggerBeginHostMigrationReconciliation(transition, failures);
            recording.TriggerBeginHostMigrationReconciliation(transition, failures);

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(recording.beginCount, Is.EqualTo(1));
            Assert.That(recording.HasHostMigrationManualHierarchyParticipant(), Is.True);
            Assert.That(recording.OwnsHostMigrationManualRoot(recording, failures), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(throwingObject);
            UnityEngine.Object.DestroyImmediate(recordingObject);
        }
    }

    [Test]
    public void ReconciliationBegin_AllowsClaimedManualRootRemovalWhileRegularGraphStaysProven()
    {
        var managerObject = new GameObject("manual-root Begin manager");
        var ownerObject = new GameObject("regular manual-root owner");
        var manualObject = new GameObject("claimed manual root");
        managerObject.SetActive(false);
        var scene = new SceneID(84);
        HierarchyV2 hierarchy = null;
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var transition = new HostMigrationTransitionOptions("manual-root-removal", 4);
            SetNetworkManagerField(manager, "_expectedHostMigrationSession", transition);

            hierarchy = CreateBareHierarchy(manager, scene, false);
            var owner = ownerObject.AddComponent<ManualRootDroppingMigrationParticipantIdentity>();
            var manualRoot = manualObject.AddComponent<NetworkIdentity>();
            owner.Configure(hierarchy, manualRoot);

            var ownerId = new NetworkID(840);
            PrepareRetainedClientIdentity(owner, manager, hierarchy, scene, ownerId);
            RegisterBareClientIdentity(hierarchy, owner);
            var manualId = new NetworkID(841);
            hierarchy.ManualEarlySpawn(manualRoot, manualId);
            hierarchy.ManualFinalizeSpawn(manualRoot);

            InvokeHierarchyPrivate(hierarchy, "BeginTransferReconciliation", Array.Empty<object>());
            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out _), Is.False);
            hierarchy.ReceiveHostMigrationSession(transition, true);

            var topologies = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1);
            topologies.Add(new SceneSpawnReconcileSpawnTopology
            {
                spawnId = new SpawnID(84, new PlayerID(8, false), null),
                prototype = HierarchyPool.GetFullPrototype(owner.transform, null, true)
            });
            var preamble = new SceneSpawnReconcileBeginPacket
            {
                sceneId = scene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                spawns = topologies
            };
            var preambleArgs = new object[] { preamble };
            Assert.That((bool)InvokeHierarchyPrivate(
                hierarchy, "TryAcceptTransferPreamble", preambleArgs), Is.True);

            Assert.That(hierarchy.TryArmTransferReconciliation(out var failure),
                Is.True, failure);
            Assert.That(owner.beginCount, Is.EqualTo(1));
            Assert.That(manualRoot.IsSpawned(false), Is.False);
            Assert.That(hierarchy.TryGetIdentity(manualId, out _), Is.False);
            Assert.That(hierarchy.TryGetIdentity(ownerId, out var retainedOwner), Is.True);
            Assert.That(retainedOwner, Is.SameAs(owner));
            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out _), Is.False);
        }
        finally
        {
            if (hierarchy != null)
                InvokeHierarchyPrivate(
                    hierarchy, "ClearTransferReconciliationState", Array.Empty<object>());
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(manualObject);
            UnityEngine.Object.DestroyImmediate(ownerObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void FinalManualReadiness_DoesNotAwaitOmittedRegularParticipant()
    {
        var managerObject = new GameObject("omitted regular readiness manager");
        var participantObject = new GameObject("omitted regular readiness participant");
        managerObject.SetActive(false);
        var scene = new SceneID(85);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var participant = participantObject.AddComponent<FinalManualReadinessProbeIdentity>();
            participant.Configure(hierarchy, waitForCompletion: true,
                createUnclaimedRoot: false);
            PrepareRetainedClientIdentity(
                participant, manager, hierarchy, scene, new NetworkID(850));
            RegisterBareClientIdentity(hierarchy, participant);

            InvokeHierarchyPrivate(hierarchy,
                "BeginManualHierarchyParticipantReadiness",
                new object[] { new HashSet<NetworkIdentity>() });

            Assert.That(participant.reconcileCount, Is.Zero,
                "An unconfirmed regular root was omitted by authority and has no baseline to await.");
            Assert.That(GetHierarchyField<List<Task>>(
                hierarchy, "_pendingReconciliationReadiness"), Is.Empty);
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(participantObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void FinalManualReadiness_StillAwaitsClaimedManualRootParticipant()
    {
        var managerObject = new GameObject("manual readiness manager");
        var participantObject = new GameObject("manual readiness participant");
        managerObject.SetActive(false);
        var scene = new SceneID(86);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var participant = participantObject.AddComponent<FinalManualReadinessProbeIdentity>();
            participant.Configure(hierarchy, waitForCompletion: true,
                createUnclaimedRoot: false);
            hierarchy.ManualEarlySpawn(participant, new NetworkID(860));
            hierarchy.ManualFinalizeSpawn(participant);

            InvokeHierarchyPrivate(hierarchy,
                "BeginManualHierarchyParticipantReadiness",
                new object[] { new HashSet<NetworkIdentity> { participant } });

            Assert.That(participant.reconcileCount, Is.EqualTo(1));
            Assert.That(GetHierarchyField<List<Task>>(
                hierarchy, "_pendingReconciliationReadiness"), Has.Count.EqualTo(1));
            participant.CompleteReadiness();
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(participantObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void FinalManualReadiness_RejectsUnclaimedRootCreatedByReconcile()
    {
        var managerObject = new GameObject("post-readiness ownership manager");
        var participantObject = new GameObject("post-readiness manual participant");
        managerObject.SetActive(false);
        var scene = new SceneID(87);
        HierarchyV2 hierarchy = null;
        FinalManualReadinessProbeIdentity participant = null;
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            hierarchy = CreateBareHierarchy(manager, scene, false);
            participant = participantObject.AddComponent<FinalManualReadinessProbeIdentity>();
            participant.Configure(hierarchy, waitForCompletion: false,
                createUnclaimedRoot: true);
            hierarchy.ManualEarlySpawn(participant, new NetworkID(870));
            hierarchy.ManualFinalizeSpawn(participant);
            SetHierarchyField(hierarchy, "_transferReconciliationOptions",
                new HostMigrationTransitionOptions("post-readiness-ownership", 5));
            SetHierarchyField(hierarchy, "_transferReconciliationRequested", true);
            SetHierarchyField(hierarchy, "_transferReconciliationComplete", false);
            SetHierarchyField(hierarchy, "_transferEndReceived", true);

            LogAssert.Expect(LogType.Error, new Regex(
                "^\\[HierarchyV2\\] Scene 087 has invalid manual-root ownership after " +
                "reconciliation readiness:"));
            InvokeHierarchyPrivate(
                hierarchy, "TryFinalizeTransferReconciliation", Array.Empty<object>());

            Assert.That(participant.reconcileCount, Is.EqualTo(1));
            Assert.That(participant.unclaimedRoot, Is.Not.Null);
            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("no longer claimed"));
        }
        finally
        {
            if (hierarchy != null)
                InvokeHierarchyPrivate(
                    hierarchy, "ClearTransferReconciliationState", Array.Empty<object>());
            if (participant && participant.unclaimedRoot)
                UnityEngine.Object.DestroyImmediate(participant.unclaimedRoot.gameObject);
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(participantObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ReconciliationBegin_StructuralFailureDoesNotMutatePackageBaselines()
    {
        var managerObject = new GameObject("structural preflight manager");
        var invalidObject = new GameObject("retained root without id");
        var participantObject = new GameObject("participant that must remain untouched");
        managerObject.SetActive(false);
        var scene = new SceneID(43);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetNetworkManagerField(manager, "_expectedHostMigrationSession",
                new HostMigrationTransitionOptions("structural-preflight", 7));

            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var invalid = invalidObject.AddComponent<NetworkIdentity>();
            invalid.SetIdentity(manager, hierarchy, scene, false, false);
            var participant = participantObject.AddComponent<RecordingMigrationParticipantIdentity>();
            participant.SetID(new NetworkID(430));
            participant.SetIdentity(manager, hierarchy, scene, false, false);
            GetHierarchyField<List<NetworkIdentity>>(hierarchy,
                "_spawnedIdentities").Add(invalid);
            GetHierarchyField<List<NetworkIdentity>>(hierarchy,
                "_spawnedIdentities").Add(participant);

            InvokeHierarchyPrivate(hierarchy, "BeginTransferReconciliation", Array.Empty<object>());

            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("identity list/map counts differ"));
            Assert.That(participant.beginCount, Is.Zero,
                "Known structural failure must stop before package baseline hooks run.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(participantObject);
            UnityEngine.Object.DestroyImmediate(invalidObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [UnityTest]
    public IEnumerator ReconciliationBegin_RejectsPhysicalCrossSceneDriftBeforePackageHooks()
    {
        var managerObject = new GameObject("physical scene preflight manager");
        var participantObject = new GameObject("physically drifted retained identity");
        managerObject.SetActive(false);
        var logicalScene = new SceneID(42);
        var driftScene = SceneManager.CreateScene($"RetainedDrift-{Guid.NewGuid():N}");
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetNetworkManagerField(manager, "_expectedHostMigrationSession",
                new HostMigrationTransitionOptions("physical-scene-preflight", 8));

            var hierarchy = CreateBareHierarchy(manager, logicalScene, false);
            var participant = participantObject.AddComponent<RecordingMigrationParticipantIdentity>();
            participant.SetID(new NetworkID(420));
            participant.SetIdentity(manager, hierarchy, logicalScene, false, false);
            GetHierarchyField<List<NetworkIdentity>>(hierarchy,
                "_spawnedIdentities").Add(participant);
            GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(hierarchy,
                "_spawnedIdentitiesMap").Add(participant.id.Value, participant);

            SceneManager.MoveGameObjectToScene(participantObject, driftScene);
            InvokeHierarchyPrivate(hierarchy, "BeginTransferReconciliation", Array.Empty<object>());

            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("physically belongs"));
            Assert.That(failure.Message, Does.Contain(driftScene.name));
            Assert.That(participant.beginCount, Is.Zero);
        }
        finally
        {
            NetworkPoolManager.RemovePool(logicalScene);
            UnityEngine.Object.DestroyImmediate(participantObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        var unload = SceneManager.UnloadSceneAsync(driftScene);
        if (unload != null)
            yield return unload;
    }

    [Test]
    public void TransitionOptions_RequireScopedEpoch_AndSnapshotExpectedRoster()
    {
        var roster = new[] { new PlayerID(7, false), new PlayerID(9, false) };
        var options = new HostMigrationTransitionOptions("room-incarnation", 4, roster);

        roster[0] = new PlayerID(99, false);

        Assert.That(options.canReconcile, Is.True);
        Assert.That(options.sessionId, Is.EqualTo("room-incarnation"));
        Assert.That(options.epoch, Is.EqualTo(4));
        Assert.That(options.expectedPlayers, Has.Count.EqualTo(2));
        Assert.That(options.expectedPlayers[0], Is.EqualTo(new PlayerID(7, false)));
        Assert.That(options.expectedPlayers, Is.Not.InstanceOf<PlayerID[]>(),
            "The public roster must not expose the mutable source array.");

        Assert.That(default(HostMigrationTransitionOptions).canReconcile, Is.False);
        Assert.That(new HostMigrationTransitionOptions("room-incarnation", 0).canReconcile, Is.False);
        Assert.That(new HostMigrationTransitionOptions(null, 4).canReconcile, Is.False);
    }

    [Test]
    public void TransitionOptions_SessionIdentity_IsOrdinalScopeAndEpoch_NotRoster()
    {
        var left = new HostMigrationTransitionOptions("Room-A", 3,
            new[] { new PlayerID(1, false) });
        var sameSession = new HostMigrationTransitionOptions("Room-A", 3,
            new[] { new PlayerID(2, false) });

        Assert.That(left, Is.EqualTo(sameSession));
        Assert.That(left, Is.Not.EqualTo(new HostMigrationTransitionOptions("room-a", 3)));
        Assert.That(left, Is.Not.EqualTo(new HostMigrationTransitionOptions("Room-A", 4)));
    }

    [Test]
    public void PrototypeCompatibility_IgnoresRuntimeTransforms_ButRequiresStableIdentityAndShape()
    {
        using var retained = CreatePrototype(
            rootPosition: Vector3.zero,
            rootId: 10,
            childId: 11,
            rootPrefabId: 5,
            parentId: 2,
            parentPath: new[] { 1 });
        using var authoritative = CreatePrototype(
            rootPosition: new Vector3(50, 60, 70),
            rootId: 10,
            childId: 11,
            rootPrefabId: 5,
            parentId: 2,
            parentPath: new[] { 1 },
            localOffset: new Vector3(8, 9, 10));

        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, authoritative), Is.True,
            "Prediction/network state owns runtime transforms; the spawn manifest owns identity and shape.");

        using var wrongId = CreatePrototype(Vector3.zero, 10, 12, 5, 2, new[] { 1 });
        using var wrongPrefab = CreatePrototype(Vector3.zero, 10, 11, 6, 2, new[] { 1 });
        using var wrongParent = CreatePrototype(Vector3.zero, 10, 11, 5, 3, new[] { 1 });
        using var wrongPath = CreatePrototype(Vector3.zero, 10, 11, 5, 2, new[] { 2 });

        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, wrongId), Is.False);
        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, wrongPrefab), Is.False);
        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, wrongParent), Is.False);
        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, wrongPath), Is.False);
    }

    [Test]
    public void PrototypeCompatibility_RequiresCompleteFrameworkAndActiveParentShape()
    {
        using var retained = CreatePrototype(Vector3.zero, 10, 11, 5, null, null);
        using var inactive = CreatePrototype(Vector3.zero, 10, 11, 5, null, null,
            childActive: false);

        var shortFramework = DisposableList<GameObjectFrameworkPiece>.Create(1);
        shortFramework.Add(new GameObjectFrameworkPiece(
            new LocalTransform(Vector3.zero, Quaternion.identity, Vector3.one),
            new PrefabPieceID(5, 0), new NetworkID(10), 0, true, Array.Empty<int>()));
        using var missingChild = new GameObjectPrototype(Vector3.zero, Quaternion.identity,
            Vector3.one, null, null, shortFramework, 0);

        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, inactive), Is.False);
        Assert.That(HierarchyV2.ArePrototypesCompatible(retained, missingChild), Is.False);
    }

    [Test]
    public void SpawnTopologyManifest_ClassifiesAndConsumesOnlyExactDeclaredSpawns()
    {
        var target = new PlayerID(7, false);
        var retainedSpawn = new SpawnID(1, target, null);
        var freshSpawn = new SpawnID(2, target, null);
        var declarations = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(2);
        declarations.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = retainedSpawn,
            bypassPool = true,
            prototype = CreatePrototype(Vector3.zero, 10, 11, 5, null, null)
        });
        declarations.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = freshSpawn,
            isAsync = true,
            prototype = CreatePrototype(Vector3.zero, 20, 21, 7, null, null)
        });

        using var retainedTopology = CreatePrototype(Vector3.one, 10, 11, 5, null, null);
        var existingRoots = new Dictionary<NetworkID, NetworkID>
        {
            [new NetworkID(10)] = new NetworkID(10),
            [new NetworkID(11)] = new NetworkID(10)
        };
        var retainedRoots = new Dictionary<NetworkID, GameObjectPrototype>
        {
            [new NetworkID(10)] = retainedTopology
        };

        Assert.That(SceneSpawnReconcileManifest.TryCreate(declarations, existingRoots,
            retainedRoots, out var manifest, out var failure), Is.True, failure);
        using (manifest)
        {
            var declaredRetained = manifest.GetTopology(0);
            var wrongFlags = new SpawnPacket
            {
                sceneId = new SceneID(3),
                packetIdx = retainedSpawn,
                bypassPool = false,
                prototype = declaredRetained.prototype
            };
            Assert.That(manifest.TryConsume(new SceneID(3), wrongFlags,
                out _, out failure), Is.False);
            Assert.That(failure, Does.Contain("scene, flags, and topology"));
            Assert.That(manifest.unconsumedCount, Is.EqualTo(2),
                "A rejected packet must not burn its declaration.");

            wrongFlags.bypassPool = true;
            Assert.That(manifest.TryConsume(new SceneID(3), wrongFlags,
                out var retainedClassification, out failure), Is.True, failure);
            Assert.That(retainedClassification.isRetained, Is.True);
            Assert.That(retainedClassification.retainedRootId, Is.EqualTo(new NetworkID(10)));
            Assert.That(manifest.TryConsume(new SceneID(3), wrongFlags,
                out _, out failure), Is.False);
            Assert.That(failure, Does.Contain("already consumed"));

            var declaredFresh = manifest.GetTopology(1);
            var freshPacket = new SpawnPacket
            {
                sceneId = new SceneID(3),
                packetIdx = freshSpawn,
                isAsync = true,
                prototype = declaredFresh.prototype
            };
            Assert.That(manifest.TryConsume(new SceneID(3), freshPacket,
                out var freshClassification, out failure), Is.True, failure);
            Assert.That(freshClassification.isRetained, Is.False);
            Assert.That(manifest.unconsumedCount, Is.Zero);
        }
    }

    [Test]
    public void SpawnTopologyManifest_RejectsGlobalDuplicateIdsAndClassifiesCrossRootReplacement()
    {
        var target = new PlayerID(7, false);
        var duplicateIds = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(2);
        duplicateIds.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = new SpawnID(1, target, null),
            prototype = CreatePrototype(Vector3.zero, 10, 11, 5, null, null)
        });
        duplicateIds.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = new SpawnID(2, target, null),
            prototype = CreatePrototype(Vector3.zero, 10, 11, 5, null, null)
        });

        Assert.That(SceneSpawnReconcileManifest.TryCreate(duplicateIds,
            new Dictionary<NetworkID, NetworkID>(),
            new Dictionary<NetworkID, GameObjectPrototype>(), out _, out var failure), Is.False);
        Assert.That(failure, Does.Contain("declared by more than one topology entry"));
        DisposeTopologyDeclarations(duplicateIds);

        var crossRoot = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1);
        crossRoot.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = new SpawnID(3, target, null),
            prototype = CreatePrototype(Vector3.zero, 10, 21, 5, null, null)
        });
        using var firstRoot = CreatePrototype(Vector3.zero, 10, 11, 5, null, null);
        using var secondRoot = CreatePrototype(Vector3.zero, 20, 21, 7, null, null);
        var existing = new Dictionary<NetworkID, NetworkID>
        {
            [new NetworkID(10)] = new NetworkID(10),
            [new NetworkID(21)] = new NetworkID(20)
        };
        var retained = new Dictionary<NetworkID, GameObjectPrototype>
        {
            [new NetworkID(10)] = firstRoot,
            [new NetworkID(20)] = secondRoot
        };

        Assert.That(SceneSpawnReconcileManifest.TryCreate(crossRoot, existing, retained,
            out var replacementManifest, out failure), Is.True, failure);
        using (replacementManifest)
        {
            var declared = replacementManifest.GetTopology(0);
            var packet = new SpawnPacket
            {
                sceneId = new SceneID(3),
                packetIdx = new SpawnID(3, target, null),
                prototype = declared.prototype
            };
            Assert.That(replacementManifest.TryConsume(new SceneID(3), packet,
                out var classification, out failure), Is.True, failure);
            Assert.That(classification.isRetained, Is.False);
            Assert.That(classification.replacementRootIds,
                Is.EquivalentTo(new[] { new NetworkID(10), new NetworkID(20) }),
                "Only the overlapping roots should be retired; the Unity scene and unrelated " +
                "compatible roots remain loaded.");
        }
    }

    [Test]
    public void SpawnTopologyManifest_ClassifiesChangedRetainedRootForTargetedReplacement()
    {
        var target = new PlayerID(7, false);
        var spawn = new SpawnID(4, target, null);
        var declarations = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1);
        declarations.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = spawn,
            prototype = CreatePrototype(Vector3.zero, 10, 11, 99, null, null)
        });

        using var retainedTopology = CreatePrototype(Vector3.zero, 10, 11, 5, null, null);
        var existing = new Dictionary<NetworkID, NetworkID>
        {
            [new NetworkID(10)] = new NetworkID(10),
            [new NetworkID(11)] = new NetworkID(10)
        };
        var retained = new Dictionary<NetworkID, GameObjectPrototype>
        {
            [new NetworkID(10)] = retainedTopology
        };

        Assert.That(SceneSpawnReconcileManifest.TryCreate(declarations, existing, retained,
            out var manifest, out var failure), Is.True, failure);
        using (manifest)
        {
            var declared = manifest.GetTopology(0);
            var packet = new SpawnPacket
            {
                sceneId = new SceneID(3),
                packetIdx = spawn,
                prototype = declared.prototype
            };
            Assert.That(manifest.TryConsume(new SceneID(3), packet,
                out var classification, out failure), Is.True, failure);
            Assert.That(classification.isRetained, Is.False);
            Assert.That(classification.replacementRootIds,
                Is.EqualTo(new[] { new NetworkID(10) }));
        }
    }

    [Test]
    public void SpawnTopologyManifest_StrictPromotedListenProofStillRejectsReplacement()
    {
        var declarations = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1);
        declarations.Add(new SceneSpawnReconcileSpawnTopology
        {
            spawnId = new SpawnID(5, new PlayerID(7, false), null),
            prototype = CreatePrototype(Vector3.zero, 10, 11, 99, null, null)
        });
        using var retainedTopology = CreatePrototype(Vector3.zero, 10, 11, 5, null, null);
        var existing = new Dictionary<NetworkID, NetworkID>
        {
            [new NetworkID(10)] = new NetworkID(10),
            [new NetworkID(11)] = new NetworkID(10)
        };
        var retained = new Dictionary<NetworkID, GameObjectPrototype>
        {
            [new NetworkID(10)] = retainedTopology
        };

        Assert.That(SceneSpawnReconcileManifest.TryCreate(
            declarations, existing, retained, false, out _, out var failure), Is.False);
        Assert.That(failure, Does.Contain("not topology-compatible"));
        DisposeTopologyDeclarations(declarations);
    }

    [Test]
    public void TargetedTopologyReplacement_RetiresOnlyClassifiedRetainedRoot()
    {
        var managerObject = new GameObject("targeted replacement manager");
        var replacedObject = new GameObject("incompatible retained root");
        var preservedObject = new GameObject("compatible retained root");
        managerObject.SetActive(false);
        var scene = new SceneID(3);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var hierarchy = CreateBareHierarchy(manager, scene, false);

            var replaced = replacedObject.AddComponent<NetworkIdentity>();
            var preserved = preservedObject.AddComponent<NetworkIdentity>();
            var replacedId = new NetworkID(10);
            var preservedId = new NetworkID(20);
            PrepareRetainedClientIdentity(replaced, manager, hierarchy, scene, replacedId);
            PrepareRetainedClientIdentity(preserved, manager, hierarchy, scene, preservedId);
            RegisterBareClientIdentity(hierarchy, replaced);
            RegisterBareClientIdentity(hierarchy, preserved);

            var retainedRoots = GetHierarchyField<HashSet<NetworkIdentity>>(
                hierarchy, "_retainedTransferRoots");
            retainedRoots.Add(replaced);
            retainedRoots.Add(preserved);
            var retainedById = GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(
                hierarchy, "_retainedTransferRootsById");
            retainedById.Add(replacedId, replaced);
            retainedById.Add(preservedId, preserved);

            var arguments = new object[]
            {
                new SpawnID(4, new PlayerID(7, false), null),
                new[] { replacedId },
                null
            };
            Assert.That((bool)InvokeHierarchyPrivate(
                hierarchy, "TryRetireReplacedTransferRoots", arguments), Is.True,
                arguments[2] as string);

            Assert.That(hierarchy.TryGetIdentity(replacedId, out _), Is.False);
            Assert.That(hierarchy.TryGetIdentity(preservedId, out var stillRegistered), Is.True);
            Assert.That(stillRegistered, Is.SameAs(preserved));
            Assert.That(retainedRoots, Is.EquivalentTo(new[] { preserved }));
            Assert.That(retainedById.Keys, Is.EquivalentTo(new[] { preservedId }));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(replacedObject);
            UnityEngine.Object.DestroyImmediate(preservedObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void SpawnTopologyPreamble_ContainsNoCustomSpawnData()
    {
        var fields = typeof(SceneSpawnReconcileSpawnTopology).GetFields(
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(Array.ConvertAll(fields, field => field.Name), Is.EquivalentTo(new[]
        {
            nameof(SceneSpawnReconcileSpawnTopology.spawnId),
            nameof(SceneSpawnReconcileSpawnTopology.bypassPool),
            nameof(SceneSpawnReconcileSpawnTopology.isAsync),
            nameof(SceneSpawnReconcileSpawnTopology.prototype)
        }));
    }

    [Test]
    public void ExactTopologyGate_LaterSceneRejectionLeavesEarlierPackageStateUntouched()
    {
        var managerObject = new GameObject("multi-scene topology gate manager");
        var firstObject = new GameObject("earlier retained topology root");
        var secondObject = new GameObject("later retained topology root");
        managerObject.SetActive(false);
        var firstScene = new SceneID(141);
        var secondScene = new SceneID(142);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var transition = new HostMigrationTransitionOptions("multi-scene-topology", 4);
            SetNetworkManagerField(manager, "_expectedHostMigrationSession", transition);
            SetNetworkManagerField(manager, "_hostMigrationSession", transition);
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            var clientModules = new ModulesCollection(manager, false);
            SetNetworkManagerField(manager, "_clientModules", clientModules);

            var first = CreateBareHierarchy(manager, firstScene, false);
            var second = CreateBareHierarchy(manager, secondScene, false);
            var firstIdentity = firstObject.AddComponent<PromotedListenRegularParticipantIdentity>();
            var secondIdentity = secondObject.AddComponent<PromotedListenRegularParticipantIdentity>();
            PrepareRetainedClientIdentity(firstIdentity, manager, first, firstScene, new NetworkID(1410));
            PrepareRetainedClientIdentity(secondIdentity, manager, second, secondScene, new NetworkID(1420));
            RegisterBareClientIdentity(first, firstIdentity);
            RegisterBareClientIdentity(second, secondIdentity);

            InvokeHierarchyPrivate(first, "BeginTransferReconciliation", Array.Empty<object>());
            InvokeHierarchyPrivate(second, "BeginTransferReconciliation", Array.Empty<object>());
            first.ReceiveHostMigrationSession(transition, true);
            second.ReceiveHostMigrationSession(transition, true);

            var factory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies")
                .AddRange(new[] { first, second });
            var map = GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(factory, "_hierarchies");
            map.Add(firstScene, first);
            map.Add(secondScene, second);
            clientModules.AddModule(factory);
            Assert.That(factory.RegisterExactInboundSceneSet(
                transition, new[] { firstScene, secondScene }, out var registerFailure),
                Is.True, registerFailure);

            var firstPreamble = new SceneSpawnReconcileBeginPacket
            {
                sceneId = firstScene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                spawns = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1)
            };
            firstPreamble.spawns.Add(new SceneSpawnReconcileSpawnTopology
            {
                spawnId = new SpawnID(1, new PlayerID(1, false), null),
                prototype = HierarchyPool.GetFullPrototype(firstIdentity.transform, null, true)
            });
            InvokeHierarchyPrivate(first, "OnSceneSpawnReconcileBeginPacket", new object[]
            {
                new PlayerID(1, false), firstPreamble, false
            });

            Assert.That(firstIdentity.beginCount, Is.Zero,
                "An accepted earlier preamble must remain package-pure while a later scene is unproven.");
            Assert.That(firstIdentity.reboundCount, Is.Zero);

            var invalidLaterPreamble = new SceneSpawnReconcileBeginPacket
            {
                sceneId = secondScene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                spawns = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1)
            };
            invalidLaterPreamble.spawns.Add(new SceneSpawnReconcileSpawnTopology
            {
                spawnId = new SpawnID(2, new PlayerID(1, false), null),
                prototype = default
            });
            var rejectedTopology = new Regex(
                "^\\[HierarchyV2\\] Scene 142 rejected its host-migration topology preflight: " +
                "spawn .* has an empty topology\\.$");
            LogAssert.Expect(LogType.Error, rejectedTopology);
            LogAssert.Expect(LogType.Error, rejectedTopology);
            InvokeHierarchyPrivate(second, "OnSceneSpawnReconcileBeginPacket", new object[]
            {
                new PlayerID(1, false), invalidLaterPreamble, false
            });

            Assert.That(firstIdentity.beginCount, Is.Zero);
            Assert.That(firstIdentity.reboundCount, Is.Zero,
                "A later-scene topology failure must cause zero earlier-scene rebound mutation.");
            Assert.That(factory.TryAuthorizeExactTransferSnapshot(
                first, transition, out var gateFailure), Is.False);
            Assert.That(gateFailure, Does.Contain("topology"));
        }
        finally
        {
            NetworkPoolManager.RemovePool(firstScene);
            NetworkPoolManager.RemovePool(secondScene);
            UnityEngine.Object.DestroyImmediate(secondObject);
            UnityEngine.Object.DestroyImmediate(firstObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void PromotedListenRegularRoot_IsDirectlyRegisteredProvenAndReadyOnlyAfterFinish()
    {
        var managerObject = new GameObject("promoted listen topology integration");
        var identityObject = new GameObject("promoted listen retained regular root");
        managerObject.SetActive(false);
        try
        {
            var transport = managerObject.AddComponent<HostMigrationCoreTestTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            manager.transport = transport;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            var clientModules = new ModulesCollection(manager, false);
            SetNetworkManagerField(manager, "_clientModules", clientModules);
            var transition = new HostMigrationTransitionOptions("promoted-listen", 9);
            SetNetworkManagerField(manager, "_expectedHostMigrationSession", transition);
            SetNetworkManagerField(manager, "_hostMigrationSession", transition);

            var scene = new SceneID(44);
            var server = CreateBareHierarchy(manager, scene, true);
            var client = CreateBareHierarchy(manager, scene, false);
            var clientFactory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(clientFactory, "_rawHierarchies").Add(client);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                clientFactory, "_hierarchies").Add(scene, client);
            clientModules.AddModule(clientFactory);
            var identity = identityObject.AddComponent<PromotedListenRegularParticipantIdentity>();
            identity.PreparePrefabInfo(5, 0, false, false);
            identity.SetID(new NetworkID(501));
            identity.SetIdentity(manager, server, scene, true, false);

            GetHierarchyField<List<NetworkIdentity>>(server, "_spawnedIdentities").Add(identity);
            GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(
                server, "_spawnedIdentitiesMap").Add(identity.id.Value, identity);

            Assert.That(client.TryAttachPromotedListenGraphCore(
                server, out var newlyRegistered, out var attachFailure), Is.True, attachFailure);
            Assert.That(client.TryPublishPromotedListenRegistrySignals(
                newlyRegistered, out attachFailure), Is.True, attachFailure);
            Assert.That(client.TryGetIdentity(identity.id.Value, out var attached), Is.True);
            Assert.That(attached, Is.SameAs(identity));
            Assert.That(identity.IsSpawned(false), Is.True);

            InvokeHierarchyPrivate(client, "BeginTransferReconciliation", Array.Empty<object>());
            client.ReceiveHostMigrationSession(transition, true);
            Assert.That(identity.beginCount, Is.Zero,
                "Package Begin hooks must wait for the transaction-wide topology proof.");
            Assert.That(clientFactory.RegisterExactInboundSceneSet(
                transition, new[] { scene }, out var gateFailure), Is.True, gateFailure);

            var topologies = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1);
            topologies.Add(new SceneSpawnReconcileSpawnTopology
            {
                spawnId = new SpawnID(1, new PlayerID(1, false), null),
                prototype = HierarchyPool.GetFullPrototype(identity.transform, null, true)
            });
            var preamble = new SceneSpawnReconcileBeginPacket
            {
                sceneId = scene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                spawns = topologies
            };
            InvokeHierarchyPrivate(client, "OnSceneSpawnReconcileBeginPacket", new object[]
            {
                new PlayerID(1, false), preamble, false
            });
            Assert.That(identity.beginCount, Is.EqualTo(1));

            var finishes = new List<SpawnID>();
            Assert.That(client.TryApplyAcceptedPromotedListenManifest(
                finishes, out var applyFailure), Is.True, applyFailure);

            Assert.That(finishes, Has.Count.EqualTo(1));
            Assert.That(identity.reboundCount, Is.Zero,
                "Topology proof must leave the root pending until the ordered baseline/Finish phase.");

            InvokeHierarchyPrivate(client, "OnSceneSpawnReconcilePacket", new object[]
            {
                new PlayerID(1, false),
                new SceneSpawnReconcilePacket
                {
                    sceneId = scene,
                    sessionId = transition.sessionId,
                    epoch = transition.epoch
                },
                false
            });
            Assert.That(identity.reboundCount, Is.Zero,
                "An early End marker must not let the finalizer bypass a pending Finish barrier.");
            Assert.That(client.isTransferReconciliationComplete, Is.False);

            InvokeHierarchyPrivate(client, "OnFinishSpawnPacket", new object[]
            {
                new PlayerID(1, false),
                new FinishSpawnPacket { sceneId = scene, packetIdx = finishes[0] },
                false
            });

            Assert.That(identity.reboundCount, Is.EqualTo(1));
            Assert.That(identity.readinessCount, Is.EqualTo(1));
            Assert.That(client.isTransferReconciliationComplete, Is.True);
        }
        finally
        {
            NetworkPoolManager.RemovePool(new SceneID(44));
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void PromotedListenGraph_LateIdentityConflictCannotPartiallyAttach()
    {
        var managerObject = new GameObject("promoted listen atomic graph");
        var firstObject = new GameObject("first promoted identity");
        var secondObject = new GameObject("conflicting promoted identity");
        managerObject.SetActive(false);
        var scene = new SceneID(45);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));

            var server = CreateBareHierarchy(manager, scene, true);
            var client = CreateBareHierarchy(manager, scene, false);
            var first = firstObject.AddComponent<NetworkIdentity>();
            var second = secondObject.AddComponent<NetworkIdentity>();
            PrepareServerIdentity(first, manager, server, scene, new NetworkID(601));
            PrepareServerIdentity(second, manager, server, scene, new NetworkID(602));
            RegisterBareServerIdentity(server, first);
            RegisterBareServerIdentity(server, second);

            SetNetworkIdentityField(second, "_idClient", (NetworkID?)new NetworkID(999));
            Assert.That(client.TryAttachPromotedListenGraphCore(
                server, out var newlyRegistered, out var attachFailure), Is.False);
            Assert.That(attachFailure, Does.Contain("conflicting server/client NetworkIDs"));
            Assert.That(newlyRegistered, Is.Null,
                "A failed pure proof must not report any registered identity.");
            Assert.That(first.IsSpawned(false), Is.False,
                "A late identity conflict must be found before the first role is attached.");
            Assert.That(GetHierarchyField<List<NetworkIdentity>>(client,
                "_spawnedIdentities"), Is.Empty);
            Assert.That(GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(client,
                "_spawnedIdentitiesMap"), Is.Empty);

            SetNetworkIdentityField(second, "_idClient", (NetworkID?)new NetworkID(602));
            var earlySignals = 0;
            var addedSignals = 0;
            client.onEarlyIdentityAdded += _ =>
            {
                Assert.That(GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(client,
                    "_spawnedIdentitiesMap").Count, Is.EqualTo(2));
                earlySignals++;
            };
            client.onIdentityAdded += _ =>
            {
                Assert.That(GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(client,
                    "_spawnedIdentitiesMap").Count, Is.EqualTo(2));
                addedSignals++;
            };

            Assert.That(client.TryAttachPromotedListenGraphCore(
                server, out newlyRegistered, out attachFailure), Is.True, attachFailure);
            Assert.That(client.TryPublishPromotedListenRegistrySignals(
                newlyRegistered, out attachFailure), Is.True, attachFailure);
            Assert.That(first.IsSpawned(false), Is.True);
            Assert.That(second.IsSpawned(false), Is.True);
            Assert.That(earlySignals, Is.EqualTo(2));
            Assert.That(addedSignals, Is.EqualTo(2));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(secondObject);
            UnityEngine.Object.DestroyImmediate(firstObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExactPromotion_RejectsPendingRegisteredSpawnBeforeRoleMutation()
    {
        var managerObject = new GameObject("promotion queue preflight");
        var identityObject = new GameObject("pending registered identity");
        managerObject.SetActive(false);
        var scene = new SceneID(46);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetNetworkManagerField(manager, "_hostMigrationSession",
                new HostMigrationTransitionOptions("promotion-queue", 1));

            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var identity = identityObject.AddComponent<NetworkIdentity>();
            identity.SetID(new NetworkID(701));
            identity.SetIdentity(manager, hierarchy, scene, false, false);
            InvokeHierarchyPrivate(hierarchy, "RegisterIdentity",
                new object[] { identity, true, true });

            var exception = Assert.Throws<InvalidOperationException>(
                () => hierarchy.PromoteToServerModule());
            Assert.That(exception.Message, Does.Contain("spawn-lifecycle=1"));
            Assert.That(identity.IsSpawned(false), Is.True);
            Assert.That(identity.IsSpawned(true), Is.False,
                "The exact queue preflight must run before client-to-server role mutation.");
            Assert.That(GetHierarchyField<HashSet<NetworkIdentity>>(hierarchy,
                "_toSpawnNextFrame"), Does.Contain(identity));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExactTransfer_RejectsPendingFinishWithoutClearingOldAuthorityState()
    {
        var managerObject = new GameObject("transfer finish preflight");
        managerObject.SetActive(false);
        var scene = new SceneID(47);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetNetworkManagerField(manager, "_expectedHostMigrationSession",
                new HostMigrationTransitionOptions("transfer-queue", 2));

            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var finish = new SpawnID(4, new PlayerID(3, false), null);
            GetHierarchyField<List<SpawnID>>(hierarchy, "_toCompleteNextFrame").Add(finish);
            var generation = GetHierarchyField<ulong>(hierarchy, "_clientSpawnGeneration");

            var exception = Assert.Throws<InvalidOperationException>(
                () => hierarchy.TransferToNewServer());
            Assert.That(exception.Message, Does.Contain("outgoing-finish=1"));
            Assert.That(GetHierarchyField<List<SpawnID>>(hierarchy,
                "_toCompleteNextFrame"), Is.EqualTo(new[] { finish }));
            Assert.That(GetHierarchyField<ulong>(hierarchy,
                "_clientSpawnGeneration"), Is.EqualTo(generation));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void NetworkManagerExactAuthoritySwitchPreflight_RejectsBeforeTransitionMutation()
    {
        var managerObject = new GameObject("manager hierarchy queue preflight");
        managerObject.SetActive(false);
        var scene = new SceneID(48);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));

            var hierarchy = CreateBareHierarchy(manager, scene, false);
            GetHierarchyField<List<SpawnID>>(hierarchy, "_toCompleteNextFrame").Add(
                new SpawnID(5, new PlayerID(3, false), null));
#pragma warning disable SYSLIB0050
            var factory = (HierarchyFactory)FormatterServices.GetUninitializedObject(
                typeof(HierarchyFactory));
#pragma warning restore SYSLIB0050
            SetHierarchyFactoryField(factory, "_manager", manager);
            SetHierarchyFactoryField(factory, "_rawHierarchies",
                new List<HierarchyV2> { hierarchy });
            var clientModules = new ModulesCollection(manager, false);
            clientModules.AddModule(factory);
            SetNetworkManagerField(manager, "_clientModules", clientModules);

            var args = new object[] { false, null };
            var method = typeof(NetworkManager).GetMethod(
                "TryValidateExactAuthoritySwitchPreflight", InstanceFields);
            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(manager, args), Is.False);
            Assert.That(args[1] as string, Does.Contain("outgoing-finish=1"));
            Assert.That(manager.isPromotingToServer, Is.False);
            Assert.That(manager.isTranferingToNewServer, Is.False);
            Assert.That(GetHierarchyField<List<SpawnID>>(hierarchy,
                "_toCompleteNextFrame"), Has.Count.EqualTo(1));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [UnityTest]
    public IEnumerator FactoryGraphPreflight_InvalidLaterHierarchyCannotPartiallyPromoteEarlierHierarchy()
    {
        var managerObject = new GameObject("factory graph preflight manager");
        var validObject = new GameObject("valid earlier retained identity");
        var invalidObject = new GameObject("dual-role later retained identity");
        managerObject.SetActive(false);
        var firstSceneId = new SceneID(49);
        var secondSceneId = new SceneID(50);
        var secondUnityScene = SceneManager.CreateScene($"HierarchyPreflight-{Guid.NewGuid():N}");
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            SetNetworkManagerField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetNetworkManagerField(manager, "_hostMigrationSession",
                new HostMigrationTransitionOptions("factory-graph-preflight", 3));

            var first = CreateBareHierarchy(manager, firstSceneId, false);
            var second = CreateBareHierarchy(manager, secondSceneId, false);
            SetHierarchyField(second, "_scene", secondUnityScene);
            SceneManager.MoveGameObjectToScene(invalidObject, secondUnityScene);

            var validIdentity = validObject.AddComponent<NetworkIdentity>();
            validIdentity.SetID(new NetworkID(801));
            validIdentity.SetIdentity(manager, first, firstSceneId, false, false);
            validIdentity.TriggerEarlySpawnEvent(false);
            validIdentity.TriggerSpawnEvent(false);
            RegisterBareClientIdentity(first, validIdentity);

            var invalidIdentity = invalidObject.AddComponent<NetworkIdentity>();
            invalidIdentity.SetID(new NetworkID(802));
            invalidIdentity.SetIdentity(manager, second, secondSceneId, false, false);
            invalidIdentity.TriggerEarlySpawnEvent(false);
            invalidIdentity.TriggerSpawnEvent(false);
            SetNetworkIdentityField(invalidIdentity, "_isSpawnedServer", true);
            SetNetworkIdentityField(invalidIdentity, "_serverHierarchy", second);
            SetNetworkIdentityField(invalidIdentity, "_spawnedCount", 2);
            RegisterBareClientIdentity(second, invalidIdentity);

            var scenes = new ScenesModule(manager, null);
            var sceneStates = GetSceneField<Dictionary<SceneID, SceneState>>(scenes, "_scenes");
            sceneStates.Add(firstSceneId, new SceneState(managerObject.scene, default));
            sceneStates.Add(secondSceneId, new SceneState(secondUnityScene, default));

            var factory = new HierarchyFactory(manager, scenes, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies")
                .AddRange(new[] { first, second });
            var hierarchyMap = GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                factory, "_hierarchies");
            hierarchyMap.Add(firstSceneId, first);
            hierarchyMap.Add(secondSceneId, second);

            var exception = Assert.Throws<InvalidOperationException>(
                () => factory.PromoteToServerModule());
            Assert.That(exception.Message, Does.Contain("live server role before promotion"));
            Assert.That(validIdentity.IsSpawned(false), Is.True);
            Assert.That(validIdentity.IsSpawned(true), Is.False,
                "Every hierarchy must be preflighted before the first identity changes roles.");
            Assert.That(invalidIdentity.IsSpawned(false), Is.True);
            Assert.That(invalidIdentity.IsSpawned(true), Is.True,
                "The invalid dual-role identity must also remain exactly as preflight found it.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(firstSceneId);
            NetworkPoolManager.RemovePool(secondSceneId);
            UnityEngine.Object.DestroyImmediate(invalidObject);
            UnityEngine.Object.DestroyImmediate(validObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        var unload = SceneManager.UnloadSceneAsync(secondUnityScene);
        if (unload != null)
            yield return unload;
    }

    [Test]
    public void RemoteSnapshotAbort_IsSessionScopedAndFailsReconciliationImmediately()
    {
        var managerObject = new GameObject("remote reconcile abort");
        managerObject.SetActive(false);
        var scene = new SceneID(51);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var transition = new HostMigrationTransitionOptions("abort-session", 4);
            ArmBareTransferReconciliation(hierarchy, transition);

            InvokeHierarchyPrivate(hierarchy, "OnSceneSpawnReconcileAbortPacket", new object[]
            {
                new PlayerID(4, false),
                new SceneSpawnReconcileAbortPacket
                {
                    sceneId = scene,
                    sessionId = transition.sessionId,
                    epoch = transition.epoch + 1,
                    reason = "stale rejection"
                },
                false
            });
            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out _), Is.False,
                "A delayed abort from another epoch must not poison this attempt.");

            LogAssert.Expect(LogType.Error,
                "[HierarchyV2] authoritative snapshot rejected");
            InvokeHierarchyPrivate(hierarchy, "OnSceneSpawnReconcileAbortPacket", new object[]
            {
                new PlayerID(4, false),
                new SceneSpawnReconcileAbortPacket
                {
                    sceneId = scene,
                    sessionId = transition.sessionId,
                    epoch = transition.epoch,
                    reason = "authoritative snapshot rejected"
                },
                false
            });

            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("authoritative snapshot rejected"));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void PromotedListenSnapshotRejection_AbortsDirectClientWithoutWaitingForWireTimeout()
    {
        var managerObject = new GameObject("local reconcile abort");
        managerObject.SetActive(false);
        var scene = new SceneID(52);
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            var clientModules = new ModulesCollection(manager, false);
            SetNetworkManagerField(manager, "_clientModules", clientModules);

            var server = CreateBareHierarchy(manager, scene, true);
            var client = CreateBareHierarchy(manager, scene, false);

            var clientFactory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(clientFactory, "_rawHierarchies").Add(client);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                clientFactory, "_hierarchies").Add(scene, client);
            clientModules.AddModule(clientFactory);
            SetHierarchyField(client, "_isPlayerReady", true);

            var player = new PlayerID(5, false);
#pragma warning disable SYSLIB0050
            var clientPlayers = (PlayersManager)FormatterServices.GetUninitializedObject(
                typeof(PlayersManager));
#pragma warning restore SYSLIB0050
            typeof(PlayersManager).GetField("<localPlayerId>k__BackingField", InstanceFields)
                ?.SetValue(clientPlayers, (PlayerID?)player);
            SetNetworkManagerField(manager, "_clientPlayersManager", clientPlayers);

            var transition = new HostMigrationTransitionOptions("local-abort-session", 5);
            ArmBareTransferReconciliation(client, transition);

            LogAssert.Expect(LogType.Error,
                "[HierarchyV2] promoted listen snapshot rejected");
            LogAssert.Expect(LogType.Error,
                "[HierarchyV2] promoted listen snapshot rejected");
            InvokeHierarchyPrivate(server, "RejectExactSpawnSnapshot", new object[]
            {
                player, transition, "promoted listen snapshot rejected"
            });

            Assert.That(client.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("promoted listen snapshot rejected"));
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [UnityTest]
    public IEnumerator ReconcilePreamble_RejectsSceneDriftCausedByBeginHookBeforeRebound()
    {
        var managerObject = new GameObject("begin-hook drift manager");
        var identityObject = new GameObject("begin-hook moving identity");
        managerObject.SetActive(false);
        var scene = new SceneID(53);
        var driftScene = SceneManager.CreateScene($"BeginHookDrift-{Guid.NewGuid():N}");
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            SetNetworkManagerField(manager, "_serverModules", new ModulesCollection(manager, true));
            var clientModules = new ModulesCollection(manager, false);
            SetNetworkManagerField(manager, "_clientModules", clientModules);
            var transition = new HostMigrationTransitionOptions("begin-hook-drift", 6);
            SetNetworkManagerField(manager, "_expectedHostMigrationSession", transition);

            var hierarchy = CreateBareHierarchy(manager, scene, false);
            var identity = identityObject.AddComponent<MovingBeginMigrationParticipantIdentity>();
            identity.moveTarget = driftScene;
            PrepareRetainedClientIdentity(
                identity, manager, hierarchy, scene, new NetworkID(901));
            RegisterBareClientIdentity(hierarchy, identity);

            InvokeHierarchyPrivate(hierarchy, "BeginTransferReconciliation", Array.Empty<object>());
            hierarchy.ReceiveHostMigrationSession(transition, true);
            Assert.That(identity.beginCount, Is.Zero,
                "Package Begin must wait until the transaction-wide topology proof succeeds.");

            var factory = new HierarchyFactory(manager, null, null, null);
            GetHierarchyFactoryField<List<HierarchyV2>>(factory, "_rawHierarchies").Add(hierarchy);
            GetHierarchyFactoryField<Dictionary<SceneID, HierarchyV2>>(
                factory, "_hierarchies").Add(scene, hierarchy);
            clientModules.AddModule(factory);
            Assert.That(factory.RegisterExactInboundSceneSet(
                transition, new[] { scene }, out var registerFailure),
                Is.True, registerFailure);

            var preamble = new SceneSpawnReconcileBeginPacket
            {
                sceneId = scene,
                sessionId = transition.sessionId,
                epoch = transition.epoch,
                spawns = DisposableList<SceneSpawnReconcileSpawnTopology>.Create(1)
            };
            preamble.spawns.Add(new SceneSpawnReconcileSpawnTopology
            {
                spawnId = new SpawnID(1, new PlayerID(6, false), null),
                prototype = HierarchyPool.GetFullPrototype(identity.transform, null, true)
            });
            LogAssert.Expect(LogType.Error, new Regex(
                "^\\[HierarchyV2\\] Scene 053 retained graph changed while package Begin hooks " +
                "armed exact reconciliation:"));
            InvokeHierarchyPrivate(hierarchy, "OnSceneSpawnReconcileBeginPacket", new object[]
            {
                new PlayerID(6, false), preamble, false
            });

            Assert.That(identity.beginCount, Is.EqualTo(1));
            Assert.That(identityObject.scene, Is.EqualTo(driftScene));
            Assert.That(hierarchy.TryGetTransferReconciliationFailure(out var failure), Is.True);
            Assert.That(failure.Message, Does.Contain("physically belongs"));
            Assert.That(factory.TryAuthorizeExactTransferSnapshot(
                hierarchy, transition, out _), Is.False,
                "A Begin hook that invalidates the proven graph must keep the body gate closed.");
            Assert.That(identity.reboundCount, Is.Zero);
        }
        finally
        {
            NetworkPoolManager.RemovePool(scene);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        var unload = SceneManager.UnloadSceneAsync(driftScene);
        if (unload != null)
            yield return unload;
    }

    private static HierarchyV2 CreateBareHierarchy(NetworkManager manager, SceneID scene,
        bool asServer)
    {
#pragma warning disable SYSLIB0050
        var hierarchy = (HierarchyV2)FormatterServices.GetUninitializedObject(typeof(HierarchyV2));
#pragma warning restore SYSLIB0050
        SetHierarchyField(hierarchy, "_manager", manager);
        SetHierarchyField(hierarchy, "_sceneId", scene);
        SetHierarchyField(hierarchy, "_scene", manager.gameObject.scene);
        SetHierarchyField(hierarchy, "_scenePool",
            NetworkPoolManager.GetScenePool(manager, manager.gameObject.scene, scene));
        SetHierarchyField(hierarchy, "_asServer", asServer);
        SetHierarchyField(hierarchy, "_enabled", true);
        SetHierarchyField(hierarchy, "_spawnedIdentities", new List<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_spawnedIdentitiesMap",
            new Dictionary<NetworkID, NetworkIdentity>());
        SetHierarchyField(hierarchy, "_retainedTransferRoots", new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_ownedManualTransferRoots", new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_confirmedTransferRoots", new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_retainedTransferRootsById",
            new Dictionary<NetworkID, NetworkIdentity>());
        SetHierarchyField(hierarchy, "_pendingReconciledSpawns",
            new Dictionary<SpawnID, DisposableList<NetworkIdentity>>());
        SetHierarchyField(hierarchy, "_pendingReconciliationReadiness", new List<Task>());
        SetHierarchyField(hierarchy, "_reconciliationNotifiedIdentities",
            new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_toSpawnNextFrame", new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_toSpawnNextFrameBuffer", new HashSet<NetworkIdentity>());
        SetHierarchyField(hierarchy, "_toCompleteNextFrame", new List<SpawnID>());
        SetHierarchyField(hierarchy, "_spawnPackets", new Dictionary<PlayerID, SpawnPacketBatch>());
        InitializeHierarchyCollection(hierarchy, "_exactBarrierBypassFinishes");
        InitializeHierarchyCollection(hierarchy, "_sceneReconcileEndsNextFrame");
        InitializeHierarchyCollection(hierarchy, "_pendingSpawns");
        InitializeHierarchyCollection(hierarchy, "_asyncPendingSpawns");
        InitializeHierarchyCollection(hierarchy, "_pendingFinishSpawns");
        InitializeHierarchyCollection(hierarchy, "_pendingDespawns");
        InitializeHierarchyCollection(hierarchy, "_cancelledPendingSpawns");
        InitializeHierarchyCollection(hierarchy, "_pendingAsyncObservers");
        InitializeHierarchyCollection(hierarchy, "_readyAsyncObservers");
        InitializeHierarchyCollection(hierarchy, "_failedAsyncObserverRoots");
        InitializeHierarchyCollection(hierarchy, "_relayAsyncSpawns");
        InitializeHierarchyCollection(hierarchy, "_failedAsyncSpawnRoots");
        InitializeHierarchyCollection(hierarchy, "_triggerLateObserverAdded");
        InitializeOptionalHierarchyCollection(hierarchy, "_pendingAsyncInstantiations");
        InitializeOptionalHierarchyCollection(hierarchy, "_reservedAsyncNetworkIds");
        SetHierarchyField(hierarchy, "_transferReconciliationComplete", true);
        return hierarchy;
    }

    private static void PrepareRetainedClientIdentity(NetworkIdentity identity,
        NetworkManager manager, HierarchyV2 client, SceneID scene, NetworkID id)
    {
        identity.PreparePrefabInfo(5, 0, false, false);
        identity.SetID(id);
        identity.SetIdentity(manager, client, scene, false, false);
        identity.TriggerEarlySpawnEvent(false);
        identity.TriggerSpawnEvent(false);
    }

    private static void PrepareServerIdentity(NetworkIdentity identity, NetworkManager manager,
        HierarchyV2 server, SceneID scene, NetworkID id)
    {
        identity.SetID(id);
        identity.SetIdentity(manager, server, scene, true, false);
    }

    private static void RegisterBareServerIdentity(HierarchyV2 server, NetworkIdentity identity)
    {
        GetHierarchyField<List<NetworkIdentity>>(server, "_spawnedIdentities").Add(identity);
        GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(
            server, "_spawnedIdentitiesMap").Add(identity.GetNetworkID(true).Value, identity);
    }

    private static void RegisterBareClientIdentity(HierarchyV2 client, NetworkIdentity identity)
    {
        GetHierarchyField<List<NetworkIdentity>>(client, "_spawnedIdentities").Add(identity);
        GetHierarchyField<Dictionary<NetworkID, NetworkIdentity>>(
            client, "_spawnedIdentitiesMap").Add(identity.GetNetworkID(false).Value, identity);
    }

    private static void ArmBareTransferReconciliation(HierarchyV2 hierarchy,
        HostMigrationTransitionOptions transition)
    {
        SetHierarchyField(hierarchy, "_transferReconciliationOptions", transition);
        SetHierarchyField(hierarchy, "_transferReconciliationRequested", true);
        SetHierarchyField(hierarchy, "_transferReconciliationComplete", false);
        SetHierarchyField(hierarchy, "_transferSessionValidated", true);
    }

    private static void SetNetworkIdentityField(NetworkIdentity identity,
        string fieldName, object value)
    {
        var field = typeof(NetworkIdentity).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(identity, value);
    }

    private static void SetHierarchyFactoryField(HierarchyFactory factory,
        string fieldName, object value)
    {
        var field = typeof(HierarchyFactory).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(factory, value);
    }

    private static T GetHierarchyFactoryField<T>(HierarchyFactory factory, string fieldName)
    {
        var field = typeof(HierarchyFactory).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        return (T)field.GetValue(factory);
    }

    private static object InvokeHierarchyFactoryPrivate(
        HierarchyFactory factory, string methodName, params object[] arguments)
    {
        var method = typeof(HierarchyFactory).GetMethod(methodName, InstanceFields);
        Assert.That(method, Is.Not.Null, methodName);
        return method.Invoke(factory, arguments);
    }

    private static void InitializeHierarchyCollection(HierarchyV2 hierarchy, string fieldName)
    {
        var field = typeof(HierarchyV2).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null);
        field.SetValue(hierarchy, Activator.CreateInstance(field.FieldType));
    }

    private static void InitializeOptionalHierarchyCollection(
        HierarchyV2 hierarchy, string fieldName)
    {
        var field = typeof(HierarchyV2).GetField(fieldName, InstanceFields);
        if (field != null)
            field.SetValue(hierarchy, Activator.CreateInstance(field.FieldType));
    }

    private static object InvokeHierarchyPrivate(HierarchyV2 hierarchy, string methodName,
        object[] arguments)
    {
        var method = typeof(HierarchyV2).GetMethod(methodName, InstanceFields);
        Assert.That(method, Is.Not.Null, methodName);
        return method.Invoke(hierarchy, arguments);
    }

    private static T GetHierarchyField<T>(HierarchyV2 hierarchy, string fieldName)
    {
        var field = typeof(HierarchyV2).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        return (T)field.GetValue(hierarchy);
    }

    private static int GetHierarchyCollectionCount(HierarchyV2 hierarchy, string fieldName)
    {
        var field = typeof(HierarchyV2).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        var collection = field.GetValue(hierarchy) as ICollection;
        Assert.That(collection, Is.Not.Null, fieldName);
        return collection.Count;
    }

    private static void SetNetworkManagerField(NetworkManager manager, string fieldName, object value)
    {
        var field = typeof(NetworkManager).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(manager, value);
    }

    private static void DisposeTopologyDeclarations(
        DisposableList<SceneSpawnReconcileSpawnTopology> declarations)
    {
        if (declarations.isDisposed)
            return;
        for (var i = 0; i < declarations.Count; i++)
            declarations[i].Dispose();
        declarations.Dispose();
    }

    private static GameObjectPrototype CreatePrototype(Vector3 rootPosition, ulong rootId,
        ulong childId, int rootPrefabId, ulong? parentId, int[] parentPath,
        Vector3? localOffset = null, bool childActive = true)
    {
        var framework = DisposableList<GameObjectFrameworkPiece>.Create(2);
        framework.Add(new GameObjectFrameworkPiece(
            new LocalTransform(localOffset ?? Vector3.zero, Quaternion.identity, Vector3.one),
            new PrefabPieceID(rootPrefabId, 0), new NetworkID(rootId), 1, true,
            Array.Empty<int>()));
        framework.Add(new GameObjectFrameworkPiece(
            new LocalTransform(localOffset ?? Vector3.one, Quaternion.identity, Vector3.one),
            new PrefabPieceID(rootPrefabId + 1, 0), new NetworkID(childId), 0, childActive,
            new[] { 0 }));

        return new GameObjectPrototype(rootPosition, Quaternion.identity, Vector3.one,
            parentId.HasValue ? new NetworkID(parentId.Value) : (NetworkID?)null,
            parentPath, framework, parentId.HasValue ? null : 0);
    }

    private static SceneAction BuildSceneAction(
        uint pathHash,
        ushort sceneId,
        LoadSceneMode mode = LoadSceneMode.Additive,
        LocalPhysicsMode physicsMode = LocalPhysicsMode.None)
    {
        return new SceneAction
        {
            type = SceneActionType.Load,
            loadSceneAction = new LoadSceneAction
            {
                scenePathHash = pathHash,
                sceneID = new SceneID(sceneId),
                parameters = new PurrSceneSettings
                {
                    mode = mode,
                    physicsMode = physicsMode
                }
            }
        };
    }

    private static SceneAction ActiveSceneAction(ushort sceneId)
    {
        return new SceneAction
        {
            type = SceneActionType.SetActive,
            setActiveSceneAction = new SetActiveSceneAction
            {
                sceneID = new SceneID(sceneId)
            }
        };
    }

    private static SceneAction AddressableSceneAction(string guid, ushort sceneId)
    {
        return new SceneAction
        {
            type = SceneActionType.LoadAddressable,
            loadAddressableSceneAction = new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = new SceneID(sceneId),
                parameters = new PurrSceneSettings { mode = LoadSceneMode.Additive }
            }
        };
    }

    private static void SetHierarchyField(HierarchyV2 hierarchy, string fieldName, object value)
    {
        var field = typeof(HierarchyV2).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, $"Missing {typeof(HierarchyV2).FullName}.{fieldName}");
        field.SetValue(hierarchy, value);
    }

    private static void SetSceneField(ScenesModule scenes, string fieldName, object value)
    {
        var field = typeof(ScenesModule).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, $"Missing {typeof(ScenesModule).FullName}.{fieldName}");
        field.SetValue(scenes, value);
    }

    private static T GetSceneField<T>(ScenesModule scenes, string fieldName)
    {
        var field = typeof(ScenesModule).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, $"Missing {typeof(ScenesModule).FullName}.{fieldName}");
        return (T)field.GetValue(scenes);
    }

    private static T GetScenePlayersField<T>(ScenePlayersModule scenes, string fieldName)
    {
        var field = typeof(ScenePlayersModule).GetField(fieldName, InstanceFields);
        Assert.That(field, Is.Not.Null, $"Missing {typeof(ScenePlayersModule).FullName}.{fieldName}");
        return (T)field.GetValue(scenes);
    }
}

public sealed class ThrowingMigrationParticipantIdentity : NetworkIdentity,
    IHostMigrationReconciliationParticipant
{
    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) =>
        throw new InvalidOperationException("expected test failure");

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition) =>
        Task.CompletedTask;
}

public sealed class RecordingMigrationParticipantIdentity : NetworkIdentity,
    IHostMigrationReconciliationParticipant, IHostMigrationManualHierarchyParticipant
{
    public int beginCount { get; private set; }

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) =>
        beginCount++;

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition) =>
        Task.CompletedTask;

    public bool OwnsHostMigrationManualRoot(NetworkIdentity root) => root == this;
}

public sealed class ManualRootDroppingMigrationParticipantIdentity : NetworkIdentity,
    IHostMigrationManualHierarchyParticipant
{
    private HierarchyV2 _hierarchy;
    private NetworkIdentity _manualRoot;

    public int beginCount { get; private set; }

    public void Configure(HierarchyV2 hierarchy, NetworkIdentity manualRoot)
    {
        _hierarchy = hierarchy;
        _manualRoot = manualRoot;
    }

    public bool OwnsHostMigrationManualRoot(NetworkIdentity root) =>
        ReferenceEquals(root, _manualRoot);

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition)
    {
        beginCount++;
        _hierarchy.ManualDespawn(_manualRoot);
    }

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition) =>
        Task.CompletedTask;
}

public sealed class FinalManualReadinessProbeIdentity : NetworkIdentity,
    IHostMigrationManualHierarchyParticipant
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private HierarchyV2 _hierarchy;
    private bool _waitForCompletion;
    private bool _createUnclaimedRoot;

    public int reconcileCount { get; private set; }
    public NetworkIdentity unclaimedRoot { get; private set; }

    public void Configure(HierarchyV2 hierarchy, bool waitForCompletion,
        bool createUnclaimedRoot)
    {
        _hierarchy = hierarchy;
        _waitForCompletion = waitForCompletion;
        _createUnclaimedRoot = createUnclaimedRoot;
    }

    public bool OwnsHostMigrationManualRoot(NetworkIdentity root) =>
        ReferenceEquals(root, this);

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) { }

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition)
    {
        reconcileCount++;
        if (_createUnclaimedRoot && !unclaimedRoot)
        {
            var rootObject = new GameObject("Unclaimed post-readiness root");
            unclaimedRoot = rootObject.AddComponent<NetworkIdentity>();
            _hierarchy.ManualEarlySpawn(unclaimedRoot, new NetworkID(879));
            _hierarchy.ManualFinalizeSpawn(unclaimedRoot);
        }

        return _waitForCompletion ? _completion.Task : Task.CompletedTask;
    }

    public void CompleteReadiness() => _completion.TrySetResult(true);
}

public sealed class PromotedListenRegularParticipantIdentity : NetworkIdentity,
    IHostMigrationReconciliationParticipant
{
    public int beginCount { get; private set; }
    public int reboundCount { get; private set; }
    public int readinessCount { get; private set; }

    protected override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        reboundCount++;

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition) =>
        beginCount++;

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition)
    {
        readinessCount++;
        return Task.CompletedTask;
    }
}

public sealed class MovingBeginMigrationParticipantIdentity : NetworkIdentity,
    IHostMigrationReconciliationParticipant
{
    public Scene moveTarget;
    public int beginCount { get; private set; }
    public int reboundCount { get; private set; }

    protected override void OnHostMigrationRebound(HostMigrationTransitionOptions transition) =>
        reboundCount++;

    public void BeginHostMigrationReconciliation(HostMigrationTransitionOptions transition)
    {
        beginCount++;
        SceneManager.MoveGameObjectToScene(gameObject, moveTarget);
    }

    public Task ReconcileHostMigrationAsync(HostMigrationTransitionOptions transition) =>
        Task.CompletedTask;
}

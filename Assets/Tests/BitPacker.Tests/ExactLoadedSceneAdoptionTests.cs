using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Pooling;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ExactLoadedSceneAdoptionTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void FreshSerializedNestedSceneIdentities_AreSafeToAdopt()
    {
        var parentObject = new GameObject("adoptable static parent");
        var childObject = new GameObject("adoptable static child");
        try
        {
            childObject.transform.SetParent(parentObject.transform);
            var parent = parentObject.AddComponent<NetworkIdentity>();
            var child = childObject.AddComponent<NetworkIdentity>();

            parent.PreparePrefabInfo(-2, 0, true, true);
            child.PreparePrefabInfo(-2, 1, true, true);
            child.parent = parent;

            Assert.That(parent.isSetup, Is.True);
            Assert.That(child.isSetup, Is.True);
            Assert.That(child.parent, Is.SameAs(parent));
            Assert.That(ScenesModule.TryPreflightUnregisteredExactSceneAdoption(
                    parentObject.scene, out var failure),
                Is.True, failure);

            child.isManualSpawn = true;
            Assert.That(ScenesModule.TryPreflightUnregisteredExactSceneAdoption(
                    parentObject.scene, out failure),
                Is.False);
            Assert.That(failure, Does.Contain("previously materialized network lifetime"));
        }
        finally
        {
            Object.DestroyImmediate(childObject);
            Object.DestroyImmediate(parentObject);
        }
    }

    [Test]
    public void StagedLoadedSceneAdoption_RollsBackRegistrationAndPoolWithoutPublicLifecycle()
    {
        var managerObject = new GameObject("loaded scene adoption manager");
        managerObject.SetActive(false);
        var id = new SceneID(612);
        HierarchyPool replacementPool = null;
        try
        {
            var manager = managerObject.AddComponent<NetworkManager>();
            manager.startServerFlags = StartFlags.None;
            manager.startClientFlags = StartFlags.None;
            var scenes = new ScenesModule(manager, null);
            var scene = managerObject.scene;

            Assert.That(ScenesModule.TryGetPhysicalLocalPhysicsMode(
                    scene, out var physicalMode, out var physicsFailure),
                Is.True, physicsFailure);
            var settings = new PurrSceneSettings
            {
                mode = LoadSceneMode.Additive,
                physicsMode = physicalMode,
                isPublic = true
            };

            SetField(scenes, "_requiresTransferReconciliation", true);
            SetField(scenes, "_deferredExactTransferManifest", new List<SceneAction>());

            var publicLifecycleCount = 0;
            scenes.onPreSceneLoaded += (_, _) => publicLifecycleCount++;
            scenes.onSceneLoaded += (_, _) => publicLifecycleCount++;
            scenes.onPostSceneLoaded += (_, _) => publicLifecycleCount++;
            scenes.onPreSceneUnloaded += (_, _) => publicLifecycleCount++;
            scenes.onSceneUnloaded += (_, _) => publicLifecycleCount++;
            scenes.onPostSceneUnloaded += (_, _) => publicLifecycleCount++;

            var stagedPool = NetworkPoolManager.GetScenePool(manager, scene, id);
            var stage = typeof(ScenesModule).GetMethod(
                "TryStageExactScene", PrivateInstance);
            Assert.That(stage, Is.Not.Null);
            var args = new object[] { scene, settings, id, null, true };
            Assert.That((bool)stage.Invoke(scenes, args), Is.True, args[3] as string);
            Assert.That(scenes.TryGetSceneState(id, out _), Is.False,
                "A staged adoption must stay unpublished until structural commit.");
            Assert.That(scenes.TryGetRegisteredOrStagedSceneState(id, out var stagedState), Is.True);
            Assert.That(stagedState.scene.handle, Is.EqualTo(scene.handle));
            Assert.That(publicLifecycleCount, Is.Zero);

            var retire = typeof(ScenesModule).GetMethod(
                "RetireAllStagedExactScenes", PrivateInstance);
            Assert.That(retire, Is.Not.Null);
            retire.Invoke(scenes, null);

            Assert.That(scene.IsValid() && scene.isLoaded, Is.True,
                "Rollback must preserve a Unity scene that predated the transaction.");
            Assert.That(scenes.TryGetRegisteredOrStagedSceneState(id, out _), Is.False);
            Assert.That(publicLifecycleCount, Is.Zero,
                "Core registration rollback is not a physical scene unload.");

            replacementPool = NetworkPoolManager.GetScenePool(manager, scene, id);
            Assert.That(replacementPool, Is.Not.SameAs(stagedPool),
                "Rollback must remove the abandoned new-SceneID pool before a retry.");
        }
        finally
        {
            NetworkPoolManager.RemovePool(id);
            Object.DestroyImmediate(managerObject);
        }
    }

    private static void SetField<T>(ScenesModule scenes, string name, T value)
    {
        var field = typeof(ScenesModule).GetField(name, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Missing field {name}");
        field.SetValue(scenes, value);
    }
}

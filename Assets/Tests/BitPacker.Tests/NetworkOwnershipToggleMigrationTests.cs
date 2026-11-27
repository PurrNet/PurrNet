using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;

public class NetworkOwnershipToggleMigrationTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(1, 1, TestName = "LegacyGameObjects_ActivateAndDeactivate")]
    [TestCase(0, 1, TestName = "LegacyGameObjects_OnlyDeactivate")]
    [TestCase(2, 3, TestName = "LegacyGameObjects_SeveralOfEach")]
    [TestCase(1, 0, TestName = "LegacyGameObjects_OnlyActivate")]
    public void LegacyGameObjectArraysMigrateEveryEntry(int activateCount, int deactivateCount)
    {
        var root = CreateInactiveToggle(out var toggle);

        try
        {
            var toActivate = CreateChildren(root, "activate", activateCount);
            var toDeactivate = CreateChildren(root, "deactivate", deactivateCount);

            SetField(toggle, "_toActivate", toActivate);
            SetField(toggle, "_toDeactivate", toDeactivate);

            Assert.DoesNotThrow(() => InvokeOnValidate(toggle));

            var migrated = GetField<OwnershipGameObjectToggle[]>(toggle, "_gameObjects");
            Assert.That(migrated, Has.Length.EqualTo(activateCount + deactivateCount));

            for (var i = 0; i < activateCount; i++)
            {
                Assert.That(migrated[i].target, Is.SameAs(toActivate[i]), $"activate entry {i}");
                Assert.IsTrue(migrated[i].activeAsOwner, $"activate entry {i}");
            }

            for (var i = 0; i < deactivateCount; i++)
            {
                var entry = migrated[activateCount + i];
                Assert.That(entry.target, Is.SameAs(toDeactivate[i]), $"deactivate entry {i}");
                Assert.IsFalse(entry.activeAsOwner, $"deactivate entry {i}");
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [TestCase(1, 1, TestName = "LegacyComponents_EnableAndDisable")]
    [TestCase(0, 1, TestName = "LegacyComponents_OnlyDisable")]
    [TestCase(2, 3, TestName = "LegacyComponents_SeveralOfEach")]
    [TestCase(1, 0, TestName = "LegacyComponents_OnlyEnable")]
    public void LegacyBehaviourArraysMigrateEveryEntry(int enableCount, int disableCount)
    {
        var root = CreateInactiveToggle(out var toggle);

        try
        {
            var toEnable = CreateBehaviours(root, "enable", enableCount);
            var toDisable = CreateBehaviours(root, "disable", disableCount);

            SetField(toggle, "_toEnable", toEnable);
            SetField(toggle, "_toDisable", toDisable);

            Assert.DoesNotThrow(() => InvokeOnValidate(toggle));

            var migrated = GetField<OwnershipComponentToggle[]>(toggle, "_components");
            Assert.That(migrated, Has.Length.EqualTo(enableCount + disableCount));

            for (var i = 0; i < enableCount; i++)
            {
                Assert.That(migrated[i].target, Is.SameAs(toEnable[i]), $"enable entry {i}");
                Assert.IsTrue(migrated[i].activeAsOwner, $"enable entry {i}");
            }

            for (var i = 0; i < disableCount; i++)
            {
                var entry = migrated[enableCount + i];
                Assert.That(entry.target, Is.SameAs(toDisable[i]), $"disable entry {i}");
                Assert.IsFalse(entry.activeAsOwner, $"disable entry {i}");
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // The root stays inactive so Awake (which calls Setup) never runs on the test object.
    static GameObject CreateInactiveToggle(out NetworkOwnershipToggle toggle)
    {
        var root = new GameObject(nameof(NetworkOwnershipToggleMigrationTests));
        root.SetActive(false);
        toggle = root.AddComponent<NetworkOwnershipToggle>();
        return root;
    }

    static GameObject[] CreateChildren(GameObject root, string prefix, int count)
    {
        var result = new GameObject[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = new GameObject($"{prefix} {i}");
            result[i].transform.SetParent(root.transform);
        }

        return result;
    }

    static Behaviour[] CreateBehaviours(GameObject root, string prefix, int count)
    {
        var children = CreateChildren(root, prefix, count);
        var result = new Behaviour[count];

        for (var i = 0; i < count; i++)
            result[i] = children[i].AddComponent<Light>();

        return result;
    }

    static void SetField(object target, string name, object value)
    {
        var field = typeof(NetworkOwnershipToggle).GetField(name, Flags);
        Assert.IsNotNull(field, $"field {name} not found");
        field.SetValue(target, value);
    }

    static T GetField<T>(object target, string name)
    {
        var field = typeof(NetworkOwnershipToggle).GetField(name, Flags);
        Assert.IsNotNull(field, $"field {name} not found");
        return (T)field.GetValue(target);
    }

    static void InvokeOnValidate(NetworkOwnershipToggle toggle)
    {
        var method = typeof(NetworkOwnershipToggle).GetMethod("OnValidate", Flags);
        Assert.IsNotNull(method, "OnValidate not found");

        try
        {
            method.Invoke(toggle, null);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            throw e.InnerException;
        }
    }
}

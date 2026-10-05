using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Utils;
using UnityEngine;

// These fixtures inspect shared static state.
public class AsyncDestroyBudgetTests
{
    private static readonly Type DestroyerType = typeof(UnityProxy).Assembly.GetType("PurrNet.AsyncDestroyer");
    private static readonly FieldInfo PendingField = DestroyerType.GetField(
        "_pending", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo SubscribedField = DestroyerType.GetField(
        "_subscribed", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo TickMethod = DestroyerType.GetMethod(
        "Tick", BindingFlags.NonPublic | BindingFlags.Static);
    private readonly List<GameObject> _objects = new List<GameObject>();
    private IList _pending;
    private bool _ownsQueue;

    [SetUp]
    public void SetUp()
    {
        Assert.That(ApplicationContext.isQuitting, Is.False);
        Assert.IsNotNull(PendingField);
        Assert.IsNotNull(SubscribedField);
        Assert.IsNotNull(TickMethod);
        _pending = (IList)PendingField.GetValue(null);
        Assert.That(_pending.Count, Is.Zero, "Do not replace an active destruction queue.");
        Assert.That((bool)SubscribedField.GetValue(null), Is.False);
        _ownsQueue = true;
    }

    [TearDown]
    public void TearDown()
    {
        if (_ownsQueue)
            _pending.Clear();
        _ownsQueue = false;
        foreach (var obj in _objects)
        {
            if (obj)
                UnityProxy.DestroyImmediateDirectly(obj);
        }
        _objects.Clear();
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void ExhaustedBudgetCompletesOnlyOneSingleNodeRootPerTick(float budgetMs)
    {
        var first = CreateShell("First");
        var second = CreateShell("Second");
        var third = CreateShell("Third");
        Queue(first, budgetMs);
        Queue(second, budgetMs);
        Queue(third, budgetMs);

        Tick();

        Assert.That((bool)first, Is.False, "At least one destruction attempt must make progress.");
        Assert.That((bool)second, Is.True, "The next root must wait after the budget is exhausted.");
        Assert.That((bool)third, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(2));

        Tick();
        Assert.That((bool)second, Is.False);
        Assert.That((bool)third, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(1));

        Tick();
        Assert.That((bool)third, Is.False);
        Assert.That(_pending.Count, Is.Zero);
    }

    [Test]
    public void CompletedRootsMayShareTickWhileBudgetRemains()
    {
        var first = CreateShell("First");
        var second = CreateShell("Second");
        // A generous budget avoids a timing-sensitive assertion on this two-object fixture.
        Queue(first, 60000f);
        Queue(second, 60000f);
        Tick();
        Assert.That((bool)first, Is.False);
        Assert.That((bool)second, Is.False);
        Assert.That(_pending.Count, Is.Zero);
    }

    [Test]
    public void LaterLargerBudgetDoesNotOverrideExhaustedCompletedRoot()
    {
        var first = CreateShell("First");
        var second = CreateShell("Second");
        Queue(first, 0f);
        Queue(second, 60000f);
        Tick();
        Assert.That((bool)first, Is.False);
        Assert.That((bool)second, Is.True);
        Tick();
        Assert.That((bool)second, Is.False);
        Assert.That(_pending.Count, Is.Zero);
    }

    [Test]
    public void DeadEntriesDoNotUseTheFirstDestructionAttempt()
    {
        var dead = CreateShell("AlreadyDestroyed");
        var first = CreateShell("FirstLive");
        var second = CreateShell("SecondLive");
        Queue(dead, 0f);
        Queue(first, 0f);
        Queue(second, 0f);
        UnityProxy.DestroyImmediateDirectly(dead);
        Tick();
        Assert.That((bool)first, Is.False);
        Assert.That((bool)second, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(1));
    }

    [Test]
    public void SameFrameEntryStillWaitsEvenWithNonPositiveBudget()
    {
        var root = CreateShell("CreatedThisFrame");
        Queue(root, 0f, Time.frameCount);
        Tick();
        Assert.That((bool)root, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(1));
    }

    [Test]
    public void ZeroBudgetPreservesLeafFirstRootLastOrderAcrossTicks()
    {
        var root = CreateShell("Root");
        var child = CreateShell("Child");
        var leaf = CreateShell("Leaf");
        var nextRoot = CreateShell("NextRoot");
        child.transform.SetParent(root.transform, false);
        leaf.transform.SetParent(child.transform, false);
        Queue(root, 0f);
        Queue(nextRoot, 0f);

        Tick();
        Assert.That((bool)leaf, Is.False);
        Assert.That((bool)child, Is.True);
        Assert.That((bool)root, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(2));

        Tick();
        Assert.That((bool)child, Is.False);
        Assert.That((bool)root, Is.True);

        Tick();
        Assert.That((bool)root, Is.False);
        Assert.That((bool)nextRoot, Is.True);
        Assert.That(_pending.Count, Is.EqualTo(1));
    }

    private GameObject CreateShell(string name)
    {
        var obj = new GameObject(name);
        obj.SetActive(false);
        _objects.Add(obj);
        return obj;
    }

    private void Queue(GameObject obj, float budgetMs, int? queuedFrame = null)
    {
        // Enqueue deliberately falls back to ordinary destruction in EditMode. Inject the
        // already-prepared inactive queue entry so these tests isolate Tick's budget logic.
        var entryType = PendingField.FieldType.GetGenericArguments()[0];
        var entry = Activator.CreateInstance(entryType);
        entryType.GetField("gameObject").SetValue(entry, obj);
        entryType.GetField("msPerFrame").SetValue(entry, budgetMs);
        entryType.GetField("frame").SetValue(entry, queuedFrame ?? Time.frameCount - 1);
        _pending.Add(entry);
    }

    private static void Tick() => TickMethod.Invoke(null, null);
}

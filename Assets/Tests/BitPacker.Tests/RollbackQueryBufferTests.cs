#if UNITY_EDITOR
using NUnit.Framework;
using PurrNet.Modules;
using UnityEngine;

public sealed class RollbackQueryBufferTests
{
#if UNITY_PHYSICS_3D
    [Test]
    public void FirstAndRepeated3DQueriesFindTheSameCollider()
    {
        var target = new GameObject("Rollback query target");
        try
        {
            target.layer = 31;
            target.transform.position = new Vector3(1374, 751, 916);
            var collider = target.AddComponent<BoxCollider>();
            var module = new RollbackModule(null, target.scene);
            var ray = new Ray(target.transform.position - Vector3.forward * 5, Vector3.forward);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(module.SphereCast(0, ray, 0.1f, out var hit, 10, 1 << 31), Is.True);
                Assert.That(hit.collider, Is.SameAs(collider));
                Assert.That(module.CheckSphere(0, target.transform.position, 0.2f, 1 << 31), Is.True);
                Assert.That(module.CheckSphere(0, target.transform.position + Vector3.right * 20, 0.2f, 1 << 31), Is.False);
            }
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }
#endif

#if UNITY_PHYSICS_3D
    [Test]
    public void CaptureRecordsTheGivenTicks()
    {
        var target = new GameObject("Rollback capture target");
        try
        {
            var collider = target.AddComponent<BoxCollider>();
            var module = new RollbackModule(new TickManager(64, null, null, true), target.scene);
            module.Register(collider, 5f);

            target.transform.position = new Vector3(1f, 0f, 0f);
            Assert.That(module.Capture(10, collider), Is.True);
            target.transform.position = new Vector3(3f, 0f, 0f);
            Assert.That(module.Capture(12, collider), Is.True);
            target.transform.position = new Vector3(9f, 0f, 0f);

            Assert.That(module.TryGetColliderState(10, collider, out var exact), Is.True);
            Assert.That(exact.position.x, Is.EqualTo(1f).Within(1e-5));
            Assert.That(module.TryGetColliderState(11, collider, out var middle), Is.True);
            Assert.That(middle.position.x, Is.EqualTo(2f).Within(1e-5));

            var untracked = target.AddComponent<SphereCollider>();
            Assert.That(module.Capture(13, untracked), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void SamplingInterpolatesAcrossAOneSidedGap()
    {
        var target = new GameObject("Rollback gap target");
        try
        {
            var collider = target.AddComponent<BoxCollider>();
            var module = new RollbackModule(new TickManager(64, null, null, true), target.scene);
            module.Register(collider, 5f);

            target.transform.position = Vector3.zero;
            module.Capture(20, collider);
            target.transform.position = new Vector3(4f, 0f, 0f);
            module.Capture(22, collider);

            // Tick 21 is missing: 20.5 has only its floor, 21.5 only its ceiling. Neither may snap
            // to the one neighbour that exists.
            Assert.That(module.TryGetColliderState(20.5, collider, out var early), Is.True);
            Assert.That(early.position.x, Is.EqualTo(1f).Within(1e-5));
            Assert.That(module.TryGetColliderState(21.5, collider, out var late), Is.True);
            Assert.That(late.position.x, Is.EqualTo(3f).Within(1e-5));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void CaptureWritesTheRightHistoryAfterOthersLeave()
    {
        var first = new GameObject("Rollback capture first");
        var second = new GameObject("Rollback capture second");
        var third = new GameObject("Rollback capture third");
        try
        {
            var a = first.AddComponent<BoxCollider>();
            var b = second.AddComponent<BoxCollider>();
            var c = third.AddComponent<BoxCollider>();
            var module = new RollbackModule(new TickManager(64, null, null, true), first.scene);
            module.Register(a, 5f);
            module.Register(b, 5f);
            module.Register(c, 5f);

            // Unregistering and pruning a destroyed collider both shift the others' places.
            module.Unregister(a);
            Object.DestroyImmediate(second);
            module.OnPostTick();

            third.transform.position = new Vector3(7f, 0f, 0f);
            Assert.That(module.Capture(30, c), Is.True);
            Assert.That(module.TryGetColliderState(30, c, out var state), Is.True);
            Assert.That(state.position.x, Is.EqualTo(7f).Within(1e-5));
            Assert.That(module.Capture(30, a), Is.False);

            // Registered again, it captures into its own new history.
            first.transform.position = new Vector3(2f, 0f, 0f);
            module.Register(a, 5f);
            Assert.That(module.Capture(31, a), Is.True);
            Assert.That(module.TryGetColliderState(31, a, out var again), Is.True);
            Assert.That(again.position.x, Is.EqualTo(2f).Within(1e-5));
            Assert.That(module.TryGetColliderState(31, c, out _), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(first);
            if (second)
                Object.DestroyImmediate(second);
            Object.DestroyImmediate(third);
        }
    }

    [Test]
    public void CaptureCostGrowsLinearlyWithTrackedColliders()
    {
        // Capturing every tracked collider once (as a caller recording its own poses each tick does)
        // must cost about 4x for 4x the colliders. Looking each collider up by a list search made it
        // grow with the square: about 16x.
        double small = BestCaptureAllSeconds(500);
        double large = BestCaptureAllSeconds(2000);
        Assert.That(large / small, Is.LessThan(8d),
            $"Capturing 500 colliders took {small * 1000:0.00} ms, 2000 took {large * 1000:0.00} ms.");
    }

    static double BestCaptureAllSeconds(int count)
    {
        var target = new GameObject("Rollback capture cost");
        try
        {
            var module = new RollbackModule(new TickManager(64, null, null, true), target.scene);
            var colliders = new Collider[count];
            for (int i = 0; i < count; i++)
            {
                colliders[i] = target.AddComponent<BoxCollider>();
                module.Register(colliders[i], 1f);
            }

            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    module.Capture((ulong)run, colliders[i]);
                watch.Stop();
                best = System.Math.Min(best, watch.Elapsed.TotalSeconds);
            }
            return best;
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }
#endif

#if UNITY_PHYSICS_2D
    [Test]
    public void FirstAndRepeated2DQueriesFindTheSameCollider()
    {
        var target = new GameObject("Rollback query target");
        try
        {
            target.layer = 31;
            target.transform.position = new Vector3(1374, 751, 0);
            var collider = target.AddComponent<BoxCollider2D>();
            var module = new RollbackModule(null, target.scene);
            var position = (Vector2)target.transform.position;
            var ray = new Ray2D(position - Vector2.right * 5, Vector2.right);
            var filter = new ContactFilter2D().NoFilter();
            filter.SetLayerMask(1 << 31);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(module.Raycast(0, ray, out var hit, 10, filter), Is.True);
                Assert.That(hit.collider, Is.SameAs(collider));
                Assert.That(module.CheckCircle(0, position, 0.2f, filter), Is.True);
                Assert.That(module.CheckCircle(0, position + Vector2.right * 20, 0.2f, filter), Is.False);
            }
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void Capture2DWritesTheRightHistoryAfterOthersLeave()
    {
        var first = new GameObject("Rollback capture first");
        var second = new GameObject("Rollback capture second");
        var third = new GameObject("Rollback capture third");
        try
        {
            var a = first.AddComponent<BoxCollider2D>();
            var b = second.AddComponent<BoxCollider2D>();
            var c = third.AddComponent<BoxCollider2D>();
            var module = new RollbackModule(new TickManager(64, null, null, true), first.scene);
            module.Register(a, 5f);
            module.Register(b, 5f);
            module.Register(c, 5f);

            module.Unregister(a);
            Object.DestroyImmediate(second);
            module.OnPostTick();

            third.transform.position = new Vector3(7f, 0f, 0f);
            Assert.That(module.Capture(30, c), Is.True);
            Assert.That(module.TryGetColliderState(30, c, out var state), Is.True);
            Assert.That(state.position.x, Is.EqualTo(7f).Within(1e-5));
            Assert.That(module.Capture(30, a), Is.False);

            first.transform.position = new Vector3(2f, 0f, 0f);
            module.Register(a, 5f);
            Assert.That(module.Capture(31, a), Is.True);
            Assert.That(module.TryGetColliderState(31, a, out var again), Is.True);
            Assert.That(again.position.x, Is.EqualTo(2f).Within(1e-5));
        }
        finally
        {
            Object.DestroyImmediate(first);
            if (second)
                Object.DestroyImmediate(second);
            Object.DestroyImmediate(third);
        }
    }
#endif
}
#endif

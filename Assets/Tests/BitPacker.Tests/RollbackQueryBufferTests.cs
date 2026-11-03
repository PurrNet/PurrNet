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
#endif
}
#endif

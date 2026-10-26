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

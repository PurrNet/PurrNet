#if UNITY_PHYSICS_3D
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NetworkTransformCharacterControllerTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();
    private Scene _scene;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i]) Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        if (_scene.IsValid() && _scene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(_scene);
    }

    [UnityTest]
    public IEnumerator PatchedControllerKeepsOneContactWhileMovingAndAtRest()
    {
        yield return CheckObserver(patch: true, colliderOnly: false);
    }

    [UnityTest]
    public IEnumerator UnpatchedControllerAppliesReceivedPosition()
    {
        yield return CheckObserver(patch: false, colliderOnly: false);
    }

    [UnityTest]
    public IEnumerator ColliderOnlyObserverKeepsContactsAndReceivedPosition()
    {
        yield return CheckObserver(patch: false, colliderOnly: true);
    }

    [UnityTest]
    public IEnumerator PatchedControllerKeepsAuthoritativePoseWhenLocalGeometryBlocksMove()
    {
        _scene = SceneManager.CreateScene("CCBlocked-" + Guid.NewGuid().ToString("N"),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var go = Create("Observer");
        go.transform.position = new Vector3(-2f, 0f, 0f);
        var controller = go.AddComponent<CharacterController>();
        controller.center = Vector3.up;
        controller.minMoveDistance = 0.5f;
        var wall = Create("Local wall");
        wall.transform.position = Vector3.up;
        wall.AddComponent<BoxCollider>().size = new Vector3(1f, 4f, 4f);
        var nt = go.AddComponent<NetworkTransform>();
        typeof(NetworkTransform).GetMethod("Awake", Private).Invoke(nt, null);
        Set(nt, "_characterControllerPatch", true);
        Set(nt, "_syncPosition", SyncMode.World);
        Set(nt, "_syncRotation", SyncMode.No);
        Set(nt, "_syncScale", false);
        var positions = new Interpolated<Vector3WithParent>(Vector3WithParent.NoLerp, 0.02f,
            new Vector3WithParent(null, false, go.transform.position));
        Set(nt, "_position", positions);
        var apply = typeof(NetworkTransform).GetMethod("ApplyLerpedPosition", Private);
        Physics.SyncTransforms();

        foreach (var target in new[] { new Vector3(2f, 0f, 0f), new Vector3(2.01f, 0f, 0f) })
        {
            positions.Teleport(new Vector3WithParent(null, false, target));
            apply.Invoke(nt, null);
            _scene.GetPhysicsScene().Simulate(0.02f);
            Assert.That(Vector3.Distance(go.transform.position, target), Is.LessThan(0.0001f));
            Assert.That(controller.enabled, Is.True);
            Assert.That(controller.minMoveDistance, Is.EqualTo(0.5f));
            Assert.That(controller.detectCollisions, Is.True);
        }
        yield break;
    }

    private IEnumerator CheckObserver(bool patch, bool colliderOnly)
    {
        _scene = SceneManager.CreateScene("CCObserver-" + Guid.NewGuid().ToString("N"),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = _scene.GetPhysicsScene();
        var go = Create("Observer");
        go.transform.position = new Vector3(-4f, 0f, 0f);
        var controller = go.AddComponent<CharacterController>();
        controller.center = Vector3.up;
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.minMoveDistance = 0.1f;
        if (colliderOnly)
        {
            controller.enabled = false;
            var collisionChild = Create("Observer collider");
            collisionChild.transform.SetParent(go.transform, false);
            var capsule = collisionChild.AddComponent<CapsuleCollider>();
            capsule.center = controller.center;
            capsule.height = controller.height;
            capsule.radius = controller.radius;
        }

        var triggerObject = Create("Trigger");
        triggerObject.transform.position = Vector3.up;
        var trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.size = new Vector3(6f, 4f, 4f);
        trigger.isTrigger = true;
        var body = triggerObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        var probe = triggerObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var nt = go.AddComponent<NetworkTransform>();
        typeof(NetworkTransform).GetMethod("Awake", Private).Invoke(nt, null);
        Set(nt, "_characterControllerPatch", patch);
        Set(nt, "_syncPosition", SyncMode.World);
        Set(nt, "_syncRotation", SyncMode.No);
        Set(nt, "_syncScale", false);
        var positions = new Interpolated<Vector3WithParent>(Vector3WithParent.NoLerp, 0.02f,
            new Vector3WithParent(null, false, go.transform.position));
        Set(nt, "_position", positions);
        var apply = typeof(NetworkTransform).GetMethod("ApplyLerpedPosition", Private);

        Physics.SyncTransforms();
        physics.Simulate(0.02f);
        for (int step = 0; step < 20; step++)
        {
            // Motion smaller than minMoveDistance must still follow the received pose exactly.
            var target = new Vector3(-2f + Mathf.Min(step, 10) * 0.025f, 0f, 0f);
            positions.Teleport(new Vector3WithParent(null, false, target));
            apply.Invoke(nt, null);
            Physics.SyncTransforms();
            physics.Simulate(0.02f);
            Assert.That(Vector3.Distance(go.transform.position, target), Is.LessThan(0.0001f));
            Assert.That(controller.enabled, Is.EqualTo(!colliderOnly));
        }

        Assert.That(probe.triggerEnterCount, Is.EqualTo(1), "Observer pose updates rebuilt an existing trigger contact.");
        Assert.That(probe.triggerStayCount, Is.GreaterThan(0));
        positions.Teleport(new Vector3WithParent(null, false, new Vector3(5f, 0f, 0f)));
        apply.Invoke(nt, null);
        Physics.SyncTransforms();
        physics.Simulate(0.02f);
        physics.Simulate(0.02f);
        Assert.That(probe.triggerExitCount, Is.EqualTo(1));
        yield break;
    }

    private GameObject Create(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, _scene);
        _objects.Add(go);
        return go;
    }

    private static void Set(NetworkTransform nt, string name, object value)
        => typeof(NetworkTransform).GetField(name, Private).SetValue(nt, value);
}
#endif

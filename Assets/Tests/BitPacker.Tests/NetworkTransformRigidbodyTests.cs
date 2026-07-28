#if UNITY_PHYSICS_3D
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NetworkTransformRigidbodyTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly List<Scene> _scenes = new List<Scene>();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i])
                Object.DestroyImmediate(_objects[i]);
        }
        _objects.Clear();

        for (int i = _scenes.Count - 1; i >= 0; i--)
        {
            var scene = _scenes[i];
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null)
                yield return unload;
        }
        _scenes.Clear();
    }

    [UnityTest]
    public IEnumerator DynamicObserverStabilizationCancelsMotionWithoutChangingConfiguration()
    {
        var scene = CreatePhysicsScene(
            $"NetworkTransformStabilization-{Guid.NewGuid():N}");
        var physicsScene = scene.GetPhysicsScene();

        var bodyObject = CreateObject("DynamicObserver");
        SceneManager.MoveGameObjectToScene(bodyObject, scene);
        var body = bodyObject.AddComponent<Rigidbody>();
        bodyObject.AddComponent<BoxCollider>();

        body.mass = 2.75f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.None;
        body.detectCollisions = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        body.position = new Vector3(-4f, 2f, 1f);
        body.rotation = Quaternion.Euler(5f, 15f, 25f);
        NetworkRigidbodyPhysics.SetLinearVelocity(body, new Vector3(3f, -2f, 1f));
        NetworkRigidbodyPhysics.SetAngularVelocity(body, new Vector3(2f, 3f, 4f));
        body.AddForce(new Vector3(8f, 6f, -3f), ForceMode.VelocityChange);
        body.AddTorque(new Vector3(-4f, 5f, 7f), ForceMode.Impulse);

        var targetPosition = new Vector3(3f, 4f, -2f);
        var targetRotation = Quaternion.Euler(20f, 35f, 10f);
        bool wasKinematic = body.isKinematic;
        bool usedGravity = body.useGravity;
        var interpolation = body.interpolation;
        var constraints = body.constraints;
        bool detectedCollisions = body.detectCollisions;
        var collisionDetection = body.collisionDetectionMode;

        NetworkTransform.ApplyObserverRigidbodyPose(
            body, true, targetPosition, true, targetRotation);
        physicsScene.Simulate(0.02f);

        Assert.That((body.position - targetPosition).sqrMagnitude, Is.LessThan(0.000001f));
        Assert.That(Quaternion.Angle(body.rotation, targetRotation), Is.LessThan(0.001f));
        Assert.That(
            NetworkRigidbodyPhysics.GetLinearVelocity(body).sqrMagnitude,
            Is.LessThan(0.000001f));
        Assert.That(body.angularVelocity.sqrMagnitude, Is.LessThan(0.000001f));
        Assert.That(body.isKinematic, Is.EqualTo(wasKinematic));
        Assert.That(body.useGravity, Is.EqualTo(usedGravity));
        Assert.That(body.interpolation, Is.EqualTo(interpolation));
        Assert.That(body.constraints, Is.EqualTo(constraints));
        Assert.That(body.detectCollisions, Is.EqualTo(detectedCollisions));
        Assert.That(body.collisionDetectionMode, Is.EqualTo(collisionDetection));
        yield break;
    }

    [UnityTest]
    public IEnumerator RepeatedPoseWritesDoNotRepeatCollisionEnter()
    {
        var scene = CreatePhysicsScene(
            $"NetworkTransformCollisionPoseWrites-{Guid.NewGuid():N}");
        var physicsScene = scene.GetPhysicsScene();

        var observerObject = CreateObject("ObserverPoseBody");
        SceneManager.MoveGameObjectToScene(observerObject, scene);
        observerObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var observerBody = observerObject.AddComponent<Rigidbody>();
        observerBody.useGravity = false;
        observerObject.AddComponent<BoxCollider>();
        var probe = observerObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var wall = CreateObject("StaticWall");
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.transform.position = Vector3.zero;
        wall.AddComponent<BoxCollider>();

        for (int step = 0; step < 20; step++)
        {
            var target = new Vector3(-0.9f + step * 0.01f, 0f, 0f);
            ApplyObserverPosition(observerBody, observerObject.transform, target);
            physicsScene.Simulate(0.02f);
        }

        Assert.That(probe.collisionEnterCount, Is.EqualTo(1));

        ApplyObserverPosition(
            observerBody, observerObject.transform, new Vector3(2f, 0f, 0f));
        physicsScene.Simulate(0.02f);
        physicsScene.Simulate(0.02f);

        Assert.That(probe.collisionExitCount, Is.EqualTo(1));
        yield break;
    }

    [UnityTest]
    public IEnumerator RepeatedPoseWritesDoNotRepeatTriggerEnter()
    {
        var scene = CreatePhysicsScene(
            $"NetworkTransformTriggerPoseWrites-{Guid.NewGuid():N}");
        var physicsScene = scene.GetPhysicsScene();

        var observerObject = CreateObject("ObserverTriggerBody");
        SceneManager.MoveGameObjectToScene(observerObject, scene);
        observerObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var observerBody = observerObject.AddComponent<Rigidbody>();
        observerBody.useGravity = false;
        observerObject.AddComponent<BoxCollider>();
        var probe = observerObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var triggerObject = CreateObject("StaticTrigger");
        SceneManager.MoveGameObjectToScene(triggerObject, scene);
        triggerObject.transform.position = Vector3.zero;
        var trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;

        for (int step = 0; step < 20; step++)
        {
            var target = new Vector3(-0.9f + step * 0.01f, 0f, 0f);
            ApplyObserverPosition(observerBody, observerObject.transform, target);
            physicsScene.Simulate(0.02f);
        }

        Assert.That(probe.triggerEnterCount, Is.EqualTo(1));
        Assert.That(probe.triggerStayCount, Is.GreaterThan(0));

        ApplyObserverPosition(
            observerBody, observerObject.transform, new Vector3(2f, 0f, 0f));
        physicsScene.Simulate(0.02f);
        physicsScene.Simulate(0.02f);

        Assert.That(probe.triggerExitCount, Is.EqualTo(1));
        yield break;
    }

    private static void ApplyObserverPosition(
        Rigidbody body, Transform bodyTransform, Vector3 targetPosition)
    {
        NetworkTransform.ApplyObserverRigidbodyPose(
            body, true, targetPosition, false, Quaternion.identity);
        bodyTransform.position = targetPosition;
    }

    private Scene CreatePhysicsScene(string name)
    {
        var scene = SceneManager.CreateScene(
            name,
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        _scenes.Add(scene);
        return scene;
    }

    private GameObject CreateObject(string name)
    {
        var value = new GameObject(name);
        _objects.Add(value);
        return value;
    }
}

public sealed class NetworkTransformPhysicsEventProbe : MonoBehaviour
{
    public int collisionEnterCount { get; private set; }
    public int collisionExitCount { get; private set; }
    public int triggerEnterCount { get; private set; }
    public int triggerStayCount { get; private set; }
    public int triggerExitCount { get; private set; }

    private void OnCollisionEnter(Collision _)
    {
        collisionEnterCount++;
    }

    private void OnCollisionExit(Collision _)
    {
        collisionExitCount++;
    }

    private void OnTriggerEnter(Collider _)
    {
        triggerEnterCount++;
    }

    private void OnTriggerStay(Collider _)
    {
        triggerStayCount++;
    }

    private void OnTriggerExit(Collider _)
    {
        triggerExitCount++;
    }
}
#endif

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

public class NetworkTransformContactManagerTests
{
    private readonly List<IDisposable> _registrations = new List<IDisposable>();
    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _registrations.Count - 1; i >= 0; i--)
            _registrations[i].Dispose();
        _registrations.Clear();

        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i])
                Object.DestroyImmediate(_objects[i]);
        }
        _objects.Clear();
    }

    [Test]
    public void ApplyDominanceOnlyChangesRequestedSides()
    {
        var massProperties = default(ModifiableMassProperties);
        massProperties.inverseMassScale = 1f;
        massProperties.inverseInertiaScale = 2f;
        massProperties.otherInverseMassScale = 3f;
        massProperties.otherInverseInertiaScale = 4f;

        NetworkTransformContactManager.ApplyDominance(
            ref massProperties,
            NetworkTransformContactDominance.Linear,
            NetworkTransformContactDominance.Angular);

        Assert.That(massProperties.inverseMassScale, Is.Zero);
        Assert.That(massProperties.inverseInertiaScale, Is.EqualTo(2f));
        Assert.That(massProperties.otherInverseMassScale, Is.EqualTo(3f));
        Assert.That(massProperties.otherInverseInertiaScale, Is.Zero);
    }

    [Test]
    public void TwoFullyDominantBodiesZeroBothSides()
    {
        var massProperties = default(ModifiableMassProperties);
        massProperties.inverseMassScale = 1f;
        massProperties.inverseInertiaScale = 1f;
        massProperties.otherInverseMassScale = 1f;
        massProperties.otherInverseInertiaScale = 1f;

        NetworkTransformContactManager.ApplyDominance(
            ref massProperties,
            NetworkTransformContactDominance.Full,
            NetworkTransformContactDominance.Full);

        Assert.That(massProperties.inverseMassScale, Is.Zero);
        Assert.That(massProperties.inverseInertiaScale, Is.Zero);
        Assert.That(massProperties.otherInverseMassScale, Is.Zero);
        Assert.That(massProperties.otherInverseInertiaScale, Is.Zero);
    }

    [Test]
    public void RegistrationCombinesDominanceAndLeavesColliderEnabled()
    {
        var bodyObject = CreateObject("NetworkDominantBody");
        var body = bodyObject.AddComponent<Rigidbody>();
        var collider = bodyObject.AddComponent<BoxCollider>();

        var linear = Register(body, NetworkTransformContactDominance.Linear);
        Assert.That(collider.hasModifiableContacts, Is.True);
        Assert.That(NetworkTransformContactManager.GetDominance(linear.bodyId),
            Is.EqualTo(NetworkTransformContactDominance.Linear));

        var angular = Register(body, NetworkTransformContactDominance.Angular);
        Assert.That(NetworkTransformContactManager.GetDominance(linear.bodyId),
            Is.EqualTo(NetworkTransformContactDominance.Full));

        linear.Dispose();
        Assert.That(collider.hasModifiableContacts, Is.True);
        Assert.That(NetworkTransformContactManager.GetDominance(angular.bodyId),
            Is.EqualTo(NetworkTransformContactDominance.Angular));

        angular.Dispose();
        Assert.That(collider.hasModifiableContacts, Is.True);
        Assert.That(NetworkTransformContactManager.GetDominance(angular.bodyId),
            Is.EqualTo(NetworkTransformContactDominance.None));
    }

    [Test]
    public void RegistrationHandlesCompoundCollidersButNotNestedBodies()
    {
        var root = CreateObject("CompoundRoot");
        var rootBody = root.AddComponent<Rigidbody>();
        var rootCollider = root.AddComponent<BoxCollider>();

        var compoundChild = CreateObject("CompoundChild");
        compoundChild.transform.SetParent(root.transform);
        var compoundCollider = compoundChild.AddComponent<SphereCollider>();

        var nestedBodyObject = CreateObject("NestedBody");
        nestedBodyObject.transform.SetParent(root.transform);
        nestedBodyObject.AddComponent<Rigidbody>();
        var nestedCollider = nestedBodyObject.AddComponent<CapsuleCollider>();

        var registration = Register(rootBody, NetworkTransformContactDominance.Full);

        Assert.That(rootCollider.hasModifiableContacts, Is.True);
        Assert.That(compoundCollider.hasModifiableContacts, Is.True);
        Assert.That(nestedCollider.hasModifiableContacts, Is.False);

        registration.Dispose();

        Assert.That(rootCollider.hasModifiableContacts, Is.True);
        Assert.That(compoundCollider.hasModifiableContacts, Is.True);
        Assert.That(nestedCollider.hasModifiableContacts, Is.False);
    }

    [Test]
    public void RegistrationPreservesAnExistingModifiableContactsSetting()
    {
        var bodyObject = CreateObject("ExistingContactModification");
        var body = bodyObject.AddComponent<Rigidbody>();
        var collider = bodyObject.AddComponent<BoxCollider>();
        collider.hasModifiableContacts = true;

        var registration = Register(body, NetworkTransformContactDominance.Full);
        registration.Dispose();

        Assert.That(collider.hasModifiableContacts, Is.True);
    }

    [Test]
    public void RegistrationDoesNotRestoreColliderFlagAfterExternalWrite()
    {
        var bodyObject = CreateObject("ExternalContactModification");
        var body = bodyObject.AddComponent<Rigidbody>();
        var collider = bodyObject.AddComponent<BoxCollider>();

        var registration = Register(body, NetworkTransformContactDominance.Full);
        collider.hasModifiableContacts = false;
        registration.Dispose();

        Assert.That(collider.hasModifiableContacts, Is.False);
    }

    [UnityTest]
    public IEnumerator RegisteredBodyKeepsVelocityWhileOtherBodyReceivesContactResponse()
    {
        var scene = SceneManager.CreateScene(
            $"NetworkDominantPhysics-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physicsScene = scene.GetPhysicsScene();

        var dominantObject = CreateObject("DominantBody");
        SceneManager.MoveGameObjectToScene(dominantObject, scene);
        dominantObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var dominantBody = dominantObject.AddComponent<Rigidbody>();
        dominantBody.useGravity = false;
        dominantObject.AddComponent<BoxCollider>();

        var otherObject = CreateObject("OtherBody");
        SceneManager.MoveGameObjectToScene(otherObject, scene);
        otherObject.transform.position = Vector3.zero;
        var otherBody = otherObject.AddComponent<Rigidbody>();
        otherBody.useGravity = false;
        otherObject.AddComponent<BoxCollider>();

        var registration = Register(dominantBody, NetworkTransformContactDominance.Full);
        NetworkRigidbodyPhysics.SetLinearVelocity(dominantBody, Vector3.right * 4f);
        NetworkRigidbodyPhysics.SetLinearVelocity(otherBody, Vector3.zero);

        Assert.That(
            physicsScene.Raycast(new Vector3(-3f, 0f, 0f), Vector3.right, out var hit, 5f),
            Is.True);
        Assert.That(hit.rigidbody, Is.EqualTo(dominantBody));

        for (int step = 0; step < 30; step++)
            physicsScene.Simulate(0.02f);

        Assert.That(
            NetworkRigidbodyPhysics.GetLinearVelocity(dominantBody).x,
            Is.EqualTo(4f).Within(0.1f));
        Assert.That(
            NetworkRigidbodyPhysics.GetLinearVelocity(otherBody).x,
            Is.GreaterThan(0.5f));

        registration.Dispose();
        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;
    }

    [UnityTest]
    public IEnumerator TwoDominantBodiesRetainCallbacksWithoutResponding()
    {
        var scene = SceneManager.CreateScene(
            $"NetworkDominantPair-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physicsScene = scene.GetPhysicsScene();

        var movingObject = CreateObject("MovingDominantBody");
        SceneManager.MoveGameObjectToScene(movingObject, scene);
        movingObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var movingBody = movingObject.AddComponent<Rigidbody>();
        movingBody.useGravity = false;
        movingObject.AddComponent<BoxCollider>();
        var movingProbe = movingObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var stationaryObject = CreateObject("StationaryDominantBody");
        SceneManager.MoveGameObjectToScene(stationaryObject, scene);
        stationaryObject.transform.position = Vector3.zero;
        var stationaryBody = stationaryObject.AddComponent<Rigidbody>();
        stationaryBody.useGravity = false;
        stationaryObject.AddComponent<BoxCollider>();
        var stationaryProbe = stationaryObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var movingRegistration = Register(movingBody, NetworkTransformContactDominance.Full);
        var stationaryRegistration = Register(stationaryBody, NetworkTransformContactDominance.Full);
        NetworkRigidbodyPhysics.SetLinearVelocity(movingBody, Vector3.right * 4f);

        for (int step = 0; step < 70; step++)
            physicsScene.Simulate(0.02f);

        Assert.That(
            NetworkRigidbodyPhysics.GetLinearVelocity(movingBody).x,
            Is.EqualTo(4f).Within(0.1f));
        Assert.That(
            NetworkRigidbodyPhysics.GetLinearVelocity(stationaryBody).sqrMagnitude,
            Is.LessThan(0.01f));
        Assert.That(movingProbe.collisionEnterCount, Is.EqualTo(1));
        Assert.That(movingProbe.collisionStayCount, Is.GreaterThan(0));
        Assert.That(movingProbe.collisionExitCount, Is.EqualTo(1));
        Assert.That(stationaryProbe.collisionEnterCount, Is.EqualTo(1));
        Assert.That(stationaryProbe.collisionStayCount, Is.GreaterThan(0));
        Assert.That(stationaryProbe.collisionExitCount, Is.EqualTo(1));

        movingRegistration.Dispose();
        stationaryRegistration.Dispose();
        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;
    }

    [UnityTest]
    public IEnumerator RepeatedPoseWritesKeepCollisionCallbacksContinuous()
    {
        var scene = SceneManager.CreateScene(
            $"NetworkDominantPoseWrites-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physicsScene = scene.GetPhysicsScene();

        var dominantObject = CreateObject("DominantPoseBody");
        SceneManager.MoveGameObjectToScene(dominantObject, scene);
        dominantObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var dominantBody = dominantObject.AddComponent<Rigidbody>();
        dominantBody.useGravity = false;
        dominantObject.AddComponent<BoxCollider>();
        var probe = dominantObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var wall = CreateObject("StaticWall");
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.transform.position = Vector3.zero;
        wall.AddComponent<BoxCollider>();

        var registration = Register(dominantBody, NetworkTransformContactDominance.Full);

        for (int step = 0; step < 20; step++)
        {
            var target = new Vector3(-0.9f + step * 0.01f, 0f, 0f);
            dominantBody.position = target;
            dominantObject.transform.position = target;
            physicsScene.Simulate(0.02f);
        }

        Assert.That(probe.collisionEnterCount, Is.EqualTo(1));
        Assert.That(probe.collisionStayCount, Is.GreaterThan(0));

        dominantBody.position = new Vector3(2f, 0f, 0f);
        dominantObject.transform.position = dominantBody.position;
        physicsScene.Simulate(0.02f);
        physicsScene.Simulate(0.02f);

        Assert.That(probe.collisionExitCount, Is.EqualTo(1));

        registration.Dispose();
        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;
    }

    [UnityTest]
    public IEnumerator RepeatedPoseWritesKeepTriggerCallbacksContinuous()
    {
        var scene = SceneManager.CreateScene(
            $"NetworkDominantTriggerPoseWrites-{Guid.NewGuid():N}",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physicsScene = scene.GetPhysicsScene();

        var dominantObject = CreateObject("DominantTriggerBody");
        SceneManager.MoveGameObjectToScene(dominantObject, scene);
        dominantObject.transform.position = new Vector3(-1.5f, 0f, 0f);
        var dominantBody = dominantObject.AddComponent<Rigidbody>();
        dominantBody.useGravity = false;
        dominantObject.AddComponent<BoxCollider>();
        var probe = dominantObject.AddComponent<NetworkTransformPhysicsEventProbe>();

        var triggerObject = CreateObject("StaticTrigger");
        SceneManager.MoveGameObjectToScene(triggerObject, scene);
        triggerObject.transform.position = Vector3.zero;
        var trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;

        var registration = Register(dominantBody, NetworkTransformContactDominance.Full);

        for (int step = 0; step < 20; step++)
        {
            var target = new Vector3(-0.9f + step * 0.01f, 0f, 0f);
            dominantBody.position = target;
            dominantObject.transform.position = target;
            physicsScene.Simulate(0.02f);
        }

        Assert.That(probe.triggerEnterCount, Is.EqualTo(1));
        Assert.That(probe.triggerStayCount, Is.GreaterThan(0));

        dominantBody.position = new Vector3(2f, 0f, 0f);
        dominantObject.transform.position = dominantBody.position;
        physicsScene.Simulate(0.02f);
        physicsScene.Simulate(0.02f);

        Assert.That(probe.triggerExitCount, Is.EqualTo(1));

        registration.Dispose();
        var unload = SceneManager.UnloadSceneAsync(scene);
        if (unload != null)
            yield return unload;
    }

    private NetworkTransformContactManager.Registration Register(
        Rigidbody body, NetworkTransformContactDominance dominance)
    {
        var registration = NetworkTransformContactManager.Register(body, dominance);
        Assert.That(registration, Is.Not.Null);
        _registrations.Add(registration);
        return registration;
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
    public int collisionStayCount { get; private set; }
    public int collisionExitCount { get; private set; }
    public int triggerEnterCount { get; private set; }
    public int triggerStayCount { get; private set; }
    public int triggerExitCount { get; private set; }

    private void OnCollisionEnter(Collision _)
    {
        collisionEnterCount++;
    }

    private void OnCollisionStay(Collision _)
    {
        collisionStayCount++;
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

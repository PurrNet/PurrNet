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

        bool ignore = NetworkTransformContactManager.ApplyDominance(
            ref massProperties,
            NetworkTransformContactDominance.Linear,
            NetworkTransformContactDominance.Angular);

        Assert.That(ignore, Is.False);
        Assert.That(massProperties.inverseMassScale, Is.Zero);
        Assert.That(massProperties.inverseInertiaScale, Is.EqualTo(2f));
        Assert.That(massProperties.otherInverseMassScale, Is.EqualTo(3f));
        Assert.That(massProperties.otherInverseInertiaScale, Is.Zero);
    }

    [Test]
    public void TwoFullyDominantBodiesIgnoreThePair()
    {
        var massProperties = default(ModifiableMassProperties);
        massProperties.inverseMassScale = 1f;
        massProperties.inverseInertiaScale = 1f;
        massProperties.otherInverseMassScale = 1f;
        massProperties.otherInverseInertiaScale = 1f;

        bool ignore = NetworkTransformContactManager.ApplyDominance(
            ref massProperties,
            NetworkTransformContactDominance.Full,
            NetworkTransformContactDominance.Full);

        Assert.That(ignore, Is.True);
        Assert.That(massProperties.inverseMassScale, Is.EqualTo(1f));
        Assert.That(massProperties.inverseInertiaScale, Is.EqualTo(1f));
        Assert.That(massProperties.otherInverseMassScale, Is.EqualTo(1f));
        Assert.That(massProperties.otherInverseInertiaScale, Is.EqualTo(1f));
    }

    [Test]
    public void RegistrationCombinesDominanceAndRestoresColliderFlag()
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
        Assert.That(collider.hasModifiableContacts, Is.False);
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

        Assert.That(rootCollider.hasModifiableContacts, Is.False);
        Assert.That(compoundCollider.hasModifiableContacts, Is.False);
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
#endif

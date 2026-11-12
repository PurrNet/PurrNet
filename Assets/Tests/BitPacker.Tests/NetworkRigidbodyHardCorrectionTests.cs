#if UNITY_PHYSICS_3D
using NUnit.Framework;
using PurrNet;
using UnityEngine;

/// <summary>
/// A hard correction places the body at its target. It must not arrive there with the speed of
/// having crossed the gap in one step, or it launches whatever it comes to rest against.
/// </summary>
public class NetworkRigidbodyHardCorrectionTests
{
    sealed class DefaultCorrection : NetworkRigidbodySettingsInstance { }

    SimulationMode _previousMode;
    GameObject _corrected;
    GameObject _bystander;

    [SetUp]
    public void SetUp()
    {
        _previousMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
    }

    [TearDown]
    public void TearDown()
    {
        Physics.simulationMode = _previousMode;
        if (_corrected) Object.DestroyImmediate(_corrected);
        if (_bystander) Object.DestroyImmediate(_bystander);
    }

    static Rigidbody CreateBox(string name, Vector3 position, bool kinematic, out GameObject go)
    {
        go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider>();
        var body = go.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = kinematic;
        return body;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BodyAtTheDestinationIsNotLaunched(bool kinematic)
    {
        var origin = new Vector3(1000f, 1000f, 1000f);
        var target = origin + Vector3.right * 10f;

        // Unit boxes: at the target the two faces are 5 mm apart, inside the contact offset.
        var restingPlace = target + Vector3.right * 1.005f;

        var corrected = CreateBox("Corrected", origin, kinematic, out _corrected);
        corrected.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var bystander = CreateBox("Bystander", restingPlace, false, out _bystander);
        Physics.SyncTransforms();

        new DefaultCorrection().ApplyHardCorrection(new RigidbodyCorrectionContext
        {
            rigidbody = corrected,
            targetPosition = target,
            targetRotation = Quaternion.identity
        });

        for (int i = 0; i < 3; i++)
            Physics.Simulate(0.02f);

        Assert.That(Vector3.Distance(corrected.position, target), Is.LessThan(0.05f), "the body reaches its target");
        Assert.That(bystander.linearVelocity.magnitude, Is.LessThan(0.5f), "the body it came to rest against was launched");
        Assert.That(Vector3.Distance(bystander.position, restingPlace), Is.LessThan(0.05f));
    }
}
#endif

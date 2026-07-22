using NUnit.Framework;
using PurrNet;
using UnityEngine;

public class NetworkRigidbodyParentFrameTests
{
    private const float Tolerance = 0.0001f;

    [Test]
    public void MovingParent_RestingChild_EncodesZeroRelativeVelocity()
    {
        var parentVelocity = new Vector3(10f, 0f, 0f);

        var encoded = NetworkRigidbodyFrameMath.ToParentLinearVelocity(
            parentVelocity,
            parentVelocity,
            Quaternion.identity,
            Vector3.one);

        AssertVector(Vector3.zero, encoded);
    }

    [Test]
    public void MovingParent_RelativeVelocityRoundTrips()
    {
        var parentPointVelocity = new Vector3(10f, 0f, 0f);
        var childWorldVelocity = new Vector3(12f, 0f, 0f);

        var encoded = NetworkRigidbodyFrameMath.ToParentLinearVelocity(
            childWorldVelocity,
            parentPointVelocity,
            Quaternion.identity,
            Vector3.one);
        var decoded = NetworkRigidbodyFrameMath.ToWorldLinearVelocity(
            encoded,
            parentPointVelocity,
            Quaternion.identity,
            Vector3.one);

        AssertVector(new Vector3(2f, 0f, 0f), encoded);
        AssertVector(childWorldVelocity, decoded);
    }

    [Test]
    public void RotatingParent_OffCenterChild_UsesPointVelocity()
    {
        var pointVelocity = NetworkRigidbodyFrameMath.GetPointVelocity(
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            Vector3.zero,
            new Vector3(2f, 0f, 0f));

        AssertVector(new Vector3(1f, 0f, -2f), pointVelocity);
    }

    [Test]
    public void CoRotatingChild_EncodesZeroRelativeAngularVelocity()
    {
        var parentAngularVelocity = new Vector3(0f, 2f, 0f);

        var encoded = NetworkRigidbodyFrameMath.ToParentAngularVelocity(
            parentAngularVelocity,
            parentAngularVelocity,
            Quaternion.Euler(10f, 20f, 30f));

        AssertVector(Vector3.zero, encoded);
    }

    [Test]
    public void ClampedParentLocalHold_KeepsParentWorldVelocity()
    {
        var parentPointVelocity = new Vector3(10f, 0f, 0f);
        var parentAngularVelocity = new Vector3(0f, 1f, 0f);

        var worldLinearVelocity = NetworkRigidbodyFrameMath.ToWorldLinearVelocity(
            Vector3.zero,
            parentPointVelocity,
            Quaternion.identity,
            Vector3.one);
        var worldAngularVelocity = NetworkRigidbodyFrameMath.ToWorldAngularVelocity(
            Vector3.zero,
            parentAngularVelocity,
            Quaternion.identity);

        AssertVector(parentPointVelocity, worldLinearVelocity);
        AssertVector(parentAngularVelocity, worldAngularVelocity);
    }

    [Test]
    public void StaticParent_RoundTripsAxesWithoutRigidbodyMotion()
    {
        var parentRotation = Quaternion.Euler(15f, -25f, 5f);
        var worldVelocity = new Vector3(4f, -2f, 7f);

        var encoded = NetworkRigidbodyFrameMath.ToParentLinearVelocity(
            worldVelocity,
            Vector3.zero,
            parentRotation,
            Vector3.one);
        var decoded = NetworkRigidbodyFrameMath.ToWorldLinearVelocity(
            encoded,
            Vector3.zero,
            parentRotation,
            Vector3.one);

        AssertVector(worldVelocity, decoded);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance));
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using UnityEngine.TestTools;

public class NetworkRigidbodySequenceTests
{
    [Test]
    public void SequenceOrder_RejectsDuplicateAndOlderStates()
    {
        Assert.That(NetworkRigidbodySequenceMath.IsNewer(2u, 1u), Is.True);
        Assert.That(NetworkRigidbodySequenceMath.IsNewer(1u, 2u), Is.False);
        Assert.That(NetworkRigidbodySequenceMath.IsNewer(2u, 2u), Is.False);
    }

    [Test]
    public void SequenceOrder_HandlesWrapAndReservesZero()
    {
        Assert.That(NetworkRigidbodySequenceMath.IsNewer(1u, uint.MaxValue), Is.True);
        Assert.That(NetworkRigidbodySequenceMath.IncrementNonZero(uint.MaxValue), Is.EqualTo(1u));
        Assert.That(NetworkRigidbodySequenceMath.IncrementNonZero(0u), Is.EqualTo(1u));
    }

    [Test]
    public void ServerSourceGate_ReacquiredControllerContinuesPastInFlightSequence()
    {
        bool hasSequence = false;
        uint currentSequence = 0;

        Assert.That(
            NetworkRigidbodySequenceMath.TryAcceptSourceSequence(
                500u, ref hasSequence, ref currentSequence),
            Is.True);
        Assert.That(
            NetworkRigidbodySequenceMath.TryAcceptSourceSequence(
                1u, ref hasSequence, ref currentSequence),
            Is.False,
            "Resetting a re-acquired controller to one would stall behind an in-flight packet.");
        Assert.That(
            NetworkRigidbodySequenceMath.TryAcceptSourceSequence(
                501u, ref hasSequence, ref currentSequence),
            Is.True);
        Assert.That(currentSequence, Is.EqualTo(501u));

        var gameObject = new GameObject(nameof(ServerSourceGate_ReacquiredControllerContinuesPastInFlightSequence));
        var networkRigidbody = gameObject.AddComponent<NetworkRigidbody>();
        try
        {
            var type = typeof(NetworkRigidbody);
            type.GetField("_sendStateSequence", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(networkRigidbody, 500u);
            type.GetMethod("BeginServerAuthorityEpoch", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(networkRigidbody, null);
            uint nextSequence = (uint)type
                .GetMethod("NextStateSequence", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(networkRigidbody, null);

            Assert.That(nextSequence, Is.EqualTo(501u));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void PreSpawnAuthorityAnchor_UsesCurrentRigidbodyPose()
    {
        var gameObject = new GameObject(nameof(PreSpawnAuthorityAnchor_UsesCurrentRigidbodyPose));
        var spawnPosition = new Vector3(3f, 4f, 5f);
        gameObject.transform.position = spawnPosition;
        gameObject.AddComponent<Rigidbody>();
        var networkRigidbody = gameObject.AddComponent<NetworkRigidbody>();

        try
        {
            var capture = typeof(NetworkRigidbody).GetMethod(
                "CaptureServerAuthorityAnchor",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var state = (RigidbodyStateData)capture.Invoke(networkRigidbody, null);
            Vector3 capturedPosition = state.position;

            Assert.That(state.positionFrame, Is.EqualTo(RigidbodyPositionFrame.World));
            Assert.That(capturedPosition.x, Is.EqualTo(spawnPosition.x).Within(0.0001f));
            Assert.That(capturedPosition.y, Is.EqualTo(spawnPosition.y).Within(0.0001f));
            Assert.That(capturedPosition.z, Is.EqualTo(spawnPosition.z).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void InvalidAuthorityAnchor_DoesNotPoisonServerStateCache()
    {
        var gameObject = new GameObject(nameof(InvalidAuthorityAnchor_DoesNotPoisonServerStateCache));
        gameObject.AddComponent<Rigidbody>();
        var networkRigidbody = gameObject.AddComponent<NetworkRigidbody>();

        try
        {
            var type = typeof(NetworkRigidbody);
            var stamp = type.GetMethod(
                "TryStampAndCacheServerAuthorityAnchor",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var epoch = type.GetField(
                "_serverAuthorityEpoch",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hasLatest = type.GetField(
                "_hasLatestServerState",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            epoch.SetValue(networkRigidbody, 7u);
            var invalidState = new RigidbodyStateData
            {
                positionFrame = RigidbodyPositionFrame.World,
                rotation = Quaternion.identity,
                time = double.NaN
            };

            LogAssert.Expect(
                LogType.Error,
                new Regex("Rejected outgoing NetworkRigidbody state because it contains invalid data"));
            object[] invalidArguments = { invalidState, "test invalid authority anchor" };
            bool invalidAccepted = (bool)stamp.Invoke(networkRigidbody, invalidArguments);

            Assert.That(invalidAccepted, Is.False);
            Assert.That(hasLatest.GetValue(networkRigidbody), Is.False);

            var validState = invalidState;
            validState.time = 1d;
            object[] validArguments = { validState, "test valid authority anchor" };
            bool validAccepted = (bool)stamp.Invoke(networkRigidbody, validArguments);

            Assert.That(validAccepted, Is.True);
            Assert.That(hasLatest.GetValue(networkRigidbody), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void SequenceGate_RejectsOlderStateAcrossChannels()
    {
        bool hasOrder = false;
        uint epoch = 0;
        uint sequence = 0;

        bool acceptedReliable = NetworkRigidbodySequenceMath.TryAccept(
            4u, 10u, ref hasOrder, ref epoch, ref sequence, out bool firstEpochChange);
        bool acceptedOlderUnreliable = NetworkRigidbodySequenceMath.TryAccept(
            4u, 9u, ref hasOrder, ref epoch, ref sequence, out bool staleEpochChange);

        Assert.That(acceptedReliable, Is.True);
        Assert.That(firstEpochChange, Is.False);
        Assert.That(acceptedOlderUnreliable, Is.False);
        Assert.That(staleEpochChange, Is.False);
        Assert.That(epoch, Is.EqualTo(4u));
        Assert.That(sequence, Is.EqualTo(10u));
    }

    [Test]
    public void SequenceGate_NewEpochAnchorSupersedesOldAuthority()
    {
        bool hasOrder = false;
        uint epoch = 0;
        uint sequence = 0;

        Assert.That(
            NetworkRigidbodySequenceMath.TryAccept(
                4u, 10u, ref hasOrder, ref epoch, ref sequence, out _),
            Is.True);

        bool acceptedAnchor = NetworkRigidbodySequenceMath.TryAccept(
            5u, 0u, ref hasOrder, ref epoch, ref sequence, out bool epochChanged);
        bool acceptedOldAuthority = NetworkRigidbodySequenceMath.TryAccept(
            4u, 11u, ref hasOrder, ref epoch, ref sequence, out bool oldEpochChanged);
        bool acceptedNewState = NetworkRigidbodySequenceMath.TryAccept(
            5u, 1u, ref hasOrder, ref epoch, ref sequence, out bool newStateEpochChanged);

        Assert.That(acceptedAnchor, Is.True);
        Assert.That(epochChanged, Is.True);
        Assert.That(acceptedOldAuthority, Is.False);
        Assert.That(oldEpochChanged, Is.False);
        Assert.That(acceptedNewState, Is.True);
        Assert.That(newStateEpochChanged, Is.False);
        Assert.That(epoch, Is.EqualTo(5u));
        Assert.That(sequence, Is.EqualTo(1u));
    }
}

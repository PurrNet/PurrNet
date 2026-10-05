using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using Object = UnityEngine.Object;

public class NetworkRigidbodyClockTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const double SendInterval = 0.05;
    private GameObject _gameObject;
    private NetworkRigidbody _networkRigidbody;

    [SetUp]
    public void SetUp()
    {
        _gameObject = new GameObject(nameof(NetworkRigidbodyClockTests));
        _gameObject.AddComponent<Rigidbody>();
        _networkRigidbody = _gameObject.AddComponent<NetworkRigidbody>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
    }

    [TestCase(14.175)]
    [TestCase(-14.175)]
    [TestCase(0.3)]
    [TestCase(-0.3)]
    public void ServerClock_RemovesControllerClockSkewAndKeepsSendSpacing(double skew)
    {
        double[] latencies = { 0.02, 0.05, 0.03, 0.08, 0.02, 0.04 };
        bool hasOffset = false;
        double offset = 0;
        double firstStamp = 0;

        for (int i = 0; i < latencies.Length; i++)
        {
            double capture = 1000d + i * SendInterval;
            double arrival = capture + latencies[i];
            double stamp = NetworkRigidbodyClockMath.ToServerClock(capture - skew, arrival, ref hasOffset, ref offset);

            if (i == 0)
                firstStamp = stamp;

            Assert.That(stamp, Is.LessThanOrEqualTo(arrival), "A relayed stamp must never lie in the server's future.");
            Assert.That(stamp, Is.GreaterThanOrEqualTo(capture), "The stamp must land on the server clock, not the controller's.");
            Assert.That(stamp - firstStamp, Is.EqualTo(i * SendInterval).Within(0.005),
                $"Capture {i} lost its controller send spacing.");
        }
    }

    [Test]
    public void ServerClock_LateBurstDoesNotShiftTheOffset()
    {
        bool hasOffset = false;
        double offset = 0;

        for (int i = 0; i < 10; i++)
            NetworkRigidbodyClockMath.ToServerClock(500d + i * SendInterval, 1000d + i * SendInterval + 0.02, ref hasOffset, ref offset);

        double settledOffset = offset;
        double burstArrival = 1000d + 10 * SendInterval + 3d;

        for (int i = 10; i < 20; i++)
        {
            double stamp = NetworkRigidbodyClockMath.ToServerClock(500d + i * SendInterval, burstArrival, ref hasOffset, ref offset);
            Assert.That(stamp, Is.EqualTo(500d + i * SendInterval + settledOffset).Within(1e-9),
                "Packets delayed by a stall must keep their capture spacing on the server clock.");
        }

        Assert.That(offset, Is.EqualTo(settledOffset).Within(1e-9));
    }

    [Test]
    public void RestampToServerClock_EstimatesEachControllerSeparately()
    {
        var first = new PlayerID(1, false);
        var second = new PlayerID(2, false);
        double now = Time.unscaledTimeAsDouble;

        var firstState = new RigidbodyStateData { time = now - 0.02 };
        Invoke("RestampToServerClock", ref firstState, first);

        var secondState = new RigidbodyStateData { time = now + 40d };
        Invoke("RestampToServerClock", ref secondState, second);

        Assert.That(secondState.time, Is.EqualTo(now).Within(1e-6),
            "A new controller's clock must not be mapped with the previous controller's offset.");
    }

    [Test]
    public void RestampToServerClock_LeavesLegacyCapturesUnstamped()
    {
        var state = new RigidbodyStateData { time = 0d };
        Invoke("RestampToServerClock", ref state, new PlayerID(1, false));
        Assert.That(state.time, Is.EqualTo(0d));
    }

    [TestCase(14.175)]
    [TestCase(-14.175)]
    [TestCase(0.3)]
    public void AuthorityHandoff_RelayedStreamNeverLandsInTheFuture(double controllerSkew)
    {
        double now = Time.unscaledTimeAsDouble;
        PushSnapshot(now - 0.3);

        bool hasOffset = false;
        double offset = 0;
        for (int i = 0; i < 5; i++)
        {
            double arrival = now - 0.25 + i * SendInterval + 0.01;
            double controllerCapture = arrival - 0.01 - controllerSkew;
            double stamp = NetworkRigidbodyClockMath.ToServerClock(controllerCapture, arrival, ref hasOffset, ref offset);
            PushSnapshot(stamp);
        }

        int count = Field<int>("_bufferCount");
        Assert.That(count, Is.EqualTo(6));
        for (int i = 0; i < count; i++)
        {
            Assert.That(SnapshotTime(i), Is.LessThanOrEqualTo(now + 1e-6),
                $"Snapshot {i} was placed {SnapshotTime(i) - now:F3}s in the future, which freezes interpolation until real time catches up.");
        }

        for (int i = 2; i < count; i++)
            Assert.That(SnapshotTime(i) - SnapshotTime(i - 1), Is.EqualTo(SendInterval).Within(0.005));
    }

    private void PushSnapshot(double time)
    {
        var state = new RigidbodyStateData
        {
            positionFrame = RigidbodyPositionFrame.World,
            rotation = Quaternion.identity,
            time = time
        };
        Invoke("PushSnapshot", state, true);
    }

    private double SnapshotTime(int index)
    {
        object snapshot = typeof(NetworkRigidbodyBase).GetMethod("GetSnapshot", PrivateInstance)
            .Invoke(_networkRigidbody, new object[] { index });
        return (double)snapshot.GetType().GetField("time").GetValue(snapshot);
    }

    private T Field<T>(string name)
    {
        return (T)typeof(NetworkRigidbodyBase).GetField(name, PrivateInstance).GetValue(_networkRigidbody);
    }

    private void Invoke(string name, ref RigidbodyStateData state, PlayerID sender)
    {
        var args = new object[] { state, sender };
        typeof(NetworkRigidbodyBase).GetMethod(name, PrivateInstance).Invoke(_networkRigidbody, args);
        state = (RigidbodyStateData)args[0];
    }

    private void Invoke(string name, params object[] arguments)
    {
        typeof(NetworkRigidbodyBase).GetMethod(name, PrivateInstance).Invoke(_networkRigidbody, arguments);
    }
}

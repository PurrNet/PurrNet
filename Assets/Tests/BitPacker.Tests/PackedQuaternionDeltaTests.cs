using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class PackedQuaternionDeltaTests
{
    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    static PackedQuaternion Shift(PackedQuaternion q, int amount)
    {
        var r = q;
        for (int i = 0; i < 4; i++)
            r.Set(i, Mathf.Clamp((int)q.Get(i) + amount * (i + 1), -1024, 1023));
        return r;
    }

    [Test]
    public void DeltaRoundTripsAndSendsThreeComponents()
    {
        var random = new System.Random(7);
        long totalBits = 0;
        for (int n = 0; n < 5000; n++)
        {
            var q = (PackedQuaternion)Quaternion.Euler(random.Next(-180, 180), random.Next(-180, 180), random.Next(-180, 180));
            var oldValues = new[] { Shift(q, random.Next(-40, 40)), default, (PackedQuaternion)Quaternion.identity, q };

            Assert.That(q.DerivedIndex(), Is.GreaterThanOrEqualTo(0), q.ToString());

            foreach (var old in oldValues)
            {
                using var packer = BitPackerPool.Get();
                DeltaPacker<PackedQuaternion>.Write(packer, old, q);
                totalBits += packer.positionInBits;
                packer.ResetPositionAndMode(true);
                var read = default(PackedQuaternion);
                DeltaPacker<PackedQuaternion>.Read(packer, old, ref read);
                Assert.That(read, Is.EqualTo(q), $"old={old} new={q}");
            }
        }

        using (var one = BitPackerPool.Get())
        {
            var q = (PackedQuaternion)Quaternion.Euler(10, 20, 30);
            DeltaPacker<PackedQuaternion>.Write(one, Shift(q, 3), q);
            Assert.That(one.positionInBits, Is.LessThan(4 + 3 * 11), "the derived component is not sent");
        }

        Assert.That(totalBits, Is.GreaterThan(0));
    }

    [Test]
    public void NonDerivableValuesFallBackToFourComponents()
    {
        var q = new PackedQuaternion { x = new NormalizedFloat(5), y = new NormalizedFloat(7), z = new NormalizedFloat(9), w = new NormalizedFloat(11) };
        Assert.That(q.DerivedIndex(), Is.EqualTo(-1));

        using var packer = BitPackerPool.Get();
        DeltaPacker<PackedQuaternion>.Write(packer, default, q);
        packer.ResetPositionAndMode(true);
        var read = default(PackedQuaternion);
        DeltaPacker<PackedQuaternion>.Read(packer, default, ref read);
        Assert.That(read, Is.EqualTo(q));
    }
}

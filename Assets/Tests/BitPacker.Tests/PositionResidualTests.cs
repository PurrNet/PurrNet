using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;

public class PositionResidualTests
{
    [Test]
    public void ResidualsRoundTrip()
    {
        var values = new System.Collections.Generic.List<int> { 0, 1, -1, 31, -32, 32, 1000, -1000, 123456, -123456, int.MaxValue / 2, int.MinValue / 2 };
        var random = new System.Random(3);
        for (int i = 0; i < 2000; i++)
            values.Add(random.Next(-5000, 5000));

        using var packer = BitPackerPool.Get();
        foreach (var v in values)
            NetworkTransform.WriteResidual(packer, v);

        packer.ResetPositionAndMode(true);
        foreach (var v in values)
            Assert.That(NetworkTransform.ReadResidual(packer), Is.EqualTo(v));
    }

    [Test]
    public void SmallResidualsAreCheap()
    {
        using var packer = BitPackerPool.Get();
        NetworkTransform.WriteResidual(packer, 0);
        Assert.That(packer.positionInBits, Is.EqualTo(1));
        NetworkTransform.WriteResidual(packer, 7);
        Assert.That(packer.positionInBits, Is.EqualTo(1 + 7));
        NetworkTransform.WriteResidual(packer, 200);
        Assert.That(packer.positionInBits, Is.LessThan(1 + 7 + 16));
    }
}

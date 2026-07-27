using NUnit.Framework;
using PurrNet.Modules;

public class TickManagerPacingTests
{
    static TickManager Create() => new TickManager(64, null, null, true);

    [Test]
    public void DefaultScaleIsNeutral()
    {
        var tm = Create();
        Assert.AreEqual(1d, tm.tickPacingScale);
    }

    [Test]
    public void ScaleClampsToUpperBound()
    {
        var tm = Create();
        tm.tickPacingScale = 1.5d;
        Assert.AreEqual(TickManager.maxTickPacingScale, tm.tickPacingScale);
    }

    [Test]
    public void ScaleClampsToLowerBound()
    {
        var tm = Create();
        tm.tickPacingScale = 0.5d;
        Assert.AreEqual(TickManager.minTickPacingScale, tm.tickPacingScale);
    }

    [Test]
    public void ScaleInsideBoundsIsPreserved()
    {
        var tm = Create();
        tm.tickPacingScale = 1.013d;
        Assert.AreEqual(1.013d, tm.tickPacingScale, 1e-12);

        tm.tickPacingScale = 0.988d;
        Assert.AreEqual(0.988d, tm.tickPacingScale, 1e-12);
    }

    [Test]
    public void NanIsIgnored()
    {
        var tm = Create();
        tm.tickPacingScale = 1.01d;
        tm.tickPacingScale = double.NaN;
        Assert.AreEqual(1.01d, tm.tickPacingScale, 1e-12);
    }

    [Test]
    public void BoundsAreSane()
    {
        Assert.Less(TickManager.minTickPacingScale, 1d);
        Assert.Greater(TickManager.maxTickPacingScale, 1d);
    }
}

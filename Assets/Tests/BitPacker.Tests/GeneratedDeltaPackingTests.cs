using NUnit.Framework;
using PurrNet.Packing;

public struct SomeStruct : IPackedAuto, IDuplicate<SomeStruct>
{
    public int value;

    public SomeStruct Duplicate()
    {
        return this;
    }
}

public class GeneratedDeltaPackingTests
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void TestSomeStruct()
    {
        var oldVal = new SomeStruct { value = 3 };
        var newVal = new SomeStruct { value = 60 };

        packer.ResetPositionAndMode(false);
        DeltaPacker<SomeStruct>.Write(packer, oldVal, newVal);

        var readValue = oldVal;
        packer.ResetPositionAndMode(true);

        DeltaPacker<SomeStruct>.Read(packer, oldVal, ref readValue);

        Assert.That(readValue.value, Is.EqualTo(newVal.value));
    }
}

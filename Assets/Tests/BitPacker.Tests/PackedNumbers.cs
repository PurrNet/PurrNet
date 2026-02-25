using NUnit.Framework;
using System;
using PurrNet;
using PurrNet.Packing;

public class PackedNumberTests
{
    private BitPacker packer;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
        packer = BitPackerPool.Get();
    }

    [TearDown]
    public void Teardown()
    {
        packer?.Dispose();
    }

    [Test]
    public void TestUshortZero()
    {
        var value = new PackedUShort(0);

        packer.ResetPositionAndMode(false);
        Packer<PackedUShort>.Write(packer, value);

        packer.ResetPositionAndMode(true);
        PackedUShort read = default;
        Packer<PackedUShort>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(0), "Failed to pack and unpack ushort value 0");
    }

    [Test]
    public void TestIntZero()
    {
        var value = new PackedInt(0);
        Packer<PackedInt>.Write(packer, value);

        packer.ResetPositionAndMode(true);

        PackedInt read = default;
        Packer<PackedInt>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(0), "Failed to pack and unpack int value 0");
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(-1)]
    [TestCase(short.MaxValue)]
    [TestCase(short.MinValue)]
    public void TestIntEdgeCases(int value)
    {
        var packed = new PackedInt(value);
        Packer<PackedInt>.Write(packer, packed);

        packer.ResetPositionAndMode(true);

        PackedInt read = default;
        Packer<PackedInt>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(value),
            $"Failed to pack and unpack int value {value}\n" +
            $"Expected binary: {Convert.ToString(value, 2).PadLeft(32, value < 0 ? '1' : '0')}\n" +
            $"Got binary:      {Convert.ToString(read.value, 2).PadLeft(32, read.value < 0 ? '1' : '0')}");
    }


    [Test]
    [TestCase((ushort)0)]
    [TestCase((ushort)1)]
    [TestCase((ushort)255)]
    [TestCase((ushort)256)]
    [TestCase((ushort)32768)]
    [TestCase((ushort)65535)]
    public void TestUshortEdgeCases(ushort value)
    {
        var packed = new PackedUShort(value);
        Packer<PackedUShort>.Write(packer, packed);

        packer.ResetPositionAndMode(true);

        PackedUShort read = default;
        Packer<PackedUShort>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(value),
            $"Failed to pack and unpack ushort value {value}\n" +
            $"Expected binary: {Convert.ToString(value, 2).PadLeft(16, '0')}\n" +
            $"Got binary:      {Convert.ToString(read.value, 2).PadLeft(16, '0')}");
    }

    [Test]
    [TestCase((ushort)69)]
    [TestCase((ushort)420)]
    [TestCase((ushort)1024)]
    [TestCase((ushort)9999)]
    [TestCase((ushort)12345)]
    public void TestUshortTypicalValues(ushort value)
    {
        var packed = new PackedUShort(value);
        Packer<PackedUShort>.Write(packer, packed);

        packer.ResetPositionAndMode(true);

        PackedUShort read = default;
        Packer<PackedUShort>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(value),
            $"Failed to pack and unpack typical ushort value {value}");
    }

    [Test]
    [TestCase(0U)]
    [TestCase(1U)]
    [TestCase(0x80000000U)]
    [TestCase(0x0000FFFFU)]
    [TestCase(0xFFFF0000U)]
    public void TestUintEdgeCases(uint value)
    {
        var packed = new PackedUInt(value);
        Packer<PackedUInt>.Write(packer, packed);

        packer.ResetPositionAndMode(true);

        PackedUInt read = default;
        Packer<PackedUInt>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(value),
            $"Failed to pack and unpack uint value {value:X8}\n" +
            $"Expected binary: {Convert.ToString((int)value, 2).PadLeft(32, '0')}\n" +
            $"Got binary:      {Convert.ToString((int)read.value, 2).PadLeft(32, '0')}");
    }

    [Test]
    [TestCase(69420U)]
    [TestCase(1000000U)]
    [TestCase(0x12345678U)]
    [TestCase(2147483648U)] // 2^31
    public void TestUintTypicalValues(uint value)
    {
        var packed = new PackedUInt(value);
        Packer<PackedUInt>.Write(packer, packed);

        packer.ResetPositionAndMode(true);

        PackedUInt read = default;
        Packer<PackedUInt>.Read(packer, ref read);

        Assert.That(read.value, Is.EqualTo(value),
            $"Failed to pack and unpack typical uint value {value}");
    }

    [Test]
    public void TestSequentialWrites()
    {
        // Test writing different types sequentially
        var ushortVal = new PackedUShort(12345);
        var uintVal = new PackedUInt(987654321);
        var intVal = new PackedInt(-42);

        // Write all values
        Packer<PackedUShort>.Write(packer, ushortVal);
        Packer<PackedUInt>.Write(packer, uintVal);
        Packer<PackedInt>.Write(packer, intVal);

        packer.ResetPositionAndMode(true);

        // Read all values
        PackedUShort readUShort = default;
        PackedUInt readUInt = default;
        PackedInt readInt = default;

        Packer<PackedUShort>.Read(packer, ref readUShort);
        Packer<PackedUInt>.Read(packer, ref readUInt);
        Packer<PackedInt>.Read(packer, ref readInt);

        Assert.That(readUShort.value, Is.EqualTo(ushortVal.value), "Failed to read sequential ushort");
        Assert.That(readUInt.value, Is.EqualTo(uintVal.value), "Failed to read sequential uint");
        Assert.That(readInt.value, Is.EqualTo(intVal.value), "Failed to read sequential int");
    }

    [Test]
    public void TestBitPatterns()
    {
        // Test alternating bit patterns
        ushort[] patterns =
        {
            0b1010101010101010,
            0b0101010101010101,
            0b1111000011110000,
            0b0000111100001111
        };

        foreach (var pattern in patterns)
        {
            var packed = new PackedUShort(pattern);
            Packer<PackedUShort>.Write(packer, packed);
        }

        packer.ResetPositionAndMode(true);

        foreach (var expected in patterns)
        {
            PackedUShort read = default;
            Packer<PackedUShort>.Read(packer, ref read);

            Assert.That(read.value, Is.EqualTo(expected),
                $"Failed to preserve bit pattern {Convert.ToString(expected, 2).PadLeft(16, '0')}");
        }
    }

    [Test]
    public void TestLeadingZeros()
    {
        // Test values with different numbers of leading zeros
        for (int i = 0; i < 16; i++)
        {
            ushort value = (ushort)(1 << i);
            var packed = new PackedUShort(value);
            Packer<PackedUShort>.Write(packer, packed);
        }

        packer.ResetPositionAndMode(true);

        for (int i = 0; i < 16; i++)
        {
            ushort expected = (ushort)(1 << i);
            PackedUShort read = default;
            Packer<PackedUShort>.Read(packer, ref read);

            Assert.That(read.value, Is.EqualTo(expected),
                $"Failed for value with {15 - i} leading zeros: {expected}");
        }
    }
}

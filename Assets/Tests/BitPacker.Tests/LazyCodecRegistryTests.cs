using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Packing;

public sealed class LazyCodecRegistryTests
{
    public struct Value { public uint number; }
    public struct ManualValue { public uint number; }
    public struct DelegateFirstValue { public uint number; }
    public struct DeltaValue { public uint number; }
    public struct ManualDeltaValue { public uint number; }
    public class BaseValue { public uint number; }
    public sealed class DerivedValue : BaseValue { }

    private BitPacker _packer;

    [SetUp]
    public void SetUp()
    {
        PurrNet.NetworkManager.LoadOrGenerateHashes();
        _packer = BitPackerPool.Get();
    }

    [TearDown]
    public void TearDown() => _packer.Dispose();

    [Test]
    public void GenericRegistrationRetainsDelegatesAndSupportsDynamicExactAndWrappedCalls()
    {
        Packer<Value>.RegisterWriter(WriteValue);
        Packer<Value>.RegisterReader(ReadValue);
        foreach (var field in new[] { "_writeExactMethods", "_writeWrappedMethods", "_readExactMethods", "_readWrappedMethods" })
            Assert.That(Registry(typeof(Packer), field)[typeof(Value)], Is.InstanceOf<Delegate>());

        Packer.Write(_packer, typeof(Value), new Value { number = 0xfedcba98 });
        Packer.WriteAsExactType(_packer, typeof(Value), new Value { number = 123 });
        _packer.ResetPositionAndMode(true);
        object first = null, second = null;
        Packer.Read(_packer, typeof(Value), ref first);
        Packer.ReadAsExactType(_packer, typeof(Value), ref second);
        Assert.That(((Value)first).number, Is.EqualTo(0xfedcba98u));
        Assert.That(((Value)second).number, Is.EqualTo(123u));
        Assert.That(_packer.positionInBits, Is.EqualTo(64));
    }

    [Test]
    public void EarlierMethodInfoRegistrationKeepsPriorityOverGenericRegistration()
    {
        var writer = Method(nameof(WriteManual));
        var reader = Method(nameof(ReadManual));
        Packer.RegisterWriter(typeof(ManualValue), writer, writer);
        Packer.RegisterReader(typeof(ManualValue), reader, reader);
        Packer<ManualValue>.RegisterWriter(WriteIgnoredManual);
        Packer<ManualValue>.RegisterReader(ReadIgnoredManual);
        Packer.Write(_packer, new ManualValue { number = 42 });
        _packer.ResetPositionAndMode(true);
        object actual = null;
        Packer.ReadRawObject(typeof(ManualValue), _packer, ref actual);
        Assert.That(((ManualValue)actual).number, Is.EqualTo(42u));
    }

    [Test]
    public void EarlierDelegateRegistrationKeepsPriorityOverMethodInfoRegistration()
    {
        Packer<DelegateFirstValue>.RegisterWriter(WriteDelegateFirst);
        Packer<DelegateFirstValue>.RegisterReader(ReadDelegateFirst);
        Packer.RegisterWriter(typeof(DelegateFirstValue), Method(nameof(WriteIgnoredDelegate)), Method(nameof(WriteIgnoredDelegate)));
        Packer.RegisterReader(typeof(DelegateFirstValue), Method(nameof(ReadIgnoredDelegate)), Method(nameof(ReadIgnoredDelegate)));
        Packer.Write(_packer, new DelegateFirstValue { number = 73 });
        _packer.ResetPositionAndMode(true);
        object actual = null;
        Packer.ReadRawObject(typeof(DelegateFirstValue), _packer, ref actual);
        Assert.That(((DelegateFirstValue)actual).number, Is.EqualTo(73u));
    }

    [Test]
    public void PolymorphicWrapperDispatchesToDerivedExactCodec()
    {
        Packer<BaseValue>.RegisterWriter(WriteBase);
        Packer<BaseValue>.RegisterReader(ReadBase);
        Packer<DerivedValue>.RegisterWriter(WriteDerived);
        Packer<DerivedValue>.RegisterReader(ReadDerived);
        Packer.Write(_packer, typeof(BaseValue), new DerivedValue { number = 91 });
        _packer.ResetPositionAndMode(true);
        object actual = null;
        Packer.Read(_packer, typeof(BaseValue), ref actual);
        Assert.That(actual, Is.TypeOf<DerivedValue>());
        Assert.That(((DerivedValue)actual).number, Is.EqualTo(91u));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DynamicDeltaUsesDeferredDelegatesAndPreservesOldValue(bool changed)
    {
        DeltaPacker<DeltaValue>.Register(WriteDelta, ReadDelta);
        Assert.That(Registry(typeof(DeltaPacker), "_writeMethods")[typeof(DeltaValue)], Is.InstanceOf<Delegate>());
        Assert.That(Registry(typeof(DeltaPacker), "_readMethods")[typeof(DeltaValue)], Is.InstanceOf<Delegate>());
        var previous = new DeltaValue { number = 17 };
        var next = new DeltaValue { number = changed ? 82u : 17u };
        Assert.That(DeltaPacker.Write(_packer, typeof(DeltaValue), previous, next), Is.EqualTo(changed));
        _packer.ResetPositionAndMode(true);
        object actual = default(DeltaValue);
        DeltaPacker.Read(_packer, typeof(DeltaValue), previous, ref actual);
        Assert.That(((DeltaValue)actual).number, Is.EqualTo(next.number));
        Assert.That(_packer.positionInBits, Is.EqualTo(changed ? 33 : 1));
    }

    [Test]
    public void EarlierDeltaMethodInfoRegistrationKeepsPriority()
    {
        DeltaPacker.RegisterWriter(typeof(ManualDeltaValue), Method(nameof(WriteManualDelta)));
        DeltaPacker.RegisterReader(typeof(ManualDeltaValue), Method(nameof(ReadManualDelta)));
        DeltaPacker<ManualDeltaValue>.Register(WriteIgnoredDelta, ReadIgnoredDelta);
        var previous = default(ManualDeltaValue);
        Assert.That(DeltaPacker.Write(_packer, typeof(ManualDeltaValue), previous, new ManualDeltaValue { number = 103 }), Is.True);
        _packer.ResetPositionAndMode(true);
        object actual = previous;
        DeltaPacker.Read(_packer, typeof(ManualDeltaValue), previous, ref actual);
        Assert.That(((ManualDeltaValue)actual).number, Is.EqualTo(103u));
    }

    private static IDictionary Registry(Type type, string field) =>
        (IDictionary)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static MethodInfo Method(string name) => typeof(LazyCodecRegistryTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    private static void WriteValue(BitPacker p, Value v) => p.WriteBits(v.number, 32);
    private static void ReadValue(BitPacker p, ref Value v) => v.number = (uint)p.ReadBits(32);
    private static void WriteManual(BitPacker p, ManualValue v) => p.WriteBits(v.number, 32);
    private static void ReadManual(BitPacker p, ref ManualValue v) => v.number = (uint)p.ReadBits(32);
    private static void WriteIgnoredManual(BitPacker p, ManualValue v) => Assert.Fail("Replaced the first writer");
    private static void ReadIgnoredManual(BitPacker p, ref ManualValue v) => Assert.Fail("Replaced the first reader");
    private static void WriteDelegateFirst(BitPacker p, DelegateFirstValue v) => p.WriteBits(v.number, 32);
    private static void ReadDelegateFirst(BitPacker p, ref DelegateFirstValue v) => v.number = (uint)p.ReadBits(32);
    private static void WriteIgnoredDelegate(BitPacker p, DelegateFirstValue v) => Assert.Fail("Replaced the first writer");
    private static void ReadIgnoredDelegate(BitPacker p, ref DelegateFirstValue v) => Assert.Fail("Replaced the first reader");
    private static void WriteBase(BitPacker p, BaseValue v) => p.WriteBits(v.number, 32);
    private static void ReadBase(BitPacker p, ref BaseValue v) => v = new BaseValue { number = (uint)p.ReadBits(32) };
    private static void WriteDerived(BitPacker p, DerivedValue v) => p.WriteBits(v.number, 32);
    private static void ReadDerived(BitPacker p, ref DerivedValue v) => v = new DerivedValue { number = (uint)p.ReadBits(32) };
    private static bool WriteDelta(BitPacker p, DeltaValue old, DeltaValue v)
    {
        bool changed = old.number != v.number;
        p.WriteBit(changed);
        if (changed) p.WriteBits(v.number, 32);
        return changed;
    }
    private static void ReadDelta(BitPacker p, DeltaValue old, ref DeltaValue v) => v.number = p.ReadBit() ? (uint)p.ReadBits(32) : old.number;
    private static bool WriteManualDelta(BitPacker p, ManualDeltaValue old, ManualDeltaValue v) { p.WriteBits(v.number, 32); return true; }
    private static void ReadManualDelta(BitPacker p, ManualDeltaValue old, ref ManualDeltaValue v) => v.number = (uint)p.ReadBits(32);
    private static bool WriteIgnoredDelta(BitPacker p, ManualDeltaValue old, ManualDeltaValue v) { Assert.Fail("Replaced the first writer"); return false; }
    private static void ReadIgnoredDelta(BitPacker p, ManualDeltaValue old, ref ManualDeltaValue v) => Assert.Fail("Replaced the first reader");
}

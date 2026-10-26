using System;
using NUnit.Framework;
using PurrNet.Packing;

namespace PurrNet.Codegen.Tests
{
    public sealed class GeneratedNativeRegistrationTests
    {
        public struct Value { public uint number; }
        public struct EarlierValue { public uint number; }
        public struct GenericValue { public uint number; }
        public struct ManagedValue { public string text; }
        public struct NativeOverrideValue { public uint number; }

        // Editor-only: generated player registration supplies ldftn directly.
        static IntPtr Pointer(Delegate callback) => callback.Method.MethodHandle.GetFunctionPointer();

        [Test]
        public void GeneratedPointersInteroperateWithNormalAndDeltaDelegates()
        {
            WriteFunc<Value> write = Write;
            ReadFunc<Value> read = Read;
            DeltaWriteFunc<Value> deltaWrite = WriteDelta;
            DeltaReadFunc<Value> deltaRead = ReadDelta;
            NativePacker<Value>.RegisterGeneratedWriter(write, Pointer(write));
            NativePacker<Value>.RegisterGeneratedReader(read, Pointer(read));
            NativeDeltaPacker<Value>.RegisterGeneratedWriter(deltaWrite, Pointer(deltaWrite));
            NativeDeltaPacker<Value>.RegisterGeneratedReader(deltaRead, Pointer(deltaRead));
            Packer<Value>.RegisterWriter(write);
            Packer<Value>.RegisterReader(read);
            DeltaPacker<Value>.Register(deltaWrite, deltaRead);

            using var packer = BitPackerPool.Get();
            var expected = new Value { number = 0xFEDCBA98 };
            NativePacker<Value>.Write(packer, expected);
            packer.ResetPositionAndMode(true);
            var actual = default(Value);
            Packer<Value>.Read(packer, ref actual);
            Assert.That(actual.number, Is.EqualTo(expected.number));

            packer.ResetPositionAndMode(false);
            Packer<Value>.Write(packer, expected);
            packer.ResetPositionAndMode(true);
            actual = default;
            NativePacker<Value>.Read(packer, ref actual);
            Assert.That(actual.number, Is.EqualTo(expected.number));

            var baseline = new Value { number = 17 };
            packer.ResetPositionAndMode(false);
            Assert.That(NativeDeltaPacker<Value>.Write(packer, baseline, expected), Is.True);
            packer.ResetPositionAndMode(true);
            actual = default;
            DeltaPacker<Value>.Read(packer, baseline, ref actual);
            Assert.That(actual.number, Is.EqualTo(expected.number));

            packer.ResetPositionAndMode(false);
            Assert.That(DeltaPacker<Value>.Write(packer, baseline, expected), Is.True);
            packer.ResetPositionAndMode(true);
            actual = default;
            NativeDeltaPacker<Value>.Read(packer, baseline, ref actual);
            Assert.That(actual.number, Is.EqualTo(expected.number));
        }

        [Test]
        public void EarlierCustomCallbacksKeepPriorityOverGeneratedPointers()
        {
            Packer<EarlierValue>.RegisterWriter(WriteEarlier);
            Packer<EarlierValue>.RegisterReader(ReadEarlier);
            DeltaPacker<EarlierValue>.Register(WriteEarlierDelta, ReadEarlierDelta);
            WriteFunc<EarlierValue> replacement = WriteReplacement;
            ReadFunc<EarlierValue> replacementRead = ReadReplacement;
            DeltaWriteFunc<EarlierValue> replacementDelta = WriteReplacementDelta;
            DeltaReadFunc<EarlierValue> replacementDeltaRead = ReadReplacementDelta;
            NativePacker<EarlierValue>.RegisterGeneratedWriter(replacement, Pointer(replacement));
            NativePacker<EarlierValue>.RegisterGeneratedReader(replacementRead, Pointer(replacementRead));
            NativeDeltaPacker<EarlierValue>.RegisterGeneratedWriter(replacementDelta, Pointer(replacementDelta));
            NativeDeltaPacker<EarlierValue>.RegisterGeneratedReader(replacementDeltaRead, Pointer(replacementDeltaRead));

            using var packer = BitPackerPool.Get();
            var expected = new EarlierValue { number = 123 };
            NativePacker<EarlierValue>.Write(packer, expected);
            packer.ResetPositionAndMode(true);
            Assert.That(packer.ReadBits(32), Is.EqualTo(123UL));
            packer.ResetPositionAndMode(true);
            var actual = default(EarlierValue);
            NativePacker<EarlierValue>.Read(packer, ref actual);
            Assert.That(actual.number, Is.EqualTo(123));

            packer.ResetPositionAndMode(false);
            Assert.That(NativeDeltaPacker<EarlierValue>.Write(packer, default, expected), Is.True);
            packer.ResetPositionAndMode(true);
            actual = default;
            NativeDeltaPacker<EarlierValue>.Read(packer, default, ref actual);
            Assert.That(actual.number, Is.EqualTo(123));
        }

        [Test]
        public void ReferenceContainingValuesKeepTheirManagedCallbacks()
        {
            NativePacker<ManagedValue>.RegisterGeneratedWriter(WriteManaged, IntPtr.Zero);
            NativePacker<ManagedValue>.RegisterGeneratedReader(ReadManaged, IntPtr.Zero);
            NativeDeltaPacker<ManagedValue>.RegisterGeneratedWriter(WriteManagedDelta, IntPtr.Zero);
            NativeDeltaPacker<ManagedValue>.RegisterGeneratedReader(ReadManagedDelta, IntPtr.Zero);
            Assert.That(NativePacker<ManagedValue>.HasPacker(), Is.False);
            Assert.That(NativeDeltaPacker<ManagedValue>.HasPacker(), Is.False);
            Packer<ManagedValue>.RegisterWriter(WriteManaged);
            Packer<ManagedValue>.RegisterReader(ReadManaged);
            using var packer = BitPackerPool.Get();
            Packer<ManagedValue>.Write(packer, new ManagedValue { text = "xxxx" });
            packer.ResetPositionAndMode(true);
            var actual = default(ManagedValue);
            Packer<ManagedValue>.Read(packer, ref actual);
            Assert.That(actual.text, Is.EqualTo("xxxx"));
        }

        [Test]
        public void ClosedGenericCodecPointersKeepTheirTypeContext()
        {
            WriteFunc<GenericValue> write = GenericCodec<int>.Write;
            ReadFunc<GenericValue> read = GenericCodec<int>.Read;
            NativePacker<GenericValue>.RegisterGeneratedWriter(write, Pointer(write));
            NativePacker<GenericValue>.RegisterGeneratedReader(read, Pointer(read));
            using var packer = BitPackerPool.Get();
            NativePacker<GenericValue>.Write(packer, new GenericValue { number = 123 });
            packer.ResetPositionAndMode(true);
            Assert.That(packer.ReadBits(32), Is.EqualTo(123UL ^ 17UL));
            packer.ResetPositionAndMode(true);
            var actual = default(GenericValue);
            NativePacker<GenericValue>.Read(packer, ref actual);
            Assert.That(actual.number, Is.EqualTo(123));
        }

        [Test]
        public void IndependentNativeOverrideKeepsPriorityOverGeneratedAndManagedRegistration()
        {
            NativePacker<NativeOverrideValue>.RegisterWriter(WriteNativeOverride);
            NativePacker<NativeOverrideValue>.RegisterGeneratedWriter(WriteNativeDefault, Pointer((WriteFunc<NativeOverrideValue>)WriteNativeDefault));
            Packer<NativeOverrideValue>.RegisterWriter(WriteNativeDefault);
            using var packer = BitPackerPool.Get();
            NativePacker<NativeOverrideValue>.Write(packer, new NativeOverrideValue { number = 41 });
            packer.ResetPositionAndMode(true);
            Assert.That(packer.ReadBits(32), Is.EqualTo(42UL));
            packer.ResetPositionAndMode(false);
            Packer<NativeOverrideValue>.Write(packer, new NativeOverrideValue { number = 41 });
            packer.ResetPositionAndMode(true);
            Assert.That(packer.ReadBits(32), Is.EqualTo(41UL));
        }

        static void WriteNativeDefault(BitPacker packer, NativeOverrideValue value) => packer.WriteBits(value.number, 32);
        static void WriteNativeOverride(BitPacker packer, NativeOverrideValue value) => packer.WriteBits(value.number + 1, 32);

        static void Write(BitPacker packer, Value value) => packer.WriteBits(value.number, 32);
        static void Read(BitPacker packer, ref Value value) => value.number = (uint)packer.ReadBits(32);
        static bool WriteDelta(BitPacker packer, Value previous, Value value)
        { packer.WriteBits(unchecked(value.number - previous.number), 32); return previous.number != value.number; }
        static void ReadDelta(BitPacker packer, Value previous, ref Value value)
            => value.number = unchecked(previous.number + (uint)packer.ReadBits(32));
        static void WriteEarlier(BitPacker packer, EarlierValue value) => packer.WriteBits(value.number, 32);
        static void ReadEarlier(BitPacker packer, ref EarlierValue value) => value.number = (uint)packer.ReadBits(32);
        static bool WriteEarlierDelta(BitPacker packer, EarlierValue previous, EarlierValue value)
        { WriteEarlier(packer, value); return true; }
        static void ReadEarlierDelta(BitPacker packer, EarlierValue previous, ref EarlierValue value) => ReadEarlier(packer, ref value);
        static void WriteReplacement(BitPacker packer, EarlierValue value) => packer.WriteBits(value.number + 1, 32);
        static void ReadReplacement(BitPacker packer, ref EarlierValue value) => value.number = (uint)packer.ReadBits(32) + 2;
        static bool WriteReplacementDelta(BitPacker packer, EarlierValue previous, EarlierValue value)
        { WriteReplacement(packer, value); return false; }
        static void ReadReplacementDelta(BitPacker packer, EarlierValue previous, ref EarlierValue value) => ReadReplacement(packer, ref value);
        static void WriteManaged(BitPacker packer, ManagedValue value) => packer.WriteBits((uint)value.text.Length, 32);
        static void ReadManaged(BitPacker packer, ref ManagedValue value) => value.text = new string('x', (int)packer.ReadBits(32));
        static bool WriteManagedDelta(BitPacker packer, ManagedValue previous, ManagedValue value)
        { WriteManaged(packer, value); return true; }
        static void ReadManagedDelta(BitPacker packer, ManagedValue previous, ref ManagedValue value) => ReadManaged(packer, ref value);

        class GenericCodec<T>
        {
            public static void Write(BitPacker packer, GenericValue value)
                => packer.WriteBits(value.number ^ (typeof(T) == typeof(int) ? 17U : 23U), 32);
            public static void Read(BitPacker packer, ref GenericValue value)
                => value.number = (uint)packer.ReadBits(32) ^ (typeof(T) == typeof(int) ? 17U : 23U);
        }
    }
}

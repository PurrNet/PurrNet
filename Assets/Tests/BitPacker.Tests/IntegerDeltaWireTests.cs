using System;
using NUnit.Framework;
using PurrNet.Packing;

namespace PurrNet.Tests
{
    public sealed class IntegerDeltaWireTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void ChangedAndUnchangedValuesKeepTheExistingWireAtEveryOrigin(int origin)
        {
            CheckType<sbyte>(origin, 8, x => unchecked((sbyte)x), x => unchecked((ulong)x), DeltaPackInteger.WriteInt8, DeltaPackInteger.ReadInt8);
            CheckType<byte>(origin, 8, x => unchecked((byte)x), x => x, DeltaPackInteger.WriteUInt8, DeltaPackInteger.ReadUInt8);
            CheckType<short>(origin, 16, x => unchecked((short)x), x => unchecked((ulong)x), DeltaPackInteger.WriteInt16, DeltaPackInteger.ReadInt16);
            CheckType<ushort>(origin, 16, x => unchecked((ushort)x), x => x, DeltaPackInteger.WriteUInt16, DeltaPackInteger.ReadUInt16);
            CheckType<int>(origin, 32, x => unchecked((int)x), x => unchecked((ulong)x), DeltaPackInteger.WriteInt32, DeltaPackInteger.ReadInt32);
            CheckType<uint>(origin, 32, x => unchecked((uint)x), x => x, DeltaPackInteger.WriteUInt32, DeltaPackInteger.ReadUInt32);
            CheckType<long>(origin, 64, x => unchecked((long)x), x => unchecked((ulong)x), DeltaPackInteger.WriteInt64, DeltaPackInteger.ReadInt64);
            CheckType<ulong>(origin, 64, x => x, x => x, DeltaPackInteger.WriteUInt64, DeltaPackInteger.ReadUInt64);
        }

        [Test]
        public void ValuesEndingAtTheBufferEdgeDecodeFromTheirExactBytes()
        {
            var random = new Random(7);
            var baselines = new int[96];
            var values = new int[96];
            var shortBaselines = new short[96];
            var shortValues = new short[96];
            for (int i = 0; i < values.Length; i++)
            {
                baselines[i] = random.Next(int.MinValue, int.MaxValue);
                values[i] = (i % 3) switch
                {
                    0 => baselines[i],
                    1 => unchecked(baselines[i] + random.Next(-20, 20)),
                    _ => random.Next(int.MinValue, int.MaxValue)
                };
                shortBaselines[i] = (short)random.Next(short.MinValue, short.MaxValue);
                shortValues[i] = i % 2 == 0 ? shortBaselines[i] : (short)random.Next(short.MinValue, short.MaxValue);
            }

            using var writer = BitPackerPool.Get();
            for (int i = 0; i < values.Length; i++)
            {
                DeltaPackInteger.WriteInt32(writer, baselines[i], values[i]);
                DeltaPackInteger.WriteInt16(writer, shortBaselines[i], shortValues[i]);
            }

            var bytes = new byte[writer.positionInBytes];
            Array.Copy(writer.buffer, bytes, bytes.Length);
            using var reader = BitPackerPool.Get(bytes);
            for (int i = 0; i < values.Length; i++)
            {
                int decoded = 0;
                short decodedShort = 0;
                DeltaPackInteger.ReadInt32(reader, baselines[i], ref decoded);
                DeltaPackInteger.ReadInt16(reader, shortBaselines[i], ref decodedShort);
                Assert.That(decoded, Is.EqualTo(values[i]), $"int {i}");
                Assert.That(decodedShort, Is.EqualTo(shortValues[i]), $"short {i}");
            }
            Assert.That(reader.positionInBits, Is.EqualTo(writer.positionInBits));
        }

        static void CheckType<T>(int origin, int width, Func<ulong, T> fromBits, Func<T, ulong> toBits,
            DeltaWriteFunc<T> write, DeltaReadFunc<T> read) where T : struct
        {
            for (int bit = 0; bit < width; bit++)
            {
                ulong boundary = 1UL << bit;
                Check(origin, width, fromBits(0), fromBits(boundary - 1), toBits, write, read);
                Check(origin, width, fromBits(boundary), fromBits(0), toBits, write, read);
                Check(origin, width, fromBits(ulong.MaxValue), fromBits(boundary + 1), toBits, write, read);
            }
            Check(origin, width, fromBits(0), fromBits(0), toBits, write, read);
            Check(origin, width, fromBits(ulong.MaxValue), fromBits(ulong.MaxValue), toBits, write, read);
        }

        static void Check<T>(int origin, int width, T baseline, T current, Func<T, ulong> toBits,
            DeltaWriteFunc<T> write, DeltaReadFunc<T> read) where T : struct
        {
            ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
            ulong before = toBits(baseline) & mask, after = toBits(current) & mask;
            using var expected = BitPackerPool.Get();
            using var actual = BitPackerPool.Get();
            for (int block = 0; block < 3; block++)
            {
                expected.WriteBits(0xA5A5A5A5A5A5A5A5, 64);
                actual.WriteBits(0xA5A5A5A5A5A5A5A5, 64);
            }
            expected.SetBitPosition(origin);
            actual.SetBitPosition(origin);
            WriteLegacy(expected, before, after, width);
            Assert.That(write(actual, baseline, current), Is.EqualTo(before != after));
            int end = expected.positionInBits;
            Assert.That(actual.positionInBits, Is.EqualTo(end));
            Assert.That(new BitData(actual, 0, 192).Equals(new BitData(expected, 0, 192)), Is.True,
                $"{typeof(T).Name}: {before} -> {after}, origin={origin}");

            expected.ResetMode(true);
            expected.SetBitPosition(origin);
            T decoded = default;
            read(expected, baseline, ref decoded);
            Assert.That(toBits(decoded) & mask, Is.EqualTo(after));
            Assert.That(expected.positionInBits, Is.EqualTo(end));
            actual.ResetMode(true);
            actual.SetBitPosition(origin);
            Assert.That(ReadLegacy(actual, before, width) & mask, Is.EqualTo(after));
            Assert.That(actual.positionInBits, Is.EqualTo(end));
        }

        // Frozen protocol: change bit, two-bit selector, then the selected zigzag payload.
        static void WriteLegacy(BitPacker packer, ulong before, ulong after, int width)
        {
            packer.WriteBit(before != after);
            if (before == after) return;
            ulong difference = unchecked(after - before);
            long signed = width == 64 ? unchecked((long)difference) : unchecked((long)(difference << (64 - width))) >> (64 - width);
            ulong zigzag = unchecked((ulong)(signed << 1) ^ (ulong)(signed >> 63));
            if (width != 64) zigzag &= (1UL << width) - 1;
            byte[] buckets = Buckets(width);
            int selector = 0;
            while (buckets[selector] < 64 && zigzag >= (1UL << buckets[selector])) selector++;
            packer.WriteBits((ulong)selector, 2);
            packer.WriteBits(zigzag, buckets[selector]);
        }

        static ulong ReadLegacy(BitPacker packer, ulong before, int width)
        {
            if (!packer.ReadBit()) return before;
            byte bits = Buckets(width)[(int)packer.ReadBits(2)];
            ulong zigzag = packer.ReadBits(bits);
            long difference = unchecked((long)(zigzag >> 1)) ^ -unchecked((long)(zigzag & 1));
            return unchecked(before + (ulong)difference);
        }

        static readonly byte[] Eight = { 2, 4, 6, 8 }, Sixteen = { 4, 8, 12, 16 },
            ThirtyTwo = { 4, 8, 16, 32 }, SixtyFour = { 4, 8, 24, 64 };
        static byte[] Buckets(int width) => width == 8 ? Eight : width == 16 ? Sixteen : width == 32 ? ThirtyTwo : SixtyFour;
    }
}

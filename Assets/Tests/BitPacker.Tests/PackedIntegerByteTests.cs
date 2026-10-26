using NUnit.Framework;
using PurrNet.Packing;

namespace PurrNet.Tests
{
    public sealed class PackedIntegerByteTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void BytesMatchLegacyCodecAtEveryBoundaryAndUnalignedOrigin(int origin)
        {
            for (int bits = 0; bits < 64; bits++)
            {
                ulong boundary = 1UL << bits;
                Check(boundary - 1, origin);
                Check(boundary, origin);
                Check(boundary + 1, origin);
            }
            Check(ulong.MaxValue, origin);
            ulong random = 0xC0FFEE123456789UL;
            for (int sample = 0; sample < 256; sample++)
            {
                random ^= random << 13;
                random ^= random >> 7;
                random ^= random << 17;
                Check(random, origin);
            }
        }

        [Test]
        public void UnterminatedAndOverflowingEncodingsKeepTheLegacyTenByteLimit()
        {
            foreach (byte last in new byte[] { 0, 1, 0x7F, 0x80, 0xFF })
            for (int origin = 0; origin < 8; origin++)
            {
                using var expected = BitPackerPool.Get();
                using var actual = BitPackerPool.Get();
                expected.WriteBits(0x55, (byte)origin);
                for (int i = 0; i < 9; i++) expected.WriteBits(0x80, 8);
                expected.WriteBits(last, 8);
                expected.WriteBits(0x15A73, 17);
                actual.WriteBitsWithoutConsumingIt(expected, expected.positionInBits);
                expected.ResetPositionAndMode(true);
                actual.ResetPositionAndMode(true);
                ulong legacy = ReadLegacy(origin, expected);
                actual.SetBitPosition(origin);
                PackedULong decoded = default;
                PackingIntegers.Read(actual, ref decoded);
                Assert.That(decoded.value, Is.EqualTo(legacy));
                Assert.That(actual.positionInBits, Is.EqualTo(expected.positionInBits));
                Assert.That(actual.ReadBits(17), Is.EqualTo(0x15A73UL));
            }
        }

        [Test]
        public void WritesPreserveExistingBitsAroundTheEncodedValue()
        {
            foreach (ulong value in new ulong[] { 0, 127, 128, 16383, 16384, 1UL << 56, 1UL << 63, ulong.MaxValue })
            for (int origin = 0; origin < 8; origin++)
            {
                using var expected = BitPackerPool.Get();
                using var actual = BitPackerPool.Get();
                for (int block = 0; block < 3; block++)
                {
                    expected.WriteBits(0xA5A5A5A5A5A5A5A5UL, 64);
                    actual.WriteBits(0xA5A5A5A5A5A5A5A5UL, 64);
                }
                expected.SetBitPosition(origin);
                actual.SetBitPosition(origin);
                WriteLegacy(value, expected);
                PackingIntegers.Write(actual, new PackedULong(value));
                Assert.That(actual.positionInBits, Is.EqualTo(expected.positionInBits));
                Assert.That(new BitData(actual, 0, 192).Equals(new BitData(expected, 0, 192)), Is.True,
                    $"value {value}, origin {origin}");
            }
        }

        private static void Check(ulong value, int origin)
        {
            using var expected = BitPackerPool.Get();
            using var actual = BitPackerPool.Get();
            expected.WriteBits(0x55, (byte)origin);
            actual.WriteBits(0x55, (byte)origin);
            WriteLegacy(value, expected);
            PackingIntegers.Write(actual, new PackedULong(value));
            expected.WriteBits(0x15A73, 17);
            actual.WriteBits(0x15A73, 17);
            Assert.That(actual.positionInBits, Is.EqualTo(expected.positionInBits));
            Assert.That(new BitData(actual, 0, actual.positionInBits).Equals(
                new BitData(expected, 0, expected.positionInBits)), Is.True,
                $"value {value}, origin {origin}");

            expected.ResetPositionAndMode(true);
            actual.ResetPositionAndMode(true);
            expected.SetBitPosition(origin);
            PackedULong decoded = default;
            PackingIntegers.Read(expected, ref decoded);
            Assert.That(decoded.value, Is.EqualTo(value));
            Assert.That(expected.ReadBits(17), Is.EqualTo(0x15A73UL));
            Assert.That(ReadLegacy(origin, actual), Is.EqualTo(value));
            Assert.That(actual.ReadBits(17), Is.EqualTo(0x15A73UL));
        }

        // Keep BitPacker after the value so codegen cannot discover a test codec.
        private static void WriteLegacy(ulong value, BitPacker packer)
        {
            do
            {
                packer.WriteBits(value & 0x7FUL, 7);
                value >>= 7;
                packer.WriteBits(value == 0 ? 0UL : 1UL, 1);
            } while (value != 0);
        }

        private static ulong ReadLegacy(int origin, BitPacker packer)
        {
            packer.SetBitPosition(origin);
            ulong result = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                ulong chunk = packer.ReadBits(7);
                bool continuation = packer.ReadBits(1) != 0;
                result |= chunk << shift;
                if (!continuation) break;
            }
            return result;
        }
    }
}

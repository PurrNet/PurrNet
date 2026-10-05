using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Tests
{
    public sealed class GeneratedEqualityTests
    {
        [SetUp]
        public void RegisterGeneratedComparers()
        {
            NetworkManager.CallAllRegisters();
        }

        [Test]
        public void ManagedStatePreservesTinyVectorDifferenceWrittenByTheSerializer()
        {
            AssertGenerated<GeneratedEqualityVectorState>();
            var left = new GeneratedEqualityVectorState { label = "same", position = Vector3.zero };
            var right = new GeneratedEqualityVectorState
            {
                label = "same",
                position = new Vector3(0.000001f, 0f, 0f)
            };

            Assert.That(left.position == right.position, Is.True, "This exercises Unity's approximate operator.");
            using var bits = BitPackerPool.Get();
            Packer<GeneratedEqualityVectorState>.Write(bits, right);
            bits.ResetPositionAndMode(true);
            GeneratedEqualityVectorState decoded = default;
            Packer<GeneratedEqualityVectorState>.Read(bits, ref decoded);
            Assert.That(BitConverter.SingleToInt32Bits(decoded.position.x),
                Is.EqualTo(BitConverter.SingleToInt32Bits(right.position.x)));

            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            Assert.That(Packer.AreEqualRef(ref right, ref decoded), Is.True);
        }

        [Test]
        public void NestedManagedFieldsUseTheirGeneratedComparer()
        {
            AssertGenerated<GeneratedEqualityNestedState>();
            AssertGenerated<GeneratedEqualityVectorState>();
            var left = new GeneratedEqualityNestedState
            {
                label = "outer",
                inner = new GeneratedEqualityVectorState { label = "inner", position = Vector3.zero }
            };
            var right = left;
            right.label = new string(left.label.ToCharArray());
            right.inner.label = new string(left.inner.label.ToCharArray());
            Assert.That(ReferenceEquals(left.label, right.label), Is.False);
            Assert.That(ReferenceEquals(left.inner.label, right.inner.label), Is.False);
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
            right.inner.position.x = 0.000001f;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            right = left;
            right.inner.label = "changed";
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
        }

        [Test]
        public void GeneratedClassComparesReadonlyFieldsAndPreservesNullGuards()
        {
            AssertGenerated<GeneratedEqualityClassState>();
            var left = new GeneratedEqualityClassState(Vector3.zero) { label = "same" };
            var right = new GeneratedEqualityClassState(Vector3.zero) { label = "same" };
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
            right = new GeneratedEqualityClassState(new Vector3(0.000001f, 0f, 0f)) { label = "same" };
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            right = null;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            left = null;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
        }

        [Test]
        public void ClosedGenericFieldsHandleBothUnmanagedAndManagedValues()
        {
            AssertGenerated<GeneratedEqualityGenericUses>();
            AssertGenerated<GeneratedEqualityGenericState<Vector3>>();
            AssertGenerated<GeneratedEqualityGenericState<GeneratedEqualityVectorState>>();
            var leftValue = new GeneratedEqualityGenericState<Vector3> { label = "same", value = Vector3.zero };
            var rightValue = leftValue;
            Assert.That(Packer.AreEqualRef(ref leftValue, ref rightValue), Is.True);
            rightValue.value.x = 0.000001f;
            Assert.That(Packer.AreEqualRef(ref leftValue, ref rightValue), Is.False);

            var leftManaged = new GeneratedEqualityGenericState<GeneratedEqualityVectorState>
            {
                label = "outer",
                value = new GeneratedEqualityVectorState { label = "inner", position = Vector3.zero }
            };
            var rightManaged = leftManaged;
            rightManaged.value.label = new string(leftManaged.value.label.ToCharArray());
            Assert.That(ReferenceEquals(leftManaged.value.label, rightManaged.value.label), Is.False);
            Assert.That(Packer.AreEqualRef(ref leftManaged, ref rightManaged), Is.True);
            rightManaged.value.position.x = 0.000001f;
            Assert.That(Packer.AreEqualRef(ref leftManaged, ref rightManaged), Is.False);
            rightManaged = leftManaged;
            rightManaged.value.label = "changed";
            Assert.That(Packer.AreEqualRef(ref leftManaged, ref rightManaged), Is.False);
        }

        [TestCase(0, int.MinValue, false)]
        [TestCase(0x7fc00001, 0x7fc00001, true)]
        [TestCase(0x7fc00001, 0x7fc00002, false)]
        public void PrimitiveFloatFieldsAndUnmanagedHelperCompareTheirBits(int leftBits, int rightBits, bool expected)
        {
            AssertGenerated<GeneratedEqualityFloatState>();
            var left = new GeneratedEqualityFloatState { label = "same", value = BitConverter.Int32BitsToSingle(leftBits) };
            var right = new GeneratedEqualityFloatState { label = "same", value = BitConverter.Int32BitsToSingle(rightBits) };

            Assert.That(Packer.AreEqualUnmanagedRef(ref left.value, ref right.value), Is.EqualTo(expected));
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.EqualTo(expected));
        }

        [TestCase(int.MinValue, long.MinValue)]
        [TestCase(int.MaxValue, long.MaxValue)]
        public void ScalarFieldsCompareExactValuesAcrossIntegerBoundaries(int value, long wideValue)
        {
            AssertGenerated<GeneratedEqualityScalarState>();
            var left = new GeneratedEqualityScalarState
            {
                label = "same", value = value, wideValue = wideValue, flag = true, character = char.MaxValue
            };
            var right = left;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);

            right.value ^= 1;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            right = left;
            right.wideValue ^= 1L << 32;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False, "All 64 bits must participate.");
            right = left;
            right.flag = false;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            right = left;
            right.character = char.MinValue;
            Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
        }

        [Test]
        public void ReferenceFieldsUseTheCurrentComparerIncludingNullValues()
        {
            AssertGenerated<GeneratedEqualityReferenceState>();
            var previous = PurrEquality<GeneratedEqualityReferenceValue>.Default;
            var comparer = new ReferenceComparer();
            var left = new GeneratedEqualityReferenceState { value = new GeneratedEqualityReferenceValue { key = 1 } };
            var right = new GeneratedEqualityReferenceState { value = new GeneratedEqualityReferenceValue { key = 2 } };
            try
            {
                PurrEquality<GeneratedEqualityReferenceValue>.OverrideDefault(comparer);
                Assert.That(left.value == right.value, Is.True, "The operator deliberately has coarse equality.");
                Assert.That(left.value.Equals(right.value), Is.True, "Typed Equals is also deliberately coarse.");
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
                right.value.key = 1;
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
                right.value = null;
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
                left.value = null;
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
                Assert.That(comparer.calls, Is.EqualTo(4));
            }
            finally
            {
                PurrEquality<GeneratedEqualityReferenceValue>.OverrideDefault(previous);
            }
        }

        [Test]
        public void UnmanagedFieldsKeepMemoryComparisonDespiteARegisteredComparer()
        {
            AssertGenerated<GeneratedEqualityVectorState>();
            var previous = PurrEquality<Vector3>.Default;
            try
            {
                PurrEquality<Vector3>.OverrideDefault(new ThrowingVectorComparer());
                var left = new GeneratedEqualityVectorState { label = "same", position = Vector3.zero };
                var right = left;
                Assert.That(Packer.AreEqualUnmanagedRef(ref left.position, ref right.position), Is.True);
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.True);
                right.position.x = 0.000001f;
                Assert.That(Packer.AreEqualUnmanagedRef(ref left.position, ref right.position), Is.False);
                Assert.That(Packer.AreEqualRef(ref left, ref right), Is.False);
            }
            finally
            {
                PurrEquality<Vector3>.OverrideDefault(previous);
            }
        }

        private static void AssertGenerated<T>()
        {
            Assert.That(typeof(IPurrEquatable<T>).IsAssignableFrom(typeof(T)), Is.True,
                $"{typeof(T)} must be processed by the real IL postprocessor, not a test comparer.");
            Assert.That(typeof(T).GetMethod("PurrEquals"), Is.Not.Null);
        }

        private sealed class ReferenceComparer : IEqualityComparer<GeneratedEqualityReferenceValue>
        {
            public int calls;

            public bool Equals(GeneratedEqualityReferenceValue left, GeneratedEqualityReferenceValue right)
            {
                calls++;
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null) return false;
                return left.key == right.key;
            }

            public int GetHashCode(GeneratedEqualityReferenceValue value) => value?.key ?? 0;
        }

        private sealed class ThrowingVectorComparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 left, Vector3 right)
                => throw new InvalidOperationException("Unmanaged fields must use memory equality.");

            public int GetHashCode(Vector3 value) => value.GetHashCode();
        }
    }

    // This runtime test assembly is postprocessed. Editor-only fixtures would not exercise generated IL.
    public struct GeneratedEqualityVectorState : IPackedAuto
    {
        public string label;
        public Vector3 position;
    }

    public struct GeneratedEqualityNestedState : IPackedAuto
    {
        public string label;
        public GeneratedEqualityVectorState inner;
    }

    public sealed class GeneratedEqualityClassState : IPackedAuto
    {
        public string label;
        public readonly Vector3 position;

        public GeneratedEqualityClassState() { }
        public GeneratedEqualityClassState(Vector3 position) => this.position = position;
    }

    public struct GeneratedEqualityGenericState<T> : IPackedAuto
    {
        public string label;
        public T value;
    }

    // Closed field types make both instantiations visible to ordinary serializer discovery.
    public struct GeneratedEqualityGenericUses : IPackedAuto
    {
        public GeneratedEqualityGenericState<Vector3> vector;
        public GeneratedEqualityGenericState<GeneratedEqualityVectorState> managed;
    }

    public struct GeneratedEqualityFloatState : IPackedAuto
    {
        public string label;
        public float value;
    }

    public struct GeneratedEqualityScalarState : IPackedAuto
    {
        public string label;
        public int value;
        public long wideValue;
        public bool flag;
        public char character;
    }

    public struct GeneratedEqualityReferenceState : IPackedAuto
    {
        public GeneratedEqualityReferenceValue value;
    }

    public sealed class GeneratedEqualityReferenceValue : IPackedAuto, IEquatable<GeneratedEqualityReferenceValue>
    {
        public int key;

        public static bool operator ==(GeneratedEqualityReferenceValue left, GeneratedEqualityReferenceValue right) => true;
        public static bool operator !=(GeneratedEqualityReferenceValue left, GeneratedEqualityReferenceValue right) => false;
        public bool Equals(GeneratedEqualityReferenceValue other) => true;
        public override bool Equals(object other) => other is GeneratedEqualityReferenceValue;
        public override int GetHashCode() => 0;
    }
}

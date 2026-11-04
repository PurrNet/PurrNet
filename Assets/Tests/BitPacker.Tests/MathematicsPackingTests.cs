using NUnit.Framework;
using PurrNet.Packing;
using PurrNet.Utils;
using Unity.Mathematics;

public struct MathematicsState : IPackedAuto
{
    public float3 position;
    public quaternion rotation;
    public int2 cell;
}

public class MathematicsPackingTests
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

    T RoundTrip<T>(T value)
    {
        packer.ResetPositionAndMode(false);
        Packer<T>.Write(packer, value);
        packer.ResetPositionAndMode(true);
        T result = default;
        Packer<T>.Read(packer, ref result);
        return result;
    }

    T DeltaRoundTrip<T>(T oldValue, T newValue, out bool changed)
    {
        packer.ResetPositionAndMode(false);
        changed = DeltaPacker<T>.Write(packer, oldValue, newValue);
        packer.ResetPositionAndMode(true);
        T result = oldValue;
        DeltaPacker<T>.Read(packer, oldValue, ref result);
        return result;
    }

    [Test]
    public void TypesAreRegisteredWithHasher()
    {
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<quaternion>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<float2>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<float3>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<float4>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<double2>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<double4>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<int2>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<int3>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<int4>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<uint2>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<uint3>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<uint4>());
        Assert.DoesNotThrow(() => Hasher.GetStableHashU32<MathematicsState>());
    }

    [Test]
    public void RoundTripsValues()
    {
        Assert.That(RoundTrip(new float2(1.5f, -2f)), Is.EqualTo(new float2(1.5f, -2f)));
        Assert.That(RoundTrip(new float3(1.5f, -2f, 3.25f)), Is.EqualTo(new float3(1.5f, -2f, 3.25f)));
        Assert.That(RoundTrip(new float4(1, 2, 3, 4)), Is.EqualTo(new float4(1, 2, 3, 4)));
        Assert.That(RoundTrip(new double2(1.5, -2)), Is.EqualTo(new double2(1.5, -2)));
        Assert.That(RoundTrip(new double3(1.5, -2, 9)), Is.EqualTo(new double3(1.5, -2, 9)));
        Assert.That(RoundTrip(new double4(1, 2, 3, 4)), Is.EqualTo(new double4(1, 2, 3, 4)));
        Assert.That(RoundTrip(new int2(-7, 9)), Is.EqualTo(new int2(-7, 9)));
        Assert.That(RoundTrip(new int3(-7, 9, int.MaxValue)), Is.EqualTo(new int3(-7, 9, int.MaxValue)));
        Assert.That(RoundTrip(new int4(-7, 9, 0, int.MinValue)), Is.EqualTo(new int4(-7, 9, 0, int.MinValue)));
        Assert.That(RoundTrip(new uint2(7, 9)), Is.EqualTo(new uint2(7, 9)));
        Assert.That(RoundTrip(new uint3(7, 9, uint.MaxValue)), Is.EqualTo(new uint3(7, 9, uint.MaxValue)));
        Assert.That(RoundTrip(new uint4(7, 9, 0, 1)), Is.EqualTo(new uint4(7, 9, 0, 1)));

        var rotation = quaternion.Euler(0.3f, 1.2f, -0.7f);
        Assert.That(RoundTrip(rotation), Is.EqualTo(rotation));
    }

    [Test]
    public void DeltaRoundTripsValues()
    {
        var oldRotation = quaternion.Euler(0.3f, 1.2f, -0.7f);
        var newRotation = quaternion.Euler(0.1f, -0.4f, 2f);

        Assert.That(DeltaRoundTrip(oldRotation, newRotation, out var changed), Is.EqualTo(newRotation));
        Assert.That(changed, Is.True);
        Assert.That(DeltaRoundTrip(oldRotation, oldRotation, out changed), Is.EqualTo(oldRotation));
        Assert.That(changed, Is.False);

        Assert.That(DeltaRoundTrip(new float2(1, 2), new float2(1, 5), out _), Is.EqualTo(new float2(1, 5)));
        Assert.That(DeltaRoundTrip(new float3(1, 2, 3), new float3(4, 2, 3), out _), Is.EqualTo(new float3(4, 2, 3)));
        Assert.That(DeltaRoundTrip(new float4(1, 2, 3, 4), new float4(1, 2, 3, 8), out _), Is.EqualTo(new float4(1, 2, 3, 8)));
        Assert.That(DeltaRoundTrip(new double2(1, 2), new double2(3, 2), out _), Is.EqualTo(new double2(3, 2)));
        Assert.That(DeltaRoundTrip(new double4(1, 2, 3, 4), new double4(1, 0, 3, 4), out _), Is.EqualTo(new double4(1, 0, 3, 4)));
        Assert.That(DeltaRoundTrip(new int2(1, 2), new int2(-1, 2), out _), Is.EqualTo(new int2(-1, 2)));
        Assert.That(DeltaRoundTrip(new int3(1, 2, 3), new int3(1, 2, 300), out _), Is.EqualTo(new int3(1, 2, 300)));
        Assert.That(DeltaRoundTrip(new int4(1, 2, 3, 4), new int4(0, 0, 0, 0), out _), Is.EqualTo(new int4(0, 0, 0, 0)));
        Assert.That(DeltaRoundTrip(new uint2(1, 2), new uint2(1, 20), out _), Is.EqualTo(new uint2(1, 20)));
        Assert.That(DeltaRoundTrip(new uint3(1, 2, 3), new uint3(10, 2, 3), out _), Is.EqualTo(new uint3(10, 2, 3)));
        Assert.That(DeltaRoundTrip(new uint4(1, 2, 3, 4), new uint4(1, 2, 30, 4), out _), Is.EqualTo(new uint4(1, 2, 30, 4)));
    }

    [Test]
    public void StructWithQuaternionField()
    {
        var oldState = new MathematicsState
        {
            position = new float3(1, 2, 3),
            rotation = quaternion.Euler(0.3f, 1.2f, -0.7f),
            cell = new int2(4, 5)
        };

        var newState = oldState;
        newState.rotation = quaternion.Euler(0.1f, -0.4f, 2f);

        var copy = RoundTrip(newState);
        Assert.That(copy.position, Is.EqualTo(newState.position));
        Assert.That(copy.rotation, Is.EqualTo(newState.rotation));
        Assert.That(copy.cell, Is.EqualTo(newState.cell));

        var delta = DeltaRoundTrip(oldState, newState, out var changed);
        Assert.That(changed, Is.True);
        Assert.That(delta.position, Is.EqualTo(newState.position));
        Assert.That(delta.rotation, Is.EqualTo(newState.rotation));
        Assert.That(delta.cell, Is.EqualTo(newState.cell));
    }
}

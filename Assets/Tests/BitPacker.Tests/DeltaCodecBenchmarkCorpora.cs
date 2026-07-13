using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

internal readonly struct DeltaBenchmarkPair<T>
{
    public readonly T oldValue;
    public readonly T newValue;

    public DeltaBenchmarkPair(T oldValue, T newValue)
    {
        this.oldValue = oldValue;
        this.newValue = newValue;
    }
}

internal readonly struct DeltaBenchmarkScenario<T>
{
    public readonly string name;
    public readonly DeltaBenchmarkPair<T>[] pairs;
    public readonly int targetOperations;

    public DeltaBenchmarkScenario(string name, DeltaBenchmarkPair<T>[] pairs, int targetOperations)
    {
        this.name = name;
        this.pairs = pairs;
        this.targetOperations = targetOperations;
    }
}

internal static class DeltaCodecBenchmarkCorpora
{
    private const int ScalarPairCount = 256;
    private const int CompositePairCount = 128;
    private const int CollectionPairCount = 16;
    private const int ScalarTargetOperations = 200_000;
    private const int CompositeTargetOperations = 100_000;
    private const int CollectionTargetOperations = 2_000;
    private const int ByteArrayTargetOperations = 3_000;
    private const int ListTargetOperations = 1_000;
    private const int ListStructuralTargetOperations = 512;
    private const int ListHardTargetOperations = 256;
    private const int ListLength = 128;
    private const int DictionaryCount = 128;
    private const int ByteArrayLength = 1200;

    internal static DeltaBenchmarkScenario<int>[] BuildIntScenarios()
    {
        var small = new DeltaBenchmarkPair<int>[ScalarPairCount];
        var mixed = new DeltaBenchmarkPair<int>[ScalarPairCount];
        var random = new DeltaBenchmarkPair<int>[ScalarPairCount];
        var rng = new DeterministicRandom(0xA341316Cu);
        int[] smallDeltas = { 0, 1, -1, 2, -2, 7, -7, 63, -63 };
        int[] mixedDeltas =
        {
            0, 1, -1, 127, -127, 128, -128, 16_383, -16_383,
            16_384, -16_384, 1_048_576, -1_048_576, int.MaxValue
        };

        for (int i = 0; i < ScalarPairCount; i++)
        {
            int oldSmall = rng.NextInt();
            int oldMixed = rng.NextInt();
            small[i] = new DeltaBenchmarkPair<int>(
                oldSmall,
                unchecked(oldSmall + smallDeltas[i % smallDeltas.Length]));
            mixed[i] = new DeltaBenchmarkPair<int>(
                oldMixed,
                unchecked(oldMixed + mixedDeltas[i % mixedDeltas.Length]));
            random[i] = new DeltaBenchmarkPair<int>(rng.NextInt(), rng.NextInt());
        }

        var boundary = new[]
        {
            new DeltaBenchmarkPair<int>(0, 0),
            new DeltaBenchmarkPair<int>(0, 1),
            new DeltaBenchmarkPair<int>(0, -1),
            new DeltaBenchmarkPair<int>(-1, 0),
            new DeltaBenchmarkPair<int>(int.MaxValue, int.MinValue),
            new DeltaBenchmarkPair<int>(int.MinValue, int.MaxValue),
            new DeltaBenchmarkPair<int>(int.MaxValue, int.MaxValue - 1),
            new DeltaBenchmarkPair<int>(int.MinValue, int.MinValue + 1),
            new DeltaBenchmarkPair<int>(127, 128),
            new DeltaBenchmarkPair<int>(128, 127),
            new DeltaBenchmarkPair<int>(16_383, 16_384),
            new DeltaBenchmarkPair<int>(16_384, 16_383),
            new DeltaBenchmarkPair<int>(1 << 20, -(1 << 20)),
            new DeltaBenchmarkPair<int>(1 << 30, -(1 << 30)),
            new DeltaBenchmarkPair<int>(-1_000_000_000, 1_000_000_000)
        };

        return new[]
        {
            Scenario("small", small, ScalarTargetOperations),
            Scenario("mixed", mixed, ScalarTargetOperations),
            Scenario("random", random, ScalarTargetOperations),
            Scenario("boundary", boundary, ScalarTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<long>[] BuildLongScenarios()
    {
        var small = new DeltaBenchmarkPair<long>[ScalarPairCount];
        var mixed = new DeltaBenchmarkPair<long>[ScalarPairCount];
        var random = new DeltaBenchmarkPair<long>[ScalarPairCount];
        var rng = new DeterministicRandom(0xC8013EA4u);
        long[] smallDeltas = { 0L, 1L, -1L, 2L, -2L, 7L, -7L, 63L, -63L };
        long[] mixedDeltas =
        {
            0L, 1L, -1L, 127L, -127L, 16_383L, -16_383L,
            1L << 20, -(1L << 20), 1L << 35, -(1L << 35), long.MaxValue
        };

        for (int i = 0; i < ScalarPairCount; i++)
        {
            long oldSmall = rng.NextLong();
            long oldMixed = rng.NextLong();
            small[i] = new DeltaBenchmarkPair<long>(
                oldSmall,
                unchecked(oldSmall + smallDeltas[i % smallDeltas.Length]));
            mixed[i] = new DeltaBenchmarkPair<long>(
                oldMixed,
                unchecked(oldMixed + mixedDeltas[i % mixedDeltas.Length]));
            random[i] = new DeltaBenchmarkPair<long>(rng.NextLong(), rng.NextLong());
        }

        var boundary = new[]
        {
            new DeltaBenchmarkPair<long>(0L, 0L),
            new DeltaBenchmarkPair<long>(0L, 1L),
            new DeltaBenchmarkPair<long>(0L, -1L),
            new DeltaBenchmarkPair<long>(-1L, 0L),
            new DeltaBenchmarkPair<long>(long.MaxValue, long.MinValue),
            new DeltaBenchmarkPair<long>(long.MinValue, long.MaxValue),
            new DeltaBenchmarkPair<long>(long.MaxValue, long.MaxValue - 1L),
            new DeltaBenchmarkPair<long>(long.MinValue, long.MinValue + 1L),
            new DeltaBenchmarkPair<long>(127L, 128L),
            new DeltaBenchmarkPair<long>(16_383L, 16_384L),
            new DeltaBenchmarkPair<long>(1L << 31, -(1L << 31)),
            new DeltaBenchmarkPair<long>(1L << 48, -(1L << 48)),
            new DeltaBenchmarkPair<long>(-9_007_199_254_740_991L, 9_007_199_254_740_991L)
        };

        return new[]
        {
            Scenario("small", small, ScalarTargetOperations),
            Scenario("mixed", mixed, ScalarTargetOperations),
            Scenario("random", random, ScalarTargetOperations),
            Scenario("boundary", boundary, ScalarTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<float>[] BuildFloatScenarios()
    {
        var smooth = new DeltaBenchmarkPair<float>[ScalarPairCount];
        var ulp = new DeltaBenchmarkPair<float>[ScalarPairCount];
        var random = new DeltaBenchmarkPair<float>[ScalarPairCount];
        var rng = new DeterministicRandom(0xAD90777Du);

        for (int i = 0; i < ScalarPairCount; i++)
        {
            float oldSmooth = (i - 128) * 0.125f + (i % 11) * 17.0f;
            float step = i % 8 == 0 ? 0f : ((i & 1) == 0 ? 0.015625f : -0.03125f);
            smooth[i] = new DeltaBenchmarkPair<float>(oldSmooth, oldSmooth + step);

            float oldUlp = rng.NextFloat(-100_000f, 100_000f);
            int ulpDistance = 1 + i % 4;
            ulp[i] = new DeltaBenchmarkPair<float>(oldUlp, OffsetFloatBits(oldUlp, ulpDistance));
            random[i] = new DeltaBenchmarkPair<float>(NextFiniteFloat(ref rng), NextFiniteFloat(ref rng));
        }

        float negativeZero = FloatFromBits(unchecked((int)0x80000000u));
        float nanOne = FloatFromBits(unchecked((int)0x7FC00001u));
        float nanTwo = FloatFromBits(unchecked((int)0x7FC01234u));
        var edge = new[]
        {
            new DeltaBenchmarkPair<float>(0f, 0f),
            new DeltaBenchmarkPair<float>(0f, negativeZero),
            new DeltaBenchmarkPair<float>(negativeZero, 0f),
            new DeltaBenchmarkPair<float>(0f, float.Epsilon),
            new DeltaBenchmarkPair<float>(float.Epsilon, FloatFromBits(2)),
            new DeltaBenchmarkPair<float>(float.MaxValue, float.PositiveInfinity),
            new DeltaBenchmarkPair<float>(float.MinValue, float.NegativeInfinity),
            new DeltaBenchmarkPair<float>(float.PositiveInfinity, float.MaxValue),
            new DeltaBenchmarkPair<float>(float.NegativeInfinity, float.MinValue),
            new DeltaBenchmarkPair<float>(nanOne, nanOne),
            new DeltaBenchmarkPair<float>(nanOne, nanTwo),
            new DeltaBenchmarkPair<float>(1f, -1f),
            new DeltaBenchmarkPair<float>(-1f, 1f),
            new DeltaBenchmarkPair<float>(1.17549435E-38f, float.Epsilon)
        };

        return new[]
        {
            Scenario("smooth", smooth, ScalarTargetOperations),
            Scenario("ulp", ulp, ScalarTargetOperations),
            Scenario("random", random, ScalarTargetOperations),
            Scenario("edge", edge, ScalarTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<double>[] BuildDoubleScenarios()
    {
        var smooth = new DeltaBenchmarkPair<double>[ScalarPairCount];
        var ulp = new DeltaBenchmarkPair<double>[ScalarPairCount];
        var random = new DeltaBenchmarkPair<double>[ScalarPairCount];
        var rng = new DeterministicRandom(0x7E95761Eu);

        for (int i = 0; i < ScalarPairCount; i++)
        {
            double oldSmooth = (i - 128) * 0.125 + (i % 11) * 17.0;
            double step = i % 8 == 0 ? 0.0 : ((i & 1) == 0 ? 0.000_001 : -0.000_002);
            smooth[i] = new DeltaBenchmarkPair<double>(oldSmooth, oldSmooth + step);

            double oldUlp = rng.NextDouble(-1_000_000.0, 1_000_000.0);
            int ulpDistance = 1 + i % 4;
            ulp[i] = new DeltaBenchmarkPair<double>(oldUlp, OffsetDoubleBits(oldUlp, ulpDistance));
            random[i] = new DeltaBenchmarkPair<double>(NextFiniteDouble(ref rng), NextFiniteDouble(ref rng));
        }

        double negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
        double nanOne = BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000000001UL));
        double nanTwo = BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000001234UL));
        var edge = new[]
        {
            new DeltaBenchmarkPair<double>(0.0, 0.0),
            new DeltaBenchmarkPair<double>(0.0, negativeZero),
            new DeltaBenchmarkPair<double>(negativeZero, 0.0),
            new DeltaBenchmarkPair<double>(0.0, double.Epsilon),
            new DeltaBenchmarkPair<double>(double.Epsilon, BitConverter.Int64BitsToDouble(2L)),
            new DeltaBenchmarkPair<double>(double.MaxValue, double.PositiveInfinity),
            new DeltaBenchmarkPair<double>(double.MinValue, double.NegativeInfinity),
            new DeltaBenchmarkPair<double>(double.PositiveInfinity, double.MaxValue),
            new DeltaBenchmarkPair<double>(double.NegativeInfinity, double.MinValue),
            new DeltaBenchmarkPair<double>(nanOne, nanOne),
            new DeltaBenchmarkPair<double>(nanOne, nanTwo),
            new DeltaBenchmarkPair<double>(1.0, -1.0),
            new DeltaBenchmarkPair<double>(-1.0, 1.0),
            new DeltaBenchmarkPair<double>(2.2250738585072014E-308, double.Epsilon)
        };

        return new[]
        {
            Scenario("smooth", smooth, ScalarTargetOperations),
            Scenario("ulp", ulp, ScalarTargetOperations),
            Scenario("random", random, ScalarTargetOperations),
            Scenario("edge", edge, ScalarTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<Vector3>[] BuildVector3Scenarios()
    {
        var character = new DeltaBenchmarkPair<Vector3>[CompositePairCount];
        var oneAxis = new DeltaBenchmarkPair<Vector3>[CompositePairCount];
        var random = new DeltaBenchmarkPair<Vector3>[CompositePairCount];
        var rng = new DeterministicRandom(0xE94B2F56u);

        for (int i = 0; i < CompositePairCount; i++)
        {
            var oldCharacter = new Vector3(
                (i - 64) * 0.125f,
                1.0f + (i % 5) * 0.01f,
                ((i * 7) % 97 - 48) * 0.125f);
            Vector3 movement = i % 10 == 0
                ? Vector3.zero
                : new Vector3((i & 1) == 0 ? 0.03125f : -0.03125f, i % 13 == 0 ? 0.125f : 0f, 0.015625f);
            character[i] = new DeltaBenchmarkPair<Vector3>(oldCharacter, oldCharacter + movement);

            var oldOneAxis = new Vector3(i * 0.5f, -37.25f, 812.0f);
            oneAxis[i] = new DeltaBenchmarkPair<Vector3>(oldOneAxis, oldOneAxis + new Vector3(0.0625f, 0f, 0f));

            random[i] = new DeltaBenchmarkPair<Vector3>(RandomVector3(ref rng), RandomVector3(ref rng));
        }

        return new[]
        {
            Scenario("character", character, CompositeTargetOperations),
            Scenario("one-axis", oneAxis, CompositeTargetOperations),
            Scenario("random", random, CompositeTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<Quaternion>[] BuildQuaternionScenarios()
    {
        var smallRotation = new DeltaBenchmarkPair<Quaternion>[CompositePairCount];
        var signFlip = new DeltaBenchmarkPair<Quaternion>[CompositePairCount];
        var random = new DeltaBenchmarkPair<Quaternion>[CompositePairCount];
        var rng = new DeterministicRandom(0x4A7C15F3u);

        for (int i = 0; i < CompositePairCount; i++)
        {
            Quaternion oldRotation = Quaternion.Euler(i * 1.75f, i * 3.125f, i * -0.625f);
            Quaternion incremental = Quaternion.AngleAxis(0.05f + (i % 5) * 0.025f, (i & 1) == 0 ? Vector3.up : Vector3.right);
            smallRotation[i] = new DeltaBenchmarkPair<Quaternion>(oldRotation, incremental * oldRotation);
            signFlip[i] = new DeltaBenchmarkPair<Quaternion>(
                oldRotation,
                new Quaternion(-oldRotation.x, -oldRotation.y, -oldRotation.z, -oldRotation.w));
            random[i] = new DeltaBenchmarkPair<Quaternion>(RandomQuaternion(ref rng), RandomQuaternion(ref rng));
        }

        return new[]
        {
            Scenario("small-rotation", smallRotation, CompositeTargetOperations),
            Scenario("sign-flip", signFlip, CompositeTargetOperations),
            Scenario("random", random, CompositeTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<List<int>>[] BuildListScenarios()
    {
        return new[]
        {
            Scenario("equal", BuildListPairs(ListMutation.Equal), ListTargetOperations),
            Scenario("edge", BuildListEdgePairs(), ListHardTargetOperations),
            Scenario("one-update", BuildListPairs(ListMutation.OneUpdate), ListTargetOperations),
            Scenario("5%-updates", BuildListPairs(ListMutation.FivePercentUpdates), ListTargetOperations),
            Scenario("append", BuildListPairs(ListMutation.Append), ListTargetOperations),
            Scenario("middle-insert", BuildListPairs(ListMutation.MiddleInsert), ListTargetOperations),
            Scenario("middle-delete", BuildListPairs(ListMutation.MiddleDelete), ListTargetOperations),
            Scenario("distributed-inserts", BuildListPairs(ListMutation.DistributedInserts),
                ListStructuralTargetOperations),
            Scenario("distributed-deletes", BuildListPairs(ListMutation.DistributedDeletes),
                ListStructuralTargetOperations),
            Scenario("mixed-structural", BuildListPairs(ListMutation.MixedStructural),
                ListStructuralTargetOperations),
            Scenario("duplicate-structural", BuildListPairs(ListMutation.DuplicateStructural),
                ListStructuralTargetOperations),
            Scenario("noisy-updates", BuildListPairs(ListMutation.NoisyUpdates),
                ListStructuralTargetOperations),
            Scenario("block-replace", BuildListPairs(ListMutation.BlockReplace), ListStructuralTargetOperations),
            Scenario("block-move", BuildListPairs(ListMutation.BlockMove), ListHardTargetOperations),
            Scenario("reverse", BuildListPairs(ListMutation.Reverse), ListHardTargetOperations),
            Scenario("full-replace", BuildListPairs(ListMutation.FullReplace), ListHardTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<Dictionary<int, int>>[] BuildDictionaryScenarios()
    {
        return new[]
        {
            Scenario("equal-reversed", BuildDictionaryPairs(DictionaryMutation.EqualReversed), CollectionTargetOperations),
            Scenario("one-update", BuildDictionaryPairs(DictionaryMutation.OneUpdate), CollectionTargetOperations),
            Scenario("5%-updates", BuildDictionaryPairs(DictionaryMutation.FivePercentUpdates), CollectionTargetOperations),
            Scenario("add-remove", BuildDictionaryPairs(DictionaryMutation.AddRemove), CollectionTargetOperations),
            Scenario("50%-churn", BuildDictionaryPairs(DictionaryMutation.FiftyPercentChurn), CollectionTargetOperations),
            Scenario("disjoint", BuildDictionaryPairs(DictionaryMutation.Disjoint), CollectionTargetOperations)
        };
    }

    internal static DeltaBenchmarkScenario<byte[]>[] BuildByteArrayScenarios()
    {
        return new[]
        {
            Scenario("equal", BuildByteArrayPairs(ByteArrayMutation.Equal), ByteArrayTargetOperations),
            Scenario("one-byte", BuildByteArrayPairs(ByteArrayMutation.OneByte), ByteArrayTargetOperations),
            Scenario("1%-scattered", BuildByteArrayPairs(ByteArrayMutation.OnePercentScattered), ByteArrayTargetOperations),
            Scenario("append", BuildByteArrayPairs(ByteArrayMutation.Append), ByteArrayTargetOperations),
            Scenario("middle-block", BuildByteArrayPairs(ByteArrayMutation.MiddleBlock), ByteArrayTargetOperations),
            Scenario("unrelated", BuildByteArrayPairs(ByteArrayMutation.Unrelated), ByteArrayTargetOperations)
        };
    }

    private static DeltaBenchmarkScenario<T> Scenario<T>(
        string name,
        DeltaBenchmarkPair<T>[] pairs,
        int targetOperations)
    {
        return new DeltaBenchmarkScenario<T>(name, pairs, targetOperations);
    }

    private static DeltaBenchmarkPair<List<int>>[] BuildListPairs(ListMutation mutation)
    {
        var result = new DeltaBenchmarkPair<List<int>>[CollectionPairCount];
        var rng = new DeterministicRandom(0x6C8E9CF5u + (uint)mutation * 0x9E3779B9u);

        for (int pairIndex = 0; pairIndex < result.Length; pairIndex++)
        {
            var oldValue = new List<int>(ListLength);
            int valueBase = pairIndex * 10_003;
            for (int i = 0; i < ListLength; i++)
            {
                if (mutation == ListMutation.DuplicateStructural)
                    oldValue.Add((i + pairIndex) & 3);
                else if (mutation == ListMutation.NoisyUpdates)
                    oldValue.Add(rng.NextInt());
                else
                    oldValue.Add(valueBase + i * 3 + i % 5);
            }

            var newValue = new List<int>(oldValue);
            switch (mutation)
            {
                case ListMutation.OneUpdate:
                    newValue[(pairIndex * 37 + 11) % ListLength] += 1_000_003;
                    break;
                case ListMutation.FivePercentUpdates:
                    for (int i = 0; i < 7; i++)
                        newValue[(pairIndex * 17 + i * 23) % ListLength] += 100_003 + i;
                    break;
                case ListMutation.Append:
                    for (int i = 0; i < 8; i++)
                        newValue.Add(valueBase + 20_000 + i);
                    break;
                case ListMutation.MiddleInsert:
                    for (int i = 0; i < 8; i++)
                        newValue.Insert(ListLength / 2 + i, valueBase + 30_000 + i);
                    break;
                case ListMutation.MiddleDelete:
                    newValue.RemoveRange(ListLength / 2, 8);
                    break;
                case ListMutation.DistributedInserts:
                    for (int i = 7; i >= 0; i--)
                        newValue.Insert(8 + i * 15, valueBase + 40_000 + i);
                    break;
                case ListMutation.DistributedDeletes:
                    for (int i = 7; i >= 0; i--)
                        newValue.RemoveAt(8 + i * 15);
                    break;
                case ListMutation.MixedStructural:
                    for (int i = 3; i >= 0; i--)
                        newValue.RemoveAt(14 + i * 27);
                    for (int i = 3; i >= 0; i--)
                        newValue.Insert(24 + i * 25, valueBase + 50_000 + i);
                    break;
                case ListMutation.DuplicateStructural:
                    for (int i = 3; i >= 0; i--)
                        newValue.RemoveAt(12 + i * 29);
                    for (int i = 3; i >= 0; i--)
                        newValue.Insert(21 + i * 25, (pairIndex + i + 2) & 3);
                    newValue[63] = 9;
                    break;
                case ListMutation.NoisyUpdates:
                    for (int i = 0; i < 7; i++)
                        newValue[(pairIndex * 19 + i * 23) % ListLength] = rng.NextInt();
                    break;
                case ListMutation.BlockReplace:
                    for (int i = 0; i < 16; i++)
                        newValue[48 + i] += 7 + i;
                    break;
                case ListMutation.BlockMove:
                {
                    var moved = newValue.GetRange(24, 16);
                    newValue.RemoveRange(24, 16);
                    newValue.InsertRange(80, moved);
                    break;
                }
                case ListMutation.Reverse:
                    newValue.Reverse();
                    break;
                case ListMutation.FullReplace:
                    newValue.Clear();
                    for (int i = 0; i < ListLength; i++)
                        newValue.Add(rng.NextInt());
                    break;
            }

            result[pairIndex] = new DeltaBenchmarkPair<List<int>>(oldValue, newValue);
        }

        return result;
    }

    private static DeltaBenchmarkPair<List<int>>[] BuildListEdgePairs()
    {
        return new[]
        {
            new DeltaBenchmarkPair<List<int>>(null, null),
            new DeltaBenchmarkPair<List<int>>(null, new List<int>()),
            new DeltaBenchmarkPair<List<int>>(new List<int>(), null),
            new DeltaBenchmarkPair<List<int>>(new List<int>(), new List<int>()),
            new DeltaBenchmarkPair<List<int>>(null, new List<int> { 1, 2, 3 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 2, 3 }, null),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1 }, new List<int> { 2 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 1, 2, 2, 3, 3 },
                new List<int> { 1, 2, 3 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 2, 3 },
                new List<int> { 1, 1, 2, 2, 3, 3 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 0, 2, 0, 3, 0 },
                new List<int> { 1, 2, 3 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 2, 1, 2, 1, 2 },
                new List<int> { 1, 2, 1, 2 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 2, 3 },
                new List<int> { 3, 2, 1 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { int.MinValue, 0, int.MaxValue },
                new List<int> { int.MaxValue, -1, int.MinValue }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 4, 5, 6 },
                new List<int> { 1, 2, 3, 4, 5, 6 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 1, 2, 3, 4, 5, 6 },
                new List<int> { 4, 5, 6 }),
            new DeltaBenchmarkPair<List<int>>(new List<int> { 10, 20, 30, 40, 50, 60 },
                new List<int> { 5, 10, 25, 30, 50, 70, 80 })
        };
    }

    private static DeltaBenchmarkPair<Dictionary<int, int>>[] BuildDictionaryPairs(DictionaryMutation mutation)
    {
        var result = new DeltaBenchmarkPair<Dictionary<int, int>>[CollectionPairCount];
        var rng = new DeterministicRandom(0xD1B54A35u + (uint)mutation * 0x85EBCA6Bu);

        for (int pairIndex = 0; pairIndex < result.Length; pairIndex++)
        {
            int keyBase = pairIndex * 1_000;
            var oldValue = new Dictionary<int, int>(DictionaryCount);
            for (int i = 0; i < DictionaryCount; i++)
                oldValue.Add(keyBase + i, pairIndex * 10_007 + i * 13);

            var newValue = new Dictionary<int, int>(DictionaryCount + 64);
            if (mutation == DictionaryMutation.EqualReversed)
            {
                for (int i = DictionaryCount - 1; i >= 0; i--)
                    newValue.Add(keyBase + i, oldValue[keyBase + i]);
            }
            else if (mutation == DictionaryMutation.Disjoint)
            {
                for (int i = 0; i < DictionaryCount; i++)
                    newValue.Add(keyBase + 10_000 + i, rng.NextInt());
            }
            else
            {
                for (int i = 0; i < DictionaryCount; i++)
                    newValue.Add(keyBase + i, oldValue[keyBase + i]);

                switch (mutation)
                {
                    case DictionaryMutation.OneUpdate:
                    {
                        int key = keyBase + (pairIndex * 37 + 11) % DictionaryCount;
                        newValue[key] += 1_000_003;
                        break;
                    }
                    case DictionaryMutation.FivePercentUpdates:
                        for (int i = 0; i < 7; i++)
                        {
                            int key = keyBase + (pairIndex * 17 + i * 23) % DictionaryCount;
                            newValue[key] += 100_003 + i;
                        }
                        break;
                    case DictionaryMutation.AddRemove:
                        for (int i = 0; i < 8; i++)
                        {
                            newValue.Remove(keyBase + (pairIndex * 7 + i * 13) % DictionaryCount);
                            newValue.Add(keyBase + DictionaryCount + i, rng.NextInt());
                        }
                        break;
                    case DictionaryMutation.FiftyPercentChurn:
                        for (int i = 0; i < DictionaryCount / 2; i++)
                        {
                            newValue.Remove(keyBase + i);
                            newValue.Add(keyBase + DictionaryCount + i, rng.NextInt());
                        }
                        break;
                }
            }

            result[pairIndex] = new DeltaBenchmarkPair<Dictionary<int, int>>(oldValue, newValue);
        }

        return result;
    }

    private static DeltaBenchmarkPair<byte[]>[] BuildByteArrayPairs(ByteArrayMutation mutation)
    {
        var result = new DeltaBenchmarkPair<byte[]>[CollectionPairCount];
        var rng = new DeterministicRandom(0x91E10DA5u + (uint)mutation * 0xC2B2AE35u);

        for (int pairIndex = 0; pairIndex < result.Length; pairIndex++)
        {
            var oldValue = new byte[ByteArrayLength];
            for (int i = 0; i < oldValue.Length; i++)
                oldValue[i] = (byte)rng.NextUInt();

            int newLength = mutation == ByteArrayMutation.Append ? ByteArrayLength + 64 : ByteArrayLength;
            var newValue = new byte[newLength];
            Array.Copy(oldValue, newValue, oldValue.Length);

            switch (mutation)
            {
                case ByteArrayMutation.OneByte:
                    newValue[(pairIndex * 137 + 31) % ByteArrayLength] ^= 0xA5;
                    break;
                case ByteArrayMutation.OnePercentScattered:
                    for (int i = 0; i < 12; i++)
                    {
                        int index = (pairIndex * 53 + i * 97) % ByteArrayLength;
                        newValue[index] ^= (byte)(0x81 + i);
                    }
                    break;
                case ByteArrayMutation.Append:
                    for (int i = ByteArrayLength; i < newValue.Length; i++)
                        newValue[i] = (byte)rng.NextUInt();
                    break;
                case ByteArrayMutation.MiddleBlock:
                    for (int i = 0; i < 96; i++)
                        newValue[ByteArrayLength / 2 - 48 + i] = (byte)rng.NextUInt();
                    break;
                case ByteArrayMutation.Unrelated:
                    for (int i = 0; i < newValue.Length; i++)
                        newValue[i] = (byte)rng.NextUInt();
                    break;
            }

            result[pairIndex] = new DeltaBenchmarkPair<byte[]>(oldValue, newValue);
        }

        return result;
    }

    private static Vector3 RandomVector3(ref DeterministicRandom rng)
    {
        return new Vector3(
            rng.NextFloat(-10_000f, 10_000f),
            rng.NextFloat(-10_000f, 10_000f),
            rng.NextFloat(-10_000f, 10_000f));
    }

    private static Quaternion RandomQuaternion(ref DeterministicRandom rng)
    {
        return Quaternion.Euler(
            rng.NextFloat(-180f, 180f),
            rng.NextFloat(-180f, 180f),
            rng.NextFloat(-180f, 180f));
    }

    private static float NextFiniteFloat(ref DeterministicRandom rng)
    {
        uint bits;
        do
        {
            bits = rng.NextUInt();
        } while ((bits & 0x7F800000u) == 0x7F800000u);

        return FloatFromBits(unchecked((int)bits));
    }

    private static double NextFiniteDouble(ref DeterministicRandom rng)
    {
        ulong bits;
        do
        {
            bits = rng.NextULong();
        } while ((bits & 0x7FF0000000000000UL) == 0x7FF0000000000000UL);

        return BitConverter.Int64BitsToDouble(unchecked((long)bits));
    }

    private static float OffsetFloatBits(float value, int distance)
    {
        var bits = new FloatBits { value = value };
        bits.bits = bits.bits < 0 ? bits.bits - distance : bits.bits + distance;
        return bits.value;
    }

    private static double OffsetDoubleBits(double value, int distance)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bits = bits < 0 ? bits - distance : bits + distance;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static float FloatFromBits(int bits)
    {
        return new FloatBits { bits = bits }.value;
    }

    private enum ListMutation
    {
        Equal,
        OneUpdate,
        FivePercentUpdates,
        Append,
        MiddleInsert,
        MiddleDelete,
        DistributedInserts,
        DistributedDeletes,
        MixedStructural,
        DuplicateStructural,
        NoisyUpdates,
        BlockReplace,
        BlockMove,
        Reverse,
        FullReplace
    }

    private enum DictionaryMutation
    {
        EqualReversed,
        OneUpdate,
        FivePercentUpdates,
        AddRemove,
        FiftyPercentChurn,
        Disjoint
    }

    private enum ByteArrayMutation
    {
        Equal,
        OneByte,
        OnePercentScattered,
        Append,
        MiddleBlock,
        Unrelated
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] public float value;
        [FieldOffset(0)] public int bits;
    }

    private struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0 ? 0x6D2B79F5u : seed;
        }

        public uint NextUInt()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        public ulong NextULong()
        {
            return ((ulong)NextUInt() << 32) | NextUInt();
        }

        public int NextInt()
        {
            return unchecked((int)NextUInt());
        }

        public long NextLong()
        {
            return unchecked((long)NextULong());
        }

        public float NextFloat(float min, float max)
        {
            float unit = (NextUInt() >> 8) * (1f / 16_777_216f);
            return min + (max - min) * unit;
        }

        public double NextDouble(double min, double max)
        {
            double unit = (NextULong() >> 11) * (1.0 / 9_007_199_254_740_992.0);
            return min + (max - min) * unit;
        }
    }
}

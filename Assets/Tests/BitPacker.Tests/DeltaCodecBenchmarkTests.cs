using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

/// <summary>
/// A/B benchmarks for candidate DeltaPacker wire formats. Candidates deliberately live in the
/// test assembly and are called directly; they never mutate the global Packer registrations.
/// Run this test by name so normal test runs do not pay the benchmark cost.
/// </summary>
public sealed class DeltaCodecBenchmarkTests
{
    private const int TimingSamples = 5;
    private const int WarmupOperations = 10_000;
    private const string OutputDirectory = "Library/DeltaCodecBenchmarks";
    private const string OutputFile = "DeltaCodecBenchmark_latest.json";

    private delegate bool BenchmarkWrite<T>(BitPacker packer, T oldValue, T newValue);
    private delegate void BenchmarkRead<T>(BitPacker packer, T oldValue, ref T value);
    private delegate bool BenchmarkEquals<T>(T left, T right);

    private sealed class Codec<T>
    {
        public readonly string name;
        public readonly BenchmarkWrite<T> write;
        public readonly BenchmarkRead<T> read;
        public readonly bool candidate;
        public readonly bool reportsChanged;
        public readonly bool mustBeCorrect;

        public Codec(string name, BenchmarkWrite<T> write, BenchmarkRead<T> read,
            bool candidate, bool reportsChanged = true, bool? mustBeCorrect = null)
        {
            this.name = name;
            this.write = write;
            this.read = read;
            this.candidate = candidate;
            this.reportsChanged = reportsChanged;
            this.mustBeCorrect = mustBeCorrect ?? candidate;
        }
    }

    private sealed class CodecState<T> : IDisposable
    {
        public Codec<T> codec;
        public DeltaBenchmarkScenario<T> scenario;
        public BitPacker encoded;
        public BitPacker[] encodedValues;
        public BitPacker working;
        public int rounds;
        public int operations;
        public int[] bits;
        public double[] writeSamples;
        public double[] readSamples;
        public double writeAllocatedBytes;
        public double readAllocatedBytes;
        public bool correct = true;
        public bool canRead = true;
        public string error;

        public void Dispose()
        {
            // These are intentionally dedicated benchmark packers rather than pooled instances.
            // Long-lived pooled instances can alias if unrelated tests returned the same packer twice.
            encoded = null;
            encodedValues = null;
            working = null;
        }
    }

    [Serializable]
    private sealed class BenchmarkReport
    {
        public string formatVersion = "5";
        public string timestampUtc;
        public string unityVersion;
        public string runtimeVersion;
        public string processor;
        public int timingSamples;
        public List<BenchmarkResult> results = new List<BenchmarkResult>();
    }

    [Serializable]
    private sealed class BenchmarkResult
    {
        public string category;
        public string scenario;
        public string codec;
        public bool candidate;
        public int corpusSize;
        public int timedOperations;
        public bool correct;
        public bool canRead;
        public string error;
        public double meanBits;
        public int p50Bits;
        public int p95Bits;
        public int maxBits;
        public double medianWriteNanoseconds;
        public double medianReadNanoseconds;
        public double writeAllocatedBytesPerOperation;
        public double readAllocatedBytesPerOperation;
    }

    private readonly List<string> _candidateFailures = new List<string>();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        NetworkManager.CallAllRegisters();
        AssertBenchmarkCodecsAreIsolated();
        GC.GetAllocatedBytesForCurrentThread();
    }

    private static void AssertBenchmarkCodecsAreIsolated()
    {
        Delegate[] registeredDelegates =
        {
            DeltaPacker<int>.DirectWrite, DeltaPacker<int>.DirectRead,
            DeltaPacker<long>.DirectWrite, DeltaPacker<long>.DirectRead,
            DeltaPacker<float>.DirectWrite, DeltaPacker<float>.DirectRead,
            DeltaPacker<double>.DirectWrite, DeltaPacker<double>.DirectRead,
            DeltaPacker<Vector3>.DirectWrite, DeltaPacker<Vector3>.DirectRead,
            DeltaPacker<Quaternion>.DirectWrite, DeltaPacker<Quaternion>.DirectRead,
            DeltaPacker<List<int>>.DirectWrite, DeltaPacker<List<int>>.DirectRead,
            DeltaPacker<Dictionary<int, int>>.DirectWrite, DeltaPacker<Dictionary<int, int>>.DirectRead,
            DeltaPacker<byte[]>.DirectWrite, DeltaPacker<byte[]>.DirectRead,
            DeltaPacker<PackedByte>.DirectWrite, DeltaPacker<PackedByte>.DirectRead,
            DeltaPacker<PackedSByte>.DirectWrite, DeltaPacker<PackedSByte>.DirectRead,
            DeltaPacker<PackedUShort>.DirectWrite, DeltaPacker<PackedUShort>.DirectRead,
            DeltaPacker<PackedShort>.DirectWrite, DeltaPacker<PackedShort>.DirectRead,
            DeltaPacker<PackedUInt>.DirectWrite, DeltaPacker<PackedUInt>.DirectRead,
            DeltaPacker<PackedInt>.DirectWrite, DeltaPacker<PackedInt>.DirectRead,
            DeltaPacker<PackedULong>.DirectWrite, DeltaPacker<PackedULong>.DirectRead,
            DeltaPacker<PackedLong>.DirectWrite, DeltaPacker<PackedLong>.DirectRead,
            DeltaPacker<Size>.DirectWrite, DeltaPacker<Size>.DirectRead,
            Packer<List<int>>.DirectWrite, Packer<List<int>>.DirectRead,
            Packer<Dictionary<int, int>>.DirectWrite, Packer<Dictionary<int, int>>.DirectRead,
            Packer<byte[]>.DirectWrite, Packer<byte[]>.DirectRead,
            Packer<PackedByte>.DirectWrite, Packer<PackedByte>.DirectRead,
            Packer<PackedSByte>.DirectWrite, Packer<PackedSByte>.DirectRead,
            Packer<PackedUShort>.DirectWrite, Packer<PackedUShort>.DirectRead,
            Packer<PackedShort>.DirectWrite, Packer<PackedShort>.DirectRead,
            Packer<PackedUInt>.DirectWrite, Packer<PackedUInt>.DirectRead,
            Packer<PackedInt>.DirectWrite, Packer<PackedInt>.DirectRead,
            Packer<PackedULong>.DirectWrite, Packer<PackedULong>.DirectRead,
            Packer<PackedLong>.DirectWrite, Packer<PackedLong>.DirectRead,
            Packer<Size>.DirectWrite, Packer<Size>.DirectRead
        };

        foreach (Delegate registeredDelegate in registeredDelegates)
        {
            string declaringType = registeredDelegate?.Method.DeclaringType?.FullName ?? string.Empty;
            Assert.That(declaringType, Does.Not.Contain("Benchmark"),
                $"Benchmark helper {declaringType}.{registeredDelegate?.Method.Name} was globally registered.");
        }
    }

    [Test]
    public void Benchmark_Current_Versus_Candidate_Delta_Codecs()
    {
        _candidateFailures.Clear();
        var report = new BenchmarkReport
        {
            timestampUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            runtimeVersion = Environment.Version.ToString(),
            processor = SystemInfo.processorType,
            timingSamples = TimingSamples
        };

        RunIntegerBenchmarks(report);
        RunPackedIntegerBenchmarks(report);
        RunFloatingPointBenchmarks(report);
        RunUnityTypeBenchmarks(report);
        RunCollectionBenchmarks(report);

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        string outputDirectory = Path.Combine(projectRoot, OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, OutputFile);
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));

        LogSummary(report, outputPath);
        Assert.That(_candidateFailures, Is.Empty,
            "Candidate codec correctness failures:\n" + string.Join("\n", _candidateFailures));
    }

    private void RunIntegerBenchmarks(BenchmarkReport report)
    {
        var intCodecs = new[]
        {
            CurrentCodec<int>(DeltaBenchmarkCurrentCodecs.WriteInt, DeltaBenchmarkCurrentCodecs.ReadInt),
            FullCodec<int>(DeltaBenchmarkFullCodecs.WriteInt, DeltaBenchmarkFullCodecs.ReadInt),
            new Codec<int>("adaptive-modular", DeltaNumericBenchmarkCandidates.WriteIntAdaptive,
                DeltaNumericBenchmarkCandidates.ReadIntAdaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildIntScenarios())
            RunScenario(report, "int", scenario, intCodecs, (a, b) => a == b);

        var longCodecs = new[]
        {
            CurrentCodec<long>(DeltaBenchmarkCurrentCodecs.WriteLong, DeltaBenchmarkCurrentCodecs.ReadLong),
            FullCodec<long>(DeltaBenchmarkFullCodecs.WriteLong, DeltaBenchmarkFullCodecs.ReadLong),
            new Codec<long>("adaptive-modular", DeltaNumericBenchmarkCandidates.WriteLongAdaptive,
                DeltaNumericBenchmarkCandidates.ReadLongAdaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildLongScenarios())
            RunScenario(report, "long", scenario, longCodecs, (a, b) => a == b);
    }

    private void RunPackedIntegerBenchmarks(BenchmarkReport report)
    {
        var absoluteCodecs = new[]
        {
            new Codec<PackedBenchmarkValue>("current",
                PackedIntegerBenchmarkCodecs.WriteCurrentAbsolute,
                PackedIntegerBenchmarkCodecs.ReadCurrentAbsolute,
                false, reportsChanged: false, mustBeCorrect: true),
            new Codec<PackedBenchmarkValue>("raw-fixed",
                PackedIntegerBenchmarkCodecs.WriteRawAbsolute,
                PackedIntegerBenchmarkCodecs.ReadRawAbsolute,
                false, reportsChanged: false, mustBeCorrect: true),
            new Codec<PackedBenchmarkValue>("compact-shifted",
                PackedIntegerBenchmarkCodecs.WriteCompactAbsolute,
                PackedIntegerBenchmarkCodecs.ReadCompactAbsolute,
                true, reportsChanged: false),
            new Codec<PackedBenchmarkValue>("adaptive-shifted-raw",
                PackedIntegerBenchmarkCodecs.WriteAdaptiveAbsolute,
                PackedIntegerBenchmarkCodecs.ReadAdaptiveAbsolute,
                true, reportsChanged: false)
        };

        var deltaCodecs = new[]
        {
            new Codec<PackedBenchmarkValue>("current",
                PackedIntegerBenchmarkCodecs.WriteCurrentDelta,
                PackedIntegerBenchmarkCodecs.ReadCurrentDelta,
                false, mustBeCorrect: true),
            new Codec<PackedBenchmarkValue>("raw-on-change",
                PackedIntegerBenchmarkCodecs.WriteRawOnChange,
                PackedIntegerBenchmarkCodecs.ReadRawOnChange,
                false, mustBeCorrect: true),
            new Codec<PackedBenchmarkValue>("compact-modular-nonzero",
                PackedIntegerBenchmarkCodecs.WriteCompactModularDelta,
                PackedIntegerBenchmarkCodecs.ReadCompactModularDelta,
                true),
            new Codec<PackedBenchmarkValue>("compact-forward-nonzero",
                PackedIntegerBenchmarkCodecs.WriteCompactForwardDelta,
                PackedIntegerBenchmarkCodecs.ReadCompactForwardDelta,
                true),
            new Codec<PackedBenchmarkValue>("adaptive-modular-raw",
                PackedIntegerBenchmarkCodecs.WriteAdaptiveModularDelta,
                PackedIntegerBenchmarkCodecs.ReadAdaptiveModularDelta,
                true),
            new Codec<PackedBenchmarkValue>("compact-2bit-nonzero",
                PackedIntegerBenchmarkCodecs.WriteCompactTwoBitDelta,
                PackedIntegerBenchmarkCodecs.ReadCompactTwoBitDelta,
                true),
            new Codec<PackedBenchmarkValue>("adaptive-2bit-raw",
                PackedIntegerBenchmarkCodecs.WriteAdaptiveTwoBitDelta,
                PackedIntegerBenchmarkCodecs.ReadAdaptiveTwoBitDelta,
                true)
        };

        foreach (PackedBenchmarkKind kind in Enum.GetValues(typeof(PackedBenchmarkKind)))
        {
            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildAbsoluteScenarios(kind))
                RunScenario(report, $"{kind}/absolute", scenario, absoluteCodecs, PackedValuesEqual);

            foreach (var scenario in PackedIntegerBenchmarkCorpora.BuildDeltaScenarios(kind))
                RunScenario(report, $"{kind}/delta", scenario, deltaCodecs, PackedValuesEqual);
        }
    }

    private void RunFloatingPointBenchmarks(BenchmarkReport report)
    {
        var floatCodecs = new[]
        {
            CurrentCodec<float>(DeltaBenchmarkCurrentCodecs.WriteFloat, DeltaBenchmarkCurrentCodecs.ReadFloat),
            FullCodec<float>(DeltaBenchmarkFullCodecs.WriteFloat, DeltaBenchmarkFullCodecs.ReadFloat),
            new Codec<float>("xor-leb", DeltaNumericBenchmarkCandidates.WriteFloatXorLeb,
                DeltaNumericBenchmarkCandidates.ReadFloatXorLeb, true),
            new Codec<float>("xor-window", DeltaNumericBenchmarkCandidates.WriteFloatXorWindow,
                DeltaNumericBenchmarkCandidates.ReadFloatXorWindow, true),
            new Codec<float>("hybrid", DeltaNumericBenchmarkCandidates.WriteFloatHybrid,
                DeltaNumericBenchmarkCandidates.ReadFloatHybrid, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildFloatScenarios())
            RunScenario(report, "float", scenario, floatCodecs, FloatBitsEqual);

        var doubleCodecs = new[]
        {
            CurrentCodec<double>(DeltaBenchmarkCurrentCodecs.WriteDouble, DeltaBenchmarkCurrentCodecs.ReadDouble),
            FullCodec<double>(DeltaBenchmarkFullCodecs.WriteDouble, DeltaBenchmarkFullCodecs.ReadDouble),
            new Codec<double>("xor-leb", DeltaNumericBenchmarkCandidates.WriteDoubleXorLeb,
                DeltaNumericBenchmarkCandidates.ReadDoubleXorLeb, true),
            new Codec<double>("xor-window", DeltaNumericBenchmarkCandidates.WriteDoubleXorWindow,
                DeltaNumericBenchmarkCandidates.ReadDoubleXorWindow, true),
            new Codec<double>("hybrid", DeltaNumericBenchmarkCandidates.WriteDoubleHybrid,
                DeltaNumericBenchmarkCandidates.ReadDoubleHybrid, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildDoubleScenarios())
            RunScenario(report, "double", scenario, doubleCodecs, DoubleBitsEqual);
    }

    private void RunUnityTypeBenchmarks(BenchmarkReport report)
    {
        var vectorCodecs = new[]
        {
            CurrentCodec<Vector3>(DeltaBenchmarkCurrentCodecs.WriteVector3, DeltaBenchmarkCurrentCodecs.ReadVector3),
            FullCodec<Vector3>(DeltaBenchmarkFullCodecs.WriteVector3, DeltaBenchmarkFullCodecs.ReadVector3),
            new Codec<Vector3>("adaptive-mask-window", DeltaNumericBenchmarkCandidates.WriteVector3Adaptive,
                DeltaNumericBenchmarkCandidates.ReadVector3Adaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildVector3Scenarios())
            RunScenario(report, "Vector3", scenario, vectorCodecs, Vector3BitsEqual);

        var quaternionCodecs = new[]
        {
            CurrentCodec<Quaternion>(DeltaBenchmarkCurrentCodecs.WriteQuaternion,
                DeltaBenchmarkCurrentCodecs.ReadQuaternion),
            FullCodec<Quaternion>(DeltaBenchmarkFullCodecs.WriteQuaternion,
                DeltaBenchmarkFullCodecs.ReadQuaternion),
            new Codec<Quaternion>("adaptive-sign-mask-window",
                DeltaNumericBenchmarkCandidates.WriteQuaternionAdaptive,
                DeltaNumericBenchmarkCandidates.ReadQuaternionAdaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildQuaternionScenarios())
            RunScenario(report, "Quaternion", scenario, quaternionCodecs, QuaternionBitsEqual);
    }

    private void RunCollectionBenchmarks(BenchmarkReport report)
    {
        var listCodecs = new[]
        {
            CurrentCodec<List<int>>(DeltaBenchmarkCurrentCodecs.WriteList, DeltaBenchmarkCurrentCodecs.ReadList),
            FullCodec<List<int>>(DeltaBenchmarkFullCodecs.WriteList, DeltaBenchmarkFullCodecs.ReadList),
            new Codec<List<int>>("adaptive-index-splice", DeltaCollectionBenchmarkCandidates.WriteListAdaptive,
                DeltaCollectionBenchmarkCandidates.ReadListAdaptive, true),
            new Codec<List<int>>("myers-current-op-wire",
                DeltaListMyersBenchmarkCandidates.WriteCurrentMyers,
                DeltaListMyersBenchmarkCandidates.ReadCurrentMyers, false, mustBeCorrect: true),
            new Codec<List<int>>("myers-bounded-compact",
                DeltaListMyersBenchmarkCandidates.WriteBoundedMyers,
                DeltaListMyersBenchmarkCandidates.ReadBoundedMyers, true),
            new Codec<List<int>>("adaptive-index-splice-myers",
                DeltaCollectionBenchmarkCandidates.WriteListAdaptiveMyers,
                DeltaCollectionBenchmarkCandidates.ReadListAdaptiveMyers, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildListScenarios())
            RunScenario(report, "List<int>[128]", scenario, listCodecs, ListsEqual);

        var dictionaryCodecs = new[]
        {
            CurrentCodec<Dictionary<int, int>>(DeltaBenchmarkCurrentCodecs.WriteDictionary,
                DeltaBenchmarkCurrentCodecs.ReadDictionary),
            FullCodec<Dictionary<int, int>>(DeltaBenchmarkFullCodecs.WriteDictionary,
                DeltaBenchmarkFullCodecs.ReadDictionary),
            new Codec<Dictionary<int, int>>("adaptive-semantic-ops",
                DeltaCollectionBenchmarkCandidates.WriteDictionaryAdaptive,
                DeltaCollectionBenchmarkCandidates.ReadDictionaryAdaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildDictionaryScenarios())
            RunScenario(report, "Dictionary<int,int>[128]", scenario, dictionaryCodecs, DictionariesEqual);

        var byteCodecs = new[]
        {
            CurrentCodec<byte[]>(DeltaBenchmarkCurrentCodecs.WriteBytes, DeltaBenchmarkCurrentCodecs.ReadBytes),
            FullCodec<byte[]>(DeltaBenchmarkFullCodecs.WriteBytes, DeltaBenchmarkFullCodecs.ReadBytes),
            new Codec<byte[]>("adaptive-prefix-suffix-indexed", DeltaCollectionBenchmarkCandidates.WriteBytesAdaptive,
                DeltaCollectionBenchmarkCandidates.ReadBytesAdaptive, true)
        };

        foreach (var scenario in DeltaCodecBenchmarkCorpora.BuildByteArrayScenarios())
            RunScenario(report, "byte[1200]", scenario, byteCodecs, ByteArraysEqual);
    }

    private void RunScenario<T>(BenchmarkReport report, string category, DeltaBenchmarkScenario<T> scenario,
        Codec<T>[] codecs, BenchmarkEquals<T> valuesEqual)
    {
        var states = new CodecState<T>[codecs.Length];
        try
        {
            for (int i = 0; i < codecs.Length; i++)
                states[i] = PrepareState(category, scenario, codecs[i], valuesEqual);

            int warmupOperations = Math.Min(WarmupOperations, scenario.targetOperations);
            int warmupRounds = Math.Max(1,
                (warmupOperations + scenario.pairs.Length - 1) / scenario.pairs.Length);
            for (int i = 0; i < states.Length; i++)
            {
                RunWriteBatches(states[i], warmupRounds);
                RunReadBatches(states[i], warmupRounds);
            }

            for (int sample = 0; sample < TimingSamples; sample++)
            {
                for (int order = 0; order < states.Length; order++)
                {
                    int index = (order + sample) % states.Length;
                    states[index].writeSamples[sample] = MeasureWrite(states[index]);
                }

                for (int order = states.Length - 1; order >= 0; order--)
                {
                    int index = (order + sample) % states.Length;
                    states[index].readSamples[sample] = MeasureRead(states[index]);
                }
            }

            for (int i = 0; i < states.Length; i++)
            {
                MeasureAllocations(states[i]);
                var result = CreateResult(category, states[i]);
                report.results.Add(result);

                if (states[i].codec.mustBeCorrect && !states[i].correct)
                    _candidateFailures.Add($"{category}/{scenario.name}/{states[i].codec.name}: {states[i].error}");
            }
        }
        finally
        {
            for (int i = 0; i < states.Length; i++)
                states[i]?.Dispose();
        }
    }

    private static CodecState<T> PrepareState<T>(string category, DeltaBenchmarkScenario<T> scenario,
        Codec<T> codec, BenchmarkEquals<T> valuesEqual)
    {
        var state = new CodecState<T>
        {
            codec = codec,
            scenario = scenario,
            encoded = new BitPacker(),
            encodedValues = new BitPacker[scenario.pairs.Length],
            working = new BitPacker(),
            rounds = Math.Max(1, (scenario.targetOperations + scenario.pairs.Length - 1) / scenario.pairs.Length),
            bits = new int[scenario.pairs.Length],
            writeSamples = new double[TimingSamples],
            readSamples = new double[TimingSamples]
        };
        state.operations = state.rounds * scenario.pairs.Length;

        for (int i = 0; i < scenario.pairs.Length; i++)
        {
            var pair = scenario.pairs[i];
            var valuePacker = new BitPacker();
            state.encodedValues[i] = valuePacker;
            bool changed = codec.write(valuePacker, pair.oldValue, pair.newValue);
            int writtenBits = valuePacker.positionInBits;
            state.bits[i] = writtenBits;

            valuePacker.ResetPositionAndMode(true);
            T decoded = default;
            bool readSucceeded = true;
            try
            {
                ReadForValidation(codec, valuePacker, pair.oldValue, ref decoded);
            }
            catch (Exception exception)
            {
                readSucceeded = false;
                state.canRead = false;
                state.correct = false;
                state.error = AppendError(state.error,
                    $"individual read failed at corpus index {i}: {exception.GetType().Name} {exception.Message}");
            }

            bool shouldChange = !valuesEqual(pair.oldValue, pair.newValue);
            if (readSucceeded && !valuesEqual(decoded, pair.newValue))
            {
                state.correct = false;
                state.error = AppendError(state.error, $"round-trip mismatch at corpus index {i}");
            }

            if (readSucceeded && valuePacker.positionInBits != writtenBits)
            {
                state.correct = false;
                state.error = AppendError(state.error,
                    $"reader consumed {valuePacker.positionInBits} of {writtenBits} bits at corpus index {i}");
            }

            if (codec.reportsChanged && changed != shouldChange)
            {
                state.correct = false;
                state.error = AppendError(state.error,
                    $"changed={changed} but expected {shouldChange} at corpus index {i}");
            }

            valuePacker.ResetPositionAndMode(true);
        }

        long totalEncodedBits = 0;
        for (int i = 0; i < state.bits.Length; i++)
            totalEncodedBits += state.bits[i];
        if (totalEncodedBits > int.MaxValue - 64)
            throw new InvalidOperationException($"Benchmark corpus {category}/{scenario.name}/{codec.name} is too large.");

        state.encoded.ResetPositionAndMode(false);
        state.encoded.EnsureBitsExist((int)totalEncodedBits + 64);
        state.working.ResetPositionAndMode(false);
        state.working.EnsureBitsExist((int)totalEncodedBits + 64);
        for (int i = 0; i < scenario.pairs.Length; i++)
        {
            var pair = scenario.pairs[i];
            codec.write(state.encoded, pair.oldValue, pair.newValue);
        }
        if (state.encoded.positionInBits != totalEncodedBits)
        {
            throw new InvalidOperationException(
                $"Benchmark corpus {category}/{scenario.name}/{codec.name} measured {totalEncodedBits} bits " +
                $"but batch encoding wrote {state.encoded.positionInBits} bits.");
        }
        if ((long)state.encoded.buffer.Length * 8 < totalEncodedBits + 64)
        {
            throw new InvalidOperationException(
                $"Benchmark corpus {category}/{scenario.name}/{codec.name} has only " +
                $"{state.encoded.buffer.Length * 8} buffer bits for {totalEncodedBits} encoded bits.");
        }
        state.encoded.ResetPositionAndMode(true);

        if (state.canRead)
        {
            T batchDecoded = default;
            int expectedBatchPosition = 0;
            for (int i = 0; i < scenario.pairs.Length; i++)
            {
                var pair = scenario.pairs[i];
                try
                {
                    ReadForValidation(codec, state.encoded, pair.oldValue, ref batchDecoded);
                }
                catch (Exception exception)
                {
                    state.correct = false;
                    state.error = AppendError(state.error,
                        $"Sequential read failed for {category}/{scenario.name}/{codec.name} at corpus index {i}; " +
                        $"position {state.encoded.positionInBits}, expected start {expectedBatchPosition}, " +
                        $"encoded total {totalEncodedBits}, capacity {state.encoded.buffer.Length * 8} bits: " +
                        exception.GetType().Name + " " + exception.Message);
                    break;
                }

                expectedBatchPosition += state.bits[i];
                if (state.encoded.positionInBits != expectedBatchPosition)
                {
                    state.correct = false;
                    state.error = AppendError(state.error,
                        $"Sequential read position mismatch for {category}/{scenario.name}/{codec.name} at corpus index {i}: " +
                        $"read {state.encoded.positionInBits}, expected {expectedBatchPosition}.");
                    break;
                }
                if (!valuesEqual(batchDecoded, pair.newValue))
                {
                    state.correct = false;
                    state.error = AppendError(state.error,
                        $"Sequential round-trip mismatch for {category}/{scenario.name}/{codec.name} at corpus index {i}.");
                    break;
                }
            }
        }
        state.encoded.ResetPositionAndMode(true);

        // Pre-grow the write buffer before timing.
        RunWriteBatches(state, 1);
        return state;
    }

    private static void ReadForValidation<T>(Codec<T> codec, BitPacker packer, T oldValue, ref T value)
    {
        if (codec.name != "current")
        {
            codec.read(packer, oldValue, ref value);
            return;
        }

        // Current collection readers can log internally while producing an invalid result. Preserve
        // that as a benchmark correctness failure without relaxing log assertions for candidates.
        bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            codec.read(packer, oldValue, ref value);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
        }
    }

    private static string AppendError(string existing, string next)
    {
        if (string.IsNullOrEmpty(existing))
            return next;
        return existing + "; " + next;
    }

    private static double MeasureWrite<T>(CodecState<T> state)
    {
        var watch = Stopwatch.StartNew();
        RunWriteBatches(state, state.rounds);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds * 1_000_000.0 / state.operations;
    }

    private static double MeasureRead<T>(CodecState<T> state)
    {
        if (!state.canRead)
            return -1;
        var watch = Stopwatch.StartNew();
        RunReadBatches(state, state.rounds);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds * 1_000_000.0 / state.operations;
    }

    private static void RunWriteBatches<T>(CodecState<T> state, int rounds)
    {
        var pairs = state.scenario.pairs;
        var codec = state.codec;
        for (int round = 0; round < rounds; round++)
        {
            state.working.ResetPositionAndMode(false);
            for (int i = 0; i < pairs.Length; i++)
                codec.write(state.working, pairs[i].oldValue, pairs[i].newValue);
        }
    }

    private static void RunReadBatches<T>(CodecState<T> state, int rounds)
    {
        if (!state.canRead)
            return;
        var pairs = state.scenario.pairs;
        var codec = state.codec;
        T value = default;
        for (int round = 0; round < rounds; round++)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                var encodedValue = state.encodedValues[i];
                encodedValue.ResetPositionAndMode(true);
                codec.read(encodedValue, pairs[i].oldValue, ref value);
            }
        }
        GC.KeepAlive(value);
    }

    private static void MeasureAllocations<T>(CodecState<T> state)
    {
        int rounds = Math.Max(1, Math.Min(state.rounds,
            (10_000 + state.scenario.pairs.Length - 1) / state.scenario.pairs.Length));
        int operations = rounds * state.scenario.pairs.Length;

        long beforeWrite = GC.GetAllocatedBytesForCurrentThread();
        RunWriteBatches(state, rounds);
        long writeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeWrite;

        long beforeRead = GC.GetAllocatedBytesForCurrentThread();
        if (state.canRead)
            RunReadBatches(state, rounds);
        long readBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRead;

        state.writeAllocatedBytes = (double)writeBytes / operations;
        state.readAllocatedBytes = state.canRead ? (double)readBytes / operations : -1;
    }

    private static BenchmarkResult CreateResult<T>(string category, CodecState<T> state)
    {
        int[] sortedBits = (int[])state.bits.Clone();
        Array.Sort(sortedBits);
        long totalBits = 0;
        for (int i = 0; i < state.bits.Length; i++)
            totalBits += state.bits[i];

        return new BenchmarkResult
        {
            category = category,
            scenario = state.scenario.name,
            codec = state.codec.name,
            candidate = state.codec.candidate,
            corpusSize = state.scenario.pairs.Length,
            timedOperations = state.operations,
            correct = state.correct,
            canRead = state.canRead,
            error = state.error ?? string.Empty,
            meanBits = (double)totalBits / state.bits.Length,
            p50Bits = Percentile(sortedBits, 0.50),
            p95Bits = Percentile(sortedBits, 0.95),
            maxBits = sortedBits[sortedBits.Length - 1],
            medianWriteNanoseconds = Median(state.writeSamples),
            medianReadNanoseconds = Median(state.readSamples),
            writeAllocatedBytesPerOperation = state.writeAllocatedBytes,
            readAllocatedBytesPerOperation = state.readAllocatedBytes
        };
    }

    private static int Percentile(int[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static Codec<T> CurrentCodec<T>(BenchmarkWrite<T> write, BenchmarkRead<T> read)
    {
        return new Codec<T>("current", write, read, false);
    }

    private static Codec<T> FullCodec<T>(BenchmarkWrite<T> write, BenchmarkRead<T> read)
    {
        return new Codec<T>("full", write, read, false, false);
    }

    private static bool FloatBitsEqual(float left, float right)
    {
        return BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
    }

    private static bool DoubleBitsEqual(double left, double right)
    {
        return BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
    }

    private static bool PackedValuesEqual(PackedBenchmarkValue left, PackedBenchmarkValue right) =>
        left.Equals(right);

    private static bool Vector3BitsEqual(Vector3 left, Vector3 right)
    {
        return FloatBitsEqual(left.x, right.x) && FloatBitsEqual(left.y, right.y) &&
               FloatBitsEqual(left.z, right.z);
    }

    private static bool QuaternionBitsEqual(Quaternion left, Quaternion right)
    {
        return FloatBitsEqual(left.x, right.x) && FloatBitsEqual(left.y, right.y) &&
               FloatBitsEqual(left.z, right.z) && FloatBitsEqual(left.w, right.w);
    }

    private static bool ListsEqual(List<int> left, List<int> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    private static bool DictionariesEqual(Dictionary<int, int> left, Dictionary<int, int> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Count != right.Count) return false;
        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out int value) || value != pair.Value) return false;
        return true;
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    private static void LogSummary(BenchmarkReport report, string outputPath)
    {
        Debug.Log($"[DeltaCodecBench] Wrote {report.results.Count} rows to {outputPath}");
        for (int i = 0; i < report.results.Count; i++)
        {
            var result = report.results[i];
            Debug.Log($"[DeltaCodecBench] {result.category}/{result.scenario}/{result.codec} | " +
                      $"bits mean {result.meanBits:F1}, p95 {result.p95Bits}, max {result.maxBits} | " +
                      $"write {result.medianWriteNanoseconds:F1} ns, read {result.medianReadNanoseconds:F1} ns | " +
                      $"alloc W/R {result.writeAllocatedBytesPerOperation:F1}/" +
                      $"{result.readAllocatedBytesPerOperation:F1} B | correct {result.correct}");
        }
    }
}

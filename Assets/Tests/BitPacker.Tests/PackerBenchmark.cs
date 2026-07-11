using System;
using System.Diagnostics;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Benchmarks for measuring BitPacker throughput under realistic game networking load.
/// Simulates the packing workload of N players each sending 1 RPC to all other players per tick.
/// Run from Unity Test Runner (Edit Mode).
/// </summary>
public class PackerBenchmark
{
    const int WARMUP_TICKS = 10;
    const int MEASURE_TICKS = 100;

    struct BenchmarkEntryCache
    {
        public UnionRPCHeader previousHeader;
        public Size previousDataLen;
        public ulong previousStateVersion;
        public BitPacker data;
        public int bitLength;
        public bool isValid;
    }

    sealed class NoopRPCBatchBackend : IRPCBatchBackend
    {
        public int GetMTU(PlayerID player, Channel channel, bool asServer) => int.MaxValue;
        public void Send(PlayerID player, RPCBatchPacket data, Channel channel) { }
        public void Subscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback) { }
        public void Unsubscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback) { }
    }

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    // ───────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────

    /// <summary>
    /// Packs a typical RPC payload: position, rotation, health, speed, grounded flag.
    /// This represents what a single RPC call might serialize.
    /// </summary>
    static void PackTypicalRPCPayload(BitPacker packer, int seed)
    {
        Packer<Vector3>.Write(packer, new Vector3(seed * 0.1f, seed * 0.2f, seed * 0.3f));
        Packer<Quaternion>.Write(packer, new Quaternion(0.1f * seed, 0.2f, 0.3f, 0.9f));
        Packer<int>.Write(packer, 100 - seed);
        Packer<float>.Write(packer, 5.5f + seed);
        Packer<bool>.Write(packer, seed % 2 == 0);
    }

    /// <summary>
    /// Reads back the typical RPC payload.
    /// </summary>
    static void UnpackTypicalRPCPayload(BitPacker packer)
    {
        Vector3 v3 = default;
        Quaternion q = default;
        int hp = default;
        float spd = default;
        bool grounded = default;

        Packer<Vector3>.Read(packer, ref v3);
        Packer<Quaternion>.Read(packer, ref q);
        Packer<int>.Read(packer, ref hp);
        Packer<float>.Read(packer, ref spd);
        Packer<bool>.Read(packer, ref grounded);
    }

    static UnionRPCHeader MakeHeader(int senderId, int networkId, int rpcId)
    {
        return new UnionRPCHeader(new NetworkIdentityRPCHeader
        {
            senderId = new PlayerID((ulong)senderId, false),
            networkId = new NetworkID((ulong)networkId),
            sceneId = new SceneID(0),
            rpcId = new Size((uint)rpcId),
            targetId = null
        });
    }

    static void WriteBitDataLegacy(BitPacker destination, BitData content)
    {
        int bitLength = content.bitLength;
        if (bitLength == 0) return;

        var source = content.packer;
        int sourcePosition = source.positionInBits;
        source.SetBitPosition(content.bitOrigin);
        destination.EnsureBitsExist(bitLength);
        int chunks = bitLength >> 6;
        byte excess = (byte)(bitLength & 63);

        for (int i = 0; i < chunks; i++)
            destination.WriteBitsWithoutChecks(source.ReadBits(64), 64);
        if (excess != 0)
            destination.WriteBitsWithoutChecks(source.ReadBits(excess), excess);

        source.SetBitPosition(sourcePosition);
    }

    static void WriteByteAlignedBitsStateful(BitPacker destination, BitPacker source, int bits)
    {
        destination.EnsureBitsExist(bits);
        int sourcePosition = source.positionInBits;
        source.SetBitPosition(0);
        int chunks = bits >> 6;
        byte excess = (byte)(bits & 63);

        for (int i = 0; i < chunks; i++)
            destination.WriteBitsWithoutChecks(source.ReadBitsWithoutChecks(64), 64);
        if (excess != 0)
            destination.WriteBitsWithoutChecks(source.ReadBitsWithoutChecks(excess), excess);

        source.SetBitPosition(sourcePosition);
    }

    static bool LogicalBitsEqual(BitPacker a, BitPacker b)
    {
        if (a.positionInBits != b.positionInBits)
            return false;

        int fullBytes = a.positionInBits >> 3;
        var aBytes = a.ToByteData().span;
        var bBytes = b.ToByteData().span;
        if (!aBytes.Slice(0, fullBytes).SequenceEqual(bBytes.Slice(0, fullBytes)))
            return false;

        int remainingBits = a.positionInBits & 7;
        if (remainingBits == 0)
            return true;

        int mask = (1 << remainingBits) - 1;
        return (aBytes[fullBytes] & mask) == (bBytes[fullBytes] & mask);
    }

    static void GetBenchmarkEntry(ref BenchmarkEntryCache cacheA, ref BenchmarkEntryCache cacheB,
        UnionRPCHeader previousHeader, Size previousDataLen, ulong previousStateVersion,
        UnionRPCHeader header, BitData content, out BitPacker data, out int bitLength)
    {
        if (cacheA.isValid &&
            ((previousStateVersion != 0 && cacheA.previousStateVersion == previousStateVersion) ||
             (cacheA.previousDataLen.value == previousDataLen.value &&
              cacheA.previousHeader.Equals(previousHeader))))
        {
            data = cacheA.data;
            bitLength = cacheA.bitLength;
            return;
        }

        if (cacheB.isValid &&
            ((previousStateVersion != 0 && cacheB.previousStateVersion == previousStateVersion) ||
             (cacheB.previousDataLen.value == previousDataLen.value &&
              cacheB.previousHeader.Equals(previousHeader))))
        {
            data = cacheB.data;
            bitLength = cacheB.bitLength;
            return;
        }

        ref var cache = ref (cacheA.isValid ? ref cacheB : ref cacheA);
        cache.data.ResetPositionAndMode(false);
        DeltaPacker<UnionRPCHeader>.Write(cache.data, previousHeader, header);
        DeltaPackInteger.WriteIndex(cache.data, previousDataLen, content.bitLength);
        cache.data.WriteBitDataWithoutConsumingIt(content);
        cache.previousHeader = previousHeader;
        cache.previousDataLen = previousDataLen;
        cache.previousStateVersion = previousStateVersion;
        cache.bitLength = cache.data.positionInBits;
        cache.isValid = true;

        data = cache.data;
        bitLength = cache.bitLength;
    }

    static void RunBaselineFanout(UnionRPCHeader[] headers, BitData[] contents, BitPacker[] batches,
        UnionRPCHeader[] lastHeaders, Size[] lastSizes)
    {
        Array.Clear(lastHeaders, 0, lastHeaders.Length);
        Array.Clear(lastSizes, 0, lastSizes.Length);
        for (int i = 0; i < batches.Length; i++)
            batches[i].ResetPositionAndMode(false);

        for (int sender = 0; sender < headers.Length; sender++)
        {
            var content = contents[sender];
            for (int recipient = headers.Length - 1; recipient >= 0; recipient--)
            {
                if (sender == recipient) continue;
                var batch = batches[recipient];
                DeltaPacker<UnionRPCHeader>.Write(batch, lastHeaders[recipient], headers[sender]);
                DeltaPackInteger.WriteIndex(batch, lastSizes[recipient], content.bitLength);
                WriteBitDataLegacy(batch, content);
                lastHeaders[recipient] = headers[sender];
                lastSizes[recipient] = content.bitLength;
            }
        }
    }

    static void RunCachedFanout(UnionRPCHeader[] headers, BitData[] contents, BitPacker[] batches,
        UnionRPCHeader[] lastHeaders, Size[] lastSizes, ref BenchmarkEntryCache cacheA,
        ref BenchmarkEntryCache cacheB, ulong[] lastStateVersions)
    {
        Array.Clear(lastHeaders, 0, lastHeaders.Length);
        Array.Clear(lastSizes, 0, lastSizes.Length);
        Array.Clear(lastStateVersions, 0, lastStateVersions.Length);
        for (int i = 0; i < batches.Length; i++)
            batches[i].ResetPositionAndMode(false);

        for (int sender = 0; sender < headers.Length; sender++)
        {
            ulong stateVersion = (ulong)sender + 1;
            cacheA.isValid = false;
            cacheB.isValid = false;
            var content = contents[sender];

            for (int recipient = headers.Length - 1; recipient >= 0; recipient--)
            {
                if (sender == recipient) continue;
                GetBenchmarkEntry(ref cacheA, ref cacheB, lastHeaders[recipient], lastSizes[recipient],
                    lastStateVersions[recipient], headers[sender], content, out var entry, out int entryBitLength);
                batches[recipient].WriteBitsWithoutConsumingItUnchecked(entry, entryBitLength);
                lastHeaders[recipient] = headers[sender];
                lastSizes[recipient] = content.bitLength;
                lastStateVersions[recipient] = stateVersion;
            }
        }
    }

    // ───────────────────────────────────────────────
    //  1. Raw packing write throughput (Packer<T>.Write)
    // ───────────────────────────────────────────────

    [Test]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_RawPacking_Write(int playerCount)
    {
        var packer = BitPackerPool.Get();
        int opsPerTick = playerCount * (playerCount - 1);

        // warmup
        for (int t = 0; t < WARMUP_TICKS; t++)
        {
            for (int sender = 0; sender < playerCount; sender++)
            {
                for (int r = 0; r < playerCount - 1; r++)
                {
                    packer.ResetPositionAndMode(false);
                    PackTypicalRPCPayload(packer, sender);
                }
            }
        }

        // measure
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < MEASURE_TICKS; t++)
        {
            for (int sender = 0; sender < playerCount; sender++)
            {
                for (int r = 0; r < playerCount - 1; r++)
                {
                    packer.ResetPositionAndMode(false);
                    PackTypicalRPCPayload(packer, sender);
                }
            }
        }
        sw.Stop();

        long totalOps = (long)opsPerTick * MEASURE_TICKS;
        double msPerTick = sw.Elapsed.TotalMilliseconds / MEASURE_TICKS;
        double opsPerSec = totalOps / sw.Elapsed.TotalSeconds;

        packer.Dispose();

        Debug.Log($"[Raw Write] {playerCount} players | " +
                  $"{opsPerTick:N0} packs/tick | " +
                  $"{msPerTick:F3} ms/tick | " +
                  $"{opsPerSec:N0} ops/sec | " +
                  $"{totalOps:N0} total ops in {sw.ElapsedMilliseconds} ms");
    }

    // ───────────────────────────────────────────────
    //  2. Raw packing write + read round-trip
    // ───────────────────────────────────────────────

    [Test]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_RawPacking_WriteAndRead(int playerCount)
    {
        var packer = BitPackerPool.Get();
        int opsPerTick = playerCount * (playerCount - 1);

        // warmup
        for (int t = 0; t < WARMUP_TICKS; t++)
        {
            for (int sender = 0; sender < playerCount; sender++)
            {
                for (int r = 0; r < playerCount - 1; r++)
                {
                    packer.ResetPositionAndMode(false);
                    PackTypicalRPCPayload(packer, sender);
                    packer.ResetPositionAndMode(true);
                    UnpackTypicalRPCPayload(packer);
                }
            }
        }

        // measure
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < MEASURE_TICKS; t++)
        {
            for (int sender = 0; sender < playerCount; sender++)
            {
                for (int r = 0; r < playerCount - 1; r++)
                {
                    packer.ResetPositionAndMode(false);
                    PackTypicalRPCPayload(packer, sender);
                    packer.ResetPositionAndMode(true);
                    UnpackTypicalRPCPayload(packer);
                }
            }
        }
        sw.Stop();

        long totalOps = (long)opsPerTick * MEASURE_TICKS;
        double msPerTick = sw.Elapsed.TotalMilliseconds / MEASURE_TICKS;

        packer.Dispose();

        Debug.Log($"[Write+Read] {playerCount} players | " +
                  $"{opsPerTick:N0} round-trips/tick | " +
                  $"{msPerTick:F3} ms/tick | " +
                  $"{totalOps:N0} total ops in {sw.ElapsedMilliseconds} ms");
    }

    // ───────────────────────────────────────────────
    //  3. RPC Batch header delta packing
    //     Simulates what RPCBatch.Queue does per recipient:
    //     delta-pack UnionRPCHeader + content Size for each RPC.
    // ───────────────────────────────────────────────

    [Test]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_RPCBatch_HeaderDelta(int playerCount)
    {
        // Pre-generate headers: one per sender
        var headers = new UnionRPCHeader[playerCount];
        for (int i = 0; i < playerCount; i++)
            headers[i] = MakeHeader(senderId: i + 1, networkId: i + 1, rpcId: i % 20);

        var contentSizes = new Size[playerCount];
        for (int i = 0; i < playerCount; i++)
            contentSizes[i] = new Size((uint)(20 + i % 30)); // typical content bit lengths

        var packer = BitPackerPool.Get();

        // warmup
        for (int t = 0; t < WARMUP_TICKS; t++)
        {
            // For each recipient, batch all RPCs from all senders
            for (int recipient = 0; recipient < playerCount; recipient++)
            {
                packer.ResetPositionAndMode(false);
                var lastHeader = default(UnionRPCHeader);
                var lastSize = default(Size);

                for (int sender = 0; sender < playerCount; sender++)
                {
                    if (sender == recipient) continue;
                    DeltaPacker<UnionRPCHeader>.Write(packer, lastHeader, headers[sender]);
                    DeltaPackInteger.WriteIndex(packer, lastSize, contentSizes[sender]);
                    lastHeader = headers[sender];
                    lastSize = contentSizes[sender];
                }
            }
        }

        // measure
        var sw = Stopwatch.StartNew();
        long deltaOps = 0;

        for (int t = 0; t < MEASURE_TICKS; t++)
        {
            for (int recipient = 0; recipient < playerCount; recipient++)
            {
                packer.ResetPositionAndMode(false);
                var lastHeader = default(UnionRPCHeader);
                var lastSize = default(Size);

                for (int sender = 0; sender < playerCount; sender++)
                {
                    if (sender == recipient) continue;
                    DeltaPacker<UnionRPCHeader>.Write(packer, lastHeader, headers[sender]);
                    DeltaPackInteger.WriteIndex(packer, lastSize, contentSizes[sender]);
                    lastHeader = headers[sender];
                    lastSize = contentSizes[sender];
                    deltaOps++;
                }
            }
        }
        sw.Stop();

        long deltaOpsPerTick = deltaOps / MEASURE_TICKS;
        double msPerTick = sw.Elapsed.TotalMilliseconds / MEASURE_TICKS;

        packer.Dispose();

        Debug.Log($"[Batch Delta] {playerCount} players | " +
                  $"{deltaOpsPerTick:N0} delta packs/tick | " +
                  $"{msPerTick:F3} ms/tick | " +
                  $"{deltaOps:N0} total ops in {sw.ElapsedMilliseconds} ms");
    }

    [Test]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_RPCBatch_FanoutCache(int playerCount)
    {
        var headers = new UnionRPCHeader[playerCount];
        var payloadPackers = new BitPacker[playerCount];
        var contents = new BitData[playerCount];
        var baselineBatches = new BitPacker[playerCount];
        var cachedBatches = new BitPacker[playerCount];

        for (int i = 0; i < playerCount; i++)
        {
            headers[i] = MakeHeader(i + 1, i + 1, i % 20);
            payloadPackers[i] = BitPackerPool.Get();
            PackTypicalRPCPayload(payloadPackers[i], i);
            contents[i] = new BitData(payloadPackers[i]);
            baselineBatches[i] = BitPackerPool.Get();
            cachedBatches[i] = BitPackerPool.Get();
        }

        var baselineLastHeaders = new UnionRPCHeader[playerCount];
        var baselineLastSizes = new Size[playerCount];
        var cachedLastHeaders = new UnionRPCHeader[playerCount];
        var cachedLastSizes = new Size[playerCount];
        var cachedLastStateVersions = new ulong[playerCount];
        var cacheA = new BenchmarkEntryCache { data = BitPackerPool.Get() };
        var cacheB = new BenchmarkEntryCache { data = BitPackerPool.Get() };

        for (int i = 0; i < WARMUP_TICKS; i++)
        {
            RunBaselineFanout(headers, contents, baselineBatches, baselineLastHeaders, baselineLastSizes);
            RunCachedFanout(headers, contents, cachedBatches, cachedLastHeaders, cachedLastSizes,
                ref cacheA, ref cacheB, cachedLastStateVersions);
        }

        var baselineWatch = Stopwatch.StartNew();
        for (int i = 0; i < MEASURE_TICKS; i++)
            RunBaselineFanout(headers, contents, baselineBatches, baselineLastHeaders, baselineLastSizes);
        baselineWatch.Stop();

        var cachedWatch = Stopwatch.StartNew();
        for (int i = 0; i < MEASURE_TICKS; i++)
            RunCachedFanout(headers, contents, cachedBatches, cachedLastHeaders, cachedLastSizes,
                ref cacheA, ref cacheB, cachedLastStateVersions);
        cachedWatch.Stop();

        for (int i = 0; i < playerCount; i++)
        {
            Assert.That(cachedBatches[i].positionInBits, Is.EqualTo(baselineBatches[i].positionInBits));
            Assert.That(LogicalBitsEqual(cachedBatches[i], baselineBatches[i]),
                Is.True, $"Cached batch differed for recipient {i}.");
        }

        double baselineMs = baselineWatch.Elapsed.TotalMilliseconds / MEASURE_TICKS;
        double cachedMs = cachedWatch.Elapsed.TotalMilliseconds / MEASURE_TICKS;
        Debug.Log($"[Batch Fanout Cache] {playerCount} players | baseline {baselineMs:F3} ms/tick | " +
                  $"cached {cachedMs:F3} ms/tick | {baselineMs / cachedMs:F2}x faster");

        cacheA.data.Dispose();
        cacheB.data.Dispose();
        for (int i = 0; i < playerCount; i++)
        {
            payloadPackers[i].Dispose();
            baselineBatches[i].Dispose();
            cachedBatches[i].Dispose();
        }
    }

    [Test]
    public void Benchmark_RPCBatch_IndexLookup()
    {
        const int playerCount = 100;
        const int channelCount = 4;
        const int passes = 10_000;
        var keys = new BatchKey[playerCount * channelCount];
        var original = new NativeHashMap<BatchKey, int>(keys.Length, Allocator.Temp);
        using var optimized = new BatchIndexMap(playerCount);

        int keyIndex = 0;
        for (int player = 1; player <= playerCount; player++)
        {
            for (int channel = 0; channel < channelCount; channel++)
            {
                var key = new BatchKey
                {
                    playerId = new PlayerID((ulong)player, false),
                    channel = (Channel)channel
                };
                keys[keyIndex] = key;
                original[key] = keyIndex;
                optimized.Set(key.playerId.id.value, key.channel, keyIndex);
                keyIndex++;
            }
        }

        long warmupSum = 0;
        for (int pass = 0; pass < 100; pass++)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                warmupSum += original[key];
                optimized.TryGetValue(key.playerId.id.value, key.channel, out int optimizedValue);
                warmupSum += optimizedValue;
            }
        }
        Assert.That(warmupSum, Is.GreaterThan(0));

        long originalSum = 0;
        var originalWatch = Stopwatch.StartNew();
        for (int pass = 0; pass < passes; pass++)
            for (int i = 0; i < keys.Length; i++)
                originalSum += original[keys[i]];
        originalWatch.Stop();

        long optimizedSum = 0;
        var optimizedWatch = Stopwatch.StartNew();
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                optimized.TryGetValue(key.playerId.id.value, key.channel, out int value);
                optimizedSum += value;
            }
        }
        optimizedWatch.Stop();

        original.Dispose();
        Assert.That(optimizedSum, Is.EqualTo(originalSum));
        Debug.Log($"[Batch Index Lookup] 4M lookups | original {originalWatch.Elapsed.TotalMilliseconds:F2} ms | " +
                   $"optimized {optimizedWatch.Elapsed.TotalMilliseconds:F2} ms | " +
                   $"{originalWatch.Elapsed.TotalMilliseconds / optimizedWatch.Elapsed.TotalMilliseconds:F2}x faster");
    }

    [Test]
    [TestCase(1)]
    [TestCase(2)]
    public void Benchmark_RPCBatch_DirectQueue(int recipientCount)
    {
        const int warmupOperations = 5_000;
        const int measuredOperations = 250_000;
        const int sampleCount = 5;
        const int flushInterval = 64;
        var targets = new PlayerID[recipientCount];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = new PlayerID((ulong)(i + 1), false);

        var backend = new NoopRPCBatchBackend();
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });
        using var payload = BitPackerPool.Get();
        PackTypicalRPCPayload(payload, 7);
        var content = new BitData(payload);

        void Run(int operationCount)
        {
            for (int operation = 0; operation < operationCount; operation++)
            {
                var header = MakeHeader(1, 1, operation & 15);
                if (recipientCount == 1)
                    batch.Queue(targets[0], header, content, Channel.ReliableOrdered);
                else
                    batch.Queue(targets, header, content, Channel.ReliableOrdered);

                if ((operation & (flushInterval - 1)) == flushInterval - 1)
                    batch.Flush();
            }
            batch.Flush();
        }

        Run(warmupOperations);
        var samples = new double[sampleCount];
        for (int sample = 0; sample < sampleCount; sample++)
        {
            var watch = Stopwatch.StartNew();
            Run(measuredOperations);
            watch.Stop();
            samples[sample] = watch.Elapsed.TotalMilliseconds * 1_000_000.0 /
                              (measuredOperations * recipientCount);
        }

        Array.Sort(samples);
        double nsPerRecipient = samples[sampleCount / 2];
        Debug.Log($"[Batch Direct Queue] {recipientCount} recipient(s) | " +
                  $"median {nsPerRecipient:F1} ns/recipient | " +
                  $"range {samples[0]:F1}-{samples[sampleCount - 1]:F1}");
    }

    [Test]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(12)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    [TestCase(256)]
    [TestCase(512)]
    [TestCase(1400)]
    public void Benchmark_UnalignedCachedEntryCopy(int byteCount)
    {
        var bytes = new byte[byteCount];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 31 + 17);

        using var source = BitPackerPool.Get();
        using var statefulDestination = BitPackerPool.Get();
        using var optimizedDestination = BitPackerPool.Get();
        source.WriteBytes(bytes);
        int sourcePosition = source.positionInBits;
        int bits = (byteCount << 3) - 3;
        int iterations = Math.Max(10_000, 10_000_000 / byteCount);

        for (int i = 0; i < 100; i++)
        {
            statefulDestination.ResetPositionAndMode(false);
            statefulDestination.WriteBit(true);
            WriteByteAlignedBitsStateful(statefulDestination, source, bits);
            optimizedDestination.ResetPositionAndMode(false);
            optimizedDestination.WriteBit(true);
            optimizedDestination.WriteBitsWithoutConsumingItUnchecked(source, bits);
        }

        var statefulWatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            statefulDestination.ResetPositionAndMode(false);
            statefulDestination.WriteBit(true);
            WriteByteAlignedBitsStateful(statefulDestination, source, bits);
        }
        statefulWatch.Stop();

        var optimizedWatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            optimizedDestination.ResetPositionAndMode(false);
            optimizedDestination.WriteBit(true);
            optimizedDestination.WriteBitsWithoutConsumingItUnchecked(source, bits);
        }
        optimizedWatch.Stop();

        Assert.That(source.positionInBits, Is.EqualTo(sourcePosition));
        Assert.That(optimizedDestination.positionInBits, Is.EqualTo(statefulDestination.positionInBits));
        Assert.That(LogicalBitsEqual(optimizedDestination, statefulDestination), Is.True);
        Debug.Log($"[Unaligned Cached Copy] {byteCount} bytes | stateful " +
                  $"{statefulWatch.Elapsed.TotalMilliseconds:F2} ms | optimized " +
                  $"{optimizedWatch.Elapsed.TotalMilliseconds:F2} ms | " +
                  $"{statefulWatch.Elapsed.TotalMilliseconds / optimizedWatch.Elapsed.TotalMilliseconds:F2}x faster");
    }

    // ───────────────────────────────────────────────
    //  4. Combined: payload write + batch header delta
    //     Full simulation of what happens per tick.
    // ───────────────────────────────────────────────

    [Test]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_FullTick_Simulation(int playerCount)
    {
        // Pre-generate headers
        var headers = new UnionRPCHeader[playerCount];
        for (int i = 0; i < playerCount; i++)
            headers[i] = MakeHeader(senderId: i + 1, networkId: i + 1, rpcId: i % 20);

        // One packer per recipient for batching (like RPCBatch does)
        var batchPackers = new BitPacker[playerCount];
        for (int i = 0; i < playerCount; i++)
            batchPackers[i] = BitPackerPool.Get();

        var payloadPacker = BitPackerPool.Get();

        // warmup
        for (int t = 0; t < WARMUP_TICKS; t++)
        {
            for (int i = 0; i < playerCount; i++)
                batchPackers[i].ResetPositionAndMode(false);

            var lastHeaders = new UnionRPCHeader[playerCount];
            var lastSizes = new Size[playerCount];

            for (int sender = 0; sender < playerCount; sender++)
            {
                // Pack the RPC payload once
                payloadPacker.ResetPositionAndMode(false);
                PackTypicalRPCPayload(payloadPacker, sender);
                var contentSize = new Size((uint)payloadPacker.positionInBits);

                // Then batch-write header + size for each recipient
                for (int recipient = 0; recipient < playerCount; recipient++)
                {
                    if (sender == recipient) continue;
                    DeltaPacker<UnionRPCHeader>.Write(batchPackers[recipient], lastHeaders[recipient], headers[sender]);
                    DeltaPackInteger.WriteIndex(batchPackers[recipient], lastSizes[recipient], contentSize);
                    lastHeaders[recipient] = headers[sender];
                    lastSizes[recipient] = contentSize;
                }
            }
        }

        // measure
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < MEASURE_TICKS; t++)
        {
            for (int i = 0; i < playerCount; i++)
                batchPackers[i].ResetPositionAndMode(false);

            var lastHeaders = new UnionRPCHeader[playerCount];
            var lastSizes = new Size[playerCount];

            for (int sender = 0; sender < playerCount; sender++)
            {
                // Pack RPC payload
                payloadPacker.ResetPositionAndMode(false);
                PackTypicalRPCPayload(payloadPacker, sender);
                var contentSize = new Size((uint)payloadPacker.positionInBits);

                // Batch header delta for each recipient
                for (int recipient = 0; recipient < playerCount; recipient++)
                {
                    if (sender == recipient) continue;
                    DeltaPacker<UnionRPCHeader>.Write(batchPackers[recipient], lastHeaders[recipient], headers[sender]);
                    DeltaPackInteger.WriteIndex(batchPackers[recipient], lastSizes[recipient], contentSize);
                    lastHeaders[recipient] = headers[sender];
                    lastSizes[recipient] = contentSize;
                }
            }
        }
        sw.Stop();

        int packsPerTick = playerCount; // payload packs
        int deltaOpsPerTick = playerCount * (playerCount - 1); // header deltas
        double msPerTick = sw.Elapsed.TotalMilliseconds / MEASURE_TICKS;

        payloadPacker.Dispose();
        for (int i = 0; i < playerCount; i++)
            batchPackers[i].Dispose();

        Debug.Log($"[Full Tick] {playerCount} players | " +
                  $"{packsPerTick} payload packs + {deltaOpsPerTick:N0} header deltas per tick | " +
                  $"{msPerTick:F3} ms/tick | " +
                  $"{sw.ElapsedMilliseconds} ms for {MEASURE_TICKS} ticks");
    }

    // ───────────────────────────────────────────────
    //  5. Per-type breakdown
    //     Measures individual type packing cost.
    // ───────────────────────────────────────────────

    [Test]
    public void Benchmark_PerType_Breakdown()
    {
        const int iterations = 1_000_000;
        var packer = BitPackerPool.Get();

        // Vector3
        packer.ResetPositionAndMode(false);
        var swV3 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<Vector3>.Write(packer, new Vector3(1.5f, 2.3f, 4.1f));
        }
        swV3.Stop();

        // Quaternion
        packer.ResetPositionAndMode(false);
        var swQ = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<Quaternion>.Write(packer, new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        }
        swQ.Stop();

        // int
        packer.ResetPositionAndMode(false);
        var swInt = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<int>.Write(packer, 42);
        }
        swInt.Stop();

        // float
        packer.ResetPositionAndMode(false);
        var swFloat = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<float>.Write(packer, 100.5f);
        }
        swFloat.Stop();

        // bool
        packer.ResetPositionAndMode(false);
        var swBool = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<bool>.Write(packer, true);
        }
        swBool.Stop();

        // string (short)
        packer.ResetPositionAndMode(false);
        var swStr = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            packer.ResetPosition();
            Packer<string>.Write(packer, "Hello");
        }
        swStr.Stop();

        packer.Dispose();

        Debug.Log($"[Per-Type] {iterations:N0} writes each:\n" +
                  $"  Vector3:    {swV3.Elapsed.TotalMilliseconds:F1} ms ({iterations / swV3.Elapsed.TotalSeconds:N0}/sec)\n" +
                  $"  Quaternion: {swQ.Elapsed.TotalMilliseconds:F1} ms ({iterations / swQ.Elapsed.TotalSeconds:N0}/sec)\n" +
                  $"  int:        {swInt.Elapsed.TotalMilliseconds:F1} ms ({iterations / swInt.Elapsed.TotalSeconds:N0}/sec)\n" +
                  $"  float:      {swFloat.Elapsed.TotalMilliseconds:F1} ms ({iterations / swFloat.Elapsed.TotalSeconds:N0}/sec)\n" +
                  $"  bool:       {swBool.Elapsed.TotalMilliseconds:F1} ms ({iterations / swBool.Elapsed.TotalSeconds:N0}/sec)\n" +
                  $"  string(5):  {swStr.Elapsed.TotalMilliseconds:F1} ms ({iterations / swStr.Elapsed.TotalSeconds:N0}/sec)");
    }
}

using System.Diagnostics;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
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

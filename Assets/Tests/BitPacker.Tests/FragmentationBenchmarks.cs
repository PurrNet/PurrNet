using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using PurrNet.Transports;
using Debug = UnityEngine.Debug;

/// <summary>
/// Allocation guards and repeatable microbenchmarks for the transport-independent
/// fragmentation core. Times are diagnostic rather than pass/fail thresholds.
/// </summary>
public class FragmentationBenchmarks
{
    sealed class LoopbackState
    {
        public readonly FragmentationLayer receiver;
        public int completed;
        public uint checksum;

        public LoopbackState(FragmentationLayer receiver)
        {
            this.receiver = receiver;
        }
    }

    sealed class CountState
    {
        public int packets;
    }

    static readonly FragmentationLayer.FragmentCallback<LoopbackState> _loopback = Loopback;
    static readonly FragmentationLayer.FragmentCallback<CountState> _count = Count;

    [Test]
    public void Fragmentation_SteadyStateFragmentedRoundtrip_AllocatesZeroManagedBytes()
    {
        const int mtu = 256;
        const int iterations = 10_000;
        var payload = CreatePayload(1400);

        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var state = new LoopbackState(receiver);

        for (int i = 0; i < 128; i++)
            sender.Send(new ByteData(payload, 0, payload.Length), mtu, 0, state, _loopback);

        state.completed = 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.GetAllocatedBytesForCurrentThread(); // initialize any runtime bookkeeping

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            sender.Send(new ByteData(payload, 0, payload.Length), mtu, 0, state, _loopback);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, state.completed);
        Assert.AreEqual(0, allocated, $"Steady-state fragmentation allocated {allocated} managed bytes.");
    }

    [Test]
    public void Fragmentation_SteadyStateUnfragmentedFraming_AllocatesZeroManagedBytes()
    {
        const int iterations = 100_000;
        var payload = CreatePayload(64);
        using var layer = new FragmentationLayer();
        var state = new CountState();

        for (int i = 0; i < 128; i++)
            layer.Send(new ByteData(payload, 0, payload.Length), 1200, 0, state, _count);

        state.packets = 0;
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            layer.Send(new ByteData(payload, 0, payload.Length), 1200, 0, state, _count);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, state.packets);
        Assert.AreEqual(0, allocated, $"Steady-state unfragmented framing allocated {allocated} managed bytes.");
    }

    [Test]
    [TestCase(256, 64)]
    [TestCase(1400, 256)]
    [TestCase(32768, 1200)]
    public void Benchmark_Fragmentation_InOrderRoundtrip(int payloadSize, int mtu)
    {
        var payload = CreatePayload(payloadSize);
        int iterations = Math.Min(50_000, Math.Max(2_000, 32 * 1024 * 1024 / payloadSize));

        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var state = new LoopbackState(receiver);
        for (int i = 0; i < 128; i++)
            sender.Send(new ByteData(payload, 0, payload.Length), mtu, 0, state, _loopback);

        state.completed = 0;
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < iterations; i++)
            sender.Send(new ByteData(payload, 0, payload.Length), mtu, 0, state, _loopback);
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, state.completed);
        double nsPerMessage = watch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
        double mibPerSecond = (double)payloadSize * iterations /
                              (1024 * 1024 * watch.Elapsed.TotalSeconds);
        Debug.Log($"[Fragmentation In-Order] payload={payloadSize} MTU={mtu} " +
                  $"{nsPerMessage:F1} ns/message {mibPerSecond:F1} MiB/s managed={allocated} B");
    }

    [Test]
    public void Benchmark_Fragmentation_OutOfOrderReassembly()
    {
        const int payloadSize = 1400;
        const int mtu = 256;
        const int iterations = 25_000;
        var payload = CreatePayload(payloadSize);
        var packets = new List<byte[]>();

        using (var sender = new FragmentationLayer())
        {
            sender.Send(new ByteData(payload, 0, payload.Length), mtu, fragment =>
            {
                var copy = new byte[fragment.length];
                Buffer.BlockCopy(fragment.data, fragment.offset, copy, 0, fragment.length);
                packets.Add(copy);
            });
        }

        using var receiver = new FragmentationLayer();
        int completed = 0;
        uint checksum = 0;

        void RunOnce()
        {
            for (int i = packets.Count - 1; i >= 0; i--)
            {
                byte[] packet = packets[i];
                if (!receiver.Receive(new ByteData(packet, 0, packet.Length), out var assembled))
                    continue;

                completed++;
                checksum += assembled.data[assembled.offset];
                checksum += assembled.data[assembled.offset + assembled.length - 1];
            }
        }

        for (int i = 0; i < 128; i++)
            RunOnce();

        completed = 0;
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < iterations; i++)
            RunOnce();
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, completed);
        double nsPerMessage = watch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
        double mibPerSecond = (double)payloadSize * iterations /
                              (1024 * 1024 * watch.Elapsed.TotalSeconds);
        Debug.Log($"[Fragmentation Out-of-Order] payload={payloadSize} MTU={mtu} fragments={packets.Count} " +
                  $"{nsPerMessage:F1} ns/message {mibPerSecond:F1} MiB/s managed={allocated} B checksum={checksum}");
    }

    static void Loopback(ByteData fragment, LoopbackState state)
    {
        if (!state.receiver.Receive(fragment, out var assembled))
            return;

        state.completed++;
        state.checksum += assembled.data[assembled.offset];
        state.checksum += assembled.data[assembled.offset + assembled.length - 1];
    }

    static void Count(ByteData fragment, CountState state)
    {
        state.packets++;
    }

    static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 31 + 17);
        return payload;
    }
}

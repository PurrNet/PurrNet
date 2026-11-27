using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Profiler;
using UnityEngine;

/// <summary>
/// BandwidthProfilerToFile writes each sample as a BinaryWriter length prefix (little-endian int)
/// followed by the payload, and trims the file to half its size once it reaches the limit.
/// The trim must walk whole entries so the newest samples survive and the file stays parseable.
/// </summary>
public class BandwidthProfilerToFileTests
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private const float MaxFileSizeInMb = 0.001f;
    private const int TagBase = 100000;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    // 10, 30 and 40 forwarded values give payloads of roughly 60, 140 and 180 bytes.
    [TestCase(10)]
    [TestCase(30)]
    [TestCase(40)]
    public void TrimKeepsNewestWholeEntries(int forwardedPerSample)
    {
        var type = typeof(BandwidthProfilerToFile);
        var limit = Mathf.CeilToInt(MaxFileSizeInMb * 1024 * 1024);

        // Inactive so OnEnable does not open a file on disk or touch Statistics.
        var go = new GameObject("BandwidthProfilerToFileTests");
        go.SetActive(false);
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        try
        {
            var component = go.AddComponent<BandwidthProfilerToFile>();
            type.GetField("_writer", Flags).SetValue(component, writer);
            type.GetField("_maxFileSizeInMb", Flags).SetValue(component, MaxFileSizeInMb);
            var onSample = type.GetMethod("OnSample", Flags);

            bool trimmed = false;
            long previousLength = 0;

            for (int i = 0; i < 40; i++)
            {
                var sample = new TickSample();
                try
                {
                    for (int k = 0; k < forwardedPerSample - 1; k++)
                        sample.forwardedBytes.Add(1000 + k);
                    sample.forwardedBytes.Add(TagBase + i);

                    try
                    {
                        onSample.Invoke(component, new object[] { sample });
                    }
                    catch (TargetInvocationException e)
                    {
                        Assert.Fail($"OnSample threw on sample {i}: {e.InnerException}");
                    }
                }
                finally
                {
                    sample.Dispose();
                }

                if (stream.Length < previousLength)
                    trimmed = true;
                previousLength = stream.Length;

                Assert.That(stream.Length, Is.GreaterThan(0), $"The file was emptied after sample {i}.");
                Assert.That(stream.Length, Is.LessThan(limit), $"The file is over the size limit after sample {i}.");

                var tags = ReadTags(stream);
                Assert.That(tags.Count, Is.GreaterThan(0), $"No whole entry left after sample {i}.");
                Assert.That(tags[tags.Count - 1], Is.EqualTo(TagBase + i), $"The newest sample is missing after sample {i}.");
                for (int t = 1; t < tags.Count; t++)
                    Assert.That(tags[t], Is.EqualTo(tags[t - 1] + 1), $"The kept samples are not consecutive after sample {i}.");
            }

            Assert.That(trimmed, Is.True, "The file never reached the size limit; the test did not exercise trimming.");
        }
        finally
        {
            writer.Dispose();
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// Parses the stream the way LoadSamples does and returns each entry's tag (its last forwarded value).
    /// Fails unless the entries end exactly at the end of the stream.
    /// </summary>
    private static List<int> ReadTags(MemoryStream stream)
    {
        var readSample = typeof(BandwidthProfilerToFile).GetMethod("ReadSample", BindingFlags.NonPublic | BindingFlags.Static);
        var bytes = stream.ToArray();
        var tags = new List<int>();
        int position = 0;

        while (position < bytes.Length)
        {
            Assert.That(bytes.Length - position, Is.GreaterThanOrEqualTo(4), $"Truncated length prefix at byte {position}.");
            int length = BitConverter.IsLittleEndian
                ? BitConverter.ToInt32(bytes, position)
                : bytes[position] | (bytes[position + 1] << 8) | (bytes[position + 2] << 16) | (bytes[position + 3] << 24);
            position += 4;
            Assert.That(length, Is.InRange(0, bytes.Length - position), $"Entry length {length} at byte {position - 4} runs past the end of the stream.");

            var payload = new byte[length];
            Array.Copy(bytes, position, payload, 0, length);
            position += length;

            using var packer = BitPackerPool.Get(payload);
            var sample = (TickSample)readSample.Invoke(null, new object[] { packer });
            try
            {
                tags.Add(sample.forwardedBytes[sample.forwardedBytes.Count - 1]);
            }
            finally
            {
                sample.Dispose();
            }
        }

        Assert.That(position, Is.EqualTo(bytes.Length), "The entries do not end exactly at the end of the stream.");
        return tags;
    }
}

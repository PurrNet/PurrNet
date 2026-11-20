using System;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Profiler;

/// <summary>
/// Statistics.MarkEndOfSampling stores the finished sample and hands it to onSample subscribers.
/// A subscriber that throws must not leave the finished sample as the current one, otherwise the
/// same instance is stored again next tick and later disposed more than once.
/// </summary>
public class StatisticsSampleTests
{
    private static void Throw(TickSample sample) => throw new InvalidOperationException("subscriber failure");

    [Test]
    public void ThrowingSubscriberDoesNotReuseTheFinishedSample()
    {
        bool wasPaused = Statistics.paused;
        int countBefore = Statistics.samples.Count;

        Statistics.paused = false;
        Statistics.inspecting += 1;
        Statistics.onSample += Throw;

        try
        {
            Assert.Throws<InvalidOperationException>(Statistics.MarkEndOfSampling);
            Assert.Throws<InvalidOperationException>(Statistics.MarkEndOfSampling);

            int count = Statistics.samples.Count;
            Assert.That(count, Is.GreaterThanOrEqualTo(2));
            Assert.That(Statistics.samples[count - 1], Is.Not.SameAs(Statistics.samples[count - 2]),
                "The sample that was handed to a throwing subscriber was stored again on the next tick.");
        }
        finally
        {
            Statistics.onSample -= Throw;
            Statistics.inspecting -= 1;
            Statistics.paused = wasPaused;

            // Remove only the entries this test added, disposing each distinct instance once.
            // Statistics evicts from the front at MAX_SAMPLES, so count down from the end.
            int added = Math.Max(0, Statistics.samples.Count - countBefore);
            added = Math.Min(added, 2);
            var currentField = typeof(Statistics).GetField("_currentSample", BindingFlags.NonPublic | BindingFlags.Static);
            var current = currentField.GetValue(null);
            TickSample last = null;
            for (int i = 0; i < added; i++)
            {
                int index = Statistics.samples.Count - 1;
                var sample = Statistics.samples[index];
                Statistics.samples.RemoveAt(index);
                if (!ReferenceEquals(sample, last))
                    sample.Dispose();
                if (ReferenceEquals(sample, current))
                    currentField.SetValue(null, new TickSample());
                last = sample;
            }
        }
    }
}

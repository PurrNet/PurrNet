using System.Collections.Generic;
using NUnit.Framework;
using PurrNet.Pooling;

// Each test rents lists of its own element type, so it starts from an empty pool.
public class ListPoolTests
{
    struct SmallestFitItem { public int value; }
    struct BoundaryItem { public int value; }
    struct RecentItem { public int value; }
    struct SwingItem { public int value; }

    [Test]
    public void SizedRentTakesTheSmallestPooledListThatFits()
    {
        var small = new List<SmallestFitItem>(8);
        var large = new List<SmallestFitItem>(128);
        ListPool<SmallestFitItem>.Destroy(large);
        ListPool<SmallestFitItem>.Destroy(small);
        Assert.That(ListPool<SmallestFitItem>.GetCount(), Is.EqualTo(2));

        // A single stack handed back the small list and grew it.
        var forLarge = ListPool<SmallestFitItem>.Instantiate(100);
        var forSmall = ListPool<SmallestFitItem>.Instantiate(5);

        Assert.That(forLarge, Is.SameAs(large));
        Assert.That(forLarge.Capacity, Is.EqualTo(128));
        Assert.That(forSmall, Is.SameAs(small));
        Assert.That(ListPool<SmallestFitItem>.GetCount(), Is.Zero);
    }

    [Test]
    public void ASizedRentThatNothingFitsCreatesAListThatSizeFindsAgain()
    {
        ListPool<BoundaryItem>.Destroy(new List<BoundaryItem>(16));

        var created = ListPool<BoundaryItem>.Instantiate(73);
        Assert.That(created.Capacity, Is.EqualTo(128), "sized to its capacity class boundary");
        Assert.That(ListPool<BoundaryItem>.GetCount(), Is.EqualTo(1), "the smaller pooled list is left for smaller rents");

        ListPool<BoundaryItem>.Destroy(created);
        Assert.That(ListPool<BoundaryItem>.Instantiate(73), Is.SameAs(created));
        Assert.That(ListPool<BoundaryItem>.Instantiate(9).Capacity, Is.EqualTo(16));
    }

    [Test]
    public void UnsizedRentReusesTheLastReturnedList()
    {
        var first = new List<RecentItem>(8);
        var last = new List<RecentItem>(64);
        ListPool<RecentItem>.Destroy(first);
        ListPool<RecentItem>.Destroy(last);

        Assert.That(ListPool<RecentItem>.Instantiate(), Is.SameAs(last));
        Assert.That(ListPool<RecentItem>.Instantiate(), Is.SameAs(first));
        Assert.That(ListPool<RecentItem>.Instantiate(), Is.Not.SameAs(first).And.Not.SameAs(last));
    }

    // A history of copies whose size swings keeps cycling lists through the pool. With one stack each
    // copy took whichever list was returned last, so every list kept growing toward the largest size
    // it would ever be handed, allocating a new backing array each time.
    [Test]
    public void DuplicatesOfAListWhoseSizeSwingsStopAllocatingOnceThePoolHoldsEverySize()
    {
        const int period = 80;
        const int history = 299;
        var source = DisposableList<SwingItem>.Create();
        var ring = new Queue<DisposableList<SwingItem>>();
        var capacities = new Dictionary<List<SwingItem>, int>();
        int created = 0, grown = 0;
        try
        {
            for (int tick = 0; tick < 2000; tick++)
            {
                int phase = tick % period;
                int count = phase < period / 2 ? phase * 2 : (period - phase) * 2;
                source.Clear();
                for (int i = 0; i < count; i++)
                    source.Add(new SwingItem { value = i });

                var copy = source.Duplicate();
                var list = copy.list;
                if (tick >= 1000)
                {
                    if (!capacities.TryGetValue(list, out int capacity))
                        created++;
                    else if (capacity != list.Capacity)
                        grown++;
                }
                capacities[list] = list.Capacity;

                ring.Enqueue(copy);
                if (ring.Count > history)
                    ring.Dequeue().Dispose();
            }
        }
        finally
        {
            source.Dispose();
            while (ring.Count > 0)
                ring.Dequeue().Dispose();
        }

        Assert.That(created, Is.Zero, "new lists created after warmup");
        Assert.That(grown, Is.Zero, "pooled lists grown after warmup");
    }
}

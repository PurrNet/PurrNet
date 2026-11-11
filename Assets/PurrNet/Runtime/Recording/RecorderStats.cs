using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;

namespace PurrNet.Recording
{
    /// <summary>
    /// Main-thread cost of recording, per section and tick. Shows up in the Unity profiler through the markers and is
    /// summarized in the log when a recording finishes, so a player build can be measured without the profiler.
    /// Fixed-size histograms: measuring makes no garbage.
    /// </summary>
    public sealed class RecorderStats
    {
        const double BucketMs = 0.005;
        const int Buckets = 4000; // up to 20 ms, then one overflow bucket

        public sealed class Section
        {
            public readonly string name;
            public readonly ProfilerMarker marker;
            internal readonly int[] histogram = new int[Buckets + 1];
            internal int ticks;
            internal double sumMs;
            internal double maxMs;
            /// <summary>Heap growth seen during the section (process-wide, 4 KB granular).</summary>
            internal long allocated;
            internal int ticksWithAllocation;
            internal double pendingMs;
            internal long pendingAllocated;
            internal bool touched;
            internal long start;
            internal long startMemory;
            internal long startCounted;
            /// <summary>Exact bytes from Unity's GC allocation counter, when it is available (development builds).</summary>
            internal long counted;
            internal int ticksWithCounted;
            internal long pendingCounted;

            public Section(string prefix, string name)
            {
                this.name = name;
                marker = new ProfilerMarker(prefix + "." + name.Trim());
            }
        }

        readonly List<Section> _sections = new List<Section>();
        // Allocation over time (first section), per 30 s: shows whether pools have warmed up.
        const int TimelineSlots = 240;
        readonly long[] _timelineBytes = new long[TimelineSlots];
        readonly int[] _timelineTicks = new int[TimelineSlots];
        readonly Stopwatch _since = new Stopwatch();
        ProfilerRecorder _gcAllocated;
        bool _counted;
        /// <summary>Heap growth is process-wide; while the writer thread works it is not attributed to the recorder.</summary>
        public RecordingFile file;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        int _collectionsAtStart;

        readonly string _prefix;

        /// <summary>
        /// Whether time and allocations are measured. Off, sections only show up in the Unity profiler and the
        /// summary is empty. Set before a recording starts.
        /// </summary>
        public bool enabled;

        /// <param name="prefix">Start of the profiler marker names (the recorder's name).</param>
        public RecorderStats(string prefix)
        {
            _prefix = prefix;
        }

        public Section Add(string name)
        {
            var section = new Section(_prefix, name);
            _sections.Add(section);
            return section;
        }

        public Section Find(string name)
        {
            foreach (var s in _sections)
                if (s.name == name)
                    return s;
            return null;
        }

        public void Begin(Section section)
        {
            section.marker.Begin();
            if (!enabled)
                return;
            section.startCounted = _counted ? _gcAllocated.CurrentValue : 0;
            section.startMemory = file != null && file.busy ? -1 : GC.GetTotalMemory(false);
            section.start = _clock.ElapsedTicks;
        }

        public void End(Section section)
        {
            if (!enabled)
            {
                section.marker.End();
                return;
            }
            long elapsed = _clock.ElapsedTicks - section.start;
            bool clean = section.startMemory >= 0 && !(file != null && file.busy);
            long memory = clean ? GC.GetTotalMemory(false) - section.startMemory : 0;
            if (_counted && clean)
            {
                long counted = _gcAllocated.CurrentValue - section.startCounted;
                if (counted > 0)
                    section.pendingCounted += counted;
            }
            section.marker.End();
            section.pendingMs += elapsed * 1000.0 / Stopwatch.Frequency;
            section.touched = true;
            // A collection in between makes the delta negative.
            if (memory > 0)
                section.pendingAllocated += memory;
        }

        /// <summary>Ends a tick: every section that ran during it gets one sample, the sum of its calls.</summary>
        public void CommitTick()
        {
            if (!enabled)
                return;
            foreach (var s in _sections)
            {
                if (!s.touched)
                    continue;
                double ms = s.pendingMs;
                s.ticks++;
                s.sumMs += ms;
                if (ms > s.maxMs)
                    s.maxMs = ms;
                s.histogram[Math.Min(Buckets, (int)(ms / BucketMs))]++;
                if (s.pendingAllocated > 0)
                {
                    s.allocated += s.pendingAllocated;
                    s.ticksWithAllocation++;
                }
                if (s.pendingCounted > 0)
                {
                    s.counted += s.pendingCounted;
                    s.ticksWithCounted++;
                    if (s == _sections[0])
                    {
                        int slot = Math.Min(TimelineSlots - 1, (int)(_since.Elapsed.TotalSeconds / 30));
                        _timelineBytes[slot] += s.pendingCounted;
                        _timelineTicks[slot]++;
                    }
                }
                s.pendingCounted = 0;
                s.pendingMs = 0;
                s.pendingAllocated = 0;
                s.touched = false;
            }
        }

        public void Reset()
        {
            foreach (var s in _sections)
            {
                Array.Clear(s.histogram, 0, s.histogram.Length);
                s.ticks = 0;
                s.sumMs = s.maxMs = 0;
                s.allocated = 0;
                s.ticksWithAllocation = 0;
                s.counted = 0;
                s.ticksWithCounted = 0;
            }
            if (!enabled)
                return;
            if (!_gcAllocated.Valid)
                _gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _counted = _gcAllocated.Valid;
            _collectionsAtStart = GC.CollectionCount(0);
            Array.Clear(_timelineBytes, 0, TimelineSlots);
            Array.Clear(_timelineTicks, 0, TimelineSlots);
            _since.Restart();
        }

        static double Percentile(Section s, double p)
        {
            int target = (int)Math.Ceiling(s.ticks * p);
            int seen = 0;
            for (int i = 0; i <= Buckets; i++)
            {
                seen += s.histogram[i];
                if (seen >= target)
                    return i == Buckets ? s.maxMs : (i + 1) * BucketMs;
            }
            return s.maxMs;
        }

        public string Summary(ulong ticks, string name)
        {
            var text = new StringBuilder($"[{name}] main-thread cost per tick it ran in: ");
            foreach (var s in _sections)
            {
                if (s.ticks == 0)
                    continue;
                text.Append($"\n  {s.name,-12} ticks={s.ticks,6} mean={s.sumMs / s.ticks:0.000}ms " +
                            $"p50<={Percentile(s, 0.5):0.000}ms p99<={Percentile(s, 0.99):0.000}ms max={s.maxMs:0.000}ms " +
                            $"total={s.sumMs:0}ms heapGrowth={s.allocated / 1024.0:0.0}KiB in {s.ticksWithAllocation} ticks (writer idle)" +
                            (_counted ? $" gcAlloc={s.counted / 1024.0:0.0}KiB in {s.ticksWithCounted} ticks" : ""));
            }
            text.Append($"\n  GC collections during the recording (whole game): {GC.CollectionCount(0) - _collectionsAtStart}");
            if (_counted)
            {
                text.Append("\n  recorder allocations per 30 s (KiB/ticks):");
                int last = (int)Math.Min(TimelineSlots - 1, _since.Elapsed.TotalSeconds / 30);
                for (int i = 0; i <= last; i++)
                    text.Append($" {_timelineBytes[i] / 1024}/{_timelineTicks[i]}");
            }
            return text.ToString();
        }
    }
}

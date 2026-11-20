using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;

namespace PurrNet.Recording
{
    public sealed class RecorderStats
    {
        const double BucketMs = 0.005;
        const int Buckets = 4000;

        public sealed class Section
        {
            public readonly string name;
            public readonly ProfilerMarker marker;
            internal readonly int[] histogram = new int[Buckets + 1];
            internal int ticks;
            internal double sumMs;
            internal double maxMs;
            internal long allocated;
            internal int ticksWithAllocation;
            internal double pendingMs;
            internal long pendingAllocated;
            internal bool touched;
            internal long start;
            internal long startMemory;
            internal long startCounted;
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
        const int TimelineSlots = 240;
        readonly long[] _timelineBytes = new long[TimelineSlots];
        readonly int[] _timelineTicks = new int[TimelineSlots];
        readonly Stopwatch _since = new Stopwatch();
        ProfilerRecorder _gcAllocated;
        bool _counted;
        public RecordingFile file;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        int _collectionsAtStart;

        /// <summary>
        /// The slowest tick, and the slowest one once the recording has settled: what each section took in it.
        /// </summary>
        sealed class Slowest
        {
            public double ms;
            public int tick;
            public bool collected;
            public double[] sections = new double[0];
        }

        const int SettledAfterTicks = 20;
        readonly Slowest _slowest = new Slowest(), _slowestSettled = new Slowest();
        int _commits;
        int _collectionsAtTick;

        readonly string _prefix;

        public bool enabled;

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
            if (ReferenceEquals(section, _sections[0]))
                _collectionsAtTick = GC.CollectionCount(0);
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
            if (memory > 0)
                section.pendingAllocated += memory;
        }

        public void CommitTick()
        {
            if (!enabled)
                return;
            if (_sections[0].touched)
            {
                _commits++;
                Note(_slowest);
                if (_commits > SettledAfterTicks)
                    Note(_slowestSettled);
            }
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

        void Note(Slowest slowest)
        {
            double ms = _sections[0].pendingMs;
            if (ms <= slowest.ms)
                return;
            slowest.ms = ms;
            slowest.tick = _commits;
            slowest.collected = GC.CollectionCount(0) != _collectionsAtTick;
            if (slowest.sections.Length != _sections.Count)
                slowest.sections = new double[_sections.Count];
            for (int i = 0; i < _sections.Count; i++)
                slowest.sections[i] = _sections[i].touched ? _sections[i].pendingMs : 0;
        }

        void Describe(StringBuilder text, string title, Slowest slowest)
        {
            if (slowest.ms <= 0)
                return;
            text.Append($"\n  {title}: tick {slowest.tick}, {slowest.ms:0.000}ms{(slowest.collected ? ", a garbage collection ran during it" : "")}:");
            for (int i = 1; i < _sections.Count; i++)
                if (slowest.sections[i] >= 0.0005)
                    text.Append($" {_sections[i].name.Trim()} {slowest.sections[i]:0.000}");
        }

        public void Reset()
        {
            _commits = 0;
            _slowest.ms = _slowestSettled.ms = 0;
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
            if (_commits == 0)
                return "";
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
            Describe(text, "slowest tick", _slowest);
            Describe(text, $"slowest tick after the first {SettledAfterTicks}", _slowestSettled);
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

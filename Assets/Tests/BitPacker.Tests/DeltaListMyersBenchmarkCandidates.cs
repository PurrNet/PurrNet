using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;

/// <summary>
/// Closed-type, benchmark-only Myers codecs. Keeping this as a non-static type prevents PurrNet's
/// serializer discovery from registering these experimental methods globally.
/// </summary>
internal sealed class DeltaListMyersBenchmarkCandidates
{
    private const int MaxEditDistance = 32;
    private const int MaxMyersWork = 50_000;

    private const byte StepMatch = 0;
    private const byte StepDelete = 1;
    private const byte StepInsert = 2;

    // Two-bit compact operation headers. Insert/replace share a header and use one shape bit.
    private const byte CompactEnd = 0;
    private const byte CompactAppend = 1;
    private const byte CompactDelete = 2;
    private const byte CompactInsertOrReplace = 3;

    private const byte ValuesRaw = 0;
    private const byte ValuesSequentialDelta = 1;
    private const byte ValuesContextDelta = 2;

    private static readonly List<int> Empty = new List<int>(0);

    [ThreadStatic] private static MyersScratch _scratch;

    private DeltaListMyersBenchmarkCandidates() { }

    /// <summary>
    /// Adapts the current production Myers search and DiffOp wire to List&lt;int&gt; framing without
    /// copying the inputs into DisposableLists. This isolates current Myers search/wire cost from
    /// bridge allocations while preserving its operation representation.
    /// </summary>
    internal static bool WriteCurrentMyers(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        if (ListsEqual(oldValue, newValue))
        {
            packer.WriteBit(false);
            return false;
        }

        packer.WriteBit(true);
        packer.WriteBit(true); // declared List<int> representation
        if (newValue == null)
        {
            packer.WriteBit(false);
            return true;
        }

        packer.WriteBit(true);
        var changes = MyersDiff.Diff((IReadOnlyList<int>)(oldValue ?? Empty), newValue);
        try
        {
            for (int i = 0; i < changes.Count; i++)
                WriteCurrentOperation(packer, changes[i]);
            packer.WriteBits((byte)OperationType.End, 2);
        }
        finally
        {
            DisposeOperations(changes);
        }

        return true;
    }

    internal static void ReadCurrentMyers(BitPacker packer, List<int> oldValue, ref List<int> value)
    {
        if (!packer.ReadBit())
        {
            value = Clone(oldValue);
            return;
        }

        if (!packer.ReadBit())
            throw new InvalidOperationException("The current-Myers benchmark received a runtime-type payload.");
        if (!packer.ReadBit())
        {
            value = null;
            return;
        }

        var result = oldValue == null ? new List<int>() : new List<int>(oldValue);
        int offset = 0;
        int operationCount = 0;
        while (true)
        {
            if (++operationCount > 1_000_000)
                throw new InvalidOperationException("The current-Myers operation stream has no terminator.");

            var type = (OperationType)packer.ReadBits(2);
            if (type == OperationType.End)
                break;

            switch (type)
            {
                case OperationType.Add:
                {
                    var inserted = ReadCurrentValues(packer);
                    result.AddRange(inserted);
                    offset = checked(offset + inserted.Count);
                    break;
                }
                case OperationType.Insert:
                {
                    int originalIndex = ReadCurrentSize(packer);
                    var inserted = ReadCurrentValues(packer);
                    int index = checked(originalIndex + offset);
                    ValidateInsertIndex(result, index);
                    result.InsertRange(index, inserted);
                    offset = checked(offset + inserted.Count);
                    break;
                }
                case OperationType.Delete:
                {
                    int originalIndex = ReadCurrentSize(packer);
                    int count = ReadCurrentSize(packer);
                    int index = checked(originalIndex + offset);
                    ValidateRange(result.Count, index, count);
                    result.RemoveRange(index, count);
                    offset = checked(offset - count);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown current-Myers operation {(byte)type}.");
            }
        }

        value = result;
    }

    /// <summary>
    /// Uses the bounded compact edit script when it beats an exact full payload, otherwise falls
    /// back to full. This measures the Myers-specific format independently of indexed/splice modes.
    /// </summary>
    internal static bool WriteBoundedMyers(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        if (ListsEqual(oldValue, newValue))
        {
            packer.WriteBit(false);
            return false;
        }

        packer.WriteBit(true);
        packer.WriteBit(true); // declared List<int> representation

        bool hasCompactPlan = PrepareCompact(oldValue, newValue, out long compactBits);
        bool useCompact = hasCompactPlan && compactBits < GetFullPayloadBits(oldValue, newValue);
        packer.WriteBit(useCompact);
        if (useCompact)
            WritePreparedCompact(packer, oldValue, newValue);
        else
            WriteFullPayload(packer, oldValue, newValue);
        return true;
    }

    internal static void ReadBoundedMyers(BitPacker packer, List<int> oldValue, ref List<int> value)
    {
        if (!packer.ReadBit())
        {
            value = Clone(oldValue);
            return;
        }

        if (!packer.ReadBit())
            throw new InvalidOperationException("The bounded-Myers benchmark received a runtime-type payload.");

        if (packer.ReadBit())
            value = ReadCompact(packer, oldValue);
        else
            value = ReadFullPayload(packer, oldValue);
    }

    /// <summary>
    /// Builds a compact edit plan into thread-local benchmark scratch. The corresponding write must
    /// happen immediately; this intentionally avoids per-operation allocations in timed samples.
    /// </summary>
    internal static bool PrepareCompact(List<int> oldValue, List<int> newValue, out long payloadBits)
    {
        payloadBits = 0;
        if (oldValue == null || newValue == null)
            return false;

        MyersScratch scratch = Scratch;
        scratch.hunks.Clear();
        scratch.preparedOld = null;
        scratch.preparedNew = null;

        int shared = Math.Min(oldValue.Count, newValue.Count);
        int prefix = 0;
        while (prefix < shared && oldValue[prefix] == newValue[prefix])
            prefix++;

        int suffix = 0;
        int remainingShared = shared - prefix;
        while (suffix < remainingShared &&
               oldValue[oldValue.Count - suffix - 1] == newValue[newValue.Count - suffix - 1])
        {
            suffix++;
        }

        int oldMiddleCount = oldValue.Count - prefix - suffix;
        int newMiddleCount = newValue.Count - prefix - suffix;
        if (oldMiddleCount == 0 || newMiddleCount == 0)
        {
            scratch.hunks.Add(new Hunk(prefix, oldMiddleCount, prefix, newMiddleCount));
        }
        else if (!TryBuildBoundedMyers(oldValue, newValue, prefix, oldMiddleCount, newMiddleCount, scratch))
        {
            return false;
        }

        CoalesceHunksByWireCost(oldValue, newValue, scratch);
        payloadBits = GetCompactPayloadBits(oldValue, newValue, scratch.hunks);
        scratch.preparedOld = oldValue;
        scratch.preparedNew = newValue;
        return true;
    }

    internal static void WritePreparedCompact(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        MyersScratch scratch = Scratch;
        if (!ReferenceEquals(scratch.preparedOld, oldValue) || !ReferenceEquals(scratch.preparedNew, newValue))
            throw new InvalidOperationException("The compact Myers plan is stale or was not prepared.");

        int oldCursor = 0;
        for (int i = 0; i < scratch.hunks.Count; i++)
        {
            Hunk hunk = scratch.hunks[i];
            int gap = hunk.oldStart - oldCursor;
            if (gap < 0)
                throw new InvalidOperationException("Compact Myers hunks are not ordered.");

            if (hunk.oldCount == 0 && hunk.oldStart == oldValue.Count)
            {
                packer.WriteBits(CompactAppend, 2);
                WritePositiveCount(packer, hunk.newCount);
            }
            else if (hunk.newCount == 0)
            {
                packer.WriteBits(CompactDelete, 2);
                WriteCount(packer, gap);
                WritePositiveCount(packer, hunk.oldCount);
            }
            else
            {
                packer.WriteBits(CompactInsertOrReplace, 2);
                bool replaces = hunk.oldCount != 0;
                packer.WriteBit(replaces);
                WriteCount(packer, gap);
                if (replaces)
                    WritePositiveCount(packer, hunk.oldCount);
                WritePositiveCount(packer, hunk.newCount);
            }

            if (hunk.newCount != 0)
                WriteInsertedValues(packer, oldValue, newValue, hunk);
            oldCursor = checked(hunk.oldStart + hunk.oldCount);
        }

        packer.WriteBits(CompactEnd, 2);
    }

    internal static List<int> ReadCompact(BitPacker packer, List<int> oldValue)
    {
        if (oldValue == null)
            throw new InvalidOperationException("A compact Myers payload requires a non-null baseline.");

        var result = new List<int>(oldValue.Count);
        int oldCursor = 0;
        int hunkCount = 0;
        while (true)
        {
            if (++hunkCount > oldValue.Count + 65_536)
                throw new InvalidOperationException("The compact Myers operation stream has no terminator.");

            byte header = (byte)packer.ReadBits(2);
            if (header == CompactEnd)
            {
                CopyRange(oldValue, oldCursor, oldValue.Count - oldCursor, result);
                return result;
            }

            int gap;
            int removeCount;
            int insertCount;
            if (header == CompactAppend)
            {
                gap = oldValue.Count - oldCursor;
                removeCount = 0;
                insertCount = ReadPositiveCount(packer);
            }
            else if (header == CompactDelete)
            {
                gap = ReadCount(packer);
                removeCount = ReadPositiveCount(packer);
                insertCount = 0;
            }
            else if (header == CompactInsertOrReplace)
            {
                bool replaces = packer.ReadBit();
                gap = ReadCount(packer);
                removeCount = replaces ? ReadPositiveCount(packer) : 0;
                insertCount = ReadPositiveCount(packer);
            }
            else
            {
                throw new InvalidOperationException($"Unknown compact Myers header {header}.");
            }

            if (gap > oldValue.Count - oldCursor)
                throw new InvalidOperationException("A compact Myers gap exceeds the baseline bounds.");
            CopyRange(oldValue, oldCursor, gap, result);
            oldCursor += gap;

            if (removeCount > oldValue.Count - oldCursor)
                throw new InvalidOperationException("A compact Myers removal exceeds the baseline bounds.");
            int hunkStart = oldCursor;
            oldCursor += removeCount;

            if (insertCount != 0)
                ReadInsertedValues(packer, oldValue, hunkStart, removeCount, insertCount, result);

            if (header == CompactAppend && oldCursor != oldValue.Count)
                throw new InvalidOperationException("A compact Myers append did not consume the baseline.");
        }
    }

    private static bool TryBuildBoundedMyers(List<int> oldValue, List<int> newValue, int prefix,
        int oldCount, int newCount, MyersScratch scratch)
    {
        int max = checked(oldCount + newCount);
        int limit = Math.Min(MaxEditDistance, max);
        if (Math.Abs(oldCount - newCount) > limit)
            return false;

        int width = checked(2 * limit + 3);
        int offset = limit + 1;
        scratch.EnsureFrontier(width, checked((limit + 1) * width));
        Array.Clear(scratch.frontier, 0, width);

        int foundDepth = -1;
        int work = 0;
        for (int depth = 0; depth <= limit; depth++)
        {
            Array.Copy(scratch.frontier, 0, scratch.trace, depth * width, width);
            for (int diagonal = -depth; diagonal <= depth; diagonal += 2)
            {
                if (++work > MaxMyersWork)
                    return false;

                int diagonalIndex = diagonal + offset;
                int x;
                if (diagonal == -depth ||
                    (diagonal != depth && scratch.frontier[diagonalIndex - 1] < scratch.frontier[diagonalIndex + 1]))
                {
                    x = scratch.frontier[diagonalIndex + 1];
                }
                else
                {
                    x = scratch.frontier[diagonalIndex - 1] + 1;
                }

                int y = x - diagonal;
                while (x < oldCount && y < newCount)
                {
                    if (++work > MaxMyersWork)
                        return false;
                    if (oldValue[prefix + x] != newValue[prefix + y])
                        break;
                    x++;
                    y++;
                }

                scratch.frontier[diagonalIndex] = x;
                if (x >= oldCount && y >= newCount)
                {
                    foundDepth = depth;
                    break;
                }
            }

            if (foundDepth >= 0)
                break;
        }

        if (foundDepth < 0)
            return false;

        scratch.EnsureSteps(max + 1);
        int stepCount = BacktrackSteps(oldCount, newCount, foundDepth, width, offset, scratch);
        BuildHunksFromReverseSteps(prefix, oldCount, newCount, scratch.reverseSteps, stepCount, scratch.hunks);
        return true;
    }

    private static int BacktrackSteps(int oldCount, int newCount, int depth, int width, int offset,
        MyersScratch scratch)
    {
        int x = oldCount;
        int y = newCount;
        int stepCount = 0;
        for (int currentDepth = depth; currentDepth >= 0; currentDepth--)
        {
            int row = currentDepth * width;
            int diagonal = x - y;
            int diagonalIndex = diagonal + offset;
            int previousDiagonal;
            int previousX;
            bool insertion;
            if (diagonal == -currentDepth ||
                (diagonal != currentDepth &&
                 scratch.trace[row + diagonalIndex - 1] < scratch.trace[row + diagonalIndex + 1]))
            {
                previousDiagonal = diagonal + 1;
                previousX = scratch.trace[row + previousDiagonal + offset];
                insertion = true;
            }
            else
            {
                previousDiagonal = diagonal - 1;
                previousX = scratch.trace[row + previousDiagonal + offset];
                insertion = false;
            }

            int previousY = previousX - previousDiagonal;
            while (x > previousX && y > previousY)
            {
                scratch.reverseSteps[stepCount++] = StepMatch;
                x--;
                y--;
            }

            if (currentDepth == 0)
                continue;
            if (insertion)
            {
                scratch.reverseSteps[stepCount++] = StepInsert;
                y--;
            }
            else
            {
                scratch.reverseSteps[stepCount++] = StepDelete;
                x--;
            }
        }

        return stepCount;
    }

    private static void BuildHunksFromReverseSteps(int prefix, int oldMiddleCount, int newMiddleCount,
        byte[] reverseSteps, int stepCount, List<Hunk> hunks)
    {
        int oldPosition = prefix;
        int newPosition = prefix;
        int hunkOldStart = 0;
        int hunkNewStart = 0;
        int removed = 0;
        int inserted = 0;
        bool inHunk = false;

        for (int i = stepCount - 1; i >= 0; i--)
        {
            byte step = reverseSteps[i];
            if (step == StepMatch)
            {
                FlushHunk(hunks, ref inHunk, hunkOldStart, removed, hunkNewStart, inserted);
                removed = 0;
                inserted = 0;
                oldPosition++;
                newPosition++;
                continue;
            }

            if (!inHunk)
            {
                inHunk = true;
                hunkOldStart = oldPosition;
                hunkNewStart = newPosition;
            }

            if (step == StepDelete)
            {
                removed++;
                oldPosition++;
            }
            else if (step == StepInsert)
            {
                inserted++;
                newPosition++;
            }
            else
            {
                throw new InvalidOperationException($"Unknown Myers backtrack step {step}.");
            }
        }

        FlushHunk(hunks, ref inHunk, hunkOldStart, removed, hunkNewStart, inserted);
        if (oldPosition != prefix + oldMiddleCount || newPosition != prefix + newMiddleCount)
            throw new InvalidOperationException("The bounded Myers backtrack did not consume both middle regions.");
    }

    private static void FlushHunk(List<Hunk> hunks, ref bool inHunk, int oldStart, int oldCount,
        int newStart, int newCount)
    {
        if (!inHunk)
            return;
        hunks.Add(new Hunk(oldStart, oldCount, newStart, newCount));
        inHunk = false;
    }

    /// <summary>
    /// Myers minimizes inserted/deleted elements, not encoded bits. A short exact match between two
    /// edit regions can cost more in a second header than encoding those matched values as one-bit
    /// contextual deltas. This dynamic program finds the cheapest partition of the discovered hunks.
    /// </summary>
    private static void CoalesceHunksByWireCost(List<int> oldValue, List<int> newValue,
        MyersScratch scratch)
    {
        int count = scratch.hunks.Count;
        if (count < 2)
            return;

        scratch.EnsureMerge(count + 1, count, newValue.Count);
        PrecomputeMergedHunkCosts(oldValue, newValue, scratch, count);
        scratch.mergeCosts[0] = 0;
        scratch.mergePrevious[0] = -1;

        for (int end = 1; end <= count; end++)
        {
            long best = long.MaxValue;
            int bestStart = end - 1;
            for (int start = end - 1; start >= 0; start--)
            {
                long candidate = checked(scratch.mergeCosts[start] +
                                         scratch.segmentCosts[start * count + end - 1]);
                if (candidate < best)
                {
                    best = candidate;
                    bestStart = start;
                }
            }

            scratch.mergeCosts[end] = best;
            scratch.mergePrevious[end] = bestStart;
        }

        int mergedCount = 0;
        for (int end = count; end > 0;)
        {
            int start = scratch.mergePrevious[end];
            scratch.mergedHunks[mergedCount++] = MergeHunks(scratch.hunks[start], scratch.hunks[end - 1]);
            end = start;
        }

        scratch.hunks.Clear();
        for (int i = mergedCount - 1; i >= 0; i--)
            scratch.hunks.Add(scratch.mergedHunks[i]);
    }

    private static void PrecomputeMergedHunkCosts(List<int> oldValue, List<int> newValue,
        MyersScratch scratch, int hunkCount)
    {
        if (newValue.Count != 0)
        {
            scratch.adjacentDeltaPrefix[0] = 0;
            for (int i = 1; i < newValue.Count; i++)
            {
                scratch.adjacentDeltaPrefix[i] = checked(
                    scratch.adjacentDeltaPrefix[i - 1] + DeltaIntBits(newValue[i - 1], newValue[i]));
            }
        }

        Hunk finalHunk = scratch.hunks[hunkCount - 1];
        for (int start = 0; start < hunkCount; start++)
        {
            Hunk firstHunk = scratch.hunks[start];
            int maximumOldCount = checked(finalHunk.oldStart + finalHunk.oldCount - firstHunk.oldStart);
            int maximumNewCount = checked(finalHunk.newStart + finalHunk.newCount - firstHunk.newStart);
            int maximumOverlap = Math.Min(maximumOldCount, maximumNewCount);
            scratch.contextDeltaPrefix[0] = 0;
            for (int i = 0; i < maximumOverlap; i++)
            {
                scratch.contextDeltaPrefix[i + 1] = checked(
                    scratch.contextDeltaPrefix[i] +
                    DeltaIntBits(oldValue[firstHunk.oldStart + i], newValue[firstHunk.newStart + i]));
            }

            int oldCursor = start == 0
                ? 0
                : checked(scratch.hunks[start - 1].oldStart + scratch.hunks[start - 1].oldCount);
            for (int end = start; end < hunkCount; end++)
            {
                Hunk merged = MergeHunks(firstHunk, scratch.hunks[end]);
                long bits = GetCompactHunkMetadataBits(oldValue, merged, oldCursor);
                if (merged.newCount != 0)
                {
                    long valueBits = GetFastMergedValueBits(oldValue, newValue, merged, scratch);
                    bits = checked(bits + 2 + valueBits);
                }
                scratch.segmentCosts[start * hunkCount + end] = bits;
            }
        }
    }

    private static long GetFastMergedValueBits(List<int> oldValue, List<int> newValue, Hunk hunk,
        MyersScratch scratch)
    {
        long rawBits = (long)hunk.newCount * 32;
        int newEnd = checked(hunk.newStart + hunk.newCount - 1);
        long sequentialBits = DeltaIntBits(0, newValue[hunk.newStart]);
        if (hunk.newCount > 1)
        {
            sequentialBits = checked(sequentialBits +
                                     scratch.adjacentDeltaPrefix[newEnd] -
                                     scratch.adjacentDeltaPrefix[hunk.newStart]);
        }

        int overlap = Math.Min(hunk.oldCount, hunk.newCount);
        long contextBits = scratch.contextDeltaPrefix[overlap];
        if (hunk.newCount > hunk.oldCount)
        {
            int firstExtra = checked(hunk.newStart + hunk.oldCount);
            if (hunk.oldCount != 0)
            {
                contextBits = checked(contextBits +
                                      scratch.adjacentDeltaPrefix[newEnd] -
                                      scratch.adjacentDeltaPrefix[firstExtra - 1]);
            }
            else
            {
                int baseline = hunk.oldStart > 0 ? oldValue[hunk.oldStart - 1] : 0;
                contextBits = checked(contextBits + DeltaIntBits(baseline, newValue[firstExtra]));
                if (hunk.newCount > 1)
                {
                    contextBits = checked(contextBits +
                                          scratch.adjacentDeltaPrefix[newEnd] -
                                          scratch.adjacentDeltaPrefix[firstExtra]);
                }
            }
        }

        return Math.Min(rawBits, Math.Min(sequentialBits, contextBits));
    }

    private static Hunk MergeHunks(Hunk first, Hunk last)
    {
        if (first.oldStart == last.oldStart && first.oldCount == last.oldCount &&
            first.newStart == last.newStart && first.newCount == last.newCount)
        {
            return first;
        }

        int oldGap = last.oldStart - checked(first.oldStart + first.oldCount);
        int newGap = last.newStart - checked(first.newStart + first.newCount);
        if (oldGap < 0 || newGap < 0)
            throw new InvalidOperationException("Overlapping Myers hunks cannot be coalesced.");

        return new Hunk(
            first.oldStart,
            checked(last.oldStart + last.oldCount - first.oldStart),
            first.newStart,
            checked(last.newStart + last.newCount - first.newStart));
    }

    private static long GetCompactPayloadBits(List<int> oldValue, List<int> newValue, List<Hunk> hunks)
    {
        long bits = 2; // End marker.
        int oldCursor = 0;
        for (int i = 0; i < hunks.Count; i++)
        {
            Hunk hunk = hunks[i];
            bits += GetCompactHunkBits(oldValue, newValue, hunk, oldCursor);
            oldCursor = checked(hunk.oldStart + hunk.oldCount);
        }

        return bits;
    }

    private static long GetCompactHunkBits(List<int> oldValue, List<int> newValue, Hunk hunk,
        int oldCursor)
    {
        long bits = GetCompactHunkMetadataBits(oldValue, hunk, oldCursor);
        if (hunk.newCount != 0)
        {
            SelectValueMode(oldValue, newValue, hunk, out long valueBits);
            bits += 2 + valueBits;
        }

        return bits;
    }

    private static long GetCompactHunkMetadataBits(List<int> oldValue, Hunk hunk, int oldCursor)
    {
        int gap = hunk.oldStart - oldCursor;
        if (hunk.oldCount == 0 && hunk.oldStart == oldValue.Count)
        {
            return 2 + CompactPositiveCountBits(hunk.newCount);
        }
        if (hunk.newCount == 0)
        {
            return 2 + CompactCountBits(gap) + CompactPositiveCountBits(hunk.oldCount);
        }

        long bits = 3 + CompactCountBits(gap) + CompactPositiveCountBits(hunk.newCount);
        if (hunk.oldCount != 0)
            bits += CompactPositiveCountBits(hunk.oldCount);
        return bits;
    }

    private static void WriteInsertedValues(BitPacker packer, List<int> oldValue, List<int> newValue, Hunk hunk)
    {
        byte mode = SelectValueMode(oldValue, newValue, hunk, out _);
        packer.WriteBits(mode, 2);
        int last = 0;
        for (int i = 0; i < hunk.newCount; i++)
        {
            int item = newValue[hunk.newStart + i];
            if (mode == ValuesRaw)
            {
                packer.WriteBits(unchecked((uint)item), 32);
            }
            else
            {
                int baseline = mode == ValuesSequentialDelta
                    ? last
                    : ContextBaseline(oldValue, newValue, hunk, i);
                DeltaPacker<int>.WriteFunc(packer, baseline, item);
            }

            last = item;
        }
    }

    private static void ReadInsertedValues(BitPacker packer, List<int> oldValue, int oldStart,
        int oldCount, int insertCount, List<int> result)
    {
        byte mode = (byte)packer.ReadBits(2);
        if (mode > ValuesContextDelta)
            throw new InvalidOperationException($"Unknown compact Myers value mode {mode}.");

        int last = 0;
        for (int i = 0; i < insertCount; i++)
        {
            int item;
            if (mode == ValuesRaw)
            {
                item = unchecked((int)(uint)packer.ReadBits(32));
            }
            else
            {
                int baseline;
                if (mode == ValuesSequentialDelta)
                    baseline = last;
                else if (i < oldCount)
                    baseline = oldValue[oldStart + i];
                else if (i > 0)
                    baseline = last;
                else if (oldStart > 0)
                    baseline = oldValue[oldStart - 1];
                else
                    baseline = 0;

                item = baseline;
                DeltaPacker<int>.ReadFunc(packer, baseline, ref item);
            }

            result.Add(item);
            last = item;
        }
    }

    private static byte SelectValueMode(List<int> oldValue, List<int> newValue, Hunk hunk,
        out long encodedBits)
    {
        byte mode = ValuesRaw;
        encodedBits = (long)hunk.newCount * 32;

        long sequentialBits = 0;
        int last = 0;
        for (int i = 0; i < hunk.newCount; i++)
        {
            int item = newValue[hunk.newStart + i];
            sequentialBits += DeltaIntBits(last, item);
            last = item;
        }

        if (sequentialBits < encodedBits)
        {
            mode = ValuesSequentialDelta;
            encodedBits = sequentialBits;
        }

        long contextBits = 0;
        for (int i = 0; i < hunk.newCount; i++)
        {
            int item = newValue[hunk.newStart + i];
            contextBits += DeltaIntBits(ContextBaseline(oldValue, newValue, hunk, i), item);
        }

        if (contextBits < encodedBits)
        {
            mode = ValuesContextDelta;
            encodedBits = contextBits;
        }

        return mode;
    }

    private static int ContextBaseline(List<int> oldValue, List<int> newValue, Hunk hunk, int index)
    {
        if (index < hunk.oldCount)
            return oldValue[hunk.oldStart + index];
        if (index > 0)
            return newValue[hunk.newStart + index - 1];
        if (hunk.oldStart > 0)
            return oldValue[hunk.oldStart - 1];
        return 0;
    }

    private static int DeltaIntBits(int oldValue, int newValue)
    {
        if (oldValue == newValue)
            return 1;
        long difference = newValue - (long)oldValue;
        return 1 + PackedUnsignedBits(PackingIntegers.ZigzagEncode(difference));
    }

    private static void WriteCurrentOperation(BitPacker packer, DiffOp<int> operation)
    {
        packer.WriteBits((byte)operation.type, 2);
        switch (operation.type)
        {
            case OperationType.Delete:
                WriteCurrentSize(packer, operation.index);
                WriteCurrentSize(packer, operation.length);
                break;
            case OperationType.Insert:
                WriteCurrentSize(packer, operation.index);
                WriteCurrentValues(packer, operation.values);
                break;
            case OperationType.Add:
                WriteCurrentValues(packer, operation.values);
                break;
            default:
                throw new InvalidOperationException($"Unexpected current Myers operation {operation.type}.");
        }
    }

    private static void WriteCurrentValues(BitPacker packer, DisposableList<int> values)
    {
        WriteCurrentSize(packer, values.Count);
        int last = 0;
        for (int i = 0; i < values.Count; i++)
        {
            int item = values[i];
            DeltaPacker<int>.WriteFunc(packer, last, item);
            last = item;
        }
    }

    private static List<int> ReadCurrentValues(BitPacker packer)
    {
        int count = ReadCurrentSize(packer);
        var result = new List<int>(count);
        int last = 0;
        for (int i = 0; i < count; i++)
        {
            int item = last;
            DeltaPacker<int>.ReadFunc(packer, last, ref item);
            result.Add(item);
            last = item;
        }

        return result;
    }

    private static void WriteCurrentSize(BitPacker packer, int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Packer<Size>.WriteFunc(packer, (Size)value);
    }

    private static int ReadCurrentSize(BitPacker packer)
    {
        Size value = default;
        Packer<Size>.ReadFunc(packer, ref value);
        if (value.value > int.MaxValue)
            throw new InvalidOperationException("An encoded current-Myers size exceeds Int32.MaxValue.");
        return (int)value.value;
    }

    private static void DisposeOperations(DisposableList<DiffOp<int>> operations)
    {
        for (int i = 0; i < operations.Count; i++)
            operations[i].values.Dispose();
        operations.Dispose();
    }

    private static long GetFullPayloadBits(List<int> oldValue, List<int> value)
    {
        if (value == null)
            return 1;
        int oldCount = oldValue?.Count ?? 0;
        return 1L + DeltaIntBits(oldCount, value.Count) + value.Count * 32L;
    }

    private static void WriteFullPayload(BitPacker packer, List<int> oldValue, List<int> value)
    {
        if (value == null)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        int oldCount = oldValue?.Count ?? 0;
        DeltaPacker<int>.WriteFunc(packer, oldCount, value.Count);
        for (int i = 0; i < value.Count; i++)
            packer.WriteBits(unchecked((uint)value[i]), 32);
    }

    private static List<int> ReadFullPayload(BitPacker packer, List<int> oldValue)
    {
        if (!packer.ReadBit())
            return null;
        int oldCount = oldValue?.Count ?? 0;
        int count = oldCount;
        DeltaPacker<int>.ReadFunc(packer, oldCount, ref count);
        if (count < 0)
            throw new InvalidOperationException("A full Myers fallback contains a negative list length.");
        var result = new List<int>(count);
        for (int i = 0; i < count; i++)
            result.Add(unchecked((int)(uint)packer.ReadBits(32)));
        return result;
    }

    private static void WriteCount(BitPacker packer, int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        ulong unsigned = (uint)value;
        ulong offset = 0;
        ulong groupSize = 4;
        int tier = 0;
        while (unsigned >= offset + groupSize)
        {
            packer.WriteBit(true);
            offset += groupSize;
            groupSize <<= 2;
            tier++;
        }

        packer.WriteBit(false);
        packer.WriteBits(unsigned - offset, checked((byte)((tier + 1) * 2)));
    }

    private static int ReadCount(BitPacker packer)
    {
        int tier = 0;
        while (packer.ReadBit())
        {
            if (++tier > 15)
                throw new InvalidOperationException("An encoded Myers count is malformed.");
        }

        ulong offset = 0;
        ulong groupSize = 4;
        for (int i = 0; i < tier; i++)
        {
            offset += groupSize;
            groupSize <<= 2;
        }

        ulong result = offset + packer.ReadBits(checked((byte)((tier + 1) * 2)));
        if (result > int.MaxValue)
            throw new InvalidOperationException("An encoded Myers count exceeds Int32.MaxValue.");
        return (int)result;
    }

    private static void WritePositiveCount(BitPacker packer, int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        WriteCount(packer, value - 1);
    }

    private static int ReadPositiveCount(BitPacker packer)
    {
        return checked(ReadCount(packer) + 1);
    }

    private static int CompactCountBits(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        ulong unsigned = (uint)value;
        ulong offset = 0;
        ulong groupSize = 4;
        int tiers = 1;
        while (unsigned >= offset + groupSize)
        {
            offset += groupSize;
            groupSize <<= 2;
            tiers++;
        }
        return checked(tiers * 3);
    }

    private static int CompactPositiveCountBits(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return CompactCountBits(value - 1);
    }

    private static int PackedUnsignedBits(ulong value)
    {
        int bits = 8;
        while ((value >>= 7) != 0)
            bits += 8;
        return bits;
    }

    private static bool ListsEqual(List<int> left, List<int> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    private static List<int> Clone(List<int> value)
    {
        return value == null ? null : new List<int>(value);
    }

    private static void CopyRange(List<int> source, int start, int count, List<int> destination)
    {
        ValidateRange(source.Count, start, count);
        for (int i = 0; i < count; i++)
            destination.Add(source[start + i]);
    }

    private static void ValidateRange(int length, int start, int count)
    {
        if (start < 0 || count < 0 || start > length || count > length - start)
            throw new InvalidOperationException("An encoded Myers range exceeds the collection bounds.");
    }

    private static void ValidateInsertIndex(List<int> list, int index)
    {
        if (index < 0 || index > list.Count)
            throw new InvalidOperationException("An encoded Myers insert index exceeds the collection bounds.");
    }

    private static MyersScratch Scratch => _scratch ?? (_scratch = new MyersScratch());

    private readonly struct Hunk
    {
        internal readonly int oldStart;
        internal readonly int oldCount;
        internal readonly int newStart;
        internal readonly int newCount;

        internal Hunk(int oldStart, int oldCount, int newStart, int newCount)
        {
            this.oldStart = oldStart;
            this.oldCount = oldCount;
            this.newStart = newStart;
            this.newCount = newCount;
        }
    }

    private sealed class MyersScratch
    {
        internal int[] frontier = Array.Empty<int>();
        internal int[] trace = Array.Empty<int>();
        internal byte[] reverseSteps = Array.Empty<byte>();
        internal readonly List<Hunk> hunks = new List<Hunk>(16);
        internal long[] mergeCosts = Array.Empty<long>();
        internal int[] mergePrevious = Array.Empty<int>();
        internal Hunk[] mergedHunks = Array.Empty<Hunk>();
        internal long[] segmentCosts = Array.Empty<long>();
        internal long[] adjacentDeltaPrefix = Array.Empty<long>();
        internal long[] contextDeltaPrefix = Array.Empty<long>();
        internal List<int> preparedOld;
        internal List<int> preparedNew;

        internal void EnsureFrontier(int frontierLength, int traceLength)
        {
            if (frontier.Length < frontierLength)
                frontier = new int[frontierLength];
            if (trace.Length < traceLength)
                trace = new int[traceLength];
        }

        internal void EnsureSteps(int length)
        {
            if (reverseSteps.Length < length)
                reverseSteps = new byte[length];
        }

        internal void EnsureMerge(int stateLength, int hunkCount, int newCount)
        {
            if (mergeCosts.Length < stateLength)
                mergeCosts = new long[stateLength];
            if (mergePrevious.Length < stateLength)
                mergePrevious = new int[stateLength];
            if (mergedHunks.Length < stateLength)
                mergedHunks = new Hunk[stateLength];
            int segmentCount = checked(hunkCount * hunkCount);
            if (segmentCosts.Length < segmentCount)
                segmentCosts = new long[segmentCount];
            if (adjacentDeltaPrefix.Length < newCount)
                adjacentDeltaPrefix = new long[newCount];
            int contextLength = checked(newCount + 1);
            if (contextDeltaPrefix.Length < contextLength)
                contextDeltaPrefix = new long[contextLength];
        }
    }
}

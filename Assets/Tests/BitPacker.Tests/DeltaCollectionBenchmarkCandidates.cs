using System;
using System.Collections.Generic;
using PurrNet.Packing;

/// <summary>
/// Exact, benchmark-only collection delta candidates. These deliberately are not registered with
/// <see cref="DeltaPacker{T}"/> so benchmarks can compare them with the production codecs without
/// changing global serialization state.
/// </summary>
internal sealed class DeltaCollectionBenchmarkCandidates
{
    private DeltaCollectionBenchmarkCandidates() { }

    const byte LIST_MODE_FULL = 0;
    const byte LIST_MODE_SPLICE = 1;
    const byte LIST_MODE_INDEXED = 2;
    const byte LIST_MODE_MYERS = 3;

    const byte LIST_VALUES_RAW = 0;
    const byte LIST_VALUES_SEQUENTIAL_DELTA = 1;
    const byte LIST_VALUES_CONTEXT_DELTA = 2;

    const byte BYTE_MODE_FULL = 0;
    const byte BYTE_MODE_REPLACEMENT = 1;
    const byte BYTE_MODE_INDEXED = 2;

    /// <summary>
    /// Writes an exact List&lt;int&gt; delta. Changed values retain the inheritance-aware delta framing
    /// (the declared-type/direct bit), then choose the smaller of a full direct payload and a
    /// common-prefix/common-suffix splice payload and, for equal lengths, an indexed sparse payload.
    /// </summary>
    public static bool WriteListAdaptive(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        return WriteListAdaptive(packer, oldValue, newValue, false);
    }

    /// <summary>
    /// Adds the bounded, replacement-aware Myers script as a fourth adaptive mode. The Myers search
    /// cost is paid only by this benchmark variant so the original adaptive result remains visible.
    /// </summary>
    public static bool WriteListAdaptiveMyers(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        return WriteListAdaptive(packer, oldValue, newValue, true);
    }

    private static bool WriteListAdaptive(BitPacker packer, List<int> oldValue, List<int> newValue,
        bool includeMyers)
    {
        if (ListsEqual(oldValue, newValue))
        {
            packer.WriteBit(false);
            return false;
        }

        packer.WriteBit(true);

        // This benchmark codec only handles the declared List<int> representation. Keeping this
        // bit makes its framing cost comparable with DeltaPacker<List<int>>'s class dispatch.
        packer.WriteBit(true);

        byte mode = LIST_MODE_FULL;
        long bestPayloadBits = GetFullListPayloadBits(newValue);
        int prefixLength = 0;
        int suffixLength = 0;
        int sameLengthChangedCount = int.MaxValue;

        if (oldValue != null && newValue != null)
        {
            GetCommonEnds(oldValue, newValue, out prefixLength, out suffixLength);
            int replacementLength = newValue.Count - prefixLength - suffixLength;
            long spliceBits = GetListSplicePayloadBits(
                oldValue, newValue, prefixLength, suffixLength, replacementLength);
            if (spliceBits < bestPayloadBits)
            {
                mode = LIST_MODE_SPLICE;
                bestPayloadBits = spliceBits;
            }

            if (oldValue.Count == newValue.Count)
            {
                long indexedBits = GetIndexedListPayloadBits(oldValue, newValue, out sameLengthChangedCount);
                if (indexedBits < bestPayloadBits)
                {
                    mode = LIST_MODE_INDEXED;
                    bestPayloadBits = indexedBits;
                }
            }

            // Sparse same-length replacements are already represented directly by indexed/splice
            // deltas. Myers needs two edits per replacement and cannot justify its search cost here;
            // keep it for length changes and reorder-like cases with a larger changed region.
            bool shouldTryMyers = includeMyers &&
                                  (oldValue.Count != newValue.Count || sameLengthChangedCount > 16);
            if (shouldTryMyers &&
                DeltaListMyersBenchmarkCandidates.PrepareCompact(oldValue, newValue, out long myersBits) &&
                myersBits < bestPayloadBits)
            {
                mode = LIST_MODE_MYERS;
            }
        }

        packer.WriteBits(mode, 2);
        switch (mode)
        {
            case LIST_MODE_FULL:
                WriteFullListPayload(packer, newValue);
                break;
            case LIST_MODE_SPLICE:
                WriteListSplice(packer, oldValue, newValue, prefixLength, suffixLength);
                break;
            case LIST_MODE_INDEXED:
                WriteListIndexed(packer, oldValue, newValue);
                break;
            case LIST_MODE_MYERS:
                DeltaListMyersBenchmarkCandidates.WritePreparedCompact(packer, oldValue, newValue);
                break;
        }

        return true;
    }

    /// <summary>
    /// Reads a value written by <see cref="WriteListAdaptive"/> without ever mutating
    /// <paramref name="oldValue"/>.
    /// </summary>
    public static void ReadListAdaptive(BitPacker packer, List<int> oldValue, ref List<int> value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue == null ? null : new List<int>(oldValue);
            return;
        }

        bool usesDeclaredType = packer.ReadBit();
        if (!usesDeclaredType)
            throw new InvalidOperationException("The benchmark List<int> codec only supports declared-type payloads.");

        byte mode = (byte)packer.ReadBits(2);
        if (mode == LIST_MODE_FULL)
        {
            value = ReadFullListPayload(packer);
            return;
        }

        if (oldValue == null)
            throw new InvalidOperationException("A compact list delta requires a non-null baseline.");

        if (mode == LIST_MODE_INDEXED)
        {
            value = ReadListIndexed(packer, oldValue);
            return;
        }

        if (mode == LIST_MODE_MYERS)
        {
            value = DeltaListMyersBenchmarkCandidates.ReadCompact(packer, oldValue);
            return;
        }

        if (mode != LIST_MODE_SPLICE)
            throw new InvalidOperationException($"Unknown benchmark list delta mode {mode}.");

        int prefixLength = ReadCount(packer);
        int suffixLength = ReadCount(packer);
        int replacementLength = ReadCount(packer);
        ValidateSplice(oldValue.Count, prefixLength, suffixLength, replacementLength);
        int removedLength = oldValue.Count - prefixLength - suffixLength;
        byte valueMode = replacementLength == 0 ? LIST_VALUES_RAW : (byte)packer.ReadBits(2);
        if (replacementLength != 0 && valueMode > LIST_VALUES_CONTEXT_DELTA)
            throw new InvalidOperationException($"Unknown list splice value mode {valueMode}.");

        var resultList = new List<int>(CheckedResultLength(prefixLength, suffixLength, replacementLength));
        for (int i = 0; i < prefixLength; i++)
            resultList.Add(oldValue[i]);

        int last = 0;
        for (int i = 0; i < replacementLength; i++)
        {
            int item;
            if (valueMode == LIST_VALUES_RAW)
            {
                item = unchecked((int)(uint)packer.ReadBits(32));
            }
            else
            {
                int baseline = valueMode == LIST_VALUES_SEQUENTIAL_DELTA
                    ? last
                    : GetListSpliceContextBaseline(oldValue, prefixLength, removedLength, resultList, i);
                item = baseline;
                DeltaPacker<int>.Read(packer, baseline, ref item);
            }

            resultList.Add(item);
            last = item;
        }

        int suffixStart = oldValue.Count - suffixLength;
        for (int i = suffixStart; i < oldValue.Count; i++)
            resultList.Add(oldValue[i]);

        value = resultList;
    }

    public static void ReadListAdaptiveMyers(BitPacker packer, List<int> oldValue, ref List<int> value)
    {
        ReadListAdaptive(packer, oldValue, ref value);
    }

    /// <summary>
    /// Writes an exact Dictionary&lt;int, int&gt; delta. The semantic branch serializes removed keys
    /// and added/updated entries; existing values use DeltaPacker&lt;int&gt;. The full direct payload is
    /// retained whenever it is no larger.
    /// </summary>
    public static bool WriteDictionaryAdaptive(BitPacker packer, Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue)
    {
        if (DictionariesEqual(oldValue, newValue))
        {
            packer.WriteBit(false);
            return false;
        }

        packer.WriteBit(true);

        // Dictionary<TKey, TValue> is non-sealed, so retain the public delta packer's direct-type
        // framing cost just as the List<int> candidate does.
        packer.WriteBit(true);

        bool useSemantic = CanUseSemanticDictionaryDelta(oldValue, newValue) &&
                           GetSemanticDictionaryPayloadBits(oldValue, newValue) <
                           GetFullDictionaryPayloadBits(newValue);

        packer.WriteBit(useSemantic);
        if (useSemantic)
            WriteDictionarySemantic(packer, oldValue, newValue);
        else
            WriteFullDictionaryPayload(packer, newValue);
        return true;
    }

    /// <summary>
    /// Reads a value written by <see cref="WriteDictionaryAdaptive"/> into a fresh dictionary.
    /// </summary>
    public static void ReadDictionaryAdaptive(BitPacker packer, Dictionary<int, int> oldValue,
        ref Dictionary<int, int> value)
    {
        if (!packer.ReadBit())
        {
            value = CloneDictionary(oldValue);
            return;
        }

        bool usesDeclaredType = packer.ReadBit();
        if (!usesDeclaredType)
        {
            throw new InvalidOperationException(
                "The benchmark Dictionary<int, int> codec only supports declared-type payloads.");
        }

        bool usesSemanticDelta = packer.ReadBit();
        if (!usesSemanticDelta)
        {
            value = ReadFullDictionaryPayload(packer);
            return;
        }

        if (oldValue == null)
            throw new InvalidOperationException("A semantic dictionary delta requires a non-null baseline.");

        var resultDictionary = CloneDictionary(oldValue);
        int removedCount = ReadCount(packer);
        for (int i = 0; i < removedCount; i++)
        {
            int key = ReadPackedInt(packer);
            resultDictionary.Remove(key);
        }

        int upsertCount = ReadCount(packer);
        for (int i = 0; i < upsertCount; i++)
        {
            int key = ReadPackedInt(packer);
            int newEntryValue;

            if (oldValue.TryGetValue(key, out int oldEntryValue))
            {
                newEntryValue = oldEntryValue;
                DeltaPacker<int>.Read(packer, oldEntryValue, ref newEntryValue);
            }
            else
            {
                newEntryValue = unchecked((int)(uint)packer.ReadBits(32));
            }

            resultDictionary[key] = newEntryValue;
        }

        value = resultDictionary;
    }

    /// <summary>
    /// Writes an exact byte-array delta, choosing between the full direct array payload, a
    /// common-prefix/common-suffix replacement payload, and an indexed sparse payload for
    /// equal-length arrays. The compact modes use a prefix code so the full fallback retains its
    /// one-bit mode cost: 0 = full, 10 = replacement, 11 = indexed.
    /// </summary>
    public static bool WriteBytesAdaptive(BitPacker packer, byte[] oldValue, byte[] newValue)
    {
        if (BytesEqual(oldValue, newValue))
        {
            packer.WriteBit(false);
            return false;
        }

        packer.WriteBit(true);

        byte mode = BYTE_MODE_FULL;
        long bestBits = 1 + GetFullBytesPayloadBits(newValue);
        int prefixLength = 0;
        int suffixLength = 0;
        if (oldValue != null && newValue != null)
        {
            GetCommonEnds(oldValue, newValue, out prefixLength, out suffixLength);
            int replacementLength = newValue.Length - prefixLength - suffixLength;
            long replacementBits = 2 + GetSplicePayloadBits(prefixLength, suffixLength, replacementLength, 8);
            if (replacementBits < bestBits)
            {
                mode = BYTE_MODE_REPLACEMENT;
                bestBits = replacementBits;
            }

            if (oldValue.Length == newValue.Length)
            {
                long indexedBits = 2 + GetIndexedBytesPayloadBits(oldValue, newValue);
                if (indexedBits < bestBits)
                    mode = BYTE_MODE_INDEXED;
            }
        }

        if (mode == BYTE_MODE_FULL)
        {
            packer.WriteBit(false);
            WriteFullBytesPayload(packer, newValue);
            return true;
        }

        packer.WriteBit(true);
        packer.WriteBit(mode == BYTE_MODE_INDEXED);
        if (mode == BYTE_MODE_INDEXED)
            WriteBytesIndexed(packer, oldValue, newValue);
        else
            WriteBytesReplacement(packer, newValue, prefixLength, suffixLength);
        return true;
    }

    /// <summary>
    /// Reads a value written by <see cref="WriteBytesAdaptive"/> into a fresh array.
    /// </summary>
    public static void ReadBytesAdaptive(BitPacker packer, byte[] oldValue, ref byte[] value)
    {
        if (!packer.ReadBit())
        {
            value = oldValue == null ? null : (byte[])oldValue.Clone();
            return;
        }

        bool usesCompactMode = packer.ReadBit();
        if (!usesCompactMode)
        {
            value = ReadFullBytesPayload(packer);
            return;
        }

        if (oldValue == null)
            throw new InvalidOperationException("A compact byte-array delta requires a non-null baseline.");

        bool usesIndexedMode = packer.ReadBit();
        if (usesIndexedMode)
        {
            value = ReadBytesIndexed(packer, oldValue);
            return;
        }

        int prefixLength = ReadByteCount(packer);
        int suffixLength = ReadByteCount(packer);
        int replacementLength = ReadByteCount(packer);
        ValidateSplice(oldValue.Length, prefixLength, suffixLength, replacementLength);

        int resultLength = CheckedResultLength(prefixLength, suffixLength, replacementLength);
        var resultBytes = new byte[resultLength];
        if (prefixLength != 0)
            Array.Copy(oldValue, 0, resultBytes, 0, prefixLength);

        if (replacementLength != 0)
            packer.ReadBytes(resultBytes.AsSpan(prefixLength, replacementLength));

        if (suffixLength != 0)
        {
            Array.Copy(oldValue, oldValue.Length - suffixLength, resultBytes,
                prefixLength + replacementLength, suffixLength);
        }

        value = resultBytes;
    }

    static void WriteListSplice(BitPacker packer, List<int> oldValue, List<int> newValue,
        int prefixLength, int suffixLength)
    {
        int replacementLength = newValue.Count - prefixLength - suffixLength;
        int removedLength = oldValue.Count - prefixLength - suffixLength;

        WriteCount(packer, prefixLength);
        WriteCount(packer, suffixLength);
        WriteCount(packer, replacementLength);

        byte valueMode = LIST_VALUES_RAW;
        if (replacementLength != 0)
        {
            valueMode = SelectListSpliceValueMode(
                oldValue, newValue, prefixLength, removedLength, replacementLength, out _);
            packer.WriteBits(valueMode, 2);
        }

        int replacementEnd = prefixLength + replacementLength;
        int last = 0;
        for (int i = prefixLength; i < replacementEnd; i++)
        {
            int item = newValue[i];
            int replacementIndex = i - prefixLength;
            if (valueMode == LIST_VALUES_RAW)
            {
                packer.WriteBits(unchecked((uint)item), 32);
            }
            else
            {
                int baseline = valueMode == LIST_VALUES_SEQUENTIAL_DELTA
                    ? last
                    : GetListSpliceContextBaseline(
                        oldValue, newValue, prefixLength, removedLength, replacementIndex);
                DeltaPacker<int>.Write(packer, baseline, item);
            }

            last = item;
        }
    }

    static void WriteFullListPayload(BitPacker packer, List<int> value)
    {
        if (value == null)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)value.Count, 31);
        for (int i = 0; i < value.Count; i++)
            packer.WriteBits(unchecked((uint)value[i]), 32);
    }

    static List<int> ReadFullListPayload(BitPacker packer)
    {
        if (!packer.ReadBit())
            return null;

        int count = checked((int)packer.ReadBits(31));
        var result = new List<int>(count);
        for (int i = 0; i < count; i++)
            result.Add(unchecked((int)(uint)packer.ReadBits(32)));
        return result;
    }

    static void WriteListIndexed(BitPacker packer, List<int> oldValue, List<int> newValue)
    {
        int changedCount = 0;
        for (int i = 0; i < oldValue.Count; i++)
        {
            if (oldValue[i] != newValue[i])
                changedCount++;
        }

        WriteCount(packer, changedCount);
        int previousIndex = -1;
        for (int i = 0; i < oldValue.Count; i++)
        {
            if (oldValue[i] == newValue[i])
                continue;

            WriteCount(packer, i - previousIndex - 1);
            DeltaPacker<int>.Write(packer, oldValue[i], newValue[i]);
            previousIndex = i;
        }
    }

    static List<int> ReadListIndexed(BitPacker packer, List<int> oldValue)
    {
        var result = new List<int>(oldValue);
        int changedCount = ReadCount(packer);
        int previousIndex = -1;
        for (int i = 0; i < changedCount; i++)
        {
            int indexGap = ReadCount(packer);
            long indexLong = (long)previousIndex + indexGap + 1;
            if (indexLong < 0 || indexLong >= oldValue.Count)
                throw new InvalidOperationException("An indexed list delta exceeds the baseline bounds.");

            int index = (int)indexLong;
            int item = oldValue[index];
            DeltaPacker<int>.Read(packer, oldValue[index], ref item);
            result[index] = item;
            previousIndex = index;
        }

        return result;
    }

    static void WriteDictionarySemantic(BitPacker packer, Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue)
    {
        int removedCount = 0;
        foreach (var pair in oldValue)
        {
            if (!newValue.ContainsKey(pair.Key))
                removedCount++;
        }

        WriteCount(packer, removedCount);
        foreach (var pair in oldValue)
        {
            if (!newValue.ContainsKey(pair.Key))
                WritePackedInt(packer, pair.Key);
        }

        int upsertCount = 0;
        foreach (var pair in newValue)
        {
            if (!oldValue.TryGetValue(pair.Key, out int oldEntryValue) || oldEntryValue != pair.Value)
                upsertCount++;
        }

        WriteCount(packer, upsertCount);
        foreach (var pair in newValue)
        {
            if (oldValue.TryGetValue(pair.Key, out int oldEntryValue))
            {
                if (oldEntryValue == pair.Value)
                    continue;

                WritePackedInt(packer, pair.Key);
                DeltaPacker<int>.Write(packer, oldEntryValue, pair.Value);
            }
            else
            {
                WritePackedInt(packer, pair.Key);
                packer.WriteBits(unchecked((uint)pair.Value), 32);
            }
        }
    }

    static void WriteFullDictionaryPayload(BitPacker packer, Dictionary<int, int> value)
    {
        if (value == null)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)value.Count, 31);
        foreach (var pair in value)
        {
            packer.WriteBits(unchecked((uint)pair.Key), 32);
            packer.WriteBits(unchecked((uint)pair.Value), 32);
        }
    }

    static Dictionary<int, int> ReadFullDictionaryPayload(BitPacker packer)
    {
        if (!packer.ReadBit())
            return null;

        int count = checked((int)packer.ReadBits(31));
        var result = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
        {
            int key = unchecked((int)(uint)packer.ReadBits(32));
            int item = unchecked((int)(uint)packer.ReadBits(32));
            result.Add(key, item);
        }

        return result;
    }

    static void WriteBytesReplacement(BitPacker packer, byte[] newValue, int prefixLength, int suffixLength)
    {
        int replacementLength = newValue.Length - prefixLength - suffixLength;

        WriteByteCount(packer, prefixLength);
        WriteByteCount(packer, suffixLength);
        WriteByteCount(packer, replacementLength);

        if (replacementLength != 0)
            packer.WriteBytes(newValue.AsSpan(prefixLength, replacementLength));
    }

    static void WriteBytesIndexed(BitPacker packer, byte[] oldValue, byte[] newValue)
    {
        int changedCount = 0;
        for (int i = 0; i < oldValue.Length; i++)
        {
            if (oldValue[i] != newValue[i])
                changedCount++;
        }

        WriteByteCount(packer, changedCount);
        int previousIndex = -1;
        for (int i = 0; i < oldValue.Length; i++)
        {
            if (oldValue[i] == newValue[i])
                continue;

            WriteByteCount(packer, i - previousIndex - 1);
            packer.WriteBits(newValue[i], 8);
            previousIndex = i;
        }
    }

    static byte[] ReadBytesIndexed(BitPacker packer, byte[] oldValue)
    {
        int changedCount = ReadByteCount(packer);
        if (changedCount > oldValue.Length)
            throw new InvalidOperationException("The indexed byte delta contains too many changes.");

        var result = (byte[])oldValue.Clone();
        int previousIndex = -1;
        for (int i = 0; i < changedCount; i++)
        {
            int gap = ReadByteCount(packer);
            long index = previousIndex + (long)gap + 1;
            if (index >= oldValue.Length)
                throw new InvalidOperationException("The indexed byte delta exceeds the baseline bounds.");

            result[index] = (byte)packer.ReadBits(8);
            previousIndex = (int)index;
        }

        return result;
    }

    static void WriteFullBytesPayload(BitPacker packer, byte[] value)
    {
        if (value == null)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        packer.WriteBits((uint)value.Length, 31);
        if (value.Length != 0)
            packer.WriteBytes(value);
    }

    static byte[] ReadFullBytesPayload(BitPacker packer)
    {
        if (!packer.ReadBit())
            return null;

        int count = checked((int)packer.ReadBits(31));
        var result = new byte[count];
        if (count != 0)
            packer.ReadBytes(result);
        return result;
    }

    static bool ListsEqual(List<int> oldValue, List<int> newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return true;
        if (oldValue == null || newValue == null || oldValue.Count != newValue.Count)
            return false;

        for (int i = 0; i < oldValue.Count; i++)
        {
            if (oldValue[i] != newValue[i])
                return false;
        }

        return true;
    }

    static bool DictionariesEqual(Dictionary<int, int> oldValue, Dictionary<int, int> newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return true;
        if (oldValue == null || newValue == null || oldValue.Count != newValue.Count)
            return false;

        foreach (var pair in oldValue)
        {
            if (!newValue.TryGetValue(pair.Key, out int newEntryValue) || newEntryValue != pair.Value)
                return false;
        }

        return true;
    }

    static bool BytesEqual(byte[] oldValue, byte[] newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return true;
        if (oldValue == null || newValue == null)
            return false;
        return oldValue.AsSpan().SequenceEqual(newValue);
    }

    // These costs match PurrNet's standard direct packers used by this benchmark: a one-bit null
    // marker, a 31-bit collection length, and fixed-width int/byte elements. Packed counts and keys
    // use the existing seven-data-bit/eight-wire-bit integer codec.
    static long GetFullListPayloadBits(List<int> value)
    {
        return value == null ? 1 : 32L + value.Count * 32L;
    }

    static long GetFullDictionaryPayloadBits(Dictionary<int, int> value)
    {
        return value == null ? 1 : 32L + value.Count * 64L;
    }

    static long GetFullBytesPayloadBits(byte[] value)
    {
        return value == null ? 1 : 32L + value.Length * 8L;
    }

    static long GetSplicePayloadBits(int prefixLength, int suffixLength, int replacementLength,
        int elementBits)
    {
        return PackedUnsignedBits((uint)prefixLength) +
               PackedUnsignedBits((uint)suffixLength) +
               PackedUnsignedBits((uint)replacementLength) +
               (long)replacementLength * elementBits;
    }

    static long GetListSplicePayloadBits(List<int> oldValue, List<int> newValue,
        int prefixLength, int suffixLength, int replacementLength)
    {
        int removedLength = oldValue.Count - prefixLength - suffixLength;
        SelectListSpliceValueMode(
            oldValue, newValue, prefixLength, removedLength, replacementLength, out long valueBits);
        return PackedUnsignedBits((uint)prefixLength) +
               PackedUnsignedBits((uint)suffixLength) +
               PackedUnsignedBits((uint)replacementLength) +
               (replacementLength == 0 ? 0 : 2 + valueBits);
    }

    static byte SelectListSpliceValueMode(List<int> oldValue, List<int> newValue,
        int prefixLength, int removedLength, int replacementLength, out long encodedBits)
    {
        byte mode = LIST_VALUES_RAW;
        encodedBits = (long)replacementLength * 32;

        long sequentialBits = 0;
        int last = 0;
        for (int i = 0; i < replacementLength; i++)
        {
            int item = newValue[prefixLength + i];
            sequentialBits += GetDeltaIntPayloadBits(last, item);
            last = item;
        }

        if (sequentialBits < encodedBits)
        {
            mode = LIST_VALUES_SEQUENTIAL_DELTA;
            encodedBits = sequentialBits;
        }

        long contextBits = 0;
        for (int i = 0; i < replacementLength; i++)
        {
            int item = newValue[prefixLength + i];
            int baseline = GetListSpliceContextBaseline(
                oldValue, newValue, prefixLength, removedLength, i);
            contextBits += GetDeltaIntPayloadBits(baseline, item);
        }

        if (contextBits < encodedBits)
        {
            mode = LIST_VALUES_CONTEXT_DELTA;
            encodedBits = contextBits;
        }

        return mode;
    }

    static int GetListSpliceContextBaseline(List<int> oldValue, List<int> newValue,
        int prefixLength, int removedLength, int replacementIndex)
    {
        if (replacementIndex < removedLength)
            return oldValue[prefixLength + replacementIndex];
        if (replacementIndex > 0)
            return newValue[prefixLength + replacementIndex - 1];
        if (prefixLength > 0)
            return oldValue[prefixLength - 1];
        return 0;
    }

    static int GetListSpliceContextBaseline(List<int> oldValue, int prefixLength,
        int removedLength, List<int> decoded, int replacementIndex)
    {
        if (replacementIndex < removedLength)
            return oldValue[prefixLength + replacementIndex];
        if (replacementIndex > 0)
            return decoded[decoded.Count - 1];
        if (prefixLength > 0)
            return oldValue[prefixLength - 1];
        return 0;
    }

    static long GetIndexedListPayloadBits(List<int> oldValue, List<int> newValue,
        out int changedCount)
    {
        changedCount = 0;
        int previousIndex = -1;
        long changedEntryBits = 0;

        for (int i = 0; i < oldValue.Count; i++)
        {
            if (oldValue[i] == newValue[i])
                continue;

            changedCount++;
            changedEntryBits += PackedUnsignedBits((uint)(i - previousIndex - 1));
            changedEntryBits += GetDeltaIntPayloadBits(oldValue[i], newValue[i]);
            previousIndex = i;
        }

        return PackedUnsignedBits((uint)changedCount) + changedEntryBits;
    }

    static long GetIndexedBytesPayloadBits(byte[] oldValue, byte[] newValue)
    {
        int changedCount = 0;
        int previousIndex = -1;
        long changedEntryBits = 0;

        for (int i = 0; i < oldValue.Length; i++)
        {
            if (oldValue[i] == newValue[i])
                continue;

            changedCount++;
            changedEntryBits += PackedUnsignedBits((uint)(i - previousIndex - 1)) + 8;
            previousIndex = i;
        }

        return PackedUnsignedBits((uint)changedCount) + changedEntryBits;
    }

    static long GetSemanticDictionaryPayloadBits(Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue)
    {
        int removedCount = 0;
        long removedEntryBits = 0;
        foreach (var pair in oldValue)
        {
            if (newValue.ContainsKey(pair.Key))
                continue;

            removedCount++;
            removedEntryBits += GetPackedIntPayloadBits(pair.Key);
        }

        int upsertCount = 0;
        long upsertEntryBits = 0;
        foreach (var pair in newValue)
        {
            if (oldValue.TryGetValue(pair.Key, out int oldEntryValue))
            {
                if (oldEntryValue == pair.Value)
                    continue;

                upsertCount++;
                upsertEntryBits += GetPackedIntPayloadBits(pair.Key);
                upsertEntryBits += GetDeltaIntPayloadBits(oldEntryValue, pair.Value);
            }
            else
            {
                upsertCount++;
                upsertEntryBits += GetPackedIntPayloadBits(pair.Key) + 32;
            }
        }

        return PackedUnsignedBits((uint)removedCount) + removedEntryBits +
               PackedUnsignedBits((uint)upsertCount) + upsertEntryBits;
    }

    static int GetPackedIntPayloadBits(int value)
    {
        return PackedUnsignedBits(PackingIntegers.ZigzagEncode(value));
    }

    static int GetDeltaIntPayloadBits(int oldValue, int newValue)
    {
        if (oldValue == newValue)
            return 1;

        long difference = newValue - (long)oldValue;
        return 1 + PackedUnsignedBits(PackingIntegers.ZigzagEncode(difference));
    }

    static int PackedUnsignedBits(ulong value)
    {
        int bits = 8;
        while ((value >>= 7) != 0)
            bits += 8;
        return bits;
    }

    static bool CanUseSemanticDictionaryDelta(Dictionary<int, int> oldValue,
        Dictionary<int, int> newValue)
    {
        if (oldValue == null || newValue == null)
            return false;

        // Dictionary comparers are not part of PurrNet's serialized representation. Restrict the
        // semantic candidate to the normal comparer so baseline and destination key semantics agree.
        var defaultComparer = EqualityComparer<int>.Default;
        return ReferenceEquals(oldValue.Comparer, defaultComparer) &&
               ReferenceEquals(newValue.Comparer, defaultComparer);
    }

    static Dictionary<int, int> CloneDictionary(Dictionary<int, int> value)
    {
        return value == null ? null : new Dictionary<int, int>(value, value.Comparer);
    }

    static void GetCommonEnds(List<int> oldValue, List<int> newValue, out int prefixLength,
        out int suffixLength)
    {
        int sharedLength = Math.Min(oldValue.Count, newValue.Count);
        prefixLength = 0;
        while (prefixLength < sharedLength && oldValue[prefixLength] == newValue[prefixLength])
            prefixLength++;

        suffixLength = 0;
        int remainingSharedLength = sharedLength - prefixLength;
        while (suffixLength < remainingSharedLength &&
               oldValue[oldValue.Count - suffixLength - 1] == newValue[newValue.Count - suffixLength - 1])
        {
            suffixLength++;
        }
    }

    static void GetCommonEnds(byte[] oldValue, byte[] newValue, out int prefixLength,
        out int suffixLength)
    {
        int sharedLength = Math.Min(oldValue.Length, newValue.Length);
        prefixLength = 0;
        while (prefixLength < sharedLength && oldValue[prefixLength] == newValue[prefixLength])
            prefixLength++;

        suffixLength = 0;
        int remainingSharedLength = sharedLength - prefixLength;
        while (suffixLength < remainingSharedLength &&
               oldValue[oldValue.Length - suffixLength - 1] == newValue[newValue.Length - suffixLength - 1])
        {
            suffixLength++;
        }
    }

    static void WriteCount(BitPacker packer, int value)
    {
        Packer<PackedUInt>.Write(packer, new PackedUInt((uint)value));
    }

    static int ReadCount(BitPacker packer)
    {
        PackedUInt value = default;
        Packer<PackedUInt>.Read(packer, ref value);
        if (value.value > int.MaxValue)
            throw new InvalidOperationException("The encoded collection count exceeds Int32.MaxValue.");
        return (int)value.value;
    }

    // Byte-array candidates keep their varuint wire explicit so benchmark results do not depend
    // on generic packer registration or IL post-processing.
    static void WriteByteCount(BitPacker packer, int value)
    {
        uint remaining = (uint)value;
        while (true)
        {
            packer.WriteBits(remaining & 0x7Fu, 7);
            remaining >>= 7;
            bool hasMore = remaining != 0;
            packer.WriteBit(hasMore);
            if (!hasMore)
                return;
        }
    }

    static int ReadByteCount(BitPacker packer)
    {
        ulong result = 0;
        for (int segment = 0; segment < 5; segment++)
        {
            result |= packer.ReadBits(7) << (segment * 7);
            bool hasMore = packer.ReadBit();
            if (!hasMore)
            {
                if (result > int.MaxValue)
                    throw new InvalidOperationException("The encoded byte-array count exceeds Int32.MaxValue.");
                return (int)result;
            }
        }

        throw new InvalidOperationException("The encoded byte-array count is malformed.");
    }

    static void WritePackedInt(BitPacker packer, int value)
    {
        Packer<PackedInt>.Write(packer, new PackedInt(value));
    }

    static int ReadPackedInt(BitPacker packer)
    {
        PackedInt value = default;
        Packer<PackedInt>.Read(packer, ref value);
        return value.value;
    }

    static void ValidateSplice(int oldLength, int prefixLength, int suffixLength, int replacementLength)
    {
        if (prefixLength > oldLength || suffixLength > oldLength - prefixLength)
            throw new InvalidOperationException("The encoded splice exceeds the baseline bounds.");

        CheckedResultLength(prefixLength, suffixLength, replacementLength);
    }

    static int CheckedResultLength(int prefixLength, int suffixLength, int replacementLength)
    {
        try
        {
            return checked(prefixLength + suffixLength + replacementLength);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The encoded splice result is too large.", exception);
        }
    }

}

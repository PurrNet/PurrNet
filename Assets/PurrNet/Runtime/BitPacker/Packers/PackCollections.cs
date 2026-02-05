using System;
using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Pooling;
using UnityEngine;
using Unity.Collections;

namespace PurrNet.Packing
{
    public static class PackCollections
    {
        [UsedByIL]
        public static void RegisterHashSet<T>()
        {
            Packer<HashSet<T>>.RegisterWriter(WriteCollection);
            Packer<HashSet<T>>.RegisterReader(ReadHashSet);

            PurrEquality<HashSet<T>>.OverrideDefault(new HashsetComparator<T>());
        }

        [UsedByIL]
        public static void RegisterList<T>()
        {
            Packer<List<T>>.RegisterWriter(WriteList);
            Packer<List<T>>.RegisterReader(ReadList);

            DeltaPacker<List<T>>.RegisterWriter(WriteDeltaList);
            DeltaPacker<List<T>>.RegisterReader(ReadDeltaList);

            PurrEquality<List<T>>.OverrideDefault(new ListComparator<T>());
        }

        [UsedByIL]
        public static void RegisterArray<T>()
        {
            Packer<T[]>.RegisterWriter(WriteList);
            Packer<T[]>.RegisterReader(ReadArray);

            DeltaPacker<T[]>.RegisterWriter(WriteDeltaList);
            DeltaPacker<T[]>.RegisterReader(ReadDeltaArray);

            PurrEquality<T[]>.OverrideDefault(new ArrayComparator<T>());
        }

        [UsedByIL]
        public static void RegisterNullable<T>() where T : struct
        {
            Packer<T?>.RegisterWriter(PackNullables.WriteNullable);
            Packer<T?>.RegisterReader(PackNullables.ReadNullable);

            DeltaPacker<T?>.RegisterWriter(PackNullables.WriteDeltaNullable);
            DeltaPacker<T?>.RegisterReader(PackNullables.ReadDeltaNullable);

            PurrEquality<T?>.OverrideDefault(new NullableComparator<T>());
        }

        [UsedByIL]
        public static void RegisterDictionary<TKey, TValue>()
        {
            Packer<Dictionary<TKey, TValue>>.RegisterWriter(WriteDictionary);
            Packer<Dictionary<TKey, TValue>>.RegisterReader(ReadDictionary);
            RegisterDisposableList<TKey>();
            RegisterDisposableList<TValue>();

            PurrEquality<Dictionary<TKey, TValue>>.OverrideDefault(new DictionaryComparator<TKey, TValue>());
        }

        [UsedByIL]
        public static void RegisterDisposableDictionary<TKey, TValue>()
        {
            Packer<DisposableDictionary<TKey, TValue>>.RegisterWriter(PackDisposableDictionary.WriteDictionary);
            Packer<DisposableDictionary<TKey, TValue>>.RegisterReader(PackDisposableDictionary.ReadDictionary);
            DeltaPacker<DisposableDictionary<TKey, TValue>>.RegisterWriter(PackDisposableDictionary.WriteDeltaDictionary);
            DeltaPacker<DisposableDictionary<TKey, TValue>>.RegisterReader(PackDisposableDictionary.ReadDeltaDictionary);
            RegisterDisposableList<TKey>();
            RegisterDisposableList<TValue>();
        }

        [UsedByIL]
        public static void RegisterQueue<T>()
        {
            Packer<Queue<T>>.RegisterWriter(WriteQueue);
            Packer<Queue<T>>.RegisterReader(ReadQueue);

            PurrEquality<Queue<T>>.OverrideDefault(new QueueComparator<T>());
        }

        [UsedByIL]
        public static void RegisterStack<T>()
        {
            Packer<Stack<T>>.RegisterWriter(WriteStack);
            Packer<Stack<T>>.RegisterReader(ReadStack);

            PurrEquality<Stack<T>>.OverrideDefault(new StackComparator<T>());
        }

        [UsedByIL]
        public static void RegisterDisposableArray<T>()
        {
            Packer<DisposableArray<T>>.RegisterWriter(WriteDArray);
            Packer<DisposableArray<T>>.RegisterReader(ReadDArray);
            DeltaPacker<DisposableArray<T>>.RegisterWriter(PackDisposableArrays.WriteDisposableArrayDelta);
            DeltaPacker<DisposableArray<T>>.RegisterReader(PackDisposableArrays.ReadDisposablArrayDelta);

            RegisterDisposableList<T>();
        }

        [UsedByIL]
        public static void RegisterDisposableList<T>()
        {
            Packer<DisposableList<T>>.RegisterWriter(PackDisposableLists.WriteDisposableList);
            Packer<DisposableList<T>>.RegisterReader(PackDisposableLists.ReadDisposableList);
            DeltaPacker<DisposableList<T>>.RegisterWriter(MyersPackDisposableLists.WriteDisposableDeltaList);
            DeltaPacker<DisposableList<T>>.RegisterReader(MyersPackDisposableLists.ReadDisposableDeltaList);
            DiffOpSerializer.Register<T>();
        }

        [UsedByIL]
        public static void RegisterDisposableHashSet<T>()
        {
            Packer<DisposableHashSet<T>>.RegisterWriter(WriteDisposableHashSet);
            Packer<DisposableHashSet<T>>.RegisterReader(ReadDisposableHashSet);
            DeltaPacker<DisposableHashSet<T>>.RegisterWriter(PackDisposableHashsets.WriteDisposableHashSetDelta);
            DeltaPacker<DisposableHashSet<T>>.RegisterReader(PackDisposableHashsets.ReadDisposableHashSetDelta);
            RegisterDisposableList<T>();
        }

        [UsedByIL]
        public static void RegisterNativeArray<T>() where T : unmanaged
        {
            Packer<NativeArray<T>>.RegisterWriter(PackNativeCollections.WriteNativeArray<T>);
            Packer<NativeArray<T>>.RegisterReader(PackNativeCollections.ReadNativeArray<T>);
            DeltaPacker<NativeArray<T>>.RegisterWriter(PackNativeCollections.WriteNativeArrayDelta<T>);
            DeltaPacker<NativeArray<T>>.RegisterReader(PackNativeCollections.ReadNativeArrayDelta<T>);
            PurrEquality<NativeArray<T>>.OverrideDefault(new NativeArrayComparator<T>());
            PurrCopy<NativeArray<T>>.Override(PackNativeCollections.CopyNativeArray<T>);
            DiffOpNativeSerializer.Register<T>();
        }

        [UsedByIL]
        public static void RegisterNativeList<T>() where T : unmanaged
        {
            Packer<NativeList<T>>.RegisterWriter(PackNativeCollections.WriteNativeList<T>);
            Packer<NativeList<T>>.RegisterReader(PackNativeCollections.ReadNativeList<T>);
            DeltaPacker<NativeList<T>>.RegisterWriter(MyersPackNativeLists.WriteNativeListDelta<T>);
            DeltaPacker<NativeList<T>>.RegisterReader(MyersPackNativeLists.ReadNativeListDelta<T>);
            PurrEquality<NativeList<T>>.OverrideDefault(new NativeListComparator<T>());
            PurrCopy<NativeList<T>>.Override(PackNativeCollections.CopyNativeList<T>);
            DiffOpNativeSerializer.Register<T>();
        }

        [UsedByIL]
        public static void WriteDArray<T>(this BitPacker packer, DisposableArray<T> value)
        {
            if (value.isDisposed)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);
            Packer<Size>.Write(packer, value.Count);

            for (int i = 0; i < value.Count; i++)
                Packer<T>.Write(packer, value[i]);
        }

        [UsedByIL]
        public static bool WriteDeltaDArray<T>(this BitPacker packer, DisposableArray<T> value, DisposableArray<T> newValue)
        {
            using var old = value.isDisposed ? default : DisposableList<T>.Create(value);
            using var @new = newValue.isDisposed ? default : DisposableList<T>.Create(newValue);

            return DeltaPacker<DisposableList<T>>.Write(packer, old, @new);
        }

        [UsedByIL]
        public static void ReadDeltaDArray<T>(this BitPacker packer, ref DisposableArray<T> value,
            ref DisposableArray<T> newValue)
        {
            using var old = value.isDisposed ? default : DisposableList<T>.Create(value);
            var @new = newValue.isDisposed ? default : DisposableList<T>.Create(newValue);

            DeltaPacker<DisposableList<T>>.Read(packer, old, ref @new);

            value.Dispose();

            if (!@new.isDisposed)
                value = DisposableArray<T>.Create(@new);

            @new.Dispose();
        }

        [UsedByIL]
        public static void ReadDArray<T>(this BitPacker packer, ref DisposableArray<T> value)
        {
            bool hasValue = packer.ReadBit();
            value.Dispose();

            if (!hasValue)
                return;

            int length = Packer<Size>.Read(packer);
            value = DisposableArray<T>.Create(length);

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value[i] = item;
            }
        }

        [UsedByIL]
        public static void WriteDisposableHashSet<T>(this BitPacker packer, DisposableHashSet<T> value)
        {
            if (value.isDisposed || value.set == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            foreach (var v in value)
                Packer<T>.Write(packer, v);
        }

        [UsedByIL]
        public static void ReadDisposableHashSet<T>(this BitPacker packer, ref DisposableHashSet<T> value)
        {
            value.Dispose();

            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
                return;

            long length = default;

            packer.ReadInteger(ref length, 31);
            value = DisposableHashSet<T>.Create((int)length);

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Add(item);
            }
        }

        public static void WriteQueue<T>(BitPacker packer, Queue<T> value)
        {
            if (value == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            foreach (var v in value)
                Packer<T>.Write(packer, v);
        }

        private static void ReadQueue<T>(BitPacker packer, ref Queue<T> value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (value == null)
                value = new Queue<T>((int)length);
            else value.Clear();

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Enqueue(item);
            }
        }

        private static void WriteStack<T>(BitPacker packer, Stack<T> value)
        {
            if (value == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            foreach (var v in value)
                Packer<T>.Write(packer, v);
        }

        private static void ReadStack<T>(BitPacker packer, ref Stack<T> value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (value == null)
                value = new Stack<T>((int)length);
            else value.Clear();

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Push(item);
            }
        }

        private static void WriteDictionary<K, V>(BitPacker packer, Dictionary<K, V> value)
        {
            if (value == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            foreach (var pair in value)
            {
                Packer<K>.Write(packer, pair.Key);
                Packer<V>.Write(packer, pair.Value);
            }
        }

        private static void ReadDictionary<K, V>(BitPacker packer, ref Dictionary<K, V> value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (value == null)
                value = new Dictionary<K, V>((int)length);
            else value.Clear();

            for (int i = 0; i < length; i++)
            {
                K key = default;
                V val = default;
                Packer<K>.Read(packer, ref key);
                Packer<V>.Read(packer, ref val);

                try
                {
                    value.Add(key, val);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static bool WriteDeltaList<T>(BitPacker packer, IList<T> oldvalue, IList<T> newvalue)
        {
            bool areEqual = Packer.AreEqual(oldvalue, newvalue);

            Packer<bool>.Write(packer, areEqual);

            if (!areEqual)
                WriteList(packer, newvalue);

            return areEqual;
        }

        private static void ReadDeltaArray<T>(BitPacker packer, T[] oldvalue, ref T[] value)
        {
            bool areEqual = default;
            packer.Read(ref areEqual);

            if (!areEqual)
                ReadArray(packer, ref value);
            else value = Packer.Copy(oldvalue);
        }

        private static void ReadDeltaList<T>(BitPacker packer, List<T> oldvalue, ref List<T> value)
        {
            bool areEqual = default;
            packer.Read(ref areEqual);

            if (!areEqual)
                ReadList(packer, ref value);
            else value = Packer.Copy(oldvalue);
        }

        [UsedByIL]
        public static void WriteCollection<T>(this BitPacker packer, ICollection<T> value)
        {
            if (value == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            foreach (var v in value)
                Packer<T>.Write(packer, v);
        }

        [UsedByIL]
        public static void ReadHashSet<T>(this BitPacker packer, ref HashSet<T> value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (value == null)
                value = new HashSet<T>((int)length);
            else value.Clear();

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                try
                {
                    value.Add(item);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        [UsedByIL]
        public static void WriteList<T>(this BitPacker packer, IList<T> value)
        {
            if (value == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int length = value.Count;
            packer.WriteInteger(length, 31);

            for (int i = 0; i < length; i++)
                Packer<T>.Write(packer, value[i]);
        }

        [UsedByIL]
        public static void ReadList<T>(this BitPacker packer, ref List<T> value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (value == null)
                value = new List<T>((int)length);
            else value.Clear();

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Add(item);
            }
        }

        [UsedByIL]
        public static void ReadArray<T>(this BitPacker packer, ref T[] value)
        {
            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            long length = default;

            packer.ReadInteger(ref length, 31);

            if (length == -1)
            {
                value = null;
                return;
            }

            if (value == null)
                value = new T[length];
            else if (value.Length != length)
                Array.Resize(ref value, (int)length);

            for (int i = 0; i < length; i++)
                Packer<T>.Read(packer, ref value[i]);
        }
    }
}

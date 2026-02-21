using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Modules;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableList<T> : IList<T>, IDisposable, IReadOnlyList<T>, IDuplicate<DisposableList<T>>, IEquatable<DisposableList<T>>
    {
        private bool _shouldDispose;

        public List<T> list { get; private set; }

        public DisposableList<T> Duplicate()
        {
            if (!_isAllocated)
                return default;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                int c = Count;
                int targetCapacity = c + Math.Max(c >> 2, 8);
                var res = Create(targetCapacity);
                for (var i = 0; i < c; ++i)
                    res.Add(PurrCopy<T>.Copy(list[i]));
                return res;
            }

            return Create(this);
        }

        public readonly bool Equals(DisposableList<T> other) => new ListComparator<T>().Equals(list, other.list);

        public override string ToString()
        {
            return string.Concat("[", string.Join(", ", list), "]");
        }

        [Obsolete("Use DisposableList<T>.Create instead")]
        public DisposableList(int capacity)
        {
            var newList = ListPool<T>.Instantiate();

            if (newList.Capacity < capacity)
                newList.Capacity = capacity;

            list = newList;
            _isAllocated = true;
            _shouldDispose = true;
        }

        public static DisposableList<T> Create(int capacity)
        {
            var val = new DisposableList<T>();
            var newList = ListPool<T>.Instantiate();

            if (newList.Capacity < capacity)
                newList.Capacity = capacity;

            val.list = newList;
            val._isAllocated = true;
            val._shouldDispose = true;
            return val;
        }

        public static DisposableList<T> Create(DisposableList<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val.list = ListPool<T>.Instantiate();

            int count = copyFrom.Count;
            int targetCapacity = count + Math.Max(count >> 2, 8);

            if (val.list.Capacity < targetCapacity)
                val.list.Capacity = targetCapacity;

            int c = copyFrom.Count;
            for (var i = 0; i < c; ++i)
                val.list.Add(copyFrom[i]);

            val._isAllocated = true;
            val._shouldDispose = true;
            return val;
        }

        public static DisposableList<T> Create(IList<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val.list = ListPool<T>.Instantiate();

            int count = copyFrom.Count;
            int targetCapacity = count + Math.Max(count >> 2, 8);

            if (val.list.Capacity < targetCapacity)
                val.list.Capacity = targetCapacity;

            int c = copyFrom.Count;
            for (var i = 0; i < c; ++i)
                val.list.Add(copyFrom[i]);

            val._isAllocated = true;
            val._shouldDispose = true;
            return val;
        }

        public static DisposableList<T> Create(IEnumerable<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val.list = ListPool<T>.Instantiate();
            val.list.AddRange(copyFrom);
            val._isAllocated = true;
            val._shouldDispose = true;
            return val;
        }

        public static DisposableList<T> Create()
        {
            var val = new DisposableList<T>();
            val.list = ListPool<T>.Instantiate();
            val._isAllocated = true;
            val._shouldDispose = true;
            return val;
        }

        public void AddRange(IList<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            int c = collection.Count;
            for (var i = 0; i < c; i++)
                list.Add(collection[i]);
            NotifyUsage();
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            foreach (var item in collection)
                list.Add(item);
            NotifyUsage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyUsage()
        {
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UpdateUsage(list);
#endif
        }

        public void Dispose()
        {
            if (isDisposed) return;

            if (_shouldDispose && list != null)
                ListPool<T>.Destroy(list);
            _isAllocated = false;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return GetEnumerator();
        }

        public void Add(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            list.Add(item);
            NotifyUsage();
        }

        public void Clear()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            list.Clear();
            NotifyUsage();
        }

        public bool Contains(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            list.CopyTo(array, arrayIndex);
            NotifyUsage();
        }

        public bool Remove(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return list.Remove(item);
        }

        public int Count
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();
                return list.Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();
                return false;
            }
        }

        private bool _isAllocated;

        public bool isDisposed => !_isAllocated;

        [UsedByIL]
        public T GetAt(int index)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return list[index];
        }

        public int IndexOf(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            list.RemoveAt(index);
        }

        public T this[int index]
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();
                if (index >= list.Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {list.Count}.");
                return list[index];
            }
            set
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();

                if (index >= list.Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {list.Count}.");
                list[index] = value;
            }
        }

        public void Reverse()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            list.Reverse();
        }

        public void RemoveRange(int opIndex, int opLength)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();

            if (opIndex + opLength > list.Count)
                throw new IndexOutOfRangeException($"Index {opIndex} + {opLength} is out of range for list of size {list.Count}.");
            if (opIndex < 0)
                throw new IndexOutOfRangeException($"Index {opIndex} is out of range for list of size {list.Count}.");

            list.RemoveRange(opIndex, opLength);
        }

        public void InsertRange(int index, IEnumerable<T> values)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            list.InsertRange(index, values);
        }

        public override int GetHashCode()
        {
            if (list == null)
                return 17;
            int result = 17;
            for (var i = 0; i < list.Count; i++)
                result = result * 31 + list[i].GetHashCode();
            return result;
        }
    }
}

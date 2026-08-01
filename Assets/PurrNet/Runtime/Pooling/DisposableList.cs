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
        private DisposableLease _lease;
        private int _leaseVersion;
        private List<T> _list;

        /// <summary>
        /// Direct access to the backing list.
        /// </summary>
        public List<T> list
        {
            get
            {
                if (isDisposed)
                    return null;
                return _list;
            }
            private set => _list = value;
        }

        internal List<T> rawList => isDisposed ? null : _list;

        /// <summary>
        /// Creates an independently owned copy. A regular struct assignment aliases the
        /// same pooled collection and must not be disposed independently.
        /// </summary>
        public DisposableList<T> Duplicate()
        {
            if (isDisposed)
                return default;

            int count = _list.Count;
            int targetCapacity = count + Math.Max(count >> 2, 8);
            var copy = Create(targetCapacity);

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                for (var i = 0; i < count; ++i)
                    copy.Add(PurrCopy<T>.Copy(_list[i]));
            }
            else
            {
                copy._list.AddRange(_list);
            }

            return copy;
        }

        public bool Equals(DisposableList<T> other)
        {
            var mine = isDisposed ? null : _list;
            var theirs = other.isDisposed ? null : other._list;

            if (ReferenceEquals(mine, theirs))
                return true;

            return new ListComparator<T>().Equals(mine, theirs);
        }

        public override string ToString()
        {
            if (_list == null || isDisposed)
            {
                return "null";
            }

            return string.Concat("[", string.Join(", ", _list), "]");
        }

        [Obsolete("Use DisposableList<T>.Create instead")]
        public DisposableList(int capacity)
        {
            var newList = ListPool<T>.Instantiate();

            if (newList.Capacity < capacity)
                newList.Capacity = capacity;

            _list = newList;
            _isAllocated = true;
            _shouldDispose = true;
            _lease = DisposableLeasePool.Rent(out _leaseVersion);
        }

        public static DisposableList<T> Create(int capacity)
        {
            var val = new DisposableList<T>();
            var newList = ListPool<T>.Instantiate();

            if (newList.Capacity < capacity)
                newList.Capacity = capacity;

            val._list = newList;
            val._isAllocated = true;
            val._shouldDispose = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableList<T> Create(DisposableList<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val._list = ListPool<T>.Instantiate();

            int count = copyFrom.Count;
            int targetCapacity = count + Math.Max(count >> 2, 8);

            if (val._list.Capacity < targetCapacity)
                val._list.Capacity = targetCapacity;

            int c = copyFrom.Count;
            for (var i = 0; i < c; ++i)
                val._list.Add(copyFrom[i]);

            val._isAllocated = true;
            val._shouldDispose = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableList<T> Create(IList<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val._list = ListPool<T>.Instantiate();

            int count = copyFrom.Count;
            int targetCapacity = count + Math.Max(count >> 2, 8);

            if (val._list.Capacity < targetCapacity)
                val._list.Capacity = targetCapacity;

            int c = copyFrom.Count;
            for (var i = 0; i < c; ++i)
                val._list.Add(copyFrom[i]);

            val._isAllocated = true;
            val._shouldDispose = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableList<T> Create(IEnumerable<T> copyFrom)
        {
            var val = new DisposableList<T>();
            val._list = ListPool<T>.Instantiate();
            val._list.AddRange(copyFrom);
            val._isAllocated = true;
            val._shouldDispose = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableList<T> Create()
        {
            var val = new DisposableList<T>();
            val._list = ListPool<T>.Instantiate();
            val._isAllocated = true;
            val._shouldDispose = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public void AddRange(IList<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            int c = collection.Count;
            for (var i = 0; i < c; i++)
                _list.Add(collection[i]);
            NotifyUsage();
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            foreach (var item in collection)
                _list.Add(item);
            NotifyUsage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyUsage()
        {
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UpdateUsage(_list);
#endif
        }

        public void Dispose()
        {
            if (isDisposed) return;

            if (_list != null && _shouldDispose)
                ListPool<T>.Destroy(_list);

            _isAllocated = false;
            _list = null;
            DisposableLeasePool.Return(_lease, _leaseVersion);
            _lease = null;
            _leaseVersion = 0;
        }

        public List<T>.Enumerator GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list.GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list.GetEnumerator();
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
            _list.Add(item);
            NotifyUsage();
        }

        public void Clear()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            _list.Clear();
            NotifyUsage();
        }

        public bool Contains(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            _list.CopyTo(array, arrayIndex);
            NotifyUsage();
        }

        public bool Remove(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list.Remove(item);
        }

        public int Count
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();
                return _list.Count;
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

        public bool isDisposed => !_isAllocated || !DisposableLeasePool.IsValid(_lease, _leaseVersion);

        [UsedByIL]
        public T GetAt(int index)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list[index];
        }

        public int IndexOf(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            return _list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            _list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            _list.RemoveAt(index);
        }

        public T this[int index]
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();
                if (index >= _list.Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {_list.Count}.");
                return _list[index];
            }
            set
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
                NotifyUsage();

                if (index >= _list.Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {_list.Count}.");
                _list[index] = value;
            }
        }

        public void Reverse()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            _list.Reverse();
        }

        public void RemoveRange(int opIndex, int opLength)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();

            if (opIndex + opLength > _list.Count)
                throw new IndexOutOfRangeException($"Index {opIndex} + {opLength} is out of range for list of size {_list.Count}.");
            if (opIndex < 0)
                throw new IndexOutOfRangeException($"Index {opIndex} is out of range for list of size {_list.Count}.");

            _list.RemoveRange(opIndex, opLength);
        }

        public void InsertRange(int index, IEnumerable<T> values)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            NotifyUsage();
            _list.InsertRange(index, values);
        }

        public override int GetHashCode()
        {
            if (_list == null || isDisposed)
                return 17;
            int result = 17;
            for (var i = 0; i < _list.Count; i++)
                result = result * 31 + _list[i].GetHashCode();
            return result;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
        private ListRefCounter _refs;

        /// <summary>
        /// Direct access to the backing list. This hands out mutable access, so if the
        /// buffer is shared with a snapshot it is unshared first. Prefer the regular
        /// list API for reads; it never forces an unshare. Do not cache the returned
        /// reference across a Duplicate() or history save: writes through a cached
        /// reference bypass copy-on-write and can corrupt shared snapshots.
        /// </summary>
        public List<T> list
        {
            get
            {
                if (isDisposed)
                    return null;
                EnsureUnique();
                return _list;
            }
            private set => _list = value;
        }

        internal List<T> rawList => isDisposed ? null : _list;

        internal int refCountForTests => _refs == null ? 0 : Volatile.Read(ref _refs.refs);

        internal bool SharesHandleWith(in DisposableList<T> other)
            => ReferenceEquals(_lease, other._lease) && _leaseVersion == other._leaseVersion;

        public DisposableList<T> Duplicate()
        {
            if (isDisposed)
                return default;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                int c = _list.Count;
                int targetCapacity = c + Math.Max(c >> 2, 8);
                var res = Create(targetCapacity);
                for (var i = 0; i < c; ++i)
                    res.Add(PurrCopy<T>.Copy(_list[i]));
                return res;
            }

            if (_refs == null)
                return Create(this);

            var val = new DisposableList<T>();
            val._list = _list;
            val._refs = _refs;
            Interlocked.Increment(ref _refs.refs);
            val._isAllocated = true;
            val._shouldDispose = _shouldDispose;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureUnique()
        {
            if (_refs == null || Volatile.Read(ref _refs.refs) <= 1)
                return;

            var clone = ListPool<T>.Instantiate();
            int count = _list.Count;

            if (clone.Capacity < count)
                clone.Capacity = count;

            for (var i = 0; i < count; ++i)
                clone.Add(_list[i]);

            if (Interlocked.Decrement(ref _refs.refs) == 0)
            {
                if (_shouldDispose)
                    ListPool<T>.Destroy(_list);
                ListRefCounterPool.Return(_refs);
            }

            // This handle takes the group's refcount unit with it, so any other struct
            // alias of the same handle would be left pointing at a buffer it no longer
            // holds a count for. Swapping the lease makes those aliases observably
            // disposed instead of silently reading a buffer that can be pooled under them.
            DisposableLeasePool.Return(_lease, _leaseVersion);
            _lease = DisposableLeasePool.Rent(out _leaseVersion);

            _list = clone;
            _refs = ListRefCounterPool.Rent();
            _shouldDispose = true;
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
            _refs = ListRefCounterPool.Rent();
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
            val._refs = ListRefCounterPool.Rent();
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
            val._refs = ListRefCounterPool.Rent();
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
            val._refs = ListRefCounterPool.Rent();
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
            val._refs = ListRefCounterPool.Rent();
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableList<T> Create()
        {
            var val = new DisposableList<T>();
            val._list = ListPool<T>.Instantiate();
            val._isAllocated = true;
            val._shouldDispose = true;
            val._refs = ListRefCounterPool.Rent();
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public void AddRange(IList<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
            int c = collection.Count;
            for (var i = 0; i < c; i++)
                _list.Add(collection[i]);
            NotifyUsage();
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
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

            if (_list != null && _refs != null)
            {
                if (Interlocked.Decrement(ref _refs.refs) == 0)
                {
                    if (_shouldDispose)
                        ListPool<T>.Destroy(_list);
                    ListRefCounterPool.Return(_refs);
                }
            }

            _isAllocated = false;
            _list = null;
            _refs = null;
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
            EnsureUnique();
            _list.Add(item);
            NotifyUsage();
        }

        public void Clear()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
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
            EnsureUnique();
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
            EnsureUnique();
            NotifyUsage();
            _list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
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
                EnsureUnique();
                NotifyUsage();

                if (index >= _list.Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {_list.Count}.");
                _list[index] = value;
            }
        }

        public void Reverse()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
            NotifyUsage();
            _list.Reverse();
        }

        public void RemoveRange(int opIndex, int opLength)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableList<T>));
            EnsureUnique();
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
            EnsureUnique();
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

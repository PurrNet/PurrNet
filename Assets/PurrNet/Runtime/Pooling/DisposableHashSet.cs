using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableHashSet<T> : ISet<T>, IDisposable, IDuplicate<DisposableHashSet<T>>, IEquatable<DisposableHashSet<T>>
    {
        private bool _isAllocated;
        private DisposableLease _lease;
        private int _leaseVersion;
        private HashSet<T> _set;
        private DisposableList<T> _items;

        public HashSet<T> set => isDisposed ? null : _set;

        public bool isDisposed => !_isAllocated || !DisposableLeasePool.IsValid(_lease, _leaseVersion);

        [Obsolete( "Use DisposableHashSet<T>.Create() instead")]
        public DisposableHashSet(int capacity)
        {
            var newSet = HashSetPool<T>.Instantiate();
            newSet.EnsureCapacity(capacity);

            _set = newSet;
            _items = DisposableList<T>.Create(capacity);
            _isAllocated = true;
            _lease = DisposableLeasePool.Rent(out _leaseVersion);
        }

        public static DisposableHashSet<T> Create()
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val._items = DisposableList<T>.Create();
            val._isAllocated = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableHashSet<T> Create(int capacity)
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val._set.EnsureCapacity(capacity);
            val._items = DisposableList<T>.Create(capacity);
            val._isAllocated = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableHashSet<T> Create(IEnumerable<T> copyFrom)
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val._items = DisposableList<T>.Create();
            foreach (var item in copyFrom)
            {
                if (val._set.Add(item))
                    val._items.Add(item);
            }
            val._isAllocated = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            if (_set != null)
            {
                HashSetPool<T>.Destroy(_set);
                _items.Dispose();
            }

            _isAllocated = false;
            _set = null;
            _items = default;
            DisposableLeasePool.Return(_lease, _leaseVersion);
            _lease = null;
            _leaseVersion = 0;
        }

        public Enumerator GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return new Enumerator(_items.rawList, _items.Count);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly List<T> _items;
            private readonly int _count;
            private int _index;

            internal Enumerator(List<T> items, int count)
            {
                _items = items;
                _count = count;
                _index = -1;
            }

            public bool MoveNext()
            {
                int next = _index + 1;
                if (next >= _count)
                    return false;
                _index = next;
                return true;
            }

            public void Reset()
            {
                _index = -1;
            }

            public T Current => _items[_index];

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        public void Add(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (_set.Add(item))
                _items.Add(item);
        }

        public void UnionWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            foreach (var item in other)
            {
                if (_set.Add(item))
                    _items.Add(item);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.IntersectWith(other);
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (!_set.Contains(_items[i]))
                    _items.RemoveAt(i);
            }
        }

        bool ISet<T>.Add(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            if (_set.Add(item))
            {
                _items.Add(item);
                return true;
            }
            return false;
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.ExceptWith(other);
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (!_set.Contains(_items[i]))
                    _items.RemoveAt(i);
            }
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.SymmetricExceptWith(other);
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (!_set.Contains(_items[i]))
                    _items.RemoveAt(i);
            }
            foreach (var item in other)
            {
                if (_set.Contains(item) && !_items.Contains(item))
                    _items.Add(item);
            }
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.IsSupersetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.IsProperSupersetOf(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.IsProperSubsetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.Overlaps(other);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.SetEquals(other);
        }

        public void Clear()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.Clear();
            _items.Clear();
        }

        public bool Contains(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            int count = _items.Count;
            for (int i = 0; i < count; i++)
                array[arrayIndex + i] = _items[i];
        }

        public bool Remove(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            if (_set.Remove(item))
            {
                _items.Remove(item);
                return true;
            }
            return false;
        }

        public int Count
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
                return _set.Count;
            }
        }

        public bool IsReadOnly => false;

        /// <summary>
        /// Creates an independently owned copy. A regular struct assignment aliases the
        /// same pooled collection and must not be disposed independently.
        /// </summary>
        public DisposableHashSet<T> Duplicate()
        {
            if (isDisposed)
                return default;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                var res = Create();
                foreach (var value in this)
                    res.Add(Packer.Copy(value));
                return res;
            }
            return Create(this);
        }

        public bool Equals(DisposableHashSet<T> other) => new HashsetComparator<T>().Equals(set, other.set);
    }
}

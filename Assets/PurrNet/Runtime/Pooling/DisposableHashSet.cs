using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableHashSet<T> : ISet<T>, IDisposable, IDuplicate<DisposableHashSet<T>>, IEquatable<DisposableHashSet<T>>
    {
        private HashSet<T> _set;

        public HashSet<T> set => _set;

        [Obsolete( "Use DisposableHashSet<T>.Create() instead")]
        public DisposableHashSet(int capacity)
        {
            var newSet = HashSetPool<T>.Instantiate();

            if (newSet.Count < capacity)
                newSet = new HashSet<T>(newSet);

            _set = newSet;
            isDisposed = false;
        }

        public static DisposableHashSet<T> Create()
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val.isDisposed = false;
            return val;
        }

        public static DisposableHashSet<T> Create(int capacity)
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val._set.EnsureCapacity(capacity);
            val.isDisposed = false;
            return val;
        }

        public static DisposableHashSet<T> Create(IEnumerable<T> copyFrom)
        {
            var val = new DisposableHashSet<T>();
            val._set = HashSetPool<T>.Instantiate();
            val._set.UnionWith(copyFrom);
            val.isDisposed = false;
            return val;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            HashSetPool<T>.Destroy(_set);
            isDisposed = true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return GetEnumerator();
        }

        public void Add(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            if (item == null) throw new ArgumentNullException(nameof(item));

            _set.Add(item);
        }

        public void UnionWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.UnionWith(other);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.IntersectWith(other);
        }

        bool ISet<T>.Add(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.Add(item);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.ExceptWith(other);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.SymmetricExceptWith(other);
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
        }

        public bool Contains(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            _set.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableHashSet<T>));
            return _set.Remove(item);
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
        public bool isDisposed { get; private set; }

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

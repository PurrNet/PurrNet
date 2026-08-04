using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableArray<T> : IDisposable, IReadOnlyList<T>, IList<T>, IDuplicate<DisposableArray<T>>, IEquatable<DisposableArray<T>>
    {
        private bool _shouldDispose;
        private DisposableLease _lease;
        private int _leaseVersion;
        private T[] _array;

        public T[] array
        {
            get => isDisposed ? null : _array;
            private set => _array = value;
        }

        public void Add(T item)
        {
            throw new InvalidOperationException("Cannot add to a DisposableArray");
        }

        public void Clear()
        {
            throw new InvalidOperationException("Cannot clear a DisposableArray");
        }

        public bool Contains(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            return Array.IndexOf(array, item, 0, Count) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            Array.Copy(this.array, 0, array, arrayIndex, Count);
        }

        public bool Remove(T item)
        {
            throw new InvalidOperationException("Cannot remove from a DisposableArray");
        }

        public int Count { get; private set; }

        public bool IsReadOnly => false;

        public bool isDisposed => !_shouldDispose || !DisposableLeasePool.IsValid(_lease, _leaseVersion);

        public int IndexOf(T item)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            return Array.IndexOf(array, item, 0, Count);
        }

        public void Insert(int index, T item)
        {
            throw new InvalidOperationException("Cannot insert into a DisposableArray");
        }

        public void RemoveAt(int index)
        {
            throw new InvalidOperationException("Cannot remove from a DisposableArray");
        }

        public T this[int index]
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
                if (index >= Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {Count}.");
                return array[index];
            }
            set
            {
                if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
                if (index >= Count || index < 0)
                    throw new IndexOutOfRangeException($"Index {index} is out of range for list of size {Count}.");
                array[index] = value;
            }
        }

        public static DisposableArray<T> Create(int size)
        {
            var rented = ArrayPool<T>.Shared.Rent(size);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(rented);
#endif
            Array.Clear(rented, 0, size);

            var result = new DisposableArray<T>
            {
                array = rented,
                Count = size,
                _shouldDispose = true
            };
            result._lease = DisposableLeasePool.Rent(out result._leaseVersion);
            return result;
        }

        /// <summary>
        /// Rents an array without clearing value-type contents. The caller must overwrite every
        /// element that can become observable before reading or exposing the array.
        /// </summary>
        internal static DisposableArray<T> CreateUninitialized(int size)
        {
            var rented = ArrayPool<T>.Shared.Rent(size);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(rented);
#endif
            var result = new DisposableArray<T>
            {
                array = rented,
                Count = size,
                _shouldDispose = true
            };
            result._lease = DisposableLeasePool.Rent(out result._leaseVersion);
            return result;
        }

        public static DisposableArray<T> Create(DisposableArray<T> copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Count);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            Array.Copy(copyFrom.array, array, copyFrom.Count);
            var result = new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Count,
                _shouldDispose = true
            };
            result._lease = DisposableLeasePool.Rent(out result._leaseVersion);
            return result;
        }

        public static DisposableArray<T> Create(IList<T> copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Count);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            copyFrom.CopyTo(array, 0);
            var result = new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Count,
                _shouldDispose = true
            };
            result._lease = DisposableLeasePool.Rent(out result._leaseVersion);
            return result;
        }

        public static DisposableArray<T> Create(T[] copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Length);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                for (int i = 0; i < copyFrom.Length; i++)
                    array[i] = PurrCopy<T>.Copy(copyFrom[i]);
            }
            else Array.Copy(copyFrom, array, copyFrom.Length);

            var result = new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Length,
                _shouldDispose = true
            };
            result._lease = DisposableLeasePool.Rent(out result._leaseVersion);
            return result;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            var rented = _array;
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UnTrack(rented);
#endif
            ArrayPool<T>.Shared.Return(rented, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            array = null;
            _shouldDispose = false;
            Count = 0;
            DisposableLeasePool.Return(_lease, _leaseVersion);
            _lease = null;
            _leaseVersion = 0;
        }

        public Enumerator GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            return new Enumerator(array, Count);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _array;
            private readonly int _count;
            private int _index;

            internal Enumerator(T[] array, int count)
            {
                _array = array;
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

            public T Current => _array[_index];

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        public void Resize(int valueCount)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            if (Count >= valueCount)
                return;

            var newArray = ArrayPool<T>.Shared.Rent(valueCount);
            Array.Copy(array, newArray, Count);
            Array.Clear(newArray, Count, valueCount - Count);
            var oldArray = _array;
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UnTrack(oldArray);
            AllocationTracker.Track(newArray);
#endif
            ArrayPool<T>.Shared.Return(oldArray, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            array = newArray;
            Count = valueCount;
            NotifyUsage();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyUsage()
        {
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UpdateUsage(array);
#endif
        }

        /// <summary>
        /// Creates an independently owned copy. A regular struct assignment aliases the
        /// same pooled collection and must not be disposed independently.
        /// </summary>
        public DisposableArray<T> Duplicate()
        {
            if (isDisposed)
                return default;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                var res = Create(Count);
                for (var i = Count - 1; i >= 0; --i)
                    res[i] = PurrCopy<T>.Copy(in array[i]);
                return res;
            }

            return Create(this);
        }

        public bool Equals(DisposableArray<T> other)
        {
            if (isDisposed || other.isDisposed)
                return isDisposed == other.isDisposed;
            if (Count != other.Count)
                return false;

            var equality = PurrEquality<T>.Default;
            for (int i = 0; i < Count; i++)
            {
                if (!equality.Equals(array[i], other.array[i]))
                    return false;
            }

            return true;
        }
    }
}

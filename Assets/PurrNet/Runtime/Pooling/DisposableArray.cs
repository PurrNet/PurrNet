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

        public T[] array { get; private set; }

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
            NotifyUsage();
            return Array.IndexOf(array, item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            NotifyUsage();
            Array.Copy(this.array, array, Count);
        }

        public bool Remove(T item)
        {
            throw new InvalidOperationException("Cannot remove from a DisposableArray");
        }

        public int Count { get; private set; }

        public bool IsReadOnly => false;

        public bool isDisposed => !_shouldDispose;

        public int IndexOf(T item)
        {
            NotifyUsage();
            return Array.IndexOf(array, item);
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

            return new DisposableArray<T>
            {
                array = rented,
                Count = size,
                _shouldDispose = true
            };
        }

        public static DisposableArray<T> Create(DisposableArray<T> copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Count);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            Array.Copy(copyFrom.array, array, copyFrom.Count);
            return new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Count,
                _shouldDispose = true
            };
        }

        public static DisposableArray<T> Create(IList<T> copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Count);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            copyFrom.CopyTo(array, 0);
            return new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Count,
                _shouldDispose = true
            };
        }

        public static DisposableArray<T> Create(T[] copyFrom)
        {
            var array = ArrayPool<T>.Shared.Rent(copyFrom.Length);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.Track(array);
#endif
            Array.Copy(copyFrom, array, copyFrom.Length);
            return new DisposableArray<T>
            {
                array = array,
                Count = copyFrom.Length,
                _shouldDispose = true
            };
        }

        public void Dispose()
        {
            if (!_shouldDispose) return;
            ArrayPool<T>.Shared.Return(array);
            array = null;
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UnTrack(array);
#endif
            _shouldDispose = false;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            NotifyUsage();
            for (int i = 0; i < Count; i++)
                yield return array[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            NotifyUsage();
            return GetEnumerator();
        }

        public void Resize(int valueCount)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DisposableArray<T>));
            if (Count >= valueCount)
                return;

            var newArray = ArrayPool<T>.Shared.Rent(valueCount);
            Array.Copy(array, newArray, Count);
            ArrayPool<T>.Shared.Return(array);
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UnTrack(array);
            AllocationTracker.Track(newArray);
#endif
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

        public bool Equals(DisposableArray<T> other) => new ArrayComparator<T>().Equals(array, other.array);
    }
}

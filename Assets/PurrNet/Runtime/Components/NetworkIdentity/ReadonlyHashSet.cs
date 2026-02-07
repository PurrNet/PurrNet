using System;
using System.Collections;
using System.Collections.Generic;

namespace PurrNet.Collections
{
    public static class CollectionUtils
    {
        public static T[] RemoveAt<T>(this T[] source, int index)
        {
            var dest = new T[source.Length - 1];
            if (index > 0)
                Array.Copy(source, 0, dest, 0, index);

            if (index < source.Length - 1)
                Array.Copy(source, index + 1, dest, index, source.Length - index - 1);

            return dest;
        }
    }

    /// <summary>Represents hash set which don't allow for items addition.</summary>
    /// <typeparam name="T">Type of items int he set.</typeparam>
    public interface IReadonlyHashSet<T> : IReadOnlyCollection<T>
    {
        /// <summary>Returns true if the set contains given item.</summary>
        public bool Contains(T i);
    }

    [DontPack]
    public class PurrHashSet<T> : ISet<T>, IReadonlyHashSet<T>
    {
        /// <inheritdoc cref="ICollection{T}.Count" />
        public int Count => set.Count;

        public bool IsReadOnly => false;

        private readonly HashSet<T> set;

        /// <summary>Creates new wrapper instance for given hash set.</summary>
        public PurrHashSet(HashSet<T> set) => this.set = set;

        public PurrHashSet() => set = new HashSet<T>();

        public PurrHashSet(int capacity) => set = new HashSet<T>(capacity);

        public bool Remove(T item)
        {

            return set.Remove(item);
        }

        void ICollection<T>.Add(T item)
        {
            set.Add(item);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            set.ExceptWith(other);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            set.IntersectWith(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return set.IsProperSubsetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return set.IsProperSupersetOf(other);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return set.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return set.IsSupersetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            return set.Overlaps(other);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            return set.SetEquals(other);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            set.SymmetricExceptWith(other);
        }

        public void UnionWith(IEnumerable<T> other)
        {
            set.UnionWith(other);
        }

        public bool Add(T item)
        {
            return set.Add(item);
        }

        public void Clear()
        {
            set.Clear();
        }

        /// <inheritdoc cref="ICollection{T}.Contains" />
        public bool Contains(T i) => set.Contains(i);

        public void CopyTo(T[] array, int arrayIndex)
        {
            set.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => set.GetEnumerator();
    }
}

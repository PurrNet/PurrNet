using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDisposable, IDuplicate<DisposableDictionary<TKey, TValue>>,
        IEquatable<DisposableDictionary<TKey, TValue>>
        where TKey : notnull
    {
        private bool _isAllocated;
        private DisposableLease _lease;
        private int _leaseVersion;

        public bool isDisposed => !_isAllocated || !DisposableLeasePool.IsValid(_lease, _leaseVersion);

        private DisposableList<TKey> _keys;
        private Dictionary<TKey, TValue> _dictionary;

        public Dictionary<TKey, TValue> dictionary => isDisposed ? null : _dictionary;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyUsage()
        {
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            AllocationTracker.UpdateUsage(dictionary);
#endif
        }

        public static DisposableDictionary<TKey, TValue> Create()
        {
            var val = new DisposableDictionary<TKey, TValue>();
            val._dictionary = DictionaryPool<TKey, TValue>.Instantiate();
            val._keys = DisposableList<TKey>.Create();
            val._isAllocated = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            return val;
        }

        public static DisposableDictionary<TKey, TValue> Create(IDictionary<TKey, TValue> copyFrom)
        {
            var val = new DisposableDictionary<TKey, TValue>();
            val._dictionary = DictionaryPool<TKey, TValue>.Instantiate();
            val._keys = DisposableList<TKey>.Create();
            val._isAllocated = true;
            val._lease = DisposableLeasePool.Rent(out val._leaseVersion);
            foreach (var kvp in copyFrom)
            {
                if (val._dictionary.TryAdd(kvp.Key, kvp.Value))
                    val._keys.Add(kvp.Key);
            }
            return val;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            if (_dictionary != null)
            {
                DictionaryPool<TKey, TValue>.Destroy(_dictionary);
                _keys.Dispose();
            }

            _isAllocated = false;
            _dictionary = null;
            _keys = default;
            DisposableLeasePool.Return(_lease, _leaseVersion);
            _lease = null;
            _leaseVersion = 0;
        }

        public Enumerator GetEnumerator()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return new Enumerator(_keys.rawList, dictionary, _keys.Count);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly List<TKey> _keys;
            private readonly Dictionary<TKey, TValue> _dictionary;
            private readonly int _count;
            private int _index;

            internal Enumerator(List<TKey> keys, Dictionary<TKey, TValue> dictionary, int count)
            {
                _keys = keys;
                _dictionary = dictionary;
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

            public KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    var key = _keys[_index];
                    return new KeyValuePair<TKey, TValue>(key, _dictionary[key]);
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            dictionary.Add(item.Key, item.Value);
            _keys.Add(item.Key);
        }

        public void Clear()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            _keys.Clear();
            dictionary.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.TryGetValue(item.Key, out var val) && EqualityComparer<TValue>.Default.Equals(val, item.Value);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex + dictionary.Count > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            for (int i = 0; i < _keys.Count; i++)
            {
                var key = _keys[i];
                array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(key, dictionary[key]);
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            if (dictionary.TryGetValue(item.Key, out var val) && EqualityComparer<TValue>.Default.Equals(val, item.Value))
            {
                dictionary.Remove(item.Key);
                _keys.Remove(item.Key);
                return true;
            }
            return false;
        }

        public int Count
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return dictionary.Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return false;
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            dictionary.Add(key, value);
            _keys.Add(key);
        }

        public bool ContainsKey(TKey key)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();

            if (dictionary.Remove(key))
            {
                _keys.Remove(key);
                return true;
            }

            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (isDisposed)
            {
                value = default;
                return false;
            }
            NotifyUsage();
            return dictionary.TryGetValue(key, out value);
        }

        public TValue this[TKey key]
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return dictionary[key];
            }
            set
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                if (dictionary.TryAdd(key, value))
                    _keys.Add(key);
                else
                    dictionary[key] = value;
            }
        }

        public IReadOnlyList<TKey> KeysReadOnly
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return _keys;
            }
        }

        public ICollection<TKey> Keys
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return _keys;
            }
        }

        public ICollection<TValue> Values
        {
            get => throw new NotSupportedException("Values may be mismatched with keys. Use dictionary.Values directly if needed.");
        }

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public TValue GetValueOrDefault(TKey key)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.GetValueOrDefault(key);
        }

        /// <summary>
        /// Creates an independently owned copy. A regular struct assignment aliases the
        /// same pooled collection and must not be disposed independently.
        /// </summary>
        public DisposableDictionary<TKey, TValue> Duplicate()
        {
            if (isDisposed)
                return default;

            var res = Create();

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>() ||
                RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
            {
                foreach (var pair in this)
                    res.Add(Packer.Copy(pair.Key), Packer.Copy(pair.Value));
            }
            else
            {
                foreach (var kvp in this)
                    res.Add(kvp.Key, kvp.Value);
            }

            return res;
        }

        public bool Equals(DisposableDictionary<TKey, TValue> other)
            => new DictionaryComparator<TKey, TValue>().Equals(dictionary, other.dictionary);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Packing;

namespace PurrNet.Pooling
{
    public struct DisposableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDisposable, IDuplicate<DisposableDictionary<TKey, TValue>>,
        IEquatable<DisposableDictionary<TKey, TValue>>
        where TKey : notnull
    {
        private bool _isAllocated;

        public bool isDisposed => !_isAllocated;

        private DisposableList<TKey> _keys;

        public Dictionary<TKey, TValue> dictionary { get; private set; }

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
            val.dictionary = DictionaryPool<TKey, TValue>.Instantiate();
            val._keys = DisposableList<TKey>.Create();
            val._isAllocated = true;
            return val;
        }

        public static DisposableDictionary<TKey, TValue> Create(IDictionary<TKey, TValue> copyFrom)
        {
            var val = new DisposableDictionary<TKey, TValue>();
            val.dictionary = DictionaryPool<TKey, TValue>.Instantiate();
            val._keys = DisposableList<TKey>.Create(copyFrom.Keys);
            val._isAllocated = true;
            foreach (var kvp in copyFrom)
                val.dictionary.Add(kvp.Key, kvp.Value);
            return val;
        }

        public void Dispose()
        {
            if (!_isAllocated) return;

            if (dictionary != null)
            {
                DictionaryPool<TKey, TValue>.Destroy(dictionary);
                _keys.Dispose();
            }

            _isAllocated = false;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            int count = _keys.Count;
            for (var i = 0; i < count; ++i)
            {
                var key = _keys[i];
                yield return new KeyValuePair<TKey, TValue>(key, dictionary[key]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return GetEnumerator();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            dictionary.Add(item.Key, item.Value);
            _keys.Add(item.Key);
        }

        public void Clear()
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            _keys.Clear();
            dictionary.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.TryGetValue(item.Key, out var val) && EqualityComparer<TValue>.Default.Equals(val, item.Value);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (!_isAllocated)
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
            if (!_isAllocated)
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
                if (!_isAllocated)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return dictionary.Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                if (!_isAllocated)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return false;
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            _keys.Add(key);
            dictionary.Add(key, value);
        }

        public bool ContainsKey(TKey key)
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (!_isAllocated)
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
            if (!_isAllocated)
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
                if (!_isAllocated)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return dictionary[key];
            }
            set
            {
                if (!_isAllocated)
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
                if (!_isAllocated)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return _keys;
            }
        }

        public ICollection<TKey> Keys
        {
            get
            {
                if (!_isAllocated)
                    throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
                NotifyUsage();
                return _keys;
            }
        }

        public ICollection<TValue> Values
        {
            get => throw new NotSupportedException("Values may be mismatched with keys. Use dictionary.Values directly if needed.");
        }

        public TValue GetValueOrDefault(TKey key)
        {
            if (!_isAllocated)
                throw new ObjectDisposedException(nameof(DisposableDictionary<TKey, TValue>));
            NotifyUsage();
            return dictionary.GetValueOrDefault(key);
        }

        public DisposableDictionary<TKey, TValue> Duplicate()
        {
            if (isDisposed)
                return default;

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>() ||
                RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
            {
                var res = Create();
                foreach (var pair in this)
                    res.Add(Packer.Copy(pair.Key), Packer.Copy(pair.Value));
                return res;
            }
            return Create(this);
        }

        public bool Equals(DisposableDictionary<TKey, TValue> other)
            => new DictionaryComparator<TKey, TValue>().Equals(dictionary, other.dictionary);
    }
}

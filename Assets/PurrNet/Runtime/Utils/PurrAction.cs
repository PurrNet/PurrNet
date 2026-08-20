using System;
using System.Runtime.CompilerServices;

namespace PurrNet.Utils
{
    public sealed class PurrAction<T> where T : class
    {
        private const int MinNullsBeforeCompact = 8;

        private T[] _listeners;
        private int _count;
        private int _nullCount;
        private int _invokeDepth;
        private readonly Action<T> _invoke;

        public int count => _count - _nullCount;

        public PurrAction(Action<T> invoke, int capacity = 0)
        {
            _invoke = invoke;
            _listeners = capacity > 0 ? new T[capacity] : Array.Empty<T>();
        }

        public void Add(T listener)
        {
            if (listener == null)
                return;

            if (_invokeDepth == 0 && ShouldCompact())
                Compact();

            if (_count == _listeners.Length)
                EnsureCapacity();

            _listeners[_count++] = listener;
        }

        public void Remove(T listener)
        {
            if (listener == null)
                return;

            var listeners = _listeners;

            for (var i = _count - 1; i >= 0; i--)
            {
                if (listeners[i] != listener)
                    continue;

                listeners[i] = null;
                _nullCount++;
                return;
            }
        }

        public void Invoke()
        {
            var count = _count;
            _invokeDepth++;

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var listener = _listeners[i];
                    if (listener != null)
                        _invoke(listener);
                }
            }
            finally
            {
                if (--_invokeDepth == 0 && _nullCount > 0)
                    Compact();
            }
        }

        /// <summary>Force reclamation of removed slots. No-op during dispatch.</summary>
        public void CompactNow()
        {
            if (_invokeDepth == 0 && _nullCount > 0)
                Compact();
        }

        public void Clear()
        {
            if (_invokeDepth > 0)
            {

                for (var i = 0; i < _count; i++)
                {
                    if (_listeners[i] == null)
                        continue;

                    _listeners[i] = null;
                    _nullCount++;
                }
                return;
            }

            Array.Clear(_listeners, 0, _count);
            _count = 0;
            _nullCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldCompact()
        {
            return _nullCount >= MinNullsBeforeCompact && _nullCount * 2 >= _count;
        }

        private void EnsureCapacity()
        {
            if (_invokeDepth == 0 && _nullCount > 0)
            {
                Compact();
                if (_count < _listeners.Length)
                    return;
            }

            Array.Resize(ref _listeners, _listeners.Length == 0 ? 4 : _listeners.Length * 2);
        }

        private void Compact()
        {
            var listeners = _listeners;
            var count = _count;
            var write = 0;

            for (var read = 0; read < count; read++)
            {
                var listener = listeners[read];
                if (listener != null)
                    listeners[write++] = listener;
            }

            Array.Clear(listeners, write, count - write);
            _count = write;
            _nullCount = 0;
        }
    }
}
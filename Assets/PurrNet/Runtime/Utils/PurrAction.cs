using System.Collections.Generic;

namespace PurrNet.Utils
{
    public sealed class PurrAction<T> where T : class, IBaseTickListener
    {
        private readonly List<T> _listeners;
        private readonly System.Action<T> _invoke;
        private bool _isInvoking;

        public PurrAction(System.Action<T> invoke, int capacity = 0)
        {
            _listeners = new List<T>(capacity);
            _invoke = invoke;
        }

        public void Add(T listener)
        {
            _listeners.Add(listener);
        }

        public void Remove(T listener)
        {
            for (var i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i] != listener)
                    continue;

                if (_isInvoking)
                    _listeners[i] = null;
                else
                    _listeners.RemoveAt(i);

                return;
            }
        }

        public void Invoke()
        {
            var count = _listeners.Count;
            _isInvoking = true;

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
                _isInvoking = false;
                Compact();
            }
        }

        private void Compact()
        {
            var writeIndex = 0;

            for (var readIndex = 0; readIndex < _listeners.Count; readIndex++)
            {
                var listener = _listeners[readIndex];
                if (listener == null)
                    continue;

                _listeners[writeIndex++] = listener;
            }

            if (writeIndex < _listeners.Count)
                _listeners.RemoveRange(writeIndex, _listeners.Count - writeIndex);
        }
    }
}

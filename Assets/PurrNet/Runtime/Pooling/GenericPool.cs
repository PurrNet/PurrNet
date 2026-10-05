using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace PurrNet.Pooling
{
    [UsedImplicitly]
    public class GenericPool<T>
    {
        private readonly Stack<T> _pool = new Stack<T>();
        private readonly Func<T> _factory;
        private readonly Action<T> _reset;

        [UsedImplicitly] public virtual int count => _pool.Count;

        public GenericPool(Func<T> factory, Action<T> reset)
        {
            _factory = factory;
            _reset = reset;
        }

        [UsedImplicitly]
        public virtual T Allocate()
        {
            return _pool.Count > 0 ? _pool.Pop() : _factory();
        }

        [UsedImplicitly]
        public virtual void Delete(T obj)
        {
            _reset(obj);
            _pool.Push(obj);
        }
    }
}
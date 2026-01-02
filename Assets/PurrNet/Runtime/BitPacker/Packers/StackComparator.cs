using System.Collections.Generic;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    internal readonly struct StackComparator<T> : IEqualityComparer<Stack<T>>
    {
        public bool Equals(Stack<T> x, Stack<T> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Count != y.Count) return false;

            using var xList = DisposableList<T>.Create(x);
            using var yList = DisposableList<T>.Create(y);

            return xList.Equals(yList);
        }

        public int GetHashCode(Stack<T> obj)
        {
            return obj.GetHashCode();
        }
    }
}
using System.Collections.Generic;

namespace PurrNet.Packing
{
    internal readonly struct ArrayComparator<T> : IEqualityComparer<T[]>
    {
        public bool Equals(T[] x, T[] y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Length != y.Length) return false;

            int count = x.Length;

            if (typeof(T).IsValueType)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!Packer.AreEqualRef(ref x[i], ref y[i]))
                        return false;
                }
            }
            else
            {
                // Covariant arrays permit reading elements, but taking a writable T ref can throw.
                for (int i = 0; i < count; i++)
                {
                    if (!PurrEquality<T>.Equals(x[i], y[i]))
                        return false;
                }
            }
            return true;
        }

        public int GetHashCode(T[] obj)
        {
            return obj.GetHashCode();
        }
    }
}

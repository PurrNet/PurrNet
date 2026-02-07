using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace PurrNet.Packing
{
    public static class FactoryCache<T>
    {
        public static readonly Func<T> Create = BuildFactory();

        private static Func<T> BuildFactory()
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return () => default;

            var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctor != null)
                return Activator.CreateInstance<T>;
            return () => (T)FormatterServices.GetUninitializedObject(typeof(T));
        }
    }
}

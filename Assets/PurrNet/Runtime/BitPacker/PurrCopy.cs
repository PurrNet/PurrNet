using System.Runtime.CompilerServices;
using PurrNet.Modules;

namespace PurrNet.Packing
{
    [UsedByIL]
    public static class PurrCopy
    {
        [UsedByIL]
        public static void Override<D>() where D : IDuplicate<D>
        {
            PurrCopy<D>.Copy = CopyMethod;
            return;

            static D CopyMethod(in D value) => value.Duplicate();
        }
    }

    public static class PurrCopy<T>
    {
        public delegate T CopyDelegate(in T value);

        public static CopyDelegate Copy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Duplicate(in T value)
        {
            return Copy(in value);
        }

        static T StructShortcut(in T value)
        {
            return value;
        }

        static T Fallback(in T value)
        {
            using var tmpPacker = BitPackerPool.Get();
            Packer<T>.WriteFunc(tmpPacker, value);
            tmpPacker.ResetPositionAndMode(true);
            var copy = default(T);
            Packer<T>.ReadFunc(tmpPacker, ref copy);
            return copy;
        }

        public static void Override(CopyDelegate copyDelegate)
        {
            Copy = copyDelegate;
        }

        static PurrCopy()
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                 Copy = StructShortcut;
            else Copy = Fallback;
        }
    }
}

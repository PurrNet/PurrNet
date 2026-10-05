using System;

namespace PurrNet.Packing
{
    public static class DeltaReadingScope
    {
        public static bool Continue<T>(BitPacker packer, T old, ref T newVal)
        {
            if (!packer.ReadBit())
            {
                if (IsDisposable<T>.value && newVal is IDisposable disposable)
                    disposable.Dispose();
                newVal = Packer.Copy(old);
                return false;
            }
            return true;
        }

        public static bool ContinueDisposable<T>(BitPacker packer, T old, ref T newVal) where T : IDisposable
        {
            if (!packer.ReadBit())
            {
                if (typeof(T).IsValueType || newVal != null)
                    newVal.Dispose();
                newVal = Packer.Copy(old);
                return false;
            }
            return true;
        }
    }

    internal static class IsDisposable<T>
    {
        public static readonly bool value = typeof(IDisposable).IsAssignableFrom(typeof(T));
    }
}

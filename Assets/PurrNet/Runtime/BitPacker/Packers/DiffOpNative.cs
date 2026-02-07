using System;
using Unity.Collections;

namespace PurrNet.Packing
{
    /// <summary>Native variant of <see cref="DiffOp{T}"/>: holds <see cref="NativeList{T}"/> for Add/Insert values. Caller must dispose.</summary>
    public struct DiffOpNative<T> : IDisposable where T : unmanaged
    {
        public readonly OperationType type;
        public readonly int index;
        public readonly int length;
        public NativeList<T> values;

        public DiffOpNative(OperationType type, int index, int length, NativeList<T> values = default)
        {
            this.type = type;
            this.index = index;
            this.length = length;
            this.values = values;
        }

        public static DiffOpNative<T> FinalOperation()
        {
            return new DiffOpNative<T>(OperationType.End, 0, 0);
        }

        public void Dispose()
        {
            if (values.IsCreated)
            {
                values.Dispose();
                values = default;
            }
        }
    }
}

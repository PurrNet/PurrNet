using JetBrains.Annotations;
using PurrNet.Modules;
using Unity.Collections;

namespace PurrNet.Packing
{
    /// <summary>Myers-based delta pack for <see cref="NativeList{T}"/>, using <see cref="MyersDiffNative"/> (unmanaged collections only).</summary>
    public static class MyersPackNativeLists
    {
        /// <summary>Allocator used for the diff result and internal buffers during write. Temp is safe since we consume immediately.</summary>
        [UsedImplicitly]
        public static Allocator DiffAllocator { get; set; } = Allocator.Temp;

        [UsedByIL]
        public static bool WriteNativeListDelta<T>(BitPacker packer, NativeList<T> old, NativeList<T> value) where T : unmanaged
        {
            var scope = new DeltaWritingScope(packer);

            if (NativeListEqual(old, value))
                return scope.Complete();

            if (!value.IsCreated)
            {
                scope.Write<bool>(false);
                return scope.Complete();
            }

            scope.Write<bool>(true);

            NativeList<DiffOpNative<T>> changes;
            if (!old.IsCreated || old.Length == 0)
            {
                var empty = new NativeList<T>(0, Allocator.Temp);
                try
                {
                    changes = MyersDiffNative.Diff(empty, value, DiffAllocator);
                }
                finally
                {
                    empty.Dispose();
                }
            }
            else
            {
                changes = MyersDiffNative.Diff(old, value, DiffAllocator);
            }

            try
            {
                for (int i = 0; i < changes.Length; i++)
                    Packer<DiffOpNative<T>>.Write(packer, changes[i]);
                scope.Write(DiffOpNative<T>.FinalOperation());

                return scope.Complete();
            }
            finally
            {
                for (int i = 0; i < changes.Length; i++)
                    changes[i].Dispose();
                changes.Dispose();
            }
        }

        [UsedByIL]
        public static void ReadNativeListDelta<T>(BitPacker packer, NativeList<T> old, ref NativeList<T> value) where T : unmanaged
        {
            if (!packer.ReadBit())
            {
                if (value.IsCreated)
                    value.Dispose();
                if (old.IsCreated)
                {
                    value = new NativeList<T>(old.Length, PackNativeCollections.ReadAllocator);
                    for (int i = 0; i < old.Length; i++)
                        value.Add(old[i]);
                }
                else
                    value = default;
                return;
            }

            if (!packer.ReadBit())
            {
                if (value.IsCreated)
                    value.Dispose();
                value = default;
                return;
            }

            if (!value.IsCreated)
                value = new NativeList<T>(0, PackNativeCollections.ReadAllocator);
            else
                value.Clear();

            if (old.IsCreated)
            {
                for (int i = 0; i < old.Length; i++)
                    value.Add(old[i]);
            }

            var changes = new NativeList<DiffOpNative<T>>(8, Allocator.Temp);
            try
            {
                while (true)
                {
                    var operation = default(DiffOpNative<T>);
                    Packer<DiffOpNative<T>>.Read(packer, ref operation);
                    if (operation.type == OperationType.End)
                    {
                        operation.Dispose();
                        break;
                    }
                    changes.Add(operation);
                }

                if (changes.Length > 0)
                {
                    MyersDiffNative.Apply(value, changes);
                    for (int i = 0; i < changes.Length; i++)
                        changes[i].Dispose();
                }
            }
            finally
            {
                changes.Dispose();
            }
        }

        static bool NativeListEqual<T>(NativeList<T> a, NativeList<T> b) where T : unmanaged
        {
            if (!a.IsCreated && !b.IsCreated) return true;
            if (!a.IsCreated || !b.IsCreated) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!PurrEquality<T>.Default.Equals(a[i], b[i]))
                    return false;
            }
            return true;
        }
    }
}

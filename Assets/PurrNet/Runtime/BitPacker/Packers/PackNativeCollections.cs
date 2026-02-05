using JetBrains.Annotations;
using PurrNet.Modules;
using Unity.Collections;

namespace PurrNet.Packing
{
    public static class PackNativeCollections
    {
        [UsedImplicitly]
        public static Allocator ReadAllocator { get; set; } = Allocator.Persistent;

        /// <summary>Fast copy for PurrCopy; uses NativeArray.Copy instead of packer round-trip.</summary>
        [UsedByIL]
        public static NativeArray<T> CopyNativeArray<T>(in NativeArray<T> value) where T : unmanaged
        {
            if (!value.IsCreated) return default;
            var copy = new NativeArray<T>(value.Length, ReadAllocator);
            NativeArray<T>.Copy(value, copy);
            return copy;
        }

        /// <summary>Fast copy for PurrCopy; copies elements into a new list instead of packer round-trip.</summary>
        [UsedByIL]
        public static NativeList<T> CopyNativeList<T>(in NativeList<T> value) where T : unmanaged
        {
            if (!value.IsCreated) return default;
            var copy = new NativeList<T>(value.Length, ReadAllocator);
            copy.AddRange(value.AsArray());
            return copy;
        }

        [UsedByIL]
        public static void WriteNativeArray<T>(BitPacker packer, NativeArray<T> value) where T : unmanaged
        {
            if (!value.IsCreated)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);
            int length = value.Length;
            Packer<Size>.Write(packer, (uint)length);
            for (int i = 0; i < length; i++)
                Packer<T>.Write(packer, value[i]);
        }

        [UsedByIL]
        public static void ReadNativeArray<T>(BitPacker packer, ref NativeArray<T> value) where T : unmanaged
        {
            if (value.IsCreated)
                value.Dispose();

            bool hasValue = default;
            packer.Read(ref hasValue);
            if (!hasValue)
                return;

            Size length = default;
            Packer<Size>.Read(packer, ref length);
            int len = (int)length.value;
            value = new NativeArray<T>(len, ReadAllocator);

            for (int i = 0; i < len; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value[i] = item;
            }
        }

        [UsedByIL]
        public static bool WriteNativeArrayDelta<T>(BitPacker packer, NativeArray<T> old, NativeArray<T> @new) where T : unmanaged
        {
            var oldList = NativeArrayToTempList(old);
            var newList = NativeArrayToTempList(@new);
            try
            {
                return MyersPackNativeLists.WriteNativeListDelta(packer, oldList, newList);
            }
            finally
            {
                if (oldList.IsCreated) oldList.Dispose();
                if (newList.IsCreated) newList.Dispose();
            }
        }

        [UsedByIL]
        public static void ReadNativeArrayDelta<T>(BitPacker packer, NativeArray<T> old, ref NativeArray<T> value) where T : unmanaged
        {
            var oldList = NativeArrayToTempList(old);
            var resultList = default(NativeList<T>);
            try
            {
                MyersPackNativeLists.ReadNativeListDelta(packer, oldList, ref resultList);

                if (value.IsCreated && value.Length != (resultList.IsCreated ? resultList.Length : 0))
                {
                    value.Dispose();
                    value = default;
                }
                if (!resultList.IsCreated)
                    return;
                int len = resultList.Length;
                if (len == 0)
                {
                    if (value.IsCreated) value.Dispose();
                    value = default;
                    return;
                }
                if (!value.IsCreated)
                    value = new NativeArray<T>(len, ReadAllocator);
                for (int i = 0; i < len; i++)
                    value[i] = resultList[i];
            }
            finally
            {
                if (oldList.IsCreated) oldList.Dispose();
                if (resultList.IsCreated) resultList.Dispose();
            }
        }

        static NativeList<T> NativeArrayToTempList<T>(NativeArray<T> arr) where T : unmanaged
        {
            if (!arr.IsCreated)
                return new NativeList<T>(0, Allocator.Temp);
            var list = new NativeList<T>(arr.Length, Allocator.Temp);
            for (int i = 0; i < arr.Length; i++)
                list.Add(arr[i]);
            return list;
        }

        [UsedByIL]
        public static void WriteNativeList<T>(BitPacker packer, NativeList<T> value) where T : unmanaged
        {
            if (!value.IsCreated)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);
            int length = value.Length;
            Packer<Size>.Write(packer, (uint)length);
            for (int i = 0; i < length; i++)
                Packer<T>.Write(packer, value[i]);
        }

        [UsedByIL]
        public static void ReadNativeList<T>(BitPacker packer, ref NativeList<T> value) where T : unmanaged
        {
            if (value.IsCreated)
                value.Dispose();

            bool hasValue = default;
            packer.Read(ref hasValue);
            if (!hasValue)
                return;

            Size length = default;
            Packer<Size>.Read(packer, ref length);
            int len = (int)length.value;
            value = new NativeList<T>(len, ReadAllocator);

            for (int i = 0; i < len; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Add(item);
            }
        }
    }
}

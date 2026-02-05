using System;
using PurrNet.Modules;
using Unity.Collections;

namespace PurrNet.Packing
{
    /// <summary>Serializes <see cref="DiffOpNative{T}"/> in the same format as <see cref="DiffOpSerializer"/> (type, index, length, delta-compressed list for values). Mirror of <see cref="DiffOpSerializer"/> for native collections.</summary>
    public static class DiffOpNativeSerializer
    {
        [UsedByIL]
        public static void Register<T>() where T : unmanaged
        {
            Packer<DiffOpNative<T>>.RegisterWriter(WriteOperation);
            Packer<DiffOpNative<T>>.RegisterReader(ReadOperation);
            DeltaPacker<DiffOpNative<T>>.RegisterWriter(DeltaWrite);
            DeltaPacker<DiffOpNative<T>>.RegisterReader(DeltaRead);
        }

        [UsedByIL]
        public static void WriteOperation<T>(this BitPacker packer, DiffOpNative<T> value) where T : unmanaged
        {
            Packer<OperationType>.Write(packer, value.type);

            switch (value.type)
            {
                case OperationType.End:
                    return;
                case OperationType.Delete:
                    Packer<Size>.Write(packer, value.index);
                    Packer<Size>.Write(packer, value.length);
                    break;
                case OperationType.Insert:
                    Packer<Size>.Write(packer, value.index);
                    WriteListCompressed(packer, value.values);
                    break;
                case OperationType.Add:
                    WriteListCompressed(packer, value.values);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static void WriteListCompressed<T>(BitPacker packer, NativeList<T> values) where T : unmanaged
        {
            if (!values.IsCreated)
            {
                Packer<Size>.Write(packer, 0);
                return;
            }
            T last = default;
            Packer<Size>.Write(packer, (uint)values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                DeltaPacker<T>.Write(packer, last, v);
                last = v;
            }
        }

        static NativeList<T> ReadListCompressed<T>(BitPacker packer) where T : unmanaged
        {
            var count = Packer<Size>.Read(packer);
            int len = (int)count.value;
            var list = new NativeList<T>(len, PackNativeCollections.ReadAllocator);
            var last = default(T);

            for (int i = 0; i < len; i++)
            {
                T current = default;
                DeltaPacker<T>.Read(packer, last, ref current);
                last = current;
                list.Add(current);
            }

            return list;
        }

        [UsedByIL]
        public static void ReadOperation<T>(this BitPacker packer, ref DiffOpNative<T> value) where T : unmanaged
        {
            value.Dispose();

            var type = Packer<OperationType>.Read(packer);
            Size index = default;
            Size length = default;

            switch (type)
            {
                case OperationType.End:
                    value = DiffOpNative<T>.FinalOperation();
                    break;
                case OperationType.Delete:
                    Packer<Size>.Read(packer, ref index);
                    Packer<Size>.Read(packer, ref length);
                    value = new DiffOpNative<T>(type, (int)index.value, (int)length.value);
                    break;
                case OperationType.Insert:
                    Packer<Size>.Read(packer, ref index);
                    var valuesInsert = ReadListCompressed<T>(packer);
                    value = new DiffOpNative<T>(type, (int)index.value, valuesInsert.Length, valuesInsert);
                    break;
                case OperationType.Add:
                    var valuesAdd = ReadListCompressed<T>(packer);
                    value = new DiffOpNative<T>(type, 0, valuesAdd.Length, valuesAdd);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [UsedByIL]
        public static bool DeltaWrite<T>(this BitPacker packer, DiffOpNative<T> old, DiffOpNative<T> newVal) where T : unmanaged
        {
            var scope = new DeltaWritingScope(packer);

            if (Packer.AreEqual(old, newVal))
                return scope.CompleteWithoutChanges();

            packer.WriteOperation<T>(newVal);
            return scope.CompleteWithChanges();
        }

        [UsedByIL]
        public static void DeltaRead<T>(this BitPacker packer, DiffOpNative<T> old, ref DiffOpNative<T> newVal) where T : unmanaged
        {
            if (!DeltaReadingScope.Continue(packer, old, ref newVal))
                return;

            packer.ReadOperation<T>(ref newVal);
        }
    }
}

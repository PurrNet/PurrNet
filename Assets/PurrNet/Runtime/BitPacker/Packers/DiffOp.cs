using System;
using PurrNet.Modules;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public struct DiffOp<T> : IDisposable
    {
        public readonly OperationType type;
        public readonly int index;       // for Insert/Delete/Replace
        public readonly int length;      // for Delete (count) / Replace (old length)
        public DisposableList<T> values; // for Insert/Replace/Add

        public DiffOp(OperationType type, int index, int length, DisposableList<T> values = default)
        {
            this.type = type;
            this.index = index;
            this.length = length;
            this.values = values;
        }

        public static DiffOp<T> FinalOperation()
        {
            return new DiffOp<T>(OperationType.End, 0, 0);
        }

        public override string ToString()
        {
            return type switch
            {
                OperationType.Delete => $"Delete {length} at [{index}]",
                OperationType.Insert => $"Insert {{{string.Join(",", values)}}} at [{index}]",
                OperationType.Add => $"Add {{{string.Join(",", values)}}}",
                _ => base.ToString()
            };
        }

        public void Dispose()
        {
            // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
            values.Dispose();
        }
    }

    public static class DiffOpSerializer
    {
        [UsedByIL]
        public static void Register<T>()
        {
            Packer<DiffOp<T>>.RegisterWriter(WriteOperation);
            Packer<DiffOp<T>>.RegisterReader(ReadOperation);
            DeltaPacker<DiffOp<T>>.RegisterWriter(DeltaWrite);
            DeltaPacker<DiffOp<T>>.RegisterReader(DeltaRead);
        }

        [UsedByIL]
        public static void WriteOperation<T>(this BitPacker packer, DiffOp<T> value)
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
                    //Packer<DisposableList<T>>.Write(packer, value.values);
                    break;
                case OperationType.Add:
                    WriteListCompressed(packer, value.values);
                    //Packer<DisposableList<T>>.Write(packer, value.values);
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        private static void WriteListCompressed<T>(BitPacker packer, DisposableList<T> values)
        {
            T last = default;
            Packer<Size>.Write(packer, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var v = values[i];
                DeltaPacker<T>.Write(packer, last, v);
                last = v;
            }
        }

        private static DisposableList<T> ReadListCompressed<T>(BitPacker packer)
        {
            var count = Packer<Size>.Read(packer);
            var list = DisposableList<T>.Create(count);
            var last = default(T);

            for (var i = 0; i < count; i++)
            {
                T current = default;
                DeltaPacker<T>.Read(packer, last, ref current);
                last = current;
                list.Add(current);
            }

            return list;
        }

        [UsedByIL]
        public static void ReadOperation<T>(this BitPacker packer, ref DiffOp<T> value)
        {
            // dispose old value for new ones
            value.Dispose();

            var type = Packer<OperationType>.Read(packer);
            Size index = default;
            Size length = default;
            DisposableList<T> values;

            switch (type)
            {
                case OperationType.End:
                    value = DiffOp<T>.FinalOperation();
                    break;
                case OperationType.Delete:
                    Packer<Size>.Read(packer, ref index);
                    Packer<Size>.Read(packer, ref length);
                    value = new DiffOp<T>(type, (int)index.value, (int)length.value);
                    break;
                case OperationType.Insert:
                    Packer<Size>.Read(packer, ref index);
                    values = ReadListCompressed<T>(packer);
                    //Packer<DisposableList<T>>.Read(packer, ref values);
                    value = new DiffOp<T>(type, (int)index.value, values.Count, values);
                    break;
                case OperationType.Add:
                    // Packer<DisposableList<T>>.Read(packer, ref values);
                    values = ReadListCompressed<T>(packer);
                    value = new DiffOp<T>(type, 0, values.Count, values);
                    break;
                default: throw new ArgumentOutOfRangeException();
            }

        }

        [UsedByIL]
        public static bool DeltaWrite<T>(this BitPacker packer, DiffOp<T> old, DiffOp<T> newVal)
        {
            var scope = new DeltaWritingScope(packer);

            if (Packer.AreEqual(old, newVal))
                return scope.CompleteWithoutChanges();

            packer.WriteOperation<T>(newVal);
            return scope.CompleteWithChanges();
        }

        [UsedByIL]
        public static void DeltaRead<T>(this BitPacker packer, DiffOp<T> old, ref DiffOp<T> newVal)
        {
            if (!DeltaReadingScope.Continue(packer, old, ref newVal))
                return;

            packer.ReadOperation<T>(ref newVal);
        }
    }
}

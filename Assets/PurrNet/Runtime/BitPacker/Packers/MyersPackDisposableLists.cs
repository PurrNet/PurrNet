using System.Runtime.CompilerServices;
using PurrNet.Modules;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class MyersPackDisposableLists
    {
        [UsedByIL]
        public static bool WriteDisposableDeltaList<T>(BitPacker packer, DisposableList<T> old, DisposableList<T> value)
        {
            var scope = new DeltaWritingScope(packer);

            if (old.Equals(value))
                return scope.Complete();

            if (value.isDisposed)
            {
                scope.Write<bool>(false);
                return scope.Complete();
            }

            scope.Write<bool>(true);

            DisposableList<DiffOp<T>> changes;

            if (old.isDisposed)
            {
                using var tmp = DisposableList<T>.Create();
                changes = MyersDiff.Diff(tmp.rawList, value.rawList);
            }
            else changes = MyersDiff.Diff(old.rawList, value.rawList);

            if (changes.Count > 0)
            {
                int count = changes.Count;
                for (int i = 0; i < count; i++)
                    scope.Write<DiffOp<T>>(changes[i]);
            }

            scope.Write(DiffOp<T>.FinalOperation());

            var result = scope.Complete();

            for (int i = 0; i < changes.Count; i++)
                changes[i].values.Dispose();
            changes.Dispose();

            return result;
        }

        [UsedByIL]
        public static void ReadDisposableDeltaList<T>(BitPacker packer, DisposableList<T> old, ref DisposableList<T> value)
        {
            if (!DeltaReadingScope.ContinueDisposable(packer, old, ref value))
                return;

            if (!packer.ReadBit())
            {
                value.Dispose();
                return;
            }

            var changes = DisposableList<DiffOp<T>>.Create();
            try
            {
                long length = old.isDisposed ? (value.isDisposed ? 0 : value.Count) : old.Count;
                int peak = DeserializationLimits.ValidateCollectionLength<T>(length);
                long offset = 0;
                long decodedValues = 0;
                while (true)
                {
                    var operation = Packer<DiffOp<T>>.Read(packer);
                    if (operation.type == OperationType.End)
                    {
                        operation.Dispose();
                        break;
                    }
                    try
                    {
                        DeserializationLimits.ValidateCollectionLength<DiffOp<T>>((long)changes.Count + 1);
                        int added = operation.values.isDisposed ? 0 : operation.values.Count;
                        decodedValues += added;
                        DeserializationLimits.ValidateCollectionLength<T>(decodedValues);
                        DeserializationLimits.ValidateDeltaOperation<T>(operation.type, operation.index,
                            operation.length, added, ref length, ref offset);
                        if (length > peak)
                            peak = (int)length;
                        changes.Add(operation);
                    }
                    catch
                    {
                        operation.Dispose();
                        throw;
                    }
                }

                if (value.isDisposed)
                {
                    value = DisposableList<T>.Create(peak);
                }
                else if (!old.isDisposed && (old.rawList == value.rawList || value.rawList.Capacity < peak))
                {
                    if (old.rawList != value.rawList)
                        value.Dispose();
                    value = DisposableList<T>.Create(peak);
                }

                if (!old.isDisposed)
                {
                    value.Clear();
                    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                    {
                        for (int i = 0; i < old.Count; i++)
                            value.Add(PurrCopy<T>.Copy(old[i]));
                    }
                    else value.AddRange(old);
                }

                if (changes.Count > 0)
                    MyersDiff.Apply(value, changes);
            }
            finally
            {
                for (var i = 0; i < changes.Count; i++)
                    changes[i].Dispose();
                changes.Dispose();
            }
        }
    }
}

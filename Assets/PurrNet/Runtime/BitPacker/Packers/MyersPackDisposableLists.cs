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

            if (ListsEqualExact(old, value))
                return scope.Complete();

            if (value.isDisposed)
            {
                scope.Write<bool>(false);
                return scope.Complete();
            }

            scope.Write<bool>(true);

            var changes = default(DisposableList<DiffOp<T>>);
            try
            {
                if (old.isDisposed)
                {
                    using var tmp = DisposableList<T>.Create();
                    changes = MyersDiff.Diff(tmp, value);
                }
                else changes = MyersDiff.Diff(old, value);

                if (changes.Count > 0)
                {
                    int count = changes.Count;
                    for (int i = 0; i < count; i++)
                        scope.Write<DiffOp<T>>(changes[i]);
                }

                scope.Write(DiffOp<T>.FinalOperation());
                return scope.Complete();
            }
            finally
            {
                if (!changes.isDisposed)
                {
                    for (int i = 0; i < changes.Count; i++)
                        changes[i].Dispose();
                    changes.Dispose();
                }
            }
        }

        [UsedByIL]
        public static void ReadDisposableDeltaList<T>(BitPacker packer, DisposableList<T> old, ref DisposableList<T> value)
        {
            if (!packer.ReadBit())
            {
                bool aliasesOld = SharesBackingList(old, value);
                var copy = old.Duplicate();
                if (!value.isDisposed && !aliasesOld)
                    value.Dispose();
                value = copy;
                return;
            }

            if (!packer.ReadBit())
            {
                if (!value.isDisposed && !SharesBackingList(old, value))
                    value.Dispose();
                value = default;
                return;
            }

            if (value.isDisposed || (!old.isDisposed && old.list == value.list))
                value = DisposableList<T>.Create();
            else
                value.Clear();

            if (!old.isDisposed)
            {
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    for (int i = 0; i < old.Count; i++)
                        value.Add(PurrCopy<T>.Copy(old[i]));
                }
                else value.AddRange(old);
            }

            var changes = DisposableList<DiffOp<T>>.Create();
            try
            {
                while (true)
                {
                    var operation = default(DiffOp<T>);
                    try
                    {
                        Packer<DiffOp<T>>.Read(packer, ref operation);
                        if (operation.type == OperationType.End)
                            break;
                        changes.Add(operation);
                        operation = default;
                    }
                    finally
                    {
                        operation.Dispose();
                    }
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

        static bool ListsEqualExact<T>(DisposableList<T> left, DisposableList<T> right)
        {
            if (left.isDisposed && right.isDisposed)
                return true;
            if (left.isDisposed || right.isDisposed || left.Count != right.Count)
                return false;
            if (left.list == right.list)
                return true;

            for (int i = 0; i < left.Count; i++)
            {
                if (!PurrEquality<T>.Equals(left[i], right[i]))
                    return false;
            }
            return true;
        }

        static bool SharesBackingList<T>(DisposableList<T> left, DisposableList<T> right)
        {
            return !left.isDisposed && !right.isDisposed && left.list == right.list;
        }
    }
}

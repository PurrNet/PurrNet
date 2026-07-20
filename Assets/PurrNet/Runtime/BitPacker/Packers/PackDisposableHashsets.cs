using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class PackDisposableHashsets
    {
        public static bool WriteDisposableHashSetDelta<T>(BitPacker packer, DisposableHashSet<T> oldValue, DisposableHashSet<T> newValue)
        {
            var start = packer.AdvanceBits(1);

            bool hasChanged;

            PackedInt oldCount = oldValue.isDisposed ? -1 : oldValue.Count;
            PackedInt newCount = newValue.isDisposed ? -1 : newValue.Count;

            hasChanged = DeltaPacker<PackedInt>.Write(packer, oldCount, newCount);

            if (newCount > 0)
            {
                DisposableList<T> oldItemsList = default;
                using var newItemsList = DisposableList<T>.Create(newCount.value);

                if (oldCount.value >= 0)
                {
                    oldItemsList = DisposableList<T>.Create(oldCount.value);
                    foreach (var item in oldValue)
                        oldItemsList.Add(item);
                }

                foreach (var item in newValue)
                    newItemsList.Add(item);

                hasChanged = DeltaPacker<DisposableList<T>>.Write(packer, oldItemsList, newItemsList) || hasChanged;

                oldItemsList.Dispose();
            }

            packer.WriteAt(start, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(start + 1);

            return hasChanged;
        }

        public static void ReadDisposableHashSetDelta<T>(BitPacker packer, DisposableHashSet<T> oldValue, ref DisposableHashSet<T> value)
        {
            bool hasChanged = default;
            packer.Read(ref hasChanged);

            if (!hasChanged)
            {
                if (oldValue.isDisposed)
                {
                    value.Dispose();
                    return;
                }

                if (value.isDisposed)
                    value = DisposableHashSet<T>.Create();
                else value.Clear();

                foreach (var item in oldValue)
                    value.Add(item);

                return;
            }

            PackedInt oldCount = oldValue.isDisposed ? -1 : oldValue.Count;
            PackedInt newCount = default;

            DeltaPacker<PackedInt>.Read(packer, oldCount, ref newCount);

            if (newCount.value < 0)
            {
                if (!value.isDisposed)
                    value.Dispose();
                return;
            }

            if (value.isDisposed || value.set == null)
                value = DisposableHashSet<T>.Create();
            else value.Clear();

            if (newCount.value == 0)
                return;

            DisposableList<T> oldItemsList = default;

            if (oldCount.value >= 0)
            {
                oldItemsList = DisposableList<T>.Create(oldCount.value);
                foreach (var item in oldValue)
                    oldItemsList.Add(item);
            }

            var itemsList = DisposableList<T>.Create(newCount.value);

            DeltaPacker<DisposableList<T>>.Read(packer, oldItemsList, ref itemsList);

            for (int i = 0; i < itemsList.Count; i++)
                value.Add(itemsList[i]);

            oldItemsList.Dispose();
            itemsList.Dispose();
        }
    }
}

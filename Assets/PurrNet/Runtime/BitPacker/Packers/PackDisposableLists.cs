using PurrNet.Modules;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class PackDisposableLists
    {
        [UsedByIL]
        public static void WriteDisposableList<T>(this BitPacker packer, DisposableList<T> value)
        {
            if (value.isDisposed || value.list == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            uint length = (uint)value.Count;
            Packer<Size>.Write(packer, length);

            for (int i = 0; i < length; i++)
                Packer<T>.Write(packer, value[i]);
        }

        [UsedByIL]
        public static void ReadDisposableList<T>(this BitPacker packer, ref DisposableList<T> value)
        {
            value.Dispose();

            bool hasValue = default;

            packer.Read(ref hasValue);

            if (!hasValue)
                return;

            Size length = default;
            Packer<Size>.Read(packer, ref length);
            value = DisposableList<T>.Create(length);

            for (int i = 0; i < length; i++)
            {
                T item = default;
                Packer<T>.Read(packer, ref item);
                value.Add(item);
            }
        }

        [UsedByIL]
        public static bool WriteDisposableDeltaList<T>(this BitPacker packer, DisposableList<T> old, DisposableList<T> value)
        {
            var start = packer.AdvanceBits(1);

            bool hasChanged;

            PackedInt oldCount = old.isDisposed ? -1 : old.Count;
            PackedInt newCount = value.isDisposed ? -1 : value.Count;

            hasChanged = DeltaPacker<PackedInt>.Write(packer, oldCount, newCount);

            T oldNewValue = default;

            if (newCount > 0)
            {
                for (int i = 0; i < newCount.value; i++)
                {
                    bool isNewIndex = i >= oldCount.value;
                    var oldValue = isNewIndex ? oldNewValue : old[i];

                    var newValue = value[i];
                    hasChanged = DeltaPacker<T>.Write(packer, oldValue, newValue) || hasChanged;
                    oldNewValue = newValue;
                }
            }

            packer.WriteAt(start, hasChanged);

            if (!hasChanged)
                packer.SetBitPosition(start + 1);

            return hasChanged;
        }

        [UsedByIL]
        public static void ReadDisposableDeltaList<T>(this BitPacker packer, DisposableList<T> old, ref DisposableList<T> value)
        {
            bool hasChanged = packer.ReadBits(1) == 1;

            if (!hasChanged)
            {
                value.Dispose();
                value = Packer.Copy(old);
                return;
            }

            PackedInt count = default;
            PackedInt oldCount = old.isDisposed ? -1 : old.Count;

            DeltaPacker<PackedInt>.Read(packer, oldCount, ref count);

            if (count < 0)
            {
                value.Dispose();
                return;
            }

            if (value.isDisposed)
                value = DisposableList<T>.Create(count.value);
            else value.Clear();

            T oldNewValue = default;

            for (int i = 0; i < count; i++)
            {
                bool isNewIndex = i >= oldCount;
                var oldValue = isNewIndex ? oldNewValue : old[i];
                T newValue = default;
                DeltaPacker<T>.Read(packer, oldValue, ref newValue);
                oldNewValue = newValue;
                value.Add(newValue);
            }
        }
    }
}

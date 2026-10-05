using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace PurrNet.Packing
{
    /// <summary>
    /// Limits expansion of collection data during deserialization. Configure these before receiving
    /// messages when a game needs larger collections.
    /// </summary>
    public static class DeserializationLimits
    {
        private static int _maxCollectionLength = 16 * 1024 * 1024;
        private static long _maxCollectionStorageBytes = 16L * 1024 * 1024;

        /// <summary>Maximum decoded entries in a collection, including compressed and RLE data.</summary>
        public static int maxCollectionLength
        {
            get => _maxCollectionLength;
            set => _maxCollectionLength = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>
        /// Maximum estimated element storage for one decoded collection. This counts inline values
        /// or reference slots, not referenced objects, container overhead, or the entire message.
        /// </summary>
        public static long maxCollectionStorageBytes
        {
            get => _maxCollectionStorageBytes;
            set => _maxCollectionStorageBytes = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        internal static int ValidateCollectionLength<T>(long length)
        {
            if (length < 0 || length > maxCollectionLength ||
                length > maxCollectionStorageBytes / ElementStorage<T>.size)
                throw new SerializationException($"Invalid decoded collection length {length} for {typeof(T)}. " +
                    $"Limits are {maxCollectionLength} entries and {maxCollectionStorageBytes} bytes of element storage.");
            return (int)length;
        }

        internal static int ClampCapacity(BitPacker packer, int length)
        {
            return Math.Min(length, packer.remainingBits);
        }

        internal static int ValidateByteLength(BitPacker packer, long length)
        {
            if (length < 0 || length > packer.remainingBits / 8)
                throw new SerializationException($"Declared byte length {length} exceeds the remaining message data.");
            return (int)length;
        }

        internal static int ValidateBitLength(BitPacker packer, uint length)
        {
            if (length > packer.remainingBits)
                throw new SerializationException($"Declared bit length {length} exceeds the remaining message data.");
            return (int)length;
        }

        internal static int ValidateIndex(uint index)
        {
            if (index > int.MaxValue)
                throw new SerializationException($"Invalid collection index {index}.");
            return (int)index;
        }

        internal static void ValidateDeltaOperation<T>(OperationType type, int index, int deleted,
            int added, ref long length, ref long offset)
        {
            long effectiveIndex = (long)index + offset;
            switch (type)
            {
                case OperationType.Add:
                    length += added;
                    offset += added;
                    break;
                case OperationType.Insert:
                    if (effectiveIndex < 0 || effectiveIndex > length)
                        throw new SerializationException("Delta insertion index is outside the collection.");
                    length += added;
                    offset += added;
                    break;
                case OperationType.Delete:
                    if (effectiveIndex < 0 || effectiveIndex > length || deleted < 0 || deleted > length - effectiveIndex)
                        throw new SerializationException("Delta deletion range is outside the collection.");
                    length -= deleted;
                    offset -= deleted;
                    break;
                default:
                    throw new SerializationException("Invalid collection delta operation.");
            }
            ValidateCollectionLength<T>(length);
            if (offset < int.MinValue || offset > int.MaxValue)
                throw new SerializationException("Collection delta offset overflow.");
        }

        private static class ElementStorage<T>
        {
            internal static readonly int size = Math.Max(1, Unsafe.SizeOf<T>());
        }
    }
}

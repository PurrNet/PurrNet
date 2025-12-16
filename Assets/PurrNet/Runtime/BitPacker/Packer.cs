using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;
using Object = UnityEngine.Object;

namespace PurrNet.Packing
{
    public delegate bool DeltaWriteFunc<in T>(BitPacker packer, T oldValue, T newValue);

    public delegate void DeltaReadFunc<T>(BitPacker packer, T oldValue, ref T value);

    public delegate void WriteFunc<in T>(BitPacker packer, T value);

    public delegate void ReadFunc<T>(BitPacker packer, ref T value);

    public static class Packer<T>
    {
        static WriteFunc<T> _write;
        static WriteFunc<T> _writeWrapper;
        static ReadFunc<T> _read;
        static ReadFunc<T> _readWrapper;


        public static void RegisterWriter(WriteFunc<T> a)
        {
            if (_write != null)
                return;

            _write = a;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;

            if (!isStructOrSealed)
                _writeWrapper = WriteClass;
            else _writeWrapper = WriteAsExactType;

            Packer.RegisterWriter(typeof(T), _write.Method, _writeWrapper.Method);
        }

        public static bool HasPacker()
        {
            return _write != null;
        }

        public static void RegisterReader(ReadFunc<T> b)
        {
            Hasher.PrepareType(typeof(T));

            if (_read != null)
                return;

            _read = b;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;

            if (!isStructOrSealed)
                _readWrapper = ReadClass;
            else _readWrapper = ReadAsExactType;

            Packer.RegisterReader(typeof(T), _read.Method, _readWrapper.Method);
        }

        [UsedByIL]
        public static void WriteAsExactType(BitPacker packer, T value)
        {
            try
            {
                if (_write == null)
                {
                    Packer.FallbackWriter(packer, value);
                    return;
                }

                _write(packer, value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        [UsedByIL]
        public static void ReadAsExactType(BitPacker packer, ref T value)
        {
            try
            {
                if (_read == null)
                {
                    Packer.FallbackReader(packer, ref value);
                    return;
                }

                _read(packer, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        [UsedByIL]
        public static void Write(BitPacker packer, T value)
        {
            try
            {
                if (_writeWrapper == null)
                {
                    Packer.FallbackWriter(packer, value);
                    return;
                }

                _writeWrapper(packer, value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        [UsedByIL]
        public static void Read(BitPacker packer, ref T value)
        {
            try
            {
                if (_readWrapper == null)
                {
                    Packer.FallbackReader(packer, ref value);
                    return;
                }

                _readWrapper(packer, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        static void WriteClass(BitPacker packer, T value)
        {
            Type type;

            if (value == null)
            {
                type = typeof(T);
            }
            else
            {
                var vtype = value.GetType();
                type = Hasher.IsRegistered(vtype) ? vtype : typeof(T);
            }

            bool isTypeSameAsGeneric = type == typeof(T);

            Packer<bool>.WriteAsExactType(packer, isTypeSameAsGeneric);

            if (isTypeSameAsGeneric)
            {
                WriteAsExactType(packer, value);
                return;
            }

            Packer<PackedUInt>.WriteAsExactType(packer, Hasher.GetStableHashU32(type));
            Packer.WriteAsExactType(packer, type, value);
        }

        static void ReadClass(BitPacker packer, ref T value)
        {
            bool isTypeSameAsGeneric = Packer<bool>.Read(packer);

            if (isTypeSameAsGeneric)
            {
                ReadAsExactType(packer, ref value);
                return;
            }

            var hash = Packer<PackedUInt>.Read(packer);

            if (!Hasher.TryGetType(hash, out var type))
                throw new Exception($"Type with hash '{hash}' not found.");

            object result = value;
            Packer.ReadAsExactType(packer, type, ref result);

            switch (result)
            {
                case null:
                    value = default;
                    break;
                case T cast:
                    value = cast;
                    break;
                default:
                    PurrLogger.LogError($"While reading `{type}`, we got `{result.GetType()}` which does not match expected type `{typeof(T)}`.");
                    value = default;
                    break;
            }
        }

        public static T Read(BitPacker packer)
        {
            var value = default(T);
            Read(packer, ref value);
            return value;
        }

        public static void Serialize(BitPacker packer, ref T value)
        {
            if (packer.isWriting)
                Write(packer, value);
            else Read(packer, ref value);
        }
    }

    public static class Packer
    {
        public static T Copy<T>(T value)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return value;

            if (value is IDuplicate<T> duplicate)
                return duplicate.Duplicate();

            using var tmpPacker = BitPackerPool.Get();
            Packer<T>.Write(tmpPacker, value);
            tmpPacker.ResetPositionAndMode(true);
            var copy = default(T);
            Packer<T>.Read(tmpPacker, ref copy);
            return copy;
        }

        /// <summary>
        /// Modifies `target` to become `whatToCopy` without re-creating it.
        /// </summary>
        /// <returns>
        /// If a modification happened
        /// </returns>
        public static bool Transform<T>(ref T target, T whatToCopy)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                bool equal = EqualityComparer<T>.Default.Equals(target, whatToCopy);
                if (equal)
                    return false;
                target = whatToCopy;
                return true;
            }

            if (target is IEquatable<T> comparable && comparable.Equals(whatToCopy))
                return false;

            using var packerA = BitPackerPool.Get();
            using var packerB = BitPackerPool.Get();

            Packer<T>.Write(packerA, target);
            Packer<T>.Write(packerB, whatToCopy);

            if (ArePackersEqual(packerA, packerB))
                return false;

            packerB.ResetPositionAndMode(true);
            Packer<T>.Read(packerB, ref target);
            return true;
        }

        [UsedByIL]
        public static bool ArePackersEqual(BitPacker packerA, BitPacker packerB)
        {
            if (packerA.positionInBits != packerB.positionInBits)
                return false;

            int bits = packerA.positionInBits;

            packerA.ResetPositionAndMode(true);
            packerB.ResetPositionAndMode(true);

            while (bits >= 64)
            {
                ulong aBits = packerA.ReadBits(64);
                ulong bBits = packerB.ReadBits(64);

                if (aBits != bBits)
                {
                    packerA.SetBitPosition(bits);
                    packerB.SetBitPosition(bits);
                    return false;
                }

                bits -= 64;
            }

            if (bits > 0)
            {
                var remainingBits = (byte)bits;
                ulong aBits = packerA.ReadBits(remainingBits);
                ulong bBits = packerB.ReadBits(remainingBits);
                if (aBits != bBits)
                {
                    packerA.SetBitPosition(bits);
                    packerB.SetBitPosition(bits);
                    return false;
                }
            }

            packerA.SetBitPosition(bits);
            packerB.SetBitPosition(bits);
            return true;
        }

        [UsedByIL]
        public static bool AreEqual<T>(T a, T b)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return EqualityComparer<T>.Default.Equals(a, b);

            if (a is IEquatable<T> comparable)
                return comparable.Equals(b);

            using var packerA = BitPackerPool.Get();
            using var packerB = BitPackerPool.Get();

            Packer<T>.Write(packerA, a);
            Packer<T>.Write(packerB, b);

            if (packerA.positionInBits != packerB.positionInBits)
                return false;

            int bits = packerA.positionInBits;

            packerA.ResetPositionAndMode(true);
            packerB.ResetPositionAndMode(true);

            while (bits >= 64)
            {
                ulong aBits = packerA.ReadBits(64);
                ulong bBits = packerB.ReadBits(64);

                if (aBits != bBits)
                    return false;

                bits -= 64;
            }

            if (bits > 0)
            {
                var remainingBits = (byte)bits;
                ulong aBits = packerA.ReadBits(remainingBits);
                ulong bBits = packerB.ReadBits(remainingBits);
                if (aBits != bBits)
                    return false;
            }

            return true;
        }

        [UsedByIL]
        public static bool AreEqualRef<T>(ref T a, ref T b)
        {
            return AreEqual(a, b);
        }

        static readonly Dictionary<Type, MethodInfo> _writeExactMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _writeWrappedMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readExactMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readWrappedMethods = new Dictionary<Type, MethodInfo>();

        public static void RegisterWriter(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            _writeWrappedMethods.TryAdd(type, wrapper);
            _writeExactMethods.TryAdd(type, exact);
        }

        public static void RegisterReader(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            _readWrappedMethods.TryAdd(type, wrapper);
            _readExactMethods.TryAdd(type, exact);
        }

        public static void FallbackWriter<T>(BitPacker packer, T value)
        {
            try
            {
                bool hasValue = value != null;
                Packer<bool>.Write(packer, hasValue);

                if (!hasValue) return;

                object obj = value;

                if (obj is Object unityObj)
                {
                    if (WriteAsNetworkAsset(packer, unityObj))
                        return;
                }
                else Packer<bool>.Write(packer, false);

                PackedUInt typeHash = Hasher.GetStableHashU32(obj.GetType());
                Packer<PackedUInt>.Write(packer, typeHash);
                WriteRawObject(obj, packer);
            }
            catch (Exception e)
            {
                PurrLogger.LogError(
                    $"Failed to write value of type '{typeof(T)}' when using fallback writer.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static bool WriteAsNetworkAsset(BitPacker packer, Object unityObj)
        {
            var nassets = NetworkManager.main.networkAssets;
            int index = nassets && unityObj ? nassets.GetIndex(unityObj) : -1;
            bool isNetworkAsset = index != -1;
            Packer<bool>.Write(packer, isNetworkAsset);

            if (isNetworkAsset)
            {
                Packer<PackedInt>.Write(packer, index);
                return true;
            }

            return false;
        }

        public static void FallbackReader<T>(BitPacker packer, ref T value)
        {
            try
            {
                bool hasValue = default;
                Packer<bool>.Read(packer, ref hasValue);

                if (!hasValue)
                {
                    value = default;
                    return;
                }

                if (ReadAsNetworkAsset(packer, ref value))
                    return;

                var typeHash = Packer<PackedUInt>.Read(packer);
                var type = Hasher.ResolveType(typeHash);

                object obj = null;
                ReadRawObject(type, packer, ref obj);

                if (obj is T entity)
                    value = entity;
                else value = default;
            }
            catch (Exception e)
            {
                PurrLogger.LogError(
                    $"Failed to read value of type '{typeof(T)}' when using fallback reader.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static bool ReadAsNetworkAsset<T>(BitPacker packer, ref T value)
        {
            bool isNetworkAsset = Packer<bool>.Read(packer);

            if (isNetworkAsset && NetworkManager.main && NetworkManager.main.networkAssets)
            {
                int index = Packer<PackedInt>.Read(packer);
                value = NetworkManager.main.networkAssets.GetAsset(index) is T cast ? cast : default;
                return true;
            }

            return false;
        }

        public static void WriteAsExactType<T>(BitPacker packer, Type type, T value)
        {
            if (!_writeExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Write(BitPacker packer, Type type, object value)
        {
            if (!_writeWrappedMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Write(BitPacker packer, object value)
        {
            var type = value.GetType();

            if (!_writeWrappedMethods.TryGetValue(type, out var method))
            {
                FallbackWriter(packer, value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        static void WriteRawObject(object value, BitPacker packer)
        {
            var type = value.GetType();

            if (!_writeExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Read(BitPacker packer, Type type, ref object value)
        {
            if (!_readWrappedMethods.TryGetValue(type, out var method))
            {
                FallbackReader(packer, ref value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError(e.InnerException != null
                    ? $"Failed to read value of type '{type}'.\n{e.InnerException.Message}\n{e.InnerException.StackTrace}"
                    : $"Failed to read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void ReadAsExactType(BitPacker packer, Type type, ref object value)
        {
            if (!_readExactMethods.TryGetValue(type, out var method))
            {
                FallbackReader(packer, ref value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError(e.InnerException != null
                    ? $"Failed to read value of type '{type}'.\n{e.InnerException.Message}\n{e.InnerException.StackTrace}"
                    : $"Failed to read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void ReadRawObject(Type type, BitPacker packer, ref object value)
        {
            if (!_readExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No reader for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Serialize(BitPacker packer, Type type, ref object value)
        {
            if (packer.isWriting)
                Write(packer, value);
            else Read(packer, type, ref value);
        }
    }
}

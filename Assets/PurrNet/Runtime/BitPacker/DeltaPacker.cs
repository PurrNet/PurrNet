using System;
using System.Collections.Concurrent;
using System.Reflection;
using PurrNet.Logging;

namespace PurrNet.Packing
{
    public static class DeltaPacker
    {
        interface IRuntimeDeltaWriter
        {
            bool Invoke(BitPacker packer, object oldValue, object newValue);
        }

        interface IRuntimeDeltaReader
        {
            void Invoke(BitPacker packer, object oldValue, ref object newValue);
        }

        sealed class RuntimeDeltaWriter<T> : IRuntimeDeltaWriter
        {
            readonly DeltaWriteFunc<T> _writer;

            public RuntimeDeltaWriter(DeltaWriteFunc<T> writer)
            {
                _writer = writer;
            }

            public bool Invoke(BitPacker packer, object oldValue, object newValue)
            {
                return _writer(packer, Cast<T>(oldValue), Cast<T>(newValue));
            }
        }

        sealed class RuntimeDeltaReader<T> : IRuntimeDeltaReader
        {
            readonly DeltaReadFunc<T> _reader;

            public RuntimeDeltaReader(DeltaReadFunc<T> reader)
            {
                _reader = reader;
            }

            public void Invoke(BitPacker packer, object oldValue, ref object newValue)
            {
                T value = Cast<T>(newValue);
                _reader(packer, Cast<T>(oldValue), ref value);
                newValue = value;
            }
        }

        sealed class ReflectionDeltaWriter : IRuntimeDeltaWriter
        {
            readonly Type _type;
            readonly MethodInfo _method;

            public ReflectionDeltaWriter(Type type, MethodInfo method)
            {
                _type = type;
                _method = method;
            }

            public bool Invoke(BitPacker packer, object oldValue, object newValue)
            {
                var args = new[] { (object)packer, oldValue, newValue };
                var result = _method.Invoke(null, args);
                if (result is bool changed)
                    return changed;

                PurrLogger.LogError($"Delta writer for type '{_type}' did not return a boolean value.");
                return false;
            }
        }

        sealed class ReflectionDeltaReader : IRuntimeDeltaReader
        {
            readonly MethodInfo _method;

            public ReflectionDeltaReader(MethodInfo method)
            {
                _method = method;
            }

            public void Invoke(BitPacker packer, object oldValue, ref object newValue)
            {
                var args = new[] { (object)packer, oldValue, newValue };
                _method.Invoke(null, args);
                newValue = args[2];
            }
        }

        static readonly ConcurrentDictionary<Type, IRuntimeDeltaWriter> _writeExactMethods = new ConcurrentDictionary<Type, IRuntimeDeltaWriter>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaWriter> _writeWrappedMethods = new ConcurrentDictionary<Type, IRuntimeDeltaWriter>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaReader> _readExactMethods = new ConcurrentDictionary<Type, IRuntimeDeltaReader>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaReader> _readWrappedMethods = new ConcurrentDictionary<Type, IRuntimeDeltaReader>();

        static T Cast<T>(object value)
        {
            return value == null ? default : (T)value;
        }

        public static void RegisterWriter(Type type, MethodInfo method)
        {
            RegisterWriter(type, method, method);
        }

        public static void RegisterWriter(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            var exactInvoker = new ReflectionDeltaWriter(type, exact);
            var wrappedInvoker = exact == wrapper ? exactInvoker : new ReflectionDeltaWriter(type, wrapper);
            _writeExactMethods.TryAdd(type, exactInvoker);
            _writeWrappedMethods.TryAdd(type, wrappedInvoker);
        }

        internal static void RegisterWriter<T>(DeltaWriteFunc<T> exact, DeltaWriteFunc<T> wrapper)
        {
            var exactInvoker = new RuntimeDeltaWriter<T>(exact);
            var wrappedInvoker = ReferenceEquals(exact, wrapper)
                ? exactInvoker
                : new RuntimeDeltaWriter<T>(wrapper);
            _writeExactMethods.TryAdd(typeof(T), exactInvoker);
            _writeWrappedMethods.TryAdd(typeof(T), wrappedInvoker);
        }

        public static void RegisterReader(Type type, MethodInfo method)
        {
            RegisterReader(type, method, method);
        }

        public static void RegisterReader(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            var exactInvoker = new ReflectionDeltaReader(exact);
            var wrappedInvoker = exact == wrapper ? exactInvoker : new ReflectionDeltaReader(wrapper);
            _readExactMethods.TryAdd(type, exactInvoker);
            _readWrappedMethods.TryAdd(type, wrappedInvoker);
        }

        internal static void RegisterReader<T>(DeltaReadFunc<T> exact, DeltaReadFunc<T> wrapper)
        {
            var exactInvoker = new RuntimeDeltaReader<T>(exact);
            var wrappedInvoker = ReferenceEquals(exact, wrapper)
                ? exactInvoker
                : new RuntimeDeltaReader<T>(wrapper);
            _readExactMethods.TryAdd(typeof(T), exactInvoker);
            _readWrappedMethods.TryAdd(typeof(T), wrappedInvoker);
        }

        static readonly ConcurrentDictionary<Type, IRuntimeDeltaWriter> _writeFallbackMethods = new ConcurrentDictionary<Type, IRuntimeDeltaWriter>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaReader> _readFallbackMethods = new ConcurrentDictionary<Type, IRuntimeDeltaReader>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaWriter> _writeExactFallbackMethods = new ConcurrentDictionary<Type, IRuntimeDeltaWriter>();
        static readonly ConcurrentDictionary<Type, IRuntimeDeltaReader> _readExactFallbackMethods = new ConcurrentDictionary<Type, IRuntimeDeltaReader>();

        static bool WriteUnpacked(BitPacker packer, Type type, object oldValue, object newValue)
        {
            var writer = _writeFallbackMethods.GetOrAdd(type, static valueType =>
                new ReflectionDeltaWriter(valueType,
                    typeof(DeltaPacker<>).MakeGenericType(valueType).GetMethod(
                        nameof(DeltaPacker<int>.WriteUnpacked), BindingFlags.Public | BindingFlags.Static)));
            return InvokeWriter(packer, type, oldValue, newValue, writer);
        }

        static void ReadUnpacked(BitPacker packer, Type type, object oldValue, ref object value)
        {
            var reader = _readFallbackMethods.GetOrAdd(type, static valueType =>
                new ReflectionDeltaReader(
                    typeof(DeltaPacker<>).MakeGenericType(valueType).GetMethod(
                        nameof(DeltaPacker<int>.ReadUnpacked), BindingFlags.Public | BindingFlags.Static)));
            InvokeReader(packer, type, oldValue, ref value, reader);
        }

        public static bool Write(BitPacker packer, Type type, object oldValue, object newValue)
        {
            if (!_writeWrappedMethods.TryGetValue(type, out var writer))
                return WriteUnpacked(packer, type, oldValue, newValue);

            return InvokeWriter(packer, type, oldValue, newValue, writer);
        }

        public static bool WriteAsExactType(BitPacker packer, Type type, object oldValue, object newValue)
        {
            if (!_writeExactMethods.TryGetValue(type, out var writer))
                return WriteUnpackedExact(packer, type, oldValue, newValue);

            return InvokeWriter(packer, type, oldValue, newValue, writer);
        }

        static bool InvokeWriter(BitPacker packer, Type type, object oldValue, object newValue, IRuntimeDeltaWriter writer)
        {
            try
            {
                return writer.Invoke(packer, oldValue, newValue);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        public static void Read(BitPacker packer, Type type, object oldValue, ref object newValue)
        {
            if (!_readWrappedMethods.TryGetValue(type, out var reader))
            {
                ReadUnpacked(packer, type, oldValue, ref newValue);
                return;
            }

            InvokeReader(packer, type, oldValue, ref newValue, reader);
        }

        public static void ReadAsExactType(BitPacker packer, Type type, object oldValue, ref object newValue)
        {
            if (!_readExactMethods.TryGetValue(type, out var reader))
            {
                ReadUnpackedExact(packer, type, oldValue, ref newValue);
                return;
            }

            InvokeReader(packer, type, oldValue, ref newValue, reader);
        }

        static void InvokeReader(BitPacker packer, Type type, object oldValue, ref object newValue, IRuntimeDeltaReader reader)
        {
            try
            {
                reader.Invoke(packer, oldValue, ref newValue);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        static bool WriteUnpackedExact(BitPacker packer, Type type, object oldValue, object newValue)
        {
            var writer = _writeExactFallbackMethods.GetOrAdd(type, static valueType =>
                new ReflectionDeltaWriter(valueType,
                    typeof(DeltaPacker<>).MakeGenericType(valueType).GetMethod(
                        nameof(DeltaPacker<int>.WriteUnpackedAsExactType), BindingFlags.Public | BindingFlags.Static)));
            return InvokeWriter(packer, type, oldValue, newValue, writer);
        }

        static void ReadUnpackedExact(BitPacker packer, Type type, object oldValue, ref object value)
        {
            var reader = _readExactFallbackMethods.GetOrAdd(type, static valueType =>
                new ReflectionDeltaReader(
                    typeof(DeltaPacker<>).MakeGenericType(valueType).GetMethod(
                        nameof(DeltaPacker<int>.ReadUnpackedAsExactType), BindingFlags.Public | BindingFlags.Static)));
            InvokeReader(packer, type, oldValue, ref value, reader);
        }

        /// <summary>Fallback for types with no registered delta: write one "changed" bit then full value. Avoids delegating to DeltaPacker&lt;object&gt; which would recurse (stack overflow) for string and other unregistered types.</summary>
        public static bool FallbackWriter<T>(BitPacker packer, T oldValue, T value)
        {
            if (Packer.AreEqual(oldValue, value))
            {
                packer.WriteBit(false);
                return false;
            }
            packer.WriteBit(true);
            Packer<T>.Write(packer, value);
            return true;
        }

        /// <summary>Fallback for types with no registered delta: read one "changed" bit then full value or copy of old.</summary>
        public static void FallbackReader<T>(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                if (value is IDisposable disposable && !ReferenceEquals(value, oldValue))
                    disposable.Dispose();
                value = Packer.Copy(oldValue);
                return;
            }
            Packer<T>.Read(packer, ref value);
        }
    }
}

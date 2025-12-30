using System;
using System.Collections.Generic;
using System.Reflection;
using PurrNet.Logging;

namespace PurrNet.Packing
{
    public static class DeltaPacker
    {
        static readonly Dictionary<Type, MethodInfo> _writeMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readMethods = new Dictionary<Type, MethodInfo>();

        public static bool ShouldBeDeltaPacked(Type type)
        {
            return type.IsAssignableFrom(typeof(DeltaPacker));
        }

        public static void RegisterWriter(Type type, MethodInfo method)
        {
            _writeMethods.TryAdd(type, method);
        }

        public static void RegisterReader(Type type, MethodInfo method)
        {
            _readMethods.TryAdd(type, method);
        }

        static readonly object[] _args = new object[3];

        static bool WriteUnpacked(BitPacker packer, Type type, object oldValue, object newValue)
        {
            if (Packer.AreEqual(oldValue, newValue))
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            Packer.Write(packer, type, newValue);
            return true;
        }

        static void ReadUnpacked(BitPacker packer, Type type, object oldValue, ref object value)
        {
            if (!packer.ReadBit())
            {
                value = Packer.Copy(oldValue);
                return;
            }

            Packer.Read(packer, type, ref value);
        }

        public static bool Write(BitPacker packer, Type type, object oldValue, object newValue)
        {
            if (!_writeMethods.TryGetValue(type, out var method))
                return WriteUnpacked(packer, type, oldValue, newValue);

            try
            {
                _args[0] = packer;
                _args[1] = oldValue;
                _args[2] = newValue;
                var res = method.Invoke(null, _args);
                if (res is bool result)
                {
                    return result;
                }

                PurrLogger.LogError($"Delta writer for type '{type}' did not return a boolean value.");
                return false;
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        public static void Read(BitPacker packer, Type type, object oldValue, ref object newValue)
        {
            if (!_readMethods.TryGetValue(type, out var method))
            {
                ReadUnpacked(packer, type, oldValue, ref newValue);
                return;
            }

            try
            {
                _args[0] = packer;
                _args[1] = oldValue;
                _args[2] = newValue;
                method.Invoke(null, _args);
                newValue = _args[2];
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to delta read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static bool FallbackWriter<T>(BitPacker packer, T oldValue, T value)
        {
            return DeltaPacker<object>.Write(packer, oldValue, value);
        }

        public static void FallbackReader<T>(BitPacker packer, T oldValue, ref T value)
        {
            object newValue = value;
            DeltaPacker<object>.Read(packer, oldValue, ref newValue);
            value = (T)newValue;
        }
    }
}

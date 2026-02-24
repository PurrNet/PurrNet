using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;

namespace PurrNet.Utils
{
    public static class HasherBoilerplate
    {
        [RegisterPackers]
        static void Register()
        {
            Hasher.PrepareType(typeof(object));
        }
    }

    public static class Hasher<T>
    {
        // ReSharper disable once StaticMemberInGenericType
        public static readonly uint stableHash;

        static Hasher()
        {
            stableHash = Hasher.ActualHash(typeof(T).FullName);
        }
    }

    public class Hasher
    {
        private const uint FNV_offset_basis32 = 2166136261;
        private const uint FNV_prime32 = 16777619;

        static readonly Dictionary<Type, uint> _hashes = new Dictionary<Type, uint>();
        static readonly Dictionary<uint, Type> _decoder = new Dictionary<uint, Type>();

        static uint _hashCounter = 1;

        public static uint hashCounter => _hashCounter;

        public static uint ActualHash(string txt)
        {
            unchecked
            {
                uint hash = FNV_offset_basis32;
                for (int i = 0; i < txt.Length; i++)
                {
                    uint ch = txt[i];
                    hash *= FNV_prime32;
                    hash ^= ch;
                }

                return hash;
            }
        }

        public static Type ResolveType(uint hash)
        {
            if (_decoder.TryGetValue(hash, out var type))
                return type;

            throw new InvalidOperationException(
                PurrLogger.FormatMessage($"Type with hash '{hash}' not found.")
            );
        }

        public static bool TryGetType(uint hash, out Type type)
        {
            return _decoder.TryGetValue(hash, out type);
        }

        public static uint Load(Type type, uint hash)
        {
            _hashes[type] = hash;
            _decoder[hash] = type;
            return hash;
        }

        [UsedImplicitly]
        public static uint PrepareType(Type type)
        {
            if (_hashes.TryGetValue(type, out var hash))
                return hash;

            hash = _hashCounter++;
            _hashes[type] = hash;
            _decoder[hash] = type;

            return hash;
        }

        [UsedByIL]
        public static void PrepareType<T>() => PrepareType(typeof(T));

        public static bool IsRegistered(Type type)
        {
            return _hashes.ContainsKey(type);
        }

        public static uint GetStableHashU32(Type type)
        {
            if (type == null)
                return 0;

            if (_hashCounter == 1)
                throw new InvalidOperationException($"Hasher hasn't been initialized yet when trying to get hash for type '{type.FullName}'.");

            return _hashes.TryGetValue(type, out var hash)
                ? hash
                : throw new InvalidOperationException(
                    PurrLogger.FormatMessage($"Type '{type.FullName}' is not registered.")
                );
        }

        public static uint GetStableHashU32WithInstance<T>(T obj)
        {
            if (obj != null)
                return GetStableHashU32(obj.GetType());
            return GetStableHashU32(typeof(T));
        }

        public static uint GetStableHashU32<T>()
        {
            return GetStableHashU32(typeof(T));
        }

        public static string GetAllHashesAsText()
        {
            var builder = new StringBuilder();

            foreach (var pair in _hashes)
            {
                builder.Append(pair.Key.AssemblyQualifiedName);
                builder.Append(";");
                builder.Append(pair.Value);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        public static uint CombineHashes(uint hash1, uint hash2)
        {
            unchecked
            {
                uint hash = FNV_offset_basis32;
                hash ^= hash1;
                hash *= FNV_prime32;
                hash ^= hash2;
                hash *= FNV_prime32;
                return hash;
            }
        }

        public static void ClearState()
        {
            _hashes.Clear();
            _decoder.Clear();
            _hashCounter = 1;
        }

        public static void FinishLoad(int linesLength)
        {
            _hashCounter += (uint)linesLength;
        }
    }
}

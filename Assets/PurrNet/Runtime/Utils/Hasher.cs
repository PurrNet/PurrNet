using System;
using System.Collections.Generic;
using System.Reflection;
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
        public static readonly uint stableHash = Hasher.Hash(typeof(T));
    }

    public class Hasher
    {
        private const uint FNV_offset_basis32 = 2166136261;
        private const uint FNV_prime32 = 16777619;

        static readonly Dictionary<Type, uint> _hashes = new Dictionary<Type, uint>();
        static readonly Dictionary<uint, Type> _decoder = new Dictionary<uint, Type>();
        static readonly Dictionary<uint, Type> _bruteForceCache = new Dictionary<uint, Type>();
        static readonly HashSet<Assembly> _scannedAssemblies = new HashSet<Assembly>();

        public static uint Hash(Type type)
        {
            return Hash($"{type.FullName}, {type.Assembly.GetName().Name}");
        }

        public static uint Hash(string txt)
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

        public static Type BruteForceFind(uint hash)
        {
            if (_decoder.TryGetValue(hash, out var registered))
                return registered;

            ScanLoadedAssemblies();

            return _bruteForceCache.GetValueOrDefault(hash);
        }

        static void ScanLoadedAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (!_scannedAssemblies.Add(asm))
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type?.FullName == null)
                        continue;
                    var h = Hash(type);
                    _bruteForceCache[h] = type;
                }
            }
        }

        public static void PrintHashError(uint hash)
        {
            var meantToBe = BruteForceFind(hash);

            if (meantToBe == null)
            {
                PurrLogger.LogError($"Can't resolve type hash '{hash}'; either a corrupt packet," +
                                    $" or the remote has a type that doesn't exist in this build.");
                return;
            }

            PurrLogger.LogError(
                $"Can't resolve type hash '{hash}' ({meantToBe.FullName}); type exists locally but has no registered packer." +
                $" Likely a codegen mismatch between builds (conditional compile, stripping, or an asmdef difference), or the type isn't intended for networking."
            );
        }

        static void ThrowHashError(uint hash)
        {
            var meantToBe = BruteForceFind(hash);

            if (meantToBe == null)
            {
                throw new InvalidOperationException(
                    PurrLogger.FormatMessage($"Can't resolve type hash '{hash}'; either a corrupt packet," +
                                             $" or the remote has a type that doesn't exist in this build.")
                );
            }

            throw new InvalidOperationException(
                PurrLogger.FormatMessage(
                    $"Can't resolve type hash '{hash}' ({meantToBe.FullName}); type exists locally but has no registered packer." +
                    $" Likely a codegen mismatch between builds (conditional compile, stripping, or an asmdef difference), or the type isn't intended for networking.")
            );
        }

        public static Type ResolveType(uint hash)
        {
            if (_decoder.TryGetValue(hash, out var type))
                return type;

            ThrowHashError(hash);
            return null;
        }

        public static bool TryGetType(uint hash, out Type type)
        {
            return _decoder.TryGetValue(hash, out type);
        }

        [UsedImplicitly]
        public static uint PrepareType(Type type)
        {
            if (_hashes.TryGetValue(type, out var hash))
                return hash;

            hash = Hash(type);
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
            _bruteForceCache.Clear();
            _scannedAssemblies.Clear();
        }
    }
}

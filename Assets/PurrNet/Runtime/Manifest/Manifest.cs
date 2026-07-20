using System;
using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Utils;

namespace PurrNet
{
    public static class Manifest
    {
        static readonly Dictionary<RPCManifestKey, RPCManifestEntry> _entries = new Dictionary<RPCManifestKey, RPCManifestEntry>();

        [UsedByIL]
        public static void RegisterRPC(Type declaringType, int rpcId, RPCSignature signature)
        {
            if (declaringType == null)
                return;

            var key = new RPCManifestKey(declaringType, rpcId);
            _entries[key] = new RPCManifestEntry(declaringType, rpcId, signature);
        }

        public static bool TryGet(Type declaringType, int rpcId, out RPCManifestEntry entry)
        {
            return _entries.TryGetValue(new RPCManifestKey(declaringType, rpcId), out entry);
        }

        public static bool TryGet(uint typeHash, int rpcId, out RPCManifestEntry entry)
        {
            if (Hasher.TryGetType(typeHash, out var type))
                return TryGet(type, rpcId, out entry);

            entry = default;
            return false;
        }

        public static string Describe(Type declaringType, int rpcId)
        {
            if (TryGet(declaringType, rpcId, out var entry))
                return entry.ToString();

            return $"{declaringType?.FullName ?? "<unknown>"}.<rpcId={rpcId}>";
        }

        public static string Describe(uint typeHash, int rpcId)
        {
            if (Hasher.TryGetType(typeHash, out var type))
                return Describe(type, rpcId);

            return $"<typeHash={typeHash}>.<rpcId={rpcId}>";
        }

        public static IReadOnlyCollection<RPCManifestEntry> Entries => _entries.Values;

        public static int Count => _entries.Count;

        public static void Clear()
        {
            _entries.Clear();
        }
    }
}

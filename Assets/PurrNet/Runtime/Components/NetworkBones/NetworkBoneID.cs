using PurrNet.Modules;

namespace PurrNet
{
    public readonly struct NetworkBoneID : IStableHashable
    {
        readonly uint _hash;

        public NetworkBoneID(SceneID sceneId, NetworkID id, int index, BoneInfoType type)
        {
            ulong a = sceneId.id.value;
            ulong b = id.id.value;
            ulong c = id.scope.id.value;
            ulong d = (uint)index;
            ulong e = (uint)type;

            const ulong x = 0x9E3779B97F4A7C15UL;
            ulong h = 0;

            h += a + 0xBF58476D1CE4E5B9UL; h = Mix64(h);
            h += b + 0x94D049BB133111EBUL; h = Mix64(h);
            h += c + 0xD2B74407B1CE6E93UL; h = Mix64(h);
            h += d + 0xCA5A826395121157UL; h = Mix64(h);
            h += e + x;                          h = Mix64(h);

            h = Mix64(h);
            _hash = (uint)(h ^ (h >> 32));
        }

        public uint GetStableHash()
        {
            return _hash;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong Mix64(ulong z)
        {
            // SplitMix64 finalizer
            z ^= z >> 30;
            z *= 0xBF58476D1CE4E5B9UL;
            z ^= z >> 27;
            z *= 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return z;
        }
    }
}

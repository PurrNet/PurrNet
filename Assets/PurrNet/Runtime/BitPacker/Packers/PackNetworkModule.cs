using PurrNet.Modules;
using PurrNet.Packing;

namespace PurrNet
{
    public static class PackNetworkModule
    {
        [UsedByIL]
        public static void RegisterNetworkModule<T>() where T : NetworkModule
        {
            Packer<T>.RegisterWriter(WriteModule);
            Packer<T>.RegisterReader(ReadModule);
        }

        [UsedByIL]
        public static void WriteModule<T>(this BitPacker packer, T module) where T : NetworkModule
        {
            if (module is not { isSpawned: true } || !module.parent)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);
            Packer<byte>.Write(packer, module.index);

            Packer<NetworkIdentity>.Write(packer, module.parent);
        }

        [UsedByIL]
        public static void ReadModule<T>(this BitPacker packer, ref T module) where T : NetworkModule
        {
            if (!packer.ReadBit())
            {
                module = null;
                return;
            }

            byte index = 0;
            Packer<byte>.Read(packer, ref index);

            NetworkIdentity identity = null;
            Packer<NetworkIdentity>.Read(packer, ref identity);

            if (identity && identity.TryGetModule(index, out var nmodule) && nmodule is T result)
                module = result;
            else module = null;
        }

        [UsedByIL]
        public static void WriteIdentityConcrete(this BitPacker packer, NetworkModule module) =>
            WriteModule(packer, module);

        [UsedByIL]
        public static void ReadIdentityConcrete(this BitPacker packer, ref NetworkModule module) =>
            ReadModule(packer, ref module);
    }
}

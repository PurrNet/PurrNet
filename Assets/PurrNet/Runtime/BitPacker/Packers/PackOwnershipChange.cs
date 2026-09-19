using PurrNet.Modules;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class PackOwnerShipChange
    {
        [UsedByIL]
        internal static void WriteOwnershipChange(this BitPacker packer, OwnershipChange value)
        {
            Packer<SceneID>.Write(packer, value.sceneId);
            Packer<bool>.Write(packer, value.isAdding);
            Packer<PlayerID>.Write(packer, value.player);
            Packer<bool>.Write(packer, value.isSpawner);

            WriteIdentities(packer, value.identities);
        }

        [UsedByIL]
        internal static void ReadOwnershipChange(this BitPacker packer, ref OwnershipChange value)
        {
            Packer<SceneID>.Read(packer, ref value.sceneId);
            Packer<bool>.Read(packer, ref value.isAdding);
            Packer<PlayerID>.Read(packer, ref value.player);
            Packer<bool>.Read(packer, ref value.isSpawner);

            ReadIdentities(packer, ref value.identities);
        }

        private static void WriteIdentities(BitPacker packer, DisposableList<NetworkID> identities)
        {
            if (identities.isDisposed || identities.rawList == null)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);

            int count = identities.Count;
            Packer<Size>.Write(packer, (uint)count);

			bool useRLE = ShouldUseRLE(identities);
			packer.WriteBit(useRLE);

			if (!useRLE)
            {
                for (int id = 0; id < count; id++)
                {
                    Packer<NetworkID>.Write(packer, identities[id]);
                }

                return;
            }

            int i = 0;

            while (i < count)
            {
                NetworkID start = identities[i];
                int runLength = 1;

                while (i + runLength < count)
                {
                    NetworkID previous = identities[i + runLength - 1];
                    NetworkID next = identities[i + runLength];

                    if (!previous.scope.Equals(next.scope) || previous.scope.isBot != next.scope.isBot || next.id.value != previous.id.value + 1)
                    {
                        break;
                    }

                    runLength++;
                }

                Packer<PlayerID>.Write(packer, start.scope);
                Packer<PackedULong>.Write(packer, start.id);
                Packer<Size>.Write(packer, (uint)runLength);

                i += runLength;
            }
        }

        private static bool ShouldUseRLE(DisposableList<NetworkID> identities)
        {
            int count = identities.Count;

            if (count < 2)
            {
                return false;
            }

            int runs = 0;
            int i = 0;

            while (i < count)
            {
                runs++;

                NetworkID previous = identities[i];
                i++;

                while (i < count)
                {
                    NetworkID next = identities[i];

                    if (!previous.scope.Equals(next.scope) || previous.scope.isBot != next.scope.isBot || next.id.value != previous.id.value + 1)
                    {
                        break;
                    }

                    previous = next;
                    i++;
                }
            }

            // RLE stores scope + stating ID + length per run
            // uncompressed size is just count
            return runs * 3 < count;
        }

        static void ReadIdentities(BitPacker packer, ref DisposableList<NetworkID> identities)
        {
            identities.Dispose();

            bool hasValue = default;
            packer.Read(ref hasValue);

            if (!hasValue)
                return;

            Size totalCount = default;
            Packer<Size>.Read(packer, ref totalCount);

            identities = DisposableList<NetworkID>.Create(totalCount);

            bool useRLE = default;
            packer.Read(ref useRLE);

            if (!useRLE)
            {
                for (int i = 0; i < totalCount; i++)
                {
                    NetworkID identity = default;
                    Packer<NetworkID>.Read(packer, ref identity);
                    identities.Add(identity);
                }

                return;
            }

            int read = 0;

            while (read < totalCount)
            {
                PlayerID scope = default;
                Packer<PlayerID>.Read(packer, ref scope);

                PackedULong startId = default;
                Packer<PackedULong>.Read(packer, ref startId);

                Size runLength = default;
                Packer<Size>.Read(packer, ref runLength);

                for (int j = 0; j < runLength; j++)
                {
                    identities.Add(new NetworkID(startId.value + (ulong)j, scope));
                }

                read += runLength;
            }
        }
    }
}

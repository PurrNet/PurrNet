using System;
using PurrNet.Packing;
using PurrNet.Pooling;

namespace PurrNet.Modules
{

    public static class SpawnPacketBatchPacker
    {
        [UsedByIL]
        public static void Pack(BitPacker packer, SpawnPacketBatch batch)
        {
            Packer<SceneID>.Write(packer, batch.sceneId);

            int spawnCount = batch.spawnPackets.Count;
            Packer<PackedInt>.Write(packer, spawnCount);

            SpawnPacket lastSpawn = default;

            for (var i = 0; i < spawnCount; ++i)
            {
                var spawn = batch.spawnPackets[i];
                DeltaPacker<SpawnPacket>.Write(packer, lastSpawn, spawn);
                lastSpawn = spawn;
            }

            int despawnCount = batch.despawnPackets.Count;
            Packer<PackedInt>.Write(packer, despawnCount);

            DespawnPacket lastDespawn = default;
            for (var i = 0; i < despawnCount; ++i)
            {
                var despawn = batch.despawnPackets[i];
                DeltaPacker<DespawnPacket>.Write(packer, lastDespawn, despawn);
                lastDespawn = despawn;
            }
        }

        [UsedByIL]
        public static void Unpack(BitPacker packer, ref SpawnPacketBatch batch)
        {
            Packer<SceneID>.Read(packer, ref batch.sceneId);

            PackedInt spawnCount = default;
            Packer<PackedInt>.Read(packer, ref spawnCount);
            DeserializationLimits.ValidateCollectionLength<SpawnPacket>(spawnCount.value);

            SpawnPacket spawn = default;

            if (batch.spawnPackets.isDisposed)
                 batch.spawnPackets = DisposableList<SpawnPacket>.Create(spawnCount);
            else batch.spawnPackets.Clear();

            for (var i = 0; i < spawnCount; ++i)
            {
                var newSpawn = Packer.Copy(spawn);
                DeltaPacker<SpawnPacket>.Read(packer, spawn, ref newSpawn);
                spawn = newSpawn;
                batch.spawnPackets.Add(newSpawn);
            }

            PackedInt despawnCount = default;
            Packer<PackedInt>.Read(packer, ref despawnCount);
            DeserializationLimits.ValidateCollectionLength<DespawnPacket>(despawnCount.value);

            DespawnPacket despawn = default;

            if (batch.despawnPackets.isDisposed)
                batch.despawnPackets = DisposableList<DespawnPacket>.Create(despawnCount);
            else batch.despawnPackets.Clear();

            for (var i = 0; i < despawnCount; ++i)
            {
                DeltaPacker<DespawnPacket>.Read(packer, despawn, ref despawn);
                batch.despawnPackets.Add(despawn);
            }
        }
    }

    public struct SpawnPacketBatch : IPackedAuto, IDisposable
    {
        public SceneID sceneId;
        public DisposableList<SpawnPacket> spawnPackets;
        public DisposableList<DespawnPacket> despawnPackets;

        public SpawnPacketBatch(SceneID scene, DisposableList<SpawnPacket> spawnPackets, DisposableList<DespawnPacket> despawnPackets)
        {
            this.sceneId = scene;
            this.despawnPackets = despawnPackets;
            this.spawnPackets = spawnPackets;
        }

        public void Dispose()
        {
            int c = spawnPackets.Count;
            for (var i = 0; i < c; ++i)
                spawnPackets[i].Dispose();

            spawnPackets.Dispose();
            despawnPackets.Dispose();
        }

        public override string ToString()
        {
            return $"SpawnPacketBatch: {{ spawnPackets: {spawnPackets.Count} }}";
        }
    }
}

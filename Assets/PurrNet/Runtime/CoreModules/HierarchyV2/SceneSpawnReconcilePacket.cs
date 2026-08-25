using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;

namespace PurrNet.Modules
{
    /// <summary>
    /// One custom-data-free entry in the ordered scene-spawn preflight. The real
    /// <see cref="SpawnPacket"/> must match this topology before any custom deserializer runs.
    /// </summary>
    public struct SceneSpawnReconcileSpawnTopology : IPackedAuto, IDisposable
    {
        public SpawnID spawnId;
        public bool bypassPool;
        public bool isAsync;
        public GameObjectPrototype prototype;

        public void Dispose()
        {
            prototype.Dispose();
        }
    }

    /// <summary>
    /// Ordered preamble for one authoritative scene spawn manifest. A client must receive and
    /// validate this descriptor before it may reuse retained identities.
    /// </summary>
    public struct SceneSpawnReconcileBeginPacket : IPackedAuto, IDisposable
    {
        public SceneID sceneId;
        public string sessionId;
        public uint epoch;
        public DisposableList<SceneSpawnReconcileSpawnTopology> spawns;

        public void Dispose()
        {
            if (!spawns.isDisposed)
            {
                for (var i = 0; i < spawns.Count; i++)
                    spawns[i].Dispose();
                spawns.Dispose();
            }
        }

        public override string ToString()
        {
            var count = spawns.isDisposed ? 0 : spawns.Count;
            return $"SceneSpawnReconcileBeginPacket: {{ sceneId: {sceneId}, " +
                   $"session: {sessionId}@{epoch}, spawns: {count} }}";
        }
    }

    internal readonly struct SceneSpawnReconcileClassification
    {
        internal readonly bool isRetained;
        internal readonly NetworkID retainedRootId;
        internal readonly NetworkID[] replacementRootIds;

        internal SceneSpawnReconcileClassification(NetworkID retainedRootId)
        {
            isRetained = true;
            this.retainedRootId = retainedRootId;
            replacementRootIds = null;
        }

        internal SceneSpawnReconcileClassification(NetworkID[] replacementRootIds)
        {
            isRetained = false;
            retainedRootId = default;
            this.replacementRootIds = replacementRootIds;
        }
    }

    internal sealed class SceneSpawnReconcileManifest : IDisposable
    {
        private struct Entry
        {
            internal int topologyIndex;
            internal SceneSpawnReconcileClassification classification;
            internal bool consumed;
        }

        private DisposableList<SceneSpawnReconcileSpawnTopology> _topologies;
        private readonly Dictionary<SpawnID, Entry> _entries;
        private int _unconsumedCount;

        private SceneSpawnReconcileManifest(
            DisposableList<SceneSpawnReconcileSpawnTopology> topologies,
            Dictionary<SpawnID, Entry> entries)
        {
            _topologies = topologies;
            _entries = entries;
            _unconsumedCount = entries.Count;
        }

        internal int count => _entries.Count;
        internal int unconsumedCount => _unconsumedCount;

        internal SceneSpawnReconcileSpawnTopology GetTopology(int index) => _topologies[index];

        internal static bool TryCreate(
            DisposableList<SceneSpawnReconcileSpawnTopology> topologies,
            IReadOnlyDictionary<NetworkID, NetworkID> existingRootByIdentity,
            IReadOnlyDictionary<NetworkID, GameObjectPrototype> retainedTopologyByRoot,
            out SceneSpawnReconcileManifest manifest,
            out string failure) =>
            TryCreate(topologies, existingRootByIdentity, retainedTopologyByRoot,
                true, out manifest, out failure);

        internal static bool TryCreate(
            DisposableList<SceneSpawnReconcileSpawnTopology> topologies,
            IReadOnlyDictionary<NetworkID, NetworkID> existingRootByIdentity,
            IReadOnlyDictionary<NetworkID, GameObjectPrototype> retainedTopologyByRoot,
            bool allowRetainedRootReplacement,
            out SceneSpawnReconcileManifest manifest,
            out string failure)
        {
            manifest = null;
            failure = null;

            if (topologies.isDisposed)
            {
                failure = "the topology list was not allocated";
                return false;
            }

            existingRootByIdentity ??= new Dictionary<NetworkID, NetworkID>();
            retainedTopologyByRoot ??= new Dictionary<NetworkID, GameObjectPrototype>();

            var entries = new Dictionary<SpawnID, Entry>(topologies.Count);
            var declaredNetworkIds = new HashSet<NetworkID>();

            for (var topologyIndex = 0; topologyIndex < topologies.Count; topologyIndex++)
            {
                var topology = topologies[topologyIndex];
                if (!entries.TryAdd(topology.spawnId, new Entry { topologyIndex = topologyIndex }))
                {
                    failure = $"spawn {topology.spawnId} is declared more than once";
                    return false;
                }

                if (topology.prototype.framework.isDisposed ||
                    topology.prototype.framework.Count == 0)
                {
                    failure = $"spawn {topology.spawnId} has an empty topology";
                    return false;
                }

                var overlappingRoots = new HashSet<NetworkID>();
                for (var pieceIndex = 0; pieceIndex < topology.prototype.framework.Count; pieceIndex++)
                {
                    var networkId = topology.prototype.framework[pieceIndex].id;
                    if (!declaredNetworkIds.Add(networkId))
                    {
                        failure = $"NetworkID {networkId} is declared by more than one topology entry";
                        return false;
                    }

                    if (!existingRootByIdentity.TryGetValue(networkId, out var existingRoot))
                        continue;

                    if (!retainedTopologyByRoot.ContainsKey(existingRoot))
                    {
                        failure = $"spawn {topology.spawnId} collides with non-retained root {existingRoot}";
                        return false;
                    }

                    overlappingRoots.Add(existingRoot);
                }

                var entry = entries[topology.spawnId];
                if (overlappingRoots.Count == 1)
                {
                    NetworkID retainedRoot = default;
                    foreach (var root in overlappingRoots)
                    {
                        retainedRoot = root;
                        break;
                    }

                    var retainedTopology = retainedTopologyByRoot[retainedRoot];
                    if (HierarchyV2.ArePrototypesCompatible(
                            retainedTopology, topology.prototype))
                    {
                        entry.classification =
                            new SceneSpawnReconcileClassification(retainedRoot);
                    }
                    else if (!allowRetainedRootReplacement)
                    {
                        failure = $"spawn {topology.spawnId} is not topology-compatible with " +
                                  $"retained root {retainedRoot}";
                        return false;
                    }
                    else
                    {
                        entry.classification =
                            new SceneSpawnReconcileClassification(new[] { retainedRoot });
                    }
                }
                else if (overlappingRoots.Count > 1)
                {
                    if (!allowRetainedRootReplacement)
                    {
                        failure = $"spawn {topology.spawnId} spans {overlappingRoots.Count} retained roots";
                        return false;
                    }

                    var replacementRoots = new NetworkID[overlappingRoots.Count];
                    overlappingRoots.CopyTo(replacementRoots);
                    entry.classification = new SceneSpawnReconcileClassification(replacementRoots);
                }

                entries[topology.spawnId] = entry;
            }

            manifest = new SceneSpawnReconcileManifest(topologies, entries);
            return true;
        }

        internal bool TryConsume(SceneID sceneId, SpawnPacket packet,
            out SceneSpawnReconcileClassification classification, out string failure)
        {
            classification = default;
            failure = null;

            if (!_entries.TryGetValue(packet.packetIdx, out var entry))
            {
                failure = $"spawn {packet.packetIdx} was not declared by the preflight";
                return false;
            }

            if (entry.consumed)
            {
                failure = $"spawn {packet.packetIdx} was already consumed";
                return false;
            }

            var expected = _topologies[entry.topologyIndex];
            if (packet.sceneId != sceneId || packet.bypassPool != expected.bypassPool ||
                packet.isAsync != expected.isAsync ||
                !HierarchyV2.ArePrototypesCompatible(expected.prototype, packet.prototype))
            {
                failure = $"spawn {packet.packetIdx} does not match its declared scene, flags, and topology";
                return false;
            }

            entry.consumed = true;
            _entries[packet.packetIdx] = entry;
            _unconsumedCount--;
            classification = entry.classification;
            return true;
        }

        internal bool TryGetFirstUnconsumed(out SpawnID spawnId)
        {
            foreach (var pair in _entries)
            {
                if (pair.Value.consumed)
                    continue;
                spawnId = pair.Key;
                return true;
            }

            spawnId = default;
            return false;
        }

        internal bool ContainsCompatibleRetainedRoot(NetworkID rootId)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.classification.isRetained &&
                    entry.classification.retainedRootId == rootId)
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_topologies.isDisposed)
                return;

            for (var i = 0; i < _topologies.Count; i++)
                _topologies[i].Dispose();
            _topologies.Dispose();
            _entries.Clear();
            _unconsumedCount = 0;
        }
    }

    /// <summary>Ordered end marker for an authoritative scene spawn manifest.</summary>
    public struct SceneSpawnReconcilePacket : IPackedAuto
    {
        public SceneID sceneId;
        public string sessionId;
        public uint epoch;

        public override string ToString()
        {
            return $"SceneSpawnReconcilePacket: {{ sceneId: {sceneId}, session: {sessionId}@{epoch} }}";
        }
    }

    /// <summary>
    /// Reliable ordered rejection of one exact scene snapshot. Session and epoch scoping keeps a
    /// delayed abort from poisoning a later authority-switch attempt for the same scene.
    /// </summary>
    public struct SceneSpawnReconcileAbortPacket : IPackedAuto
    {
        public SceneID sceneId;
        public string sessionId;
        public uint epoch;
        public string reason;

        public override string ToString()
        {
            return $"SceneSpawnReconcileAbortPacket: {{ sceneId: {sceneId}, session: " +
                   $"{sessionId}@{epoch}, reason: {reason} }}";
        }
    }

}

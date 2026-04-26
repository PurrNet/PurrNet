using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;

namespace PurrNet.Modules
{
    /// <summary>
    /// Compresses spawn packets when runs of framework pieces structurally match a registered
    /// prefab prototype. The redundant per-piece <c>pid</c>, <c>childCount</c> and
    /// <c>inversedRelativePath</c> are reconstructed on the receiver from the prefab cache;
    /// only the runtime-determined network id, active state and local transform are sent.
    /// Mash-up spawns degrade gracefully: pieces that don't match a prefab subtree fall through
    /// to the original full-piece encoding.
    ///
    /// Driven directly by <see cref="HierarchyV2"/>, which has the owning <see cref="NetworkManager"/>
    /// in scope. Keeping the manager explicit (rather than reaching for NetworkManager.main) is what
    /// lets multi-manager setups work — the wire format is per-manager because prefab IDs are.
    /// </summary>
    public static class PackGameObjectPrototype
    {
        public static void WritePrototype(BitPacker packer, GameObjectPrototype value, NetworkManager manager)
        {
            Packer<UnityEngine.Vector3>.Write(packer, value.position);
            Packer<UnityEngine.Quaternion>.Write(packer, value.rotation);
            Packer<UnityEngine.Vector3>.Write(packer, value.scale);
            Packer<NetworkID?>.Write(packer, value.parentID);
            Packer<int[]>.Write(packer, value.path);
            Packer<int?>.Write(packer, value.defaultParentSiblingIndex);

            WriteFramework(packer, value.framework, manager);
        }

        public static GameObjectPrototype ReadPrototype(BitPacker packer, NetworkManager manager)
        {
            UnityEngine.Vector3 position = default;
            UnityEngine.Quaternion rotation = default;
            UnityEngine.Vector3 scale = default;
            NetworkID? parentID = default;
            int[] path = default;
            int? defaultParentSiblingIndex = default;

            Packer<UnityEngine.Vector3>.Read(packer, ref position);
            Packer<UnityEngine.Quaternion>.Read(packer, ref rotation);
            Packer<UnityEngine.Vector3>.Read(packer, ref scale);
            Packer<NetworkID?>.Read(packer, ref parentID);
            Packer<int[]>.Read(packer, ref path);
            Packer<int?>.Read(packer, ref defaultParentSiblingIndex);

            var framework = ReadFramework(packer, manager);
            return new GameObjectPrototype(position, rotation, scale, parentID, path, framework, defaultParentSiblingIndex);
        }

        private static void WriteFramework(BitPacker packer, DisposableList<GameObjectFrameworkPiece> framework, NetworkManager manager)
        {
            if (framework.isDisposed || framework.list == null || framework.Count == 0)
            {
                Packer<Size>.Write(packer, (uint)0);
                return;
            }

            int count = framework.Count;

            using var groupStarts = DisposableList<int>.Create(16);
            using var groupLengths = DisposableList<int>.Create(16);
            using var groupIsSubtree = DisposableList<bool>.Create(16);

            // Compression depends on prefab lookup; without a manager, every piece is loose.
            bool canCompress = manager;

            int gi = 0;
            while (gi < count)
            {
                var piece = framework[gi];
                int prefabId = piece.pid.prefabId;

                if (canCompress && prefabId >= 0 &&
                    HierarchyPool.TryGetPrefabPrototypeById(manager, prefabId, out var proto) &&
                    MatchesPrefabSubtree(framework, gi, proto))
                {
                    int n = proto.framework.Count;
                    groupStarts.Add(gi);
                    groupLengths.Add(n);
                    groupIsSubtree.Add(true);
                    gi += n;
                }
                else
                {
                    groupStarts.Add(gi);
                    groupLengths.Add(1);
                    groupIsSubtree.Add(false);
                    gi += 1;
                }
            }

            int groupCount = groupStarts.Count;
            Packer<Size>.Write(packer, (uint)groupCount);

            for (int g = 0; g < groupCount; g++)
            {
                int start = groupStarts[g];
                int len = groupLengths[g];
                bool isSubtree = groupIsSubtree[g];

                packer.WriteBit(isSubtree);

                if (isSubtree)
                {
                    var rootPiece = framework[start];
                    Packer<PackedInt>.Write(packer, rootPiece.pid.prefabId);
                    Packer<Size>.Write(packer, (uint)len);
                    // Root path is runtime-determined (where the subtree is parented in the broader spawn).
                    Packer<int[]>.Write(packer, rootPiece.inversedRelativePath);

                    for (int k = 0; k < len; k++)
                    {
                        var piece = framework[start + k];
                        Packer<NetworkID>.Write(packer, piece.id);
                        packer.WriteBit(piece.isActive);
                        Packer<LocalTransform>.Write(packer, piece.localTransform);
                    }
                }
                else
                {
                    WriteLoosePiece(packer, framework[start]);
                }
            }
        }

        private static void WriteLoosePiece(BitPacker packer, GameObjectFrameworkPiece piece)
        {
            Packer<PrefabPieceID>.Write(packer, piece.pid);
            Packer<NetworkID>.Write(packer, piece.id);
            Packer<Size>.Write(packer, piece.childCount);
            packer.WriteBit(piece.isActive);
            Packer<int[]>.Write(packer, piece.inversedRelativePath);
            Packer<LocalTransform>.Write(packer, piece.localTransform);
        }

        private static DisposableList<GameObjectFrameworkPiece> ReadFramework(BitPacker packer, NetworkManager manager)
        {
            Size groupCount = default;
            Packer<Size>.Read(packer, ref groupCount);

            var framework = DisposableList<GameObjectFrameworkPiece>.Create(groupCount.value > 0 ? (int)groupCount.value : 4);

            for (int g = 0; g < groupCount; g++)
            {
                bool isSubtree = packer.ReadBit();

                if (isSubtree)
                {
                    PackedInt prefabId = default;
                    Size subtreeSize = default;
                    int[] rootPath = default;

                    Packer<PackedInt>.Read(packer, ref prefabId);
                    Packer<Size>.Read(packer, ref subtreeSize);
                    Packer<int[]>.Read(packer, ref rootPath);

                    GameObjectPrototype proto = default;
                    bool resolved = manager && HierarchyPool.TryGetPrefabPrototypeById(manager, prefabId.value, out proto);

                    if (resolved && proto.framework.Count != subtreeSize.value)
                    {
                        PurrLogger.LogError(
                            $"SpawnPacket subtree size mismatch for prefabId {prefabId.value}: wire={subtreeSize.value}, prefab={proto.framework.Count}. Falling back.");
                        resolved = false;
                    }

                    for (int k = 0; k < subtreeSize.value; k++)
                    {
                        NetworkID id = default;
                        Packer<NetworkID>.Read(packer, ref id);
                        bool isActive = packer.ReadBit();
                        LocalTransform tr = default;
                        Packer<LocalTransform>.Read(packer, ref tr);

                        if (!resolved)
                            continue;

                        var protoPiece = proto.framework[k];
                        int[] piecePath = k == 0
                            ? (rootPath ?? System.Array.Empty<int>())
                            : (protoPiece.inversedRelativePath ?? System.Array.Empty<int>());

                        framework.Add(new GameObjectFrameworkPiece(
                            tr,
                            protoPiece.pid,
                            id,
                            protoPiece.childCount,
                            isActive,
                            piecePath));
                    }

                    if (!resolved)
                    {
                        PurrLogger.LogError($"SpawnPacket subtree references unknown prefabId {prefabId.value}; spawn will be incomplete.");
                    }
                }
                else
                {
                    PrefabPieceID pid = default;
                    NetworkID id = default;
                    Size childCount = default;
                    int[] invPath = default;
                    LocalTransform tr = default;

                    Packer<PrefabPieceID>.Read(packer, ref pid);
                    Packer<NetworkID>.Read(packer, ref id);
                    Packer<Size>.Read(packer, ref childCount);
                    bool isActive = packer.ReadBit();
                    Packer<int[]>.Read(packer, ref invPath);
                    Packer<LocalTransform>.Read(packer, ref tr);

                    framework.Add(new GameObjectFrameworkPiece(
                        tr,
                        pid,
                        id,
                        (int)childCount.value,
                        isActive,
                        invPath ?? System.Array.Empty<int>()));
                }
            }

            return framework;
        }

        /// <summary>
        /// Checks that <paramref name="framework"/> starting at <paramref name="start"/> structurally
        /// matches <paramref name="proto"/>'s framework. The root piece's inversedRelativePath is
        /// allowed to differ (it's runtime-determined and sent on the wire); descendants must match
        /// exactly so they can be reconstructed from the prefab.
        /// </summary>
        private static bool MatchesPrefabSubtree(DisposableList<GameObjectFrameworkPiece> framework, int start, GameObjectPrototype proto)
        {
            int n = proto.framework.Count;
            if (n == 0 || start + n > framework.Count)
                return false;

            if (!framework[start].StructurallyMatches(proto.framework[0], ignorePath: true))
                return false;

            for (int i = 1; i < n; i++)
            {
                if (!framework[start + i].StructurallyMatches(proto.framework[i], ignorePath: false))
                    return false;
            }

            return true;
        }
    }
}

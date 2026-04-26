using System.Text;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Modules
{
    public readonly struct LocalTransform
    {
        public readonly CompressedVector3 localPosition;
        public readonly PackedQuaternion localRotation;
        public readonly CompressedVector3 localScale;

        public LocalTransform(Vector3 position, Quaternion rotation, Vector3 localScale)
        {
            localPosition = position;
            localRotation = rotation;
            this.localScale = localScale;
        }

        public void Apply(Transform trs)
        {
            HierarchyV2.SetLocalPosAndRot(trs, localPosition, localRotation, localScale);
        }
    }

    public readonly struct GameObjectFrameworkPiece
    {
        public readonly PrefabPieceID pid;
        public readonly NetworkID id;
        public readonly Size childCount;
        public readonly bool isActive;
        public readonly int[] inversedRelativePath;

        public readonly LocalTransform localTransform;

        public GameObjectFrameworkPiece(LocalTransform trs, PrefabPieceID pid, NetworkID id, int childCount, bool isActive,
            int[] path)
        {
            this.localTransform  = trs;
            this.pid = pid;
            this.id = id;
            this.childCount = childCount;
            this.inversedRelativePath = path;
            this.isActive = isActive;
        }

        public bool AreEqual(GameObjectFrameworkPiece other)
        {
            return pid.Equals(other.pid) && childCount == other.childCount &&
                   isActive == other.isActive && ArePathsEqual(inversedRelativePath, other.inversedRelativePath);
        }

        /// <summary>
        /// Structural match for prefab-subtree compression: identical prefab piece id, child count
        /// and inversed relative path. isActive and localTransform are runtime-overridable, so they
        /// are compared separately and sent as overrides on the wire.
        /// </summary>
        public bool StructurallyMatches(GameObjectFrameworkPiece other, bool ignorePath)
        {
            if (!pid.Equals(other.pid) || childCount != other.childCount)
                return false;
            return ignorePath || ArePathsEqual(inversedRelativePath, other.inversedRelativePath);
        }

        private static bool ArePathsEqual(int[] ints, int[] otherInversedRelativePath)
        {
            int la = ints?.Length ?? 0;
            int lb = otherInversedRelativePath?.Length ?? 0;
            if (la != lb)
                return false;

            for (int i = 0; i < la; i++)
            {
                if (ints[i] != otherInversedRelativePath[i])
                    return false;
            }

            return true;
        }

        public override string ToString()
        {
            StringBuilder builder = new();
            builder.Append("GameObjectFrameworkPiece: { ");
            builder.Append("Pid: ");
            builder.Append(pid);
            builder.Append(", Nid: ");
            builder.Append(id);
            builder.Append(", childCount: ");
            builder.Append(childCount);
            builder.Append(", Path: ");
            for (int i = 0; i < inversedRelativePath.Length; i++)
            {
                builder.Append(inversedRelativePath[i]);
                if (i < inversedRelativePath.Length - 1)
                    builder.Append(" <- ");
            }

            builder.Append(" }");
            return builder.ToString();
        }
    }
}

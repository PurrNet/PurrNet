using System;
using System.Text;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public struct GameObjectPrototype : IDisposable
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public NetworkID? parentID;
        public readonly int[] path;
        public readonly int? defaultParentSiblingIndex;
        public DisposableList<GameObjectFrameworkPiece> framework;

        public GameObjectPrototype(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            NetworkID? parentID,
            int[] path,
            DisposableList<GameObjectFrameworkPiece> framework,
            int? defaultParentSiblingIndex)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
            this.framework = framework;
            this.parentID = parentID;
            this.path = path;
            this.defaultParentSiblingIndex = defaultParentSiblingIndex;
        }

        public void Dispose()
        {
            framework.Dispose();
        }

        public GameObjectPrototype Clone()
        {
            var newFramework = DisposableList<GameObjectFrameworkPiece>.Create(framework.Count);
            for (int i = 0; i < framework.Count; i++)
            {
                var piece = framework[i];
                int[] pathCopy = null;
                if (piece.inversedRelativePath != null && piece.inversedRelativePath.Length > 0)
                {
                    pathCopy = new int[piece.inversedRelativePath.Length];
                    System.Array.Copy(piece.inversedRelativePath, pathCopy, piece.inversedRelativePath.Length);
                }
                newFramework.Add(new GameObjectFrameworkPiece(
                    piece.localTransform,
                    piece.pid,
                    piece.id,
                    piece.childCount,
                    piece.isActive,
                    pathCopy ?? System.Array.Empty<int>()));
            }
            return new GameObjectPrototype(
                position,
                rotation,
                scale,
                parentID,
                path,
                newFramework,
                defaultParentSiblingIndex);
        }

        public override string ToString()
        {
            StringBuilder builder = new();
            builder.Append($"GameObjectPrototype: {{\n    ");
            for (int i = 0; i < framework.Count; i++)
            {
                builder.Append(framework[i]);
                if (i < framework.Count - 1)
                    builder.Append("\n    ");
            }

            builder.Append("\n}");
            return builder.ToString();
        }
    }
}

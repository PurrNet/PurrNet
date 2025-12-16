using UnityEngine;

namespace PurrNet
{
    public struct Vector3WithParent
    {
        readonly Transform parent;
        public readonly Vector3 position;
        readonly bool isLocalPos;

        public Vector3WithParent(Transform parent, Vector3WithParent other)
        {
            this.parent = parent;
            this.position = other.position;
            this.isLocalPos = other.isLocalPos;
        }

        public Vector3WithParent(Transform parent, bool isLocalPos, Vector3 position)
        {
            this.parent = parent;
            this.position = position;
            this.isLocalPos = isLocalPos;
        }

        public static Vector3WithParent Lerp(Vector3WithParent a, Vector3WithParent b, float t)
        {
            if (!b.isLocalPos)
                return new Vector3WithParent(default, default, Vector3.Lerp(a.position, b.position, t));

            var aWorldPos = a.parent ? a.parent.TransformPoint(a.position) : a.position;
            var bWorldPos = b.parent ? b.parent.TransformPoint(b.position) : b.position;
            var lerpedWorldPos = Vector3.Lerp(aWorldPos, bWorldPos, t);
            return new Vector3WithParent(default, default, lerpedWorldPos);
        }

        public static Vector3WithParent NoLerp(Vector3WithParent a, Vector3WithParent b, float t)
        {
            if (!b.isLocalPos)
                return new Vector3WithParent(default, default, b.position);
            var worldPos = b.parent ? b.parent.TransformPoint(b.position) : b.position;
            return new Vector3WithParent(default, default, worldPos);
        }
    }
}

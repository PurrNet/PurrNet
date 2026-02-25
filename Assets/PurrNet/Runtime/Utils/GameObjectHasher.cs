using System.Text;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public static class GameObjectHasher
    {
        public static StringBuilder ComputeStringRecursive(GameObject obj)
        {
            var sb = new StringBuilder();

            // Use invariant culture for consistent string formatting
            sb.Append(obj.name);

            var components = obj.GetComponents<Component>();
            sb.Append(components.Length);

            foreach (var component in components)
            {
                if (component)
                    sb.Append(component.GetType().FullName);
            }

            var childCount = obj.transform.childCount;
            sb.Append(childCount);
            for (var i = 0; i < childCount; i++)
            {
                var child = obj.transform.GetChild(i);
                sb.Append(ComputeStringRecursive(child.gameObject));
            }

            return sb;
        }

        public static uint ComputeHashRecursive(GameObject obj)
        {
            return Hasher.Hash(ComputeStringRecursive(obj).ToString());
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
#pragma warning disable CS0618 // Type or member is obsolete
    [CustomEditor(typeof(PrefabLink))]
#pragma warning restore CS0618 // Type or member is obsolete
    public class PrefabLinkInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "PrefabLink is obsolete and no longer used by PurrNet. " +
                "It can be safely removed from this GameObject.",
                MessageType.Warning);

            GUILayout.Space(4);

            if (GUILayout.Button("Remove PrefabLink"))
            {
#pragma warning disable CS0618 // Type or member is obsolete
                var component = (PrefabLink)target;
#pragma warning restore CS0618 // Type or member is obsolete
                Undo.DestroyObjectImmediate(component);
            }
        }
    }
}

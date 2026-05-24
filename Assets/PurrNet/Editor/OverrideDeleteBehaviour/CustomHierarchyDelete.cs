using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [InitializeOnLoad]
    public class CustomHierarchyDelete
    {
        static CustomHierarchyDelete()
        {
#if UNITY_6000_5_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
        }

#if UNITY_6000_5_OR_NEWER
        private static void OnHierarchyGUI(EntityId entityId, Rect selectionrect)
#else
        private static void OnHierarchyGUI(int instanceid, Rect selectionrect)
#endif
        {
            bool isPlaying = Application.isPlaying;

            if (!isPlaying)
                return;

            var currentEvent = Event.current;

            switch (currentEvent.type)
            {
                case EventType.ExecuteCommand when currentEvent.commandName == "Paste":
                case EventType.ExecuteCommand when currentEvent.commandName == "Duplicate":
                {
                    EditorApplication.delayCall += () =>
                    {
                        foreach (var go in Selection.gameObjects)
                            NetworkIdentity.SpawnInternal(go);
                    };
                    break;
                }
                case EventType.KeyDown when
                    currentEvent.keyCode is KeyCode.Delete or KeyCode.Backspace:
                {
                    var selectedObjects = Selection.objects;

                    if (selectedObjects.Length > 0)
                    {
                        if (PurrDeleteHandler.CustomDeleteLogic(selectedObjects))
                            currentEvent.Use();
                    }

                    break;
                }
            }
        }
    }
}

using System.Collections.Generic;
using PurrNet.Modules;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [InitializeOnLoad]
    public class CustomDragAndDropHandler
    {
        private static readonly HashSet<int> _beforeObjects = new();
        private static readonly HashSet<int> _afterObjects = new();
        private static readonly HashSet<int> _newObjects = new();

        static int _lastDragDropEventFrame = -1;

        private static void TakeSnapShotOfHierarchy(HashSet<int> set)
        {
            set.Clear();
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allObjects.Length; i++)
            {
                var obj = allObjects[i];
                set.Add(obj.GetInstanceID());
            }
        }

        static CustomDragAndDropHandler()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        }

        private static void OnHierarchyItemGUI(int instanceid, Rect selectionrect)
        {
            bool isPlaying = Application.isPlaying;

            if (!isPlaying)
                return;

            switch (Event.current.type)
            {
                case EventType.DragPerform:
                {
                    if (!IsDraggingPrefabs())
                        break;

                    if (_lastDragDropEventFrame != Time.frameCount)
                    {
                        TakeSnapShotOfHierarchy(_beforeObjects);
                        _lastDragDropEventFrame = Time.frameCount;
                    }

                    break;
                }
                case EventType.DragExited:
                {
                    if (!IsDraggingPrefabs())
                        break;

                    CheckNewInstantiations();
                    break;
                }
            }
        }

        static bool IsDraggingPrefabs()
        {
            if (DragAndDrop.objectReferences.Length == 0)
                return false;

            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (PrefabUtility.IsPartOfPrefabAsset(obj))
                    return true;
            }

            return false;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            bool isPlaying = Application.isPlaying;

            if (!isPlaying)
                return;

            if (Event.current.type == EventType.DragExited && IsDraggingPrefabs())
            {
                _dragDropReferences.Clear();
                FillPrefabListWithDrapDropReferences(_dragDropReferences);
                var gameObjects = Selection.gameObjects;
                for (var i = 0; i < gameObjects.Length; i++)
                {
                    var gos = gameObjects[i];
                    PurrNetGameObjectUtils.NotifyGameObjectCreated(gos,
                        _dragDropReferences.Count > i ? _dragDropReferences[i] : null);
                }
            }
        }

        static readonly List<GameObject> _dragDropReferences = new();

        private static void CheckNewInstantiations()
        {
            TakeSnapShotOfHierarchy(_afterObjects);
            _newObjects.Clear();

            foreach (var id in _afterObjects)
            {
                if (!_beforeObjects.Contains(id))
                    _newObjects.Add(id);
            }

            if (_newObjects.Count > 0)
            {
                _dragDropReferences.Clear();
                FillPrefabListWithDrapDropReferences(_dragDropReferences);

                int idx = 0;
                foreach (var id in _newObjects)
                {
#if UNITY_6000_3_OR_NEWER
                    var go = EditorUtility.EntityIdToObject(id) as GameObject;
#else
                    var go = EditorUtility.InstanceIDToObject(id) as GameObject;
#endif
                    if (go)
                    {
                        bool isAnyParentInNewObjects = false;

                        var trs = go.transform.parent;

                        while (trs)
                        {
                            if (_newObjects.Contains(trs.gameObject.GetInstanceID()))
                            {
                                isAnyParentInNewObjects = true;
                                break;
                            }

                            trs = trs.parent;
                        }

                        if (!isAnyParentInNewObjects)
                        {
                            PurrNetGameObjectUtils.NotifyGameObjectCreated(go,
                                idx < _dragDropReferences.Count ? _dragDropReferences[idx] : null);
                            idx++;
                        }
                    }
                }
            }

            _beforeObjects.Clear();
            _beforeObjects.UnionWith(_afterObjects);
        }

        static void FillPrefabListWithDrapDropReferences(List<GameObject> list)
        {
            var references = DragAndDrop.objectReferences;

            for (var i = 0; i < references.Length; i++)
            {
                if (references[i] is GameObject go)
                    list.Add(go);
            }
        }
    }
}

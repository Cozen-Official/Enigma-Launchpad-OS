#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty objectHandlers;
        private readonly List<SerializedObject> objectHandlerObjects = new List<SerializedObject>();

        private class ObjectDuplicateUsage
        {
            public readonly GameObject gameObject;
            public readonly List<int> folderIndices = new List<int>();
            public int ownerFolder;

            public ObjectDuplicateUsage(GameObject gameObject, int ownerFolder)
            {
                this.gameObject = gameObject;
                this.ownerFolder = ownerFolder;
                folderIndices.Add(ownerFolder);
            }

            public void AddFolder(int folderIdx)
            {
                if (!folderIndices.Contains(folderIdx))
                {
                    folderIndices.Add(folderIdx);
                }
            }
        }

        private SerializedObject GetObjectHandlerObjectForFolder(int folderIdx)
        {
            if (objectHandlerObjects == null)
            {
                return null;
            }

            foreach (SerializedObject handlerObj in objectHandlerObjects)
            {
                if (handlerObj == null) continue;
                SerializedProperty folderIndexProp = handlerObj.FindProperty("folderIndex");
                if (folderIndexProp != null && folderIndexProp.intValue == folderIdx)
                {
                    return handlerObj;
                }
            }

            return null;
        }

        private void EnsureObjectHandlerParity()
        {
            objectHandlerObjects.Clear();

            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || objectHandlers == null)
            {
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            List<int> objectFolders = GetObjectFolderIndices();
            int objectFolderCount = objectFolders.Count;

            // Create one ObjectHandler per Object folder
            int requiredHandlerCount = objectFolderCount;

            var existingHandlers = new List<ObjectHandler>();
            for (int i = 0; i < objectHandlers.arraySize; i++)
            {
                SerializedProperty element = objectHandlers.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is ObjectHandler handler)
                {
                    existingHandlers.Add(handler);
                }
            }

            foreach (ObjectHandler handler in launchpad.GetComponentsInChildren<ObjectHandler>(true))
            {
                if (handler != null && !existingHandlers.Contains(handler))
                {
                    existingHandlers.Add(handler);
                }
            }

            var assigned = new ObjectHandler[requiredHandlerCount];
            var unused = new List<ObjectHandler>(existingHandlers);

            for (int i = 0; i < existingHandlers.Count; i++)
            {
                ObjectHandler handler = existingHandlers[i];
                if (handler == null)
                {
                    continue;
                }

                int slot = objectFolders.IndexOf(handler.folderIndex);
                if (slot >= 0 && assigned[slot] == null)
                {
                    assigned[slot] = handler;
                    unused.Remove(handler);
                }
            }

            for (int slot = 0; slot < assigned.Length && unused.Count > 0; slot++)
            {
                if (assigned[slot] != null)
                {
                    continue;
                }

                assigned[slot] = unused[0];
                unused.RemoveAt(0);
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] != null)
                {
                    continue;
                }

                int folderIndex = objectFolders[i];
                string handlerName = GetExpectedObjectHandlerName(folderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create ObjectHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                ObjectHandler handler = handlerObject.AddComponent<ObjectHandler>();
                Undo.RecordObject(handler, "Configure ObjectHandler");
                handler.launchpad = launchpad;
                handler.folderIndex = folderIndex;
                assigned[i] = handler;
            }

            foreach (ObjectHandler handler in unused)
            {
                if (handler != null)
                {
                    Undo.DestroyObjectImmediate(handler.gameObject);
                }
            }

            objectHandlers.arraySize = requiredHandlerCount;
            for (int i = 0; i < assigned.Length; i++)
            {
                ObjectHandler handler = assigned[i];
                // assigned.Length = requiredHandlerCount = objectFolderCount, so i < objectFolders.Count
                int folderIndex = objectFolders[i];
                if (handler != null)
                {
                    Undo.RecordObject(handler, "Configure ObjectHandler");
                    handler.launchpad = launchpad;
                    handler.folderIndex = folderIndex;
                    handler.transform.SetParent(foldersTransform);
                    if (handler.gameObject.hideFlags != HandlerHideFlags)
                    {
                        handler.gameObject.hideFlags = HandlerHideFlags;
                    }

                    // Update GameObject name to match current folder name
                    string expectedName = GetExpectedObjectHandlerName(folderIndex);
                    if (handler.gameObject.name != expectedName)
                    {
                        Undo.RecordObject(handler.gameObject, "Rename ObjectHandler");
                        handler.gameObject.name = expectedName;
                    }
                }

                SerializedProperty element = objectHandlers.GetArrayElementAtIndex(i);
                if (element != null)
                {
                    element.objectReferenceValue = handler;
                }

                if (handler != null)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    objectHandlerObjects.Add(serializedHandler);
                }
                else
                {
                    objectHandlerObjects.Add(null);
                }
            }
        }

        private List<int> GetObjectFolderIndices()
        {
            var indices = new List<int>();
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;
            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (GetFolderTypeFromProp(typeProp) == ToggleFolderType.Objects)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private string GetExpectedObjectHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            string sanitizedFolderName = SanitizeForHandlerName(folderName);
            return $"ObjectHandler_{sanitizedFolderName}";
        }

        // Update all ObjectHandler folderIndex values to match their folder positions
        private void UpdateHandlerFolderIndices()
        {
            if (objectHandlerObjects == null) return;

            foreach (SerializedObject handlerObj in objectHandlerObjects)
            {
                if (handlerObj == null) continue;
                handlerObj.Update();
            }
            // EnsureObjectHandlerParity will be called on next inspector update to reassign handlers
        }

        private Dictionary<int, List<DuplicateMessage>> BuildObjectDuplicateReport()
        {
            var result = new Dictionary<int, List<DuplicateMessage>>();
            if (folderNamesProperty == null || objectHandlerObjects == null)
            {
                return result;
            }

            int folderCount = folderNamesProperty.arraySize;
            var usageMap = new Dictionary<GameObject, ObjectDuplicateUsage>();

            // Iterate through all ObjectHandlers and check their folderEntries
            for (int handlerIdx = 0; handlerIdx < objectHandlerObjects.Count; handlerIdx++)
            {
                SerializedObject handlerObject = objectHandlerObjects[handlerIdx];
                if (handlerObject == null)
                {
                    continue;
                }

                SerializedProperty folderIndexProp = handlerObject.FindProperty("folderIndex");
                SerializedProperty folderEntriesProp = handlerObject.FindProperty("folderEntries");

                if (folderIndexProp == null || folderEntriesProp == null)
                {
                    continue;
                }

                int folderIdx = folderIndexProp.intValue;
                if (folderIdx < 0 || folderIdx >= folderCount)
                {
                    continue;
                }

                ToggleFolderType folderType = GetFolderType(folderIdx);
                if (folderType == ToggleFolderType.Materials ||
                    folderType == ToggleFolderType.Skybox ||
                    folderType == ToggleFolderType.Mochie ||
                    folderType == ToggleFolderType.Stats)
                {
                    continue;
                }

                // Check each entry in this handler's folderEntries array
                for (int i = 0; i < folderEntriesProp.arraySize; i++)
                {
                    var element = folderEntriesProp.GetArrayElementAtIndex(i);
                    GameObject go = element.objectReferenceValue as GameObject;
                    if (go == null) continue;

                    if (!usageMap.TryGetValue(go, out var usage))
                    {
                        usage = new ObjectDuplicateUsage(go, folderIdx);
                        usageMap.Add(go, usage);
                    }
                    else
                    {
                        usage.AddFolder(folderIdx);
                    }
                }
            }

            foreach (var usage in usageMap.Values)
            {
                if (usage.folderIndices.Count <= 1) continue;

                usage.folderIndices.Sort();
                string objectName = usage.gameObject != null ? usage.gameObject.name : "(GameObject)";
                string ownerName = GetFolderDisplayName(usage.ownerFolder);

                foreach (int folderIdx in usage.folderIndices)
                {
                    bool isOwner = folderIdx == usage.ownerFolder;
                    var list = GetOrCreateMessageList(result, folderIdx);
                    if (isOwner)
                    {
                        string others = BuildOtherFolderList(usage.folderIndices, folderIdx);
                        string msg = $"Owns GameObject '{objectName}', also referenced by {others}.";
                        list.Add(new DuplicateMessage(msg, MessageType.Warning));
                    }
                    else
                    {
                        string msg = $"GameObject '{objectName}' is already owned by folder '{ownerName}'.";
                        list.Add(new DuplicateMessage(msg, MessageType.Error));
                    }
                }
            }

            return result;
        }
    }
}
#endif

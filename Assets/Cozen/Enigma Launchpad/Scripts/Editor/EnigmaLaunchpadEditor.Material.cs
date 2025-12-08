#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty materialHandlers;
        private readonly List<SerializedObject> materialHandlerObjects = new List<SerializedObject>();
        private readonly List<int> materialHandlerFolderIndices = new List<int>();

        private struct MaterialAssignmentKey : System.IEquatable<MaterialAssignmentKey>
        {
            public readonly Renderer renderer;
            public readonly Material material;

            public MaterialAssignmentKey(Renderer renderer, Material material)
            {
                this.renderer = renderer;
                this.material = material;
            }

            public bool Equals(MaterialAssignmentKey other)
            {
                return renderer == other.renderer && material == other.material;
            }

            public override bool Equals(object obj)
            {
                return obj is MaterialAssignmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 31 + (renderer != null ? renderer.GetHashCode() : 0);
                hash = hash * 31 + (material != null ? material.GetHashCode() : 0);
                return hash;
            }
        }

        private class MaterialDuplicateUsage
        {
            public readonly Renderer renderer;
            public readonly Material material;
            public readonly List<int> folderIndices = new List<int>();
            public int ownerFolder;

            public MaterialDuplicateUsage(Renderer renderer, Material material, int ownerFolder)
            {
                this.renderer = renderer;
                this.material = material;
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

        private SerializedObject GetMaterialHandlerObjectForFolder(int folderIdx)
        {
            if (materialHandlerObjects != null && materialHandlerFolderIndices != null)
            {
                int handlerIndex = materialHandlerFolderIndices.IndexOf(folderIdx);
                if (handlerIndex >= 0 && handlerIndex < materialHandlerObjects.Count)
                {
                    return materialHandlerObjects[handlerIndex];
                }
            }

            // Fallback: search the serialized materialHandlers array for a matching folder index.
            if (materialHandlers == null)
            {
                return null;
            }

            for (int i = 0; i < materialHandlers.arraySize; i++)
            {
                SerializedProperty element = materialHandlers.GetArrayElementAtIndex(i);
                if (element == null || element.objectReferenceValue == null)
                {
                    continue;
                }

                if (element.objectReferenceValue is MaterialHandler handler && handler.folderIndex == folderIdx)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    return serializedHandler;
                }
            }

            return null;
        }

        private SerializedProperty GetFolderMaterialRendererProperty(int folderIdx)
        {
            if (materialHandlerObjects != null && materialHandlerFolderIndices != null)
            {
                int handlerIndex = materialHandlerFolderIndices.IndexOf(folderIdx);
                if (handlerIndex >= 0 && handlerIndex < materialHandlerObjects.Count && materialHandlerObjects[handlerIndex] != null)
                {
                    return materialHandlerObjects[handlerIndex].FindProperty("folderMaterialRenderers");
                }
            }

            // Fallback: search the serialized materialHandlers array for a matching folder index.
            if (materialHandlers == null)
            {
                return null;
            }

            for (int i = 0; i < materialHandlers.arraySize; i++)
            {
                SerializedProperty element = materialHandlers.GetArrayElementAtIndex(i);
                if (element == null || element.objectReferenceValue == null)
                {
                    continue;
                }

                if (element.objectReferenceValue is MaterialHandler handler && handler.folderIndex == folderIdx)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    return serializedHandler.FindProperty("folderMaterialRenderers");
                }
            }

            return null;
        }

        private int GetRendererArraySize(SerializedProperty rendererProp)
        {
            return rendererProp != null && rendererProp.isArray ? rendererProp.arraySize : 0;
        }

        private Renderer GetRendererFromProperty(SerializedProperty rendererProp, int index)
        {
            if (rendererProp == null || !rendererProp.isArray || index < 0 || index >= rendererProp.arraySize)
            {
                return null;
            }

            var element = rendererProp.GetArrayElementAtIndex(index);
            return element != null ? element.objectReferenceValue as Renderer : null;
        }

        private void EnsureMaterialHandlerParity()
        {
            materialHandlerObjects.Clear();
            materialHandlerFolderIndices.Clear();

            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || materialHandlers == null)
            {
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);

            List<int> materialFolders = GetMaterialFolderIndices();
            int materialFolderCount = materialFolders.Count;

            var existingHandlers = new List<MaterialHandler>();
            for (int i = 0; i < materialHandlers.arraySize; i++)
            {
                SerializedProperty element = materialHandlers.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is MaterialHandler handler)
                {
                    existingHandlers.Add(handler);
                }
            }

            foreach (MaterialHandler handler in launchpad.GetComponentsInChildren<MaterialHandler>(true))
            {
                if (handler != null && !existingHandlers.Contains(handler))
                {
                    existingHandlers.Add(handler);
                }
            }

            var assigned = new MaterialHandler[materialFolderCount];
            var unused = new List<MaterialHandler>(existingHandlers);

            for (int i = 0; i < existingHandlers.Count; i++)
            {
                MaterialHandler handler = existingHandlers[i];
                if (handler == null) continue;
                int slot = materialFolders.IndexOf(handler.folderIndex);
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

                int folderIndex = materialFolders[i];
                string handlerName = GetExpectedMaterialHandlerName(folderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create MaterialHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                MaterialHandler handler = handlerObject.AddComponent<MaterialHandler>();
                Undo.RecordObject(handler, "Configure MaterialHandler");
                handler.launchpad = launchpad;
                handler.folderIndex = folderIndex;
                assigned[i] = handler;
            }

            foreach (MaterialHandler handler in unused)
            {
                if (handler != null)
                {
                    Undo.DestroyObjectImmediate(handler.gameObject);
                }
            }

            materialHandlers.arraySize = materialFolderCount;
            for (int i = 0; i < assigned.Length; i++)
            {
                MaterialHandler handler = assigned[i];
                int folderIndex = materialFolders[i];
                if (handler != null)
                {
                    Undo.RecordObject(handler, "Configure MaterialHandler");
                    handler.launchpad = launchpad;
                    handler.folderIndex = folderIndex;
                    handler.transform.SetParent(foldersTransform);
                    if (handler.gameObject.hideFlags != HandlerHideFlags)
                    {
                        handler.gameObject.hideFlags = HandlerHideFlags;
                    }

                    // Update GameObject name to match current folder name
                    string expectedName = GetExpectedMaterialHandlerName(folderIndex);
                    if (handler.gameObject.name != expectedName)
                    {
                        Undo.RecordObject(handler.gameObject, "Rename MaterialHandler");
                        handler.gameObject.name = expectedName;
                    }
                }

                SerializedProperty element = materialHandlers.GetArrayElementAtIndex(i);
                if (element != null)
                {
                    element.objectReferenceValue = handler;
                }

                materialHandlerFolderIndices.Add(folderIndex);
                if (handler != null)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    materialHandlerObjects.Add(serializedHandler);
                }
                else
                {
                    materialHandlerObjects.Add(null);
                }
            }
        }

        private List<int> GetMaterialFolderIndices()
        {
            var indices = new List<int>();
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;
            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (GetFolderTypeFromProp(typeProp) == ToggleFolderType.Materials)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private string GetExpectedMaterialHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"MaterialHandler_{folderName}";
        }

        private int GetMaterialHandlerSlotForFolder(int folderIdx)
        {
            return materialHandlerFolderIndices.IndexOf(folderIdx);
        }

        private SerializedObject GetMaterialHandlerSerializedObject(int folderIdx)
        {
            int slot = GetMaterialHandlerSlotForFolder(folderIdx);
            if (slot < 0 || slot >= materialHandlerObjects.Count)
            {
                return null;
            }

            return materialHandlerObjects[slot];
        }

        private SerializedProperty GetMaterialHandlerPropertyAtSlot(int slot)
        {
            if (materialHandlers == null || slot < 0 || slot >= materialHandlers.arraySize)
            {
                return null;
            }

            return materialHandlers.GetArrayElementAtIndex(slot);
        }

        private void DrawMaterialHandlerInspector(int folderIdx)
        {
            SerializedObject handlerObject = GetMaterialHandlerSerializedObject(folderIdx);
            if (handlerObject == null)
            {
                int slot = GetMaterialHandlerSlotForFolder(folderIdx);
                SerializedProperty handlerProp = GetMaterialHandlerPropertyAtSlot(slot);
                if (handlerProp != null && handlerProp.objectReferenceValue is MaterialHandler handler)
                {
                    handlerObject = new SerializedObject(handler);
                    handlerObject.Update();

                    if (slot >= 0 && slot < materialHandlerObjects.Count)
                    {
                        materialHandlerObjects[slot] = handlerObject;
                    }
                }
            }
            if (handlerObject == null)
            {
                return;
            }
        }

        private Dictionary<int, List<DuplicateMessage>> BuildMaterialDuplicateReport()
        {
            var result = new Dictionary<int, List<DuplicateMessage>>();
            if (folderNamesProperty == null || objectHandlerObjects == null)
            {
                return result;
            }

            int folderCount = folderNamesProperty.arraySize;
            var usageMap = new Dictionary<MaterialAssignmentKey, MaterialDuplicateUsage>();

            // Iterate through all ObjectHandlers and check their folderEntries for materials
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
                if (folderType != ToggleFolderType.Materials)
                {
                    continue;
                }

                SerializedProperty rendererProp = GetFolderMaterialRendererProperty(folderIdx);
                int rendererCount = GetRendererArraySize(rendererProp);
                if (rendererCount == 0) continue;

                // Check each entry in this handler's folderEntries array against all configured renderers
                for (int r = 0; r < rendererCount; r++)
                {
                    Renderer renderer = GetRendererFromProperty(rendererProp, r);
                    if (renderer == null) continue;

                    for (int i = 0; i < folderEntriesProp.arraySize; i++)
                    {
                        var element = folderEntriesProp.GetArrayElementAtIndex(i);
                        Material material = element.objectReferenceValue as Material;
                        if (material == null) continue;

                        var key = new MaterialAssignmentKey(renderer, material);
                        if (!usageMap.TryGetValue(key, out var usage))
                        {
                            usage = new MaterialDuplicateUsage(renderer, material, folderIdx);
                            usageMap.Add(key, usage);
                        }
                        else
                        {
                            usage.AddFolder(folderIdx);
                        }
                    }
                }
            }

            foreach (var usage in usageMap.Values)
            {
                if (usage.folderIndices.Count <= 1) continue;

                usage.folderIndices.Sort();
                string rendererName = usage.renderer != null ? usage.renderer.name : "(Renderer)";
                string materialName = usage.material != null ? usage.material.name : "(Material)";
                string ownerName = GetFolderDisplayName(usage.ownerFolder);

                foreach (int folderIdx in usage.folderIndices)
                {
                    bool isOwner = folderIdx == usage.ownerFolder;
                    var list = GetOrCreateMessageList(result, folderIdx);
                    if (isOwner)
                    {
                        string others = BuildOtherFolderList(usage.folderIndices, folderIdx);
                        string msg = $"Owns material '{materialName}' on renderer '{rendererName}', also referenced by {others}.";
                        list.Add(new DuplicateMessage(msg, MessageType.Warning));
                    }
                    else
                    {
                        string msg = $"Material '{materialName}' on renderer '{rendererName}' is already owned by folder '{ownerName}'.";
                        list.Add(new DuplicateMessage(msg, MessageType.Error));
                    }
                }
            }

            return result;
        }
    }
}
#endif

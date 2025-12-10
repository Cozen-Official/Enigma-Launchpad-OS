#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ShaderPropertyType = UnityEditor.ShaderUtil.ShaderPropertyType;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty propertyHandlers;
        private readonly List<SerializedObject> propertyHandlerObjects = new List<SerializedObject>();
        private readonly List<int> propertyHandlerFolderIndices = new List<int>();

        private SerializedObject GetPropertyHandlerObjectForFolder(int folderIdx)
        {
            if (propertyHandlerObjects != null && propertyHandlerFolderIndices != null)
            {
                int handlerIndex = propertyHandlerFolderIndices.IndexOf(folderIdx);
                if (handlerIndex >= 0 && handlerIndex < propertyHandlerObjects.Count)
                {
                    return propertyHandlerObjects[handlerIndex];
                }
            }

            if (propertyHandlers == null)
            {
                return null;
            }

            for (int i = 0; i < propertyHandlers.arraySize; i++)
            {
                SerializedProperty element = propertyHandlers.GetArrayElementAtIndex(i);
                if (element == null || element.objectReferenceValue == null)
                {
                    continue;
                }

                if (element.objectReferenceValue is PropertyHandler handler && handler.folderIndex == folderIdx)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    return serializedHandler;
                }
            }

            return null;
        }

        private void EnsurePropertyHandlerParity()
        {
            propertyHandlerObjects.Clear();
            propertyHandlerFolderIndices.Clear();

            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || propertyHandlers == null)
            {
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);

            List<int> propertyFolders = GetPropertyFolderIndices();
            int propertyFolderCount = propertyFolders.Count;

            var existingHandlers = new List<PropertyHandler>();
            for (int i = 0; i < propertyHandlers.arraySize; i++)
            {
                SerializedProperty element = propertyHandlers.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is PropertyHandler handler)
                {
                    existingHandlers.Add(handler);
                }
            }

            foreach (PropertyHandler handler in launchpad.GetComponentsInChildren<PropertyHandler>(true))
            {
                if (handler != null && !existingHandlers.Contains(handler))
                {
                    existingHandlers.Add(handler);
                }
            }

            var assigned = new PropertyHandler[propertyFolderCount];
            var unused = new List<PropertyHandler>(existingHandlers);

            for (int i = 0; i < existingHandlers.Count; i++)
            {
                PropertyHandler handler = existingHandlers[i];
                if (handler == null)
                {
                    continue;
                }

                int slot = propertyFolders.IndexOf(handler.folderIndex);
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

                int folderIndex = propertyFolders[i];
                string handlerName = GetExpectedPropertyHandlerName(folderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create PropertyHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                PropertyHandler handler = handlerObject.AddComponent<PropertyHandler>();
                Undo.RecordObject(handler, "Configure PropertyHandler");
                handler.launchpad = launchpad;
                handler.folderIndex = folderIndex;
                assigned[i] = handler;
            }

            foreach (PropertyHandler handler in unused)
            {
                if (handler != null)
                {
                    Undo.DestroyObjectImmediate(handler.gameObject);
                }
            }

            propertyHandlers.arraySize = propertyFolderCount;
            for (int i = 0; i < assigned.Length; i++)
            {
                PropertyHandler handler = assigned[i];
                int folderIndex = propertyFolders[i];
                if (handler != null)
                {
                    Undo.RecordObject(handler, "Configure PropertyHandler");
                    handler.launchpad = launchpad;
                    handler.folderIndex = folderIndex;
                    handler.transform.SetParent(foldersTransform);
                    if (handler.gameObject.hideFlags != HandlerHideFlags)
                    {
                        handler.gameObject.hideFlags = HandlerHideFlags;
                    }

                    string expectedName = GetExpectedPropertyHandlerName(folderIndex);
                    if (handler.gameObject.name != expectedName)
                    {
                        Undo.RecordObject(handler.gameObject, "Rename PropertyHandler");
                        handler.gameObject.name = expectedName;
                    }
                }

                SerializedProperty element = propertyHandlers.GetArrayElementAtIndex(i);
                if (element != null)
                {
                    element.objectReferenceValue = handler;
                }

                propertyHandlerFolderIndices.Add(folderIndex);
                if (handler != null)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    propertyHandlerObjects.Add(serializedHandler);
                }
                else
                {
                    propertyHandlerObjects.Add(null);
                }
            }
        }

        private bool DrawPropertiesSection(int folderIdx, SerializedProperty exclusivityProperty, SerializedProperty countProp)
        {
            SerializedObject handlerObject = GetPropertyHandlerObjectForFolder(folderIdx);
            if (handlerObject == null)
            {
                EditorGUILayout.HelpBox("Properties handler missing or misconfigured. Re-select the launchpad to regenerate references.", MessageType.Error);
                return false;
            }

            handlerObject.Update();

            SerializedProperty rendererProp = handlerObject.FindProperty("propertyRenderers");
            SerializedProperty entriesProp = handlerObject.FindProperty("propertyEntries");
            SerializedProperty displayNamesProp = handlerObject.FindProperty("propertyDisplayNames");
            SerializedProperty materialIndicesProp = handlerObject.FindProperty("propertyMaterialIndices");
            SerializedProperty propertyNamesProp = handlerObject.FindProperty("propertyNames");
            SerializedProperty propertyTypesProp = handlerObject.FindProperty("propertyTypes");
            SerializedProperty floatValuesProp = handlerObject.FindProperty("propertyFloatValues");
            SerializedProperty colorValuesProp = handlerObject.FindProperty("propertyColorValues");
            SerializedProperty vectorValuesProp = handlerObject.FindProperty("propertyVectorValues");
            SerializedProperty textureValuesProp = handlerObject.FindProperty("propertyTextureValues");

            if (exclusivityProperty != null)
            {
                EditorGUILayout.PropertyField(exclusivityProperty, new GUIContent("Make Entries Exclusive"));
            }

            bool structuralChange = false;

            if (rendererProp != null)
            {
                EditorGUILayout.PropertyField(rendererProp, new GUIContent("Target Renderers"), true);

                if (rendererProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("Assign at least one Target Renderer to modify properties.", MessageType.Warning);
                }
            }

            EnsurePropertyEntryArraySizes(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, countProp);

            int count = Mathf.Max(0, countProp?.intValue ?? 0);

            EditorGUILayout.LabelField($"Properties ({count})", folderHeaderLabelStyle);
            GUILayout.Space(2);

            List<Renderer> rendererList = BuildRendererList(rendererProp);

            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                string displayName = GetArrayString(displayNamesProp, i);
                string propertyName = GetArrayString(propertyNamesProp, i);
                string headerName = !string.IsNullOrEmpty(displayName) ? displayName : propertyName;
                string headerLabel = string.IsNullOrEmpty(headerName) ? $"Property {i + 1}" : $"Property {i + 1}: {headerName}";
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MovePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i, i - 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i < count - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MovePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i, i + 1);
                    structuralChange = true;
                }

                GUI.enabled = true;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemovePropertyEntryAt(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i);
                    countProp.intValue = Mathf.Max(0, count - 1);
                    structuralChange = true;
                }

                EditorGUILayout.EndHorizontal();

                if (!structuralChange)
                {
                    SerializedProperty entryNameProp = GetArrayElement(entriesProp, i);
                    SerializedProperty displayNameProp = GetArrayElement(displayNamesProp, i);
                    string currentDisplay = displayNameProp != null ? displayNameProp.stringValue : string.Empty;
                    string updatedDisplay = EditorGUILayout.TextField(new GUIContent("Name"), currentDisplay);
                    if (displayNameProp != null && updatedDisplay != currentDisplay)
                    {
                        displayNameProp.stringValue = updatedDisplay;
                        if (entryNameProp != null)
                        {
                            entryNameProp.stringValue = updatedDisplay;
                        }
                    }
                    else if (entryNameProp != null && string.IsNullOrEmpty(entryNameProp.stringValue))
                    {
                        entryNameProp.stringValue = updatedDisplay;
                    }

                    SerializedProperty materialIndexProp = GetArrayElement(materialIndicesProp, i);
                    int matIndex = materialIndexProp != null ? materialIndexProp.intValue : 0;
                    int newMatIndex = EditorGUILayout.IntField(new GUIContent("Material Index"), matIndex);
                    if (materialIndexProp != null && newMatIndex != matIndex)
                    {
                        materialIndexProp.intValue = Mathf.Max(0, newMatIndex);
                    }

                    DrawPropertyDropdown(propertyNamesProp, propertyTypesProp, displayNameProp, i, rendererList, materialIndexProp?.intValue ?? 0);

                    int propType = GetArrayInt(propertyTypesProp, i);
                    propertyName = GetArrayString(propertyNamesProp, i);

                    if (propType == 1)
                    {
                        SerializedProperty colorProp = GetArrayElement(colorValuesProp, i);
                        TryAutoPopulateColorName(displayNameProp, colorProp != null ? colorProp.colorValue : Color.white, propertyName);
                    }

                    DrawPropertyValueField(floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, displayNameProp, propertyName, i, propType);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);

                if (structuralChange)
                {
                    break;
                }
            }

            if (structuralChange)
            {
                return true;
            }

            GUI.enabled = true;
            if (GUILayout.Button("Add Property", GUILayout.Height(24)))
            {
                AddPropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp);
                countProp.intValue = count + 1;
                structuralChange = true;
            }
            GUI.enabled = true;

            return structuralChange;
        }

        private void EnsurePropertyEntryArraySizes(
            SerializedProperty entriesProp,
            SerializedProperty displayNamesProp,
            SerializedProperty materialIndicesProp,
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp,
            SerializedProperty countProp)
        {
            int targetCount = Mathf.Max(0, countProp?.intValue ?? 0);
            targetCount = Mathf.Max(targetCount, entriesProp != null ? entriesProp.arraySize : 0);

            EnsureArraySize(entriesProp, targetCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(displayNamesProp, targetCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(materialIndicesProp, targetCount, prop => prop.intValue = 0);
            EnsureArraySize(propertyNamesProp, targetCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(propertyTypesProp, targetCount, prop => prop.intValue = 0);
            EnsureArraySize(floatValuesProp, targetCount, prop => prop.floatValue = 0f);
            EnsureArraySize(colorValuesProp, targetCount, prop => prop.colorValue = Color.white);
            EnsureArraySize(vectorValuesProp, targetCount, prop => prop.vector4Value = Vector4.zero);
            EnsureArraySize(textureValuesProp, targetCount, prop => prop.objectReferenceValue = null);

            if (countProp != null)
            {
                countProp.intValue = targetCount;
            }
        }

        private void MovePropertyEntry(
            SerializedProperty entriesProp,
            SerializedProperty displayNamesProp,
            SerializedProperty materialIndicesProp,
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp,
            int from,
            int to)
        {
            if (from < 0 || to < 0)
            {
                return;
            }

            entriesProp?.MoveArrayElement(from, to);
            displayNamesProp?.MoveArrayElement(from, to);
            materialIndicesProp?.MoveArrayElement(from, to);
            propertyNamesProp?.MoveArrayElement(from, to);
            propertyTypesProp?.MoveArrayElement(from, to);
            floatValuesProp?.MoveArrayElement(from, to);
            colorValuesProp?.MoveArrayElement(from, to);
            vectorValuesProp?.MoveArrayElement(from, to);
            textureValuesProp?.MoveArrayElement(from, to);
        }

        private void RemovePropertyEntryAt(
            SerializedProperty entriesProp,
            SerializedProperty displayNamesProp,
            SerializedProperty materialIndicesProp,
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp,
            int index)
        {
            DeleteArrayElement(entriesProp, index);
            DeleteArrayElement(displayNamesProp, index);
            DeleteArrayElement(materialIndicesProp, index);
            DeleteArrayElement(propertyNamesProp, index);
            DeleteArrayElement(propertyTypesProp, index);
            DeleteArrayElement(floatValuesProp, index);
            DeleteArrayElement(colorValuesProp, index);
            DeleteArrayElement(vectorValuesProp, index);
            DeleteArrayElement(textureValuesProp, index);
        }

        private void AddPropertyEntry(
            SerializedProperty entriesProp,
            SerializedProperty displayNamesProp,
            SerializedProperty materialIndicesProp,
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp)
        {
            int insertIndex = entriesProp != null ? entriesProp.arraySize : 0;
            EnsurePropertyEntryArraySizes(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, null);
            EnsureArraySize(entriesProp, insertIndex + 1, prop => prop.stringValue = string.Empty);
            EnsureArraySize(displayNamesProp, insertIndex + 1, prop => prop.stringValue = string.Empty);
            EnsureArraySize(materialIndicesProp, insertIndex + 1, prop => prop.intValue = 0);
            EnsureArraySize(propertyNamesProp, insertIndex + 1, prop => prop.stringValue = string.Empty);
            EnsureArraySize(propertyTypesProp, insertIndex + 1, prop => prop.intValue = 0);
            EnsureArraySize(floatValuesProp, insertIndex + 1, prop => prop.floatValue = 0f);
            EnsureArraySize(colorValuesProp, insertIndex + 1, prop => prop.colorValue = Color.white);
            EnsureArraySize(vectorValuesProp, insertIndex + 1, prop => prop.vector4Value = Vector4.zero);
            EnsureArraySize(textureValuesProp, insertIndex + 1, prop => prop.objectReferenceValue = null);
        }

        private SerializedProperty GetArrayElement(SerializedProperty prop, int index)
        {
            if (prop == null || index < 0 || index >= prop.arraySize)
            {
                return null;
            }

            return prop.GetArrayElementAtIndex(index);
        }

        private string GetArrayString(SerializedProperty prop, int index)
        {
            SerializedProperty element = GetArrayElement(prop, index);
            return element != null ? element.stringValue : string.Empty;
        }

        private int GetArrayInt(SerializedProperty prop, int index)
        {
            SerializedProperty element = GetArrayElement(prop, index);
            return element != null ? element.intValue : 0;
        }

        private List<Renderer> BuildRendererList(SerializedProperty rendererProp)
        {
            List<Renderer> rendererList = new List<Renderer>();
            if (rendererProp == null)
            {
                return rendererList;
            }

            for (int i = 0; i < rendererProp.arraySize; i++)
            {
                SerializedProperty element = rendererProp.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is Renderer renderer)
                {
                    rendererList.Add(renderer);
                }
            }

            return rendererList;
        }

        private void DrawPropertyDropdown(
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty displayNameProp,
            int entryIndex,
            List<Renderer> rendererList,
            int materialIndex)
        {
            Renderer[] rendererArray = rendererList?.ToArray();
            int[] materialIndices = rendererArray != null ? Enumerable.Repeat(materialIndex, rendererArray.Length).ToArray() : Array.Empty<int>();

            if (!TryBuildShaderPropertyOptions(rendererArray, materialIndices, out List<string> propertyNames, out List<ShaderPropertyType> propertyTypes, out string warning))
            {
                if (!string.IsNullOrEmpty(warning))
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                return;
            }

            SerializedProperty propertyNameProp = GetArrayElement(propertyNamesProp, entryIndex);
            string currentPropName = propertyNameProp != null ? propertyNameProp.stringValue : string.Empty;
            
            // Draw property selection with search button on single line
            // Match the layout behavior of EditorGUILayout.Popup to maintain consistent width
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Property"));
            string displayName = string.IsNullOrEmpty(currentPropName) ? "(None)" : currentPropName;
            GUILayout.Label(displayName, EditorStyles.textField);
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                // Build target structure for search window
                FaderShaderTarget target = new FaderShaderTarget
                {
                    renderers = rendererArray,
                    materialIndices = materialIndices,
                    directMaterials = null
                };
                
                OpenPropertySearchWindowForPropertyFolder(target, propertyNames, propertyTypes, (selectedName, selectedType) =>
                {
                    if (propertyNameProp != null)
                    {
                        propertyNameProp.stringValue = selectedName;
                    }
                    
                    SerializedProperty propertyTypeProp = GetArrayElement(propertyTypesProp, entryIndex);
                    if (propertyTypeProp != null)
                    {
                        propertyTypeProp.intValue = ShaderPropertyTypeToPropertyType(selectedType);
                    }
                    
                    if (displayNameProp != null)
                    {
                        string currentDisplay = displayNameProp.stringValue;
                        if (string.IsNullOrEmpty(currentDisplay) || currentDisplay == currentPropName)
                        {
                            displayNameProp.stringValue = selectedName;
                        }
                    }
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPropertyValueField(
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp,
            SerializedProperty displayNameProp,
            string propertyName,
            int entryIndex,
            int propType)
        {
            switch (propType)
            {
                case 0:
                    SerializedProperty floatProp = GetArrayElement(floatValuesProp, entryIndex);
                    if (floatProp != null)
                    {
                        EditorGUILayout.PropertyField(floatProp, new GUIContent("Value"));
                    }
                    break;
                case 1:
                    SerializedProperty colorProp = GetArrayElement(colorValuesProp, entryIndex);
                    if (colorProp != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(colorProp, new GUIContent("Value"));
                        if (EditorGUI.EndChangeCheck())
                        {
                            TryAutoPopulateColorName(displayNameProp, colorProp.colorValue, propertyName);
                        }
                    }
                    break;
                case 2:
                    SerializedProperty vectorProp = GetArrayElement(vectorValuesProp, entryIndex);
                    if (vectorProp != null)
                    {
                        EditorGUILayout.PropertyField(vectorProp, new GUIContent("Value"));
                    }
                    break;
                case 3:
                    SerializedProperty textureProp = GetArrayElement(textureValuesProp, entryIndex);
                    if (textureProp != null)
                    {
                        EditorGUILayout.ObjectField(textureProp, new GUIContent("Value"));
                    }
                    break;
            }
        }

        private void TryAutoPopulateColorName(SerializedProperty displayNameProp, Color color, string propertyName = null)
        {
            if (displayNameProp == null)
            {
                return;
            }

            string current = displayNameProp.stringValue;

            bool allowUpdate = string.IsNullOrEmpty(current)
                || LooksLikeAutoGeneratedColorName(current)
                || (!string.IsNullOrEmpty(propertyName) && current == propertyName);

            if (!allowUpdate) return;

            displayNameProp.stringValue = GetMatchedColorName(color);
        }

        private bool TryBuildShaderPropertyOptions(
            Renderer[] renderers,
            int[] materialIndices,
            out List<string> propertyNames,
            out List<ShaderPropertyType> propertyTypes,
            out string warning)
        {
            propertyNames = null;
            propertyTypes = null;
            warning = null;

            if (renderers == null || renderers.Length == 0)
            {
                warning = "Add at least one Target Renderer to select properties.";
                return false;
            }

            if (materialIndices == null || materialIndices.Length != renderers.Length)
            {
                warning = "Material index array must align with renderers.";
                return false;
            }

            for (int idx = 0; idx < renderers.Length; idx++)
            {
                Renderer renderer = renderers[idx];
                if (renderer == null)
                {
                    warning = $"Renderer {idx + 1} is not assigned.";
                    return false;
                }

                int materialIndex = materialIndices[idx];
                Material targetMaterial = ResolveTargetMaterial(renderer, materialIndex, out string materialWarning);
                if (materialWarning != null)
                {
                    warning = materialWarning;
                    return false;
                }

                if (targetMaterial.shader == null)
                {
                    warning = $"Renderer '{renderer.name}' material has no shader.";
                    return false;
                }
            }

            Dictionary<string, ShaderPropertyType> sharedProperties = GetCommonShaderProperties(renderers, materialIndices, out List<string> propertyOrder);
            if (sharedProperties == null || sharedProperties.Count == 0)
            {
                warning = "No shared shader properties found across all target renderers.";
                return false;
            }

            propertyOrder = propertyOrder?.Where(sharedProperties.ContainsKey).ToList();

            propertyNames = propertyOrder ?? sharedProperties.Keys.OrderBy(name => name, System.StringComparer.Ordinal).ToList();
            propertyTypes = propertyNames.Select(name => sharedProperties[name]).ToList();
            return true;
        }

        private Dictionary<string, ShaderPropertyType> GetCommonShaderProperties(Renderer[] renderers, int[] materialIndices, out List<string> orderedKeys)
        {
            orderedKeys = null;
            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            Material firstMaterial = ResolveTargetMaterial(renderers[0], materialIndices[0], out _);
            Dictionary<string, ShaderPropertyType> shared = CollectShaderProperties(firstMaterial, out List<string> baseOrder);
            orderedKeys = baseOrder;

            for (int i = 1; i < renderers.Length && shared != null; i++)
            {
                Material material = ResolveTargetMaterial(renderers[i], materialIndices[i], out _);
                Dictionary<string, ShaderPropertyType> props = CollectShaderProperties(material, out _);
                if (props == null)
                {
                    continue;
                }

                shared = shared
                    .Where(pair => props.TryGetValue(pair.Key, out ShaderPropertyType otherType) && otherType == pair.Value)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                if (orderedKeys != null)
                {
                    orderedKeys = orderedKeys.Where(shared.ContainsKey).ToList();
                }
            }

            return shared;
        }

        private Dictionary<string, ShaderPropertyType> CollectShaderProperties(Material material, out List<string> propertyOrder)
        {
            propertyOrder = null;
            if (material == null || material.shader == null)
            {
                return null;
            }

            var properties = new Dictionary<string, ShaderPropertyType>();
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            propertyOrder = new List<string>(propertyCount);

            for (int i = 0; i < propertyCount; i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, i);
                if (!properties.ContainsKey(name))
                {
                    properties.Add(name, type);
                    propertyOrder.Add(name);
                }
            }

            return properties;
        }

        private Material ResolveTargetMaterial(Renderer renderer, int materialIndex, out string warning)
        {
            warning = null;
            if (renderer == null)
            {
                warning = "Renderer is null.";
                return null;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                warning = $"Renderer '{renderer.name}' has no materials.";
                return null;
            }

            if (materialIndex < 0 || materialIndex >= materials.Length)
            {
                warning = $"Renderer '{renderer.name}' has {materials.Length} materials; index {materialIndex} is out of range.";
                return null;
            }

            Material material = materials[materialIndex];
            if (material == null)
            {
                warning = $"Renderer '{renderer.name}' material at index {materialIndex} is missing.";
                return null;
            }

            return material;
        }

        private int ShaderPropertyTypeToPropertyType(ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return 0;
                case ShaderPropertyType.Color:
                    return 1;
                case ShaderPropertyType.Vector:
                    return 2;
                case ShaderPropertyType.TexEnv:
                    return 3;
                default:
                    return 0;
            }
        }

        private List<int> GetPropertyFolderIndices()
        {
            var indices = new List<int>();
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;
            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (GetFolderTypeFromProp(typeProp) == ToggleFolderType.Properties)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private string GetExpectedPropertyHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"PropertyHandler_{folderName}";
        }

        // ==================== Property Search Window ====================

        /// <summary>
        /// Opens a search window for selecting shader properties for the Property folder
        /// </summary>
        private void OpenPropertySearchWindowForPropertyFolder(
            FaderShaderTarget target,
            List<string> propertyNames,
            List<ShaderPropertyType> propertyTypes,
            Action<string, ShaderPropertyType> onSelect)
        {
            var searchWindow = new PropertySearchWindow("Shader Properties");
            var mainGroup = searchWindow.GetMainGroup();
            
            // Build property map for quick lookup
            // Note: propertyMap uses UnityEngine.Rendering.ShaderPropertyType because shader.GetPropertyType() returns that type
            var propertyMap = new Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>();
            for (int i = 0; i < propertyNames.Count && i < propertyTypes.Count; i++)
            {
                propertyMap[propertyNames[i]] = (UnityEngine.Rendering.ShaderPropertyType)(int)propertyTypes[i];
            }

            // Since propertyNames already contains only shared properties,
            // we don't need to group by renderer - just show them directly
            // Get a representative material to extract property descriptions
            Material representativeMaterial = null;
            if (target.renderers != null && target.renderers.Length > 0)
            {
                int matIndex = target.materialIndices != null && target.materialIndices.Length > 0 
                    ? target.materialIndices[0] 
                    : 0;
                representativeMaterial = GetRendererMaterial(target.renderers[0], matIndex);
            }

            if (representativeMaterial != null)
            {
                AddPropertiesFromMaterialForPropertyFolder(mainGroup, representativeMaterial, propertyMap);
            }

            searchWindow.Open(propName => {
                if (propertyMap.TryGetValue(propName, out UnityEngine.Rendering.ShaderPropertyType propType))
                {
                    onSelect(propName, (ShaderPropertyType)(int)propType);
                    // Apply changes immediately and force repaint
                    serializedObject.ApplyModifiedProperties();
                    Repaint();
                }
            });
        }

        /// <summary>
        /// Adds properties from a material to a search window group for Property folder
        /// </summary>
        private void AddPropertiesFromMaterialForPropertyFolder(
            PropertySearchWindow.Group group,
            Material material,
            Dictionary<string, UnityEngine.Rendering.ShaderPropertyType> propertyMap)
        {
            if (material == null || material.shader == null) return;
            
            Shader shader = material.shader;
            int propCount = shader.GetPropertyCount();
            
            for (int i = 0; i < propCount; i++)
            {
                string propName = shader.GetPropertyName(i);
                
                // Only include properties that are in the available property map
                if (!propertyMap.ContainsKey(propName)) continue;
                
                UnityEngine.Rendering.ShaderPropertyType propType = shader.GetPropertyType(i);
                string description = shader.GetPropertyDescription(i);
                
                // Skip hidden properties
                var flags = shader.GetPropertyFlags(i);
                if ((flags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0)
                    continue;
                
                // Use description as display name if available, otherwise use property name
                string displayName = string.IsNullOrEmpty(description) ? propName : description;
                
                // Create full entry with property name suffix for clarity
                string entryName = displayName == propName 
                    ? propName 
                    : $"{displayName} ({propName})";
                
                // Add type indicator
                string typeIndicator = GetPropertyTypeIndicatorForPropertyFolder(propType);
                if (!string.IsNullOrEmpty(typeIndicator))
                {
                    entryName += $" [{typeIndicator}]";
                }
                
                group.Add(entryName, propName);
            }
        }

        /// <summary>
        /// Gets a short indicator string for the property type for Property folder
        /// </summary>
        private string GetPropertyTypeIndicatorForPropertyFolder(UnityEngine.Rendering.ShaderPropertyType propType)
        {
            switch (propType)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    return "Color";
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    return "Vector";
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                    return "Float";
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    return "Range";
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    return "Texture";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Helper to get material from renderer at specific index
        /// </summary>
        private Material GetRendererMaterial(Renderer renderer, int materialIndex)
        {
            if (renderer == null) return null;
            
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materialIndex < 0 || materialIndex >= materials.Length)
            {
                return null;
            }
            
            return materials[materialIndex];
        }
    }
}
#endif

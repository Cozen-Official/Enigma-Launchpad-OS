#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty shaderHandlers;
        private readonly List<SerializedObject> shaderHandlerObjects = new List<SerializedObject>();
        private readonly List<int> shaderHandlerFolderIndices = new List<int>();

        private SerializedObject GetShaderHandlerObjectForFolder(int folderIdx)
        {
            if (shaderHandlerObjects != null && shaderHandlerFolderIndices != null)
            {
                int handlerIndex = shaderHandlerFolderIndices.IndexOf(folderIdx);
                if (handlerIndex >= 0 && handlerIndex < shaderHandlerObjects.Count)
                {
                    return shaderHandlerObjects[handlerIndex];
                }
            }

            // Fallback: search the serialized shaderHandlers array for a matching folder index.
            if (shaderHandlers == null)
            {
                return null;
            }

            for (int i = 0; i < shaderHandlers.arraySize; i++)
            {
                SerializedProperty element = shaderHandlers.GetArrayElementAtIndex(i);
                if (element == null || element.objectReferenceValue == null)
                {
                    continue;
                }

                if (element.objectReferenceValue is ShaderHandler handler && handler.folderIndex == folderIdx)
                {
                    var serializedHandler = new SerializedObject(handler);
                    serializedHandler.Update();
                    return serializedHandler;
                }
            }

            return null;
        }

        private void EnsureShaderHandlerParity()
        {
            shaderHandlerObjects.Clear();
            shaderHandlerFolderIndices.Clear();

            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || shaderHandlers == null)
            {
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            List<int> shaderFolders = GetShaderFolderIndices();
            int shaderFolderCount = shaderFolders.Count;

            // Create one ShaderHandler per Shader folder
            int requiredHandlerCount = shaderFolderCount;

            var existingHandlers = new List<ShaderHandler>();
            for (int i = 0; i < shaderHandlers.arraySize; i++)
            {
                SerializedProperty element = shaderHandlers.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is ShaderHandler handler)
                {
                    existingHandlers.Add(handler);
                }
            }

            foreach (ShaderHandler handler in launchpad.GetComponentsInChildren<ShaderHandler>(true))
            {
                if (handler != null && !existingHandlers.Contains(handler))
                {
                    existingHandlers.Add(handler);
                }
            }

            var assigned = new ShaderHandler[requiredHandlerCount];
            var unused = new List<ShaderHandler>(existingHandlers);

            for (int i = 0; i < existingHandlers.Count; i++)
            {
                ShaderHandler handler = existingHandlers[i];
                if (handler == null)
                {
                    continue;
                }

                int folderIdx = handler.folderIndex;
                int position = shaderFolders.IndexOf(folderIdx);
                if (position >= 0 && position < assigned.Length && assigned[position] == null)
                {
                    assigned[position] = handler;
                    unused.Remove(handler);
                }
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] == null && unused.Count > 0)
                {
                    assigned[i] = unused[0];
                    unused.RemoveAt(0);
                }
            }

            foreach (ShaderHandler unusedHandler in unused)
            {
                if (unusedHandler != null)
                {
                    DestroyImmediate(unusedHandler);
                }
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                int folderIdx = shaderFolders[i];
                ShaderHandler handler = assigned[i];

                if (handler == null)
                {
                    GameObject handlerGO = new GameObject($"ShaderHandler_Folder{folderIdx}");
                    handlerGO.transform.SetParent(foldersTransform, false);
                    handler = handlerGO.AddComponent<ShaderHandler>();
                    handler.hideFlags = HandlerHideFlags;
                    assigned[i] = handler;
                }

                handler.folderIndex = folderIdx;
                handler.launchpad = launchpad;
                EditorUtility.SetDirty(handler);
            }

            shaderHandlers.arraySize = assigned.Length;
            for (int i = 0; i < assigned.Length; i++)
            {
                SerializedProperty element = shaderHandlers.GetArrayElementAtIndex(i);
                element.objectReferenceValue = assigned[i];

                if (assigned[i] != null)
                {
                    var handlerObject = new SerializedObject(assigned[i]);
                    handlerObject.Update();
                    shaderHandlerObjects.Add(handlerObject);
                    shaderHandlerFolderIndices.Add(shaderFolders[i]);
                }
            }
        }

        private List<int> GetShaderFolderIndices()
        {
            var indices = new List<int>();
            if (folderTypesProperty == null)
            {
                return indices;
            }

            for (int i = 0; i < folderTypesProperty.arraySize; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (typeProp == null)
                {
                    continue;
                }

                ToggleFolderType type = GetFolderTypeFromProp(typeProp);
                if (type == ToggleFolderType.Shaders)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private bool DrawShaderFolderSection(int folderIdx, SerializedProperty exclProp, SerializedProperty countProp, ref bool structural)
        {
            ToggleFolderType folderType = GetFolderType(folderIdx);
            if (folderType != ToggleFolderType.Shaders)
            {
                return false;
            }

            SerializedObject handlerObject = GetShaderHandlerObjectForFolder(folderIdx);
            if (handlerObject == null)
            {
                EditorGUILayout.HelpBox("Shader handler not initialized. This should not happen.", MessageType.Error);
                EditorGUI.indentLevel--;
                return true;
            }

            SerializedProperty templateGameObjectProp = handlerObject.FindProperty("templateGameObject");
            SerializedProperty shaderMaterialsProp = handlerObject.FindProperty("shaderMaterials");
            SerializedProperty shaderNamesProp = handlerObject.FindProperty("shaderNames");
            SerializedProperty defaultShaderIndexProp = handlerObject.FindProperty("defaultShaderIndex");

            GUILayout.Space(6);

            // GameObject reference
            EditorGUILayout.PropertyField(templateGameObjectProp, new GUIContent("Template GameObject"));
            if (templateGameObjectProp.objectReferenceValue != null)
            {
                GameObject templateGO = templateGameObjectProp.objectReferenceValue as GameObject;
                if (templateGO != null)
                {
                    MeshRenderer meshRenderer = templateGO.GetComponent<MeshRenderer>();
                    if (meshRenderer == null)
                    {
                        EditorGUILayout.HelpBox("Warning: The template GameObject does not have a MeshRenderer component. Please assign a GameObject with a MeshRenderer.", MessageType.Warning);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Please assign a GameObject with a MeshRenderer sized for the room. This will be used as a template - it will be renamed to 'Template' and set to EditorOnly tag. New runtime GameObjects will be created as duplicates for each shader material.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(exclProp, new GUIContent("Make Entries Exclusive"));

            GUILayout.Space(6);

            // Materials drag and drop zone
            EditorGUILayout.LabelField("Shader Materials", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag and drop materials with screen shaders here. Each material will get its own GameObject instance.", MessageType.Info);

            Rect dropRect = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag & Drop Materials Here", dragZoneStyle ?? EditorStyles.helpBox);

            Event evt = Event.current;
            if (dropRect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
                {
                    bool anyValid = false;
                    foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj is Material)
                        {
                            anyValid = true;
                            break;
                        }
                    }

                    if (anyValid)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                            {
                                if (obj is Material material)
                                {
                                    int newIndex = shaderMaterialsProp.arraySize;
                                    shaderMaterialsProp.arraySize = newIndex + 1;
                                    shaderNamesProp.arraySize = newIndex + 1;

                                    SerializedProperty matElement = shaderMaterialsProp.GetArrayElementAtIndex(newIndex);
                                    SerializedProperty nameElement = shaderNamesProp.GetArrayElementAtIndex(newIndex);

                                    matElement.objectReferenceValue = material;
                                    nameElement.stringValue = material.name;

                                    structural = true;
                                }
                            }
                            handlerObject.ApplyModifiedProperties();
                            countProp.intValue = shaderMaterialsProp.arraySize;
                        }
                        evt.Use();
                    }
                }
            }

            GUILayout.Space(4);

            // Display parallel arrays
            if (shaderMaterialsProp.arraySize > 0)
            {
                EditorGUILayout.LabelField($"Shader Entries ({shaderMaterialsProp.arraySize})", EditorStyles.boldLabel);

                for (int i = 0; i < shaderMaterialsProp.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    SerializedProperty matElement = shaderMaterialsProp.GetArrayElementAtIndex(i);
                    SerializedProperty nameElement = shaderNamesProp.GetArrayElementAtIndex(i);

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.PropertyField(nameElement, new GUIContent($"Name {i}"));
                    EditorGUILayout.PropertyField(matElement, new GUIContent($"Material {i}"));
                    EditorGUILayout.EndVertical();

                    // Up arrow
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(22)))
                    {
                        MoveShaderEntry(handlerObject, i, i - 1);
                        structural = true;
                        handlerObject.ApplyModifiedProperties();
                        break;
                    }
                    
                    // Down arrow
                    GUI.enabled = !structural && i < shaderMaterialsProp.arraySize - 1;
                    if (GUILayout.Button("▼", GUILayout.Width(22)))
                    {
                        MoveShaderEntry(handlerObject, i, i + 1);
                        structural = true;
                        handlerObject.ApplyModifiedProperties();
                        break;
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        // Check if material slot has a reference before deleting
                        bool hasReference = matElement.objectReferenceValue != null;
                        
                        // Temporarily disable Unity's logger to suppress benign "out of bounds" warnings
                        // that Unity logs during SerializedProperty array deletion
                        bool wasLogEnabled = Debug.unityLogger.logEnabled;
                        Debug.unityLogger.logEnabled = false;
                        
                        try
                        {
                            // Delete material - first call nulls the reference if it exists, or removes if already null
                            shaderMaterialsProp.DeleteArrayElementAtIndex(i);
                            
                            // If material had a reference, the first delete only nulled it, so delete again to remove
                            if (hasReference)
                            {
                                shaderMaterialsProp.DeleteArrayElementAtIndex(i);
                            }
                            
                            // Now delete the corresponding name entry (materials array is already reduced)
                            shaderNamesProp.DeleteArrayElementAtIndex(i);
                        }
                        finally
                        {
                            // Re-enable logger
                            Debug.unityLogger.logEnabled = wasLogEnabled;
                        }
                        
                        structural = true;
                        handlerObject.ApplyModifiedProperties();
                        countProp.intValue = shaderMaterialsProp.arraySize;
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(2);
                }
            }

            GUILayout.Space(6);

            // Default shader dropdown
            EditorGUILayout.LabelField("Default Shader", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select which shader should be active by default. Choose 'None' to disable all shaders at start.", MessageType.Info);

            int currentDefaultIndex = defaultShaderIndexProp.intValue;
            string[] options = new string[shaderNamesProp.arraySize + 1];
            options[0] = "None";
            for (int i = 0; i < shaderNamesProp.arraySize; i++)
            {
                SerializedProperty nameElement = shaderNamesProp.GetArrayElementAtIndex(i);
                string name = nameElement.stringValue;
                if (string.IsNullOrEmpty(name))
                {
                    name = $"Shader {i}";
                }
                options[i + 1] = name;
            }

            int selectedIndex = currentDefaultIndex + 1; // +1 because "None" is at index 0
            selectedIndex = Mathf.Clamp(selectedIndex, 0, options.Length - 1);
            int newSelectedIndex = EditorGUILayout.Popup("Default Shader", selectedIndex, options);

            if (newSelectedIndex != selectedIndex)
            {
                defaultShaderIndexProp.intValue = newSelectedIndex - 1; // -1 to convert back from dropdown index
                handlerObject.ApplyModifiedProperties();
            }

            handlerObject.ApplyModifiedProperties();
            
            // Synchronize shader GameObjects with materials
            SynchronizeShaderGameObjects(handlerObject);
            
            EditorGUI.indentLevel--;
            return true;
        }

        private void SynchronizeShaderGameObjects(SerializedObject handlerObject)
        {
            if (handlerObject == null) return;

            SerializedProperty templateGameObjectProp = handlerObject.FindProperty("templateGameObject");
            SerializedProperty shaderMaterialsProp = handlerObject.FindProperty("shaderMaterials");
            SerializedProperty shaderNamesProp = handlerObject.FindProperty("shaderNames");
            SerializedProperty shaderGameObjectsProp = handlerObject.FindProperty("shaderGameObjects");
            SerializedProperty defaultShaderIndexProp = handlerObject.FindProperty("defaultShaderIndex");

            GameObject templateGO = templateGameObjectProp.objectReferenceValue as GameObject;
            if (templateGO == null)
            {
                // No template, clear gameobjects array
                ClearShaderGameObjects(shaderGameObjectsProp);
                handlerObject.ApplyModifiedProperties();
                return;
            }

            MeshRenderer templateRenderer = templateGO.GetComponent<MeshRenderer>();
            if (templateRenderer == null)
            {
                // No mesh renderer on template
                ClearShaderGameObjects(shaderGameObjectsProp);
                handlerObject.ApplyModifiedProperties();
                return;
            }

            int materialCount = shaderMaterialsProp.arraySize;
            if (materialCount == 0)
            {
                // No materials, clear gameobjects array
                ClearShaderGameObjects(shaderGameObjectsProp);
                handlerObject.ApplyModifiedProperties();
                return;
            }

            // Configure template: rename to "Template" and set to EditorOnly
            if (templateGO.name != "Template")
            {
                templateGO.name = "Template";
                EditorUtility.SetDirty(templateGO);
            }
            if (templateGO.tag != "EditorOnly")
            {
                templateGO.tag = "EditorOnly";
                EditorUtility.SetDirty(templateGO);
            }

            // Collect existing shader GameObjects that we own (created duplicates)
            List<GameObject> existingShaderGOs = new List<GameObject>();
            for (int i = 0; i < shaderGameObjectsProp.arraySize; i++)
            {
                SerializedProperty goProp = shaderGameObjectsProp.GetArrayElementAtIndex(i);
                GameObject go = goProp.objectReferenceValue as GameObject;
                if (go != null)
                {
                    existingShaderGOs.Add(go);
                }
            }

            // Build the new list of shader GameObjects (all duplicates, never use template)
            List<GameObject> shaderGOs = new List<GameObject>();

            for (int i = 0; i < materialCount; i++)
            {
                GameObject shaderGO = null;

                // Try to reuse an existing duplicate
                if (existingShaderGOs.Count > 0)
                {
                    shaderGO = existingShaderGOs[0];
                    existingShaderGOs.RemoveAt(0);
                }
                else
                {
                    // Create a new duplicate of the template
                    shaderGO = UnityEngine.Object.Instantiate(templateGO);
                    
                    // Set to Untagged (not EditorOnly like the template)
                    shaderGO.tag = "Untagged";
                    
                    // Place it as a sibling of the template (same parent, right after template)
                    shaderGO.transform.SetParent(templateGO.transform.parent, true);
                    shaderGO.transform.SetSiblingIndex(templateGO.transform.GetSiblingIndex() + 1 + i);
                    
                    // Copy transform from template
                    shaderGO.transform.localPosition = templateGO.transform.localPosition;
                    shaderGO.transform.localRotation = templateGO.transform.localRotation;
                    shaderGO.transform.localScale = templateGO.transform.localScale;
                }

                // Name the GameObject based on the shader name
                SerializedProperty nameElement = shaderNamesProp.GetArrayElementAtIndex(i);
                string shaderName = nameElement.stringValue;
                if (string.IsNullOrEmpty(shaderName))
                {
                    shaderName = $"Shader {i}";
                }
                shaderGO.name = shaderName;

                shaderGOs.Add(shaderGO);
            }

            // Destroy any leftover GameObjects that we no longer need
            foreach (GameObject leftover in existingShaderGOs)
            {
                if (leftover != null)
                {
                    UnityEngine.Object.DestroyImmediate(leftover);
                }
            }

            // Update the array
            shaderGameObjectsProp.arraySize = shaderGOs.Count;
            for (int i = 0; i < shaderGOs.Count; i++)
            {
                SerializedProperty goProp = shaderGameObjectsProp.GetArrayElementAtIndex(i);
                goProp.objectReferenceValue = shaderGOs[i];
            }

            // Apply materials to each GameObject's MeshRenderer
            for (int i = 0; i < materialCount; i++)
            {
                SerializedProperty matElement = shaderMaterialsProp.GetArrayElementAtIndex(i);
                Material material = matElement.objectReferenceValue as Material;
                
                if (material != null && i < shaderGOs.Count)
                {
                    GameObject shaderGO = shaderGOs[i];
                    if (shaderGO != null)
                    {
                        MeshRenderer renderer = shaderGO.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                            renderer.sharedMaterial = material;
                            EditorUtility.SetDirty(renderer);
                        }
                    }
                }
            }

            // Set default shader states
            int defaultIndex = defaultShaderIndexProp.intValue;
            for (int i = 0; i < shaderGOs.Count; i++)
            {
                GameObject shaderGO = shaderGOs[i];
                if (shaderGO != null)
                {
                    bool shouldBeActive = (i == defaultIndex && defaultIndex >= 0);
                    shaderGO.SetActive(shouldBeActive);
                    EditorUtility.SetDirty(shaderGO);
                }
            }

            handlerObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(handlerObject.targetObject);
        }

        private void ClearShaderGameObjects(SerializedProperty shaderGameObjectsProp)
        {
            // Destroy all managed GameObjects (all are duplicates now)
            for (int i = 0; i < shaderGameObjectsProp.arraySize; i++)
            {
                SerializedProperty goProp = shaderGameObjectsProp.GetArrayElementAtIndex(i);
                GameObject go = goProp.objectReferenceValue as GameObject;
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            shaderGameObjectsProp.arraySize = 0;
        }

        private List<ToggleOption> BuildShaderToggleOptions(int folderIndex)
        {
            List<ToggleOption> options = new List<ToggleOption>();

            SerializedObject handlerObject = GetShaderHandlerObjectForFolder(folderIndex);
            if (handlerObject == null)
            {
                return options;
            }

            SerializedProperty shaderNamesProp = handlerObject.FindProperty("shaderNames");
            if (shaderNamesProp == null || !shaderNamesProp.isArray)
            {
                return options;
            }

            int count = shaderNamesProp.arraySize;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty nameElement = shaderNamesProp.GetArrayElementAtIndex(i);
                string label = nameElement != null ? nameElement.stringValue : string.Empty;
                
                if (string.IsNullOrEmpty(label))
                {
                    label = $"Shader {i + 1}";
                }
                else
                {
                    label = ButtonHandler.FormatName(label);
                }

                options.Add(new ToggleOption
                {
                    Value = i,
                    Label = label
                });
            }

            return options;
        }

        private FaderShaderTarget BuildShadersFolderShaderTarget(int folderIndex, int toggleIndex)
        {
            SerializedObject handlerObject = GetShaderHandlerObjectForFolder(folderIndex);
            if (handlerObject == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            SerializedProperty shaderMaterialsProp = handlerObject.FindProperty("shaderMaterials");
            if (shaderMaterialsProp == null || !shaderMaterialsProp.isArray)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // If toggleIndex is specified, only use that single shader material
            if (toggleIndex >= 0 && toggleIndex < shaderMaterialsProp.arraySize)
            {
                SerializedProperty matProp = shaderMaterialsProp.GetArrayElementAtIndex(toggleIndex);
                Material mat = matProp != null ? matProp.objectReferenceValue as Material : null;
                if (mat != null)
                {
                    return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0, new[] { mat });
                }
            }

            // Otherwise, use all shader materials for property inspection
            List<Material> materials = new List<Material>();
            for (int i = 0; i < shaderMaterialsProp.arraySize; i++)
            {
                SerializedProperty matProp = shaderMaterialsProp.GetArrayElementAtIndex(i);
                Material mat = matProp != null ? matProp.objectReferenceValue as Material : null;
                if (mat != null)
                {
                    materials.Add(mat);
                }
            }

            return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0, materials.ToArray());
        }

        private void MoveShaderEntry(SerializedObject handlerObject, int fromIndex, int toIndex)
        {
            if (handlerObject == null) return;

            SerializedProperty shaderMaterialsProp = handlerObject.FindProperty("shaderMaterials");
            SerializedProperty shaderNamesProp = handlerObject.FindProperty("shaderNames");
            SerializedProperty shaderGameObjectsProp = handlerObject.FindProperty("shaderGameObjects");

            if (shaderMaterialsProp == null || shaderNamesProp == null || shaderGameObjectsProp == null)
                return;

            if (fromIndex < 0 || fromIndex >= shaderMaterialsProp.arraySize || 
                toIndex < 0 || toIndex >= shaderMaterialsProp.arraySize || 
                fromIndex == toIndex)
                return;

            // Move arrays using MoveArrayElement
            shaderMaterialsProp.MoveArrayElement(fromIndex, toIndex);
            shaderNamesProp.MoveArrayElement(fromIndex, toIndex);
            shaderGameObjectsProp.MoveArrayElement(fromIndex, toIndex);
        }
    }
}
#endif

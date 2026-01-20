#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using UdonSharp;
using UdonSharp.Compiler;
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

            SerializedProperty entriesProp = handlerObject.FindProperty("propertyEntries");
            SerializedProperty displayNamesProp = handlerObject.FindProperty("propertyDisplayNames");
            SerializedProperty materialIndicesProp = handlerObject.FindProperty("propertyMaterialIndices");
            SerializedProperty propertyNamesProp = handlerObject.FindProperty("propertyNames");
            SerializedProperty propertyTypesProp = handlerObject.FindProperty("propertyTypes");
            SerializedProperty floatValuesProp = handlerObject.FindProperty("propertyFloatValues");
            SerializedProperty colorValuesProp = handlerObject.FindProperty("propertyColorValues");
            SerializedProperty vectorValuesProp = handlerObject.FindProperty("propertyVectorValues");
            SerializedProperty textureValuesProp = handlerObject.FindProperty("propertyTextureValues");
            
            // Shader property targeting (per-entry)
            SerializedProperty targetsShaderProp = handlerObject.FindProperty("propertyTargetsShader");
            SerializedProperty shaderRenderersProp = handlerObject.FindProperty("propertyShaderRenderers");
            SerializedProperty shaderRendererCountsProp = handlerObject.FindProperty("propertyShaderRendererCounts");

            // UdonBehaviour properties
            SerializedProperty targetsUdonProp = handlerObject.FindProperty("propertyTargetsUdon");
            SerializedProperty udonBehavioursProp = handlerObject.FindProperty("propertyUdonBehaviours");
            SerializedProperty udonCountsProp = handlerObject.FindProperty("propertyUdonCounts");
            SerializedProperty udonVariableNamesProp = handlerObject.FindProperty("propertyUdonVariableNames");

            // Unity Slider targeting (per-entry)
            SerializedProperty targetsSliderProp = handlerObject.FindProperty("propertyTargetsSlider");
            SerializedProperty slidersProp = handlerObject.FindProperty("propertySliders");
            SerializedProperty sliderCountsProp = handlerObject.FindProperty("propertySliderCounts");
            SerializedProperty sliderReversedProp = handlerObject.FindProperty("propertySliderReversed");

            if (exclusivityProperty != null)
            {
                EditorGUILayout.PropertyField(exclusivityProperty, new GUIContent("Make Entries Exclusive"));
            }

            bool structuralChange = false;

            EnsurePropertyEntryArraySizes(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, countProp);
            EnsurePropertyShaderArraySizes(targetsShaderProp, shaderRendererCountsProp, countProp);
            EnsurePropertyUdonArraySizes(targetsUdonProp, udonCountsProp, udonVariableNamesProp, countProp);
            EnsurePropertySliderArraySizes(targetsSliderProp, sliderCountsProp, countProp);

            int count = Mathf.Max(0, countProp?.intValue ?? 0);

            EditorGUILayout.LabelField($"Properties ({count})", folderHeaderLabelStyle);
            GUILayout.Space(2);

            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                string displayName = GetArrayString(displayNamesProp, i);
                string propertyName = GetArrayString(propertyNamesProp, i);
                string udonVarName = GetArrayString(udonVariableNamesProp, i);
                bool isShaderEntry = GetArrayBool(targetsShaderProp, i);
                bool isUdonEntry = GetArrayBool(targetsUdonProp, i);
                string headerName = !string.IsNullOrEmpty(displayName) ? displayName : (isUdonEntry ? udonVarName : propertyName);
                string headerLabel = string.IsNullOrEmpty(headerName) ? $"Property {i + 1}" : $"Property {i + 1}: {headerName}";
                
                // Add target indicators to header
                List<string> targetIndicators = new List<string>();
                if (isShaderEntry) targetIndicators.Add("Shader");
                if (isUdonEntry) targetIndicators.Add("Udon");
                if (targetIndicators.Count > 0)
                {
                    headerLabel += " [" + string.Join("+", targetIndicators) + "]";
                }
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button(DuplicateFaderButtonContent, GUILayout.Width(22)))
                {
                    DuplicatePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp,
                        targetsShaderProp, shaderRendererCountsProp, shaderRenderersProp,
                        targetsUdonProp, udonCountsProp, udonVariableNamesProp, udonBehavioursProp,
                        i, countProp);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i > 0;
                if (!structuralChange && GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MovePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i, i - 1);
                    MovePropertyShaderEntry(targetsShaderProp, shaderRendererCountsProp, i, i - 1);
                    MovePropertyUdonEntry(targetsUdonProp, udonCountsProp, udonVariableNamesProp, udonBehavioursProp, i, i - 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i < count - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MovePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i, i + 1);
                    MovePropertyShaderEntry(targetsShaderProp, shaderRendererCountsProp, i, i + 1);
                    MovePropertyUdonEntry(targetsUdonProp, udonCountsProp, udonVariableNamesProp, udonBehavioursProp, i, i + 1);
                    structuralChange = true;
                }

                GUI.enabled = true;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemovePropertyEntryAt(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, i);
                    RemovePropertyShaderEntryAt(targetsShaderProp, shaderRendererCountsProp, shaderRenderersProp, i);
                    RemovePropertyUdonEntryAt(targetsUdonProp, udonCountsProp, udonVariableNamesProp, udonBehavioursProp, i);
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

                    // Shader Property checkbox
                    SerializedProperty targetsShaderEntryProp = GetArrayElement(targetsShaderProp, i);
                    bool currentTargetsShader = targetsShaderEntryProp != null && targetsShaderEntryProp.boolValue;
                    bool updatedTargetsShader = EditorGUILayout.ToggleLeft(new GUIContent("Shader Property", "Target shader properties on renderers"), currentTargetsShader);
                    if (targetsShaderEntryProp != null && updatedTargetsShader != currentTargetsShader)
                    {
                        targetsShaderEntryProp.boolValue = updatedTargetsShader;
                    }

                    // Udon Behavior checkbox
                    SerializedProperty targetsUdonEntryProp = GetArrayElement(targetsUdonProp, i);
                    bool currentTargetsUdon = targetsUdonEntryProp != null && targetsUdonEntryProp.boolValue;
                    bool updatedTargetsUdon = EditorGUILayout.ToggleLeft(new GUIContent("Udon Behavior", "Target UdonBehaviour variables"), currentTargetsUdon);
                    if (targetsUdonEntryProp != null && updatedTargetsUdon != currentTargetsUdon)
                    {
                        targetsUdonEntryProp.boolValue = updatedTargetsUdon;
                    }

                    // Unity Slider checkbox
                    SerializedProperty targetsSliderEntryProp = GetArrayElement(targetsSliderProp, i);
                    bool currentTargetsSlider = targetsSliderEntryProp != null && targetsSliderEntryProp.boolValue;
                    bool updatedTargetsSlider = EditorGUILayout.ToggleLeft(new GUIContent("Unity Slider", "Target Unity UI Slider components"), currentTargetsSlider);
                    if (targetsSliderEntryProp != null && updatedTargetsSlider != currentTargetsSlider)
                    {
                        targetsSliderEntryProp.boolValue = updatedTargetsSlider;
                    }

                    // Show warning if neither is selected
                    if (!updatedTargetsShader && !updatedTargetsUdon && !updatedTargetsSlider)
                    {
                        EditorGUILayout.HelpBox("Select at least one target type (Shader Property, Udon Behavior, or Unity Slider).", MessageType.Warning);
                    }

                    // Draw Shader Property UI if enabled
                    if (updatedTargetsShader)
                    {
                        EditorGUILayout.LabelField("Shader Property Settings", EditorStyles.miniBoldLabel);
                        EditorGUI.indentLevel++;
                        
                        // Draw per-entry renderer list
                        DrawPropertyShaderRendererList(handlerObject, shaderRenderersProp, shaderRendererCountsProp, i);
                        
                        // Build renderer list for this entry
                        List<Renderer> entryRendererList = BuildEntryRendererList(shaderRenderersProp, shaderRendererCountsProp, i);
                        
                        if (entryRendererList.Count > 0)
                        {
                            SerializedProperty materialIndexProp = GetArrayElement(materialIndicesProp, i);
                            int matIndex = materialIndexProp != null ? materialIndexProp.intValue : 0;
                            int newMatIndex = EditorGUILayout.IntField(new GUIContent("Material Index"), matIndex);
                            if (materialIndexProp != null && newMatIndex != matIndex)
                            {
                                materialIndexProp.intValue = Mathf.Max(0, newMatIndex);
                            }

                            // If also targeting Udon, find common properties; otherwise show all shader properties
                            List<UdonBehaviour> udonTargetList = null;
                            if (updatedTargetsUdon)
                            {
                                udonTargetList = BuildEntryUdonList(udonBehavioursProp, udonCountsProp, i);
                            }
                            
                            DrawPropertyDropdownWithTargets(propertyNamesProp, propertyTypesProp, displayNameProp, i, entryRendererList, udonTargetList, materialIndexProp?.intValue ?? 0);

                            int propType = GetArrayInt(propertyTypesProp, i);
                            propertyName = GetArrayString(propertyNamesProp, i);

                            if (propType == 1)
                            {
                                SerializedProperty colorProp = GetArrayElement(colorValuesProp, i);
                                TryAutoPopulateColorName(displayNameProp, colorProp != null ? colorProp.colorValue : Color.white, propertyName);
                            }

                            DrawPropertyValueField(floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, displayNameProp, propertyName, i, propType);
                        }
                        
                        EditorGUI.indentLevel--;
                    }

                    // Draw Udon Behavior UI if enabled
                    if (updatedTargetsUdon)
                    {
                        EditorGUILayout.LabelField("Udon Behavior Settings", EditorStyles.miniBoldLabel);
                        EditorGUI.indentLevel++;
                        
                        // Draw UdonBehaviour list and variable selection
                        DrawPropertyUdonBehaviourList(handlerObject, udonBehavioursProp, udonCountsProp, i);
                        
                        // If also targeting shader, the property is shared; otherwise show Udon variable selection
                        if (!updatedTargetsShader)
                        {
                            DrawPropertyUdonVariableField(handlerObject, udonBehavioursProp, udonCountsProp, udonVariableNamesProp, floatValuesProp, i);
                        }
                        
                        // Draw float value field for UdonBehaviour (shared with shader if both are enabled)
                        if (!updatedTargetsShader)
                        {
                            SerializedProperty floatProp = GetArrayElement(floatValuesProp, i);
                            if (floatProp != null)
                            {
                                EditorGUILayout.PropertyField(floatProp, new GUIContent("Value"));
                            }
                        }
                        
                        EditorGUI.indentLevel--;
                    }

                    // Draw Unity Slider UI if enabled
                    if (updatedTargetsSlider)
                    {
                        EditorGUILayout.LabelField("Unity Slider Settings", EditorStyles.miniBoldLabel);
                        EditorGUI.indentLevel++;
                        
                        DrawPropertySliderList(handlerObject, slidersProp, sliderCountsProp, sliderReversedProp, floatValuesProp, i);
                        
                        EditorGUI.indentLevel--;
                    }
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
                AddPropertyShaderEntry(targetsShaderProp, shaderRendererCountsProp);
                AddPropertyUdonEntry(targetsUdonProp, udonCountsProp, udonVariableNamesProp);
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

        private void DuplicatePropertyEntry(
            SerializedProperty entriesProp,
            SerializedProperty displayNamesProp,
            SerializedProperty materialIndicesProp,
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty floatValuesProp,
            SerializedProperty colorValuesProp,
            SerializedProperty vectorValuesProp,
            SerializedProperty textureValuesProp,
            SerializedProperty targetsShaderProp,
            SerializedProperty shaderRendererCountsProp,
            SerializedProperty shaderRenderersProp,
            SerializedProperty targetsUdonProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp,
            SerializedProperty udonBehavioursProp,
            int sourceIndex,
            SerializedProperty countProp)
        {
            if (sourceIndex < 0)
            {
                return;
            }

            // Get source values
            string entryName = GetArrayString(entriesProp, sourceIndex);
            string displayName = GetArrayString(displayNamesProp, sourceIndex);
            int materialIndex = GetArrayInt(materialIndicesProp, sourceIndex);
            string propertyName = GetArrayString(propertyNamesProp, sourceIndex);
            int propertyType = GetArrayInt(propertyTypesProp, sourceIndex);
            
            SerializedProperty floatProp = GetArrayElement(floatValuesProp, sourceIndex);
            float floatValue = floatProp != null ? floatProp.floatValue : 0f;
            
            SerializedProperty colorProp = GetArrayElement(colorValuesProp, sourceIndex);
            Color colorValue = colorProp != null ? colorProp.colorValue : Color.white;
            
            SerializedProperty vectorProp = GetArrayElement(vectorValuesProp, sourceIndex);
            Vector4 vectorValue = vectorProp != null ? vectorProp.vector4Value : Vector4.zero;
            
            SerializedProperty textureProp = GetArrayElement(textureValuesProp, sourceIndex);
            UnityEngine.Object textureValue = textureProp != null ? textureProp.objectReferenceValue : null;
            
            bool targetsShader = GetArrayBool(targetsShaderProp, sourceIndex);
            bool targetsUdon = GetArrayBool(targetsUdonProp, sourceIndex);
            string udonVariableName = GetArrayString(udonVariableNamesProp, sourceIndex);

            int insertIndex = sourceIndex + 1;

            // Add new entry
            AddPropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp);
            AddPropertyShaderEntry(targetsShaderProp, shaderRendererCountsProp);
            AddPropertyUdonEntry(targetsUdonProp, udonCountsProp, udonVariableNamesProp);
            
            if (countProp != null)
            {
                countProp.intValue = countProp.intValue + 1;
            }

            int newIndex = (entriesProp != null ? entriesProp.arraySize : 1) - 1;

            // Move the new entry to right after the source
            if (newIndex > insertIndex)
            {
                for (int j = newIndex; j > insertIndex; j--)
                {
                    MovePropertyEntry(entriesProp, displayNamesProp, materialIndicesProp, propertyNamesProp, propertyTypesProp, floatValuesProp, colorValuesProp, vectorValuesProp, textureValuesProp, j, j - 1);
                    MovePropertyShaderEntry(targetsShaderProp, shaderRendererCountsProp, j, j - 1);
                    MovePropertyUdonEntry(targetsUdonProp, udonCountsProp, udonVariableNamesProp, udonBehavioursProp, j, j - 1);
                }
            }

            // Set duplicated values
            SetArrayString(entriesProp, insertIndex, entryName);
            SetArrayString(displayNamesProp, insertIndex, displayName);
            SetArrayInt(materialIndicesProp, insertIndex, materialIndex);
            SetArrayString(propertyNamesProp, insertIndex, propertyName);
            SetArrayInt(propertyTypesProp, insertIndex, propertyType);
            
            SerializedProperty newFloatProp = GetArrayElement(floatValuesProp, insertIndex);
            if (newFloatProp != null) newFloatProp.floatValue = floatValue;
            
            SerializedProperty newColorProp = GetArrayElement(colorValuesProp, insertIndex);
            if (newColorProp != null) newColorProp.colorValue = colorValue;
            
            SerializedProperty newVectorProp = GetArrayElement(vectorValuesProp, insertIndex);
            if (newVectorProp != null) newVectorProp.vector4Value = vectorValue;
            
            SerializedProperty newTextureProp = GetArrayElement(textureValuesProp, insertIndex);
            if (newTextureProp != null) newTextureProp.objectReferenceValue = textureValue;
            
            SerializedProperty newTargetsShaderProp = GetArrayElement(targetsShaderProp, insertIndex);
            if (newTargetsShaderProp != null) newTargetsShaderProp.boolValue = targetsShader;
            
            SerializedProperty newTargetsUdonProp = GetArrayElement(targetsUdonProp, insertIndex);
            if (newTargetsUdonProp != null) newTargetsUdonProp.boolValue = targetsUdon;
            
            SetArrayString(udonVariableNamesProp, insertIndex, udonVariableName);
        }

        private void SetArrayString(SerializedProperty prop, int index, string value)
        {
            SerializedProperty element = GetArrayElement(prop, index);
            if (element != null)
            {
                element.stringValue = value;
            }
        }

        private void SetArrayInt(SerializedProperty prop, int index, int value)
        {
            SerializedProperty element = GetArrayElement(prop, index);
            if (element != null)
            {
                element.intValue = value;
            }
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

        // ==================== Property Shader Renderer Helper Methods ====================

        private void EnsurePropertyShaderArraySizes(
            SerializedProperty targetsShaderProp,
            SerializedProperty shaderRendererCountsProp,
            SerializedProperty countProp)
        {
            int targetCount = Mathf.Max(0, countProp?.intValue ?? 0);

            EnsureArraySize(targetsShaderProp, targetCount, prop => prop.boolValue = false);
            EnsureArraySize(shaderRendererCountsProp, targetCount, prop => prop.intValue = 0);
        }

        private void MovePropertyShaderEntry(
            SerializedProperty targetsShaderProp,
            SerializedProperty shaderRendererCountsProp,
            int from,
            int to)
        {
            if (from < 0 || to < 0)
            {
                return;
            }

            targetsShaderProp?.MoveArrayElement(from, to);
            shaderRendererCountsProp?.MoveArrayElement(from, to);
        }

        private void RemovePropertyShaderEntryAt(
            SerializedProperty targetsShaderProp,
            SerializedProperty shaderRendererCountsProp,
            SerializedProperty shaderRenderersProp,
            int index)
        {
            // First, remove the renderer entries from the flat array
            if (shaderRendererCountsProp != null && shaderRenderersProp != null && index >= 0 && index < shaderRendererCountsProp.arraySize)
            {
                int startIndex = GetPropertyShaderRendererStartIndex(shaderRendererCountsProp, index);
                int count = GetPropertyShaderRendererCount(shaderRendererCountsProp, index);
                
                // Remove entries from flat array (in reverse order to maintain indices)
                for (int i = count - 1; i >= 0; i--)
                {
                    int flatIndex = startIndex + i;
                    if (flatIndex >= 0 && flatIndex < shaderRenderersProp.arraySize)
                    {
                        shaderRenderersProp.DeleteArrayElementAtIndex(flatIndex);
                    }
                }
            }

            DeleteArrayElement(targetsShaderProp, index);
            DeleteArrayElement(shaderRendererCountsProp, index);
        }

        private void AddPropertyShaderEntry(
            SerializedProperty targetsShaderProp,
            SerializedProperty shaderRendererCountsProp)
        {
            int insertIndex = targetsShaderProp != null ? targetsShaderProp.arraySize : 0;
            EnsureArraySize(targetsShaderProp, insertIndex + 1, prop => prop.boolValue = false);
            EnsureArraySize(shaderRendererCountsProp, insertIndex + 1, prop => prop.intValue = 0);
        }

        private int GetPropertyShaderRendererStartIndex(SerializedProperty shaderRendererCountsProp, int entryIndex)
        {
            int start = 0;
            if (shaderRendererCountsProp == null || entryIndex <= 0)
            {
                return start;
            }

            int limit = Mathf.Min(entryIndex, shaderRendererCountsProp.arraySize);
            for (int i = 0; i < limit; i++)
            {
                SerializedProperty countProp = shaderRendererCountsProp.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private int GetPropertyShaderRendererCount(SerializedProperty shaderRendererCountsProp, int entryIndex)
        {
            if (shaderRendererCountsProp == null || entryIndex < 0 || entryIndex >= shaderRendererCountsProp.arraySize)
            {
                return 0;
            }

            SerializedProperty countProp = shaderRendererCountsProp.GetArrayElementAtIndex(entryIndex);
            return Mathf.Max(0, countProp?.intValue ?? 0);
        }

        private void DrawPropertyShaderRendererList(
            SerializedObject handlerObject,
            SerializedProperty shaderRenderersProp,
            SerializedProperty shaderRendererCountsProp,
            int entryIndex)
        {
            if (shaderRenderersProp == null || shaderRendererCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(shaderRendererCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int rendererCount = Mathf.Max(0, countProp.intValue);
            int rendererStart = GetPropertyShaderRendererStartIndex(shaderRendererCountsProp, entryIndex);
            EnsurePropertyShaderRenderersCapacity(shaderRenderersProp, rendererStart + rendererCount);

            EditorGUI.indentLevel++;
            
            bool structuralChange = false;

            for (int i = 0; i < rendererCount; i++)
            {
                int flatIndex = rendererStart + i;
                if (flatIndex < 0 || flatIndex >= shaderRenderersProp.arraySize)
                {
                    break;
                }

                SerializedProperty rendererProp = shaderRenderersProp.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginHorizontal();
                Renderer current = rendererProp.objectReferenceValue as Renderer;
                Renderer updated = (Renderer)EditorGUILayout.ObjectField($"Renderer {i + 1}", current, typeof(Renderer), true);
                if (updated != current)
                {
                    rendererProp.objectReferenceValue = updated;
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemovePropertyShaderRendererAt(shaderRenderersProp, shaderRendererCountsProp, entryIndex, i);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (structuralChange)
                {
                    break;
                }
            }

            if (!structuralChange)
            {
                if (GUILayout.Button("Add Renderer", GUILayout.Height(20)))
                {
                    AddPropertyShaderRenderer(shaderRenderersProp, shaderRendererCountsProp, entryIndex);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void EnsurePropertyShaderRenderersCapacity(SerializedProperty shaderRenderersProp, int required)
        {
            if (shaderRenderersProp == null || required <= 0)
            {
                return;
            }

            while (shaderRenderersProp.arraySize < required)
            {
                shaderRenderersProp.InsertArrayElementAtIndex(shaderRenderersProp.arraySize);
                SerializedProperty element = shaderRenderersProp.GetArrayElementAtIndex(shaderRenderersProp.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }
        }

        private void AddPropertyShaderRenderer(
            SerializedProperty shaderRenderersProp,
            SerializedProperty shaderRendererCountsProp,
            int entryIndex)
        {
            if (shaderRenderersProp == null || shaderRendererCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(shaderRendererCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int startIndex = GetPropertyShaderRendererStartIndex(shaderRendererCountsProp, entryIndex);
            int insertIndex = startIndex + currentCount;

            shaderRenderersProp.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = shaderRenderersProp.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }

            countProp.intValue = currentCount + 1;
        }

        private void RemovePropertyShaderRendererAt(
            SerializedProperty shaderRenderersProp,
            SerializedProperty shaderRendererCountsProp,
            int entryIndex,
            int localIndex)
        {
            if (shaderRenderersProp == null || shaderRendererCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(shaderRendererCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int startIndex = GetPropertyShaderRendererStartIndex(shaderRendererCountsProp, entryIndex);
            int flatIndex = startIndex + localIndex;

            if (flatIndex >= 0 && flatIndex < shaderRenderersProp.arraySize)
            {
                shaderRenderersProp.DeleteArrayElementAtIndex(flatIndex);
                countProp.intValue = Mathf.Max(0, currentCount - 1);
            }
        }

        private List<Renderer> BuildEntryRendererList(
            SerializedProperty shaderRenderersProp,
            SerializedProperty shaderRendererCountsProp,
            int entryIndex)
        {
            List<Renderer> rendererList = new List<Renderer>();
            
            if (shaderRenderersProp == null || shaderRendererCountsProp == null)
            {
                return rendererList;
            }

            int count = GetPropertyShaderRendererCount(shaderRendererCountsProp, entryIndex);
            int start = GetPropertyShaderRendererStartIndex(shaderRendererCountsProp, entryIndex);

            for (int i = 0; i < count; i++)
            {
                int flatIndex = start + i;
                if (flatIndex >= 0 && flatIndex < shaderRenderersProp.arraySize)
                {
                    SerializedProperty rendererProp = shaderRenderersProp.GetArrayElementAtIndex(flatIndex);
                    Renderer renderer = rendererProp?.objectReferenceValue as Renderer;
                    if (renderer != null)
                    {
                        rendererList.Add(renderer);
                    }
                }
            }

            return rendererList;
        }

        private List<UdonBehaviour> BuildEntryUdonList(
            SerializedProperty udonBehavioursProp,
            SerializedProperty udonCountsProp,
            int entryIndex)
        {
            List<UdonBehaviour> udonList = new List<UdonBehaviour>();
            
            if (udonBehavioursProp == null || udonCountsProp == null)
            {
                return udonList;
            }

            int count = GetPropertyUdonCount(udonCountsProp, entryIndex);
            int start = GetPropertyUdonStartIndex(udonCountsProp, entryIndex);

            for (int i = 0; i < count; i++)
            {
                int flatIndex = start + i;
                if (flatIndex >= 0 && flatIndex < udonBehavioursProp.arraySize)
                {
                    SerializedProperty udonProp = udonBehavioursProp.GetArrayElementAtIndex(flatIndex);
                    UdonBehaviour udon = udonProp?.objectReferenceValue as UdonBehaviour;
                    if (udon != null)
                    {
                        udonList.Add(udon);
                    }
                }
            }

            return udonList;
        }

        private void DrawPropertyDropdownWithTargets(
            SerializedProperty propertyNamesProp,
            SerializedProperty propertyTypesProp,
            SerializedProperty displayNameProp,
            int entryIndex,
            List<Renderer> rendererList,
            List<UdonBehaviour> udonList,
            int materialIndex)
        {
            // Get properties from shader
            HashSet<string> shaderProperties = new HashSet<string>();
            Dictionary<string, ShaderPropertyType> propertyTypes = new Dictionary<string, ShaderPropertyType>();
            
            foreach (Renderer renderer in rendererList)
            {
                if (renderer == null) continue;
                Material mat = GetRendererMaterial(renderer, materialIndex);
                if (mat == null || mat.shader == null) continue;

                Shader shader = mat.shader;
                int propCount = ShaderUtil.GetPropertyCount(shader);
                for (int p = 0; p < propCount; p++)
                {
                    string propName = ShaderUtil.GetPropertyName(shader, p);
                    ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, p);
                    
                    if (!shaderProperties.Contains(propName))
                    {
                        shaderProperties.Add(propName);
                        propertyTypes[propName] = propType;
                    }
                }
            }

            // If also targeting Udon, find common float properties
            if (udonList != null && udonList.Count > 0)
            {
                HashSet<string> udonVariables = new HashSet<string>();
                bool first = true;
                
                foreach (UdonBehaviour udon in udonList)
                {
                    if (udon == null) continue;
                    HashSet<string> udonVars = GetPropertyUdonPublicFloatVariables(udon);
                    
                    if (first)
                    {
                        udonVariables = udonVars;
                        first = false;
                    }
                    else
                    {
                        udonVariables.IntersectWith(udonVars);
                    }
                }

                // Filter shader properties to only those that also exist as Udon variables (by name)
                // and are float/range type (since Udon variables are numeric)
                HashSet<string> commonProperties = new HashSet<string>();
                foreach (string prop in shaderProperties)
                {
                    if (udonVariables.Contains(prop))
                    {
                        ShaderPropertyType propType = propertyTypes[prop];
                        if (propType == ShaderPropertyType.Float || propType == ShaderPropertyType.Range)
                        {
                            commonProperties.Add(prop);
                        }
                    }
                }
                shaderProperties = commonProperties;
            }

            // Draw dropdown with filtered properties
            string currentProp = GetArrayString(propertyNamesProp, entryIndex);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Property"));
            string displayStr = string.IsNullOrEmpty(currentProp) ? "(None)" : currentProp;
            GUILayout.Label(displayStr, EditorStyles.textField);
            
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                var searchWindow = new PropertySearchWindow("Shader Properties");
                var mainGroup = searchWindow.GetMainGroup();

                foreach (string propName in shaderProperties.OrderBy(p => p))
                {
                    if (propertyTypes.TryGetValue(propName, out ShaderPropertyType propType))
                    {
                        string typeIndicator = GetPropertyTypeIndicatorForPropertyFolder((UnityEngine.Rendering.ShaderPropertyType)(int)propType);
                        string label = string.IsNullOrEmpty(typeIndicator) ? propName : $"{propName} [{typeIndicator}]";
                        mainGroup.Add(label, propName);
                    }
                    else
                    {
                        mainGroup.Add(propName, propName);
                    }
                }

                searchWindow.Open(selectedName =>
                {
                    SerializedProperty nameProp = GetArrayElement(propertyNamesProp, entryIndex);
                    if (nameProp != null)
                    {
                        nameProp.stringValue = selectedName;
                    }

                    // Set property type based on shader
                    if (propertyTypes.TryGetValue(selectedName, out ShaderPropertyType selType))
                    {
                        SerializedProperty typeProp = GetArrayElement(propertyTypesProp, entryIndex);
                        if (typeProp != null)
                        {
                            typeProp.intValue = ShaderPropertyTypeToPropertyType(selType);
                        }
                    }
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        // ==================== Property UdonBehaviour Helper Methods ====================

        private bool GetArrayBool(SerializedProperty prop, int index)
        {
            SerializedProperty element = GetArrayElement(prop, index);
            return element != null && element.boolValue;
        }

        private void EnsurePropertyUdonArraySizes(
            SerializedProperty targetsUdonProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp,
            SerializedProperty countProp)
        {
            int targetCount = Mathf.Max(0, countProp?.intValue ?? 0);

            EnsureArraySize(targetsUdonProp, targetCount, prop => prop.boolValue = false);
            EnsureArraySize(udonCountsProp, targetCount, prop => prop.intValue = 0);
            EnsureArraySize(udonVariableNamesProp, targetCount, prop => prop.stringValue = string.Empty);
        }

        private void MovePropertyUdonEntry(
            SerializedProperty targetsUdonProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp,
            SerializedProperty udonBehavioursProp,
            int from,
            int to)
        {
            if (from < 0 || to < 0)
            {
                return;
            }

            targetsUdonProp?.MoveArrayElement(from, to);
            udonCountsProp?.MoveArrayElement(from, to);
            udonVariableNamesProp?.MoveArrayElement(from, to);
            
            // Note: Moving UdonBehaviour entries in the flat array is complex because
            // the flat array indices depend on cumulative counts. By only moving the
            // per-entry metadata (targetsUdon, counts, variableNames), the flat array
            // references remain valid at runtime since GetUdonStartIndex recalculates
            // based on the reordered counts array. This avoids expensive array surgery.
        }

        private void RemovePropertyUdonEntryAt(
            SerializedProperty targetsUdonProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp,
            SerializedProperty udonBehavioursProp,
            int index)
        {
            // First, remove the UdonBehaviour entries from the flat array
            if (udonCountsProp != null && udonBehavioursProp != null && index >= 0 && index < udonCountsProp.arraySize)
            {
                int startIndex = GetPropertyUdonStartIndex(udonCountsProp, index);
                int count = GetPropertyUdonCount(udonCountsProp, index);
                
                // Remove entries from flat array (in reverse order to maintain indices)
                for (int i = count - 1; i >= 0; i--)
                {
                    int flatIndex = startIndex + i;
                    if (flatIndex >= 0 && flatIndex < udonBehavioursProp.arraySize)
                    {
                        udonBehavioursProp.DeleteArrayElementAtIndex(flatIndex);
                    }
                }
            }

            DeleteArrayElement(targetsUdonProp, index);
            DeleteArrayElement(udonCountsProp, index);
            DeleteArrayElement(udonVariableNamesProp, index);
        }

        private void AddPropertyUdonEntry(
            SerializedProperty targetsUdonProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp)
        {
            int insertIndex = targetsUdonProp != null ? targetsUdonProp.arraySize : 0;
            EnsureArraySize(targetsUdonProp, insertIndex + 1, prop => prop.boolValue = false);
            EnsureArraySize(udonCountsProp, insertIndex + 1, prop => prop.intValue = 0);
            EnsureArraySize(udonVariableNamesProp, insertIndex + 1, prop => prop.stringValue = string.Empty);
        }

        private int GetPropertyUdonStartIndex(SerializedProperty udonCountsProp, int entryIndex)
        {
            int start = 0;
            if (udonCountsProp == null || entryIndex <= 0)
            {
                return start;
            }

            int limit = Mathf.Min(entryIndex, udonCountsProp.arraySize);
            for (int i = 0; i < limit; i++)
            {
                SerializedProperty countProp = udonCountsProp.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private int GetPropertyUdonCount(SerializedProperty udonCountsProp, int entryIndex)
        {
            if (udonCountsProp == null || entryIndex < 0 || entryIndex >= udonCountsProp.arraySize)
            {
                return 0;
            }

            SerializedProperty countProp = udonCountsProp.GetArrayElementAtIndex(entryIndex);
            return Mathf.Max(0, countProp?.intValue ?? 0);
        }

        private void DrawPropertyUdonBehaviourList(
            SerializedObject handlerObject,
            SerializedProperty udonBehavioursProp,
            SerializedProperty udonCountsProp,
            int entryIndex)
        {
            if (udonBehavioursProp == null || udonCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(udonCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int udonCount = Mathf.Max(0, countProp.intValue);
            int udonStart = GetPropertyUdonStartIndex(udonCountsProp, entryIndex);
            EnsurePropertyUdonBehavioursCapacity(udonBehavioursProp, udonStart + udonCount);

            EditorGUI.indentLevel++;
            
            bool structuralChange = false;

            for (int i = 0; i < udonCount; i++)
            {
                int flatIndex = udonStart + i;
                if (flatIndex < 0 || flatIndex >= udonBehavioursProp.arraySize)
                {
                    break;
                }

                SerializedProperty udonProp = udonBehavioursProp.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginHorizontal();
                UdonBehaviour current = udonProp.objectReferenceValue as UdonBehaviour;
                UdonBehaviour updated = (UdonBehaviour)EditorGUILayout.ObjectField($"Udon Behavior {i + 1}", current, typeof(UdonBehaviour), true);
                if (updated != current)
                {
                    udonProp.objectReferenceValue = updated;
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemovePropertyUdonBehaviourAt(udonBehavioursProp, udonCountsProp, entryIndex, i);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (structuralChange)
                {
                    break;
                }
            }

            if (!structuralChange)
            {
                if (GUILayout.Button("Add Udon Behavior", GUILayout.Height(20)))
                {
                    AddPropertyUdonBehaviour(udonBehavioursProp, udonCountsProp, entryIndex);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void EnsurePropertyUdonBehavioursCapacity(SerializedProperty udonBehavioursProp, int required)
        {
            if (udonBehavioursProp == null || required <= 0)
            {
                return;
            }

            while (udonBehavioursProp.arraySize < required)
            {
                udonBehavioursProp.InsertArrayElementAtIndex(udonBehavioursProp.arraySize);
                SerializedProperty element = udonBehavioursProp.GetArrayElementAtIndex(udonBehavioursProp.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }
        }

        private void AddPropertyUdonBehaviour(
            SerializedProperty udonBehavioursProp,
            SerializedProperty udonCountsProp,
            int entryIndex)
        {
            if (udonBehavioursProp == null || udonCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(udonCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int insertIndex = GetPropertyUdonStartIndex(udonCountsProp, entryIndex) + currentCount;

            udonBehavioursProp.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = udonBehavioursProp.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }

            countProp.intValue = currentCount + 1;
        }

        private void RemovePropertyUdonBehaviourAt(
            SerializedProperty udonBehavioursProp,
            SerializedProperty udonCountsProp,
            int entryIndex,
            int localIndex)
        {
            if (udonBehavioursProp == null || udonCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(udonCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int startIndex = GetPropertyUdonStartIndex(udonCountsProp, entryIndex);
            int flatIndex = startIndex + localIndex;

            if (flatIndex >= 0 && flatIndex < udonBehavioursProp.arraySize)
            {
                udonBehavioursProp.DeleteArrayElementAtIndex(flatIndex);
                countProp.intValue = Mathf.Max(0, currentCount - 1);
            }
        }

        private void DrawPropertyUdonVariableField(
            SerializedObject handlerObject,
            SerializedProperty udonBehavioursProp,
            SerializedProperty udonCountsProp,
            SerializedProperty udonVariableNamesProp,
            SerializedProperty floatValuesProp,
            int entryIndex)
        {
            int udonCount = GetPropertyUdonCount(udonCountsProp, entryIndex);
            int udonStart = GetPropertyUdonStartIndex(udonCountsProp, entryIndex);

            if (udonCount == 0 || udonBehavioursProp == null)
            {
                EditorGUILayout.HelpBox("Add at least one Udon Behavior to select a variable.", MessageType.Info);
                return;
            }

            // Build list of UdonBehaviour targets
            List<UdonBehaviour> udonTargets = new List<UdonBehaviour>();
            for (int i = 0; i < udonCount; i++)
            {
                int flatIndex = udonStart + i;
                if (flatIndex >= 0 && flatIndex < udonBehavioursProp.arraySize)
                {
                    SerializedProperty udonProp = udonBehavioursProp.GetArrayElementAtIndex(flatIndex);
                    UdonBehaviour udon = udonProp?.objectReferenceValue as UdonBehaviour;
                    if (udon != null)
                    {
                        udonTargets.Add(udon);
                    }
                }
            }

            if (udonTargets.Count == 0)
            {
                EditorGUILayout.HelpBox("No valid Udon Behaviors assigned.", MessageType.Warning);
                return;
            }

            // Get common public variables across all UdonBehaviour targets
            List<string> variableNames;
            if (!TryBuildPropertyUdonVariableOptions(udonTargets, out variableNames, out string warning))
            {
                if (!string.IsNullOrEmpty(warning))
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                return;
            }

            if (variableNames.Count == 0)
            {
                EditorGUILayout.HelpBox("No common public float variables found across all Udon Behaviors.", MessageType.Info);
                return;
            }

            SerializedProperty varNameProp = GetArrayElement(udonVariableNamesProp, entryIndex);
            string currentName = varNameProp != null ? varNameProp.stringValue : string.Empty;

            // Draw variable selection with search button on single line
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Variable"));
            string displayName = string.IsNullOrEmpty(currentName) ? "(None)" : currentName;
            GUILayout.Label(displayName, EditorStyles.textField);
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                // Capture references for the callback closure
                List<UdonBehaviour> capturedTargets = new List<UdonBehaviour>(udonTargets);
                SerializedProperty capturedFloatProp = GetArrayElement(floatValuesProp, entryIndex);
                OpenPropertyUdonVariableSearchWindow(variableNames, (selectedName) =>
                {
                    if (varNameProp != null)
                    {
                        varNameProp.stringValue = selectedName;
                    }
                    // Autofill float value from the Udon variable's current value
                    AutofillPropertyUdonValue(capturedFloatProp, selectedName, capturedTargets);
                    // Apply changes immediately and force repaint
                    handlerObject.ApplyModifiedProperties();
                    Repaint();
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Autofills the property float value from the selected Udon variable's current value.
        /// </summary>
        private void AutofillPropertyUdonValue(SerializedProperty floatProp, string variableName, List<UdonBehaviour> udonTargets)
        {
            if (floatProp == null || string.IsNullOrEmpty(variableName) || udonTargets == null || udonTargets.Count == 0)
            {
                return;
            }

            // Use the first valid UdonBehaviour to get the current value
            UdonBehaviour firstUdon = udonTargets.FirstOrDefault(u => u != null);
            if (firstUdon == null)
            {
                return;
            }

            if (TryGetPropertyUdonVariableValue(firstUdon, variableName, out float currentValue))
            {
                floatProp.floatValue = currentValue;
            }
        }

        /// <summary>
        /// Attempts to get the current value of an Udon variable as a float.
        /// </summary>
        private bool TryGetPropertyUdonVariableValue(UdonBehaviour udon, string variableName, out float value)
        {
            value = 0f;

            if (udon == null || udon.programSource == null || string.IsNullOrEmpty(variableName))
            {
                return false;
            }

            try
            {
                // Try to get the current value from publicVariables
                if (udon.publicVariables != null && udon.publicVariables.TryGetVariableValue(variableName, out object currentValue))
                {
                    if (currentValue is float floatVal)
                    {
                        value = floatVal;
                        return true;
                    }
                    else if (currentValue is int intVal)
                    {
                        value = intVal;
                        return true;
                    }
                    else if (currentValue is double doubleVal)
                    {
                        value = (float)doubleVal;
                        return true;
                    }
                }
            }
            catch (System.Exception)
            {
                // Fallback: could not get value
            }

            return false;
        }

        private bool TryBuildPropertyUdonVariableOptions(List<UdonBehaviour> udonTargets, out List<string> variableNames, out string warning)
        {
            variableNames = new List<string>();
            warning = null;

            if (udonTargets == null || udonTargets.Count == 0)
            {
                warning = "No Udon Behaviors available.";
                return false;
            }

            HashSet<string> commonVariables = null;

            foreach (UdonBehaviour udon in udonTargets)
            {
                if (udon == null)
                {
                    continue;
                }

                HashSet<string> udonVariables = GetPropertyUdonPublicFloatVariables(udon);

                if (commonVariables == null)
                {
                    commonVariables = udonVariables;
                }
                else
                {
                    // Intersect to find common variables
                    commonVariables.IntersectWith(udonVariables);
                }
            }

            if (commonVariables == null || commonVariables.Count == 0)
            {
                warning = "No common public float variables found.";
                return false;
            }

            variableNames = commonVariables.OrderBy(v => v).ToList();
            return true;
        }

        private HashSet<string> GetPropertyUdonPublicFloatVariables(UdonBehaviour udon)
        {
            HashSet<string> variables = new HashSet<string>();

            if (udon == null || udon.programSource == null)
            {
                return variables;
            }

            try
            {
                // Check if this is an UdonSharp behavior - use fieldDefinitions from UdonSharpProgramAsset
                if (udon.programSource is UdonSharpProgramAsset udonSharpProgramAsset)
                {
                    if (udonSharpProgramAsset.fieldDefinitions != null)
                    {
                        foreach (var fieldDef in udonSharpProgramAsset.fieldDefinitions.Values)
                        {
                            // Include numeric types that can be set via SetProgramVariable with a float value.
                            // float: Direct assignment
                            // int: Implicit conversion from float (truncates decimal)
                            // double: Implicit conversion from float
                            // Other numeric types (byte, short, etc.) are excluded to avoid unexpected truncation.
                            if (fieldDef.SystemType == typeof(float) || 
                                fieldDef.SystemType == typeof(int) || 
                                fieldDef.SystemType == typeof(double))
                            {
                                variables.Add(fieldDef.Name);
                            }
                        }
                    }
                }
                else
                {
                    // Fall back to publicVariables for non-UdonSharp behaviors (Udon Graph, etc.)
                    var publicVariables = udon.publicVariables;
                    if (publicVariables != null)
                    {
                        var symbolNames = publicVariables.VariableSymbols;
                        foreach (string symbolName in symbolNames)
                        {
                            // Include numeric types that can be set via SetProgramVariable with a float value.
                            // float: Direct assignment
                            // int: Implicit conversion from float (truncates decimal)
                            // double: Implicit conversion from float
                            // Other numeric types (byte, short, etc.) are excluded to avoid unexpected truncation.
                            if (publicVariables.TryGetVariableType(symbolName, out System.Type varType))
                            {
                                if (varType == typeof(float) || varType == typeof(int) || varType == typeof(double))
                                {
                                    variables.Add(symbolName);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Fallback: no variables found
            }

            return variables;
        }

        private void OpenPropertyUdonVariableSearchWindow(List<string> variableNames, Action<string> onSelect)
        {
            var searchWindow = new PropertySearchWindow("Udon Variables");
            var mainGroup = searchWindow.GetMainGroup();

            foreach (string varName in variableNames)
            {
                mainGroup.Add(varName, varName);
            }

            searchWindow.Open(onSelect);
        }

        // ==================== Property Slider Helper Methods ====================

        private void EnsurePropertySliderArraySizes(
            SerializedProperty targetsSliderProp,
            SerializedProperty sliderCountsProp,
            SerializedProperty countProp)
        {
            int targetCount = Mathf.Max(0, countProp?.intValue ?? 0);

            EnsureArraySize(targetsSliderProp, targetCount, prop => prop.boolValue = false);
            EnsureArraySize(sliderCountsProp, targetCount, prop => prop.intValue = 0);
        }

        private void DrawPropertySliderList(
            SerializedObject handlerObject,
            SerializedProperty slidersProp,
            SerializedProperty sliderCountsProp,
            SerializedProperty sliderReversedProp,
            SerializedProperty floatValuesProp,
            int entryIndex)
        {
            if (slidersProp == null || sliderCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(sliderCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int sliderCount = Mathf.Max(0, countProp.intValue);
            int sliderStart = GetPropertySliderStartIndex(sliderCountsProp, entryIndex);
            EnsurePropertySliderCapacity(slidersProp, sliderReversedProp, sliderStart + sliderCount);

            bool structuralChange = false;

            for (int i = 0; i < sliderCount; i++)
            {
                int flatIndex = sliderStart + i;
                if (flatIndex < 0 || flatIndex >= slidersProp.arraySize)
                {
                    break;
                }

                SerializedProperty sliderProp = slidersProp.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.BeginHorizontal();
                UnityEngine.UI.Slider current = sliderProp.objectReferenceValue as UnityEngine.UI.Slider;
                UnityEngine.UI.Slider updated = (UnityEngine.UI.Slider)EditorGUILayout.ObjectField($"Slider {i + 1}", current, typeof(UnityEngine.UI.Slider), true);
                if (updated != current)
                {
                    sliderProp.objectReferenceValue = updated;
                    // Auto-fill value from slider when assigned
                    if (updated != null)
                    {
                        SerializedProperty floatProp = GetArrayElement(floatValuesProp, entryIndex);
                        if (floatProp != null)
                        {
                            floatProp.floatValue = updated.value;
                        }
                    }
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemovePropertySliderAt(slidersProp, sliderCountsProp, sliderReversedProp, entryIndex, i);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Show reversed direction checkbox
                if (!structuralChange && current != null)
                {
                    SerializedProperty reversedProp = (sliderReversedProp != null && flatIndex < sliderReversedProp.arraySize)
                        ? sliderReversedProp.GetArrayElementAtIndex(flatIndex)
                        : null;
                    
                    if (reversedProp != null)
                    {
                        bool reversed = EditorGUILayout.ToggleLeft("Reversed Direction", reversedProp.boolValue);
                        if (reversed != reversedProp.boolValue)
                        {
                            reversedProp.boolValue = reversed;
                        }
                    }
                    
                    // Show slider info
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"Min: {current.minValue}, Max: {current.maxValue}, Current: {current.value}", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.EndVertical();

                if (structuralChange)
                {
                    break;
                }
            }

            // Add value field for slider-only targeting (when no shader property is selected)
            // The floatValue is shared across all target types, so show it here for slider targeting
            if (sliderCount > 0)
            {
                GUILayout.Space(4);
                SerializedProperty floatProp = GetArrayElement(floatValuesProp, entryIndex);
                if (floatProp != null)
                {
                    // Get the first slider to show min/max bounds for reference
                    UnityEngine.UI.Slider firstSlider = null;
                    int firstSliderIndex = GetPropertySliderStartIndex(sliderCountsProp, entryIndex);
                    if (firstSliderIndex >= 0 && firstSliderIndex < slidersProp.arraySize)
                    {
                        SerializedProperty firstSliderProp = slidersProp.GetArrayElementAtIndex(firstSliderIndex);
                        firstSlider = firstSliderProp?.objectReferenceValue as UnityEngine.UI.Slider;
                    }

                    if (firstSlider != null)
                    {
                        // Show slider with min/max from the first slider component
                        float currentValue = floatProp.floatValue;
                        float newValue = EditorGUILayout.Slider(
                            new GUIContent("Target Value", "The value to set on the slider(s) when this toggle is activated"),
                            currentValue, 
                            firstSlider.minValue, 
                            firstSlider.maxValue);
                        if (newValue != currentValue)
                        {
                            floatProp.floatValue = newValue;
                        }
                    }
                    else
                    {
                        // No slider assigned yet, show regular float field
                        EditorGUILayout.PropertyField(floatProp, new GUIContent("Target Value", "The value to set on the slider(s) when this toggle is activated"));
                    }
                }
            }

            if (!structuralChange)
            {
                if (GUILayout.Button("Add Slider", GUILayout.Height(20)))
                {
                    AddPropertySlider(slidersProp, sliderCountsProp, sliderReversedProp, entryIndex);
                }
            }
        }

        private int GetPropertySliderStartIndex(SerializedProperty sliderCountsProp, int entryIndex)
        {
            int start = 0;
            if (sliderCountsProp == null || entryIndex <= 0)
            {
                return start;
            }

            int limit = Mathf.Min(entryIndex, sliderCountsProp.arraySize);
            for (int i = 0; i < limit; i++)
            {
                SerializedProperty countProp = sliderCountsProp.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private void EnsurePropertySliderCapacity(SerializedProperty slidersProp, SerializedProperty sliderReversedProp, int required)
        {
            if (slidersProp == null || required <= 0)
            {
                return;
            }

            while (slidersProp.arraySize < required)
            {
                slidersProp.InsertArrayElementAtIndex(slidersProp.arraySize);
                SerializedProperty element = slidersProp.GetArrayElementAtIndex(slidersProp.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }

            if (sliderReversedProp != null)
            {
                while (sliderReversedProp.arraySize < required)
                {
                    sliderReversedProp.InsertArrayElementAtIndex(sliderReversedProp.arraySize);
                    SerializedProperty element = sliderReversedProp.GetArrayElementAtIndex(sliderReversedProp.arraySize - 1);
                    if (element != null)
                    {
                        element.boolValue = false;
                    }
                }
            }
        }

        private void AddPropertySlider(
            SerializedProperty slidersProp,
            SerializedProperty sliderCountsProp,
            SerializedProperty sliderReversedProp,
            int entryIndex)
        {
            if (slidersProp == null || sliderCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(sliderCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int startIndex = GetPropertySliderStartIndex(sliderCountsProp, entryIndex);
            int insertIndex = startIndex + currentCount;

            slidersProp.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = slidersProp.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }

            if (sliderReversedProp != null)
            {
                sliderReversedProp.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty reversedElement = sliderReversedProp.GetArrayElementAtIndex(insertIndex);
                if (reversedElement != null)
                {
                    reversedElement.boolValue = false;
                }
            }

            countProp.intValue = currentCount + 1;
        }

        private void RemovePropertySliderAt(
            SerializedProperty slidersProp,
            SerializedProperty sliderCountsProp,
            SerializedProperty sliderReversedProp,
            int entryIndex,
            int localIndex)
        {
            if (slidersProp == null || sliderCountsProp == null)
            {
                return;
            }

            SerializedProperty countProp = GetArrayElement(sliderCountsProp, entryIndex);
            if (countProp == null)
            {
                return;
            }

            int currentCount = Mathf.Max(0, countProp.intValue);
            int startIndex = GetPropertySliderStartIndex(sliderCountsProp, entryIndex);
            int flatIndex = startIndex + localIndex;

            if (flatIndex >= 0 && flatIndex < slidersProp.arraySize)
            {
                slidersProp.DeleteArrayElementAtIndex(flatIndex);
                countProp.intValue = Mathf.Max(0, currentCount - 1);
            }

            if (sliderReversedProp != null && flatIndex >= 0 && flatIndex < sliderReversedProp.arraySize)
            {
                sliderReversedProp.DeleteArrayElementAtIndex(flatIndex);
            }
        }
    }
}
#endif

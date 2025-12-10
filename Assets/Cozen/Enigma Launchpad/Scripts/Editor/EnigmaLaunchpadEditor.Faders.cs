#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cozen
{
    internal struct FaderShaderTarget
    {
        public Renderer[] renderers;
        public int[] materialIndices;
        public Material[] directMaterials;
    }

    public partial class EnigmaLaunchpadEditor : Editor
    {
        // Fader section foldout key
        private const string F_Faders = "Faders";

        // Scale factor for expanding float property ranges above their current value
        private const float FloatRangeExpansionFactor = 1.5f;

        // Fader handler serialized properties
        private SerializedProperty faderHandlerProperty;
        private SerializedObject faderHandlerObject;
        private SerializedProperty leftHandColliderProperty;
        private SerializedProperty rightHandColliderProperty;

        // Static fader properties
        private SerializedProperty fadersFadersArray;
        private SerializedProperty dynamicFaderCountProperty;
        private SerializedProperty staticFaderNames;
        private SerializedProperty staticFaderTargetFolders;
        private SerializedProperty staticFaderTargetsCustom;
        private SerializedProperty staticFaderMaterialIndices;
        private SerializedProperty staticFaderPropertyNames;
        private SerializedProperty staticFaderPropertyTypes;
        private SerializedProperty staticFaderRendererCounts;
        private SerializedProperty staticFaderRenderers;
        private SerializedProperty staticFaderMinValues;
        private SerializedProperty staticFaderMaxValues;
        private SerializedProperty staticFaderDefaultValues;
        private SerializedProperty staticFaderDefaultColors;
        private SerializedProperty staticFaderColorIndicatorsEnabled;
        private SerializedProperty staticFaderIndicatorColors;
        private SerializedProperty staticFaderIndicatorConditional;

        // Dynamic fader properties
        private SerializedProperty dynamicFaderNames;
        private SerializedProperty dynamicFaderFolders;
        private SerializedProperty dynamicFaderToggles;
        private SerializedProperty dynamicFaderMaterialIndices;
        private SerializedProperty dynamicFaderPropertyNames;
        private SerializedProperty dynamicFaderPropertyTypes;
        private SerializedProperty dynamicFaderMinValues;
        private SerializedProperty dynamicFaderMaxValues;
        private SerializedProperty dynamicFaderDefaultValues;
        private SerializedProperty dynamicFaderDefaultColors;
        private SerializedProperty dynamicFaderColorIndicatorsEnabled;
        private SerializedProperty dynamicFaderIndicatorColors;
        private SerializedProperty dynamicFaderIndicatorConditional;

        // Fader foldout states
        private readonly bool[] staticFaderFoldouts = new bool[9];
        private readonly bool[] staticFaderTargetFoldouts = new bool[9];
        private bool dynamicFaderFoldout = true;

        // Mochie dynamic effect labels for toggle dropdown
        private static readonly string[] MochieDynamicEffectLabels = new[]
        {
            "Aura Outline",
            "Sobel Outline",
            "Sobel Filter",
            "Invert",
            "Shake",
            "Pixel Blur",
            "Distort",
            "Noise",
            "Scan Lines",
            "Depth Buffer",
            "Normal Map",
            "Saturation",
            "Rounding",
            "Fog",
            "Brightness",
            "Contrast",
            "HDR",
            "Overlay",
            "Scan"
        };

        private static GUIContent duplicateFaderButtonContent;

        private static GUIContent DuplicateFaderButtonContent
        {
            get
            {
                EnsureDuplicateFaderButtonContent();
                return duplicateFaderButtonContent;
            }
        }

        private static void EnsureDuplicateFaderButtonContent()
        {
            if (duplicateFaderButtonContent != null)
            {
                return;
            }

            duplicateFaderButtonContent = CreateDuplicateFaderButtonContent();
        }

        private static GUIContent CreateDuplicateFaderButtonContent()
        {
            GUIContent iconContent = EditorGUIUtility.IconContent("TreeEditor.Duplicate", "Duplicate this fader");
            if (iconContent.image != null)
            {
                return iconContent;
            }

            return new GUIContent("⧉", "Duplicate this fader");
        }

        private void BindFaderHandlerSerializedObject()
        {
            faderHandlerObject = null;
            fadersFadersArray = null;
            dynamicFaderCountProperty = null;
            staticFaderNames = null;
            staticFaderTargetFolders = null;
            staticFaderTargetsCustom = null;
            staticFaderMaterialIndices = null;
            staticFaderPropertyNames = null;
            staticFaderPropertyTypes = null;
            staticFaderRendererCounts = null;
            staticFaderRenderers = null;
            staticFaderMinValues = null;
            staticFaderMaxValues = null;
            staticFaderDefaultValues = null;
            staticFaderColorIndicatorsEnabled = null;
            staticFaderIndicatorColors = null;
            staticFaderIndicatorConditional = null;
            leftHandColliderProperty = null;
            rightHandColliderProperty = null;
            dynamicFaderNames = null;
            dynamicFaderFolders = null;
            dynamicFaderToggles = null;
            dynamicFaderPropertyNames = null;
            dynamicFaderPropertyTypes = null;
            dynamicFaderMinValues = null;
            dynamicFaderMaxValues = null;
            dynamicFaderDefaultValues = null;
            dynamicFaderColorIndicatorsEnabled = null;
            dynamicFaderIndicatorColors = null;
            dynamicFaderIndicatorConditional = null;

            if (faderHandlerProperty == null || faderHandlerProperty.objectReferenceValue == null)
            {
                return;
            }

            faderHandlerObject = new SerializedObject(faderHandlerProperty.objectReferenceValue);
            fadersFadersArray = faderHandlerObject.FindProperty("faders");
            dynamicFaderCountProperty = faderHandlerObject.FindProperty("dynamicFaderCount");
            leftHandColliderProperty = faderHandlerObject.FindProperty("leftHandCollider");
            rightHandColliderProperty = faderHandlerObject.FindProperty("rightHandCollider");

            // Static fader properties
            staticFaderNames = faderHandlerObject.FindProperty("staticFaderNames");
            staticFaderTargetFolders = faderHandlerObject.FindProperty("staticFaderTargetFolders");
            staticFaderTargetsCustom = faderHandlerObject.FindProperty("staticFaderTargetsCustom");
            staticFaderMaterialIndices = faderHandlerObject.FindProperty("staticFaderMaterialIndices");
            staticFaderPropertyNames = faderHandlerObject.FindProperty("staticFaderPropertyNames");
            staticFaderPropertyTypes = faderHandlerObject.FindProperty("staticFaderPropertyTypes");
            staticFaderRendererCounts = faderHandlerObject.FindProperty("staticFaderRendererCounts");
            staticFaderRenderers = faderHandlerObject.FindProperty("staticFaderRenderers");
            staticFaderMinValues = faderHandlerObject.FindProperty("staticFaderMinValues");
            staticFaderMaxValues = faderHandlerObject.FindProperty("staticFaderMaxValues");
            staticFaderDefaultValues = faderHandlerObject.FindProperty("staticFaderDefaultValues");
            staticFaderDefaultColors = faderHandlerObject.FindProperty("staticFaderDefaultColors");
            staticFaderColorIndicatorsEnabled = faderHandlerObject.FindProperty("staticFaderColorIndicatorsEnabled");
            staticFaderIndicatorColors = faderHandlerObject.FindProperty("staticFaderIndicatorColors");
            staticFaderIndicatorConditional = faderHandlerObject.FindProperty("staticFaderIndicatorConditional");

            // Dynamic fader properties
            dynamicFaderNames = faderHandlerObject.FindProperty("dynamicFaderNames");
            dynamicFaderFolders = faderHandlerObject.FindProperty("dynamicFaderFolders");
            dynamicFaderToggles = faderHandlerObject.FindProperty("dynamicFaderToggles");
            dynamicFaderMaterialIndices = faderHandlerObject.FindProperty("dynamicFaderMaterialIndices");
            dynamicFaderPropertyNames = faderHandlerObject.FindProperty("dynamicFaderPropertyNames");
            dynamicFaderPropertyTypes = faderHandlerObject.FindProperty("dynamicFaderPropertyTypes");
            dynamicFaderMinValues = faderHandlerObject.FindProperty("dynamicFaderMinValues");
            dynamicFaderMaxValues = faderHandlerObject.FindProperty("dynamicFaderMaxValues");
            dynamicFaderDefaultValues = faderHandlerObject.FindProperty("dynamicFaderDefaultValues");
            dynamicFaderDefaultColors = faderHandlerObject.FindProperty("dynamicFaderDefaultColors");
            dynamicFaderColorIndicatorsEnabled = faderHandlerObject.FindProperty("dynamicFaderColorIndicatorsEnabled");
            dynamicFaderIndicatorColors = faderHandlerObject.FindProperty("dynamicFaderIndicatorColors");
            dynamicFaderIndicatorConditional = faderHandlerObject.FindProperty("dynamicFaderIndicatorConditional");

            // Auto-assign faderSystemHandler and faderIndex on each FaderHandler
            AutoAssignFaderHandlerReferences();
        }

        /// <summary>
        /// Returns true if the Faders section should be shown in the editor.
        /// The section is only shown if a FaderSystemHandler is assigned AND has faders configured.
        /// </summary>
        private bool ShouldShowFadersSection()
        {
            if (faderHandlerObject == null || fadersFadersArray == null)
            {
                return false;
            }
            
            // Check if any faders are assigned
            for (int i = 0; i < fadersFadersArray.arraySize; i++)
            {
                SerializedProperty element = fadersFadersArray.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue != null)
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Auto-assigns faderSystemHandler, faderIndex, and hand colliders on each FaderHandler in the faders array.
        /// This ensures each FaderHandler knows which system it belongs to, its index for grab coordination,
        /// and uses the correct shared hand colliders.
        /// </summary>
        private void AutoAssignFaderHandlerReferences()
        {
            if (faderHandlerObject == null || fadersFadersArray == null || faderHandlerProperty == null)
            {
                return;
            }

            FaderSystemHandler systemHandler = faderHandlerProperty.objectReferenceValue as FaderSystemHandler;
            if (systemHandler == null)
            {
                return;
            }

            // Get the hand colliders from the FaderSystemHandler
            GameObject leftHandCollider = leftHandColliderProperty != null 
                ? leftHandColliderProperty.objectReferenceValue as GameObject 
                : null;
            GameObject rightHandCollider = rightHandColliderProperty != null 
                ? rightHandColliderProperty.objectReferenceValue as GameObject 
                : null;

            bool anyChanges = false;
            int faderCount = fadersFadersArray.arraySize;
            
            for (int i = 0; i < faderCount; i++)
            {
                SerializedProperty element = fadersFadersArray.GetArrayElementAtIndex(i);
                if (element == null || element.objectReferenceValue == null)
                {
                    continue;
                }

                FaderHandler fader = element.objectReferenceValue as FaderHandler;
                if (fader == null)
                {
                    continue;
                }

                SerializedObject faderObject = new SerializedObject(fader);
                SerializedProperty systemHandlerProp = faderObject.FindProperty("faderSystemHandler");
                SerializedProperty indexProp = faderObject.FindProperty("faderIndex");
                SerializedProperty leftColliderProp = faderObject.FindProperty("leftHandCollider");
                SerializedProperty rightColliderProp = faderObject.FindProperty("rightHandCollider");

                bool needsUpdate = false;

                if (systemHandlerProp != null && systemHandlerProp.objectReferenceValue != systemHandler)
                {
                    systemHandlerProp.objectReferenceValue = systemHandler;
                    needsUpdate = true;
                }

                if (indexProp != null && indexProp.intValue != i)
                {
                    indexProp.intValue = i;
                    needsUpdate = true;
                }

                if (leftColliderProp != null && leftHandCollider != null && leftColliderProp.objectReferenceValue != leftHandCollider)
                {
                    leftColliderProp.objectReferenceValue = leftHandCollider;
                    needsUpdate = true;
                }

                if (rightColliderProp != null && rightHandCollider != null && rightColliderProp.objectReferenceValue != rightHandCollider)
                {
                    rightColliderProp.objectReferenceValue = rightHandCollider;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    faderObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(fader);
                    anyChanges = true;
                }
            }

            if (anyChanges)
            {
                // Save assets to persist changes
                AssetDatabase.SaveAssets();
            }
        }

        private void EnsureFaderHandlerParity()
        {
            // FaderSystemHandler is optional - only create if faders are being used
            // Unlike other handlers, we don't auto-create the FaderSystemHandler
        }

        private void EnsureStaticFaderArrayParity()
        {
            if (faderHandlerObject == null)
            {
                return;
            }

            const int FaderCount = 9;
            EnsureArraySize(staticFaderNames, FaderCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(staticFaderTargetFolders, FaderCount, prop => prop.intValue = -1);
            EnsureArraySize(staticFaderTargetsCustom, FaderCount, prop => prop.boolValue = false);
            EnsureArraySize(staticFaderMaterialIndices, FaderCount, prop => prop.intValue = 0);
            EnsureArraySize(staticFaderPropertyNames, FaderCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(staticFaderPropertyTypes, FaderCount, prop => prop.intValue = 0);
            EnsureArraySize(staticFaderMinValues, FaderCount, prop => prop.floatValue = 0f);
            EnsureArraySize(staticFaderMaxValues, FaderCount, prop => prop.floatValue = 1f);
            EnsureArraySize(staticFaderDefaultValues, FaderCount, prop => prop.floatValue = 0f);
            EnsureArraySize(staticFaderDefaultColors, FaderCount, prop => prop.colorValue = Color.white);
            EnsureArraySize(staticFaderColorIndicatorsEnabled, FaderCount, prop => prop.boolValue = false);
            EnsureArraySize(staticFaderIndicatorColors, FaderCount, prop => prop.colorValue = Color.white);
            EnsureArraySize(staticFaderIndicatorConditional, FaderCount, prop => prop.boolValue = false);
            EnsureArraySize(staticFaderRendererCounts, FaderCount, prop => prop.intValue = 0);
        }

        private void EnsureDynamicFaderArrayParity()
        {
            if (faderHandlerObject == null)
            {
                return;
            }

            int maxSize = 0;
            if (dynamicFaderNames != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderNames.arraySize);
            }
            if (dynamicFaderFolders != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderFolders.arraySize);
            }
            if (dynamicFaderToggles != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderToggles.arraySize);
            }
            if (dynamicFaderPropertyNames != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderPropertyNames.arraySize);
            }
            if (dynamicFaderPropertyTypes != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderPropertyTypes.arraySize);
            }
            if (dynamicFaderMinValues != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderMinValues.arraySize);
            }
            if (dynamicFaderMaxValues != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderMaxValues.arraySize);
            }
            if (dynamicFaderDefaultValues != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderDefaultValues.arraySize);
            }
            if (dynamicFaderColorIndicatorsEnabled != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderColorIndicatorsEnabled.arraySize);
            }
            if (dynamicFaderIndicatorColors != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderIndicatorColors.arraySize);
            }
            if (dynamicFaderIndicatorConditional != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderIndicatorConditional.arraySize);
            }

            if (maxSize == 0)
            {
                return;
            }

            EnsureDynamicFaderArraySize(dynamicFaderNames, maxSize, prop => prop.stringValue = string.Empty);
            EnsureDynamicFaderArraySize(dynamicFaderFolders, maxSize, prop => prop.intValue = -1);
            EnsureDynamicFaderArraySize(dynamicFaderToggles, maxSize, prop => prop.intValue = -1);
            EnsureDynamicFaderArraySize(dynamicFaderPropertyNames, maxSize, prop => prop.stringValue = string.Empty);
            EnsureDynamicFaderArraySize(dynamicFaderPropertyTypes, maxSize, prop => prop.intValue = 0);
            EnsureDynamicFaderArraySize(dynamicFaderMinValues, maxSize, prop => prop.floatValue = 0f);
            EnsureDynamicFaderArraySize(dynamicFaderMaxValues, maxSize, prop => prop.floatValue = 1f);
            EnsureDynamicFaderArraySize(dynamicFaderDefaultValues, maxSize, prop => prop.floatValue = 0f);
            EnsureDynamicFaderArraySize(dynamicFaderDefaultColors, maxSize, prop => prop.colorValue = Color.white);
            EnsureDynamicFaderArraySize(dynamicFaderColorIndicatorsEnabled, maxSize, prop => prop.boolValue = false);
            EnsureDynamicFaderArraySize(dynamicFaderIndicatorColors, maxSize, prop => prop.colorValue = Color.white);
            EnsureDynamicFaderArraySize(dynamicFaderIndicatorConditional, maxSize, prop => prop.boolValue = false);
        }

        private void EnsureDynamicFaderArraySize(SerializedProperty prop, int targetSize, Action<SerializedProperty> initialize)
        {
            if (prop == null)
            {
                return;
            }

            while (prop.arraySize < targetSize)
            {
                int insertIndex = prop.arraySize;
                prop.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty element = prop.GetArrayElementAtIndex(insertIndex);
                initialize?.Invoke(element);
            }

            while (prop.arraySize > targetSize)
            {
                prop.DeleteArrayElementAtIndex(prop.arraySize - 1);
            }
        }

        private void EnsureStaticFaderFoldoutDefaults()
        {
            for (int i = 0; i < staticFaderFoldouts.Length; i++)
            {
                staticFaderFoldouts[i] = true;
                staticFaderTargetFoldouts[i] = true;
            }

            dynamicFaderFoldout = true;
        }

        private void DrawFadersSection()
        {
            if (faderHandlerObject == null)
            {
                // No handler assigned - section will be hidden by ShouldShowFadersSection
                return;
            }

            faderHandlerObject.Update();
            EnsureStaticFaderArrayParity();
            EnsureDynamicFaderArrayParity();

            DrawDynamicFaderSlider();
            GUILayout.Space(InnerContentVerticalPad);
            DrawStaticFaders();

            faderHandlerObject.ApplyModifiedProperties();
        }

        private void DrawDynamicFaderSlider()
        {
            const int TotalFaders = 9;
            if (dynamicFaderCountProperty == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntSlider(
                        new GUIContent(
                            "Dynamic Faders",
                            "How many of the nine faders are configured for dynamic control."),
                        0,
                        0,
                        TotalFaders);
                }
                return;
            }

            int current = Mathf.Clamp(dynamicFaderCountProperty.intValue, 0, TotalFaders);
            if (current != dynamicFaderCountProperty.intValue)
            {
                dynamicFaderCountProperty.intValue = current;
            }

            EditorGUI.BeginChangeCheck();
            int updated = EditorGUILayout.IntSlider(
                new GUIContent(
                    "Dynamic Faders",
                    "How many of the nine faders are configured for dynamic control."),
                current,
                0,
                TotalFaders);
            if (EditorGUI.EndChangeCheck())
            {
                dynamicFaderCountProperty.intValue = updated;
            }
        }

        private void DrawStaticFaders()
        {
            const int TotalFaders = 9;
            int dynamicCount = (dynamicFaderCountProperty != null) ? Mathf.Clamp(dynamicFaderCountProperty.intValue, 0, TotalFaders) : 0;
            int staticCount = Mathf.Clamp(TotalFaders - dynamicCount, 0, TotalFaders);
            GUIStyle foldoutStyle = folderHeaderFoldoutStyle ?? EditorStyles.foldout;

            if (staticCount > 0)
            {
                List<FolderOption> folderOptions = BuildFolderOptions();
                List<FolderOption> shaderFolders = folderOptions
                    .Where(option =>
                        option.Type == ToggleFolderType.Materials ||
                        option.Type == ToggleFolderType.Properties ||
                        option.Type == ToggleFolderType.Mochie ||
                        option.Type == ToggleFolderType.Skybox ||
                        option.Type == ToggleFolderType.June ||
                        option.Type == ToggleFolderType.Shaders)
                    .ToList();

                for (int faderIndex = 0; faderIndex < staticCount; faderIndex++)
                {
                    int displayIndex = faderIndex + 1;
                    bool expanded = staticFaderFoldouts[faderIndex];
                    bool updatedExpanded = EditorGUILayout.Foldout(expanded, $"Static Fader {displayIndex}", true, foldoutStyle);
                    if (expanded != updatedExpanded)
                    {
                        staticFaderFoldouts[faderIndex] = updatedExpanded;
                        expanded = updatedExpanded;
                    }

                    if (!expanded)
                    {
                        GUILayout.Space(2);
                        continue;
                    }

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    DrawStaticFaderNameField(faderIndex);
                    GUILayout.Space(4);
                    DrawStaticFaderTargetOptions(faderIndex, shaderFolders);
                    GUILayout.Space(4);
                    DrawStaticFaderMaterialIndex(faderIndex);
                    GUILayout.Space(4);
                    DrawStaticFaderPropertyField(faderIndex);
                    GUILayout.Space(4);
                    DrawStaticFaderValueRangeFields(faderIndex);
                    GUILayout.Space(4);
                    DrawStaticFaderIndicatorFields(faderIndex);
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(4);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("All faders are configured as dynamic.", MessageType.Info);
            }

            DrawDynamicFaderFoldout(dynamicCount, foldoutStyle);
        }

        private void DrawStaticFaderNameField(int faderIndex)
        {
            if (staticFaderNames == null || faderIndex < 0 || faderIndex >= staticFaderNames.arraySize)
            {
                return;
            }

            SerializedProperty nameProp = staticFaderNames.GetArrayElementAtIndex(faderIndex);
            string current = nameProp?.stringValue ?? string.Empty;
            string updated = EditorGUILayout.TextField(
                new GUIContent("Fader Name", "Label shown for this static fader."),
                current);
            if (nameProp != null && updated != current)
            {
                nameProp.stringValue = updated;
            }
        }

        private void DrawStaticFaderTargetOptions(int faderIndex, List<FolderOption> rendererFolders)
        {
            GUIStyle foldoutStyle = folderHeaderFoldoutStyle ?? EditorStyles.foldout;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int initialIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            bool expanded = staticFaderTargetFoldouts[faderIndex];
            bool updatedExpanded = EditorGUILayout.Foldout(expanded, "Target Renderer", true, foldoutStyle);
            if (expanded != updatedExpanded)
            {
                staticFaderTargetFoldouts[faderIndex] = updatedExpanded;
                expanded = updatedExpanded;
            }

            if (expanded)
            {
                EditorGUI.indentLevel++;

                if (staticFaderTargetFolders != null && faderIndex < staticFaderTargetFolders.arraySize)
                {
                    SerializedProperty folderProp = staticFaderTargetFolders.GetArrayElementAtIndex(faderIndex);
                    foreach (FolderOption option in rendererFolders)
                    {
                        string label = string.IsNullOrEmpty(option.Label)
                            ? "Folder Renderer"
                            : $"{option.Label} Renderer";
                        bool selected = folderProp != null && folderProp.intValue == option.Index;
                        bool next = EditorGUILayout.ToggleLeft(label, selected);
                        if (folderProp != null)
                        {
                            if (next && !selected)
                            {
                                folderProp.intValue = option.Index;
                            }
                            else if (!next && selected)
                            {
                                folderProp.intValue = -1;
                            }
                        }
                    }
                }

                bool customToggle = false;
                SerializedProperty customProp = (staticFaderTargetsCustom != null && faderIndex < staticFaderTargetsCustom.arraySize)
                    ? staticFaderTargetsCustom.GetArrayElementAtIndex(faderIndex)
                    : null;

                if (customProp != null)
                {
                    customToggle = EditorGUILayout.ToggleLeft("Other Renderer", customProp.boolValue);
                    if (customToggle != customProp.boolValue)
                    {
                        customProp.boolValue = customToggle;
                    }
                }

                if (customToggle)
                {
                    GUILayout.Space(4);
                    DrawStaticFaderRendererList(faderIndex);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel = initialIndent;
            EditorGUILayout.EndVertical();
        }

        private void DrawStaticFaderMaterialIndex(int faderIndex)
        {
            if (staticFaderMaterialIndices == null || faderIndex < 0 || faderIndex >= staticFaderMaterialIndices.arraySize)
            {
                return;
            }

            SerializedProperty matIndexProp = staticFaderMaterialIndices.GetArrayElementAtIndex(faderIndex);
            int current = matIndexProp?.intValue ?? 0;
            int updated = EditorGUILayout.IntField(new GUIContent("Material Index"), current);
            if (matIndexProp != null && updated != current)
            {
                matIndexProp.intValue = Mathf.Max(0, updated);
            }
        }

        private void DrawStaticFaderValueRangeFields(int faderIndex)
        {
            if (staticFaderMinValues == null || staticFaderMaxValues == null || staticFaderDefaultValues == null)
            {
                return;
            }

            // Check if a renderer or material target is configured
            FaderShaderTarget target = BuildStaticFaderShaderTarget(faderIndex);
            if ((target.renderers == null || target.renderers.Length == 0) &&
                (target.directMaterials == null || target.directMaterials.Length == 0))
            {
                return;
            }

            // Check if a property has been selected
            string propertyName = GetStaticFaderPropertyName(faderIndex);
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            // Check if this is a color property (propertyType == 2)
            int propertyType = GetStaticFaderPropertyType(faderIndex);
            bool isColorProperty = (propertyType == 2);

            if (isColorProperty)
            {
                // For color properties, show default color and max shift
                if (staticFaderDefaultColors == null || faderIndex < 0 || faderIndex >= staticFaderDefaultColors.arraySize)
                {
                    return;
                }

                SerializedProperty colorProp = staticFaderDefaultColors.GetArrayElementAtIndex(faderIndex);
                if (colorProp != null)
                {
                    Color defaultColor = colorProp.colorValue;
                    // Enable HDR support for color properties that may use HDR values
                    Color updatedColor = EditorGUILayout.ColorField(
                        new GUIContent("Default Color", "Base color to shift from. Supports HDR colors."), 
                        defaultColor,
                        true,  // showEyedropper
                        true,  // showAlpha
                        true   // hdr
                    );
                    if (updatedColor != defaultColor)
                    {
                        colorProp.colorValue = updatedColor;
                    }

                    // Check saturation and show warning if too low
                    Color.RGBToHSV(updatedColor, out float h, out float s, out float v);
                    if (s < 0.15f)
                    {
                        EditorGUILayout.HelpBox(
                            "Warning: This color has low saturation (greyscale). Hue shifting will have minimal effect on greyscale colors.",
                            MessageType.Warning);
                    }
                }

                // Max Shift field (stored in maxValue, 0-360 degrees)
                float maxShift = GetStaticFaderMaxValue(faderIndex);
                maxShift = Mathf.Clamp(maxShift, 0f, 360f);
                float updatedMaxShift = EditorGUILayout.Slider(
                    new GUIContent("Max Shift (degrees)", "Maximum hue shift in degrees. 360 = full color wheel rotation."),
                    maxShift,
                    0f,
                    360f);

                if (!Mathf.Approximately(updatedMaxShift, maxShift))
                {
                    SetStaticFaderMinValue(faderIndex, 0f); // Min is always 0 for color
                    SetStaticFaderMaxValue(faderIndex, updatedMaxShift);
                    SetStaticFaderDefaultValue(faderIndex, 0f); // Default position is at min (no shift)
                }
            }
            else
            {
                // For float/range properties, show min/max/default
                float minValue = GetStaticFaderMinValue(faderIndex);
                float maxValue = GetStaticFaderMaxValue(faderIndex);
                float defaultValue = GetStaticFaderDefaultValue(faderIndex);

                float updatedMin = EditorGUILayout.FloatField(new GUIContent("Min", "Lower bound for this fader."), minValue);
                float updatedMax = EditorGUILayout.FloatField(new GUIContent("Max", "Upper bound for this fader."), maxValue);
                if (updatedMax < updatedMin)
                {
                    updatedMax = updatedMin;
                }

                float updatedDefault = EditorGUILayout.FloatField(new GUIContent("Default", "Value applied on start and reset."), defaultValue);
                updatedDefault = Mathf.Clamp(updatedDefault, updatedMin, updatedMax);

                if (!Mathf.Approximately(updatedMin, minValue))
                {
                    SetStaticFaderMinValue(faderIndex, updatedMin);
                }

                if (!Mathf.Approximately(updatedMax, maxValue))
                {
                    SetStaticFaderMaxValue(faderIndex, updatedMax);
                }

                if (!Mathf.Approximately(updatedDefault, defaultValue))
                {
                    SetStaticFaderDefaultValue(faderIndex, updatedDefault);
                }
            }
        }

        private void DrawStaticFaderIndicatorFields(int faderIndex)
        {
            if (staticFaderColorIndicatorsEnabled == null || staticFaderIndicatorColors == null || staticFaderIndicatorConditional == null)
            {
                return;
            }

            SerializedProperty enabledProp = staticFaderColorIndicatorsEnabled.GetArrayElementAtIndex(faderIndex);
            bool enabled = enabledProp != null && enabledProp.boolValue;
            bool updatedEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Enable Color Indicator"), enabled);
            if (enabledProp != null && updatedEnabled != enabled)
            {
                enabledProp.boolValue = updatedEnabled;
            }

            if (!updatedEnabled)
            {
                return;
            }

            SerializedProperty colorProp = staticFaderIndicatorColors.GetArrayElementAtIndex(faderIndex);
            if (colorProp != null)
            {
                Color updatedColor = EditorGUILayout.ColorField(
                    new GUIContent("Color"), 
                    colorProp.colorValue, 
                    true,  // showEyedropper
                    true,  // showAlpha
                    true   // hdr
                );
                colorProp.colorValue = updatedColor;
            }

            SerializedProperty conditionalProp = staticFaderIndicatorConditional.GetArrayElementAtIndex(faderIndex);
            if (conditionalProp != null)
            {
                bool updatedConditional = EditorGUILayout.ToggleLeft(new GUIContent("Turn on when Fader > Min"), conditionalProp.boolValue);
                conditionalProp.boolValue = updatedConditional;
            }
        }

        private void DrawStaticFaderRendererList(int faderIndex)
        {
            if (staticFaderRenderers == null || staticFaderRendererCounts == null)
            {
                return;
            }

            SerializedProperty countProp = (faderIndex >= 0 && faderIndex < staticFaderRendererCounts.arraySize)
                ? staticFaderRendererCounts.GetArrayElementAtIndex(faderIndex)
                : null;

            if (countProp == null)
            {
                return;
            }

            int rendererCount = Mathf.Max(0, countProp.intValue);
            int rendererStart = GetStaticFaderRendererStartIndex(faderIndex);
            EnsureStaticFaderRendererArrayCapacity(rendererStart + rendererCount);

            bool structuralChange = false;

            for (int i = 0; i < rendererCount; i++)
            {
                int flatIndex = rendererStart + i;
                if (flatIndex < 0 || flatIndex >= staticFaderRenderers.arraySize)
                {
                    break;
                }

                SerializedProperty rendererProp = staticFaderRenderers.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginHorizontal();
                Renderer current = rendererProp.objectReferenceValue as Renderer;
                Renderer updated = (Renderer)EditorGUILayout.ObjectField($"Renderer {i + 1}", current, typeof(Renderer), true);
                if (updated != current)
                {
                    rendererProp.objectReferenceValue = updated;
                }

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MoveStaticFaderRenderer(faderIndex, i, i - 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i < rendererCount - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MoveStaticFaderRenderer(faderIndex, i, i + 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveStaticFaderRendererAt(faderIndex, i);
                    countProp.intValue = Mathf.Max(0, rendererCount - 1);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (structuralChange)
                {
                    GUILayout.Space(2);
                    break;
                }

                GUILayout.Space(2);
            }

            if (structuralChange)
            {
                return;
            }

            Rect addButtonRect = GUILayoutUtility.GetRect(new GUIContent("Add Renderer"), GUI.skin.button, GUILayout.Height(22));
            bool addClicked = GUI.Button(addButtonRect, "Add Renderer");
            if (HandleRendererDrop(addButtonRect, faderIndex, countProp, rendererCount))
            {
                return;
            }

            if (addClicked)
            {
                AddStaticFaderRenderer(faderIndex);
                countProp.intValue = rendererCount + 1;
            }
            GUI.enabled = true;
        }

        private void DrawDynamicFaderFoldout(int dynamicCount, GUIStyle foldoutStyle)
        {
            if (dynamicCount <= 0)
            {
                return;
            }

            string label = dynamicCount == 1 ? "Dynamic Fader" : "Dynamic Faders";
            bool expanded = dynamicFaderFoldout;
            bool updatedExpanded = EditorGUILayout.Foldout(expanded, label, true, foldoutStyle);
            if (expanded != updatedExpanded)
            {
                dynamicFaderFoldout = updatedExpanded;
                expanded = updatedExpanded;
            }

            if (!expanded)
            {
                GUILayout.Space(2);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (DrawDynamicFaderList(dynamicCount))
            {
                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
                ApplyAndRepaint();
                return;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private bool DrawDynamicFaderList(int dynamicCount)
        {
            EnsureDynamicFaderArrayParity();

            int entryCount = GetDynamicFaderCount();
            bool structuralChange = false;

            List<FolderOption> folderOptions = BuildFolderOptions()
                .Where(option => option.Type != ToggleFolderType.Stats)
                .ToList();

            if (entryCount == 0)
            {
                EditorGUILayout.HelpBox("No dynamic faders configured yet.", MessageType.Info);
            }

            for (int i = 0; i < entryCount; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Dynamic Fader {i + 1}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button(DuplicateFaderButtonContent, GUILayout.Width(22)))
                {
                    DuplicateDynamicFader(i);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i > 0;
                if (!structuralChange && GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MoveDynamicFader(i, i - 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i < entryCount - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MoveDynamicFader(i, i + 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveDynamicFaderAt(i);
                    structuralChange = true;
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (structuralChange)
                {
                    EditorGUILayout.EndVertical();
                    break;
                }

                DrawDynamicFaderNameField(i);
                GUILayout.Space(2);
                DrawDynamicFaderFolderDropdown(i, folderOptions);
                GUILayout.Space(2);
                DrawDynamicFaderToggleDropdown(i);
                GUILayout.Space(2);
                DrawDynamicFaderMaterialIndexField(i);
                GUILayout.Space(2);
                DrawDynamicFaderPropertyField(i);
                GUILayout.Space(2);
                DrawDynamicFaderValueRangeFields(i);
                GUILayout.Space(2);
                DrawDynamicFaderIndicatorFields(i);
                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }

            if (structuralChange)
            {
                return true;
            }

            if (GUILayout.Button("+ Add Fader", GUILayout.Height(24)))
            {
                AddDynamicFaderEntry();
                structuralChange = true;
            }

            if (structuralChange)
            {
                return true;
            }

            int allowedCount = Mathf.Clamp(dynamicCount, 0, 9);
            if (allowedCount > 0)
            {
                string slotSummary = allowedCount == 1
                    ? "1 dynamic slot is available; entries fill that slot in priority order."
                    : $"{allowedCount} dynamic slots are available; entries fill slots in priority order.";
                EditorGUILayout.HelpBox(slotSummary, MessageType.Info);
            }

            return false;
        }

        private void DrawDynamicFaderNameField(int index)
        {
            string current = GetDynamicFaderName(index);
            string updated = EditorGUILayout.TextField(new GUIContent("Fader Name"), current);
            if (!string.Equals(current, updated, StringComparison.Ordinal))
            {
                SetDynamicFaderName(index, updated);
            }
        }

        private void DrawDynamicFaderFolderDropdown(int index, List<FolderOption> folderOptions)
        {
            int currentFolder = GetDynamicFaderFolderIndex(index);
            bool hasOptions = folderOptions != null && folderOptions.Count > 0;

            if (!hasOptions)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(new GUIContent("Folder"), 0, new[] { "No folders available" });
                }
                if (currentFolder != -1)
                {
                    SetDynamicFaderFolderIndex(index, -1);
                    SetDynamicFaderToggleIndex(index, -1);
                }
                return;
            }

            string[] labels = new string[folderOptions.Count + 1];
            int[] values = new int[labels.Length];
            labels[0] = "Select Folder";
            values[0] = -1;

            int currentSelection = 0;
            for (int i = 0; i < folderOptions.Count; i++)
            {
                FolderOption option = folderOptions[i];
                labels[i + 1] = option.Label;
                values[i + 1] = option.Index;
                if (option.Index == currentFolder)
                {
                    currentSelection = i + 1;
                }
            }

            if (currentSelection == 0 && currentFolder != -1)
            {
                SetDynamicFaderFolderIndex(index, -1);
                SetDynamicFaderToggleIndex(index, -1);
            }

            int newSelection = EditorGUILayout.Popup(new GUIContent("Folder"), currentSelection, labels);
            if (newSelection != currentSelection)
            {
                SetDynamicFaderFolderIndex(index, values[newSelection]);
                SetDynamicFaderToggleIndex(index, -1);
            }
        }

        private void DrawDynamicFaderToggleDropdown(int index)
        {
            int folderIndex = GetDynamicFaderFolderIndex(index);
            if (folderIndex < 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(new GUIContent("Toggle"), 0, new[] { "Select a folder first" });
                }
                SetDynamicFaderToggleIndex(index, -1);
                return;
            }

            ToggleFolderType folderType = GetFolderType(folderIndex);
            List<ToggleOption> toggleOptions = BuildDynamicToggleOptions(folderIndex, folderType);

            if (toggleOptions == null || toggleOptions.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(new GUIContent("Toggle"), 0, new[] { "No toggles available" });
                }
                SetDynamicFaderToggleIndex(index, -1);
                return;
            }

            int currentToggle = GetDynamicFaderToggleIndex(index);
            string[] labels = new string[toggleOptions.Count + 1];
            int[] values = new int[labels.Length];
            labels[0] = "Select Toggle";
            values[0] = -1;

            int currentSelection = 0;
            for (int i = 0; i < toggleOptions.Count; i++)
            {
                ToggleOption option = toggleOptions[i];
                labels[i + 1] = option.Label;
                values[i + 1] = option.Value;
                if (option.Value == currentToggle)
                {
                    currentSelection = i + 1;
                }
            }

            int newSelection = EditorGUILayout.Popup(new GUIContent("Toggle"), currentSelection, labels);
            if (newSelection != currentSelection)
            {
                SetDynamicFaderToggleIndex(index, values[newSelection]);
            }
        }

        private void DrawDynamicFaderMaterialIndexField(int index)
        {
            int folderIndex = GetDynamicFaderFolderIndex(index);
            if (folderIndex < 0)
            {
                return; // No folder selected, don't show material index field
            }

            ToggleFolderType folderType = GetFolderType(folderIndex);
            
            // Only show material index field for Object folders
            if (folderType != ToggleFolderType.Objects)
            {
                return;
            }

            int toggleIndex = GetDynamicFaderToggleIndex(index);
            if (toggleIndex < 0)
            {
                return; // No toggle selected, don't show material index field
            }

            if (dynamicFaderMaterialIndices == null || index < 0 || index >= dynamicFaderMaterialIndices.arraySize)
            {
                return;
            }

            int currentValue = GetDynamicFaderMaterialIndexValue(index);
            int newValue = EditorGUILayout.IntField(
                new GUIContent("Material Index", "Material slot index on the renderer (0 for first material)"),
                currentValue);
            
            // Validate and clamp the material index to reasonable bounds
            if (newValue < 0)
            {
                newValue = 0;
                EditorGUILayout.HelpBox("Material index cannot be negative. Value set to 0.", MessageType.Warning);
            }
            else if (newValue > 10)
            {
                EditorGUILayout.HelpBox("Material index is unusually high. Most renderers have fewer than 10 materials.", MessageType.Warning);
            }
            
            if (newValue != currentValue)
            {
                SetDynamicFaderMaterialIndex(index, newValue);
            }
        }

        private void DrawDynamicFaderValueRangeFields(int index)
        {
            if (dynamicFaderMinValues == null || dynamicFaderMaxValues == null || dynamicFaderDefaultValues == null)
            {
                return;
            }

            // Check if a folder and toggle are configured
            int folderIndex = GetDynamicFaderFolderIndex(index);
            int toggleIndex = GetDynamicFaderToggleIndex(index);
            if (folderIndex < 0 || toggleIndex < 0)
            {
                return;
            }

            ToggleFolderType folderType = GetFolderType(folderIndex);
            FaderShaderTarget target = BuildDynamicFaderShaderTarget(folderType, folderIndex, toggleIndex, index);
            if ((target.renderers == null || target.renderers.Length == 0) &&
                (target.directMaterials == null || target.directMaterials.Length == 0))
            {
                return;
            }

            // Check if a property has been selected
            string propertyName = GetDynamicFaderPropertyName(index);
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            // Check if this is a color property (propertyType == 2)
            int propertyType = GetDynamicFaderPropertyType(index);
            bool isColorProperty = (propertyType == 2);

            if (isColorProperty)
            {
                // For color properties, show default color and max shift
                if (dynamicFaderDefaultColors == null || index < 0 || index >= dynamicFaderDefaultColors.arraySize)
                {
                    return;
                }

                SerializedProperty colorProp = dynamicFaderDefaultColors.GetArrayElementAtIndex(index);
                if (colorProp != null)
                {
                    Color defaultColor = colorProp.colorValue;
                    // Enable HDR support for color properties that may use HDR values
                    Color updatedColor = EditorGUILayout.ColorField(
                        new GUIContent("Default Color", "Base color to shift from. Supports HDR colors."), 
                        defaultColor,
                        true,  // showEyedropper
                        true,  // showAlpha
                        true   // hdr
                    );
                    if (updatedColor != defaultColor)
                    {
                        colorProp.colorValue = updatedColor;
                    }

                    // Check saturation and show warning if too low
                    Color.RGBToHSV(updatedColor, out float h, out float s, out float v);
                    if (s < 0.15f)
                    {
                        EditorGUILayout.HelpBox(
                            "Warning: This color has low saturation (greyscale). Hue shifting will have minimal effect on greyscale colors.",
                            MessageType.Warning);
                    }
                }

                // Max Shift field (stored in maxValue, 0-360 degrees)
                float maxShift = GetDynamicFaderMaxValue(index);
                maxShift = Mathf.Clamp(maxShift, 0f, 360f);
                float updatedMaxShift = EditorGUILayout.Slider(
                    new GUIContent("Max Shift (degrees)", "Maximum hue shift in degrees. 360 = full color wheel rotation."),
                    maxShift,
                    0f,
                    360f);

                if (!Mathf.Approximately(updatedMaxShift, maxShift))
                {
                    SetDynamicFaderMinValue(index, 0f); // Min is always 0 for color
                    SetDynamicFaderMaxValue(index, updatedMaxShift);
                    SetDynamicFaderDefaultValue(index, 0f); // Default position is at min (no shift)
                }
            }
            else
            {
                // For float/range properties, show min/max/default
                float minValue = GetDynamicFaderMinValue(index);
                float maxValue = GetDynamicFaderMaxValue(index);
                float defaultValue = GetDynamicFaderDefaultValue(index);

                float updatedMin = EditorGUILayout.FloatField(new GUIContent("Min", "Lower bound for this fader."), minValue);
                float updatedMax = EditorGUILayout.FloatField(new GUIContent("Max", "Upper bound for this fader."), maxValue);
                if (updatedMax < updatedMin)
                {
                    updatedMax = updatedMin;
                }

                float updatedDefault = EditorGUILayout.FloatField(new GUIContent("Default", "Value applied on start and reset."), defaultValue);
                updatedDefault = Mathf.Clamp(updatedDefault, updatedMin, updatedMax);

                if (!Mathf.Approximately(updatedMin, minValue))
                {
                    SetDynamicFaderMinValue(index, updatedMin);
                }

                if (!Mathf.Approximately(updatedMax, maxValue))
                {
                    SetDynamicFaderMaxValue(index, updatedMax);
                }

                if (!Mathf.Approximately(updatedDefault, defaultValue))
                {
                    SetDynamicFaderDefaultValue(index, updatedDefault);
                }
            }
        }

        private void DrawDynamicFaderIndicatorFields(int index)
        {
            if (dynamicFaderColorIndicatorsEnabled == null || dynamicFaderIndicatorColors == null || dynamicFaderIndicatorConditional == null)
            {
                return;
            }

            if (index < 0 || index >= dynamicFaderColorIndicatorsEnabled.arraySize)
            {
                return;
            }

            SerializedProperty enabledProp = dynamicFaderColorIndicatorsEnabled.GetArrayElementAtIndex(index);
            bool enabled = enabledProp != null && enabledProp.boolValue;
            bool updatedEnabled = EditorGUILayout.ToggleLeft(new GUIContent("Enable Color Indicator"), enabled);
            if (enabledProp != null && updatedEnabled != enabled)
            {
                enabledProp.boolValue = updatedEnabled;
            }

            if (!updatedEnabled)
            {
                return;
            }

            if (index < dynamicFaderIndicatorColors.arraySize)
            {
                SerializedProperty colorProp = dynamicFaderIndicatorColors.GetArrayElementAtIndex(index);
                if (colorProp != null)
                {
                    Color updatedColor = EditorGUILayout.ColorField(
                        new GUIContent("Color"), 
                        colorProp.colorValue, 
                        true,  // showEyedropper
                        true,  // showAlpha
                        true   // hdr
                    );
                    colorProp.colorValue = updatedColor;
                }
            }

            if (index < dynamicFaderIndicatorConditional.arraySize)
            {
                SerializedProperty conditionalProp = dynamicFaderIndicatorConditional.GetArrayElementAtIndex(index);
                if (conditionalProp != null)
                {
                    bool updatedConditional = EditorGUILayout.ToggleLeft(new GUIContent("Turn on when Fader > Min"), conditionalProp.boolValue);
                    conditionalProp.boolValue = updatedConditional;
                }
            }
        }

        private bool GetDynamicFaderIndicatorEnabled(int index)
        {
            if (dynamicFaderColorIndicatorsEnabled == null || index < 0 || index >= dynamicFaderColorIndicatorsEnabled.arraySize)
            {
                return false;
            }

            SerializedProperty enabledProp = dynamicFaderColorIndicatorsEnabled.GetArrayElementAtIndex(index);
            return enabledProp != null && enabledProp.boolValue;
        }

        private void SetDynamicFaderIndicatorEnabled(int index, bool value)
        {
            if (dynamicFaderColorIndicatorsEnabled == null || index < 0 || index >= dynamicFaderColorIndicatorsEnabled.arraySize)
            {
                return;
            }

            SerializedProperty enabledProp = dynamicFaderColorIndicatorsEnabled.GetArrayElementAtIndex(index);
            if (enabledProp != null)
            {
                enabledProp.boolValue = value;
            }
        }

        private Color GetDynamicFaderIndicatorColor(int index)
        {
            if (dynamicFaderIndicatorColors == null || index < 0 || index >= dynamicFaderIndicatorColors.arraySize)
            {
                return Color.white;
            }

            SerializedProperty colorProp = dynamicFaderIndicatorColors.GetArrayElementAtIndex(index);
            return colorProp != null ? colorProp.colorValue : Color.white;
        }

        private void SetDynamicFaderIndicatorColor(int index, Color value)
        {
            if (dynamicFaderIndicatorColors == null || index < 0 || index >= dynamicFaderIndicatorColors.arraySize)
            {
                return;
            }

            SerializedProperty colorProp = dynamicFaderIndicatorColors.GetArrayElementAtIndex(index);
            if (colorProp != null)
            {
                colorProp.colorValue = value;
            }
        }

        private bool GetDynamicFaderIndicatorConditional(int index)
        {
            if (dynamicFaderIndicatorConditional == null || index < 0 || index >= dynamicFaderIndicatorConditional.arraySize)
            {
                return false;
            }

            SerializedProperty conditionalProp = dynamicFaderIndicatorConditional.GetArrayElementAtIndex(index);
            return conditionalProp != null && conditionalProp.boolValue;
        }

        private void SetDynamicFaderIndicatorConditional(int index, bool value)
        {
            if (dynamicFaderIndicatorConditional == null || index < 0 || index >= dynamicFaderIndicatorConditional.arraySize)
            {
                return;
            }

            SerializedProperty conditionalProp = dynamicFaderIndicatorConditional.GetArrayElementAtIndex(index);
            if (conditionalProp != null)
            {
                conditionalProp.boolValue = value;
            }
        }

        // Helper structs for folder and toggle options
        private struct FolderOption
        {
            public int Index;
            public string Label;
            public ToggleFolderType Type;
        }

        private struct ToggleOption
        {
            public int Value;
            public string Label;
        }

        private List<FolderOption> BuildFolderOptions()
        {
            int folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            List<FolderOption> options = new List<FolderOption>(folderCount);

            for (int i = 0; i < folderCount; i++)
            {
                string label = string.Empty;
                if (folderNamesProperty != null)
                {
                    SerializedProperty nameProp = folderNamesProperty.GetArrayElementAtIndex(i);
                    if (nameProp != null)
                    {
                        label = nameProp.stringValue;
                    }
                }

                ToggleFolderType folderType = GetFolderType(i);

                if (string.IsNullOrEmpty(label))
                {
                    label = GetFolderDisplayLabel(folderType);
                }

                if (string.IsNullOrEmpty(label))
                {
                    label = "Folder";
                }

                options.Add(new FolderOption
                {
                    Index = i,
                    Label = label,
                    Type = folderType
                });
            }

            return options;
        }

        private List<ToggleOption> BuildDynamicToggleOptions(int folderIndex, ToggleFolderType folderType)
        {
            List<ToggleOption> options = new List<ToggleOption>();

            switch (folderType)
            {
                case ToggleFolderType.Objects:
                case ToggleFolderType.Materials:
                    return BuildFolderToggleOptions(folderIndex, folderType);

                case ToggleFolderType.Properties:
                    return BuildPropertyToggleOptions(folderIndex);

                case ToggleFolderType.Mochie:
                    return BuildMochieToggleOptions();

                case ToggleFolderType.Skybox:
                    return BuildSkyboxToggleOptions();

                case ToggleFolderType.June:
                    return BuildJuneToggleOptions(folderIndex);

                case ToggleFolderType.Shaders:
                    return BuildShaderToggleOptions(folderIndex);

                default:
                    return options;
            }
        }

        private List<ToggleOption> BuildFolderToggleOptions(int folderIndex, ToggleFolderType folderType)
        {
            List<ToggleOption> options = new List<ToggleOption>();

            SerializedProperty entriesProperty = GetFolderEntriesProperty(folderIndex);
            if (entriesProperty == null)
            {
                return options;
            }

            int entryCount = entriesProperty.arraySize;
            for (int i = 0; i < entryCount; i++)
            {
                SerializedProperty element = entriesProperty.GetArrayElementAtIndex(i);
                string label;
                if (element != null && element.objectReferenceValue != null)
                {
                    label = ButtonHandler.FormatName(element.objectReferenceValue.name);
                }
                else
                {
                    label = $"{GetFolderTypeSingular(folderType)} {i + 1}";
                }

                options.Add(new ToggleOption
                {
                    Value = i,
                    Label = label
                });
            }

            return options;
        }

        private List<ToggleOption> BuildPropertyToggleOptions(int folderIndex)
        {
            List<ToggleOption> options = new List<ToggleOption>();

            SerializedObject handlerObject = GetPropertyHandlerObjectForFolder(folderIndex);
            if (handlerObject == null)
            {
                return options;
            }

            SerializedProperty entriesProperty = handlerObject.FindProperty("propertyEntries");
            if (entriesProperty == null)
            {
                return options;
            }

            int entryCount = entriesProperty.arraySize;
            for (int i = 0; i < entryCount; i++)
            {
                SerializedProperty element = entriesProperty.GetArrayElementAtIndex(i);
                string label = element != null ? element.stringValue : string.Empty;
                if (string.IsNullOrEmpty(label))
                {
                    label = $"Property {i + 1}";
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

        private List<ToggleOption> BuildMochieToggleOptions()
        {
            List<ToggleOption> options = new List<ToggleOption>(MochieDynamicEffectLabels.Length);
            for (int i = 0; i < MochieDynamicEffectLabels.Length; i++)
            {
                options.Add(new ToggleOption
                {
                    Value = i,
                    Label = MochieDynamicEffectLabels[i]
                });
            }

            return options;
        }

        private List<ToggleOption> BuildSkyboxToggleOptions()
        {
            List<ToggleOption> options = new List<ToggleOption>();
            if (skyboxMaterials == null || !skyboxMaterials.isArray)
            {
                return options;
            }

            int count = skyboxMaterials.arraySize;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = skyboxMaterials.GetArrayElementAtIndex(i);
                Material material = element != null ? element.objectReferenceValue as Material : null;
                string label = material != null
                    ? ButtonHandler.FormatName(material.name)
                    : $"Skybox {i + 1}";

                options.Add(new ToggleOption
                {
                    Value = i,
                    Label = label
                });
            }

            return options;
        }

        // Dynamic fader helper methods
        private int GetDynamicFaderCount()
        {
            return dynamicFaderNames != null ? dynamicFaderNames.arraySize : 0;
        }

        private void AddDynamicFaderEntry()
        {
            InsertDynamicFaderEntry(GetDynamicFaderCount());
        }

        private void DuplicateDynamicFader(int index)
        {
            if (index < 0 || index >= GetDynamicFaderCount())
            {
                return;
            }

            string name = GetDynamicFaderName(index);
            int folderIndex = GetDynamicFaderFolderIndex(index);
            int toggleIndex = GetDynamicFaderToggleIndex(index);
            int materialIndex = GetDynamicFaderMaterialIndexValue(index);
            string propertyName = GetDynamicFaderPropertyName(index);
            int propertyType = GetDynamicFaderPropertyType(index);
            float minValue = GetDynamicFaderMinValue(index);
            float maxValue = GetDynamicFaderMaxValue(index);
            float defaultValue = GetDynamicFaderDefaultValue(index);
            Color defaultColor = GetDynamicFaderDefaultColor(index);
            bool indicatorEnabled = GetDynamicFaderIndicatorEnabled(index);
            Color indicatorColor = GetDynamicFaderIndicatorColor(index);
            bool indicatorConditional = GetDynamicFaderIndicatorConditional(index);

            int insertIndex = Mathf.Clamp(index + 1, 0, GetDynamicFaderCount());
            InsertDynamicFaderEntry(insertIndex);

            SetDynamicFaderName(insertIndex, name);
            SetDynamicFaderFolderIndex(insertIndex, folderIndex);
            SetDynamicFaderToggleIndex(insertIndex, toggleIndex);
            SetDynamicFaderMaterialIndex(insertIndex, materialIndex);
            SetDynamicFaderPropertyName(insertIndex, propertyName);
            SetDynamicFaderPropertyType(insertIndex, propertyType);
            SetDynamicFaderMinValue(insertIndex, minValue);
            SetDynamicFaderMaxValue(insertIndex, maxValue);
            SetDynamicFaderDefaultValue(insertIndex, defaultValue);
            SetDynamicFaderDefaultColor(insertIndex, defaultColor);
            SetDynamicFaderIndicatorEnabled(insertIndex, indicatorEnabled);
            SetDynamicFaderIndicatorColor(insertIndex, indicatorColor);
            SetDynamicFaderIndicatorConditional(insertIndex, indicatorConditional);
        }

        private void InsertDynamicFaderEntry(int index)
        {
            InsertDynamicFaderElement(dynamicFaderNames, index, prop => prop.stringValue = string.Empty);
            InsertDynamicFaderElement(dynamicFaderFolders, index, prop => prop.intValue = -1);
            InsertDynamicFaderElement(dynamicFaderToggles, index, prop => prop.intValue = -1);
            InsertDynamicFaderElement(dynamicFaderMaterialIndices, index, prop => prop.intValue = 0);
            InsertDynamicFaderElement(dynamicFaderPropertyNames, index, prop => prop.stringValue = string.Empty);
            InsertDynamicFaderElement(dynamicFaderPropertyTypes, index, prop => prop.intValue = 0);
            InsertDynamicFaderElement(dynamicFaderMinValues, index, prop => prop.floatValue = 0f);
            InsertDynamicFaderElement(dynamicFaderMaxValues, index, prop => prop.floatValue = 1f);
            InsertDynamicFaderElement(dynamicFaderDefaultValues, index, prop => prop.floatValue = 0f);
            InsertDynamicFaderElement(dynamicFaderDefaultColors, index, prop => prop.colorValue = Color.white);
            InsertDynamicFaderElement(dynamicFaderColorIndicatorsEnabled, index, prop => prop.boolValue = false);
            InsertDynamicFaderElement(dynamicFaderIndicatorColors, index, prop => prop.colorValue = Color.white);
            InsertDynamicFaderElement(dynamicFaderIndicatorConditional, index, prop => prop.boolValue = false);
        }

        private void InsertDynamicFaderElement(SerializedProperty prop, int index, Action<SerializedProperty> initialize)
        {
            if (prop == null)
            {
                return;
            }

            int safeIndex = Mathf.Clamp(index, 0, prop.arraySize);
            prop.InsertArrayElementAtIndex(safeIndex);
            SerializedProperty element = prop.GetArrayElementAtIndex(safeIndex);
            initialize?.Invoke(element);
        }

        private void RemoveDynamicFaderAt(int index)
        {
            DeleteDynamicFaderElement(dynamicFaderNames, index);
            DeleteDynamicFaderElement(dynamicFaderFolders, index);
            DeleteDynamicFaderElement(dynamicFaderToggles, index);
            DeleteDynamicFaderElement(dynamicFaderMaterialIndices, index);
            DeleteDynamicFaderElement(dynamicFaderPropertyNames, index);
            DeleteDynamicFaderElement(dynamicFaderPropertyTypes, index);
            DeleteDynamicFaderElement(dynamicFaderMinValues, index);
            DeleteDynamicFaderElement(dynamicFaderMaxValues, index);
            DeleteDynamicFaderElement(dynamicFaderDefaultValues, index);
            DeleteDynamicFaderElement(dynamicFaderDefaultColors, index);
            DeleteDynamicFaderElement(dynamicFaderColorIndicatorsEnabled, index);
            DeleteDynamicFaderElement(dynamicFaderIndicatorColors, index);
            DeleteDynamicFaderElement(dynamicFaderIndicatorConditional, index);
        }

        private void DeleteDynamicFaderElement(SerializedProperty prop, int index)
        {
            if (prop == null || index < 0 || index >= prop.arraySize)
            {
                return;
            }

            prop.DeleteArrayElementAtIndex(index);
        }

        private void MoveDynamicFader(int from, int to)
        {
            MoveDynamicFaderElement(dynamicFaderNames, from, to);
            MoveDynamicFaderElement(dynamicFaderFolders, from, to);
            MoveDynamicFaderElement(dynamicFaderToggles, from, to);
            MoveDynamicFaderElement(dynamicFaderMaterialIndices, from, to);
            MoveDynamicFaderElement(dynamicFaderPropertyNames, from, to);
            MoveDynamicFaderElement(dynamicFaderPropertyTypes, from, to);
            MoveDynamicFaderElement(dynamicFaderMinValues, from, to);
            MoveDynamicFaderElement(dynamicFaderMaxValues, from, to);
            MoveDynamicFaderElement(dynamicFaderDefaultValues, from, to);
            MoveDynamicFaderElement(dynamicFaderDefaultColors, from, to);
            MoveDynamicFaderElement(dynamicFaderColorIndicatorsEnabled, from, to);
            MoveDynamicFaderElement(dynamicFaderIndicatorColors, from, to);
            MoveDynamicFaderElement(dynamicFaderIndicatorConditional, from, to);
        }

        private void MoveDynamicFaderElement(SerializedProperty prop, int from, int to)
        {
            if (prop == null || from < 0 || from >= prop.arraySize || to < 0 || to >= prop.arraySize || from == to)
            {
                return;
            }

            prop.MoveArrayElement(from, to);
        }

        private string GetDynamicFaderName(int index)
        {
            if (dynamicFaderNames == null || index < 0 || index >= dynamicFaderNames.arraySize)
            {
                return string.Empty;
            }

            SerializedProperty prop = dynamicFaderNames.GetArrayElementAtIndex(index);
            return prop != null ? prop.stringValue : string.Empty;
        }

        private void SetDynamicFaderName(int index, string value)
        {
            if (dynamicFaderNames == null || index < 0 || index >= dynamicFaderNames.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderNames.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.stringValue = value;
            }
        }

        private int GetDynamicFaderFolderIndex(int index)
        {
            if (dynamicFaderFolders == null || index < 0 || index >= dynamicFaderFolders.arraySize)
            {
                return -1;
            }

            SerializedProperty prop = dynamicFaderFolders.GetArrayElementAtIndex(index);
            return prop != null ? prop.intValue : -1;
        }

        private void SetDynamicFaderFolderIndex(int index, int value)
        {
            if (dynamicFaderFolders == null || index < 0 || index >= dynamicFaderFolders.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderFolders.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.intValue = value;
            }
        }

        private int GetDynamicFaderToggleIndex(int index)
        {
            if (dynamicFaderToggles == null || index < 0 || index >= dynamicFaderToggles.arraySize)
            {
                return -1;
            }

            SerializedProperty prop = dynamicFaderToggles.GetArrayElementAtIndex(index);
            return prop != null ? prop.intValue : -1;
        }

        private void SetDynamicFaderToggleIndex(int index, int value)
        {
            if (dynamicFaderToggles == null || index < 0 || index >= dynamicFaderToggles.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderToggles.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.intValue = value;
            }
        }

        private int GetDynamicFaderMaterialIndexValue(int index)
        {
            if (dynamicFaderMaterialIndices == null || index < 0 || index >= dynamicFaderMaterialIndices.arraySize)
            {
                return 0;
            }

            SerializedProperty prop = dynamicFaderMaterialIndices.GetArrayElementAtIndex(index);
            int matIndex = prop != null ? prop.intValue : 0;
            return matIndex < 0 ? 0 : matIndex;
        }

        private void SetDynamicFaderMaterialIndex(int index, int value)
        {
            if (dynamicFaderMaterialIndices == null || index < 0 || index >= dynamicFaderMaterialIndices.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderMaterialIndices.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.intValue = Mathf.Max(0, value);
            }
        }

        private float GetDynamicFaderMinValue(int index)
        {
            if (dynamicFaderMinValues == null || index < 0 || index >= dynamicFaderMinValues.arraySize)
            {
                return 0f;
            }

            SerializedProperty prop = dynamicFaderMinValues.GetArrayElementAtIndex(index);
            return prop != null ? prop.floatValue : 0f;
        }

        private void SetDynamicFaderMinValue(int index, float value)
        {
            if (dynamicFaderMinValues == null || index < 0 || index >= dynamicFaderMinValues.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderMinValues.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.floatValue = value;
            }
        }

        private float GetDynamicFaderMaxValue(int index)
        {
            if (dynamicFaderMaxValues == null || index < 0 || index >= dynamicFaderMaxValues.arraySize)
            {
                return 1f;
            }

            SerializedProperty prop = dynamicFaderMaxValues.GetArrayElementAtIndex(index);
            return prop != null ? prop.floatValue : 1f;
        }

        private void SetDynamicFaderMaxValue(int index, float value)
        {
            if (dynamicFaderMaxValues == null || index < 0 || index >= dynamicFaderMaxValues.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderMaxValues.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.floatValue = value;
            }
        }

        private float GetDynamicFaderDefaultValue(int index)
        {
            if (dynamicFaderDefaultValues == null || index < 0 || index >= dynamicFaderDefaultValues.arraySize)
            {
                return 0f;
            }

            SerializedProperty prop = dynamicFaderDefaultValues.GetArrayElementAtIndex(index);
            return prop != null ? prop.floatValue : 0f;
        }

        private void SetDynamicFaderDefaultValue(int index, float value)
        {
            if (dynamicFaderDefaultValues == null || index < 0 || index >= dynamicFaderDefaultValues.arraySize)
            {
                return;
            }

            float minValue = GetDynamicFaderMinValue(index);
            float maxValue = GetDynamicFaderMaxValue(index);
            float clamped = Mathf.Clamp(value, minValue, maxValue);

            SerializedProperty prop = dynamicFaderDefaultValues.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.floatValue = clamped;
            }
        }

        private Color GetDynamicFaderDefaultColor(int index)
        {
            if (dynamicFaderDefaultColors == null || index < 0 || index >= dynamicFaderDefaultColors.arraySize)
            {
                return Color.white;
            }

            SerializedProperty prop = dynamicFaderDefaultColors.GetArrayElementAtIndex(index);
            return prop != null ? prop.colorValue : Color.white;
        }

        private void SetDynamicFaderDefaultColor(int index, Color value)
        {
            if (dynamicFaderDefaultColors == null || index < 0 || index >= dynamicFaderDefaultColors.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderDefaultColors.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.colorValue = value;
            }
        }

        // Static fader value helper methods
        private float GetStaticFaderMinValue(int faderIndex)
        {
            if (staticFaderMinValues == null || faderIndex < 0 || faderIndex >= staticFaderMinValues.arraySize)
            {
                return 0f;
            }

            SerializedProperty prop = staticFaderMinValues.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.floatValue : 0f;
        }

        private void SetStaticFaderMinValue(int faderIndex, float value)
        {
            if (staticFaderMinValues == null || faderIndex < 0 || faderIndex >= staticFaderMinValues.arraySize)
            {
                return;
            }

            SerializedProperty prop = staticFaderMinValues.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.floatValue = value;
            }
        }

        private float GetStaticFaderMaxValue(int faderIndex)
        {
            if (staticFaderMaxValues == null || faderIndex < 0 || faderIndex >= staticFaderMaxValues.arraySize)
            {
                return 1f;
            }

            SerializedProperty prop = staticFaderMaxValues.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.floatValue : 1f;
        }

        private void SetStaticFaderMaxValue(int faderIndex, float value)
        {
            if (staticFaderMaxValues == null || faderIndex < 0 || faderIndex >= staticFaderMaxValues.arraySize)
            {
                return;
            }

            SerializedProperty prop = staticFaderMaxValues.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.floatValue = value;
            }
        }

        private float GetStaticFaderDefaultValue(int faderIndex)
        {
            if (staticFaderDefaultValues == null || faderIndex < 0 || faderIndex >= staticFaderDefaultValues.arraySize)
            {
                return 0f;
            }

            SerializedProperty prop = staticFaderDefaultValues.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.floatValue : 0f;
        }

        private void SetStaticFaderDefaultValue(int faderIndex, float value)
        {
            if (staticFaderDefaultValues == null || faderIndex < 0 || faderIndex >= staticFaderDefaultValues.arraySize)
            {
                return;
            }

            float minValue = GetStaticFaderMinValue(faderIndex);
            float maxValue = GetStaticFaderMaxValue(faderIndex);
            float clamped = Mathf.Clamp(value, minValue, maxValue);

            SerializedProperty prop = staticFaderDefaultValues.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.floatValue = clamped;
            }
        }

        // Static fader renderer helper methods
        private void AddStaticFaderRenderer(int faderIndex)
        {
            int insertIndex = GetStaticFaderRendererStartIndex(faderIndex) + GetStaticFaderRendererCountValue(faderIndex);
            InsertStaticFaderRendererAt(insertIndex);
        }

        private bool HandleRendererDrop(Rect dropRect, int faderIndex, SerializedProperty countProp, int rendererCount)
        {
            Event current = Event.current;
            if (current == null || countProp == null)
            {
                return false;
            }

            if (!dropRect.Contains(current.mousePosition))
            {
                return false;
            }

            EventType type = current.type;
            if (type != EventType.DragUpdated && type != EventType.DragPerform)
            {
                return false;
            }

            List<Renderer> droppedRenderers = new List<Renderer>();
            HashSet<Renderer> seenRenderers = new HashSet<Renderer>();
            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                Renderer renderer = reference as Renderer;
                if (renderer != null)
                {
                    if (seenRenderers.Add(renderer))
                    {
                        droppedRenderers.Add(renderer);
                    }

                    continue;
                }

                GameObject go = reference as GameObject;
                if (go == null)
                {
                    continue;
                }

                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer childRenderer in renderers)
                {
                    if (childRenderer != null && seenRenderers.Add(childRenderer))
                    {
                        droppedRenderers.Add(childRenderer);
                    }
                }
            }

            if (droppedRenderers.Count == 0)
            {
                return false;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddDroppedRenderers(faderIndex, countProp, rendererCount, droppedRenderers);
            }

            current.Use();
            return type == EventType.DragPerform;
        }

        private void AddDroppedRenderers(int faderIndex, SerializedProperty countProp, int rendererCount, List<Renderer> droppedRenderers)
        {
            int insertIndex = GetStaticFaderRendererStartIndex(faderIndex) + rendererCount;

            foreach (Renderer renderer in droppedRenderers)
            {
                InsertStaticFaderRendererAt(insertIndex);
                SerializedProperty rendererProp = staticFaderRenderers.GetArrayElementAtIndex(insertIndex);
                if (rendererProp != null)
                {
                    rendererProp.objectReferenceValue = renderer;
                }

                insertIndex++;
            }

            countProp.intValue = rendererCount + droppedRenderers.Count;
        }

        private void RemoveStaticFaderRendererAt(int faderIndex, int localIndex)
        {
            int start = GetStaticFaderRendererStartIndex(faderIndex);
            int flatIndex = start + localIndex;
            if (staticFaderRenderers == null || flatIndex < 0 || flatIndex >= staticFaderRenderers.arraySize)
            {
                return;
            }

            staticFaderRenderers.DeleteArrayElementAtIndex(flatIndex);
        }

        private void MoveStaticFaderRenderer(int faderIndex, int from, int to)
        {
            if (staticFaderRenderers == null)
            {
                return;
            }

            int start = GetStaticFaderRendererStartIndex(faderIndex);
            int count = GetStaticFaderRendererCountValue(faderIndex);
            if (from < 0 || to < 0 || from >= count || to >= count)
            {
                return;
            }

            int fromFlat = start + from;
            int toFlat = start + to;
            if (fromFlat < 0 || toFlat < 0 || fromFlat >= staticFaderRenderers.arraySize || toFlat >= staticFaderRenderers.arraySize)
            {
                return;
            }

            staticFaderRenderers.MoveArrayElement(fromFlat, toFlat);
        }

        private void InsertStaticFaderRendererAt(int flatIndex)
        {
            if (staticFaderRenderers == null)
            {
                return;
            }

            int insertIndex = Mathf.Clamp(flatIndex, 0, staticFaderRenderers.arraySize);
            staticFaderRenderers.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = staticFaderRenderers.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }
        }

        private void EnsureStaticFaderRendererArrayCapacity(int required)
        {
            if (staticFaderRenderers == null || required <= 0)
            {
                return;
            }

            while (staticFaderRenderers.arraySize < required)
            {
                staticFaderRenderers.InsertArrayElementAtIndex(staticFaderRenderers.arraySize);
                SerializedProperty element = staticFaderRenderers.GetArrayElementAtIndex(staticFaderRenderers.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }
        }

        private int GetStaticFaderRendererStartIndex(int faderIndex)
        {
            int start = 0;
            if (staticFaderRendererCounts == null)
            {
                return start;
            }

            int count = Mathf.Min(faderIndex, staticFaderRendererCounts.arraySize);
            for (int i = 0; i < count; i++)
            {
                SerializedProperty countProp = staticFaderRendererCounts.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private int GetStaticFaderRendererCountValue(int faderIndex)
        {
            if (staticFaderRendererCounts == null || faderIndex < 0 || faderIndex >= staticFaderRendererCounts.arraySize)
            {
                return 0;
            }

            SerializedProperty countProp = staticFaderRendererCounts.GetArrayElementAtIndex(faderIndex);
            return Mathf.Max(0, countProp?.intValue ?? 0);
        }

        // ==================== Property Field Methods ====================

        private void DrawDynamicFaderPropertyField(int index)
        {
            if (dynamicFaderPropertyNames == null || dynamicFaderPropertyTypes == null)
            {
                return;
            }

            int folderIndex = GetDynamicFaderFolderIndex(index);
            int toggleIndex = GetDynamicFaderToggleIndex(index);
            if (folderIndex < 0 || toggleIndex < 0)
            {
                return;
            }

            ToggleFolderType folderType = GetFolderType(folderIndex);
            FaderShaderTarget target = BuildDynamicFaderShaderTarget(folderType, folderIndex, toggleIndex, index);
            if ((target.renderers == null || target.renderers.Length == 0) &&
                (target.directMaterials == null || target.directMaterials.Length == 0))
            {
                EditorGUILayout.HelpBox("No renderer targets available for this folder.", MessageType.Info);
                return;
            }

            if (!TryBuildFaderShaderPropertyOptions(
                    target.renderers,
                    target.materialIndices,
                    target.directMaterials,
                    out List<string> propertyNames,
                    out List<ShaderPropertyType> propertyTypes,
                    out string warning,
                    true))
            {
                if (!string.IsNullOrEmpty(warning))
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                return;
            }

            if (propertyNames.Count == 0)
            {
                EditorGUILayout.HelpBox("No shader properties found for this folder's materials.", MessageType.Info);
                return;
            }

            string currentName = GetDynamicFaderPropertyName(index);
            
            // Draw property selection with search button on single line
            // Match the layout behavior of EditorGUILayout.Popup to maintain consistent width
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Property"));
            string displayName = string.IsNullOrEmpty(currentName) ? "(None)" : currentName;
            GUILayout.Label(displayName, EditorStyles.textField);
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                OpenPropertySearchWindow(target, propertyNames, propertyTypes, (selectedName, selectedType) =>
                {
                    SetDynamicFaderPropertyName(index, selectedName);
                    SetDynamicFaderPropertyType(index, ShaderPropertyTypeToPropertyType(selectedType));
                    AutofillDynamicFaderValues(index, selectedName, selectedType, target);
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStaticFaderPropertyField(int faderIndex)
        {
            if (staticFaderPropertyNames == null || staticFaderPropertyTypes == null)
            {
                return;
            }

            FaderShaderTarget target = BuildStaticFaderShaderTarget(faderIndex);
            if ((target.renderers == null || target.renderers.Length == 0) &&
                (target.directMaterials == null || target.directMaterials.Length == 0))
            {
                EditorGUILayout.HelpBox("Select at least one renderer or material target to populate shader properties.", MessageType.Info);
                return;
            }

            if (!TryBuildFaderShaderPropertyOptions(
                    target.renderers,
                    target.materialIndices,
                    target.directMaterials,
                    out List<string> propertyNames,
                    out List<ShaderPropertyType> propertyTypes,
                    out string warning,
                    true))
            {
                if (!string.IsNullOrEmpty(warning))
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                return;
            }

            if (propertyNames.Count == 0)
            {
                EditorGUILayout.HelpBox("No shader properties found for target materials.", MessageType.Info);
                return;
            }

            string currentName = GetStaticFaderPropertyName(faderIndex);
            
            // Draw property selection with search button on single line
            // Match the layout behavior of EditorGUILayout.Popup to maintain consistent width
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Property"));
            string displayName = string.IsNullOrEmpty(currentName) ? "(None)" : currentName;
            GUILayout.Label(displayName, EditorStyles.textField);
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                OpenPropertySearchWindow(target, propertyNames, propertyTypes, (selectedName, selectedType) =>
                {
                    SetStaticFaderPropertyName(faderIndex, selectedName);
                    SetStaticFaderPropertyType(faderIndex, ShaderPropertyTypeToPropertyType(selectedType));
                    AutofillStaticFaderValues(faderIndex, selectedName, selectedType, target);
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        // ==================== Shader Target Building ====================

        private FaderShaderTarget BuildDynamicFaderShaderTarget(ToggleFolderType folderType, int folderIndex, int toggleIndex, int dynamicIndex = -1)
        {
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                    return BuildObjectFolderShaderTarget(folderIndex, toggleIndex, dynamicIndex);

                case ToggleFolderType.Properties:
                    return BuildPropertiesFolderShaderTarget(folderIndex);

                case ToggleFolderType.Materials:
                    return BuildMaterialsFolderShaderTarget(folderIndex, toggleIndex);

                case ToggleFolderType.Skybox:
                    return BuildSkyboxShaderTarget(toggleIndex);

                case ToggleFolderType.Mochie:
                    return BuildMochieShaderTarget();

                case ToggleFolderType.June:
                    return BuildJuneFolderShaderTarget(folderIndex);

                case ToggleFolderType.Shaders:
                    return BuildShadersFolderShaderTarget(folderIndex, toggleIndex);

                default:
                    return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }
        }

        private FaderShaderTarget BuildJuneFolderShaderTarget(int folderIndex)
        {
            SerializedObject handlerObject = GetJuneHandlerObjectForFolder(folderIndex);
            if (handlerObject == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Get the June material from the handler (preferred for property lookup)
            SerializedProperty juneMaterialProp = handlerObject.FindProperty("juneMaterial");
            Material juneMaterial = juneMaterialProp != null ? juneMaterialProp.objectReferenceValue as Material : null;
            if (juneMaterial != null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0, new[] { juneMaterial });
            }

            // Fallback: try to get renderer and use its material
            SerializedProperty rendererProp = handlerObject.FindProperty("juneRenderer");
            Renderer juneRenderer = rendererProp != null ? rendererProp.objectReferenceValue as Renderer : null;
            if (juneRenderer != null)
            {
                return PrepareFaderShaderTarget(new[] { juneRenderer }, 0);
            }

            return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
        }

        private FaderShaderTarget BuildStaticFaderShaderTarget(int faderIndex)
        {
            int materialIndex = GetStaticFaderMaterialIndexValue(faderIndex);

            if (IsStaticFaderCustomTarget(faderIndex))
            {
                return BuildCustomStaticFaderTarget(faderIndex, materialIndex);
            }

            int folderIndex = GetStaticFaderFolderIndex(faderIndex);
            if (folderIndex >= 0)
            {
                ToggleFolderType folderType = GetFolderType(folderIndex);
                return BuildFolderShaderTarget(folderType, folderIndex, materialIndex);
            }

            return PrepareFaderShaderTarget(Array.Empty<Renderer>(), materialIndex);
        }

        private FaderShaderTarget BuildCustomStaticFaderTarget(int faderIndex, int materialIndex)
        {
            int rendererCount = GetStaticFaderRendererCountValue(faderIndex);
            int start = GetStaticFaderRendererStartIndex(faderIndex);
            EnsureStaticFaderRendererArrayCapacity(start + rendererCount);

            Renderer[] renderers = new Renderer[rendererCount];
            for (int i = 0; i < rendererCount; i++)
            {
                int flatIndex = start + i;
                if (flatIndex >= 0 && staticFaderRenderers != null && flatIndex < staticFaderRenderers.arraySize)
                {
                    SerializedProperty rendererProp = staticFaderRenderers.GetArrayElementAtIndex(flatIndex);
                    renderers[i] = rendererProp.objectReferenceValue as Renderer;
                }
            }

            return PrepareFaderShaderTarget(renderers, materialIndex);
        }

        private FaderShaderTarget BuildFolderShaderTarget(ToggleFolderType folderType, int folderIndex, int materialIndex)
        {
            switch (folderType)
            {
                case ToggleFolderType.Properties:
                    return BuildPropertiesFolderShaderTarget(folderIndex);

                case ToggleFolderType.Materials:
                    return BuildMaterialsFolderShaderTarget(folderIndex, -1);

                case ToggleFolderType.Skybox:
                    return BuildSkyboxShaderTarget(-1);

                case ToggleFolderType.Mochie:
                    return BuildMochieShaderTarget();

                case ToggleFolderType.June:
                    return BuildJuneFolderShaderTarget(folderIndex);

                case ToggleFolderType.Shaders:
                    return BuildShadersFolderShaderTarget(folderIndex, -1);

                default:
                    return PrepareFaderShaderTarget(Array.Empty<Renderer>(), materialIndex);
            }
        }

        private FaderShaderTarget BuildObjectFolderShaderTarget(int folderIndex, int toggleIndex, int dynamicIndex)
        {
            SerializedObject objHandlerObj = GetObjectHandlerObjectForFolder(folderIndex);
            if (objHandlerObj == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            SerializedProperty entriesProperty = objHandlerObj.FindProperty("folderEntries");
            if (entriesProperty == null || !entriesProperty.isArray || entriesProperty.arraySize == 0)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Get the GameObject at the toggle index
            if (toggleIndex < 0 || toggleIndex >= entriesProperty.arraySize)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            SerializedProperty entryProp = entriesProperty.GetArrayElementAtIndex(toggleIndex);
            GameObject targetObject = entryProp.objectReferenceValue as GameObject;
            if (targetObject == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Get the renderer from the GameObject
            Renderer targetRenderer = targetObject.GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Get the material index (default to 0 if not specified)
            int materialIndex = 0;
            if (dynamicIndex >= 0)
            {
                materialIndex = GetDynamicFaderMaterialIndexValue(dynamicIndex);
            }

            return PrepareFaderShaderTarget(new[] { targetRenderer }, materialIndex);
        }

        private FaderShaderTarget BuildPropertiesFolderShaderTarget(int folderIndex)
        {
            SerializedObject propHandlerObj = GetPropertyHandlerObjectForFolder(folderIndex);
            if (propHandlerObj == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            SerializedProperty renderersProperty = propHandlerObj.FindProperty("propertyRenderers");
            if (renderersProperty == null || !renderersProperty.isArray || renderersProperty.arraySize == 0)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            Renderer[] renderers = new Renderer[renderersProperty.arraySize];
            for (int i = 0; i < renderersProperty.arraySize; i++)
            {
                SerializedProperty rendererProp = renderersProperty.GetArrayElementAtIndex(i);
                renderers[i] = rendererProp.objectReferenceValue as Renderer;
            }

            return PrepareFaderShaderTarget(renderers, 0);
        }

        private FaderShaderTarget BuildMaterialsFolderShaderTarget(int folderIndex, int toggleIndex)
        {
            SerializedObject matHandlerObj = GetMaterialHandlerObjectForFolder(folderIndex);
            if (matHandlerObj == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Get renderers from MaterialHandler
            SerializedProperty renderersProperty = matHandlerObj.FindProperty("folderMaterialRenderers");
            Renderer[] renderers = Array.Empty<Renderer>();
            if (renderersProperty != null && renderersProperty.isArray && renderersProperty.arraySize > 0)
            {
                renderers = new Renderer[renderersProperty.arraySize];
                for (int i = 0; i < renderersProperty.arraySize; i++)
                {
                    SerializedProperty rendererProp = renderersProperty.GetArrayElementAtIndex(i);
                    renderers[i] = rendererProp.objectReferenceValue as Renderer;
                }
            }

            // Get materials from MaterialHandler.folderEntries for property lookup
            SerializedProperty materialsProperty = matHandlerObj.FindProperty("folderEntries");
            Material[] directMaterials = null;
            if (materialsProperty != null && materialsProperty.isArray && materialsProperty.arraySize > 0)
            {
                // If toggleIndex is specified, only use that material
                if (toggleIndex >= 0 && toggleIndex < materialsProperty.arraySize)
                {
                    SerializedProperty matProp = materialsProperty.GetArrayElementAtIndex(toggleIndex);
                    Material mat = matProp.objectReferenceValue as Material;
                    if (mat != null)
                    {
                        directMaterials = new[] { mat };
                    }
                }
                else
                {
                    // Use all materials
                    List<Material> mats = new List<Material>();
                    for (int i = 0; i < materialsProperty.arraySize; i++)
                    {
                        SerializedProperty matProp = materialsProperty.GetArrayElementAtIndex(i);
                        Material mat = matProp.objectReferenceValue as Material;
                        if (mat != null)
                        {
                            mats.Add(mat);
                        }
                    }
                    directMaterials = mats.ToArray();
                }
            }

            return PrepareFaderShaderTarget(renderers, 0, directMaterials);
        }

        private FaderShaderTarget BuildSkyboxShaderTarget(int toggleIndex)
        {
            // If toggleIndex is specified, only use that single skybox material
            if (toggleIndex >= 0 && skyboxMaterials != null && skyboxMaterials.isArray && toggleIndex < skyboxMaterials.arraySize)
            {
                SerializedProperty matProp = skyboxMaterials.GetArrayElementAtIndex(toggleIndex);
                Material mat = matProp != null ? matProp.objectReferenceValue as Material : null;
                if (mat != null)
                {
                    return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0, new[] { mat });
                }
            }

            // Otherwise, use all skybox materials
            Material[] skyboxTargets = BuildSkyboxReferenceMaterials();
            return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0, skyboxTargets);
        }

        private FaderShaderTarget BuildMochieShaderTarget()
        {
            Renderer mochieRenderer = shaderRenderer != null ? shaderRenderer.objectReferenceValue as Renderer : null;
            Material[] mochieMaterials = BuildMochieReferenceMaterials();
            Renderer[] renderers = mochieRenderer != null ? new[] { mochieRenderer } : Array.Empty<Renderer>();
            return PrepareFaderShaderTarget(renderers, 0, mochieMaterials);
        }

        private Material[] BuildSkyboxReferenceMaterials()
        {
            List<Material> skyboxTargets = new List<Material>();
            if (skyboxMaterials != null && skyboxMaterials.isArray)
            {
                for (int i = 0; i < skyboxMaterials.arraySize; i++)
                {
                    SerializedProperty matProp = skyboxMaterials.GetArrayElementAtIndex(i);
                    Material mat = matProp != null ? matProp.objectReferenceValue as Material : null;
                    if (mat != null)
                    {
                        skyboxTargets.Add(mat);
                    }
                }
            }

            return skyboxTargets.ToArray();
        }

        private Material[] BuildMochieReferenceMaterials()
        {
            if (mochiHandlerObject == null)
            {
                return Array.Empty<Material>();
            }

            SerializedProperty activeMaterialProp = mochiHandlerObject.FindProperty("activeMochieMaterial");
            Material activeMaterial = activeMaterialProp != null ? activeMaterialProp.objectReferenceValue as Material : null;
            if (activeMaterial != null)
            {
                return new[] { activeMaterial };
            }

            // Fallback: try to get the configured Mochie materials (mochieMaterialStandard or mochieMaterialX)
            // These are assigned in the editor on the main EnigmaLaunchpad component
            Material standardMaterial = mochieMaterialStandardProperty?.objectReferenceValue as Material;
            if (standardMaterial != null)
            {
                return new[] { standardMaterial };
            }

            Material xMaterial = mochieMaterialXProperty?.objectReferenceValue as Material;
            if (xMaterial != null)
            {
                return new[] { xMaterial };
            }

            // Legacy fallback: try to get initial materials
            SerializedProperty initialMaterialsProp = mochiHandlerObject.FindProperty("initialMaterials");
            if (initialMaterialsProp != null && initialMaterialsProp.isArray && initialMaterialsProp.arraySize > 0)
            {
                SerializedProperty firstMat = initialMaterialsProp.GetArrayElementAtIndex(0);
                Material mat = firstMat != null ? firstMat.objectReferenceValue as Material : null;
                if (mat != null)
                {
                    return new[] { mat };
                }
            }

            return Array.Empty<Material>();
        }

        private FaderShaderTarget PrepareFaderShaderTarget(Renderer[] renderers, int materialIndex, Material[] directMaterials = null)
        {
            Renderer[] targetRenderers = renderers ?? Array.Empty<Renderer>();
            int[] materialIndices = (targetRenderers.Length > 0)
                ? Enumerable.Repeat(Mathf.Max(0, materialIndex), targetRenderers.Length).ToArray()
                : Array.Empty<int>();

            FaderShaderTarget target = new FaderShaderTarget();
            target.renderers = targetRenderers;
            target.materialIndices = materialIndices;
            target.directMaterials = directMaterials ?? Array.Empty<Material>();

            return target;
        }

        // ==================== Property Options Building ====================

        private bool TryBuildFaderShaderPropertyOptions(
            Renderer[] renderers,
            int[] materialIndices,
            Material[] directMaterials,
            out List<string> propertyNames,
            out List<ShaderPropertyType> propertyTypes,
            out string warning,
            bool floatRangeOnly)
        {
            propertyNames = null;
            propertyTypes = null;
            warning = null;

            // If we have direct materials, use those for property inspection
            if (directMaterials != null && directMaterials.Length > 0)
            {
                return TryBuildPropertyOptionsFromMaterials(directMaterials, out propertyNames, out propertyTypes, out warning, floatRangeOnly);
            }

            // Otherwise use renderers
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

            Dictionary<string, ShaderPropertyType> sharedProperties = GetCommonShaderPropertiesFromRenderers(renderers, materialIndices, out List<string> propertyOrder, floatRangeOnly);
            if (sharedProperties == null || sharedProperties.Count == 0)
            {
                warning = "No shared shader properties found across all target renderers.";
                return false;
            }

            propertyNames = propertyOrder ?? sharedProperties.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
            propertyTypes = propertyNames.Select(name => sharedProperties[name]).ToList();
            return true;
        }

        private bool TryBuildPropertyOptionsFromMaterials(
            Material[] materials,
            out List<string> propertyNames,
            out List<ShaderPropertyType> propertyTypes,
            out string warning,
            bool floatRangeOnly)
        {
            propertyNames = null;
            propertyTypes = null;
            warning = null;

            if (materials == null || materials.Length == 0)
            {
                warning = "No materials available for property selection.";
                return false;
            }

            Dictionary<string, ShaderPropertyType> sharedProperties = null;
            List<string> orderedKeys = null;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || material.shader == null)
                {
                    continue;
                }

                Dictionary<string, ShaderPropertyType> materialProperties = CollectMaterialShaderProperties(material, floatRangeOnly, out List<string> materialOrder);
                if (materialProperties == null || materialProperties.Count == 0)
                {
                    continue;
                }

                if (sharedProperties == null)
                {
                    sharedProperties = materialProperties;
                    orderedKeys = materialOrder;
                    continue;
                }

                // Intersect properties
                var keys = new List<string>(sharedProperties.Keys);
                foreach (string key in keys)
                {
                    if (!materialProperties.TryGetValue(key, out ShaderPropertyType matType) || sharedProperties[key] != matType)
                    {
                        sharedProperties.Remove(key);
                    }
                }

                if (orderedKeys != null)
                {
                    orderedKeys = orderedKeys.Where(sharedProperties.ContainsKey).ToList();
                }
            }

            if (sharedProperties == null || sharedProperties.Count == 0)
            {
                warning = "No common shader properties found across materials.";
                return false;
            }

            propertyNames = orderedKeys ?? sharedProperties.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
            propertyTypes = propertyNames.Select(name => sharedProperties[name]).ToList();
            return true;
        }

        private Dictionary<string, ShaderPropertyType> CollectMaterialShaderProperties(Material material, bool floatRangeOnly, out List<string> orderedKeys)
        {
            orderedKeys = new List<string>();
            Dictionary<string, ShaderPropertyType> properties = new Dictionary<string, ShaderPropertyType>();

            if (material == null || material.shader == null)
            {
                return properties;
            }

            Shader shader = material.shader;
            int propCount = shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
            {
                ShaderPropertyType propType = shader.GetPropertyType(i);
                if (floatRangeOnly && propType != ShaderPropertyType.Float && propType != ShaderPropertyType.Range && propType != ShaderPropertyType.Color)
                {
                    continue;
                }

                string propName = shader.GetPropertyName(i);
                properties[propName] = propType;
                orderedKeys.Add(propName);
            }

            return properties;
        }

        private Dictionary<string, ShaderPropertyType> GetCommonShaderPropertiesFromRenderers(
            Renderer[] renderers,
            int[] materialIndices,
            out List<string> orderedKeys,
            bool floatRangeOnly)
        {
            orderedKeys = null;

            if (renderers == null || renderers.Length == 0 || materialIndices == null || materialIndices.Length != renderers.Length)
            {
                return new Dictionary<string, ShaderPropertyType>();
            }

            Dictionary<string, ShaderPropertyType> sharedProperties = null;

            for (int idx = 0; idx < renderers.Length; idx++)
            {
                Renderer renderer = renderers[idx];
                if (renderer == null)
                {
                    return new Dictionary<string, ShaderPropertyType>();
                }

                int materialIndex = materialIndices[idx];
                Material targetMaterial = ResolveTargetMaterial(renderer, materialIndex, out string _);
                if (targetMaterial == null || targetMaterial.shader == null)
                {
                    return new Dictionary<string, ShaderPropertyType>();
                }

                Dictionary<string, ShaderPropertyType> rendererProperties = CollectMaterialShaderProperties(targetMaterial, floatRangeOnly, out List<string> rendererOrder);

                if (sharedProperties == null)
                {
                    sharedProperties = rendererProperties;
                    orderedKeys = rendererOrder;
                    continue;
                }

                if (sharedProperties.Count == 0)
                {
                    break;
                }

                var keys = new List<string>(sharedProperties.Keys);
                foreach (string key in keys)
                {
                    if (!rendererProperties.TryGetValue(key, out ShaderPropertyType rendererType) || sharedProperties[key] != rendererType)
                    {
                        sharedProperties.Remove(key);
                    }
                }

                if (orderedKeys != null)
                {
                    orderedKeys = orderedKeys.Where(sharedProperties.ContainsKey).ToList();
                }
            }

            if (sharedProperties == null)
            {
                sharedProperties = new Dictionary<string, ShaderPropertyType>();
            }

            if (orderedKeys == null && sharedProperties.Count > 0)
            {
                orderedKeys = sharedProperties.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
            }

            return sharedProperties;
        }

        // ==================== Property Search Window ====================

        /// <summary>
        /// Opens a search window for selecting shader properties with hierarchical organization
        /// </summary>
        private void OpenPropertySearchWindow(
            FaderShaderTarget target,
            List<string> propertyNames,
            List<ShaderPropertyType> propertyTypes,
            Action<string, ShaderPropertyType> onSelect)
        {
            var searchWindow = new PropertySearchWindow("Shader Properties");
            var mainGroup = searchWindow.GetMainGroup();
            
            // Build property map for quick lookup
            var propertyMap = new Dictionary<string, ShaderPropertyType>();
            for (int i = 0; i < propertyNames.Count && i < propertyTypes.Count; i++)
            {
                propertyMap[propertyNames[i]] = propertyTypes[i];
            }

            // Since propertyNames already contains only shared properties,
            // we don't need to group by renderer/material - just show them directly
            // Get a representative material to extract property descriptions
            Material representativeMaterial = null;
            if (target.directMaterials != null && target.directMaterials.Length > 0)
            {
                representativeMaterial = target.directMaterials[0];
            }
            else if (target.renderers != null && target.renderers.Length > 0)
            {
                int matIndex = target.materialIndices != null && target.materialIndices.Length > 0 
                    ? target.materialIndices[0] 
                    : 0;
                representativeMaterial = ResolveTargetMaterial(target.renderers[0], matIndex, out _);
            }

            if (representativeMaterial != null)
            {
                AddPropertiesFromMaterial(mainGroup, representativeMaterial, propertyMap);
            }

            searchWindow.Open(propName => {
                if (propertyMap.TryGetValue(propName, out ShaderPropertyType propType))
                {
                    onSelect(propName, propType);
                    // Apply changes immediately and force repaint
                    if (faderHandlerObject != null)
                    {
                        faderHandlerObject.ApplyModifiedProperties();
                    }
                    Repaint();
                }
            });
        }

        /// <summary>
        /// Adds properties from a material to a search window group, organized by shader description sections
        /// </summary>
        private void AddPropertiesFromMaterial(
            PropertySearchWindow.Group group,
            Material material,
            Dictionary<string, ShaderPropertyType> propertyMap)
        {
            if (material == null || material.shader == null) return;
            
            Shader shader = material.shader;
            int propCount = shader.GetPropertyCount();
            
            for (int i = 0; i < propCount; i++)
            {
                string propName = shader.GetPropertyName(i);
                
                // Only include properties that are in the available property map
                if (!propertyMap.ContainsKey(propName)) continue;
                
                ShaderPropertyType propType = shader.GetPropertyType(i);
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
                string typeIndicator = GetPropertyTypeIndicator(propType);
                if (!string.IsNullOrEmpty(typeIndicator))
                {
                    entryName += $" [{typeIndicator}]";
                }
                
                group.Add(entryName, propName);
            }
        }

        /// <summary>
        /// Gets a short indicator string for the property type
        /// </summary>
        private string GetPropertyTypeIndicator(ShaderPropertyType propType)
        {
            switch (propType)
            {
                case ShaderPropertyType.Color:
                    return "Color";
                case ShaderPropertyType.Vector:
                    return "Vector";
                case ShaderPropertyType.Float:
                    return "Float";
                case ShaderPropertyType.Range:
                    return "Range";
                case ShaderPropertyType.Texture:
                    return "Texture";
                default:
                    return "";
            }
        }

        // ==================== Property Name/Type Accessors ====================

        private string GetDynamicFaderPropertyName(int index)
        {
            if (dynamicFaderPropertyNames == null || index < 0 || index >= dynamicFaderPropertyNames.arraySize)
            {
                return string.Empty;
            }

            SerializedProperty prop = dynamicFaderPropertyNames.GetArrayElementAtIndex(index);
            return prop != null ? prop.stringValue : string.Empty;
        }

        private void SetDynamicFaderPropertyName(int index, string value)
        {
            if (dynamicFaderPropertyNames == null || index < 0 || index >= dynamicFaderPropertyNames.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderPropertyNames.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.stringValue = value;
            }
        }

        private int GetDynamicFaderPropertyType(int index)
        {
            if (dynamicFaderPropertyTypes == null || index < 0 || index >= dynamicFaderPropertyTypes.arraySize)
            {
                return 0;
            }

            SerializedProperty prop = dynamicFaderPropertyTypes.GetArrayElementAtIndex(index);
            return prop != null ? prop.intValue : 0;
        }

        private void SetDynamicFaderPropertyType(int index, int value)
        {
            if (dynamicFaderPropertyTypes == null || index < 0 || index >= dynamicFaderPropertyTypes.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderPropertyTypes.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.intValue = value;
            }
        }

        private string GetStaticFaderPropertyName(int faderIndex)
        {
            if (staticFaderPropertyNames == null || faderIndex < 0 || faderIndex >= staticFaderPropertyNames.arraySize)
            {
                return string.Empty;
            }

            SerializedProperty prop = staticFaderPropertyNames.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.stringValue : string.Empty;
        }

        private void SetStaticFaderPropertyName(int faderIndex, string value)
        {
            if (staticFaderPropertyNames == null || faderIndex < 0 || faderIndex >= staticFaderPropertyNames.arraySize)
            {
                return;
            }

            SerializedProperty prop = staticFaderPropertyNames.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.stringValue = value;
            }
        }

        private int GetStaticFaderPropertyType(int faderIndex)
        {
            if (staticFaderPropertyTypes == null || faderIndex < 0 || faderIndex >= staticFaderPropertyTypes.arraySize)
            {
                return 0;
            }

            SerializedProperty prop = staticFaderPropertyTypes.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.intValue : 0;
        }

        private void SetStaticFaderPropertyType(int faderIndex, int value)
        {
            if (staticFaderPropertyTypes == null || faderIndex < 0 || faderIndex >= staticFaderPropertyTypes.arraySize)
            {
                return;
            }

            SerializedProperty prop = staticFaderPropertyTypes.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.intValue = value;
            }
        }

        private int GetStaticFaderMaterialIndexValue(int faderIndex)
        {
            if (staticFaderMaterialIndices == null || faderIndex < 0 || faderIndex >= staticFaderMaterialIndices.arraySize)
            {
                return 0;
            }

            SerializedProperty prop = staticFaderMaterialIndices.GetArrayElementAtIndex(faderIndex);
            return prop != null ? Mathf.Max(0, prop.intValue) : 0;
        }

        private int GetStaticFaderFolderIndex(int faderIndex)
        {
            if (staticFaderTargetFolders == null || faderIndex < 0 || faderIndex >= staticFaderTargetFolders.arraySize)
            {
                return -1;
            }

            SerializedProperty prop = staticFaderTargetFolders.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.intValue : -1;
        }

        private bool IsStaticFaderCustomTarget(int faderIndex)
        {
            if (staticFaderTargetsCustom == null || faderIndex < 0 || faderIndex >= staticFaderTargetsCustom.arraySize)
            {
                return false;
            }

            SerializedProperty prop = staticFaderTargetsCustom.GetArrayElementAtIndex(faderIndex);
            return prop != null && prop.boolValue;
        }

        private int ShaderPropertyTypeToPropertyType(ShaderPropertyType shaderType)
        {
            switch (shaderType)
            {
                case ShaderPropertyType.Float:
                    return 0;
                case ShaderPropertyType.Range:
                    return 1;
                case ShaderPropertyType.Color:
                    return 2;
                case ShaderPropertyType.Vector:
                    return 3;
                case ShaderPropertyType.Texture:
                    return 4;
                default:
                    return 0;
            }
        }

        // ==================== Property Value Autofill ====================

        /// <summary>
        /// Gets the first valid material from a FaderShaderTarget for reading property values.
        /// </summary>
        private Material GetFirstMaterialFromTarget(FaderShaderTarget target)
        {
            // Prefer direct materials first
            if (target.directMaterials != null && target.directMaterials.Length > 0)
            {
                for (int i = 0; i < target.directMaterials.Length; i++)
                {
                    Material mat = target.directMaterials[i];
                    if (mat != null && mat.shader != null)
                    {
                        return mat;
                    }
                }
            }

            // Fall back to renderer materials
            if (target.renderers != null && target.materialIndices != null &&
                target.renderers.Length > 0 && target.materialIndices.Length > 0)
            {
                for (int i = 0; i < target.renderers.Length; i++)
                {
                    Renderer renderer = target.renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    int matIndex = i < target.materialIndices.Length ? target.materialIndices[i] : 0;
                    Material mat = ResolveTargetMaterial(renderer, matIndex, out string _);
                    if (mat != null && mat.shader != null)
                    {
                        return mat;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Reads property values from a material to autofill fader defaults.
        /// Returns true if values were successfully read.
        /// </summary>
        private bool TryGetPropertyValuesFromMaterial(
            Material material,
            string propertyName,
            ShaderPropertyType propertyType,
            out float defaultValue,
            out float minValue,
            out float maxValue)
        {
            defaultValue = 0f;
            minValue = 0f;
            maxValue = 1f;

            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            // Check if the material has this property
            if (!material.HasProperty(propertyName))
            {
                return false;
            }

            Shader shader = material.shader;
            int propIndex = shader.FindPropertyIndex(propertyName);
            if (propIndex < 0)
            {
                return false;
            }

            // For Range properties, get the min/max limits from the shader
            if (propertyType == ShaderPropertyType.Range)
            {
                // Get the current value from the material as the default
                defaultValue = material.GetFloat(propertyName);

                Vector2 rangeLimits = shader.GetPropertyRangeLimits(propIndex);
                minValue = rangeLimits.x;
                maxValue = rangeLimits.y;

                // Clamp default to be within range
                defaultValue = Mathf.Clamp(defaultValue, minValue, maxValue);
            }
            else if (propertyType == ShaderPropertyType.Color)
            {
                // For color properties, we don't use float values
                // Set defaults for hue shift: min=0, max=360, default=0
                defaultValue = 0f;
                minValue = 0f;
                maxValue = 360f;
            }
            else
            {
                // Get the current value from the material as the default
                defaultValue = material.GetFloat(propertyName);

                // For Float properties, use sensible defaults or infer from current value
                // If the current value is outside [0,1], expand the range
                if (defaultValue < 0f)
                {
                    // For negative values, create a range from the negative value to its positive counterpart
                    // This allows the fader to span both negative and positive values (e.g., -5 to 5)
                    minValue = defaultValue;
                    maxValue = Mathf.Max(1f, -minValue); // At least 1, or the positive magnitude of the default
                }
                else if (defaultValue > 1f)
                {
                    minValue = 0f;
                    maxValue = defaultValue * FloatRangeExpansionFactor; // Give some headroom above current value
                }
            }

            return true;
        }

        /// <summary>
        /// Autofills the static fader min/max/default values based on the selected property and target material.
        /// </summary>
        private void AutofillStaticFaderValues(int faderIndex, string propertyName, ShaderPropertyType propertyType, FaderShaderTarget target)
        {
            Material material = GetFirstMaterialFromTarget(target);
            if (material == null)
            {
                return;
            }

            if (TryGetPropertyValuesFromMaterial(material, propertyName, propertyType, out float defaultValue, out float minValue, out float maxValue))
            {
                SetStaticFaderMinValue(faderIndex, minValue);
                SetStaticFaderMaxValue(faderIndex, maxValue);
                SetStaticFaderDefaultValue(faderIndex, defaultValue);
            }

            // For color properties, also get the color from the material
            if (propertyType == ShaderPropertyType.Color && material.HasProperty(propertyName))
            {
                Color materialColor = material.GetColor(propertyName);
                if (staticFaderDefaultColors != null && faderIndex >= 0 && faderIndex < staticFaderDefaultColors.arraySize)
                {
                    SerializedProperty colorProp = staticFaderDefaultColors.GetArrayElementAtIndex(faderIndex);
                    if (colorProp != null)
                    {
                        colorProp.colorValue = materialColor;
                    }
                }
            }
        }

        /// <summary>
        /// Autofills the dynamic fader min/max/default values based on the selected property and target material.
        /// </summary>
        private void AutofillDynamicFaderValues(int index, string propertyName, ShaderPropertyType propertyType, FaderShaderTarget target)
        {
            Material material = GetFirstMaterialFromTarget(target);
            if (material == null)
            {
                return;
            }

            if (TryGetPropertyValuesFromMaterial(material, propertyName, propertyType, out float defaultValue, out float minValue, out float maxValue))
            {
                SetDynamicFaderMinValue(index, minValue);
                SetDynamicFaderMaxValue(index, maxValue);
                SetDynamicFaderDefaultValue(index, defaultValue);
            }

            // For color properties, also get the color from the material
            if (propertyType == ShaderPropertyType.Color && material.HasProperty(propertyName))
            {
                Color materialColor = material.GetColor(propertyName);
                if (dynamicFaderDefaultColors != null && index >= 0 && index < dynamicFaderDefaultColors.arraySize)
                {
                    SerializedProperty colorProp = dynamicFaderDefaultColors.GetArrayElementAtIndex(index);
                    if (colorProp != null)
                    {
                        colorProp.colorValue = materialColor;
                    }
                }
            }
        }

        /// <summary>
        /// Updates both static and dynamic fader folder indices after a folder move operation.
        /// This preserves fader assignments when folders are reordered.
        /// </summary>
        /// <param name="from">Original folder index</param>
        /// <param name="to">New folder index</param>
        private void UpdateFaderFolderIndices(int from, int to)
        {
            // Update static fader folder indices
            if (staticFaderTargetFolders != null && staticFaderTargetFolders.arraySize > 0)
            {
                for (int i = 0; i < staticFaderTargetFolders.arraySize; i++)
                {
                    SerializedProperty folderIndexProp = staticFaderTargetFolders.GetArrayElementAtIndex(i);
                    if (folderIndexProp == null) continue;

                    int currentFolderIndex = folderIndexProp.intValue;
                    
                    // Only update valid folder indices (>= 0)
                    if (currentFolderIndex < 0) continue;

                    // Apply the same transformation as defaultFolderIndex
                    if (currentFolderIndex == from)
                    {
                        // The folder this fader points to was moved
                        folderIndexProp.intValue = to;
                    }
                    else if (from < to && currentFolderIndex > from && currentFolderIndex <= to)
                    {
                        // Folders between from and to shift down
                        folderIndexProp.intValue--;
                    }
                    else if (from > to && currentFolderIndex >= to && currentFolderIndex < from)
                    {
                        // Folders between to and from shift up
                        folderIndexProp.intValue++;
                    }
                }
            }

            // Update dynamic fader folder indices
            if (dynamicFaderFolders != null && dynamicFaderFolders.arraySize > 0)
            {
                for (int i = 0; i < dynamicFaderFolders.arraySize; i++)
                {
                    SerializedProperty folderIndexProp = dynamicFaderFolders.GetArrayElementAtIndex(i);
                    if (folderIndexProp == null) continue;

                    int currentFolderIndex = folderIndexProp.intValue;
                    
                    // Only update valid folder indices (>= 0)
                    if (currentFolderIndex < 0) continue;

                    // Apply the same transformation as defaultFolderIndex
                    if (currentFolderIndex == from)
                    {
                        // The folder this fader points to was moved
                        folderIndexProp.intValue = to;
                    }
                    else if (from < to && currentFolderIndex > from && currentFolderIndex <= to)
                    {
                        // Folders between from and to shift down
                        folderIndexProp.intValue--;
                    }
                    else if (from > to && currentFolderIndex >= to && currentFolderIndex < from)
                    {
                        // Folders between to and from shift up
                        folderIndexProp.intValue++;
                    }
                }
            }
        }
    }
}
#endif

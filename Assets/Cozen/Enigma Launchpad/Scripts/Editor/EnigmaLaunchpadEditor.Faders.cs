#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDKBase;
using VRC.Udon;
using UdonSharp;
using UdonSharp.Compiler;

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
        private SerializedProperty staticFaderTargetsUdon;
        private SerializedProperty staticFaderUdonBehaviours;
        private SerializedProperty staticFaderUdonCounts;
        private SerializedProperty staticFaderUdonVariableNames;
        private SerializedProperty staticFaderTargetsSlider;
        private SerializedProperty staticFaderSliders;
        private SerializedProperty staticFaderSliderCounts;
        private SerializedProperty staticFaderSliderReversed;

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
        private SerializedProperty dynamicFaderTargetsUdon;
        private SerializedProperty dynamicFaderUdonBehaviours;
        private SerializedProperty dynamicFaderUdonCounts;
        private SerializedProperty dynamicFaderUdonVariableNames;
        private SerializedProperty dynamicFaderTargetsSlider;
        private SerializedProperty dynamicFaderSliders;
        private SerializedProperty dynamicFaderSliderCounts;
        private SerializedProperty dynamicFaderSliderReversed;

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
            staticFaderTargetsUdon = null;
            staticFaderUdonBehaviours = null;
            staticFaderUdonCounts = null;
            staticFaderUdonVariableNames = null;
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
            dynamicFaderTargetsUdon = null;
            dynamicFaderUdonBehaviours = null;
            dynamicFaderUdonCounts = null;
            dynamicFaderUdonVariableNames = null;

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
            staticFaderTargetsUdon = faderHandlerObject.FindProperty("staticFaderTargetsUdon");
            staticFaderUdonBehaviours = faderHandlerObject.FindProperty("staticFaderUdonBehaviours");
            staticFaderUdonCounts = faderHandlerObject.FindProperty("staticFaderUdonCounts");
            staticFaderUdonVariableNames = faderHandlerObject.FindProperty("staticFaderUdonVariableNames");
            staticFaderTargetsSlider = faderHandlerObject.FindProperty("staticFaderTargetsSlider");
            staticFaderSliders = faderHandlerObject.FindProperty("staticFaderSliders");
            staticFaderSliderCounts = faderHandlerObject.FindProperty("staticFaderSliderCounts");
            staticFaderSliderReversed = faderHandlerObject.FindProperty("staticFaderSliderReversed");

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
            dynamicFaderTargetsUdon = faderHandlerObject.FindProperty("dynamicFaderTargetsUdon");
            dynamicFaderUdonBehaviours = faderHandlerObject.FindProperty("dynamicFaderUdonBehaviours");
            dynamicFaderUdonCounts = faderHandlerObject.FindProperty("dynamicFaderUdonCounts");
            dynamicFaderUdonVariableNames = faderHandlerObject.FindProperty("dynamicFaderUdonVariableNames");
            dynamicFaderTargetsSlider = faderHandlerObject.FindProperty("dynamicFaderTargetsSlider");
            dynamicFaderSliders = faderHandlerObject.FindProperty("dynamicFaderSliders");
            dynamicFaderSliderCounts = faderHandlerObject.FindProperty("dynamicFaderSliderCounts");
            dynamicFaderSliderReversed = faderHandlerObject.FindProperty("dynamicFaderSliderReversed");

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
                SerializedProperty vrcPickupProp = faderObject.FindProperty("vrcPickup");
                SerializedProperty faderRigidbodyProp = faderObject.FindProperty("faderRigidbody");

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

                // Auto-assign VRC_Pickup from the same GameObject if not already assigned
                if (vrcPickupProp != null && vrcPickupProp.objectReferenceValue == null)
                {
                    VRC_Pickup pickup = fader.GetComponent<VRC_Pickup>();
                    if (pickup != null)
                    {
                        vrcPickupProp.objectReferenceValue = pickup;
                        needsUpdate = true;
                    }
                }

                // Auto-assign Rigidbody from the same GameObject if not already assigned
                if (faderRigidbodyProp != null && faderRigidbodyProp.objectReferenceValue == null)
                {
                    Rigidbody rigidbody = fader.GetComponent<Rigidbody>();
                    if (rigidbody != null)
                    {
                        faderRigidbodyProp.objectReferenceValue = rigidbody;
                        needsUpdate = true;
                    }
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
            EnsureArraySize(staticFaderTargetsUdon, FaderCount, prop => prop.boolValue = false);
            EnsureArraySize(staticFaderUdonCounts, FaderCount, prop => prop.intValue = 0);
            EnsureArraySize(staticFaderUdonVariableNames, FaderCount, prop => prop.stringValue = string.Empty);
            EnsureArraySize(staticFaderTargetsSlider, FaderCount, prop => prop.boolValue = false);
            EnsureArraySize(staticFaderSliderCounts, FaderCount, prop => prop.intValue = 0);
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
            if (dynamicFaderTargetsUdon != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderTargetsUdon.arraySize);
            }
            if (dynamicFaderUdonCounts != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderUdonCounts.arraySize);
            }
            if (dynamicFaderUdonVariableNames != null)
            {
                maxSize = Mathf.Max(maxSize, dynamicFaderUdonVariableNames.arraySize);
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
            EnsureDynamicFaderArraySize(dynamicFaderTargetsUdon, maxSize, prop => prop.boolValue = false);
            EnsureDynamicFaderArraySize(dynamicFaderUdonCounts, maxSize, prop => prop.intValue = 0);
            EnsureDynamicFaderArraySize(dynamicFaderUdonVariableNames, maxSize, prop => prop.stringValue = string.Empty);
            EnsureDynamicFaderArraySize(dynamicFaderTargetsSlider, maxSize, prop => prop.boolValue = false);
            EnsureDynamicFaderArraySize(dynamicFaderSliderCounts, maxSize, prop => prop.intValue = 0);
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

                // Check if targeting UdonBehaviour
                bool udonToggle = false;
                SerializedProperty udonProp = (staticFaderTargetsUdon != null && faderIndex < staticFaderTargetsUdon.arraySize)
                    ? staticFaderTargetsUdon.GetArrayElementAtIndex(faderIndex)
                    : null;

                // Check if targeting Unity Slider
                bool sliderToggle = false;
                SerializedProperty sliderProp = (staticFaderTargetsSlider != null && faderIndex < staticFaderTargetsSlider.arraySize)
                    ? staticFaderTargetsSlider.GetArrayElementAtIndex(faderIndex)
                    : null;

                if (sliderProp != null)
                {
                    sliderToggle = sliderProp.boolValue;
                }

                if (udonProp != null)
                {
                    udonToggle = udonProp.boolValue;
                }

                // Unity Slider targeting (exclusive with other options)
                if (!udonToggle && sliderProp != null)
                {
                    bool newSliderToggle = EditorGUILayout.ToggleLeft("Unity Slider", sliderToggle);
                    if (newSliderToggle != sliderToggle)
                    {
                        sliderProp.boolValue = newSliderToggle;
                        sliderToggle = newSliderToggle;
                        // Clear other target options when switching to Slider
                        if (newSliderToggle)
                        {
                            if (udonProp != null)
                            {
                                udonProp.boolValue = false;
                            }
                            if (staticFaderTargetFolders != null && faderIndex < staticFaderTargetFolders.arraySize)
                            {
                                staticFaderTargetFolders.GetArrayElementAtIndex(faderIndex).intValue = -1;
                            }
                            if (staticFaderTargetsCustom != null && faderIndex < staticFaderTargetsCustom.arraySize)
                            {
                                staticFaderTargetsCustom.GetArrayElementAtIndex(faderIndex).boolValue = false;
                            }
                        }
                    }
                }

                if (sliderToggle)
                {
                    GUILayout.Space(4);
                    DrawStaticFaderSliderList(faderIndex);
                }
                else
                {
                    // Show Udon Behavior option only when Slider is not selected
                    if (udonProp != null)
                    {
                        bool newUdonToggle = EditorGUILayout.ToggleLeft("Udon Behavior", udonToggle);
                        if (newUdonToggle != udonToggle)
                        {
                            udonProp.boolValue = newUdonToggle;
                            udonToggle = newUdonToggle;
                            // Clear other target options when switching to Udon
                            if (newUdonToggle)
                            {
                                if (sliderProp != null)
                                {
                                    sliderProp.boolValue = false;
                                }
                                if (staticFaderTargetFolders != null && faderIndex < staticFaderTargetFolders.arraySize)
                                {
                                    staticFaderTargetFolders.GetArrayElementAtIndex(faderIndex).intValue = -1;
                                }
                                if (staticFaderTargetsCustom != null && faderIndex < staticFaderTargetsCustom.arraySize)
                                {
                                    staticFaderTargetsCustom.GetArrayElementAtIndex(faderIndex).boolValue = false;
                                }
                            }
                        }
                    }

                    if (udonToggle)
                    {
                        GUILayout.Space(4);
                        DrawStaticFaderUdonList(faderIndex);
                    }
                    else
                    {
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
                    }
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

            // Skip Material Index when targeting Udon or Slider (not applicable)
            if (IsStaticFaderTargetingUdon(faderIndex) || IsStaticFaderTargetingSlider(faderIndex))
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

            // Check if targeting UdonBehaviour
            bool targetsUdon = IsStaticFaderTargetingUdon(faderIndex);

            if (targetsUdon)
            {
                // For UdonBehaviour, check if a variable has been selected
                string variableName = GetStaticFaderUdonVariableName(faderIndex);
                if (string.IsNullOrEmpty(variableName))
                {
                    return;
                }

                // For Udon variables, always show float range fields (no color support)
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
            else
            {
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

            // Check if this is a color property (propertyType == 2)
            int propertyType = GetStaticFaderPropertyType(faderIndex);
            bool isColorProperty = propertyType == 2;

            if (isColorProperty)
            {
                // For color properties, show informational note instead of color picker
                EditorGUILayout.HelpBox(
                    "The indicator will display the color currently applied by this fader.",
                    MessageType.Info
                );
            }
            else
            {
                // For non-color properties, show the color picker
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

        private void DrawStaticFaderSliderList(int faderIndex)
        {
            if (staticFaderSliders == null || staticFaderSliderCounts == null)
            {
                return;
            }

            SerializedProperty countProp = (faderIndex >= 0 && faderIndex < staticFaderSliderCounts.arraySize)
                ? staticFaderSliderCounts.GetArrayElementAtIndex(faderIndex)
                : null;

            if (countProp == null)
            {
                return;
            }

            int sliderCount = Mathf.Max(0, countProp.intValue);
            int sliderStart = GetStaticFaderSliderStartIndex(faderIndex);
            EnsureStaticFaderSliderArrayCapacity(sliderStart + sliderCount);

            bool structuralChange = false;

            for (int i = 0; i < sliderCount; i++)
            {
                int flatIndex = sliderStart + i;
                if (flatIndex < 0 || flatIndex >= staticFaderSliders.arraySize)
                {
                    break;
                }

                SerializedProperty sliderProp = staticFaderSliders.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.BeginHorizontal();
                UnityEngine.UI.Slider current = sliderProp.objectReferenceValue as UnityEngine.UI.Slider;
                UnityEngine.UI.Slider updated = (UnityEngine.UI.Slider)EditorGUILayout.ObjectField($"Slider {i + 1}", current, typeof(UnityEngine.UI.Slider), true);
                if (updated != current)
                {
                    sliderProp.objectReferenceValue = updated;
                    // Auto-fill min/max/default from slider when assigned
                    if (updated != null)
                    {
                        AutofillStaticFaderFromSlider(faderIndex, updated);
                    }
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveStaticFaderSliderAt(faderIndex, i);
                    countProp.intValue = Mathf.Max(0, sliderCount - 1);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Show reversed direction checkbox
                if (!structuralChange && current != null)
                {
                    SerializedProperty reversedProp = (staticFaderSliderReversed != null && flatIndex < staticFaderSliderReversed.arraySize)
                        ? staticFaderSliderReversed.GetArrayElementAtIndex(flatIndex)
                        : null;
                    
                    if (reversedProp != null)
                    {
                        bool reversed = EditorGUILayout.ToggleLeft("Reversed Direction (Right to Left / Top to Bottom)", reversedProp.boolValue);
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
                    GUILayout.Space(2);
                    break;
                }

                GUILayout.Space(2);
            }

            if (structuralChange)
            {
                return;
            }

            if (GUILayout.Button("Add Slider", GUILayout.Height(22)))
            {
                AddStaticFaderSlider(faderIndex);
                countProp.intValue = sliderCount + 1;
            }
        }

        private void AutofillStaticFaderFromSlider(int faderIndex, UnityEngine.UI.Slider slider)
        {
            if (slider == null) return;
            
            SetStaticFaderMinValue(faderIndex, slider.minValue);
            SetStaticFaderMaxValue(faderIndex, slider.maxValue);
            SetStaticFaderDefaultValue(faderIndex, slider.value);
        }

        private int GetStaticFaderSliderStartIndex(int faderIndex)
        {
            int start = 0;
            if (staticFaderSliderCounts == null || faderIndex <= 0)
            {
                return start;
            }

            for (int i = 0; i < faderIndex && i < staticFaderSliderCounts.arraySize; i++)
            {
                SerializedProperty countProp = staticFaderSliderCounts.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private void EnsureStaticFaderSliderArrayCapacity(int required)
        {
            if (staticFaderSliders == null || required <= 0)
            {
                return;
            }

            while (staticFaderSliders.arraySize < required)
            {
                staticFaderSliders.InsertArrayElementAtIndex(staticFaderSliders.arraySize);
                SerializedProperty element = staticFaderSliders.GetArrayElementAtIndex(staticFaderSliders.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }

            // Also ensure reversed array
            if (staticFaderSliderReversed != null)
            {
                while (staticFaderSliderReversed.arraySize < required)
                {
                    staticFaderSliderReversed.InsertArrayElementAtIndex(staticFaderSliderReversed.arraySize);
                    SerializedProperty element = staticFaderSliderReversed.GetArrayElementAtIndex(staticFaderSliderReversed.arraySize - 1);
                    if (element != null)
                    {
                        element.boolValue = false;
                    }
                }
            }
        }

        private void AddStaticFaderSlider(int faderIndex)
        {
            if (staticFaderSliders == null || staticFaderSliderCounts == null)
            {
                return;
            }

            SerializedProperty countProp = (faderIndex >= 0 && faderIndex < staticFaderSliderCounts.arraySize)
                ? staticFaderSliderCounts.GetArrayElementAtIndex(faderIndex)
                : null;

            if (countProp == null)
            {
                return;
            }

            int sliderStart = GetStaticFaderSliderStartIndex(faderIndex);
            int currentCount = Mathf.Max(0, countProp.intValue);
            int insertIndex = sliderStart + currentCount;

            staticFaderSliders.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = staticFaderSliders.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }

            // Also add to reversed array
            if (staticFaderSliderReversed != null)
            {
                staticFaderSliderReversed.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty reversedElement = staticFaderSliderReversed.GetArrayElementAtIndex(insertIndex);
                if (reversedElement != null)
                {
                    reversedElement.boolValue = false;
                }
            }
        }

        private void RemoveStaticFaderSliderAt(int faderIndex, int localIndex)
        {
            if (staticFaderSliders == null || staticFaderSliderCounts == null)
            {
                return;
            }

            int sliderStart = GetStaticFaderSliderStartIndex(faderIndex);
            int flatIndex = sliderStart + localIndex;

            if (flatIndex >= 0 && flatIndex < staticFaderSliders.arraySize)
            {
                staticFaderSliders.DeleteArrayElementAtIndex(flatIndex);
            }

            // Also remove from reversed array
            if (staticFaderSliderReversed != null && flatIndex >= 0 && flatIndex < staticFaderSliderReversed.arraySize)
            {
                staticFaderSliderReversed.DeleteArrayElementAtIndex(flatIndex);
            }
        }

        private void DrawStaticFaderUdonList(int faderIndex)
        {
            if (staticFaderUdonBehaviours == null || staticFaderUdonCounts == null)
            {
                return;
            }

            SerializedProperty countProp = (faderIndex >= 0 && faderIndex < staticFaderUdonCounts.arraySize)
                ? staticFaderUdonCounts.GetArrayElementAtIndex(faderIndex)
                : null;

            if (countProp == null)
            {
                return;
            }

            int udonCount = Mathf.Max(0, countProp.intValue);
            int udonStart = GetStaticFaderUdonStartIndex(faderIndex);
            EnsureStaticFaderUdonArrayCapacity(udonStart + udonCount);

            bool structuralChange = false;

            for (int i = 0; i < udonCount; i++)
            {
                int flatIndex = udonStart + i;
                if (flatIndex < 0 || flatIndex >= staticFaderUdonBehaviours.arraySize)
                {
                    break;
                }

                SerializedProperty udonProp = staticFaderUdonBehaviours.GetArrayElementAtIndex(flatIndex);

                EditorGUILayout.BeginHorizontal();
                UdonBehaviour current = udonProp.objectReferenceValue as UdonBehaviour;
                UdonBehaviour updated = (UdonBehaviour)EditorGUILayout.ObjectField($"Udon Behavior {i + 1}", current, typeof(UdonBehaviour), true);
                if (updated != current)
                {
                    udonProp.objectReferenceValue = updated;
                }

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MoveStaticFaderUdon(faderIndex, i, i - 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange && i < udonCount - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MoveStaticFaderUdon(faderIndex, i, i + 1);
                    structuralChange = true;
                }

                GUI.enabled = !structuralChange;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveStaticFaderUdonAt(faderIndex, i);
                    countProp.intValue = Mathf.Max(0, udonCount - 1);
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

            Rect addButtonRect = GUILayoutUtility.GetRect(new GUIContent("Add Udon Behavior"), GUI.skin.button, GUILayout.Height(22));
            bool addClicked = GUI.Button(addButtonRect, "Add Udon Behavior");
            if (HandleUdonDrop(addButtonRect, faderIndex, countProp, udonCount))
            {
                return;
            }

            if (addClicked)
            {
                AddStaticFaderUdon(faderIndex);
                countProp.intValue = udonCount + 1;
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
                .Where(option => option.Type != ToggleFolderType.Stats && option.Type != ToggleFolderType.Presets)
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

            // Check if this is a color property (propertyType == 2)
            int propertyType = GetDynamicFaderPropertyType(index);
            bool isColorProperty = propertyType == 2;

            if (isColorProperty)
            {
                // For color properties, show informational note instead of color picker
                EditorGUILayout.HelpBox(
                    "The indicator will display the color currently applied by this fader.",
                    MessageType.Info
                );
            }
            else
            {
                // For non-color properties, show the color picker
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
            InsertDynamicFaderElement(dynamicFaderTargetsUdon, index, prop => prop.boolValue = false);
            InsertDynamicFaderElement(dynamicFaderUdonCounts, index, prop => prop.intValue = 0);
            InsertDynamicFaderElement(dynamicFaderUdonVariableNames, index, prop => prop.stringValue = string.Empty);
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
            // First remove Udon behaviours from flat array before modifying counts
            RemoveDynamicFaderUdonBehavioursAt(index);
            
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
            DeleteDynamicFaderElement(dynamicFaderTargetsUdon, index);
            DeleteDynamicFaderElement(dynamicFaderUdonCounts, index);
            DeleteDynamicFaderElement(dynamicFaderUdonVariableNames, index);
        }

        private void RemoveDynamicFaderUdonBehavioursAt(int index)
        {
            if (dynamicFaderUdonBehaviours == null || dynamicFaderUdonCounts == null)
            {
                return;
            }
            
            if (index < 0 || index >= dynamicFaderUdonCounts.arraySize)
            {
                return;
            }
            
            int startIndex = GetDynamicFaderUdonStartIndex(index);
            int count = GetDynamicFaderUdonCountValue(index);
            
            // Remove entries from flat array (in reverse order to maintain indices)
            for (int i = count - 1; i >= 0; i--)
            {
                int flatIndex = startIndex + i;
                if (flatIndex >= 0 && flatIndex < dynamicFaderUdonBehaviours.arraySize)
                {
                    dynamicFaderUdonBehaviours.DeleteArrayElementAtIndex(flatIndex);
                }
            }
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

        // ==================== UdonBehaviour Helper Methods ====================

        private int GetStaticFaderUdonStartIndex(int faderIndex)
        {
            int start = 0;
            if (staticFaderUdonCounts == null)
            {
                return start;
            }

            int count = Mathf.Min(faderIndex, staticFaderUdonCounts.arraySize);
            for (int i = 0; i < count; i++)
            {
                SerializedProperty countProp = staticFaderUdonCounts.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private int GetStaticFaderUdonCountValue(int faderIndex)
        {
            if (staticFaderUdonCounts == null || faderIndex < 0 || faderIndex >= staticFaderUdonCounts.arraySize)
            {
                return 0;
            }

            SerializedProperty countProp = staticFaderUdonCounts.GetArrayElementAtIndex(faderIndex);
            return Mathf.Max(0, countProp?.intValue ?? 0);
        }

        private void AddStaticFaderUdon(int faderIndex)
        {
            int insertIndex = GetStaticFaderUdonStartIndex(faderIndex) + GetStaticFaderUdonCountValue(faderIndex);
            InsertStaticFaderUdonAt(insertIndex);
        }

        private void InsertStaticFaderUdonAt(int flatIndex)
        {
            if (staticFaderUdonBehaviours == null)
            {
                return;
            }

            int insertIndex = Mathf.Clamp(flatIndex, 0, staticFaderUdonBehaviours.arraySize);
            staticFaderUdonBehaviours.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = staticFaderUdonBehaviours.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.objectReferenceValue = null;
            }
        }

        private void RemoveStaticFaderUdonAt(int faderIndex, int localIndex)
        {
            int start = GetStaticFaderUdonStartIndex(faderIndex);
            int flatIndex = start + localIndex;
            if (staticFaderUdonBehaviours == null || flatIndex < 0 || flatIndex >= staticFaderUdonBehaviours.arraySize)
            {
                return;
            }

            staticFaderUdonBehaviours.DeleteArrayElementAtIndex(flatIndex);
        }

        private void MoveStaticFaderUdon(int faderIndex, int from, int to)
        {
            if (staticFaderUdonBehaviours == null)
            {
                return;
            }

            int start = GetStaticFaderUdonStartIndex(faderIndex);
            int count = GetStaticFaderUdonCountValue(faderIndex);
            if (from < 0 || to < 0 || from >= count || to >= count)
            {
                return;
            }

            int fromFlat = start + from;
            int toFlat = start + to;
            if (fromFlat < 0 || toFlat < 0 || fromFlat >= staticFaderUdonBehaviours.arraySize || toFlat >= staticFaderUdonBehaviours.arraySize)
            {
                return;
            }

            staticFaderUdonBehaviours.MoveArrayElement(fromFlat, toFlat);
        }

        private void EnsureStaticFaderUdonArrayCapacity(int required)
        {
            if (staticFaderUdonBehaviours == null || required <= 0)
            {
                return;
            }

            while (staticFaderUdonBehaviours.arraySize < required)
            {
                staticFaderUdonBehaviours.InsertArrayElementAtIndex(staticFaderUdonBehaviours.arraySize);
                SerializedProperty element = staticFaderUdonBehaviours.GetArrayElementAtIndex(staticFaderUdonBehaviours.arraySize - 1);
                if (element != null)
                {
                    element.objectReferenceValue = null;
                }
            }
        }

        private bool HandleUdonDrop(Rect dropRect, int faderIndex, SerializedProperty countProp, int udonCount)
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

            List<UdonBehaviour> droppedUdon = new List<UdonBehaviour>();
            HashSet<UdonBehaviour> seenUdon = new HashSet<UdonBehaviour>();
            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                UdonBehaviour udon = reference as UdonBehaviour;
                if (udon != null)
                {
                    if (seenUdon.Add(udon))
                    {
                        droppedUdon.Add(udon);
                    }

                    continue;
                }

                GameObject go = reference as GameObject;
                if (go == null)
                {
                    continue;
                }

                UdonBehaviour[] udons = go.GetComponentsInChildren<UdonBehaviour>(true);
                foreach (UdonBehaviour childUdon in udons)
                {
                    if (childUdon != null && seenUdon.Add(childUdon))
                    {
                        droppedUdon.Add(childUdon);
                    }
                }
            }

            if (droppedUdon.Count == 0)
            {
                return false;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddDroppedUdons(faderIndex, countProp, udonCount, droppedUdon);
            }

            current.Use();
            return type == EventType.DragPerform;
        }

        private void AddDroppedUdons(int faderIndex, SerializedProperty countProp, int udonCount, List<UdonBehaviour> droppedUdon)
        {
            int insertIndex = GetStaticFaderUdonStartIndex(faderIndex) + udonCount;

            foreach (UdonBehaviour udon in droppedUdon)
            {
                InsertStaticFaderUdonAt(insertIndex);
                SerializedProperty udonProp = staticFaderUdonBehaviours.GetArrayElementAtIndex(insertIndex);
                if (udonProp != null)
                {
                    udonProp.objectReferenceValue = udon;
                }

                insertIndex++;
            }

            countProp.intValue = udonCount + droppedUdon.Count;
        }

        private bool IsStaticFaderTargetingUdon(int faderIndex)
        {
            if (staticFaderTargetsUdon == null || faderIndex < 0 || faderIndex >= staticFaderTargetsUdon.arraySize)
            {
                return false;
            }

            SerializedProperty prop = staticFaderTargetsUdon.GetArrayElementAtIndex(faderIndex);
            return prop != null && prop.boolValue;
        }

        private bool IsStaticFaderTargetingSlider(int faderIndex)
        {
            if (staticFaderTargetsSlider == null || faderIndex < 0 || faderIndex >= staticFaderTargetsSlider.arraySize)
            {
                return false;
            }

            SerializedProperty prop = staticFaderTargetsSlider.GetArrayElementAtIndex(faderIndex);
            return prop != null && prop.boolValue;
        }

        private string GetStaticFaderUdonVariableName(int faderIndex)
        {
            if (staticFaderUdonVariableNames == null || faderIndex < 0 || faderIndex >= staticFaderUdonVariableNames.arraySize)
            {
                return string.Empty;
            }

            SerializedProperty prop = staticFaderUdonVariableNames.GetArrayElementAtIndex(faderIndex);
            return prop != null ? prop.stringValue : string.Empty;
        }

        private void SetStaticFaderUdonVariableName(int faderIndex, string value)
        {
            if (staticFaderUdonVariableNames == null || faderIndex < 0 || faderIndex >= staticFaderUdonVariableNames.arraySize)
            {
                return;
            }

            SerializedProperty prop = staticFaderUdonVariableNames.GetArrayElementAtIndex(faderIndex);
            if (prop != null)
            {
                prop.stringValue = value ?? string.Empty;
            }
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
            
            // Check if this is a Properties folder and the entry targets UdonBehaviour
            if (folderType == ToggleFolderType.Properties && IsPropertyEntryTargetingUdon(folderIndex, toggleIndex))
            {
                DrawDynamicFaderUdonFromPropertyEntry(index, folderIndex, toggleIndex);
                return;
            }

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
            
            // Show note when fader property conflicts with Mochie controls
            if (folderType == ToggleFolderType.Mochie && !string.IsNullOrEmpty(currentName))
            {
                string conflictNote = GetMochieFaderConflictNote(currentName);
                if (!string.IsNullOrEmpty(conflictNote))
                {
                    EditorGUILayout.HelpBox(conflictNote, MessageType.Info);
                }
            }
        }

        private bool IsPropertyEntryTargetingUdon(int folderIndex, int entryIndex)
        {
            SerializedObject propHandlerObj = GetPropertyHandlerObjectForFolder(folderIndex);
            if (propHandlerObj == null)
            {
                return false;
            }

            SerializedProperty targetsUdonProp = propHandlerObj.FindProperty("propertyTargetsUdon");
            if (targetsUdonProp == null || !targetsUdonProp.isArray || entryIndex < 0 || entryIndex >= targetsUdonProp.arraySize)
            {
                return false;
            }

            SerializedProperty entryTargetsUdon = targetsUdonProp.GetArrayElementAtIndex(entryIndex);
            return entryTargetsUdon != null && entryTargetsUdon.boolValue;
        }

        private void DrawDynamicFaderUdonFromPropertyEntry(int dynamicIndex, int folderIndex, int entryIndex)
        {
            SerializedObject propHandlerObj = GetPropertyHandlerObjectForFolder(folderIndex);
            if (propHandlerObj == null)
            {
                EditorGUILayout.HelpBox("Properties handler not found.", MessageType.Warning);
                return;
            }

            SerializedProperty udonCountsProp = propHandlerObj.FindProperty("propertyUdonCounts");
            SerializedProperty udonBehavioursProp = propHandlerObj.FindProperty("propertyUdonBehaviours");
            SerializedProperty udonVariableNamesProp = propHandlerObj.FindProperty("propertyUdonVariableNames");

            if (udonCountsProp == null || udonBehavioursProp == null || udonVariableNamesProp == null)
            {
                EditorGUILayout.HelpBox("UdonBehaviour configuration missing from PropertyHandler.", MessageType.Warning);
                return;
            }

            // Get the variable name from the Properties entry
            string entryVariableName = string.Empty;
            if (entryIndex >= 0 && entryIndex < udonVariableNamesProp.arraySize)
            {
                SerializedProperty varNameProp = udonVariableNamesProp.GetArrayElementAtIndex(entryIndex);
                entryVariableName = varNameProp != null ? varNameProp.stringValue : string.Empty;
            }

            // Get UdonBehaviour count and start index
            int udonCount = 0;
            int udonStart = 0;
            if (entryIndex >= 0 && entryIndex < udonCountsProp.arraySize)
            {
                SerializedProperty countProp = udonCountsProp.GetArrayElementAtIndex(entryIndex);
                udonCount = countProp != null ? Mathf.Max(0, countProp.intValue) : 0;
                
                // Calculate start index
                for (int i = 0; i < entryIndex && i < udonCountsProp.arraySize; i++)
                {
                    SerializedProperty prevCount = udonCountsProp.GetArrayElementAtIndex(i);
                    udonStart += prevCount != null ? Mathf.Max(0, prevCount.intValue) : 0;
                }
            }

            // Display info about the linked UdonBehaviour entry
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Udon Behavior Target", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Variable: {(string.IsNullOrEmpty(entryVariableName) ? "(None)" : entryVariableName)}");
            EditorGUILayout.LabelField($"Target Count: {udonCount}");
            EditorGUILayout.EndVertical();

            // Sync the dynamic fader's Udon configuration with the Properties entry
            if (!string.IsNullOrEmpty(entryVariableName) && udonCount > 0)
            {
                // Set the dynamic fader to target Udon
                SetDynamicFaderTargetsUdon(dynamicIndex, true);
                SetDynamicFaderUdonVariableName(dynamicIndex, entryVariableName);
                
                // Copy UdonBehaviour references from Properties entry to dynamic fader
                SyncDynamicFaderUdonBehaviours(dynamicIndex, udonBehavioursProp, udonStart, udonCount);
            }
            else
            {
                EditorGUILayout.HelpBox("Configure the UdonBehaviour target in the Properties folder entry.", MessageType.Info);
            }
        }

        private void SetDynamicFaderTargetsUdon(int index, bool value)
        {
            if (dynamicFaderTargetsUdon == null || index < 0 || index >= dynamicFaderTargetsUdon.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderTargetsUdon.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.boolValue = value;
            }
        }

        private void SetDynamicFaderUdonVariableName(int index, string value)
        {
            if (dynamicFaderUdonVariableNames == null || index < 0 || index >= dynamicFaderUdonVariableNames.arraySize)
            {
                return;
            }

            SerializedProperty prop = dynamicFaderUdonVariableNames.GetArrayElementAtIndex(index);
            if (prop != null)
            {
                prop.stringValue = value ?? string.Empty;
            }
        }

        private void SyncDynamicFaderUdonBehaviours(int dynamicIndex, SerializedProperty sourceUdonBehaviours, int sourceStart, int sourceCount)
        {
            if (dynamicFaderUdonBehaviours == null || dynamicFaderUdonCounts == null || sourceUdonBehaviours == null)
            {
                return;
            }

            // Get current count for this dynamic fader
            int currentStart = GetDynamicFaderUdonStartIndex(dynamicIndex);
            int currentCount = GetDynamicFaderUdonCountValue(dynamicIndex);

            // Remove existing entries for this dynamic fader
            for (int i = currentCount - 1; i >= 0; i--)
            {
                int flatIndex = currentStart + i;
                if (flatIndex >= 0 && flatIndex < dynamicFaderUdonBehaviours.arraySize)
                {
                    dynamicFaderUdonBehaviours.DeleteArrayElementAtIndex(flatIndex);
                }
            }

            // Add entries from source
            int insertIndex = currentStart;
            for (int i = 0; i < sourceCount; i++)
            {
                int sourceIndex = sourceStart + i;
                if (sourceIndex < 0 || sourceIndex >= sourceUdonBehaviours.arraySize)
                {
                    continue;
                }

                SerializedProperty sourceEntry = sourceUdonBehaviours.GetArrayElementAtIndex(sourceIndex);
                UdonBehaviour udon = sourceEntry?.objectReferenceValue as UdonBehaviour;

                dynamicFaderUdonBehaviours.InsertArrayElementAtIndex(insertIndex);
                SerializedProperty destEntry = dynamicFaderUdonBehaviours.GetArrayElementAtIndex(insertIndex);
                if (destEntry != null)
                {
                    destEntry.objectReferenceValue = udon;
                }
                insertIndex++;
            }

            // Update count
            if (dynamicIndex >= 0 && dynamicIndex < dynamicFaderUdonCounts.arraySize)
            {
                SerializedProperty countProp = dynamicFaderUdonCounts.GetArrayElementAtIndex(dynamicIndex);
                if (countProp != null)
                {
                    countProp.intValue = sourceCount;
                }
            }
        }

        private int GetDynamicFaderUdonStartIndex(int index)
        {
            int start = 0;
            if (dynamicFaderUdonCounts == null || index <= 0)
            {
                return start;
            }

            int limit = Mathf.Min(index, dynamicFaderUdonCounts.arraySize);
            for (int i = 0; i < limit; i++)
            {
                SerializedProperty countProp = dynamicFaderUdonCounts.GetArrayElementAtIndex(i);
                start += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            return start;
        }

        private int GetDynamicFaderUdonCountValue(int index)
        {
            if (dynamicFaderUdonCounts == null || index < 0 || index >= dynamicFaderUdonCounts.arraySize)
            {
                return 0;
            }

            SerializedProperty countProp = dynamicFaderUdonCounts.GetArrayElementAtIndex(index);
            return Mathf.Max(0, countProp?.intValue ?? 0);
        }

        private void DrawStaticFaderPropertyField(int faderIndex)
        {
            // Check if targeting UdonBehaviour
            if (IsStaticFaderTargetingUdon(faderIndex))
            {
                DrawStaticFaderUdonVariableField(faderIndex);
                return;
            }

            // Check if targeting Unity Slider (no shader property needed)
            if (IsStaticFaderTargetingSlider(faderIndex))
            {
                return;
            }

            if (staticFaderPropertyNames == null || staticFaderPropertyTypes == null)
            {
                return;
            }

            FaderShaderTarget target = BuildStaticFaderShaderTarget(faderIndex);
            if ((target.renderers == null || target.renderers.Length == 0) &&
                (target.directMaterials == null || target.directMaterials.Length == 0))
            {
                EditorGUILayout.HelpBox("Select a target (folder, renderer, Udon Behavior, or Unity Slider) to configure the fader.", MessageType.Info);
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
            
            // Show note when fader property conflicts with Mochie controls
            int staticFolderIndex = GetStaticFaderFolderIndex(faderIndex);
            if (staticFolderIndex >= 0 && GetFolderType(staticFolderIndex) == ToggleFolderType.Mochie && !string.IsNullOrEmpty(currentName))
            {
                string conflictNote = GetMochieFaderConflictNote(currentName);
                if (!string.IsNullOrEmpty(conflictNote))
                {
                    EditorGUILayout.HelpBox(conflictNote, MessageType.Info);
                }
            }
        }

        private void DrawStaticFaderUdonVariableField(int faderIndex)
        {
            // Get the UdonBehaviour targets for this fader
            int udonCount = GetStaticFaderUdonCountValue(faderIndex);
            int udonStart = GetStaticFaderUdonStartIndex(faderIndex);
            
            if (udonCount == 0 || staticFaderUdonBehaviours == null)
            {
                EditorGUILayout.HelpBox("Add at least one Udon Behavior to select a variable.", MessageType.Info);
                return;
            }

            // Build list of UdonBehaviour targets
            List<UdonBehaviour> udonTargets = new List<UdonBehaviour>();
            for (int i = 0; i < udonCount; i++)
            {
                int flatIndex = udonStart + i;
                if (flatIndex >= 0 && flatIndex < staticFaderUdonBehaviours.arraySize)
                {
                    SerializedProperty udonProp = staticFaderUdonBehaviours.GetArrayElementAtIndex(flatIndex);
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
            if (!TryBuildUdonVariableOptions(udonTargets, out variableNames, out string warning))
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

            string currentName = GetStaticFaderUdonVariableName(faderIndex);
            
            // Draw variable selection with search button on single line
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Variable"));
            string displayName = string.IsNullOrEmpty(currentName) ? "(None)" : currentName;
            GUILayout.Label(displayName, EditorStyles.textField);
            if (GUILayout.Button("Search", GUILayout.Width(60)))
            {
                // Capture udonTargets for the callback closure
                List<UdonBehaviour> capturedTargets = new List<UdonBehaviour>(udonTargets);
                OpenUdonVariableSearchWindow(variableNames, (selectedName) =>
                {
                    SetStaticFaderUdonVariableName(faderIndex, selectedName);
                    // Set property type to Float (0) for Udon variables
                    SetStaticFaderPropertyType(faderIndex, 0);
                    // Autofill min/max/default values from the Udon variable
                    AutofillStaticFaderUdonValues(faderIndex, selectedName, capturedTargets);
                    // Apply changes immediately and force repaint
                    if (faderHandlerObject != null)
                    {
                        faderHandlerObject.ApplyModifiedProperties();
                    }
                    Repaint();
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool TryBuildUdonVariableOptions(List<UdonBehaviour> udonTargets, out List<string> variableNames, out string warning)
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

                HashSet<string> udonVariables = GetUdonPublicFloatVariables(udon);
                
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

        private HashSet<string> GetUdonPublicFloatVariables(UdonBehaviour udon)
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
                            // Only include numeric types that can be used as fader values
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
                            // Only include float variables (faders work with floats)
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

        /// <summary>
        /// Attempts to get the min, max, and default values for an Udon variable.
        /// For UdonSharp behaviors, this uses the RangeAttribute if present and gets the current value from publicVariables.
        /// </summary>
        private bool TryGetUdonVariableValues(UdonBehaviour udon, string variableName, out float defaultValue, out float minValue, out float maxValue)
        {
            defaultValue = 0f;
            minValue = 0f;
            maxValue = 1f;

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
                        defaultValue = floatVal;
                    else if (currentValue is int intVal)
                        defaultValue = intVal;
                    else if (currentValue is double doubleVal)
                        defaultValue = (float)doubleVal;
                }

                // For UdonSharp behaviors, check for RangeAttribute
                if (udon.programSource is UdonSharpProgramAsset udonSharpProgramAsset)
                {
                    if (udonSharpProgramAsset.fieldDefinitions != null &&
                        udonSharpProgramAsset.fieldDefinitions.TryGetValue(variableName, out var fieldDef))
                    {
                        // Try to get RangeAttribute for min/max values
                        var rangeAttr = fieldDef.GetAttribute<RangeAttribute>();
                        if (rangeAttr != null)
                        {
                            minValue = rangeAttr.min;
                            maxValue = rangeAttr.max;
                            return true;
                        }
                    }
                }

                // If no RangeAttribute, use sensible defaults based on the current value
                if (defaultValue < minValue)
                    minValue = defaultValue;
                if (defaultValue > maxValue)
                {
                    // Handle both positive and negative values appropriately
                    if (defaultValue >= 0)
                        maxValue = defaultValue * FloatRangeExpansionFactor;
                    else
                        maxValue = defaultValue; // For negative values, just use the value as max
                }
                if (maxValue <= minValue)
                    maxValue = minValue + 1f;

                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Autofills the static fader min/max/default values based on the selected Udon variable.
        /// Uses the first UdonBehaviour target to extract values.
        /// </summary>
        private void AutofillStaticFaderUdonValues(int faderIndex, string variableName, List<UdonBehaviour> udonTargets)
        {
            if (udonTargets == null || udonTargets.Count == 0 || string.IsNullOrEmpty(variableName))
            {
                return;
            }

            // Use the first valid UdonBehaviour to get values
            UdonBehaviour firstUdon = udonTargets.FirstOrDefault(u => u != null);
            if (firstUdon == null)
            {
                return;
            }

            if (TryGetUdonVariableValues(firstUdon, variableName, out float defaultValue, out float minValue, out float maxValue))
            {
                SetStaticFaderMinValue(faderIndex, minValue);
                SetStaticFaderMaxValue(faderIndex, maxValue);
                SetStaticFaderDefaultValue(faderIndex, defaultValue);
            }
        }

        private void OpenUdonVariableSearchWindow(List<string> variableNames, Action<string> onSelect)
        {
            var searchWindow = new PropertySearchWindow("Udon Variables");
            var mainGroup = searchWindow.GetMainGroup();
            
            foreach (string varName in variableNames)
            {
                mainGroup.Add(varName, varName);
            }

            searchWindow.Open(onSelect);
        }

        // ==================== Shader Target Building ====================

        private FaderShaderTarget BuildDynamicFaderShaderTarget(ToggleFolderType folderType, int folderIndex, int toggleIndex, int dynamicIndex = -1)
        {
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                    return BuildObjectFolderShaderTarget(folderIndex, toggleIndex, dynamicIndex);

                case ToggleFolderType.Properties:
                    return BuildPropertiesFolderShaderTarget(folderIndex, toggleIndex);

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
                    // Properties folder entries have per-entry renderers, so folder-level targeting
                    // doesn't apply. Static faders should use custom targeting for Properties folders.
                    return PrepareFaderShaderTarget(Array.Empty<Renderer>(), materialIndex);

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

        private FaderShaderTarget BuildPropertiesFolderShaderTarget(int folderIndex, int toggleIndex)
        {
            SerializedObject propHandlerObj = GetPropertyHandlerObjectForFolder(folderIndex);
            if (propHandlerObj == null)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            SerializedProperty renderersProperty = propHandlerObj.FindProperty("propertyShaderRenderers");
            SerializedProperty rendererCountsProperty = propHandlerObj.FindProperty("propertyShaderRendererCounts");
            
            if (renderersProperty == null || rendererCountsProperty == null || 
                !renderersProperty.isArray || !rendererCountsProperty.isArray ||
                toggleIndex < 0 || toggleIndex >= rendererCountsProperty.arraySize)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            // Calculate start index for this entry
            int startIndex = 0;
            for (int i = 0; i < toggleIndex; i++)
            {
                SerializedProperty countProp = rendererCountsProperty.GetArrayElementAtIndex(i);
                startIndex += Mathf.Max(0, countProp?.intValue ?? 0);
            }

            SerializedProperty entryCountProp = rendererCountsProperty.GetArrayElementAtIndex(toggleIndex);
            int entryCount = Mathf.Max(0, entryCountProp?.intValue ?? 0);

            if (entryCount == 0 || startIndex >= renderersProperty.arraySize)
            {
                return PrepareFaderShaderTarget(Array.Empty<Renderer>(), 0);
            }

            List<Renderer> rendererList = new List<Renderer>();
            for (int i = 0; i < entryCount; i++)
            {
                int flatIndex = startIndex + i;
                if (flatIndex < renderersProperty.arraySize)
                {
                    SerializedProperty rendererProp = renderersProperty.GetArrayElementAtIndex(flatIndex);
                    Renderer renderer = rendererProp?.objectReferenceValue as Renderer;
                    if (renderer != null)
                    {
                        rendererList.Add(renderer);
                    }
                }
            }

            return PrepareFaderShaderTarget(rendererList.ToArray(), 0);
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
        
        /// <summary>
        /// Returns a user-facing note describing which Mochie layout controls will be disabled
        /// when a fader drives the given shader property. Returns null if no conflict exists.
        /// </summary>
        private string GetMochieFaderConflictNote(string propertyName)
        {
            switch (propertyName)
            {
                case "_Saturation":
                    return "The Saturation +/\u2212 buttons in the Mochie layout will be disabled. The middle button will toggle the effect on/off.";
                case "_RoundingOpacity":
                    return "The Rounding +/\u2212 buttons in the Mochie layout (SFX X) will be disabled. The middle button will toggle the effect on/off.";
                case "_FogSafeOpacity":
                    return "The Fog Safe +/\u2212 buttons in the Mochie layout will be disabled. The middle button will toggle the effect on/off.";
                case "_Brightness":
                    return "The Brightness +/\u2212 buttons in the Mochie layout will be disabled. The middle button will toggle the effect on/off.";
                case "_Contrast":
                    return "The Contrast +/\u2212 buttons in the Mochie layout will be disabled. The middle button will toggle the effect on/off.";
                case "_HDR":
                    return "The HDR +/\u2212 buttons in the Mochie layout will be disabled. The middle button will toggle the effect on/off.";
                case "_Invert":
                    return "The Invert and Invert+ buttons will act as on/off toggles. The fader controls the strength.";
                case "_Amplitude":
                    return "The Shake button will act as an on/off toggle. The fader controls the strength.";
                case "_BlurStr":
                    return "The Blur button will act as an on/off toggle. The fader controls the strength.";
                case "_DistortionStr":
                    return "The Distortion button will act as an on/off toggle. The fader controls the strength.";
                case "_Noise":
                    return "The Noise button will act as an on/off toggle. The fader controls the strength.";
                case "_ScanLine":
                    return "The Scan Line button will act as an on/off toggle. The fader controls the strength.";
                case "_DBOpacity":
                    return "The Depth Buffer button will act as an on/off toggle. The fader controls the strength.";
                case "_NMFOpacity":
                    return "The Normal Map button will act as an on/off toggle. The fader controls the strength.";
                case "_SobelFilterOpacity":
                    return "The Sobel Filter toggle button in the Mochie layout will be disabled.";
                case "_OutlineCol":
                    return "The outline color selector (Apply and Cycle buttons) in the Mochie layout will be disabled.";
                case "_OutlineType":
                    return "The Aura and Sobel outline type buttons in the Mochie layout will be disabled.";
                case "_AuraStr":
                    return "The outline strength buttons (Low/Normal/High) in the Mochie layout will be disabled.";
                case "_OutlineThresh":
                    return "The outline strength buttons (Low/Normal/High) in the Mochie layout will be disabled.";
                default:
                    return null;
            }
        }
    }
}
#endif

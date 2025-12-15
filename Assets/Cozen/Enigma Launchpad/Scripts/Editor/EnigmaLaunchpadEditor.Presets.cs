#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty presetHandlerProperty;
        private SerializedObject presetHandlerObject;

        private SerializedProperty presetSlotCount;
        private SerializedProperty presetIncludedFolderIndices;
        private SerializedProperty presetFolderSelectionInitialized;
        private SerializedProperty presetIncludeFaders;

        private void BindPresetHandlerSerializedObject()
        {
            presetHandlerObject = null;
            presetSlotCount = null;
            presetIncludedFolderIndices = null;
            presetFolderSelectionInitialized = null;
            presetIncludeFaders = null;

            if (presetHandlerProperty == null || presetHandlerProperty.objectReferenceValue == null)
            {
                return;
            }

            presetHandlerObject = new SerializedObject(presetHandlerProperty.objectReferenceValue);
            presetSlotCount = presetHandlerObject.FindProperty("presetPages");
            presetIncludedFolderIndices = presetHandlerObject.FindProperty("includedFolderIndices");
            presetFolderSelectionInitialized = presetHandlerObject.FindProperty("folderSelectionInitialized");
            presetIncludeFaders = presetHandlerObject.FindProperty("includeFaders");
        }

        private void EnsurePresetHandlerParity()
        {
            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || presetHandlerProperty == null)
            {
                presetHandlerObject = null;
                return;
            }

            int presetFolderIndex = GetFolderIndexForType(ToggleFolderType.Presets);
            PresetHandler existing = presetHandlerProperty.objectReferenceValue as PresetHandler;

            if (presetFolderIndex < 0)
            {
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }

                presetHandlerProperty.objectReferenceValue = null;
                presetHandlerObject = null;
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            PresetHandler handler = existing;
            if (handler == null)
            {
                string handlerName = GetExpectedPresetHandlerName(presetFolderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create PresetHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                handler = handlerObject.AddComponent<PresetHandler>();
            }

            Undo.RecordObject(handler, "Configure PresetHandler");
            handler.launchpad = launchpad;
            handler.transform.SetParent(foldersTransform);
            if (handler.gameObject.hideFlags != HandlerHideFlags)
            {
                handler.gameObject.hideFlags = HandlerHideFlags;
            }

            // Update GameObject name to match current folder name
            string expectedName = GetExpectedPresetHandlerName(presetFolderIndex);
            if (handler.gameObject.name != expectedName)
            {
                Undo.RecordObject(handler.gameObject, "Rename PresetHandler");
                handler.gameObject.name = expectedName;
            }

            presetHandlerProperty.objectReferenceValue = handler;
        }

        private string GetExpectedPresetHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"PresetHandler_{folderName}";
        }

        private bool DrawPresetFolderSection(ToggleFolderType folderType, SerializedProperty exclProp, SerializedProperty countProp, ref bool structural)
        {
            if (folderType != ToggleFolderType.Presets)
            {
                return false;
            }

            // Presets folder doesn't use exclusive mode
            if (exclProp != null && exclProp.boolValue)
            {
                exclProp.boolValue = false;
            }

            GUILayout.Space(6);

            if (presetHandlerObject == null)
            {
                EditorGUILayout.HelpBox("PresetHandler missing required serialized properties.", MessageType.Error);
                return false;
            }

            presetHandlerObject.Update();

            // Draw preset pages
            if (presetSlotCount != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(presetSlotCount, new GUIContent("Preset Pages"));
                if (EditorGUI.EndChangeCheck())
                {
                    presetSlotCount.intValue = Mathf.Clamp(presetSlotCount.intValue, 1, 9);
                    structural = true;
                }
                
                // Calculate and display actual slot count
                // Page 1 = 6 slots (9 - 3 header), Page 2+ = 9 slots each
                int pages = presetSlotCount.intValue;
                int firstPageSlots = 6; // 9 items - 3 header buttons
                int additionalSlots = (pages > 1) ? (pages - 1) * 9 : 0;
                int totalSlots = firstPageSlots + additionalSlots;
                EditorGUILayout.LabelField($"Preset Slots: {totalSlots}", EditorStyles.miniLabel);
            }

            GUILayout.Space(6);
            DrawPresetFolderSelector();

            presetHandlerObject.ApplyModifiedProperties();

            EditorGUILayout.HelpBox(
                "Presets allow saving and loading toggle states for selected folders.\n\n" +
                "Clicking an Empty slot saves the current state to that slot.\n" +
                "Clicking a Preset slot applies that preset.\n" +
                "Delete: Toggle delete mode, then click a preset to remove it.\n" +
                "Save: Saves all presets to your persistent PlayerData.\n" +
                "Load: Loads your saved presets from PlayerData.\n" +
                "Reset: Clears all preset slots.",
                MessageType.Info);

            return false;
        }

        private void DrawPresetFolderSelector()
        {
            if (presetIncludedFolderIndices == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Folders captured when saving presets:", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            List<FolderOption> eligibleOptions = BuildEligiblePresetFolderOptions();
            if (eligibleOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("Add Objects, Materials, Properties, Skybox, Mochie, or June folders to capture in presets.", MessageType.Info);
            }
            else
            {
                foreach (FolderOption option in eligibleOptions)
                {
                    bool selected = IsPresetFolderIndexSelected(option.Index);
                    EditorGUI.BeginChangeCheck();
                    bool newValue = EditorGUILayout.ToggleLeft(option.Label, selected);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newValue)
                        {
                            AddPresetFolderSelection(option.Index);
                        }
                        else
                        {
                            RemovePresetFolderSelection(option.Index);
                        }
                    }
                }
            }

            // Fader checkbox (separate from folder types)
            if (presetIncludeFaders != null)
            {
                GUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                bool includeFaders = EditorGUILayout.ToggleLeft("Faders", presetIncludeFaders.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    presetIncludeFaders.boolValue = includeFaders;
                }
            }

            int selectionCount = presetIncludedFolderIndices.arraySize;
            bool hasFaders = presetIncludeFaders != null && presetIncludeFaders.boolValue;
            if (selectionCount == 0 && !hasFaders)
            {
                bool initialized = presetFolderSelectionInitialized != null && presetFolderSelectionInitialized.boolValue;
                string message = initialized
                    ? "No folders selected. Saving a preset will capture all compatible folders by default."
                    : "No folders selected. Default folders will be saved until configured.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        private List<FolderOption> BuildEligiblePresetFolderOptions()
        {
            List<FolderOption> result = new List<FolderOption>();
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;

            for (int i = 0; i < folderCount; i++)
            {
                ToggleFolderType folderType = GetFolderType(i);
                if (IsPresetEligibleFolder(folderType))
                {
                    string folderName = GetFolderDisplayName(i);
                    result.Add(new FolderOption { Index = i, Label = folderName, Type = folderType });
                }
            }

            return result;
        }

        private bool IsPresetEligibleFolder(ToggleFolderType folderType)
        {
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                case ToggleFolderType.Materials:
                case ToggleFolderType.Properties:
                case ToggleFolderType.Skybox:
                case ToggleFolderType.Shaders:
                case ToggleFolderType.Mochie:
                case ToggleFolderType.June:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsPresetFolderIndexSelected(int folderIndex)
        {
            return FindPresetFolderSelectionIndex(folderIndex) >= 0;
        }

        private int FindPresetFolderSelectionIndex(int folderIndex)
        {
            if (presetIncludedFolderIndices == null || !presetIncludedFolderIndices.isArray)
            {
                return -1;
            }

            for (int i = 0; i < presetIncludedFolderIndices.arraySize; i++)
            {
                SerializedProperty element = presetIncludedFolderIndices.GetArrayElementAtIndex(i);
                if (element != null && element.intValue == folderIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AddPresetFolderSelection(int folderIndex)
        {
            if (presetIncludedFolderIndices == null || !presetIncludedFolderIndices.isArray)
            {
                return;
            }

            if (FindPresetFolderSelectionIndex(folderIndex) >= 0)
            {
                return;
            }

            int insertIndex = presetIncludedFolderIndices.arraySize;
            presetIncludedFolderIndices.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = presetIncludedFolderIndices.GetArrayElementAtIndex(insertIndex);
            if (element != null)
            {
                element.intValue = folderIndex;
            }

            SetPresetFolderSelectionInitialized(true);
        }

        private void RemovePresetFolderSelection(int folderIndex)
        {
            if (presetIncludedFolderIndices == null || !presetIncludedFolderIndices.isArray)
            {
                return;
            }

            int selectionIndex = FindPresetFolderSelectionIndex(folderIndex);
            if (selectionIndex < 0)
            {
                return;
            }

            presetIncludedFolderIndices.DeleteArrayElementAtIndex(selectionIndex);
            SetPresetFolderSelectionInitialized(true);
        }

        private void SetPresetFolderSelectionInitialized(bool value)
        {
            if (presetFolderSelectionInitialized == null)
            {
                return;
            }

            if (presetFolderSelectionInitialized.boolValue != value)
            {
                presetFolderSelectionInitialized.boolValue = value;
            }
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty juneHandlers;
        private readonly List<SerializedObject> juneHandlerObjects = new List<SerializedObject>();
        private readonly List<List<bool>> juneToggleFoldoutStatesByFolder = new List<List<bool>>();

        // June mapping cache
        private static JuneModel cachedJuneModel;
        private static string juneMappingCacheKey;
        private static Dictionary<string, JuneModuleView> juneModulesByKey;
        private static Dictionary<string, JuneModuleView> juneModulesByNormalizedName;
        private readonly Dictionary<string, bool> juneSectionFoldouts = new Dictionary<string, bool>();

        private static readonly string JuneMappingAssetPath = "Assets/Cozen/Enigma Launchpad/Scripts/Parser/JuneMapping.json";
        private static readonly JuneToggleType DefaultJuneToggleType = JuneToggleType.Blur;
        private static readonly GUIContent RegenerateJuneContent = new GUIContent("↻", "Regenerate June Mapping");
        private const string JuneShaderNameFragment = "June";

        private static readonly string[] AudiolinkBandOptions = new string[]
        {
            "Bass",
            "Low Mid",
            "High Mid",
            "Treble"
        };

        private static readonly JuneAudiolinkBand[] AudiolinkBandValues = new JuneAudiolinkBand[]
        {
            JuneAudiolinkBand.Bass,
            JuneAudiolinkBand.LowMid,
            JuneAudiolinkBand.HighMid,
            JuneAudiolinkBand.Treble
        };

        private static readonly string[] AudiolinkPowerOptions = new string[]
        {
            "Off",
            "0.25",
            "0.5",
            "0.75",
            "1.0"
        };

        private SerializedObject GetJuneHandlerObjectForFolder(int folderIdx)
        {
            if (juneHandlerObjects == null)
            {
                return null;
            }

            foreach (SerializedObject handlerObj in juneHandlerObjects)
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

        private void EnsureJuneHandlerParity()
        {
            juneHandlerObjects.Clear();

            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || juneHandlers == null)
            {
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            List<int> juneFolders = GetJuneFolderIndices();
            int juneFolderCount = juneFolders.Count;
            int requiredHandlerCount = juneFolderCount;

            var existingHandlers = new List<JuneHandler>();
            for (int i = 0; i < juneHandlers.arraySize; i++)
            {
                SerializedProperty element = juneHandlers.GetArrayElementAtIndex(i);
                if (element != null && element.objectReferenceValue is JuneHandler handler)
                {
                    existingHandlers.Add(handler);
                }
            }

            foreach (JuneHandler handler in launchpad.GetComponentsInChildren<JuneHandler>(true))
            {
                if (handler != null && !existingHandlers.Contains(handler))
                {
                    existingHandlers.Add(handler);
                }
            }

            var assigned = new JuneHandler[requiredHandlerCount];
            var unused = new List<JuneHandler>(existingHandlers);

            for (int i = 0; i < existingHandlers.Count; i++)
            {
                JuneHandler handler = existingHandlers[i];
                if (handler == null) continue;

                int folderIdx = handler.folderIndex;
                int slotIdx = juneFolders.IndexOf(folderIdx);
                if (slotIdx >= 0 && slotIdx < assigned.Length && assigned[slotIdx] == null)
                {
                    assigned[slotIdx] = handler;
                    unused.Remove(handler);
                }
            }

            for (int i = 0; i < requiredHandlerCount; i++)
            {
                if (assigned[i] != null) continue;

                int folderIdx = juneFolders[i];
                JuneHandler match = null;
                foreach (JuneHandler handler in unused)
                {
                    match = handler;
                    break;
                }

                if (match != null)
                {
                    assigned[i] = match;
                    unused.Remove(match);
                }
                else
                {
                    string handlerName = GetExpectedJuneHandlerName(folderIdx);
                    GameObject handlerObject = new GameObject(handlerName);
                    Undo.RegisterCreatedObjectUndo(handlerObject, "Create JuneHandler");
                    handlerObject.transform.SetParent(foldersTransform);
                    handlerObject.hideFlags = HandlerHideFlags;
                    assigned[i] = handlerObject.AddComponent<JuneHandler>();
                }

                Undo.RecordObject(assigned[i], "Configure JuneHandler");
                assigned[i].launchpad = launchpad;
                assigned[i].folderIndex = folderIdx;
                assigned[i].juneMaterial = launchpad.juneMaterial;

                string expectedName = GetExpectedJuneHandlerName(folderIdx);
                if (assigned[i].gameObject.name != expectedName)
                {
                    Undo.RecordObject(assigned[i].gameObject, "Rename JuneHandler");
                    assigned[i].gameObject.name = expectedName;
                }
            }

            foreach (JuneHandler handler in unused)
            {
                if (handler != null)
                {
                    Undo.DestroyObjectImmediate(handler.gameObject);
                }
            }

            juneHandlers.arraySize = requiredHandlerCount;
            for (int i = 0; i < requiredHandlerCount; i++)
            {
                SerializedProperty element = juneHandlers.GetArrayElementAtIndex(i);
                if (element != null)
                {
                    element.objectReferenceValue = assigned[i];
                }
            }

            for (int i = 0; i < requiredHandlerCount; i++)
            {
                JuneHandler handler = assigned[i];
                if (handler == null) continue;

                handler.transform.SetParent(foldersTransform);
                if (handler.gameObject.hideFlags != HandlerHideFlags)
                {
                    handler.gameObject.hideFlags = HandlerHideFlags;
                }

                juneHandlerObjects.Add(new SerializedObject(handler));
            }

            ApplyJuneMaterialToHandlers();
        }

        private void ApplyJuneMaterialToHandlers()
        {
            if (juneHandlerObjects == null || juneMaterialProperty == null)
            {
                return;
            }

            Material sharedMaterial = juneMaterialProperty.objectReferenceValue as Material;

            foreach (SerializedObject handlerObject in juneHandlerObjects)
            {
                if (handlerObject == null)
                {
                    continue;
                }

                handlerObject.Update();
                SerializedProperty materialProperty = handlerObject.FindProperty("juneMaterial");
                if (materialProperty != null)
                {
                    materialProperty.objectReferenceValue = sharedMaterial;
                }
                handlerObject.ApplyModifiedProperties();
            }
        }

        private List<int> GetJuneFolderIndices()
        {
            List<int> result = new List<int>();
            int folderCount = (folderTypesProperty != null) ? folderTypesProperty.arraySize : 0;

            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (typeProp != null && typeProp.enumValueIndex == (int)ToggleFolderType.June)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private bool DrawJuneFolderSection(
            int folderIndex,
            ToggleFolderType folderType,
            SerializedProperty exclProp,
            SerializedProperty countProp,
            ref bool structural)
        {
            if (folderType != ToggleFolderType.June)
            {
                return false;
            }

            SerializedObject handlerObject = GetJuneHandlerObjectForFolder(folderIndex);

            GUILayout.Space(6);
            if (handlerObject == null)
            {
                EditorGUILayout.HelpBox(
                    "June handler is auto-managed; reselect the Launchpad to regenerate if missing.",
                    MessageType.Warning);
            }
            else
            {
                handlerObject.Update();

                SerializedProperty juneRenderer = handlerObject.FindProperty("juneRenderer");
                if (juneRenderer != null)
                {
                    EditorGUILayout.PropertyField(juneRenderer, new GUIContent("Target Renderer"));

                    if (juneRenderer.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Assign a Target Renderer to drive June materials.", MessageType.Warning);
                    }
                }

                Material assignedJuneMaterial = juneMaterialProperty != null
                    ? juneMaterialProperty.objectReferenceValue as Material
                    : null;

                if (assignedJuneMaterial == null)
                {
                    EditorGUILayout.HelpBox("Assign a June Material to enable this folder.", MessageType.Warning);
                }
                else if (assignedJuneMaterial.shader == null)
                {
                    EditorGUILayout.HelpBox("Assigned June Material has no shader. Import June shaders to enable this folder.", MessageType.Warning);
                }
                else if (assignedJuneMaterial.shader.name.IndexOf(JuneShaderNameFragment, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    EditorGUILayout.HelpBox($"Assigned June Material does not use a June shader (found '{assignedJuneMaterial.shader.name}').", MessageType.Warning);
                }

                if (exclProp != null)
                {
                    GUILayout.Space(4);
                    EditorGUILayout.PropertyField(exclProp, new GUIContent("Make Entries Exclusive"));
                }

                GUILayout.Space(6);
                bool togglesChanged = DrawJuneToggleSection(folderIndex, handlerObject, countProp, assignedJuneMaterial);
                if (togglesChanged)
                {
                    structural = true;
                }
                
                // Add button to prepare and lock the June material
                GUILayout.Space(6);
                DrawJuneMaterialLockingSection(assignedJuneMaterial);

                handlerObject.ApplyModifiedProperties();
            }

            GUILayout.Space(6);
            EditorGUI.indentLevel--;
            return true;
        }
        
        private void DrawJuneMaterialLockingSection(Material juneMaterial)
        {
            if (juneMaterial == null)
            {
                return;
            }
            
            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null)
            {
                return;
            }
            
            // Check if the material is currently locked
            bool isLocked = IsJuneMaterialLocked(juneMaterial);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("June Material Locking", EditorStyles.boldLabel);
            
            if (isLocked)
            {
                EditorGUILayout.HelpBox(
                    "Material is currently LOCKED. Module editing is disabled. Click 'Unlock Material' to enable editing.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Click 'Prepare and Lock Material' to configure locking properties and invoke the June shader locking system.",
                    MessageType.Info);
            }
            
            EditorGUILayout.BeginHorizontal();
            
            if (isLocked)
            {
                if (GUILayout.Button("Unlock Material", GUILayout.Height(24)))
                {
                    UnlockJuneMaterial(juneMaterial);
                }
            }
            else
            {
                if (GUILayout.Button("Prepare and Lock Material", GUILayout.Height(24)))
                {
                    PrepareSingleJuneMaterial(launchpad, juneMaterial);
                    LockJuneMaterial(juneMaterial);
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private bool IsJuneMaterialLocked(Material juneMaterial)
        {
            if (juneMaterial == null || juneMaterial.shader == null)
            {
                return false;
            }
            
            // Locked shaders have "/locked/" in their name (e.g., "luke/june/five/locked/audiolink/...")
            string shaderName = juneMaterial.shader.name;
            return !string.IsNullOrEmpty(shaderName) && shaderName.Contains("/locked/");
        }
        
        private void PrepareSingleJuneMaterial(EnigmaLaunchpad launchpad, Material juneMaterial)
        {
            if (launchpad == null || juneMaterial == null)
            {
                return;
            }
            
            // Call the build validator's preparation method
            var errors = new List<string>();
            EnigmaLaunchpadBuildValidator.PrepareJuneMaterialForLocking(launchpad, juneMaterial, errors);
            
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Preparation Errors", 
                    string.Join("\n", errors), 
                    "OK");
            }
            else
            {
                Debug.Log($"[EnigmaLaunchpadEditor] Successfully prepared June material '{juneMaterial.name}' for locking");
            }
        }
        
        private void LockJuneMaterial(Material juneMaterial)
        {
            if (juneMaterial == null)
            {
                EditorUtility.DisplayDialog("Lock Error", "No material to lock.", "OK");
                return;
            }
            
            // Validate material has a shader
            if (juneMaterial.shader == null)
            {
                EditorUtility.DisplayDialog("Lock Error", 
                    "Material has no shader assigned. Assign a June shader before locking.", 
                    "OK");
                return;
            }
            
            // Validate it's a June shader
            if (juneMaterial.shader.name.IndexOf("June", StringComparison.OrdinalIgnoreCase) < 0)
            {
                EditorUtility.DisplayDialog("Lock Error", 
                    $"Material does not use a June shader (found '{juneMaterial.shader.name}').", 
                    "OK");
                return;
            }
            
            try
            {
                // Find the JuneLock5 type across all loaded assemblies
                System.Type juneLockType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    juneLockType = assembly.GetType("June.JuneLock5");
                    if (juneLockType != null)
                        break;
                }
                
                if (juneLockType == null)
                {
                    EditorUtility.DisplayDialog("Lock Error", 
                        "June locking system (June.JuneLock5) not found. Make sure June shaders are imported.", 
                        "OK");
                    return;
                }
                
                var lockInstance = System.Activator.CreateInstance(juneLockType, new object[] { juneMaterial });
                var executeMethod = juneLockType.GetMethod("execute");
                
                if (executeMethod == null)
                {
                    EditorUtility.DisplayDialog("Lock Error", 
                        "June lock execute method not found.", 
                        "OK");
                    return;
                }
                
                executeMethod.Invoke(lockInstance, null);
                
                Debug.Log($"[EnigmaLaunchpadEditor] Successfully locked June material '{juneMaterial.name}'");
                EditorUtility.DisplayDialog("Success", 
                    $"Material '{juneMaterial.name}' has been locked!\n\nA new locked shader variant has been created.", 
                    "OK");
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                // Unwrap the inner exception for more helpful error messages
                var innerEx = ex.InnerException ?? ex;
                Debug.LogError($"[EnigmaLaunchpadEditor] Failed to lock June material: {innerEx.Message}\n{innerEx.StackTrace}");
                EditorUtility.DisplayDialog("Lock Error", 
                    $"Failed to lock material: {innerEx.Message}", 
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Failed to lock June material: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Lock Error", 
                    $"Failed to lock material: {ex.Message}", 
                    "OK");
            }
        }
        
        private void UnlockJuneMaterial(Material juneMaterial)
        {
            if (juneMaterial == null)
            {
                EditorUtility.DisplayDialog("Unlock Error", "No material to unlock.", "OK");
                return;
            }
            
            // Validate it's a locked June shader
            if (!IsJuneMaterialLocked(juneMaterial))
            {
                EditorUtility.DisplayDialog("Unlock Error", 
                    "Material is not locked. Only locked June materials can be unlocked.", 
                    "OK");
                return;
            }
            
            try
            {
                // Find the JuneFunc5 type across all loaded assemblies
                System.Type juneFuncType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    juneFuncType = assembly.GetType("June.JuneFunc5");
                    if (juneFuncType != null)
                        break;
                }
                
                if (juneFuncType == null)
                {
                    EditorUtility.DisplayDialog("Unlock Error", 
                        "June unlock system (June.JuneFunc5) not found. Make sure June shaders are imported.", 
                        "OK");
                    return;
                }
                
                var unlockMethod = juneFuncType.GetMethod("unlockShader", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (unlockMethod == null)
                {
                    EditorUtility.DisplayDialog("Unlock Error", 
                        "June unlock method not found.", 
                        "OK");
                    return;
                }
                
                unlockMethod.Invoke(null, new object[] { juneMaterial });
                
                Debug.Log($"[EnigmaLaunchpadEditor] Successfully unlocked June material '{juneMaterial.name}'");
                EditorUtility.DisplayDialog("Success", 
                    $"Material '{juneMaterial.name}' has been unlocked!\n\nThe material is now using the standard June shader.", 
                    "OK");
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                // Unwrap the inner exception for more helpful error messages
                var innerEx = ex.InnerException ?? ex;
                Debug.LogError($"[EnigmaLaunchpadEditor] Failed to unlock June material: {innerEx.Message}\n{innerEx.StackTrace}");
                EditorUtility.DisplayDialog("Unlock Error", 
                    $"Failed to unlock material: {innerEx.Message}", 
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Failed to unlock June material: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Unlock Error", 
                    $"Failed to unlock material: {ex.Message}", 
                    "OK");
            }
        }

        private bool DrawJuneToggleSection(int folderIndex, SerializedObject handlerObject, SerializedProperty countProp, Material juneMaterial)
        {
            if (handlerObject == null || countProp == null) return false;

            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");
            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");

            if (juneToggleTypes == null || juneToggleNames == null) return false;

            bool structuralChange = false;
            int count = Mathf.Max(0, countProp.intValue);

            EnsureJuneArrayCapacity(handlerObject, count);

            bool hasGeneratedJuneMapping = AssetDatabase.LoadAssetAtPath<TextAsset>(JuneMappingAssetPath) != null;
            bool isLocked = IsJuneMaterialLocked(juneMaterial);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"June Toggles ({count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (hasGeneratedJuneMapping && GUILayout.Button(RegenerateJuneContent, GUILayout.Width(EditorGUIUtility.singleLineHeight + 10f)))
            {
                RegenerateJuneMapping();
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
            
            // Disable editing if material is locked
            GUI.enabled = !isLocked;

            List<bool> toggleFoldouts = EnsureJuneToggleFoldoutsForFolder(folderIndex, count);

            for (int i = 0; i < count; i++)
            {
                SerializedProperty typeProp = GetJuneArrayElement(juneToggleTypes, i);
                SerializedProperty nameProp = GetJuneArrayElement(juneToggleNames, i);

                JuneToggleType currentType = (typeProp != null)
                    ? ClampJuneEnumValue<JuneToggleType>(typeProp.enumValueIndex)
                    : DefaultJuneToggleType;
                string toggleNameValue = GetJuneToggleDisplayName(handlerObject, i);
                string typeLabel = JuneHandler.GetJuneToggleTypeLabel(currentType);
                string headerCore = string.IsNullOrEmpty(toggleNameValue)
                    ? $"Toggle {i + 1}"
                    : $"Toggle {i + 1}: {toggleNameValue}";
                string headerLabel = $"{headerCore} ({typeLabel})";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool expanded = (toggleFoldouts.Count > i) ? toggleFoldouts[i] : true;
                bool newExpanded = EditorGUILayout.Foldout(expanded, headerLabel, true);
                if (toggleFoldouts.Count > i && newExpanded != toggleFoldouts[i])
                {
                    toggleFoldouts[i] = newExpanded;
                }
                GUILayout.FlexibleSpace();

                GUI.enabled = !isLocked && i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    MoveJuneToggle(handlerObject, i, i - 1);
                    structuralChange = true;
                }
                GUI.enabled = !isLocked && !structuralChange && i < count - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    MoveJuneToggle(handlerObject, i, i + 1);
                    structuralChange = true;
                }
                GUI.enabled = !isLocked;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    DeleteJuneToggle(handlerObject, i);
                    countProp.intValue = Mathf.Max(0, count - 1);
                    structuralChange = true;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (newExpanded && !structuralChange)
                {
                    GUI.enabled = !isLocked;
                    EditorGUI.BeginChangeCheck();
                    JuneToggleType newType = (JuneToggleType)EditorGUILayout.EnumPopup(
                        new GUIContent("Toggle Type"),
                        currentType);
                    if (EditorGUI.EndChangeCheck() && typeProp != null)
                    {
                        AutoAssignJuneToggleName(nameProp, currentType, newType);
                        typeProp.enumValueIndex = (int)newType;
                        currentType = newType;
                        structuralChange = true;
                    }

                    if (nameProp != null)
                    {
                        if (currentType == JuneToggleType.Audiolink)
                        {
                            if (!string.IsNullOrEmpty(nameProp.stringValue))
                            {
                                nameProp.stringValue = string.Empty;
                            }
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(nameProp, new GUIContent("Name"));
                        }
                    }

                    // Draw properties for this toggle type using the generated June mapping
                    DrawJuneToggleProperties(handlerObject, currentType, folderIndex, i);
                    GUI.enabled = true;
                }

                EditorGUILayout.EndVertical();

                if (structuralChange) break;
            }

            if (!structuralChange)
            {
                GUI.enabled = !isLocked;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Toggle"))
                {
                    InsertJuneToggle(handlerObject, count);
                    countProp.intValue = count + 1;
                    EnsureJuneToggleFoldoutsForFolder(folderIndex, countProp.intValue);
                    structuralChange = true;
                }

                GUI.enabled = !isLocked && count > 0;
                if (!structuralChange && GUILayout.Button("- Toggle"))
                {
                    DeleteJuneToggle(handlerObject, count - 1);
                    countProp.intValue = Mathf.Max(0, count - 1);
                    EnsureJuneToggleFoldoutsForFolder(folderIndex, countProp.intValue);
                    structuralChange = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            
            // Re-enable GUI after section
            GUI.enabled = true;

            return structuralChange;
        }

        private void DrawJuneToggleProperties(
            SerializedObject handlerObject,
            JuneToggleType toggleType,
            int folderIndex,
            int flatToggleIndex)
        {
            if (toggleType == JuneToggleType.Audiolink)
            {
                DrawJuneAudiolinkProperties(handlerObject, flatToggleIndex);
                return;
            }

            string toggleLabel = JuneHandler.GetJuneToggleTypeLabel(toggleType);

            if (!TryGetJuneModule(toggleType, out JuneModuleView moduleDefinition))
            {
                if (GUILayout.Button(new GUIContent($"Generate {toggleLabel} Mapping", "Generate June mapping data to enable this UI.")))
                {
                    RegenerateJuneMapping();
                }
                return;
            }

            GUILayout.Space(4);

            // Load current preset values for this toggle
            Dictionary<string, float> floatPresets = new Dictionary<string, float>();
            Dictionary<string, Color> colorPresets = new Dictionary<string, Color>();
            Dictionary<string, Texture> texturePresets = new Dictionary<string, Texture>();
            Dictionary<string, Vector4> vectorPresets = new Dictionary<string, Vector4>();
            LoadJunePresetForToggle(handlerObject, flatToggleIndex, floatPresets, colorPresets, texturePresets, vectorPresets);
            SeedJuneDefaults(moduleDefinition, floatPresets, colorPresets, texturePresets, vectorPresets);

            // Track if any property changed
            EditorGUI.BeginChangeCheck();

            bool appliedAllowOverrides = ApplyJuneAllowOverrides(moduleDefinition, floatPresets);

            DrawJuneModuleProperties(
                moduleDefinition,
                floatPresets,
                colorPresets,
                texturePresets,
                vectorPresets,
                folderIndex,
                flatToggleIndex);

            // Save changes if any
            bool propertyChanged = EditorGUI.EndChangeCheck() || appliedAllowOverrides;
            if (propertyChanged)
            {
                SaveJunePresetForToggle(handlerObject, flatToggleIndex, floatPresets, colorPresets, texturePresets, vectorPresets, moduleDefinition);
                EditorUtility.SetDirty(target);
            }
        }

        private void DrawJuneAudiolinkProperties(SerializedObject handlerObject, int flatToggleIndex)
        {
            SerializedProperty juneAudiolinkControlTypes = handlerObject.FindProperty("juneAudiolinkControlTypes");
            SerializedProperty juneAudiolinkBands = handlerObject.FindProperty("juneAudiolinkBands");
            SerializedProperty juneAudiolinkPowers = handlerObject.FindProperty("juneAudiolinkPowers");

            SerializedProperty controlTypeProp = GetJuneArrayElement(juneAudiolinkControlTypes, flatToggleIndex);
            SerializedProperty bandProp = GetJuneArrayElement(juneAudiolinkBands, flatToggleIndex);
            SerializedProperty powerProp = GetJuneArrayElement(juneAudiolinkPowers, flatToggleIndex);

            JuneAudiolinkControlType controlType = controlTypeProp != null
                ? ClampJuneEnumValue<JuneAudiolinkControlType>(controlTypeProp.enumValueIndex)
                : JuneAudiolinkControlType.BandToggle;

            EditorGUI.BeginChangeCheck();
            JuneAudiolinkControlType newControlType = (JuneAudiolinkControlType)EditorGUILayout.EnumPopup(new GUIContent("Type"), controlType);
            if (EditorGUI.EndChangeCheck() && controlTypeProp != null)
            {
                controlTypeProp.enumValueIndex = (int)newControlType;
                controlType = newControlType;
            }

            switch (controlType)
            {
                case JuneAudiolinkControlType.BandToggle:
                    DrawAudiolinkBandField(bandProp, "Band");
                    break;
                case JuneAudiolinkControlType.BandSelector:
                    DrawAudiolinkBandField(bandProp, "Default Band");
                    break;
                case JuneAudiolinkControlType.PowerToggle:
                    DrawAudiolinkPowerField(powerProp, "Power");
                    break;
                case JuneAudiolinkControlType.PowerSelector:
                    DrawAudiolinkPowerField(powerProp, "Default Power");
                    break;
            }
        }

        private void DrawAudiolinkBandField(SerializedProperty bandProp, string label)
        {
            if (bandProp == null)
            {
                EditorGUILayout.HelpBox("Audiolink band configuration is unavailable.", MessageType.Warning);
                return;
            }

            int storedIndex = ClampJuneEnumIndex<JuneAudiolinkBand>(bandProp.enumValueIndex);
            JuneAudiolinkBand storedBand = (JuneAudiolinkBand)storedIndex;
            int optionIndex = Array.IndexOf(AudiolinkBandValues, storedBand);
            if (optionIndex < 0)
            {
                optionIndex = 0;
                bandProp.enumValueIndex = (int)AudiolinkBandValues[optionIndex];
            }

            int newIndex = EditorGUILayout.Popup(new GUIContent(label), optionIndex, AudiolinkBandOptions);
            if (newIndex != optionIndex && newIndex >= 0 && newIndex < AudiolinkBandValues.Length)
            {
                bandProp.enumValueIndex = (int)AudiolinkBandValues[newIndex];
            }
        }

        private void DrawAudiolinkPowerField(SerializedProperty powerProp, string label)
        {
            if (powerProp == null)
            {
                EditorGUILayout.HelpBox("Audiolink power configuration is unavailable.", MessageType.Warning);
                return;
            }

            int currentIndex = ClampJuneEnumIndex<JuneAudiolinkPowerLevel>(powerProp.enumValueIndex);
            int newIndex = EditorGUILayout.Popup(new GUIContent(label), currentIndex, AudiolinkPowerOptions);
            if (newIndex != currentIndex)
            {
                powerProp.enumValueIndex = newIndex;
            }
        }

        private bool TryGetJuneModule(JuneToggleType toggleType, out JuneModuleView moduleDefinition)
        {
            moduleDefinition = null;

            if (toggleType == JuneToggleType.Audiolink)
            {
                return false;
            }

            EnsureJuneMappingLoaded();

            if (juneModulesByNormalizedName == null || juneModulesByNormalizedName.Count == 0)
            {
                return false;
            }

            string toggleKey = NormalizeJuneKey(JuneHandler.GetJuneToggleTypeLabel(toggleType));
            if (string.IsNullOrEmpty(toggleKey))
            {
                return false;
            }

            if (juneModulesByNormalizedName.TryGetValue(toggleKey, out moduleDefinition))
            {
                return true;
            }

            foreach (var kvp in juneModulesByNormalizedName)
            {
                if (kvp.Key.Contains(toggleKey) || toggleKey.Contains(kvp.Key))
                {
                    moduleDefinition = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        private void LoadJunePresetForToggle(
            SerializedObject handlerObject,
            int flatToggleIndex,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets)
        {
            SerializedProperty juneToggleStartIndices = handlerObject.FindProperty("juneToggleStartIndices");
            SerializedProperty juneTogglePropertyCounts = handlerObject.FindProperty("juneTogglePropertyCounts");
            SerializedProperty juneTogglePropertyNames = handlerObject.FindProperty("juneTogglePropertyNames");
            SerializedProperty juneToggleFloatValues = handlerObject.FindProperty("juneToggleFloatValues");
            SerializedProperty juneToggleColorValues = handlerObject.FindProperty("juneToggleColorValues");
            SerializedProperty juneToggleTextureValues = handlerObject.FindProperty("juneToggleTextureValues");
            SerializedProperty juneToggleVectorValues = handlerObject.FindProperty("juneToggleVectorValues");
            SerializedProperty juneToggleHasTextureValues = handlerObject.FindProperty("juneToggleHasTextureValues");
            SerializedProperty juneToggleHasVectorValues = handlerObject.FindProperty("juneToggleHasVectorValues");

            if (juneToggleStartIndices == null || flatToggleIndex >= juneToggleStartIndices.arraySize) return;
            if (juneTogglePropertyCounts == null || flatToggleIndex >= juneTogglePropertyCounts.arraySize) return;

            int startIdx = juneToggleStartIndices.GetArrayElementAtIndex(flatToggleIndex).intValue;
            int count = juneTogglePropertyCounts.GetArrayElementAtIndex(flatToggleIndex).intValue;

            for (int i = 0; i < count; i++)
            {
                int propIdx = startIdx + i;
                if (juneTogglePropertyNames == null || propIdx >= juneTogglePropertyNames.arraySize) break;

                string propName = juneTogglePropertyNames.GetArrayElementAtIndex(propIdx).stringValue;
                if (string.IsNullOrEmpty(propName)) continue;

                bool hasTextureFlag = juneToggleHasTextureValues != null
                    && propIdx < juneToggleHasTextureValues.arraySize
                    && juneToggleHasTextureValues.GetArrayElementAtIndex(propIdx).boolValue;
                bool hasVectorFlag = juneToggleHasVectorValues != null
                    && propIdx < juneToggleHasVectorValues.arraySize
                    && juneToggleHasVectorValues.GetArrayElementAtIndex(propIdx).boolValue;

                if (hasTextureFlag && juneToggleTextureValues != null && propIdx < juneToggleTextureValues.arraySize)
                {
                    texturePresets[propName] = juneToggleTextureValues.GetArrayElementAtIndex(propIdx).objectReferenceValue as Texture;
                    continue;
                }

                if (hasVectorFlag && juneToggleVectorValues != null && propIdx < juneToggleVectorValues.arraySize)
                {
                    vectorPresets[propName] = juneToggleVectorValues.GetArrayElementAtIndex(propIdx).vector4Value;
                    continue;
                }

                bool isColorProperty = propName.Contains("Color") || propName.Contains("colour");

                if (isColorProperty && juneToggleColorValues != null && propIdx < juneToggleColorValues.arraySize)
                {
                    colorPresets[propName] = juneToggleColorValues.GetArrayElementAtIndex(propIdx).colorValue;
                }
                else if (juneToggleFloatValues != null && propIdx < juneToggleFloatValues.arraySize)
                {
                    floatPresets[propName] = juneToggleFloatValues.GetArrayElementAtIndex(propIdx).floatValue;
                }
            }
        }

        private void SeedJuneDefaults(
            JuneModuleView moduleDefinition,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets)
        {
            if (moduleDefinition?.properties == null) return;

            var sectionScopedProperties = moduleDefinition.sectionProperties ?? new HashSet<JunePropertyView>();

            foreach (var propDef in moduleDefinition.properties)
            {
                if (propDef == null || string.IsNullOrEmpty(propDef.shaderPropertyName)) continue;
                if (sectionScopedProperties.Contains(propDef)) continue;

                switch (propDef.propertyType)
                {
                    case JunePropertyKind.Color:
                        if (propDef.hasDefaultColor && !colorPresets.ContainsKey(propDef.shaderPropertyName))
                        {
                            colorPresets[propDef.shaderPropertyName] = propDef.defaultColor;
                        }
                        break;
                    case JunePropertyKind.Texture:
                        if (!texturePresets.ContainsKey(propDef.shaderPropertyName))
                        {
                            texturePresets[propDef.shaderPropertyName] = null;
                        }
                        break;
                    case JunePropertyKind.Vector:
                        if (propDef.hasDefaultVector && !vectorPresets.ContainsKey(propDef.shaderPropertyName))
                        {
                            vectorPresets[propDef.shaderPropertyName] = propDef.defaultVector;
                        }
                        break;
                    default:
                        if (!floatPresets.ContainsKey(propDef.shaderPropertyName))
                        {
                            floatPresets[propDef.shaderPropertyName] = propDef.defaultValue;
                        }
                        break;
                }
            }
        }

        private bool ApplyJuneAllowOverrides(JuneModuleView moduleDefinition, Dictionary<string, float> floatPresets)
        {
            if (moduleDefinition?.properties == null) return false;

            bool changed = false;
            foreach (var propDef in moduleDefinition.properties)
            {
                if (propDef == null) continue;
                if (!IsJuneAllowProperty(propDef)) continue;
                if (!floatPresets.ContainsKey(propDef.shaderPropertyName) || floatPresets[propDef.shaderPropertyName] < 0.5f)
                {
                    floatPresets[propDef.shaderPropertyName] = 1f;
                    changed = true;
                }
            }
            return changed;
        }

        private void DrawJuneModuleProperties(
            JuneModuleView moduleDefinition,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets,
            int folderIndex,
            int flatToggleIndex)
        {
            if (moduleDefinition == null) return;

            var sectionProperties = moduleDefinition.sectionProperties ?? new HashSet<JunePropertyView>();
            bool hasSections = moduleDefinition.sections != null && moduleDefinition.sections.Count > 0;
            var ungroupedProperties = hasSections
                ? moduleDefinition.ungroupedProperties
                : moduleDefinition.ungroupedProperties ?? moduleDefinition.properties;

            ungroupedProperties = ungroupedProperties ?? new List<JunePropertyView>();

            foreach (var propDef in ungroupedProperties)
            {
                if (sectionProperties.Contains(propDef)) continue;
                if (IsJuneAllowProperty(propDef)) continue;
                if (!IsJunePropertyVisibleForPreset(moduleDefinition, propDef, floatPresets)) continue;

                if (propDef.indented) EditorGUI.indentLevel++;
                DrawJunePresetProperty(propDef, floatPresets, colorPresets, texturePresets, vectorPresets);
                if (propDef.indented) EditorGUI.indentLevel--;
            }

            if (moduleDefinition.sections == null) return;

            foreach (var section in moduleDefinition.sections)
            {
                DrawJuneSection(
                    moduleDefinition,
                    section,
                    floatPresets,
                    colorPresets,
                    texturePresets,
                    vectorPresets,
                    folderIndex,
                    flatToggleIndex);
            }
        }

        private void DrawJuneSection(
            JuneModuleView moduleDefinition,
            JuneSectionView section,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets,
            int folderIndex,
            int flatToggleIndex)
        {
            if (section == null) return;

            string foldoutKey = $"{folderIndex}:{flatToggleIndex}:{moduleDefinition.moduleName}|{section.FullPath}";
            bool expanded = juneSectionFoldouts.TryGetValue(foldoutKey, out bool storedState) && storedState;

            int indentBefore = EditorGUI.indentLevel;
            EditorGUI.indentLevel += Mathf.Max(0, section.IndentLevel);
            expanded = EditorGUILayout.Foldout(expanded, section.FoldoutName, true);
            juneSectionFoldouts[foldoutKey] = expanded;

            if (expanded)
            {
                EditorGUI.indentLevel++;

                foreach (var propDef in section.Properties)
                {
                    if (IsJuneAllowProperty(propDef)) continue;
                    if (!IsJunePropertyVisibleForPreset(moduleDefinition, propDef, floatPresets)) continue;

                    if (propDef.indented) EditorGUI.indentLevel++;
                    DrawJunePresetProperty(propDef, floatPresets, colorPresets, texturePresets, vectorPresets);
                    if (propDef.indented) EditorGUI.indentLevel--;
                }

                if (section.Children != null)
                {
                    foreach (var child in section.Children)
                    {
                        DrawJuneSection(
                            moduleDefinition,
                            child,
                            floatPresets,
                            colorPresets,
                            texturePresets,
                            vectorPresets,
                            folderIndex,
                            flatToggleIndex);
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel = indentBefore;
        }

        private bool IsJunePropertyVisibleForPreset(JuneModuleView moduleDefinition, JunePropertyView propDef, Dictionary<string, float> floatPresets)
        {
            if (propDef == null) return false;
            if (propDef.conditions == null || propDef.conditions.Count == 0) return true;

            foreach (var condition in propDef.conditions)
            {
                int count = Mathf.Min(condition.Paths.Count, condition.RequiredValues.Count);
                if (count == 0) continue;

                bool conditionSatisfied = true;
                for (int i = 0; i < count; i++)
                {
                    if (!TryResolvePresetConditionValue(moduleDefinition, condition.Paths[i], floatPresets, out float currentValue) ||
                        !Mathf.Approximately(currentValue, condition.RequiredValues[i]))
                    {
                        conditionSatisfied = false;
                        break;
                    }
                }

                if (conditionSatisfied) return true;
            }

            return false;
        }

        private bool TryResolvePresetConditionValue(JuneModuleView moduleDefinition, string path, Dictionary<string, float> floatPresets, out float value)
        {
            value = 0f;

            if (moduleDefinition?.propertyLookup == null || !moduleDefinition.propertyLookup.TryGetValue(path, out JunePropertyView property))
            {
                return false;
            }

            if (floatPresets != null && floatPresets.TryGetValue(property.shaderPropertyName, out float storedValue))
            {
                value = storedValue;
                return true;
            }

            value = property.defaultValue;
            return true;
        }

        private void DrawJunePresetProperty(
            JunePropertyView propDef,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets)
        {
            if (propDef == null || string.IsNullOrEmpty(propDef.shaderPropertyName)) return;

            GUIContent label = new GUIContent(propDef.displayName);

            switch (propDef.propertyType)
            {
                case JunePropertyKind.Color:
                    Color currentColor = colorPresets != null && colorPresets.TryGetValue(propDef.shaderPropertyName, out Color storedColor)
                        ? storedColor
                        : (propDef.hasDefaultColor ? propDef.defaultColor : Color.white);
                    Color newColor = EditorGUILayout.ColorField(label, currentColor);
                    colorPresets[propDef.shaderPropertyName] = newColor;
                    break;
                case JunePropertyKind.Toggle:
                    float storedToggle = floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float toggleValue) ? toggleValue : propDef.defaultValue;
                    bool newToggle = EditorGUILayout.Toggle(label, storedToggle > 0.5f);
                    floatPresets[propDef.shaderPropertyName] = newToggle ? 1f : 0f;
                    break;
                case JunePropertyKind.Enum:
                    var enumOptions = propDef.enumValues ?? new List<string>();
                    if (enumOptions.Count == 0) goto case JunePropertyKind.Float;
                    int currentIndex = Mathf.Clamp(Mathf.RoundToInt(floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float enumValue) ? enumValue : propDef.defaultValue), 0, enumOptions.Count - 1);
                    int newIndex = EditorGUILayout.Popup(label, currentIndex, enumOptions.ToArray());
                    floatPresets[propDef.shaderPropertyName] = newIndex;
                    break;
                case JunePropertyKind.Range:
                    float currentRange = floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float rangeValue) ? rangeValue : propDef.defaultValue;
                    float newRange = EditorGUILayout.Slider(label, currentRange, propDef.minValue, propDef.maxValue);
                    floatPresets[propDef.shaderPropertyName] = newRange;
                    break;
                case JunePropertyKind.Int:
                    int intValue = Mathf.RoundToInt(floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float intStored) ? intStored : propDef.defaultValue);
                    int newIntValue = EditorGUILayout.IntField(label, intValue);
                    floatPresets[propDef.shaderPropertyName] = newIntValue;
                    break;
                case JunePropertyKind.Texture:
                    texturePresets.TryGetValue(propDef.shaderPropertyName, out Texture storedTexture);
                    Texture newTexture = (Texture)EditorGUILayout.ObjectField(label, storedTexture, typeof(Texture), false);
                    texturePresets[propDef.shaderPropertyName] = newTexture;
                    break;
                case JunePropertyKind.Vector:
                    Vector4 currentVector = vectorPresets != null && vectorPresets.TryGetValue(propDef.shaderPropertyName, out Vector4 vectorValue)
                        ? vectorValue
                        : (propDef.hasDefaultVector ? propDef.defaultVector : Vector4.zero);
                    Vector4 newVector = EditorGUILayout.Vector4Field(label, currentVector);
                    vectorPresets[propDef.shaderPropertyName] = newVector;
                    break;
                case JunePropertyKind.Float:
                default:
                    float currentValue = floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float storedValue) ? storedValue : propDef.defaultValue;
                    float newValue = EditorGUILayout.FloatField(label, currentValue);
                    floatPresets[propDef.shaderPropertyName] = newValue;
                    break;
            }
        }

        private void SaveJunePresetForToggle(
            SerializedObject handlerObject,
            int flatToggleIndex,
            Dictionary<string, float> floatPresets,
            Dictionary<string, Color> colorPresets,
            Dictionary<string, Texture> texturePresets,
            Dictionary<string, Vector4> vectorPresets,
            JuneModuleView moduleDef)
        {
            List<string> propNames = new List<string>();
            List<float> floatValues = new List<float>();
            List<Color> colorValues = new List<Color>();
            List<Texture> textureValues = new List<Texture>();
            List<Vector4> vectorValues = new List<Vector4>();
            List<bool> hasTextureFlags = new List<bool>();
            List<bool> hasVectorFlags = new List<bool>();

            if (moduleDef?.properties != null)
            {
                foreach (var propDef in moduleDef.properties)
                {
                    if (propDef == null || string.IsNullOrEmpty(propDef.shaderPropertyName)) continue;

                    switch (propDef.propertyType)
                    {
                        case JunePropertyKind.Color:
                            if (colorPresets != null && colorPresets.TryGetValue(propDef.shaderPropertyName, out Color storedColor))
                            {
                                propNames.Add(propDef.shaderPropertyName);
                                floatValues.Add(0f);
                                colorValues.Add(storedColor);
                                textureValues.Add(null);
                                vectorValues.Add(Vector4.zero);
                                hasTextureFlags.Add(false);
                                hasVectorFlags.Add(false);
                            }
                            else if (propDef.hasDefaultColor)
                            {
                                propNames.Add(propDef.shaderPropertyName);
                                floatValues.Add(0f);
                                colorValues.Add(propDef.defaultColor);
                                textureValues.Add(null);
                                vectorValues.Add(Vector4.zero);
                                hasTextureFlags.Add(false);
                                hasVectorFlags.Add(false);
                            }
                            break;
                        case JunePropertyKind.Texture:
                            propNames.Add(propDef.shaderPropertyName);
                            floatValues.Add(0f);
                            colorValues.Add(Color.clear);
                            textureValues.Add(texturePresets != null && texturePresets.TryGetValue(propDef.shaderPropertyName, out Texture storedTexture) ? storedTexture : null);
                            vectorValues.Add(Vector4.zero);
                            hasTextureFlags.Add(true);
                            hasVectorFlags.Add(false);
                            break;
                        case JunePropertyKind.Vector:
                            propNames.Add(propDef.shaderPropertyName);
                            floatValues.Add(0f);
                            colorValues.Add(Color.clear);
                            vectorValues.Add(vectorPresets != null && vectorPresets.TryGetValue(propDef.shaderPropertyName, out Vector4 storedVector)
                                ? storedVector
                                : (propDef.hasDefaultVector ? propDef.defaultVector : Vector4.zero));
                            textureValues.Add(null);
                            hasTextureFlags.Add(false);
                            hasVectorFlags.Add(true);
                            break;
                        default:
                            float value = propDef.defaultValue;
                            if (floatPresets != null && floatPresets.TryGetValue(propDef.shaderPropertyName, out float storedValue))
                            {
                                value = storedValue;
                            }
                            propNames.Add(propDef.shaderPropertyName);
                            floatValues.Add(value);
                            colorValues.Add(Color.clear);
                            textureValues.Add(null);
                            vectorValues.Add(Vector4.zero);
                            hasTextureFlags.Add(false);
                            hasVectorFlags.Add(false);
                            break;
                    }
                }
            }

            int startIdx = CalculateJunePresetStartIndex(handlerObject, flatToggleIndex);
            EnsureJunePresetArraysSize(handlerObject, startIdx + propNames.Count);

            SerializedProperty juneToggleStartIndices = handlerObject.FindProperty("juneToggleStartIndices");
            SerializedProperty juneTogglePropertyCounts = handlerObject.FindProperty("juneTogglePropertyCounts");
            SerializedProperty juneTogglePropertyNames = handlerObject.FindProperty("juneTogglePropertyNames");
            SerializedProperty juneToggleFloatValues = handlerObject.FindProperty("juneToggleFloatValues");
            SerializedProperty juneToggleColorValues = handlerObject.FindProperty("juneToggleColorValues");
            SerializedProperty juneToggleTextureValues = handlerObject.FindProperty("juneToggleTextureValues");
            SerializedProperty juneToggleVectorValues = handlerObject.FindProperty("juneToggleVectorValues");
            SerializedProperty juneToggleHasTextureValues = handlerObject.FindProperty("juneToggleHasTextureValues");
            SerializedProperty juneToggleHasVectorValues = handlerObject.FindProperty("juneToggleHasVectorValues");

            if (juneToggleStartIndices != null && flatToggleIndex >= juneToggleStartIndices.arraySize)
            {
                juneToggleStartIndices.arraySize = flatToggleIndex + 1;
            }

            if (juneTogglePropertyCounts != null && flatToggleIndex >= juneTogglePropertyCounts.arraySize)
            {
                juneTogglePropertyCounts.arraySize = flatToggleIndex + 1;
            }

            if (juneToggleStartIndices != null)
                juneToggleStartIndices.GetArrayElementAtIndex(flatToggleIndex).intValue = startIdx;
            if (juneTogglePropertyCounts != null)
                juneTogglePropertyCounts.GetArrayElementAtIndex(flatToggleIndex).intValue = propNames.Count;

            for (int i = 0; i < propNames.Count; i++)
            {
                int propIdx = startIdx + i;
                if (juneTogglePropertyNames != null && propIdx < juneTogglePropertyNames.arraySize)
                    juneTogglePropertyNames.GetArrayElementAtIndex(propIdx).stringValue = propNames[i];
                if (juneToggleFloatValues != null && propIdx < juneToggleFloatValues.arraySize)
                    juneToggleFloatValues.GetArrayElementAtIndex(propIdx).floatValue = floatValues[i];
                if (juneToggleColorValues != null && propIdx < juneToggleColorValues.arraySize)
                    juneToggleColorValues.GetArrayElementAtIndex(propIdx).colorValue = colorValues[i];
                if (juneToggleTextureValues != null && propIdx < juneToggleTextureValues.arraySize)
                    juneToggleTextureValues.GetArrayElementAtIndex(propIdx).objectReferenceValue = textureValues[i];
                if (juneToggleVectorValues != null && propIdx < juneToggleVectorValues.arraySize)
                    juneToggleVectorValues.GetArrayElementAtIndex(propIdx).vector4Value = vectorValues[i];
                if (juneToggleHasTextureValues != null && propIdx < juneToggleHasTextureValues.arraySize)
                    juneToggleHasTextureValues.GetArrayElementAtIndex(propIdx).boolValue = hasTextureFlags[i];
                if (juneToggleHasVectorValues != null && propIdx < juneToggleHasVectorValues.arraySize)
                    juneToggleHasVectorValues.GetArrayElementAtIndex(propIdx).boolValue = hasVectorFlags[i];
            }

            handlerObject.ApplyModifiedProperties();
        }

        private int CalculateJunePresetStartIndex(SerializedObject handlerObject, int flatToggleIndex)
        {
            SerializedProperty juneTogglePropertyCounts = handlerObject.FindProperty("juneTogglePropertyCounts");
            int startIdx = 0;
            for (int i = 0; i < flatToggleIndex; i++)
            {
                if (juneTogglePropertyCounts != null && i < juneTogglePropertyCounts.arraySize)
                {
                    startIdx += juneTogglePropertyCounts.GetArrayElementAtIndex(i).intValue;
                }
            }
            return startIdx;
        }

        private void EnsureJunePresetArraysSize(SerializedObject handlerObject, int requiredSize)
        {
            EnsureArraySize(handlerObject.FindProperty("juneTogglePropertyNames"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleFloatValues"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleColorValues"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleTextureValues"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleVectorValues"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleHasTextureValues"), requiredSize);
            EnsureArraySize(handlerObject.FindProperty("juneToggleHasVectorValues"), requiredSize);
        }

        private void EnsureArraySize(SerializedProperty prop, int requiredSize)
        {
            if (prop != null && prop.arraySize < requiredSize)
            {
                prop.arraySize = requiredSize;
            }
        }

        private bool IsJuneAllowProperty(JunePropertyView property)
        {
            if (property == null) return false;

            string displayName = (property.displayName ?? property.name ?? string.Empty).TrimStart();
            bool hasAllowDisplayName = displayName.StartsWith("Allow", StringComparison.OrdinalIgnoreCase);

            string shaderName = property.shaderPropertyName ?? string.Empty;
            shaderName = shaderName.TrimStart('_');
            bool hasAllowShaderName = shaderName.StartsWith("Allow", StringComparison.OrdinalIgnoreCase);

            return hasAllowDisplayName || hasAllowShaderName;
        }

        private void EnsureJuneMappingLoaded()
        {
            TextAsset mappingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JuneMappingAssetPath);
            cachedJuneModel = JuneMappingJsonReader.Load(mappingAsset, out string cacheKey);

            bool cacheValid = string.Equals(juneMappingCacheKey, cacheKey, StringComparison.Ordinal);
            if (cacheValid && juneModulesByKey != null && juneModulesByNormalizedName != null) return;

            juneMappingCacheKey = cacheKey;
            BuildJuneModuleLookups(cachedJuneModel, out juneModulesByKey, out juneModulesByNormalizedName);
        }

        private void RegenerateJuneMapping()
        {
            Enigma.Editor.JuneMappingUtilityWindow.RegenerateMapping();
            TextAsset mappingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JuneMappingAssetPath);
            cachedJuneModel = JuneMappingJsonReader.Load(mappingAsset, out string cacheKey);
            juneMappingCacheKey = cacheKey;

            if (cachedJuneModel != null)
            {
                BuildJuneModuleLookups(cachedJuneModel, out juneModulesByKey, out juneModulesByNormalizedName);
            }
            else
            {
                juneModulesByKey = null;
                juneModulesByNormalizedName = null;
            }
        }

        private void BuildJuneModuleLookups(
            JuneModel model,
            out Dictionary<string, JuneModuleView> byName,
            out Dictionary<string, JuneModuleView> byNormalizedName)
        {
            byName = new Dictionary<string, JuneModuleView>(StringComparer.OrdinalIgnoreCase);
            byNormalizedName = new Dictionary<string, JuneModuleView>(StringComparer.OrdinalIgnoreCase);

            if (model?.modules == null) return;

            foreach (var module in model.modules)
            {
                if (module == null || string.IsNullOrEmpty(module.name)) continue;

                JuneModuleView view = BuildJuneModuleView(module);
                byName[module.name] = view;

                string normalized = NormalizeJuneKey(module.name);
                if (!string.IsNullOrEmpty(normalized) && !byNormalizedName.ContainsKey(normalized))
                {
                    byNormalizedName[normalized] = view;
                }
            }
        }

        private static string NormalizeJuneKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }
            return builder.ToString();
        }

        private JuneModuleView BuildJuneModuleView(JuneModule module)
        {
            var view = new JuneModuleView
            {
                moduleName = module.name ?? string.Empty,
                properties = new List<JunePropertyView>(),
                sections = new List<JuneSectionView>(),
                propertyLookup = new Dictionary<string, JunePropertyView>(StringComparer.OrdinalIgnoreCase),
            };

            if (module.properties != null)
            {
                foreach (var property in module.properties)
                {
                    if (property == null) continue;

                    JunePropertyView propertyView = BuildJunePropertyView(property);
                    view.properties.Add(propertyView);
                    AddJunePropertyLookup(view.propertyLookup, propertyView);
                }
            }

            BuildSectionViews(module, view);
            PopulateSectionPropertyCache(view);

            return view;
        }

        private void PopulateSectionPropertyCache(JuneModuleView view)
        {
            if (view == null) return;

            view.sectionProperties.Clear();
            view.ungroupedProperties.Clear();

            void Collect(JuneSectionView section)
            {
                if (section == null) return;

                if (section.Properties != null)
                {
                    foreach (var property in section.Properties)
                    {
                        if (property != null) view.sectionProperties.Add(property);
                    }
                }

                if (section.Children != null)
                {
                    foreach (var child in section.Children) Collect(child);
                }
            }

            if (view.sections != null)
            {
                foreach (var section in view.sections) Collect(section);
            }

            if (view.properties != null)
            {
                foreach (var property in view.properties)
                {
                    if (property != null && !view.sectionProperties.Contains(property))
                    {
                        view.ungroupedProperties.Add(property);
                    }
                }
            }
        }

        private JunePropertyView BuildJunePropertyView(JuneProperty property)
        {
            var propertyView = new JunePropertyView
            {
                name = property.name ?? property.shaderPropertyName ?? property.rawShaderPropertyName ?? string.Empty,
                displayName = string.IsNullOrEmpty(property.displayName) ? property.name ?? property.shaderPropertyName ?? string.Empty : property.displayName,
                shaderPropertyName = property.shaderPropertyName ?? property.rawShaderPropertyName ?? property.name ?? string.Empty,
                rawShaderPropertyName = property.rawShaderPropertyName ?? property.shaderPropertyName ?? property.name ?? string.Empty,
                propertyType = DeterminePropertyKind(property),
                minValue = property.min ?? 0f,
                maxValue = property.max ?? 1f,
                defaultValue = property.defaultValue ?? (property.defaultIntValue.HasValue ? property.defaultIntValue.Value : 0f),
                enumValues = property.enumValues ?? new List<string>(),
                indented = property.indented,
                conditions = ConvertConditions(property.conditions),
            };

            if (property.defaultColor != null && property.defaultColor.Length >= 4)
            {
                propertyView.defaultColor = new Color(property.defaultColor[0], property.defaultColor[1], property.defaultColor[2], property.defaultColor[3]);
                propertyView.hasDefaultColor = true;
            }

            if (property.defaultVector != null && property.defaultVector.Length >= 4)
            {
                propertyView.defaultVector = new Vector4(property.defaultVector[0], property.defaultVector[1], property.defaultVector[2], property.defaultVector[3]);
                propertyView.hasDefaultVector = true;
            }

            return propertyView;
        }

        private static List<JuneCondition> ConvertConditions(List<ConditionalRule> conditions)
        {
            var converted = new List<JuneCondition>();
            if (conditions == null) return converted;

            foreach (var condition in conditions)
            {
                if (condition?.paths == null || condition.values == null) continue;

                var newCondition = new JuneCondition();
                int count = Mathf.Min(condition.paths.Count, condition.values.Count);
                for (int i = 0; i < count; i++)
                {
                    newCondition.Paths.Add(condition.paths[i]);
                    float floatValue = 0f;
                    if (condition.values[i] != null)
                    {
                        float.TryParse(condition.values[i].ToString(), out floatValue);
                    }
                    newCondition.RequiredValues.Add(floatValue);
                }

                if (newCondition.Paths.Count > 0) converted.Add(newCondition);
            }

            return converted;
        }

        private JunePropertyKind DeterminePropertyKind(JuneProperty property)
        {
            if (property.enumValues != null && property.enumValues.Count > 0) return JunePropertyKind.Enum;
            if (string.Equals(property.propertyType, "Color", StringComparison.OrdinalIgnoreCase)) return JunePropertyKind.Color;
            if (string.Equals(property.propertyType, "Texture", StringComparison.OrdinalIgnoreCase)) return JunePropertyKind.Texture;
            if (string.Equals(property.propertyType, "Vector", StringComparison.OrdinalIgnoreCase)) return JunePropertyKind.Vector;
            if (string.Equals(property.propertyType, "Range", StringComparison.OrdinalIgnoreCase)) return JunePropertyKind.Range;

            if (string.Equals(property.propertyType, "Int", StringComparison.OrdinalIgnoreCase))
            {
                return property.max.HasValue && property.min.HasValue && property.min.Value == 0f && property.max.Value == 1f
                    ? JunePropertyKind.Toggle
                    : JunePropertyKind.Int;
            }

            if (property.max.HasValue && property.min.HasValue && property.min.Value == 0f && property.max.Value == 1f)
            {
                return JunePropertyKind.Toggle;
            }

            return JunePropertyKind.Float;
        }

        private void AddJunePropertyLookup(Dictionary<string, JunePropertyView> lookup, JunePropertyView property)
        {
            void TryAdd(string key)
            {
                if (!string.IsNullOrEmpty(key) && !lookup.ContainsKey(key)) lookup.Add(key, property);
            }

            TryAdd(property.name);
            TryAdd(property.shaderPropertyName);
            TryAdd(property.rawShaderPropertyName);
            if (!string.IsNullOrEmpty(property.name)) TryAdd($"prp{property.name}");
        }

        private void BuildSectionViews(JuneModule module, JuneModuleView view)
        {
            if (module.sections == null || module.sections.Count == 0)
            {
                view.sections = new List<JuneSectionView>();
                return;
            }

            var sectionsByName = new Dictionary<string, JuneSectionView>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < module.sections.Count; i++)
            {
                JuneSection section = module.sections[i];
                if (section == null || string.IsNullOrEmpty(section.name)) continue;

                var sectionView = new JuneSectionView
                {
                    Name = section.name,
                    FoldoutName = string.IsNullOrEmpty(section.foldoutName) ? section.name : section.foldoutName,
                    IndentLevel = Mathf.Max(0, section.indentLevel),
                    DisplayOrder = section.displayOrder,
                    FullPath = section.name,
                    Properties = new List<JunePropertyView>(),
                    Children = new List<JuneSectionView>()
                };

                if (section.propertyIndices != null)
                {
                    foreach (int propertyIndex in section.propertyIndices)
                    {
                        if (propertyIndex >= 0 && propertyIndex < view.properties.Count)
                        {
                            sectionView.Properties.Add(view.properties[propertyIndex]);
                        }
                    }
                }

                sectionsByName[section.name] = sectionView;
            }

            List<JuneSectionView> roots = new List<JuneSectionView>();

            foreach (JuneSection section in module.sections)
            {
                if (section == null || string.IsNullOrEmpty(section.name)) continue;

                JuneSectionView sectionView = sectionsByName[section.name];

                if (!string.IsNullOrEmpty(section.parentSection) && sectionsByName.TryGetValue(section.parentSection, out JuneSectionView parent))
                {
                    sectionView.FullPath = $"{parent.FullPath}/{sectionView.Name}";
                    parent.Children.Add(sectionView);
                }
                else
                {
                    roots.Add(sectionView);
                }
            }

            foreach (var root in roots) SortSectionHierarchy(root);

            view.sections = roots
                .OrderBy(section => section.DisplayOrder < 0 ? int.MaxValue : section.DisplayOrder)
                .ThenBy(section => section.Name)
                .ToList();
        }

        private void SortSectionHierarchy(JuneSectionView section)
        {
            if (section.Children == null || section.Children.Count == 0) return;

            section.Children = section.Children
                .OrderBy(child => child.DisplayOrder < 0 ? int.MaxValue : child.DisplayOrder)
                .ThenBy(child => child.Name)
                .ToList();

            foreach (var child in section.Children) SortSectionHierarchy(child);
        }

        // June toggle helpers
        private string GetJuneToggleDisplayName(SerializedObject handlerObject, int flatIndex)
        {
            if (flatIndex < 0 || handlerObject == null) return string.Empty;

            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");
            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");

            JuneToggleType toggleType = JuneToggleType.Blur;
            if (juneToggleTypes != null && flatIndex < juneToggleTypes.arraySize)
            {
                SerializedProperty typeProp = juneToggleTypes.GetArrayElementAtIndex(flatIndex);
                if (typeProp != null) toggleType = ClampJuneEnumValue<JuneToggleType>(typeProp.enumValueIndex);
            }

            if (toggleType == JuneToggleType.Audiolink)
            {
                return GetAudiolinkDisplayName(handlerObject, flatIndex);
            }

            string customName = string.Empty;
            if (juneToggleNames != null && flatIndex < juneToggleNames.arraySize)
            {
                SerializedProperty nameProp = juneToggleNames.GetArrayElementAtIndex(flatIndex);
                if (nameProp != null) customName = nameProp.stringValue;
            }

            if (!string.IsNullOrEmpty(customName)) return customName;

            return JuneHandler.GetJuneToggleTypeLabel(toggleType);
        }

        private string GetAudiolinkDisplayName(SerializedObject handlerObject, int flatIndex)
        {
            SerializedProperty juneAudiolinkControlTypes = handlerObject.FindProperty("juneAudiolinkControlTypes");
            SerializedProperty juneAudiolinkBands = handlerObject.FindProperty("juneAudiolinkBands");
            SerializedProperty juneAudiolinkPowers = handlerObject.FindProperty("juneAudiolinkPowers");

            JuneAudiolinkControlType controlType = JuneAudiolinkControlType.BandToggle;
            if (juneAudiolinkControlTypes != null && flatIndex >= 0 && flatIndex < juneAudiolinkControlTypes.arraySize)
            {
                SerializedProperty prop = juneAudiolinkControlTypes.GetArrayElementAtIndex(flatIndex);
                if (prop != null) controlType = ClampJuneEnumValue<JuneAudiolinkControlType>(prop.enumValueIndex);
            }

            switch (controlType)
            {
                case JuneAudiolinkControlType.BandToggle:
                    return JuneHandler.GetAudiolinkBandLabel(GetAudiolinkConfiguredBand(juneAudiolinkBands, flatIndex));
                case JuneAudiolinkControlType.BandSelector:
                    return "Band Selector";
                case JuneAudiolinkControlType.PowerToggle:
                    return JuneHandler.FormatAudiolinkPowerButtonLabel(GetAudiolinkConfiguredPower(juneAudiolinkPowers, flatIndex));
                case JuneAudiolinkControlType.PowerSelector:
                    return "Power Selector";
                default:
                    return "Audiolink";
            }
        }

        private JuneAudiolinkBand GetAudiolinkConfiguredBand(SerializedProperty juneAudiolinkBands, int index)
        {
            if (juneAudiolinkBands == null || index < 0 || index >= juneAudiolinkBands.arraySize) return JuneAudiolinkBand.Bass;

            SerializedProperty prop = juneAudiolinkBands.GetArrayElementAtIndex(index);
            if (prop == null) return JuneAudiolinkBand.Bass;

            JuneAudiolinkBand configured = ClampJuneEnumValue<JuneAudiolinkBand>(prop.enumValueIndex);
            return configured == JuneAudiolinkBand.Disabled ? JuneAudiolinkBand.Bass : configured;
        }

        private JuneAudiolinkPowerLevel GetAudiolinkConfiguredPower(SerializedProperty juneAudiolinkPowers, int index)
        {
            if (juneAudiolinkPowers == null || index < 0 || index >= juneAudiolinkPowers.arraySize) return JuneAudiolinkPowerLevel.Disabled;

            SerializedProperty prop = juneAudiolinkPowers.GetArrayElementAtIndex(index);
            if (prop == null) return JuneAudiolinkPowerLevel.Disabled;

            return ClampJuneEnumValue<JuneAudiolinkPowerLevel>(prop.enumValueIndex);
        }

        private void EnsureJuneArrayCapacity(SerializedObject handlerObject, int requiredCount)
        {
            if (handlerObject == null) return;

            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");
            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");
            SerializedProperty juneAudiolinkControlTypes = handlerObject.FindProperty("juneAudiolinkControlTypes");
            SerializedProperty juneAudiolinkBands = handlerObject.FindProperty("juneAudiolinkBands");
            SerializedProperty juneAudiolinkPowers = handlerObject.FindProperty("juneAudiolinkPowers");

            EnsureJunePropertyArraySize(juneToggleTypes, requiredCount, prop => prop.enumValueIndex = (int)DefaultJuneToggleType);
            EnsureJunePropertyArraySize(juneToggleNames, requiredCount, prop => prop.stringValue = string.Empty);
            EnsureJunePropertyArraySize(juneAudiolinkControlTypes, requiredCount, prop => prop.enumValueIndex = (int)JuneAudiolinkControlType.BandToggle);
            EnsureJunePropertyArraySize(juneAudiolinkBands, requiredCount, prop => prop.enumValueIndex = (int)JuneAudiolinkBand.Bass);
            EnsureJunePropertyArraySize(juneAudiolinkPowers, requiredCount, prop => prop.enumValueIndex = (int)JuneAudiolinkPowerLevel.Disabled);
        }

        private void EnsureJunePropertyArraySize(SerializedProperty prop, int requiredCount, Action<SerializedProperty> initialize)
        {
            if (prop == null) return;

            while (prop.arraySize < requiredCount)
            {
                prop.InsertArrayElementAtIndex(prop.arraySize);
                SerializedProperty element = prop.GetArrayElementAtIndex(prop.arraySize - 1);
                initialize?.Invoke(element);
            }
        }

        private List<bool> EnsureJuneToggleFoldoutsForFolder(int folderIndex, int requiredCount)
        {
            if (folderIndex < 0) return new List<bool>();

            while (juneToggleFoldoutStatesByFolder.Count <= folderIndex)
            {
                juneToggleFoldoutStatesByFolder.Add(new List<bool>());
            }

            List<bool> foldouts = juneToggleFoldoutStatesByFolder[folderIndex];
            if (foldouts == null)
            {
                foldouts = new List<bool>();
                juneToggleFoldoutStatesByFolder[folderIndex] = foldouts;
            }

            while (foldouts.Count < requiredCount) foldouts.Add(true);
            if (foldouts.Count > requiredCount) foldouts.RemoveRange(requiredCount, foldouts.Count - requiredCount);

            return foldouts;
        }

        private void InsertJuneToggle(SerializedObject handlerObject, int flatIndex)
        {
            if (handlerObject == null) return;

            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");
            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");
            SerializedProperty juneAudiolinkControlTypes = handlerObject.FindProperty("juneAudiolinkControlTypes");
            SerializedProperty juneAudiolinkBands = handlerObject.FindProperty("juneAudiolinkBands");
            SerializedProperty juneAudiolinkPowers = handlerObject.FindProperty("juneAudiolinkPowers");

            InsertJuneElement(juneToggleTypes, flatIndex, prop => prop.enumValueIndex = (int)DefaultJuneToggleType);
            InsertJuneElement(juneToggleNames, flatIndex, prop => prop.stringValue = string.Empty);
            InsertJuneElement(juneAudiolinkControlTypes, flatIndex, prop => prop.enumValueIndex = (int)JuneAudiolinkControlType.BandToggle);
            InsertJuneElement(juneAudiolinkBands, flatIndex, prop => prop.enumValueIndex = (int)JuneAudiolinkBand.Bass);
            InsertJuneElement(juneAudiolinkPowers, flatIndex, prop => prop.enumValueIndex = (int)JuneAudiolinkPowerLevel.Disabled);
        }

        private void InsertJuneElement(SerializedProperty prop, int index, Action<SerializedProperty> initialize)
        {
            if (prop == null) return;

            int safeIndex = Mathf.Clamp(index, 0, prop.arraySize);
            prop.InsertArrayElementAtIndex(safeIndex);
            SerializedProperty element = prop.GetArrayElementAtIndex(safeIndex);
            initialize?.Invoke(element);
        }

        private void DeleteJuneToggle(SerializedObject handlerObject, int flatIndex)
        {
            if (handlerObject == null) return;

            DeleteJuneElement(handlerObject.FindProperty("juneToggleTypes"), flatIndex);
            DeleteJuneElement(handlerObject.FindProperty("juneToggleNames"), flatIndex);
            DeleteJuneElement(handlerObject.FindProperty("juneAudiolinkControlTypes"), flatIndex);
            DeleteJuneElement(handlerObject.FindProperty("juneAudiolinkBands"), flatIndex);
            DeleteJuneElement(handlerObject.FindProperty("juneAudiolinkPowers"), flatIndex);
        }

        private void DeleteJuneElement(SerializedProperty prop, int index)
        {
            if (prop == null || index < 0 || index >= prop.arraySize) return;
            prop.DeleteArrayElementAtIndex(index);
        }

        private void MoveJuneToggle(SerializedObject handlerObject, int fromIndex, int toIndex)
        {
            if (handlerObject == null) return;

            // Move the basic per-toggle arrays
            MoveJuneElement(handlerObject.FindProperty("juneToggleTypes"), fromIndex, toIndex);
            MoveJuneElement(handlerObject.FindProperty("juneToggleNames"), fromIndex, toIndex);
            MoveJuneElement(handlerObject.FindProperty("juneAudiolinkControlTypes"), fromIndex, toIndex);
            MoveJuneElement(handlerObject.FindProperty("juneAudiolinkBands"), fromIndex, toIndex);
            MoveJuneElement(handlerObject.FindProperty("juneAudiolinkPowers"), fromIndex, toIndex);

            // Move the preset property data (flattened arrays)
            MoveJunePresetData(handlerObject, fromIndex, toIndex);
        }

        /// <summary>
        /// Moves preset property data from one toggle index to another.
        /// This handles the flattened arrays by extracting, moving, and reconstructing them.
        /// </summary>
        private void MoveJunePresetData(SerializedObject handlerObject, int fromIndex, int toIndex)
        {
            SerializedProperty startIndices = handlerObject.FindProperty("juneToggleStartIndices");
            SerializedProperty propertyCounts = handlerObject.FindProperty("juneTogglePropertyCounts");
            SerializedProperty propertyNames = handlerObject.FindProperty("juneTogglePropertyNames");
            SerializedProperty floatValues = handlerObject.FindProperty("juneToggleFloatValues");
            SerializedProperty colorValues = handlerObject.FindProperty("juneToggleColorValues");
            SerializedProperty textureValues = handlerObject.FindProperty("juneToggleTextureValues");
            SerializedProperty vectorValues = handlerObject.FindProperty("juneToggleVectorValues");
            SerializedProperty hasTextureValues = handlerObject.FindProperty("juneToggleHasTextureValues");
            SerializedProperty hasVectorValues = handlerObject.FindProperty("juneToggleHasVectorValues");

            // Bail if arrays don't exist or indices are out of range
            if (startIndices == null || propertyCounts == null) return;
            if (fromIndex < 0 || toIndex < 0) return;
            if (fromIndex >= startIndices.arraySize || toIndex >= startIndices.arraySize) return;
            if (fromIndex >= propertyCounts.arraySize || toIndex >= propertyCounts.arraySize) return;
            if (fromIndex == toIndex) return;

            int toggleCount = startIndices.arraySize;

            // Extract all toggle property data as lists
            List<JuneTogglePresetData> allPresets = new List<JuneTogglePresetData>();
            for (int i = 0; i < toggleCount; i++)
            {
                allPresets.Add(ExtractJuneTogglePreset(handlerObject, i, startIndices, propertyCounts, 
                    propertyNames, floatValues, colorValues, textureValues, vectorValues, hasTextureValues, hasVectorValues));
            }

            // Swap the presets
            JuneTogglePresetData temp = allPresets[fromIndex];
            allPresets[fromIndex] = allPresets[toIndex];
            allPresets[toIndex] = temp;

            // Rebuild the flattened arrays from the swapped presets
            RebuildJuneFlattenedArrays(handlerObject, allPresets, startIndices, propertyCounts, 
                propertyNames, floatValues, colorValues, textureValues, vectorValues, hasTextureValues, hasVectorValues);
        }

        /// <summary>
        /// Temporary struct to hold preset data for a single June toggle during swap operations.
        /// </summary>
        private struct JuneTogglePresetData
        {
            public List<string> PropertyNames;
            public List<float> FloatValues;
            public List<Color> ColorValues;
            public List<Texture> TextureValues;
            public List<Vector4> VectorValues;
            public List<bool> HasTextureFlags;
            public List<bool> HasVectorFlags;
        }

        private JuneTogglePresetData ExtractJuneTogglePreset(
            SerializedObject handlerObject, int toggleIndex,
            SerializedProperty startIndices, SerializedProperty propertyCounts,
            SerializedProperty propertyNames, SerializedProperty floatValues, SerializedProperty colorValues,
            SerializedProperty textureValues, SerializedProperty vectorValues,
            SerializedProperty hasTextureValues, SerializedProperty hasVectorValues)
        {
            JuneTogglePresetData data = new JuneTogglePresetData
            {
                PropertyNames = new List<string>(),
                FloatValues = new List<float>(),
                ColorValues = new List<Color>(),
                TextureValues = new List<Texture>(),
                VectorValues = new List<Vector4>(),
                HasTextureFlags = new List<bool>(),
                HasVectorFlags = new List<bool>()
            };

            if (toggleIndex < 0 || toggleIndex >= startIndices.arraySize || toggleIndex >= propertyCounts.arraySize)
            {
                return data;
            }

            int startIdx = startIndices.GetArrayElementAtIndex(toggleIndex).intValue;
            int count = propertyCounts.GetArrayElementAtIndex(toggleIndex).intValue;

            for (int i = 0; i < count; i++)
            {
                int propIdx = startIdx + i;

                // Extract property name
                if (propertyNames != null && propIdx < propertyNames.arraySize)
                {
                    data.PropertyNames.Add(propertyNames.GetArrayElementAtIndex(propIdx).stringValue ?? string.Empty);
                }
                else
                {
                    data.PropertyNames.Add(string.Empty);
                }

                // Extract float value
                if (floatValues != null && propIdx < floatValues.arraySize)
                {
                    data.FloatValues.Add(floatValues.GetArrayElementAtIndex(propIdx).floatValue);
                }
                else
                {
                    data.FloatValues.Add(0f);
                }

                // Extract color value
                if (colorValues != null && propIdx < colorValues.arraySize)
                {
                    data.ColorValues.Add(colorValues.GetArrayElementAtIndex(propIdx).colorValue);
                }
                else
                {
                    data.ColorValues.Add(Color.clear);
                }

                // Extract texture value
                if (textureValues != null && propIdx < textureValues.arraySize)
                {
                    data.TextureValues.Add(textureValues.GetArrayElementAtIndex(propIdx).objectReferenceValue as Texture);
                }
                else
                {
                    data.TextureValues.Add(null);
                }

                // Extract vector value
                if (vectorValues != null && propIdx < vectorValues.arraySize)
                {
                    data.VectorValues.Add(vectorValues.GetArrayElementAtIndex(propIdx).vector4Value);
                }
                else
                {
                    data.VectorValues.Add(Vector4.zero);
                }

                // Extract has-texture flag
                if (hasTextureValues != null && propIdx < hasTextureValues.arraySize)
                {
                    data.HasTextureFlags.Add(hasTextureValues.GetArrayElementAtIndex(propIdx).boolValue);
                }
                else
                {
                    data.HasTextureFlags.Add(false);
                }

                // Extract has-vector flag
                if (hasVectorValues != null && propIdx < hasVectorValues.arraySize)
                {
                    data.HasVectorFlags.Add(hasVectorValues.GetArrayElementAtIndex(propIdx).boolValue);
                }
                else
                {
                    data.HasVectorFlags.Add(false);
                }
            }

            return data;
        }

        private void RebuildJuneFlattenedArrays(
            SerializedObject handlerObject, List<JuneTogglePresetData> allPresets,
            SerializedProperty startIndices, SerializedProperty propertyCounts,
            SerializedProperty propertyNames, SerializedProperty floatValues, SerializedProperty colorValues,
            SerializedProperty textureValues, SerializedProperty vectorValues,
            SerializedProperty hasTextureValues, SerializedProperty hasVectorValues)
        {
            // Calculate total property count
            int totalPropertyCount = 0;
            foreach (var preset in allPresets)
            {
                totalPropertyCount += preset.PropertyNames.Count;
            }

            // Resize flattened arrays
            if (propertyNames != null) propertyNames.arraySize = totalPropertyCount;
            if (floatValues != null) floatValues.arraySize = totalPropertyCount;
            if (colorValues != null) colorValues.arraySize = totalPropertyCount;
            if (textureValues != null) textureValues.arraySize = totalPropertyCount;
            if (vectorValues != null) vectorValues.arraySize = totalPropertyCount;
            if (hasTextureValues != null) hasTextureValues.arraySize = totalPropertyCount;
            if (hasVectorValues != null) hasVectorValues.arraySize = totalPropertyCount;

            // Rebuild the flattened arrays
            int currentIdx = 0;
            for (int toggleIdx = 0; toggleIdx < allPresets.Count; toggleIdx++)
            {
                JuneTogglePresetData preset = allPresets[toggleIdx];
                int count = preset.PropertyNames.Count;

                // Update start index and property count for this toggle
                if (toggleIdx < startIndices.arraySize)
                {
                    startIndices.GetArrayElementAtIndex(toggleIdx).intValue = currentIdx;
                }
                if (toggleIdx < propertyCounts.arraySize)
                {
                    propertyCounts.GetArrayElementAtIndex(toggleIdx).intValue = count;
                }

                // Write property data
                for (int i = 0; i < count; i++)
                {
                    int propIdx = currentIdx + i;

                    if (propertyNames != null && propIdx < propertyNames.arraySize)
                    {
                        propertyNames.GetArrayElementAtIndex(propIdx).stringValue = preset.PropertyNames[i];
                    }
                    if (floatValues != null && propIdx < floatValues.arraySize)
                    {
                        floatValues.GetArrayElementAtIndex(propIdx).floatValue = preset.FloatValues[i];
                    }
                    if (colorValues != null && propIdx < colorValues.arraySize)
                    {
                        colorValues.GetArrayElementAtIndex(propIdx).colorValue = preset.ColorValues[i];
                    }
                    if (textureValues != null && propIdx < textureValues.arraySize)
                    {
                        textureValues.GetArrayElementAtIndex(propIdx).objectReferenceValue = preset.TextureValues[i];
                    }
                    if (vectorValues != null && propIdx < vectorValues.arraySize)
                    {
                        vectorValues.GetArrayElementAtIndex(propIdx).vector4Value = preset.VectorValues[i];
                    }
                    if (hasTextureValues != null && propIdx < hasTextureValues.arraySize)
                    {
                        hasTextureValues.GetArrayElementAtIndex(propIdx).boolValue = preset.HasTextureFlags[i];
                    }
                    if (hasVectorValues != null && propIdx < hasVectorValues.arraySize)
                    {
                        hasVectorValues.GetArrayElementAtIndex(propIdx).boolValue = preset.HasVectorFlags[i];
                    }
                }

                currentIdx += count;
            }
        }

        private void MoveJuneElement(SerializedProperty prop, int from, int to)
        {
            if (prop == null || from < 0 || from >= prop.arraySize || to < 0 || to >= prop.arraySize || from == to) return;
            prop.MoveArrayElement(from, to);
        }

        private void AutoAssignJuneToggleName(SerializedProperty nameProp, JuneToggleType oldType, JuneToggleType newType)
        {
            if (nameProp == null) return;

            if (newType == JuneToggleType.Audiolink)
            {
                nameProp.stringValue = string.Empty;
                return;
            }

            string current = nameProp.stringValue;
            string oldLabel = JuneHandler.GetJuneToggleTypeLabel(oldType);
            if (string.IsNullOrWhiteSpace(current) || string.Equals(current, oldLabel, StringComparison.Ordinal))
            {
                nameProp.stringValue = JuneHandler.GetJuneToggleTypeLabel(newType);
            }
        }

        private string GetExpectedJuneHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"JuneHandler_{folderName}";
        }

        private SerializedProperty GetJuneArrayElement(SerializedProperty array, int index)
        {
            if (array == null || !array.isArray || index < 0 || index >= array.arraySize) return null;
            return array.GetArrayElementAtIndex(index);
        }

        private TEnum ClampJuneEnumValue<TEnum>(int value) where TEnum : Enum
        {
            Array values = Enum.GetValues(typeof(TEnum));
            if (values.Length == 0) return default;
            if (value < 0) return (TEnum)values.GetValue(0);
            if (value >= values.Length) return (TEnum)values.GetValue(values.Length - 1);
            return (TEnum)values.GetValue(value);
        }

        private int ClampJuneEnumIndex<TEnum>(int value) where TEnum : Enum
        {
            Array values = Enum.GetValues(typeof(TEnum));
            if (values.Length == 0) return 0;
            if (value < 0) return 0;
            if (value >= values.Length) return values.Length - 1;
            return value;
        }

        // June toggle options for faders
        private List<ToggleOption> BuildJuneToggleOptions(int folderIndex)
        {
            List<ToggleOption> options = new List<ToggleOption>();

            SerializedObject handlerObject = GetJuneHandlerObjectForFolder(folderIndex);
            if (handlerObject == null) return options;

            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");
            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");

            if (juneToggleNames == null || juneToggleTypes == null) return options;

            int count = juneToggleNames.arraySize;
            for (int i = 0; i < count; i++)
            {
                string label = GetJuneToggleDisplayName(handlerObject, i);
                if (string.IsNullOrEmpty(label)) label = $"Toggle {i + 1}";
                else label = ButtonHandler.FormatName(label);

                options.Add(new ToggleOption { Value = i, Label = label });
            }

            return options;
        }

        // June internal view classes
        private enum JunePropertyKind
        {
            Float,
            Range,
            Color,
            Toggle,
            Enum,
            Texture,
            Vector,
            Int,
        }

        private sealed class JuneCondition
        {
            public List<string> Paths { get; } = new List<string>();
            public List<float> RequiredValues { get; } = new List<float>();
        }

        private sealed class JunePropertyView
        {
            public string name;
            public string displayName;
            public string shaderPropertyName;
            public string rawShaderPropertyName;
            public JunePropertyKind propertyType;
            public float minValue;
            public float maxValue;
            public float defaultValue;
            public Color defaultColor;
            public Vector4 defaultVector;
            public bool hasDefaultColor;
            public bool hasDefaultVector;
            public List<string> enumValues = new List<string>();
            public List<JuneCondition> conditions = new List<JuneCondition>();
            public bool indented;
        }

        private sealed class JuneSectionView
        {
            public string Name;
            public string FoldoutName;
            public int IndentLevel;
            public int DisplayOrder;
            public string FullPath = string.Empty;
            public List<JunePropertyView> Properties = new List<JunePropertyView>();
            public List<JuneSectionView> Children = new List<JuneSectionView>();
        }

        private sealed class JuneModuleView
        {
            public string moduleName;
            public List<JunePropertyView> properties = new List<JunePropertyView>();
            public List<JuneSectionView> sections = new List<JuneSectionView>();
            public Dictionary<string, JunePropertyView> propertyLookup = new Dictionary<string, JunePropertyView>(StringComparer.OrdinalIgnoreCase);
            public HashSet<JunePropertyView> sectionProperties = new HashSet<JunePropertyView>();
            public List<JunePropertyView> ungroupedProperties = new List<JunePropertyView>();
        }
    }
}
#endif

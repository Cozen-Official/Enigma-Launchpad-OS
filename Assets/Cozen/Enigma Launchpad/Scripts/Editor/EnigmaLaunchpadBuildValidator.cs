#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cozen
{
    public sealed class EnigmaLaunchpadBuildValidator : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        
        // Mochie shader names for validation
        private const string MochieStandardShaderName = "Mochie/Screen FX";
        private const string MochieXShaderName = "Mochie/Screen FX X";
        private const string JuneShaderNameFragment = "June";
        
        private static bool s_playModeCallbackRegistered = false;
        
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!s_playModeCallbackRegistered)
            {
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                s_playModeCallbackRegistered = true;
            }
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                try
                {
                    ValidateAndPrepareAll();
                }
                catch (BuildFailedException ex)
                {
                    // Show dialog to user and prevent play mode entry
                    EditorApplication.isPlaying = false;
                    EditorUtility.DisplayDialog("Validation Error", 
                    ex.Message, 
                    "OK");
                }
            }
        }
        
        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateAndPrepareAll();
        }
        
        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            ValidateAndPrepareAll();
        }
        
        private static void ValidateAndPrepareAll()
        {
            var errors = new List<string>();
            
            foreach (var entry in EnumerateLaunchpads())
            {
                ValidateFolderNames(entry.launchpad, entry.context, errors);
                ValidateJuneFolders(entry.launchpad, entry.context, errors);
                ValidateMochieFolders(entry.launchpad, entry.context, errors);
                ValidateFolderRenderers(entry.launchpad, entry.context, errors);

                // Prepare Mochie materials on the renderer before build
                PrepareMochieMaterialOnRenderer(entry.launchpad, entry.context, errors);
                
                // Prepare June material with optimized module keywords
                PrepareJuneMaterialModules(entry.launchpad, entry.context, errors);
            }
            
            if (errors.Count > 0)
            {
                throw new BuildFailedException(string.Join("\n", errors));
            }
        }
        
        /// <summary>
        /// Assigns the appropriate Mochie material to the renderer before builds.
        /// This ensures the material is directly on the renderer so the runtime
        /// can use sharedMaterial without needing to create instances.
        /// Also enables all Mochie shader keywords so shader variants are compiled.
        /// </summary>
        private static void PrepareMochieMaterialOnRenderer(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null)
                return;
                
            MochieHandler handler = launchpad.mochiHandler;
            if (handler == null)
                return;
                
            Renderer shaderRenderer = handler.shaderRenderer;
            if (shaderRenderer == null)
                return;
                
            // Determine which material to use: prefer X if available, otherwise Standard
            Material targetMaterial = null;
            Material xMat = launchpad.mochieMaterialX;
            Material standardMat = launchpad.mochieMaterialStandard;
            
            // Prefer Screen FX X if available and valid
            if (xMat != null && xMat.shader != null && xMat.shader.name == MochieXShaderName)
            {
                targetMaterial = xMat;
            }
            // Otherwise use Screen FX Standard if available and valid
            else if (standardMat != null && standardMat.shader != null && standardMat.shader.name == MochieStandardShaderName)
            {
                targetMaterial = standardMat;
            }
            
            if (targetMaterial == null)
            {
                // No valid material to assign - validation errors will be raised elsewhere
                return;
            }
            
            // Enable all Mochie shader keywords on the material to ensure all shader variants
            // are compiled into the VRChat build. Keywords control which code paths are compiled.
            // If a keyword is disabled at build time, that feature won't work in the built world.
            EnableAllMochieKeywords(targetMaterial);
            
            // Check if the material is already assigned to the renderer
            Material[] currentMaterials = shaderRenderer.sharedMaterials;
            if (currentMaterials != null && currentMaterials.Length > 0)
            {
                // Check if the target material is already in slot 0
                if (currentMaterials[0] == targetMaterial)
                {
                    // Material already assigned, just ensure keywords are enabled
                    return;
                }
                
                // Check if it's a different valid Mochie material (in case user prefers Standard over X)
                if (currentMaterials[0] != null && currentMaterials[0].shader != null)
                {
                    string currentShaderName = currentMaterials[0].shader.name;
                    if (currentShaderName == MochieStandardShaderName || currentShaderName == MochieXShaderName)
                    {
                        // Already has a valid Mochie material, ensure keywords are enabled
                        EnableAllMochieKeywords(currentMaterials[0]);
                        return;
                    }
                }
            }
            
            // Assign the material to the renderer
            Undo.RecordObject(shaderRenderer, "Assign Mochie Material");
            
            Material[] newMaterials;
            if (currentMaterials == null || currentMaterials.Length == 0)
            {
                newMaterials = new Material[] { targetMaterial };
            }
            else
            {
                newMaterials = new Material[currentMaterials.Length];
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    newMaterials[i] = currentMaterials[i];
                }
                newMaterials[0] = targetMaterial;
            }
            
            shaderRenderer.sharedMaterials = newMaterials;
            EditorUtility.SetDirty(shaderRenderer);
            
            Debug.Log($"[EnigmaLaunchpadBuildValidator] Assigned Mochie material '{targetMaterial.name}' to renderer on '{shaderRenderer.gameObject.name}'");
        }
        
        /// <summary>
        /// Enables all Mochie shader keywords on the material to ensure all shader variants
        /// are compiled into the VRChat build. At runtime, effects will be controlled by
        /// property values (strengths set to 0 = no visible effect, even with keyword enabled).
        /// </summary>
        private static void EnableAllMochieKeywords(Material mat)
        {
            if (mat == null) return;
            
            // Record material for undo
            Undo.RecordObject(mat, "Enable Mochie Keywords");
            
            // Enable all Mochie feature keywords so shader variants are compiled
            string[] mochieKeywords = new string[]
            {
                "_SHAKE_ON",
                "_DISTORTION_ON",
                "_BLUR_PIXEL_ON",
                "_NOISE_ON",
                "_COLOR_ON",
                "_SOBEL_FILTER_ON",
                "_OUTLINE_ON",
                "_FOG_ON",
                "_TRIPLANAR_ON",
                "_IMAGE_OVERLAY_ON",
                "_AUDIOLINK_ON"
            };
            
            bool anyChanged = false;
            foreach (string keyword in mochieKeywords)
            {
                if (!mat.IsKeywordEnabled(keyword))
                {
                    mat.EnableKeyword(keyword);
                    anyChanged = true;
                }
            }
            
            if (anyChanged)
            {
                EditorUtility.SetDirty(mat);
                Debug.Log($"[EnigmaLaunchpadBuildValidator] Enabled Mochie shader keywords on material '{mat.name}'");
            }
        }
        
        /// <summary>
        /// Mapping of JuneToggleType to the corresponding shader keyword property name.
        /// These properties control which modules are compiled into the June shader.
        /// </summary>
        private static readonly Dictionary<JuneToggleType, string> JuneModuleKeywordMap = new Dictionary<JuneToggleType, string>
        {
            { JuneToggleType.Blur, "_KeywordBlur" },
            { JuneToggleType.Border, "_KeywordBorder" },
            { JuneToggleType.Chromatic, "_KeywordChromaticAberration" },
            { JuneToggleType.Creativity, "_KeywordCreativity" },
            { JuneToggleType.Grading, "_KeywordColorManipulation" },
            { JuneToggleType.Distortions, "_KeywordDistortions" },
            { JuneToggleType.Enhance, "_KeywordEnhancements" },
            { JuneToggleType.Filters, "_KeywordFilters" },
            { JuneToggleType.Generation, "_KeywordGeneration" },
            { JuneToggleType.Glitch, "_KeywordGlitch" },
            { JuneToggleType.Others, "_KeywordOthers" },
            { JuneToggleType.Outlines, "_KeywordOutlines" },
            { JuneToggleType.Overlay, "_KeywordOverlay" },
            { JuneToggleType.Stylize, "_KeywordStylize" },
            { JuneToggleType.Special, "_KeywordSpecial" },
            { JuneToggleType.Transition, "_KeywordTransition" },
            { JuneToggleType.Triplanar, "_KeywordTriplanar" },
            { JuneToggleType.UV, "_KeywordUVManipulation" },
            { JuneToggleType.Vertex, "_KeywordVertexReconstruction" },
            { JuneToggleType.Zoom, "_KeywordZoom" },
            // Note: Audiolink is not included in this map because it uses separate properties
            // (_AudioLinkBand, _AudioLinkPower, etc.) rather than a single _Keyword* toggle.
            // Audiolink state is handled dynamically at runtime by JuneHandler.
        };
        
        /// <summary>
        /// Mapping of JuneToggleType to the corresponding _LockingModule* property name.
        /// These properties are used by the June shader locking system to determine which
        /// modules should be included in the locked shader build.
        /// </summary>
        private static readonly Dictionary<JuneToggleType, string> JuneLockingModuleMap = new Dictionary<JuneToggleType, string>
        {
            { JuneToggleType.Blur, "_LockingModuleBlur" },
            { JuneToggleType.Border, "_LockingModuleBorder" },
            { JuneToggleType.Chromatic, "_LockingModuleChromaticaberration" },
            { JuneToggleType.Creativity, "_LockingModuleCreativity" },
            { JuneToggleType.Grading, "_LockingModuleColormanipulation" },
            { JuneToggleType.Distortions, "_LockingModuleDistortions" },
            { JuneToggleType.Enhance, "_LockingModuleEnhancements" },
            { JuneToggleType.Filters, "_LockingModuleFilters" },
            { JuneToggleType.Generation, "_LockingModuleGeneration" },
            { JuneToggleType.Glitch, "_LockingModuleGlitch" },
            { JuneToggleType.Others, "_LockingModuleOthers" },
            { JuneToggleType.Outlines, "_LockingModuleOutlines" },
            { JuneToggleType.Overlay, "_LockingModuleOverlay" },
            { JuneToggleType.Stylize, "_LockingModuleStylize" },
            { JuneToggleType.Special, "_LockingModuleSpecial" },
            { JuneToggleType.Transition, "_LockingModuleTransition" },
            { JuneToggleType.Triplanar, "_LockingModuleTriplanar" },
            { JuneToggleType.UV, "_LockingModuleUvmanipulation" },
            { JuneToggleType.Vertex, "_LockingModuleVertexreconstruction" },
            { JuneToggleType.Zoom, "_LockingModuleZoom" },
        };
        
        /// <summary>
        /// Prepares the June material by enabling module keywords and setting locking properties
        /// for all modules and sub-effects that are used by the configured JuneHandler toggles.
        /// This ensures the shader is properly configured for locking and runtime use.
        /// </summary>
        private static void PrepareJuneMaterialModules(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null)
                return;
                
            Material juneMaterial = launchpad.juneMaterial;
            if (juneMaterial == null)
                return;
                
            JuneHandler[] handlers = launchpad.juneHandlers;
            if (handlers == null || handlers.Length == 0)
                return;
                
            // Collect all toggle types and their names used across all JuneHandlers
            var usedToggleTypes = new HashSet<JuneToggleType>();
            var usedToggleNames = new HashSet<string>();
            
            foreach (JuneHandler handler in handlers)
            {
                if (handler == null)
                    continue;
                    
                JuneToggleType[] toggleTypes = handler.juneToggleTypes;
                string[] toggleNames = handler.juneToggleNames;
                
                if (toggleTypes == null)
                    continue;
                    
                for (int i = 0; i < toggleTypes.Length; i++)
                {
                    usedToggleTypes.Add(toggleTypes[i]);
                    
                    // Collect toggle names for sub-effect locking
                    if (toggleNames != null && i < toggleNames.Length && !string.IsNullOrEmpty(toggleNames[i]))
                    {
                        usedToggleNames.Add(toggleNames[i]);
                    }
                }
            }
            
            // Record material for undo
            Undo.RecordObject(juneMaterial, "Configure June Module Keywords and Locking");
            
            bool anyChanged = false;
            var enabledModules = new List<string>();
            var disabledModules = new List<string>();
            
            // Step 1: Configure module keywords (_Keyword*)
            foreach (var kvp in JuneModuleKeywordMap)
            {
                JuneToggleType toggleType = kvp.Key;
                string keywordProperty = kvp.Value;
                
                if (!juneMaterial.HasProperty(keywordProperty))
                    continue;
                    
                bool shouldEnable = usedToggleTypes.Contains(toggleType);
                float currentValue = juneMaterial.GetFloat(keywordProperty);
                float targetValue = shouldEnable ? 1f : 0f;
                
                if (!Mathf.Approximately(currentValue, targetValue))
                {
                    juneMaterial.SetFloat(keywordProperty, targetValue);
                    anyChanged = true;
                }
                
                if (shouldEnable)
                {
                    enabledModules.Add(toggleType.ToString());
                }
                else
                {
                    disabledModules.Add(toggleType.ToString());
                }
            }
            
            // Step 2: Configure module locking properties (_LockingModule*)
            // First, reset ALL locking module properties to 0 to ensure clean state
            // This prevents unused modules (like Frames) from being included in the locked shader
            var allLockingModuleProperties = new string[]
            {
                "_LockingModuleConditional",
                "_LockingModuleBlur",
                "_LockingModuleBorder",
                "_LockingModuleChromaticaberration",
                "_LockingModuleColormanipulation",
                "_LockingModuleCreativity",
                "_LockingModuleDistortions",
                "_LockingModuleEnhancements",
                "_LockingModuleExperiments",
                "_LockingModuleFilters",
                "_LockingModuleFrames",
                "_LockingModuleGlitch",
                "_LockingModuleGeneration",
                "_LockingModuleMotion",
                "_LockingModuleOthers",
                "_LockingModuleOutlines",
                "_LockingModuleOverlay",
                "_LockingModuleStylize",
                "_LockingModuleSpecial",
                "_LockingModuleTransition",
                "_LockingModuleTriplanar",
                "_LockingModuleUvmanipulation",
                "_LockingModuleVertexreconstruction",
                "_LockingModuleZoom"
            };
            
            foreach (string lockingProp in allLockingModuleProperties)
            {
                if (juneMaterial.HasProperty(lockingProp))
                {
                    float currentValue = juneMaterial.GetFloat(lockingProp);
                    if (!Mathf.Approximately(currentValue, 0f))
                    {
                        juneMaterial.SetFloat(lockingProp, 0f);
                        anyChanged = true;
                    }
                }
            }
            
            // Now set only the used modules to 1
            var lockedModules = new List<string>();
            foreach (var kvp in JuneLockingModuleMap)
            {
                JuneToggleType toggleType = kvp.Key;
                string lockingProperty = kvp.Value;
                
                if (!juneMaterial.HasProperty(lockingProperty))
                    continue;
                    
                bool shouldLock = usedToggleTypes.Contains(toggleType);
                if (shouldLock)
                {
                    float currentValue = juneMaterial.GetFloat(lockingProperty);
                    if (!Mathf.Approximately(currentValue, 1f))
                    {
                        juneMaterial.SetFloat(lockingProperty, 1f);
                        anyChanged = true;
                    }
                    lockedModules.Add(toggleType.ToString());
                }
            }
            
            // Step 3: Configure sub-effect locking properties (_Locking*)
            // For each toggle name, enable its corresponding _Locking* property
            var lockedEffects = new List<string>();
            foreach (string toggleName in usedToggleNames)
            {
                // Convert toggle name to locking property format
                // Examples: "Crt" -> "_LockingCrt", "Film Grain" -> "_LockingFilmgrain"
                string lockingPropertyName = ConvertToggleNameToLockingProperty(toggleName);
                
                if (!juneMaterial.HasProperty(lockingPropertyName))
                    continue;
                
                float currentValue = juneMaterial.GetFloat(lockingPropertyName);
                if (!Mathf.Approximately(currentValue, 1f))
                {
                    juneMaterial.SetFloat(lockingPropertyName, 1f);
                    anyChanged = true;
                    lockedEffects.Add(toggleName);
                }
            }
            
            if (anyChanged)
            {
                EditorUtility.SetDirty(juneMaterial);
                
                string enabledList = enabledModules.Count > 0 
                    ? string.Join(", ", enabledModules) 
                    : "none";
                string disabledList = disabledModules.Count > 0 
                    ? string.Join(", ", disabledModules) 
                    : "none";
                string lockedList = lockedModules.Count > 0 
                    ? string.Join(", ", lockedModules) 
                    : "none";
                string effectsList = lockedEffects.Count > 0 
                    ? string.Join(", ", lockedEffects) 
                    : "none";
                    
                Debug.Log($"[EnigmaLaunchpadBuildValidator] Configured June material '{juneMaterial.name}':\n" +
                          $"  Module Keywords Enabled: [{enabledList}]\n" +
                          $"  Module Keywords Disabled: [{disabledList}]\n" +
                          $"  Modules Locked: [{lockedList}]\n" +
                          $"  Sub-Effects Locked: [{effectsList}]");
            }
        }
        
        /// <summary>
        /// Converts a June toggle name to its corresponding _Locking* property name.
        /// Examples: "Crt" -> "_LockingCrt", "Film Grain" -> "_LockingFilmgrain"
        /// </summary>
        private static string ConvertToggleNameToLockingProperty(string toggleName)
        {
            if (string.IsNullOrEmpty(toggleName))
                return string.Empty;
            
            // Remove spaces and special characters, convert to lowercase (except first letter)
            string cleaned = toggleName.Replace(" ", "").Replace("-", "").Trim();
            
            // Capitalize first letter
            if (cleaned.Length > 0)
            {
                cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1).ToLower();
            }
            
            return "_Locking" + cleaned;
        }
        
        /// <summary>
        /// Public method to prepare a single June material for locking.
        /// Called from the editor UI to manually prepare and verify locking properties.
        /// </summary>
        public static void PrepareJuneMaterialForLocking(EnigmaLaunchpad launchpad, Material juneMaterial, List<string> errors)
        {
            if (launchpad == null || juneMaterial == null)
            {
                if (errors != null)
                {
                    errors.Add("Invalid launchpad or material reference");
                }
                return;
            }
            
            // Verify this is the material used by the launchpad
            if (launchpad.juneMaterial != juneMaterial)
            {
                if (errors != null)
                {
                    errors.Add("The provided material is not the June material assigned to this launchpad");
                }
                return;
            }
            
            // Use the same preparation logic as the build validator
            PrepareJuneMaterialModules(launchpad, null, errors ?? new List<string>());
        }
        
        private static IEnumerable<(EnigmaLaunchpad launchpad, string context)> EnumerateLaunchpads()
        {
            var processed = new HashSet<int>();
            
            foreach (var launchpad in Resources.FindObjectsOfTypeAll<EnigmaLaunchpad>())
            {
                if (launchpad == null)
                    continue;

                GameObject go = launchpad.gameObject;
                if (IsPrefabAssetOrStageObject(go))
                    continue;

                if (!IsSceneObject(go))
                    continue;

                if (GameObjectIsEditorOnly(go))
                    continue;

                int id = launchpad.GetInstanceID();
                if (!processed.Add(id))
                    continue;

                yield return (launchpad, BuildContextLabel(launchpad, null));
            }

        }
        
        private static bool IsPrefabAssetOrStageObject(GameObject go)
        {
            if (go == null)
                return false;

            if (EditorUtility.IsPersistent(go))
                return true;

            PrefabStage stage = PrefabStageUtility.GetPrefabStage(go);
            return stage != null && stage.IsPartOfPrefabContents(go);
        }

        private static void ValidateFolderNames(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null || errors == null)
            return;
            
            string[] names = launchpad.folderNames;
            ToggleFolderType[] types = launchpad.folderTypes;
            if (names == null || names.Length == 0)
            return;
            
            var usageMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            
            for (int i = 0; i < names.Length; i++)
            {
                ToggleFolderType type = (types != null && types.Length > i) ? types[i] : ToggleFolderType.Objects;
                string normalized = string.IsNullOrWhiteSpace(names[i])
                ? GetAutoFolderName(type)
                : names[i].Trim();
                if (!usageMap.TryGetValue(normalized, out var indices))
                {
                    indices = new List<int>();
                    usageMap.Add(normalized, indices);
                }
                indices.Add(i);
            }

            string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
            foreach (var pair in usageMap)
            {
                if (pair.Value.Count > 1)
                {
                    pair.Value.Sort();
                    string indicesStr = string.Join(", ", pair.Value.ConvertAll(idx => (idx + 1).ToString()));
                    errors.Add($"Duplicate folder name '{pair.Key}' found in {location} at entries {indicesStr}. Please give each folder a unique name.");
                }
            }
        }

        private static void ValidateFolderRenderers(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null || errors == null)
                return;

            string location = string.IsNullOrEmpty(context) ? launchpad.name : context;

            ValidateMaterialFolderRenderers(launchpad, location, errors);
            ValidatePropertyFolderRenderers(launchpad, location, errors);
            ValidateJuneFolderRenderers(launchpad, location, errors);
            ValidateMochieFolderRenderers(launchpad, location, errors);
        }

        private static void ValidateMaterialFolderRenderers(EnigmaLaunchpad launchpad, string location, List<string> errors)
        {
            MaterialHandler[] handlers = launchpad.materialHandlers;
            if (handlers == null || handlers.Length == 0)
                return;

            foreach (MaterialHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                int folderIndex = handler.folderIndex;
                string folderLabel = BuildFolderDescription(launchpad, folderIndex, ToggleFolderType.Materials);
                Renderer[] renderers = ResolveMaterialRenderers(handler, folderIndex);
                ValidateRendererArray(renderers, folderLabel, location, errors);
            }
        }

        private static Renderer[] ResolveMaterialRenderers(MaterialHandler handler, int folderIndex)
        {
            if (handler == null)
                return Array.Empty<Renderer>();

            Renderer[] folderRenderers = handler.folderMaterialRenderers;
            if (folderRenderers != null && folderRenderers.Length > 0)
                return folderRenderers;

            Renderer[] materialRenderers = handler.materialRenderers;
            if (materialRenderers == null || materialRenderers.Length == 0)
                return Array.Empty<Renderer>();

            int[] rendererIndices = handler.materialRendererIndices;
            int rendererIndex = (rendererIndices != null && folderIndex >= 0 && folderIndex < rendererIndices.Length)
                ? rendererIndices[folderIndex]
                : -1;

            if (rendererIndex < 0 || rendererIndex >= materialRenderers.Length)
                return Array.Empty<Renderer>();

            return new Renderer[] { materialRenderers[rendererIndex] };
        }

        private static void ValidatePropertyFolderRenderers(EnigmaLaunchpad launchpad, string location, List<string> errors)
        {
            PropertyHandler[] handlers = launchpad.propertyHandlers;
            if (handlers == null || handlers.Length == 0)
                return;

            foreach (PropertyHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                int folderIndex = handler.folderIndex;
                string folderLabel = BuildFolderDescription(launchpad, folderIndex, ToggleFolderType.Properties);
                ValidateRendererArray(handler.propertyRenderers, folderLabel, location, errors);
            }
        }

        private static void ValidateJuneFolderRenderers(EnigmaLaunchpad launchpad, string location, List<string> errors)
        {
            JuneHandler[] handlers = launchpad.juneHandlers;
            if (handlers == null || handlers.Length == 0)
                return;

            foreach (JuneHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                string folderLabel = BuildFolderDescription(launchpad, handler.folderIndex, ToggleFolderType.June);
                ValidateRendererArray(WrapRenderer(handler.juneRenderer), folderLabel, location, errors);
            }
        }

        private static void ValidateMochieFolderRenderers(EnigmaLaunchpad launchpad, string location, List<string> errors)
        {
            MochieHandler handler = launchpad.mochiHandler;
            if (handler == null)
                return;

            string folderLabel = BuildFolderDescription(launchpad, handler.folderIndex, ToggleFolderType.Mochie);
            ValidateRendererArray(WrapRenderer(handler.shaderRenderer), folderLabel, location, errors);
        }

        private static Renderer[] WrapRenderer(Renderer renderer)
        {
            if (renderer == null)
                return Array.Empty<Renderer>();

            return new Renderer[] { renderer };
        }

        private static void ValidateRendererArray(Renderer[] renderers, string folderLabel, string location, List<string> errors)
        {
            if (renderers == null || renderers.Length == 0)
                return;

            var seen = new HashSet<int>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                int id = renderer.GetInstanceID();
                if (!seen.Add(id))
                    continue;

                if (RendererIsEditorOnly(renderer))
                {
                    string goName = renderer.gameObject != null ? renderer.gameObject.name : renderer.name;
                    errors.Add($"{folderLabel} in {location} references renderer '{renderer.name}' on '{goName}', but it is tagged 'EditorOnly' and will be stripped from builds. Assign a renderer included in builds so this folder's effects remain visible at runtime.");
                }
            }
        }

        private static bool RendererIsEditorOnly(Renderer renderer)
        {
            if (renderer == null)
                return false;

            GameObject go = renderer.gameObject;
            return GameObjectIsEditorOnly(go);
        }

        private static bool GameObjectIsEditorOnly(GameObject go)
        {
            return go != null && go.CompareTag("EditorOnly");
        }

        private static bool IsSceneObject(GameObject go)
        {
            if (go == null)
                return false;

            var scene = go.scene;
            return scene.IsValid() && !string.IsNullOrEmpty(scene.path);
        }

        private static string BuildFolderDescription(EnigmaLaunchpad launchpad, int folderIndex, ToggleFolderType folderType)
        {
            ToggleFolderType[] types = launchpad != null ? launchpad.folderTypes : null;
            if (types != null && folderIndex >= 0 && folderIndex < types.Length)
            {
                folderType = types[folderIndex];
            }

            string folderName = launchpad != null ? launchpad.GetFolderLabelForIndex(folderIndex, folderType == ToggleFolderType.Skybox) : null;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = GetAutoFolderName(folderType);
            }

            if (folderIndex >= 0)
            {
                return $"{folderType} folder '{folderName}' (index {folderIndex + 1})";
            }

            return $"{folderType} folder '{folderName}'";
        }

        private static void ValidateMochieFolders(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null || errors == null)
            return;
            
            ToggleFolderType[] types = launchpad.folderTypes;
            if (types == null || types.Length == 0)
            return;
            
            // Check if there's any Mochie folder configured
            bool hasMochieFolder = false;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == ToggleFolderType.Mochie)
                {
                    hasMochieFolder = true;
                    break;
                }
            }
            
            if (!hasMochieFolder)
            return;
            
            // Validate that Mochie handler and materials are set up
            MochieHandler handler = launchpad.mochiHandler;
            if (handler == null)
            {
                string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                errors.Add($"Mochie folder is configured in {location} but MochieHandler is not assigned. Please assign a MochieHandler in the inspector.");
                return;
            }
            
            // Check renderer
            Renderer shaderRenderer = handler.shaderRenderer;
            if (shaderRenderer == null)
            {
                string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                errors.Add($"Mochie folder is configured in {location} but Shader Renderer is not assigned in MochieHandler. Please assign a renderer in the inspector.");
            }
            
            // Check that at least one material is assigned
            Material standardMat = launchpad.mochieMaterialStandard;
            Material xMat = launchpad.mochieMaterialX;
            
            if (standardMat == null && xMat == null)
            {
                string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                errors.Add($"Mochie folder is configured in {location} but no Mochie materials are assigned. Please assign at least one Mochie material (Standard or X) in the inspector under 'Internal References'.");
            }
            else
            {
                // Validate that assigned materials use the correct shaders
                bool hasValidStandardMaterial = false;
                bool hasValidXMaterial = false;
                
                if (standardMat != null)
                {
                    if (standardMat.shader == null)
                    {
                        string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                        errors.Add($"Mochie folder is configured in {location} but the assigned Standard material has no shader.");
                    }
                    else
                    {
                        string shaderName = standardMat.shader.name;
                        // Standard material should use exactly "Mochie/Screen FX" (not the X variant)
                        if (shaderName == MochieXShaderName)
                        {
                            string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                            errors.Add($"Mochie folder is configured in {location} but the assigned Standard material uses '{MochieXShaderName}' shader. It should use '{MochieStandardShaderName}' (without X) instead.");
                        }
                        else if (shaderName != MochieStandardShaderName)
                        {
                            string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                            errors.Add($"Mochie folder is configured in {location} but the assigned Standard material does not use a valid Mochie shader. Expected '{MochieStandardShaderName}' shader, but found '{shaderName}'.");
                        }
                        else
                        {
                            hasValidStandardMaterial = true;
                        }
                    }
                }
                
                if (xMat != null)
                {
                    if (xMat.shader == null)
                    {
                        string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                        errors.Add($"Mochie folder is configured in {location} but the assigned X material has no shader.");
                    }
                    else if (xMat.shader.name != MochieXShaderName)
                    {
                        string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                        errors.Add($"Mochie folder is configured in {location} but the assigned X material does not use the '{MochieXShaderName}' shader. Found '{xMat.shader.name}'.");
                    }
                    else
                    {
                        hasValidXMaterial = true;
                    }
                }
                
                // Ensure at least one material has a valid shader
                if (!hasValidStandardMaterial && !hasValidXMaterial)
                {
                    string location = string.IsNullOrEmpty(context) ? launchpad.name : context;
                    errors.Add($"Mochie folder is configured in {location} but no valid Mochie shaders found on the assigned materials. At least one material must use '{MochieStandardShaderName}' or '{MochieXShaderName}' shader.");
                }
            }
        }

        private static void ValidateJuneFolders(EnigmaLaunchpad launchpad, string context, List<string> errors)
        {
            if (launchpad == null || errors == null)
            return;

            ToggleFolderType[] types = launchpad.folderTypes;
            if (types == null || types.Length == 0)
            return;

            bool hasJuneFolder = false;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == ToggleFolderType.June)
                {
                    hasJuneFolder = true;
                    break;
                }
            }

            if (!hasJuneFolder)
            return;

            string location = string.IsNullOrEmpty(context) ? launchpad.name : context;

            Material juneMaterial = launchpad.juneMaterial;
            if (juneMaterial == null)
            {
                errors.Add($"June folder is configured in {location} but June Material is not assigned. Please assign a June material in the inspector.");
            }
            else if (juneMaterial.shader == null)
            {
                errors.Add($"June folder is configured in {location} but the assigned June material has no shader. Import June shaders or select a valid material.");
            }
            else if (juneMaterial.shader.name.IndexOf(JuneShaderNameFragment, StringComparison.OrdinalIgnoreCase) < 0)
            {
                errors.Add($"June folder is configured in {location} but the assigned June material does not use a June shader. Found '{juneMaterial.shader.name}'.");
            }

            JuneHandler[] handlers = launchpad.juneHandlers;
            if (handlers == null || handlers.Length == 0)
            {
                errors.Add($"June folder is configured in {location} but no JuneHandler is assigned. Reselect the Launchpad to regenerate June handlers.");
                return;
            }

            for (int i = 0; i < handlers.Length; i++)
            {
                JuneHandler handler = handlers[i];
                if (handler == null)
                {
                    errors.Add($"June folder is configured in {location} but a JuneHandler reference is missing. Reselect the Launchpad to regenerate June handlers.");
                    continue;
                }

                if (handler.juneRenderer == null)
                {
                    errors.Add($"June folder is configured in {location} but JuneHandler '{handler.name}' has no Target Renderer assigned. Please assign a renderer in the June folder configuration.");
                }
            }
        }
        
        private static string GetAutoFolderName(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Materials:
                return "Materials";
                case ToggleFolderType.Skybox:
                return "Skybox";
                case ToggleFolderType.Mochie:
                return "Mochie";
                case ToggleFolderType.June:
                return "June";
                case ToggleFolderType.Stats:
                return "Stats";
                case ToggleFolderType.Objects:
                default:
                return "Objects";
            }
        }
        
        private static string BuildContextLabel(EnigmaLaunchpad launchpad, string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            return explicitPath;
            
            if (launchpad == null)
            return "EnigmaLaunchpad";
            
            var go = launchpad.gameObject;
            if (go != null)
            {
                if (go.scene.IsValid() && !string.IsNullOrEmpty(go.scene.path))
                {
                    return $"{go.scene.path} -> {GetHierarchyPath(go)}";
                }
                
                string assetPath = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(assetPath))
                return assetPath;
            }
            
            string componentPath = AssetDatabase.GetAssetPath(launchpad);
            if (!string.IsNullOrEmpty(componentPath))
            return componentPath;
            
            return launchpad.name;
        }
        
        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            return string.Empty;
            
            var segments = new Stack<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", segments);
        }
    }
}
#endif

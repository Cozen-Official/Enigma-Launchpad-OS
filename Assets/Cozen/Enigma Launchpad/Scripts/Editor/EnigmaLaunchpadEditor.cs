#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using VRC.SDKBase;
using UdonSharp;
using AudioLink;

// Conditional imports for optional video player packages
#if ARCHIT_PROTV
using ArchiTech.ProTV;
#endif
#if TEXEL_VIDEO
using Texel;
#endif

namespace Cozen
{
    [CustomEditor(typeof(EnigmaLaunchpad))]
    public partial class EnigmaLaunchpadEditor : Editor
    {
        [InitializeOnLoadMethod]
        private static void UpdatePackageDefines()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var defines = currentDefines.Split(';').ToList();
            
            bool modified = false;
            
            // Check for ProTV
            bool hasProTV = System.Type.GetType("ArchiTech.ProTV.MediaControls, ArchiTech.ProTV.Runtime") != null;
            modified |= UpdateDefine(defines, "ARCHIT_PROTV", hasProTV);
            
            // Check for VideoTXL
            bool hasVideoTXL = System.Type.GetType("Texel.PlayerControls, com.texelsaur.video") != null;
            modified |= UpdateDefine(defines, "TEXEL_VIDEO", hasVideoTXL);
            
            if (modified)
            {
                string newDefines = string.Join(";", defines.Where(d => !string.IsNullOrEmpty(d)));
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);
            }
        }
        
        private static bool UpdateDefine(System.Collections.Generic.List<string> defines, string define, bool shouldExist)
        {
            bool exists = defines.Contains(define);
            
            if (shouldExist && !exists)
            {
                defines.Add(define);
                return true;
            }
            else if (!shouldExist && exists)
            {
                defines.Remove(define);
                return true;
            }
            
            return false;
        }
        
        // Serialized references
        private SerializedObject so;
        private const HideFlags HandlerHideFlags = HideFlags.None;
        private SerializedProperty whitelistEnabled;
        private SerializedProperty instanceOwnerAlwaysHasAccess;
        private SerializedProperty ohGeezCmonAccessControl;
        private SerializedProperty authorizedUsernames;
        private SerializedProperty buttonHandlers;
        private SerializedProperty itemsPerPage;
        private SerializedProperty defaultFolderIndexProperty;
        private SerializedProperty activeColorProperty;
        private SerializedProperty inactiveColorProperty;
        
        private SerializedProperty folderNamesProperty;
        private SerializedProperty folderExclusiveProperty;
        private SerializedProperty folderEntryCountsProperty;
        private SerializedProperty folderTypesProperty;
        private SerializedProperty mochieMaterialStandardProperty;
        private SerializedProperty mochieMaterialXProperty;
        private SerializedProperty juneMaterialProperty;
        private SerializedProperty screenHandlerProperty;
        private SerializedProperty autoLinkComponentProperty;
        private SerializedProperty videoScreenMaterialProperty;
        private SerializedProperty videoPlayerControlsModeProperty;
        private SerializedProperty proTVMediaControlsProperty;
        private SerializedProperty videoTXLPlayerControlsProperty;
        
        // Foldout keys
        private const string F_Global    = "Settings";
        private const string F_Folders     = "Folders";
        private const string F_Whitelist = "Whitelist";
        private const string F_Internal  = "Internal References";

        private const string FoldoutStateSessionKey = "Cozen.EnigmaLaunchpadEditor.Foldouts";
        
        // Foldout states
        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
        private bool[] folderFoldouts;
        // Visual constants
        private const float SectionVerticalSpacing   = 12f;
        private const float SectionBetweenSpacing    = 6f;
        private const float SectionHorizontalPadding = 12f;
        private const float SectionHeaderHeight      = 24f;
        private const float SectionCornerRadius      = 10f;
        private const float SectionBorderWidth       = 2f;
        private const float DropZoneHeight           = 48f;

        private const float BorderPadding = 6f;
        private const float InnerContentVerticalPad = 3f;
        private const float FolderInnerContentPad = 2f;

        private Texture2D logoTexture;
        
        private GUIStyle headerTitleStyle;
        private GUIStyle headerSubtitleStyle;
        private GUIStyle foldoutLabelStyle;
        private GUIStyle folderHeaderLabelStyle;
        private GUIStyle folderHeaderFoldoutStyle;
        private GUIStyle dragZoneStyle;
        private bool stylesReady = false;
        
        // Version checking
        private const string VersionFilePath = "Assets/Cozen/Enigma Launchpad/VERSION";
        private const string RemoteVersionUrl = "https://raw.githubusercontent.com/Cozen-Official/Enigma-Launchpad-OS/refs/heads/main/Assets/Cozen/Enigma%20Launchpad/VERSION";
        private const string RepoUrl = "https://github.com/Cozen-Official/Enigma-Launchpad-OS";
        private string localVersion;
        private string remoteVersion;
        private bool versionCheckInProgress = false;
        private bool versionCheckComplete = false;
        private bool updateAvailable = false;
        private UnityEditor.Networking.UnityWebRequest versionCheckRequest;
        
        private struct DuplicateMessage
        {
            public readonly string message;
            public readonly MessageType type;
            
            public DuplicateMessage(string message, MessageType type)
            {
                this.message = message;
                this.type = type;
            }
        }
        
        [Serializable]
        private class FoldoutStateSnapshot
        {
            public bool preview;
            public bool global;
            public bool folders;
            public bool faders;
            public bool whitelist;
            public bool internalRefs;
            public bool[] folderStates;
            public bool mochieConfiguration;
            public bool mochieOutlineList;
            public bool mochieOverlayList;
            public bool statsAdvanced;
            public int previewFolderIndex;
            public int previewPageIndex;
        }
        
        void OnEnable()
        {
            so = serializedObject;
            so.Update();

            EnsureDuplicateFaderButtonContent();

            skyboxHandlerProperty   = so.FindProperty("skyboxHandler");
            statsHandlerProperty    = so.FindProperty("statsHandler");
            presetHandlerProperty   = so.FindProperty("presetHandler");
            mochiHandlerProperty    = so.FindProperty("mochiHandler");
            juneHandlers            = so.FindProperty("juneHandlers");
            faderHandlerProperty    = so.FindProperty("faderHandler");
            objectHandlers          = so.FindProperty("objectHandlers");
            materialHandlers        = so.FindProperty("materialHandlers");
            propertyHandlers        = so.FindProperty("propertyHandlers");
            shaderHandlers          = so.FindProperty("shaderHandlers");
            BindSkyboxHandlerSerializedObject();
            BindStatsHandlerSerializedObject();
            BindPresetHandlerSerializedObject();
            BindMochieHandlerSerializedObject();
            BindFaderHandlerSerializedObject();
            buttonHandlers          = so.FindProperty("buttonHandlers");
            itemsPerPage            = so.FindProperty("itemsPerPage");
            defaultFolderIndexProperty             = so.FindProperty("defaultFolderIndex");
            activeColorProperty     = so.FindProperty("activeColor");
            inactiveColorProperty   = so.FindProperty("inactiveColor");
            whitelistEnabled        = so.FindProperty("whitelistEnabled");
            instanceOwnerAlwaysHasAccess = so.FindProperty("instanceOwnerAlwaysHasAccess");
            ohGeezCmonAccessControl = so.FindProperty("ohGeezCmonAccessControl");
            authorizedUsernames     = so.FindProperty("authorizedUsernames");
            mochieMaterialStandardProperty = so.FindProperty("mochieMaterialStandard");
            mochieMaterialXProperty = so.FindProperty("mochieMaterialX");
            juneMaterialProperty    = so.FindProperty("juneMaterial");
            screenHandlerProperty   = so.FindProperty("screenHandler");
            autoLinkComponentProperty = so.FindProperty("autoLinkComponent");
            videoScreenMaterialProperty = so.FindProperty("videoScreenMaterial");
            videoPlayerControlsModeProperty = so.FindProperty("videoPlayerControlsMode");
            proTVMediaControlsProperty = so.FindProperty("proTVMediaControls");
            videoTXLPlayerControlsProperty = so.FindProperty("videoTXLPlayerControls");
            
            folderNamesProperty              = so.FindProperty("folderNames");
            folderExclusiveProperty          = so.FindProperty("folderExclusive");
            folderEntryCountsProperty       = so.FindProperty("folderEntryCounts");
            folderTypesProperty        = so.FindProperty("folderTypes");
            
            EnsureOutlineColorArrayParity();
            EnsureAutoGeneratedNameFlags();
            BuildOutlineColorEntriesList();
            EnsureOverlayArrayParity();
            EnsureAutoGeneratedOverlayNameFlags();
            BuildOverlayEntriesList();
            
            CacheMochieShaderAvailability();

            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Cozen/Enigma Launchpad/Textures/enigma white.png");

            LoadPersistedFoldoutStates();
            EnsureFoldoutDefaults();
            EnsurePreviewFoldoutDefault();
            EnsureFolderArrayParity();
            RefreshFolderFoldouts();
            LoadPreviewState();
            
            // Load version and check for updates
            LoadLocalVersion();
            CheckForUpdates();
        }

        private void OnDisable()
        {
            SavePersistedFoldoutStates();
            
            // Clean up version check request if still in progress
            if (versionCheckRequest != null && !versionCheckRequest.isDone)
            {
                versionCheckRequest.Abort();
                versionCheckRequest.Dispose();
                versionCheckRequest = null;
            }
        }

        private string GetFoldoutStateKey()
        {
            int instanceId = target != null ? target.GetInstanceID() : 0;
            return $"{FoldoutStateSessionKey}.{instanceId}";
        }

        private void LoadPersistedFoldoutStates()
        {
            string json = SessionState.GetString(GetFoldoutStateKey(), string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            FoldoutStateSnapshot snapshot = JsonUtility.FromJson<FoldoutStateSnapshot>(json);
            if (snapshot == null)
            {
                return;
            }

            foldouts[F_Preview] = snapshot.preview;
            foldouts[F_Global] = snapshot.global;
            foldouts[F_Folders] = snapshot.folders;
            foldouts[F_Faders] = snapshot.faders;
            foldouts[F_Whitelist] = snapshot.whitelist;
            foldouts[F_Internal] = snapshot.internalRefs;

            if (snapshot.folderStates != null)
            {
                folderFoldouts = snapshot.folderStates;
            }

            mochieConfigurationExpanded = snapshot.mochieConfiguration;
            mochieOutlineListExpanded = snapshot.mochieOutlineList;
            mochieOverlayListExpanded = snapshot.mochieOverlayList;
            statsAdvancedFoldout = snapshot.statsAdvanced;
            previewFolderIndex = snapshot.previewFolderIndex;
            previewPageIndex = snapshot.previewPageIndex;
        }

        private void PersistFoldoutState(string key, bool value)
        {
            foldouts[key] = value;
            SavePersistedFoldoutStates();
        }

        private void SavePersistedFoldoutStates()
        {
            var snapshot = new FoldoutStateSnapshot
            {
                preview = foldouts.TryGetValue(F_Preview, out bool preview) ? preview : true,
                global = foldouts.TryGetValue(F_Global, out bool global) ? global : true,
                folders = foldouts.TryGetValue(F_Folders, out bool folders) ? folders : true,
                faders = foldouts.TryGetValue(F_Faders, out bool faders) ? faders : false,
                whitelist = foldouts.TryGetValue(F_Whitelist, out bool whitelist) ? whitelist : false,
                internalRefs = foldouts.TryGetValue(F_Internal, out bool internalRefs) ? internalRefs : false,
                folderStates = folderFoldouts ?? Array.Empty<bool>(),
                mochieConfiguration = mochieConfigurationExpanded,
                mochieOutlineList = mochieOutlineListExpanded,
                mochieOverlayList = mochieOverlayListExpanded,
                statsAdvanced = statsAdvancedFoldout,
                previewFolderIndex = previewFolderIndex,
                previewPageIndex = previewPageIndex
            };

            SessionState.SetString(GetFoldoutStateKey(), JsonUtility.ToJson(snapshot));
        }
        
        // Gets the folderEntries property for a specific folder's ObjectHandler
        private SerializedProperty GetFolderEntriesProperty(int folderIdx)
        {
            SerializedObject handlerObj = GetHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
            {
                return null;
            }

            ToggleFolderType folderType = GetFolderType(folderIdx);
            string propertyName = folderType == ToggleFolderType.Properties ? "propertyEntries" : "folderEntries";
            return handlerObj.FindProperty(propertyName);
        }

        // Gets the SerializedObject for the handler managing a specific folder
        private SerializedObject GetHandlerObjectForFolder(int folderIdx)
        {
            ToggleFolderType folderType = GetFolderType(folderIdx);

            switch (folderType)
            {
                case ToggleFolderType.Materials:
                    return GetMaterialHandlerObjectForFolder(folderIdx);
                case ToggleFolderType.Properties:
                    return GetPropertyHandlerObjectForFolder(folderIdx);
                case ToggleFolderType.Objects:
                    return GetObjectHandlerObjectForFolder(folderIdx);
                default:
                    return null;
            }
        }

        private static bool ShaderAssetExists(string shaderName)
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets($"\"{shaderName}\" t:Shader");
                if (guids != null && guids.Length > 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // ignored - we'll fall back to Shader.Find below
            }
            
            Shader shader = Shader.Find(shaderName);
            return shader != null;
        }
        
        private void EnsureStyles()
        {
            if (stylesReady) return;
            try
            {
                headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold
                };
                headerSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Italic
                };
                foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold
                };
                folderHeaderLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };
                folderHeaderFoldoutStyle = new GUIStyle(EditorStyles.foldout)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
                dragZoneStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic,
                    padding = new RectOffset(4,4,6,6)
                };
                stylesReady = true;
            }
            catch
            {
                stylesReady = false;
            }
        }
        
        private void EnsureFoldoutDefaults()
        {
            if (!foldouts.ContainsKey(F_Global))    foldouts[F_Global]    = true;
            if (!foldouts.ContainsKey(F_Folders))     foldouts[F_Folders]     = true;
            if (!foldouts.ContainsKey(F_Faders))    foldouts[F_Faders]    = false;
            if (!foldouts.ContainsKey(F_Whitelist)) foldouts[F_Whitelist] = false;
            if (!foldouts.ContainsKey(F_Internal))  foldouts[F_Internal]  = false;
        }
        
        private void EnforceSingleInstanceFolderTypes(params ToggleFolderType[] singleInstanceTypes)
        {
            if (singleInstanceTypes == null || singleInstanceTypes.Length == 0)
            return;
            
            if (folderTypesProperty == null || folderNamesProperty == null)
            return;
            
            int limit = Mathf.Min(folderTypesProperty.arraySize, folderNamesProperty.arraySize);
            foreach (ToggleFolderType type in singleInstanceTypes)
            {
                bool found = false;
                for (int i = 0; i < limit; i++)
                {
                    SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                    if (typeProp == null)
                    continue;
                    
                    if (typeProp.enumValueIndex == (int)type)
                    {
                        if (found)
                        {
                            ToggleFolderType previousType = GetFolderTypeFromProp(typeProp);
                            HandleFolderTypeChanged(i, previousType, ToggleFolderType.Objects);
                            typeProp.enumValueIndex = (int)ToggleFolderType.Objects;
                        }
                        else
                        {
                            found = true;
                        }
                    }
                }
            }
        }
        
        private Transform GetFoldersTransform(EnigmaLaunchpad launchpad)
        {
            if (launchpad == null) return null;
            Transform folders = launchpad.transform.Find("Folders");
            return folders == null ? launchpad.transform : folders;
        }
        
        private void EnsureFolderArrayParity()
        {
            if (folderNamesProperty == null) return;
            int folderCount = folderNamesProperty.arraySize;
            
            EnsureArraySize(folderTypesProperty, folderCount, prop =>
            {
                prop.enumValueIndex = (int)ToggleFolderType.Objects;
            });
        }
        
        private void EnsureArraySize(SerializedProperty array, int targetSize, Action<SerializedProperty> initializeElement)
        {
            if (array == null) return;

            while (array.arraySize < targetSize)
            {
                array.InsertArrayElementAtIndex(array.arraySize);
                var element = array.GetArrayElementAtIndex(array.arraySize - 1);
                initializeElement?.Invoke(element);
            }

            while (array.arraySize > targetSize)
            {
                int lastIndex = array.arraySize - 1;
                array.DeleteArrayElementAtIndex(lastIndex);

                if (array.arraySize > targetSize)
                {
                    SerializedProperty check = array.GetArrayElementAtIndex(Mathf.Min(lastIndex, array.arraySize - 1));
                    if (check != null && check.propertyType == SerializedPropertyType.ObjectReference && check.objectReferenceValue == null)
                    {
                        array.DeleteArrayElementAtIndex(Mathf.Min(lastIndex, array.arraySize - 1));
                    }
                }
            }
        }

        private void DeleteArrayElement(SerializedProperty array, int index)
        {
            if (array == null || index < 0 || index >= array.arraySize)
            {
                return;
            }

            array.DeleteArrayElementAtIndex(index);

            if (index < array.arraySize)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(index);
                if (element != null && element.propertyType == SerializedPropertyType.ObjectReference && element.objectReferenceValue == null)
                {
                    array.DeleteArrayElementAtIndex(index);
                }
            }
        }
        
        
        
        
        private bool LooksLikeAutoGeneratedColorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            
            // Check if it matches the hex format "Color #XXXXXX"
            if (name.StartsWith(HexColorNamePrefix) && name.Length == HexColorNameLength)
            {
                // Verify that the characters after "Color #" are exactly 6 valid hex digits
                string hexPart = name.Substring(HexColorNamePrefix.Length);
                if (hexPart.Length == 6 && int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out _))
                {
                    return true;
                }
            }
            
            // Check if it matches any of the known preset color names from ColorNameMatcher
            return ColorNameMatcher.IsKnownColorName(name);
        }
        
        private string GetMatchedColorName(Color color)
        {
            #if UNITY_EDITOR
            string matched = ColorNameMatcher.GetNearestColorName(color);
            if (!string.IsNullOrEmpty(matched))
            {
                return matched;
            }
            #endif
            
            return $"Color #{ColorUtility.ToHtmlStringRGB(color)}";
        }
        
        private void RefreshFolderFoldouts()
        {
            int count = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            if (folderFoldouts == null || folderFoldouts.Length != count)
            {
                var arr = new bool[count];
                // Preserve existing foldout states
                int oldLength = (folderFoldouts != null) ? folderFoldouts.Length : 0;
                for (int i = 0; i < count; i++)
                {
                    if (i < oldLength)
                    {
                        // Copy existing state
                        arr[i] = folderFoldouts[i];
                    }
                    else
                    {
                        // New folders default to open
                        arr[i] = true;
                    }
                }
                folderFoldouts = arr;
            }
        }
        
        public override void OnInspectorGUI()
        {
            EnsureStyles();
            so.Update();
            EnsureFolderArrayParity();
            EnforceSingleInstanceFolderTypes(ToggleFolderType.Skybox, ToggleFolderType.Mochie, ToggleFolderType.Stats);
            EnsureObjectHandlerParity();
            EnsureMaterialHandlerParity();
            EnsurePropertyHandlerParity();
            EnsureShaderHandlerParity();
            EnsureJuneHandlerParity();
            EnsureSkyboxHandlerParity();
            EnsureMochieHandlerParity();
            EnsureStatsHandlerParity();
            EnsurePresetHandlerParity();
            if (objectHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in objectHandlerObjects)
                {
                    handlerObject?.Update();
                }
            }
            if (materialHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in materialHandlerObjects)
                {
                    handlerObject?.Update();
                }
            }
            if (propertyHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in propertyHandlerObjects)
                {
                    handlerObject?.Update();
                }
            }
            BindSkyboxHandlerSerializedObject();
            if (skyboxHandlerObject != null)
            {
                skyboxHandlerObject.Update();
            }
            BindStatsHandlerSerializedObject();
            if (statsHandlerObject != null)
            {
                statsHandlerObject.Update();
            }
            BindPresetHandlerSerializedObject();
            if (presetHandlerObject != null)
            {
                presetHandlerObject.Update();
            }
            BindMochieHandlerSerializedObject();
            if (mochiHandlerObject != null)
            {
                mochiHandlerObject.Update();
            }
            BindFaderHandlerSerializedObject();
            if (faderHandlerObject != null)
            {
                faderHandlerObject.Update();
            }
            
            DrawHeaderSafe();
            GUILayout.Space(4);
            
            DrawPreviewSection();
            
            DrawSection(F_Global, () =>
            {
                GUILayout.Space(InnerContentVerticalPad);
                DrawDefaultFolderPopup();
                GUILayout.Space(InnerContentVerticalPad);
                DrawColorField(activeColorProperty, "Active Color", "Color for active/toggled elements");
                DrawColorField(inactiveColorProperty, "Inactive Color", "Base color for inactive elements");
                GUILayout.Space(InnerContentVerticalPad);
                
                // Show ScreenHandler settings if screenHandler is assigned
                if (screenHandlerProperty != null && screenHandlerProperty.objectReferenceValue != null)
                {
                    ScreenHandler screenHandler = screenHandlerProperty.objectReferenceValue as ScreenHandler;
                    if (screenHandler != null)
                    {
                        GUILayout.Space(6);
                        
                        // Default Screen dropdown
                        string[] screenNames = screenHandler.GetScreenNames();
                        if (screenNames != null && screenNames.Length > 0)
                        {
                            // Map defaultScreenIndex to dropdown index:
                            // -1 (AudioLink) → 0, 0 (first screen) → 1, 1 (second screen) → 2, etc.
                            int currentIndex = screenHandler.GetDefaultScreenIndex();
                            int dropdownIndex = currentIndex + 1; // Convert from defaultScreenIndex to dropdown index
                            
                            // Clamp to valid range to handle edge cases
                            dropdownIndex = Mathf.Clamp(dropdownIndex, 0, screenNames.Length - 1);
                            
                            int newDropdownIndex = EditorGUILayout.Popup("Default Screen", dropdownIndex, screenNames);
                            if (newDropdownIndex != dropdownIndex)
                            {
                                // Convert from dropdown index to defaultScreenIndex
                                int newDefaultScreenIndex = newDropdownIndex - 1;
                                screenHandler.SetDefaultScreenIndex(newDefaultScreenIndex);
                                EditorUtility.SetDirty(screenHandler);
                            }
                        }
                        
                        // AudioLink field (from AutoLink component)
                        GUILayout.Space(6);
                        DrawAudioLinkField();
                        
                        // Video Screen Material
                        if (videoScreenMaterialProperty != null)
                        {
                            EditorGUILayout.PropertyField(videoScreenMaterialProperty, new GUIContent("Video Screen Material", "Material to assign to the Video screen mesh renderer"));
                        }
                        
                        // Video Player Controls dropdown
                        if (videoPlayerControlsModeProperty != null)
                        {
                            string[] videoPlayerOptions = new string[] { "None", "ProTV", "VideoTXL" };
                            int currentMode = videoPlayerControlsModeProperty.intValue;
                            int newMode = EditorGUILayout.Popup("Video Player Controls", currentMode, videoPlayerOptions);
                            if (newMode != currentMode)
                            {
                                videoPlayerControlsModeProperty.intValue = newMode;
                                ApplyVideoPlayerControlsMode(newMode);
                            }
                        }
                        
                        // Show ProTV fields when ProTV is selected
                        if (videoPlayerControlsModeProperty != null && videoPlayerControlsModeProperty.intValue == 1)
                        {
#if ARCHIT_PROTV
                            if (proTVMediaControlsProperty != null && proTVMediaControlsProperty.objectReferenceValue != null)
                            {
                                MediaControls mediaControls = proTVMediaControlsProperty.objectReferenceValue as MediaControls;
                                if (mediaControls != null)
                                {
                                    SerializedObject mediaControlsObject = new SerializedObject(mediaControls);
                                    mediaControlsObject.Update();
                                    
                                    SerializedProperty tvProperty = mediaControlsObject.FindProperty("tv");
                                    SerializedProperty queueProperty = mediaControlsObject.FindProperty("queue");
                                    
                                    if (tvProperty != null)
                                    {
                                        EditorGUILayout.PropertyField(tvProperty, new GUIContent("TV Manager"));
                                    }
                                    if (queueProperty != null)
                                    {
                                        EditorGUILayout.PropertyField(queueProperty, new GUIContent("Queue"));
                                    }
                                    
                                    mediaControlsObject.ApplyModifiedProperties();
                                }
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("Assign a ProTV MediaControls component in Internal References to configure TV settings.", MessageType.Info);
                            }
#else
                            EditorGUILayout.HelpBox("ProTV package is not installed. Install ProTV to use this feature.", MessageType.Warning);
#endif
                        }
                        
                        // Show VideoTXL fields when VideoTXL is selected
                        if (videoPlayerControlsModeProperty != null && videoPlayerControlsModeProperty.intValue == 2)
                        {
#if TEXEL_VIDEO
                            if (videoTXLPlayerControlsProperty != null && videoTXLPlayerControlsProperty.objectReferenceValue != null)
                            {
                                PlayerControls playerControls = videoTXLPlayerControlsProperty.objectReferenceValue as PlayerControls;
                                if (playerControls != null)
                                {
                                    SerializedObject playerControlsObject = new SerializedObject(playerControls);
                                    playerControlsObject.Update();
                                    
                                    SerializedProperty videoPlayerProperty = playerControlsObject.FindProperty("videoPlayer");
                                    SerializedProperty audioManagerProperty = playerControlsObject.FindProperty("audioManager");
                                    
                                    if (videoPlayerProperty != null)
                                    {
                                        EditorGUILayout.PropertyField(videoPlayerProperty, new GUIContent("Video Player"));
                                    }
                                    if (audioManagerProperty != null)
                                    {
                                        EditorGUILayout.PropertyField(audioManagerProperty, new GUIContent("Audio Manager"));
                                    }
                                    
                                    playerControlsObject.ApplyModifiedProperties();
                                }
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("Assign a VideoTXL PlayerControls component in Internal References to configure video player settings.", MessageType.Info);
                            }
#else
                            EditorGUILayout.HelpBox("VideoTXL package is not installed. Install VideoTXL to use this feature.", MessageType.Warning);
#endif
                        }
                        
                        GUILayout.Space(InnerContentVerticalPad);
                    }
                }
            });
            
            DrawSection(F_Folders, () =>
            {
                GUILayout.Space(InnerContentVerticalPad);
                DrawFoldersManager();
                GUILayout.Space(6);
                var duplicateReport = BuildDuplicateReport();
                DrawAllFolders(duplicateReport);
                GUILayout.Space(InnerContentVerticalPad);
            });
            
            // Only show Faders section if there are faders configured
            if (ShouldShowFadersSection())
            {
                DrawSection(F_Faders, () =>
                {
                    GUILayout.Space(InnerContentVerticalPad);
                    DrawFadersSection();
                    GUILayout.Space(InnerContentVerticalPad);
                });
            }
            
            DrawSection(F_Whitelist, () =>
            {
                GUILayout.Space(InnerContentVerticalPad);
                
                if (whitelistEnabled != null)
                {
                    EditorGUILayout.PropertyField(
                    whitelistEnabled,
                    new GUIContent(
                    "Whitelist Enabled",
                    "If true, only the listed VRChat usernames may operate the launchpad."));
                }
                
                bool whitelistActive = whitelistEnabled == null || whitelistEnabled.boolValue;
                
                using (new EditorGUI.DisabledScope(!whitelistActive))
                {
                    if (instanceOwnerAlwaysHasAccess != null)
                    {
                        EditorGUILayout.PropertyField(
                        instanceOwnerAlwaysHasAccess,
                        new GUIContent(
                        "Instance Owner Always Has Access",
                        "If true, the instance owner may operate the launchpad even when not on the whitelist."));
                    }
                    
                    if (ohGeezCmonAccessControl != null)
                    {
                        EditorGUILayout.PropertyField(
                        ohGeezCmonAccessControl,
                        new GUIContent(
                        "OhGeezCmon Access Control",
                        "Optional AccessControlManager integration that supplies whitelist usernames."));
                    }
                    
                    bool hasExternalAccessControl = ohGeezCmonAccessControl != null && ohGeezCmonAccessControl.objectReferenceValue != null;
                    
                    if (authorizedUsernames != null)
                    {
                        using (new EditorGUI.DisabledScope(hasExternalAccessControl))
                        {
                            EditorGUILayout.PropertyField(
                            authorizedUsernames,
                            new GUIContent(
                            "Authorized Usernames",
                            "Authorized VRChat usernames (case-insensitive, trims whitespace)."),
                            true);
                        }
                    }
                    
                    if (hasExternalAccessControl)
                    {
                        EditorGUILayout.HelpBox(
                        "Authorized usernames are provided by the assigned OhGeezCmon Access Control Manager.",
                        MessageType.Info);
                    }
                }
                
                GUILayout.Space(InnerContentVerticalPad);
            });
            
            DrawSection(F_Internal, () =>
            {
                GUILayout.Space(InnerContentVerticalPad);
                EditorGUILayout.PropertyField(buttonHandlers, new GUIContent("Button Handlers"));
                // Add ScreenHandler reference above FaderSystemHandler
                if (screenHandlerProperty != null)
                {
                    EditorGUILayout.PropertyField(screenHandlerProperty, new GUIContent("Screen Handler"));
                }
                if (faderHandlerProperty != null)
                {
                    EditorGUILayout.PropertyField(faderHandlerProperty, new GUIContent("Fader System Handler"));
                    
                    // Show fader arrays when FaderSystemHandler is assigned
                    if (faderHandlerObject != null)
                    {
                        faderHandlerObject.Update();
                        if (fadersFadersArray != null)
                        {
                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.PropertyField(fadersFadersArray, new GUIContent("Fader Handlers"), true);
                            if (EditorGUI.EndChangeCheck())
                            {
                                faderHandlerObject.ApplyModifiedProperties();
                                AutoAssignFaderHandlerReferences();
                            }
                        }
                        EditorGUI.BeginChangeCheck();
                        if (leftHandColliderProperty != null)
                        {
                            EditorGUILayout.PropertyField(leftHandColliderProperty, new GUIContent("Left Hand Collider"));
                        }
                        if (rightHandColliderProperty != null)
                        {
                            EditorGUILayout.PropertyField(rightHandColliderProperty, new GUIContent("Right Hand Collider"));
                        }
                        if (EditorGUI.EndChangeCheck())
                        {
                            faderHandlerObject.ApplyModifiedProperties();
                            AutoAssignFaderHandlerReferences();
                        }
                        faderHandlerObject.ApplyModifiedProperties();
                    }
                }
                EditorGUILayout.PropertyField(itemsPerPage);
                if (materialHandlers != null)
                {
                    // Material handlers are managed automatically per Materials folder.
                }
                if (mochieMaterialStandardProperty != null || mochieMaterialXProperty != null)
                {
                    GUILayout.Space(6);

                    if (mochieMaterialStandardProperty != null)
                    {
                        mochieMaterialStandardProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                        "Standard Material",
                        mochieMaterialStandardProperty.objectReferenceValue,
                        typeof(Material),
                        false) as Material;
                    }
                    
                    bool hasXShader = mochieShaderXAvailable || (mochieMaterialXProperty?.objectReferenceValue != null);
                    using (new EditorGUI.DisabledScope(!hasXShader))
                    {
                        if (mochieMaterialXProperty != null)
                        {
                            mochieMaterialXProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                            "X Material",
                            mochieMaterialXProperty.objectReferenceValue,
                            typeof(Material),
                            false) as Material;
                        }
                    }
                }

                bool juneMaterialChanged = false;
                if (juneMaterialProperty != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(juneMaterialProperty, new GUIContent("June Material"));
                    juneMaterialChanged = EditorGUI.EndChangeCheck();
                }

                if (juneMaterialChanged)
                {
                    ApplyJuneMaterialToHandlers();
                }
                
                // Video Player Controls references at the bottom
#if ARCHIT_PROTV
                if (proTVMediaControlsProperty != null)
                {
                    proTVMediaControlsProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                        new GUIContent("ProTV", "ProTV MediaControls component reference"),
                        proTVMediaControlsProperty.objectReferenceValue,
                        typeof(MediaControls),
                        true);
                }
#else
                if (proTVMediaControlsProperty != null)
                {
                    proTVMediaControlsProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                        new GUIContent("ProTV", "ProTV MediaControls component reference (Package not installed)"),
                        proTVMediaControlsProperty.objectReferenceValue,
                        typeof(UdonSharpBehaviour),
                        true);
                }
#endif
                
#if TEXEL_VIDEO
                if (videoTXLPlayerControlsProperty != null)
                {
                    videoTXLPlayerControlsProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                        new GUIContent("VideoTXL", "VideoTXL PlayerControls component reference"),
                        videoTXLPlayerControlsProperty.objectReferenceValue,
                        typeof(PlayerControls),
                        true);
                }
#else
                if (videoTXLPlayerControlsProperty != null)
                {
                    videoTXLPlayerControlsProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                        new GUIContent("VideoTXL", "VideoTXL PlayerControls component reference (Package not installed)"),
                        videoTXLPlayerControlsProperty.objectReferenceValue,
                        typeof(UdonSharpBehaviour),
                        true);
                }
#endif
                
                GUILayout.Space(InnerContentVerticalPad);
            });
            
            GUILayout.Space(10);
            DrawFooterLogo();
            
            if (materialHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in materialHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (objectHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in objectHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (propertyHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in propertyHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (skyboxHandlerObject != null)
            {
                skyboxHandlerObject.ApplyModifiedProperties();
            }
            if (statsHandlerObject != null)
            {
                statsHandlerObject.ApplyModifiedProperties();
            }
            if (presetHandlerObject != null)
            {
                presetHandlerObject.ApplyModifiedProperties();
            }
            if (mochiHandlerObject != null)
            {
                // Auto-set useSfxXLayout based on assigned materials
                SetMochieLayoutFlag();
                mochiHandlerObject.ApplyModifiedProperties();
            }
            if (juneHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in juneHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (faderHandlerObject != null)
            {
                faderHandlerObject.ApplyModifiedProperties();
            }
            so.ApplyModifiedProperties();
        }
        
        #region Header / Footer
        private void DrawHeaderSafe()
        {
            GUILayout.Space(6);
            if (stylesReady)
            {
                GUILayout.Label("ENIGMA LAUNCHPAD OS", headerTitleStyle);
                GUILayout.Label("Developed by Cozen", headerSubtitleStyle);
                string versionDisplay = string.IsNullOrEmpty(localVersion) ? "V0.9" : $"V{localVersion}";
                GUILayout.Label(versionDisplay, headerSubtitleStyle);
            }
            else
            {
                EditorGUILayout.LabelField("ENIGMA LAUNCHPAD OS (initializing styles...)", EditorStyles.boldLabel);
            }
            
            // Display update notification if available
            if (updateAvailable && !string.IsNullOrEmpty(remoteVersion))
            {
                GUILayout.Space(8);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(4);
                
                EditorGUILayout.LabelField($"Update Available: V{remoteVersion}", EditorStyles.boldLabel);
                
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("View Update on GitHub", GUILayout.Width(180)))
                {
                    Application.OpenURL(RepoUrl);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                GUILayout.Space(4);
                EditorGUILayout.EndVertical();
            }
        }
        
        private void DrawFooterLogo()
        {
            if (logoTexture != null)
            {
                GUILayout.Space(8);
                float iw = EditorGUIUtility.currentViewWidth;
                float targetWidth = Mathf.Min(iw * 0.55f, 340f);
                float aspect = (float)logoTexture.height / logoTexture.width;
                Rect r = GUILayoutUtility.GetRect(targetWidth, targetWidth * aspect, GUILayout.ExpandWidth(false));
                r.x = (iw - targetWidth) * 0.5f;
                GUI.DrawTexture(r, logoTexture, ScaleMode.ScaleToFit);
            }
        }
        #endregion
        
        #region Foldout Sections
        private void DrawSection(string key, Action content)
        {
            GUILayout.Space(SectionBetweenSpacing);
            GUILayout.Space(SectionVerticalSpacing);
            
            Rect headerRect = EditorGUILayout.GetControlRect(false, SectionHeaderHeight);
            Rect clickable = new Rect(headerRect.x + SectionHorizontalPadding,
            headerRect.y,
            headerRect.width - SectionHorizontalPadding * 2,
            headerRect.height);
            
            if (GUI.Button(clickable, GUIContent.none, GUIStyle.none))
            {
                bool updated = !foldouts[key];
                PersistFoldoutState(key, updated);
                GUI.FocusControl(null);
            }
            
            var labelStyle = stylesReady ? foldoutLabelStyle : EditorStyles.boldLabel;
            EditorGUI.LabelField(new Rect(clickable.x + 6, clickable.y + 4, clickable.width - 12, 18), key, labelStyle);
            
            float sidePad = SectionHorizontalPadding + 14;
            if (foldouts[key])
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Space(sidePad);
                GUILayout.BeginVertical();
                content.Invoke();
                GUILayout.EndVertical();
                GUILayout.Space(sidePad);
                GUILayout.EndHorizontal();
            }
            
            Rect totalRect = GUILayoutUtility.GetLastRect();
            if (!foldouts[key]) totalRect = headerRect;
            
            totalRect.x += SectionHorizontalPadding;
            totalRect.width -= SectionHorizontalPadding * 2;
            
            DrawRoundedBox(totalRect, SectionCornerRadius, SectionBorderWidth);
        }
        
        private void DrawFolderInnerSection(Action content)
        {
            Rect outer = EditorGUILayout.BeginVertical();
            GUILayout.Space(FolderInnerContentPad);
            content.Invoke();
            GUILayout.Space(FolderInnerContentPad);
            EditorGUILayout.EndVertical();
            
            DrawRoundedBox(outer, SectionCornerRadius - 2f, 1.4f);
        }
        
        private void DrawRoundedBox(Rect rect, float radius, float borderWidth)
        {
            if (Event.current.type != EventType.Repaint) return;
            
            Rect expanded = new Rect(
            rect.x - BorderPadding,
            rect.y - BorderPadding,
            rect.width + BorderPadding * 2f,
            rect.height + BorderPadding * 2f
            );
            
            Handles.BeginGUI();
            Color prev = Handles.color;
            
            if (borderWidth > 0f)
            {
                Handles.color = GUI.contentColor;
                var edgePts = RoundedRectPoints(expanded, radius, 14);
                for (int i = 0; i < edgePts.Length; i++)
                {
                    Vector3 a = edgePts[i];
                    Vector3 b = edgePts[(i + 1) % edgePts.Length];
                    Handles.DrawAAPolyLine(borderWidth, a, b);
                }
            }
            
            Handles.color = prev;
            Handles.EndGUI();
        }
        
        private Vector3[] RoundedRectPoints(Rect r, float radius, int segmentsPerCorner)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) / 2f);
            List<Vector3> pts = new List<Vector3>(segmentsPerCorner * 4 + 4);
            
            Vector2 tl = new Vector2(r.x + radius, r.y + radius);
            Vector2 tr = new Vector2(r.xMax - radius, r.y + radius);
            Vector2 br = new Vector2(r.xMax - radius, r.yMax - radius);
            Vector2 bl = new Vector2(r.x + radius, r.yMax - radius);
            
            AddArc(pts, tl, 180f, 270f, radius, segmentsPerCorner);
            AddArc(pts, tr, 270f, 360f, radius, segmentsPerCorner);
            AddArc(pts, br,   0f,  90f, radius, segmentsPerCorner);
            AddArc(pts, bl,  90f, 180f, radius, segmentsPerCorner);
            
            return pts.ToArray();
        }
        
        private void AddArc(List<Vector3> list, Vector2 center, float startDeg, float endDeg, float radius, int segs)
        {
            float startRad = startDeg * Mathf.Deg2Rad;
            float endRad   = endDeg   * Mathf.Deg2Rad;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float ang = Mathf.Lerp(startRad, endRad, t);
                list.Add(new Vector3(center.x + Mathf.Cos(ang) * radius,
                center.y + Mathf.Sin(ang) * radius));
            }
        }
        #endregion
        
        #region Global Section
        private void DrawDefaultFolderPopup()
        {
            int folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            if (folderCount == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup("Default Folder", 0, new[] { "No folders available" });
                }
                defaultFolderIndexProperty.intValue = 0;
                return;
            }
            
            string[] opts = new string[folderCount];
            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty nameProp = folderNamesProperty.GetArrayElementAtIndex(i);
                string label = (nameProp != null) ? nameProp.stringValue : string.Empty;
                
                if (string.IsNullOrEmpty(label) && folderTypesProperty != null && folderTypesProperty.arraySize > i)
                {
                    var typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                    if (typeProp != null)
                    {
                        label = GetFolderDisplayLabel((ToggleFolderType)typeProp.enumValueIndex);
                    }
                }
                
                if (string.IsNullOrEmpty(label))
                {
                    label = "Folder";
                }
                
                opts[i] = label;
            }
            
            int current = Mathf.Clamp(defaultFolderIndexProperty.intValue, 0, opts.Length - 1);
            if (current != defaultFolderIndexProperty.intValue)
            {
                defaultFolderIndexProperty.intValue = current;
            }
            
            int newVal = EditorGUILayout.Popup("Default Folder", current, opts);
            if (newVal != current) defaultFolderIndexProperty.intValue = newVal;
        }
        
        private void DrawColorField(SerializedProperty colorProperty, string label, string tooltip)
        {
            if (colorProperty == null) return;
            
            EditorGUILayout.BeginHorizontal();
            
            Color currentColor = colorProperty.colorValue;
            Color newColor = EditorGUILayout.ColorField(new GUIContent(label, tooltip), currentColor);
            
            if (newColor != currentColor)
            {
                colorProperty.colorValue = newColor;
            }
            
            // Display color name using ColorNameMatcher
            string colorName = GetMatchedColorName(newColor);
            GUILayout.Label(colorName, GUILayout.Width(120));
            
            EditorGUILayout.EndHorizontal();
        }
        #endregion
        
        #region Folders UI
        private void DrawFoldersManager()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Folder", GUILayout.MinWidth(90)))
            {
                AddFolder();
                ApplyAndRepaint();
                GUILayout.EndHorizontal();
                return;
            }
            
            GUI.enabled = folderNamesProperty != null && folderNamesProperty.arraySize > 0;
            if (GUILayout.Button("Clear All", GUILayout.MinWidth(80)))
            {
                if (EditorUtility.DisplayDialog("Clear All Folders", "Remove ALL folders?", "Yes", "No"))
                {
                    folderNamesProperty.ClearArray();
                    folderExclusiveProperty.ClearArray();
                    folderEntryCountsProperty.ClearArray();
                    if (folderTypesProperty != null) folderTypesProperty.ClearArray();
                    // Handlers will be cleaned up by EnsureObjectHandlerParity
                    defaultFolderIndexProperty.intValue = 0;
                    RefreshFolderFoldouts();
                    ApplyAndRepaint();
                    GUILayout.EndHorizontal();
                    return;
                }
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        
        private void DrawAllFolders(Dictionary<int, List<DuplicateMessage>> duplicateReport)
        {
            if (folderNamesProperty == null) return;
            RefreshFolderFoldouts();
            float folderPanelVerticalPad = 8f;
            for (int m = 0; m < folderNamesProperty.arraySize; m++)
            {
                GUILayout.Space(folderPanelVerticalPad);
                if (DrawSingleFolder(m, duplicateReport))
                return;
                GUILayout.Space(folderPanelVerticalPad);
            }
        }
        
        private bool DrawSingleFolder(int m, Dictionary<int, List<DuplicateMessage>> duplicateReport)
        {
            bool structural = false;
            
            SerializedProperty nameProp  = folderNamesProperty.GetArrayElementAtIndex(m);
            SerializedProperty exclProp  = folderExclusiveProperty.GetArrayElementAtIndex(m);
            SerializedProperty countProp = folderEntryCountsProperty.GetArrayElementAtIndex(m);
            SerializedProperty typeProp  = (folderTypesProperty != null && folderTypesProperty.arraySize > m)
            ? folderTypesProperty.GetArrayElementAtIndex(m)
            : null;
            SerializedProperty rendererProp = GetFolderMaterialRendererProperty(m);
            
            ToggleFolderType folderType = GetFolderTypeFromProp(typeProp);
            string displayName = string.IsNullOrEmpty(nameProp.stringValue)
            ? GetFolderDisplayLabel(folderType)
            : nameProp.stringValue;
            
            DrawFolderInnerSection(() =>
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(SectionHorizontalPadding);
                bool expanded = EditorGUILayout.Foldout(folderFoldouts[m], $"Folder {m + 1}: {displayName}", true);
                if (expanded != folderFoldouts[m])
                {
                    folderFoldouts[m] = expanded;
                    SavePersistedFoldoutStates();
                }
                GUILayout.FlexibleSpace();
                
                GUI.enabled = m > 0;
                if (GUILayout.Button("▲", GUILayout.Width(24)))
                {
                    MoveFolder(m, m - 1);
                    structural = true;
                }
                GUI.enabled = m < folderNamesProperty.arraySize - 1 && !structural;
                if (!structural && GUILayout.Button("▼", GUILayout.Width(24)))
                {
                    MoveFolder(m, m + 1);
                    structural = true;
                }
                GUI.enabled = true;
                
                if (!structural && GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    if (EditorUtility.DisplayDialog("Remove Folder",
                    $"Remove folder '{displayName}'?", "Yes", "No"))
                    {
                        RemoveFolder(m);
                        structural = true;
                    }
                }
                GUILayout.EndHorizontal();
                
                if (structural) return;
                if (!folderFoldouts[m]) return;
                
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(nameProp, new GUIContent("Name"));

                if (typeProp != null)
                {
                    ToggleFolderType newType = DrawFilteredFolderTypePopup(m, folderType);
                    if (newType != folderType)
                    {
                        bool changed = HandleFolderTypeChanged(m, folderType, newType);
                        typeProp.enumValueIndex = (int)newType;
                        folderType = newType;
                        structural |= changed;
                    }
                }
                
                List<DuplicateMessage> duplicateMessages = null;
                if (duplicateReport != null && duplicateReport.TryGetValue(m, out var captured) && captured != null && captured.Count > 0)
                {
                    duplicateMessages = captured;
                }
                
                if (duplicateMessages != null)
                {
                    foreach (var entry in duplicateMessages)
                    {
                        EditorGUILayout.HelpBox(entry.message, entry.type);
                    }
                }
                
                // Add edit-time warnings for folder configuration
                if (folderType == ToggleFolderType.Objects && countProp.intValue == 0)
                {
                    EditorGUILayout.HelpBox("This Objects folder has zero entries. Add objects or remove this folder.", MessageType.Warning);
                }
                
                if (folderType == ToggleFolderType.Materials)
                {
                    if (countProp.intValue == 0)
                    {
                        EditorGUILayout.HelpBox("This Materials folder has zero entries. Add materials or remove this folder.", MessageType.Warning);
                    }
                    if (GetRendererArraySize(rendererProp) == 0)
                    {
                        EditorGUILayout.HelpBox("This Materials folder is missing a Target Renderer. Assign a renderer below.", MessageType.Warning);
                    }
                }
                
                if (DrawSkyboxFolderSection(folderType, exclProp, rendererProp, countProp, ref structural)) return;
                
                if (DrawMochieFolderSection(folderType, exclProp, rendererProp, countProp, ref structural)) return;
                
                if (DrawJuneFolderSection(m, folderType, exclProp, countProp, ref structural)) return;
                
                if (folderType == ToggleFolderType.Stats)
                {
                    if (exclProp.boolValue)
                    exclProp.boolValue = false;
                    if (rendererProp != null && rendererProp.isArray && rendererProp.arraySize > 0)
                    rendererProp.arraySize = 0;

                    GUILayout.Space(6);
                    if (DrawWorldStatsSection(m, countProp))
                    {
                        structural = true;
                        EditorGUI.indentLevel--;
                        return;
                    }
                    EditorGUI.indentLevel--;
                    return;
                }

                if (DrawShaderFolderSection(m, exclProp, countProp, ref structural))
                {
                    return;
                }

                if (folderType == ToggleFolderType.Presets)
                {
                    if (DrawPresetFolderSection(folderType, exclProp, countProp, ref structural))
                    {
                        EditorGUI.indentLevel--;
                        return;
                    }
                    EditorGUI.indentLevel--;
                    return;
                }

                if (folderType == ToggleFolderType.Properties)
                {
                    GUILayout.Space(6);
                    if (DrawPropertiesSection(m, exclProp, countProp))
                    {
                        structural = true;
                        EditorGUI.indentLevel--;
                        return;
                    }
                    EditorGUI.indentLevel--;
                    return;
                }

                EditorGUILayout.PropertyField(exclProp, new GUIContent("Make Entries Exclusive"));
                
                if (rendererProp != null)
                {
                    if (folderType == ToggleFolderType.Materials)
                    {
                        EditorGUILayout.PropertyField(rendererProp, new GUIContent("Target Renderers"), true);
                    }
                    else if (rendererProp.isArray && rendererProp.arraySize > 0)
                    {
                        rendererProp.arraySize = 0;
                    }
                }
                
                if (folderType == ToggleFolderType.Materials)
                {
                    DrawMaterialHandlerInspector(m);
                }
                
                int count = countProp.intValue;
                GUILayout.Space(4);
                string pluralLabel = GetFolderTypePlural(folderType);
                EditorGUILayout.LabelField($"{pluralLabel} ({count})", folderHeaderLabelStyle);
                GUILayout.Space(2);
                
                Type entryType = GetFolderEntryType(folderType);
                bool allowSceneObjects = folderType != ToggleFolderType.Materials;
                string singularLabel = GetFolderTypeSingular(folderType);
                
                // Get the per-handler folderEntries for this folder
                SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(m);
                if (folderEntriesProperty == null)
                {
                    EditorGUILayout.HelpBox(
                    $"Internal error: Could not find handler for folder {m}. " +
                    "This folder may not have an associated handler.",
                    MessageType.Error);
                    EditorGUI.indentLevel--;
                    return;
                }
                
                for (int i = 0; i < count; i++)
                {
                    if (i >= folderEntriesProperty.arraySize) break;
                    
                    var element = folderEntriesProperty.GetArrayElementAtIndex(i);
                    GUILayout.BeginHorizontal();
                    UnityEngine.Object currentValue = element.objectReferenceValue;
                    UnityEngine.Object newValue = EditorGUILayout.ObjectField(
                    $"{singularLabel} {i + 1}",
                    currentValue,
                    entryType,
                    allowSceneObjects);
                    if (newValue != currentValue && (newValue == null || entryType.IsInstanceOfType(newValue)))
                    {
                        element.objectReferenceValue = newValue;
                    }
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(22)))
                    {
                        if (MoveFolderObject(m, i, i - 1))
                        {
                            structural = true;
                            GUILayout.EndHorizontal();
                            break;
                        }
                    }
                    GUI.enabled = !structural && i < count - 1;
                    if (!structural && GUILayout.Button("▼", GUILayout.Width(22)))
                    {
                        if (MoveFolderObject(m, i, i + 1))
                        {
                            structural = true;
                            GUILayout.EndHorizontal();
                            break;
                        }
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        RemoveObjectAt(m, i);
                        structural = true;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
                
                if (structural)
                {
                    EditorGUI.indentLevel--;
                    return;
                }
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"+ {singularLabel}"))
                {
                    if (ModifyFolderObjectCount(m, count + 1))
                    structural = true;
                }
                GUI.enabled = count > 0;
                if (!structural && GUILayout.Button($"- {singularLabel}"))
                {
                    if (ModifyFolderObjectCount(m, count - 1))
                    structural = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                
                if (structural)
                {
                    EditorGUI.indentLevel--;
                    return;
                }
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Selected"))
                {
                    if (AddEntriesToFolder(m, GetSelectionForFolderType(folderType)))
                    structural = true;
                }
                GUI.enabled = count > 0;
                if (!structural && GUILayout.Button("Remove All"))
                {
                    if (EditorUtility.DisplayDialog($"Remove All {pluralLabel}",
                    $"Remove all {pluralLabel} in folder '{displayName}'?", "Yes", "No"))
                    {
                        ModifyFolderObjectCount(m, 0);
                        structural = true;
                    }
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                
                if (structural)
                {
                    EditorGUI.indentLevel--;
                    return;
                }
                
                GUILayout.Space(4);
                if (DrawDragDropZone(m))
                structural = true;
                
                EditorGUI.indentLevel--;
            });
            
            if (structural)
            {
                ApplyAndRepaint();
                return true;
            }
            return false;
        }
        
        
        
        
        
        
        private static void SetFloatProperty(SerializedProperty property, float value)
        {
            if (property != null)
            {
                property.floatValue = value;
            }
        }
        
        private bool DrawDragDropZone(int folderIdx)
        {
            ToggleFolderType folderType = GetFolderType(folderIdx);
            if (folderType == ToggleFolderType.Skybox) return false;
            string pluralLabel = GetFolderTypePlural(folderType);
            Type entryType = GetFolderEntryType(folderType);
            
            Rect r = GUILayoutUtility.GetRect(0f, DropZoneHeight, GUILayout.ExpandWidth(true));
            GUI.Box(r, $"Drag & Drop {pluralLabel} here to add", dragZoneStyle ?? EditorStyles.helpBox);
            
            Event e = Event.current;
            if (!r.Contains(e.mousePosition))
            return false;
            
            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                bool anyValid = false;
                foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
                {
                    if (o != null && entryType.IsInstanceOfType(o))
                    {
                        anyValid = true;
                        break;
                    }
                }
                
                if (anyValid)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        var list = new System.Collections.Generic.List<UnityEngine.Object>();
                        foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
                        {
                            if (o != null && entryType.IsInstanceOfType(o))
                            list.Add(o);
                        }
                        if (list.Count > 0 && AddEntriesToFolder(folderIdx, list.ToArray()))
                        {
                            return true;
                        }
                    }
                    e.Use();
                }
            }
            return false;
        }

        private ToggleFolderType GetFolderTypeFromProp(SerializedProperty typeProp)
        {
            if (typeProp == null) return ToggleFolderType.Objects;
            int idx = typeProp.enumValueIndex;
            int max = Enum.GetValues(typeof(ToggleFolderType)).Length - 1;
            if (idx < 0 || idx > max) idx = 0;
            return (ToggleFolderType)idx;
        }
        
        private ToggleFolderType GetFolderType(int folderIdx)
        {
            SerializedProperty typeProp = (folderTypesProperty != null && folderTypesProperty.arraySize > folderIdx)
            ? folderTypesProperty.GetArrayElementAtIndex(folderIdx)
            : null;
            return GetFolderTypeFromProp(typeProp);
        }
        
        /// <summary>
        /// Draws a folder type popup that filters out single-instance types (Skybox, Stats, Mochie, Presets)
        /// if they are already assigned to another folder.
        /// </summary>
        private ToggleFolderType DrawFilteredFolderTypePopup(int folderIdx, ToggleFolderType currentType)
        {
            // Single-instance folder types that can only be assigned to one folder at a time
            var singleInstanceTypes = new HashSet<ToggleFolderType>
            {
                ToggleFolderType.Skybox,
                ToggleFolderType.Stats,
                ToggleFolderType.Mochie,
                ToggleFolderType.Presets
            };
            
            // Build a set of types that are already in use by other folders
            var usedSingleInstanceTypes = new HashSet<ToggleFolderType>();
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;
            for (int i = 0; i < folderCount; i++)
            {
                if (i == folderIdx) continue; // Skip the current folder
                ToggleFolderType otherType = GetFolderType(i);
                if (singleInstanceTypes.Contains(otherType))
                {
                    usedSingleInstanceTypes.Add(otherType);
                }
            }
            
            // Build list of available types (exclude single-instance types already in use)
            var availableTypes = new List<ToggleFolderType>();
            var displayNames = new List<string>();
            foreach (ToggleFolderType type in Enum.GetValues(typeof(ToggleFolderType)))
            {
                bool isSingleInstance = singleInstanceTypes.Contains(type);
                bool isUsedElsewhere = usedSingleInstanceTypes.Contains(type);
                
                // Include the type if:
                // - It's not a single-instance type, OR
                // - It's a single-instance type but not used elsewhere, OR
                // - It's the current folder's type (so the current selection is always visible)
                if (!isSingleInstance || !isUsedElsewhere || type == currentType)
                {
                    availableTypes.Add(type);
                    displayNames.Add(type.ToString());
                }
            }
            
            // Find the current index in the filtered list
            int currentIndex = availableTypes.IndexOf(currentType);
            if (currentIndex < 0) currentIndex = 0;
            
            // Draw the popup with filtered options
            int newIndex = EditorGUILayout.Popup(new GUIContent("Folder Type"), currentIndex, displayNames.ToArray());
            
            return availableTypes[newIndex];
        }
        
        private string GetFolderName(int folderIdx)
        {
            if (folderNamesProperty == null || folderIdx < 0 || folderIdx >= folderNamesProperty.arraySize)
            {
                return string.Empty;
            }
            
            SerializedProperty nameProp = folderNamesProperty.GetArrayElementAtIndex(folderIdx);
            return nameProp != null ? nameProp.stringValue : string.Empty;
        }
        
        private int GetFolderIndexForType(ToggleFolderType folderType)
        {
            int folderCount = folderTypesProperty != null ? folderTypesProperty.arraySize : 0;
            for (int i = 0; i < folderCount; i++)
            {
                SerializedProperty typeProp = folderTypesProperty.GetArrayElementAtIndex(i);
                if (GetFolderTypeFromProp(typeProp) == folderType)
                {
                    return i;
                }
            }
            
            return -1;
        }
        
        private string GetResolvedFolderName(int folderIndex)
        {
            string folderName = GetFolderName(folderIndex);
            if (string.IsNullOrEmpty(folderName))
            {
                folderName = $"Folder{folderIndex + 1}";
            }
            return folderName;
        }
        
        private string SanitizeForHandlerName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Folder";
            }
            
            var builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
                else if (!char.IsWhiteSpace(c))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append('_');
                }
            }
            
            string sanitized = builder.ToString();
            return string.IsNullOrEmpty(sanitized) ? "Folder" : sanitized;
        }
        
        private Type GetFolderEntryType(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Materials:
                return typeof(Material);
                case ToggleFolderType.Properties:
                return typeof(string);
                case ToggleFolderType.Skybox:
                case ToggleFolderType.Mochie:
                return typeof(UnityEngine.Object);
                default:
                return typeof(GameObject);
            }
        }
        
        private string GetFolderTypeSingular(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Materials:
                return "Material";
                case ToggleFolderType.Properties:
                return "Property";
                case ToggleFolderType.Skybox:
                return "Skybox";
                case ToggleFolderType.Stats:
                return "Stat";
                case ToggleFolderType.Shaders:
                return "Shader";
                case ToggleFolderType.Mochie:
                return "Mochie Setting";
                default:
                return "GameObject";
            }
        }
        
        private string GetFolderTypePlural(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Materials:
                return "Materials";
                case ToggleFolderType.Properties:
                return "Properties";
                case ToggleFolderType.Skybox:
                return "Skyboxes";
                case ToggleFolderType.Stats:
                return "Stats";
                case ToggleFolderType.Shaders:
                return "Shaders";
                case ToggleFolderType.Mochie:
                return "Mochie Settings";
                default:
                return "GameObjects";
            }
        }
        
        private UnityEngine.Object[] GetSelectionForFolderType(ToggleFolderType type)
        {
            if (type == ToggleFolderType.Materials)
            {
                UnityEngine.Object[] selection = Selection.objects;
                if (selection == null || selection.Length == 0) return Array.Empty<UnityEngine.Object>();
                var filtered = new List<UnityEngine.Object>();
                foreach (var obj in selection)
                if (obj is Material) filtered.Add(obj);
                return filtered.ToArray();
            }
            
            if (type == ToggleFolderType.Skybox || type == ToggleFolderType.Mochie || type == ToggleFolderType.Stats)
            {
                return Array.Empty<UnityEngine.Object>();
            }
            
            GameObject[] gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return Array.Empty<UnityEngine.Object>();
            var result = new UnityEngine.Object[gos.Length];
            for (int i = 0; i < gos.Length; i++) result[i] = gos[i];
            return result;
        }
        
        private bool HandleFolderTypeChanged(int folderIdx, ToggleFolderType oldType, ToggleFolderType newType)
        {
            if (oldType == newType) return false;
            bool changed = false;
            
            if (newType != ToggleFolderType.Materials)
            {
                var rendererProp = GetFolderMaterialRendererProperty(folderIdx);
                if (rendererProp != null && rendererProp.isArray && rendererProp.arraySize > 0)
                {
                    rendererProp.arraySize = 0;
                    changed = true;
                }
            }
            
            if (ModifyFolderObjectCount(folderIdx, 0))
            changed = true;
            
            // Clear exclusive flag for single-instance folder types
            if (newType == ToggleFolderType.Skybox || newType == ToggleFolderType.Mochie || newType == ToggleFolderType.Stats || newType == ToggleFolderType.Presets)
            {
                if (folderExclusiveProperty != null && folderExclusiveProperty.arraySize > folderIdx)
                {
                    var exclProp = folderExclusiveProperty.GetArrayElementAtIndex(folderIdx);
                    if (exclProp.boolValue)
                    {
                        exclProp.boolValue = false;
                        changed = true;
                    }
                }
            }
            
            // Set the default folder name for all folder types
            changed |= EnsureDefaultFolderName(folderIdx, newType);
            
            return changed;
        }
        
        private bool EnsureDefaultFolderName(int folderIdx, ToggleFolderType type)
        {
            if (folderNamesProperty == null || folderNamesProperty.arraySize <= folderIdx)
            return false;
            
            var nameProp = folderNamesProperty.GetArrayElementAtIndex(folderIdx);
            if (nameProp == null)
            return false;
            
            string desired = GetAutoFolderName(type);
            if (string.IsNullOrEmpty(desired) || nameProp.stringValue == desired)
            return false;
            
            nameProp.stringValue = desired;
            return true;
        }
        #endregion
        
        #region Structural Helpers
        private void ApplyAndRepaint()
        {
            if (objectHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in objectHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (materialHandlerObjects != null)
            {
                foreach (SerializedObject handlerObject in materialHandlerObjects)
                {
                    handlerObject?.ApplyModifiedProperties();
                }
            }
            if (mochiHandlerObject != null)
            {
                mochiHandlerObject.ApplyModifiedProperties();
            }
            if (statsHandlerObject != null)
            {
                statsHandlerObject.ApplyModifiedProperties();
            }
            if (faderHandlerObject != null)
            {
                faderHandlerObject.ApplyModifiedProperties();
            }
            so.ApplyModifiedProperties();
            Repaint();
        }
        
        private void AddFolder()
        {
            if (folderNamesProperty == null ||
            folderExclusiveProperty == null ||
            folderEntryCountsProperty == null)
            {
                Debug.LogError(
                "AddFolder failed: one or more folder SerializedProperties are null. " +
                "Check EnigmaLaunchpad serialized field names (folderNames, folderExclusive, folderEntryCounts).");
                return;
            }
            
            int newIndex = folderNamesProperty.arraySize;
            folderNamesProperty.InsertArrayElementAtIndex(newIndex);
            folderExclusiveProperty.InsertArrayElementAtIndex(newIndex);
            folderEntryCountsProperty.InsertArrayElementAtIndex(newIndex);
            if (folderTypesProperty != null)
            {
                folderTypesProperty.InsertArrayElementAtIndex(newIndex);
                folderTypesProperty.GetArrayElementAtIndex(newIndex).enumValueIndex = (int)ToggleFolderType.Objects;
            }
            
            folderNamesProperty.GetArrayElementAtIndex(newIndex).stringValue = GetAutoFolderName(ToggleFolderType.Objects);
            folderExclusiveProperty.GetArrayElementAtIndex(newIndex).boolValue = false;
            folderEntryCountsProperty.GetArrayElementAtIndex(newIndex).intValue = 0;
            
            RefreshFolderFoldouts();
        }
        
        private void RemoveFolder(int m)
        {
            if (folderNamesProperty == null ||
            folderExclusiveProperty == null ||
            folderEntryCountsProperty == null)
            {
                Debug.LogError("RemoveFolder failed: one or more folder SerializedProperties are null.");
                return;
            }
            
            int count = folderEntryCountsProperty.GetArrayElementAtIndex(m).intValue;
            ToggleFolderType folderType = GetFolderType(m);
            if (folderType == ToggleFolderType.Stats)
            {
                int statsStart = GetStatsFolderStartIndex(m);
                RemoveStatsSegment(statsStart, count);
            }
            // For Objects/Materials folders, handlers will be cleaned up by EnsureObjectHandlerParity
            
            folderNamesProperty.DeleteArrayElementAtIndex(m);
            folderExclusiveProperty.DeleteArrayElementAtIndex(m);
            folderEntryCountsProperty.DeleteArrayElementAtIndex(m);
            if (folderTypesProperty != null && folderTypesProperty.arraySize > m)
            folderTypesProperty.DeleteArrayElementAtIndex(m);
            
            int folderCount = folderNamesProperty.arraySize;
            int maxIndex = Mathf.Max(0, folderCount - 1);
            if (defaultFolderIndexProperty.intValue > maxIndex)
            {
                defaultFolderIndexProperty.intValue = Mathf.Clamp(defaultFolderIndexProperty.intValue, 0, maxIndex);
            }
            
            RefreshFolderFoldouts();
        }
        
        private void MoveFolder(int from, int to)
        {
            if (from == to) return;
            
            if (folderNamesProperty == null ||
            folderExclusiveProperty == null ||
            folderEntryCountsProperty == null)
            {
                Debug.LogError("MoveFolder failed: one or more folder SerializedProperties are null.");
                return;
            }
            
            int fromCount = folderEntryCountsProperty.GetArrayElementAtIndex(from).intValue;
            ToggleFolderType fromType = GetFolderType(from);
            WorldStatMetric[] extractedStats = null;
            
            if (fromType == ToggleFolderType.Stats)
            {
                int statsStart = GetStatsFolderStartIndex(from);
                extractedStats = ExtractStatsSegment(statsStart, fromCount);
                RemoveStatsSegment(statsStart, fromCount);
            }
            // For Objects/Materials folders, handlers maintain their own data independently
            
            folderNamesProperty.MoveArrayElement(from, to);
            folderExclusiveProperty.MoveArrayElement(from, to);
            folderEntryCountsProperty.MoveArrayElement(from, to);
            if (folderTypesProperty != null)
            folderTypesProperty.MoveArrayElement(from, to);
            
            if (fromType == ToggleFolderType.Stats)
            {
                int newStatsStart = GetStatsFolderStartIndex(to);
                InsertStatsSegment(newStatsStart, extractedStats);
            }
            // For Objects/Materials, update handler folderIndex values
            else if (fromType == ToggleFolderType.Objects || fromType == ToggleFolderType.Materials)
            {
                // Handlers will be updated by EnsureObjectHandlerParity when properties are applied
                // We need to update folderIndex for handlers to match new positions
                UpdateHandlerFolderIndices();
            }
            
            int current = defaultFolderIndexProperty.intValue;
            if (current == from) defaultFolderIndexProperty.intValue = to;
            else if (from < to && current > from && current <= to) defaultFolderIndexProperty.intValue--;
            else if (from > to && current >= to && current < from) defaultFolderIndexProperty.intValue++;
            
            // Update fader folder indices to preserve fader assignments after reordering
            UpdateFaderFolderIndices(from, to);
            
            RefreshFolderFoldouts();
        }
        
        private bool ModifyFolderObjectCount(int folderIdx, int newCount)
        {
            if (newCount < 0) newCount = 0;
            ToggleFolderType folderType = GetFolderType(folderIdx);
            if (folderType == ToggleFolderType.Skybox)
            newCount = 0;
            if (folderType == ToggleFolderType.Stats)
            {
                return ModifyStatsEntryCount(folderIdx, newCount);
            }
            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int oldCount = countProp.intValue;
            if (newCount == oldCount) return false;
            
            // Get the per-handler folderEntries for this folder
            SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(folderIdx);
            if (folderEntriesProperty == null)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Could not find handler for folder {folderIdx}");
                return false;
            }
            
            if (newCount > oldCount)
            {
                int toAdd = newCount - oldCount;
                for (int i = 0; i < toAdd; i++)
                {
                    folderEntriesProperty.InsertArrayElementAtIndex(folderEntriesProperty.arraySize);
                    var elem = folderEntriesProperty.GetArrayElementAtIndex(folderEntriesProperty.arraySize - 1);
                    if (elem.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        elem.objectReferenceValue = null;
                    }
                    else if (elem.propertyType == SerializedPropertyType.String)
                    {
                        elem.stringValue = string.Empty;
                    }
                }
            }
            else
            {
                int toRemove = oldCount - newCount;
                for (int i = 0; i < toRemove; i++)
                {
                    int removeAt = folderEntriesProperty.arraySize - 1;
                    if (removeAt >= 0)
                    {
                        folderEntriesProperty.DeleteArrayElementAtIndex(removeAt);
                    }
                }
            }
            countProp.intValue = newCount;
            return true;
        }
        
        private void RemoveObjectAt(int folderIdx, int localIndex)
        {
            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int count = countProp.intValue;
            if (localIndex < 0 || localIndex >= count) return;
            
            if (GetFolderType(folderIdx) == ToggleFolderType.Stats)
            {
                RemoveStatsEntryAt(folderIdx, localIndex);
                return;
            }
            
            // Get the per-handler folderEntries for this folder
            SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(folderIdx);
            if (folderEntriesProperty == null)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Could not find handler for folder {folderIdx}");
                countProp.intValue = count - 1;
                return;
            }
            
            if (localIndex >= 0 && localIndex < folderEntriesProperty.arraySize)
            {
                folderEntriesProperty.DeleteArrayElementAtIndex(localIndex);
                if (localIndex < folderEntriesProperty.arraySize)
                {
                    var prop = folderEntriesProperty.GetArrayElementAtIndex(localIndex);
                    if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                    prop.objectReferenceValue == null)
                    {
                        folderEntriesProperty.DeleteArrayElementAtIndex(localIndex);
                    }
                }
            }
            countProp.intValue = count - 1;
        }
        
        private bool MoveFolderObject(int folderIdx, int fromLocalIndex, int toLocalIndex)
        {
            if (fromLocalIndex == toLocalIndex) return false;
            
            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int count = countProp.intValue;
            if (toLocalIndex < 0 || toLocalIndex >= count) return false;
            
            if (GetFolderType(folderIdx) == ToggleFolderType.Stats)
            {
                return MoveStatsEntry(folderIdx, fromLocalIndex, toLocalIndex);
            }
            
            // Get the per-handler folderEntries for this folder
            SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(folderIdx);
            if (folderEntriesProperty == null)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Could not find handler for folder {folderIdx}");
                return false;
            }
            
            if (fromLocalIndex < 0 || fromLocalIndex >= folderEntriesProperty.arraySize) return false;
            if (toLocalIndex < 0 || toLocalIndex >= folderEntriesProperty.arraySize) return false;
            
            folderEntriesProperty.MoveArrayElement(fromLocalIndex, toLocalIndex);
            return true;
        }
        
        private bool AddEntriesToFolder(int folderIdx, UnityEngine.Object[] newEntries)
        {
            if (newEntries == null || newEntries.Length == 0) return false;
            ToggleFolderType folderType = GetFolderType(folderIdx);
            if (folderType == ToggleFolderType.Skybox) return false;
            if (folderType == ToggleFolderType.Stats) return false;
            if (folderType == ToggleFolderType.Properties) return false;
            
            // Get the per-handler folderEntries for this folder
            SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(folderIdx);
            if (folderEntriesProperty == null)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Could not find handler for folder {folderIdx}");
                return false;
            }
            
            Type expectedType = GetFolderEntryType(folderType);
            
            var filtered = new System.Collections.Generic.List<UnityEngine.Object>();
            foreach (var entry in newEntries)
            {
                if (entry == null) continue;
                if (!expectedType.IsInstanceOfType(entry)) continue;
                if (FolderContainsEntry(folderIdx, entry)) continue;
                filtered.Add(entry);
            }
            if (filtered.Count == 0) return false;
            
            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int oldCount = countProp.intValue;
            int addCount = filtered.Count;
            
            ModifyFolderObjectCount(folderIdx, oldCount + addCount);
            
            // Add entries to the per-handler array
            for (int i = 0; i < addCount; i++)
            {
                int insertAt = oldCount + i;
                if (insertAt < folderEntriesProperty.arraySize)
                {
                    folderEntriesProperty.GetArrayElementAtIndex(insertAt).objectReferenceValue = filtered[i];
                }
            }
            return true;
        }
        
        private bool FolderContainsEntry(int folderIdx, UnityEngine.Object target)
        {
            if (target == null) return false;
            
            // Get the per-handler folderEntries for this folder
            SerializedProperty folderEntriesProperty = GetFolderEntriesProperty(folderIdx);
            if (folderEntriesProperty == null)
            {
                Debug.LogError($"[EnigmaLaunchpadEditor] Could not find handler for folder {folderIdx}");
                return false;
            }
            
            int count = folderEntriesProperty.arraySize;
            for (int i = 0; i < count; i++)
            {
                var slot = folderEntriesProperty.GetArrayElementAtIndex(i);
                if (slot.objectReferenceValue == target) return true;
            }
            return false;
        }
        #endregion
        
        #region Flat Array Utilities
        private int GetFolderStartOffset(int folderIdx)
        {
            int start = 0;
            int totalFolders = (folderEntryCountsProperty != null) ? folderEntryCountsProperty.arraySize : 0;
            for (int i = 0; i < folderIdx && i < totalFolders; i++)
            {
                ToggleFolderType type = GetFolderType(i);
                if (type == ToggleFolderType.Skybox || type == ToggleFolderType.Mochie || type == ToggleFolderType.Stats)
                continue;
                start += folderEntryCountsProperty.GetArrayElementAtIndex(i).intValue;
            }
            return start;
        }
        

        private Dictionary<int, List<DuplicateMessage>> BuildDuplicateReport()
        {
            var combined = BuildMaterialDuplicateReport();
            MergeDuplicateReports(combined, BuildObjectDuplicateReport());
            MergeDuplicateReports(combined, BuildFolderNameDuplicateReport());
            return combined;
        }
        
        private Dictionary<int, List<DuplicateMessage>> BuildFolderNameDuplicateReport()
        {
            var result = new Dictionary<int, List<DuplicateMessage>>();
            if (folderNamesProperty == null)
            return result;
            
            var usageMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            
            for (int folderIdx = 0; folderIdx < folderNamesProperty.arraySize; folderIdx++)
            {
                string rawName = folderNamesProperty.GetArrayElementAtIndex(folderIdx).stringValue;
                ToggleFolderType type = GetFolderType(folderIdx);
                string normalized = string.IsNullOrWhiteSpace(rawName)
                ? GetAutoFolderName(type)
                : rawName.Trim();
                
                if (!usageMap.TryGetValue(normalized, out var indices))
                {
                    indices = new List<int>();
                    usageMap.Add(normalized, indices);
                }
                indices.Add(folderIdx);
            }
            
            foreach (var pair in usageMap)
            {
                var indices = pair.Value;
                if (indices == null || indices.Count <= 1)
                continue;
                
                indices.Sort();
                string displayName = pair.Key;
                
                foreach (int folderIdx in indices)
                {
                    var list = GetOrCreateMessageList(result, folderIdx);
                    string others = BuildOtherFolderList(indices, folderIdx);
                    string msg = $"Folder name '{displayName}' duplicates {others}. Rename folders so each has a unique name.";
                    list.Add(new DuplicateMessage(msg, MessageType.Error));
                }
            }
            
            return result;
        }
        
        private void MergeDuplicateReports(Dictionary<int, List<DuplicateMessage>> destination, Dictionary<int, List<DuplicateMessage>> source)
        {
            if (destination == null || source == null) return;
            foreach (var pair in source)
            {
                var list = GetOrCreateMessageList(destination, pair.Key);
                if (pair.Value != null)
                {
                    list.AddRange(pair.Value);
                }
            }
        }
        
        private static string GetAutoFolderName(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Objects:
                return "Objects";
                case ToggleFolderType.Materials:
                return "Materials";
                case ToggleFolderType.Properties:
                return "Properties";
                case ToggleFolderType.Skybox:
                return "Skybox";
                case ToggleFolderType.Stats:
                return "Stats";
                case ToggleFolderType.Shaders:
                return "Shaders";
                case ToggleFolderType.Mochie:
                return "Mochie";
                case ToggleFolderType.June:
                return "June";
                case ToggleFolderType.Presets:
                return "Presets";
                default:
                return "Objects";
            }
        }
        
        private static string GetFolderDisplayLabel(ToggleFolderType type)
        {
            switch (type)
            {
                case ToggleFolderType.Objects:
                return "Objects";
                case ToggleFolderType.Materials:
                return "Materials";
                case ToggleFolderType.Skybox:
                return "Skybox";
                case ToggleFolderType.Stats:
                return "World Stats";
                case ToggleFolderType.Shaders:
                return "Shaders";
                case ToggleFolderType.Mochie:
                return "Mochie";
                case ToggleFolderType.June:
                return "June";
                case ToggleFolderType.Presets:
                return "Presets";
                default:
                return "Folder";
            }
        }
        
        private string GetFolderDisplayName(int folderIdx)
        {
            if (folderNamesProperty == null || folderIdx < 0 || folderIdx >= folderNamesProperty.arraySize)
            return $"Folder {folderIdx + 1}";
            string value = folderNamesProperty.GetArrayElementAtIndex(folderIdx).stringValue;
            if (string.IsNullOrEmpty(value))
            {
                var type = GetFolderType(folderIdx);
                value = GetFolderDisplayLabel(type);
            }
            return value;
        }
        
        private static List<DuplicateMessage> GetOrCreateMessageList(Dictionary<int, List<DuplicateMessage>> dict, int key)
        {
            if (!dict.TryGetValue(key, out var list) || list == null)
            {
                list = new List<DuplicateMessage>();
                dict[key] = list;
            }
            return list;
        }
        
        private string BuildOtherFolderList(List<int> folderIndices, int excludeFolder)
        {
            if (folderIndices == null) return "other folders";
            var builder = new List<string>();
            foreach (int idx in folderIndices)
            {
                if (idx == excludeFolder) continue;
                builder.Add($"'{GetFolderDisplayName(idx)}'");
            }
            if (builder.Count == 0) return "other folders";
            return string.Join(", ", builder);
        }
        
        private void ApplyVideoPlayerControlsMode(int mode)
        {
            EnigmaLaunchpad launchpad = (EnigmaLaunchpad)target;
            if (launchpad == null) return;
            
            // Find the Video Player Controls transform under Launchpad
            Transform videoPlayerControlsTransform = launchpad.transform.Find("Video Player Controls");
            if (videoPlayerControlsTransform == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad Editor] Could not find 'Video Player Controls' child object");
                return;
            }
            
            // Disable all children first and set to EditorOnly
            foreach (Transform child in videoPlayerControlsTransform)
            {
                child.gameObject.SetActive(false);
                child.gameObject.tag = "EditorOnly";
            }
            
            // Enable the appropriate child based on mode
            string childName = null;
            if (mode == 1) // ProTV
            {
                childName = "MediaControls";
            }
            else if (mode == 2) // VideoTXL
            {
                childName = "PlayerControls";
            }
            
            if (!string.IsNullOrEmpty(childName))
            {
                Transform targetChild = videoPlayerControlsTransform.Find(childName);
                if (targetChild != null)
                {
                    targetChild.gameObject.SetActive(true);
                    targetChild.gameObject.tag = "Untagged";
                }
                else
                {
                    Debug.LogWarning($"[EnigmaLaunchpad Editor] Could not find '{childName}' child object under 'Video Player Controls'");
                }
            }
            
            EditorUtility.SetDirty(launchpad);
        }
        
        private void DrawAudioLinkField()
        {
            EnigmaLaunchpad launchpad = (EnigmaLaunchpad)target;
            
            // Auto-find and assign AutoLink component if not already set
            if (autoLinkComponentProperty != null && autoLinkComponentProperty.objectReferenceValue == null)
            {
                EnsureAutoLinkComponent();
            }
            
            // Get the AutoLink component
            UdonSharpBehaviour autoLink = autoLinkComponentProperty?.objectReferenceValue as UdonSharpBehaviour;
            
            if (autoLink != null)
            {
                SerializedObject autoLinkObject = new SerializedObject(autoLink);
                autoLinkObject.Update();
                
                SerializedProperty audioLinkProperty = autoLinkObject.FindProperty("audioLink");
                
                if (audioLinkProperty != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(audioLinkProperty, new GUIContent("Audio Link", "AudioLink component for audio-reactive features"));
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        autoLinkObject.ApplyModifiedProperties();
                        HandleAudioLinkAssignment(audioLinkProperty.objectReferenceValue);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("AutoLink component found but 'audioLink' property not accessible.", MessageType.Warning);
                }
            }
        }
        
        private void EnsureAutoLinkComponent()
        {
            EnigmaLaunchpad launchpad = (EnigmaLaunchpad)target;
            
            // Try to find AutoLink at the specified path
            Transform autoLinkTransform = launchpad.transform.Find("Screen/AudioLink/AudioLinkControllerBody/AutoLink");
            
            if (autoLinkTransform != null)
            {
                UdonSharpBehaviour autoLinkComponent = autoLinkTransform.GetComponent<UdonSharpBehaviour>();
                if (autoLinkComponent != null)
                {
                    autoLinkComponentProperty.objectReferenceValue = autoLinkComponent;
                    
                    // Try to auto-populate with first AudioLink in scene
                    SerializedObject autoLinkObject = new SerializedObject(autoLinkComponent);
                    autoLinkObject.Update();
                    
                    SerializedProperty audioLinkProperty = autoLinkObject.FindProperty("audioLink");
                    if (audioLinkProperty != null && audioLinkProperty.objectReferenceValue == null)
                    {
                        // Find first AudioLink in scene
                        AudioLink.AudioLink[] audioLinks = UnityEngine.Object.FindObjectsOfType<AudioLink.AudioLink>();
                        if (audioLinks != null && audioLinks.Length > 0)
                        {
                            audioLinkProperty.objectReferenceValue = audioLinks[0];
                            autoLinkObject.ApplyModifiedProperties();
                            Debug.Log($"[EnigmaLaunchpad Editor] Auto-populated AudioLink with: {audioLinks[0].name}");
                        }
                    }
                    
                    EditorUtility.SetDirty(launchpad);
                }
            }
        }
        
        private void HandleAudioLinkAssignment(UnityEngine.Object audioLinkObj)
        {
            EnigmaLaunchpad launchpad = (EnigmaLaunchpad)target;
            
            // Find the AutoLink GameObject
            Transform autoLinkTransform = launchpad.transform.Find("Screen/AudioLink/AudioLinkControllerBody/AutoLink");
            
            if (autoLinkTransform != null)
            {
                // Enable/disable the AutoLink GameObject based on whether AudioLink is assigned
                bool shouldEnable = audioLinkObj != null;
                
                if (autoLinkTransform.gameObject.activeSelf != shouldEnable)
                {
                    autoLinkTransform.gameObject.SetActive(shouldEnable);
                    EditorUtility.SetDirty(autoLinkTransform.gameObject);
                    Debug.Log($"[EnigmaLaunchpad Editor] AutoLink GameObject {(shouldEnable ? "enabled" : "disabled")}");
                }
            }
            
            // Also assign to AudioLinkController at Screen/AudioLink
            Transform audioLinkControllerTransform = launchpad.transform.Find("Screen/AudioLink");
            if (audioLinkControllerTransform != null)
            {
                UdonSharpBehaviour audioLinkController = audioLinkControllerTransform.GetComponent<UdonSharpBehaviour>();
                if (audioLinkController != null)
                {
                    SerializedObject audioLinkControllerObject = new SerializedObject(audioLinkController);
                    audioLinkControllerObject.Update();
                    
                    SerializedProperty audioLinkProperty = audioLinkControllerObject.FindProperty("audioLink");
                    if (audioLinkProperty != null)
                    {
                        audioLinkProperty.objectReferenceValue = audioLinkObj;
                        audioLinkControllerObject.ApplyModifiedProperties();
                        Debug.Log($"[EnigmaLaunchpad Editor] AudioLinkController.audioLink updated");
                    }
                }
            }
        }
        
        #endregion
        
        #region Version Checking
        
        private void LoadLocalVersion()
        {
            try
            {
                if (File.Exists(VersionFilePath))
                {
                    localVersion = File.ReadAllText(VersionFilePath).Trim();
                }
                else
                {
                    localVersion = "Unknown";
                    Debug.LogWarning($"[EnigmaLaunchpad Editor] VERSION file not found at {VersionFilePath}");
                }
            }
            catch (Exception ex)
            {
                localVersion = "Unknown";
                Debug.LogError($"[EnigmaLaunchpad Editor] Error reading VERSION file: {ex.Message}");
            }
        }
        
        private void CheckForUpdates()
        {
            if (versionCheckInProgress || versionCheckComplete)
            {
                return;
            }
            
            versionCheckInProgress = true;
            versionCheckRequest = UnityWebRequest.Get(RemoteVersionUrl);
            
            var operation = versionCheckRequest.SendWebRequest();
            operation.completed += OnVersionCheckComplete;
        }
        
        private void OnVersionCheckComplete(UnityEngine.AsyncOperation op)
        {
            versionCheckInProgress = false;
            versionCheckComplete = true;
            
            if (versionCheckRequest == null)
            {
                return;
            }
            
            #if UNITY_2020_1_OR_NEWER
            if (versionCheckRequest.result == UnityWebRequest.Result.Success)
            #else
            if (!versionCheckRequest.isNetworkError && !versionCheckRequest.isHttpError)
            #endif
            {
                remoteVersion = versionCheckRequest.downloadHandler.text.Trim();
                updateAvailable = CompareVersions(localVersion, remoteVersion) < 0;
                
                if (updateAvailable)
                {
                    Debug.Log($"[EnigmaLaunchpad Editor] Update available! Local: {localVersion}, Remote: {remoteVersion}");
                }
            }
            else
            {
                Debug.LogWarning($"[EnigmaLaunchpad Editor] Failed to check for updates: {versionCheckRequest.error}");
            }
            
            versionCheckRequest.Dispose();
            versionCheckRequest = null;
            
            // Request a repaint to show the update notification
            Repaint();
        }
        
        private int CompareVersions(string version1, string version2)
        {
            if (string.IsNullOrEmpty(version1) || version1 == "Unknown") return -1;
            if (string.IsNullOrEmpty(version2) || version2 == "Unknown") return 1;
            
            // Parse version numbers (e.g., "0.9" -> [0, 9])
            var parts1 = version1.Split('.');
            var parts2 = version2.Split('.');
            
            int maxLength = Math.Max(parts1.Length, parts2.Length);
            
            for (int i = 0; i < maxLength; i++)
            {
                int num1 = 0;
                int num2 = 0;
                
                if (i < parts1.Length)
                {
                    int.TryParse(parts1[i], out num1);
                }
                
                if (i < parts2.Length)
                {
                    int.TryParse(parts2[i], out num2);
                }
                
                if (num1 < num2) return -1;
                if (num1 > num2) return 1;
            }
            
            return 0; // Versions are equal
        }
        
        #endregion
    }
}
#endif

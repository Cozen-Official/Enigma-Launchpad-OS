using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using TMPro;

namespace Cozen
{
    public enum ToggleFolderType
    {
        Objects,
        Materials,
        Properties,
        Skybox,
        Stats,
        Shaders,
        Mochie,
        June,
        Presets
    }
    
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class EnigmaLaunchpad : UdonSharpBehaviour
    {
        [Tooltip("Initial folder when world loads.")]
        [UdonSynced]
        [Range(0, 255)]
        public int defaultFolderIndex = 1;
        
        [Tooltip("Handlers for the primary launchpad buttons (0-8 items, 9 page/auto, optional 10 folder label).")]
        public ButtonHandler[] buttonHandlers;
        
        [System.Obsolete("Deprecated: replaced by ButtonHandler-managed text references.")]
        [SerializeField, HideInInspector]
        private TMP_Text[] buttonTexts;
        
        [Tooltip("If true, only the listed VRChat usernames may operate the launchpad.")]
        public bool whitelistEnabled = false;
        [Tooltip("If true, the instance owner may always operate the launchpad, even when not on the whitelist.")]
        public bool instanceOwnerAlwaysHasAccess = false;
        [Tooltip("Optional integration with OhGeezCmon's Access Control Manager. When assigned, its runtime admin list is used for the whitelist.")]
        public UdonSharpBehaviour ohGeezCmonAccessControl;
        [Tooltip("Optional integration with ProTV's TVManagedWhitelist. When assigned without OhGeezCmon, its authorizedList is used. When both are assigned, OhGeezCmon drives the whitelist and changes are pushed to ProTV.")]
        public UdonSharpBehaviour proTVManagedWhitelist;
        [Tooltip("Optional integration with Flatline's FlatlineSync whitelist. When assigned without higher-priority systems, its bakedWhitelist is used. Changes from higher-priority systems are pushed to Flatline.")]
        public UdonSharpBehaviour flatlineSync;
        [Tooltip("Authorized VRChat usernames (case-insensitive, trims whitespace).")]
        public string[] authorizedUsernames;
        
        private string[] normalizedAuthorizedUsernames;
        private bool whitelistInitialized = false;
        private UdonSharpBehaviour ohGeezAccessControlBehaviour;
        private bool isWaitingForOhGeezSync = false;
        private int ohGeezSyncRetryCount = 0;
        private const int MAX_OHGEEZ_SYNC_RETRIES = 10;
        private const float OHGEEZ_SYNC_RETRY_DELAY = 0.5f;
        private int lastKnownOhGeezSyncVersion = -1;
        private const float OHGEEZ_SYNC_CHECK_INTERVAL = 2.0f;
        
        // ProTV whitelist tracking
        private bool isWaitingForProTVSync = false;
        private int proTVSyncRetryCount = 0;
        private const int MAX_PROTV_SYNC_RETRIES = 10;
        private const float PROTV_SYNC_RETRY_DELAY = 0.5f;
        private int lastKnownProTVAuthorizedCount = -1;
        private const float PROTV_SYNC_CHECK_INTERVAL = 2.0f;
        private UdonSharpBehaviour proTVManager = null;
        private bool proTVManagerResolved = false;
        
        // Flatline whitelist tracking
        // Note: Flatline supports online whitelists downloaded from a URL (e.g., GitHub raw file)
        // The URL download may take longer than local data, so we use more retries with longer delays
        private bool isWaitingForFlatlineSync = false;
        private int flatlineSyncRetryCount = 0;
        private const int MAX_FLATLINE_SYNC_RETRIES = 20;  // More retries to account for URL download time
        private const float FLATLINE_SYNC_RETRY_DELAY = 1.0f;  // Longer delay for network operations
        private int lastKnownFlatlineWhitelistCount = -1;
        private const float FLATLINE_SYNC_CHECK_INTERVAL = 2.0f;
        
        [Header("Skybox Routing")]
        [Tooltip("Handler responsible for Skybox-specific logic.")]
        public SkyboxHandler skyboxHandler;
        
        [Header("Stats Routing")]
        [Tooltip("Handler responsible for Stats-specific logic.")]
        public StatsHandler statsHandler;
        
        [Header("Shader Routing")]
        [Tooltip("Handlers responsible for Shader folders.")]
        public ShaderHandler[] shaderHandlers;
        
        [Header("Preset Routing")]
        [Tooltip("Handler responsible for Preset-specific logic.")]
        public PresetHandler presetHandler;
        
        [Header("Folder Configuration")]
        [Tooltip("Display names for each folder (Objects, Materials, Skybox, etc.).")] 
        public string[] folderNames;
        [Tooltip("Folder type associated with each folder index.")]
        public ToggleFolderType[] folderTypes;
        [Tooltip("If true, only one toggle in the folder may be active at a time.")]
        public bool[] folderExclusive;
        [Tooltip("Number of entries contained in each folder.")]
        public int[] folderEntryCounts;
        
        [Header("Object Routing")]
        [Tooltip("Handlers responsible for object folders.")]
        public ObjectHandler[] objectHandlers;
        
        [Header("Material Routing")]
        [Tooltip("Handlers responsible for Materials folders.")]
        public MaterialHandler[] materialHandlers;

        [Header("Property Routing")]
        [Tooltip("Handlers responsible for Property folders.")]
        public PropertyHandler[] propertyHandlers;
        
        [Header("Mochie Routing")]
        [Tooltip("Handler responsible for Mochie-specific logic.")]
        public MochieHandler mochiHandler;
        [Tooltip("Standard Mochie material.")]
        public Material mochieMaterialStandard;
        [Tooltip("Mochie X material.")]
        public Material mochieMaterialX;

        [Header("June Routing")]
        [Tooltip("Handlers responsible for June folders.")]
        public JuneHandler[] juneHandlers;
        [Tooltip("Material assigned to all June handlers.")]
        public Material juneMaterial;
        
        [Tooltip("Handler responsible for screen toggling.")]
        public ScreenHandler screenHandler;
        
        [HideInInspector]
        public UdonSharpBehaviour autoLinkComponent;
        
        [Tooltip("Material to assign to the Video screen mesh renderer.")]
        public Material videoScreenMaterial;
        
        [HideInInspector]
        public int videoPlayerControlsMode = 0; // 0 = None, 1 = ProTV, 2 = VideoTXL
        
        [Tooltip("ProTV MediaControls component reference.")]
        public UdonSharpBehaviour proTVMediaControls;
        
        [Tooltip("VideoTXL PlayerControls component reference.")]
        public UdonSharpBehaviour videoTXLPlayerControls;
        
        [Tooltip("Handler responsible for fader system logic.")]
        public FaderSystemHandler faderHandler;
        
        [Tooltip("Color for active/toggled elements")]
        public Color activeColor = Color.HSVToRGB(242f / 360f, 1f, 1f);
        [Tooltip("Base color for inactive elements")]
        public Color inactiveColor = Color.white;
        
        // Button index constants for special buttons
        private const int PAGE_BUTTON_INDEX = 9;
        private const int FOLDER_BUTTON_INDEX = 10;
        
        public int itemsPerPage = 9;
        
        // --- Reset support (store initial state) ---
        private int initialDefaultFolderIndex;
        
        private void EnsureButtonHandlerReferences()
        {
            if (buttonHandlers == null || buttonHandlers.Length == 0)
            {
                buttonHandlers = GetComponentsInChildren<ButtonHandler>(true);
            }
            
            if (buttonHandlers == null)
            {
                return;
            }
            
            for (int i = 0; i < buttonHandlers.Length; i++)
            {
                ButtonHandler handler = buttonHandlers[i];
                if (handler == null)
                {
                    continue;
                }
                
                if (ButtonHandler.GetEnigmaLaunchpad(handler) == null)
                {
                    ButtonHandler.SetEnigmaLaunchpad(handler, this);
                }
            }
        }
        
        private void EnsureSkyboxHandlerReference()
        {
            if (skyboxHandler == null)
            {
                skyboxHandler = GetComponent<SkyboxHandler>();
                if (skyboxHandler == null)
                {
                    skyboxHandler = GetComponentInChildren<SkyboxHandler>(true);
                }
            }
            
            if (skyboxHandler != null)
            {
                skyboxHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsureStatsHandlerReference()
        {
            if (statsHandler == null)
            {
                statsHandler = GetComponent<StatsHandler>();
                if (statsHandler == null)
                {
                    statsHandler = GetComponentInChildren<StatsHandler>(true);
                }
            }
            
            if (statsHandler != null)
            {
                statsHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsurePresetHandlerReference()
        {
            if (presetHandler == null)
            {
                presetHandler = GetComponent<PresetHandler>();
                if (presetHandler == null)
                {
                    presetHandler = GetComponentInChildren<PresetHandler>(true);
                }
            }
            
            if (presetHandler != null)
            {
                presetHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsureMochieHandlerReference()
        {
            if (mochiHandler == null)
            {
                mochiHandler = GetComponent<MochieHandler>();
                if (mochiHandler == null)
                {
                    mochiHandler = GetComponentInChildren<MochieHandler>(true);
                }
            }
            
            if (mochiHandler != null)
            {
                mochiHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsureJuneHandlerReferences()
        {
            // If array is null or empty, try to find all JuneHandlers
            if (juneHandlers == null || juneHandlers.Length == 0)
            {
                JuneHandler[] found = GetComponentsInChildren<JuneHandler>(true);
                if (found != null && found.Length > 0)
                {
                    juneHandlers = found;
                }
            }
            
            if (juneHandlers != null)
            {
                for (int i = 0; i < juneHandlers.Length; i++)
                {
                    if (juneHandlers[i] != null)
                    {
                        juneHandlers[i].SetLaunchpad(this);
                        juneHandlers[i].juneMaterial = juneMaterial;
                    }
                }
            }
        }
        
        private void EnsureFaderHandlerReference()
        {
            if (faderHandler == null)
            {
                faderHandler = GetComponent<FaderSystemHandler>();
                if (faderHandler == null)
                {
                    faderHandler = GetComponentInChildren<FaderSystemHandler>(true);
                }
            }
            
            if (faderHandler != null)
            {
                faderHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsureScreenHandlerReference()
        {
            if (screenHandler == null)
            {
                screenHandler = GetComponent<ScreenHandler>();
                if (screenHandler == null)
                {
                    screenHandler = GetComponentInChildren<ScreenHandler>(true);
                }
            }
            
            if (screenHandler != null)
            {
                screenHandler.SetLaunchpad(this);
            }
        }
        
        private void EnsureObjectHandlerReferences()
        {
            // If array is null or empty, try to find all ObjectHandlers
            if (objectHandlers == null || objectHandlers.Length == 0)
            {
                objectHandlers = GetComponentsInChildren<ObjectHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsureObjectHandlerReferences: Found {(objectHandlers != null ? objectHandlers.Length : 0)} ObjectHandler(s) via GetComponentsInChildren");
                
                // Set launchpad reference on all found handlers
                if (objectHandlers != null)
                {
                    for (int i = 0; i < objectHandlers.Length; i++)
                    {
                        if (objectHandlers[i] != null)
                        {
                            objectHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                return;
            }
            
            // If array exists but has null entries, try to populate them
            bool hasNulls = false;
            for (int i = 0; i < objectHandlers.Length; i++)
            {
                if (objectHandlers[i] == null)
                {
                    hasNulls = true;
                    break;
                }
            }
            
            if (hasNulls)
            {
                Debug.Log("[EnigmaLaunchpad] EnsureObjectHandlerReferences: Array has NULL entries, attempting to find handlers");
                ObjectHandler[] foundHandlers = GetComponentsInChildren<ObjectHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsureObjectHandlerReferences: GetComponentsInChildren returned {(foundHandlers != null ? foundHandlers.Length.ToString() : "null")} handler(s)");
                if (foundHandlers != null && foundHandlers.Length > 0)
                {
                    Debug.Log($"[EnigmaLaunchpad] EnsureObjectHandlerReferences: Replacing array with {foundHandlers.Length} found ObjectHandler(s)");
                    // Replace the array with found handlers
                    objectHandlers = foundHandlers;
                    
                    // Set launchpad reference on all found handlers
                    for (int i = 0; i < objectHandlers.Length; i++)
                    {
                        if (objectHandlers[i] != null)
                        {
                            objectHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[EnigmaLaunchpad] EnsureObjectHandlerReferences: No ObjectHandler components found in children. Ensure ObjectHandler GameObjects are active and are children of this GameObject.");
                }
            }
        }
        
        private void EnsureMaterialHandlerReferences()
        {
            // If array is null or empty, try to find all MaterialHandlers
            if (materialHandlers == null || materialHandlers.Length == 0)
            {
                materialHandlers = GetComponentsInChildren<MaterialHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsureMaterialHandlerReferences: Found {(materialHandlers != null ? materialHandlers.Length : 0)} MaterialHandler(s) via GetComponentsInChildren");
                
                // Set launchpad reference on all found handlers
                if (materialHandlers != null)
                {
                    for (int i = 0; i < materialHandlers.Length; i++)
                    {
                        if (materialHandlers[i] != null)
                        {
                            materialHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                return;
            }
            
            // If array exists but has null entries, try to populate them
            bool hasNulls = false;
            for (int i = 0; i < materialHandlers.Length; i++)
            {
                if (materialHandlers[i] == null)
                {
                    hasNulls = true;
                    break;
                }
            }
            
            if (hasNulls)
            {
                Debug.Log("[EnigmaLaunchpad] EnsureMaterialHandlerReferences: Array has NULL entries, attempting to find handlers");
                MaterialHandler[] foundHandlers = GetComponentsInChildren<MaterialHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsureMaterialHandlerReferences: GetComponentsInChildren returned {(foundHandlers != null ? foundHandlers.Length.ToString() : "null")} handler(s)");
                if (foundHandlers != null && foundHandlers.Length > 0)
                {
                    Debug.Log($"[EnigmaLaunchpad] EnsureMaterialHandlerReferences: Replacing array with {foundHandlers.Length} found MaterialHandler(s)");
                    // Replace the array with found handlers
                    materialHandlers = foundHandlers;
                    
                    // Set launchpad reference on all found handlers
                    for (int i = 0; i < materialHandlers.Length; i++)
                    {
                        if (materialHandlers[i] != null)
                        {
                            materialHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[EnigmaLaunchpad] EnsureMaterialHandlerReferences: No MaterialHandler components found in children. Ensure MaterialHandler GameObjects are active and are children of this GameObject.");
                }
            }
        }

        private void EnsurePropertyHandlerReferences()
        {
            // If array is null or empty, try to find all PropertyHandlers
            if (propertyHandlers == null || propertyHandlers.Length == 0)
            {
                propertyHandlers = GetComponentsInChildren<PropertyHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsurePropertyHandlerReferences: Found {(propertyHandlers != null ? propertyHandlers.Length : 0)} PropertyHandler(s) via GetComponentsInChildren");

                // Set launchpad reference on all found handlers
                if (propertyHandlers != null)
                {
                    for (int i = 0; i < propertyHandlers.Length; i++)
                    {
                        if (propertyHandlers[i] != null)
                        {
                            propertyHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                return;
            }

            // If array exists but has null entries, try to populate them
            bool hasNulls = false;
            for (int i = 0; i < propertyHandlers.Length; i++)
            {
                if (propertyHandlers[i] == null)
                {
                    hasNulls = true;
                    break;
                }
            }

            if (hasNulls)
            {
                Debug.Log("[EnigmaLaunchpad] EnsurePropertyHandlerReferences: Array has NULL entries, attempting to find handlers");
                PropertyHandler[] foundHandlers = GetComponentsInChildren<PropertyHandler>(true);
                Debug.Log($"[EnigmaLaunchpad] EnsurePropertyHandlerReferences: GetComponentsInChildren returned {(foundHandlers != null ? foundHandlers.Length.ToString() : "null")} handler(s)");
                if (foundHandlers != null && foundHandlers.Length > 0)
                {
                    Debug.Log($"[EnigmaLaunchpad] EnsurePropertyHandlerReferences: Replacing array with {foundHandlers.Length} found PropertyHandler(s)");
                    // Replace the array with found handlers
                    propertyHandlers = foundHandlers;

                    // Set launchpad reference on all found handlers
                    for (int i = 0; i < propertyHandlers.Length; i++)
                    {
                        if (propertyHandlers[i] != null)
                        {
                            propertyHandlers[i].SetLaunchpad(this);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[EnigmaLaunchpad] EnsurePropertyHandlerReferences: No PropertyHandler components found in children. Ensure PropertyHandler GameObjects are active and are children of this GameObject.");
                }
            }
        }
        
        public ObjectHandler GetPrimaryObjectHandler()
        {
            if (objectHandlers == null)
            {
                return null;
            }
            
            for (int i = 0; i < objectHandlers.Length; i++)
            {
                ObjectHandler handler = objectHandlers[i];
                if (handler != null)
                {
                    return handler;
                }
            }
            
            return null;
        }
        
        public MochieHandler GetMochiHandler()
        {
            return mochiHandler;
        }
        
        public ObjectHandler GetObjectHandlerForFolder(int folderIndex)
        {
            if (objectHandlers != null)
            {
                for (int i = 0; i < objectHandlers.Length; i++)
                {
                    ObjectHandler handler = objectHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }
                    
                    if (ObjectHandler.GetFolderIndex(handler) == folderIndex)
                    {
                        return handler;
                    }
                }
            }
            
            return GetPrimaryObjectHandler();
        }
        
        private MaterialHandler GetPrimaryMaterialHandler()
        {
            if (materialHandlers == null)
            {
                return null;
            }
            
            for (int i = 0; i < materialHandlers.Length; i++)
            {
                MaterialHandler handler = materialHandlers[i];
                if (handler != null)
                {
                    return handler;
                }
            }
            
            return null;
        }
        
        public MaterialHandler GetMaterialHandlerForFolder(int folderIndex)
        {
            if (materialHandlers != null)
            {
                for (int i = 0; i < materialHandlers.Length; i++)
                {
                    MaterialHandler handler = materialHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }
                    
                    if (MaterialHandler.GetFolderIndex(handler) == folderIndex)
                    {
                        return handler;
                    }
                }
            }

            return GetPrimaryMaterialHandler();
        }

        private PropertyHandler GetPrimaryPropertyHandler()
        {
            if (propertyHandlers == null)
            {
                return null;
            }

            for (int i = 0; i < propertyHandlers.Length; i++)
            {
                PropertyHandler handler = propertyHandlers[i];
                if (handler != null)
                {
                    return handler;
                }
            }

            return null;
        }

        public PropertyHandler GetPropertyHandlerForFolder(int folderIndex)
        {
            if (propertyHandlers != null)
            {
                for (int i = 0; i < propertyHandlers.Length; i++)
                {
                    PropertyHandler handler = propertyHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    if (PropertyHandler.GetFolderIndex(handler) == folderIndex)
                    {
                        return handler;
                    }
                }
            }

            return GetPrimaryPropertyHandler();
        }
        
        public ShaderHandler GetShaderHandlerForFolder(int folderIndex)
        {
            if (shaderHandlers != null)
            {
                for (int i = 0; i < shaderHandlers.Length; i++)
                {
                    ShaderHandler handler = shaderHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    if (handler.folderIndex == folderIndex)
                    {
                        return handler;
                    }
                }
            }

            return null;
        }
        
        public ToggleFolderType GetFolderTypeForIndex(int folderIdx)
        {
            if (folderTypes == null || folderIdx < 0 || folderIdx >= folderTypes.Length)
            {
                return ToggleFolderType.Objects;
            }
            
            return folderTypes[folderIdx];
        }
        
        public int GetFolderCount()
        {
            if (folderNames != null)
            {
                return folderNames.Length;
            }
            
            return folderTypes != null ? folderTypes.Length : 0;
        }
        
        public int GetFolderEntryCount(int folderIdx)
        {
            if (folderEntryCounts == null || folderIdx < 0 || folderIdx >= folderEntryCounts.Length)
            {
                return 0;
            }
            
            return folderEntryCounts[folderIdx];
        }
        
        public bool IsFolderExclusive(int folderIdx)
        {
            return folderExclusive != null
            && folderIdx >= 0
            && folderIdx < folderExclusive.Length
            && folderExclusive[folderIdx];
        }
        
        private void EnsureWhitelistInitialized()
        {
            // Don't initialize whitelist if it's disabled
            if (!whitelistEnabled)
            {
                return;
            }
            
            if (whitelistInitialized)
            {
                return;
            }
            
            NormalizeWhitelistEntries();
        }
        
        private void NormalizeWhitelistEntries()
        {
            whitelistInitialized = true;
            
            string[] sourceUsernames = GetWhitelistSourceEntries();
            
            if (sourceUsernames == null || sourceUsernames.Length == 0)
            {
                normalizedAuthorizedUsernames = new string[0];
                // Push empty list to downstream whitelists in router mode
                PushWhitelistToDownstreamSystems(new string[0]);
                NotifyFaderHandlerAuthorizationChanged();
                return;
            }
            
            string[] scratch = new string[sourceUsernames.Length];
            int count = 0;
            
            for (int i = 0; i < sourceUsernames.Length; i++)
            {
                string normalized = NormalizeUsername(sourceUsernames[i]);
                if (string.IsNullOrEmpty(normalized))
                continue;
                
                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    if (scratch[j] == normalized)
                    {
                        duplicate = true;
                        break;
                    }
                }
                
                if (duplicate)
                continue;
                
                scratch[count++] = normalized;
            }
            
            normalizedAuthorizedUsernames = new string[count];
            for (int i = 0; i < count; i++)
            {
                normalizedAuthorizedUsernames[i] = scratch[i];
            }
            
            // Build deduplicated list preserving original case for downstream systems
            string[] deduplicatedList = BuildDeduplicatedList(sourceUsernames, count);
            
            // Push to downstream systems (router mode)
            PushWhitelistToDownstreamSystems(deduplicatedList);
            
            NotifyFaderHandlerAuthorizationChanged();
        }
        
        /// <summary>
        /// Builds a deduplicated list preserving original case, used for pushing to downstream systems.
        /// </summary>
        private string[] BuildDeduplicatedList(string[] sourceUsernames, int expectedCount)
        {
            string[] resultList = new string[expectedCount];
            string[] resultListNormalized = new string[expectedCount];
            int resultCount = 0;
            
            for (int i = 0; i < sourceUsernames.Length; i++)
            {
                string trimmed = sourceUsernames[i] != null ? sourceUsernames[i].Trim() : null;
                if (string.IsNullOrEmpty(trimmed))
                continue;
                
                // Check for duplicate using cached normalized values
                bool duplicate = false;
                string normalizedTrimmed = trimmed.ToLower();
                for (int j = 0; j < resultCount; j++)
                {
                    if (resultListNormalized[j] == normalizedTrimmed)
                    {
                        duplicate = true;
                        break;
                    }
                }
                
                if (duplicate || resultCount >= expectedCount)
                continue;
                
                resultList[resultCount] = trimmed;
                resultListNormalized[resultCount] = normalizedTrimmed;
                resultCount++;
            }
            
            // Resize to actual count
            string[] finalList = new string[resultCount];
            for (int i = 0; i < resultCount; i++)
            {
                finalList[i] = resultList[i];
            }
            
            return finalList;
        }
        
        /// <summary>
        /// Pushes the whitelist to downstream systems based on current source priority.
        /// OhGeezCmon → ProTV → Flatline. Only pushes to systems that are lower priority than the current source.
        /// </summary>
        private void PushWhitelistToDownstreamSystems(string[] usernames)
        {
            // Determine which is the current source
            bool sourceIsOhGeezCmon = ohGeezCmonAccessControl != null;
            bool sourceIsProTV = !sourceIsOhGeezCmon && proTVManagedWhitelist != null;
            bool sourceIsFlatline = !sourceIsOhGeezCmon && !sourceIsProTV && flatlineSync != null;
            
            // Push to ProTV if OhGeezCmon is the source
            if (sourceIsOhGeezCmon && proTVManagedWhitelist != null)
            {
                PushWhitelistToProTV(usernames);
            }
            
            // Push to Flatline if OhGeezCmon or ProTV is the source
            if ((sourceIsOhGeezCmon || sourceIsProTV) && flatlineSync != null)
            {
                PushWhitelistToFlatline(usernames);
            }
        }
        
        private void NotifyFaderHandlerAuthorizationChanged()
        {
            if (faderHandler != null)
            {
                faderHandler.UpdateHandColliderAuthorizationState();
            }
            
            UpdateVideoPlayerControlsAuthorizationState();
        }
        
        /// <summary>
        /// Updates the Video Player Controls GameObject visibility based on whitelist authorization.
        /// The GameObject is enabled for authorized users and disabled for unauthorized users.
        /// </summary>
        private void UpdateVideoPlayerControlsAuthorizationState()
        {
            // Find the Video Player Controls GameObject directly under the Enigma Launchpad transform
            Transform videoPlayerControlsTransform = transform.Find("Video Player Controls");
            if (videoPlayerControlsTransform == null)
            {
                // GameObject doesn't exist or has been renamed - this is not an error
                return;
            }
            
            GameObject videoPlayerControls = videoPlayerControlsTransform.gameObject;
            bool shouldEnable = CanLocalUserInteract();
            
            // Only update if the state is different to avoid unnecessary operations
            if (videoPlayerControls.activeSelf != shouldEnable)
            {
                videoPlayerControls.SetActive(shouldEnable);
                
                if (shouldEnable)
                {
                    Debug.Log("[EnigmaLaunchpad] Video Player Controls ENABLED for authorized user");
                }
                else
                {
                    Debug.Log("[EnigmaLaunchpad] Video Player Controls DISABLED for unauthorized user");
                }
            }
        }
        
        private string[] GetWhitelistSourceEntries()
        {
            // Priority 1: OhGeezCmon Access Control (highest priority, routes to ProTV and Flatline)
            if (ohGeezCmonAccessControl != null)
            {
                string[] runtimeEntries = TryGetOhGeezRuntimeAdmins();
                if (runtimeEntries != null && runtimeEntries.Length > 0)
                {
                    Debug.Log("[EnigmaLaunchpad] Using OhGeezCmon Access Control runtime admins: " + FormatUsernameListForLogging(runtimeEntries));
                    isWaitingForOhGeezSync = false;
                    return runtimeEntries;
                }

                // If we got null or empty array and haven't started waiting, initiate delayed retry
                if (!isWaitingForOhGeezSync && ohGeezSyncRetryCount == 0)
                {
                    Debug.Log("[EnigmaLaunchpad] OhGeezCmon Access Control assigned but fullAccessUsers not yet available. Will retry in background.");
                    isWaitingForOhGeezSync = true;
                    SendCustomEventDelayedSeconds(nameof(RetryOhGeezSync), OHGEEZ_SYNC_RETRY_DELAY);
                    // Return empty array while waiting for first sync
                    return new string[0];
                }
                else
                {
                    Debug.Log("[EnigmaLaunchpad] OhGeezCmon Access Control assigned but no runtime admins available.");
                    return new string[0];
                }
            }
            
            // Priority 2: ProTV TVManagedWhitelist (when OhGeezCmon not assigned, routes to Flatline)
            if (proTVManagedWhitelist != null)
            {
                string[] runtimeEntries = TryGetProTVAuthorizedList();
                
                // Also check if we can access the TVManager for implicit authorization
                UdonSharpBehaviour tvManager = GetProTVManager();
                bool hasTVManagerAccess = tvManager != null;
                
                if (runtimeEntries != null && runtimeEntries.Length > 0)
                {
                    Debug.Log("[EnigmaLaunchpad] Using ProTV TVManagedWhitelist authorizedList: " + FormatUsernameListForLogging(runtimeEntries));
                    isWaitingForProTVSync = false;
                    return runtimeEntries;
                }
                
                // If we have TVManager access, ProTV integration is successful even with empty list
                // (users may be authorized implicitly via master/first master status)
                if (hasTVManagerAccess)
                {
                    Debug.Log("[EnigmaLaunchpad] ProTV TVManager accessible - implicit authorization will be checked. Explicit authorizedList: " + FormatUsernameListForLogging(runtimeEntries));
                    isWaitingForProTVSync = false;
                    return runtimeEntries != null ? runtimeEntries : new string[0];
                }

                // If we got null or empty array and haven't started waiting, initiate delayed retry
                if (!isWaitingForProTVSync && proTVSyncRetryCount == 0)
                {
                    Debug.Log("[EnigmaLaunchpad] ProTV TVManagedWhitelist assigned but authorizedList not yet available. Will retry in background.");
                    isWaitingForProTVSync = true;
                    SendCustomEventDelayedSeconds(nameof(RetryProTVSync), PROTV_SYNC_RETRY_DELAY);
                    // Return empty array while waiting for first sync
                    return new string[0];
                }
                else
                {
                    Debug.Log("[EnigmaLaunchpad] ProTV TVManagedWhitelist assigned but no authorized users available.");
                    return new string[0];
                }
            }
            
            // Priority 3: Flatline FlatlineSync whitelist (when OhGeezCmon and ProTV not assigned)
            if (flatlineSync != null)
            {
                string[] runtimeEntries = TryGetFlatlineBakedWhitelist();
                if (runtimeEntries != null && runtimeEntries.Length > 0)
                {
                    Debug.Log("[EnigmaLaunchpad] Using Flatline bakedWhitelist: " + FormatUsernameListForLogging(runtimeEntries));
                    isWaitingForFlatlineSync = false;
                    return runtimeEntries;
                }

                // If we got null or empty array and haven't started waiting, initiate delayed retry
                // Note: Flatline may be downloading its whitelist from a URL, which can take time
                if (!isWaitingForFlatlineSync && flatlineSyncRetryCount == 0)
                {
                    Debug.Log("[EnigmaLaunchpad] Flatline FlatlineSync assigned but bakedWhitelist not yet available. Will retry in background (may be waiting for online whitelist download).");
                    isWaitingForFlatlineSync = true;
                    SendCustomEventDelayedSeconds(nameof(RetryFlatlineSync), FLATLINE_SYNC_RETRY_DELAY);
                    // Return empty array while waiting for first sync
                    return new string[0];
                }
                else
                {
                    Debug.Log("[EnigmaLaunchpad] Flatline FlatlineSync assigned but no whitelisted users available.");
                    return new string[0];
                }
            }

            Debug.Log("[EnigmaLaunchpad] Using manual authorizedUsernames array: " + FormatUsernameListForLogging(authorizedUsernames));
            return authorizedUsernames;
        }
        
        public void RetryOhGeezSync()
        {
            if (!isWaitingForOhGeezSync)
            {
                return;
            }
            
            ohGeezSyncRetryCount++;
            Debug.Log($"[EnigmaLaunchpad] RetryOhGeezSync attempt {ohGeezSyncRetryCount}/{MAX_OHGEEZ_SYNC_RETRIES}");
            
            string[] runtimeEntries = TryGetOhGeezRuntimeAdmins();
            if (runtimeEntries != null && runtimeEntries.Length > 0)
            {
                Debug.Log("[EnigmaLaunchpad] Successfully retrieved OhGeezCmon Access Control runtime admins: " + FormatUsernameListForLogging(runtimeEntries));
                isWaitingForOhGeezSync = false;
                
                // Track the sync version and start monitoring for changes
                TrackOhGeezSyncVersion();
                SendCustomEventDelayedSeconds(nameof(CheckOhGeezSyncVersion), OHGEEZ_SYNC_CHECK_INTERVAL);
                
                // Re-initialize whitelist with the new data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
                return;
            }
            
            // If still empty and we haven't exceeded max retries, schedule another attempt
            if (ohGeezSyncRetryCount < MAX_OHGEEZ_SYNC_RETRIES)
            {
                Debug.Log($"[EnigmaLaunchpad] OhGeezCmon data still not available, will retry again in {OHGEEZ_SYNC_RETRY_DELAY}s");
                SendCustomEventDelayedSeconds(nameof(RetryOhGeezSync), OHGEEZ_SYNC_RETRY_DELAY);
            }
            else
            {
                Debug.LogWarning("[EnigmaLaunchpad] Max retries reached for OhGeezCmon Access Control sync.");
                isWaitingForOhGeezSync = false;
            }
        }
        
        public void CheckOhGeezSyncVersion()
        {
            if (ohGeezCmonAccessControl == null)
            {
                return;
            }
            
            UdonSharpBehaviour behaviour = GetOhGeezAccessControlBehaviour();
            if (behaviour == null)
            {
                return;
            }
            
            // Get the current syncedVersion from AccessControlManager
            object versionObj = behaviour.GetProgramVariable("syncedVersion");
            if (versionObj == null)
            {
                // Schedule next check
                SendCustomEventDelayedSeconds(nameof(CheckOhGeezSyncVersion), OHGEEZ_SYNC_CHECK_INTERVAL);
                return;
            }
            
            int currentVersion = (int)versionObj;
            
            // If version changed, update the whitelist
            if (currentVersion != lastKnownOhGeezSyncVersion && lastKnownOhGeezSyncVersion != -1)
            {
                Debug.Log($"[EnigmaLaunchpad] OhGeezCmon Access Control list changed (version {lastKnownOhGeezSyncVersion} -> {currentVersion}), updating whitelist");
                lastKnownOhGeezSyncVersion = currentVersion;
                
                // Re-initialize whitelist with the updated data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
            }
            else if (lastKnownOhGeezSyncVersion == -1)
            {
                // First time tracking, just store the version
                lastKnownOhGeezSyncVersion = currentVersion;
            }
            
            // Schedule next check
            SendCustomEventDelayedSeconds(nameof(CheckOhGeezSyncVersion), OHGEEZ_SYNC_CHECK_INTERVAL);
        }
        
        private void TrackOhGeezSyncVersion()
        {
            if (ohGeezCmonAccessControl == null)
            {
                return;
            }
            
            UdonSharpBehaviour behaviour = GetOhGeezAccessControlBehaviour();
            if (behaviour == null)
            {
                return;
            }
            
            object versionObj = behaviour.GetProgramVariable("syncedVersion");
            if (versionObj != null)
            {
                lastKnownOhGeezSyncVersion = (int)versionObj;
                Debug.Log($"[EnigmaLaunchpad] Now tracking OhGeezCmon Access Control sync version: {lastKnownOhGeezSyncVersion}");
            }
        }
        
        private string[] TryGetOhGeezRuntimeAdmins()
        {
            UdonSharpBehaviour behaviour = GetOhGeezAccessControlBehaviour();
            if (behaviour == null)
            {
                return null;
            }
            
            // Get the fullAccessUsers array (simple string array exposed by AccessControlManager)
            // The AccessControlManager internally uses VRC.SDK3.Data.DataList for the "admins" variable
            // but properly exposes it as a [UdonSynced] string[] "fullAccessUsers" for compatibility.
            object syncedArrayObject = behaviour.GetProgramVariable("fullAccessUsers");
            if (syncedArrayObject == null)
            {
                return null;
            }
            
            // Direct cast without type check - AccessControlManager guarantees this is string[]
            // UdonSharp compiler has issues with GetType(), 'as', and 'is' operators for type checking
            // Since this code only runs when whitelistEnabled=true and relies on known integration,
            // the direct cast is safe and avoids all UdonSharp compiler issues
            string[] runtimeAdmins = CloneStringArray((string[])syncedArrayObject);
            Debug.Log("[EnigmaLaunchpad] Retrieved fullAccessUsers from OhGeezCmon Access Control: " + FormatUsernameListForLogging(runtimeAdmins));
            return runtimeAdmins;
        }
        
        private UdonSharpBehaviour GetOhGeezAccessControlBehaviour()
        {
            if (ohGeezCmonAccessControl == null)
            {
                ohGeezAccessControlBehaviour = null;
                return null;
            }
            
            if (ohGeezAccessControlBehaviour != ohGeezCmonAccessControl)
            {
                ohGeezAccessControlBehaviour = ohGeezCmonAccessControl;
            }
            
            return ohGeezAccessControlBehaviour;
        }
        
        // ProTV TVManagedWhitelist integration methods
        
        public void RetryProTVSync()
        {
            if (!isWaitingForProTVSync)
            {
                return;
            }
            
            proTVSyncRetryCount++;
            Debug.Log($"[EnigmaLaunchpad] RetryProTVSync attempt {proTVSyncRetryCount}/{MAX_PROTV_SYNC_RETRIES}");
            
            string[] runtimeEntries = TryGetProTVAuthorizedList();
            
            // Check if we can get the TVManager reference (for implicit authorization support)
            UdonSharpBehaviour tvManager = GetProTVManager();
            bool hasTVManagerAccess = tvManager != null;
            
            if ((runtimeEntries != null && runtimeEntries.Length > 0) || hasTVManagerAccess)
            {
                if (hasTVManagerAccess)
                {
                    Debug.Log("[EnigmaLaunchpad] Successfully accessed ProTV TVManager - implicit authorization will be supported");
                }
                if (runtimeEntries != null && runtimeEntries.Length > 0)
                {
                    Debug.Log("[EnigmaLaunchpad] Successfully retrieved ProTV TVManagedWhitelist authorizedList: " + FormatUsernameListForLogging(runtimeEntries));
                }
                isWaitingForProTVSync = false;
                
                // Track the authorized count and start monitoring for changes
                TrackProTVAuthorizedCount();
                SendCustomEventDelayedSeconds(nameof(CheckProTVSyncVersion), PROTV_SYNC_CHECK_INTERVAL);
                
                // Re-initialize whitelist with the new data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
                
                // Notify fader handler and update authorization state
                NotifyFaderHandlerAuthorizationChanged();
                return;
            }
            
            // If still empty and we haven't exceeded max retries, schedule another attempt
            if (proTVSyncRetryCount < MAX_PROTV_SYNC_RETRIES)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV data still not available, will retry again in {PROTV_SYNC_RETRY_DELAY}s");
                SendCustomEventDelayedSeconds(nameof(RetryProTVSync), PROTV_SYNC_RETRY_DELAY);
            }
            else
            {
                Debug.LogWarning("[EnigmaLaunchpad] Max retries reached for ProTV TVManagedWhitelist sync.");
                isWaitingForProTVSync = false;
                
                // Still notify in case we need to update UI state
                NotifyFaderHandlerAuthorizationChanged();
            }
        }
        
        public void CheckProTVSyncVersion()
        {
            if (proTVManagedWhitelist == null)
            {
                return;
            }
            
            // If OhGeezCmon is assigned, we are in router mode - don't pull from ProTV
            if (ohGeezCmonAccessControl != null)
            {
                return;
            }
            
            // Get the current authorizedList from TVManagedWhitelist
            object listObj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
            if (listObj == null)
            {
                // Schedule next check
                SendCustomEventDelayedSeconds(nameof(CheckProTVSyncVersion), PROTV_SYNC_CHECK_INTERVAL);
                return;
            }
            
            string[] currentList = (string[])listObj;
            int currentCount = GetNonEmptyCount(currentList);
            
            // If count changed, update the whitelist
            if (currentCount != lastKnownProTVAuthorizedCount && lastKnownProTVAuthorizedCount != -1)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV TVManagedWhitelist changed (count {lastKnownProTVAuthorizedCount} -> {currentCount}), updating whitelist");
                lastKnownProTVAuthorizedCount = currentCount;
                
                // Re-initialize whitelist with the updated data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
            }
            else if (lastKnownProTVAuthorizedCount == -1)
            {
                // First time tracking, just store the count
                lastKnownProTVAuthorizedCount = currentCount;
            }
            
            // Schedule next check
            SendCustomEventDelayedSeconds(nameof(CheckProTVSyncVersion), PROTV_SYNC_CHECK_INTERVAL);
        }
        
        private void TrackProTVAuthorizedCount()
        {
            if (proTVManagedWhitelist == null)
            {
                return;
            }
            
            object listObj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
            if (listObj != null)
            {
                string[] currentList = (string[])listObj;
                lastKnownProTVAuthorizedCount = GetNonEmptyCount(currentList);
                Debug.Log($"[EnigmaLaunchpad] Now tracking ProTV TVManagedWhitelist authorized count: {lastKnownProTVAuthorizedCount}");
            }
        }
        
        private string[] TryGetProTVAuthorizedList()
        {
            if (proTVManagedWhitelist == null)
            {
                return null;
            }
            
            // Get the authorizedList array from TVManagedWhitelist
            // This is a [UdonSynced] string[] that contains currently authorized usernames
            object syncedArrayObject = proTVManagedWhitelist.GetProgramVariable("authorizedList");
            if (syncedArrayObject == null)
            {
                return null;
            }
            
            // Direct cast - TVManagedWhitelist guarantees this is string[]
            string[] authorizedList = CloneStringArray((string[])syncedArrayObject);
            Debug.Log("[EnigmaLaunchpad] Retrieved authorizedList from ProTV TVManagedWhitelist: " + FormatUsernameListForLogging(authorizedList));
            return authorizedList;
        }
        
        /// <summary>
        /// Gets the ProTV TVManager reference from the TVManagedWhitelist.
        /// The TVManager provides the full authorization check including implicit authorization.
        /// </summary>
        private UdonSharpBehaviour GetProTVManager()
        {
            if (proTVManagerResolved)
            {
                return proTVManager;
            }
            
            if (proTVManagedWhitelist == null)
            {
                proTVManagerResolved = true;
                proTVManager = null;
                return null;
            }
            
            // TVManagedWhitelist extends TVAuthPlugin which has a 'tv' field referencing the TVManager
            object tvObj = proTVManagedWhitelist.GetProgramVariable("tv");
            if (tvObj != null)
            {
                proTVManager = (UdonSharpBehaviour)tvObj;
                Debug.Log("[EnigmaLaunchpad] Successfully resolved ProTV TVManager reference from TVManagedWhitelist");
            }
            else
            {
                Debug.LogWarning("[EnigmaLaunchpad] Could not resolve TVManager reference from TVManagedWhitelist - 'tv' field is null");
                proTVManager = null;
            }
            
            proTVManagerResolved = true;
            return proTVManager;
        }
        
        /// <summary>
        /// Checks if a player is authorized via ProTV's full authorization system.
        /// This includes both explicit (authorizedList) and implicit (master, first master) authorization.
        /// </summary>
        /// <param name="player">The player to check authorization for</param>
        /// <returns>True if ProTV considers the player authorized, false otherwise or if check cannot be performed</returns>
        private bool IsPlayerProTVAuthorized(VRCPlayerApi player)
        {
            if (player == null)
            {
                return false;
            }
            
            UdonSharpBehaviour tvManager = GetProTVManager();
            if (tvManager == null)
            {
                // Fall back to checking the explicit list
                return IsPlayerInProTVExplicitList(player);
            }
            
            // Replicate ProTV's implicit authorization logic
            // Get allowMasterControl
            object allowMasterObj = tvManager.GetProgramVariable("allowMasterControl");
            bool allowMasterControl = allowMasterObj != null && (bool)allowMasterObj;
            
            // Get allowFirstMasterControl
            object allowFirstMasterObj = tvManager.GetProgramVariable("allowFirstMasterControl");
            bool allowFirstMasterControl = allowFirstMasterObj != null && (bool)allowFirstMasterObj;
            
            // Get firstMaster
            object firstMasterObj = tvManager.GetProgramVariable("firstMaster");
            string firstMaster = firstMasterObj != null ? (string)firstMasterObj : null;
            
            // Get syncToOwner - if false, everyone is authorized
            object syncToOwnerObj = tvManager.GetProgramVariable("syncToOwner");
            bool syncToOwner = syncToOwnerObj == null || (bool)syncToOwnerObj; // default to true if not found
            
            if (!syncToOwner)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV syncToOwner is false - player {player.displayName} is authorized");
                return true;
            }
            
            // Check implicit authorization (master, first master)
            bool implicitlyAuthorized = (allowMasterControl && player.isMaster) || 
                                         (allowFirstMasterControl && !string.IsNullOrEmpty(firstMaster) && player.displayName == firstMaster);
            
            if (implicitlyAuthorized)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV implicit authorization: player {player.displayName} is authorized (isMaster={player.isMaster}, allowMasterControl={allowMasterControl}, firstMaster={firstMaster}, allowFirstMasterControl={allowFirstMasterControl})");
                return true;
            }
            
            // Check instance owner authorization (only when instanceOwnerIsSuper is enabled)
            object instanceOwnerIsSuperObj = tvManager.GetProgramVariable("instanceOwnerIsSuper");
            bool instanceOwnerIsSuper = instanceOwnerIsSuperObj != null && (bool)instanceOwnerIsSuperObj;
            
            if (instanceOwnerIsSuper && player.isInstanceOwner)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV: player {player.displayName} is instance owner and instanceOwnerIsSuper is enabled - authorized");
                return true;
            }
            
            // Fall back to checking the explicit list
            bool explicitlyAuthorized = IsPlayerInProTVExplicitList(player);
            if (explicitlyAuthorized)
            {
                Debug.Log($"[EnigmaLaunchpad] ProTV explicit authorization: player {player.displayName} is in authorizedList");
            }
            
            return explicitlyAuthorized;
        }
        
        /// <summary>
        /// Checks if a player is in ProTV's explicit authorizedList.
        /// </summary>
        private bool IsPlayerInProTVExplicitList(VRCPlayerApi player)
        {
            if (proTVManagedWhitelist == null || player == null)
            {
                return false;
            }
            
            object syncedArrayObject = proTVManagedWhitelist.GetProgramVariable("authorizedList");
            if (syncedArrayObject == null)
            {
                return false;
            }
            
            string[] authorizedList = (string[])syncedArrayObject;
            string playerName = player.displayName;
            
            for (int i = 0; i < authorizedList.Length; i++)
            {
                if (authorizedList[i] == playerName)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private int GetNonEmptyCount(string[] arr)
        {
            if (arr == null)
            {
                return 0;
            }
            
            int count = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (!string.IsNullOrEmpty(arr[i]))
                {
                    count++;
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Pushes the current master whitelist to ProTV TVManagedWhitelist.
        /// Called when OhGeezCmon updates its whitelist and both systems are assigned.
        /// </summary>
        private void PushWhitelistToProTV(string[] usernames)
        {
            if (proTVManagedWhitelist == null)
            {
                return;
            }
            
            Debug.Log("[EnigmaLaunchpad] Pushing whitelist to ProTV TVManagedWhitelist: " + FormatUsernameListForLogging(usernames));
            
            // Set the authorizedList directly - TVManagedWhitelist will handle serialization
            proTVManagedWhitelist.SetProgramVariable("authorizedList", usernames);
            proTVManagedWhitelist.SendCustomEvent("RequestSerialization");
        }
        
        // Flatline FlatlineSync integration methods
        
        public void RetryFlatlineSync()
        {
            if (!isWaitingForFlatlineSync)
            {
                return;
            }
            
            flatlineSyncRetryCount++;
            Debug.Log($"[EnigmaLaunchpad] RetryFlatlineSync attempt {flatlineSyncRetryCount}/{MAX_FLATLINE_SYNC_RETRIES}");
            
            string[] runtimeEntries = TryGetFlatlineBakedWhitelist();
            if (runtimeEntries != null && runtimeEntries.Length > 0)
            {
                Debug.Log("[EnigmaLaunchpad] Successfully retrieved Flatline bakedWhitelist: " + FormatUsernameListForLogging(runtimeEntries));
                isWaitingForFlatlineSync = false;
                
                // Track the whitelist count and start monitoring for changes
                TrackFlatlineWhitelistCount();
                SendCustomEventDelayedSeconds(nameof(CheckFlatlineSyncVersion), FLATLINE_SYNC_CHECK_INTERVAL);
                
                // Re-initialize whitelist with the new data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
                return;
            }
            
            // If still empty and we haven't exceeded max retries, schedule another attempt
            // Note: Flatline may be downloading its whitelist from a URL, which can take time
            if (flatlineSyncRetryCount < MAX_FLATLINE_SYNC_RETRIES)
            {
                Debug.Log($"[EnigmaLaunchpad] Flatline data still not available (may be waiting for online whitelist download), will retry again in {FLATLINE_SYNC_RETRY_DELAY}s (attempt {flatlineSyncRetryCount}/{MAX_FLATLINE_SYNC_RETRIES})");
                SendCustomEventDelayedSeconds(nameof(RetryFlatlineSync), FLATLINE_SYNC_RETRY_DELAY);
            }
            else
            {
                Debug.LogWarning("[EnigmaLaunchpad] Max retries reached for Flatline FlatlineSync sync. If using an online whitelist, ensure the URL is accessible.");
                isWaitingForFlatlineSync = false;
            }
        }
        
        public void CheckFlatlineSyncVersion()
        {
            if (flatlineSync == null)
            {
                return;
            }
            
            // If OhGeezCmon or ProTV is assigned, we are in router mode - don't pull from Flatline
            if (ohGeezCmonAccessControl != null || proTVManagedWhitelist != null)
            {
                return;
            }
            
            // Get the current bakedWhitelist from FlatlineSync
            object listObj = flatlineSync.GetProgramVariable("bakedWhitelist");
            if (listObj == null)
            {
                // Schedule next check
                SendCustomEventDelayedSeconds(nameof(CheckFlatlineSyncVersion), FLATLINE_SYNC_CHECK_INTERVAL);
                return;
            }
            
            string[] currentList = (string[])listObj;
            int currentCount = GetNonEmptyCount(currentList);
            
            // If count changed, update the whitelist
            if (currentCount != lastKnownFlatlineWhitelistCount && lastKnownFlatlineWhitelistCount != -1)
            {
                Debug.Log($"[EnigmaLaunchpad] Flatline bakedWhitelist changed (count {lastKnownFlatlineWhitelistCount} -> {currentCount}), updating whitelist");
                lastKnownFlatlineWhitelistCount = currentCount;
                
                // Re-initialize whitelist with the updated data
                whitelistInitialized = false;
                NormalizeWhitelistEntries();
            }
            else if (lastKnownFlatlineWhitelistCount == -1)
            {
                // First time tracking, just store the count
                lastKnownFlatlineWhitelistCount = currentCount;
            }
            
            // Schedule next check
            SendCustomEventDelayedSeconds(nameof(CheckFlatlineSyncVersion), FLATLINE_SYNC_CHECK_INTERVAL);
        }
        
        private void TrackFlatlineWhitelistCount()
        {
            if (flatlineSync == null)
            {
                return;
            }
            
            object listObj = flatlineSync.GetProgramVariable("bakedWhitelist");
            if (listObj != null)
            {
                string[] currentList = (string[])listObj;
                lastKnownFlatlineWhitelistCount = GetNonEmptyCount(currentList);
                Debug.Log($"[EnigmaLaunchpad] Now tracking Flatline bakedWhitelist count: {lastKnownFlatlineWhitelistCount}");
            }
        }
        
        private string[] TryGetFlatlineBakedWhitelist()
        {
            if (flatlineSync == null)
            {
                return null;
            }
            
            // Get the bakedWhitelist array from FlatlineSync
            // This is a string[] that contains whitelisted usernames
            object syncedArrayObject = flatlineSync.GetProgramVariable("bakedWhitelist");
            if (syncedArrayObject == null)
            {
                return null;
            }
            
            // Direct cast - FlatlineSync guarantees this is string[]
            string[] bakedWhitelist = CloneStringArray((string[])syncedArrayObject);
            Debug.Log("[EnigmaLaunchpad] Retrieved bakedWhitelist from Flatline FlatlineSync: " + FormatUsernameListForLogging(bakedWhitelist));
            return bakedWhitelist;
        }
        
        /// <summary>
        /// Pushes the current master whitelist to Flatline FlatlineSync.
        /// Called when a higher-priority system updates its whitelist and Flatline is assigned.
        /// </summary>
        private void PushWhitelistToFlatline(string[] usernames)
        {
            if (flatlineSync == null)
            {
                return;
            }
            
            Debug.Log("[EnigmaLaunchpad] Pushing whitelist to Flatline FlatlineSync: " + FormatUsernameListForLogging(usernames));
            
            // Set the bakedWhitelist and enable external whitelist mode
            flatlineSync.SetProgramVariable("bakedWhitelist", usernames);
            flatlineSync.SetProgramVariable("useExternalWhitelist", true);
            
            // Call _CheckWhitelist to re-evaluate local player authorization
            flatlineSync.SendCustomEvent("_CheckWhitelist");
        }
        
        private string[] CloneStringArray(string[] source)
        {
            if (source == null)
            return null;
            
            string[] clone = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                clone[i] = source[i];
            }
            
            return clone;
        }
        
        private string NormalizeUsername(string value)
        {
            if (string.IsNullOrEmpty(value))
            return string.Empty;

            return value.Trim().ToLower();
        }

        private string FormatUsernameListForLogging(string[] values)
        {
            if (values == null)
            return "(null)";

            if (values.Length == 0)
            return "(empty)";

            string[] sanitized = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrEmpty(values[i]))
                {
                    sanitized[i] = "(blank)";
                }
                else
                {
                    sanitized[i] = values[i];
                }
            }

            return string.Join(", ", sanitized);
        }
        
        /// <summary>
        /// Determines whether the local user is permitted to interact with the launchpad.
        /// </summary>
        /// <returns>
        /// <c>true</c> when the whitelist is disabled or the local user matches a configured username;
        /// otherwise, <c>false</c>. When the whitelist is enabled but empty, interaction is denied.
        /// </returns>
        public bool CanLocalUserInteract()
        {
            EnsureWhitelistInitialized();
            
            if (!whitelistEnabled)
            return true;
            
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            return false;
            
            if (instanceOwnerAlwaysHasAccess && localPlayer.isInstanceOwner)
            return true;
            
            // When ProTV is the whitelist source (and OhGeezCmon is not), use ProTV's full authorization
            // This includes implicit authorization (master, first master) not just the explicit authorizedList
            if (proTVManagedWhitelist != null && ohGeezCmonAccessControl == null)
            {
                return IsPlayerProTVAuthorized(localPlayer);
            }
            
            if (normalizedAuthorizedUsernames == null || normalizedAuthorizedUsernames.Length == 0)
            return false;
            
            string normalizedLocal = NormalizeUsername(localPlayer.displayName);
            if (string.IsNullOrEmpty(normalizedLocal))
            return false;
            
            for (int i = 0; i < normalizedAuthorizedUsernames.Length; i++)
            {
                if (normalizedAuthorizedUsernames[i] == normalizedLocal)
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks whether the specified player is whitelisted.
        /// </summary>
        /// <param name="player">The player to check</param>
        /// <returns>
        /// <c>true</c> when the whitelist is disabled or the player matches a configured username;
        /// otherwise, <c>false</c>.
        /// </returns>
        public bool IsPlayerWhitelisted(VRCPlayerApi player)
        {
            EnsureWhitelistInitialized();
            
            if (!whitelistEnabled)
            return true;
            
            if (player == null)
            return false;
            
            if (instanceOwnerAlwaysHasAccess && player.isInstanceOwner)
            return true;
            
            // When ProTV is the whitelist source (and OhGeezCmon is not), use ProTV's full authorization
            // This includes implicit authorization (master, first master) not just the explicit authorizedList
            if (proTVManagedWhitelist != null && ohGeezCmonAccessControl == null)
            {
                return IsPlayerProTVAuthorized(player);
            }
            
            if (normalizedAuthorizedUsernames == null || normalizedAuthorizedUsernames.Length == 0)
            return false;
            
            string normalizedName = NormalizeUsername(player.displayName);
            if (string.IsNullOrEmpty(normalizedName))
            return false;
            
            for (int i = 0; i < normalizedAuthorizedUsernames.Length; i++)
            {
                if (normalizedAuthorizedUsernames[i] == normalizedName)
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Initializes folder configuration arrays (folderExclusive, folderEntryCounts).
        /// This method must be called before handlers initialize to ensure read-only consumption.
        /// Handlers must never modify these arrays.
        /// </summary>
        private void InitializeConfiguration()
        {
            int folderCount = GetFolderCount();
            
            // Initialize folderExclusive array with proper size
            if (folderExclusive == null || folderExclusive.Length != folderCount)
            {
                bool[] newEx = new bool[folderCount];
                if (folderExclusive != null)
                {
                    int copyExclusive = Mathf.Min(folderCount, folderExclusive.Length);
                    for (int i = 0; i < copyExclusive; i++) newEx[i] = folderExclusive[i];
                }
                folderExclusive = newEx;
            }
            
            // Initialize folderEntryCounts array with proper size
            if (folderEntryCounts == null || folderEntryCounts.Length != folderCount)
            {
                int[] newCounts = new int[folderCount];
                if (folderEntryCounts != null)
                {
                    int copyCounts = Mathf.Min(folderCount, folderEntryCounts.Length);
                    for (int i = 0; i < copyCounts; i++) newCounts[i] = folderEntryCounts[i];
                }
                folderEntryCounts = newCounts;
            }
            
            // Clamp negative counts and set special folder types
            for (int i = 0; i < folderCount; i++)
            {
                if (folderEntryCounts[i] < 0) folderEntryCounts[i] = 0;
                ToggleFolderType folderType = GetFolderTypeForIndex(i);
                if (folderType != ToggleFolderType.Objects)
                {
                    // Don't override folderEntryCounts for Skybox/Mochie - they manage their own entry counts
                    // Skybox uses skyboxMaterials.Length, Mochie uses fixed page layout
                    if (folderType == ToggleFolderType.Stats)
                    {
                        if (folderExclusive != null && i < folderExclusive.Length)
                        folderExclusive[i] = false;
                    }
                }
            }
        }
        
        public void Start()
        {
            Debug.Log("[EnigmaLaunchpad] Start() beginning initialization");
            
            EnsureWhitelistInitialized();
            Debug.Log("[EnigmaLaunchpad] Whitelist initialized");
            
            EnsureButtonHandlerReferences();
            Debug.Log($"[EnigmaLaunchpad] Button handlers: {(buttonHandlers != null ? buttonHandlers.Length.ToString() : "null")}");
            
            // Initialize Mochie handler with error handling
            EnsureMochieHandlerReference();
            MochieHandler handler = GetMochiHandler();
            Debug.Log($"[EnigmaLaunchpad] Mochie handler reference: {(handler != null ? "found" : "NULL")}");
            
            if (handler != null)
            {
                Debug.Log("[EnigmaLaunchpad] Calling handler.InitializeMaterialsIfNeeded via SendCustomEvent");
                handler.SendCustomEvent("InitializeMaterialsIfNeeded");
                bool mochieInitOk = handler.lastInitializationResult;
                Debug.Log($"[EnigmaLaunchpad] Mochie init result: {mochieInitOk}");
                if (!mochieInitOk)
                {
                    Debug.LogWarning("[EnigmaLaunchpad] Mochie handler initialization failed - shader may be missing or renderer not configured");
                }
            }
            else
            {
                Debug.LogWarning("[EnigmaLaunchpad] Mochie handler is NULL - skipping initialization");
            }
            
            // Initialize Skybox handler with error handling
            EnsureSkyboxHandlerReference();
            Debug.Log($"[EnigmaLaunchpad] Skybox handler reference: {(skyboxHandler != null ? "found" : "NULL")}");
            if (skyboxHandler == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Skybox handler is null - skybox functionality will be disabled");
            }
            
            // Initialize Stats handler with error handling
            EnsureStatsHandlerReference();
            Debug.Log($"[EnigmaLaunchpad] Stats handler reference: {(statsHandler != null ? "found" : "NULL")}");
            if (statsHandler == null && HasStatsFolderConfigured())
            {
                Debug.LogWarning("[EnigmaLaunchpad] Stats handler is null but Stats folder is configured");
            }
            
            // Initialize configuration arrays before handlers need them
            Debug.Log("[EnigmaLaunchpad] Calling InitializeConfiguration");
            InitializeConfiguration();
            
            // Ensure handler references are valid (handles build serialization issues)
            Debug.Log("[EnigmaLaunchpad] Ensuring object handler references");
            EnsureObjectHandlerReferences();
            Debug.Log("[EnigmaLaunchpad] Ensuring material handler references");
            EnsureMaterialHandlerReferences();
            Debug.Log("[EnigmaLaunchpad] Ensuring property handler references");
            EnsurePropertyHandlerReferences();
            
            // Initialize object handlers with error handling
            if (objectHandlers != null)
            {
                Debug.Log($"[EnigmaLaunchpad] Initializing {objectHandlers.Length} object handlers");
                int nullHandlerCount = 0;
                for (int i = 0; i < objectHandlers.Length; i++)
                {
                    Debug.Log($"[EnigmaLaunchpad] Checking handler at index {i}: {(objectHandlers[i] != null ? "NOT NULL" : "NULL")}");
                    if (objectHandlers[i] != null)
                    {
                        Debug.Log($"[EnigmaLaunchpad] About to call SetLaunchpad on handler {i}");
                        objectHandlers[i].SetLaunchpad(this);
                        Debug.Log($"[EnigmaLaunchpad] About to call InitializeObjectRuntime on handler {i}");
                        objectHandlers[i].InitializeObjectRuntime();
                        Debug.Log($"[EnigmaLaunchpad] Completed initialization for handler {i}");
                    }
                    else
                    {
                        nullHandlerCount++;
                    }
                }
                if (nullHandlerCount > 0)
                {
                    Debug.LogWarning($"[EnigmaLaunchpad] Found {nullHandlerCount} NULL object handler(s) in objectHandlers array. Please assign ObjectHandler components in the Unity editor or remove empty array slots.");
                }
                Debug.Log("[EnigmaLaunchpad] Finished initializing all object handlers");
            }
            else
            {
                Debug.Log("[EnigmaLaunchpad] No object handlers to initialize");
            }
            
            // Initialize material handlers with error handling
            if (materialHandlers != null)
            {
                Debug.Log($"[EnigmaLaunchpad] Initializing {materialHandlers.Length} material handlers");
                int nullHandlerCount = 0;
                for (int i = 0; i < materialHandlers.Length; i++)
                {
                    Debug.Log($"[EnigmaLaunchpad] Checking material handler at index {i}: {(materialHandlers[i] != null ? "NOT NULL" : "NULL")}");
                    if (materialHandlers[i] != null)
                    {
                        Debug.Log($"[EnigmaLaunchpad] About to call SetLaunchpad on material handler {i}");
                        materialHandlers[i].SetLaunchpad(this);
                        Debug.Log($"[EnigmaLaunchpad] Completed SetLaunchpad for material handler {i}");
                        materialHandlers[i].InitializeMaterialRuntime();
                    }
                    else
                    {
                        nullHandlerCount++;
                    }
                }
                if (nullHandlerCount > 0)
                {
                    Debug.LogWarning($"[EnigmaLaunchpad] Found {nullHandlerCount} NULL material handler(s) in materialHandlers array. Please assign MaterialHandler components in the Unity editor or remove empty array slots.");
                }
                Debug.Log("[EnigmaLaunchpad] Finished initializing all material handlers");
            }
            else
            {
                Debug.Log("[EnigmaLaunchpad] No material handlers to initialize");
            }

            // Initialize property handlers with error handling
            if (propertyHandlers != null)
            {
                Debug.Log($"[EnigmaLaunchpad] Initializing {propertyHandlers.Length} property handlers");
                int nullHandlerCount = 0;
                for (int i = 0; i < propertyHandlers.Length; i++)
                {
                    Debug.Log($"[EnigmaLaunchpad] Checking property handler at index {i}: {(propertyHandlers[i] != null ? "NOT NULL" : "NULL")}");
                    if (propertyHandlers[i] != null)
                    {
                        propertyHandlers[i].SetLaunchpad(this);
                        propertyHandlers[i].InitializePropertyRuntime();
                    }
                    else
                    {
                        nullHandlerCount++;
                    }
                }
                if (nullHandlerCount > 0)
                {
                    Debug.LogWarning($"[EnigmaLaunchpad] Found {nullHandlerCount} NULL property handler(s) in propertyHandlers array. Please assign PropertyHandler components in the Unity editor or remove empty array slots.");
                }
                Debug.Log("[EnigmaLaunchpad] Finished initializing all property handlers");
            }
            else
            {
                Debug.Log("[EnigmaLaunchpad] No property handlers to initialize");
            }

            // Initialize stats handler runtime
            if (statsHandler != null)
            {
                if (HasStatsFolderConfigured())
                {
                    Debug.Log("[EnigmaLaunchpad] Initializing stats handler runtime");
                    statsHandler.InitializeStatsRuntime();
                }
            }
            
            // Initialize shader handlers runtime
            if (shaderHandlers != null && shaderHandlers.Length > 0)
            {
                Debug.Log($"[EnigmaLaunchpad] Initializing {shaderHandlers.Length} shader handlers");
                int nullHandlerCount = 0;
                for (int i = 0; i < shaderHandlers.Length; i++)
                {
                    if (shaderHandlers[i] == null)
                    {
                        nullHandlerCount++;
                        continue;
                    }
                    shaderHandlers[i].SetLaunchpad(this);
                    shaderHandlers[i].InitializeShaderRuntime();
                }
                if (nullHandlerCount > 0)
                {
                    Debug.LogWarning($"[EnigmaLaunchpad] Found {nullHandlerCount} NULL shader handler(s) in shaderHandlers array.");
                }
                Debug.Log("[EnigmaLaunchpad] Finished initializing all shader handlers");
            }
            else
            {
                Debug.Log("[EnigmaLaunchpad] No shader handlers to initialize");
            }
            
            // Initialize preset handler runtime
            EnsurePresetHandlerReference();
            if (presetHandler != null)
            {
                if (HasPresetFolderConfigured())
                {
                    Debug.Log("[EnigmaLaunchpad] Initializing preset handler runtime");
                    presetHandler.InitializePresetRuntime();
                }
            }
            
            // Initialize skybox handler runtime
            if (skyboxHandler != null)
            {
                Debug.Log("[EnigmaLaunchpad] Initializing skybox handler runtime");
                Debug.Log("[EnigmaLaunchpad] About to call InitializeSkyboxRuntime");
                skyboxHandler.InitializeSkyboxRuntime();
                Debug.Log("[EnigmaLaunchpad] Completed InitializeSkyboxRuntime");
            }
            else if (HasSkyboxFolderConfigured())
            {
                Debug.LogWarning("[EnigmaLaunchpad] Skybox folder configured but handler is null - auto-change will not work");
            }
            
            // Configure Page Display collider based on skybox configuration
            ConfigurePageDisplayCollider();
            
            // Initialize fader system handler runtime
            EnsureFaderHandlerReference();
            if (faderHandler != null)
            {
                Debug.Log("[EnigmaLaunchpad] Initializing fader system handler runtime");
                faderHandler.InitializeFaderRuntime();
                Debug.Log("[EnigmaLaunchpad] Completed InitializeFaderRuntime");
            }
            
            // Initialize screen handler runtime
            EnsureScreenHandlerReference();
            if (screenHandler != null)
            {
                Debug.Log("[EnigmaLaunchpad] Screen handler initialized");
                ApplyVideoScreenMaterial();
            }
            
            // Initialize June handlers with error handling
            EnsureJuneHandlerReferences();
            Debug.Log($"[EnigmaLaunchpad] June handlers: {(juneHandlers != null ? juneHandlers.Length.ToString() : "null")}");
            if (juneHandlers != null)
            {
                Debug.Log($"[EnigmaLaunchpad] Initializing {juneHandlers.Length} June handlers");
                int nullHandlerCount = 0;
                for (int i = 0; i < juneHandlers.Length; i++)
                {
                    Debug.Log($"[EnigmaLaunchpad] Checking June handler at index {i}: {(juneHandlers[i] != null ? "NOT NULL" : "NULL")}");
                    if (juneHandlers[i] != null)
                    {
                        juneHandlers[i].SetLaunchpad(this);
                        juneHandlers[i].InitializeJuneRuntime();
                    }
                    else
                    {
                        nullHandlerCount++;
                    }
                }
                if (nullHandlerCount > 0)
                {
                    Debug.LogWarning($"[EnigmaLaunchpad] Found {nullHandlerCount} NULL June handler(s) in juneHandlers array.");
                }
                Debug.Log("[EnigmaLaunchpad] Finished initializing all June handlers");
            }
            
            Debug.Log("[EnigmaLaunchpad] Calling ClampAndValidateDefaultFolderIndex");
            ClampAndValidateDefaultFolderIndex();
            
            Debug.Log("[EnigmaLaunchpad] Calling SyncObjectStates");
            SyncObjectStates(); // ensure correct active states early
            
            // Capture initial states for Reset feature
            initialDefaultFolderIndex = defaultFolderIndex;
            
            // Update Video Player Controls visibility based on whitelist authorization
            UpdateVideoPlayerControlsAuthorizationState();
            
            Debug.Log("[EnigmaLaunchpad] Calling UpdateDisplay");
            UpdateDisplay();
            
            Debug.Log("[EnigmaLaunchpad] Start() completed successfully");
        }
        
        private void ClampAndValidateDefaultFolderIndex()
        {
            int maxFolderIndex = GetFolderCount() - 1;
            if (defaultFolderIndex < 0) defaultFolderIndex = 0;
            if (defaultFolderIndex > maxFolderIndex) defaultFolderIndex = maxFolderIndex;
        }
        
        private void ConfigurePageDisplayCollider()
        {
            // Find the Page Display object in the hierarchy
            Transform buttonsTransform = transform.Find("Buttons");
            if (buttonsTransform == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Could not find 'Buttons' child object");
                return;
            }
            
            Transform pageDisplayTransform = buttonsTransform.Find("Page Display");
            if (pageDisplayTransform == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Could not find 'Page Display' child object under 'Buttons'");
                return;
            }
            
            MeshCollider meshCollider = pageDisplayTransform.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] 'Page Display' does not have a MeshCollider component");
                return;
            }
            
            // Disable the mesh collider if no skybox folder is configured
            bool shouldEnableCollider = HasSkyboxFolderConfigured();
            meshCollider.enabled = shouldEnableCollider;
            
            Debug.Log($"[EnigmaLaunchpad] Page Display MeshCollider {(shouldEnableCollider ? "enabled" : "disabled")} (skybox folder {(shouldEnableCollider ? "is" : "not")} configured)");
        }
        
        public int FindFolderIndex(ToggleFolderType folderType)
        {
            if (folderTypes == null) return -1;
            
            for (int i = 0; i < folderTypes.Length; i++)
            {
                if (folderTypes[i] == folderType)
                return i;
            }
            
            return -1;
        }
        
        public int GetSkyboxFolderObjectIndex()
        {
            return FindFolderIndex(ToggleFolderType.Skybox);
        }
        
        private int GetSkyboxFolderIndex()
        {
            return GetSkyboxFolderObjectIndex();
        }
        
        private bool FolderRepresentsSkybox(int folderIndex)
        {
            int skyboxIndex = GetSkyboxFolderIndex();
            return skyboxIndex >= 0 && folderIndex == skyboxIndex;
        }
        
        private bool HasFolderTypeConfigured(ToggleFolderType folderType)
        {
            return FindFolderIndex(folderType) >= 0;
        }
        
        public bool HasStatsFolderConfigured()
        {
            return HasFolderTypeConfigured(ToggleFolderType.Stats);
        }
        
        public bool HasPresetFolderConfigured()
        {
            return HasFolderTypeConfigured(ToggleFolderType.Presets);
        }
        
        public bool HasSkyboxFolderConfigured()
        {
            return HasFolderTypeConfigured(ToggleFolderType.Skybox);
        }
        
        public bool HasJuneFolderConfigured()
        {
            return HasFolderTypeConfigured(ToggleFolderType.June);
        }
        
        public bool FolderRepresentsJune(int folderIndex)
        {
            if (folderIndex < 0 || folderTypes == null || folderIndex >= folderTypes.Length)
            {
                return false;
            }
            return folderTypes[folderIndex] == ToggleFolderType.June;
        }
        
        public JuneHandler[] GetJuneHandlers()
        {
            return juneHandlers;
        }
        
        public static JuneHandler[] GetJuneHandlers(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetJuneHandlers() : null;
        }
        
        public JuneHandler GetJuneHandlerForFolder(int folderIndex)
        {
            if (juneHandlers == null)
            {
                return null;
            }
            
            // Count June folders to find the handler index
            int juneHandlerIndex = 0;
            if (folderTypes != null)
            {
                for (int i = 0; i < folderIndex && i < folderTypes.Length; i++)
                {
                    if (folderTypes[i] == ToggleFolderType.June)
                    {
                        juneHandlerIndex++;
                    }
                }
            }
            
            if (juneHandlerIndex >= 0 && juneHandlerIndex < juneHandlers.Length)
            {
                return juneHandlers[juneHandlerIndex];
            }
            
            return null;
        }
        
        public int GetDefaultFolderIndex()
        {
            return defaultFolderIndex;
        }
        
        public static int GetDefaultFolderIndex(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetDefaultFolderIndex() : -1;
        }
        
        public Color GetInactiveColor()
        {
            return inactiveColor;
        }
        
        public static Color GetInactiveColor(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetInactiveColor() : Color.white;
        }
        
        public Color GetActiveColor()
        {
            return activeColor;
        }
        
        public static Color GetActiveColor(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetActiveColor() : Color.white;
        }
        
        public Renderer GetShaderRenderer()
        {
            return mochiHandler != null ? mochiHandler.shaderRenderer : null;
        }
        
        public static Renderer GetShaderRenderer(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetShaderRenderer() : null;
        }
        
        public int GetItemsPerPage()
        {
            return itemsPerPage;
        }
        
        public static int GetItemsPerPage(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetItemsPerPage() : 9;
        }
        
        public string[] GetFolderNames()
        {
            return folderNames;
        }
        
        public static string[] GetFolderNames(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetFolderNames() : null;
        }
        
        public ToggleFolderType[] GetFolderTypes()
        {
            return folderTypes;
        }
        
        public static ToggleFolderType[] GetFolderTypes(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetFolderTypes() : null;
        }
        
        public ObjectHandler[] GetObjectHandlers()
        {
            return objectHandlers;
        }
        
        public static ObjectHandler[] GetObjectHandlers(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetObjectHandlers() : null;
        }
        
        public MaterialHandler[] GetMaterialHandlers()
        {
            return materialHandlers;
        }
        
        public static MaterialHandler[] GetMaterialHandlers(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetMaterialHandlers() : null;
        }
        
        public PropertyHandler[] GetPropertyHandlers()
        {
            return propertyHandlers;
        }
        
        public static PropertyHandler[] GetPropertyHandlers(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetPropertyHandlers() : null;
        }
        
        public ShaderHandler[] GetShaderHandlers()
        {
            return shaderHandlers;
        }
        
        public static ShaderHandler[] GetShaderHandlers(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetShaderHandlers() : null;
        }
        
        public SkyboxHandler GetSkyboxHandler()
        {
            return skyboxHandler;
        }
        
        public static SkyboxHandler GetSkyboxHandler(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetSkyboxHandler() : null;
        }
        
        public StatsHandler GetStatsHandler()
        {
            return statsHandler;
        }
        
        public static StatsHandler GetStatsHandler(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetStatsHandler() : null;
        }
        
        public PresetHandler GetPresetHandler()
        {
            return presetHandler;
        }
        
        public static PresetHandler GetPresetHandler(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetPresetHandler() : null;
        }
        
        public MochieHandler GetMochieHandlerField()
        {
            return mochiHandler;
        }
        
        public static MochieHandler GetMochieHandlerField(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetMochieHandlerField() : null;
        }
        
        public FaderSystemHandler GetFaderHandler()
        {
            return faderHandler;
        }
        
        public static FaderSystemHandler GetFaderHandler(EnigmaLaunchpad launchpad)
        {
            return launchpad != null ? launchpad.GetFaderHandler() : null;
        }
        
        /// <summary>
        /// Get the toggle state for a specific folder and local index.
        /// Used by FaderSystemHandler to determine if a dynamic fader condition is active.
        /// </summary>
        public bool GetToggleStateForFolder(int folderIndex, int localIndex)
        {
            if (folderIndex < 0 || localIndex < 0)
            {
                return false;
            }
            
            ToggleFolderType folderType = GetFolderTypeForIndex(folderIndex);
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                    ObjectHandler objHandler = GetObjectHandlerForFolder(folderIndex);
                    if (objHandler != null)
                    {
                        return objHandler.GetEntryState(localIndex);
                    }
                    return false;
                    
                case ToggleFolderType.Materials:
                    MaterialHandler matHandler = GetMaterialHandlerForFolder(folderIndex);
                    if (matHandler != null)
                    {
                        return matHandler.GetEntryState(localIndex);
                    }
                    return false;
                    
                case ToggleFolderType.Shaders:
                    ShaderHandler shaderHandler = GetShaderHandlerForFolder(folderIndex);
                    if (shaderHandler != null)
                    {
                        return shaderHandler.GetEntryState(localIndex);
                    }
                    return false;
                    
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Get the property state for a specific folder and local index.
        /// Used by FaderSystemHandler to determine if a dynamic fader condition is active.
        /// </summary>
        public bool GetPropertyStateForFolder(int folderIndex, int localIndex)
        {
            if (folderIndex < 0 || localIndex < 0)
            {
                return false;
            }
            
            PropertyHandler propHandler = GetPropertyHandlerForFolder(folderIndex);
            if (propHandler != null)
            {
                return propHandler.GetEntryState(localIndex);
            }
            return false;
        }
        
        /// <summary>
        /// Gets the ButtonHandler for the page/auto-change button (index 9).
        /// </summary>
        public ButtonHandler GetPageButtonHandler()
        {
            if (buttonHandlers == null || buttonHandlers.Length <= PAGE_BUTTON_INDEX)
            {
                return null;
            }
            
            return buttonHandlers[PAGE_BUTTON_INDEX];
        }
        
        
        
        public bool FolderRepresentsStats(int folderIndex)
        {
            if (!TryGetObjectFolderIndex(folderIndex, out int objectFolderIdx))
            return false;
            
            return folderTypes != null &&
            objectFolderIdx >= 0 &&
            objectFolderIdx < folderTypes.Length &&
            folderTypes[objectFolderIdx] == ToggleFolderType.Stats;
        }
        
        public bool FolderRepresentsPresets(int folderIndex)
        {
            if (folderIndex < 0 || folderTypes == null || folderIndex >= folderTypes.Length)
            {
                return false;
            }
            return folderTypes[folderIndex] == ToggleFolderType.Presets;
        }
        
        public bool TryGetObjectFolderIndex(int folderIndex, out int objectFolderIdx)
        {
            objectFolderIdx = -1;
            int totalFolders = GetFolderCount();
            if (folderIndex < 0 || folderIndex >= totalFolders)
            return false;
            
            if (!IsFolderIndexValid(folderIndex))
            return false;
            
            if (folderTypes != null && folderIndex < folderTypes.Length)
            {
                var type = folderTypes[folderIndex];
                if (type == ToggleFolderType.Skybox || type == ToggleFolderType.Mochie || type == ToggleFolderType.June || type == ToggleFolderType.Presets)
                return false;
            }
            
            objectFolderIdx = folderIndex;
            return true;
        }
        
        // New bidirectional folder cycling
        public void CycleFolder(int direction)
        {
            if (!CanLocalUserInteract())
            return;
            
            if (direction == 0) direction = 1; // default to forward
            if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            
            int total = GetFolderCount();
            if (total <= 0) return;
            
            int newFolder = defaultFolderIndex;
            direction = (direction > 0) ? 1 : -1;
            
            // Simply cycle through all folders - no validity checks needed
            for (int offset = 1; offset <= total; offset++)
            {
                int check = (defaultFolderIndex + direction * offset) % total;
                if (check < 0) check += total;
                newFolder = check;
                break;
            }
            
            defaultFolderIndex = newFolder;
            if (FolderRepresentsSkybox(newFolder))
            {
                if (skyboxHandler != null)
                {
                    skyboxHandler.RefreshSkyboxSetup();
                }
            }
            MochieHandler mochiHandler = GetMochiHandler();
            if (mochiHandler != null && mochiHandler.FolderRepresentsMochie(newFolder) && mochiHandler.IsMochieFolderEnabled())
            {
                mochiHandler.SendCustomEvent("InitializeMaterialsIfNeeded");
            }
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
        }
        
        public void HandleItemSelect(int buttonIndex)
        {
            if (!CanLocalUserInteract())
            return;
            
            bool displayNeedsUpdate = false;
            
            if (FolderRepresentsSkybox(defaultFolderIndex))
            {
                if (skyboxHandler != null)
                {
                    skyboxHandler.OnSelect(buttonIndex);
                    displayNeedsUpdate = true;
                }
            }
            else if (FolderRepresentsJune(defaultFolderIndex))
            {
                JuneHandler juneHandler = GetJuneHandlerForFolder(defaultFolderIndex);
                if (juneHandler != null)
                {
                    juneHandler.OnSelect(buttonIndex);
                    displayNeedsUpdate = true;
                }
            }
            else
            {
                MochieHandler mochiHandler = GetMochiHandler();
                if (mochiHandler != null && mochiHandler.FolderRepresentsMochie(defaultFolderIndex))
                {
                    mochiHandler.OnSelect(buttonIndex);
                    displayNeedsUpdate = true;
                }
                else if (FolderRepresentsStats(defaultFolderIndex))
                {
                    if (statsHandler != null)
                    {
                        statsHandler.OnSelect(buttonIndex);
                        displayNeedsUpdate = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else if (FolderRepresentsPresets(defaultFolderIndex))
                {
                    if (presetHandler != null)
                    {
                        presetHandler.OnSelect(buttonIndex);
                        displayNeedsUpdate = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if (!TryGetObjectFolderIndex(defaultFolderIndex, out int folderIdx))
                    {
                        return;
                    }
                    
                    ToggleFolderType folderType = GetFolderTypeForIndex(folderIdx);
                    if (folderType == ToggleFolderType.Materials)
                    {
                        MaterialHandler materialHandler = GetMaterialHandlerForFolder(folderIdx);
                        if (materialHandler != null)
                        {
                            materialHandler.OnSelect(buttonIndex);
                            displayNeedsUpdate = true;
                        }
                    }
                    else if (folderType == ToggleFolderType.Objects)
                    {
                        ObjectHandler handler = GetObjectHandlerForFolder(folderIdx);
                        if (handler != null)
                        {
                            handler.OnSelect(buttonIndex);
                            displayNeedsUpdate = true;
                        }
                    }
                    else if (folderType == ToggleFolderType.Properties)
                    {
                        PropertyHandler propertyHandler = GetPropertyHandlerForFolder(folderIdx);
                        if (propertyHandler != null)
                        {
                            propertyHandler.OnSelect(buttonIndex);
                            displayNeedsUpdate = true;
                        }
                    }
                    else if (folderType == ToggleFolderType.Shaders)
                    {
                        ShaderHandler shaderHandler = GetShaderHandlerForFolder(folderIdx);
                        if (shaderHandler != null)
                        {
                            shaderHandler.OnSelect(buttonIndex);
                            displayNeedsUpdate = true;
                        }
                    }
                }
            }
            
            if (displayNeedsUpdate)
            {
                // Handlers call RequestSerialization() in their OnSelect methods,
                // so we only need to send the display update network event here
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
            }
        }
        
        public void HandleScreenButtonPress(int buttonIndex)
        {
            if (!CanLocalUserInteract())
                return;

            if (screenHandler == null)
            {
                return;
            }

            if (buttonIndex == 0)
            {
                screenHandler.DisableAllScreens();
                return;
            }

            screenHandler.ToggleScreen(buttonIndex - 1);
        }
        
        /// <summary>
        /// Toggles fader control between VRC Pickup mode and hand collider mode.
        /// This is a LOCAL-only operation - each player can choose their preferred control method.
        /// </summary>
        public void ToggleFaderPickupMode()
        {
            if (!CanLocalUserInteract())
                return;

            if (faderHandler == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Cannot toggle fader pickup mode - faderHandler is null");
                return;
            }

            faderHandler.TogglePickupMode();
        }
        
        private void ApplyVideoScreenMaterial()
        {
            if (videoScreenMaterial == null || screenHandler == null)
            {
                return;
            }
            
            // Find the Video screen object under Launchpad/Screens/Video
            // Note: This path is hardcoded by design to match the expected prefab hierarchy
            Transform screensTransform = transform.Find("Screens");
            if (screensTransform == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Could not find Screens child object");
                return;
            }
            
            Transform videoTransform = screensTransform.Find("Video");
            if (videoTransform == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Could not find Video child object under Screens");
                return;
            }
            
            MeshRenderer meshRenderer = videoTransform.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogWarning("[EnigmaLaunchpad] Video object does not have a MeshRenderer");
                return;
            }
            
            meshRenderer.material = videoScreenMaterial;
            Debug.Log("[EnigmaLaunchpad] Applied video screen material to Video object");
        }
        
        public void ChangePage(int direction)
        {
            if (!CanLocalUserInteract())
            return;
            
            bool displayNeedsUpdate = false;
            
            MochieHandler mochiHandler = GetMochiHandler();
            if (mochiHandler != null && mochiHandler.FolderRepresentsMochie(defaultFolderIndex))
            {
                ChangeMochiePage(direction);
            }
            else if (FolderRepresentsJune(defaultFolderIndex))
            {
                JuneHandler juneHandler = GetJuneHandlerForFolder(defaultFolderIndex);
                if (juneHandler != null)
                {
                    juneHandler.OnPageChange(direction);
                    displayNeedsUpdate = true;
                }
            }
            else if (FolderRepresentsStats(defaultFolderIndex))
            {
                if (statsHandler != null)
                {
                    statsHandler.OnPageChange(direction);
                }
                displayNeedsUpdate = true;
            }
            else if (FolderRepresentsPresets(defaultFolderIndex))
            {
                if (presetHandler != null)
                {
                    presetHandler.OnPageChange(direction);
                }
                displayNeedsUpdate = true;
            }
            else
            {
                if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                
                if (FolderRepresentsSkybox(defaultFolderIndex))
                {
                    if (skyboxHandler != null)
                    {
                        skyboxHandler.OnPageChange(direction);
                    }
                    displayNeedsUpdate = true;
                }
                else
                {
                    if (!TryGetObjectFolderIndex(defaultFolderIndex, out int folderIdx))
                    {
                        return;
                    }
                    
                    ToggleFolderType folderType = GetFolderTypeForIndex(folderIdx);
                    if (folderType == ToggleFolderType.Materials)
                    {
                        MaterialHandler materialHandler = GetMaterialHandlerForFolder(folderIdx);
                        if (materialHandler != null)
                        {
                            materialHandler.OnPageChange(direction);
                        }
                    }
                    else if (folderType == ToggleFolderType.Objects)
                    {
                        ObjectHandler handler = GetObjectHandlerForFolder(folderIdx);
                        if (handler != null)
                        {
                            handler.OnPageChange(direction);
                        }
                    }
                    else if (folderType == ToggleFolderType.Properties)
                    {
                        PropertyHandler propertyHandler = GetPropertyHandlerForFolder(folderIdx);
                        if (propertyHandler != null)
                        {
                            propertyHandler.OnPageChange(direction);
                        }
                    }
                    else if (folderType == ToggleFolderType.Shaders)
                    {
                        ShaderHandler shaderHandler = GetShaderHandlerForFolder(folderIdx);
                        if (shaderHandler != null)
                        {
                            shaderHandler.OnPageChange(direction);
                        }
                    }

                    RequestSerialization();
                    displayNeedsUpdate = true;
                }
            }
            
            if (displayNeedsUpdate)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
            }
        }
        
        public void ToggleAutoChange()
        {
            if (skyboxHandler != null)
            {
                skyboxHandler.ToggleAutoChange();
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
            }
        }
        
        public void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }
        
        public void ChangeMochiePage(int direction)
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            Debug.Log($"[EnigmaLaunchpad] ChangeMochiePage START - Player: {localPlayerName}, Direction: {direction}");
            
            MochieHandler handler = GetMochiHandler();
            if (handler != null)
            {
                handler.OnPageChange(direction);
                Debug.Log($"[EnigmaLaunchpad] ChangeMochiePage - Sending SyncUpdateDisplayGlobally network event");
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
            }
            else
            {
                Debug.LogWarning($"[EnigmaLaunchpad] ChangeMochiePage - MochieHandler is null");
            }
            
            Debug.Log($"[EnigmaLaunchpad] ChangeMochiePage END - Player: {localPlayerName}");
        }
        
        
        private void ApplyButtonVisuals()
        {
            if (buttonHandlers == null)
            {
                return;
            }
            
            MochieHandler mochiHandler = GetMochiHandler();
            int currentFolderIndex = GetDefaultFolderIndex();
            StatsHandler statsHandlerLocal = GetStatsHandler();
            PresetHandler presetHandlerLocal = GetPresetHandler();
            JuneHandler juneHandler = GetJuneHandlerForFolder(currentFolderIndex);
            
            bool skyboxActive = FolderRepresentsSkybox(currentFolderIndex);
            bool mochiActive = mochiHandler != null && mochiHandler.FolderRepresentsMochie(currentFolderIndex);
            bool statsActive = FolderRepresentsStats(currentFolderIndex) && statsHandlerLocal != null;
            bool presetActive = FolderRepresentsPresets(currentFolderIndex) && presetHandlerLocal != null;
            bool juneActive = FolderRepresentsJune(currentFolderIndex) && juneHandler != null;
            
            int folderIndex = -1;
            ToggleFolderType folderType = ToggleFolderType.Objects;
            
            if (TryGetObjectFolderIndex(currentFolderIndex, out int folderIdx) && IsFolderIndexValid(folderIdx))
            {
                folderIndex = folderIdx;
                folderType = GetFolderTypeForIndex(folderIdx);
            }
            
            for (int i = 0; i < buttonHandlers.Length; i++)
            {
                ButtonHandler handler = buttonHandlers[i];
                if (handler == null)
                {
                    continue;
                }
                
                ApplyButtonVisualForHandler(handler, i, mochiActive, skyboxActive, statsActive, presetActive, juneActive, juneHandler, presetHandlerLocal, folderType, folderIndex);
            }
        }
        
        private void ApplyButtonVisualForHandler(ButtonHandler handler, int arrayIndex, bool mochiActive, bool skyboxActive, bool statsActive, bool presetActive, bool juneActive, JuneHandler juneHandler, PresetHandler presetHandlerLocal, ToggleFolderType folderType, int folderIndex)
        {
            // Navigation buttons (up/down/left/right/reset/autochange) handle their own visuals
            // via flash effects - don't override their colors based on toggle states. The page
            // button (array index 9) still needs handler visuals applied so page labels and
            // auto-change state stay updated.
            if (IsNavigationHandler(handler) && arrayIndex != PAGE_BUTTON_INDEX)
            {
                return;
            }
            
            if (mochiActive)
            {
                ApplyMochieVisual(handler, arrayIndex);
                return;
            }
            
            if (skyboxActive)
            {
                ApplySkyboxVisual(handler, arrayIndex);
                return;
            }
            
            if (juneActive)
            {
                ApplyJuneVisual(handler, arrayIndex, juneHandler);
                return;
            }
            
            if (presetActive && presetHandlerLocal != null)
            {
                ApplyPresetVisual(handler, arrayIndex, presetHandlerLocal);
                return;
            }
            
            if (statsActive && folderType == ToggleFolderType.Stats)
            {
                ApplyStatsVisual(handler, arrayIndex, folderIndex);
                return;
            }

            ApplyObjectMaterialOrPropertyVisual(handler, arrayIndex, folderType, folderIndex);
        }
        
        /// <summary>
        /// Determines the effective button index to use for handler method calls.
        /// For special buttons at positions 9 (page) and 10 (folder name), uses the array position.
        /// For regular item buttons, uses the handler's configured buttonIndex field.
        /// </summary>
        /// <param name="handler">The ButtonHandler whose effective index is being determined.</param>
        /// <param name="arrayIndex">The position of this handler in the buttonHandlers array.</param>
        /// <returns>The button index to pass to handler GetLabel/GetColor methods.</returns>
        private int GetEffectiveButtonIndex(ButtonHandler handler, int arrayIndex)
        {
            // For positions 9 and 10 in the array, use the array position as the button index
            // so handlers can provide page numbers and folder names
            if (arrayIndex == PAGE_BUTTON_INDEX || arrayIndex == FOLDER_BUTTON_INDEX)
            {
                return arrayIndex;
            }
            
            // For other positions, use the handler's configured buttonIndex
            return ButtonHandler.GetButtonIndex(handler);
        }
        
        private void ApplyMochieVisual(ButtonHandler handler, int arrayIndex)
        {
            MochieHandler mochiHandler = GetMochiHandler();
            if (mochiHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = mochiHandler != null ? mochiHandler.GetLabel(buttonIndex) : (buttonIndex == 10 ? "Mochie" : string.Empty);
            Color color = mochiHandler != null ? mochiHandler.GetColor(buttonIndex) : Color.white;
            bool interactable = mochiHandler != null && mochiHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel, false, false);
        }
        
        private void ApplySkyboxVisual(ButtonHandler handler, int arrayIndex)
        {
            if (skyboxHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = skyboxHandler.GetLabel(buttonIndex);
            bool isActive = skyboxHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = skyboxHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = ButtonHandler.GetButtonIndex(handler) < 9;

            ApplyVisual(handler, label, color, interactable, shouldFormatLabel, false, false);

            if (arrayIndex == PAGE_BUTTON_INDEX)
            {
                handler.SetAutoChangeActive(skyboxHandler.IsAutoChanging());
            }
        }
        
        private void ApplyJuneVisual(ButtonHandler handler, int arrayIndex, JuneHandler juneHandler)
        {
            if (juneHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = juneHandler.GetLabel(buttonIndex);
            // Use juneHandler.GetColor() for AudioLink band colors, 
            // which returns appropriate band colors (bass/lowmid/highmid/treble) or active/inactive
            Color color = juneHandler.GetColor(buttonIndex);
            bool interactable = juneHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel, false, false);
        }
        
        private void ApplyStatsVisual(ButtonHandler handler, int arrayIndex, int folderIndex)
        {
            if (statsHandler != null)
            {
                statsHandler.SetActiveStatsFolderIndex(folderIndex);
            }
            
            if (statsHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = statsHandler.GetLabel(buttonIndex);
            bool isActive = statsHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = statsHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel, true, true);
        }
        
        private void ApplyPresetVisual(ButtonHandler handler, int arrayIndex, PresetHandler presetHandlerLocal)
        {
            if (presetHandlerLocal == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = presetHandlerLocal.GetLabel(buttonIndex);
            bool isActive = presetHandlerLocal.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = presetHandlerLocal.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel);
        }
        
        private void ApplyObjectMaterialOrPropertyVisual(ButtonHandler handler, int arrayIndex, ToggleFolderType folderType, int folderIndex)
        {
            if (folderType == ToggleFolderType.Materials)
            {
                MaterialHandler materialHandler = GetMaterialHandlerForFolder(folderIndex);
                ApplyMaterialVisual(handler, arrayIndex, materialHandler);
                return;
            }

            if (folderType == ToggleFolderType.Objects)
            {
                ObjectHandler objectHandler = GetObjectHandlerForFolder(folderIndex);
                ApplyObjectVisual(handler, arrayIndex, objectHandler);
                return;
            }

            if (folderType == ToggleFolderType.Properties)
            {
                PropertyHandler propertyHandler = GetPropertyHandlerForFolder(folderIndex);
                ApplyPropertyVisual(handler, arrayIndex, propertyHandler);
                return;
            }

            if (folderType == ToggleFolderType.Shaders)
            {
                ShaderHandler shaderHandler = GetShaderHandlerForFolder(folderIndex);
                ApplyShaderVisual(handler, arrayIndex, shaderHandler);
                return;
            }

            ApplyEmptyVisual(handler);
        }

        private void ApplyPropertyVisual(ButtonHandler handler, int arrayIndex, PropertyHandler propertyHandler)
        {
            if (propertyHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }

            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = propertyHandler.GetLabel(buttonIndex);
            bool isActive = propertyHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = propertyHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex >= 0 && buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel);
        }
        
        private void ApplyShaderVisual(ButtonHandler handler, int arrayIndex, ShaderHandler shaderHandler)
        {
            if (shaderHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }

            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = shaderHandler.GetLabel(buttonIndex);
            bool isActive = shaderHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = shaderHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex >= 0 && buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel);
        }
        
        private void ApplyMaterialVisual(ButtonHandler handler, int arrayIndex, MaterialHandler materialHandler)
        {
            if (materialHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = materialHandler.GetLabel(buttonIndex);
            bool isActive = materialHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = materialHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex >= 0 && buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel);
        }
        
        private void ApplyObjectVisual(ButtonHandler handler, int arrayIndex, ObjectHandler objectHandler)
        {
            if (objectHandler == null)
            {
                ApplyEmptyVisual(handler);
                return;
            }
            
            int buttonIndex = GetEffectiveButtonIndex(handler, arrayIndex);
            string label = objectHandler.GetLabel(buttonIndex);
            bool isActive = objectHandler.IsActive(buttonIndex);
            Color color = isActive ? GetActiveColor() : GetInactiveColor();
            bool interactable = objectHandler.IsInteractable(buttonIndex);
            bool shouldFormatLabel = buttonIndex >= 0 && buttonIndex < PAGE_BUTTON_INDEX;
            ApplyVisual(handler, label, color, interactable, shouldFormatLabel);
        }
        
        public string GetFolderLabelForIndex(int folderIndex, bool skyboxOverride)
        {
            if (skyboxOverride)
            {
                int skyboxIndex = GetSkyboxFolderObjectIndex();
                if (skyboxIndex >= 0 && folderNames != null && skyboxIndex < folderNames.Length)
                {
                    string skyboxLabel = folderNames[skyboxIndex];
                    return string.IsNullOrEmpty(skyboxLabel) ? "Skybox" : skyboxLabel;
                }
                return "Skybox";
            }
            
            if (folderIndex >= 0 && folderNames != null && folderIndex < folderNames.Length)
            {
                return folderNames[folderIndex];
            }
            
            return "Objects";
        }
        
        private bool IsNavigationHandler(ButtonHandler handler)
        {
            return handler != null && (ButtonHandler.GetIsFolderLeftButton(handler) || ButtonHandler.GetIsFolderRightButton(handler) ||
            ButtonHandler.GetIsDownButton(handler) || ButtonHandler.GetIsUpButton(handler) ||
            ButtonHandler.GetIsAutoChangeButton(handler) || ButtonHandler.GetIsResetButton(handler));
        }
        
        private void ApplyVisual(ButtonHandler handler, string label, Color color, bool interactable,
        bool formatLabel = false, bool useStatsFormatting = false, bool formatFirstLineOnly = false)
        {
            handler.UpdateVisual(
            label,
            color,
            interactable || IsNavigationHandler(handler),
            formatLabel,
            useStatsFormatting,
            formatFirstLineOnly);
        }
        
        private void ApplyEmptyVisual(ButtonHandler handler)
        {
            Color inactiveColorLocal = GetInactiveColor();
            ApplyVisual(handler, string.Empty, inactiveColorLocal, false, false, false, false);
        }
        
        public void UpdateDisplay()
        {
            EnsureButtonHandlerReferences();
            
            ApplyButtonVisuals();
            
            if (statsHandler != null)
            {
                int statsFolderIndex = -1;
                if (FolderRepresentsStats(defaultFolderIndex) && TryGetObjectFolderIndex(defaultFolderIndex, out statsFolderIndex))
                {
                    statsHandler.UpdateWorldStatsAuxiliaryButtonColors(statsFolderIndex);
                }
                else
                {
                    statsHandler.UpdateWorldStatsAuxiliaryButtonColors(-1);
                }
                
                statsHandler.UpdateWorldStatsPollingState();
            }
        }
        
        /// <summary>
        /// Network event method that triggers UpdateDisplay on all clients.
        /// Called after any state change that should be visible to all players.
        /// </summary>
        public void SyncUpdateDisplayGlobally()
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            Debug.Log($"[EnigmaLaunchpad] SyncUpdateDisplayGlobally - Player: {localPlayerName} received network event, updating display");
            UpdateDisplay();
            
            // Update fader bindings when display is synced across network
            if (faderHandler != null)
            {
                faderHandler.OnToggleStateChanged();
            }
        }
        
        public void SyncObjectStates()
        {
            ObjectHandler handler = GetPrimaryObjectHandler();
            if (handler != null)
            {
                handler.RestoreLocalState();
            }
            
        }
        
        public bool IsFolderIndexValid(int folderIdx)
        {
            return folderEntryCounts != null &&
            folderIdx >= 0 &&
            folderIdx < GetFolderCount() &&
            folderIdx < folderEntryCounts.Length;
        }
        
        public override void OnDeserialization()
        {
            base.OnDeserialization();
            
            EnsureMochieHandlerReference();
            EnsureSkyboxHandlerReference();
            EnsureStatsHandlerReference();
            EnsureObjectHandlerReferences();
            EnsureMaterialHandlerReferences();
            EnsurePropertyHandlerReferences();
            
            // Ensure ObjectHandlers and MaterialHandlers have their launchpad references
            if (objectHandlers != null)
            {
                for (int i = 0; i < objectHandlers.Length; i++)
                {
                    if (objectHandlers[i] != null)
                    {
                        objectHandlers[i].SetLaunchpad(this);
                    }
                }
            }

            if (propertyHandlers != null)
            {
                for (int i = 0; i < propertyHandlers.Length; i++)
                {
                    if (propertyHandlers[i] != null)
                    {
                        propertyHandlers[i].SetLaunchpad(this);
                    }
                }
            }
            
            if (materialHandlers != null)
            {
                for (int i = 0; i < materialHandlers.Length; i++)
                {
                    if (materialHandlers[i] != null)
                    {
                        materialHandlers[i].SetLaunchpad(this);
                    }
                }
            }
            
            NotifyHandlersOfDeserialization();
            // UpdateDisplay is called locally here because OnDeserialization is triggered
            // when synced data arrives from the network, so we only need to update the local display
            UpdateDisplay();
            
            // Update fader bindings when synced state arrives
            if (faderHandler != null)
            {
                faderHandler.OnToggleStateChanged();
            }
        }
        
        public void RequestDisplayUpdateFromHandler()
        {
            // UpdateDisplay is called locally here because handlers request local UI updates
            // after they've made local state changes (e.g., material application)
            UpdateDisplay();
            
            // Update fader bindings when toggle states change
            if (faderHandler != null)
            {
                faderHandler.OnToggleStateChanged();
            }
        }
        
        private void NotifyHandlersOfDeserialization()
        {
            if (skyboxHandler != null)
            {
                skyboxHandler.OnLaunchpadDeserialized();
            }
            
            MochieHandler mochiHandler = GetMochiHandler();
            if (mochiHandler != null)
            {
                mochiHandler.OnLaunchpadDeserialized();
            }
            
            if (objectHandlers != null)
            {
                for (int i = 0; i < objectHandlers.Length; i++)
                {
                    ObjectHandler handler = objectHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }
                    
                    handler.OnLaunchpadDeserialized();
                }
            }

            if (propertyHandlers != null)
            {
                for (int i = 0; i < propertyHandlers.Length; i++)
                {
                    PropertyHandler handler = propertyHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    handler.OnLaunchpadDeserialized();
                }
            }
        }
        
        // ---------------- Object Folders Initialization & Helpers ----------------
        private void InitializeObjectFolders()
        {
            ObjectHandler handler = GetPrimaryObjectHandler();
            if (handler != null)
            {
                handler.InitializeObjectRuntime();
            }
        }
        
        private void BuildObjectOwnershipMap(int folderCount, int totalEntries)
        {
            // Managed by ObjectHandler
        }
        
        /// <summary>
        /// Ensures the provided scratch array can hold <paramref name="size"/> elements.
        /// Existing buffers are now handled by ObjectHandler.
        /// </summary>
        private void EnsureIntArrayCapacity(ref int[] array, int size)
        {
        }
        
        public Material GetMaterialEntry(int index)
        {
            ObjectHandler handler = GetPrimaryObjectHandler();
            return handler != null ? handler.GetMaterialEntry(index) : null;
        }
        
        public GameObject GetGameObjectEntry(int index)
        {
            ObjectHandler handler = GetPrimaryObjectHandler();
            return handler != null ? handler.GetGameObjectEntry(index) : null;
        }
        
        public int GetFolderPage(int folderIdx)
        {
            // Route to the appropriate handler for this folder
            ObjectHandler handler = GetObjectHandlerForFolder(folderIdx);
            if (handler != null)
            {
                // Handler now manages its own page internally
                // This method is kept for compatibility but may not be needed
                return 0;
            }
            return 0;
        }
        
        public void SetFolderPage(int folderIdx, int page)
        {
            // Page state is now managed internally by each handler
            // This method is kept for compatibility but no longer used
        }
        
        // ---------------- Reset Feature ----------------
        // Emergency reset: restores initial captured states, stops auto, resets pages & colors.
        public void ResetLaunchpad()
        {
            if (!CanLocalUserInteract())
            return;
            
            if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            
            // Restore folder
            defaultFolderIndex = initialDefaultFolderIndex;
            ClampAndValidateDefaultFolderIndex();
            
            // Restore skybox selection
            if (skyboxHandler != null)
            {
                skyboxHandler.ResetSkybox();
            }

            if (materialHandlers != null)
            {
                for (int i = 0; i < materialHandlers.Length; i++)
                {
                    MaterialHandler handler = materialHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    handler.RestoreInitialState();
                    handler.RequestSerialization();
                }
            }

            if (objectHandlers != null)
            {
                for (int i = 0; i < objectHandlers.Length; i++)
                {
                    ObjectHandler handler = objectHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    if (!Networking.IsOwner(handler.gameObject))
                    {
                        Networking.SetOwner(Networking.LocalPlayer, handler.gameObject);
                    }

                    handler.RestoreInitialState();
                    handler.RequestSerialization();
                }

                SyncObjectStates();
            }

            if (mochiHandler != null)
            {
                mochiHandler.RestoreInitialState();
            }

            if (propertyHandlers != null)
            {
                for (int i = 0; i < propertyHandlers.Length; i++)
                {
                    PropertyHandler handler = propertyHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    handler.RestoreInitialState();
                    handler.RequestSerialization();
                }
            }

            JuneHandler[] juneHandlers = GetJuneHandlers();
            if (juneHandlers != null)
            {
                for (int i = 0; i < juneHandlers.Length; i++)
                {
                    JuneHandler handler = juneHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    handler.RestoreInitialState();
                    handler.RequestSerialization();
                }
            }

            if (presetHandler != null)
            {
                presetHandler.ResetPresets();
            }

            if (faderHandler != null)
            {
                faderHandler.ResetAllFaders();
            }
            
            if (shaderHandlers != null)
            {
                for (int i = 0; i < shaderHandlers.Length; i++)
                {
                    ShaderHandler handler = shaderHandlers[i];
                    if (handler == null)
                    {
                        continue;
                    }

                    if (!Networking.IsOwner(handler.gameObject))
                    {
                        Networking.SetOwner(Networking.LocalPlayer, handler.gameObject);
                    }

                    handler.RestoreInitialState();
                    handler.RequestSerialization();
                }
            }
            
            if (screenHandler != null)
            {
                screenHandler.RestoreInitialState();
            }
            
            // RequestSerialization must be called before SendCustomNetworkEvent to ensure
            // synced variables are queued for network transmission before the event triggers
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplayGlobally));
        }
        
        public void ForceMaterialUpdate()
        {
            Renderer renderer = GetShaderRenderer();
            if (renderer != null)
            {
                // Toggle renderer state to force Unity to re-process the material
                // Note: We deliberately do NOT reassign renderer.materials here because that
                // creates new material instances and invalidates the MochieHandler's cached
                // activeMochieMaterial reference. Just toggling the renderer is sufficient.
                renderer.enabled = false;
                renderer.enabled = true;
            }
        }
        
        public bool IsMochieFolderActive()
        {
            MochieHandler handler = GetMochiHandler();
            return handler != null && handler.FolderRepresentsMochie(defaultFolderIndex) && handler.IsMochieFolderEnabled();
        }
        
        public void UpdateAudioLinkBands()
        {
            MochieHandler handler = GetMochiHandler();
            if (handler != null)
            {
                handler.UpdateAudioLinkBands();
            }
        }
        
        void UpdateFeatureSupport(Material candidate = null)
        {
            MochieHandler handler = GetMochiHandler();
            if (handler != null)
            {
                handler.UpdateFeatureSupport(candidate);
            }
        }
        
        public void UpdateMochiePage(int direction)
        {
            MochieHandler handler = GetMochiHandler();
            if (handler == null) return;
            
            handler.OnPageChange(direction);
        }
        
        public void FlashButtonAtIndex(int buttonIndex)
        {
            if (buttonHandlers == null || buttonIndex < 0 || buttonIndex >= buttonHandlers.Length)
            {
                return;
            }
            
            ButtonHandler handler = buttonHandlers[buttonIndex];
            if (handler == null)
            {
                return;
            }
            
            handler.SendCustomNetworkEvent(NetworkEventTarget.All, "FlashButton");
        }
        
    }
}

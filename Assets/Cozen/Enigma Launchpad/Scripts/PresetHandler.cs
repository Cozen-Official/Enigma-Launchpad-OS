using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PresetHandler : UdonSharpBehaviour
    {
        [Header("Preset Routing")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Header("Preset Configuration")]
        [Range(1, 9)]
        [Tooltip("Number of preset pages available.")]
        public int presetPages = 1;
        
        [Tooltip("Folder indices to include when capturing presets (editor configured).")]
        [SerializeField]
        private int[] includedFolderIndices;
        
        [SerializeField, HideInInspector]
        private bool folderSelectionInitialized = false;
        
        // Preset slot header layout: [Save] [Load] [Delete] [Preset 1] [Preset 2] ... [Preset N]
        private const int PresetHeaderEntries = 3;
        
        // PlayerData key prefix for preset persistence
        private const string PlayerDataKeyPrefix = "EnigmaPreset_";
        
        // Preset folder type masks
        private const int PresetMaskObjects = 1 << (int)ToggleFolderType.Objects;
        private const int PresetMaskMaterials = 1 << (int)ToggleFolderType.Materials;
        private const int PresetMaskProperties = 1 << (int)ToggleFolderType.Properties;
        private const int PresetMaskSkybox = 1 << (int)ToggleFolderType.Skybox;
        private const int PresetMaskStats = 1 << (int)ToggleFolderType.Stats;
        private const int PresetMaskShaders = 1 << (int)ToggleFolderType.Shaders;
        private const int PresetMaskMochie = 1 << (int)ToggleFolderType.Mochie;
        private const int PresetMaskJune = 1 << (int)ToggleFolderType.June;
        private const int PresetDefaultMask = PresetMaskSkybox | PresetMaskObjects | PresetMaskMaterials | PresetMaskProperties | PresetMaskShaders | PresetMaskMochie | PresetMaskJune;
        
        // ============================================================================
        // SIMPLIFIED SYNCED DATA - Only 4 synced variables total
        // Toggle states for Object/Material/Property/June stored as bitmasks
        // All Mochie values (indices and steps) stored in values array
        // ============================================================================
        
        // Toggle bitmask array: Each preset uses N ints (based on total toggles / 32)
        // Bit layout: [Object toggles][Material toggles][Property toggles][June toggles]
        // Note: Mochie state is represented by indices/steps, not individual toggle bits
        [UdonSynced] private int[] syncedToggleBitmasks;
        
        // Values array: Fixed stride per preset containing non-toggle data
        // Layout per preset (PRESET_VALUES_STRIDE ints):
        //   [0] hasSnapshot (0 or 1)
        //   [1] skyboxIndex
        //   [2] juneALBand
        //   [3] juneALPower
        //   [4] mochieOutlineType (0-2: None, Sobel, Aura)
        //   [5] mochieOutlineStrength (0-2: Low, Normal, High)
        //   [6] mochieScanIndex (-1 = none)
        //   [7] mochieOverlayIndex (-1 = none)
        //   [8] mochieColorIndex
        //   [9] mochieAudioBand
        //   [10-15] Mochie +/- steps: Saturation, Rounding, FogSafe, Brightness, HDR, Contrast
        //   [16-24] AudioLink strengths (as steps): Filter, Shake, Blur, Distort, Noise, Fog, Outline, Image, Misc
        private const int PV_HAS_SNAPSHOT = 0;
        private const int PV_SKYBOX_INDEX = 1;
        private const int PV_JUNE_AL_BAND = 2;
        private const int PV_JUNE_AL_POWER = 3;
        private const int PV_MOCHIE_OUTLINE_TYPE = 4;
        private const int PV_MOCHIE_OUTLINE_STRENGTH = 5;
        private const int PV_MOCHIE_SCAN_INDEX = 6;
        private const int PV_MOCHIE_OVERLAY_INDEX = 7;
        private const int PV_MOCHIE_COLOR_INDEX = 8;
        private const int PV_MOCHIE_AUDIO_BAND = 9;
        // Mochie +/- step values (6 values)
        private const int PV_MOCHIE_SATURATION = 10;
        private const int PV_MOCHIE_ROUNDING = 11;
        private const int PV_MOCHIE_FOG_SAFE = 12;
        private const int PV_MOCHIE_BRIGHTNESS = 13;
        private const int PV_MOCHIE_HDR = 14;
        private const int PV_MOCHIE_CONTRAST = 15;
        // AudioLink strengths as steps (9 values)
        private const int PV_MOCHIE_AL_FILTER = 16;
        private const int PV_MOCHIE_AL_SHAKE = 17;
        private const int PV_MOCHIE_AL_BLUR = 18;
        private const int PV_MOCHIE_AL_DISTORT = 19;
        private const int PV_MOCHIE_AL_NOISE = 20;
        private const int PV_MOCHIE_AL_FOG = 21;
        private const int PV_MOCHIE_AL_OUTLINE = 22;
        private const int PV_MOCHIE_AL_IMAGE = 23;
        private const int PV_MOCHIE_AL_MISC = 24;
        private const int PRESET_VALUES_STRIDE = 25;
        [UdonSynced] private int[] syncedPresetValues;
        
        // Computed at runtime - number of ints needed per preset for toggle bitmasks
        private int _toggleIntsPerPreset = 1;
        
        // UI state (small, synced separately)
        [UdonSynced] private int currentPage = 0;
        [UdonSynced] private bool isDeleteMode = false;
        private bool isPlayerDataRestored = false;
        
        // ============================================================================
        // ACCESSOR METHODS FOR PACKED ARRAYS
        // ============================================================================
        
        // Values array accessors
        private int GetPresetValue(int presetIndex, int field)
        {
            if (syncedPresetValues == null) return 0;
            int idx = presetIndex * PRESET_VALUES_STRIDE + field;
            return (idx >= 0 && idx < syncedPresetValues.Length) ? syncedPresetValues[idx] : 0;
        }
        
        private void SetPresetValue(int presetIndex, int field, int value)
        {
            if (syncedPresetValues == null) return;
            int idx = presetIndex * PRESET_VALUES_STRIDE + field;
            if (idx >= 0 && idx < syncedPresetValues.Length)
                syncedPresetValues[idx] = value;
        }
        
        // Convenience accessors
        private bool GetPresetHasSnapshot(int presetIndex) => GetPresetValue(presetIndex, PV_HAS_SNAPSHOT) != 0;
        private void SetPresetHasSnapshot(int presetIndex, bool value) => SetPresetValue(presetIndex, PV_HAS_SNAPSHOT, value ? 1 : 0);
        
        private int GetPresetSkyboxIndex(int presetIndex) => GetPresetValue(presetIndex, PV_SKYBOX_INDEX);
        private void SetPresetSkyboxIndex(int presetIndex, int value) => SetPresetValue(presetIndex, PV_SKYBOX_INDEX, value);
        
        private int GetPresetJuneALBand(int presetIndex) => GetPresetValue(presetIndex, PV_JUNE_AL_BAND);
        private void SetPresetJuneALBand(int presetIndex, int value) => SetPresetValue(presetIndex, PV_JUNE_AL_BAND, value);
        
        private int GetPresetJuneALPower(int presetIndex) => GetPresetValue(presetIndex, PV_JUNE_AL_POWER);
        private void SetPresetJuneALPower(int presetIndex, int value) => SetPresetValue(presetIndex, PV_JUNE_AL_POWER, value);
        
        // Toggle bitmask accessors
        private bool GetToggleBit(int presetIndex, int toggleIndex)
        {
            if (syncedToggleBitmasks == null || toggleIndex < 0) return false;
            int intIndex = toggleIndex / 32;
            int bitIndex = toggleIndex % 32;
            int arrayIndex = presetIndex * _toggleIntsPerPreset + intIndex;
            if (arrayIndex < 0 || arrayIndex >= syncedToggleBitmasks.Length) return false;
            return (syncedToggleBitmasks[arrayIndex] & (1 << bitIndex)) != 0;
        }
        
        private void SetToggleBit(int presetIndex, int toggleIndex, bool value)
        {
            if (syncedToggleBitmasks == null || toggleIndex < 0) return;
            int intIndex = toggleIndex / 32;
            int bitIndex = toggleIndex % 32;
            int arrayIndex = presetIndex * _toggleIntsPerPreset + intIndex;
            if (arrayIndex < 0 || arrayIndex >= syncedToggleBitmasks.Length) return;
            if (value)
                syncedToggleBitmasks[arrayIndex] |= (1 << bitIndex);
            else
                syncedToggleBitmasks[arrayIndex] &= ~(1 << bitIndex);
        }
        
        // Confirmation and feedback state
        private bool isSaveConfirmPending = false;
        private bool isLoadConfirmPending = false;
        private float confirmTimeoutStart = 0f;
        private const float ConfirmTimeoutSeconds = 30f;
        private string saveLoadFeedbackMessage = null;
        private float feedbackMessageStart = 0f;
        private const float FeedbackDisplaySeconds = 2f;
        
        // Initial state for reset
        private int initialPage = 0;
        
        public void Awake()
        {
            if (launchpad == null)
            {
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
            }
        }
        
        public void Start()
        {
            Debug.Log($"[PresetHandler] Start() called on {gameObject.name}");
            if (launchpad == null)
            {
                Debug.Log("[PresetHandler] launchpad is null, calling Awake()");
                Awake();
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[PresetHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }
        
        public bool IsReady()
        {
            return launchpad != null;
        }
        
        public void InitializePresetRuntime()
        {
            Debug.Log($"[PresetHandler] InitializePresetRuntime called on {gameObject.name}");
            
            if (!IsReady())
            {
                Debug.LogWarning("[PresetHandler] InitializePresetRuntime skipped - launchpad is null");
                return;
            }
            
            EnsureSnapshotArrays();
            CaptureInitialState();
            Debug.Log("[PresetHandler] InitializePresetRuntime completed");
        }
        
        public void CaptureInitialState()
        {
            initialPage = currentPage;
        }
        
        public void ResetPresets()
        {
            currentPage = initialPage;
            isDeleteMode = false;
            
            // Clear all preset slots
            int presetCount = GetPresetSlotCount();
            for (int i = 0; i < presetCount; i++)
            {
                SetPresetHasSnapshot(i, false);
            }
            
            // Clear toggle bitmasks
            if (syncedToggleBitmasks != null)
            {
                for (int i = 0; i < syncedToggleBitmasks.Length; i++)
                {
                    syncedToggleBitmasks[i] = 0;
                }
            }
            
            RequestSerialization();
        }
        
        public override void OnDeserialization()
        {
            base.OnDeserialization();
            
            if (!IsReady())
            {
                return;
            }
            
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }
        
        #region Handler Interface Methods
        
        public string GetLabel(int buttonIndex)
        {
            if (launchpad != null && buttonIndex == 10)
            {
                int folderIdx = launchpad.FindFolderIndex(ToggleFolderType.Presets);
                return launchpad.GetFolderLabelForIndex(folderIdx, false);
            }
            
            if (buttonIndex == 9)
            {
                return GetPageLabel();
            }
            
            return GetButtonLabel(buttonIndex);
        }
        
        public bool IsInteractable(int buttonIndex)
        {
            if (buttonIndex == 10)
            {
                return true;
            }
            
            if (buttonIndex == 9)
            {
                return GetPageCount() > 1;
            }
            
            int localIndex = currentPage * GetItemsPerPage() + buttonIndex;
            return localIndex < GetTotalEntryCount();
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (!IsReady())
            {
                return false;
            }
            
            if (buttonIndex == 9)
            {
                return false;
            }
            
            int localIndex = currentPage * GetItemsPerPage() + buttonIndex;
            
            // Save button (index 0) - always inactive (no toggle state)
            if (localIndex == 0)
            {
                return false;
            }
            
            // Load button (index 1) - always inactive (no toggle state)
            if (localIndex == 1)
            {
                return false;
            }
            
            // Delete button (index 2) - shows delete mode state
            if (localIndex == 2)
            {
                return isDeleteMode;
            }
            
            // Preset slots - show active if preset matches current state
            if (TryResolvePresetSlot(localIndex, out int presetIndex))
            {
                return IsPresetStateActive(presetIndex);
            }
            
            return false;
        }
        
        public void OnPageChange(int direction)
        {
            if (!IsReady())
            {
                return;
            }
            
            if (!launchpad.CanLocalUserInteract())
            {
                return;
            }
            
            EnsureLocalOwnership();
            
            int totalPages = GetPageCount();
            currentPage = (currentPage + direction + totalPages) % totalPages;
            
            RequestSerialization();
        }
        
        public void OnSelect(int buttonIndex)
        {
            if (!IsReady())
            {
                return;
            }
            
            if (!launchpad.CanLocalUserInteract())
            {
                return;
            }
            
            EnsureLocalOwnership();
            
            int localIndex = currentPage * GetItemsPerPage() + buttonIndex;
            
            // Save button (index 0) - saves all presets to PlayerData
            if (localIndex == 0)
            {
                HandleSaveButtonClick();
                return;
            }
            
            // Load button (index 1) - loads presets from PlayerData
            if (localIndex == 1)
            {
                HandleLoadButtonClick();
                return;
            }
            
            // Delete button (index 2) - toggles delete mode
            if (localIndex == 2)
            {
                isDeleteMode = !isDeleteMode;
                // Reset any pending confirmations when toggling delete mode
                isSaveConfirmPending = false;
                isLoadConfirmPending = false;
                confirmTimeoutStart = 0f;
                RequestSerialization();
                return;
            }
            
            // Preset slots
            if (TryResolvePresetSlot(localIndex, out int presetIndex))
            {
                if (isDeleteMode)
                {
                    // Delete the preset
                    ClearPresetSlot(presetIndex);
                    isDeleteMode = false;
                }
                else if (IsPresetSlotEmpty(presetIndex))
                {
                    // Empty slot - save current state to this slot
                    int mask = BuildPresetCaptureMask();
                    CapturePresetSnapshot(presetIndex, mask);
                }
                else
                {
                    // Filled slot - apply this preset
                    ApplyPresetSnapshot(presetIndex);
                }
                RequestSerialization();
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
            }
        }
        
        private void HandleSaveButtonClick()
        {
            if (isSaveConfirmPending)
            {
                // Second click - actually save
                int savedCount = SavePresetsToPlayerData();
                saveLoadFeedbackMessage = $"Saved\n{savedCount} Presets";
                feedbackMessageStart = Time.time;
                isSaveConfirmPending = false;
                confirmTimeoutStart = 0f;
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
                return;
            }
            
            // Check if presets exist in PlayerData
            if (DoPresetsExistInPlayerData())
            {
                // Presets exist - require confirmation
                isSaveConfirmPending = true;
                isLoadConfirmPending = false;
                confirmTimeoutStart = Time.time;
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
            }
            else
            {
                // No presets in PlayerData - save directly
                int savedCount = SavePresetsToPlayerData();
                saveLoadFeedbackMessage = $"Saved\n{savedCount} Presets";
                feedbackMessageStart = Time.time;
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
            }
        }
        
        private void HandleLoadButtonClick()
        {
            if (isLoadConfirmPending)
            {
                // Second click - actually load
                int loadedCount = LoadPresetsFromPlayerData();
                saveLoadFeedbackMessage = $"Loaded\n{loadedCount} Presets";
                feedbackMessageStart = Time.time;
                isLoadConfirmPending = false;
                confirmTimeoutStart = 0f;
                RequestSerialization();
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
                return;
            }
            
            // Check if any slots on launchpad are not empty
            if (AnyPresetsExistOnBoard())
            {
                // Presets exist on board - require confirmation
                isLoadConfirmPending = true;
                isSaveConfirmPending = false;
                confirmTimeoutStart = Time.time;
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
            }
            else
            {
                // All slots empty - load directly
                int loadedCount = LoadPresetsFromPlayerData();
                saveLoadFeedbackMessage = $"Loaded\n{loadedCount} Presets";
                feedbackMessageStart = Time.time;
                RequestSerialization();
                if (launchpad != null)
                {
                    launchpad.RequestDisplayUpdateFromHandler();
                }
            }
        }
        
        private bool AnyPresetsExistOnBoard()
        {
            int presetCount = GetPresetSlotCount();
            for (int i = 0; i < presetCount; i++)
            {
                if (GetPresetHasSnapshot(i))
                {
                    return true;
                }
            }
            return false;
        }
        
        private bool IsPresetSlotEmpty(int presetIndex)
        {
            return !GetPresetHasSnapshot(presetIndex);
        }
        
        private void ClearPresetSlot(int presetIndex)
        {
            SetPresetHasSnapshot(presetIndex, false);
            
            // Clear toggle bits for this preset
            int totalToggles = GetTotalToggleCount();
            for (int i = 0; i < totalToggles; i++)
            {
                SetToggleBit(presetIndex, i, false);
            }
        }
        
        #endregion
        
        #region UI Helpers
        
        private string GetPageLabel()
        {
            int totalPages = GetPageCount();
            return $"{currentPage + 1}/{totalPages}";
        }
        
        private string GetButtonLabel(int buttonIndex)
        {
            int localIndex = currentPage * GetItemsPerPage() + buttonIndex;
            
            // Check for feedback message timeouts
            TryExpireTimeouts();
            
            if (localIndex == 0)
            {
                // Save button with confirmation/feedback states
                if (saveLoadFeedbackMessage != null && feedbackMessageStart > 0 && saveLoadFeedbackMessage.Contains("Saved"))
                {
                    return saveLoadFeedbackMessage;
                }
                if (isSaveConfirmPending)
                {
                    return "Are You\nSure?";
                }
                return "Save";
            }
            
            if (localIndex == 1)
            {
                // Load button with confirmation/feedback states
                if (saveLoadFeedbackMessage != null && feedbackMessageStart > 0 && saveLoadFeedbackMessage.Contains("Loaded"))
                {
                    return saveLoadFeedbackMessage;
                }
                if (isLoadConfirmPending)
                {
                    return "Are You\nSure?";
                }
                return "Load";
            }
            
            if (localIndex == 2)
            {
                return "Delete";
            }
            
            if (TryResolvePresetSlot(localIndex, out int presetIndex))
            {
                bool hasSnapshot = GetPresetHasSnapshot(presetIndex);
                return hasSnapshot ? $"Preset\n{presetIndex + 1}" : $"Empty\n{presetIndex + 1}";
            }
            
            return string.Empty;
        }
        
        private bool CheckConfirmTimeout()
        {
            if (confirmTimeoutStart > 0f && Time.time - confirmTimeoutStart >= ConfirmTimeoutSeconds)
            {
                isSaveConfirmPending = false;
                isLoadConfirmPending = false;
                confirmTimeoutStart = 0f;
                return true;
            }

            return false;
        }

        private bool CheckFeedbackTimeout()
        {
            if (feedbackMessageStart > 0f && Time.time - feedbackMessageStart >= FeedbackDisplaySeconds)
            {
                saveLoadFeedbackMessage = null;
                feedbackMessageStart = 0f;
                return true;
            }

            return false;
        }

        private bool TryExpireTimeouts()
        {
            bool stateChanged = false;
            stateChanged |= CheckFeedbackTimeout();
            stateChanged |= CheckConfirmTimeout();

            return stateChanged;
        }

        private bool HasTimeoutsPending()
        {
            return feedbackMessageStart > 0f || confirmTimeoutStart > 0f;
        }

        public void Update()
        {
            if (!HasTimeoutsPending())
            {
                return;
            }

            if (TryExpireTimeouts() && launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }
        
        private int GetTotalEntryCount()
        {
            return PresetHeaderEntries + GetPresetSlotCount();
        }
        
        /// <summary>
        /// Calculates the number of preset slots based on presetPages setting.
        /// Page 1 = 6 slots (9 - 3 header buttons)
        /// Page 2+ = 9 slots each
        /// </summary>
        public int GetPresetSlotCount()
        {
            int itemsPerPage = GetItemsPerPage();
            if (itemsPerPage <= 0) itemsPerPage = 9;
            
            // First page has fewer slots due to header buttons
            int firstPageSlots = itemsPerPage - PresetHeaderEntries;
            
            if (presetPages <= 1)
            {
                return firstPageSlots;
            }
            
            // Additional pages have full itemsPerPage slots
            int additionalPages = presetPages - 1;
            return firstPageSlots + (additionalPages * itemsPerPage);
        }

        private int GetItemsPerPage()
        {
            return launchpad != null ? launchpad.GetItemsPerPage() : 9;
        }
        
        private int GetPageCount()
        {
            int itemsPerPage = GetItemsPerPage();
            if (itemsPerPage <= 0) return 1;
            return Mathf.Max(1, Mathf.CeilToInt((float)GetTotalEntryCount() / itemsPerPage));
        }
        
        #endregion
        
        #region Preset Resolution
        
        private bool TryResolvePresetSlot(int localIndex, out int presetIndex)
        {
            presetIndex = -1;
            
            int slotStart = PresetHeaderEntries;
            int slotCount = GetPresetSlotCount();
            if (localIndex >= slotStart && localIndex < slotStart + slotCount)
            {
                presetIndex = localIndex - slotStart;
                return true;
            }
            
            return false;
        }
        
        private int BuildPresetCaptureMask()
        {
            int mask = 0;
            
            if (includedFolderIndices != null && launchpad != null)
            {
                ToggleFolderType[] folderTypes = launchpad.GetFolderTypes();
                if (folderTypes != null)
                {
                    for (int i = 0; i < includedFolderIndices.Length; i++)
                    {
                        int folderIndex = includedFolderIndices[i];
                        if (folderIndex < 0 || folderIndex >= folderTypes.Length)
                        {
                            continue;
                        }
                        
                        mask |= GetMaskForFolderType(folderTypes[folderIndex]);
                    }
                }
            }
            
            if (mask == 0 && !folderSelectionInitialized)
            {
                return PresetDefaultMask;
            }
            
            return mask;
        }
        
        private int GetMaskForFolderType(ToggleFolderType folderType)
        {
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                    return PresetMaskObjects;
                case ToggleFolderType.Materials:
                    return PresetMaskMaterials;
                case ToggleFolderType.Properties:
                    return PresetMaskProperties;
                case ToggleFolderType.Skybox:
                    return PresetMaskSkybox;
                case ToggleFolderType.Stats:
                    return PresetMaskStats;
                case ToggleFolderType.Shaders:
                    return PresetMaskShaders;
                case ToggleFolderType.Mochie:
                    return PresetMaskMochie;
                case ToggleFolderType.June:
                    return PresetMaskJune;
                default:
                    return 0;
            }
        }
        
        #endregion
        
        #region Snapshot Arrays
        
        /// <summary>
        /// Gets the total number of toggles across all handlers (Object, Material, Property, Shader, June).
        /// </summary>
        private int GetTotalToggleCount()
        {
            return GetObjectSnapshotStride() + GetPropertySnapshotStride() + GetShaderSnapshotStride() + GetJuneSnapshotStride();
        }
        
        private void EnsureSnapshotArrays()
        {
            int presetCount = GetPresetSlotCount();
            if (presetCount <= 0)
            {
                ClearAllSnapshotArrays();
                return;
            }
            
            // Calculate how many ints needed for toggle bitmasks
            int totalToggles = GetTotalToggleCount();
            _toggleIntsPerPreset = Mathf.Max(1, Mathf.CeilToInt((float)totalToggles / 32f));
            
            // Resize toggle bitmask array
            ResizeArray(ref syncedToggleBitmasks, presetCount * _toggleIntsPerPreset);
            
            // Resize values array
            ResizeArray(ref syncedPresetValues, presetCount * PRESET_VALUES_STRIDE);
            
            // Initialize skybox index to -1 for empty presets
            for (int i = 0; i < presetCount; i++)
            {
                if (!GetPresetHasSnapshot(i))
                {
                    SetPresetValue(i, PV_SKYBOX_INDEX, -1);
                }
            }
        }
        
        private void ClearAllSnapshotArrays()
        {
            syncedToggleBitmasks = null;
            syncedPresetValues = null;
            _toggleIntsPerPreset = 1;
        }
        
        private int GetObjectSnapshotStride()
        {
            if (launchpad == null) return 0;
            
            int total = 0;
            ObjectHandler[] handlers = launchpad.GetObjectHandlers();
            if (handlers != null)
            {
                for (int i = 0; i < handlers.Length; i++)
                {
                    if (handlers[i] != null)
                    {
                        total += handlers[i].GetEntryCount();
                    }
                }
            }
            
            MaterialHandler[] matHandlers = launchpad.GetMaterialHandlers();
            if (matHandlers != null)
            {
                for (int i = 0; i < matHandlers.Length; i++)
                {
                    if (matHandlers[i] != null)
                    {
                        total += matHandlers[i].GetEntryCount();
                    }
                }
            }
            
            return total;
        }
        
        private int GetPropertySnapshotStride()
        {
            if (launchpad == null) return 0;
            
            int total = 0;
            PropertyHandler[] handlers = launchpad.GetPropertyHandlers();
            if (handlers != null)
            {
                for (int i = 0; i < handlers.Length; i++)
                {
                    if (handlers[i] != null)
                    {
                        total += handlers[i].GetEntryCount();
                    }
                }
            }
            
            return total;
        }
        
        private int GetShaderSnapshotStride()
        {
            if (launchpad == null) return 0;
            
            int total = 0;
            ShaderHandler[] handlers = launchpad.GetShaderHandlers();
            if (handlers != null)
            {
                for (int i = 0; i < handlers.Length; i++)
                {
                    if (handlers[i] != null)
                    {
                        total += handlers[i].GetEntryCount();
                    }
                }
            }
            
            return total;
        }
        
        private int GetJuneSnapshotStride()
        {
            if (launchpad == null) return 0;
            
            int total = 0;
            JuneHandler[] handlers = launchpad.GetJuneHandlers();
            if (handlers != null)
            {
                for (int i = 0; i < handlers.Length; i++)
                {
                    if (handlers[i] != null)
                    {
                        total += handlers[i].GetToggleCount();
                    }
                }
            }
            
            return total;
        }
        
        #endregion
        
        #region Capture Snapshot
        
        private bool CapturePresetSnapshot(int presetIndex, int mask)
        {
            bool captured = false;
            
            if ((mask & (PresetMaskObjects | PresetMaskMaterials)) != 0)
            {
                captured |= CaptureObjectStates(presetIndex, mask);
            }
            
            if ((mask & PresetMaskProperties) != 0)
            {
                captured |= CapturePropertyStates(presetIndex);
            }
            
            if ((mask & PresetMaskShaders) != 0)
            {
                captured |= CaptureShaderStates(presetIndex);
            }
            
            if ((mask & PresetMaskSkybox) != 0)
            {
                captured |= CaptureSkyboxSnapshot(presetIndex);
            }
            
            if ((mask & PresetMaskJune) != 0)
            {
                captured |= CaptureJuneSnapshot(presetIndex);
            }
            
            if ((mask & PresetMaskMochie) != 0)
            {
                captured |= CaptureMochieSnapshot(presetIndex);
            }
            
            if (captured)
            {
                SetPresetHasSnapshot(presetIndex, true);
            }
            
            return captured;
        }
        
        private bool CaptureObjectStates(int presetIndex, int mask)
        {
            if (syncedToggleBitmasks == null) return false;
            
            int toggleOffset = 0;
            bool captured = false;
            
            // Capture Object handler states
            if ((mask & PresetMaskObjects) != 0)
            {
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        ObjectHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        for (int i = 0; i < count; i++)
                        {
                            SetToggleBit(presetIndex, toggleOffset + i, handler.GetEntryState(i));
                            captured = true;
                        }
                        toggleOffset += count;
                    }
                }
            }
            else
            {
                // Skip object handler toggle slots
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        if (handlers[h] != null)
                            toggleOffset += handlers[h].GetEntryCount();
                    }
                }
            }
            
            // Capture Material handler states  
            if ((mask & PresetMaskMaterials) != 0)
            {
                MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        MaterialHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        for (int i = 0; i < count; i++)
                        {
                            SetToggleBit(presetIndex, toggleOffset + i, handler.GetEntryState(i));
                            captured = true;
                        }
                        toggleOffset += count;
                    }
                }
            }
            
            return captured;
        }
        
        private bool CapturePropertyStates(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material toggles)
            int toggleOffset = GetObjectSnapshotStride();
            
            bool captured = false;
            
            PropertyHandler[] handlers = launchpad.GetPropertyHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    PropertyHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetEntryCount();
                    for (int i = 0; i < count; i++)
                    {
                        SetToggleBit(presetIndex, toggleOffset + i, handler.GetEntryState(i));
                        captured = true;
                    }
                    toggleOffset += count;
                }
            }
            
            return captured;
        }
        
        private bool CaptureShaderStates(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material + Property toggles)
            int toggleOffset = GetObjectSnapshotStride() + GetPropertySnapshotStride();
            
            bool captured = false;
            
            ShaderHandler[] handlers = launchpad.GetShaderHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    ShaderHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetEntryCount();
                    for (int i = 0; i < count; i++)
                    {
                        SetToggleBit(presetIndex, toggleOffset + i, handler.GetEntryState(i));
                        captured = true;
                    }
                    toggleOffset += count;
                }
            }
            
            return captured;
        }
        
        private bool CaptureSkyboxSnapshot(int presetIndex)
        {
            SkyboxHandler handler = launchpad.GetSkyboxHandler();
            if (handler == null) return false;
            
            int skyboxIndex = handler.GetActiveSkyboxIndex();
            if (skyboxIndex < 0) return false;
            
            SetPresetSkyboxIndex(presetIndex, skyboxIndex);
            
            return true;
        }
        
        private bool CaptureJuneSnapshot(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material + Property + Shader toggles)
            int toggleOffset = GetObjectSnapshotStride() + GetPropertySnapshotStride() + GetShaderSnapshotStride();
            
            bool captured = false;
            
            JuneHandler[] handlers = launchpad.GetJuneHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    JuneHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetToggleCount();
                    for (int i = 0; i < count; i++)
                    {
                        SetToggleBit(presetIndex, toggleOffset + i, handler.GetToggleState(i));
                        captured = true;
                    }
                    
                    // Capture audiolink state from first June handler
                    if (h == 0)
                    {
                        SetPresetJuneALBand(presetIndex, handler.GetAudiolinkBandState());
                        SetPresetJuneALPower(presetIndex, handler.GetAudiolinkPowerState());
                    }
                    
                    toggleOffset += count;
                }
            }
            
            return captured;
        }
        
        private bool CaptureMochieSnapshot(int presetIndex)
        {
            MochieHandler handler = launchpad.GetMochiHandler();
            if (handler == null) return false;
            
            if (syncedPresetValues == null) return false;
            
            // Capture Mochie indices
            SetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_TYPE, handler.GetOutlineType());
            SetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_STRENGTH, handler.GetOutlineStrengthLevel());
            SetPresetValue(presetIndex, PV_MOCHIE_SCAN_INDEX, handler.GetScanIndex());
            SetPresetValue(presetIndex, PV_MOCHIE_OVERLAY_INDEX, handler.GetOverlayIndex());
            SetPresetValue(presetIndex, PV_MOCHIE_COLOR_INDEX, handler.GetAppliedColorIndex());
            SetPresetValue(presetIndex, PV_MOCHIE_AUDIO_BAND, handler.GetAudioLinkBand());
            
            // Capture +/- effect steps (6 values)
            for (int i = 0; i < 6; i++)
            {
                SetPresetValue(presetIndex, PV_MOCHIE_SATURATION + i, handler.GetEffectStep(i));
            }
            
            // Capture AudioLink strengths (9 values)
            for (int i = 0; i < 9; i++)
            {
                SetPresetValue(presetIndex, PV_MOCHIE_AL_FILTER + i, handler.GetAudioLinkStep(i));
            }
            
            return true;
        }
        
        #endregion
        
        #region Apply Snapshot
        
        private bool ApplyPresetSnapshot(int presetIndex)
        {
            if (!GetPresetHasSnapshot(presetIndex))
            {
                return false;
            }
            
            // Use the editor-configured mask since we no longer store it per-preset
            int mask = BuildPresetCaptureMask();
            
            if (mask == 0)
            {
                return false;
            }
            
            bool changed = false;
            
            if ((mask & (PresetMaskObjects | PresetMaskMaterials)) != 0)
            {
                changed |= ApplyObjectStates(presetIndex, mask);
            }
            
            if ((mask & PresetMaskProperties) != 0)
            {
                changed |= ApplyPropertyStates(presetIndex);
            }
            
            if ((mask & PresetMaskShaders) != 0)
            {
                changed |= ApplyShaderStates(presetIndex);
            }
            
            if ((mask & PresetMaskSkybox) != 0)
            {
                changed |= ApplySkyboxSnapshot(presetIndex);
            }
            
            if ((mask & PresetMaskJune) != 0)
            {
                changed |= ApplyJuneSnapshot(presetIndex);
            }
            
            if ((mask & PresetMaskMochie) != 0)
            {
                changed |= ApplyMochieSnapshot(presetIndex);
            }
            
            return changed;
        }
        
        private bool ApplyObjectStates(int presetIndex, int mask)
        {
            if (syncedToggleBitmasks == null) return false;
            
            int toggleOffset = 0;
            bool changed = false;
            
            // Apply Object handler states
            if ((mask & PresetMaskObjects) != 0)
            {
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        ObjectHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        bool handlerChanged = false;
                        for (int i = 0; i < count; i++)
                        {
                            bool newState = GetToggleBit(presetIndex, toggleOffset + i);
                            if (handler.GetEntryState(i) != newState)
                            {
                                handler.SetEntryState(i, newState);
                                handlerChanged = true;
                            }
                        }
                        
                        if (handlerChanged)
                        {
                            handler.ApplyStates();
                            handler.RequestSerialization();
                            changed = true;
                        }
                        toggleOffset += count;
                    }
                }
            }
            else
            {
                // Skip object handler toggle slots
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        if (handlers[h] != null)
                            toggleOffset += handlers[h].GetEntryCount();
                    }
                }
            }
            
            // Apply Material handler states
            if ((mask & PresetMaskMaterials) != 0)
            {
                MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        MaterialHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        bool matHandlerChanged = false;
                        for (int i = 0; i < count; i++)
                        {
                            bool newState = GetToggleBit(presetIndex, toggleOffset + i);
                            if (handler.GetEntryState(i) != newState)
                            {
                                handler.SetEntryState(i, newState);
                                matHandlerChanged = true;
                            }
                        }
                        
                        if (matHandlerChanged)
                        {
                            handler.ApplyMaterial();
                            handler.RequestSerialization();
                            changed = true;
                        }
                        toggleOffset += count;
                    }
                }
            }
            
            return changed;
        }
        
        private bool ApplyPropertyStates(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material toggles)
            int toggleOffset = GetObjectSnapshotStride();
            
            bool changed = false;
            
            PropertyHandler[] handlers = launchpad.GetPropertyHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    PropertyHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetEntryCount();
                    bool handlerChanged = false;
                    for (int i = 0; i < count; i++)
                    {
                        bool newState = GetToggleBit(presetIndex, toggleOffset + i);
                        if (handler.GetEntryState(i) != newState)
                        {
                            handler.SetEntryState(i, newState);
                            handlerChanged = true;
                        }
                    }
                    
                    if (handlerChanged)
                    {
                        handler.ApplyStates();
                        handler.RequestSerialization();
                        changed = true;
                    }
                    toggleOffset += count;
                }
            }
            
            return changed;
        }
        
        private bool ApplyShaderStates(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material + Property toggles)
            int toggleOffset = GetObjectSnapshotStride() + GetPropertySnapshotStride();
            
            bool changed = false;
            
            ShaderHandler[] handlers = launchpad.GetShaderHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    ShaderHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetEntryCount();
                    bool handlerChanged = false;
                    for (int i = 0; i < count; i++)
                    {
                        bool newState = GetToggleBit(presetIndex, toggleOffset + i);
                        if (handler.GetEntryState(i) != newState)
                        {
                            handler.SetEntryState(i, newState);
                            handlerChanged = true;
                        }
                    }
                    
                    if (handlerChanged)
                    {
                        handler.RequestSerialization();
                        changed = true;
                    }
                    toggleOffset += count;
                }
            }
            
            return changed;
        }
        
        private bool ApplySkyboxSnapshot(int presetIndex)
        {
            SkyboxHandler handler = launchpad.GetSkyboxHandler();
            if (handler == null) return false;
            
            int targetIndex = GetPresetSkyboxIndex(presetIndex);
            if (targetIndex < 0) return false;
            
            // Note: We no longer store skybox page in presets, just the index
            return handler.ApplyPresetSnapshot(targetIndex, 0);
        }
        
        private bool ApplyJuneSnapshot(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return false;
            
            // Calculate toggle offset (after Object + Material + Property + Shader toggles)
            int toggleOffset = GetObjectSnapshotStride() + GetPropertySnapshotStride() + GetShaderSnapshotStride();
            
            bool changed = false;
            
            JuneHandler[] handlers = launchpad.GetJuneHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    JuneHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetToggleCount();
                    bool handlerChanged = false;
                    
                    for (int i = 0; i < count; i++)
                    {
                        bool newState = GetToggleBit(presetIndex, toggleOffset + i);
                        if (handler.GetToggleState(i) != newState)
                        {
                            handler.SetToggleState(i, newState);
                            handlerChanged = true;
                        }
                    }
                    
                    // Apply audiolink state to first June handler
                    if (h == 0)
                    {
                        int band = GetPresetJuneALBand(presetIndex);
                        int power = GetPresetJuneALPower(presetIndex);
                        if (handler.ApplyAudiolinkState(band, power))
                        {
                            handlerChanged = true;
                        }
                    }
                    
                    if (handlerChanged)
                    {
                        handler.ApplyToggles();
                        handler.RequestSerialization();
                        changed = true;
                    }
                    
                    toggleOffset += count;
                }
            }
            
            return changed;
        }
        
        private bool ApplyMochieSnapshot(int presetIndex)
        {
            MochieHandler handler = launchpad.GetMochiHandler();
            if (handler == null) return false;
            
            if (syncedPresetValues == null) return false;
            
            // Apply Mochie indices
            handler.SetOutlineType(GetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_TYPE));
            handler.SetOutlineStrengthLevel(GetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_STRENGTH));
            handler.SetScanIndex(GetPresetValue(presetIndex, PV_MOCHIE_SCAN_INDEX));
            handler.SetOverlayIndex(GetPresetValue(presetIndex, PV_MOCHIE_OVERLAY_INDEX));
            handler.SetAppliedColorIndex(GetPresetValue(presetIndex, PV_MOCHIE_COLOR_INDEX));
            handler.SetAudioLinkBand(GetPresetValue(presetIndex, PV_MOCHIE_AUDIO_BAND));
            
            // Apply +/- effect steps (6 values)
            for (int i = 0; i < 6; i++)
            {
                handler.SetEffectFromStep(i, GetPresetValue(presetIndex, PV_MOCHIE_SATURATION + i));
            }
            
            // Apply AudioLink strengths (9 values)
            for (int i = 0; i < 9; i++)
            {
                handler.SetAudioLinkFromStep(i, GetPresetValue(presetIndex, PV_MOCHIE_AL_FILTER + i));
            }
            
            // Apply changes and sync
            handler.ApplyAllMochieStates();
            handler.RequestSerialization();
            
            return true;
        }
        
        #endregion
        
        #region Is Active Check
        
        private bool IsPresetStateActive(int presetIndex)
        {
            if (!GetPresetHasSnapshot(presetIndex))
            {
                return false;
            }
            
            // Use the editor-configured mask
            int mask = BuildPresetCaptureMask();
            
            if (mask == 0)
            {
                return false;
            }
            
            if ((mask & (PresetMaskObjects | PresetMaskMaterials)) != 0)
            {
                if (!DoesObjectStatesMatch(presetIndex, mask))
                {
                    return false;
                }
            }
            
            if ((mask & PresetMaskProperties) != 0)
            {
                if (!DoesPropertyStatesMatch(presetIndex))
                {
                    return false;
                }
            }
            
            if ((mask & PresetMaskSkybox) != 0)
            {
                if (!DoesSkyboxMatch(presetIndex))
                {
                    return false;
                }
            }
            
            if ((mask & PresetMaskJune) != 0)
            {
                if (!DoesJuneMatch(presetIndex))
                {
                    return false;
                }
            }
            
            if ((mask & PresetMaskMochie) != 0)
            {
                if (!DoesMochieMatch(presetIndex))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        private bool DoesObjectStatesMatch(int presetIndex, int mask)
        {
            if (syncedToggleBitmasks == null) return true;
            
            int toggleOffset = 0;
            
            // Check Object handler states
            if ((mask & PresetMaskObjects) != 0)
            {
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        ObjectHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        for (int i = 0; i < count; i++)
                        {
                            bool savedState = GetToggleBit(presetIndex, toggleOffset + i);
                            if (handler.GetEntryState(i) != savedState)
                            {
                                return false;
                            }
                        }
                        toggleOffset += count;
                    }
                }
            }
            else
            {
                // Skip object handler toggle slots
                ObjectHandler[] handlers = launchpad.GetObjectHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        if (handlers[h] != null)
                            toggleOffset += handlers[h].GetEntryCount();
                    }
                }
            }
            
            // Check Material handler states
            if ((mask & PresetMaskMaterials) != 0)
            {
                MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
                if (handlers != null)
                {
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        MaterialHandler handler = handlers[h];
                        if (handler == null) continue;
                        
                        int count = handler.GetEntryCount();
                        for (int i = 0; i < count; i++)
                        {
                            bool savedState = GetToggleBit(presetIndex, toggleOffset + i);
                            if (handler.GetEntryState(i) != savedState)
                            {
                                return false;
                            }
                        }
                        toggleOffset += count;
                    }
                }
            }
            
            return true;
        }
        
        private bool DoesPropertyStatesMatch(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return true;
            
            // Calculate toggle offset (after Object + Material toggles)
            int toggleOffset = GetObjectSnapshotStride();
            
            PropertyHandler[] handlers = launchpad.GetPropertyHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    PropertyHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetEntryCount();
                    for (int i = 0; i < count; i++)
                    {
                        bool savedState = GetToggleBit(presetIndex, toggleOffset + i);
                        if (handler.GetEntryState(i) != savedState)
                        {
                            return false;
                        }
                    }
                    toggleOffset += count;
                }
            }
            
            return true;
        }
        
        private bool DoesSkyboxMatch(int presetIndex)
        {
            SkyboxHandler handler = launchpad.GetSkyboxHandler();
            if (handler == null) return true;
            
            int savedIndex = GetPresetSkyboxIndex(presetIndex);
            if (savedIndex < 0) return true;
            
            if (handler.GetActiveSkyboxIndex() != savedIndex)
            {
                return false;
            }
            
            // Note: We no longer compare page since we don't store it
            return true;
        }
        
        private bool DoesJuneMatch(int presetIndex)
        {
            if (syncedToggleBitmasks == null) return true;
            
            // Calculate toggle offset (after Object + Material + Property toggles)
            int toggleOffset = GetObjectSnapshotStride() + GetPropertySnapshotStride();
            
            JuneHandler[] handlers = launchpad.GetJuneHandlers();
            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    JuneHandler handler = handlers[h];
                    if (handler == null) continue;
                    
                    int count = handler.GetToggleCount();
                    for (int i = 0; i < count; i++)
                    {
                        bool savedState = GetToggleBit(presetIndex, toggleOffset + i);
                        if (handler.GetToggleState(i) != savedState)
                        {
                            return false;
                        }
                    }
                    
                    // Check audiolink state on first handler
                    if (h == 0)
                    {
                        if (handler.GetAudiolinkBandState() != GetPresetJuneALBand(presetIndex))
                        {
                            return false;
                        }
                        if (handler.GetAudiolinkPowerState() != GetPresetJuneALPower(presetIndex))
                        {
                            return false;
                        }
                    }
                    
                    toggleOffset += count;
                }
            }
            
            return true;
        }
        
        private bool DoesMochieMatch(int presetIndex)
        {
            MochieHandler handler = launchpad.GetMochiHandler();
            if (handler == null) return true;
            
            if (syncedPresetValues == null) return true;
            
            // Compare Mochie indices
            if (handler.GetOutlineType() != GetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_TYPE)) return false;
            if (handler.GetOutlineStrengthLevel() != GetPresetValue(presetIndex, PV_MOCHIE_OUTLINE_STRENGTH)) return false;
            if (handler.GetScanIndex() != GetPresetValue(presetIndex, PV_MOCHIE_SCAN_INDEX)) return false;
            if (handler.GetOverlayIndex() != GetPresetValue(presetIndex, PV_MOCHIE_OVERLAY_INDEX)) return false;
            if (handler.GetAppliedColorIndex() != GetPresetValue(presetIndex, PV_MOCHIE_COLOR_INDEX)) return false;
            if (handler.GetAudioLinkBand() != GetPresetValue(presetIndex, PV_MOCHIE_AUDIO_BAND)) return false;
            
            // Compare +/- effect steps (6 values)
            for (int i = 0; i < 6; i++)
            {
                if (handler.GetEffectStep(i) != GetPresetValue(presetIndex, PV_MOCHIE_SATURATION + i))
                    return false;
            }
            
            // Compare AudioLink strengths (9 values)
            for (int i = 0; i < 9; i++)
            {
                if (handler.GetAudioLinkStep(i) != GetPresetValue(presetIndex, PV_MOCHIE_AL_FILTER + i))
                    return false;
            }
            
            return true;
        }
        
        #endregion
        
        #region Utility
        
        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }
        
        private static void ResizeArray<T>(ref T[] array, int length)
        {
            int targetLength = Mathf.Max(0, length);
            if (array != null && array.Length == targetLength)
            {
                return;
            }
            
            T[] resized = new T[targetLength];
            if (array != null && array.Length > 0 && targetLength > 0)
            {
                Array.Copy(array, resized, Mathf.Min(targetLength, array.Length));
            }
            
            array = resized;
        }
        
        #endregion
        
        #region PlayerData Persistence
        
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player != null && player.isLocal)
            {
                isPlayerDataRestored = true;
                Debug.Log("[PresetHandler] Local player data restored");
            }
        }
        
        private bool DoPresetsExistInPlayerData()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsValid())
            {
                return false;
            }
            
            // Check first slot
            string hasValue = PlayerData.GetString(localPlayer, $"{PlayerDataKeyPrefix}Has_0");
            return !string.IsNullOrEmpty(hasValue);
        }
        
        private int SavePresetsToPlayerData()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsValid())
            {
                Debug.LogWarning("[PresetHandler] Cannot save to PlayerData - local player not valid");
                return 0;
            }
            
            Debug.Log("[PresetHandler] Saving presets to PlayerData");
            int savedCount = 0;
            
            // Save preset slot count
            int slotCount = GetPresetSlotCount();
            PlayerData.SetString($"{PlayerDataKeyPrefix}SlotCount", slotCount.ToString());
            
            // Save each preset
            for (int i = 0; i < slotCount; i++)
            {
                bool hasSnapshot = GetPresetHasSnapshot(i);
                PlayerData.SetString($"{PlayerDataKeyPrefix}Has_{i}", hasSnapshot ? "1" : "0");
                
                if (hasSnapshot)
                {
                    savedCount++;
                    SavePresetToPlayerData(i);
                }
            }
            
            Debug.Log($"[PresetHandler] Saved {savedCount} presets to PlayerData");
            return savedCount;
        }
        
        private void SavePresetToPlayerData(int presetIndex)
        {
            // Save toggle bitmasks as a comma-separated string of ints
            if (syncedToggleBitmasks != null)
            {
                int baseIndex = presetIndex * _toggleIntsPerPreset;
                string toggleData = "";
                for (int i = 0; i < _toggleIntsPerPreset && baseIndex + i < syncedToggleBitmasks.Length; i++)
                {
                    if (i > 0) toggleData += ",";
                    toggleData += syncedToggleBitmasks[baseIndex + i].ToString();
                }
                PlayerData.SetString($"{PlayerDataKeyPrefix}Toggles_{presetIndex}", toggleData);
            }
            
            // Save values array as a comma-separated string of ints
            if (syncedPresetValues != null)
            {
                int baseIndex = presetIndex * PRESET_VALUES_STRIDE;
                string valuesData = "";
                for (int i = 0; i < PRESET_VALUES_STRIDE && baseIndex + i < syncedPresetValues.Length; i++)
                {
                    if (i > 0) valuesData += ",";
                    valuesData += syncedPresetValues[baseIndex + i].ToString();
                }
                PlayerData.SetString($"{PlayerDataKeyPrefix}Values_{presetIndex}", valuesData);
            }
        }
        
        private int LoadPresetsFromPlayerData()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsValid())
            {
                Debug.LogWarning("[PresetHandler] Cannot load from PlayerData - local player not valid");
                return 0;
            }

            EnsureLocalOwnership();
            EnsureSnapshotArrays();
            Debug.Log("[PresetHandler] Loading presets from PlayerData");
            int loadedCount = 0;

            int slotCount = GetPresetSlotCount();
            
            // Load each preset
            for (int i = 0; i < slotCount; i++)
            {
                string hasValue = PlayerData.GetString(localPlayer, $"{PlayerDataKeyPrefix}Has_{i}");
                if (!string.IsNullOrEmpty(hasValue) && hasValue == "1")
                {
                    LoadPresetFromPlayerData(localPlayer, i);
                    SetPresetHasSnapshot(i, true);
                    loadedCount++;
                }
                else
                {
                    SetPresetHasSnapshot(i, false);
                }
            }

            Debug.Log($"[PresetHandler] Loaded {loadedCount} presets from PlayerData");
            return loadedCount;
        }
        
        private void LoadPresetFromPlayerData(VRCPlayerApi localPlayer, int presetIndex)
        {
            // Load toggle bitmasks
            string toggleData = PlayerData.GetString(localPlayer, $"{PlayerDataKeyPrefix}Toggles_{presetIndex}");
            if (!string.IsNullOrEmpty(toggleData) && syncedToggleBitmasks != null)
            {
                string[] toggleParts = toggleData.Split(',');
                int baseIndex = presetIndex * _toggleIntsPerPreset;
                for (int i = 0; i < toggleParts.Length && i < _toggleIntsPerPreset && baseIndex + i < syncedToggleBitmasks.Length; i++)
                {
                    if (int.TryParse(toggleParts[i], out int val))
                    {
                        syncedToggleBitmasks[baseIndex + i] = val;
                    }
                }
            }
            
            // Load values array
            string valuesData = PlayerData.GetString(localPlayer, $"{PlayerDataKeyPrefix}Values_{presetIndex}");
            if (!string.IsNullOrEmpty(valuesData) && syncedPresetValues != null)
            {
                string[] valuesParts = valuesData.Split(',');
                int baseIndex = presetIndex * PRESET_VALUES_STRIDE;
                for (int i = 0; i < valuesParts.Length && i < PRESET_VALUES_STRIDE && baseIndex + i < syncedPresetValues.Length; i++)
                {
                    if (int.TryParse(valuesParts[i], out int val))
                    {
                        syncedPresetValues[baseIndex + i] = val;
                    }
                }
            }
        }
        
        #endregion
    }
}

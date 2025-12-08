using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SkyboxHandler : UdonSharpBehaviour
    {
        [Header("Skybox Routing")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Header("Skyboxes")]
        [Tooltip("Skybox materials available for selection.")]
        public Material[] skyboxMaterials;
        
        [Tooltip("Time in seconds between skybox changes while in Auto folder")]
        public float autoChangeInterval = 90f;
        
        [Tooltip("If true, auto change is enabled by default when the world loads.")]
        public bool autoChangeOnByDefault = false;
        
        [UdonSynced] private int syncedSkyboxIndex = -1;
        [UdonSynced] private int syncedSkyboxPage = 0;
        
        [UdonSynced] private bool isAutoChanging = false;
        private int[] recentSkyboxIndices;
        private int currentHistoryPointer = 0;
        private int historySize;
        private bool skyboxMaterialsValid = false;
        private bool isAutoChangeScheduled = false;
        
        // Chain ID tracking to prevent multiple concurrent auto-change loops.
        // Each time auto-change is toggled (ON or OFF), autoChangeChainId is incremented.
        // Events check if their scheduledChainId matches the current autoChangeChainId.
        // If they don't match, the event is from an old chain and should be ignored.
        private int autoChangeChainId = 0;
        private int scheduledChainId = -1; // -1 = invalid/uninitialized, set when scheduling events
        
        private int initialSkyboxIndex = -1;
        private int initialSkyboxPage = 0;
        private bool initialAutoChangeEnabled = false;
        
        
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
            int instanceId = gameObject.GetInstanceID();
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            bool isOwner = Networking.IsOwner(gameObject);
            Debug.Log($"[SkyboxHandler] Start() called - Player: {localPlayerName}, IsOwner: {isOwner}, InstanceID: {instanceId}, InitialPage: {syncedSkyboxPage}");
            if (launchpad == null)
            {
                Debug.Log("[SkyboxHandler] Start: launchpad is null, calling Awake()");
                Awake();
            }
            else
            {
                Debug.Log("[SkyboxHandler] Start: launchpad already set");
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[SkyboxHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }
        
        public bool IsReady()
        {
            bool ready = launchpad != null;
            Debug.Log($"[SkyboxHandler] IsReady() returning {ready}");
            return ready;
        }
        
        public bool HasValidSkyboxes()
        {
            return skyboxMaterialsValid;
        }
        
        public int GetActiveSkyboxIndex()
        {
            return syncedSkyboxIndex;
        }
        
        public bool IsAutoChanging()
        {
            return isAutoChanging;
        }
        
        public int GetCurrentPage()
        {
            return syncedSkyboxPage;
        }
        
        public int GetTotalPages()
        {
            if (skyboxMaterials == null || skyboxMaterials.Length == 0 || launchpad == null)
            {
                return 1;
            }
            
            return Mathf.Max(1, Mathf.CeilToInt((float)skyboxMaterials.Length / launchpad.GetItemsPerPage()));
        }
        
        public int GetSkyboxCount()
        {
            return skyboxMaterials == null ? 0 : skyboxMaterials.Length;
        }
        
        public int GetSkyboxIndexForButton(int buttonIndex)
        {
            if (!IsReady() || launchpad == null)
            {
                return -1;
            }
            
            int materialIndex = syncedSkyboxPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (skyboxMaterials != null &&
            materialIndex >= 0 &&
            materialIndex < skyboxMaterials.Length)
            {
                return materialIndex;
            }
            
            return -1;
        }
        
        public string GetButtonLabel(int buttonIndex)
        {
            int materialIndex = GetSkyboxIndexForButton(buttonIndex);
            if (materialIndex >= 0 && skyboxMaterials != null && skyboxMaterials[materialIndex] != null)
            {
                return skyboxMaterials[materialIndex].name;
            }
            
            return string.Empty;
        }
        
        public string GetPageOrAutoLabel()
        {
            if (!HasValidSkyboxes())
            {
                return "0/0";
            }
            
            if (isAutoChanging)
            {
                return "Auto\nChange\nEnabled";
            }
            
            int totalPages = GetTotalPages();
            return $"{syncedSkyboxPage + 1}/{totalPages}";
        }
        
        public void InitializeSkyboxRuntime()
        {
            Debug.Log($"[SkyboxHandler] InitializeSkyboxRuntime called, IsReady: {IsReady()}");
            if (!IsReady())
            {
                Debug.LogWarning("[SkyboxHandler] InitializeSkyboxRuntime returning early - not ready (launchpad is null)");
                return;
            }
            
            Debug.Log("[SkyboxHandler] Setting auto-change state to false");
            isAutoChanging = false;
            isAutoChangeScheduled = false;
            autoChangeChainId = 0;
            scheduledChainId = -1;
            
            Debug.Log("[SkyboxHandler] Calling InitializeSkybox");
            InitializeSkybox();
            
            Debug.Log($"[SkyboxHandler] Checking auto-change: autoChangeOnByDefault={autoChangeOnByDefault}, IsOwner={Networking.IsOwner(gameObject)}, CanInteract={launchpad.CanLocalUserInteract()}");
            if (autoChangeOnByDefault && Networking.IsOwner(gameObject) && launchpad.CanLocalUserInteract())
            {
                Debug.Log("[SkyboxHandler] Toggling auto-change on");
                ToggleAutoChangeInternal();
            }
            else
            {
                Debug.Log("[SkyboxHandler] Auto-change off, updating page button state");
                // Ensure page button state is initialized even if auto-change is off
                UpdatePageButtonAutoChangeState();
            }
            
            Debug.Log("[SkyboxHandler] Capturing initial state");
            CaptureInitialState();
            Debug.Log("[SkyboxHandler] InitializeSkyboxRuntime completed");
        }
        
        public void RefreshSkyboxSetup()
        {
            InitializeSkybox();
        }
        
        public void CaptureInitialState()
        {
            initialSkyboxIndex = syncedSkyboxIndex;
            initialSkyboxPage = syncedSkyboxPage;
            initialAutoChangeEnabled = isAutoChanging;
        }
        
        public void ResetSkybox()
        {
            isAutoChanging = false;
            isAutoChangeScheduled = false;
            autoChangeChainId = 0;
            scheduledChainId = -1;
            
            syncedSkyboxIndex = initialSkyboxIndex;
            syncedSkyboxPage = initialSkyboxPage < 0 ? 0 : initialSkyboxPage;
            isAutoChanging = initialAutoChangeEnabled;
            
            UpdateSkyboxActiveSystem();
            
            // Update page button state after resetting isAutoChanging
            UpdatePageButtonAutoChangeState();
            
            ResumeAutoChangeIfNeeded(0f);
            RequestSerialization();
        }
        
        public void ApplyActiveSkybox()
        {
            UpdateSkyboxActiveSystem();
        }
        
        /// <summary>
        /// Applies a preset snapshot to the skybox.
        /// Used by PresetHandler when applying presets.
        /// </summary>
        public bool ApplyPresetSnapshot(int targetIndex, int targetPage)
        {
            bool changed = false;
            
            if (syncedSkyboxIndex != targetIndex)
            {
                syncedSkyboxIndex = targetIndex;
                changed = true;
            }
            
            if (syncedSkyboxPage != targetPage)
            {
                syncedSkyboxPage = targetPage;
                changed = true;
            }
            
            if (changed)
            {
                UpdateSkyboxActiveSystem();
                RequestSerialization();
            }
            
            return changed;
        }
        
        public string GetLabel(int buttonIndex)
        {
            if (launchpad != null && buttonIndex == 10)
            {
                int folderIdx = launchpad.GetSkyboxFolderObjectIndex();
                return launchpad.GetFolderLabelForIndex(folderIdx, true);
            }
            
            if (!HasValidSkyboxes())
            {
                return buttonIndex == 9 ? "0/0" : string.Empty;
            }
            
            if (buttonIndex == 9)
            {
                return GetPageOrAutoLabel();
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
                return true;
            }
            
            if (!HasValidSkyboxes())
            {
                return false;
            }
            
            return !string.IsNullOrEmpty(GetLabel(buttonIndex));
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (!IsReady() || launchpad == null)
            {
                return false;
            }
            
            if (buttonIndex == 9)
            {
                return isAutoChanging;
            }
            
            if (!HasValidSkyboxes())
            {
                return false;
            }
            
            return (syncedSkyboxPage * launchpad.GetItemsPerPage() + buttonIndex) == syncedSkyboxIndex;
        }
        
        public void OnPageChange(int direction)
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            int instanceId = gameObject.GetInstanceID();
            Debug.Log($"[SkyboxHandler] OnPageChange START - Player: {localPlayerName}, Direction: {direction}, CurrentPage: {syncedSkyboxPage}, IsOwner: {Networking.IsOwner(gameObject)}, InstanceID: {instanceId}");
            
            if (!IsReady())
            {
                Debug.LogWarning($"[SkyboxHandler] OnPageChange ABORT - IsReady() returned false");
                return;
            }
            
            if (!launchpad.CanLocalUserInteract())
            {
                Debug.LogWarning($"[SkyboxHandler] OnPageChange ABORT - CanLocalUserInteract returned false");
                return;
            }
            
            Debug.Log($"[SkyboxHandler] OnPageChange - About to EnsureLocalOwnership");
            EnsureLocalOwnership();
            
            int oldPage = syncedSkyboxPage;
            UpdateSkyboxPage(direction);
            Debug.Log($"[SkyboxHandler] OnPageChange - Page changed from {oldPage} to {syncedSkyboxPage}, InstanceID: {instanceId}");
            
            Debug.Log($"[SkyboxHandler] OnPageChange - Calling RequestSerialization to sync syncedSkyboxPage={syncedSkyboxPage} to other players");
            RequestSerialization();
            Debug.Log($"[SkyboxHandler] OnPageChange END - Player: {localPlayerName}, NewPage: {syncedSkyboxPage}, InstanceID: {instanceId}");
        }
        
        public void OnSelect(int buttonIndex)
        {
            if (!IsReady())
            {
                return;
            }
            
            SelectSkybox(buttonIndex);
        }
        
        public void ToggleAutoChange()
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
            ToggleAutoChangeInternal();
        }
        
        public void _ProcessAutoChange()
        {
            // Check if this event belongs to the current auto-change chain
            // scheduledChainId should always be set when this method is called through normal scheduling,
            // but check for -1 to handle edge cases where the event might be triggered improperly
            if (scheduledChainId == -1 || scheduledChainId != autoChangeChainId)
            {
                // This event belongs to a previous auto-change chain that has been superseded,
                // or was not properly scheduled
                return;
            }
            
            isAutoChangeScheduled = false;
            if (!isAutoChanging || !Networking.IsOwner(gameObject) || skyboxMaterials == null || skyboxMaterials.Length == 0)
            return;
            AutoChangeSkybox();
            if (isAutoChanging && autoChangeInterval > 0f && !isAutoChangeScheduled)
            {
                isAutoChangeScheduled = true;
                scheduledChainId = autoChangeChainId;
                SendCustomEventDelayedSeconds(nameof(_ProcessAutoChange), autoChangeInterval);
            }
        }
        
        public void AutoChangeSkybox()
        {
            int newIndex = GetNewSkyboxIndex();
            if (newIndex < 0) return;
            syncedSkyboxIndex = newIndex;
            syncedSkyboxPage = newIndex / launchpad.GetItemsPerPage();
            AddToRecent(newIndex);
            UpdateSkyboxActiveSystem();
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
            RequestSerialization();
        }
        
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!player.isLocal || !isAutoChanging)
            {
                return;
            }
            
            ResumeAutoChangeIfNeeded();
        }
        
        public override void OnDeserialization()
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            bool isOwner = Networking.IsOwner(gameObject);
            int instanceId = gameObject.GetInstanceID();
            Debug.Log($"[SkyboxHandler] OnDeserialization START - Player: {localPlayerName}, IsOwner: {isOwner}, ReceivedPage: {syncedSkyboxPage}, InstanceID: {instanceId}");
            
            base.OnDeserialization();
            
            Debug.Log($"[SkyboxHandler] OnDeserialization AFTER BASE - syncedSkyboxPage is now: {syncedSkyboxPage}, InstanceID: {instanceId}");
            
            if (!IsReady())
            {
                Debug.LogWarning($"[SkyboxHandler] OnDeserialization - IsReady() returned false, skipping HandleDeserializationState");
                return;
            }
            
            Debug.Log($"[SkyboxHandler] OnDeserialization - Calling HandleDeserializationState with page: {syncedSkyboxPage}");
            HandleDeserializationState();
            Debug.Log($"[SkyboxHandler] OnDeserialization END - Player: {localPlayerName}, Page: {syncedSkyboxPage}, InstanceID: {instanceId}");
        }
        
        private void ToggleAutoChangeInternal()
        {
            isAutoChanging = !isAutoChanging;
            
            if (!isAutoChanging)
            {
                isAutoChangeScheduled = false;
            }
            
            // Increment chain ID on every toggle to invalidate any pending auto-change events
            // from the previous session. This prevents multiple concurrent event loops.
            autoChangeChainId++;
            
            ResumeAutoChangeIfNeeded(0f);
            
            // Update the page button's auto-change animation state
            UpdatePageButtonAutoChangeState();
            
            RequestSerialization();
        }
        
        /// <summary>
        /// Updates the page button's auto-change animation state.
        /// Should be called whenever isAutoChanging changes.
        /// </summary>
        private void UpdatePageButtonAutoChangeState()
        {
            if (launchpad == null)
            {
                return;
            }
            
            ButtonHandler pageButton = launchpad.GetPageButtonHandler();
            if (pageButton != null)
            {
                pageButton.SetAutoChangeActive(isAutoChanging);
            }
        }
        
        public void OnLaunchpadDeserialized()
        {
            ApplyActiveSkybox();
            // Synchronize page button animation state with the deserialized isAutoChanging flag from network
            UpdatePageButtonAutoChangeState();
        }
        
        private void InitializeSkybox()
        {
            skyboxMaterialsValid = (skyboxMaterials != null && skyboxMaterials.Length > 0);
            
            if (skyboxMaterialsValid)
            {
                historySize = Mathf.CeilToInt(skyboxMaterials.Length * 0.3f);
                recentSkyboxIndices = new int[historySize];
                for (int i = 0; i < historySize; i++) recentSkyboxIndices[i] = -1;
                currentHistoryPointer = 0;
                
                if (Networking.IsOwner(gameObject) && syncedSkyboxIndex == -1)
                {
                    Material defaultSkybox = RenderSettings.skybox;
                    if (defaultSkybox != null)
                    {
                        string cleanDefault = defaultSkybox.name.Replace(" (Instance)", "");
                        for (int i = 0; i < skyboxMaterials.Length; i++)
                        {
                            if (skyboxMaterials[i] == null) continue;
                            string cleanMat = skyboxMaterials[i].name.Replace(" (Instance)", "");
                            if (cleanMat == cleanDefault)
                            {
                                syncedSkyboxIndex = i;
                                syncedSkyboxPage = launchpad != null && launchpad.GetItemsPerPage() > 0
                                ? i / launchpad.GetItemsPerPage()
                                : 0;
                                RequestSerialization();
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                historySize = 0;
                recentSkyboxIndices = new int[0];
            }
        }
        
        private void HandleDeserializationState()
        {
            UpdateSkyboxActiveSystem();
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
            ResumeAutoChangeIfNeeded();
        }
        
        private void ResumeAutoChangeIfNeeded(float delayOverrideSeconds = -1f)
        {
            if (isAutoChanging)
            {
                if (Networking.IsOwner(gameObject) && !isAutoChangeScheduled)
                {
                    float delay = delayOverrideSeconds >= 0f
                    ? Mathf.Max(0f, delayOverrideSeconds)
                    : Mathf.Max(0f, autoChangeInterval);
                    isAutoChangeScheduled = true;
                    scheduledChainId = autoChangeChainId;
                    SendCustomEventDelayedSeconds(nameof(_ProcessAutoChange), delay);
                }
            }
            else
            {
                isAutoChangeScheduled = false;
            }
        }

        private void EnsureLocalOwnership()
        {
            bool wasOwner = Networking.IsOwner(gameObject);
            if (!wasOwner)
            {
                string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
                Debug.Log($"[SkyboxHandler] EnsureLocalOwnership - Player: {localPlayerName} taking ownership");
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        private void UpdateSkyboxPage(int direction)
        {
            if (skyboxMaterials == null || skyboxMaterials.Length == 0 || launchpad == null) return;
            int totalPages = Mathf.CeilToInt((float)skyboxMaterials.Length / launchpad.GetItemsPerPage());
            syncedSkyboxPage = (syncedSkyboxPage + direction + totalPages) % totalPages;
        }
        
        private void UpdateSkyboxActiveSystem()
        {
            if (skyboxMaterials != null &&
            syncedSkyboxIndex >= 0 &&
            syncedSkyboxIndex < skyboxMaterials.Length)
            {
                RenderSettings.skybox = skyboxMaterials[syncedSkyboxIndex];
            }
        }
        
        private void SelectSkybox(int buttonIndex)
        {
            if (!launchpad.CanLocalUserInteract())
            return;
            
            int materialIndex = syncedSkyboxPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (skyboxMaterials == null ||
            materialIndex < 0 ||
            materialIndex >= skyboxMaterials.Length) return;
            
            EnsureLocalOwnership();

            syncedSkyboxIndex = materialIndex;
            ApplyActiveSkybox();
            RequestSerialization();
        }
        
        private int GetNewSkyboxIndex()
        {
            if (skyboxMaterials == null || skyboxMaterials.Length == 0) return -1;
            int[] available = new int[skyboxMaterials.Length];
            int availableCount = 0;
            for (int i = 0; i < skyboxMaterials.Length; i++)
            {
                if (!IsIndexInRecent(i))
                {
                    available[availableCount++] = i;
                }
            }
            return (availableCount > 0)
            ? available[Random.Range(0, availableCount)]
            : Random.Range(0, skyboxMaterials.Length);
        }
        
        private bool IsIndexInRecent(int index)
        {
            if (recentSkyboxIndices == null) return false;
            for (int i = 0; i < recentSkyboxIndices.Length; i++)
            if (recentSkyboxIndices[i] == index) return true;
            return false;
        }
        
        private void AddToRecent(int index)
        {
            if (recentSkyboxIndices == null || recentSkyboxIndices.Length == 0) return;
            recentSkyboxIndices[currentHistoryPointer] = index;
            currentHistoryPointer = (currentHistoryPointer + 1) % recentSkyboxIndices.Length;
        }
    }
}

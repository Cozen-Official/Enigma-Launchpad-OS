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
        
        // Robust auto-change loop prevention:
        // - autoChangeChainId: Monotonically increasing counter, incremented every time auto-change is toggled OFF
        //   or when performing a full reset. This invalidates any pending events from previous sessions.
        // - scheduledChainId: Set when scheduling an event, stores the chainId at scheduling time.
        //   Events check if scheduledChainId == autoChangeChainId to validate they're from the current session.
        // - isEventPending: True from when an event is scheduled until it fires (regardless of outcome).
        //   This prevents scheduling multiple events - only one event can be "in flight" at a time.
        //   Key insight: this flag is ONLY reset when an event actually fires, never by other code paths.
        //   This ensures that even during Reset operations, we wait for pending events to complete
        //   before scheduling new ones.
        private int autoChangeChainId = 0;
        private int scheduledChainId = -1;
        private bool isEventPending = false;
        
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
            
            // Increment chain ID to invalidate any potentially pending events from previous sessions.
            // Note: At true initialization (Start), there shouldn't be pending events, but in VRChat
            // late joiners may have synced state. Incrementing maintains safety and monotonic ordering.
            autoChangeChainId++;
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
            // First, disable auto-change to stop any new scheduling
            isAutoChanging = false;
            
            // Increment chain ID to invalidate any pending events
            // Note: We do NOT clear isEventPending here. If there's a pending event,
            // it will fire, see the chain mismatch, and clear the flag itself.
            // This prevents scheduling multiple concurrent events.
            autoChangeChainId++;
            scheduledChainId = -1;
            
            // Restore initial state
            syncedSkyboxIndex = initialSkyboxIndex;
            syncedSkyboxPage = initialSkyboxPage < 0 ? 0 : initialSkyboxPage;
            isAutoChanging = initialAutoChangeEnabled;
            
            UpdateSkyboxActiveSystem();
            
            // Update page button state after resetting isAutoChanging
            UpdatePageButtonAutoChangeState();
            
            // Attempt to resume auto-change. If there's a pending event (isEventPending=true),
            // this won't schedule a new one. When the pending event fires, it will see the
            // chain mismatch, exit early, and call ResumeAutoChangeIfNeeded() to start fresh.
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
            // Mark that the pending event has now fired.
            // This is done FIRST, before any validation checks, because regardless of whether
            // this event is valid or stale, it's no longer "pending" - it has executed.
            isEventPending = false;
            
            // Validate this event belongs to the current auto-change session.
            // If scheduledChainId doesn't match autoChangeChainId, this event is from a
            // previous session that was cancelled when the user toggled auto-change OFF.
            if (scheduledChainId == -1 || scheduledChainId != autoChangeChainId)
            {
                // Stale event from a cancelled session - ignore it completely.
                // Do NOT change the skybox, but DO check if we should resume auto-change
                // for the current session (in case user toggled it back ON).
                if (isAutoChanging && Networking.IsOwner(gameObject))
                {
                    // User toggled auto-change back ON while this stale event was pending.
                    // Now that this event has cleared isEventPending, schedule a new event
                    // for the current session.
                    ResumeAutoChangeIfNeeded();
                }
                return;
            }
            
            // Event is valid. Check if we should still be auto-changing.
            if (!isAutoChanging || !Networking.IsOwner(gameObject) || skyboxMaterials == null || skyboxMaterials.Length == 0)
            {
                return;
            }
            
            // Perform the skybox change
            AutoChangeSkybox();
            
            // Schedule the next auto-change event, but ONLY if:
            // 1. Auto-change is still enabled
            // 2. The interval is valid
            // 3. No other event is already pending (defensive check - should always be false here)
            if (isAutoChanging && autoChangeInterval > 0f && !isEventPending)
            {
                isEventPending = true;
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
                // Turning OFF: Increment chain ID to invalidate any pending events.
                // IMPORTANT: Do NOT reset isEventPending here. The pending event will still fire,
                // but it will see the chain ID mismatch and exit early. When it fires, IT will
                // reset isEventPending to false. This prevents a race condition where we could
                // schedule multiple events.
                autoChangeChainId++;
            }
            else
            {
                // Turning ON: Only schedule a new event if there isn't one already pending.
                // If there IS a pending event (from before we toggled OFF), it will fire and
                // see the chain ID mismatch, then reset isEventPending. We don't want to
                // schedule a NEW event that would run concurrently.
                //
                // However, if the user toggled OFF and the pending event already fired (clearing
                // isEventPending), we DO want to schedule a new event now.
                ResumeAutoChangeIfNeeded(0f);
            }
            
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
            if (!isAutoChanging)
            {
                // Auto-change is OFF - don't schedule anything.
                // Note: We do NOT reset isEventPending here. If there's a pending event,
                // it will fire and see the chain ID mismatch, then reset the flag itself.
                return;
            }
            
            // Auto-change is ON. Only schedule if we're the owner AND no event is pending.
            if (Networking.IsOwner(gameObject) && !isEventPending)
            {
                float delay = delayOverrideSeconds >= 0f
                    ? Mathf.Max(0f, delayOverrideSeconds)
                    : Mathf.Max(0f, autoChangeInterval);
                    
                isEventPending = true;
                scheduledChainId = autoChangeChainId;
                SendCustomEventDelayedSeconds(nameof(_ProcessAutoChange), delay);
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

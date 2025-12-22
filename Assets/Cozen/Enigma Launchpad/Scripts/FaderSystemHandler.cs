using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cozen
{
    /// <summary>
    /// FaderSystemHandler manages the configuration and runtime state of both static and dynamic shader faders.
    /// Static faders have fixed targets configured in the editor.
    /// Dynamic faders are assigned when toggles from other folders (Objects, Materials, Properties, etc.) become active.
    /// </summary>
    public class FaderSystemHandler : UdonSharpBehaviour
    {
        // Maximum number of faders supported by the launchpad
        private const int MaxFaders = 9;
        // Hysteresis threshold for switching grabbed fader (prevents flickering)
        private const float FaderSwitchThreshold = 0.01f;

        [Header("Fader Routing")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;

        [Tooltip("FaderHandler behaviours mapped to the nine launchpad faders.")]
        public FaderHandler[] faders;

        [Tooltip("Shared collider for left hand interactions across all faders.")]
        [SerializeField] private GameObject leftHandCollider;
        [Tooltip("Shared collider for right hand interactions across all faders.")]
        [SerializeField] private GameObject rightHandCollider;

        [Header("Static Fader Configuration")]
        [Tooltip("Number of the nine faders configured as dynamic controls (0-9). The rest are static.")]
        [Range(0, 9)]
        public int dynamicFaderCount = 0;

        [Tooltip("Optional names for static faders (non-dynamic).")]
        public string[] staticFaderNames;

        [Tooltip("Folder index (Materials/Properties) targeted by each static fader; -1 when unused.")]
        public int[] staticFaderTargetFolders;

        [Tooltip("Whether each static fader should use custom renderer targets.")]
        public bool[] staticFaderTargetsCustom;

        [Tooltip("Material index used when driving renderer materials for each static fader.")]
        public int[] staticFaderMaterialIndices;

        [Tooltip("Shader property names targeted by each static fader.")]
        public string[] staticFaderPropertyNames;

        [Tooltip("Shader property types targeted by each static fader (aligned with staticFaderPropertyNames).")]
        public int[] staticFaderPropertyTypes;

        [Tooltip("Renderer counts for each static fader when targeting custom renderers.")]
        public int[] staticFaderRendererCounts;

        [Tooltip("Custom renderers associated with static faders in a flat list.")]
        public Renderer[] staticFaderRenderers;

        [Tooltip("Minimum value for each static fader.")]
        public float[] staticFaderMinValues;

        [Tooltip("Maximum value for each static fader.")]
        public float[] staticFaderMaxValues;

        [Tooltip("Default value for each static fader.")]
        public float[] staticFaderDefaultValues;

        [Tooltip("Default color for each static fader when using color properties.")]
        public Color[] staticFaderDefaultColors;

        [Tooltip("Whether each static fader should light its indicator.")]
        public bool[] staticFaderColorIndicatorsEnabled;

        [Tooltip("Indicator color for each static fader when enabled.")]
        public Color[] staticFaderIndicatorColors;

        [Tooltip("If true, the static fader indicator lights only when the value is above Min.")]
        public bool[] staticFaderIndicatorConditional;

        [Header("Dynamic Fader Configuration")]
        [Tooltip("Metadata for dynamic fader labels.")]
        public string[] dynamicFaderNames;

        [Tooltip("Folder indices targeted by each dynamic fader entry.")]
        public int[] dynamicFaderFolders;

        [Tooltip("Toggle indices targeted by each dynamic fader entry.")]
        public int[] dynamicFaderToggles;

        [Tooltip("Material index used when driving renderer materials for each dynamic fader.")]
        public int[] dynamicFaderMaterialIndices;

        [Tooltip("Shader property names targeted by each dynamic fader entry.")]
        public string[] dynamicFaderPropertyNames;

        [Tooltip("Shader property types targeted by each dynamic fader entry.")]
        public int[] dynamicFaderPropertyTypes;

        [Tooltip("Minimum value for each dynamic fader entry.")]
        public float[] dynamicFaderMinValues;

        [Tooltip("Maximum value for each dynamic fader entry.")]
        public float[] dynamicFaderMaxValues;

        [Tooltip("Default value for each dynamic fader entry.")]
        public float[] dynamicFaderDefaultValues;

        [Tooltip("Default color for each dynamic fader entry when using color properties.")]
        public Color[] dynamicFaderDefaultColors;

        [Tooltip("Whether each dynamic fader should light its indicator when active.")]
        public bool[] dynamicFaderColorIndicatorsEnabled;

        [Tooltip("Indicator color for each dynamic fader when enabled.")]
        public Color[] dynamicFaderIndicatorColors;

        [Tooltip("If true, the dynamic fader indicator lights only when the value is above Min.")]
        public bool[] dynamicFaderIndicatorConditional;

        // Runtime state
        private string[] faderSlotLabels;
        private int[] faderDynamicSources;
        private bool[] faderStaticAssignments;
        private Color[] faderIndicatorAppliedColors;
        private float[] faderIndicatorAppliedEmission;
        private bool faderBindingsInitialized;
        private bool faderSyncReady;
        private bool pendingFaderSerialization;
        private VRCPlayerApi localPlayer;
        private bool handColliderUpdatesEnabled = false;
        private bool whitelistCheckPending = true;

        // Grab coordination: tracks which fader index is currently grabbed by each hand (-1 = none)
        private int _leftHandGrabbedFaderIndex = -1;
        private int _rightHandGrabbedFaderIndex = -1;

        // Tracks whether pickup mode is enabled (true = VRC Pickup, false = hand collider)
        private bool _pickupModeEnabled = false;

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
            Debug.Log($"[FaderSystemHandler] Start() called on {gameObject.name}");
            if (launchpad == null)
            {
                Debug.Log("[FaderSystemHandler] Start: launchpad is null, calling Awake()");
                Awake();
            }

            localPlayer = Networking.LocalPlayer;
            ApplyHandColliderAssignments();
        }

        private void Update()
        {
            // Check authorization on first update if still pending
            if (whitelistCheckPending)
            {
                UpdateHandColliderAuthorizationState();
            }
            
            UpdateHandColliderPositions();
        }

        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[FaderSystemHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }

        public bool IsReady()
        {
            return launchpad != null;
        }

        /// <summary>
        /// Initialize fader runtime state. Should be called after launchpad is set.
        /// </summary>
        public void InitializeFaderRuntime()
        {
            Debug.Log("[FaderSystemHandler] InitializeFaderRuntime called");
            if (launchpad == null)
            {
                Debug.LogWarning("[FaderSystemHandler] InitializeFaderRuntime skipped - launchpad is null");
                return;
            }

            ApplyHandColliderAssignments();
            InitializeStaticFaders();
            UpdateDynamicFaderBindings();
            UpdateFaderLabels();
            UpdateFaderIndicators();
            Debug.Log("[FaderSystemHandler] InitializeFaderRuntime completed");
        }

        /// <summary>
        /// Called by EnigmaLaunchpad when a toggle state changes to update dynamic fader bindings.
        /// </summary>
        public void OnToggleStateChanged()
        {
            Debug.Log("[FaderSystemHandler] OnToggleStateChanged called - updating dynamic fader bindings");
            UpdateDynamicFaderBindings();
            UpdateFaderLabels();
            UpdateFaderIndicators();
        }

        /// <summary>
        /// Updates the label text on each FaderHandler based on faderSlotLabels.
        /// </summary>
        private void UpdateFaderLabels()
        {
            if (faders == null || faderSlotLabels == null)
            {
                Debug.Log($"[FaderSystemHandler] UpdateFaderLabels: Skipping - faders={faders != null}, faderSlotLabels={faderSlotLabels != null}");
                return;
            }

            int count = Mathf.Min(faders.Length, faderSlotLabels.Length);
            Debug.Log($"[FaderSystemHandler] UpdateFaderLabels: Updating {count} fader labels");
            for (int i = 0; i < count; i++)
            {
                FaderHandler fader = faders[i];
                if (fader != null)
                {
                    string label = faderSlotLabels[i];
                    Debug.Log($"[FaderSystemHandler] UpdateFaderLabels: Setting fader[{i}] label to '{label}'");
                    fader.SetLabel(label);
                }
                else
                {
                    Debug.LogWarning($"[FaderSystemHandler] UpdateFaderLabels: faders[{i}] is null");
                }
            }
        }

        private void ApplyHandColliderAssignments()
        {
            if (faders == null)
            {
                return;
            }

            int count = Mathf.Min(MaxFaders, faders.Length);
            for (int i = 0; i < count; i++)
            {
                FaderHandler fader = faders[i];
                if (fader != null)
                {
                    fader.SetHandColliders(leftHandCollider, rightHandCollider);
                }
            }
        }

        /// <summary>
        /// Called by FaderHandler when a hand enters its trigger zone.
        /// Determines if this fader should be granted grab permission based on distance.
        /// Note: Once a fader is grabbed, it stays locked until release - no switching during grab.
        /// </summary>
        /// <param name="faderIndex">Index of the fader requesting permission</param>
        /// <param name="isRightHand">True for right hand, false for left hand</param>
        public void OnFaderTriggerEnter(int faderIndex, bool isRightHand)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return;
            }

            int currentGrabbed = isRightHand ? _rightHandGrabbedFaderIndex : _leftHandGrabbedFaderIndex;

            // If no fader is currently assigned to this hand, grant permission to this one
            // Once a fader is grabbed, it stays locked - no switching until grab is released
            if (currentGrabbed < 0)
            {
                GrantGrabPermission(faderIndex, isRightHand);
            }
            // If another fader is already assigned, don't switch - stay locked to current
        }

        /// <summary>
        /// Called by FaderHandler when a hand exits its trigger zone.
        /// </summary>
        /// <param name="faderIndex">Index of the fader that was exited</param>
        /// <param name="isRightHand">True for right hand, false for left hand</param>
        public void OnFaderTriggerExit(int faderIndex, bool isRightHand)
        {
            int currentGrabbed = isRightHand ? _rightHandGrabbedFaderIndex : _leftHandGrabbedFaderIndex;

            // Only process if this was the fader that had grab permission
            if (currentGrabbed != faderIndex)
            {
                // Still revoke permission on the fader even if it wasn't the grabbed one
                // This handles edge cases where state gets out of sync
                RevokeGrabPermission(faderIndex, isRightHand);
                return;
            }

            // Revoke grab permission from the fader
            RevokeGrabPermission(faderIndex, isRightHand);

            // Clear the tracked grab index
            if (isRightHand)
            {
                _rightHandGrabbedFaderIndex = -1;
            }
            else
            {
                _leftHandGrabbedFaderIndex = -1;
            }

            // Check if any other fader is still in contact with this hand and grant permission to the closest one
            FindAndGrantClosestFader(isRightHand);
        }

        /// <summary>
        /// Called by FaderHandler when grab input starts.
        /// Re-evaluates which fader should be grabbed based on current hand position.
        /// </summary>
        /// <param name="isRightHand">True for right hand, false for left hand</param>
        public void OnGrabStarted(bool isRightHand)
        {
            // Clear any stale assignment first
            ClearHandAssignment(isRightHand);

            // Pick the closest fader currently in trigger zone
            FindAndGrantClosestFader(isRightHand);
        }

        /// <summary>
        /// Called by FaderHandler when grab input ends.
        /// Clears the grab lock so the next grab can re-evaluate closest fader.
        /// </summary>
        /// <param name="isRightHand">True for right hand, false for left hand</param>
        public void OnGrabEnded(bool isRightHand)
        {
            ClearHandAssignment(isRightHand);
        }

        /// <summary>
        /// Clears the grab assignment for the specified hand, revoking permission from any currently assigned fader.
        /// </summary>
        private void ClearHandAssignment(bool isRightHand)
        {
            int currentIndex = isRightHand ? _rightHandGrabbedFaderIndex : _leftHandGrabbedFaderIndex;
            if (currentIndex >= 0)
            {
                RevokeGrabPermission(currentIndex, isRightHand);
            }

            if (isRightHand)
            {
                _rightHandGrabbedFaderIndex = -1;
            }
            else
            {
                _leftHandGrabbedFaderIndex = -1;
            }
        }

        private void GrantGrabPermission(int faderIndex, bool isRightHand)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return;
            }

            if (isRightHand)
            {
                _rightHandGrabbedFaderIndex = faderIndex;
            }
            else
            {
                _leftHandGrabbedFaderIndex = faderIndex;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader != null)
            {
                fader.SetGrabPermission(isRightHand, true);
            }
        }

        private void RevokeGrabPermission(int faderIndex, bool isRightHand)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader != null)
            {
                fader.SetGrabPermission(isRightHand, false);
            }
        }

        private void FindAndGrantClosestFader(bool isRightHand)
        {
            if (faders == null)
            {
                return;
            }

            Vector3 handPosition = GetHandPosition(isRightHand);
            if (handPosition == Vector3.zero)
            {
                return;
            }

            int closestIndex = -1;
            float closestDistance = float.MaxValue;

            int count = Mathf.Min(MaxFaders, faders.Length);
            for (int i = 0; i < count; i++)
            {
                FaderHandler fader = faders[i];
                if (fader == null)
                {
                    continue;
                }

                // Only consider faders that are still in the trigger zone for this hand
                if (!fader.IsInTrigger(isRightHand))
                {
                    continue;
                }

                float distance = Vector3.Distance(handPosition, fader.GetWorldPosition());
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                GrantGrabPermission(closestIndex, isRightHand);
            }
        }

        private Vector3 GetHandPosition(bool isRightHand)
        {
            GameObject handCollider = isRightHand ? rightHandCollider : leftHandCollider;
            if (handCollider != null)
            {
                return handCollider.transform.position;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Called by FaderHandler when a color property value changes.
        /// Updates the indicator color to match the newly computed color.
        /// </summary>
        /// <param name="faderIndex">Index of the fader whose color changed</param>
        public void OnFaderColorChanged(int faderIndex)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader == null || fader.propertyType != 2)
            {
                return;
            }

            // Check if indicator is enabled for this fader
            bool isStatic = faderStaticAssignments != null && faderIndex < faderStaticAssignments.Length && faderStaticAssignments[faderIndex];
            int dynamicSource = faderDynamicSources != null && faderIndex < faderDynamicSources.Length ? faderDynamicSources[faderIndex] : -1;

            bool indicatorEnabled = false;
            bool indicatorConditional = false;
            float threshold = 0f;

            if (isStatic)
            {
                indicatorEnabled = IsStaticFaderIndicatorEnabled(faderIndex);
                indicatorConditional = IsStaticFaderIndicatorConditional(faderIndex);
                threshold = GetStaticFaderMinValue(faderIndex);
            }
            else if (dynamicSource >= 0)
            {
                indicatorEnabled = IsDynamicFaderIndicatorEnabled(dynamicSource);
                indicatorConditional = IsDynamicFaderIndicatorConditional(dynamicSource);
                threshold = GetDynamicFaderMinValue(dynamicSource);
            }

            if (!indicatorEnabled)
            {
                return;
            }

            // Get the current computed color from the fader
            Color indicatorColor = fader.GetCurrentComputedColor();
            float currentValue = fader.currentValue;

            // Apply the indicator update
            bool active = !indicatorConditional || currentValue > threshold;
            Color targetColor = active ? indicatorColor : (launchpad != null ? launchpad.inactiveColor : Color.black);
            float emission = active ? 1f : 0f;

            ApplyFaderIndicator(faderIndex, targetColor, emission);
        }

        private void UpdateHandColliderPositions()
        {
            // Only update hand colliders if the player is authorized
            if (!handColliderUpdatesEnabled)
            {
                return;
            }
            
            if (localPlayer == null)
            {
                localPlayer = Networking.LocalPlayer;
            }

            if (localPlayer == null || !localPlayer.IsUserInVR())
            {
                return;
            }

            if (rightHandCollider != null)
            {
                Vector3 rightHandData = localPlayer.GetBonePosition(HumanBodyBones.RightIndexDistal);
                if (IsValidBonePosition(rightHandData))
                {
                    rightHandCollider.transform.position = rightHandData;
                }
            }

            if (leftHandCollider != null)
            {
                Vector3 leftHandData = localPlayer.GetBonePosition(HumanBodyBones.LeftIndexDistal);
                if (IsValidBonePosition(leftHandData))
                {
                    leftHandCollider.transform.position = leftHandData;
                }
            }
        }

        /// <summary>
        /// Updates the authorization state for hand collider position syncing.
        /// Called by EnigmaLaunchpad when the whitelist is initialized or updated.
        /// </summary>
        public void UpdateHandColliderAuthorizationState()
        {
            if (launchpad == null)
            {
                Debug.LogWarning("[FaderSystemHandler] Cannot check authorization - launchpad reference is null");
                return;
            }
            
            bool wasEnabled = handColliderUpdatesEnabled;
            handColliderUpdatesEnabled = launchpad.CanLocalUserInteract();
            whitelistCheckPending = false;
            
            if (handColliderUpdatesEnabled != wasEnabled)
            {
                if (handColliderUpdatesEnabled)
                {
                    Debug.Log("[FaderSystemHandler] Hand collider position updates ENABLED for authorized player");
                }
                else
                {
                    Debug.Log("[FaderSystemHandler] Hand collider position updates DISABLED for unauthorized player");
                }
            }
        }

        /// <summary>
        /// Toggles between VRC Pickup mode and Hand Collider mode for fader control.
        /// Called by a UI toggle button. This is LOCAL only - each player controls their own mode.
        /// </summary>
        public void TogglePickupMode()
        {
            SetPickupModeEnabled(!_pickupModeEnabled);
        }

        /// <summary>
        /// Sets the pickup mode for all faders.
        /// When enabled, faders can be grabbed and moved using VRC Pickup.
        /// When disabled, faders are controlled via hand collider tracking.
        /// This is LOCAL only - each player controls their own mode.
        /// </summary>
        /// <param name="usePickupMode">True to enable VRC Pickup mode, false for hand collider mode.</param>
        public void SetPickupModeEnabled(bool usePickupMode)
        {
            if (faders == null) return;

            _pickupModeEnabled = usePickupMode;

            for (int i = 0; i < faders.Length; i++)
            {
                FaderHandler fader = faders[i];
                if (fader == null) continue;

                // Toggle the VRC Pickup and Rigidbody settings on each fader
                fader.SetPickupMode(usePickupMode);
            }

            // Disable hand collider updates when in pickup mode (local only)
            // Hand collider updates are enabled only when NOT in pickup mode AND the user is authorized
            handColliderUpdatesEnabled = !usePickupMode && launchpad != null && launchpad.CanLocalUserInteract();

            Debug.Log($"[FaderSystemHandler] Pickup mode {(usePickupMode ? "ENABLED" : "DISABLED")}. Hand collider updates: {handColliderUpdatesEnabled}");
        }

        /// <summary>
        /// Returns whether pickup mode is currently enabled.
        /// </summary>
        public bool IsPickupModeEnabled()
        {
            return _pickupModeEnabled;
        }

        private bool IsValidBonePosition(Vector3 position)
        {
            if (position == Vector3.zero)
            {
                return false;
            }

            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z))
            {
                return false;
            }

            if (float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Enable serialization for faders after initial setup is complete.
        /// </summary>
        public void EnableFaderSerialization()
        {
            faderSyncReady = true;
            if (pendingFaderSerialization)
            {
                RequestPendingFaderSerialization();
            }
            Debug.Log("[FaderSystemHandler] Fader sync enabled");
        }

        /// <summary>
        /// Reset all faders to their default values.
        /// </summary>
        public void ResetAllFaders()
        {
            ResetStaticFaders();
            UpdateDynamicFaderBindings();
            UpdateFaderIndicators();
        }

        /// <summary>
        /// Get the label for a specific fader slot.
        /// </summary>
        public string GetFaderLabel(int faderIndex)
        {
            if (faderSlotLabels == null || faderIndex < 0 || faderIndex >= faderSlotLabels.Length)
            {
                return string.Empty;
            }
            return faderSlotLabels[faderIndex];
        }

        private void UpdateDynamicFaderBindings()
        {
            int faderCount = faders != null ? Mathf.Min(MaxFaders, faders.Length) : 0;
            int staticSlotCount = faderCount - Mathf.Clamp(dynamicFaderCount, 0, faderCount);

            Debug.Log($"[FaderSystemHandler] UpdateDynamicFaderBindings: faderCount={faderCount}, dynamicFaderCount={dynamicFaderCount}, staticSlotCount={staticSlotCount}");

            if (faderCount <= 0)
            {
                Debug.Log("[FaderSystemHandler] UpdateDynamicFaderBindings: No faders to update");
                return;
            }

            if (!faderBindingsInitialized)
            {
                faderDynamicSources = new int[MaxFaders];
                faderSlotLabels = new string[MaxFaders];
                faderStaticAssignments = new bool[MaxFaders];
                faderIndicatorAppliedColors = new Color[MaxFaders];
                faderIndicatorAppliedEmission = new float[MaxFaders];

                faderBindingsInitialized = true;
                for (int i = 0; i < faderDynamicSources.Length; i++)
                {
                    faderDynamicSources[i] = -1;
                }

                for (int i = 0; i < faderSlotLabels.Length; i++)
                {
                    faderSlotLabels[i] = string.Empty;
                }

                for (int i = 0; i < faderStaticAssignments.Length; i++)
                {
                    faderStaticAssignments[i] = false;
                }
            }

            int[] nextDynamicSources = new int[MaxFaders];
            bool[] nextStaticAssignments = new bool[MaxFaders];

            for (int i = 0; i < nextDynamicSources.Length; i++)
            {
                nextDynamicSources[i] = -1;
                nextStaticAssignments[i] = false;
            }

            for (int i = 0; i < faderSlotLabels.Length; i++)
            {
                faderSlotLabels[i] = string.Empty;
            }

            for (int i = 0; i < staticSlotCount; i++)
            {
                nextStaticAssignments[i] = true;
                string label = GetStaticFaderLabel(i);
                faderSlotLabels[i] = label;
                Debug.Log($"[FaderSystemHandler] UpdateDynamicFaderBindings: Static fader[{i}] label set to '{label}'");
            }

            int nextDynamicSlot = staticSlotCount;
            int dynamicEntryCount = GetDynamicFaderEntryCount();
            for (int dynamicIndex = 0; dynamicIndex < dynamicEntryCount && nextDynamicSlot < faderCount; dynamicIndex++)
            {
                if (!IsDynamicFaderValid(dynamicIndex))
                {
                    continue;
                }

                if (!IsDynamicFaderConditionActive(dynamicIndex))
                {
                    continue;
                }

                nextDynamicSources[nextDynamicSlot] = dynamicIndex;
                faderSlotLabels[nextDynamicSlot] = GetDynamicFaderLabel(dynamicIndex);
                nextDynamicSlot++;
            }

            for (int i = 0; i < faderCount; i++)
            {
                int previousSource = faderDynamicSources[i];
                bool previouslyStatic = faderStaticAssignments[i];
                int nextSource = nextDynamicSources[i];
                bool nextStatic = nextStaticAssignments[i];

                faderDynamicSources[i] = nextSource;
                faderStaticAssignments[i] = nextStatic;

                if (nextStatic)
                {
                    if (!previouslyStatic || previousSource != -1)
                    {
                        ApplyStaticFaderConfiguration(i, !previouslyStatic);
                    }
                    continue;
                }

                if (nextSource >= 0)
                {
                    bool assignmentChanged = previouslyStatic || nextSource != previousSource;
                    ApplyDynamicFaderConfiguration(nextSource, i, assignmentChanged);
                    continue;
                }

                if (previouslyStatic || previousSource >= 0)
                {
                    ClearFaderSlot(i);
                }
            }
        }

        private void ApplyDynamicFaderConfiguration(int dynamicIndex, int faderIndex, bool resetValue)
        {
            FaderHandler fader = faders != null && faderIndex >= 0 && faderIndex < faders.Length ? faders[faderIndex] : null;
            if (fader == null)
            {
                return;
            }

            string propertyName = GetDynamicFaderPropertyName(dynamicIndex);
            Material[] targets = BuildDynamicFaderMaterials(dynamicIndex, propertyName);

            fader.materialPropertyId = propertyName;
            fader.targetMaterials = targets;
            fader.propertyType = GetDynamicFaderPropertyType(dynamicIndex);
            fader.valueMin = GetDynamicFaderMinValue(dynamicIndex);
            fader.valueMax = GetDynamicFaderMaxValue(dynamicIndex);
            fader.defaultValue = GetDynamicFaderDefaultValue(dynamicIndex);
            fader.defaultColor = GetDynamicFaderDefaultColor(dynamicIndex);

            if (resetValue)
            {
                fader.ResetFaderPosition();
                if (faderSyncReady)
                {
                    fader.RequestSerialization();
                }
                else
                {
                    pendingFaderSerialization = true;
                }
            }
        }

        private void ClearFaderSlot(int faderIndex)
        {
            FaderHandler fader = faders != null && faderIndex >= 0 && faderIndex < faders.Length ? faders[faderIndex] : null;
            if (fader == null)
            {
                return;
            }

            fader.materialPropertyId = string.Empty;
            fader.targetMaterials = new Material[0];
            fader.valueMin = 0f;
            fader.valueMax = 1f;
            fader.defaultValue = 0f;
            fader.ResetFaderPosition();
        }

        private void UpdateFaderIndicators()
        {
            if (launchpad == null)
            {
                return;
            }

            int faderCount = faders != null ? Mathf.Min(MaxFaders, faders.Length) : 0;

            Color inactiveColor = launchpad.inactiveColor;

            for (int i = 0; i < faderCount; i++)
            {
                bool isStatic = faderStaticAssignments != null && faderStaticAssignments[i];
                int dynamicSource = faderDynamicSources != null ? faderDynamicSources[i] : -1;

                bool indicatorEnabled = false;
                bool indicatorConditional = false;
                Color indicatorColor = inactiveColor;
                float threshold = 0f;
                float currentValue = 0f;
                int propertyType = 0;

                if (isStatic)
                {
                    indicatorEnabled = IsStaticFaderIndicatorEnabled(i);
                    indicatorConditional = IsStaticFaderIndicatorConditional(i);
                    indicatorColor = GetStaticFaderIndicatorColor(i);
                    threshold = GetStaticFaderMinValue(i);
                    propertyType = GetStaticFaderPropertyType(i);
                }
                else if (dynamicSource >= 0)
                {
                    indicatorEnabled = IsDynamicFaderIndicatorEnabled(dynamicSource);
                    indicatorConditional = IsDynamicFaderIndicatorConditional(dynamicSource);
                    indicatorColor = GetDynamicFaderIndicatorColor(dynamicSource);
                    threshold = GetDynamicFaderMinValue(dynamicSource);
                    propertyType = GetDynamicFaderPropertyType(dynamicSource);
                }

                if (indicatorEnabled && i < faderCount)
                {
                    FaderHandler fader = faders[i];
                    if (fader != null)
                    {
                        currentValue = fader.currentValue;
                        
                        // For color properties (propertyType == 2), use the computed color from the fader
                        if (propertyType == 2)
                        {
                            indicatorColor = fader.GetCurrentComputedColor();
                        }
                    }
                }

                bool active = indicatorEnabled && (!indicatorConditional || currentValue > threshold);
                Color targetColor = active ? indicatorColor : inactiveColor;
                float emission = active ? 1f : 0f;

                ApplyFaderIndicator(i, targetColor, emission);
            }
        }

        private void InitializeStaticFaders()
        {
            if (faders == null || faders.Length == 0)
            {
                return;
            }

            int totalFaders = Mathf.Min(MaxFaders, faders.Length);
            for (int faderIndex = 0; faderIndex < totalFaders; faderIndex++)
            {
                ApplyStaticFaderConfiguration(faderIndex, true);
            }
        }

        private void ResetStaticFaders()
        {
            if (faders == null || faders.Length == 0)
            {
                return;
            }

            int totalFaders = Mathf.Min(MaxFaders, faders.Length);
            for (int faderIndex = 0; faderIndex < totalFaders; faderIndex++)
            {
                ApplyStaticFaderConfiguration(faderIndex, true);
            }
        }

        private void RequestPendingFaderSerialization()
        {
            pendingFaderSerialization = false;
            if (faders == null || faders.Length == 0)
            {
                return;
            }

            int totalFaders = Mathf.Min(MaxFaders, faders.Length);
            for (int i = 0; i < totalFaders; i++)
            {
                FaderHandler fader = faders[i];
                if (fader != null)
                {
                    fader.RequestSerialization();
                }
            }
        }

        private void ApplyStaticFaderConfiguration(int faderIndex, bool applyDefaultValue)
        {
            FaderHandler fader = faders != null && faderIndex >= 0 && faderIndex < faders.Length ? faders[faderIndex] : null;
            if (fader == null)
            {
                return;
            }

            string propertyName = GetStaticFaderPropertyName(faderIndex);
            Material[] targets = BuildStaticFaderMaterials(faderIndex, propertyName);

            fader.materialPropertyId = propertyName;
            fader.targetMaterials = targets;
            fader.propertyType = GetStaticFaderPropertyType(faderIndex);
            fader.valueMin = GetStaticFaderMinValue(faderIndex);
            fader.valueMax = GetStaticFaderMaxValue(faderIndex);
            fader.defaultValue = GetStaticFaderDefaultValue(faderIndex);
            fader.defaultColor = GetStaticFaderDefaultColor(faderIndex);

            if (applyDefaultValue)
            {
                fader.ResetFaderPosition();
            }
        }

        private Material[] BuildStaticFaderMaterials(int faderIndex, string propertyName)
        {
            Renderer[] renderers;
            int[] materialIndices;
            Material[] directMaterials;
            BuildStaticFaderShaderTarget(faderIndex, out renderers, out materialIndices, out directMaterials);
            return BuildMaterialTargets(renderers, materialIndices, directMaterials, propertyName);
        }

        private Material[] BuildDynamicFaderMaterials(int dynamicIndex, string propertyName)
        {
            Renderer[] renderers;
            int[] materialIndices;
            Material[] directMaterials;
            BuildDynamicFaderShaderTarget(dynamicIndex, out renderers, out materialIndices, out directMaterials);
            return BuildMaterialTargets(renderers, materialIndices, directMaterials, propertyName);
        }

        private Material[] BuildMaterialTargets(Renderer[] renderers, int[] materialIndices, Material[] directMaterials, string propertyName)
        {
            int rendererCount = renderers != null ? renderers.Length : 0;
            int directCount = directMaterials != null ? directMaterials.Length : 0;
            int validCount = 0;

            for (int i = 0; i < rendererCount; i++)
            {
                int materialIndex = (materialIndices != null && i < materialIndices.Length) ? materialIndices[i] : 0;
                Renderer renderer = renderers[i];
                Material[] shared = renderer != null ? renderer.sharedMaterials : null;
                if (shared != null && materialIndex >= 0 && materialIndex < shared.Length)
                {
                    Material mat = shared[materialIndex];
                    if (mat != null && (string.IsNullOrEmpty(propertyName) || mat.HasProperty(propertyName)))
                    {
                        validCount++;
                    }
                }
            }

            for (int i = 0; i < directCount; i++)
            {
                Material direct = directMaterials[i];
                if (direct != null && (string.IsNullOrEmpty(propertyName) || direct.HasProperty(propertyName)))
                {
                    validCount++;
                }
            }

            if (validCount <= 0)
            {
                return new Material[0];
            }

            Material[] targets = new Material[validCount];
            int insert = 0;

            for (int i = 0; i < rendererCount; i++)
            {
                int materialIndex = (materialIndices != null && i < materialIndices.Length) ? materialIndices[i] : 0;
                Renderer renderer = renderers[i];
                Material[] shared = renderer != null ? renderer.sharedMaterials : null;
                if (shared != null && materialIndex >= 0 && materialIndex < shared.Length)
                {
                    Material mat = shared[materialIndex];
                    if (mat != null && (string.IsNullOrEmpty(propertyName) || mat.HasProperty(propertyName)))
                    {
                        targets[insert] = mat;
                        insert++;
                    }
                }
            }

            for (int i = 0; i < directCount; i++)
            {
                Material direct = directMaterials[i];
                if (direct != null && (string.IsNullOrEmpty(propertyName) || direct.HasProperty(propertyName)))
                {
                    targets[insert] = direct;
                    insert++;
                }
            }

            return targets;
        }

        private void BuildStaticFaderShaderTarget(int faderIndex, out Renderer[] renderers, out int[] materialIndices, out Material[] directMaterials)
        {
            int materialIndex = GetStaticFaderMaterialIndex(faderIndex);

            if (IsStaticFaderCustomTarget(faderIndex))
            {
                BuildCustomStaticFaderTarget(faderIndex, materialIndex, out renderers, out materialIndices, out directMaterials);
                return;
            }

            int folderIndex = GetStaticFaderFolderIndex(faderIndex);
            if (folderIndex >= 0 && launchpad != null)
            {
                BuildFolderStaticFaderTarget(folderIndex, materialIndex, out renderers, out materialIndices, out directMaterials);
                return;
            }

            PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
        }

        private void BuildDynamicFaderShaderTarget(int dynamicIndex, out Renderer[] renderers, out int[] materialIndices, out Material[] directMaterials)
        {
            int folderIndex = GetDynamicFaderFolderIndex(dynamicIndex);
            if (folderIndex < 0 || launchpad == null)
            {
                PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                return;
            }

            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            switch (folderType)
            {
                case ToggleFolderType.Objects:
                {
                    // For Object folders, get the GameObject from the toggle and extract its Renderer
                    int toggleIndex = GetDynamicFaderToggleIndex(dynamicIndex);
                    int materialIndex = GetDynamicFaderMaterialIndex(dynamicIndex);
                    ObjectHandler objHandler = launchpad.GetObjectHandlerForFolder(folderIndex);
                    if (objHandler != null && objHandler.folderEntries != null &&
                        toggleIndex >= 0 && toggleIndex < objHandler.folderEntries.Length)
                    {
                        UnityEngine.Object entry = objHandler.folderEntries[toggleIndex];
                        if (entry != null && entry.GetType() == typeof(GameObject))
                        {
                            GameObject targetObject = (GameObject)entry;
                            Renderer targetRenderer = targetObject.GetComponent<Renderer>();
                            if (targetRenderer != null)
                            {
                                PrepareShaderTarget(new[] { targetRenderer }, new[] { materialIndex }, null, out renderers, out materialIndices, out directMaterials);
                                return;
                            }
                        }
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Materials:
                {
                    // For Materials folders, target the specific material from the toggle
                    // instead of using a fixed material index (which would be wrong for non-exclusive folders)
                    int toggleIndex = GetDynamicFaderToggleIndex(dynamicIndex);
                    MaterialHandler matHandler = launchpad.GetMaterialHandlerForFolder(folderIndex);
                    if (matHandler != null && matHandler.folderEntries != null &&
                        toggleIndex >= 0 && toggleIndex < matHandler.folderEntries.Length)
                    {
                        Material targetMaterial = matHandler.folderEntries[toggleIndex];
                        if (targetMaterial != null)
                        {
                            PrepareShaderTarget(new Renderer[0], new int[0], new[] { targetMaterial }, out renderers, out materialIndices, out directMaterials);
                            return;
                        }
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Properties:
                case ToggleFolderType.Mochie:
                case ToggleFolderType.Skybox:
                    BuildFolderStaticFaderTarget(folderIndex, 0, out renderers, out materialIndices, out directMaterials);
                    return;

                case ToggleFolderType.Shaders:
                {
                    // For Shader folders, target the specific material from the toggle
                    int toggleIndex = GetDynamicFaderToggleIndex(dynamicIndex);
                    ShaderHandler shaderHandler = launchpad.GetShaderHandlerForFolder(folderIndex);
                    if (shaderHandler != null && shaderHandler.shaderMaterials != null &&
                        toggleIndex >= 0 && toggleIndex < shaderHandler.shaderMaterials.Length)
                    {
                        Material targetMaterial = shaderHandler.shaderMaterials[toggleIndex];
                        if (targetMaterial != null)
                        {
                            PrepareShaderTarget(new Renderer[0], new int[0], new[] { targetMaterial }, out renderers, out materialIndices, out directMaterials);
                            return;
                        }
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.June:
                {
                    // For June folders, target the June material from the handler
                    JuneHandler juneHandler = launchpad.GetJuneHandlerForFolder(folderIndex);
                    if (juneHandler != null && juneHandler.juneMaterial != null)
                    {
                        PrepareShaderTarget(new Renderer[0], new int[0], new[] { juneHandler.juneMaterial }, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    // Fallback: try to use the renderer if material is not directly accessible
                    if (juneHandler != null && juneHandler.juneRenderer != null)
                    {
                        PrepareShaderTarget(new[] { juneHandler.juneRenderer }, new[] { 0 }, null, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                default:
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
            }
        }

        private void BuildCustomStaticFaderTarget(int faderIndex, int materialIndex, out Renderer[] renderers, out int[] materialIndices, out Material[] directMaterials)
        {
            int rendererCount = GetStaticFaderRendererCount(faderIndex);
            int rendererStart = GetStaticFaderRendererStartIndex(faderIndex);
            int available = staticFaderRenderers != null ? staticFaderRenderers.Length : 0;
            int count = Mathf.Clamp(rendererCount, 0, Mathf.Max(0, available - rendererStart));

            Renderer[] targets = new Renderer[count];
            int[] materialSlots = new int[count];
            for (int i = 0; i < count; i++)
            {
                int flatIndex = rendererStart + i;
                targets[i] = (flatIndex >= 0 && flatIndex < available) ? staticFaderRenderers[flatIndex] : null;
                materialSlots[i] = materialIndex;
            }

            PrepareShaderTarget(targets, materialSlots, null, out renderers, out materialIndices, out directMaterials);
        }

        private void BuildFolderStaticFaderTarget(int folderIndex, int materialIndex, out Renderer[] renderers, out int[] materialIndices, out Material[] directMaterials)
        {
            if (launchpad == null)
            {
                PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                return;
            }

            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            switch (folderType)
            {
                case ToggleFolderType.Properties:
                {
                    PropertyHandler propHandler = launchpad.GetPropertyHandlerForFolder(folderIndex);
                    if (propHandler != null && propHandler.propertyRenderers != null && propHandler.propertyRenderers.Length > 0)
                    {
                        Renderer[] propRenderers = propHandler.propertyRenderers;
                        int[] propMaterialIndices = new int[propRenderers.Length];
                        for (int i = 0; i < propMaterialIndices.Length; i++)
                        {
                            propMaterialIndices[i] = materialIndex;
                        }
                        PrepareShaderTarget(propRenderers, propMaterialIndices, null, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Materials:
                {
                    MaterialHandler matHandler = launchpad.GetMaterialHandlerForFolder(folderIndex);
                    if (matHandler != null)
                    {
                        // Use the folder's renderers for targeting
                        Renderer[] matRenderers = matHandler.folderMaterialRenderers;
                        if (matRenderers != null && matRenderers.Length > 0)
                        {
                            int[] matMaterialIndices = new int[matRenderers.Length];
                            for (int i = 0; i < matMaterialIndices.Length; i++)
                            {
                                matMaterialIndices[i] = materialIndex;
                            }
                            PrepareShaderTarget(matRenderers, matMaterialIndices, null, out renderers, out materialIndices, out directMaterials);
                            return;
                        }
                        // Fallback: use folderEntries as direct materials if no renderers configured
                        if (matHandler.folderEntries != null && matHandler.folderEntries.Length > 0)
                        {
                            PrepareShaderTarget(new Renderer[0], new int[0], matHandler.folderEntries, out renderers, out materialIndices, out directMaterials);
                            return;
                        }
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Mochie:
                {
                    Renderer shaderRenderer = launchpad.GetShaderRenderer();
                    MochieHandler mochiHandler = launchpad.GetMochiHandler();
                    Material mochieMaterial = mochiHandler != null ? mochiHandler.activeMochieMaterial : null;
                    PrepareShaderTarget(new[] { shaderRenderer }, new[] { materialIndex }, new[] { mochieMaterial }, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Shaders:
                {
                    // For Shader folders, use the shader materials as direct materials
                    ShaderHandler shaderHandler = launchpad.GetShaderHandlerForFolder(folderIndex);
                    if (shaderHandler != null && shaderHandler.shaderMaterials != null && shaderHandler.shaderMaterials.Length > 0)
                    {
                        PrepareShaderTarget(new Renderer[0], new int[0], shaderHandler.shaderMaterials, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.Skybox:
                {
                    Material skyboxMaterial = RenderSettings.skybox;
                    PrepareShaderTarget(new Renderer[0], new int[0], new[] { skyboxMaterial }, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                case ToggleFolderType.June:
                {
                    // For June folders, target the June material from the handler
                    JuneHandler juneHandler = launchpad.GetJuneHandlerForFolder(folderIndex);
                    if (juneHandler != null && juneHandler.juneMaterial != null)
                    {
                        PrepareShaderTarget(new Renderer[0], new int[0], new[] { juneHandler.juneMaterial }, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    // Fallback: try to use the renderer if material is not directly accessible
                    if (juneHandler != null && juneHandler.juneRenderer != null)
                    {
                        PrepareShaderTarget(new[] { juneHandler.juneRenderer }, new[] { materialIndex }, null, out renderers, out materialIndices, out directMaterials);
                        return;
                    }
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
                }

                default:
                    PrepareShaderTarget(new Renderer[0], new int[0], null, out renderers, out materialIndices, out directMaterials);
                    return;
            }
        }

        private void PrepareShaderTarget(Renderer[] sourceRenderers, int[] sourceMaterialIndices, Material[] sourceDirect,
            out Renderer[] renderers, out int[] materialIndices, out Material[] directMaterials)
        {
            renderers = sourceRenderers ?? new Renderer[0];
            materialIndices = sourceMaterialIndices ?? new int[0];
            directMaterials = sourceDirect ?? new Material[0];
        }

        private int GetStaticFaderMaterialIndex(int faderIndex)
        {
            if (staticFaderMaterialIndices == null || faderIndex < 0 || faderIndex >= staticFaderMaterialIndices.Length)
            {
                return 0;
            }

            int index = staticFaderMaterialIndices[faderIndex];
            return index < 0 ? 0 : index;
        }

        private bool IsStaticFaderCustomTarget(int faderIndex)
        {
            return staticFaderTargetsCustom != null && faderIndex >= 0 && faderIndex < staticFaderTargetsCustom.Length && staticFaderTargetsCustom[faderIndex];
        }

        private int GetStaticFaderFolderIndex(int faderIndex)
        {
            if (staticFaderTargetFolders == null || faderIndex < 0 || faderIndex >= staticFaderTargetFolders.Length)
            {
                return -1;
            }

            return staticFaderTargetFolders[faderIndex];
        }

        private string GetStaticFaderPropertyName(int faderIndex)
        {
            if (staticFaderPropertyNames == null || faderIndex < 0 || faderIndex >= staticFaderPropertyNames.Length)
            {
                return string.Empty;
            }

            string propertyName = staticFaderPropertyNames[faderIndex];
            return propertyName ?? string.Empty;
        }

        private float GetStaticFaderMinValue(int faderIndex)
        {
            if (staticFaderMinValues == null || faderIndex < 0 || faderIndex >= staticFaderMinValues.Length)
            {
                return 0f;
            }

            return staticFaderMinValues[faderIndex];
        }

        private float GetStaticFaderMaxValue(int faderIndex)
        {
            if (staticFaderMaxValues == null || faderIndex < 0 || faderIndex >= staticFaderMaxValues.Length)
            {
                return 1f;
            }

            return staticFaderMaxValues[faderIndex];
        }

        private float GetStaticFaderDefaultValue(int faderIndex)
        {
            if (staticFaderDefaultValues == null || faderIndex < 0 || faderIndex >= staticFaderDefaultValues.Length)
            {
                return 0f;
            }

            return staticFaderDefaultValues[faderIndex];
        }

        private int GetStaticFaderPropertyType(int faderIndex)
        {
            if (staticFaderPropertyTypes == null || faderIndex < 0 || faderIndex >= staticFaderPropertyTypes.Length)
            {
                return 0;
            }

            return staticFaderPropertyTypes[faderIndex];
        }

        private Color GetStaticFaderDefaultColor(int faderIndex)
        {
            if (staticFaderDefaultColors == null || faderIndex < 0 || faderIndex >= staticFaderDefaultColors.Length)
            {
                return Color.white;
            }

            return staticFaderDefaultColors[faderIndex];
        }

        private int GetStaticFaderRendererStartIndex(int faderIndex)
        {
            int start = 0;
            if (staticFaderRendererCounts == null || faderIndex <= 0)
            {
                return start;
            }

            int limit = Mathf.Min(faderIndex, staticFaderRendererCounts.Length);
            for (int i = 0; i < limit; i++)
            {
                int count = staticFaderRendererCounts[i];
                if (count > 0)
                {
                    start += count;
                }
            }

            return start;
        }

        private int GetStaticFaderRendererCount(int faderIndex)
        {
            if (staticFaderRendererCounts == null || faderIndex < 0 || faderIndex >= staticFaderRendererCounts.Length)
            {
                return 0;
            }

            int count = staticFaderRendererCounts[faderIndex];
            return count < 0 ? 0 : count;
        }

        private string GetStaticFaderLabel(int faderIndex)
        {
            if (staticFaderNames == null || faderIndex < 0 || faderIndex >= staticFaderNames.Length)
            {
                return string.Empty;
            }

            string label = staticFaderNames[faderIndex];
            return string.IsNullOrEmpty(label) ? string.Empty : label;
        }

        private string GetDynamicFaderLabel(int index)
        {
            if (dynamicFaderNames == null || index < 0 || index >= dynamicFaderNames.Length)
            {
                return string.Empty;
            }

            string label = dynamicFaderNames[index];
            return string.IsNullOrEmpty(label) ? string.Empty : label;
        }

        private int GetDynamicFaderEntryCount()
        {
            return dynamicFaderNames != null ? dynamicFaderNames.Length : 0;
        }

        private int GetDynamicFaderFolderIndex(int index)
        {
            if (dynamicFaderFolders == null || index < 0 || index >= dynamicFaderFolders.Length)
            {
                return -1;
            }

            return dynamicFaderFolders[index];
        }

        private int GetDynamicFaderToggleIndex(int index)
        {
            if (dynamicFaderToggles == null || index < 0 || index >= dynamicFaderToggles.Length)
            {
                return -1;
            }

            return dynamicFaderToggles[index];
        }

        private string GetDynamicFaderPropertyName(int index)
        {
            if (dynamicFaderPropertyNames == null || index < 0 || index >= dynamicFaderPropertyNames.Length)
            {
                return string.Empty;
            }

            string name = dynamicFaderPropertyNames[index];
            return name ?? string.Empty;
        }

        private float GetDynamicFaderMinValue(int index)
        {
            if (dynamicFaderMinValues == null || index < 0 || index >= dynamicFaderMinValues.Length)
            {
                return 0f;
            }

            return dynamicFaderMinValues[index];
        }

        private float GetDynamicFaderMaxValue(int index)
        {
            if (dynamicFaderMaxValues == null || index < 0 || index >= dynamicFaderMaxValues.Length)
            {
                return 1f;
            }

            return dynamicFaderMaxValues[index];
        }

        private float GetDynamicFaderDefaultValue(int index)
        {
            if (dynamicFaderDefaultValues == null || index < 0 || index >= dynamicFaderDefaultValues.Length)
            {
                return 0f;
            }

            float min = GetDynamicFaderMinValue(index);
            float max = GetDynamicFaderMaxValue(index);
            float value = dynamicFaderDefaultValues[index];
            return Mathf.Clamp(value, min, max);
        }

        private int GetDynamicFaderPropertyType(int index)
        {
            if (dynamicFaderPropertyTypes == null || index < 0 || index >= dynamicFaderPropertyTypes.Length)
            {
                return 0;
            }

            return dynamicFaderPropertyTypes[index];
        }

        private Color GetDynamicFaderDefaultColor(int index)
        {
            if (dynamicFaderDefaultColors == null || index < 0 || index >= dynamicFaderDefaultColors.Length)
            {
                return Color.white;
            }

            return dynamicFaderDefaultColors[index];
        }

        private int GetDynamicFaderMaterialIndex(int index)
        {
            if (dynamicFaderMaterialIndices == null || index < 0 || index >= dynamicFaderMaterialIndices.Length)
            {
                return 0;
            }

            int matIndex = dynamicFaderMaterialIndices[index];
            return matIndex < 0 ? 0 : matIndex;
        }

        private bool IsStaticFaderIndicatorEnabled(int faderIndex)
        {
            if (staticFaderColorIndicatorsEnabled == null || faderIndex < 0 || faderIndex >= staticFaderColorIndicatorsEnabled.Length)
            {
                return false;
            }

            return staticFaderColorIndicatorsEnabled[faderIndex];
        }

        private bool IsStaticFaderIndicatorConditional(int faderIndex)
        {
            if (staticFaderIndicatorConditional == null || faderIndex < 0 || faderIndex >= staticFaderIndicatorConditional.Length)
            {
                return false;
            }

            return staticFaderIndicatorConditional[faderIndex];
        }

        private Color GetStaticFaderIndicatorColor(int faderIndex)
        {
            if (staticFaderIndicatorColors == null || faderIndex < 0 || faderIndex >= staticFaderIndicatorColors.Length)
            {
                return launchpad != null ? launchpad.activeColor : Color.white;
            }

            return staticFaderIndicatorColors[faderIndex];
        }

        private bool IsDynamicFaderIndicatorEnabled(int index)
        {
            if (dynamicFaderColorIndicatorsEnabled == null || index < 0 || index >= dynamicFaderColorIndicatorsEnabled.Length)
            {
                return false;
            }

            return dynamicFaderColorIndicatorsEnabled[index];
        }

        private bool IsDynamicFaderIndicatorConditional(int index)
        {
            if (dynamicFaderIndicatorConditional == null || index < 0 || index >= dynamicFaderIndicatorConditional.Length)
            {
                return false;
            }

            return dynamicFaderIndicatorConditional[index];
        }

        private Color GetDynamicFaderIndicatorColor(int index)
        {
            if (dynamicFaderIndicatorColors == null || index < 0 || index >= dynamicFaderIndicatorColors.Length)
            {
                return launchpad != null ? launchpad.activeColor : Color.white;
            }

            return dynamicFaderIndicatorColors[index];
        }

        private bool IsDynamicFaderValid(int index)
        {
            int folderIndex = GetDynamicFaderFolderIndex(index);
            int toggleIndex = GetDynamicFaderToggleIndex(index);
            string propertyName = GetDynamicFaderPropertyName(index);
            if (folderIndex < 0 || toggleIndex < 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            if (launchpad == null)
            {
                return false;
            }

            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            return folderType == ToggleFolderType.Properties ||
                   folderType == ToggleFolderType.Materials ||
                   folderType == ToggleFolderType.Objects ||
                   folderType == ToggleFolderType.Shaders ||
                   folderType == ToggleFolderType.Mochie ||
                   folderType == ToggleFolderType.Skybox ||
                   folderType == ToggleFolderType.June;
        }

        private bool IsDynamicFaderConditionActive(int index)
        {
            int folderIndex = GetDynamicFaderFolderIndex(index);
            int toggleIndex = GetDynamicFaderToggleIndex(index);
            Debug.Log($"[FaderSystemHandler] IsDynamicFaderConditionActive: index={index}, folderIndex={folderIndex}, toggleIndex={toggleIndex}");
            if (folderIndex < 0 || toggleIndex < 0 || launchpad == null)
            {
                Debug.Log($"[FaderSystemHandler] IsDynamicFaderConditionActive: returning false (invalid indices or null launchpad)");
                return false;
            }

            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            Debug.Log($"[FaderSystemHandler] IsDynamicFaderConditionActive: folderType={folderType}");
            switch (folderType)
            {
                case ToggleFolderType.Materials:
                case ToggleFolderType.Objects:
                case ToggleFolderType.Shaders:
                    return launchpad.GetToggleStateForFolder(folderIndex, toggleIndex);

                case ToggleFolderType.Properties:
                    return launchpad.GetPropertyStateForFolder(folderIndex, toggleIndex);

                case ToggleFolderType.Mochie:
                {
                    MochieHandler mochiHandler = launchpad.GetMochiHandler();
                    // Use IsMochieDynamicToggleActive which maps toggle indices to specific Mochie effects
                    // This matches the Read Only version's implementation
                    bool isActive = mochiHandler != null && mochiHandler.IsMochieDynamicToggleActive(toggleIndex);
                    Debug.Log($"[FaderSystemHandler] IsDynamicFaderConditionActive: Mochie toggleIndex={toggleIndex}, mochiHandler={(mochiHandler != null ? "NOT NULL" : "NULL")}, isActive={isActive}");
                    return isActive;
                }

                case ToggleFolderType.Skybox:
                {
                    SkyboxHandler skyboxHandler = launchpad.GetSkyboxHandler();
                    return skyboxHandler != null && skyboxHandler.GetActiveSkyboxIndex() == toggleIndex;
                }

                case ToggleFolderType.June:
                {
                    JuneHandler juneHandler = launchpad.GetJuneHandlerForFolder(folderIndex);
                    bool isActive = juneHandler != null && juneHandler.GetEntryState(toggleIndex);
                    Debug.Log($"[FaderSystemHandler] IsDynamicFaderConditionActive: June toggleIndex={toggleIndex}, juneHandler={(juneHandler != null ? "NOT NULL" : "NULL")}, isActive={isActive}");
                    return isActive;
                }

                default:
                    return false;
            }
        }

        private void ApplyFaderIndicator(int index, Color color, float emission)
        {
            if (faderIndicatorAppliedColors == null || index < 0 || index >= faderIndicatorAppliedColors.Length)
            {
                return;
            }

            bool colorChanged = !ColorsApproximately(faderIndicatorAppliedColors[index], color);
            bool emissionChanged = !Mathf.Approximately(faderIndicatorAppliedEmission[index], emission);

            if (!colorChanged && !emissionChanged)
            {
                return;
            }

            faderIndicatorAppliedColors[index] = color;
            faderIndicatorAppliedEmission[index] = emission;

            // Use FaderHandler's method to update indicator
            if (faders == null || index >= faders.Length)
            {
                return;
            }

            FaderHandler fader = faders[index];
            if (fader != null)
            {
                fader.SetIndicatorColor(color, emission);
            }
        }

        private bool ColorsApproximately(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) &&
                   Mathf.Approximately(a.g, b.g) &&
                   Mathf.Approximately(a.b, b.b) &&
                   Mathf.Approximately(a.a, b.a);
        }

        #region Fader Snapshot Support

        /// <summary>
        /// Gets the number of fader slots available.
        /// </summary>
        public int GetFaderCount()
        {
            return faders != null ? Mathf.Min(MaxFaders, faders.Length) : 0;
        }

        /// <summary>
        /// Checks if a fader at the given index is currently assigned (has a property to control).
        /// Static faders with a property and dynamic faders with an active source are considered assigned.
        /// </summary>
        public bool IsFaderAssigned(int faderIndex)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return false;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader == null)
            {
                return false;
            }

            return fader.IsAssigned();
        }

        /// <summary>
        /// Gets the current position of a fader as a discrete step (0 to FaderStepCount-1).
        /// Returns -1 if the fader is not assigned or invalid.
        /// </summary>
        public int GetFaderPositionStep(int faderIndex)
        {
            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return -1;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader == null || !fader.IsAssigned())
            {
                return -1;
            }

            return fader.GetPositionStep();
        }

        /// <summary>
        /// Sets a fader's position from a discrete step (0 to FaderStepCount-1).
        /// Does nothing if the fader is not assigned or invalid, or if step is -1 (unassigned marker).
        /// </summary>
        public void SetFaderPositionFromStep(int faderIndex, int step)
        {
            if (step < 0)
            {
                // -1 means the fader wasn't assigned when the preset was captured
                return;
            }

            if (faders == null || faderIndex < 0 || faderIndex >= faders.Length)
            {
                return;
            }

            FaderHandler fader = faders[faderIndex];
            if (fader == null || !fader.IsAssigned())
            {
                return;
            }

            fader.SetPositionFromStep(step);
            fader.RequestSerialization();
        }

        #endregion
    }
}

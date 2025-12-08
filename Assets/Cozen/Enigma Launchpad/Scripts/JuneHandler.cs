using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    public enum JuneToggleType
    {
        Audiolink,
        Blur,
        Border,
        Chromatic,
        Creativity,
        Grading,
        Distortions,
        Enhance,
        Filters,
        Generation,
        Glitch,
        Others,
        Outlines,
        Overlay,
        Stylize,
        Special,
        Transition,
        Triplanar,
        UV,
        Vertex,
        Zoom,
    }

    public enum JuneAudiolinkControlType
    {
        BandToggle,
        BandSelector,
        PowerToggle,
        PowerSelector,
    }

    public enum JuneAudiolinkBand
    {
        Disabled = 0,
        Bass = 1,
        LowMid = 2,
        HighMid = 3,
        Treble = 4,
    }

    public enum JuneAudiolinkPowerLevel
    {
        Disabled = 0,
        Quarter = 1,
        Half = 2,
        ThreeQuarter = 3,
        Full = 4,
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class JuneHandler : UdonSharpBehaviour
    {
        // AudioLink band colors matching MochieHandler's color scheme
        private readonly Color bassColor = new Color(0.446540833f / 3f, 0.0586366728f / 3f, 0f);
        private readonly Color lowMidColor = new Color(76 / 255f, 49 / 255f, 15 / 255f);
        private readonly Color upperMidColor = new Color(27 / 255f, 58 / 255f, 14 / 255f);
        private readonly Color trebleColor = new Color(0 / 255f, 46 / 255f, 71 / 255f);

        [Header("June Routing")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;

        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;

        [Header("June Configuration")]
        [Tooltip("Renderer controlled by June material toggles.")]
        public Renderer juneRenderer;

        [Tooltip("Material template applied to June folder renderer.")]
        public Material juneMaterial;

        [Header("June Toggles")]
        [Tooltip("Flattened June toggle types (per June folder entry order).")]
        public JuneToggleType[] juneToggleTypes;

        [Tooltip("Flattened June toggle names (per June folder entry order).")]
        public string[] juneToggleNames;

        [Tooltip("Audiolink control behaviour for each June toggle (only used when type is Audiolink).")]
        public JuneAudiolinkControlType[] juneAudiolinkControlTypes;

        [Tooltip("Audiolink band configuration per June toggle (only used when type is Audiolink).")]
        public JuneAudiolinkBand[] juneAudiolinkBands;

        [Tooltip("Audiolink power configuration per June toggle (only used when type is Audiolink).")]
        public JuneAudiolinkPowerLevel[] juneAudiolinkPowers;

        [Tooltip("Flattened array of property names for June toggle property mappings.")]
        public string[] juneTogglePropertyNames;

        [Tooltip("Flattened array of float values for June toggle property mappings.")]
        public float[] juneToggleFloatValues;

        [Tooltip("Flattened array of color values for June toggle property mappings.")]
        public Color[] juneToggleColorValues;

        [Tooltip("Flattened array of texture values for June toggle property mappings.")]
        public Texture[] juneToggleTextureValues;

        [Tooltip("Flattened array of vector values for June toggle property mappings.")]
        public Vector4[] juneToggleVectorValues;

        [Tooltip("Flags indicating whether a texture value is assigned for each June toggle entry.")]
        public bool[] juneToggleHasTextureValues;

        [Tooltip("Flags indicating whether a vector value is assigned for each June toggle entry.")]
        public bool[] juneToggleHasVectorValues;

        [Tooltip("Start indices for each toggle's properties in the flattened June arrays.")]
        public int[] juneToggleStartIndices;

        [Tooltip("Count of properties for each June toggle.")]
        public int[] juneTogglePropertyCounts;

        [UdonSynced] private bool[] juneToggleStates;
        [UdonSynced] private int currentPage;
        [UdonSynced] private int juneAudiolinkBandState = (int)JuneAudiolinkBand.Disabled;
        [UdonSynced] private int juneAudiolinkPowerState = (int)JuneAudiolinkPowerLevel.Disabled;

        private Material _runtimeJuneMaterial;
        private bool initialJuneAudiolinkCaptured;
        private int initialJuneAudiolinkBandState = (int)JuneAudiolinkBand.Disabled;
        private int initialJuneAudiolinkPowerState = (int)JuneAudiolinkPowerLevel.Disabled;
        private bool[] initialToggleStates;
        
        // Track the previous band state before power goes to 0, so we can restore it
        private int _previousAudiolinkBandState = (int)JuneAudiolinkBand.Bass;

        private const int AudiolinkBandCount = 5;
        private const int AudiolinkPowerCount = 5;

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
            Debug.Log($"[JuneHandler] Start() called on {gameObject.name}");
            if (launchpad == null)
            {
                Debug.Log("[JuneHandler] launchpad is null, calling Awake()");
                Awake();
            }
        }

        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[JuneHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }

        public bool IsReady()
        {
            return launchpad != null;
        }

        public void InitializeJuneRuntime()
        {
            Debug.Log($"[JuneHandler] InitializeJuneRuntime called on {gameObject.name}");

            if (launchpad == null)
            {
                Debug.LogWarning("[JuneHandler] InitializeJuneRuntime skipped - launchpad is null");
                return;
            }

            InitializeHandler();
            InitializeJuneMaterial();
            CaptureInitialState();
            Debug.Log("[JuneHandler] InitializeJuneRuntime completed");
        }

        private void InitializeHandler()
        {
            int count = GetToggleCount();
            if (juneToggleStates == null || juneToggleStates.Length != count)
            {
                juneToggleStates = new bool[count];
            }

            ClampJuneAudiolinkStates();
            
            // Initialize previous band state from the default configuration
            JuneAudiolinkBand defaultBand = FindFirstAudiolinkDefaultBand();
            if (defaultBand != JuneAudiolinkBand.Disabled)
            {
                _previousAudiolinkBandState = (int)defaultBand;
            }
        }

        private void InitializeJuneMaterial()
        {
            if (juneRenderer == null || juneMaterial == null)
            {
                return;
            }

            // Create a runtime instance of the June material
            if (_runtimeJuneMaterial == null)
            {
                Material[] materials = juneRenderer.materials;
                if (materials == null || materials.Length < 1)
                {
                    materials = new Material[] { juneMaterial };
                }
                else
                {
                    materials[0] = juneMaterial;
                }
                juneRenderer.materials = materials;
                _runtimeJuneMaterial = juneRenderer.material;
            }

            // Initialize June material to disabled state
            if (_runtimeJuneMaterial != null && _runtimeJuneMaterial.HasProperty("_BlurStyle"))
            {
                _runtimeJuneMaterial.SetFloat("_BlurStyle", 0f);
            }

            ApplyJuneAudiolinkDefaults();
            ApplyJuneAudiolinkState();
            CaptureInitialAudiolinkStateIfNeeded();
        }

        private void CaptureInitialState()
        {
            if (juneToggleStates != null)
            {
                initialToggleStates = new bool[juneToggleStates.Length];
                for (int i = 0; i < juneToggleStates.Length; i++)
                {
                    initialToggleStates[i] = juneToggleStates[i];
                }
            }
        }

        public void RestoreInitialState()
        {
            if (initialToggleStates != null && juneToggleStates != null && initialToggleStates.Length == juneToggleStates.Length)
            {
                for (int i = 0; i < juneToggleStates.Length; i++)
                {
                    juneToggleStates[i] = initialToggleStates[i];
                }
            }

            currentPage = 0;
            RestoreJuneAudiolinkState();
            RestoreLocalState();
        }

        public void OnLaunchpadDeserialized()
        {
            RestoreLocalState();
        }

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            RestoreLocalState();

            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }

        public void RestoreLocalState()
        {
            if (launchpad == null || juneToggleStates == null)
            {
                return;
            }

            // Apply toggle presets for active toggles
            for (int i = 0; i < juneToggleStates.Length; i++)
            {
                if (juneToggleStates[i])
                {
                    ApplyJuneTogglePreset(i);
                }
                else
                {
                    ClearJuneTogglePreset(i);
                }
            }

            ApplyJuneAudiolinkState();
        }

        // Handler Interface Methods (called by EnigmaLaunchpad)

        public int GetToggleCount()
        {
            return juneToggleTypes != null ? juneToggleTypes.Length : 0;
        }

        /// <summary>
        /// Gets the toggle state at a specific local index.
        /// Used by PresetHandler to capture preset snapshots.
        /// </summary>
        public bool GetToggleState(int localIndex)
        {
            if (juneToggleStates == null || localIndex < 0 || localIndex >= juneToggleStates.Length)
            {
                return false;
            }
            return juneToggleStates[localIndex];
        }

        /// <summary>
        /// Sets the toggle state at a specific local index.
        /// Used by PresetHandler to apply preset snapshots.
        /// </summary>
        public void SetToggleState(int localIndex, bool state)
        {
            if (juneToggleStates == null || localIndex < 0 || localIndex >= juneToggleStates.Length)
            {
                return;
            }
            juneToggleStates[localIndex] = state;
        }

        /// <summary>
        /// Gets the current audiolink band state.
        /// Used by PresetHandler to capture preset snapshots.
        /// </summary>
        public int GetAudiolinkBandState()
        {
            return juneAudiolinkBandState;
        }

        /// <summary>
        /// Gets the current audiolink power state.
        /// Used by PresetHandler to capture preset snapshots.
        /// </summary>
        public int GetAudiolinkPowerState()
        {
            return juneAudiolinkPowerState;
        }

        /// <summary>
        /// Applies audiolink band and power state.
        /// Used by PresetHandler to apply preset snapshots.
        /// </summary>
        public bool ApplyAudiolinkState(int band, int power)
        {
            bool changed = false;
            if (juneAudiolinkBandState != band)
            {
                juneAudiolinkBandState = band;
                changed = true;
            }
            if (juneAudiolinkPowerState != power)
            {
                juneAudiolinkPowerState = power;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Applies all toggle states to the June material.
        /// Used by PresetHandler after applying preset snapshots.
        /// </summary>
        public void ApplyToggles()
        {
            ApplyJuneAudiolinkState();
            
            int count = GetToggleCount();
            for (int i = 0; i < count; i++)
            {
                if (juneToggleStates != null && i < juneToggleStates.Length && juneToggleStates[i])
                {
                    ApplyJuneTogglePreset(i);
                }
            }
        }

        public string GetLabel(int buttonIndex)
        {
            if (buttonIndex == 10)
            {
                return launchpad != null ? launchpad.GetFolderLabelForIndex(folderIndex, false) : string.Empty;
            }

            if (!IsHandlerConfigured())
            {
                return buttonIndex == 9 ? "0/0" : string.Empty;
            }

            if (buttonIndex == 9)
            {
                return GetPageLabel();
            }

            return GetButtonLabel(buttonIndex);
        }

        public bool IsInteractable(int buttonIndex)
        {
            bool configured = IsHandlerConfigured();

            if (buttonIndex == 10)
            {
                return configured;
            }

            if (buttonIndex == 9)
            {
                return configured && GetTotalPages() > 1;
            }

            if (!configured)
            {
                return false;
            }

            return !string.IsNullOrEmpty(GetButtonLabel(buttonIndex));
        }

        public bool IsActive(int buttonIndex)
        {
            if (buttonIndex >= 9)
            {
                return IsHandlerConfigured();
            }

            return TryGetToggleState(buttonIndex, out bool state) && state;
        }

        /// <summary>
        /// Gets the color for a button. AudioLink band buttons use special colors matching MochieHandler.
        /// </summary>
        public Color GetColor(int buttonIndex)
        {
            if (launchpad == null)
            {
                return Color.gray;
            }

            if (!IsHandlerConfigured() || buttonIndex < 0)
            {
                return launchpad.GetInactiveColor();
            }

            // Page button and folder button use standard colors
            if (buttonIndex >= 9)
            {
                return launchpad.GetInactiveColor();
            }

            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= GetToggleCount())
            {
                return launchpad.GetInactiveColor();
            }

            // Check if this is an AudioLink toggle
            if (juneToggleTypes == null || localIndex >= juneToggleTypes.Length)
            {
                return IsActive(buttonIndex) ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
            }

            JuneToggleType toggleType = juneToggleTypes[localIndex];
            if (toggleType != JuneToggleType.Audiolink)
            {
                return IsActive(buttonIndex) ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
            }

            // For AudioLink toggles, use special colors based on control type and state
            JuneAudiolinkControlType controlType = (juneAudiolinkControlTypes != null && localIndex < juneAudiolinkControlTypes.Length)
                ? juneAudiolinkControlTypes[localIndex]
                : JuneAudiolinkControlType.BandToggle;

            return GetAudiolinkButtonColor(localIndex, controlType);
        }

        /// <summary>
        /// Gets the color for an AudioLink button based on its control type and current state.
        /// </summary>
        private Color GetAudiolinkButtonColor(int localIndex, JuneAudiolinkControlType controlType)
        {
            switch (controlType)
            {
                case JuneAudiolinkControlType.BandToggle:
                {
                    // Individual band button: use band color when active
                    JuneAudiolinkBand configuredBand = GetAudiolinkConfiguredBand(localIndex);
                    bool isActive = configuredBand != JuneAudiolinkBand.Disabled && juneAudiolinkBandState == (int)configuredBand;
                    if (!isActive)
                    {
                        return launchpad.GetInactiveColor();
                    }
                    return GetBandColor(configuredBand);
                }
                case JuneAudiolinkControlType.BandSelector:
                {
                    // Band selector: use the current band's color when active, inactive when power is 0
                    if (juneAudiolinkPowerState == (int)JuneAudiolinkPowerLevel.Disabled)
                    {
                        return launchpad.GetInactiveColor();
                    }
                    JuneAudiolinkBand currentBand = (JuneAudiolinkBand)ClampAudiolinkBandIndex(juneAudiolinkBandState);
                    if (currentBand == JuneAudiolinkBand.Disabled)
                    {
                        // This shouldn't happen if power is on, but handle it gracefully
                        return launchpad.GetInactiveColor();
                    }
                    return GetBandColor(currentBand);
                }
                case JuneAudiolinkControlType.PowerToggle:
                {
                    // Individual power button: standard active/inactive
                    JuneAudiolinkPowerLevel configuredPower = GetAudiolinkConfiguredPower(localIndex);
                    bool isActive = configuredPower != JuneAudiolinkPowerLevel.Disabled && juneAudiolinkPowerState == (int)configuredPower;
                    return isActive ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                }
                case JuneAudiolinkControlType.PowerSelector:
                {
                    // Power selector: standard active/inactive
                    bool isActive = juneAudiolinkPowerState != (int)JuneAudiolinkPowerLevel.Disabled;
                    return isActive ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                }
                default:
                    return launchpad.GetInactiveColor();
            }
        }

        /// <summary>
        /// Gets the color associated with an AudioLink band, matching MochieHandler's color scheme.
        /// </summary>
        private Color GetBandColor(JuneAudiolinkBand band)
        {
            switch (band)
            {
                case JuneAudiolinkBand.Bass:
                    return bassColor * 3f;
                case JuneAudiolinkBand.LowMid:
                    return lowMidColor * 3f;
                case JuneAudiolinkBand.HighMid:
                    return upperMidColor * 3f;
                case JuneAudiolinkBand.Treble:
                    return trebleColor * 3f;
                default:
                    return launchpad.GetInactiveColor();
            }
        }

        private bool IsHandlerConfigured()
        {
            return launchpad != null &&
                juneToggleTypes != null &&
                juneToggleTypes.Length > 0;
        }

        private string GetButtonLabel(int buttonIndex)
        {
            if (launchpad == null)
            {
                return string.Empty;
            }

            if (!IsHandlerConfigured())
            {
                return string.Empty;
            }

            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= GetToggleCount())
            {
                return string.Empty;
            }

            return GetJuneToggleDisplayName(localIndex);
        }

        private bool TryGetToggleState(int buttonIndex, out bool state)
        {
            state = false;
            if (launchpad == null)
            {
                return false;
            }

            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= GetToggleCount())
            {
                return false;
            }

            if (juneToggleStates == null || localIndex >= juneToggleStates.Length)
            {
                return false;
            }

            state = juneToggleStates[localIndex];
            return true;
        }

        private string GetPageLabel()
        {
            if (!IsHandlerConfigured())
            {
                return "0/0";
            }

            int totalPages = GetTotalPages();
            int displayPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));
            return $"{displayPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        private int GetTotalPages()
        {
            if (launchpad == null)
            {
                return 1;
            }

            int count = GetToggleCount();
            return Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
        }

        public void OnSelect(int buttonIndex)
        {
            if (!ToggleLocalEntry(buttonIndex))
            {
                return;
            }

            RequestSerialization();
        }

        private bool ToggleLocalEntry(int buttonIndex)
        {
            if (launchpad == null)
            {
                return false;
            }

            if (!IsHandlerConfigured())
            {
                return false;
            }

            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= GetToggleCount())
            {
                return false;
            }

            if (juneToggleStates == null || localIndex >= juneToggleStates.Length)
            {
                return false;
            }

            EnsureLocalOwnership();

            JuneToggleType toggleType = juneToggleTypes[localIndex];

            if (toggleType == JuneToggleType.Audiolink)
            {
                return HandleJuneAudiolinkToggle(localIndex);
            }

            bool newState = !juneToggleStates[localIndex];

            // Handle exclusive folder logic
            bool isExclusive = launchpad.IsFolderExclusive(folderIndex);
            if (newState)
            {
                int count = GetToggleCount();
                bool restrictByType = !isExclusive;
                JuneToggleType selectedType = toggleType;

                // Disable conflicting toggles
                for (int i = 0; i < count; i++)
                {
                    if (i == localIndex)
                    {
                        continue;
                    }

                    if (i >= juneToggleStates.Length)
                    {
                        break;
                    }

                    bool shouldDisable = isExclusive;

                    if (!shouldDisable && restrictByType)
                    {
                        if (juneToggleTypes != null && i < juneToggleTypes.Length)
                        {
                            shouldDisable = juneToggleTypes[i] == selectedType;
                        }
                    }

                    if (!shouldDisable)
                    {
                        continue;
                    }

                    if (juneToggleStates[i])
                    {
                        juneToggleStates[i] = false;
                        ClearJuneTogglePreset(i);
                    }
                }

                ApplyJuneTogglePreset(localIndex);
            }
            else
            {
                ClearJuneTogglePreset(localIndex);
            }

            juneToggleStates[localIndex] = newState;
            return true;
        }

        public void OnPageChange(int direction)
        {
            if (launchpad == null)
            {
                return;
            }

            EnsureLocalOwnership();
            UpdatePage(direction);
        }

        private void UpdatePage(int direction)
        {
            int count = GetToggleCount();
            if (count <= 0)
            {
                return;
            }

            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
            currentPage = (currentPage + direction + totalPages) % totalPages;

            RequestSerialization();
        }

        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        // June Toggle Preset Methods

        private void ApplyJuneTogglePreset(int flatIndex)
        {
            if (_runtimeJuneMaterial == null)
            {
                return;
            }

            JuneToggleType toggleType = JuneToggleType.Others;
            if (juneToggleTypes != null && flatIndex >= 0 && flatIndex < juneToggleTypes.Length)
            {
                toggleType = juneToggleTypes[flatIndex];
                if (toggleType == JuneToggleType.Audiolink)
                {
                    return;
                }
            }

            if (juneToggleStartIndices == null || flatIndex < 0 || flatIndex >= juneToggleStartIndices.Length)
            {
                return;
            }

            if (juneTogglePropertyCounts == null || flatIndex >= juneTogglePropertyCounts.Length)
            {
                return;
            }

            int startIdx = juneToggleStartIndices[flatIndex];
            int count = juneTogglePropertyCounts[flatIndex];

            if (count <= 0 || juneTogglePropertyNames == null || juneToggleFloatValues == null)
            {
                return;
            }

            int endIdx = startIdx + count;
            if (endIdx > juneTogglePropertyNames.Length
                || endIdx > juneToggleFloatValues.Length
                || endIdx > juneToggleColorValues.Length
                || endIdx > juneToggleTextureValues.Length
                || endIdx > juneToggleVectorValues.Length
                || endIdx > juneToggleHasTextureValues.Length
                || endIdx > juneToggleHasVectorValues.Length)
            {
                return;
            }

            // Apply each property in the preset
            for (int i = 0; i < count; i++)
            {
                int propIdx = startIdx + i;
                if (propIdx >= juneTogglePropertyNames.Length)
                {
                    break;
                }

                string propName = juneTogglePropertyNames[propIdx];
                if (string.IsNullOrEmpty(propName))
                {
                    continue;
                }

                if (!_runtimeJuneMaterial.HasProperty(propName))
                {
                    continue;
                }

                bool hasTextureValue = juneToggleHasTextureValues != null
                    && propIdx < juneToggleHasTextureValues.Length
                    && juneToggleHasTextureValues[propIdx];
                bool hasVectorValue = juneToggleHasVectorValues != null
                    && propIdx < juneToggleHasVectorValues.Length
                    && juneToggleHasVectorValues[propIdx];

                if (hasTextureValue && juneToggleTextureValues != null && propIdx < juneToggleTextureValues.Length)
                {
                    _runtimeJuneMaterial.SetTexture(propName, juneToggleTextureValues[propIdx]);
                }
                else if (hasVectorValue && juneToggleVectorValues != null && propIdx < juneToggleVectorValues.Length)
                {
                    _runtimeJuneMaterial.SetVector(propName, juneToggleVectorValues[propIdx]);
                }
                else
                {
                    bool isColorProperty = propName.Contains("Color") || propName.Contains("colour");

                    if (isColorProperty && juneToggleColorValues != null && propIdx < juneToggleColorValues.Length)
                    {
                        _runtimeJuneMaterial.SetColor(propName, juneToggleColorValues[propIdx]);
                    }
                    else if (propIdx < juneToggleFloatValues.Length)
                    {
                        _runtimeJuneMaterial.SetFloat(propName, juneToggleFloatValues[propIdx]);
                    }
                }
            }

            // NOTE: We no longer enable/disable module keywords at runtime.
            // Module keywords are set once at build time by the build validator and locked.
            // At runtime, we only set property values to achieve the desired effects.
        }

        private void ClearJuneTogglePreset(int flatIndex)
        {
            if (_runtimeJuneMaterial == null)
            {
                return;
            }

            if (juneToggleTypes == null || flatIndex < 0 || flatIndex >= juneToggleTypes.Length)
            {
                return;
            }

            JuneToggleType toggleType = juneToggleTypes[flatIndex];

            if (toggleType == JuneToggleType.Audiolink)
            {
                return;
            }

            // Reset the individual toggle properties to their default (0) values
            // This disables the specific effect while keeping the module enabled
            ResetToggleProperties(flatIndex);
            
            // NOTE: We no longer disable module keywords at runtime.
            // Module keywords remain enabled as configured at build time.
            // Disabling effects is achieved by setting property values to 0.
        }

        /// <summary>
        /// Resets all properties associated with a toggle to 0.
        /// This ensures the specific effect is disabled even if other effects in the same module remain active.
        /// </summary>
        private void ResetToggleProperties(int flatIndex)
        {
            if (juneToggleStartIndices == null || flatIndex < 0 || flatIndex >= juneToggleStartIndices.Length)
            {
                return;
            }

            if (juneTogglePropertyCounts == null || flatIndex >= juneTogglePropertyCounts.Length)
            {
                return;
            }

            int startIdx = juneToggleStartIndices[flatIndex];
            int count = juneTogglePropertyCounts[flatIndex];

            if (count <= 0 || juneTogglePropertyNames == null)
            {
                return;
            }

            int endIdx = startIdx + count;
            if (endIdx > juneTogglePropertyNames.Length)
            {
                return;
            }

            // Reset each property to 0 (default disabled state)
            for (int i = 0; i < count; i++)
            {
                int propIdx = startIdx + i;
                if (propIdx >= juneTogglePropertyNames.Length)
                {
                    break;
                }

                string propName = juneTogglePropertyNames[propIdx];
                if (string.IsNullOrEmpty(propName))
                {
                    continue;
                }

                if (!_runtimeJuneMaterial.HasProperty(propName))
                {
                    continue;
                }

                // Reset float properties to 0
                _runtimeJuneMaterial.SetFloat(propName, 0f);
            }
        }

        private void SetJuneFloat(string propertyName, float value)
        {
            if (string.IsNullOrEmpty(propertyName) || _runtimeJuneMaterial == null)
            {
                return;
            }

            if (_runtimeJuneMaterial.HasProperty(propertyName))
            {
                _runtimeJuneMaterial.SetFloat(propertyName, value);
            }
        }

        // Audiolink Methods

        private bool HandleJuneAudiolinkToggle(int flatIndex)
        {
            if (juneAudiolinkControlTypes == null || flatIndex < 0 || flatIndex >= juneAudiolinkControlTypes.Length)
            {
                return false;
            }

            JuneAudiolinkControlType controlType = juneAudiolinkControlTypes[flatIndex];
            bool changed = false;

            switch (controlType)
            {
                case JuneAudiolinkControlType.BandToggle:
                    changed = SetAudiolinkBand(GetAudiolinkConfiguredBand(flatIndex));
                    break;
                case JuneAudiolinkControlType.BandSelector:
                    changed = CycleAudiolinkBand();
                    break;
                case JuneAudiolinkControlType.PowerToggle:
                    changed = SetAudiolinkPower(GetAudiolinkConfiguredPower(flatIndex));
                    break;
                case JuneAudiolinkControlType.PowerSelector:
                    changed = CycleAudiolinkPower();
                    break;
            }

            ApplyJuneAudiolinkState();
            return changed;
        }

        private bool SetAudiolinkBand(JuneAudiolinkBand band)
        {
            int clamped = ClampAudiolinkBandIndex((int)band);
            if (juneAudiolinkBandState == clamped)
            {
                return false;
            }

            // If we're setting to a non-disabled band, update the previous state tracker
            if (clamped != (int)JuneAudiolinkBand.Disabled)
            {
                _previousAudiolinkBandState = clamped;
            }

            juneAudiolinkBandState = clamped;
            return true;
        }

        private bool CycleAudiolinkBand()
        {
            int current = ClampAudiolinkBandIndex(juneAudiolinkBandState);
            int next = current;
            for (int i = 0; i < AudiolinkBandCount; i++)
            {
                next = (next + 1) % AudiolinkBandCount;
                if (next != (int)JuneAudiolinkBand.Disabled)
                {
                    break;
                }
            }

            if (next == current)
            {
                return false;
            }

            // Update the previous band tracker if we're setting to a valid band
            if (next != (int)JuneAudiolinkBand.Disabled)
            {
                _previousAudiolinkBandState = next;
            }

            juneAudiolinkBandState = next;
            return true;
        }

        private bool SetAudiolinkPower(JuneAudiolinkPowerLevel powerLevel)
        {
            int clamped = ClampAudiolinkPowerIndex((int)powerLevel);
            if (juneAudiolinkPowerState == clamped)
            {
                return false;
            }

            HandlePowerStateTransition(juneAudiolinkPowerState, clamped);

            juneAudiolinkPowerState = clamped;
            return true;
        }

        private bool CycleAudiolinkPower()
        {
            int current = ClampAudiolinkPowerIndex(juneAudiolinkPowerState);
            int next = (current + 1) % AudiolinkPowerCount;
            if (next == current)
            {
                return false;
            }

            HandlePowerStateTransition(current, next);

            juneAudiolinkPowerState = next;
            return true;
        }

        /// <summary>
        /// Handles the band state transition when power state changes.
        /// Saves band state when transitioning to inactive, restores when transitioning to active.
        /// </summary>
        private void HandlePowerStateTransition(int oldPower, int newPower)
        {
            // Transitioning from active to inactive (0): save the current band state
            if (oldPower != (int)JuneAudiolinkPowerLevel.Disabled && newPower == (int)JuneAudiolinkPowerLevel.Disabled)
            {
                if (juneAudiolinkBandState != (int)JuneAudiolinkBand.Disabled)
                {
                    _previousAudiolinkBandState = juneAudiolinkBandState;
                }
            }
            // Transitioning from inactive (0) to active: restore the previous band state
            else if (oldPower == (int)JuneAudiolinkPowerLevel.Disabled && newPower != (int)JuneAudiolinkPowerLevel.Disabled)
            {
                juneAudiolinkBandState = _previousAudiolinkBandState;
            }
        }

        private JuneAudiolinkBand GetAudiolinkConfiguredBand(int index)
        {
            if (juneAudiolinkBands == null || index < 0 || index >= juneAudiolinkBands.Length)
            {
                return JuneAudiolinkBand.Bass;
            }

            JuneAudiolinkBand configured = juneAudiolinkBands[index];
            return configured == JuneAudiolinkBand.Disabled ? JuneAudiolinkBand.Bass : configured;
        }

        private JuneAudiolinkPowerLevel GetAudiolinkConfiguredPower(int index)
        {
            if (juneAudiolinkPowers == null || index < 0 || index >= juneAudiolinkPowers.Length)
            {
                return JuneAudiolinkPowerLevel.Disabled;
            }

            return juneAudiolinkPowers[index];
        }

        private int ClampAudiolinkBandIndex(int value)
        {
            if (value < 0)
            {
                return (int)JuneAudiolinkBand.Disabled;
            }

            if (value >= AudiolinkBandCount)
            {
                return AudiolinkBandCount - 1;
            }

            return value;
        }

        private int ClampAudiolinkPowerIndex(int value)
        {
            if (value < 0)
            {
                return (int)JuneAudiolinkPowerLevel.Disabled;
            }

            if (value >= AudiolinkPowerCount)
            {
                return AudiolinkPowerCount - 1;
            }

            return value;
        }

        private void ClampJuneAudiolinkStates()
        {
            juneAudiolinkBandState = ClampAudiolinkBandIndex(juneAudiolinkBandState);
            juneAudiolinkPowerState = ClampAudiolinkPowerIndex(juneAudiolinkPowerState);
        }

        private float GetAudiolinkPowerValue(int powerIndex)
        {
            switch (ClampAudiolinkPowerIndex(powerIndex))
            {
                case (int)JuneAudiolinkPowerLevel.Quarter:
                    return 0.25f;
                case (int)JuneAudiolinkPowerLevel.Half:
                    return 0.5f;
                case (int)JuneAudiolinkPowerLevel.ThreeQuarter:
                    return 0.75f;
                case (int)JuneAudiolinkPowerLevel.Full:
                    return 1f;
                default:
                    return 0f;
            }
        }

        private void ApplyAudiolinkBand()
        {
            if (_runtimeJuneMaterial == null)
            {
                return;
            }

            float bandValue = (float)ClampAudiolinkBandIndex(juneAudiolinkBandState);
            SetJuneFloat("_AudioLinkBand", bandValue);
        }

        private void ApplyAudiolinkPower()
        {
            if (_runtimeJuneMaterial == null)
            {
                return;
            }

            int clamped = ClampAudiolinkPowerIndex(juneAudiolinkPowerState);
            float powerValue = GetAudiolinkPowerValue(clamped);
            SetJuneFloat("_AudioLinkPower", powerValue);

            float enabledValue = Mathf.Approximately(powerValue, 0f) ? 0f : 1f;
            SetJuneFloat("_AudioLinkUseGlobal", enabledValue);
            SetJuneFloat("_AudioLinkUseEffects", enabledValue);
            SetJuneFloat("_AudioLinkUseColor", enabledValue);
            SetJuneFloat("_AudioLinkUseUV", enabledValue);

            if (Mathf.Approximately(powerValue, 0f))
            {
                juneAudiolinkBandState = (int)JuneAudiolinkBand.Disabled;
                SetJuneFloat("_AudioLinkBand", 0f);
            }
        }

        private void ApplyJuneAudiolinkState()
        {
            ClampJuneAudiolinkStates();
            ApplyAudiolinkBand();
            ApplyAudiolinkPower();
            RefreshJuneAudiolinkToggleStates();
        }

        private void RefreshJuneAudiolinkToggleStates()
        {
            if (juneToggleStates == null || juneToggleTypes == null)
            {
                return;
            }

            int limit = Mathf.Min(juneToggleStates.Length, juneToggleTypes.Length);
            for (int i = 0; i < limit; i++)
            {
                if (juneToggleTypes[i] != JuneToggleType.Audiolink)
                {
                    continue;
                }

                JuneAudiolinkControlType controlType = (juneAudiolinkControlTypes != null && i < juneAudiolinkControlTypes.Length)
                    ? juneAudiolinkControlTypes[i]
                    : JuneAudiolinkControlType.BandToggle;

                switch (controlType)
                {
                    case JuneAudiolinkControlType.BandToggle:
                        JuneAudiolinkBand configuredBand = GetAudiolinkConfiguredBand(i);
                        juneToggleStates[i] = configuredBand != JuneAudiolinkBand.Disabled && juneAudiolinkBandState == (int)configuredBand;
                        break;
                    case JuneAudiolinkControlType.BandSelector:
                        juneToggleStates[i] = juneAudiolinkBandState != (int)JuneAudiolinkBand.Disabled;
                        break;
                    case JuneAudiolinkControlType.PowerToggle:
                        JuneAudiolinkPowerLevel configuredPower = GetAudiolinkConfiguredPower(i);
                        juneToggleStates[i] = configuredPower != JuneAudiolinkPowerLevel.Disabled && juneAudiolinkPowerState == (int)configuredPower;
                        break;
                    case JuneAudiolinkControlType.PowerSelector:
                        juneToggleStates[i] = juneAudiolinkPowerState != (int)JuneAudiolinkPowerLevel.Disabled;
                        break;
                }
            }
        }

        private void ApplyJuneAudiolinkDefaults()
        {
            if (!Networking.IsOwner(gameObject))
            {
                ClampJuneAudiolinkStates();
                return;
            }

            ClampJuneAudiolinkStates();

            if (juneAudiolinkBandState == (int)JuneAudiolinkBand.Disabled)
            {
                JuneAudiolinkBand defaultBand = FindFirstAudiolinkDefaultBand();
                if (defaultBand != JuneAudiolinkBand.Disabled)
                {
                    juneAudiolinkBandState = (int)defaultBand;
                }
            }

            if (juneAudiolinkPowerState == (int)JuneAudiolinkPowerLevel.Disabled)
            {
                JuneAudiolinkPowerLevel defaultPower = FindFirstAudiolinkDefaultPower();
                if (defaultPower != JuneAudiolinkPowerLevel.Disabled)
                {
                    juneAudiolinkPowerState = (int)defaultPower;
                }
            }
        }

        private JuneAudiolinkBand FindFirstAudiolinkDefaultBand()
        {
            if (juneToggleTypes == null || juneAudiolinkControlTypes == null || juneAudiolinkBands == null)
            {
                return JuneAudiolinkBand.Disabled;
            }

            int limit = Mathf.Min(juneToggleTypes.Length, juneAudiolinkControlTypes.Length, juneAudiolinkBands.Length);
            for (int i = 0; i < limit; i++)
            {
                if (juneToggleTypes[i] != JuneToggleType.Audiolink)
                {
                    continue;
                }

                if (juneAudiolinkControlTypes[i] == JuneAudiolinkControlType.BandSelector)
                {
                    return juneAudiolinkBands[i];
                }
            }

            return JuneAudiolinkBand.Disabled;
        }

        private JuneAudiolinkPowerLevel FindFirstAudiolinkDefaultPower()
        {
            if (juneToggleTypes == null || juneAudiolinkControlTypes == null || juneAudiolinkPowers == null)
            {
                return JuneAudiolinkPowerLevel.Disabled;
            }

            int limit = Mathf.Min(juneToggleTypes.Length, juneAudiolinkControlTypes.Length, juneAudiolinkPowers.Length);
            for (int i = 0; i < limit; i++)
            {
                if (juneToggleTypes[i] != JuneToggleType.Audiolink)
                {
                    continue;
                }

                if (juneAudiolinkControlTypes[i] == JuneAudiolinkControlType.PowerSelector)
                {
                    return juneAudiolinkPowers[i];
                }
            }

            return JuneAudiolinkPowerLevel.Disabled;
        }

        private void CaptureInitialAudiolinkStateIfNeeded()
        {
            if (initialJuneAudiolinkCaptured)
            {
                return;
            }

            initialJuneAudiolinkCaptured = true;
            initialJuneAudiolinkBandState = juneAudiolinkBandState;
            initialJuneAudiolinkPowerState = juneAudiolinkPowerState;
        }

        private void RestoreJuneAudiolinkState()
        {
            juneAudiolinkBandState = initialJuneAudiolinkBandState;
            juneAudiolinkPowerState = initialJuneAudiolinkPowerState;
            ApplyJuneAudiolinkState();
        }

        // Display Name Methods

        public string GetJuneToggleDisplayName(int flatIndex)
        {
            if (flatIndex < 0)
            {
                return string.Empty;
            }

            JuneToggleType toggleType = (juneToggleTypes != null && flatIndex < juneToggleTypes.Length)
                ? juneToggleTypes[flatIndex]
                : JuneToggleType.Blur;

            if (toggleType == JuneToggleType.Audiolink)
            {
                return GetAudiolinkDisplayName(flatIndex);
            }

            string customName = (juneToggleNames != null && flatIndex < juneToggleNames.Length)
                ? juneToggleNames[flatIndex]
                : null;

            if (!string.IsNullOrEmpty(customName))
            {
                return customName;
            }

            return GetJuneToggleTypeLabel(toggleType);
        }

        private string GetAudiolinkDisplayName(int flatIndex)
        {
            JuneAudiolinkControlType controlType = (juneAudiolinkControlTypes != null && flatIndex >= 0 && flatIndex < juneAudiolinkControlTypes.Length)
                ? juneAudiolinkControlTypes[flatIndex]
                : JuneAudiolinkControlType.BandToggle;

            switch (controlType)
            {
                case JuneAudiolinkControlType.BandToggle:
                    return GetAudiolinkBandLabel(GetAudiolinkConfiguredBand(flatIndex));
                case JuneAudiolinkControlType.BandSelector:
                {
                    JuneAudiolinkBand currentBand = (JuneAudiolinkBand)ClampAudiolinkBandIndex(juneAudiolinkBandState);
                    if (currentBand == JuneAudiolinkBand.Disabled)
                    {
                        currentBand = JuneAudiolinkBand.Bass;
                    }

                    return GetAudiolinkBandLabel(currentBand);
                }
                case JuneAudiolinkControlType.PowerToggle:
                    return FormatAudiolinkPowerButtonLabel(GetAudiolinkConfiguredPower(flatIndex));
                case JuneAudiolinkControlType.PowerSelector:
                {
                    JuneAudiolinkPowerLevel currentPower = (JuneAudiolinkPowerLevel)ClampAudiolinkPowerIndex(juneAudiolinkPowerState);
                    return FormatAudiolinkPowerButtonLabel(currentPower);
                }
                default:
                    return "Audiolink";
            }
        }

        public static string GetJuneToggleTypeLabel(JuneToggleType type)
        {
            switch (type)
            {
                case JuneToggleType.UV:
                    return "UV";
                default:
                    return type.ToString();
            }
        }

        public static string GetAudiolinkBandLabel(JuneAudiolinkBand band)
        {
            switch (band)
            {
                case JuneAudiolinkBand.LowMid:
                    return "Low Mid";
                case JuneAudiolinkBand.HighMid:
                    return "High Mid";
                case JuneAudiolinkBand.Treble:
                    return "Treble";
                default:
                    return "Bass";
            }
        }

        public static string GetAudiolinkPowerLabel(JuneAudiolinkPowerLevel power)
        {
            switch (power)
            {
                case JuneAudiolinkPowerLevel.Quarter:
                    return ".25";
                case JuneAudiolinkPowerLevel.Half:
                    return ".5";
                case JuneAudiolinkPowerLevel.ThreeQuarter:
                    return ".75";
                case JuneAudiolinkPowerLevel.Full:
                    return "1";
                default:
                    return "0";
            }
        }

        public static string FormatAudiolinkPowerButtonLabel(JuneAudiolinkPowerLevel power)
        {
            return "AL " + GetAudiolinkPowerLabel(power);
        }

        // Static helper methods for UdonSharp compatibility
        public static int GetFolderIndex(JuneHandler handler)
        {
            return handler != null ? handler.folderIndex : -1;
        }

        /// <summary>
        /// Check if the given folder represents a June folder for this handler.
        /// </summary>
        public bool FolderRepresentsJune(int folderIdx)
        {
            if (launchpad == null)
            {
                return false;
            }

            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIdx);
            return folderType == ToggleFolderType.June;
        }

        /// <summary>
        /// Gets the activation state of a specific entry by local index.
        /// Used by FaderSystemHandler to check dynamic fader conditions.
        /// </summary>
        public bool GetEntryState(int localIndex)
        {
            if (juneToggleStates == null || localIndex < 0 || localIndex >= juneToggleStates.Length)
            {
                return false;
            }
            return juneToggleStates[localIndex];
        }
    }
}

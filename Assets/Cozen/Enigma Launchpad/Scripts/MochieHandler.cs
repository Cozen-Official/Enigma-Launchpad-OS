using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace Cozen
{
    public enum MochieFeatureKeyword
    {
        Shake,
        Distortion,
        BlurPixel,
        Noise,
        Color,
        SobelFilter,
        Outline,
        Fog,
        Triplanar,
        ImageOverlay,
        AudioLink,
    }
    
    // Use Manual sync mode because MochieHandler has many synced variables (33+)
    // and state should only sync when explicitly changed via RequestSerialization()
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MochieHandler : UdonSharpBehaviour
    {
        public const string MochieScreenFxMissingMessage = "Mochie ScreenFX not found, import them to enable these features.";
        public const string MochieScreenFxUpgradeMessage = "Upgrade to SFX X to unlock greyed out features.";
        
        private readonly Color positiveColor = Color.HSVToRGB(0.3f, 0.8f, 1f);
        private readonly Color negativeColor = Color.HSVToRGB(0f, 0.8f, 1f);
        private readonly Color bassColor = new Color(0.446540833f / 3f, 0.0586366728f / 3f, 0f);
        private readonly Color lowMidColor = new Color(76 / 255f, 49 / 255f, 15 / 255f);
        private readonly Color upperMidColor = new Color(27 / 255f, 58 / 255f, 14 / 255f);
        private readonly Color trebleColor = new Color(0 / 255f, 46 / 255f, 71 / 255f);
        
        [Header("Mochie")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;
        
        [Tooltip("Renderer used for applying Mochie shader effects.")]
        public Renderer shaderRenderer;
        
        [Header("Mochie Layout")]
        [Tooltip("Use SFX X layout (more features). Set by editor based on assigned materials. Do not change at runtime.")]
        [SerializeField] public bool useSfxXLayout = false;
        
        public const float MochieAuraStrengthMin = 0f;
        public const float MochieAuraStrengthMax = 1f;
        public const float MochieOutlineThresholdMin = 0f;
        public const float MochieOutlineThresholdMax = 1000f;
        public const float MochieSobelOpacityMin = 0f;
        public const float MochieSobelOpacityMax = 1f;
        public const float MochieEffectStrengthMin = 0f;
        public const float MochieEffectStrengthMax = 1f;
        private const float AdjustmentActivationThreshold = 0.0001f;
        
        public const float MochieOutlineAuraLowDefault = 0.08f;
        public const float MochieOutlineThresholdLowDefault = 2f;
        public const float MochieOutlineSobelLowDefault = 0.25f;
        
        public const float MochieOutlineAuraNormalDefault = 0.15f;
        public const float MochieOutlineThresholdNormalDefault = 20f;
        public const float MochieOutlineSobelNormalDefault = 0.5f;
        
        public const float MochieOutlineAuraHighDefault = 0.3f;
        public const float MochieOutlineThresholdHighDefault = 150f;
        public const float MochieOutlineSobelHighDefault = 0.8f;
        
        public const float MochieInvertStrengthDefault = 0.01f;
        public const float MochieInvertPlusStrengthDefault = 0.02f;
        public const float MochieShakeAmplitudeDefault = 0.018f;
        public const float MochieBlurStrengthDefault = 0.3f;
        public const float MochieDistortionStrengthDefault = 0.25f;
        public const float MochieNoiseStrengthDefault = 0.08f;
        public const float MochieScanLineStrengthDefault = 0.05f;
        public const float MochieDepthBufferOpacityDefault = 0.8f;
        public const float MochieNormalMapOpacityDefault = 0.1f;
        
        [SerializeField] public float mochieOutlineAuraLow = MochieOutlineAuraLowDefault;
        [SerializeField] public float mochieOutlineThresholdLow = MochieOutlineThresholdLowDefault;
        [SerializeField] public float mochieOutlineSobelLow = MochieOutlineSobelLowDefault;
        
        [SerializeField] public float mochieOutlineAuraNormal = MochieOutlineAuraNormalDefault;
        [SerializeField] public float mochieOutlineThresholdNormal = MochieOutlineThresholdNormalDefault;
        [SerializeField] public float mochieOutlineSobelNormal = MochieOutlineSobelNormalDefault;
        
        [SerializeField] public float mochieOutlineAuraHigh = MochieOutlineAuraHighDefault;
        [SerializeField] public float mochieOutlineThresholdHigh = MochieOutlineThresholdHighDefault;
        [SerializeField] public float mochieOutlineSobelHigh = MochieOutlineSobelHighDefault;
        
        [SerializeField] public float mochieInvertStrength = MochieInvertStrengthDefault;
        [SerializeField] public float mochieInvertPlusStrength = MochieInvertPlusStrengthDefault;
        [SerializeField] public float mochieShakeAmplitude = MochieShakeAmplitudeDefault;
        [SerializeField] public float mochieBlurStrength = MochieBlurStrengthDefault;
        [SerializeField] public float mochieDistortionStrength = MochieDistortionStrengthDefault;
        [SerializeField] public float mochieNoiseStrength = MochieNoiseStrengthDefault;
        [SerializeField] public float mochieScanLineStrength = MochieScanLineStrengthDefault;
        [SerializeField] public float mochieDepthBufferOpacity = MochieDepthBufferOpacityDefault;
        [SerializeField] public float mochieNormalMapOpacity = MochieNormalMapOpacityDefault;
        
        public Color[] outlineColors =
        {
            Color.white
        };
        public string[] outlineColorNames = Array.Empty<string>();
        public Texture2D[] overlayTextures;
        public string[] overlayNames;
        
        // Page indices for Mochie UI layout
        private const int AudioLinkSelectionPage = 4;
        
        // Button layout constants for overlay/scan buttons on AudioLinkSelectionPage
        private const int OverlayButtonCount = 3;
        private const int ScanButtonStartIndex = 3;
        private const int TotalOverlayScanButtons = 6;
        
        public const int AudioLinkOutlineIndex = 0;
        public const int AudioLinkFilterIndex = 1;
        public const int AudioLinkShakeIndex = 2;
        public const int AudioLinkBlurIndex = 3;
        public const int AudioLinkDistortIndex = 4;
        public const int AudioLinkNoiseIndex = 5;
        public const int AudioLinkFogIndex = 6;
        public const int AudioLinkImageIndex = 7;
        public const int AudioLinkMiscIndex = 8;
        
        public const int StandardAudioLinkStrengthStartIndex = 3;
        
        public readonly float[] AudioLinkStrengthSteps =
        {
            0f,
            0.25f,
            0.5f,
            0.75f,
            1f
        };
        
        public bool hasCachedMochieShakeAmplitude;
        public float cachedMochieShakeAmplitude;
        
        // IMPORTANT: Consolidated synced variables into arrays to reduce synced variable count.
        // VRChat/UdonSharp has issues syncing behaviors with many individual synced variables.
        // ObjectHandler works with 2 synced vars (bool[] + int), SkyboxHandler with 3 synced vars.
        // Previously MochieHandler had 36 individual synced vars which caused sync failures.
        // Now consolidated to 3 synced vars: mochiePage, syncedInts[], syncedFloats[]
        
        [UdonSynced] private int mochiePage = 0;
        
        // Consolidated int values: [selectedColorIndex, outlineType, outlineStrengthLevel, _scanIndex, _audioLinkBand, _overlayIndex, boolFlags, appliedColorIndex]
        // boolFlags: reserved for future use
        private const int SYNC_INT_COLOR_INDEX = 0;  // "Change to" preview color
        private const int SYNC_INT_OUTLINE_TYPE = 1;
        private const int SYNC_INT_OUTLINE_STRENGTH_LEVEL = 2;
        private const int SYNC_INT_SCAN_INDEX = 3;
        private const int SYNC_INT_AUDIOLINK_BAND = 4;
        private const int SYNC_INT_OVERLAY_INDEX = 5;
        private const int SYNC_INT_BOOL_FLAGS = 6;
        private const int SYNC_INT_APPLIED_COLOR_INDEX = 7;  // Currently applied color
        private const int SYNC_INT_COUNT = 8;
        [UdonSynced] private int[] syncedInts = new int[SYNC_INT_COUNT];
        
        // Consolidated float values in order
        private const int SYNC_FLOAT_AL_FILTER = 0;
        private const int SYNC_FLOAT_AL_SHAKE = 1;
        private const int SYNC_FLOAT_AL_BLUR = 2;
        private const int SYNC_FLOAT_AL_DISTORT = 3;
        private const int SYNC_FLOAT_AL_NOISE = 4;
        private const int SYNC_FLOAT_AL_FOG = 5;
        private const int SYNC_FLOAT_AL_OUTLINE = 6;
        private const int SYNC_FLOAT_AL_IMAGE = 7;
        private const int SYNC_FLOAT_AL_MISC = 8;
        private const int SYNC_FLOAT_SOBEL_OPACITY = 9;
        private const int SYNC_FLOAT_CURRENT_SOBEL = 10;
        private const int SYNC_FLOAT_INVERT = 11;
        private const int SYNC_FLOAT_AMPLITUDE = 12;
        private const int SYNC_FLOAT_BLUR = 13;
        private const int SYNC_FLOAT_DISTORTION = 14;
        private const int SYNC_FLOAT_NOISE = 15;
        private const int SYNC_FLOAT_SCANLINE = 16;
        private const int SYNC_FLOAT_DEPTH_BUFFER = 17;
        private const int SYNC_FLOAT_NORMAL_MAP = 18;
        private const int SYNC_FLOAT_SATURATION = 19;
        private const int SYNC_FLOAT_ROUNDING = 20;
        private const int SYNC_FLOAT_FOG_SAFE = 21;
        private const int SYNC_FLOAT_BRIGHTNESS = 22;
        private const int SYNC_FLOAT_HDR = 23;
        private const int SYNC_FLOAT_CONTRAST = 24;
        private const int SYNC_FLOAT_COUNT = 25;
        [UdonSynced] private float[] syncedFloats = new float[SYNC_FLOAT_COUNT];

        private int _lastSyncedPage = -1;
        private int _lastSerializedPage = -1;
        
        // Local accessors for synced values (backed by arrays)
        // boolFlags reserved for future use
        
        private float _alFilterStrength { get => syncedFloats[SYNC_FLOAT_AL_FILTER]; set => syncedFloats[SYNC_FLOAT_AL_FILTER] = value; }
        private float _alShakeStrength { get => syncedFloats[SYNC_FLOAT_AL_SHAKE]; set => syncedFloats[SYNC_FLOAT_AL_SHAKE] = value; }
        private float _alBlurStrength { get => syncedFloats[SYNC_FLOAT_AL_BLUR]; set => syncedFloats[SYNC_FLOAT_AL_BLUR] = value; }
        private float _alDistortStrength { get => syncedFloats[SYNC_FLOAT_AL_DISTORT]; set => syncedFloats[SYNC_FLOAT_AL_DISTORT] = value; }
        private float _alNoiseStrength { get => syncedFloats[SYNC_FLOAT_AL_NOISE]; set => syncedFloats[SYNC_FLOAT_AL_NOISE] = value; }
        private float _alFogStrength { get => syncedFloats[SYNC_FLOAT_AL_FOG]; set => syncedFloats[SYNC_FLOAT_AL_FOG] = value; }
        private float _alOutlineStrength { get => syncedFloats[SYNC_FLOAT_AL_OUTLINE]; set => syncedFloats[SYNC_FLOAT_AL_OUTLINE] = value; }
        private float _alImageStrength { get => syncedFloats[SYNC_FLOAT_AL_IMAGE]; set => syncedFloats[SYNC_FLOAT_AL_IMAGE] = value; }
        private float _alMiscStrength { get => syncedFloats[SYNC_FLOAT_AL_MISC]; set => syncedFloats[SYNC_FLOAT_AL_MISC] = value; }
        
        public Material activeMochieMaterial;
        public bool materialInitialized;
        
        // Used for cross-component communication via SendCustomEvent
        [System.NonSerialized] public bool lastInitializationResult = false;
        
        private int selectedColorIndex { get => syncedInts[SYNC_INT_COLOR_INDEX]; set => syncedInts[SYNC_INT_COLOR_INDEX] = value; }
        private int appliedColorIndex { get => syncedInts[SYNC_INT_APPLIED_COLOR_INDEX]; set => syncedInts[SYNC_INT_APPLIED_COLOR_INDEX] = value; }
        private Color currentOutlineColor = Color.white;
        
        private int outlineType { get => syncedInts[SYNC_INT_OUTLINE_TYPE]; set => syncedInts[SYNC_INT_OUTLINE_TYPE] = value; }
        private float sobelFilterOpacity { get => syncedFloats[SYNC_FLOAT_SOBEL_OPACITY]; set => syncedFloats[SYNC_FLOAT_SOBEL_OPACITY] = value; }
        private int outlineStrengthLevel { get => syncedInts[SYNC_INT_OUTLINE_STRENGTH_LEVEL]; set => syncedInts[SYNC_INT_OUTLINE_STRENGTH_LEVEL] = value; }
        private float currentSobelOpacity { get => syncedFloats[SYNC_FLOAT_CURRENT_SOBEL]; set => syncedFloats[SYNC_FLOAT_CURRENT_SOBEL] = value; }
        private float invertStrength { get => syncedFloats[SYNC_FLOAT_INVERT]; set => syncedFloats[SYNC_FLOAT_INVERT] = value; }
        private float _amplitude { get => syncedFloats[SYNC_FLOAT_AMPLITUDE]; set => syncedFloats[SYNC_FLOAT_AMPLITUDE] = value; }
        private float blurStrength { get => syncedFloats[SYNC_FLOAT_BLUR]; set => syncedFloats[SYNC_FLOAT_BLUR] = value; }
        private float distortionStrength { get => syncedFloats[SYNC_FLOAT_DISTORTION]; set => syncedFloats[SYNC_FLOAT_DISTORTION] = value; }
        private float noiseStrength { get => syncedFloats[SYNC_FLOAT_NOISE]; set => syncedFloats[SYNC_FLOAT_NOISE] = value; }
        private float scanLineStrength { get => syncedFloats[SYNC_FLOAT_SCANLINE]; set => syncedFloats[SYNC_FLOAT_SCANLINE] = value; }
        private float depthBufferOpacity { get => syncedFloats[SYNC_FLOAT_DEPTH_BUFFER]; set => syncedFloats[SYNC_FLOAT_DEPTH_BUFFER] = value; }
        private float normalMapOpacity { get => syncedFloats[SYNC_FLOAT_NORMAL_MAP]; set => syncedFloats[SYNC_FLOAT_NORMAL_MAP] = value; }
        private float saturation { get => syncedFloats[SYNC_FLOAT_SATURATION]; set => syncedFloats[SYNC_FLOAT_SATURATION] = value; }
        private float roundingOpacity { get => syncedFloats[SYNC_FLOAT_ROUNDING]; set => syncedFloats[SYNC_FLOAT_ROUNDING] = value; }
        private float fogSafeOpacity { get => syncedFloats[SYNC_FLOAT_FOG_SAFE]; set => syncedFloats[SYNC_FLOAT_FOG_SAFE] = value; }
        private float _Brightness { get => syncedFloats[SYNC_FLOAT_BRIGHTNESS]; set => syncedFloats[SYNC_FLOAT_BRIGHTNESS] = value; }
        private float _HDR { get => syncedFloats[SYNC_FLOAT_HDR]; set => syncedFloats[SYNC_FLOAT_HDR] = value; }
        private float _Contrast { get => syncedFloats[SYNC_FLOAT_CONTRAST]; set => syncedFloats[SYNC_FLOAT_CONTRAST] = value; }
        private int _scanIndex { get => syncedInts[SYNC_INT_SCAN_INDEX]; set => syncedInts[SYNC_INT_SCAN_INDEX] = value; }
        private int _audioLinkBand { get => syncedInts[SYNC_INT_AUDIOLINK_BAND]; set => syncedInts[SYNC_INT_AUDIOLINK_BAND] = value; }
        private int _overlayIndex { get => syncedInts[SYNC_INT_OVERLAY_INDEX]; set => syncedInts[SYNC_INT_OVERLAY_INDEX] = value; }
        
        public readonly int[] FullAudioLinkStrengthMap = new int[] {
            AudioLinkOutlineIndex,
            AudioLinkFilterIndex,
            AudioLinkShakeIndex,
            AudioLinkBlurIndex,
            AudioLinkDistortIndex,
            AudioLinkNoiseIndex,
            AudioLinkFogIndex,
            AudioLinkImageIndex,
            AudioLinkMiscIndex
        };
        
        public readonly int[] StandardAudioLinkStrengthMap = new int[] {
            AudioLinkFilterIndex,
            AudioLinkShakeIndex,
            AudioLinkBlurIndex,
            AudioLinkDistortIndex,
            AudioLinkNoiseIndex
        };
        
        public bool hasSfxStandardFeatures;
        public bool hasSfxXFeatures;
        public bool supportsOverlaySelection;
        public bool supportsScanSelection;
        public bool supportsAudioLinkFilter;
        public bool supportsAudioLinkShake;
        public bool supportsAudioLinkBlur;
        public bool supportsAudioLinkDistort;
        public bool supportsAudioLinkNoise;
        public bool supportsAudioLinkFog;
        public bool supportsAudioLinkOutline;
        public bool supportsAudioLinkImage;
        public bool supportsAudioLinkTriplanar;
        public bool supportsAudioLinkMisc;
        
        
        /// <summary>
        /// Initializes the synced arrays with default values.
        /// Called on first access or when arrays are null.
        /// </summary>
        private void EnsureSyncedArraysInitialized()
        {
            if (syncedInts == null || syncedInts.Length != SYNC_INT_COUNT)
            {
                syncedInts = new int[SYNC_INT_COUNT];
                // Set default values
                syncedInts[SYNC_INT_COLOR_INDEX] = 0;
                syncedInts[SYNC_INT_OUTLINE_TYPE] = 0;
                syncedInts[SYNC_INT_OUTLINE_STRENGTH_LEVEL] = 1; // Normal
                syncedInts[SYNC_INT_SCAN_INDEX] = -1;
                syncedInts[SYNC_INT_AUDIOLINK_BAND] = 0;
                syncedInts[SYNC_INT_OVERLAY_INDEX] = -1;
                syncedInts[SYNC_INT_BOOL_FLAGS] = 0;
                syncedInts[SYNC_INT_APPLIED_COLOR_INDEX] = 0;
            }
            
            if (syncedFloats == null || syncedFloats.Length != SYNC_FLOAT_COUNT)
            {
                syncedFloats = new float[SYNC_FLOAT_COUNT];
                // Set default values
                syncedFloats[SYNC_FLOAT_AL_FILTER] = 1f;
                syncedFloats[SYNC_FLOAT_AL_SHAKE] = 1f;
                syncedFloats[SYNC_FLOAT_AL_BLUR] = 1f;
                syncedFloats[SYNC_FLOAT_AL_DISTORT] = 1f;
                syncedFloats[SYNC_FLOAT_AL_NOISE] = 1f;
                syncedFloats[SYNC_FLOAT_AL_FOG] = 0f;
                syncedFloats[SYNC_FLOAT_AL_OUTLINE] = 1f;
                syncedFloats[SYNC_FLOAT_AL_IMAGE] = 1f;
                syncedFloats[SYNC_FLOAT_AL_MISC] = 1f;
                syncedFloats[SYNC_FLOAT_SOBEL_OPACITY] = 0f;
                syncedFloats[SYNC_FLOAT_CURRENT_SOBEL] = 0.5f;
                syncedFloats[SYNC_FLOAT_INVERT] = 0f;
                syncedFloats[SYNC_FLOAT_AMPLITUDE] = 0f;
                syncedFloats[SYNC_FLOAT_BLUR] = 0f;
                syncedFloats[SYNC_FLOAT_DISTORTION] = 0f;
                syncedFloats[SYNC_FLOAT_NOISE] = 0f;
                syncedFloats[SYNC_FLOAT_SCANLINE] = 0f;
                syncedFloats[SYNC_FLOAT_DEPTH_BUFFER] = 0f;
                syncedFloats[SYNC_FLOAT_NORMAL_MAP] = 0f;
                syncedFloats[SYNC_FLOAT_SATURATION] = 1f;
                syncedFloats[SYNC_FLOAT_ROUNDING] = 0f;
                syncedFloats[SYNC_FLOAT_FOG_SAFE] = 0f;
                syncedFloats[SYNC_FLOAT_BRIGHTNESS] = 1f;
                syncedFloats[SYNC_FLOAT_HDR] = 0f;
                syncedFloats[SYNC_FLOAT_CONTRAST] = 1f;
            }
        }
        
        public void Awake()
        {
            Debug.Log("[MochieHandler] Awake() called");
            EnsureSyncedArraysInitialized();
            
            if (launchpad == null)
            {
                Debug.Log("[MochieHandler] Awake: launchpad is null, trying GetComponent");
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    Debug.Log("[MochieHandler] Awake: GetComponent failed, trying GetComponentInParent");
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
            }
            
            if (launchpad != null)
            {
                Debug.Log("[MochieHandler] Awake: launchpad reference established");
            }
            else
            {
                Debug.LogWarning("[MochieHandler] Awake: Failed to find launchpad reference");
            }
        }
        
        public void Start()
        {
            int instanceId = gameObject.GetInstanceID();
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            bool isOwner = Networking.IsOwner(gameObject);
            Debug.Log($"[MochieHandler] Start() called - Player: {localPlayerName}, IsOwner: {isOwner}, InstanceID: {instanceId}, InitialPage: {mochiePage}");
            if (launchpad == null)
            {
                Debug.Log("[MochieHandler] Start: launchpad is null, calling Awake()");
                Awake();
            }
            else
            {
                Debug.Log("[MochieHandler] Start: launchpad already set");
            }
        }

        public void RestoreInitialState()
        {
            EnsureSyncedArraysInitialized();

            mochiePage = 0;
            _lastSyncedPage = -1;
            _lastSerializedPage = -1;

            syncedInts[SYNC_INT_COLOR_INDEX] = 0;
            syncedInts[SYNC_INT_OUTLINE_TYPE] = 0;
            syncedInts[SYNC_INT_OUTLINE_STRENGTH_LEVEL] = 1;
            syncedInts[SYNC_INT_SCAN_INDEX] = -1;
            syncedInts[SYNC_INT_AUDIOLINK_BAND] = 0;
            syncedInts[SYNC_INT_OVERLAY_INDEX] = -1;
            syncedInts[SYNC_INT_BOOL_FLAGS] = 0;
            syncedInts[SYNC_INT_APPLIED_COLOR_INDEX] = 0;

            syncedFloats[SYNC_FLOAT_AL_FILTER] = 1f;
            syncedFloats[SYNC_FLOAT_AL_SHAKE] = 1f;
            syncedFloats[SYNC_FLOAT_AL_BLUR] = 1f;
            syncedFloats[SYNC_FLOAT_AL_DISTORT] = 1f;
            syncedFloats[SYNC_FLOAT_AL_NOISE] = 1f;
            syncedFloats[SYNC_FLOAT_AL_FOG] = 0f;
            syncedFloats[SYNC_FLOAT_AL_OUTLINE] = 1f;
            syncedFloats[SYNC_FLOAT_AL_IMAGE] = 1f;
            syncedFloats[SYNC_FLOAT_AL_MISC] = 1f;
            syncedFloats[SYNC_FLOAT_SOBEL_OPACITY] = 0f;
            syncedFloats[SYNC_FLOAT_CURRENT_SOBEL] = 0.5f;
            syncedFloats[SYNC_FLOAT_INVERT] = 0f;
            syncedFloats[SYNC_FLOAT_AMPLITUDE] = 0f;
            syncedFloats[SYNC_FLOAT_BLUR] = 0f;
            syncedFloats[SYNC_FLOAT_DISTORTION] = 0f;
            syncedFloats[SYNC_FLOAT_NOISE] = 0f;
            syncedFloats[SYNC_FLOAT_SCANLINE] = 0f;
            syncedFloats[SYNC_FLOAT_DEPTH_BUFFER] = 0f;
            syncedFloats[SYNC_FLOAT_NORMAL_MAP] = 0f;
            syncedFloats[SYNC_FLOAT_SATURATION] = 1f;
            syncedFloats[SYNC_FLOAT_ROUNDING] = 0f;
            syncedFloats[SYNC_FLOAT_FOG_SAFE] = 0f;
            syncedFloats[SYNC_FLOAT_BRIGHTNESS] = 1f;
            syncedFloats[SYNC_FLOAT_HDR] = 0f;
            syncedFloats[SYNC_FLOAT_CONTRAST] = 1f;

            UpdateCurrentOutlineColorFromIndex();

            InitializeMochieMaterial();
            UpdateMochieShaderProperties(true);

            RequestSerialization();
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[MochieHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            EnsureSyncedArraysInitialized();
            launchpad = pad;
        }
        
        private void ResetMochieFeatureFlags()
        {
            supportsOverlaySelection = false;
            supportsScanSelection = false;
            supportsAudioLinkFilter = false;
            supportsAudioLinkShake = false;
            supportsAudioLinkBlur = false;
            supportsAudioLinkDistort = false;
            supportsAudioLinkNoise = false;
            supportsAudioLinkFog = false;
            supportsAudioLinkOutline = false;
            supportsAudioLinkImage = false;
            supportsAudioLinkTriplanar = false;
            supportsAudioLinkMisc = false;
        }
        
        private bool MaterialSupportsProperty(Material material, string propertyName)
        {
            return material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName);
        }
        
        private bool SetKeywordState(Material mat, string keyword, bool enabled)
        {
            if (mat == null || string.IsNullOrEmpty(keyword)) return false;
            
            bool changed = false;
            bool currentlyEnabled = mat.IsKeywordEnabled(keyword);
            
            // IMPORTANT: Only ENABLE keywords, never disable them at runtime.
            // In VRChat builds, disabling a keyword makes that shader variant unavailable.
            // Effects are controlled by property values (strength=0 = invisible effect).
            // Keywords are enabled at build time by the validator and should stay enabled.
            if (enabled && !currentlyEnabled)
            {
                mat.EnableKeyword(keyword);
                changed = true;
            }
            // Do NOT disable keywords - let property values control effect visibility
            
            return changed;
        }
        
        private bool SetKeyword(Material mat, MochieFeatureKeyword keyword, bool enabled)
        {
            bool changed = false;
            
            switch (keyword)
            {
                case MochieFeatureKeyword.Shake:
                changed |= SetKeywordState(mat, "_SHAKE_ON", enabled);
                break;
                case MochieFeatureKeyword.Distortion:
                changed |= SetKeywordState(mat, "_DISTORTION_ON", enabled);
                break;
                case MochieFeatureKeyword.BlurPixel:
                changed |= SetKeywordState(mat, "_BLUR_PIXEL_ON", enabled);
                break;
                case MochieFeatureKeyword.Noise:
                changed |= SetKeywordState(mat, "_NOISE_ON", enabled);
                break;
                case MochieFeatureKeyword.Color:
                changed |= SetKeywordState(mat, "_COLOR_ON", enabled);
                break;
                case MochieFeatureKeyword.SobelFilter:
                changed |= SetKeywordState(mat, "_SOBEL_FILTER_ON", enabled);
                break;
                case MochieFeatureKeyword.Outline:
                changed |= SetKeywordState(mat, "_OUTLINE_ON", enabled);
                break;
                case MochieFeatureKeyword.Fog:
                changed |= SetKeywordState(mat, "_FOG_ON", enabled);
                break;
                case MochieFeatureKeyword.Triplanar:
                changed |= SetKeywordState(mat, "_TRIPLANAR_ON", enabled);
                break;
                case MochieFeatureKeyword.ImageOverlay:
                changed |= SetKeywordState(mat, "_IMAGE_OVERLAY_ON", enabled);
                break;
                case MochieFeatureKeyword.AudioLink:
                changed |= SetKeywordState(mat, "_AUDIOLINK_ON", enabled);
                break;
            }
            
            return changed;
        }
        
        public bool ApplyMochieKeywords(Material target = null)
        {
            Debug.Log("[MochieHandler] ApplyMochieKeywords called");
            
            if (target == null && activeMochieMaterial == null)
            {
                Debug.LogWarning("ApplyMochieKeywords aborted: no material reference provided or available.");
                return false;
            }
            
            Material mat = target != null ? target : activeMochieMaterial;
            if (mat == null)
            {
                Debug.LogWarning("ApplyMochieKeywords aborted: resolved material reference is null.");
                return false;
            }

            NormalizeAdjustmentFields();

            bool changed = false;
            
            bool shakeActive = _amplitude > 0.0001f;
            changed |= SetKeyword(mat, MochieFeatureKeyword.Shake, shakeActive);
            
            bool distortionActive = distortionStrength > 0.0001f;
            changed |= SetKeyword(mat, MochieFeatureKeyword.Distortion, distortionActive);
            
            bool blurActive = blurStrength > 0.0001f;
            changed |= SetKeyword(mat, MochieFeatureKeyword.BlurPixel, blurActive);
            
            bool noiseActive = noiseStrength > 0.0001f;
            changed |= SetKeyword(mat, MochieFeatureKeyword.Noise, noiseActive);
            
            bool colorActive = invertStrength > 0.0001f ||
            !Mathf.Approximately(saturation, 1f) ||
            HasAdjustmentActivation(roundingOpacity) ||
            !Mathf.Approximately(_Brightness, 1f) ||
            !Mathf.Approximately(_Contrast, 1f) ||
            HasAdjustmentActivation(_HDR);
            changed |= SetKeyword(mat, MochieFeatureKeyword.Color, colorActive);
            
            bool sobelActive = sobelFilterOpacity > 0.0001f;
            changed |= SetKeyword(mat, MochieFeatureKeyword.SobelFilter, sobelActive);
            
            bool outlineActive = outlineType != 0;
            changed |= SetKeyword(mat, MochieFeatureKeyword.Outline, outlineActive);
            
            bool fogActive = HasAdjustmentActivation(fogSafeOpacity) || (supportsAudioLinkFog && _alFogStrength > 0.0001f);
            changed |= SetKeyword(mat, MochieFeatureKeyword.Fog, fogActive);
            
            bool triplanarActive = (hasSfxXFeatures && supportsScanSelection && _scanIndex >= 0) ||
            (supportsAudioLinkTriplanar && _alImageStrength > 0.0001f);
            changed |= SetKeyword(mat, MochieFeatureKeyword.Triplanar, triplanarActive);
            
            bool imageOverlayActive = hasSfxXFeatures && supportsOverlaySelection && _overlayIndex >= 0;
            changed |= SetKeyword(mat, MochieFeatureKeyword.ImageOverlay, imageOverlayActive);
            
            bool audioLinkActive = (supportsAudioLinkFilter && _alFilterStrength > 0.0001f) ||
            (supportsAudioLinkShake && _alShakeStrength > 0.0001f) ||
            (supportsAudioLinkBlur && _alBlurStrength > 0.0001f) ||
            (supportsAudioLinkDistort && _alDistortStrength > 0.0001f) ||
            (supportsAudioLinkNoise && _alNoiseStrength > 0.0001f) ||
            (supportsAudioLinkFog && _alFogStrength > 0.0001f) ||
            (supportsAudioLinkOutline && _alOutlineStrength > 0.0001f) ||
            ((supportsAudioLinkImage || supportsAudioLinkTriplanar) && _alImageStrength > 0.0001f) ||
            (supportsAudioLinkMisc && _alMiscStrength > 0.0001f);
            changed |= SetKeyword(mat, MochieFeatureKeyword.AudioLink, audioLinkActive);
            
            // Log keyword states for diagnostics
            Debug.Log($"[MochieHandler] ApplyMochieKeywords: outlineType={outlineType}, outlineActive={outlineActive}, sobelActive={sobelActive}, _OUTLINE_ON enabled: {mat.IsKeywordEnabled("_OUTLINE_ON")}, _SOBEL_FILTER_ON enabled: {mat.IsKeywordEnabled("_SOBEL_FILTER_ON")}");
            
            // Log all enabled keywords
            string[] allKeywords = mat.shaderKeywords;
            Debug.Log($"[MochieHandler] ApplyMochieKeywords: Active shader keywords ({allKeywords.Length}): {string.Join(", ", allKeywords)}");
            
            return changed;
        }
        
        public void InitializeMochieRuntime()
        {
            InitializeMaterialsIfNeeded();
        }
        
        public bool IsValid()
        {
            // Editor validates at build time - just check we have an active material
            return materialInitialized && activeMochieMaterial != null;
        }
        
        public bool HasAnyShaderDetected()
        {
            return hasSfxStandardFeatures || hasSfxXFeatures;
        }
        
        public bool HasXMochieShaderDetected()
        {
            return hasSfxXFeatures;
        }
        
        public bool IsMochieFolderAvailable()
        {
            return GetConfiguredMochieFolderIndex() >= 0;
        }
        
        public bool IsMochieFolderEnabled()
        {
            return HasConfiguredMochieComponents();
        }
        
        // Static wrapper methods removed - UdonSharp has a bug where having both a static
        // and instance method with the same name prevents the instance method from executing
        // at runtime in VRChat builds (compiles fine but method doesn't run).
        // Always call instance methods directly: handler.Method() with null checks.
        
        public int GetMochieFolderIndex()
        {
            int configuredIndex = GetConfiguredMochieFolderIndex();
            if (configuredIndex >= 0) return configuredIndex;
            return -1;
        }
        
        public bool FolderRepresentsMochie(int folderIndex)
        {
            int mochiIndex = GetMochieFolderIndex();
            return mochiIndex >= 0 && folderIndex == mochiIndex;
        }
        
        private int GetConfiguredMochieFolderIndex()
        {
            if (launchpad == null)
            {
                return -1;
            }
            
            return launchpad.FindFolderIndex(ToggleFolderType.Mochie);
        }
        
        private bool HasConfiguredMochieComponents()
        {
            return HasOperationalMochieFeatures() && GetConfiguredMochieFolderIndex() >= 0;
        }
        
        private bool HasOperationalMochieFeatures()
        {
            if (launchpad == null) return false;
            if (!IsValid()) return false;
            if (shaderRenderer == null) return false;
            
            return hasSfxStandardFeatures || hasSfxXFeatures;
        }
        
        public void InitializeMaterialsIfNeeded()
        {
            // Editor validates shaders at build time, so we trust that references are valid.
            // Just initialize the material if not already done.
            if (!materialInitialized)
            {
                InitializeMochieMaterial();
            }
            lastInitializationResult = materialInitialized;
        }
        
        public void OnSelect(int buttonIndex)
        {
            Debug.Log($"[MochieHandler] OnSelect called - buttonIndex: {buttonIndex}");
            
            if (launchpad == null)
            {
                Debug.Log("[MochieHandler] OnSelect: launchpad is null, returning");
                return;
            }
            
            if (!IsHandlerActive() || !materialInitialized)
            {
                Debug.Log($"[MochieHandler] OnSelect: handler not active or material not initialized - IsHandlerActive: {IsHandlerActive()}, materialInitialized: {materialInitialized}");
                return;
            }
            
            if (!TryGetDisplayPage(out int displayPage, out int totalPages))
            {
                Debug.Log("[MochieHandler] OnSelect: TryGetDisplayPage returned false");
                return;
            }

            Debug.Log($"[MochieHandler] OnSelect: displayPage={displayPage}, totalPages={totalPages}");

            if (!EnsureLocalOwnership())
            {
                Debug.LogWarning("[MochieHandler] OnSelect aborted - failed to secure ownership before state change.");
                return;
            }
            
            bool stateChanged = HandleMochieSelection(displayPage, buttonIndex);
            Debug.Log($"[MochieHandler] OnSelect: HandleMochieSelection returned stateChanged={stateChanged}");
            if (!stateChanged)
            {
                return;
            }
            
            Debug.Log("[MochieHandler] OnSelect: calling ApplyMochieState");
            ApplyMochieState();
            RequestSerialization();
        }
        
        public float GetClampedMochieShakeAmplitude()
        {
            if (!hasCachedMochieShakeAmplitude)
            {
                cachedMochieShakeAmplitude = Mathf.Clamp(mochieShakeAmplitude, MochieEffectStrengthMin, MochieEffectStrengthMax);
                hasCachedMochieShakeAmplitude = true;
            }
            
            return cachedMochieShakeAmplitude;
        }
        
        public float GetClampedMochieInvertStrength()
        {
            return Mathf.Clamp(mochieInvertStrength, MochieEffectStrengthMin, MochieEffectStrengthMax);
        }
        
        public float GetClampedMochieInvertPlusStrength()
        {
            return Mathf.Clamp(mochieInvertPlusStrength, MochieEffectStrengthMin, MochieEffectStrengthMax);
        }

        private bool HasAdjustmentActivation(float value)
        {
            return Mathf.Abs(value) > AdjustmentActivationThreshold;
        }

        private float NormalizeAdjustmentValue(float value)
        {
            return HasAdjustmentActivation(value) ? value : 0f;
        }

        private bool IsRoundingActive()
        {
            return HasAdjustmentActivation(roundingOpacity);
        }

        private void NormalizeRoundingOpacity()
        {
            roundingOpacity = NormalizeAdjustmentValue(roundingOpacity);
        }

        private void NormalizeAdjustmentFields()
        {
            roundingOpacity = NormalizeAdjustmentValue(roundingOpacity);
            fogSafeOpacity = NormalizeAdjustmentValue(fogSafeOpacity);
            _HDR = NormalizeAdjustmentValue(_HDR);
        }

        public bool IsInvertActive()
        {
            return invertStrength > 0.0001f && Mathf.Approximately(invertStrength, GetClampedMochieInvertStrength());
        }

        public bool IsInvertPlusActive()
        {
            return invertStrength > 0.0001f && Mathf.Approximately(invertStrength, GetClampedMochieInvertPlusStrength());
        }
        
        /// <summary>
        /// Checks if a dynamic fader toggle is active based on the toggle index.
        /// This uses semantic indices (0-18) that map to specific Mochie effects,
        /// matching the Read Only version's implementation.
        /// </summary>
        public bool IsMochieDynamicToggleActive(int toggleIndex)
        {
            switch (toggleIndex)
            {
                case 0:
                    return outlineType == 2; // Aura outline
                case 1:
                    return outlineType == 1; // Sobel outline
                case 2:
                    return sobelFilterOpacity > 0f;
                case 3:
                    return IsInvertActive() || IsInvertPlusActive(); // Invert group
                case 4:
                    return _amplitude > 0f;
                case 5:
                    return blurStrength > 0f;
                case 6:
                    return distortionStrength > 0f;
                case 7:
                    return noiseStrength > 0f;
                case 8:
                    return scanLineStrength > 0f;
                case 9:
                    return depthBufferOpacity > 0f;
                case 10:
                    return normalMapOpacity > 0f;
                case 11:
                    return !Mathf.Approximately(saturation, 1f);
                case 12:
                    return roundingOpacity > 0f;
                case 13:
                    return fogSafeOpacity > 0f;
                case 14:
                    return !Mathf.Approximately(_Brightness, 1f);
                case 15:
                    return !Mathf.Approximately(_Contrast, 1f);
                case 16:
                    return !Mathf.Approximately(_HDR, 0f);
                case 17:
                    return hasSfxXFeatures && supportsOverlaySelection && _overlayIndex >= 0 && overlayTextures != null && _overlayIndex < overlayTextures.Length;
                case 18:
                    return hasSfxXFeatures && supportsScanSelection && _scanIndex >= 0 && overlayTextures != null && _scanIndex < overlayTextures.Length;
                default:
                    return false;
            }
        }
        
        private bool HandleMochieSelection(int displayPage, int buttonIndex)
        {
            bool stateChanged;
            switch (displayPage)
            {
                case 0:
                stateChanged = HandleOutlineSelection(buttonIndex);
                break;
                case 1:
                stateChanged = HandleCoreEffectSelection(buttonIndex);
                break;
                case 2:
                stateChanged = HandleAdjustmentSelection(buttonIndex);
                break;
                case 3:
                stateChanged = HandleImageControlSelection(buttonIndex);
                break;
                case 4:
                stateChanged = HandleAudioLinkSelection(buttonIndex);
                break;
                case 5:
                stateChanged = HandleAudioLinkStrengthSelection(buttonIndex);
                break;
                default:
                stateChanged = false;
                break;
            }
            
            if (stateChanged && displayPage == 0 && buttonIndex >= 3 && buttonIndex <= 5 && sobelFilterOpacity > 0f)
            {
                float targetSobel = currentSobelOpacity;
                if (!Mathf.Approximately(sobelFilterOpacity, targetSobel))
                {
                    sobelFilterOpacity = targetSobel;
                }
            }
            
            return stateChanged;
        }
        
        private bool HandleOutlineSelection(int buttonIndex)
        {
            switch (buttonIndex)
            {
                case 0:
                {
                    int targetType = outlineType == 2 ? 0 : 2;
                    if (targetType == outlineType)
                    {
                        return false;
                    }
                    
                    outlineType = targetType;
                    return true;
                }
                case 1:
                {
                    int targetType = outlineType == 1 ? 0 : 1;
                    if (targetType == outlineType)
                    {
                        return false;
                    }
                    
                    outlineType = targetType;
                    return true;
                }
                case 2:
                {
                    float targetOpacity = Mathf.Approximately(sobelFilterOpacity, 0f)
                    ? currentSobelOpacity
                    : 0f;
                    
                    if (Mathf.Approximately(targetOpacity, sobelFilterOpacity))
                    {
                        return false;
                    }
                    
                    sobelFilterOpacity = targetOpacity;
                    return true;
                }
                case 3:
                {
                    float targetSobel = Mathf.Clamp(mochieOutlineSobelLow, MochieSobelOpacityMin, MochieSobelOpacityMax);
                    bool changed = outlineStrengthLevel != 0 || !Mathf.Approximately(currentSobelOpacity, targetSobel);
                    outlineStrengthLevel = 0;
                    currentSobelOpacity = targetSobel;
                    return changed;
                }
                case 4:
                {
                    float targetSobel = Mathf.Clamp(mochieOutlineSobelNormal, MochieSobelOpacityMin, MochieSobelOpacityMax);
                    bool changed = outlineStrengthLevel != 1 || !Mathf.Approximately(currentSobelOpacity, targetSobel);
                    outlineStrengthLevel = 1;
                    currentSobelOpacity = targetSobel;
                    return changed;
                }
                case 5:
                {
                    float targetSobel = Mathf.Clamp(mochieOutlineSobelHigh, MochieSobelOpacityMin, MochieSobelOpacityMax);
                    bool changed = outlineStrengthLevel != 2 || !Mathf.Approximately(currentSobelOpacity, targetSobel);
                    outlineStrengthLevel = 2;
                    currentSobelOpacity = targetSobel;
                    return changed;
                }
                case 7:
                ApplySelectedColor();
                return true;
                case 8:
                CycleColorSelection();
                return true;
                default:
                return false;
            }
        }
        
        private bool HandleCoreEffectSelection(int buttonIndex)
        {
            switch (buttonIndex)
            {
                case 0:
                {
                    float baseInvert = GetClampedMochieInvertStrength();
                    float targetInvert = Mathf.Approximately(invertStrength, baseInvert) ? 0f : baseInvert;
                    if (Mathf.Approximately(targetInvert, invertStrength))
                    {
                        return false;
                    }
                    
                    invertStrength = targetInvert;
                    return true;
                }
                case 1:
                {
                    float boostedInvert = GetClampedMochieInvertPlusStrength();
                    float targetInvert = Mathf.Approximately(invertStrength, boostedInvert) ? 0f : boostedInvert;
                    if (Mathf.Approximately(targetInvert, invertStrength))
                    {
                        return false;
                    }
                    
                    invertStrength = targetInvert;
                    return true;
                }
                case 2:
                {
                    float targetAmplitude = Mathf.Approximately(_amplitude, 0f) ? GetClampedMochieShakeAmplitude() : 0f;
                    if (Mathf.Approximately(targetAmplitude, _amplitude))
                    {
                        return false;
                    }
                    
                    _amplitude = targetAmplitude;
                    return true;
                }
                case 3:
                {
                    float targetBlur = Mathf.Approximately(blurStrength, 0f)
                    ? Mathf.Clamp(mochieBlurStrength, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetBlur, blurStrength))
                    {
                        return false;
                    }
                    
                    blurStrength = targetBlur;
                    return true;
                }
                case 4:
                {
                    float targetDistortion = Mathf.Approximately(distortionStrength, 0f)
                    ? Mathf.Clamp(mochieDistortionStrength, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetDistortion, distortionStrength))
                    {
                        return false;
                    }
                    
                    distortionStrength = targetDistortion;
                    return true;
                }
                case 5:
                {
                    float targetNoise = Mathf.Approximately(noiseStrength, 0f)
                    ? Mathf.Clamp(mochieNoiseStrength, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetNoise, noiseStrength))
                    {
                        return false;
                    }
                    
                    noiseStrength = targetNoise;
                    return true;
                }
                case 6:
                {
                    float targetScan = Mathf.Approximately(scanLineStrength, 0f)
                    ? Mathf.Clamp(mochieScanLineStrength, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetScan, scanLineStrength))
                    {
                        return false;
                    }
                    
                    scanLineStrength = targetScan;
                    return true;
                }
                case 7:
                {
                    if (!hasSfxXFeatures)
                    {
                        return false;
                    }
                    
                    float targetDepth = Mathf.Approximately(depthBufferOpacity, 0f)
                    ? Mathf.Clamp(mochieDepthBufferOpacity, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetDepth, depthBufferOpacity))
                    {
                        return false;
                    }
                    
                    depthBufferOpacity = targetDepth;
                    return true;
                }
                case 8:
                {
                    if (!hasSfxXFeatures)
                    {
                        return false;
                    }
                    
                    float targetNormal = Mathf.Approximately(normalMapOpacity, 0f)
                    ? Mathf.Clamp(mochieNormalMapOpacity, MochieEffectStrengthMin, MochieEffectStrengthMax)
                    : 0f;
                    if (Mathf.Approximately(targetNormal, normalMapOpacity))
                    {
                        return false;
                    }
                    
                    normalMapOpacity = targetNormal;
                    return true;
                }
                default:
                return false;
            }
        }
        
        private bool HandleAdjustmentSelection(int buttonIndex)
        {
            switch (buttonIndex)
            {
                case 0:
                {
                    float targetSaturation = saturation - 0.1f;
                    if (Mathf.Approximately(targetSaturation, saturation))
                    {
                        return false;
                    }
                    
                    saturation = targetSaturation;
                    return true;
                }
                case 1:
                if (Mathf.Approximately(saturation, 1f))
                {
                    return false;
                }
                
                saturation = 1f;
                return true;
                case 2:
                {
                    float targetSaturation = saturation + 0.1f;
                    if (Mathf.Approximately(targetSaturation, saturation))
                    {
                        return false;
                    }
                    
                    saturation = targetSaturation;
                    return true;
                }
                case 3:
                if (hasSfxXFeatures)
                {
                    float targetRounding = Mathf.Max(0f, roundingOpacity - 0.1f);
                    if (Mathf.Approximately(targetRounding, roundingOpacity))
                    {
                        return false;
                    }

                    roundingOpacity = NormalizeAdjustmentValue(targetRounding);
                    NormalizeRoundingOpacity();
                    return true;
                }

                float decreasedHdr = Mathf.Clamp(_HDR - 0.1f, -2f, 2f);
                if (Mathf.Approximately(decreasedHdr, _HDR))
                {
                    return false;
                }
                
                _HDR = NormalizeAdjustmentValue(decreasedHdr);
                return true;
                case 4:
                if (hasSfxXFeatures)
                {
                    if (!HasAdjustmentActivation(roundingOpacity))
                    {
                        return false;
                    }

                    roundingOpacity = 0f;
                    return true;
                }
                
                if (!HasAdjustmentActivation(_HDR))
                {
                    return false;
                }

                _HDR = 0f;
                return true;
                case 5:
                if (hasSfxXFeatures)
                {
                    float targetRounding = Mathf.Min(1f, roundingOpacity + 0.1f);
                    if (Mathf.Approximately(targetRounding, roundingOpacity))
                    {
                        return false;
                    }

                    roundingOpacity = NormalizeAdjustmentValue(targetRounding);
                    NormalizeRoundingOpacity();
                    return true;
                }

                float increasedHdr = Mathf.Clamp(_HDR + 0.1f, -2f, 2f);
                if (Mathf.Approximately(increasedHdr, _HDR))
                {
                    return false;
                }
                
                _HDR = NormalizeAdjustmentValue(increasedHdr);
                return true;
                case 6:
                {
                    float targetFog = Mathf.Max(0f, fogSafeOpacity - 0.05f);
                    if (Mathf.Approximately(targetFog, fogSafeOpacity))
                    {
                        return false;
                    }

                    fogSafeOpacity = NormalizeAdjustmentValue(targetFog);
                    return true;
                }
                case 7:
                if (!HasAdjustmentActivation(fogSafeOpacity))
                {
                    return false;
                }

                fogSafeOpacity = 0f;
                return true;
                case 8:
                {
                    float targetFog = Mathf.Min(1f, fogSafeOpacity + 0.05f);
                    if (Mathf.Approximately(targetFog, fogSafeOpacity))
                    {
                        return false;
                    }

                    fogSafeOpacity = NormalizeAdjustmentValue(targetFog);
                    return true;
                }
                default:
                return false;
            }
        }
        
        private bool HandleImageControlSelection(int buttonIndex)
        {
            switch (buttonIndex)
            {
                case 0:
                {
                    float targetBrightness = Mathf.Clamp(_Brightness - 0.1f, -2f, 4f);
                    if (Mathf.Approximately(targetBrightness, _Brightness))
                    {
                        return false;
                    }
                    
                    _Brightness = targetBrightness;
                    return true;
                }
                case 1:
                if (Mathf.Approximately(_Brightness, 1f))
                {
                    return false;
                }
                
                _Brightness = 1f;
                return true;
                case 2:
                {
                    float targetBrightness = Mathf.Clamp(_Brightness + 0.1f, -2f, 4f);
                    if (Mathf.Approximately(targetBrightness, _Brightness))
                    {
                        return false;
                    }
                    
                    _Brightness = targetBrightness;
                    return true;
                }
                case 3:
                {
                    float targetContrast = Mathf.Clamp(_Contrast - 0.02f, 0.5f, 1.5f);
                    if (Mathf.Approximately(targetContrast, _Contrast))
                    {
                        return false;
                    }
                    
                    _Contrast = targetContrast;
                    return true;
                }
                case 4:
                if (Mathf.Approximately(_Contrast, 1f))
                {
                    return false;
                }
                
                _Contrast = 1f;
                return true;
                case 5:
                {
                    float targetContrast = Mathf.Clamp(_Contrast + 0.02f, 0.5f, 1.5f);
                    if (Mathf.Approximately(targetContrast, _Contrast))
                    {
                        return false;
                    }
                    
                    _Contrast = targetContrast;
                    return true;
                }
                case 6:
                if (!hasSfxXFeatures)
                {
                    return false;
                }

                {
                    float decreasedHdr = Mathf.Clamp(_HDR - 0.1f, -2f, 2f);
                    if (Mathf.Approximately(decreasedHdr, _HDR))
                    {
                        return false;
                    }

                    _HDR = NormalizeAdjustmentValue(decreasedHdr);
                    return true;
                }
                case 7:
                if (!hasSfxXFeatures || !HasAdjustmentActivation(_HDR))
                {
                    return false;
                }

                _HDR = 0f;
                return true;
                case 8:
                if (!hasSfxXFeatures)
                {
                    return false;
                }

                {
                    float increasedHdr = Mathf.Clamp(_HDR + 0.1f, -2f, 2f);
                    if (Mathf.Approximately(increasedHdr, _HDR))
                    {
                        return false;
                    }

                    _HDR = NormalizeAdjustmentValue(increasedHdr);
                    return true;
                }
                default:
                return false;
            }
        }
        
        private bool HandleAudioLinkSelection(int buttonIndex)
        {
            if (hasSfxXFeatures)
            {
                if (buttonIndex < OverlayButtonCount)
                {
                    if (!supportsOverlaySelection)
                    {
                        if (_overlayIndex == -1)
                        {
                            return false;
                        }
                        
                        _overlayIndex = -1;
                        return true;
                    }
                    
                    // Don't allow selecting overlay if no texture is assigned
                    if (!HasOverlayTextureAtIndex(buttonIndex))
                    {
                        return false;
                    }
                    
                    int targetOverlay = buttonIndex;
                    if (_overlayIndex == targetOverlay)
                    {
                        _overlayIndex = -1;
                    }
                    else
                    {
                        _overlayIndex = targetOverlay;
                    }
                    
                    return true;
                }
                
                if (buttonIndex >= ScanButtonStartIndex && buttonIndex < TotalOverlayScanButtons)
                {
                    if (!supportsScanSelection)
                    {
                        if (_scanIndex == -1)
                        {
                            return false;
                        }
                        
                        _scanIndex = -1;
                        return true;
                    }
                    
                    int scanIdx = buttonIndex - ScanButtonStartIndex;
                    
                    // Don't allow selecting scan if no texture is assigned
                    if (!HasOverlayTextureAtIndex(scanIdx))
                    {
                        return false;
                    }
                    
                    _scanIndex = _scanIndex == scanIdx ? -1 : scanIdx;
                    return true;
                }
                
                switch (buttonIndex)
                {
                    case 6:
                    if (_audioLinkBand == 0)
                    {
                        return false;
                    }
                    
                    _audioLinkBand = 0;
                    return true;
                    case 7:
                    {
                        int currentBand = _audioLinkBand;
                        _audioLinkBand = currentBand == 1 ? 2 : 1;
                        return currentBand != _audioLinkBand;
                    }
                    case 8:
                    if (_audioLinkBand == 3)
                    {
                        return false;
                    }
                    
                    _audioLinkBand = 3;
                    return true;
                    default:
                    return false;
                }
            }
            
            if (buttonIndex <= 2)
            {
                switch (buttonIndex)
                {
                    case 0:
                    if (_audioLinkBand == 0)
                    {
                        return false;
                    }
                    
                    _audioLinkBand = 0;
                    return true;
                    case 1:
                    {
                        int currentBand = _audioLinkBand;
                        _audioLinkBand = currentBand == 1 ? 2 : 1;
                        return currentBand != _audioLinkBand;
                    }
                    case 2:
                    if (_audioLinkBand == 3)
                    {
                        return false;
                    }
                    
                    _audioLinkBand = 3;
                    return true;
                    default:
                    return false;
                }
            }
            
            int strengthIndex = buttonIndex - StandardAudioLinkStrengthStartIndex;
            if (strengthIndex < 0 || strengthIndex >= StandardAudioLinkStrengthMap.Length)
            {
                return false;
            }
            
            HandleAudioLinkStrengthButton(StandardAudioLinkStrengthMap, strengthIndex);
            return true;
        }
        
        private bool HandleAudioLinkStrengthSelection(int buttonIndex)
        {
            if (hasSfxXFeatures)
            {
                int[] fullMap = FullAudioLinkStrengthMap;
                if (fullMap == null || buttonIndex < 0 || buttonIndex >= fullMap.Length)
                {
                    return false;
                }
                
                HandleAudioLinkStrengthButton(fullMap, buttonIndex);
                return true;
            }
            
            int strengthIndex = buttonIndex - StandardAudioLinkStrengthStartIndex;
            int[] standardMap = StandardAudioLinkStrengthMap;
            if (standardMap == null || strengthIndex < 0 || strengthIndex >= standardMap.Length)
            {
                return false;
            }
            
            HandleAudioLinkStrengthButton(standardMap, strengthIndex);
            return true;
        }
        
        public void OnPageChange(int direction)
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            int instanceId = gameObject.GetInstanceID();
            Debug.Log($"[MochieHandler] OnPageChange START - Player: {localPlayerName}, Direction: {direction}, CurrentPage: {mochiePage}, IsOwner: {Networking.IsOwner(gameObject)}, InstanceID: {instanceId}");
            
            if (launchpad == null)
            {
                Debug.LogWarning($"[MochieHandler] OnPageChange ABORT - launchpad is null");
                return;
            }
            
            if (!launchpad.CanLocalUserInteract())
            {
                Debug.LogWarning($"[MochieHandler] OnPageChange ABORT - CanLocalUserInteract returned false");
                return;
            }
            
            // Editor validates shaders; launchpad determines which handler is active.
            // No additional runtime checks needed here.

            Debug.Log($"[MochieHandler] OnPageChange - About to EnsureLocalOwnership");
            if (!EnsureLocalOwnership())
            {
                Debug.LogWarning("[MochieHandler] OnPageChange ABORT - failed to secure ownership, cannot serialize page change.");
                return;
            }
            
            int oldPage = mochiePage;
            UpdatePage(direction);
            Debug.Log($"[MochieHandler] OnPageChange - Page changed from {oldPage} to {mochiePage}, InstanceID: {instanceId}");

            LogSerializationPayload("OnPageChange - Before RequestSerialization");
            Debug.Log($"[MochieHandler] OnPageChange - Calling RequestSerialization to sync mochiePage={mochiePage} to other players");
            RequestSerialization();
            Debug.Log($"[MochieHandler] OnPageChange END - Player: {localPlayerName}, NewPage: {mochiePage}, InstanceID: {instanceId}");
        }

        private bool EnsureLocalOwnership()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            bool wasOwner = Networking.IsOwner(gameObject);

            if (localPlayer == null)
            {
                Debug.LogWarning("[MochieHandler] EnsureLocalOwnership - Local player is null; cannot take ownership.");
                return wasOwner;
            }

            if (!wasOwner)
            {
                Debug.Log($"[MochieHandler] EnsureLocalOwnership - Player: {localPlayer.displayName} taking ownership");
                Networking.SetOwner(localPlayer, gameObject);
            }

            bool isOwnerNow = Networking.IsOwner(gameObject);
            if (!isOwnerNow)
            {
                Debug.LogWarning("[MochieHandler] EnsureLocalOwnership - Ownership request failed; serialization will not sync from this client.");
            }

            return isOwnerNow;
        }

        public void RequestDisplayUpdate()
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            Debug.Log($"[MochieHandler] RequestDisplayUpdate called - Player: {localPlayerName}, CurrentPage: {mochiePage}");
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }

        private void LogSerializationPayload(string context)
        {
            int totalPages = GetPageCount();
            int clampedPage = mochiePage;

            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            string ownerName = owner != null ? owner.displayName : "Unknown";
            bool isOwner = Networking.IsOwner(gameObject);
            if (totalPages > 0)
            {
                int maxPageIndex = totalPages - 1;
                if (mochiePage < 0 || mochiePage > maxPageIndex)
                {
                    clampedPage = Mathf.Clamp(mochiePage, 0, maxPageIndex);
                    if (isOwner)
                    {
                        Debug.LogWarning($"[MochieHandler] {context} - Owner clamping mochiePage from {mochiePage} to {clampedPage} before serialization.");
                        mochiePage = clampedPage;
                    }
                    else
                    {
                        Debug.LogWarning($"[MochieHandler] {context} - Non-owner detected out-of-range mochiePage {mochiePage}, reporting clamp target {clampedPage} without mutating synced value.");
                    }
                }
            }

            Debug.Log($"[MochieHandler] {context} - Owner: {ownerName}, LocalIsOwner: {isOwner}, SerializedPage: {mochiePage}, TotalPages: {totalPages}, LastSerialized: {_lastSerializedPage}, LastSynced: {_lastSyncedPage}");
        }

        public override void OnPreSerialization()
        {
            _lastSerializedPage = mochiePage;
            LogSerializationPayload("OnPreSerialization");
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            string ownerName = owner != null ? owner.displayName : "Unknown";
            Debug.Log($"[MochieHandler] OnPostSerialization - Player: {localPlayerName}, Owner: {ownerName}, LocalIsOwner: {Networking.IsOwner(gameObject)}, SerializedPage: {mochiePage}, Success: {result.success}, ByteCount: {result.byteCount}, LastSerialized: {_lastSerializedPage}, LastSynced: {_lastSyncedPage}");
            base.OnPostSerialization(result);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            string newOwnerName = player != null ? player.displayName : "Unknown";
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            Debug.Log($"[MochieHandler] OnOwnershipTransferred - NewOwner: {newOwnerName}, LocalPlayer: {localPlayerName}, LocalIsOwner: {Networking.IsOwner(gameObject)}, CurrentPage: {mochiePage}");
            base.OnOwnershipTransferred(player);
        }

        public override void OnDeserialization()
        {
            // Defensive: ensure arrays are initialized before accessing them.
            // VRChat may call OnDeserialization before Awake() in some edge cases.
            EnsureSyncedArraysInitialized();
            
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            bool isOwner = Networking.IsOwner(gameObject);
            int instanceId = gameObject.GetInstanceID();
            int pageBeforeDeserialize = mochiePage;
            Debug.Log($"[MochieHandler] OnDeserialization START - Player: {localPlayerName}, IsOwner: {isOwner}, ReceivedPage: {mochiePage}, InstanceID: {instanceId}, LastSynced: {_lastSyncedPage}, LastSerialized: {_lastSerializedPage}");

            base.OnDeserialization();

            // Capture the page value AFTER base.OnDeserialization() applied network sync
            int receivedPage = mochiePage;

            // Log the page value AFTER base.OnDeserialization() to see if it changed
            Debug.Log($"[MochieHandler] OnDeserialization AFTER BASE - mochiePage is now: {mochiePage}, PageBeforeDeserialize: {pageBeforeDeserialize}, InstanceID: {instanceId}, LastSynced: {_lastSyncedPage}, LastSerialized: {_lastSerializedPage}");

            // Validate and clamp page to valid range (logic moved from FieldChangeCallback)
            int totalPages = GetPageCount();
            if (totalPages > 0)
            {
                int maxPageIndex = totalPages - 1;
                if (mochiePage < 0 || mochiePage > maxPageIndex)
                {
                    int clampedPage = Mathf.Clamp(mochiePage, 0, maxPageIndex);
                    Debug.LogWarning($"[MochieHandler] OnDeserialization - Page {mochiePage} out of range, clamping to {clampedPage}");
                    mochiePage = clampedPage;
                }
            }
            _lastSyncedPage = mochiePage;

            // Compare against the page BEFORE deserialization to detect actual changes
            if (pageBeforeDeserialize != receivedPage)
            {
                LogSerializationPayload("OnDeserialization - Applied");
            }

            if (launchpad == null)
            {
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
                Debug.Log($"[MochieHandler] OnDeserialization - Launchpad reference was null, now: {(launchpad != null ? "found" : "still null")}");
            }

            Debug.Log($"[MochieHandler] OnDeserialization - Applying Mochie state with page: {mochiePage}");
            
            // Update local currentOutlineColor from synced appliedColorIndex
            UpdateCurrentOutlineColorFromIndex();
            
            ApplyMochieState();
            RequestDisplayUpdate();
            Debug.Log($"[MochieHandler] OnDeserialization END - Player: {localPlayerName}, Page: {mochiePage}, InstanceID: {instanceId}");
        }
        
        public string GetLabel(int buttonIndex)
        {
            if (launchpad != null && buttonIndex == 10)
            {
                return launchpad.GetFolderLabelForIndex(folderIndex, false);
            }

            return GetButtonLabel(buttonIndex);
        }
        
        public Color GetColor(int buttonIndex)
        {
            return GetButtonColor(buttonIndex);
        }
        
        public bool IsInteractable(int buttonIndex)
        {
            if (buttonIndex == 10)
            {
                return IsMochieFolderEnabled();
            }

            return IsButtonEnabled(buttonIndex);
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (launchpad == null)
            {
                return false;
            }
            
            if (!IsButtonEnabled(buttonIndex))
            {
                return false;
            }
            
            // Check if the button's color equals the active color (indicating the toggle is on)
            Color buttonColor = GetButtonColor(buttonIndex);
            Color activeColor = launchpad.GetActiveColor();
            return ColorsApproximatelyEqual(buttonColor, activeColor);
        }
        
        public string GetButtonLabel(int localIndex)
        {
            if (!IsHandlerActive())
            {
                if (localIndex == 10)
                {
                    return "Mochie";
                }
                
                if (localIndex == 9)
                {
                    return "0/0";
                }
                
                // Runtime: No guidance messages. Build validator ensures proper configuration.
                return string.Empty;
            }
            
            if (!TryGetDisplayPage(out int displayPage, out int totalPages))
            {
                return string.Empty;
            }
            
            string pageLabel = FormatMochiePageLabel(mochiePage, totalPages);
            
            switch (displayPage)
            {
                case 0:
                return GetOutlinePageLabel(localIndex, pageLabel);
                case 1:
                return GetCoreEffectLabel(localIndex, pageLabel);
                case 2:
                return GetAdjustmentLabel(localIndex, pageLabel);
                case 3:
                return GetImageControlLabel(localIndex, pageLabel);
                case 4:
                return hasSfxXFeatures
                ? GetAudioLinkSelectionLabel(localIndex, pageLabel)
                : GetStandardAudioLinkLabel(localIndex, pageLabel);
                case 5:
                return hasSfxXFeatures
                ? GetAudioLinkStrengthLabel(localIndex, pageLabel, FullAudioLinkStrengthMap, 0)
                : GetAudioLinkStrengthLabel(localIndex, pageLabel, StandardAudioLinkStrengthMap, StandardAudioLinkStrengthStartIndex);
                default:
                return string.Empty;
            }
        }
        
        public Color GetButtonColor(int localIndex)
        {
            if (!IsHandlerActive())
            {
                return launchpad != null ? launchpad.GetInactiveColor() : Color.clear;
            }
            
            if (!TryGetDisplayPage(out int displayPage, out int totalPages))
            {
                return launchpad.GetInactiveColor();
            }
            
            switch (displayPage)
            {
                case 0:
                return GetOutlineButtonColor(localIndex);
                case 1:
                return GetCoreEffectColor(localIndex);
                case 2:
                return GetAdjustmentColor(localIndex);
                case 3:
                return GetImageControlColor(localIndex);
                case 4:
                return hasSfxXFeatures ? GetAudioLinkSelectionColor(localIndex) : GetStandardAudioLinkColor(localIndex);
                case 5:
                return hasSfxXFeatures
                ? GetAudioLinkStrengthColor(localIndex, FullAudioLinkStrengthMap, 0)
                : GetAudioLinkStrengthColor(localIndex, StandardAudioLinkStrengthMap, StandardAudioLinkStrengthStartIndex);
                default:
                return launchpad.GetInactiveColor();
            }
        }
        
        public bool IsButtonEnabled(int localIndex)
        {
            if (!IsHandlerActive())
            {
                return false;
            }
            
            if (localIndex < 0)
            {
                return false;
            }
            
            if (!TryGetDisplayPage(out int displayPage, out int totalPages))
            {
                return false;
            }
            
            // On the audio link selection page, disable overlay/scan buttons that have no texture
            if (displayPage == AudioLinkSelectionPage && hasSfxXFeatures)
            {
                // Buttons 0-2 are overlay buttons, buttons 3-5 are scan buttons
                if (localIndex < OverlayButtonCount)
                {
                    // Overlay button - check if texture exists at this index
                    return HasOverlayTextureAtIndex(localIndex);
                }
                else if (localIndex >= ScanButtonStartIndex && localIndex < TotalOverlayScanButtons)
                {
                    // Scan button - check if texture exists at the corresponding index (0, 1, 2)
                    int textureIndex = localIndex - ScanButtonStartIndex;
                    return HasOverlayTextureAtIndex(textureIndex);
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Checks if a valid (non-null) overlay texture exists at the given index.
        /// </summary>
        private bool HasOverlayTextureAtIndex(int index)
        {
            if (overlayTextures == null || index < 0 || index >= overlayTextures.Length)
            {
                return false;
            }
            
            return overlayTextures[index] != null;
        }
        
        public void OnLaunchpadDeserialized()
        {
            ApplyMochieState();
        }
        
        public void ApplyMochieState()
        {
            Debug.Log($"[MochieHandler] ApplyMochieState called - folderIndex: {folderIndex}");
            
            if (launchpad == null || launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Mochie)
            {
                Debug.Log($"[MochieHandler] ApplyMochieState: early return - launchpad null: {launchpad == null}");
                return;
            }
            
            InitializeMaterialsIfNeeded();
            Debug.Log($"[MochieHandler] ApplyMochieState: materialInitialized={materialInitialized}, activeMochieMaterial null: {activeMochieMaterial == null}");
            
            // Always apply material state if Mochie is enabled, regardless of current folder selection.
            // The shader effect is visible regardless of which launchpad folder is selected.
            bool enabled = IsMochieFolderEnabled();
            Debug.Log($"[MochieHandler] ApplyMochieState: IsMochieFolderEnabled={enabled}");
            if (enabled)
            {
                ApplyMochieMaterial();
            }
        }
        
        public void InitializeMochieMaterial()
        {
            // The editor pre-assigns the appropriate Mochie material to the renderer before build
            // and validates all shaders. No runtime validation needed.
            if (shaderRenderer == null)
            {
                materialInitialized = false;
                return;
            }
            
            // Get the material directly from the renderer (assigned by editor)
            // Use sharedMaterial to modify the actual asset, not an instance
            activeMochieMaterial = shaderRenderer.sharedMaterial;
            
            if (activeMochieMaterial == null)
            {
                materialInitialized = false;
                return;
            }
            
            // Set feature flags based on the useSfxXLayout flag set by the editor
            // The editor ensures this matches the assigned material
            hasSfxXFeatures = useSfxXLayout;
            hasSfxStandardFeatures = true;
            
            UpdateFeatureSupport(activeMochieMaterial);
            
            // Reset material property values to default state - this undoes any play-mode changes
            // that persisted and ensures the material matches the "all toggles off" state.
            // IMPORTANT: We do NOT disable shader keywords here because keywords control which
            // shader variants are compiled into the VRChat build. If keywords are disabled on
            // the sharedMaterial before build, those features won't work in the built world.
            // Keywords should remain enabled on the material asset; only property values are reset.
            ResetMaterialToDefaultState(activeMochieMaterial);
            
            // Initialize outline colors
            if (outlineColors == null || outlineColors.Length == 0) outlineColors = new Color[]
            {
                Color.white
            };
            if (outlineColorNames == null || outlineColorNames.Length != outlineColors.Length)
            {
                string[] newNames = new string[outlineColors.Length];
                if (outlineColorNames != null)
                {
                    int copyLength = Mathf.Min(outlineColorNames.Length, newNames.Length);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newNames[i] = outlineColorNames[i];
                    }
                }
                for (int i = 0; i < newNames.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(newNames[i]))
                    {
                        newNames[i] = string.Empty;
                    }
                    else
                    {
                        newNames[i] = newNames[i].Trim();
                    }
                }
                outlineColorNames = newNames;
            }
            else
            {
                for (int i = 0; i < outlineColorNames.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(outlineColorNames[i]))
                    {
                        outlineColorNames[i] = string.Empty;
                    }
                    else
                    {
                        outlineColorNames[i] = outlineColorNames[i].Trim();
                    }
                }
            }
            
            // Set default outline color to index 0
            currentOutlineColor = outlineColors[0];
            selectedColorIndex = 0;
            appliedColorIndex = 0;
            
            if (launchpad != null)
            {
                launchpad.ForceMaterialUpdate();
            }
            
            materialInitialized = true;
        }
        
        /// <summary>
        /// Resets the Mochie material to its default state with all effects disabled.
        /// This ensures the material matches the "all toggles off" state and undoes
        /// any play-mode changes that may have persisted to the asset.
        /// </summary>
        private void ResetMaterialToDefaultState(Material mat)
        {
            if (mat == null) return;
            
            // Reset outline settings to defaults (off)
            mat.SetFloat("_AuraStr", Mathf.Clamp(mochieOutlineAuraNormal, MochieAuraStrengthMin, MochieAuraStrengthMax));
            mat.SetFloat("_OutlineThresh", Mathf.Clamp(mochieOutlineThresholdNormal, MochieOutlineThresholdMin, MochieOutlineThresholdMax));
            
            // Reset all effect strengths to off (0)
            currentSobelOpacity = Mathf.Clamp(mochieOutlineSobelNormal, MochieSobelOpacityMin, MochieSobelOpacityMax);
            sobelFilterOpacity = 0f;
            mat.SetFloat("_SobelFilterOpacity", 0f);
            
            blurStrength = 0f;
            mat.SetFloat("_BlurStr", 0f);
            
            distortionStrength = 0f;
            mat.SetFloat("_DistortionStr", 0f);
            
            noiseStrength = 0f;
            mat.SetFloat("_Noise", 0f);
            
            scanLineStrength = 0f;
            mat.SetFloat("_ScanLine", 0f);
            
            depthBufferOpacity = 0f;
            normalMapOpacity = 0f;
            mat.SetFloat("_DBOpacity", 0f);
            mat.SetFloat("_NMFOpacity", 0f);
            
            // Reset color/adjustment effects
            saturation = 1.0f;
            roundingOpacity = 0.0f;
            fogSafeOpacity = 0.0f;
            _Brightness = 1f;
            _Contrast = 1f;
            _HDR = 0f;
            mat.SetFloat("_Saturation", 1f);
            mat.SetFloat("_RoundingOpacity", 0f);
            mat.SetFloat("_FogSafeOpacity", 0f);
            
            // Reset overlay/image effects
            _overlayIndex = -1;
            mat.SetTexture("_ScreenTex", null);
            if (mat.HasProperty("_SSTColor"))
            {
                Color sstColor = mat.GetColor("_SSTColor");
                sstColor.a = 0f;
                mat.SetColor("_SSTColor", sstColor);
            }
            
            // Reset outline type and strength
            outlineType = 0;
            outlineStrengthLevel = 1;
            invertStrength = 0f;
            _amplitude = 0f;
            
            mat.SetInt("_OutlineType", 0);
            mat.SetFloat("_Invert", 0f);
            mat.SetFloat("_Amplitude", 0f);
            
            // Reset triplanar/scan effects
            _scanIndex = -1;
            if (mat.HasProperty("_TPColor"))
            {
                Color tpColor = mat.GetColor("_TPColor");
                tpColor.a = 0f;
                mat.SetColor("_TPColor", tpColor);
            }
            mat.SetTexture("_TPTexture", null);
            
            // Reset AudioLink settings
            _audioLinkBand = 0;
            _alFilterStrength = 1f;
            _alShakeStrength = 1f;
            _alBlurStrength = 1f;
            _alDistortStrength = 1f;
            _alNoiseStrength = 1f;
            _alFogStrength = 0f;
            _alOutlineStrength = 1f;
            _alImageStrength = 1f;
            _alMiscStrength = 1f;
            
            if (launchpad != null)
            {
                launchpad.UpdateAudioLinkBands();
            }
            
            // Note: We do NOT call UpdateMochieShaderProperties() or ApplyMochieKeywords() here
            // because those would disable shader keywords. Keywords must remain enabled on the
            // sharedMaterial so shader variants are compiled into the VRChat build.
            // Property values are already set above; keywords will be managed at runtime when
            // effects are toggled on/off by the user.
        }
        
        public void ApplyMochieMaterial()
        {
            Debug.Log("[MochieHandler] ApplyMochieMaterial called");
            
            // Apply material if Mochie is enabled, regardless of current folder selection.
            // The shader effect should sync to all players and apply to their views.
            if (launchpad == null || !IsMochieFolderEnabled())
            {
                Debug.Log($"[MochieHandler] ApplyMochieMaterial: early return - launchpad null: {launchpad == null}, IsMochieFolderEnabled: {IsMochieFolderEnabled()}");
                return;
            }
            
            if (!materialInitialized || activeMochieMaterial == null)
            {
                Debug.Log("[MochieHandler] ApplyMochieMaterial: material not initialized, calling InitializeMaterialsIfNeeded");
                InitializeMaterialsIfNeeded();
                if (!materialInitialized || activeMochieMaterial == null)
                {
                    Debug.Log("[MochieHandler] ApplyMochieMaterial: still not initialized after init call, returning");
                    return;
                }
            }
            
            // Re-fetch material reference if somehow null (editor assigns the material)
            if (activeMochieMaterial == null && shaderRenderer != null)
            {
                Debug.Log("[MochieHandler] ApplyMochieMaterial: re-fetching activeMochieMaterial from shaderRenderer");
                activeMochieMaterial = shaderRenderer.sharedMaterial;
                if (activeMochieMaterial == null)
                {
                    Debug.Log("[MochieHandler] ApplyMochieMaterial: activeMochieMaterial is still null after re-fetch, returning");
                    return;
                }
            }
            
            // Log shader info for diagnostics
            if (activeMochieMaterial != null && activeMochieMaterial.shader != null)
            {
                Debug.Log($"[MochieHandler] ApplyMochieMaterial: shader name = {activeMochieMaterial.shader.name}");
            }
            
            Debug.Log($"[MochieHandler] ApplyMochieMaterial: calling UpdateMochieShaderProperties - outlineType={outlineType}, sobelFilterOpacity={sobelFilterOpacity}");
            UpdateMochieShaderProperties();
            
            launchpad.ForceMaterialUpdate();
            
            if (Networking.IsOwner(gameObject))
            {
                RequestSerialization();
            }
        }
        
        public void UpdateMochieShaderProperties(bool forceUpdate = false)
        {
            if (!materialInitialized || activeMochieMaterial == null) return;

            NormalizeAdjustmentFields();

            // Note: currentOutlineColor is updated only by ApplySelectedColor(), not here.
            // selectedColorIndex is the "change to" preview color, currentOutlineColor is the applied color.
            
            float auraStrength;
            float outlineThreshold;
            
            switch (outlineStrengthLevel)
            {
                case 0:
                auraStrength = Mathf.Clamp(mochieOutlineAuraLow, MochieAuraStrengthMin, MochieAuraStrengthMax);
                outlineThreshold = Mathf.Clamp(mochieOutlineThresholdLow, MochieOutlineThresholdMin, MochieOutlineThresholdMax);
                break;
                case 2:
                auraStrength = Mathf.Clamp(mochieOutlineAuraHigh, MochieAuraStrengthMin, MochieAuraStrengthMax);
                outlineThreshold = Mathf.Clamp(mochieOutlineThresholdHigh, MochieOutlineThresholdMin, MochieOutlineThresholdMax);
                break;
                default:
                auraStrength = Mathf.Clamp(mochieOutlineAuraNormal, MochieAuraStrengthMin, MochieAuraStrengthMax);
                outlineThreshold = Mathf.Clamp(mochieOutlineThresholdNormal, MochieOutlineThresholdMin, MochieOutlineThresholdMax);
                break;
            }
            
            activeMochieMaterial.SetFloat("_AuraStr", auraStrength);
            activeMochieMaterial.SetFloat("_OutlineThresh", outlineThreshold);
            
            activeMochieMaterial.SetInt("_OutlineType", outlineType);
            activeMochieMaterial.SetFloat("_SobelFilterOpacity", sobelFilterOpacity);
            activeMochieMaterial.SetFloat("_Invert", Mathf.Clamp(invertStrength, MochieEffectStrengthMin, MochieEffectStrengthMax));
            activeMochieMaterial.SetFloat("_Amplitude", _amplitude);
            activeMochieMaterial.SetColor("_OutlineCol", currentOutlineColor);
            activeMochieMaterial.SetFloat("_BlurStr", blurStrength);
            activeMochieMaterial.SetFloat("_DistortionStr", distortionStrength);
            activeMochieMaterial.SetFloat("_Noise", noiseStrength);
            activeMochieMaterial.SetFloat("_ScanLine", scanLineStrength);
            activeMochieMaterial.SetFloat("_DBOpacity", depthBufferOpacity);
            activeMochieMaterial.SetFloat("_NMFOpacity", normalMapOpacity);
            activeMochieMaterial.SetFloat("_Saturation", saturation);
            activeMochieMaterial.SetFloat("_RoundingOpacity", roundingOpacity);
            activeMochieMaterial.SetFloat("_FogSafeOpacity", fogSafeOpacity);
            if (activeMochieMaterial.HasProperty("_Brightness"))
            {
                activeMochieMaterial.SetFloat("_Brightness", _Brightness);
            }
            if (activeMochieMaterial.HasProperty("_Contrast"))
            {
                activeMochieMaterial.SetFloat("_Contrast", _Contrast);
            }
            if (activeMochieMaterial.HasProperty("_HDR"))
            {
                activeMochieMaterial.SetFloat("_HDR", _HDR);
            }
            
            if (supportsAudioLinkFilter)
            {
                SetAudioLinkFloat("_AudioLinkFilteringStrength", _alFilterStrength);
            }
            if (supportsAudioLinkShake)
            {
                SetAudioLinkFloat("_AudioLinkShakeStrength", _alShakeStrength);
            }
            if (supportsAudioLinkBlur)
            {
                SetAudioLinkFloat("_AudioLinkBlurStrength", _alBlurStrength);
            }
            if (supportsAudioLinkDistort)
            {
                SetAudioLinkFloat("_AudioLinkDistortionStrength", _alDistortStrength);
            }
            if (supportsAudioLinkNoise)
            {
                SetAudioLinkFloat("_AudioLinkNoiseStrength", _alNoiseStrength);
            }
            
            if (hasSfxXFeatures)
            {
                if (activeMochieMaterial.HasProperty("_SSTColor"))
                {
                    Color sstColor = activeMochieMaterial.GetColor("_SSTColor");
                    sstColor.a = _overlayIndex >= 0 ? 1f : 0f;
                    activeMochieMaterial.SetColor("_SSTColor", sstColor);
                }
                
                if (activeMochieMaterial.HasProperty("_ScreenTex"))
                {
                    if (_overlayIndex >= 0 && overlayTextures != null && _overlayIndex < overlayTextures.Length)
                    {
                        activeMochieMaterial.SetTexture("_ScreenTex", overlayTextures[_overlayIndex]);
                    }
                    else
                    {
                        activeMochieMaterial.SetTexture("_ScreenTex", null);
                    }
                }
                
                if (activeMochieMaterial.HasProperty("_TPColor"))
                {
                    Color tpColor = activeMochieMaterial.GetColor("_TPColor");
                    tpColor.a = _scanIndex >= 0 ? 1f : 0f;
                    activeMochieMaterial.SetColor("_TPColor", tpColor);
                }
                
                if (activeMochieMaterial.HasProperty("_TPTexture"))
                {
                    if (_scanIndex >= 0 && overlayTextures != null && _scanIndex < overlayTextures.Length)
                    {
                        activeMochieMaterial.SetTexture("_TPTexture", overlayTextures[_scanIndex]);
                    }
                    else
                    {
                        activeMochieMaterial.SetTexture("_TPTexture", null);
                    }
                }
                
                UpdateAudioLinkBands();
                
                if (supportsAudioLinkFog)
                {
                    SetAudioLinkFloat("_AudioLinkFogOpacity", _alFogStrength);
                }
                if (supportsAudioLinkOutline)
                {
                    SetAudioLinkFloat("_AudioLinkOutlineStrength", _alOutlineStrength);
                }
                if (supportsAudioLinkImage)
                {
                    SetAudioLinkFloat("_AudioLinkSSTStrength", _alImageStrength);
                }
                if (supportsAudioLinkTriplanar)
                {
                    SetAudioLinkFloat("_AudioLinkTriplanarOpacity", _alImageStrength);
                }
                if (supportsAudioLinkMisc)
                {
                    SetAudioLinkFloat("_AudioLinkMiscStrength", _alMiscStrength);
                }
            }
            else
            {
                ClearXOnlyMaterialProperties();
            }
            
            UpdateAudioLinkBands();
            
            bool keywordsChanged = ApplyMochieKeywords();
            
            if (forceUpdate || keywordsChanged)
            {
                launchpad.ForceMaterialUpdate();
            }
        }
        
        void SetAudioLinkFloat(string property, float value)
        {
            if (activeMochieMaterial == null || !activeMochieMaterial.HasProperty(property)) return;
            activeMochieMaterial.SetFloat(property, value);
        }
        
        public void UpdateAudioLinkBands()
        {
            if (activeMochieMaterial == null) return;
            
            string[] properties = hasSfxXFeatures
            ? new string[] {
                "_AudioLinkFilteringBand",
                "_AudioLinkShakeBand",
                "_AudioLinkDistortionBand",
                "_AudioLinkBlurBand",
                "_AudioLinkNoiseBand",
                "_AudioLinkZoomBand",
                "_AudioLinkSSTBand",
                "_AudioLinkFogBand",
                "_AudioLinkTriplanarBand",
                "_AudioLinkOutlineBand",
                "_AudioLinkMiscBand"
            }
            : new string[] {
                "_AudioLinkFilteringBand",
                "_AudioLinkShakeBand",
                "_AudioLinkDistortionBand",
                "_AudioLinkBlurBand",
                "_AudioLinkNoiseBand"
            };
            
            foreach(string prop in properties)
            {
                if (activeMochieMaterial.HasProperty(prop))
                {
                    activeMochieMaterial.SetInt(prop, _audioLinkBand);
                }
            }
        }
        
        void ClearXOnlyMaterialProperties()
        {
            if (activeMochieMaterial == null) return;
            
            if (supportsOverlaySelection && activeMochieMaterial.HasProperty("_SSTColor"))
            {
                Color sstColor = activeMochieMaterial.GetColor("_SSTColor");
                sstColor.a = 0f;
                activeMochieMaterial.SetColor("_SSTColor", sstColor);
            }
            
            if (supportsOverlaySelection && activeMochieMaterial.HasProperty("_ScreenTex"))
            {
                activeMochieMaterial.SetTexture("_ScreenTex", null);
            }
            
            if (supportsScanSelection && activeMochieMaterial.HasProperty("_TPColor"))
            {
                Color tpColor = activeMochieMaterial.GetColor("_TPColor");
                tpColor.a = 0f;
                activeMochieMaterial.SetColor("_TPColor", tpColor);
            }
            
            if (supportsScanSelection && activeMochieMaterial.HasProperty("_TPTexture"))
            {
                activeMochieMaterial.SetTexture("_TPTexture", null);
            }
            
            if (supportsAudioLinkFog)
            {
                SetAudioLinkFloat("_AudioLinkFogOpacity", 0f);
            }
            if (supportsAudioLinkOutline)
            {
                SetAudioLinkFloat("_AudioLinkOutlineStrength", 0f);
            }
            if (supportsAudioLinkImage)
            {
                SetAudioLinkFloat("_AudioLinkSSTStrength", 0f);
            }
            if (supportsAudioLinkTriplanar)
            {
                SetAudioLinkFloat("_AudioLinkTriplanarOpacity", 0f);
            }
            if (supportsAudioLinkMisc)
            {
                SetAudioLinkFloat("_AudioLinkMiscStrength", 0f);
            }
        }
        
        public float GetAudioLinkStrength(int controlIndex)
        {
            switch (controlIndex)
            {
                case AudioLinkOutlineIndex:
                return supportsAudioLinkOutline ? _alOutlineStrength : 0f;
                case AudioLinkFilterIndex:
                return supportsAudioLinkFilter ? _alFilterStrength : 0f;
                case AudioLinkShakeIndex:
                return supportsAudioLinkShake ? _alShakeStrength : 0f;
                case AudioLinkBlurIndex:
                return supportsAudioLinkBlur ? _alBlurStrength : 0f;
                case AudioLinkDistortIndex:
                return supportsAudioLinkDistort ? _alDistortStrength : 0f;
                case AudioLinkNoiseIndex:
                return supportsAudioLinkNoise ? _alNoiseStrength : 0f;
                case AudioLinkFogIndex:
                return supportsAudioLinkFog ? _alFogStrength : 0f;
                case AudioLinkImageIndex:
                return (supportsAudioLinkImage || supportsAudioLinkTriplanar) ? _alImageStrength : 0f;
                case AudioLinkMiscIndex:
                return supportsAudioLinkMisc ? _alMiscStrength : 0f;
                default:
                return 0f;
            }
        }
        
        public bool IsAudioLinkStrengthSupported(int controlIndex)
        {
            switch (controlIndex)
            {
                case AudioLinkOutlineIndex:
                return supportsAudioLinkOutline;
                case AudioLinkFilterIndex:
                return supportsAudioLinkFilter;
                case AudioLinkShakeIndex:
                return supportsAudioLinkShake;
                case AudioLinkBlurIndex:
                return supportsAudioLinkBlur;
                case AudioLinkDistortIndex:
                return supportsAudioLinkDistort;
                case AudioLinkNoiseIndex:
                return supportsAudioLinkNoise;
                case AudioLinkFogIndex:
                return supportsAudioLinkFog;
                case AudioLinkImageIndex:
                return supportsAudioLinkImage || supportsAudioLinkTriplanar;
                case AudioLinkMiscIndex:
                return supportsAudioLinkMisc;
                default:
                return false;
            }
        }
        
        float GetNextAudioLinkStrengthValue(float currentValue)
        {
            int idx = Mathf.Clamp((int)(Mathf.Clamp01(currentValue) * 4f), 0, AudioLinkStrengthSteps.Length - 1);
            int nextIndex = (idx + 1) % AudioLinkStrengthSteps.Length;
            return AudioLinkStrengthSteps[nextIndex];
        }
        
        void SetAudioLinkStrength(int controlIndex, float value)
        {
            switch (controlIndex)
            {
                case AudioLinkOutlineIndex:
                if (supportsAudioLinkOutline) _alOutlineStrength = value;
                break;
                case AudioLinkFilterIndex:
                if (supportsAudioLinkFilter) _alFilterStrength = value;
                break;
                case AudioLinkShakeIndex:
                if (supportsAudioLinkShake) _alShakeStrength = value;
                break;
                case AudioLinkBlurIndex:
                if (supportsAudioLinkBlur) _alBlurStrength = value;
                break;
                case AudioLinkDistortIndex:
                if (supportsAudioLinkDistort) _alDistortStrength = value;
                break;
                case AudioLinkNoiseIndex:
                if (supportsAudioLinkNoise) _alNoiseStrength = value;
                break;
                case AudioLinkFogIndex:
                if (supportsAudioLinkFog) _alFogStrength = value;
                break;
                case AudioLinkImageIndex:
                if (supportsAudioLinkImage || supportsAudioLinkTriplanar) _alImageStrength = value;
                break;
                case AudioLinkMiscIndex:
                if (supportsAudioLinkMisc) _alMiscStrength = value;
                break;
            }
        }
        
        public void HandleAudioLinkStrengthButton(int[] controlMap, int buttonIndex)
        {
            if (controlMap == null || buttonIndex < 0 || buttonIndex >= controlMap.Length) return;
            
            int controlIndex = controlMap[buttonIndex];
            if (controlIndex < 0 || !IsAudioLinkStrengthSupported(controlIndex)) return;
            
            float currentValue = GetAudioLinkStrength(controlIndex);
            float nextValue = GetNextAudioLinkStrengthValue(currentValue);
            SetAudioLinkStrength(controlIndex, nextValue);
        }
        
        /// <summary>
        /// Updates the local currentOutlineColor from the synced appliedColorIndex.
        /// Called during deserialization to sync the currently applied color.
        /// </summary>
        private void UpdateCurrentOutlineColorFromIndex()
        {
            if (outlineColors != null && outlineColors.Length > 0)
            {
                int clampedIndex = Mathf.Clamp(appliedColorIndex, 0, outlineColors.Length - 1);
                currentOutlineColor = outlineColors[clampedIndex];
            }
        }
        
        public void ApplySelectedColor()
        {
            if (outlineColors == null || outlineColors.Length == 0) return;

            // Apply the selected preview color as the current color
            selectedColorIndex = Mathf.Clamp(selectedColorIndex, 0, outlineColors.Length - 1);
            appliedColorIndex = selectedColorIndex;  // Sync the applied color
            currentOutlineColor = outlineColors[appliedColorIndex];

            if (!EnsureLocalOwnership())
            {
                Debug.LogWarning("[MochieHandler] ApplySelectedColor aborted - failed to secure ownership for color sync.");
                return;
            }
            RequestSerialization();
            UpdateMochieShaderProperties(true);
            // UpdateDisplay is called by EnigmaLaunchpad.HandleItemSelect after OnSelect returns
        }
        
        public void CycleColorSelection()
        {
            if (outlineColors == null || outlineColors.Length == 0) return;
            
            // Only update the preview color, not the applied color
            selectedColorIndex = (selectedColorIndex + 1) % outlineColors.Length;
            
            // Flash button 8 using ButtonHandler's centralized system
            if (launchpad != null)
            {
                launchpad.FlashButtonAtIndex(8);
            }
            // UpdateDisplay is called by EnigmaLaunchpad.HandleItemSelect after OnSelect returns
        }
        
        // Check which shader properties are available on the material
        // This is based on actual material capabilities, not shader name validation
        public void UpdateFeatureSupport(Material mat)
        {
            if (mat == null)
            {
                ResetMochieFeatureFlags();
                return;
            }
            
            // Check which specific features the material supports
            supportsOverlaySelection = useSfxXLayout && (MaterialSupportsProperty(mat, "_ScreenTex") || MaterialSupportsProperty(mat, "_SSTColor"));
            supportsScanSelection = useSfxXLayout && (MaterialSupportsProperty(mat, "_TPTexture") || MaterialSupportsProperty(mat, "_TPColor"));
            
            supportsAudioLinkFilter = MaterialSupportsProperty(mat, "_AudioLinkFilteringStrength");
            supportsAudioLinkShake = MaterialSupportsProperty(mat, "_AudioLinkShakeStrength");
            supportsAudioLinkBlur = MaterialSupportsProperty(mat, "_AudioLinkBlurStrength");
            supportsAudioLinkDistort = MaterialSupportsProperty(mat, "_AudioLinkDistortionStrength");
            supportsAudioLinkNoise = MaterialSupportsProperty(mat, "_AudioLinkNoiseStrength");
            supportsAudioLinkFog = MaterialSupportsProperty(mat, "_AudioLinkFogOpacity");
            supportsAudioLinkOutline = MaterialSupportsProperty(mat, "_AudioLinkOutlineStrength");
            supportsAudioLinkImage = MaterialSupportsProperty(mat, "_AudioLinkSSTStrength");
            supportsAudioLinkTriplanar = MaterialSupportsProperty(mat, "_AudioLinkTriplanarOpacity");
            supportsAudioLinkMisc = MaterialSupportsProperty(mat, "_AudioLinkMiscStrength");
            
            if (!supportsOverlaySelection) _overlayIndex = -1;
            if (!supportsScanSelection) _scanIndex = -1;
        }
        
        public void ApplyAllMochieStates()
        {
            ApplyMochieState();
        }
        
        public void EnforceOwnership()
        {
        }
        
        private void UpdatePage(int direction)
        {
            string localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Unknown";
            
            if (launchpad == null)
            {
                Debug.LogWarning($"[MochieHandler] UpdatePage ABORT - launchpad is null");
                return;
            }
            
            int totalPages = GetPageCount();
            if (totalPages <= 0)
            {
                Debug.LogWarning($"[MochieHandler] UpdatePage ABORT - totalPages is {totalPages}");
                return;
            }
            
            int oldPage = mochiePage;
            mochiePage = (mochiePage + direction + totalPages) % totalPages;
            Debug.Log($"[MochieHandler] UpdatePage - Player: {localPlayerName}, Direction: {direction}, OldPage: {oldPage}, NewPage: {mochiePage}, TotalPages: {totalPages}");
        }
        
        private bool IsHandlerActive()
        {
            if (launchpad == null)
            {
                return false;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Mochie)
            {
                return false;
            }
            
            if (!materialInitialized)
            {
                return false;
            }
            
            int currentFolder = launchpad.GetDefaultFolderIndex();
            return currentFolder >= 0 && launchpad.GetFolderTypeForIndex(currentFolder) == ToggleFolderType.Mochie;
        }
        
        public int GetPageCount()
        {
            // Editor validates Mochie shaders at build time, so no runtime checks are needed.
            // useSfxXLayout is set by the editor and serialized with the scene/prefab,
            // so it's consistent across all clients.
            return useSfxXLayout ? 6 : 4;
        }
        
        private bool TryGetDisplayPage(out int displayPage, out int totalPages)
        {
            displayPage = 0;
            totalPages = GetPageCount();
            if (totalPages <= 0)
            {
                return false;
            }
            
            int clampedPage = Mathf.Clamp(mochiePage, 0, totalPages - 1);
            if (clampedPage != mochiePage && Networking.IsOwner(gameObject))
            {
                Debug.LogWarning($"[MochieHandler] TryGetDisplayPage - Owner clamping mochiePage from {mochiePage} to {clampedPage} before display.");
                mochiePage = clampedPage;
            }

            displayPage = hasSfxXFeatures ? clampedPage : clampedPage + 1;
            return true;
        }
        
        private string GetGuidanceMessage()
        {
            if (launchpad == null)
            {
                return string.Empty;
            }
            
            if (!HasAnyShaderDetected())
            {
                return MochieScreenFxMissingMessage;
            }
            
            if (!hasSfxXFeatures)
            {
                return MochieScreenFxUpgradeMessage;
            }
            
            return string.Empty;
        }
        
        private string FormatMochiePageLabel(int pageIndex, int totalPages)
        {
            if (totalPages <= 0)
            {
                return string.Empty;
            }
            
            pageIndex = Mathf.Clamp(pageIndex, 0, totalPages - 1);
            return $"{pageIndex + 1}/{totalPages}";
        }
        
        private string GetOutlinePageLabel(int localIndex, string pageLabel)
        {
            string[] colorNames = GetOutlineColorNamesForDisplay();
            int colorCount = colorNames.Length;
            int currentColorIndex = colorCount > 0 ? Mathf.Clamp(GetColorIndex(currentOutlineColor), 0, colorCount - 1) : 0;
            int displaySelectedIndex = colorCount > 0 ? Mathf.Clamp(selectedColorIndex, 0, colorCount - 1) : 0;
            string currentColorName = colorCount > 0 ? colorNames[currentColorIndex] : "None";
            string nextColorName = colorCount > 0 ? colorNames[displaySelectedIndex] : "None";
            
            switch (localIndex)
            {
                case 0: return "Aura\nOutline";
                case 1: return "Sobel\nOutline";
                case 2: return "Sobel\nFilter";
                case 3: return "Low";
                case 4: return "Normal";
                case 5: return "High";
                case 6: return $"Current\nColor:\n{currentColorName}";
                case 7: return $"Change\nto:\n{nextColorName}";
                case 8: return "Next\nColor";
                case 9: return pageLabel;
                default: return string.Empty;
            }
        }
        
        private string GetCoreEffectLabel(int localIndex, string pageLabel)
        {
            switch (localIndex)
            {
                case 0: return "Invert";
                case 1: return "Invert+";
                case 2: return "Shake";
                case 3: return "Pixel\nBlur";
                case 4: return "Distort";
                case 5: return "Noise";
                case 6: return "Scan\nLines";
                case 7: return hasSfxXFeatures ? "Depth\nBuffer" : string.Empty;
                case 8: return hasSfxXFeatures ? "Normal\nMap" : string.Empty;
                case 9: return pageLabel;
                default: return string.Empty;
            }
        }
        
        private string GetAdjustmentLabel(int localIndex, string pageLabel)
        {
            switch (localIndex)
            {
                case 0: return "-";
                case 1: return $"Satur\n{saturation:F1}";
                case 2: return "+";
                case 3: return hasSfxXFeatures ? (IsRoundingActive() ? "-" : string.Empty) : (_HDR > -2f ? "-" : string.Empty);
                case 4: return hasSfxXFeatures ? $"Round\n{roundingOpacity:F1}" : $"HDR\n{_HDR:F1}";
                case 5: return hasSfxXFeatures ? (roundingOpacity < 1f ? "+" : string.Empty) : (_HDR < 2f ? "+" : string.Empty);
                case 6: return HasAdjustmentActivation(fogSafeOpacity) ? "-" : string.Empty;
                case 7: return $"Fog\n{(fogSafeOpacity * 2):F1}";
                case 8: return fogSafeOpacity < 1f ? "+" : string.Empty;
                case 9: return pageLabel;
                default: return string.Empty;
            }
        }
        
        private string GetImageControlLabel(int localIndex, string pageLabel)
        {
            switch (localIndex)
            {
                case 0: return _Brightness > -2f ? "-" : string.Empty;
                case 1: return $"Bright\n{_Brightness:F1}";
                case 2: return _Brightness < 4f ? "+" : string.Empty;
                case 3: return _Contrast > 0.5f ? "-" : string.Empty;
                case 4: return $"Contr\n{_Contrast:F2}";
                case 5: return _Contrast < 1.5f ? "+" : string.Empty;
                case 6: return hasSfxXFeatures ? (_HDR > -2f ? "-" : string.Empty) : string.Empty;
                case 7: return hasSfxXFeatures ? $"HDR\n{_HDR:F1}" : "Upgrade";
                case 8: return hasSfxXFeatures ? (_HDR < 2f ? "+" : string.Empty) : "for";
                case 9: return pageLabel;
                default: return string.Empty;
            }
        }
        
        private string GetAudioLinkSelectionLabel(int localIndex, string pageLabel)
        {
            int overlayCount = overlayNames != null ? overlayNames.Length : 0;
            if (localIndex < 3)
            {
                if (!supportsOverlaySelection)
                {
                    return string.Empty;
                }
                
                if (localIndex < overlayCount)
                {
                    return "Overlay\n" + overlayNames[localIndex];
                }
                
                return "No\nImage";
            }
            
            if (localIndex < 6)
            {
                if (!supportsScanSelection)
                {
                    return string.Empty;
                }
                
                int overlayIdx = localIndex - 3;
                if (overlayIdx < overlayCount)
                {
                    return "Scan\n" + overlayNames[overlayIdx];
                }
                
                return "No\nImage";
            }
            
            switch (localIndex)
            {
                case 6: return "AL\nBass";
                case 7: return _audioLinkBand == 2 ? "AL\nUpper\nMids" : "AL\nLow\nMids";
                case 8: return "AL\nTreble";
                case 9: return pageLabel;
                default: return string.Empty;
            }
        }
        
        private string GetStandardAudioLinkLabel(int localIndex, string pageLabel)
        {
            if (localIndex == 0) return "AL\nBass";
            if (localIndex == 1) return _audioLinkBand == 2 ? "AL\nUpper\nMids" : "AL\nLow\nMids";
            if (localIndex == 2) return "AL\nTreble";
            if (localIndex >= StandardAudioLinkStrengthStartIndex && localIndex < 9)
            {
                int strengthIndex = localIndex - StandardAudioLinkStrengthStartIndex;
                return GetAudioLinkStrengthLabel(localIndex, pageLabel, StandardAudioLinkStrengthMap, StandardAudioLinkStrengthStartIndex);
            }
            if (localIndex == 9) return pageLabel;
            return string.Empty;
        }
        
        private string GetAudioLinkStrengthLabel(int localIndex, string pageLabel, int[] controlMap, int startIndex)
        {
            if (localIndex == 9)
            {
                return pageLabel;
            }
            
            if (controlMap == null)
            {
                return string.Empty;
            }
            
            if (localIndex < startIndex || localIndex >= 9)
            {
                return string.Empty;
            }
            
            int mapIndex = localIndex - startIndex;
            if (mapIndex < 0 || mapIndex >= controlMap.Length)
            {
                return string.Empty;
            }
            
            int controlIndex = controlMap[mapIndex];
            if (controlIndex < 0 || !IsAudioLinkStrengthSupported(controlIndex))
            {
                return string.Empty;
            }
            
            string label = GetAudioLinkLabel(controlIndex);
            float value = GetAudioLinkStrength(controlIndex);
            return $"AL\n{label}\n{value:F2}";
        }
        
        private string GetAudioLinkLabel(int controlIndex)
        {
            switch (controlIndex)
            {
                case AudioLinkOutlineIndex: return "Outline";
                case AudioLinkFilterIndex: return "Filter";
                case AudioLinkShakeIndex: return "Shake";
                case AudioLinkBlurIndex: return "Blur";
                case AudioLinkDistortIndex: return "Distort";
                case AudioLinkNoiseIndex: return "Noise";
                case AudioLinkFogIndex: return "Fog";
                case AudioLinkImageIndex: return (supportsAudioLinkTriplanar && !supportsAudioLinkImage) ? "Triplanar" : "Image";
                case AudioLinkMiscIndex: return "Misc";
                default: return string.Empty;
            }
        }
        
        private Color GetOutlineButtonColor(int localIndex)
        {
            switch (localIndex)
            {
                case 0: return outlineType == 2 ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 1: return outlineType == 1 ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 2: return sobelFilterOpacity > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 3: return outlineStrengthLevel == 0 ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 4: return outlineStrengthLevel == 1 ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 5: return outlineStrengthLevel == 2 ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 6: return currentOutlineColor;
                case 7:
                if (outlineColors != null && outlineColors.Length > 0)
                {
                    return outlineColors[Mathf.Clamp(selectedColorIndex, 0, outlineColors.Length - 1)];
                }
                return launchpad.GetInactiveColor();
                default:
                return launchpad.GetInactiveColor();
            }
        }
        
        private Color GetCoreEffectColor(int localIndex)
        {
            switch (localIndex)
            {
                case 0: return IsInvertActive() ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 1: return IsInvertPlusActive() ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 2: return Mathf.Approximately(_amplitude, GetClampedMochieShakeAmplitude()) ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 3: return blurStrength > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 4: return distortionStrength > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 5: return noiseStrength > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 6: return scanLineStrength > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
                case 7: return hasSfxXFeatures ? (depthBufferOpacity > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor()) : launchpad.GetInactiveColor();
                case 8: return hasSfxXFeatures ? (normalMapOpacity > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor()) : launchpad.GetInactiveColor();
                default: return launchpad.GetInactiveColor();
            }
        }
        
        private Color GetAdjustmentColor(int localIndex)
        {
            switch (localIndex)
            {
                case 1:
                if (saturation > 1f) return positiveColor;
                if (saturation < 1f) return negativeColor;
                return launchpad.GetInactiveColor();
                case 4:
                if (hasSfxXFeatures)
                {
                    return IsRoundingActive() ? positiveColor : launchpad.GetInactiveColor();
                }
                else
                {
                    if (HasAdjustmentActivation(_HDR))
                    {
                        return _HDR > 0f ? positiveColor : negativeColor;
                    }
                    return launchpad.GetInactiveColor();
                }
                case 7:
                return HasAdjustmentActivation(fogSafeOpacity) ? positiveColor : launchpad.GetInactiveColor();
                default:
                return launchpad.GetInactiveColor();
            }
        }
        
        private Color GetImageControlColor(int localIndex)
        {
            switch (localIndex)
            {
                case 1:
                if (!Mathf.Approximately(_Brightness, 1f))
                {
                    return _Brightness > 1f ? positiveColor : negativeColor;
                }
                return launchpad.GetInactiveColor();
                case 4:
                if (!Mathf.Approximately(_Contrast, 1f))
                {
                    return _Contrast > 1f ? positiveColor : negativeColor;
                }
                return launchpad.GetInactiveColor();
                case 7:
                if (hasSfxXFeatures)
                {
                    if (HasAdjustmentActivation(_HDR))
                    {
                        return _HDR > 0f ? positiveColor : negativeColor;
                    }
                    return launchpad.GetInactiveColor();
                }
                return launchpad.GetInactiveColor();
                default:
                return launchpad.GetInactiveColor();
            }
        }
        
        private Color GetAudioLinkSelectionColor(int localIndex)
        {
            int overlayCount = overlayNames != null ? overlayNames.Length : 0;
            if (localIndex < 3)
            {
                if (!supportsOverlaySelection)
                {
                    return launchpad.GetInactiveColor();
                }
                
                bool hasOverlay = overlayTextures != null && localIndex < overlayTextures.Length && localIndex < overlayCount;
                bool active = hasOverlay && localIndex == _overlayIndex;
                if (!hasOverlay)
                {
                    return launchpad.GetInactiveColor();
                }
                return active ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
            }
            
            if (localIndex < 6)
            {
                if (!supportsScanSelection)
                {
                    return launchpad.GetInactiveColor();
                }
                
                int overlayIdx = localIndex - 3;
                bool hasOverlay = overlayTextures != null && overlayIdx < overlayTextures.Length && overlayIdx < overlayCount;
                bool active = hasOverlay && overlayIdx == _scanIndex;
                if (!hasOverlay)
                {
                    return launchpad.GetInactiveColor();
                }
                return active ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
            }
            
            if (localIndex == 6)
            {
                return _audioLinkBand == 0 ? bassColor * 3f : launchpad.GetInactiveColor();
            }
            
            if (localIndex == 7)
            {
                Color midColor = launchpad.GetInactiveColor();
                if (_audioLinkBand == 1) midColor = lowMidColor * 3f;
                else if (_audioLinkBand == 2) midColor = upperMidColor * 3f;
                return midColor;
            }
            
            if (localIndex == 8)
            {
                return _audioLinkBand == 3 ? trebleColor * 3f : launchpad.GetInactiveColor();
            }
            
            if (localIndex == 9)
            {
                return launchpad.GetInactiveColor();
            }
            
            return launchpad.GetInactiveColor();
        }
        
        private Color GetStandardAudioLinkColor(int localIndex)
        {
            if (localIndex == 0) return _audioLinkBand == 0 ? bassColor * 3f : launchpad.GetInactiveColor();
            
            if (localIndex == 1)
            {
                Color midColor = launchpad.GetInactiveColor();
                if (_audioLinkBand == 1) midColor = lowMidColor * 3f;
                else if (_audioLinkBand == 2) midColor = upperMidColor * 3f;
                return midColor;
            }
            
            if (localIndex == 2) return _audioLinkBand == 3 ? trebleColor * 3f : launchpad.GetInactiveColor();
            
            if (localIndex >= StandardAudioLinkStrengthStartIndex && localIndex < 9)
            {
                return GetAudioLinkStrengthColor(localIndex, StandardAudioLinkStrengthMap, StandardAudioLinkStrengthStartIndex);
            }
            
            if (localIndex == 9)
            {
                return launchpad.GetInactiveColor();
            }
            
            return launchpad.GetInactiveColor();
        }
        
        private Color GetAudioLinkStrengthColor(int localIndex, int[] controlMap, int startIndex)
        {
            if (controlMap == null)
            {
                return launchpad.GetInactiveColor();
            }
            
            if (localIndex == 9)
            {
                return launchpad.GetInactiveColor();
            }
            
            if (localIndex < startIndex || localIndex >= 9)
            {
                return launchpad.GetInactiveColor();
            }
            
            int mapIndex = localIndex - startIndex;
            if (mapIndex < 0 || mapIndex >= controlMap.Length)
            {
                return launchpad.GetInactiveColor();
            }
            
            int controlIndex = controlMap[mapIndex];
            if (controlIndex < 0 || !IsAudioLinkStrengthSupported(controlIndex))
            {
                return launchpad.GetInactiveColor();
            }
            
            float value = GetAudioLinkStrength(controlIndex);
            return value > 0f ? launchpad.GetActiveColor() : launchpad.GetInactiveColor();
        }
        
        private string[] GetOutlineColorNamesForDisplay()
        {
            if (outlineColors == null || outlineColors.Length == 0)
            {
                return new string[0];
            }
            
            if (outlineColorNames != null && outlineColorNames.Length == outlineColors.Length)
            {
                bool hasMissing = false;
                for (int i = 0; i < outlineColorNames.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(outlineColorNames[i]))
                    {
                        hasMissing = true;
                        break;
                    }
                }
                
                if (!hasMissing)
                {
                    return outlineColorNames;
                }
            }
            
            // Fallback to generic names if editor didn't populate them
            string[] fallback = new string[outlineColors.Length];
            for (int i = 0; i < fallback.Length; i++)
            {
                fallback[i] = $"Color {i + 1}";
            }
            
            return fallback;
        }
        
        private int GetColorIndex(Color targetColor)
        {
            if (outlineColors == null || outlineColors.Length == 0)
            {
                return 0;
            }
            
            for (int i = 0; i < outlineColors.Length; i++)
            {
                if (ColorsApproximatelyEqual(outlineColors[i], targetColor))
                {
                    return i;
                }
            }
            
            return 0;
        }
        
        #region Preset Support
        
        // ============================================================================
        // SIMPLIFIED PRESET API - returns step indices instead of raw values
        // ============================================================================
        
        /// <summary>Gets the current outline type (0=None, 1=Sobel, 2=Aura)</summary>
        public int GetOutlineType() => outlineType;
        
        /// <summary>Gets the current outline strength level (0=Low, 1=Normal, 2=High)</summary>
        public int GetOutlineStrengthLevel() => outlineStrengthLevel;
        
        /// <summary>Gets the current scan index (-1=none, 0+ = specific scan)</summary>
        public int GetScanIndex() => _scanIndex;
        
        /// <summary>Gets the current audiolink band</summary>
        public int GetAudioLinkBand() => _audioLinkBand;
        
        /// <summary>Gets the applied color index</summary>
        public int GetAppliedColorIndex() => appliedColorIndex;
        
        /// <summary>Gets the current overlay index (-1=none, 0+ = specific overlay)</summary>
        public int GetOverlayIndex() => _overlayIndex;
        
        /// <summary>
        /// Gets a specific +/- controlled value as a step index.
        /// stepIndex: 0=Saturation, 1=Rounding, 2=FogSafe, 3=Brightness, 4=HDR, 5=Contrast
        /// </summary>
        public int GetEffectStep(int stepIndex)
        {
            switch (stepIndex)
            {
                case 0: return Mathf.RoundToInt(saturation * 10f);  // 0-10 for 0.0-1.0
                case 1: return Mathf.RoundToInt(roundingOpacity * 10f);
                case 2: return Mathf.RoundToInt(fogSafeOpacity * 10f);
                case 3: return Mathf.RoundToInt((_Brightness + 2f) * 2f);  // -2 to 4 range → 0-12 steps
                case 4: return Mathf.RoundToInt((_HDR + 2f) * 2f);  // -2 to 2 range → 0-8 steps
                case 5: return Mathf.RoundToInt((_Contrast - 0.5f) * 20f);  // 0.5-1.5 → 0-20
                default: return 0;
            }
        }
        
        /// <summary>
        /// Sets a +/- controlled value from a step index.
        /// </summary>
        public void SetEffectFromStep(int stepIndex, int step)
        {
            switch (stepIndex)
            {
                case 0: saturation = step * 0.1f; break;
                case 1: roundingOpacity = step * 0.1f; break;
                case 2: fogSafeOpacity = step * 0.1f; break;
                case 3: _Brightness = (step * 0.5f) - 2f; break;
                case 4: _HDR = (step * 0.5f) - 2f; break;
                case 5: _Contrast = (step * 0.05f) + 0.5f; break;
            }
        }
        
        /// <summary>
        /// Gets an AudioLink strength as a step index (0-4 for the 5 steps).
        /// alIndex: 0=Filter, 1=Shake, 2=Blur, 3=Distort, 4=Noise, 5=Fog, 6=Outline, 7=Image, 8=Misc
        /// </summary>
        public int GetAudioLinkStep(int alIndex)
        {
            float value = 0f;
            switch (alIndex)
            {
                case 0: value = _alFilterStrength; break;
                case 1: value = _alShakeStrength; break;
                case 2: value = _alBlurStrength; break;
                case 3: value = _alDistortStrength; break;
                case 4: value = _alNoiseStrength; break;
                case 5: value = _alFogStrength; break;
                case 6: value = _alOutlineStrength; break;
                case 7: value = _alImageStrength; break;
                case 8: value = _alMiscStrength; break;
            }
            // AudioLink uses 5 steps: 0, 0.25, 0.5, 0.75, 1.0
            return Mathf.RoundToInt(value * 4f);
        }
        
        /// <summary>
        /// Sets an AudioLink strength from a step index (0-4).
        /// </summary>
        public void SetAudioLinkFromStep(int alIndex, int step)
        {
            float value = Mathf.Clamp01(step * 0.25f);
            switch (alIndex)
            {
                case 0: _alFilterStrength = value; break;
                case 1: _alShakeStrength = value; break;
                case 2: _alBlurStrength = value; break;
                case 3: _alDistortStrength = value; break;
                case 4: _alNoiseStrength = value; break;
                case 5: _alFogStrength = value; break;
                case 6: _alOutlineStrength = value; break;
                case 7: _alImageStrength = value; break;
                case 8: _alMiscStrength = value; break;
            }
        }
        
        /// <summary>Sets the outline type (0=None, 1=Sobel, 2=Aura)</summary>
        public void SetOutlineType(int type) => outlineType = type;
        
        /// <summary>Sets the outline strength level (0=Low, 1=Normal, 2=High)</summary>
        public void SetOutlineStrengthLevel(int level) => outlineStrengthLevel = level;
        
        /// <summary>Sets the scan index (-1=none, 0+ = specific scan)</summary>
        public void SetScanIndex(int index) => _scanIndex = index;
        
        /// <summary>Sets the audiolink band</summary>
        public void SetAudioLinkBand(int band) => _audioLinkBand = band;
        
        /// <summary>Sets the applied color index</summary>
        public void SetAppliedColorIndex(int index) => appliedColorIndex = index;
        
        /// <summary>Sets the overlay index (-1=none, 0+ = specific overlay)</summary>
        public void SetOverlayIndex(int index) => _overlayIndex = index;
        
        /// <summary>
        /// Captures the current Mochie state into preset snapshot arrays.
        /// Used by PresetHandler when saving presets.
        /// </summary>
        public bool CapturePresetSnapshot(
            int presetIndex,
            int[] outlineTypeArr,
            float[] sobelOpacityArr,
            int[] outlineStrengthArr,
            float[] currentSobelArr,
            float[] invertArr,
            float[] amplitudeArr,
            float[] blurArr,
            float[] distortionArr,
            float[] noiseArr,
            float[] scanlineArr,
            float[] depthBufferArr,
            float[] normalMapArr,
            float[] saturationArr,
            float[] roundingArr,
            float[] fogSafeArr,
            float[] brightnessArr,
            float[] hdrArr,
            float[] contrastArr,
            int[] scanIndexArr,
            int[] audioBandArr,
            float[] alFilterArr,
            float[] alShakeArr,
            float[] alBlurArr,
            float[] alDistortArr,
            float[] alNoiseArr,
            float[] alFogArr,
            float[] alOutlineArr,
            float[] alImageArr,
            float[] alMiscArr,
            int[] selectedColorArr,
            Color[] outlineColorArr,
            int[] overlayArr)
        {
            if (presetIndex < 0 || outlineTypeArr == null || presetIndex >= outlineTypeArr.Length)
            {
                return false;
            }
            
            outlineTypeArr[presetIndex] = outlineType;
            sobelOpacityArr[presetIndex] = sobelFilterOpacity;
            outlineStrengthArr[presetIndex] = outlineStrengthLevel;
            currentSobelArr[presetIndex] = syncedFloats[SYNC_FLOAT_CURRENT_SOBEL];
            invertArr[presetIndex] = invertStrength;
            amplitudeArr[presetIndex] = syncedFloats[SYNC_FLOAT_AMPLITUDE];
            blurArr[presetIndex] = blurStrength;
            distortionArr[presetIndex] = syncedFloats[SYNC_FLOAT_DISTORTION];
            noiseArr[presetIndex] = syncedFloats[SYNC_FLOAT_NOISE];
            scanlineArr[presetIndex] = syncedFloats[SYNC_FLOAT_SCANLINE];
            depthBufferArr[presetIndex] = syncedFloats[SYNC_FLOAT_DEPTH_BUFFER];
            normalMapArr[presetIndex] = syncedFloats[SYNC_FLOAT_NORMAL_MAP];
            saturationArr[presetIndex] = syncedFloats[SYNC_FLOAT_SATURATION];
            roundingArr[presetIndex] = syncedFloats[SYNC_FLOAT_ROUNDING];
            fogSafeArr[presetIndex] = syncedFloats[SYNC_FLOAT_FOG_SAFE];
            brightnessArr[presetIndex] = syncedFloats[SYNC_FLOAT_BRIGHTNESS];
            hdrArr[presetIndex] = syncedFloats[SYNC_FLOAT_HDR];
            contrastArr[presetIndex] = syncedFloats[SYNC_FLOAT_CONTRAST];
            scanIndexArr[presetIndex] = syncedInts[SYNC_INT_SCAN_INDEX];
            audioBandArr[presetIndex] = syncedInts[SYNC_INT_AUDIOLINK_BAND];
            alFilterArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_FILTER];
            alShakeArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_SHAKE];
            alBlurArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_BLUR];
            alDistortArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_DISTORT];
            alNoiseArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_NOISE];
            alFogArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_FOG];
            alOutlineArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_OUTLINE];
            alImageArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_IMAGE];
            alMiscArr[presetIndex] = syncedFloats[SYNC_FLOAT_AL_MISC];
            selectedColorArr[presetIndex] = syncedInts[SYNC_INT_COLOR_INDEX];
            outlineColorArr[presetIndex] = currentOutlineColor;
            overlayArr[presetIndex] = syncedInts[SYNC_INT_OVERLAY_INDEX];
            
            return true;
        }
        
        /// <summary>
        /// Applies a preset snapshot to the current Mochie state.
        /// Used by PresetHandler when loading presets.
        /// </summary>
        public bool ApplyPresetSnapshot(
            int presetIndex,
            int[] outlineTypeArr,
            float[] sobelOpacityArr,
            int[] outlineStrengthArr,
            float[] currentSobelArr,
            float[] invertArr,
            float[] amplitudeArr,
            float[] blurArr,
            float[] distortionArr,
            float[] noiseArr,
            float[] scanlineArr,
            float[] depthBufferArr,
            float[] normalMapArr,
            float[] saturationArr,
            float[] roundingArr,
            float[] fogSafeArr,
            float[] brightnessArr,
            float[] hdrArr,
            float[] contrastArr,
            int[] scanIndexArr,
            int[] audioBandArr,
            float[] alFilterArr,
            float[] alShakeArr,
            float[] alBlurArr,
            float[] alDistortArr,
            float[] alNoiseArr,
            float[] alFogArr,
            float[] alOutlineArr,
            float[] alImageArr,
            float[] alMiscArr,
            int[] selectedColorArr,
            Color[] outlineColorArr,
            int[] overlayArr)
        {
            if (presetIndex < 0 || outlineTypeArr == null || presetIndex >= outlineTypeArr.Length)
            {
                return false;
            }
            
            bool changed = false;
            
            if (outlineType != outlineTypeArr[presetIndex]) { outlineType = outlineTypeArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(sobelFilterOpacity, sobelOpacityArr[presetIndex])) { sobelFilterOpacity = sobelOpacityArr[presetIndex]; changed = true; }
            if (outlineStrengthLevel != outlineStrengthArr[presetIndex]) { outlineStrengthLevel = outlineStrengthArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_CURRENT_SOBEL], currentSobelArr[presetIndex])) { syncedFloats[SYNC_FLOAT_CURRENT_SOBEL] = currentSobelArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(invertStrength, invertArr[presetIndex])) { invertStrength = invertArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AMPLITUDE], amplitudeArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AMPLITUDE] = amplitudeArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(blurStrength, blurArr[presetIndex])) { blurStrength = blurArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_DISTORTION], distortionArr[presetIndex])) { syncedFloats[SYNC_FLOAT_DISTORTION] = distortionArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_NOISE], noiseArr[presetIndex])) { syncedFloats[SYNC_FLOAT_NOISE] = noiseArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_SCANLINE], scanlineArr[presetIndex])) { syncedFloats[SYNC_FLOAT_SCANLINE] = scanlineArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_DEPTH_BUFFER], depthBufferArr[presetIndex])) { syncedFloats[SYNC_FLOAT_DEPTH_BUFFER] = depthBufferArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_NORMAL_MAP], normalMapArr[presetIndex])) { syncedFloats[SYNC_FLOAT_NORMAL_MAP] = normalMapArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_SATURATION], saturationArr[presetIndex])) { syncedFloats[SYNC_FLOAT_SATURATION] = saturationArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_ROUNDING], roundingArr[presetIndex])) { syncedFloats[SYNC_FLOAT_ROUNDING] = roundingArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_FOG_SAFE], fogSafeArr[presetIndex])) { syncedFloats[SYNC_FLOAT_FOG_SAFE] = fogSafeArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_BRIGHTNESS], brightnessArr[presetIndex])) { syncedFloats[SYNC_FLOAT_BRIGHTNESS] = brightnessArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_HDR], hdrArr[presetIndex])) { syncedFloats[SYNC_FLOAT_HDR] = hdrArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_CONTRAST], contrastArr[presetIndex])) { syncedFloats[SYNC_FLOAT_CONTRAST] = contrastArr[presetIndex]; changed = true; }
            if (syncedInts[SYNC_INT_SCAN_INDEX] != scanIndexArr[presetIndex]) { syncedInts[SYNC_INT_SCAN_INDEX] = scanIndexArr[presetIndex]; changed = true; }
            if (syncedInts[SYNC_INT_AUDIOLINK_BAND] != audioBandArr[presetIndex]) { syncedInts[SYNC_INT_AUDIOLINK_BAND] = audioBandArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_FILTER], alFilterArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_FILTER] = alFilterArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_SHAKE], alShakeArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_SHAKE] = alShakeArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_BLUR], alBlurArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_BLUR] = alBlurArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_DISTORT], alDistortArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_DISTORT] = alDistortArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_NOISE], alNoiseArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_NOISE] = alNoiseArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_FOG], alFogArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_FOG] = alFogArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_OUTLINE], alOutlineArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_OUTLINE] = alOutlineArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_IMAGE], alImageArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_IMAGE] = alImageArr[presetIndex]; changed = true; }
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_MISC], alMiscArr[presetIndex])) { syncedFloats[SYNC_FLOAT_AL_MISC] = alMiscArr[presetIndex]; changed = true; }
            if (syncedInts[SYNC_INT_COLOR_INDEX] != selectedColorArr[presetIndex]) { syncedInts[SYNC_INT_COLOR_INDEX] = selectedColorArr[presetIndex]; changed = true; }
            if (!ColorsApproximatelyEqual(currentOutlineColor, outlineColorArr[presetIndex])) { currentOutlineColor = outlineColorArr[presetIndex]; changed = true; }
            if (syncedInts[SYNC_INT_OVERLAY_INDEX] != overlayArr[presetIndex]) { syncedInts[SYNC_INT_OVERLAY_INDEX] = overlayArr[presetIndex]; changed = true; }
            
            if (changed)
            {
                ApplyMochieMaterial();
                RequestSerialization();
            }
            
            return changed;
        }
        
        /// <summary>
        /// Checks if the current Mochie state matches a preset snapshot.
        /// Used by PresetHandler to determine if a preset is active.
        /// </summary>
        public bool DoesPresetMatch(
            int presetIndex,
            int[] outlineTypeArr,
            float[] sobelOpacityArr,
            int[] outlineStrengthArr,
            float[] currentSobelArr,
            float[] invertArr,
            float[] amplitudeArr,
            float[] blurArr,
            float[] distortionArr,
            float[] noiseArr,
            float[] scanlineArr,
            float[] depthBufferArr,
            float[] normalMapArr,
            float[] saturationArr,
            float[] roundingArr,
            float[] fogSafeArr,
            float[] brightnessArr,
            float[] hdrArr,
            float[] contrastArr,
            int[] scanIndexArr,
            int[] audioBandArr,
            float[] alFilterArr,
            float[] alShakeArr,
            float[] alBlurArr,
            float[] alDistortArr,
            float[] alNoiseArr,
            float[] alFogArr,
            float[] alOutlineArr,
            float[] alImageArr,
            float[] alMiscArr,
            int[] selectedColorArr,
            Color[] outlineColorArr,
            int[] overlayArr)
        {
            if (presetIndex < 0 || outlineTypeArr == null || presetIndex >= outlineTypeArr.Length)
            {
                return true;
            }
            
            if (outlineType != outlineTypeArr[presetIndex]) return false;
            if (!Mathf.Approximately(sobelFilterOpacity, sobelOpacityArr[presetIndex])) return false;
            if (outlineStrengthLevel != outlineStrengthArr[presetIndex]) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_CURRENT_SOBEL], currentSobelArr[presetIndex])) return false;
            if (!Mathf.Approximately(invertStrength, invertArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AMPLITUDE], amplitudeArr[presetIndex])) return false;
            if (!Mathf.Approximately(blurStrength, blurArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_DISTORTION], distortionArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_NOISE], noiseArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_SCANLINE], scanlineArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_DEPTH_BUFFER], depthBufferArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_NORMAL_MAP], normalMapArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_SATURATION], saturationArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_ROUNDING], roundingArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_FOG_SAFE], fogSafeArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_BRIGHTNESS], brightnessArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_HDR], hdrArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_CONTRAST], contrastArr[presetIndex])) return false;
            if (syncedInts[SYNC_INT_SCAN_INDEX] != scanIndexArr[presetIndex]) return false;
            if (syncedInts[SYNC_INT_AUDIOLINK_BAND] != audioBandArr[presetIndex]) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_FILTER], alFilterArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_SHAKE], alShakeArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_BLUR], alBlurArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_DISTORT], alDistortArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_NOISE], alNoiseArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_FOG], alFogArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_OUTLINE], alOutlineArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_IMAGE], alImageArr[presetIndex])) return false;
            if (!Mathf.Approximately(syncedFloats[SYNC_FLOAT_AL_MISC], alMiscArr[presetIndex])) return false;
            if (syncedInts[SYNC_INT_COLOR_INDEX] != selectedColorArr[presetIndex]) return false;
            if (!ColorsApproximatelyEqual(currentOutlineColor, outlineColorArr[presetIndex])) return false;
            if (syncedInts[SYNC_INT_OVERLAY_INDEX] != overlayArr[presetIndex]) return false;
            
            return true;
        }
        
        #endregion
        
        private bool ColorsApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) &&
            Mathf.Approximately(a.g, b.g) &&
            Mathf.Approximately(a.b, b.b) &&
            Mathf.Approximately(a.a, b.a);
        }
    }
}

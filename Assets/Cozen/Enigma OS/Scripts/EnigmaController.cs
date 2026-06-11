
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Persistence;
using VRC.SDK3.StringLoading;
using TMPro;

namespace Cozen.EnigmaOS
{
    // ----------------------------------------------------------------------------
    //  EnigmaController � Main runtime controller
    // ----------------------------------------------------------------------------

    /// <summary>
    /// Main controller for Enigma OS. Hosts the editor-time data model and the
    /// build-time generated runtime flat arrays that Udon consumes at play time.
    ///
    /// Architecture summary:
    ///   � EnigmaManagedButton / EnigmaFader are the physical scene
    ///     components. They know nothing about folders or entries.
    ///   � EnigmaController is the single source of truth for all state.
    ///   � The custom editor build step populates all rt* arrays from the
    ///     structured EnigmaFolderData[] before upload.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class EnigmaController : UdonSharpBehaviour
    {
        // ------------------------------------------------------------------------
        //  EDITOR-TIME DATA � serialized, written by custom inspector
        //  NOT accessed by Udon runtime code.
        // ------------------------------------------------------------------------
        // Editor data lives on the companion EnigmaControllerData MonoBehaviour
        // (NOT on this UdonSharpBehaviour) to avoid polluting the Udon heap.

        // ------------------------------------------------------------------------
        //  PHYSICAL COMPONENT REFERENCES � ordered by user in reorderable lists
        // ------------------------------------------------------------------------
        [Tooltip("Content button slots, ordered as desired by the user.")]
        public EnigmaManagedButton[] buttonSlots = new EnigmaManagedButton[0];
        [Tooltip("Fader slots, ordered as desired by the user.")]
        public EnigmaFader[] faderSlots = new EnigmaFader[0];

        // -- Optional display text fields --
        [Tooltip("TMP text showing the current folder name on the physical hardware.")]
        public TMP_Text folderNameText;
        [Tooltip("TMP text showing current page / total pages (e.g. '1 / 3').")]
        public TMP_Text pageIndicatorText;

        // -- Shared hand-tracker objects for controller-managed faders --
        [Header("Controller-Managed Fader Hand Trackers")]
        [Tooltip("A single left-hand tracking collider shared across all fader slots " +
                 "when faders have this controller assigned. Eliminates duplicate per-fader " +
                 "hand-collider objects and update loops.")]
        public GameObject sharedLeftHandCollider;
        [Tooltip("Right-hand equivalent of sharedLeftHandCollider.")]
        public GameObject sharedRightHandCollider;

        // ------------------------------------------------------------------------
        //  SETTINGS
        // ------------------------------------------------------------------------
        [Header("Preview Layout")]
        [Tooltip("Number of button columns in the physical controller layout. " +
                 "Used by the inspector preview grid.")]
        public int previewColumns = 3;
        [Tooltip("Number of button rows in the physical controller layout. " +
                 "Used by the inspector preview grid.")]
        public int previewRows = 3;

        public Color activeColor = Color.HSVToRGB(242f / 360f, 1f, 1f);
        public Color inactiveColor = Color.white;

        [Tooltip("Enable verbose debug logging to the Unity console for networking, state changes, and action execution.")]
        public bool debugLogging = false;

        private void Log(string msg)
        {
            if (debugLogging) Debug.Log($"[Enigma] {msg}");
        }

        [Header("Defaults")]
        public int defaultFolderIndex = 0;
        public float autoChangeInterval = 10f;

        public bool whitelistEnabled = false;
        public bool instanceOwnerAlwaysHasAccess = false;
        public string[] authorizedUsernames = new string[0];

        // Optional third-party access control integrations
        public UdonSharpBehaviour ohGeezCmonAccessControl;
        public UdonSharpBehaviour proTVManagedWhitelist;
        public UdonSharpBehaviour flatlineSync;

        // ── Whitelist monitoring / sync state ──
        private string[] _normalizedAuthorizedUsernames;
        private bool _whitelistInitialized;

        // OhGeezCmon sync tracking
        private int _lastKnownOhGeezSyncVersion = -1;
        private float _ohGeezNextCheckTime;
        private bool _ohGeezWaitingForSync;
        private int _ohGeezSyncRetryCount;
        private const int OhGeezMaxRetries = 10;
        private const float OhGeezRetryDelay = 0.5f;
        private const float OhGeezCheckInterval = 2f;

        // ProTV sync tracking
        private string[] _lastKnownProTVAuthorizedList;
        private float _proTVNextCheckTime;
        private bool _proTVWaitingForSync;
        private int _proTVSyncRetryCount;
        private UdonSharpBehaviour _proTVManager;
        private bool _proTVManagerResolved;
        private const int ProTVMaxRetries = 10;
        private const float ProTVRetryDelay = 0.5f;
        private const float ProTVCheckInterval = 2f;

        // Flatline sync tracking
        private string[] _lastKnownFlatlineWhitelistList;
        private float _flatlineNextCheckTime;
        private bool _flatlineWaitingForSync;
        private int _flatlineSyncRetryCount;
        private const int FlatlineMaxRetries = 20;
        private const float FlatlineRetryDelay = 1f;
        private const float FlatlineCheckInterval = 2f;

        // -- World Stats configuration (Display Stat action, type 21) --
        // The editor builds the API URL from the world ID and stores it here.
        // All fields are HideInInspector � configured via the World Stats section in the inspector.
        [HideInInspector] public string worldStatsWorldId = "";
        [HideInInspector] public VRCUrl worldStatsBuiltApiUrl;
        [HideInInspector] public float worldStatsUpdateInterval = 120f;
        [HideInInspector] public bool worldStatsAutoStart = true;
        [HideInInspector] public bool worldStatsUseThousandsSeparators = true;
        [HideInInspector] public bool worldStatsPreserveOnError = true;

        // ------------------------------------------------------------------------
        //  RUNTIME FLAT ARRAYS � populated by build step, consumed by Udon
        //  All HideInInspector � users never touch these directly.
        // ------------------------------------------------------------------------

        // -- Folder structure --
        [HideInInspector] public string[] rtFolderNames = new string[0];
        [HideInInspector] public int[] rtFolderEntryStart = new int[0];
        [HideInInspector] public int[] rtFolderEntryCount = new int[0];
        [HideInInspector] public int[] rtEntryFolderIndex = new int[0]; // O(1) folder lookup per entry

        // -- Entry data (all folders concatenated) --
        [HideInInspector] public string[] rtEntryLabels = new string[0];
        [HideInInspector] public int[] rtEntryButtonTypes = new int[0];
        [HideInInspector] public bool[] rtEntryIsStateful  = new bool[0]; // true = maintains on/off state (toggle, toggle+step)
        [HideInInspector] public bool[] rtEntryDefaultOn = new bool[0];
        [HideInInspector] public int[] rtEntryExclusiveGroup = new int[0];  // -1 = none (legacy: first tag only)
        // Parallel string array so DeactivateExclusiveGroup can find a group at runtime by tag (legacy)
        [HideInInspector] public string[] rtEntryExclusiveGroupNames = new string[0];
        // Multi-group support: each entry can belong to several exclusive groups (one per comma-separated tag).
        // rtEntryExclusiveGroupFlat is a flat list of group IDs; start/count give the slice for each entry.
        [HideInInspector] public int[] rtEntryExclusiveGroupFlat  = new int[0];
        [HideInInspector] public int[] rtEntryExclusiveGroupStart = new int[0];
        [HideInInspector] public int[] rtEntryExclusiveGroupCount = new int[0];
        // Tag name indexed by group ID � used by DeactivateExclusiveGroup to resolve a name ? ID.
        [HideInInspector] public string[] rtGroupTagNames = new string[0];
        // Per-entry: whether clicking an already-active exclusive button turns it off (VRCFury "Exclusive Off").
        [HideInInspector] public bool[] rtEntryExclusiveOff = new bool[0];

        // Standalone EnigmaButton instances that participate in this controller's exclusive groups.
        // Indexed by exclusive-group ID (same axis as rtGroupTagNames).
        // rtExclusiveButtonPeers is the flat list; rtExclusiveButtonPeerGroupStart[gid] and
        // rtExclusiveButtonPeerGroupCount[gid] give the slice for exclusive-group ID gid.
        [HideInInspector] public EnigmaButton[] rtExclusiveButtonPeers         = new EnigmaButton[0];
        [HideInInspector] public int[]          rtExclusiveButtonPeerGroupStart = new int[0];
        [HideInInspector] public int[]          rtExclusiveButtonPeerGroupCount = new int[0];

        // Pre-baked peer lookup: for each entry, the range of peer entry indices that share
        // any exclusive group. Eliminates nested O(N*G) loops at runtime.
        [HideInInspector] public int[] rtEntryExclusivePeerStart = new int[0];
        [HideInInspector] public int[] rtEntryExclusivePeerCount = new int[0];
        [HideInInspector] public int[] rtEntryExclusivePeerFlat  = new int[0];
        // Pre-baked exclusive-off peer: for each entry, the index of the exclusive-off
        // entry in its group (-1 if none). Eliminates FindExclusiveOffInGroup searches.
        [HideInInspector] public int[] rtEntryExclusiveOffPeer = new int[0];

        // -- Autochange Group data --
        // Per-entry: the autochange group ID this entry belongs to (-1 = not in any group).
        [HideInInspector] public int[] rtEntryAutoChangeGroupId = new int[0];
        // -- Per-entry expire (auto-deactivate timer) --
        // 0 = no expire. Authoritative source: EnigmaEntryData.useExpire +
        // expireSeconds. Read by ScheduleEntryExpire on Toggle activation.
        [HideInInspector] public float[] rtEntryExpireSeconds = new float[0];
        // Tag name indexed by autochange group ID.
        [HideInInspector] public string[] rtAutoChangeGroupTagNames = new string[0];
        // -- Shader template instances (created by build step, tracked for cleanup) --
        [HideInInspector] public GameObject[] shaderInstances = new GameObject[0];

        // -- Action data (primary + additional, all entries concatenated) --
        [HideInInspector] public int[] rtEntryActionStart = new int[0];
        [HideInInspector] public int[] rtEntryActionCount = new int[0];

        // -- Executor component (holds all action-indexed arrays and execution logic) --
        // Created by the build step on the same GameObject. Single source of truth for
        // action execution — both controller and standalone buttons delegate to it.
        [HideInInspector] public EnigmaExecutor executor;

        // -- Shader variant keeper materials (asset anchors, never rendered) --
        // Baked by PrepareShaderLocking: one hidden clone of each Enigma-locked
        // material, captured in its "hot" state (section toggles = 1, all
        // shader_feature_local keywords enabled). Unity collects shader_feature
        // variants from EVERY material included in a build — referencing the
        // keepers here drags them into the world bundle, guaranteeing the
        // variants Enigma's runtime needs are compiled regardless of what
        // happens to the live material's keyword state mid-build (Mochie's
        // inspector syncs keywords to values on every repaint, which during an
        // async VRC build stripped _IMAGE_OVERLAY_ON from a shipped world).
        // Never read at runtime.
        [HideInInspector] public Material[] rtVariantKeeperMaterials = new Material[0];

        // Per-entry custom button color (static or conditional).
        [HideInInspector] public bool[]  rtEntryUseCustomColor  = new bool[0];
        [HideInInspector] public Color[] rtEntryCustomColor     = new Color[0];
        // Conditional color source (independent of display actions).
        [HideInInspector] public int[]          rtEntryCondColorSourceType   = new int[0];   // 0=mat, 1=udon
        [HideInInspector] public Renderer[]     rtEntryCondColorRenderers    = new Renderer[0];
        [HideInInspector] public int[]          rtEntryCondColorMatIndices   = new int[0];
        [HideInInspector] public string[]       rtEntryCondColorPropertyNames = new string[0];
        [HideInInspector] public UdonSharp.UdonSharpBehaviour[] rtEntryCondColorUdonTargets = new UdonSharp.UdonSharpBehaviour[0];
        [HideInInspector] public string[]       rtEntryCondColorUdonVarNames = new string[0];
        // Conditional color rules (indexed per-entry).
        [HideInInspector] public int[]   rtEntryCondColorStart = new int[0];
        [HideInInspector] public int[]   rtEntryCondColorCount = new int[0];
        [HideInInspector] public int[]   rtCondColorConditions = new int[0];   // 0=<, 1=>, 2==, 3=≤, 4=≥
        [HideInInspector] public float[] rtCondColorValues     = new float[0];
        [HideInInspector] public Color[] rtCondColorColors     = new Color[0];

        // -- Fader link data --
        [HideInInspector] public int staticFaderCount = 0;
        
        // Static fader configuration arrays (size matches staticFaderCount)
        [HideInInspector] public string[] rtStaticFaderNames = new string[0];
        // Editor-only helpers
        [HideInInspector] public int[] rtStaticFaderTargetFolders = new int[0];
        [HideInInspector] public bool[] rtStaticFaderTargetsCustom = new bool[0];
        
        // Material/Renderer targets
        [HideInInspector] public Renderer[] rtStaticFaderRenderers = new Renderer[0];
        [HideInInspector] public int[] rtStaticFaderMaterialIndices = new int[0];
        // Extra renderers for multi-renderer static faders. The primary
        // renderer/material is still in rtStaticFaderRenderers / rtStaticFaderMaterialIndices
        // (indexed per-entry). These three parallel flat arrays hold additional
        // renderers. Per-entry count is rtStaticFaderExtraCount[entryIdx];
        // the extras for entry N live at flat indices
        //   [ sum(rtStaticFaderExtraCount[0..N-1]), that_sum + rtStaticFaderExtraCount[N] ).
        // A start index is derived rather than stored so adds/removes only need
        // to mutate one slot of rtStaticFaderExtraCount plus the flat arrays.
        [HideInInspector] public Renderer[] rtStaticFaderExtraRenderers = new Renderer[0];
        [HideInInspector] public int[] rtStaticFaderExtraMaterialIndices = new int[0];
        [HideInInspector] public int[] rtStaticFaderExtraCount = new int[0];
        [HideInInspector] public string[] rtStaticFaderPropertyNames = new string[0];
        [HideInInspector] public int[] rtStaticFaderPropertyTypes = new int[0]; // 0=Float, 1=Color
        [HideInInspector] public float[] rtStaticFaderMinValues = new float[0];
        [HideInInspector] public float[] rtStaticFaderMaxValues = new float[0];
        [HideInInspector] public float[] rtStaticFaderDefaultValues = new float[0];
        [HideInInspector] public Color[] rtStaticFaderDefaultColors = new Color[0];
        
        // Value indicators
        [HideInInspector] public bool[] rtStaticFaderIndicatorEnabled = new bool[0];
        [HideInInspector] public Color[] rtStaticFaderIndicatorColors = new Color[0];
        [HideInInspector] public bool[] rtStaticFaderIndicatorConditional = new bool[0];
        
        // Udon targets
        [HideInInspector] public bool[] rtStaticFaderTargetsUdon = new bool[0];
        [HideInInspector] public UdonSharpBehaviour[] rtStaticFaderUdonBehaviours = new UdonSharpBehaviour[0];
        [HideInInspector] public string[] rtStaticFaderUdonVariableNames = new string[0];
        // Extra Udon behaviours for multi-target Udon faders. Mirror of the
        // rtStaticFaderExtraRenderers flat-array pattern: all extras across
        // all entries are packed into rtStaticFaderExtraUdonBehaviours; the
        // block for entry N starts at prefix-sum(rtStaticFaderExtraUdonCount[0..N-1])
        // and has length rtStaticFaderExtraUdonCount[N]. Kept separate from
        // the renderer-extras count so flipping a fader between Udon and
        // Material modes doesn't mis-index the wrong flat array.
        [HideInInspector] public UdonSharpBehaviour[] rtStaticFaderExtraUdonBehaviours = new UdonSharpBehaviour[0];
        [HideInInspector] public int[] rtStaticFaderExtraUdonCount = new int[0];
        
        // Slider targets
        [HideInInspector] public bool[] rtStaticFaderTargetsSlider = new bool[0];
#if UNITY_UI
        [HideInInspector] public UnityEngine.UI.Slider[] rtStaticFaderSliders = new UnityEngine.UI.Slider[0];
#else
        [HideInInspector] public GameObject[] rtStaticFaderSliders = new GameObject[0]; // Fallback if no UI, shouldn't normally happen
#endif
        [HideInInspector] public bool[] rtStaticFaderSliderReversed = new bool[0];
        [HideInInspector] public bool[] rtStaticFaderAlwaysVisible = new bool[0];
        [HideInInspector] public bool[] rtStaticFaderTargetsSkybox = new bool[0];

        // Dynamic fader configuration arrays (used when buttons link to faders)
        [HideInInspector] public int[] rtFaderLinkEntryIndex = new int[0];
        // Per-link display name sourced from EnigmaFaderLinkData.faderName. When
        // empty, BindDynamicFaderToSlot falls back to the owning entry's label.
        // Without this array, every dynamic fader displayed as the button label,
        // so multiple links on one button (e.g. "Holo 1"/"Holo 2"/"Holo Thick")
        // all read the same on the physical slot.
        [HideInInspector] public string[] rtFaderLinkNames = new string[0];
        [HideInInspector] public Renderer[] rtFaderLinkRenderers = new Renderer[0];
        [HideInInspector] public int[] rtFaderLinkMaterialIndices = new int[0];
        [HideInInspector] public string[] rtFaderLinkPropertyNames = new string[0];
        [HideInInspector] public int[] rtFaderLinkPropertyTypes = new int[0];
        [HideInInspector] public float[] rtFaderLinkMinValues = new float[0];
        [HideInInspector] public float[] rtFaderLinkMaxValues = new float[0];
        [HideInInspector] public float[] rtFaderLinkDefaultValues = new float[0];
        [HideInInspector] public Color[] rtFaderLinkDefaultColors = new Color[0];
        [HideInInspector] public bool[] rtFaderLinkIndicatorEnabled = new bool[0];
        [HideInInspector] public Color[] rtFaderLinkIndicatorColors = new Color[0];
        [HideInInspector] public bool[] rtFaderLinkIndicatorConditional = new bool[0];
        // Dynamic fader link — slider targets
        [HideInInspector] public bool[] rtFaderLinkTargetsSlider = new bool[0];
#if UNITY_UI
        [HideInInspector] public UnityEngine.UI.Slider[] rtFaderLinkSliders = new UnityEngine.UI.Slider[0];
#else
        [HideInInspector] public GameObject[] rtFaderLinkSliders = new GameObject[0];
#endif
        [HideInInspector] public bool[] rtFaderLinkSliderReversed = new bool[0];
        // Dynamic fader link — udon targets
        [HideInInspector] public bool[] rtFaderLinkTargetsSkybox = new bool[0];
        [HideInInspector] public bool[] rtFaderLinkTargetsUdon = new bool[0];
        [HideInInspector] public UdonSharpBehaviour[] rtFaderLinkUdonBehaviours = new UdonSharpBehaviour[0];
        [HideInInspector] public string[] rtFaderLinkUdonVariableNames = new string[0];

        // -- Step button data --
        [HideInInspector] public float[] rtStepAmounts = new float[0];
        [HideInInspector] public float[] rtStepMinValues = new float[0];
        [HideInInspector] public float[] rtStepMaxValues = new float[0];
        [HideInInspector] public bool[]  rtStepWrap      = new bool[0];

        // -- Color cycle data (flat with index mapping; Udon has no jagged arrays) --
        [HideInInspector] public int[] rtColorPaletteStart = new int[0];
        [HideInInspector] public int[] rtColorPaletteCount = new int[0];
        [HideInInspector] public Color[] rtColorPaletteColors = new Color[0];
        [HideInInspector] public Renderer[] rtColorPaletteRenderers = new Renderer[0];
        [HideInInspector] public int[] rtColorPaletteMaterialIndices = new int[0];
        [HideInInspector] public string[] rtColorPalettePropertyNames = new string[0];

        // -- Color Selector (type 10) runtime data --
        // Per-entry: for entries with Color Display (role 0) or Change Color (role 2), the
        // entry index of the linked Set Color (role 1) palette owner (-1 if none).
        [HideInInspector] public int[] rtColorLinkedEntry = new int[0];

        // -- Variant Selector (type 19) runtime data --
        // Mirrors the Color Selector (type 10) system but for arbitrary shader-property values.
        // Per-entry (role-1 "Set Variant" entries): start/count into the flat variant-items list.
        [HideInInspector] public int[] rtVariantItemStart   = new int[0];   // -1 = no items
        [HideInInspector] public int[] rtVariantItemCount   = new int[0];
        // Flat variant-item arrays (indexed by totalVariantItems):
        [HideInInspector] public string[]  rtVariantItemNames        = new string[0];
        [HideInInspector] public float[]   rtVariantItemFloatValues  = new float[0];
        [HideInInspector] public Color[]   rtVariantItemColorValues  = new Color[0];
        [HideInInspector] public Vector4[] rtVariantItemVectorValues = new Vector4[0];
        [HideInInspector] public Texture[] rtVariantItemTextures     = new Texture[0];
        // Per-entry: for role-0 (Variant Display) and role-2 (Change Variant) entries, the
        // entry index of the linked Set Variant (role 1) owner (-1 if none).
        [HideInInspector] public int[] rtVariantLinkedEntry = new int[0];

        // -- Preset button runtime data --
        [HideInInspector] public bool[] rtEntryIsPreset = new bool[0];
        [HideInInspector] public int[] rtPresetScopes = new int[0];
        [HideInInspector] public int[] rtPresetIncludedFolderStart = new int[0];
        [HideInInspector] public int[] rtPresetIncludedFolderCount = new int[0];
        [HideInInspector] public int[] rtPresetIncludedFolders = new int[0];
        [HideInInspector] public bool[] rtPresetIncludeFaders = new bool[0];
        [HideInInspector] public bool[] rtPresetIncludeStepValues = new bool[0];
        [HideInInspector] public bool[] rtPresetIncludeColorPalettes = new bool[0];
        [HideInInspector] public bool[] rtPresetIncludeVariantGroups = new bool[0];
        // Maps each entry to its 0-based preset-slot index (-1 if the entry is not a preset button).
        [HideInInspector] public int[] rtPresetSlotIndex = new int[0];
        // Maps each entry to its preset button role (-1 = not a preset,
        // 0 = Slot, 1 = Save Button, 2 = Load Button, 3 = Clear Button).
        [HideInInspector] public int[] rtPresetRoles = new int[0];

        // ------------------------------------------------------------------------
        //  RUNTIME SYNCED STATE
        // ------------------------------------------------------------------------
        [UdonSynced] public int currentFolderIndex = 0;
        [UdonSynced] public int currentPageIndex = 0;
        [UdonSynced] public bool[] entryStates = new bool[0];
        [UdonSynced] public bool autoChangeActive = false;
        [UdonSynced] public float[] stepCurrentValues = new float[0];
        // Per fader-link: remembered current value so dynamic faders restore
        // their position when the parent entry is re-activated on any slot.
        [UdonSynced] public float[] faderLinkCurrentValues = new float[0];
        [UdonSynced] public int[] colorPaletteCurrentIndices = new int[0];
        // Per-entry: pending/preview color index for Color Selector Set Color (role 1) entries.
        // Distinct from colorPaletteCurrentIndices (the applied color index).
        [UdonSynced] public int[] colorPalettePendingIndices = new int[0];

        // -- Autochange Group synced state --
        [UdonSynced] public bool autoChangeGroupActive = false;
        [UdonSynced] public int  autoChangeGroupId     = -1;   // -1 = no group active
        [UdonSynced] public float autoChangeGroupInterval = 10f;
        [UdonSynced] public bool autoChangeGroupRandom = false;

        // -- Variant Selector synced state --
        // Per-entry (role-1 entries): applied and pending variant item indices.
        // Mirrors colorPaletteCurrentIndices / colorPalettePendingIndices.
        [UdonSynced] public int[] variantCurrentIndices = new int[0];
        [UdonSynced] public int[] variantPendingIndices = new int[0];

        // -- Auto-change timer --
        private float _autoChangeTimer;
        private float _autoChangeGroupTimer;

        // -- Delayed-action pending queue --
        // Fixed-size slot table: each slot stores one pending (entry-index, action-index, active-flag, remaining-time).
        // 32 slots covers simultaneous presses on a full 32-button layout where every action has a delay.
        // Increase kDelayQueueSize if you have more than 32 actions with delays that could fire concurrently.
        private const int kDelayQueueSize = 32;
        private int     _delayQueueActiveCount = 0;   // number of currently occupied slots
        private int[]   _delayQueueEntryIdx  = new int[kDelayQueueSize];
        private int[]   _delayQueueActionIdx = new int[kDelayQueueSize];
        private bool[]  _delayQueueActive    = new bool[kDelayQueueSize];
        private float[] _delayQueueTimer     = new float[kDelayQueueSize];
        private bool[]  _delayQueueOccupied  = new bool[kDelayQueueSize];
        // (A) Condition value snapshotted at schedule time so that a condition-referenced entry
        // changing state during the delay window does not silently suppress or unexpectedly
        // trigger the deferred action.
        private bool[]  _delayQueueConditionSnapshot = new bool[kDelayQueueSize];

        // -- Entry-level expire queue --
        // Tracks entries that should auto-deactivate after a timer expires.
        // Up to 16 entries can be pending simultaneously (one per entry in a typical 4�4 layout).
        // If all 16 slots are occupied the new schedule silently falls through � the entry will
        // not auto-revert, but this is an unlikely edge case in normal use.
        private const int kExpireQueueSize = 16;
        private int     _expireQueueCount    = 0;
        private int[]   _expireQueueEntryIdx = new int[kExpireQueueSize];
        private float[] _expireQueueTimer    = new float[kExpireQueueSize];
        private bool[]  _expireQueueOccupied = new bool[kExpireQueueSize];

        // -- Fader mode: 0 = hand collider, 1 = VRC Pickup --
        [UdonSynced] public int faderMode = 0;
        [UdonSynced] public int currentFaderPage = 0;

        // -- World Stats synced data (Display Stat type 21) --
        // Only the instance master polls the VRChat API; results are synced to all clients.
        [UdonSynced] public int statsVisits    = -1;
        [UdonSynced] public int statsFavorites = -1;
        [UdonSynced] public int statsOccupants = -1;
        [UdonSynced] public int statsPopularity = -1;
        [UdonSynced] public int statsHeat      = -1;
        [UdonSynced] public int statsCapacity  = -1;

        // -- Preset save/load storage --
        // The synced preset slot data lives on a dedicated EnigmaPresetStorage
        // UdonSharpBehaviour attached to this same GameObject. It is auto-created
        // by the editor build pipeline when any folder contains a preset action
        // (presetRole == 0).
        //
        // Why a separate behaviour?
        // UdonSharp [UdonSynced] is atomic per behaviour — every call to
        // RequestSerialization() ships ALL synced fields on that behaviour in one
        // packet. Keeping the ~26 KB preset arrays on the controller meant every
        // effect-toggle press dragged the full preset library across the wire
        // even when nothing in it had changed. Splitting storage into its own
        // behaviour means the controller's sync carries only the small ~2 KB
        // effect-state payload, and the storage's own RequestSerialization is
        // called exclusively when presets are actually saved / cleared / loaded
        // from PlayerData — rare events compared to effect toggles.
        //
        // All preset reads and writes in EnigmaController.Presets.cs go through
        // this reference. If presetStorage is null (no preset actions in any
        // folder) the preset code paths early-out and no storage is allocated.
        [HideInInspector] public EnigmaPresetStorage presetStorage;

        // ── Momentary dispatch ────────────────────────────────────────────────
        // Momentary buttons (Satur, Round, Fog, Bright, etc.) don't mutate
        // entryStates or stepCurrentValues, so their material writes are invisible
        // to HasEntryStateChanged() and never replayed via RestoreWorldState.
        // Mirror each Momentary press through these two synced fields: the
        // receiver's OnDeserialization replays ExecuteEntryActions for the entry
        // whenever momentaryDispatchSeq advances beyond the local baseline.
        [UdonSynced] public int momentaryDispatchEntry = -1;
        [UdonSynced] public int momentaryDispatchSeq   = 0;

        // -- Controller-managed fader coordination --
        // These are local-only (not synced) — each client manages its own hand interaction.
        private bool _ctrlLeftGrabbed;
        private bool _ctrlRightGrabbed;
        private EnigmaFader _activeLeftFader;
        private EnigmaFader _activeRightFader;

        // -- Grab permission arbitration --
        // Tracks which fader index is currently grabbed by each hand (-1 = none).
        // Prevents two hands from fighting over the same fader and ensures
        // the closest fader is selected when multiple overlap.
        private int _leftGrabbedFaderIndex  = -1;
        private int _rightGrabbedFaderIndex = -1;

        // -- Hand collider tracking --
        // Moves the shared hand collider objects to follow the local player's
        // finger bone positions each frame. Only active for VR players in
        // hand-collider mode who pass the whitelist check.
        private VRCPlayerApi _localPlayer;
        private bool _handColliderTrackingEnabled;
        private bool _isLocalPlayerVR;

        // -- Preset clear mode --
        // When true, the next Preset Slot press will clear that slot instead of loading it.
        // Local-only; each client tracks its own clear-mode state.
        private bool _presetClearModeActive;

        private float _presetIncompatibleTimer = 0f;

        // _initialSkybox and _savedTransformValues now live on the executor.

        // -- World Stats private polling fields --
        private const float StatsMinInterval    = 30f;
        private const float StatsMaxInterval    = 300f;
        private const float StatsTimingTolerance = 0.01f;
        private float _statsNextFetchTime  = float.PositiveInfinity;
        private bool  _statsPendingRequest = false;
        private bool  _statsInitialized    = false;
        private bool  _statsLocalUpdateScheduled = false;
        private int   _statsPeakPlayers    = 0;
        private VRCPlayerApi[] _statsPlayerBuffer;

        // ------------------------------------------------------------------------
        //  LIFECYCLE
        // ------------------------------------------------------------------------

        private void Start()
        {
            Log($"Start() isMaster={Networking.IsMaster} entries={(entryStates != null ? entryStates.Length : 0)} folders={(rtFolderNames != null ? rtFolderNames.Length : 0)}");
            // Keyword diagnostics: log what's baked and what's on the material.
            if (debugLogging && executor != null)
            {
                int kwCount = 0;
                int kwToggleCount = 0;
                if (executor.rtActionKeywords != null)
                {
                    for (int i = 0; i < executor.rtActionKeywords.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(executor.rtActionKeywords[i]))
                        {
                            kwCount++;
                            bool isTog = executor.rtActionIsKeywordToggle != null
                                && i < executor.rtActionIsKeywordToggle.Length
                                && executor.rtActionIsKeywordToggle[i];
                            if (isTog) kwToggleCount++;
                            string prop = executor.rtActionPropertyNames != null && i < executor.rtActionPropertyNames.Length
                                ? executor.rtActionPropertyNames[i] : "?";
                            Log($"  Baked kw[{i}]: prop={prop} kw={executor.rtActionKeywords[i]} isToggle={isTog}");
                        }
                    }
                }
                Log($"  Total baked keywords: {kwCount} ({kwToggleCount} toggles)");

                // Check material keyword state
                if (executor.rtActionTargetRenderers != null && executor.rtActionTargetRenderers.Length > 0
                    && executor.rtActionTargetRenderers[0] != null)
                {
                    Material[] mats = executor.rtActionTargetRenderers[0].sharedMaterials;
                    if (mats != null && mats.Length > 0 && mats[0] != null)
                    {
                        string[] matKws = mats[0].shaderKeywords;
                        Log($"  Material keywords ({matKws.Length}): {string.Join(", ", matKws)}");
                    }
                }
            }
            if (executor != null) executor.Initialize();
            if (Networking.IsMaster)
            {
                InitializeRuntimeState();
                int maxFolderIndex = rtFolderNames != null && rtFolderNames.Length > 0
                                     ? rtFolderNames.Length - 1 : 0;
                currentFolderIndex = Mathf.Clamp(defaultFolderIndex, 0, maxFolderIndex);
                currentPageIndex = 0;

                // Master seeds the snapshot so later deserializations (after a ownership
                // handoff) diff against a real baseline instead of a null _prev.
                SnapshotEntryState();
                // Baseline the Momentary dispatch tracker so that if ownership
                // later transfers back to us after a Momentary press, we don't
                // spuriously replay our own advance on the inbound sync.
                _momentaryDispatchBaselined = true;

                RequestSerialization();
            }

            // Preset storage: every client (master and non-master) allocates the
            // storage's flat arrays locally, so reads are safe before the first
            // sync arrives. AllocateStorage is idempotent — if sync has already
            // delivered data with matching sizes, the existing contents are
            // preserved. If not, the freshly-allocated zero-initialized arrays
            // will be overwritten by the master's sync packet when it arrives.
            //
            // Size derives from the baked rtPresetRoles / rtEntryLabels / faderSlots
            // arrays, all of which are part of the prefab (not synced) and
            // therefore match across all clients at build time.
            if (presetStorage != null)
            {
                int numPresetSlots = 0;
                if (rtPresetRoles != null)
                {
                    for (int pi = 0; pi < rtPresetRoles.Length; pi++)
                        if (rtPresetRoles[pi] == 0) numPresetSlots++;
                }
                else if (rtEntryIsPreset != null)
                {
                    for (int pi = 0; pi < rtEntryIsPreset.Length; pi++)
                        if (rtEntryIsPreset[pi]) numPresetSlots++;
                }

                int numEntriesForPresets = rtEntryLabels != null ? rtEntryLabels.Length : 0;
                int numFadersForPresets  = faderSlots    != null ? faderSlots.Length    : 0;
                presetStorage.AllocateStorage(numPresetSlots, numEntriesForPresets, numFadersForPresets);
            }
            InitializeWorldStats();
            BindStaticFaders();
            UpdateDisplay();
            InitializeHandColliderTracking();
            InitializeWhitelist();
        }

        // Tracks the last-applied entry/step state to detect actual changes
        // and avoid running RestoreWorldState on stats-only deserializations.
        private bool[] _prevEntryStates;
        private float[] _prevStepValues;

        // ------------------------------------------------------------------------
        //  DEFERRED SYNC (press coalescing)
        //
        //  VRChat Manual-sync has a per-behaviour bandwidth budget (~11 KB/s).
        //  If you call RequestSerialization faster than that budget allows, the
        //  packets queue up on VRChat's side and drain at the budget rate,
        //  delivering a trail of historical states to the receiver and leaving
        //  them seconds behind the sender.
        //
        //  The "drain rate" looks like a fixed ~4 s cadence when the payload is
        //  large (e.g. when the presetSaved* arrays were accidentally UdonSynced
        //  — ~30 KB per packet / 11 KB/s = ~2.7 s per packet, plus queueing).
        //  With the preset arrays unsynced, the base state payload is under
        //  2 KB, so a single packet transmits in ~200 ms and you can actually
        //  sync at a modest rate without backlog.
        //
        //  DeferredRequestSerialization still coalesces rapid calls into at most
        //  one sync per SyncDebounceSeconds window, but the window can be
        //  much smaller now:
        //
        //    • First call after a quiet period fires immediately.
        //    • Subsequent calls inside the window schedule a single pending
        //      commit via SendCustomEventDelayedSeconds at end-of-window.
        //      Further calls while the commit is pending are no-ops — the
        //      commit will grab the latest state when it fires.
        //    • When the commit fires it calls RequestSerialization once, so a
        //      burst of N clicks produces at most 2 sync packets.
        //
        //  0.5 s is a compromise: fast enough that single clicks feel instant
        //  across clients (burst coalesce latency ≤0.5 s on top of VRChat's
        //  own ~200 ms delivery), slow enough that a true finger-drumming
        //  stream of ~5 clicks/s still collapses to ~2 packets/s and stays
        //  well under VRChat's bandwidth budget.
        //
        //  If sync payload size grows again (adding a new large UdonSynced
        //  array), first check the payload size via an editor script — the
        //  debounce window should exceed payload_bytes / 11000. Previously
        //  this was tuned to 5.0 to work around the ballooned preset storage.
        // ------------------------------------------------------------------------
        private const float SyncDebounceSeconds = 0.5f;
        private float _lastSyncRequestTime = -10f;
        private bool  _syncPending          = false;

        private void DeferredRequestSerialization()
        {
            float now = Time.time;
            float elapsed = now - _lastSyncRequestTime;

            if (elapsed >= SyncDebounceSeconds)
            {
                // First call in a fresh window — send immediately so a single
                // press doesn't incur any extra latency.
                _lastSyncRequestTime = now;
                _syncPending = false;
                RequestSerialization();
                Log($"DeferredSerialize: IMMEDIATE (dt={elapsed:F2}s since last)");
                return;
            }

            if (_syncPending)
            {
                // A commit is already scheduled; it'll pick up the latest state
                // when it fires. Coalesce.
                return;
            }

            // Inside the window and no commit pending — schedule one that fires
            // exactly at the end of the current window.
            _syncPending = true;
            float delay = SyncDebounceSeconds - elapsed;
            if (delay < 0.01f) delay = 0.01f;
            SendCustomEventDelayedSeconds(nameof(CommitDeferredSync), delay);
            Log($"DeferredSerialize: SCHEDULED in {delay:F2}s");
        }

        // Public because VRChat's SendCustomEventDelayedSeconds dispatches via
        // public event name. Not meant to be called directly.
        public void CommitDeferredSync()
        {
            if (!_syncPending) return;
            _syncPending = false;
            _lastSyncRequestTime = Time.time;
            RequestSerialization();
            Log("DeferredSerialize: COMMIT (coalesced tail)");
        }

        // Momentary dispatch local tracker — see momentaryDispatchEntry/Seq above.
        // The baselined flag is set on first sync (master: in Start, non-master:
        // in the first OnDeserialization) so a non-zero seq from a late join
        // doesn't spuriously replay a historical press on the joining client.
        private int  _prevMomentaryDispatchSeq   = 0;
        private bool _momentaryDispatchBaselined = false;

        private bool HasEntryStateChanged()
        {
            if (entryStates == null) return false;
            if (_prevEntryStates == null || _prevEntryStates.Length != entryStates.Length)
                return true;
            for (int i = 0; i < entryStates.Length; i++)
                if (entryStates[i] != _prevEntryStates[i]) return true;
            if (stepCurrentValues != null && _prevStepValues != null
                && stepCurrentValues.Length == _prevStepValues.Length)
            {
                for (int i = 0; i < stepCurrentValues.Length; i++)
                    if (stepCurrentValues[i] != _prevStepValues[i]) return true;
            }
            return false;
        }

        private void SnapshotEntryState()
        {
            if (entryStates != null)
            {
                if (_prevEntryStates == null || _prevEntryStates.Length != entryStates.Length)
                    _prevEntryStates = new bool[entryStates.Length];
                for (int i = 0; i < entryStates.Length; i++)
                    _prevEntryStates[i] = entryStates[i];
            }
            if (stepCurrentValues != null)
            {
                if (_prevStepValues == null || _prevStepValues.Length != stepCurrentValues.Length)
                    _prevStepValues = new float[stepCurrentValues.Length];
                for (int i = 0; i < stepCurrentValues.Length; i++)
                    _prevStepValues[i] = stepCurrentValues[i];
            }
        }

        public override void OnDeserialization()
        {
            Log($"OnDeserialization() folder={currentFolderIndex} page={currentPageIndex} isOwner={Networking.IsOwner(gameObject)}");
            // Only run RestoreWorldState when entry/step state actually changed.
            // Stats-only deserializations (which fire every poll cycle) should NOT
            // trigger a full material state restore — that causes exclusive group
            // properties to flicker as Pass 1 resets shared properties to defaults.
            bool stateChanged = HasEntryStateChanged();
            if (stateChanged)
            {
                // Log what changed for diagnostics
                if (_prevEntryStates != null && entryStates != null)
                {
                    for (int i = 0; i < entryStates.Length && i < _prevEntryStates.Length; i++)
                    {
                        if (entryStates[i] != _prevEntryStates[i])
                        {
                            string lbl = rtEntryLabels != null && i < rtEntryLabels.Length ? rtEntryLabels[i] : "?";
                            Log($"  StateChanged: entry={i} \"{lbl}\" {_prevEntryStates[i]} -> {entryStates[i]}");
                        }
                    }
                    if (stepCurrentValues != null && _prevStepValues != null)
                    {
                        for (int i = 0; i < stepCurrentValues.Length && i < _prevStepValues.Length; i++)
                        {
                            if (stepCurrentValues[i] != _prevStepValues[i])
                            {
                                string lbl = rtEntryLabels != null && i < rtEntryLabels.Length ? rtEntryLabels[i] : "?";
                                Log($"  StepChanged: entry={i} \"{lbl}\" {_prevStepValues[i]} -> {stepCurrentValues[i]}");
                            }
                        }
                    }
                }
                RestoreWorldState();
                SnapshotEntryState();
            }
            else
            {
                Log("  No state change detected, skipping RestoreWorldState");
            }

            // Momentary dispatch: replay one-shot actions from Momentary buttons
            // whose writes aren't captured by entryStates/stepCurrentValues.
            // Runs AFTER RestoreWorldState so that when a Momentary reset arrives
            // in the same sync batch as a step-value change (the common user
            // pattern: step the value up, then press center to reset), the
            // reset wins. A rapid reverse-order same-frame sequence (press
            // center then immediately "+") is a known edge case that
            // self-corrects on the next step press.
            if (!_momentaryDispatchBaselined)
            {
                // First deserialize on this client — record the sender's
                // current seq as the baseline so we don't spuriously replay
                // a historical press on late join.
                _prevMomentaryDispatchSeq   = momentaryDispatchSeq;
                _momentaryDispatchBaselined = true;
            }
            else if (momentaryDispatchSeq != _prevMomentaryDispatchSeq)
            {
                _prevMomentaryDispatchSeq = momentaryDispatchSeq;
                if (momentaryDispatchEntry >= 0
                    && entryStates != null
                    && momentaryDispatchEntry < entryStates.Length)
                {
                    string mLbl = rtEntryLabels != null && momentaryDispatchEntry < rtEntryLabels.Length
                                  ? rtEntryLabels[momentaryDispatchEntry] : "?";
                    Log($"  MomentaryDispatch: replaying entry={momentaryDispatchEntry} \"{mLbl}\"");
                    ExecuteEntryActions(momentaryDispatchEntry, true);
                }
            }

            UpdateDisplay();
            // Re-apply fader mode in case it changed while this client was absent
            if (faderSlots != null)
            {
                for (int i = 0; i < faderSlots.Length; i++)
                {
                    if (faderSlots[i] != null)
                        faderSlots[i].SetFaderMode(faderMode);
                }
            }
            // Sync hand collider tracking to match the (possibly changed) fader mode
            _handColliderTrackingEnabled = faderMode == 0 && _isLocalPlayerVR && CanLocalUserInteract();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            Log($"OnPlayerJoined() player={player.displayName} isMaster={Networking.IsMaster}");
            // On late join, the master re-sends the full synced state so the new player
            // can reconstruct the world correctly.
            if (!Networking.IsMaster) return;
            RequestSerialization();
        }

        // Log the actual wire size of each Manual-sync serialization. This is the
        // ground truth for diagnosing bandwidth issues — it's what VRChat actually
        // puts on the network. Compare against the 11 KB/s per-script budget to
        // compute the expected send cadence.
        public override void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
        {
            if (result.success)
                Log($"OnPostSerialization() SUCCESS byteCount={result.byteCount} (~{(result.byteCount / 11000.0):F2}s cooldown at 11KB/s)");
            else
                Log($"OnPostSerialization() FAILED byteCount={result.byteCount}");
        }

        private void Update()
        {
            // Auto-change folder cycling
            if (autoChangeActive)
            {
                _autoChangeTimer -= Time.deltaTime;
                if (_autoChangeTimer <= 0f)
                {
                    _autoChangeTimer = autoChangeInterval;
                    if (Networking.IsMaster)
                        CycleFolder(1);
                }
            }

            // Auto-change group entry cycling
            if (autoChangeGroupActive && autoChangeGroupId >= 0)
            {
                _autoChangeGroupTimer -= Time.deltaTime;
                if (_autoChangeGroupTimer <= 0f)
                {
                    _autoChangeGroupTimer = autoChangeGroupInterval;
                    if (Networking.IsMaster)
                        CycleAutoChangeGroup();
                }
            }

            // Tick delayed-action queue (skip entirely when nothing is pending)
            if (_delayQueueActiveCount > 0)
            {
                float dt = Time.deltaTime;
                for (int q = 0; q < kDelayQueueSize; q++)
                {
                    if (!_delayQueueOccupied[q]) continue;
                    _delayQueueTimer[q] -= dt;
                    if (_delayQueueTimer[q] <= 0f)
                    {
                        // (A) Read slot data before clearing so we can use it below.
                        bool   condSnap   = _delayQueueConditionSnapshot[q];
                        int    dEntryIdx  = _delayQueueEntryIdx[q];
                        int    dActionIdx = _delayQueueActionIdx[q];
                        bool   dActive    = _delayQueueActive[q];
                        // (G) Decrement the active count and clear the slot BEFORE executing.
                        // This keeps _delayQueueActiveCount accurate even if execution throws.
                        _delayQueueOccupied[q] = false;
                        _delayQueueActiveCount--;
                        // (A) Only fire if the condition was satisfied at schedule time.
                        // Using the snapshot bypasses the race condition where the referenced
                        // entry's state changes during the delay window.
                        if (condSnap)
                            ExecuteSingleActionCore(dEntryIdx, dActionIdx, dActive);
                    }
                }
            }

            // Tick entry-expire queue � auto-deactivate entries whose timer elapsed
            if (_expireQueueCount > 0)
            {
                float edt = Time.deltaTime;
                for (int q = 0; q < kExpireQueueSize; q++)
                {
                    if (!_expireQueueOccupied[q]) continue;
                    _expireQueueTimer[q] -= edt;
                    if (_expireQueueTimer[q] <= 0f)
                    {
                        int eIdx = _expireQueueEntryIdx[q];
                        _expireQueueOccupied[q] = false;
                        _expireQueueCount--;
                        // Only deactivate if the entry is still active (user may have manually toggled it off)
                        if (entryStates != null && eIdx < entryStates.Length && entryStates[eIdx])
                        {
                            HandleToggle(eIdx);
                            // Expire runs independently on every client without a network
                            // round-trip, so refresh _prev to match the locally-expired state.
                            // Without this, a later remote deserialize whose value happens to
                            // match the stale _prev would be mistaken for a no-op.
                            SnapshotEntryState();
                        }
                    }
                }
            }

            // Poll third-party whitelist integrations for changes
            MonitorWhitelistSources();

            // Move shared hand colliders to follow the local player's finger bones
            UpdateHandColliderPositions();

            // Controller-managed fader position drive (runs every frame regardless of auto-change)
            UpdateControlledFaderPositions();

            // World stats fetch tick (owner only, skip when nothing is configured)
            if (_statsInitialized && !_statsPendingRequest
                && Networking.IsOwner(gameObject)
                && !float.IsPositiveInfinity(_statsNextFetchTime)
                && Time.time + StatsTimingTolerance >= _statsNextFetchTime)
            {
                BeginStatsFetch();
            }

            if (_presetIncompatibleTimer > 0f)
            {
                _presetIncompatibleTimer -= Time.deltaTime;
                if (_presetIncompatibleTimer <= 0f)
                {
                    UpdateDisplay(); // Reset the labels back to normal
                }
            }
        }

        // ------------------------------------------------------------------------
        //  INITIALIZATION
        // ------------------------------------------------------------------------

        private void InitializeRuntimeState()
        {
            int totalEntries = rtEntryLabels != null ? rtEntryLabels.Length : 0;
            entryStates = new bool[totalEntries];
            stepCurrentValues = new float[totalEntries];

            // Initialize per-fader-link current values from defaults.
            int totalLinks = rtFaderLinkDefaultValues != null ? rtFaderLinkDefaultValues.Length : 0;
            faderLinkCurrentValues = new float[totalLinks];
            for (int i = 0; i < totalLinks; i++)
                faderLinkCurrentValues[i] = rtFaderLinkDefaultValues[i];

            colorPaletteCurrentIndices = new int[totalEntries];
            // Pending indices default to 0 (first palette color) � same as applied indices,
            // so the color selector starts in a consistent state before any user interaction.
            colorPalettePendingIndices = new int[totalEntries];

            // Variant Selector: per-entry pending/current indices (parallel to color palette indices).
            variantCurrentIndices = new int[totalEntries];
            variantPendingIndices = new int[totalEntries];

            for (int i = 0; i < totalEntries; i++)
            {
                entryStates[i] = rtEntryDefaultOn != null && i < rtEntryDefaultOn.Length && rtEntryDefaultOn[i];

                if (rtEntryButtonTypes != null && i < rtEntryButtonTypes.Length
                    && rtEntryButtonTypes[i] == 2) // Step
                {
                    // Use the action's configured default value for initialization
                    // instead of reading the (potentially stale) live material value.
                    stepCurrentValues[i] = ReadStepDefaultValue(i);
                }
            }

            // Execute default-on entries normally.
            for (int i = 0; i < totalEntries; i++)
            {
                if (entryStates[i])
                {
                    ExecuteEntryActions(i, true);
                    ScheduleEntryExpire(i);
                }
            }

            // For default-off entries, force-reset only stateful toggle actions
            // (types that use active as a direct on/off flag). This clears stale
            // material/object state that persists across play sessions without
            // incorrectly zeroing out shader properties like Saturation.
            ApplyDefaultsOff();

            // Apply default palette color (index 0) for entries with color palettes.
            if (rtColorPaletteStart != null && rtColorPaletteCount != null)
            {
                for (int i = 0; i < totalEntries; i++)
                {
                    int palCount = i < rtColorPaletteCount.Length ? rtColorPaletteCount[i] : 0;
                    if (palCount > 0)
                        ApplyColorCycleAtIndex(i, 0);
                }
            }

            // Activate Exclusive Off buttons for any group that has no default-on member.
            ActivateExclusiveOffButtons();

            // Note: preset storage allocation is no longer performed here.
            // It's handled by Start() via presetStorage.AllocateStorage() on every
            // client, since the storage arrays moved to the dedicated
            // EnigmaPresetStorage UdonSharpBehaviour.
        }

    }
}


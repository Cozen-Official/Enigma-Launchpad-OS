using System;
using UnityEngine;
using VRC.SDKBase;
using UdonSharp;

namespace Cozen.EnigmaOS
{
    // ════════════════════════════════════════════════════════════════════════════
    //  Action Model enums — declared at namespace level (outside any class) to
    //  satisfy UdonSharp's no-nested-type rule.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether an action tracks persistent state and whether the world
    /// reverts when the entry goes inactive.
    /// </summary>
    public enum ActionCategory
    {
        Toggle    = 0,  // Tracks on/off. World reverts to previous value when inactive.
        Command   = 1,  // Fire-and-forget. No state tracking; executes unconditionally on press.
        Selection = 2,  // Manages an active index within a named group (color palette, variant set).
        Preset    = 3,  // Snapshots or restores a set of entry, fader, step, and palette values.
        Display   = 4,  // Read-only. Renders live world data onto the button label.
        System    = 5,  // Controller navigation (folders, pages, reset).
    }

    /// <summary>
    /// VRChat world-stats metrics that can be displayed by a Display Stat action (type 21).
    /// Values mirror the Gen 2 WorldStatMetric enum so builds can be compared directly.
    /// </summary>
    public enum WorldStatMetric
    {
        Visits       = 0,   // API-polled
        Favorites    = 1,   // API-polled
        Occupancy    = 2,   // API-polled
        Popularity   = 3,   // API-polled
        Heat         = 4,   // API-polled
        Players      = 5,   // local: VRCPlayerApi.GetPlayerCount()
        Age          = 6,   // local: Networking.GetServerTimeInSeconds()
        Time         = 7,   // local: DateTime.Now
        VRUsers      = 8,   // local: count players in VR
        DesktopUsers = 9,   // local: count desktop players
        Capacity     = 10,  // API-polled
        PeakPlayers  = 11,  // local: peak during current instance
        InstanceMaster = 12,// local: Networking.GetOwner(gameObject).displayName
        Authenticated  = 13,// local: count authenticated players
    }

    /// <summary>What the operation acts on.</summary>
    public enum ActionTarget
    {
        Object          = 0,
        Renderer        = 1,
        Component       = 2,
        Material        = 3,
        ShaderProperty  = 4,
        Skybox          = 5,
        UdonBehaviour   = 6,
        Transform       = 7,
        Player          = 8,
        ColorGroup      = 9,
        VariantGroup    = 10,
        AutoChangeGroup = 11,
        PresetSlot      = 12,
        Controller      = 13,
        WorldStats      = 14,   // Display Stat (type 21) — VRChat world statistics
        ScreenShader    = 15,   // Screen Shader (type 26) — build-time template duplication
    }

    /// <summary>
    /// What to do to the target. The editor filters this to valid operations for each
    /// Category + Target combination.
    /// </summary>
    public enum ActionOperation
    {
        Toggle         = 0,
        Apply          = 1,
        Set            = 2,
        SetState       = 3,
        SetVariable    = 4,
        TriggerEvent   = 5,
        Teleport       = 6,
        Next           = 7,
        Previous       = 8,
        Select         = 9,
        Save           = 10,
        Load           = 11,
        SaveOrLoad     = 12,
        Clear          = 13,
        Show           = 14,
        NextFolder     = 15,
        PreviousFolder = 16,
        GoToFolder     = 17,
        NextPage       = 18,
        PreviousPage   = 19,
        GoToPage       = 20,
        Reset          = 21,
        SetFaderMode   = 22,
        ShowFolderName = 23,
        ShowPageNumber = 24,
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Editor-time data model
    //  These serializable classes are read/written by the custom inspector.
    //  They are NOT accessed by Udon at runtime — the build step flattens them
    //  into rt* flat arrays that Udon can consume.
    //
    //  Kept in a separate file so that UdonSharp only needs to compile the
    //  EnigmaController UdonSharpBehaviour in isolation, avoiding errors from
    //  complex [Serializable] types in the same compilation unit.
    // ════════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class EnigmaFolderData
    {
        public string name = "New Folder";
        public EnigmaEntryData[] entries = new EnigmaEntryData[0];
    }

    [Serializable]
    public class EnigmaEntryData
    {
        // ── Empty-slot sentinel ──
        // When true this slot is unoccupied.  The entry is kept in the array so
        // that every physical button always maps to a fixed array index.  The
        // build pipeline skips isEmpty entries; the preview grid draws them as
        // blank "+" cells.
        public bool isEmpty = false;

        // ── Identity ──
        public string label = "New Entry";

        // ── Button behavior ──
        // Kept for JSON import backward-compatibility. No longer shown in the editor UI.
        // The build pipeline now derives runtime button types automatically from the
        // assigned actions (see EnigmaControllerEditor.Build.cs).
        //   Legacy editor values: 0=Toggle, 1=Momentary, 2=DisplayOnly (ignored).
        public int buttonType = 0;

        // ── Default state ──
        public bool onByDefault = false;

        // ── Exclusive group (tag-based, VRCFury-inspired) ──
        public bool useExclusiveGroup = false;
        public string exclusiveGroup = "";
        // When true, this button is the designated "Exclusive Off" state for its exclusive group.
        // If all other group members are deactivated, this button auto-activates to represent
        // the "nothing selected" state.  Only one button per group should have this set;
        // the build pipeline warns if more than one is found in the same group.
        public bool exclusiveOff = false;

        // ── Autochange group ──
        // When enabled the entry is included in the named autochange pool.
        // An entry action of type 14 (Autochange Group) pointing to the same tag
        // will cycle through all members of the pool one at a time on a timer.
        public bool useAutoChangeGroup = false;
        public string autoChangeGroup = "";

        // ── Expire (entry-level) ──
        // When enabled, the entry auto-deactivates after expireSeconds on its
        // toggle-active state. Only takes effect for stateful (Toggle) buttons —
        // see EnigmaControllerEditor.IsStatefulActionType for what counts.
        // Warning: combining with Exclusive Off creates a reactivation loop.
        public bool  useExpire    = false;
        public float expireSeconds = 5f;

        // ── Actions ──
        // All entry actions live here. Step and Color Cycle behavior is driven by
        // action-level options (useStep on types 2/6, actionType 7 for Color Cycle).
        // Preset buttons are created by adding a Save/Recall Preset action (type 8).
        public EnigmaActionData[] actions = new EnigmaActionData[0];

        // ── Fader assignment ──
        public bool assignFader = false;
        public EnigmaFaderLinkData faderLink = new EnigmaFaderLinkData();
        public EnigmaFaderLinkData[] faderLinks = new EnigmaFaderLinkData[0];

        // ── Custom button color ──
        public bool useCustomColor = false;
        public Color customColor = Color.white;
        public bool useConditionalColor = false;
        public int condColorSourceType = 0;                    // 0=Material, 1=Udon, 2=UI Slider
        public Renderer condColorRenderer;
        public int condColorMaterialIndex = 0;
        public string condColorPropertyName = "";
        public bool condColorTargetsSkybox = false;
        public Material condColorSkyboxMaterial;
        public UdonSharp.UdonSharpBehaviour condColorUdonTarget;
        public string condColorUdonVariableName = "";
        public ConditionalColorRule[] condColorRules = new ConditionalColorRule[0];
    }

    [Serializable]
    public class EnigmaActionData
    {
        // Action types:
        // 0 = Toggle Object       (SetActive on/off)
        // 1 = Set Material        (swap material on renderer)
        // 2 = Set Shader Property (set float/color/vector on material; supports step)
        // 3 = (removed — use Toggle Component targeting a Renderer instead)
        // 4 = Apply Skybox       (RenderSettings.skybox = material; command/fire-once)
        // 22 = Toggle Skybox      (stateful; on=apply material, off=revert to scene-start skybox)
        // 5 = Trigger Udon Event  (SendCustomEvent on UdonBehaviour; fires on both activate AND deactivate when paired with toggle actions)
        // 6 = Set Udon Variable   (SetProgramVariable on UdonBehaviour; supports step)
        // 7 = Color Cycle         (cycles through a palette, shows current color on button)
        // 8 = Presets (presetRole: 0=Preset Slot, 1=Save Button, 2=Load Button, 3=Clear Button)
        // 9 = Display Value       (reads a shader property or Udon variable and shows it on a second line of the button label)
        // 11 = Toggle Component   (enable/disable a Behaviour on a GameObject)
        // 12 = Transform          (set/add position, rotation, or scale on a GameObject's Transform)
        // 13 = Teleport           (player: respawn/to vector/to transform; object: to vector/to transform)
        // 14 = Autochange Group   (toggle cycling entries tagged with the named autochange group on/off)
        public int actionType;

        // Basic action targets (types 0–6)
        public GameObject targetObject;
        public Renderer targetRenderer;
        public int materialIndex;
        public Material targetMaterial;
        // Toggle Material (actionType 1, category 0): the material restored on
        // deactivate. Editor auto-populates from sharedMaterials[materialIndex]
        // when the renderer is first assigned and the field is null. Unused for
        // category-1 (Apply Material) and other actionType 1 contexts where the
        // user wants a one-shot swap.
        public Material defaultMaterial;
        public string propertyName;
        public float propertyFloatValue;
        public Color propertyColorValue = Color.white;
        public Vector4 propertyVectorValue;
        public Texture targetTexture;

        // ── Auto-toggle (type 2 — Set Shader Property) ──
        // When true, the build pipeline emits a synthetic second action that
        // sets the section toggle property (e.g. _FilterModel = 1) detected
        // for this action's property via EnigmaShaderHelper.TryGetEffectToggle.
        // Default true so users get the auto-toggle behavior for free on
        // typical effect parameters; uncheck for power-level-style buttons
        // that should only change the value without enabling the section.
        // The action drawer hides the checkbox entirely when no toggle is
        // detected, when the action's property already IS the section toggle,
        // or when the action isn't a Set Shader Property.
        public bool alsoSetEffectToggle = true;

        // ── Default values (type 2 — applied when action is deactivated / at init) ──
        public float defaultFloatValue = 0f;
        public Color defaultColorValue = Color.white;
        public Vector4 defaultVectorValue;
        public int propertyType;            // types 2/7: 0=Float, 1=Color, 2=Vector, 3=Texture  |  type 9: 0=Float, 1=Bool, 2=Int, 3=String, 4=Color, 5=Vector
        public UdonSharpBehaviour targetUdon;
        public string udonEventName;
        public string udonVariableName;
        // For action type 6 (Set Udon Variable):
        //   0=bool, 1=float, 2=int, 3=string
        public int udonVariableType = 1;
        public string udonVariableStringValue;

        // ── Toggle Component settings (type 11) ──
        // targetObject is reused as the source GameObject for the component picker UI.
        // targetComponent stores the specific Behaviour to enable/disable.
        public Behaviour targetComponent;

        // ── Trigger Udon Event scope (type 5) ──
        // 0 = All Players (SendCustomNetworkEvent NetworkEventTarget.All)
        // 1 = Owner       (SendCustomNetworkEvent NetworkEventTarget.Owner)
        // 2 = Local       (SendCustomEvent — same client only)
        public int udonEventScope = 0;

        // ── Transform settings (type 12) ──
        // Reuses targetObject (GameObject to transform), propertyVectorValue (x/y/z value),
        // and propertyType (transform mode below).
        // Transform mode (propertyType for type 12):
        //   0 = Set Position, 1 = Set Rotation (Euler), 2 = Set Scale,
        //   3 = Add Position,  4 = Add Rotation (Euler)
        // transformSpace: 0 = World, 1 = Local  (scale is always local)
        public int transformSpace = 0;

        // ── Teleport settings (type 13) ──
        // Reuses propertyType (teleport mode below), propertyVectorValue (position for modes 1/3),
        // and targetObject (destination transform for mode 2, source object for modes 3/4).
        // Teleport mode (propertyType for type 13):
        //   Player (target == 8):
        //     0 = Respawn to Spawn Origin  (player.Respawn())
        //     1 = Teleport to Vector       (explicit position + rotation; player)
        //     2 = Teleport to Transform    (use targetObject's world position/rotation; player)
        //   Object (target == 0):
        //     3 = Teleport Object to Vector     (move targetObject to explicit position)
        //     4 = Teleport Object to Transform  (move targetObject to teleportDestination's position/rotation)
        public Vector3 teleportRotationEuler;  // Euler rotation used in player mode 1
        public GameObject teleportDestination; // Destination transform used in object mode 4

        // ── Step option (types 2 and 6 only) ──
        // When enabled, each press increments the current value by stepAmount
        // (wrapping back to stepMin when stepMax is exceeded).
        public bool useStep = false;
        public float stepAmount = 0.1f;
        public float stepMin = 0f;
        public float stepMax = 1f;
        // Default Wrap to true so stepped properties loop back to stepMin
        // when they exceed stepMax (and vice-versa). Non-wrapping clamps at
        // the bounds which is rarely what the user actually wants for the
        // typical "cycle through N levels" pattern — they'd see the step
        // button stop responding at the top of the range. Wrap makes the
        // out-of-the-box behaviour match the common case; users who need
        // clamping can untick it.
        public bool  stepWrap = true;

        // ── Delay option (any action type) ──
        // When enabled the action fires after delaySeconds instead of immediately.
        public bool  useDelay    = false;
        public float delaySeconds = 1f;
        // When false (default), the delay only applies on activation; deactivation
        // executes immediately. When true, the delay also applies on deactivation
        // (matching the legacy behaviour). Per-action, only meaningful when useDelay
        // is also true.
        public bool  delayOnDeactivate = false;

        // ── Lerp option (type 2 — Set Shader Property, float/color/vector) ──
        // When enabled, activation fades the property from its CURRENT value to
        // the target value over lerpSeconds instead of snapping. Composes with
        // Delay (the fade starts when the delayed action fires). Mirrors the
        // Delay option's activation/deactivation split: by default deactivation
        // snaps to the default value immediately; lerpOnDeactivate fades the
        // default back in over the same duration.
        public bool  useLerp     = false;
        public float lerpSeconds = 1f;
        public bool  lerpOnDeactivate = false;

        // Expire moved to EnigmaEntryData (controller) and EnigmaButton (standalone).
        // The action no longer carries an expire field — expire deactivates the whole
        // entry/button, not a single action, so it lives at that level.

        // ── Color Cycle settings (type 7) ──
        public Color[] paletteColors = new Color[0];
        public Renderer colorTargetRenderer;
        public int colorMaterialIndex;
        public string colorPropertyName;

        // ── Color Selector settings (type 10) ──
        // colorSelectorRole: 0 = Color Display (shows applied color), 1 = Set Color (shows pending, applies on press),
        //                    2 = Change Color (advances pending on linked entry)
        // colorGroupName: shared tag linking roles 0/2 to the role-1 entry in the same folder.
        //                 For role 1 this is the group name that Display/Change Color entries reference.
        // Role 1 reuses colorTargetRenderer, colorMaterialIndex, colorPropertyName, paletteColors (same as type 7).
        public int colorSelectorRole = 0;
        public string colorGroupName = "";

        // ── Preset settings (type 8) ──
        // presetRole: 0 = Preset Slot, 1 = Save Button, 2 = Load Button, 3 = Clear Button
        public int presetRole = 0;
        // 0 = All folders, 1 = Selected folders only
        public int presetScope = 0;
        public int[] presetIncludedFolderIndices = new int[0];
        public bool presetIncludeFaders = true;
        public bool presetIncludeStepValues = true;
        public bool presetIncludeColorPalettes = true;
        public bool presetIncludeVariantGroups = true;

        // ── Autochange Group settings (type 14) ──
        // autoChangeGroupName: the autochange group tag whose members should be cycled.
        //   Must match the Autochange Group tag set on the target entries.
        // autoChangeGroupInterval: seconds between each advance when cycling is active.
        // autoChangeGroupRandom: when true, the next member is chosen randomly (not consecutively).
        // To start the cycle at world load, set the owning entry/button's
        // `onByDefault = true` — the entry's normal default-on dispatch will
        // fire this action with active=true on master Start(), which calls
        // StartAutoChangeGroup() and sets the button's state to "on" the same
        // way a user press would.
        public string autoChangeGroupName = "";
        public float  autoChangeGroupInterval = 10f;
        public bool   autoChangeGroupRandom = false;

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION MODEL FIELDS (Category + Target + Operation)
        //  category/target/operation drive the editor UI; actionType is kept in
        //  sync by SyncActionType() and is what the Udon runtime reads.
        // ════════════════════════════════════════════════════════════════════════
        public int category  =  0;  // ActionCategory enum value
        public int target    =  0;  // ActionTarget enum value
        public int operation =  0;  // ActionOperation enum value

        // ── Nav embedded parameters (actionType 20 / System category) ──
        // folderTarget: folder index used by GoToFolder.
        // pageTarget:   page index used by GoToPage.
        public int navFolderTarget     = 0;
        public int navPageTarget       = 0;
        public int navFaderPageTarget  = 0;

        // ── VariantGroup parameters (Selection / VariantGroup) ──
        // variantSelectorRole: 0 = Variant Display (shows applied variant name), 1 = Set Variant (applies pending, owns item list),
        //                      2 = Change Variant (advances pending on linked role-1 entry)
        // variantGroupName: shared tag linking roles 0/2 to the role-1 entry in the same folder.
        //                   For role 1 this is the group name that Variant Display and Change Variant entries reference.
        // Role 1 reuses targetRenderer, materialIndex, propertyName, propertyType (same fields as type 2).
        public int variantSelectorRole = 0;
        public string variantGroupName = "";
        public EnigmaVariantItem[] variantItems = new EnigmaVariantItem[0];

        // ── Command SetState target value ──
        // Used by actionTypes 15 (Object SetState), 16 (Component SetState),
        // 17 (AutoChangeGroup SetState), 18 (Controller/Whitelist SetState).
        // The build pipeline bakes this into rtActionFloatValues (1f = on, 0f = off).
        public bool commandTargetState = true;

        // ── Screen Shader settings (type 26) ──
        // shaderTemplateIndex: which EnigmaShaderTemplate in the scene to duplicate.
        // targetMaterial is reused as the material to assign to the duplicated MeshRenderer.
        public int shaderTemplateIndex = 1;

        // ── Display Stat settings (type 21) ──
        // statMetric: WorldStatMetric enum value (int). Determines which VRChat world stat
        // is shown on the button label as "<MetricName>\n<value>".
        public int statMetric = 0;

        // ── Per-action Momentary option ──
        // When true, the action always treats activation as a one-shot press and
        // does not contribute to the entry's persistent toggle state.
        public bool useMomentary = false;

        // ── Condition option (inspired by CyanTrigger conditional logic) ──
        // When enabled, the action only executes if the referenced entry's current
        // active state matches conditionRequireActive.  If the condition is not
        // satisfied the action is skipped entirely — no state change occurs.
        //
        // conditionFolderIndex — index of the folder that contains the entry to test.
        // conditionEntryIndex  — index within folder.entries[] (raw, may include empty
        //                        slots). The build step resolves this to a global entry
        //                        index stored in rtActionConditionEntryIndex[].
        // conditionRequireActive — true  → run only when the referenced entry IS active.
        //                          false → run only when the referenced entry is inactive.
        public bool useCondition            = false;
        public int  conditionFolderIndex    = 0;
        public int  conditionEntryIndex     = 0;
        public bool conditionRequireActive  = true;

        // ── Conditional Coloring (type 9 — Display Value) ──
        // When enabled, the button color is determined by evaluating the displayed
        // value against a list of rules. The first matching rule wins.
        // Conditions: 0=Less(<), 1=Greater(>), 2=Equal(=), 3=LessEqual(≤), 4=GreaterEqual(≥)
        public bool useConditionalColoring = false;
        public ConditionalColorRule[] conditionalColorRules = new ConditionalColorRule[0];

        // ── Fader link association ──
        // Non-zero value links this action to fader link(s) sharing the same ID.
        // Used by the editor to visually indicate linked pairs and auto-sync
        // renderer/material changes between the action and its fader link(s).
        public int faderLinkId = 0;
    }

    [Serializable]
    public class ConditionalColorRule
    {
        // 0=<, 1=>, 2==, 3=≤, 4=≥
        // Default to `==` (index 2): when users enable conditional colouring
        // on a button, the most common case is colouring the button when the
        // watched value hits a specific target (e.g. colour red when
        // `_Brightness == 1`). Greater-than was the previous default but
        // required every new rule to be manually flipped to equality first
        // in the typical use case.
        public int   condition = 2;
        public float value     = 0f;
        public Color color     = Color.white;
    }

    [Serializable]
    public class EnigmaVariantItem
    {
        public string variantName = "Variant";
        public float     floatValue   = 0f;
        public Color     colorValue   = Color.white;
        public Vector4   vectorValue  = Vector4.zero;
        public Texture   textureValue = null;
    }

    [Serializable]
    public class EnigmaFaderLinkData
    {
        public string faderName = "";
        public Renderer targetRenderer;
        public int materialIndex;
        public string propertyName;
        // FADER convention: 0=Float, 1=Color (Vector/Texture not supported;
        // see EnigmaFader.propertyType for the canonical doc). Read at
        // runtime by EnigmaPlayModeHook's default-application pass and by
        // EnigmaController.Faders.cs when binding the link to a slot.
        public int propertyType;
        public float minValue = 0f;
        public float maxValue = 1f;
        public float defaultValue = 0f;
        public Color defaultColor = Color.white;
        public bool colorIndicatorEnabled = false;
        public Color indicatorColor = Color.white;
        public bool indicatorConditional = false;

        // UI Slider targets
        public bool targetsSlider = false;
        public UnityEngine.UI.Slider[] targetSliders;
        public bool[] sliderDirectionsReversed;

        // Skybox target
        public bool targetsSkybox = false;
        public Material skyboxMaterial; // Editor-only: the specific skybox material for property search

        // Udon variable targets
        public bool targetsUdon = false;
        public UdonSharpBehaviour[] targetUdonBehaviours;
        public string udonVariableName = "";

        // Link ID — matches the faderLinkId on the associated action.
        public int faderLinkId = 0;
    }
}

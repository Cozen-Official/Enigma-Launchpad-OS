
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Centralized action executor for Enigma OS. Holds all action-indexed runtime
    /// arrays and the unified ExecuteAction() method containing all action type handlers.
    ///
    /// Both EnigmaController and EnigmaButton delegate action execution to this component.
    /// The build pipeline creates an EnigmaExecutor on each controller/button GameObject
    /// and writes the compiled action arrays to it.
    ///
    /// This is the single source of truth for action execution logic — new action types
    /// only need to be added here, not in both controller and button.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EnigmaExecutor : UdonSharpBehaviour
    {
        // --------------------------------------------------------------------
        //  LINKED CONTROLLER
        // --------------------------------------------------------------------

        /// <summary>
        /// Reference to the linked controller. For controller-managed executors, this
        /// points to the controller on the same GameObject. For standalone button executors,
        /// this points to the button's linked controller (may be null).
        /// Required for types 10, 14, 17, 18, 19, 20.
        /// </summary>
        [HideInInspector] public EnigmaController linkedController;

        // --------------------------------------------------------------------
        //  ACTION-INDEXED RUNTIME ARRAYS (populated by build step)
        // --------------------------------------------------------------------

        [HideInInspector] public int[]   rtActionTypes               = new int[0];
        [HideInInspector] public GameObject[] rtActionTargetObjects   = new GameObject[0];
        [HideInInspector] public Renderer[]   rtActionTargetRenderers = new Renderer[0];
        [HideInInspector] public int[]   rtActionMaterialIndices     = new int[0];
        [HideInInspector] public Material[]   rtActionMaterials      = new Material[0];
        // Default material for Toggle Material (actionType 1, category 0).
        // Restored to the renderer slot on entry deactivate. Authored or
        // auto-populated in the editor; baked here at build time alongside
        // rtActionMaterials so the runtime can swap back without snapshotting.
        [HideInInspector] public Material[]   rtActionDefaultMaterials = new Material[0];
        [HideInInspector] public string[] rtActionPropertyNames      = new string[0];
        [HideInInspector] public float[]  rtActionFloatValues        = new float[0];
        [HideInInspector] public Color[]  rtActionColorValues        = new Color[0];
        [HideInInspector] public Vector4[] rtActionVectorValues      = new Vector4[0];
        [HideInInspector] public Texture[] rtActionTextures          = new Texture[0];
        [HideInInspector] public float[]  rtActionDefaultFloatValues  = new float[0];
        [HideInInspector] public Color[]  rtActionDefaultColorValues  = new Color[0];
        [HideInInspector] public Vector4[] rtActionDefaultVectorValues = new Vector4[0];
        [HideInInspector] public int[]   rtActionPropertyTypes       = new int[0];
        [HideInInspector] public UdonSharpBehaviour[] rtActionUdonTargets = new UdonSharpBehaviour[0];
        [HideInInspector] public string[] rtActionUdonEventNames     = new string[0];
        [HideInInspector] public string[] rtActionUdonVariableNames        = new string[0];
        [HideInInspector] public int[]    rtActionUdonVariableTypes        = new int[0];
        [HideInInspector] public string[] rtActionUdonVariableStringValues = new string[0];
        [HideInInspector] public float[]  rtActionDelaySeconds       = new float[0];
        // Per-action: when false (default), delay only applies on activation;
        // deactivation runs immediately. When true, delay applies on both.
        [HideInInspector] public bool[]   rtActionDelayOnDeactivate  = new bool[0];
        // rtActionExpireSeconds removed — expire is now per-entry, see
        // EnigmaController.rtEntryExpireSeconds and EnigmaButton.expireSeconds.
        [HideInInspector] public int[]   rtActionUdonEventScopes     = new int[0];
        [HideInInspector] public int[]   rtActionTransformSpaces     = new int[0];
        [HideInInspector] public Vector3[] rtActionTeleportRotations = new Vector3[0];
        [HideInInspector] public GameObject[] rtActionTeleportDestinations = new GameObject[0];
        [HideInInspector] public int[]  rtActionStatMetrics          = new int[0];
        [HideInInspector] public bool[] rtActionHasCondition         = new bool[0];
        [HideInInspector] public int[]  rtActionConditionEntryIndex  = new int[0];
        [HideInInspector] public bool[] rtActionConditionRequireActive = new bool[0];

        // Per-action non-stateful flag. Category 1 (Set) actions fire on activate
        // but do NOT reset to default on deactivate. Used for toggle properties on
        // step rows (e.g., _FilterModel=1 alongside _Saturation step).
        [HideInInspector] public bool[] rtActionNonStateful = new bool[0];

        // Per-action step flag. True for actions with useStep=true in the editor.
        // Used by HandleStep to write the computed step value to the correct action
        // regardless of action order within the entry.
        [HideInInspector] public bool[] rtActionUseStep = new bool[0];

        // Auto-detected shader_feature_local keywords for type 2 (Set Shader Property) actions.
        // Baked at build time from EnigmaShaderHelper.GetPropertyKeywordInfo().
        [HideInInspector] public string[] rtActionKeywords      = new string[0]; // keyword name, or "" if none
        [HideInInspector] public string[] rtActionKeywordToggles = new string[0]; // toggle property name, or "" if none
        [HideInInspector] public bool[] rtActionIsKeywordToggle = new bool[0]; // pre-baked: true when action targets the keyword's toggle property
        [HideInInspector] public int[]  rtActionColorSelectorRoles   = new int[0];
        [HideInInspector] public int[]  rtActionVariantSelectorRoles = new int[0];
        [HideInInspector] public int[]   rtActionAutoChangeGroupIds  = new int[0];
        [HideInInspector] public float[] rtActionAutoChangeIntervals = new float[0];
        [HideInInspector] public bool[]  rtAutoChangeGroupRandom     = new bool[0];

        // --------------------------------------------------------------------
        //  PER-ACTION CONDITIONAL COLORING (standalone buttons only)
        //  Controller uses per-entry conditional coloring on the controller itself.
        // --------------------------------------------------------------------

        [HideInInspector] public int[]   rtCondColorStart      = new int[0];   // per-action start index
        [HideInInspector] public int[]   rtCondColorCount      = new int[0];   // per-action rule count
        [HideInInspector] public int[]   rtCondColorConditions = new int[0];   // flat: 0=<, 1=>, 2==, 3=≤, 4=≥
        [HideInInspector] public float[] rtCondColorValues     = new float[0]; // flat: threshold
        [HideInInspector] public Color[] rtCondColorColors     = new Color[0]; // flat: color when matched

        // --------------------------------------------------------------------
        //  PRIVATE STATE
        // --------------------------------------------------------------------

        /// <summary>Scene-start skybox for Toggle Skybox (type 22) revert.</summary>
        private Material _initialSkybox;

        /// <summary>Saved transform values for Toggle Transform (type 23) revert.</summary>
        private Vector4[] _savedTransformValues;

        // --------------------------------------------------------------------
        //  PUBLIC API
        // --------------------------------------------------------------------

        /// <summary>
        /// Captures initial state needed for action execution.
        /// Must be called by the owning controller or button in Start() before any
        /// actions are executed.
        /// </summary>
        public void Initialize()
        {
            _initialSkybox = RenderSettings.skybox;
            if (rtActionTypes != null && rtActionTypes.Length > 0)
                _savedTransformValues = new Vector4[rtActionTypes.Length];
        }

        /// <summary>
        /// Returns the baked boolean target state for Command/SetState actions
        /// (types 15–18). The build pipeline stores the desired on/off value as
        /// <c>rtActionFloatValues[a] >= 0.5f</c>.
        /// </summary>
        public bool GetBakedTargetState(int a)
        {
            return rtActionFloatValues != null && a < rtActionFloatValues.Length
                   && rtActionFloatValues[a] >= 0.5f;
        }

        /// <summary>
        /// Applies the default state for all stateful material-affecting actions
        /// in the given range. Used both at runtime initialization (active=false
        /// to clear stale state) and in the editor when exiting play mode
        /// (active=true for default-on entries, active=false for the rest).
        ///
        /// Only stateful toggle types are processed (0, 2, 11, 27).
        /// Type 22 (Toggle Skybox) is included only when active=true because
        /// the off-state reverts to _initialSkybox which is unset in edit mode.
        /// Type 23 (Toggle Transform) is always excluded — revert reads
        /// _savedTransformValues which are empty at init.
        /// </summary>
        public void ApplyDefaults(int actStart, int actCount, bool active)
        {
            if (rtActionTypes == null) return;

            // When deactivating, skip shader property actions (type 2) if the
            // default value arrays aren't populated. Without correct defaults,
            // ExecuteAction falls back to 0 which zeroes out material properties
            // that may already be at the right value from the asset file.
            bool hasDefaults = rtActionDefaultFloatValues != null
                               && rtActionDefaultFloatValues.Length > 0;

            int end = actStart + actCount;
            for (int a = actStart; a < end; a++)
            {
                if (a >= rtActionTypes.Length) break;
                int t = rtActionTypes[a];
                if (!active && t == 2 && !hasDefaults) continue;
                if (t == 0 || t == 2 || t == 27 || (t == 22 && active))
                    ExecuteAction(-1, a, active, true);
            }
        }

        /// <summary>
        /// Execute a single action by its global index in the flat action arrays.
        /// This is the unified action handler — all action types are dispatched here.
        ///
        /// Parameters:
        ///   entryIdx      — the global entry index on the linked controller (-1 for standalone buttons)
        ///   a             — the global action index in the flat arrays
        ///   active        — true for activation, false for deactivation
        ///   isToggleEntry — true when the owning entry/button is a toggle (affects type 5 behavior)
        /// </summary>
        public void ExecuteAction(int entryIdx, int a, bool active, bool isToggleEntry)
        {
            if (rtActionTypes == null || a >= rtActionTypes.Length) return;

            int type = rtActionTypes[a];

            if (type == 0) // Toggle Object
            {
                if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                    && rtActionTargetObjects[a] != null)
                    rtActionTargetObjects[a].SetActive(active);
            }
            else if (type == 1) // Toggle / Apply Material
            {
                // Active state: swap to rtActionMaterials[a] (the configured
                //   target material).
                // Inactive state: swap to rtActionDefaultMaterials[a] when set,
                //   otherwise leave the renderer alone (matches pre-default-
                //   material behaviour for legacy actions / category-1 Apply).
                //
                // Apply Material (category 1) actions never deactivate state-
                // wise — they're momentary one-shots — so this branch only
                // matters for Toggle Material (category 0) where the entry
                // toggles its active state.
                if (rtActionTargetRenderers == null || a >= rtActionTargetRenderers.Length) return;
                Renderer rend = rtActionTargetRenderers[a];
                if (rend == null) return;

                int idx = rtActionMaterialIndices != null && a < rtActionMaterialIndices.Length
                          ? rtActionMaterialIndices[a] : -1;
                Material[] mats = rend.sharedMaterials;
                if (idx < 0 || idx >= mats.Length) return;

                Material targetMat = active
                    ? (rtActionMaterials != null && a < rtActionMaterials.Length ? rtActionMaterials[a] : null)
                    : (rtActionDefaultMaterials != null && a < rtActionDefaultMaterials.Length ? rtActionDefaultMaterials[a] : null);

                // Skip the slot write entirely when target is null. For active=
                // true that preserves the legacy behaviour of "no material =
                // no swap." For active=false (deactivate) it lets entries that
                // never had a default material configured behave like the old
                // one-shot swap — pressing again-and-off doesn't clobber the
                // slot with null.
                if (targetMat == null) return;

                mats[idx] = targetMat;
                rend.sharedMaterials = mats;
            }
            else if (type == 2) // Set Shader Property
            {
                // Non-stateful actions (category 1 / Set) only fire on activate.
                // They do NOT reset to default on deactivate.
                if (!active && rtActionNonStateful != null && a < rtActionNonStateful.Length
                    && rtActionNonStateful[a])
                {
                    // …with one exception: synthetic section-toggle actions whose
                    // keyword lives in Mochie's "Always" shader pass. That pass is
                    // NOT value-gated by its toggle property (_SST) — the overlay
                    // draws whenever the pass is enabled — so skipping deactivation
                    // entirely leaves the pass running after the entry's primary
                    // texture action reverts _ScreenTex to None, which renders the
                    // shader's "white" fallback texture fullscreen. Mirror Mochie's
                    // own inspector and disable the pass — unless another active
                    // entry on the linked controller still drives the same keyword
                    // on this material (e.g. a default-on overlay sibling during
                    // init's ApplyDefaultsOff sweep). Exclusive-group switches stay
                    // correct because peers deactivate BEFORE the pressed entry
                    // activates, so the incoming overlay re-enables the pass in the
                    // same frame.
                    bool nsKwToggle = rtActionIsKeywordToggle != null && a < rtActionIsKeywordToggle.Length
                        && rtActionIsKeywordToggle[a]
                        && rtActionKeywords != null && a < rtActionKeywords.Length;
                    if (nsKwToggle)
                    {
                        string nsKw = rtActionKeywords[a];
                        if (nsKw == "_IMAGE_OVERLAY_ON" || nsKw == "_IMAGE_OVERLAY_DISTORTION_ON")
                        {
                            Renderer nsRend = rtActionTargetRenderers != null && a < rtActionTargetRenderers.Length
                                              ? rtActionTargetRenderers[a] : null;
                            if (nsRend != null)
                            {
                                int nsMatIdx = rtActionMaterialIndices != null && a < rtActionMaterialIndices.Length
                                               ? rtActionMaterialIndices[a] : 0;
                                Material nsMat = null;
                                if (nsMatIdx == 0)
                                {
                                    nsMat = nsRend.sharedMaterial;
                                }
                                else
                                {
                                    Material[] nsMats = nsRend.sharedMaterials;
                                    if (nsMatIdx < nsMats.Length) nsMat = nsMats[nsMatIdx];
                                }
                                if (nsMat != null)
                                {
                                    bool stillUsed = linkedController != null
                                        && linkedController.IsKeywordUsedByActiveEntry(nsKw, nsRend, nsMatIdx);
                                    if (!stillUsed)
                                        nsMat.SetShaderPassEnabled("Always", false);
                                }
                            }
                        }
                    }
                    return;
                }

                if (rtActionTargetRenderers != null && a < rtActionTargetRenderers.Length
                    && rtActionTargetRenderers[a] != null)
                {
                    Renderer rend = rtActionTargetRenderers[a];
                    int      matIdx  = rtActionMaterialIndices != null && a < rtActionMaterialIndices.Length ? rtActionMaterialIndices[a] : 0;
                    int      propType = rtActionPropertyTypes[a];
                    string   propName = rtActionPropertyNames[a];

                    // Use sharedMaterial (singular) for index 0 — VRChat's Udon VM
                    // may return copies from sharedMaterials (plural) that don't
                    // affect the actual rendering material.
                    Material mat = null;
                    if (matIdx == 0)
                    {
                        mat = rend.sharedMaterial;
                    }
                    else
                    {
                        Material[] mats = rend.sharedMaterials;
                        if (matIdx < mats.Length) mat = mats[matIdx];
                    }

                    if (mat != null)
                    {
                        if (propType == 0)      // Float / Int
                        {
                            float def = rtActionDefaultFloatValues != null && a < rtActionDefaultFloatValues.Length
                                        ? rtActionDefaultFloatValues[a] : 0f;
                            float setVal = active ? rtActionFloatValues[a] : def;
                            // SetFloat writes the property. SetInt is also needed for
                            // properties declared as Int in the shader — Mochie and
                            // others declare many properties that way, and SetFloat
                            // alone may not update the int uniform buffer in VRChat
                            // standalone builds.
                            //
                            // IMPORTANT: Only call SetInt when the value is exactly an
                            // integer. Unity's Material class aliases SetFloat/SetInt
                            // onto the same underlying uniform slot, so calling SetInt
                            // with a truncated value (e.g. (int)0.15f == 0) would
                            // clobber a Float-declared property that was just written
                            // with SetFloat. Int-declared shader properties only ever
                            // receive integer values from the template, so this check
                            // is safe — they still hit the SetInt path. Float-declared
                            // properties with fractional values (e.g. _Invert=0.01,
                            // _AuraStr=0.15, _SobelFilterOpacity=0.5) skip SetInt and
                            // retain their SetFloat value.
                            mat.SetFloat(propName, setVal);
                            int intVal = (int)setVal;
                            if (setVal == (float)intVal)
                                mat.SetInt(propName, intVal);
                        }
                        else if (propType == 1)  // Color
                        {
                            Color def = rtActionDefaultColorValues != null && a < rtActionDefaultColorValues.Length
                                        ? rtActionDefaultColorValues[a] : Color.white;
                            mat.SetColor(propName, active ? rtActionColorValues[a] : def);
                        }
                        else if (propType == 2)  // Vector
                        {
                            Vector4 def = rtActionDefaultVectorValues != null && a < rtActionDefaultVectorValues.Length
                                          ? rtActionDefaultVectorValues[a] : Vector4.zero;
                            mat.SetVector(propName, active ? rtActionVectorValues[a] : def);
                        }
                        else if (propType == 3)  // Texture
                        {
                            Texture tex = rtActionTextures != null && a < rtActionTextures.Length
                                          ? rtActionTextures[a] : null;
                            mat.SetTexture(propName, active ? tex : null);
                        }

                        // If this action IS the keyword toggle for its group,
                        // enable the keyword when the property goes non-zero.
                        // IMPORTANT: Only ENABLE, never disable. shader_feature_local
                        // variants may be unloaded by DisableKeyword at runtime, making
                        // them impossible to re-enable. Effect visibility is controlled
                        // purely by the property value (e.g., _OutlineType=0 hides the
                        // outline even with _OUTLINE_ON enabled).
                        bool kwToggle = rtActionIsKeywordToggle != null && a < rtActionIsKeywordToggle.Length
                            && rtActionIsKeywordToggle[a];
                        bool kwValid = rtActionKeywords != null && a < rtActionKeywords.Length
                            && !string.IsNullOrEmpty(rtActionKeywords[a]);
                        if (kwToggle && kwValid)
                        {
                            float val = active ? rtActionFloatValues[a]
                                : (rtActionDefaultFloatValues != null && a < rtActionDefaultFloatValues.Length
                                    ? rtActionDefaultFloatValues[a] : 0f);
                            if (val > 0.5f)
                            {
                                mat.EnableKeyword(rtActionKeywords[a]);
                                bool kwAfter = mat.IsKeywordEnabled(rtActionKeywords[a]);
                                Debug.Log($"[Enigma] EnableKeyword '{rtActionKeywords[a]}' on '{mat.name}' rend='{rend.gameObject.name}' (a={a} val={val} kwEnabled={kwAfter} matID={mat.GetInstanceID()})");
                            }

                            // ── Mochie ScreenFX "Always" pass runtime toggle ──
                            // Mochie's Image Overlay (and Zoom/Letterbox) live in a
                            // dedicated "Always" shader pass that the shader itself does
                            // NOT value-gate on _SST — the overlay renders unconditionally
                            // whenever the pass runs AND the _IMAGE_OVERLAY_ON variant is
                            // active. Mochie's custom inspector manages this by calling
                            // SetShaderPassEnabled("Always", ...) based on _SST/_Zoom/
                            // _Letterbox state. Replicate that here so runtime presses
                            // correctly turn the overlay on and off. The editor-side
                            // ApplyMaterialFixups leaves the pass disabled as baseline
                            // and the keyword pre-enabled so the variant ships in the
                            // build; the runtime only toggles the pass.
                            if (rtActionKeywords[a] == "_IMAGE_OVERLAY_ON"
                                || rtActionKeywords[a] == "_IMAGE_OVERLAY_DISTORTION_ON")
                            {
                                mat.SetShaderPassEnabled("Always", active);
                            }
                        }
                        else if (kwToggle && !kwValid)
                        {
                            int kwLen = rtActionKeywords != null ? rtActionKeywords.Length : -1;
                            Debug.LogWarning($"[Enigma] Keyword toggle a={a} but keyword string invalid! rtActionKeywords null={rtActionKeywords == null} len={kwLen}");
                        }
                    }
                }
            }
            else if (type == 27) // Shader Keyword (Toggle or Set)
            {
                if (rtActionTargetRenderers != null && a < rtActionTargetRenderers.Length
                    && rtActionTargetRenderers[a] != null)
                {
                    Renderer kwRend = rtActionTargetRenderers[a];
                    int kwMatIdx = rtActionMaterialIndices != null && a < rtActionMaterialIndices.Length ? rtActionMaterialIndices[a] : 0;
                    string keyword = rtActionPropertyNames != null && a < rtActionPropertyNames.Length ? rtActionPropertyNames[a] : null;
                    Material kwMat = kwMatIdx == 0 ? kwRend.sharedMaterial : null;
                    if (kwMat == null)
                    {
                        Material[] kwMats = kwRend.sharedMaterials;
                        if (kwMatIdx < kwMats.Length) kwMat = kwMats[kwMatIdx];
                    }
                    if (kwMat != null && !string.IsNullOrEmpty(keyword))
                    {
                        bool enable = active;
                        int pType = rtActionPropertyTypes != null && a < rtActionPropertyTypes.Length ? rtActionPropertyTypes[a] : 0;
                        if (pType == 1) // Set mode: baked target state
                            enable = GetBakedTargetState(a);

                        if (enable)
                            kwMat.EnableKeyword(keyword);
                        else
                            kwMat.DisableKeyword(keyword);
                    }
                }
            }
            else if (type == 4) // Apply Skybox (command, fire-once)
            {
                if (active && rtActionMaterials != null && a < rtActionMaterials.Length
                    && rtActionMaterials[a] != null)
                    RenderSettings.skybox = rtActionMaterials[a];
            }
            else if (type == 22) // Toggle Skybox (stateful: on=apply, off=revert to scene-start)
            {
                if (active)
                {
                    if (rtActionMaterials != null && a < rtActionMaterials.Length
                        && rtActionMaterials[a] != null)
                        RenderSettings.skybox = rtActionMaterials[a];
                }
                else
                {
                    RenderSettings.skybox = _initialSkybox;
                }
            }
            else if (type == 5) // Trigger Udon Event
            {
                // For toggle entries the event fires on both activate AND deactivate.
                // For momentary (or pure-trigger) entries it only fires on press (active=true).
                if ((active || isToggleEntry)
                    && rtActionUdonTargets != null && a < rtActionUdonTargets.Length
                    && rtActionUdonTargets[a] != null)
                {
                    int scope = rtActionUdonEventScopes != null && a < rtActionUdonEventScopes.Length
                                ? rtActionUdonEventScopes[a] : 0;
                    string eventName = rtActionUdonEventNames != null && a < rtActionUdonEventNames.Length
                                       ? rtActionUdonEventNames[a] : "";
                    if (string.IsNullOrEmpty(eventName)) return;
                    if (scope == 0) // All Players (default)
                        rtActionUdonTargets[a].SendCustomNetworkEvent(
                            NetworkEventTarget.All, eventName);
                    else if (scope == 1) // Owner
                        rtActionUdonTargets[a].SendCustomNetworkEvent(
                            NetworkEventTarget.Owner, eventName);
                    else // scope == 2 — Local
                        rtActionUdonTargets[a].SendCustomEvent(eventName);
                }
            }
            else if (type == 6) // Set Udon Variable
            {
                if (rtActionUdonTargets != null && a < rtActionUdonTargets.Length
                    && rtActionUdonTargets[a] != null
                    && rtActionUdonVariableNames != null && a < rtActionUdonVariableNames.Length
                    && !string.IsNullOrEmpty(rtActionUdonVariableNames[a]))
                {
                    int varType = rtActionUdonVariableTypes != null && a < rtActionUdonVariableTypes.Length
                                  ? rtActionUdonVariableTypes[a] : 1;
                    // float/int honor active↔deactive symmetrically with type 2 (shader
                    // properties at :251-253 above): on-value = rtActionFloatValues[a],
                    // off-value = rtActionDefaultFloatValues[a]. Previously the executor
                    // always wrote the on-value regardless of `active`, so a Toggle Udon
                    // float action "stuck" on deactivate at runtime even though the
                    // build-time ApplyActionsDefault pass wrote the correct off-value on
                    // scene init. String remains on-only for now — introducing a default
                    // string field is a data-model change tracked separately.
                    if (varType == 0)       // bool
                        rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a], active);
                    else if (varType == 1)  // float
                    {
                        float onF = rtActionFloatValues != null && a < rtActionFloatValues.Length
                                    ? rtActionFloatValues[a] : 0f;
                        float offF = rtActionDefaultFloatValues != null && a < rtActionDefaultFloatValues.Length
                                     ? rtActionDefaultFloatValues[a] : 0f;
                        rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a],
                            active ? onF : offF);
                    }
                    else if (varType == 2)  // int
                    {
                        float onF = rtActionFloatValues != null && a < rtActionFloatValues.Length
                                    ? rtActionFloatValues[a] : 0f;
                        float offF = rtActionDefaultFloatValues != null && a < rtActionDefaultFloatValues.Length
                                     ? rtActionDefaultFloatValues[a] : 0f;
                        rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a],
                            (int)(active ? onF : offF));
                    }
                    else if (varType == 3)  // string
                        rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a],
                            rtActionUdonVariableStringValues != null && a < rtActionUdonVariableStringValues.Length
                                ? rtActionUdonVariableStringValues[a] : "");
                }
            }
            else if (type == 10) // Color Selector — delegate to linked controller
            {
                if (!active) return;
                if (linkedController == null)
                {
                    Debug.LogWarning("[EnigmaExecutor] Type 10 (Color Selector): no linked controller, skipping.");
                    return;
                }
                int role = rtActionColorSelectorRoles != null && a < rtActionColorSelectorRoles.Length
                           ? rtActionColorSelectorRoles[a] : 0;
                linkedController.HandleColorSelectorAction(entryIdx, a, role);
            }
            else if (type == 12) // Set Transform (command)
            {
                if (!active) return;
                if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                    && rtActionTargetObjects[a] != null)
                {
                    Transform t    = rtActionTargetObjects[a].transform;
                    int       mode  = rtActionPropertyTypes[a];  // 0=SetPos 1=SetRot 2=SetScale 3=AddPos 4=AddRot
                    int       space = rtActionTransformSpaces != null && a < rtActionTransformSpaces.Length
                                      ? rtActionTransformSpaces[a] : 0;
                    Vector3   val   = rtActionVectorValues != null && a < rtActionVectorValues.Length
                                      ? (Vector3)rtActionVectorValues[a] : Vector3.zero;
                    if (mode == 0)
                    {
                        if (space == 0) t.position      = val;
                        else            t.localPosition = val;
                    }
                    else if (mode == 1)
                    {
                        if (space == 0) t.eulerAngles      = val;
                        else            t.localEulerAngles = val;
                    }
                    else if (mode == 2) t.localScale = val;
                    else if (mode == 3)
                    {
                        if (space == 0) t.position      += val;
                        else            t.localPosition += val;
                    }
                    else if (mode == 4)
                    {
                        if (space == 0) t.eulerAngles      += val;
                        else            t.localEulerAngles += val;
                    }
                }
            }
            else if (type == 23) // Toggle Transform (stateful: on=apply, off=revert)
            {
                if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                    && rtActionTargetObjects[a] != null)
                {
                    Transform t    = rtActionTargetObjects[a].transform;
                    int       mode  = rtActionPropertyTypes[a];
                    int       space = rtActionTransformSpaces != null && a < rtActionTransformSpaces.Length
                                      ? rtActionTransformSpaces[a] : 0;

                    if (active)
                    {
                        // Save the current transform value before applying.
                        Vector3 current;
                        if      (mode == 0 || mode == 3) current = space == 0 ? t.position      : t.localPosition;
                        else if (mode == 1 || mode == 4) current = space == 0 ? t.eulerAngles   : t.localEulerAngles;
                        else                             current = t.localScale;

                        if (_savedTransformValues != null && a < _savedTransformValues.Length)
                            _savedTransformValues[a] = new Vector4(current.x, current.y, current.z, 0f);

                        // Apply the configured value
                        Vector3 val = rtActionVectorValues != null && a < rtActionVectorValues.Length
                                      ? (Vector3)rtActionVectorValues[a] : Vector3.zero;
                        if      (mode == 0) { if (space == 0) t.position      = val; else t.localPosition    = val; }
                        else if (mode == 1) { if (space == 0) t.eulerAngles   = val; else t.localEulerAngles = val; }
                        else if (mode == 2) t.localScale = val;
                        else if (mode == 3) { if (space == 0) t.position     += val; else t.localPosition   += val; }
                        else if (mode == 4) { if (space == 0) t.eulerAngles  += val; else t.localEulerAngles += val; }
                    }
                    else
                    {
                        // Restore saved transform value
                        if (_savedTransformValues != null && a < _savedTransformValues.Length)
                        {
                            Vector3 saved = (Vector3)_savedTransformValues[a];
                            if      (mode == 0 || mode == 3) { if (space == 0) t.position    = saved; else t.localPosition    = saved; }
                            else if (mode == 1 || mode == 4) { if (space == 0) t.eulerAngles = saved; else t.localEulerAngles = saved; }
                            else if (mode == 2)              t.localScale = saved;
                        }
                    }
                }
            }
            else if (type == 13) // Teleport
            {
                if (active)
                {
                    int teleportMode = rtActionPropertyTypes[a];
                    if (teleportMode == 0) // Respawn
                    {
                        VRCPlayerApi player = Networking.LocalPlayer;
                        if (player != null) player.Respawn();
                    }
                    else if (teleportMode == 1) // Teleport player to vector
                    {
                        VRCPlayerApi player = Networking.LocalPlayer;
                        if (player != null)
                        {
                            Vector3 pos = rtActionVectorValues != null && a < rtActionVectorValues.Length
                                          ? (Vector3)rtActionVectorValues[a] : Vector3.zero;
                            Vector3 rot = rtActionTeleportRotations != null && a < rtActionTeleportRotations.Length
                                          ? rtActionTeleportRotations[a] : Vector3.zero;
                            player.TeleportTo(pos, Quaternion.Euler(rot));
                        }
                    }
                    else if (teleportMode == 2) // Teleport player to Transform
                    {
                        VRCPlayerApi player = Networking.LocalPlayer;
                        if (player != null
                            && rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                            && rtActionTargetObjects[a] != null)
                        {
                            Transform target = rtActionTargetObjects[a].transform;
                            player.TeleportTo(target.position, target.rotation);
                        }
                    }
                    else if (teleportMode == 3) // Teleport object to vector
                    {
                        if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                            && rtActionTargetObjects[a] != null)
                        {
                            Vector3 pos = rtActionVectorValues != null && a < rtActionVectorValues.Length
                                          ? (Vector3)rtActionVectorValues[a] : Vector3.zero;
                            rtActionTargetObjects[a].transform.position = pos;
                        }
                    }
                    else if (teleportMode == 4) // Teleport object to Transform
                    {
                        if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                            && rtActionTargetObjects[a] != null
                            && rtActionTeleportDestinations != null && a < rtActionTeleportDestinations.Length
                            && rtActionTeleportDestinations[a] != null)
                        {
                            Transform dest = rtActionTeleportDestinations[a].transform;
                            rtActionTargetObjects[a].transform.SetPositionAndRotation(dest.position, dest.rotation);
                        }
                    }
                }
            }
            else if (type == 14) // Autochange Group — delegate to linked controller
            {
                if (linkedController == null)
                {
                    Debug.LogWarning("[EnigmaExecutor] Type 14 (Autochange Group): no linked controller, skipping.");
                    return;
                }
                int gid = rtActionAutoChangeGroupIds != null && a < rtActionAutoChangeGroupIds.Length
                          ? rtActionAutoChangeGroupIds[a] : -1;
                float ivl = rtActionAutoChangeIntervals != null && a < rtActionAutoChangeIntervals.Length
                            ? rtActionAutoChangeIntervals[a] : 10f;
                bool rnd = rtAutoChangeGroupRandom != null && a < rtAutoChangeGroupRandom.Length
                           && rtAutoChangeGroupRandom[a];
                if (gid >= 0)
                {
                    if (active)
                        linkedController.StartAutoChangeGroup(gid, ivl, rnd);
                    else
                        linkedController.StopAutoChangeGroup();
                }
            }
            else if (type == 15) // Set Object State (command)
            {
                if (rtActionTargetObjects != null && a < rtActionTargetObjects.Length
                    && rtActionTargetObjects[a] != null)
                    rtActionTargetObjects[a].SetActive(GetBakedTargetState(a));
            }
            else if (type == 17) // Set Autochange Group State (command)
            {
                if (linkedController == null) return;
                int gid = rtActionAutoChangeGroupIds != null && a < rtActionAutoChangeGroupIds.Length
                          ? rtActionAutoChangeGroupIds[a] : -1;
                float ivl = rtActionAutoChangeIntervals != null && a < rtActionAutoChangeIntervals.Length
                            ? rtActionAutoChangeIntervals[a] : 10f;
                bool rnd = rtAutoChangeGroupRandom != null && a < rtAutoChangeGroupRandom.Length
                           && rtAutoChangeGroupRandom[a];
                if (gid >= 0)
                {
                    if (GetBakedTargetState(a)) linkedController.StartAutoChangeGroup(gid, ivl, rnd);
                    else                         linkedController.StopAutoChangeGroup();
                }
            }
            else if (type == 18) // Set Whitelist (command, one-shot)
            {
                if (linkedController != null)
                    linkedController.whitelistEnabled = GetBakedTargetState(a);
            }
            else if (type == 28) // Toggle Whitelist
            {
                // Toggle action: write !default on activate, default on deactivate.
                // defaultFloatValue >= 0.5 = ON, < 0.5 = OFF.
                //
                // The default value is NOT applied at scene init by
                // EnigmaPlayModeHook.ApplyActionsDefault — case 28 is
                // intentionally absent there so the controller's own
                // whitelistEnabled inspector value drives the initial state.
                // Default is only written when the entry deactivates at
                // runtime (active=false here).
                if (linkedController != null
                    && rtActionDefaultFloatValues != null
                    && a < rtActionDefaultFloatValues.Length)
                {
                    bool defOn = rtActionDefaultFloatValues[a] >= 0.5f;
                    linkedController.whitelistEnabled = active ? !defOn : defOn;
                }
            }
            else if (type == 19) // Variant Selector — delegate to linked controller
            {
                if (!active) return;
                if (linkedController == null)
                {
                    Debug.LogWarning("[EnigmaExecutor] Type 19 (Variant Selector): no linked controller, skipping.");
                    return;
                }
                int role = rtActionVariantSelectorRoles != null && a < rtActionVariantSelectorRoles.Length
                           ? rtActionVariantSelectorRoles[a] : 0;
                linkedController.HandleVariantSelectorAction(entryIdx, a, role);
            }
            else if (type == 20) // Nav — delegate to linked controller
            {
                if (!active) return;
                if (linkedController == null) return;
                int navOp = rtActionPropertyTypes != null && a < rtActionPropertyTypes.Length
                            ? rtActionPropertyTypes[a] : 0;
                int navTgt = rtActionFloatValues != null && a < rtActionFloatValues.Length
                             ? (int)rtActionFloatValues[a] : 0;
                if      (navOp == 0) linkedController.CycleFolder(1);
                else if (navOp == 1) linkedController.CycleFolder(-1);
                else if (navOp == 2) linkedController.GoToFolder(navTgt);
                else if (navOp == 3) linkedController.ChangePage(1);
                else if (navOp == 4) linkedController.ChangePage(-1);
                else if (navOp == 5) linkedController.GoToPage(navTgt);
                else if (navOp == 6) linkedController.ResetAll();
                else if (navOp == 7) linkedController.ToggleFaderMode();
                else if (navOp == 10) linkedController.CycleFaderPage(1);
                else if (navOp == 11) linkedController.CycleFaderPage(-1);
                else if (navOp == 12) linkedController.GoToFaderPage(navTgt);
            }
            // Types 9, 21, 24, 25 are display-only — handled by UpdateDisplay/UpdateVisual, not here.
        }

    }
}

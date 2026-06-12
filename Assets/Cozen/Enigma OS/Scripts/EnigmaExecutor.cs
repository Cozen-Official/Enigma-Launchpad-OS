
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
        // Per-action lerp (type 2 float/color/vector): 0 = snap (default).
        // Activation fades the property from its CURRENT value to the target
        // over this many seconds. Composes with delay — the fade starts when
        // the (possibly delayed) action fires.
        [HideInInspector] public float[]  rtActionLerpSeconds        = new float[0];
        // When false (default), deactivation snaps to the default value
        // immediately; when true it fades back over the same duration.
        [HideInInspector] public bool[]   rtActionLerpOnDeactivate   = new bool[0];
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

        // Mochie "Always" pass gate id for type 2 actions: 0=_Zoom, 1=_SST,
        // 2=_Letterbox, -1=not a gate. Mochie renders Zoom/Image Overlay/
        // Letterbox in a dedicated "Always" pass that the shader does NOT
        // value-gate on _SST (and only partially on the others), so the pass
        // itself must be toggled at runtime — mirroring what Mochie's own
        // inspector does at edit time. Baked from
        // EnigmaShaderHelper.GetAlwaysPassGateId for both primary actions and
        // synthetic section-toggle actions.
        [HideInInspector] public int[] rtActionAlwaysGate = new int[0];
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

        // ── Lerp slots ──
        // Fixed pool of concurrently running property fades (same pattern as
        // the controller's expire queue — Udon has no dynamic collections).
        // 16 simultaneous fades per executor is far beyond realistic use; when
        // the pool is full a new lerp degrades gracefully to an instant write.
        private const int kLerpSlots = 16;
        private bool[]     _lerpOccupied;
        private int[]      _lerpActionIdx;
        private int[]      _lerpPropType;        // 0=Float 1=Color 2=Vector 3=UdonFloat 4=UdonInt
        private Material[] _lerpMaterials;       // material slots (propType 0-2)
        private string[]   _lerpProps;
        private UdonSharpBehaviour[] _lerpUdonTargets; // udon slots (propType 3-4)
        private string[]   _lerpUdonVars;
        private float[]    _lerpFromF;
        private float[]    _lerpToF;
        private Color[]    _lerpFromC;
        private Color[]    _lerpToC;
        private Vector4[]  _lerpFromV;
        private Vector4[]  _lerpToV;
        private float[]    _lerpElapsed;
        private float[]    _lerpDuration;
        private int        _lerpCount;

        // Set while ApplyDefaults runs so the scene-init state reset snaps
        // instead of fading (a world shouldn't fade its defaults in at load).
        private bool _suppressLerp;

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

            // Scene-init state resets snap — fading defaults in at world load
            // would look like every lerped effect "playing itself" once.
            _suppressLerp = true;
            int end = actStart + actCount;
            for (int a = actStart; a < end; a++)
            {
                if (a >= rtActionTypes.Length) break;
                int t = rtActionTypes[a];
                if (!active && t == 2 && !hasDefaults) continue;
                if (t == 0 || t == 2 || t == 27 || (t == 22 && active))
                    ExecuteAction(-1, a, active, true);
            }
            _suppressLerp = false;
        }

        // --------------------------------------------------------------------
        //  LERP ENGINE
        // --------------------------------------------------------------------

        private void EnsureLerpArrays()
        {
            if (_lerpOccupied != null) return;
            _lerpOccupied  = new bool[kLerpSlots];
            _lerpActionIdx = new int[kLerpSlots];
            _lerpPropType  = new int[kLerpSlots];
            _lerpMaterials = new Material[kLerpSlots];
            _lerpProps     = new string[kLerpSlots];
            _lerpUdonTargets = new UdonSharpBehaviour[kLerpSlots];
            _lerpUdonVars    = new string[kLerpSlots];
            _lerpFromF     = new float[kLerpSlots];
            _lerpToF       = new float[kLerpSlots];
            _lerpFromC     = new Color[kLerpSlots];
            _lerpToC       = new Color[kLerpSlots];
            _lerpFromV     = new Vector4[kLerpSlots];
            _lerpToV       = new Vector4[kLerpSlots];
            _lerpElapsed   = new float[kLerpSlots];
            _lerpDuration  = new float[kLerpSlots];
        }

        /// <summary>
        /// Returns true when action <paramref name="a"/> should fade instead
        /// of snapping for this activation direction.
        /// </summary>
        private bool ShouldLerp(int a, bool active)
        {
            if (_suppressLerp) return false;
            float secs = rtActionLerpSeconds != null && a < rtActionLerpSeconds.Length
                         ? rtActionLerpSeconds[a] : 0f;
            if (secs <= 0f) return false;
            if (active) return true;
            return rtActionLerpOnDeactivate != null && a < rtActionLerpOnDeactivate.Length
                   && rtActionLerpOnDeactivate[a];
        }

        /// <summary>
        /// Claims a lerp slot for the action (reusing the action's running
        /// slot so a mid-fade re-press continues smoothly from the current
        /// value). Returns -1 when the pool is exhausted — callers fall back
        /// to an instant write.
        /// </summary>
        private int ClaimLerpSlot(int a, Material mat, string propName, int propType)
        {
            EnsureLerpArrays();
            int slot = -1;
            for (int s = 0; s < kLerpSlots; s++)
            {
                if (_lerpOccupied[s] && _lerpActionIdx[s] == a) { slot = s; break; }
                if (slot < 0 && !_lerpOccupied[s]) slot = s;
            }
            if (slot < 0) return -1;
            if (!_lerpOccupied[slot]) _lerpCount++;
            _lerpOccupied[slot]  = true;
            _lerpActionIdx[slot] = a;
            _lerpPropType[slot]  = propType;
            _lerpMaterials[slot] = mat;
            _lerpProps[slot]     = propName;
            // Clear stale Udon refs from a recycled slot; the Udon claim path
            // fills these in right after claiming.
            _lerpUdonTargets[slot] = null;
            _lerpUdonVars[slot]    = null;
            _lerpElapsed[slot]   = 0f;
            _lerpDuration[slot]  = rtActionLerpSeconds[a];
            return slot;
        }

        /// <summary>
        /// Returns the float target of an in-flight fade on the given action,
        /// or <paramref name="fallback"/> when none is running. Step buttons
        /// use this as the authoritative "current" value: mid-fade the
        /// material holds an intermediate value, and stepping from THAT (after
        /// step-precision rounding) can resolve to the same step the fade was
        /// already heading to — making rapid presses appear to not advance.
        /// Stepping from the fade's target keeps the sequence exact.
        /// </summary>
        public float GetLerpTargetFloat(int a, float fallback)
        {
            if (_lerpCount <= 0 || _lerpOccupied == null) return fallback;
            for (int s = 0; s < kLerpSlots; s++)
            {
                // Float-valued slot kinds: material float (0), udon float (3),
                // udon int (4) — all store their target in _lerpToF.
                if (_lerpOccupied[s] && _lerpActionIdx[s] == a
                    && (_lerpPropType[s] == 0 || _lerpPropType[s] >= 3))
                    return _lerpToF[s];
            }
            return fallback;
        }

        /// <summary>
        /// Public cancel for callers outside ExecuteAction that snap a value
        /// directly (e.g. the controller's step-restore path writing a Udon
        /// variable) — a stale in-flight fade would otherwise overwrite the
        /// snap on the next frame.
        /// </summary>
        public void CancelLerp(int a)
        {
            CancelLerpForAction(a);
        }

        /// <summary>
        /// Cancels any running fade on the given action — called before every
        /// instant write so a snap isn't overwritten by a stale in-flight fade
        /// on the next frame.
        /// </summary>
        private void CancelLerpForAction(int a)
        {
            if (_lerpCount <= 0 || _lerpOccupied == null) return;
            for (int s = 0; s < kLerpSlots; s++)
            {
                if (_lerpOccupied[s] && _lerpActionIdx[s] == a)
                {
                    _lerpOccupied[s] = false;
                    _lerpCount--;
                }
            }
        }

        /// <summary>
        /// Advances all running fades. Early-outs when idle so the per-frame
        /// cost of the feature is a single int compare for worlds that never
        /// use Lerp.
        /// </summary>
        public void Update()
        {
            if (_lerpCount <= 0 || _lerpOccupied == null) return;
            float dt = Time.deltaTime;
            for (int s = 0; s < kLerpSlots; s++)
            {
                if (!_lerpOccupied[s]) continue;

                int pt = _lerpPropType[s];
                bool isUdon = pt >= 3;
                Material mat = isUdon ? null : _lerpMaterials[s];
                UdonSharpBehaviour utgt = isUdon ? _lerpUdonTargets[s] : null;
                if ((isUdon && utgt == null) || (!isUdon && mat == null))
                {
                    _lerpOccupied[s] = false;
                    _lerpCount--;
                    continue;
                }

                _lerpElapsed[s] += dt;
                float t = _lerpDuration[s] > 0f ? _lerpElapsed[s] / _lerpDuration[s] : 1f;
                if (t >= 1f)
                {
                    // Release the slot BEFORE the final managed write so
                    // WriteManagedFloat's own CancelLerpForAction doesn't
                    // double-decrement the count.
                    int a = _lerpActionIdx[s];
                    _lerpOccupied[s] = false;
                    _lerpCount--;
                    if (pt == 0)
                    {
                        // Managed final write: exact target, SetInt mirror for
                        // Int-declared properties, keyword enable, and the
                        // Always-pass recompute — which is what turns the pass
                        // off at the END of a deactivation fade instead of
                        // killing the effect at its start.
                        WriteManagedFloat(a, _lerpToF[s]);
                    }
                    else if (pt == 1) mat.SetColor(_lerpProps[s], _lerpToC[s]);
                    else if (pt == 2) mat.SetVector(_lerpProps[s], _lerpToV[s]);
                    else if (pt == 3) utgt.SetProgramVariable(_lerpUdonVars[s], _lerpToF[s]);
                    else              utgt.SetProgramVariable(_lerpUdonVars[s], (int)_lerpToF[s]);
                    continue;
                }

                if (pt == 0)      mat.SetFloat(_lerpProps[s], Mathf.Lerp(_lerpFromF[s], _lerpToF[s], t));
                else if (pt == 1) mat.SetColor(_lerpProps[s], Color.Lerp(_lerpFromC[s], _lerpToC[s], t));
                else if (pt == 2) mat.SetVector(_lerpProps[s], Vector4.Lerp(_lerpFromV[s], _lerpToV[s], t));
                else if (pt == 3) utgt.SetProgramVariable(_lerpUdonVars[s], Mathf.Lerp(_lerpFromF[s], _lerpToF[s], t));
                else              utgt.SetProgramVariable(_lerpUdonVars[s], (int)Mathf.Round(Mathf.Lerp(_lerpFromF[s], _lerpToF[s], t)));
            }
        }

        /// <summary>
        /// Managed float write for a type-2 action's target property, used by
        /// paths that compute their value at runtime instead of reading the
        /// baked rtActionFloatValues (step restore on deserialize/preset/reset,
        /// variant selector commits). Performs the SAME shader management as
        /// the type-2 float branch in ExecuteAction: SetFloat + whole-value
        /// SetInt mirror (Int-declared Mochie properties don't update from
        /// SetFloat alone on standalone), Always-pass gate handling, and
        /// keyword enabling. A naked mat.SetFloat here used to leave
        /// late-joiners with stale int uniforms and gate properties that never
        /// toggled the pass.
        /// </summary>
        public void WriteManagedFloat(int a, float value)
        {
            string kw = rtActionKeywords != null && a < rtActionKeywords.Length
                        ? rtActionKeywords[a] : "";
            WriteManagedFloatKeyword(a, value, kw);
        }

        /// <summary>
        /// Variant of <see cref="WriteManagedFloat"/> with an explicit keyword —
        /// variant-selector items resolve their keyword per item VALUE at build
        /// time (enum-mode toggles gate different keywords per value), so the
        /// caller passes rtVariantItemKeywords[item] instead of the action's.
        /// </summary>
        public void WriteManagedFloatKeyword(int a, float value, string keyword)
        {
            if (rtActionTargetRenderers == null || a >= rtActionTargetRenderers.Length) return;
            Renderer rend = rtActionTargetRenderers[a];
            if (rend == null) return;
            int matIdx = rtActionMaterialIndices != null && a < rtActionMaterialIndices.Length
                         ? rtActionMaterialIndices[a] : 0;
            string propName = rtActionPropertyNames != null && a < rtActionPropertyNames.Length
                              ? rtActionPropertyNames[a] : null;
            if (string.IsNullOrEmpty(propName)) return;

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
            if (mat == null) return;

            // A direct write supersedes any in-flight fade on this action —
            // without this, a running lerp keeps overwriting the snapped
            // value every frame until it finishes.
            CancelLerpForAction(a);

            mat.SetFloat(propName, value);
            int intVal = (int)value;
            if (value == (float)intVal)
                mat.SetInt(propName, intVal);

            // Enable-only keyword policy — matches the type-2 branch.
            if (keyword != null && keyword.Length > 0 && value > 0.5f)
                mat.EnableKeyword(keyword);

            // Mochie "Always" pass gate (_Zoom / _SST / _Letterbox).
            if (rtActionAlwaysGate != null && a < rtActionAlwaysGate.Length
                && rtActionAlwaysGate[a] >= 0)
            {
                if (value > 0.5f)
                {
                    mat.SetShaderPassEnabled("Always", true);
                }
                else
                {
                    bool held = linkedController != null
                        ? linkedController.ComputeAlwaysPassHeld(rend, matIdx, mat)
                        : ComputeHeldFallback(mat);
                    mat.SetShaderPassEnabled("Always", held);
                }
            }
        }

        /// <summary>
        /// Always-pass recompute fallback for executors with no linked
        /// controller (standalone buttons). Trusts honestly-valued gates only:
        /// _Zoom and _Letterbox are read from the material; _SST never is —
        /// the synthetic section-toggle action leaves it at 1 after the entry
        /// deactivates, so trusting it would hold the pass on forever.
        /// </summary>
        private bool ComputeHeldFallback(Material mat)
        {
            if (mat == null) return false;
            if (mat.HasProperty("_Zoom") && mat.GetFloat("_Zoom") > 0.5f) return true;
            if (mat.HasProperty("_Letterbox") && mat.GetFloat("_Letterbox") > 0.5f) return true;
            return false;
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
                    // …with one exception: actions that gate Mochie's "Always"
                    // shader pass (_Zoom / _SST / _Letterbox — including the
                    // synthetic section-toggle actions emitted for
                    // alsoSetEffectToggle). That pass is NOT value-gated by _SST —
                    // the overlay draws whenever the pass is enabled — so skipping
                    // deactivation entirely leaves the pass running after the
                    // entry's primary texture action reverts _ScreenTex to None,
                    // which renders the shader's "white" fallback texture
                    // fullscreen. Mirror Mochie's own inspector and recompute the
                    // pass from what still holds it: another active entry with a
                    // gate action on this material (e.g. a default-on sibling
                    // during init's ApplyDefaultsOff sweep, an active Zoom button
                    // while an overlay deactivates), or an honestly-valued gate
                    // property. Exclusive-group switches stay correct because
                    // peers deactivate BEFORE the pressed entry activates, so the
                    // incoming overlay re-enables the pass in the same frame.
                    bool nsIsGate = rtActionAlwaysGate != null && a < rtActionAlwaysGate.Length
                        && rtActionAlwaysGate[a] >= 0;
                    if (nsIsGate)
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
                                // Standalone buttons (no linked controller) fall back to
                                // honest gate values — _Zoom/_Letterbox only, never _SST
                                // (the synthetic toggle leaves _SST=1 after deactivation).
                                bool held = linkedController != null
                                    ? linkedController.ComputeAlwaysPassHeld(nsRend, nsMatIdx, nsMat)
                                    : ComputeHeldFallback(nsMat);
                                nsMat.SetShaderPassEnabled("Always", held);
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
                        // Lerp option: fade from the CURRENT value to the
                        // target instead of snapping. Activation always fades
                        // when enabled; deactivation only when "Also Lerp on
                        // Deactivation" is set (otherwise it snaps, mirroring
                        // the Delay option's split). Composes with Delay since
                        // delayed actions re-enter here when they fire.
                        bool doLerp = ShouldLerp(a, active);

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
                            int lerpSlot = -1;
                            if (doLerp)
                            {
                                lerpSlot = ClaimLerpSlot(a, mat, propName, 0);
                                if (lerpSlot >= 0)
                                {
                                    _lerpFromF[lerpSlot] = mat.GetFloat(propName);
                                    _lerpToF[lerpSlot]   = setVal;
                                }
                            }
                            if (lerpSlot < 0)
                            {
                                CancelLerpForAction(a);
                                mat.SetFloat(propName, setVal);
                                int intVal = (int)setVal;
                                if (setVal == (float)intVal)
                                    mat.SetInt(propName, intVal);
                            }

                            // Mochie "Always" pass gate (_Zoom / _SST / _Letterbox).
                            // The pass renders Zoom/Image Overlay/Letterbox and must
                            // be toggled alongside the gate property — Mochie's own
                            // inspector does this at edit time, we do it at runtime.
                            // Writing an "on" mode enables the pass directly; writing
                            // 0 (deactivate revert or a momentary off-Set) recomputes
                            // from whatever still holds the pass: another active
                            // entry's gate action or an honestly-valued gate property
                            // (covers an active Zoom surviving an Overlay turning off,
                            // and vice versa).
                            //
                            // Lerp interaction: a fade-IN enables the pass at fade
                            // START (so the effect is visible while ramping); a
                            // fade-OUT keeps the pass alive until the fade COMPLETES
                            // — the recompute runs from the lerp's final managed
                            // write in Update(), not here.
                            if (rtActionAlwaysGate != null && a < rtActionAlwaysGate.Length
                                && rtActionAlwaysGate[a] >= 0)
                            {
                                if (setVal > 0.5f)
                                {
                                    mat.SetShaderPassEnabled("Always", true);
                                }
                                else if (lerpSlot < 0)
                                {
                                    bool gateHeld = linkedController != null
                                        ? linkedController.ComputeAlwaysPassHeld(rend, matIdx, mat)
                                        : ComputeHeldFallback(mat);
                                    mat.SetShaderPassEnabled("Always", gateHeld);
                                }
                            }
                        }
                        else if (propType == 1)  // Color
                        {
                            Color def = rtActionDefaultColorValues != null && a < rtActionDefaultColorValues.Length
                                        ? rtActionDefaultColorValues[a] : Color.white;
                            Color cTarget = active ? rtActionColorValues[a] : def;
                            int cSlot = -1;
                            if (doLerp)
                            {
                                cSlot = ClaimLerpSlot(a, mat, propName, 1);
                                if (cSlot >= 0)
                                {
                                    _lerpFromC[cSlot] = mat.GetColor(propName);
                                    _lerpToC[cSlot]   = cTarget;
                                }
                            }
                            if (cSlot < 0)
                            {
                                CancelLerpForAction(a);
                                mat.SetColor(propName, cTarget);
                            }
                        }
                        else if (propType == 2)  // Vector
                        {
                            Vector4 def = rtActionDefaultVectorValues != null && a < rtActionDefaultVectorValues.Length
                                          ? rtActionDefaultVectorValues[a] : Vector4.zero;
                            Vector4 vTarget = active ? rtActionVectorValues[a] : def;
                            int vSlot = -1;
                            if (doLerp)
                            {
                                vSlot = ClaimLerpSlot(a, mat, propName, 2);
                                if (vSlot >= 0)
                                {
                                    _lerpFromV[vSlot] = mat.GetVector(propName);
                                    _lerpToV[vSlot]   = vTarget;
                                }
                            }
                            if (vSlot < 0)
                            {
                                CancelLerpForAction(a);
                                mat.SetVector(propName, vTarget);
                            }
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
                                mat.EnableKeyword(rtActionKeywords[a]);

                            // The Mochie "Always" pass toggle (Zoom/Image Overlay/
                            // Letterbox) is handled by the rtActionAlwaysGate hook in
                            // the float branch above — gate actions are always float
                            // mode writes, so every activation/revert passes through
                            // it. The editor-side ApplyMaterialFixups leaves the pass
                            // disabled as baseline and the keyword pre-enabled so the
                            // variant ships in the build; the runtime only toggles
                            // the pass.
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

                        // DisableKeyword is safe HERE (unlike the type-2
                        // enable-only policy above) because the build pipeline
                        // guarantees both states ship: PrepareShaderLocking
                        // force-enables every type-27 keyword on the live
                        // material AND its variant keeper (so the keyword-on
                        // variant survives stripping), and the keyword-off
                        // variant is the keyword-less default Unity always
                        // compiles. Type 2's blanket no-disable stance guards
                        // auto-detected keywords whose toggle state must stay
                        // recoverable without that explicit build-time pinning.
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
                // Category-1 (Set) actions are one-shot — they fire on
                // activate only. Type 2 has always honored this via the
                // rtActionNonStateful flag; type 6 historically wrote its
                // off-value on deactivate too, contradicting the documented
                // Set semantics (and the Options UI, which only offers
                // deactivation behaviour — Delay/Lerp checkboxes — on
                // Toggle-category actions). Aligned in 2.0.6.
                if (!active && rtActionNonStateful != null && a < rtActionNonStateful.Length
                    && rtActionNonStateful[a])
                    return;

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
                    //
                    // Lerp option: float and int variables fade from their
                    // CURRENT value to the target, exactly like shader
                    // property fades (ints round per frame). Bool/string
                    // can't interpolate and always snap.
                    if (varType == 0)       // bool
                        rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a], active);
                    else if (varType == 1 || varType == 2)  // float / int
                    {
                        float onF = rtActionFloatValues != null && a < rtActionFloatValues.Length
                                    ? rtActionFloatValues[a] : 0f;
                        float offF = rtActionDefaultFloatValues != null && a < rtActionDefaultFloatValues.Length
                                     ? rtActionDefaultFloatValues[a] : 0f;
                        float uTarget = active ? onF : offF;
                        bool uLerped = false;
                        if (ShouldLerp(a, active))
                        {
                            int uSlot = ClaimLerpSlot(a, null, null, varType == 1 ? 3 : 4);
                            if (uSlot >= 0)
                            {
                                _lerpUdonTargets[uSlot] = rtActionUdonTargets[a];
                                _lerpUdonVars[uSlot]    = rtActionUdonVariableNames[a];
                                // Fade start = the variable's current value;
                                // fall back to the opposite endpoint when the
                                // variable isn't a readable number.
                                float uCur = active ? offF : onF;
                                object uv = rtActionUdonTargets[a].GetProgramVariable(rtActionUdonVariableNames[a]);
                                if (uv != null && uv.GetType() == typeof(float)) uCur = (float)uv;
                                else if (uv != null && uv.GetType() == typeof(int)) uCur = (float)(int)uv;
                                _lerpFromF[uSlot] = uCur;
                                _lerpToF[uSlot]   = uTarget;
                                uLerped = true;
                            }
                        }
                        if (!uLerped)
                        {
                            CancelLerpForAction(a);
                            if (varType == 1)
                                rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a], uTarget);
                            else
                                rtActionUdonTargets[a].SetProgramVariable(rtActionUdonVariableNames[a], (int)uTarget);
                        }
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

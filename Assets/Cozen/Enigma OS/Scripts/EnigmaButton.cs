using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// A self-contained button that executes one or more actions on interact.
    /// Does NOT require an EnigmaController.
    ///
    /// Author action list in the inspector via the "actions" array (EnigmaActionData[]).
    /// Click "Build Runtime Arrays" to compile that list into the rt* flat arrays below,
    /// which Udon reads at runtime.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EnigmaButton : UdonSharpBehaviour
    {
        public Renderer buttonRenderer;
        public TMP_Text buttonText;
        public string label = "";
        public Color activeColor   = Color.HSVToRGB(242f / 360f, 1f, 1f);
        public Color inactiveColor = Color.white;
        public float flashDuration = 0.5f;
        public float flashEmissionIntensity = 2f;

        [Tooltip("When enabled, the button triggers automatically when the local " +
                 "player enters a trigger collider on this GameObject.")]
        public bool triggerOnEnter = false;

        [Tooltip("When enabled, the button triggers automatically when the local " +
                 "player exits a trigger collider on this GameObject.")]
        public bool triggerOnExit = false;

        private const float EmissionStrength = 1f;

        // Fallback button type used when no actions are built yet.
        // 0 = Toggle, 1 = Momentary
        public int buttonType = 0;

        [UdonSynced] public bool isActive = false;

        // ── Runtime flat arrays (compiled from actions by the build step) ────────
        // Editor-time action data lives on the companion EnigmaButtonActions
        // MonoBehaviour (Editor-only) to avoid UdonSharp serialisation errors.
        // These are the only arrays Udon reads at runtime.

        [HideInInspector] public int rtButtonType = 0;

        // -- Executor component (holds all action-indexed arrays and execution logic) --
        [HideInInspector] public EnigmaExecutor executor;

        // ── Exclusive group peer arrays (baked by BuildExclusivePeerLinks) ────────
        // All other EnigmaButton instances that share any exclusive tag with this button.
        [HideInInspector] public EnigmaButton[]     rtExclusivePeerButtons        = new EnigmaButton[0];
        // One entry per (controller, tag) pair for every controller whose entries share an
        // exclusive tag with this button. Parallel to rtExclusivePeerControllerTags.
        [HideInInspector] public EnigmaController[] rtExclusivePeerControllers    = new EnigmaController[0];
        // The exclusive group tag string to pass to each peer controller's DeactivateExclusiveGroup.
        [HideInInspector] public string[]           rtExclusivePeerControllerTags = new string[0];

        // ── Whitelist / controller link / nav ────────────────────────────────────

        public bool whitelistEnabled = false;
        public string[] authorizedUsernames = new string[0];

        [Tooltip("Optional controller link. Required for autochange group actions (type 14/17), nav actions (type 20), and display actions (type 24/25). Not needed for exclusive group deactivation — peer links are baked automatically by the build pipeline.")]
        public EnigmaController linkedController;
        /// <summary>Comma-separated exclusive group tags. Requires <see cref="useExclusiveGroup"/>. Peer links to other buttons and controllers are baked by the scene-wide build pass.</summary>
        public string exclusiveGroup  = "";
        /// <summary>When true the button activates on scene start, running its actions immediately.</summary>
        public bool   onByDefault     = false;
        /// <summary>
        /// When true this button acts as the "Exclusive Off" representative for its group:
        /// it auto-activates when all other group members are deactivated. Combine with care —
        /// using Expire on an Exclusive Off button creates a reactivation loop.
        /// </summary>
        public bool   exclusiveOff    = false;
        /// <summary>When true <see cref="exclusiveGroup"/> is applied on press to deactivate sibling entries.</summary>
        public bool   useExclusiveGroup = false;
        /// <summary>
        /// When true the button auto-deactivates after <see cref="expireSeconds"/> on its
        /// toggle-active state. Only meaningful for stateful (Toggle) buttons. Combining
        /// with Exclusive Off creates a reactivation loop.
        /// </summary>
        public bool   useExpire        = false;
        /// <summary>How long the button stays active before auto-deactivating, in seconds.</summary>
        public float  expireSeconds    = 5f;


        // ── Internal state ────────────────────────────────────────────────────────

        private MaterialPropertyBlock _mpb;
        private float _flashTimer;
        private bool  _isFlashing;
        private float _expireTimer = 0f;
        private bool  _isExpiring  = false;
        private bool  _hasDisplayAction = false;
        // _savedTransformValues, _conditionalColor, _hasConditionalColor moved to executor

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        private void Reset()
        {
            foreach (Transform child in transform)
            {
                Renderer r           = child.GetComponent<Renderer>();
                TMPro.TMP_Text tmp   = child.GetComponent<TMPro.TMP_Text>();
                if (buttonRenderer == null && r != null && tmp == null)
                    buttonRenderer = r;
                if (buttonText == null && tmp != null)
                    buttonText = tmp;
            }
        }
#endif

        private void Start()
        {
            if (executor != null)
            {
                executor.Initialize();
                // Detect display actions that need per-frame refresh
                var types = executor.rtActionTypes;
                if (types != null)
                {
                    for (int i = 0; i < types.Length; i++)
                    {
                        int t = types[i];
                        if (t == 9 || ((t == 21 || t == 24 || t == 25) && linkedController != null))
                        {
                            _hasDisplayAction = true;
                            break;
                        }
                    }
                }
            }

            if (onByDefault)
            {
                isActive = true;
                ExecuteActions(true);
                ScheduleExpire();
            }
            else
            {
                // Force stateful toggle actions to off-state to clear stale
                // material/object values from a previous play session.
                ApplyDefaultsOff();
            }
            UpdateVisual();
        }

        public override void Interact()
        {
            if (!IsAuthorized()) return;

            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            // Use rtButtonType if we have compiled actions, otherwise fall back to buttonType.
            // rtButtonType values: 0=Toggle, 1=Momentary, 2=Step, 3=ColorCycle, 4=DisplayOnly
            int effectiveType = (executor != null && executor.rtActionTypes != null && executor.rtActionTypes.Length > 0) ? rtButtonType : buttonType;

            if (effectiveType == 0) // Toggle
            {
                // Exclusive Off button: cannot be toggled off by the user while it is active
                // (mirrors the same guard in EnigmaController.HandleToggle).
                if (exclusiveOff && isActive && useExclusiveGroup) return;

                isActive = !isActive;
                if (isActive && useExclusiveGroup)
                {
                    // Deactivate peer EnigmaButton instances sharing any exclusive tag.
                    for (int i = 0; i < rtExclusivePeerButtons.Length; i++)
                        if (rtExclusivePeerButtons[i] != null) rtExclusivePeerButtons[i].ForceDeactivate();
                    // Deactivate matching entries on peer controllers.
                    for (int i = 0; i < rtExclusivePeerControllers.Length; i++)
                        if (rtExclusivePeerControllers[i] != null && i < rtExclusivePeerControllerTags.Length)
                            rtExclusivePeerControllers[i].DeactivateExclusiveGroup(rtExclusivePeerControllerTags[i]);
                }
                ExecuteActions(isActive);
                if (isActive) ScheduleExpire();
                else _isExpiring = false;
            }
            else if (effectiveType != 4) // Momentary / Step / ColorCycle — not DisplayOnly
            {
                ExecuteActions(true);
                Flash(activeColor);
            }

            UpdateVisual();
            RequestSerialization();

        }

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!triggerOnEnter || !player.isLocal) return;
            Interact();
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!triggerOnExit || !player.isLocal) return;
            Interact();
        }

        public override void OnDeserialization()
        {
            ExecuteActions(isActive);
            UpdateVisual();
        }

        /// <summary>
        /// Called by an exclusive-group peer when it activates, to force this button off.
        /// Also called by EnigmaController when one of its entries activates and this button
        /// shares the same exclusive group tag.
        /// </summary>
        public void ForceDeactivate()
        {
            if (!isActive) return;
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            isActive = false;
            ExecuteActions(false);
            _isExpiring = false;
            UpdateVisual();
            RequestSerialization();
        }

        /// <summary>
        /// Always-pass gate scan, called by EnigmaController's cross-component
        /// ComputeAlwaysPassHeld (the controller is wired with every gate-
        /// holding standalone button at rebuild). Bitmask contract matches
        /// EnigmaController.GetAlwaysGateStateLocal: bit 0 (1) = this button
        /// is active and owns a gate action on the given renderer/material
        /// slot; bit 1 (2) = a non-stateful action here writes the _Zoom gate
        /// (material value untrustworthy); bit 2 (4) = same for _Letterbox.
        /// </summary>
        public int GetAlwaysGateState(Renderer rend, int matIdx)
        {
            int state = 0;
            if (executor == null || rend == null) return 0;
            var exe = executor;
            if (exe.rtActionAlwaysGate == null || exe.rtActionTargetRenderers == null) return 0;

            for (int a = 0; a < exe.rtActionAlwaysGate.Length; a++)
            {
                int gate = exe.rtActionAlwaysGate[a];
                if (gate < 0) continue;
                if (a >= exe.rtActionTargetRenderers.Length
                    || exe.rtActionTargetRenderers[a] != rend) continue;
                int mi = exe.rtActionMaterialIndices != null && a < exe.rtActionMaterialIndices.Length
                         ? exe.rtActionMaterialIndices[a] : 0;
                if (mi != matIdx) continue;

                if (isActive) return state | 1;

                bool ns = exe.rtActionNonStateful != null && a < exe.rtActionNonStateful.Length
                          && exe.rtActionNonStateful[a];
                if (ns)
                {
                    if (gate == 0) state = state | 2;
                    else if (gate == 2) state = state | 4;
                }
            }
            return state;
        }

        private void ExecuteActions(bool active)
        {
            if (executor == null) return;
            var exe = executor;
            if (exe.rtActionTypes == null) return;
            int len = exe.rtActionTypes.Length;
            for (int a = 0; a < len; a++)
            {
                int type = exe.rtActionTypes[a];

                ExecuteSingleAction(a, active);
            }
        }

        private void ExecuteSingleAction(int a, bool active)
        {
            if (executor == null) return;
            // Standalone buttons: use -1 for entryIdx (no controller entry context)
            // isToggleEntry: true when this button is a toggle type
            bool isToggle = (rtButtonType == 0) ||
                            (executor.rtActionTypes != null && executor.rtActionTypes.Length > 0 && rtButtonType == 0);
            int effectiveType = (executor.rtActionTypes != null && executor.rtActionTypes.Length > 0) ? rtButtonType : buttonType;
            bool isToggleEntry = (effectiveType == 0);
            executor.ExecuteAction(-1, a, active, isToggleEntry);
        }

        /// <summary>
        /// Delegates to executor.ApplyDefaults to reset stateful toggle
        /// actions into their off state, clearing stale material/object values
        /// from a previous play session.
        /// </summary>
        private void ApplyDefaultsOff()
        {
            if (executor == null) return;
            var exe = executor;
            int len = exe.rtActionTypes != null ? exe.rtActionTypes.Length : 0;
            exe.ApplyDefaults(0, len, false);
        }

        /// <summary>
        /// Triggers a brief emission flash on this button's renderer.
        /// </summary>
        private void Flash(Color color)
        {
            if (buttonRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", color * flashEmissionIntensity);
            _mpb.SetFloat("_EmissionStrength", flashEmissionIntensity);
            buttonRenderer.SetPropertyBlock(_mpb);
            _isFlashing = true;
            _flashTimer = flashDuration;
        }

        private void UpdateVisual()
        {
            Color c = isActive ? activeColor : inactiveColor;
            string displayLabel = label;
            var exe = executor;

            // ── Evaluate display actions (per-frame refresh) ──
            if (_hasDisplayAction && exe != null && exe.rtActionTypes != null)
            {
                float displayFloat = 0f;
                bool  hasDisplayFloat = false;
                var types = exe.rtActionTypes;

                for (int i = 0; i < types.Length; i++)
                {
                    int t = types[i];

                    if (t == 9) // Display Value
                    {
                        // Shader property source
                        if (i < exe.rtActionTargetRenderers.Length && exe.rtActionTargetRenderers[i] != null
                            && i < exe.rtActionPropertyNames.Length && !string.IsNullOrEmpty(exe.rtActionPropertyNames[i]))
                        {
                            Material[] dvMats = exe.rtActionTargetRenderers[i].sharedMaterials;
                            int dvMatIdx = i < exe.rtActionMaterialIndices.Length ? exe.rtActionMaterialIndices[i] : 0;
                            Material mat = (dvMats != null && dvMatIdx >= 0 && dvMatIdx < dvMats.Length)
                                           ? dvMats[dvMatIdx] : null;
                            if (mat != null)
                            {
                                int propType = i < exe.rtActionPropertyTypes.Length ? exe.rtActionPropertyTypes[i] : 0;
                                string valStr = "";
                                string propName = exe.rtActionPropertyNames[i];
                                if (propType == 0)
                                {
                                    float fv = mat.GetFloat(propName);
                                    valStr = fv.ToString("F2");
                                    displayFloat = fv;
                                    hasDisplayFloat = true;
                                }
                                else if (propType == 1)
                                {
                                    Color col = mat.GetColor(propName);
                                    valStr = "(" + col.r.ToString("F2") + "," + col.g.ToString("F2") + "," + col.b.ToString("F2") + ")";
                                }
                                else if (propType == 2)
                                {
                                    Vector4 v = mat.GetVector(propName);
                                    valStr = "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
                                }
                                if (!string.IsNullOrEmpty(valStr))
                                    displayLabel = displayLabel + "\n" + valStr;
                            }
                        }
                        // Udon variable source
                        else if (i < exe.rtActionUdonTargets.Length && exe.rtActionUdonTargets[i] != null)
                        {
                            string udonVarName = null;
                            if (i < exe.rtActionUdonVariableNames.Length && !string.IsNullOrEmpty(exe.rtActionUdonVariableNames[i]))
                                udonVarName = exe.rtActionUdonVariableNames[i];
                            else if (i < exe.rtActionPropertyNames.Length && !string.IsNullOrEmpty(exe.rtActionPropertyNames[i]))
                                udonVarName = exe.rtActionPropertyNames[i];

                            if (!string.IsNullOrEmpty(udonVarName))
                            {
                                int varType = i < exe.rtActionPropertyTypes.Length ? exe.rtActionPropertyTypes[i]
                                              : (i < exe.rtActionUdonVariableTypes.Length ? exe.rtActionUdonVariableTypes[i] : 0);
                                object rawVal = exe.rtActionUdonTargets[i].GetProgramVariable(udonVarName);
                                if (rawVal != null)
                                {
                                    string valStr = "";
                                    if (varType == 0)
                                    {
                                        float fv = (float)rawVal;
                                        valStr = fv.ToString("F2");
                                        displayFloat = fv;
                                        hasDisplayFloat = true;
                                    }
                                    else if (varType == 1) valStr = ((bool)rawVal) ? "true" : "false";
                                    else if (varType == 2)
                                    {
                                        int iv = (int)rawVal;
                                        valStr = iv.ToString();
                                        displayFloat = iv;
                                        hasDisplayFloat = true;
                                    }
                                    else if (varType == 3) valStr = (string)rawVal;
                                    else if (varType == 4)
                                    {
                                        Color col = (Color)rawVal;
                                        valStr = "(" + col.r.ToString("F2") + "," + col.g.ToString("F2") + "," + col.b.ToString("F2") + ")";
                                    }
                                    if (!string.IsNullOrEmpty(valStr))
                                        displayLabel = displayLabel + "\n" + valStr;
                                }
                            }
                        }

                        // Conditional Coloring
                        if (hasDisplayFloat
                            && exe.rtCondColorStart != null && i < exe.rtCondColorStart.Length
                            && exe.rtCondColorCount != null && i < exe.rtCondColorCount.Length)
                        {
                            int ccStart = exe.rtCondColorStart[i];
                            int ccCount = exe.rtCondColorCount[i];
                            for (int cc = ccStart; cc < ccStart + ccCount; cc++)
                            {
                                if (exe.rtCondColorConditions == null || cc >= exe.rtCondColorConditions.Length) break;
                                int   cond = exe.rtCondColorConditions[cc];
                                float cval = exe.rtCondColorValues[cc];
                                bool match = false;
                                if      (cond == 0) match = displayFloat <  cval;
                                else if (cond == 1) match = displayFloat >  cval;
                                else if (cond == 2)
                                {
                                    float diff = displayFloat - cval;
                                    if (diff < 0f) diff = -diff;
                                    match = diff < 0.0001f;
                                }
                                else if (cond == 3) match = displayFloat <= cval;
                                else if (cond == 4) match = displayFloat >= cval;
                                if (match)
                                {
                                    c = exe.rtCondColorColors[cc];
                                    break;
                                }
                            }
                        }
                        break; // only one display value action
                    }

                    if (t == 21 && linkedController != null) // Display Stat
                    {
                        int metric = i < exe.rtActionStatMetrics.Length ? exe.rtActionStatMetrics[i] : 0;
                        string statName  = linkedController.GetStatDisplayName(metric);
                        string statValue = linkedController.FormatStatValue(metric);
                        displayLabel = string.IsNullOrEmpty(statValue) ? statName : statName + "\n" + statValue;
                        break;
                    }

                    if (t == 24 && linkedController != null) // Display Folder Name
                    {
                        int fi = linkedController.currentFolderIndex;
                        string[] names = linkedController.rtFolderNames;
                        displayLabel = (names != null && fi >= 0 && fi < names.Length) ? names[fi] : "";
                        break;
                    }

                    if (t == 25 && linkedController != null) // Display Page Number
                    {
                        int total = linkedController.GetPageCount(linkedController.currentFolderIndex);
                        displayLabel = (linkedController.currentPageIndex + 1) + " / " + total;
                        break;
                    }
                }
            }

            // ── Apply button color ──
            // If conditional coloring is active but no rule matched this frame, keep default
            if (buttonRenderer != null && !_isFlashing)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                buttonRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c);
                _mpb.SetFloat("_EmissionStrength", EmissionStrength);
                buttonRenderer.SetPropertyBlock(_mpb);
            }

            if (buttonText != null)
                buttonText.text = displayLabel;
        }

        private void Update()
        {
            if (!_isFlashing && !_isExpiring && !_hasDisplayAction) return;
            float dt = Time.deltaTime;
            if (_isFlashing)
            {
                _flashTimer -= dt;
                if (_flashTimer <= 0f) { _isFlashing = false; UpdateVisual(); }
            }
            if (_isExpiring)
            {
                _expireTimer -= dt;
                if (_expireTimer <= 0f)
                {
                    _isExpiring = false;
                    isActive    = false;
                    ExecuteActions(false);
                    UpdateVisual();
                    RequestSerialization();
                }
            }
            if (_hasDisplayAction) UpdateVisual();
        }

        private bool IsAuthorized()
        {
            if (linkedController != null)
                return linkedController.CanLocalUserInteract();

            if (!whitelistEnabled) return true;
            if (authorizedUsernames == null || authorizedUsernames.Length == 0) return true;
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null) return false;
            string name = player.displayName.Trim().ToLower();
            for (int i = 0; i < authorizedUsernames.Length; i++)
            {
                if (string.IsNullOrEmpty(authorizedUsernames[i])) continue;
                if (authorizedUsernames[i].Trim().ToLower() == name) return true;
            }
            return false;
        }

        /// <summary>
        /// Starts an expire countdown using the button-level <see cref="expireSeconds"/>
        /// when <see cref="useExpire"/> is enabled. Does nothing otherwise.
        /// </summary>
        private void ScheduleExpire()
        {
            if (!useExpire || expireSeconds <= 0f) return;
            _expireTimer = expireSeconds;
            _isExpiring = true;
        }
    }
}

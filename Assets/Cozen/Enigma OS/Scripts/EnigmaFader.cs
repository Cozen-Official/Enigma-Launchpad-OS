using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using TMPro;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Axis along which a fader slider moves.
    /// Declared outside any class to comply with UdonSharp's no-nested-types rule.
    /// </summary>
    public enum FaderAxis
    {
        X,
        Y,
        Z
    }

    /// <summary>
    /// Physical fader/slider component for Enigma OS.
    ///
    /// TWO OPERATING MODES based on whether a controller is assigned:
    ///
    ///   STANDALONE (controller == null)
    ///     The fader handles everything itself: its own left/right hand-collider
    ///     tracking objects, InputGrab, and the Update position loop.
    ///     Use this for one-off world faders that are not part of an
    ///     EnigmaController layout.
    ///
    ///   CONTROLLER-MANAGED (controller != null)
    ///     The controller owns a single shared pair of hand-tracker objects
    ///     (controller.sharedLeftHandCollider / sharedRightHandCollider).
    ///     The fader's OnTriggerEnter/Exit routes events to the controller and
    ///     then exits — the controller's own Update loop drives the position of
    ///     whichever fader is currently active.  The fader's standalone Update
    ///     and InputGrab code are fully bypassed, avoiding N concurrent loops.
    ///
    /// Two physical interaction sub-modes (toggled by EnigmaController.ToggleFaderMode):
    ///   0 = Hand Collider  — collider objects track player hands.
    ///   1 = VRC Pickup     — the slider itself is a VRC Pickup constrained to one axis.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EnigmaFader : UdonSharpBehaviour
    {
        [Tooltip("Leave null for standalone mode. Assign an EnigmaController to hand over " +
                 "position tracking to the controller's shared hand-collider system.")]
        public EnigmaController controller;

        [Tooltip("Slot index assigned by the controller's Build step.")]
        [HideInInspector] public int slotIndex = -1;
        // Set by the controller when binding a dynamic fader link.
        // -1 means this slot is not bound to a dynamic link.
        [HideInInspector] public int boundLinkIndex = -1;

        // ── Physical interaction ──
        [Header("Physical Interaction")]
        [Tooltip("(Standalone only) Left-hand tracking collider object.")]
        [SerializeField] private GameObject leftHandCollider;
        [Tooltip("(Standalone only) Right-hand tracking collider object.")]
        [SerializeField] private GameObject rightHandCollider;
        [SerializeField] private GameObject bottomLimiter;
        [SerializeField] private GameObject topLimiter;
        [SerializeField] private FaderAxis movementAxis = FaderAxis.Y;

        [Header("Pickup Mode (optional)")]
        [SerializeField] private VRC_Pickup vrcPickup;
        [SerializeField] private Rigidbody faderRigidbody;

        // ── Display ──
        [Header("Display")]
        public Renderer indicatorRenderer;
        public TMP_Text labelText;

        // ── Runtime synced state ──
        [UdonSynced] public float currentValue = 0f;
        [UdonSynced] private Vector3 _syncedSliderPosition;

        // ── Bound target (set by controller at runtime) ──
        [HideInInspector] public Material[] targetMaterials;
        [HideInInspector] public string materialPropertyId = "";
        // Property-type enum for the bound target. FADER convention:
        //   0 = Float (scalar — applied via SetFloat / written to Udon scalar
        //       variables / pushed to UI Slider.value)
        //   1 = Color (hue-shift from defaultColor baseline — see
        //       ApplyValueToMaterials' propertyType == 1 branch at ~L680)
        // Faders don't support Vector or Texture. The shader-property Search
        // button in the inspector filters Texture out of the tree and clamps
        // Vector → Float at write time, so this field is always 0 or 1 by
        // the time any runtime code reads it.
        [HideInInspector] public int propertyType = 0;
        [HideInInspector] public float valueMin = 0f;
        [HideInInspector] public float valueMax = 1f;
        [HideInInspector] public float defaultValue = 0f;
        [HideInInspector] public Color defaultColor = Color.white;
        [HideInInspector] public bool isBound = false;

        // ── UI Slider targets (set by Bind) ──
#if UNITY_UI
        [HideInInspector] public UnityEngine.UI.Slider[] targetSliders;
#endif
        [HideInInspector] public bool[] sliderDirectionsReversed;

        // ── Udon variable targets (set by Bind) ──
        [HideInInspector] public UdonSharpBehaviour[] targetUdonBehaviours;
        [HideInInspector] public string udonVariableName = "";

        // ── Standalone interaction state (ignored in controller-managed mode) ──
        private bool _inLeftTrigger;
        private bool _inRightTrigger;
        private bool _leftGrabbed;
        private bool _rightGrabbed;
        private bool _isPickupHeld;

        // ── Indicator configuration (set by Bind) ──
        [HideInInspector] public bool indicatorEnabled = false;
        [HideInInspector] public Color indicatorColor = Color.white;
        [HideInInspector] public bool indicatorConditional = false;

        // ── Trigger zone tracking (for distance-based switching) ──
        private bool _inLeftTriggerZone;
        private bool _inRightTriggerZone;

        // ── Cached computed color ──
        private Color _currentComputedColor = Color.white;

        // ── Shared ──
        private float _lastValue;
        private float _bottomBound;
        private float _topBound;
        private Vector3 _initialLocalPosition;
        private bool _movementInitialized;
        private VRCPlayerApi _localPlayer;
        private bool _standAloneHandTrackingEnabled;
        private MaterialPropertyBlock _indicatorMpb;

        // ── Step quantization ──
        private const int FaderStepCount = 32;

        // ────────────────────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            _localPlayer = Networking.LocalPlayer;
            EnsureMovementInitialized();
            // Standalone faders track their own hand colliders; controller-managed
            // faders rely on the controller's UpdateHandColliderPositions().
            _standAloneHandTrackingEnabled = controller == null
                && _localPlayer != null && _localPlayer.IsUserInVR();
        }

        /// <summary>
        /// Idempotently captures the handle's authored rest position, its
        /// movement bounds, and the indicator MaterialPropertyBlock. MUST run
        /// before any code reads <see cref="_initialLocalPosition"/>,
        /// <see cref="_bottomBound"/>, <see cref="_topBound"/>, or
        /// <see cref="_indicatorMpb"/>.
        ///
        /// Why this isn't just Start(): VRChat does NOT guarantee this fader's
        /// Start() runs before the controller's Start() binds it. The controller
        /// binds every static fader from its own Start() (EnigmaController.Start
        /// → BindStaticFaders), and Udon Start ordering across behaviours is
        /// non-deterministic — it varies between ClientSim and uploaded builds,
        /// and even between runs. A static fader bound before its Start() would
        /// read _initialLocalPosition = (0,0,0) and _bottomBound/_topBound = 0,
        /// snapping the handle to the local origin — and Bind() then writes that
        /// zero into _syncedSliderPosition and serializes it, so the bad
        /// position syncs to everyone and survives (OnDeserialization skips the
        /// restore when _syncedSliderPosition == Vector3.zero). Dynamic faders
        /// escaped this because they bind on button activation, long after all
        /// Start()s have run. Capturing lazily on first touch — guarded so the
        /// authored position is only ever recorded once, before any Bind moves
        /// the handle — closes the race for both fader types.
        /// </summary>
        private void EnsureMovementInitialized()
        {
            if (_movementInitialized) return;
            _movementInitialized = true;
            _initialLocalPosition = transform.localPosition;
            if (_indicatorMpb == null) _indicatorMpb = new MaterialPropertyBlock();
            CacheMovementBounds();
        }

        public override void OnDeserialization()
        {
            // A remote sync can arrive before this fader's Start(); make sure
            // the authored rest position is captured before we touch the handle.
            EnsureMovementInitialized();
            ApplyValueToMaterials(currentValue);
            if (_syncedSliderPosition != Vector3.zero)
            {
                float beforeY = GetAxisPosition(transform.localPosition);
                float syncedY = GetAxisPosition(_syncedSliderPosition);
                if (!Mathf.Approximately(beforeY, syncedY))
                    if (controller != null && controller.debugLogging)
                        Debug.Log($"[Fader] OnDeserialization '{gameObject.name}': Y {beforeY} -> {syncedY} (delta={syncedY - beforeY})");
                transform.localPosition = _syncedSliderPosition;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  BIND / UNBIND
        // ────────────────────────────────────────────────────────────────────────

        public void Bind(string label, Material[] materials, string propertyName,
                         int propType, float min, float max, float defaultVal)
        {
            Bind(label, materials, propertyName, propType, min, max, defaultVal, Color.white);
        }

        public void Bind(string label, Material[] materials, string propertyName,
                         int propType, float min, float max, float defaultVal, Color defColor)
        {
            Bind(label, materials, propertyName, propType, min, max, defaultVal, defColor,
                 false, Color.white, false);
        }

        public void Bind(string label, Material[] materials, string propertyName,
                         int propType, float min, float max, float defaultVal, Color defColor,
                         bool indEnabled, Color indColor, bool indConditional)
        {
            // Capture authored rest position / bounds before we reposition the
            // handle below — this Bind can run before the fader's own Start().
            EnsureMovementInitialized();
            targetMaterials    = materials;
            materialPropertyId = propertyName;
            propertyType       = propType;
            valueMin           = min;
            valueMax           = max;
            defaultValue       = defaultVal;
            defaultColor       = defColor;
            isBound            = true;
            indicatorEnabled   = indEnabled;
            indicatorColor     = indColor;
            indicatorConditional = indConditional;
            // Seed the computed-color cache with the fader's baseline so the
            // indicator ring renders the correct colour on the very first frame
            // after bind — before ApplyValueToMaterials has run. Otherwise the
            // ring briefly shows whatever colour was left over from the previous
            // bind (or Color.white at startup).
            _currentComputedColor = defColor;

            // First-bind seed: if no sync data has arrived yet (the synced
            // slider position is still Vector3.zero) AND currentValue is
            // still at its UdonSharp default of 0, initialize currentValue
            // from the normalized default. Otherwise a fader whose default
            // is 1 (e.g. Brightness) would write 0 to its material the first
            // time it binds, overwriting the authored default. We intentionally
            // gate this on _syncedSliderPosition == Vector3.zero so that late
            // joiners receiving synced currentValue==0 keep the owner's value.
            if (_syncedSliderPosition == Vector3.zero && currentValue == 0f)
            {
                float _seedRange = max - min;
                if (_seedRange > 0f)
                    currentValue = Mathf.Clamp01((defaultVal - min) / _seedRange);
            }

            if (labelText != null) labelText.text = label;
            if (indicatorRenderer != null) indicatorRenderer.enabled = indEnabled;

            // Restore the fader's physical position from its remembered value.
            // currentValue is always normalized 0-1, so use it directly as the
            // interpolation factor between the physical bounds.
            float t = Mathf.Clamp01(currentValue);
            float axisPos = _bottomBound + (_topBound - _bottomBound) * t;
            float beforeY = GetAxisPosition(transform.localPosition);
            Vector3 newPos = SetAxisValue(_initialLocalPosition, axisPos);
            if (controller != null && controller.debugLogging)
                Debug.Log($"[Fader] Bind '{gameObject.name}': currentValue={currentValue}, t={t}, " +
                      $"axisPos={axisPos}, beforeY={beforeY}, bounds=[{_bottomBound},{_topBound}], " +
                      $"initY={GetAxisPosition(_initialLocalPosition)}");
            transform.localPosition = newPos;
            _syncedSliderPosition   = newPos;
            _lastValue              = currentValue;
            RequestSerialization();

            ApplyValueToMaterials(currentValue);
        }

#if UNITY_UI
        public void BindSlider(string label, UnityEngine.UI.Slider slider, bool reversed, int propType, float min, float max, float defaultVal, Color defColor, bool indEnabled, Color indColor, bool indConditional)
        {
            EnsureMovementInitialized();
            targetSliders = new UnityEngine.UI.Slider[] { slider };
            sliderDirectionsReversed = new bool[] { reversed };
            propertyType = propType;
            valueMin = min;
            valueMax = max;
            defaultValue = defaultVal;
            defaultColor = defColor;
            isBound = true;
            indicatorEnabled = indEnabled;
            indicatorColor = indColor;
            indicatorConditional = indConditional;

            // Clear other bindings
            targetMaterials = null;
            materialPropertyId = "";
            targetUdonBehaviours = null;
            udonVariableName = "";

            // First-bind seed (see Bind() for the material overload comment).
            if (_syncedSliderPosition == Vector3.zero && currentValue == 0f)
            {
                float _seedRange = max - min;
                if (_seedRange > 0f)
                    currentValue = Mathf.Clamp01((defaultVal - min) / _seedRange);
            }

            // Write the display label so slider-target faders show their name
            // in-game (same as the material overload). Previously Slider/Udon
            // faders silently left labelText blank because this parameter
            // didn't exist.
            if (labelText != null) labelText.text = label;
            if (indicatorRenderer != null) indicatorRenderer.enabled = indEnabled;

            ApplyValueToMaterials(currentValue);
        }
#endif

        public void BindUdon(string label, UdonSharpBehaviour behav, string varName, int propType, float min, float max, float defaultVal, Color defColor, bool indEnabled, Color indColor, bool indConditional)
        {
            BindUdon(label, new UdonSharpBehaviour[] { behav }, varName, propType, min, max, defaultVal, defColor, indEnabled, indColor, indConditional);
        }

        // Overload that binds the fader to multiple Udon behaviours sharing
        // the same variable name — used by multi-target static faders (one
        // slider controlling the same variable on several UdonSharp
        // components at once, e.g. lightColorTint across all VRSL lights).
        public void BindUdon(string label, UdonSharpBehaviour[] behaviours, string varName, int propType, float min, float max, float defaultVal, Color defColor, bool indEnabled, Color indColor, bool indConditional)
        {
            EnsureMovementInitialized();
            targetUdonBehaviours = behaviours;
            udonVariableName = varName;
            propertyType = propType;
            valueMin = min;
            valueMax = max;
            defaultValue = defaultVal;
            defaultColor = defColor;
            isBound = true;
            indicatorEnabled = indEnabled;
            indicatorColor = indColor;
            indicatorConditional = indConditional;

            // Clear other bindings
            targetMaterials = null;
            materialPropertyId = "";
#if UNITY_UI
            targetSliders = null;
#endif
            sliderDirectionsReversed = null;

            // First-bind seed (see Bind() for the material overload comment).
            if (_syncedSliderPosition == Vector3.zero && currentValue == 0f)
            {
                float _seedRange = max - min;
                if (_seedRange > 0f)
                    currentValue = Mathf.Clamp01((defaultVal - min) / _seedRange);
            }

            // Write the display label so Udon-target faders show their name
            // in-game (same as the material overload). Previously Slider/Udon
            // faders silently left labelText blank because this parameter
            // didn't exist.
            if (labelText != null) labelText.text = label;
            if (indicatorRenderer != null) indicatorRenderer.enabled = indEnabled;

            ApplyValueToMaterials(currentValue);
        }

        public void Unbind()
        {
            if (isBound)
            {
                if (controller != null && controller.debugLogging)
                    Debug.Log($"[Fader] Unbind '{gameObject.name}': currentValue={currentValue}, " +
                          $"Y={GetAxisPosition(transform.localPosition)}, bounds=[{_bottomBound},{_topBound}]");
                // Snap the physical fader to the bottom so empty slots look clean,
                // but do NOT reset currentValue — the fader remembers its position
                // for the next time it's bound to an entry.
                float axisPos = _bottomBound;
                Vector3 newPos = SetAxisValue(transform.localPosition, axisPos);
                transform.localPosition = newPos;
                _syncedSliderPosition   = newPos;
            }

            isBound            = false;
            boundLinkIndex     = -1;
            targetMaterials    = null;
            materialPropertyId = "";
#if UNITY_UI
            targetSliders      = null;
#endif
            sliderDirectionsReversed = null;
            targetUdonBehaviours = null;
            udonVariableName   = "";
            indicatorEnabled   = false;

            if (labelText != null) labelText.text = "";
            if (indicatorRenderer != null) indicatorRenderer.enabled = false;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  FADER MODE
        // ────────────────────────────────────────────────────────────────────────

        public void SetFaderMode(int mode)
        {
            if (mode == 1) // Pickup mode
            {
                if (vrcPickup != null) vrcPickup.pickupable = true;
                if (faderRigidbody != null) faderRigidbody.isKinematic = false;
                // Disable whichever hand colliders this fader owns (could be own or none)
                if (leftHandCollider  != null) leftHandCollider.SetActive(false);
                if (rightHandCollider != null) rightHandCollider.SetActive(false);
                // Disable controller shared colliders for this fader (controller handles globally)
                if (controller != null)
                {
                    if (controller.sharedLeftHandCollider  != null) controller.sharedLeftHandCollider.SetActive(false);
                    if (controller.sharedRightHandCollider != null) controller.sharedRightHandCollider.SetActive(false);
                }
                // Stop standalone hand tracking in pickup mode
                if (controller == null) _standAloneHandTrackingEnabled = false;
            }
            else // Hand-collider mode
            {
                if (vrcPickup != null) vrcPickup.pickupable = false;
                if (faderRigidbody != null) faderRigidbody.isKinematic = true;
                if (leftHandCollider  != null) leftHandCollider.SetActive(true);
                if (rightHandCollider != null) rightHandCollider.SetActive(true);
                if (controller != null)
                {
                    if (controller.sharedLeftHandCollider  != null) controller.sharedLeftHandCollider.SetActive(true);
                    if (controller.sharedRightHandCollider != null) controller.sharedRightHandCollider.SetActive(true);
                }
                // Re-enable standalone hand tracking in hand-collider mode
                if (controller == null && _localPlayer != null && _localPlayer.IsUserInVR())
                    _standAloneHandTrackingEnabled = true;
            }
        }

        public void ResetPosition()
        {
            currentValue = defaultValue;
            float t = (valueMax - valueMin) > 0f
                      ? (currentValue - valueMin) / (valueMax - valueMin) : 0f;
            float axisPos = _bottomBound + (_topBound - _bottomBound) * t;
            Vector3 newPos = SetAxisValue(transform.localPosition, axisPos);
            transform.localPosition = newPos;
            _syncedSliderPosition   = newPos;
            ApplyValueToMaterials(currentValue);
            SyncValue();
        }

        /// <summary>
        /// Directly restore a normalized (0–1) value saved by a preset, moving the
        /// handle to the corresponding physical position and applying to materials.
        /// </summary>
        public void RestoreValue(float normalizedValue)
        {
            currentValue = Mathf.Clamp01(normalizedValue);
            float axisPos = _bottomBound + (_topBound - _bottomBound) * currentValue;
            Vector3 newPos = SetAxisValue(transform.localPosition, axisPos);
            transform.localPosition = newPos;
            _syncedSliderPosition   = newPos;
            ApplyValueToMaterials(currentValue);
            SyncValue();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  TRIGGER EVENTS — routes to controller or handles locally
        // ────────────────────────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            // Determine which hand object touched this fader.
            // In controller mode: compare against the controller's shared colliders.
            // In standalone mode: compare against the fader's own hand colliders.
            bool isLeft, isRight;
            DetectHand(other.gameObject, out isLeft, out isRight);
            if (!isLeft && !isRight) return;

            if (controller != null)
            {
                // Route to controller — it decides which fader gets grab ownership
                controller.OnFaderTriggerEnter(this, isRight);
            }
            else
            {
                // Standalone: track locally
                if (isLeft)  _inLeftTrigger  = true;
                if (isRight) _inRightTrigger = true;
            }

            // Haptics regardless of mode
            if (_localPlayer != null)
            {
                if (isLeft)  _localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left,  1f, 1f, 0.2f);
                if (isRight) _localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 1f, 1f, 0.2f);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;

            bool isLeft, isRight;
            DetectHand(other.gameObject, out isLeft, out isRight);
            if (!isLeft && !isRight) return;

            if (controller != null)
            {
                controller.OnFaderTriggerExit(this, isRight);
            }
            else
            {
                if (isLeft)  _inLeftTrigger  = false;
                if (isRight) _inRightTrigger = false;
            }

            if (_localPlayer != null)
            {
                if (isLeft)  _localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left,  1f, 1f, 0.2f);
                if (isRight) _localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 1f, 1f, 0.2f);
            }
        }

        // Detect which hand object (own or controller-shared) touched us
        private void DetectHand(GameObject go, out bool isLeft, out bool isRight)
        {
            isLeft  = (leftHandCollider  != null && go == leftHandCollider);
            isRight = (rightHandCollider != null && go == rightHandCollider);

            if (!isLeft && !isRight && controller != null)
            {
                isLeft  = controller.sharedLeftHandCollider  != null && go == controller.sharedLeftHandCollider;
                isRight = controller.sharedRightHandCollider != null && go == controller.sharedRightHandCollider;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  INPUT GRAB — standalone only; controller handles its own InputGrab
        // ────────────────────────────────────────────────────────────────────────

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            // Controller-managed faders don't handle their own grab — the controller does.
            if (controller != null) return;

            if (args.handType == HandType.LEFT)  _leftGrabbed  = value;
            if (args.handType == HandType.RIGHT) _rightGrabbed = value;

            // Release: sync final position
            if (!value) SyncValue();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  PICKUP EVENTS
        // ────────────────────────────────────────────────────────────────────────

        public override void OnPickup() { _isPickupHeld = true; }

        public override void OnDrop()
        {
            _isPickupHeld = false;

            // Zero physics so the knob doesn't fly off after release
            if (faderRigidbody != null)
            {
                faderRigidbody.velocity        = Vector3.zero;
                faderRigidbody.angularVelocity = Vector3.zero;
            }

            // Constrain final position to the fader axis and reset rotation
            float curAxisPos = GetAxisPosition(transform.localPosition);
            float clampedPos = Mathf.Clamp(curAxisPos, _bottomBound, _topBound);
            transform.localPosition = SetAxisValue(_initialLocalPosition, clampedPos);
            transform.localRotation = Quaternion.identity;

            // Recompute currentValue from the final constrained position so the
            // stored value exactly matches where the knob actually landed.
            float finalNormalized = (_topBound - _bottomBound) > 0f
                ? Mathf.InverseLerp(_bottomBound, _topBound, clampedPos) : 0f;
            currentValue = finalNormalized;
            _lastValue   = finalNormalized;
            ApplyValueToMaterials(currentValue);

            // Report final value to the controller for dynamic fader memory.
            if (controller != null && boundLinkIndex >= 0)
                controller.OnFaderLinkValueChanged(boundLinkIndex, currentValue);

            _syncedSliderPosition = transform.localPosition;
            SyncValue();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  UPDATE LOOP — standalone only; controller drives position in its Update
        // ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Pickup mode works the same in both modes
            if (_isPickupHeld)
            {
                UpdatePickupModePosition();
                return;
            }

            // In controller-managed mode the controller's Update() drives our position.
            if (controller != null) return;

            // Move standalone hand colliders to track the VR player's finger bones
            if (_standAloneHandTrackingEnabled)
            {
                if (rightHandCollider != null)
                {
                    Vector3 rPos = _localPlayer.GetBonePosition(HumanBodyBones.RightIndexDistal);
                    if (rPos.sqrMagnitude > 0.001f)
                        rightHandCollider.transform.position = rPos;
                }
                if (leftHandCollider != null)
                {
                    Vector3 lPos = _localPlayer.GetBonePosition(HumanBodyBones.LeftIndexDistal);
                    if (lPos.sqrMagnitude > 0.001f)
                        leftHandCollider.transform.position = lPos;
                }
            }

            // ── Standalone hand-collider mode ──
            bool rightActive = _inRightTrigger && _rightGrabbed;
            bool leftActive  = _inLeftTrigger  && _leftGrabbed;
            if (!rightActive && !leftActive) return;
            if (!isBound) return;

            Transform handTransform = rightActive
                ? (rightHandCollider != null ? rightHandCollider.transform : null)
                : (leftHandCollider  != null ? leftHandCollider.transform  : null);
            if (handTransform == null) return;

            MoveToHand(handTransform);
        }

        private void UpdatePickupModePosition()
        {
            float curAxisPos = GetAxisPosition(transform.localPosition);
            float clampedPos = Mathf.Clamp(curAxisPos, _bottomBound, _topBound);
            Vector3 constrained = SetAxisValue(_initialLocalPosition, clampedPos);
            transform.localPosition = constrained;
            transform.localRotation = Quaternion.identity;

            float normalizedValue = (_topBound - _bottomBound) > 0f
                ? Mathf.InverseLerp(_bottomBound, _topBound, clampedPos) : 0f;

            if (!Mathf.Approximately(normalizedValue, currentValue))
            {
                if (!Networking.IsOwner(gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);
                currentValue          = normalizedValue;
                _syncedSliderPosition = transform.localPosition;
                ApplyValueToMaterials(currentValue);
                _lastValue = currentValue;
                SyncValue();

                // Report value change to the controller for dynamic fader memory.
                if (controller != null && boundLinkIndex >= 0)
                    controller.OnFaderLinkValueChanged(boundLinkIndex, currentValue);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  CONTROLLER-MANAGED POSITION DRIVE
        //  Called every frame by EnigmaController.UpdateControlledFaderPositions()
        //  for whichever fader is currently the active left or right grab target.
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Drive this fader's position from a shared hand-tracker transform.
        /// Only called by the EnigmaController (controller-managed mode).
        /// </summary>
        public void UpdateFromController(Transform handTransform)
        {
            if (!isBound) return;
            MoveToHand(handTransform);
        }

        /// <summary>
        /// Called by the controller after releasing the grab to sync the final value.
        /// </summary>
        public void SyncValue()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            RequestSerialization();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  SHARED MOVEMENT HELPER
        // ────────────────────────────────────────────────────────────────────────

        private void MoveToHand(Transform handTransform)
        {
            // Convert hand world position into the same local space as the knob
            // (both the knob and the limiters are children of the same parent).
            Vector3 handLocalPos = transform.parent.InverseTransformPoint(handTransform.position);
            float handAxisPos = GetAxisPosition(handLocalPos);
            float clampedPos  = Mathf.Clamp(handAxisPos, _bottomBound, _topBound);
            transform.localPosition = SetAxisValue(transform.localPosition, clampedPos);

            float normalizedValue = (_topBound - _bottomBound) > 0f
                ? Mathf.InverseLerp(_bottomBound, _topBound, clampedPos) : 0f;
            currentValue = normalizedValue;

            if (!Mathf.Approximately(currentValue, _lastValue))
            {
                if (!Networking.IsOwner(gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);
                _syncedSliderPosition = transform.localPosition;
                ApplyValueToMaterials(currentValue);
                _lastValue = currentValue;

                // Report value change to the controller for dynamic fader memory.
                if (controller != null && boundLinkIndex >= 0)
                    controller.OnFaderLinkValueChanged(boundLinkIndex, currentValue);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  VALUE APPLICATION
        // ────────────────────────────────────────────────────────────────────────

        private void ApplyValueToMaterials(float normalizedValue)
        {
            float mapped = Mathf.Lerp(valueMin, valueMax, normalizedValue);

            // Hue-shift is computed ONCE here (not per-material) so every
            // downstream target (materials, Udon, indicator) sees the same
            // value. Also updates _currentComputedColor even for Udon-only
            // faders — previously the color cache only advanced when the
            // fader had a bound material, which left the indicator ring
            // stuck on the baseline colour for any Udon-only color fader.
            //
            // Color.RGBToHSV preserves HDR intensity (V can exceed 1 when
            // the base color is HDR), and Color.HSVToRGB reproduces the
            // same intensity at the new hue — so HDR colors hue-shift
            // correctly without being clamped back to SDR.
            Color shiftedColor = defaultColor;
            if (propertyType == 1)
            {
                float h, s, v;
                Color.RGBToHSV(defaultColor, out h, out s, out v);
                float shiftAmount = (valueMax / 360f) * normalizedValue;
                float newHue  = (h + shiftAmount) % 1f;
                shiftedColor = Color.HSVToRGB(newHue, s, v, hdr: true);
                shiftedColor.a = defaultColor.a;
                _currentComputedColor = shiftedColor;
            }

            if (targetMaterials != null && !string.IsNullOrEmpty(materialPropertyId))
            {
                foreach (var mat in targetMaterials)
                {
                    if (mat == null) continue;

                    if (propertyType == 1) // Color — hue-shift from defaultColor baseline
                        mat.SetColor(materialPropertyId, shiftedColor);
                    else                   // Float / Range
                        mat.SetFloat(materialPropertyId, mapped);
                }
            }

            // Apply to UI Slider targets
            UpdateTargetSliders(normalizedValue);

            // Apply to Udon variable targets. For Color variables we pass
            // the hue-shifted Color object so the Udon program receives an
            // actual Color — previously this path passed the float `mapped`
            // value, which left Udon color variables unchanged (Udon's
            // SetProgramVariable silently no-ops when the value type
            // doesn't match the variable's declared type).
            if (propertyType == 1)
                UpdateTargetUdon(shiftedColor);
            else
                UpdateTargetUdon(mapped);

            UpdateIndicatorColor(normalizedValue);
        }

        private void UpdateIndicatorColor(float t)
        {
            if (indicatorRenderer == null || !indicatorEnabled) return;

            // Conditional mode: only light when value is above minimum
            if (indicatorConditional && Mathf.Approximately(t, 0f))
            {
                indicatorRenderer.GetPropertyBlock(_indicatorMpb);
                _indicatorMpb.SetColor("_Color", Color.black);
                _indicatorMpb.SetColor("_EmissionColor", Color.black);
                indicatorRenderer.SetPropertyBlock(_indicatorMpb);
                return;
            }

            // For color-property faders, show the actually-applied material
            // color on the indicator ring. <see cref="_currentComputedColor"/>
            // is the hue-shifted output of the last <c>ApplyValueToMaterials</c>
            // call, so at t=0 it equals <c>defaultColor</c> (the baseline pink,
            // cyan, etc.) and at higher t it reflects the live hue shift.
            //
            // The previous implementation did <c>Color.HSVToRGB(t, 1f, 1f)</c>,
            // which treated the slider's normalized position as a hue at full
            // saturation/value. That ignored <c>defaultColor</c> entirely and
            // made every color fader's ring show pure red at t=0 (hue 0) —
            // regardless of whether the fader's actual color was pink, cyan,
            // green, etc.
            Color c = propertyType == 1
                ? _currentComputedColor
                : indicatorColor;
            float emission = t;

            indicatorRenderer.GetPropertyBlock(_indicatorMpb);
            _indicatorMpb.SetColor("_Color", c);
            _indicatorMpb.SetColor("_EmissionColor", c * emission);
            indicatorRenderer.SetPropertyBlock(_indicatorMpb);
        }

        // ────────────────────────────────────────────────────────────────────────
        //  UI SLIDER TARGETS
        // ────────────────────────────────────────────────────────────────────────

#if UNITY_UI
        private void UpdateTargetSliders(float normalizedValue)
        {
            if (targetSliders == null) return;
            for (int i = 0; i < targetSliders.Length; i++)
            {
                var slider = targetSliders[i];
                if (slider == null) continue;

                bool reversed = sliderDirectionsReversed != null
                             && i < sliderDirectionsReversed.Length
                             && sliderDirectionsReversed[i];

                float sliderRange = slider.maxValue - slider.minValue;
                if (reversed)
                {
                    float reversedVal = slider.maxValue - (normalizedValue * sliderRange);
                    slider.value = Mathf.Clamp(reversedVal, slider.minValue, slider.maxValue);
                }
                else
                {
                    float sliderVal = slider.minValue + (normalizedValue * sliderRange);
                    slider.value = Mathf.Clamp(sliderVal, slider.minValue, slider.maxValue);
                }
            }
        }
#else
        private void UpdateTargetSliders(float normalizedValue) { }
#endif

        // ────────────────────────────────────────────────────────────────────────
        //  UDON VARIABLE TARGETS
        // ────────────────────────────────────────────────────────────────────────

        private void UpdateTargetUdon(float mappedValue)
        {
            if (targetUdonBehaviours == null || string.IsNullOrEmpty(udonVariableName)) return;
            for (int i = 0; i < targetUdonBehaviours.Length; i++)
            {
                if (targetUdonBehaviours[i] != null)
                    targetUdonBehaviours[i].SetProgramVariable(udonVariableName, mappedValue);
            }
        }

        // Overload for Color-typed Udon variables. Passing a Color through
        // SetProgramVariable writes to a Color field on the target
        // UdonSharpBehaviour (UdonBehaviour for raw); the float overload
        // above silently fails on Color-typed targets because Udon checks
        // the value type against the declared variable type and no-ops on
        // mismatch.
        private void UpdateTargetUdon(Color colorValue)
        {
            if (targetUdonBehaviours == null || string.IsNullOrEmpty(udonVariableName)) return;
            for (int i = 0; i < targetUdonBehaviours.Length; i++)
            {
                if (targetUdonBehaviours[i] != null)
                    targetUdonBehaviours[i].SetProgramVariable(udonVariableName, colorValue);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────────────────

        private float GetAxisPosition(Vector3 pos)
        {
            switch (movementAxis)
            {
                case FaderAxis.X: return pos.x;
                case FaderAxis.Z: return pos.z;
                default:          return pos.y;
            }
        }

        private Vector3 SetAxisValue(Vector3 pos, float value)
        {
            switch (movementAxis)
            {
                case FaderAxis.X: pos.x = value; break;
                case FaderAxis.Z: pos.z = value; break;
                default:          pos.y = value; break;
            }
            return pos;
        }

        private void CacheMovementBounds()
        {
            if (bottomLimiter != null && topLimiter != null)
            {
                _bottomBound = GetAxisPosition(bottomLimiter.transform.localPosition);
                _topBound    = GetAxisPosition(topLimiter.transform.localPosition);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  TRIGGER ZONE TRACKING (for distance-based fader selection)
        // ────────────────────────────────────────────────────────────────────────

        public void SetInTrigger(bool isRight, bool value)
        {
            if (isRight) _inRightTriggerZone = value;
            else         _inLeftTriggerZone  = value;
        }

        public bool IsInTrigger(bool isRight)
        {
            return isRight ? _inRightTriggerZone : _inLeftTriggerZone;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  STEP QUANTIZATION (for compact preset storage)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current fader position as a discrete step (0 to FaderStepCount-1).
        /// Used by the preset system to minimize serialization size.
        /// </summary>
        public int GetPositionStep()
        {
            float normalized = Mathf.Clamp01(currentValue);
            int step = Mathf.RoundToInt(normalized * (FaderStepCount - 1));
            return Mathf.Clamp(step, 0, FaderStepCount - 1);
        }

        /// <summary>
        /// Sets the fader position from a discrete step value (0 to FaderStepCount-1).
        /// </summary>
        public void SetPositionFromStep(int step)
        {
            step = Mathf.Clamp(step, 0, FaderStepCount - 1);
            float normalizedValue = (float)step / (FaderStepCount - 1);
            RestoreValue(normalizedValue);
        }

        // ────────────────────────────────────────────────────────────────────────
        //  COMPUTED COLOR ACCESS
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the last computed color (for color-type properties) without recalculating.
        /// </summary>
        public Color GetCurrentComputedColor()
        {
            return _currentComputedColor;
        }
    }
}

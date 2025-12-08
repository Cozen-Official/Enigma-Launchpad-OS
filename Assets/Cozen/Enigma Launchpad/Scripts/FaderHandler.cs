// Portions of this behaviour are adapted from Wo1fie's original fader implementation.
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Common;
using UnityEngine.Serialization;

namespace Cozen
{
    /// <summary>
    /// FaderHandler is the physical slider component that controls shader property values.
    /// It handles VR hand interactions and syncs the slider position across the network.
    /// Similar to ButtonHandler, one FaderHandler component exists per fader.
    /// </summary>
    public enum FaderAxis
    {
        X,
        Y,
        Z
    }

    public class FaderHandler : UdonSharpBehaviour
    {
        [Tooltip("Renderer driving collider for left hand interactions.")]
        [SerializeField] private GameObject leftHandCollider;
        [Tooltip("Renderer driving collider for right hand interactions.")]
        [SerializeField] private GameObject rightHandCollider;
        [Tooltip("Local transform limiter for the bottom boundary of the slider.")]
        [FormerlySerializedAs("leftLimiter")]
        [SerializeField] private GameObject bottomLimiter;
        [Tooltip("Local transform limiter for the top boundary of the slider.")]
        [FormerlySerializedAs("rightLimiter")]
        [SerializeField] private GameObject topLimiter;

        [Tooltip("Axis the fader moves along.")]
        [SerializeField] private FaderAxis movementAxis = FaderAxis.X;

        [Header("System References")]
        [Tooltip("The parent FaderSystemHandler that coordinates multi-fader interactions.")]
        public FaderSystemHandler faderSystemHandler;
        [Tooltip("Index of this fader in the FaderSystemHandler's fader array.")]
        public int faderIndex = -1;

        [Header("Display References")]
        [Tooltip("Renderer for this fader's indicator light.")]
        public Renderer indicatorRenderer;
        [Tooltip("TMP text for this fader's label display.")]
        public TMPro.TMP_Text labelText;

        [Tooltip("Materials driven by this fader.")]
        public Material[] targetMaterials;
        [Tooltip("Shader property name driven by this fader.")]
        public string materialPropertyId = string.Empty;
        [Tooltip("Property type: 0=Float, 1=Range, 2=Color")]
        public int propertyType = 0;

        [UdonSynced] public float currentValue = 0f;
        [UdonSynced] private Vector3 syncedSliderPosition;

        [Tooltip("Value to restore on reset.")]
        public float defaultValue = 0f;
        [Tooltip("Default color for color properties.")]
        public Color defaultColor = Color.white;
        [Tooltip("Minimum fader value.")]
        public float valueMin = 0f;
        [Tooltip("Maximum fader value.")]
        public float valueMax = 1f;

        private GameObject _sliderObject;
        private MeshRenderer _sliderRenderer;
        private Material[] _sliderMaterials;
        private float _bottomLimit;
        private float _topLimit;
        private VRCPlayerApi _currentPlayer;

        private bool _rightGrabbed;
        private bool _leftGrabbed;
        private bool _inTrigger;
        private bool _inLeftTrigger;
        private bool _inRightTrigger;
        private float _lastValue;

        // Tracks if this fader has been granted grab permission by FaderSystemHandler
        private bool _leftGrabPermitted;
        private bool _rightGrabPermitted;

        public override void OnDeserialization()
        {
            if (currentValue != _lastValue)
            {
                UpdateTargetFloat(currentValue);
                _lastValue = currentValue;
                if (_sliderObject != null)
                {
                    _sliderObject.transform.localPosition = syncedSliderPosition;
                }
            }
        }

        public void Start()
        {
            _sliderObject = gameObject;
            _currentPlayer = Networking.LocalPlayer;

            _bottomLimit = bottomLimiter != null ? GetAxisValue(bottomLimiter.transform.localPosition) : 0f;
            _topLimit = topLimiter != null ? GetAxisValue(topLimiter.transform.localPosition) : 0f;

            _sliderRenderer = gameObject.GetComponent<MeshRenderer>();
            if (_sliderRenderer != null)
            {
                _sliderMaterials = _sliderRenderer.materials;
                if (_sliderMaterials != null && _sliderMaterials.Length > 0)
                {
                    _sliderMaterials[0].DisableKeyword("_EMISSION");
                    _sliderMaterials[0].globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
            }

            ResetFaderPosition();
            OnDeserialization();
        }

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
            {
                _leftGrabbed = value;
            }
            if (args.handType == HandType.RIGHT)
            {
                _rightGrabbed = value;
            }

            // Notify FaderSystemHandler of grab state change for re-evaluation
            if (faderSystemHandler != null)
            {
                bool isRightHand = args.handType == HandType.RIGHT;
                if (value)
                {
                    // Grab started - re-evaluate which fader should be grabbed
                    faderSystemHandler.OnGrabStarted(isRightHand);
                }
                else
                {
                    // Grab ended - clear the lock
                    faderSystemHandler.OnGrabEnded(isRightHand);
                }
            }
        }

        private void Update()
        {
            if (!_inTrigger || (!_rightGrabbed && !_leftGrabbed))
            {
                return;
            }

            // Check if this fader is permitted to be grabbed by the active hand
            bool rightActive = _inRightTrigger && _rightGrabbed && _rightGrabPermitted;
            bool leftActive = _inLeftTrigger && _leftGrabbed && _leftGrabPermitted;

            if (!rightActive && !leftActive)
            {
                return;
            }

            if (!_currentPlayer.IsOwner(gameObject))
            {
                Networking.SetOwner(_currentPlayer, _sliderObject);
            }

            // Use the hand that has permission (prefer right if both have permission)
            Transform handData = rightActive ? rightHandCollider.transform : leftHandCollider.transform;
            float handAxisPos = handData != null ? GetAxisValue(handData.localPosition) : 0f;
            float clampedPos = Mathf.Clamp(handAxisPos, _bottomLimit, _topLimit);

            Vector3 newPos = SetAxisValue(_sliderObject.transform.localPosition, clampedPos);
            _sliderObject.transform.localPosition = newPos;

            float normalizedValue = Mathf.InverseLerp(_bottomLimit, _topLimit, clampedPos);
            currentValue = Mathf.Lerp(valueMin, valueMax, normalizedValue);

            if (currentValue != _lastValue)
            {
                syncedSliderPosition = _sliderObject.transform.localPosition;
                OnDeserialization();
                _lastValue = currentValue;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_sliderMaterials != null && _sliderMaterials.Length > 1)
            {
                _sliderMaterials[1].EnableKeyword("_EMISSION");
                _sliderMaterials[1].globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            GameObject otherObject = other != null ? other.gameObject : null;
            bool isLeftCollider = otherObject != null && otherObject == leftHandCollider;
            bool isRightCollider = otherObject != null && otherObject == rightHandCollider;

            if (isLeftCollider)
            {
                _inTrigger = true;
                _inLeftTrigger = true;
                _currentPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, 1f, 1f, 0.2f);
                // Request grab permission from FaderSystemHandler
                if (faderSystemHandler != null)
                {
                    faderSystemHandler.OnFaderTriggerEnter(faderIndex, false);
                }
            }
            else if (isRightCollider)
            {
                _inTrigger = true;
                _inRightTrigger = true;
                _currentPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 1f, 1f, 0.2f);
                // Request grab permission from FaderSystemHandler
                if (faderSystemHandler != null)
                {
                    faderSystemHandler.OnFaderTriggerEnter(faderIndex, true);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_sliderMaterials != null && _sliderMaterials.Length > 1)
            {
                _sliderMaterials[1].EnableKeyword("_EMISSION");
                _sliderMaterials[1].globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            GameObject otherObject = other != null ? other.gameObject : null;
            bool isLeftCollider = otherObject != null && otherObject == leftHandCollider;
            bool isRightCollider = otherObject != null && otherObject == rightHandCollider;

            if (isLeftCollider)
            {
                _inLeftTrigger = false;
                // Only set _inTrigger to false when both hands have exited
                _inTrigger = _inLeftTrigger || _inRightTrigger;
                _currentPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, 1f, 1f, 0.2f);
                // Notify FaderSystemHandler that we exited - it will clear grab permission
                if (faderSystemHandler != null)
                {
                    faderSystemHandler.OnFaderTriggerExit(faderIndex, false);
                }
            }
            else if (isRightCollider)
            {
                _inRightTrigger = false;
                // Only set _inTrigger to false when both hands have exited
                _inTrigger = _inLeftTrigger || _inRightTrigger;
                _currentPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 1f, 1f, 0.2f);
                // Notify FaderSystemHandler that we exited - it will clear grab permission
                if (faderSystemHandler != null)
                {
                    faderSystemHandler.OnFaderTriggerExit(faderIndex, true);
                }
            }
        }

        public void UpdateTargetFloat(float val)
        {
            int propID = VRCShader.PropertyToID(materialPropertyId);
            int count = targetMaterials != null ? targetMaterials.Length : 0;
            
            // Check if this is a color property (propertyType == 2)
            if (propertyType == 2)
            {
                // Color property: apply hue shift
                UpdateTargetColor(val);
            }
            else
            {
                // Float/Range property: apply value directly
                for (int i = 0; i < count; i++)
                {
                    Material target = targetMaterials[i];
                    if (target != null)
                    {
                        target.SetFloat(propID, val);
                    }
                }
            }
        }

        private void UpdateTargetColor(float val)
        {
            int propID = VRCShader.PropertyToID(materialPropertyId);
            int count = targetMaterials != null ? targetMaterials.Length : 0;
            
            // Convert default color to HSV
            Color.RGBToHSV(defaultColor, out float h, out float s, out float v);
            
            // Calculate hue shift based on fader position
            // For color properties, the editor sets valueMin=0 and valueMax=maxShift (0-360 degrees)
            // The fader position (val) is in the range [valueMin, valueMax], so normalize it to [0, 1]
            float normalizedValue = valueMax != valueMin ? (val - valueMin) / (valueMax - valueMin) : 0f;
            normalizedValue = Mathf.Clamp01(normalizedValue);
            float maxShiftDegrees = valueMax;
            float hueShift = (maxShiftDegrees / 360f) * normalizedValue;
            
            // Apply hue shift (wrapping around at 1.0)
            float newHue = (h + hueShift) % 1.0f;
            if (newHue < 0f)
            {
                newHue += 1.0f;
            }
            
            // Convert back to RGB
            Color shiftedColor = Color.HSVToRGB(newHue, s, v);
            shiftedColor.a = defaultColor.a; // Preserve alpha
            
            // Apply to all target materials
            for (int i = 0; i < count; i++)
            {
                Material target = targetMaterials[i];
                if (target != null)
                {
                    target.SetColor(propID, shiftedColor);
                }
            }
        }

        public void ResetFaderPosition()
        {
            currentValue = defaultValue;
            float t = valueMax != valueMin ? (currentValue - valueMin) / (valueMax - valueMin) : 0f;
            float axisPos = _bottomLimit + (_topLimit - _bottomLimit) * t;
            GameObject slider = _sliderObject != null ? _sliderObject : gameObject;
            Vector3 newPosition = SetAxisValue(slider.transform.localPosition, axisPos);
            syncedSliderPosition = newPosition;
            _sliderObject = slider;
            if (_sliderObject != null)
            {
                _sliderObject.transform.localPosition = newPosition;
            }
            UpdateTargetFloat(currentValue);
            _lastValue = currentValue;
        }

        private float GetAxisValue(Vector3 position)
        {
            switch (movementAxis)
            {
                case FaderAxis.Y:
                    return position.y;
                case FaderAxis.Z:
                    return position.z;
                default:
                    return position.x;
            }
        }

        private Vector3 SetAxisValue(Vector3 position, float value)
        {
            switch (movementAxis)
            {
                case FaderAxis.Y:
                    position.y = value;
                    break;
                case FaderAxis.Z:
                    position.z = value;
                    break;
                default:
                    position.x = value;
                    break;
            }

            return position;
        }

        /// <summary>
        /// Updates the label text displayed for this fader.
        /// Called by FaderSystemHandler when fader configuration changes.
        /// </summary>
        public void SetLabel(string label)
        {
            if (labelText != null)
            {
                labelText.text = label ?? string.Empty;
            }
        }

        /// <summary>
        /// Updates the indicator color for this fader.
        /// Called by FaderSystemHandler when indicator state changes.
        /// </summary>
        public void SetIndicatorColor(Color color, float emission)
        {
            if (indicatorRenderer == null)
            {
                return;
            }

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            indicatorRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_Color", color);
            propertyBlock.SetColor("_EmissionColor", color * emission);
            indicatorRenderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Assign shared hand colliders managed by the fader system.
        /// </summary>
        public void SetHandColliders(GameObject left, GameObject right)
        {
            leftHandCollider = left;
            rightHandCollider = right;
        }

        /// <summary>
        /// Grants or revokes grab permission for a specific hand.
        /// Called by FaderSystemHandler to coordinate which fader is grabbed.
        /// </summary>
        /// <param name="isRightHand">True for right hand, false for left hand</param>
        /// <param name="permitted">True to grant permission, false to revoke</param>
        public void SetGrabPermission(bool isRightHand, bool permitted)
        {
            if (isRightHand)
            {
                _rightGrabPermitted = permitted;
            }
            else
            {
                _leftGrabPermitted = permitted;
            }
        }

        /// <summary>
        /// Gets the world position of this fader for distance calculations.
        /// </summary>
        public Vector3 GetWorldPosition()
        {
            return transform.position;
        }

        /// <summary>
        /// Checks if this fader is currently in the trigger zone for a specific hand.
        /// </summary>
        public bool IsInTrigger(bool isRightHand)
        {
            return isRightHand ? _inRightTrigger : _inLeftTrigger;
        }
    }
}

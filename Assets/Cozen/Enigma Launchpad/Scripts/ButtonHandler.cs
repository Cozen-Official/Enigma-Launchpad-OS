using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ButtonHandler : UdonSharpBehaviour
    {
        [Header("Button Settings")]
        [Tooltip("The index of this button on the current page (0-8).")]
        public int buttonIndex = 0;
        
        [Tooltip("Reference to the master Enigma Launchpad script.")]
        public EnigmaLaunchpad enigmaLaunchpad;
        
        [Header("Folder Navigation")]
        [Tooltip("Cycle to the previous folder when pressed.")]
        public bool IsFolderLeftButton = false;
        [Tooltip("Cycle to the next folder when pressed.")]
        public bool IsFolderRightButton = false;
        
        [Header("Paging / Skybox / Auto")]
        [Tooltip("Check this if this button should trigger the Next Page event.")]
        public bool IsDownButton = false;
        [Tooltip("Check this if this button should trigger the Previous Page event.")]
        public bool IsUpButton = false;
        [Tooltip("Check this if this button should toggle the auto-change feature.")]
        public bool IsAutoChangeButton = false;
        
        [Header("Reset")]
        [Tooltip("If true, this button performs an emergency reset of the launchpad.")]
        public bool IsResetButton = false;
        
        [Header("Screen")]
        [Tooltip("If true, this button toggles screens in the ScreenHandler.")]
        public bool IsScreenButton = false;
        
        [Header("Visuals")]
        [Tooltip("Renderer to flash (if null, will try GetComponent<Renderer>() )")]
        public Renderer buttonRenderer;
        [Tooltip("Optional button label.")]
        public TMP_Text buttonText;
        [Tooltip("Flash duration in seconds")]
        public float flashDuration = 0.5f;
        public float flashEmissionIntensity = 2f;
        
        // Internal
        private const float UnifiedEmission = 1f;
        private const float AutoChangeHueSpeed = 60f;
        private MaterialPropertyBlock _mpb;
        private bool _capturedOriginal;
        private Color _origColor = Color.white;
        private Color _origEmission = Color.white;
        private bool _hasOriginalTextColor;
        private Color _originalTextColor = Color.white;
        private Color _targetColor = Color.white;
        private bool _targetInteractable;
        private bool _autoChangeActive;
        private bool _autoChangeLoopScheduled;
        private float _autoHueOffset;
        
        public void Start()
        {
            if (buttonRenderer == null)
            buttonRenderer = GetComponentInChildren<Renderer>(true);
            if (buttonText == null)
            buttonText = GetComponentInChildren<TMP_Text>(true);
            
            if (buttonText != null && !_hasOriginalTextColor)
            {
                _originalTextColor = buttonText.color;
                _hasOriginalTextColor = true;
            }
            
            CaptureOriginal();
            ResetButton();
        }
        
        public override void Interact()
        {
            if (enigmaLaunchpad == null)
            {
                Debug.LogError("EnigmaLaunchpad reference is not set on " + gameObject.name);
                return;
            }
            
            if (!enigmaLaunchpad.CanLocalUserInteract())
            {
                return;
            }
            
            bool shouldFlash = false;
            
            if (IsFolderLeftButton)
            {
                enigmaLaunchpad.CycleFolder(-1);
                shouldFlash = true;
            }
            else if (IsFolderRightButton)
            {
                enigmaLaunchpad.CycleFolder(+1);
                shouldFlash = true;
            }
            else if (IsAutoChangeButton)
            {
                enigmaLaunchpad.ToggleAutoChange();
            }
            else if (IsDownButton)
            {
                if (enigmaLaunchpad.IsMochieFolderActive())
                enigmaLaunchpad.ChangeMochiePage(1);
                else
                enigmaLaunchpad.ChangePage(1);
                shouldFlash = true;
            }
            else if (IsUpButton)
            {
                if (enigmaLaunchpad.IsMochieFolderActive())
                enigmaLaunchpad.ChangeMochiePage(-1);
                else
                enigmaLaunchpad.ChangePage(-1);
                shouldFlash = true;
            }
            else if (IsResetButton)
            {
                enigmaLaunchpad.ResetLaunchpad();
                shouldFlash = true;
            }
            else if (IsScreenButton)
            {
                enigmaLaunchpad.HandleScreenButtonPress(buttonIndex);
                shouldFlash = true;
            }
            else
            {
                enigmaLaunchpad.HandleItemSelect(buttonIndex);
                // item select typically no flash
            }
            
            if (shouldFlash)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(FlashButton));
            }
        }
        
        public void UpdateVisual(string label, Color color, bool interactable,
        bool formatLabel = false, bool formatLabelForStats = false, bool formatFirstLineOnly = false)
        {
            if (buttonText == null)
            {
                buttonText = GetComponentInChildren<TMP_Text>(true);
            }
            if (buttonRenderer == null)
            {
                buttonRenderer = GetComponentInChildren<Renderer>(true);
            }
            
            if (buttonText != null)
            {
                string processedLabel = string.IsNullOrEmpty(label)
                ? string.Empty
                : ApplyLabelFormatting(label, formatLabel, formatLabelForStats, formatFirstLineOnly);
                buttonText.text = processedLabel;
                if (!_hasOriginalTextColor)
                {
                    _originalTextColor = buttonText.color;
                    _hasOriginalTextColor = true;
                }
                
                var appliedColor = interactable
                ? new Color(_originalTextColor.r, _originalTextColor.g, _originalTextColor.b, 1f)
                : new Color(_originalTextColor.r, _originalTextColor.g, _originalTextColor.b, 0.5f);
                buttonText.color = appliedColor;
            }
            
            CaptureOriginal();
            
            // Always update interactable state
            _targetInteractable = interactable;
            
            // Refuse color updates when auto-change animation is running
            if (!_autoChangeActive)
            {
                _targetColor = color;
            }
            
            // Always apply visual changes to ensure consistent emission based on interactable state
            ApplyVisualColors();
        }
        
        /// <summary>
        /// Enable or disable auto-change hue cycling animation.
        /// When enabled, this button will cycle through colors and refuse external color updates.
        /// </summary>
        public void SetAutoChangeActive(bool active)
        {
            _autoChangeActive = active;

            if (_autoChangeActive)
            {
                if (!_autoChangeLoopScheduled)
                {
                    _autoChangeLoopScheduled = true;
                    // Always kick the hue loop so it can't stall if the state desyncs.
                    SendCustomEventDelayedFrames(nameof(UpdateAutoChangeVisual), 1);
                }
            }
            else
            {
                _autoChangeLoopScheduled = false;
                if (_autoHueOffset != 0f)
                {
                    _autoHueOffset = 0f;
                }

            }

            // Apply current target colors to sync the renderer with the latest state.
            ApplyVisualColors();
        }
        
        private void ApplyVisualColors()
        {
            if (buttonRenderer == null)
            {
                return;
            }

            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            Color appliedColor = _autoChangeActive
            ? CosinePalette(_autoHueOffset)
            : _targetColor;
            float appliedEmission = _targetInteractable ? UnifiedEmission : 0f;

            buttonRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", appliedColor);
            _mpb.SetColor("_EmissionColor", appliedColor);
            _mpb.SetFloat("_EmissionStrength", appliedEmission);
            buttonRenderer.SetPropertyBlock(_mpb);
        }
        
        public void UpdateAutoChangeVisual()
        {
            if (!_autoChangeActive)
            {
                _autoChangeLoopScheduled = false;
                _autoHueOffset = 0f;
                ApplyVisualColors();
                return;
            }

            _autoHueOffset = Mathf.Repeat(
                _autoHueOffset + Time.deltaTime * AutoChangeHueSpeed * 0.75f / 360f,
                1f
            );
            ApplyVisualColors();
            _autoChangeLoopScheduled = true;
            SendCustomEventDelayedFrames(nameof(UpdateAutoChangeVisual), 1);
        }
        
        public void FlashButton()
        {
            if (buttonRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            CaptureOriginal();
            
            // Get the active color from the launchpad for unified coloring
            Color activeFlashColor = (enigmaLaunchpad != null)
            ? enigmaLaunchpad.GetActiveColor()
            : Color.HSVToRGB(242f / 360f, 1f, 1f);
            
            buttonRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", activeFlashColor);
            _mpb.SetColor("_EmissionColor", activeFlashColor * flashEmissionIntensity);
            _mpb.SetFloat("_EmissionStrength", flashEmissionIntensity);
            buttonRenderer.SetPropertyBlock(_mpb);
            
            SendCustomEventDelayedSeconds(nameof(ResetButton), flashDuration);
        }
        
        public void ResetButton()
        {
            if (buttonRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(_mpb);
            // Restore captured color/emission (these will be overridden later by central UpdateButtonColors if this renderer is in the main array)
            _mpb.SetColor("_Color", _origColor);
            _mpb.SetColor("_EmissionColor", _origEmission);
            _mpb.SetFloat("_EmissionStrength", UnifiedEmission);
            buttonRenderer.SetPropertyBlock(_mpb);
        }

        private static Color CosinePalette(float t)
        {
            const float a = 0.5f;
            const float b = 0.5f;

            float r = a + b * Mathf.Cos(2f * Mathf.PI * (t + 0f));
            float g = a + b * Mathf.Cos(2f * Mathf.PI * (t + 0.33f));
            float bC = a + b * Mathf.Cos(2f * Mathf.PI * (t + 0.67f));

            return new Color(r, g, bC, 1f);
        }
        
        private void CaptureOriginal()
        {
            if (_capturedOriginal || buttonRenderer == null) return;
            var mat = buttonRenderer.sharedMaterial;
            if (mat != null)
            {
                if (mat.HasProperty("_Color"))
                _origColor = mat.GetColor("_Color");
                if (mat.HasProperty("_EmissionColor"))
                _origEmission = mat.GetColor("_EmissionColor");
            }
            _capturedOriginal = true;
        }
        
        private string ApplyLabelFormatting(string label, bool formatLabel, bool useStatsFormatting, bool formatFirstLineOnly)
        {
            if (!formatLabel)
            {
                return label;
            }
            
            string[] lines = label.Split('\n');
            int lineCount = lines.Length;
            for (int i = 0; i < lineCount; i++)
            {
                if (formatFirstLineOnly && i > 0)
                {
                    break;
                }
                
                string line = lines[i];
                lines[i] = useStatsFormatting ? FormatNameForStats(line) : FormatName(line);
            }
            
            return string.Join("\n", lines);
        }
        
        public static string FormatName(string rawName)
        {
            return FormatNameInternal(rawName);
        }
        
        public static string FormatNameForStats(string rawName)
        {
            return FormatNameInternal(rawName);
        }
        
        private static string FormatNameInternal(string rawName)
        {
            // No longer formats text with newlines - returns text as-is
            if (string.IsNullOrEmpty(rawName)) return string.Empty;
            return rawName;
        }
        
        // Static helper methods to access fields for UdonSharp compatibility
        public static int GetButtonIndex(ButtonHandler handler)
        {
            return handler != null ? handler.buttonIndex : -1;
        }
        
        public static EnigmaLaunchpad GetEnigmaLaunchpad(ButtonHandler handler)
        {
            return handler != null ? handler.enigmaLaunchpad : null;
        }
        
        public static void SetEnigmaLaunchpad(ButtonHandler handler, EnigmaLaunchpad launchpad)
        {
            if (handler != null) handler.enigmaLaunchpad = launchpad;
        }
        
        public static bool GetIsFolderLeftButton(ButtonHandler handler)
        {
            return handler != null && handler.IsFolderLeftButton;
        }
        
        public static bool GetIsFolderRightButton(ButtonHandler handler)
        {
            return handler != null && handler.IsFolderRightButton;
        }
        
        public static bool GetIsDownButton(ButtonHandler handler)
        {
            return handler != null && handler.IsDownButton;
        }
        
        public static bool GetIsUpButton(ButtonHandler handler)
        {
            return handler != null && handler.IsUpButton;
        }
        
        public static bool GetIsAutoChangeButton(ButtonHandler handler)
        {
            return handler != null && handler.IsAutoChangeButton;
        }
        
        public static bool GetIsResetButton(ButtonHandler handler)
        {
            return handler != null && handler.IsResetButton;
        }
    }
}

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Attach to any physical button object in the scene.
    /// The EnigmaController discovers all EnigmaManagedButton components via its
    /// reorderable buttonSlots list and assigns slot indices.
    /// At runtime, the controller tells each button what to display based on
    /// the active folder and page.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EnigmaManagedButton : UdonSharpBehaviour
    {
        [Tooltip("Reference to the controller that owns this button.")]
        public EnigmaController controller;

        [Tooltip("Slot index assigned by the controller via the reorderable list.")]
        [HideInInspector] public int slotIndex = -1;

        [Header("Visuals")]
        [Tooltip("Renderer to flash and tint on interaction.")]
        public Renderer buttonRenderer;
        [Tooltip("Optional label text component.")]
        public TMP_Text buttonText;
        [Tooltip("Duration of the flash effect in seconds.")]
        public float flashDuration = 0.5f;
        [Tooltip("Emission intensity multiplier used during the flash.")]
        public float flashEmissionIntensity = 2f;

        private const float EmissionStrength = 1f;

        // ── Internal visual state ──
        private MaterialPropertyBlock _mpb;
        private Color _targetColor;
        private bool _targetInteractable;
        private float _flashTimer;
        private bool _isFlashing;

        public override void Interact()
        {
            if (controller == null) return;
            controller.OnButtonPressed(slotIndex);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            // Controller: two levels up (EnigmaController → Buttons → this button)
            if (controller == null
                && transform.parent != null
                && transform.parent.parent != null)
            {
                controller = transform.parent.parent.GetComponent<EnigmaController>();
            }

            // Children: one has a Renderer (the button mesh), the other has TMP_Text (the label).
            foreach (Transform child in transform)
            {
                Renderer r = child.GetComponent<Renderer>();
                TMPro.TMP_Text tmp = child.GetComponent<TMPro.TMP_Text>();

                if (buttonRenderer == null && r != null && tmp == null)
                    buttonRenderer = r;

                if (buttonText == null && tmp != null)
                    buttonText = tmp;
            }
        }
#endif

        /// <summary>
        /// Called by the controller every time the display updates
        /// (folder change, page change, toggle state change).
        /// Sets color and emission so the button visually reflects its active state.
        /// </summary>
        public void UpdateVisual(string label, Color color, bool interactable)
        {
            _targetColor = color;
            _targetInteractable = interactable;

            if (buttonText != null)
                buttonText.text = label;

            ApplyVisualColors();
        }

        private void ApplyVisualColors()
        {
            if (buttonRenderer == null) return;
            // Don't overwrite flash emission while a flash is playing.
            if (_isFlashing) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", _targetColor);
            _mpb.SetColor("_EmissionColor", _targetColor);
            _mpb.SetFloat("_EmissionStrength", _targetInteractable ? EmissionStrength : 0f);
            buttonRenderer.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// Triggers a brief color flash on this button's renderer.
        /// </summary>
        public void Flash(Color color)
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

        private void Update()
        {
            if (!_isFlashing) return;
            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f)
            {
                _isFlashing = false;
                ApplyVisualColors(); // Restore to current target state after flash.
            }
        }
    }
}

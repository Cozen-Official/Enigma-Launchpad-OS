using UdonSharp;
using VRC.SDKBase;
using UnityEngine;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Place this component on a GameObject with a trigger collider that defines
    /// the boundary of a controller's room. When the local player enters the
    /// trigger:
    ///   1. Every other EnigmaController in the scene is locally disabled
    ///      (when <see cref="suppressOtherControllersOnEntry"/> is true) so
    ///      their actions can't overwrite this room's visuals — e.g., another
    ///      room's autochange skybox swapping the global skybox out from
    ///      under you.
    ///   2. The assigned controller is re-enabled (if it was disabled by a
    ///      different boundary earlier).
    ///   3. The assigned controller re-applies all active entry states so
    ///      materials, skybox, shaders, step values, and color cycles match
    ///      the current networked state.
    ///
    /// IMPORTANT — boundary parenting:
    /// This GameObject must NOT be a child of any controller's hierarchy. If
    /// the boundary lives under a controller, disabling that controller from
    /// another room's boundary disables this one too — and walking back into
    /// the room will silently fail to re-trigger. Place boundaries under a
    /// dedicated parent (e.g., "EnigmaBoundaries") that's never disabled.
    ///
    /// Usage:
    ///   1. Create a GameObject (NOT under a controller) with a Box / Sphere /
    ///      Mesh Collider set to Is Trigger.
    ///   2. Add this component and assign the controller reference.
    ///   3. Position the collider to cover the room where the controller is
    ///      used.
    ///
    /// The build pipeline auto-populates <see cref="otherControllers"/> with
    /// every other EnigmaController in the scene; users don't manage it.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EnigmaControllerBoundary : UdonSharpBehaviour
    {
        [Tooltip("The Enigma OS controller that becomes the local active " +
                 "controller when the local player enters this boundary.")]
        public EnigmaController controller;

        [Tooltip("When ON, every other EnigmaController in the scene is " +
                 "locally disabled (gameObject.SetActive(false)) on entry " +
                 "so their actions can't overwrite this room's visuals — " +
                 "for example, another room's autochange skybox swapping " +
                 "the global skybox while you're in this room. Defaults ON. " +
                 "The list of controllers to disable is auto-populated by " +
                 "the build pipeline.")]
        public bool suppressOtherControllersOnEntry = true;

        // Auto-populated at build time by EnigmaPlayModeHook.RebuildAllControllers.
        // Hidden from the inspector because the build always overwrites it —
        // any manual edits get clobbered on the next build / play-mode entry.
        // Use Unity's inspector Debug Mode to view contents if needed.
        [HideInInspector]
        public EnigmaController[] otherControllers = new EnigmaController[0];

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (controller == null || !player.isLocal) return;

            // Step 1: locally suppress other rooms' controllers. This stops
            // their Update() ticks (autochange timer halts) and their UdonSync
            // callbacks (remote-driven action execution doesn't land), so
            // they can't overwrite the global skybox / materials while you're
            // in this room. Network ownership and synced state are unaffected
            // — only this client's visibility / processing changes.
            if (suppressOtherControllersOnEntry && otherControllers != null)
            {
                for (int i = 0; i < otherControllers.Length; i++)
                {
                    var other = otherControllers[i];
                    if (other != null && other.gameObject != null
                        && other.gameObject.activeSelf)
                        other.gameObject.SetActive(false);
                }
            }

            // Step 2: make sure the room we just entered is enabled. If
            // another boundary disabled it earlier, re-enabling resumes its
            // Update / sync callback processing.
            if (!controller.gameObject.activeSelf)
                controller.gameObject.SetActive(true);

            // Step 3: catch up the visual state. Defer by one frame so any
            // pending UdonSync deserialization that arrives during/after
            // re-enable (entryStates, stepCurrentValues, colorPaletteCurrentIndices)
            // lands first. Without the delay the catch-up could read stale
            // values left over from before the controller was suppressed.
            controller.SendCustomEventDelayedFrames(
                nameof(EnigmaController.ReapplyActiveStates), 1);
        }
    }
}

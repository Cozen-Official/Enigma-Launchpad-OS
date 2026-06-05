
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Dedicated UdonSharpBehaviour for preset slot storage. Lives on the
    /// same GameObject as <see cref="EnigmaController"/> and is auto-created
    /// by the editor build pipeline when any folder in the controller
    /// contains a preset action.
    ///
    /// <para>
    /// <b>Why a separate behaviour?</b>
    /// UdonSharp's <c>[UdonSynced]</c> is atomic across a single behaviour —
    /// every <c>RequestSerialization()</c> call ships ALL of a behaviour's
    /// synced fields in one packet. When these preset arrays lived on
    /// <see cref="EnigmaController"/>, every effect-toggle button press
    /// (which calls the controller's <c>RequestSerialization</c>) was dragging
    /// ~30 KB of preset data across the wire even though it had not changed,
    /// saturating VRChat's per-behaviour bandwidth budget and producing
    /// multi-second delivery lag.
    /// </para>
    ///
    /// <para>
    /// Splitting storage into its own behaviour means the controller's sync
    /// ships only the small effect-state payload (~2 KB) on every toggle, and
    /// <c>RequestSerialization()</c> on THIS behaviour only fires when a
    /// preset is actually saved, cleared, or restored from PlayerData —
    /// rare events compared to effect toggles. Late joiners still receive
    /// the full preset library automatically via Udon's sync-on-join
    /// handshake, so shared presets remain shared without the per-toggle
    /// bandwidth cost.
    /// </para>
    ///
    /// <para>
    /// <b>Array layout.</b> All arrays are flat, sized
    /// <c>numPresets × numEntries</c> (entry-scoped) or
    /// <c>numPresets × numFaders</c> (fader-scoped). The index for slot
    /// <c>s</c>, entry <c>e</c> is <c>s * numEntries + e</c>; fader index
    /// <c>f</c> is <c>s * numFaders + f</c>. This matches the packing used
    /// by <see cref="EnigmaController.Presets"/> SavePreset / LoadPreset.
    /// </para>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class EnigmaPresetStorage : UdonSharpBehaviour
    {
        /// <summary>
        /// Parent controller reference. Set by the editor build pipeline when
        /// the storage component is auto-created. The controller reads/writes
        /// this storage's arrays via the <c>presetStorage</c> field it holds
        /// on itself; the back-reference here is kept so tooling and
        /// diagnostics can walk in either direction.
        /// </summary>
        public EnigmaController controller;

        // ── Synced storage ─────────────────────────────────────────────────
        // Flat arrays. Sizes are (numPresets * numEntries) or
        // (numPresets * numFaders). AllocateStorage() sizes them correctly
        // when the controller's Start() runs on every client.
        //
        // These are [UdonSynced] but only this behaviour's own
        // RequestSerialization() ships them — the controller's sync path is
        // completely independent.

        /// <summary>Per-slot flag: true if the slot contains a saved preset.</summary>
        [UdonSynced] public bool[]  presetIsSaved           = new bool[0];

        /// <summary>Flat (slot × entry) bool array of saved entry on/off state.</summary>
        [UdonSynced] public bool[]  presetSavedEntryStates  = new bool[0];

        /// <summary>Flat (slot × entry) float array of saved step values.</summary>
        [UdonSynced] public float[] presetSavedStepValues   = new float[0];

        /// <summary>Flat (slot × entry) int array of saved color palette indices.</summary>
        [UdonSynced] public int[]   presetSavedColorIndices = new int[0];

        /// <summary>Flat (slot × fader) float array of saved fader step positions.</summary>
        [UdonSynced] public float[] presetSavedFaderValues  = new float[0];

        /// <summary>Flat (slot × entry) int array of saved variant group indices.</summary>
        [UdonSynced] public int[]   presetSavedVariantIndices = new int[0];

        /// <summary>
        /// Size (or resize) the storage arrays to match the current
        /// controller layout. Idempotent when sizes already match — existing
        /// data is preserved, so calling this after sync has arrived does
        /// NOT wipe the master's synced state.
        /// </summary>
        /// <param name="numPresets">Number of preset slots (presetRole == 0 entries).</param>
        /// <param name="numEntries">Total runtime entry count (rtEntryLabels.Length).</param>
        /// <param name="numFaders">Total fader count (controller.faderSlots.Length).</param>
        public void AllocateStorage(int numPresets, int numEntries, int numFaders)
        {
            int entryStride = numPresets * numEntries;
            int faderStride = numPresets * numFaders;

            if (presetIsSaved == null || presetIsSaved.Length != numPresets)
                presetIsSaved = new bool[numPresets];
            if (presetSavedEntryStates == null || presetSavedEntryStates.Length != entryStride)
                presetSavedEntryStates = new bool[entryStride];
            if (presetSavedStepValues == null || presetSavedStepValues.Length != entryStride)
                presetSavedStepValues = new float[entryStride];
            if (presetSavedColorIndices == null || presetSavedColorIndices.Length != entryStride)
                presetSavedColorIndices = new int[entryStride];
            if (presetSavedFaderValues == null || presetSavedFaderValues.Length != faderStride)
                presetSavedFaderValues = new float[faderStride];
            if (presetSavedVariantIndices == null || presetSavedVariantIndices.Length != entryStride)
                presetSavedVariantIndices = new int[entryStride];
        }

        /// <summary>
        /// Take local ownership of THIS behaviour's GameObject (which is a child
        /// of the controller's GameObject, not the controller itself) and call
        /// RequestSerialization. The controller's save/clear/load-from-PlayerData
        /// paths all go through this helper so ownership transfer is handled at
        /// the right level — VRChat ownership is per-GameObject, and our storage
        /// lives on a dedicated child to keep its sync frame independent from
        /// the controller's. Calling Networking.SetOwner on the controller's
        /// GameObject does NOT transfer the child's ownership, so direct
        /// RequestSerialization calls would silently fail on non-owner clients.
        /// </summary>
        public void SyncNow()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            RequestSerialization();
        }

        // Log the actual wire size of each Manual-sync serialization on this
        // behaviour. Used for diagnosing whether the preset storage's sync is
        // firing when it shouldn't be, and to compare its cooldown contribution
        // against VRChat's 11 KB/s per-script bandwidth budget.
        public override void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
        {
            if (controller != null && controller.debugLogging)
            {
                if (result.success)
                    Debug.Log($"[Enigma] PresetStorage.OnPostSerialization() SUCCESS byteCount={result.byteCount} (~{(result.byteCount / 11000.0):F2}s cooldown at 11KB/s)");
                else
                    Debug.Log($"[Enigma] PresetStorage.OnPostSerialization() FAILED byteCount={result.byteCount}");
            }
        }

        // Log OnDeserialization so we can see how often it fires on the receiver
        // and cross-reference with the controller's deserializations.
        public override void OnDeserialization()
        {
            if (controller != null && controller.debugLogging)
                Debug.Log("[Enigma] PresetStorage.OnDeserialization() fired");
        }
    }
}

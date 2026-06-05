
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  RESET
        // ------------------------------------------------------------------------

        /// <summary>
        /// Re-applies every entry's current active/inactive state to the world (objects,
        /// materials, shader properties, renderers).  Called during OnDeserialization so
        /// late-joining players see the same in-world state as existing players.
        /// Momentary Udon trigger events (action type 5) are NOT re-fired because momentary
        /// buttons never write a persisted active state.
        ///
        /// (E) Ordering note: <c>entryStates</c> is fully settled (filled with the synced values)
        /// before this method is called.  Per-action conditions read only <c>entryStates[]</c>,
        /// never physical world-object state, so processing entries 0..n linearly is safe —
        /// conditions for any entry correctly see the synced state of every other entry regardless
        /// of the order in which world effects are being re-applied.
        /// ExecuteEntryActions does not mutate <c>entryStates</c>, so there is no mid-loop
        /// state drift.  If conditions ever read physical world state this ordering must be
        /// revisited (consider a two-pass approach: apply states first, then conditional effects).
        /// </summary>
        private void RestoreWorldState()
        {
            Log("RestoreWorldState() re-applying all entry actions");
            if (entryStates == null) return;
            int total = entryStates.Length;

            // Pass 1: Apply inactive entries first (set properties to defaults).
            // Skip inactive entries that share an exclusive group with an active entry —
            // their deactivation is implied by the active entry's activation in Pass 2,
            // and processing them here would briefly reset shared properties (e.g.,
            // _OutlineType=0) before Pass 2 can set them, causing flickering.
            for (int i = 0; i < total; i++)
            {
                if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;
                if (entryStates[i]) continue; // skip active entries — applied in pass 2
                if (SharesExclusiveGroupWithActiveEntry(i)) continue; // skip peers of active entries
                ExecuteEntryActions(i, false);

                // Re-apply step values for inactive entries too — toggle entries with
                // embedded step actions (e.g., AL power buttons) need their synced step
                // value applied even when inactive, since non-stateful action skipping
                // prevents ExecuteEntryActions from resetting the property.
                if (stepCurrentValues != null && i < stepCurrentValues.Length)
                {
                    ExecuteStepActions(i, stepCurrentValues[i]);
                }
            }

            // Pass 2: Apply active entries last so their values override shared
            // properties (e.g., _OutlineType set by both Aura and Sobel entries).
            for (int i = 0; i < total; i++)
            {
                if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;
                if (!entryStates[i]) continue;
                ExecuteEntryActions(i, true);

                // Re-apply step value for any entry that has one (toggle entries
                // with embedded step actions like AL power buttons, not just btnType==2).
                if (stepCurrentValues != null && i < stepCurrentValues.Length)
                {
                    ExecuteStepActions(i, stepCurrentValues[i]);
                }
            }

            // Pass 3: Re-apply palette colors for every entry that has a palette.
            // Covers both ColorCycle (btnType==3) and Color Selector "Set Color"
            // (actionType 10 role 1) entries — the latter are typically momentary
            // buttons whose entryStates[i] is false, so gating on state or on
            // btnType would miss them. ApplyColorCycleAtIndex is a no-op when
            // palCount <= 0 so it's safe to call for every entry unconditionally.
            // This runs AFTER Pass 2 so active entries' ExecuteEntryActions (which
            // may write the target color property to a default) can't overwrite
            // the restored palette color.
            if (colorPaletteCurrentIndices != null)
            {
                for (int i = 0; i < total; i++)
                {
                    if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;
                    if (i < colorPaletteCurrentIndices.Length)
                        ApplyColorCycleAtIndex(i, colorPaletteCurrentIndices[i]);
                }
            }
        }

        /// <summary>
        /// Re-applies all active entry states to the local client's world (materials,
        /// skybox, shaders, objects, step values, color cycles). Called when the local
        /// player enters the room collider so that the visual state matches the current
        /// networked state even if actions were executed while this controller was
        /// inactive on this client.
        /// </summary>
        public void ReapplyActiveStates()
        {
            RestoreWorldState();
            UpdateFaderBindings();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void ResetAll()
        {
            Log("ResetAll() resetting all entries to defaults");
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();

            int total = entryStates != null ? entryStates.Length : 0;

            // Reset entry states to defaults.
            for (int i = 0; i < total; i++)
                entryStates[i] = rtEntryDefaultOn != null && i < rtEntryDefaultOn.Length && rtEntryDefaultOn[i];

            // Reset step values to their configured defaults (not min values).
            for (int i = 0; i < total; i++)
            {
                if (rtEntryButtonTypes != null && i < rtEntryButtonTypes.Length
                    && rtEntryButtonTypes[i] == 2)
                {
                    float defVal = ReadStepDefaultValue(i);
                    if (stepCurrentValues != null && i < stepCurrentValues.Length)
                        stepCurrentValues[i] = defVal;
                    ExecuteStepActions(i, defVal);
                }
            }

            // Execute default-on entries.
            for (int i = 0; i < total; i++)
            {
                if (entryStates[i])
                    ExecuteEntryActions(i, true);
            }

            // Force-reset stateful actions for non-default entries.
            ApplyDefaultsOff();

            // Reset color palettes to index 0 and apply.
            if (rtColorPaletteStart != null && rtColorPaletteCount != null)
            {
                for (int i = 0; i < total; i++)
                {
                    if (colorPaletteCurrentIndices != null && i < colorPaletteCurrentIndices.Length)
                        colorPaletteCurrentIndices[i] = 0;
                    if (colorPalettePendingIndices != null && i < colorPalettePendingIndices.Length)
                        colorPalettePendingIndices[i] = 0;
                    int palCount = i < rtColorPaletteCount.Length ? rtColorPaletteCount[i] : 0;
                    if (palCount > 0)
                        ApplyColorCycleAtIndex(i, 0);
                }
            }

            // Reset variant indices.
            if (variantCurrentIndices != null)
                for (int i = 0; i < variantCurrentIndices.Length; i++) variantCurrentIndices[i] = 0;
            if (variantPendingIndices != null)
                for (int i = 0; i < variantPendingIndices.Length; i++) variantPendingIndices[i] = 0;

            // Activate exclusive off buttons for groups with no default-on member.
            ActivateExclusiveOffButtons();

            currentFolderIndex = defaultFolderIndex;
            currentPageIndex   = 0;
            currentFaderPage   = 0;

            // Snapshot the post-reset state so OnDeserialization's diff reads
            // against this client's authoritative view rather than a stale _prev.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }
    }
}

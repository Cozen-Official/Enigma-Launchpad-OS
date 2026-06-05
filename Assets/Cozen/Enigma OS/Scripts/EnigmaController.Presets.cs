using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  PRESET BUTTONS
        //
        //  All preset slot storage lives on a dedicated EnigmaPresetStorage
        //  UdonSharpBehaviour (see presetStorage field on the controller). That
        //  behaviour has its own sync cycle — so an effect-toggle RequestSerialization
        //  on the controller does NOT drag the ~30 KB preset arrays along. Only
        //  explicit save / clear / PlayerData-load calls fire the storage's
        //  own RequestSerialization.
        //
        //  Every read/write here goes through presetStorage. If presetStorage is
        //  null (no preset actions in any folder) the handlers early-out and
        //  nothing happens — the auto-creation in the build pipeline ensures
        //  the component exists whenever it's needed.
        // ------------------------------------------------------------------------

        public void HandlePresetPress(int entryIdx)
        {
            int role = rtPresetRoles != null && entryIdx < rtPresetRoles.Length
                       ? rtPresetRoles[entryIdx] : 0;
            string[] roleNames = { "Slot", "Save", "Load", "Clear" };
            Log($"HandlePresetPress() entry={entryIdx} role={role} ({(role < roleNames.Length ? roleNames[role] : "?")})");

            switch (role)
            {
                case 0: HandlePresetSlotPress(entryIdx);   break;  // Slot
                case 1: HandlePresetSavePress();            break;  // Save Button
                case 2: HandlePresetLoadPress(entryIdx);    break;  // Load Button
                case 3: HandlePresetClearPress(entryIdx);  break;  // Clear Button
            }
        }

        // -- Preset Slot: first press saves state, subsequent presses recall it.
        //    When clear mode is active, pressing a slot clears it instead.
        private void HandlePresetSlotPress(int entryIdx)
        {
            if (presetStorage == null)
            {
                Log("HandlePresetSlotPress() presetStorage is null — preset actions exist but storage component was not created. Rebuild the controller to auto-create EnigmaPresetStorage.");
                return;
            }
            if (rtPresetSlotIndex == null || entryIdx >= rtPresetSlotIndex.Length) return;
            int slotIdx = rtPresetSlotIndex[entryIdx];
            if (slotIdx < 0) return;

            EnsureLocalOwnership();

            if (_presetClearModeActive)
            {
                Log($"PresetSlot() slot={slotIdx} -> CLEAR");
                ClearPresetSlot(slotIdx, entryIdx);
                _presetClearModeActive = false;
            }
            else if (presetStorage.presetIsSaved == null
                     || slotIdx >= presetStorage.presetIsSaved.Length
                     || !presetStorage.presetIsSaved[slotIdx])
            {
                Log($"PresetSlot() slot={slotIdx} -> SAVE (first press, slot empty)");
                SavePreset(entryIdx, slotIdx);
            }
            else
            {
                Log($"PresetSlot() slot={slotIdx} -> RECALL (slot has data)");
                LoadPreset(entryIdx, slotIdx);
            }

            // Snapshot the post-mutation state so OnDeserialization's diff reads
            // against this client's authoritative view rather than a stale _prev.
            // Save/Load/Clear all mutate entryStates and/or stepCurrentValues.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // -- Save Button: persist all saved preset slots to VRC PlayerData.
        private void HandlePresetSavePress()
        {
            Log("HandlePresetSavePress() persisting to PlayerData");
            SavePresetsToPlayerData();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // -- Load Button: restore saved preset slots from VRC PlayerData.
        private void HandlePresetLoadPress(int entryIdx)
        {
            Log("HandlePresetLoadPress() restoring from PlayerData");
            EnsureLocalOwnership();
            bool success = LoadPresetsFromPlayerData();
            Log($"HandlePresetLoadPress() result={success}");
            if (!success)
            {
                // Temporarily show "Preset Incompatible" on the button
                if (buttonSlots != null)
                {
                    int itemsPerPage = GetItemsPerPage();
                    int startIdx = rtFolderEntryStart[currentFolderIndex];
                    int pageOffset = currentPageIndex * itemsPerPage;
                    int slotIndex = entryIdx - startIdx - pageOffset;

                    if (slotIndex >= 0 && slotIndex < buttonSlots.Length && buttonSlots[slotIndex] != null)
                    {
                        buttonSlots[slotIndex].UpdateVisual("Incompatible\nLayout", inactiveColor, false);
                        _presetIncompatibleTimer = 3f;
                    }
                }
            }
            // LoadPresetsFromPlayerData writes to presetStorage and fires its own
            // storage-level RequestSerialization. The controller's own sync only
            // needs to fire for the display-state refresh, via the snapshot +
            // DeferredRequestSerialization path below.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // -- Clear Button: toggle clear mode.  Next Slot press will clear that slot.
        private void HandlePresetClearPress(int entryIdx)
        {
            _presetClearModeActive = !_presetClearModeActive;
            Log($"HandlePresetClearPress() clearMode={_presetClearModeActive}");
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // -- Erase a single preset slot and turn its button off.
        private void ClearPresetSlot(int slotIdx, int entryIdx)
        {
            if (presetStorage == null) return;

            if (slotIdx >= 0 && presetStorage.presetIsSaved != null
                && slotIdx < presetStorage.presetIsSaved.Length)
                presetStorage.presetIsSaved[slotIdx] = false;

            if (entryStates != null && entryIdx < entryStates.Length)
                entryStates[entryIdx] = false;

            // Ship the cleared slot to every client.
            presetStorage.SyncNow();
        }

        private string ComputeLayoutHash()
        {
            if (rtEntryLabels == null) return "0";
            int hash = 17;
            hash = hash * 31 + rtEntryLabels.Length;
            for (int i = 0; i < rtEntryLabels.Length; i++)
            {
                string label = rtEntryLabels[i];
                if (string.IsNullOrEmpty(label)) continue;
                hash = hash * 31 + label.Length;
                hash = hash * 31 + label[0];
                if (label.Length > 1) hash = hash * 31 + label[label.Length - 1];
            }
            return hash.ToString("X");
        }

        // -- Persist all saved preset slots to VRC PlayerData (cross-session storage).
        private void SavePresetsToPlayerData()
        {
            if (presetStorage == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsValid()) return;

            int numSlots = presetStorage.presetIsSaved != null ? presetStorage.presetIsSaved.Length : 0;
            int savedCount = 0;
            for (int i = 0; i < numSlots; i++)
                if (presetStorage.presetIsSaved[i]) savedCount++;
            Log($"SavePresetsToPlayerData() slots={numSlots} saved={savedCount} hash={ComputeLayoutHash()}");
            PlayerData.SetString("EnigmaPreset_SlotCount", numSlots.ToString());
            PlayerData.SetString("EnigmaPreset_LayoutHash", ComputeLayoutHash());

            for (int s = 0; s < numSlots; s++)
            {
                bool has = presetStorage.presetIsSaved[s];
                PlayerData.SetString($"EnigmaPreset_Has_{s}", has ? "1" : "0");

                if (!has) continue;

                int numEntries = rtEntryLabels != null ? rtEntryLabels.Length : 0;
                int numFaders  = faderSlots   != null ? faderSlots.Length   : 0;

                // Serialize entry states
                var sb = new System.Text.StringBuilder(numEntries * 2);
                for (int e = 0; e < numEntries; e++)
                {
                    int idx = s * numEntries + e;
                    bool state = idx < presetStorage.presetSavedEntryStates.Length
                                 && presetStorage.presetSavedEntryStates[idx];
                    if (e > 0) sb.Append(',');
                    sb.Append(state ? '1' : '0');
                }
                PlayerData.SetString($"EnigmaPreset_States_{s}", sb.ToString());

                // Serialize step values
                sb.Length = 0;
                for (int e = 0; e < numEntries; e++)
                {
                    int idx = s * numEntries + e;
                    float val = idx < presetStorage.presetSavedStepValues.Length
                                ? presetStorage.presetSavedStepValues[idx] : 0f;
                    if (e > 0) sb.Append(',');
                    sb.Append(val.ToString("G"));
                }
                PlayerData.SetString($"EnigmaPreset_Steps_{s}", sb.ToString());

                // Serialize color palette indices
                sb.Length = 0;
                for (int e = 0; e < numEntries; e++)
                {
                    int idx = s * numEntries + e;
                    int val = idx < presetStorage.presetSavedColorIndices.Length
                              ? presetStorage.presetSavedColorIndices[idx] : 0;
                    if (e > 0) sb.Append(',');
                    sb.Append(val.ToString());
                }
                PlayerData.SetString($"EnigmaPreset_Colors_{s}", sb.ToString());

                // Serialize variant group indices
                sb.Length = 0;
                for (int e = 0; e < numEntries; e++)
                {
                    int idx = s * numEntries + e;
                    int val = idx < presetStorage.presetSavedVariantIndices.Length
                              ? presetStorage.presetSavedVariantIndices[idx] : 0;
                    if (e > 0) sb.Append(',');
                    sb.Append(val.ToString());
                }
                PlayerData.SetString($"EnigmaPreset_Variants_{s}", sb.ToString());

                // Serialize fader positions (step values)
                sb.Length = 0;
                for (int f = 0; f < numFaders; f++)
                {
                    int idx = s * numFaders + f;
                    float val = idx < presetStorage.presetSavedFaderValues.Length
                                ? presetStorage.presetSavedFaderValues[idx] : 0f;
                    if (f > 0) sb.Append(',');
                    sb.Append(((int)val).ToString());
                }
                PlayerData.SetString($"EnigmaPreset_Faders_{s}", sb.ToString());
            }
        }

        // -- Restore preset slots from VRC PlayerData.
        private bool LoadPresetsFromPlayerData()
        {
            if (presetStorage == null) return false;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsValid()) return false;

            string layoutHash = PlayerData.GetString(localPlayer, "EnigmaPreset_LayoutHash");
            string currentHash = ComputeLayoutHash();
            Log($"LoadPresetsFromPlayerData() storedHash={layoutHash} currentHash={currentHash}");
            if (!string.IsNullOrEmpty(layoutHash) && layoutHash != currentHash)
            {
                Log("LoadPresetsFromPlayerData() HASH MISMATCH - discarding old presets");
                Debug.LogWarning("[EnigmaController] Preset layout hash mismatch. Discarding old presets.");

                // Clear the invalid presets from PlayerData so it doesn't keep failing
                PlayerData.SetString("EnigmaPreset_SlotCount", "0");
                PlayerData.SetString("EnigmaPreset_LayoutHash", "");

                // Also clear the local preset slots
                if (presetStorage.presetIsSaved != null)
                {
                    for (int i = 0; i < presetStorage.presetIsSaved.Length; i++)
                        presetStorage.presetIsSaved[i] = false;
                }

                // Ship the cleared storage to every client.
                presetStorage.SyncNow();
                return false;
            }

            string slotCountStr = PlayerData.GetString(localPlayer, "EnigmaPreset_SlotCount");
            if (string.IsNullOrEmpty(slotCountStr)) { Log("LoadPresetsFromPlayerData() no slot count in PlayerData"); return false; }

            int numSlots = 0;
            if (!int.TryParse(slotCountStr, out numSlots) || numSlots <= 0) { Log($"LoadPresetsFromPlayerData() invalid slot count: {slotCountStr}"); return false; }

            int numEntries = rtEntryLabels != null ? rtEntryLabels.Length : 0;

            int numFaders = faderSlots != null ? faderSlots.Length : 0;

            for (int s = 0; s < numSlots && s < (presetStorage.presetIsSaved != null ? presetStorage.presetIsSaved.Length : 0); s++)
            {
                string hasVal = PlayerData.GetString(localPlayer, $"EnigmaPreset_Has_{s}");
                bool has = hasVal == "1";
                presetStorage.presetIsSaved[s] = has;
                if (!has) continue;

                // Restore entry states
                string statesStr = PlayerData.GetString(localPlayer, $"EnigmaPreset_States_{s}");
                if (!string.IsNullOrEmpty(statesStr))
                {
                    string[] parts = statesStr.Split(',');
                    for (int e = 0; e < parts.Length && e < numEntries; e++)
                    {
                        int idx = s * numEntries + e;
                        if (idx < presetStorage.presetSavedEntryStates.Length)
                            presetStorage.presetSavedEntryStates[idx] = parts[e] == "1";
                    }
                }

                // Restore step values
                string stepsStr = PlayerData.GetString(localPlayer, $"EnigmaPreset_Steps_{s}");
                if (!string.IsNullOrEmpty(stepsStr))
                {
                    string[] parts = stepsStr.Split(',');
                    for (int e = 0; e < parts.Length && e < numEntries; e++)
                    {
                        int idx = s * numEntries + e;
                        if (idx < presetStorage.presetSavedStepValues.Length)
                        {
                            float val = 0f;
                            if (float.TryParse(parts[e], out val))
                                presetStorage.presetSavedStepValues[idx] = val;
                        }
                    }
                }

                // Restore color palette indices
                string colorsStr = PlayerData.GetString(localPlayer, $"EnigmaPreset_Colors_{s}");
                if (!string.IsNullOrEmpty(colorsStr))
                {
                    string[] parts = colorsStr.Split(',');
                    for (int e = 0; e < parts.Length && e < numEntries; e++)
                    {
                        int idx = s * numEntries + e;
                        if (idx < presetStorage.presetSavedColorIndices.Length)
                        {
                            int val = 0;
                            if (int.TryParse(parts[e], out val))
                                presetStorage.presetSavedColorIndices[idx] = val;
                        }
                    }
                }

                // Restore variant group indices
                string variantsStr = PlayerData.GetString(localPlayer, $"EnigmaPreset_Variants_{s}");
                if (!string.IsNullOrEmpty(variantsStr))
                {
                    string[] parts = variantsStr.Split(',');
                    for (int e = 0; e < parts.Length && e < numEntries; e++)
                    {
                        int idx = s * numEntries + e;
                        if (idx < presetStorage.presetSavedVariantIndices.Length)
                        {
                            int val = 0;
                            if (int.TryParse(parts[e], out val))
                                presetStorage.presetSavedVariantIndices[idx] = val;
                        }
                    }
                }

                // Restore fader positions
                string fadersStr = PlayerData.GetString(localPlayer, $"EnigmaPreset_Faders_{s}");
                if (!string.IsNullOrEmpty(fadersStr))
                {
                    string[] parts = fadersStr.Split(',');
                    for (int f = 0; f < parts.Length && f < numFaders; f++)
                    {
                        int idx = s * numFaders + f;
                        if (idx < presetStorage.presetSavedFaderValues.Length)
                        {
                            int val = 0;
                            if (int.TryParse(parts[f], out val))
                                presetStorage.presetSavedFaderValues[idx] = (float)val;
                        }
                    }
                }
            }

            int loadedCount = 0;
            for (int i = 0; i < numSlots && i < (presetStorage.presetIsSaved != null ? presetStorage.presetIsSaved.Length : 0); i++)
                if (presetStorage.presetIsSaved[i]) loadedCount++;
            Log($"LoadPresetsFromPlayerData() loaded {loadedCount}/{numSlots} slots from PlayerData");

            // Ship the newly-loaded storage to every client so all operators see
            // the same preset library after a Load-button press.
            presetStorage.SyncNow();
            return true;
        }

        private void SavePreset(int entryIdx, int slotIdx)
        {
            if (presetStorage == null) return;

            int numEntries = rtEntryLabels != null ? rtEntryLabels.Length : 0;
            int numFaders  = faderSlots    != null ? faderSlots.Length    : 0;
            int entryBase  = slotIdx * numEntries;
            int faderBase  = slotIdx * numFaders;

            bool[] folderMask = BuildFolderIncludedMask(entryIdx);

            bool includeStep     = rtPresetIncludeStepValues     != null && entryIdx < rtPresetIncludeStepValues.Length     && rtPresetIncludeStepValues[entryIdx];
            bool includeColor    = rtPresetIncludeColorPalettes  != null && entryIdx < rtPresetIncludeColorPalettes.Length  && rtPresetIncludeColorPalettes[entryIdx];
            bool includeFaders   = rtPresetIncludeFaders         != null && entryIdx < rtPresetIncludeFaders.Length         && rtPresetIncludeFaders[entryIdx];
            bool includeVariants = rtPresetIncludeVariantGroups  != null && entryIdx < rtPresetIncludeVariantGroups.Length  && rtPresetIncludeVariantGroups[entryIdx];
            Log($"SavePreset() slot={slotIdx} entries={numEntries} faders={numFaders} step={includeStep} color={includeColor} faders={includeFaders} variants={includeVariants}");

            int savedActive = 0;
            int savedScoped = 0;
            int skippedOutOfScope = 0;
            for (int i = 0; i < numEntries; i++)
            {
                // Preset buttons manage their own "saved" state — don't overwrite them
                if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;

                int folder = GetFolderForEntry(i);
                if (folder < 0 || folder >= folderMask.Length || !folderMask[folder])
                {
                    skippedOutOfScope++;
                    continue;
                }

                int idx = entryBase + i;
                if (idx < presetStorage.presetSavedEntryStates.Length)
                {
                    bool state = entryStates != null && i < entryStates.Length && entryStates[i];
                    presetStorage.presetSavedEntryStates[idx] = state;
                    savedScoped++;
                    if (state) savedActive++;
                }

                if (includeStep && idx < presetStorage.presetSavedStepValues.Length)
                    presetStorage.presetSavedStepValues[idx] = stepCurrentValues != null && i < stepCurrentValues.Length ? stepCurrentValues[i] : 0f;

                if (includeColor && idx < presetStorage.presetSavedColorIndices.Length)
                    presetStorage.presetSavedColorIndices[idx] = colorPaletteCurrentIndices != null && i < colorPaletteCurrentIndices.Length ? colorPaletteCurrentIndices[i] : 0;

                if (includeVariants && idx < presetStorage.presetSavedVariantIndices.Length)
                    presetStorage.presetSavedVariantIndices[idx] = variantCurrentIndices != null && i < variantCurrentIndices.Length ? variantCurrentIndices[i] : 0;
            }

            if (includeFaders && faderSlots != null)
            {
                for (int f = 0; f < numFaders && f < faderSlots.Length; f++)
                {
                    int idx = faderBase + f;
                    if (idx < presetStorage.presetSavedFaderValues.Length)
                        presetStorage.presetSavedFaderValues[idx] = faderSlots[f] != null ? (float)faderSlots[f].GetPositionStep() : 0f;
                }
            }

            if (slotIdx < presetStorage.presetIsSaved.Length)
                presetStorage.presetIsSaved[slotIdx] = true;

            // Light up the preset button to signal that data has been saved.
            // First clear any other slot buttons so the "currently-active preset"
            // indicator shows only the one we just touched.
            ClearPresetSlotIndicators();
            if (entryStates != null && entryIdx < entryStates.Length)
                entryStates[entryIdx] = true;

            Log($"SavePreset() slot={slotIdx} DONE — scoped={savedScoped} active={savedActive} outOfScope={skippedOutOfScope}");

            // Ship the newly-saved slot to every client.
            presetStorage.SyncNow();
        }

        private void LoadPreset(int entryIdx, int slotIdx)
        {
            if (presetStorage == null) return;

            int numEntries = rtEntryLabels != null ? rtEntryLabels.Length : 0;
            int numFaders  = faderSlots    != null ? faderSlots.Length    : 0;
            int entryBase  = slotIdx * numEntries;
            int faderBase  = slotIdx * numFaders;

            bool[] folderMask = BuildFolderIncludedMask(entryIdx);

            bool includeStep     = rtPresetIncludeStepValues     != null && entryIdx < rtPresetIncludeStepValues.Length     && rtPresetIncludeStepValues[entryIdx];
            bool includeColor    = rtPresetIncludeColorPalettes  != null && entryIdx < rtPresetIncludeColorPalettes.Length  && rtPresetIncludeColorPalettes[entryIdx];
            bool includeFaders   = rtPresetIncludeFaders         != null && entryIdx < rtPresetIncludeFaders.Length         && rtPresetIncludeFaders[entryIdx];
            bool includeVariants = rtPresetIncludeVariantGroups  != null && entryIdx < rtPresetIncludeVariantGroups.Length  && rtPresetIncludeVariantGroups[entryIdx];
            Log($"LoadPreset() slot={slotIdx} entries={numEntries} faders={numFaders} step={includeStep} color={includeColor} faders={includeFaders} variants={includeVariants}");

            // Pass 1: Set states and trigger exclusive group logic
            for (int i = 0; i < numEntries; i++)
            {
                if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;

                int folder = GetFolderForEntry(i);
                if (folder < 0 || folder >= folderMask.Length || !folderMask[folder]) continue;

                int idx = entryBase + i;
                bool newState = idx < presetStorage.presetSavedEntryStates.Length
                                && presetStorage.presetSavedEntryStates[idx];

                if (entryStates != null && i < entryStates.Length)
                {
                    bool wasActive = entryStates[i];
                    entryStates[i] = newState;

                    if (newState && !wasActive)
                    {
                        // Run exclusive group deactivations
                        bool hasMultiGroup = rtEntryExclusiveGroupCount != null
                                             && rtEntryExclusiveGroupStart != null
                                             && rtEntryExclusiveGroupFlat  != null
                                             && i < rtEntryExclusiveGroupCount.Length
                                             && i < rtEntryExclusiveGroupStart.Length
                                             && rtEntryExclusiveGroupCount[i] > 0;

                        if (hasMultiGroup)
                        {
                            int myStart = rtEntryExclusiveGroupStart[i];
                            int myCount = rtEntryExclusiveGroupCount[i];

                            for (int j = 0; j < entryStates.Length; j++)
                            {
                                if (!entryStates[j] || j == i) continue;
                                if (rtEntryExclusiveGroupCount[j] == 0) continue;

                                int jStart = rtEntryExclusiveGroupStart[j];
                                int jCount = rtEntryExclusiveGroupCount[j];
                                bool shared = false;
                                for (int g = myStart; g < myStart + myCount && !shared; g++)
                                    for (int h = jStart; h < jStart + jCount && !shared; h++)
                                        if (rtEntryExclusiveGroupFlat[g] == rtEntryExclusiveGroupFlat[h])
                                            shared = true;

                                if (shared)
                                {
                                    entryStates[j] = false;
                                    ExecuteEntryActions(j, false);
                                }
                            }

                            for (int g = myStart; g < myStart + myCount; g++)
                            {
                                int gid = rtEntryExclusiveGroupFlat[g];
                                if (rtExclusiveButtonPeers == null || rtExclusiveButtonPeerGroupStart == null
                                    || rtExclusiveButtonPeerGroupCount == null) break;
                                if (gid < 0 || gid >= rtExclusiveButtonPeerGroupStart.Length
                                    || gid >= rtExclusiveButtonPeerGroupCount.Length) continue;
                                int bStart = rtExclusiveButtonPeerGroupStart[gid];
                                int bCount = rtExclusiveButtonPeerGroupCount[gid];
                                for (int b = bStart; b < bStart + bCount; b++)
                                    if (b < rtExclusiveButtonPeers.Length && rtExclusiveButtonPeers[b] != null)
                                        rtExclusiveButtonPeers[b].ForceDeactivate();
                            }
                        }
                        else
                        {
                            int group = rtEntryExclusiveGroup != null && i < rtEntryExclusiveGroup.Length
                                        ? rtEntryExclusiveGroup[i] : -1;
                            if (group >= 0)
                            {
                                for (int j = 0; j < entryStates.Length; j++)
                                {
                                    if (j >= rtEntryExclusiveGroup.Length) break;
                                    if (j == i) continue;
                                    if (rtEntryExclusiveGroup[j] == group && entryStates[j])
                                    {
                                        entryStates[j] = false;
                                        ExecuteEntryActions(j, false);
                                    }
                                }

                                if (rtExclusiveButtonPeers != null && rtExclusiveButtonPeerGroupStart != null
                                    && rtExclusiveButtonPeerGroupCount != null
                                    && group >= 0 && group < rtExclusiveButtonPeerGroupStart.Length
                                    && group < rtExclusiveButtonPeerGroupCount.Length)
                                {
                                    int bStart = rtExclusiveButtonPeerGroupStart[group];
                                    int bCount = rtExclusiveButtonPeerGroupCount[group];
                                    for (int b = bStart; b < bStart + bCount; b++)
                                        if (b < rtExclusiveButtonPeers.Length && rtExclusiveButtonPeers[b] != null)
                                            rtExclusiveButtonPeers[b].ForceDeactivate();
                                }
                            }
                        }
                    }
                }
            }

            // Restore variant indices before Pass 2 so Set Variant actions read the correct pending value
            if (includeVariants)
            {
                for (int i = 0; i < numEntries; i++)
                {
                    if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;
                    int folder = GetFolderForEntry(i);
                    if (folder < 0 || folder >= folderMask.Length || !folderMask[folder]) continue;

                    int idx = entryBase + i;
                    int savedVariant = idx < presetStorage.presetSavedVariantIndices.Length
                                       ? presetStorage.presetSavedVariantIndices[idx] : 0;
                    if (variantCurrentIndices != null && i < variantCurrentIndices.Length)
                        variantCurrentIndices[i] = savedVariant;
                    if (variantPendingIndices != null && i < variantPendingIndices.Length)
                        variantPendingIndices[i] = savedVariant;
                }
            }

            // Pass 2: Stage state only (step values, color palette indices) WITHOUT
            // executing world effects. We deliberately do NOT call ExecuteEntryActions,
            // ExecuteStepActions, or ApplyColorCycleAtIndex in this loop — a linear
            // 0..N iteration that mixed active and inactive entries would let later
            // inactive entries clobber shared shader properties set by earlier active
            // entries (e.g., entry N+1 "Sobel Outline" inactive writes _OutlineType=0
            // over entry N "Aura Outline" active which just wrote _OutlineType=2).
            //
            // Instead we stage entryStates (already done in Pass 1), stepCurrentValues,
            // colorPaletteCurrentIndices, variantCurrentIndices (Pass 1.5 above), then
            // delegate the actual world application to RestoreWorldState() below.
            // RestoreWorldState applies inactive entries first, then active entries
            // last (skipping inactive peers of active entries so they never transiently
            // zero out shared properties), then Pass 3 re-applies palette colors for
            // every entry regardless of state. This is the exact same code path that
            // receiver clients use via OnDeserialization, so local and remote behavior
            // stay byte-identical after a preset recall.
            int loadedScoped = 0;
            int loadedActive = 0;
            int loadedStepsApplied = 0;
            int loadedColorsApplied = 0;
            for (int i = 0; i < numEntries; i++)
            {
                if (rtEntryIsPreset != null && i < rtEntryIsPreset.Length && rtEntryIsPreset[i]) continue;

                int folder = GetFolderForEntry(i);
                if (folder < 0 || folder >= folderMask.Length || !folderMask[folder]) continue;

                int idx = entryBase + i;
                bool newState = entryStates != null && i < entryStates.Length && entryStates[i];
                loadedScoped++;
                if (newState)
                {
                    loadedActive++;
                    // Preserve the auto-expire behavior from the old single-pass
                    // implementation — newly-active entries with expire timers should
                    // begin counting down immediately after a preset recall.
                    ScheduleEntryExpire(i);
                }

                // Stage step value (state only — RestoreWorldState below applies it).
                if (includeStep && stepCurrentValues != null && i < stepCurrentValues.Length)
                {
                    float savedStep = idx < presetStorage.presetSavedStepValues.Length
                                      ? presetStorage.presetSavedStepValues[idx] : 0f;
                    stepCurrentValues[i] = savedStep;
                    loadedStepsApplied++;
                }

                // Stage color palette index (state only — RestoreWorldState's Pass 3
                // re-applies the color to the material). Set BOTH current and pending
                // so Color Selector buttons' preview tint stays consistent.
                if (includeColor && colorPaletteCurrentIndices != null && i < colorPaletteCurrentIndices.Length)
                {
                    int savedColorIdx = idx < presetStorage.presetSavedColorIndices.Length
                                        ? presetStorage.presetSavedColorIndices[idx] : 0;
                    colorPaletteCurrentIndices[i] = savedColorIdx;
                    if (colorPalettePendingIndices != null && i < colorPalettePendingIndices.Length)
                        colorPalettePendingIndices[i] = savedColorIdx;
                    loadedColorsApplied++;
                }
            }

            // Restore fader values (stored as discrete steps). SetPositionFromStep
            // writes the fader's bound property, so do this BEFORE RestoreWorldState
            // so any entry-action property writes from RestoreWorldState take
            // precedence over fader-driven values on shared shader properties.
            int loadedFadersApplied = 0;
            if (includeFaders && faderSlots != null)
            {
                for (int f = 0; f < numFaders && f < faderSlots.Length; f++)
                {
                    int idx = faderBase + f;
                    int savedStep = idx < presetStorage.presetSavedFaderValues.Length
                                    ? (int)presetStorage.presetSavedFaderValues[idx] : 0;
                    if (faderSlots[f] != null)
                    {
                        faderSlots[f].SetPositionFromStep(savedStep);
                        loadedFadersApplied++;
                    }
                }
            }

            // Apply all staged state to the world via the shared 2-pass ordering.
            // This guarantees inactive entries run their "off" actions first, then
            // active entries run their "on" actions last, so exclusive-group shared
            // properties settle on the active entry's value. Pass 3 inside
            // RestoreWorldState re-applies palette colors for Color Selector entries
            // that would otherwise never have their color written on the sender.
            RestoreWorldState();

            Log($"LoadPreset() slot={slotIdx} DONE — scoped={loadedScoped} active={loadedActive} steps={loadedStepsApplied} colors={loadedColorsApplied} faders={loadedFadersApplied}");

            // Visually mark this slot as the "currently-active preset" by
            // clearing all other preset slot indicators and lighting up just
            // this one. Any subsequent non-preset press will clear this flag
            // in OnButtonPressed, so the indicator faithfully reflects
            // "live state matches this preset" until the user touches
            // anything else. Done AFTER RestoreWorldState because
            // RestoreWorldState skips preset entries — so it can't stomp
            // on the lit slot, and the slot entry's active=true here makes
            // it visible after the post-load SyncUpdateDisplay.
            ClearPresetSlotIndicators();
            if (entryStates != null && entryIdx < entryStates.Length)
                entryStates[entryIdx] = true;

            // LoadPreset only READS from storage, so no storage-level sync needed.
            // The caller (HandlePresetSlotPress) handles the controller-level sync
            // for the mutated entryStates / stepCurrentValues.
        }

        // -- Preset helpers --------------------------------------------------------

        /// <summary>Returns the folder index that owns <paramref name="entryIdx"/>, or -1.</summary>
        private int GetFolderForEntry(int entryIdx)
        {
            if (rtEntryFolderIndex != null && entryIdx < rtEntryFolderIndex.Length)
                return rtEntryFolderIndex[entryIdx];
            return -1;
        }

        /// <summary>
        /// Returns a per-folder boolean mask indicating which folders are included
        /// in the preset scope of <paramref name="entryIdx"/>.
        /// scope 0 = all folders; scope 1 = selected folders only.
        /// </summary>
        private bool[] BuildFolderIncludedMask(int entryIdx)
        {
            int numFolders = rtFolderNames != null ? rtFolderNames.Length : 0;
            bool[] mask = new bool[numFolders];

            int scope = rtPresetScopes != null && entryIdx < rtPresetScopes.Length
                        ? rtPresetScopes[entryIdx] : 0;

            if (scope == 0) // All folders
            {
                for (int f = 0; f < numFolders; f++) mask[f] = true;
            }
            else // Selected folders
            {
                int start = rtPresetIncludedFolderStart != null && entryIdx < rtPresetIncludedFolderStart.Length
                            ? rtPresetIncludedFolderStart[entryIdx] : 0;
                int count = rtPresetIncludedFolderCount != null && entryIdx < rtPresetIncludedFolderCount.Length
                            ? rtPresetIncludedFolderCount[entryIdx] : 0;
                for (int pf = start; pf < start + count; pf++)
                {
                    if (rtPresetIncludedFolders != null && pf < rtPresetIncludedFolders.Length)
                    {
                        int fi = rtPresetIncludedFolders[pf];
                        if (fi >= 0 && fi < numFolders) mask[fi] = true;
                    }
                }
            }
            return mask;
        }

    }
}

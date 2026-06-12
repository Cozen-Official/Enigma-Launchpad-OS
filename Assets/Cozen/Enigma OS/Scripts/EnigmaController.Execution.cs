
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  BUTTON PRESS DISPATCH
        // ------------------------------------------------------------------------

        // A 50 ms same-slot debounce used to live here. An earlier comment claimed
        // "VRChat can fire Interact twice per click due to overlapping colliders",
        // but walking the shipped Mixer and Launchpad prefab hierarchies confirmed
        // every EnigmaManagedButton has exactly one trigger MeshCollider with no
        // overlapping ancestor or descendant colliders — so a single laser/trigger
        // click can only hit one collider. Two live VRChat runs captured 66
        // rapid-fire presses across multiple slots (April 2026) and the debounce
        // log fired zero times, confirming duplicate Interact events do not occur
        // in the current build. The guard was removed as dead code; if duplicate
        // fires ever surface from a new code path (notably EnigmaButton proximity
        // triggers when both triggerOnEnter and triggerOnExit are enabled), add a
        // targeted guard at that call site rather than a blanket debounce here.

        public void OnButtonPressed(int slotIndex)
        {
            Log($"OnButtonPressed() slot={slotIndex} folder={currentFolderIndex} page={currentPageIndex}");
            if (!CanLocalUserInteract()) return;

            int itemsPerPage = GetItemsPerPage();
            if (itemsPerPage <= 0) return;
            if (rtFolderNames == null || rtFolderNames.Length == 0) return;

            int startIdx  = rtFolderEntryStart[currentFolderIndex];
            int count     = rtFolderEntryCount[currentFolderIndex];
            int pageOffset = currentPageIndex * itemsPerPage;
            int localIdx  = pageOffset + slotIndex;

            if (localIdx >= count) return;

            int entryIdx = startIdx + localIdx;

            // Ignore empty placeholder entries.
            if (rtEntryLabels != null && entryIdx < rtEntryLabels.Length
                && string.IsNullOrEmpty(rtEntryLabels[entryIdx]))
                return;

            int btnType  = rtEntryButtonTypes[entryIdx];

            EnsureLocalOwnership();

            // Preset button takes priority
            if (rtEntryIsPreset != null && entryIdx < rtEntryIsPreset.Length && rtEntryIsPreset[entryIdx])
            {
                HandlePresetPress(entryIdx);
            }
            else if (btnType == 0) // Toggle
            {
                HandleToggle(entryIdx);
            }
            else if (btnType == 1) // Momentary
            {
                // Momentary: fire-once, flash. Deactivate exclusive peers but don't become active.
                if (HasExclusiveGroup(entryIdx))
                    DeactivateExclusiveGroupPeers(entryIdx);
                ExecuteEntryActions(entryIdx, true);
                if (buttonSlots != null && slotIndex < buttonSlots.Length && buttonSlots[slotIndex] != null)
                    buttonSlots[slotIndex].Flash(activeColor);
                // Propagate Momentary "reset" writes into the stepCurrentValues
                // of any step-sibling entries that target the same property, so
                // that a later RestoreWorldState (triggered by an UNRELATED
                // step press on another entry) doesn't re-apply stale step
                // values and revert the material.
                PropagateMomentaryToStepSiblings(entryIdx);
                // Mark this Momentary press so non-owner clients can replay the
                // action on deserialize. Momentary presses don't mutate
                // entryStates themselves, so HasEntryStateChanged would miss
                // Momentary buttons whose Set actions don't correspond to any
                // step sibling. See OnDeserialization for the receiving side.
                // Local tracker is advanced here too so that if ownership
                // bounces back to us we don't spuriously replay our own press
                // on the next inbound sync.
                momentaryDispatchEntry    = entryIdx;
                momentaryDispatchSeq++;
                _prevMomentaryDispatchSeq = momentaryDispatchSeq;
                _momentaryDispatchBaselined = true;
            }
            else if (btnType == 2) // Step
            {
                HandleStep(entryIdx);
                // Non-stateful step buttons flash; stateful ones get display update via HandleStep
                bool isStateful = rtEntryIsStateful != null && entryIdx < rtEntryIsStateful.Length && rtEntryIsStateful[entryIdx];
                if (!isStateful && buttonSlots != null && slotIndex < buttonSlots.Length && buttonSlots[slotIndex] != null)
                    buttonSlots[slotIndex].Flash(activeColor);
            }
            else if (btnType == 3) // ColorCycle
            {
                HandleColorCycle(entryIdx);
                if (buttonSlots != null && slotIndex < buttonSlots.Length && buttonSlots[slotIndex] != null)
                    buttonSlots[slotIndex].Flash(activeColor);
            }
            // buttonType == 4 (DisplayOnly) — no action

            // Any non-preset press drifts the live state away from whatever
            // preset was last loaded/saved, so extinguish all preset slot
            // indicators. Preset-button presses take a separate path inside
            // HandlePresetPress that calls SavePreset/LoadPreset, which run
            // ClearPresetSlotIndicators themselves and then explicitly light
            // up just the slot they touched — so we must NOT clear here for
            // preset presses or we'd immediately undo that activation.
            bool isPresetPress = rtEntryIsPreset != null && entryIdx < rtEntryIsPreset.Length && rtEntryIsPreset[entryIdx];
            if (!isPresetPress)
                ClearPresetSlotIndicators();

            // Refresh the snapshot used by OnDeserialization's state-change diff so that
            // local mutations made in HandleToggle / HandleStep / HandlePresetPress etc.
            // are reflected in _prevEntryStates. Without this, a later remote deserialize
            // whose value happens to equal the stale _prev would be treated as a no-op
            // and RestoreWorldState would be skipped, leaving the material state stranded.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // ------------------------------------------------------------------------
        //  TOGGLE WITH EXCLUSIVE GROUPS
        // ------------------------------------------------------------------------

        private void HandleToggle(int entryIdx)
        {
            string entryName = rtEntryLabels != null && entryIdx < rtEntryLabels.Length ? rtEntryLabels[entryIdx] : "?";
            bool wasActive = entryStates != null && entryIdx < entryStates.Length && entryStates[entryIdx];
            Log($"HandleToggle() entry={entryIdx} \"{entryName}\" wasActive={wasActive}");

            bool isExclusiveOffBtn = rtEntryExclusiveOff != null && entryIdx < rtEntryExclusiveOff.Length
                                     && rtEntryExclusiveOff[entryIdx];
            bool hasExcl = HasExclusiveGroup(entryIdx);

            if (hasExcl)
            {
                // Clicking the Exclusive Off button while it is the only active one → do nothing.
                if (wasActive && isExclusiveOffBtn) return;

                // Clicking an already-active non-ExclusiveOff button: deactivate self.
                // If the group has an Exclusive Off button, auto-activate it.
                if (wasActive && !isExclusiveOffBtn)
                {
                    entryStates[entryIdx] = false;
                    ExecuteEntryActions(entryIdx, false);

                    int exOffIdx = FindExclusiveOffInGroup(entryIdx);
                    if (exOffIdx >= 0)
                    {
                        entryStates[exOffIdx] = true;
                        ExecuteEntryActions(exOffIdx, true);
                    }
                    return;
                }

                // !wasActive: deactivate all peers, then activate self.
                DeactivateExclusiveGroupPeers(entryIdx);
                entryStates[entryIdx] = true;
                ExecuteEntryActions(entryIdx, true);
                ScheduleEntryExpire(entryIdx);
            }
            else
            {
                // No exclusive group — simple toggle.
                entryStates[entryIdx] = !wasActive;
                ExecuteEntryActions(entryIdx, entryStates[entryIdx]);
                if (entryStates[entryIdx]) ScheduleEntryExpire(entryIdx);
            }
        }

        /// <summary>
        /// Deactivate the visual "active" state on all preset Slot buttons
        /// (presetRole == 0). Called whenever a non-preset press mutates the
        /// live entry/step/color state, so the slot buttons visually reflect
        /// "current state does not match any saved preset" instead of
        /// "slot has data saved" (that's what the [P] label badge is for).
        ///
        /// SavePreset and LoadPreset call this FIRST, then explicitly set
        /// <c>entryStates[entryIdx] = true</c> on just the slot they're
        /// operating on, so exactly one slot lights up at a time.
        /// </summary>
        private void ClearPresetSlotIndicators()
        {
            if (rtEntryIsPreset == null || entryStates == null) return;
            int count = rtEntryIsPreset.Length;
            for (int i = 0; i < count; i++)
            {
                if (!rtEntryIsPreset[i]) continue;
                // Only Slot entries (role 0) use entryStates as a "was recently
                // loaded" indicator. Save/Load/Clear buttons are momentary
                // and should not track state.
                if (rtPresetRoles != null && i < rtPresetRoles.Length && rtPresetRoles[i] != 0)
                    continue;
                if (i < entryStates.Length && entryStates[i])
                    entryStates[i] = false;
            }
        }

        /// <summary>
        /// Schedules an automatic deactivation of <paramref name="entryIdx"/> after the
        /// entry's configured expire time. If the entry has no expire configured
        /// (rtEntryExpireSeconds == 0), this is a no-op.
        /// </summary>
        private void ScheduleEntryExpire(int entryIdx)
        {
            if (rtEntryExpireSeconds == null || entryIdx < 0 || entryIdx >= rtEntryExpireSeconds.Length) return;

            float min = rtEntryExpireSeconds[entryIdx];
            if (min <= 0f) return;

            // Cancel any existing slot for this entry (e.g. re-activated before expire fired).
            for (int q = 0; q < kExpireQueueSize; q++)
            {
                if (_expireQueueOccupied[q] && _expireQueueEntryIdx[q] == entryIdx)
                {
                    _expireQueueOccupied[q] = false;
                    _expireQueueCount--;
                    break;
                }
            }

            // Insert into first free slot.
            for (int q = 0; q < kExpireQueueSize; q++)
            {
                if (_expireQueueOccupied[q]) continue;
                _expireQueueOccupied[q]  = true;
                _expireQueueEntryIdx[q]  = entryIdx;
                _expireQueueTimer[q]     = min;
                _expireQueueCount++;
                return;
            }
        }

        // ------------------------------------------------------------------------
        //  ACTION EXECUTOR
        // ------------------------------------------------------------------------

        private void ExecuteEntryActions(int entryIdx, bool active)
        {
            if (executor == null) return;
            string entryName = rtEntryLabels != null && entryIdx < rtEntryLabels.Length ? rtEntryLabels[entryIdx] : "?";
            Log($"ExecuteEntryActions() entry={entryIdx} \"{entryName}\" active={active}");
            var exe = executor;
            if (rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length) return;

            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];

            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                int type = exe.rtActionTypes[a];

                // Check for a delay on this specific action. Delay defaults to
                // activation-only — on deactivation it's skipped unless the
                // action has rtActionDelayOnDeactivate=true.
                float delay = exe.rtActionDelaySeconds != null && a < exe.rtActionDelaySeconds.Length
                              ? exe.rtActionDelaySeconds[a] : 0f;
                bool delayOnDeact = exe.rtActionDelayOnDeactivate != null && a < exe.rtActionDelayOnDeactivate.Length
                                    && exe.rtActionDelayOnDeactivate[a];
                bool shouldDelay = delay > 0f && (active || delayOnDeact);
                if (shouldDelay)
                {
                    ScheduleDelayedAction(entryIdx, a, active, delay);
                    continue;
                }

                ExecuteSingleAction(entryIdx, a, active);
            }
        }

        /// <summary>
        /// Decides whether Mochie's "Always" shader pass should currently be
        /// enabled on the given renderer/material. The pass renders Zoom, Image
        /// Overlay (_SST), and Letterbox; Mochie's own inspector enables it iff
        /// <c>_Zoom > 0 || _SST > 0 || _Letterbox > 0</c>. The executor calls
        /// this whenever a gate action writes an "off" value or a non-stateful
        /// gate action deactivates, so one effect turning off doesn't kill the
        /// pass while another still needs it.
        ///
        /// Two truth sources, because they disagree:
        ///
        ///  1. ACTIVE ENTRIES — any currently-active entry owning a gate action
        ///     (rtActionAlwaysGate >= 0) on this material holds the pass. This is
        ///     the only reliable source for gates written by non-stateful
        ///     synthetic actions (the Overlay template's _SST), whose material
        ///     value stays 1 after deactivation. All deactivation paths clear
        ///     entryStates before executing actions, so a deactivating entry
        ///     excludes itself; default-on entries are marked active before
        ///     init's ApplyDefaultsOff sweep, so they hold the pass through it.
        ///
        ///  2. MATERIAL VALUES — _Zoom/_Letterbox are read from the material,
        ///     covering momentary Set actions (no entry state) and matching
        ///     Mochie's own rule. A gate's material value is trusted only when
        ///     no non-stateful action writes that gate on this material —
        ///     otherwise the lingering synthetic value would hold the pass on
        ///     forever. _SST is never material-trusted for the same reason
        ///     (the bundled Overlay template always drives it synthetically).
        /// </summary>
        public bool ComputeAlwaysPassHeld(Renderer rend, int matIdx, Material mat)
        {
            // Local entries first.
            int state = GetAlwaysGateStateLocal(rend, matIdx);
            if ((state & 1) != 0) return true;

            // Other controllers sharing this material (wired at rebuild). A
            // controller used to consult only its OWN entries — deactivating a
            // gate on controller A killed the pass while controller B's
            // overlay entry on the same material was still active.
            if (rtOtherControllers != null)
            {
                for (int c = 0; c < rtOtherControllers.Length; c++)
                {
                    var oc = rtOtherControllers[c];
                    if (oc == null) continue;
                    int os = oc.GetAlwaysGateStateLocal(rend, matIdx);
                    if ((os & 1) != 0) return true;
                    state = state | os;
                }
            }

            // Standalone buttons owning gate actions (wired at rebuild).
            if (rtGateHolderButtons != null)
            {
                for (int b = 0; b < rtGateHolderButtons.Length; b++)
                {
                    var gb = rtGateHolderButtons[b];
                    if (gb == null) continue;
                    int bs = gb.GetAlwaysGateState(rend, matIdx);
                    if ((bs & 1) != 0) return true;
                    state = state | bs;
                }
            }

            bool zoomTrusted      = (state & 2) == 0;
            bool letterboxTrusted = (state & 4) == 0;

            if (mat != null)
            {
                if (zoomTrusted && mat.HasProperty("_Zoom") && mat.GetFloat("_Zoom") > 0.5f)
                    return true;
                if (letterboxTrusted && mat.HasProperty("_Letterbox") && mat.GetFloat("_Letterbox") > 0.5f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Local-entries-only gate scan, callable from peer controllers.
        /// Returns a bitmask: bit 0 (1) = an active entry on THIS controller
        /// holds the pass; bit 1 (2) = a non-stateful action here writes the
        /// _Zoom gate (its material value can't be trusted); bit 2 (4) = same
        /// for _Letterbox. _SST is never value-trusted regardless.
        /// </summary>
        public int GetAlwaysGateStateLocal(Renderer rend, int matIdx)
        {
            int state = 0;
            if (executor == null || rend == null) return 0;
            var exe = executor;
            if (exe.rtActionAlwaysGate == null) return 0;
            if (entryStates == null || rtEntryActionStart == null || rtEntryActionCount == null) return 0;

            for (int e = 0; e < entryStates.Length; e++)
            {
                if (e >= rtEntryActionStart.Length || e >= rtEntryActionCount.Length) continue;
                int aStart = rtEntryActionStart[e];
                int aCount = rtEntryActionCount[e];
                for (int a = aStart; a < aStart + aCount; a++)
                {
                    if (a >= exe.rtActionAlwaysGate.Length) break;
                    int gate = exe.rtActionAlwaysGate[a];
                    if (gate < 0) continue;
                    if (exe.rtActionTargetRenderers == null || a >= exe.rtActionTargetRenderers.Length
                        || exe.rtActionTargetRenderers[a] != rend) continue;
                    int mi = exe.rtActionMaterialIndices != null && a < exe.rtActionMaterialIndices.Length
                             ? exe.rtActionMaterialIndices[a] : 0;
                    if (mi != matIdx) continue;

                    if (entryStates[e]) return state | 1; // an active entry holds the pass

                    bool ns = exe.rtActionNonStateful != null && a < exe.rtActionNonStateful.Length
                              && exe.rtActionNonStateful[a];
                    if (ns)
                    {
                        if (gate == 0) state = state | 2;
                        else if (gate == 2) state = state | 4;
                    }
                }
            }
            return state;
        }

        /// <summary>
        /// Material-keyed Always-pass recompute for callers that hold a
        /// Material but not the (renderer, material index) pair — faders.
        /// Resolves the first gate action whose material matches and defers to
        /// the full cross-component recompute; when no gate action references
        /// the material, falls back to honest _Zoom/_Letterbox values.
        /// </summary>
        public bool ComputeAlwaysPassHeldForMaterial(Material mat)
        {
            if (mat == null || executor == null) return false;
            var exe = executor;
            if (exe.rtActionAlwaysGate != null)
            {
                for (int a = 0; a < exe.rtActionAlwaysGate.Length; a++)
                {
                    if (exe.rtActionAlwaysGate[a] < 0) continue;
                    Renderer r = exe.rtActionTargetRenderers != null && a < exe.rtActionTargetRenderers.Length
                                 ? exe.rtActionTargetRenderers[a] : null;
                    if (r == null) continue;
                    int mi = exe.rtActionMaterialIndices != null && a < exe.rtActionMaterialIndices.Length
                             ? exe.rtActionMaterialIndices[a] : 0;
                    Material m = null;
                    if (mi == 0)
                    {
                        m = r.sharedMaterial;
                    }
                    else
                    {
                        Material[] ms = r.sharedMaterials;
                        if (mi < ms.Length) m = ms[mi];
                    }
                    if (m == mat) return ComputeAlwaysPassHeld(r, mi, mat);
                }
            }
            if (mat.HasProperty("_Zoom") && mat.GetFloat("_Zoom") > 0.5f) return true;
            if (mat.HasProperty("_Letterbox") && mat.GetFloat("_Letterbox") > 0.5f) return true;
            return false;
        }

        /// <summary>
        /// When a Momentary button is pressed, propagate its Set-Shader-Property
        /// writes to the stepCurrentValues of any step-sibling entries — entries
        /// whose useStep action targets the same shader property.
        ///
        /// Without this, a Momentary "reset" (e.g. Fog center button setting
        /// _FogFade=0) only updates the material directly. The step backing
        /// store stepCurrentValues[fogPlusEntry] keeps its stale value (e.g.
        /// 30). On the next deserialize-triggered RestoreWorldState — which
        /// can be caused by any UNRELATED step press on another entry — Pass
        /// 1 iterates all inactive step entries and re-applies their
        /// stepCurrentValues to their step-target material properties,
        /// reverting _FogFade back to 30 on the receiver. The presser avoids
        /// this because their own step press never triggers RestoreWorldState
        /// locally.
        ///
        /// Propagating the reset value into the step-sibling stepCurrentValues
        /// keeps the step backing store consistent with the material, so
        /// RestoreWorldState writes the correct value instead of the stale
        /// one. stepCurrentValues is UdonSynced, so the fix flows to receivers
        /// automatically via the existing sync path.
        ///
        /// Only type 2 (Set Shader Property) float actions in the Momentary
        /// entry are considered. Color and Vector properties aren't step
        /// targets. Properties with no matching step sibling are silent
        /// no-ops.
        /// </summary>
        private void PropagateMomentaryToStepSiblings(int entryIdx)
        {
            if (executor == null) return;
            var exe = executor;
            if (rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length) return;
            if (rtEntryActionCount == null || entryIdx >= rtEntryActionCount.Length) return;
            if (stepCurrentValues == null) return;

            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];
            int totalEntries = entryStates != null ? entryStates.Length : 0;

            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                if (exe.rtActionTypes[a] != 2) continue;
                if (exe.rtActionPropertyTypes == null || a >= exe.rtActionPropertyTypes.Length) continue;
                if (exe.rtActionPropertyTypes[a] != 0) continue; // float props only
                if (exe.rtActionPropertyNames == null || a >= exe.rtActionPropertyNames.Length) continue;

                string propName = exe.rtActionPropertyNames[a];
                if (string.IsNullOrEmpty(propName)) continue;

                float value = exe.rtActionFloatValues != null && a < exe.rtActionFloatValues.Length
                              ? exe.rtActionFloatValues[a] : 0f;

                for (int i = 0; i < totalEntries; i++)
                {
                    if (i == entryIdx) continue;
                    if (i >= stepCurrentValues.Length) break;
                    if (i >= rtEntryActionStart.Length || i >= rtEntryActionCount.Length) continue;

                    int sStart = rtEntryActionStart[i];
                    int sCount = rtEntryActionCount[i];
                    for (int sa = sStart; sa < sStart + sCount; sa++)
                    {
                        if (sa >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionUseStep == null || sa >= exe.rtActionUseStep.Length) break;
                        if (!exe.rtActionUseStep[sa]) continue;
                        if (exe.rtActionTypes[sa] != 2) continue;
                        if (exe.rtActionPropertyNames == null || sa >= exe.rtActionPropertyNames.Length) continue;
                        if (exe.rtActionPropertyNames[sa] != propName) continue;

                        stepCurrentValues[i] = value;
                        string lbl = rtEntryLabels != null && i < rtEntryLabels.Length ? rtEntryLabels[i] : "?";
                        Log($"PropagateMomentaryToStepSiblings: entry={i} \"{lbl}\" prop={propName} <- {value}");
                        break; // one match per sibling entry
                    }
                }
            }
        }

        /// <summary>
        /// For all default-off entries, delegates to executor.ApplyDefaults
        /// to reset stateful toggle actions into their off state. This clears stale
        /// material/object values from a previous play session.
        /// Skips entries whose exclusive group already has an active member, since that
        /// member already set the correct values for the shared properties.
        /// </summary>
        private void ApplyDefaultsOff()
        {
            var exe = executor;
            if (exe == null) return;
            if (rtEntryActionStart == null || rtEntryActionCount == null) return;

            int totalEntries = entryStates != null ? entryStates.Length : 0;
            for (int i = 0; i < totalEntries; i++)
            {
                if (entryStates[i]) continue; // already executed with active=true
                if (SharesExclusiveGroupWithActiveEntry(i)) continue;

                int actStart = i < rtEntryActionStart.Length ? rtEntryActionStart[i] : 0;
                int actCount = i < rtEntryActionCount.Length ? rtEntryActionCount[i] : 0;
                exe.ApplyDefaults(actStart, actCount, false);
            }
        }

        /// <summary>
        /// Enqueues a single global action index for delayed execution.
        /// Uses the first unoccupied slot in the fixed-size ring buffer.
        /// If the buffer is full the action executes immediately as a fallback.
        /// (L) If a slot already exists for the same (entryIdx, actionIdx) pair � e.g. due to
        /// rapid toggling � the old slot is cancelled and a fresh slot is opened so only the
        /// most-recent activation/deactivation intent fires.
        /// </summary>
        private void ScheduleDelayedAction(int entryIdx, int actionIdx, bool active, float delay)
        {
            // (A) Snapshot the condition at schedule time.  The fire path uses this value rather
            // than re-evaluating, so a condition-referenced entry changing state during the delay
            // window does not silently suppress or unexpectedly trigger the deferred action.
            // ActionConditionPassed reads entryStates[], which reflects the live game state at
            // press time and is the authoritative source for per-action condition gates.
            bool condSnapshot = ActionConditionPassed(actionIdx);

            // (L) Cancel any existing slot for the same (entryIdx, actionIdx).  This prevents
            // rapid toggling from queuing conflicting actions that would both fire sequentially.
            // By design each (entryIdx, actionIdx) pair can occupy at most one queue slot at
            // a time; ScheduleDelayedAction is the only writer so this invariant holds as long
            // as no other code path calls it concurrently (Udon is single-threaded).
            for (int q = 0; q < kDelayQueueSize; q++)
            {
                if (_delayQueueOccupied[q]
                    && _delayQueueEntryIdx[q]  == entryIdx
                    && _delayQueueActionIdx[q] == actionIdx)
                {
                    _delayQueueOccupied[q] = false;
                    _delayQueueActiveCount--;
                    break; // at most one slot per (entry, action) pair; stop after cancelling it
                }
            }

            for (int q = 0; q < kDelayQueueSize; q++)
            {
                if (_delayQueueOccupied[q]) continue;
                _delayQueueOccupied[q]          = true;
                _delayQueueEntryIdx[q]          = entryIdx;
                _delayQueueActionIdx[q]         = actionIdx;
                _delayQueueActive[q]            = active;
                _delayQueueTimer[q]             = delay;
                _delayQueueConditionSnapshot[q] = condSnapshot;
                _delayQueueActiveCount++;
                return;
            }
            // Queue full � execute immediately as a fallback and warn so the builder knows
            // to either reduce simultaneous delays or increase kDelayQueueSize.
            Debug.LogWarning("[EnigmaController] Delayed-action queue is full; executing action immediately. " +
                             "Consider increasing kDelayQueueSize if this happens frequently.");
            ExecuteSingleAction(entryIdx, actionIdx, active);
        }

        /// <summary>
        /// Returns true when action <paramref name="a"/> is allowed to execute based on
        /// its configured condition.  If no condition is set the method always returns true.
        /// Inspired by CyanTrigger's conditional-logic model.
        /// </summary>
        private bool ActionConditionPassed(int a)
        {
            if (executor == null) return true;
            var exe = executor;

            if (exe.rtActionHasCondition == null || a >= exe.rtActionHasCondition.Length
                || !exe.rtActionHasCondition[a])
                return true;

            int condIdx = exe.rtActionConditionEntryIndex != null && a < exe.rtActionConditionEntryIndex.Length
                          ? exe.rtActionConditionEntryIndex[a] : -1;
            if (condIdx < 0 || entryStates == null || condIdx >= entryStates.Length)
                return true; // unresolved reference � fail open so existing content keeps working

            bool required = exe.rtActionConditionRequireActive != null
                             && a < exe.rtActionConditionRequireActive.Length
                             && exe.rtActionConditionRequireActive[a];
            return entryStates[condIdx] == required;
        }

        /// <summary>
        /// Executes one action by its global index in the flat action arrays.
        /// Checks the per-action condition before executing.  Normal call path.
        /// </summary>
        private void ExecuteSingleAction(int entryIdx, int a, bool active)
        {
            if (executor == null) return;
            if (executor.rtActionTypes == null || a >= executor.rtActionTypes.Length) return;
            // -- Condition check (inspired by CyanTrigger conditional logic) --
            if (!ActionConditionPassed(a)) return;
            ExecuteSingleActionCore(entryIdx, a, active);
        }

        private void ExecuteSingleActionCore(int entryIdx, int a, bool active)
        {
            if (executor == null) return;
            bool isToggle = rtEntryButtonTypes != null && entryIdx >= 0 && entryIdx < rtEntryButtonTypes.Length
                            && rtEntryButtonTypes[entryIdx] == 0;
            executor.ExecuteAction(entryIdx, a, active, isToggle);
        }

        /// <summary>
        /// Handles Color Selector action execution (type 10). Called by the executor
        /// which delegates controller-state-coupled types back to the controller.
        /// </summary>
        public void HandleColorSelectorAction(int entryIdx, int actionIdx, int role)
        {
            if (role == 1) // Set Color: apply pending → applied, write to renderer
            {
                int palStart = rtColorPaletteStart != null && entryIdx < rtColorPaletteStart.Length
                               ? rtColorPaletteStart[entryIdx] : -1;
                int palCount = rtColorPaletteCount != null && entryIdx < rtColorPaletteCount.Length
                               ? rtColorPaletteCount[entryIdx] : 0;
                if (palStart < 0 || palCount <= 0) return;

                int pending = colorPalettePendingIndices != null && entryIdx < colorPalettePendingIndices.Length
                              ? colorPalettePendingIndices[entryIdx] : 0;
                if (colorPaletteCurrentIndices != null && entryIdx < colorPaletteCurrentIndices.Length)
                    colorPaletteCurrentIndices[entryIdx] = pending;

                if (palStart + pending < rtColorPaletteColors.Length)
                {
                    Color selectedColor = rtColorPaletteColors[palStart + pending];
                    Renderer rend = rtColorPaletteRenderers != null && entryIdx < rtColorPaletteRenderers.Length
                                    ? rtColorPaletteRenderers[entryIdx] : null;
                    string prop = rtColorPalettePropertyNames != null && entryIdx < rtColorPalettePropertyNames.Length
                                  ? rtColorPalettePropertyNames[entryIdx] : null;
                    int matIdx = rtColorPaletteMaterialIndices != null && entryIdx < rtColorPaletteMaterialIndices.Length
                                 ? rtColorPaletteMaterialIndices[entryIdx] : 0;
                    if (rend != null && !string.IsNullOrEmpty(prop))
                    {
                        Material[] mats = rend.sharedMaterials;
                        if (matIdx >= 0 && matIdx < mats.Length)
                            mats[matIdx].SetColor(prop, selectedColor);
                    }
                }
            }
            else if (role == 2) // Change Color: cycle pending index on linked Set Color entry
            {
                int linkedEntry = rtColorLinkedEntry != null && entryIdx < rtColorLinkedEntry.Length
                                  ? rtColorLinkedEntry[entryIdx] : -1;
                int src = linkedEntry >= 0 ? linkedEntry : -1;
                if (src < 0) return;

                int palStart = rtColorPaletteStart != null && src < rtColorPaletteStart.Length
                               ? rtColorPaletteStart[src] : -1;
                int palCount = rtColorPaletteCount != null && src < rtColorPaletteCount.Length
                               ? rtColorPaletteCount[src] : 0;
                if (palStart < 0 || palCount <= 0) return;

                // Direction from executor's per-action property type
                int dir = 1;
                if (executor != null && executor.rtActionPropertyTypes != null && actionIdx < executor.rtActionPropertyTypes.Length)
                    dir = executor.rtActionPropertyTypes[actionIdx] == 1 ? -1 : 1;

                int pending = colorPalettePendingIndices != null && src < colorPalettePendingIndices.Length
                              ? colorPalettePendingIndices[src] : 0;
                pending = (pending + dir + palCount) % palCount;
                if (colorPalettePendingIndices != null && src < colorPalettePendingIndices.Length)
                    colorPalettePendingIndices[src] = pending;
            }
        }

        /// <summary>
        /// Handles Variant Selector action execution (type 19). Called by the executor
        /// which delegates controller-state-coupled types back to the controller.
        /// </summary>
        public void HandleVariantSelectorAction(int entryIdx, int actionIdx, int role)
        {
            if (executor == null) return;
            var exe = executor;

            if (role == 1) // Set Variant: commit pending → applied, write to renderer
            {
                int itmStart = rtVariantItemStart != null && entryIdx < rtVariantItemStart.Length
                               ? rtVariantItemStart[entryIdx] : -1;
                int itmCount = rtVariantItemCount != null && entryIdx < rtVariantItemCount.Length
                               ? rtVariantItemCount[entryIdx] : 0;
                if (itmStart < 0 || itmCount <= 0) return;

                int pending = variantPendingIndices != null && entryIdx < variantPendingIndices.Length
                              ? variantPendingIndices[entryIdx] : 0;
                if (variantCurrentIndices != null && entryIdx < variantCurrentIndices.Length)
                    variantCurrentIndices[entryIdx] = pending;

                int flat = itmStart + pending;
                if (flat < (rtVariantItemNames != null ? rtVariantItemNames.Length : 0))
                {
                    Renderer rend = exe.rtActionTargetRenderers != null && actionIdx < exe.rtActionTargetRenderers.Length
                                    ? exe.rtActionTargetRenderers[actionIdx] : null;
                    string prop = exe.rtActionPropertyNames != null && actionIdx < exe.rtActionPropertyNames.Length
                                  ? exe.rtActionPropertyNames[actionIdx] : null;
                    int matIdx = exe.rtActionMaterialIndices != null && actionIdx < exe.rtActionMaterialIndices.Length
                                 ? exe.rtActionMaterialIndices[actionIdx] : 0;
                    int propType = exe.rtActionPropertyTypes != null && actionIdx < exe.rtActionPropertyTypes.Length
                                   ? exe.rtActionPropertyTypes[actionIdx] : 0;
                    if (rend != null && !string.IsNullOrEmpty(prop))
                    {
                        Material[] mats = rend.sharedMaterials;
                        if (matIdx >= 0 && matIdx < mats.Length)
                        {
                            if (propType == 0)
                            {
                                // Managed write with the PER-ITEM keyword —
                                // enum-mode toggles (Mochie _SST/_Zoom/…) gate
                                // a different keyword per value, resolved at
                                // build time into rtVariantItemKeywords.
                                float vv = rtVariantItemFloatValues != null && flat < rtVariantItemFloatValues.Length
                                           ? rtVariantItemFloatValues[flat] : 0f;
                                string vkw = rtVariantItemKeywords != null && flat < rtVariantItemKeywords.Length
                                             ? rtVariantItemKeywords[flat] : "";
                                exe.WriteManagedFloatKeyword(actionIdx, vv, vkw);
                            }
                            else if (propType == 1)
                                mats[matIdx].SetColor(prop,
                                    rtVariantItemColorValues != null && flat < rtVariantItemColorValues.Length
                                    ? rtVariantItemColorValues[flat] : Color.white);
                            else if (propType == 2)
                                mats[matIdx].SetVector(prop,
                                    rtVariantItemVectorValues != null && flat < rtVariantItemVectorValues.Length
                                    ? rtVariantItemVectorValues[flat] : Vector4.zero);
                            else if (propType == 3)
                            {
                                Texture tex = rtVariantItemTextures != null && flat < rtVariantItemTextures.Length
                                              ? rtVariantItemTextures[flat] : null;
                                mats[matIdx].SetTexture(prop, tex);
                            }
                        }
                    }
                }
            }
            else if (role == 2) // Change Variant: cycle pending index on linked role-1 entry
            {
                int linkedEntry = rtVariantLinkedEntry != null && entryIdx < rtVariantLinkedEntry.Length
                                  ? rtVariantLinkedEntry[entryIdx] : -1;
                int src = linkedEntry >= 0 ? linkedEntry : -1;
                if (src < 0) return;

                int itmCount = rtVariantItemCount != null && src < rtVariantItemCount.Length
                               ? rtVariantItemCount[src] : 0;
                if (itmCount <= 0) return;

                int dir = 1;
                if (exe.rtActionPropertyTypes != null && actionIdx < exe.rtActionPropertyTypes.Length)
                    dir = exe.rtActionPropertyTypes[actionIdx] == 1 ? -1 : 1;

                int pending = variantPendingIndices != null && src < variantPendingIndices.Length
                              ? variantPendingIndices[src] : 0;
                pending = (pending + dir + itmCount) % itmCount;
                if (variantPendingIndices != null && src < variantPendingIndices.Length)
                    variantPendingIndices[src] = pending;
            }
        }

        // ------------------------------------------------------------------------
        //  STEP BUTTON
        // ------------------------------------------------------------------------

        /// <summary>
        /// Returns the baked boolean target state for Command/SetState actions
        /// (types 15�18). The build pipeline stores the desired on/off value as
        /// <c>rtActionFloatValues[a] >= 0.5f</c>.
        /// </summary>
        private bool GetBakedTargetState(int a)
        {
            return executor != null && executor.GetBakedTargetState(a);
        }

        private void HandleStep(int entryIdx)
        {
            bool isStateful = rtEntryIsStateful != null && entryIdx < rtEntryIsStateful.Length && rtEntryIsStateful[entryIdx];
            bool wasActive = entryStates != null && entryIdx < entryStates.Length && entryStates[entryIdx];

            float step = rtStepAmounts != null && entryIdx < rtStepAmounts.Length
                         ? rtStepAmounts[entryIdx] : 0.1f;
            float min  = rtStepMinValues != null && entryIdx < rtStepMinValues.Length
                         ? rtStepMinValues[entryIdx] : 0f;
            float max  = rtStepMaxValues != null && entryIdx < rtStepMaxValues.Length
                         ? rtStepMaxValues[entryIdx] : 1f;

            // Read the current value from the actual target (material property or
            // Udon variable) so multiple buttons sharing the same property stay in sync.
            float current = ReadStepCurrentValue(entryIdx);

            current += step;
            bool wrap = rtStepWrap != null && entryIdx < rtStepWrap.Length && rtStepWrap[entryIdx];
            if (wrap)
            {
                if (current > max) current = min;
                else if (current < min) current = max;
            }
            else
            {
                // Only clamp against the relevant bound for the step direction.
                // A negative step button only cares about min (floor), not max.
                // A positive step button only cares about max (ceiling), not min.
                if (step > 0f && current > max) current = max;
                else if (step < 0f && current < min) current = min;
            }

            // Round to step precision to avoid floating-point drift.
            if (step != 0f)
            {
                float factor = 1f / Mathf.Abs(step);
                current = Mathf.Round(current * factor) / factor;
            }

            if (stepCurrentValues != null && entryIdx < stepCurrentValues.Length)
                stepCurrentValues[entryIdx] = current;

            if (isStateful)
            {
                // Stateful step (Toggle+Step): first press activates + deactivates exclusive peers.
                // Subsequent presses just step while staying active.
                if (!wasActive && HasExclusiveGroup(entryIdx))
                    DeactivateExclusiveGroupPeers(entryIdx);

                if (entryStates != null && entryIdx < entryStates.Length)
                    entryStates[entryIdx] = true;
            }
            else
            {
                // Non-stateful step (Set+Step): deactivate exclusive peers if any, but don't set self active.
                if (HasExclusiveGroup(entryIdx))
                    DeactivateExclusiveGroupPeers(entryIdx);
            }

            // Write the computed step value into the baked float array for the
            // step target action, then execute ALL actions through the normal path.
            // This ensures every action is self-contained and order-independent.
            WriteStepValueToBakedArray(entryIdx, current);
            ExecuteEntryActions(entryIdx, true);
        }

        /// <summary>
        /// Writes the computed step value into rtActionFloatValues for the first
        /// type-2-Float or type-6 action in the entry (the step target). This lets
        /// the normal ExecuteEntryActions path pick up the step value without any
        /// special-case handling in the action executor.
        /// </summary>
        private void WriteStepValueToBakedArray(int entryIdx, float value)
        {
            if (executor == null || rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length) return;
            var exe = executor;
            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];

            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                // Find the action with useStep=true and write the step value there.
                if (exe.rtActionUseStep != null && a < exe.rtActionUseStep.Length
                    && exe.rtActionUseStep[a]
                    && exe.rtActionFloatValues != null && a < exe.rtActionFloatValues.Length)
                {
                    exe.rtActionFloatValues[a] = value;
                    return;
                }
            }
        }

        /// <summary>
        /// Reads the live value from the first step-capable action's target
        /// (shader property or Udon variable). Falls back to stepCurrentValues.
        /// </summary>
        private float ReadStepCurrentValue(int entryIdx)
        {
            if (rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length)
                return stepCurrentValues != null && entryIdx < stepCurrentValues.Length
                       ? stepCurrentValues[entryIdx] : 0f;

            if (executor == null)
                return stepCurrentValues != null && entryIdx < stepCurrentValues.Length
                       ? stepCurrentValues[entryIdx] : 0f;
            var exe = executor;

            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];

            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                int type = exe.rtActionTypes[a];

                // Only read from the action that has useStep=true.
                if (exe.rtActionUseStep == null || a >= exe.rtActionUseStep.Length
                    || !exe.rtActionUseStep[a])
                    continue;

                // Shader property source
                if (type == 2
                    && exe.rtActionTargetRenderers != null && a < exe.rtActionTargetRenderers.Length
                    && exe.rtActionTargetRenderers[a] != null
                    && exe.rtActionPropertyNames != null && a < exe.rtActionPropertyNames.Length
                    && exe.rtActionPropertyTypes != null && a < exe.rtActionPropertyTypes.Length
                    && exe.rtActionPropertyTypes[a] == 0)
                {
                    Material[] mats = exe.rtActionTargetRenderers[a].sharedMaterials;
                    int matIdx = exe.rtActionMaterialIndices != null && a < exe.rtActionMaterialIndices.Length ? exe.rtActionMaterialIndices[a] : 0;
                    if (matIdx >= 0 && matIdx < mats.Length && mats[matIdx] != null)
                    {
                        if (mats[matIdx].HasProperty(exe.rtActionPropertyNames[a]))
                            return mats[matIdx].GetFloat(exe.rtActionPropertyNames[a]);
                    }
                }

                // Udon variable source
                if (type == 6
                    && exe.rtActionUdonTargets != null && a < exe.rtActionUdonTargets.Length
                    && exe.rtActionUdonTargets[a] != null
                    && exe.rtActionUdonVariableNames != null && a < exe.rtActionUdonVariableNames.Length
                    && !string.IsNullOrEmpty(exe.rtActionUdonVariableNames[a]))
                {
                    object val = exe.rtActionUdonTargets[a].GetProgramVariable(exe.rtActionUdonVariableNames[a]);
                    if (val != null && val.GetType() == typeof(float)) return (float)val;
                    if (val != null && val.GetType() == typeof(int)) return (float)(int)val;
                }
            }

            return stepCurrentValues != null && entryIdx < stepCurrentValues.Length
                   ? stepCurrentValues[entryIdx] : 0f;
        }

        /// <summary>
        /// Returns the configured default float value for the first type-2 or type-6
        /// step action in this entry. Falls back to stepMin if no default is found.
        /// </summary>
        private float ReadStepDefaultValue(int entryIdx)
        {
            var exe = executor;
            if (exe == null || rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length)
                return rtStepMinValues != null && entryIdx < rtStepMinValues.Length
                       ? rtStepMinValues[entryIdx] : 0f;

            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];
            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                if (exe.rtActionUseStep == null || a >= exe.rtActionUseStep.Length || !exe.rtActionUseStep[a])
                    continue;
                int type = exe.rtActionTypes[a];
                if (type == 2 && exe.rtActionDefaultFloatValues != null && a < exe.rtActionDefaultFloatValues.Length)
                    return exe.rtActionDefaultFloatValues[a];
                if (type == 6 && exe.rtActionDefaultFloatValues != null && a < exe.rtActionDefaultFloatValues.Length)
                    return exe.rtActionDefaultFloatValues[a];
            }

            return rtStepMinValues != null && entryIdx < rtStepMinValues.Length
                   ? rtStepMinValues[entryIdx] : 0f;
        }

        /// <summary>
        /// Writes the given step value to the step-target action of the specified
        /// entry — and nothing else.
        ///
        /// Matching rule: the step target is the single action in the entry whose
        /// <c>rtActionUseStep[a]</c> flag is true. This is the same rule used by
        /// <see cref="WriteStepValueToBakedArray"/> and <see cref="ReadStepCurrentValue"/>.
        ///
        /// Callers (RestoreWorldState, LoadPreset, ResetAll) are responsible for
        /// executing the entry's non-step-target actions via ExecuteEntryActions
        /// themselves. This function must not touch other actions, because doing so
        /// would:
        ///   (a) double-fire effects that were already applied by ExecuteEntryActions
        ///       on the same caller tick (e.g. re-running a Set Color commit), and
        ///   (b) run actions with active=true on entries whose entryStates[i] is
        ///       false (Pass 1 of RestoreWorldState), which incorrectly triggers
        ///       Color Selector role 2 to cycle the pending palette index on every
        ///       deserialize — observable as palette selector buttons changing color
        ///       without being pressed.
        ///
        /// Entries without a step-target action are a no-op here.
        /// </summary>
        private void ExecuteStepActions(int entryIdx, float value)
        {
            if (rtEntryActionStart == null || entryIdx >= rtEntryActionStart.Length) return;
            if (executor == null) return;
            var exe = executor;

            int actStart = rtEntryActionStart[entryIdx];
            int actCount = rtEntryActionCount[entryIdx];

            for (int a = actStart; a < actStart + actCount; a++)
            {
                if (exe.rtActionTypes == null || a >= exe.rtActionTypes.Length) break;
                if (exe.rtActionUseStep == null || a >= exe.rtActionUseStep.Length
                    || !exe.rtActionUseStep[a])
                    continue;

                int type = exe.rtActionTypes[a];

                if (type == 2
                    && exe.rtActionTargetRenderers != null && a < exe.rtActionTargetRenderers.Length
                    && exe.rtActionTargetRenderers[a] != null
                    && exe.rtActionPropertyTypes != null && a < exe.rtActionPropertyTypes.Length
                    && exe.rtActionPropertyTypes[a] == 0)
                {
                    // Managed write (SetInt mirror + keyword + Always-pass
                    // gate) — a naked SetFloat here left late-joiners with
                    // stale int uniforms on Int-declared Mochie properties
                    // and never toggled the pass for gate properties.
                    exe.WriteManagedFloat(a, value);
                    return;
                }

                if (type == 6
                    && exe.rtActionUdonTargets != null && a < exe.rtActionUdonTargets.Length
                    && exe.rtActionUdonTargets[a] != null
                    && exe.rtActionUdonVariableNames != null && a < exe.rtActionUdonVariableNames.Length
                    && !string.IsNullOrEmpty(exe.rtActionUdonVariableNames[a]))
                {
                    exe.rtActionUdonTargets[a].SetProgramVariable(exe.rtActionUdonVariableNames[a], value);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------------
        //  COLOR CYCLE BUTTON
        // ------------------------------------------------------------------------

        private void HandleColorCycle(int entryIdx)
        {
            int palStart = rtColorPaletteStart != null && entryIdx < rtColorPaletteStart.Length
                           ? rtColorPaletteStart[entryIdx] : -1;
            int palCount = rtColorPaletteCount != null && entryIdx < rtColorPaletteCount.Length
                           ? rtColorPaletteCount[entryIdx] : 0;
            if (palStart < 0 || palCount <= 0) return;

            int curIdx = colorPaletteCurrentIndices != null && entryIdx < colorPaletteCurrentIndices.Length
                         ? colorPaletteCurrentIndices[entryIdx] : 0;
            curIdx = (curIdx + 1) % palCount;
            if (colorPaletteCurrentIndices != null && entryIdx < colorPaletteCurrentIndices.Length)
                colorPaletteCurrentIndices[entryIdx] = curIdx;

            Color selectedColor = rtColorPaletteColors[palStart + curIdx];

            Renderer rend = rtColorPaletteRenderers != null && entryIdx < rtColorPaletteRenderers.Length
                            ? rtColorPaletteRenderers[entryIdx] : null;
            string prop   = rtColorPalettePropertyNames != null && entryIdx < rtColorPalettePropertyNames.Length
                            ? rtColorPalettePropertyNames[entryIdx] : null;
            int matIdx    = rtColorPaletteMaterialIndices != null && entryIdx < rtColorPaletteMaterialIndices.Length
                            ? rtColorPaletteMaterialIndices[entryIdx] : 0;

            if (rend != null && !string.IsNullOrEmpty(prop))
            {
                Material[] mats = rend.sharedMaterials;
                if (matIdx >= 0 && matIdx < mats.Length)
                    mats[matIdx].SetColor(prop, selectedColor);
            }
        }

    }
}

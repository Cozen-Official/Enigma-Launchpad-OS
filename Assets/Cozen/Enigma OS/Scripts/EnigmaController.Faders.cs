
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  FADER BINDING
        // ------------------------------------------------------------------------

        private void BindStaticFaders()
        {
            if (faderSlots == null || staticFaderCount <= 0 || rtStaticFaderNames == null) return;

            int count = Mathf.Min(staticFaderCount, faderSlots.Length);
            for (int f = 0; f < count; f++)
            {
                if (faderSlots[f] == null) continue;

                // Ensure we do not go out of bounds of the static arrays
                if (f >= rtStaticFaderNames.Length) break;

                bool isUdon = f < rtStaticFaderTargetsUdon.Length && rtStaticFaderTargetsUdon[f];
                bool isSlider = f < rtStaticFaderTargetsSlider.Length && rtStaticFaderTargetsSlider[f];

                bool indEnabled = f < rtStaticFaderIndicatorEnabled.Length && rtStaticFaderIndicatorEnabled[f];
                Color indColor = f < rtStaticFaderIndicatorColors.Length ? rtStaticFaderIndicatorColors[f] : Color.white;
                bool indConditional = f < rtStaticFaderIndicatorConditional.Length && rtStaticFaderIndicatorConditional[f];
                Color defColor = f < rtStaticFaderDefaultColors.Length ? rtStaticFaderDefaultColors[f] : Color.white;
                int pType = f < rtStaticFaderPropertyTypes.Length ? rtStaticFaderPropertyTypes[f] : 0;
                float minV = f < rtStaticFaderMinValues.Length ? rtStaticFaderMinValues[f] : 0f;
                float maxV = f < rtStaticFaderMaxValues.Length ? rtStaticFaderMaxValues[f] : 1f;
                float defV = f < rtStaticFaderDefaultValues.Length ? rtStaticFaderDefaultValues[f] : 0f;

                if (isSlider)
                {
#if UNITY_UI
                    UnityEngine.UI.Slider targetSlider = f < rtStaticFaderSliders.Length ? rtStaticFaderSliders[f] as UnityEngine.UI.Slider : null;
                    bool reversed = f < rtStaticFaderSliderReversed.Length && rtStaticFaderSliderReversed[f];
                    if (targetSlider != null)
                    {
                        faderSlots[f].BindSlider(
                            rtStaticFaderNames[f],
                            targetSlider,
                            reversed,
                            pType,
                            minV,
                            maxV,
                            defV,
                            defColor,
                            indEnabled,
                            indColor,
                            indConditional
                        );
                    }
#endif
                }
                else if (isUdon)
                {
                    string varName = f < rtStaticFaderUdonVariableNames.Length ? rtStaticFaderUdonVariableNames[f] : null;
                    UdonSharpBehaviour[] bindBehaviours = CollectStaticFaderUdonBehaviours(f);
                    if (bindBehaviours != null && bindBehaviours.Length > 0 && !string.IsNullOrEmpty(varName))
                    {
                        faderSlots[f].BindUdon(
                            rtStaticFaderNames[f],
                            bindBehaviours,
                            varName,
                            pType,
                            minV,
                            maxV,
                            defV,
                            defColor,
                            indEnabled,
                            indColor,
                            indConditional
                        );
                    }
                }
                else
                {
                    string prop = f < rtStaticFaderPropertyNames.Length ? rtStaticFaderPropertyNames[f] : null;
                    Material[] bindMats = CollectStaticFaderMaterials(f);
                    if (bindMats != null && bindMats.Length > 0 && !string.IsNullOrEmpty(prop))
                    {
                        faderSlots[f].Bind(
                            rtStaticFaderNames[f],
                            bindMats,
                            prop,
                            pType,
                            minV,
                            maxV,
                            defV,
                            defColor,
                            indEnabled,
                            indColor,
                            indConditional
                        );
                    }
                }
            }
        }

        // Build the Material[] that a static fader binds to. The primary
        // renderer contributes rtStaticFaderMaterialIndices[idx] of its
        // sharedMaterials; each non-null extra renderer (stored in the
        // rtStaticFaderExtra* flat arrays) contributes its own chosen
        // material index. Nulls are skipped. Returns null if no materials
        // are available (primary missing AND no valid extras).
        private Material[] CollectStaticFaderMaterials(int idx)
        {
            // Primary
            Renderer primary = idx >= 0 && idx < rtStaticFaderRenderers.Length
                ? rtStaticFaderRenderers[idx] : null;
            int primaryMatIdx = idx >= 0 && idx < rtStaticFaderMaterialIndices.Length
                ? rtStaticFaderMaterialIndices[idx] : 0;
            Material primaryMat = null;
            if (primary != null)
            {
                Material[] pm = primary.sharedMaterials;
                if (primaryMatIdx >= 0 && primaryMatIdx < pm.Length)
                    primaryMat = pm[primaryMatIdx];
            }

            // Extras: compute start = prefix sum of rtStaticFaderExtraCount[0..idx-1].
            int extraStart = 0;
            if (rtStaticFaderExtraCount != null)
            {
                int stop = idx < rtStaticFaderExtraCount.Length ? idx : rtStaticFaderExtraCount.Length;
                for (int i = 0; i < stop; i++)
                    extraStart += rtStaticFaderExtraCount[i];
            }
            int extraCount = rtStaticFaderExtraCount != null && idx >= 0 && idx < rtStaticFaderExtraCount.Length
                ? rtStaticFaderExtraCount[idx] : 0;

            // Count valid extras to size the output array.
            int validExtras = 0;
            for (int e = 0; e < extraCount; e++)
            {
                int flat = extraStart + e;
                if (rtStaticFaderExtraRenderers == null || flat >= rtStaticFaderExtraRenderers.Length) continue;
                Renderer xr = rtStaticFaderExtraRenderers[flat];
                if (xr == null) continue;
                Material[] xms = xr.sharedMaterials;
                int xmi = rtStaticFaderExtraMaterialIndices != null && flat < rtStaticFaderExtraMaterialIndices.Length
                    ? rtStaticFaderExtraMaterialIndices[flat] : 0;
                if (xmi >= 0 && xmi < xms.Length && xms[xmi] != null)
                    validExtras++;
            }

            int total = (primaryMat != null ? 1 : 0) + validExtras;
            if (total == 0) return null;

            Material[] result = new Material[total];
            int wi = 0;
            if (primaryMat != null) result[wi++] = primaryMat;
            for (int e = 0; e < extraCount; e++)
            {
                int flat = extraStart + e;
                if (rtStaticFaderExtraRenderers == null || flat >= rtStaticFaderExtraRenderers.Length) continue;
                Renderer xr = rtStaticFaderExtraRenderers[flat];
                if (xr == null) continue;
                Material[] xms = xr.sharedMaterials;
                int xmi = rtStaticFaderExtraMaterialIndices != null && flat < rtStaticFaderExtraMaterialIndices.Length
                    ? rtStaticFaderExtraMaterialIndices[flat] : 0;
                if (xmi >= 0 && xmi < xms.Length && xms[xmi] != null)
                    result[wi++] = xms[xmi];
            }
            return result;
        }

        // Build the UdonSharpBehaviour[] that a static fader binds to. Same
        // primary + extras pattern as CollectStaticFaderMaterials but for
        // Udon targets — primary lives in rtStaticFaderUdonBehaviours[idx]
        // and extras live in rtStaticFaderExtraUdonBehaviours at
        // prefix-sum(rtStaticFaderExtraUdonCount[0..idx-1]). Nulls are
        // skipped. Returns null if no behaviours are available.
        private UdonSharpBehaviour[] CollectStaticFaderUdonBehaviours(int idx)
        {
            UdonSharpBehaviour primary = idx >= 0 && idx < rtStaticFaderUdonBehaviours.Length
                ? rtStaticFaderUdonBehaviours[idx] : null;

            int extraStart = 0;
            if (rtStaticFaderExtraUdonCount != null)
            {
                int stop = idx < rtStaticFaderExtraUdonCount.Length ? idx : rtStaticFaderExtraUdonCount.Length;
                for (int i = 0; i < stop; i++)
                    extraStart += rtStaticFaderExtraUdonCount[i];
            }
            int extraCount = rtStaticFaderExtraUdonCount != null && idx >= 0 && idx < rtStaticFaderExtraUdonCount.Length
                ? rtStaticFaderExtraUdonCount[idx] : 0;

            int validExtras = 0;
            for (int e = 0; e < extraCount; e++)
            {
                int flat = extraStart + e;
                if (rtStaticFaderExtraUdonBehaviours == null
                    || flat >= rtStaticFaderExtraUdonBehaviours.Length) continue;
                if (rtStaticFaderExtraUdonBehaviours[flat] != null) validExtras++;
            }

            int total = (primary != null ? 1 : 0) + validExtras;
            if (total == 0) return null;

            UdonSharpBehaviour[] result = new UdonSharpBehaviour[total];
            int wi = 0;
            if (primary != null) result[wi++] = primary;
            for (int e = 0; e < extraCount; e++)
            {
                int flat = extraStart + e;
                if (rtStaticFaderExtraUdonBehaviours == null
                    || flat >= rtStaticFaderExtraUdonBehaviours.Length) continue;
                var xb = rtStaticFaderExtraUdonBehaviours[flat];
                if (xb != null) result[wi++] = xb;
            }
            return result;
        }

        public void UpdateFaderBindings()
        {
            if (faderSlots == null) return;
            int slotsPerPage = faderSlots.Length;

            // Unbind ALL fader slots.
            for (int f = 0; f < slotsPerPage; f++)
            {
                if (faderSlots[f] != null)
                    faderSlots[f].Unbind();
            }

            // Pass 1: bind always-visible static faders to their natural slot positions.
            // These are pinned on every page and consume slots from the page budget.
            int pinnedCount = 0;
            for (int s = 0; s < staticFaderCount; s++)
            {
                bool pinned = rtStaticFaderAlwaysVisible != null
                              && s < rtStaticFaderAlwaysVisible.Length
                              && rtStaticFaderAlwaysVisible[s];
                if (!pinned) continue;
                if (pinnedCount < slotsPerPage && faderSlots[pinnedCount] != null)
                    BindStaticFaderToSlot(s, pinnedCount);
                pinnedCount++;
            }

            // Pass 2: page through non-pinned bindings (remaining static + dynamic)
            // using the slots after the pinned ones.
            int pageableSlots = slotsPerPage - pinnedCount;
            if (pageableSlots > 0)
            {
                int pageStart = currentFaderPage * pageableSlots;
                int slotIdx = pinnedCount;
                int bindingIdx = 0;

                // Non-pinned static faders
                for (int s = 0; s < staticFaderCount; s++)
                {
                    bool pinned = rtStaticFaderAlwaysVisible != null
                                  && s < rtStaticFaderAlwaysVisible.Length
                                  && rtStaticFaderAlwaysVisible[s];
                    if (pinned) continue;

                    if (bindingIdx >= pageStart && slotIdx < slotsPerPage)
                    {
                        if (faderSlots[slotIdx] != null)
                            BindStaticFaderToSlot(s, slotIdx);
                        slotIdx++;
                    }
                    bindingIdx++;
                }

                // Dynamic fader bindings (active entry links)
                if (rtFaderLinkEntryIndex != null && entryStates != null)
                {
                    for (int l = 0; l < rtFaderLinkEntryIndex.Length; l++)
                    {
                        int eIdx = rtFaderLinkEntryIndex[l];
                        if (eIdx >= entryStates.Length || !entryStates[eIdx]) continue;

                        if (bindingIdx >= pageStart && slotIdx < slotsPerPage)
                        {
                            if (faderSlots[slotIdx] != null)
                                BindDynamicFaderToSlot(l, eIdx, slotIdx);
                            slotIdx++;
                        }
                        bindingIdx++;
                        if (slotIdx >= slotsPerPage) break;
                    }
                }
            }

            // Clamp page if bindings shrank below current page.
            int totalPages = GetFaderPageCount();
            if (currentFaderPage >= totalPages)
                currentFaderPage = Mathf.Max(0, totalPages - 1);
        }

        private void BindStaticFaderToSlot(int staticIdx, int slotIdx)
        {
            if (staticIdx >= staticFaderCount || rtStaticFaderNames == null
                || staticIdx >= rtStaticFaderNames.Length) return;
            if (faderSlots[slotIdx] == null) return;

            int f = staticIdx;
            bool isUdon = f < rtStaticFaderTargetsUdon.Length && rtStaticFaderTargetsUdon[f];
            bool isSlider = f < rtStaticFaderTargetsSlider.Length && rtStaticFaderTargetsSlider[f];
            bool isSkybox = f < rtStaticFaderTargetsSkybox.Length && rtStaticFaderTargetsSkybox[f];
            bool indEnabled = f < rtStaticFaderIndicatorEnabled.Length && rtStaticFaderIndicatorEnabled[f];
            Color indColor = f < rtStaticFaderIndicatorColors.Length ? rtStaticFaderIndicatorColors[f] : Color.white;
            bool indConditional = f < rtStaticFaderIndicatorConditional.Length && rtStaticFaderIndicatorConditional[f];
            Color defColor = f < rtStaticFaderDefaultColors.Length ? rtStaticFaderDefaultColors[f] : Color.white;
            int pType = f < rtStaticFaderPropertyTypes.Length ? rtStaticFaderPropertyTypes[f] : 0;
            float minV = f < rtStaticFaderMinValues.Length ? rtStaticFaderMinValues[f] : 0f;
            float maxV = f < rtStaticFaderMaxValues.Length ? rtStaticFaderMaxValues[f] : 1f;
            float defV = f < rtStaticFaderDefaultValues.Length ? rtStaticFaderDefaultValues[f] : 0f;

            if (isSlider)
            {
#if UNITY_UI
                UnityEngine.UI.Slider targetSlider = f < rtStaticFaderSliders.Length ? rtStaticFaderSliders[f] as UnityEngine.UI.Slider : null;
                bool reversed = f < rtStaticFaderSliderReversed.Length && rtStaticFaderSliderReversed[f];
                if (targetSlider != null)
                    faderSlots[slotIdx].BindSlider(rtStaticFaderNames[f], targetSlider, reversed, pType, minV, maxV, defV, defColor, indEnabled, indColor, indConditional);
#endif
            }
            else if (isUdon)
            {
                string varName = f < rtStaticFaderUdonVariableNames.Length ? rtStaticFaderUdonVariableNames[f] : null;
                UdonSharpBehaviour[] bindBehaviours = CollectStaticFaderUdonBehaviours(f);
                if (bindBehaviours != null && bindBehaviours.Length > 0 && !string.IsNullOrEmpty(varName))
                    faderSlots[slotIdx].BindUdon(rtStaticFaderNames[f], bindBehaviours, varName, pType, minV, maxV, defV, defColor, indEnabled, indColor, indConditional);
            }
            else if (isSkybox)
            {
                string prop = f < rtStaticFaderPropertyNames.Length ? rtStaticFaderPropertyNames[f] : null;
                Material skyMat = RenderSettings.skybox;
                if (skyMat != null && !string.IsNullOrEmpty(prop))
                    faderSlots[slotIdx].Bind(rtStaticFaderNames[f], new Material[] { skyMat }, prop, pType, minV, maxV, defV, defColor, indEnabled, indColor, indConditional);
            }
            else
            {
                string prop = f < rtStaticFaderPropertyNames.Length ? rtStaticFaderPropertyNames[f] : null;
                Material[] bindMats = CollectStaticFaderMaterials(f);
                if (bindMats != null && bindMats.Length > 0 && !string.IsNullOrEmpty(prop))
                    faderSlots[slotIdx].Bind(rtStaticFaderNames[f], bindMats, prop, pType, minV, maxV, defV, defColor, indEnabled, indColor, indConditional);
            }
        }

        private void BindDynamicFaderToSlot(int linkIdx, int entryIdx, int slotIdx)
        {
            int l = linkIdx;

            // Restore the fader slot's currentValue from the per-link remembered value
            // so the fader appears at the position it was last set to, regardless of
            // which physical slot it's assigned to this time.
            if (faderLinkCurrentValues != null && l < faderLinkCurrentValues.Length)
            {
                Log($"BindDynamicFader link={l} slot={slotIdx}: remembered={faderLinkCurrentValues[l]}, slot.currentValue was={faderSlots[slotIdx].currentValue}");
                faderSlots[slotIdx].currentValue = faderLinkCurrentValues[l];
            }
            faderSlots[slotIdx].boundLinkIndex = l;

            bool isSlider = l < rtFaderLinkTargetsSlider.Length && rtFaderLinkTargetsSlider[l];
            bool isSkybox = l < rtFaderLinkTargetsSkybox.Length && rtFaderLinkTargetsSkybox[l];
            bool isUdon = l < rtFaderLinkTargetsUdon.Length && rtFaderLinkTargetsUdon[l];

            bool indEnabled = l < rtFaderLinkIndicatorEnabled.Length && rtFaderLinkIndicatorEnabled[l];
            Color indColor = l < rtFaderLinkIndicatorColors.Length ? rtFaderLinkIndicatorColors[l] : Color.white;
            bool indConditional = l < rtFaderLinkIndicatorConditional.Length && rtFaderLinkIndicatorConditional[l];
            Color defColor = l < rtFaderLinkDefaultColors.Length ? rtFaderLinkDefaultColors[l] : Color.white;
            int pType = l < rtFaderLinkPropertyTypes.Length ? rtFaderLinkPropertyTypes[l] : 0;
            float minV = l < rtFaderLinkMinValues.Length ? rtFaderLinkMinValues[l] : 0f;
            float maxV = l < rtFaderLinkMaxValues.Length ? rtFaderLinkMaxValues[l] : 1f;
            float defV = l < rtFaderLinkDefaultValues.Length ? rtFaderLinkDefaultValues[l] : 0f;

            if (isSlider)
            {
#if UNITY_UI
                UnityEngine.UI.Slider targetSlider = l < rtFaderLinkSliders.Length ? rtFaderLinkSliders[l] as UnityEngine.UI.Slider : null;
                bool reversed = l < rtFaderLinkSliderReversed.Length && rtFaderLinkSliderReversed[l];
                if (targetSlider != null)
                {
                    faderSlots[slotIdx].BindSlider(
                        ResolveDynamicFaderDisplayName(l, entryIdx),
                        targetSlider, reversed, pType, minV, maxV, defV,
                        defColor, indEnabled, indColor, indConditional
                    );
                }
#endif
            }
            else if (isUdon)
            {
                UdonSharpBehaviour targetBehaviour = l < rtFaderLinkUdonBehaviours.Length ? rtFaderLinkUdonBehaviours[l] : null;
                string varName = l < rtFaderLinkUdonVariableNames.Length ? rtFaderLinkUdonVariableNames[l] : null;
                if (targetBehaviour != null && !string.IsNullOrEmpty(varName))
                {
                    faderSlots[slotIdx].BindUdon(
                        ResolveDynamicFaderDisplayName(l, entryIdx),
                        targetBehaviour, varName, pType, minV, maxV, defV,
                        defColor, indEnabled, indColor, indConditional
                    );
                }
            }
            else if (isSkybox)
            {
                string prop = l < rtFaderLinkPropertyNames.Length ? rtFaderLinkPropertyNames[l] : null;
                Material skyMat = RenderSettings.skybox;
                if (skyMat != null && !string.IsNullOrEmpty(prop))
                {
                    faderSlots[slotIdx].Bind(
                        ResolveDynamicFaderDisplayName(l, entryIdx),
                        new Material[] { skyMat },
                        prop, pType, minV, maxV, defV,
                        defColor, indEnabled, indColor, indConditional
                    );
                }
            }
            else
            {
                Material[] mats = GetMaterialsForFaderLink(l);
                faderSlots[slotIdx].Bind(
                    ResolveDynamicFaderDisplayName(l, entryIdx),
                    mats,
                    rtFaderLinkPropertyNames[l],
                    pType, minV, maxV, defV,
                    defColor, indEnabled, indColor, indConditional
                );
            }
        }

        /// <summary>
        /// Returns the display name the fader slot UI should show for a
        /// dynamic fader link. Prefers the link's own
        /// <c>EnigmaFaderLinkData.faderName</c> (stored in
        /// <see cref="rtFaderLinkNames"/>) — that's what the user typed in
        /// the "Fader Name" field on the button's fader link. Falls back to
        /// the owning entry's label when the user didn't specify a name,
        /// which preserves the historical behavior for existing buttons
        /// that never set a fader name.
        ///
        /// Without this split, every dynamic fader on a button displayed
        /// the button label verbatim — so "Holo 1" / "Holo 2" / "Holo Thick"
        /// on the Holographic Outline button all read "Holographic Outline"
        /// in-play and were indistinguishable on the physical hardware.
        /// </summary>
        private string ResolveDynamicFaderDisplayName(int linkIdx, int entryIdx)
        {
            string linkName = linkIdx >= 0 && linkIdx < rtFaderLinkNames.Length
                ? rtFaderLinkNames[linkIdx] : null;
            if (!string.IsNullOrEmpty(linkName)) return linkName;
            return entryIdx >= 0 && entryIdx < rtEntryLabels.Length
                ? rtEntryLabels[entryIdx] : "";
        }

        private Material[] GetMaterialsForFaderLink(int linkIdx)
        {
            Renderer rend = rtFaderLinkRenderers != null && linkIdx < rtFaderLinkRenderers.Length
                            ? rtFaderLinkRenderers[linkIdx] : null;
            int matIdx    = rtFaderLinkMaterialIndices != null && linkIdx < rtFaderLinkMaterialIndices.Length
                            ? rtFaderLinkMaterialIndices[linkIdx] : 0;
            if (rend == null) return null;
            Material[] all = rend.sharedMaterials;
            if (matIdx < 0 || matIdx >= all.Length) return null;
            return new Material[] { all[matIdx] };
        }

        /// <summary>
        /// Called by EnigmaFader when a dynamic fader's value changes.
        /// Stores the value in the per-link array so it can be restored
        /// when the fader is rebound to a different slot.
        /// </summary>
        public void OnFaderLinkValueChanged(int linkIndex, float value)
        {
            if (faderLinkCurrentValues != null && linkIndex >= 0
                && linkIndex < faderLinkCurrentValues.Length)
                faderLinkCurrentValues[linkIndex] = value;
        }

        // ------------------------------------------------------------------------
        //  FADER MODE TOGGLE
        // ------------------------------------------------------------------------

        public void ToggleFaderMode()
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            faderMode = faderMode == 0 ? 1 : 0;
            if (faderSlots != null)
            {
                for (int i = 0; i < faderSlots.Length; i++)
                {
                    if (faderSlots[i] != null)
                        faderSlots[i].SetFaderMode(faderMode);
                }
            }
            // Hand collider tracking only runs in hand-collider mode (0) for authorized VR players
            _handColliderTrackingEnabled = faderMode == 0 && _isLocalPlayerVR && CanLocalUserInteract();
            DeferredRequestSerialization();
        }

        // ------------------------------------------------------------------------
        //  FADER PAGE NAVIGATION
        // ------------------------------------------------------------------------

        public int GetFaderPageCount()
        {
            int slotsPerPage = faderSlots != null ? faderSlots.Length : 0;
            if (slotsPerPage <= 0) return 1;

            // Count always-visible (pinned) static faders — these consume slots on every page.
            int pinnedCount = 0;
            for (int s = 0; s < staticFaderCount; s++)
            {
                if (rtStaticFaderAlwaysVisible != null
                    && s < rtStaticFaderAlwaysVisible.Length
                    && rtStaticFaderAlwaysVisible[s])
                    pinnedCount++;
            }

            int pageableSlots = slotsPerPage - pinnedCount;
            if (pageableSlots <= 0) return 1;

            // Count non-pinned bindings: non-pinned static faders + active dynamic links.
            int pageableBindings = staticFaderCount - pinnedCount;
            if (rtFaderLinkEntryIndex != null && entryStates != null)
            {
                for (int l = 0; l < rtFaderLinkEntryIndex.Length; l++)
                {
                    int eIdx = rtFaderLinkEntryIndex[l];
                    if (eIdx < entryStates.Length && entryStates[eIdx])
                        pageableBindings++;
                }
            }

            return Mathf.Max(1, Mathf.CeilToInt((float)pageableBindings / pageableSlots));
        }

        public void CycleFaderPage(int direction)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            int total = GetFaderPageCount();
            if (total <= 1) return;
            currentFaderPage = (currentFaderPage + direction + total) % total;
            UpdateFaderBindings();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void GoToFaderPage(int pageIndex)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            int total = GetFaderPageCount();
            if (total <= 0) return;
            currentFaderPage = Mathf.Clamp(pageIndex, 0, total - 1);
            UpdateFaderBindings();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // ------------------------------------------------------------------------
        //  CONTROLLER-MANAGED FADER COORDINATION
        //  InputGrab on the controller fires for ALL UdonSharpBehaviours on the
        //  same GameObject. We track grab state here and drive the active fader
        //  from Update() rather than running N per-fader update loops.
        // ------------------------------------------------------------------------

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
            {
                _ctrlLeftGrabbed = value;
                if (!value && _activeLeftFader != null)
                {
                    _activeLeftFader.SyncValue();
                    _activeLeftFader = null;
                }
            }
            else if (args.handType == HandType.RIGHT)
            {
                _ctrlRightGrabbed = value;
                if (!value && _activeRightFader != null)
                {
                    _activeRightFader.SyncValue();
                    _activeRightFader = null;
                }
            }
        }

        /// <summary>
        /// Called by an EnigmaFader when a hand-tracking collider enters its trigger.
        /// Uses distance-based selection to pick the closest fader when multiple overlap,
        /// checks whitelist authorization, and enforces per-hand grab locking.
        /// </summary>
        public void OnFaderTriggerEnter(EnigmaFader fader, bool isRight)
        {
            // Whitelist gate — unauthorized players cannot interact with faders
            if (!CanLocalUserInteract()) return;

            // Mark the fader as being in the trigger zone for this hand
            fader.SetInTrigger(isRight, true);

            // Find the closest fader among all that are currently in this hand's trigger
            FindAndAssignClosestFader(isRight);
        }

        /// <summary>
        /// Called by an EnigmaFader when the hand-tracking collider exits its trigger.
        /// Clears trigger state and re-evaluates closest fader for the hand.
        /// </summary>
        public void OnFaderTriggerExit(EnigmaFader fader, bool isRight)
        {
            fader.SetInTrigger(isRight, false);

            // If the exiting fader was the active one, clear it and re-evaluate
            if (isRight && _activeRightFader == fader)
            {
                _activeRightFader = null;
                _rightGrabbedFaderIndex = -1;
            }
            else if (!isRight && _activeLeftFader == fader)
            {
                _activeLeftFader = null;
                _leftGrabbedFaderIndex = -1;
            }

            FindAndAssignClosestFader(isRight);
        }

        /// <summary>
        /// Finds the closest fader that is in the trigger zone for the given hand
        /// and assigns it as the active fader, enforcing per-hand grab locking.
        /// </summary>
        private void FindAndAssignClosestFader(bool isRight)
        {
            if (faderSlots == null) return;

            GameObject handObj = isRight ? sharedRightHandCollider : sharedLeftHandCollider;
            if (handObj == null) return;
            Vector3 handPos = handObj.transform.position;

            int closestIndex = -1;
            float closestDist = float.MaxValue;

            for (int i = 0; i < faderSlots.Length; i++)
            {
                EnigmaFader f = faderSlots[i];
                if (f == null) continue;
                if (!f.IsInTrigger(isRight)) continue;

                // Don't allow this hand to grab a fader already grabbed by the other hand
                if (isRight && _leftGrabbedFaderIndex == i) continue;
                if (!isRight && _rightGrabbedFaderIndex == i) continue;

                float dist = Vector3.Distance(handPos, f.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                if (isRight)
                {
                    _activeRightFader = faderSlots[closestIndex];
                    _rightGrabbedFaderIndex = closestIndex;
                }
                else
                {
                    _activeLeftFader = faderSlots[closestIndex];
                    _leftGrabbedFaderIndex = closestIndex;
                }
            }
        }

        /// <summary>
        /// Initializes hand collider tracking state. Called once from Start().
        /// Enables tracking only for VR players in hand-collider mode who pass the whitelist.
        /// </summary>
        private void InitializeHandColliderTracking()
        {
            _localPlayer = Networking.LocalPlayer;
            _isLocalPlayerVR = _localPlayer != null && _localPlayer.IsUserInVR();
            _handColliderTrackingEnabled = faderMode == 0 && _isLocalPlayerVR && CanLocalUserInteract();
        }

        /// <summary>
        /// Moves the shared hand collider objects to follow the local VR player's
        /// index finger tip each frame. Skipped for desktop players and when in
        /// pickup mode or when the player is not authorized.
        /// </summary>
        private void UpdateHandColliderPositions()
        {
            if (!_handColliderTrackingEnabled) return;
            if (_localPlayer == null) return;

            if (sharedRightHandCollider != null)
            {
                Vector3 rightPos = _localPlayer.GetBonePosition(HumanBodyBones.RightIndexDistal);
                if (rightPos.sqrMagnitude > 0.001f)
                    sharedRightHandCollider.transform.position = rightPos;
            }

            if (sharedLeftHandCollider != null)
            {
                Vector3 leftPos = _localPlayer.GetBonePosition(HumanBodyBones.LeftIndexDistal);
                if (leftPos.sqrMagnitude > 0.001f)
                    sharedLeftHandCollider.transform.position = leftPos;
            }
        }

        /// <summary>
        /// Per-frame position drive for controller-managed faders.
        /// Called at the bottom of Update(). Only runs when a fader is grabbed.
        /// </summary>
        private void UpdateControlledFaderPositions()
        {
            if (_ctrlRightGrabbed && _activeRightFader != null && sharedRightHandCollider != null)
                _activeRightFader.UpdateFromController(sharedRightHandCollider.transform);

            if (_ctrlLeftGrabbed && _activeLeftFader != null && sharedLeftHandCollider != null)
                _activeLeftFader.UpdateFromController(sharedLeftHandCollider.transform);
        }
    }
}


using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  DISPLAY
        // ------------------------------------------------------------------------

        public void UpdateDisplay()
        {
            var exe = executor;
            if (exe == null) return;
            if (buttonSlots == null || buttonSlots.Length == 0) return;
            if (rtFolderNames == null || rtFolderNames.Length == 0) return;

            int itemsPerPage = GetItemsPerPage();
            int startIdx = rtFolderEntryStart[currentFolderIndex];
            int count    = rtFolderEntryCount[currentFolderIndex];
            int pageOffset = currentPageIndex * itemsPerPage;

            for (int slot = 0; slot < buttonSlots.Length; slot++)
            {
                int localIdx = pageOffset + slot;
                bool hasEntry = localIdx < count;

                if (!hasEntry || buttonSlots[slot] == null)
                {
                    if (buttonSlots[slot] != null)
                        buttonSlots[slot].UpdateVisual("", inactiveColor, false);
                    continue;
                }

                int entryIdx = startIdx + localIdx;
                string label     = rtEntryLabels[entryIdx];

                // Empty placeholder entries render as blank slots.
                if (string.IsNullOrEmpty(label))
                {
                    buttonSlots[slot].UpdateVisual("", inactiveColor, false);
                    continue;
                }

                bool   active    = entryStates != null && entryIdx < entryStates.Length && entryStates[entryIdx];
                int    btnType   = rtEntryButtonTypes[entryIdx];

                Color color = active ? activeColor : inactiveColor;

                // Per-entry custom color (static override when active)
                if (active && rtEntryUseCustomColor != null && entryIdx < rtEntryUseCustomColor.Length
                    && rtEntryUseCustomColor[entryIdx])
                {
                    color = rtEntryCustomColor[entryIdx];
                }

                // ColorCycle: show the current palette color
                if (btnType == 3)
                {
                    int palIdx   = colorPaletteCurrentIndices != null && entryIdx < colorPaletteCurrentIndices.Length
                                   ? colorPaletteCurrentIndices[entryIdx] : 0;
                    int palStart = rtColorPaletteStart != null && entryIdx < rtColorPaletteStart.Length
                                   ? rtColorPaletteStart[entryIdx] : -1;
                    if (palStart >= 0 && rtColorPaletteColors != null
                        && palStart + palIdx < rtColorPaletteColors.Length)
                    {
                        color = rtColorPaletteColors[palStart + palIdx];
                    }
                }

                // Color Selector (type 10): tint depends on role
                // Role 0 (Color Display) ? applied color from linked Set Color entry
                // Role 1 (Set Color)     ? pending/preview color from this entry's palette
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length
                    && exe.rtActionTypes != null && exe.rtActionColorSelectorRoles != null)
                {
                    int csActStart = rtEntryActionStart[entryIdx];
                    int csActCount = rtEntryActionCount[entryIdx];
                    for (int a = csActStart; a < csActStart + csActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 10) continue;

                        int role = a < exe.rtActionColorSelectorRoles.Length ? exe.rtActionColorSelectorRoles[a] : 0;
                        int linkedEntry = rtColorLinkedEntry != null && entryIdx < rtColorLinkedEntry.Length
                                          ? rtColorLinkedEntry[entryIdx] : -1;

                        if (role == 0) // Color Display: show applied color from the linked Set Color entry
                        {
                            int src = linkedEntry >= 0 ? linkedEntry : entryIdx;
                            int palStart = rtColorPaletteStart != null && src < rtColorPaletteStart.Length
                                           ? rtColorPaletteStart[src] : -1;
                            int palCount = rtColorPaletteCount != null && src < rtColorPaletteCount.Length
                                           ? rtColorPaletteCount[src] : 0;
                            if (palStart >= 0 && palCount > 0 && rtColorPaletteColors != null)
                            {
                                int idx = colorPaletteCurrentIndices != null && src < colorPaletteCurrentIndices.Length
                                          ? colorPaletteCurrentIndices[src] : 0;
                                if (palStart + idx < rtColorPaletteColors.Length)
                                    color = rtColorPaletteColors[palStart + idx];
                            }
                        }
                        else if (role == 1) // Set Color: show pending/preview color from this entry's palette
                        {
                            int palStart = rtColorPaletteStart != null && entryIdx < rtColorPaletteStart.Length
                                           ? rtColorPaletteStart[entryIdx] : -1;
                            int palCount = rtColorPaletteCount != null && entryIdx < rtColorPaletteCount.Length
                                           ? rtColorPaletteCount[entryIdx] : 0;
                            if (palStart >= 0 && palCount > 0 && rtColorPaletteColors != null)
                            {
                                int pending = colorPalettePendingIndices != null && entryIdx < colorPalettePendingIndices.Length
                                              ? colorPalettePendingIndices[entryIdx] : 0;
                                if (palStart + pending < rtColorPaletteColors.Length)
                                    color = rtColorPaletteColors[palStart + pending];
                            }
                        }
                        break; // only the first type-10 action determines the button tint
                    }
                }

                // Display Value action (type 9): read current value and append to label
                float displayFloat = 0f;
                bool  hasDisplayFloat = false;
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length
                    && exe.rtActionTypes != null)
                {
                    int dActStart = rtEntryActionStart[entryIdx];
                    int dActCount = rtEntryActionCount[entryIdx];
                    for (int a = dActStart; a < dActStart + dActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 9) continue;

                        // Shader property source
                        if (exe.rtActionTargetRenderers != null && a < exe.rtActionTargetRenderers.Length
                            && exe.rtActionTargetRenderers[a] != null
                            && exe.rtActionPropertyNames != null && a < exe.rtActionPropertyNames.Length
                            && !string.IsNullOrEmpty(exe.rtActionPropertyNames[a]))
                        {
                            Material[] dvMats = exe.rtActionTargetRenderers[a].sharedMaterials;
                            int dvMatIdx = exe.rtActionMaterialIndices != null && a < exe.rtActionMaterialIndices.Length
                                           ? exe.rtActionMaterialIndices[a] : 0;
                            Material mat = (dvMats != null && dvMatIdx >= 0 && dvMatIdx < dvMats.Length)
                                           ? dvMats[dvMatIdx] : null;
                            if (mat != null)
                            {
                                int propType = exe.rtActionPropertyTypes != null && a < exe.rtActionPropertyTypes.Length
                                               ? exe.rtActionPropertyTypes[a] : 0;
                                string valStr = "";
                                string propName = exe.rtActionPropertyNames[a];
                                if (propType == 0)      // Float
                                {
                                    float fv = mat.GetFloat(propName);
                                    valStr = fv.ToString("F2");
                                    displayFloat = fv;
                                    hasDisplayFloat = true;
                                }
                                // Color (1) and Vector (2) are not displayed — the text
                                // representation is too long for a button label.
                                if (!string.IsNullOrEmpty(valStr))
                                    label = label + "\n" + valStr;
                            }
                        }
                        // Udon variable source
                        else if (exe.rtActionUdonTargets != null && a < exe.rtActionUdonTargets.Length
                                 && exe.rtActionUdonTargets[a] != null)
                        {
                            // Variable name: prefer rtActionUdonVariableNames; fall back to rtActionPropertyNames
                            // (the latter is used when the action was configured with the unified Display Value UI).
                            string udonVarName = null;
                            if (exe.rtActionUdonVariableNames != null && a < exe.rtActionUdonVariableNames.Length
                                && !string.IsNullOrEmpty(exe.rtActionUdonVariableNames[a]))
                                udonVarName = exe.rtActionUdonVariableNames[a];
                            else if (exe.rtActionPropertyNames != null && a < exe.rtActionPropertyNames.Length
                                     && !string.IsNullOrEmpty(exe.rtActionPropertyNames[a]))
                                udonVarName = exe.rtActionPropertyNames[a];

                            if (!string.IsNullOrEmpty(udonVarName))
                            {
                                // Map propertyType (unified enum) to the expected runtime type:
                                // 0=Float, 1=Bool, 2=Int, 3=String, 4=Color, 5=Vector
                                int varType = exe.rtActionPropertyTypes != null && a < exe.rtActionPropertyTypes.Length
                                              ? exe.rtActionPropertyTypes[a]
                                              : (exe.rtActionUdonVariableTypes != null && a < exe.rtActionUdonVariableTypes.Length
                                                 ? exe.rtActionUdonVariableTypes[a] : 0);
                                object rawVal = exe.rtActionUdonTargets[a].GetProgramVariable(udonVarName);
                                if (rawVal != null)
                                {
                                    string valStr = "";
                                    if (varType == 0)
                                    {
                                        float fv = (float)rawVal;
                                        valStr = fv.ToString("F2");
                                        displayFloat = fv;
                                        hasDisplayFloat = true;
                                    }
                                    else if (varType == 1) valStr = ((bool)rawVal) ? "true" : "false";
                                    else if (varType == 2)
                                    {
                                        int iv = (int)rawVal;
                                        valStr = iv.ToString();
                                        displayFloat = iv;
                                        hasDisplayFloat = true;
                                    }
                                    else if (varType == 3) valStr = (string)rawVal;
                                    // Color (4) and Vector (5) are not displayed — the text
                                    // representation is too long for a button label.
                                    if (!string.IsNullOrEmpty(valStr))
                                        label = label + "\n" + valStr;
                                }
                            }
                        }
                        break; // only one display action per entry
                    }
                }

                // Conditional Coloring: read value from entry's own source and evaluate rules.
                if (rtEntryCondColorStart != null && entryIdx < rtEntryCondColorStart.Length
                    && rtEntryCondColorCount != null && entryIdx < rtEntryCondColorCount.Length
                    && rtEntryCondColorCount[entryIdx] > 0)
                {
                    float condFloat = 0f;
                    bool hasCondFloat = false;

                    // Read from display action source (backward compat)
                    if (hasDisplayFloat)
                    {
                        condFloat = displayFloat;
                        hasCondFloat = true;
                    }

                    // Read from entry's own conditional color source
                    if (!hasCondFloat && rtEntryCondColorSourceType != null && entryIdx < rtEntryCondColorSourceType.Length)
                    {
                        int srcType = rtEntryCondColorSourceType[entryIdx];
                        if (srcType == 0) // Material
                        {
                            Renderer ccRend = rtEntryCondColorRenderers != null && entryIdx < rtEntryCondColorRenderers.Length
                                ? rtEntryCondColorRenderers[entryIdx] : null;
                            int ccMatIdx = rtEntryCondColorMatIndices != null && entryIdx < rtEntryCondColorMatIndices.Length
                                ? rtEntryCondColorMatIndices[entryIdx] : 0;
                            string ccProp = rtEntryCondColorPropertyNames != null && entryIdx < rtEntryCondColorPropertyNames.Length
                                ? rtEntryCondColorPropertyNames[entryIdx] : null;
                            if (ccRend != null && !string.IsNullOrEmpty(ccProp))
                            {
                                var ccMats = ccRend.sharedMaterials;
                                if (ccMats != null && ccMatIdx >= 0 && ccMatIdx < ccMats.Length && ccMats[ccMatIdx] != null
                                    && ccMats[ccMatIdx].HasProperty(ccProp))
                                {
                                    condFloat = ccMats[ccMatIdx].GetFloat(ccProp);
                                    hasCondFloat = true;
                                }
                            }
                        }
                        else if (srcType == 1) // Udon
                        {
                            UdonSharp.UdonSharpBehaviour ccUdonProxy = rtEntryCondColorUdonTargets != null && entryIdx < rtEntryCondColorUdonTargets.Length
                                ? rtEntryCondColorUdonTargets[entryIdx] : null;
                            string ccVar = rtEntryCondColorUdonVarNames != null && entryIdx < rtEntryCondColorUdonVarNames.Length
                                ? rtEntryCondColorUdonVarNames[entryIdx] : null;
                            if (ccUdonProxy != null && !string.IsNullOrEmpty(ccVar))
                            {
                                var ccUdon = (VRC.Udon.UdonBehaviour)ccUdonProxy.GetComponent(typeof(VRC.Udon.UdonBehaviour));
                                if (ccUdon != null)
                                {
                                    object rawVal = ccUdon.GetProgramVariable(ccVar);
                                    if (rawVal != null)
                                    {
                                        System.Type t = rawVal.GetType();
                                        if (t == typeof(float)) { condFloat = (float)rawVal; hasCondFloat = true; }
                                        else if (t == typeof(int)) { condFloat = (int)rawVal; hasCondFloat = true; }
                                    }
                                }
                            }
                        }
                    }

                    if (hasCondFloat)
                    {
                        int ccStart = rtEntryCondColorStart[entryIdx];
                        int ccCount = rtEntryCondColorCount[entryIdx];
                        for (int cc = ccStart; cc < ccStart + ccCount; cc++)
                        {
                            if (rtCondColorConditions == null || cc >= rtCondColorConditions.Length) break;
                            int   cond = rtCondColorConditions[cc];
                            float cval = rtCondColorValues[cc];
                            bool match = false;
                            if      (cond == 0) match = condFloat <  cval;  // Less
                            else if (cond == 1) match = condFloat >  cval;  // Greater
                            else if (cond == 2)                              // Equal (float tolerance)
                            {
                                float diff = condFloat - cval;
                                if (diff < 0f) diff = -diff;
                                match = diff < 0.0001f;
                            }
                            else if (cond == 3) match = condFloat <= cval;  // LessEqual
                            else if (cond == 4) match = condFloat >= cval;  // GreaterEqual
                            if (match)
                            {
                                color = rtCondColorColors[cc];
                                break;
                            }
                        }
                    }
                }

                // Variant Display (type 19, role 0): append current variant name on a second line
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length
                    && exe.rtActionTypes != null && exe.rtActionVariantSelectorRoles != null)
                {
                    int vdActStart = rtEntryActionStart[entryIdx];
                    int vdActCount = rtEntryActionCount[entryIdx];
                    for (int a = vdActStart; a < vdActStart + vdActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 19) continue;

                        int vdRole = a < exe.rtActionVariantSelectorRoles.Length ? exe.rtActionVariantSelectorRoles[a] : 0;
                        if (vdRole != 0) continue; // only role-0 (Variant Display) appends the name

                        int src = rtVariantLinkedEntry != null && entryIdx < rtVariantLinkedEntry.Length
                                  ? rtVariantLinkedEntry[entryIdx] : -1;
                        if (src < 0) break;

                        int itmStart = rtVariantItemStart != null && src < rtVariantItemStart.Length
                                       ? rtVariantItemStart[src] : -1;
                        int itmCount = rtVariantItemCount != null && src < rtVariantItemCount.Length
                                       ? rtVariantItemCount[src] : 0;
                        if (itmStart < 0 || itmCount <= 0) break;

                        int cur  = variantCurrentIndices != null && src < variantCurrentIndices.Length
                                   ? variantCurrentIndices[src] : 0;
                        int flat = itmStart + cur;
                        if (rtVariantItemNames != null && flat < rtVariantItemNames.Length
                            && !string.IsNullOrEmpty(rtVariantItemNames[flat]))
                            label = label + "\n" + rtVariantItemNames[flat];
                        break;
                    }
                }

                // Display Stat (type 21): replace the label with "<stat name>\n<value>".
                // The stat name is always shown; the value updates each frame.
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length && exe.rtActionTypes != null)
                {
                    int dsActStart = rtEntryActionStart[entryIdx];
                    int dsActCount = rtEntryActionCount[entryIdx];
                    for (int a = dsActStart; a < dsActStart + dsActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 21) continue;
                        int metric = exe.rtActionStatMetrics != null && a < exe.rtActionStatMetrics.Length
                                     ? exe.rtActionStatMetrics[a] : 0;
                        string statName  = GetStatDisplayName(metric);
                        string statValue = FormatStatValue(metric);
                        label = string.IsNullOrEmpty(statValue) ? statName : statName + "\n" + statValue;
                        break; // only one Display Stat per entry
                    }
                }

                // Display Folder Name (type 24): replace the label with the current folder's name.
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length && exe.rtActionTypes != null)
                {
                    int dfActStart = rtEntryActionStart[entryIdx];
                    int dfActCount = rtEntryActionCount[entryIdx];
                    for (int a = dfActStart; a < dfActStart + dfActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 24) continue;
                        int folderIdx = Mathf.Clamp(currentFolderIndex, 0,
                            rtFolderNames != null ? rtFolderNames.Length - 1 : 0);
                        label = rtFolderNames != null && rtFolderNames.Length > 0
                                ? rtFolderNames[folderIdx] : "";
                        break;
                    }
                }

                // Display Page Number (type 25): replace the label with "current / total" page indicator.
                if (rtEntryActionStart != null && entryIdx < rtEntryActionStart.Length && exe.rtActionTypes != null)
                {
                    int dpActStart = rtEntryActionStart[entryIdx];
                    int dpActCount = rtEntryActionCount[entryIdx];
                    for (int a = dpActStart; a < dpActStart + dpActCount; a++)
                    {
                        if (a >= exe.rtActionTypes.Length) break;
                        if (exe.rtActionTypes[a] != 25) continue;
                        int total = GetPageCount(currentFolderIndex);
                        label = (currentPageIndex + 1) + " / " + total;
                        break;
                    }
                }

                // Preset: badge for saved slots; active tint for Clear button in clear mode
                if (rtEntryIsPreset != null && entryIdx < rtEntryIsPreset.Length && rtEntryIsPreset[entryIdx])
                {
                    int presRole = rtPresetRoles != null && entryIdx < rtPresetRoles.Length
                                   ? rtPresetRoles[entryIdx] : 0;
                    if (presRole == 0)
                    {
                        // Preset Slot: badge if saved. Reads from the dedicated
                        // EnigmaPresetStorage behaviour; if no storage component
                        // exists (e.g. a Slot entry somehow survived without the
                        // auto-created storage component) the badge simply
                        // doesn't render.
                        int slotIdx = rtPresetSlotIndex != null && entryIdx < rtPresetSlotIndex.Length
                                      ? rtPresetSlotIndex[entryIdx] : -1;
                        if (slotIdx >= 0 && presetStorage != null
                            && presetStorage.presetIsSaved != null
                            && slotIdx < presetStorage.presetIsSaved.Length
                            && presetStorage.presetIsSaved[slotIdx])
                            label = label + " [P]";
                    }
                    else if (presRole == 3)
                    {
                        // Clear Button: highlight when clear mode is active
                        if (_presetClearModeActive) color = activeColor;
                    }
                }

                buttonSlots[slot].UpdateVisual(label, color, true);
            }

            UpdateFolderDisplay();
            UpdatePageDisplay();
            UpdateFaderBindings();
        }

        private void UpdateFolderDisplay()
        {
            if (folderNameText == null) return;
            if (rtFolderNames == null || rtFolderNames.Length == 0) return;
            int idx = Mathf.Clamp(currentFolderIndex, 0, rtFolderNames.Length - 1);
            folderNameText.text = rtFolderNames[idx];
        }

        private void UpdatePageDisplay()
        {
            if (pageIndicatorText == null) return;
            int total = GetPageCount(currentFolderIndex);
            pageIndicatorText.text = (currentPageIndex + 1) + " / " + total;
        }

        /// <summary>
        /// Applies the palette color at <paramref name="idx"/> to the target renderer
        /// without advancing the index.  Used by LoadPreset and RestoreWorldState.
        /// </summary>
        private void ApplyColorCycleAtIndex(int entryIdx, int idx)
        {
            int palStart = rtColorPaletteStart != null && entryIdx < rtColorPaletteStart.Length
                           ? rtColorPaletteStart[entryIdx] : -1;
            int palCount = rtColorPaletteCount != null && entryIdx < rtColorPaletteCount.Length
                           ? rtColorPaletteCount[entryIdx] : 0;
            if (palStart < 0 || palCount <= 0) return;

            idx = idx % palCount;
            if (rtColorPaletteColors == null || palStart + idx >= rtColorPaletteColors.Length) return;
            Color selectedColor = rtColorPaletteColors[palStart + idx];

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

        public void SyncUpdateDisplay()
        {
            Log("SyncUpdateDisplay() received network event");
            UpdateDisplay();
        }
    }
}

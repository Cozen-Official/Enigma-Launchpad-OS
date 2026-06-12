#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Build pipeline: reads the structured EnigmaFolderData[] and writes all
    /// rt* flat arrays onto the EnigmaController component so Udon can consume
    /// them at runtime.
    ///
    /// Run this before every world upload (the "Build Runtime Arrays" button
    /// in the inspector triggers this).
    /// </summary>
    public partial class EnigmaControllerEditor
    {
        internal static void BuildRuntimeArrays(SerializedObject so, EnigmaController ctrl)
        {
            EnigmaFolderData[] folders = ctrl.GetFolders() ?? new EnigmaFolderData[0];

            // ── Pass 1: Count totals ──
            int totalEntries       = 0;
            int totalActions       = 0;
            int totalFaderLinks    = 0;
            int totalPaletteColors = 0;
            int totalPresetFolders = 0;
            int totalVariantItems  = 0;
            int totalCondColorRules = 0;

            foreach (var folder in folders)
            {
                foreach (var entry in folder.entries)
                {
                    totalEntries += 1;
                    if (entry.isEmpty) continue;    // empty slots are counted but have no actions
                    totalActions += entry.actions.Length;

                    // Reserve one extra slot for each Set Shader Property action that
                    // will spawn a synthetic "set the section toggle" action at build
                    // time. Must use the same WouldEmitSyntheticToggle check as pass 2
                    // or the runtime arrays would be under-allocated.
                    foreach (var act in entry.actions)
                    {
                        if (WouldEmitSyntheticToggle(act, out _, out _))
                            totalActions += 1;
                    }

                    if (entry.assignFader)
                        totalFaderLinks += entry.faderLinks != null && entry.faderLinks.Length > 0
                            ? entry.faderLinks.Length
                            : (entry.faderLink != null ? 1 : 0);

                    // Color Cycle (type 7) and Color Selector Set Color (type 10 role 1):
                    // palette colors come from the first qualifying action.
                    foreach (var action in entry.actions)
                    {
                        if (action.actionType == 7 && action.paletteColors != null)
                        {
                            totalPaletteColors += action.paletteColors.Length;
                            break;
                        }
                        if (action.actionType == 10 && action.colorSelectorRole == 1
                            && action.paletteColors != null)
                        {
                            totalPaletteColors += action.paletteColors.Length;
                            break;
                        }
                    }

                    // Variant Selector Set Variant (type 19 role 1): variant items come from the first qualifying action.
                    foreach (var action in entry.actions)
                    {
                        if (action.actionType == 19 && action.variantSelectorRole == 1
                            && action.variantItems != null)
                        {
                            totalVariantItems += action.variantItems.Length;
                            break;
                        }
                    }

                    // Preset: folder list from the first type-8 action with scope==1.
                    foreach (var action in entry.actions)
                    {
                        if (action.actionType == 8 && action.presetScope == 1
                            && action.presetIncludedFolderIndices != null)
                        {
                            totalPresetFolders += action.presetIncludedFolderIndices.Length;
                            break;
                        }
                    }

                    // Conditional coloring rules: prefer entry-level, fall back to display action.
                    if (entry.useCustomColor && entry.useConditionalColor
                        && entry.condColorRules != null && entry.condColorRules.Length > 0)
                    {
                        totalCondColorRules += entry.condColorRules.Length;
                    }
                    else
                    {
                        foreach (var action in entry.actions)
                        {
                            if (action.actionType == 9 && action.useConditionalColoring
                                && action.conditionalColorRules != null)
                            {
                                totalCondColorRules += action.conditionalColorRules.Length;
                                break;
                            }
                        }
                    }
                }
            }

            // ── Pre-Pass: Screen Shader expansion ──
            if (EditorApplication.isPlaying)
            {
                // In play mode, EditorOnly templates have been stripped. The pre-play
                // build already created the duplicates in ctrl.shaderInstances.
                // Re-map type 26 actions → type 0 using those existing instances.
                int siIdx = 0;
                foreach (var folder in folders)
                {
                    foreach (var entry in folder.entries)
                    {
                        if (entry.isEmpty || entry.actions == null) continue;

                        foreach (var action in entry.actions)
                        {
                            if (action.actionType != 26) continue;

                            if (ctrl.shaderInstances != null && siIdx < ctrl.shaderInstances.Length
                                && ctrl.shaderInstances[siIdx] != null)
                            {
                                action.actionType   = 0;
                                action.targetObject = ctrl.shaderInstances[siIdx];
                                siIdx++;
                            }
                            else
                            {
                                Debug.LogWarning($"[EnigmaController] Screen Shader on \"{entry.label}\": " +
                                    "no matching shader instance from pre-play build. Skipping.");
                            }
                        }
                    }
                }
            }
            else
            {
                // Clean up previously-created shader instance GOs.
                if (ctrl.shaderInstances != null)
                {
                    foreach (var go in ctrl.shaderInstances)
                        if (go != null) DestroyImmediate(go);
                }

                // Find all shader templates in the scene.
                var shaderTemplates = Object.FindObjectsOfType<EnigmaShaderTemplate>();
                var shaderTemplateMap = new Dictionary<int, EnigmaShaderTemplate>();
                foreach (var t in shaderTemplates)
                    shaderTemplateMap[t.templateNumber] = t;

                var createdShaderInstances = new List<GameObject>();

                // For each Screen Shader action (type 26): duplicate the template GO,
                // assign the material, and rewrite to Toggle Object (type 0).
                foreach (var folder in folders)
                {
                    foreach (var entry in folder.entries)
                    {
                        if (entry.isEmpty || entry.actions == null) continue;

                        foreach (var action in entry.actions)
                        {
                            if (action.actionType != 26) continue;

                            if (!shaderTemplateMap.TryGetValue(action.shaderTemplateIndex, out var tmpl))
                            {
                                Debug.LogWarning($"[EnigmaController] Screen Shader on \"{entry.label}\": " +
                                    $"template {action.shaderTemplateIndex} not found in scene. Skipping.");
                                continue;
                            }

                            if (action.targetMaterial == null)
                            {
                                Debug.LogWarning($"[EnigmaController] Screen Shader on \"{entry.label}\": " +
                                    "no material assigned. Skipping.");
                                continue;
                            }

                            // Duplicate the template GO.
                            var duplicate = Object.Instantiate(tmpl.gameObject);
                            duplicate.name = $"{entry.label} ({action.targetMaterial.name})";
                            duplicate.tag = "Untagged"; // Remove EditorOnly tag.

                            // Remove the marker component from the duplicate.
                            var dupTemplate = duplicate.GetComponent<EnigmaShaderTemplate>();
                            if (dupTemplate != null) DestroyImmediate(dupTemplate);

                            // Parent as sibling of template, same transform.
                            var parentTransform = tmpl.transform.parent;
                            duplicate.transform.SetParent(parentTransform, true);
                            duplicate.transform.localPosition = tmpl.transform.localPosition;
                            duplicate.transform.localRotation = tmpl.transform.localRotation;
                            duplicate.transform.localScale    = tmpl.transform.localScale;

                            // Assign the material.
                            var renderer = duplicate.GetComponent<MeshRenderer>();
                            if (renderer != null)
                                renderer.sharedMaterial = action.targetMaterial;

                            // Default to inactive (toggled on by button press).
                            duplicate.SetActive(false);

                            // Rewrite action to Toggle Object (type 0) targeting the duplicate.
                            action.actionType   = 0;
                            action.targetObject = duplicate;

                            createdShaderInstances.Add(duplicate);
                        }
                    }
                }

                ctrl.shaderInstances = createdShaderInstances.ToArray();
            }

            // ── Pass 2: Allocate arrays ──
            var rtFolderNames       = new string[folders.Length];
            var rtFolderEntryStart  = new int[folders.Length];
            var rtFolderEntryCount  = new int[folders.Length];

            var rtEntryLabels              = new string[totalEntries];
            var rtEntryButtonTypes         = new int[totalEntries];
            var rtEntryIsStateful          = new bool[totalEntries];
            var rtEntryDefaultOn           = new bool[totalEntries];
            var rtEntryExclusiveGroup      = new int[totalEntries];
            var rtEntryExclusiveGroupNames = new string[totalEntries];
            var rtEntryExclusiveOff        = new bool[totalEntries];
            var rtEntryAutoChangeGroupId   = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtEntryAutoChangeGroupId[i] = -1;
            // Per-entry expire (auto-deactivate after N seconds). 0 = no expire.
            var rtEntryExpireSeconds       = new float[totalEntries];
            var rtEntryActionStart         = new int[totalEntries];
            var rtEntryActionCount         = new int[totalEntries];
            var rtEntryIsPreset            = new bool[totalEntries];

            var rtActionTypes            = new int[totalActions];
            var rtActionTargetObjects    = new GameObject[totalActions];
            var rtActionTargetRenderers  = new Renderer[totalActions];
            var rtActionMaterialIndices  = new int[totalActions];
            var rtActionMaterials        = new Material[totalActions];
            // Toggle Material default-state material per action. Mirrored from
            // EnigmaActionData.defaultMaterial; consumed by the runtime type==1
            // branch on deactivate.
            var rtActionDefaultMaterials = new Material[totalActions];
            var rtActionPropertyNames    = new string[totalActions];
            var rtActionFloatValues      = new float[totalActions];
            var rtActionColorValues      = new Color[totalActions];
            var rtActionVectorValues     = new Vector4[totalActions];
            var rtActionTextures         = new Texture[totalActions];
            var rtActionDefaultFloatValues  = new float[totalActions];
            var rtActionDefaultColorValues  = new Color[totalActions];
            for (int dc = 0; dc < totalActions; dc++) rtActionDefaultColorValues[dc] = Color.white;
            var rtActionDefaultVectorValues = new Vector4[totalActions];
            var rtActionPropertyTypes    = new int[totalActions];
            var rtActionUdonTargets              = new UdonSharp.UdonSharpBehaviour[totalActions];
            var rtActionUdonEventNames           = new string[totalActions];
            var rtActionUdonVariableNames        = new string[totalActions];
            var rtActionUdonVariableTypes        = new int[totalActions];
            var rtActionUdonVariableStringValues = new string[totalActions];
            var rtActionDelaySeconds             = new float[totalActions];
            var rtActionDelayOnDeactivate        = new bool[totalActions];
            var rtActionUdonEventScopes          = new int[totalActions];
            var rtActionTransformSpaces          = new int[totalActions];
            var rtActionTeleportRotations        = new Vector3[totalActions];
            var rtActionTeleportDestinations     = new GameObject[totalActions];

            // Per-action stat metric for type 21 (Display Stat).
            var rtActionStatMetrics = new int[totalActions];

            // Per-action auto-detected shader_feature_local keywords (for type 2 actions).
            var rtActionKeywords       = new string[totalActions];
            var rtActionKeywordToggles = new string[totalActions];
            var rtActionIsKeywordToggle = new bool[totalActions];
            var rtActionNonStateful    = new bool[totalActions];
            var rtActionUseStep        = new bool[totalActions];
            var rtActionAlwaysGate     = new int[totalActions];
            for (int i = 0; i < totalActions; i++) { rtActionKeywords[i] = ""; rtActionKeywordToggles[i] = ""; rtActionAlwaysGate[i] = -1; }

            // Per-action conditional execution.
            var rtActionHasCondition           = new bool[totalActions];
            var rtActionConditionEntryIndex    = new int[totalActions];
            var rtActionConditionRequireActive = new bool[totalActions];
            for (int i = 0; i < totalActions; i++) rtActionConditionEntryIndex[i] = -1;

            // Per-action autochange group settings (type 14).
            var rtActionAutoChangeGroupIds   = new int[totalActions];
            var rtActionAutoChangeIntervals  = new float[totalActions];
            var rtAutoChangeGroupRandom      = new bool[totalActions];
            for (int i = 0; i < totalActions; i++)
            {
                rtActionAutoChangeGroupIds[i]  = -1;
                rtActionAutoChangeIntervals[i] = 10f;
            }

            var rtFaderLinkEntryIndex           = new int[totalFaderLinks];
            var rtFaderLinkNames                = new string[totalFaderLinks];
            var rtFaderLinkRenderers            = new Renderer[totalFaderLinks];
            var rtFaderLinkMaterialIndices      = new int[totalFaderLinks];
            var rtFaderLinkPropertyNames        = new string[totalFaderLinks];
            var rtFaderLinkPropertyTypes        = new int[totalFaderLinks];
            var rtFaderLinkMinValues            = new float[totalFaderLinks];
            var rtFaderLinkMaxValues            = new float[totalFaderLinks];
            var rtFaderLinkDefaultValues        = new float[totalFaderLinks];
            var rtFaderLinkDefaultColors        = new Color[totalFaderLinks];
            var rtFaderLinkIndicatorEnabled     = new bool[totalFaderLinks];
            var rtFaderLinkIndicatorColors      = new Color[totalFaderLinks];
            var rtFaderLinkIndicatorConditional = new bool[totalFaderLinks];
            var rtFaderLinkTargetsSlider        = new bool[totalFaderLinks];
            var rtFaderLinkSliders              = new Object[totalFaderLinks]; // Component or Slider
            var rtFaderLinkSliderReversed       = new bool[totalFaderLinks];
            var rtFaderLinkTargetsSkybox        = new bool[totalFaderLinks];
            var rtFaderLinkTargetsUdon          = new bool[totalFaderLinks];
            var rtFaderLinkUdonBehaviours       = new UdonSharpBehaviour[totalFaderLinks];
            var rtFaderLinkUdonVariableNames    = new string[totalFaderLinks];
            // Managed-write metadata (Int-declared mirror / Always gate / keyword).
            var rtFaderLinkPropertyIsInt        = new bool[totalFaderLinks];
            var rtFaderLinkAlwaysGate           = new int[totalFaderLinks];
            var rtFaderLinkKeywords             = new string[totalFaderLinks];
            for (int fl0 = 0; fl0 < totalFaderLinks; fl0++)
            { rtFaderLinkAlwaysGate[fl0] = -1; rtFaderLinkKeywords[fl0] = ""; }

            // Per-entry custom color.
            var rtEntryUseCustomColor          = new bool[totalEntries];
            var rtEntryCustomColor             = new Color[totalEntries];
            var rtEntryCondColorSourceType     = new int[totalEntries];
            var rtEntryCondColorRenderers      = new Renderer[totalEntries];
            var rtEntryCondColorMatIndices     = new int[totalEntries];
            var rtEntryCondColorPropertyNames  = new string[totalEntries];
            var rtEntryCondColorUdonTargets    = new UdonSharp.UdonSharpBehaviour[totalEntries];
            var rtEntryCondColorUdonVarNames   = new string[totalEntries];

            // Conditional coloring rules.
            var rtEntryCondColorStart = new int[totalEntries];
            var rtEntryCondColorCount = new int[totalEntries];
            var rtCondColorConditions = new int[totalCondColorRules];
            var rtCondColorValues     = new float[totalCondColorRules];
            var rtCondColorColors     = new Color[totalCondColorRules];
            int condColorIdx = 0;

            var rtColorPaletteStart           = new int[totalEntries];
            var rtColorPaletteCount           = new int[totalEntries];
            var rtColorPaletteColors          = new Color[totalPaletteColors];
            var rtColorPaletteRenderers       = new Renderer[totalEntries];
            var rtColorPaletteMaterialIndices = new int[totalEntries];
            var rtColorPalettePropertyNames   = new string[totalEntries];

            // Color Selector (type 10) — per-entry linked palette-owner index (-1 = no link).
            var rtColorLinkedEntry = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtColorLinkedEntry[i] = -1;
            // Per-action color selector role (0=Color Display, 1=Set Color, 2=Change Color).
            var rtActionColorSelectorRoles = new int[totalActions];

            // Variant Selector (type 19) — per-entry linked palette-owner index (-1 = no link).
            var rtVariantLinkedEntry = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtVariantLinkedEntry[i] = -1;
            // Per-action variant selector role (0=Variant Display, 1=Set Variant, 2=Change Variant).
            var rtActionVariantSelectorRoles = new int[totalActions];
            // Per-entry: start/count into flat variant items list (-1/0 when no items for this entry).
            var rtVariantItemStart = new int[totalEntries];
            var rtVariantItemCount = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtVariantItemStart[i] = -1;
            // Flat variant item arrays.
            var rtVariantItemNames        = new string[totalVariantItems];
            var rtVariantItemFloatValues  = new float[totalVariantItems];
            var rtVariantItemColorValues  = new Color[totalVariantItems];
            var rtVariantItemVectorValues = new Vector4[totalVariantItems];
            var rtVariantItemTextures     = new Texture[totalVariantItems];
            // Per-item keyword for float-mode items: enum-mode toggle
            // properties (Mochie _SST/_Zoom/…) gate a different keyword per
            // value, so the keyword is resolved per item value at build time.
            var rtVariantItemKeywords     = new string[totalVariantItems];
            for (int vi0 = 0; vi0 < totalVariantItems; vi0++) rtVariantItemKeywords[vi0] = "";

            var rtStepAmounts   = new float[totalEntries];
            var rtStepMinValues = new float[totalEntries];
            var rtStepMaxValues = new float[totalEntries];
            var rtStepWrap      = new bool[totalEntries];

            var rtPresetScopes               = new int[totalEntries];
            var rtPresetIncludedFolderStart  = new int[totalEntries];
            var rtPresetIncludedFolderCount  = new int[totalEntries];
            var rtPresetIncludedFolders      = new int[totalPresetFolders];
            var rtPresetIncludeFaders        = new bool[totalEntries];
            var rtPresetIncludeStepValues    = new bool[totalEntries];
            var rtPresetIncludeColorPalettes = new bool[totalEntries];
            var rtPresetIncludeVariantGroups = new bool[totalEntries];

            // Maps each entry index to its 0-based slot index among all preset buttons (-1 if not a preset).
            var rtPresetSlotIndex = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtPresetSlotIndex[i] = -1;

            // Maps each entry index to its preset button role (-1 = not a preset,
            // 0 = Slot, 1 = Save, 2 = Load, 3 = Clear).
            var rtPresetRoles = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtPresetRoles[i] = -1;

            // ── Pass 3: Build exclusive group name → ID mapping ──
            var groupMap = new Dictionary<string, int>();
            int nextGroupId = 0;

            // Pre-scan all entries to assign group IDs for every individual comma-separated tag,
            // so the flat arrays can be built in one pass without dynamic resizing surprises.
            foreach (var folder in folders)
            {
                foreach (var entry in folder.entries)
                {
                    if (entry.isEmpty || !entry.useExclusiveGroup
                        || string.IsNullOrEmpty(entry.exclusiveGroup)) continue;
                    foreach (string rawTag in entry.exclusiveGroup.Split(','))
                    {
                        string tag = rawTag.Trim();
                        if (!string.IsNullOrEmpty(tag) && !groupMap.ContainsKey(tag))
                            groupMap[tag] = nextGroupId++;
                    }
                }
            }

            // rtGroupTagNames: indexed by group ID → tag name string (used at runtime).
            var rtGroupTagNames = new string[nextGroupId];
            foreach (var kvp in groupMap)
                rtGroupTagNames[kvp.Value] = kvp.Key;

            // Flat multi-group membership arrays.
            var flatGroupIds   = new List<int>();
            var entryGroupStart = new int[totalEntries];
            var entryGroupCount = new int[totalEntries];

            // ── Pass 3.5: Build autochange group name → ID mapping ──
            var autoChangeGroupMap = new Dictionary<string, int>();
            int nextAutoChangeGroupId = 0;
            foreach (var acgFolder in folders)
            {
                if (acgFolder.entries == null) continue;
                foreach (var acgEntry in acgFolder.entries)
                {
                    if (acgEntry.isEmpty || !acgEntry.useAutoChangeGroup
                        || string.IsNullOrEmpty(acgEntry.autoChangeGroup)) continue;
                    string acgTag = acgEntry.autoChangeGroup.Trim();
                    if (!string.IsNullOrEmpty(acgTag) && !autoChangeGroupMap.ContainsKey(acgTag))
                        autoChangeGroupMap[acgTag] = nextAutoChangeGroupId++;
                }
            }
            var rtAutoChangeGroupTagNames = new string[nextAutoChangeGroupId];
            foreach (var kvp in autoChangeGroupMap)
                rtAutoChangeGroupTagNames[kvp.Value] = kvp.Key;

            // ── Pass 3.6 removed ── (variant group links resolved in Pass 4.5 like color selector)

            // ── Pre-Pass 4: Build (folderIdx, localEntryIdx) → globalEntryIdx lookup ──
            // Used to resolve action conditions: conditionFolderIndex + conditionEntryIndex
            // → the global entry index stored in rtActionConditionEntryIndex[].
            var globalEntryLookup = new int[folders.Length][];
            {
                int gIdx = 0;
                for (int folderIndex = 0; folderIndex < folders.Length; folderIndex++)
                {
                    var fol = folders[folderIndex];
                    globalEntryLookup[folderIndex] = new int[fol.entries != null ? fol.entries.Length : 0];
                    if (fol.entries == null) continue;
                    for (int entryIndex = 0; entryIndex < fol.entries.Length; entryIndex++)
                        globalEntryLookup[folderIndex][entryIndex] = fol.entries[entryIndex].isEmpty ? -1 : gIdx++;
                }
            }

            // ── Pre-Pass: shader-specific material fixups (scoped) ──
            // Collect every material referenced by a type-2 / variant-selector
            // action together with the properties Enigma manages on it, then
            // apply the Mochie baseline fixups ONCE per material, scoped to
            // those managed toggles. Previously ApplyMaterialFixups ran
            // per-action and zeroed EVERY Mochie master toggle — silently
            // killing effects the user had enabled manually on the material
            // and never put under Enigma control.
            {
                var fixupProps = new Dictionary<Material, HashSet<string>>();
                foreach (var fpFolder in folders)
                {
                    if (fpFolder.entries == null) continue;
                    foreach (var fpEntry in fpFolder.entries)
                    {
                        if (fpEntry.isEmpty || fpEntry.actions == null) continue;
                        foreach (var fpAct in fpEntry.actions)
                        {
                            if (fpAct == null
                                || (fpAct.actionType != 2
                                    && !(fpAct.actionType == 19 && fpAct.variantSelectorRole == 1))
                                || fpAct.targetRenderer == null
                                || string.IsNullOrEmpty(fpAct.propertyName)) continue;
                            var fpMats = fpAct.targetRenderer.sharedMaterials;
                            int fpMi = fpAct.materialIndex;
                            if (fpMats == null || fpMi < 0 || fpMi >= fpMats.Length || fpMats[fpMi] == null) continue;
                            HashSet<string> fpSet;
                            if (!fixupProps.TryGetValue(fpMats[fpMi], out fpSet))
                            {
                                fpSet = new HashSet<string>();
                                fixupProps[fpMats[fpMi]] = fpSet;
                            }
                            fpSet.Add(fpAct.propertyName);
                            if (WouldEmitSyntheticToggle(fpAct, out string fpTog, out _))
                                fpSet.Add(fpTog);
                        }
                    }
                }
                foreach (var fpKv in fixupProps)
                    EnigmaShaderHelper.ApplyMaterialFixups(fpKv.Key,
                        EnigmaShaderHelper.ComputeManagedToggles(fpKv.Key, fpKv.Value));
            }

            // ── Pass 4: Populate ──
            int entryIdx        = 0;
            int actionIdx       = 0;
            int faderLinkIdx    = 0;
            int paletteColorIdx = 0;
            int variantItemIdx  = 0;
            int presetFolderIdx = 0;
            int presetSlotCounter = 0;

            // Maps each compiled entryIdx → its original EnigmaEntryData.
            // Used in Pass 4.5 to resolve Color Selector group links in O(n).
            var entryDataMap = new EnigmaEntryData[totalEntries];

            for (int f = 0; f < folders.Length; f++)
            {
                var folder = folders[f];
                rtFolderNames[f]      = folder.name;
                rtFolderEntryStart[f] = entryIdx;

                foreach (var entry in folder.entries)
                {
                    if (entry.isEmpty)
                    {
                        // Bake a placeholder for empty slots so page layout is preserved.
                        rtEntryLabels[entryIdx]    = "";
                        rtEntryDefaultOn[entryIdx] = false;
                        rtEntryButtonTypes[entryIdx] = 4; // DisplayOnly (inert)
                        rtEntryActionStart[entryIdx] = actionIdx;
                        rtEntryActionCount[entryIdx] = 0;
                        entryIdx++;
                        continue;
                    }
                    rtEntryLabels[entryIdx]    = entry.label;
                    rtEntryDefaultOn[entryIdx] = entry.onByDefault;
                    entryDataMap[entryIdx]     = entry;

                    // ── Derive runtime button type from assigned actions ──
                    // Runtime buttonType: 0=Toggle, 1=Momentary, 2=Step, 3=ColorCycle, 4=DisplayOnly
                    // Priority: Step > ColorCycle > Toggle > Momentary > DisplayOnly
                    // Type 10 (Color Selector) roles 1/2 are Momentary (not Toggle); role 0 is display-only.
                    bool hasStep = false, hasColorCycle = false, hasPreset = false;
                    bool hasToggleAction = false;  // any stateful (toggle) action type (not type 5, 9, or 10)
                    foreach (var act in entry.actions)
                    {
                        // Step only applies to Set-category actions (category == 1). Toggle-
                        // category (category == 0) actions ignore useStep: they're plain toggles
                        // that write propertyFloatValue on activate and defaultFloatValue on
                        // deactivate, no cycling. Legacy data with Toggle+useStep=true lingers
                        // harmlessly — the flag is vestigial on Toggle actions.
                        if (!hasStep       && act.useStep && act.category == 1
                                           && (act.actionType == 2 || act.actionType == 6)) hasStep = true;
                        if (!hasColorCycle && act.actionType == 7) hasColorCycle = true;
                        if (!hasPreset     && act.actionType == 8) hasPreset     = true;
                        if (IsStatefulAction(act.actionType, act.category))
                            hasToggleAction = true;
                    }

                    if (hasStep)
                    {
                        rtEntryButtonTypes[entryIdx] = 2; // Step
                        rtEntryIsStateful[entryIdx] = hasToggleAction; // Toggle+Step = stateful step
                    }
                    else if (hasColorCycle)
                        rtEntryButtonTypes[entryIdx] = 3;
                    else if (hasToggleAction)
                    {
                        rtEntryButtonTypes[entryIdx] = 0; // Toggle
                        rtEntryIsStateful[entryIdx] = true;
                    }
                    else if (entry.actions.Length > 0)
                    {
                        // If every action is purely display-only (types 9 or 21) treat the
                        // button as DisplayOnly (non-interactive) rather than Momentary.
                        bool allDisplay = true;
                        foreach (var act in entry.actions)
                        {
                            if (act.actionType != 9 && act.actionType != 21 && act.actionType != 24 && act.actionType != 25) { allDisplay = false; break; }
                        }
                        rtEntryButtonTypes[entryIdx] = allDisplay ? 4 : 1; // DisplayOnly or Momentary
                    }
                    else
                        rtEntryButtonTypes[entryIdx] = 4; // No actions → DisplayOnly
                    rtEntryIsPreset[entryIdx] = hasPreset;

                    // ── Exclusive groups (comma-separated tags; each tag → its own group ID) ──
                    entryGroupStart[entryIdx] = flatGroupIds.Count;
                    if (entry.useExclusiveGroup && !string.IsNullOrEmpty(entry.exclusiveGroup))
                    {
                        int firstGid = -1;
                        var seenIds  = new HashSet<int>();
                        foreach (string rawTag in entry.exclusiveGroup.Split(','))
                        {
                            string tag = rawTag.Trim();
                            if (string.IsNullOrEmpty(tag)) continue;
                            int gid = groupMap[tag];
                            if (seenIds.Add(gid))   // Add returns false when already present
                            {
                                flatGroupIds.Add(gid);
                                if (firstGid < 0) firstGid = gid;
                            }
                        }
                        rtEntryExclusiveGroup[entryIdx]      = firstGid;  // backward-compat: first tag
                        rtEntryExclusiveGroupNames[entryIdx] = entry.exclusiveGroup;
                    }
                    else
                    {
                        rtEntryExclusiveGroup[entryIdx]      = -1;
                        rtEntryExclusiveGroupNames[entryIdx] = "";
                    }
                    entryGroupCount[entryIdx] = flatGroupIds.Count - entryGroupStart[entryIdx];
                    rtEntryExclusiveOff[entryIdx] = entry.exclusiveOff;

                    // ── Autochange group ──
                    if (entry.useAutoChangeGroup && !string.IsNullOrEmpty(entry.autoChangeGroup))
                    {
                        string acgTag = entry.autoChangeGroup.Trim();
                        if (!string.IsNullOrEmpty(acgTag)
                            && autoChangeGroupMap.TryGetValue(acgTag, out int acgId))
                            rtEntryAutoChangeGroupId[entryIdx] = acgId;
                    }

                    // ── Expire (per-entry auto-deactivate) ──
                    rtEntryExpireSeconds[entryIdx] = entry.useExpire ? Mathf.Max(0f, entry.expireSeconds) : 0f;

                    // ── Actions ──
                    rtEntryActionStart[entryIdx] = actionIdx;
                    foreach (var act in entry.actions)
                    {
                        CompileAction(act, actionIdx,
                            rtActionTypes, rtActionTargetObjects, rtActionTargetRenderers,
                            rtActionMaterialIndices, rtActionMaterials, rtActionDefaultMaterials, rtActionPropertyNames,
                            rtActionFloatValues, rtActionColorValues, rtActionVectorValues,
                            rtActionTextures,
                            rtActionDefaultFloatValues, rtActionDefaultColorValues, rtActionDefaultVectorValues,
                            rtActionPropertyTypes, rtActionUdonTargets,
                            rtActionUdonEventNames, rtActionUdonVariableNames,
                            rtActionUdonVariableTypes, rtActionUdonVariableStringValues);
                        // Bake auto-detected shader keywords for type-2 actions.
                        // Only toggle the keyword when the action targets the toggle
                        // property itself (e.g., _SobelFilterToggle, _OutlineType),
                        // NOT when it targets an effect parameter (e.g., _OutlineThresh,
                        // _SobelFilterOpacity). Effect parameters should change values
                        // without toggling the feature on/off.
                        //
                        // Variant Selector role-1 actions (type 19) reuse the same
                        // renderer/property fields and need the Always-pass gate
                        // baked too (their keywords are baked PER ITEM below since
                        // each item value can gate a different keyword).
                        //
                        // (Material fixups moved to a once-per-material scoped
                        // pre-pass before this loop — see Pre-Pass: fixups.)
                        bool bakesShaderProp =
                            (act.actionType == 2
                             || (act.actionType == 19 && act.variantSelectorRole == 1))
                            && act.targetRenderer != null
                            && !string.IsNullOrEmpty(act.propertyName);
                        if (bakesShaderProp)
                        {
                            var mats = act.targetRenderer.sharedMaterials;
                            int mi = act.materialIndex;
                            if (mats != null && mi >= 0 && mi < mats.Length && mats[mi] != null)
                            {
                                if (act.actionType == 2)
                                {
                                    var kwInfo = EnigmaShaderHelper.GetPropertyKeywordInfo(mats[mi], act.propertyName);
                                    if (kwInfo.keyword != null && kwInfo.toggleProp == act.propertyName)
                                    {
                                        // Value-aware: enum-mode toggles (Mochie
                                        // _SST/_Zoom/_BlurModel/…) gate a DIFFERENT
                                        // keyword per value. Resolve against this
                                        // action's target value; fall back to the
                                        // single auto-detected keyword.
                                        string vkw = EnigmaShaderHelper.GetKeywordForToggleValue(
                                            mats[mi], act.propertyName, act.propertyFloatValue);
                                        rtActionKeywords[actionIdx]       = vkw != null ? vkw : kwInfo.keyword;
                                        rtActionKeywordToggles[actionIdx] = kwInfo.toggleProp;
                                        rtActionIsKeywordToggle[actionIdx] = true;
                                    }
                                }

                                // Mochie "Always" pass gate (_Zoom/_SST/_Letterbox).
                                // Independent of the keyword bake — _Letterbox has
                                // no keyword but still gates the pass.
                                rtActionAlwaysGate[actionIdx] =
                                    EnigmaShaderHelper.GetAlwaysPassGateId(mats[mi], act.propertyName);
                            }
                        }
                        // Bake non-stateful flag for category 1 (Set) actions.
                        if (act.category == 1)
                            rtActionNonStateful[actionIdx] = true;
                        if (act.useStep)
                            rtActionUseStep[actionIdx] = true;
                        // Bake color selector role for type-10 actions
                        if (act.actionType == 10)
                            rtActionColorSelectorRoles[actionIdx] = act.colorSelectorRole;
                        // Bake variant selector role for type-19 actions
                        if (act.actionType == 19)
                            rtActionVariantSelectorRoles[actionIdx] = act.variantSelectorRole;
                        // Bake per-action delay. Delay defaults to activation-only;
                        // delayOnDeactivate flips the runtime to also defer the
                        // deactivate path through the same scheduler.
                        rtActionDelaySeconds[actionIdx]      = act.useDelay ? Mathf.Max(0f, act.delaySeconds) : 0f;
                        rtActionDelayOnDeactivate[actionIdx] = act.useDelay && act.delayOnDeactivate;
                        // Bake udon event scope for type-5 (TriggerEvent) and type-6 (SetVariable) actions
                        if (act.actionType == 5 || act.actionType == 6)
                            rtActionUdonEventScopes[actionIdx] = act.udonEventScope;
                        // Bake transform space for type-12 (Set Transform) and type-23 (Toggle Transform) actions
                        if (act.actionType == 12 || act.actionType == 23)
                            rtActionTransformSpaces[actionIdx] = act.transformSpace;
                        // Bake teleport rotation and destination for type-13 actions
                        if (act.actionType == 13)
                        {
                            rtActionTeleportRotations[actionIdx] = act.teleportRotationEuler;
                            if (act.propertyType == 4)
                                rtActionTeleportDestinations[actionIdx] = act.teleportDestination;
                        }
                        // Bake stat metric for type-21 (Display Stat) actions
                        if (act.actionType == 21)
                            rtActionStatMetrics[actionIdx] = act.statMetric;
                        // Bake autochange group settings for type-14 and type-17 actions
                        if (act.actionType == 14 || act.actionType == 17)
                        {
                            string acgTag = act.autoChangeGroupName?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(acgTag)
                                && autoChangeGroupMap.TryGetValue(acgTag, out int acgActionId))
                            {
                                rtActionAutoChangeGroupIds[actionIdx]  = acgActionId;
                                rtActionAutoChangeIntervals[actionIdx] = Mathf.Max(0.1f, act.autoChangeGroupInterval);
                                rtAutoChangeGroupRandom[actionIdx]     = act.autoChangeGroupRandom;
                            }
                        }
                        // Bake per-action condition
                        if (act.useCondition)
                        {
                            int cf = act.conditionFolderIndex;
                            int ce = act.conditionEntryIndex;
                            int condGlobalIdx = -1;
                            if (cf >= 0 && cf < globalEntryLookup.Length
                                && globalEntryLookup[cf] != null
                                && ce >= 0 && ce < globalEntryLookup[cf].Length)
                            {
                                condGlobalIdx = globalEntryLookup[cf][ce];
                            }
                            rtActionHasCondition[actionIdx]           = true;
                            rtActionConditionEntryIndex[actionIdx]    = condGlobalIdx;
                            rtActionConditionRequireActive[actionIdx] = act.conditionRequireActive;
                        }
                        actionIdx++;

                        // Auto-toggle: emit a synthetic action that sets the section
                        // toggle property to 1 (category 1, non-stateful). Pass 1
                        // already reserved the extra slot via WouldEmitSyntheticToggle.
                        if (WouldEmitSyntheticToggle(act, out string togProp, out Material togMat))
                        {
                            EmitSyntheticToggleAction(
                                actionIdx, act.targetRenderer, act.materialIndex, togMat, togProp,
                                rtActionTypes, rtActionTargetRenderers, rtActionMaterialIndices,
                                rtActionPropertyNames, rtActionFloatValues, rtActionDefaultFloatValues,
                                rtActionPropertyTypes, rtActionNonStateful,
                                rtActionKeywords, rtActionKeywordToggles, rtActionIsKeywordToggle,
                                rtActionAlwaysGate);
                            actionIdx++;
                        }
                    }
                    rtEntryActionCount[entryIdx] = actionIdx - rtEntryActionStart[entryIdx];

                    // ── Custom Color ──
                    rtEntryUseCustomColor[entryIdx] = entry.useCustomColor;
                    rtEntryCustomColor[entryIdx]    = entry.useCustomColor ? entry.customColor : Color.white;
                    if (entry.useCustomColor && entry.useConditionalColor)
                    {
                        rtEntryCondColorSourceType[entryIdx]    = entry.condColorSourceType;
                        rtEntryCondColorRenderers[entryIdx]     = entry.condColorRenderer;
                        rtEntryCondColorMatIndices[entryIdx]    = entry.condColorMaterialIndex;
                        rtEntryCondColorPropertyNames[entryIdx] = entry.condColorPropertyName ?? "";
                        rtEntryCondColorUdonTargets[entryIdx]   = entry.condColorUdonTarget;
                        rtEntryCondColorUdonVarNames[entryIdx]  = entry.condColorUdonVariableName ?? "";
                    }

                    // ── Conditional Coloring rules ──
                    rtEntryCondColorStart[entryIdx] = condColorIdx;
                    // Prefer entry-level rules; fall back to display action rules.
                    if (entry.useCustomColor && entry.useConditionalColor
                        && entry.condColorRules != null && entry.condColorRules.Length > 0)
                    {
                        foreach (var rule in entry.condColorRules)
                        {
                            rtCondColorConditions[condColorIdx] = rule.condition;
                            rtCondColorValues[condColorIdx]     = rule.value;
                            rtCondColorColors[condColorIdx]     = rule.color;
                            condColorIdx++;
                        }
                    }
                    else
                    {
                        foreach (var act in entry.actions)
                        {
                            if (act.actionType == 9 && act.useConditionalColoring
                                && act.conditionalColorRules != null)
                            {
                                foreach (var rule in act.conditionalColorRules)
                                {
                                    rtCondColorConditions[condColorIdx] = rule.condition;
                                    rtCondColorValues[condColorIdx]     = rule.value;
                                    rtCondColorColors[condColorIdx]     = rule.color;
                                    condColorIdx++;
                                }
                                break;
                            }
                        }
                    }
                    rtEntryCondColorCount[entryIdx] = condColorIdx - rtEntryCondColorStart[entryIdx];

                    // ── Fader links ──
                    if (entry.assignFader)
                    {
                        // Use faderLinks array if populated, otherwise fall back to single faderLink.
                        EnigmaFaderLinkData[] links = entry.faderLinks != null && entry.faderLinks.Length > 0
                            ? entry.faderLinks
                            : (entry.faderLink != null ? new[] { entry.faderLink } : new EnigmaFaderLinkData[0]);

                        foreach (var link in links)
                        {
                            if (link == null || faderLinkIdx >= rtFaderLinkEntryIndex.Length) break;
                            rtFaderLinkEntryIndex[faderLinkIdx]           = entryIdx;
                            rtFaderLinkNames[faderLinkIdx]                = link.faderName ?? "";
                            rtFaderLinkRenderers[faderLinkIdx]            = link.targetRenderer;
                            rtFaderLinkMaterialIndices[faderLinkIdx]      = link.materialIndex;
                            rtFaderLinkPropertyNames[faderLinkIdx]        = link.propertyName;
                            rtFaderLinkPropertyTypes[faderLinkIdx]        = link.propertyType;
                            rtFaderLinkMinValues[faderLinkIdx]            = link.minValue;
                            rtFaderLinkMaxValues[faderLinkIdx]            = link.maxValue;
                            rtFaderLinkDefaultValues[faderLinkIdx]        = link.defaultValue;
                            rtFaderLinkDefaultColors[faderLinkIdx]        = link.defaultColor;
                            // All faders render the indicator ring. The per-link
                            // flag is kept on the data model for legacy reasons
                            // but the runtime always treats it as enabled.
                            rtFaderLinkIndicatorEnabled[faderLinkIdx]     = true;
                            rtFaderLinkIndicatorColors[faderLinkIdx]      = link.indicatorColor;
                            rtFaderLinkIndicatorConditional[faderLinkIdx] = link.indicatorConditional;
                            rtFaderLinkTargetsSlider[faderLinkIdx]        = link.targetsSlider;
                            rtFaderLinkSliders[faderLinkIdx]              = link.targetsSlider && link.targetSliders != null && link.targetSliders.Length > 0
                                                                            ? (Object)link.targetSliders[0] : null;
                            rtFaderLinkSliderReversed[faderLinkIdx]       = link.targetsSlider && link.sliderDirectionsReversed != null && link.sliderDirectionsReversed.Length > 0
                                                                            && link.sliderDirectionsReversed[0];
                            rtFaderLinkTargetsSkybox[faderLinkIdx]        = link.targetsSkybox;
                            rtFaderLinkTargetsUdon[faderLinkIdx]          = link.targetsUdon;
                            rtFaderLinkUdonBehaviours[faderLinkIdx]       = link.targetsUdon && link.targetUdonBehaviours != null && link.targetUdonBehaviours.Length > 0
                                                                            ? link.targetUdonBehaviours[0] : null;
                            rtFaderLinkUdonVariableNames[faderLinkIdx]    = link.targetsUdon ? link.udonVariableName : "";
                            // Managed-write metadata for material-property links.
                            ComputeFaderShaderWriteInfo(link.targetsSlider, link.targetsUdon,
                                link.targetsSkybox ? null : link.targetRenderer, link.materialIndex,
                                link.targetsSkybox ? link.skyboxMaterial : null, link.propertyName,
                                out bool flIsInt, out int flGate, out string flKw);
                            rtFaderLinkPropertyIsInt[faderLinkIdx] = flIsInt;
                            rtFaderLinkAlwaysGate[faderLinkIdx]    = flGate;
                            rtFaderLinkKeywords[faderLinkIdx]      = flKw;
                            faderLinkIdx++;
                        }
                    }

                    // ── Color palette — from first type-7 OR type-10-role-1 action ──
                    EnigmaActionData colorCycleAction = null;
                    foreach (var act in entry.actions)
                    {
                        if (act.actionType == 7) { colorCycleAction = act; break; }
                        if (act.actionType == 10 && act.colorSelectorRole == 1) { colorCycleAction = act; break; }
                    }

                    if (colorCycleAction != null && colorCycleAction.paletteColors != null
                        && colorCycleAction.paletteColors.Length > 0)
                    {
                        rtColorPaletteStart[entryIdx]           = paletteColorIdx;
                        rtColorPaletteCount[entryIdx]           = colorCycleAction.paletteColors.Length;
                        rtColorPaletteRenderers[entryIdx]       = colorCycleAction.colorTargetRenderer;
                        rtColorPaletteMaterialIndices[entryIdx] = colorCycleAction.colorMaterialIndex;
                        rtColorPalettePropertyNames[entryIdx]   = colorCycleAction.colorPropertyName;
                        for (int c = 0; c < colorCycleAction.paletteColors.Length; c++)
                            rtColorPaletteColors[paletteColorIdx++] = colorCycleAction.paletteColors[c];
                    }
                    else
                    {
                        rtColorPaletteStart[entryIdx] = -1;
                        rtColorPaletteCount[entryIdx] = 0;
                    }

                    // ── Variant items — from first type-19 role-1 action ──
                    EnigmaActionData variantAction = null;
                    foreach (var act in entry.actions)
                    {
                        if (act.actionType == 19 && act.variantSelectorRole == 1) { variantAction = act; break; }
                    }

                    if (variantAction != null && variantAction.variantItems != null
                        && variantAction.variantItems.Length > 0)
                    {
                        // Resolve the per-item keyword material once for the
                        // whole item list (the action targets one property).
                        Material variantMat = null;
                        if (variantAction.propertyType == 0
                            && variantAction.targetRenderer != null
                            && !string.IsNullOrEmpty(variantAction.propertyName))
                        {
                            var vMats = variantAction.targetRenderer.sharedMaterials;
                            int vMi = variantAction.materialIndex;
                            if (vMats != null && vMi >= 0 && vMi < vMats.Length)
                                variantMat = vMats[vMi];
                        }

                        rtVariantItemStart[entryIdx] = variantItemIdx;
                        rtVariantItemCount[entryIdx] = variantAction.variantItems.Length;
                        for (int vi = 0; vi < variantAction.variantItems.Length; vi++)
                        {
                            var item = variantAction.variantItems[vi];
                            rtVariantItemNames[variantItemIdx]        = item.variantName ?? "";
                            rtVariantItemFloatValues[variantItemIdx]  = item.floatValue;
                            rtVariantItemColorValues[variantItemIdx]  = item.colorValue;
                            rtVariantItemVectorValues[variantItemIdx] = item.vectorValue;
                            rtVariantItemTextures[variantItemIdx]     = item.textureValue;
                            if (variantMat != null)
                            {
                                string vik = EnigmaShaderHelper.GetKeywordForToggleValue(
                                    variantMat, variantAction.propertyName, item.floatValue);
                                rtVariantItemKeywords[variantItemIdx] = vik ?? "";
                            }
                            variantItemIdx++;
                        }
                    }
                    else
                    {
                        rtVariantItemStart[entryIdx] = -1;
                        rtVariantItemCount[entryIdx] = 0;
                    }

                    // ── Step values — from first action with useStep ──
                    foreach (var act in entry.actions)
                    {
                        if (act.useStep && (act.actionType == 2 || act.actionType == 6))
                        {
                            rtStepAmounts[entryIdx]   = act.stepAmount;
                            rtStepMinValues[entryIdx] = act.stepMin;
                            rtStepMaxValues[entryIdx] = act.stepMax;
                            rtStepWrap[entryIdx]      = act.stepWrap;
                            break;
                        }
                    }

                    // ── Preset data — from first type-8 action ──
                    if (hasPreset)
                    {
                        EnigmaActionData presetAction = null;
                        foreach (var act in entry.actions)
                            if (act.actionType == 8) { presetAction = act; break; }

                        rtPresetRoles[entryIdx] = presetAction.presetRole;

                        // Only Preset Slot (role 0) gets a valid slot index;
                        // Save / Load / Clear buttons (roles 1–3) remain at -1.
                        if (presetAction.presetRole == 0)
                            rtPresetSlotIndex[entryIdx] = presetSlotCounter++;

                        rtPresetScopes[entryIdx]               = presetAction.presetScope;
                        rtPresetIncludeFaders[entryIdx]        = presetAction.presetIncludeFaders;
                        rtPresetIncludeStepValues[entryIdx]    = presetAction.presetIncludeStepValues;
                        rtPresetIncludeColorPalettes[entryIdx] = presetAction.presetIncludeColorPalettes;
                        rtPresetIncludeVariantGroups[entryIdx] = presetAction.presetIncludeVariantGroups;

                        rtPresetIncludedFolderStart[entryIdx] = presetFolderIdx;
                        if (presetAction.presetScope == 1 && presetAction.presetIncludedFolderIndices != null)
                        {
                            rtPresetIncludedFolderCount[entryIdx] = presetAction.presetIncludedFolderIndices.Length;
                            for (int pf = 0; pf < presetAction.presetIncludedFolderIndices.Length; pf++)
                                rtPresetIncludedFolders[presetFolderIdx++] = presetAction.presetIncludedFolderIndices[pf];
                        }
                        else
                        {
                            rtPresetIncludedFolderCount[entryIdx] = 0;
                        }
                    }

                    entryIdx++;
                }
                // Folder entry count = only the non-empty entries that were compiled.
                rtFolderEntryCount[f] = entryIdx - rtFolderEntryStart[f];
            }

            // ── Pass 4.5: Resolve Color Selector group links ──
            // Build a per-folder lookup: colorGroupName → entryIdx of the Set Color (role 1) owner.
            // Then for each role-0/2 entry, look up its linked role-1 entry in O(1).
            for (int f = 0; f < folders.Length; f++)
            {
                int folderStart = rtFolderEntryStart[f];
                int folderCount = rtFolderEntryCount[f];

                // Build group name → role-1 entry index map for this folder.
                var roleOneByGroup = new Dictionary<string, int>();
                for (int ei = folderStart; ei < folderStart + folderCount; ei++)
                {
                    var data = entryDataMap[ei];
                    if (data == null) continue;
                    foreach (var act in data.actions)
                    {
                        if (act.actionType == 10 && act.colorSelectorRole == 1
                            && !string.IsNullOrEmpty(act.colorGroupName))
                        {
                            roleOneByGroup[act.colorGroupName] = ei;
                            break;
                        }
                    }
                }

                // Resolve links for role-0 (Color Display) and role-2 (Change Color) entries.
                for (int ei = folderStart; ei < folderStart + folderCount; ei++)
                {
                    var data = entryDataMap[ei];
                    if (data == null) continue;
                    foreach (var act in data.actions)
                    {
                        if (act.actionType == 10
                            && (act.colorSelectorRole == 0 || act.colorSelectorRole == 2)
                            && !string.IsNullOrEmpty(act.colorGroupName)
                            && roleOneByGroup.TryGetValue(act.colorGroupName, out int roleOneEntry))
                        {
                            rtColorLinkedEntry[ei] = roleOneEntry;
                            break;
                        }
                    }
                }

                // ── Resolve Variant Selector group links (same pattern as Color Selector) ──
                var variantRoleOneByGroup = new Dictionary<string, int>();
                for (int ei = folderStart; ei < folderStart + folderCount; ei++)
                {
                    var data = entryDataMap[ei];
                    if (data == null) continue;
                    foreach (var act in data.actions)
                    {
                        if (act.actionType == 19 && act.variantSelectorRole == 1
                            && !string.IsNullOrEmpty(act.variantGroupName))
                        {
                            variantRoleOneByGroup[act.variantGroupName] = ei;
                            break;
                        }
                    }
                }

                for (int ei = folderStart; ei < folderStart + folderCount; ei++)
                {
                    var data = entryDataMap[ei];
                    if (data == null) continue;
                    foreach (var act in data.actions)
                    {
                        if (act.actionType == 19
                            && (act.variantSelectorRole == 0 || act.variantSelectorRole == 2)
                            && !string.IsNullOrEmpty(act.variantGroupName)
                            && variantRoleOneByGroup.TryGetValue(act.variantGroupName, out int varRoleOneEntry))
                        {
                            rtVariantLinkedEntry[ei] = varRoleOneEntry;
                            break;
                        }
                    }
                }
            }

            // ── Pass 4b: Implicit default-on for Exclusive Off entries ──
            // If an exclusive group has an Exclusive Off entry but no entry is
            // marked On By Default, the Exclusive Off entry is implicitly
            // treated as default-on (it represents "nothing selected").
            {
                // Collect which exclusive group tags already have a default-on entry.
                var groupHasDefaultOn = new HashSet<string>();
                for (int i = 0; i < totalEntries; i++)
                {
                    if (rtEntryDefaultOn[i] && !string.IsNullOrEmpty(rtEntryExclusiveGroupNames[i]))
                    {
                        foreach (string tag in rtEntryExclusiveGroupNames[i].Split(','))
                        {
                            string t = tag.Trim();
                            if (t.Length > 0) groupHasDefaultOn.Add(t);
                        }
                    }
                }

                // For each Exclusive Off entry whose group has no default-on, mark it default-on.
                for (int i = 0; i < totalEntries; i++)
                {
                    if (!rtEntryExclusiveOff[i] || rtEntryDefaultOn[i]) continue;
                    if (string.IsNullOrEmpty(rtEntryExclusiveGroupNames[i])) continue;

                    bool groupCovered = false;
                    foreach (string tag in rtEntryExclusiveGroupNames[i].Split(','))
                    {
                        string t = tag.Trim();
                        if (t.Length > 0 && groupHasDefaultOn.Contains(t))
                        { groupCovered = true; break; }
                    }

                    if (!groupCovered)
                        rtEntryDefaultOn[i] = true;
                }
            }

            // ── Pass 5: Write to component via SerializedObject ──
            WriteArray(so, "rtFolderNames",      rtFolderNames);
            WriteArray(so, "rtFolderEntryStart", rtFolderEntryStart);
            WriteArray(so, "rtFolderEntryCount", rtFolderEntryCount);

            // Bake O(1) folder index lookup per entry.
            var rtEntryFolderIndex = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++) rtEntryFolderIndex[i] = -1;
            for (int f = 0; f < folders.Length; f++)
            {
                int fStart = rtFolderEntryStart[f];
                int fCount = rtFolderEntryCount[f];
                for (int e = fStart; e < fStart + fCount && e < totalEntries; e++)
                    rtEntryFolderIndex[e] = f;
            }
            WriteArray(so, "rtEntryFolderIndex", rtEntryFolderIndex);

            WriteArray(so, "rtEntryLabels",              rtEntryLabels);
            WriteArray(so, "rtEntryButtonTypes",         rtEntryButtonTypes);
            WriteArray(so, "rtEntryIsStateful",          rtEntryIsStateful);
            WriteArray(so, "rtEntryDefaultOn",           rtEntryDefaultOn);
            WriteArray(so, "rtEntryExclusiveGroup",      rtEntryExclusiveGroup);
            WriteArray(so, "rtEntryExclusiveGroupNames", rtEntryExclusiveGroupNames);
            WriteArray(so, "rtEntryExclusiveOff",        rtEntryExclusiveOff);
            WriteArray(so, "rtEntryExclusiveGroupFlat",  flatGroupIds.ToArray());
            WriteArray(so, "rtEntryExclusiveGroupStart", entryGroupStart);
            WriteArray(so, "rtEntryExclusiveGroupCount", entryGroupCount);
            WriteArray(so, "rtGroupTagNames",            rtGroupTagNames);
            WriteArray(so, "rtEntryAutoChangeGroupId",   rtEntryAutoChangeGroupId);
            WriteArray(so, "rtAutoChangeGroupTagNames",  rtAutoChangeGroupTagNames);
            WriteArray(so, "rtEntryExpireSeconds",       rtEntryExpireSeconds);
            WriteArray(so, "rtEntryActionStart",         rtEntryActionStart);
            WriteArray(so, "rtEntryActionCount",         rtEntryActionCount);
            WriteArray(so, "rtEntryIsPreset",            rtEntryIsPreset);

            // ── Write action arrays to executor component ──
            var executor = FindOrCreateExecutor(ctrl.gameObject);
            if (executor != null)
            {
                executor.linkedController = ctrl;
                var exeSo = new SerializedObject(executor);
                ClearExecutorArrays(exeSo);
                WriteArray(exeSo, "rtActionTypes",                        rtActionTypes);
                WriteObjectArray(exeSo, "rtActionTargetObjects",          rtActionTargetObjects);
                WriteObjectArray(exeSo, "rtActionTargetRenderers",        rtActionTargetRenderers);
                WriteArray(exeSo, "rtActionMaterialIndices",              rtActionMaterialIndices);
                WriteObjectArray(exeSo, "rtActionMaterials",              rtActionMaterials);
                WriteObjectArray(exeSo, "rtActionDefaultMaterials",       rtActionDefaultMaterials);
                WriteArray(exeSo, "rtActionPropertyNames",                rtActionPropertyNames);
                WriteArray(exeSo, "rtActionFloatValues",                  rtActionFloatValues);
                WriteColorArray(exeSo, "rtActionColorValues",             rtActionColorValues);
                WriteVector4Array(exeSo, "rtActionVectorValues",          rtActionVectorValues);
                WriteObjectArray(exeSo, "rtActionTextures",               rtActionTextures);
                WriteArray(exeSo, "rtActionDefaultFloatValues",          rtActionDefaultFloatValues);
                WriteColorArray(exeSo, "rtActionDefaultColorValues",     rtActionDefaultColorValues);
                WriteVector4Array(exeSo, "rtActionDefaultVectorValues",  rtActionDefaultVectorValues);
                WriteArray(exeSo, "rtActionPropertyTypes",                rtActionPropertyTypes);
                WriteObjectArray(exeSo, "rtActionUdonTargets",            rtActionUdonTargets);
                WriteArray(exeSo, "rtActionUdonEventNames",               rtActionUdonEventNames);
                WriteArray(exeSo, "rtActionUdonVariableNames",            rtActionUdonVariableNames);
                WriteArray(exeSo, "rtActionUdonVariableTypes",            rtActionUdonVariableTypes);
                WriteArray(exeSo, "rtActionUdonVariableStringValues",     rtActionUdonVariableStringValues);
                WriteArray(exeSo, "rtActionDelaySeconds",                 rtActionDelaySeconds);
                WriteArray(exeSo, "rtActionDelayOnDeactivate",            rtActionDelayOnDeactivate);
                WriteArray(exeSo, "rtActionUdonEventScopes",              rtActionUdonEventScopes);
                WriteArray(exeSo, "rtActionTransformSpaces",              rtActionTransformSpaces);
                WriteVector3Array(exeSo, "rtActionTeleportRotations",     rtActionTeleportRotations);
                WriteObjectArray(exeSo, "rtActionTeleportDestinations",   rtActionTeleportDestinations);
                WriteArray(exeSo, "rtActionAutoChangeGroupIds",           rtActionAutoChangeGroupIds);
                WriteArray(exeSo, "rtActionAutoChangeIntervals",          rtActionAutoChangeIntervals);
                WriteArray(exeSo, "rtAutoChangeGroupRandom",              rtAutoChangeGroupRandom);
                WriteArray(exeSo, "rtActionStatMetrics",                  rtActionStatMetrics);
                WriteArray(exeSo, "rtActionHasCondition",                 rtActionHasCondition);
                WriteArray(exeSo, "rtActionConditionEntryIndex",          rtActionConditionEntryIndex);
                WriteArray(exeSo, "rtActionConditionRequireActive",       rtActionConditionRequireActive);
                WriteArray(exeSo, "rtActionColorSelectorRoles",           rtActionColorSelectorRoles);
                WriteArray(exeSo, "rtActionVariantSelectorRoles",         rtActionVariantSelectorRoles);
                WriteArray(exeSo, "rtActionKeywords",              rtActionKeywords);
                WriteArray(exeSo, "rtActionKeywordToggles",        rtActionKeywordToggles);
                WriteArray(exeSo, "rtActionIsKeywordToggle",       rtActionIsKeywordToggle);
                WriteArray(exeSo, "rtActionNonStateful",           rtActionNonStateful);
                WriteArray(exeSo, "rtActionUseStep",               rtActionUseStep);
                WriteArray(exeSo, "rtActionAlwaysGate",            rtActionAlwaysGate);
                var exeLinkedProp = exeSo.FindProperty("linkedController");
                if (exeLinkedProp != null) exeLinkedProp.objectReferenceValue = ctrl;
                exeSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(executor);
                UdonSharpEditorUtility.CopyProxyToUdon(executor, ProxySerializationPolicy.All);
            }

            // Link the executor on the controller
            var exeProp = so.FindProperty("executor");
            if (exeProp != null) exeProp.objectReferenceValue = executor;

            WriteArray(so, "rtEntryUseCustomColor",                 rtEntryUseCustomColor);
            WriteColorArray(so, "rtEntryCustomColor",            rtEntryCustomColor);
            WriteArray(so, "rtEntryCondColorSourceType",         rtEntryCondColorSourceType);
            WriteObjectArray(so, "rtEntryCondColorRenderers",    rtEntryCondColorRenderers);
            WriteArray(so, "rtEntryCondColorMatIndices",         rtEntryCondColorMatIndices);
            WriteArray(so, "rtEntryCondColorPropertyNames",      rtEntryCondColorPropertyNames);
            WriteObjectArray(so, "rtEntryCondColorUdonTargets",  rtEntryCondColorUdonTargets);
            WriteArray(so, "rtEntryCondColorUdonVarNames",       rtEntryCondColorUdonVarNames);
            WriteArray(so, "rtEntryCondColorStart",              rtEntryCondColorStart);
            WriteArray(so, "rtEntryCondColorCount",              rtEntryCondColorCount);
            WriteArray(so, "rtCondColorConditions",              rtCondColorConditions);
            WriteArray(so, "rtCondColorValues",                  rtCondColorValues);
            WriteColorArray(so, "rtCondColorColors",             rtCondColorColors);

            WriteArray(so, "rtFaderLinkEntryIndex",           rtFaderLinkEntryIndex);
            WriteArray(so, "rtFaderLinkNames",                rtFaderLinkNames);
            WriteObjectArray(so, "rtFaderLinkRenderers",          rtFaderLinkRenderers);
            WriteArray(so, "rtFaderLinkMaterialIndices",      rtFaderLinkMaterialIndices);
            WriteArray(so, "rtFaderLinkPropertyNames",        rtFaderLinkPropertyNames);
            WriteArray(so, "rtFaderLinkPropertyTypes",        rtFaderLinkPropertyTypes);
            WriteArray(so, "rtFaderLinkMinValues",            rtFaderLinkMinValues);
            WriteArray(so, "rtFaderLinkMaxValues",            rtFaderLinkMaxValues);
            WriteArray(so, "rtFaderLinkDefaultValues",        rtFaderLinkDefaultValues);
            WriteColorArray(so, "rtFaderLinkDefaultColors",   rtFaderLinkDefaultColors);
            WriteArray(so, "rtFaderLinkIndicatorEnabled",     rtFaderLinkIndicatorEnabled);
            WriteColorArray(so, "rtFaderLinkIndicatorColors", rtFaderLinkIndicatorColors);
            WriteArray(so, "rtFaderLinkIndicatorConditional", rtFaderLinkIndicatorConditional);
            WriteArray(so, "rtFaderLinkTargetsSlider",       rtFaderLinkTargetsSlider);
            WriteObjectArray(so, "rtFaderLinkSliders",       rtFaderLinkSliders);
            WriteArray(so, "rtFaderLinkSliderReversed",      rtFaderLinkSliderReversed);
            WriteArray(so, "rtFaderLinkTargetsSkybox",        rtFaderLinkTargetsSkybox);
            WriteArray(so, "rtFaderLinkTargetsUdon",         rtFaderLinkTargetsUdon);
            WriteObjectArray(so, "rtFaderLinkUdonBehaviours", rtFaderLinkUdonBehaviours);
            WriteArray(so, "rtFaderLinkUdonVariableNames",   rtFaderLinkUdonVariableNames);
            WriteArray(so, "rtFaderLinkPropertyIsInt",       rtFaderLinkPropertyIsInt);
            WriteArray(so, "rtFaderLinkAlwaysGate",          rtFaderLinkAlwaysGate);
            WriteArray(so, "rtFaderLinkKeywords",            rtFaderLinkKeywords);

            // ── Static fader managed-write metadata ──
            // Static faders are authored directly in the inspector (no build
            // pass of their own), so their Int/gate/keyword info is derived
            // here from the already-serialized rtStaticFader* arrays.
            {
                var sfNames = ctrl.rtStaticFaderPropertyNames ?? new string[0];
                int sfCount = sfNames.Length;
                var sfIsInt = new bool[sfCount];
                var sfGate  = new int[sfCount];
                var sfKw    = new string[sfCount];
                for (int sf = 0; sf < sfCount; sf++)
                {
                    sfGate[sf] = -1; sfKw[sf] = "";
                    bool sfSlider = ctrl.rtStaticFaderTargetsSlider != null
                        && sf < ctrl.rtStaticFaderTargetsSlider.Length && ctrl.rtStaticFaderTargetsSlider[sf];
                    bool sfUdon = ctrl.rtStaticFaderTargetsUdon != null
                        && sf < ctrl.rtStaticFaderTargetsUdon.Length && ctrl.rtStaticFaderTargetsUdon[sf];
                    bool sfSkybox = ctrl.rtStaticFaderTargetsSkybox != null
                        && sf < ctrl.rtStaticFaderTargetsSkybox.Length && ctrl.rtStaticFaderTargetsSkybox[sf];
                    Renderer sfRend = ctrl.rtStaticFaderRenderers != null
                        && sf < ctrl.rtStaticFaderRenderers.Length ? ctrl.rtStaticFaderRenderers[sf] : null;
                    int sfMi = ctrl.rtStaticFaderMaterialIndices != null
                        && sf < ctrl.rtStaticFaderMaterialIndices.Length ? ctrl.rtStaticFaderMaterialIndices[sf] : 0;
                    Material sfSky = sfSkybox ? RenderSettings.skybox : null;
                    ComputeFaderShaderWriteInfo(sfSlider, sfUdon,
                        sfSkybox ? null : sfRend, sfMi, sfSky, sfNames[sf],
                        out sfIsInt[sf], out sfGate[sf], out sfKw[sf]);
                }
                WriteArray(so, "rtStaticFaderPropertyIsInt", sfIsInt);
                WriteArray(so, "rtStaticFaderAlwaysGate",    sfGate);
                WriteArray(so, "rtStaticFaderKeywords",      sfKw);
            }

            WriteArray(so, "rtColorPaletteStart",           rtColorPaletteStart);
            WriteArray(so, "rtColorPaletteCount",           rtColorPaletteCount);
            WriteColorArray(so, "rtColorPaletteColors",     rtColorPaletteColors);
            WriteObjectArray(so, "rtColorPaletteRenderers", rtColorPaletteRenderers);
            WriteArray(so, "rtColorPaletteMaterialIndices", rtColorPaletteMaterialIndices);
            WriteArray(so, "rtColorPalettePropertyNames",   rtColorPalettePropertyNames);
            WriteArray(so, "rtColorLinkedEntry",            rtColorLinkedEntry);
            // rtActionColorSelectorRoles written to executor above

            WriteArray(so, "rtVariantItemStart",              rtVariantItemStart);
            WriteArray(so, "rtVariantItemCount",              rtVariantItemCount);
            WriteArray(so, "rtVariantItemNames",              rtVariantItemNames);
            WriteArray(so, "rtVariantItemFloatValues",        rtVariantItemFloatValues);
            WriteColorArray(so, "rtVariantItemColorValues",   rtVariantItemColorValues);
            WriteVector4Array(so, "rtVariantItemVectorValues",rtVariantItemVectorValues);
            WriteObjectArray(so, "rtVariantItemTextures",     rtVariantItemTextures);
            WriteArray(so, "rtVariantItemKeywords",           rtVariantItemKeywords);
            WriteArray(so, "rtVariantLinkedEntry",            rtVariantLinkedEntry);
            // rtActionVariantSelectorRoles written to executor above

            WriteArray(so, "rtStepAmounts",   rtStepAmounts);
            WriteArray(so, "rtStepMinValues", rtStepMinValues);
            WriteArray(so, "rtStepMaxValues", rtStepMaxValues);
            WriteArray(so, "rtStepWrap",      rtStepWrap);

            WriteArray(so, "rtPresetScopes",               rtPresetScopes);
            WriteArray(so, "rtPresetIncludedFolderStart",  rtPresetIncludedFolderStart);
            WriteArray(so, "rtPresetIncludedFolderCount",  rtPresetIncludedFolderCount);
            WriteArray(so, "rtPresetIncludedFolders",      rtPresetIncludedFolders);
            WriteArray(so, "rtPresetIncludeFaders",        rtPresetIncludeFaders);
            WriteArray(so, "rtPresetIncludeStepValues",    rtPresetIncludeStepValues);
            WriteArray(so, "rtPresetIncludeColorPalettes", rtPresetIncludeColorPalettes);
            WriteArray(so, "rtPresetIncludeVariantGroups", rtPresetIncludeVariantGroups);
            WriteArray(so, "rtPresetSlotIndex",            rtPresetSlotIndex);
            WriteArray(so, "rtPresetRoles",                rtPresetRoles);

            // ── Pass 5.5: Auto-create EnigmaPresetStorage if any preset slots exist ──
            // The preset slot storage is a separate UdonSharpBehaviour with its own
            // [UdonSynced] arrays and its own RequestSerialization lifecycle. See
            // EnigmaPresetStorage.cs for the rationale. We create/update it here
            // because this is the first point in the build where we know the
            // final numPresets / numEntries / numFaders counts needed for sizing.
            EnsurePresetStoragePresence(so, ctrl, rtPresetRoles, totalEntries);

            // ── Pass 6: Initialize runtime synced state ──
            var entryStates              = new bool[totalEntries];
            var stepCurrentValues        = new float[totalEntries];
            var colorPaletteCurrentIdxs  = new int[totalEntries];
            var colorPalettePendingIdxs  = new int[totalEntries];
            for (int i = 0; i < totalEntries; i++)
            {
                entryStates[i] = rtEntryDefaultOn[i];
                if (rtEntryButtonTypes[i] == 2) // Step
                    stepCurrentValues[i] = rtStepMinValues[i];
            }
            WriteArray(so, "entryStates",                entryStates);
            WriteArray(so, "stepCurrentValues",          stepCurrentValues);
            WriteArray(so, "colorPaletteCurrentIndices", colorPaletteCurrentIdxs);
            WriteArray(so, "colorPalettePendingIndices", colorPalettePendingIdxs);
            WriteArray(so, "variantCurrentIndices",      new int[totalEntries]);
            WriteArray(so, "variantPendingIndices",      new int[totalEntries]);

            WriteObjectArray(so, "shaderInstances", ctrl.shaderInstances);

            // ── Pass 7: Auto-assign slot indices on hardware components ──
            AutoAssignButtonSlotIndices(ctrl);
            AutoAssignFaderSlotIndices(ctrl);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ctrl);

            // ── Sync proxy to UdonBehaviour heap ──
            // SerializedObject writes to the C# proxy, but UdonSharp's Udon VM
            // reads from the backing UdonBehaviour heap. In edit mode UdonSharp
            // auto-syncs them, but during play-mode-entry builds the heap
            // already holds stale data. Force-sync so the runtime sees the
            // freshly built rt* arrays.
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(ctrl, ProxySerializationPolicy.All);
            }
            catch (System.ArgumentNullException)
            {
                // In test environments the backing UdonBehaviour may not exist,
                // causing the serializer to fail with a null-key lookup.
                // The proxy fields are still written via SerializedObject above,
                // so this is safe to skip.
            }

            // ── Build per-entry exclusive peer lookup arrays ──
            // HandleToggle / DeactivateExclusiveGroupPeers read from
            // rtEntryExclusivePeerStart/Count/Flat. These are populated by
            // BuildExclusivePeerLinks, which historically was only called
            // from EnigmaPlayModeHook at play-mode entry. That meant any
            // consumer that called BuildRuntimeArrays directly (editor
            // "Rebuild" button, editor-time tests, scripted builds) ended
            // up with NULL peer arrays, silently turning every exclusive
            // group operation into a no-op. Run it here so the build output
            // is self-consistent regardless of entry point. The PlayModeHook
            // will still re-run it with the full scene context (all
            // controllers + all standalone EnigmaButtons) at play-mode
            // entry — that's the authoritative pass for cross-component
            // wiring. This per-controller call handles the entry peer
            // arrays so in-editor functionality works immediately.
            try
            {
                BuildExclusivePeerLinks(
                    new EnigmaController[] { ctrl },
                    new EnigmaButton[0]);
            }
            catch (System.ArgumentNullException)
            {
                // Same test-environment safety net as above.
            }

            Debug.Log($"[EnigmaController] Build complete — {folders.Length} folders, {totalEntries} entries, {totalActions} actions.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION COMPILER
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns <c>true</c> when the given action type contributes to the button's
        /// persistent on/off state (and therefore causes it to behave as a Toggle).
        /// Non-stateful types (events, display-only, Command/Nav/Preset operations)
        /// do not toggle state — they push the button toward Momentary instead.
        /// </summary>
        internal static bool IsStatefulActionType(int actionType)
        {
            return IsStatefulAction(actionType, 0);
        }

        /// <summary>
        /// Overload that also considers the action's category.
        /// Category 0 = toggle (stateful), category 1 = set (one-shot).
        /// Action types that share a runtime type for both toggle and set modes
        /// (e.g. type 2 = Set Shader Property) are only stateful when category == 0.
        /// </summary>
        internal static bool IsStatefulAction(int actionType, int category)
        {
            switch (actionType)
            {
                // Types that fire once without maintaining persistent state:
                case 4:   // Apply Skybox (command, fire-once; Toggle Skybox = type 22 is stateful)
                case 5:   // TriggerEvent
                case 9:   // DisplayValue (read-only)
                case 10:  // ColorSelector (momentary role interactions)
                case 12:  // Transform — command-style, no persistent on/off state
                case 13:  // Teleport — command-style, fires once on press
                case 15:  // Command: SetObjectState
                case 16:  // Command: SetComponentState
                case 17:  // Command: SetAutochangeGroupState
                case 18:  // Command: Set Whitelist (Toggle Whitelist = type 28 is stateful via default branch)
                case 19:  // VariantGroup (placeholder)
                case 20:  // Nav embedded
                case 21:  // DisplayStat — read-only world stats display
                case 24:  // DisplayFolderName — read-only
                case 25:  // DisplayPageNumber — read-only
                    return false;
                default:
                    // Category 1 = "Set" (one-shot), only category 0 = "Toggle" is stateful.
                    return category == 0;
            }
        }

        private static void CompileAction(
            EnigmaActionData action, int idx,
            int[] types, GameObject[] objs, Renderer[] rends,
            int[] matIdxs, Material[] mats, Material[] defMats, string[] propNames,
            float[] floatVals, Color[] colorVals, Vector4[] vecVals,
            Texture[] textures,
            float[] defFloatVals, Color[] defColorVals, Vector4[] defVecVals,
            int[] propTypes, UdonSharp.UdonSharpBehaviour[] udons,
            string[] eventNames, string[] varNames,
            int[] varTypes, string[] varStringVals)
        {
            types[idx]         = action.actionType;
            objs[idx]          = action.targetObject;
            rends[idx]         = action.targetRenderer;
            matIdxs[idx]       = action.materialIndex;
            mats[idx]          = action.targetMaterial;
            defMats[idx]       = action.defaultMaterial;
            propNames[idx]     = action.propertyName ?? "";
            floatVals[idx]     = action.propertyFloatValue;
            colorVals[idx]     = action.propertyColorValue;
            vecVals[idx]       = action.propertyVectorValue;
            textures[idx]      = action.targetTexture;
            defFloatVals[idx]  = action.defaultFloatValue;
            defColorVals[idx]  = action.defaultColorValue;
            defVecVals[idx]    = action.defaultVectorValue;
            propTypes[idx]     = action.propertyType;
            udons[idx]         = action.targetUdon;
            eventNames[idx]    = action.udonEventName ?? "";
            // For Display Value (type 9), variable name is stored in the unified propertyName field.
            string udonVarName = action.udonVariableName ?? "";
            if (action.actionType == 9 && string.IsNullOrEmpty(udonVarName))
                udonVarName = action.propertyName ?? "";
            varNames[idx]      = udonVarName;
            varTypes[idx]      = action.udonVariableType;
            varStringVals[idx] = action.udonVariableStringValue ?? "";

            // ── Command SetState types (15, 16, 17, 18): bake target state into floatValues ──
            // Runtime reads floatValues[a] >= 0.5f as "true / on".
            if (action.actionType == 15
                || action.actionType == 17 || action.actionType == 18
                || (action.actionType == 27 && action.category == 1))
            {
                floatVals[idx] = action.commandTargetState ? 1f : 0f;
            }

            // ── Shader Keyword Set mode (type 27, cat 1): mark with propertyType=1 ──
            if (action.actionType == 27 && action.category == 1)
            {
                propTypes[idx] = 1; // Runtime uses this to distinguish Set from Toggle
            }

            // ── Nav embedded (type 20): propertyType = nav operation, floatValues = target index ──
            if (action.actionType == 20)
            {
                // propertyType holds the nav operation (set by SyncActionType).
                // Bake folder/page target into floatValues.
                int navOp = action.propertyType;
                if (navOp == 2) // GoToFolder
                    floatVals[idx] = action.navFolderTarget;
                else if (navOp == 5) // GoToPage
                    floatVals[idx] = action.navPageTarget;
                else if (navOp == 12) // GoToFaderPage
                    floatVals[idx] = action.navFaderPageTarget;
                else
                    floatVals[idx] = 0f;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  AUTO-TOGGLE SYNTHETIC ACTION HELPER
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Decides whether a Set Shader Property action would emit an extra
        /// synthetic "set the section toggle" action at build time. Used by
        /// both the allocation pass (to size the runtime arrays correctly) and
        /// the compilation pass (to actually emit the synthetic action). MUST
        /// return the same answer for both passes — otherwise the compilation
        /// pass would write past the end of the allocated arrays.
        ///
        /// Returns false when the action type isn't Set Shader Property, the
        /// checkbox is unchecked, the renderer/material can't be resolved, or
        /// EnigmaShaderHelper.TryGetEffectToggle finds no associated section
        /// toggle for this property (or the action's property already IS the
        /// section toggle).
        /// </summary>
        /// <summary>
        /// Resolves the managed-write metadata a fader needs to drive a shader
        /// property safely at runtime: whether the property is Int-declared
        /// (so writes must mirror through SetInt — Mochie declares many props
        /// that way and SetFloat alone may not update the int uniform on
        /// standalone), the Mochie Always-pass gate id, and the keyword to
        /// enable when the value goes non-zero. All inert for slider/Udon
        /// targets and unresolvable materials.
        /// </summary>
        internal static void ComputeFaderShaderWriteInfo(
            bool targetsSlider, bool targetsUdon,
            Renderer renderer, int materialIndex, Material explicitMaterial,
            string propertyName,
            out bool isInt, out int gate, out string keyword)
        {
            isInt = false; gate = -1; keyword = "";
            if (targetsSlider || targetsUdon || string.IsNullOrEmpty(propertyName)) return;

            Material mat = explicitMaterial;
            if (mat == null && renderer != null)
            {
                var mats = renderer.sharedMaterials;
                if (mats != null && materialIndex >= 0 && materialIndex < mats.Length)
                    mat = mats[materialIndex];
            }
            if (mat == null || mat.shader == null) return;

            gate = EnigmaShaderHelper.GetAlwaysPassGateId(mat, propertyName);
            // Mode-1 keyword: a fader sweeping an enum-mode toggle can't bake a
            // per-value keyword, so the first "on" mode's keyword is used —
            // faders on multi-mode enums are a degenerate authoring case anyway.
            string kw = EnigmaShaderHelper.GetKeywordForToggleValue(mat, propertyName, 1f);
            keyword = kw != null ? kw : "";
            isInt = EnigmaShaderHelper.IsIntDeclaredProperty(mat.shader, propertyName);
        }

        private static bool WouldEmitSyntheticToggle(
            EnigmaActionData act, out string toggleProp, out Material mat)
        {
            toggleProp = null;
            mat = null;
            if (act == null
                || act.actionType != 2
                || !act.alsoSetEffectToggle
                || act.targetRenderer == null
                || string.IsNullOrEmpty(act.propertyName))
                return false;

            var mats = act.targetRenderer.sharedMaterials;
            int mi = act.materialIndex;
            if (mats == null || mi < 0 || mi >= mats.Length || mats[mi] == null)
                return false;
            mat = mats[mi];

            return EnigmaShaderHelper.TryGetEffectToggle(mat, act.propertyName, out toggleProp);
        }

        /// <summary>
        /// Writes a synthetic "set <paramref name="toggleProp"/> = 1" action
        /// into the runtime arrays at <paramref name="idx"/>. Mirrors the
        /// fields a hand-written category-1 (non-stateful) Set Shader Property
        /// action would produce, then bakes its keyword info via
        /// <see cref="EnigmaShaderHelper.GetPropertyKeywordInfo"/> so the
        /// shader_feature_local variant gets enabled at runtime.
        ///
        /// Caller is responsible for incrementing its action counter past
        /// <paramref name="idx"/> after this returns.
        /// </summary>
        private static void EmitSyntheticToggleAction(
            int idx, Renderer renderer, int materialIndex, Material mat, string toggleProp,
            int[] rtActionTypes, Renderer[] rtActionTargetRenderers,
            int[] rtActionMaterialIndices, string[] rtActionPropertyNames,
            float[] rtActionFloatValues, float[] rtActionDefaultFloatValues,
            int[] rtActionPropertyTypes, bool[] rtActionNonStateful,
            string[] rtActionKeywords, string[] rtActionKeywordToggles,
            bool[] rtActionIsKeywordToggle, int[] rtActionAlwaysGate)
        {
            rtActionTypes[idx]              = 2;
            rtActionTargetRenderers[idx]    = renderer;
            rtActionMaterialIndices[idx]    = materialIndex;
            rtActionPropertyNames[idx]      = toggleProp;
            rtActionPropertyTypes[idx]      = 0; // Float
            rtActionFloatValues[idx]        = 1f;
            rtActionDefaultFloatValues[idx] = 0f;
            rtActionNonStateful[idx]        = true; // category 1 — set on activate, don't revert
            // Mochie "Always" pass gate: synthetic toggles for _Zoom/_SST/_Letterbox
            // sections must carry the gate id so the runtime manages the pass on
            // both activation and entry deactivation.
            rtActionAlwaysGate[idx] = EnigmaShaderHelper.GetAlwaysPassGateId(mat, toggleProp);

            // Bake the keyword association for the synthetic action so its
            // shader_feature_local variant gets enabled at runtime (e.g.
            // _COLOR_ON for _FilterModel, _IMAGE_OVERLAY_ON for _SST). The
            // synthetic action always writes 1, so the value-aware resolution
            // uses the mode-1 keyword. (Material fixups are handled by the
            // once-per-material scoped pre-pass, which already includes
            // synthetic toggle properties in the managed set.)
            var kwInfo = EnigmaShaderHelper.GetPropertyKeywordInfo(mat, toggleProp);
            if (kwInfo.keyword != null && kwInfo.toggleProp == toggleProp)
            {
                string vkw = EnigmaShaderHelper.GetKeywordForToggleValue(mat, toggleProp, 1f);
                rtActionKeywords[idx]        = vkw != null ? vkw : kwInfo.keyword;
                rtActionKeywordToggles[idx]  = kwInfo.toggleProp;
                rtActionIsKeywordToggle[idx] = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  EXECUTOR COMPONENT MANAGEMENT
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds an existing EnigmaExecutor on the GameObject, or creates one.
        /// This ensures exactly one executor per controller/button.
        /// </summary>
        private static EnigmaExecutor FindOrCreateExecutor(GameObject go)
        {
            var executor = go.GetComponent<EnigmaExecutor>();
            if (executor == null)
            {
                try
                {
                    executor = go.AddUdonSharpComponent<EnigmaExecutor>();
                }
                catch (System.Exception ex)
                {
                    // During domain reload UdonSharp's type mapping may not be ready yet.
                    // Return null — the EnteredPlayMode rebuild will retry successfully.
                    Debug.LogWarning(
                        $"[EnigmaOS] Could not create EnigmaExecutor on '{go.name}' " +
                        $"(will retry after domain reload): {ex.Message}", go);
                    return null;
                }
            }
            return executor;
        }

        /// <summary>
        /// Auto-create and wire up the <see cref="EnigmaPresetStorage"/> component
        /// when any folder in the controller contains a preset slot (presetRole == 0).
        ///
        /// <b>IMPORTANT:</b> the storage component lives on a dedicated CHILD
        /// GameObject (named <c>"EnigmaPresetStorage"</c>), NOT on the controller's
        /// own GameObject. Live VRChat testing (April 2026) showed that
        /// <c>OnPostSerialization.byteCount</c> reports the SAME value on every
        /// UdonBehaviour on a given GameObject — VRChat batches all synced
        /// behaviours on a single GameObject into one Manual-sync frame. Putting
        /// the 28 KB preset storage on the same GameObject as the 2 KB controller
        /// meant every controller sync was dragging the preset storage along as
        /// a single ~30 KB packet. Moving storage to a child GameObject gives it
        /// its own independent sync frame, so controller syncs stay at ~2 KB
        /// and fire at VRChat's expected ~0.17 s cadence, while preset storage
        /// only syncs when it's actually called via its own RequestSerialization.
        ///
        /// Called from <see cref="BuildRuntimeArrays"/> right after
        /// <c>rtPresetRoles</c> is written. Idempotent — re-runs just resize the
        /// arrays and re-wire the reference without re-creating the component.
        ///
        /// When <paramref name="rtPresetRoles"/> is null or contains no slot
        /// entries, this is a no-op. It does NOT remove an existing storage
        /// component — the empty arrays are harmless and removing could lose
        /// data if the user is mid-edit.
        ///
        /// Migration: if a previous version of this code put the storage
        /// component directly on the controller's own GameObject, we detect
        /// that here and remove it BEFORE creating the child-GameObject
        /// replacement, so that the migration happens silently on the next
        /// Rebuild.
        /// </summary>
        private static void EnsurePresetStoragePresence(
            SerializedObject so, EnigmaController ctrl,
            int[] rtPresetRoles, int totalEntries)
        {
            // Count slot entries (presetRole == 0). Other roles (Save/Load/Clear)
            // don't need storage.
            int numPresets = 0;
            if (rtPresetRoles != null)
            {
                for (int i = 0; i < rtPresetRoles.Length; i++)
                    if (rtPresetRoles[i] == 0) numPresets++;
            }
            if (numPresets == 0) return;

            // ─── Migration: remove any stale EnigmaPresetStorage on the controller's
            //    own GameObject (an older version of this builder put it there). ───
            var staleOnParent = ctrl.GetComponent<EnigmaPresetStorage>();
            if (staleOnParent != null)
            {
                Debug.Log(
                    "[EnigmaOS] Migrating EnigmaPresetStorage off the controller's own " +
                    "GameObject to a dedicated child (required for independent sync frame). " +
                    "Existing in-session preset data will be re-allocated empty.", ctrl);
                Object.DestroyImmediate(staleOnParent);
                // Clear the stale reference so the FindProperty below starts clean.
                so.FindProperty("presetStorage").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }

            // ─── Locate or create the dedicated child GameObject. ───
            // Name is just "PresetStorage" — the parent already has "Enigma" in
            // its name so the prefix would be redundant. Older builds used
            // "EnigmaPresetStorage"; if we find that legacy child we rename it
            // in-place so the migration is invisible.
            const string StorageChildName = "PresetStorage";
            Transform childT = ctrl.transform.Find(StorageChildName);
            if (childT == null)
            {
                Transform legacyChildT = ctrl.transform.Find("EnigmaPresetStorage");
                if (legacyChildT != null)
                {
                    legacyChildT.gameObject.name = StorageChildName;
                    childT = legacyChildT;
                    Debug.Log("[EnigmaOS] Renamed legacy 'EnigmaPresetStorage' child GameObject to 'PresetStorage'.", ctrl);
                }
            }
            GameObject childGo;
            if (childT == null)
            {
                childGo = new GameObject(StorageChildName);
                childGo.transform.SetParent(ctrl.transform, worldPositionStays: false);
                childGo.transform.localPosition = Vector3.zero;
                childGo.transform.localRotation = Quaternion.identity;
                childGo.transform.localScale    = Vector3.one;
            }
            else
            {
                childGo = childT.gameObject;
            }

            // ─── Locate or add the storage component on the child. ───
            var storage = childGo.GetComponent<EnigmaPresetStorage>();
            if (storage == null)
            {
                try
                {
                    storage = childGo.AddUdonSharpComponent<EnigmaPresetStorage>();
                }
                catch (System.Exception ex)
                {
                    // Domain-reload races: the EnteredPlayMode rebuild will retry.
                    Debug.LogWarning(
                        $"[EnigmaOS] Could not add EnigmaPresetStorage component to child '{childGo.name}' " +
                        $"(will retry after domain reload): {ex.Message}", ctrl);
                    return;
                }
            }

            int numFaders = ctrl.faderSlots != null ? ctrl.faderSlots.Length : 0;

            // Wire up the controller ↔ storage references on both sides so the
            // runtime code paths can find each other without hunting.
            so.FindProperty("presetStorage").objectReferenceValue = storage;

            var storageSo = new SerializedObject(storage);
            storageSo.FindProperty("controller").objectReferenceValue = ctrl;

            // Size the storage arrays to match the current layout. Matches the
            // runtime EnigmaPresetStorage.AllocateStorage logic so that the
            // prefab/scene state is consistent before Start() runs.
            int entryStride = numPresets * totalEntries;
            int faderStride = numPresets * numFaders;
            ResizeSerializedArray(storageSo, "presetIsSaved",              numPresets);
            ResizeSerializedArray(storageSo, "presetSavedEntryStates",     entryStride);
            ResizeSerializedArray(storageSo, "presetSavedStepValues",      entryStride);
            ResizeSerializedArray(storageSo, "presetSavedColorIndices",    entryStride);
            ResizeSerializedArray(storageSo, "presetSavedFaderValues",     faderStride);
            ResizeSerializedArray(storageSo, "presetSavedVariantIndices",  entryStride);

            storageSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(storage);
            EditorUtility.SetDirty(childGo);
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(storage, ProxySerializationPolicy.All);
            }
            catch (System.ArgumentNullException)
            {
                // In test environments the backing UdonBehaviour may not exist,
                // same safety net used elsewhere in this file.
            }
        }

        /// <summary>
        /// Resize a SerializedProperty array to the target length, preserving
        /// existing element data. Used by <see cref="EnsurePresetStoragePresence"/>
        /// so that an existing preset library (saved slot state) survives a
        /// rebuild when the layout size hasn't changed.
        /// </summary>
        private static void ResizeSerializedArray(SerializedObject so, string propName, int targetLength)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) return;
            if (!prop.isArray)
            {
                Debug.LogWarning($"[EnigmaOS] ResizeSerializedArray: '{propName}' is not an array on {so.targetObject}");
                return;
            }
            if (prop.arraySize != targetLength)
                prop.arraySize = targetLength;
        }

        /// <summary>
        /// Clears all rt* arrays on the executor to zero length via SerializedObject.
        /// Must be called before writing new arrays so that stale data from a previous
        /// build (e.g. deleted folders) doesn't linger.
        /// </summary>
        private static void ClearExecutorArrays(SerializedObject exeSo)
        {
            string[] arrayNames = {
                "rtActionTypes", "rtActionTargetObjects", "rtActionTargetRenderers",
                "rtActionMaterialIndices", "rtActionMaterials", "rtActionDefaultMaterials", "rtActionPropertyNames",
                "rtActionFloatValues", "rtActionColorValues", "rtActionVectorValues",
                "rtActionTextures", "rtActionDefaultFloatValues", "rtActionDefaultColorValues",
                "rtActionDefaultVectorValues", "rtActionPropertyTypes", "rtActionUdonTargets",
                "rtActionUdonEventNames", "rtActionUdonVariableNames", "rtActionUdonVariableTypes",
                "rtActionUdonVariableStringValues", "rtActionDelaySeconds", "rtActionDelayOnDeactivate",
                "rtActionUdonEventScopes", "rtActionTransformSpaces",
                "rtActionTeleportRotations", "rtActionTeleportDestinations",
                "rtActionStatMetrics", "rtActionHasCondition", "rtActionConditionEntryIndex",
                "rtActionConditionRequireActive", "rtActionColorSelectorRoles",
                "rtActionVariantSelectorRoles", "rtActionKeywords", "rtActionKeywordToggles", "rtActionIsKeywordToggle", "rtActionNonStateful", "rtActionUseStep", "rtActionAlwaysGate",
                "rtActionAutoChangeGroupIds",
                "rtActionAutoChangeIntervals", "rtAutoChangeGroupRandom",
                "rtCondColorStart", "rtCondColorCount", "rtCondColorConditions",
                "rtCondColorValues", "rtCondColorColors",
            };
            foreach (string name in arrayNames)
            {
                var prop = exeSo.FindProperty(name);
                if (prop != null) prop.arraySize = 0;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SERIALIZED PROPERTY WRITERS
        // ════════════════════════════════════════════════════════════════════════

        private static void WriteArray(SerializedObject so, string propName, string[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).stringValue = data[i] ?? "";
        }

        private static void WriteArray(SerializedObject so, string propName, int[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).intValue = data[i];
        }

        private static void WriteArray(SerializedObject so, string propName, float[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).floatValue = data[i];
        }

        private static void WriteArray(SerializedObject so, string propName, bool[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).boolValue = data[i];
        }

        private static void WriteColorArray(SerializedObject so, string propName, Color[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).colorValue = data[i];
        }

        private static void WriteVector4Array(SerializedObject so, string propName, Vector4[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).vector4Value = data[i];
        }

        private static void WriteVector3Array(SerializedObject so, string propName, Vector3[] data)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).vector3Value = data[i];
        }

        private static void WriteObjectArray<T>(SerializedObject so, string propName, T[] data) where T : Object
        {
            var prop = so.FindProperty(propName);
            if (prop == null) { Debug.LogWarning($"[Build] Property not found: {propName}"); return; }
            prop.arraySize = data.Length;
            for (int i = 0; i < data.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = data[i];
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ENIGMABUTTON BUILD PIPELINE
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compiles a standalone EnigmaButton's EnigmaActionData[] into its flat rt* arrays,
        /// analogous to BuildRuntimeArrays for EnigmaController.
        /// </summary>
        internal static void BuildEnigmaButtonArrays(SerializedObject so, EnigmaButton btn)
        {
            var actionsHolder = EnigmaButtonEditor.GetActionsIfExists(btn);
            EnigmaActionData[] actions = (actionsHolder != null ? actionsHolder.actions : null) ?? new EnigmaActionData[0];
            int totalActions = actions.Length;

            // Reserve extra slots for auto-toggle synthetic actions, matching the
            // controller's pass-1 sizing logic.
            for (int i = 0; i < actions.Length; i++)
            {
                if (WouldEmitSyntheticToggle(actions[i], out _, out _))
                    totalActions += 1;
            }

            // ── Allocate arrays ──
            var rtActionTypes            = new int[totalActions];
            var rtActionTargetObjects    = new GameObject[totalActions];
            var rtActionTargetRenderers  = new Renderer[totalActions];
            var rtActionMaterialIndices  = new int[totalActions];
            var rtActionMaterials        = new Material[totalActions];
            // Toggle Material default-state material per action. Mirrored from
            // EnigmaActionData.defaultMaterial; consumed by the runtime type==1
            // branch on deactivate.
            var rtActionDefaultMaterials = new Material[totalActions];
            var rtActionPropertyNames    = new string[totalActions];
            var rtActionFloatValues      = new float[totalActions];
            var rtActionColorValues      = new Color[totalActions];
            var rtActionVectorValues     = new Vector4[totalActions];
            var rtActionTextures         = new Texture[totalActions];
            var rtActionDefaultFloatValues  = new float[totalActions];
            var rtActionDefaultColorValues  = new Color[totalActions];
            for (int dc = 0; dc < totalActions; dc++) rtActionDefaultColorValues[dc] = Color.white;
            var rtActionDefaultVectorValues = new Vector4[totalActions];
            var rtActionPropertyTypes    = new int[totalActions];
            var rtActionUdonTargets              = new UdonSharp.UdonSharpBehaviour[totalActions];
            var rtActionUdonEventNames           = new string[totalActions];
            var rtActionUdonVariableNames        = new string[totalActions];
            var rtActionUdonVariableTypes        = new int[totalActions];
            var rtActionUdonVariableStringValues = new string[totalActions];
            var rtActionDelaySeconds             = new float[totalActions];
            var rtActionDelayOnDeactivate        = new bool[totalActions];
            var rtActionUdonEventScopes          = new int[totalActions];
            var rtActionTransformSpaces          = new int[totalActions];
            var rtActionTeleportRotations        = new Vector3[totalActions];
            var rtActionTeleportDestinations     = new GameObject[totalActions];
            var rtActionStatMetrics              = new int[totalActions];
            var rtActionHasCondition             = new bool[totalActions];
            var rtActionConditionRequireActive   = new bool[totalActions];
            var rtActionColorSelectorRoles       = new int[totalActions];
            var rtActionVariantSelectorRoles     = new int[totalActions];
            var rtActionAutoChangeGroupIds       = new int[totalActions];
            var rtActionAutoChangeIntervals      = new float[totalActions];
            var rtAutoChangeGroupRandom          = new bool[totalActions];
            var rtActionKeywords                 = new string[totalActions];
            var rtActionKeywordToggles           = new string[totalActions];
            var rtActionIsKeywordToggle          = new bool[totalActions];
            var rtActionNonStateful              = new bool[totalActions];
            var rtActionUseStep                  = new bool[totalActions];
            var rtActionAlwaysGate               = new int[totalActions];
            for (int i = 0; i < totalActions; i++)
            {
                rtActionAutoChangeGroupIds[i]  = -1;
                rtActionAutoChangeIntervals[i] = 10f;
                rtActionKeywords[i] = "";
                rtActionKeywordToggles[i] = "";
                rtActionAlwaysGate[i] = -1;
            }

            // ── Pre-pass: scoped material fixups (once per material) ──
            // Same rationale as the controller build's fixups pre-pass: only
            // reset the Mochie toggles this button actually manages.
            {
                var fixupProps = new Dictionary<Material, HashSet<string>>();
                foreach (var fpAct in actions)
                {
                    if (fpAct == null || fpAct.actionType != 2 || fpAct.targetRenderer == null
                        || string.IsNullOrEmpty(fpAct.propertyName)) continue;
                    var fpMats = fpAct.targetRenderer.sharedMaterials;
                    int fpMi = fpAct.materialIndex;
                    if (fpMats == null || fpMi < 0 || fpMi >= fpMats.Length || fpMats[fpMi] == null) continue;
                    HashSet<string> fpSet;
                    if (!fixupProps.TryGetValue(fpMats[fpMi], out fpSet))
                    {
                        fpSet = new HashSet<string>();
                        fixupProps[fpMats[fpMi]] = fpSet;
                    }
                    fpSet.Add(fpAct.propertyName);
                    if (WouldEmitSyntheticToggle(fpAct, out string fpTog, out _))
                        fpSet.Add(fpTog);
                }
                foreach (var fpKv in fixupProps)
                    EnigmaShaderHelper.ApplyMaterialFixups(fpKv.Key,
                        EnigmaShaderHelper.ComputeManagedToggles(fpKv.Key, fpKv.Value));
            }

            // ── Compile actions ──
            for (int idx = 0; idx < actions.Length; idx++)
            {
                var act = actions[idx];
                CompileAction(act, idx,
                    rtActionTypes, rtActionTargetObjects, rtActionTargetRenderers,
                    rtActionMaterialIndices, rtActionMaterials, rtActionDefaultMaterials, rtActionPropertyNames,
                    rtActionFloatValues, rtActionColorValues, rtActionVectorValues,
                    rtActionTextures,
                    rtActionDefaultFloatValues, rtActionDefaultColorValues, rtActionDefaultVectorValues,
                    rtActionPropertyTypes, rtActionUdonTargets,
                    rtActionUdonEventNames, rtActionUdonVariableNames,
                    rtActionUdonVariableTypes, rtActionUdonVariableStringValues);

                // Bake auto-detected shader keywords for type-2 actions.
                // Only when the action targets the toggle property itself.
                if (act.actionType == 2 && act.targetRenderer != null
                    && !string.IsNullOrEmpty(act.propertyName))
                {
                    var mats = act.targetRenderer.sharedMaterials;
                    int mi = act.materialIndex;
                    if (mats != null && mi >= 0 && mi < mats.Length && mats[mi] != null)
                    {
                        var kwInfo = EnigmaShaderHelper.GetPropertyKeywordInfo(mats[mi], act.propertyName);
                        if (kwInfo.keyword != null && kwInfo.toggleProp == act.propertyName)
                        {
                            // Value-aware: enum-mode toggles gate a different
                            // keyword per value (see GetKeywordForToggleValue).
                            string vkw = EnigmaShaderHelper.GetKeywordForToggleValue(
                                mats[mi], act.propertyName, act.propertyFloatValue);
                            rtActionKeywords[idx]       = vkw != null ? vkw : kwInfo.keyword;
                            rtActionKeywordToggles[idx] = kwInfo.toggleProp;
                            rtActionIsKeywordToggle[idx] = true;
                        }

                        // Mochie "Always" pass gate (_Zoom/_SST/_Letterbox).
                        // Independent of the keyword bake — _Letterbox has no
                        // keyword but still gates the pass.
                        rtActionAlwaysGate[idx] =
                            EnigmaShaderHelper.GetAlwaysPassGateId(mats[mi], act.propertyName);
                    }
                }

                if (act.category == 1) rtActionNonStateful[idx] = true;
                if (act.useStep) rtActionUseStep[idx] = true;
                if (act.actionType == 10) rtActionColorSelectorRoles[idx]  = act.colorSelectorRole;
                if (act.actionType == 19) rtActionVariantSelectorRoles[idx] = act.variantSelectorRole;
                rtActionDelaySeconds[idx]      = act.useDelay ? Mathf.Max(0f, act.delaySeconds) : 0f;
                rtActionDelayOnDeactivate[idx] = act.useDelay && act.delayOnDeactivate;
                if (act.actionType == 5 || act.actionType == 6)
                    rtActionUdonEventScopes[idx] = act.udonEventScope;
                if (act.actionType == 12 || act.actionType == 23)
                    rtActionTransformSpaces[idx] = act.transformSpace;
                if (act.actionType == 13)
                {
                    rtActionTeleportRotations[idx] = act.teleportRotationEuler;
                    if (act.propertyType == 4)
                        rtActionTeleportDestinations[idx] = act.teleportDestination;
                }
                if (act.actionType == 21)
                    rtActionStatMetrics[idx] = act.statMetric;
                if (act.actionType == 14 || act.actionType == 17)
                {
                    // Resolve autochange group name against the linked controller's tag map.
                    int resolvedId = -1;
                    string acgTag = act.autoChangeGroupName?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(acgTag) && btn.linkedController != null)
                    {
                        var tagNames = btn.linkedController.rtAutoChangeGroupTagNames;
                        if (tagNames != null)
                        {
                            for (int t = 0; t < tagNames.Length; t++)
                            {
                                if (tagNames[t] == acgTag) { resolvedId = t; break; }
                            }
                        }
                        if (resolvedId < 0)
                        {
                            string available = tagNames != null && tagNames.Length > 0
                                ? string.Join(", ", tagNames)
                                : "(none — has the controller been built?)";
                            Debug.LogWarning(
                                $"[EnigmaButton] Action #{idx}: autochange group tag \"{acgTag}\" " +
                                $"not found on linked controller. Available tags: {available}", btn);
                        }
                    }
                    else if (string.IsNullOrEmpty(acgTag))
                    {
                        Debug.LogWarning(
                            $"[EnigmaButton] Action #{idx}: autochange group tag is empty.", btn);
                    }
                    else if (btn.linkedController == null)
                    {
                        Debug.LogWarning(
                            $"[EnigmaButton] Action #{idx}: no linked controller — " +
                            $"autochange group \"{acgTag}\" cannot be resolved.", btn);
                    }
                    rtActionAutoChangeGroupIds[idx]       = resolvedId;
                    rtActionAutoChangeIntervals[idx]      = Mathf.Max(0.1f, act.autoChangeGroupInterval);
                    rtAutoChangeGroupRandom[idx]          = act.autoChangeGroupRandom;
                }
                if (act.useCondition)
                {
                    rtActionHasCondition[idx]           = true;
                    rtActionConditionRequireActive[idx] = act.conditionRequireActive;
                }
            }

            // ── Auto-toggle synthetic actions ──
            // Emit any "set the section toggle = 1" synthetic actions for type-2
            // actions that have alsoSetEffectToggle enabled and a detected toggle.
            // They go into the trailing slots (after the template actions) so the
            // existing template-index ↔ runtime-index mapping stays intact for the
            // cond-color and button-type loops below. Functionally identical to
            // adjacent insertion: both the parent and synthetic actions execute on
            // the same button press in the same frame, so order within the array
            // doesn't matter.
            int syntheticIdx = actions.Length;
            for (int i = 0; i < actions.Length; i++)
            {
                if (WouldEmitSyntheticToggle(actions[i], out string togProp, out Material togMat))
                {
                    EmitSyntheticToggleAction(
                        syntheticIdx, actions[i].targetRenderer, actions[i].materialIndex, togMat, togProp,
                        rtActionTypes, rtActionTargetRenderers, rtActionMaterialIndices,
                        rtActionPropertyNames, rtActionFloatValues, rtActionDefaultFloatValues,
                        rtActionPropertyTypes, rtActionNonStateful,
                        rtActionKeywords, rtActionKeywordToggles, rtActionIsKeywordToggle,
                        rtActionAlwaysGate);
                    syntheticIdx++;
                }
            }

            // ── Bake per-action conditional coloring (Display Value type 9) ──
            int totalCondColorRulesBtn = 0;
            for (int idx = 0; idx < actions.Length; idx++)
            {
                if (actions[idx].actionType == 9 && actions[idx].useConditionalColoring
                    && actions[idx].conditionalColorRules != null)
                    totalCondColorRulesBtn += actions[idx].conditionalColorRules.Length;
            }
            var btnCondColorStart      = new int[totalActions];
            var btnCondColorCount      = new int[totalActions];
            var btnCondColorConditions = new int[totalCondColorRulesBtn];
            var btnCondColorValues     = new float[totalCondColorRulesBtn];
            var btnCondColorColors     = new Color[totalCondColorRulesBtn];
            int btnCondIdx = 0;
            for (int idx = 0; idx < actions.Length; idx++)
            {
                if (actions[idx].actionType == 9 && actions[idx].useConditionalColoring
                    && actions[idx].conditionalColorRules != null
                    && actions[idx].conditionalColorRules.Length > 0)
                {
                    btnCondColorStart[idx] = btnCondIdx;
                    btnCondColorCount[idx] = actions[idx].conditionalColorRules.Length;
                    foreach (var rule in actions[idx].conditionalColorRules)
                    {
                        btnCondColorConditions[btnCondIdx] = rule.condition;
                        btnCondColorValues[btnCondIdx]     = rule.value;
                        btnCondColorColors[btnCondIdx]     = rule.color;
                        btnCondIdx++;
                    }
                }
            }

            // ── Derive runtime button type ──
            bool hasStep = false, hasColorCycle = false;
            bool hasToggleAction = false;
            foreach (var act in actions)
            {
                if (!hasStep      && act.useStep && (act.actionType == 2 || act.actionType == 6)) hasStep = true;
                if (!hasColorCycle && act.actionType == 7) hasColorCycle = true;
                if (IsStatefulAction(act.actionType, act.category))
                    hasToggleAction = true;
            }

            int rtButtonType;
            if (hasStep)
                rtButtonType = 2;
            else if (hasColorCycle)
                rtButtonType = 3;
            else if (hasToggleAction)
                rtButtonType = 0;
            else if (actions.Length > 0)
            {
                bool allDisplay = true;
                foreach (var act in actions)
                {
                    if (act.actionType != 9 && act.actionType != 21 && act.actionType != 24 && act.actionType != 25) { allDisplay = false; break; }
                }
                rtButtonType = allDisplay ? 4 : 1;
            }
            else
                rtButtonType = 4;

            // ── Write action arrays to executor component ──
            var executor = FindOrCreateExecutor(btn.gameObject);
            if (executor != null)
            {
                executor.linkedController = btn.linkedController;
                var exeSo = new SerializedObject(executor);
                ClearExecutorArrays(exeSo);
                WriteArray(exeSo, "rtActionTypes",                        rtActionTypes);
                WriteObjectArray(exeSo, "rtActionTargetObjects",          rtActionTargetObjects);
                WriteObjectArray(exeSo, "rtActionTargetRenderers",        rtActionTargetRenderers);
                WriteArray(exeSo, "rtActionMaterialIndices",              rtActionMaterialIndices);
                WriteObjectArray(exeSo, "rtActionMaterials",              rtActionMaterials);
                WriteObjectArray(exeSo, "rtActionDefaultMaterials",       rtActionDefaultMaterials);
                WriteArray(exeSo, "rtActionPropertyNames",                rtActionPropertyNames);
                WriteArray(exeSo, "rtActionFloatValues",                  rtActionFloatValues);
                WriteColorArray(exeSo, "rtActionColorValues",             rtActionColorValues);
                WriteVector4Array(exeSo, "rtActionVectorValues",          rtActionVectorValues);
                WriteObjectArray(exeSo, "rtActionTextures",               rtActionTextures);
                WriteArray(exeSo, "rtActionDefaultFloatValues",          rtActionDefaultFloatValues);
                WriteColorArray(exeSo, "rtActionDefaultColorValues",     rtActionDefaultColorValues);
                WriteVector4Array(exeSo, "rtActionDefaultVectorValues",  rtActionDefaultVectorValues);
                WriteArray(exeSo, "rtActionPropertyTypes",                rtActionPropertyTypes);
                WriteObjectArray(exeSo, "rtActionUdonTargets",            rtActionUdonTargets);
                WriteArray(exeSo, "rtActionUdonEventNames",               rtActionUdonEventNames);
                WriteArray(exeSo, "rtActionUdonVariableNames",            rtActionUdonVariableNames);
                WriteArray(exeSo, "rtActionUdonVariableTypes",            rtActionUdonVariableTypes);
                WriteArray(exeSo, "rtActionUdonVariableStringValues",     rtActionUdonVariableStringValues);
                WriteArray(exeSo, "rtActionDelaySeconds",                 rtActionDelaySeconds);
                WriteArray(exeSo, "rtActionDelayOnDeactivate",            rtActionDelayOnDeactivate);
                WriteArray(exeSo, "rtActionUdonEventScopes",              rtActionUdonEventScopes);
                WriteArray(exeSo, "rtActionTransformSpaces",              rtActionTransformSpaces);
                WriteVector3Array(exeSo, "rtActionTeleportRotations",     rtActionTeleportRotations);
                WriteObjectArray(exeSo, "rtActionTeleportDestinations",   rtActionTeleportDestinations);
                WriteArray(exeSo, "rtActionStatMetrics",                  rtActionStatMetrics);
                WriteArray(exeSo, "rtActionHasCondition",                 rtActionHasCondition);
                WriteArray(exeSo, "rtActionConditionRequireActive",       rtActionConditionRequireActive);
                WriteArray(exeSo, "rtActionColorSelectorRoles",           rtActionColorSelectorRoles);
                WriteArray(exeSo, "rtActionVariantSelectorRoles",         rtActionVariantSelectorRoles);
                WriteArray(exeSo, "rtActionKeywords",                     rtActionKeywords);
                WriteArray(exeSo, "rtActionKeywordToggles",               rtActionKeywordToggles);
                WriteArray(exeSo, "rtActionIsKeywordToggle",              rtActionIsKeywordToggle);
                WriteArray(exeSo, "rtActionNonStateful",                  rtActionNonStateful);
                WriteArray(exeSo, "rtActionUseStep",                      rtActionUseStep);
                WriteArray(exeSo, "rtActionAlwaysGate",                   rtActionAlwaysGate);
                WriteArray(exeSo, "rtActionAutoChangeGroupIds",           rtActionAutoChangeGroupIds);
                WriteArray(exeSo, "rtActionAutoChangeIntervals",          rtActionAutoChangeIntervals);
                WriteArray(exeSo, "rtAutoChangeGroupRandom",              rtAutoChangeGroupRandom);
                // Per-action conditional coloring
                WriteArray(exeSo, "rtCondColorStart",      btnCondColorStart);
                WriteArray(exeSo, "rtCondColorCount",      btnCondColorCount);
                WriteArray(exeSo, "rtCondColorConditions",  btnCondColorConditions);
                WriteArray(exeSo, "rtCondColorValues",      btnCondColorValues);
                WriteColorArray(exeSo, "rtCondColorColors",  btnCondColorColors);
                var exeLinkedProp = exeSo.FindProperty("linkedController");
                if (exeLinkedProp != null) exeLinkedProp.objectReferenceValue = btn.linkedController;
                exeSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(executor);
                UdonSharpEditorUtility.CopyProxyToUdon(executor, ProxySerializationPolicy.All);
            }

            // ── Write button-level data to button SerializedObject ──
            var rtButtonTypeProp = so.FindProperty("rtButtonType");
            if (rtButtonTypeProp != null) rtButtonTypeProp.intValue = rtButtonType;

            // Link the executor on the button
            var exeRefProp = so.FindProperty("executor");
            if (exeRefProp != null) exeRefProp.objectReferenceValue = executor;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(btn);
            UdonSharpEditorUtility.CopyProxyToUdon(btn, ProxySerializationPolicy.All);

            Debug.Log($"[EnigmaButton] Build complete — {totalActions} actions, buttonType={rtButtonType}.", btn);
        }

        /// <summary>
        /// Scene-wide exclusive-tag peer linkage pass.  Must be called after all
        /// EnigmaController and EnigmaButton individual builds are complete, because it
        /// reads <c>rtGroupTagNames</c> from already-built controllers.
        ///
        /// For every <see cref="EnigmaButton"/> with <c>useExclusiveGroup=true</c>:
        ///   • <c>rtExclusivePeerButtons</c>        — other buttons sharing any exclusive tag
        ///   • <c>rtExclusivePeerControllers</c>    — controllers whose entries share a tag
        ///   • <c>rtExclusivePeerControllerTags</c> — the tag to pass to each controller's
        ///                                            DeactivateExclusiveGroup
        ///
        /// For every <see cref="EnigmaController"/> with exclusive groups:
        ///   • <c>rtExclusiveButtonPeers</c>          — flat list of peer buttons
        ///   • <c>rtExclusiveButtonPeerGroupStart</c> — start index per group ID
        ///   • <c>rtExclusiveButtonPeerGroupCount</c> — count per group ID
        /// </summary>
        internal static void BuildExclusivePeerLinks(
            IEnumerable<EnigmaController> controllers,
            IEnumerable<EnigmaButton> buttons)
        {
            var ctrlList = new List<EnigmaController>(controllers);
            var btnList  = new List<EnigmaButton>(buttons);

            // ── Per-button: find peer buttons and peer controllers ─────────────────
            foreach (var btn in btnList)
            {
                if (!btn.useExclusiveGroup || string.IsNullOrEmpty(btn.exclusiveGroup))
                {
                    // Clear any stale peer data left from a previous build.
                    var soClear = new SerializedObject(btn);
                    WriteObjectArray(soClear, "rtExclusivePeerButtons",        new EnigmaButton[0]);
                    WriteObjectArray(soClear, "rtExclusivePeerControllers",    new EnigmaController[0]);
                    WriteArray      (soClear, "rtExclusivePeerControllerTags", new string[0]);
                    soClear.ApplyModifiedProperties();
                    EditorUtility.SetDirty(btn);
                    UdonSharpEditorUtility.CopyProxyToUdon(btn, ProxySerializationPolicy.All);
                    continue;
                }

                // Parse this button's comma-separated exclusive tags.
                var btnTags = new HashSet<string>();
                foreach (string rawTag in btn.exclusiveGroup.Split(','))
                {
                    string tag = rawTag.Trim();
                    if (!string.IsNullOrEmpty(tag)) btnTags.Add(tag);
                }

                // Peer buttons that share at least one tag.
                var peerButtons = new List<EnigmaButton>();
                foreach (var other in btnList)
                {
                    if (other == btn || !other.useExclusiveGroup
                        || string.IsNullOrEmpty(other.exclusiveGroup)) continue;
                    foreach (string rawTag in other.exclusiveGroup.Split(','))
                    {
                        if (btnTags.Contains(rawTag.Trim())) { peerButtons.Add(other); break; }
                    }
                }

                // Peer controllers — one entry per (controller, tag) pair.
                var peerControllers    = new List<EnigmaController>();
                var peerControllerTags = new List<string>();
                foreach (var ctrl in ctrlList)
                {
                    if (ctrl.rtGroupTagNames == null) continue;
                    foreach (string tag in btnTags)
                    {
                        for (int i = 0; i < ctrl.rtGroupTagNames.Length; i++)
                        {
                            if (ctrl.rtGroupTagNames[i] == tag)
                            {
                                peerControllers.Add(ctrl);
                                peerControllerTags.Add(tag);
                                break;
                            }
                        }
                    }
                }

                var so = new SerializedObject(btn);
                WriteObjectArray(so, "rtExclusivePeerButtons",        peerButtons.ToArray());
                WriteObjectArray(so, "rtExclusivePeerControllers",    peerControllers.ToArray());
                WriteArray      (so, "rtExclusivePeerControllerTags", peerControllerTags.ToArray());
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(btn);
                UdonSharpEditorUtility.CopyProxyToUdon(btn, ProxySerializationPolicy.All);
            }

            // ── Per-controller: find peer EnigmaButtons grouped by exclusive-group ID ──
            foreach (var ctrl in ctrlList)
            {
                if (ctrl.rtGroupTagNames == null || ctrl.rtGroupTagNames.Length == 0)
                {
                    var soClear = new SerializedObject(ctrl);
                    WriteObjectArray(soClear, "rtExclusiveButtonPeers",         new EnigmaButton[0]);
                    WriteArray      (soClear, "rtExclusiveButtonPeerGroupStart", new int[0]);
                    WriteArray      (soClear, "rtExclusiveButtonPeerGroupCount", new int[0]);
                    WriteArray      (soClear, "rtEntryExclusivePeerStart",       new int[0]);
                    WriteArray      (soClear, "rtEntryExclusivePeerCount",       new int[0]);
                    WriteArray      (soClear, "rtEntryExclusivePeerFlat",        new int[0]);
                    WriteArray      (soClear, "rtEntryExclusiveOffPeer",         new int[0]);
                    soClear.ApplyModifiedProperties();
                    EditorUtility.SetDirty(ctrl);
                    UdonSharpEditorUtility.CopyProxyToUdon(ctrl, ProxySerializationPolicy.All);
                    continue;
                }

                int numGroups    = ctrl.rtGroupTagNames.Length;
                var peersPerGroup = new List<EnigmaButton>[numGroups];
                for (int g = 0; g < numGroups; g++)
                    peersPerGroup[g] = new List<EnigmaButton>();

                foreach (var btn in btnList)
                {
                    if (!btn.useExclusiveGroup || string.IsNullOrEmpty(btn.exclusiveGroup)) continue;
                    foreach (string rawTag in btn.exclusiveGroup.Split(','))
                    {
                        string tag = rawTag.Trim();
                        if (string.IsNullOrEmpty(tag)) continue;
                        for (int g = 0; g < numGroups; g++)
                        {
                            if (ctrl.rtGroupTagNames[g] == tag && !peersPerGroup[g].Contains(btn))
                            {
                                peersPerGroup[g].Add(btn);
                                break;
                            }
                        }
                    }
                }

                var allPeers = new List<EnigmaButton>();
                var starts   = new int[numGroups];
                var counts   = new int[numGroups];
                for (int g = 0; g < numGroups; g++)
                {
                    starts[g] = allPeers.Count;
                    counts[g] = peersPerGroup[g].Count;
                    allPeers.AddRange(peersPerGroup[g]);
                }

                var so = new SerializedObject(ctrl);
                WriteObjectArray(so, "rtExclusiveButtonPeers",         allPeers.ToArray());
                WriteArray      (so, "rtExclusiveButtonPeerGroupStart", starts);
                WriteArray      (so, "rtExclusiveButtonPeerGroupCount", counts);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(ctrl);
                UdonSharpEditorUtility.CopyProxyToUdon(ctrl, ProxySerializationPolicy.All);
            }

            // ── Per-controller: build pre-baked entry peer lookup arrays ──────────
            foreach (var ctrl in ctrlList)
            {
                int totalEntries = ctrl.rtEntryExclusiveGroupCount != null ? ctrl.rtEntryExclusiveGroupCount.Length : 0;
                if (totalEntries == 0)
                {
                    // Clear peer arrays when no entries exist.
                    var soClear2 = new SerializedObject(ctrl);
                    WriteArray(soClear2, "rtEntryExclusivePeerStart", new int[0]);
                    WriteArray(soClear2, "rtEntryExclusivePeerCount", new int[0]);
                    WriteArray(soClear2, "rtEntryExclusivePeerFlat",  new int[0]);
                    WriteArray(soClear2, "rtEntryExclusiveOffPeer",   new int[0]);
                    soClear2.ApplyModifiedProperties();
                    EditorUtility.SetDirty(ctrl);
                    UdonSharpEditorUtility.CopyProxyToUdon(ctrl, ProxySerializationPolicy.All);
                    continue;
                }

                var peerLists = new List<int>[totalEntries];
                var offPeers = new int[totalEntries];
                for (int i = 0; i < totalEntries; i++) { peerLists[i] = new List<int>(); offPeers[i] = -1; }

                // For each pair of entries, check if they share any exclusive group
                for (int a = 0; a < totalEntries; a++)
                {
                    int aCount = ctrl.rtEntryExclusiveGroupCount[a];
                    if (aCount == 0) continue;
                    int aStart = ctrl.rtEntryExclusiveGroupStart[a];

                    for (int b = a + 1; b < totalEntries; b++)
                    {
                        int bCount = ctrl.rtEntryExclusiveGroupCount[b];
                        if (bCount == 0) continue;
                        int bStart = ctrl.rtEntryExclusiveGroupStart[b];

                        bool shared = false;
                        for (int g = aStart; g < aStart + aCount && !shared; g++)
                            for (int h = bStart; h < bStart + bCount && !shared; h++)
                                if (ctrl.rtEntryExclusiveGroupFlat[g] == ctrl.rtEntryExclusiveGroupFlat[h])
                                    shared = true;

                        if (shared)
                        {
                            peerLists[a].Add(b);
                            peerLists[b].Add(a);
                        }
                    }

                    // Find exclusive-off peer for this entry
                    if (ctrl.rtEntryExclusiveOff != null)
                    {
                        for (int p = 0; p < peerLists[a].Count; p++)
                        {
                            int peer = peerLists[a][p];
                            if (peer < ctrl.rtEntryExclusiveOff.Length && ctrl.rtEntryExclusiveOff[peer])
                            { offPeers[a] = peer; break; }
                        }
                    }
                }

                // Also handle legacy single-group path
                if (ctrl.rtEntryExclusiveGroup != null)
                {
                    for (int a = 0; a < totalEntries && a < ctrl.rtEntryExclusiveGroup.Length; a++)
                    {
                        int group = ctrl.rtEntryExclusiveGroup[a];
                        if (group < 0) continue;
                        for (int b = a + 1; b < totalEntries && b < ctrl.rtEntryExclusiveGroup.Length; b++)
                        {
                            if (ctrl.rtEntryExclusiveGroup[b] == group)
                            {
                                if (!peerLists[a].Contains(b)) peerLists[a].Add(b);
                                if (!peerLists[b].Contains(a)) peerLists[b].Add(a);
                            }
                        }
                        // Legacy exclusive-off
                        if (offPeers[a] < 0 && ctrl.rtEntryExclusiveOff != null)
                        {
                            for (int b = 0; b < totalEntries && b < ctrl.rtEntryExclusiveGroup.Length; b++)
                            {
                                if (b != a && ctrl.rtEntryExclusiveGroup[b] == group
                                    && b < ctrl.rtEntryExclusiveOff.Length && ctrl.rtEntryExclusiveOff[b])
                                { offPeers[a] = b; break; }
                            }
                        }
                    }
                }

                // Flatten into arrays
                var flat2 = new List<int>();
                var starts2 = new int[totalEntries];
                var counts2 = new int[totalEntries];
                for (int i = 0; i < totalEntries; i++)
                {
                    starts2[i] = flat2.Count;
                    counts2[i] = peerLists[i].Count;
                    flat2.AddRange(peerLists[i]);
                }

                // Write to controller
                var so2 = new SerializedObject(ctrl);
                WriteArray(so2, "rtEntryExclusivePeerStart", starts2);
                WriteArray(so2, "rtEntryExclusivePeerCount", counts2);
                WriteArray(so2, "rtEntryExclusivePeerFlat", flat2.ToArray());
                WriteArray(so2, "rtEntryExclusiveOffPeer", offPeers);
                so2.ApplyModifiedProperties();
                EditorUtility.SetDirty(ctrl);
                UdonSharpEditorUtility.CopyProxyToUdon(ctrl, ProxySerializationPolicy.All);
            }
        }

        /// <summary>
        /// Creates a SerializedObject wrapper for <paramref name="btn"/> and calls
        /// <see cref="BuildEnigmaButtonArrays"/>.
        /// </summary>
        internal static void RunBuildButton(EnigmaButton btn)
        {
            var so = new SerializedObject(btn);
            BuildEnigmaButtonArrays(so, btn);
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EnigmaPlayModeHook.ApplyDefaultMaterialStateForButton(btn);
        }
    }
}
#endif

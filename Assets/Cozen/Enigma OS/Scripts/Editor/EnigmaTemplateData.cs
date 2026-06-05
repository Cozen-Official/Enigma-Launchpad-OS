#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    // ════════════════════════════════════════════════════════════════════════════
    //  Template JSON schema
    //
    //  Templates are JSON files that describe a complete folder configuration.
    //  Users can import them via the "Import Template" button in the folder panel.
    //  Built-in templates are generated directly in code (see Templates.cs).
    //
    //  Example JSON:
    //  {
    //    "templateName": "Presets Folder",
    //    "folderName":   "Presets",
    //    "entries": [
    //      {
    //        "label":      "Preset 1",
    //        "buttonType": 0,
    //        "actions": [
    //          { "actionType": 8, "presetScope": 0, "presetIncludeFaders": true }
    //        ]
    //      }
    //    ]
    //  }
    // ════════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class EnigmaTemplateData
    {
        /// <summary>Human-readable template name shown in the picker.</summary>
        public string templateName = "";

        /// <summary>Default folder name when the template is applied.</summary>
        public string folderName = "New Folder";

        /// <summary>Entry definitions for this template.</summary>
        public List<EnigmaTemplateEntryData> entries = new List<EnigmaTemplateEntryData>();
    }

    [Serializable]
    public class EnigmaTemplateEntryData
    {
        // ── Empty-slot sentinel (preserves slot positions on import) ──
        public bool isEmpty = false;

        // ── Identity ──
        public string label       = "New Entry";
        public int    buttonType  = 0;
        public bool   onByDefault = false;

        // ── Exclusive group ──
        public bool   useExclusiveGroup = false;
        public string exclusiveGroup    = "";
        public bool   exclusiveOff      = false;

        // ── Autochange group ──
        public bool   useAutoChangeGroup = false;
        public string autoChangeGroup    = "";

        // ── Fader assignment ──
        public bool                      assignFader = false;
        public EnigmaTemplateFaderLinkData faderLink = new EnigmaTemplateFaderLinkData();
        public List<EnigmaTemplateFaderLinkData> faderLinks = new List<EnigmaTemplateFaderLinkData>();

        // ── Custom color ──
        public bool   useCustomColor       = false;
        public Color  customColor          = Color.white;
        public bool   useConditionalColor  = false;
        public int    condColorSourceType  = 0;
        public int    condColorMaterialIndex = 0;
        public string condColorPropertyName = "";
        public bool   condColorTargetsSkybox = false;
        public string condColorUdonVariableName = "";
        public List<TemplateConditionalColorRule> condColorRules = new List<TemplateConditionalColorRule>();

        // ── Actions ──
        public List<EnigmaTemplateActionData> actions = new List<EnigmaTemplateActionData>();

        public static EnigmaTemplateEntryData FromEntryData(EnigmaEntryData entry)
            => FromEntryData(entry, includeAssetPaths: true);

        // Overload: when includeAssetPaths is false, every asset-path field on
        // child actions / variant items is emitted as "" — the resulting JSON
        // still loads cleanly but imports into a different project won't drag
        // any asset references along. Scene-object references (targetRenderer,
        // targetObject, targetUdon, fader-link targetRenderer) are already
        // excluded regardless of this flag.
        public static EnigmaTemplateEntryData FromEntryData(EnigmaEntryData entry, bool includeAssetPaths)
        {
            var te = new EnigmaTemplateEntryData
            {
                isEmpty           = entry.isEmpty,
                label             = entry.label,
                buttonType        = entry.buttonType,
                onByDefault       = entry.onByDefault,
                useExclusiveGroup = entry.useExclusiveGroup,
                exclusiveGroup    = entry.exclusiveGroup ?? "",
                exclusiveOff      = entry.exclusiveOff,
                useAutoChangeGroup = entry.useAutoChangeGroup,
                autoChangeGroup   = entry.autoChangeGroup ?? "",
                assignFader       = entry.assignFader,
                faderLink         = entry.faderLink != null
                                    ? EnigmaTemplateFaderLinkData.FromFaderLinkData(entry.faderLink)
                                    : new EnigmaTemplateFaderLinkData(),
                useCustomColor       = entry.useCustomColor,
                customColor          = entry.customColor,
                useConditionalColor  = entry.useConditionalColor,
                condColorSourceType  = entry.condColorSourceType,
                condColorMaterialIndex = entry.condColorMaterialIndex,
                condColorPropertyName = entry.condColorPropertyName ?? "",
                condColorTargetsSkybox = entry.condColorTargetsSkybox,
                condColorUdonVariableName = entry.condColorUdonVariableName ?? "",
            };

            if (entry.faderLinks != null)
                foreach (var fl in entry.faderLinks)
                    if (fl != null) te.faderLinks.Add(EnigmaTemplateFaderLinkData.FromFaderLinkData(fl));

            if (entry.condColorRules != null)
                foreach (var cr in entry.condColorRules)
                    te.condColorRules.Add(TemplateConditionalColorRule.FromRule(cr));

            if (entry.actions != null)
            {
                foreach (var act in entry.actions)
                    te.actions.Add(EnigmaTemplateActionData.FromActionData(act, includeAssetPaths));
            }
            return te;
        }

        /// <summary>
        /// Convert this template entry into an EnigmaEntryData instance.
        /// Scene-object references (targetObject, targetRenderer, etc.) are left
        /// null; the user fills them in after import.
        /// </summary>
        public EnigmaEntryData ToEntryData()
        {
            var entry = new EnigmaEntryData
            {
                isEmpty           = isEmpty,
                label             = label,
                buttonType        = buttonType,
                onByDefault       = onByDefault,
                useExclusiveGroup = useExclusiveGroup,
                exclusiveGroup    = exclusiveGroup,
                exclusiveOff      = exclusiveOff,
                useAutoChangeGroup = useAutoChangeGroup,
                autoChangeGroup   = autoChangeGroup,
                assignFader       = assignFader,
                faderLink         = faderLink != null
                                    ? faderLink.ToFaderLinkData()
                                    : new EnigmaFaderLinkData(),
                useCustomColor       = useCustomColor,
                customColor          = customColor,
                useConditionalColor  = useConditionalColor,
                condColorSourceType  = condColorSourceType,
                condColorMaterialIndex = condColorMaterialIndex,
                condColorPropertyName = condColorPropertyName ?? "",
                condColorTargetsSkybox = condColorTargetsSkybox,
                condColorUdonVariableName = condColorUdonVariableName ?? "",
            };

            if (condColorRules != null && condColorRules.Count > 0)
            {
                entry.condColorRules = new ConditionalColorRule[condColorRules.Count];
                for (int i = 0; i < condColorRules.Count; i++)
                    entry.condColorRules[i] = condColorRules[i].ToRule();
            }

            if (faderLinks != null && faderLinks.Count > 0)
            {
                entry.faderLinks = new EnigmaFaderLinkData[faderLinks.Count];
                for (int i = 0; i < faderLinks.Count; i++)
                    entry.faderLinks[i] = faderLinks[i] != null ? faderLinks[i].ToFaderLinkData() : new EnigmaFaderLinkData();
            }

            var actionList = new List<EnigmaActionData>();
            foreach (var act in actions)
                actionList.Add(act.ToActionData());
            entry.actions = actionList.ToArray();

            return entry;
        }
    }

    // ─── Variant item (mirrors EnigmaVariantItem; preserves Texture as an asset path) ───
    [Serializable]
    public class EnigmaTemplateVariantItem
    {
        public string  variantName     = "Variant";
        public float   floatValue      = 0f;
        public Color   colorValue      = Color.white;
        public Vector4 vectorValue     = Vector4.zero;
        // Asset path for the texture variant (e.g. "Assets/Materials/sky.png").
        // Populated on export; resolved back to a Texture on import.
        public string  textureValuePath = "";

        public static EnigmaTemplateVariantItem FromVariantItem(EnigmaVariantItem v)
        {
            return FromVariantItem(v, includeAssetPaths: true);
        }

        // Overload: pass includeAssetPaths=false to strip the texture asset path.
        public static EnigmaTemplateVariantItem FromVariantItem(EnigmaVariantItem v, bool includeAssetPaths)
        {
            string texPath = "";
            if (includeAssetPaths && v.textureValue != null)
                texPath = AssetDatabase.GetAssetPath(v.textureValue) ?? "";

            return new EnigmaTemplateVariantItem
            {
                variantName      = v.variantName ?? "Variant",
                floatValue       = v.floatValue,
                colorValue       = v.colorValue,
                vectorValue      = v.vectorValue,
                textureValuePath = texPath,
            };
        }

        public EnigmaVariantItem ToVariantItem()
        {
            Texture tex = null;
            if (!string.IsNullOrEmpty(textureValuePath))
                tex = AssetDatabase.LoadAssetAtPath<Texture>(textureValuePath);

            return new EnigmaVariantItem
            {
                variantName  = variantName,
                floatValue   = floatValue,
                colorValue   = colorValue,
                vectorValue  = vectorValue,
                textureValue = tex,
            };
        }
    }

    // ─── Fader link config (mirrors EnigmaFaderLinkData; Renderer ref is not JSON-serialisable) ───
    [Serializable]
    public class EnigmaTemplateFaderLinkData
    {
        public string faderName              = "";
        public int    materialIndex          = 0;
        public string propertyName           = "";
        public int    propertyType           = 0;
        public float  minValue               = 0f;
        public float  maxValue               = 1f;
        public float  defaultValue           = 0f;
        public Color  defaultColor           = Color.white;
        public bool   colorIndicatorEnabled  = false;
        public Color  indicatorColor         = Color.white;
        public bool   indicatorConditional   = false;
        public bool   targetsSkybox          = false;
        // Preserves the fader↔action pairing (shared id) across export/import.
        // When non-zero on import, the importer can match this fader link to an
        // action with the same faderLinkId and auto-populate the fader's
        // targetRenderer from the action's — avoiding a redundant user slot.
        public int    faderLinkId            = 0;
        // targetRenderer intentionally omitted — scene references cannot be stored in JSON

        public static EnigmaTemplateFaderLinkData FromFaderLinkData(EnigmaFaderLinkData f) =>
            new EnigmaTemplateFaderLinkData
            {
                faderName             = f.faderName ?? "",
                materialIndex         = f.materialIndex,
                propertyName          = f.propertyName ?? "",
                propertyType          = f.propertyType,
                minValue              = f.minValue,
                maxValue              = f.maxValue,
                defaultValue          = f.defaultValue,
                defaultColor          = f.defaultColor,
                colorIndicatorEnabled = f.colorIndicatorEnabled,
                indicatorColor        = f.indicatorColor,
                indicatorConditional  = f.indicatorConditional,
                targetsSkybox         = f.targetsSkybox,
                faderLinkId           = f.faderLinkId,
            };

        public EnigmaFaderLinkData ToFaderLinkData() =>
            new EnigmaFaderLinkData
            {
                faderName             = faderName,
                targetRenderer        = null,   // user must reassign after import
                materialIndex         = materialIndex,
                propertyName          = propertyName,
                propertyType          = propertyType,
                minValue              = minValue,
                maxValue              = maxValue,
                defaultValue          = defaultValue,
                defaultColor          = defaultColor,
                colorIndicatorEnabled = colorIndicatorEnabled,
                indicatorColor        = indicatorColor,
                indicatorConditional  = indicatorConditional,
                targetsSkybox         = targetsSkybox,
                faderLinkId           = faderLinkId,
            };
    }

    // ─── Conditional color rule (mirrors ConditionalColorRule) ───
    [Serializable]
    public class TemplateConditionalColorRule
    {
        public int   condition = 1;  // 0=<, 1=>, 2==, 3=≤, 4=≥
        public float value     = 0f;
        public Color color     = Color.white;

        public static TemplateConditionalColorRule FromRule(ConditionalColorRule r) =>
            new TemplateConditionalColorRule { condition = r.condition, value = r.value, color = r.color };

        public ConditionalColorRule ToRule() =>
            new ConditionalColorRule { condition = condition, value = value, color = color };
    }

    [Serializable]
    public class EnigmaTemplateActionData
    {
        // ── Action identity ──
        public int actionType = 0;

        // ── Action model (category / target / operation) ──
        public int category  =  0;
        public int target    =  0;
        public int operation =  0;

        // ── Generic shader-property / Udon variable fields ──
        public int     materialIndex       = 0;
        public string  propertyName        = "";
        public float   propertyFloatValue  = 0f;
        public Color   propertyColorValue  = Color.white;
        public Vector4 propertyVectorValue = Vector4.zero;
        public int     propertyType        = 0;

        // ── Default values (shader property revert state when action is deactivated) ──
        public float   defaultFloatValue   = 0f;
        public Color   defaultColorValue   = Color.white;
        public Vector4 defaultVectorValue  = Vector4.zero;

        // ── Auto-toggle (type 2 — Set Shader Property) ──
        // When true, the build pipeline emits a synthetic second action that
        // sets the auto-detected section toggle property (e.g. _FilterModel = 1).
        // See EnigmaActionData.alsoSetEffectToggle for full description.
        public bool alsoSetEffectToggle = true;

        // ── Asset references (stored as project-relative paths for portability) ──
        // On export: populated via AssetDatabase.GetAssetPath().
        // On import: resolved via AssetDatabase.LoadAssetAtPath(); null when missing.
        public string targetMaterialPath = "";
        public string targetTexturePath  = "";

        // ── Step (types 2 / 6) ──
        public bool  useStep    = false;
        public float stepAmount = 0.1f;
        public float stepMin    = 0f;
        public float stepMax    = 1f;
        public bool  stepWrap   = false;

        // ── Delay ──
        public bool  useDelay     = false;
        public float delaySeconds = 1f;

        // ── Udon (types 5 / 6) ──
        public string udonEventName         = "";
        public string udonVariableName      = "";
        public int    udonVariableType      = 1;
        public string udonVariableStringValue = "";
        public int    udonEventScope        = 0;

        // ── Transform / Teleport (types 11 / 12) ──
        public int     transformSpace       = 0;
        public Vector3 teleportRotationEuler = Vector3.zero;

        // ── Color Cycle / Color Selector ──
        public string  colorPropertyName  = "";
        public int     colorMaterialIndex = 0;
        public Color[] paletteColors      = new Color[0];

        // ── Color Selector (type 10) ──
        public int    colorSelectorRole = 0;
        public string colorGroupName    = "";

        // ── Presets (type 8) ──
        public int    presetRole              = 0;
        public int    presetScope             = 0;
        public int[]  presetIncludedFolderIndices = new int[0];
        public bool   presetIncludeFaders        = true;
        public bool   presetIncludeStepValues    = false;
        public bool   presetIncludeColorPalettes = false;
        public bool   presetIncludeVariantGroups = true;

        // ── Autochange group (types 14 / 17) ──
        public string autoChangeGroupName      = "";
        public float  autoChangeGroupInterval  = 10f;
        public bool   autoChangeGroupRandom    = false;

        // ── Navigation (type 20) ──
        public int navFolderTarget     = 0;
        public int navPageTarget       = 0;
        public int navFaderPageTarget  = 0;

        // ── Variant Selector (type 19) ──
        public int    variantSelectorRole = 0;
        public string variantGroupName    = "";
        public List<EnigmaTemplateVariantItem> variantItems = new List<EnigmaTemplateVariantItem>();

        // ── Command / SetState (types 15–18) ──
        public bool commandTargetState = true;

        // ── Momentary ──
        public bool useMomentary = false;

        // ── Condition ──
        public bool useCondition           = false;
        public int  conditionFolderIndex   = 0;
        public int  conditionEntryIndex    = 0;
        public bool conditionRequireActive = true;

        // ── Screen Shader (type 26) ──
        public int shaderTemplateIndex = 1;

        // ── Display Stat (type 21) ──
        public int statMetric = 0;

        // ── Conditional Coloring (type 9 — Display Value) ──
        public bool useConditionalColoring = false;
        public List<TemplateConditionalColorRule> conditionalColorRules = new List<TemplateConditionalColorRule>();

        // ── Fader link pairing ──
        // Preserves the action↔fader pairing so the importer can auto-populate
        // a linked fader link's targetRenderer from this action's targetRenderer
        // without asking the user for a redundant scene reference.
        public int faderLinkId = 0;

        public static EnigmaTemplateActionData FromActionData(EnigmaActionData act)
            => FromActionData(act, includeAssetPaths: true);

        // Overload: when includeAssetPaths is false, targetMaterialPath /
        // targetTexturePath / variantItem.textureValuePath are all emitted as
        // "" so the template carries no project-specific asset references.
        public static EnigmaTemplateActionData FromActionData(EnigmaActionData act, bool includeAssetPaths)
        {
            string matPath = "";
            if (includeAssetPaths && act.targetMaterial != null)
                matPath = AssetDatabase.GetAssetPath(act.targetMaterial) ?? "";

            string texPath = "";
            if (includeAssetPaths && act.targetTexture != null)
                texPath = AssetDatabase.GetAssetPath(act.targetTexture) ?? "";

            var ta = new EnigmaTemplateActionData
            {
                actionType             = act.actionType,
                category               = act.category,
                target                 = act.target,
                operation              = act.operation,
                materialIndex          = act.materialIndex,
                propertyName           = act.propertyName ?? "",
                propertyFloatValue     = act.propertyFloatValue,
                propertyColorValue     = act.propertyColorValue,
                propertyVectorValue    = act.propertyVectorValue,
                propertyType           = act.propertyType,
                defaultFloatValue      = act.defaultFloatValue,
                defaultColorValue      = act.defaultColorValue,
                defaultVectorValue     = act.defaultVectorValue,
                alsoSetEffectToggle    = act.alsoSetEffectToggle,
                targetMaterialPath     = matPath,
                targetTexturePath      = texPath,
                useStep                = act.useStep,
                stepAmount             = act.stepAmount,
                stepMin                = act.stepMin,
                stepMax                = act.stepMax,
                stepWrap               = act.stepWrap,
                useDelay               = act.useDelay,
                delaySeconds           = act.delaySeconds,
                udonEventName          = act.udonEventName ?? "",
                udonVariableName       = act.udonVariableName ?? "",
                udonVariableType       = act.udonVariableType,
                udonVariableStringValue = act.udonVariableStringValue ?? "",
                udonEventScope         = act.udonEventScope,
                transformSpace         = act.transformSpace,
                teleportRotationEuler  = act.teleportRotationEuler,
                colorPropertyName      = act.colorPropertyName ?? "",
                colorMaterialIndex     = act.colorMaterialIndex,
                paletteColors          = act.paletteColors ?? new Color[0],
                colorSelectorRole      = act.colorSelectorRole,
                colorGroupName         = act.colorGroupName ?? "",
                presetRole             = act.presetRole,
                presetScope            = act.presetScope,
                presetIncludedFolderIndices = act.presetIncludedFolderIndices ?? new int[0],
                presetIncludeFaders        = act.presetIncludeFaders,
                presetIncludeStepValues    = act.presetIncludeStepValues,
                presetIncludeColorPalettes = act.presetIncludeColorPalettes,
                presetIncludeVariantGroups = act.presetIncludeVariantGroups,
                autoChangeGroupName     = act.autoChangeGroupName ?? "",
                autoChangeGroupInterval = act.autoChangeGroupInterval,
                autoChangeGroupRandom   = act.autoChangeGroupRandom,
                navFolderTarget        = act.navFolderTarget,
                navPageTarget          = act.navPageTarget,
                navFaderPageTarget     = act.navFaderPageTarget,
                variantSelectorRole    = act.variantSelectorRole,
                variantGroupName       = act.variantGroupName ?? "",
                commandTargetState     = act.commandTargetState,
                useMomentary           = act.useMomentary,
                useCondition           = act.useCondition,
                conditionFolderIndex   = act.conditionFolderIndex,
                conditionEntryIndex    = act.conditionEntryIndex,
                conditionRequireActive = act.conditionRequireActive,
                statMetric             = act.statMetric,
                shaderTemplateIndex    = act.shaderTemplateIndex,
                useConditionalColoring = act.useConditionalColoring,
                faderLinkId            = act.faderLinkId,
            };

            if (act.variantItems != null)
                foreach (var vi in act.variantItems)
                    ta.variantItems.Add(EnigmaTemplateVariantItem.FromVariantItem(vi, includeAssetPaths));

            if (act.conditionalColorRules != null)
                foreach (var cr in act.conditionalColorRules)
                    ta.conditionalColorRules.Add(TemplateConditionalColorRule.FromRule(cr));

            return ta;
        }

        public EnigmaActionData ToActionData()
        {
            // Resolve asset-path references. If the asset doesn't exist in this
            // project the field is left null (same behaviour as before the feature).
            Material mat = null;
            if (!string.IsNullOrEmpty(targetMaterialPath))
                mat = AssetDatabase.LoadAssetAtPath<Material>(targetMaterialPath);

            Texture tex = null;
            if (!string.IsNullOrEmpty(targetTexturePath))
                tex = AssetDatabase.LoadAssetAtPath<Texture>(targetTexturePath);

            var act = new EnigmaActionData
            {
                actionType             = actionType,
                category               = category,
                target                 = target,
                operation              = operation,
                materialIndex          = materialIndex,
                propertyName           = propertyName,
                propertyFloatValue     = propertyFloatValue,
                propertyColorValue     = propertyColorValue,
                propertyVectorValue    = propertyVectorValue,
                propertyType           = propertyType,
                defaultFloatValue      = defaultFloatValue,
                defaultColorValue      = defaultColorValue,
                defaultVectorValue     = defaultVectorValue,
                alsoSetEffectToggle    = alsoSetEffectToggle,
                targetMaterial         = mat,
                targetTexture          = tex,
                useStep                = useStep,
                stepAmount             = stepAmount,
                stepMin                = stepMin,
                stepMax                = stepMax,
                stepWrap               = stepWrap,
                useDelay               = useDelay,
                delaySeconds           = delaySeconds,
                udonEventName          = udonEventName,
                udonVariableName       = udonVariableName,
                udonVariableType       = udonVariableType,
                udonVariableStringValue = udonVariableStringValue,
                udonEventScope         = udonEventScope,
                transformSpace         = transformSpace,
                teleportRotationEuler  = teleportRotationEuler,
                colorPropertyName      = colorPropertyName,
                colorMaterialIndex     = colorMaterialIndex,
                paletteColors          = paletteColors ?? new Color[0],
                colorSelectorRole      = colorSelectorRole,
                colorGroupName         = colorGroupName ?? "",
                presetRole             = presetRole,
                presetScope            = presetScope,
                presetIncludedFolderIndices = presetIncludedFolderIndices ?? new int[0],
                presetIncludeFaders        = presetIncludeFaders,
                presetIncludeStepValues    = presetIncludeStepValues,
                presetIncludeColorPalettes = presetIncludeColorPalettes,
                presetIncludeVariantGroups = presetIncludeVariantGroups,
                autoChangeGroupName     = autoChangeGroupName,
                autoChangeGroupInterval = autoChangeGroupInterval,
                autoChangeGroupRandom   = autoChangeGroupRandom,
                navFolderTarget        = navFolderTarget,
                navPageTarget          = navPageTarget,
                navFaderPageTarget     = navFaderPageTarget,
                variantSelectorRole    = variantSelectorRole,
                variantGroupName       = variantGroupName ?? "",
                commandTargetState     = commandTargetState,
                useMomentary           = useMomentary,
                useCondition           = useCondition,
                conditionFolderIndex   = conditionFolderIndex,
                conditionEntryIndex    = conditionEntryIndex,
                conditionRequireActive = conditionRequireActive,
                statMetric             = statMetric,
                shaderTemplateIndex    = shaderTemplateIndex,
                useConditionalColoring = useConditionalColoring,
                faderLinkId            = faderLinkId,
            };

            if (variantItems != null)
            {
                act.variantItems = new EnigmaVariantItem[variantItems.Count];
                for (int i = 0; i < variantItems.Count; i++)
                    act.variantItems[i] = variantItems[i].ToVariantItem();
            }

            if (conditionalColorRules != null && conditionalColorRules.Count > 0)
            {
                act.conditionalColorRules = new ConditionalColorRule[conditionalColorRules.Count];
                for (int i = 0; i < conditionalColorRules.Count; i++)
                    act.conditionalColorRules[i] = conditionalColorRules[i].ToRule();
            }

            // If the category/target/operation triple is invalid (e.g. from an older
            // template export), derive them from the actionType so the header label
            // displays correctly.
            if (act.category < 0
                || EnigmaActionListDrawer.GetActionLabel(act.category, act.target, act.operation)
                       .StartsWith("Action ("))
            {
                SyncCategoryFromActionType(act);
            }

            return act;
        }

        /// <summary>
        /// Reverse-sync: derives category/target/operation from actionType.
        /// Used when importing templates that have stale or missing model fields.
        /// </summary>
        private static void SyncCategoryFromActionType(EnigmaActionData act)
        {
            switch (act.actionType)
            {
                case  0: act.category = 0; act.target =  0; act.operation = 0; break; // Toggle Object
                case  1: act.category = 0; act.target =  3; act.operation = 0; break; // Set Material
                case  2: act.category = 0; act.target =  4; act.operation = 2; break; // Set Shader Property
                // case 3: removed (Toggle Renderer — use Toggle Component instead)
                case  4: act.category = 1; act.target =  5; act.operation = 0; break; // Apply Skybox
                case  5: act.category = 1; act.target =  6; act.operation = 5; break; // Trigger Udon Event
                case  6: act.category = 0; act.target =  6; act.operation = 0; break; // Set Udon Variable
                case  9: act.category = 4; act.target =  4; act.operation = 0; break; // Display Value
                case 10: act.category = 2; act.target =  9; act.operation = 9; break; // Color Selector
                case 11: act.category = 0; act.target =  2; act.operation = 0; break; // Toggle Component
                case 12: act.category = 1; act.target =  7; act.operation = 0; break; // Set Transform
                case 13: act.category = 1; act.target =  8; act.operation = 6; break; // Teleport Player
                case 14: act.category = 0; act.target = 11; act.operation = 0; break; // Toggle Autochange
                case 19: act.category = 2; act.target = 10; act.operation = 9; break; // Variant Selector
                case 20: act.category = 5; act.target =  0; act.operation = 0; break; // Nav Embedded
                case 21: act.category = 4; act.target = 14; act.operation = 0; break; // Display Stat
                case 22: act.category = 0; act.target =  5; act.operation = 0; break; // Toggle Skybox
                case 23: act.category = 0; act.target =  7; act.operation = 0; break; // Toggle Transform
                case 26: act.category = 0; act.target = 15; act.operation = 0; break; // Screen Shader
                case 27: act.category = 0; act.target =  4; act.operation = 3; break; // Toggle Shader Keyword
            }
        }
    }
}
#endif

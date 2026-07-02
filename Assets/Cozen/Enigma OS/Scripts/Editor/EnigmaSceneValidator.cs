#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Scene-wide validation checks that span all EnigmaControllers and
    /// EnigmaButtons. Called after every build pass to catch per-entry,
    /// per-controller, and cross-entity configuration issues.
    /// </summary>
    internal static class EnigmaSceneValidator
    {
        /// <summary>
        /// Returns true if the GameObject or any ancestor is tagged "EditorOnly".
        /// Editor-only objects are stripped at build time and should be skipped
        /// during validation.
        /// </summary>
        private static bool IsEditorOnly(GameObject go)
        {
            var t = go.transform;
            while (t != null)
            {
                if (t.CompareTag("EditorOnly")) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Runs all validation checks across the given scene.
        /// Called from <see cref="EnigmaPlayModeHook.RebuildAllControllers"/>
        /// after all individual builds complete.
        /// </summary>
        internal static void ValidateScene(Scene scene)
        {
            if (!scene.isLoaded) return;

            var warnings = new StringBuilder();

            ValidateControllerEntries(scene, warnings);
            ValidateButtonActions(scene, warnings);
            ValidatePerControllerChecks(scene, warnings);
            ValidateExclusiveGroups(scene, warnings);
            ValidateOverlappingStatefulTargets(scene, warnings);
            ValidateShaderPropertyDefaults(scene, warnings);
            ValidateFaderSlotCapacity(scene, warnings);
            ValidateSkyboxFaderConflict(scene, warnings);
            ValidateMochieLookalikes(scene, warnings);
            ValidateAudioLinkControllerCount(scene);

            if (warnings.Length > 0)
                Debug.LogWarning($"[EnigmaOS] Scene validation warnings:\n{warnings}");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MOCHIE LOOKALIKE DETECTION
        //  All Mochie-specific handling (Always-pass gating, keyword sync,
        //  baseline fixups, value-aware keyword resolution) is keyed on the
        //  EXACT shader name "Mochie/Screen FX (X)". A duplicated or renamed
        //  copy of the shader silently loses all of it — overlay/zoom/letterbox
        //  buttons appear to work in the editor preview (where variants compile
        //  on demand) but the Always pass never toggles in uploaded worlds.
        //  Warn so the user understands why a renamed copy behaves differently.
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateMochieLookalikes(Scene scene, StringBuilder warnings)
        {
            var seen = new HashSet<Material>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    foreach (var folder in folders)
                    {
                        if (folder.entries == null) continue;
                        foreach (var entry in folder.entries)
                        {
                            if (entry.isEmpty || entry.actions == null) continue;
                            foreach (var act in entry.actions)
                            {
                                if (act == null || act.actionType != 2 || act.targetRenderer == null) continue;
                                var mats = act.targetRenderer.sharedMaterials;
                                int mi = act.materialIndex;
                                if (mats == null || mi < 0 || mi >= mats.Length || mats[mi] == null) continue;
                                var mat = mats[mi];
                                if (!seen.Add(mat) || mat.shader == null) continue;

                                // Fingerprint: the three Always-pass gate properties
                                // together are unique to Mochie Screen FX.
                                if (!EnigmaShaderHelper.IsMochieScreenFX(mat.shader.name)
                                    && mat.HasProperty("_SST")
                                    && mat.HasProperty("_ScreenTex")
                                    && mat.HasProperty("_Letterbox"))
                                {
                                    warnings.AppendLine(
                                        $"- Material '{mat.name}' uses shader '{mat.shader.name}', which looks like " +
                                        "a renamed/duplicated copy of Mochie Screen FX. Enigma's Mochie handling " +
                                        "(Always-pass gating for Zoom/Overlay/Letterbox, keyword sync, off-state " +
                                        "baselines) matches the exact shader name 'Mochie/Screen FX (X)' and will " +
                                        "NOT apply to this copy — those effects may render wrong or not at all in " +
                                        "uploaded worlds. Use the original Mochie shader, or rename the copy back.");
                                }
                            }
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PER-ENTRY CHECKS (controllers)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateControllerEntries(Scene scene, StringBuilder warnings)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    string ctrlName = ctrl.gameObject.name;

                    for (int f = 0; f < folders.Length; f++)
                    {
                        var folder = folders[f];
                        for (int e = 0; e < folder.entries.Length; e++)
                        {
                            var entry = folder.entries[e];
                            if (entry.isEmpty) continue;
                            string loc = $"'{ctrlName}' > Folder '{folder.name}', Entry '{entry.label}'";

                            // Missing action targets
                            if (entry.actions != null)
                            {
                                ValidateActionTargets(entry.actions, loc, warnings);

                                // Condition reference validation (controller-only)
                                foreach (var action in entry.actions)
                                {
                                    if (!action.useCondition) continue;
                                    int fi = action.conditionFolderIndex;
                                    int ei2 = action.conditionEntryIndex;
                                    bool valid = false;
                                    if (fi >= 0 && fi < folders.Length)
                                    {
                                        var targetFolder = folders[fi];
                                        if (ei2 >= 0 && ei2 < targetFolder.entries.Length
                                            && !targetFolder.entries[ei2].isEmpty)
                                            valid = true;
                                    }
                                    if (!valid)
                                    {
                                        warnings.AppendLine(
                                            $"• {loc}: Conditional action references folder {fi}, entry {ei2} which " +
                                            "does not exist or is empty. The condition will always evaluate to false.");
                                    }
                                }

                                // Action ordering checks
                                ValidateActionOrdering(entry.actions, loc, warnings);
                            }

                            // Fader link with no active renderer
                            if (entry.assignFader && entry.faderLinks != null)
                            {
                                for (int fl = 0; fl < entry.faderLinks.Length; fl++)
                                {
                                    var link = entry.faderLinks[fl];
                                    if (link == null) continue;
                                    // Skip linked faders (renderer comes from action), skybox faders, udon faders, and slider faders
                                    if (link.faderLinkId != 0 || link.targetsSkybox || link.targetsUdon || link.targetsSlider) continue;
                                    if (link.targetRenderer == null)
                                        warnings.AppendLine($"• {loc}: Dynamic Fader {fl + 1} — targetRenderer is null (fader will always be unbound).");
                                }
                            }
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PER-ACTION CHECKS (standalone buttons)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateButtonActions(Scene scene, StringBuilder warnings)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                {
                    if (IsEditorOnly(btn.gameObject)) continue;
                    var actionsHolder = btn.GetComponent<EnigmaButtonActions>();
                    if (actionsHolder == null || actionsHolder.actions == null) continue;
                    string loc = $"Button '{btn.gameObject.name}'";

                    ValidateActionTargets(actionsHolder.actions, loc, warnings);
                    ValidateActionOrdering(actionsHolder.actions, loc, warnings);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHARED ACTION TARGET VALIDATION
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateActionTargets(EnigmaActionData[] actions, string loc, StringBuilder warnings)
        {
            foreach (var action in actions)
            {
                if (action == null) continue;

                if (action.actionType == 0 && action.targetObject == null)
                    warnings.AppendLine($"• {loc}: Toggle Object — targetObject is null.");
                if ((action.actionType == 1 || action.actionType == 2 || action.actionType == 27)
                    && action.targetRenderer == null)
                    warnings.AppendLine($"• {loc}: action type {action.actionType} — targetRenderer is null.");
                if (action.actionType == 5 && action.targetUdon == null)
                    warnings.AppendLine($"• {loc}: Trigger Udon Event — targetUdon is null.");
                if (action.actionType == 6 && action.targetUdon == null)
                    warnings.AppendLine($"• {loc}: Set Udon Variable — targetUdon is null.");
                if (action.actionType == 15 && action.targetObject == null)
                    warnings.AppendLine($"• {loc}: Set Object State — targetObject is null.");
                if (action.actionType == 17 && string.IsNullOrEmpty(action.autoChangeGroupName))
                    warnings.AppendLine($"• {loc}: Set Autochange Group State — autoChangeGroupName is empty.");
                // Color Selector group-name checks
                if (action.actionType == 10 && action.colorSelectorRole == 1
                    && string.IsNullOrEmpty(action.colorGroupName))
                    warnings.AppendLine($"• {loc}: Set Color — Color Palette Name is empty; Color Display and Next/Previous Color buttons cannot link to it.");
                if (action.actionType == 10 && (action.colorSelectorRole == 0 || action.colorSelectorRole == 2)
                    && string.IsNullOrEmpty(action.colorGroupName))
                {
                    string colorRoleLabel = action.colorSelectorRole == 0 ? "Color Display"
                                          : action.propertyType == 1      ? "Previous Color"
                                          :                                  "Next Color";
                    warnings.AppendLine($"• {loc}: {colorRoleLabel} — Color Palette Name is empty; this action cannot link to a Set Color entry.");
                }
                if (action.actionType == 19 && action.variantSelectorRole == 1
                    && action.targetRenderer == null)
                    warnings.AppendLine($"• {loc}: Set Variant — targetRenderer is null.");
                if (action.actionType == 19 && action.variantSelectorRole == 1
                    && (action.variantItems == null || action.variantItems.Length == 0))
                    warnings.AppendLine($"• {loc}: Set Variant — variant items list is empty.");
                if (action.actionType == 19 && action.variantSelectorRole != 1
                    && string.IsNullOrEmpty(action.variantGroupName))
                    warnings.AppendLine($"• {loc}: Variant Selector role {action.variantSelectorRole} — variantGroupName is empty.");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION ORDERING (Change Color/Variant must precede Set Color/Variant)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateActionOrdering(EnigmaActionData[] acts, string loc, StringBuilder warnings)
        {
            if (acts == null || acts.Length < 2) return;

            var colorChangeByGroup   = new Dictionary<string, int>();
            var colorSetByGroup      = new Dictionary<string, int>();
            var variantChangeByGroup = new Dictionary<string, int>();
            var variantSetByGroup    = new Dictionary<string, int>();

            for (int ai = 0; ai < acts.Length; ai++)
            {
                var a = acts[ai];
                if (a == null) continue;
                if (a.actionType == 10 && !string.IsNullOrEmpty(a.colorGroupName))
                {
                    if (a.colorSelectorRole == 2 && !colorChangeByGroup.ContainsKey(a.colorGroupName))
                        colorChangeByGroup[a.colorGroupName] = ai;
                    else if (a.colorSelectorRole == 1 && !colorSetByGroup.ContainsKey(a.colorGroupName))
                        colorSetByGroup[a.colorGroupName] = ai;
                }
                if (a.actionType == 19 && !string.IsNullOrEmpty(a.variantGroupName))
                {
                    if (a.variantSelectorRole == 2 && !variantChangeByGroup.ContainsKey(a.variantGroupName))
                        variantChangeByGroup[a.variantGroupName] = ai;
                    else if (a.variantSelectorRole == 1 && !variantSetByGroup.ContainsKey(a.variantGroupName))
                        variantSetByGroup[a.variantGroupName] = ai;
                }
            }

            // Check each Color Selector group that has both a Change and a Set action.
            foreach (var kvpC in colorChangeByGroup)
            {
                string group = kvpC.Key;
                if (!colorSetByGroup.TryGetValue(group, out int si)) continue;
                int ci = kvpC.Value;
                var changeAct = acts[ci];
                var setAct    = acts[si];
                float changeDelay = changeAct.useDelay ? changeAct.delaySeconds : 0f;
                float setDelay    = setAct.useDelay    ? setAct.delaySeconds    : 0f;
                bool orderWrong   = si < ci;
                bool delayWrong   = changeDelay > setDelay;

                if (orderWrong && delayWrong)
                    warnings.AppendLine(
                        $"• {loc}: Set Color (action {si + 1}) is listed before Change Color " +
                        $"(action {ci + 1}) AND Change Color (delay {changeDelay}s) fires after " +
                        $"Set Color (delay {setDelay}s) for group '{group}'. " +
                        "Fix both: move Change Color before Set Color in the list, and ensure " +
                        "Change Color's delay is <= Set Color's delay.");
                else if (orderWrong)
                    warnings.AppendLine(
                        $"• {loc}: Set Color (action {si + 1}) is listed before " +
                        $"Change Color (action {ci + 1}) for group '{group}'. " +
                        "Set Color commits the pending color, so it must come AFTER " +
                        "Change Color; otherwise it applies the old pending selection.");
                else if (delayWrong)
                    warnings.AppendLine(
                        $"• {loc}: Change Color (action {ci + 1}, delay {changeDelay}s) " +
                        $"fires after Set Color (action {si + 1}, delay {setDelay}s) " +
                        $"for group '{group}'. " +
                        "Set Color will commit the old pending selection before Change Color " +
                        "has advanced it. Reduce Change Color's delay or increase Set Color's " +
                        "so that Change Color fires first.");
            }

            // Check each Variant Selector group that has both a Change and a Set action.
            foreach (var kvpV in variantChangeByGroup)
            {
                string group = kvpV.Key;
                if (!variantSetByGroup.TryGetValue(group, out int si)) continue;
                int ci = kvpV.Value;
                var changeAct = acts[ci];
                var setAct    = acts[si];
                float changeDelay = changeAct.useDelay ? changeAct.delaySeconds : 0f;
                float setDelay    = setAct.useDelay    ? setAct.delaySeconds    : 0f;
                bool orderWrong   = si < ci;
                bool delayWrong   = changeDelay > setDelay;

                if (orderWrong && delayWrong)
                    warnings.AppendLine(
                        $"• {loc}: Set Variant (action {si + 1}) is listed before Change Variant " +
                        $"(action {ci + 1}) AND Change Variant (delay {changeDelay}s) fires after " +
                        $"Set Variant (delay {setDelay}s) for group '{group}'. " +
                        "Fix both: move Change Variant before Set Variant in the list, and ensure " +
                        "Change Variant's delay is <= Set Variant's delay.");
                else if (orderWrong)
                    warnings.AppendLine(
                        $"• {loc}: Set Variant (action {si + 1}) is listed before " +
                        $"Change Variant (action {ci + 1}) for group '{group}'. " +
                        "Set Variant commits the pending variant, so it must come AFTER " +
                        "Change Variant; otherwise it applies the old pending selection.");
                else if (delayWrong)
                    warnings.AppendLine(
                        $"• {loc}: Change Variant (action {ci + 1}, delay {changeDelay}s) " +
                        $"fires after Set Variant (action {si + 1}, delay {setDelay}s) " +
                        $"for group '{group}'. " +
                        "Set Variant will commit the old pending selection before Change Variant " +
                        "has advanced it. Reduce Change Variant's delay or increase Set Variant's " +
                        "so that Change Variant fires first.");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PER-CONTROLLER CHECKS
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidatePerControllerChecks(Scene scene, StringBuilder warnings)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    string ctrlName = ctrl.gameObject.name;

                    // Fader links exist but no fader slots assigned
                    int faderLinkCount = 0;
                    foreach (var f in folders)
                        foreach (var e in f.entries)
                            if (e.assignFader) faderLinkCount++;

                    if (faderLinkCount > 0 && (ctrl.faderSlots == null || ctrl.faderSlots.Length == 0))
                        warnings.AppendLine($"• '{ctrlName}': Fader links are configured but no fader slots are assigned to the controller.");

                    // Whitelist enabled but no usernames or integrations configured
                    if (ctrl.whitelistEnabled
                        && (ctrl.authorizedUsernames == null || ctrl.authorizedUsernames.Length == 0)
                        && ctrl.ohGeezCmonAccessControl == null
                        && ctrl.proTVManagedWhitelist == null
                        && ctrl.flatlineSync == null)
                    {
                        warnings.AppendLine(
                            $"• '{ctrlName}': Whitelist is enabled but no authorized usernames, " +
                            "OhGeez access control, ProTV managed whitelist, or Flatline sync is assigned. " +
                            "All interactions will be blocked at runtime.");
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  EXCLUSIVE GROUP CHECKS (cross-entity: controllers + buttons)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateExclusiveGroups(Scene scene, StringBuilder warnings)
        {
            // Collect all exclusive group members from controllers and buttons.
            // Each member: (location label, tags set, isExclusiveOff, isOnByDefault, controllerName, folderIndex)
            var allMembers = new List<(string loc, HashSet<string> tags, bool exclusiveOff, bool onByDefault,
                                       string ctrlName, int folderIdx)>();

            foreach (var root in scene.GetRootGameObjects())
            {
                // Controller entries
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    string ctrlName = ctrl.gameObject.name;

                    for (int f = 0; f < folders.Length; f++)
                    {
                        var folder = folders[f];
                        for (int e = 0; e < folder.entries.Length; e++)
                        {
                            var entry = folder.entries[e];
                            if (entry.isEmpty || !entry.useExclusiveGroup
                                || string.IsNullOrEmpty(entry.exclusiveGroup)) continue;

                            var tags = new HashSet<string>();
                            foreach (string rawTag in entry.exclusiveGroup.Split(','))
                            {
                                string tag = rawTag.Trim();
                                if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
                            }
                            if (tags.Count == 0) continue;

                            string loc = $"'{ctrlName}' > Folder '{folder.name}', Entry '{entry.label}'";
                            allMembers.Add((loc, tags, entry.exclusiveOff, entry.onByDefault, ctrlName, f));
                        }
                    }
                }

                // Standalone buttons
                foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                {
                    if (IsEditorOnly(btn.gameObject)) continue;
                    if (!btn.useExclusiveGroup || string.IsNullOrEmpty(btn.exclusiveGroup)) continue;

                    var tags = new HashSet<string>();
                    foreach (string rawTag in btn.exclusiveGroup.Split(','))
                    {
                        string tag = rawTag.Trim();
                        if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
                    }
                    if (tags.Count == 0) continue;

                    string loc = $"Button '{btn.gameObject.name}'";
                    allMembers.Add((loc, tags, btn.exclusiveOff, btn.onByDefault, null, -1));
                }
            }

            // Build per-tag lists
            var membersPerTag     = new Dictionary<string, List<string>>();       // tag → location labels
            var exclusiveOffPerTag = new Dictionary<string, List<string>>();       // tag → exclusive-off locations
            var defaultOnPerTag   = new Dictionary<string, List<string>>();       // tag → on-by-default locations
            // For cross-folder detection within a controller: tag → (ctrlName → set of folderIdx)
            var tagControllerFolders = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            // For cross-folder warning message: tag → (ctrlName → set of folder names)
            var tagControllerFolderNames = new Dictionary<string, Dictionary<string, HashSet<string>>>();

            foreach (var member in allMembers)
            {
                foreach (string tag in member.tags)
                {
                    // Members per tag
                    if (!membersPerTag.ContainsKey(tag))
                        membersPerTag[tag] = new List<string>();
                    membersPerTag[tag].Add(member.loc);

                    // Exclusive off per tag
                    if (member.exclusiveOff)
                    {
                        if (!exclusiveOffPerTag.ContainsKey(tag))
                            exclusiveOffPerTag[tag] = new List<string>();
                        exclusiveOffPerTag[tag].Add(member.loc);
                    }

                    // On by default per tag
                    if (member.onByDefault)
                    {
                        if (!defaultOnPerTag.ContainsKey(tag))
                            defaultOnPerTag[tag] = new List<string>();
                        defaultOnPerTag[tag].Add(member.loc);
                    }

                    // Cross-folder tracking (controller entries only)
                    if (member.ctrlName != null)
                    {
                        if (!tagControllerFolders.ContainsKey(tag))
                        {
                            tagControllerFolders[tag] = new Dictionary<string, HashSet<int>>();
                            tagControllerFolderNames[tag] = new Dictionary<string, HashSet<string>>();
                        }
                        if (!tagControllerFolders[tag].ContainsKey(member.ctrlName))
                        {
                            tagControllerFolders[tag][member.ctrlName] = new HashSet<int>();
                            tagControllerFolderNames[tag][member.ctrlName] = new HashSet<string>();
                        }
                        tagControllerFolders[tag][member.ctrlName].Add(member.folderIdx);

                        // Extract folder name from loc for display
                        string folderName = ExtractFolderName(member.loc);
                        if (folderName != null)
                            tagControllerFolderNames[tag][member.ctrlName].Add(folderName);
                    }
                }
            }

            // Warn about exclusive group tags that span multiple folders within a controller
            foreach (var kvp in tagControllerFolders)
            {
                string tag = kvp.Key;
                foreach (var ctrlKvp in kvp.Value)
                {
                    if (ctrlKvp.Value.Count > 1)
                    {
                        string folderList = string.Join(", ", new List<string>(tagControllerFolderNames[tag][ctrlKvp.Key]));
                        warnings.AppendLine(
                            $"• '{ctrlKvp.Key}': Exclusive group tag '{tag}' appears in multiple folders: {folderList}. " +
                            "Exclusive groups enforce mutual exclusion across the entire controller. " +
                            "Ensure this cross-folder behaviour is intentional.");
                    }
                }
            }

            // Warn about multiple Exclusive Off entries in same exclusive group tag
            foreach (var kvp in exclusiveOffPerTag)
            {
                if (kvp.Value.Count > 1)
                {
                    string entries = string.Join("; ", kvp.Value);
                    warnings.AppendLine(
                        $"• Exclusive group tag '{kvp.Key}' has {kvp.Value.Count} Exclusive Off entries: {entries}. " +
                        "Only one entry per exclusive group should be marked as Exclusive Off.");
                }
            }

            // Warn about multiple On By Default entries in same exclusive group tag
            foreach (var kvp in defaultOnPerTag)
            {
                if (kvp.Value.Count > 1)
                {
                    string entries = string.Join("; ", kvp.Value);
                    warnings.AppendLine(
                        $"• Exclusive group tag '{kvp.Key}' has {kvp.Value.Count} On By Default entries: {entries}. " +
                        "Only one entry per exclusive group should be marked as On By Default.");
                }
            }

            // Warn about exclusive group tags with only one member
            foreach (var kvp in membersPerTag)
            {
                if (kvp.Value.Count == 1)
                    warnings.AppendLine(
                        $"• Exclusive group tag '{kvp.Key}' has only one entry: {kvp.Value[0]}. " +
                        "Exclusive tags require at least two entries to have any effect.");
            }
        }

        /// <summary>
        /// Extracts the folder name from a location string like "'CtrlName' > Folder 'FolderName', Entry 'Label'".
        /// Returns null for button locations.
        /// </summary>
        private static string ExtractFolderName(string loc)
        {
            const string marker = "Folder '";
            int start = loc.IndexOf(marker);
            if (start < 0) return null;
            start += marker.Length;
            int end = loc.IndexOf('\'', start);
            if (end < 0) return null;
            return loc.Substring(start, end - start);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OVERLAPPING STATEFUL TOGGLE TARGETS (cross-entity)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateOverlappingStatefulTargets(Scene scene, StringBuilder warnings)
        {
            // target key → list of (location label, exclusive tags set)
            var targetEntries = new Dictionary<string, List<(string loc, HashSet<string> tags)>>();

            foreach (var root in scene.GetRootGameObjects())
            {
                // Controller entries
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    string ctrlName = ctrl.gameObject.name;

                    for (int f = 0; f < folders.Length; f++)
                    {
                        var folder = folders[f];
                        for (int e = 0; e < folder.entries.Length; e++)
                        {
                            var entry = folder.entries[e];
                            if (entry.isEmpty || entry.actions == null) continue;
                            string loc = $"'{ctrlName}' > Folder '{folder.name}', Entry '{entry.label}'";

                            var entryTags = new HashSet<string>();
                            if (entry.useExclusiveGroup && !string.IsNullOrEmpty(entry.exclusiveGroup))
                                foreach (string rawTag in entry.exclusiveGroup.Split(','))
                                {
                                    string tag = rawTag.Trim();
                                    if (!string.IsNullOrEmpty(tag)) entryTags.Add(tag);
                                }

                            CollectStatefulTargets(entry.actions, loc, entryTags, targetEntries);
                        }
                    }
                }

                // Standalone buttons
                foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                {
                    if (IsEditorOnly(btn.gameObject)) continue;
                    var actionsHolder = btn.GetComponent<EnigmaButtonActions>();
                    if (actionsHolder == null || actionsHolder.actions == null) continue;
                    string loc = $"Button '{btn.gameObject.name}'";

                    var btnTags = new HashSet<string>();
                    if (btn.useExclusiveGroup && !string.IsNullOrEmpty(btn.exclusiveGroup))
                        foreach (string rawTag in btn.exclusiveGroup.Split(','))
                        {
                            string tag = rawTag.Trim();
                            if (!string.IsNullOrEmpty(tag)) btnTags.Add(tag);
                        }

                    CollectStatefulTargets(actionsHolder.actions, loc, btnTags, targetEntries);
                }
            }

            // Check for overlaps between entries that don't share any exclusive tag.
            var reported = new HashSet<string>();
            foreach (var kvp in targetEntries)
            {
                var list = kvp.Value;
                if (list.Count < 2) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        bool sharesTag = false;
                        foreach (string tag in list[i].tags)
                            if (list[j].tags.Contains(tag)) { sharesTag = true; break; }
                        if (sharesTag) continue;

                        string pairKey = string.Compare(list[i].loc, list[j].loc) < 0
                            ? $"{list[i].loc}|{list[j].loc}" : $"{list[j].loc}|{list[i].loc}";
                        if (reported.Contains(pairKey)) continue;
                        reported.Add(pairKey);

                        warnings.AppendLine(
                            $"• Overlapping toggle target: {list[i].loc} and {list[j].loc} both control the same target " +
                            "but are not in the same exclusive group. This may cause conflicting state.");
                    }
                }
            }
        }

        private static void CollectStatefulTargets(
            EnigmaActionData[] actions, string loc, HashSet<string> tags,
            Dictionary<string, List<(string loc, HashSet<string> tags)>> targetEntries)
        {
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action.category != 0) continue;
                if (!EnigmaControllerEditor.IsStatefulAction(action.actionType, action.category)) continue;

                string targetKey = null;
                switch (action.actionType)
                {
                    case 0: // Toggle Object
                        if (action.targetObject != null)
                            targetKey = $"obj:{action.targetObject.GetInstanceID()}";
                        break;
                    case 2:  // Toggle Shader Property
                    case 27: // Shader Keyword
                        if (action.targetRenderer != null)
                            targetKey = $"mat:{action.targetRenderer.GetInstanceID()}:{action.materialIndex}:{action.propertyName}";
                        break;
                    case 22: // Toggle Skybox
                        targetKey = "skybox:global";
                        break;
                }
                if (targetKey == null) continue;

                if (!targetEntries.ContainsKey(targetKey))
                    targetEntries[targetKey] = new List<(string, HashSet<string>)>();
                targetEntries[targetKey].Add((loc, tags));
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHADER PROPERTY DEFAULT MISMATCH (cross-entity)
        // ════════════════════════════════════════════════════════════════════════

        private static void ValidateShaderPropertyDefaults(Scene scene, StringBuilder warnings)
        {
            // Key: renderer instanceID:matIdx:propName
            // Value: list of (location label, defFloat, defColor, defVec, propType)
            var propDefaults = new Dictionary<string,
                List<(string loc, float defF, Color defC, Vector4 defV, int propType)>>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    for (int f = 0; f < folders.Length; f++)
                    {
                        var folder = folders[f];
                        for (int e = 0; e < folder.entries.Length; e++)
                        {
                            var entry = folder.entries[e];
                            if (entry.isEmpty || entry.actions == null) continue;
                            string loc = $"'{ctrl.gameObject.name}' > Folder '{folder.name}', Entry '{entry.label}'";
                            CollectShaderPropertyActions(entry.actions, loc, propDefaults);
                        }
                    }
                }

                foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                {
                    if (IsEditorOnly(btn.gameObject)) continue;
                    var actionsHolder = btn.GetComponent<EnigmaButtonActions>();
                    if (actionsHolder == null || actionsHolder.actions == null) continue;
                    string loc = $"Button '{btn.gameObject.name}'";
                    CollectShaderPropertyActions(actionsHolder.actions, loc, propDefaults);
                }
            }

            // Check for mismatches
            foreach (var kvp in propDefaults)
            {
                var list = kvp.Value;
                if (list.Count < 2) continue;
                var first = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    bool mismatch = false;
                    if (first.propType == 0 && first.defF != list[i].defF) mismatch = true;
                    else if (first.propType == 1 && first.defC != list[i].defC) mismatch = true;
                    else if (first.propType == 2 && first.defV != list[i].defV) mismatch = true;

                    if (mismatch)
                    {
                        string propName = kvp.Key.Substring(kvp.Key.LastIndexOf(':') + 1);
                        var locs = new List<string>();
                        foreach (var item in list) locs.Add(item.loc);
                        warnings.AppendLine(
                            $"• Shader property '{propName}' is targeted by multiple entries with different " +
                            $"default values: {string.Join("; ", locs)}. " +
                            "All actions targeting the same property should use the same default value " +
                            "so the material resets consistently when entries are deactivated.");
                        break; // One warning per property
                    }
                }
            }
        }

        private static void CollectShaderPropertyActions(
            EnigmaActionData[] actions, string loc,
            Dictionary<string, List<(string, float, Color, Vector4, int)>> propDefaults)
        {
            foreach (var action in actions)
            {
                if (action == null || action.actionType != 2) continue;
                if (action.targetRenderer == null || string.IsNullOrEmpty(action.propertyName)) continue;
                string key = $"{action.targetRenderer.GetInstanceID()}:{action.materialIndex}:{action.propertyName}";
                if (!propDefaults.ContainsKey(key))
                    propDefaults[key] = new List<(string, float, Color, Vector4, int)>();
                propDefaults[key].Add((loc, action.defaultFloatValue, action.defaultColorValue,
                    action.defaultVectorValue, action.propertyType));
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  AUDIOLINK CONTROLLER COUNT
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Errors when multiple AudioLinkController instances exist in the scene.
        /// Warns when all fader slots are consumed by always-visible static faders
        /// but dynamic fader links exist on entries, leaving no slots for dynamic faders.
        /// </summary>
        private static void ValidateFaderSlotCapacity(Scene scene, StringBuilder warnings)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;
                    int totalSlots = ctrl.faderSlots != null ? ctrl.faderSlots.Length : 0;
                    if (totalSlots <= 0) continue;

                    int pinnedCount = 0;
                    if (ctrl.rtStaticFaderAlwaysVisible != null)
                        for (int i = 0; i < ctrl.rtStaticFaderAlwaysVisible.Length; i++)
                            if (ctrl.rtStaticFaderAlwaysVisible[i]) pinnedCount++;

                    if (pinnedCount < totalSlots) continue;

                    // All slots are pinned — check if any dynamic fader links exist.
                    var folders = ctrl.GetFolders();
                    if (folders == null) continue;
                    bool hasDynamicLinks = false;
                    foreach (var folder in folders)
                    {
                        foreach (var entry in folder.entries)
                        {
                            if (!entry.isEmpty && entry.assignFader)
                            { hasDynamicLinks = true; break; }
                        }
                        if (hasDynamicLinks) break;
                    }

                    if (hasDynamicLinks)
                    {
                        warnings.AppendLine(
                            $"• '{ctrl.gameObject.name}': All {totalSlots} fader slots are consumed by " +
                            "always-visible static faders, but entries with dynamic fader links exist. " +
                            "No slots remain for dynamic faders. Remove some Always Visible tags or add more fader slots.");
                    }
                }
            }
        }

        /// <summary>
        /// Warns when a controller has both skybox-linked static faders and
        /// Toggle Skybox actions, since changing the skybox replaces the material
        /// and the fader would control the old material.
        /// </summary>
        private static void ValidateSkyboxFaderConflict(Scene scene, StringBuilder warnings)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    if (IsEditorOnly(ctrl.gameObject)) continue;

                    bool hasSkyboxFader = false;
                    if (ctrl.rtStaticFaderTargetsSkybox != null)
                        for (int i = 0; i < ctrl.rtStaticFaderTargetsSkybox.Length; i++)
                            if (ctrl.rtStaticFaderTargetsSkybox[i]) { hasSkyboxFader = true; break; }

                    if (!hasSkyboxFader) continue;

                    bool hasToggleSkybox = false;
                    var folders = ctrl.GetFolders();
                    if (folders != null)
                    {
                        foreach (var folder in folders)
                        {
                            foreach (var entry in folder.entries)
                            {
                                if (entry.isEmpty || entry.actions == null) continue;
                                foreach (var action in entry.actions)
                                {
                                    if (action != null && action.actionType == 22)
                                    { hasToggleSkybox = true; break; }
                                }
                                if (hasToggleSkybox) break;
                            }
                            if (hasToggleSkybox) break;
                        }
                    }

                    if (hasToggleSkybox)
                    {
                        warnings.AppendLine(
                            $"• '{ctrl.gameObject.name}': Has both skybox-linked static faders and Toggle Skybox actions. " +
                            "Changing the skybox replaces the material, so skybox faders will control the old material " +
                            "and have no visible effect. Remove skybox faders or Toggle Skybox actions to avoid this conflict.");
                    }
                }
            }
        }

        /// <summary>
        /// Errors when multiple AudioLinkController instances exist in the scene.
        /// Only one is supported at a time (see <see cref="CollectAudioLinkControllers"/>
        /// for the authoritative list of hosts the check inspects).
        /// </summary>
        private static void ValidateAudioLinkControllerCount(Scene scene)
        {
            var controllers = CollectAudioLinkControllers(scene);
            if (controllers.Count > 1)
                Debug.LogError(BuildMultipleAudioLinkControllerMessage(controllers));
        }

        /// <summary>
        /// Collects every non-EditorOnly <c>AudioLink.AudioLinkController</c> in the
        /// scene. Used both by the generic validation pass (logs a warning) and by
        /// the hard build/play-mode gates in <see cref="EnigmaBuildValidator"/> and
        /// <see cref="EnigmaPlayModeHook"/> (which abort rather than just warn).
        /// </summary>
        internal static List<GameObject> CollectAudioLinkControllers(Scene scene)
        {
            var results = new List<GameObject>();
            if (!scene.isLoaded) return results;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var alc in root.GetComponentsInChildren<AudioLink.AudioLinkController>(true))
                {
                    if (alc == null) continue;
                    if (IsEditorOnly(alc.gameObject)) continue;
                    results.Add(alc.gameObject);
                }
            }
            return results;
        }

        /// <summary>
        /// Sums AudioLinkController hosts across every loaded scene. Multi-scene
        /// editing can put two controllers in different scenes yet the same
        /// instance at runtime — they'll still fight over the shared AudioLink
        /// state, so the build/play gates treat the sum as the relevant count.
        /// </summary>
        internal static List<GameObject> CollectAudioLinkControllersAcrossLoadedScenes()
        {
            var results = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                results.AddRange(CollectAudioLinkControllers(s));
            }
            return results;
        }

        /// <summary>
        /// Builds the user-facing error message listing each offending GameObject's
        /// full hierarchy path. Called by the validator log path and by the hard-
        /// gate dialogs so the wording stays consistent across entry points.
        /// </summary>
        internal static string BuildMultipleAudioLinkControllerMessage(List<GameObject> offenders)
        {
            var sb = new StringBuilder();
            sb.Append("[EnigmaOS] Only one AudioLink Controller is supported in the scene at a time. ");
            sb.Append("Each controller is an independently synced authority over the one shared ");
            sb.Append("AudioLink state (gain, power, band thresholds, theme colors), so extra ");
            sb.Append("controllers display stale values, overwrite each other's settings, and can ");
            sb.Append("leave late joiners with different effective settings than everyone else. ");
            sb.Append("The Mixer's AutoLink auto-gain also reconciles only its own controller each ");
            sb.Append("frame, so any other controller reads wrong and its auto-gain toggle does ");
            sb.Append("nothing.\n\nFound ");
            sb.Append(offenders.Count);
            sb.Append(" AudioLinkController host(s):\n");
            for (int i = 0; i < offenders.Count; i++)
            {
                var go = offenders[i];
                if (go == null) { sb.Append("  - (missing GameObject)\n"); continue; }
                sb.Append("  - ");
                sb.Append(GetHierarchyPath(go));
                sb.Append("  (scene: ");
                sb.Append(go.scene.name);
                sb.Append(")\n");
            }
            sb.Append("\nDelete all but one and try again.");
            return sb.ToString();
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "(null)";
            var t = go.transform;
            var path = new StringBuilder(t.name);
            t = t.parent;
            while (t != null)
            {
                path.Insert(0, "/");
                path.Insert(0, t.name);
                t = t.parent;
            }
            return path.ToString();
        }
    }
}
#endif

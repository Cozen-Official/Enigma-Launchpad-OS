#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Cozen.EnigmaOS.Editor
{
    public partial class EnigmaControllerEditor
    {
        // ── Shared action-list drawer (initialized lazily in DrawEntryContent) ──
        private EnigmaActionListDrawer _actionDrawer;

        /// <summary>
        /// Marks both the controller AND its EnigmaControllerData companion as dirty
        /// so changes to folder/entry/fader data persist through play mode and prefab overrides.
        /// </summary>
        private void MarkDataDirty(EnigmaController ctrl) => MarkDataDirtyStatic(ctrl);

        /// <summary>
        /// Extracts an <see cref="UdonSharp.UdonSharpBehaviour"/> from whatever
        /// the user dropped into a fader-link Udon field. See the matching
        /// helper in <c>EnigmaControllerEditor.Faders.cs</c> for the static
        /// fader rationale — same bug, same fix, separated because this
        /// partial is in a different file and the helpers aren't shared.
        /// </summary>
        private static UdonSharp.UdonSharpBehaviour ResolveUdonSharpBehaviourForFaderLink(UnityEngine.Object source)
        {
            if (source == null) return null;
            if (source is UdonSharp.UdonSharpBehaviour usb) return usb;
            if (source is GameObject go) return go.GetComponent<UdonSharp.UdonSharpBehaviour>();
            if (source is VRC.Udon.UdonBehaviour ub)
                return ub.gameObject != null ? ub.gameObject.GetComponent<UdonSharp.UdonSharpBehaviour>() : null;
            if (source is Component c) return c.GetComponent<UdonSharp.UdonSharpBehaviour>();
            return null;
        }

        internal static void MarkDataDirtyStatic(EnigmaController ctrl)
        {
            EditorUtility.SetDirty(ctrl);
            var data = ctrl.GetComponent<EnigmaControllerData>();
            if (data != null) EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// Returns true for events that can cause the user to mutate state
        /// through this GUI frame — MouseDown, MouseUp, MouseDrag, KeyDown,
        /// ExecuteCommand (paste / undo-redo), ValidateCommand, and
        /// DragPerform. Returns false for Layout and Repaint (which cannot
        /// mutate) and MouseMove (rare on inspectors). Used to gate per-frame
        /// <see cref="Undo.RecordObject"/> calls: doing the record every frame
        /// on large EnigmaControllerData (Mochie-FX scale ~30k serialized
        /// values) produces 1.7-3.2s Unity freezes because every frame's
        /// commit diffs the entire object. Gating on mutation-capable events
        /// keeps undo + prefab-override persistence correct while letting
        /// Layout/Repaint run free.
        /// </summary>
        internal static bool IsMutationEvent(Event evt)
        {
            if (evt == null) return false;
            switch (evt.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.KeyDown:
                case EventType.ExecuteCommand:
                case EventType.ValidateCommand:
                case EventType.DragPerform:
                case EventType.ScrollWheel:
                    return true;
                default:
                    return false;
            }
        }

        // ── Fader link drag-to-reorder state ──
        private int _flDragSource = -1;
        private int _flDragTarget = -1;
        private readonly List<float> _flRowTopYs = new List<float>();
        private readonly HashSet<int> _collapsedDynamicFaders = new HashSet<int>();

        // ── Copy/paste ──
        private EnigmaEntryData _copiedEntry;

        // ════════════════════════════════════════════════════════════════════════
        //  SELECTED BUTTON SETTINGS PANEL
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws the settings for whichever entry was last clicked in the preview grid.
        /// <see cref="_selectedLocalEntryIndex"/> holds the folder-scoped entry index;
        /// -1 means nothing is selected.
        /// </summary>
        private void DrawSelectedButtonSettings()
        {
            EnigmaController ctrl    = (EnigmaController)target;

            // Record undo on the data companion BEFORE any mutation happens so
            // direct-field writes (e.g. `link.defaultValue = EditorGUILayout.FloatField(...)`)
            // persist across scene saves and play mode entry. Gated on
            // mutation-capable events only — Layout/Repaint don't need the
            // record and make up ~99% of inspector frames. On Mochie-FX-scale
            // controllers (~30k serialized values), doing this every frame
            // caused 1.7-3.2s Unity freezes because each Undo.RecordObject
            // commits a diff of the entire EnigmaControllerData object at
            // frame end. Gating on MouseDown/MouseUp/KeyDown/commands keeps
            // undo + prefab-override persistence working without the freeze.
            // Same hot-control gate as DrawPreview: skip the per-MouseDrag
            // snapshot when a foreign IMGUI control owns the active drag
            // (typically a FloatField label-scrub on an action's Value field).
            // Without this gate, dragging a Value field triggered a full
            // snapshot of EnigmaControllerData (~15k serialized values) every
            // MouseDrag tick, blocking Unity's main thread for ~1.5s/tick.
            var sbsEvt = Event.current;
            bool sbsShouldRecord = IsMutationEvent(sbsEvt)
                && !(sbsEvt.type == EventType.MouseDrag && GUIUtility.hotControl != 0);
            if (sbsShouldRecord)
            {
                var dataComp = ctrl.GetComponent<EnigmaControllerData>();
                if (dataComp != null)
                {
                    Undo.RecordObject(dataComp, "Modify Enigma Entry");
                }
            }

            EnigmaFolderData[] folders = ctrl.GetFolders() ?? new EnigmaFolderData[0];

            if (folders.Length == 0 || _previewFolderIndex >= folders.Length)
            {
                EditorGUILayout.HelpBox(
                    "Add a folder in the preview above to get started.",
                    MessageType.Info);
                return;
            }

            var folder = folders[_previewFolderIndex];

            if (_selectedLocalEntryIndex < 0 || _selectedLocalEntryIndex >= folder.entries.Length
                || folder.entries[_selectedLocalEntryIndex].isEmpty)
            {
                EditorGUILayout.HelpBox(
                    "Click a button in the preview above to edit its settings.",
                    MessageType.Info);
                return;
            }

            EnigmaEntryData entry = folder.entries[_selectedLocalEntryIndex];

            // ── Context header + reorder / delete controls ────────────────────
            EditorGUILayout.BeginHorizontal();

            // Bold breadcrumb: Folder › Entry N: Label
            var breadcrumbStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true };
            EditorGUILayout.LabelField(
                $"{folder.name}  ›  Entry {_selectedLocalEntryIndex + 1}: {entry.label}",
                breadcrumbStyle);

            GUILayout.FlexibleSpace();

            int assignedCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
            bool atLastEntry  = _selectedLocalEntryIndex >= folder.entries.Length - 1;

            // Options context menu — toggles On By Default / Use Exclusive Tags / Exclusivity Presets
            if (GUILayout.Button("Options ▾", GUILayout.Width(80)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("On By Default"), entry.onByDefault, () =>
                {
                    entry.onByDefault = !entry.onByDefault;
                    MarkDataDirty(ctrl);
                    Repaint();
                });
                menu.AddItem(new GUIContent("Use Exclusive Tags"), entry.useExclusiveGroup, () =>
                {
                    entry.useExclusiveGroup = !entry.useExclusiveGroup;
                    if (!entry.useExclusiveGroup)
                        entry.exclusiveOff = false;
                    MarkDataDirty(ctrl);
                    Repaint();
                });
                if (entry.useExclusiveGroup)
                {
                    menu.AddItem(new GUIContent("Exclusive Off"), entry.exclusiveOff, () =>
                    {
                        entry.exclusiveOff = !entry.exclusiveOff;
                        MarkDataDirty(ctrl);
                        Repaint();
                    });
                }
                menu.AddItem(new GUIContent("Use Autochange Group"), entry.useAutoChangeGroup, () =>
                {
                    entry.useAutoChangeGroup = !entry.useAutoChangeGroup;
                    MarkDataDirty(ctrl);
                    Repaint();
                });
                menu.AddItem(new GUIContent("Assign Fader When Active"), entry.assignFader, () =>
                {
                    entry.assignFader = !entry.assignFader;
                    // When disabling, clear fader link IDs from all actions.
                    if (!entry.assignFader && entry.actions != null)
                    {
                        foreach (var act in entry.actions)
                            if (act != null) act.faderLinkId = 0;
                        entry.faderLinks = new EnigmaFaderLinkData[0];
                    }
                    MarkDataDirty(ctrl);
                    Repaint();
                });

                menu.AddItem(new GUIContent("Custom Color"), entry.useCustomColor, () =>
                {
                    entry.useCustomColor = !entry.useCustomColor;
                    MarkDataDirty(ctrl);
                    Repaint();
                });

                // Expire — only meaningful when the entry runs as a Toggle button.
                // The runtime gate is rtEntryExpireSeconds[entryIdx], which is only
                // consulted by HandleToggle. Non-stateful entries (Momentary /
                // DisplayOnly) never call ScheduleEntryExpire so the option has no
                // effect there. We still expose it freely — the rebuild pass will
                // bake 0 if there's no stateful action to gate against.
                menu.AddItem(new GUIContent("Expire"), entry.useExpire, () =>
                {
                    entry.useExpire = !entry.useExpire;
                    MarkDataDirty(ctrl);
                    Repaint();
                });

                // ── Exclusivity Presets submenu ────────────────────────────────
                var capturedFolder = folder;
                var capturedCtrl   = ctrl;
                menu.AddItem(new GUIContent("Exclusivity Presets/Make Folder Exclusive"), false, () =>
                {
                    string folderTag = capturedFolder.name;
                    for (int i = 0; i < capturedFolder.entries.Length; i++)
                    {
                        if (capturedFolder.entries[i].isEmpty) continue;
                        capturedFolder.entries[i].useExclusiveGroup = true;
                        string   existing = capturedFolder.entries[i].exclusiveGroup ?? "";
                        string[] parts    = existing.Split(',');
                        bool hasTag = false;
                        foreach (string t in parts)
                            if (t.Trim() == folderTag) { hasTag = true; break; }
                        if (!hasTag)
                            capturedFolder.entries[i].exclusiveGroup =
                                string.IsNullOrEmpty(existing.Trim()) ? folderTag : existing + ", " + folderTag;
                    }
                    EditorUtility.SetDirty(capturedCtrl);
                    Repaint();
                });
                menu.AddItem(new GUIContent("Exclusivity Presets/Clear Exclusivity"), false, () =>
                {
                    for (int i = 0; i < capturedFolder.entries.Length; i++)
                    {
                        if (capturedFolder.entries[i].isEmpty) continue;
                        capturedFolder.entries[i].useExclusiveGroup = false;
                    }
                    EditorUtility.SetDirty(capturedCtrl);
                    Repaint();
                });

                menu.ShowAsContext();
            }

            // Copy entry to clipboard
            if (GUILayout.Button(new GUIContent("C", "Copy entry"), GUILayout.Width(24)))
            {
                var templateEntry = EnigmaTemplateEntryData.FromEntryData(entry);
                string json = JsonUtility.ToJson(templateEntry, false);
                EditorGUIUtility.systemCopyBuffer = "ENIGMA_ENTRY:" + json;
                // Also stash the live entry for same-session paste (preserves scene refs).
                _copiedEntry = DeepCopyEntry(entry);
            }

            // Paste entry from clipboard
            bool canPaste = _copiedEntry != null
                || (EditorGUIUtility.systemCopyBuffer != null
                    && EditorGUIUtility.systemCopyBuffer.StartsWith("ENIGMA_ENTRY:"));
            using (new EditorGUI.DisabledScope(!canPaste))
            {
                if (GUILayout.Button(new GUIContent("P", "Paste entry"), GUILayout.Width(24)))
                    PasteEntry(folder, ctrl, _selectedLocalEntryIndex);
            }

            // Duplicate — copies current entry to an empty slot on the current page,
            // or creates a new page and places the copy as its first element.
            if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Duplicate", "|Duplicate entry"), GUILayout.Width(24)))
                DuplicateEntry(folder, ctrl, _selectedLocalEntryIndex, assignedCount);

            using (new EditorGUI.DisabledScope(_selectedLocalEntryIndex <= 0))
            {
                if (GUILayout.Button("▲", GUILayout.Width(24)))
                {
                    CleanUpPristineIfNeeded(folder, _selectedLocalEntryIndex, ctrl);
                    if (assignedCount > 0)
                        TrimEmptyTrailingPages(folder, assignedCount, ctrl);
                    _selectedLocalEntryIndex--;
                    if (assignedCount > 0)
                        _previewPageIndex = _selectedLocalEntryIndex / assignedCount;
                    EnsureEntryAtIndex(folder, _selectedLocalEntryIndex, ctrl);
                }
            }
            // ▼ is disabled only when at the last entry and no button slots are assigned
            // (no page concept). When slots are assigned a new page can always be added.
            using (new EditorGUI.DisabledScope(atLastEntry && assignedCount == 0))
            {
                if (GUILayout.Button("▼", GUILayout.Width(24)))
                {
                    CleanUpPristineIfNeeded(folder, _selectedLocalEntryIndex, ctrl);
                    if (atLastEntry && assignedCount > 0)
                        AddPageToFolder(folder, assignedCount, ctrl);
                    _selectedLocalEntryIndex++;
                    if (assignedCount > 0)
                        _previewPageIndex = _selectedLocalEntryIndex / assignedCount;
                    EnsureEntryAtIndex(folder, _selectedLocalEntryIndex, ctrl);
                }
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                // Clear the slot — keeps the array size (and button-index mapping) stable.
                folder.entries[_selectedLocalEntryIndex] = new EnigmaEntryData { isEmpty = true };
                _selectedLocalEntryIndex = -1;
                MarkDataDirty(ctrl);
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            DrawSeparator();
            EditorGUILayout.Space(4);

            // ── Entry fields ──────────────────────────────────────────────────
            DrawEntryContent(ctrl, folder, _previewFolderIndex, _selectedLocalEntryIndex, entry);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ENTRY CONTENT DRAWING
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws all editable fields for a single <paramref name="entry"/> inline —
        /// no outer container or foldout is added so callers can embed this in any
        /// context (e.g. the Selected Button Settings section).
        /// </summary>
        private void DrawEntryContent(EnigmaController ctrl, EnigmaFolderData folder,
                                      int folderIdx, int entryIdx, EnigmaEntryData entry)
        {
            // ── Identity ──
            entry.label = EditorGUILayout.TextField("Label", entry.label);

            EditorGUILayout.Space(2);

            // ── Options tag bar (tags for On By Default / Exclusive Tags) ──
            DrawEntryTagBar(ctrl, entry);

            // ── Custom Color ──
            if (entry.useCustomColor)
            {
                EditorGUI.indentLevel++;
                entry.useConditionalColor = EditorGUILayout.Toggle("Conditional", entry.useConditionalColor);
                if (!entry.useConditionalColor)
                    entry.customColor = EditorGUILayout.ColorField("Active Color", entry.customColor);
                if (entry.useConditionalColor)
                {
                    EditorGUI.indentLevel++;

                    // Source type selector
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Source", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                    bool isMat   = entry.condColorSourceType == 0;
                    bool isUdon  = entry.condColorSourceType == 1;
                    bool newMat   = GUILayout.Toggle(isMat,  "Material", EditorStyles.miniButtonLeft);
                    bool newUdon  = GUILayout.Toggle(isUdon, "Udon",     EditorStyles.miniButtonRight);
                    if (newMat && !isMat)   entry.condColorSourceType = 0;
                    if (newUdon && !isUdon) entry.condColorSourceType = 1;
                    EditorGUILayout.EndHorizontal();

                    if (entry.condColorSourceType == 0) // Material
                    {
                        // Renderer + Skybox
                        EditorGUILayout.BeginHorizontal();
                        using (new EditorGUI.DisabledScope(entry.condColorTargetsSkybox))
                            entry.condColorRenderer = (Renderer)EditorGUILayout.ObjectField(
                                "Renderer", entry.condColorRenderer, typeof(Renderer), true);
                        if (GUILayout.Button(entry.condColorTargetsSkybox ? "Clear" : "Skybox", GUILayout.Width(60)))
                        {
                            entry.condColorTargetsSkybox = !entry.condColorTargetsSkybox;
                            if (entry.condColorTargetsSkybox) entry.condColorRenderer = null;
                            MarkDataDirty(ctrl);
                        }
                        EditorGUILayout.EndHorizontal();

                        Material ccMat = null;
                        if (entry.condColorTargetsSkybox)
                        {
                            ccMat = entry.condColorSkyboxMaterial != null ? entry.condColorSkyboxMaterial : RenderSettings.skybox;
                            if (ccMat != null)
                                EditorGUILayout.LabelField("Material", ccMat.name, EditorStyles.miniLabel);
                            else
                                EditorGUILayout.HelpBox("No skybox material assigned.", MessageType.Warning);
                        }
                        else if (entry.condColorRenderer != null)
                        {
                            entry.condColorMaterialIndex = EnigmaActionListDrawer.DrawMaterialPopup(
                                "Material", entry.condColorMaterialIndex, entry.condColorRenderer);
                            var mats = entry.condColorRenderer.sharedMaterials;
                            if (mats != null && entry.condColorMaterialIndex >= 0 && entry.condColorMaterialIndex < mats.Length)
                                ccMat = mats[entry.condColorMaterialIndex];
                        }

                        // Property name with search
                        if (ccMat != null)
                        {
                            if (_actionDrawer == null) _actionDrawer = new EnigmaActionListDrawer(Repaint);
                            if (!entry.condColorTargetsSkybox)
                            {
                                // Pass `entry` as actionRef so two entries with the
                                // same renderer+material+label don't collapse onto
                                // the same internal pending-value key and cross-talk
                                // (Search in entry B would land the selection on
                                // entry A's condColorPropertyName). Each entry
                                // instance produces a distinct actionId hash.
                                //
                                // Pass undoTarget so the Repaint-time consumption
                                // of the Search popup's pending value registers a
                                // proper prefab-override — see the fader-link call
                                // site below (~line 888) for the full rationale.
                                var ccDataComp = ctrl.GetComponent<EnigmaControllerData>();
                                entry.condColorPropertyName = _actionDrawer.DrawPropertyNameField(
                                    "Property Name", entry.condColorPropertyName,
                                    entry.condColorRenderer, entry.condColorMaterialIndex, entry,
                                    includeFilter: null,
                                    undoTarget: ccDataComp,
                                    undoLabel: "Set Conditional Color Property");
                            }
                            else
                            {
                                EditorGUILayout.BeginHorizontal();
                                entry.condColorPropertyName = EditorGUILayout.TextField("Property Name", entry.condColorPropertyName);
                                if (GUILayout.Button("Search", GUILayout.Width(60)))
                                {
                                    var search = new EnigmaPropertySearch("Shader Properties");
                                    var group = search.GetMainGroup();
                                    // Shared by-toggle grouping — matches the
                                    // drawer's Set Shader Property search so
                                    // users see the same tree everywhere.
                                    EnigmaActionListDrawer.PopulateShaderPropertyTree(group, ccMat.shader);

                                    var capturedEntry = entry;
                                    search.Open(selected =>
                                    {
                                        capturedEntry.condColorPropertyName = selected;
                                        MarkDataDirty(ctrl);
                                        Repaint();
                                    });
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        else if (!entry.condColorTargetsSkybox)
                        {
                            EditorGUILayout.HelpBox("Assign a Renderer to select a property.", MessageType.Info);
                        }
                    }
                    else // Udon
                    {
                        entry.condColorUdonTarget = (UdonSharp.UdonSharpBehaviour)EditorGUILayout.ObjectField(
                            "UdonBehaviour", entry.condColorUdonTarget, typeof(UdonSharp.UdonSharpBehaviour), true);
                        EditorGUILayout.BeginHorizontal();
                        entry.condColorUdonVariableName = EditorGUILayout.TextField("Variable Name", entry.condColorUdonVariableName);
                        using (new EditorGUI.DisabledScope(entry.condColorUdonTarget == null))
                        {
                            if (GUILayout.Button("Search", GUILayout.Width(60)) && entry.condColorUdonTarget != null)
                            {
                                var search = new EnigmaPropertySearch("Udon Variables");
                                var group = search.GetMainGroup();
                                var targetType = entry.condColorUdonTarget.GetType();
                                var fields = targetType.GetFields(
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                foreach (var field in fields)
                                {
                                    if (field.DeclaringType != targetType) continue;
                                    if (field.FieldType == typeof(float) || field.FieldType == typeof(int))
                                        group.Add($"{field.Name}  ({field.FieldType.Name})", field.Name);
                                }
                                var capturedEntry = entry;
                                search.Open(selected =>
                                {
                                    capturedEntry.condColorUdonVariableName = selected;
                                    MarkDataDirty(ctrl);
                                    Repaint();
                                });
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    // ── Conditional color rules ──
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Color Rules", EditorStyles.miniBoldLabel);

                    if (entry.condColorRules == null)
                        entry.condColorRules = new ConditionalColorRule[0];

                    int removeRule = -1;
                    for (int r = 0; r < entry.condColorRules.Length; r++)
                    {
                        var rule = entry.condColorRules[r];
                        Rect ruleRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                        float x = ruleRect.x;
                        float w = ruleRect.width;
                        // Layout: [condition 70px] [4px gap] [value 60px] [4px gap] [color: fill] [4px gap] [X 20px]
                        float condW = 70f, valW = 60f, btnW = 20f, gap = 4f;
                        float colorW = w - condW - valW - btnW - gap * 3;
                        rule.condition = EditorGUI.Popup(new Rect(x, ruleRect.y, condW, ruleRect.height),
                            rule.condition, new[] { "<", ">", "=", "<=", ">=" });
                        x += condW + gap;
                        rule.value = EditorGUI.FloatField(new Rect(x, ruleRect.y, valW, ruleRect.height), rule.value);
                        x += valW + gap;
                        rule.color = EditorGUI.ColorField(new Rect(x, ruleRect.y, colorW, ruleRect.height), rule.color);
                        x += colorW + gap;
                        if (GUI.Button(new Rect(x, ruleRect.y, btnW, ruleRect.height), "\u2715"))
                            removeRule = r;
                    }
                    if (removeRule >= 0)
                    {
                        var list = new System.Collections.Generic.List<ConditionalColorRule>(entry.condColorRules);
                        list.RemoveAt(removeRule);
                        entry.condColorRules = list.ToArray();
                        MarkDataDirty(ctrl);
                    }
                    if (GUILayout.Button("+ Add Condition"))
                    {
                        var list = new System.Collections.Generic.List<ConditionalColorRule>(entry.condColorRules);
                        list.Add(new ConditionalColorRule());
                        entry.condColorRules = list.ToArray();
                        MarkDataDirty(ctrl);
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            // ── Expire + Exclusive Off conflict warning ──
            // Same loop concern as before: if this entry is the Exclusive Off
            // representative AND it has an entry-level expire, a peer-deactivation
            // re-activates this entry, expire fires N seconds later, this entry
            // toggles off, peers re-activate it, … forever.
            if (entry.exclusiveOff && entry.useExpire)
            {
                EditorGUILayout.HelpBox(
                    "Warning: This entry has both Exclusive Off and Expire enabled. " +
                    "When this entry auto-activates as the exclusive-off state and then expires, " +
                    "it will re-activate itself, creating a loop.",
                    MessageType.Warning);
            }

            // ── Actions ──
            EditorGUILayout.Space(4);
            if (_actionDrawer == null) _actionDrawer = new EnigmaActionListDrawer(Repaint);
            var actionDirtyTarget = (UnityEngine.Object)ctrl.GetComponent<EnigmaControllerData>() ?? ctrl;
            _actionDrawer.DrawActionList(actionDirtyTarget, ctrl, ref entry.actions, entry);

            // ── Fader assignment ──
            if (entry.assignFader)
            {
                // "Assign Fader When Active" only takes effect while the entry is
                // in the active state (entryStates[eIdx] == true). That flag is
                // only toggled by buttons whose runtime buttonType derives to
                // Toggle (0) or stateful Step (2) — which requires at least one
                // stateful action in the entry. An entry built from only
                // non-stateful actions (Set-category, TriggerEvent, DisplayValue,
                // Command/Nav, etc.) derives to Momentary (1) or DisplayOnly (4);
                // those press paths in EnigmaController.Execution.OnButtonPressed
                // never set entryStates[entryIdx] = true, so the fader assignment
                // silently never fires. Warn so the user knows to add a Toggle-
                // category action (or switch an existing Set action to Toggle)
                // before the fader link will ever bind.
                //
                // Matches the same IsStatefulAction check the build pipeline uses
                // at EnigmaControllerEditor.Build.cs:507 to derive buttonType.
                bool hasStatefulAction = false;
                if (entry.actions != null)
                {
                    foreach (var act in entry.actions)
                    {
                        if (act == null) continue;
                        if (IsStatefulAction(act.actionType, act.category))
                        {
                            hasStatefulAction = true;
                            break;
                        }
                    }
                }
                if (!hasStatefulAction)
                {
                    EditorGUILayout.HelpBox(
                        "\"Assign Fader When Active\" is enabled, but this button has no Toggle action. " +
                        "Fader assignments only bind while the button is in the active (on) state, which " +
                        "requires at least one Toggle-category action. Set-only actions fire once on press " +
                        "and never activate the button, so the fader links below will never take effect.",
                        MessageType.Warning);
                }

                // Migrate single faderLink to faderLinks array if it has meaningful data.
                if ((entry.faderLinks == null || entry.faderLinks.Length == 0)
                    && entry.faderLink != null
                    && (entry.faderLink.targetRenderer != null
                        || entry.faderLink.targetsSkybox
                        || !string.IsNullOrEmpty(entry.faderLink.propertyName)))
                    entry.faderLinks = new[] { entry.faderLink };
                if (entry.faderLinks == null)
                    entry.faderLinks = new EnigmaFaderLinkData[0];

                EditorGUI.indentLevel++;
                int removeLink = -1;
                int flCount = entry.faderLinks.Length;

                // Size row-Y tracking list
                while (_flRowTopYs.Count < flCount + 1) _flRowTopYs.Add(0f);
                while (_flRowTopYs.Count > flCount + 1) _flRowTopYs.RemoveAt(_flRowTopYs.Count - 1);

                for (int fl = 0; fl < flCount; fl++)
                {
                    var link = entry.faderLinks[fl];
                    if (link == null) continue;

                    // Row gap marker for drag insertion line
                    Rect flRowMarker = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint && fl < _flRowTopYs.Count)
                        _flRowTopYs[fl] = flRowMarker.y;
                    if (_flDragSource >= 0 && _flDragTarget == fl)
                        EditorGUI.DrawRect(new Rect(flRowMarker.x, flRowMarker.y - 1f, flRowMarker.width, 2f),
                            new Color(0.25f, 0.65f, 1f, 0.9f));

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();

                    // Drag handle — colored if linked to an action
                    Rect flHandle = GUILayoutUtility.GetRect(24f, EditorGUIUtility.singleLineHeight, GUILayout.Width(24));
                    if (Event.current.type == EventType.Repaint)
                    {
                        Color handleColor = link.faderLinkId != 0
                            ? EnigmaActionListDrawer.LinkIdToColor(link.faderLinkId)
                            : new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.25f : 0.15f);
                        EditorGUI.DrawRect(flHandle, handleColor);
                    }
                    EditorGUI.LabelField(flHandle, "\u283F", EditorStyles.centeredGreyMiniLabel);
                    EditorGUIUtility.AddCursorRect(flHandle, MouseCursor.Pan);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                        && flHandle.Contains(Event.current.mousePosition))
                    {
                        _flDragSource = fl;
                        _flDragTarget = fl;
                        Event.current.Use();
                    }

                    bool dfCollapsed = _collapsedDynamicFaders.Contains(fl);
                    string dfArrow = dfCollapsed ? "\u25B6" : "\u25BC";
                    string dfDisplayName = string.IsNullOrEmpty(link.faderName)
                        ? $"Dynamic Fader {fl + 1}"
                        : $"Dynamic Fader {fl + 1}: {link.faderName}";
                    GUILayout.Label($"{dfArrow} {dfDisplayName}", EditorStyles.boldLabel);
                    Rect dfLabelRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && dfLabelRect.Contains(Event.current.mousePosition))
                    {
                        if (dfCollapsed) _collapsedDynamicFaders.Remove(fl);
                        else _collapsedDynamicFaders.Add(fl);
                        Event.current.Use();
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("\u2715", GUILayout.Width(24)))
                        removeLink = fl;
                    EditorGUILayout.EndHorizontal();

                    if (!dfCollapsed)
                        DrawFaderLinkFields(link, ctrl, entry);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }

                // Bottom drop-target marker
                Rect flBottomMarker = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint && flCount < _flRowTopYs.Count)
                    _flRowTopYs[flCount] = flBottomMarker.y;
                if (_flDragSource >= 0 && _flDragTarget == flCount)
                    EditorGUI.DrawRect(new Rect(flBottomMarker.x, flBottomMarker.y - 1f, flBottomMarker.width, 2f),
                        new Color(0.25f, 0.65f, 1f, 0.9f));

                // Drag tracking / commit
                if (_flDragSource >= 0)
                {
                    if (Event.current.type == EventType.MouseDrag)
                    {
                        float mouseY = Event.current.mousePosition.y;
                        int best = 0;
                        float bestDist = float.MaxValue;
                        for (int j = 0; j <= flCount && j < _flRowTopYs.Count; j++)
                        {
                            float d = Mathf.Abs(_flRowTopYs[j] - mouseY);
                            if (d < bestDist) { bestDist = d; best = j; }
                        }
                        if (best != _flDragTarget) { _flDragTarget = best; Repaint(); }
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseUp)
                    {
                        int src = _flDragSource;
                        int tgt = _flDragTarget;
                        _flDragSource = -1;
                        _flDragTarget = -1;
                        if (tgt >= 0 && tgt != src && tgt != src + 1)
                        {
                            var list = new List<EnigmaFaderLinkData>(entry.faderLinks);
                            var item = list[src];
                            list.RemoveAt(src);
                            list.Insert(tgt > src ? tgt - 1 : tgt, item);
                            entry.faderLinks = list.ToArray();
                            _collapsedDynamicFaders.Clear();
                            MarkDataDirty(ctrl);
                        }
                        Repaint();
                        Event.current.Use();
                    }
                }

                if (removeLink >= 0)
                {
                    int removedLinkId = entry.faderLinks[removeLink].faderLinkId;
                    var list = new List<EnigmaFaderLinkData>(entry.faderLinks);
                    list.RemoveAt(removeLink);
                    entry.faderLinks = list.ToArray();
                    _collapsedDynamicFaders.Clear();

                    // If no remaining fader links share the removed link ID,
                    // clear the ID from the associated action.
                    if (removedLinkId != 0 && entry.actions != null)
                    {
                        bool anyRemaining = false;
                        foreach (var fl in entry.faderLinks)
                            if (fl != null && fl.faderLinkId == removedLinkId) { anyRemaining = true; break; }
                        if (!anyRemaining)
                            foreach (var act in entry.actions)
                                if (act != null && act.faderLinkId == removedLinkId)
                                    act.faderLinkId = 0;
                    }

                    MarkDataDirty(ctrl);
                }

                if (GUILayout.Button("+ Add Dynamic Fader"))
                {
                    var list = new List<EnigmaFaderLinkData>(entry.faderLinks);
                    list.Add(new EnigmaFaderLinkData());
                    entry.faderLinks = list.ToArray();
                    MarkDataDirty(ctrl);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawFaderLinkFields(EnigmaFaderLinkData link, EnigmaController ctrl,
            EnigmaEntryData entry = null)
        {
            if (_actionDrawer == null) _actionDrawer = new EnigmaActionListDrawer(Repaint);

            bool isLinked = link.faderLinkId != 0;

            // For linked fader links, resolve target from the linked action.
            if (isLinked && entry != null && entry.actions != null)
            {
                foreach (var act in entry.actions)
                {
                    if (act == null || act.faderLinkId != link.faderLinkId) continue;
                    if (act.actionType == 22) // Toggle Skybox
                    {
                        link.targetsSkybox = true;
                        link.skyboxMaterial = act.targetMaterial;
                    }
                    else if (act.actionType == 5 || act.actionType == 6) // Udon
                    {
                        link.targetsUdon = true;
                        link.targetUdonBehaviours = act.targetUdon != null
                            ? new UdonSharp.UdonSharpBehaviour[] { act.targetUdon } : null;
                    }
                    else
                    {
                        link.targetRenderer = act.targetRenderer;
                        link.materialIndex = act.materialIndex;
                    }
                    break;
                }
            }

            // Fader name — always the first field.
            link.faderName = EditorGUILayout.TextField("Fader Name", link.faderName);

            // Target type selector — hidden for linked entries (action manages the target).
            if (!isLinked)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Target Type", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                bool isMat = !link.targetsUdon && !link.targetsSlider;
                bool newMat    = GUILayout.Toggle(isMat, "Material", EditorStyles.miniButtonLeft);
                bool newUdon   = GUILayout.Toggle(link.targetsUdon, "Udon", EditorStyles.miniButtonMid);
                bool newSlider = GUILayout.Toggle(link.targetsSlider, "UI Slider", EditorStyles.miniButtonRight);
                if (newUdon && !link.targetsUdon)   { link.targetsUdon = true;  link.targetsSlider = false; link.targetsSkybox = false; }
                else if (newSlider && !link.targetsSlider) { link.targetsSlider = true; link.targetsUdon = false; link.targetsSkybox = false; }
                else if (newMat && (link.targetsUdon || link.targetsSlider)) { link.targetsUdon = false; link.targetsSlider = false; }
                EditorGUILayout.EndHorizontal();
            }

            // ── Udon target ──
            if (link.targetsUdon)
            {
                UdonSharp.UdonSharpBehaviour udonRef = link.targetUdonBehaviours != null && link.targetUdonBehaviours.Length > 0
                    ? link.targetUdonBehaviours[0] : null;
                if (!isLinked)
                {
                    // Accept any Object drag (GameObject, UdonBehaviour, etc.) and
                    // resolve to UdonSharpBehaviour. Filtering the ObjectField to
                    // UdonSharpBehaviour alone silently rejects GameObject drops,
                    // which reads as "the field didn't take my drop" to the user.
                    var picked = EditorGUILayout.ObjectField(
                        "UdonBehaviour", udonRef, typeof(UnityEngine.Object), true);
                    UdonSharp.UdonSharpBehaviour udonTarget = picked == udonRef
                        ? udonRef
                        : ResolveUdonSharpBehaviourForFaderLink(picked);
                    if (picked != udonRef)
                    {
                        link.targetUdonBehaviours = udonTarget != null ? new[] { udonTarget } : null;
                        if (picked != null && udonTarget == null)
                        {
                            Debug.LogWarning(
                                $"[EnigmaOS] '{picked.name}' has no UdonSharpBehaviour component — " +
                                "fader Udon target cleared.");
                        }
                    }
                    udonRef = udonTarget;
                }
                EditorGUILayout.BeginHorizontal();
                link.udonVariableName = EditorGUILayout.TextField("Variable Name", link.udonVariableName);
                using (new EditorGUI.DisabledScope(udonRef == null))
                {
                    if (GUILayout.Button("Search", GUILayout.Width(60)) && udonRef != null)
                    {
                        var search = new EnigmaPropertySearch("Udon Variables");
                        var group = search.GetMainGroup();
                        var targetType = udonRef.GetType();
                        var fields = targetType.GetFields(
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        foreach (var field in fields)
                        {
                            if (field.DeclaringType != targetType) continue;
                            if (field.FieldType == typeof(float) || field.FieldType == typeof(int)
                                || field.FieldType == typeof(bool) || field.FieldType == typeof(string))
                                group.Add($"{field.Name}  ({field.FieldType.Name})", field.Name);
                        }
                        var capturedLink = link;
                        search.Open(selected =>
                        {
                            capturedLink.udonVariableName = selected;
                            MarkDataDirty(ctrl);
                            Repaint();
                        });
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            // ── UI Slider target ──
            else if (link.targetsSlider)
            {
                if (!isLinked)
                {
                    var slider = (UnityEngine.UI.Slider)EditorGUILayout.ObjectField(
                        "UI Slider", link.targetSliders != null && link.targetSliders.Length > 0 ? link.targetSliders[0] : null,
                        typeof(UnityEngine.UI.Slider), true);
                    link.targetSliders = slider != null ? new[] { slider } : null;
                }
                bool reversed = link.sliderDirectionsReversed != null && link.sliderDirectionsReversed.Length > 0 && link.sliderDirectionsReversed[0];
                reversed = EditorGUILayout.Toggle("Reversed", reversed);
                link.sliderDirectionsReversed = new[] { reversed };
            }
            // ── Material / Skybox target ──
            else if (!link.targetsUdon && !link.targetsSlider)
            {
                if (!isLinked)
                {
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(link.targetsSkybox))
                        link.targetRenderer = (UnityEngine.Renderer)EditorGUILayout.ObjectField(
                            "Renderer", link.targetRenderer, typeof(UnityEngine.Renderer), true);
                    if (GUILayout.Button(link.targetsSkybox ? "Clear" : "Skybox", GUILayout.Width(60)))
                    {
                        link.targetsSkybox = !link.targetsSkybox;
                        if (link.targetsSkybox) link.targetRenderer = null;
                        MarkDataDirty(ctrl);
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // VRSL fixture meshes overwrite their material every frame
                // from the fixture's Udon script — warn the user that the
                // Material target type won't work here and they should
                // switch to Udon and target lightColorTint instead.
                if (!link.targetsSkybox)
                    EnigmaActionListDrawer.DrawVRSLFaderWarningIfNeeded(link.targetRenderer);

                Material activeMat = null;
                if (link.targetsSkybox)
                {
                    activeMat = link.skyboxMaterial != null ? link.skyboxMaterial : RenderSettings.skybox;
                    if (activeMat == null)
                        EditorGUILayout.HelpBox("No skybox material assigned.", MessageType.Warning);
                    else if (!isLinked)
                        EditorGUILayout.LabelField("Material", activeMat.name, EditorStyles.miniLabel);
                }
                else
                {
                    if (!isLinked)
                        link.materialIndex = EnigmaActionListDrawer.DrawMaterialPopup(
                            "Material", link.materialIndex, link.targetRenderer);
                    if (link.targetRenderer != null)
                    {
                        var mats = link.targetRenderer.sharedMaterials;
                        if (mats != null && link.materialIndex >= 0 && link.materialIndex < mats.Length)
                            activeMat = mats[link.materialIndex];
                    }
                }

                // Property name with search
                if (activeMat != null && !link.targetsSkybox)
                {
                    string prevName = link.propertyName;
                    // Pass the link instance as actionRef so DrawPropertyNameField's
                    // internal pending-value dictionary keys on this specific
                    // link, not on a shared (renderer, materialIndex, label)
                    // tuple. Without this, two fader links on the same entry
                    // that target the same renderer/material would collapse to
                    // the same fieldKey — a property selected in link #2's
                    // Search popup would get written to link #1 on the next
                    // repaint, because link #1 draws first and consumes the
                    // pending value keyed under their shared tuple. See
                    // EnigmaActionListDrawer.DrawPropertyNameField:1708 for
                    // the actionId fallback logic (`_drawIndex` is only set
                    // during the action-draw loop, so fader links fall back
                    // to a stale / identical value).
                    //
                    // Pass undoTarget=EnigmaControllerData so the pending-
                    // value consumption at Repaint time runs Undo.RecordObject
                    // + SetDirty before mutating link.propertyName. The event-
                    // gated outer hook at DrawSelectedButtonSettings:109 only
                    // fires on MouseDown/MouseUp/KeyDown and the pending-value
                    // path lands on Repaint, so without this the user's
                    // Search selection reverts on scene save / play-mode enter
                    // because Unity never registered the override.
                    var dataComp = ctrl.GetComponent<EnigmaControllerData>();
                    link.propertyName = _actionDrawer.DrawPropertyNameField("Fader Property",
                        link.propertyName, link.targetRenderer, link.materialIndex, link,
                        includeFilter: null,
                        undoTarget: dataComp,
                        undoLabel: "Set Fader Link Property");
                    int autoType = _actionDrawer.ConsumeAutoPropertyType();
                    if (autoType < 0 && link.propertyName != prevName)
                        autoType = EnigmaActionListDrawer.ResolveShaderPropertyType(
                            link.targetRenderer, link.materialIndex, link.propertyName);
                    if (autoType >= 0)
                    {
                        // Clamp to the FADER convention {0=Float, 1=Color}.
                        // ResolveShaderPropertyType returns the ACTION_SHADER
                        // enum (0/1/2/3); Vector and Texture aren't supported
                        // by fader links, so map anything non-Color down to
                        // Float. Matches the same clamp on the static-fader
                        // Search callback.
                        link.propertyType = autoType == 1 ? 1 : 0;
                    }
                    if (link.targetRenderer != null && !string.IsNullOrEmpty(link.propertyName)
                        && EnigmaActionListDrawer.ResolveShaderPropertyType(
                            link.targetRenderer, link.materialIndex, link.propertyName) < 0)
                        EditorGUILayout.HelpBox($"Property \"{link.propertyName}\" not found on this material.", MessageType.Warning);
                }
                else if (activeMat != null && link.targetsSkybox)
                {
                    EditorGUILayout.BeginHorizontal();
                    link.propertyName = EditorGUILayout.TextField("Property Name", link.propertyName);
                    if (GUILayout.Button("Search", GUILayout.Width(60)))
                    {
                        var search = new EnigmaPropertySearch("Shader Properties");
                        var group = search.GetMainGroup();
                        // Shared by-toggle grouping — matches the drawer's
                        // Set Shader Property search so users see the same
                        // tree everywhere.
                        var typeDict = EnigmaActionListDrawer.PopulateShaderPropertyTree(group, activeMat.shader);

                        var capturedLink = link;
                        var capturedMat = activeMat;
                        search.Open(selected =>
                        {
                            capturedLink.propertyName = selected;
                            if (typeDict.TryGetValue(selected, out var spt))
                            {
                                capturedLink.propertyType = EnigmaActionListDrawer.ShaderPropertyTypeToActionType(spt);
                                if (capturedMat.HasProperty(selected))
                                    capturedLink.defaultValue = capturedMat.GetFloat(selected);
                            }
                            MarkDataDirty(ctrl);
                            Repaint();
                        });
                    }
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(link.propertyName) && !activeMat.HasProperty(link.propertyName))
                        EditorGUILayout.HelpBox($"Property \"{link.propertyName}\" not found on this material.", MessageType.Warning);
                }
                else if (!link.targetsSkybox && !isLinked)
                {
                    EditorGUILayout.HelpBox("Assign a Renderer to select a property.", MessageType.Info);
                }
            }

            // Common value fields
            if (link.targetsSlider)
                link.propertyType = 0; // Float for sliders always

            if (link.propertyType == 1) // Color
            {
                // HDR-enabled picker — same reason as the static-fader
                // inspector: color faders frequently target HDR shader
                // properties (emission, bloom inputs) and HDR Udon color
                // fields, which need values > 1 to round-trip correctly.
                link.defaultColor = EditorGUILayout.ColorField(
                    new GUIContent("Default Color"),
                    link.defaultColor,
                    showEyedropper: true,
                    showAlpha: true,
                    hdr: true);
                Color.RGBToHSV(link.defaultColor, out float h, out float s, out float v);
                if (s < 0.15f)
                    EditorGUILayout.HelpBox(
                        "Warning: This color has low saturation (greyscale). Hue shifting will have minimal effect on greyscale colors.",
                        MessageType.Warning);
                float maxShift = Mathf.Clamp(link.maxValue, 0f, 360f);
                float updatedMaxShift = EditorGUILayout.Slider(
                    new GUIContent("Max Shift (degrees)", "Maximum hue shift in degrees. 360 = full color wheel rotation."),
                    maxShift, 0f, 360f);
                if (!Mathf.Approximately(updatedMaxShift, link.maxValue))
                {
                    link.minValue = 0f;
                    link.defaultValue = 0f;
                    link.maxValue = updatedMaxShift;
                }
            }
            else
            {
                link.minValue = EditorGUILayout.FloatField("Min Value", link.minValue);
                link.maxValue = EditorGUILayout.FloatField("Max Value", link.maxValue);
                link.defaultValue = EditorGUILayout.FloatField("Default", link.defaultValue);
            }
            // All faders render an indicator ring. For color-property faders the
            // ring auto-reflects the live material colour (driven by the fader's
            // hue shift), so the Indicator Color field would be ignored — hide it.
            // Float / range faders get a free-form indicator colour, defaulting
            // to white. The per-link colorIndicatorEnabled flag is kept on the
            // data model for backwards compatibility but is always forced true
            // at build time (see EnigmaControllerEditor.Build.cs).
            link.colorIndicatorEnabled = true;
            if (link.propertyType != 1)
                link.indicatorColor = EditorGUILayout.ColorField("Indicator Color", link.indicatorColor);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ENTRY OPTIONS TAG BAR
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws the enabled-option tag pills.  Each pill is a coloured button; clicking
        /// its ✕ disables the option.  When "Use Exclusive Tags" is active the Exclusive Tags
        /// text field is shown below the pill row.  When "Use Autochange Group" is active the
        /// group-name text field is shown below the pill row.
        /// </summary>
        private void DrawEntryTagBar(EnigmaController ctrl, EnigmaEntryData entry)
        {
            bool showOnByDefault     = entry.onByDefault;
            bool showExclusiveTags   = entry.useExclusiveGroup;
            bool showExclusiveOff    = entry.exclusiveOff;
            bool showAutoChangeGroup = entry.useAutoChangeGroup;
            bool showAssignFader     = entry.assignFader;
            bool showCustomColor     = entry.useCustomColor;
            bool showExpire          = entry.useExpire;

            if (!showOnByDefault && !showExclusiveTags && !showExclusiveOff && !showAutoChangeGroup && !showAssignFader && !showCustomColor && !showExpire) return;

            EditorGUILayout.BeginHorizontal();

            if (showOnByDefault)
                DrawTagPill("On By Default", () =>
                {
                    entry.onByDefault = false;
                    MarkDataDirty(ctrl);
                });

            if (showExclusiveTags)
                DrawTagPill("Exclusive Tags", () =>
                {
                    entry.useExclusiveGroup = false;
                    MarkDataDirty(ctrl);
                });

            if (showExclusiveOff)
                DrawTagPill("Exclusive Off", () =>
                {
                    entry.exclusiveOff = false;
                    MarkDataDirty(ctrl);
                });

            if (showAutoChangeGroup)
                DrawAutoChangeTagPill("Autochange", () =>
                {
                    entry.useAutoChangeGroup = false;
                    MarkDataDirty(ctrl);
                });

            if (showAssignFader)
                DrawTagPill("Fader", () =>
                {
                    entry.assignFader = false;
                    MarkDataDirty(ctrl);
                });

            if (showCustomColor)
                DrawTagPill("Custom Color", () =>
                {
                    entry.useCustomColor = false;
                    MarkDataDirty(ctrl);
                });

            if (showExpire)
                DrawTagPill($"Expire: {entry.expireSeconds:0.##}s", () =>
                {
                    entry.useExpire = false;
                    MarkDataDirty(ctrl);
                });

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (showExclusiveTags)
            {
                EditorGUI.indentLevel++;
                entry.exclusiveGroup = EditorGUILayout.TextField("Exclusive Tags", entry.exclusiveGroup);
                EditorGUI.indentLevel--;
            }

            if (showAutoChangeGroup)
            {
                EditorGUI.indentLevel++;
                entry.autoChangeGroup = EditorGUILayout.TextField("Autochange Group", entry.autoChangeGroup);
                EditorGUI.indentLevel--;
            }

            if (showExpire)
            {
                EditorGUI.indentLevel++;
                entry.expireSeconds = Mathf.Max(0.1f,
                    EditorGUILayout.FloatField("Expire (s)", entry.expireSeconds));
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// Draws a single tag pill with an ✕ button.  Clicking the pill invokes
        /// <paramref name="onRemove"/> which should disable the corresponding option.
        /// </summary>
        private static void DrawTagPill(string label, System.Action onRemove)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.55f, 0.85f);
            if (GUILayout.Button($"{label}  ✕", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                GUI.backgroundColor = oldBg;
                onRemove();
                return;
            }
            GUI.backgroundColor = oldBg;
        }

        /// <summary>
        /// Draws a tag pill with a green tint for autochange group tags.
        /// Clicking its ✕ invokes <paramref name="onRemove"/>.
        /// </summary>
        private static void DrawAutoChangeTagPill(string label, System.Action onRemove)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f, 0.75f, 0.35f);
            if (GUILayout.Button($"{label}  ✕", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                GUI.backgroundColor = oldBg;
                onRemove();
                return;
            }
            GUI.backgroundColor = oldBg;
        }
        private static void SwapEntries(EnigmaEntryData[] entries, int a, int b)
        {
            EnigmaEntryData tmp = entries[a];
            entries[a] = entries[b];
            entries[b] = tmp;
        }

        /// <summary>
        /// Removes the entry at <paramref name="from"/> and re-inserts it at
        /// <paramref name="to"/>, shifting the intermediate elements.
        /// </summary>
        private static void MoveEntry(EnigmaEntryData[] entries, int from, int to)
        {
            bool isInvalidMove = from == to || from < 0 || to < 0
                                 || from >= entries.Length || to >= entries.Length;
            if (isInvalidMove) return;
            EnigmaEntryData item = entries[from];
            if (from < to)
                for (int i = from; i < to; i++) entries[i] = entries[i + 1];
            else
                for (int i = from; i > to; i--) entries[i] = entries[i - 1];
            entries[to] = item;
        }

        private static EnigmaEntryData[] AddEntry(EnigmaEntryData[] entries, EnigmaEntryData entry)
        {
            var list = new List<EnigmaEntryData>(entries) { entry };
            return list.ToArray();
        }

        private static EnigmaEntryData[] RemoveEntryAt(EnigmaEntryData[] entries, int idx)
        {
            var list = new List<EnigmaEntryData>(entries);
            list.RemoveAt(idx);
            return list.ToArray();
        }

        // Default label used for newly-created / auto-populated entry slots.
        private const string DefaultEntryLabel = "New button";

        /// <summary>
        /// Returns <c>true</c> when <paramref name="entry"/> is still in its factory-default
        /// "New button" state: label unchanged, no actions, and no custom settings applied.
        /// Such entries are candidates for auto-cleanup when the user navigates away with
        /// the ▲/▼ arrows without having edited them.
        /// </summary>
        private static bool IsEntryPristine(EnigmaEntryData entry)
        {
            return entry != null
                && !entry.isEmpty
                && entry.label      == DefaultEntryLabel
                && !entry.onByDefault
                && !entry.useExclusiveGroup
                && !entry.assignFader
                && (entry.actions == null || entry.actions.Length == 0);
        }

        /// <summary>
        /// Creates a default entry for a newly-populated slot (used both when clicking "+"
        /// in the preview grid and when the ▲/▼ arrows land on an empty slot).
        /// </summary>
        private static EnigmaEntryData NewDefaultEntry() =>
            new EnigmaEntryData { label = DefaultEntryLabel };

        /// <summary>
        /// If the slot at <paramref name="idx"/> holds a pristine "New button" entry,
        /// resets it to an empty slot so ▲/▼ navigation does not litter the folder with
        /// unmodified placeholder entries.
        /// </summary>
        private static void CleanUpPristineIfNeeded(EnigmaFolderData folder, int idx, EnigmaController ctrl)
        {
            if (idx < 0 || idx >= folder.entries.Length) return;
            if (!IsEntryPristine(folder.entries[idx])) return;
            folder.entries[idx] = new EnigmaEntryData { isEmpty = true };
            MarkDataDirtyStatic(ctrl);
        }

        /// <summary>
        /// If the slot at <paramref name="idx"/> is currently empty, populates it with a
        /// default "New button" entry so the Selected Button Settings panel has content to
        /// display after the user navigates to that slot with the ▲/▼ arrows.
        /// </summary>
        private static void EnsureEntryAtIndex(EnigmaFolderData folder, int idx, EnigmaController ctrl)
        {
            if (idx < 0 || idx >= folder.entries.Length) return;
            if (!folder.entries[idx].isEmpty) return;
            folder.entries[idx] = NewDefaultEntry();
            MarkDataDirtyStatic(ctrl);
        }

        /// <summary>
        /// Appends one additional page of empty slots to <paramref name="folder"/>.
        /// Used both by the "Add Page" button in the preview bar and by the ▼ nav
        /// button in Selected Button Settings when the last entry is reached.
        /// </summary>
        private static void AddPageToFolder(EnigmaFolderData folder, int slotsPerPage, EnigmaController ctrl)
        {
            if (slotsPerPage <= 0) return;
            int oldLen = folder.entries.Length;
            int newLen = oldLen + slotsPerPage;
            var extended = new EnigmaEntryData[newLen];
            for (int i = 0; i < oldLen; i++)
                extended[i] = folder.entries[i];
            for (int i = oldLen; i < newLen; i++)
                extended[i] = new EnigmaEntryData { isEmpty = true };
            folder.entries = extended;
            MarkDataDirtyStatic(ctrl);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DUPLICATE ENTRY
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deep-copies <paramref name="sourceIdx"/> to the first empty slot on the current
        /// page.  If no empty slot is available on the page a new page is appended and the
        /// copy is placed as its first element.  When no button slots are assigned (no page
        /// concept) any empty slot in the folder is used, or the array is grown by one.
        /// The inspector navigates to the duplicate after placement.
        /// </summary>
        private void DuplicateEntry(EnigmaFolderData folder, EnigmaController ctrl,
                                    int sourceIdx, int assignedCount)
        {
            EnigmaEntryData copy = DeepCopyEntry(folder.entries[sourceIdx]);
            int destIdx = -1;

            if (assignedCount > 0)
            {
                // Search the current page for an empty slot (skip the source itself).
                int pageStart = _previewPageIndex * assignedCount;
                int pageEnd   = pageStart + assignedCount;
                for (int i = pageStart; i < pageEnd && i < folder.entries.Length; i++)
                {
                    if (i != sourceIdx && folder.entries[i].isEmpty)
                    {
                        destIdx = i;
                        break;
                    }
                }

                if (destIdx < 0)
                {
                    // No empty slot on the current page — append a new page and use its first slot.
                    int oldLen = folder.entries.Length;
                    AddPageToFolder(folder, assignedCount, ctrl);
                    destIdx = oldLen;
                }
            }
            else
            {
                // No page concept — find any empty slot in the folder.
                for (int i = 0; i < folder.entries.Length; i++)
                {
                    if (i != sourceIdx && folder.entries[i].isEmpty)
                    {
                        destIdx = i;
                        break;
                    }
                }

                if (destIdx < 0)
                {
                    // No empty slot exists — grow the folder by one.
                    int oldLen = folder.entries.Length;
                    var extended = new EnigmaEntryData[oldLen + 1];
                    Array.Copy(folder.entries, extended, oldLen);
                    extended[oldLen] = new EnigmaEntryData { isEmpty = true };
                    folder.entries   = extended;
                    destIdx          = oldLen;
                }
            }

            folder.entries[destIdx] = copy;
            MarkDataDirty(ctrl);

            // Navigate to the newly placed duplicate.
            _selectedLocalEntryIndex = destIdx;
            if (assignedCount > 0)
                _previewPageIndex = destIdx / assignedCount;
            Repaint();
        }

        /// <summary>Deep-copies an <see cref="EnigmaEntryData"/>, preserving all
        /// scene-object references and making independent copies of every array.</summary>
        private void PasteEntry(EnigmaFolderData folder, EnigmaController ctrl, int targetIdx)
        {
            EnigmaEntryData pasted;

            // Prefer the in-memory copy (preserves scene refs within same session).
            if (_copiedEntry != null)
            {
                pasted = DeepCopyEntry(_copiedEntry);
            }
            else
            {
                // Fall back to clipboard JSON (cross-session, no scene refs).
                string buf = EditorGUIUtility.systemCopyBuffer;
                if (buf == null || !buf.StartsWith("ENIGMA_ENTRY:")) return;
                string json = buf.Substring("ENIGMA_ENTRY:".Length);
                var templateEntry = JsonUtility.FromJson<EnigmaTemplateEntryData>(json);
                if (templateEntry == null) return;
                pasted = templateEntry.ToEntryData();
            }

            var pasteData = ctrl.GetComponent<EnigmaControllerData>();
            if (pasteData != null) Undo.RecordObject(pasteData, "Paste Entry");
            folder.entries[targetIdx] = pasted;
            MarkDataDirty(ctrl);
        }

        private static EnigmaEntryData DeepCopyEntry(EnigmaEntryData src)
        {
            var dst = new EnigmaEntryData
            {
                isEmpty            = src.isEmpty,
                label              = src.label,
                buttonType         = src.buttonType,
                onByDefault        = src.onByDefault,
                useExclusiveGroup  = src.useExclusiveGroup,
                exclusiveGroup     = src.exclusiveGroup,
                exclusiveOff       = src.exclusiveOff,
                useAutoChangeGroup = src.useAutoChangeGroup,
                autoChangeGroup    = src.autoChangeGroup,
                useExpire          = src.useExpire,
                expireSeconds      = src.expireSeconds,
                assignFader        = src.assignFader,
                faderLink          = DeepCopyFaderLink(src.faderLink),
                useCustomColor           = src.useCustomColor,
                customColor              = src.customColor,
                useConditionalColor      = src.useConditionalColor,
                condColorSourceType      = src.condColorSourceType,
                condColorRenderer        = src.condColorRenderer,
                condColorMaterialIndex   = src.condColorMaterialIndex,
                condColorPropertyName    = src.condColorPropertyName,
                condColorTargetsSkybox   = src.condColorTargetsSkybox,
                condColorSkyboxMaterial  = src.condColorSkyboxMaterial,
                condColorUdonTarget      = src.condColorUdonTarget,
                condColorUdonVariableName = src.condColorUdonVariableName,
            };

            if (src.condColorRules != null)
            {
                dst.condColorRules = new ConditionalColorRule[src.condColorRules.Length];
                for (int i = 0; i < src.condColorRules.Length; i++)
                {
                    var r = src.condColorRules[i];
                    dst.condColorRules[i] = new ConditionalColorRule
                        { condition = r.condition, value = r.value, color = r.color };
                }
            }

            if (src.faderLinks != null)
            {
                dst.faderLinks = new EnigmaFaderLinkData[src.faderLinks.Length];
                for (int i = 0; i < src.faderLinks.Length; i++)
                    dst.faderLinks[i] = DeepCopyFaderLink(src.faderLinks[i]);
            }

            if (src.actions != null)
            {
                dst.actions = new EnigmaActionData[src.actions.Length];
                for (int i = 0; i < src.actions.Length; i++)
                    dst.actions[i] = DeepCopyAction(src.actions[i]);
            }
            else
            {
                dst.actions = new EnigmaActionData[0];
            }

            return dst;
        }

        /// <summary>Deep-copies an <see cref="EnigmaActionData"/>, preserving all
        /// scene/asset object references and making independent copies of sub-arrays.</summary>
        private static EnigmaActionData DeepCopyAction(EnigmaActionData src)
        {
            if (src == null) return new EnigmaActionData();

            var dst = new EnigmaActionData
            {
                actionType              = src.actionType,
                targetObject            = src.targetObject,
                targetRenderer          = src.targetRenderer,
                materialIndex           = src.materialIndex,
                targetMaterial          = src.targetMaterial,
                defaultMaterial         = src.defaultMaterial,
                propertyName            = src.propertyName,
                propertyFloatValue      = src.propertyFloatValue,
                propertyColorValue      = src.propertyColorValue,
                propertyVectorValue     = src.propertyVectorValue,
                targetTexture           = src.targetTexture,
                propertyType            = src.propertyType,
                targetUdon              = src.targetUdon,
                udonEventName           = src.udonEventName,
                udonVariableName        = src.udonVariableName,
                udonVariableType        = src.udonVariableType,
                udonVariableStringValue = src.udonVariableStringValue,
                targetComponent         = src.targetComponent,
                udonEventScope          = src.udonEventScope,
                transformSpace          = src.transformSpace,
                teleportRotationEuler   = src.teleportRotationEuler,
                teleportDestination     = src.teleportDestination,
                useStep                 = src.useStep,
                stepAmount              = src.stepAmount,
                stepMin                 = src.stepMin,
                stepMax                 = src.stepMax,
                useDelay                = src.useDelay,
                delaySeconds            = src.delaySeconds,
                colorTargetRenderer     = src.colorTargetRenderer,
                colorMaterialIndex      = src.colorMaterialIndex,
                colorPropertyName       = src.colorPropertyName,
                colorSelectorRole       = src.colorSelectorRole,
                colorGroupName          = src.colorGroupName,
                presetRole              = src.presetRole,
                presetScope             = src.presetScope,
                presetIncludeFaders          = src.presetIncludeFaders,
                presetIncludeStepValues      = src.presetIncludeStepValues,
                presetIncludeColorPalettes   = src.presetIncludeColorPalettes,
                autoChangeGroupName     = src.autoChangeGroupName,
                autoChangeGroupInterval = src.autoChangeGroupInterval,
                category                = src.category,
                target                  = src.target,
                operation               = src.operation,
                navFolderTarget         = src.navFolderTarget,
                navPageTarget           = src.navPageTarget,
                variantSelectorRole     = src.variantSelectorRole,
                variantGroupName        = src.variantGroupName,
                commandTargetState      = src.commandTargetState,
                statMetric              = src.statMetric,
                useMomentary            = src.useMomentary,
                useCondition            = src.useCondition,
                conditionFolderIndex    = src.conditionFolderIndex,
                conditionEntryIndex     = src.conditionEntryIndex,
                conditionRequireActive  = src.conditionRequireActive,
            };

            // Arrays that need independent copies:
            if (src.paletteColors != null)
            {
                dst.paletteColors = new Color[src.paletteColors.Length];
                Array.Copy(src.paletteColors, dst.paletteColors, src.paletteColors.Length);
            }

            if (src.presetIncludedFolderIndices != null)
            {
                dst.presetIncludedFolderIndices = new int[src.presetIncludedFolderIndices.Length];
                Array.Copy(src.presetIncludedFolderIndices, dst.presetIncludedFolderIndices,
                           src.presetIncludedFolderIndices.Length);
            }

            if (src.variantItems != null)
            {
                dst.variantItems = new EnigmaVariantItem[src.variantItems.Length];
                for (int i = 0; i < src.variantItems.Length; i++)
                {
                    EnigmaVariantItem vi = src.variantItems[i];
                    dst.variantItems[i] = vi == null ? null : new EnigmaVariantItem
                    {
                        variantName  = vi.variantName,
                        floatValue   = vi.floatValue,
                        colorValue   = vi.colorValue,
                        vectorValue  = vi.vectorValue,
                        textureValue = vi.textureValue,
                    };
                }
            }

            return dst;
        }

        /// <summary>Deep-copies an <see cref="EnigmaFaderLinkData"/>, preserving
        /// all scene/asset object references.</summary>
        private static EnigmaFaderLinkData DeepCopyFaderLink(EnigmaFaderLinkData src)
        {
            if (src == null) return new EnigmaFaderLinkData();
            return new EnigmaFaderLinkData
            {
                targetRenderer       = src.targetRenderer,
                materialIndex        = src.materialIndex,
                propertyName         = src.propertyName,
                propertyType         = src.propertyType,
                minValue             = src.minValue,
                maxValue             = src.maxValue,
                defaultValue         = src.defaultValue,
                defaultColor         = src.defaultColor,
                colorIndicatorEnabled = src.colorIndicatorEnabled,
                indicatorColor       = src.indicatorColor,
                indicatorConditional = src.indicatorConditional,
            };
        }

    }
}
#endif

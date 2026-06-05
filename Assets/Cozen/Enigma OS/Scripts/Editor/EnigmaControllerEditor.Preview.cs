#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System.Collections.Generic;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Inspector preview panel — always visible, acts as the primary navigation surface.
    ///
    /// Folder management (add / rename / reorder / delete) lives here.
    /// Clicking a button in the grid selects that entry for editing in the
    /// "Selected Button Settings" section below; no toggle simulation is performed.
    /// Exclusive-group members share a color tint so grouping is visually apparent.
    /// </summary>
    public partial class EnigmaControllerEditor
    {
        // ── Preview / selection state (persisted via SessionState in OnEnable/OnInspectorGUI) ──
        private int _previewFolderIndex;
        private int _previewPageIndex;
        private bool _showFolderReorderList;
        private ReorderableList _folderReorderList;
        private List<EnigmaFolderData> _folderReorderSource;

        /// <summary>
        /// Index of the selected entry within the current folder's entries array.
        /// -1 means nothing is selected.  Updated by <see cref="HandlePreviewClick"/>.
        /// Read by <see cref="DrawSelectedButtonSettings"/> to display the correct entry.
        /// </summary>
        private int _selectedLocalEntryIndex;

        // ── Drag-reorder state ──
        private int     _dragSourceLocalIdx = -1;  // entry being dragged (-1 = idle)
        private int     _dragOverLocalIdx   = -1;  // slot the mouse is currently hovering
        private bool    _isDragging         = false; // true once mouse has moved after MouseDown
        private Vector2 _dragMousePos       = Vector2.zero; // last known cursor position during drag

        // ── Cross-page drag state ──
        // When the cursor hovers over a page-flip zone during a drag we start a
        // short timer.  Once the timer expires the page flips automatically.
        private const  float kPageFlipHoldTime = 0.4f;   // seconds to hold before flip
        private const  float kPageFlipZoneH    = 28f;    // height of the flip strip (px)
        private bool   _pageFlipPrevHeld = false;         // cursor in "prev page" zone
        private bool   _pageFlipNextHeld = false;         // cursor in "next page" zone
        private double _pageFlipHoldStart = 0;            // EditorApplication.timeSinceStartup when hold began

        // Distinct hues used to tint exclusive-group members (same group → same tint)
        private static readonly float[] _groupHues = { 0f, 0.1f, 0.25f, 0.55f, 0.7f, 0.85f };

        // Accent colour used for the selected button in the grid
        private static readonly Color _selectionColor = new Color(0.27f, 0.76f, 0.94f, 1f);

        // ── Dynamic layout presets ────────────────────────────────────────────
        // Rebuilt whenever the assigned-button count changes.
        // Format: Rows × Columns.  Only layouts where grid size equals the
        // button count OR exceeds it by exactly 1 ("1 empty") are included.
        private int[]    _validPresetRows  = new int[0];
        private int[]    _validPresetCols  = new int[0];
        private string[] _validPresetNames = new string[0];
        private int      _presetsForCount  = -1;   // cache key
        private int      _defaultPresetIndex = 0;  // most-square preset, cols ≥ rows

        private void RebuildPresetsIfNeeded(int buttonCount)
        {
            if (_presetsForCount == buttonCount) return;
            _presetsForCount = buttonCount;

            var rows  = new List<int>();
            var cols  = new List<int>();
            var names = new List<string>();

            for (int r = 1; r <= buttonCount; r++)
            {
                int c     = Mathf.CeilToInt((float)buttonCount / r);
                int waste = r * c - buttonCount;
                if (waste <= 1)
                {
                    string suffix = waste == 1 ? " (1 empty)" : "";
                    rows.Add(r);
                    cols.Add(c);
                    names.Add($"{r} \u00d7 {c}{suffix}");   // "r × c"
                }
            }

            _validPresetRows  = rows.ToArray();
            _validPresetCols  = cols.ToArray();
            _validPresetNames = names.ToArray();

            // Default to the most-square preset, preferring cols ≥ rows (wider than tall).
            // Ties in squareness are broken by preferring the wider (cols ≥ rows) option.
            _defaultPresetIndex = 0;
            int  bestDiff = int.MaxValue;
            bool bestWide = false;
            for (int p = 0; p < _validPresetRows.Length; p++)
            {
                int  diff = Mathf.Abs(_validPresetCols[p] - _validPresetRows[p]);
                bool wide = _validPresetCols[p] >= _validPresetRows[p];
                if (diff < bestDiff || (diff == bestDiff && wide && !bestWide))
                {
                    bestDiff            = diff;
                    bestWide            = wide;
                    _defaultPresetIndex = p;
                }
            }
        }

        // ── Sparse-array helpers ──────────────────────────────────────────────

        /// <summary>
        /// Returns true when entries[idx] holds a real (non-empty) entry.
        /// </summary>
        private static bool IsEntryFilled(EnigmaEntryData[] entries, int idx)
            => idx >= 0 && idx < entries.Length && !entries[idx].isEmpty;

        /// <summary>
        /// Ensures <paramref name="folder"/>.entries is large enough to cover at
        /// least <paramref name="minCapacity"/> slots.  The array is always grown
        /// to the next multiple of <paramref name="assignedCount"/> so page
        /// boundaries stay aligned.  New slots are filled with isEmpty entries.
        /// </summary>
        private static void EnsureEntriesCapacity(EnigmaFolderData folder, int assignedCount)
        {
            if (assignedCount <= 0) return;

            int existing  = folder.entries.Length;
            // Minimum: at least one full page.
            int minLen    = assignedCount;
            // If there are already more entries, round up to a page boundary.
            if (existing > minLen)
                minLen = Mathf.CeilToInt((float)existing / assignedCount) * assignedCount;

            if (existing >= minLen) return;

            var extended = new EnigmaEntryData[minLen];
            for (int i = 0; i < existing; i++)
                extended[i] = folder.entries[i];
            for (int i = existing; i < minLen; i++)
                extended[i] = new EnigmaEntryData { isEmpty = true };
            folder.entries = extended;
        }

        private void DrawPreview()
        {
            EnigmaController ctrl    = (EnigmaController)target;

            // See DrawSelectedButtonSettings for the rationale on event-gated
            // Undo.RecordObject: we need the record so direct-field writes in
            // the preview grid (folder rename, reorder, etc.) persist, but
            // doing it every Layout/Repaint frame on Mochie-scale data froze
            // Unity for 1.7-3.2s per commit. Gating on mutation-capable events
            // keeps persistence correct while leaving hot repaint frames free.
            //
            // Extra gate for MouseDrag: skip when GUIUtility.hotControl != 0.
            // That means a foreign IMGUI control (a FloatField label-scrub on
            // an action's Value field, a slider thumb, etc.) owns the active
            // drag — DrawPreview has no pending mutation. Without this gate,
            // every MouseDrag tick of a value-field scrub forced a full
            // snapshot of EnigmaControllerData (~15k serialized values), which
            // queued a per-frame undo/prefab-diff that blocked Unity's main
            // thread for ~1.5s/tick on Mochie-FX-scale controllers. DrawPreview's
            // own folder-reorder uses custom drag tracking that does NOT set
            // hotControl, so this gate doesn't block its undo.
            var pvEvt = Event.current;
            bool pvShouldRecord = IsMutationEvent(pvEvt)
                && !(pvEvt.type == EventType.MouseDrag && GUIUtility.hotControl != 0);
            if (pvShouldRecord)
            {
                var dataComp = ctrl.GetComponent<EnigmaControllerData>();
                if (dataComp != null)
                {
                    using (new EnigmaPerfProbe.PerfTrace("Preview.Undo.RecordObject(dataComp)"))
                        Undo.RecordObject(dataComp, "Modify Enigma Preview");
                }
            }

            EnigmaFolderData[] folders = ctrl.GetFolders() ?? new EnigmaFolderData[0];

            // ═══════════════════════════════════════════════════════════════════
            //  FOLDER MANAGEMENT BAR
            // ═══════════════════════════════════════════════════════════════════

            EditorGUILayout.BeginVertical(_previewSectionStyle);

            EditorGUILayout.BeginHorizontal();

            if (folders.Length > 0)
            {
                // Clamp selection index in case folders were removed externally.
                _previewFolderIndex = Mathf.Clamp(_previewFolderIndex, 0, folders.Length - 1);

                string[] folderNames = BuildUniqueFolderNames(folders);
                string currentName = folderNames[_previewFolderIndex];

                Rect dropdownRect = GUILayoutUtility.GetRect(
                    new GUIContent(currentName), EditorStyles.popup, GUILayout.ExpandWidth(true));

                if (EditorGUI.DropdownButton(dropdownRect, new GUIContent(currentName), FocusType.Passive))
                {
                    if (EditorApplication.isPlaying)
                    {
                        // In play mode, show a simple selection menu (no reordering).
                        var menu = new GenericMenu();
                        for (int fi = 0; fi < folderNames.Length; fi++)
                        {
                            int capturedIdx = fi;
                            menu.AddItem(new GUIContent(folderNames[fi]), fi == _previewFolderIndex, () =>
                            {
                                _previewFolderIndex      = capturedIdx;
                                _selectedLocalEntryIndex = -1;
                                _previewPageIndex        = 0;
                                Repaint();
                            });
                        }
                        menu.DropDown(dropdownRect);
                    }
                    else
                    {
                        _showFolderReorderList = !_showFolderReorderList;
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No folders yet", EditorStyles.miniLabel,
                    GUILayout.ExpandWidth(true));
            }

            // Navigate folders
            using (new EditorGUI.DisabledScope(folders.Length == 0 || _previewFolderIndex <= 0))
            {
                if (GUILayout.Button("\u25C0", GUILayout.Width(24)))
                {
                    int slotCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
                    TrimEmptyTrailingPages(folders[_previewFolderIndex], slotCount, ctrl);
                    _previewFolderIndex--;
                    _selectedLocalEntryIndex = -1;
                    _previewPageIndex = 0;
                }
            }
            using (new EditorGUI.DisabledScope(folders.Length == 0 || _previewFolderIndex >= folders.Length - 1))
            {
                if (GUILayout.Button("\u25B6", GUILayout.Width(24)))
                {
                    int slotCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
                    TrimEmptyTrailingPages(folders[_previewFolderIndex], slotCount, ctrl);
                    _previewFolderIndex++;
                    _selectedLocalEntryIndex = -1;
                    _previewPageIndex = 0;
                }
            }

            // Delete folder
            using (new EditorGUI.DisabledScope(folders.Length == 0 || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    var list = new List<EnigmaFolderData>(folders);
                    list.RemoveAt(_previewFolderIndex);
                    ctrl.SetFolders(list.ToArray());
                    MarkDataDirty(ctrl);
                    _previewFolderIndex      = Mathf.Clamp(_previewFolderIndex, 0, Mathf.Max(0, list.Count - 1));
                    _selectedLocalEntryIndex = -1;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }
            }

            // Add folder
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if (GUILayout.Button("+ Folder", GUILayout.Width(70)))
            {
                var list = new List<EnigmaFolderData>(folders);
                list.Add(new EnigmaFolderData { name = "New Folder " + (folders.Length + 1) });
                ctrl.SetFolders(list.ToArray());
                MarkDataDirty(ctrl);
                // Re-fetch so the rename field below picks up the new entry.
                folders             = ctrl.GetFolders();
                _previewFolderIndex = folders.Length - 1;
                _selectedLocalEntryIndex = -1;
            }

            EditorGUILayout.EndHorizontal();

            // Inline reorderable folder list (toggled by dropdown button)
            if (_showFolderReorderList && folders.Length > 0 && !EditorApplication.isPlaying)
            {
                // Rebuild the list if the source data changed (folders added/removed).
                if (_folderReorderSource == null
                    || _folderReorderSource.Count != folders.Length
                    || (_folderReorderSource.Count > 0 && _folderReorderSource[0] != folders[0]))
                {
                    _folderReorderSource = new List<EnigmaFolderData>(folders);
                    _folderReorderList = new ReorderableList(_folderReorderSource, typeof(EnigmaFolderData),
                        true, false, false, false);
                    _folderReorderList.headerHeight = 0f;
                    _folderReorderList.index = _previewFolderIndex;

                    _folderReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        rect.y += 1f;
                        rect.height = EditorGUIUtility.singleLineHeight;
                        if (index == _previewFolderIndex)
                            EditorGUI.DrawRect(new Rect(rect.x, rect.y - 1f, rect.width, rect.height + 2f),
                                new Color(0.24f, 0.48f, 0.9f, 0.25f));
                        string name = _folderReorderSource[index].name;
                        if (string.IsNullOrEmpty(name)) name = "(unnamed)";
                        EditorGUI.LabelField(rect, name);
                    };

                    _folderReorderList.onSelectCallback = list =>
                    {
                        int slotCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
                        TrimEmptyTrailingPages(folders[_previewFolderIndex], slotCount, ctrl);
                        _previewFolderIndex      = list.index;
                        _selectedLocalEntryIndex = -1;
                        _previewPageIndex        = 0;
                    };

                    _folderReorderList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                    {
                        // Track the previously selected folder through the reorder.
                        var tracked = folders[_previewFolderIndex];
                        ctrl.SetFolders(_folderReorderSource.ToArray());
                        MarkDataDirty(ctrl);
                        folders = ctrl.GetFolders();
                        int newIdx = System.Array.IndexOf(folders, tracked);
                        _previewFolderIndex = newIdx >= 0 ? newIdx : 0;
                        _folderReorderList.index = _previewFolderIndex;
                        // Force list rebuild on next frame with fresh data.
                        _folderReorderSource = null;
                    };
                }

                _folderReorderList.index = _previewFolderIndex;
                _folderReorderList.DoLayoutList();
            }

            // Folder name rename field
            if (folders.Length > 0)
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                {
                    var folder = folders[_previewFolderIndex];
                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.TextField("Name", folder.name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        folder.name = newName;
                        MarkDataDirty(ctrl);
                    }
                }
            }

            EditorGUILayout.EndVertical();

            if (folders.Length == 0)
                return;

            // ═══════════════════════════════════════════════════════════════════
            //  LAYOUT CONTROLS
            // ═══════════════════════════════════════════════════════════════════

            int assignedCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;

            SerializedProperty colsProp = _so.FindProperty("previewColumns");
            SerializedProperty rowsProp = _so.FindProperty("previewRows");

            // Read the definitive values.
            int cols = Mathf.Max(1, colsProp.intValue);
            int rows = Mathf.Max(1, rowsProp.intValue);

            if (assignedCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No button slots assigned — assign EnigmaManagedButton slots under Hardware to enable the preview grid.",
                    MessageType.Warning);
            }

            // ═══════════════════════════════════════════════════════════════════
            //  PAGE NAVIGATION
            // ═══════════════════════════════════════════════════════════════════

            var activeFolder = folders[_previewFolderIndex];

            // ── Remap entries when per-page slot count changed ────────────────
            // When buttonSlots.Length changes the entry index formula changes:
            //   index = page * slotsPerPage + slot
            // so every entry on pages > 0 shifts position.  Remap all folders
            // while we still know the old count (persisted in lastButtonSlotCount).
            if (assignedCount > 0)
            {
                int savedCount = ctrl.GetLastButtonSlotCount();
                if (savedCount > 0 && savedCount != assignedCount)
                {
                    foreach (var f in folders)
                        if (f != null)
                            RemapEntriesForNewSlotCount(f, savedCount, assignedCount, ctrl);
                    Debug.Log($"[EnigmaController] Button slot count {savedCount}->{assignedCount}: " +
                              $"entry layout remapped for all folders.");
                }
                if (savedCount != assignedCount)
                {
                    ctrl.SetLastButtonSlotCount(assignedCount);
                    MarkDataDirty(ctrl);
                }
            }

            // Ensure the entries array is always sized to a multiple of assignedCount
            // so every button slot maps to a fixed array index.
            if (assignedCount > 0)
                EnsureEntriesCapacity(activeFolder, assignedCount);

            int slotsPerPage = Mathf.Max(1, assignedCount);
            // With a sparse fixed-size array, length is always a multiple of slotsPerPage.
            int totalPages   = assignedCount > 0
                               ? Mathf.Max(1, activeFolder.entries.Length / slotsPerPage)
                               : 1;
            _previewPageIndex = Mathf.Clamp(_previewPageIndex, 0, totalPages - 1);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Page  {_previewPageIndex + 1} / {totalPages}", GUILayout.Width(100));
            using (new EditorGUI.DisabledScope(_previewPageIndex <= 0))
            {
                if (GUILayout.Button("◀", GUILayout.Width(30)))
                {
                    TrimEmptyTrailingPages(activeFolder, assignedCount, ctrl);
                    _previewPageIndex--;
                    _selectedLocalEntryIndex = -1;
                }
            }
            using (new EditorGUI.DisabledScope(_previewPageIndex >= totalPages - 1))
            {
                if (GUILayout.Button("▶", GUILayout.Width(30))) _previewPageIndex++;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (assignedCount > 0 && GUILayout.Button("+ Page", GUILayout.Width(56)))
                {
                    AddPageToFolder(activeFolder, assignedCount, ctrl);
                    _previewPageIndex = totalPages;
                }
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Templates", GUILayout.Width(80)))
                    ShowTemplatePickerForFolder(ctrl, _previewFolderIndex);
            }
            EditorGUILayout.EndHorizontal();

            // ═══════════════════════════════════════════════════════════════════
            //  BUTTON GRID  (fixed-size cells, drag-to-reorder)
            // ═══════════════════════════════════════════════════════════════════

            if (assignedCount > 0)
            {
                EditorGUILayout.Space(4);

                // Fixed cell dimensions — independent of label length.
                const float kCellH = 50f;
                const float kGap   = 3f;

                // Available width inside the inspector panel (minus indent + padding + right margin + scrollbar).
                float availW = EditorGUIUtility.currentViewWidth
                               - EditorGUI.indentLevel * 15f - 41f;
                float cellW  = Mathf.Floor((availW - kGap * (cols - 1)) / cols);
                float gridH  = rows * kCellH + (rows - 1) * kGap;

                // Reserve the entire grid as a single layout rect so nothing below
                // shifts when the grid content changes.
                Rect gridRect     = EditorGUILayout.GetControlRect(false, gridH);
                int  gridCtrlId   = GUIUtility.GetControlID(FocusType.Passive, gridRect);

                // Page-flip strips — overlaid on the top and bottom rows of the grid.
                // Only shown when actively dragging on a multi-page folder.
                bool canFlipPrev  = _previewPageIndex > 0;
                bool canFlipNext  = _previewPageIndex < totalPages - 1;
                bool showFlipZones = _isDragging && totalPages > 1;

                // Top strip: covers the top kPageFlipZoneH pixels of the grid
                Rect flipPrevRect = new Rect(gridRect.x, gridRect.y,
                                            gridRect.width, kPageFlipZoneH);
                // Bottom strip: covers the bottom kPageFlipZoneH pixels of the grid
                Rect flipNextRect = new Rect(gridRect.x, gridRect.yMax - kPageFlipZoneH,
                                            gridRect.width, kPageFlipZoneH);

                Event e        = Event.current;
                var groupTints = BuildGroupTints(activeFolder);
                int pageOffset = _previewPageIndex * assignedCount;

                // ── DRAW PASS (Repaint only) ──────────────────────────────────

                if (e.type == EventType.Repaint)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            int slot      = row * cols + col;
                            int localIdx  = pageOffset + slot;
                            bool slotAssigned = slot < assignedCount;
                            bool hasEntry     = slotAssigned && IsEntryFilled(activeFolder.entries, localIdx);
                            bool isSelected   = hasEntry && localIdx == _selectedLocalEntryIndex;
                            bool isDragSrc    = _isDragging && localIdx == _dragSourceLocalIdx;
                            bool isDragOver   = slotAssigned
                                               && localIdx == _dragOverLocalIdx
                                               && _dragSourceLocalIdx >= 0
                                               && _dragSourceLocalIdx != _dragOverLocalIdx;

                            Rect cell = new Rect(
                                gridRect.x + col * (cellW + kGap),
                                gridRect.y + row * (kCellH + kGap),
                                cellW, kCellH);

                            // ── Background colour ─────────────────────────────
                            Color bg;
                            if (!slotAssigned)
                                bg = new Color(0.15f, 0.15f, 0.15f);
                            else if (isDragOver)
                                bg = new Color(1f, 0.76f, 0.1f, 0.9f);
                            else if (isSelected)
                                bg = _selectionColor;
                            else if (hasEntry)
                            {
                                var entry = activeFolder.entries[localIdx];
                                if (entry.onByDefault)
                                    bg = ctrl.activeColor;
                                else if (entry.useExclusiveGroup
                                    && !string.IsNullOrEmpty(entry.exclusiveGroup)
                                    && groupTints.TryGetValue(FirstExclusiveTag(entry.exclusiveGroup), out Color tint))
                                    bg = Color.Lerp(ctrl.inactiveColor, tint, 0.3f);
                                else
                                    bg = ctrl.inactiveColor;
                            }
                            else
                                bg = new Color(0.35f, 0.35f, 0.35f);

                            // Dim the source cell while dragging.
                            if (isDragSrc)
                                bg = Color.Lerp(bg, Color.black, 0.4f);

                            EditorGUI.DrawRect(cell, bg);

                            // Border overlays.
                            if (isDragOver)
                                DrawCellBorder(cell, new Color(1f, 0.76f, 0.1f), 2f);
                            else if (isSelected)
                                DrawCellBorder(cell, Color.white);
                            if (isDragSrc)
                                DrawCellBorder(cell, new Color(0.8f, 0.8f, 0.8f, 0.5f));

                            string cellLabel = hasEntry ? activeFolder.entries[localIdx].label
                                                        : (slotAssigned ? "+" : "");
                            Color savedContent = GUI.contentColor;
                            GUI.contentColor   = ContrastTextColor(bg);
                            GUI.Label(cell, cellLabel, _previewButtonStyle);
                            GUI.contentColor   = savedContent;
                        }
                    }

                    // ── Floating ghost card following the cursor ──────────────
                    // Only render the ghost when dragging a filled entry.
                    if (_isDragging && _dragSourceLocalIdx >= 0
                        && IsEntryFilled(activeFolder.entries, _dragSourceLocalIdx))
                    {
                        bool   srcHasEntry = IsEntryFilled(activeFolder.entries, _dragSourceLocalIdx);
                        string ghostLabel  = srcHasEntry
                            ? activeFolder.entries[_dragSourceLocalIdx].label : "+";

                        Color ghostBg;
                        if (srcHasEntry)
                        {
                            var src = activeFolder.entries[_dragSourceLocalIdx];
                            if (src.onByDefault)
                                ghostBg = ctrl.activeColor;
                            else if (src.useExclusiveGroup
                                && !string.IsNullOrEmpty(src.exclusiveGroup)
                                && groupTints.TryGetValue(FirstExclusiveTag(src.exclusiveGroup), out Color tint))
                                ghostBg = Color.Lerp(ctrl.inactiveColor, tint, 0.3f);
                            else
                                ghostBg = ctrl.inactiveColor;
                        }
                        else
                            ghostBg = new Color(0.35f, 0.35f, 0.35f);

                        Rect ghostRect = new Rect(
                            _dragMousePos.x - cellW  * 0.5f,
                            _dragMousePos.y - kCellH * 0.5f,
                            cellW, kCellH);

                        const float kGhostAlpha = 0.80f;
                        EditorGUI.DrawRect(ghostRect,
                            new Color(ghostBg.r, ghostBg.g, ghostBg.b, kGhostAlpha));
                        DrawCellBorder(ghostRect,
                            new Color(1f, 1f, 1f, kGhostAlpha), 2f);

                        Color prev        = GUI.color;
                        Color prevContent = GUI.contentColor;
                        GUI.color         = new Color(1f, 1f, 1f, kGhostAlpha);
                        GUI.contentColor  = ContrastTextColor(ghostBg);
                        GUI.Label(ghostRect, ghostLabel, _previewButtonStyle);
                        GUI.color        = prev;
                        GUI.contentColor = prevContent;
                    }

                    // ── Page-flip zones (shown only during a drag when multi-page) ──
                    if (showFlipZones)
                    {
                        double elapsed = EditorApplication.timeSinceStartup - _pageFlipHoldStart;
                        float  fill    = Mathf.Clamp01((float)(elapsed / kPageFlipHoldTime));

                        if (canFlipPrev)
                        {
                            Color zoneBase = _pageFlipPrevHeld
                                ? Color.Lerp(new Color(0.2f, 0.5f, 1f, 0.5f),
                                             new Color(0.2f, 0.8f, 1f, 0.85f), fill)
                                : new Color(0.2f, 0.4f, 0.8f, 0.3f);
                            EditorGUI.DrawRect(flipPrevRect, zoneBase);
                            if (_pageFlipPrevHeld)
                            {
                                // Progress bar overlay showing hold progress
                                Rect prog = new Rect(flipPrevRect.x, flipPrevRect.y,
                                                     flipPrevRect.width * fill, flipPrevRect.height);
                                EditorGUI.DrawRect(prog, new Color(0.3f, 0.7f, 1f, 0.35f));
                            }
                            GUI.Label(flipPrevRect, "◀  Prev Page", _previewButtonStyle);
                        }

                        if (canFlipNext)
                        {
                            Color zoneBase = _pageFlipNextHeld
                                ? Color.Lerp(new Color(0.2f, 0.5f, 1f, 0.5f),
                                             new Color(0.2f, 0.8f, 1f, 0.85f), fill)
                                : new Color(0.2f, 0.4f, 0.8f, 0.3f);
                            EditorGUI.DrawRect(flipNextRect, zoneBase);
                            if (_pageFlipNextHeld)
                            {
                                Rect prog = new Rect(flipNextRect.x, flipNextRect.y,
                                                     flipNextRect.width * fill, flipNextRect.height);
                                EditorGUI.DrawRect(prog, new Color(0.3f, 0.7f, 1f, 0.35f));
                            }
                            GUI.Label(flipNextRect, "Next Page  ▶", _previewButtonStyle);
                        }
                    }
                }

                // ── EVENT PASS ────────────────────────────────────────────────

                // Shared helper: find which assigned slot rect contains a point.
                System.Func<Vector2, int> findSlot = (pos) =>
                {
                    for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int s = r * cols + c;
                        if (s >= assignedCount) continue;
                        Rect cr = new Rect(
                            gridRect.x + c * (cellW + kGap),
                            gridRect.y + r * (kCellH + kGap),
                            cellW, kCellH);
                        if (cr.Contains(pos)) return pageOffset + s;
                    }
                    return -1;
                };

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    int hit = findSlot(e.mousePosition);
                    if (hit >= 0)
                    {
                        GUIUtility.hotControl = gridCtrlId;
                        _dragSourceLocalIdx   = hit;
                        _dragOverLocalIdx     = hit;
                        _isDragging           = false;
                        _dragMousePos         = e.mousePosition;
                        // In play mode, skip drag tracking — only allow click-to-select.
                        if (EditorApplication.isPlaying)
                        {
                            bool clickHasEntry = IsEntryFilled(activeFolder.entries, hit);
                            if (clickHasEntry)
                                HandlePreviewClick(hit);
                            _dragSourceLocalIdx = -1;
                        }
                        e.Use();
                    }
                }
                else if (e.type == EventType.MouseDrag
                         && GUIUtility.hotControl == gridCtrlId)
                {
                    _isDragging   = true;
                    _dragMousePos = e.mousePosition;

                    // Update which cell the cursor is currently over.
                    int hovered = findSlot(e.mousePosition);
                    _dragOverLocalIdx = hovered >= 0 ? hovered : _dragSourceLocalIdx;

                    // ── Page-flip zone handling ──
                    bool inPrev = canFlipPrev && flipPrevRect.Contains(e.mousePosition);
                    bool inNext = canFlipNext && flipNextRect.Contains(e.mousePosition);

                    if (inPrev)
                    {
                        if (!_pageFlipPrevHeld)
                        {
                            _pageFlipPrevHeld  = true;
                            _pageFlipNextHeld  = false;
                            _pageFlipHoldStart = EditorApplication.timeSinceStartup;
                        }
                        else if (EditorApplication.timeSinceStartup - _pageFlipHoldStart >= kPageFlipHoldTime)
                        {
                            _previewPageIndex--;
                            pageOffset = _previewPageIndex * assignedCount;
                            _pageFlipHoldStart = EditorApplication.timeSinceStartup; // reset for continuous flip
                        }
                    }
                    else if (inNext)
                    {
                        if (!_pageFlipNextHeld)
                        {
                            _pageFlipNextHeld  = true;
                            _pageFlipPrevHeld  = false;
                            _pageFlipHoldStart = EditorApplication.timeSinceStartup;
                        }
                        else if (EditorApplication.timeSinceStartup - _pageFlipHoldStart >= kPageFlipHoldTime)
                        {
                            // Ensure target page exists before navigating to it.
                            EnsureEntriesCapacity(activeFolder, assignedCount * (_previewPageIndex + 2));
                            _previewPageIndex++;
                            pageOffset = _previewPageIndex * assignedCount;
                            _pageFlipHoldStart = EditorApplication.timeSinceStartup;
                        }
                    }
                    else
                    {
                        _pageFlipPrevHeld = false;
                        _pageFlipNextHeld = false;
                    }

                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0
                         && GUIUtility.hotControl == gridCtrlId)
                {
                    GUIUtility.hotControl = 0;

                    int releaseIdx = findSlot(e.mousePosition);
                    bool isRealDrag = _isDragging
                                     && releaseIdx >= 0
                                     && releaseIdx != _dragSourceLocalIdx;

                    if (isRealDrag && !EditorApplication.isPlaying)
                    {
                        if (IsEntryFilled(activeFolder.entries, _dragSourceLocalIdx))
                        {
                            // Swap source and destination.
                            // • Filled → empty : source becomes empty, destination gets the entry.
                            // • Filled → filled : the two entries are exchanged.
                            SwapEntries(activeFolder.entries, _dragSourceLocalIdx, releaseIdx);
                            _selectedLocalEntryIndex = releaseIdx;
                            MarkDataDirty(ctrl);
                        }
                    }
                    else if (!_isDragging && releaseIdx == _dragSourceLocalIdx)
                    {
                        // Pure click (no movement).
                        bool clickHasEntry = IsEntryFilled(activeFolder.entries, releaseIdx);
                        if (releaseIdx >= 0)
                        {
                            if (clickHasEntry)
                            {
                                HandlePreviewClick(releaseIdx);
                            }
                            else if (!EditorApplication.isPlaying)
                            {
                                // Empty slot clicked — create a new entry at that exact slot.
                                activeFolder.entries[releaseIdx] = NewDefaultEntry();
                                MarkDataDirty(ctrl);
                                _selectedLocalEntryIndex = releaseIdx;
                            }
                        }
                    }

                    _dragSourceLocalIdx = -1;
                    _dragOverLocalIdx   = -1;
                    _isDragging         = false;
                    _pageFlipPrevHeld   = false;
                    _pageFlipNextHeld   = false;
                    Repaint();
                    e.Use();
                }
            }

        }

        // ════════════════════════════════════════════════════════════════════════
        //  PREVIEW HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called when a grid button for an existing entry is clicked.
        /// Selects the entry for editing; clicking the already-selected entry deselects it.
        /// </summary>
        private void HandlePreviewClick(int localIdx)
        {
            _selectedLocalEntryIndex = (localIdx == _selectedLocalEntryIndex) ? -1 : localIdx;
            // Clear keyboard focus so text fields don't retain stale values
            // from the previously selected entry.
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        /// <summary>
        /// Returns the first non-empty, trimmed tag from a comma-separated
        /// <c>exclusiveGroup</c> string, or the original string if none is found.
        /// Used to pick a consistent tint color for multi-tag entries.
        /// </summary>
        private static string FirstExclusiveTag(string exclusiveGroup)
        {
            foreach (string raw in exclusiveGroup.Split(','))
            {
                string t = raw.Trim();
                if (!string.IsNullOrEmpty(t)) return t;
            }
            return string.Empty;
        }

        /// <summary>
        /// Builds a mapping from exclusive-group tag → tint color for a folder.
        /// Each distinct tag (split from comma-separated <c>exclusiveGroup</c> strings)
        /// gets a unique hue from <see cref="_groupHues"/>.
        /// </summary>
        private static Dictionary<string, Color> BuildGroupTints(EnigmaFolderData folder)
        {
            var result   = new Dictionary<string, Color>();
            int hueIndex = 0;
            foreach (var entry in folder.entries)
            {
                if (!entry.useExclusiveGroup || string.IsNullOrEmpty(entry.exclusiveGroup)) continue;
                foreach (string rawTag in entry.exclusiveGroup.Split(','))
                {
                    string tag = rawTag.Trim();
                    if (string.IsNullOrEmpty(tag) || result.ContainsKey(tag)) continue;
                    float hue = _groupHues[hueIndex % _groupHues.Length];
                    result[tag] = Color.HSVToRGB(hue, 0.65f, 0.9f);
                    hueIndex++;
                }
            }
            return result;
        }

        /// <summary>
        /// Draws a thin border rectangle around <paramref name="r"/> using
        /// <paramref name="color"/>. Used for selected and drag-over cell highlights.
        /// </summary>
        private static void DrawCellBorder(Rect r, Color color, float t = 1.5f)
        {
            EditorGUI.DrawRect(new Rect(r.x,          r.y,          r.width, t),       color); // top
            EditorGUI.DrawRect(new Rect(r.x,          r.yMax - t,   r.width, t),       color); // bottom
            EditorGUI.DrawRect(new Rect(r.x,          r.y,          t, r.height),      color); // left
            EditorGUI.DrawRect(new Rect(r.xMax - t,   r.y,          t, r.height),      color); // right
        }

        /// <summary>
        /// Returns black or white — whichever has higher contrast against
        /// <paramref name="bg"/> — using the ITU-R BT.601 perceived-luminance formula.
        /// </summary>
        private static Color ContrastTextColor(Color bg)
        {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.5f ? Color.black : Color.white;
        }

        /// <summary>
        /// Removes fully-empty trailing pages from <paramref name="folder"/>.entries,
        /// keeping at least one page.  A page is "empty" when every entry slot on it
        /// has <c>isEmpty == true</c>.  This runs when the user navigates away from a
        /// page, so an accidentally-added blank page disappears automatically.
        /// </summary>
        private static void TrimEmptyTrailingPages(
            EnigmaFolderData folder, int assignedCount, EnigmaController ctrl)
        {
            if (assignedCount <= 0 || folder.entries.Length <= assignedCount) return;

            int pages     = folder.entries.Length / assignedCount;
            int keepPages = pages;

            while (keepPages > 1)
            {
                int pageStart = (keepPages - 1) * assignedCount;
                bool pageEmpty = true;
                for (int i = 0; i < assignedCount; i++)
                {
                    int idx = pageStart + i;
                    if (idx < folder.entries.Length && !folder.entries[idx].isEmpty)
                    {
                        pageEmpty = false;
                        break;
                    }
                }
                if (!pageEmpty) break;
                keepPages--;
            }

            if (keepPages == pages) return;  // nothing to trim

            var trimmed = new EnigmaEntryData[keepPages * assignedCount];
            System.Array.Copy(folder.entries, trimmed, trimmed.Length);
            folder.entries = trimmed;
            MarkDataDirtyStatic(ctrl);
        }

        /// <summary>
        /// Re-organises <paramref name="folder"/>.entries so that every entry keeps
        /// its logical (page, slot) position when the number of buttons per page
        /// changes from <paramref name="oldCount"/> to <paramref name="newCount"/>.
        ///
        /// Entries in slots beyond <paramref name="newCount"/> on a given page are
        /// discarded (no room in the new layout).  New slots are filled with empty
        /// entries.  The resulting array length is always a multiple of
        /// <paramref name="newCount"/> with the same number of pages as before.
        /// </summary>
        private static void RemapEntriesForNewSlotCount(
            EnigmaFolderData folder, int oldCount, int newCount, EnigmaController ctrl)
        {
            if (oldCount <= 0 || newCount <= 0 || oldCount == newCount) return;

            var old    = folder.entries;
            // Round up so a partial final page isn't silently dropped.
            int pages  = Mathf.Max(1, Mathf.CeilToInt((float)old.Length / oldCount));
            int newLen = pages * newCount;

            var remapped = new EnigmaEntryData[newLen];
            for (int i = 0; i < newLen; i++)
                remapped[i] = new EnigmaEntryData { isEmpty = true };

            int slotsToCopy = Mathf.Min(oldCount, newCount);
            for (int page = 0; page < pages; page++)
            {
                for (int slot = 0; slot < slotsToCopy; slot++)
                {
                    int oldIdx = page * oldCount + slot;
                    int newIdx = page * newCount + slot;
                    if (oldIdx < old.Length)
                        remapped[newIdx] = old[oldIdx];
                }
            }

            folder.entries = remapped;
            MarkDataDirtyStatic(ctrl);
        }

        /// <summary>
        /// Draws a thin horizontal separator line using the current label color.
        /// </summary>
        private static void DrawSeparator()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        }
    }

}
#endif

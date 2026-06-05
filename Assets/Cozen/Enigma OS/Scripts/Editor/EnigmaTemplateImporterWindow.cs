#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Cozen.EnigmaOS.Editor
{
    // ── Reference field kinds ─────────────────────────────────────────────────
    // UdonSharp constraint: enums must be at namespace level (outside any class).

    /// <summary>
    /// Identifies which scene-reference field on an EnigmaActionData
    /// (or related data) a TemplateRefSlot targets.
    /// </summary>
    public enum TemplateRefField
    {
        TargetObject,           // action.targetObject          (GameObject)
        TargetRenderer,         // action.targetRenderer        (Renderer)
        TargetMaterial,         // action.targetMaterial        (Material)
        TargetComponent,        // action.targetComponent       (Behaviour)
        TeleportDestination,    // action.teleportDestination   (GameObject)
        ColorTargetRenderer,    // action.colorTargetRenderer   (Renderer)
        TargetUdon,             // action.targetUdon            (UdonSharpBehaviour)
        TargetTexture,          // action.targetTexture         (Texture)
        VariantTexture,         // action.variantItems[vi].textureValue (Texture)
        FaderRenderer,          // entry.faderLink.targetRenderer (Renderer)
        DisplayValueSource,     // action.targetRenderer OR action.targetUdon (Component)
        CustomColorRenderer,    // entry.condColorRenderer (Renderer)
    }

    /// <summary>
    /// One pre-import reference assignment slot shown in
    /// EnigmaTemplateImporterWindow.
    /// </summary>
    public class TemplateRefSlot
    {
        public int            entryIdx;          // index into template.entries[]
        public int            actionIdx;         // -1 => fader-link ref
        public int            variantIdx;        // -1 => not a variant texture
        public TemplateRefField field;
        public string         label;             // "Entry 1: My Button — Renderer"
        public System.Type    objectType;        // type for ObjectField
        public bool           allowSceneObjects; // true for scene refs, false for asset refs
        public Object         value;             // assigned by the user (null = skip)
    }

    // ── Main window ───────────────────────────────────────────────────────────

    /// <summary>
    /// Modal-style EditorWindow shown before a template is applied.
    ///
    /// Layout (top -> bottom):
    ///   1. Template name header.
    ///   2. Page navigation (arrow buttons) + page counter.
    ///   3. Read-only button grid showing entry labels from the template.
    ///   4. Reference Assignments foldout -- ObjectField for every scene ref
    ///      that will be null after import.
    ///   5. Action buttons: "Overwrite ...", "Append to ...", "Cancel".
    ///
    /// Opened via EnigmaTemplateImporterWindow.Show.
    /// </summary>
    public class EnigmaTemplateImporterWindow : EditorWindow
    {
        // ── Injected data ─────────────────────────────────────────────────────

        private EnigmaTemplateData _tpl;
        private string             _folderName;
        private int                _cols;
        private int                _rows;
        private int                _slotsPerPage;

        private System.Action<List<TemplateRefSlot>> _onOverwrite;
        private System.Action<List<TemplateRefSlot>> _onAppend;
        private System.Action<List<TemplateRefSlot>> _onNewFolder;

        // ── Preview state ─────────────────────────────────────────────────────

        private int      _page;
        private GUIStyle _btnStyle;

        // ── Reference foldout state ───────────────────────────────────────────

        private List<TemplateRefSlot> _refSlots = new List<TemplateRefSlot>();
        private bool    _refsFoldout     = false;
        private bool    _prevFoldout     = false;
        private Vector2 _refsScroll;
        private float   _baseHeight;
        private Renderer _fillRenderer;   // shared renderer for bulk auto-fill
        private bool    _needsInitialResize;

        // ── Screen Shader template state ────────────────────────────────────
        private bool _hasScreenShaderActions = false;
        private int  _shaderTemplateNumber   = 1;

        // ── Constants ────────────────────────────────────────────────────────

        private const float kCellH      = 46f;
        private const float kGap        = 3f;
        private const float kMinWidth   = 540f;
        private const float kMinHeight  = 280f;
        private const float kActionBarH = 38f;
        private const float kPadding    = 8f;
        private const float kRefsMaxH   = 220f;
        // Each ref slot uses two rows: label (18px + 2px spacing) + field (18px + 2px spacing) = 40px
        private const float kRefRowH    = 40f;

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>
        /// Opens (or re-uses) the template apply window.
        /// </summary>
        public static void Show(
            EnigmaTemplateData                   tpl,
            string                               folderName,
            int                                  cols,
            int                                  rows,
            int                                  slotsPerPage,
            System.Action<List<TemplateRefSlot>> onOverwrite,
            System.Action<List<TemplateRefSlot>> onAppend,
            System.Action<List<TemplateRefSlot>> onNewFolder = null)
        {
            var win = GetWindow<EnigmaTemplateImporterWindow>(
                utility: true, title: "Template Importer", focus: true);

            win._tpl          = tpl;
            win._folderName   = folderName;
            win._cols         = Mathf.Max(1, cols);
            win._rows         = Mathf.Max(1, rows);
            win._slotsPerPage = Mathf.Max(1, slotsPerPage);
            win._onOverwrite  = onOverwrite;
            win._onAppend     = onAppend;
            win._onNewFolder  = onNewFolder;
            win._page         = 0;
            win._btnStyle     = null;
            win._refsFoldout  = false;
            win._prevFoldout  = false;
            win._fillRenderer = null;
            win._hasScreenShaderActions = false;
            win._shaderTemplateNumber  = 1;

            // Detect if template has Screen Shader actions.
            if (tpl.entries != null)
            {
                foreach (var te in tpl.entries)
                {
                    if (te == null || te.actions == null) continue;
                    foreach (var act in te.actions)
                    {
                        if (act != null && act.actionType == 26)
                        {
                            win._hasScreenShaderActions = true;
                            win._shaderTemplateNumber  = act.shaderTemplateIndex > 0
                                ? act.shaderTemplateIndex : 1;
                            break;
                        }
                    }
                    if (win._hasScreenShaderActions) break;
                }
            }

            win.BuildRefSlots();

            float gridW = win._cols * (60f + kGap) + kPadding * 2f;
            float gridH = win._rows * (kCellH + kGap) + kPadding * 2f;
            float w     = Mathf.Max(kMinWidth, gridW);
            // Base height: header + page nav + grid + action bar + padding
            float h     = 22f + 28f + gridH + kActionBarH + kPadding * 4f;

            // Add height for always-visible rows above the foldout.
            bool hasRendererSlots = false;
            if (win._refSlots != null)
                foreach (var s in win._refSlots)
                    if (s.objectType == typeof(Renderer) || s.field == TemplateRefField.DisplayValueSource)
                    { hasRendererSlots = true; break; }
            if (win._refSlots != null && win._refSlots.Count > 0)
                h += 20f; // status summary line
            if (hasRendererSlots)
                h += 24f; // auto-fill renderer row
            if (win._hasScreenShaderActions)
                h += 24f; // shader template row
            h += 18f; // foldout header itself

            h           = Mathf.Max(kMinHeight, h);
            win._baseHeight = h;
            win._needsInitialResize = true;

            win.minSize = new Vector2(kMinWidth, h);
            float maxRefH = win._refSlots != null && win._refSlots.Count > 0
                ? win._refSlots.Count * kRefRowH + 30f
                : 0f;
            win.maxSize = new Vector2(Mathf.Max(w * 1.5f, 600f), Mathf.Max(h + maxRefH + 60f, 900f));
            win.position = new Rect(
                (Screen.currentResolution.width  - w) * 0.5f,
                (Screen.currentResolution.height - h) * 0.5f,
                w, h);
            win.ShowUtility();
        }

        // ── IMGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_tpl == null) { Close(); return; }
            InitStyle();

            EditorGUILayout.Space(kPadding);

            // ── Header ────────────────────────────────────────────────────────
            string displayName = !string.IsNullOrEmpty(_tpl.templateName)
                ? _tpl.templateName : "Template";
            EditorGUILayout.LabelField($"Preview: {displayName}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // ── Page navigation ───────────────────────────────────────────────
            int totalEntries = _tpl.entries != null ? _tpl.entries.Count : 0;
            int totalPages   = Mathf.Max(1, Mathf.CeilToInt((float)totalEntries / _slotsPerPage));
            _page = Mathf.Clamp(_page, 0, totalPages - 1);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Page {_page + 1} / {totalPages}", GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(_page <= 0))
                if (GUILayout.Button("\u25C4", GUILayout.Width(30))) { _page--; Repaint(); }
            using (new EditorGUI.DisabledScope(_page >= totalPages - 1))
                if (GUILayout.Button("\u25BA", GUILayout.Width(30))) { _page++; Repaint(); }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            // ── Read-only grid ────────────────────────────────────────────────
            DrawGrid(totalEntries);

            EditorGUILayout.Space(6f);

            // ── Reference assignments foldout ─────────────────────────────────
            DrawRefsFoldout();

            // Resize window when foldout is toggled.
            if (_refsFoldout != _prevFoldout)
            {
                _prevFoldout = _refsFoldout;
                float refH = _refsFoldout && _refSlots != null && _refSlots.Count > 0
                    ? _refSlots.Count * kRefRowH + 30f
                    : 0f;
                Rect r = position;
                r.height = Mathf.Max(kMinHeight, _baseHeight + refH);
                position = r;
                Repaint();
            }

            // Auto-resize on first frame to match actual layout height.
            if (_needsInitialResize && Event.current.type == EventType.Repaint)
            {
                _needsInitialResize = false;
                float actualH = GUILayoutUtility.GetLastRect().yMax + kActionBarH + kPadding * 2f;
                actualH = Mathf.Max(kMinHeight, actualH);
                _baseHeight = actualH;
                minSize = new Vector2(kMinWidth, actualH);
                Rect r = position;
                r.height = actualH;
                position = r;
                Repaint();
            }

            GUILayout.FlexibleSpace();

            // ── Action buttons ────────────────────────────────────────────────
            DrawActionBar();

            EditorGUILayout.Space(kPadding);
        }

        // ── Grid drawing ──────────────────────────────────────────────────────

        private void DrawGrid(int totalEntries)
        {
            float availW = position.width - kPadding * 2f;
            float cellW  = Mathf.Floor((availW - kGap * (_cols - 1)) / _cols);
            float gridH  = _rows * (kCellH + kGap) - kGap;

            Rect gridRect = EditorGUILayout.GetControlRect(false, gridH);
            if (Event.current.type != EventType.Repaint) return;

            int pageOffset = _page * _slotsPerPage;

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int slot     = row * _cols + col;
                    int entryIdx = pageOffset + slot;
                    bool hasEntry = entryIdx < totalEntries
                                 && _tpl.entries[entryIdx] != null
                                 && !_tpl.entries[entryIdx].isEmpty
                                 && !string.IsNullOrEmpty(_tpl.entries[entryIdx].label);

                    Rect cell = new Rect(
                        gridRect.x + col * (cellW + kGap),
                        gridRect.y + row * (kCellH + kGap),
                        cellW, kCellH);

                    Color bg = hasEntry
                        ? new Color(0.28f, 0.28f, 0.28f)
                        : new Color(0.18f, 0.18f, 0.18f);
                    EditorGUI.DrawRect(cell, bg);

                    string lbl = hasEntry ? _tpl.entries[entryIdx].label : "";
                    Color savedContent = GUI.contentColor;
                    GUI.contentColor   = ContrastTextColor(bg);
                    GUI.Label(cell, lbl, _btnStyle);
                    GUI.contentColor   = savedContent;
                }
            }
        }

        // ── Reference foldout drawing ─────────────────────────────────────────

        private void DrawRefsFoldout()
        {
            if (_refSlots == null || _refSlots.Count == 0) return;

            // Check whether any slot accepts a Renderer so we know whether to show
            // the auto-fill row (avoids showing it for templates with only GameObjects).
            bool hasRendererSlots = false;
            foreach (var s in _refSlots)
            {
                if (s.objectType == typeof(Renderer) || s.field == TemplateRefField.DisplayValueSource
                    || s.field == TemplateRefField.CustomColorRenderer)
                { hasRendererSlots = true; break; }
            }

            // ── Reference status summary ────────────────────────────────────────
            DrawRefStatusSummary();
            EditorGUILayout.Space(2f);

            // ── Auto-fill row ──────────────────────────────────────────────────
            // Picking a renderer here fills every unfilled Renderer-typed slot
            // immediately — there's no separate "Fill" button because it was
            // the only action this field enabled and required an extra click.
            // We only overwrite slots the user hasn't already assigned, so
            // setting this field after a partial manual assignment doesn't
            // clobber their picks.
            if (hasRendererSlots)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Auto-fill Renderer:", GUILayout.Width(120));
                EditorGUI.BeginChangeCheck();
                _fillRenderer = (Renderer)EditorGUILayout.ObjectField(
                    GUIContent.none, _fillRenderer, typeof(Renderer), true);
                if (EditorGUI.EndChangeCheck() && _fillRenderer != null)
                {
                    foreach (var slot in _refSlots)
                    {
                        if (slot.value != null) continue;
                        if (slot.objectType == typeof(Renderer))
                            slot.value = _fillRenderer;
                        else if (slot.field == TemplateRefField.DisplayValueSource)
                            slot.value = _fillRenderer;
                        else if (slot.field == TemplateRefField.CustomColorRenderer)
                            slot.value = _fillRenderer;
                    }
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);
            }

            // ── Screen Shader template row ──────────────────────────────────────
            if (_hasScreenShaderActions)
            {
                var templates = EnigmaShaderTemplate.FindAllInScene();
                var labels    = EnigmaShaderTemplate.GetTemplateLabels(templates);
                int popupIdx  = EnigmaShaderTemplate.GetPopupIndex(templates, _shaderTemplateNumber);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Shader Template:", GUILayout.Width(120));
                int newIdx = EditorGUILayout.Popup(popupIdx, labels);
                if (newIdx != popupIdx && templates.Length > 0)
                {
                    _shaderTemplateNumber = templates[newIdx].templateNumber;
                    // Update all Screen Shader actions in the template data.
                    if (_tpl?.entries != null)
                        foreach (var te in _tpl.entries)
                            if (te?.actions != null)
                                foreach (var act in te.actions)
                                    if (act != null && act.actionType == 26)
                                        act.shaderTemplateIndex = _shaderTemplateNumber;
                }

                if (GUILayout.Button("New", GUILayout.Width(42)))
                {
                    var created = EnigmaShaderTemplate.CreateNewTemplate();
                    if (created != null)
                    {
                        _shaderTemplateNumber = created.templateNumber;
                        if (_tpl?.entries != null)
                            foreach (var te in _tpl.entries)
                                if (te?.actions != null)
                                    foreach (var act in te.actions)
                                        if (act != null && act.actionType == 26)
                                            act.shaderTemplateIndex = _shaderTemplateNumber;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);
            }

            string foldoutLabel = _refSlots.Count == 1
                ? "Reference Assignments  (1 ref)"
                : $"Reference Assignments  ({_refSlots.Count} refs)";

            _refsFoldout = EditorGUILayout.Foldout(
                _refsFoldout, foldoutLabel, toggleOnLabelClick: true);

            if (!_refsFoldout) return;

            EditorGUI.indentLevel++;

            // Use remaining window space for the scroll view instead of a fixed cap.
            _refsScroll = EditorGUILayout.BeginScrollView(_refsScroll, GUILayout.ExpandHeight(true));

            // Split into unfilled (left) and filled (right) columns.
            var unfilled = new System.Collections.Generic.List<TemplateRefSlot>();
            var filled   = new System.Collections.Generic.List<TemplateRefSlot>();
            foreach (var s in _refSlots)
            {
                if (s.value == null) unfilled.Add(s);
                else filled.Add(s);
            }

            // Split the available width 50/50 so long slot labels (e.g.
            // "Entry 1: Outline — Action 1: Renderer") aren't truncated when
            // the other column is mostly empty ("None filled"). Without an
            // explicit width, IMGUI's horizontal layout lets the content-less
            // right column claim more than its share because the left column
            // doesn't advertise an intrinsic width wider than MinWidth.
            //
            // Budget: window width minus a vertical scrollbar (~16 px) minus
            // left/right padding + indent (~24 px) minus a small gap between
            // the two columns (8 px). Floor at 100 px per column so the
            // fields never collapse if the user shrinks the window below the
            // minimum.
            float totalWidth = position.width - 16f - 24f - 8f;
            float colWidth   = Mathf.Max(100f, totalWidth * 0.5f);
            var colOpts = new[] { GUILayout.Width(colWidth) };

            EditorGUILayout.BeginHorizontal();

            // Left column — Unfilled
            EditorGUILayout.BeginVertical(colOpts);
            if (unfilled.Count > 0)
            {
                EditorGUILayout.LabelField($"Unfilled ({unfilled.Count})", EditorStyles.boldLabel);
                foreach (var slot in unfilled)
                {
                    EditorGUILayout.LabelField(slot.label, EditorStyles.miniLabel);
                    slot.value = EditorGUILayout.ObjectField(
                        GUIContent.none, slot.value, slot.objectType, slot.allowSceneObjects);
                }
            }
            else
            {
                EditorGUILayout.LabelField("All assigned", EditorStyles.boldLabel);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);

            // Right column — Filled
            EditorGUILayout.BeginVertical(colOpts);
            if (filled.Count > 0)
            {
                EditorGUILayout.LabelField($"Filled ({filled.Count})", EditorStyles.boldLabel);
                foreach (var slot in filled)
                {
                    EditorGUILayout.LabelField(slot.label, EditorStyles.miniLabel);
                    slot.value = EditorGUILayout.ObjectField(
                        GUIContent.none, slot.value, slot.objectType, slot.allowSceneObjects);
                }
            }
            else
            {
                EditorGUILayout.LabelField("None filled", EditorStyles.boldLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            EditorGUI.indentLevel--;
        }

        // ── Action bar ────────────────────────────────────────────────────────

        private void DrawActionBar()
        {
            Rect sep = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(sep, new Color(0.3f, 0.3f, 0.3f));
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();

            string shortName = !string.IsNullOrEmpty(_folderName) ? _folderName : "Folder";

            if (GUILayout.Button($"Overwrite {shortName}", GUILayout.Height(28)))
            {
                if (ConfirmUnfilledRefs())
                {
                    var cb    = _onOverwrite;
                    var slots = _refSlots;
                    Close();
                    cb?.Invoke(slots);
                }
            }

            if (GUILayout.Button($"Append to {shortName}", GUILayout.Height(28)))
            {
                if (ConfirmUnfilledRefs())
                {
                    var cb    = _onAppend;
                    var slots = _refSlots;
                    Close();
                    cb?.Invoke(slots);
                }
            }

            if (_onNewFolder != null && GUILayout.Button("New Folder", GUILayout.Height(28)))
            {
                if (ConfirmUnfilledRefs())
                {
                    var cb    = _onNewFolder;
                    var slots = _refSlots;
                    Close();
                    cb?.Invoke(slots);
                }
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(28)))
                Close();

            EditorGUILayout.EndHorizontal();
        }

        // ── Reference status helpers ─────────────────────────────────────────

        /// <summary>
        /// Returns a user-friendly category name for a reference slot.
        /// </summary>
        private static string GetRefCategory(TemplateRefSlot slot)
        {
            switch (slot.field)
            {
                case TemplateRefField.TargetRenderer:
                case TemplateRefField.ColorTargetRenderer:
                case TemplateRefField.FaderRenderer:
                case TemplateRefField.DisplayValueSource:
                case TemplateRefField.CustomColorRenderer:
                    return "Renderers";
                case TemplateRefField.TargetObject:
                case TemplateRefField.TeleportDestination:
                    return "Objects";
                case TemplateRefField.TargetMaterial:
                    return "Materials";
                case TemplateRefField.TargetTexture:
                case TemplateRefField.VariantTexture:
                    return "Textures";
                case TemplateRefField.TargetComponent:
                    return "Components";
                case TemplateRefField.TargetUdon:
                    return "Udon Behaviours";
                default:
                    return "References";
            }
        }

        /// <summary>
        /// Builds per-category (filled/total) counts from the current ref slots.
        /// </summary>
        private Dictionary<string, (int filled, int total)> GetRefCategoryCounts()
        {
            var counts = new Dictionary<string, (int filled, int total)>();
            foreach (var slot in _refSlots)
            {
                string cat = GetRefCategory(slot);
                if (!counts.TryGetValue(cat, out var c))
                    c = (0, 0);
                c.total++;
                if (slot.value != null)
                    c.filled++;
                counts[cat] = c;
            }
            return counts;
        }

        private void DrawRefStatusSummary()
        {
            var counts = GetRefCategoryCounts();
            if (counts.Count == 0) return;

            var parts = new List<string>();
            // Show categories in a stable order.
            string[] order = { "Renderers", "Objects", "Materials", "Textures", "Components", "Udon Behaviours" };
            foreach (string cat in order)
            {
                if (!counts.TryGetValue(cat, out var c)) continue;
                bool done = c.filled == c.total;
                string color = done ? "#88cc88" : "#cc6666";
                string icon  = done ? "\u2714" : "\u2716";
                parts.Add($"<color={color}>{c.filled}/{c.total} {cat} {icon}</color>");
            }
            // Catch any categories not in the predefined order.
            foreach (var kvp in counts)
            {
                bool found = false;
                foreach (string o in order) { if (o == kvp.Key) { found = true; break; } }
                if (!found)
                {
                    bool done = kvp.Value.filled == kvp.Value.total;
                    string color = done ? "#88cc88" : "#cc6666";
                    string icon  = done ? "\u2714" : "\u2716";
                    parts.Add($"<color={color}>{kvp.Value.filled}/{kvp.Value.total} {kvp.Key} {icon}</color>");
                }
            }

            var style = new GUIStyle(EditorStyles.label)
            {
                richText  = true,
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUILayout.LabelField(string.Join("   |   ", parts), style);
        }

        /// <summary>
        /// If there are unfilled reference slots, shows a confirmation dialog.
        /// Returns true if the user wants to proceed, false to cancel.
        /// </summary>
        private bool ConfirmUnfilledRefs()
        {
            var counts = GetRefCategoryCounts();
            var missing = new List<string>();
            foreach (var kvp in counts)
            {
                int unfilled = kvp.Value.total - kvp.Value.filled;
                if (unfilled > 0)
                    missing.Add($"{unfilled} {kvp.Key.ToLower()}");
            }
            if (missing.Count == 0) return true;

            string list = string.Join(", ", missing);
            return EditorUtility.DisplayDialog(
                "Unassigned References",
                $"You still have {list} unassigned. You will need to manually assign these in their respective actions if you proceed.",
                "Continue",
                "Cancel");
        }

        // ── Reference slot building ───────────────────────────────────────────

        private void BuildRefSlots()
        {
            _refSlots = new List<TemplateRefSlot>();
            if (_tpl?.entries == null) return;

            for (int ei = 0; ei < _tpl.entries.Count; ei++)
            {
                var entry = _tpl.entries[ei];
                if (entry == null || entry.isEmpty) continue;

                string name   = !string.IsNullOrEmpty(entry.label) ? entry.label : $"Entry {ei + 1}";
                string prefix = $"Entry {ei + 1}: {name}";

                // ── Fader links ────────────────────────────────────────────
                // A fader link is "linked" when its faderLinkId matches some
                // action's faderLinkId on the same entry — the pairing the
                // drawer's "Fader Link" option creates. For linked fader links
                // we DON'T show a separate renderer slot: the user already
                // assigns the action's Renderer via the action's own slot, and
                // the apply pass propagates that renderer to every fader link
                // sharing the id. This halves the number of scene references
                // the user has to wire up for typical effect-button templates.
                //
                // Legacy single-field fallback: if the entry has only the
                // non-array faderLink field populated (older templates), fall
                // back to the original single-slot behaviour.
                if (entry.assignFader)
                {
                    bool handledArray = false;
                    if (entry.faderLinks != null && entry.faderLinks.Count > 0)
                    {
                        handledArray = true;
                        for (int fi = 0; fi < entry.faderLinks.Count; fi++)
                        {
                            var fl = entry.faderLinks[fi];
                            if (fl == null) continue;
                            // Skip linked fader links — renderer will be inherited
                            // from the paired action in the apply pass.
                            if (fl.faderLinkId != 0 && EntryHasActionWithId(entry, fl.faderLinkId))
                                continue;
                            string flName = !string.IsNullOrEmpty(fl.faderName)
                                ? fl.faderName : $"Fader {fi + 1}";
                            Add(ei, -1, fi, TemplateRefField.FaderRenderer,
                                $"{prefix} \u2014 Fader Renderer ({flName})",
                                typeof(Renderer), true);
                        }
                    }
                    if (!handledArray && entry.faderLink != null)
                    {
                        // Legacy path. Skip if linked to an action via id.
                        if (!(entry.faderLink.faderLinkId != 0
                              && EntryHasActionWithId(entry, entry.faderLink.faderLinkId)))
                        {
                            Add(ei, -1, -1, TemplateRefField.FaderRenderer,
                                $"{prefix} \u2014 Fader Renderer", typeof(Renderer), true);
                        }
                    }
                }

                // ── Custom color source ────────────────────────────────────
                if (entry.useCustomColor && entry.useConditionalColor
                    && entry.condColorSourceType == 0 && !entry.condColorTargetsSkybox)
                {
                    Add(ei, -1, -1, TemplateRefField.CustomColorRenderer,
                        $"{prefix} \u2014 Color Source Renderer", typeof(Renderer), true);
                }

                // ── Actions ────────────────────────────────────────────────
                if (entry.actions == null) continue;

                for (int ai = 0; ai < entry.actions.Count; ai++)
                {
                    var act = entry.actions[ai];
                    if (act == null) continue;

                    string ap = $"{prefix} \u2014 Action {ai + 1}";

                    switch (act.actionType)
                    {
                        case 0: // Toggle Object
                            Add(ei, ai, -1, TemplateRefField.TargetObject,
                                $"{ap}: Target Object", typeof(GameObject), true);
                            break;

                        case 1: // Set Material
                            Add(ei, ai, -1, TemplateRefField.TargetRenderer,
                                $"{ap}: Renderer", typeof(Renderer), true);
                            Add(ei, ai, -1, TemplateRefField.TargetMaterial,
                                $"{ap}: Material", typeof(Material), false,
                                LoadAsset<Material>(act.targetMaterialPath));
                            break;

                        case 2: // Shader Property
                            Add(ei, ai, -1, TemplateRefField.TargetRenderer,
                                $"{ap}: Renderer", typeof(Renderer), true);
                            if (act.propertyType == 3)
                                Add(ei, ai, -1, TemplateRefField.TargetTexture,
                                    $"{ap}: Texture", typeof(Texture), false,
                                    LoadAsset<Texture>(act.targetTexturePath));
                            break;

                        case 4:  // Apply Skybox
                        case 22: // Toggle Skybox
                            Add(ei, ai, -1, TemplateRefField.TargetMaterial,
                                $"{ap}: Skybox Material", typeof(Material), false,
                                LoadAsset<Material>(act.targetMaterialPath));
                            break;

                        case 5: // Trigger Udon Event
                        case 6: // Set Udon Variable
                            Add(ei, ai, -1, TemplateRefField.TargetUdon,
                                $"{ap}: UdonBehaviour",
                                typeof(UdonSharp.UdonSharpBehaviour), true);
                            break;

                        case 7: // Color Cycle (deprecated)
                            Add(ei, ai, -1, TemplateRefField.ColorTargetRenderer,
                                $"{ap}: Target Renderer", typeof(Renderer), true);
                            break;

                        case 9: // Display Value -- accepts Renderer or UdonBehaviour
                            Add(ei, ai, -1, TemplateRefField.DisplayValueSource,
                                $"{ap}: Source (Renderer or UdonBehaviour)",
                                typeof(Component), true);
                            break;

                        case 10: // Color Selector -- only role 1 (Set Color) needs a ref
                            if (act.colorSelectorRole == 1)
                                Add(ei, ai, -1, TemplateRefField.ColorTargetRenderer,
                                    $"{ap}: Target Renderer", typeof(Renderer), true);
                            break;

                        case 11: // Toggle Component
                            Add(ei, ai, -1, TemplateRefField.TargetObject,
                                $"{ap}: Target Object", typeof(GameObject), true);
                            Add(ei, ai, -1, TemplateRefField.TargetComponent,
                                $"{ap}: Target Component", typeof(Behaviour), true);
                            break;

                        case 12: // Transform
                        case 23: // Toggle Transform
                            Add(ei, ai, -1, TemplateRefField.TargetObject,
                                $"{ap}: Target Object", typeof(GameObject), true);
                            break;

                        case 13: // Teleport
                            if (act.target == 0) // Teleport Object
                            {
                                Add(ei, ai, -1, TemplateRefField.TargetObject,
                                    $"{ap}: Object to Teleport", typeof(GameObject), true);
                                if (act.propertyType == 4) // To Transform
                                    Add(ei, ai, -1, TemplateRefField.TeleportDestination,
                                        $"{ap}: Destination Transform", typeof(GameObject), true);
                            }
                            else if (act.propertyType == 2) // Teleport Player to Transform
                            {
                                Add(ei, ai, -1, TemplateRefField.TargetObject,
                                    $"{ap}: Target Transform", typeof(GameObject), true);
                            }
                            break;

                        case 15: // Command: Set Object State
                            Add(ei, ai, -1, TemplateRefField.TargetObject,
                                $"{ap}: Target Object", typeof(GameObject), true);
                            break;

                        case 16: // Command: Set Component State
                            Add(ei, ai, -1, TemplateRefField.TargetObject,
                                $"{ap}: Target Object", typeof(GameObject), true);
                            Add(ei, ai, -1, TemplateRefField.TargetComponent,
                                $"{ap}: Target Component", typeof(Behaviour), true);
                            break;

                        case 19: // Variant Selector -- only role 1 owns refs
                            if (act.variantSelectorRole == 1)
                            {
                                Add(ei, ai, -1, TemplateRefField.TargetRenderer,
                                    $"{ap}: Target Renderer", typeof(Renderer), true);
                                if (act.propertyType == 3 && act.variantItems != null)
                                {
                                    for (int vi = 0; vi < act.variantItems.Count; vi++)
                                    {
                                        string vn = act.variantItems[vi]?.variantName
                                                    ?? $"Variant {vi + 1}";
                                        string vTexPath = act.variantItems[vi]?.textureValuePath ?? "";
                                        Add(ei, ai, vi, TemplateRefField.VariantTexture,
                                            $"{ap}: Variant \"{vn}\" Texture",
                                            typeof(Texture), false,
                                            LoadAsset<Texture>(vTexPath));
                                    }
                                }
                            }
                            break;

                        case 26: // Screen Shader — material is an asset ref (auto-resolved from path)
                            Add(ei, ai, -1, TemplateRefField.TargetMaterial,
                                $"{ap}: Shader Material", typeof(Material), false,
                                LoadAsset<Material>(act.targetMaterialPath));
                            break;

                        case 27: // Shader Keyword
                            Add(ei, ai, -1, TemplateRefField.TargetRenderer,
                                $"{ap}: Renderer", typeof(Renderer), true);
                            break;
                    }
                }
            }
        }

        private void Add(int ei, int ai, int vi, TemplateRefField field,
                         string label, System.Type type, bool allowScene,
                         Object initialValue = null)
        {
            _refSlots.Add(new TemplateRefSlot
            {
                entryIdx          = ei,
                actionIdx         = ai,
                variantIdx        = vi,
                field             = field,
                label             = label,
                objectType        = type,
                allowSceneObjects = allowScene,
                value             = initialValue,
            });
        }

        /// <summary>
        /// Attempts to load a project asset at <paramref name="assetPath"/>.
        /// Returns null (silently) when the path is empty or the asset is not present.
        /// </summary>
        private static T LoadAsset<T>(string assetPath) where T : Object
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        // ── Static helper: apply assigned refs to a converted entry array ─────

        /// <summary>
        /// Writes all non-null assigned values from slots into the converted
        /// entries array.  entryOffset is added to each slot's entryIdx before
        /// indexing into entries; use 0 for Overwrite and paddedLen for Append.
        /// </summary>
        public static void ApplyRefSlotsToEntries(
            EnigmaEntryData[]     entries,
            List<TemplateRefSlot> slots,
            int                   entryOffset = 0)
        {
            if (entries == null || slots == null) return;

            foreach (var slot in slots)
            {
                if (slot.value == null) continue;

                int idx = slot.entryIdx + entryOffset;
                if (idx < 0 || idx >= entries.Length) continue;

                var entry = entries[idx];
                if (entry == null) continue;

                // Fader link ─────────────────────────────────────────────────
                if (slot.actionIdx < 0)
                {
                    if (slot.field == TemplateRefField.FaderRenderer)
                    {
                        // slot.variantIdx carries the faderLinks array index when
                        // the slot was built for the per-link path. When it's -1
                        // this is the legacy single-field path.
                        if (slot.variantIdx >= 0
                            && entry.faderLinks != null
                            && slot.variantIdx < entry.faderLinks.Length
                            && entry.faderLinks[slot.variantIdx] != null)
                        {
                            entry.faderLinks[slot.variantIdx].targetRenderer = slot.value as Renderer;
                        }
                        else if (entry.faderLink != null)
                        {
                            entry.faderLink.targetRenderer = slot.value as Renderer;
                        }
                    }
                    else if (slot.field == TemplateRefField.CustomColorRenderer)
                    {
                        entry.condColorRenderer = slot.value as Renderer;
                    }
                    continue;
                }

                // Action refs ─────────────────────────────────────────────────
                if (entry.actions == null || slot.actionIdx >= entry.actions.Length) continue;
                var action = entry.actions[slot.actionIdx];
                if (action == null) continue;

                switch (slot.field)
                {
                    case TemplateRefField.TargetObject:
                        action.targetObject = slot.value as GameObject;
                        break;
                    case TemplateRefField.TargetRenderer:
                        action.targetRenderer = slot.value as Renderer;
                        break;
                    case TemplateRefField.TargetMaterial:
                        action.targetMaterial = slot.value as Material;
                        break;
                    case TemplateRefField.TargetComponent:
                        action.targetComponent = slot.value as Behaviour;
                        break;
                    case TemplateRefField.TeleportDestination:
                        action.teleportDestination = slot.value as GameObject;
                        break;
                    case TemplateRefField.ColorTargetRenderer:
                        action.colorTargetRenderer = slot.value as Renderer;
                        break;
                    case TemplateRefField.TargetUdon:
                        action.targetUdon = slot.value as UdonSharp.UdonSharpBehaviour;
                        break;
                    case TemplateRefField.TargetTexture:
                        action.targetTexture = slot.value as Texture;
                        break;
                    case TemplateRefField.VariantTexture:
                        if (slot.variantIdx >= 0
                            && action.variantItems != null
                            && slot.variantIdx < action.variantItems.Length)
                        {
                            action.variantItems[slot.variantIdx].textureValue =
                                slot.value as Texture;
                        }
                        break;
                    case TemplateRefField.DisplayValueSource:
                        if (slot.value is Renderer sr)
                        { action.targetRenderer = sr; action.targetUdon = null; }
                        else if (slot.value is UdonSharp.UdonSharpBehaviour su)
                        { action.targetUdon = su; action.targetRenderer = null; }
                        break;
                }
            }

            // ── Propagate action.targetRenderer to linked fader links ─────────
            // Any fader link whose faderLinkId matches an action's id inherits
            // that action's targetRenderer. This is the reason the BuildRefSlots
            // pass above can safely skip Renderer slots for linked fader links
            // — the user only has to assign the action's Renderer once and every
            // linked fader link on the same button picks it up here.
            foreach (var entry in entries)
            {
                if (entry == null || entry.actions == null || entry.faderLinks == null) continue;
                foreach (var action in entry.actions)
                {
                    if (action == null || action.faderLinkId == 0 || action.targetRenderer == null) continue;
                    foreach (var link in entry.faderLinks)
                    {
                        if (link == null || link.faderLinkId != action.faderLinkId) continue;
                        if (link.targetRenderer == null)
                        {
                            link.targetRenderer = action.targetRenderer;
                            // Also inherit material index so the link points at
                            // the same material slot as its paired action.
                            link.materialIndex = action.materialIndex;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true when the entry has any action whose faderLinkId equals
        /// <paramref name="linkId"/>. Used by <c>BuildRefSlots</c> to decide
        /// whether a fader link's Renderer slot should be shown or suppressed
        /// (linked fader links inherit the renderer from their paired action).
        /// </summary>
        private static bool EntryHasActionWithId(EnigmaTemplateEntryData entry, int linkId)
        {
            if (entry == null || entry.actions == null || linkId == 0) return false;
            foreach (var act in entry.actions)
                if (act != null && act.faderLinkId == linkId) return true;
            return false;
        }

        // ── Style helpers ─────────────────────────────────────────────────────

        private void InitStyle()
        {
            if (_btnStyle != null) return;
            _btnStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                clipping  = TextClipping.Clip,
                wordWrap  = true,
                fontStyle = FontStyle.Bold,
            };
            _btnStyle.normal.textColor = Color.white;
        }

        private static Color ContrastTextColor(Color bg)
        {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.5f ? Color.black : Color.white;
        }
    }
}
#endif

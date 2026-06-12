#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Standalone action-list drawer shared between EnigmaControllerEditor and
    /// EnigmaButtonEditor. Draws the full Category/Target/Operation action editor
    /// for any EnigmaActionData[] — whether it belongs to a controller entry or a
    /// standalone EnigmaButton.
    ///
    /// Caller supplies a repaint callback and all SetDirty calls are made against
    /// the caller-supplied dirtyObj, so the drawer has no dependency on any
    /// specific Editor subclass.
    /// </summary>
    public class EnigmaActionListDrawer
    {
        // ── Async pending dictionaries ──────────────────────────────────────────
        // Keys are stable field identifiers; values are deposited by async search
        // popup callbacks and consumed on the next repaint.

        private Dictionary<string, string>    _pendingPropertyValues;
        private Dictionary<string, int>       _pendingPropertyTypes;
        private int _drawIndex; // current action index during drawing, used for field key disambiguation
        private Dictionary<string, Behaviour> _pendingComponentValues;
        private Dictionary<string, int[]>     _pendingActionValues;

        private readonly System.Action _repaint;

        private int          _dragSourceIndex = -1;
        private int          _dragTargetLine  = -1;
        private readonly List<float> _rowTopYs = new List<float>();
        private readonly HashSet<int> _collapsedActions = new HashSet<int>();

        public EnigmaActionListDrawer(System.Action repaint)
        {
            _repaint                = repaint;
            _pendingPropertyValues  = new Dictionary<string, string>();
            _pendingPropertyTypes   = new Dictionary<string, int>();
            _pendingComponentValues = new Dictionary<string, Behaviour>();
            _pendingActionValues    = new Dictionary<string, int[]>();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DRAG-DEFERRED NUMERIC FIELDS
        // ════════════════════════════════════════════════════════════════════════
        //
        // Why these exist: a stock `EditorGUILayout.FloatField` with a draggable
        // label writes to its source every MouseDrag tick (30-60Hz). On a
        // Mochie-FX-scale EnigmaController, every write to the data component
        // triggers a ~1.5s native-side cascade — likely Unity's per-frame undo
        // diff / prefab override propagation across the 15k-value
        // EnigmaControllerData MonoBehaviour. The freeze was reproducible in
        // EnigmaPerfProbe logs as 1.5-1.6s `EditorApplication.update gap`
        // entries between MouseDrag events, while C# code returned in <5ms
        // (the lag was downstream of our OnInspectorGUI return).
        //
        // The helpers below intercept the drag: during a label-scrub drag
        // (Event.type == MouseDrag with non-zero hot control), the new value
        // is buffered in static state and shown back through the field on the
        // next repaint, but the caller-supplied setter is NOT invoked. On
        // MouseUp (release), the buffered value is committed via a single
        // Undo.RecordObject + setter call — the native cascade fires once
        // instead of 60×/second.
        //
        // Direct-type commits (typing into the field + Enter / focus change)
        // bypass the buffer and commit immediately, since they're already
        // one-shot.

        // The action object whose field is being scrubbed. null = no active scrub.
        // Using an action reference + field key disambiguates simultaneous scrubs
        // across actions (IMGUI only allows one active hot control, but the
        // reference catches the case where the user clicks one field then drags
        // a different one in the same OnInspectorGUI pass).
        private static EnigmaActionData _scrubAction;
        private static int   _scrubFieldKey;
        private static float _scrubFloatBuffer;
        private static int   _scrubIntBuffer;

        /// <summary>
        /// FloatField variant that defers commits until MouseUp. Use for any
        /// action-data float that can lag the inspector when the data lives on
        /// a large MonoBehaviour (EnigmaControllerData).
        /// </summary>
        public static void DragDeferredFloatField(
            string label,
            EnigmaActionData ownerAction,
            int fieldKey,
            System.Func<float> get,
            System.Action<float> commit,
            UnityEngine.Object undoTarget,
            string undoLabel)
        {
            bool isActiveScrub = _scrubAction == ownerAction && _scrubFieldKey == fieldKey;
            float displayValue = isActiveScrub ? _scrubFloatBuffer : get();

            // Capture the event type and hot-control state BEFORE the FloatField
            // consumes the event. After EditorGUILayout.FloatField returns, the
            // mouse-drag event has already been turned into Used and hotControl
            // may be cleared on a MouseUp tick — our scrub vs commit branching
            // needs the pre-call state.
            var evt = Event.current;
            EventType prevEventType = evt.type;
            int prevHotControl = GUIUtility.hotControl;

            EditorGUI.BeginChangeCheck();
            float newValue = EditorGUILayout.FloatField(label, displayValue);
            bool changed = EditorGUI.EndChangeCheck();

            // A drag-scrub is in progress if the incoming event was a MouseDrag
            // AND some IMGUI control already owned the hot control before this
            // field ran (i.e., the FloatField label was claimed on a prior
            // MouseDown). Includes MouseDown because the FloatField also
            // commits a tiny initial delta on the first mouse-down tick.
            bool scrubInProgress =
                (prevEventType == EventType.MouseDrag && prevHotControl != 0)
                || (prevEventType == EventType.MouseDown && prevHotControl != 0);

            if (changed)
            {
                if (scrubInProgress)
                {
                    // Drag-scrub: buffer the value, don't touch the data component.
                    _scrubAction = ownerAction;
                    _scrubFieldKey = fieldKey;
                    _scrubFloatBuffer = newValue;
                }
                else
                {
                    // Typing / Enter / focus loss: commit immediately.
                    if (undoTarget != null) Undo.RecordObject(undoTarget, undoLabel);
                    commit(newValue);
                    if (isActiveScrub)
                    {
                        _scrubAction = null;
                        _scrubFieldKey = 0;
                        _scrubFloatBuffer = 0f;
                    }
                }
            }

            // Commit a buffered scrub on MouseUp. Check both prev and current
            // event types — IMGUI may have already consumed the MouseUp into
            // Used by the time we re-read Event.current here.
            bool isMouseUpRelease = isActiveScrub &&
                (prevEventType == EventType.MouseUp || evt.type == EventType.MouseUp);
            if (isMouseUpRelease)
            {
                float committed = _scrubFloatBuffer;
                _scrubAction = null;
                _scrubFieldKey = 0;
                _scrubFloatBuffer = 0f;
                if (undoTarget != null) Undo.RecordObject(undoTarget, undoLabel);
                commit(committed);
                GUI.changed = true;
            }
        }

        /// <summary>
        /// IntField variant of <see cref="DragDeferredFloatField"/>.
        /// </summary>
        public static void DragDeferredIntField(
            string label,
            EnigmaActionData ownerAction,
            int fieldKey,
            System.Func<int> get,
            System.Action<int> commit,
            UnityEngine.Object undoTarget,
            string undoLabel)
        {
            bool isActiveScrub = _scrubAction == ownerAction && _scrubFieldKey == fieldKey;
            int displayValue = isActiveScrub ? _scrubIntBuffer : get();

            var evt = Event.current;
            EventType prevEventType = evt.type;
            int prevHotControl = GUIUtility.hotControl;

            EditorGUI.BeginChangeCheck();
            int newValue = EditorGUILayout.IntField(label, displayValue);
            bool changed = EditorGUI.EndChangeCheck();

            bool scrubInProgress =
                (prevEventType == EventType.MouseDrag && prevHotControl != 0)
                || (prevEventType == EventType.MouseDown && prevHotControl != 0);

            if (changed)
            {
                if (scrubInProgress)
                {
                    _scrubAction = ownerAction;
                    _scrubFieldKey = fieldKey;
                    _scrubIntBuffer = newValue;
                }
                else
                {
                    if (undoTarget != null) Undo.RecordObject(undoTarget, undoLabel);
                    commit(newValue);
                    if (isActiveScrub)
                    {
                        _scrubAction = null;
                        _scrubFieldKey = 0;
                        _scrubIntBuffer = 0;
                    }
                }
            }

            bool isMouseUpRelease = isActiveScrub &&
                (prevEventType == EventType.MouseUp || evt.type == EventType.MouseUp);
            if (isMouseUpRelease)
            {
                int committed = _scrubIntBuffer;
                _scrubAction = null;
                _scrubFieldKey = 0;
                _scrubIntBuffer = 0;
                if (undoTarget != null) Undo.RecordObject(undoTarget, undoLabel);
                commit(committed);
                GUI.changed = true;
            }
        }

        /// <summary>
        /// Returns the right undo/dirty target for action-data mutations. When
        /// the action lives on an EnigmaController's companion data component,
        /// returns the EnigmaControllerData; otherwise (e.g. standalone
        /// EnigmaButton), returns the caller-supplied dirtyObj.
        /// </summary>
        public static UnityEngine.Object ResolveActionUndoTarget(UnityEngine.Object dirtyObj, EnigmaController ctrl)
        {
            if (ctrl != null)
            {
                var dataComp = ctrl.GetComponent<EnigmaControllerData>();
                if (dataComp != null) return dataComp;
            }
            return dirtyObj;
        }

        // Known VRSL fixture script type names. Match by full name to avoid a
        // hard reference on the VRSL package (so this still compiles in
        // projects without VRSL installed).
        private static readonly System.Collections.Generic.HashSet<string> VRSLFixtureTypeNames =
            new System.Collections.Generic.HashSet<string>
            {
                "VRSL.VRStageLighting_AudioLink_Static",
                "VRSL.VRStageLighting_AudioLink_Laser",
                "VRSL.VRStageLighting_DMX_Static",
            };

        /// <summary>
        /// Returns true when <paramref name="r"/> is part of a VRSL fixture
        /// (its renderer GameObject or any ancestor carries one of the
        /// VRSL stage-light scripts). Walks parents because VRSL fixtures
        /// keep their renderers on dedicated child meshes (the volumetric /
        /// projection / GI-point meshes) while the controlling script lives
        /// on the prefab root.
        /// </summary>
        public static bool IsVRSLFixtureRenderer(UnityEngine.Renderer r)
        {
            if (r == null) return false;
            for (var t = r.transform; t != null; t = t.parent)
            {
                var comps = t.GetComponents<UnityEngine.Component>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    if (VRSLFixtureTypeNames.Contains(c.GetType().FullName))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Renders a HelpBox warning when a material-targeting fader is
        /// pointed at a VRSL fixture mesh. VRSL's fixture script writes
        /// <c>_Emission</c> via MaterialPropertyBlock every frame, so any
        /// fader that drives a material property on a VRSL renderer is
        /// immediately overwritten. The user should switch to the Udon
        /// path and target the <c>lightColorTint</c> variable on the
        /// fixture root&#x2019;s UdonBehaviour instead.
        /// </summary>
        public static void DrawVRSLFaderWarningIfNeeded(UnityEngine.Renderer r)
        {
            if (!IsVRSLFixtureRenderer(r)) return;
            EditorGUILayout.HelpBox(
                "This is probably not the property you're looking for. " +
                "Target lightColorTint on the VRSL UdonBehaviour instead — " +
                "the fixture's script overwrites material properties every frame.",
                MessageType.Warning);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION MODEL — LOOKUP TABLES
        // ════════════════════════════════════════════════════════════════════════

        private static readonly string[] CATEGORY_NAMES = {
            "Toggle", "Command", "Selection", "Preset", "Display", "System"
        };

        private static readonly string[] TARGET_NAMES = {
            "Object",           // 0
            "Renderer",         // 1
            "Component",        // 2
            "Material",         // 3
            "Shader Property",  // 4
            "Skybox",           // 5
            "Udon Behaviour",   // 6
            "Transform",        // 7
            "Player",           // 8
            "Color Palette",    // 9
            "Variant Group",    // 10
            "Autochange Group", // 11
            "Preset Slot",      // 12
            "Controller",       // 13
            "World Stats",      // 14
            "Screen Shader",    // 15
        };

        private static readonly string[] OPERATION_NAMES = {
            "Toggle",           // 0
            "Apply",            // 1
            "Set",              // 2
            "Set State",        // 3
            "Set Variable",     // 4
            "Trigger Event",    // 5
            "Teleport",         // 6
            "Next",             // 7
            "Previous",         // 8
            "Select",           // 9
            "Save",             // 10
            "Load",             // 11
            "Save or Load",     // 12
            "Clear",            // 13
            "Show",             // 14
            "Next Folder",      // 15
            "Previous Folder",  // 16
            "Go To Folder",     // 17
            "Next Page",        // 18
            "Previous Page",    // 19
            "Go To Page",       // 20
            "Reset",            // 21
            "Set Fader Mode",   // 22
        };

        /// <summary>Returns the valid ActionTarget indices for a given Category.</summary>
        public static int[] GetValidTargets(int category)
        {
            switch (category)
            {
                case 0: return new[] { 0, 1, 2, 3, 4, 5, 6, 7, 11, 13, 15 };
                case 1: return new[] { 0, 2, 3, 4, 5, 6, 7, 8, 11, 13 };
                case 2: return new[] { 9, 10 };
                case 3: return new[] { 12 };
                case 4: return new[] { 4, 6, 9, 10, 11, 13, 14 };
                case 5: return new[] { 13 };
                default: return new[] { 0 };
            }
        }

        /// <summary>Returns the valid ActionOperation indices for a Category + Target pair.</summary>
        public static int[] GetValidOperations(int category, int target)
        {
            if (category == 0)
            {
                if (target == 0 || target == 1 || target == 2 || target == 11 || target == 13 || target == 15)
                    return new[] { 0 };
                if (target == 3 || target == 5)
                    return new[] { 1 };
                if (target == 4)
                    return new[] { 2, 3 };
                if (target == 7)
                    return new[] { 0 };
                if (target == 6)
                    return new[] { 4 };
            }
            if (category == 1)
            {
                if (target == 0)
                    return new[] { 3, 6 };
                if (target == 2 || target == 11 || target == 13)
                    return new[] { 3 };
                if (target == 3 || target == 5)
                    return new[] { 1 };
                if (target == 4)
                    return new[] { 2, 3 };
                if (target == 7)
                    return new[] { 2 };
                if (target == 6)
                    return new[] { 5, 4 };
                if (target == 8)
                    return new[] { 6 };
            }
            if (category == 2) return new[] { 7, 8, 9 };
            if (category == 3) return new[] { 12, 10, 11, 13 };
            if (category == 4)
            {
                return new[] { 14 };
            }
            if (category == 5) return new[] { 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27 };
            return new[] { 0 };
        }

        /// <summary>
        /// Syncs the runtime actionType from the current Category + Target + Operation triple.
        /// Must be called whenever category, target, or operation changes in the editor.
        /// </summary>
        public static void SyncActionType(EnigmaActionData action)
        {
            int cat = action.category;
            int tgt = action.target;
            int op  = action.operation;

            if (cat == 0)
            {
                if      (tgt == 0)  action.actionType = 0;
                else if (tgt == 3)  action.actionType = 1;
                else if (tgt == 4 && op == 3)  action.actionType = 27;
                else if (tgt == 4)  action.actionType = 2;
                else if (tgt == 5)  action.actionType = 22;
                else if (tgt == 6)  action.actionType = 6;
                else if (tgt == 7)  action.actionType = 23;
                else if (tgt == 11) action.actionType = 14;
                else if (tgt == 13)
                {
                    // Toggle Whitelist — actionType 28. Runtime convention:
                    // defaultFloatValue >= 0.5 = ON, < 0.5 = OFF. New actions
                    // default to ON ("whitelist normally enabled, toggling
                    // pushes it OFF and back"). Only initialize when the
                    // field is at its struct default (0.0); preserves the
                    // user's value if they cycle through other types and
                    // back to Toggle Whitelist.
                    action.actionType = 28;
                    if (action.defaultFloatValue == 0.0f)
                        action.defaultFloatValue = 1.0f;
                }
                else if (tgt == 15) action.actionType = 26;
            }
            else if (cat == 1)
            {
                if      (tgt == 0  && op == 3)  action.actionType = 15;
                else if (tgt == 0  && op == 6)
                {
                    action.actionType = 13;
                    if (action.propertyType != 3 && action.propertyType != 4)
                        action.propertyType = 3;
                }
                else if (tgt == 3)              action.actionType = 1;
                else if (tgt == 4  && op == 3)  action.actionType = 27;
                else if (tgt == 4)              action.actionType = 2;
                else if (tgt == 5)              action.actionType = 4;
                else if (tgt == 6  && op == 5)  action.actionType = 5;
                else if (tgt == 6  && op == 4)  action.actionType = 6;
                else if (tgt == 7)              action.actionType = 12;
                else if (tgt == 8  && op == 6)  action.actionType = 13;
                else if (tgt == 11 && op == 3)  action.actionType = 17;
                else if (tgt == 13 && op == 3)  action.actionType = 18;
            }
            else if (cat == 2)
            {
                if (tgt == 9)
                {
                    if (op == 9) { action.actionType = 10; action.colorSelectorRole = 1; }
                    else
                    {
                        action.actionType        = 10;
                        action.colorSelectorRole = 2;
                        action.propertyType      = (op == 8) ? 1 : 0;
                    }
                }
                else if (tgt == 10)
                {
                    action.actionType = 19;
                    if (op == 9)
                    {
                        action.variantSelectorRole = 1;
                    }
                    else
                    {
                        action.variantSelectorRole = 2;
                        action.propertyType        = (op == 8) ? 1 : 0;
                    }
                }
            }
            else if (cat == 3)
            {
                action.actionType = 8;
                if (op == 10)      action.presetRole = 1;
                else if (op == 11) action.presetRole = 2;
                else if (op == 13) action.presetRole = 3;
                else               action.presetRole = 0;
            }
            else if (cat == 4)
            {
                if (tgt == 9)  { action.actionType = 10; action.colorSelectorRole = 0; }
                else if (tgt == 10) { action.actionType = 19; action.variantSelectorRole = 0; }
                else if (tgt == 14) action.actionType = 21;
                else             action.actionType = 9;
            }
            else if (cat == 5)
            {
                if      (op == 23) action.actionType = 24;
                else if (op == 24) action.actionType = 25;
                else { action.actionType = 20; action.propertyType = op >= 15 ? op - 15 : 0; }
            }
        }

        /// <summary>Human-readable label for a category/target/operation triple.</summary>
        public static string GetActionLabel(int cat, int tgt, int op)
        {
            switch (cat)
            {
                case 0:
                    if (tgt == 0)  return "Toggle Object";
                    if (tgt == 3)  return "Toggle Material";
                    if (tgt == 4 && op == 3) return "Toggle Shader Keyword";
                    if (tgt == 4)  return "Toggle Shader Property";
                    if (tgt == 5)  return "Toggle Skybox";
                    if (tgt == 6)  return "Toggle Udon Variable";
                    if (tgt == 7)  return "Toggle Transform";
                    if (tgt == 11) return "Toggle Autochange Group";
                    if (tgt == 13) return "Toggle Whitelist";
                    if (tgt == 15) return "Toggle Shader";
                    break;
                case 1:
                    if (tgt == 0  && op == 6) return "Teleport Object";
                    if (tgt == 0)             return "Set Object State";
                    if (tgt == 3)             return "Apply Material";
                    if (tgt == 4 && op == 3)  return "Set Shader Keyword";
                    if (tgt == 4)             return "Set Shader Property";
                    if (tgt == 5)             return "Apply Skybox";
                    if (tgt == 6  && op == 5) return "Trigger Udon Event";
                    if (tgt == 6)             return "Set Udon Variable";
                    if (tgt == 7)             return "Set Transform";
                    if (tgt == 8)             return "Teleport Player";
                    if (tgt == 11)            return "Set Autochange Group State";
                    if (tgt == 13)            return "Set Whitelist";
                    break;
                case 2:
                    if (tgt == 9  && op == 8) return "Previous Color";
                    if (tgt == 9  && op == 9) return "Set Color";
                    if (tgt == 9)             return "Next Color";
                    if (tgt == 10 && op == 8) return "Previous Variant";
                    if (tgt == 10 && op == 9) return "Set Variant";
                    if (tgt == 10)            return "Next Variant";
                    break;
                case 3:
                    if (op == 10) return "Save Presets";
                    if (op == 11) return "Load Presets";
                    if (op == 13) return "Clear Presets";
                    return "Preset Slot";
                case 4:
                    if (tgt == 4)  return "Display Shader Property";
                    if (tgt == 6)  return "Display Udon Variable";
                    if (tgt == 9)  return "Display Color Palette";
                    if (tgt == 10) return "Display Variant Group";
                    if (tgt == 11) return "Display Autochange Group";
                    if (tgt == 13) return "Display Controller";
                    if (tgt == 14) return "Display Stat";
                    break;
                case 5:
                    if (op == 15) return "Next Folder";
                    if (op == 16) return "Previous Folder";
                    if (op == 17) return "Go To Folder";
                    if (op == 18) return "Next Page";
                    if (op == 19) return "Previous Page";
                    if (op == 20) return "Go To Page";
                    if (op == 21) return "Reset";
                    if (op == 22) return "Set Fader Mode";
                    if (op == 23) return "Display Folder Name";
                    if (op == 24) return "Display Page Number";
                    if (op == 25) return "Next Fader Page";
                    if (op == 26) return "Previous Fader Page";
                    if (op == 27) return "Go To Fader Page";
                    break;
            }
            return $"Action ({cat}/{tgt}/{op})";
        }

        /// <summary>
        /// Maps a Unity shader property type to the unified Display Value type index
        /// (0=Float, 1=Bool, 2=Int, 3=String, 4=Color, 5=Vector).
        /// </summary>
        /// <summary>
        /// Converts a fader link ID to a deterministic, visually distinct color.
        /// Used to color drag handles on linked actions and fader links.
        /// </summary>
        public static Color LinkIdToColor(int linkId)
        {
            float hue = ((linkId * 0.618033988f) % 1f + 1f) % 1f; // golden ratio spread
            return Color.HSVToRGB(hue, 0.6f, 0.8f) * new Color(1f, 1f, 1f, 0.6f);
        }

        // ShaderPropTypeToUnified removed — was using wrong mapping (Color=4, Vector=5).
        // All callers now use ShaderPropToActionType (Color=1, Vector=2).

        // ── Array helpers ──────────────────────────────────────────────────────

        public static EnigmaActionData[] AddAction(EnigmaActionData[] actions, EnigmaActionData action)
        {
            var list = new List<EnigmaActionData>(actions) { action };
            return list.ToArray();
        }

        public static EnigmaActionData[] RemoveActionAt(EnigmaActionData[] actions, int idx)
        {
            var list = new List<EnigmaActionData>(actions);
            list.RemoveAt(idx);
            return list.ToArray();
        }

        public static EnigmaActionData[] InsertActionAt(EnigmaActionData[] actions, int idx, EnigmaActionData action)
        {
            var list = new List<EnigmaActionData>(actions);
            int clamped = Mathf.Clamp(idx, 0, list.Count);
            list.Insert(clamped, action);
            return list.ToArray();
        }

        // Deep-clones an EnigmaActionData. Uses JsonUtility so every
        // [SerializeField] field (including Unity Object references, which
        // JsonUtility serializes by instance ID) comes across without
        // hand-maintaining a field-by-field copy. The clone shares the
        // original's scene-object references on purpose — a duplicated "Set
        // Shader Property" action targeting Renderer X should still target X.
        // The caller can overwrite anything it wants to diverge after the
        // copy (e.g. reset faderLinkId).
        public static EnigmaActionData CloneAction(EnigmaActionData src)
        {
            if (src == null) return new EnigmaActionData();
            string json = JsonUtility.ToJson(src);
            return JsonUtility.FromJson<EnigmaActionData>(json);
        }

        public static bool[] BuildFolderIncludedBools(int[] indices, int totalFolders)
        {
            bool[] included = new bool[totalFolders];
            if (indices == null) return included;
            foreach (int idx in indices)
                if (idx >= 0 && idx < totalFolders)
                    included[idx] = true;
            return included;
        }

        public static int[] BoolsToFolderIndices(bool[] included)
        {
            var result = new List<int>();
            for (int i = 0; i < included.Length; i++)
                if (included[i]) result.Add(i);
            return result.ToArray();
        }

        /// <summary>
        /// Draws an amber-tinted tag pill with an ✕ button for per-action options.
        /// </summary>
        public static void DrawActionTagPill(string label, System.Action onRemove)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.55f, 0.15f);
            if (GUILayout.Button($"{label}  ✕", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                GUI.backgroundColor = oldBg;
                onRemove();
                return;
            }
            GUI.backgroundColor = oldBg;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PUBLIC ENTRY POINT
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws the full action list editor for the supplied actions array.
        /// </summary>
        /// <param name="dirtyObj">Object to mark dirty on any change (controller or button).</param>
        /// <param name="ctrl">The linked controller; may be null for standalone buttons.</param>
        /// <param name="actions">The action array to edit (passed by ref so additions/removals are reflected).</param>
        public void DrawActionList(UnityEngine.Object dirtyObj, EnigmaController ctrl, ref EnigmaActionData[] actions,
            EnigmaEntryData entry = null)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            if (actions == null) actions = new EnigmaActionData[0];

            EnigmaFolderData[] allFolders = ctrl != null
                ? (ctrl.GetFolders() ?? new EnigmaFolderData[0])
                : new EnigmaFolderData[0];

            // Keep row-Y tracking list sized to exactly actions.Length + 1 (prevents unbounded growth)
            while (_rowTopYs.Count < actions.Length + 1) _rowTopYs.Add(0f);
            while (_rowTopYs.Count > actions.Length + 1) _rowTopYs.RemoveAt(_rowTopYs.Count - 1);

            for (int a = 0; a < actions.Length; a++)
            {
                _drawIndex = a;
                var action = actions[a];


                // ── Drag-and-drop row marker ──
                Rect rowMarker = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint && a < _rowTopYs.Count)
                    _rowTopYs[a] = rowMarker.y;
                if (_dragSourceIndex >= 0 && _dragTargetLine == a)
                    EditorGUI.DrawRect(new Rect(rowMarker.x, rowMarker.y - 1f, rowMarker.width, 2f),
                        new Color(0.25f, 0.65f, 1f, 0.9f));

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                string actionKey = $"actiontype_{action.GetHashCode()}";
                if (_pendingActionValues.TryGetValue(actionKey, out int[] pendingInHeader))
                {
                    _pendingActionValues.Remove(actionKey);
                    action.category  = pendingInHeader[0];
                    action.target    = pendingInHeader[1];
                    action.operation = pendingInHeader[2];
                    SyncActionType(action);
                    EditorUtility.SetDirty(dirtyObj);
                }

                // ── Action row header ──
                EditorGUILayout.BeginHorizontal();

                // Drag handle — shaded box, colored if linked to a fader
                Rect handleRect = GUILayoutUtility.GetRect(24f, 18f, GUILayout.Width(24));
                if (Event.current.type == EventType.Repaint)
                {
                    Color handleColor = action.faderLinkId != 0
                        ? LinkIdToColor(action.faderLinkId)
                        : new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.25f : 0.15f);
                    EditorGUI.DrawRect(handleRect, handleColor);
                }
                EditorGUI.LabelField(handleRect, "\u283F", EditorStyles.centeredGreyMiniLabel);
                EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && handleRect.Contains(Event.current.mousePosition))
                {
                    _dragSourceIndex = a;
                    _dragTargetLine  = a;
                    Event.current.Use();
                }

                // Action type label (clickable to collapse/expand)
                bool actCollapsed = _collapsedActions.Contains(a);
                string actArrow = actCollapsed ? "\u25B6" : "\u25BC";
                string actLabel = $"{actArrow} {GetActionLabel(action.category, action.target, action.operation)}";
                Rect actLabelRect = GUILayoutUtility.GetRect(
                    new GUIContent(actLabel), EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                EditorGUI.LabelField(actLabelRect, actLabel, EditorStyles.boldLabel);
                EditorGUIUtility.AddCursorRect(actLabelRect, MouseCursor.Link);
                if (Event.current.type == EventType.MouseDown && actLabelRect.Contains(Event.current.mousePosition))
                {
                    if (actCollapsed) _collapsedActions.Remove(a);
                    else _collapsedActions.Add(a);
                    Event.current.Use();
                }

                GUILayout.FlexibleSpace();

                if (action.useDelay)
                    DrawActionTagPill($"Delay: {action.delaySeconds:0.##}s", () =>
                    {
                        action.useDelay = false;
                        EditorUtility.SetDirty(dirtyObj);
                    });

                if (action.useLerp)
                    DrawActionTagPill($"Lerp: {action.lerpSeconds:0.##}s", () =>
                    {
                        action.useLerp = false;
                        EditorUtility.SetDirty(dirtyObj);
                    });

                if (action.useCondition)
                    DrawActionTagPill("Condition", () =>
                    {
                        action.useCondition = false;
                        EditorUtility.SetDirty(dirtyObj);
                    });

                // Type picker button
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    string capturedKey = actionKey;
                    OpenActionTypePicker(ctrl, selected =>
                    {
                        _pendingActionValues[capturedKey] = selected;
                        _repaint();
                    });
                }

                // Duplicate — inserts a clone of this action at a+1. Mirrors
                // the duplicate button on entries (EnigmaControllerEditor.
                // Folders.cs). Same icon for visual consistency.
                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Duplicate", "|Duplicate action"), GUILayout.Width(24)))
                {
                    var clone = CloneAction(action);
                    actions = InsertActionAt(actions, a + 1, clone);
                    _collapsedActions.Clear();
                    EditorUtility.SetDirty(dirtyObj);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    _repaint();
                    break;
                }

                // Per-action options menu
                if (GUILayout.Button("Options ▾", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    var capturedAction = action;
                    var capturedDirty  = dirtyObj;
                    var menu = new GenericMenu();

                    menu.AddItem(new GUIContent("Delay"), capturedAction.useDelay, () =>
                    {
                        capturedAction.useDelay = !capturedAction.useDelay;
                        EditorUtility.SetDirty(capturedDirty);
                        _repaint();
                    });

                    // Lerp — Set Shader Property actions with an interpolable
                    // value type (Float / Color / Vector — not Texture).
                    bool canLerp = capturedAction.actionType == 2
                                   && capturedAction.propertyType != 3;
                    if (canLerp)
                    {
                        menu.AddItem(new GUIContent("Lerp"), capturedAction.useLerp, () =>
                        {
                            capturedAction.useLerp = !capturedAction.useLerp;
                            EditorUtility.SetDirty(capturedDirty);
                            _repaint();
                        });
                    }

                    menu.AddItem(new GUIContent("Condition"), capturedAction.useCondition, () =>
                    {
                        capturedAction.useCondition = !capturedAction.useCondition;
                        EditorUtility.SetDirty(capturedDirty);
                        _repaint();
                    });

                    // Fader Link — for shader property, keyword, skybox, and udon actions.
                    bool canFaderLink = entry != null &&
                        (capturedAction.actionType == 2   // Set Shader Property
                         || capturedAction.actionType == 27 // Shader Keyword
                         || capturedAction.actionType == 22 // Toggle Skybox
                         || capturedAction.actionType == 5  // Trigger Udon Event
                         || capturedAction.actionType == 6); // Set Udon Variable

                    if (canFaderLink)
                    {
                        menu.AddItem(new GUIContent("Fader Link"), false, () =>
                        {
                            // Ensure assignFader is enabled.
                            if (!entry.assignFader)
                            {
                                entry.assignFader = true;
                                if (entry.faderLinks == null || entry.faderLinks.Length == 0)
                                    entry.faderLinks = new EnigmaFaderLinkData[0];
                            }

                            // Migrate single faderLink if it has meaningful data.
                            if ((entry.faderLinks == null || entry.faderLinks.Length == 0)
                                && entry.faderLink != null
                                && (entry.faderLink.targetRenderer != null
                                    || entry.faderLink.targetsSkybox
                                    || !string.IsNullOrEmpty(entry.faderLink.propertyName)))
                                entry.faderLinks = new[] { entry.faderLink };

                            // Generate a unique link ID and assign to both action and fader link.
                            int linkId = capturedAction.faderLinkId != 0
                                ? capturedAction.faderLinkId
                                : System.Environment.TickCount ^ capturedAction.GetHashCode();
                            capturedAction.faderLinkId = linkId;

                            // Create a new fader link pre-populated from the action.
                            var newLink = new EnigmaFaderLinkData { faderLinkId = linkId };
                            if (capturedAction.actionType == 22) // Toggle Skybox
                            {
                                newLink.targetsSkybox = true;
                                newLink.skyboxMaterial = capturedAction.targetMaterial;
                            }
                            else if (capturedAction.actionType == 5 || capturedAction.actionType == 6) // Udon
                            {
                                newLink.targetsUdon = true;
                                newLink.targetUdonBehaviours = capturedAction.targetUdon != null
                                    ? new UdonSharp.UdonSharpBehaviour[] { capturedAction.targetUdon } : null;
                                newLink.udonVariableName = capturedAction.actionType == 6
                                    ? (capturedAction.udonVariableName ?? "") : "";
                            }
                            else
                            {
                                newLink.targetRenderer = capturedAction.targetRenderer;
                                newLink.materialIndex  = capturedAction.materialIndex;
                                newLink.propertyName   = capturedAction.propertyName ?? "";
                                newLink.propertyType   = capturedAction.propertyType;
                                newLink.defaultValue   = capturedAction.defaultFloatValue;
                                newLink.defaultColor   = capturedAction.defaultColorValue;
                            }

                            var list = new System.Collections.Generic.List<EnigmaFaderLinkData>(entry.faderLinks);
                            list.Add(newLink);
                            entry.faderLinks = list.ToArray();

                            EditorUtility.SetDirty(capturedDirty);
                            _repaint();
                        });
                    }

                    menu.ShowAsContext();
                }

                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    actions = RemoveActionAt(actions, a);
                    _collapsedActions.Clear();
                    EditorUtility.SetDirty(dirtyObj);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                if (!actCollapsed && action.useDelay)
                {
                    EditorGUI.indentLevel++;
                    action.delaySeconds = Mathf.Max(0f,
                        EditorGUILayout.FloatField("Delay (s)", action.delaySeconds));
                    // Delay defaults to activation-only. The "Also Delay on
                    // Deactivation" checkbox restores the legacy behaviour
                    // where deactivation also waits for the same delay before
                    // running the action's deactivate path. Useful for cases
                    // like "fade out after N seconds" — most actions only
                    // need delay on activation.
                    action.delayOnDeactivate = EditorGUILayout.Toggle(
                        new GUIContent("Also Delay on Deactivation",
                            "When off (default), the delay only applies on activation; deactivation runs immediately. " +
                            "When on, the delay applies to both."),
                        action.delayOnDeactivate);
                    EditorGUI.indentLevel--;
                }

                if (!actCollapsed && action.useLerp)
                {
                    EditorGUI.indentLevel++;
                    action.lerpSeconds = Mathf.Max(0f,
                        EditorGUILayout.FloatField("Lerp (s)", action.lerpSeconds));
                    // Mirrors Delay's activation/deactivation split: by
                    // default the fade only plays on activation — turning the
                    // button off snaps back to the default value immediately.
                    // The checkbox fades the deactivation too (current value
                    // back to the default over the same duration).
                    //
                    // Only shown for Toggle-category actions: Set (category 1)
                    // actions are non-stateful and never take the deactivate
                    // path at runtime, so the checkbox would be dead weight.
                    if (action.category == 0)
                    {
                        action.lerpOnDeactivate = EditorGUILayout.Toggle(
                            new GUIContent("Also Lerp on Deactivation",
                                "When off (default), the fade only plays on activation; deactivation snaps to the default value. " +
                                "When on, deactivation fades from the current value back to the default over the same duration."),
                            action.lerpOnDeactivate);
                    }
                    EditorGUI.indentLevel--;
                }

                if (!actCollapsed && action.useCondition)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("Condition", EditorStyles.miniBoldLabel);

                    // Folder picker
                    string[] folderNames;
                    if (allFolders.Length > 0)
                    {
                        folderNames = new string[allFolders.Length];
                        for (int fi = 0; fi < allFolders.Length; fi++)
                            folderNames[fi] = !string.IsNullOrEmpty(allFolders[fi].name)
                                ? allFolders[fi].name : $"Folder {fi}";
                    }
                    else
                    {
                        folderNames = new[] { "(no folders)" };
                    }
                    action.conditionFolderIndex = Mathf.Clamp(action.conditionFolderIndex, 0, Mathf.Max(0, allFolders.Length - 1));
                    action.conditionFolderIndex = EditorGUILayout.Popup("Folder", action.conditionFolderIndex, folderNames);

                    // Entry picker
                    string[] entryNames;
                    if (allFolders.Length > 0 && action.conditionFolderIndex < allFolders.Length)
                    {
                        var folder = allFolders[action.conditionFolderIndex];
                        var entries = folder.entries ?? new EnigmaEntryData[0];
                        entryNames = new string[entries.Length];
                        for (int ei = 0; ei < entries.Length; ei++)
                        {
                            if (entries[ei].isEmpty)
                                entryNames[ei] = $"(empty slot {ei})";
                            else
                                entryNames[ei] = !string.IsNullOrEmpty(entries[ei].label)
                                    ? entries[ei].label : $"Entry {ei}";
                        }
                        if (entryNames.Length == 0) entryNames = new[] { "(no entries)" };
                    }
                    else
                    {
                        entryNames = new[] { "(no entries)" };
                    }
                    action.conditionEntryIndex = Mathf.Clamp(action.conditionEntryIndex, 0, Mathf.Max(0, entryNames.Length - 1));
                    action.conditionEntryIndex = EditorGUILayout.Popup("Entry", action.conditionEntryIndex, entryNames);

                    int condState = action.conditionRequireActive ? 0 : 1;
                    condState = EditorGUILayout.Popup("Is", condState, new[] { "Active", "Inactive" });
                    action.conditionRequireActive = condState == 0;
                    EditorGUI.indentLevel--;
                }

                if (!actCollapsed)
                {
                // Capture state before drawing for fader link auto-sync.
                var prevRenderer = action.targetRenderer;
                int prevMatIdx   = action.materialIndex;
                var prevSkyMat   = action.targetMaterial;
                var prevUdon     = action.targetUdon;

                EditorGUI.BeginChangeCheck();
                DrawActionBody(dirtyObj, ctrl, action);
                if (EditorGUI.EndChangeCheck())
                {
                    // The change is on the action — which lives in
                    // EnigmaControllerData.folders[i].entries[j].actions[k],
                    // NOT on the controller. SetDirty-ing the controller here
                    // was wrong-target work — its serialized state hadn't
                    // changed. The Undo.RecordObject(dataComp) at the top of
                    // DrawSelectedButtonSettings already marks the data
                    // component dirty for any mutation event.
                    //
                    // For non-action-list callers (EnigmaButtonEditor passes
                    // the standalone EnigmaButton as dirtyObj), the SetDirty
                    // path is still correct, so gate by type.
                    if (dirtyObj != null && !(dirtyObj is EnigmaController))
                        EditorUtility.SetDirty(dirtyObj);
                }

                // Auto-sync linked fader links when target references change.
                if (entry != null && action.faderLinkId != 0)
                {
                    bool changed = action.targetRenderer != prevRenderer
                                || action.materialIndex != prevMatIdx
                                || action.targetMaterial != prevSkyMat
                                || action.targetUdon != prevUdon;

                    if (changed && entry.faderLinks != null)
                    {
                        foreach (var fl in entry.faderLinks)
                        {
                            if (fl == null || fl.faderLinkId != action.faderLinkId) continue;
                            if (action.actionType == 22) // Toggle Skybox
                            {
                                fl.skyboxMaterial = action.targetMaterial;
                            }
                            else if (action.actionType == 5 || action.actionType == 6) // Udon
                            {
                                fl.targetUdonBehaviours = action.targetUdon != null
                                    ? new UdonSharp.UdonSharpBehaviour[] { action.targetUdon } : null;
                            }
                            else
                            {
                                fl.targetRenderer = action.targetRenderer;
                                fl.materialIndex  = action.materialIndex;
                            }
                        }
                        EditorUtility.SetDirty(dirtyObj);
                    }
                }
                } // end if (!actCollapsed)

                EditorGUILayout.EndVertical();
            }

            // ── Bottom drop-target marker ──
            Rect bottomMarker = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && actions.Length < _rowTopYs.Count)
                _rowTopYs[actions.Length] = bottomMarker.y;
            if (_dragSourceIndex >= 0 && _dragTargetLine == actions.Length)
                EditorGUI.DrawRect(new Rect(bottomMarker.x, bottomMarker.y - 1f, bottomMarker.width, 2f),
                    new Color(0.25f, 0.65f, 1f, 0.9f));

            // ── Drag tracking / commit ──
            if (_dragSourceIndex >= 0)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    float mouseY = Event.current.mousePosition.y;
                    int best = 0;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i <= actions.Length && i < _rowTopYs.Count; i++)
                    {
                        float d = Mathf.Abs(_rowTopYs[i] - mouseY);
                        if (d < bestDist) { bestDist = d; best = i; }
                    }
                    if (best != _dragTargetLine) { _dragTargetLine = best; _repaint(); }
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    int src = _dragSourceIndex;
                    int tgt = _dragTargetLine;
                    _dragSourceIndex = -1;
                    _dragTargetLine  = -1;
                    if (tgt >= 0 && tgt != src && tgt != src + 1)
                    {
                        var list = new List<EnigmaActionData>(actions);
                        var item  = list[src];
                        list.RemoveAt(src);
                        // When dragging downward, removing src shifts all later indices by -1,
                        // so the logical insertion point shifts down by one as well.
                        list.Insert(tgt > src ? tgt - 1 : tgt, item);
                        actions = list.ToArray();
                        _collapsedActions.Clear();
                        EditorUtility.SetDirty(dirtyObj);
                    }
                    _repaint();
                    Event.current.Use();
                }
            }

            // ── Add action button ──
            if (GUILayout.Button("+ Add Action"))
            {
                var newAction = new EnigmaActionData();
                actions = AddAction(actions, newAction);
                EditorUtility.SetDirty(dirtyObj);
                string capturedKey = $"actiontype_{newAction.GetHashCode()}";
                OpenActionTypePicker(ctrl, selected =>
                {
                    _pendingActionValues[capturedKey] = selected;
                    _repaint();
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ACTION BODY DRAWING
        // ════════════════════════════════════════════════════════════════════════

        private void DrawActionBody(UnityEngine.Object dirtyObj, EnigmaController ctrl, EnigmaActionData action)
        {
            switch (action.actionType)
            {
                case 0: // Toggle Object
                    action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                        "Target Object", action.targetObject, typeof(GameObject), true);
                    break;

                case 1: // Toggle / Apply Material
                    {
                        var prevRenderer = action.targetRenderer;
                        action.targetRenderer = (UnityEngine.Renderer)EditorGUILayout.ObjectField(
                            "Renderer", action.targetRenderer, typeof(UnityEngine.Renderer), true);

                        // Material slot popup — only when the renderer has more
                        // than one sub-material; otherwise the popup is just
                        // clutter (single-material renderers always slot 0).
                        var rendMats = action.targetRenderer != null
                            ? action.targetRenderer.sharedMaterials : null;
                        if (rendMats != null && rendMats.Length > 1)
                        {
                            action.materialIndex = DrawMaterialPopup(
                                "Material Slot", action.materialIndex, action.targetRenderer);
                        }
                        else if (rendMats != null && rendMats.Length == 1)
                        {
                            // Force slot 0 for single-material renderers so any
                            // stale value from a previous renderer can't leak
                            // through into the build pipeline.
                            action.materialIndex = 0;
                        }

                        action.targetMaterial = (Material)EditorGUILayout.ObjectField(
                            "Active Material", action.targetMaterial, typeof(Material), false);

                        // Auto-populate Default Material from the renderer's
                        // current material at the chosen slot when the field
                        // is null (struct default OR user-cleared). Triggered
                        // both when the renderer is freshly assigned and on
                        // any later repaint where the field is empty. Won't
                        // clobber a user-set value because the gate is
                        // null-only.
                        if (action.defaultMaterial == null && action.targetRenderer != null)
                        {
                            var sm = action.targetRenderer.sharedMaterials;
                            int mi = action.materialIndex;
                            if (sm != null && mi >= 0 && mi < sm.Length && sm[mi] != null)
                                action.defaultMaterial = sm[mi];
                        }

                        action.defaultMaterial = (Material)EditorGUILayout.ObjectField(
                            "Default Material", action.defaultMaterial, typeof(Material), false);

                        if (action.category == 0)
                        {
                            // Toggle Material — describe the active/default split so
                            // users know what each field does at runtime.
                            EditorGUILayout.HelpBox(
                                "Active Material is applied when the button activates. " +
                                "Default Material is applied when the button deactivates " +
                                "(auto-filled from the renderer's current material at this slot " +
                                "when first assigned).\n\n" +
                                "On scene start, buttons with \"Default On\" apply Active; " +
                                "all others apply Default.",
                                MessageType.None);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "Apply Material is a one-shot swap: pressing the button writes " +
                                "Active Material to the renderer slot. Default Material is only " +
                                "used by Toggle-category Material actions; you can leave it empty here.",
                                MessageType.None);
                        }

                        // Suppress "value unused" warning for prevRenderer in
                        // single-material branch — we keep the local for
                        // future change detection if the auto-populate rule
                        // ever needs to differentiate "renderer was just
                        // changed" from "renderer was already set."
                        _ = prevRenderer;
                    }
                    break;

                case 2: // Set Shader Property (+ optional step)
                    action.targetRenderer = (UnityEngine.Renderer)EditorGUILayout.ObjectField(
                        "Renderer", action.targetRenderer, typeof(UnityEngine.Renderer), true);
                    action.materialIndex = DrawMaterialPopup("Material", action.materialIndex, action.targetRenderer);
                    // dirtyObj doubles as the Undo.RecordObject target for the
                    // Search-popup's Repaint-time pending-value consumption.
                    // Without it, picking a property from Search on a prefab
                    // instance is not registered as an override and reverts on
                    // scene save / play-mode enter. See EnigmaControllerEditor
                    // .Folders.cs:109 for the event-gated outer hook that
                    // covers direct text-field edits (MouseDown/KeyDown) but
                    // can't cover the async Search → Repaint path.
                    action.propertyName  = DrawPropertyNameField("Property Name", action.propertyName,
                        action.targetRenderer, action.materialIndex, action,
                        includeFilter: null, undoTarget: dirtyObj, undoLabel: "Set Shader Property Name");
                    {
                        // Auto-resolve property type from the material whenever possible.
                        int autoType = ConsumeAutoPropertyType();
                        if (autoType < 0)
                            autoType = ResolveShaderPropertyType(action.targetRenderer, action.materialIndex, action.propertyName);
                        if (autoType >= 0)
                            action.propertyType = autoType;
                        else if (action.targetRenderer != null && !string.IsNullOrEmpty(action.propertyName))
                            EditorGUILayout.HelpBox(
                                $"Property \"{action.propertyName}\" not found on this material.",
                                MessageType.Warning);
                    }
                    if (action.propertyType == 3)
                    {
                        action.targetTexture = (Texture)EditorGUILayout.ObjectField(
                            "Texture", action.targetTexture, typeof(Texture), false);
                        EnigmaShaderHelper.DrawTextureShaderUseWarnings(
                            action.targetTexture, action.propertyName);
                    }
                    else
                    {
                        // Use Step is Set-only. On Toggle actions, stepping would require
                        // the button to stay active indefinitely (since HandleStep never
                        // deactivates) — that contradicts the word "Toggle" and monopolizes
                        // dynamic fader slots. Multi-value cycling belongs on Set+Step
                        // (Momentary step button). Single-press offsets on a Toggle are
                        // already expressible via Value = default+offset, Default = default.
                        if (action.category == 1)
                        {
                            action.useStep = EditorGUILayout.Toggle("Use Step", action.useStep);
                        }
                        bool showStepFields = action.useStep && action.category == 1;
                        if (showStepFields)
                        {
                            EditorGUI.indentLevel++;
                            action.stepAmount = EditorGUILayout.FloatField("Step Amount", action.stepAmount);
                            if (action.stepAmount == 0f)
                            {
                                EditorGUILayout.HelpBox("Step Amount is 0 — the value will never change.", MessageType.Warning);
                            }
                            else
                            {
                                if (action.stepWrap || action.stepAmount < 0)
                                    action.stepMin = EditorGUILayout.FloatField("Minimum", action.stepMin);
                                if (action.stepWrap || action.stepAmount > 0)
                                    action.stepMax = EditorGUILayout.FloatField("Maximum", action.stepMax);
                                action.stepWrap = EditorGUILayout.Toggle(
                                    new GUIContent("Wrap", "When enabled, stepping past the maximum wraps to the minimum (and vice versa)."),
                                    action.stepWrap);
                            }
                            EditorGUI.indentLevel--;
                        }
                        else if (action.propertyType == 0)
                        {
                            // Drag-deferred — see DragDeferredFloatField docs.
                            var undoTgt = ResolveActionUndoTarget(dirtyObj, ctrl);
                            DragDeferredFloatField("Value", action, 1,
                                () => action.propertyFloatValue,
                                v => action.propertyFloatValue = v,
                                undoTgt, "Modify Action Value");
                        }
                        else if (action.propertyType == 1)
                            action.propertyColorValue = EditorGUILayout.ColorField("Value", action.propertyColorValue);
                        else
                            action.propertyVectorValue = EditorGUILayout.Vector4Field("Value", action.propertyVectorValue);

                        // Default value — written when the entry deactivates or on init.
                        // Shown for BOTH Toggle and Set now (runtime already reads it for
                        // both at EnigmaExecutor.cs:251-253 — on Toggle it's the off value,
                        // on Set it's the reset-to value). Previously hidden on Toggle even
                        // though the runtime consumed it, giving Toggle actions an implicit
                        // off-value of 0 that users couldn't edit.
                        if (action.propertyType == 0)
                        {
                            var undoTgt = ResolveActionUndoTarget(dirtyObj, ctrl);
                            DragDeferredFloatField("Default", action, 2,
                                () => action.defaultFloatValue,
                                v => action.defaultFloatValue = v,
                                undoTgt, "Modify Action Default");
                        }
                        else if (action.propertyType == 1)
                            action.defaultColorValue = EditorGUILayout.ColorField("Default", action.defaultColorValue);
                        else if (action.propertyType == 2)
                            action.defaultVectorValue = EditorGUILayout.Vector4Field("Default", action.defaultVectorValue);
                    }

                    // ── Auto-toggle helper ──
                    // When the action's property has an associated section toggle in the
                    // shader (e.g. _Invert sits inside a [Enum(Off,...)] _FilterModel
                    // section in Mochie), offer a checkbox that emits a synthetic second
                    // action at build time setting the toggle to 1. Hidden when no
                    // section toggle is detected, when the action's property already IS
                    // the section toggle, or when the renderer/material isn't resolved.
                    {
                        Material autoMat = null;
                        if (action.targetRenderer != null)
                        {
                            var mats = action.targetRenderer.sharedMaterials;
                            if (mats != null && action.materialIndex >= 0
                                && action.materialIndex < mats.Length)
                                autoMat = mats[action.materialIndex];
                        }
                        if (autoMat != null && !string.IsNullOrEmpty(action.propertyName)
                            && EnigmaShaderHelper.TryGetEffectToggle(
                                autoMat, action.propertyName, out string togProp))
                        {
                            action.alsoSetEffectToggle = EditorGUILayout.Toggle(
                                new GUIContent("Also Set Effect Toggle",
                                    $"When enabled, the build pipeline also sets {togProp} = 1 " +
                                    "alongside this action so the effect actually turns on. " +
                                    "Uncheck for buttons that change effect parameters without " +
                                    "enabling the effect itself (e.g. Outline power-level buttons)."),
                                action.alsoSetEffectToggle);
                            if (action.alsoSetEffectToggle)
                                EditorGUILayout.LabelField(" ",
                                    $"→ Will also set: {togProp} = 1",
                                    EditorStyles.miniLabel);
                        }
                    }
                    break;

                case 27: // Shader Keyword (Toggle / Set)
                    action.targetRenderer = (UnityEngine.Renderer)EditorGUILayout.ObjectField(
                        "Renderer", action.targetRenderer, typeof(UnityEngine.Renderer), true);
                    action.materialIndex = DrawMaterialPopup("Material", action.materialIndex, action.targetRenderer);
                    action.propertyName  = DrawKeywordField("Keyword", action.propertyName, action.targetRenderer, action.materialIndex);
                    if (action.category == 1) // Set mode — let user choose enable or disable
                        action.commandTargetState = EditorGUILayout.Toggle("Enable", action.commandTargetState);
                    break;

                case 4:  // Apply Skybox
                case 22: // Toggle Skybox
                    action.targetMaterial = (Material)EditorGUILayout.ObjectField(
                        "Skybox Material", action.targetMaterial, typeof(Material), false);
                    break;

                case 5: // Trigger Udon Event
                {
                    action.targetUdon = (UdonSharp.UdonSharpBehaviour)EditorGUILayout.ObjectField(
                        "UdonBehaviour", action.targetUdon, typeof(UdonSharp.UdonSharpBehaviour), true);

                    // Event name with search button
                    EditorGUILayout.BeginHorizontal();
                    action.udonEventName = EditorGUILayout.TextField("Event Name", action.udonEventName);
                    using (new EditorGUI.DisabledScope(action.targetUdon == null))
                    {
                        if (GUILayout.Button("…", GUILayout.Width(24)) && action.targetUdon != null)
                        {
                            var search = new EnigmaPropertySearch("Udon Events");
                            var group = search.GetMainGroup();
                            var targetType = action.targetUdon.GetType();
                            var methods = targetType.GetMethods(
                                System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.DeclaredOnly);
                            foreach (var m in methods)
                            {
                                if (m.GetParameters().Length == 0 && !m.IsSpecialName
                                    && m.ReturnType == typeof(void))
                                    group.Add(m.Name, m.Name);
                            }
                            string capturedKey = $"udonevt_{action.GetHashCode()}";
                            search.Open(selected =>
                            {
                                _pendingPropertyValues[capturedKey] = selected;
                                _repaint();
                            });
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Apply pending search result
                    string udonEvtKey = $"udonevt_{action.GetHashCode()}";
                    if (_pendingPropertyValues.TryGetValue(udonEvtKey, out string pendingEvt))
                    {
                        _pendingPropertyValues.Remove(udonEvtKey);
                        action.udonEventName = pendingEvt;
                    }

                    // Network scope dropdown
                    string[] scopeLabels = { "All Players", "Owner", "Local" };
                    action.udonEventScope = EditorGUILayout.Popup("Network Scope",
                        Mathf.Clamp(action.udonEventScope, 0, 2), scopeLabels);
                    if (action.udonEventScope == 1)
                        EditorGUILayout.HelpBox(
                            "Broadcasts the event over the network, but only the owner of the " +
                            "target UdonBehaviour's GameObject executes it. For static world " +
                            "objects, this is typically the instance master.",
                            MessageType.None);
                    break;
                }

                case 6: // Set Udon Variable (+ optional step)
                {
                    action.targetUdon = (UdonSharp.UdonSharpBehaviour)EditorGUILayout.ObjectField(
                        "UdonBehaviour", action.targetUdon, typeof(UdonSharp.UdonSharpBehaviour), true);

                    // Variable name with search button
                    EditorGUILayout.BeginHorizontal();
                    action.udonVariableName = EditorGUILayout.TextField("Variable Name", action.udonVariableName);
                    using (new EditorGUI.DisabledScope(action.targetUdon == null))
                    {
                        if (GUILayout.Button("Search", GUILayout.Width(60)) && action.targetUdon != null)
                        {
                            var search = new EnigmaPropertySearch("Udon Variables");
                            var group = search.GetMainGroup();
                            var targetType = action.targetUdon.GetType();
                            var fields = targetType.GetFields(
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            var typeMap = new Dictionary<string, int>();
                            foreach (var field in fields)
                            {
                                if (field.DeclaringType != targetType) continue;
                                int vType = -1;
                                if (field.FieldType == typeof(bool)) vType = 0;
                                else if (field.FieldType == typeof(float)) vType = 1;
                                else if (field.FieldType == typeof(int)) vType = 2;
                                else if (field.FieldType == typeof(string)) vType = 3;
                                if (vType < 0) continue;
                                group.Add($"{field.Name}  ({field.FieldType.Name})", field.Name);
                                typeMap[field.Name] = vType;
                            }
                            string capturedKey = $"udonvar_{action.GetHashCode()}";
                            search.Open(selected =>
                            {
                                _pendingPropertyValues[capturedKey] = selected;
                                if (typeMap.TryGetValue(selected, out int selType))
                                    _pendingPropertyTypes[capturedKey] = selType;
                                _repaint();
                            });
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Apply pending search result
                    string udonVarKey = $"udonvar_{action.GetHashCode()}";
                    if (_pendingPropertyValues.TryGetValue(udonVarKey, out string pendingVar))
                    {
                        _pendingPropertyValues.Remove(udonVarKey);
                        action.udonVariableName = pendingVar;
                    }
                    if (_pendingPropertyTypes.TryGetValue(udonVarKey, out int pendingVarType))
                    {
                        _pendingPropertyTypes.Remove(udonVarKey);
                        action.udonVariableType = pendingVarType;
                    }
                    // Use Step is Set-only (same rationale as type 2 above).
                    if (action.category == 1)
                    {
                        action.useStep = EditorGUILayout.Toggle("Use Step", action.useStep);
                    }
                    bool showUdonStepFields = action.useStep && action.category == 1;
                    if (showUdonStepFields)
                    {
                        EditorGUI.indentLevel++;
                        action.stepAmount = EditorGUILayout.FloatField("Step Amount", action.stepAmount);
                        if (action.stepAmount == 0f)
                        {
                            EditorGUILayout.HelpBox("Step Amount is 0 — the value will never change.", MessageType.Warning);
                        }
                        else
                        {
                            if (action.stepWrap || action.stepAmount < 0)
                                action.stepMin = EditorGUILayout.FloatField("Minimum", action.stepMin);
                            if (action.stepWrap || action.stepAmount > 0)
                                action.stepMax = EditorGUILayout.FloatField("Maximum", action.stepMax);
                            action.stepWrap = EditorGUILayout.Toggle(
                                new GUIContent("Wrap", "When enabled, stepping past the maximum wraps to the minimum (and vice versa)."),
                                action.stepWrap);
                        }
                        EditorGUI.indentLevel--;
                    }
                    else
                    {
                        if (action.udonVariableType == 0)
                            action.propertyFloatValue = EditorGUILayout.Toggle("Value", action.propertyFloatValue > 0f) ? 1f : 0f;
                        else if (action.udonVariableType == 1)
                            action.propertyFloatValue = EditorGUILayout.FloatField("Value", action.propertyFloatValue);
                        else if (action.udonVariableType == 2)
                            action.propertyFloatValue = EditorGUILayout.IntField("Value", (int)action.propertyFloatValue);
                        else
                            action.udonVariableStringValue = EditorGUILayout.TextField("Value", action.udonVariableStringValue ?? "");
                    }

                    // Default value — written when the entry deactivates (runtime) or at
                    // scene init (ApplyActionsDefault). Shown for both Toggle and Set
                    // modes so users can configure the off-value explicitly. Skipped for
                    // bool (implicit on=true / off=false) and string (no default field on
                    // the data model yet — out of scope for this revision).
                    if (action.udonVariableType == 1)
                        action.defaultFloatValue = EditorGUILayout.FloatField("Default", action.defaultFloatValue);
                    else if (action.udonVariableType == 2)
                        action.defaultFloatValue = EditorGUILayout.IntField("Default", (int)action.defaultFloatValue);
                    break;
                }

                // case 7: Color Cycle — deprecated; no new type-7 actions are created.
                // Legacy type-7 data is still handled at runtime via HandleColorCycle.

                case 8: // Presets
                {
                    EnigmaFolderData[] presetFolders = ctrl != null
                        ? (ctrl.GetFolders() ?? new EnigmaFolderData[0])
                        : new EnigmaFolderData[0];

                    switch (action.presetRole)
                    {
                        case 0:
                            action.presetScope = EditorGUILayout.Popup("Preset Scope", action.presetScope,
                                new[] { "All Folders", "Selected Folders" });
                            if (action.presetScope == 1)
                            {
                                EditorGUI.indentLevel++;
                                EditorGUILayout.LabelField("Include Folders:", EditorStyles.miniLabel);
                                bool[] included = BuildFolderIncludedBools(action.presetIncludedFolderIndices, presetFolders.Length);
                                for (int fi = 0; fi < presetFolders.Length; fi++)
                                    included[fi] = EditorGUILayout.Toggle($"  {fi + 1}. {presetFolders[fi].name}", included[fi]);
                                action.presetIncludedFolderIndices = BoolsToFolderIndices(included);
                                EditorGUI.indentLevel--;
                            }
                            action.presetIncludeFaders        = EditorGUILayout.Toggle("Include Faders",         action.presetIncludeFaders);
                            action.presetIncludeStepValues    = EditorGUILayout.Toggle("Include Step Values",    action.presetIncludeStepValues);
                            action.presetIncludeColorPalettes = EditorGUILayout.Toggle("Include Color Palettes", action.presetIncludeColorPalettes);
                            action.presetIncludeVariantGroups = EditorGUILayout.Toggle("Include Variant Groups", action.presetIncludeVariantGroups);
                            break;
                        case 1:
                            EditorGUILayout.HelpBox(
                                "When pressed, saves all preset slots to PlayerData for persistence across sessions.",
                                MessageType.None);
                            break;
                        case 2:
                            EditorGUILayout.HelpBox("When pressed, loads all preset slots from PlayerData.", MessageType.None);
                            break;
                        case 3:
                            EditorGUILayout.HelpBox(
                                "When toggled on, the next preset slot you press will be cleared instead of loaded.",
                                MessageType.None);
                            break;
                    }
                    break;
                }

                case 9: // Display Value
                {
                    bool isShaderPropertyDisplay = action.target == 4;

                    if (isShaderPropertyDisplay)
                    {
                        EditorGUILayout.HelpBox(
                            "Displays a live value on a second line of the button label.\n" +
                            "Assign a Renderer (shader property) as the source.",
                            MessageType.None);
                        action.targetRenderer = (Renderer)EditorGUILayout.ObjectField(
                            "Source", action.targetRenderer, typeof(Renderer), true);
                    }
                    else // Udon Variable (target == 6)
                    {
                        EditorGUILayout.HelpBox(
                            "Displays a live value on a second line of the button label.\n" +
                            "Assign a UdonBehaviour (variable) as the source.",
                            MessageType.None);
                        action.targetUdon = (UdonSharp.UdonSharpBehaviour)EditorGUILayout.ObjectField(
                            "Source", action.targetUdon, typeof(UdonSharp.UdonSharpBehaviour), true);
                    }

                    if (action.targetRenderer != null)
                    {
                        action.materialIndex = DrawMaterialPopup("Material", action.materialIndex, action.targetRenderer);

                        // Route through the shared DrawPropertyNameField so the
                        // Display Value action gets the same tree grouping
                        // ([Header]/section toggles, Thry-style sub-groups), the
                        // same "Search" button label (width 60), and the same
                        // actionRef-keyed pending-value dispatch as every other
                        // property search site (Set Shader Property, fader
                        // links, Color Selector, entry condition color). Pre-
                        // this unification, Display Value built a flat tree
                        // inline with a "…" button and a shared
                        // dv_<renderer>_<mat> pending key that cross-talked
                        // between two Display Value actions on the same
                        // renderer+material.
                        //
                        // The includeFilter restricts the tree to scalar
                        // property types — Display Value formats the value as
                        // button-label text, so Color/Vector/Texture properties
                        // make no sense in this search. Float and Range are
                        // the obvious ones; Int is included on Unity 2021.1+
                        // for shaders that use the new integer property type.
                        bool dvHasMaterial = action.targetRenderer.sharedMaterials != null
                            && action.materialIndex >= 0
                            && action.materialIndex < action.targetRenderer.sharedMaterials.Length
                            && action.targetRenderer.sharedMaterials[action.materialIndex] != null;

                        System.Predicate<UnityEngine.Rendering.ShaderPropertyType> dvScalarOnly = t =>
                               t == UnityEngine.Rendering.ShaderPropertyType.Float
                            || t == UnityEngine.Rendering.ShaderPropertyType.Range
#if UNITY_2021_1_OR_NEWER
                            || t == UnityEngine.Rendering.ShaderPropertyType.Int
#endif
                            ;

                        // undoTarget: dirtyObj — same Repaint-time prefab-override
                        // rationale as the Set Shader Property case above.
                        action.propertyName = DrawPropertyNameField("Property Name", action.propertyName,
                            action.targetRenderer, action.materialIndex, action, dvScalarOnly,
                            undoTarget: dirtyObj, undoLabel: "Set Display Value Property");

                        // Pick up the property type that DrawPropertyNameField
                        // stashed in _lastAutoPropertyType when the user made
                        // a Search selection, so the action persists the
                        // Float/Range/Int type index for runtime formatting.
                        int dvAutoType = ConsumeAutoPropertyType();
                        if (dvAutoType >= 0)
                            action.propertyType = dvAutoType;

                        // DisabledScope is handled inside DrawPropertyNameField
                        // via the hasRenderer check; no extra guard needed here.
                        _ = dvHasMaterial; // (retained for future validation hooks)
                    }
                    else if (action.targetUdon != null)
                    {
                        if (string.IsNullOrEmpty(action.propertyName) && !string.IsNullOrEmpty(action.udonVariableName))
                            action.propertyName = action.udonVariableName;

                        action.propertyName = EditorGUILayout.TextField("Variable Name", action.propertyName);
                    }

                    break;
                }

                case 10: // Color Selector
                    // colorSelectorRole is set authoritatively by SyncActionType when
                    // the user picks the action from the picker (Display Color Palette
                    // → 0, Set Color → 1, Next/Previous Color → 2). The dropdown that
                    // used to live here let users desync the role from the picked
                    // action type, which was a UX wart — the action type IS the role.
                    // Removed; role is now read-only from the perspective of the
                    // action body and only changes if the user re-picks via the
                    // picker (which routes back through SyncActionType).
                    action.colorGroupName = EditorGUILayout.TextField("Color Palette Name", action.colorGroupName);
                    if (action.colorSelectorRole == 2)
                    {
                        bool colorIsBackward = action.propertyType == 1;
                        EditorGUILayout.LabelField("Direction",
                            colorIsBackward ? "← Previous" : "→ Next", EditorStyles.miniLabel);
                        EditorGUILayout.HelpBox(
                            $"On press, cycles the pending color {(colorIsBackward ? "backward (previous)" : "forward (next)")} on the linked Set Color entry.",
                            MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            action.colorSelectorRole == 0
                                ? "Displays the currently applied color as the button tint. No action on press.\nSet Color Palette Name to match the Set Color entry in the same folder."
                                : "Owns the color palette. Shows the pending (preview) color as tint. On press, applies the pending color to the target renderer.",
                            MessageType.None);
                    }
                    if (action.colorSelectorRole == 1)
                    {
                        action.colorTargetRenderer = (UnityEngine.Renderer)EditorGUILayout.ObjectField(
                            "Target Renderer", action.colorTargetRenderer, typeof(UnityEngine.Renderer), true);
                        action.colorMaterialIndex = DrawMaterialPopup("Material", action.colorMaterialIndex, action.colorTargetRenderer);
                        // Pass `action` as actionRef so the internal pending-value
                        // dictionary keys on this specific action rather than on the
                        // shared (renderer, matIndex, label, _drawIndex) tuple. Two
                        // Color Selector actions on different entries targeting the
                        // same renderer/material would otherwise cross-talk via the
                        // pending dictionary — a property chosen in action B's
                        // Search could land on action A's colorPropertyName on the
                        // next repaint.
                        // undoTarget: dirtyObj — same Repaint-time prefab-override
                        // rationale as the Set Shader Property case above.
                        action.colorPropertyName  = DrawPropertyNameField("Color Property",
                            action.colorPropertyName, action.colorTargetRenderer, action.colorMaterialIndex, action,
                            includeFilter: null, undoTarget: dirtyObj, undoLabel: "Set Color Property Name");

                        int colorSelectorPaletteSize = EditorGUILayout.IntField("Palette Size",
                            action.paletteColors != null ? action.paletteColors.Length : 0);
                        colorSelectorPaletteSize = Mathf.Max(0, colorSelectorPaletteSize);
                        if (action.paletteColors == null || action.paletteColors.Length != colorSelectorPaletteSize)
                        {
                            Color[] newPalette = new Color[colorSelectorPaletteSize];
                            if (action.paletteColors != null)
                                System.Array.Copy(action.paletteColors, newPalette,
                                    Mathf.Min(action.paletteColors.Length, colorSelectorPaletteSize));
                            action.paletteColors = newPalette;
                        }
                        for (int c = 0; c < action.paletteColors.Length; c++)
                            action.paletteColors[c] = EditorGUILayout.ColorField("Color " + (c + 1), action.paletteColors[c]);
                    }
                    break;

                case 12: // Transform
                {
                    action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                        "Target Object", action.targetObject, typeof(GameObject), true);
                    action.propertyType = EditorGUILayout.Popup("Mode", action.propertyType,
                        new[] { "Set Position", "Set Rotation", "Set Scale", "Add Position", "Add Rotation" });
                    bool isScale = action.propertyType == 2;
                    if (!isScale)
                        action.transformSpace = EditorGUILayout.Popup("Space", action.transformSpace,
                            new[] { "World", "Local" });
                    Vector3 vec3 = new Vector3(action.propertyVectorValue.x, action.propertyVectorValue.y, action.propertyVectorValue.z);
                    vec3 = EditorGUILayout.Vector3Field("Value", vec3);
                    action.propertyVectorValue = new Vector4(vec3.x, vec3.y, vec3.z, 0f);
                    break;
                }

                case 23: // Toggle Transform
                {
                    action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                        "Target Object", action.targetObject, typeof(GameObject), true);
                    action.propertyType = EditorGUILayout.Popup("Mode", action.propertyType,
                        new[] { "Set Position", "Set Rotation", "Set Scale", "Add Position", "Add Rotation" });
                    bool isScale23 = action.propertyType == 2;
                    if (!isScale23)
                        action.transformSpace = EditorGUILayout.Popup("Space", action.transformSpace,
                            new[] { "World", "Local" });
                    Vector3 vec23 = new Vector3(action.propertyVectorValue.x, action.propertyVectorValue.y, action.propertyVectorValue.z);
                    vec23 = EditorGUILayout.Vector3Field("Value", vec23);
                    action.propertyVectorValue = new Vector4(vec23.x, vec23.y, vec23.z, 0f);
                    EditorGUILayout.HelpBox(
                        "On activate: saves the current transform value then applies the one above.\n" +
                        "On deactivate: restores the saved value.", MessageType.None);
                    break;
                }

                case 13: // Teleport
                {
                    if (action.target == 0) // Teleport Object
                    {
                        action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                            "Object to Teleport", action.targetObject, typeof(GameObject), true);
                        int displayMode    = (action.propertyType == 4) ? 1 : 0;
                        int newDisplayMode = EditorGUILayout.Popup("Mode", displayMode,
                            new[] { "Teleport to Vector", "Teleport to Transform" });
                        action.propertyType = (newDisplayMode == 1) ? 4 : 3;
                        if (action.propertyType == 3)
                        {
                            Vector3 pos = new Vector3(action.propertyVectorValue.x, action.propertyVectorValue.y, action.propertyVectorValue.z);
                            pos = EditorGUILayout.Vector3Field("Position", pos);
                            action.propertyVectorValue = new Vector4(pos.x, pos.y, pos.z, 0f);
                        }
                        else if (action.propertyType == 4)
                        {
                            action.teleportDestination = (GameObject)EditorGUILayout.ObjectField(
                                "Destination Transform", action.teleportDestination, typeof(GameObject), true);
                        }
                    }
                    else // Teleport Player
                    {
                        action.propertyType = EditorGUILayout.Popup("Mode", action.propertyType,
                            new[] { "Respawn to Spawn Origin", "Teleport to Vector", "Teleport to Transform" });
                        if (action.propertyType == 1)
                        {
                            Vector3 pos = new Vector3(action.propertyVectorValue.x, action.propertyVectorValue.y, action.propertyVectorValue.z);
                            pos = EditorGUILayout.Vector3Field("Position", pos);
                            action.propertyVectorValue = new Vector4(pos.x, pos.y, pos.z, 0f);
                            action.teleportRotationEuler = EditorGUILayout.Vector3Field("Rotation (Euler)", action.teleportRotationEuler);
                        }
                        else if (action.propertyType == 2)
                        {
                            action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                                "Target Transform", action.targetObject, typeof(GameObject), true);
                        }
                    }
                    break;
                }

                case 14: // Autochange Group
                    action.autoChangeGroupName      = EditorGUILayout.TextField("Group Name", action.autoChangeGroupName);
                    action.autoChangeGroupInterval  = EditorGUILayout.FloatField("Interval (seconds)", action.autoChangeGroupInterval);
                    action.autoChangeGroupRandom    = EditorGUILayout.Toggle("Random", action.autoChangeGroupRandom);
                    EditorGUILayout.HelpBox(
                        "Toggles autochanging on/off for the named group. To start the cycle " +
                        "at world load, set this button's \"Default On\" option.",
                        MessageType.None);
                    break;

                case 15: // Command: Set Object State
                    action.targetObject = (GameObject)EditorGUILayout.ObjectField(
                        "Target Object", action.targetObject, typeof(GameObject), true);
                    action.commandTargetState = EditorGUILayout.Toggle("Target State (On)", action.commandTargetState);
                    EditorGUILayout.HelpBox(
                        "Forces the GameObject's active state to the specified value on every press.",
                        MessageType.None);
                    break;

                case 17: // Command: Set Autochange Group State
                    action.autoChangeGroupName     = EditorGUILayout.TextField("Group Name", action.autoChangeGroupName);
                    action.autoChangeGroupInterval = EditorGUILayout.FloatField("Interval (seconds)", action.autoChangeGroupInterval);
                    action.autoChangeGroupRandom   = EditorGUILayout.Toggle("Random", action.autoChangeGroupRandom);
                    action.commandTargetState      = EditorGUILayout.Toggle("Target State (Active)", action.commandTargetState);
                    EditorGUILayout.HelpBox("Forces the named autochange group on or off unconditionally on every press.", MessageType.None);
                    break;

                case 18: // Command: Set Whitelist
                    action.commandTargetState = EditorGUILayout.Toggle("Whitelist Enabled", action.commandTargetState);
                    EditorGUILayout.HelpBox("Forces the controller's whitelist on or off on every press.", MessageType.None);
                    if (ctrl != null && !ctrl.whitelistEnabled)
                    {
                        // The controller's InitializeWhitelist() bails early when
                        // whitelistEnabled is false at Start (), so its username
                        // list never loads. Flipping the bool to true at runtime
                        // through this action will pass the CanLocalUserInteract
                        // gate but the underlying user list isn't initialized,
                        // making the action a no-op at best.
                        EditorGUILayout.HelpBox(
                            "Whitelist is currently disabled on this controller. Enable " +
                            "\"Whitelist\" on the EnigmaController inspector for this action " +
                            "to function — the username list won't initialize otherwise.",
                            MessageType.Warning);
                    }
                    break;

                case 28: // Toggle: Toggle Whitelist
                    {
                        // Single bool field "Default" backed by defaultFloatValue
                        // (>= 0.5 = ON, < 0.5 = OFF). Active state is the inverse,
                        // computed by the runtime executor — no separate "active"
                        // field is needed for a binary on/off concept.
                        bool defOn = action.defaultFloatValue >= 0.5f;
                        bool newDefOn = EditorGUILayout.Toggle("Default", defOn);
                        if (newDefOn != defOn)
                            action.defaultFloatValue = newDefOn ? 1f : 0f;

                        EditorGUILayout.HelpBox(
                            "Press toggles the controller's whitelist between this default " +
                            "state and its inverse. Default is the value restored when the " +
                            "button deactivates.\n\n" +
                            "Default is NOT applied at scene start — the controller's own " +
                            "\"Whitelist\" toggle in the EnigmaController inspector " +
                            "determines the initial state. This action only writes whitelist " +
                            "state in response to runtime button presses.",
                            MessageType.None);

                        if (ctrl != null && !ctrl.whitelistEnabled)
                        {
                            // Same rationale as case 18 — InitializeWhitelist
                            // bails early on a false initial value, so toggling
                            // at runtime can't make the username list materialize.
                            EditorGUILayout.HelpBox(
                                "Whitelist is currently disabled on this controller. Enable " +
                                "\"Whitelist\" on the EnigmaController inspector for this action " +
                                "to function — the username list won't initialize otherwise.",
                                MessageType.Warning);
                        }
                    }
                    break;

                case 19: // Variant Selector
                    if (action.variantSelectorRole == 1)
                    {
                        action.variantGroupName = EditorGUILayout.TextField("Variant Group Name", action.variantGroupName);
                        EditorGUILayout.HelpBox(
                            "Variant Group Name is the shared tag that Variant Display and Change Variant entries reference.\n" +
                            "On press, applies the pending variant to the target material property.",
                            MessageType.None);
                        EditorGUILayout.Space(4);
                        action.targetRenderer = (Renderer)EditorGUILayout.ObjectField(
                            "Target Renderer", action.targetRenderer, typeof(Renderer), true);
                        action.materialIndex = DrawMaterialPopup("Material", action.materialIndex, action.targetRenderer);
                        action.propertyName  = EditorGUILayout.TextField("Shader Property", action.propertyName);
                        action.propertyType  = EditorGUILayout.Popup("Property Type", action.propertyType,
                            new[] { "Float", "Color", "Vector", "Texture" });
                        EditorGUILayout.Space(4);

                        if (action.variantItems == null) action.variantItems = new EnigmaVariantItem[0];
                        int count = action.variantItems.Length;
                        EditorGUILayout.LabelField($"Variants ({count})", EditorStyles.boldLabel);
                        for (int vi = 0; vi < count; vi++)
                        {
                            var item = action.variantItems[vi];
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            item.variantName = EditorGUILayout.TextField("Name", item.variantName);
                            if (action.propertyType == 0)
                                item.floatValue  = EditorGUILayout.FloatField("Float Value", item.floatValue);
                            else if (action.propertyType == 1)
                                item.colorValue  = EditorGUILayout.ColorField("Color Value", item.colorValue);
                            else if (action.propertyType == 2)
                                item.vectorValue = EditorGUILayout.Vector4Field("Vector Value", item.vectorValue);
                            else if (action.propertyType == 3)
                            {
                                item.textureValue = (Texture)EditorGUILayout.ObjectField(
                                    "Texture", item.textureValue, typeof(Texture), false);
                                EnigmaShaderHelper.DrawTextureShaderUseWarnings(
                                    item.textureValue, action.propertyName);
                            }

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                            {
                                var vlist = new List<EnigmaVariantItem>(action.variantItems);
                                vlist.RemoveAt(vi);
                                action.variantItems = vlist.ToArray();
                                EditorUtility.SetDirty(dirtyObj);
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.EndVertical();
                                break;
                            }
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.EndVertical();
                        }
                        if (GUILayout.Button("+ Add Variant"))
                        {
                            var vlist = new List<EnigmaVariantItem>(action.variantItems);
                            vlist.Add(new EnigmaVariantItem { variantName = $"Variant {vlist.Count + 1}" });
                            action.variantItems = vlist.ToArray();
                            EditorUtility.SetDirty(dirtyObj);
                        }
                    }
                    else
                    {
                        action.variantGroupName = EditorGUILayout.TextField("Variant Group Name", action.variantGroupName);
                        if (action.variantSelectorRole == 2)
                        {
                            bool variantIsBackward = action.propertyType == 1;
                            EditorGUILayout.LabelField("Direction",
                                variantIsBackward ? "← Previous" : "→ Next", EditorStyles.miniLabel);
                            EditorGUILayout.HelpBox(
                                $"On press, cycles the pending variant {(variantIsBackward ? "backward (previous)" : "forward (next)")} on the linked Set Variant entry.",
                                MessageType.None);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "Displays the name of the currently applied variant on the button label.\n" +
                                "Set Variant Group Name to match the Set Variant entry in this folder.",
                                MessageType.None);
                        }
                    }
                    break;

                case 20: // Nav embedded
                {
                    string[] navOpLabels = {
                        "Next Folder", "Previous Folder", "Go To Folder",
                        "Next Page", "Previous Page", "Go To Page",
                        "Reset", "Set Fader Mode",
                        "\u2014", "\u2014",
                        "Next Fader Page", "Previous Fader Page", "Go To Fader Page"
                    };
                    int navOpClamped = Mathf.Clamp(action.propertyType, 0, navOpLabels.Length - 1);
                    EditorGUILayout.LabelField("System", navOpLabels[navOpClamped], EditorStyles.miniLabel);

                    if (action.propertyType == 2) // GoToFolder
                    {
                        EnigmaFolderData[] navFolders = ctrl != null
                            ? (ctrl.GetFolders() ?? new EnigmaFolderData[0])
                            : new EnigmaFolderData[0];
                        if (navFolders.Length > 0)
                        {
                            string[] navFolderNames = EnigmaControllerEditor.BuildUniqueFolderNames(navFolders);
                            action.navFolderTarget = Mathf.Clamp(action.navFolderTarget, 0, navFolders.Length - 1);
                            action.navFolderTarget = EditorGUILayout.Popup("Target Folder", action.navFolderTarget, navFolderNames);
                        }
                        else
                        {
                            action.navFolderTarget = EditorGUILayout.IntField("Folder Index", action.navFolderTarget);
                            EditorGUILayout.HelpBox("Jumps directly to the folder at the specified index (0-based).", MessageType.None);
                        }
                    }
                    else if (action.propertyType == 5) // GoToPage
                    {
                        action.navPageTarget = EditorGUILayout.IntField("Page Index", action.navPageTarget);
                        EditorGUILayout.HelpBox("Jumps directly to the specified page (0-based) within the current folder.", MessageType.None);
                    }
                    else if (action.propertyType == 12) // GoToFaderPage
                    {
                        action.navFaderPageTarget = EditorGUILayout.IntField("Fader Page Index", action.navFaderPageTarget);
                        EditorGUILayout.HelpBox("Jumps directly to the specified fader page (0-based).", MessageType.None);
                    }
                    break;
                }

                case 21: // Display Stat
                {
                    EditorGUILayout.HelpBox(
                        "Displays a live VRChat world statistic on the button label.\n" +
                        "Local stats (Players, Time, Age, etc.) update every frame.\n" +
                        "API stats (Visits, Favorites, etc.) require a World ID.",
                        MessageType.None);

                    string[] statMetricOptions = {
                        "Visits", "Favorites", "Occupancy", "Popularity",
                        "Heat", "Players", "Age", "Time",
                        "VR Users", "Desktop Users", "Capacity", "Peak Players",
                        "Instance Master", "Authenticated"
                    };
                    int clamped = Mathf.Clamp(action.statMetric, 0, statMetricOptions.Length - 1);
                    int chosen  = EditorGUILayout.Popup("Metric", clamped, statMetricOptions);
                    if (chosen != action.statMetric)
                        action.statMetric = chosen;

                    bool needsApi = action.statMetric == 0 || action.statMetric == 1 || action.statMetric == 2
                                 || action.statMetric == 3 || action.statMetric == 4 || action.statMetric == 10;
                    if (needsApi && ctrl != null)
                    {
                        bool hasUrl = ctrl.worldStatsBuiltApiUrl != null
                            && !string.IsNullOrEmpty(ctrl.worldStatsBuiltApiUrl.Get());
                        if (!hasUrl)
                        {
                            // Auto-detect world ID from the scene's PipelineManager if field is empty.
                            if (string.IsNullOrEmpty(ctrl.worldStatsWorldId))
                            {
                                string detected = DetectWorldIdFromScene();
                                if (!string.IsNullOrEmpty(detected))
                                {
                                    Undo.RecordObject(ctrl, "Auto-detect World ID");
                                    ctrl.worldStatsWorldId = detected;
                                    ctrl.EditorBuildWorldStatsApiUrl();
                                    EditorUtility.SetDirty(ctrl);
                                }
                            }

                            // Re-check after auto-detect attempt.
                            hasUrl = ctrl.worldStatsBuiltApiUrl != null
                                && !string.IsNullOrEmpty(ctrl.worldStatsBuiltApiUrl.Get());
                        }

                        if (!hasUrl)
                        {
                            EditorGUILayout.HelpBox(
                                "This metric requires the VRChat API. Enter your World ID below.",
                                MessageType.Warning);

                            EditorGUILayout.BeginHorizontal();
                            string prevId = ctrl.worldStatsWorldId ?? "";
                            string newId = EditorGUILayout.TextField("World ID", prevId);
                            if (newId != prevId)
                            {
                                Undo.RecordObject(ctrl, "Set World ID");
                                ctrl.worldStatsWorldId = newId;
                                EditorUtility.SetDirty(ctrl);
                            }
                            if (GUILayout.Button("Build URL", GUILayout.Width(80)))
                            {
                                Undo.RecordObject(ctrl, "Build World Stats URL");
                                ctrl.EditorBuildWorldStatsApiUrl();
                                EditorUtility.SetDirty(ctrl);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    break;
                }

                case 24: // Display Folder Name
                {
                    EditorGUILayout.HelpBox(
                        "Displays the name of the currently active folder on this button's label.\n" +
                        "The label updates automatically whenever the active folder changes.",
                        MessageType.None);
                    break;
                }

                case 25: // Display Page Number
                {
                    EditorGUILayout.HelpBox(
                        "Displays the current page indicator (e.g. \"1 / 3\") on this button's label.\n" +
                        "The label updates automatically whenever the page or folder changes.",
                        MessageType.None);
                    break;
                }

                case 26: // Screen Shader
                {
                    EditorGUILayout.HelpBox(
                        "Assigns a screen shader material to a template mesh in the scene.\n" +
                        "The build step duplicates the template and assigns the material.\n" +
                        "Entries sharing the same template are auto-exclusive (one active at a time).",
                        MessageType.None);

                    action.targetMaterial = (Material)EditorGUILayout.ObjectField(
                        "Shader Material", action.targetMaterial, typeof(Material), false);

                    // Template dropdown
                    var templates = EnigmaShaderTemplate.FindAllInScene();
                    var labels    = EnigmaShaderTemplate.GetTemplateLabels(templates);
                    int popupIdx  = EnigmaShaderTemplate.GetPopupIndex(templates, action.shaderTemplateIndex);

                    EditorGUILayout.BeginHorizontal();
                    int newIdx = EditorGUILayout.Popup("Template", popupIdx, labels);
                    if (newIdx != popupIdx && templates.Length > 0)
                        action.shaderTemplateIndex = templates[newIdx].templateNumber;

                    if (GUILayout.Button("New", GUILayout.Width(42)))
                    {
                        var created = EnigmaShaderTemplate.CreateNewTemplate();
                        if (created != null)
                            action.shaderTemplateIndex = created.templateNumber;
                    }
                    EditorGUILayout.EndHorizontal();

                    break;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PROPERTY / COMPONENT PICKER HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws a material dropdown for the given renderer, showing material names.
        /// Defaults to index 0 if the current index is out of range.
        /// Returns the (possibly updated) material index.
        /// </summary>
        public static int DrawMaterialPopup(string label, int matIndex, UnityEngine.Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                return matIndex;

            var mats = renderer.sharedMaterials;
            if (matIndex < 0 || matIndex >= mats.Length)
                matIndex = 0;

            string[] matNames = new string[mats.Length];
            for (int mi = 0; mi < mats.Length; mi++)
                matNames[mi] = mats[mi] != null ? mats[mi].name : $"Slot {mi} (None)";

            return EditorGUILayout.Popup(label, matIndex, matNames);
        }

        public string DrawPropertyNameField(string label, string current,
                                            UnityEngine.Renderer renderer, int matIndex,
                                            object actionRef = null,
                                            System.Predicate<UnityEngine.Rendering.ShaderPropertyType> includeFilter = null,
                                            UnityEngine.Object undoTarget = null,
                                            string undoLabel = null)
        {
            // Use the action object's identity hash to disambiguate multiple actions
            // in the same entry that share the same renderer, matIndex, and label.
            // This is stable across repaints as long as the action object isn't recreated.
            int actionId = actionRef != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(actionRef) : _drawIndex;
            string fieldKey = $"{(renderer != null ? renderer.GetInstanceID() : 0)}_{matIndex}_{label}_{actionId}";

            if (_pendingPropertyValues.TryGetValue(fieldKey, out string pending))
            {
                // A Search popup callback queued this pending value and
                // fired Repaint. The consumption below is the point where
                // the user's selection actually lands in the caller's
                // field — and crucially, it lands on a Repaint event, not
                // a MouseDown/MouseUp/KeyDown, so the event-gated
                // Undo.RecordObject in the outer inspector's top-of-frame
                // hook (EnigmaControllerEditor.Folders.IsMutationEvent)
                // doesn't cover it. On prefab instances, SetDirty alone
                // does not register a property override; without
                // Undo.RecordObject BEFORE the mutation the user's
                // selection reverts on scene save / play-mode enter.
                //
                // Record the undoTarget (typically EnigmaControllerData)
                // here, one frame only, at the exact moment the pending
                // value is about to be consumed. Safe on the Mochie-scale
                // 30k-field perf path because this branch executes exactly
                // once per Search selection, not once per inspector frame.
                if (undoTarget != null)
                {
                    UnityEditor.Undo.RecordObject(undoTarget,
                        undoLabel ?? $"Set {label}");
                }
                _pendingPropertyValues.Remove(fieldKey);
                current = pending;
                if (undoTarget != null)
                    UnityEditor.EditorUtility.SetDirty(undoTarget);
            }

            EditorGUILayout.BeginHorizontal();
            string result = EditorGUILayout.TextField(label, current);
            bool hasRenderer = renderer != null
                && renderer.sharedMaterials != null
                && matIndex >= 0
                && matIndex < renderer.sharedMaterials.Length
                && renderer.sharedMaterials[matIndex] != null;

            using (new EditorGUI.DisabledScope(!hasRenderer))
            {
                if (GUILayout.Button("Search", GUILayout.Width(60)))
                {
                    var search    = new EnigmaPropertySearch("Shader Properties");
                    var mat       = renderer.sharedMaterials[matIndex];
                    var shader    = mat.shader;
                    var mainGroup = search.GetMainGroup();

                    // Shared tree builder — uses the same attribute-walk
                    // grouping as the "Also Set Effect Toggle" checkbox, so
                    // the section a property is listed under here matches the
                    // toggle that will be auto-set when it's picked. The
                    // optional includeFilter lets callers like the Display
                    // Value action restrict which property types show up
                    // (e.g. Float/Range only — Color/Vector/Texture make no
                    // sense as button-label display text) while still
                    // getting the unified [Header] grouping and the "Search"
                    // button label instead of the old inline "…" variant.
                    var typeDict = PopulateShaderPropertyTree(mainGroup, shader, includeFilter);

                    string capturedKey = fieldKey;
                    search.Open(selected =>
                    {
                        _pendingPropertyValues[capturedKey] = selected;
                        if (typeDict.TryGetValue(selected, out var selectedType))
                            _pendingPropertyTypes[capturedKey] = ShaderPropertyTypeToActionType(selectedType);
                        _repaint();
                    });
                }
            }
            EditorGUILayout.EndHorizontal();

            // Auto-apply property type from search selection.
            if (_pendingPropertyTypes.TryGetValue(fieldKey, out int pendingType))
            {
                _pendingPropertyTypes.Remove(fieldKey);
                _lastAutoPropertyType = pendingType;
            }

            return result;
        }

        /// <summary>
        /// Draws a keyword field with a search picker that lists all local shader keywords
        /// from the selected material's shader.
        /// </summary>
        public string DrawKeywordField(string label, string current,
                                       UnityEngine.Renderer renderer, int matIndex)
        {
            string fieldKey = $"kw_{(renderer != null ? renderer.GetInstanceID() : 0)}_{matIndex}_{label}";

            if (_pendingPropertyValues.TryGetValue(fieldKey, out string pending))
            {
                _pendingPropertyValues.Remove(fieldKey);
                current = pending;
            }

            EditorGUILayout.BeginHorizontal();
            string result = EditorGUILayout.TextField(label, current);
            bool hasRenderer = renderer != null
                && renderer.sharedMaterials != null
                && matIndex >= 0
                && matIndex < renderer.sharedMaterials.Length
                && renderer.sharedMaterials[matIndex] != null;

            using (new EditorGUI.DisabledScope(!hasRenderer))
            {
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    var search = new EnigmaPropertySearch("Shader Keywords");
                    var mat    = renderer.sharedMaterials[matIndex];
                    var shader = mat.shader;
                    var group  = search.GetMainGroup();

                    var added = new HashSet<string>();
                    // Enumerate local keywords defined by the shader (Unity 2021.2+)
                    var localKeywords = shader.keywordSpace.keywords;
                    foreach (var kw in localKeywords)
                    {
                        string kwName = kw.name;
                        if (string.IsNullOrEmpty(kwName)) continue;
                        if (!added.Add(kwName)) continue;
                        bool enabled = mat.IsKeywordEnabled(kwName);
                        string suffix = enabled ? "  (enabled)" : "";
                        group.Add($"{kwName}{suffix}", kwName);
                    }

                    if (added.Count == 0)
                    {
                        // Fallback: show currently enabled keywords from the material
                        foreach (string kwName in mat.shaderKeywords)
                        {
                            if (string.IsNullOrEmpty(kwName) || !added.Add(kwName)) continue;
                            group.Add($"{kwName}  (enabled)", kwName);
                        }
                    }

                    string capturedKey = fieldKey;
                    search.Open(selected =>
                    {
                        _pendingPropertyValues[capturedKey] = selected;
                        _repaint();
                    });
                }
            }
            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// After calling DrawPropertyNameField, read this to get the auto-detected
        /// property type if a search selection just occurred. Returns -1 if none.
        /// </summary>
        private int _lastAutoPropertyType = -1;
        public int ConsumeAutoPropertyType()
        {
            int t = _lastAutoPropertyType;
            _lastAutoPropertyType = -1;
            return t;
        }

        /// <summary>
        /// Cache keyed by (shaderInstanceID, propertyName) → action property
        /// type index. Without this, case 2 of the action drawer (Set/Toggle
        /// Shader Property) called <see cref="ResolveShaderPropertyType"/>
        /// every repaint frame, doing a fresh O(N) scan of every shader
        /// property (~260 string comparisons for Mochie) for EACH action
        /// in the controller. On controllers with many shader-property
        /// actions, that added up to visible repaint lag — especially
        /// noticeable as a 2-3s hang when Unity re-rendered the editor
        /// window multiple times around a SearchWindow.Open call.
        ///
        /// <para>Cache grows only when properties are actually resolved;
        /// entries are never evicted during a session. Cleared via
        /// <see cref="ClearPropertyTypeCache"/> on shader reimport or when
        /// the user invalidates via the Enigma menu.</para>
        /// </summary>
        private static readonly Dictionary<(int shaderId, string propName), int>
            _propertyTypeCache = new Dictionary<(int, string), int>();

        /// <summary>
        /// Clears the per-session property-type resolution cache. Called by
        /// <see cref="EnigmaShaderHelper.ClearCache"/> so shader reimports
        /// and play-mode transitions produce fresh lookups.
        /// </summary>
        internal static void ClearPropertyTypeCache()
        {
            _propertyTypeCache.Clear();
        }

        /// <summary>
        /// Looks up a shader property by name on the renderer's material and returns
        /// its action property type (0=Float, 1=Color, 2=Vector, 3=Texture). Returns -1 if not found.
        /// Results are cached per (shader, propertyName) pair to avoid re-walking the shader's
        /// property list on every repaint frame.
        /// </summary>
        public static int ResolveShaderPropertyType(UnityEngine.Renderer renderer, int matIndex, string propertyName)
        {
            if (renderer == null || string.IsNullOrEmpty(propertyName)) return -1;
            var mats = renderer.sharedMaterials;
            if (mats == null || matIndex < 0 || matIndex >= mats.Length || mats[matIndex] == null) return -1;
            var shader = mats[matIndex].shader;
            if (shader == null) return -1;

            var key = (shader.GetInstanceID(), propertyName);
            if (_propertyTypeCache.TryGetValue(key, out int cached))
                return cached;

            int count = UnityEditor.ShaderUtil.GetPropertyCount(shader);
            int result = -1;
            for (int p = 0; p < count; p++)
            {
                if (UnityEditor.ShaderUtil.GetPropertyName(shader, p) == propertyName)
                {
                    result = ShaderPropToActionType(UnityEditor.ShaderUtil.GetPropertyType(shader, p));
                    break;
                }
            }
            _propertyTypeCache[key] = result;
            return result;
        }

        /// <summary>
        /// Converts a ShaderUtil.ShaderPropertyType to the action property type index
        /// (0=Float, 1=Color, 2=Vector, 3=Texture).
        /// ShaderUtil enum: Color=0, Vector=1, Float=2, Range=3, TexEnv=4.
        /// </summary>
        public static int ShaderPropToActionType(UnityEditor.ShaderUtil.ShaderPropertyType t)
        {
            switch (t)
            {
                case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                case UnityEditor.ShaderUtil.ShaderPropertyType.Range:
                    return 0; // Float
                case UnityEditor.ShaderUtil.ShaderPropertyType.Color:
                    return 1; // Color
                case UnityEditor.ShaderUtil.ShaderPropertyType.Vector:
                    return 2; // Vector
                case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv:
                    return 3; // Texture
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Populates a shader property search tree using the shared attribute-walk
        /// grouping algorithm from <see cref="EnigmaShaderHelper.BuildShaderPropertyGroups"/>.
        /// Every call site that shows a shader property search window uses this
        /// helper, so the organization (section toggles + children + ungrouped
        /// bucket) is identical across the drawer, faders, folders, and anywhere
        /// else a shader property is selected in the editor UI.
        ///
        /// <para>The returned dictionary maps property name →
        /// <see cref="UnityEngine.Rendering.ShaderPropertyType"/> for every entry
        /// added to the tree. Callers typically use this in the search-window
        /// selection callback to auto-populate a type field or pick up default
        /// values from the material.</para>
        ///
        /// <para>Pass <paramref name="includeFilter"/> to exclude property types
        /// the caller can't use (e.g. faders pass <c>t =&gt; t != Texture</c> to
        /// hide texture properties). When a filter excludes a section toggle's
        /// children entirely the empty section is dropped.</para>
        /// </summary>
        internal static Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>
            PopulateShaderPropertyTree(
                EnigmaPropertySearch.Group mainGroup,
                UnityEngine.Shader shader,
                System.Predicate<UnityEngine.Rendering.ShaderPropertyType> includeFilter = null)
        {
            var result = new Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>();
            if (mainGroup == null || shader == null) return result;

            var groups = EnigmaShaderHelper.BuildShaderPropertyGroups(shader);
            foreach (var g in groups)
            {
                if (g.toggle == null)
                {
                    // Ungrouped bucket — render entries directly at the top
                    // level with no gear icons (matches pre-refactor behavior
                    // for properties the walker couldn't place in a section).
                    foreach (var d in g.children)
                    {
                        if (includeFilter != null && !includeFilter(d.type)) continue;
                        result[d.name] = d.type;
                        mainGroup.Add(FormatShaderPropertyEntryLabel(d, gearIfToggleAttr: false), d.name);
                    }
                    continue;
                }

                // Section toggle group. Build it lazily so a filter that
                // excludes every child AND the toggle header collapses the
                // whole sub-group away — we don't want empty headers in the
                // tree just because a filter stripped out all children.
                bool toggleIncluded = includeFilter == null || includeFilter(g.toggle.type);
                bool anyChildIncluded = false;
                if (includeFilter == null)
                    anyChildIncluded = g.children.Count > 0;
                else
                {
                    foreach (var d in g.children)
                    {
                        if (includeFilter(d.type)) { anyChildIncluded = true; break; }
                    }
                }
                if (!toggleIncluded && !anyChildIncluded) continue;

                string groupTitle = string.IsNullOrEmpty(g.toggle.description)
                    ? g.toggle.name
                    : $"{g.toggle.description}  ({g.toggle.name})";
                var subGroup = mainGroup.AddGroup(groupTitle);

                // Skip emitting marker-only toggle entries (Thry's hidden
                // `m_start_*` / `s_start_*` properties) — we use them to
                // label the sub-group, but selecting them as a fader/action
                // target would be a no-op because they're hidden markers.
                bool isThryMarker = g.toggle.name != null
                    && (g.toggle.name.StartsWith("m_start_", System.StringComparison.Ordinal)
                     || g.toggle.name.StartsWith("s_start_", System.StringComparison.Ordinal));
                if (toggleIncluded && !isThryMarker)
                {
                    result[g.toggle.name] = g.toggle.type;
                    subGroup.Add(
                        $"⚙ Toggle: {g.toggle.name}{FormatShaderPropertyTypeSuffix(g.toggle.type)}",
                        g.toggle.name);
                }

                foreach (var d in g.children)
                {
                    if (includeFilter != null && !includeFilter(d.type)) continue;
                    result[d.name] = d.type;
                    subGroup.Add(FormatShaderPropertyEntryLabel(d, gearIfToggleAttr: true), d.name);
                }
            }
            return result;
        }

        /// <summary>
        /// Overload that builds the property tree from the intersection of
        /// properties across several materials. Used by multi-renderer static
        /// faders so the Search tree only surfaces properties the fader can
        /// actually drive on every bound material.
        ///
        /// <para>Ordering and grouping follow the FIRST material's shader
        /// (the primary). A property is kept only when:
        /// <list type="bullet">
        /// <item>it exists on every unique shader across <paramref name="materials"/>;</item>
        /// <item>its <c>ShaderPropertyType</c> is identical on every one of them
        /// (a rename collision with a different type is excluded — safer than
        /// picking a type arbitrarily).</item>
        /// </list>
        /// </para>
        ///
        /// <para>Fast paths: a null/empty input returns an empty tree; a set
        /// that collapses to a single unique shader after null/dedupe
        /// delegates straight to the single-shader overload, so behaviour is
        /// identical to today for the common one-renderer case.</para>
        /// </summary>
        internal static Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>
            PopulateSharedShaderPropertyTree(
                EnigmaPropertySearch.Group mainGroup,
                Material[] materials,
                System.Predicate<UnityEngine.Rendering.ShaderPropertyType> includeFilter = null)
        {
            var result = new Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>();
            if (mainGroup == null || materials == null || materials.Length == 0) return result;

            // Collect unique shaders, preserving the order of first appearance
            // so materials[0]'s shader ends up first (the "primary").
            var uniqueShaders = new List<UnityEngine.Shader>();
            var seenIds = new HashSet<int>();
            foreach (var m in materials)
            {
                if (m == null || m.shader == null) continue;
                int id = m.shader.GetInstanceID();
                if (seenIds.Add(id))
                    uniqueShaders.Add(m.shader);
            }

            if (uniqueShaders.Count == 0) return result;
            if (uniqueShaders.Count == 1)
                return PopulateShaderPropertyTree(mainGroup, uniqueShaders[0], includeFilter);

            // Build per-shader name→type maps for every non-primary shader.
            // Index 0 is the primary; we walk its BuildShaderPropertyGroups
            // output below and cross-check each descriptor against these maps.
            var otherMaps = new List<Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>>(uniqueShaders.Count - 1);
            for (int s = 1; s < uniqueShaders.Count; s++)
            {
                var sh = uniqueShaders[s];
                var map = new Dictionary<string, UnityEngine.Rendering.ShaderPropertyType>();
                int count = sh.GetPropertyCount();
                for (int p = 0; p < count; p++)
                {
                    string pname = sh.GetPropertyName(p);
                    if (string.IsNullOrEmpty(pname)) continue;
                    // Last-declaration-wins on duplicates is fine; real shaders
                    // don't declare the same name twice.
                    map[pname] = sh.GetPropertyType(p);
                }
                otherMaps.Add(map);
            }

            // Local predicate: a descriptor is shared iff every other shader
            // has the same name AND the same ShaderPropertyType.
            bool IsShared(EnigmaShaderHelper.ShaderPropertyDescriptor d)
            {
                foreach (var om in otherMaps)
                {
                    if (!om.TryGetValue(d.name, out var t)) return false;
                    if (t != d.type) return false;
                }
                return true;
            }

            var primary = uniqueShaders[0];
            var groups = EnigmaShaderHelper.BuildShaderPropertyGroups(primary);
            foreach (var g in groups)
            {
                if (g.toggle == null)
                {
                    foreach (var d in g.children)
                    {
                        if (includeFilter != null && !includeFilter(d.type)) continue;
                        if (!IsShared(d)) continue;
                        result[d.name] = d.type;
                        mainGroup.Add(FormatShaderPropertyEntryLabel(d, gearIfToggleAttr: false), d.name);
                    }
                    continue;
                }

                // Section: only draw the sub-group if either the section
                // toggle itself OR at least one child is both type-allowed AND
                // shared. Matches the single-shader overload's "don't emit
                // empty sections" behaviour.
                bool toggleTypeOk = includeFilter == null || includeFilter(g.toggle.type);
                bool toggleShared = IsShared(g.toggle);
                bool toggleIncluded = toggleTypeOk && toggleShared;

                bool anyChildIncluded = false;
                foreach (var d in g.children)
                {
                    if (includeFilter != null && !includeFilter(d.type)) continue;
                    if (!IsShared(d)) continue;
                    anyChildIncluded = true;
                    break;
                }
                if (!toggleIncluded && !anyChildIncluded) continue;

                string groupTitle = string.IsNullOrEmpty(g.toggle.description)
                    ? g.toggle.name
                    : $"{g.toggle.description}  ({g.toggle.name})";
                var subGroup = mainGroup.AddGroup(groupTitle);

                // Same marker-suppression rule as the single-shader overload —
                // Thry hidden `m_start_*` / `s_start_*` markers label groups
                // but aren't usable property targets.
                bool isThryMarker = g.toggle.name != null
                    && (g.toggle.name.StartsWith("m_start_", System.StringComparison.Ordinal)
                     || g.toggle.name.StartsWith("s_start_", System.StringComparison.Ordinal));
                if (toggleIncluded && !isThryMarker)
                {
                    result[g.toggle.name] = g.toggle.type;
                    subGroup.Add(
                        $"⚙ Toggle: {g.toggle.name}{FormatShaderPropertyTypeSuffix(g.toggle.type)}",
                        g.toggle.name);
                }

                foreach (var d in g.children)
                {
                    if (includeFilter != null && !includeFilter(d.type)) continue;
                    if (!IsShared(d)) continue;
                    result[d.name] = d.type;
                    subGroup.Add(FormatShaderPropertyEntryLabel(d, gearIfToggleAttr: true), d.name);
                }
            }
            return result;
        }

        /// <summary>
        /// Converts a Shader.GetPropertyType result to the action property type index.
        /// </summary>
        internal static int ShaderPropertyTypeToActionType(UnityEngine.Rendering.ShaderPropertyType t)
        {
            switch (t)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:
#endif
                    return 0;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    return 1;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    return 2;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    return 3;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Returns a bracketed type suffix for a shader property type, shown
        /// at the end of each search-tree entry so users can tell at a glance
        /// what the property takes. Every type gets a suffix — including
        /// <c>[Float]</c>, <c>[Range]</c>, and <c>[Int]</c> — so there's no
        /// ambiguity between "this has no annotation because it's float" and
        /// "this has no annotation because the helper didn't recognize it."
        /// Distinguishing <c>[Range]</c> from <c>[Float]</c> is also useful
        /// because Range properties carry min/max bounds that affect how
        /// they're bound to faders.
        /// </summary>
        private static string FormatShaderPropertyTypeSuffix(UnityEngine.Rendering.ShaderPropertyType t)
        {
            switch (t)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:   return "  [Float]";
                case UnityEngine.Rendering.ShaderPropertyType.Range:   return "  [Range]";
                case UnityEngine.Rendering.ShaderPropertyType.Color:   return "  [Color]";
                case UnityEngine.Rendering.ShaderPropertyType.Vector:  return "  [Vector]";
                case UnityEngine.Rendering.ShaderPropertyType.Texture: return "  [Texture]";
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:     return "  [Int]";
#endif
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Formats a single entry line for the shader property search tree.
        /// Format: <c>[⚙] &lt;description&gt;  (&lt;propName&gt;)[  [Type]]</c>
        /// where the gear is added only when <paramref name="gearIfToggleAttr"/>
        /// is true AND the descriptor carries a toggle-like attribute. The
        /// description falls back to the raw property name when the shader
        /// didn't supply one.
        /// </summary>
        private static string FormatShaderPropertyEntryLabel(
            EnigmaShaderHelper.ShaderPropertyDescriptor d, bool gearIfToggleAttr)
        {
            string displayName = string.IsNullOrEmpty(d.description) ? d.name : d.description;
            string suffix      = FormatShaderPropertyTypeSuffix(d.type);
            bool gear          = gearIfToggleAttr && d.hasToggleAttribute;
            return gear
                ? $"⚙ {displayName}  ({d.name}){suffix}"
                : $"{displayName}  ({d.name}){suffix}";
        }

        /// <summary>
        /// Finds the VRC PipelineManager in the scene and returns its blueprintId.
        /// Returns null if not found or empty.
        /// </summary>
        private static string DetectWorldIdFromScene()
        {
            // PipelineManager is in a DLL (VRCCore-Editor), use FindObjectOfType via reflection.
            var pmType = System.Type.GetType("VRC.Core.PipelineManager, VRCCore-Editor");
            if (pmType == null) return null;
            var pm = UnityEngine.Object.FindObjectOfType(pmType);
            if (pm == null) return null;
            var field = pmType.GetField("blueprintId");
            if (field == null) return null;
            string id = field.GetValue(pm) as string;
            return string.IsNullOrEmpty(id) ? null : id.Trim();
        }

        private Behaviour DrawComponentPickerField(UnityEngine.Object dirtyObj, GameObject targetObject,
                                                   Behaviour current, string fieldKey)
        {
            if (_pendingComponentValues.TryGetValue(fieldKey, out Behaviour pending))
            {
                _pendingComponentValues.Remove(fieldKey);
                current = pending;
                EditorUtility.SetDirty(dirtyObj);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Component");
            string compLabel = current != null ? current.GetType().Name : "(none)";
            EditorGUILayout.LabelField(compLabel, EditorStyles.miniLabel);

            if (GUILayout.Button("…", GUILayout.Width(24)))
            {
                var search = new EnigmaPropertySearch("Components");
                var group  = search.GetMainGroup();
                Component[] components = targetObject.GetComponents<Component>();
                bool any = false;
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    Behaviour b = comp as Behaviour;
                    if (b == null) continue;
                    group.Add(comp.GetType().Name, b);
                    any = true;
                }
                if (any)
                {
                    string capturedKey = fieldKey;
                    search.Open((Behaviour selected) =>
                    {
                        _pendingComponentValues[capturedKey] = selected;
                        _repaint();
                    });
                }
                else
                {
                    EditorUtility.DisplayDialog("No Components",
                        "No toggleable Behaviour components were found on the selected GameObject.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();
            return current;
        }

        private void OpenActionTypePicker(EnigmaController ctrl, Action<int[]> onSelect)
        {
            bool hasCtrl = ctrl != null;
            var search = new EnigmaPropertySearch("Action Type");
            var main   = search.GetMainGroup();

            var state = main.AddGroup("Toggle");
            state.Add("Toggle Autochange Group",     new int[] { 0, 11,  0 });
            state.Add("Toggle Material",             new int[] { 0,  3,  1 });
            state.Add("Toggle Object",               new int[] { 0,  0,  0 });
            state.Add("Toggle Shader",               new int[] { 0, 15,  0 });
            state.Add("Toggle Shader Keyword",       new int[] { 0,  4,  3 });
            state.Add("Toggle Shader Property",      new int[] { 0,  4,  2 });
            state.Add("Toggle Skybox",               new int[] { 0,  5,  1 });
            state.Add("Toggle Transform",            new int[] { 0,  7,  0 });
            state.Add("Toggle Udon Variable",        new int[] { 0,  6,  4 });

            var cmd = main.AddGroup("Command");
            cmd.Add("Apply Material",                new int[] { 1,  3,  1 });
            cmd.Add("Apply Skybox",                  new int[] { 1,  5,  1 });
            cmd.Add("Set Autochange Group State",    new int[] { 1, 11,  3 });
            cmd.Add("Set Object State",              new int[] { 1,  0,  3 });
            cmd.Add("Set Shader Keyword",            new int[] { 1,  4,  3 });
            cmd.Add("Set Shader Property",           new int[] { 1,  4,  2 });
            cmd.Add("Set Transform",                 new int[] { 1,  7,  2 });
            cmd.Add("Set Udon Variable",             new int[] { 1,  6,  4 });
            cmd.Add("Teleport Object",               new int[] { 1,  0,  6 });
            cmd.Add("Teleport Player",               new int[] { 1,  8,  6 });
            cmd.Add("Trigger Udon Event",            new int[] { 1,  6,  5 });

            var sel = main.AddGroup("Selection");
            var selColor = sel.AddGroup("Color Palette");
            selColor.Add("Next Color",               new int[] { 2,  9,  7 });
            selColor.Add("Previous Color",           new int[] { 2,  9,  8 });
            selColor.Add("Set Color",                new int[] { 2,  9,  9 });
            var selVariant = sel.AddGroup("Variants");
            selVariant.Add("Next Variant",           new int[] { 2, 10,  7 });
            selVariant.Add("Previous Variant",       new int[] { 2, 10,  8 });
            selVariant.Add("Set Variant",            new int[] { 2, 10,  9 });

            if (hasCtrl)
            {
                var preset = main.AddGroup("Preset");
                preset.Add("Clear Presets",              new int[] { 3, 12, 13 });
                preset.Add("Load Presets",               new int[] { 3, 12, 11 });
                preset.Add("Preset Slot",                new int[] { 3, 12, 12 });
                preset.Add("Save Presets",               new int[] { 3, 12, 10 });
            }

            var disp = main.AddGroup("Display");
            disp.Add("Autochange Group",             new int[] { 4, 11, 14 });
            disp.Add("Color Palette",                new int[] { 4,  9, 14 });
            disp.Add("Controller",                   new int[] { 4, 13, 14 });
            disp.Add("Shader Property",              new int[] { 4,  4, 14 });
            disp.Add("Stat",                         new int[] { 4, 14, 14 });
            disp.Add("Udon Variable",                new int[] { 4,  6, 14 });
            disp.Add("Variant Group",                new int[] { 4, 10, 14 });

            if (hasCtrl)
            {
                var sys = main.AddGroup("System");

                // Subgroups in alphabetical order (Fader < Folder < Page),
                // followed by the lone Reset leaf which sorts after the
                // subgroups by name (R > P) — matches the alphabetical
                // ordering convention used throughout the picker.
                var sysFader = sys.AddGroup("Fader");
                sysFader.Add("Go To Fader Page",         new int[] { 5, 13, 27 });
                sysFader.Add("Next Fader Page",          new int[] { 5, 13, 25 });
                sysFader.Add("Previous Fader Page",      new int[] { 5, 13, 26 });
                sysFader.Add("Set Fader Mode",           new int[] { 5, 13, 22 });

                var sysFolder = sys.AddGroup("Folder");
                sysFolder.Add("Display Folder Name",     new int[] { 5, 13, 23 });
                sysFolder.Add("Go To Folder",            new int[] { 5, 13, 17 });
                sysFolder.Add("Next Folder",             new int[] { 5, 13, 15 });
                sysFolder.Add("Previous Folder",         new int[] { 5, 13, 16 });

                var sysPage = sys.AddGroup("Page");
                sysPage.Add("Display Page Number",       new int[] { 5, 13, 24 });
                sysPage.Add("Go To Page",                new int[] { 5, 13, 20 });
                sysPage.Add("Next Page",                 new int[] { 5, 13, 18 });
                sysPage.Add("Previous Page",             new int[] { 5, 13, 19 });

                // Whitelist subgroup. The picker triple uses target=13
                // (Controller) just like the legacy Set Controller State /
                // Toggle Controller Whitelist did — SyncActionType maps
                // cat/tgt/op back to actionType 18 (Set Whitelist) or 28
                // (Toggle Whitelist).
                var sysWhitelist = sys.AddGroup("Whitelist");
                sysWhitelist.Add("Set Whitelist",        new int[] { 1, 13,  3 });
                sysWhitelist.Add("Toggle Whitelist",     new int[] { 0, 13,  0 });

                sys.Add("Reset",                         new int[] { 5, 13, 21 });
            }

            search.Open(onSelect);
        }
    }
}
#endif

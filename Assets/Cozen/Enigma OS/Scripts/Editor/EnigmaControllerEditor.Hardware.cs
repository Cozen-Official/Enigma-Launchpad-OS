#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    public partial class EnigmaControllerEditor
    {
        // ── Reorderable lists ──
        private ReorderableList _buttonList;
        private ReorderableList _faderList;

        // ── Hardware sub-foldout state ──
        private bool _showButtonSlots = false;
        private bool _showFaderSlots  = false;

        // ════════════════════════════════════════════════════════════════════════
        //  HARDWARE PANEL (buttons + faders reorderable lists)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawHardware()
        {
            EnsureButtonList();
            EnsureFaderList();

            // ── Auto Assign ──────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Auto Assign Buttons & Faders", GUILayout.Width(220)))
                AutoAssignHardwareFromChildren();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // ── Button Slots sub-foldout ─────────────────────────────────────
            _showButtonSlots = DrawSubFoldout(_showButtonSlots, "Button Slots");
            if (_showButtonSlots)
                _buttonList.DoLayoutList();

            EditorGUILayout.Space(4);

            // ── Fader Slots sub-foldout ──────────────────────────────────────
            _showFaderSlots = DrawSubFoldout(_showFaderSlots, "Fader Slots");
            if (_showFaderSlots)
                _faderList.DoLayoutList();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Assign references in order, left to right per row.",
                MessageType.Info);

        }

        // ════════════════════════════════════════════════════════════════════════
        //  REORDERABLE LIST SETUP
        // ════════════════════════════════════════════════════════════════════════

        private void EnsureButtonList()
        {
            if (_buttonList != null) return;

            SerializedProperty prop = _so.FindProperty("buttonSlots");
            _buttonList = new ReorderableList(_so, prop, true, true, true, true);

            _buttonList.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, $"Button Slots  [{prop.arraySize}]");

            _buttonList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty element = prop.GetArrayElementAtIndex(index);
                rect.y      += 2;
                rect.height  = EditorGUIUtility.singleLineHeight;
                float labelW = 60f;

                EditorGUI.LabelField(new Rect(rect.x, rect.y, labelW, rect.height),
                    $"Slot {index}");
                EditorGUI.PropertyField(
                    new Rect(rect.x + labelW, rect.y, rect.width - labelW, rect.height),
                    element, GUIContent.none);
            };

            _buttonList.onAddCallback = list =>
            {
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = null;
                _so.ApplyModifiedProperties();
            };

            _buttonList.onChangedCallback = list =>
            {
                // Auto-assign slotIndex after reorder
                AutoAssignButtonSlotIndices((EnigmaController)target);
            };
        }

        private void EnsureFaderList()
        {
            if (_faderList != null) return;

            SerializedProperty prop = _so.FindProperty("faderSlots");
            _faderList = new ReorderableList(_so, prop, true, true, true, true);

            _faderList.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, $"Fader Slots  [{prop.arraySize}]");

            _faderList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty element = prop.GetArrayElementAtIndex(index);
                rect.y      += 2;
                rect.height  = EditorGUIUtility.singleLineHeight;
                float labelW = 60f;

                EditorGUI.LabelField(new Rect(rect.x, rect.y, labelW, rect.height),
                    $"Slot {index}");
                EditorGUI.PropertyField(
                    new Rect(rect.x + labelW, rect.y, rect.width - labelW, rect.height),
                    element, GUIContent.none);
            };

            _faderList.onAddCallback = list =>
            {
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = null;
                _so.ApplyModifiedProperties();
            };

            _faderList.onChangedCallback = list =>
            {
                // Auto-assign slotIndex after reorder
                AutoAssignFaderSlotIndices((EnigmaController)target);
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HARDWARE AUTO-ASSIGNMENT FROM CHILDREN
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans two levels down (controller → child → grandchild) for all
        /// EnigmaManagedButton and EnigmaFader components and populates
        /// buttonSlots / faderSlots in order. Also back-assigns controller + slotIndex
        /// on each found content button/fader, and wires linkedController on every
        /// EnigmaButton found in the same child hierarchy (auto-fills their renderer/text too).
        /// </summary>
        private void AutoAssignHardwareFromChildren()
        {
            EnigmaController ctrl = (EnigmaController)target;
            Undo.RecordObject(ctrl, "Auto Assign Buttons & Faders");

            var buttons    = new System.Collections.Generic.List<EnigmaManagedButton>();
            var faders     = new System.Collections.Generic.List<EnigmaFader>();
            var enigmaBtns = new System.Collections.Generic.List<EnigmaButton>();

            // Collect grandchildren (buttons) and great-grandchildren (faders).
            // EnigmaFader lives on the "Fader" knob child (great-grandchild), while
            // buttons live on the group object (grandchild).
            for (int ci = 0; ci < ctrl.transform.childCount; ci++)
            {
                Transform child = ctrl.transform.GetChild(ci);
                for (int gi = 0; gi < child.childCount; gi++)
                {
                    Transform grandchild = child.GetChild(gi);

                    EnigmaManagedButton btn = grandchild.GetComponent<EnigmaManagedButton>();
                    if (btn != null) buttons.Add(btn);

                    EnigmaButton eb = grandchild.GetComponent<EnigmaButton>();
                    if (eb != null) enigmaBtns.Add(eb);

                    // Faders are one level deeper (on the knob child)
                    for (int ggi = 0; ggi < grandchild.childCount; ggi++)
                    {
                        EnigmaFader fdr = grandchild.GetChild(ggi).GetComponent<EnigmaFader>();
                        if (fdr != null) faders.Add(fdr);
                    }
                }
            }

            ctrl.buttonSlots = buttons.ToArray();
            ctrl.faderSlots  = faders.ToArray();

            // Back-assign controller + slotIndex on content buttons
            for (int i = 0; i < ctrl.buttonSlots.Length; i++)
            {
                if (ctrl.buttonSlots[i] == null) continue;
                Undo.RecordObject(ctrl.buttonSlots[i], "Auto Assign Buttons & Faders");
                ctrl.buttonSlots[i].controller = ctrl;
                ctrl.buttonSlots[i].slotIndex  = i;
                EditorUtility.SetDirty(ctrl.buttonSlots[i]);
            }

            for (int i = 0; i < ctrl.faderSlots.Length; i++)
            {
                if (ctrl.faderSlots[i] == null) continue;
                Undo.RecordObject(ctrl.faderSlots[i], "Auto Assign Buttons & Faders");
                ctrl.faderSlots[i].controller = ctrl;
                ctrl.faderSlots[i].slotIndex  = i;
                EnigmaFaderEditor.AutoAssignFaderReferences(ctrl.faderSlots[i]);
            }

            // Wire shared hand colliders — look for "Left/Right Hand Collider" as siblings of faders
            if (ctrl.sharedLeftHandCollider == null || ctrl.sharedRightHandCollider == null)
            {
                for (int ci = 0; ci < ctrl.transform.childCount; ci++)
                {
                    Transform child = ctrl.transform.GetChild(ci);
                    if (ctrl.sharedLeftHandCollider == null)
                    {
                        var t = child.Find("Left Hand Collider");
                        if (t != null) ctrl.sharedLeftHandCollider = t.gameObject;
                    }
                    if (ctrl.sharedRightHandCollider == null)
                    {
                        var t = child.Find("Right Hand Collider");
                        if (t != null) ctrl.sharedRightHandCollider = t.gameObject;
                    }
                    if (ctrl.sharedLeftHandCollider != null && ctrl.sharedRightHandCollider != null) break;
                }
            }

            // Wire linkedController on all EnigmaButtons + auto-fill child renderer/text
            for (int i = 0; i < enigmaBtns.Count; i++)
            {
                EnigmaButton eb = enigmaBtns[i];
                if (eb == null) continue;
                Undo.RecordObject(eb, "Auto Assign Buttons & Faders");
                eb.linkedController = ctrl;

                if (eb.buttonRenderer == null || eb.buttonText == null)
                {
                    foreach (Transform child in eb.transform)
                    {
                        if (eb.buttonRenderer == null)
                        {
                            Renderer r = child.GetComponent<Renderer>();
                            if (r != null && child.GetComponent<TMPro.TMP_Text>() == null)
                                eb.buttonRenderer = r;
                        }

                        if (eb.buttonText == null)
                        {
                            TMPro.TMP_Text tmp = child.GetComponent<TMPro.TMP_Text>();
                            if (tmp != null)
                                eb.buttonText = tmp;
                        }
                    }
                }

                EditorUtility.SetDirty(eb);
            }

            // Set the preview grid to the most-square ratio that fits all assigned buttons.
            // Skip when no buttons were found so any existing manual layout is preserved.
            if (ctrl.buttonSlots.Length > 0)
            {
                ComputePreviewGrid(ctrl.buttonSlots.Length, out int previewCols, out int previewRows);
                ctrl.previewColumns = previewCols;
                ctrl.previewRows    = previewRows;
                Debug.Log($"[EnigmaController] Auto-assigned {ctrl.buttonSlots.Length} button(s), " +
                          $"{ctrl.faderSlots.Length} fader(s), and {enigmaBtns.Count} standalone button(s). " +
                          $"Preview grid set to {previewCols} × {previewRows}.");
            }
            else
            {
                Debug.Log($"[EnigmaController] Auto-assigned 0 button(s), " +
                          $"{ctrl.faderSlots.Length} fader(s), and {enigmaBtns.Count} standalone button(s). " +
                          $"Preview grid unchanged.");
            }

            EditorUtility.SetDirty(ctrl);
            _so.Update();
            // Defer the list invalidation to after the current GUI pass.
            // Nulling _buttonList/_faderList here (mid-draw) would
            // crash DoLayoutList() calls later in the same DrawHardware() invocation.
            EditorApplication.delayCall += () =>
            {
                _buttonList = null;  // Force rebuild so lists reflect new array contents and header counts
                _faderList  = null;
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SLOT INDEX AUTO-ASSIGNMENT
        // ════════════════════════════════════════════════════════════════════════

        private static void AutoAssignButtonSlotIndices(EnigmaController ctrl)
        {
            if (ctrl.buttonSlots == null) return;
            for (int i = 0; i < ctrl.buttonSlots.Length; i++)
            {
                EnigmaManagedButton btn = ctrl.buttonSlots[i];
                if (btn == null) continue;
                btn.slotIndex  = i;
                btn.controller = ctrl;

                // Auto-fill buttonRenderer and buttonText from child objects when
                // the fields are not yet assigned — mirrors EnigmaManagedButton.Reset().
                if (btn.buttonRenderer == null || btn.buttonText == null)
                {
                    foreach (Transform child in btn.transform)
                    {
                        if (btn.buttonRenderer == null)
                        {
                            Renderer r = child.GetComponent<Renderer>();
                            if (r != null && child.GetComponent<TMPro.TMP_Text>() == null)
                                btn.buttonRenderer = r;
                        }

                        if (btn.buttonText == null)
                        {
                            TMPro.TMP_Text tmp = child.GetComponent<TMPro.TMP_Text>();
                            if (tmp != null)
                                btn.buttonText = tmp;
                        }
                    }
                }

                EditorUtility.SetDirty(btn);
            }
        }

        private static void AutoAssignFaderSlotIndices(EnigmaController ctrl)
        {
            if (ctrl.faderSlots == null) return;
            for (int i = 0; i < ctrl.faderSlots.Length; i++)
            {
                if (ctrl.faderSlots[i] == null) continue;
                ctrl.faderSlots[i].slotIndex = i;
                ctrl.faderSlots[i].controller = ctrl;
                EnigmaFaderEditor.AutoAssignFaderReferences(ctrl.faderSlots[i]);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PREVIEW GRID HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Computes the most-square (cols × rows) layout for <paramref name="count"/> buttons.
        /// rows = floor(sqrt(count)); cols = ceil(count / rows).
        /// cols is always ≥ rows so the grid is wider than it is tall when not square
        /// (e.g. 12 → 4 × 3, 16 → 4 × 4, 9 → 3 × 3).
        /// </summary>
        private static void ComputePreviewGrid(int count, out int cols, out int rows)
        {
            // Mathf.Max(1,...) guards against sqrt(0)=0 causing division by zero on the next line.
            rows = Mathf.Max(1, Mathf.FloorToInt(Mathf.Sqrt(count)));
            cols = Mathf.CeilToInt((float)count / rows);
            // cols >= rows is guaranteed by the math above, so no swap is needed.
        }
    }
}
#endif

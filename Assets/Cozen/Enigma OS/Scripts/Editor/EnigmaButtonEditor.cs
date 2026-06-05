#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Custom inspector for the standalone EnigmaButton component.
    /// Uses the shared EnigmaActionListDrawer to show the same rich action editor
    /// as the main EnigmaController inspector.
    /// </summary>
    [CustomEditor(typeof(EnigmaButton))]
    public class EnigmaButtonEditor : UnityEditor.Editor
    {
        private SerializedObject     _so;
        private EnigmaActionListDrawer _actionDrawer;

        /// <summary>
        /// Returns the companion component, or null if it doesn't exist.
        /// Use in build paths where auto-creation is not appropriate.
        /// </summary>
        internal static EnigmaButtonActions GetActionsIfExists(EnigmaButton btn)
        {
            return btn.GetComponent<EnigmaButtonActions>();
        }

        /// <summary>
        /// Returns (or auto-creates) the companion component.
        /// Use in inspector / interactive editor contexts.
        /// </summary>
        internal static EnigmaButtonActions GetOrCreateActions(EnigmaButton btn)
        {
            var holder = btn.GetComponent<EnigmaButtonActions>();
            if (holder == null)
                holder = Undo.AddComponent<EnigmaButtonActions>(btn.gameObject);
            return holder;
        }

        private void OnEnable()
        {
            _so           = serializedObject;
            _actionDrawer = new EnigmaActionListDrawer(Repaint);
        }

        public override void OnInspectorGUI()
        {
            // Lazy-init: UdonSharp's editor wrapper may skip OnEnable
            if (_so == null) _so = serializedObject;
            if (_actionDrawer == null) _actionDrawer = new EnigmaActionListDrawer(Repaint);

            _so.Update();
            EnigmaButton btn = (EnigmaButton)target;

            bool isPlaying = EditorApplication.isPlaying;
            if (isPlaying)
                EditorGUILayout.HelpBox("Configuration is read-only during play mode.", MessageType.Info);

            using (new EditorGUI.DisabledScope(isPlaying))
            {

            // ── Header ──
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Enigma Button", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // ── Button-wide Options ▾ menu ──
            if (GUILayout.Button("Options ▾", GUILayout.Width(80)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("On By Default"), btn.onByDefault, () =>
                {
                    btn.onByDefault = !btn.onByDefault;
                    EditorUtility.SetDirty(btn);
                    Repaint();
                });
                menu.AddItem(new GUIContent("Use Exclusive Tags"), btn.useExclusiveGroup, () =>
                {
                    btn.useExclusiveGroup = !btn.useExclusiveGroup;
                    if (!btn.useExclusiveGroup)
                        btn.exclusiveOff = false;
                    EditorUtility.SetDirty(btn);
                    Repaint();
                });
                if (btn.useExclusiveGroup)
                {
                    menu.AddItem(new GUIContent("Exclusive Off"), btn.exclusiveOff, () =>
                    {
                        btn.exclusiveOff = !btn.exclusiveOff;
                        EditorUtility.SetDirty(btn);
                        Repaint();
                    });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();

            // Active option pills
            bool hasActivePills = btn.onByDefault || btn.exclusiveOff;
            if (hasActivePills)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                if (btn.onByDefault)
                    EnigmaActionListDrawer.DrawActionTagPill("On By Default", () =>
                    {
                        btn.onByDefault = false;
                        EditorUtility.SetDirty(btn);
                        Repaint();
                    });
                if (btn.exclusiveOff)
                    EnigmaActionListDrawer.DrawActionTagPill("Exclusive Off", () =>
                    {
                        btn.exclusiveOff = false;
                        EditorUtility.SetDirty(btn);
                        Repaint();
                    });
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField("Standalone action button", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // ── Visuals ──
            EditorGUILayout.LabelField("Visuals", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Auto Assign", GUILayout.Width(100)))
            {
                Undo.RecordObject(btn, "Auto Assign EnigmaButton");
                foreach (Transform child in btn.transform)
                {
                    Renderer r           = child.GetComponent<Renderer>();
                    TMPro.TMP_Text tmp   = child.GetComponent<TMPro.TMP_Text>();
                    if (btn.buttonRenderer == null && r != null && tmp == null)
                        btn.buttonRenderer = r;
                    if (btn.buttonText == null && tmp != null)
                        btn.buttonText = tmp;
                }
                EditorUtility.SetDirty(btn);
                _so.Update();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(_so.FindProperty("buttonText"),     new GUIContent("TMP"));
            if (_so.FindProperty("buttonText").objectReferenceValue != null)
                EditorGUILayout.PropertyField(_so.FindProperty("label"),      new GUIContent("Label Text"));
            EditorGUILayout.PropertyField(_so.FindProperty("buttonRenderer"), new GUIContent("Renderer"));
            if (_so.FindProperty("buttonRenderer").objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(_so.FindProperty("activeColor"),   new GUIContent("Active Color"));
                EditorGUILayout.PropertyField(_so.FindProperty("inactiveColor"), new GUIContent("Inactive Color"));
                EditorGUILayout.PropertyField(_so.FindProperty("flashDuration"), new GUIContent("Flash Duration"));
            }

            EditorGUILayout.Space(4);

            // ── Actions ──
            var actionsHolder = GetOrCreateActions(btn);

            // Expire + Exclusive Off conflict warning. Expire is now a per-button
            // setting (btn.useExpire) rather than per-action; the loop concern is
            // the same — Exclusive Off auto-activates the button, Expire counts
            // down, button deactivates, peers reactivate it, repeat.
            if (btn.exclusiveOff && btn.useExpire)
            {
                EditorGUILayout.HelpBox(
                    "Warning: This button has both Exclusive Off and Expire enabled. " +
                    "When this button auto-activates as the exclusive-off state and then expires, " +
                    "it will re-activate itself, creating a loop. Disable one of the two.",
                    MessageType.Warning);
            }
            _actionDrawer.DrawActionList(actionsHolder, btn.linkedController, ref actionsHolder.actions);

            EditorGUILayout.Space(4);

            // ── Controller link ──
            EditorGUILayout.LabelField("Controller Link (Enables more actions)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("linkedController"),
                new GUIContent("Linked Controller"));
            if (btn.linkedController != null)
            {
                if (btn.useExclusiveGroup)
                    EditorGUILayout.PropertyField(_so.FindProperty("exclusiveGroup"),
                        new GUIContent("Exclusive Tags"));
            }

            EditorGUILayout.Space(4);

            // ── Expire (auto-deactivate) ──
            // Per-button setting; only meaningful for buttons that toggle stateful
            // actions. Use the standalone-button equivalent of the controller-side
            // entry Options menu — a checkbox + a conditional time field.
            EditorGUILayout.PropertyField(_so.FindProperty("useExpire"),
                new GUIContent("Use Expire", "Auto-deactivate this button after a configured time."));
            if (btn.useExpire)
            {
                EditorGUI.indentLevel++;
                var expireProp = _so.FindProperty("expireSeconds");
                expireProp.floatValue = Mathf.Max(0.1f,
                    EditorGUILayout.FloatField("Expire (s)", expireProp.floatValue));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ── Whitelist ──
            if (btn.linkedController != null)
            {
                EditorGUILayout.LabelField("Whitelist", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Using whitelist from linked Enigma Controller.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Whitelist (Optional)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_so.FindProperty("whitelistEnabled"), new GUIContent("Enable"));
                if (btn.whitelistEnabled)
                    EditorGUILayout.PropertyField(_so.FindProperty("authorizedUsernames"),
                        new GUIContent("Authorized Usernames"), true);
            }

            EditorGUILayout.Space(4);

            // ── Trigger ──
            EditorGUILayout.LabelField("Collider Trigger", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("triggerOnEnter"),
                new GUIContent("Trigger On Enter", "Fire the button's actions when the local player enters a trigger collider on this GameObject."));
            EditorGUILayout.PropertyField(_so.FindProperty("triggerOnExit"),
                new GUIContent("Trigger On Exit", "Fire the button's actions when the local player exits a trigger collider on this GameObject."));
            if (btn.triggerOnEnter || btn.triggerOnExit)
            {
                EditorGUILayout.HelpBox(
                    "Place this component on a GameObject with a trigger collider (Is Trigger enabled). " +
                    "The button's actions will fire automatically when the local player enters or exits the collider. " +
                    "Do not use this on a small button-sized collider — use it on room-sized triggers instead.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);

            // ── Build ──
            EditorGUILayout.LabelField("Build (Runs on build and play mode entry)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click Build to compile the action list above into runtime flat arrays.\n" +
                "You must rebuild after changing any action.",
                MessageType.Info);
            if (GUILayout.Button("Build Runtime Arrays"))
            {
                _so.ApplyModifiedProperties();
                EnigmaControllerEditor.RunBuildButton(btn);
            }

            } // end DisabledScope(isPlaying)

            _so.ApplyModifiedProperties();
        }
    }
}
#endif

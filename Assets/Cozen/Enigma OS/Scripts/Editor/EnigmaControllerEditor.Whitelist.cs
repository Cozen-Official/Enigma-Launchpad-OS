#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UdonSharp;

#if ENIGMAOS_OHGEEZ
// OhGeezCmon Access Control is in Assembly-CSharp (no namespace)
#endif
#if ENIGMAOS_PROTV
using ArchiTech.ProTV;
#endif
#if ENIGMAOS_FLATLINE
// Flatline is in Assembly-CSharp (no namespace)
#endif

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Whitelist configuration panel in the EnigmaController inspector.
    /// Supports manual username lists and optional third-party access control integrations:
    ///   • OhGeezCmon Access Control Manager
    ///   • ProTV Managed Whitelist
    ///   • Flatline Sync
    /// </summary>
    public partial class EnigmaControllerEditor
    {
        private void DrawWhitelist()
        {
            SerializedProperty enabledProp = _so.FindProperty("whitelistEnabled");
            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enable Whitelist"));

            if (!enabledProp.boolValue)
            {
                EditorGUILayout.HelpBox("Whitelist is disabled. All players can interact.", MessageType.Info);
                return;
            }

            EditorGUILayout.PropertyField(_so.FindProperty("instanceOwnerAlwaysHasAccess"),
                new GUIContent("Instance Owner Always Has Access"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Third-Party Integrations (Optional)", EditorStyles.boldLabel);

            // ── OhGeezCmon Access Control ──
            SerializedProperty ohGeezProp = _so.FindProperty("ohGeezCmonAccessControl");
#if ENIGMAOS_OHGEEZ
            ohGeezProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("OhGeezCmon Access Control",
                    "Highest priority. Routes whitelist to ProTV and Flatline."),
                ohGeezProp.objectReferenceValue,
                typeof(AccessControlManager),
                true);
#else
            ohGeezProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("OhGeezCmon Access Control",
                    "Highest priority (OhGeezCmon not installed). Routes whitelist to ProTV and Flatline."),
                ohGeezProp.objectReferenceValue,
                typeof(UdonSharpBehaviour),
                true);
#endif

            // ── ProTV Managed Whitelist ──
            SerializedProperty proTVProp = _so.FindProperty("proTVManagedWhitelist");
#if ENIGMAOS_PROTV
            proTVProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("ProTV Managed Whitelist",
                    "Second priority. Routes whitelist to Flatline when OhGeezCmon is not assigned."),
                proTVProp.objectReferenceValue,
                typeof(TVManagedWhitelist),
                true);
#else
            proTVProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("ProTV Managed Whitelist",
                    "Second priority (ProTV not installed). Routes whitelist to Flatline when OhGeezCmon is not assigned."),
                proTVProp.objectReferenceValue,
                typeof(UdonSharpBehaviour),
                true);
#endif

            // ── Flatline Sync ──
            SerializedProperty flatlineProp = _so.FindProperty("flatlineSync");
#if ENIGMAOS_FLATLINE
            flatlineProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("Flatline Sync",
                    "Third priority. Drives whitelist when OhGeezCmon and ProTV are not assigned."),
                flatlineProp.objectReferenceValue,
                typeof(FlatlineSync),
                true);
#else
            flatlineProp.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent("Flatline Sync",
                    "Third priority (Flatline not installed). Drives whitelist when OhGeezCmon and ProTV are not assigned."),
                flatlineProp.objectReferenceValue,
                typeof(UdonSharpBehaviour),
                true);
#endif

            EditorGUILayout.HelpBox(
                "Priority order (highest to lowest): OhGeezCmon \u2192 ProTV \u2192 Flatline \u2192 Manual list.\n" +
                "When an integration is assigned it drives the whitelist. Changes from higher-priority\n" +
                "systems are pushed down to lower-priority ones.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Manual Username List", EditorStyles.boldLabel);

            bool hasAnyIntegration =
                ohGeezProp.objectReferenceValue != null ||
                proTVProp.objectReferenceValue != null ||
                flatlineProp.objectReferenceValue != null;

            if (hasAnyIntegration)
            {
                EditorGUILayout.HelpBox(
                    "A third-party integration is assigned. This list is only used as a fallback " +
                    "if the controller fails to pull the whitelist from all assigned sources.",
                    MessageType.Info);
            }

            // Unity's built-in array PropertyField draws its foldout triangle
            // at the absolute left of its rect, ignoring the surrounding helpBox
            // padding. Without an explicit indent, the triangle clips outside
            // the Whitelist section and sits to the left of where "Manual
            // Username List" starts. Bumping indentLevel here shifts the whole
            // field right by one tab so the triangle lines up with the M in
            // "Manual Username List" above it.
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_so.FindProperty("authorizedUsernames"),
                new GUIContent("Authorized Usernames"), true);
            EditorGUI.indentLevel--;
        }
    }
}
#endif

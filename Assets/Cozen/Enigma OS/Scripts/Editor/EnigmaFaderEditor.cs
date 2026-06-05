#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    [CustomEditor(typeof(EnigmaFader))]
    [CanEditMultipleObjects]
    public class EnigmaFaderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Auto Assign", GUILayout.Width(100)))
            {
                foreach (var t in targets)
                {
                    EnigmaFader fader = (EnigmaFader)t;
                    AutoAssignFaderReferences(fader);
                }
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Finds and assigns child references (limiters, TMP label, indicator light,
        /// VRC Pickup, Rigidbody) from the known fader hierarchy if not already set.
        /// Called from the fader inspector button and from the controller's hardware auto-assign.
        /// </summary>
        public static void AutoAssignFaderReferences(EnigmaFader fader)
        {
            Undo.RecordObject(fader, "Auto Assign EnigmaFader");
            var so = new SerializedObject(fader);

            // The EnigmaFader component lives on the "Fader" knob child.
            // Limiters and Display are siblings (children of the same parent, e.g. "Fader 1").
            Transform faderParent = fader.transform.parent;

            // Bottom Limit — sibling of knob
            var bottomProp = so.FindProperty("bottomLimiter");
            if (bottomProp.objectReferenceValue == null && faderParent != null)
            {
                var t = faderParent.Find("Bottom Limit");
                if (t != null) bottomProp.objectReferenceValue = t.gameObject;
            }

            // Top Limit — sibling of knob
            var topProp = so.FindProperty("topLimiter");
            if (topProp.objectReferenceValue == null && faderParent != null)
            {
                var t = faderParent.Find("Top Limit");
                if (t != null) topProp.objectReferenceValue = t.gameObject;
            }

            // VRC Pickup and Rigidbody — on the same GameObject as EnigmaFader
            var pickupProp = so.FindProperty("vrcPickup");
            if (pickupProp.objectReferenceValue == null)
            {
                var pickup = fader.GetComponent<VRC.SDKBase.VRC_Pickup>();
                if (pickup != null) pickupProp.objectReferenceValue = pickup;
            }

            var rbProp = so.FindProperty("faderRigidbody");
            if (rbProp.objectReferenceValue == null)
            {
                var rb = fader.GetComponent<Rigidbody>();
                if (rb != null) rbProp.objectReferenceValue = rb;
            }

            // Indicator Renderer — sibling path: Display/Button Model/Light Ring
            if (fader.indicatorRenderer == null && faderParent != null)
            {
                var lightRing = faderParent.Find("Display/Button Model/Light Ring");
                if (lightRing != null)
                    fader.indicatorRenderer = lightRing.GetComponent<Renderer>();
            }

            // Label Text — first TextMeshPro found under sibling "Display"
            if (fader.labelText == null && faderParent != null)
            {
                var display = faderParent.Find("Display");
                if (display != null)
                {
                    for (int i = 0; i < display.childCount; i++)
                    {
                        var tmp = display.GetChild(i).GetComponent<TMPro.TextMeshPro>();
                        if (tmp != null)
                        {
                            fader.labelText = tmp;
                            break;
                        }
                    }
                }
            }

            // Hand Colliders — siblings of the fader group parent (e.g. under "Faders")
            Transform fadersContainer = faderParent != null ? faderParent.parent : null;
            var leftProp = so.FindProperty("leftHandCollider");
            if (leftProp != null && leftProp.objectReferenceValue == null && fadersContainer != null)
            {
                var t = fadersContainer.Find("Left Hand Collider");
                if (t != null) leftProp.objectReferenceValue = t.gameObject;
            }

            var rightProp = so.FindProperty("rightHandCollider");
            if (rightProp != null && rightProp.objectReferenceValue == null && fadersContainer != null)
            {
                var t = fadersContainer.Find("Right Hand Collider");
                if (t != null) rightProp.objectReferenceValue = t.gameObject;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(fader);
        }
    }
}
#endif

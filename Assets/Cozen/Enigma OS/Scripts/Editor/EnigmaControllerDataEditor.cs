#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Replaces Unity's default inspector for <see cref="EnigmaControllerData"/>
    /// with a one-line help message.
    ///
    /// Why this exists: EnigmaControllerData is the companion MonoBehaviour
    /// that backs EnigmaController's folders/entries/actions/fader-links data
    /// (~15k serialized values on a Mochie-FX-scale config). The default
    /// inspector walks the entire serialized tree on every repaint — and the
    /// Unity Inspector window draws every component on the selected
    /// GameObject, so this default rendering runs each time the user
    /// interacts with the sibling EnigmaController's custom inspector.
    ///
    /// The cost is severe: scrubbing a single FloatField on EnigmaController's
    /// inspector triggers ~1.5s of native-thread blocking PER MouseDrag tick,
    /// because each tick repaints the Inspector window and the default editor
    /// has to traverse the 15k-value tree for EnigmaControllerData. (This
    /// reproduces even when our code does NOT mutate the data component —
    /// confirmed in EnigmaPerfProbe logs where the BUFFER-only drag-defer
    /// branch still saw 1.5s `EditorApplication.update gap` entries per tick.)
    ///
    /// By overriding OnInspectorGUI with a tiny HelpBox, we collapse that
    /// per-repaint cost to ~µs. Power users can still inspect raw values via
    /// the Inspector's Debug mode if needed.
    /// </summary>
    [CustomEditor(typeof(EnigmaControllerData))]
    internal class EnigmaControllerDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            {
                EditorGUILayout.HelpBox(
                    "Storage component for the EnigmaController on this GameObject.\n" +
                    "All folders, entries, actions, and fader links are edited from the " +
                    "EnigmaController inspector above.",
                    MessageType.Info);
            }
        }
    }
}
#endif

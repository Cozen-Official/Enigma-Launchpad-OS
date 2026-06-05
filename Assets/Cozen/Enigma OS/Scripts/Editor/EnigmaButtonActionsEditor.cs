#if UNITY_EDITOR
using UnityEditor;

namespace Cozen.EnigmaOS.Editor
{
    [CustomEditor(typeof(EnigmaButtonActions))]
    internal class EnigmaButtonActionsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "This component stores action data for the Enigma Button. " +
                "Edit actions through the Enigma Button inspector above.",
                MessageType.Info);
        }
    }
}
#endif

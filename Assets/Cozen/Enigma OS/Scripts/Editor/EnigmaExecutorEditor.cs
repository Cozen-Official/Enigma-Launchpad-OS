#if UNITY_EDITOR
using UnityEditor;

namespace Cozen.EnigmaOS.Editor
{
    [CustomEditor(typeof(EnigmaExecutor))]
    internal class EnigmaExecutorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "This component stores compiled action data for the Enigma OS runtime. " +
                "It is managed automatically by the build step — do not edit manually.",
                MessageType.Info);
        }
    }
}
#endif

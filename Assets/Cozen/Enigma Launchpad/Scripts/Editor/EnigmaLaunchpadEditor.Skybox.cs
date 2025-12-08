#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty skyboxHandlerProperty;
        private SerializedObject skyboxHandlerObject;
        private SerializedProperty skyboxMaterials;
        private SerializedProperty autoChangeInterval;
        private SerializedProperty autoChangeOnByDefault;

        private void BindSkyboxHandlerSerializedObject()
        {
            skyboxHandlerObject = null;
            skyboxMaterials = null;
            autoChangeInterval = null;
            autoChangeOnByDefault = null;

            if (skyboxHandlerProperty == null || skyboxHandlerProperty.objectReferenceValue == null)
            {
                return;
            }

            skyboxHandlerObject = new SerializedObject(skyboxHandlerProperty.objectReferenceValue);
            skyboxMaterials = skyboxHandlerObject.FindProperty("skyboxMaterials");
            autoChangeInterval = skyboxHandlerObject.FindProperty("autoChangeInterval");
            autoChangeOnByDefault = skyboxHandlerObject.FindProperty("autoChangeOnByDefault");
        }

        private void EnsureSkyboxHandlerParity()
        {
            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || skyboxHandlerProperty == null)
            {
                skyboxHandlerObject = null;
                return;
            }

            int skyboxFolderIndex = GetFolderIndexForType(ToggleFolderType.Skybox);
            SkyboxHandler existing = skyboxHandlerProperty.objectReferenceValue as SkyboxHandler;

            if (skyboxFolderIndex < 0)
            {
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }

                skyboxHandlerProperty.objectReferenceValue = null;
                skyboxHandlerObject = null;
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            SkyboxHandler handler = existing;
            if (handler == null)
            {
                string handlerName = GetExpectedSkyboxHandlerName(skyboxFolderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create SkyboxHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                handler = handlerObject.AddComponent<SkyboxHandler>();
            }

            Undo.RecordObject(handler, "Configure SkyboxHandler");
            handler.launchpad = launchpad;
            handler.transform.SetParent(foldersTransform);
            if (handler.gameObject.hideFlags != HandlerHideFlags)
            {
                handler.gameObject.hideFlags = HandlerHideFlags;
            }

            // Update GameObject name to match current folder name
            string expectedName = GetExpectedSkyboxHandlerName(skyboxFolderIndex);
            if (handler.gameObject.name != expectedName)
            {
                Undo.RecordObject(handler.gameObject, "Rename SkyboxHandler");
                handler.gameObject.name = expectedName;
            }

            skyboxHandlerProperty.objectReferenceValue = handler;
        }

        private bool DrawSkyboxFolderSection(
            ToggleFolderType folderType,
            SerializedProperty exclProp,
            SerializedProperty rendererProp,
            SerializedProperty countProp,
            ref bool structural)
        {
            if (folderType != ToggleFolderType.Skybox)
            {
                return false;
            }

            if (exclProp.boolValue)
            {
                exclProp.boolValue = false;
            }

            if (rendererProp != null && rendererProp.isArray && rendererProp.arraySize > 0)
            {
                rendererProp.arraySize = 0;
            }

            GUILayout.Space(6);
            if (skyboxHandlerObject == null || skyboxMaterials == null || autoChangeInterval == null || autoChangeOnByDefault == null)
            {
                EditorGUILayout.HelpBox(
                    "Skybox handler is auto-managed; reselect the Launchpad to regenerate if missing.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.PropertyField(skyboxMaterials);
                EditorGUILayout.PropertyField(autoChangeInterval, new GUIContent("Auto Change Interval"));
                EditorGUILayout.PropertyField(autoChangeOnByDefault, new GUIContent("Auto Change On By Default"));
            }

            GUILayout.Space(6);

            if (countProp.intValue != 0)
            {
                countProp.intValue = 0;
                structural = true;
            }

            EditorGUI.indentLevel--;
            return true;
        }

        private string GetExpectedSkyboxHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"SkyboxHandler_{folderName}";
        }
    }
}
#endif

using UnityEngine;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Marker component placed on template GameObjects for the Screen Shader
    /// action type. The build step duplicates these templates and assigns
    /// materials to create per-effect GameObjects at editor time.
    ///
    /// Template GOs should be:
    ///   - A cube (or any mesh) with a MeshRenderer
    ///   - No collider
    ///   - No material assigned
    ///   - Tagged "EditorOnly"
    /// </summary>
    [AddComponentMenu("Enigma OS/Shader Template")]
    public class EnigmaShaderTemplate : MonoBehaviour
    {
        [Tooltip("Unique template number displayed in the action dropdown.")]
        public int templateNumber = 1;

        // ── Static helpers used by the action drawer and template apply window ──

        /// <summary>
        /// Finds all EnigmaShaderTemplate components in the active scene(s),
        /// sorted by templateNumber.
        /// </summary>
        public static EnigmaShaderTemplate[] FindAllInScene()
        {
            var all = Object.FindObjectsOfType<EnigmaShaderTemplate>(true);
            System.Array.Sort(all, (a, b) => a.templateNumber.CompareTo(b.templateNumber));
            return all;
        }

        /// <summary>
        /// Returns display labels for a popup/dropdown, e.g. ["Template 1", "Template 2"].
        /// </summary>
        public static string[] GetTemplateLabels(EnigmaShaderTemplate[] templates)
        {
            if (templates == null || templates.Length == 0)
                return new string[] { "(No templates in scene)" };

            var labels = new string[templates.Length];
            for (int i = 0; i < templates.Length; i++)
                labels[i] = $"Template {templates[i].templateNumber}";
            return labels;
        }

        /// <summary>
        /// Returns the popup index for a given templateNumber, or 0 if not found.
        /// </summary>
        public static int GetPopupIndex(EnigmaShaderTemplate[] templates, int templateNumber)
        {
            if (templates == null) return 0;
            for (int i = 0; i < templates.Length; i++)
                if (templates[i].templateNumber == templateNumber)
                    return i;
            return 0;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Creates a new shader template GameObject in the scene, parented under
        /// a "Shaders" container (or "Shaders (N)" for template N > 1).
        /// Returns the created EnigmaShaderTemplate component.
        /// </summary>
        public static EnigmaShaderTemplate CreateNewTemplate()
        {
            // Determine the next available template number.
            var existing = FindAllInScene();
            int nextNumber = 1;
            if (existing.Length > 0)
                nextNumber = existing[existing.Length - 1].templateNumber + 1;

            // Create or find the container parent for this template group.
            string containerName = nextNumber == 1 ? "Shaders" : $"Shaders ({nextNumber})";
            var container = new GameObject(containerName);
            UnityEditor.Undo.RegisterCreatedObjectUndo(container, "Create Shader Template");

            // Create the template cube as a child of the container.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Template";
            go.tag = "EditorOnly";
            go.transform.SetParent(container.transform, false);

            // Remove collider — not needed for screen shaders.
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            // Clear the default material.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = null;

            // Add marker component.
            var template = go.AddComponent<EnigmaShaderTemplate>();
            template.templateNumber = nextNumber;

            // Select the container in hierarchy so the user can position it.
            UnityEditor.Selection.activeGameObject = container;

            return template;
        }
#endif
    }
}

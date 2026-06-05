#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Template import/export for the EnigmaController.
    ///
    /// Templates are JSON files (EnigmaTemplateData) stored in
    ///   Assets/Cozen/Enigma OS/Templates/
    /// Each file is discovered automatically; the context menu is built from the
    /// templateName field inside each JSON file.
    ///
    /// Users can also import any external JSON file or export the current folder.
    ///
    /// Selecting a template (or finishing a JSON import) always opens
    /// <see cref="EnigmaTemplateImporterWindow"/> so the user can preview the
    /// template and choose between Overwrite and Append.
    /// </summary>
    public partial class EnigmaControllerEditor
    {
        // Path (project-relative) where built-in template JSON files live.
        private const string TEMPLATES_FOLDER = "Assets/Cozen/Enigma OS/Templates";

        // ════════════════════════════════════════════════════════════════════════
        //  TEMPLATE PICKER
        // ════════════════════════════════════════════════════════════════════════

        private void ShowTemplatePickerForFolder(EnigmaController ctrl, int folderIdx)
        {
            GenericMenu menu = new GenericMenu();

            // ── Built-in templates loaded from JSON files ──
            var templates = DiscoverTemplates();
            foreach (var t in templates)
            {
                var captured = t;
                menu.AddItem(new GUIContent(captured.name), false, () =>
                    OpenTemplatePreviewForAsset(ctrl, folderIdx, captured.asset));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Import from JSON file…"), false, () =>
                ImportFromJsonFile(ctrl, folderIdx));
            menu.AddItem(new GUIContent("Export folder to JSON…"), false, () =>
            {
                var folders = ctrl.GetFolders();
                if (folders != null && folderIdx < folders.Length)
                    ExportFolderToJson(folders[folderIdx]);
            });

            menu.ShowAsContext();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TEMPLATE DISCOVERY
        // ════════════════════════════════════════════════════════════════════════

        private struct TemplateEntry
        {
            public string    name;
            public TextAsset asset;
        }

        /// <summary>
        /// Scans <see cref="TEMPLATES_FOLDER"/> for TextAsset (.json) files and
        /// returns them sorted by their <c>templateName</c> field.
        /// </summary>
        private static TemplateEntry[] DiscoverTemplates()
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { TEMPLATES_FOLDER });
            var results = new List<TemplateEntry>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Compare(Path.GetExtension(path), ".json",
                        System.StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset == null) continue;

                // Always derive display name from the filename, not the JSON field.
                string displayName = Path.GetFileNameWithoutExtension(path);

                results.Add(new TemplateEntry { name = displayName, asset = asset });
            }

            results.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return results.ToArray();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OPEN PREVIEW WINDOW (template asset path)
        // ════════════════════════════════════════════════════════════════════════

        private static void OpenTemplatePreviewForAsset(
            EnigmaController ctrl, int folderIdx, TextAsset asset)
        {
            EnigmaFolderData[] folders = ctrl.GetFolders();
            if (folders == null || folderIdx >= folders.Length) return;

            EnigmaTemplateData tpl;
            try
            {
                tpl = JsonUtility.FromJson<EnigmaTemplateData>(asset.text);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Apply Template Failed",
                    "JSON parse error:\n" + ex.Message, "OK");
                return;
            }

            if (tpl == null || tpl.entries == null)
            {
                EditorUtility.DisplayDialog("Apply Template Failed",
                    "Template file does not contain valid Enigma template data.", "OK");
                return;
            }

            OpenPreviewWindow(ctrl, folderIdx, tpl);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OPEN PREVIEW WINDOW (shared entry point)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves the grid dimensions from <paramref name="ctrl"/> and opens
        /// <see cref="EnigmaTemplateImporterWindow"/> with Overwrite / Append callbacks.
        /// </summary>
        private static void OpenPreviewWindow(
            EnigmaController ctrl, int folderIdx, EnigmaTemplateData tpl)
        {
            EnigmaFolderData[] folders = ctrl.GetFolders();
            if (folders == null || folderIdx >= folders.Length) return;

            string folderName = folders[folderIdx].name;

            // Resolve grid dimensions from the controller.
            int cols         = Mathf.Max(1, ctrl.previewColumns);
            int rows         = Mathf.Max(1, ctrl.previewRows);
            int assignedCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
            int slotsPerPage  = assignedCount > 0 ? assignedCount : cols * rows;

            EnigmaTemplateImporterWindow.Show(
                tpl:          tpl,
                folderName:   folderName,
                cols:         cols,
                rows:         rows,
                slotsPerPage: slotsPerPage,
                onOverwrite:  (slots) => OverwriteFolderWithTemplate(ctrl, folderIdx, tpl, slots),
                onAppend:     (slots) => AppendTemplateToFolder(ctrl, folderIdx, tpl, slots),
                onNewFolder:  (slots) => ImportAsNewFolder(ctrl, tpl, slots));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OVERWRITE  (replaces all entries with template entries)
        // ════════════════════════════════════════════════════════════════════════

        private static void OverwriteFolderWithTemplate(
            EnigmaController ctrl, int folderIdx, EnigmaTemplateData tpl,
            List<TemplateRefSlot> refSlots)
        {
            EnigmaFolderData[] folders = ctrl.GetFolders();
            if (folders == null || folderIdx >= folders.Length) return;

            Undo.RecordObject(ctrl, "Apply Enigma Template");
            EnigmaFolderData folder = folders[folderIdx];

            if (!string.IsNullOrEmpty(tpl.folderName))
                folder.name = tpl.folderName;

            var list = new List<EnigmaEntryData>();
            foreach (var te in tpl.entries)
                list.Add(te.ToEntryData());
            folder.entries = list.ToArray();

            // Apply any pre-import reference assignments made in the preview window.
            EnigmaTemplateImporterWindow.ApplyRefSlotsToEntries(folder.entries, refSlots, entryOffset: 0);

            ctrl.SetFolders(folders);
            MarkDataDirtyStatic(ctrl);
            Debug.Log($"[EnigmaController] Template '{tpl.templateName}' applied to folder {folderIdx}.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  APPEND  (adds a new page and imports template entries from there)
        // ════════════════════════════════════════════════════════════════════════

        private static void AppendTemplateToFolder(
            EnigmaController ctrl, int folderIdx, EnigmaTemplateData tpl,
            List<TemplateRefSlot> refSlots)
        {
            EnigmaFolderData[] folders = ctrl.GetFolders();
            if (folders == null || folderIdx >= folders.Length) return;

            Undo.RecordObject(ctrl, "Append Enigma Template");
            EnigmaFolderData folder = folders[folderIdx];

            int assignedCount = ctrl.buttonSlots != null ? ctrl.buttonSlots.Length : 0;
            int slotsPerPage  = Mathf.Max(1, assignedCount);

            // Pad the existing entries array to the next page boundary so that
            // the imported entries start on a fresh page.
            int currentLen = folder.entries != null ? folder.entries.Length : 0;
            int paddedLen  = assignedCount > 0
                ? Mathf.CeilToInt((float)Mathf.Max(currentLen, 1) / slotsPerPage) * slotsPerPage
                : currentLen;

            var list = new List<EnigmaEntryData>(paddedLen + tpl.entries.Count);

            // Keep all existing entries.
            if (folder.entries != null)
                foreach (var e in folder.entries)
                    list.Add(e);

            // Pad with empty slots up to the page boundary.
            while (list.Count < paddedLen)
                list.Add(new EnigmaEntryData { isEmpty = true });

            // Add template entries.
            foreach (var te in tpl.entries)
                list.Add(te.ToEntryData());

            folder.entries = list.ToArray();

            // Apply any pre-import reference assignments made in the preview window.
            // Template entry 0 is now at folder.entries[paddedLen], so offset accordingly.
            EnigmaTemplateImporterWindow.ApplyRefSlotsToEntries(folder.entries, refSlots, entryOffset: paddedLen);

            ctrl.SetFolders(folders);
            MarkDataDirtyStatic(ctrl);
            Debug.Log($"[EnigmaController] Template '{tpl.templateName}' appended to folder {folderIdx}" +
                      $" (new page starts at entry {paddedLen}).");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  NEW FOLDER  (creates a new folder and imports template entries into it)
        // ════════════════════════════════════════════════════════════════════════

        private static void ImportAsNewFolder(
            EnigmaController ctrl, EnigmaTemplateData tpl,
            List<TemplateRefSlot> refSlots)
        {
            Undo.RecordObject(ctrl, "Import Enigma Template as New Folder");

            EnigmaFolderData[] folders = ctrl.GetFolders() ?? new EnigmaFolderData[0];
            var newFolder = new EnigmaFolderData
            {
                name = !string.IsNullOrEmpty(tpl.folderName) ? tpl.folderName : tpl.templateName
            };

            var list = new List<EnigmaEntryData>();
            foreach (var te in tpl.entries)
                list.Add(te.ToEntryData());
            newFolder.entries = list.ToArray();

            // Apply any pre-import reference assignments.
            EnigmaTemplateImporterWindow.ApplyRefSlotsToEntries(newFolder.entries, refSlots, entryOffset: 0);

            var folderList = new List<EnigmaFolderData>(folders);
            folderList.Add(newFolder);
            ctrl.SetFolders(folderList.ToArray());
            MarkDataDirtyStatic(ctrl);
            Debug.Log($"[EnigmaController] Template '{tpl.templateName}' imported as new folder '{newFolder.name}'.");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  JSON IMPORT (external file) — opens preview window before applying
        // ════════════════════════════════════════════════════════════════════════

        private static void ImportFromJsonFile(EnigmaController ctrl, int folderIdx)
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Enigma Folder Template", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed",
                    "Could not read file:\n" + ex.Message, "OK");
                return;
            }

            EnigmaTemplateData tpl;
            try
            {
                tpl = JsonUtility.FromJson<EnigmaTemplateData>(json);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed",
                    "JSON parse error:\n" + ex.Message, "OK");
                return;
            }

            if (tpl == null || tpl.entries == null)
            {
                EditorUtility.DisplayDialog("Import Failed",
                    "File does not contain valid Enigma template data.", "OK");
                return;
            }

            EnigmaFolderData[] folders = ctrl.GetFolders();
            if (folders == null || folderIdx >= folders.Length)
            {
                EditorUtility.DisplayDialog("Import Failed",
                    $"Folder index {folderIdx} is out of range.", "OK");
                return;
            }

            // Give the JSON file a display name if it doesn't already have one.
            if (string.IsNullOrEmpty(tpl.templateName))
                tpl.templateName = Path.GetFileNameWithoutExtension(path);

            OpenPreviewWindow(ctrl, folderIdx, tpl);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  JSON EXPORT
        // ════════════════════════════════════════════════════════════════════════

        public static void ExportFolderToJson(EnigmaFolderData folder)
        {
            // Prompt first, save dialog second. DisplayDialogComplex returns:
            //   0 = first button ("Include"), 1 = second button ("Cancel"),
            //   2 = third/alt button ("Exclude"). The "Cancel" slot is the
            //   middle one so dismissing via Esc / window-close maps to it.
            int choice = EditorUtility.DisplayDialogComplex(
                "Export Enigma Folder Template",
                "Include asset paths in the export?\n\n" +
                "• Include — targetMaterial / targetTexture / variant texture references\n" +
                "  are written as asset paths (e.g. \"Assets/Materials/Foo.mat\"). The\n" +
                "  importer will re-resolve them in a project that has the same assets\n" +
                "  at the same paths.\n\n" +
                "• Exclude — those asset fields are written as empty strings, so the\n" +
                "  template carries nothing project-specific. Users will reassign\n" +
                "  materials and textures by hand after import.\n\n" +
                "Scene references (Renderers, GameObjects, UdonBehaviours) are never\n" +
                "included regardless of this choice — they're always reassigned on\n" +
                "import.",
                "Include",
                "Cancel",
                "Exclude");
            if (choice == 1) return;
            bool includeAssetPaths = choice == 0;

            // Default the save dialogue to the built-in Templates folder so the
            // exported file is immediately visible in the Templates context menu.
            // The user can still navigate to any other location.
            // TEMPLATES_FOLDER is "Assets/Cozen/Enigma OS/Templates" — combine
            // with dataPath (which points to the Assets folder) to get an absolute path.
            string templatesFolderAbs = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..",
                             TEMPLATES_FOLDER.Replace('/', Path.DirectorySeparatorChar)));
            if (!Directory.Exists(templatesFolderAbs))
                Directory.CreateDirectory(templatesFolderAbs);

            string path = EditorUtility.SaveFilePanel(
                "Export Enigma Folder Template", templatesFolderAbs, folder.name + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var tpl = new EnigmaTemplateData { templateName = folder.name, folderName = folder.name };
            foreach (var e in folder.entries)
                tpl.entries.Add(EnigmaTemplateEntryData.FromEntryData(e, includeAssetPaths));

            string json = JsonUtility.ToJson(tpl, prettyPrint: true);
            try
            {
                File.WriteAllText(path, json);
                Debug.Log($"[EnigmaController] Exported folder '{folder.name}' to '{path}'.");

                // If the file landed inside the project, refresh AssetDatabase so it
                // appears in the Templates context menu immediately.
                string dataPath = Application.dataPath;
                string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
                if (path.StartsWith(projectRoot, System.StringComparison.OrdinalIgnoreCase))
                {
                    string assetRelative = path.Substring(projectRoot.Length)
                                              .Replace('\\', '/');
                    AssetDatabase.ImportAsset(assetRelative,
                        ImportAssetOptions.ForceUpdate);
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed",
                    "Could not write file:\n" + ex.Message, "OK");
            }
        }
    }
}
#endif

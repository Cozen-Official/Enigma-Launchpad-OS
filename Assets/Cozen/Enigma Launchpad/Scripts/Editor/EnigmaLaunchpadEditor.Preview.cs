#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        // Preview foldout key
        private const string F_Preview = "Preview";

        // Preview state
        private int previewFolderIndex;
        private int previewPageIndex;

        // Preview GUI styles
        private GUIStyle previewCellStyle;
        private GUIStyle previewSummaryLabelStyle;
        private GUIStyle previewFolderLabelStyle;

        // Preview state tracking
        private int lastPreviewFolderCount = -1;

        // Mochie Preview Constants
        private enum MochiePreviewVariant
        {
            Standard,
            X
        }

        private const string MochieStandardShaderName = "Mochie/Screen FX";
        private const string MochieXShaderName = "Mochie/Screen FX X";

        private static readonly string[][] MochiePreviewPagesStandard =
        {
            new[]
            {
                "Invert",
                "Invert+",
                "Shake",
                "Pixel\nBlur",
                "Distort",
                "Noise",
                "Scan\nLines",
                string.Empty,
                string.Empty
            },
            new[]
            {
                "-",
                "Satur",
                "+",
                "-",
                "HDR",
                "+",
                "-",
                "Fog",
                "+"
            },
            new[]
            {
                "-",
                "Bright",
                "+",
                "-",
                "Contr",
                "+",
                "Upgrade",
                "for",
                "more"
            },
            new[]
            {
                "AL\nBass",
                "AL\nMids",
                "AL\nTreble",
                "AL\nFilter",
                "AL\nShake",
                "AL\nBlur",
                "AL\nDistort",
                "AL\nNoise",
                string.Empty
            }
        };

        private static readonly string[][] MochiePreviewPagesX =
        {
            new[]
            {
                "Aura\nOutline",
                "Sobel\nOutline",
                "Sobel\nFilter",
                "Low",
                "Normal",
                "High",
                "Current\nColor",
                "Set\nColor",
                "Next\nColor"
            },
            new[]
            {
                "Invert",
                "Invert+",
                "Shake",
                "Pixel\nBlur",
                "Distort",
                "Noise",
                "Scan\nLines",
                "Depth\nBuffer",
                "Normal\nMap"
            },
            new[]
            {
                "-",
                "Satur",
                "+",
                "-",
                "Round",
                "+",
                "-",
                "Fog",
                "+"
            },
            new[]
            {
                "-",
                "Bright",
                "+",
                "-",
                "Contr",
                "+",
                "-",
                "HDR",
                "+"
            },
            new[]
            {
                "Overlay\nSlot 1",
                "Overlay\nSlot 2",
                "Overlay\nSlot 3",
                "Scan\nSlot 1",
                "Scan\nSlot 2",
                "Scan\nSlot 3",
                "AL\nBass",
                "AL\nMids",
                "AL\nTreble"
            },
            new[]
            {
                "AL\nOutline",
                "AL\nFilter",
                "AL\nShake",
                "AL\nBlur",
                "AL\nDistort",
                "AL\nNoise",
                "AL\nFog",
                "AL\nImage",
                "AL\nMisc"
            }
        };

        private void EnsurePreviewFoldoutDefault()
        {
            if (!foldouts.ContainsKey(F_Preview))
            {
                foldouts[F_Preview] = true;
            }
        }

        private void EnsurePreviewStyles()
        {
            if (previewCellStyle == null)
            {
                previewCellStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    fontSize = 11,
                    padding = new RectOffset(6, 6, 8, 8),
                    margin = new RectOffset(2, 2, 2, 2)
                };
            }

            if (previewSummaryLabelStyle == null)
            {
                previewSummaryLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            // Always ensure previewFolderLabelStyle is set - use foldoutLabelStyle if available, else EditorStyles.boldLabel
            if (previewFolderLabelStyle == null)
            {
                GUIStyle baseStyle = foldoutLabelStyle ?? EditorStyles.boldLabel;
                previewFolderLabelStyle = new GUIStyle(baseStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
            }
        }

        private void LoadPreviewState()
        {
            // Preview state is already loaded from FoldoutStateSnapshot in LoadPersistedFoldoutStates
            // Just clamp to valid values
            int folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            ClampPreviewState(folderCount);
        }

        private void SavePreviewState()
        {
            // Preview state is saved with FoldoutStateSnapshot in SavePersistedFoldoutStates
            // This method is called when navigating folders/pages to save immediately
            SavePersistedFoldoutStates();
        }

        private void DrawPreviewSection()
        {
            DrawSection(F_Preview, () =>
            {
                GUILayout.Space(InnerContentVerticalPad);
                DrawPreviewFoldout();
                GUILayout.Space(InnerContentVerticalPad);
            });
        }

        private void DrawPreviewFoldout()
        {
            EnsurePreviewStyles();

            int folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            if (folderCount <= 0)
            {
                EditorGUILayout.HelpBox("Add folders to preview launchpad labels.", MessageType.Info);
                previewFolderIndex = 0;
                previewPageIndex = 0;
                return;
            }

            ClampPreviewState(folderCount);

            int clampedFolderIndex = previewFolderIndex;
            int itemsPerPageValue = GetItemsPerPageValue();
            ToggleFolderType folderType = GetFolderType(clampedFolderIndex);
            int totalPages = CalculatePreviewPageCount(clampedFolderIndex);

            DrawPreviewFolderControls(folderCount, folderType);
            GUILayout.Space(4f);
            DrawPreviewPageControls(totalPages);
            GUILayout.Space(6f);
            DrawPreviewGrid(previewFolderIndex, folderType, itemsPerPageValue);
        }

        private void DrawPreviewFolderControls(int folderCount, ToggleFolderType folderType)
        {
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(folderCount <= 1))
            {
                if (GUILayout.Button("◀ Folder", GUILayout.Width(80f)))
                {
                    AdvancePreviewFolder(-1);
                }
            }

            GUILayout.FlexibleSpace();
            string title = GetPreviewFolderTitle(previewFolderIndex, folderType);
            // previewFolderLabelStyle is guaranteed non-null after EnsurePreviewStyles()
            GUILayout.Label(title, previewFolderLabelStyle, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(folderCount <= 1))
            {
                if (GUILayout.Button("Folder ▶", GUILayout.Width(80f)))
                {
                    AdvancePreviewFolder(1);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawPreviewPageControls(int totalPages)
        {
            int currentPage = Mathf.Clamp(previewPageIndex, 0, Mathf.Max(0, totalPages - 1));
            bool canNavigate = totalPages > 1;

            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!canNavigate))
            {
                if (GUILayout.Button("◀ Page", GUILayout.Width(80f)))
                {
                    AdvancePreviewPage(-1);
                }
            }

            GUILayout.FlexibleSpace();
            // previewSummaryLabelStyle is guaranteed non-null after EnsurePreviewStyles()
            GUILayout.Label($"Page {currentPage + 1}/{Mathf.Max(1, totalPages)}", previewSummaryLabelStyle, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!canNavigate))
            {
                if (GUILayout.Button("Page ▶", GUILayout.Width(80f)))
                {
                    AdvancePreviewPage(1);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawPreviewGrid(int folderIdx, ToggleFolderType folderType, int itemsPerPageValue)
        {
            GUIStyle containerStyle = EditorStyles.helpBox;
            GUIStyle labelStyle = previewCellStyle ?? EditorStyles.wordWrappedLabel;

            const float spacing = 4f;
            const float defaultCellSize = 64f;
            const float minCellSize = 64f;
            const float maxCellSize = 96f;
            
            // Calculate cell size based on available width, with safe defaults
            float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
            float cellSize;
            if (availableWidth > spacing * 2f)
            {
                float calculatedSize = (availableWidth - (spacing * 2f)) / 3f;
                cellSize = Mathf.Clamp(calculatedSize, minCellSize, maxCellSize);
            }
            else
            {
                cellSize = defaultCellSize;
            }

            for (int row = 0; row < 3; row++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                for (int col = 0; col < 3; col++)
                {
                    if (col > 0)
                    {
                        GUILayout.Space(spacing);
                    }

                    Rect cellRect = GUILayoutUtility.GetRect(
                        cellSize,
                        cellSize,
                        containerStyle,
                        GUILayout.Width(cellSize),
                        GUILayout.Height(cellSize));

                    GUI.Box(cellRect, GUIContent.none, containerStyle);

                    int buttonIndex = row * 3 + col;
                    string label = GetPreviewCellLabel(folderIdx, folderType, buttonIndex, itemsPerPageValue);
                    // Use zero-width space (\u200B) for empty cells to maintain consistent cell height
                    // and allow proper layout calculation even when no text is displayed
                    string display = string.IsNullOrEmpty(label) ? "\u200B" : label;

                    Rect labelRect = cellRect;
                    RectOffset padding = containerStyle.padding;
                    if (padding != null)
                    {
                        labelRect.x += padding.left;
                        labelRect.y += padding.top;
                        labelRect.width -= padding.horizontal;
                        labelRect.height -= padding.vertical;
                    }

                    if (labelRect.width > 0f && labelRect.height > 0f)
                    {
                        GUI.Label(labelRect, display, labelStyle);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (row < 2)
                {
                    GUILayout.Space(spacing);
                }
            }
        }

        private string GetPreviewFolderTitle(int folderIdx, ToggleFolderType folderType)
        {
            string name = GetFolderDisplayName(folderIdx);
            string typeLabel = GetFolderDisplayLabel(folderType);

            if (string.IsNullOrEmpty(typeLabel) || string.Equals(name, typeLabel, System.StringComparison.Ordinal))
                return name;

            return $"{name} ({typeLabel})";
        }

        private string GetPreviewCellLabel(int folderIdx, ToggleFolderType folderType, int buttonIndex, int itemsPerPageValue)
        {
            if (buttonIndex < 0 || buttonIndex >= 9)
                return string.Empty;

            if (itemsPerPageValue <= 0)
                return string.Empty;

            if (buttonIndex >= itemsPerPageValue)
                return string.Empty;

            if (folderType == ToggleFolderType.Mochie)
                return GetPreviewMochieLabel(buttonIndex);

            if (folderType == ToggleFolderType.June)
                return GetPreviewJuneLabel(folderIdx, buttonIndex);

            int localIndex = previewPageIndex * itemsPerPageValue + buttonIndex;
            int count = GetPreviewEntryCount(folderIdx);
            if (localIndex < 0 || localIndex >= count)
                return string.Empty;

            switch (folderType)
            {
                case ToggleFolderType.Skybox:
                    return GetPreviewSkyboxLabel(localIndex);
                case ToggleFolderType.Stats:
                    return GetPreviewStatsLabel(folderIdx, localIndex);
                case ToggleFolderType.Presets:
                    return GetPreviewPresetLabel(localIndex);
                case ToggleFolderType.Properties:
                    return GetPreviewPropertyLabel(folderIdx, localIndex);
                case ToggleFolderType.Materials:
                    return GetPreviewMaterialLabel(folderIdx, localIndex);
                default:
                    return GetPreviewObjectLabel(folderIdx, localIndex);
            }
        }

        private string GetPreviewPresetLabel(int localIndex)
        {
            // Preset layout: Save, Load, Delete, Preset 1, Preset 2, ...
            if (localIndex == 0)
            {
                return "Save";
            }
            if (localIndex == 1)
            {
                return "Load";
            }
            if (localIndex == 2)
            {
                return "Delete";
            }
            
            int presetIndex = localIndex - 3;
            return $"Preset\n{presetIndex + 1}";
        }

        private string GetPreviewMochieLabel(int buttonIndex)
        {
            string[][] pages = GetMochiePreviewPages();
            if (pages == null || pages.Length == 0)
                return string.Empty;

            int clampedPage = Mathf.Clamp(previewPageIndex, 0, pages.Length - 1);
            if (clampedPage < 0 || clampedPage >= pages.Length)
                return string.Empty;

            string[] page = pages[clampedPage];
            if (page == null || buttonIndex < 0 || buttonIndex >= page.Length)
                return string.Empty;

            return page[buttonIndex] ?? string.Empty;
        }

        private string[][] GetMochiePreviewPages()
        {
            switch (GetMochiePreviewVariant())
            {
                case MochiePreviewVariant.X:
                    return MochiePreviewPagesX;
                case MochiePreviewVariant.Standard:
                default:
                    return MochiePreviewPagesStandard;
            }
        }

        private string GetPreviewJuneLabel(int folderIndex, int buttonIndex)
        {
            SerializedObject handlerObject = GetJuneHandlerObjectForFolder(folderIndex);
            if (handlerObject == null)
            {
                return string.Empty;
            }

            SerializedProperty juneToggleNames = handlerObject.FindProperty("juneToggleNames");
            SerializedProperty juneToggleTypes = handlerObject.FindProperty("juneToggleTypes");

            if (juneToggleNames == null)
            {
                return string.Empty;
            }

            int itemsPerPageValue = (itemsPerPage != null) ? itemsPerPage.intValue : 9;
            int localIndex = previewPageIndex * itemsPerPageValue + buttonIndex;

            if (localIndex < 0 || localIndex >= juneToggleNames.arraySize)
            {
                return string.Empty;
            }

            string label = GetJuneToggleDisplayName(handlerObject, localIndex);
            return string.IsNullOrEmpty(label) ? $"Toggle {localIndex + 1}" : label;
        }

        private MochiePreviewVariant GetMochiePreviewVariant()
        {
            Material standardMaterial = GetMaterialFromProperty(mochieMaterialStandard);
            Material xMaterial = GetMaterialFromProperty(mochieMaterialX);

            bool usesXMaterial = UsesShader(xMaterial, MochieXShaderName) || UsesShader(standardMaterial, MochieXShaderName);
            if (usesXMaterial)
                return MochiePreviewVariant.X;

            bool usesStandardMaterial = UsesShader(standardMaterial, MochieStandardShaderName) || UsesShader(xMaterial, MochieStandardShaderName);
            if (usesStandardMaterial)
                return MochiePreviewVariant.Standard;

            if (mochieShaderXAvailable)
                return MochiePreviewVariant.X;

            if (mochieShaderStandardAvailable)
                return MochiePreviewVariant.Standard;

            return MochiePreviewVariant.Standard;
        }

        private static bool UsesShader(Material material, string shaderName)
        {
            return material != null && material.shader != null && material.shader.name == shaderName;
        }

        private static Material GetMaterialFromProperty(SerializedProperty property)
        {
            return property != null ? property.objectReferenceValue as Material : null;
        }

        private string GetPreviewSkyboxLabel(int localIndex)
        {
            if (skyboxMaterials == null || !skyboxMaterials.isArray)
                return string.Empty;

            if (localIndex < 0 || localIndex >= skyboxMaterials.arraySize)
                return string.Empty;

            SerializedProperty element = skyboxMaterials.GetArrayElementAtIndex(localIndex);
            Material material = element != null ? element.objectReferenceValue as Material : null;
            if (material == null)
                return string.Empty;

            return ButtonHandler.FormatName(FormatSkyboxName(material.name));
        }

        private string GetPreviewObjectLabel(int folderIdx, int localIndex)
        {
            SerializedObject handlerObj = GetObjectHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return string.Empty;

            SerializedProperty entriesProperty = handlerObj.FindProperty("folderEntries");
            if (entriesProperty == null || !entriesProperty.isArray)
                return string.Empty;

            if (localIndex < 0 || localIndex >= entriesProperty.arraySize)
                return string.Empty;

            SerializedProperty element = entriesProperty.GetArrayElementAtIndex(localIndex);
            UnityEngine.Object reference = element != null ? element.objectReferenceValue : null;
            if (reference == null)
                return string.Empty;

            string rawName = reference.name;
            if (string.IsNullOrEmpty(rawName))
                return string.Empty;

            return ButtonHandler.FormatName(rawName);
        }

        private string GetPreviewMaterialLabel(int folderIdx, int localIndex)
        {
            SerializedObject handlerObj = GetMaterialHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return string.Empty;

            SerializedProperty entriesProperty = handlerObj.FindProperty("folderEntries");
            if (entriesProperty == null || !entriesProperty.isArray)
                return string.Empty;

            if (localIndex < 0 || localIndex >= entriesProperty.arraySize)
                return string.Empty;

            SerializedProperty element = entriesProperty.GetArrayElementAtIndex(localIndex);
            Material material = element != null ? element.objectReferenceValue as Material : null;
            if (material == null)
                return string.Empty;

            return ButtonHandler.FormatName(material.name);
        }

        private string GetPreviewPropertyLabel(int folderIdx, int localIndex)
        {
            SerializedObject handlerObj = GetPropertyHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return string.Empty;

            SerializedProperty entriesProperty = handlerObj.FindProperty("propertyEntries");
            SerializedProperty displayNamesProperty = handlerObj.FindProperty("propertyDisplayNames");
            SerializedProperty propertyNamesProperty = handlerObj.FindProperty("propertyNames");

            if (entriesProperty == null || !entriesProperty.isArray)
                return string.Empty;

            if (localIndex < 0 || localIndex >= entriesProperty.arraySize)
                return string.Empty;

            string displayName = GetArrayString(displayNamesProperty, localIndex);

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = GetArrayString(propertyNamesProperty, localIndex);
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = GetArrayString(entriesProperty, localIndex);
            }

            if (string.IsNullOrEmpty(displayName))
                return string.Empty;

            return ButtonHandler.FormatName(displayName);
        }

        private string GetPreviewStatsLabel(int folderIdx, int localIndex)
        {
            if (statsHandlerObject == null)
                return string.Empty;

            SerializedProperty statsFolderStartIndices = statsHandlerObject.FindProperty("statsFolderStartIndices");
            SerializedProperty statsMetricsFlat = statsHandlerObject.FindProperty("statsMetricsFlat");

            if (statsFolderStartIndices == null || statsMetricsFlat == null)
                return string.Empty;

            // Get the start index for this folder
            int startIndex = GetStatsFolderStartIndex(folderIdx);
            int flatIndex = startIndex + localIndex;

            if (flatIndex < 0 || flatIndex >= statsMetricsFlat.arraySize)
                return string.Empty;

            SerializedProperty metricProp = statsMetricsFlat.GetArrayElementAtIndex(flatIndex);
            if (metricProp == null)
                return string.Empty;

            int metricValue = metricProp.enumValueIndex;
            return ButtonHandler.FormatName(GetStatsMetricLabel((WorldStatMetric)metricValue));
        }

        private string GetStatsMetricLabel(WorldStatMetric metric)
        {
            switch (metric)
            {
                case WorldStatMetric.Visits:
                    return "Visits";
                case WorldStatMetric.Favorites:
                    return "Favorites";
                case WorldStatMetric.Occupancy:
                    return "Occupancy";
                case WorldStatMetric.Popularity:
                    return "Popularity";
                case WorldStatMetric.Heat:
                    return "Heat";
                case WorldStatMetric.Players:
                    return "Players";
                case WorldStatMetric.Age:
                    return "Age";
                case WorldStatMetric.Time:
                    return "Time";
                default:
                    return metric.ToString();
            }
        }

        private static string FormatSkyboxName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return string.Empty;

            // Remove common skybox prefixes/suffixes
            string formatted = rawName;

            // Remove "Skybox_" or "skybox_" prefix
            if (formatted.StartsWith("Skybox_", System.StringComparison.OrdinalIgnoreCase))
                formatted = formatted.Substring(7);
            else if (formatted.StartsWith("Skybox", System.StringComparison.OrdinalIgnoreCase))
                formatted = formatted.Substring(6);

            // Remove " Skybox" suffix
            if (formatted.EndsWith(" Skybox", System.StringComparison.OrdinalIgnoreCase))
                formatted = formatted.Substring(0, formatted.Length - 7);
            else if (formatted.EndsWith("Skybox", System.StringComparison.OrdinalIgnoreCase))
                formatted = formatted.Substring(0, formatted.Length - 6);

            return formatted.Trim();
        }

        private void AdvancePreviewFolder(int direction)
        {
            int folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;
            if (folderCount <= 0)
                return;

            if (direction == 0)
                return;

            direction = direction > 0 ? 1 : -1;
            int newIndex = (previewFolderIndex + direction + folderCount) % folderCount;
            if (newIndex == previewFolderIndex)
                return;

            previewFolderIndex = newIndex;
            previewPageIndex = 0;
            ClampPreviewState(folderCount);
            SavePreviewState();
            Repaint();
        }

        private void AdvancePreviewPage(int direction)
        {
            int totalPages = CalculatePreviewPageCount(previewFolderIndex);
            if (totalPages <= 1)
                return;

            if (direction == 0)
                return;

            direction = direction > 0 ? 1 : -1;
            previewPageIndex = (previewPageIndex + direction + totalPages) % totalPages;
            SavePreviewState();
            Repaint();
        }

        private void ClampPreviewState(int folderCount = -1)
        {
            if (folderCount < 0)
                folderCount = (folderNamesProperty != null) ? folderNamesProperty.arraySize : 0;

            if (folderCount <= 0)
            {
                previewFolderIndex = 0;
                previewPageIndex = 0;
                return;
            }

            previewFolderIndex = Mathf.Clamp(previewFolderIndex, 0, folderCount - 1);
            int totalPages = CalculatePreviewPageCount(previewFolderIndex);
            int maxPage = Mathf.Max(0, totalPages - 1);
            previewPageIndex = Mathf.Clamp(previewPageIndex, 0, maxPage);
        }

        private int CalculatePreviewPageCount(int folderIdx)
        {
            ToggleFolderType folderType = GetFolderType(folderIdx);
            if (folderType == ToggleFolderType.Mochie)
            {
                string[][] pages = GetMochiePreviewPages();
                return (pages != null && pages.Length > 0) ? pages.Length : 1;
            }

            int perPage = GetItemsPerPageValue();
            if (perPage <= 0)
                perPage = 1;

            int count = GetPreviewEntryCount(folderIdx);
            if (count <= 0)
                return 1;

            return Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));
        }

        private int GetPreviewEntryCount(int folderIdx)
        {
            ToggleFolderType folderType = GetFolderType(folderIdx);
            switch (folderType)
            {
                case ToggleFolderType.Skybox:
                    return (skyboxMaterials != null && skyboxMaterials.isArray) ? skyboxMaterials.arraySize : 0;
                case ToggleFolderType.Mochie:
                    return 0;
                case ToggleFolderType.June:
                    return GetJuneFolderEntryCount(folderIdx);
                case ToggleFolderType.Stats:
                    return GetStatsFolderEntryCount(folderIdx);
                case ToggleFolderType.Presets:
                    return GetPresetFolderEntryCount();
                case ToggleFolderType.Properties:
                    return GetPropertyFolderEntryCount(folderIdx);
                case ToggleFolderType.Materials:
                    return GetMaterialFolderEntryCount(folderIdx);
                default:
                    return GetObjectFolderEntryCount(folderIdx);
            }
        }

        private int GetPresetFolderEntryCount()
        {
            // Entry count = 3 header entries (Save/Load/Delete) + calculated slot count based on pages
            if (presetHandlerObject == null)
            {
                return 9; // Default: 3 header + 6 slots (1 page)
            }

            SerializedProperty pagesProp = presetHandlerObject.FindProperty("presetPages");
            int pages = (pagesProp != null) ? pagesProp.intValue : 1;
            
            // Calculate slots: First page = 6, additional pages = 9 each
            int firstPageSlots = 6;
            int additionalSlots = (pages > 1) ? (pages - 1) * 9 : 0;
            int totalSlots = firstPageSlots + additionalSlots;
            
            return 3 + totalSlots;
        }

        private int GetJuneFolderEntryCount(int folderIdx)
        {
            SerializedObject handlerObj = GetJuneHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return 0;

            SerializedProperty entriesProperty = handlerObj.FindProperty("juneToggleTypes");
            return (entriesProperty != null && entriesProperty.isArray) ? entriesProperty.arraySize : 0;
        }

        private int GetObjectFolderEntryCount(int folderIdx)
        {
            SerializedObject handlerObj = GetObjectHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return 0;

            SerializedProperty entriesProperty = handlerObj.FindProperty("folderEntries");
            return (entriesProperty != null && entriesProperty.isArray) ? entriesProperty.arraySize : 0;
        }

        private int GetMaterialFolderEntryCount(int folderIdx)
        {
            SerializedObject handlerObj = GetMaterialHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return 0;

            SerializedProperty entriesProperty = handlerObj.FindProperty("folderEntries");
            return (entriesProperty != null && entriesProperty.isArray) ? entriesProperty.arraySize : 0;
        }

        private int GetPropertyFolderEntryCount(int folderIdx)
        {
            SerializedObject handlerObj = GetPropertyHandlerObjectForFolder(folderIdx);
            if (handlerObj == null)
                return 0;

            SerializedProperty entriesProperty = handlerObj.FindProperty("propertyEntries");
            return (entriesProperty != null && entriesProperty.isArray) ? entriesProperty.arraySize : 0;
        }

        private int GetStatsFolderEntryCount(int folderIdx)
        {
            if (folderEntryCountsProperty == null || folderIdx < 0 || folderIdx >= folderEntryCountsProperty.arraySize)
                return 0;

            SerializedProperty countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            return countProp != null ? countProp.intValue : 0;
        }

        private int GetItemsPerPageValue()
        {
            return (itemsPerPage != null) ? Mathf.Max(1, itemsPerPage.intValue) : 9;
        }
    }
}
#endif

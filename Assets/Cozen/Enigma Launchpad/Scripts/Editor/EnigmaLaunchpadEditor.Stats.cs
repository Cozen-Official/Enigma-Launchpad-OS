#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    public partial class EnigmaLaunchpadEditor : Editor
    {
        private SerializedProperty statsHandlerProperty;
        private SerializedObject statsHandlerObject;

        private SerializedProperty worldStatsWorldId;
        private SerializedProperty worldStatsUpdateIntervalSeconds;
        private SerializedProperty worldStatsUseThousandsSeparators;
        private SerializedProperty worldStatsAutoStart;
        private SerializedProperty worldStatsJitterFirstRequest;
        private SerializedProperty worldStatsPreserveOnError;
        private SerializedProperty worldStatsPerFetchJitterFraction;
        private SerializedProperty worldStatsEnableBackoff;
        private SerializedProperty worldStatsInitialBackoffSeconds;
        private SerializedProperty worldStatsMaxBackoffSeconds;
        private SerializedProperty worldStatsBackoffGrowthFactor;
        private SerializedProperty worldStatsOnlyBackoffOn429And5xx;
        private SerializedProperty worldStatsMetricsFlat;

        private bool statsAdvancedFoldout = false;

        private static readonly string[] WorldStatsMetricOptions =
        {
            "Visits",
            "Favorites",
            "Occupancy",
            "Popularity",
            "Heat",
            "Players",
            "Age",
            "Time",
            "VR Users",
            "Desktop Users",
            "Capacity",
            "Peak Players",
            "Instance Master",
            "Authenticated"
        };

        private const string WorldStatsApiPrefix = "https://api.vrchat.cloud/api/1/worlds/";

        private void BindStatsHandlerSerializedObject()
        {
            statsHandlerObject = null;
            worldStatsWorldId = null;
            worldStatsUpdateIntervalSeconds = null;
            worldStatsUseThousandsSeparators = null;
            worldStatsAutoStart = null;
            worldStatsJitterFirstRequest = null;
            worldStatsPreserveOnError = null;
            worldStatsPerFetchJitterFraction = null;
            worldStatsEnableBackoff = null;
            worldStatsInitialBackoffSeconds = null;
            worldStatsMaxBackoffSeconds = null;
            worldStatsBackoffGrowthFactor = null;
            worldStatsOnlyBackoffOn429And5xx = null;
            worldStatsMetricsFlat = null;

            if (statsHandlerProperty == null || statsHandlerProperty.objectReferenceValue == null)
            {
                return;
            }

            statsHandlerObject = new SerializedObject(statsHandlerProperty.objectReferenceValue);
            worldStatsWorldId = statsHandlerObject.FindProperty("worldStatsWorldId");
            worldStatsUpdateIntervalSeconds = statsHandlerObject.FindProperty("worldStatsUpdateIntervalSeconds");
            worldStatsUseThousandsSeparators = statsHandlerObject.FindProperty("worldStatsUseThousandsSeparators");
            worldStatsAutoStart = statsHandlerObject.FindProperty("worldStatsAutoStart");
            worldStatsJitterFirstRequest = statsHandlerObject.FindProperty("worldStatsJitterFirstRequest");
            worldStatsPreserveOnError = statsHandlerObject.FindProperty("worldStatsPreserveOnError");
            worldStatsPerFetchJitterFraction = statsHandlerObject.FindProperty("worldStatsPerFetchJitterFraction");
            worldStatsEnableBackoff = statsHandlerObject.FindProperty("worldStatsEnableBackoff");
            worldStatsInitialBackoffSeconds = statsHandlerObject.FindProperty("worldStatsInitialBackoffSeconds");
            worldStatsMaxBackoffSeconds = statsHandlerObject.FindProperty("worldStatsMaxBackoffSeconds");
            worldStatsBackoffGrowthFactor = statsHandlerObject.FindProperty("worldStatsBackoffGrowthFactor");
            worldStatsOnlyBackoffOn429And5xx = statsHandlerObject.FindProperty("worldStatsOnlyBackoffOn429And5xx");
            worldStatsMetricsFlat = statsHandlerObject.FindProperty("worldStatsMetricsFlat");
        }

        private void EnsureStatsHandlerParity()
        {
            EnigmaLaunchpad launchpad = target as EnigmaLaunchpad;
            if (launchpad == null || statsHandlerProperty == null)
            {
                statsHandlerObject = null;
                return;
            }

            int statsFolderIndex = GetFolderIndexForType(ToggleFolderType.Stats);
            StatsHandler existing = statsHandlerProperty.objectReferenceValue as StatsHandler;

            if (statsFolderIndex < 0)
            {
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }

                statsHandlerProperty.objectReferenceValue = null;
                statsHandlerObject = null;
                return;
            }

            Transform foldersTransform = GetFoldersTransform(launchpad);
            StatsHandler handler = existing;
            if (handler == null)
            {
                string handlerName = GetExpectedStatsHandlerName(statsFolderIndex);

                GameObject handlerObject = new GameObject(handlerName);
                Undo.RegisterCreatedObjectUndo(handlerObject, "Create StatsHandler");
                handlerObject.transform.SetParent(foldersTransform);
                handlerObject.hideFlags = HandlerHideFlags;

                handler = handlerObject.AddComponent<StatsHandler>();
            }

            Undo.RecordObject(handler, "Configure StatsHandler");
            handler.launchpad = launchpad;
            handler.transform.SetParent(foldersTransform);
            if (handler.gameObject.hideFlags != HandlerHideFlags)
            {
                handler.gameObject.hideFlags = HandlerHideFlags;
            }

            // Update GameObject name to match current folder name
            string expectedName = GetExpectedStatsHandlerName(statsFolderIndex);
            if (handler.gameObject.name != expectedName)
            {
                Undo.RecordObject(handler.gameObject, "Rename StatsHandler");
                handler.gameObject.name = expectedName;
            }

            statsHandlerProperty.objectReferenceValue = handler;
        }

        private string GetExpectedStatsHandlerName(int folderIndex)
        {
            string folderName = GetResolvedFolderName(folderIndex);
            return $"StatsHandler_{folderName}";
        }

        private bool DrawWorldStatsSection(int folderIdx, SerializedProperty countProp)
        {
            if (worldStatsMetricsFlat == null)
            {
                EditorGUILayout.HelpBox("StatsHandler missing required serialized properties.", MessageType.Error);
                return false;
            }

            EditorGUILayout.PropertyField(worldStatsWorldId, new GUIContent("VRChat World ID"));
            EditorGUILayout.PropertyField(worldStatsAutoStart);

            bool worldStatsUrlCurrent = IsWorldStatsUrlCurrent();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Final API URL", worldStatsUrlCurrent ? "(Up-to-date)" : "(Needs rebuild)");

            using (new EditorGUI.DisabledScope(worldStatsUrlCurrent))
            {
                if (GUILayout.Button("Rebuild URL", GUILayout.Width(90)))
                {
                    TriggerWorldStatsUrlRebuild();
                }
            }

            if (GUILayout.Button("Copy Final URL", GUILayout.Width(110)))
            {
                CopyWorldStatsUrl();
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            int statsStart = GetStatsFolderStartIndex(folderIdx);
            int count = countProp.intValue;
            EnsureWorldStatsArrayLength(statsStart + count);

            bool structuralChange = false;
            GUILayout.Space(6);
            EditorGUILayout.LabelField("Displayed Metrics", EditorStyles.boldLabel);

            for (int i = 0; i < count; i++)
            {
                int flatIndex = statsStart + i;
                if (flatIndex < 0 || flatIndex >= worldStatsMetricsFlat.arraySize) break;

                SerializedProperty metricProp = worldStatsMetricsFlat.GetArrayElementAtIndex(flatIndex);
                EditorGUILayout.BeginHorizontal();
                int newMetric = EditorGUILayout.Popup($"Metric {i + 1}", metricProp.enumValueIndex, WorldStatsMetricOptions);
                if (newMetric != metricProp.enumValueIndex)
                    metricProp.enumValueIndex = newMetric;

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    if (MoveStatsEntry(folderIdx, i, i - 1))
                    {
                        structuralChange = true;
                        EditorGUILayout.EndHorizontal();
                        GUI.enabled = true;
                        break;
                    }
                }
                GUI.enabled = i < count - 1;
                if (!structuralChange && GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    if (MoveStatsEntry(folderIdx, i, i + 1))
                    {
                        structuralChange = true;
                        EditorGUILayout.EndHorizontal();
                        GUI.enabled = true;
                        break;
                    }
                }
                GUI.enabled = true;
                if (!structuralChange && GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveStatsEntryAt(folderIdx, i);
                    structuralChange = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (structuralChange)
                return true;

            if (count == 0)
            {
                EditorGUILayout.HelpBox("This Stats folder has zero metrics. Add metrics or remove this folder.", MessageType.Warning);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Metric"))
            {
                if (ModifyStatsEntryCount(folderIdx, count + 1))
                    structuralChange = true;
            }
            GUI.enabled = count > 0;
            if (!structuralChange && GUILayout.Button("- Metric"))
            {
                if (ModifyStatsEntryCount(folderIdx, count - 1))
                    structuralChange = true;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (structuralChange)
                return true;

            GUILayout.Space(6);
            bool updatedAdvancedFoldout = EditorGUILayout.Foldout(statsAdvancedFoldout, "Advanced Settings", true);
            if (updatedAdvancedFoldout != statsAdvancedFoldout)
            {
                statsAdvancedFoldout = updatedAdvancedFoldout;
                SavePersistedFoldoutStates();
            }
            if (statsAdvancedFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(worldStatsUseThousandsSeparators, new GUIContent("Use Thousands Separators"));

                GUILayout.Space(4);
                EditorGUILayout.PropertyField(worldStatsJitterFirstRequest);
                EditorGUILayout.PropertyField(worldStatsPreserveOnError);

                GUILayout.Space(4);
                EditorGUILayout.PropertyField(worldStatsUpdateIntervalSeconds, new GUIContent("Update Interval Seconds"));
                EditorGUILayout.PropertyField(worldStatsPerFetchJitterFraction, new GUIContent("Per Fetch Jitter Fraction"));

                GUILayout.Space(4);
                EditorGUILayout.PropertyField(worldStatsEnableBackoff);
                if (worldStatsEnableBackoff != null && worldStatsEnableBackoff.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(worldStatsInitialBackoffSeconds, new GUIContent("Initial Backoff Seconds"));
                    EditorGUILayout.PropertyField(worldStatsMaxBackoffSeconds, new GUIContent("Max Backoff Seconds"));
                    EditorGUILayout.PropertyField(worldStatsBackoffGrowthFactor, new GUIContent("Backoff Growth Factor"));
                    EditorGUILayout.PropertyField(worldStatsOnlyBackoffOn429And5xx, new GUIContent("Only Backoff On 429/5xx"));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.HelpBox(
                "Enter only the world ID from the VRChat website (starts with wrld_).\n" +
                "Use the metric list to choose which stats appear on the launchpad.\n" +
                "The API URL is generated automatically; use 'Copy Final URL' for debugging.",
                MessageType.Info);

            return false;
        }

        private bool ModifyStatsEntryCount(int folderIdx, int newCount)
        {
            if (worldStatsMetricsFlat == null || folderEntryCountsProperty == null) return false;

            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            if (newCount < 0) newCount = 0;
            int oldCount = countProp.intValue;
            if (newCount == oldCount) return false;

            int statsStart = GetStatsFolderStartIndex(folderIdx);
            if (newCount > oldCount)
            {
                EnsureWorldStatsArrayLength(statsStart + oldCount);
                int toAdd = newCount - oldCount;
                for (int i = 0; i < toAdd; i++)
                {
                    int insertIndex = statsStart + oldCount + i;
                    worldStatsMetricsFlat.InsertArrayElementAtIndex(insertIndex);
                    var element = worldStatsMetricsFlat.GetArrayElementAtIndex(insertIndex);
                    if (element != null)
                        element.enumValueIndex = (int)WorldStatMetric.Visits;
                }
            }
            else
            {
                int toRemove = oldCount - newCount;
                RemoveStatsSegment(statsStart + newCount, toRemove);
            }

            countProp.intValue = newCount;
            return true;
        }

        private void RemoveStatsEntryAt(int folderIdx, int localIndex)
        {
            if (worldStatsMetricsFlat == null) return;
            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int count = countProp.intValue;
            if (localIndex < 0 || localIndex >= count) return;

            int statsStart = GetStatsFolderStartIndex(folderIdx);
            RemoveStatsSegment(statsStart + localIndex, 1);
            countProp.intValue = count - 1;
        }

        private void RemoveStatsSegment(int start, int length)
        {
            if (worldStatsMetricsFlat == null) return;
            if (length <= 0) return;
            for (int i = 0; i < length; i++)
            {
                if (start >= 0 && start < worldStatsMetricsFlat.arraySize)
                {
                    worldStatsMetricsFlat.DeleteArrayElementAtIndex(start);
                }
            }
        }

        private bool MoveStatsEntry(int folderIdx, int fromLocalIndex, int toLocalIndex)
        {
            if (worldStatsMetricsFlat == null) return false;
            if (fromLocalIndex == toLocalIndex) return false;

            var countProp = folderEntryCountsProperty.GetArrayElementAtIndex(folderIdx);
            int count = countProp.intValue;
            if (toLocalIndex < 0 || toLocalIndex >= count) return false;

            int start = GetStatsFolderStartIndex(folderIdx);
            int from = start + fromLocalIndex;
            int to = start + toLocalIndex;
            if (from < 0 || to < 0) return false;
            if (from >= worldStatsMetricsFlat.arraySize || to >= worldStatsMetricsFlat.arraySize) return false;

            worldStatsMetricsFlat.MoveArrayElement(from, to);
            return true;
        }

        private WorldStatMetric[] ExtractStatsSegment(int start, int count)
        {
            if (worldStatsMetricsFlat == null || count <= 0) return Array.Empty<WorldStatMetric>();
            var result = new WorldStatMetric[count];
            for (int i = 0; i < count; i++)
            {
                int idx = start + i;
                if (idx >= 0 && idx < worldStatsMetricsFlat.arraySize)
                {
                    var prop = worldStatsMetricsFlat.GetArrayElementAtIndex(idx);
                    result[i] = (WorldStatMetric)prop.enumValueIndex;
                }
                else
                {
                    result[i] = WorldStatMetric.Visits;
                }
            }
            return result;
        }

        private void InsertStatsSegment(int start, WorldStatMetric[] entries)
        {
            if (worldStatsMetricsFlat == null || entries == null || entries.Length == 0) return;
            if (start < 0) start = 0;
            if (start > worldStatsMetricsFlat.arraySize) start = worldStatsMetricsFlat.arraySize;
            for (int i = 0; i < entries.Length; i++)
            {
                worldStatsMetricsFlat.InsertArrayElementAtIndex(start + i);
                var element = worldStatsMetricsFlat.GetArrayElementAtIndex(start + i);
                if (element != null)
                    element.enumValueIndex = (int)entries[i];
            }
        }

        private int GetStatsFolderStartIndex(int folderIdx)
        {
            int start = 0;
            int totalFolders = (folderEntryCountsProperty != null) ? folderEntryCountsProperty.arraySize : 0;
            for (int i = 0; i < folderIdx && i < totalFolders; i++)
            {
                if (GetFolderType(i) != ToggleFolderType.Stats) continue;
                start += folderEntryCountsProperty.GetArrayElementAtIndex(i).intValue;
            }
            return start;
        }

        private void EnsureWorldStatsArrayLength(int requiredLength)
        {
            if (worldStatsMetricsFlat == null) return;
            if (requiredLength < 0) requiredLength = 0;
            while (worldStatsMetricsFlat.arraySize < requiredLength)
            {
                int insertIndex = worldStatsMetricsFlat.arraySize;
                worldStatsMetricsFlat.InsertArrayElementAtIndex(insertIndex);
                var element = worldStatsMetricsFlat.GetArrayElementAtIndex(insertIndex);
                if (element != null)
                    element.enumValueIndex = (int)WorldStatMetric.Visits;
            }
        }

        private bool IsWorldStatsUrlCurrent()
        {
            if (worldStatsWorldId == null || serializedObject == null) return false;
            if (worldStatsWorldId.hasMultipleDifferentValues) return false;

            string normalizedWorldId = NormalizeWorldStatsWorldId(worldStatsWorldId.stringValue);
            string expectedUrl = BuildWorldStatsApiUrl(normalizedWorldId);

            foreach (UnityEngine.Object obj in serializedObject.targetObjects)
            {
                if (obj is EnigmaLaunchpad launchpad)
                {
                    StatsHandler stats = launchpad != null ? launchpad.statsHandler : null;
                    string builtUrl = stats != null && stats.WorldStatsBuiltApiUrl != null ? stats.WorldStatsBuiltApiUrl.Get() : string.Empty;
                    if (!string.Equals(builtUrl, expectedUrl, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeWorldStatsWorldId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string trimmed = input.Trim();
            if (!trimmed.StartsWith("wrld_", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "wrld_" + trimmed;
            }
            return trimmed;
        }

        private static string BuildWorldStatsApiUrl(string normalizedWorldId)
        {
            return string.IsNullOrEmpty(normalizedWorldId) ? string.Empty : WorldStatsApiPrefix + normalizedWorldId;
        }

        private void TriggerWorldStatsUrlRebuild()
        {
            if (serializedObject == null) return;
            if (statsHandlerObject != null)
            {
                statsHandlerObject.ApplyModifiedProperties();
            }
            so.ApplyModifiedProperties();
            foreach (UnityEngine.Object obj in serializedObject.targetObjects)
            {
                if (obj is EnigmaLaunchpad launchpad)
                {
                    if (launchpad.statsHandler != null)
                    {
                        launchpad.statsHandler.EditorBuildWorldStatsApiUrl();
                    }
                }
            }
            so.Update();
        }

        private void CopyWorldStatsUrl()
        {
            if (serializedObject == null) return;
            foreach (UnityEngine.Object obj in serializedObject.targetObjects)
            {
                if (obj is EnigmaLaunchpad launchpad)
                {
                    StatsHandler stats = launchpad != null ? launchpad.statsHandler : null;
                    VRCUrl url = stats != null ? stats.WorldStatsBuiltApiUrl : null;
                    if (url != null)
                    {
                        string value = url.Get();
                        if (!string.IsNullOrEmpty(value))
                        {
                            EditorGUIUtility.systemCopyBuffer = value;
                            Debug.Log("[EnigmaLaunchpadEditor] Copied World Stats URL: " + value);
                            return;
                        }
                    }
                }
            }
            Debug.LogWarning("[EnigmaLaunchpadEditor] No built URL to copy. Enter a valid World ID first.");
        }
    }
}
#endif

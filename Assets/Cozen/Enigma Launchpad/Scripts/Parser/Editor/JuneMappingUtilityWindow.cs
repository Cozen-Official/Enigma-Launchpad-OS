#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Enigma.Editor
{
    public sealed class JuneMappingUtilityWindow : EditorWindow
    {
        private static readonly string ParserFolder = CombineUnityPath(
            "Assets",
            "Cozen",
            "Enigma Launchpad",
            "Scripts",
            "Parser");

        private static readonly string OutputPath = CombineUnityPath(ParserFolder, "JuneMapping.json");
        private static readonly string SourceEditorPath = CombineUnityPath(
            "Assets",
            "luka",
            "june",
            "june five",
            "Resources",
            "Code",
            "Tools",
            "Editor",
            "JuneUI5.cs");

        private MonoScript _sourceEditorScript;
        private TextAsset _outputJsonAsset;

        [MenuItem("Enigma/June Mapping Utility", false, 51)]
        public static void ShowWindow()
        {
            var window = GetWindow<JuneMappingUtilityWindow>(true, "June Mapping Utility");
            window.minSize = new Vector2(420f, 180f);
            window.InitializeAssets();
        }

        [MenuItem("Enigma/Regenerate June Mapping", false, 52)]
        public static void RegenerateMapping()
        {
            GenerateMapping();
        }

        private void OnEnable()
        {
            InitializeAssets();

            if (_outputJsonAsset == null && _sourceEditorScript != null)
            {
                GenerateMapping();
                _outputJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputPath);
            }
        }

        private void InitializeAssets()
        {
            _sourceEditorScript = AssetDatabase.LoadAssetAtPath<MonoScript>(SourceEditorPath);
            _outputJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Prepare a generated June mapping file by reading the June5UI editor definition.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("June5UI Editor Source", _sourceEditorScript, typeof(MonoScript), false);
                EditorGUILayout.TextField("Output File Path", OutputPath);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_sourceEditorScript == null))
            {
                if (GUILayout.Button("Generate / Regenerate Mapping"))
                {
                    GenerateMapping();
                    _outputJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputPath);
                    Repaint();
                }
            }

            using (new EditorGUI.DisabledScope(_outputJsonAsset == null && !File.Exists(OutputPath)))
            {
                if (GUILayout.Button("Ping Output Asset"))
                {
                    PingOutputAsset();
                }
            }

            if (_sourceEditorScript == null)
            {
                EditorGUILayout.HelpBox(
                    $"Unable to locate June5UI editor at '{SourceEditorPath}'. Mapping generation is disabled until it is present.",
                    MessageType.Warning);
            }

            if (_outputJsonAsset == null && !File.Exists(OutputPath))
            {
                EditorGUILayout.HelpBox(
                    "The JuneMapping.json output file has not been created yet. Generate to create a placeholder.",
                    MessageType.Info);
            }
        }

        private static void GenerateMapping()
        {
            if (AssetDatabase.LoadAssetAtPath<MonoScript>(SourceEditorPath) == null)
            {
                Debug.LogError($"Cannot generate mapping. Missing June5UI editor at '{SourceEditorPath}'.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            JuneModel mapping = BuildMapping();

            var serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Culture = CultureInfo.InvariantCulture,
                FloatFormatHandling = FloatFormatHandling.Symbol
            };

            string json = JsonConvert.SerializeObject(mapping, serializerSettings);
            File.WriteAllText(OutputPath, json);
            AssetDatabase.ImportAsset(OutputPath);

            var generatedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputPath);
            if (generatedAsset != null)
            {
                EditorGUIUtility.PingObject(generatedAsset);
            }

            int totalProperties = 0;
            int totalSections = 0;
            foreach (var module in mapping.modules)
            {
                totalProperties += module.properties.Count;
                totalSections += module.sections.Count;
            }

            Debug.Log($"Generated JuneMapping.json with {mapping.modules.Count} modules, {totalSections} sections, and {totalProperties} properties.");
        }

        private void PingOutputAsset()
        {
            if (_outputJsonAsset == null && File.Exists(OutputPath))
            {
                AssetDatabase.ImportAsset(OutputPath);
                _outputJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(OutputPath);
            }

            if (_outputJsonAsset != null)
            {
                EditorGUIUtility.PingObject(_outputJsonAsset);
            }
            else
            {
                Debug.LogWarning("JuneMapping.json has not been generated yet.");
            }
        }

        private static JuneModel BuildMapping()
        {
            return JuneRoslynMappingParser.Parse(SourceEditorPath);
        }

        private static string CombineUnityPath(params string[] parts)
        {
            return string.Join("/", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

    }
}
#endif

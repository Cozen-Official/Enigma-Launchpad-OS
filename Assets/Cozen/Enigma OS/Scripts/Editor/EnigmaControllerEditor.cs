#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Monolithic custom inspector for EnigmaController.
    /// All configuration lives here — folders, entries, hardware slots, whitelist,
    /// preview, and the build step.
    ///
    /// Organized as partial classes across multiple files:
    ///   EnigmaControllerEditor.cs          — this file: shared state, OnInspectorGUI, utilities
    ///   EnigmaControllerEditor.Folders.cs  — folder + entry drawing
    ///   EnigmaControllerEditor.Hardware.cs — button/fader reorderable lists
    ///   EnigmaControllerEditor.Build.cs    — build pipeline
    ///   EnigmaControllerEditor.Templates.cs — template import/export
    ///   EnigmaControllerEditor.Preview.cs  — inspector preview panel
    ///   EnigmaControllerEditor.Whitelist.cs — whitelist configuration
    /// </summary>
    [CustomEditor(typeof(EnigmaController))]
    public partial class EnigmaControllerEditor : UnityEditor.Editor
    {
        // ── Third-party package detection ──
        [InitializeOnLoadMethod]
        private static void UpdatePackageDefines()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var defines = currentDefines.Split(';').ToList();

            bool modified = false;

            bool hasProTV = System.Type.GetType("ArchiTech.ProTV.MediaControls, ArchiTech.ProTV.Runtime") != null;
            modified |= UpdateDefine(defines, "ENIGMAOS_PROTV", hasProTV);

            bool hasFlatline = System.Type.GetType("FlatlineSync, Assembly-CSharp") != null;
            modified |= UpdateDefine(defines, "ENIGMAOS_FLATLINE", hasFlatline);

            bool hasOhGeezCmon = System.Type.GetType("AccessControlManager, Assembly-CSharp") != null;
            modified |= UpdateDefine(defines, "ENIGMAOS_OHGEEZ", hasOhGeezCmon);

            if (modified)
            {
                string newDefines = string.Join(";", defines.Where(d => !string.IsNullOrEmpty(d)));
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);
            }
        }

        private static bool UpdateDefine(List<string> defines, string define, bool shouldExist)
        {
            bool exists = defines.Contains(define);
            if (shouldExist && !exists)
            {
                defines.Add(define);
                return true;
            }
            else if (!shouldExist && exists)
            {
                defines.Remove(define);
                return true;
            }
            return false;
        }
        // ── Serialized object ──
        private SerializedObject _so;

        // ── Foldout state (persisted via SessionState) ──
        private bool _showSettings;
        private bool _showHardware;
        private bool _showWhitelist;
        private bool _showWorldStats;
        private bool _showFaders;

        // ── Styles (initialized lazily) ──
        private GUIStyle _headerStyle;
        private GUIStyle _headerSubtitleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _entryStyle;
        private GUIStyle _previewSectionStyle;
        private GUIStyle _previewButtonStyle;
        private GUIStyle _fontButtonStyle;
        private GUIStyle _inspectorMarginStyle;
        private bool _stylesInitialized;

        // ── Build status ──
        private string _buildStatus = "";
        private bool _buildSuccess;

        // ── Branding ──
        private const string VersionFilePath = "Assets/Cozen/Enigma OS/VERSION";
        private const string EnigmaOSRootPath = "Assets/Cozen/Enigma OS";
        private const string LogoPath = "Assets/Cozen/Enigma OS/Textures/enigma white.png";
        private const string DocsIconPath = "Assets/Cozen/Enigma OS/Textures/docs_icon.png";
        private const string DiscordIconPath = "Assets/Cozen/Enigma OS/Textures/discord-white.png";
        private const string PinIconPath = "Assets/Cozen/Enigma OS/Textures/pin_icon.png";
        private const string DocumentationUrl = "https://cozen-official.github.io/Enigma-Launchpad-OS/";
        private const string DiscordUrl = "https://discord.gg/DQw3r9VJjZ";
        private const string GitHubReleasesApiUrl = "https://api.github.com/repos/Cozen-Official/Enigma-Launchpad-OS/releases";
        private const string RepoUrl = "https://github.com/Cozen-Official/Enigma-Launchpad-OS";
        private const string UnknownVersion = "Unknown";
        private string _localVersion = UnknownVersion;
        private Texture2D _logoTexture;
        private Texture2D _docsIcon;
        private Texture2D _discordIcon;
        private Texture2D _pinIcon;

        // ── Update checking ──
        private string _remoteVersion;
        private string _releaseUrl;
        private string _latestPackageDownloadUrl;
        private List<ReleaseInfo> _pendingReleases = new List<ReleaseInfo>();
        private bool _versionCheckInProgress;
        private bool _versionCheckComplete;
        private bool _updateAvailable;
        private bool _showReleaseNotes;
        private bool _isDownloading;
        private UnityWebRequest _versionCheckRequest;
        private UnityWebRequest _packageDownloadRequest;

        private struct ReleaseInfo
        {
            public string version;
            public string description;
            public string url;

            public ReleaseInfo(string version, string description, string url)
            {
                this.version = version;
                this.description = description;
                this.url = url;
            }
        }

        [Serializable] private class GitHubReleaseAsset { public string name; public string browser_download_url; }
        [Serializable] private class GitHubRelease { public string tag_name; public string body; public string html_url; public GitHubReleaseAsset[] assets; }
        [Serializable] private class GitHubReleasesWrapper { public GitHubRelease[] releases; }

        private void OnEnable()
        {
            _so = serializedObject;
            LoadLocalVersion();
            CheckForUpdates();
            _logoTexture  = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            _docsIcon     = AssetDatabase.LoadAssetAtPath<Texture2D>(DocsIconPath);
            _discordIcon  = AssetDatabase.LoadAssetAtPath<Texture2D>(DiscordIconPath);
            _pinIcon      = AssetDatabase.LoadAssetAtPath<Texture2D>(PinIconPath);
            LoadSessionState();
        }

        private void OnDisable()
        {
            if (_versionCheckRequest != null) { _versionCheckRequest.Dispose(); _versionCheckRequest = null; }
            if (_packageDownloadRequest != null) { _packageDownloadRequest.Dispose(); _packageDownloadRequest = null; }
        }

        // ── SessionState persistence ──
        // Keyed by controller instance ID so each inspector remembers its own state.
        // Survives recompile/reselect but resets when Unity restarts (clean defaults).
        private string SessionKey(string suffix) => $"EnigmaCtrl_{target.GetInstanceID()}_{suffix}";

        private void LoadSessionState()
        {
            _showSettings    = SessionState.GetBool(SessionKey("foldSettings"),  false);
            _showHardware    = SessionState.GetBool(SessionKey("foldHardware"),  false);
            _showWhitelist   = SessionState.GetBool(SessionKey("foldWhitelist"), false);
            _showWorldStats  = SessionState.GetBool(SessionKey("foldWorldStats"), false);
            _showFaders      = SessionState.GetBool(SessionKey("foldFaders"),    false);
            _previewFolderIndex      = SessionState.GetInt(SessionKey("prevFolder"), 0);
            _previewPageIndex        = SessionState.GetInt(SessionKey("prevPage"),   0);
            _selectedLocalEntryIndex = SessionState.GetInt(SessionKey("prevEntry"), -1);

            // Clamp restored indices to valid ranges in case data changed.
            var ctrl = (EnigmaController)target;
            var folders = ctrl.GetFolders();
            if (folders == null || _previewFolderIndex >= folders.Length)
                _previewFolderIndex = 0;
        }

        private void SaveFoldoutState()
        {
            SessionState.SetBool(SessionKey("foldSettings"),  _showSettings);
            SessionState.SetBool(SessionKey("foldHardware"),  _showHardware);
            SessionState.SetBool(SessionKey("foldWhitelist"), _showWhitelist);
            SessionState.SetBool(SessionKey("foldWorldStats"), _showWorldStats);
            SessionState.SetBool(SessionKey("foldFaders"),    _showFaders);
        }

        private void SavePreviewState()
        {
            SessionState.SetInt(SessionKey("prevFolder"), _previewFolderIndex);
            SessionState.SetInt(SessionKey("prevPage"),   _previewPageIndex);
            SessionState.SetInt(SessionKey("prevEntry"),  _selectedLocalEntryIndex);
        }

        private void LoadLocalVersion()
        {
            try
            {
                if (File.Exists(VersionFilePath))
                    _localVersion = File.ReadAllText(VersionFilePath).Trim();
                else
                    _localVersion = UnknownVersion;
            }
            catch
            {
                _localVersion = UnknownVersion;
            }
        }

        // ── Version checking & update system ──

        private void CheckForUpdates()
        {
            if (_versionCheckInProgress || _versionCheckComplete) return;
            _versionCheckInProgress = true;

            try
            {
                _versionCheckRequest = UnityWebRequest.Get(GitHubReleasesApiUrl);
                if (_versionCheckRequest == null) { _versionCheckInProgress = false; return; }
                _versionCheckRequest.SetRequestHeader("User-Agent", "EnigmaOS-Unity-Editor");
                var operation = _versionCheckRequest.SendWebRequest();
                operation.completed += OnVersionCheckComplete;
            }
            catch (Exception ex)
            {
                _versionCheckInProgress = false;
                Debug.LogWarning($"[Enigma OS] Error initiating version check: {ex.Message}");
            }
        }

        private void OnVersionCheckComplete(UnityEngine.AsyncOperation op)
        {
            _versionCheckInProgress = false;
            _versionCheckComplete = true;

            if (_versionCheckRequest == null) return;

            if (_versionCheckRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string wrappedJson = "{\"releases\":" + _versionCheckRequest.downloadHandler.text + "}";
                    GitHubReleasesWrapper wrapper = JsonUtility.FromJson<GitHubReleasesWrapper>(wrappedJson);

                    if (wrapper?.releases != null && wrapper.releases.Length > 0)
                    {
                        _pendingReleases.Clear();
                        _latestPackageDownloadUrl = null;
                        GitHubRelease latestRelease = null;
                        string latestVersion = null;

                        foreach (var release in wrapper.releases)
                        {
                            if (string.IsNullOrEmpty(release.tag_name)) continue;
                            string tagVersion = release.tag_name;
                            if (tagVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                                tagVersion = tagVersion.Substring(1);

                            if (CompareVersions(_localVersion, tagVersion) < 0)
                            {
                                _pendingReleases.Add(new ReleaseInfo(
                                    tagVersion,
                                    CleanReleaseDescription(release.body),
                                    release.html_url));

                                if (latestRelease == null || CompareVersions(latestVersion, tagVersion) < 0)
                                {
                                    latestRelease = release;
                                    latestVersion = tagVersion;
                                }
                            }
                        }

                        _pendingReleases.Sort((a, b) => CompareVersions(b.version, a.version));

                        if (_pendingReleases.Count > 0)
                        {
                            _remoteVersion = _pendingReleases[0].version;
                            _releaseUrl = _pendingReleases[0].url;
                            _updateAvailable = true;

                            if (latestRelease?.assets != null)
                            {
                                foreach (var asset in latestRelease.assets)
                                {
                                    if (asset.name != null && asset.name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                                    {
                                        _latestPackageDownloadUrl = asset.browser_download_url;
                                        break;
                                    }
                                }
                            }

                            Debug.Log($"[Enigma OS] Update available! Local: {_localVersion}, Latest: {_remoteVersion} ({_pendingReleases.Count} new release(s))");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Enigma OS] Error parsing version check response: {ex.Message}");
                }
            }

            _versionCheckRequest.Dispose();
            _versionCheckRequest = null;
            Repaint();
        }

        private void DownloadAndImportPackage()
        {
            if (string.IsNullOrEmpty(_latestPackageDownloadUrl) || _isDownloading) return;
            _isDownloading = true;

            try
            {
                _packageDownloadRequest = UnityWebRequest.Get(_latestPackageDownloadUrl);
                if (_packageDownloadRequest == null) { _isDownloading = false; return; }
                _packageDownloadRequest.SetRequestHeader("User-Agent", "EnigmaOS-Unity-Editor");
                var operation = _packageDownloadRequest.SendWebRequest();
                operation.completed += OnPackageDownloadComplete;
                Debug.Log($"[Enigma OS] Downloading Enigma OS v{_remoteVersion}...");
            }
            catch (Exception ex)
            {
                _isDownloading = false;
                Debug.LogError($"[Enigma OS] Error initiating download: {ex.Message}");
            }
        }

        private void OnPackageDownloadComplete(UnityEngine.AsyncOperation op)
        {
            _isDownloading = false;
            if (_packageDownloadRequest == null) return;

            if (_packageDownloadRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string safeVersion = SanitizeVersionForFilename(_remoteVersion);
                    string tempPath = Path.Combine(Path.GetTempPath(), $"EnigmaOS_v{safeVersion}.unitypackage");
                    File.WriteAllBytes(tempPath, _packageDownloadRequest.downloadHandler.data);

                    Debug.Log("[Enigma OS] Download complete. Preparing update...");

                    EditorApplication.delayCall += () =>
                    {
                        if (AssetDatabase.IsValidFolder(EnigmaOSRootPath))
                        {
                            if (!AssetDatabase.DeleteAsset(EnigmaOSRootPath))
                            {
                                Debug.LogError($"[Enigma OS] Failed to delete existing folder at {EnigmaOSRootPath}. Close any open assets or check file permissions, then run the update again.");
                                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                                catch (Exception cleanupEx) { Debug.LogWarning($"[Enigma OS] Could not clean up temp file: {cleanupEx.Message}"); }
                                return;
                            }
                            AssetDatabase.Refresh();
                        }

                        EditorApplication.delayCall += () =>
                        {
                            AssetDatabase.ImportPackage(tempPath, true);
                            EditorApplication.delayCall += () =>
                            {
                                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                                catch (Exception cleanupEx) { Debug.LogWarning($"[Enigma OS] Could not clean up temp file: {cleanupEx.Message}"); }
                            };
                        };
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Enigma OS] Error importing package: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError($"[Enigma OS] Failed to download package: {_packageDownloadRequest.error}");
            }

            _packageDownloadRequest.Dispose();
            _packageDownloadRequest = null;
            Repaint();
        }

        private string SanitizeVersionForFilename(string version)
        {
            if (string.IsNullOrEmpty(version)) return "unknown";
            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(version);
            foreach (char c in invalidChars) sb.Replace(c, '_');
            return sb.ToString();
        }

        private int CompareVersions(string version1, string version2)
        {
            if (string.IsNullOrEmpty(version1) || version1 == UnknownVersion) return 0;
            if (string.IsNullOrEmpty(version2) || version2 == UnknownVersion) return 0;

            if (decimal.TryParse(version1, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d1) &&
                decimal.TryParse(version2, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d2))
                return d1.CompareTo(d2);

            var parts1 = version1.Split('.');
            var parts2 = version2.Split('.');
            int maxLen = Math.Max(parts1.Length, parts2.Length);

            for (int i = 0; i < maxLen; i++)
            {
                int n1 = i < parts1.Length && int.TryParse(parts1[i], out int p1) ? p1 : 0;
                int n2 = i < parts2.Length && int.TryParse(parts2[i], out int p2) ? p2 : 0;
                if (n1 < n2) return -1;
                if (n1 > n2) return 1;
            }
            return 0;
        }

        private string CleanReleaseDescription(string description)
        {
            if (string.IsNullOrEmpty(description)) return string.Empty;
            string cleaned = description;
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\*\*(.+?)\*\*", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"__(.+?)__", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\*(.+?)\*", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"_(.+?)_", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"`(.+?)`", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[(.+?)\]\(.+?\)", "$1");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^#{1,6}\s*", "",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\s*[\*\-]\s+", "\u2022 ",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            return cleaned.Trim();
        }

        public override void OnInspectorGUI()
        {
            EnigmaPerfProbe.MarkInspectorStart();
            using (new EnigmaPerfProbe.PerfTrace("OnInspectorGUI TOTAL", 5.0))
            {
                using (new EnigmaPerfProbe.PerfTrace("_so.Update()"))
                    _so.Update();
                InitStyles();

                EnigmaController ctrl = (EnigmaController)target;

                DrawHeader();

                bool isPlaying = EditorApplication.isPlaying;
                if (isPlaying)
                    EditorGUILayout.HelpBox("Configuration is read-only during play mode.", MessageType.Info);

                EditorGUILayout.Space(4);

                // ── Settings ──
                _showSettings = DrawFoldout(_showSettings, "Settings", DrawSettings, isPlaying);

                EditorGUILayout.Space(4);

                // ── World Stats — only shown when the controller has any Display Stat (type 21) actions ──
                if (HasAnyDisplayStatAction(ctrl))
                {
                    _showWorldStats = DrawFoldout(_showWorldStats, "World Stats", DrawWorldStats, isPlaying);
                    EditorGUILayout.Space(4);
                }

                // ── Preview — always visible, acts as the primary navigation surface ──
                // Navigation (folder selection, page arrows, grid click-to-select) stays
                // enabled during play mode. Editing (add/delete/rename/reorder) is disabled
                // inside DrawPreview via its own isPlaying checks.
                using (new EnigmaPerfProbe.PerfTrace("DrawPreview()"))
                    DrawPreview();

                EditorGUILayout.Space(4);

                // ── Selected Button Settings — inline below preview, no foldout ──
                using (new EnigmaPerfProbe.PerfTrace("DrawSelectedButtonSettings()"))
                using (new EditorGUI.DisabledScope(isPlaying))
                    DrawSelectedButtonSettings();

                EditorGUILayout.Space(4);

                // ── Faders ──
                if (ctrl.faderSlots != null && ctrl.faderSlots.Length > 0)
                {
                    _showFaders = DrawFoldout(_showFaders, "Faders", DrawFaders, isPlaying);
                    EditorGUILayout.Space(2);
                }

                // ── Whitelist ──
                _showWhitelist = DrawFoldout(_showWhitelist, "Whitelist", DrawWhitelist, isPlaying);

                EditorGUILayout.Space(2);

                // ── Hardware ──
                _showHardware = DrawFoldout(_showHardware, "Hardware", DrawHardware, isPlaying);

                EditorGUILayout.Space(8);

                // ── Build button ──
                using (new EditorGUI.DisabledScope(isPlaying))
                    DrawBuildSection(ctrl);

                EditorGUILayout.Space(8);

                // ── Footer ──
                DrawFooter();

                using (new EnigmaPerfProbe.PerfTrace("_so.ApplyModifiedProperties()"))
                    _so.ApplyModifiedProperties();

                // Persist editor UI state so it survives reselection/recompile.
                SaveFoldoutState();
                SavePreviewState();
            }
            EnigmaPerfProbe.MarkInspectorEnd();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HEADER
        // ════════════════════════════════════════════════════════════════════════

        private new void DrawHeader()
        {
            // Colored accent bar above the header box.
            Rect accentRect = EditorGUILayout.GetControlRect(false, 3f);
            EditorGUI.DrawRect(accentRect, new Color(0.27f, 0.51f, 0.94f, 1f));

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(4);
            if (_logoTexture != null)
            {
                float inspectorWidth = EditorGUIUtility.currentViewWidth - 41f;
                float logoH = 40f;
                float logoW = logoH * ((float)_logoTexture.width / _logoTexture.height);

                // Build a style whose font size makes "OS" visually the same height as the logo
                var osStyle = new GUIStyle(_headerStyle) { fontSize = 36, fontStyle = FontStyle.Normal };
                Vector2 osSize = osStyle.CalcSize(new GUIContent("OS"));
                float totalW = logoW + osSize.x;
                float rowH = Mathf.Max(logoH, osSize.y);

                Rect rowRect = GUILayoutUtility.GetRect(totalW, rowH);
                float startX = rowRect.x + (rowRect.width - totalW) * 0.5f;

                Rect imgRect = new Rect(startX, rowRect.y + (rowH - logoH) * 0.5f, logoW, logoH);
                GUI.DrawTexture(imgRect, _logoTexture, ScaleMode.ScaleToFit);

                Rect osRect = new Rect(startX + logoW + 8f, rowRect.y, osSize.x, rowH);
                GUI.Label(osRect, "OS", osStyle);
            }
            else
            {
                EditorGUILayout.LabelField("ENIGMA OS", _headerStyle);
            }
            EditorGUILayout.LabelField("Developed by Cozen", _headerSubtitleStyle);
            EditorGUILayout.LabelField($"V{_localVersion}", _headerSubtitleStyle);
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();

            // Update notification
            if (_updateAvailable && !string.IsNullOrEmpty(_remoteVersion) &&
                !string.IsNullOrEmpty(_localVersion) && _localVersion != UnknownVersion)
            {
                GUILayout.Space(4);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(4);

                EditorGUILayout.LabelField($"Update Available: V{_remoteVersion}", EditorStyles.boldLabel);

                if (_pendingReleases.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    _showReleaseNotes = EditorGUILayout.Foldout(_showReleaseNotes, "What's New", true);
                    if (_showReleaseNotes)
                    {
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < _pendingReleases.Count; i++)
                        {
                            var release = _pendingReleases[i];
                            if (_pendingReleases.Count > 1)
                                EditorGUILayout.LabelField($"V{release.version}:", EditorStyles.boldLabel);
                            if (!string.IsNullOrEmpty(release.description))
                                EditorGUILayout.LabelField(release.description, EditorStyles.wordWrappedLabel);
                            if (i < _pendingReleases.Count - 1)
                                GUILayout.Space(8);
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (!string.IsNullOrEmpty(_latestPackageDownloadUrl))
                {
                    EditorGUI.BeginDisabledGroup(_isDownloading);
                    if (GUILayout.Button(_isDownloading ? "Downloading..." : "Download", GUILayout.Width(100)))
                        DownloadAndImportPackage();
                    EditorGUI.EndDisabledGroup();
                    GUILayout.Space(8);
                }

                if (GUILayout.Button("View on GitHub", GUILayout.Width(120)))
                    Application.OpenURL(!string.IsNullOrEmpty(_releaseUrl) ? _releaseUrl : RepoUrl);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                EditorGUILayout.EndVertical();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  FOOTER
        // ════════════════════════════════════════════════════════════════════════

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            if (DrawIconButton(_docsIcon, "Documentation", 24))
                Application.OpenURL(DocumentationUrl);
            GUILayout.Space(4);
            if (DrawIconButton(_discordIcon, "Join the Discord", 24))
                Application.OpenURL(DiscordUrl);
            GUILayout.EndHorizontal();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SETTINGS (shared with partial files via _so)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSettings()
        {
            EnigmaController ctrl = (EnigmaController)target;

            // Default Folder — Popup of configured folder names.
            EnigmaFolderData[] folders = ctrl.GetFolders() ?? new EnigmaFolderData[0];
            SerializedProperty defFolderProp = _so.FindProperty("defaultFolderIndex");
            if (folders.Length > 0)
            {
                string[] folderNames = BuildUniqueFolderNames(folders);
                int clamped = Mathf.Clamp(defFolderProp.intValue, 0, folders.Length - 1);
                int newIdx  = EditorGUILayout.Popup("Default Folder", clamped, folderNames);
                if (newIdx != defFolderProp.intValue)
                    defFolderProp.intValue = newIdx;
            }
            else
            {
                EditorGUILayout.LabelField("Default Folder", "No folders configured");
            }

            EditorGUILayout.PropertyField(_so.FindProperty("activeColor"),
                new GUIContent("Active Color"));
            EditorGUILayout.PropertyField(_so.FindProperty("inactiveColor"),
                new GUIContent("Inactive Color"));
            EditorGUILayout.PropertyField(_so.FindProperty("debugLogging"),
                new GUIContent("Debug Logging", "Prints verbose logs for networking, state changes, and action execution."));

            // Layout — only shown when button slots are assigned.
            EnigmaController ctrl2 = (EnigmaController)target;
            int assignedCount = ctrl2.buttonSlots != null ? ctrl2.buttonSlots.Length : 0;
            if (assignedCount > 0)
            {
                RebuildPresetsIfNeeded(assignedCount);

                SerializedProperty colsProp2 = _so.FindProperty("previewColumns");
                SerializedProperty rowsProp2 = _so.FindProperty("previewRows");
                int currentCols = Mathf.Max(1, colsProp2.intValue);
                int currentRows = Mathf.Max(1, rowsProp2.intValue);

                int matchedPreset = _defaultPresetIndex;
                for (int p = 0; p < _validPresetRows.Length; p++)
                {
                    if (_validPresetRows[p] == currentRows && _validPresetCols[p] == currentCols)
                    {
                        matchedPreset = p;
                        break;
                    }
                }

                int chosenPreset = EditorGUILayout.Popup(
                    new GUIContent("Layout (Rows \u00d7 Columns)"), matchedPreset, _validPresetNames);

                if (chosenPreset != matchedPreset
                    || colsProp2.intValue != _validPresetCols[chosenPreset]
                    || rowsProp2.intValue != _validPresetRows[chosenPreset])
                {
                    colsProp2.intValue = _validPresetCols[chosenPreset];
                    rowsProp2.intValue = _validPresetRows[chosenPreset];
                    _so.ApplyModifiedProperties();
                }
            }

        }

        // ════════════════════════════════════════════════════════════════════════
        //  BUILD SECTION
        // ════════════════════════════════════════════════════════════════════════

        // Called by EnigmaPlayModeHook and EnigmaBuildValidator during pre-build and play-mode entry.
        // Static so it can be invoked directly without creating an editor instance.
        public static void RunBuild(EnigmaController ctrl)
        {
            var so = new SerializedObject(ctrl);
            BuildRuntimeArrays(so, ctrl);
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EnigmaPlayModeHook.ApplyDefaultMaterialStateForController(ctrl);

            // Sync Mochie SFX keywords to property values (mirror Mochie's
            // ScreenFXEditor.ApplyMaterialSettings). Without this, master toggles
            // are at 0 from BuildRuntimeArrays' ApplyMaterialFixups pass but
            // section keywords stay enabled — and any keyword-only-gated shader
            // path (e.g. ApplyColor) renders unconditionally with default
            // property values, producing the visible "grey" overlay until the
            // user clicks the material in the inspector to trigger Mochie's
            // own sync.
            //
            // Runs for play-mode entry too: EnigmaController.Initialize only
            // writes property values, never keyword state, so without this pass
            // the runtime preview starts in the same bad state.
            //
            // Skipped during VRC SDK builds — EnigmaBuildValidator.OnPostprocessBuild
            // runs the same sync after variant stripping completes so the build
            // still ships the variants Enigma's runtime executor needs.
            if (!IsActiveVrcBuild())
                SyncMochieKeywordsForController(ctrl);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Mochie SFX keyword sync helpers
        // ════════════════════════════════════════════════════════════════════════

        private static bool IsActiveVrcBuild()
        {
            return UnityEditor.BuildPipeline.isBuildingPlayer
                   || EnigmaBuildValidator.IsVrcBuildInProgress();
        }

        internal static void SyncMochieKeywordsForController(EnigmaController ctrl)
        {
            if (ctrl == null) return;
            var folders = ctrl.GetFolders();
            if (folders == null) return;
            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (var f in folders)
            {
                if (f?.entries == null) continue;
                foreach (var entry in f.entries)
                {
                    if (entry == null || entry.actions == null) continue;
                    foreach (var act in entry.actions)
                    {
                        if (act == null || act.targetRenderer == null) continue;
                        var mats = act.targetRenderer.sharedMaterials;
                        int mi = act.materialIndex;
                        if (mats == null || mi < 0 || mi >= mats.Length) continue;
                        var mat = mats[mi];
                        if (mat != null && seen.Add(mat))
                            EnigmaShaderHelper.SyncMochieKeywordsToValues(mat);
                    }
                }
            }
        }

        private void DrawBuildSection(EnigmaController ctrl)
        {
            if (!string.IsNullOrEmpty(_buildStatus))
            {
                Color prev = GUI.color;
                GUI.color = _buildSuccess ? Color.green : Color.red;
                EditorGUILayout.LabelField(_buildStatus, EditorStyles.wordWrappedLabel);
                GUI.color = prev;
            }

            if (GUILayout.Button("Build (Runs on build and play mode entry)", _fontButtonStyle, GUILayout.Height(24)))
            {
                try
                {
                    BuildRuntimeArrays(_so, ctrl);
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        EnigmaPlayModeHook.ApplyDefaultMaterialStateForController(ctrl);
                    _buildStatus  = "✓ Build succeeded.";
                    _buildSuccess = true;
                }
                catch (System.Exception ex)
                {
                    _buildStatus  = "✗ Build failed: " + ex.Message;
                    _buildSuccess = false;
                    Debug.LogException(ex);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws a button with a small icon (16×16) to the left of the label text.
        /// Uses the standard button background with our custom font for the label.
        /// Returns true when clicked.
        /// </summary>
        private bool DrawIconButton(Texture2D icon, string label, float height)
        {
            Rect btnRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button,
                GUILayout.Height(height), GUILayout.ExpandWidth(true));

            // Draw the standard button background; returns true on click
            bool clicked = GUI.Button(btnRect, GUIContent.none, GUI.skin.button);

            const float iconSize = 14f;
            const float gap      = 4f;
            float textW  = _fontButtonStyle.CalcSize(new GUIContent(label)).x;
            float totalW = (icon != null ? iconSize + gap : 0f) + textW;
            float startX = btnRect.x + (btnRect.width - totalW) * 0.5f;
            float iconY  = btnRect.y + (btnRect.height - iconSize) * 0.5f;

            // Clip all content to the button rect so text doesn't overflow.
            GUI.BeginGroup(btnRect);
            float localStartX = (btnRect.width - totalW) * 0.5f;
            float localIconY  = (btnRect.height - iconSize) * 0.5f;

            if (icon != null)
                GUI.DrawTexture(new Rect(localStartX, localIconY, iconSize, iconSize), icon, ScaleMode.ScaleToFit);

            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                font      = _fontButtonStyle.font,
                fontSize  = _fontButtonStyle.fontSize,
                fontStyle = _fontButtonStyle.fontStyle,
                alignment = TextAnchor.MiddleLeft,
                clipping  = TextClipping.Clip,
            };
            float labelX = localStartX + (icon != null ? iconSize + gap : 0f);
            float labelW = Mathf.Min(textW, btnRect.width - labelX);
            Rect labelRect = new Rect(labelX, 0f, labelW, btnRect.height);
            GUI.Label(labelRect, label, labelStyle);
            GUI.EndGroup();

            return clicked;
        }

        private static GUIStyle _foldoutBarStyle;

        private bool DrawFoldout(bool state, string label, System.Action drawContent, bool disableContent = false)
        {
            // Lazy-init the bar style based on ShurikenModuleTitle
            if (_foldoutBarStyle == null)
            {
                _foldoutBarStyle = new GUIStyle("ShurikenModuleTitle")
                {
                    font          = LoadEnigmaFont(),
                    fontSize      = 15,
                    fontStyle     = FontStyle.Normal,
                    alignment     = TextAnchor.MiddleCenter,
                    fixedHeight   = 24,
                    contentOffset = new Vector2(0, -2),
                    border        = new RectOffset(15, 7, 4, 4),
                };
                _foldoutBarStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.85f, 0.85f, 0.85f)
                    : new Color(0.1f, 0.1f, 0.1f);
            }

            // Draw the bar
            Rect barRect = GUILayoutUtility.GetRect(16f, _foldoutBarStyle.fixedHeight, _foldoutBarStyle);
            GUI.Box(barRect, label, _foldoutBarStyle);

            // Handle click
            Event e = Event.current;
            if (e.type == EventType.MouseDown && barRect.Contains(e.mousePosition))
            {
                state = !state;
                e.Use();
            }

            // Draw foldout arrow on the left side (only during repaint)
            if (e.type == EventType.Repaint)
            {
                Rect arrowRect = new Rect(barRect.x + 4f, barRect.y + 2f, 13f, 13f);
                EditorStyles.foldout.Draw(arrowRect, false, false, state, false);
            }

            if (state)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                using (new EditorGUI.DisabledScope(disableContent))
                    drawContent();
                EditorGUILayout.EndVertical();
            }
            return state;
        }

        private static GUIStyle _subFoldoutBarStyle;

        private bool DrawSubFoldout(bool state, string label)
        {
            // Lazy-init a smaller sub-foldout bar style
            if (_subFoldoutBarStyle == null)
            {
                _subFoldoutBarStyle = new GUIStyle("ShurikenModuleTitle")
                {
                    font          = LoadEnigmaFont(),
                    fontSize      = 13,
                    fontStyle     = FontStyle.Normal,
                    alignment     = TextAnchor.MiddleCenter,
                    fixedHeight   = 20,
                    contentOffset = new Vector2(0, -2),
                    border        = new RectOffset(15, 7, 4, 4),
                };
                _subFoldoutBarStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.75f, 0.75f, 0.75f)
                    : new Color(0.15f, 0.15f, 0.15f);
            }

            Rect barRect = GUILayoutUtility.GetRect(16f, _subFoldoutBarStyle.fixedHeight, _subFoldoutBarStyle);
            GUI.Box(barRect, label, _subFoldoutBarStyle);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && barRect.Contains(e.mousePosition))
            {
                state = !state;
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                Rect arrowRect = new Rect(barRect.x + 4f, barRect.y + 2f, 13f, 13f);
                EditorStyles.foldout.Draw(arrowRect, false, false, state, false);
            }

            return state;
        }

        /// <summary>
        /// Adds a thin space (U+2009) between each character to simulate letter-spacing.
        /// </summary>
        private static string AddLetterSpacing(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2) return text;
            var sb = new System.Text.StringBuilder(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append(text[i]);
                if (i < text.Length - 1)
                    sb.Append('\u2009'); // thin space
            }
            return sb.ToString();
        }

        private const string EnigmaFontRegularPath  = "Assets/Cozen/Enigma OS/Fonts/MonumentExtended-Regular.otf";
        private const string EnigmaFontBoldPath     = "Assets/Cozen/Enigma OS/Fonts/MonumentExtended-Ultrabold.otf";
        private static Font _enigmaFontRegular;
        private static Font _enigmaFontBold;

        private static Font LoadEnigmaFont()
        {
            if (_enigmaFontRegular == null)
            {
                _enigmaFontRegular = AssetDatabase.LoadAssetAtPath<Font>(EnigmaFontRegularPath);
                _foldoutBarStyle = null;
                _subFoldoutBarStyle = null;
            }
            return _enigmaFontRegular;
        }

        private static Font LoadEnigmaFontBold()
        {
            if (_enigmaFontBold == null)
                _enigmaFontBold = AssetDatabase.LoadAssetAtPath<Font>(EnigmaFontBoldPath);
            return _enigmaFontBold;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            Font fontRegular = LoadEnigmaFont();
            Font fontBold    = LoadEnigmaFontBold();

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                font      = fontBold,
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 21,
                fontStyle = FontStyle.Normal
            };
            _headerSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                font      = fontRegular,
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 11,
                fontStyle = FontStyle.Italic
            };
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { font = fontRegular, fontStyle = FontStyle.Normal };
            _entryStyle   = new GUIStyle(EditorStyles.helpBox);
            _previewSectionStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 6, 6)
            };
            _previewButtonStyle = new GUIStyle
            {
                font      = fontRegular,
                fontSize  = 15,
                alignment = TextAnchor.MiddleCenter,
                clipping  = TextClipping.Clip,
                wordWrap  = true,
                fontStyle = FontStyle.Normal,
            };
            _previewButtonStyle.normal.textColor  = Color.white;
            _previewButtonStyle.hover.textColor   = Color.white;
            _previewButtonStyle.active.textColor  = Color.white;
            _previewButtonStyle.focused.textColor = Color.white;
            _fontButtonStyle = new GUIStyle(GUI.skin.button)
            {
                font      = fontRegular,
                fontSize  = 12,
                fontStyle = FontStyle.Normal,
            };
            _stylesInitialized = true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  WORLD STATS SECTION
        // ════════════════════════════════════════════════════════════════════════

        private const string WorldStatsApiPrefix = "https://api.vrchat.cloud/api/1/worlds/";

        /// <summary>
        /// Returns true when any folder entry has at least one Display Stat (type 21) action.
        /// Used to conditionally show the World Stats section in the inspector.
        /// </summary>
        private static bool HasAnyDisplayStatAction(EnigmaController ctrl)
        {
            var folders = ctrl.GetFolders();
            if (folders == null) return false;
            foreach (var f in folders)
            {
                if (f.entries == null) continue;
                foreach (var e in f.entries)
                {
                    if (e.actions == null) continue;
                    foreach (var a in e.actions)
                    {
                        if (a.actionType == 21) return true;
                    }
                }
            }
            return false;
        }

        private void DrawWorldStats()
        {
            EnigmaController ctrl = (EnigmaController)target;

            EditorGUILayout.HelpBox(
                "Configure the VRChat API connection used by Display Stat buttons.\n" +
                "Local metrics (Players, Time, Age, etc.) work without a World ID.\n" +
                "API metrics (Visits, Favorites, Occupancy, etc.) require the world ID below.",
                MessageType.Info);

            SerializedProperty worldIdProp = _so.FindProperty("worldStatsWorldId");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(worldIdProp, new GUIContent("VRChat World ID",
                "The world ID from the VRChat website (starts with wrld_)."));
            if (EditorGUI.EndChangeCheck())
                _so.ApplyModifiedProperties();

            bool urlCurrent = IsWorldStatsUrlCurrent(ctrl);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API URL", urlCurrent ? "(Up-to-date)" : "(Needs rebuild)");
            using (new EditorGUI.DisabledScope(urlCurrent))
            {
                if (GUILayout.Button("Rebuild URL", GUILayout.Width(90)))
                    RebuildWorldStatsUrl(ctrl);
            }
            if (GUILayout.Button("Copy URL", GUILayout.Width(70)))
            {
                string url = ctrl.worldStatsBuiltApiUrl != null ? ctrl.worldStatsBuiltApiUrl.Get() : "";
                if (!string.IsNullOrEmpty(url))
                {
                    EditorGUIUtility.systemCopyBuffer = url;
                    Debug.Log("[EnigmaController] Copied World Stats URL: " + url);
                }
                else
                    Debug.LogWarning("[EnigmaController] No URL to copy — enter a World ID and rebuild.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_so.FindProperty("worldStatsAutoStart"),
                new GUIContent("Auto-Start", "Begin polling when the scene starts."));
            EditorGUILayout.PropertyField(_so.FindProperty("worldStatsUseThousandsSeparators"),
                new GUIContent("Thousands Separators", "Show 1,234 instead of 1234."));
            EditorGUILayout.PropertyField(_so.FindProperty("worldStatsPreserveOnError"),
                new GUIContent("Preserve On Error", "Keep last known values on API errors."));

            SerializedProperty intervalProp = _so.FindProperty("worldStatsUpdateInterval");
            float interval = Mathf.Clamp(intervalProp.floatValue, 30f, 300f);
            float newInterval = EditorGUILayout.Slider("Update Interval (sec)", interval, 30f, 300f);
            if (!Mathf.Approximately(newInterval, interval))
                intervalProp.floatValue = newInterval;
        }

        private static bool IsWorldStatsUrlCurrent(EnigmaController ctrl)
        {
            if (ctrl == null) return false;
            string id = NormalizeWorldId(ctrl.worldStatsWorldId);
            string expected = string.IsNullOrEmpty(id) ? "" : WorldStatsApiPrefix + id;
            string current = "";
            try { current = ctrl.worldStatsBuiltApiUrl != null ? ctrl.worldStatsBuiltApiUrl.Get() ?? "" : ""; }
            catch { current = ""; }
            return string.Equals(expected, current, System.StringComparison.Ordinal);
        }

        private void RebuildWorldStatsUrl(EnigmaController ctrl)
        {
            _so.ApplyModifiedProperties();
            // Normalize the world ID first, then let the runtime method build the VRCUrl.
            string id = NormalizeWorldId(ctrl.worldStatsWorldId);
            ctrl.worldStatsWorldId = id;
            ctrl.EditorBuildWorldStatsApiUrl();
            _so.Update();
        }

        private static string NormalizeWorldId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string t = input.Trim();
            if (!t.StartsWith("wrld_", System.StringComparison.OrdinalIgnoreCase))
                t = "wrld_" + t;
            return t;
        }

        /// <summary>
        /// Builds a display-name array for <paramref name="folders"/> where duplicate raw
        /// names are made unique by appending " (2)", " (3)", etc.
        /// Unity's <see cref="EditorGUILayout.Popup"/> collapses identical entries into one,
        /// so every element must be distinct when two or more folders share the same name.
        /// </summary>
        internal static string[] BuildUniqueFolderNames(EnigmaFolderData[] folders)
        {
            var names     = new string[folders.Length];
            var seenCounts = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < folders.Length; i++)
            {
                string raw = folders[i].name;
                if (!seenCounts.TryGetValue(raw, out int count))
                    count = 0;
                seenCounts[raw] = count + 1;
                names[i] = count == 0 ? raw : $"{raw} ({count + 1})";
            }
            return names;
        }
    }
}
#endif

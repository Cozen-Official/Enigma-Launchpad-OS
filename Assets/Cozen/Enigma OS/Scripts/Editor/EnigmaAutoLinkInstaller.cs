#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Ensures lackofbindings/AutoLink is present in the project. Enigma OS's
    /// default prefabs (e.g. <c>Enigma Mixer.prefab</c> has a child GameObject
    /// with an <c>AutoLink</c> UdonSharpBehaviour) depend on the AutoLink
    /// type being resolvable at load. In a project where AutoLink was never
    /// installed the prefab instances have broken Udon references and the
    /// runtime throws NREs / bounds exceptions on scene enter — symptoms
    /// identical to the ProTV case we saw (private fields stripped from the
    /// Udon heap).
    ///
    /// On Unity startup this installer runs once per session:
    /// <list type="number">
    /// <item>Detect AutoLink via reflection (works whether it was imported as
    /// a UPM package at <c>Packages/com.lackofbindings.autolink</c> or as a
    /// <c>.unitypackage</c> into <c>Assets/</c>).</item>
    /// <item>If missing AND the user hasn't permanently dismissed, show a
    /// three-button dialog: <b>Install Now</b> / <b>Not Now</b> /
    /// <b>Never Remind Me</b>.</item>
    /// <item>On <b>Install Now</b>: fetch the latest release from
    /// <c>https://api.github.com/repos/lackofbindings/AutoLink/releases/latest</c>,
    /// download the first <c>.unitypackage</c> asset, and import via
    /// <c>AssetDatabase.ImportPackage(…, interactive: true)</c> — the same
    /// flow Enigma OS's self-updater uses (see
    /// <see cref="EnigmaControllerEditor"/>).</item>
    /// </list>
    ///
    /// Dismissal semantics:
    /// <list type="bullet">
    /// <item><c>Not Now</c> — <see cref="SessionState"/> flag, re-asks on
    /// next Unity session.</item>
    /// <item><c>Never Remind Me</c> — <see cref="EditorPrefs"/> flag,
    /// permanent per user. User can reset via the Project Settings wipe or
    /// manually via the "EnigmaOS.AutoLinkInstaller.NeverAsk" key.</item>
    /// </list>
    ///
    /// Batch mode (CI / automated builds) is skipped entirely — those should
    /// already have the dependency installed via upm manifest or project
    /// template, and a blocking dialog would stall the pipeline.
    /// </summary>
    internal static class EnigmaAutoLinkInstaller
    {
        private const string GitHubReleasesApiUrl =
            "https://api.github.com/repos/lackofbindings/AutoLink/releases/latest";
        private const string RepoUrl =
            "https://github.com/lackofbindings/AutoLink";

        // EditorPrefs key for the permanent "Never Remind Me" dismissal.
        // Survives across Unity sessions and project opens.
        private const string NeverAskKey = "EnigmaOS.AutoLinkInstaller.NeverAsk";

        // SessionState key for the one-session "Not Now" dismissal.
        // Survives domain reloads within a session but resets on editor quit.
        private const string SessionDismissKey = "EnigmaOS.AutoLinkInstaller.SessionDismissed";

        // Held at static scope while the async webrequests are in flight so
        // we can Dispose them reliably even if the callback chain aborts.
        private static UnityWebRequest _versionRequest;
        private static UnityWebRequest _downloadRequest;

        [Serializable] private class GitHubReleaseAsset
        {
            public string name;
            public string browser_download_url;
        }

        [Serializable] private class GitHubRelease
        {
            public string tag_name;
            public string html_url;
            public GitHubReleaseAsset[] assets;
        }

        [InitializeOnLoadMethod]
        private static void CheckForAutoLinkOnLoad()
        {
            // CI / headless builds should never see a modal dialog.
            if (Application.isBatchMode) return;

            if (EditorPrefs.GetBool(NeverAskKey, false)) return;
            if (SessionState.GetBool(SessionDismissKey, false)) return;

            if (IsAutoLinkPresent()) return;

            // Delay one frame so the prompt doesn't fight Unity's startup
            // splash / asset import warm-up. DisplayDialog during the initial
            // domain-reload window can be suppressed or behave oddly.
            EditorApplication.delayCall += PromptInstall;
        }

        /// <summary>
        /// Detect AutoLink by reflecting every loaded assembly for the
        /// <c>AutoLink</c> type (global namespace, as declared in
        /// <c>com.lackofbindings.autolink/Runtime/AutoLink.cs</c>). Type
        /// detection works across both installation paths:
        /// <list type="bullet">
        /// <item>UPM package at <c>Packages/com.lackofbindings.autolink</c> —
        /// resolved via its <c>.asmdef</c> assembly.</item>
        /// <item>Assets-imported copy — resolved via <c>Assembly-CSharp</c>.</item>
        /// </list>
        /// </summary>
        private static bool IsAutoLinkPresent()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // Signature: (name, throwOnError, ignoreCase).
                    if (asm.GetType("AutoLink", false, false) != null) return true;
                }
                catch
                {
                    // Some dynamic assemblies throw on GetType; skip them.
                }
            }
            return false;
        }

        private static void PromptInstall()
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Enigma OS — AutoLink Required",
                "Enigma OS depends on lackofbindings/AutoLink for its default Mixer " +
                "prefab and Udon auto-wiring. AutoLink isn't installed in this " +
                "project — without it, Enigma prefabs will have broken Udon " +
                "references and the Udon VM will throw runtime errors on scene " +
                "enter.\n\n" +
                "Download and install the latest AutoLink release from GitHub now?\n\n" +
                RepoUrl,
                "Install Now",        // option 0
                "Not Now",            // option 1
                "Never Remind Me");   // option 2

            switch (choice)
            {
                case 0:
                    BeginFetchLatestRelease();
                    break;
                case 1:
                    SessionState.SetBool(SessionDismissKey, true);
                    Debug.Log("[Enigma OS] AutoLink install deferred for this session.");
                    break;
                case 2:
                    EditorPrefs.SetBool(NeverAskKey, true);
                    Debug.Log(
                        "[Enigma OS] AutoLink install prompt permanently dismissed. " +
                        $"To re-enable, clear the '{NeverAskKey}' EditorPrefs key.");
                    break;
            }
        }

        private static void BeginFetchLatestRelease()
        {
            try
            {
                _versionRequest = UnityWebRequest.Get(GitHubReleasesApiUrl);
                if (_versionRequest == null) return;
                _versionRequest.SetRequestHeader("User-Agent", "EnigmaOS-Unity-Editor");
                var op = _versionRequest.SendWebRequest();
                op.completed += OnVersionRequestComplete;
                Debug.Log("[Enigma OS] Fetching latest AutoLink release info...");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Enigma OS] Could not start AutoLink release fetch: {ex.Message}");
                _versionRequest?.Dispose();
                _versionRequest = null;
            }
        }

        private static void OnVersionRequestComplete(AsyncOperation op)
        {
            try
            {
                if (_versionRequest == null) return;

                if (_versionRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[Enigma OS] Failed to fetch AutoLink release info: " +
                        $"{_versionRequest.error}. Install manually from {RepoUrl}/releases");
                    return;
                }

                GitHubRelease release;
                try
                {
                    release = JsonUtility.FromJson<GitHubRelease>(_versionRequest.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Enigma OS] Could not parse AutoLink release JSON: {ex.Message}");
                    return;
                }

                if (release == null || release.assets == null || release.assets.Length == 0)
                {
                    Debug.LogError(
                        $"[Enigma OS] AutoLink's latest release has no downloadable assets. " +
                        $"Install manually from {RepoUrl}/releases");
                    return;
                }

                string packageUrl = null;
                foreach (var asset in release.assets)
                {
                    if (asset?.name != null &&
                        asset.name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                    {
                        packageUrl = asset.browser_download_url;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(packageUrl))
                {
                    Debug.LogError(
                        $"[Enigma OS] AutoLink release '{release.tag_name}' has no .unitypackage " +
                        $"asset. Install manually from {RepoUrl}/releases");
                    return;
                }

                Debug.Log($"[Enigma OS] Downloading AutoLink {release.tag_name}...");
                BeginDownloadPackage(packageUrl, release.tag_name);
            }
            finally
            {
                _versionRequest?.Dispose();
                _versionRequest = null;
            }
        }

        private static void BeginDownloadPackage(string url, string versionTag)
        {
            try
            {
                _downloadRequest = UnityWebRequest.Get(url);
                if (_downloadRequest == null) return;
                _downloadRequest.SetRequestHeader("User-Agent", "EnigmaOS-Unity-Editor");
                // Stash the version tag so OnDownloadComplete can name the temp file.
                _downloadRequest.SetRequestHeader("X-EnigmaOS-VersionTag", versionTag ?? "latest");
                var op = _downloadRequest.SendWebRequest();
                op.completed += OnDownloadComplete;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Enigma OS] Could not start AutoLink download: {ex.Message}");
                _downloadRequest?.Dispose();
                _downloadRequest = null;
            }
        }

        private static void OnDownloadComplete(AsyncOperation op)
        {
            try
            {
                if (_downloadRequest == null) return;

                if (_downloadRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[Enigma OS] Failed to download AutoLink package: " +
                        $"{_downloadRequest.error}. Install manually from {RepoUrl}/releases");
                    return;
                }

                // Write to a uniquely-named temp file — guid suffix avoids
                // collision if the installer runs twice in the same session
                // (e.g. user dismissed, relaunched the prompt via a future
                // menu item).
                string versionTag = _downloadRequest.GetRequestHeader("X-EnigmaOS-VersionTag") ?? "latest";
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"AutoLink_{SanitizeForFilename(versionTag)}_{Guid.NewGuid():N}.unitypackage");

                try
                {
                    File.WriteAllBytes(tempPath, _downloadRequest.downloadHandler.data);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Enigma OS] Could not write AutoLink temp file: {ex.Message}");
                    return;
                }

                Debug.Log($"[Enigma OS] AutoLink download complete. Opening Unity package importer...");

                // Delay the ImportPackage call so it doesn't fight the
                // AsyncOperation callback's current frame. interactive: true
                // shows Unity's package-import dialog so the user can review
                // files before accepting — matches how the self-updater
                // imports Enigma OS updates.
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        AssetDatabase.ImportPackage(tempPath, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Enigma OS] AssetDatabase.ImportPackage failed: {ex.Message}");
                    }
                    finally
                    {
                        // Clean up temp file after a further delay so the
                        // import has time to read it.
                        EditorApplication.delayCall += () =>
                        {
                            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                            catch (Exception ex)
                            {
                                Debug.LogWarning(
                                    $"[Enigma OS] Could not clean up AutoLink temp file " +
                                    $"'{tempPath}': {ex.Message}");
                            }
                        };
                    }
                };
            }
            finally
            {
                _downloadRequest?.Dispose();
                _downloadRequest = null;
            }
        }

        private static string SanitizeForFilename(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(s);
            foreach (char c in invalid) sb.Replace(c, '_');
            return sb.ToString();
        }

        /// <summary>
        /// Menu item to reset the permanent dismissal — useful for users who
        /// clicked "Never Remind Me" but later want to install AutoLink. A
        /// hidden key they'd otherwise have to edit by hand via EditorPrefs.
        /// </summary>
        [MenuItem("Tools/Enigma OS/Re-check AutoLink dependency", priority = 2000)]
        private static void RecheckMenu()
        {
            EditorPrefs.DeleteKey(NeverAskKey);
            SessionState.SetBool(SessionDismissKey, false);
            if (IsAutoLinkPresent())
            {
                EditorUtility.DisplayDialog(
                    "Enigma OS — AutoLink",
                    "AutoLink is already installed in this project.",
                    "OK");
                return;
            }
            PromptInstall();
        }
    }
}
#endif

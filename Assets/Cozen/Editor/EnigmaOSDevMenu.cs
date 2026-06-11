#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.DevTools
{
    /// <summary>
    /// Dev-only menu items for shipping Enigma OS. This file lives in
    /// <c>Assets/Cozen/Editor</c> — OUTSIDE the shippable
    /// <c>Assets/Cozen/Enigma OS</c> folder — so it is never included in the
    /// exported release package. Nothing here ships to end users.
    /// </summary>
    internal static class EnigmaOSDevMenu
    {
        // Shipped asset roots.
        private const string PackageRoot   = "Assets/Cozen/Enigma OS";
        private const string VersionFile   = "Assets/Cozen/Enigma OS/VERSION";

        // Editor-only generated artifacts that must never ship:
        //  • VariantKeepers — material clones regenerated at build time.
        //  • OptimizedShaders — Poiyomi's per-material locked shader folders.
        //    Regenerated on lock and a licensing/bloat hazard; if a material
        //    needs one, that's a material to fix, not a shader to bundle.
        private static readonly string[] ExcludeDirs =
        {
            "Assets/Cozen/Enigma OS/VariantKeepers",
        };
        // Any folder with this exact name (at any depth under the root) is skipped.
        private const string ExcludeFolderName = "OptimizedShaders";

        private static bool IsExcluded(string p)
        {
            foreach (var dir in ExcludeDirs)
                if (p == dir || p.StartsWith(dir + "/")) return true;
            // Match an "OptimizedShaders" path segment anywhere under the root.
            return p.EndsWith("/" + ExcludeFolderName)
                || p.Contains("/" + ExcludeFolderName + "/");
        }

        // The only shaders Enigma OS materials use that aren't provided by a
        // separate dependency (AudioLink/TMP/ProTV/Mochie). The Surface folder
        // contains ~20 unrelated effect shaders we must NOT bundle, so these
        // three are listed explicitly.
        private static readonly string[] RequiredShaders =
        {
            "Assets/Cozen/Shaders/Surface/Standard.shader",
            "Assets/Cozen/Shaders/Surface/Waveform.shader",
            "Assets/Cozen/Shaders/Surface/AudioLinkLaunchpadUI.shader",
        };

        // ─────────────────────────────────────────────────────────────────────
        //  RELEASE PACKAGER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Exports a clean release .unitypackage: everything under
        /// <c>Assets/Cozen/Enigma OS</c> except <c>VariantKeepers</c>, plus the
        /// three required Cozen shaders. Dependencies are deliberately NOT
        /// auto-included — that's what drags in Poiyomi/Mochie/etc.; users
        /// provide those via the documented dependencies instead.
        /// </summary>
        [MenuItem("Tools/Enigma OS/Export Release Package", priority = 0)]
        private static void ExportReleasePackage()
        {
            if (!AssetDatabase.IsValidFolder(PackageRoot))
            {
                EditorUtility.DisplayDialog("Enigma OS Packager",
                    $"Package root not found:\n{PackageRoot}", "OK");
                return;
            }

            // Collect every asset under the package root, skipping the
            // editor-only generated artifacts (VariantKeepers, Poiyomi
            // OptimizedShaders lock folders).
            var paths = new List<string>();
            int excluded = 0;
            foreach (var raw in AssetDatabase.GetAllAssetPaths())
            {
                string p = raw.Replace('\\', '/');
                if (p != PackageRoot && !p.StartsWith(PackageRoot + "/")) continue;
                if (IsExcluded(p)) { excluded++; continue; }
                paths.Add(raw);
            }

            // Add the required shaders (and warn loudly if any is missing).
            var missing = new List<string>();
            foreach (var s in RequiredShaders)
            {
                if (File.Exists(s)) paths.Add(s);
                else missing.Add(s);
            }
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("Enigma OS Packager",
                    "Aborting — required shader(s) not found:\n\n" + string.Join("\n", missing),
                    "OK");
                return;
            }

            string version = ReadVersion();
            string defaultName = $"Enigma OS v{version}.unitypackage";

            // Transparency before writing: confirm exactly what goes in.
            bool go = EditorUtility.DisplayDialog(
                "Export Release Package",
                $"Version: {version}\n\n" +
                $"• {paths.Count - RequiredShaders.Length} assets from {PackageRoot}\n" +
                $"• {RequiredShaders.Length} required shaders (Standard, Waveform, AudioLink Launchpad UI)\n" +
                $"• {excluded} editor-only asset(s) EXCLUDED (VariantKeepers, Poiyomi OptimizedShaders)\n" +
                $"• Dependencies NOT auto-included (controlled list)\n\n" +
                "Choose where to save the .unitypackage next.",
                "Choose location…", "Cancel");
            if (!go) return;

            string outPath = EditorUtility.SaveFilePanel(
                "Export Enigma OS Release Package",
                "", defaultName, "unitypackage");
            if (string.IsNullOrEmpty(outPath)) return;

            // ExportPackageOptions.Default = no recurse (we list assets
            // explicitly) and no IncludeDependencies (controlled list).
            AssetDatabase.ExportPackage(paths.ToArray(), outPath, ExportPackageOptions.Default);

            Debug.Log($"[Enigma OS] Exported release package v{version} " +
                      $"({paths.Count} assets, {excluded} VariantKeepers excluded) → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        private static string ReadVersion()
        {
            try
            {
                if (File.Exists(VersionFile))
                    return File.ReadAllText(VersionFile).Trim();
            }
            catch { /* fall through */ }
            return "unknown";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  STAR PROMPT RESET (dev-only)
        // ─────────────────────────────────────────────────────────────────────

        // Key strings mirror EnigmaStarPrompt (the shipped user-facing prompt).
        // Kept in sync by convention — if those keys ever change, update here.
        private const string StarNeverAskKey      = "EnigmaOS.StarPrompt.NeverAsk";
        private const string StarSessionDismissKey = "EnigmaOS.StarPrompt.SessionDismissed";

        /// <summary>
        /// Resets the shipped "Star on GitHub" prompt so it fires again on the
        /// next domain reload. Dev-only — moved out of the shipped EnigmaStarPrompt
        /// so end users don't get a reset button for it.
        /// </summary>
        [MenuItem("Tools/Enigma OS/Reset Star Prompt", priority = 100)]
        private static void ResetStarPrompt()
        {
            EditorPrefs.DeleteKey(StarNeverAskKey);
            SessionState.EraseBool(StarSessionDismissKey);
            Debug.Log("[Enigma OS] Star prompt reset (dev). It will reappear on next domain reload.");
        }
    }
}
#endif

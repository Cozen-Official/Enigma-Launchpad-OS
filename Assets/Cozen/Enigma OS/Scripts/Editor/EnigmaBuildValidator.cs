#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Build-time hooks for Enigma OS.
    ///
    /// Rebuilds rt* arrays before a standard Unity build (IPreprocessBuildWithReport),
    /// per-scene during scene processing (IProcessSceneWithReport), and when the user
    /// clicks "Build &amp; Publish" in the VRChat SDK Control Panel
    /// (IVRCSDKBuildRequestedCallback).
    ///
    /// Play-mode auto-build lives in EnigmaPlayModeHook so that any compile error
    /// in this file (which depends on the VRC SDK) can never block play-mode rebuilds.
    /// </summary>
    public class EnigmaBuildValidator : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport, IVRCSDKBuildRequestedCallback
    {
        // 50 is chosen for two ordering constraints that share this field:
        //   1. IVRCSDKBuildRequestedCallback — must run BEFORE BeanFX's validator
        //      (BeanFXBuildValidator.callbackOrder == 90). BeanFX's "Unlocked
        //      Materials Detected" dialog reads each material's current _EnableX
        //      floats and bakes a variant that strips any effect currently at 0.
        //      Runtime SetFloat("_EnableX", 1) then toggles a keyword the variant
        //      no longer contains and the effect silently does nothing. Running
        //      first means PrepareAndLock sets _EnableX = 1 for every Enigma-
        //      targeted effect, invokes the lock compiler, and produces a variant
        //      that compiles every runtime-reachable effect. BeanFX's validator
        //      then sees the generated (non-base) shader, IsBaseShader returns
        //      false, and its dialog is skipped entirely for Enigma-managed
        //      materials.
        //   2. IProcessSceneWithReport — must run AFTER UdonSharp's OnSceneBuild
        //      (callbackOrder == -1) so the rt* array rebuild sees the
        //      UdonSharp-upgraded behaviours. 50 > -1 satisfies this.
        public int callbackOrder => 50;

        // Build-in-progress flag, set in OnBuildRequested / OnProcessScene and
        // cleared in OnPostprocessBuild + on play-mode transitions (see
        // EnigmaPlayModeHook.OnPlayModeStateChanged). While true:
        //   1. OnPreprocessBuild skips its RebuildAllControllers call because
        //      OnBuildRequested already did the full rebuild-and-lock pass
        //      (running PrepareShaderLocking twice per build races BeanFX's
        //      on-disk shader regeneration).
        //   2. RunBuild skips ApplyDefaultMaterialState and the Mochie keyword
        //      sync — both mutate material state that Unity is about to
        //      serialize into the bundle, and the sync DISABLES section
        //      keywords (e.g. _IMAGE_OVERLAY_ON when _SST == 0), which strips
        //      the shader_feature_local variants from the build entirely.
        //
        // Backed by SessionState, NOT a static bool: VRC builds trigger script
        // and UdonSharp compilation mid-build, and any resulting domain reload
        // silently resets statics. That exact failure shipped a world with the
        // Image Overlay variant stripped (2026-06-11): the flag reset between
        // OnBuildRequested and scene processing, OnProcessScene's RunBuild ran
        // the keyword sync, and the bundle serialized the material with only
        // _AUDIOLINK_ON in validKeywords. SessionState survives domain reloads
        // and clears on editor restart.
        //
        // A pure Unity build (File → Build Settings → Build, no VRC SDK
        // involved) runs OnPreprocessBuild without OnBuildRequested firing,
        // so the flag stays false and the OnPreprocessBuild path handles the
        // rebuild as it always did.
        private const string BuildFlagKey = "EnigmaOS.VrcBuildInProgress";

        internal static void MarkVrcBuildInProgress()
            => UnityEditor.SessionState.SetBool(BuildFlagKey, true);

        internal static void ClearVrcBuildInProgress()
            => UnityEditor.SessionState.SetBool(BuildFlagKey, false);

        // ── IVRCSDKBuildRequestedCallback ──

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            // Hard gate: multiple AudioLinkControllers cause runtime state-stomping
            // and sync storms (see EnigmaSceneValidator.BuildMultipleAudioLinkControllerMessage).
            // Abort the build before any rebuild/lock work runs.
            if (TryFailOnDuplicateAudioLinkControllers(true))
                return false;

            UnityEngine.Debug.Log("[EnigmaOS] VRC build requested — rebuilding controllers and enabling keywords.");
            // Mark BEFORE the rebuild so RunBuild's material-mutation guards
            // (ApplyDefaultMaterialState / Mochie keyword sync) hold from the
            // very first controller.
            MarkVrcBuildInProgress();
            EnigmaPlayModeHook.RebuildAllControllers("vrc-build");
            // DO NOT call ApplyDefaultMaterialState here — keywords must stay enabled
            // for shader_feature_local variant preservation during the build.
            return true;
        }

        // ── IPreprocessBuildWithReport ──

        public void OnPreprocessBuild(BuildReport report)
        {
            // Hard gate identical to OnBuildRequested. Throws BuildFailedException
            // rather than returning a bool — that's how IPreprocessBuildWithReport
            // aborts a standalone Unity build (no VRC SDK in the mix).
            if (TryFailOnDuplicateAudioLinkControllers(false))
                throw new UnityEditor.Build.BuildFailedException(
                    "[EnigmaOS] Build aborted — multiple AudioLinkControllers in scene. See console for details.");

            if (IsVrcBuildInProgress())
            {
                // Already rebuilt + locked in the IVRCSDKBuildRequestedCallback
                // path. Running RebuildAllControllers again here would race
                // BeanFX's shader regeneration — see the BuildFlagKey docs
                // above for the full failure mode.
                UnityEngine.Debug.Log("[EnigmaOS] OnPreprocessBuild: skipping rebuild (VRC OnBuildRequested already ran).");
                return;
            }
            // Pure Unity player build: mark so RunBuild's material-mutation
            // guards hold here too, then rebuild.
            MarkVrcBuildInProgress();
            EnigmaPlayModeHook.RebuildAllControllers("pre-build");
        }

        /// <summary>
        /// Shared check run at the top of every build hook. Logs a detailed error
        /// (with GameObject paths) to the Console and, when <paramref name="showDialog"/>
        /// is true, surfaces a modal dialog so users who aren't watching the console
        /// still see why the build aborted. Returns true when duplicates were found
        /// and the caller should abort.
        /// </summary>
        private static bool TryFailOnDuplicateAudioLinkControllers(bool showDialog)
        {
            var offenders = EnigmaSceneValidator.CollectAudioLinkControllersAcrossLoadedScenes();
            if (offenders.Count <= 1) return false;
            string msg = EnigmaSceneValidator.BuildMultipleAudioLinkControllerMessage(offenders);
            UnityEngine.Debug.LogError(msg);
            if (showDialog)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Enigma OS — Build Aborted",
                    msg,
                    "OK");
            }
            return true;
        }

        // ── IPostprocessBuildWithReport ──

        public void OnPostprocessBuild(BuildReport report)
        {
            // Sync Mochie SFX keywords to property values on every Enigma-
            // referenced material. The VRC build path keeps all section
            // keywords enabled through RebuildAllControllers → PrepareAndLock
            // so Unity's variant stripping includes them in the player bundle
            // — but those same keywords (with master toggle values at 0) cause
            // Mochie's keyword-gated shader paths (e.g. ApplyColor) to render
            // the default-valued effect at edit time, producing a grey overlay.
            // Variant compilation is already done by this point, so it's safe
            // to disable the keywords the user's master toggles say are off.
            try
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var ctrl in root.GetComponentsInChildren<Cozen.EnigmaOS.EnigmaController>(true))
                            EnigmaControllerEditor.SyncMochieKeywordsForController(ctrl);
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[EnigmaOS] OnPostprocessBuild Mochie keyword sync failed: {ex.Message}");
            }

            // Reset so the next build cycle (VRC or pure Unity) starts fresh.
            // NOTE: this only fires for player builds — VRC worlds build as
            // AssetBundles, where IPostprocessBuildWithReport never runs. For
            // those, the flag is cleared by EnigmaPlayModeHook on the next
            // play-mode transition (and by editor restart via SessionState).
            ClearVrcBuildInProgress();
        }

        /// <summary>
        /// Exposes the in-progress flag for editor code that needs to gate
        /// "post-build" behaviour during the build window — e.g. Mochie
        /// keyword sync must NOT run between OnBuildRequested and the end of
        /// scene serialization or it strips just-enabled variants.
        /// </summary>
        public static bool IsVrcBuildInProgress()
            => UnityEditor.SessionState.GetBool(BuildFlagKey, false);

        // ── IProcessSceneWithReport ──

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Detect build-time scene processing. OnProcessScene fires in two
            // contexts: entering play mode (report == null, play state pending)
            // and builds. Player builds pass a report; ASSETBUNDLE builds —
            // which is what VRC worlds are — pass report == null, so the play
            // state is the discriminator.
            bool isBuild = report != null || !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;

            // Self-assert the build flag. OnBuildRequested set it already, but
            // mid-build script/UdonSharp compilation can reload the domain and
            // (before SessionState backing) wiped it — letting RunBuild's
            // Mochie keyword sync strip shader variants right before Unity
            // serialized the scene. Asserting here makes scene processing
            // self-healing regardless of what happened earlier in the build.
            //
            // Note: by the time this runs, UdonSharp's own scene processing
            // (callbackOrder -1) has already stripped the C# proxy components
            // from the build copy of the scene, so the RebuildControllersInScene
            // call below finds no controllers during builds — it only does real
            // work in the play-mode invocation of OnProcessScene. Variant
            // survival for builds is guaranteed by the keeper materials baked
            // in EnigmaPlayModeHook.PrepareShaderLocking instead, which ship in
            // the bundle via EnigmaController.rtVariantKeeperMaterials and are
            // immune to mid-build keyword mutations on the live material.
            if (isBuild) MarkVrcBuildInProgress();

            EnigmaPlayModeHook.RebuildControllersInScene(scene, "scene-process");

            // A heap-scrubber (CleanStaleUdonHeapVars) used to run here to strip a
            // stale 'folders' variable off EnigmaController's Udon heap. It was
            // removed (2.0.4) after a build investigation proved it was both dead
            // and unsafe:
            //
            //   1. Its target no longer exists. 'folders' is not a field on the
            //      EnigmaController UdonSharpBehaviour — the editor config lives on
            //      a separate plain MonoBehaviour (EnigmaControllerData), so it is
            //      never serialized to any Udon heap. A scan of every Enigma Udon
            //      heap in the demo scene found zero 'folders' symbols.
            //   2. It never ran during builds anyway. UdonSharp's scene processing
            //      (callbackOrder -1) strips the C# proxy components before this
            //      callback (order 50) runs, so GetProxyBehaviour() returned null
            //      for every UdonBehaviour. A diagnostic build measured
            //      udons=101, nullProxy=101, symbolsRemoved=0 — a pure no-op.
            //   3. It would have been actively harmful if it ever DID run. Its
            //      "remove every heap symbol that isn't a public field" rule would
            //      strip the [SerializeField] private fields the runtime depends on
            //      — e.g. EnigmaFader.topLimiter/bottomLimiter (read by
            //      CacheMovementBounds) and the hand colliders — breaking every
            //      fader. Deleting it removes that landmine too.
            //
            // The original folders-on-the-Udon-heap problem is now prevented by
            // architecture (folders moved off the UdonSharpBehaviour entirely),
            // not by a build-time scrub.
        }
    }
}
#endif


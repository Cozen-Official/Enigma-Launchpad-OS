#if UNITY_EDITOR
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Rebuilds all EnigmaController rt* arrays automatically when entering play mode
    /// and resets material state when exiting.
    ///
    /// Deliberately kept in its own file with zero VRC SDK dependencies so that a
    /// compilation error in EnigmaBuildValidator (which pulls in VRC SDK interfaces)
    /// can never prevent this hook from running.
    ///
    /// UdonSharp triggers a domain reload during ExitingEditMode, which destroys all
    /// static subscriptions before they can execute. After the reload completes,
    /// [InitializeOnLoad] re-runs. The static constructor detects the play-mode
    /// transition via isPlayingOrWillChangePlaymode and fires the build immediately
    /// — before UdonSharp copies field values to the Udon VM heap. A redundant
    /// EnteredPlayMode handler is kept as a safety net; BuildRuntimeArrays calls
    /// CopyProxyToUdon to force-sync the C# proxy to the heap regardless of timing.
    /// </summary>
    [InitializeOnLoad]
    internal static class EnigmaPlayModeHook
    {
        private const string kWasPlayingKey = "EnigmaOS_WasInPlayMode";

        static EnigmaPlayModeHook()
        {
            // -= before += prevents duplicate registration across domain reloads.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // UdonSharp triggers a domain reload during ExitingEditMode, killing
            // the playModeStateChanged subscription before it can fire. After the
            // reload, [InitializeOnLoad] re-runs. Detect the play-mode transition
            // and build rt* arrays NOW — before UdonSharp initialises the Udon VM
            // heap from the (stale) serialised field values.
            if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            {
                // Gate duplicated from OnPlayModeStateChanged.ExitingEditMode:
                // when UdonSharp reloads the domain DURING ExitingEditMode, the
                // subscription dies before that case can run, so the gate has to
                // also happen here. Aborting via isPlaying = false after a reload
                // has already started still works — Unity unwinds the in-flight
                // play-mode transition and returns the editor to edit mode.
                if (TryAbortPlayModeOnDuplicateAudioLinkControllers())
                {
                    EditorApplication.isPlaying = false;
                    return;
                }
                RebuildAllControllers("pre-play-domain-reload");
                ApplyDefaultMaterialState();
            }

            // UdonSharp also triggers a domain reload when EXITING play mode,
            // which kills the subscription before EnteredEditMode can fire.
            // SessionState survives domain reloads within the same editor session.
            bool wasPlaying = SessionState.GetBool(kWasPlayingKey, false);
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
                && wasPlaying)
            {
                SessionState.SetBool(kWasPlayingKey, false);
                Debug.Log("[EnigmaOS] Post-play domain reload — resetting material state and unlocking shaders.");
                ApplyDefaultMaterialState();
                UnlockAllShaderMaterials("post-play-domain-reload");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Any play-mode transition means no build is running. Clear the
            // build-in-progress flag (set by EnigmaBuildValidator during VRC /
            // player builds) so a build that never reached its clear point —
            // VRC AssetBundle builds never fire IPostprocessBuildWithReport —
            // can't leave RunBuild's material-state passes disabled forever.
            // Key string duplicated from EnigmaBuildValidator.BuildFlagKey on
            // purpose: this file must not depend on the VRC-SDK-dependent
            // validator (see class doc).
            SessionState.SetBool("EnigmaOS.VrcBuildInProgress", false);

            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    Debug.Log("[EnigmaOS] ExitingEditMode.");
                    // Hard gate: if the scene(s) contain multiple AudioLinkControllers,
                    // abort play-mode entry here. Setting isPlaying = false during the
                    // ExitingEditMode transition cancels the transition cleanly —
                    // Unity never enters play mode, no domain reload happens, the
                    // user returns to edit mode with the Console + dialog explaining
                    // why. Mirrors the build-time gate in EnigmaBuildValidator so
                    // users can't sidestep the check via "Build & Test".
                    if (TryAbortPlayModeOnDuplicateAudioLinkControllers())
                    {
                        EditorApplication.isPlaying = false;
                        return;
                    }
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    // This fires after any domain reload is complete and before Start() runs
                    // on any UdonBehaviour — the correct place to bake rt* arrays.
                    Debug.Log("[EnigmaOS] EnteredPlayMode — building rt* arrays.");
                    SessionState.SetBool(kWasPlayingKey, true);
                    RebuildAllControllers("play-mode");
                    ApplyDefaultMaterialState();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    Debug.Log("[EnigmaOS] ExitingPlayMode — unlocking shader materials.");
                    // Flag persists through the domain reload UdonSharp triggers,
                    // so the [InitializeOnLoad] constructor can detect we need to
                    // reset material state even though EnteredEditMode won't fire.
                    SessionState.SetBool(kWasPlayingKey, true);
                    // Unlock before the domain reload; the post-play-domain-reload
                    // branch of the static constructor also calls this as a safety
                    // net (idempotent, so running twice is harmless).
                    UnlockAllShaderMaterials("exiting-play-mode");
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    Debug.Log("[EnigmaOS] EnteredEditMode — resetting material state and unlocking shaders.");
                    ApplyDefaultMaterialState();
                    UnlockAllShaderMaterials("entered-edit-mode");
                    break;
            }
        }

        /// <summary>
        /// Checks all loaded scenes for multiple AudioLinkControllers. If found,
        /// logs a detailed error (with GameObject paths) and shows a modal dialog
        /// explaining why play mode is being aborted. Returns true when the caller
        /// should cancel the play-mode transition. Mirrors the build-time gate in
        /// EnigmaBuildValidator.TryFailOnDuplicateAudioLinkControllers — same
        /// wording, same offender collection, different abort mechanism.
        /// </summary>
        private static bool TryAbortPlayModeOnDuplicateAudioLinkControllers()
        {
            var offenders = EnigmaSceneValidator.CollectAudioLinkControllersAcrossLoadedScenes();
            if (offenders.Count <= 1) return false;
            string msg = EnigmaSceneValidator.BuildMultipleAudioLinkControllerMessage(offenders);
            Debug.LogError(msg);
            EditorUtility.DisplayDialog(
                "Enigma OS — Play Mode Aborted",
                msg,
                "OK");
            return true;
        }

        /// <summary>Rebuilds all EnigmaControllers in every loaded scene.</summary>
        internal static void RebuildAllControllers(string trigger)
        {
            EnigmaShaderHelper.ClearCache();
            int rebuilt = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                    {
                        try
                        {
                            EnigmaControllerEditor.RunBuild(ctrl);
                            rebuilt++;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError(
                                $"[EnigmaOS] {trigger}: failed to rebuild controller " +
                                $"'{ctrl.gameObject.name}': {ex.Message}", ctrl);
                        }
                    }
                }
            }

            Debug.Log($"[EnigmaOS] {trigger}: rebuilt rt* arrays on {rebuilt} EnigmaController(s).");

            // Rebuild EnigmaButton rt* arrays
            int rebuiltButtons = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                    {
                        try
                        {
                            EnigmaControllerEditor.RunBuildButton(btn);
                            rebuiltButtons++;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError(
                                $"[EnigmaOS] {trigger}: failed to rebuild button " +
                                $"'{btn.gameObject.name}': {ex.Message}", btn);
                        }
                    }
                }
            }

            if (rebuiltButtons > 0)
                Debug.Log($"[EnigmaOS] {trigger}: rebuilt rt* arrays on {rebuiltButtons} EnigmaButton(s).");

            // ── Scene-wide exclusive peer linkage pass ───────────────────────────
            // Must run after all individual builds so rtGroupTagNames is populated.
            var allControllers = new System.Collections.Generic.List<EnigmaController>();
            var allButtons     = new System.Collections.Generic.List<EnigmaButton>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    allControllers.AddRange(root.GetComponentsInChildren<EnigmaController>(true));
                    allButtons.AddRange(root.GetComponentsInChildren<EnigmaButton>(true));
                }
            }
            EnigmaControllerEditor.BuildExclusivePeerLinks(allControllers, allButtons);

            // ── Boundary reference auto-population ───────────────────────────────
            // Wire up every EnigmaControllerBoundary's `otherControllers` array
            // with all the scene's controllers EXCEPT the one it owns. The
            // boundary uses this list at runtime to locally disable other
            // rooms' controllers when the player enters its trigger, so their
            // actions can't overwrite the entered room's visuals.
            //
            // Ran here (after exclusive peer linkage, before shader locking)
            // so allControllers is already collected and authoritative for the
            // entire loaded-scene set. Boundaries reference controllers by
            // direct object reference, so cross-scene boundaries pick up
            // controllers from additively-loaded scenes too.
            RebuildBoundaryReferences(allControllers);

            // ── AudioLink reference auto-population ──────────────────────────────
            // The Mixer bundles an AudioLink.AudioLinkController (+ the AutoLink
            // auto-gain/threshold adjuster) whose `audioLink` reference must point
            // at the scene's AudioLink instance — a user-supplied object OUTSIDE
            // the prefab, so it ships unassigned and nothing populates it. Auto-wire
            // it here (runs at build + play-enter) so the mixer's AudioLink panel
            // works without the user hand-assigning it. Only fills a NULL field
            // (never clobbers a manual assignment). This restores 1.x behaviour
            // (EnigmaLaunchpadEditor did it) that was dropped in the 2.0 rewrite.
            RebuildAudioLinkReferences(allControllers);

            // ── Shader locking pass ──────────────────────────────────────────────
            // Collect all materials targeted by Set Shader Property actions across
            // controllers and buttons, then prepare and lock each material.
            PrepareShaderLocking(allControllers, allButtons);

            // ── Cross-component Always-pass wiring ──────────────────────────────
            // Each controller gets references to every OTHER controller plus
            // every standalone button that owns an Always-gate action, so the
            // runtime ComputeAlwaysPassHeld can see gate holders outside its
            // own entries. Without this, deactivating an overlay on one
            // controller killed the pass while another controller's (or a
            // standalone button's) effect on the same material was active.
            WireCrossComponentGateRefs(allControllers, allButtons);

            // ── Re-apply entry defaults AFTER shader locking ─────────────────────
            // PrepareAndLock sets every section toggle (e.g. _FilterModel,
            // _Triplanar) to 1 so the lock compiler doesn't strip the
            // shader_feature_local variant. ApplyMaterialFixups (called from
            // PrepareShaderLocking after PrepareAndLock) zeroes the Mochie
            // baselines back to 0, but that overrides any default-on entry
            // that legitimately wants its master toggle non-zero (e.g. an
            // "Aura Outline" entry with onByDefault=true setting _OutlineType=2).
            // Running ApplyDefaultMaterialStateForController one more time here
            // re-writes the entry-driven values on top of the zeroed baseline.
            //
            // Skipped during play-mode entry — the EnigmaController runtime
            // executor handles the same job from Start()/InitializeRuntimeState.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                foreach (var ctrl in allControllers)
                {
                    try { ApplyDefaultMaterialStateForController(ctrl); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[EnigmaOS] {trigger}: post-lock ApplyDefaultMaterialStateForController failed on " +
                            $"'{ctrl.gameObject.name}': {ex.Message}", ctrl);
                    }
                }
                foreach (var btn in allButtons)
                {
                    try { ApplyDefaultMaterialStateForButton(btn); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[EnigmaOS] {trigger}: post-lock ApplyDefaultMaterialStateForButton failed on " +
                            $"'{btn.gameObject.name}': {ex.Message}", btn);
                    }
                }
            }

            // ── Mochie SFX keyword sync ─────────────────────────────────────────
            // Mirrors ScreenFXEditor.ApplyMaterialSettings: any section keyword
            // (_COLOR_ON, _TRIPLANAR_ON, …) whose master toggle property is 0
            // must be DISABLED. Mochie's shader paths that gate only on the
            // keyword (most notably ApplyColor, which doesn't check _FilterModel)
            // render with default property values whenever the keyword is on,
            // producing the "grey overlay" the user otherwise has to clear by
            // clicking the material.
            //
            // Runs for both edit-mode rebuilds AND play-mode entry — runtime
            // EnigmaController.Initialize doesn't sync keywords (it only writes
            // property values), so without this pass the play-mode preview also
            // shows the bad state.
            //
            // Skipped during VRC SDK builds because Unity's shader_feature_local
            // stripping uses the keyword state at variant collection time, and
            // PrepareAndLock's enables must survive into the player bundle. The
            // matching post-strip cleanup lives in
            // EnigmaBuildValidator.OnPostprocessBuild.
            if (!UnityEditor.BuildPipeline.isBuildingPlayer
                && !EnigmaBuildValidator.IsVrcBuildInProgress())
            {
                foreach (var ctrl in allControllers)
                {
                    try { EnigmaControllerEditor.SyncMochieKeywordsForController(ctrl); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[EnigmaOS] {trigger}: SyncMochieKeywordsForController failed on " +
                            $"'{ctrl.gameObject.name}': {ex.Message}", ctrl);
                    }
                }
            }

            // ── Scene-wide validation pass ──────────────────────────────────────
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    EnigmaSceneValidator.ValidateScene(scene);
            }
        }

        /// <summary>
        /// Walks every loaded scene's EnigmaControllerBoundary components and
        /// populates each one's <c>otherControllers</c> array with every
        /// controller in the scene-wide <paramref name="allControllers"/> set
        /// EXCEPT the one the boundary owns.
        ///
        /// Uses Undo.RecordObject + SetDirty on each boundary so prefab-
        /// instance overrides persist (per the project's prefab-undo memo).
        /// Skips writes when the array is already correct, so most builds
        /// have zero IO here.
        /// </summary>
        private static void RebuildBoundaryReferences(
            System.Collections.Generic.List<EnigmaController> allControllers)
        {
            int updated = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var boundary in root.GetComponentsInChildren<EnigmaControllerBoundary>(true))
                    {
                        if (boundary == null) continue;

                        // Build the desired array — every controller except
                        // the boundary's own. Order matches allControllers
                        // (scene order) so diffs are stable across rebuilds.
                        var others = new System.Collections.Generic.List<EnigmaController>(allControllers.Count);
                        foreach (var ctrl in allControllers)
                            if (ctrl != null && ctrl != boundary.controller)
                                others.Add(ctrl);
                        var desired = others.ToArray();

                        if (BoundaryArrayMatches(boundary.otherControllers, desired))
                            continue;

                        Undo.RecordObject(boundary, "Update boundary references");
                        boundary.otherControllers = desired;
                        EditorUtility.SetDirty(boundary);
                        updated++;
                    }
                }
            }

            if (updated > 0)
                Debug.Log($"[EnigmaOS] Boundary auto-wire: updated {updated} EnigmaControllerBoundary reference list(s).");
        }

        private static bool BoundaryArrayMatches(EnigmaController[] a, EnigmaController[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Populates the `audioLink` reference on the Mixer's bundled
        /// AudioLink.AudioLinkController (and the AutoLink auto-gain adjuster
        /// beside it) with the scene's AudioLink instance, when it is unassigned.
        /// AudioLink is a required Enigma dependency so its type is hard-referenced;
        /// AutoLink (lackofbindings) is treated as optional (see
        /// EnigmaAutoLinkInstaller) so it is reached by type name via a
        /// SerializedProperty write, keeping this code compilable if AutoLink is
        /// absent. Only ever fills a null field — a manual assignment is left alone.
        /// </summary>
        private static void RebuildAudioLinkReferences(
            System.Collections.Generic.List<EnigmaController> allControllers)
        {
            // Is there even a controller with an AudioLink panel to wire?
            bool anyTarget = false;
            foreach (var ctrl in allControllers)
                if (ctrl != null && ctrl.GetComponentInChildren<AudioLink.AudioLinkController>(true) != null)
                { anyTarget = true; break; }
            if (!anyTarget) return;

            // Resolve the scene's AudioLink instance(s), skipping EditorOnly ones
            // (they are stripped at build, so a reference to them would break).
            var audioLinks = new System.Collections.Generic.List<AudioLink.AudioLink>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var al in root.GetComponentsInChildren<AudioLink.AudioLink>(true))
                    {
                        if (al == null || IsUnderEditorOnlyTag(al.gameObject)) continue;
                        audioLinks.Add(al);
                    }
            }

            if (audioLinks.Count == 0)
            {
                Debug.LogWarning(
                    "[EnigmaOS] AudioLink auto-wire: a Mixer AudioLink controller is present " +
                    "but no AudioLink instance was found in the loaded scene(s). Add an AudioLink " +
                    "prefab, or the mixer's AudioLink panel won't function.");
                return;
            }

            Object target = audioLinks[0];
            if (audioLinks.Count > 1)
                Debug.LogWarning(
                    $"[EnigmaOS] AudioLink auto-wire: {audioLinks.Count} AudioLink instances found; " +
                    $"wiring the mixer to '{audioLinks[0].name}'. AudioLink is a single global system " +
                    "— consider removing the extras.");

            int wired = 0;
            foreach (var ctrl in allControllers)
            {
                if (ctrl == null) continue;

                // AudioLinkController(s) — hard type (AudioLink is a required dep).
                foreach (var alc in ctrl.GetComponentsInChildren<AudioLink.AudioLinkController>(true))
                    if (alc != null && TryFillAudioLinkField(alc, target)) wired++;

                // AutoLink auto-gain adjuster(s) — matched by type name so we keep
                // no hard reference to lackofbindings/AutoLink (it may be absent).
                foreach (var comp in ctrl.GetComponentsInChildren<Component>(true))
                    if (comp != null && comp.GetType().Name == "AutoLink"
                        && TryFillAudioLinkField(comp, target)) wired++;
            }

            if (wired > 0)
                Debug.Log($"[EnigmaOS] AudioLink auto-wire: populated {wired} unassigned AudioLink reference(s) with '{target.name}'.");
        }

        /// <summary>
        /// Sets a UdonSharp behaviour's serialized <c>audioLink</c> object
        /// reference to <paramref name="audioLink"/> IFF it is currently null,
        /// then force-copies the proxy into the Udon heap so the value is live
        /// this same play/build cycle (Start() reads it before UdonSharp's own
        /// sync would otherwise run — see the class header note). Never overwrites
        /// a manual assignment. Returns true when it wrote.
        /// </summary>
        private static bool TryFillAudioLinkField(Component comp, Object audioLink)
        {
            var so = new SerializedObject(comp);
            var prop = so.FindProperty("audioLink");
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return false;
            if (prop.objectReferenceValue != null) return false; // respect manual wiring
            prop.objectReferenceValue = audioLink;
            so.ApplyModifiedProperties(); // registers undo + marks the proxy dirty

            // Force the proxy → Udon heap copy now; the AudioLink controller reads
            // `audioLink` in Start(), which can run before UdonSharp's own sync.
            var usb = comp as UdonSharpBehaviour;
            if (usb != null) UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
            return true;
        }

        /// <summary>True if the GameObject or any ancestor is tagged EditorOnly.</summary>
        private static bool IsUnderEditorOnlyTag(GameObject go)
        {
            var t = go != null ? go.transform : null;
            while (t != null)
            {
                if (t.CompareTag("EditorOnly")) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Scans all controllers and buttons for Set Shader Property actions,
        /// groups used properties by material, and calls
        /// <see cref="EnigmaShaderHelper.PrepareAndLock"/> for each.
        /// </summary>
        private static void PrepareShaderLocking(
            System.Collections.Generic.List<EnigmaController> controllers,
            System.Collections.Generic.List<EnigmaButton> buttons)
        {
            // material instanceID → (material, set of property names)
            var materialProps = new System.Collections.Generic.Dictionary<int,
                (Material mat, System.Collections.Generic.HashSet<string> props)>();

            // (material, keyword) pairs for shader_feature variant preservation.
            var keywordsToEnable = new System.Collections.Generic.List<(Material mat, string keyword)>();

            void CollectFromActions(EnigmaActionData[] actions)
            {
                if (actions == null) return;
                foreach (var act in actions)
                {
                    if (act == null) continue;
                    if (act.targetRenderer == null || string.IsNullOrEmpty(act.propertyName)) continue;
                    var mats = act.targetRenderer.sharedMaterials;
                    int matIdx = act.materialIndex;
                    if (mats == null || matIdx < 0 || matIdx >= mats.Length || mats[matIdx] == null) continue;

                    if (act.actionType == 2) // Set Shader Property
                    {
                        Material mat = mats[matIdx];
                        int id = mat.GetInstanceID();
                        if (!materialProps.ContainsKey(id))
                            materialProps[id] = (mat, new System.Collections.Generic.HashSet<string>());
                        materialProps[id].props.Add(act.propertyName);

                        // Auto-detected keywords are handled by PrepareAndLock's
                        // EnableRequiredKeywords (which respects toggle state in play mode).
                    }
                    else if (act.actionType == 27) // Shader Keyword (legacy/manual)
                    {
                        keywordsToEnable.Add((mats[matIdx], act.propertyName));
                    }
                }
            }

            // Fader links drive shader properties too — without collecting
            // them, a property controlled ONLY by a fader never got its
            // keyword enabled or its variant preserved (locking/stripping
            // only saw button actions).
            void CollectFromFaderLinks(EnigmaEntryData entry)
            {
                if (entry == null || !entry.assignFader) return;
                var links = entry.faderLinks != null && entry.faderLinks.Length > 0
                    ? entry.faderLinks
                    : (entry.faderLink != null ? new[] { entry.faderLink } : null);
                if (links == null) return;
                foreach (var link in links)
                {
                    if (link == null || link.targetsSlider || link.targetsUdon || link.targetsSkybox) continue;
                    if (link.targetRenderer == null || string.IsNullOrEmpty(link.propertyName)) continue;
                    var lmats = link.targetRenderer.sharedMaterials;
                    if (lmats == null || link.materialIndex < 0 || link.materialIndex >= lmats.Length
                        || lmats[link.materialIndex] == null) continue;
                    Material lmat = lmats[link.materialIndex];
                    int lid = lmat.GetInstanceID();
                    if (!materialProps.ContainsKey(lid))
                        materialProps[lid] = (lmat, new System.Collections.Generic.HashSet<string>());
                    materialProps[lid].props.Add(link.propertyName);
                }
            }

            // Collect from controllers (button actions + dynamic fader links
            // + static fader targets).
            foreach (var ctrl in controllers)
            {
                var folders = ctrl.GetFolders();
                if (folders != null)
                {
                    foreach (var folder in folders)
                        foreach (var entry in folder.entries)
                            if (!entry.isEmpty)
                            {
                                CollectFromActions(entry.actions);
                                CollectFromFaderLinks(entry);
                            }
                }

                // Static faders (authored directly on the controller).
                var sfNames = ctrl.rtStaticFaderPropertyNames;
                if (sfNames != null)
                {
                    for (int sf = 0; sf < sfNames.Length; sf++)
                    {
                        if (string.IsNullOrEmpty(sfNames[sf])) continue;
                        bool sfSlider = ctrl.rtStaticFaderTargetsSlider != null
                            && sf < ctrl.rtStaticFaderTargetsSlider.Length && ctrl.rtStaticFaderTargetsSlider[sf];
                        bool sfUdon = ctrl.rtStaticFaderTargetsUdon != null
                            && sf < ctrl.rtStaticFaderTargetsUdon.Length && ctrl.rtStaticFaderTargetsUdon[sf];
                        bool sfSkybox = ctrl.rtStaticFaderTargetsSkybox != null
                            && sf < ctrl.rtStaticFaderTargetsSkybox.Length && ctrl.rtStaticFaderTargetsSkybox[sf];
                        if (sfSlider || sfUdon || sfSkybox) continue;
                        Renderer sfRend = ctrl.rtStaticFaderRenderers != null
                            && sf < ctrl.rtStaticFaderRenderers.Length ? ctrl.rtStaticFaderRenderers[sf] : null;
                        if (sfRend == null) continue;
                        int sfMi = ctrl.rtStaticFaderMaterialIndices != null
                            && sf < ctrl.rtStaticFaderMaterialIndices.Length ? ctrl.rtStaticFaderMaterialIndices[sf] : 0;
                        var sfMats = sfRend.sharedMaterials;
                        if (sfMats == null || sfMi < 0 || sfMi >= sfMats.Length || sfMats[sfMi] == null) continue;
                        Material sfMat = sfMats[sfMi];
                        int sfId = sfMat.GetInstanceID();
                        if (!materialProps.ContainsKey(sfId))
                            materialProps[sfId] = (sfMat, new System.Collections.Generic.HashSet<string>());
                        materialProps[sfId].props.Add(sfNames[sf]);
                    }
                }
            }

            // Collect from standalone buttons.
            foreach (var btn in buttons)
            {
                var holder = btn.GetComponent<EnigmaButtonActions>();
                if (holder != null)
                    CollectFromActions(holder.actions);
            }

            // Prepare and lock each material.
            //
            // Important: unlock each material first. After a previous Build (or
            // a manual BeanFX lock), the material's shader is the generated
            // variant — whose Properties block contains only the previously-
            // baked subset of _EnableX toggles, and whose shader_feature_local
            // pragmas are likewise a subset. Running PrepareAndLock on that
            // stripped shader reads an incomplete feature map (step 4 can't
            // find the toggle for properties whose _EnableX was stripped), so
            // it writes no _EnableX = 1 values, then BeanFX.LockMaterialSilent
            // reads the still-stripped material, finds HasProperty == false
            // for all effect enables, disables every keyword, and regenerates
            // a variant with zero effects. Unlocking first restores the base
            // shader with its full Properties block and all shader_feature
            // pragmas, so PrepareAndLock then sees the complete feature map
            // and can correctly re-enable every effect Enigma references.
            //
            // UnlockMaterial is idempotent (it's a no-op when the shader is
            // already the base template) so materials that haven't been
            // locked yet pay nothing. See EnigmaShaderHelper.UnlockMaterial
            // for the full contract.
            Debug.Log($"[EnigmaOS] PrepareShaderLocking: {materialProps.Count} material(s) collected, {keywordsToEnable.Count} keyword(s) to enable.");
            var keeperByMatId = new System.Collections.Generic.Dictionary<int, Material>();
            foreach (var kvp in materialProps)
            {
                EnigmaShaderHelper.UnlockMaterial(kvp.Value.mat);
                EnigmaShaderHelper.PrepareAndLock(kvp.Value.mat, kvp.Value.props);

                // Capture the variant keeper NOW, while the material is "hot"
                // (section toggles = 1, every Enigma-needed keyword enabled by
                // PrepareAndLock). The keeper is a hidden material asset that
                // ships in the world bundle via EnigmaController.
                // rtVariantKeeperMaterials — Unity collects shader_feature
                // variants from every material in the build, so the keeper
                // guarantees variant inclusion even if the LIVE material's
                // keywords get disabled later in the build window (Mochie's
                // inspector re-syncs keywords to the zeroed baseline values on
                // every repaint; an Inspector showing the material during the
                // async VRC build stripped _IMAGE_OVERLAY_ON from a shipped
                // world on 2026-06-11). Because the keeper's VALUES are hot
                // too, a Mochie-style value→keyword sync on the keeper keeps
                // its keywords enabled — it is stable by construction.
                var keeper = CreateOrUpdateVariantKeeper(kvp.Value.mat);
                if (keeper != null)
                {
                    keeperByMatId[kvp.Key] = keeper;

                    // Enum-mode toggles (Mochie _SST/_Zoom/_BlurModel/…) gate a
                    // DIFFERENT keyword per value, and the live material only
                    // carries the keyword for each action's baked value. Enable
                    // every sibling keyword of every used group on the keeper so
                    // ALL mode variants ship — the keeper is never rendered, so
                    // over-enabling costs only a few extra compiled variants.
                    foreach (string usedProp in kvp.Value.props)
                    {
                        var groupKws = EnigmaShaderHelper.GetGroupKeywords(kvp.Value.mat, usedProp);
                        for (int gk = 0; gk < groupKws.Count; gk++)
                            keeper.EnableKeyword(groupKws[gk]);
                    }
                    EditorUtility.SetDirty(keeper);
                }

                // Re-apply shader-specific baseline AFTER the lock pass. PrepareAndLock
                // leaves section toggles (e.g. Mochie _SST, _Triplanar) at 1 so the lock
                // compiler doesn't strip the variant — for shaders where the section
                // toggle IS the property the entry's main action sets (BeanFX style),
                // the runtime executor re-baselines the toggle to 0 on Start() based on
                // the entry's default state. For shaders where the section toggle differs
                // from the action's main property (Mochie _ScreenTex vs _SST), the toggle
                // is set by a synthetic non-stateful action that the executor's
                // ApplyDefaults skips at init, so PrepareAndLock's _SST=1 leaks through
                // and the overlay renders at world load. ApplyMaterialFixups resets the
                // Mochie SST baseline (and AL modulation strengths) so the overlay is
                // genuinely off until an Overlay button is pressed. Scoped to
                // the toggles Enigma manages on this material so user-authored
                // effect state outside Enigma's control survives the rebuild.
                EnigmaShaderHelper.ApplyMaterialFixups(kvp.Value.mat,
                    EnigmaShaderHelper.ComputeManagedToggles(kvp.Value.mat, kvp.Value.props));
            }

            // Enable keywords for shader_feature_local variant preservation.
            // Enable keywords from legacy type-27 (manual keyword) actions.
            // Auto-detected keywords are handled by PrepareAndLock's EnableRequiredKeywords.
            // This loop handles legacy type-27 (manual keyword) actions.
            // Mirror onto the material's keeper so the variant ships even if
            // the live material's keyword state changes mid-build. Materials
            // referenced ONLY by keyword actions get a keeper created here —
            // they used to be skipped entirely, leaving their force-enabled
            // keyword exposed to the same mid-build stripping the keepers
            // exist to prevent.
            foreach (var (mat, keyword) in keywordsToEnable)
            {
                if (mat == null) continue;
                mat.EnableKeyword(keyword);
                Material km;
                if (!keeperByMatId.TryGetValue(mat.GetInstanceID(), out km) || km == null)
                {
                    km = CreateOrUpdateVariantKeeper(mat);
                    if (km != null) keeperByMatId[mat.GetInstanceID()] = km;
                }
                if (km != null) km.EnableKeyword(keyword);
            }

            // Anchor the keepers in every controller's serialized data so they
            // ship inside the world bundle (see EnigmaController.
            // rtVariantKeeperMaterials). Copy to the backing UdonBehaviour
            // explicitly — PrepareShaderLocking runs after the controllers'
            // own build pass, so the standard proxy→Udon copy already happened.
            if (keeperByMatId.Count > 0 && controllers != null)
            {
                var keeperList = new System.Collections.Generic.List<Material>(keeperByMatId.Values);
                foreach (var ctrl in controllers)
                {
                    if (ctrl == null) continue;
                    try
                    {
                        var ctrlSo = new SerializedObject(ctrl);
                        var prop = ctrlSo.FindProperty("rtVariantKeeperMaterials");
                        if (prop == null) continue;
                        prop.arraySize = keeperList.Count;
                        for (int i = 0; i < keeperList.Count; i++)
                            prop.GetArrayElementAtIndex(i).objectReferenceValue = keeperList[i];
                        ctrlSo.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(ctrl);
                        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(
                            ctrl, UdonSharpEditor.ProxySerializationPolicy.All);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[EnigmaOS] Failed to anchor variant keepers on '{ctrl.gameObject.name}': {ex.Message}", ctrl);
                    }
                }
                Debug.Log($"[EnigmaOS] Variant keepers: {keeperList.Count} keeper material(s) anchored on {controllers.Count} controller(s).");
            }

            // Disable pass-gating keywords (e.g., _TRIPLANAR_ON) on all collected
            // materials if their toggle property is off. Must run after all keyword
            // enabling to catch keywords enabled by both PrepareAndLock and this loop.
            foreach (var kvp in materialProps)
                EnigmaShaderHelper.DisablePassGatingKeywords(kvp.Value.mat);

            // Flush every dirty material to disk so the locked state survives
            // Unity's asset-reimport during the subsequent build pipeline. Some
            // lock compilers (notably BeanFXLayerGenerator.LockMaterialSilent)
            // only call EditorUtility.SetDirty on the material — they don't
            // AssetDatabase.SaveAssets — which means the shader swap + keyword
            // enables live in memory only. During VRC Build & Test, Unity
            // reimports the .mat asset after our lock (OnPostprocessAllAssets
            // fires on the material), and that reimport reads the still-stale
            // on-disk .mat, blowing away our in-memory lock. Result: the user
            // sees "LOCKED - 0 compiled, 43 stripped" even though
            // PrepareAndLock ran correctly and wrote a 7-effect generated
            // shader file. SaveAssets here persists the in-memory state so
            // the reimport picks up the correct locked material.
            //
            // BeanFX's menu-driven LockAllMaterials flow already calls
            // SaveAssets at the end; only the reflection-invoked
            // LockMaterialSilent path (our Enigma lock) was missing this step.
            if (materialProps.Count > 0)
                AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Wires every controller's <c>rtOtherControllers</c> (all peer
        /// controllers in the loaded scenes) and <c>rtGateHolderButtons</c>
        /// (standalone buttons owning at least one Always-gate action) so the
        /// runtime's cross-component ComputeAlwaysPassHeld can consult them.
        /// Runs after the per-component builds, so the buttons' executor
        /// rtActionAlwaysGate arrays are current.
        /// </summary>
        private static void WireCrossComponentGateRefs(
            System.Collections.Generic.List<EnigmaController> allControllers,
            System.Collections.Generic.List<EnigmaButton> allButtons)
        {
            var gateButtons = new System.Collections.Generic.List<EnigmaButton>();
            if (allButtons != null)
            {
                foreach (var btn in allButtons)
                {
                    if (btn == null) continue;
                    var bexe = btn.GetComponent<EnigmaExecutor>();
                    if (bexe == null || bexe.rtActionAlwaysGate == null) continue;
                    foreach (int g in bexe.rtActionAlwaysGate)
                    {
                        if (g >= 0) { gateButtons.Add(btn); break; }
                    }
                }
            }

            if (allControllers == null) return;
            foreach (var ctrl in allControllers)
            {
                if (ctrl == null) continue;
                try
                {
                    var ctrlSo = new SerializedObject(ctrl);
                    var op = ctrlSo.FindProperty("rtOtherControllers");
                    var bp = ctrlSo.FindProperty("rtGateHolderButtons");
                    if (op == null || bp == null) continue;

                    var others = new System.Collections.Generic.List<EnigmaController>();
                    foreach (var oc in allControllers)
                        if (oc != null && oc != ctrl) others.Add(oc);

                    op.arraySize = others.Count;
                    for (int i = 0; i < others.Count; i++)
                        op.GetArrayElementAtIndex(i).objectReferenceValue = others[i];
                    bp.arraySize = gateButtons.Count;
                    for (int i = 0; i < gateButtons.Count; i++)
                        bp.GetArrayElementAtIndex(i).objectReferenceValue = gateButtons[i];

                    ctrlSo.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ctrl);
                    UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(
                        ctrl, UdonSharpEditor.ProxySerializationPolicy.All);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[EnigmaOS] Cross-component gate-ref wiring failed on '{ctrl.gameObject.name}': {ex.Message}", ctrl);
                }
            }
        }

        // Generated variant-keeper materials live here. They are build-time
        // asset anchors only — never assigned to a renderer, never rendered.
        private const string KeeperFolder = "Assets/Cozen/Enigma OS/VariantKeepers";

        /// <summary>
        /// Creates (or refreshes) the hidden variant-keeper clone of
        /// <paramref name="src"/>, capturing its current shader, property
        /// values, and enabled keyword set. Call while the material is in its
        /// post-PrepareAndLock "hot" state. Returns null on failure (keeper
        /// is an optimization for build robustness, never a hard requirement).
        /// </summary>
        private static Material CreateOrUpdateVariantKeeper(Material src)
        {
            try
            {
                if (src == null || src.shader == null) return null;
                if (!AssetDatabase.IsValidFolder(KeeperFolder))
                    AssetDatabase.CreateFolder("Assets/Cozen/Enigma OS", "VariantKeepers");

                // Key the keeper path by the source material's GUID, not just
                // its name — two same-named materials in different folders
                // used to clobber each other's keeper, silently dropping the
                // variant protection for one of them. Scene-embedded materials
                // (no GUID) fall back to the instance ID.
                string guid; long localId;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(src, out guid, out localId);
                string keeperKey = !string.IsNullOrEmpty(guid)
                    ? guid.Substring(0, System.Math.Min(12, guid.Length))
                    : src.GetInstanceID().ToString("X8");
                string path = $"{KeeperFolder}/{src.name}.{keeperKey} Keeper.mat";

                // Clean up a legacy name-keyed keeper (regenerated under the
                // new scheme on this and every future build).
                string legacyPath = $"{KeeperFolder}/{src.name} Keeper.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(legacyPath) != null)
                    AssetDatabase.DeleteAsset(legacyPath);

                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null)
                {
                    var clone = new Material(src) { name = src.name + " Keeper" };
                    AssetDatabase.CreateAsset(clone, path);
                    return clone;
                }

                existing.shader = src.shader;
                existing.CopyPropertiesFromMaterial(src);
                existing.shaderKeywords = src.shaderKeywords;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[EnigmaOS] Variant keeper creation failed for '{(src != null ? src.name : "null")}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The symmetric counterpart to <see cref="PrepareShaderLocking"/>.
        /// Collects every material referenced by a Set Shader Property action
        /// across all controllers / buttons in loaded scenes and calls
        /// <see cref="EnigmaShaderHelper.UnlockMaterial"/> on each, restoring
        /// the material to its editable base shader. Idempotent — calling on
        /// an already-unlocked material is a no-op (the unlock reflection
        /// helper early-exits when the shader is already the base template).
        ///
        /// Called on play-mode exit so the inspector's property list comes
        /// back to the full set of shader properties; without this call, the
        /// material stays on the generated locked shader (which only exposes
        /// the subset of properties the template locked in), and the user
        /// can't pick other properties when authoring a new button.
        /// </summary>
        private static void UnlockAllShaderMaterials(string trigger)
        {
            var materials = new System.Collections.Generic.HashSet<Material>();

            void CollectFromActions(EnigmaActionData[] actions)
            {
                if (actions == null) return;
                foreach (var act in actions)
                {
                    if (act == null || act.actionType != 2) continue; // Set Shader Property only
                    if (act.targetRenderer == null) continue;
                    var mats = act.targetRenderer.sharedMaterials;
                    int matIdx = act.materialIndex;
                    if (mats == null || matIdx < 0 || matIdx >= mats.Length || mats[matIdx] == null) continue;
                    materials.Add(mats[matIdx]);
                }
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                    {
                        var folders = ctrl.GetFolders();
                        if (folders == null) continue;
                        foreach (var folder in folders)
                            foreach (var entry in folder.entries)
                                if (!entry.isEmpty)
                                    CollectFromActions(entry.actions);
                    }
                    foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                    {
                        var holder = btn.GetComponent<EnigmaButtonActions>();
                        if (holder != null)
                            CollectFromActions(holder.actions);
                    }
                }
            }

            if (materials.Count == 0) return;

            Debug.Log($"[EnigmaOS] UnlockAllShaderMaterials ({trigger}): attempting unlock on {materials.Count} material(s).");
            foreach (var mat in materials)
            {
                try { EnigmaShaderHelper.UnlockMaterial(mat); }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[EnigmaOS] Unlock threw for '{mat?.name}': {ex.Message}");
                }
            }

            // Flush the shader swaps to disk so the inspector sees the base
            // shader after the domain reload completes.
            AssetDatabase.SaveAssets();
        }

        /// <summary>Rebuilds all EnigmaControllers and EnigmaButtons within a single scene.</summary>
        internal static void RebuildControllersInScene(Scene scene, string trigger)
        {
            if (!scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                {
                    try { EnigmaControllerEditor.RunBuild(ctrl); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[EnigmaOS] {trigger}: failed to rebuild controller " +
                            $"'{ctrl.gameObject.name}': {ex.Message}", ctrl);
                    }
                }
                foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                {
                    try { EnigmaControllerEditor.RunBuildButton(btn); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(
                            $"[EnigmaOS] {trigger}: failed to rebuild button " +
                            $"'{btn.gameObject.name}': {ex.Message}", btn);
                    }
                }
            }

            // ── Scene-wide exclusive peer linkage pass ───────────────────────────
            var sceneControllers = new System.Collections.Generic.List<EnigmaController>();
            var sceneButtons     = new System.Collections.Generic.List<EnigmaButton>();
            foreach (var root in scene.GetRootGameObjects())
            {
                sceneControllers.AddRange(root.GetComponentsInChildren<EnigmaController>(true));
                sceneButtons.AddRange(root.GetComponentsInChildren<EnigmaButton>(true));
            }
            EnigmaControllerEditor.BuildExclusivePeerLinks(sceneControllers, sceneButtons);
            EnigmaSceneValidator.ValidateScene(scene);
        }

        // Udon proxy behaviours whose serialised fields have been mutated by the
        // current Build / default-apply pass. Drained by RefreshVRSLMaterialPropertyBlocks
        // at the end of each top-level entry point (controller or button) so VRSL-style
        // fixtures repush their per-renderer MaterialPropertyBlock after the Build
        // writes new lightColorTint / intensity values to their fields. Without this,
        // VRSL's inspector-driven MPB stays stale until the user manually nudges each
        // fixture's inspector, even though the serialised colour IS updated.
        private static readonly System.Collections.Generic.HashSet<UnityEngine.MonoBehaviour> _touchedUdonForVRSL
            = new System.Collections.Generic.HashSet<UnityEngine.MonoBehaviour>();

        /// <summary>
        /// Writes a typed value to an <see cref="UdonSharpBehaviour"/>'s serialised
        /// public field — used by the Build pass so clicking Build leaves every
        /// VRSL-style fixture's <c>lightColorTint</c> (and similar) at the fader /
        /// action default the user authored. The write goes through
        /// <see cref="SerializedObject"/>, which also registers Undo and marks the
        /// target dirty so the value persists through domain reloads.
        ///
        /// <para>Any behaviour touched here is queued for a VRSL MPB refresh at
        /// the end of the pass via <see cref="RefreshVRSLMaterialPropertyBlocks"/>.</para>
        ///
        /// <para>Supported <paramref name="value"/> types: <c>Color</c>, <c>float</c>,
        /// <c>int</c>, <c>bool</c>, <c>string</c>. Numeric coercions handle the
        /// common mismatch cases (int↔float, int↔bool) so a Udon variable declared
        /// as <c>float</c> can accept an <c>int</c> caller value and vice versa.</para>
        /// </summary>
        private static void WriteUdonFieldForBuild(UdonSharpBehaviour behaviour, string varName, object value)
        {
            if (behaviour == null || string.IsNullOrEmpty(varName) || value == null) return;
            var so = new SerializedObject(behaviour);
            var sp = so.FindProperty(varName);
            if (sp == null) return;

            bool dirty = false;
            switch (value)
            {
                case Color c when sp.propertyType == SerializedPropertyType.Color:
                    if (sp.colorValue != c) { sp.colorValue = c; dirty = true; }
                    break;
                case float f when sp.propertyType == SerializedPropertyType.Float:
                    if (!Mathf.Approximately(sp.floatValue, f)) { sp.floatValue = f; dirty = true; }
                    break;
                case float f when sp.propertyType == SerializedPropertyType.Integer:
                    if (sp.intValue != (int)f) { sp.intValue = (int)f; dirty = true; }
                    break;
                case float f when sp.propertyType == SerializedPropertyType.Boolean:
                    if (sp.boolValue != (f > 0.5f)) { sp.boolValue = f > 0.5f; dirty = true; }
                    break;
                case int i when sp.propertyType == SerializedPropertyType.Integer:
                    if (sp.intValue != i) { sp.intValue = i; dirty = true; }
                    break;
                case int i when sp.propertyType == SerializedPropertyType.Float:
                    if (!Mathf.Approximately(sp.floatValue, i)) { sp.floatValue = i; dirty = true; }
                    break;
                case int i when sp.propertyType == SerializedPropertyType.Boolean:
                    if (sp.boolValue != (i != 0)) { sp.boolValue = i != 0; dirty = true; }
                    break;
                case bool b when sp.propertyType == SerializedPropertyType.Boolean:
                    if (sp.boolValue != b) { sp.boolValue = b; dirty = true; }
                    break;
                case string s when sp.propertyType == SerializedPropertyType.String:
                    if (sp.stringValue != s) { sp.stringValue = s; dirty = true; }
                    break;
            }

            if (dirty)
            {
                so.ApplyModifiedProperties();
                _touchedUdonForVRSL.Add(behaviour);
            }
        }

        /// <summary>
        /// Walks <see cref="_touchedUdonForVRSL"/> and, for any behaviour that looks
        /// like a VRSL fixture (type full name starts with
        /// <c>VRSL.VRStageLighting_</c>), invokes the fixture's MPB-push method via
        /// reflection. We use reflection rather than a typed call so Enigma OS
        /// doesn't need a hard dependency on the VRSL package being installed.
        ///
        /// <para>Mirrors VRSL's own editor logic: AudioLink fixtures use
        /// <c>_UpdateInstancedPropertiesSansAudioLink</c>, DMX fixtures use
        /// <c>_UpdateInstancedPropertiesSansDMX</c>. These variants skip the live
        /// AudioLink / DMX texture reads that would otherwise require runtime state,
        /// so they're safe to call in edit mode.</para>
        /// </summary>
        private static void RefreshVRSLMaterialPropertyBlocks()
        {
            if (_touchedUdonForVRSL.Count == 0) return;
            try
            {
                foreach (var ub in _touchedUdonForVRSL)
                {
                    if (ub == null) continue;
                    var type = ub.GetType();
                    string fullName = type.FullName ?? "";
                    if (!fullName.StartsWith("VRSL.VRStageLighting_", System.StringComparison.Ordinal)) continue;

                    // Name-sniff to pick the right "Sans" variant. Fallback order:
                    // the fixture's edit-mode refresh method first (no AudioLink /
                    // DMX required), then the full _UpdateInstancedProperties as a
                    // last resort (only safe if AudioLink/DMX happen to be initialised).
                    string preferred = fullName.Contains("_DMX")
                        ? "_UpdateInstancedPropertiesSansDMX"
                        : "_UpdateInstancedPropertiesSansAudioLink";

                    var m = type.GetMethod(preferred, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (m == null)
                        m = type.GetMethod("_UpdateInstancedProperties", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (m == null) continue;

                    try { m.Invoke(ub, null); }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[EnigmaOS] VRSL MPB refresh on '{ub.name}' via {preferred} failed: {ex.Message}", ub);
                    }
                }
            }
            finally
            {
                _touchedUdonForVRSL.Clear();
            }
        }

        /// <summary>
        /// Resets all material properties touched by controller actions back to their
        /// default state.  Called on EnteredEditMode so that sharedMaterial changes
        /// made during play mode don't leak into the editor.
        /// </summary>
        private static void ApplyDefaultMaterialState()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var ctrl in root.GetComponentsInChildren<EnigmaController>(true))
                    {
                        try { ApplyDefaultMaterialStateForController(ctrl); }
                        catch (System.Exception ex)
                        {
                            Debug.LogError(
                                $"[EnigmaOS] Failed to reset material state on '{ctrl.gameObject.name}': {ex.Message}", ctrl);
                        }
                    }
                    foreach (var btn in root.GetComponentsInChildren<EnigmaButton>(true))
                    {
                        try { ApplyDefaultMaterialStateForButton(btn); }
                        catch (System.Exception ex)
                        {
                            Debug.LogError(
                                $"[EnigmaOS] Failed to reset material state on button '{btn.gameObject.name}': {ex.Message}", btn);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reads directly from the editor-time action data (EnigmaControllerData /
        /// EnigmaButtonActions) instead of the baked rt* arrays on the executor.
        /// The rt* arrays are populated by the build step which runs AFTER Unity
        /// takes its scene snapshot, so they may be empty/stale after play-mode exit.
        /// The companion MonoBehaviours are plain (non-Udon) and always survive
        /// the snapshot round-trip.
        /// </summary>
        internal static void ApplyDefaultMaterialStateForController(EnigmaController ctrl)
        {
            var folders = ctrl.GetFolders();
            if (folders == null || folders.Length == 0) return;

            // Build set of exclusive group tags that have a default-on entry.
            var activeGroupTags = new System.Collections.Generic.HashSet<string>();
            foreach (var folder in folders)
            {
                if (folder == null || folder.entries == null) continue;
                foreach (var entry in folder.entries)
                {
                    if (entry == null || entry.isEmpty || !entry.onByDefault) continue;
                    if (string.IsNullOrEmpty(entry.exclusiveGroup)) continue;
                    foreach (string tag in entry.exclusiveGroup.Split(','))
                    {
                        string t = tag.Trim();
                        if (t.Length > 0) activeGroupTags.Add(t);
                    }
                }
            }

            // Pass 1: apply active=true for default-on entries.
            foreach (var folder in folders)
            {
                if (folder == null || folder.entries == null) continue;
                foreach (var entry in folder.entries)
                {
                    if (entry == null || entry.isEmpty || !entry.onByDefault) continue;
                    ApplyActionsDefault(entry.actions, true);
                }
            }

            // Pass 2: apply active=false for entries that are not default-on
            // and not in an exclusive group with a default-on member.
            foreach (var folder in folders)
            {
                if (folder == null || folder.entries == null) continue;
                foreach (var entry in folder.entries)
                {
                    if (entry == null || entry.isEmpty || entry.onByDefault) continue;

                    bool skipForceOff = false;
                    if (!string.IsNullOrEmpty(entry.exclusiveGroup))
                    {
                        foreach (string tag in entry.exclusiveGroup.Split(','))
                        {
                            if (activeGroupTags.Contains(tag.Trim()))
                            { skipForceOff = true; break; }
                        }
                    }

                    if (!skipForceOff)
                        ApplyActionsDefault(entry.actions, false);
                }
            }

            // Reset fader-linked properties to their default values.
            foreach (var folder in folders)
            {
                if (folder == null || folder.entries == null) continue;
                foreach (var entry in folder.entries)
                {
                    if (entry == null || entry.isEmpty || !entry.assignFader) continue;

                    // Collect all fader links (single + array)
                    EnigmaFaderLinkData[] links = entry.faderLinks != null && entry.faderLinks.Length > 0
                        ? entry.faderLinks
                        : (entry.faderLink != null && !string.IsNullOrEmpty(entry.faderLink.propertyName)
                            ? new[] { entry.faderLink } : null);
                    if (links == null) continue;

                    foreach (var link in links)
                    {
                        if (link == null || string.IsNullOrEmpty(link.propertyName)) continue;

                        if (link.targetsSkybox)
                        {
                            // Skybox fader
                            Material skyMat = link.skyboxMaterial != null ? link.skyboxMaterial : RenderSettings.skybox;
                            if (skyMat != null && skyMat.HasProperty(link.propertyName))
                            {
                                if (link.propertyType == 1) // Color
                                    skyMat.SetColor(link.propertyName, link.defaultColor);
                                else
                                    skyMat.SetFloat(link.propertyName, link.defaultValue);
                            }
                        }
                        else if (link.targetRenderer != null)
                        {
                            // Renderer fader
                            Material[] mats = link.targetRenderer.sharedMaterials;
                            int mi = link.materialIndex;
                            if (mi >= 0 && mi < mats.Length && mats[mi] != null)
                            {
                                if (link.propertyType == 1) // Color
                                    mats[mi].SetColor(link.propertyName, link.defaultColor);
                                else
                                    mats[mi].SetFloat(link.propertyName, link.defaultValue);
                            }
                        }
                    }
                }
            }

            // Reset STATIC-fader-linked properties to their default values.
            // Iterates the runtime arrays populated by BuildRuntimeArrays (which
            // runs just before this method). Only material/skybox targets get
            // baked — Udon variable and UI Slider targets are driven every frame
            // by the fader itself and don't need a default snapshot here.
            int sfCount = ctrl.staticFaderCount;
            if (sfCount > 0 && ctrl.rtStaticFaderNames != null)
            {
                int len = Mathf.Min(sfCount, ctrl.rtStaticFaderNames.Length);
                for (int f = 0; f < len; f++)
                {
                    bool isUdon   = ctrl.rtStaticFaderTargetsUdon   != null && f < ctrl.rtStaticFaderTargetsUdon.Length   && ctrl.rtStaticFaderTargetsUdon[f];
                    bool isSlider = ctrl.rtStaticFaderTargetsSlider != null && f < ctrl.rtStaticFaderTargetsSlider.Length && ctrl.rtStaticFaderTargetsSlider[f];
                    bool isSkybox = ctrl.rtStaticFaderTargetsSkybox != null && f < ctrl.rtStaticFaderTargetsSkybox.Length && ctrl.rtStaticFaderTargetsSkybox[f];
                    if (isUdon || isSlider) continue;

                    string prop = ctrl.rtStaticFaderPropertyNames != null && f < ctrl.rtStaticFaderPropertyNames.Length
                        ? ctrl.rtStaticFaderPropertyNames[f] : null;
                    if (string.IsNullOrEmpty(prop)) continue;

                    int   pType = ctrl.rtStaticFaderPropertyTypes  != null && f < ctrl.rtStaticFaderPropertyTypes.Length  ? ctrl.rtStaticFaderPropertyTypes[f]  : 0;
                    float defV  = ctrl.rtStaticFaderDefaultValues  != null && f < ctrl.rtStaticFaderDefaultValues.Length  ? ctrl.rtStaticFaderDefaultValues[f]  : 0f;
                    Color defC  = ctrl.rtStaticFaderDefaultColors  != null && f < ctrl.rtStaticFaderDefaultColors.Length  ? ctrl.rtStaticFaderDefaultColors[f]  : Color.white;

                    if (isSkybox)
                    {
                        Material skyMat = RenderSettings.skybox;
                        if (skyMat != null && skyMat.HasProperty(prop))
                        {
                            if (pType == 1) skyMat.SetColor(prop, defC);
                            else            skyMat.SetFloat(prop, defV);
                        }
                    }
                    else
                    {
                        // Primary renderer.
                        Renderer rend = ctrl.rtStaticFaderRenderers       != null && f < ctrl.rtStaticFaderRenderers.Length       ? ctrl.rtStaticFaderRenderers[f]       : null;
                        int      mi   = ctrl.rtStaticFaderMaterialIndices != null && f < ctrl.rtStaticFaderMaterialIndices.Length ? ctrl.rtStaticFaderMaterialIndices[f] : 0;
                        if (rend != null)
                        {
                            Material[] mats = rend.sharedMaterials;
                            if (mi >= 0 && mi < mats.Length && mats[mi] != null)
                            {
                                if (pType == 1) mats[mi].SetColor(prop, defC);
                                else            mats[mi].SetFloat(prop, defV);
                            }
                        }

                        // Extra renderers (flat arrays, per-entry block located
                        // at prefix sum of rtStaticFaderExtraCount[0..f-1]).
                        if (ctrl.rtStaticFaderExtraCount != null && f < ctrl.rtStaticFaderExtraCount.Length)
                        {
                            int extraStart = 0;
                            for (int i = 0; i < f && i < ctrl.rtStaticFaderExtraCount.Length; i++)
                                extraStart += ctrl.rtStaticFaderExtraCount[i];
                            int extraCount = ctrl.rtStaticFaderExtraCount[f];
                            for (int e = 0; e < extraCount; e++)
                            {
                                int flat = extraStart + e;
                                Renderer xr = ctrl.rtStaticFaderExtraRenderers != null && flat < ctrl.rtStaticFaderExtraRenderers.Length
                                    ? ctrl.rtStaticFaderExtraRenderers[flat] : null;
                                if (xr == null) continue;
                                int xmi = ctrl.rtStaticFaderExtraMaterialIndices != null && flat < ctrl.rtStaticFaderExtraMaterialIndices.Length
                                    ? ctrl.rtStaticFaderExtraMaterialIndices[flat] : 0;
                                Material[] xms = xr.sharedMaterials;
                                if (xmi < 0 || xmi >= xms.Length || xms[xmi] == null) continue;
                                if (pType == 1) xms[xmi].SetColor(prop, defC);
                                else            xms[xmi].SetFloat(prop, defV);
                            }
                        }
                    }
                }
            }

            // Reset STATIC-fader-linked UDON VARIABLES to their default values.
            // Without this pass, an Udon-target fader (e.g. driving VRSL's
            // lightColorTint) leaves the target behaviour's serialised field at
            // whatever the user last typed in the VRSL inspector — so after Build
            // the scene shows the wrong starting colour until the user enters play
            // mode (Bind() writes it) or drags the fader. Touches primary + every
            // extra Udon behaviour so multi-light faders baseline every target.
            // Each touched behaviour is queued for a VRSL MPB refresh at the end
            // of this method (RefreshVRSLMaterialPropertyBlocks).
            if (sfCount > 0 && ctrl.rtStaticFaderNames != null
                && ctrl.rtStaticFaderTargetsUdon != null
                && ctrl.rtStaticFaderUdonBehaviours != null
                && ctrl.rtStaticFaderUdonVariableNames != null)
            {
                int len = Mathf.Min(sfCount, ctrl.rtStaticFaderNames.Length);
                for (int f = 0; f < len; f++)
                {
                    if (f >= ctrl.rtStaticFaderTargetsUdon.Length || !ctrl.rtStaticFaderTargetsUdon[f]) continue;

                    string varName = f < ctrl.rtStaticFaderUdonVariableNames.Length
                        ? ctrl.rtStaticFaderUdonVariableNames[f] : null;
                    if (string.IsNullOrEmpty(varName)) continue;

                    int   pType = ctrl.rtStaticFaderPropertyTypes != null && f < ctrl.rtStaticFaderPropertyTypes.Length ? ctrl.rtStaticFaderPropertyTypes[f] : 0;
                    float defV  = ctrl.rtStaticFaderDefaultValues != null && f < ctrl.rtStaticFaderDefaultValues.Length ? ctrl.rtStaticFaderDefaultValues[f] : 0f;
                    Color defC  = ctrl.rtStaticFaderDefaultColors != null && f < ctrl.rtStaticFaderDefaultColors.Length ? ctrl.rtStaticFaderDefaultColors[f] : Color.white;

                    // FADER propertyType convention: 0=Float, 1=Color.
                    object defVal = pType == 1 ? (object)defC : (object)defV;

                    // Primary Udon behaviour.
                    UdonSharpBehaviour primary = f < ctrl.rtStaticFaderUdonBehaviours.Length
                        ? ctrl.rtStaticFaderUdonBehaviours[f] : null;
                    WriteUdonFieldForBuild(primary, varName, defVal);

                    // Extras: flat arrays, per-entry block located at prefix sum
                    // of rtStaticFaderExtraUdonCount[0..f-1]. Mirrors the runtime
                    // CollectStaticFaderUdonBehaviours layout.
                    if (ctrl.rtStaticFaderExtraUdonCount != null
                        && f < ctrl.rtStaticFaderExtraUdonCount.Length
                        && ctrl.rtStaticFaderExtraUdonBehaviours != null)
                    {
                        int extraStart = 0;
                        for (int i = 0; i < f && i < ctrl.rtStaticFaderExtraUdonCount.Length; i++)
                            extraStart += ctrl.rtStaticFaderExtraUdonCount[i];
                        int extraCount = ctrl.rtStaticFaderExtraUdonCount[f];
                        for (int e = 0; e < extraCount; e++)
                        {
                            int flat = extraStart + e;
                            if (flat >= ctrl.rtStaticFaderExtraUdonBehaviours.Length) break;
                            WriteUdonFieldForBuild(ctrl.rtStaticFaderExtraUdonBehaviours[flat], varName, defVal);
                        }
                    }
                }
            }

            // Pass 4: Disable pass-gating keywords whose toggle is off.
            // Some shaders (e.g., Mochie Triplanar) compile in entire visual passes
            // based on keyword presence without runtime property guards. With the
            // keyword enabled and toggle=0, the pass renders with default shader
            // values causing visual artifacts. These keywords must be disabled.
            // Effects using pass-gating keywords cannot be runtime-toggled via
            // Enigma OS (Udon can't call EnableKeyword).
            foreach (var folder in folders)
            {
                if (folder == null || folder.entries == null) continue;
                foreach (var entry in folder.entries)
                {
                    if (entry == null || entry.isEmpty || entry.actions == null) continue;
                    foreach (var action in entry.actions)
                    {
                        if (action == null || action.actionType != 2) continue;
                        if (action.targetRenderer == null) continue;
                        Material[] mats = action.targetRenderer.sharedMaterials;
                        int mi = action.materialIndex;
                        if (mi < 0 || mi >= mats.Length || mats[mi] == null) continue;
                        EnigmaShaderHelper.DisablePassGatingKeywords(mats[mi]);
                    }
                }
            }

            // Final pass: push MaterialPropertyBlocks on any VRSL-style Udon
            // fixtures we just mutated so the editor preview reflects the new
            // lightColorTint / intensity values without the user having to nudge
            // each fixture's inspector. No-op if nothing VRSL-shaped was touched.
            RefreshVRSLMaterialPropertyBlocks();
        }

        internal static void ApplyDefaultMaterialStateForButton(EnigmaButton btn)
        {
            var holder = btn.GetComponent<EnigmaButtonActions>();
            if (holder == null || holder.actions == null || holder.actions.Length == 0) return;

            bool isDefaultOn = btn.onByDefault;
            bool skipForceOff = false;

            if (!isDefaultOn && btn.useExclusiveGroup && !string.IsNullOrEmpty(btn.exclusiveGroup))
            {
                bool groupHasDefaultOn = false;
                foreach (var root in btn.gameObject.scene.GetRootGameObjects())
                {
                    foreach (var peer in root.GetComponentsInChildren<EnigmaButton>(true))
                    {
                        if (peer == btn) continue;
                        if (peer.onByDefault && peer.useExclusiveGroup
                            && peer.exclusiveGroup == btn.exclusiveGroup)
                        {
                            groupHasDefaultOn = true;
                            break;
                        }
                    }
                    if (groupHasDefaultOn) break;
                }

                if (groupHasDefaultOn)
                    skipForceOff = true;
                else if (btn.exclusiveOff)
                    isDefaultOn = true;
            }

            if (isDefaultOn)
                ApplyActionsDefault(holder.actions, true);
            else if (!skipForceOff)
                ApplyActionsDefault(holder.actions, false);

            // Flush any VRSL fixtures touched by this button's Set-Udon-Variable
            // actions so their MPB reflects the new value immediately. Mirrors
            // the same call at the end of ApplyDefaultMaterialStateForController.
            RefreshVRSLMaterialPropertyBlocks();
        }

        /// <summary>
        /// Applies default material/object state for a set of editor-time actions.
        /// Handles shader properties (type 2), keywords (type 27), toggle objects (type 0),
        /// toggle components (type 11), and skybox (type 4).
        /// </summary>
        private static void ApplyActionsDefault(EnigmaActionData[] actions, bool active)
        {
            if (actions == null) return;
            foreach (var action in actions)
            {
                if (action == null) continue;
                int t = action.actionType;

                if (t == 0) // Toggle Object
                {
                    if (action.targetObject != null)
                        action.targetObject.SetActive(active);
                }
                else if (t == 1) // Toggle / Apply Material
                {
                    // Apply Active when the entry is on-by-default (active=true,
                    // pass 1 in the caller). Apply Default when the entry is
                    // off (active=false, pass 2). Default-on buttons therefore
                    // get their Active Material at scene start without any
                    // extra special-case in the caller.
                    //
                    // Skip when the relevant material is null:
                    //   - active=true with null Active Material preserves the
                    //     legacy "no swap on activate without a material"
                    //     behaviour for actions authored before defaults
                    //     existed.
                    //   - active=false with null Default Material avoids
                    //     blanking renderers that never had a default
                    //     configured (e.g. category-1 Apply Material entries
                    //     where the user doesn't care about restoration).
                    if (action.targetRenderer == null) continue;
                    Material chosen = active ? action.targetMaterial : action.defaultMaterial;
                    if (chosen == null) continue;

                    Material[] sm = action.targetRenderer.sharedMaterials;
                    int matIdx = action.materialIndex;
                    if (sm == null || matIdx < 0 || matIdx >= sm.Length) continue;
                    if (sm[matIdx] == chosen) continue; // already correct, avoid dirtying
                    sm[matIdx] = chosen;
                    action.targetRenderer.sharedMaterials = sm;
                }
                else if (t == 2) // Set Shader Property
                {
                    if (action.targetRenderer == null || string.IsNullOrEmpty(action.propertyName)) continue;
                    Material[] mats = action.targetRenderer.sharedMaterials;
                    int matIdx = action.materialIndex;
                    if (matIdx < 0 || matIdx >= mats.Length || mats[matIdx] == null) continue;
                    Material mat = mats[matIdx];

                    int propType = action.propertyType;
                    if (propType == 0)
                        mat.SetFloat(action.propertyName, active ? action.propertyFloatValue : action.defaultFloatValue);
                    else if (propType == 1)
                        mat.SetColor(action.propertyName, active ? action.propertyColorValue : action.defaultColorValue);
                    else if (propType == 2)
                        mat.SetVector(action.propertyName, active ? action.propertyVectorValue : action.defaultVectorValue);
                    else if (propType == 3)
                        mat.SetTexture(action.propertyName, active ? action.targetTexture : null);

                    // Enable keyword when the toggle property goes non-zero.
                    // Never disable — shader_feature_local variants may be
                    // unloaded by DisableKeyword, making them unrecoverable.
                    // Effect visibility is controlled by the property value alone.
                    var kwInfo = EnigmaShaderHelper.GetPropertyKeywordInfo(mat, action.propertyName);
                    if (kwInfo.keyword != null && kwInfo.toggleProp == action.propertyName)
                    {
                        float val = active ? action.propertyFloatValue : action.defaultFloatValue;
                        if (val > 0.5f)
                            mat.EnableKeyword(kwInfo.keyword);
                    }
                }
                else if (t == 27) // Shader Keyword (legacy)
                {
                    // Keywords are permanently enabled by PrepareAndLock.
                    // Legacy type 27 actions are kept for backwards compatibility
                    // but no longer toggle keywords at play mode entry/exit.
                }
                else if (t == 22) // Toggle Skybox (stateful: on=apply material to RenderSettings)
                {
                    // Only act on activate. On deactivate the runtime reverts to
                    // _initialSkybox (captured at Start), which at edit time
                    // is simply whatever the user authored in the Lighting
                    // window — so we leave RenderSettings.skybox alone on
                    // active=false rather than try to guess a "default".
                    if (active && action.targetMaterial != null
                        && RenderSettings.skybox != action.targetMaterial)
                    {
                        RenderSettings.skybox = action.targetMaterial;
                        DynamicGI.UpdateEnvironment();
                    }
                }
                else if (t == 6) // Set Udon Variable
                {
                    if (action.targetUdon == null || string.IsNullOrEmpty(action.udonVariableName)) continue;

                    // udonVariableType convention (EnigmaDataModel.cs:239):
                    //   0=bool, 1=float, 2=int, 3=string
                    // propertyFloatValue is the "active" payload; defaultFloatValue
                    // is the restored value when the entry is off. The string type
                    // has no explicit off-state payload, so inactive writes the
                    // empty string (matches the runtime default).
                    object val;
                    switch (action.udonVariableType)
                    {
                        case 0: val = active ? action.propertyFloatValue > 0.5f : action.defaultFloatValue > 0.5f; break;
                        case 2: val = (int)(active ? action.propertyFloatValue : action.defaultFloatValue); break;
                        case 3: val = active ? (action.udonVariableStringValue ?? "") : ""; break;
                        case 1:
                        default: val = active ? action.propertyFloatValue : action.defaultFloatValue; break;
                    }
                    WriteUdonFieldForBuild(action.targetUdon, action.udonVariableName, val);
                }
            }
        }
        /// <summary>
        /// Finds and sets the float/int property that drives a shader keyword.
        /// Custom shader editors (Mochie, Poiyomi, etc.) read a toggle property
        /// and call EnableKeyword/DisableKeyword based on its value. If we only
        /// toggle the keyword without updating the property, the editor re-syncs
        /// the keyword from the stale property value on the next inspector repaint.
        ///
        /// Strategy:
        /// 1. Find properties with [Toggle(KEYWORD)] attribute (Unity built-in convention)
        /// 2. Fall back to properties with [Toggle]/[ToggleUI]/[ToggleOff] that have a
        ///    toggle-like value (0 or 1) matching the keyword's PREVIOUS state.
        ///    We call this BEFORE changing the keyword, so we can use correlation.
        /// </summary>
        private static void SetKeywordToggleProperty(Material mat, string keyword, bool enable)
        {
            if (mat == null || mat.shader == null) return;
            Shader shader = mat.shader;
            int count = shader.GetPropertyCount();
            int targetInt = enable ? 1 : 0;

            // Pass 1: exact [Toggle(KEYWORD)] or [ToggleOff(KEYWORD)] match.
            for (int i = 0; i < count; i++)
            {
                if (!IsToggleCompatibleType(shader.GetPropertyType(i))) continue;

                string[] attrs = shader.GetPropertyAttributes(i);
                if (attrs == null) continue;
                foreach (string attr in attrs)
                {
                    if (attr.Contains(keyword))
                    {
                        SetToggleValue(mat, shader, i, targetInt);
                        return;
                    }
                }
            }

            // Pass 2: [Toggle], [ToggleUI], or [ToggleOff] properties whose current
            // value disagrees with 'enable'. Since the keyword was already toggled by
            // the caller, a correlated toggle property still has the OLD value.
            for (int i = 0; i < count; i++)
            {
                if (!IsToggleCompatibleType(shader.GetPropertyType(i))) continue;

                string[] attrs = shader.GetPropertyAttributes(i);
                if (attrs == null) continue;

                bool isToggle = false;
                foreach (string attr in attrs)
                {
                    if (attr == "Toggle" || attr == "ToggleUI" || attr == "ToggleOff")
                    { isToggle = true; break; }
                }
                if (!isToggle) continue;

                string propName = shader.GetPropertyName(i);
                int curVal = GetToggleValue(mat, shader, i);
                bool propState = curVal >= 1;
                if (propState != enable)
                {
                    SetToggleValue(mat, shader, i, targetInt);
                    return;
                }
            }
        }

        private static bool IsToggleCompatibleType(UnityEngine.Rendering.ShaderPropertyType type)
        {
            return type == UnityEngine.Rendering.ShaderPropertyType.Float
                || type == UnityEngine.Rendering.ShaderPropertyType.Range
#if UNITY_2021_1_OR_NEWER
                || type == UnityEngine.Rendering.ShaderPropertyType.Int
#endif
                ;
        }

        /// <summary>
        /// Reads a toggle property using the correct getter for its type.
        /// Int-type properties must use GetInt; Float/Range use GetFloat.
        /// </summary>
        private static int GetToggleValue(Material mat, Shader shader, int propIdx)
        {
#if UNITY_2021_1_OR_NEWER
            if (shader.GetPropertyType(propIdx) == UnityEngine.Rendering.ShaderPropertyType.Int)
                return mat.GetInt(shader.GetPropertyName(propIdx));
#endif
            return mat.GetFloat(shader.GetPropertyName(propIdx)) >= 0.5f ? 1 : 0;
        }

        /// <summary>
        /// Sets a toggle property using the correct setter for its type.
        /// Int-type properties must use SetInt; Float/Range use SetFloat.
        /// Mochie uses GetInt to read toggle state, so SetFloat won't work.
        /// </summary>
        private static void SetToggleValue(Material mat, Shader shader, int propIdx, int value)
        {
            string propName = shader.GetPropertyName(propIdx);
#if UNITY_2021_1_OR_NEWER
            if (shader.GetPropertyType(propIdx) == UnityEngine.Rendering.ShaderPropertyType.Int)
            {
                mat.SetInt(propName, value);
                return;
            }
#endif
            mat.SetFloat(propName, value);
        }
    }
}
#endif

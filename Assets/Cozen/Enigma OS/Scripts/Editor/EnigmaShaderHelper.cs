#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// Generic shader locking integration for the Enigma OS build step.
    ///
    /// Automatically detects module-based shader systems (like June 5) by parsing
    /// the shader's Properties block for <c>[ToggleUI] _Keyword*</c> patterns,
    /// sets the appropriate <c>_LockingModule*</c> and <c>_Keyword*</c> properties
    /// on the material, and invokes the shader's lock compiler via reflection.
    ///
    /// Works for any shader that follows the <c>_Keyword*</c> / <c>_LockingModule*</c>
    /// convention. Shaders without these properties (Mochie, TacoFX, etc.) are
    /// silently skipped — no locking needed.
    /// </summary>
    internal static class EnigmaShaderHelper
    {
        // ════════════════════════════════════════════════════════════════════════
        //  CACHES
        // ════════════════════════════════════════════════════════════════════════

        // shaderAssetPath → (propertyName → moduleKeywordName)
        private static readonly Dictionary<string, Dictionary<string, string>> _moduleMapCache
            = new Dictionary<string, Dictionary<string, string>>();

        // shaderAssetPath → (propertyName → (shaderKeyword, togglePropertyName))
        private static readonly Dictionary<string, Dictionary<string, (string keyword, string toggle)>>
            _shaderFeatureCache = new Dictionary<string, Dictionary<string, (string, string)>>();

        // Toggle property → keyword aliases for popular shaders where naming
        // conventions don't apply. Keyed by property name (with leading underscore).
        // Used in MatchToggleToKeyword for toggle→keyword resolution.
        private static readonly Dictionary<string, string> _knownAliases =
            new Dictionary<string, string>
            {
                { "_FilterModel", "_COLOR_ON" },                // Mochie Screen FX
                { "_RGBSplit",    "_CHROMATIC_ABBERATION_ON" }, // Mochie Screen FX (note: shader has typo "ABBERATION")
            };

        // Direct property → (keyword, toggle) overrides for properties in groups
        // where naming is too inconsistent for automatic grouping. Applied after
        // the main parser runs, patching any properties that were missed.
        private static readonly Dictionary<string, (string keyword, string toggle)> _knownPropertyOverrides =
            new Dictionary<string, (string, string)>
            {
                // Mochie Screen FX: _FilterModel (_COLOR_ON) group has properties
                // with unrelated names that don't share the "Filter" prefix.
                { "_Color",          ("_COLOR_ON", "_FilterModel") },
                { "_RGB",            ("_COLOR_ON", "_FilterModel") },
                { "_Hue",            ("_COLOR_ON", "_FilterModel") },
                { "_HueMode",        ("_COLOR_ON", "_FilterModel") },
                { "_Saturation",     ("_COLOR_ON", "_FilterModel") },
                { "_SaturationR",    ("_COLOR_ON", "_FilterModel") },
                { "_SaturationG",    ("_COLOR_ON", "_FilterModel") },
                { "_SaturationB",    ("_COLOR_ON", "_FilterModel") },
                { "_Value",          ("_COLOR_ON", "_FilterModel") },
                { "_Brightness",     ("_COLOR_ON", "_FilterModel") },
                { "_Contrast",       ("_COLOR_ON", "_FilterModel") },
                { "_HDR",            ("_COLOR_ON", "_FilterModel") },
                { "_Invert",         ("_COLOR_ON", "_FilterModel") },
                { "_InvertR",        ("_COLOR_ON", "_FilterModel") },
                { "_InvertG",        ("_COLOR_ON", "_FilterModel") },
                { "_InvertB",        ("_COLOR_ON", "_FilterModel") },
                { "_Amplitude",      ("_SHAKE_ON", "_ShakeModel") },
                { "_ScanLine",       ("_NOISE_ON", "_NoiseMode") },
                { "_ScanLineThick",  ("_NOISE_ON", "_NoiseMode") },
                { "_ScanLineSpeed",  ("_NOISE_ON", "_NoiseMode") },
            };

        // Reserved for future use. Runtime keyword toggling via EnableKeyword/
        // DisableKeyword IS supported in VRChat's Udon VM, so keywords are managed
        // at runtime by the executor. This table is kept as an extensibility point.
        private static readonly Dictionary<string, string> _passGatingKeywords =
            new Dictionary<string, string>();

        // Method names to search for on discovered lock compiler types.
        // Searched on classes in the shader editor's namespace that have a Material constructor.
        private static readonly string[] LockMethodNames =
            { "execute", "Execute", "lock", "Lock", "bake", "Bake" };

        // ════════════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prepares a material for shader locking based on the set of property names
        /// actually used by Enigma OS actions. Detects modules, sets locking properties,
        /// and invokes the lock compiler if one is registered.
        ///
        /// Safe to call on any material — silently skips shaders without locking support.
        /// </summary>
        /// <param name="material">The target material.</param>
        /// <param name="usedPropertyNames">Property names used by controller/button actions.</param>
        public static void PrepareAndLock(Material material, HashSet<string> usedPropertyNames)
        {
            if (material == null || material.shader == null || usedPropertyNames.Count == 0)
                return;

            // Step 1: Parse shader to find property → module mapping (if any).
            var moduleMap = GetModuleMap(material.shader);
            bool hasModules = moduleMap != null && moduleMap.Count > 0;

            // Step 2: If modules exist, set _Keyword* and _LockingModule* properties.
            if (hasModules)
            {
                var usedModuleKeywords = new HashSet<string>();
                var allModuleKeywords = new HashSet<string>(moduleMap.Values);

                foreach (string propName in usedPropertyNames)
                {
                    if (moduleMap.TryGetValue(propName, out string moduleKeyword))
                        usedModuleKeywords.Add(moduleKeyword);
                }

                // Check if the material has _LockingModule* properties.
                bool hasLocking = false;
                foreach (string moduleKeyword in allModuleKeywords)
                {
                    if (material.HasProperty(KeywordToLockingModule(moduleKeyword)))
                    { hasLocking = true; break; }
                }

                if (hasLocking && usedModuleKeywords.Count > 0)
                {
                    foreach (string moduleKeyword in allModuleKeywords)
                    {
                        bool used = usedModuleKeywords.Contains(moduleKeyword);
                        if (material.HasProperty(moduleKeyword))
                            material.SetFloat(moduleKeyword, used ? 1f : 0f);
                        string lockingProp = KeywordToLockingModule(moduleKeyword);
                        if (material.HasProperty(lockingProp))
                            material.SetFloat(lockingProp, used ? 1f : 0f);
                    }

                    Debug.Log($"[EnigmaOS] Shader locking prepared for '{material.name}': " +
                              $"{usedModuleKeywords.Count}/{allModuleKeywords.Count} modules enabled.");
                }
            }

            // Step 3: Enable shader_feature_local keywords for all used properties.
            int kwEnabled = EnableRequiredKeywords(material, usedPropertyNames);
            if (kwEnabled > 0)
                Debug.Log($"[EnigmaOS] Enabled {kwEnabled} shader keyword(s) on '{material.name}' for variant preservation.");

            // Step 4: Set enable-toggle float properties to 1 for used effects.
            // Lock compilers (e.g., BeanFX) read _Enable* floats to decide which
            // effects to compile into the generated shader variant. Without this,
            // effects that start disabled (default 0) get stripped entirely.
            // Original values are saved and restored after the lock compiler runs.
            var savedToggleValues = new Dictionary<string, float>();
            var featureMap = GetShaderFeatureMap(material.shader);

            // Diagnostic — tracks why locking sometimes produces a 0-effect
            // variant during VRC Build. Prints the state the lock compiler
            // will see. Leave in place until the 0-effect regression is
            // closed; remove once regression tests cover this path.
            Debug.Log($"[EnigmaOS][LockDiag] PrepareAndLock step 4 on '{material.name}' (shader='{material.shader.name}'): " +
                      $"featureMap={(featureMap == null ? "null" : featureMap.Count.ToString())}, " +
                      $"usedProps={usedPropertyNames.Count}");

            if (featureMap != null)
            {
                var enabledToggles = new HashSet<string>();
                foreach (string prop in usedPropertyNames)
                {
                    if (featureMap.TryGetValue(prop, out var info) && info.toggle != null
                        && !enabledToggles.Contains(info.toggle))
                    {
                        if (material.HasProperty(info.toggle))
                        {
                            savedToggleValues[info.toggle] = material.GetFloat(info.toggle);
                            material.SetFloat(info.toggle, 1f);
                            enabledToggles.Add(info.toggle);
                        }
                    }
                }
                Debug.Log($"[EnigmaOS][LockDiag]   step 4 set {enabledToggles.Count} toggle(s) to 1: [{string.Join(", ", enabledToggles)}]");
            }

            EditorUtility.SetDirty(material);

            // Step 5: Discover and invoke a lock compiler if one exists.
            Debug.Log($"[EnigmaOS][LockDiag]   step 5: invoking lock compiler on '{material.name}' (shader='{material.shader.name}')");
            InvokeLockCompiler(material);
            Debug.Log($"[EnigmaOS][LockDiag]   step 5 complete: shader is now '{material.shader.name}'");

            // Step 6: Leave toggle properties at 1 for locked materials.
            //
            // We previously restored saved _EnableX values here (typically
            // back to 0) to avoid effects visually rendering "on" at scene
            // start. That restore breaks Unity's material serialization on
            // shaders that declare `[Toggle(KEYWORD)] _EnableX` — the
            // standard BeanFX convention for every effect toggle. On save,
            // Unity enforces the `[Toggle(KW)] _EnableX` contract: if
            // _EnableX = 0 at serialize time, KW is removed from
            // m_ValidKeywords even if we called EnableKeyword a moment
            // earlier. The result: shader variant has the shader_feature
            // pragmas compiled in, but the material's keyword list is
            // empty, so BeanFX reports "0 compiled, 43 stripped" and the
            // shader stripper in the build drops the variants entirely.
            // Runtime SetFloat("_EnableX", 1) then toggles a keyword the
            // built bundle no longer contains, and the effect silently
            // does nothing.
            //
            // BeanFX's own manual LockAllMaterials flow leaves _EnableX at
            // 1 for every checked effect at save time (see
            // BeanFXEditor.cs:2107-2126 and LockMaterialSilent's same-
            // pattern loop) — that's the reason the menu-driven lock works.
            // Matching that behaviour here keeps m_ValidKeywords intact and
            // preserves the variant through the build bundle.
            //
            // "Effect visible at scene start" worry: the Enigma runtime
            // executor writes per-entry _EnableX values from its rt arrays
            // on Start() / OnEnable() before the first rendered frame,
            // re-baselining every toggle based on the entry's actual
            // default state. Udon's first-frame timing means there's no
            // visible flash of effects-on even though the material loads
            // with _EnableX = 1. For effects whose Enigma entry is
            // default-off, the runtime's synthetic toggle action writes
            // _EnableX = 0 + DisableKeyword before render, hiding the
            // effect correctly. For default-on entries the baseline
            // _EnableX = 1 is the desired visible state anyway.
            //
            // savedToggleValues is kept as an empty-path no-op below so
            // shader types without the [Toggle(KW)] pattern can still opt
            // into the restore by having step 4 populate it — they just
            // won't today. Explicit restore per-shader-type is a future
            // refinement once we have a second example shader to compare.
            //
            // (Leave savedToggleValues unused here — effectively step 6 is
            // now a no-op for BeanFX-style shaders.)

            // Keyword sync is handled by ApplyDefaultMaterialState (play mode only)
            // and by the executor at runtime. PrepareAndLock must NOT disable keywords
            // because it runs during both play mode AND VRC builds — disabling here
            // would strip shader variants from the build.
        }

        /// <summary>
        /// Disables keywords that gate entire shader passes without runtime property
        /// guards, when their associated toggle property is off (0). Call after
        /// EnableRequiredKeywords to fix up known problematic keywords.
        /// </summary>
        public static void DisablePassGatingKeywords(Material material)
        {
            if (material == null) return;
            foreach (var kvp in _passGatingKeywords)
            {
                if (material.HasProperty(kvp.Value) && material.GetFloat(kvp.Value) < 0.5f)
                    material.DisableKeyword(kvp.Key);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MODULE MAP PARSER
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns a dictionary mapping each shader property name to its parent
        /// module keyword name. Parsed from the shader file's Properties block.
        /// Returns null if the shader file can't be found or has no modules.
        /// </summary>
        private static Dictionary<string, string> GetModuleMap(Shader shader)
        {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return null;

            if (_moduleMapCache.TryGetValue(shaderPath, out var cached))
                return cached;

            var map = ParsePropertiesBlock(shaderPath);
            _moduleMapCache[shaderPath] = map;
            return map;
        }

        /// <summary>
        /// Strips quoted string literals and trailing line comments from a
        /// shader-source line so brace counting isn't skewed by braces inside
        /// property display names (e.g. <c>_Foo("{weird} label", Float)</c>)
        /// or commented-out code. Quotes are removed first so a <c>//</c>
        /// inside a string (e.g. a URL in a label) isn't mistaken for a
        /// comment. An unbalanced quote truncates the rest of the line —
        /// safe for depth tracking since an open string can't contain a
        /// structural brace the parser should honor.
        /// </summary>
        private static string StripForBraceCount(string line)
        {
            int q = line.IndexOf('"');
            while (q >= 0)
            {
                int close = line.IndexOf('"', q + 1);
                if (close < 0) { line = line.Substring(0, q); break; }
                line = line.Remove(q, close - q + 1);
                q = line.IndexOf('"');
            }
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            return line;
        }

        /// <summary>
        /// Parses the Properties block of a .shader file to find module groupings.
        /// Walks properties linearly: any [ToggleUI] _Keyword* property marks a new
        /// module; all subsequent properties belong to that module until the next
        /// _Keyword* appears.
        /// </summary>
        private static Dictionary<string, string> ParsePropertiesBlock(string shaderPath)
        {
            var map = new Dictionary<string, string>();

            string[] lines;
            try { lines = File.ReadAllLines(shaderPath); }
            catch { return map; }

            // Regex to match [ToggleUI] _Keyword{Name}("...", Float) = ...
            var keywordRegex = new Regex(
                @"\[ToggleUI\]\s+(_Keyword\w+)\s*\(",
                RegexOptions.Compiled);

            // Regex to match any property declaration: _PropertyName("...", ...)
            var propertyRegex = new Regex(
                @"^\s*(?:\[[^\]]+\]\s*)*(_\w+)\s*\(",
                RegexOptions.Compiled);

            bool inProperties = false;
            int braceDepth = 0;
            string currentModule = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                // Find the Properties block.
                if (!inProperties)
                {
                    if (trimmed.StartsWith("Properties"))
                    {
                        inProperties = true;
                        braceDepth = 0;
                        foreach (char c in StripForBraceCount(trimmed))
                        {
                            if (c == '{') braceDepth++;
                            else if (c == '}') braceDepth--;
                        }
                    }
                    continue;
                }

                // Track brace depth to know when Properties block ends.
                // Quotes/comments are stripped first so braces inside
                // property display names don't end the block early.
                foreach (char c in StripForBraceCount(trimmed))
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }
                if (braceDepth <= 0 && inProperties)
                    break;

                // Check for a keyword toggle (module boundary).
                var keywordMatch = keywordRegex.Match(trimmed);
                if (keywordMatch.Success)
                {
                    currentModule = keywordMatch.Groups[1].Value;
                    // The keyword property itself also belongs to its module.
                    map[currentModule] = currentModule;
                    continue;
                }

                // Check for a regular property declaration.
                if (currentModule != null)
                {
                    var propMatch = propertyRegex.Match(trimmed);
                    if (propMatch.Success)
                    {
                        string propName = propMatch.Groups[1].Value;
                        // Skip _LockingModule* and _Locking* properties — these are
                        // locking metadata, not user-facing effect properties.
                        if (!propName.StartsWith("_Locking"))
                            map[propName] = currentModule;
                    }
                }
            }

            return map;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  LOCKING NAME DERIVATION
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts a _Keyword* property name to its corresponding _LockingModule* name.
        /// Example: "_KeywordChromaticAberration" → "_LockingModuleChromaticaberration"
        /// Verified against all 24 June 5 modules.
        /// </summary>
        private static string KeywordToLockingModule(string keywordProp)
        {
            if (!keywordProp.StartsWith("_Keyword") || keywordProp.Length <= "_Keyword".Length)
                return keywordProp;

            string suffix = keywordProp.Substring("_Keyword".Length);
            string lower = suffix.ToLowerInvariant();
            return "_LockingModule" + char.ToUpper(lower[0]) + lower.Substring(1);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  LOCK COMPILER AUTO-DISCOVERY & INVOCATION
        // ════════════════════════════════════════════════════════════════════════

        // Static method names for Pattern B (Poiyomi/Thry-style).
        private static readonly string[] StaticLockMethodNames =
            { "SetLockedForAllMaterials", "LockMaterial", "LockMaterialSilent", "Lock", "lock", "Bake", "bake" };

        // Static method names for the UNLOCK side. Two families:
        //   A) Shared lock/unlock entry points where the second arg is a lock state
        //      (Poiyomi/Thry): <c>SetLockedForAllMaterials(mats, 0)</c> unlocks.
        //   B) Dedicated unlock methods (BeanFX): <c>UnlockMaterialSilent(mat)</c>.
        // We try Family A first so a shader that exposes SetLockedForAllMaterials
        // matches the same entry point we used to lock; fall back to Family B.
        private static readonly string[] StaticUnlockMethodNames =
            { "SetLockedForAllMaterials", "UnlockMaterial", "UnlockMaterialSilent", "Unlock", "unlock" };

        /// <summary>
        /// Auto-discovers and invokes a lock compiler for the material's shader.
        ///
        /// Supports two patterns:
        ///   Pattern A (June-style): A class with a Material constructor and a public
        ///     parameterless instance method (execute/lock/bake).
        ///   Pattern B (Poiyomi/Thry-style): A class with a public static method that
        ///     accepts materials and a lock state int.
        ///
        /// Discovery searches the same namespace as the shader's CustomEditor class.
        /// Silently skips if no lock compiler is found.
        /// </summary>
        private static void InvokeLockCompiler(Material material)
        {
            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            // Step 1: Find CustomEditor class name from shader file.
            string customEditorName = ParseCustomEditor(shaderPath);
            if (string.IsNullOrEmpty(customEditorName))
                return;

            // Step 2: Find the CustomEditor type to determine its namespace/assembly.
            Type editorType = null;
            Assembly editorAssembly = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                editorType = assembly.GetType(customEditorName);
                if (editorType != null) { editorAssembly = assembly; break; }
            }
            if (editorType == null)
                return;

            string editorNamespace = editorType.Namespace ?? "";

            // Candidate ordering: prefer types whose names suggest a lock
            // compiler (Lock / Optimiz / Generator / Compiler / Bake) over
            // arbitrary namespace siblings. The generic method-name probes
            // below (execute/lock/bake) are loose enough that an unrelated
            // type with a Material constructor and an "Execute" method could
            // otherwise be invoked first on shaders we've never seen.
            var orderedTypes = new List<Type>();
            foreach (var type in editorAssembly.GetTypes())
            {
                if ((type.Namespace ?? "") != editorNamespace) continue;
                if (LooksLikeLockCompilerType(type)) orderedTypes.Add(type);
            }
            foreach (var type in editorAssembly.GetTypes())
            {
                if ((type.Namespace ?? "") != editorNamespace) continue;
                if (!LooksLikeLockCompilerType(type)) orderedTypes.Add(type);
            }

            // Step 3A: Search for Pattern A — instance class with Material constructor
            // and a parameterless lock method.
            foreach (var type in orderedTypes)
            {
                if (type.IsAbstract || type.IsInterface) continue;

                var ctor = type.GetConstructor(new[] { typeof(Material) });
                if (ctor == null) continue;

                foreach (string methodName in LockMethodNames)
                {
                    var method = type.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type, new object[] { material });
                            method.Invoke(instance, null);
                            Debug.Log($"[EnigmaOS] Locked '{material.name}' via {type.Name}.{methodName}().");
                        }
                        catch (TargetInvocationException ex)
                        {
                            Debug.LogError($"[EnigmaOS] Lock failed for '{material.name}': {ex.InnerException?.Message ?? ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[EnigmaOS] Lock error for '{material.name}': {ex.Message}");
                        }
                        return;
                    }
                }
            }

            // Step 3B: Search for Pattern B — static method that accepts materials.
            // Checks for: (IEnumerable<Material>, int), (Material[], int), or (Material, int)
            // NOTE: Don't skip abstract types here — C# static classes are abstract sealed,
            // and lock compilers like BeanFXLayerGenerator are static classes.
            foreach (var type in orderedTypes)
            {
                if (type.IsInterface) continue;

                foreach (string methodName in StaticLockMethodNames)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != methodName) continue;
                        var parameters = method.GetParameters();

                        try
                        {
                            // (IEnumerable<Material>, int, ...) — pass single-element list + lockState=1
                            if (parameters.Length >= 2
                                && typeof(System.Collections.Generic.IEnumerable<Material>).IsAssignableFrom(parameters[0].ParameterType)
                                && parameters[1].ParameterType == typeof(int))
                            {
                                // Build args: required params + defaults for optional ones.
                                var args = new object[parameters.Length];
                                args[0] = new Material[] { material };
                                args[1] = 1; // lock state = locked
                                for (int i = 2; i < parameters.Length; i++)
                                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : GetDefault(parameters[i].ParameterType);
                                method.Invoke(null, args);
                                Debug.Log($"[EnigmaOS] Locked '{material.name}' via {type.Name}.{methodName}().");
                                return;
                            }

                            // (Material, int) — single material + lockState
                            if (parameters.Length == 2
                                && parameters[0].ParameterType == typeof(Material)
                                && parameters[1].ParameterType == typeof(int))
                            {
                                method.Invoke(null, new object[] { material, 1 });
                                Debug.Log($"[EnigmaOS] Locked '{material.name}' via {type.Name}.{methodName}().");
                                return;
                            }

                            // (Material) — single material, no lock state
                            if (parameters.Length == 1
                                && parameters[0].ParameterType == typeof(Material))
                            {
                                method.Invoke(null, new object[] { material });
                                Debug.Log($"[EnigmaOS] Locked '{material.name}' via {type.Name}.{methodName}().");
                                return;
                            }
                        }
                        catch (TargetInvocationException ex)
                        {
                            Debug.LogError($"[EnigmaOS] Lock failed for '{material.name}': {ex.InnerException?.Message ?? ex.Message}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[EnigmaOS] Lock error for '{material.name}': {ex.Message}");
                            return;
                        }
                    }
                }
            }
        }

        private static object GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        /// <summary>
        /// True when the type's name suggests it's a shader lock compiler.
        /// Used only for candidate ORDERING in <see cref="InvokeLockCompiler"/>
        /// — non-matching types are still probed afterwards, so shaders whose
        /// compiler has an unconventional name (June-style) keep working.
        /// </summary>
        private static bool LooksLikeLockCompilerType(Type t)
        {
            string n = t.Name.ToLowerInvariant();
            return n.Contains("lock") || n.Contains("optimiz") || n.Contains("generator")
                || n.Contains("compiler") || n.Contains("bake");
        }

        /// <summary>
        /// Restores a material from its locked/generated shader back to its
        /// editable base shader. Mirrors <see cref="InvokeLockCompiler"/>:
        /// discovers a static unlock method on a class in the shader's custom
        /// editor namespace and invokes it.
        ///
        /// Supports two unlock signatures:
        ///   A) Shared lock/unlock entry: <c>SetLockedForAllMaterials(mats, 0)</c>
        ///      (Poiyomi/Thry-style — same function used to lock and unlock,
        ///      second arg is the lock-state int).
        ///   B) Dedicated unlock method: <c>UnlockMaterialSilent(mat)</c>
        ///      (BeanFX-style — separate named function for unlocking).
        ///
        /// Silently skips when no unlock entry exists (shader has no unlock
        /// concept, or the shader's lock compiler was purely instance-based
        /// with no reverse operation). Also silently skips when the material
        /// is already using the base shader (not locked), since the unlock
        /// method is typically a no-op for an already-unlocked material.
        /// </summary>
        private static void InvokeUnlockCompiler(Material material)
        {
            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            string customEditorName = ParseCustomEditor(shaderPath);
            if (string.IsNullOrEmpty(customEditorName))
                return;

            Type editorType = null;
            Assembly editorAssembly = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                editorType = assembly.GetType(customEditorName);
                if (editorType != null) { editorAssembly = assembly; break; }
            }
            if (editorType == null)
                return;

            string editorNamespace = editorType.Namespace ?? "";

            foreach (var type in editorAssembly.GetTypes())
            {
                // Static classes are emitted as abstract sealed — don't skip
                // abstract here (BeanFXLayerGenerator is a static class).
                if (type.IsInterface) continue;
                if ((type.Namespace ?? "") != editorNamespace) continue;

                foreach (string methodName in StaticUnlockMethodNames)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != methodName) continue;
                        var parameters = method.GetParameters();

                        try
                        {
                            // Family A — (IEnumerable<Material>, int state, …) with state=0.
                            // Only meaningful for shared lock/unlock entry points.
                            if (methodName == "SetLockedForAllMaterials"
                                && parameters.Length >= 2
                                && typeof(System.Collections.Generic.IEnumerable<Material>).IsAssignableFrom(parameters[0].ParameterType)
                                && parameters[1].ParameterType == typeof(int))
                            {
                                var args = new object[parameters.Length];
                                args[0] = new Material[] { material };
                                args[1] = 0; // lock state = unlocked
                                for (int i = 2; i < parameters.Length; i++)
                                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : GetDefault(parameters[i].ParameterType);
                                method.Invoke(null, args);
                                Debug.Log($"[EnigmaOS] Unlocked '{material.name}' via {type.Name}.{methodName}(…, 0).");
                                return;
                            }

                            // Family A — (Material, int) with state=0.
                            if (methodName == "SetLockedForAllMaterials"
                                && parameters.Length == 2
                                && parameters[0].ParameterType == typeof(Material)
                                && parameters[1].ParameterType == typeof(int))
                            {
                                method.Invoke(null, new object[] { material, 0 });
                                Debug.Log($"[EnigmaOS] Unlocked '{material.name}' via {type.Name}.{methodName}(mat, 0).");
                                return;
                            }

                            // Family B — (IEnumerable<Material>, …) no state, dedicated unlock name.
                            if (methodName != "SetLockedForAllMaterials"
                                && parameters.Length >= 1
                                && typeof(System.Collections.Generic.IEnumerable<Material>).IsAssignableFrom(parameters[0].ParameterType))
                            {
                                var args = new object[parameters.Length];
                                args[0] = new Material[] { material };
                                for (int i = 1; i < parameters.Length; i++)
                                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : GetDefault(parameters[i].ParameterType);
                                method.Invoke(null, args);
                                Debug.Log($"[EnigmaOS] Unlocked '{material.name}' via {type.Name}.{methodName}().");
                                return;
                            }

                            // Family B — (Material) single material, no state.
                            if (methodName != "SetLockedForAllMaterials"
                                && parameters.Length == 1
                                && parameters[0].ParameterType == typeof(Material))
                            {
                                method.Invoke(null, new object[] { material });
                                Debug.Log($"[EnigmaOS] Unlocked '{material.name}' via {type.Name}.{methodName}(mat).");
                                return;
                            }
                        }
                        catch (TargetInvocationException ex)
                        {
                            Debug.LogError($"[EnigmaOS] Unlock failed for '{material.name}': {ex.InnerException?.Message ?? ex.Message}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[EnigmaOS] Unlock error for '{material.name}': {ex.Message}");
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Public entry point for unlocking a locked material — the symmetric
        /// counterpart to <see cref="PrepareAndLock"/>. Intended to be called
        /// on play-mode exit so the inspector's shader property list contains
        /// all effect properties, not just the generated variant's subset.
        ///
        /// Idempotent: calling on an already-unlocked material is a no-op in
        /// practice (the dedicated unlock methods early-exit when the shader
        /// is already the base template; shared lock/unlock methods with
        /// state=0 likewise).
        /// </summary>
        public static void UnlockMaterial(Material material)
        {
            if (material == null || material.shader == null) return;
            InvokeUnlockCompiler(material);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Parses the <c>CustomEditor "ClassName"</c> directive from a .shader file.
        /// Returns null if not found.
        /// </summary>
        private static string ParseCustomEditor(string shaderPath)
        {
            var regex = new Regex(@"CustomEditor\s+""([^""]+)""", RegexOptions.Compiled);
            try
            {
                foreach (string line in File.ReadLines(shaderPath))
                {
                    var match = regex.Match(line);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHADER_FEATURE_LOCAL KEYWORD AUTO-DETECTION
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the shader_feature_local keyword and its toggle property that
        /// must be enabled for the given shader property to work at runtime.
        /// Returns (null, null) if no keyword association is found.
        ///
        /// Example: GetPropertyKeywordInfo(mat, "_SobelFilterOpacity")
        ///   → ("_SOBEL_FILTER_ON", "_SobelFilterToggle")
        ///
        /// Detection order:
        ///   1. Attribute-walk map (generic, no per-shader curated entries) —
        ///      walks Shader.GetPropertyAttributes() to identify section toggles
        ///      structurally and via word-overlap matching. Works for Mochie,
        ///      BeanFX, TacoFX, and other shaders that use standard Unity
        ///      attributes like [Toggle], [Toggle(KW)], [Enum(Off,...)],
        ///      [ToggleUI], or [Header].
        ///   2. Source-file parser — fallback that scans the shader's .shader
        ///      text for #pragma shader_feature_local and Properties block
        ///      attributes. Slower but handles edge cases the attribute walk
        ///      misses, plus carries the keyword string (the attribute walk
        ///      knows the toggle property but not always the keyword name).
        ///   3. Curated overrides — last-resort safety net.
        ///
        /// The attribute walk only knows the TOGGLE PROPERTY (e.g. _FilterModel),
        /// not the keyword (_COLOR_ON), since the keyword can only be discovered
        /// from the source file. So when the attribute walk produces a toggle,
        /// we still need to consult the source-file map to learn the keyword.
        /// </summary>
        public static (string keyword, string toggleProp) GetPropertyKeywordInfo(
            Material material, string propertyName)
        {
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
                return (null, null);

            // Layer 1: attribute-walk map (generic, source-free)
            var attrMap = GetAttributeToggleMap(material.shader);
            string togglePropFromAttr = null;
            if (attrMap != null && attrMap.TryGetValue(propertyName, out togglePropFromAttr)
                && togglePropFromAttr != null)
            {
                // Look up the keyword for the discovered toggle property via the
                // source-file parser. If the source-file map also knows about it,
                // return both. Otherwise return (null, toggleProp) — the action
                // drawer's TryGetEffectToggle just needs the toggle property; the
                // keyword is only needed for variant-stripping in EnableRequiredKeywords.
                var sourceMap = GetShaderFeatureMap(material.shader);
                if (sourceMap != null && sourceMap.TryGetValue(togglePropFromAttr, out var togInfo)
                    && togInfo.keyword != null)
                {
                    return (togInfo.keyword, togglePropFromAttr);
                }
                return (null, togglePropFromAttr);
            }

            // Layer 2: source-file parser (existing behavior — handles edge cases
            // and carries keyword strings the attribute walk doesn't know).
            var map = GetShaderFeatureMap(material.shader);
            if (map != null && map.TryGetValue(propertyName, out var info))
                return info;
            return (null, null);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ATTRIBUTE-WALK TOGGLE DETECTION (generic, source-free)
        //
        //  Discovers which "section toggle" property goes with each effect
        //  parameter, by walking Shader.GetPropertyAttributes() in declaration
        //  order. Works for any shader that uses standard Unity attributes:
        //
        //    Mochie : [Enum(Off,0, On,1, ...)] for section toggles, [ToggleUI]
        //             for sub-features. Sections are interleaved with their
        //             parameters → "interleaved" layout, strict mode.
        //    BeanFX : [Toggle(KEYWORD)] for section toggles, [Toggle] for
        //             sub-features. Each section toggle followed by its params
        //             → interleaved layout, strict mode.
        //    TacoFX : [Toggle] for section toggles, with all toggles clustered
        //             at the top followed by all parameters → "lumped" layout,
        //             loose mode + word-match disambiguation.
        //
        //  Layout is detected by the longest run of consecutive toggle-like
        //  properties: ≥5 → lumped, otherwise → interleaved.
        //
        //  Verified against all four shaders in this project: Mochie 15/15,
        //  TacoFX 18/18, BeanFX 11/11, June 5 graceful degradation. See plan
        //  file for details.
        // ════════════════════════════════════════════════════════════════════════

        // Cache attribute-walk results per shader instance ID. Cleared by ClearCache().
        private static readonly Dictionary<int, Dictionary<string, string>> _attributeToggleMapCache
            = new Dictionary<int, Dictionary<string, string>>();

        // Cache the built property-group structure per shader instance ID so
        // clicking the search button multiple times doesn't re-walk the shader
        // + re-run the inspector overlay + re-sort. The first click for a
        // shader builds (~100-300 ms on larger shaders like Mochie) and
        // caches; subsequent clicks are O(1) dictionary hits. Cleared by
        // ClearCache() alongside the attribute map.
        private static readonly Dictionary<int, List<ShaderPropertyGroup>> _propertyGroupsCache
            = new Dictionary<int, List<ShaderPropertyGroup>>();

        // Word tokens dropped during word-overlap matching — too generic to be
        // distinctive section markers across shaders.
        private static readonly HashSet<string> _wordMatchNoise = new HashSet<string>
        {
            "fx", "enable", "use", "the", "a"
        };

        /// <summary>
        /// Returns a property → section toggle map for the given shader, computed
        /// by walking <see cref="Shader.GetPropertyAttributes"/>. Cached per
        /// shader instance ID. Self-mappings (toggle → itself) are included so
        /// callers can detect "this property IS the toggle" by checking
        /// <c>map[name] == name</c>.
        /// </summary>
        internal static Dictionary<string, string> GetAttributeToggleMap(Shader shader)
        {
            if (shader == null) return null;
            int id = shader.GetInstanceID();
            if (_attributeToggleMapCache.TryGetValue(id, out var cached))
                return cached;

            // Step 0: detect any shader-wide "toggle marker" prefix shared by
            // ≥5 weak-toggle properties at a word boundary (e.g. June 5's
            // [ToggleUI]_Keyword<Module> convention). Properties with this
            // prefix are treated as top-level section starters regardless of
            // how their name ends — complementary to the toggle-suffix rule.
            string shaderPrefix = DetectShaderTogglePrefix(shader);

            // Step 1: pick mode based on layout
            int longestRun = LongestToggleRun(shader);
            bool strictMode = longestRun < 5;

            // Step 2: structural walk
            var (structural, toggles) = WalkShaderForToggles(shader, strictMode, shaderPrefix);

            // Step 2b: if the shader's custom inspector gives us an
            // authoritative list of section toggles (via `DoFoldout` calls),
            // merge those into the set of known toggles and rebuild the
            // structural map with the unioned section boundaries. This fixes
            // Mochie-style shaders where weak `[ToggleUI]` properties like
            // `_Letterbox` and `_DeepFry` are top-level sections in the
            // inspector but the attribute walker can't infer that from the
            // shader attributes alone (their names don't end in a toggle
            // suffix like `Toggle`/`Mode`/`Type`/`Enable`).
            var inspectorData = GetInspectorSourceData(shader);
            var inspectorSections = inspectorData?.sectionToggles;
            if (inspectorSections != null && inspectorSections.Count > 0)
            {
                bool needsRebuild = false;
                foreach (var s in inspectorSections)
                {
                    // A section from the inspector is "missing" from the
                    // walker's view if the walker didn't mark it as a
                    // self-mapping (i.e. as a section toggle).
                    if (!structural.TryGetValue(s, out var assigned) || assigned != s)
                    {
                        needsRebuild = true;
                        break;
                    }
                }
                if (needsRebuild)
                {
                    // Build the union section set: everything the walker
                    // already recognised as a section PLUS everything the
                    // inspector told us is a section. Then rebuild the map
                    // via a simple linear walk — each non-section property
                    // gets the most recent section that precedes it in
                    // shader declaration order.
                    //
                    // Orphan detection: the referenced-properties set lets
                    // the rebuild walker drop properties that aren't known
                    // to the inspector at all (e.g. Mochie's deprecated
                    // _DeepFry/_Flavor/_Heat/_Sizzle group which is still
                    // declared in the shader but removed from the UI).
                    // These fall into the ungrouped bucket instead of
                    // getting misattributed to a neighboring section.
                    var allSections = new HashSet<string>(toggles, StringComparer.Ordinal);
                    foreach (var s in inspectorSections) allSections.Add(s);
                    (structural, toggles) = RebuildMapWithExplicitSections(
                        shader, allSections, inspectorData.referencedProperties);
                }
            }

            // Step 3 + 4: combine structural with word-match for each non-toggle
            // property. We keep the structural assignment as the baseline and
            // override with a word match only when the word match's score is
            // strictly higher (handles TacoFX where structural is wrong).
            var result = new Dictionary<string, string>(structural);
            int n = shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                // Skip self-references — they're toggle properties, not effect params.
                if (structural.TryGetValue(name, out var assigned) && assigned == name)
                    continue;

                int strucScore = assigned != null ? ScoreNameMatch(name, assigned) : 0;
                var (wordToggle, wordScore) = BestWordMatch(name, toggles);

                if (wordToggle != null && wordScore > strucScore)
                    result[name] = wordToggle;
                else if (assigned == null && wordToggle != null)
                    result[name] = wordToggle;
                // else: keep structural (or null)
            }

            _attributeToggleMapCache[id] = result;
            return result;
        }

        /// <summary>
        /// Walks the shader in declaration order and builds a
        /// property → section-toggle map using <paramref name="sections"/>
        /// as the ONLY source of section starters. Each non-section property
        /// is assigned to the most recent section it comes after; properties
        /// before the first section are left unassigned (they'll end up in
        /// the ungrouped bucket of <see cref="BuildShaderPropertyGroups"/>).
        ///
        /// <para>If <paramref name="inspectorReferenced"/> is non-null, it's
        /// treated as the "known to the inspector" set: any shader property
        /// NOT in that set is considered orphaned and is dropped from the
        /// map (falls to the ungrouped bucket). This handles shaders where
        /// the Properties block has legacy properties the inspector no
        /// longer draws — without this check, they'd be silently
        /// misattributed to whichever section happens to come before them
        /// in declaration order.</para>
        ///
        /// Hidden properties (<c>[HideInInspector]</c>) are skipped unless
        /// a <see cref="DetectShaderTogglePrefix"/>-style convention is in
        /// play (handled separately by <see cref="WalkShaderForToggles"/>
        /// — this rebuild path is only triggered by custom-inspector
        /// scrapes where hidden properties typically aren't user-facing).
        /// </summary>
        private static (Dictionary<string, string> map, List<string> toggles)
            RebuildMapWithExplicitSections(
                Shader shader,
                HashSet<string> sections,
                HashSet<string> inspectorReferenced)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var toggleList = new List<string>();
            int n = shader.GetPropertyCount();
            string current = null;
            for (int i = 0; i < n; i++)
            {
                var flags = shader.GetPropertyFlags(i);
                if ((flags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0)
                    continue;

                string name = shader.GetPropertyName(i);
                if (sections.Contains(name))
                {
                    current = name;
                    toggleList.Add(name);
                    map[name] = name;
                    continue;
                }
                // Orphan check: if the inspector source exists but never
                // references this property, don't attribute it to the
                // current section — leave it unassigned so it surfaces in
                // the ungrouped bucket as "not drawn by this inspector".
                if (inspectorReferenced != null && !inspectorReferenced.Contains(name))
                    continue;

                if (current != null)
                    map[name] = current;
            }
            return (map, toggleList);
        }

        /// <summary>
        /// Pre-scans the shader's weak-toggle properties (plain <c>[Toggle]</c>
        /// and <c>[ToggleUI]</c>, no keyword) for a common naming prefix that
        /// the shader author uses to mark module/feature section starters.
        /// Some shaders — most visibly June 5 — declare every module toggle
        /// as <c>[ToggleUI]_Keyword&lt;Module&gt;</c>. Recognizing that
        /// convention lets us promote all 24 June 5 module toggles to
        /// top-level section starters in one pass, without per-shader curated
        /// entries.
        ///
        /// Returns the longest prefix (length ≥ 6) that:
        ///   - is shared by ≥ 5 weak-toggle properties (absolute floor — too
        ///     few and it's an isolated cluster rather than a convention)
        ///   - covers ≥ 50% of ALL weak-toggle properties in the shader
        ///     (dominance check — a real shader-wide convention applies to
        ///     MOST of the weak toggles, not a small group inside a larger
        ///     section)
        ///   - is followed by an uppercase letter in every matching property
        ///     (word boundary check — rejects accidental substrings like
        ///     <c>_Keyboards</c>)
        ///
        /// Dominance and the length-6 floor together exclude:
        ///   - TacoFX <c>_FX_</c> (length 4) — already handled by loose mode
        ///   - BeanFX <c>_Lidar</c> (5 of ~70 weak toggles, ~7%) — a small
        ///     cluster, not a convention
        ///   - Mochie's ~4 <c>_Auto*</c> <c>[ToggleUI]</c> properties — under
        ///     the absolute floor
        ///
        /// Returns null when no prefix meets all three criteria, in which
        /// case the walker falls back to its existing rules.
        /// </summary>
        private static string DetectShaderTogglePrefix(Shader shader)
        {
            int n = shader.GetPropertyCount();
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string[] attrs = shader.GetPropertyAttributes(i) ?? new string[0];
                bool isWeakToggle = false;
                bool isStrong = false;
                foreach (var a in attrs)
                {
                    if (a.Equals("Toggle", StringComparison.OrdinalIgnoreCase)
                        || a.Equals("ToggleUI", StringComparison.OrdinalIgnoreCase))
                        isWeakToggle = true;
                    else if (a.StartsWith("Toggle(", StringComparison.OrdinalIgnoreCase))
                        isStrong = true;
                    else if (a.StartsWith("Enum(", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(a, @"Enum\(\s*([^,)]+)\s*,\s*0", RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Trim()
                            .Equals("Off", StringComparison.OrdinalIgnoreCase))
                            isStrong = true;
                    }
                }
                // Only consider weak toggles — strong signals already drive
                // section detection via the normal path and don't need a
                // shader-level prefix override.
                if (isWeakToggle && !isStrong)
                    names.Add(shader.GetPropertyName(i));
            }
            if (names.Count < 5) return null;

            // Dominance threshold: the prefix must cover at least half of all
            // weak-toggle properties. A convention that applies to <50% is a
            // cluster, not a shader-wide marker.
            int dominanceThreshold = (names.Count + 1) / 2; // ceil(n/2)
            int absoluteFloor = 5;
            int requiredCount = Math.Max(dominanceThreshold, absoluteFloor);

            // Scan prefix lengths from longest to shortest, returning the
            // longest length where a single prefix meets BOTH the absolute
            // floor AND the dominance threshold, with every matching property
            // having an uppercase character immediately after the prefix.
            for (int prefixLen = 12; prefixLen >= 6; prefixLen--)
            {
                var counts = new Dictionary<string, int>();
                foreach (var name in names)
                {
                    if (name.Length <= prefixLen) continue;
                    char next = name[prefixLen];
                    if (!char.IsUpper(next)) continue;
                    string prefix = name.Substring(0, prefixLen);
                    counts.TryGetValue(prefix, out int c);
                    counts[prefix] = c + 1;
                }
                string best = null;
                int bestCount = 0;
                foreach (var kv in counts)
                {
                    if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
                }
                if (bestCount >= requiredCount)
                    return best;
            }
            return null;
        }

        /// <summary>
        /// Counts the longest run of consecutive toggle-like properties in the
        /// shader (any [Toggle], [Toggle(KW)], [ToggleUI], or [Enum(Off,...)]),
        /// skipping [HideInInspector] properties. Used to pick interleaved vs
        /// lumped mode in <see cref="GetAttributeToggleMap"/>.
        /// </summary>
        private static int LongestToggleRun(Shader shader)
        {
            int n = shader.GetPropertyCount();
            int longest = 0, current = 0;
            for (int i = 0; i < n; i++)
            {
                string[] attrs = shader.GetPropertyAttributes(i) ?? new string[0];
                bool isHidden = false;
                bool isToggleLike = false;
                foreach (var a in attrs)
                {
                    if (a.Equals("HideInInspector", StringComparison.OrdinalIgnoreCase))
                    { isHidden = true; }
                    else if (a.StartsWith("Toggle(", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("Toggle", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("ToggleUI", StringComparison.OrdinalIgnoreCase))
                    { isToggleLike = true; }
                    else if (a.StartsWith("Enum(", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(a, @"Enum\(\s*([^,)]+)\s*,\s*0", RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Trim().Equals("Off", StringComparison.OrdinalIgnoreCase))
                            isToggleLike = true;
                    }
                }
                if (isHidden) continue;
                if (isToggleLike) { current++; if (current > longest) longest = current; }
                else current = 0;
            }
            return longest;
        }

        /// <summary>
        /// Linear walk over the shader's properties producing a property →
        /// section-toggle map and the ordered list of section toggles.
        /// Strict mode (interleaved layout) treats <c>[Toggle]</c> and
        /// <c>[ToggleUI]</c> as sub-features; loose mode (lumped layout) treats
        /// <c>[Toggle]</c> as a section starter.
        ///
        /// In strict mode, weak toggles (<c>[Toggle]</c> / <c>[ToggleUI]</c>)
        /// whose property name ends in a toggle suffix (<c>Toggle</c>,
        /// <c>Mode</c>, <c>Type</c>, <c>Enable</c>) are promoted to
        /// **sub-section starters** within the current parent section. This
        /// handles Mochie's "Extras" block where features like Sobel Filter,
        /// Depth Buffer, Normal Map, and Rounding are each gated by their own
        /// <c>[ToggleUI]_FooToggle</c> property followed by their parameters,
        /// despite living inside the broader Outline declaration order with
        /// no <c>[Enum(Off,...)]</c> section starter between them.
        ///
        /// Other weak toggles inside a section (e.g. <c>[ToggleUI]_AutoShift</c>,
        /// <c>[ToggleUI]_RGBSplit</c>) are pure feature flags whose names don't
        /// match the suffix list, so they stay as plain sub-features.
        /// </summary>
        private static (Dictionary<string, string> map, List<string> toggles)
            WalkShaderForToggles(Shader shader, bool strictMode, string shaderTogglePrefix)
        {
            var map = new Dictionary<string, string>();
            var toggles = new List<string>();
            int n = shader.GetPropertyCount();
            string current = null;
            bool headerReset = false;

            // When the shader uses a consistent toggle-marker prefix (e.g.
            // June 5's <c>_Keyword</c>), include [HideInInspector] properties
            // in the map rather than skipping them. June 5 heavily uses
            // [HideInInspector] for module parameters that users still want
            // to bind to Enigma buttons, and they need to be auto-toggled.
            // For shaders without a prefix convention, keep the old behavior
            // (skip hidden) to avoid false positives on internal state
            // properties like Mochie's _MaterialResetCheck.
            bool includeHidden = !string.IsNullOrEmpty(shaderTogglePrefix);

            // Tracks whether the most recent toggle-suffix property was promoted
            // to a sub-section. Reset by strong section toggles. Used by the
            // "chain rule" — if we're in a run of consecutive sub-section
            // promotions (e.g. Mochie's Extras block: _RoundingToggle →
            // _NMFToggle → _DepthBufferToggle → _SobelFilterToggle), a
            // candidate without a stem-matching follower still gets promoted
            // because it's part of the same sub-section block. This handles
            // abbreviation cases like _DepthBufferToggle → _DBOpacity where
            // the dependent property doesn't share the toggle's prefix.
            bool inSubsectionChain = false;

            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                string[] attrs = shader.GetPropertyAttributes(i) ?? new string[0];
                bool hasHeader = false, isToggleKw = false, isToggleNoKw = false,
                     isToggleUI = false, isEnumOff = false, isHidden = false;
                foreach (var attr in attrs)
                {
                    if (attr.StartsWith("Header(", StringComparison.OrdinalIgnoreCase))
                        hasHeader = true;
                    else if (attr.Equals("HideInInspector", StringComparison.OrdinalIgnoreCase))
                        isHidden = true;
                    else if (attr.StartsWith("Toggle(", StringComparison.OrdinalIgnoreCase))
                        isToggleKw = true;
                    else if (attr.Equals("Toggle", StringComparison.OrdinalIgnoreCase))
                        isToggleNoKw = true;
                    else if (attr.Equals("ToggleUI", StringComparison.OrdinalIgnoreCase))
                        isToggleUI = true;
                    else if (attr.StartsWith("Enum(", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(attr, @"Enum\(\s*([^,)]+)\s*,\s*0", RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Trim().Equals("Off", StringComparison.OrdinalIgnoreCase))
                            isEnumOff = true;
                    }
                }
                if (isHidden && !includeHidden) continue;
                if (hasHeader) { current = null; headerReset = true; inSubsectionChain = false; }

                // Strong signals: always section toggles. Reset the sub-section chain.
                if (isToggleKw || isEnumOff)
                {
                    current = name; toggles.Add(name); map[name] = name;
                    headerReset = false;
                    inSubsectionChain = false;
                    continue;
                }

                // Shader-wide toggle marker prefix (e.g. June 5 _Keyword*):
                // promote weak toggles matching the prefix to top-level
                // section starters regardless of suffix rules.
                bool matchesShaderPrefix = !string.IsNullOrEmpty(shaderTogglePrefix)
                    && (isToggleNoKw || isToggleUI)
                    && name.Length > shaderTogglePrefix.Length
                    && name.StartsWith(shaderTogglePrefix, StringComparison.Ordinal)
                    && char.IsUpper(name[shaderTogglePrefix.Length]);
                if (matchesShaderPrefix)
                {
                    current = name; toggles.Add(name); map[name] = name;
                    inSubsectionChain = false;
                    continue;
                }

                // Weak [Toggle]: section toggle in loose mode, sub-feature in strict mode
                // unless its name has a toggle suffix and it can be promoted to sub-section.
                if (isToggleNoKw)
                {
                    if (!strictMode)
                    {
                        current = name; toggles.Add(name); map[name] = name;
                        headerReset = false;
                        continue;
                    }
                    if (HasToggleSuffix(name)
                        && ShouldPromoteToSubsection(shader, i, name, inSubsectionChain))
                    {
                        current = name; toggles.Add(name); map[name] = name;
                        inSubsectionChain = true;
                        continue;
                    }
                    if (current != null) map[name] = current;
                    continue;
                }

                // Weak [ToggleUI]: same logic — sub-section starter if the name ends
                // in a toggle suffix AND has dependent properties (or is in a chain).
                if (isToggleUI)
                {
                    if (!strictMode && current == null)
                    {
                        current = name; toggles.Add(name); map[name] = name;
                        continue;
                    }
                    if (HasToggleSuffix(name)
                        && ShouldPromoteToSubsection(shader, i, name, inSubsectionChain))
                    {
                        current = name; toggles.Add(name); map[name] = name;
                        inSubsectionChain = true;
                        continue;
                    }
                    if (current != null) map[name] = current;
                    continue;
                }

                // Regular property: belongs to current section.
                if (current != null) map[name] = current;
            }
            return (map, toggles);
        }

        /// <summary>
        /// Decides whether a weak toggle-suffix property at the given index
        /// should be promoted to a sub-section starter, vs. left as a plain
        /// sub-feature inside its parent section.
        ///
        /// The heuristic combines two signals:
        ///
        /// 1. **Stem match** — if the next non-toggle, non-hidden property in
        ///    declaration order starts with the candidate's stem (the name with
        ///    the toggle suffix stripped), the candidate has dependent
        ///    properties and is a real sub-section. This catches Mochie's
        ///    <c>_SobelFilterToggle → _SobelFilterColor</c>,
        ///    <c>_NMFToggle → _NMFOpacity</c>, <c>_RoundingToggle → _Rounding</c>,
        ///    and <c>_AudioLinkToggle → _AudioLinkStrength</c>.
        ///
        /// 2. **Sub-section chain** — if the most recent toggle-suffix property
        ///    in the current section was already promoted, the candidate is in
        ///    the middle of a "sub-section block" and gets promoted even if its
        ///    own name doesn't match its dependents. This catches Mochie's
        ///    <c>_DepthBufferToggle → _DBOpacity</c> where the dependent uses
        ///    an abbreviated prefix that doesn't match the toggle's full name.
        ///
        /// Properties that satisfy NEITHER signal are binary feature flags that
        /// happen to end in a toggle suffix (e.g. BeanFX's <c>_RainbowBlendMode</c>,
        /// followed only by other <c>_Rainbow*</c> properties belonging to the
        /// parent <c>_EnableRainbow</c> section). They stay as sub-features.
        /// </summary>
        private static bool ShouldPromoteToSubsection(
            Shader shader, int index, string name, bool inSubsectionChain)
        {
            string stem = StripToggleSuffix(name);
            if (string.IsNullOrEmpty(stem)) return false;

            int n = shader.GetPropertyCount();
            // Look ahead at up to 5 following non-hidden properties. Stop at
            // the next toggle-like property (since that marks a new boundary).
            int seen = 0;
            for (int j = index + 1; j < n && seen < 5; j++)
            {
                string[] attrs = shader.GetPropertyAttributes(j) ?? new string[0];
                bool isHidden = false, isAnyToggle = false;
                foreach (var attr in attrs)
                {
                    if (attr.Equals("HideInInspector", StringComparison.OrdinalIgnoreCase))
                        isHidden = true;
                    else if (attr.StartsWith("Toggle(", StringComparison.OrdinalIgnoreCase)
                          || attr.Equals("Toggle", StringComparison.OrdinalIgnoreCase)
                          || attr.Equals("ToggleUI", StringComparison.OrdinalIgnoreCase))
                        isAnyToggle = true;
                    else if (attr.StartsWith("Enum(", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(attr, @"Enum\(\s*([^,)]+)\s*,\s*0", RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Trim().Equals("Off", StringComparison.OrdinalIgnoreCase))
                            isAnyToggle = true;
                    }
                }
                if (isHidden) continue;
                if (isAnyToggle) break; // boundary — next toggle starts its own context
                seen++;

                string nextName = shader.GetPropertyName(j);
                if (nextName != null && nextName.StartsWith(stem, StringComparison.Ordinal)
                    && nextName.Length > stem.Length)
                    return true; // stem-matching dependent found → real sub-section
            }

            // No stem-matching dependent. Fall back to the chain rule: if a
            // prior toggle-suffix property in this section was already promoted,
            // we're in a chain and this one belongs in it too.
            return inSubsectionChain;
        }

        /// <summary>
        /// Returns the stem of a toggle-suffix property name — the part before
        /// the recognised suffix (Toggle / Mode / Type / Enable). For
        /// <c>_SobelFilterToggle</c> returns <c>_SobelFilter</c>. Returns null
        /// if the name doesn't actually end in a recognised suffix at a word
        /// boundary (matching <see cref="HasToggleSuffix"/>).
        /// </summary>
        private static string StripToggleSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string[] suffixes = { "Toggle", "Mode", "Type", "Enable" };
            foreach (var suf in suffixes)
            {
                if (name.Length <= suf.Length) continue;
                if (!name.EndsWith(suf, StringComparison.Ordinal)) continue;
                char before = name[name.Length - suf.Length - 1];
                char first  = name[name.Length - suf.Length];
                bool boundary = before == '_' || char.IsLower(before)
                                || (char.IsUpper(before) && char.IsUpper(first));
                if (boundary)
                    return name.Substring(0, name.Length - suf.Length);
            }
            return null;
        }

        /// <summary>
        /// Returns true when the shader property name ends in one of the
        /// "section toggle" suffixes — <c>Toggle</c>, <c>Mode</c>, <c>Type</c>,
        /// <c>Enable</c>. These suffixes signal that the property is intended
        /// to gate a feature group rather than be a feature parameter itself.
        ///
        /// The suffix must appear at a word boundary. Word boundaries are:
        ///   - underscore before the suffix (e.g. <c>_Mode</c>)
        ///   - lowercase before the suffix (camelCase: <c>SobelFilterToggle</c>
        ///     where 'r' precedes 'T')
        ///   - uppercase before AND uppercase first char of suffix (initialism
        ///     followed by PascalCase: <c>NMFToggle</c> where 'F' precedes 'T'
        ///     and 'T' begins a new word that continues with lowercase)
        ///
        /// This recognises Mochie's <c>_NMFToggle</c>, <c>_DBToggle</c>-style
        /// abbreviations as well as the standard <c>_SobelFilterToggle</c> form.
        /// </summary>
        private static bool HasToggleSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string[] suffixes = { "Toggle", "Mode", "Type", "Enable" };
            foreach (var suf in suffixes)
            {
                if (name.Length <= suf.Length) continue;
                if (!name.EndsWith(suf, StringComparison.Ordinal)) continue;
                char before = name[name.Length - suf.Length - 1];
                char first  = name[name.Length - suf.Length]; // first char of suffix
                // Underscore or camelCase boundary.
                if (before == '_' || char.IsLower(before)) return true;
                // Initialism → PascalCase boundary (NMF + Toggle, DB + Toggle).
                if (char.IsUpper(before) && char.IsUpper(first)) return true;
            }
            return false;
        }

        /// <summary>
        /// Tokenizes a shader property name into lowercase words by splitting on
        /// underscores and camelCase boundaries. Drops short tokens (&lt;3 chars)
        /// and common noise words ("fx", "enable", "use", etc.) so the matcher
        /// only sees distinctive content.
        /// </summary>
        private static List<string> TokenizeName(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            if (name.StartsWith("_")) name = name.Substring(1);
            foreach (var part in name.Split('_'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var current = new System.Text.StringBuilder();
                for (int i = 0; i < part.Length; i++)
                {
                    char c = part[i];
                    if (i > 0 && char.IsUpper(c) && char.IsLower(part[i - 1]))
                    {
                        if (current.Length > 0)
                        {
                            string w = current.ToString().ToLowerInvariant();
                            if (w.Length >= 3 && !_wordMatchNoise.Contains(w))
                                words.Add(w);
                            current.Length = 0;
                        }
                    }
                    current.Append(c);
                }
                if (current.Length > 0)
                {
                    string w = current.ToString().ToLowerInvariant();
                    if (w.Length >= 3 && !_wordMatchNoise.Contains(w))
                        words.Add(w);
                }
            }
            return words;
        }

        /// <summary>
        /// Scores how strongly two shader property names share distinctive words.
        /// +10 per exact word match, +5 per prefix match (≥4 chars). Used both
        /// for "best toggle for property" and for grading the structural pick
        /// against word-match candidates.
        /// </summary>
        private static int ScoreNameMatch(string property, string toggle)
        {
            var pw = TokenizeName(property);
            var tw = TokenizeName(toggle);
            int score = 0;
            foreach (var a in pw)
            {
                foreach (var b in tw)
                {
                    if (a == b) { score += 10; continue; }
                    if (a.Length >= 4 && b.Length >= 4
                        && (a.StartsWith(b) || b.StartsWith(a)))
                        score += 5;
                }
            }
            return score;
        }

        /// <summary>
        /// Finds the toggle in <paramref name="toggles"/> whose name has the
        /// highest <see cref="ScoreNameMatch"/> with the given property name.
        /// Requires a minimum score of 5 (one prefix match) to count, filtering
        /// out coincidental noise.
        /// </summary>
        private static (string toggle, int score) BestWordMatch(string prop, List<string> toggles)
        {
            string best = null;
            int bestScore = 0;
            foreach (var t in toggles)
            {
                int s = ScoreNameMatch(prop, t);
                if (s > bestScore) { bestScore = s; best = t; }
            }
            return bestScore >= 5 ? (best, bestScore) : (null, 0);
        }

        /// <summary>
        /// Public helper for the action drawer and build pipeline. Returns true
        /// when an effect-toggle association is found AND the toggle property
        /// is different from the action's own property (i.e. the user isn't
        /// already directly setting the toggle, e.g. <c>_OutlineType</c> or
        /// <c>_FilterModel</c>).
        /// </summary>
        internal static bool TryGetEffectToggle(
            Material material, string propertyName, out string toggleProp)
        {
            toggleProp = null;
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
                return false;

            var map = GetAttributeToggleMap(material.shader);
            if (map == null) return false;
            if (!map.TryGetValue(propertyName, out var detected) || detected == null)
                return false;

            // Hide the checkbox when the property already IS the section toggle —
            // there's nothing meaningful to "also set".
            if (detected == propertyName) return false;

            toggleProp = detected;
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHADER PROPERTY GROUPING FOR EDITOR SEARCH UI
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Property description strings that carry no useful information for
        /// end users — they're internal type-tag markers that some shaders use
        /// to communicate with their own custom inspectors. Mochie Screen FX is
        /// the most visible offender: it labels many properties with
        /// <c>"fl"</c>, <c>"tog"</c>, <c>"vec"</c>, or <c>"tex"</c> — and the
        /// "type" implied by those tags isn't even consistent with the real
        /// shader property type (e.g. <c>_OutlineTexCoord</c> is declared as
        /// a <c>Vector</c> but carries the description <c>"fl"</c>).
        ///
        /// When <see cref="BuildShaderPropertyGroups"/> sees one of these
        /// descriptions it blanks it out so downstream UI falls back to the
        /// property name, which is at least accurate. Comparison is
        /// case-insensitive; anything not on this list passes through.
        /// </summary>
        private static readonly HashSet<string> _junkPropertyDescriptions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fl", "flt", "float",
                "int", "integer",
                "bool", "boolean",
                "tog", "toggle",
                "vec", "vec2", "vec3", "vec4", "vector",
                "tex", "tex2d", "texture",
                "col", "color", "colour",
                "range",
            };

        /// <summary>
        /// Returns the property description if it looks genuinely useful, or
        /// <c>null</c> if it's a Mochie-style type-tag marker we should hide.
        /// The rule is: trim the description; if empty or in
        /// <see cref="_junkPropertyDescriptions"/>, return null.
        /// </summary>
        private static string SanitizePropertyDescription(string rawDesc)
        {
            if (string.IsNullOrWhiteSpace(rawDesc)) return null;
            string trimmed = rawDesc.Trim();
            if (_junkPropertyDescriptions.Contains(trimmed)) return null;
            return rawDesc;
        }

        // ─── Custom inspector label scraper ──────────────────────────────────
        // Shaders with custom inspectors (Mochie being the most visible) often
        // declare every property with an empty label string in the Properties
        // block, and hardcode the user-facing labels inside the inspector's
        // C# source. Unity's Shader.GetPropertyDescription returns the empty
        // shader-declared label in that case, so our search tree was stuck
        // showing raw underscore names. The scraper pulls those labels out of
        // the inspector's source file via regex so the tree can render what
        // the user actually sees in the material inspector.

        /// <summary>
        /// Everything we pull out of a custom-inspector C# source in one scan:
        /// property display labels, the authoritative list of section toggles
        /// (properties referenced as the toggle arg of a <c>DoFoldout</c>-style
        /// call), AND the full set of underscore-prefixed tokens referenced
        /// anywhere in the source (used to detect orphaned shader properties
        /// that exist in the Properties block but aren't drawn by the
        /// inspector — e.g. Mochie's legacy <c>_DeepFry</c>/<c>_Flavor</c>
        /// /<c>_Heat</c>/<c>_Sizzle</c> properties which exist in the shader
        /// but were removed from the inspector UI). Any field may be null
        /// when the scraper didn't find anything useful — the outer cache
        /// distinguishes "no data" from "not yet scraped".
        /// </summary>
        internal class InspectorSourceData
        {
            public Dictionary<string, string> labels;                // property → display label
            public HashSet<string>            sectionToggles;        // explicit section toggle names
            public List<string>               sectionOrder;          // section toggles in inspector-source order (DoFoldout appearance order)
            public HashSet<string>            referencedProperties;  // every _PropName token in the source
        }

        /// <summary>
        /// Cache: shader asset path → scrape result (labels + section toggles).
        /// A null entry means "we tried and there's no useful data, don't retry".
        /// </summary>
        private static readonly Dictionary<string, InspectorSourceData>
            _inspectorDataCache = new Dictionary<string, InspectorSourceData>();

        // Per-regex match timeout. Every scraper regex is given this cap so a
        // pathological input — a truly adversarial source file, an unbalanced
        // string, or a bounded-but-exponential backtrack sequence — can't hang
        // the Unity Editor or the test runner. On timeout the engine throws
        // <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>,
        // which the outer scrape try/catch in
        // <see cref="GetInspectorSourceData"/> swallows and treats as "no
        // inspector data" — the caller degrades gracefully to "use shader-
        // declared labels only". 5 seconds is far more than any legitimate
        // match needs on a 100 KB source file, but short enough that a stuck
        // test reports progress in seconds, not minutes.
        private static readonly TimeSpan _regexMatchTimeout = TimeSpan.FromSeconds(5);

        // Hard cap on the size of a custom-inspector C# source the scraper
        // will attempt to process. Inspector files over this size are treated
        // as "no data" rather than risk a large regex scan on a file the
        // shader author almost certainly didn't intend us to parse. 512 KB
        // comfortably accommodates Mochie's ~60 KB and BeanFX's ~90 KB editors
        // while ruling out pathological outliers that could be JSON blobs,
        // minified bundles, or auto-generated data files accidentally named
        // like a ShaderGUI class.
        private const int _maxInspectorSourceBytes = 512 * 1024;

        // Match `CustomEditor "Namespace.ClassName"` inside a .shader file.
        private static readonly Regex _shaderCustomEditorPattern =
            new Regex(@"CustomEditor\s+""([^""]+)""",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled,
                      _regexMatchTimeout);

        // Variable declarations: string foo = "Label";
        private static readonly Regex _stringVarPattern =
            new Regex(@"\bstring\s+(\w+)\s*=\s*""([^""]*)""\s*;",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // Variable declarations: GUIContent foo = new GUIContent("Label");
        private static readonly Regex _guiContentVarPattern =
            new Regex(@"\bGUIContent\s+(\w+)\s*=\s*new\s+GUIContent\s*\(\s*""([^""]*)""\s*\)",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // Property → literal label:  (_PropName, "Label")
        // Guards against leading lower-case chars so `some_Prop` style locals
        // don't match — the leading underscore preceded by a non-word
        // boundary is required.
        private static readonly Regex _propLiteralLabelPattern =
            new Regex(@"(?<![A-Za-z0-9])(_\w+)\s*,\s*""([^""]+)""",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // Property → variable label:  (_PropName, identifier)
        // Captures the identifier — we only keep the match if the identifier
        // exists in the variable map built earlier.
        private static readonly Regex _propVariableLabelPattern =
            new Regex(@"(?<![A-Za-z0-9])(_\w+)\s*,\s*(\w+)\s*[,\)]",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // new GUIContent("Label"), _PrimaryProp[, _SecondaryProp]
        // Catches texture-single-line calls where the label comes before the
        // properties. The optional secondary prop is Unity's "color swatch"
        // slot and gets the same label as the primary.
        private static readonly Regex _guiContentLabeledPropPattern =
            new Regex(@"new\s+GUIContent\s*\(\s*""([^""]+)""\s*\)\s*,\s*(_\w+)(?:\s*,\s*(_\w+))?",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // "Label", _Prop1, _Prop2
        // Catches calls of the shape `Method(otherArgs, "Label", _PropA, _PropB)`
        // where a literal label directly precedes two shader property refs.
        // Mochie's <c>MGUI.ToggleFloat(me, "Image Clamp", _ClampToggle, _ClampMax)</c>
        // is the motivating case — both props share the same user-facing label.
        private static readonly Regex _literalLabelTwoPropsPattern =
            new Regex(@"""([^""]+)""\s*,\s*(_\w+)\s*,\s*(_\w+)",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // _Prop1, _Prop2, <numerics/args…>, "Label"
        // Catches `MGUI.SliderMinMax(_AudioLinkMin, _AudioLinkMax, 0f, 2f, "Remap", 1)`
        // where two shader property refs come first, followed by up to six
        // non-string intermediate arguments (numerics, booleans, etc.), then
        // the literal label. Both props share the label.
        //
        // The inner <c>[^,"\r\n]+</c> excludes commas (argument separators),
        // quotes (string literal delimiters), and newlines (to prevent
        // matching across statements) so the non-greedy roll-back terminates
        // cleanly at the right boundary.
        private static readonly Regex _twoPropsLiteralLabelPattern =
            new Regex(@"(_\w+)\s*,\s*(_\w+)(?:\s*,\s*[^,""\r\n]+){0,6}\s*,\s*""([^""]+)""",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // DoFoldout( …, _PropName, "Label", … )
        // Catches Mochie's `Foldouts.DoFoldout(foldouts, mat, me, _Letterbox,
        // "Letterbox", Foldouts.Style.ThinToggle)` pattern. The shader
        // property referenced here is ALWAYS a top-level section toggle in
        // Mochie's inspector — that's exactly the information the attribute
        // walker can't infer from `[ToggleUI]_Letterbox` alone because the
        // name doesn't end in a recognised toggle suffix.
        //
        // The captured property name feeds into the authoritative section
        // set, and the captured label seeds the property-label map so the
        // tree's section header shows the user-facing name.
        //
        // <c>[^)]*?</c> is a non-greedy stretch up to the first
        // `_PropName, "Label"` pair WITHIN the same `DoFoldout(...)` call —
        // the `[^)]` exclusion prevents the match from running past the
        // closing paren into unrelated code.
        private static readonly Regex _doFoldoutSectionPattern =
            new Regex(@"DoFoldout\s*\(\s*[^)]*?,\s*(_\w+)\s*,\s*""([^""]+)""",
                      RegexOptions.Compiled, _regexMatchTimeout);

        // Any <c>_PropName</c>-style token anywhere in the inspector source.
        // Used to build the "known to the inspector" set for orphan detection:
        // a shader property that exists in the Properties block but isn't
        // referenced anywhere in the inspector source is legacy/dead state
        // and should go to the ungrouped bucket rather than being attributed
        // to whichever section happens to come before it in shader
        // declaration order.
        //
        // `\b` plus `_` at the start anchors to a word boundary, which in
        // .NET regex means the match can't start inside another identifier
        // (e.g. the `_Bar` inside `foo_Bar` won't match). False positives
        // like C# local variables that happen to start with `_` are
        // harmless — they just mean we treat a property as "known" even
        // though the collision is coincidental, which just preserves the
        // old behavior for that property.
        private static readonly Regex _inspectorShaderPropRefPattern =
            new Regex(@"\b_\w+\b", RegexOptions.Compiled, _regexMatchTimeout);

        /// <summary>
        /// Returns the scraped inspector-source data (labels + authoritative
        /// section toggles) for the shader's custom inspector, or <c>null</c>
        /// if the shader has no custom inspector, the inspector source can't
        /// be located, or nothing useful was found. Cached per shader asset
        /// path so successive calls are cheap.
        /// </summary>
        private static InspectorSourceData GetInspectorSourceData(Shader shader)
        {
            if (shader == null) return null;
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath)) return null;

            if (_inspectorDataCache.TryGetValue(shaderPath, out var cached))
                return cached;

            InspectorSourceData data = null;
            try { data = ScrapeInspectorDataForShader(shaderPath); }
            catch { data = null; /* parse failure → degrade gracefully */ }

            _inspectorDataCache[shaderPath] = data;
            return data;
        }

        /// <summary>
        /// Reads the shader file at <paramref name="shaderPath"/>, extracts
        /// its CustomEditor class, locates the C# source by filename, and
        /// scrapes labels + authoritative section toggles. See the region
        /// header for the rationale.
        ///
        /// <para>Implementation note: this function deliberately uses raw
        /// <c>File.ReadAllText</c> + <c>Directory.GetFiles</c> instead of
        /// <c>AssetDatabase.FindAssets</c> / <c>LoadAssetAtPath&lt;MonoScript&gt;</c>.
        /// AssetDatabase calls can block indefinitely during test-runner
        /// contexts (the test runner may hold importer locks while tests
        /// build controllers, and any AssetDatabase query in that window
        /// can deadlock). Direct filesystem access bypasses those locks
        /// entirely.</para>
        /// </summary>
        private static InspectorSourceData ScrapeInspectorDataForShader(string shaderPath)
        {
            // shaderPath is a project-relative path like "Assets/Foo/Bar.shader"
            // which File.ReadAllText resolves against Unity's CWD (project root).
            string shaderText;
            try { shaderText = System.IO.File.ReadAllText(shaderPath); }
            catch { return null; }
            if (string.IsNullOrEmpty(shaderText)) return null;

            var ceMatch = _shaderCustomEditorPattern.Match(shaderText);
            if (!ceMatch.Success) return null;

            string customEditorName = ceMatch.Groups[1].Value.Trim();
            // Strip namespace: "Mochie.ScreenFXEditor" → "ScreenFXEditor".
            // Unity's CustomEditor attribute on shaders accepts either form.
            int lastDot = customEditorName.LastIndexOf('.');
            string className = lastDot >= 0
                ? customEditorName.Substring(lastDot + 1)
                : customEditorName;
            if (string.IsNullOrEmpty(className)) return null;

            // Locate the C# source whose filename matches the class.
            string editorScriptPath = FindMonoScriptPathForClassName(className);
            if (string.IsNullOrEmpty(editorScriptPath)) return null;

            string editorText;
            try { editorText = System.IO.File.ReadAllText(editorScriptPath); }
            catch { return null; }
            if (string.IsNullOrEmpty(editorText)) return null;

            // Size guard: skip the regex scan on implausibly large "inspector"
            // files. A real ShaderGUI source is tens of KB at most (Mochie's
            // ScreenFXEditor.cs is ~60 KB, BeanFX's BeanFXEditor.cs is ~90 KB).
            // Anything over _maxInspectorSourceBytes is likely a mis-identified
            // file — a JSON blob, a minified bundle, an autogenerated data
            // file — and running our regex suite on it risks minute-long work
            // for zero real signal. Degrade gracefully: return null so the
            // caller falls back to shader-declared labels.
            if (editorText.Length > _maxInspectorSourceBytes) return null;

            return ExtractInspectorData(editorText);
        }

        /// <summary>
        /// Index of <c>className → project-relative path</c> for every
        /// <c>.cs</c> file under the project's <c>Assets/</c> folder.
        /// Built lazily on first use via <see cref="EnsureScriptPathIndexBuilt"/>
        /// and cleared by <see cref="ClearCache"/>.
        ///
        /// <para>This bypasses <see cref="AssetDatabase.FindAssets"/>, which
        /// is the safer but slower path during test-runner contexts. The
        /// initial walk visits every .cs file once; subsequent lookups are
        /// dictionary hits.</para>
        /// </summary>
        private static Dictionary<string, string> _csScriptPathIndex;

        /// <summary>
        /// Finds the path of the <c>.cs</c> file whose filename (without
        /// extension) equals <paramref name="className"/>. Uses a one-time
        /// recursive walk of <c>Assets/</c> rather than
        /// <see cref="AssetDatabase.FindAssets"/> — see the
        /// <see cref="ScrapeInspectorDataForShader"/> doc-comment for why.
        /// </summary>
        private static string FindMonoScriptPathForClassName(string className)
        {
            if (string.IsNullOrEmpty(className)) return null;
            EnsureScriptPathIndexBuilt();
            return _csScriptPathIndex != null
                && _csScriptPathIndex.TryGetValue(className, out var path)
                ? path : null;
        }

        /// <summary>
        /// Builds <see cref="_csScriptPathIndex"/> by walking the project's
        /// <c>Assets/</c> folder once. Safe to call from any context — uses
        /// only direct filesystem APIs, no AssetDatabase. Errors are
        /// swallowed: if the walk fails, the index stays empty and lookups
        /// return null (the scraper degrades gracefully to "no inspector
        /// data found").
        /// </summary>
        private static void EnsureScriptPathIndexBuilt()
        {
            if (_csScriptPathIndex != null) return;
            _csScriptPathIndex = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string assetsDir = Application.dataPath; // absolute path to project's Assets/
                if (!System.IO.Directory.Exists(assetsDir)) return;
                var files = System.IO.Directory.GetFiles(
                    assetsDir, "*.cs", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    // Convert absolute path back to project-relative ("Assets/...")
                    // so the result matches what AssetDatabase would have returned.
                    string relPath = "Assets" + file.Substring(assetsDir.Length).Replace('\\', '/');
                    // First-write-wins for collisions (multiple files named the same)
                    if (!_csScriptPathIndex.ContainsKey(fileName))
                        _csScriptPathIndex[fileName] = relPath;
                }
            }
            catch
            {
                // If the walk fails (permissions, path too long, etc.) just
                // leave the index empty — lookups will return null and the
                // scraper will skip the inspector overlay for any shader.
            }
        }

        /// <summary>
        /// Runs the regex patterns against a custom-inspector C# source and
        /// returns an <see cref="InspectorSourceData"/> carrying both the
        /// scraped property → label map AND the authoritative set of section
        /// toggle names (properties referenced in <c>DoFoldout</c>-style
        /// calls). Returns <c>null</c> when nothing useful is extracted.
        ///
        /// Label sources, in priority order:
        /// <list type="number">
        /// <item>Literal strings in method args: <c>(_Prop, "Label")</c></item>
        /// <item>Variables from the class body: <c>string foo = "Label"</c>
        /// then <c>(_Prop, foo)</c></item>
        /// <item>Texture-single-line convention:
        /// <c>new GUIContent("Label"), _Prop, _ColorProp</c> — both props
        /// get the same label since they render inline together in Unity's
        /// texture row.</item>
        /// <item>Two-prop MGUI calls like
        /// <c>ToggleFloat(me, "Label", _PropA, _PropB)</c> and
        /// <c>SliderMinMax(_PropA, _PropB, …, "Label", …)</c>.</item>
        /// <item><c>DoFoldout</c> calls, which also seed the section-toggle
        /// set with the property name.</item>
        /// </list>
        /// </summary>
        private static InspectorSourceData ExtractInspectorData(string source)
        {
            // Pass 1: build the variable → label map so the property scan
            // below can resolve names like `ugfLabel` to "Use Global Falloff".
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in _stringVarPattern.Matches(source))
                vars[m.Groups[1].Value] = m.Groups[2].Value;
            foreach (Match m in _guiContentVarPattern.Matches(source))
                vars[m.Groups[1].Value] = m.Groups[2].Value;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            // Pass 2a: direct literal labels — `(_Prop, "Label")`.
            foreach (Match m in _propLiteralLabelPattern.Matches(source))
            {
                string propName = m.Groups[1].Value;
                string label    = m.Groups[2].Value;
                if (!result.ContainsKey(propName))
                    result[propName] = label;
            }

            // Pass 2b: variable-backed labels — `(_Prop, variableName)`.
            foreach (Match m in _propVariableLabelPattern.Matches(source))
            {
                string propName = m.Groups[1].Value;
                string varName  = m.Groups[2].Value;
                if (result.ContainsKey(propName)) continue;
                if (vars.TryGetValue(varName, out var label))
                    result[propName] = label;
            }

            // Pass 2c: texture single-line calls —
            // `new GUIContent("Label"), _Tex, _Color`.
            foreach (Match m in _guiContentLabeledPropPattern.Matches(source))
            {
                string label = m.Groups[1].Value;
                string p1    = m.Groups[2].Value;
                if (!result.ContainsKey(p1)) result[p1] = label;
                if (m.Groups[3].Success)
                {
                    string p2 = m.Groups[3].Value;
                    if (!result.ContainsKey(p2)) result[p2] = label;
                }
            }

            // Pass 2d: label-first two-props — `"Label", _Prop1, _Prop2`.
            // Covers Mochie's `MGUI.ToggleFloat(me, "Image Clamp",
            // _ClampToggle, _ClampMax)` shape where a literal label precedes
            // two shader property references in the same call.
            foreach (Match m in _literalLabelTwoPropsPattern.Matches(source))
            {
                string label = m.Groups[1].Value;
                string p1    = m.Groups[2].Value;
                string p2    = m.Groups[3].Value;
                if (!result.ContainsKey(p1)) result[p1] = label;
                if (!result.ContainsKey(p2)) result[p2] = label;
            }

            // Pass 2e: two-props-then-label — `_Prop1, _Prop2, …, "Label"`.
            // Covers Mochie's `MGUI.SliderMinMax(_AudioLinkMin, _AudioLinkMax,
            // 0f, 2f, "Remap", 1)` shape where two shader property refs lead
            // the call and the literal label appears later, after numeric /
            // boolean arguments.
            foreach (Match m in _twoPropsLiteralLabelPattern.Matches(source))
            {
                string p1    = m.Groups[1].Value;
                string p2    = m.Groups[2].Value;
                string label = m.Groups[3].Value;
                if (!result.ContainsKey(p1)) result[p1] = label;
                if (!result.ContainsKey(p2)) result[p2] = label;
            }

            // Pass 3: authoritative section toggles from `DoFoldout` calls.
            // These are Mochie's top-level foldouts — the property captured
            // is DEFINITELY a section toggle in the inspector, regardless of
            // whether the attribute walker would infer that from the shader
            // alone. The scraped label also seeds the labels map (overriding
            // any earlier label for that property since foldout headings are
            // the most authoritative source for section-toggle display names).
            var sectionToggles = new HashSet<string>(StringComparer.Ordinal);
            var sectionOrder   = new List<string>();
            foreach (Match m in _doFoldoutSectionPattern.Matches(source))
            {
                string prop  = m.Groups[1].Value;
                string label = m.Groups[2].Value;
                if (sectionToggles.Add(prop)) // Add returns false for duplicates
                    sectionOrder.Add(prop);
                // Foldout labels win over any earlier first-write for this
                // property — they're the user-visible section header.
                result[prop] = label;
            }

            // Pass 4: every underscore-prefixed token anywhere in the source.
            // This is the "known to the inspector" set used by the section
            // walker to detect orphaned shader properties. A property that
            // exists in the shader Properties block but NEVER appears as a
            // token in the inspector source — not even as a MaterialProperty
            // declaration — is legacy/dead state and gets routed to the
            // ungrouped bucket.
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in _inspectorShaderPropRefPattern.Matches(source))
                referenced.Add(m.Value);

            if (result.Count == 0 && sectionToggles.Count == 0 && referenced.Count == 0)
                return null;

            return new InspectorSourceData
            {
                labels               = result.Count > 0 ? result : null,
                sectionToggles       = sectionToggles.Count > 0 ? sectionToggles : null,
                sectionOrder         = sectionOrder.Count > 0 ? sectionOrder : null,
                referencedProperties = referenced.Count > 0 ? referenced : null,
            };
        }

        /// <summary>
        /// Descriptor for a single shader property, used by
        /// <see cref="BuildShaderPropertyGroups"/> to hand structured information
        /// to search-tree builders in the editor UI. Carries display info and
        /// two classification flags that are distinct on purpose:
        /// <list type="bullet">
        /// <item><see cref="isSectionToggle"/> — this property is the header
        /// of its own group (e.g. Mochie <c>_FilterModel</c>, BeanFX
        /// <c>_EnableOutline</c>).</item>
        /// <item><see cref="hasToggleAttribute"/> — the property has ANY
        /// <c>[Toggle]</c> / <c>[ToggleUI]</c> / <c>[Toggle(KW)]</c> /
        /// <c>[Enum(Off,…)]</c> attribute, regardless of whether it heads a
        /// section. Use this to render a gear icon on sub-toggle children
        /// (e.g. BeanFX <c>_PixelUseGlobalFalloff</c>).</item>
        /// </list>
        /// Every section toggle also has <c>hasToggleAttribute==true</c>; not
        /// every toggle-attributed property is a section toggle.
        /// </summary>
        internal class ShaderPropertyDescriptor
        {
            public int    index;               // shader property declaration index
            public string name;                // underscore-prefixed property name (e.g. "_BlurStr")
            public string description;         // inspector display string from Shader.GetPropertyDescription
            public UnityEngine.Rendering.ShaderPropertyType type;
            public bool   hasToggleAttribute;  // has [Toggle] / [ToggleUI] / [Enum(Off,...)] attr
            public bool   isSectionToggle;     // heads its own ShaderPropertyGroup
        }

        /// <summary>
        /// An ordered bucket of shader properties in
        /// <see cref="BuildShaderPropertyGroups"/> output. When
        /// <see cref="toggle"/> is non-null the group represents a section
        /// toggle; <see cref="children"/> is the list of properties assigned
        /// to that toggle (excluding the toggle itself). When
        /// <see cref="toggle"/> is null the group is the "ungrouped" bucket
        /// (properties the attribute walker could not assign to any section).
        /// </summary>
        internal class ShaderPropertyGroup
        {
            public ShaderPropertyDescriptor toggle;   // null = ungrouped bucket
            public List<ShaderPropertyDescriptor> children = new List<ShaderPropertyDescriptor>();
        }

        /// <summary>
        /// Builds an ordered list of shader property groups for editor search
        /// UIs (the "Search" button next to Property Name fields). Uses the
        /// same attribute-walk algorithm as
        /// <see cref="GetAttributeToggleMap"/> / <see cref="TryGetEffectToggle"/>
        /// — so whatever toggle would be auto-set by the "Also Set Effect
        /// Toggle" checkbox for a given property is exactly the toggle that
        /// property appears under in the search tree.
        ///
        /// <para>Ordering contract:</para>
        /// <list type="number">
        /// <item>If any ungrouped properties exist, the first element has
        /// <c>toggle == null</c> and contains them.</item>
        /// <item>Then one element per section toggle, in shader declaration
        /// order. Each has its <c>toggle</c> set, and <c>children</c> lists
        /// its assigned non-toggle properties in declaration order (the
        /// toggle itself is NOT repeated in children).</item>
        /// </list>
        ///
        /// <para>Hidden properties (<c>[HideInInspector]</c>) are excluded
        /// entirely unless the shader uses a toggle-marker prefix convention
        /// (e.g. June 5 <c>_Keyword*</c>), in which case hidden properties
        /// the attribute-walk chose to include are kept so shaders that rely
        /// heavily on hidden module parameters stay discoverable.</para>
        ///
        /// <para>The returned structure IS cached per shader instance ID —
        /// clicking the "Search" button multiple times returns the same
        /// List and descriptor instances each time, so UI latency on the
        /// second+ click is near-zero. The search-tree builder only reads
        /// from the structure, so this is safe; callers that need to
        /// mutate should copy first. The cache is cleared by
        /// <see cref="ClearCache"/>.</para>
        /// </summary>
        internal static List<ShaderPropertyGroup> BuildShaderPropertyGroups(Shader shader)
        {
            if (shader == null) return new List<ShaderPropertyGroup>();

            int cacheShaderId = shader.GetInstanceID();
            if (_propertyGroupsCache.TryGetValue(cacheShaderId, out var cachedTop))
                return cachedTop;

            // Thry-convention shaders (Poiyomi, some others) use hidden marker
            // properties `m_start_<Name>` / `m_end_<Name>` to bracket section
            // boundaries, with the display label embedded in the property
            // description (e.g. `"Color Adjust--{reference_property:_MainColorAdjustToggle}"`).
            // The attribute-walk algorithm below can't see these descriptions
            // and the markers themselves are HideInInspector, so Poiyomi
            // sections would otherwise all end up in the ungrouped bucket.
            // Detect and dispatch here.
            if (HasThrySectionMarkers(shader))
            {
                var built = BuildThryShaderPropertyGroups(shader);
                _propertyGroupsCache[cacheShaderId] = built;
                return built;
            }

            // Cache-hit path: return the previously-built structure.
            // Motivation: without this cache, every Search-button click
            // re-ran the full pipeline — attribute walk (~260 Shader API
            // calls for Mochie) + inspector source scrape lookup + property
            // grouping + sort — producing a noticeable 2-3 second hang
            // before the search window became responsive. With the cache,
            // the first click pays the cost and subsequent clicks are
            // effectively free.
            int shaderId = shader.GetInstanceID();
            if (_propertyGroupsCache.TryGetValue(shaderId, out var cached))
                return cached;

            var groups = new List<ShaderPropertyGroup>();
            var map = GetAttributeToggleMap(shader);
            if (map == null)
            {
                _propertyGroupsCache[shaderId] = groups;
                return groups;
            }

            // Scraped data from the shader's custom-inspector source file,
            // if any. Mochie's Screen FX X is the motivating case: its
            // shader properties block declares every label as "" and the
            // real display labels only exist as hardcoded strings inside
            // ScreenFXEditor.cs. The scrape also produces an authoritative
            // list of section toggles from the inspector's `DoFoldout` calls,
            // which GetAttributeToggleMap uses to fix up sections that the
            // attribute walker misclassifies (e.g. Mochie's `_Letterbox`
            // which is a top-level section but doesn't end in a toggle
            // suffix that the heuristic walker recognises).
            var inspectorData   = GetInspectorSourceData(shader);
            var inspectorLabels = inspectorData?.labels;

            int n = shader.GetPropertyCount();

            // The ungrouped bucket comes first in the output. We create it
            // eagerly and remove it at the end if it's empty, so the caller
            // doesn't need special-case logic for "maybe ungrouped, maybe not".
            var ungrouped = new ShaderPropertyGroup();
            groups.Add(ungrouped);

            // Pass 1: Walk properties in declaration order and build a
            // descriptor for every non-hidden property. Section toggles get
            // their own group created at the declaration site so groups stay
            // in declaration order even when a word-match reassignment puts
            // a child ahead of its section toggle in the map.
            var descriptors = new ShaderPropertyDescriptor[n];
            var byToggle    = new Dictionary<string, ShaderPropertyGroup>();

            for (int i = 0; i < n; i++)
            {
                // Skip properties the attribute walker chose not to consider.
                // We follow the map rather than re-checking flags here — if
                // the walker included a hidden property for a shader with a
                // toggle-marker prefix, it'll be in the map and we honor that.
                var flags = shader.GetPropertyFlags(i);
                bool hidden = (flags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0;

                string name = shader.GetPropertyName(i);
                bool inMap = map.ContainsKey(name);

                // Skip hidden properties UNLESS they appear in the toggle map
                // (which happens for shaders with a detected toggle prefix).
                if (hidden && !inMap) continue;

                // Hide truly orphaned properties — declared in the shader's
                // Properties block but referenced NOWHERE in the inspector
                // source code (not even as a MaterialProperty declaration).
                // For Mochie that's the legacy `_DeepFry` / `_Flavor` /
                // `_Heat` / `_Sizzle` group: declared in the shader but
                // never sampled in any pass and never drawn by the inspector,
                // so showing them in the search tree is just noise. Only
                // applies when we have inspector data to make the call;
                // shaders without a custom inspector show every property
                // since we can't tell what's "really used".
                if (!inMap
                    && inspectorData != null
                    && inspectorData.referencedProperties != null
                    && !inspectorData.referencedProperties.Contains(name))
                    continue;

                // Label precedence:
                //   1. Scraped inspector-source label (Mochie's "Background Tint")
                //   2. Sanitized shader-declared description (Unity Standard's
                //      "Albedo (RGB)")
                //   3. null — downstream UI falls back to the property name
                string inspectorLabel = null;
                if (inspectorLabels != null)
                    inspectorLabels.TryGetValue(name, out inspectorLabel);
                string resolvedLabel  = !string.IsNullOrEmpty(inspectorLabel)
                    ? inspectorLabel
                    : SanitizePropertyDescription(shader.GetPropertyDescription(i));

                var desc = new ShaderPropertyDescriptor
                {
                    index       = i,
                    name        = name,
                    description = resolvedLabel,
                    type        = shader.GetPropertyType(i),
                };

                // Classify toggle-ness from the shader attributes. A property
                // "has a toggle attribute" whenever any of the following is
                // present: [Toggle], [ToggleUI], [Toggle(KW)], [Enum(Off,...)].
                string[] attrs = shader.GetPropertyAttributes(i) ?? new string[0];
                foreach (var attr in attrs)
                {
                    if (attr.Equals("Toggle", StringComparison.OrdinalIgnoreCase)
                        || attr.Equals("ToggleUI", StringComparison.OrdinalIgnoreCase)
                        || attr.StartsWith("Toggle(", StringComparison.OrdinalIgnoreCase))
                    {
                        desc.hasToggleAttribute = true;
                    }
                    else if (attr.StartsWith("Enum(", StringComparison.OrdinalIgnoreCase))
                    {
                        // Match the same "Off as first value" rule the walker
                        // uses so _SST etc. are recognised as toggles but
                        // general enums aren't.
                        var m = Regex.Match(attr, @"Enum\(\s*([^,)]+)\s*,\s*0",
                                            RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Trim()
                            .Equals("Off", StringComparison.OrdinalIgnoreCase))
                            desc.hasToggleAttribute = true;
                    }
                }

                descriptors[i] = desc;

                // Section toggle? The map records self-mappings (toggle → itself)
                // so we can detect them here without re-running the walker.
                if (inMap && map[name] == name)
                {
                    desc.isSectionToggle = true;
                    var group = new ShaderPropertyGroup { toggle = desc };
                    groups.Add(group);
                    byToggle[name] = group;
                }
            }

            // Pass 2: Assign each non-toggle property to its section toggle's
            // children list, or to the ungrouped bucket if the map didn't
            // place it anywhere we recognise.
            for (int i = 0; i < n; i++)
            {
                var desc = descriptors[i];
                if (desc == null || desc.isSectionToggle) continue;

                if (map.TryGetValue(desc.name, out var assignedToggle)
                    && assignedToggle != null
                    && byToggle.TryGetValue(assignedToggle, out var targetGroup))
                {
                    targetGroup.children.Add(desc);
                }
                else
                {
                    ungrouped.children.Add(desc);
                }
            }

            // Drop the leading ungrouped bucket if it turned out empty so the
            // search tree doesn't render an empty header entry.
            if (ungrouped.children.Count == 0)
                groups.RemoveAt(0);

            // Reorder section groups to match the inspector's drawing order
            // (the order `DoFoldout` calls appear in the inspector source)
            // instead of shader property declaration order. This puts Mochie's
            // "Audio Link" section last in the search tree — matching what
            // the user sees in the material inspector — even though the
            // shader declares `_AudioLinkToggle` near the top of its
            // Properties block. Groups without a scraped order entry (e.g.
            // the ungrouped bucket, or sections from the attribute walker
            // that aren't in the DoFoldout set) keep their relative position
            // at the end of the list.
            var inspectorOrder = inspectorData?.sectionOrder;
            if (inspectorOrder != null && inspectorOrder.Count > 0)
            {
                // Build a position lookup: section toggle name → desired
                // index. Sections NOT in the inspector order get int.MaxValue
                // so they sort to the end. The ungrouped bucket (toggle==null)
                // always sorts before all section groups (position -1).
                var posMap = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int p = 0; p < inspectorOrder.Count; p++)
                    posMap[inspectorOrder[p]] = p;

                groups.Sort((a, b) =>
                {
                    int posA = a.toggle == null ? -1
                        : posMap.TryGetValue(a.toggle.name, out int pa) ? pa : int.MaxValue;
                    int posB = b.toggle == null ? -1
                        : posMap.TryGetValue(b.toggle.name, out int pb) ? pb : int.MaxValue;
                    return posA.CompareTo(posB);
                });
            }

            _propertyGroupsCache[shaderId] = groups;
            return groups;
        }

        /// <summary>
        /// Enables all shader_feature_local keywords required by the given set of
        /// property names on the material. Returns the number of keywords enabled.
        /// </summary>
        public static int EnableRequiredKeywords(Material material, HashSet<string> usedPropertyNames)
        {
            if (material == null || material.shader == null || usedPropertyNames == null)
                return 0;

            var map = GetShaderFeatureMap(material.shader);
            if (map == null || map.Count == 0) return 0;

            var enabled = new HashSet<string>();
            foreach (string prop in usedPropertyNames)
            {
                if (map.TryGetValue(prop, out var info) && info.keyword != null
                    && !enabled.Contains(info.keyword))
                {
                    material.EnableKeyword(info.keyword);
                    enabled.Add(info.keyword);
                }
            }
            return enabled.Count;
        }

        /// <summary>
        /// Applies shader-specific one-time material fixups for known third-party
        /// shaders. Called during build so the modifications persist on the
        /// material asset.
        ///
        /// Mochie SFX case: Image Overlay (SST) and Zoom live in the shader's
        /// <c>"Always"</c> pass, which Mochie's custom inspector disables as an
        /// optimization whenever SST/Zoom/Letterbox are all off, and re-enables
        /// when any of them becomes active. Enigma mirrors this at runtime
        /// (<see cref="EnigmaExecutor.ExecuteAction"/> toggles the pass on the
        /// matching keyword) — but for the runtime toggle to work at all, two
        /// things must be true at build time:
        ///
        ///   1. The <c>_IMAGE_OVERLAY_ON</c> shader variant must be compiled
        ///      into the player build. Unity's shader_feature_local stripping
        ///      only includes variants whose keyword is enabled on at least
        ///      one material at build time.
        ///   2. The material must be in a clean baseline (pass disabled,
        ///      _SST = 0) so the overlay doesn't render on scene load before
        ///      any button is pressed.
        ///
        /// This method enforces both: enables the keyword (to force variant
        /// inclusion), and puts the pass + overlay properties back to their
        /// off state. Safe to run repeatedly — every Enigma build brings the
        /// Mochie material back to this baseline.
        /// </summary>
        public static void ApplyMaterialFixups(Material material)
        {
            // Legacy entry point — no managed-toggle scope known, treat every
            // toggle as Enigma-managed (the pre-2.0.6 behaviour). Prefer the
            // scoped overload: it leaves user-authored effect state alone.
            ApplyMaterialFixups(material, null);
        }

        /// <summary>
        /// Scoped overload. <paramref name="managedToggles"/> is the set of
        /// section-toggle / gate property names Enigma actually manages on
        /// this material (compute with <see cref="ComputeManagedToggles"/>).
        /// Only those toggles are reset to the off baseline — master toggles
        /// the user enabled manually on the material (e.g. a permanent _Fog=1
        /// as part of the world's look, with no Enigma button for it) are
        /// left untouched. Passing null manages everything (legacy behaviour).
        /// </summary>
        public static void ApplyMaterialFixups(Material material, HashSet<string> managedToggles)
        {
            if (material == null || material.shader == null) return;

            if (IsMochieScreenFX(material.shader.name))
            {
                bool changed = false;

                // Scope gates: overlay handling (keyword/_ScreenTex/_SST) only
                // applies when Enigma manages the SST section; the Always-pass
                // baseline only applies when Enigma manages ANY of the three
                // gate effects that render in that pass.
                bool overlayManaged = managedToggles == null
                    || managedToggles.Contains("_SST")
                    || managedToggles.Contains("_ScreenTex");
                bool gateManaged = overlayManaged
                    || managedToggles == null
                    || managedToggles.Contains("_Zoom")
                    || managedToggles.Contains("_Letterbox");

                // (1) Ensure the _IMAGE_OVERLAY_ON variant is compiled into the
                //     player build. Without this, runtime EnableKeyword() on
                //     non-master clients would fall back to the no-overlay
                //     variant and the effect would never render.
                if (overlayManaged && !material.IsKeywordEnabled("_IMAGE_OVERLAY_ON"))
                {
                    material.EnableKeyword("_IMAGE_OVERLAY_ON");
                    changed = true;
                }

                // (2) Baseline: "Always" pass off, _SST off, transparent
                //     placeholder bound to _ScreenTex. The runtime executor
                //     flips the pass + _SST on when an Overlay button is
                //     pressed. Mochie's ApplySST runs on the keyword ALONE
                //     (the shader does not value-gate it on _SST), and an
                //     UNBOUND _ScreenTex samples the shader's "white" default
                //     — so with the keyword shipped enabled (see (1)), any
                //     OTHER gate effect enabling the shared pass (a Zoom
                //     button, a Letterbox fader) would paint the screen solid
                //     white. The placeholder's alpha is 0, which zeroes
                //     ApplySST's blend weight and makes it a no-op no matter
                //     what state the pass and keyword are in.
                //     Skipped entirely when Enigma manages no gate effect on
                //     this material — a user-enabled permanent Zoom/Letterbox
                //     keeps its pass.
                if (gateManaged && material.GetShaderPassEnabled("Always"))
                {
                    material.SetShaderPassEnabled("Always", false);
                    changed = true;
                }
                if (overlayManaged)
                {
                    Texture clearTex = GetClearTexture();
                    if (material.GetTexture("_ScreenTex") != clearTex)
                    {
                        material.SetTexture("_ScreenTex", clearTex);
                        changed = true;
                    }
                }

                // (1b/2c) Fog. Unlike the value-gated sections, Mochie fog
                //     renders on its keyword ALONE: ApplyFog sits behind
                //     #if FOG_ENABLED (= _FOG_ON) and nothing in the shader
                //     reads the _Fog toggle at render time, so no property
                //     write can switch it off once the keyword is compiled in.
                //     Visibility is values-only — and _FogColor.a is the one
                //     value the blend math fully respects (col.a lerps toward
                //     fogAlpha; the pass blends SrcAlpha OneMinusSrcAlpha, so
                //     alpha 0 renders nothing).
                //
                //     Managed (fog buttons exist on the controller): enable the
                //     keyword so the variant ships and the buttons work on an
                //     untouched user material, and baseline _FogColor.a to 0 so
                //     fog renders nothing before the runtime executor applies
                //     the entries' values at Start(). Unmanaged: strip a stale
                //     hot keyword unless the user authored a permanent fog on
                //     the material themselves (_Fog == 1), mirroring the
                //     "user-enabled permanent Zoom keeps its pass" rule.
                bool fogManaged = managedToggles == null
                    || managedToggles.Contains("_Fog")
                    || managedToggles.Contains("_FogRadius")
                    || managedToggles.Contains("_FogFade")
                    || managedToggles.Contains("_FogColor");
                if (fogManaged)
                {
                    if (!material.IsKeywordEnabled("_FOG_ON"))
                    {
                        material.EnableKeyword("_FOG_ON");
                        changed = true;
                    }
                    if (material.HasProperty("_FogColor"))
                    {
                        Color fogCol = material.GetColor("_FogColor");
                        if (fogCol.a != 0f)
                        {
                            fogCol.a = 0f;
                            material.SetColor("_FogColor", fogCol);
                            changed = true;
                        }
                    }
                }
                else if (material.IsKeywordEnabled("_FOG_ON")
                         && material.HasProperty("_Fog")
                         && material.GetFloat("_Fog") == 0f)
                {
                    material.DisableKeyword("_FOG_ON");
                    changed = true;
                }

                // (1c) Zoom. The Always-pass zoom feature is keyword-gated
                //     (_ZOOM_ON / _ZOOM_RGB_ON) but — unlike overlay — fully
                //     value-gated inside the pass (GetZoom scales by _ZoomStr,
                //     which the zeroed baseline keeps at 0), so the keyword is
                //     safe to ship enabled. Enable the Basic variant when
                //     Enigma manages zoom so it survives build-time stripping
                //     and the buttons work on an untouched user material.
                //
                //     Zoom is also the one Mochie section whose distance
                //     falloff DEFAULTS to the local 3–4.5 m range
                //     (_ZoomUseGlobal=0) — a fresh material would only zoom
                //     within a few meters of the FX object. Switch a managed
                //     material to the global range so zoom reaches as far as
                //     every sibling effect.
                bool zoomManaged = material.HasProperty("_ZoomStr")
                    && (managedToggles == null
                        || managedToggles.Contains("_Zoom")
                        || managedToggles.Contains("_ZoomStr"));
                if (zoomManaged)
                {
                    if (!material.IsKeywordEnabled("_ZOOM_ON")
                        && !material.IsKeywordEnabled("_ZOOM_RGB_ON"))
                    {
                        material.EnableKeyword("_ZOOM_ON");
                        changed = true;
                    }
                    if (material.HasProperty("_ZoomUseGlobal")
                        && material.GetInt("_ZoomUseGlobal") != 1)
                    {
                        material.SetInt("_ZoomUseGlobal", 1);
                        changed = true;
                    }
                }

                // (2b) Zero the Enigma-managed Mochie section master-toggles.
                //      PrepareAndLock sets these to 1 so the shader's
                //      `shader_feature_local` variants survive build-time
                //      stripping, but Mochie reads each value at render time
                //      (`if (_X != 0) ApplyEffect()`) so leaving them at 1
                //      makes the effect render the moment the world loads.
                //      The Enigma runtime executor re-applies values from rt
                //      arrays at Start(); resetting here gives a clean
                //      off-state baseline for both the editor preview AND the
                //      player build's pre-Start frame. All Mochie SFX master
                //      toggles use 0 == "Off" by convention.
                //
                //      Only toggles in managedToggles are reset — zeroing the
                //      full list (pre-2.0.6) silently killed effects the user
                //      had enabled manually on the material asset.
                string[] mochieZeroToggles = new[]
                {
                    "_FilterModel",
                    "_OutlineType",
                    "_ShakeModel",
                    "_DistortionModel",
                    "_BlurModel",
                    "_NoiseMode",
                    "_Fog",
                    "_Zoom",
                    "_SST",
                    "_Letterbox",
                    "_Triplanar",
                    "_ClampToggle",
                    "_RoundingToggle",
                    "_NMFToggle",
                    "_DepthBufferToggle",
                    "_SobelFilterToggle",
                };
                foreach (var p in mochieZeroToggles)
                {
                    if (managedToggles != null && !managedToggles.Contains(p)) continue;
                    if (material.HasProperty(p) && material.GetFloat(p) != 0f)
                    {
                        material.SetFloat(p, 0f);
                        material.SetInt(p, 0);
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(material);
                    Debug.Log($"[EnigmaShaderHelper] Reset Mochie SFX baseline on '{material.name}' (managed toggles=0, Always pass off where gate-managed; section keywords stay enabled for variant inclusion — call SyncMochieKeywordsToValues post-build to clean up).", material);
                }
            }
        }

        /// <summary>
        /// The shipped 100% transparent 4×4 placeholder texture
        /// (Textures/EnigmaClear.png). Bound to Mochie's _ScreenTex whenever
        /// the overlay is off: ApplySST blends by the sampled ALPHA, so an
        /// all-zero texture makes it a no-op even while the keyword and the
        /// shared "Always" pass are hot (which zoom/letterbox legitimately
        /// cause). Null when the asset is missing — callers degrade to the
        /// pre-placeholder null-bind behaviour.
        /// </summary>
        private static Texture2D _clearTexture;
        public static Texture2D GetClearTexture()
        {
            if (_clearTexture != null) return _clearTexture;
            foreach (string guid in AssetDatabase.FindAssets("EnigmaClear t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != "EnigmaClear") continue;
                _clearTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (_clearTexture != null) return _clearTexture;
            }
            return null;
        }

        /// <summary>
        /// The texture a type-2 texture action should write in its OFF state:
        /// the transparent placeholder for Mochie's _ScreenTex (see
        /// <see cref="GetClearTexture"/>), null for every other property.
        /// Shared by the edit-mode default-state pass and the build bake of
        /// EnigmaExecutor.rtActionDefaultTextures.
        /// </summary>
        public static Texture GetOffTexture(Material material, string propertyName)
        {
            if (material == null || material.shader == null) return null;
            if (propertyName != "_ScreenTex") return null;
            if (!IsMochieScreenFX(material.shader.name)) return null;
            return GetClearTexture();
        }

        /// <summary>
        /// Computes the set of section-toggle / gate property names Enigma
        /// manages on a material, given the property names its actions and
        /// fader links write. Used to scope <see cref="ApplyMaterialFixups"/>
        /// so user-authored effect state outside Enigma's control survives
        /// rebuilds.
        /// </summary>
        public static HashSet<string> ComputeManagedToggles(
            Material material, IEnumerable<string> usedPropertyNames)
        {
            var managed = new HashSet<string>(StringComparer.Ordinal);
            if (material == null || material.shader == null || usedPropertyNames == null)
                return managed;

            foreach (string prop in usedPropertyNames)
            {
                if (string.IsNullOrEmpty(prop)) continue;
                // Direct writes count as managed (e.g. a button setting _Zoom).
                managed.Add(prop);
                // Section toggle auto-set alongside this property
                // (alsoSetEffectToggle's synthetic action).
                if (TryGetEffectToggle(material, prop, out string tog) && tog != null)
                    managed.Add(tog);
                // Feature-map association (covers _ScreenTex → _SST via the
                // Mochie override table even when the attribute walk misses it).
                var info = GetPropertyKeywordInfo(material, prop);
                if (info.toggleProp != null)
                    managed.Add(info.toggleProp);
            }
            return managed;
        }

        /// <summary>
        /// Maps a property to its Mochie "Always" pass gate id, or -1 when the
        /// property does not gate that pass. Mochie's ScreenFXEditor enables the
        /// "Always" pass iff <c>_Zoom > 0 || _SST > 0 || _Letterbox > 0</c> —
        /// these three mode properties are the only gates. Baked per action into
        /// <c>rtActionAlwaysGate</c> so the runtime executor can manage the pass
        /// for any user-built button targeting these effects, with or without an
        /// associated keyword (_Letterbox has none).
        /// </summary>
        internal static int GetAlwaysPassGateId(Material material, string propertyName)
        {
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
                return -1;
            if (!IsMochieScreenFX(material.shader.name))
                return -1;
            switch (propertyName)
            {
                case "_Zoom":      return 0;
                case "_SST":       return 1;
                case "_Letterbox": return 2;
                default:           return -1;
            }
        }

        /// <summary>Exact-name check shared by all Mochie-specific handling.</summary>
        internal static bool IsMochieScreenFX(string shaderName)
        {
            return shaderName == "Mochie/Screen FX X" || shaderName == "Mochie/Screen FX";
        }

        // Cache: shader asset path → set of all shader_feature(_local)(_stage)
        // keywords declared by the shader. Cleared by ClearCache().
        private static readonly Dictionary<string, HashSet<string>> _shaderKeywordSetCache
            = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// Returns every shader_feature keyword the shader declares (all
        /// pragma forms), or null when the source file can't be read.
        /// </summary>
        internal static HashSet<string> GetShaderKeywordSet(Shader shader)
        {
            if (shader == null) return null;
            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (_shaderKeywordSetCache.TryGetValue(path, out var cached)) return cached;

            var set = new HashSet<string>();
            try
            {
                var rx = new Regex(@"#pragma\s+shader_feature(?:_local)?(?:_fragment|_vertex)?\s+(.+)");
                foreach (string line in File.ReadLines(path))
                {
                    var m = rx.Match(line.Trim());
                    if (!m.Success) continue;
                    foreach (string token in m.Groups[1].Value.Split(
                        new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string kw = token.Trim();
                        if (kw.Length > 0 && kw != "_") set.Add(kw);
                    }
                }
            }
            catch { }
            _shaderKeywordSetCache[path] = set;
            return set;
        }

        // Cache: shader asset path → property names declared with the legacy
        // ShaderLab `Int` type. Cleared by ClearCache().
        private static readonly Dictionary<string, HashSet<string>> _intPropertyCache
            = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// True when the shader declares the property as an integer — either
        /// the modern <c>Integer</c> type (visible via GetPropertyType) or the
        /// legacy ShaderLab <c>Int</c> type, which the property API reports as
        /// Float even though the HLSL uniform may be a real <c>int</c>. Mochie
        /// declares many properties the legacy way; SetFloat alone may not
        /// update those uniforms on VRChat standalone, so callers mirror
        /// writes through SetInt when this returns true.
        /// </summary>
        internal static bool IsIntDeclaredProperty(Shader shader, string propertyName)
        {
            if (shader == null || string.IsNullOrEmpty(propertyName)) return false;

            int n = shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                if (shader.GetPropertyName(i) != propertyName) continue;
#if UNITY_2021_1_OR_NEWER
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Int)
                    return true;
#endif
                break;
            }

            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            HashSet<string> set;
            if (!_intPropertyCache.TryGetValue(path, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    // Legacy `_Prop("label", Int) = 0` declarations.
                    var rx = new Regex(@"(_\w+)\s*\(\s*""[^""]*""\s*,\s*Int\s*\)");
                    foreach (string line in File.ReadLines(path))
                    {
                        var m = rx.Match(line);
                        if (m.Success) set.Add(m.Groups[1].Value);
                    }
                }
                catch { }
                _intPropertyCache[path] = set;
            }
            return set.Contains(propertyName);
        }

        /// <summary>
        /// Value-aware keyword resolution for enum-mode toggle properties.
        /// Mochie's mode enums gate DIFFERENT keywords per value (_SST 1/2 →
        /// _IMAGE_OVERLAY_ON but 3 → _IMAGE_OVERLAY_DISTORTION_ON; _Zoom 2 →
        /// _ZOOM_RGB_ON; _BlurModel 1/2/3 → pixel/dither/radial; …) — a
        /// single property→keyword map can't express that, which used to
        /// leave every non-default mode broken in uploaded worlds (wrong
        /// keyword enabled at runtime AND the right variant never shipped).
        ///
        /// Returns the keyword that should be enabled when the toggle property
        /// is set to <paramref name="value"/>, or null when the value is off
        /// (≤ 0.5), the property isn't a recognised toggle, or the keyword
        /// doesn't exist in this shader (e.g. non-X Mochie lacks zoom/overlay
        /// keywords). Mirrors Mochie's ScreenFXEditor.ApplyMaterialSettings
        /// value→keyword rules; generic shaders fall back to the single
        /// auto-detected keyword.
        /// </summary>
        internal static string GetKeywordForToggleValue(
            Material material, string propertyName, float value)
        {
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
                return null;
            if (value <= 0.5f) return null; // off — nothing to enable

            if (IsMochieScreenFX(material.shader.name))
            {
                int mode = Mathf.RoundToInt(value);
                string kw = null;
                switch (propertyName)
                {
                    case "_SST":               kw = mode >= 3 ? "_IMAGE_OVERLAY_DISTORTION_ON" : "_IMAGE_OVERLAY_ON"; break;
                    case "_Zoom":              kw = mode >= 2 ? "_ZOOM_RGB_ON" : "_ZOOM_ON"; break;
                    case "_DistortionModel":   kw = mode >= 2 ? "_DISTORTION_WORLD_ON" : "_DISTORTION_ON"; break;
                    case "_BlurModel":         kw = mode >= 3 ? "_BLUR_RADIAL_ON" : (mode == 2 ? "_BLUR_DITHER_ON" : "_BLUR_PIXEL_ON"); break;
                    case "_FilterModel":       kw = "_COLOR_ON"; break;
                    case "_ShakeModel":        kw = "_SHAKE_ON"; break;
                    case "_NoiseMode":         kw = "_NOISE_ON"; break;
                    case "_Fog":               kw = "_FOG_ON"; break;
                    case "_Triplanar":         kw = "_TRIPLANAR_ON"; break;
                    case "_OutlineType":       kw = "_OUTLINE_ON"; break;
                    case "_AudioLinkToggle":   kw = "_AUDIOLINK_ON"; break;
                    case "_SobelFilterToggle": kw = "_SOBEL_FILTER_ON"; break;
                    case "_BlurY":             kw = "_BLUR_Y_ON"; break;
                    case "_RGBSplit":          kw = "_CHROMATIC_ABBERATION_ON"; break;
                    case "_DoF":               kw = "_DOF_ON"; break;
                }
                if (kw != null)
                {
                    // Non-X Mochie lacks several of these keywords entirely.
                    var declared = GetShaderKeywordSet(material.shader);
                    if (declared != null && declared.Contains(kw))
                        return kw;
                    return null;
                }
            }

            // Generic fallback: single auto-detected keyword for the toggle.
            var info = GetPropertyKeywordInfo(material, propertyName);
            return info.toggleProp == propertyName ? info.keyword : null;
        }

        /// <summary>
        /// All keywords that the section toggle associated with
        /// <paramref name="propertyName"/> can gate, across every mode value.
        /// Used to enable the FULL group on the variant-keeper material so
        /// every mode's variant ships in the build — the keeper is never
        /// rendered, so over-enabling costs only a few extra compiled
        /// variants. Returns an empty list when no keyword association exists.
        /// </summary>
        internal static List<string> GetGroupKeywords(Material material, string propertyName)
        {
            var result = new List<string>();
            if (material == null || material.shader == null || string.IsNullOrEmpty(propertyName))
                return result;

            var info = GetPropertyKeywordInfo(material, propertyName);
            string toggle = info.toggleProp ?? propertyName;

            if (IsMochieScreenFX(material.shader.name))
            {
                string[] group = null;
                switch (toggle)
                {
                    case "_SST":             group = new[] { "_IMAGE_OVERLAY_ON", "_IMAGE_OVERLAY_DISTORTION_ON" }; break;
                    case "_Zoom":            group = new[] { "_ZOOM_ON", "_ZOOM_RGB_ON" }; break;
                    case "_DistortionModel": group = new[] { "_DISTORTION_ON", "_DISTORTION_WORLD_ON" }; break;
                    case "_BlurModel":       group = new[] { "_BLUR_PIXEL_ON", "_BLUR_DITHER_ON", "_BLUR_RADIAL_ON" }; break;
                }
                if (group != null)
                {
                    var declared = GetShaderKeywordSet(material.shader);
                    foreach (var k in group)
                        if (declared != null && declared.Contains(k))
                            result.Add(k);
                    if (result.Count > 0) return result;
                }
            }

            if (info.keyword != null) result.Add(info.keyword);
            return result;
        }

        /// <summary>
        /// Mirrors Mochie's own <c>ScreenFXEditor.ApplyMaterialSettings</c>:
        /// for each Mochie SFX section, enable/disable the section's
        /// shader_feature_local keyword based on the corresponding master-toggle
        /// property value. Without this, Enigma's build pipeline leaves every
        /// section keyword enabled (so variants survive build-time stripping)
        /// even after <see cref="ApplyMaterialFixups"/> has zeroed the master
        /// toggles, and the shader paths gated only by the keyword (e.g.
        /// <c>ApplyColor</c>, which does not value-gate on <c>_FilterModel</c>)
        /// render with default values — producing the "grey overlay" the user
        /// would otherwise see until they clicked the material to trigger
        /// Mochie's own keyword sync.
        ///
        /// MUST be called AFTER any in-build shader-variant collection has
        /// completed (Enigma Build button, OnPostprocessBuild, or non-build
        /// editor flows). Calling it during the active build window will
        /// disable keywords before Unity strips variants, removing them from
        /// the player build entirely.
        /// </summary>
        public static void SyncMochieKeywordsToValues(Material material)
        {
            if (material == null || material.shader == null) return;
            string shaderName = material.shader.name;
            if (shaderName != "Mochie/Screen FX X" && shaderName != "Mochie/Screen FX")
                return;

            bool isXVersion = shaderName == "Mochie/Screen FX X";

            // 1:1 reproduction of ScreenFXEditor.ApplyMaterialSettings — the
            // editor logic that the user previously had to click the material
            // to invoke.
            int filterModel    = material.HasProperty("_FilterModel")    ? material.GetInt("_FilterModel")    : 0;
            int shakeModel     = material.HasProperty("_ShakeModel")     ? material.GetInt("_ShakeModel")     : 0;
            int distortionModel = material.HasProperty("_DistortionModel") ? material.GetInt("_DistortionModel") : 0;
            int blurModel      = material.HasProperty("_BlurModel")      ? material.GetInt("_BlurModel")      : 0;
            int blurY          = material.HasProperty("_BlurY")          ? material.GetInt("_BlurY")          : 0;
            int rgbSplit       = material.HasProperty("_RGBSplit")       ? material.GetInt("_RGBSplit")       : 0;
            int dof            = material.HasProperty("_DoF")            ? material.GetInt("_DoF")            : 0;
            int zoomMode       = material.HasProperty("_Zoom")           ? material.GetInt("_Zoom")           : 0;
            int sstMode        = material.HasProperty("_SST")            ? material.GetInt("_SST")            : 0;
            int fogMode        = material.HasProperty("_Fog")            ? material.GetInt("_Fog")            : 0;
            int triplanar      = material.HasProperty("_Triplanar")      ? material.GetInt("_Triplanar")      : 0;
            int outlineType    = material.HasProperty("_OutlineType")    ? material.GetInt("_OutlineType")    : 0;
            int noiseMode      = material.HasProperty("_NoiseMode")      ? material.GetInt("_NoiseMode")      : 0;
            int alToggle       = material.HasProperty("_AudioLinkToggle") ? material.GetInt("_AudioLinkToggle") : 0;
            int sobelToggle    = material.HasProperty("_SobelFilterToggle") ? material.GetInt("_SobelFilterToggle") : 0;

            void SetKW(string kw, bool on)
            {
                if (on) material.EnableKeyword(kw);
                else    material.DisableKeyword(kw);
            }

            SetKW("_COLOR_ON",                  filterModel > 0);
            SetKW("_SHAKE_ON",                  shakeModel > 0);
            SetKW("_DISTORTION_ON",             distortionModel == 1);
            SetKW("_DISTORTION_WORLD_ON",       distortionModel == 2);
            SetKW("_BLUR_PIXEL_ON",             blurModel == 1);
            SetKW("_BLUR_DITHER_ON",            blurModel == 2);
            SetKW("_BLUR_RADIAL_ON",            blurModel == 3);
            SetKW("_BLUR_Y_ON",                 blurY == 1);
            SetKW("_CHROMATIC_ABBERATION_ON",   rgbSplit == 1);
            SetKW("_DOF_ON",                    dof == 1);
            SetKW("_ZOOM_ON",                   zoomMode == 1 && isXVersion);
            SetKW("_ZOOM_RGB_ON",               zoomMode == 2 && isXVersion);
            SetKW("_IMAGE_OVERLAY_ON",          sstMode < 3 && sstMode > 0 && isXVersion);
            SetKW("_IMAGE_OVERLAY_DISTORTION_ON", sstMode == 3 && isXVersion);
            SetKW("_FOG_ON",                    fogMode == 1 && isXVersion);
            SetKW("_TRIPLANAR_ON",              triplanar > 0 && isXVersion);
            SetKW("_OUTLINE_ON",                outlineType > 0 && isXVersion);
            SetKW("_NOISE_ON",                  noiseMode == 1);
            SetKW("_AUDIOLINK_ON",              alToggle == 1);
            SetKW("_SOBEL_FILTER_ON",           sobelToggle == 1);

            EditorUtility.SetDirty(material);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHADER-SPECIFIC OVERRIDES
        //  Some third-party shaders use non-conventional property naming that the
        //  heuristic parser can't match to shader_feature_local keywords. This
        //  table lets us provide explicit property→keyword mappings per shader
        //  name. Entries here OVERWRITE any heuristic match for the same property.
        //
        //  Mochie/Screen FX (and SFX X) case:
        //    [Enum(Off,0, Static,1, Animated,2, Distortion,3)]_SST("", Int) = 0
        //    followed by `_ScreenTex`, `_SSTColor`, etc. in the same Properties
        //    section, all guarded at render time by:
        //    #pragma shader_feature_local _ _IMAGE_OVERLAY_ON _IMAGE_OVERLAY_DISTORTION_ON
        //    The heuristic fails because `_SST` doesn't follow the camelCase →
        //    KEYWORD_ON naming convention. Map the SST group to _IMAGE_OVERLAY_ON
        //    so Overlay buttons that set `_SST=1` (Static) correctly enable the
        //    keyword at runtime.
        // ════════════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Dictionary<string, (string keyword, string toggle)>>
            _shaderNameOverrides = new Dictionary<string, Dictionary<string, (string keyword, string toggle)>>
        {
            // Mochie screen FX — two shader variants share the same _SST/overlay structure.
            { "Mochie/Screen FX X", BuildMochieSFXOverrides() },
            { "Mochie/Screen FX",   BuildMochieSFXOverrides() },
        };

        private static Dictionary<string, (string keyword, string toggle)> BuildMochieSFXOverrides()
        {
            // Every property in the "Screenspace Texture" block of Mochie's SFX
            // shader needs `_IMAGE_OVERLAY_ON` enabled to survive shader-feature
            // stripping. _SST is the leader (toggle prop); the rest are dependents.
            const string kw = "_IMAGE_OVERLAY_ON";
            const string leader = "_SST";
            return new Dictionary<string, (string keyword, string toggle)>
            {
                { "_SST",                (kw, leader) },
                { "_SSTBlend",           (kw, leader) },
                { "_SSTUseGlobal",       (kw, leader) },
                { "_SSTMinRange",        (kw, leader) },
                { "_SSTMaxRange",        (kw, leader) },
                { "_ScreenTex",          (kw, leader) },
                { "_SSTColor",           (kw, leader) },
                { "_SSTScale",           (kw, leader) },
                { "_SSTWidth",           (kw, leader) },
                { "_SSTHeight",          (kw, leader) },
                { "_SSTLR",              (kw, leader) },
                { "_SSTUD",              (kw, leader) },
                { "_SSTAnimatedDist",    (kw, leader) },
                { "_SSTColumnsX",        (kw, leader) },
                { "_SSTRowsY",           (kw, leader) },
                { "_SSTAnimationSpeed",  (kw, leader) },
                { "_SSTFrameSizeXP",     (kw, leader) },
                { "_SSTFrameSizeYP",     (kw, leader) },
                { "_SSTFrameSizeXN",     (kw, leader) },
                { "_SSTFrameSizeYN",     (kw, leader) },
            };
        }

        private static Dictionary<string, (string keyword, string toggle)> GetShaderFeatureMap(Shader shader)
        {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return null;

            if (_shaderFeatureCache.TryGetValue(shaderPath, out var cached))
                return cached;

            var map = ParseShaderFeatureKeywords(shaderPath);

            // Merge in shader-name overrides (override-wins). The heuristic may
            // have mapped some of these properties to nothing or to the wrong
            // keyword; the explicit override replaces it.
            if (shader != null && _shaderNameOverrides.TryGetValue(shader.name, out var overrides))
            {
                if (map == null) map = new Dictionary<string, (string keyword, string toggle)>();
                foreach (var kvp in overrides)
                    map[kvp.Key] = kvp.Value;
            }

            // Patch in the curated Mochie property overrides for properties
            // the heuristic missed. Gated on the Mochie shader name — the
            // property names in that table (_Color, _Hue, _Value, …) are far
            // too generic to apply to arbitrary shaders that merely declare a
            // matching keyword. Keyword existence is still verified so the
            // non-X Mochie variant only gets the keywords it actually has.
            if (shader != null && IsMochieScreenFX(shader.name))
            {
                var declared = GetShaderKeywordSet(shader);
                if (map == null) map = new Dictionary<string, (string keyword, string toggle)>();
                foreach (var kvp in _knownPropertyOverrides)
                {
                    if (!map.ContainsKey(kvp.Key)
                        && declared != null && declared.Contains(kvp.Value.keyword))
                        map[kvp.Key] = kvp.Value;
                }
            }

            _shaderFeatureCache[shaderPath] = map;
            return map;
        }

        /// <summary>
        /// Parses a .shader file to build a map from every property name to the
        /// shader_feature_local keyword (+ toggle property) that guards it.
        ///
        /// Algorithm:
        /// 1. Collect all shader_feature_local keywords from #pragma lines.
        /// 2. Walk the Properties block, grouping properties by their nearest
        ///    preceding [ToggleUI] / [Enum] toggle property.
        /// 3. Match each toggle to a keyword via naming convention
        ///    (camelCase → UPPER_SNAKE_CASE + _ON) with fallback heuristics.
        /// 4. Map every property in a group to (keyword, togglePropName).
        /// </summary>
        private static Dictionary<string, (string keyword, string toggle)> ParseShaderFeatureKeywords(
            string shaderPath)
        {
            var result = new Dictionary<string, (string keyword, string toggle)>();

            string[] lines;
            try { lines = File.ReadAllLines(shaderPath); }
            catch { return result; }

            // ── Step 1: Collect all shader_feature keywords ──
            // Matches shader_feature, shader_feature_local, and the
            // stage-scoped _fragment/_vertex forms (liltoon, Poiyomi, and
            // most modern shaders use the stage-scoped variants — the old
            // `shader_feature_local`-only pattern silently skipped them,
            // leaving those shaders with zero keyword auto-detection).
            var allKeywords = new HashSet<string>();
            var sfRegex = new Regex(
                @"#pragma\s+shader_feature(?:_local)?(?:_fragment|_vertex)?\s+(.+)",
                RegexOptions.Compiled);

            foreach (string line in lines)
            {
                var m = sfRegex.Match(line.Trim());
                if (!m.Success) continue;
                foreach (string token in m.Groups[1].Value.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string kw = token.Trim();
                    if (kw.Length > 0 && kw != "_")
                        allKeywords.Add(kw);
                }
            }

            if (allKeywords.Count == 0) return result;

            // ── Step 2: Walk Properties block, collect all properties linearly ──
            // Each property is tagged as toggle/enum/regular.
            var propertyRegex = new Regex(
                @"^\s*(?:\[[^\]]+\]\s*)*(_\w+)\s*\(",
                RegexOptions.Compiled);
            var toggleAttrRegex = new Regex(
                @"\[ToggleUI\]|\[Toggle\]|\[Toggle\([^\)]*\)\]",
                RegexOptions.Compiled);
            var enumAttrRegex = new Regex(
                @"\[Enum\(",
                RegexOptions.Compiled);

            // Regex to extract explicit keyword from [Toggle(KEYWORD)] attributes.
            // BeanFX uses [Toggle(SCREENFX_OUTLINE)] _EnableOutline, etc.
            var explicitKeywordRegex = new Regex(
                @"\[Toggle\((\w+)\)\]",
                RegexOptions.Compiled);

            // (propName, isToggleOrEnum, isEnumWithOff, explicitKeyword)
            var allProps = new List<(string name, bool isToggleOrEnum, bool isEnumWithOff, string explicitKeyword)>();

            bool inProperties = false;
            int braceDepth = 0;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (!inProperties)
                {
                    if (trimmed.StartsWith("Properties"))
                    {
                        inProperties = true;
                        braceDepth = 0;
                        foreach (char c in StripForBraceCount(trimmed))
                        {
                            if (c == '{') braceDepth++;
                            else if (c == '}') braceDepth--;
                        }
                    }
                    continue;
                }

                foreach (char c in StripForBraceCount(trimmed))
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }
                if (braceDepth <= 0 && inProperties)
                    break;

                if (trimmed.StartsWith("//")) continue;
                if (trimmed.Contains("[HideInInspector]")) continue;

                var propMatch = propertyRegex.Match(trimmed);
                if (!propMatch.Success) continue;

                string propName = propMatch.Groups[1].Value;
                bool isToggle = toggleAttrRegex.IsMatch(trimmed);
                bool isEnum = enumAttrRegex.IsMatch(trimmed) &&
                    (trimmed.Contains("Off,0") || trimmed.Contains("Off, 0"));

                // Extract explicit keyword from [Toggle(KEYWORD)] if present.
                string explicitKw = null;
                if (isToggle)
                {
                    var ekMatch = explicitKeywordRegex.Match(trimmed);
                    if (ekMatch.Success)
                    {
                        string candidate = ekMatch.Groups[1].Value;
                        if (allKeywords.Contains(candidate))
                            explicitKw = candidate;
                    }
                }

                allProps.Add((propName, isToggle || isEnum, isEnum, explicitKw));
            }

            // ── Step 3: Two-pass grouping ──
            // Pass A: Try to match every toggle/enum property to a keyword.
            // Only those that match become "group leaders".
            // Prefer explicit keywords from [Toggle(KEYWORD)] attributes when available.
            var leaderKeywords = new Dictionary<string, string>(); // propName → keyword
            foreach (var (name, isTog, _, explicitKw) in allProps)
            {
                if (!isTog) continue;
                if (explicitKw != null)
                {
                    leaderKeywords[name] = explicitKw;
                    continue;
                }
                string kw = MatchToggleToKeyword(name, allKeywords);
                if (kw != null)
                    leaderKeywords[name] = kw;
            }

            // Build stripped prefix for each leader (for prefix matching).
            // e.g., "_SobelFilterToggle" → "SobelFilter", "_ShakeModel" → "Shake",
            //        "_EnableOutline" → "Outline" (BeanFX)
            var leaderPrefixes = new Dictionary<string, string>(); // leader propName → stripped prefix
            foreach (var kvp in leaderKeywords)
            {
                string stripped = kvp.Key.StartsWith("_") ? kvp.Key.Substring(1) : kvp.Key;
                string[] suf = { "Toggle", "Model", "Mode", "Type" };
                foreach (string s in suf)
                {
                    if (stripped.EndsWith(s) && stripped.Length > s.Length)
                    { stripped = stripped.Substring(0, stripped.Length - s.Length); break; }
                }
                // BeanFX: "_EnableOutline" → strip "Enable" prefix → "Outline"
                if (stripped.StartsWith("Enable") && stripped.Length > 6)
                    stripped = stripped.Substring(6);
                leaderPrefixes[kvp.Key] = stripped;
            }

            // Pass B: For each property, find the best-matching leader by prefix.
            // Fallback: if no prefix match, use the nearest preceding leader (linear).
            // Non-matched toggle/enum properties break the linear chain so that
            // unrelated features (e.g., _DBOpacity after _AudioLinkToggle) don't
            // inherit the wrong keyword.
            string linearLeader = null;
            string linearKeyword = null;
            bool linearBroken = false; // true after a non-matching [ToggleUI] is seen
            foreach (var (name, isTog, isEnumOff, _) in allProps)
            {
                if (leaderKeywords.TryGetValue(name, out string kw))
                {
                    // This property IS a leader — map it to itself.
                    linearLeader = name;
                    linearKeyword = kw;
                    linearBroken = false;
                    result[name] = (kw, name);
                    continue;
                }

                // A non-matching toggle/enum may mark the start of a different
                // feature section. [Enum(Off,...)] breaks completely (hard break).
                // [ToggleUI] sets a soft break ONLY if it doesn't share a prefix
                // with the current leader — sub-settings like _ShakeUseGlobal
                // within the _ShakeModel group should not break the chain.
                if (isEnumOff)
                {
                    linearLeader = null;
                    linearKeyword = null;
                    linearBroken = true;
                }
                else if (isTog && linearLeader != null)
                {
                    string togStripped = name.StartsWith("_") ? name.Substring(1) : name;

                    // Known sub-setting toggles are never feature boundaries.
                    // BeanFX: _*UseGlobalFalloff, _*AutoHueShift are per-effect settings.
                    bool isKnownSubSetting = togStripped.Contains("UseGlobal")
                        || togStripped.Contains("AutoHue");

                    if (!isKnownSubSetting)
                    {
                        string leaderPrefix = leaderPrefixes.ContainsKey(linearLeader)
                            ? leaderPrefixes[linearLeader] : "";
                        // Only break if this toggle doesn't share the leader's prefix.
                        // Check both directions: prop starts with prefix, or prefix starts
                        // with prop's first word (handles BeanFX abbreviated names).
                        bool shares = false;
                        if (leaderPrefix.Length > 0)
                        {
                            if (togStripped.StartsWith(leaderPrefix))
                                shares = true;
                            else
                            {
                                int wordEnd = 1;
                                while (wordEnd < togStripped.Length && !char.IsUpper(togStripped[wordEnd]))
                                    wordEnd++;
                                if (wordEnd >= 3)
                                {
                                    string fw = togStripped.Substring(0, wordEnd);
                                    if (leaderPrefix.StartsWith(fw, StringComparison.Ordinal)
                                        || leaderPrefix.Contains(fw))
                                        shares = true;
                                }
                            }
                        }
                        if (!shares)
                            linearBroken = true;
                    }
                }
                else if (isTog)
                {
                    linearBroken = true;
                }

                // Try prefix match: find the leader whose stripped prefix is the
                // longest match at the start of this property name.
                string propStripped = name.StartsWith("_") ? name.Substring(1) : name;
                string bestLeader = null;
                string bestKeyword = null;
                int bestLen = 0;

                foreach (var kvp in leaderPrefixes)
                {
                    string prefix = kvp.Value;
                    if (prefix.Length == 0) continue;

                    // Direct match: property starts with leader prefix.
                    // e.g., "OutlineColor".StartsWith("Outline") → true
                    if (propStripped.StartsWith(prefix) && prefix.Length > bestLen)
                    {
                        bestLen = prefix.Length;
                        bestLeader = kvp.Key;
                        bestKeyword = leaderKeywords[kvp.Key];
                    }
                    // Reverse match: leader prefix starts with property's first word.
                    // Handles abbreviated property names in BeanFX, e.g.,
                    // _EnablePixelate → prefix "Pixelate", property _PixelUseGlobalFalloff
                    // → first word "Pixel", "Pixelate".StartsWith("Pixel") → true
                    else if (bestLeader == null)
                    {
                        // Extract first camelCase word from property name.
                        int wordEnd = 1;
                        while (wordEnd < propStripped.Length && !char.IsUpper(propStripped[wordEnd]))
                            wordEnd++;
                        if (wordEnd >= 3) // require at least 3 chars to avoid false positives
                        {
                            string firstWord = propStripped.Substring(0, wordEnd);
                            if ((prefix.StartsWith(firstWord, StringComparison.Ordinal)
                                || prefix.Contains(firstWord))
                                && firstWord.Length > bestLen)
                            {
                                bestLen = firstWord.Length;
                                bestLeader = kvp.Key;
                                bestKeyword = leaderKeywords[kvp.Key];
                            }
                        }
                    }
                }

                if (bestLeader != null)
                {
                    result[name] = (bestKeyword, bestLeader);
                }
                else if (linearLeader != null && !linearBroken)
                {
                    // Linear fallback: property doesn't prefix-match any leader
                    // but no non-matching toggle has appeared since the last leader.
                    // Handles cases like _Amplitude directly after _ShakeModel.
                    result[name] = (linearKeyword, linearLeader);
                }
            }

            // _knownPropertyOverrides used to be applied here for ANY shader
            // whose keyword set contained the override's keyword — but names
            // like _Color/_Hue/_Value are far too generic, so an unrelated
            // shader declaring a _COLOR_ON keyword inherited Mochie's
            // mappings. The override patch now lives in GetShaderFeatureMap,
            // gated on the Mochie shader name.

            return result;
        }

        /// <summary>
        /// Tries to match a toggle property name to a shader_feature_local keyword.
        ///
        /// Strategy:
        /// 1. Strip common suffixes (Toggle, Model, Mode, Type)
        /// 2. Convert camelCase to UPPER_SNAKE_CASE
        /// 3. Try _NAME_ON pattern against known keywords
        /// 4. If no exact match, try prefix matching for multi-keyword groups
        /// </summary>
        private static string MatchToggleToKeyword(string toggleProp, HashSet<string> keywords)
        {
            if (string.IsNullOrEmpty(toggleProp) || keywords.Count == 0)
                return null;

            // Strip leading underscore
            string raw = toggleProp.StartsWith("_") ? toggleProp.Substring(1) : toggleProp;

            // Try with suffix stripped first, then without stripping.
            // This handles both "_SobelFilterToggle" (strip "Toggle" → "SobelFilter")
            // and "_Fog" / "_DoF" (no suffix to strip).
            string[] namesToTry = new string[2];
            namesToTry[0] = raw; // unstripped first (for short names like _Fog, _DoF)
            namesToTry[1] = null;

            string[] suffixes = { "Toggle", "Model", "Mode", "Type" };
            foreach (string suffix in suffixes)
            {
                if (raw.EndsWith(suffix) && raw.Length > suffix.Length)
                {
                    namesToTry[0] = raw.Substring(0, raw.Length - suffix.Length); // stripped
                    namesToTry[1] = raw; // also try unstripped as fallback
                    break;
                }
            }

            foreach (string name in namesToTry)
            {
                if (name == null) continue;
                string snake = CamelToUpperSnake(name);
                // Also try the name uppercased without snake_case splitting.
                // "AudioLink" → AUDIO_LINK (snake) vs AUDIOLINK (flat).
                // Mochie uses _AUDIOLINK_ON (flat), not _AUDIO_LINK_ON.
                string flat = name.ToUpperInvariant();

                string[] variants = { snake, flat };
                foreach (string v in variants)
                {
                    // Try exact _NAME_ON
                    string candidate = "_" + v + "_ON";
                    if (keywords.Contains(candidate))
                        return candidate;

                    // Try without _ON
                    candidate = "_" + v;
                    if (keywords.Contains(candidate))
                        return candidate;

                    // Try prefix match for multi-keyword groups (e.g., _BLUR → _BLUR_PIXEL_ON).
                    // HashSet iteration order is unspecified — pick the
                    // lexicographically smallest match so the result is
                    // deterministic across runs. Value-aware resolution for
                    // multi-keyword groups lives in GetKeywordForToggleValue;
                    // this is just the stable single-keyword fallback.
                    string prefix = "_" + v + "_";
                    string bestKw = null;
                    foreach (string kw in keywords)
                    {
                        if (kw.StartsWith(prefix)
                            && (bestKw == null || string.CompareOrdinal(kw, bestKw) < 0))
                            bestKw = kw;
                    }
                    if (bestKw != null)
                        return bestKw;
                }
            }

            // Well-known aliases for popular shaders where the toggle property name
            // has no naming relation to its keyword. Covers the two Mochie Screen FX
            // exceptions so the entire shader is auto-detected without manual actions.
            if (_knownAliases.TryGetValue(toggleProp, out string alias) && keywords.Contains(alias))
                return alias;

            return null;
        }

        /// <summary>
        /// Converts a camelCase or PascalCase string to UPPER_SNAKE_CASE.
        /// Example: "SobelFilter" → "SOBEL_FILTER", "DoF" → "DOF"
        /// </summary>
        private static string CamelToUpperSnake(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c) && i > 0)
                {
                    // Insert underscore before uppercase if preceded by lowercase,
                    // or if it's the start of a new word in a sequence like "DoF" → "DO_F"
                    char prev = input[i - 1];
                    if (char.IsLower(prev))
                        sb.Append('_');
                    else if (i + 1 < input.Length && char.IsLower(input[i + 1]))
                        sb.Append('_');
                }
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Clears all cached maps (e.g., when shaders are reimported).</summary>
        internal static void ClearCache()
        {
            _moduleMapCache.Clear();
            _shaderFeatureCache.Clear();
            _shaderKeywordSetCache.Clear();
            _intPropertyCache.Clear();
            _textureWarningCache.Clear();
            _attributeToggleMapCache.Clear();
            _inspectorDataCache.Clear();
            _propertyGroupsCache.Clear();
            // Also drop the drawer-side cache that maps (shader, propName) →
            // action property type. It's in EnigmaActionListDrawer because
            // it's only used by the action drawer, but logically it's part
            // of the same "shader lookup is stale after reimport" family.
            EnigmaActionListDrawer.ClearPropertyTypeCache();
            // Drop the script-path index too — if the user adds/removes/renames
            // a .cs inspector file we want a fresh walk on the next scrape
            // rather than serving a stale cached path.
            _csScriptPathIndex = null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  TEXTURE IMPORT VALIDATION
        //
        //  Surfaces likely-broken texture assignments in the action drawer: e.g.
        //  a Sprite imported with Clamp wrap used as a Mochie triplanar scan
        //  texture. Those tend to render invisibly because the effect's shader
        //  scrolls UVs beyond [0,1] and samples the texture's transparent edge.
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A single concern about a texture being assigned to a shader property.
        /// Rendered as a HelpBox beneath the texture picker in the action list.
        /// </summary>
        internal struct TextureShaderUseWarning
        {
            public string      Summary;
            public string      Detail;
            public MessageType Severity;
        }

        // Cache raw (unfiltered) warnings per texture instance so we don't
        // rescan the importer settings on every inspector repaint. The
        // property-name filter is applied fresh on each lookup so the same
        // texture can warn for one property and not another. Invalidated by
        // ClearCache().
        private static readonly Dictionary<int, List<TextureShaderUseWarning>> _textureWarningCache
            = new Dictionary<int, List<TextureShaderUseWarning>>();

        // Shader properties where Clamp wrap mode and Sprite texture type are
        // intentional and expected — the effect samples the texture as a single
        // positioned/scaled image rather than tiling across the screen. For
        // these, any texture that decodes at all is fine, so we suppress the
        // entire warning pipeline.
        //
        // Add new entries as they come up. Keep the check property-name-only
        // (not shader-name-qualified) since these names are distinctive enough
        // that collisions with unrelated shaders are unlikely.
        private static readonly HashSet<string> _tilingInsensitiveProperties =
            new HashSet<string>
            {
                "_ScreenTex",  // Mochie Screen FX — Image Overlay (centered, scaled image)
            };

        /// <summary>
        /// Inspects a texture's import settings for properties that commonly
        /// cause rendering problems when the texture is assigned to a shader
        /// property (e.g., Mochie Screen FX triplanar or overlay textures).
        ///
        /// Flags, in order of severity:
        /// - Texture Type = Sprite → usually imported from a UI folder with
        ///   Clamp wrap and partial opacity; almost never renders as expected
        ///   in tiling / scrolling shader effects.
        /// - (When not a Sprite) Wrap Mode = Clamp → effects that scroll or
        ///   project UVs outside [0,1] sample transparent edges instead of
        ///   tiling seamlessly. Shown at Info severity since Clamp is legit
        ///   for some overlays.
        ///
        /// Returns an empty list when:
        /// - <paramref name="propertyName"/> is on the tiling-insensitive list
        ///   (e.g., Mochie's _ScreenTex overlay, where Clamp/Sprite is fine).
        /// - The texture is a non-asset (RenderTexture, built-in,
        ///   runtime-created) where no importer exists.
        /// </summary>
        internal static List<TextureShaderUseWarning> GetTextureShaderUseWarnings(
            Texture tex, string propertyName)
        {
            if (tex == null) return _emptyWarnings;

            // Property-scoped suppression: overlay-style properties where the
            // shader samples the texture as a single positioned image accept
            // Sprite/Clamp textures without issue.
            if (!string.IsNullOrEmpty(propertyName)
                && _tilingInsensitiveProperties.Contains(propertyName))
                return _emptyWarnings;

            int id = tex.GetInstanceID();
            if (_textureWarningCache.TryGetValue(id, out var cached))
                return cached;

            var warnings = new List<TextureShaderUseWarning>();

            string assetPath = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(assetPath))
            {
                _textureWarningCache[id] = warnings;
                return warnings;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                _textureWarningCache[id] = warnings;
                return warnings;
            }

            // Read the importer's TextureImporterSettings directly — tex.wrapMode
            // can be misleading for Sprites and reports the runtime sampler state
            // rather than the import-time wrap setting.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool isSprite  = importer.textureType == TextureImporterType.Sprite;
            bool isClamp   = settings.wrapMode == TextureWrapMode.Clamp;

            if (isSprite)
            {
                warnings.Add(new TextureShaderUseWarning
                {
                    Summary  = "Texture Type is Sprite",
                    Detail   =
                        "This texture is imported as a UI sprite. Sprites default to " +
                        "Clamp wrap mode and often contain transparent regions, so " +
                        "tiling or scrolling effects (e.g., triplanar scan) may render " +
                        "invisibly.\n\n" +
                        "Fix: in the texture's import settings, change Texture Type to " +
                        "Default and Wrap Mode to Repeat — or duplicate the file and " +
                        "edit the copy so the original sprite asset is left alone.",
                    Severity = MessageType.Warning
                });
            }
            else if (isClamp)
            {
                warnings.Add(new TextureShaderUseWarning
                {
                    Summary  = "Wrap Mode is Clamp",
                    Detail   =
                        "This texture's Wrap Mode is Clamp. Shader effects that scroll " +
                        "or project UVs outside [0,1] will sample the texture's edge " +
                        "pixels instead of tiling seamlessly, which usually produces no " +
                        "visible output. Safe to ignore if you're using it as a single " +
                        "centered overlay; change Wrap Mode to Repeat in the import " +
                        "settings if the effect should tile.",
                    Severity = MessageType.Info
                });
            }

            _textureWarningCache[id] = warnings;
            return warnings;
        }

        private static readonly List<TextureShaderUseWarning> _emptyWarnings = new List<TextureShaderUseWarning>();

        /// <summary>
        /// Draws <see cref="GetTextureShaderUseWarnings"/> as HelpBoxes directly
        /// below the texture picker field. Call immediately after the
        /// ObjectField in the action drawer, passing the action's shader
        /// property name so overlay-style properties (e.g., Mochie
        /// <c>_ScreenTex</c>) suppress the warning.
        /// </summary>
        internal static void DrawTextureShaderUseWarnings(Texture tex, string propertyName)
        {
            var warnings = GetTextureShaderUseWarnings(tex, propertyName);
            for (int i = 0; i < warnings.Count; i++)
            {
                var w = warnings[i];
                EditorGUILayout.HelpBox(w.Summary + "\n\n" + w.Detail, w.Severity);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  THRY-CONVENTION SECTION DETECTION (Poiyomi, etc.)
        // ════════════════════════════════════════════════════════════════════════

        // Quick probe: does the shader declare any `m_start_*` properties?
        // Thry-edited shaders always emit at least one. Cheap linear walk — we
        // return as soon as we find one, so for non-Thry shaders this is a
        // one-pass scan of the property list with a prefix check per name.
        private static bool HasThrySectionMarkers(Shader shader)
        {
            int n = shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                if (!string.IsNullOrEmpty(name)
                    && name.StartsWith("m_start_", System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // Regex for Thry's `reference_property:_FooToggle` annotation in a
        // marker property's description. Extracted once so the section walker
        // doesn't recompile it per property.
        private static readonly Regex _thryReferencePropertyRegex =
            new Regex(@"reference_property:(\w+)", RegexOptions.Compiled);

        // Strip Thry's `--{...}` options suffix off a description, returning
        // just the display label.
        private static string ParseThryLabel(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return null;
            int marker = desc.IndexOf("--{", System.StringComparison.Ordinal);
            string label = marker > 0 ? desc.Substring(0, marker) : desc;
            return label.Trim();
        }

        // Build a ShaderPropertyGroup list for Thry-edited shaders. Each
        // `m_start_<Name>` opens a group at the top of the output; its label
        // is parsed from the description, and the group's toggle is the
        // shader property named by `reference_property:<X>` (if present and
        // resolvable). `m_end_<Name>` closes the group. Children from
        // `s_start_/s_end_` sub-sections are FLATTENED into the enclosing
        // `m_` section so the search tree stays 2-level (matches how Mochie /
        // BeanFX grouped sections are rendered today).
        //
        // Nested `m_start_` (e.g. Decal0 inside DecalSection) become their
        // own top-level group in the output — the Thry inspector indents
        // them visually, but for our tree they're all peers. That matches
        // the user's expectation: "Color Adjust, Details, Vertex Options,
        // etc. should be a toggle category."
        private static List<ShaderPropertyGroup> BuildThryShaderPropertyGroups(Shader shader)
        {
            var groups = new List<ShaderPropertyGroup>();
            int n = shader.GetPropertyCount();

            // Ungrouped bucket for properties that appear before the first
            // `m_start_` (usually just a few visible Poiyomi header props
            // like `_Mode`, `_ShaderOptimizerEnabled`).
            var ungrouped = new ShaderPropertyGroup();
            groups.Add(ungrouped);

            // Stack of open `m_` section groups. Only the top of the stack
            // is the "current section" that new children get added to. On
            // `m_end_` we pop. Sub-section `s_start_`/`s_end_` markers don't
            // create new groups — sub-section children just keep flowing
            // into the enclosing `m_` section.
            var mStack = new Stack<ShaderPropertyGroup>();

            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                if (string.IsNullOrEmpty(name)) continue;
                string desc = shader.GetPropertyDescription(i);
                var flags  = shader.GetPropertyFlags(i);
                bool hidden = (flags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0;
                var pType   = shader.GetPropertyType(i);

                if (name.StartsWith("m_start_", System.StringComparison.Ordinal))
                {
                    string label = ParseThryLabel(desc);
                    if (string.IsNullOrEmpty(label))
                        label = name.Substring("m_start_".Length);

                    // Try to resolve the reference_property to a real toggle.
                    // If it exists, the group's toggle descriptor points at
                    // that property so users can select it in the search.
                    ShaderPropertyDescriptor toggleDesc = null;
                    var refMatch = _thryReferencePropertyRegex.Match(desc ?? "");
                    if (refMatch.Success)
                    {
                        string refName = refMatch.Groups[1].Value;
                        // Look up the referenced property's index + type.
                        // Small linear scan — Thry shaders have thousands of
                        // properties but this only runs per section (~150×)
                        // and the inner scan is bounded by the same n.
                        // Acceptable given the per-shader cache on top.
                        for (int j = 0; j < n; j++)
                        {
                            if (shader.GetPropertyName(j) == refName)
                            {
                                toggleDesc = new ShaderPropertyDescriptor
                                {
                                    index             = j,
                                    name              = refName,
                                    description       = label,
                                    type              = shader.GetPropertyType(j),
                                    hasToggleAttribute = true,
                                    isSectionToggle   = true,
                                };
                                break;
                            }
                        }
                    }

                    // If we couldn't resolve a real toggle, synthesize a
                    // descriptor keyed on the `m_start_` marker itself so
                    // PopulateShaderPropertyTree can still use it for the
                    // group's title. The tree builder suppresses toggle
                    // entries whose name starts with "m_start_" or
                    // "s_start_" so we never surface the marker as a
                    // selectable property.
                    if (toggleDesc == null)
                    {
                        toggleDesc = new ShaderPropertyDescriptor
                        {
                            index             = i,
                            name              = name,
                            description       = label,
                            type              = pType,
                            hasToggleAttribute = false,
                            isSectionToggle   = true,
                        };
                    }

                    var group = new ShaderPropertyGroup { toggle = toggleDesc };
                    groups.Add(group);
                    mStack.Push(group);
                    continue;
                }

                if (name.StartsWith("m_end_", System.StringComparison.Ordinal))
                {
                    if (mStack.Count > 0) mStack.Pop();
                    continue;
                }

                // Sub-section markers are info-only for our flat tree —
                // advance without touching groups.
                if (name.StartsWith("s_start_", System.StringComparison.Ordinal)) continue;
                if (name.StartsWith("s_end_",   System.StringComparison.Ordinal)) continue;

                // Regular property. Skip hidden ones (Poiyomi hides a lot of
                // sub-option properties that only show conditionally in the
                // Thry inspector; surfacing every hidden sub-option in the
                // search tree would be noise for the common case).
                if (hidden) continue;

                // Skip emitting the reference toggle as a child — it's
                // already serving as the section header.
                var currentSection = mStack.Count > 0 ? mStack.Peek() : ungrouped;
                if (currentSection.toggle != null
                    && currentSection.toggle.index == i
                    && currentSection.toggle.name  == name)
                    continue;

                var d = new ShaderPropertyDescriptor
                {
                    index       = i,
                    name        = name,
                    description = ParseThryLabel(desc) ?? desc,
                    type        = pType,
                };

                // Detect toggle-ish attributes so the tree builder can hang
                // a gear icon on sub-toggles. Recognizes both Unity
                // built-ins and Thry's custom variants.
                string[] attrs = shader.GetPropertyAttributes(i);
                if (attrs != null)
                {
                    foreach (var a in attrs)
                    {
                        if (string.IsNullOrEmpty(a)) continue;
                        if (a.Equals("Toggle",    System.StringComparison.OrdinalIgnoreCase)
                         || a.Equals("ToggleUI",  System.StringComparison.OrdinalIgnoreCase)
                         || a.StartsWith("Toggle(",     System.StringComparison.OrdinalIgnoreCase)
                         || a.StartsWith("ThryToggle",  System.StringComparison.OrdinalIgnoreCase))
                        {
                            d.hasToggleAttribute = true;
                            break;
                        }
                    }
                }

                currentSection.children.Add(d);
            }

            // Drop the ungrouped bucket if nothing landed in it (Poiyomi
            // typically has a handful there for the pre-category header
            // properties; don't emit an empty root group either way).
            if (ungrouped.children.Count == 0) groups.RemoveAt(0);
            return groups;
        }
    }
}
#endif

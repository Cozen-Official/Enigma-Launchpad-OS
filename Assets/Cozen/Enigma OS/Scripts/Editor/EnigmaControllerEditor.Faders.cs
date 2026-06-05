#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UdonSharp;
using VRC.Udon;
using System.Collections.Generic;
using System.Linq;

namespace Cozen.EnigmaOS.Editor
{
    public partial class EnigmaControllerEditor
    {
        private int _faderDragSource = -1;
        private int _faderDragTargetLine = -1;
        private readonly List<float> _faderRowTopYs = new List<float>();
        private readonly HashSet<int> _collapsedStaticFaders = new HashSet<int>();

        private void DrawFaders()
        {
            EnigmaController ctrl = (EnigmaController)target;

            SerializedProperty countProp = _so.FindProperty("staticFaderCount");
            int currentCount = countProp.intValue;

            EditorGUILayout.HelpBox(
                "Static faders are always bound to their configured targets. " +
                "Dynamic faders from active entries are populated after these in the fader bank.",
                MessageType.Info);

            if (currentCount > 0)
            {
                // Safety check: ensure arrays are actually large enough before attempting to draw.
                // Only resize once (when arrays are undersized), not every frame.
                if (_so.FindProperty("rtStaticFaderNames").arraySize < currentCount ||
                    _so.FindProperty("rtStaticFaderTargetsSlider")?.arraySize < currentCount ||
                    _so.FindProperty("rtStaticFaderSliders")?.arraySize < currentCount ||
                    _so.FindProperty("rtStaticFaderTargetsSkybox")?.arraySize < currentCount)
                {
                    ResizeStaticFaderArrays(currentCount);
                    _so.ApplyModifiedProperties();
                }

                EditorGUILayout.Space(8);

                int removeIdx = -1;
                int maxFaders = ctrl.faderSlots != null ? ctrl.faderSlots.Length : 0;

                SerializedProperty avArray = _so.FindProperty("rtStaticFaderAlwaysVisible");


                // Size row-Y tracking list
                while (_faderRowTopYs.Count < currentCount + 1) _faderRowTopYs.Add(0f);
                while (_faderRowTopYs.Count > currentCount + 1) _faderRowTopYs.RemoveAt(_faderRowTopYs.Count - 1);

                for (int i = 0; i < currentCount; i++)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    bool sfCollapsed = _collapsedStaticFaders.Contains(i);
                    string sfArrow = sfCollapsed ? "\u25B6" : "\u25BC";
                    // Use a GUIStyle with left padding to match dynamic fader handle spacing
                    var sfHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(28, 0, 0, 0) };
                    SerializedProperty sfNameProp = _so.FindProperty("rtStaticFaderNames").GetArrayElementAtIndex(i);
                    string sfName = sfNameProp.stringValue;
                    string sfTitle = string.IsNullOrEmpty(sfName)
                        ? $"{sfArrow} Static Fader {i + 1}"
                        : $"{sfArrow} Static Fader {i + 1}: {sfName}";
                    EditorGUILayout.LabelField(sfTitle, sfHeaderStyle);
                    Rect headerRect = GUILayoutUtility.GetLastRect();

                    // Track row Y for drag
                    if (Event.current.type == EventType.Repaint && i < _faderRowTopYs.Count)
                        _faderRowTopYs[i] = headerRect.y;

                    // Drag insertion line
                    if (_faderDragSource >= 0 && _faderDragTargetLine == i && _faderDragSource != i)
                        EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y - 2f, headerRect.width, 2f),
                            new Color(0.25f, 0.65f, 1f, 0.9f));

                    // Drag handle visual — GUI.* only (no EditorGUI.LabelField) to preserve control IDs
                    Rect handleRect = new Rect(headerRect.x, headerRect.y, 24f, headerRect.height);
                    if (Event.current.type == EventType.Repaint)
                    {
                        bool isAV = i < avArray.arraySize && avArray.GetArrayElementAtIndex(i).boolValue;
                        EditorGUI.DrawRect(handleRect, new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.25f : 0.15f));
                        if (isAV && _pinIcon != null)
                            GUI.DrawTexture(new Rect(handleRect.x + 4, handleRect.y + 1, 16, 16), _pinIcon, ScaleMode.ScaleToFit);
                        else
                            GUI.Label(handleRect, "\u283F", EditorStyles.centeredGreyMiniLabel);
                    }

                    // Drag handle cursor + mouse handler
                    EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                        && handleRect.Contains(Event.current.mousePosition))
                    {
                        _faderDragSource = i;
                        _faderDragTargetLine = i;
                        Event.current.Use();
                    }

                    // Collapse click on label area (between handle and buttons)
                    Rect collapseRect = new Rect(headerRect.x + 28f, headerRect.y, headerRect.width - 28f - 98f, headerRect.height);
                    if (Event.current.type == EventType.MouseDown && collapseRect.Contains(Event.current.mousePosition))
                    {
                        if (sfCollapsed) _collapsedStaticFaders.Remove(i);
                        else _collapsedStaticFaders.Add(i);
                    }

                    // Options button (overlaid on right side of header)
                    float rx = headerRect.xMax - 98f;
                    if (GUI.Button(new Rect(rx, headerRect.y, 70f, headerRect.height), "Options \u25BE", EditorStyles.miniButton))
                    {
                        int capturedIdx = i;
                        bool isAV = i < avArray.arraySize && avArray.GetArrayElementAtIndex(i).boolValue;
                        var menu = new GenericMenu();
                        if (isAV)
                            menu.AddItem(new GUIContent("Always Visible"), true, () =>
                            { avArray.GetArrayElementAtIndex(capturedIdx).boolValue = false; _so.ApplyModifiedProperties(); });
                        else if (i < maxFaders)
                            menu.AddItem(new GUIContent("Always Visible"), false, () =>
                            { avArray.GetArrayElementAtIndex(capturedIdx).boolValue = true; _so.ApplyModifiedProperties(); });
                        else
                            menu.AddDisabledItem(new GUIContent("Always Visible (exceeds fader slot count)"));
                        menu.ShowAsContext();
                    }
                    rx += 74f;
                    if (GUI.Button(new Rect(rx, headerRect.y, 24f, headerRect.height), "\u2715"))
                        removeIdx = i;

                    if (!sfCollapsed)
                        DrawStaticFaderElement(i);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(4);
                }

                // Bottom drop target Y
                if (Event.current.type == EventType.Repaint && currentCount > 0 && currentCount < _faderRowTopYs.Count)
                {
                    Rect lastBox = GUILayoutUtility.GetLastRect();
                    _faderRowTopYs[currentCount] = lastBox.yMax + 4f;
                }
                if (_faderDragSource >= 0 && _faderDragTargetLine == currentCount)
                {
                    Rect lastBox = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(lastBox.x, lastBox.yMax + 3f, lastBox.width, 2f),
                        new Color(0.25f, 0.65f, 1f, 0.9f));
                }

                // Drag tracking / commit
                if (_faderDragSource >= 0)
                {
                    if (Event.current.type == EventType.MouseDrag)
                    {
                        float mouseY = Event.current.mousePosition.y;
                        int best = 0;
                        float bestDist = float.MaxValue;
                        for (int j = 0; j <= currentCount && j < _faderRowTopYs.Count; j++)
                        {
                            float d = Mathf.Abs(_faderRowTopYs[j] - mouseY);
                            if (d < bestDist) { bestDist = d; best = j; }
                        }
                        if (best != _faderDragTargetLine) { _faderDragTargetLine = best; Repaint(); }
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseUp)
                    {
                        int src = _faderDragSource;
                        int tgt = _faderDragTargetLine;
                        _faderDragSource = -1;
                        _faderDragTargetLine = -1;
                        if (tgt >= 0 && tgt != src && tgt != src + 1)
                        {
                            int dest = tgt > src ? tgt - 1 : tgt;
                            MoveStaticFader(src, dest);
                            if (dest >= maxFaders && dest < avArray.arraySize
                                && avArray.GetArrayElementAtIndex(dest).boolValue)
                            {
                                avArray.GetArrayElementAtIndex(dest).boolValue = false;
                                _so.ApplyModifiedProperties();
                            }
                        }
                        _collapsedStaticFaders.Clear();
                        Repaint();
                        Event.current.Use();
                    }
                }

                if (removeIdx >= 0)
                {
                    RemoveStaticFaderAtIndex(removeIdx);
                    _collapsedStaticFaders.Clear();
                    countProp.intValue = currentCount - 1;
                }
            }

            if (GUILayout.Button("+ Static Fader"))
            {
                int newCount = currentCount + 1;
                countProp.intValue = newCount;
                ResizeStaticFaderArrays(newCount);
            }
        }

        // Helpers to draw SerializedProperty fields without PropertyField (avoids IMGUI control ID issues).
        private static void SPString(SerializedProperty p, string label)
        { string v = EditorGUILayout.TextField(label, p.stringValue); if (v != p.stringValue) p.stringValue = v; }
        private static void SPFloat(SerializedProperty p, string label)
        { float v = EditorGUILayout.FloatField(label, p.floatValue); if (!Mathf.Approximately(v, p.floatValue)) p.floatValue = v; }
        private static void SPColor(SerializedProperty p, string label)
        { Color v = EditorGUILayout.ColorField(label, p.colorValue); if (v != p.colorValue) p.colorValue = v; }
        private static void SPBool(SerializedProperty p, string label)
        { bool v = EditorGUILayout.Toggle(label, p.boolValue); if (v != p.boolValue) p.boolValue = v; }
        private static void SPBool(SerializedProperty p, GUIContent label)
        { bool v = EditorGUILayout.Toggle(label, p.boolValue); if (v != p.boolValue) p.boolValue = v; }
        private static void SPObject<T>(SerializedProperty p, string label) where T : UnityEngine.Object
        { var v = EditorGUILayout.ObjectField(label, p.objectReferenceValue, typeof(T), true); if (v != p.objectReferenceValue) p.objectReferenceValue = v; }

        /// <summary>
        /// Extracts an <see cref="UdonSharpBehaviour"/> from whatever the user
        /// dropped into the fader's Behaviour field.
        ///
        ///   - <c>UdonSharpBehaviour</c>       → returned directly.
        ///   - <c>UdonBehaviour</c>            → falls back to GetComponent on
        ///     its GameObject (UdonBehaviour is the native runtime wrapper;
        ///     UdonSharpBehaviour is the authored C# class. When a VRC
        ///     component is dragged, the proxy we want lives on the same GO).
        ///   - <c>GameObject</c>               → <c>GetComponent&lt;UdonSharpBehaviour&gt;()</c>.
        ///   - <c>Component</c> (MonoBehaviour, etc.) → GetComponent on its GameObject.
        ///   - Anything else → null, caller logs a warning.
        /// </summary>
        private static UdonSharpBehaviour ResolveUdonSharpBehaviour(UnityEngine.Object source)
        {
            if (source == null) return null;
            if (source is UdonSharpBehaviour usb) return usb;
            if (source is GameObject go) return go.GetComponent<UdonSharpBehaviour>();
            if (source is UdonBehaviour ub)
                return ub.gameObject != null ? ub.gameObject.GetComponent<UdonSharpBehaviour>() : null;
            if (source is Component c) return c.GetComponent<UdonSharpBehaviour>();
            return null;
        }

        private void DrawStaticFaderElement(int index)
        {
            SerializedProperty nameProp = _so.FindProperty("rtStaticFaderNames").GetArrayElementAtIndex(index);
            SPString(nameProp, $"Fader {index + 1} Name");

            SerializedProperty isUdonProp = _so.FindProperty("rtStaticFaderTargetsUdon").GetArrayElementAtIndex(index);
            SerializedProperty isSliderProp = _so.FindProperty("rtStaticFaderTargetsSlider").GetArrayElementAtIndex(index);

            bool udon = isUdonProp.boolValue;
            bool slider = isSliderProp.boolValue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Type", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
            
            bool newMaterial = GUILayout.Toggle(!udon && !slider, "Material", EditorStyles.miniButtonLeft);
            bool newUdon = GUILayout.Toggle(udon, "Udon", EditorStyles.miniButtonMid);
            bool newSlider = GUILayout.Toggle(slider, "UI Slider", EditorStyles.miniButtonRight);

            if (newUdon && !udon) { isUdonProp.boolValue = true; isSliderProp.boolValue = false; }
            else if (newSlider && !slider) { isUdonProp.boolValue = false; isSliderProp.boolValue = true; }
            else if (newMaterial && (udon || slider)) { isUdonProp.boolValue = false; isSliderProp.boolValue = false; }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUI.indentLevel++;

            if (isUdonProp.boolValue)
            {
                SerializedProperty behaviourProp = _so.FindProperty("rtStaticFaderUdonBehaviours").GetArrayElementAtIndex(index);
                // Accept any Object drag — GameObject, MonoBehaviour, UdonBehaviour,
                // UdonSharpBehaviour — and resolve to UdonSharpBehaviour on write.
                // A direct SerializedProperty-backed ObjectField can't accept a
                // GameObject because the underlying array is typed
                // UdonSharpBehaviour[]; Unity silently rejects the assignment and
                // the field reads "None (Object)" even after a drop. Routing
                // through a local picker + resolver lets the user drop whatever
                // makes sense and we compute the right component.
                //
                // Primary Behaviour field with a "+" button that appends an
                // extra Udon target to this fader. Multi-target Udon faders
                // let one slider drive e.g. lightColorTint on every VRSL
                // light in the scene at once — same UX as the + button on
                // the material-renderer section.
                EditorGUILayout.BeginHorizontal();
                var currentRef = behaviourProp.objectReferenceValue;
                var picked = EditorGUILayout.ObjectField("Behaviour", currentRef, typeof(UnityEngine.Object), true);
                if (picked != currentRef)
                {
                    UdonSharpBehaviour resolved = ResolveUdonSharpBehaviour(picked);
                    behaviourProp.objectReferenceValue = resolved;
                    if (picked != null && resolved == null)
                    {
                        Debug.LogWarning(
                            $"[EnigmaOS] '{picked.name}' has no UdonSharpBehaviour component — " +
                            "fader Behaviour field cleared. Drop a GameObject with an UdonSharpBehaviour " +
                            "on it, or drop the behaviour component directly.");
                    }
                }
                if (GUILayout.Button("+", GUILayout.Width(24)))
                    AddExtraStaticFaderUdon(index);
                EditorGUILayout.EndHorizontal();

                // Draw any existing extra Udon behaviour rows. Each row is
                // one Behaviour field + "-" button. The variable name is
                // shared across all targets on the entry (one fader, one
                // variable name, many behaviours holding the same variable).
                DrawExtraStaticFaderUdon(index);

                UdonBehaviour udonTarget = null;
                if (behaviourProp.objectReferenceValue != null)
                {
                    if (behaviourProp.objectReferenceValue is UdonBehaviour ub)
                        udonTarget = ub;
                    else if (behaviourProp.objectReferenceValue is MonoBehaviour mb)
                        udonTarget = mb.GetComponent<UdonBehaviour>();
                    else if (behaviourProp.objectReferenceValue is GameObject go)
                        udonTarget = go.GetComponent<UdonBehaviour>();
                }

                if (udonTarget == null)
                {
                    EditorGUILayout.HelpBox("Assign an Udon Behavior to select a variable.", MessageType.Info);
                }
                else
                {
                    if (!TryBuildUdonVariableOptions(new List<UdonBehaviour> { udonTarget }, out List<string> variableNames, out string warning))
                    {
                        if (!string.IsNullOrEmpty(warning)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                    }
                    else if (variableNames.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No public variables found.", MessageType.Info);
                    }
                    else
                    {
                        SerializedProperty varNameProp = _so.FindProperty("rtStaticFaderUdonVariableNames").GetArrayElementAtIndex(index);
                        string currentName = varNameProp.stringValue;
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel(new GUIContent("Variable Name"));
                        string displayName = string.IsNullOrEmpty(currentName) ? "(None)" : currentName;
                        GUILayout.Label(displayName, EditorStyles.textField);
                        if (GUILayout.Button("Search", GUILayout.Width(60)))
                        {
                            // Capture the UdonSharp proxy (if any) so the
                            // autofill can read the live C# field value. The
                            // native UdonBehaviour's publicVariables store is
                            // cached and often lags behind the script's
                            // current default — e.g. when the user edits the
                            // C# field default but hasn't re-synced via
                            // Build or a play-mode round-trip.
                            var proxyForAutofill =
                                behaviourProp.objectReferenceValue as MonoBehaviour;
                            OpenUdonVariableSearchWindow(variableNames, (selectedName) =>
                            {
                                varNameProp.stringValue = selectedName;
                                AutofillStaticFaderUdonValues(index, udonTarget, proxyForAutofill, selectedName);
                                _so.ApplyModifiedProperties();
                                Repaint();
                            });
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else if (isSliderProp.boolValue)
            {
                SPObject<UnityEngine.Object>(_so.FindProperty("rtStaticFaderSliders").GetArrayElementAtIndex(index), "UI Slider");
                SPBool(_so.FindProperty("rtStaticFaderSliderReversed").GetArrayElementAtIndex(index), "Reversed");
            }
            else
            {
                SerializedProperty skyboxArray = _so.FindProperty("rtStaticFaderTargetsSkybox");
                if (skyboxArray == null || skyboxArray.arraySize <= index) return;

                SerializedProperty rendererProp = _so.FindProperty("rtStaticFaderRenderers").GetArrayElementAtIndex(index);
                SerializedProperty matIndexProp = _so.FindProperty("rtStaticFaderMaterialIndices").GetArrayElementAtIndex(index);
                SerializedProperty propNameProp = _so.FindProperty("rtStaticFaderPropertyNames").GetArrayElementAtIndex(index);
                SerializedProperty isSkyboxProp = skyboxArray.GetArrayElementAtIndex(index);
                bool isSkybox = isSkyboxProp.boolValue;

                // Renderer field with Skybox and + buttons. The + appends another
                // renderer to this static fader entry (stored in the flat
                // rtStaticFaderExtra* arrays — see EnigmaController data model).
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(isSkybox))
                    SPObject<UnityEngine.Renderer>(rendererProp, "Renderer");
                if (GUILayout.Button(isSkybox ? "Clear" : "Skybox", GUILayout.Width(60)))
                {
                    isSkyboxProp.boolValue = !isSkybox;
                    isSkybox = !isSkybox;
                    if (isSkybox)
                        rendererProp.objectReferenceValue = null;
                }
                using (new EditorGUI.DisabledScope(isSkybox))
                {
                    if (GUILayout.Button("+", GUILayout.Width(24)))
                        AddExtraStaticFaderRenderer(index);
                }
                EditorGUILayout.EndHorizontal();

                // Extra renderer rows (for multi-renderer static faders).
                // Only shown when not in skybox mode. Each row is a renderer
                // slot + material popup + remove ("-") button.
                if (!isSkybox)
                    DrawExtraStaticFaderRenderers(index);

                // Warn if the primary renderer is a VRSL fixture mesh —
                // material-property faders won't work on those, the user
                // should drive lightColorTint via the Udon path. Static
                // faders don't have a Material/Udon toggle in the UI, so
                // this is just a heads-up — the corrective action is to
                // delete this fader and re-create it pointed at the Udon
                // variable on the fixture's UdonBehaviour.
                if (!isSkybox)
                    EnigmaActionListDrawer.DrawVRSLFaderWarningIfNeeded(
                        rendererProp.objectReferenceValue as UnityEngine.Renderer);

                // Determine which material to use for property search.
                Material activeMat = null;
                UnityEngine.Renderer renderer = null;

                if (isSkybox)
                {
                    activeMat = RenderSettings.skybox;
                    if (activeMat != null)
                        EditorGUILayout.LabelField("Material", activeMat.name, EditorStyles.miniLabel);
                    else
                        EditorGUILayout.HelpBox("No skybox material assigned in Lighting settings.", MessageType.Warning);
                }
                else
                {
                    bool hasRenderer = rendererProp.objectReferenceValue != null;
                    renderer = rendererProp.objectReferenceValue as UnityEngine.Renderer;

                    if (hasRenderer && renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                    {
                        var mats = renderer.sharedMaterials;
                        if (matIndexProp.intValue < 0 || matIndexProp.intValue >= mats.Length)
                            matIndexProp.intValue = 0;

                        // Only show the Material popup when the renderer has more
                        // than one sub-material — for single-material renderers
                        // there's nothing to choose, so the popup is just clutter.
                        // Matches the extras-row drawer.
                        if (mats.Length > 1)
                        {
                            string[] matNames = new string[mats.Length];
                            for (int mi = 0; mi < mats.Length; mi++)
                                matNames[mi] = mats[mi] != null ? mats[mi].name : $"Slot {mi} (None)";

                            int newMatIdx = EditorGUILayout.Popup("Material", matIndexProp.intValue, matNames);
                            if (newMatIdx != matIndexProp.intValue) matIndexProp.intValue = newMatIdx;
                        }
                        activeMat = mats[matIndexProp.intValue];
                    }
                }

                if (activeMat == null)
                {
                    if (!isSkybox)
                        EditorGUILayout.HelpBox("Assign a Renderer to select a property.", MessageType.Info);
                }
                else
                {
                    if (_actionDrawer == null) _actionDrawer = new EnigmaActionListDrawer(Repaint);

                    // For skybox, pass null renderer with matIndex 0 — DrawPropertyNameField
                    // needs a renderer, so we use a helper approach with the skybox material.
                    string prevName = propNameProp.stringValue;

                    // Collect every material this static fader binds to (primary
                    // + each extra renderer's chosen material). Used both by
                    // the Search tree (to show only SHARED properties) and by
                    // the "property not found on:" warning below.
                    Material[] allMats = ResolveStaticFaderMaterials(index, isSkybox, activeMat);

                    // Draw property name with search over the intersection of
                    // every bound material's shader.
                    EditorGUILayout.BeginHorizontal();
                    propNameProp.stringValue = EditorGUILayout.TextField("Property Name", propNameProp.stringValue);
                    if (GUILayout.Button("Search", GUILayout.Width(60)))
                    {
                        var search = new EnigmaPropertySearch("Shader Properties");
                        var group = search.GetMainGroup();

                        // Shared-overload: tree order follows the primary
                        // shader; entries are filtered to names + types that
                        // exist on EVERY bound material. Faders can't slide
                        // textures, so exclude the Texture type.
                        var typeDict = EnigmaActionListDrawer.PopulateSharedShaderPropertyTree(
                            group,
                            allMats,
                            t => t != UnityEngine.Rendering.ShaderPropertyType.Texture);

                        int capturedIndex = index;
                        search.Open(selected =>
                        {
                            propNameProp.stringValue = selected;
                            int selType = -1;
                            if (typeDict.TryGetValue(selected, out var spt))
                                selType = EnigmaActionListDrawer.ShaderPropertyTypeToActionType(spt);
                            if (selType >= 0)
                            {
                                // Clamp to the FADER convention {0=Float, 1=Color}.
                                // ShaderPropertyTypeToActionType can return 2 (Vector) or
                                // 3 (Texture) — neither is valid for a fader. Texture is
                                // already filtered out by the tree predicate, but Vector
                                // can still reach this callback. Fall back to Float so the
                                // runtime doesn't try to SetFloat/hue-shift on a Vector.
                                if (selType != 1) selType = 0;
                                _so.FindProperty("rtStaticFaderPropertyTypes").GetArrayElementAtIndex(capturedIndex).intValue = selType;
                                if (selType == 1 && activeMat.HasProperty(selected))
                                {
                                    Color seed = activeMat.GetColor(selected);
                                    Debug.Log($"[Enigma] Static fader search seed: entry={capturedIndex} prop={selected} mat={activeMat.name} shader={activeMat.shader.name} GetColor={seed}");
                                    _so.FindProperty("rtStaticFaderDefaultColors").GetArrayElementAtIndex(capturedIndex).colorValue = seed;
                                    _so.FindProperty("rtStaticFaderMaxValues").GetArrayElementAtIndex(capturedIndex).floatValue = 360f;
                                    _so.FindProperty("rtStaticFaderMinValues").GetArrayElementAtIndex(capturedIndex).floatValue = 0f;
                                    _so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(capturedIndex).floatValue = 0f;
                                }
                                else if (activeMat.HasProperty(selected))
                                {
                                    float fseed = activeMat.GetFloat(selected);
                                    Debug.Log($"[Enigma] Static fader search seed: entry={capturedIndex} prop={selected} mat={activeMat.name} shader={activeMat.shader.name} GetFloat={fseed}");
                                    _so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(capturedIndex).floatValue = fseed;
                                }
                            }
                            _so.ApplyModifiedProperties();
                            Repaint();
                        });
                    }
                    EditorGUILayout.EndHorizontal();

                    // Type resolution on manual name change.
                    if (propNameProp.stringValue != prevName && !string.IsNullOrEmpty(propNameProp.stringValue))
                    {
                        int resolved = -1;
                        var shader = activeMat.shader;
                        int propCount = UnityEditor.ShaderUtil.GetPropertyCount(shader);
                        for (int p = 0; p < propCount; p++)
                        {
                            if (UnityEditor.ShaderUtil.GetPropertyName(shader, p) == propNameProp.stringValue)
                            {
                                resolved = EnigmaActionListDrawer.ShaderPropToActionType(
                                    UnityEditor.ShaderUtil.GetPropertyType(shader, p));
                                break;
                            }
                        }
                        if (resolved >= 0)
                            _so.FindProperty("rtStaticFaderPropertyTypes").GetArrayElementAtIndex(index).intValue = resolved;
                    }

                    // Warning for unresolved property names. With multi-renderer
                    // static faders we now have to check against every bound
                    // material, not just the primary; report the materials
                    // that don't have the property so the user can fix them.
                    if (!string.IsNullOrEmpty(propNameProp.stringValue))
                    {
                        var missing = new System.Collections.Generic.List<string>();
                        if (allMats != null)
                        {
                            foreach (var m in allMats)
                            {
                                if (m == null) continue;
                                if (!m.HasProperty(propNameProp.stringValue))
                                    missing.Add(m.name);
                            }
                        }
                        if (missing.Count > 0)
                        {
                            string list = missing.Count <= 3
                                ? string.Join(", ", missing)
                                : string.Join(", ", missing.GetRange(0, 3)) + ", …";
                            EditorGUILayout.HelpBox(
                                $"Property \"{propNameProp.stringValue}\" not found on: {list}",
                                MessageType.Warning);
                        }
                    }
                }
            }

            // Common properties
            EditorGUILayout.Space(4);
            SerializedProperty typeProp = _so.FindProperty("rtStaticFaderPropertyTypes").GetArrayElementAtIndex(index);
            
            bool isSlider = isSliderProp.boolValue;

            if (isSlider && typeProp.intValue != 0)
            {
                typeProp.intValue = 0; // Float for sliders always
            }

            if (typeProp.intValue == 1) // Color
            {
                SerializedProperty colorProp = _so.FindProperty("rtStaticFaderDefaultColors").GetArrayElementAtIndex(index);

                // HDR-enabled color picker — many shader color properties
                // (e.g. _EmissionColor) and Udon color fields (e.g. VRSL's
                // lightColorTint, values like `Color(0.415, 0.438, 3.565, 2)`)
                // have RGB components > 1 or alpha as an intensity channel.
                // The non-HDR ColorField clamps display to 0-1 and the user
                // can't edit values above white. HDR mode shows the
                // base-color + intensity widget Unity uses for emission.
                Color currentColor = colorProp.colorValue;
                Color updatedColor = EditorGUILayout.ColorField(
                    new GUIContent("Default Color"),
                    currentColor,
                    showEyedropper: true,
                    showAlpha: true,
                    hdr: true);
                if (updatedColor != currentColor) colorProp.colorValue = updatedColor;

                Color.RGBToHSV(updatedColor, out float h, out float s, out float v);
                if (s < 0.15f)
                {
                    EditorGUILayout.HelpBox(
                        "Warning: This color has low saturation (greyscale). Hue shifting will have minimal effect on greyscale colors.",
                        MessageType.Warning);
                }

                SerializedProperty maxValProp = _so.FindProperty("rtStaticFaderMaxValues").GetArrayElementAtIndex(index);
                float maxShift = maxValProp.floatValue;
                maxShift = Mathf.Clamp(maxShift, 0f, 360f);
                
                float updatedMaxShift = EditorGUILayout.Slider(
                    new GUIContent("Max Shift (degrees)", "Maximum hue shift in degrees. 360 = full color wheel rotation."),
                    maxShift,
                    0f,
                    360f);
                    
                if (!Mathf.Approximately(updatedMaxShift, maxValProp.floatValue))
                {
                    _so.FindProperty("rtStaticFaderMinValues").GetArrayElementAtIndex(index).floatValue = 0f;
                    _so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(index).floatValue = 0f;
                    maxValProp.floatValue = updatedMaxShift;
                }
            }
            else
            {
                SPFloat(_so.FindProperty("rtStaticFaderMinValues").GetArrayElementAtIndex(index), "Min Value");
                SPFloat(_so.FindProperty("rtStaticFaderMaxValues").GetArrayElementAtIndex(index), "Max Value");
                SPFloat(_so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(index), "Default Value");
            }

            EditorGUILayout.Space(2);
            // Indicator Ring is always on. Color-property faders derive their
            // ring colour from the live hue-shifted material colour, so the
            // Indicator Color field is hidden for those; float/range faders
            // keep the freely-picked indicator colour (default white).
            // The rtStaticFaderIndicatorEnabled array stays in the data model
            // for forward/backward compat but is forced true at build time.
            SerializedProperty indEnabled = _so.FindProperty("rtStaticFaderIndicatorEnabled").GetArrayElementAtIndex(index);
            if (!indEnabled.boolValue) indEnabled.boolValue = true;
            if (typeProp.intValue != 1)
                SPColor(_so.FindProperty("rtStaticFaderIndicatorColors").GetArrayElementAtIndex(index), "Indicator Color");

            // "Show Only When Grabbed" was removed from the UI. The field
            // rtStaticFaderIndicatorConditional stays in the data model (read
            // at runtime by EnigmaFader.UpdateIndicatorColor) but is force-set
            // to false here so that any legacy true values are cleared on
            // first inspector repaint.
            SerializedProperty indCondProp = _so.FindProperty("rtStaticFaderIndicatorConditional").GetArrayElementAtIndex(index);
            if (indCondProp.boolValue) indCondProp.boolValue = false;

            EditorGUI.indentLevel--;
        }

        // Editor-side equivalent of EnigmaController.CollectStaticFaderMaterials.
        // Reads from SerializedProperty (pre-Build state) rather than the
        // runtime rt* arrays so the Search tree reflects what the user
        // currently has assigned in the inspector. Returns the primary
        // material first, followed by every non-null extra renderer's
        // chosen sub-material. Skybox faders return just the skybox
        // material — skybox mode has no extras.
        private Material[] ResolveStaticFaderMaterials(int entryIdx, bool isSkybox, Material primaryMat)
        {
            if (isSkybox)
            {
                var sky = RenderSettings.skybox;
                return sky != null ? new[] { sky } : new Material[0];
            }

            var list = new System.Collections.Generic.List<Material>();
            if (primaryMat != null) list.Add(primaryMat);

            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            if (countArr != null && rArr != null && mArr != null
                && entryIdx >= 0 && entryIdx < countArr.arraySize)
            {
                int start = ComputeExtraStart(entryIdx);
                int count = countArr.GetArrayElementAtIndex(entryIdx).intValue;
                for (int e = 0; e < count; e++)
                {
                    int flat = start + e;
                    if (flat >= rArr.arraySize || flat >= mArr.arraySize) break;
                    var rend = rArr.GetArrayElementAtIndex(flat).objectReferenceValue as UnityEngine.Renderer;
                    if (rend == null) continue;
                    var mats = rend.sharedMaterials;
                    int mi = mArr.GetArrayElementAtIndex(flat).intValue;
                    if (mi < 0 || mi >= mats.Length || mats[mi] == null) continue;
                    list.Add(mats[mi]);
                }
            }
            return list.ToArray();
        }

        private static readonly string[] kStaticFaderArrayNames = {
            "rtStaticFaderNames", "rtStaticFaderRenderers", "rtStaticFaderMaterialIndices",
            "rtStaticFaderPropertyNames", "rtStaticFaderPropertyTypes",
            "rtStaticFaderMinValues", "rtStaticFaderMaxValues", "rtStaticFaderDefaultValues",
            "rtStaticFaderDefaultColors", "rtStaticFaderIndicatorEnabled",
            "rtStaticFaderIndicatorColors", "rtStaticFaderIndicatorConditional",
            "rtStaticFaderTargetsUdon", "rtStaticFaderUdonBehaviours", "rtStaticFaderUdonVariableNames",
            "rtStaticFaderTargetsSlider", "rtStaticFaderSliders", "rtStaticFaderSliderReversed",
            "rtStaticFaderAlwaysVisible", "rtStaticFaderTargetsSkybox",
            // Per-entry count of extra renderers. The flat arrays
            // rtStaticFaderExtraRenderers/MaterialIndices are managed separately
            // in MoveStaticFader/RemoveStaticFaderAtIndex so the flat block for a
            // moved/removed entry gets moved/removed in lockstep with the count.
            "rtStaticFaderExtraCount",
            // Symmetric per-entry count for extra Udon behaviours.
            // rtStaticFaderExtraUdonBehaviours is similarly moved/removed in
            // lockstep below.
            "rtStaticFaderExtraUdonCount"
        };

        // Compute the flat-array start index for entry `idx`'s extras.
        // Prefix sum of rtStaticFaderExtraCount[0..idx-1].
        private int ComputeExtraStart(int idx)
        {
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            int start = 0;
            if (countArr == null) return 0;
            int clamped = Mathf.Min(idx, countArr.arraySize);
            for (int i = 0; i < clamped; i++)
                start += countArr.GetArrayElementAtIndex(i).intValue;
            return start;
        }

        // Append a new extra renderer row at the end of entry `entryIdx`'s block.
        private void AddExtraStaticFaderRenderer(int entryIdx)
        {
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            if (rArr == null || mArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start   = ComputeExtraStart(entryIdx);
            int count   = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            int insert  = start + count;
            rArr.InsertArrayElementAtIndex(insert);
            rArr.GetArrayElementAtIndex(insert).objectReferenceValue = null;
            mArr.InsertArrayElementAtIndex(insert);
            mArr.GetArrayElementAtIndex(insert).intValue = 0;
            countArr.GetArrayElementAtIndex(entryIdx).intValue = count + 1;
            _so.ApplyModifiedProperties();
        }

        // Remove the extra renderer at row `extraRow` (0-based, within the entry)
        // from entry `entryIdx`.
        private void RemoveExtraStaticFaderRenderer(int entryIdx, int extraRow)
        {
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            if (rArr == null || mArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start = ComputeExtraStart(entryIdx);
            int count = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            if (extraRow < 0 || extraRow >= count) return;

            int removeAt = start + extraRow;
            // DeleteArrayElementAtIndex on an object reference array nulls it out
            // on the first call rather than removing. Call twice as documented by
            // Unity's SerializedProperty API.
            if (rArr.GetArrayElementAtIndex(removeAt).objectReferenceValue != null)
                rArr.DeleteArrayElementAtIndex(removeAt);
            rArr.DeleteArrayElementAtIndex(removeAt);
            mArr.DeleteArrayElementAtIndex(removeAt);
            countArr.GetArrayElementAtIndex(entryIdx).intValue = count - 1;
            _so.ApplyModifiedProperties();
        }

        // Draw the extra-renderer rows for static fader entry `entryIdx`.
        // Each row is a Renderer field + "-" (remove) button, followed by a
        // Material popup (when multiple sub-materials exist on the renderer).
        private void DrawExtraStaticFaderRenderers(int entryIdx)
        {
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            if (rArr == null || mArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start = ComputeExtraStart(entryIdx);
            int count = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            for (int e = 0; e < count; e++)
            {
                int flatIdx = start + e;
                if (flatIdx >= rArr.arraySize || flatIdx >= mArr.arraySize) break;
                var extraRenderer = rArr.GetArrayElementAtIndex(flatIdx);
                var extraMatIdx   = mArr.GetArrayElementAtIndex(flatIdx);

                EditorGUILayout.BeginHorizontal();
                SPObject<UnityEngine.Renderer>(extraRenderer, "Renderer");
                bool removeClicked = GUILayout.Button("-", GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();

                if (removeClicked)
                {
                    RemoveExtraStaticFaderRenderer(entryIdx, e);
                    return; // Layout changed; stop drawing this frame.
                }

                var extraRend = extraRenderer.objectReferenceValue as UnityEngine.Renderer;
                if (extraRend != null && extraRend.sharedMaterials != null && extraRend.sharedMaterials.Length > 1)
                {
                    var mats = extraRend.sharedMaterials;
                    if (extraMatIdx.intValue < 0 || extraMatIdx.intValue >= mats.Length)
                        extraMatIdx.intValue = 0;
                    string[] matNames = new string[mats.Length];
                    for (int mi = 0; mi < mats.Length; mi++)
                        matNames[mi] = mats[mi] != null ? mats[mi].name : $"Slot {mi} (None)";
                    int newMatIdx = EditorGUILayout.Popup("Material", extraMatIdx.intValue, matNames);
                    if (newMatIdx != extraMatIdx.intValue) extraMatIdx.intValue = newMatIdx;
                }
            }
        }

        // Compute the flat-array start index for entry `idx`'s UDON extras.
        // Symmetric with ComputeExtraStart for renderer extras but counts
        // through rtStaticFaderExtraUdonCount.
        private int ComputeExtraUdonStart(int idx)
        {
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (countArr == null) return 0;
            int start = 0;
            int clamped = Mathf.Min(idx, countArr.arraySize);
            for (int i = 0; i < clamped; i++) start += countArr.GetArrayElementAtIndex(i).intValue;
            return start;
        }

        // Append a new extra Udon behaviour row at the end of entry
        // `entryIdx`'s block. Mirrors AddExtraStaticFaderRenderer.
        private void AddExtraStaticFaderUdon(int entryIdx)
        {
            var bArr     = _so.FindProperty("rtStaticFaderExtraUdonBehaviours");
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (bArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start  = ComputeExtraUdonStart(entryIdx);
            int count  = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            int insert = start + count;
            bArr.InsertArrayElementAtIndex(insert);
            bArr.GetArrayElementAtIndex(insert).objectReferenceValue = null;
            countArr.GetArrayElementAtIndex(entryIdx).intValue = count + 1;
            _so.ApplyModifiedProperties();
        }

        private void RemoveExtraStaticFaderUdon(int entryIdx, int extraRow)
        {
            var bArr     = _so.FindProperty("rtStaticFaderExtraUdonBehaviours");
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (bArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start = ComputeExtraUdonStart(entryIdx);
            int count = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            if (extraRow < 0 || extraRow >= count) return;

            int removeAt = start + extraRow;
            // Object-reference arrays need the Unity two-call delete pattern —
            // first DeleteArrayElementAtIndex nulls the reference, second
            // call actually shrinks the array. Same dance the renderer
            // extras remover uses.
            if (bArr.GetArrayElementAtIndex(removeAt).objectReferenceValue != null)
                bArr.DeleteArrayElementAtIndex(removeAt);
            bArr.DeleteArrayElementAtIndex(removeAt);
            countArr.GetArrayElementAtIndex(entryIdx).intValue = count - 1;
            _so.ApplyModifiedProperties();
        }

        // Draw each extra Udon behaviour row for entry `entryIdx`. Accepts
        // any Object drop and resolves to UdonSharpBehaviour — mirrors the
        // primary Udon Behaviour field's handling so drops work the same
        // way on extra rows.
        private void DrawExtraStaticFaderUdon(int entryIdx)
        {
            var bArr     = _so.FindProperty("rtStaticFaderExtraUdonBehaviours");
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (bArr == null || countArr == null) return;
            if (entryIdx < 0 || entryIdx >= countArr.arraySize) return;

            int start = ComputeExtraUdonStart(entryIdx);
            int count = countArr.GetArrayElementAtIndex(entryIdx).intValue;
            for (int e = 0; e < count; e++)
            {
                int flatIdx = start + e;
                if (flatIdx >= bArr.arraySize) break;
                var behProp = bArr.GetArrayElementAtIndex(flatIdx);

                EditorGUILayout.BeginHorizontal();
                var currentRef = behProp.objectReferenceValue;
                var picked = EditorGUILayout.ObjectField("Behaviour", currentRef, typeof(UnityEngine.Object), true);
                if (picked != currentRef)
                {
                    UdonSharpBehaviour resolved = ResolveUdonSharpBehaviour(picked);
                    behProp.objectReferenceValue = resolved;
                    if (picked != null && resolved == null)
                    {
                        Debug.LogWarning(
                            $"[EnigmaOS] '{picked.name}' has no UdonSharpBehaviour component — " +
                            "extra Behaviour field cleared.");
                    }
                }
                bool removeClicked = GUILayout.Button("-", GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();

                if (removeClicked)
                {
                    RemoveExtraStaticFaderUdon(entryIdx, e);
                    return; // layout changed; stop drawing this frame
                }
            }
        }

        private void MoveStaticFader(int fromIndex, int toIndex)
        {
            // Move the flat extras blocks for this entry in lockstep with the
            // per-entry count arrays (which are in kStaticFaderArrayNames and
            // move via the generic loop below). Extras must be moved
            // separately because they're stored as a flat concatenation
            // across entries. Do the UDON extras and the RENDERER extras.
            MoveExtraRenderersForEntry(fromIndex, toIndex);
            MoveExtraUdonForEntry(fromIndex, toIndex);

            foreach (string name in kStaticFaderArrayNames)
            {
                SerializedProperty arr = _so.FindProperty(name);
                if (arr != null && fromIndex < arr.arraySize && toIndex < arr.arraySize)
                    arr.MoveArrayElement(fromIndex, toIndex);
            }
            _collapsedStaticFaders.Clear();
            _so.ApplyModifiedProperties();
        }

        private void RemoveStaticFaderAtIndex(int index)
        {
            // Remove the flat extras blocks for this entry before removing the
            // count entries (otherwise ComputeExtraStart/ComputeExtraUdonStart
            // no longer line up).
            RemoveExtraRenderersForEntry(index);
            RemoveExtraUdonForEntry(index);

            foreach (string name in kStaticFaderArrayNames)
            {
                SerializedProperty arr = _so.FindProperty(name);
                if (arr != null && index < arr.arraySize)
                    arr.DeleteArrayElementAtIndex(index);
            }
            _collapsedStaticFaders.Clear();
        }

        // Move the block of extra renderers/material-indices for entry `from`
        // to the position of entry `to`. This mirrors the per-entry count
        // element move so extras stay attached to their owning entry.
        //
        // Called BEFORE the count array itself is moved, so ComputeExtraStart
        // uses the pre-move state.
        private void MoveExtraRenderersForEntry(int fromIdx, int toIdx)
        {
            if (fromIdx == toIdx) return;
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            if (rArr == null || mArr == null || countArr == null) return;
            if (fromIdx < 0 || fromIdx >= countArr.arraySize) return;
            if (toIdx   < 0 || toIdx   >= countArr.arraySize) return;

            int fromStart = ComputeExtraStart(fromIdx);
            int fromCount = countArr.GetArrayElementAtIndex(fromIdx).intValue;
            if (fromCount <= 0) return; // nothing to move

            // Post-move start position for the block. Derived from the pre-move
            // count array, accounting for the fact that other entries slide
            // into the space vacated by the moving entry.
            //
            // Forward move (fromIdx < toIdx): the entries between (fromIdx,toIdx]
            // each shift one position left, so their counts now come before our
            // block. Post start = sum of pre counts[0..toIdx] minus the moving
            // block itself (which is counted in that sum).
            //
            // Backward move (fromIdx > toIdx): no entry before toIdx is affected,
            // so post start equals ComputeExtraStart(toIdx) in the pre state.
            int toStart;
            if (fromIdx < toIdx)
            {
                int preSumThroughTo = 0;
                for (int i = 0; i <= toIdx && i < countArr.arraySize; i++)
                    preSumThroughTo += countArr.GetArrayElementAtIndex(i).intValue;
                toStart = preSumThroughTo - fromCount;
            }
            else
            {
                toStart = ComputeExtraStart(toIdx);
            }

            // Move the block one element at a time. Direction matters so that
            // earlier moves don't shift the remaining source elements into the
            // wrong position.
            //   Forward (fromStart < toStart): iterate high-to-low, so each
            //     element's source index stays valid as prior moves pull the
            //     tail of the block rightward.
            //   Backward (fromStart > toStart): iterate low-to-high for the
            //     symmetric reason.
            if (fromIdx < toIdx)
            {
                for (int i = fromCount - 1; i >= 0; i--)
                {
                    int src = fromStart + i;
                    int dst = toStart + i;
                    rArr.MoveArrayElement(src, dst);
                    mArr.MoveArrayElement(src, dst);
                }
            }
            else
            {
                for (int i = 0; i < fromCount; i++)
                {
                    int src = fromStart + i;
                    int dst = toStart + i;
                    rArr.MoveArrayElement(src, dst);
                    mArr.MoveArrayElement(src, dst);
                }
            }
        }

        // Remove the block of extras for entry `idx` from the flat arrays.
        private void RemoveExtraRenderersForEntry(int idx)
        {
            var rArr     = _so.FindProperty("rtStaticFaderExtraRenderers");
            var mArr     = _so.FindProperty("rtStaticFaderExtraMaterialIndices");
            var countArr = _so.FindProperty("rtStaticFaderExtraCount");
            if (rArr == null || mArr == null || countArr == null) return;
            if (idx < 0 || idx >= countArr.arraySize) return;

            int start = ComputeExtraStart(idx);
            int count = countArr.GetArrayElementAtIndex(idx).intValue;
            for (int i = count - 1; i >= 0; i--)
            {
                int removeAt = start + i;
                if (removeAt >= rArr.arraySize) continue;
                if (rArr.GetArrayElementAtIndex(removeAt).objectReferenceValue != null)
                    rArr.DeleteArrayElementAtIndex(removeAt);
                rArr.DeleteArrayElementAtIndex(removeAt);
                if (removeAt < mArr.arraySize)
                    mArr.DeleteArrayElementAtIndex(removeAt);
            }
        }

        // Symmetric move-block helper for UDON extras. Same algorithm as
        // MoveExtraRenderersForEntry — see that method's comments for why
        // the high-to-low (forward move) / low-to-high (backward move)
        // iteration order matters.
        private void MoveExtraUdonForEntry(int fromIdx, int toIdx)
        {
            if (fromIdx == toIdx) return;
            var bArr     = _so.FindProperty("rtStaticFaderExtraUdonBehaviours");
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (bArr == null || countArr == null) return;
            if (fromIdx < 0 || fromIdx >= countArr.arraySize) return;
            if (toIdx   < 0 || toIdx   >= countArr.arraySize) return;

            int fromStart = ComputeExtraUdonStart(fromIdx);
            int fromCount = countArr.GetArrayElementAtIndex(fromIdx).intValue;
            if (fromCount <= 0) return;

            int toStart;
            if (fromIdx < toIdx)
            {
                int preSumThroughTo = 0;
                for (int i = 0; i <= toIdx && i < countArr.arraySize; i++)
                    preSumThroughTo += countArr.GetArrayElementAtIndex(i).intValue;
                toStart = preSumThroughTo - fromCount;
            }
            else
            {
                toStart = ComputeExtraUdonStart(toIdx);
            }

            if (fromIdx < toIdx)
            {
                for (int i = fromCount - 1; i >= 0; i--)
                {
                    int src = fromStart + i;
                    int dst = toStart + i;
                    bArr.MoveArrayElement(src, dst);
                }
            }
            else
            {
                for (int i = 0; i < fromCount; i++)
                {
                    int src = fromStart + i;
                    int dst = toStart + i;
                    bArr.MoveArrayElement(src, dst);
                }
            }
        }

        // Symmetric remove-block helper for UDON extras.
        private void RemoveExtraUdonForEntry(int idx)
        {
            var bArr     = _so.FindProperty("rtStaticFaderExtraUdonBehaviours");
            var countArr = _so.FindProperty("rtStaticFaderExtraUdonCount");
            if (bArr == null || countArr == null) return;
            if (idx < 0 || idx >= countArr.arraySize) return;

            int start = ComputeExtraUdonStart(idx);
            int count = countArr.GetArrayElementAtIndex(idx).intValue;
            for (int i = count - 1; i >= 0; i--)
            {
                int removeAt = start + i;
                if (removeAt >= bArr.arraySize) continue;
                if (bArr.GetArrayElementAtIndex(removeAt).objectReferenceValue != null)
                    bArr.DeleteArrayElementAtIndex(removeAt);
                bArr.DeleteArrayElementAtIndex(removeAt);
            }
        }

        private void ResizeStaticFaderArrays(int size)
        {
            EnsureArraySize(_so.FindProperty("rtStaticFaderNames"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderRenderers"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderMaterialIndices"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderPropertyNames"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderPropertyTypes"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderMinValues"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderMaxValues"), size, 1f); // default to 1f for new elements
            EnsureArraySize(_so.FindProperty("rtStaticFaderDefaultValues"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderDefaultColors"), size, Color.white);
            EnsureArraySize(_so.FindProperty("rtStaticFaderIndicatorEnabled"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderIndicatorColors"), size, Color.white);
            EnsureArraySize(_so.FindProperty("rtStaticFaderIndicatorConditional"), size);

            EnsureArraySize(_so.FindProperty("rtStaticFaderTargetsUdon"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderUdonBehaviours"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderUdonVariableNames"), size);

            EnsureArraySize(_so.FindProperty("rtStaticFaderTargetsSlider"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderSliders"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderSliderReversed"), size);
            EnsureArraySize(_so.FindProperty("rtStaticFaderAlwaysVisible"), size, false);
            EnsureArraySize(_so.FindProperty("rtStaticFaderTargetsSkybox"), size, false);

            // Per-entry extra-renderer count. The flat rtStaticFaderExtraRenderers
            // and rtStaticFaderExtraMaterialIndices arrays are NOT resized here —
            // they're managed by AddExtraStaticFaderRenderer and the move/remove
            // helpers so that adding a brand-new static fader slot doesn't
            // accidentally allocate phantom renderer slots.
            EnsureArraySize(_so.FindProperty("rtStaticFaderExtraCount"), size);
            // Same rationale for UDON extras — the flat
            // rtStaticFaderExtraUdonBehaviours is managed by AddExtraStaticFaderUdon
            // and the move/remove helpers, so we only grow the per-entry
            // count here.
            EnsureArraySize(_so.FindProperty("rtStaticFaderExtraUdonCount"), size);
        }

        private void EnsureArraySize(SerializedProperty arrayProp, int size, object defaultValue = null)
        {
            if (arrayProp == null) return;

            int oldSize = arrayProp.arraySize;
            if (oldSize >= size) return;
            arrayProp.arraySize = size;
            
            if (size > oldSize && defaultValue != null)
            {
                for (int i = oldSize; i < size; i++)
                {
                    SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
                    if (defaultValue is float f)
                        element.floatValue = f;
                    else if (defaultValue is Color c)
                        element.colorValue = c;
                    else if (defaultValue is int integer)
                        element.intValue = integer;
                    else if (defaultValue is bool b)
                        element.boolValue = b;
                    else if (defaultValue is string str)
                        element.stringValue = str;
                }
            }
        }

        // `proxy` is the UdonSharpBehaviour component the user dropped into
        // the Behaviour field (or null for raw UdonBehaviours that aren't
        // UdonSharp-backed). Reading values via SerializedObject(proxy)
        // reflects the field's live C# default — which is what the user
        // sees in the inspector. UdonBehaviour.publicVariables is the
        // serialization-cache that UdonSharp pushes into at Build time; it
        // lags behind C# edits until a re-sync.
        private void AutofillStaticFaderUdonValues(int index, UdonBehaviour udon, MonoBehaviour proxy, string varName)
        {
            if (udon == null || string.IsNullOrEmpty(varName)) return;

            System.Type varType = GetUdonVariableType(udon, varName);
            if (varType == null) return;

            SerializedProperty tProp = _so.FindProperty("rtStaticFaderPropertyTypes").GetArrayElementAtIndex(index);

            if (varType == typeof(Color) || varType == typeof(Color32))
            {
                // rtStaticFaderPropertyTypes convention: 0=Float, 1=Color
                // (matches EnigmaFader.ApplyValueToMaterials' `propertyType == 1`
                // Color branch). This used to write 2 — which isn't a valid
                // static-fader type and fell through to the Float branch at
                // runtime, so Udon Color variables silently got treated as
                // floats.
                tProp.intValue = 1; // Color

                if (TryGetUdonColorValue(udon, proxy, varName, out Color colorVal))
                {
                    _so.FindProperty("rtStaticFaderDefaultColors").GetArrayElementAtIndex(index).colorValue = colorVal;
                    // Seed the hue-shift range to the full 360° wheel — same
                    // default the shader-property search uses for colors.
                    // Without this, a color fader has min=0 max=1 default=0,
                    // which gives a 1° shift and looks like nothing happens.
                    _so.FindProperty("rtStaticFaderMaxValues").GetArrayElementAtIndex(index).floatValue = 360f;
                    _so.FindProperty("rtStaticFaderMinValues").GetArrayElementAtIndex(index).floatValue = 0f;
                    _so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(index).floatValue = 0f;
                }
            }
            else
            {
                tProp.intValue = 0; // Float

                if (TryGetUdonVariableValues(udon, proxy, varName, out float defVal, out float minVal, out float maxVal))
                {
                    _so.FindProperty("rtStaticFaderMinValues").GetArrayElementAtIndex(index).floatValue = minVal;
                    _so.FindProperty("rtStaticFaderMaxValues").GetArrayElementAtIndex(index).floatValue = maxVal;
                    _so.FindProperty("rtStaticFaderDefaultValues").GetArrayElementAtIndex(index).floatValue = defVal;
                }
            }
        }

        private System.Type GetUdonVariableType(UdonBehaviour udon, string variableName)
        {
            if (udon == null || udon.programSource == null || string.IsNullOrEmpty(variableName)) return null;

            if (udon.programSource is UdonSharpProgramAsset udonSharpProgramAsset)
            {
                if (udonSharpProgramAsset.fieldDefinitions != null &&
                    udonSharpProgramAsset.fieldDefinitions.TryGetValue(variableName, out var fieldDef))
                {
                    return fieldDef.SystemType;
                }
            }
            else if (udon.publicVariables != null)
            {
                if (udon.publicVariables.TryGetVariableType(variableName, out System.Type varType))
                {
                    return varType;
                }
            }
            
            return null;
        }

        private bool TryGetUdonColorValue(UdonBehaviour udon, MonoBehaviour proxy, string variableName, out Color outColor)
        {
            outColor = Color.white;

            // Prefer the UdonSharp proxy's SerializedObject — it reflects
            // the C# field's live value (script default OR user override in
            // the Udon inspector), which is what the user sees and what
            // UdonSharp will eventually push into publicVariables. Falling
            // back to publicVariables would return a stale cached value
            // whenever the user has edited the C# default without a re-sync.
            if (proxy != null)
            {
                using (var so = new SerializedObject(proxy))
                {
                    var sp = so.FindProperty(variableName);
                    if (sp != null && sp.propertyType == SerializedPropertyType.Color)
                    {
                        outColor = sp.colorValue;
                        return true;
                    }
                }
            }

            if (udon != null && udon.publicVariables != null
                && udon.publicVariables.TryGetVariableValue(variableName, out object currentValue))
            {
                if (currentValue is Color c) { outColor = c; return true; }
                if (currentValue is Color32 c32) { outColor = c32; return true; }
            }
            return false;
        }

        private bool TryGetUdonVariableValues(UdonBehaviour udon, MonoBehaviour proxy, string variableName, out float defaultValue, out float minValue, out float maxValue)
        {
            defaultValue = 0f;
            minValue = 0f;
            maxValue = 1f;

            if (udon == null || udon.programSource == null || string.IsNullOrEmpty(variableName))
                return false;

            try
            {
                // Same proxy-first strategy as TryGetUdonColorValue.
                bool gotDefault = false;
                if (proxy != null)
                {
                    using (var so = new SerializedObject(proxy))
                    {
                        var sp = so.FindProperty(variableName);
                        if (sp != null)
                        {
                            switch (sp.propertyType)
                            {
                                case SerializedPropertyType.Float:
                                    defaultValue = sp.floatValue;
                                    gotDefault = true;
                                    break;
                                case SerializedPropertyType.Integer:
                                    defaultValue = sp.intValue;
                                    gotDefault = true;
                                    break;
                            }
                        }
                    }
                }
                if (!gotDefault
                    && udon.publicVariables != null
                    && udon.publicVariables.TryGetVariableValue(variableName, out object currentValue))
                {
                    if (currentValue is float floatVal) defaultValue = floatVal;
                    else if (currentValue is int intVal) defaultValue = intVal;
                    else if (currentValue is double doubleVal) defaultValue = (float)doubleVal;
                }

                if (udon.programSource is UdonSharpProgramAsset udonSharpProgramAsset)
                {
                    if (udonSharpProgramAsset.fieldDefinitions != null &&
                        udonSharpProgramAsset.fieldDefinitions.TryGetValue(variableName, out var fieldDef))
                    {
                        var rangeAttr = fieldDef.GetAttribute<RangeAttribute>();
                        if (rangeAttr != null)
                        {
                            minValue = rangeAttr.min;
                            maxValue = rangeAttr.max;
                            return true;
                        }
                    }
                }

                if (defaultValue < minValue) minValue = defaultValue;
                if (defaultValue > maxValue)
                {
                    if (defaultValue >= 0) maxValue = defaultValue * 2f;
                    else maxValue = defaultValue;
                }
                if (maxValue <= minValue) maxValue = minValue + 1f;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryBuildUdonVariableOptions(List<UdonBehaviour> udonTargets, out List<string> variableNames, out string warning)
        {
            variableNames = new List<string>();
            warning = null;

            if (udonTargets == null || udonTargets.Count == 0)
            {
                warning = "No Udon Behaviors available.";
                return false;
            }

            HashSet<string> commonVariables = null;

            foreach (UdonBehaviour udon in udonTargets)
            {
                if (udon == null) continue;

                HashSet<string> udonVariables = GetUdonVariables(udon);
                
                if (commonVariables == null) commonVariables = udonVariables;
                else commonVariables.IntersectWith(udonVariables);
            }

            if (commonVariables == null || commonVariables.Count == 0)
            {
                warning = "No common public float/color variables found.";
                return false;
            }

            variableNames = commonVariables.OrderBy(v => v).ToList();
            return true;
        }

        private HashSet<string> GetUdonVariables(UdonBehaviour udon)
        {
            HashSet<string> variables = new HashSet<string>();
            if (udon == null || udon.programSource == null) return variables;

            try
            {
                if (udon.programSource is UdonSharpProgramAsset udonSharpProgramAsset)
                {
                    if (udonSharpProgramAsset.fieldDefinitions != null)
                    {
                        foreach (var fieldDef in udonSharpProgramAsset.fieldDefinitions.Values)
                        {
                            var t = fieldDef.SystemType;
                            if (t == typeof(float) || t == typeof(int) || t == typeof(double) || t == typeof(Color) || t == typeof(Color32))
                            {
                                variables.Add(fieldDef.Name);
                            }
                        }
                    }
                }
                else
                {
                    var publicVariables = udon.publicVariables;
                    if (publicVariables != null)
                    {
                        foreach (string symbolName in publicVariables.VariableSymbols)
                        {
                            if (publicVariables.TryGetVariableType(symbolName, out System.Type t))
                            {
                                if (t == typeof(float) || t == typeof(int) || t == typeof(double) || t == typeof(Color) || t == typeof(Color32))
                                {
                                    variables.Add(symbolName);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return variables;
        }

        private void OpenUdonVariableSearchWindow(List<string> variableNames, System.Action<string> onSelect)
        {
            var searchWindow = new EnigmaPropertySearch("Udon Variables");
            var mainGroup = searchWindow.GetMainGroup();
            foreach (string varName in variableNames)
            {
                mainGroup.Add(varName, varName);
            }
            searchWindow.Open(onSelect);
        }

    }
}
#endif

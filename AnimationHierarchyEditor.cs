#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

using UnityObject = UnityEngine.Object;

public class AnimationHierarchyEditor : EditorWindow {
    const int columnWidth = 300;
    static GUIStyle lockIcon;
    Animator animatorObject;
    readonly HashSet<RuntimeAnimatorController> animatorControllers = new HashSet<RuntimeAnimatorController>();
    readonly Dictionary<AnimationClip, AnimationClip> animationClips = new Dictionary<AnimationClip, AnimationClip>();
    readonly Dictionary<string, HashSet<EditorCurveBinding>> paths = new Dictionary<string, HashSet<EditorCurveBinding>>(StringComparer.Ordinal);
    readonly Dictionary<string, bool> state = new Dictionary<string, bool>();
    readonly Dictionary<string, GameObject> objectCache = new Dictionary<string, GameObject>();
    readonly Dictionary<string, string> tempPathOverrides = new Dictionary<string, string>();
    readonly Dictionary<Type, int> tempTypes = new Dictionary<Type, int>();
    AnimationClip[] clipsArray;
    string[] pathsArray;
    Vector2 scrollPos, scrollPos2;
    GUIContent tempContent;
    bool locked, onlyShowMissing;
    string sOriginalRoot = "Root";
    string sNewRoot = "SomeNewObject/Root";

    [MenuItem("Window/Animation Hierarchy Editor")]
    static void ShowWindow() => GetWindow<AnimationHierarchyEditor>();

    void OnEnable() {
        OnSelectionChange();
        titleContent = new GUIContent(EditorGUIUtility.IconContent("AnimationClip Icon")) {
            text = "Animation Hierarchy Editor",
        };
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    void OnDisable() {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    void OnSelectionChange() {
        if (locked) return;
        objectCache.Clear();
        animationClips.Clear();
        animatorControllers.Clear();
        Animator defaultAnimator = null;
        foreach (var obj in Selection.gameObjects) {
            var animator = obj.GetComponentInChildren<Animator>(true);
            if (animator == null) continue;
            if (defaultAnimator == null) defaultAnimator = animator;
            AddClips(animator);
            break;
        }
        foreach (var obj in Selection.objects) {
            if (obj is Animator animator)
                AddClips(animator);
            else if (obj is AnimatorController controller)
                AddClips(controller);
            else if (obj is AnimatorOverrideController overrideController)
                AddClips(overrideController);
            else if (obj is AnimationClip clip)
                AddClips(clip);
        }
        if (animatorObject == null) animatorObject = defaultAnimator;
        if (animationClips.Count > 0) FillModel();
        Repaint();
    }

    void AddClips(Animator animator) {
        if (animator == null) return;
        var baseController = animator.runtimeAnimatorController;
        if (baseController == null) return;
        if (animatorObject == null) animatorObject = animator;
        if (baseController is AnimatorController controller)
            AddClips(controller);
        else if (baseController is AnimatorOverrideController overrideController)
            AddClips(overrideController);
    }

    void AddClips(AnimatorOverrideController overrideController) {
        if (overrideController == null) return;
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        while (overrideController != null) {
            animatorControllers.Add(overrideController);
            overrideController.GetOverrides(overrides);
            foreach (var pair in overrides) AddClips(pair.Value);
            var baseController = overrideController.runtimeAnimatorController;
            if (baseController is AnimatorOverrideController chainedOverride) {
                overrideController = chainedOverride;
                continue;
            } else if (baseController is AnimatorController controller)
                AddClips(controller);
            break;
        };
    }

    void AddClips(AnimatorController animatorController) {
        if (animatorController == null) return;
        animatorControllers.Add(animatorController);
        foreach (var layer in animatorController.layers)
            foreach (var state in layer.stateMachine.states)
                if (state.state.motion is AnimationClip clip)
                    AddClips(clip);
    }

    void AddClips(AnimationClip clip) {
        if (clip == null || animationClips.ContainsKey(clip)) return;
        animationClips.Add(clip, null);
    }

    void OnGUI() {
        if (Event.current.type == EventType.ValidateCommand)
            switch (Event.current.commandName) {
                case "UndoRedoPerformed":
                    FillModel();
                    break;
            }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                animatorObject = EditorGUILayout.ObjectField("Root Animator", animatorObject, typeof(Animator), true, GUILayout.Width(columnWidth * 2)) as Animator;
                if (changed.changed) OnHierarchyChanged();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save Animation Clips", EditorStyles.toolbarButton)) SaveModifiedAssets();
            if (GUILayout.Button("Save Clones", EditorStyles.toolbarButton)) SaveModifiedAssets(true);
        }

        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(EditorGUIUtility.singleLineHeight * 5))) {
            scrollPos = scrollView.scrollPosition;
            GUILayout.Label("Selected Animation Clips", EditorStyles.boldLabel, GUILayout.Width(columnWidth));
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(true)) {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth)))
                    foreach (var animationClip in animatorControllers)
                        EditorGUILayout.ObjectField(animationClip, typeof(RuntimeAnimatorController), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth)))
                    foreach (var animationClip in animationClips.Keys)
                        EditorGUILayout.ObjectField(animationClip, typeof(AnimationClip), true);
            }
        }

        using (new EditorGUI.DisabledScope(animationClips.Count == 0)) {
            using (new EditorGUILayout.HorizontalScope()) {
                sOriginalRoot = EditorGUILayout.TextField(sOriginalRoot, GUILayout.ExpandWidth(true));
                sNewRoot = EditorGUILayout.TextField(sNewRoot, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Replace Root", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                    Debug.Log("O: " + sOriginalRoot + " N: " + sNewRoot);
                    ReplaceRoot(sOriginalRoot, sNewRoot);
                }
            }
        }

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope()) {
            GUILayout.Label("Bindings", EditorStyles.boldLabel, GUILayout.Width(60));
            GUILayout.Label("Animated Object", EditorStyles.boldLabel, GUILayout.Width(columnWidth));
            GUILayout.Label("Reference Path", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(animatorObject == null))
                onlyShowMissing = GUILayout.Toggle(onlyShowMissing, "Only Show Missing", EditorStyles.toggle, GUILayout.ExpandWidth(false));
        }
        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos2)) {
            scrollPos2 = scrollView.scrollPosition;
            if (animationClips.Count > 0 && pathsArray != null)
                for (int i = 0; i < pathsArray.Length; i++)
                    GUICreatePathItem(pathsArray[i]);
        }
    }

    void GUICreatePathItem(string path) {
        if (!paths.TryGetValue(path, out var properties)) return;
        var gameObject = FindObjectInRoot(path);
        if (onlyShowMissing && gameObject != null) return;

        if (!tempPathOverrides.TryGetValue(path, out var newPath)) newPath = path;

        using (new EditorGUILayout.HorizontalScope()) {
            var color = gameObject != null ? Color.green : Color.red;
            var bgColor = GUI.backgroundColor;
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(columnWidth + 60), GUILayout.MinHeight(EditorGUIUtility.singleLineHeight))) {
                state.TryGetValue(path, out var expanded);
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    expanded = EditorGUILayout.Foldout(expanded, properties.Count.ToString(), true);
                    if (changed.changed) state[path] = expanded;
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth))) {
                    Type defaultType = null, transformType = null;
                    int customTypeCount = 0;
                    foreach (var entry in properties) {
                        var type = entry.type;
                        if (!tempTypes.TryGetValue(type, out var count)) {
                            if (typeof(Transform).IsAssignableFrom(type))
                                transformType = type;
                            else if (type != typeof(GameObject)) {
                                if (defaultType == null)
                                    defaultType = type;
                                customTypeCount++;
                            }
                        }
                        tempTypes[type] = count + 1;
                    }
                    if (customTypeCount > 1) defaultType = transformType;
                    else if (defaultType == null) defaultType = transformType;
                    if (animatorObject != null) GUI.backgroundColor = color;
                    UnityObject subObject = gameObject;
                    if (defaultType != null && gameObject != null && gameObject.TryGetComponent(defaultType, out var component))
                        subObject = component;
                    using (new EditorGUI.DisabledScope(animatorObject == null))
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        var newSubObject = EditorGUILayout.ObjectField(
                            subObject,
                            (subObject != null ? subObject.GetType() : null) ?? defaultType ?? typeof(GameObject),
                            true, GUILayout.ExpandWidth(true)
                        );
                        if (changed.changed) {
                            if (newSubObject is GameObject newGameObject) {}
                            else if (newSubObject is Component newComponent) newGameObject = newComponent.gameObject;
                            else newGameObject = null;
                            if (newGameObject != null) UpdatePath(path, ChildPath(newGameObject));
                        }
                    }
                    if (expanded) {
                        foreach (var kv in tempTypes) {
                            var type = kv.Key;
                            if (tempContent == null) tempContent = new GUIContent();
                            tempContent.image = AssetPreview.GetMiniTypeThumbnail(type);
                            tempContent.text = $"{ObjectNames.NicifyVariableName(type.Name)} ({kv.Value} Bindings)";
                            if (animatorObject != null) GUI.backgroundColor = gameObject != null && gameObject.TryGetComponent(type, out var _) ? Color.green : Color.red;
                            EditorGUILayout.LabelField(tempContent, EditorStyles.objectFieldThumb);
                        }
                        EditorGUILayout.Space();
                    }
                    tempTypes.Clear();
                    GUI.backgroundColor = bgColor;
                }
            }
            if (animatorObject != null) GUI.backgroundColor = color;
            newPath = EditorGUILayout.TextField(newPath, GUILayout.ExpandWidth(true));
            GUI.backgroundColor = bgColor;
            if (newPath != path) tempPathOverrides[path] = newPath;
            if (GUILayout.Button("Change", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                UpdatePath(path, newPath);
                tempPathOverrides.Remove(path);
            }
        }
    }

    void OnInspectorUpdate() => Repaint();

    void OnHierarchyChanged() => objectCache.Clear();

    void FillModel() {
        paths.Clear();
        foreach (var animationClip in animationClips.Keys) {
            FillModelWithCurves(AnimationUtility.GetCurveBindings(animationClip));
            FillModelWithCurves(AnimationUtility.GetObjectReferenceCurveBindings(animationClip));
        }
        if (pathsArray == null || pathsArray.Length != paths.Count) pathsArray = new string[paths.Count];
        paths.Keys.CopyTo(pathsArray, 0);
    }

    private void FillModelWithCurves(EditorCurveBinding[] curves) {
        foreach (var curveData in curves) {
            var key = curveData.path;
            if (!paths.TryGetValue(key, out var properties)) {
                properties = new HashSet<EditorCurveBinding>();
                paths.Add(key, properties);
            }
            properties.Add(curveData);
        }
    }

    void ReplaceRoot(string oldRoot, string newRoot) {
        var oldRootMatcher = new Regex($"^{Regex.Escape(oldRoot)}", RegexOptions.Compiled);
        UpdatePath(path => oldRootMatcher.Replace(path, newRoot));
    }

    void UpdatePath(string oldPath, string newPath) {
        if (paths.ContainsKey(newPath) && !EditorUtility.DisplayDialog(
            "Path already exists",
            $"Path `{newPath}` already exists.\nDo you want to overwrite it?",
            "Yes", "No"
        )) return;
        UpdatePath(path => path == oldPath ? newPath : null);
    }

    void UpdatePath(Func<string, string> converter) {
        EnsureAssetModifiable();
        AssetDatabase.StartAssetEditing();
        if (clipsArray == null || clipsArray.Length != animationClips.Count)
            clipsArray = new AnimationClip[animationClips.Count];
        animationClips.Keys.CopyTo(clipsArray, 0);
        for (int i = 0; i < clipsArray.Length; i++) {
            if (!animationClips.TryGetValue(clipsArray[i], out var animationClip))
                animationClip = clipsArray[i];
            if (AssetDatabase.IsForeignAsset(animationClip)) {
                var newClip = Instantiate(animationClip);
                newClip.name = animationClip.name;
                animationClips[animationClip] = newClip;
                animationClip = newClip;
            }
            Undo.RecordObject(animationClip, "Animation Hierarchy Change");
            var curves = AnimationUtility.GetCurveBindings(animationClip);
            for (int j = 0; j < curves.Length; j++)
                try {
                    var binding = curves[j];
                    var newPath = converter(binding.path);
                    if (string.IsNullOrEmpty(newPath)) continue;
                    UpdateBinding(clipsArray[i], animationClip, binding, newPath);
                } finally {
                    EditorUtility.DisplayProgressBar(
                        "Updating Animation Hierarchy",
                        animationClip.name,
                        (i + (float)j / curves.Length) / animationClips.Count
                    );
                }
        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();
        FillModel();
        Repaint();
    }

    static void UpdateBinding(AnimationClip oldClip, AnimationClip newClip, EditorCurveBinding binding, string newPath) {
        if (newClip == null) newClip = oldClip;
        var editorCurve = AnimationUtility.GetEditorCurve(oldClip, binding);
        if (editorCurve != null) {
            AnimationUtility.SetEditorCurve(newClip, binding, null);
            binding.path = newPath;
            AnimationUtility.SetEditorCurve(newClip, binding, editorCurve);
            return;
        }
        var objRefCurve = AnimationUtility.GetObjectReferenceCurve(oldClip, binding);
        if (objRefCurve != null) {
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, null);
            binding.path = newPath;
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, objRefCurve);
            return;
        }
    }

    void EnsureAssetModifiable() {
        foreach (var kv in animationClips) {
            if (kv.Value != null) {
                if (AssetDatabase.IsForeignAsset(kv.Value))
                    throw new UnityException($"Animation clip {kv.Value} is not modifiable!");
                continue;
            }
            if (AssetDatabase.IsForeignAsset(kv.Key)) {
                if (!SaveModifiedAssets())
                    throw new UnityException($"Animation clip {kv.Key} is not modifiable!");
                break;
            }
        }
    }

    bool SaveModifiedAssets(bool forceSaveAs = false) {
        var clipsRequrieToSave = new HashSet<AnimationClip>();
        var clips = new List<AnimationClip>(animationClips.Keys);
        foreach (var animationClip in clips)
            if (animationClips.TryGetValue(animationClip, out var modifiedClip)) {
                if (forceSaveAs) modifiedClip = Instantiate(modifiedClip == null ? animationClip : modifiedClip);
                if (modifiedClip != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(modifiedClip)))
                    clipsRequrieToSave.Add(modifiedClip);
            }
        UnityObject rootAsset = null;
        var path = EditorUtility.SaveFilePanelInProject(
            "Save modified animation clip",
            "ModifiedAnimationClip",
            "asset",
            "Save modified animation clips",
            AssetDatabase.GetAssetPath(animatorObject) ?? ""
        );
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var clip in clipsRequrieToSave) {
            if (rootAsset == null) {
                rootAsset = clip;
                AssetDatabase.CreateAsset(clip, path);
            } else
                AssetDatabase.AddObjectToAsset(clip, rootAsset);
        }
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = null;
        foreach (var controller in animatorControllers) {
            if (controller is AnimatorController animatorController) {
                foreach (var layer in animatorController.layers)
                    foreach (var state in layer.stateMachine.states)
                        if (state.state.motion is AnimationClip clip && animationClips.TryGetValue(clip, out var modifiedClip))
                            state.state.motion = modifiedClip;
            } else if (controller is AnimatorOverrideController animatorOverrideController) {
                if (overrides == null) overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                animatorOverrideController.GetOverrides(overrides);
                for (int i = 0, count = overrides.Count; i < count; i++) {
                    var pair = overrides[i];
                    if (animationClips.TryGetValue(pair.Value, out var modifiedClip))
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, modifiedClip);
                }
                animatorOverrideController.ApplyOverrides(overrides);
            }
            EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
        clips.Clear();
        foreach (var kv in animationClips) clips.Add(kv.Value != null ? kv.Value : kv.Key);
        animationClips.Clear();
        foreach (var clip in clips) animationClips[clip] = null;
        return true;
    }

    GameObject FindObjectInRoot(string path) {
        if (animatorObject == null) return null;
        if (!objectCache.TryGetValue(path, out var obj)) {
            var child = animatorObject.transform.Find(path);
            if (child != null) obj = child.gameObject;
            objectCache.Add(path, obj);
        }
        return obj;
    }

    string ChildPath(GameObject obj) {
        if (animatorObject == null)
            throw new UnityException("Please assign Referenced Animator (Root) first!");
        var stack = new Stack<Transform>();
        var rootTransform = animatorObject.transform;
        for (var current = obj.transform; current != rootTransform; current = current.parent) {
            if (current == null)
                throw new UnityException($"Object must belong to {animatorObject}!");
            stack.Push(current);
        }
        var names = new string[stack.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = stack.Pop().name;
        return string.Join("/", names);
    }

    void ShowButton(Rect rect) {
        if (lockIcon == null) lockIcon = GUI.skin.FindStyle("IN LockButton");
        using (var changed = new EditorGUI.ChangeCheckScope()) {
            locked = GUI.Toggle(rect, locked, GUIContent.none, lockIcon);
            if (changed.changed && !locked) OnSelectionChange();
        }
    }
}
#endif

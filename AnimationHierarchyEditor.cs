#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class AnimationHierarchyEditor : EditorWindow {
    static int columnWidth = 300;
    Animator animatorObject;
    readonly List<AnimationClip> animationClips = new List<AnimationClip>();
    List<string> pathsKeys;
    Dictionary<string, List<EditorCurveBinding>> paths;
    readonly Dictionary<string, string> tempPathOverrides = new Dictionary<string, string>();
    Vector2 scrollPos = Vector2.zero;

    [MenuItem("Window/Animation Hierarchy Editor")]
    static void ShowWindow() => GetWindow<AnimationHierarchyEditor>();
    string sReplacementOldRoot;
    string sReplacementNewRoot;
    bool locked;
    static GUIStyle lockIcon;

    void OnSelectionChange() {
        if (locked) return;
        if (Selection.objects.Length > 1) {
            Debug.Log($"Length? {Selection.objects.Length}");
            animationClips.Clear();
            foreach (var o in Selection.objects)
                if (o is AnimationClip clip)
                    animationClips.Add(clip);
        } else if (Selection.activeObject is AnimationClip clip) {
            animationClips.Clear();
            animationClips.Add(clip);
            FillModel();
        } else
            animationClips.Clear();
        Repaint();
    }

    private string sOriginalRoot = "Root";
    private string sNewRoot = "SomeNewObject/Root";

    void OnGUI() {
        if (Event.current.type == EventType.ValidateCommand)
            switch (Event.current.commandName) {
                case "UndoRedoPerformed":
                    FillModel();
                    break;
            }
        
        if (animationClips.Count <= 0) {
            GUILayout.Label("Please select an Animation Clip");
            return;
        }
        scrollPos = GUILayout.BeginScrollView(scrollPos, GUIStyle.none);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Referenced Animator (Root):", GUILayout.Width(columnWidth));
        animatorObject = EditorGUILayout.ObjectField(animatorObject, typeof(Animator), true, GUILayout.Width(columnWidth)) as Animator;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Animation Clip:", GUILayout.Width(columnWidth));

        if (animationClips.Count == 1)
            animationClips[0] = EditorGUILayout.ObjectField(animationClips[0], typeof(AnimationClip), true, GUILayout.Width(columnWidth)) as AnimationClip;
        else
            GUILayout.Label("Multiple Anim Clips: " + animationClips.Count, GUILayout.Width(columnWidth));
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(20);

        EditorGUILayout.BeginHorizontal();

        sOriginalRoot = EditorGUILayout.TextField(sOriginalRoot, GUILayout.Width(columnWidth));
        sNewRoot = EditorGUILayout.TextField(sNewRoot, GUILayout.Width(columnWidth));
        if (GUILayout.Button("Replace Root")) {
            Debug.Log("O: " + sOriginalRoot + " N: " + sNewRoot);
            ReplaceRoot(sOriginalRoot, sNewRoot);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Reference path:", GUILayout.Width(columnWidth));
        GUILayout.Label("Animated properties:", GUILayout.Width(columnWidth * 0.5f));
        GUILayout.Label("(Count)", GUILayout.Width(60));
        GUILayout.Label("Object:", GUILayout.Width(columnWidth));
        EditorGUILayout.EndHorizontal();

        if (paths != null)
            foreach (var path in pathsKeys)
                GUICreatePathItem(path);

        GUILayout.Space(40);
        GUILayout.EndScrollView();
    }


    void GUICreatePathItem(string path) {
        string newPath = path;
        GameObject obj = FindObjectInRoot(path);
        GameObject newObj;
        var properties = paths[path];

        string pathOverride = path;

        if (tempPathOverrides.ContainsKey(path)) pathOverride = tempPathOverrides[path];

        EditorGUILayout.BeginHorizontal();

        pathOverride = EditorGUILayout.TextField(pathOverride, GUILayout.Width(columnWidth));
        if (pathOverride != path) tempPathOverrides[path] = pathOverride;

        if (GUILayout.Button("Change", GUILayout.Width(60))) {
            newPath = pathOverride;
            tempPathOverrides.Remove(path);
        }

        EditorGUILayout.LabelField(properties != null ? properties.Count.ToString() : "0", GUILayout.Width(60));

        Color standardColor = GUI.color;

        GUI.color = obj != null ? Color.green : Color.red;

        newObj = EditorGUILayout.ObjectField(obj, typeof(GameObject), true, GUILayout.Width(columnWidth)) as GameObject;

        GUI.color = standardColor;

        EditorGUILayout.EndHorizontal();

        try {
            if (obj != newObj) UpdatePath(path, ChildPath(newObj));
            if (newPath != path) UpdatePath(path, newPath);
        } catch (UnityException ex) {
            Debug.LogError(ex.Message);
        }
    }

    void OnInspectorUpdate() => Repaint();

    void FillModel() {
        if (paths == null)
            paths = new Dictionary<string, List<EditorCurveBinding>>();
        else
            paths.Clear();
        if (pathsKeys == null)
            pathsKeys = new List<string>();
        else
            pathsKeys.Clear();
        foreach (var animationClip in animationClips) {
            FillModelWithCurves(AnimationUtility.GetCurveBindings(animationClip));
            FillModelWithCurves(AnimationUtility.GetObjectReferenceCurveBindings(animationClip));
        }
    }

    private void FillModelWithCurves(EditorCurveBinding[] curves) {
        foreach (var curveData in curves) {
            var key = curveData.path;
            if (paths.ContainsKey(key))
                paths[key].Add(curveData);
            else {
                var newProperties = new List<EditorCurveBinding> { curveData };
                paths.Add(key, newProperties);
                pathsKeys.Add(key);
            }
        }
    }

    void ReplaceRoot(string oldRoot, string newRoot) {
        sReplacementOldRoot = oldRoot;
        sReplacementNewRoot = newRoot;
        AssetDatabase.StartAssetEditing();
        for (int iCurrentClip = 0; iCurrentClip < animationClips.Count; iCurrentClip++) {
            var animationClip = animationClips[iCurrentClip];
            Undo.RecordObject(animationClip, "Animation Hierarchy Root Change");
            for (int iCurrentPath = 0; iCurrentPath < pathsKeys.Count; iCurrentPath++) {
                var path = pathsKeys[iCurrentPath];
                var curves = paths[path];
                for (int i = 0; i < curves.Count; i++) {
                    var binding = curves[i];
                    if (path.Contains(sReplacementOldRoot) && !path.Contains(sReplacementNewRoot)) {
                        var sNewPath = Regex.Replace(path, $"^{sReplacementOldRoot}", sReplacementNewRoot);
                        AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, binding);
                        if (curve != null) {
                            AnimationUtility.SetEditorCurve(animationClip, binding, null);
                            binding.path = sNewPath;
                            AnimationUtility.SetEditorCurve(animationClip, binding, curve);
                        } else {
                            var objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(animationClip, binding);
                            AnimationUtility.SetObjectReferenceCurve(animationClip, binding, null);
                            binding.path = sNewPath;
                            AnimationUtility.SetObjectReferenceCurve(animationClip, binding, objectReferenceCurve);
                        }
                    }
                }

                // Update the progress meter
                float fChunk = 1f / animationClips.Count;
                float fProgress = (iCurrentClip * fChunk) + fChunk * ((float)iCurrentPath / pathsKeys.Count);
                EditorUtility.DisplayProgressBar("Animation Hierarchy Progress", "How far along the animation editing has progressed.", fProgress);
            }

        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();
        FillModel();
        Repaint();
    }

    void UpdatePath(string oldPath, string newPath) {
        if (paths[newPath] != null)
            throw new UnityException($"Path {newPath} already exists in that animation!");
        AssetDatabase.StartAssetEditing();
        for (int iCurrentClip = 0; iCurrentClip < animationClips.Count; iCurrentClip++) {
            AnimationClip animationClip = animationClips[iCurrentClip];
            Undo.RecordObject(animationClip, "Animation Hierarchy Change");
            // recreating all curves one by one
            // to maintain proper order in the editor - 
            // slower than just removing old curve
            // and adding a corrected one, but it's more
            // user-friendly
            for (int iCurrentPath = 0; iCurrentPath < pathsKeys.Count; iCurrentPath++) {
                var path = pathsKeys[iCurrentPath];
                var curves = paths[path];
                for (int i = 0; i < curves.Count; i++) {
                    var binding = curves[i];
                    var curve = AnimationUtility.GetEditorCurve(animationClip, binding);
                    var objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(animationClip, binding);
                    if (curve != null)
                        AnimationUtility.SetEditorCurve(animationClip, binding, null);
                    else
                        AnimationUtility.SetObjectReferenceCurve(animationClip, binding, null);

                    if (path == oldPath)
                        binding.path = newPath;
                    if (curve != null)
                        AnimationUtility.SetEditorCurve(animationClip, binding, curve);
                    else
                        AnimationUtility.SetObjectReferenceCurve(animationClip, binding, objectReferenceCurve);
                    float fChunk = 1f / animationClips.Count;
                    float fProgress = (iCurrentClip * fChunk) + fChunk * ((float)iCurrentPath / pathsKeys.Count);

                    EditorUtility.DisplayProgressBar("Animation Hierarchy Progress", "How far along the animation editing has progressed.", fProgress);
                }
            }
        }
        AssetDatabase.StopAssetEditing();
        EditorUtility.ClearProgressBar();
        FillModel();
        Repaint();
    }

    GameObject FindObjectInRoot(string path) {
        if (animatorObject == null) return null;
        var child = animatorObject.transform.Find(path);
        return child != null ? child.gameObject : null;
    }

    string ChildPath(GameObject obj, bool sep = false) {
        if (animatorObject == null)
            throw new UnityException("Please assign Referenced Animator (Root) first!");
        if (obj == animatorObject.gameObject)
            return "";
        if (obj.transform.parent == null)
            throw new UnityException($"Object must belong to {animatorObject}!");
        return ChildPath(obj.transform.parent.gameObject, true) + obj.name + (sep ? "/" : "");
    }

    private void ShowButton(Rect rect) {
        EditorGUI.BeginDisabledGroup(false);
        EditorGUI.BeginChangeCheck();
        if (lockIcon == null) lockIcon = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle("IN LockButton");
        locked = GUI.Toggle(rect, locked, GUIContent.none, lockIcon);
        EditorGUI.EndChangeCheck();
        EditorGUI.EndDisabledGroup();
    }
}
#endif

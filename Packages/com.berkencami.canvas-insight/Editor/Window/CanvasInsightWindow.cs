using System.Collections.Generic;
using CanvasInsight.Analysis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CanvasInsight.Editor
{
    public sealed class CanvasInsightWindow : EditorWindow
    {
        const double RefreshInterval = 0.25;

        readonly List<Canvas> _canvases = new List<Canvas>();
        readonly CanvasSnapshot _snapshot = new CanvasSnapshot();
        readonly BatchResult _result = new BatchResult();
        readonly Dictionary<int, List<NativeBatch>> _native = new Dictionary<int, List<NativeBatch>>();
        readonly Dictionary<int, int> _whatIf = new Dictionary<int, int>();

        Canvas _canvas;
        bool _live = true;
        bool _sceneOverlay = true;
        bool _showNative;
        int _nativeFrame = -1;
        int _highlightBatch = -1;
        Vector2 _scroll;
        double _nextRefresh;
        bool _refreshRequested = true;

        [MenuItem("Window/Analysis/Canvas Insight")]
        public static void Open() => GetWindow<CanvasInsightWindow>("Canvas Insight");

        void OnEnable()
        {
            titleContent = new GUIContent("Canvas Insight", EditorGUIUtility.IconContent("Canvas Icon").image);
            EditorApplication.hierarchyChanged += RequestRefresh;
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += SceneOverlay;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= RequestRefresh;
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= SceneOverlay;
            SceneView.RepaintAll();
            if (_showNative) NativeBatchReader.StopRecording();
        }

        void RequestRefresh() => _refreshRequested = true;

        void OnSelectionChanged()
        {
            var go = Selection.activeGameObject;
            var canvas = go ? go.GetComponentInParent<Canvas>() : null;
            if (canvas && canvas != _canvas)
            {
                _canvas = canvas;
                _highlightBatch = -1;
                RequestRefresh();
                Repaint();
            }
        }

        void Update()
        {
            if (!_refreshRequested && !(_live && EditorApplication.timeSinceStartup >= _nextRefresh)) return;
            Refresh();
            Repaint();
        }

        void Refresh()
        {
            _refreshRequested = false;
            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;

            FindCanvases();
            if (!_canvas && _canvases.Count > 0) _canvas = _canvases[0];

            _snapshot.Capture(_canvas);
            BatchSimulator.Simulate(_snapshot.Elements, _result);
            _whatIf.Clear();
            for (int k = 0; k < _result.BatchCount; k++)
            {
                int count = WhatIf.BatchCountIfBlockerMovedAfter(_snapshot.Elements, _result, k);
                if (count >= 0) _whatIf[k] = count;
            }

            _nativeFrame = -1;
            if (_showNative && EditorApplication.isPlaying)
                NativeBatchReader.TryReadLatest(_native, out _nativeFrame);

            if (_sceneOverlay) SceneView.RepaintAll();
        }

        void FindCanvases()
        {
            _canvases.Clear();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                stage.prefabContentsRoot.GetComponentsInChildren(false, _canvases);
            else
                _canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            _canvases.Sort((a, b) => string.CompareOrdinal(Label(a), Label(b)));
            if (_canvas && !_canvases.Contains(_canvas)) _canvas = null;
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            // A scene change can destroy canvases between our refresh and this repaint.
            if (_canvases.Exists(c => !c) || HasDestroyedSource()) Refresh();
            DrawToolbar();
            if (!_canvas)
            {
                EditorGUILayout.HelpBox("No canvas found. Open a scene or prefab with a uGUI Canvas, or select one.", MessageType.Info);
                return;
            }

            DrawSummary();
            // No horizontal scrollbar: content wraps to the window width instead.
            _scroll = EditorGUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            for (int k = 0; k < _result.BatchCount; k++) DrawBatch(k);
            if (_showNative) DrawNative();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int index = _canvases.IndexOf(_canvas);
                var labels = new string[_canvases.Count];
                for (int i = 0; i < labels.Length; i++) labels[i] = Label(_canvases[i]);
                int picked = EditorGUILayout.Popup(index, labels, EditorStyles.toolbarPopup, GUILayout.MinWidth(160));
                if (picked != index && picked >= 0)
                {
                    _canvas = _canvases[picked];
                    _highlightBatch = -1;
                    RequestRefresh();
                }

                GUILayout.FlexibleSpace();
                _live = GUILayout.Toggle(_live, new GUIContent("Live", "Re-analyze continuously"), EditorStyles.toolbarButton);
                bool overlay = GUILayout.Toggle(_sceneOverlay, new GUIContent("Scene Overlay", "Outline batches in the Scene view"), EditorStyles.toolbarButton);
                if (overlay != _sceneOverlay)
                {
                    _sceneOverlay = overlay;
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(!NativeBatchReader.IsSupported))
                {
                    var tip = NativeBatchReader.IsSupported
                        ? "Compare with the batches Unity actually built (Play Mode, enables the Profiler)"
                        : "Native batch data is not available in this Unity version";
                    bool native = GUILayout.Toggle(_showNative, new GUIContent("Compare Native", tip), EditorStyles.toolbarButton);
                    if (native != _showNative)
                    {
                        _showNative = native;
                        if (native) NativeBatchReader.StartRecording();
                        else NativeBatchReader.StopRecording();
                        RequestRefresh();
                    }
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) RequestRefresh();
            }
        }

        void DrawSummary()
        {
            int drawables = 0;
            foreach (var e in _snapshot.Elements) if (!e.IsBarrier) drawables++;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
                EditorGUILayout.LabelField($"{_result.BatchCount} batches", big, GUILayout.Height(24));
                EditorGUILayout.LabelField($"{drawables} drawn elements · estimated by simulation", EditorStyles.miniLabel);

                int best = _result.BatchCount;
                foreach (var kv in _whatIf) if (kv.Value < best) best = kv.Value;
                if (_result.ActionableCount > 0)
                    EditorGUILayout.LabelField(
                        $"{_result.ActionableCount} batch(es) exist only because something overlapping is drawn in between." +
                        (best < _result.BatchCount ? $" The best single hierarchy move saves {_result.BatchCount - best}." : ""),
                        EditorStyles.wordWrappedMiniLabel);
                if (_result.OrderSplitCount > 0)
                    EditorGUILayout.LabelField(
                        $"{_result.OrderSplitCount} batch(es) exist only because of hierarchy order (no overlap involved).",
                        EditorStyles.wordWrappedMiniLabel);

                if (_showNative) DrawNativeVerdict();
            }
        }

        void DrawNativeVerdict()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Native: enter Play Mode to record the batches Unity builds.", EditorStyles.miniLabel);
                return;
            }
            if (_nativeFrame < 0 || !_native.TryGetValue(_canvas.GetInstanceID(), out var batches))
            {
                EditorGUILayout.LabelField("Native: waiting for profiler data…", EditorStyles.miniLabel);
                return;
            }
            bool match = batches.Count == _result.BatchCount;
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = match ? new Color(0.3f, 0.75f, 0.35f) : new Color(0.95f, 0.6f, 0.2f) } };
            EditorGUILayout.LabelField(match
                ? $"Native: {batches.Count} batches — simulation matches (frame {_nativeFrame})"
                : $"Native: {batches.Count} batches — simulation differs, trust native (frame {_nativeFrame})", style);
        }

        void DrawBatch(int k)
        {
            var batch = _result.Batches[k];
            var color = BatchColor(k);
            bool highlighted = _highlightBatch == k;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var swatch = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                    swatch.y += 3;
                    EditorGUI.DrawRect(swatch, color);
                    var title = $"Batch {k + 1}  ·  {batch.Elements.Count} element{(batch.Elements.Count == 1 ? "" : "s")}  ·  {TextureName(batch.First)}";
                    if (GUILayout.Button(title, highlighted ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        _highlightBatch = highlighted ? -1 : k;
                        SceneView.RepaintAll();
                    }
                }

                EditorGUILayout.LabelField(Explain(k), EditorStyles.wordWrappedMiniLabel);

                if (batch.IsActionable || (batch.InterleavedWith >= 0 && batch.BlockerElement < 0))
                    EditorGUILayout.HelpBox(Advice(k), MessageType.Warning);
                else if (batch.InterleavedWith >= 0)
                    EditorGUILayout.HelpBox(
                        $"Sits on top of '{ElementName(batch.BlockerElement)}', one layer above batch {batch.InterleavedWith + 1}, so it can't join it. " +
                        "This is expected for content drawn on a panel.", MessageType.None);
                else if (batch.OrderSplitWith >= 0)
                    EditorGUILayout.HelpBox(OrderAdvice(k), MessageType.Info);

                if (highlighted || batch.Elements.Count <= 6)
                    DrawMembers(batch);
                else
                    EditorGUILayout.LabelField($"{batch.Elements.Count} elements — click the title to list them", EditorStyles.miniLabel);
            }
        }

        readonly List<(string label, Object target)> _buttons = new List<(string, Object)>();

        void DrawMembers(Batch batch)
        {
            _buttons.Clear();
            foreach (int i in batch.Elements) _buttons.Add((ElementName(i), _snapshot.Sources[i]));
            FlowButtons(_buttons);
        }

        // Lays out buttons left to right, wrapping to the next row at the window width.
        void FlowButtons(List<(string label, Object target)> buttons)
        {
            float available = position.width - 40f;
            int i = 0;
            while (i < buttons.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    float used = 0f;
                    do
                    {
                        var content = new GUIContent(buttons[i].label);
                        float w = EditorStyles.miniButton.CalcSize(content).x + 4f;
                        if (used > 0f && used + w > available) break;
                        if (GUILayout.Button(content, EditorStyles.miniButton)) Select(buttons[i].target);
                        used += w;
                        i++;
                    } while (i < buttons.Count);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        string Explain(int k)
        {
            var batch = _result.Batches[k];
            int first = batch.First;
            switch (batch.Reason)
            {
                case BreakReason.None:
                    return "First batch of the canvas.";
                case BreakReason.NestedCanvas:
                    return $"Starts after nested canvas '{ElementName(batch.BarrierElement)}'. Elements before and after a nested canvas never share a batch.";
                case BreakReason.DifferentMaterial:
                    var material = _snapshot.Materials[first];
                    return material && material.name.EndsWith("(Instance)")
                        ? $"Split from batch {k}: different material ({MaterialName(first)}). This is a per-object material instance " +
                          "(created by code such as setting a TMP outline or accessing .material), so it can never batch with other objects. " +
                          "Use a shared material or preset instead."
                        : $"Split from batch {k}: different material ({MaterialName(first)}).";
                case BreakReason.DifferentTexture:
                    return $"Split from batch {k}: different texture ({TextureName(first)}).";
                case BreakReason.DifferentClip:
                    return $"Split from batch {k}: different RectMask2D clipping.";
                default:
                    return batch.Reason.ToString();
            }
        }

        string Advice(int k)
        {
            var batch = _result.Batches[k];
            string with = $"batch {batch.InterleavedWith + 1}";
            if (batch.BlockerElement < 0)
                return $"Same material and texture as {with}, but something overlapping draws in between.";

            string blocker = ElementName(batch.BlockerElement);
            string text = $"Could join {with}, but '{blocker}' overlaps it and must draw in between.\n" +
                          $"Fixes: move '{blocker}' later in the hierarchy (if it may draw on top), stop the overlap, " +
                          $"or put '{blocker}' on the same atlas/material.";
            if (_whatIf.TryGetValue(k, out int after) && after < _result.BatchCount)
                text += $"\nEstimate: moving '{blocker}' after '{ElementName(LastOf(batch))}' → {_result.BatchCount} → {after} batches.";
            return text;
        }

        string OrderAdvice(int k)
        {
            var batch = _result.Batches[k];
            int with = batch.OrderSplitWith;
            string between = k - with == 2 ? $"batch {with + 2}" : $"batches {with + 2}–{k}";
            return $"Same material, texture and clipping as batch {with + 1}, and nothing overlaps. " +
                   $"They are split only because {between} (different material, texture or clipping) comes between them in hierarchy order.\n" +
                   $"Fix: place these elements next to batch {with + 1}'s elements in the hierarchy, before or after the elements of {between}.";
        }

        void DrawNative()
        {
            if (!EditorApplication.isPlaying || _nativeFrame < 0 || !_native.TryGetValue(_canvas.GetInstanceID(), out var batches)) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Native batches (from the Profiler)", EditorStyles.boldLabel);
            for (int k = 0; k < batches.Count; k++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Batch {k + 1}  ·  {batches[k].Members.Count} element{(batches[k].Members.Count == 1 ? "" : "s")}  ·  {batches[k].Reason}", EditorStyles.miniBoldLabel);
                    _buttons.Clear();
                    foreach (var m in batches[k].Members)
                        if (m) _buttons.Add((m.name, m));
                    FlowButtons(_buttons);
                }
            }
        }

        // ------------------------------------------------------------------ Scene overlay

        void SceneOverlay(SceneView view)
        {
            if (!_sceneOverlay || !_canvas || Event.current.type != EventType.Repaint) return;
            var t = _canvas.transform;
            var verts = new Vector3[4];
            for (int i = 0; i < _snapshot.Elements.Count; i++)
            {
                var e = _snapshot.Elements[i];
                if (e.IsBarrier) continue;
                int k = _result.ElementBatch[i];
                if (_highlightBatch >= 0 && k != _highlightBatch) continue;
                var c = BatchColor(k);
                verts[0] = t.TransformPoint(new Vector3(e.Bounds.XMin, e.Bounds.YMin));
                verts[1] = t.TransformPoint(new Vector3(e.Bounds.XMin, e.Bounds.YMax));
                verts[2] = t.TransformPoint(new Vector3(e.Bounds.XMax, e.Bounds.YMax));
                verts[3] = t.TransformPoint(new Vector3(e.Bounds.XMax, e.Bounds.YMin));
                Handles.DrawSolidRectangleWithOutline(verts, new Color(c.r, c.g, c.b, 0.12f), c);
            }
        }

        // ------------------------------------------------------------------ helpers

        static Color BatchColor(int k) => Color.HSVToRGB((k * 0.61803f + 0.08f) % 1f, 0.7f, 0.95f);

        static string Label(Canvas c) => !c ? "(missing)" : c.isRootCanvas ? c.name : $"{c.rootCanvas.name} / {c.name}";

        bool HasDestroyedSource()
        {
            foreach (var source in _snapshot.Sources)
                if (!source) return true;
            return false;
        }

        string ElementName(int i)
        {
            var source = _snapshot.Sources[i];
            if (!source) return "(missing)";
            return source is UnityEngine.UI.Mask ? source.name + " (mask end)" : source.name;
        }

        string TextureName(int i)
        {
            var tex = _snapshot.Textures[i];
            return tex ? tex.name : "no texture";
        }

        string MaterialName(int i)
        {
            var mat = _snapshot.Materials[i];
            if (!mat) return "stencil material";
            // StencilMaterial names look like "Stencil Id:1, Op:Keep, ... (Default UI Material)".
            if (mat.name.StartsWith("Stencil"))
            {
                int open = mat.name.LastIndexOf('(');
                string baseName = open >= 0 ? mat.name.Substring(open + 1).TrimEnd(')') : mat.name;
                return $"stencil (Mask) · {baseName}";
            }
            return mat.name;
        }

        static int LastOf(Batch batch)
        {
            int last = -1;
            foreach (int i in batch.Elements) if (i > last) last = i;
            return last;
        }

        static void Select(Object source)
        {
            if (!source) return;
            var go = source is Component c ? c.gameObject : source as GameObject;
            Selection.activeObject = go ? go : source;
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}

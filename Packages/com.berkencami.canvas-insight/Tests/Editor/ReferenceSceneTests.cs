using System.Collections.Generic;
using System.Linq;
using CanvasInsight.Analysis;
using CanvasInsight.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CanvasInsight.Tests
{
    /// <summary>
    /// Builds real uGUI hierarchies and checks the simulated batch count.
    /// Every expected value was verified against native profiler batch data
    /// (UI Details module) on Unity 6000.3, Overlay canvases.
    /// </summary>
    public class ReferenceSceneTests
    {
        readonly List<Object> _created = new List<Object>();
        Texture2D _tex1, _tex2;

        [SetUp]
        public void SetUp()
        {
            _tex1 = Track(MakeTexture(Color.red, "tex1"));
            _tex2 = Track(MakeTexture(Color.blue, "tex2"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ------------------------------------------------------------------ cases

        [Test]
        public void AtlasMix_AlternatingTexturesInARow_Is2()
        {
            var c = NewCanvas();
            for (int i = 0; i < 6; i++) Raw(c, "Item" + i, i % 2 == 0 ? _tex1 : _tex2, new Vector2(-300 + i * 110, 0));
            AssertBatches(c, 2);
        }

        [Test]
        public void Interleave_ABA_Is3()
        {
            var c = NewCanvas();
            Raw(c, "A", _tex1, new Vector2(0, 0));
            Raw(c, "B", _tex2, new Vector2(30, 0));
            Raw(c, "C", _tex1, new Vector2(60, 0));
            AssertBatches(c, 3);
        }

        [Test]
        public void StencilMask_WithChildrenAndOutsider_Is4()
        {
            var c = NewCanvas();
            var panel = Img(c, "MaskPanel", Vector2.zero, new Vector2(300, 150));
            panel.gameObject.AddComponent<Mask>();
            for (int i = 0; i < 3; i++) Raw(panel.transform, "Child" + i, _tex1, new Vector2(-100 + i * 100, 0));
            Raw(c, "Outside", _tex1, new Vector2(0, 250));
            AssertBatches(c, 4);
        }

        [Test]
        public void TwoRectMask2DPanels_WithOutsider_Is4()
        {
            var c = NewCanvas();
            for (int p = 0; p < 2; p++)
            {
                var panel = Img(c, "Clip" + p, new Vector2(-200 + p * 400, 0), new Vector2(300, 150));
                panel.gameObject.AddComponent<RectMask2D>();
                for (int i = 0; i < 3; i++) Raw(panel.transform, "Child" + i, _tex1, new Vector2(-100 + i * 100, 0));
            }
            Raw(c, "Outside", _tex1, new Vector2(0, 250));
            AssertBatches(c, 4);
        }

        [Test]
        public void ZOffset_DoesNotBreakBatching_Is1()
        {
            var c = NewCanvas();
            for (int i = 0; i < 5; i++)
            {
                var r = Raw(c, "Item" + i, _tex1, new Vector2(-300 + i * 150, 0));
                if (i == 2) r.rectTransform.anchoredPosition3D = new Vector3(0, 0, 10);
            }
            AssertBatches(c, 1);
        }

        [Test]
        public void Tilt_DoesNotBreakBatching_Is1()
        {
            var c = NewCanvas();
            for (int i = 0; i < 5; i++)
            {
                var r = Raw(c, "Item" + i, _tex1, new Vector2(-300 + i * 150, 0));
                if (i == 2) r.rectTransform.localRotation = Quaternion.Euler(0, 30, 0);
            }
            AssertBatches(c, 1);
        }

        [Test]
        public void NestedCanvas_SplitsParent_Is2()
        {
            var c = NewCanvas();
            Raw(c, "Left", _tex1, new Vector2(-200, 0));
            var nested = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
            nested.transform.SetParent(c, false);
            Raw(nested.transform, "Middle", _tex1, Vector2.zero);
            Raw(c, "Right", _tex1, new Vector2(200, 0));
            AssertBatches(c, 2);
            AssertBatches(nested.transform, 1);
        }

        [Test]
        public void ListRows_LegacyText_Is3()
        {
            var c = NewCanvas();
            for (int i = 0; i < 4; i++)
            {
                var row = Img(c, "Row" + i, new Vector2(0, 150 - i * 100), new Vector2(400, 80));
                Raw(row.transform, "Icon", _tex2, new Vector2(-150, 0));
                LegacyText(row.transform, "Label", "Item " + i, Vector2.zero, new Vector2(300, 60), TextAnchor.MiddleCenter);
            }
            AssertBatches(c, 3);
        }

        [Test]
        public void TransparentGraphic_IsCulled_Is1()
        {
            var c = NewCanvas();
            Raw(c, "A", _tex1, new Vector2(150, 0));
            Img(c, "ClearPanel", Vector2.zero, new Vector2(600, 100)).color = new Color(1, 1, 1, 0);
            Raw(c, "C", _tex1, new Vector2(150, 0));
            AssertBatches(c, 1);
        }

        [Test]
        public void WideLegacyText_UsesMeshBoundsNotRect_Is2()
        {
            var c = NewCanvas();
            Raw(c, "A", _tex1, new Vector2(150, 0));
            LegacyText(c, "WideText", "Hi", Vector2.zero, new Vector2(600, 100), TextAnchor.MiddleLeft);
            Raw(c, "C", _tex1, new Vector2(150, 0));
            AssertBatches(c, 2);
        }

        // ---- TextMeshPro (skipped when TMP Essential Resources are not imported)

        [Test]
        public void Tmp_ListRows_Is3()
        {
            RequireTmp();
            var c = NewCanvas();
            for (int i = 0; i < 4; i++)
            {
                var row = Img(c, "Row" + i, new Vector2(0, 150 - i * 100), new Vector2(400, 80));
                Raw(row.transform, "Icon", _tex2, new Vector2(-150, 0), 60);
                Tmp(row.transform, "Label", "Item " + i, Vector2.zero, new Vector2(250, 60));
            }
            AssertBatches(c, 3);
        }

        [Test]
        public void Tmp_Interleave_Is3()
        {
            RequireTmp();
            var c = NewCanvas();
            Tmp(c, "TextA", "AAAA", Vector2.zero, new Vector2(200, 60));
            Raw(c, "Icon", _tex1, new Vector2(20, 0), 60);
            Tmp(c, "TextB", "BBBB", new Vector2(40, 0), new Vector2(200, 60));
            AssertBatches(c, 3);
        }

        [Test]
        public void Tmp_MaterialPresets_Is2()
        {
            RequireTmp();
            var outline = FindAsset<Material>("LiberationSans SDF - Outline t:Material");
            if (!outline) Assert.Ignore("Outline material preset not found");
            var c = NewCanvas();
            for (int i = 0; i < 4; i++)
            {
                var t = Tmp(c, "Text" + i, "Label " + i, new Vector2(-300 + i * 200, 0), new Vector2(180, 60));
                if (i % 2 == 1) t.fontSharedMaterial = outline;
            }
            AssertBatches(c, 2);
        }

        [Test]
        public void Tmp_InlineSprites_AddSubmeshBatch_Is2()
        {
            RequireTmp();
            if (!TMP_Settings.defaultSpriteAsset) Assert.Ignore("No default TMP sprite asset");
            var c = NewCanvas();
            Tmp(c, "WithSprite", "Coins <sprite=0> <sprite=1>", new Vector2(-150, 0), new Vector2(300, 60));
            Tmp(c, "Plain", "Plain text", new Vector2(200, 0), new Vector2(200, 60));
            AssertBatches(c, 2);
        }

        [Test]
        public void Tmp_WideText_UsesMeshBoundsNotRect_Is2()
        {
            RequireTmp();
            var c = NewCanvas();
            Raw(c, "A", _tex1, new Vector2(150, 0));
            Tmp(c, "WideText", "Hi", Vector2.zero, new Vector2(600, 100)).alignment = TextAlignmentOptions.MidlineLeft;
            Raw(c, "C", _tex1, new Vector2(150, 0));
            AssertBatches(c, 2);
        }

        // ------------------------------------------------------------------ helpers

        void AssertBatches(Transform canvasTransform, int expected)
        {
            Canvas.ForceUpdateCanvases();
            var snapshot = new CanvasSnapshot();
            snapshot.Capture(canvasTransform.GetComponent<Canvas>());
            var result = BatchSimulator.Simulate(snapshot.Elements);
            Assert.AreEqual(expected, result.BatchCount, Describe(snapshot, result));
        }

        static string Describe(CanvasSnapshot s, BatchResult r) =>
            string.Join(" ", r.Batches.Select(b => "[" + string.Join(" ", b.Elements.Select(i => s.Sources[i].name)) + "]"));

        static void RequireTmp()
        {
            if (!TMP_Settings.instance || !TMP_Settings.defaultFontAsset)
                Assert.Ignore("TMP Essential Resources are not imported");
        }

        static T FindAsset<T>(string filter) where T : Object
        {
            var guid = AssetDatabase.FindAssets(filter).FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        Transform NewCanvas()
        {
            var go = Track(new GameObject("TestCanvas", typeof(Canvas)));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return go.transform;
        }

        static Texture2D MakeTexture(Color c, string name)
        {
            var t = new Texture2D(4, 4) { name = name };
            t.SetPixels(Enumerable.Repeat(c, 16).ToArray());
            t.Apply();
            return t;
        }

        static RawImage Raw(Transform parent, string name, Texture tex, Vector2 pos, float size = 100)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RawImage>();
            r.texture = tex;
            r.rectTransform.sizeDelta = new Vector2(size, size);
            r.rectTransform.anchoredPosition = pos;
            return r;
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var i = go.GetComponent<Image>();
            i.rectTransform.sizeDelta = size;
            i.rectTransform.anchoredPosition = pos;
            return i;
        }

        static Text LegacyText(Transform parent, string name, string text, Vector2 pos, Vector2 size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = 30;
            t.alignment = anchor;
            t.rectTransform.sizeDelta = size;
            t.rectTransform.anchoredPosition = pos;
            return t;
        }

        static TextMeshProUGUI Tmp(Transform parent, string name, string text, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 30;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = size;
            t.rectTransform.anchoredPosition = pos;
            t.ForceMeshUpdate();
            return t;
        }
    }
}

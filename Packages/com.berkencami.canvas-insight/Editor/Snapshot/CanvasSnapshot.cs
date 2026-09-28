using System.Collections.Generic;
using System.Reflection;
using CanvasInsight.Analysis;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CanvasInsight.Editor
{
    /// <summary>
    /// Reads one canvas (nested canvases excluded) into plain <see cref="ElementSnapshot"/>s in
    /// hierarchy order, keeping a parallel list of the Unity objects they came from.
    /// </summary>
    public sealed class CanvasSnapshot
    {
        public Canvas Canvas { get; private set; }
        public readonly List<ElementSnapshot> Elements = new List<ElementSnapshot>();
        /// <summary>Graphic for drawables, nested Canvas for barriers, Mask for its stencil-pop pass.</summary>
        public readonly List<Object> Sources = new List<Object>();
        public readonly List<Material> Materials = new List<Material>();
        public readonly List<Texture> Textures = new List<Texture>();

        static readonly MethodInfo _onPopulateMesh = typeof(Graphic).GetMethod(
            "OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(VertexHelper) }, null);
        static readonly VertexHelper _vertexHelper = new VertexHelper();
        static readonly List<IMeshModifier> _modifiers = new List<IMeshModifier>();
        static readonly object[] _invokeArgs = { _vertexHelper };

        public void Capture(Canvas canvas)
        {
            Canvas = canvas;
            Elements.Clear();
            Sources.Clear();
            Materials.Clear();
            Textures.Clear();
            if (!canvas || !canvas.isActiveAndEnabled) return;

            // Apply pending layout and mesh rebuilds first: in Edit Mode a freshly opened scene can still
            // have zero-sized layout children until it is rendered.
            Canvas.ForceUpdateCanvases();
            Walk(canvas.transform, 0);
        }

        void Walk(Transform t, int clipId)
        {
            if (!t.gameObject.activeInHierarchy) return;

            if (t != Canvas.transform && t.TryGetComponent(out Canvas nested) && nested.enabled)
            {
                Add(ElementSnapshot.Barrier(), nested, null, null);
                return;
            }

            if (t.TryGetComponent(out Graphic g) && IsDrawn(g) && TryGetMeshBounds(g, out var local))
            {
                var material = g.materialForRendering;
                var texture = g.mainTexture;
                Add(ElementSnapshot.Drawable(ToCanvasSpace(t, local), Id(material), Id(texture), clipId), g, material, DisplayTexture(g, material, texture));
            }

            // RectMask2D clips its children, not its own graphic.
            if (t.TryGetComponent(out RectMask2D rectMask) && rectMask.enabled)
                clipId = clipId * 31 + rectMask.GetInstanceID();

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), clipId);

            // A stencil Mask draws its graphic again after the children, with a separate "unmask" material.
            if (t.TryGetComponent(out Mask mask) && mask.enabled && mask.graphic && IsDrawn(mask.graphic)
                && TryGetMeshBounds(mask.graphic, out var maskLocal))
            {
                var bounds = ToCanvasSpace(t, maskLocal);
                Add(ElementSnapshot.Drawable(bounds, -Id(mask.graphic.materialForRendering) - 1, Id(mask.graphic.mainTexture), clipId),
                    mask, null, mask.graphic.mainTexture);
            }
        }

        void Add(ElementSnapshot element, Object source, Material material, Texture texture)
        {
            Elements.Add(element);
            Sources.Add(source);
            Materials.Add(material);
            Textures.Add(texture);
        }

        static int Id(Object o) => o ? o.GetInstanceID() : 0;

        // TMP reports a white mainTexture; its real texture (the font or sprite atlas) lives on the material.
        static Texture DisplayTexture(Graphic g, Material material, Texture texture)
        {
            if ((g is TMP_Text || g is TMP_SubMeshUI) && material && material.mainTexture)
                return material.mainTexture;
            return texture;
        }

        static bool IsDrawn(Graphic g)
        {
            if (!g.enabled || g.canvasRenderer.cull) return false;
            // With cullTransparentMesh (default in Unity 6) a fully transparent graphic is not batched at all.
            var cr = g.canvasRenderer;
            return !(cr.cullTransparentMesh && g.color.a * cr.GetAlpha() * cr.GetInheritedAlpha() <= 0f);
        }

        Rect2D ToCanvasSpace(Transform t, Bounds local)
        {
            var canvasTransform = Canvas.transform;
            Vector3 min = local.min, max = local.max;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, 0f);
                var p = canvasTransform.InverseTransformPoint(t.TransformPoint(corner));
                xMin = Mathf.Min(xMin, p.x);
                yMin = Mathf.Min(yMin, p.y);
                xMax = Mathf.Max(xMax, p.x);
                yMax = Mathf.Max(yMax, p.y);
            }
            return new Rect2D(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// Local bounds of the mesh the graphic submits. Native batching overlaps meshes, not rects:
        /// a wide text box with short text does not block what is under its empty area.
        /// </summary>
        static bool TryGetMeshBounds(Graphic g, out Bounds bounds)
        {
            switch (g)
            {
                case TMP_Text tmp:
                    // In Edit Mode a freshly opened scene has no TMP meshes until it is rendered.
                    if (!string.IsNullOrEmpty(tmp.text) && (!tmp.mesh || tmp.mesh.vertexCount == 0))
                        tmp.ForceMeshUpdate();
                    return TryGetBounds(tmp.mesh, out bounds);
                case TMP_SubMeshUI sub: return TryGetBounds(sub.mesh, out bounds);
            }

            bounds = default;
            _vertexHelper.Clear();
            _onPopulateMesh.Invoke(g, _invokeArgs);
            g.GetComponents(_modifiers);
            foreach (var modifier in _modifiers)
                if (!(modifier is Behaviour b) || b.enabled)
                    modifier.ModifyMesh(_vertexHelper);

            int count = _vertexHelper.currentVertCount;
            if (count == 0) return false;
            var v = new UIVertex();
            _vertexHelper.PopulateUIVertex(ref v, 0);
            bounds = new Bounds(v.position, Vector3.zero);
            for (int i = 1; i < count; i++)
            {
                _vertexHelper.PopulateUIVertex(ref v, i);
                bounds.Encapsulate(v.position);
            }
            return true;
        }

        static bool TryGetBounds(Mesh mesh, out Bounds bounds)
        {
            bounds = mesh ? mesh.bounds : default;
            return mesh && mesh.vertexCount > 0 && bounds.size.sqrMagnitude > 0f;
        }
    }
}

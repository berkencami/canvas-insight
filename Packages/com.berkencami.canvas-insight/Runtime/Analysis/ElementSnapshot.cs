namespace CanvasInsight.Analysis
{
    /// <summary>Axis-aligned rectangle in canvas space.</summary>
    public readonly struct Rect2D
    {
        public readonly float XMin, YMin, XMax, YMax;

        public Rect2D(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        /// <summary>Strict overlap: rectangles that only touch at an edge do not overlap.</summary>
        public bool Overlaps(in Rect2D other) =>
            XMin < other.XMax && other.XMin < XMax && YMin < other.YMax && other.YMin < YMax;

        public bool Contains(in Rect2D inner) =>
            XMin <= inner.XMin && YMin <= inner.YMin && XMax >= inner.XMax && YMax >= inner.YMax;

        public override string ToString() => $"({XMin:0.#}, {YMin:0.#}) - ({XMax:0.#}, {YMax:0.#})";
    }

    /// <summary>
    /// One drawable element of a canvas, in hierarchy order. Plain data so the simulator can be
    /// tested without a scene.
    /// </summary>
    public struct ElementSnapshot
    {
        /// <summary>Bounds of the generated mesh (not the RectTransform) in canvas space.</summary>
        public Rect2D Bounds;
        public int MaterialId;
        public int TextureId;
        /// <summary>RectMask2D clipping group. 0 = not clipped.</summary>
        public int ClipId;
        /// <summary>
        /// A nested canvas at this hierarchy position. It draws nothing here, but elements before and
        /// after it can never share a batch.
        /// </summary>
        public bool IsBarrier;

        public static ElementSnapshot Drawable(Rect2D bounds, int materialId, int textureId, int clipId = 0) =>
            new ElementSnapshot { Bounds = bounds, MaterialId = materialId, TextureId = textureId, ClipId = clipId };

        public static ElementSnapshot Barrier() => new ElementSnapshot { IsBarrier = true };

        internal bool SameBatchKey(in ElementSnapshot other) =>
            MaterialId == other.MaterialId && TextureId == other.TextureId && ClipId == other.ClipId;
    }
}

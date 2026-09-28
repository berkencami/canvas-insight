using System.Collections.Generic;

namespace CanvasInsight.Analysis
{
    /// <summary>Why a batch could not be merged into the batch drawn right before it.</summary>
    public enum BreakReason : byte
    {
        /// <summary>First batch of the canvas.</summary>
        None,
        DifferentMaterial,
        DifferentTexture,
        DifferentClip,
        /// <summary>First batch after a nested canvas.</summary>
        NestedCanvas,
    }

    public sealed class Batch
    {
        /// <summary>Element indices in draw order.</summary>
        public readonly List<int> Elements = new List<int>();
        public int Depth;
        public BreakReason Reason;
        /// <summary>For <see cref="BreakReason.NestedCanvas"/>: index of the barrier element.</summary>
        public int BarrierElement = -1;

        /// <summary>
        /// Index of an earlier batch with the same material, texture and clipping. When set, this batch
        /// exists only because something is drawn in between.
        /// </summary>
        public int InterleavedWith = -1;
        /// <summary>The element whose overlap pushed this batch above <see cref="InterleavedWith"/>.</summary>
        public int BlockerElement = -1;
        /// <summary>
        /// The blocker fully contains the element it pushes up, i.e. it is the panel the content sits on.
        /// Being one layer above it is expected, so moving the blocker is not a real fix.
        /// </summary>
        public bool BlockerIsBackground;

        /// <summary>Interleaved, and the blocker is something that could realistically be moved.</summary>
        public bool IsActionable => InterleavedWith >= 0 && BlockerElement >= 0 && !BlockerIsBackground;

        /// <summary>
        /// Index of an earlier batch at the same depth with the same material, texture and clipping.
        /// Nothing overlaps; batches with another key sort between them because of hierarchy order.
        /// </summary>
        public int OrderSplitWith = -1;

        public int First => Elements[0];
    }

    public sealed class BatchResult
    {
        public readonly List<Batch> Batches = new List<Batch>();
        /// <summary>Per element: batch index, or -1 for barriers.</summary>
        public int[] ElementBatch = System.Array.Empty<int>();
        /// <summary>Per element: computed depth.</summary>
        public int[] ElementDepth = System.Array.Empty<int>();
        /// <summary>Per element: overlapping element with a different key that set its depth, or -1.</summary>
        public int[] ElementBlocker = System.Array.Empty<int>();

        public int BatchCount => Batches.Count;

        /// <summary>Batches split from an earlier same-key batch only by hierarchy order.</summary>
        public int OrderSplitCount
        {
            get
            {
                int n = 0;
                foreach (var b in Batches)
                    if (b.OrderSplitWith >= 0) n++;
                return n;
            }
        }

        /// <summary>Batches that could merge with an earlier one if their blocker moved.</summary>
        public int InterleavedCount
        {
            get
            {
                int n = 0;
                foreach (var b in Batches)
                    if (b.InterleavedWith >= 0) n++;
                return n;
            }
        }

        /// <summary>Interleaved batches whose blocker is not simply the panel they sit on.</summary>
        public int ActionableCount
        {
            get
            {
                int n = 0;
                foreach (var b in Batches)
                    if (b.IsActionable) n++;
                return n;
            }
        }

        internal void Reset(int elementCount)
        {
            Batches.Clear();
            if (ElementBatch.Length != elementCount)
            {
                ElementBatch = new int[elementCount];
                ElementDepth = new int[elementCount];
                ElementBlocker = new int[elementCount];
            }
        }
    }
}

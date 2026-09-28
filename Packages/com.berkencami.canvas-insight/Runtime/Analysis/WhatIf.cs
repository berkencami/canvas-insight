using System.Collections.Generic;

namespace CanvasInsight.Analysis
{
    /// <summary>Re-runs the simulation on an edited copy of a canvas to estimate the effect of a fix.</summary>
    public static class WhatIf
    {
        static readonly List<ElementSnapshot> _scratch = new List<ElementSnapshot>();
        static readonly BatchResult _result = new BatchResult();

        /// <summary>
        /// Batch count if the element at <paramref name="from"/> were moved in the hierarchy so that it
        /// comes right after <paramref name="after"/>. Bounds, material and clipping stay the same.
        /// </summary>
        public static int BatchCountAfterMove(IReadOnlyList<ElementSnapshot> elements, int from, int after)
        {
            _scratch.Clear();
            for (int i = 0; i < elements.Count; i++)
            {
                if (i != from) _scratch.Add(elements[i]);
                if (i == after) _scratch.Add(elements[from]);
            }
            BatchSimulator.Simulate(_scratch, _result);
            return _result.BatchCount;
        }

        /// <summary>
        /// For an interleaved batch: batch count if its blocker were drawn after the last element of the
        /// batch instead of before it. Returns -1 when the move does not apply.
        /// </summary>
        public static int BatchCountIfBlockerMovedAfter(IReadOnlyList<ElementSnapshot> elements, BatchResult result, int batchIndex)
        {
            var batch = result.Batches[batchIndex];
            int blocker = batch.BlockerElement;
            if (!batch.IsActionable) return -1;

            int last = -1;
            foreach (int i in batch.Elements)
                if (i > last) last = i;
            if (last <= blocker) return -1;

            // Crossing a nested canvas would move the element into another canvas.
            for (int i = blocker + 1; i <= last; i++)
                if (elements[i].IsBarrier) return -1;

            return BatchCountAfterMove(elements, blocker, last);
        }
    }
}

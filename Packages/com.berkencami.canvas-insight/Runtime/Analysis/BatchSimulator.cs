using System.Collections.Generic;

namespace CanvasInsight.Analysis
{
    /// <summary>
    /// C# model of uGUI canvas batching. It is not the native implementation; results were checked
    /// against native profiler data on reference scenes (see Tests).
    ///
    /// For each element in hierarchy order:
    ///   depth = max over earlier overlapping elements of (same key ? their depth : their depth + 1)
    /// Then sort by (depth, material, texture, hierarchy order) and merge consecutive elements that
    /// share material, texture and clipping. Nested canvases split the list into independent segments.
    /// </summary>
    public static class BatchSimulator
    {
        static readonly List<int> _order = new List<int>();

        public static BatchResult Simulate(IReadOnlyList<ElementSnapshot> elements)
        {
            var result = new BatchResult();
            Simulate(elements, result);
            return result;
        }

        public static void Simulate(IReadOnlyList<ElementSnapshot> elements, BatchResult result)
        {
            int n = elements.Count;
            result.Reset(n);

            int segmentStart = 0;
            int barrier = -1;
            for (int i = 0; i < n; i++)
            {
                if (!elements[i].IsBarrier) continue;
                SimulateSegment(elements, segmentStart, i, barrier, result);
                result.ElementBatch[i] = -1;
                result.ElementDepth[i] = 0;
                result.ElementBlocker[i] = -1;
                barrier = i;
                segmentStart = i + 1;
            }
            SimulateSegment(elements, segmentStart, n, barrier, result);
        }

        static void SimulateSegment(IReadOnlyList<ElementSnapshot> e, int start, int end, int barrierBefore, BatchResult result)
        {
            if (start >= end) return;
            int[] depth = result.ElementDepth, blocker = result.ElementBlocker;

            for (int i = start; i < end; i++)
            {
                int d = 0, b = -1;
                var ei = e[i];
                for (int j = start; j < i; j++)
                {
                    var ej = e[j];
                    if (!ei.Bounds.Overlaps(ej.Bounds)) continue;
                    bool same = ei.SameBatchKey(ej);
                    int candidate = same ? depth[j] : depth[j] + 1;
                    if (candidate > d)
                    {
                        d = candidate;
                        b = same ? blocker[j] : j;
                    }
                }
                depth[i] = d;
                blocker[i] = b;
            }

            _order.Clear();
            for (int i = start; i < end; i++) _order.Add(i);
            _order.Sort((x, y) =>
            {
                int c = depth[x].CompareTo(depth[y]);
                if (c != 0) return c;
                c = e[x].MaterialId.CompareTo(e[y].MaterialId);
                if (c != 0) return c;
                c = e[x].TextureId.CompareTo(e[y].TextureId);
                return c != 0 ? c : x.CompareTo(y);
            });

            int segmentFirstBatch = result.Batches.Count;
            Batch current = null;
            foreach (int i in _order)
            {
                if (current == null || !e[current.First].SameBatchKey(e[i]))
                {
                    current = NewBatch(e, i, current, barrierBefore, segmentFirstBatch, result);
                    result.Batches.Add(current);
                }
                current.Elements.Add(i);
                result.ElementBatch[i] = result.Batches.Count - 1;
            }

            // An element's blocker is only meaningful for the batch that was split off.
            for (int k = segmentFirstBatch; k < result.Batches.Count; k++)
            {
                var batch = result.Batches[k];
                if (batch.InterleavedWith < 0) continue;
                foreach (int i in batch.Elements)
                {
                    if (blocker[i] < 0) continue;
                    batch.BlockerElement = blocker[i];
                    batch.BlockerIsBackground = e[blocker[i]].Bounds.Contains(e[i].Bounds);
                    break;
                }
            }
        }

        static Batch NewBatch(IReadOnlyList<ElementSnapshot> e, int first, Batch previous, int barrierBefore,
            int segmentFirstBatch, BatchResult result)
        {
            var batch = new Batch { Depth = result.ElementDepth[first] };

            if (previous == null)
            {
                batch.Reason = barrierBefore >= 0 ? BreakReason.NestedCanvas : BreakReason.None;
                batch.BarrierElement = barrierBefore;
            }
            else
            {
                var p = e[previous.First];
                var c = e[first];
                batch.Reason = p.MaterialId != c.MaterialId ? BreakReason.DifferentMaterial
                    : p.TextureId != c.TextureId ? BreakReason.DifferentTexture
                    : BreakReason.DifferentClip;
            }

            for (int k = segmentFirstBatch; k < result.Batches.Count; k++)
            {
                if (!e[result.Batches[k].First].SameBatchKey(e[first])) continue;
                // Same depth: nothing overlaps, other batches just sort between them in hierarchy order.
                if (result.Batches[k].Depth == batch.Depth) batch.OrderSplitWith = k;
                else batch.InterleavedWith = k;
                break;
            }
            return batch;
        }
    }
}

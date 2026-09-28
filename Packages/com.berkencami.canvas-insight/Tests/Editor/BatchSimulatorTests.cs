using System.Collections.Generic;
using CanvasInsight.Analysis;
using NUnit.Framework;

namespace CanvasInsight.Tests
{
    /// <summary>Pure-data tests for the simulator. No scene, no Unity objects.</summary>
    public class BatchSimulatorTests
    {
        const int MatUI = 1, MatText = 2, MatPanel = 3;
        const int Tex1 = 10, Tex2 = 20;

        static Rect2D Box(float x, float y, float size = 100) => new Rect2D(x, y, x + size, y + size);

        static BatchResult Run(params ElementSnapshot[] elements) => BatchSimulator.Simulate(new List<ElementSnapshot>(elements));

        [Test]
        public void Empty_HasNoBatches()
        {
            Assert.AreEqual(0, Run().BatchCount);
        }

        [Test]
        public void SameKey_NonOverlapping_MergeIntoOneBatch()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(200, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(400, 0), MatUI, Tex1));
            Assert.AreEqual(1, r.BatchCount);
            Assert.AreEqual(BreakReason.None, r.Batches[0].Reason);
        }

        [Test]
        public void AlternatingTextures_NonOverlapping_SortIntoTwoBatches()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(200, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(400, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(600, 0), MatUI, Tex2));
            Assert.AreEqual(2, r.BatchCount);
            Assert.AreEqual(BreakReason.DifferentTexture, r.Batches[1].Reason);
            Assert.AreEqual(0, r.InterleavedCount);
        }

        [Test]
        public void Interleaved_ReportsBlockerAndEarlierBatch()
        {
            // A(tex1) B(tex2) C(tex1), each overlapping the previous one.
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(30, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(60, 0), MatUI, Tex1));

            Assert.AreEqual(3, r.BatchCount);
            var c = r.Batches[r.ElementBatch[2]];
            Assert.AreEqual(r.ElementBatch[0], c.InterleavedWith, "C should be flagged as splittable from A");
            Assert.AreEqual(1, c.BlockerElement, "B is what pushes C up");
        }

        [Test]
        public void TextOnPanel_BlockerIsBackground_NotActionable()
        {
            // Label A (text) elsewhere, then a panel with a second label on top of it. The panel sorts
            // after A at depth 0, so the second label (depth 1) cannot simply continue A's batch.
            var r = Run(
                ElementSnapshot.Drawable(Box(500, 0, 50), MatText, Tex1),
                ElementSnapshot.Drawable(Box(0, 0, 200), MatPanel, Tex2),
                ElementSnapshot.Drawable(Box(50, 50, 50), MatText, Tex1));

            var label = r.Batches[r.ElementBatch[2]];
            Assert.AreEqual(r.ElementBatch[0], label.InterleavedWith);
            Assert.AreEqual(1, label.BlockerElement);
            Assert.IsTrue(label.BlockerIsBackground);
            Assert.IsFalse(label.IsActionable);
            Assert.AreEqual(0, r.ActionableCount);
        }

        [Test]
        public void Interleaved_PartialOverlap_IsActionable()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(30, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(60, 0), MatUI, Tex1));
            Assert.AreEqual(1, r.ActionableCount);
        }

        [Test]
        public void Interleaved_IsNotReportedAsOrderSplit()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(30, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(60, 0), MatUI, Tex1));
            Assert.AreEqual(0, r.OrderSplitCount);
        }

        [Test]
        public void DifferentClipBetweenInHierarchy_IsOrderSplit_WithoutBlocker()
        {
            // Nothing overlaps. The clipped element sorts between A and C by hierarchy order.
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(200, 0), MatUI, Tex1, clipId: 7),
                ElementSnapshot.Drawable(Box(400, 0), MatUI, Tex1));

            Assert.AreEqual(3, r.BatchCount);
            var c = r.Batches[r.ElementBatch[2]];
            Assert.AreEqual(r.ElementBatch[0], c.OrderSplitWith);
            Assert.AreEqual(-1, c.InterleavedWith, "no overlap, so not interleaved");
            Assert.AreEqual(-1, c.BlockerElement);
        }

        [Test]
        public void TouchingEdges_DoNotOverlap()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(100, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(200, 0), MatUI, Tex1));
            Assert.AreEqual(2, r.BatchCount);
        }

        [Test]
        public void DifferentMaterial_IsReported()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(0, 0), MatText, Tex1));
            Assert.AreEqual(2, r.BatchCount);
            Assert.AreEqual(BreakReason.DifferentMaterial, r.Batches[1].Reason);
        }

        [Test]
        public void DifferentClip_PreventsMerging()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1, clipId: 5),
                ElementSnapshot.Drawable(Box(200, 0), MatUI, Tex1, clipId: 6));
            Assert.AreEqual(2, r.BatchCount);
            Assert.AreEqual(BreakReason.DifferentClip, r.Batches[1].Reason);
        }

        [Test]
        public void NestedCanvas_SplitsSegments()
        {
            var r = Run(
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Barrier(),
                ElementSnapshot.Drawable(Box(400, 0), MatUI, Tex1));
            Assert.AreEqual(2, r.BatchCount);
            Assert.AreEqual(BreakReason.NestedCanvas, r.Batches[1].Reason);
            Assert.AreEqual(1, r.Batches[1].BarrierElement);
            Assert.AreEqual(-1, r.ElementBatch[1]);
        }

        [Test]
        public void ListRows_BatchByLayerNotByRow()
        {
            // 4 rows of [background, icon, label], overlapping inside a row → 3 batches regardless of row count.
            var elements = new List<ElementSnapshot>();
            for (int i = 0; i < 4; i++)
            {
                float y = i * 100;
                elements.Add(ElementSnapshot.Drawable(new Rect2D(0, y, 400, y + 80), MatUI, 0));
                elements.Add(ElementSnapshot.Drawable(new Rect2D(10, y + 10, 70, y + 70), MatUI, Tex2));
                elements.Add(ElementSnapshot.Drawable(new Rect2D(100, y + 20, 300, y + 60), MatText, Tex1));
            }
            Assert.AreEqual(3, BatchSimulator.Simulate(elements).BatchCount);
        }

        [Test]
        public void WhatIf_MovingBlockerAfterBatch_MergesIt()
        {
            var elements = new List<ElementSnapshot>
            {
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(30, 0), MatUI, Tex2),
                ElementSnapshot.Drawable(Box(60, 0), MatUI, Tex1),
            };
            var r = BatchSimulator.Simulate(elements);
            int split = r.ElementBatch[2];
            Assert.AreEqual(2, WhatIf.BatchCountIfBlockerMovedAfter(elements, r, split));
        }

        [Test]
        public void WhatIf_DoesNotCrossNestedCanvas()
        {
            var elements = new List<ElementSnapshot>
            {
                ElementSnapshot.Drawable(Box(0, 0), MatUI, Tex1),
                ElementSnapshot.Drawable(Box(30, 0), MatUI, Tex2),
                ElementSnapshot.Barrier(),
                ElementSnapshot.Drawable(Box(60, 0), MatUI, Tex1),
            };
            var r = BatchSimulator.Simulate(elements);
            for (int k = 0; k < r.BatchCount; k++)
                Assert.AreEqual(-1, WhatIf.BatchCountIfBlockerMovedAfter(elements, r, k));
        }
    }
}

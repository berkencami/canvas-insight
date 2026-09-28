# Canvas Insight

**Explains uGUI draw calls.** For any canvas, Canvas Insight shows how its elements are batched, why each batch break happens, and which element to move to fix it, with an estimate of the result.

**Unity 6000.3+ · uGUI 2.0 · TextMeshPro · MIT**

## Usage

Open **Window → Analysis → Canvas Insight**, then select any UI object.

- **Batch breakdown:** every batch of the selected canvas, with its elements, texture and material, and the reason it split from the previous one.
- **Interleaving:** batches that could merge with an earlier one, the element drawn in between, a suggested fix and the batch count after it.
- **Scene Overlay:** outlines every element in its batch color. Click a batch to isolate it.
- **Compare Native (Play Mode):** checks the simulation against the batches Unity actually built, read from the Profiler's UI Details data. The Profiler's previous settings are restored when it is turned off.

Works in Edit Mode and Prefab Mode.

## Documentation

Screenshots, a before/after example, how the batching model works, and its limitations:
https://github.com/berkencami/canvas-insight#readme

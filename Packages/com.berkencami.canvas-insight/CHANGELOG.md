# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package follows
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-09-28

### Added

- **Canvas Insight window** (Window → Analysis → Canvas Insight): per-canvas batch breakdown with elements, texture and material.
- Break reasons: different material, different texture, RectMask2D clipping, nested canvas.
- Callouts for per-object material instances and stencil `Mask` materials.
- Interleaving detection with the blocking element, a suggested fix and a what-if batch count.
- Hierarchy-order splits, reported separately from overlaps.
- Content drawn on its own panel is recognised as expected and not flagged.
- Scene view overlay that outlines every element in its batch color.
- *Compare Native* (Play Mode): reads the batches Unity actually built from the Profiler's UI Details data and compares them with the simulation. Restores the Profiler's previous settings when turned off.
- Works in Edit Mode and Prefab Mode.
- Tests: simulator unit tests and 15 reference hierarchies verified against native batch data.

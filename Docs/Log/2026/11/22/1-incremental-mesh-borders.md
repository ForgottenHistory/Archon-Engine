# Incremental Mesh Border Updates
**Date**: 2026-11-22
**Session**: 1
**Status**: 🔄 Code done, needs an in-editor stress check
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Remove the ~250 ms hitch in `TextureUpdateBridge.Update()` on every ownership change with the
  `MeshGeometry` border renderer (many wars ending at once made it a constant stutter)

**Success Criteria:**
- An ownership change rebuilds only the border geometry it affects
- Same look as the full rebuild (strips and junction caps)

---

## Problems Encountered & Solutions

### Problem 1: Every ownership change rebuilt every border mesh
**Symptom:** `TextureUpdateBridge.Update() [Invoke]` 250+ ms in the profiler, once per batch of
ownership changes. The log showed it: `BorderMeshGenerator: 41027 arcs, 26726 junction caps,
1308376 vertices in 7 class meshes (234ms)`, while between two rebuilds only ~25 arcs changed class.

**Root Cause:** `BorderComputeDispatcher.DetectBordersIndexed` only has an incremental path for
`ShaderPixelPerfect`. Every other mode fell back to `DetectBorders()`, which for `MeshGeometry` means
`ClassifyAll` (every arc through the classifier) plus `GenerateBorderMeshes` (one mesh per class for
the whole map) plus the distance field.

**Solution:**
- `IIncrementalBorderRenderer.UpdateProvinces(ushort[])` (optional interface, `IBorderRenderer.cs`).
  The dispatcher calls it for the changed provinces when the active renderer implements it; the full
  rebuild stays the fallback.
- `BorderCurveCache.Reclassify(provinces, classify)`: a province -> arcs index, reclassifies only the
  arcs of the changed provinces and returns the ones whose class changed (with the old class).
- `BorderMeshGenerator.BuildChunked / UpdateChunks`: one mesh per class per 512 x 512 map px chunk.
  An arc's strip lives in the chunk of its middle point; a junction cap lives in the chunk of its
  corner and is built from every same-class arc end at that corner. Arc end vertices are cached at
  build time (geometry only), so a chunk can build its caps without its neighbours' strips. A changed
  arc dirties (its chunk, its two corner chunks) x (old class, new class); only those are rebuilt.
- Chunk meshes get their real bounds plus a margin (shader width / height offsets), so off-screen
  chunks are culled. The old class meshes had whole-map bounds.

### Problem 2: Render queue per mesh overflowed into the highlight renderers
**Symptom:** (found in review, before it shipped) `BorderMeshRenderer.SetMeshes` gave each mesh its
own queue (`baseQueue + i`). Fine with 7 class meshes; with hundreds of chunk meshes the class
materials' queues would pass the highlight outlines' (2970 / 2990) and draw over them.

**Solution:** One queue step per style (meshes sorted by style order, then name), not per mesh.

---

## What Worked ✅

1. **Measure from the existing log lines first** - the generator already logged its own time; that
   pinned the mesh rebuild (234 ms) against the distance field (4 ms) without a profiler capture.
2. **Keep the full path as the fallback** - other renderers and style reloads still use
   `GenerateBorders`; the incremental path is opt-in per renderer.

---

## Architecture Impact

- New optional interface `IIncrementalBorderRenderer` (mechanism; any custom renderer can opt in).
- `MeshGeometryBorderRenderer` now always builds chunked meshes (`Meshes` / `MeshClassIds` list
  several meshes per class). Rivers and the highlight outlines still use `GenerateMeshes` (one mesh
  per class).
- Draw calls: one per non-empty (chunk, class) instead of one per class, offset by culling.

---

## Quick Reference for Future Claude

- Incremental entry: `BorderComputeDispatcher.DetectBordersIndexed` -> `IIncrementalBorderRenderer`
- Chunking: `BorderMeshGenerator.BuildChunked`, `UpdateChunks`, `RebuildChunk`, `ChunkPixels`
- Reclassify: `BorderCurveCache.Reclassify`, `ClassOf`
- Queue per style: `BorderMeshRenderer.SetMeshes`
- `MeshGeometryBorderRenderer.UpdateProvinces` logs when an update takes over 20 ms

**Gotchas:**
- A classifier must only depend on the arc's two provinces (and data that changes with their
  ownership): only arcs touching the changed provinces are reclassified.
- Junction caps need every same-class arc end at a corner: cached per arc in `arcStart` / `arcEnd`.

# Mesh Border Geometry & Rendering Fixes
**Date**: 2026-03-08
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix mesh geometry border rendering: wrong widths, overlapping geometry, terrain clipping

**Secondary Objectives:**
- Clean up the shader to properly handle terrain-following
- Remove hardcoded coordinate assumptions

**Success Criteria:**
- Uniform border width across entire map
- No z-fighting or terrain clipping
- Clean hexagon borders without zigzag artifacts

---

## Context & Background

**Previous Work:**
- See: [01-gpu-border-extraction-pipeline.md](01-gpu-border-extraction-pipeline.md)
- GPU border extraction pipeline working (22x speedup)
- Borders visible but "blurry and unfinished"

**Current State:**
- Borders rendered but with multiple visual issues:
  - Width varied across map (wider at horizontal extremes)
  - Overlapping/double border geometry
  - Zigzag staircase on diagonal hexagon edges
  - Borders clipping into tessellated terrain

**Why Now:**
- GPU extraction pipeline complete, now need visual quality to match

---

## What We Did

### 1. Fixed Non-Uniform Border Width (Aspect Ratio Distortion)
**Files Changed:** `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`

**Root Cause:** Perpendicular offsets were computed in distorted local space (-5 to +5 on a non-square map), then further distorted by MapPlane's non-uniform scale (50, 1, 20).

**Fix:** Compute all geometry in pixel space (uniform coordinates), then convert to world space using actual MapPlane bounds. Render with identity matrix instead of MapPlane's localToWorldMatrix.

Key changes:
- Constructor now takes `Transform mapPlaneTransform` and reads world bounds from `MeshRenderer.bounds`
- `PixelToWorld()` converts pixel coords to world space using actual bounds
- `CalculatePerpendicularPixelSpace()` computes perpendiculars in uniform pixel space
- `BorderMeshRenderer` uses `Matrix4x4.identity` instead of `mapPlaneTransform.localToWorldMatrix`

### 2. Fixed Double Border Geometry
**Files Changed:** `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`

**Root Cause:** `GenerateBorderMeshes()` never cleared `provinceBorderMeshes`/`countryBorderMeshes` lists. Called twice during init (once in `OnInitialize`, once in `GenerateBorders`), each call appended 26 meshes → 52 total with duplicate geometry.

**Fix:** Clear and destroy old meshes at the start of `GenerateBorderMeshes()`.

### 3. Fixed Zigzag Staircase on Diagonal Hex Edges
**Files Changed:** `Scripts/Map/Rendering/Border/BorderCurveExtractor.cs`

**Root Cause:** Raw chained pixels follow pixel-by-pixel staircase on diagonals. Without smoothing, each pixel creates a tiny quad pointing in a different direction.

**Fix:** Re-enabled RDP simplification (epsilon=1.5) + Chaikin smoothing. Removed tessellation step (`TessellatePolyline`) which was unnecessary and created excessive vertices.

### 4. Fixed Terrain Clipping (Borders Inside Ground)
**Files Changed:** `Shaders/BorderMesh.shader`

**Root Cause:** Border mesh vertices sampled heightmap at sparse polyline points, while tessellated terrain has much denser vertex coverage. Between border vertices, quads are flat planes while terrain curves — causing borders to dip below terrain surface.

**Failed approaches:**
- Height offset (`_HeightOffset`) — requires tuning, never perfect
- `Offset -1, -1` depth bias — helps z-fighting but not geometric clipping
- Neighborhood max-height sampling in vertex shader — still misses between vertices
- `SV_Depth` from depth buffer — fixes depth test but not visual position

**Fix:** Use `ZTest Always` + heightmap vertex displacement (no offset). Borders render in Transparent queue (after opaque terrain), so `ZTest Always` ensures they always draw on top. Vertex heightmap sampling keeps borders approximately at terrain height to avoid floating.

### 5. Removed Hardcoded -5 to +5 Coordinate Assumption
**Files Changed:** `BorderMeshGenerator.cs`, `BorderMeshRenderer.cs`, `BorderMesh.shader`

MapPlane bounds are now read dynamically from `MeshRenderer.bounds`. Shader receives `_MapWorldBounds` float4 for heightmap UV derivation.

---

## Decisions Made

### Decision 1: World-Space Vertices + Identity Transform
**Context:** MapPlane has non-uniform scale (50, 1, 20), distorting local-space border geometry
**Decision:** Compute vertices directly in world space, render with identity matrix
**Rationale:** Eliminates all scale distortion. Pixel-space computation ensures uniform width.
**Trade-offs:** Shader needs map bounds passed explicitly for heightmap UV derivation

### Decision 2: ZTest Always for Terrain Following
**Context:** Border mesh can't perfectly match tessellated terrain surface at every point
**Decision:** Use `ZTest Always` in Transparent queue instead of precise height matching
**Rationale:** Transparent queue renders after opaque (terrain), so Always test is safe. Eliminates all clipping. Simple, robust, no tuning needed.
**Trade-offs:** Borders always draw on top of everything in the scene (acceptable for map borders)

### Decision 3: Remove Tessellation Step
**Context:** `TessellatePolyline(maxSegmentLength: 0.5)` subdivided every segment to sub-pixel density
**Decision:** Removed tessellation, keep only RDP + Chaikin
**Rationale:** Tessellation created ~2 vertices per pixel — massively excessive. RDP + Chaikin produce smooth enough geometry. Significant vertex count reduction.

---

## What Worked

1. **Pixel-space geometry computation**
   - Computing perpendiculars and offsets in pixel space before converting to world space
   - Eliminates all aspect ratio and scale distortion issues
   - Reusable pattern: Yes

2. **ZTest Always for decal-like overlays**
   - Simple, robust terrain following without height matching
   - No tuning, no z-fighting, no clipping
   - Reusable pattern: Yes (any overlay on tessellated terrain)

---

## What Didn't Work

1. **Height offset approach**
   - What we tried: Fixed `_HeightOffset` to push borders above terrain
   - Why it failed: Terrain height varies, no single offset works everywhere
   - Lesson: Static offsets can't solve dynamic surface following

2. **Depth bias (`Offset -1, -1`)**
   - What we tried: GPU depth bias to push borders toward camera
   - Why it failed: Helps z-fighting on coplanar surfaces, but can't fix borders geometrically below terrain
   - Lesson: Depth bias is for z-fighting, not geometric mismatch

3. **SV_Depth from depth buffer**
   - What we tried: Sample terrain depth buffer, write as border depth
   - Why it failed: Fixes depth test but doesn't change visual position — border still renders at Y=0 visually
   - Lesson: SV_Depth affects depth buffer only, not rasterization position

4. **Midpoint emission from compute shader**
   - What we tried: Emit midpoint between border pixel and cross-border neighbor to center borders
   - Why it failed: Half-pixel positions broke the chaining algorithm (strict 8-connectivity with distSq <= 2.0)
   - Lesson: Don't change upstream data format without updating all downstream consumers

---

## Problems Encountered & Solutions

### Problem 1: Border width varies across map
**Symptom:** Borders wider at horizontal extremes
**Root Cause:** Perpendiculars computed in distorted local space (-5 to +5), then scaled by non-uniform MapPlane transform (50, 1, 20)
**Solution:** Compute geometry in pixel space, convert to world space, render with identity matrix

### Problem 2: Double/overlapping borders
**Symptom:** Semi-transparent debug showing bright overlapping geometry
**Root Cause:** `GenerateBorderMeshes()` called twice without clearing mesh lists
**Solution:** Clear and destroy old meshes at start of generation

### Problem 3: Zigzag staircase on diagonal hex edges
**Symptom:** Diagonal borders show staircase pattern instead of clean lines
**Root Cause:** Raw pixel chains follow pixel grid, each step creates misaligned quad
**Solution:** Re-enabled RDP simplification + Chaikin smoothing

### Problem 4: Borders inside terrain
**Symptom:** Some borders invisible (below tessellated terrain surface)
**Root Cause:** Border vertices at sparse polyline points, terrain tessellated densely — border quads flat between vertices while terrain curves
**Solution:** `ZTest Always` in Transparent queue

---

## Next Session

### Immediate Next Steps
1. Re-enable border texture sampling in fragment shader (currently solid black)
2. Country border classification (all borders currently Province type, 0 Country borders)
3. Border width/style differentiation (country borders thicker than province borders)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Border vertices computed in pixel space, converted to world space via MapPlane bounds
- Rendered with identity matrix (NOT MapPlane's localToWorldMatrix)
- `ZTest Always` in Transparent queue for terrain following — no height offset needed
- Smoothing pipeline: RDP (epsilon=1.5) + Chaikin (no tessellation)
- Border width default: 0.015 local units → ~4 pixels

**Gotchas for Next Session:**
- Don't add height offset — `ZTest Always` handles terrain following
- Don't add tessellation back — RDP + Chaikin is sufficient
- MapPlane scale is (50, 1, 20), not (1, 1, 1) — always use world bounds
- Chaining algorithm requires integer pixel positions (strict 8-connectivity)

### Code References
- Mesh generator: `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`
- Mesh renderer: `Scripts/Map/Rendering/Border/BorderMeshRenderer.cs`
- Shader: `Shaders/BorderMesh.shader`
- Curve extractor GPU path: `Scripts/Map/Rendering/Border/BorderCurveExtractor.cs:372-490`
- Integration: `Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs`

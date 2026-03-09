# Border Junction Caps & Terrain Following
**Date**: 2026-03-09
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix junction holes where 3+ border polylines meet
- Fix terrain-following so borders aren't inside ground or floating

**Secondary Objectives:**
- Reduce border width for cleaner look
- Test border texture sampling (deferred — Paradox texture not suited for our approach)

**Success Criteria:**
- No holes at junctions
- Borders follow terrain surface without clipping or floating
- Consistent rendering at all zoom levels

---

## Context & Background

**Previous Work:**
- See: [02-mesh-border-geometry-fixes.md](../08/02-mesh-border-geometry-fixes.md)
- Border geometry working: pixel-space computation, world-space output, identity matrix rendering
- `ZTest Always` in Transparent queue for terrain following
- RDP + Chaikin smoothing, no tessellation

**Current State:**
- Borders render correctly but have holes at junctions and `ZTest Always` causes background bleed-through

---

## What We Did

### 1. Junction Caps — Triangle Fan at 3+ Border Junctions
**Files Changed:** `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`

**Problem:** Where 3+ border polylines meet, the quad strip endpoints leave a small hole in the center.

**Implementation:**
- Track endpoint vertex **positions** (not indices) during polyline generation, keyed by snapped pixel position (`Vector2Int`)
- After all polylines, iterate junctions with 3+ endpoints
- `GenerateJunctionCap()`: collect left/right vertex positions, compute center, sort by angle in XZ plane, generate triangle fan

**Key Lesson:** Initially stored vertex indices into the mesh vertex list, but indices become invalid when meshes are split at the 65k vertex limit (lists get cleared). Fixed by storing actual `Vector3` positions instead.

### 2. Polyline Subdivision for Terrain Following
**Files Changed:** `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`

**Problem:** With `ZTest Always`, background (skybox) bleeds through gaps where border quads span between sparse vertices while terrain curves above. With `ZTest LEqual`, borders clip into terrain because they have fewer vertices than tessellated terrain.

**Solution:** `SubdividePolyline()` splits any segment longer than 3 pixels into sub-segments. Each vertex gets its own heightmap sample in the vertex shader, closely following terrain surface.

### 3. Shader: Opaque Queue + Depth-Proportional Bias
**Files Changed:** `Shaders/BorderMesh.shader`

**Problem:** `ZTest Always` in Transparent queue caused background bleed-through. `ZTest LEqual` caused z-fighting with terrain at same height.

**Solution:**
- Moved to `Queue "Geometry+10"` (opaque, after terrain) with `ZWrite On` and `ZTest LEqual`
- Clip-space depth bias proportional to `positionCS.w`: `depthBias = 0.005 * W`
- W-proportional bias is consistent at all zoom levels (close = small bias, far = proportionally larger)

### 4. Border Width Reduction
**Files Changed:** `Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs`

- Reduced default width: 0.015 → 0.0075

### 5. Border Texture Test (Deferred)
**Files Changed:** `Shaders/BorderMesh.shader` (reverted)

- Tested Paradox's `border_texture.png` (64x32 RGBA from Imperator Rome)
- Texture designed for Paradox's rendering approach, not ours — borders invisible when zoomed out, only junctions visible
- Reverted to solid black. Need custom texture designed for our mesh strip UVs.

---

## Decisions Made

### Decision 1: Store Vertex Positions, Not Indices for Junctions
**Context:** Vertex indices become invalid when mesh lists are cleared at 65k vertex limit
**Decision:** Store `Vector3` positions directly in junction endpoint dictionary
**Rationale:** Positions are stable regardless of mesh splitting. Small memory overhead (6 Vector3s per junction).

### Decision 2: CPU Polyline Subdivision Over Shader Tessellation
**Context:** Need denser vertices for heightmap terrain following
**Decision:** Subdivide polylines on CPU (max 3px per segment) rather than adding tessellation to border shader
**Rationale:** Simpler, no shader complexity, consistent vertex density, works with existing pipeline

### Decision 3: W-Proportional Depth Bias Over Fixed Bias
**Context:** Fixed clip-space bias causes z-fighting when zoomed in, excessive offset when zoomed out
**Decision:** `depthBias = 0.005 * positionCS.w` in vertex shader
**Rationale:** W equals view-space distance, so bias scales naturally with camera distance. Consistent at all zoom levels.

### Decision 4: Defer Border Texture Work
**Context:** Paradox's border_texture.png not suited for our mesh strip approach
**Decision:** Keep solid black borders, design custom texture later
**Rationale:** Geometry is solid, texture is cosmetic. Better to design texture for our UV layout.

---

## What Worked

1. **Polyline subdivision + heightmap sampling**
   - Dense vertices follow terrain surface accurately
   - Combined with depth bias, eliminates both clipping and floating

2. **W-proportional depth bias**
   - Consistent z-fighting prevention at all zoom levels
   - No world-space height offset needed (no floating)

3. **Storing positions instead of indices**
   - Avoids invalidation when mesh lists are cleared at 65k limit

---

## What Didn't Work

1. **`ZTest Always` with Transparent queue**
   - Why it failed: Background (skybox) bleeds through where border quads dip below terrain between sparse vertices
   - Lesson: ZTest Always only works if the overlay perfectly covers the surface — gaps let background through

2. **Fixed clip-space depth bias (0.0005)**
   - Why it failed: Depth precision changes with distance — not enough close up, too much far away
   - Lesson: Always scale depth bias with W for zoom-independent behavior

3. **World-space height offset (0.08, 0.25)**
   - Why it failed: Any static offset either clips on slopes or visibly floats on flat terrain
   - Lesson: Height offsets are fundamentally flawed for terrain following. Use depth bias instead.

4. **`Offset -1, -1` alone**
   - Why it failed: Not strong enough for the depth difference between border and tessellated terrain
   - Lesson: GPU offset is for z-fighting between nearly coplanar surfaces, not geometric mismatch

---

## Quick Reference for Future Claude

**What Changed Since Last Session:**
- Junction caps implemented (triangle fan at 3+ border junctions)
- Polyline subdivision (max 3px segments) for terrain following
- Shader: `Geometry+10` queue, `ZWrite On`, `ZTest LEqual`, W-proportional depth bias
- Border width: 0.0075 (was 0.015)
- Border texture: solid black (Paradox texture tested and deferred)

**Gotchas:**
- Don't use `ZTest Always` — causes background bleed-through
- Don't use fixed height offset — clips on slopes or floats on flat
- Don't use fixed clip-space depth bias — breaks at different zoom levels
- Junction endpoint tracking must store positions, not indices (mesh splitting invalidates indices)
- Paradox's border_texture.png is not suited for our mesh strip approach

**Current Shader Setup:**
```
Queue: Geometry+10 (after terrain)
ZWrite: On
ZTest: LEqual
Depth bias: 0.005 * positionCS.w (in vertex shader)
Fragment: solid black (half4(0,0,0,1))
```

### Code References
- Junction caps: `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs:374-428`
- Subdivision: `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs:348-370`
- Shader: `Shaders/BorderMesh.shader`
- Width default: `Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs:29`

---

## Next Session

### Immediate Next Steps
1. Design custom border texture for mesh strip UV layout
2. Country border classification (currently all Province type)
3. Border width/style differentiation (country vs province)

### Related Sessions
- [02-mesh-border-geometry-fixes.md](../08/02-mesh-border-geometry-fixes.md)
- [01-gpu-border-extraction-pipeline.md](../08/01-gpu-border-extraction-pipeline.md)

# Border Terrain Height Matching
**Date**: 2026-03-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Make mesh borders follow tessellated terrain surface properly

**Secondary Objectives:**
- Move heightmap sampling to GPU shader (like Paradox)
- Investigate Imperator Rome's border shader approach via RenderDoc captures

**Success Criteria:**
- Borders visually sit on terrain surface without floating or clipping

---

## Context & Background

**Previous Work:**
- See: [01-border-junction-caps-terrain-following.md](../09/01-border-junction-caps-terrain-following.md)
- Junction caps working, polyline subdivision at 3px, W-proportional depth bias
- Borders still floated above terrain or clipped into it inconsistently

**Core Problem:**
- Tessellated terrain (factor 16) creates curved surfaces between vertices
- Border mesh is thin strips with different vertex positions than terrain grid
- Any height computation (CPU or GPU) at border vertices produces different curves than terrain tessellation
- Height offsets can't fix this — some parts float while others clip

---

## What We Did

### 1. Investigated Imperator Rome's Border Shader (RenderDoc)
**Files:** `imperator-investigation/1102_vertex.txt`, `imperator-investigation/1102_pixel.txt`

**Key findings:**
- Paradox uses NO heightmap sampling in border vertex shader
- Heights are pre-baked into vertex data on CPU
- Simple constant `vHeightOffset` added in vertex shader
- Pixel shader: sample `BorderTexture`, apply FoW, apply distance fog, multiply by `vAlpha`
- Works because Paradox bakes vertex heights from same heightmap, and their terrain presumably has matching density

### 2. CPU-Side Height Baking (Attempted)
**Files Changed:** `BorderMeshGenerator.cs`, `MeshGeometryBorderRenderer.cs`

- Added heightmap pixel cache to `BorderMeshGenerator` constructor
- `PixelToWorld()` samples heightmap with bilinear interpolation, applies `(height - 0.5) * heightScale`
- Heights verified correct via mesh bounds (Y: -2.36 to 1.99) and grayscale debug visualization
- **Result:** Heights matched heightmap but NOT tessellated terrain — tessellation curves between vertices, border quads are flat between vertices

### 3. GPU Heightmap Sampling in Shader (Attempted)
**Files Changed:** `BorderMesh.shader`

- Moved heightmap sampling to vertex shader, matching terrain shader's approach
- Used `TRANSFORM_TEX` for UV transformation, same `(height - 0.5) * _HeightScale` formula
- Derived terrain UV from world position via `_MapWorldBounds`
- **Result:** Borders consistently slightly inside terrain (good!) but linear interpolation between vertices still didn't match tessellation curves

### 4. Tessellation on Border Shader (Failed)
**Files Changed:** `BorderMesh.shader`

- Full tessellation pipeline: hull shader, domain shader, heightmap sampling in domain
- Copied tessellation params from terrain material (factor, min/max distance)
- **Result:** Catastrophic — massive spikes and jagged geometry. Different input geometry (thin strips vs grid) produces completely different tessellation patterns even with same heightmap. Fundamental mismatch.

### 5. Depth Buffer Approach (Failed)
**Files Changed:** `BorderMesh.shader`

- Sample `_CameraDepthTexture` in fragment shader
- Write terrain depth + offset via `SV_Depth`
- Required enabling URP depth texture in pipeline settings
- **Result:** `SV_Depth` only affects depth buffer, not visual position. Borders still rendered at vertex shader height visually. Fundamentally wrong approach — can't move pixels with depth output.

### 6. Final Solution: ZTest Always + GPU Heightmap + Subdivision
**Files Changed:** `BorderMesh.shader`

**Approach:** Accept that border mesh can never perfectly match tessellated terrain. Instead:
- Render in `Transparent-100` queue (after terrain + depth copy)
- `ZTest Always` — borders always draw on top of terrain
- `ZWrite Off` — don't affect depth buffer
- GPU heightmap sampling positions borders approximately at terrain height
- 3px polyline subdivision ensures dense enough vertices to prevent background bleed-through

**Result:** Borders appear to sit on terrain surface. Not mathematically perfect but visually nearly indistinguishable.

---

## Decisions Made

### Decision 1: ZTest Always Over Height Matching
**Context:** Tessellated terrain creates curved surfaces that no separate mesh can exactly match
**Options:**
1. CPU height baking — wrong curves
2. GPU heightmap in vertex shader — wrong curves
3. Tessellation on border mesh — catastrophic mismatch
4. Depth buffer projection — SV_Depth can't move pixels
5. ZTest Always + approximate height — visually correct

**Decision:** Option 5
**Rationale:** The fundamental problem is geometric — two different meshes tessellated independently will never match. ZTest Always sidesteps the problem entirely. Combined with approximate heightmap positioning, borders appear to sit on terrain.
**Trade-offs:** Borders always draw on top of everything (acceptable for map borders). Requires subdivision to prevent background bleed-through.

### Decision 2: GPU Heightmap Over CPU Height Baking
**Context:** CPU height baking was working but added complexity
**Decision:** Sample heightmap in vertex shader
**Rationale:** Positions borders at approximately correct height for the ZTest Always approach. Simpler than maintaining CPU heightmap cache. Uses same `TRANSFORM_TEX` as terrain shader.

---

## What Worked

1. **ZTest Always + Transparent queue**
   - Renders after terrain, always draws on top
   - Combined with approximate height positioning, creates illusion of terrain-following

2. **Polyline subdivision (3px max)**
   - Dense enough vertices that border quads closely approximate terrain
   - Prevents background bleed-through with ZTest Always

3. **GPU heightmap sampling with TRANSFORM_TEX**
   - Matches terrain shader's UV transformation
   - Positions borders approximately at terrain height

---

## What Didn't Work

1. **CPU height baking**
   - Heights correct but linear interpolation between vertices doesn't match tessellation curves
   - Borders float on convex terrain, clip on concave

2. **Tessellation on border shader**
   - Thin strip geometry tessellates completely differently from terrain grid
   - Same heightmap, different input triangles = wildly different surfaces
   - Produced massive spikes and jagged artifacts
   - Lesson: Tessellation results depend on input geometry shape, not just the displacement function

3. **Depth buffer + SV_Depth**
   - SV_Depth only writes to depth buffer, doesn't change visual fragment position
   - Borders still rendered at vertex shader height visually
   - Also required ZTest Always to execute fragment (defeating the purpose)
   - Lesson: SV_Depth is for depth testing/occlusion, not visual positioning

4. **Height offsets (constant, W-proportional, hardcoded)**
   - Any constant offset floats on some terrain while clipping on other
   - W-proportional depth bias helped z-fighting but not geometric mismatch
   - Lesson: Offsets can't fix fundamental geometry mismatch

5. **Material property `_HeightOffset` via SetFloat**
   - `new Material(shader)` does NOT apply shader property defaults — floats default to 0
   - Must explicitly call `mat.SetFloat()` from C#
   - Lesson: Always set material properties from code, don't rely on shader defaults

---

## Problems Encountered & Solutions

### Problem 1: Material shader properties not taking effect
**Symptom:** Changing `_HeightOffset` default in shader Properties block had no visual effect
**Root Cause:** `new Material(shader)` initializes float properties to 0, not the shader default
**Solution:** Explicitly set via `mat.SetFloat("_HeightOffset", value)` in C#

### Problem 2: Tessellation creates spikes on border mesh
**Symptom:** Massive jagged spikes when tessellation enabled on borders
**Root Cause:** Thin strip triangles (0.01 wide, many units long) tessellate very differently from terrain's regular grid. New tessellation vertices sample heightmap at wildly different positions.
**Solution:** Abandoned tessellation approach entirely

### Problem 3: SV_Depth doesn't move pixels visually
**Symptom:** Borders still rendered underground despite writing terrain depth via SV_Depth
**Root Cause:** SV_Depth only affects depth buffer operations (ZTest/ZWrite), not the rasterized screen position. Visual position is determined solely by vertex shader output.
**Solution:** Abandoned depth buffer approach

---

## Architecture Impact

### Key Insight
**Two independently tessellated meshes sampling the same heightmap will NOT produce matching surfaces.** Tessellation depends on input triangle shape, size, and orientation — not just the displacement function. This is a fundamental geometric property, not a bug.

### Shader State
```
Queue: Transparent-100 (after terrain + depth copy)
ZTest: Always
ZWrite: Off
Blend: Off
Vertex: heightmap sampling for approximate Y positioning
Fragment: solid black
```

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Border mesh CANNOT perfectly match tessellated terrain — different geometry = different tessellation
- ZTest Always is the correct approach for overlaying borders on tessellated terrain
- GPU heightmap sampling in vertex shader for approximate height positioning
- Polyline subdivision (3px) prevents background bleed-through with ZTest Always
- Material float properties default to 0, not shader defaults — always use SetFloat

**Gotchas:**
- Don't try tessellation on border mesh — produces catastrophic spikes
- Don't try SV_Depth for visual positioning — it only affects depth buffer
- Don't try constant height offsets — impossible to find value that works everywhere
- URP depth texture must be enabled in pipeline settings if needed

**What's Still in the Shader:**
- `_HeightmapTexture`, `_HeightScale`, `_MapWorldBounds` — for vertex height positioning
- `_BorderTex` — for future texture sampling
- `TRANSFORM_TEX` on heightmap UV — matches terrain shader

### Code References
- Shader: `Shaders/BorderMesh.shader`
- Mesh generator: `Scripts/Map/Rendering/Border/BorderMeshGenerator.cs`
- Mesh renderer: `Scripts/Map/Rendering/Border/BorderMeshRenderer.cs`
- Integration: `Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs`
- Imperator shaders: `imperator-investigation/1102_vertex.txt`, `imperator-investigation/1102_pixel.txt`

---

## Next Session

### Immediate Next Steps
1. Design custom border texture for mesh strip UV layout (Paradox texture not suited)
2. Country border classification (currently all Province type)
3. Border width/style differentiation (country vs province)
4. Clean up unused CPU heightmap code from BorderMeshGenerator (optional, still functional)

### Related Sessions
- [01-border-junction-caps-terrain-following.md](../09/01-border-junction-caps-terrain-following.md)
- [02-mesh-border-geometry-fixes.md](../08/02-mesh-border-geometry-fixes.md)
- [01-gpu-border-extraction-pipeline.md](../08/01-gpu-border-extraction-pipeline.md)

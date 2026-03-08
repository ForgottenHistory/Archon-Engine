# GPU-Accelerated Border Extraction Pipeline
**Date**: 2026-03-08
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Replace CPU-heavy border curve extraction (~7.2s) with GPU compute pipeline + disk caching for mesh geometry borders

**Secondary Objectives:**
- Fix mesh geometry border positioning (terrain-following, coordinate mapping)
- Support scaling to 100M pixel maps

**Success Criteria:**
- Border extraction under 500ms on cache hit
- Visual parity with CPU path
- Disk cache for instant subsequent loads

---

## Context & Background

**Previous Work:**
- Border rendering refactored to use IBorderRenderer with local dictionary registry
- Pixel-perfect borders working via PixelPerfectBorderRenderer
- Mesh geometry borders had wrong positions (black blob), fixed coordinate mapping and terrain-following

**Current State:**
- `BorderCurveExtractor.ExtractAllBorders()` took ~7.2s on 11.5M pixel map
- Bottlenecks: JunctionDetector (3.5s, HashSet per pixel), per-border extraction (2.4s), median filter
- Goal is 100M pixel maps, so CPU path would take ~60s+

**Why Now:**
- Mesh geometry borders are the highest quality border mode but unusable due to startup time

---

## What We Did

### 1. GPU Compute Shader: BorderExtractionPipeline.compute
**Files Changed:** `Assets/Archon-Engine/Resources/Shaders/BorderExtractionPipeline.compute` (new)

Three kernels:
- **MedianFilter** - 3x3 mode filter on province IDs (currently unused - see Decisions)
- **DetectBorderPixels** - Finds border pixels via 8-connectivity, emits `(position, provincePair)` to AppendStructuredBuffer. One-sided emission (only from smaller province ID side) to match CPU chaining behavior.
- **DetectJunctions** - Finds pixels where 3+ provinces meet in 3x3 window

**Key design:** One-sided emission ensures border pixels come from a single consistent side of each border, enabling clean pixel chaining without cross-border zigzag artifacts.

### 2. GPU Orchestrator: GPUBorderExtractor.cs
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/Border/GPUBorderExtractor.cs` (new)

Static class following `GPUProvinceNeighborDetector` patterns:
- AppendStructuredBuffer with CopyCount readback
- `uint2[]` readback matching struct layout
- Groups border pixels by province pair into `Dictionary<(ushort,ushort), List<Vector2>>`
- Binary disk cache: `provinces.png.borders` alongside source image in Template-Data/map/

**Cache format:** `[BRDR 4B][version 4B][pairCount 4B][junctionCount 4B][pairs...][junctions...]`

### 3. GPU Fast Path in BorderCurveExtractor
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/Border/BorderCurveExtractor.cs:372-490`

Added `ExtractAllBordersFromGPUResult()` - takes pre-extracted GPU data, performs only CPU-side:
- Pixel chaining (MedianFilterProcessor.ChainBorderPixelsMultiple)
- RDP simplification (BorderPolylineSimplifier)
- Chaikin smoothing
- Tessellation
- Junction endpoint snapping

### 4. Integration in MeshGeometryBorderRenderer
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs:88-162`

3-tier fallback in `ExtractBorderCurvesWithGPU()`:
1. Disk cache (fastest: ~28ms load + ~292ms chaining)
2. GPU compute extraction (~36ms) + save cache
3. CPU fallback (~7.2s)

Cache stored at `Assets/Archon-Engine/Template-Data/map/provinces.png.borders`

---

## Decisions Made

### Decision 1: Skip GPU Median Filter
**Context:** GPU median filter produced different boundary positions than CPU, causing only 1129/12753 border pairs to be found
**Decision:** Use original province ID texture directly for GPU border detection
**Rationale:** The CPU chaining/smoothing pipeline handles noise adequately via RDP simplification and Chaikin smoothing. Median filter shifts boundaries causing pair mismatches between GPU detection and adjacency system data.
**Trade-offs:** Slightly noisier raw border pixels, but indistinguishable after smoothing

### Decision 2: One-Sided Border Pixel Emission
**Context:** Initial GPU implementation emitted border pixels from both sides of each border, causing chaining to produce erratic cross-border zigzag paths
**Decision:** Only emit pixels where `currentProvince < neighborProvince` (matching CPU's per-province-A iteration)
**Rationale:** Chaining algorithms need spatially coherent pixels from one side to produce clean polylines

### Decision 3: Cache Location
**Context:** Initially used `Application.persistentDataPath/BorderCache/`
**Decision:** Store as `provinces.png.borders` in `Template-Data/map/` alongside source image
**Rationale:** Follows existing pattern (`provinces.png.adjacency`), enables staleness check against source image modification time

---

## What Worked

1. **AppendStructuredBuffer pattern from GPUProvinceNeighborDetector**
   - Proven pattern for sparse GPU output with unknown count
   - CopyCount readback, uint2[] matching struct layout
   - Reusable pattern: Yes

2. **3-tier fallback (cache → GPU → CPU)**
   - Graceful degradation if GPU unavailable
   - Cache provides near-instant loads on subsequent runs
   - Reusable pattern: Yes

---

## What Didn't Work

1. **GPU Median Filter**
   - What we tried: 3x3 mode filter on GPU writing to intermediate RenderTexture
   - Why it failed: Province boundary positions shifted differently than CPU Burst median filter, causing border pair count to drop from 12753 to 1129
   - Lesson: GPU and CPU median filters need identical behavior for downstream pipeline compatibility
   - Don't try this again because: The smoothing pipeline handles noise fine without it

2. **Two-sided border pixel emission**
   - What we tried: GPU emits current pixel position for any different neighbor (both sides of border)
   - Why it failed: Chaining algorithm produces zigzag paths crossing the border when pixels from both sides are mixed
   - Lesson: Border chaining requires spatially coherent one-sided pixel sets

---

## Problems Encountered & Solutions

### Problem 1: Mesh borders as black blob / wrong positions
**Symptom:** Borders rendered as black mass in center of map
**Root Cause:** Coordinate mapping in BorderMeshGenerator used hardcoded 10x10 with flipped X instead of proper UV mapping
**Solution:** `BorderMeshGenerator.cs` - pixel→UV→local space matching ProvinceCenterLookup pattern:
```csharp
float uvX = p.x / mapWidth;
float uvY = 1f - (p.y / mapHeight);
float x = -5f + uvX * 10f;
float z = -5f + uvY * 10f;
```

### Problem 2: Borders inside terrain
**Symptom:** Borders not visible (hidden inside tessellated terrain)
**Root Cause:** Borders at Y=0 while terrain had actual height from tessellation
**Solution:** Added heightmap sampling to `BorderMesh.shader` vertex shader, reading `_HeightScale` from map material

### Problem 3: GPU finding only 1129/12753 border pairs
**Symptom:** Most borders missing, remaining ones had 44M vertices
**Root Cause:** GPU median filter shifted boundaries differently than CPU, producing different province assignments at borders
**Solution:** Skipped GPU median filter entirely, use original province ID texture

### Problem 4: Border lines going across map
**Symptom:** Connection points not connecting properly, lines spanning entire map
**Root Cause:** GPU emitted border pixels from both sides of each border, chaining mixed pixels from different provinces
**Solution:** One-sided emission: `if (currentProvince > neighborProvince) continue;`

---

## Code Quality Notes

### Performance
- **CPU path (old):** ~7,200ms extraction
- **GPU first run:** ~36ms GPU + ~292ms chaining = ~328ms extraction
- **Cache hit:** ~28ms load + ~292ms chaining = ~320ms extraction
- **Total border init (cache hit):** 643ms (including mesh generation 216ms)
- **Speedup:** ~22x on extraction, ~11x total border init
- **Status:** ✅ Meets target

### Mesh Stats (cache hit, 5632x2048 map)
- 12,753 border pairs processed
- 1,648,914 vertices in 26 meshes (vs 44M/831 with broken GPU median filter)
- 41,940 junction pixels
- Cache file: 2,459 KB

---

## Next Session

### Immediate Next Steps
1. Test with larger maps to validate scaling
2. Consider re-enabling GPU median filter with proper encode/decode validation
3. Country border classification (currently all borders classified as Province)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- GPU border pipeline: `BorderExtractionPipeline.compute` → `GPUBorderExtractor.cs` → `BorderCurveExtractor.ExtractAllBordersFromGPUResult()` → `MeshGeometryBorderRenderer.ExtractBorderCurvesWithGPU()`
- Cache at `Template-Data/map/provinces.png.borders`
- GPU median filter exists but is bypassed - causes boundary shift issues
- One-sided emission critical for chaining: `currentProvince < neighborProvince`

**Gotchas for Next Session:**
- Don't re-enable GPU median filter without verifying identical output to CPU Burst version
- `JunctionDetector(width, height, null)` is safe for `SnapPolylineEndpointsAtJunctions` only (doesn't access pixel data)
- BorderMeshGenerator mesh splitting at 65k vertex boundary is handled during generation loop

### Code References
- Compute shader: `Assets/Archon-Engine/Resources/Shaders/BorderExtractionPipeline.compute`
- GPU orchestrator: `Assets/Archon-Engine/Scripts/Map/Rendering/Border/GPUBorderExtractor.cs`
- GPU fast path: `Assets/Archon-Engine/Scripts/Map/Rendering/Border/BorderCurveExtractor.cs:372-490`
- Integration: `Assets/Archon-Engine/Scripts/Map/Rendering/Border/Implementations/MeshGeometryBorderRenderer.cs:88-162`
- Border mesh shader: `Assets/Archon-Engine/Shaders/BorderMesh.shader`

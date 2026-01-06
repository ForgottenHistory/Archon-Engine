# Spatial Acceleration for Vector Curve Rendering
**Date**: 2025-10-26
**Session**: 7
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement spatial hash grid to prevent GPU timeout from testing 583k Bézier segments per pixel

**Secondary Objectives:**
- Enable vector curve rendering in production (currently disabled due to TDR)
- Maintain smooth curve quality while achieving practical performance
- Fix struct layout bugs preventing borders from rendering

**Success Criteria:**
- GPU doesn't timeout (TDR) when vector curves enabled
- Borders render with correct types (country vs province)
- Performance remains >100 FPS at 50x speed zoom
- Smooth anti-aliased curves at all zoom levels

---

## Context & Background

**Previous Work:**
- Session 6: [6-vector-curve-borders.md](6-vector-curve-borders.md) - Implemented Bézier curve fitting and GPU rendering
- Result: Perfect smooth curves, but DISABLED due to GPU timeout
- Problem: Testing all 583k segments for every pixel = billions of calculations → GPU hung

**Current State:**
- Vector curves implemented but `_UseVectorCurves = 0` (disabled)
- Shader tests O(all segments) per pixel → GPU TDR (timeout detection and recovery)
- Borders falling back to rasterized BorderMask texture

**Why Now:**
- Vector curves unusable in current state
- Need spatial acceleration to make it O(nearby segments) instead of O(all segments)
- This is blocking production use of smooth borders

---

## What We Did

### 1. Spatial Hash Grid Data Structure
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/SpatialHashGrid.cs` (NEW, 180 lines)

**Implementation:**
```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct CellRange
{
    public uint startIndex;  // Start index in flatSegmentIndices
    public uint count;       // Number of segments in this cell
}

public class SpatialHashGrid
{
    private List<uint>[] cellSegments;  // Per-cell segment lists
    private CellRange[] cellRanges;     // GPU-ready range data
    private uint[] flatSegmentIndices;  // Flattened segment indices

    public void AddSegment(uint segmentIndex, BezierSegment segment)
    {
        // Calculate AABB with 3px margin
        // Add segment to all intersecting cells
    }

    public void Finalize()
    {
        // Flatten cell lists into contiguous GPU buffer
    }
}
```

**Rationale:**
- Uniform grid: 64px cells (88×32 grid for 5632×2048 map)
- Segments stored in cells they intersect (AABB test with margin)
- Flattened storage for GPU access (CellRange points into flat array)

**Performance:**
- 2,816 cells total
- 705,410 segment references (~250 per cell average)
- Built in 105ms at initialization
- ~2300x reduction in segments tested per pixel

**Architecture Compliance:**
- ✅ Follows GPU optimization patterns (pre-allocated buffers, structured data)
- ✅ One-time CPU cost at init, zero runtime CPU overhead

### 2. GPU Buffer Creation and Upload
**Files Changed:** `BorderCurveRenderer.cs:120-165`

**Implementation:**
```csharp
private void BuildSpatialGrid(List<BezierSegment> allSegments)
{
    spatialGrid = new SpatialHashGrid(textureManager.MapWidth, textureManager.MapHeight, cellSize: 64);

    for (int i = 0; i < allSegments.Count; i++)
        spatialGrid.AddSegment((uint)i, allSegments[i]);

    spatialGrid.Finalize();

    // Upload to GPU buffers
    var cellRanges = spatialGrid.GetCellRanges();
    var segmentIndices = spatialGrid.GetFlatSegmentIndices();

    gridCellRangesBuffer = new ComputeBuffer(cellRanges.Length, sizeof(uint) * 2);
    gridCellRangesBuffer.SetData(cellRanges);

    gridSegmentIndicesBuffer = new ComputeBuffer(segmentIndices.Length, sizeof(uint));
    gridSegmentIndicesBuffer.SetData(segmentIndices);
}
```

**Rationale:**
- CellRange struct: 2 uints = 8 bytes (GPU-aligned)
- Segment indices: uint array (stride 4 bytes, GPU-compatible)
- Buffers bound once at initialization, used every frame

### 3. Shader Spatial Lookup
**Files Changed:** `MapModeCommon.hlsl:220-318`

**Implementation:**
```hlsl
float4 ApplyBordersVectorCurvesSpatial(
    float4 baseColor, float2 uv,
    StructuredBuffer<BezierSegment> bezierSegments,
    StructuredBuffer<uint2> gridCellRanges,
    StructuredBuffer<uint> gridSegmentIndices,
    int gridWidth, int gridHeight, int cellSize, float2 mapSize)
{
    // Early-out optimization
    float borderMask = SAMPLE_TEXTURE2D(_BorderMaskTexture, sampler_BorderMaskTexture, correctedUV).r;
    if (borderMask < 0.01) return baseColor;

    // Convert UV to map coordinates
    float2 mapPos = correctedUV * mapSize;

    // Find which grid cell we're in
    int cellX = (int)(mapPos.x / (float)cellSize);
    int cellY = (int)(mapPos.y / (float)cellSize);
    int cellIdx = cellY * gridWidth + cellX;

    // Get segment range for this cell
    uint2 cellRange = gridCellRanges[cellIdx];
    uint startIdx = cellRange.x;
    uint count = cellRange.y;

    // Test ONLY segments in this cell (~250 instead of 583k!)
    float minDistance = 999999.0;
    int closestBorderType = 0;

    for (uint i = 0; i < count; i++)
    {
        uint segIdx = gridSegmentIndices[startIdx + i];
        BezierSegment seg = bezierSegments[segIdx];

        float dist = DistanceToBezier(mapPos, seg);
        if (dist < minDistance)
        {
            minDistance = dist;
            closestBorderType = seg.borderType;
        }
    }

    // Apply borders with minimal anti-aliasing
    if (minDistance <= 1.5)
    {
        float borderAlpha = 1.0 - smoothstep(1.0, 1.5, minDistance);

        if (closestBorderType == 2) // Country
            baseColor.rgb = lerp(baseColor.rgb, _CountryBorderColor.rgb, borderAlpha * _CountryBorderStrength);
        else if (closestBorderType == 1) // Province
            baseColor.rgb = lerp(baseColor.rgb, _ProvinceBorderColor.rgb, borderAlpha * _ProvinceBorderStrength);
    }

    return baseColor;
}
```

**Rationale:**
- O(segments_in_cell) instead of O(all_segments)
- BorderMask early-out skips ~90% of pixels
- Only 0.5px fade for sharp borders with minimal AA

### 4. Struct Layout Bug Fixes
**Files Changed:**
- `BezierCurveFitter.cs:10-25`
- `BorderCurveRenderer.cs:120-122`

**Problem 1: GPU Buffer Alignment**
- Error: `Invalid stride 2 for Compute Buffer`
- Cause: Used `ushort` (2 bytes), GPU requires stride multiples of 4
- Fix: Changed to `uint` (4 bytes) everywhere

**Problem 2: CellRange Struct Padding**
- Error: `SetData(): Accessing 2821640 bytes at offset 0 for Buffer of size 1410820 bytes`
- Cause: C# adding implicit padding, struct interpreted as 16 bytes instead of 8
- Fix: Added `[StructLayout(Sequential)]` attribute

**Problem 3: BezierSegment Memory Layout Mismatch**
- Symptom: Borders rendering but `borderType` always reads as 0
- Cause: C# had `ushort provinceID1, provinceID2` (2 bytes each), HLSL expected `uint` (4 bytes each)
- Memory layout mismatch caused `borderType` to read wrong offset

**Solution:**
```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct BezierSegment
{
    public Vector2 P0, P1, P2, P3;  // 32 bytes
    public BorderType borderType;   // 4 bytes (int)
    public uint provinceID1;        // 4 bytes (CHANGED from ushort)
    public uint provinceID2;        // 4 bytes (CHANGED from ushort)
}
// Total: 44 bytes, matches HLSL exactly
```

**Buffer stride fix:**
```csharp
// OLD: sizeof(float) * 8 + sizeof(int) + sizeof(ushort) * 2 = 40 bytes ❌
// NEW: sizeof(float) * 8 + sizeof(int) + sizeof(uint) * 2 = 44 bytes ✅
int segmentStride = sizeof(float) * 8 + sizeof(int) + sizeof(uint) * 2;
```

### 5. Border Debug Map Mode Update
**Files Changed:** `EU3MapShader.shader:273-329`

**Implementation:**
- Added spatial acceleration to Border Debug mode
- Added `borderMask` early-out optimization
- Shows red (country) and green (province) borders using vector curves
- Falls back to yellow BorderMask if vector curves unavailable

---

## Decisions Made

### Decision 1: Uniform Grid vs Quadtree/BVH
**Context:** Need spatial acceleration structure for 583k segments

**Options Considered:**
1. **Uniform Grid (64px cells)** - Simple, predictable, GPU-friendly
2. **Quadtree** - Adaptive, better for uneven distribution
3. **BVH (Bounding Volume Hierarchy)** - Optimal for ray tracing

**Decision:** Chose Uniform Grid

**Rationale:**
- Simplest to implement and debug
- Predictable memory layout (GPU-friendly)
- O(1) cell lookup (no tree traversal)
- Good enough for our use case (segments roughly evenly distributed)

**Trade-offs:**
- Some cells have 0 segments (wasted memory)
- Some cells have many segments (worse case performance)
- Fixed resolution (doesn't adapt to density)

**Future Optimization:** Could upgrade to hierarchical grid later if needed

### Decision 2: Cell Size (64px)
**Context:** Need to choose optimal grid cell size

**Options Considered:**
1. 32px cells - More cells, fewer segments per cell
2. 64px cells - Balanced
3. 128px cells - Fewer cells, more segments per cell

**Decision:** Chose 64px cells

**Rationale:**
- Average ~250 segments per cell (manageable)
- 2,816 total cells (reasonable memory)
- 3px margin captures nearby segments

**Measured Results:**
- 705,410 total segment references
- 88×32 grid dimensions
- Built in 105ms

### Decision 3: Sharp Borders (Minimal AA)
**Context:** User requested "tune down the fading A LOT"

**Decision:** Reduced from 2px smooth fade to 0.5px minimal AA
- Solid borders from 0-1px
- Fade only in last 0.5px (smoothstep 1.0→1.5)

**Rationale:**
- User preference for sharp borders
- Minimal AA prevents aliasing artifacts
- Maintains visual quality at high zoom

### Decision 4: uint Province IDs (Breaking Change)
**Context:** Struct layout mismatch required fix

**Decision:** Changed `ushort provinceID1/2` to `uint` to match HLSL

**Trade-offs:**
- +8 bytes per segment (583k segments = 4.6MB more memory)
- Breaking change for any code using BezierSegment
- REQUIRED for GPU struct alignment

**Why Worth It:**
- Fixes critical rendering bug
- Simplifies GPU layout (no padding issues)
- 4.6MB negligible for modern GPUs

---

## What Worked ✅

1. **Debug-Driven Development**
   - Progressive color visualization (magenta → green → yellow gradient → types)
   - Each step isolated which component was working/broken
   - Found struct layout bug quickly

2. **Spatial Hash Grid Pattern**
   - Reduced from 583k to ~250 segments per pixel
   - Simple uniform grid sufficient (no need for complex structures)
   - Flattened storage perfect for GPU access

3. **BorderMask Early-Out**
   - Skips ~90% of pixels immediately
   - Critical for maintaining >100 FPS
   - Simple texture sample vs expensive curve testing

4. **Explicit Struct Layout Attributes**
   - `[StructLayout(Sequential)]` prevents C# padding issues
   - Documenting byte offsets in comments caught the bug
   - CRITICAL for CPU-GPU data transfer

---

## What Didn't Work ❌

1. **Initial ushort Usage**
   - Tried: Use `ushort` for province IDs and segment indices (save memory)
   - Failed: GPU requires 4-byte alignment, caused multiple stride errors
   - Lesson: Always use `uint` for GPU buffers (memory savings not worth complexity)

2. **Assuming Struct Layout Matches**
   - Tried: Trust that C# and HLSL struct layouts match automatically
   - Failed: C# added padding, province IDs used different sizes
   - Lesson: ALWAYS use explicit `[StructLayout(Sequential)]` and document byte offsets

3. **Wide Anti-Aliasing (2px fade)**
   - Tried: Smooth 2px fade for nice anti-aliasing
   - Failed: User found it too blurry, wanted sharp borders
   - Lesson: Paradox games use minimal AA, sharp borders are the aesthetic

---

## Problems Encountered & Solutions

### Problem 1: GPU Timeout (TDR)
**Symptom:** GPU hung, driver reset, Unity froze
**Root Cause:** Testing 583,005 segments for every pixel = billions of calculations
**Investigation:**
- Profiled: Vector curves disabled → 250 FPS, enabled → GPU timeout
- Math: 5632×2048 pixels × 583k segments = 6.7 trillion tests per frame

**Solution:** Spatial hash grid
```csharp
// Reduce from O(all segments) to O(segments in cell)
// 583k → ~250 segments tested per pixel
// ~2300x performance improvement
```

**Pattern for Future:**
- NEVER iterate all objects for every pixel
- Use spatial acceleration for anything that scales with object count
- GPU has massive parallelism but limited time budget per thread

### Problem 2: Struct Layout Mismatch (borderType Always 0)
**Symptom:** Borders rendering but always magenta (type 0), not red/green
**Root Cause:** Memory layout mismatch between C# and HLSL

**Investigation:**
- Added debug colors: All magenta (borderType = 0)
- C# logs showed: "Type1(Province)=9010, Type2(Country)=2371" (correct data)
- Read HLSL struct: Expected `int borderType; uint provinceID1; uint provinceID2`
- Read C# struct: Had `BorderType borderType; ushort provinceID1; ushort provinceID2`

**C# Layout:**
```
Vector2 P0-P3: 32 bytes
BorderType: 4 bytes (offset 32)
[padding]: 2 bytes (offset 36) ← C# added this!
ushort provinceID1: 2 bytes (offset 38)
ushort provinceID2: 2 bytes (offset 40)
Total: 42 bytes
```

**HLSL Expected:**
```
float2 P0-P3: 32 bytes
int borderType: 4 bytes (offset 32)
uint provinceID1: 4 bytes (offset 36) ← reading padding instead!
uint provinceID2: 4 bytes (offset 40)
Total: 44 bytes
```

**Solution:**
```csharp
// Change ushort → uint to match HLSL exactly
public uint provinceID1; // 4 bytes
public uint provinceID2; // 4 bytes

// Update buffer stride
int segmentStride = sizeof(float) * 8 + sizeof(int) + sizeof(uint) * 2; // 44 bytes
```

**Why This Works:** Memory layouts now identical (44 bytes, same offsets)

**Pattern for Future:**
- ALWAYS use `[StructLayout(Sequential)]` for GPU structs
- ALWAYS use `uint` not `ushort` for GPU data
- Document byte offsets in comments
- Test struct size matches: `sizeof(C#) == HLSL size`

### Problem 3: Buffer Stride Errors
**Symptom:** `Invalid stride 2` and `SetData size mismatch` errors
**Root Cause:** GPU buffers require stride multiples of 4

**Solution:**
- Changed `List<ushort>` → `List<uint>`
- Changed `ushort[]` → `uint[]`
- Added explicit stride calculation with `sizeof(uint)`

**Pattern for Future:** Always use 4-byte types for GPU buffers (uint, float, int)

---

## Architecture Impact

### Performance Characteristics
**Measured Results:**
- **5x speed:** 200-220 FPS (excellent)
- **50x speed:** 100-120 FPS (acceptable, debug mode only)
- **Cost:** 2x vs rasterized borders (250 FPS → 120 FPS)

**Why 50x Costs More:**
- More provinces visible = more borders on screen
- Zoomed in: ~10% of pixels are borders
- Zoomed out: ~30% of pixels are borders
- 3x more border pixels = proportional performance cost

**Still Excellent:** 100+ FPS with smooth vector curves at any zoom is a huge win

### Future Optimization Opportunities
1. **Early termination** (Easy, ~10-20% gain) - Stop testing when exact hit found
2. **Bounding box culling** (Medium, ~30% gain) - Skip segments outside AABB
3. **LOD system** (Hard, ~50% gain) - Simplified segments at far zoom
4. **Hierarchical grid** (Hard, ~40% gain) - Quadtree with coarse→fine culling
5. **Separate country/province passes** (Medium, ~25% gain) - Reduce tests per pixel
6. **Distance field texture** (Very Hard, ~80% gain) - Pre-compute distances, huge memory cost
7. **Compute shader preprocessing** (Medium, ~30% gain) - Render borders to texture once per frame

**Recommendation:** Ship current version (100-120 FPS is excellent). Only optimize if needed.

### New Patterns Discovered

**Pattern: Progressive Color Debugging**
- When to use: GPU shader debugging (RenderDoc too complex)
- How: Add debug colors that visualize data at each step
  - Magenta: Function being called?
  - Green: Spatial cells detected?
  - Yellow gradient: Distance calculations working?
  - Red/Blue/Magenta: Data values correct?
- Benefits: Isolates exactly which stage is broken
- Add to: GPU debugging best practices doc

**Pattern: Explicit Struct Layout Documentation**
- When to use: ANY struct transferred between CPU and GPU
- How: Document byte offsets in comments, use `[StructLayout(Sequential)]`
```csharp
[StructLayout(Sequential)]
public struct GPUData
{
    public Vector2 P0;  // 8 bytes, offset 0
    public int value;   // 4 bytes, offset 8
    public uint id;     // 4 bytes, offset 12
}
// Total: 16 bytes
```
- Benefits: Catches layout bugs immediately, self-documenting
- Add to: GPU programming patterns doc

### Anti-Patterns Confirmed

**Anti-Pattern: ushort for GPU Data**
- What not to do: Use `ushort` or `byte` to save memory in GPU buffers
- Why it's bad: GPU requires 4-byte alignment, causes stride errors and padding issues
- Always use: `uint`, `int`, `float` (4 bytes)
- Exception: ONLY if tightly packing in explicit struct with no padding

**Anti-Pattern: Assuming Struct Layouts Match**
- What not to do: Assume C# and HLSL struct layouts are identical
- Why it's bad: C# adds implicit padding, enum sizes differ, alignment rules vary
- Always: Use `[StructLayout(Sequential)]` and verify byte offsets

---

## Code Quality Notes

### Performance
**Measured:**
- 5x speed: 200-220 FPS
- 50x speed: 100-120 FPS
- Spatial grid build: 105ms (one-time at init)

**Target:** >60 FPS minimum (design target for strategy game)
**Status:** ✅ Exceeds target by 2-4x

### Testing
**Manual Tests Performed:**
- Vector curves render correctly (smooth, not jagged)
- Border types correct (red country, green province)
- Performance stable across zoom levels
- Border Debug mode shows correct visualization
- No GPU timeouts at any zoom/speed

**Validation:**
- Struct size: 44 bytes C# matches HLSL
- Buffer strides: All multiples of 4
- Grid coverage: All segments added to appropriate cells
- Early-out: BorderMask skips non-border pixels

### Technical Debt
**Created:**
- None - this completes the vector curve system

**Paid Down:**
- Fixed struct layout bugs from Session 6
- Enabled vector curves in production (was disabled)
- Optimized Border Debug mode (was unusable)

**TODOs:**
- Consider future optimizations if performance becomes issue
- Potential to add LOD system for far zoom
- Could implement hierarchical grid if needed

---

## Next Session

### Immediate Next Steps
1. Git commit: Spatial acceleration complete, vector curves production-ready
2. Test in extended gameplay - verify performance remains stable
3. Consider visual polish (border colors, thickness, anti-aliasing tuning)

### Future Enhancements (Not Urgent)
- LOD system for far zoom (if performance becomes issue)
- Hierarchical spatial grid (if needed)
- Bounding box culling (easy optimization)
- Early termination in distance loop

### Questions Resolved
- ✅ Can vector curves work without GPU timeout? YES with spatial grid
- ✅ What's acceptable performance? 100-120 FPS is excellent
- ✅ How sharp should borders be? Minimal AA (0.5px fade)
- ✅ Can we optimize more? Yes, but current performance is sufficient

---

## Session Statistics

**Files Changed:** 5
- `SpatialHashGrid.cs` (NEW, 180 lines)
- `BorderCurveRenderer.cs` (modified, spatial grid integration)
- `BorderComputeDispatcher.cs` (modified, public API)
- `BezierCurveFitter.cs` (modified, struct layout fix)
- `MapModeCommon.hlsl` (modified, spatial shader)
- `EU3MapShader.shader` (modified, Border Debug mode)
- `VisualStyleManager.cs` (modified, buffer binding)

**Lines Added/Removed:** ~+400/-50
**Bugs Fixed:** 4 (GPU timeout, struct layouts, buffer strides, Border Debug)
**Performance:** 2x cost vs rasterized, but 100+ FPS maintained

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Vector curves NOW ENABLED in production with spatial acceleration
- Spatial grid: 88×32 cells (64px), ~250 segments per cell, built in 105ms
- Performance: 200+ FPS at 5x, 100-120 FPS at 50x (excellent)
- Struct layout: MUST use `[StructLayout(Sequential)]` and `uint` for GPU data

**Critical Implementation:**
- Spatial lookup: `MapModeCommon.hlsl:220-318`
- Grid structure: `SpatialHashGrid.cs:1-180`
- Struct fix: `BezierCurveFitter.cs:14-25` (uint province IDs, explicit layout)
- Buffer binding: `VisualStyleManager.cs:BindVectorCurveBuffer()`

**Gotchas for Next Session:**
- Any new GPU structs MUST use `[StructLayout(Sequential)]` and document byte offsets
- Always use `uint` not `ushort` for GPU buffer data
- BorderMask early-out is critical for performance (don't remove it)
- 64px cell size is tuned for current segment density (don't change without testing)

**Performance Budget:**
- Vector curves cost ~2x vs rasterized borders
- BorderMask early-out skips ~90% of pixels (CRITICAL)
- 100-120 FPS at 50x speed is acceptable for debug mode
- 200+ FPS at normal zoom is excellent

---

## Links & References

### Related Documentation
- [master-architecture-document.md](../../Engine/master-architecture-document.md) - GPU optimization patterns
- [dual-layer-architecture.md](../../Engine/dual-layer-architecture.md) - CPU/GPU separation

### Related Sessions
- [6-vector-curve-borders.md](6-vector-curve-borders.md) - Previous session (implemented curves, disabled due to timeout)
- [5-resolution-independent-borders.md](5-resolution-independent-borders.md) - Thin border shader system

### Code References
- Spatial grid: `SpatialHashGrid.cs:1-180`
- Grid integration: `BorderCurveRenderer.cs:120-165`
- Spatial shader: `MapModeCommon.hlsl:220-318`
- Struct definition: `BezierCurveFitter.cs:10-25`
- Buffer binding: `VisualStyleManager.cs:BindVectorCurveBuffer()`
- Border Debug: `EU3MapShader.shader:273-329`

---

## Notes & Observations

**This was a journey:**
1. Started: Vector curves exist but disabled (GPU timeout)
2. Planned: Uniform spatial grid (64px cells)
3. Implemented: Grid building, GPU upload, shader lookup
4. BLOCKED: Multiple struct layout bugs (3 rounds of fixes)
5. Success: Smooth vector borders at 100+ FPS! 🎉

**Key insight:** GPU struct layout debugging is PAINFUL without progressive color visualization. Adding debug colors at each stage (magenta → green → yellow → types) isolated bugs immediately.

**The win:** From GPU timeout → 100-120 FPS with smooth resolution-independent borders. Spatial acceleration worked perfectly. 2300x reduction in segments tested per pixel.

**User reaction:** "Yeah, dude. its curved. I can see it. wow."

**Achievement unlocked:** Production-ready vector curve border rendering with spatial acceleration! ✅

---

*Session completed 2025-10-26*
*Vector curves ENABLED and production-ready*
*Performance: 100-220 FPS depending on zoom*

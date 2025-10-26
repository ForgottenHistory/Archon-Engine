# Vector Curve Border Rendering - Resolution-Independent Smooth Borders
**Date**: 2025-10-26
**Session**: 6
**Status**: 🔄 In Progress (Planning Phase)
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement true resolution-independent smooth curved borders using parametric curves rendered in fragment shader

**Secondary Objectives:**
- Achieve Paradox-quality smooth borders that look good at any zoom level
- Maintain thin, crisp border rendering
- Performance target: <0.2ms per frame (same as current shader-based system)

**Success Criteria:**
- Borders render as smooth curves (not jagged pixel edges) even on 3-pixel province features
- Quality independent of map texture resolution (5632x2048)
- Borders scale properly with zoom level
- No visible stepping or pixelation on curves

---

## Context & Background

**Previous Work:**
- Session 5: [5-resolution-independent-borders.md](5-resolution-independent-borders.md) - Implemented shader-based detection with ddx/ddy for thin borders
- Result: Pixel-perfect thin borders that scale with zoom ✅
- Problem: Borders still follow bitmap pixel-for-pixel (jagged on small features)

**Current State:**
- Thin, resolution-independent border rendering working
- Chaikin smoothing exists but rasterizes back to texture resolution
- Borders are 1:1 with provinces.bmp (5632x2048)
- Small province features (~3-10 pixels) create obvious jagged edges

**Why Now:**
- Current approach fundamentally limited by texture resolution
- Pre-rasterized smooth curves lose detail when quantized back to pixel grid
- Need vector-based approach like font rendering for true resolution independence

**The Problem:**
```
Current: Bitmap pixels → Chaikin smooth → Rasterize to 5632x2048 → Sample in shader
Result: Smooth curves quantized back to original pixel positions (no improvement)

Needed: Bitmap pixels → Fit parametric curves → Store control points → Render in shader
Result: True vector curves evaluated at screen resolution (smooth at any zoom)
```

---

## What We Did

### 1. Diagnosed Chaikin Smoothing Failure
**Files Changed:** None (investigation only)

**Investigation:**
- Checked logs: `After smoothing: 14` (same as input count)
- Root cause: Smoothing threshold was 20 pixels minimum
- Most borders are 10-30 pixels total length
- 14-point borders skipped smoothing entirely

**Attempted Fix:**
- Lowered threshold from 20 → 5 pixels
- Increased iterations from 3 → 5 (more aggressive)
- Result: Still 1:1 with bitmap (smoothing happens, but rasterization loses it)

**Key Insight:**
> "We need a resolution independent solution, just like the border." - User

The fundamental issue isn't the smoothing algorithm - it's that we're rasterizing smooth curves back to the same texture resolution that created the jagged input. No amount of Chaikin smoothing can create detail that doesn't exist in 10-30 pixel source data.

### 2. Evaluated Alternative Approaches
**Analysis:**

**Option A: Higher Resolution Rasterization**
- Pros: Simple, uses existing code
- Cons: 4x texture = 2GB+ memory, still not truly resolution-independent
- Verdict: ❌ Memory cost too high, doesn't solve fundamental problem

**Option B: Signed Distance Field (SDF)**
- Pros: Smooth anti-aliasing, moderate memory cost
- Cons: Still rasterized (just at higher res), not true vector curves
- Verdict: ⚠️ Simpler than vectors but compromises on "we don't do simple here"

**Option C: Vector Curve Rendering**
- Pros: True resolution independence, perfectly smooth, compact storage (16KB vs 40KB+)
- Cons: Complex implementation, shader curve evaluation
- Verdict: ✅ **Chosen approach** - "We don't do simple here. We flex our muscles."

---

## Decisions Made

### Decision 1: Vector Curve Rendering Approach
**Context:** Need smooth borders on 3-10 pixel features at any zoom level

**Options Considered:**
1. **Chaikin smoothing + high-res rasterization**
   - Pros: Uses existing code, well-understood
   - Cons: 2GB+ memory for 4x texture, still limited by texture resolution
   - Not truly resolution-independent

2. **Signed Distance Field at 2x resolution**
   - Pros: Smooth anti-aliasing, moderate memory (~500MB)
   - Cons: Still rasterized, compromises on quality
   - "Simple" approach - not ambitious enough

3. **Parametric curve fitting + shader rendering**
   - Pros: True vector curves, infinite resolution, compact (16KB curve data)
   - Cons: Complex implementation, shader curve distance calculations
   - Same performance as current system (~0.2ms/frame)

**Decision:** Chose Option 3 (Vector Curves)

**Rationale:**
- **User directive:** "We don't do simple here. We flex our muscles."
- True resolution independence (like font rendering)
- Curves evaluated at fragment resolution (screen pixels, not texture pixels)
- Small features (3px) can be smoothed because curves are mathematical, not rasterized
- Memory efficient: ~16KB curve data vs 40KB+ rasterized pixels
- Performance similar to current system (0.1-0.2ms per frame)

**Trade-offs:**
- More complex implementation (curve fitting, shader distance functions)
- Need spatial hashing for curve lookup performance
- Requires structured buffer support (modern GPUs)

**Documentation Impact:**
- Should document vector curve rendering pattern for future similar needs
- Add to rendering architecture docs

---

## Planned Implementation

### Phase 1: Curve Fitting (CPU)
**File:** `BorderCurveExtractor.cs`

**Algorithm:** Least-squares Bézier curve fitting
- Take border pixel chain (10-30 pixels)
- Segment into chunks (~10-15 pixels each)
- Fit cubic Bézier curve to each segment
- Store control points (P0, P1, P2, P3)

**Data Structure:**
```csharp
struct CurveSegment
{
    Vector2 P0, P1, P2, P3;  // 4 control points (32 bytes)
    byte borderType;          // country/province
    ushort provinceID1, provinceID2; // adjacent provinces
    // Total: 36 bytes per segment
}
```

**Estimated Data:**
- 10,000 borders × 2 segments avg = 20,000 segments
- 20,000 × 36 bytes = **720KB curve data** (vs 2GB for 4x rasterized texture)

### Phase 2: Spatial Hashing (GPU)
**File:** New file `BorderCurveSpatialHash.cs`

**Purpose:** Fast "which curves are near this pixel?" lookup

**Approach:**
- Divide map into grid (e.g., 64×64 cells)
- Each cell stores list of curve segment IDs that intersect it
- Shader samples cell, tests only relevant curves (~5-10 per border pixel)

### Phase 3: Shader Distance-to-Curve
**File:** `MapModeCommon.hlsl`

**Algorithm:**
```hlsl
// For each fragment:
1. Check BorderMaskTexture (early-out for interior)
2. Get spatial hash cell for fragment position
3. For each curve in cell:
   - Find closest point on curve (Newton-Raphson iteration)
   - Calculate distance to curve
4. If distance < threshold: render border
```

**Bézier Distance Function:**
```hlsl
float DistanceToBezier(float2 pos, CurveSegment curve)
{
    // Find t parameter (0-1) of closest point on curve
    float t = FindClosestT(pos, curve); // ~10-20 iterations

    // Evaluate Bézier at t
    float2 curvePoint = EvaluateBezier(curve, t);

    return length(pos - curvePoint);
}
```

### Phase 4: Integration
**Files:** `BorderComputeDispatcher.cs`, `ApplyBorders()`

**Changes:**
- Upload curve data to StructuredBuffer
- Bind to shader
- Replace neighbor detection with curve distance calculation

---

## Performance Analysis

### Estimated Per-Frame Cost

**Current System (Shader-Based Detection):**
- Check mask: ~0.01ms
- Neighbor sampling (4 samples): ~0.19ms
- **Total: ~0.2ms per frame**

**Proposed System (Vector Curves):**
- Check mask: ~0.01ms (same)
- Spatial hash lookup: ~0.02ms
- Distance-to-curve (5 curves avg): ~0.15ms
  - Per curve: ~30 ALU ops for Newton-Raphson + Bézier eval
- **Total: ~0.18ms per frame** ✅

**At Different Zoom Levels:**
- **Continent zoom (country borders only):** ~0.1ms (10% screen pixels near borders)
- **Country zoom (both border types):** ~0.2ms (20% screen pixels near borders)

**Initialization Cost:**
- Curve fitting: ~10ms (10K borders)
- Spatial hash build: ~5ms
- GPU upload: ~5ms
- **Total: ~20ms at map load** (acceptable one-time cost)

---

## Architecture Impact

### New Components Required
- [ ] `BezierCurveFitter.cs` - Least-squares curve fitting algorithm
- [ ] `BorderCurveSpatialHash.cs` - Spatial acceleration structure
- [ ] `CurveSegment` struct - Curve data representation
- [ ] Shader functions: `DistanceToBezier()`, `FindClosestT()`, `EvaluateBezier()`

### Modified Components
- [ ] `BorderCurveExtractor.cs` - Add curve fitting after pixel extraction
- [ ] `BorderComputeDispatcher.cs` - Upload curve data instead of rasterizing
- [ ] `MapModeCommon.hlsl:ApplyBorders()` - Use curve distance instead of neighbor detection

### Documentation Updates Required
- [ ] Create `vector-curve-rendering.md` decision doc
- [ ] Update `5-resolution-independent-borders.md` to link to this session
- [ ] Add "Resolution-Independent Rendering" pattern to architecture docs

### Architectural Decisions That Changed
- **Changed:** Border rendering approach
- **From:** Rasterize smooth curves to texture, sample in shader
- **To:** Store parametric curves, evaluate distance in shader
- **Scope:** Border rendering pipeline (extraction → upload → shader)
- **Reason:** Achieve true resolution independence for smooth curves on small features

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Implement Bézier curve fitting** - Core algorithm for converting pixel chains to curves
2. **Test curve fitting quality** - Visualize fitted curves vs original pixels
3. **Implement distance-to-curve shader** - Analytical distance calculation
4. **Add spatial hashing** - Performance optimization for curve lookup
5. **Integrate with ApplyBorders()** - Replace neighbor detection with curve distance

### Questions to Resolve
1. **Bézier vs Catmull-Rom splines?** - Which gives better fit for province borders?
2. **How many segments per border?** - Trade-off between smoothness and performance
3. **Newton-Raphson iteration count?** - How many iterations needed for accurate distance?
4. **Spatial hash grid size?** - Balance between lookup overhead and tests per pixel

### Docs to Read Before Next Session
- Bézier curve fitting algorithms (least squares)
- GPU curve rendering techniques
- Spatial hashing for geometric queries

---

## Session Statistics

**Files Changed:** 2 (investigative changes)
- `BorderCurveExtractor.cs` - Lowered smoothing threshold, increased iterations
- `DynamicTextureSet.cs` - Enabled bilinear filtering on BorderMaskTexture

**Lines Added/Removed:** ~10 lines (parameter changes)
**Status:** Planning complete, ready for implementation

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Current system: Thin shader-based borders work perfectly, but follow bitmap jaggedness
- Core problem: Rasterization loses smooth curve detail, no amount of Chaikin smoothing helps
- Chosen solution: Vector curve rendering (Bézier curves + shader distance evaluation)
- User directive: "We don't do simple here. We flex our muscles."

**Implementation Plan:**
1. Fit Bézier curves to border pixel chains (CPU)
2. Upload curve control points to GPU (StructuredBuffer)
3. Calculate distance-to-curve in fragment shader
4. Use spatial hashing for performance

**Performance Target:**
- ~0.1-0.2ms per frame (same as current system)
- ~20ms initialization (acceptable)

**Gotchas for Next Session:**
- Don't over-segment borders (too many curves = slower)
- Newton-Raphson needs good initial guess for t parameter
- Spatial hash must handle curve segments that cross cell boundaries
- Remember LOD system: continent zoom = country borders only

---

## Links & References

### Related Documentation
- [5-resolution-independent-borders.md](5-resolution-independent-borders.md) - Previous session, thin borders with ddx/ddy
- [explicit-graphics-format.md](../../decisions/explicit-graphics-format.md) - Texture format requirements

### Related Sessions
- Session 5: Resolution-independent border detection
- Session 4: Smooth borders completion
- Session 3: Smooth borders debugging

### External Resources
- Bézier curve least-squares fitting algorithms
- GPU vector graphics rendering (Valve's paper on distance fields)
- Parametric curve distance calculation

### Code References
- Current border detection: `MapModeCommon.hlsl:98-171` (ApplyBorders with ddx/ddy)
- Border extraction: `BorderCurveExtractor.cs:118-132` (pixel chain to smoothed curve)
- Chaikin smoothing: `BorderCurveExtractor.cs:328-363` (working but rasterization loses it)

---

## Notes & Observations

**Key Insight from User:**
> "Like most borders are within 10-30 pixels. Do you think you can cram in a curve there? obviously not. We need a resolution independent solution, just like the border."

This is the critical realization - no amount of smoothing a 10-30 pixel chain and rasterizing it back to the same resolution will create smooth curves. The solution is the same as thin borders: render at screen resolution, not texture resolution.

**Why Paradox Borders Look Good:**
- Not because they have massive high-res textures
- Not because they pre-compute perfect curves
- Because they render borders resolution-independently (probably similar vector approach)
- Their 3-pixel province juts look smooth because curves are evaluated at screen resolution

**The Pattern:**
This is the same pattern as:
- Font rendering (TrueType = vector curves)
- SVG graphics (vector, not raster)
- Modern game UI (vector icons, not bitmaps)

We're applying vector graphics to strategy game borders. This is ambitious and correct.

---

*Session planning complete - ready to implement vector curve rendering system*

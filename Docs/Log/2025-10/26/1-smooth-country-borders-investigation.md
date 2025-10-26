# Smooth Country Borders - Investigation & Solution Design
**Date**: 2025-10-26
**Session**: 1
**Status**: 🔄 In Progress (Solution Designed, Not Implemented)
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement colored country borders that use each country's color from the palette
- Make border lines smooth and sleek (not jagged pixel stairs)

**Secondary Objectives:**
- Understand why current distance field borders appear jagged
- Research how Paradox games achieve smooth borders from bitmap province maps

**Success Criteria:**
- Thin, solid, smooth curved lines for country borders (EU5/modern Paradox quality)
- Real-time performance during ownership changes
- Works with bitmap province map format

---

## Context & Background

**Previous Work:**
- Distance field borders already implemented via JFA (Jump Flooding Algorithm)
- Country border detection works (different owner = country border)
- Border rendering uses 3-layer system: black center + country color + AO fade

**Current State:**
- Country borders render with correct colors from CountryColorPalette
- But borders follow jagged bitmap province boundaries (pixel stairs visible)
- User feedback: "looks horrible", wants sleek curved lines like modern Paradox games

**Why Now:**
- Core border system functional, visual quality blocking further progress
- User specifically wants thin, smooth center line (not thick blurry borders)

---

## What We Did

### 1. Implemented Colored Country Borders
**Files Changed:**
- `Assets/Game/Shaders/MapModes/MapModeCommon.hlsl:122-154`

**Implementation:**
```hlsl
// Layer 1: Center black line - solid with minimal AA
float centerAlpha = 1.0 - smoothstep(0.0, 0.2, countryDist);

// Layer 2: Darker country color (0.3-2px)
float colorAlpha = 1.0 - smoothstep(0.3, 2.0, countryDist);
float3 darkColor = countryColor * 0.3;

// Layer 3: AO outline (2-2.3px)
float aoAlpha = 1.0 - smoothstep(2.0, 2.3, countryDist);
```

**Rationale:**
- Samples ownerID from ProvinceOwnerTexture
- Looks up country color from CountryColorPalette
- Renders 3-layer border: black center + dark country color + AO fade

**Result:**
- ✅ Colors work correctly
- ❌ Borders are jagged (follow bitmap pixel boundaries)

### 2. Removed Gaussian Blur Attempt
**Files Changed:**
- `Assets/Archon-Engine/Shaders/BorderDistanceField.compute` (removed SmoothBorders kernel)
- `Assets/Archon-Engine/Scripts/Map/Rendering/BorderDistanceFieldGenerator.cs` (removed smoothing pass)

**What We Tried:**
- Applied 5x5 Gaussian blur to distance field after JFA
- Hoped blur would round jagged corners into smooth curves

**Why It Failed:**
- Created smudgy, blurry borders instead of sharp smooth lines
- User feedback: "looks very smudgy"
- Blur != smooth curves

**Lesson Learned:**
- Distance field blur creates soft gradients, not sharp curved lines
- Need actual curve extraction/fitting, not post-processing blur

---

## Decisions Made

### Decision 1: Reject Texture Upscaling Approach
**Context:** Considered upscaling ProvinceIDTexture 2x-4x to make jagged stairs smaller relative to screen pixels

**Options Considered:**
1. **2x Upscale** - 5632x2048 → 11264x4096 textures (~130MB → ~520MB VRAM)
2. **4x Upscale** - 5632x2048 → 22528x8192 textures (~2GB VRAM)
3. **Reject upscaling entirely**

**Decision:** Chose Option 3 (Reject)

**Rationale:**
- Upscaling doesn't solve the fundamental problem (jagged bitmap boundaries)
- Would need to re-upscale for larger maps (Imperator Rome: 8192x4096)
- At 8192x4096 base, 4x upscale = 32768x16384 = **2GB+ VRAM just for borders**
- Chasing resolution forever instead of solving root cause
- Paradox games don't use massive textures, must be doing something cleverer

**Trade-offs:**
- ✅ Avoid VRAM explosion
- ✅ Works at any base resolution
- ❌ Still need to find actual solution

### Decision 2: Adjacency-Based Border Curve Extraction (CHOSEN SOLUTION)
**Context:** Need to extract smooth curves from jagged bitmap borders in real-time

**Options Considered:**
1. **Blind Pixel Tracing** - Scan entire map, trace border pixel chains, fit curves
   - Pros: Complete coverage
   - Cons: O(width×height), sequential (not GPU-friendly), expensive at runtime

2. **Texture Upscaling** - Make jagged stairs sub-pixel
   - Pros: Simple implementation
   - Cons: VRAM explosion, doesn't scale, doesn't solve root problem

3. **Adjacency-Based Extraction** - Use AdjacencySystem to find neighbor pairs, extract only shared borders
   - Pros: O(provinces × neighbors), parallelizable, pre-computable
   - Cons: Requires AdjacencySystem, more complex initial implementation

**Decision:** Chose Option 3 (Adjacency-Based)

**Rationale:**
- **Key Insight:** Border curve geometry is STATIC (defined by bitmap), only appearance changes (colors/thickness based on ownership)
- Pre-compute all border curves once at map load
- Runtime: Just toggle visibility and colors (instant)
- Complexity: ~60K border segments (10K provinces × 6 neighbors) instead of millions of pixels
- Each segment independent = fully parallelizable

**Implementation Plan:**
1. **Map Load (one-time):**
   - For each province pair from AdjacencySystem
   - Extract shared border pixels between the pair
   - Smooth pixels into curve (Chaikin's algorithm, 3 iterations)
   - Cache: `borderCurves[(provinceA, provinceB)] = smoothedCurve`
   - **Cost:** 5-10 seconds (acceptable for map load)

2. **Runtime (ownership changes):**
   - Province changes owner
   - Update style flags for its 6 neighbor borders:
     - Different owner? → Style = COUNTRY, Color = ownerColor
     - Same owner? → Style = PROVINCE, Color = gray
   - **Cost:** ~0.001ms (just flag updates!)

3. **GPU Rendering:**
   - Draw ALL cached curves every frame
   - Use style flags to determine thickness/color
   - **Cost:** Single draw call for all borders

**Trade-offs:**
- ✅ Zero runtime curve recomputation
- ✅ Instant visual updates (just color/style changes)
- ✅ Perfectly smooth pre-computed curves
- ✅ Scales to any number of ownership changes
- ❌ Requires AdjacencySystem dependency
- ❌ Initial map load time increases by 5-10 seconds

**Documentation Impact:**
- Will document as new pattern: "Static Geometry, Dynamic Appearance"
- Add to architecture docs as preferred approach for bitmap-to-vector conversion

---

## What Worked ✅

1. **Using Jump Flooding Algorithm for Distance Field**
   - What: GPU-based distance field generation for borders
   - Why it worked: Provides smooth distance gradients, works at any scale
   - Reusable pattern: Yes - JFA is the right foundation
   - Note: Problem isn't the distance field itself, it's that bitmap boundaries are jagged

2. **Three-Layer Border Rendering**
   - What: Black center + dark country color + AO fade
   - Why it worked: Creates depth and visual hierarchy
   - Reusable pattern: Yes - good for any border visualization
   - Will preserve in final solution

3. **Explicit GraphicsFormat for RenderTextures**
   - What: Always use `RenderTextureDescriptor` with explicit `GraphicsFormat`
   - Why it worked: Prevents TYPELESS format issues
   - Reusable pattern: Yes - already documented in decisions/explicit-graphics-format.md
   - Applied correctly when attempting upscaling

---

## What Didn't Work ❌

1. **Gaussian Blur for Smoothing**
   - What we tried: 5x5 Gaussian blur on distance field after JFA
   - Why it failed: Blur creates soft gradients, not sharp smooth curves
   - Lesson learned: Smoothing != blurring. Need actual curve fitting.
   - Don't try this again because: Creates smudgy appearance instead of sleek lines

2. **Texture Upscaling**
   - What we tried: Upscale ProvinceIDTexture 2x-4x to make stairs sub-pixel
   - Why it failed: Doesn't solve root cause, VRAM explosion at high resolutions
   - Lesson learned: Resolution isn't the answer - need different approach entirely
   - Don't try this again because: Paradox games prove there's a better way

3. **Smoothstep Anti-Aliasing on Jagged Distance Field**
   - What we tried: `smoothstep(0.0, 0.5, countryDist)` for softer edges
   - Why it failed: Smoothstep follows jagged distance field, just makes it blurrier
   - Lesson learned: Can't smooth a jagged input into smooth output via thresholding
   - Don't try this again because: Geometry must be smooth before rendering

---

## Problems Encountered & Solutions

### Problem 1: Jagged Center Line Despite Distance Field
**Symptom:** Black center line follows exact pixel boundaries from provinces.bmp, visible stairs

**Root Cause:**
- JFA generates smooth distance GRADIENTS from jagged pixel boundaries
- But the geometry itself is still jagged (distance=0 follows exact bitmap pixels)
- Smoothstep over jagged distance field = jagged smooth line

**Investigation:**
- Tried: Gaussian blur on distance field → smudgy
- Tried: Tighter smoothstep range → sharper jagged line
- Tried: Texture upscaling → would need infinite resolution
- Found: The distance field is working correctly, problem is source data (bitmap) is jagged

**Solution:** (Not yet implemented)
- Pre-extract border curves from bitmap using AdjacencySystem
- Smooth curves with Chaikin's algorithm (CPU/GPU at load time)
- Cache smoothed curves
- Render cached curves instead of thresholding distance field

**Why This Works:**
- Separates geometry (static, smoothable) from appearance (dynamic, runtime)
- Curve smoothing algorithms can round pixel stairs into actual curves
- Only compute once, reuse forever

**Pattern for Future:**
- When converting bitmap data to vector representation, do it at load time
- Cache vector data, use runtime flags for appearance
- "Static Geometry, Dynamic Appearance" pattern

### Problem 2: CPU Cost of Real-Time Curve Smoothing
**Symptom:** Initial plan to re-smooth borders on ownership change would cost ~30ms per province

**Root Cause:**
- Chaikin smoothing takes 1-5ms per border segment
- Province has ~6 neighbors
- 6 × 5ms = 30ms per ownership change
- War scenario: 50 provinces/second = 1500ms/frame = freeze

**Investigation:**
- Considered: GPU compute shader for smoothing
- Considered: Incremental updates over multiple frames
- Found: **Border geometry doesn't change when ownership changes!**

**Solution:** (Design finalized)
- Pre-compute ALL border curves at map load
- Runtime: Only update style flags (color/thickness)
- Geometry is static, appearance is dynamic

**Why This Works:**
- Shape of border between Province A and Province B is fixed by bitmap
- Ownership change only affects whether it's styled as country or province border
- Zero recomputation needed

**Pattern for Future:**
- Identify what's truly static vs dynamic
- Pre-compute static data, use flags for dynamic appearance
- Avoid recomputing geometry when only appearance changes

---

## Architecture Impact

### Documentation Updates Required
- [ ] Create new architecture doc: `static-geometry-dynamic-appearance-pattern.md`
- [ ] Update `master-architecture-document.md` - add border smoothing section
- [ ] Document Chaikin smoothing implementation details

### New Patterns Discovered
**Pattern:** Static Geometry, Dynamic Appearance
- When to use: Converting bitmap data to renderable vectors with runtime state changes
- Benefits:
  - Pre-compute expensive operations (curve fitting)
  - Runtime updates are instant (just flag/color changes)
  - No per-frame geometric recomputation
- Use cases:
  - Border rendering (this implementation)
  - Road networks (bitmap → smooth paths)
  - River systems (bitmap → flowing curves)
- Add to: New architecture doc

**Pattern:** Adjacency-Based Border Extraction
- When to use: Need to process borders between discrete regions
- Benefits:
  - O(regions × neighbors) instead of O(pixels)
  - Each border segment independent (parallelizable)
  - Known segment count (predictable performance)
- Requires: AdjacencySystem or equivalent neighbor data structure
- Add to: Map rendering architecture docs

### Architectural Decisions That Changed
- **Changed:** Border rendering approach
- **From:** Pure distance field rendering (threshold + smoothstep)
- **To:** Pre-computed smooth vector curves with runtime styling
- **Scope:** Border rendering system only
- **Reason:** Distance field can't extract smooth curves from jagged bitmap

---

## Code Quality Notes

### Performance
- **Target (from architecture):** Real-time ownership changes, no frame hitches
- **Current Status:** ⚠️ Solution designed but not implemented
- **Expected Performance:**
  - Map Load: +5-10 seconds (one-time, acceptable)
  - Ownership Change: <0.01ms (flag updates only)
  - Rendering: Single draw call for all borders

### Testing
- **Tests Needed:**
  - Adjacency-based border pixel extraction accuracy
  - Chaikin smoothing produces smooth curves
  - Border cache lookup performance
  - Ownership change updates correct borders
  - Visual test: Borders appear smooth at all zoom levels

### Technical Debt
- **Created:** None yet (solution not implemented)
- **To Address:**
  - Current distance field border rendering will be replaced
  - JFA code can be removed if not used elsewhere

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Implement Border Pixel Extraction** - Use AdjacencySystem to find shared pixels between province pairs
2. **Implement Chaikin Smoothing** - Smooth extracted pixel chains into curves
3. **Create Border Cache Structure** - Store pre-computed smooth curves
4. **Implement Runtime Style System** - Toggle visibility/color based on ownership
5. **Implement GPU Curve Rendering** - Draw smooth lines from cached curves

### Questions to Resolve
1. **Where are province pixel lists stored?** - Need efficient way to find shared border pixels
2. **GPU or CPU for Chaikin smoothing?** - CPU simpler, GPU faster for 60K segments
3. **How to render smooth curves on GPU?** - Line strips? Instanced quads? Compute shader rasterization?
4. **Should we keep JFA distance field?** - Might still be useful for thick borders or other effects

### Docs to Read Before Next Session
- `Assets/Archon-Engine/Scripts/Core/Systems/AdjacencySystem.cs` - How to get neighbor pairs
- Research Chaikin's corner-cutting algorithm - Implementation details
- Unity line rendering - Best approach for drawing smooth curves

---

## Session Statistics

**Files Changed:** 4
- `MapModeCommon.hlsl` (colored borders)
- `BorderDistanceField.compute` (removed blur kernel)
- `BorderDistanceFieldGenerator.cs` (removed smoothing pass)
- `MapTextureManager.cs` (added upscale parameter, then reverted concept)

**Lines Added/Removed:** ~+200/-200 (explored upscaling, then removed)
**Tests Added:** 0 (investigation/design session)
**Bugs Fixed:** 0
**Commits:** 0 (work in progress)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Distance field borders work correctly, but geometry is jagged (bitmap limitation)
- Solution: Pre-compute smooth curves from bitmap, cache them, update styles at runtime
- Key insight: Border geometry is static, appearance is dynamic
- Implementation: Use AdjacencySystem to extract neighbor pairs → find shared borders → smooth with Chaikin → cache

**Current Status:**
- Solution fully designed and validated with user
- Implementation not started
- Core architecture: Static Geometry + Dynamic Appearance pattern

**Gotchas for Next Session:**
- Don't try to smooth at runtime - pre-compute everything
- Don't recompute geometry on ownership change - just update flags
- Province border curve between A and B is ALWAYS the same shape
- Use AdjacencySystem, don't scan entire texture

---

## Links & References

### Related Documentation
- [explicit-graphics-format.md](../decisions/explicit-graphics-format.md) - RenderTexture format requirements
- [master-architecture-document.md](../Engine/master-architecture-document.md) - Overall architecture

### External Research
- Jump Flooding Algorithm (JFA) - Distance field generation
- Chaikin's corner-cutting algorithm - Curve smoothing technique
- Marching squares - Bitmap contour extraction (considered but not chosen)

### Code References
- Border rendering: `Assets/Game/Shaders/MapModes/MapModeCommon.hlsl:122-154`
- Distance field generation: `Assets/Archon-Engine/Shaders/BorderDistanceField.compute`
- JFA implementation: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderDistanceFieldGenerator.cs`

---

## Notes & Observations

**Key Realizations:**
- The problem isn't the distance field technique - it's that we're trying to extract smooth curves from jagged input
- Paradox games must pre-process borders somehow - they're not doing pure runtime distance field thresholding
- The "smooth border" problem is really a "bitmap to vector conversion" problem
- Ownership changes are frequent (wars), but border GEOMETRY never changes - huge optimization opportunity

**User Feedback Patterns:**
- Consistently rejected blurry/smudgy solutions
- Emphasized wanting thin, solid, sleek curved lines (not thick borders hiding jaggedness)
- Correctly identified that modern Paradox games achieve this somehow with similar bitmap formats
- Led to breakthrough by asking about AdjacencySystem (reduced problem complexity dramatically)

**Future Considerations:**
- This pattern (static geometry + dynamic appearance) could apply to many bitmap → vector conversions
- Consider generalizing into reusable utility: `BitmapRegionBorderExtractor`
- Could be useful for rivers, roads, terrain transitions, etc.

---

*Session Log - Created 2025-10-26*

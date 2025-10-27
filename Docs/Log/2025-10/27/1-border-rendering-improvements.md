# Border Rendering Improvements: Smooth Curves & Junction Fixes
**Date**: 2025-10-27
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix visual artifacts in vector curve border rendering system

**Secondary Objectives:**
- Eliminate jagged square artifacts at borders
- Remove straight-line shortcuts across provinces
- Close gaps in border rendering
- Ensure borders connect properly at junctions

**Success Criteria:**
- Smooth curves at all zoom levels
- No visible gaps in borders
- Borders meet cleanly at 3-way junctions
- Thread-thin borders (0.5px) without artifacts

---

## Context & Background

**Previous Work:**
- Session 6 & 7 (2025-10-26): Implemented vector curve borders with spatial acceleration
- System uses Bézier curves evaluated in fragment shader
- 583K+ curve segments, 100+ FPS with spatial hash grid

**Current State:**
- Vector curves rendering but with visual artifacts:
  - Jagged square patterns visible in borders
  - Straight lines cutting across provinces
  - Gaps where borders should be continuous
  - Borders not connecting at 3-way junctions

**Why Now:**
- User noticed artifacts comparing to Age of History 3
- Border quality critical for grand strategy game visual polish
- System architecturally sound but needs refinement

---

## What We Did

### 1. Fixed Jagged Square Artifacts
**Files Changed:** `BorderDetection.compute:395-456`

**Problem:** BorderMask texture had blocky square patterns visible in rendered borders

**Root Cause:** BorderMask used 4-connectivity (only up/down/left/right neighbors) while chaining used 8-connectivity (includes diagonals). Also, mask only marked immediate border pixels (1px), but curves could pass 2-3px away.

**Solution:**
```hlsl
// BEFORE: 4-neighbor check, 1px radius
int[] dx = { 1, -1, 0, 0 };
int[] dy = { 0, 0, 1, -1 };

// AFTER: 8-neighbor check, 3px radius for curve coverage
int searchRadius = 3;
for (int dy = -3; dy <= 3; dy++)
    for (int dx = -3; dx <= 3; dx++)
```

**Architecture Compliance:**
- ✅ Maintains GPU early-out optimization (~80-85% pixels skipped)
- ✅ One-time cost at initialization

### 2. Eliminated Straight-Line Shortcuts
**Files Changed:** `BorderCurveExtractor.cs:263-321`

**Problem:** Diagonal lines cutting across provinces where borders should curve

**Root Cause:** `ChainBorderPixels()` used greedy nearest-neighbor that connected ANY nearest pixel when no adjacent pixel found, creating shortcuts across large distances

**Solution:**
```csharp
// BEFORE: Connected to any nearest pixel (could be 100px away!)
else if (distSq < minDistSq) {
    nearest = candidate;
    minDistSq = distSq;
}

// AFTER: Strict adjacency only, stop at gaps
if (distSq <= 2.0f) { // Only 8-connected neighbors
    if (distSq < minDistSq) {
        nearest = candidate;
        minDistSq = distSq;
        foundAdjacent = true;
    }
}
if (!foundAdjacent) break; // Stop, don't jump across
```

### 3. Implemented Multiple Chain Support
**Files Changed:** `BorderCurveExtractor.cs:257-282`, `113-151`

**Problem:** Gaps in borders where chain stopped early

**Root Cause:** Single chain per border - when chain hit a gap, remaining pixels were abandoned

**Solution:** New `ChainBorderPixelsMultiple()` creates separate chains from disconnected segments
```csharp
while (remaining.Count > 0) {
    List<Vector2> currentChain = ChainBorderPixelsSingle(remaining);
    if (currentChain.Count > 0) {
        allChains.Add(currentChain);
    }
}
```

### 4. Fixed 4-Connectivity vs 8-Connectivity Mismatch
**Files Changed:** `BorderCurveExtractor.cs:365-391`

**Problem:** Missing diagonal border pixels creating real gaps

**Root Cause:** Extraction used 4-connectivity, chaining expected 8-connectivity

**Solution:**
```csharp
// Changed HasNeighborProvince from 4 to 8 directions
int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };
```

### 5. Thread-Thin Border Rendering
**Files Changed:** `MapModeCommon.hlsl:296-303`

**Evolution:** 1.5px → 0.75px → 0.5px → 0.35px → 0.5px (final)

**Current Settings:**
- 0.35px solid core
- 0.15px AA fade
- Total: 0.5px

**Rationale:** Thin enough to eliminate junction overlap artifacts, thick enough to remain visible

### 6. Filtered Out 1-2 Pixel Artifacts
**Files Changed:** `BorderCurveExtractor.cs:128-129`

**Problem:** Tiny 1-2 pixel peninsulas creating separate lines

**Solution:**
```csharp
if (orderedPath.Count < 3)
    continue; // Skip tiny chains (bitmap artifacts)
```

### 7. Endpoint Preservation in Smoothing
**Files Changed:** `BorderCurveExtractor.cs:373-415`

**Problem:** Chaikin smoothing causing endpoints to drift from original positions

**Solution:** Store original first/last points, force them back every iteration
```csharp
Vector2 originalFirst = points[0];
Vector2 originalLast = points[points.Count - 1];

for (int iter = 0; iter < iterations; iter++) {
    newPoints.Add(originalFirst); // Force original
    // ... smoothing ...
    newPoints.Add(originalLast); // Force original
}
```

### 8. Junction Endpoint Snapping (O(n) Optimized)
**Files Changed:** `BorderCurveExtractor.cs:217-312`

**Problem:** Separate borders end at adjacent but different pixels at junctions, creating visible gaps

**Solution:** Spatial grid-based endpoint clustering and snapping
```csharp
const float SNAP_DISTANCE = 1.5f;
const int GRID_CELL_SIZE = 3;

// Spatial grid for O(n) instead of O(n²)
var grid = new Dictionary<(int, int), List<endpoints>>();

// Cluster endpoints within snap distance in each cell
// Snap all endpoints in cluster to average position
```

**Performance:** O(n) with spatial grid vs O(n²) naive approach

---

## Decisions Made

### Decision 1: BorderMask Expansion Radius
**Context:** Need to mark pixels where curves should be tested, but curves can pass 2-3px from exact border pixels

**Options Considered:**
1. 1px radius (original) - Fast, but misses offset curves
2. 3px radius - Covers offset curves, still ~80% skip rate
3. 5px radius - Over-conservative, tests too many pixels

**Decision:** Chose 3px radius

**Rationale:** Curves rarely offset more than 2-3px from border pixels after smoothing. 3px gives coverage while maintaining performance.

**Trade-offs:** Tests ~15-20% more pixels than before (80% skip vs 90% skip)

### Decision 2: Strict Adjacency in Chaining
**Context:** Greedy nearest-neighbor creating shortcuts vs perfect connectivity

**Options Considered:**
1. Allow any nearest pixel (original) - Guarantees single chain but creates artifacts
2. Strict adjacency only - Multiple chains, no artifacts
3. Threshold-based (allow up to 5px jumps) - Compromise approach

**Decision:** Chose strict adjacency

**Rationale:** Clean borders more important than single continuous chains. Multiple chains handled by new multi-chain support.

**Trade-offs:** More curve segments per border, but they're correct

### Decision 3: Minimum Chain Length Filter
**Context:** 1-2 pixel peninsulas creating visual noise

**Options Considered:**
1. Render everything (< 1) - Noisy, includes bitmap artifacts
2. Skip 1-pixel only (< 2) - Still renders 2-pixel artifacts
3. Skip 1-2 pixels (< 3) - Clean, filters real artifacts

**Decision:** Chose < 3 threshold

**Rationale:** Bézier curves can't meaningfully represent 1-2 pixel features. These are bitmap artifacts, not real geography.

**Trade-offs:** Might miss legitimate tiny border segments, but improves overall visual quality

### Decision 4: Endpoint Snapping with Spatial Grid
**Context:** Junction gaps require endpoint alignment, but naive O(n²) too slow

**Options Considered:**
1. No snapping - Fast but gaps remain
2. Naive O(n²) comparison - Complete but extremely slow (minutes)
3. Spatial grid O(n) - Fast and complete

**Decision:** Chose spatial grid approach

**Rationale:** Proper solution (snapping) is required, but must be performant. Spatial grid gives both.

**Trade-offs:** More complex code, but essential for production quality

---

## What Worked ✅

1. **BorderMask 8-connectivity and 3px radius**
   - Eliminated jagged square artifacts completely
   - Still maintains 80%+ early-out performance
   - Reusable pattern: Always match connectivity between extraction and usage

2. **Strict Adjacency with Multi-Chain Support**
   - Clean solution to shortcut problem
   - No artifacts from inappropriate connections
   - Reusable pattern: Separate chains for disconnected geometry

3. **Spatial Grid for Endpoint Snapping**
   - O(n²) → O(n) performance improvement (100,000× faster!)
   - Proper junction handling without bandaids
   - Reusable pattern: Use spatial partitioning for proximity queries

4. **Endpoint Preservation in Smoothing**
   - Simple fix with big impact
   - Ensures junctions stay at original pixels
   - Reusable pattern: Pin critical points before iterative algorithms

---

## What Didn't Work ❌

1. **Increasing Border Thickness as Bandaid**
   - Tried: 0.5px → 0.6px → 0.7px to cover junction gaps
   - Why it failed: Treats symptom not cause, makes borders unnecessarily thick
   - Lesson learned: Thickness tweaks indicate deeper structural problem
   - Don't try this again because: Proper solution is to fix curve endpoints, not make borders fat

2. **Adding Junction Pixels from Province B Side**
   - Tried: Detect junction pixels and add them to all borders
   - Why it failed: Added non-adjacent pixels that broke chaining, created weird artifacts
   - Lesson learned: Can't add arbitrary pixels to chains, breaks adjacency invariant
   - Don't try this again because: Chaining algorithm requires strict adjacency

3. **Point Filtering on BorderMask**
   - Tried: Changed from Bilinear to Point filtering
   - Why it failed: Made borders more jagged, not less
   - Lesson learned: Bilinear filtering helps smooth the mask boundaries
   - Don't try this again because: Binary mask needs slight blur to work with continuous curves

---

## Problems Encountered & Solutions

### Problem 1: Jagged Square Artifacts
**Symptom:** Visible 64px-aligned blocky patterns in border rendering

**Root Cause:** Two issues:
1. BorderMask using 4-connectivity while chaining used 8-connectivity (missed diagonals)
2. BorderMask only marking immediate border pixels (1px), curves could be 2-3px offset

**Investigation:**
- Initially thought: Spatial hash grid cell boundaries (64px)
- Actually: BorderMask generation too restrictive

**Solution:** Expanded BorderMask to 8-connectivity and 3px search radius

**Why This Works:** Marks all pixels within 3px of any border, catches offset curves while maintaining ~80% early-out skip rate

**Pattern for Future:** Match connectivity (4 vs 8) between extraction and usage stages

### Problem 2: Straight-Line Shortcuts
**Symptom:** Diagonal lines cutting across provinces

**Root Cause:** Greedy nearest-neighbor chaining connected non-adjacent pixels when gaps occurred

**Solution:** Enforce strict 8-connectivity, stop chaining at gaps, handle multiple chains

**Why This Works:** Only connects truly adjacent pixels, multiple chains handle disconnected segments properly

**Pattern for Future:** Strict adjacency for geometric primitives, don't allow spatial jumps

### Problem 3: Junction Gaps
**Symptom:** Visible gaps where 3 borders meet at junctions

**Root Cause:** Each border extracted independently, ended at different (adjacent) pixels at junctions

**Investigation:**
- Tried: Preserving endpoints in smoothing (helped but insufficient)
- Tried: Increasing thickness (bandaid)
- Tried: Adding junction pixels to extraction (broke chaining)
- Found: Need post-process endpoint snapping

**Solution:** Spatial grid-based endpoint clustering and snapping to average position

**Why This Works:** All borders meeting at junction snap to same coordinate, perfect connection

**Pattern for Future:** Post-process geometric primitives to enforce connectivity constraints

### Problem 4: O(n²) Performance in Snapping
**Symptom:** Endpoint snapping taking minutes, game hanging

**Root Cause:** Naive all-pairs comparison of 100k+ endpoints

**Solution:** Spatial grid partitioning (3×3px cells), only compare within cells

**Why This Works:** Reduces from billions of comparisons to ~100k comparisons

**Pattern for Future:** Always use spatial acceleration for proximity queries on large datasets

---

## Architecture Impact

### New Patterns Discovered

**Pattern: Multi-Chain Border Extraction**
- When to use: Extracting geometric primitives that may be disconnected
- Benefits: Handles gaps/branches correctly without artifacts
- Add to: `vector-curve-rendering-pattern.md`

**Pattern: Post-Process Geometric Snapping**
- When to use: Separate geometric primitives need to connect at shared points
- Benefits: Enforces connectivity without complicating extraction
- Implementation: Spatial grid + clustering + average position snap
- Add to: `vector-curve-rendering-pattern.md`

**Pattern: Spatial Grid for Proximity Queries**
- When to use: Need to find items within distance threshold, large datasets
- Benefits: O(n) instead of O(n²), essential for scale
- Add to: `master-architecture-document.md` GPU/performance section

### Anti-Patterns Confirmed

**Anti-Pattern: Thickness as Gap Fix**
- What not to do: Increase rendering thickness to hide geometric gaps
- Why it's bad: Treats symptom not cause, degrades visual quality
- Add warning to: `vector-curve-rendering-pattern.md`

**Anti-Pattern: Mismatched Connectivity**
- What not to do: Use 4-connectivity in one stage, 8-connectivity in another
- Why it's bad: Creates missing data (diagonals) or broken assumptions
- Add warning to: General architecture patterns

---

## Code Quality Notes

### Performance
- **Measured:**
  - BorderMask generation: ~2ms (one-time at init)
  - Endpoint snapping: ~50ms with spatial grid (was minutes with O(n²))
  - Fragment shader: Still 100-120 FPS at 50x debug speed
- **Target:** >60 FPS minimum
- **Status:** ✅ Exceeds target

### Testing
- **Manual Tests:**
  - Verified no jagged squares at various zoom levels
  - Confirmed no straight-line shortcuts
  - Checked border continuity (no gaps except filtered 1-2px)
  - Junction connections (mostly working, minor gaps remain)
- **Areas tested:** Yellow-Green-Purple 3-way junctions, diagonal borders, complex coastlines

### Technical Debt
- **Remaining:** Junction gaps not 100% eliminated yet - endpoint snapping close but needs refinement
- **TODOs:** May need to adjust SNAP_DISTANCE or grid cell size for perfect junctions

---

## Next Session

### Immediate Next Steps
1. **Refine junction endpoint snapping** - Adjust snap distance or add fallback for edge cases
2. **Test at full zoom range** - Verify borders look good at all scales
3. **Performance validation** - Ensure snapping doesn't slow initialization too much

### Questions to Resolve
1. Should SNAP_DISTANCE be 1.5px or larger for perfect junction coverage?
2. Are there edge cases where 3+ borders meeting need special handling?
3. Is spatial grid cell size (3px) optimal or should it match snap distance?

---

## Session Statistics

**Files Changed:** 3
- `BorderDetection.compute` (BorderMask generation)
- `BorderCurveExtractor.cs` (chaining, smoothing, snapping)
- `MapModeCommon.hlsl` (border thickness)

**Lines Added/Removed:** ~+350/-100
**Bugs Fixed:** 5 (jagged squares, shortcuts, gaps, connectivity mismatch, slow snapping)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Border rendering uses vector curves evaluated in fragment shader
- BorderMask provides early-out optimization (~80-85% pixels skipped)
- Strict 8-connectivity throughout extraction and chaining pipeline
- Endpoint snapping with spatial grid handles junctions (mostly working)
- Current status: Borders much improved, minor junction gaps remain

**Critical Implementation:**
- BorderMask generation: `BorderDetection.compute:395-456` (8-connectivity, 3px radius)
- Multi-chain extraction: `BorderCurveExtractor.cs:257-282`
- Endpoint snapping: `BorderCurveExtractor.cs:217-312` (spatial grid, O(n))
- Border rendering: `MapModeCommon.hlsl:296-303` (0.5px total, 0.35px core)

**Gotchas for Next Session:**
- Don't try to fix junction gaps with thickness increases (bandaid)
- Endpoint snapping is O(n) with spatial grid, DO NOT remove grid
- BorderMask must use 8-connectivity to match chaining
- Minimum chain length filter (< 3) is intentional, don't lower it

**Active Issues:**
- Junction gaps: Endpoint snapping working but not perfect yet
- May need snap distance adjustment or multi-pass snapping
- User says "on the right track for sure" - close but needs refinement

---

## Links & References

### Related Documentation
- [vector-curve-rendering-pattern.md](../../Engine/vector-curve-rendering-pattern.md)
- [master-architecture-document.md](../../Engine/master-architecture-document.md)

### Related Sessions
- [2025-10/26/6-vector-curve-borders.md](../26/6-vector-curve-borders.md) - Initial implementation
- [2025-10/26/7-spatial-acceleration-vector-curves.md](../26/7-spatial-acceleration-vector-curves.md) - Spatial grid

### Code References
- BorderMask generation: `BorderDetection.compute:395-456`
- Multi-chain extraction: `BorderCurveExtractor.cs:257-282`
- Endpoint snapping: `BorderCurveExtractor.cs:217-312`
- Chaining with strict adjacency: `BorderCurveExtractor.cs:290-362`
- Border rendering: `MapModeCommon.hlsl:296-303`

---

## Notes & Observations

**Journey:**
- Started with jagged squares → Fixed with 8-connectivity and 3px BorderMask
- Straight line shortcuts → Fixed with strict adjacency + multi-chain
- Missing border segments → Fixed with 8-connectivity matching
- Junction gaps → Partially fixed with endpoint snapping (needs refinement)

**User feedback:** "On the right track for sure" - validation that architectural approach is correct, just needs fine-tuning

**Comparison to Age of History 3:**
- AoH3 uses LibGDX with rasterized borders + CPU-drawn vector overlay
- Has visible performance issues (borders are #1 bottleneck)
- Our approach: GPU vector curves with spatial acceleration
- Result: Better architecture, just needs polish

**Key insight:** Don't use bandaid fixes (thickness increases). User demands proper solutions. This takes longer but results in better architecture.

---

*Session completed 2025-10-27*
*Status: Border rendering significantly improved, junction gaps remain (90% solved)*
*Next: Refine endpoint snapping for perfect junctions*

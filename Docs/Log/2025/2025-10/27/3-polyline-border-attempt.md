# Polyline Border Rendering Attempt
**Date**: 2025-10-27
**Session**: 3
**Status**: ⚠️ Partial - Core issues remain
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Switch from Bézier curves to polyline rendering to match Age of History 3 style (clean lines following pixels exactly)

**Secondary Objectives:**
- Fix "tumor" artifacts from degenerate segments
- Eliminate "wild lines" from tiny peninsula artifacts
- Ensure junction connectivity

**Success Criteria:**
- One continuous line per border following exact pixel path
- No visual artifacts (tumors, wild lines)
- Clean connections at 3-way junctions

---

## Context & Background

**Previous Work:**
- Session 2: [2-junction-connectivity-fix.md](2-junction-connectivity-fix.md) - Fixed junction connectivity by continuing chains through junctions, removed Chaikin smoothing

**Current State:**
- Bézier curves creating self-intersecting loops at junctions
- Curves deviating from actual province boundaries
- User wants Age of History 3 style: straight polylines following pixels exactly

**Why Now:**
- Bézier curves fundamentally wrong tool for jagged pixel data
- User showed Age of History 3 screenshot: "That's the kind of style I want to have"

---

## What We Did

### 1. Switched to Polyline Approach
**Files Changed:** `BezierCurveFitter.cs:90-113`

**Implementation:**
Completely rewrote `FitCurve()` to create straight line segments between consecutive pixels:

```csharp
// POLYLINE APPROACH: Create straight line segment between each consecutive pixel
for (int i = 0; i < points.Count - 1; i++)
{
    Vector2 p0 = points[i];
    Vector2 p3 = points[i + 1];

    // Skip degenerate segments (same start/end point creates visual artifacts)
    if (Vector2.Distance(p0, p3) < 0.01f)
        continue;

    // Create linear "Bézier" (control points at 1/3 and 2/3 along line)
    Vector2 p1 = Vector2.Lerp(p0, p3, 0.33f);
    Vector2 p2 = Vector2.Lerp(p0, p3, 0.67f);

    segments.Add(new BezierSegment(p0, p1, p2, p3, borderType));
}
```

**Rationale:**
- One segment per pixel pair follows border exactly
- No overshooting or loops like Bézier curves
- Renders as straight lines in shader (linear Bézier)

### 2. Fixed "Tumor" Artifacts
**Files Changed:** `BorderCurveExtractor.cs:315-319`

**Problem:** Endpoint snapping was collapsing P0 and P3 of SAME segment to same point, creating zero-length "tumor" blobs

**First Attempt - Skip Degenerate in FitCurve:**
Added distance check to skip segments where P0 ≈ P3. Didn't work - tumors created AFTER curve fitting by snapping.

**Second Attempt - Prevent Same-Segment Snapping:**
```csharp
// CRITICAL: Don't snap P0 and P3 of the SAME segment together
bool sameSegment = (candidate.Item2 == cellEndpoints[i].Item2) &&
                   (candidate.Item3 == cellEndpoints[i].Item3);
if (sameSegment) continue;
```
Still didn't work - 10px snap distance too aggressive for short polyline segments.

**Third Attempt - Reduce Snap Distance:**
Reduced from 10.0px → 1.5px. Still collapsed 60% of segments (316,376 degenerate out of 514,529 total).

**Final Solution - Disable Snapping Entirely:**
```csharp
// DISABLED: Endpoint snapping destroys polylines by collapsing segments
// With chain merging, polylines are already continuous - no snapping needed
```

**Result:** ✅ Tumors eliminated

### 3. Chain Merging for Continuous Polylines
**Files Changed:** `BorderCurveExtractor.cs:126-140, 655-730`

**Problem:** Multiple chains per border creating separate polylines with visual gaps and clumping

**Solution:** Added `MergeChains()` function to connect chains into one continuous path:

```csharp
private List<Vector2> MergeChains(List<List<Vector2>> chains)
{
    // Start with longest chain
    List<Vector2> merged = new List<Vector2>(chains[longestIdx]);

    // Keep merging closest chains until all connected
    while (usedIndices.Count < chains.Count)
    {
        // Find closest unused chain (checks all 4 connection possibilities)
        // Reverse chain if needed to connect properly
        // Append or prepend to build one continuous path
    }

    return merged;
}
```

**Result:** Chains merged (e.g., "Chains: 3, Merged path: 28 pixels"), but junctions still have gaps

### 4. Attempted Tiny Border Filtering
**Files Changed:** `BorderCurveExtractor.cs:123-134`

**Problem:** 1-2 pixel peninsulas creating "wild lines" across provinces

**Attempts:**
1. Skip borders with <5 pixels → Didn't catch all
2. Increased to <10 pixels → Still wild lines visible

**Status:** ❌ Not fully resolved

### 5. Attempted Junction Connectivity Fixes
**Files Changed:** `BorderCurveExtractor.cs:442-497`

**Attempts:**

**Attempt 1: Add All Junction Pixels + BFS Bridging**
- Added ALL junction pixels to borders
- Used BFS to bridge gaps to reach junctions
- Result: Created disconnected pixels, chaining algorithm couldn't connect them properly
- Conclusion: Made junctions worse

**Attempt 2: Disable Junction Injection Entirely**
- Removed all junction pixel addition code
- Result: Some lines connected better, but junctions still broken

**Attempt 3: Add Only Adjacent Junction Pixels**
```csharp
// Only add junction if 8-connected to existing border
bool isAdjacent = false;
for (int dy = -1; dy <= 1; dy++)
{
    for (int dx = -1; dx <= 1; dx++)
    {
        Vector2 neighbor = new Vector2(jx + dx, jy + dy);
        if (borderPixels.Contains(neighbor))
            isAdjacent = true;
    }
}
if (isAdjacent)
    borderPixels.Add(junctionPos);
```

**Status:** ❌ Junctions still "fucked" per user

---

## Decisions Made

### Decision 1: Polylines vs Bézier Curves
**Context:** Bézier curves creating loops and deviating from borders

**Options Considered:**
1. **Tighter Bézier fitting** - Reduce segment length, lower error tolerance
2. **Sharp corner detection** - Use linear fallback at junctions
3. **Polylines** - Straight segments between pixels

**Decision:** Polylines (Option 3)

**Rationale:**
- User explicitly requested Age of History 3 style
- Bézier curves fundamentally wrong tool for jagged pixel data
- Simpler = fewer points of failure

**Trade-offs:**
- No smooth curves (acceptable - user's preference)
- More segments (514,529 vs previous ~50,000)
- Visible pixel staircasing at low zoom

### Decision 2: Disable Endpoint Snapping
**Context:** Snapping collapsing 60% of segments into degenerate blobs

**Options Considered:**
1. **Reduce snap distance** - Already tried 10px → 1.5px, still 60% degenerate
2. **Better snapping algorithm** - Prevent same-segment snapping
3. **Disable entirely** - Trust chain merging for connectivity

**Decision:** Disable entirely (Option 3)

**Rationale:**
- With polylines, segments are 1-2 pixels long
- ANY snapping distance will collapse nearby segments
- Chain merging should provide connectivity

**Trade-offs:**
- No post-processing to fix junction gaps
- Relies entirely on chain merging (which isn't working perfectly)

---

## What Worked ✅

1. **Eliminating Tumors by Disabling Snapping**
   - What: Removed endpoint snapping that was collapsing segments
   - Why it worked: Polyline segments too short for any snapping distance
   - Reusable pattern: For polylines, skip post-processing that assumes longer segments

2. **Chain Merging Concept**
   - What: Merge multiple chains into one continuous path before creating segments
   - Why it worked: Prevents multiple independent polylines per border
   - Reusable pattern: Process data into final form before rendering, not after

---

## What Didn't Work ❌

1. **Reducing Snap Distance for Polylines**
   - What we tried: 10px → 6px → 4px → 1.5px
   - Why it failed: Polyline segments are 1-2 pixels long, ANY snapping collapses them
   - Lesson learned: Post-processing algorithms designed for long segments don't work with polylines
   - Don't try this again because: Fundamental incompatibility between snapping and short segments

2. **Adding Junction Pixels (All Attempts)**
   - What we tried: Add all junctions, add only adjacent junctions, use BFS to bridge gaps
   - Why it failed: Adds pixels that may not be naturally connected, creates multiple chains
   - Lesson learned: Can't force connectivity by injecting pixels after extraction
   - Don't try this again because: Need to fix root cause (why aren't borders naturally reaching junctions?)

3. **Preventing Same-Segment Snapping**
   - What we tried: Check if P0 and P3 belong to same segment before snapping
   - Why it failed: Problem wasn't P0/P3 of same segment, it was ALL segments collapsing to same point
   - Lesson learned: When 90% of segments are degenerate, the algorithm is fundamentally wrong
   - Don't try this again because: Band-aid on wrong approach

---

## Problems Encountered & Solutions

### Problem 1: "Tumor" Artifacts on Borders
**Symptom:** Visual blobs/balls on polylines creating distorted borders

**Root Cause:** Endpoint snapping collapsing P0 and P3 to same point
- Logs showed: `P0:(3064.4,326.0) ... P3:(3064.4,326.0)` - identical points
- 463,837 degenerate segments (90% of total) at 10px snap distance
- Even at 1.5px: 305,800 degenerate (59% of total)

**Investigation:**
- Tried: Skip degenerate in FitCurve → Didn't work, created after fitting
- Tried: Prevent same-segment snapping → Still 90% degenerate
- Tried: Reduce snap distance to 1.5px → Still 60% degenerate
- Found: Polyline segments are 1-2 pixels long, incompatible with snapping

**Solution:**
Disable endpoint snapping entirely - chain merging provides connectivity

**Why This Works:** Removes destructive post-processing incompatible with short segments

**Pattern for Future:** Don't apply post-processing designed for long segments to short polylines

### Problem 2: Multiple Independent Polylines per Border
**Symptom:** Clumping at junctions, visible gaps, separate lines instead of one continuous border

**Root Cause:** Chaining creates multiple chains per border (e.g., "Chains: 3"), each rendered as independent polyline

**Investigation:**
- Logs showed: "Chains: 3, Bézier segments: 25" for first border
- Each chain processed separately in foreach loop
- Endpoint snapping tried to fix after the fact (failed)

**Solution:**
Added `MergeChains()` to connect chains into one continuous path before creating segments

**Why This Works:** Processes chains into final form before segmentation

**Pattern for Future:** Merge/preprocess data structures before final conversion, not after

### Problem 3: "Wild Lines" from Tiny Peninsulas
**Symptom:** Random lines crossing through provinces from 1-2 pixel artifacts

**Root Cause:** Map compression artifacts or tiny peninsulas creating valid but tiny borders

**Investigation:**
- Tried: MIN_BORDER_PIXELS = 5 → Still wild lines
- Tried: MIN_BORDER_PIXELS = 10 → Still visible

**Solution:**
❌ NOT SOLVED - Filtering not aggressive enough or not catching all cases

**Pattern for Future:** May need more sophisticated artifact detection (aspect ratio? length-to-width?)

### Problem 4: Junction Connectivity
**Symptom:** Borders not meeting at 3-way junctions despite junction detection and chain merging

**Root Cause:** UNKNOWN - Multiple theories:
1. Chains not naturally reaching junction pixels
2. Junction pixels not in border extraction
3. Chain merging not prioritizing junction connection points
4. Natural pixel extraction missing junction pixels

**Investigation:**
- Tried: Add all junction pixels → Created disconnected chains
- Tried: BFS bridging to junctions → Found 0 gaps (junctions reachable but not used)
- Tried: Remove junction injection → Better but still broken
- Tried: Add only adjacent junction pixels → Still broken

**Solution:**
❌ NOT SOLVED - "junctions are fucked" per user

**Pattern for Future:** Need to understand WHY borders don't naturally reach junctions in pixel extraction

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update [vector-curve-rendering-pattern.md](../../Engine/vector-curve-rendering-pattern.md) - Document polyline approach and snapping incompatibility
- [ ] Update [2-junction-connectivity-fix.md](2-junction-connectivity-fix.md) - Session 3 tried polylines, partial success

### New Anti-Patterns Discovered

**Anti-Pattern:** Applying Long-Segment Post-Processing to Polylines
- What not to do: Use endpoint snapping or other post-processing designed for long segments
- Why it's bad: Polyline segments are 1-2 pixels long, post-processing collapses them
- Better approach: Pre-process chains before creating segments
- Add warning to: vector-curve-rendering-pattern.md

**Anti-Pattern:** Injecting Disconnected Pixels for Connectivity
- What not to do: Add junction pixels that aren't naturally 8-connected to existing border
- Why it's bad: Creates multiple disconnected chains, breaks chaining algorithm
- Better approach: Fix root cause of why pixels aren't naturally extracted
- Add warning to: vector-curve-rendering-pattern.md

### Code Cleanup Needed
- Junction detection still running (3560ms) but results not reliably used
- BridgeToJunction() code still exists but disabled
- SnapCurveEndpointsAtJunctions() still exists but disabled

---

## Code Quality Notes

### Performance
- **Measured:** 514,529 segments generated (10x more than Bézier approach)
- **Target:** <5000ms total extraction, single draw call rendering
- **Status:** ✅ Extraction time similar (~26s total), GPU handles segment count fine

### Technical Debt
- **Created:** Disabled snapping code (still in file but commented out)
- **Created:** Multiple junction fixing attempts (partially disabled code paths)
- **TODO:** Clean up disabled code if polyline approach is abandoned
- **TODO:** Investigate root cause of junction pixel extraction failure

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Understand junction pixel extraction** - WHY don't borders naturally include junction pixels?
   - Debug: Log which pixels are extracted vs junction pixel positions
   - Check if `ExtractSharedBorderPixels()` is missing junction pixels

2. **Fix tiny border filtering** - Wild lines still visible
   - Try MIN_BORDER_PIXELS = 15 or 20?
   - Or: Check aspect ratio (long thin line = artifact, square blob = real border)

3. **Consider alternative approaches:**
   - Option A: Fix Bézier curve approach (sharp corner detection that actually works)
   - Option B: Fix polyline junction connectivity (solve root cause)
   - Option C: Hybrid approach (polylines for straight sections, curves for junctions)

### Blocked Items
- **Blocker:** Don't understand why junction pixels aren't in natural border extraction
- **Needs:** Debugging/logging of pixel extraction vs junction detection
- **Owner:** Next Claude session

### Questions to Resolve
1. Why doesn't `ExtractSharedBorderPixels()` naturally include junction pixels where 3 borders meet?
2. Are junction pixels actually shared border pixels, or do they belong to a third province?
3. Should we abandon polylines and return to Bézier curves with better loop prevention?
4. What threshold for MIN_BORDER_PIXELS eliminates artifacts without removing real borders?

---

## Session Statistics

**Files Changed:** 2 (`BezierCurveFitter.cs`, `BorderCurveExtractor.cs`)
**Lines Added/Removed:** ~+120/-20
**Key Changes:**
- Rewrote FitCurve() to polyline approach
- Added MergeChains() function
- Disabled endpoint snapping
- Added tiny border filtering (not working)
- Multiple junction fixing attempts (all failed)

**Bugs Fixed:** 1 (tumor artifacts)
**Bugs Introduced/Remaining:** 2 (junction gaps, wild lines)
**Commits:** 0 (not ready to commit)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Polyline approach: `BezierCurveFitter.cs:97-113` - Creates one segment per pixel pair
- Chain merging: `BorderCurveExtractor.cs:655-730` - Merges chains before segmentation
- Snapping disabled: `BorderCurveExtractor.cs:209-216` - Incompatible with short segments
- Junction attempts: `BorderCurveExtractor.cs:448-497` - Multiple failed approaches

**What Changed Since Last Doc Read:**
- Architecture: Switched from curved Bézier to straight polylines
- Implementation: Chain merging added, endpoint snapping removed
- Constraints: Polyline segments too short for post-processing snapping

**Gotchas for Next Session:**
- Watch out for: Polylines fundamentally different from Bézier curves - don't apply same post-processing
- Don't forget: User is tired and frustrated - may want to abandon polyline approach
- Remember: Junction pixel extraction is the ROOT CAUSE that needs investigation
- Consider: Might need to return to Bézier approach with better loop prevention

---

## Links & References

### Related Documentation
- [Vector Curve Rendering Pattern](../../Engine/vector-curve-rendering-pattern.md)
- [Junction Connectivity Fix Session 2](2-junction-connectivity-fix.md)
- [Border Rendering Improvements Session 1](1-border-rendering-improvements.md)

### Code References
- Polyline implementation: `BezierCurveFitter.cs:90-113`
- Chain merging: `BorderCurveExtractor.cs:655-730`
- Snapping disabled: `BorderCurveExtractor.cs:209-216`
- Junction attempts: `BorderCurveExtractor.cs:448-497`
- Tiny border filter: `BorderCurveExtractor.cs:123-134`

---

## Notes & Observations

- User showed Age of History 3 screenshot as reference - wants that exact style
- User quote: "Aren't we just following pixels at this point?" - Yes, polylines literally trace pixel path
- User is tired and frustrated: "Im too tired for this. lets make a log doc and wrap this up"
- Tumors eliminated successfully but junctions still broken
- Multiple junction fixing attempts all failed - suggests wrong approach or misunderstanding root cause
- Polyline segment count increased 10x (50k → 514k) but GPU handles it fine
- May need to abandon polylines and return to Bézier with better loop prevention
- Core issue: Don't understand why junction pixels aren't in natural border extraction

**Key Insight:** We're treating symptoms (add junction pixels after extraction) instead of root cause (why aren't they extracted naturally?)

---

*Session completed: 2025-10-27 - Status: Partial success, major issues remain*

# Junction Snapping, Small Borders, and Chain Merging Issues
**Date**: 2025-10-30
**Session**: 5
**Status**: ⚠️ Partial - Small borders rendering, U-turn issue unresolved
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix three critical border rendering issues: junction connectivity, missing small borders, and U-turn artifacts

**Secondary Objectives:**
- Add tessellation for dense vertex coverage (Paradox approach)
- Implement robust chain merging logic

**Success Criteria:**
- ✅ Borders snap correctly at junctions (no staircase artifacts)
- ✅ Small borders (3-10 pixels) render properly
- ❌ No U-turn artifacts on peninsula/indent borders (STILL BROKEN)

---

## Context & Background

**Previous Work:**
- See: [4-chaikin-smoothing-polyline-simplification.md](4-chaikin-smoothing-polyline-simplification.md)
- Session 4 achieved smooth borders via RDP simplification + Chaikin smoothing
- User reported: "Yo! Look at this. We have smoother borders now. Yippie."

**Current State:**
- Smooth borders working well for large/medium borders
- Three new issues discovered:
  1. Junctions showing staircase artifacts (endpoints not aligned)
  2. Small borders (≤10 pixels) completely invisible
  3. U-turn artifacts where borders backtrack to pick up peninsula pixels

**Why Now:**
- Smooth borders revealed underlying topology issues that were masked by jaggedness
- Critical for final visual quality

---

## What We Did

### 1. Junction Endpoint Snapping
**Files Changed:** `BorderCurveExtractor.cs:405-520`

**Problem:** Borders meeting at junctions had slightly misaligned endpoints after smoothing, creating visible staircase artifacts.

**Implementation:**
```csharp
private void SnapPolylineEndpointsAtJunctions(Dictionary<(ushort, ushort), List<Vector2>> borderPolylines)
{
    const float SNAP_DISTANCE = 2.0f;
    const int GRID_CELL_SIZE = 4;

    // Spatial grid for O(n) neighbor lookup
    var grid = new Dictionary<(int, int), List<(Vector2 point, (ushort, ushort) borderKey, bool isStart)>>();

    // Add all polyline endpoints to spatial grid
    foreach (var kvp in borderPolylines)
    {
        var polyline = kvp.Value;
        if (polyline.Count < 2) continue;

        AddToGrid(grid, polyline[0], kvp.Key, true);  // First point
        AddToGrid(grid, polyline[polyline.Count - 1], kvp.Key, false);  // Last point
    }

    // Find clusters of nearby endpoints (5x5 grid search)
    // Snap all endpoints in cluster to their average position
    // ...
}
```

**Rationale:**
- After Chaikin smoothing, endpoints may be displaced from exact junction pixels
- Spatial grid enables O(n) performance instead of O(n²)
- 2.0px snap distance accounts for smoothing displacement
- Clusters ensure all borders meeting at junction share exact same endpoint

**Result:** ✅ Fixed junction staircase artifacts

**Architecture Compliance:**
- ✅ Follows hot-path performance patterns (O(n) with spatial grid)
- ✅ Post-processing step after smoothing (correct pipeline placement)

### 2. Small Border Rendering
**Files Changed:** `BorderCurveExtractor.cs:125`

**Problem:** Borders with <10 pixels were completely filtered out by `MIN_BORDER_PIXELS = 10` threshold.

**Root Cause:**
```csharp
// OLD: Filter set to 10 pixels
const int MIN_BORDER_PIXELS = 10;
if (borderPixels.Count > 0 && borderPixels.Count < MIN_BORDER_PIXELS)
{
    continue; // Skip this border - too small to be real
}
```

This was filtering out legitimate small borders (5-9 pixels) that users could see in the province bitmap.

**Solution:**
```csharp
// NEW: Filter only truly degenerate borders (1-2 pixels)
const int MIN_BORDER_PIXELS = 3;
if (borderPixels.Count > 0 && borderPixels.Count < MIN_BORDER_PIXELS)
{
    continue; // Skip compression artifacts only
}
```

**Result:** ✅ Small borders (3-10 pixels) now render properly

**Rationale:**
- 1-2 pixel borders are compression artifacts (noise)
- 3-10 pixel borders are real, visible borders that need rendering
- User confirmed: even older Paradox games (EU4) don't render 1-2 pixel segments

### 3. Tessellation for Dense Vertex Coverage
**Files Changed:** `BorderCurveExtractor.cs:1083-1121, 184`

**Context:** Analyzed Paradox's RenderDoc captures - they use ~835k vertices per border at close zoom (12,650 vertices per unit length).

**Implementation:**
```csharp
private List<Vector2> TessellatePolyline(List<Vector2> points, float maxSegmentLength)
{
    List<Vector2> tessellated = new List<Vector2>();
    tessellated.Add(points[0]);

    for (int i = 0; i < points.Count - 1; i++)
    {
        Vector2 p0 = points[i];
        Vector2 p1 = points[i + 1];
        float segmentLength = Vector2.Distance(p0, p1);

        if (segmentLength > maxSegmentLength)
        {
            int subdivisions = Mathf.CeilToInt(segmentLength / maxSegmentLength);
            for (int j = 1; j <= subdivisions; j++)
            {
                float t = j / (float)subdivisions;
                tessellated.Add(Vector2.Lerp(p0, p1, t));
            }
        }
        else
        {
            tessellated.Add(p1);
        }
    }

    return tessellated;
}
```

**Parameters:** `maxSegmentLength: 0.5f` (0.5 pixel spacing = ~2 vertices per pixel)

**Result:** ⚠️ Only ~5% visual improvement (user feedback: "I was expecting a huge difference")

**Analysis:**
- Chaikin smoothing already creates reasonably dense vertices
- Tessellation helps but isn't the main visual quality factor
- Kept for robustness across zoom levels

---

## Decisions Made

### Decision 1: U-Turn Detection Strategy
**Context:** Multi-chain borders create U-turn artifacts when peninsula/indent pixels form separate chains that get merged back onto main border.

**Options Considered:**
1. **Angle-based detection (90° threshold)** - Check angle at connection point
   - Pros: Simple, catches sharp turns
   - Cons: Misses gradual U-turns, depends on first few points only

2. **Direction-based detection (dot product <0.5)** - Compare overall chain directions
   - Pros: Catches gradual U-turns
   - Cons: Too aggressive, blocks legitimate connections (causes gaps)

3. **Combined approach** - Both angle AND direction checks
   - Pros: Comprehensive
   - Cons: Still had failure modes (either too strict or too loose)

4. **Longest chain only** - Discard all chains except longest
   - Pros: Simple, no merge logic needed
   - Cons: Loses real border segments (14-pixel peninsula is legitimate border)

5. **Two-tier filter + simple merge** - Filter tiny chains (≤3px), merge rest without U-turn detection
   - Pros: Removes noise, allows real segments
   - Cons: Brings back U-turn artifacts

**Decision:** Currently attempting #5 with U-turn detection re-added

**Status:** ❌ UNRESOLVED - Still trying different thresholds, no solution yet

**Trade-offs:**
- Strict U-turn detection → Gaps in borders (Case 3)
- Loose U-turn detection → U-turn artifacts (Case 2)
- No U-turn detection → All U-turns render (Case 1 & 2 broken)

**Root Problem:** The chaining algorithm breaks legitimate continuous borders into multiple chains due to small gaps/indents. We're trying to fix a topology problem with heuristics.

---

## What Worked ✅

1. **Junction Endpoint Snapping**
   - What: Spatial grid-based endpoint clustering and averaging
   - Why it worked: Addresses the actual problem (endpoints displaced by smoothing)
   - Reusable pattern: Yes - spatial grid for O(n) neighbor queries
   - Impact: Completely fixed junction staircase artifacts

2. **Lowering MIN_BORDER_PIXELS from 10 to 3**
   - What: Less aggressive filtering of small borders
   - Why it worked: 3-10 pixel borders are real, visible borders
   - Impact: All small borders now render
   - User validation: "EU4 doesn't render 1-2 pixel segments either"

3. **Filtering Tiny Chains Before Merge**
   - What: `significantChains = allChains.Where(chain => chain.Count > 3)`
   - Why it worked: Removes noise (1-3 pixel chains) without losing real segments
   - Impact: Cleaner merge inputs

---

## What Didn't Work ❌

### 1. Aggressive Direction-Based U-Turn Detection (dot product < 0.5)
**What we tried:**
```csharp
float dotProduct = Vector2.Dot(mergedDir, chainDir);
if (dotProduct < 0.5f) // >60° angle
{
    wouldCreateUturn = true;
}
```

**Why it failed:**
- Threshold too strict - rejected legitimate connections
- Caused gaps in borders (Case 3: 1160-2285 border with 14+41 pixel chains)
- User feedback: "Case 3 still not working, border stops at same 2 pixels"

**Lesson learned:** Can't use a single angle threshold - real borders have varying curvature

**Don't try this again because:** Legitimate multi-segment borders can have angles >60° at connection points

### 2. Relaxing Threshold to dot product < -0.3 (>110° angle)
**What we tried:** Made threshold more permissive to allow sharper angles

**Why it failed:**
- Let U-turns through (Case 2 came back)
- Still blocked Case 3
- Couldn't find sweet spot that worked for both cases

**Lesson learned:** The problem isn't about finding the right threshold - it's about the approach

### 3. Longest Chain Only
**What we tried:**
```csharp
int longestIdx = FindLongestChain(allChains);
mergedPath = allChains[longestIdx];
// Discard all other chains
```

**Why it failed:**
- For border 1160-2285: chains [14, 41, 2]
- Kept 41-pixel chain, discarded 14-pixel peninsula
- Peninsula is a real part of the border, not noise

**Lesson learned:** Can't assume all secondary chains are noise - some are legitimate border segments

**Root cause:** Border physically split into multiple sections by indents/gaps

### 4. Distance-Based Backtrack Detection
**What we tried:**
```csharp
// Check if chain end is closer to merged start than to merged end
float backtrackDist = Vector2.Distance(chainEnd, mergedStart);
float progressDist = Vector2.Distance(chainEnd, merged[merged.Count - 1]);
if (backtrackDist < progressDist * 0.5f) // Closer to start = U-turn
{
    wouldBacktrack = true;
}
```

**Why it failed:**
- Peninsula U-turns don't necessarily end up near the start of the merged path
- They curve back along the border edge, not all the way to the beginning
- Distance metric doesn't capture the backtracking behavior

**Lesson learned:** U-turns are about direction reversal, not distance to start point

---

## Problems Encountered & Solutions

### Problem 1: Junction Staircase Artifacts
**Symptom:** Borders meeting at junctions had visible gaps/steps after smoothing

**Root Cause:** RDP simplification + Chaikin smoothing can displace endpoints by 1-3 pixels from original junction pixel positions

**Investigation:**
- Junction detection already existed (`DetectJunctionPixels()`)
- But no post-smoothing endpoint correction
- Multiple borders ending at slightly different positions near junctions

**Solution:**
Implemented `SnapPolylineEndpointsAtJunctions()`:
- Spatial grid for O(n) endpoint clustering
- Find all endpoints within 2px of each other
- Snap to average position
- Ensures all borders at junction share exact coordinate

**Why This Works:** Corrects the displacement caused by smoothing while preserving the smooth curves

**Pattern for Future:** Post-process geometric data after transformations that can displace critical points

### Problem 2: Small Borders Invisible
**Symptom:** Borders 5-10 pixels long were completely missing from rendering

**Root Cause:**
```csharp
const int MIN_BORDER_PIXELS = 10;
if (borderPixels.Count < MIN_BORDER_PIXELS)
    continue; // Filtered out ALL borders <10 pixels
```

**Investigation:**
- Added logging: `if (processedBorders < 3)` only logged first 3 borders
- Needed to check actual filtering: Added counter for skipped borders
- Found: Many 5-9 pixel borders being discarded

**Solution:** Lowered threshold to 3 pixels
```csharp
const int MIN_BORDER_PIXELS = 3; // Only filter 1-2 pixel artifacts
```

**Why This Works:**
- 1-2 pixels = compression artifacts/noise
- 3-10 pixels = real, visible borders in province bitmap
- Matches Paradox's approach (EU4/CK3)

**Pattern for Future:** Filter thresholds should be validated against actual data distribution, not set arbitrarily

### Problem 3: U-Turn Artifacts (ONGOING)
**Symptom:** Borders create visible loops where they backtrack to pick up peninsula/indent pixels

**Root Cause:** Border pixel extraction finds disconnected segments:
- Main border edge: 41 pixels (continuous chain)
- Peninsula indent: 14 pixels (separate chain, not adjacent to main)
- Indent artifacts: 2 pixels (noise chain)

When chaining algorithm (`ChainBorderPixelsSingle`) runs:
1. Starts at random pixel, chains via nearest-neighbor
2. Stops when no adjacent pixel found (gap)
3. Remaining pixels form separate chain(s)

`MergeChains` tries to connect these by closest endpoints, which can create U-turns.

**Investigation:**
- Tried 8 different approaches (angle checks, direction checks, distance checks, combinations)
- All have failure modes:
  - Too strict → gaps in borders (legitimate connections rejected)
  - Too loose → U-turns render (problematic connections allowed)
  - No detection → all U-turns render

**Current Status:** ❌ UNRESOLVED

**Attempted Solutions:**
1. Angle at connection point (>90°) - misses gradual U-turns
2. Direction comparison (dot product <0.5) - too aggressive, blocks real connections
3. Combined angle + direction - still has failure modes
4. Longest chain only - loses real border segments
5. Filter tiny + merge without detection - brings back U-turns
6. Filter tiny + merge with dot product <0.0 - breaks both cases

**Why This Is Hard:**
- Real borders can have sharp angles at peninsula connections (>60°)
- U-turn artifacts ALSO have similar angles
- Can't distinguish "legitimate sharp curve" from "U-turn artifact" with geometry alone

**Possible Root Cause:** The chaining algorithm itself is the problem - it's breaking continuous borders into multiple chains when it encounters small gaps/indents

**Next Steps to Try:**
1. Fix chaining algorithm to not break borders at small gaps
2. Accept that some peninsula indents won't render (like Paradox)
3. Use visual heuristics (check if merged path crosses itself)
4. Machine learning approach (train on good vs bad merges)

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `master-architecture-document.md` - Note junction snapping as part of border rendering pipeline
- [ ] Document MIN_BORDER_PIXELS threshold rationale and tradeoffs

### New Patterns Discovered
**Pattern: Spatial Grid for Geometric Post-Processing**
- When to use: After geometric transformations that can displace critical connection points
- Benefits: O(n) performance for endpoint clustering, scales to 10k+ borders
- Implementation: Fixed-size grid cells, 5x5 neighbor search
- Add to: Border rendering architecture section

### New Anti-Patterns Discovered
**Anti-Pattern: Arbitrary Filtering Thresholds**
- What not to do: Set MIN_BORDER_PIXELS to 10 without validating against actual data
- Why it's bad: Filtered out 50+ legitimate small borders per map
- Lesson: Check data distribution before setting thresholds
- Add warning to: Data filtering guidelines

---

## Code Quality Notes

### Performance
- **Measured:** Junction snapping ~50ms for 10k borders (O(n) with spatial grid)
- **Target:** <100ms for map initialization (from architecture docs)
- **Status:** ✅ Meets target

### Testing
- **Manual Tests:**
  - ✅ Junction connectivity (visually verified, no staircase artifacts)
  - ✅ Small borders render (3-10 pixel borders visible)
  - ❌ U-turn artifacts (still present in 2 out of 3 test cases)

### Technical Debt
- **Created:**
  - U-turn detection code has 3 different approaches tried (old code wrapped in comments)
  - `MergeChains()` vs `MergeChainsSimple()` - duplicate logic
  - Need to clean up once solution found

- **TODOs:**
  - Fix U-turn artifacts (critical)
  - Clean up duplicate merge functions
  - Remove commented-out experimental code

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Fix U-turn artifacts** - Critical visual issue, tried 8 approaches
   - Consider: Fix chaining algorithm instead of merge detection
   - Consider: Accept some peninsula indents won't render (Paradox approach)
   - Consider: Self-intersection detection for merged paths

2. **Clean up merge code** - Remove duplicate functions, commented code

3. **Reduce border width to Paradox value** - Currently 10x (0.002 vs 0.0002) for debugging

### Blocked Items
- **Blocker:** Can't finalize border rendering until U-turn issue resolved
- **Needs:** Better understanding of what Paradox actually does for multi-chain borders
- **Owner:** Need to analyze more Paradox games' border handling

### Questions to Resolve
1. **Do Paradox games even merge multi-chain borders?** - Maybe they just render longest chain and discard rest?
2. **How do we distinguish legitimate peninsula from U-turn artifact?** - Geometry alone might not be sufficient
3. **Is the chaining algorithm the root problem?** - Should we fix how chains are created instead of how they're merged?

### Docs to Read Before Next Session
- Check if any existing architecture docs cover border topology handling
- Review Paradox game screenshots for how they handle peninsula indents

---

## Session Statistics

**Files Changed:** 2
- `BorderCurveExtractor.cs` (multiple edits, ~300 lines changed)
- `BorderMeshGenerator.cs` (logging additions)

**Lines Added/Removed:** +400/-150 (approximate, lots of iteration)
**Key Methods Added:**
- `SnapPolylineEndpointsAtJunctions()` - 115 lines
- `TessellatePolyline()` - 38 lines
- `MergeChainsSimple()` - 87 lines
- `CalculateAngle()` - 13 lines

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Junction snapping: `BorderCurveExtractor.cs:405-520` (✅ WORKS)
- Small border filtering: `BorderCurveExtractor.cs:125` (MIN_BORDER_PIXELS = 3, ✅ WORKS)
- U-turn detection: `BorderCurveExtractor.cs:891-907` (❌ BROKEN, tried many approaches)
- Test cases:
  - Case 1: Original peninsula U-turn (FIXED initially, then broken)
  - Case 2: Green peninsula U-turn (keeps breaking when we fix Case 3)
  - Case 3: Border 1160-2285 gap (breaks when we try to fix Case 2)

**What Changed Since Last Doc Read:**
- Architecture: Added junction snapping post-processing step
- Implementation: MIN_BORDER_PIXELS lowered from 10 to 3
- Constraints: Cannot find U-turn detection threshold that works for all cases

**Gotchas for Next Session:**
- Watch out for: Changing U-turn threshold fixes one case, breaks another (tested 8 times)
- Don't forget: The problem might be the CHAINING algorithm, not the MERGE algorithm
- Remember: User confirmed Paradox games (EU4) don't render 1-2 pixel segments

---

## Links & References

### Related Documentation
- [Previous Session](4-chaikin-smoothing-polyline-simplification.md)
- [Border Mesh Rendering Discovery](1-renderdoc-triangle-strip-border-discovery.md)

### Code References
- Junction snapping: `BorderCurveExtractor.cs:405-520`
- Tessellation: `BorderCurveExtractor.cs:1083-1121`
- Chain merging: `BorderCurveExtractor.cs:817-926`
- Small border filter: `BorderCurveExtractor.cs:123-134`

### External Resources
- Paradox RenderDoc captures: `vertex-pairs.csv` (1.67M vertices for single border at close zoom)

---

## Notes & Observations

**Key Insight:** We're trying to solve a topology problem (borders broken into chains) with geometry heuristics (angle/direction checks). This might be fundamentally the wrong approach.

**Pattern Emerging:** Every time we fix Case 2 (U-turn), Case 3 (gap) breaks. Every time we fix Case 3, Case 2 breaks. This suggests we need a fundamentally different approach, not just threshold tuning.

**User Feedback:**
- "we're going in circles i feel. It also seems brittle."
- "we're not accepting subpar here"
- Wants robust solution that will work for mod maps

**Performance Note:** Tessellation added 5% visual improvement but creates 2-10x more vertices. Worth keeping for zoom robustness.

**Question for Investigation:** Why does the chaining algorithm create multiple chains for a continuous border edge? If we can prevent chains from splitting, we don't need complex merge logic.

---

*Session ended with U-turn artifacts unresolved, about to compact. Will continue investigating root cause (chaining vs merging) in next session.*

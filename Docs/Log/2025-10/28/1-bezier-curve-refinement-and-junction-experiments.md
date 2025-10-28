# Bézier Curve Refinement & Junction Rendering Experiments
**Date**: 2025-10-28
**Session**: 1
**Status**: ⚠️ Partial - Improved curves, junction problem remains unsolved
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix junction overlap blobs from previous session's Bézier curve implementation
- Achieve clean junction rendering where 3+ province borders meet

**Secondary Objectives:**
- Improve curve accuracy to follow province boundaries
- Reduce visual artifacts (gaps, overlaps, knots)

**Success Criteria:**
- Borders curve smoothly along province edges
- Junction points render cleanly without massive black blobs
- Visual quality comparable to Paradox grand strategy games (Imperator Rome reference)

---

## Context & Background

**Previous Work:**
- Previous sessions implemented vector Bézier curve borders with SDF rendering
- SDF approach had accuracy issues ("graffiti" borders not following provinces)
- Switched to fragment shader vector curve evaluation (truly resolution independent)
- **Problem from last session:** Junction blobs where 3+ curves meet, overlapping segments

**Current State:**
- Gentle Bézier curves (0.15x tangent influence) follow province shapes
- Curves accurate within 1.5px tolerance (adaptive fitting)
- BUT: Massive black blobs at junctions due to overlapping curves

**Why Now:**
- User correctly identified that curves need "cleanup pass" to unify at junctions
- Current junction rendering is 10x worse than acceptable Paradox quality standard

---

## What We Did

### 1. Fixed Control Point Tangent Alignment
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/BezierCurveFitter.cs:196-208`

**Problem:** Local pixel-to-pixel tangents at endpoints created perpendicular control point offsets, causing curves to "swing wide" at junctions.

**Solution:** Use overall segment direction (P3 - P0) instead of local tangent estimation.

**Implementation:**
```csharp
// Use OVERALL segment direction for tangents (prevents overshoot at junctions)
Vector2 overallDirection = (P3 - P0).normalized;

// Control points aligned with overall segment direction (no perpendicular offset)
P1 = P0 + overallDirection * alpha0;
P2 = P3 - overallDirection * alpha1;
```

**Result:** Curves became straighter, less unnecessary curvature. User confirmed: "the lines are more straight now, not unnecessarily curved."

### 2. Attempted CPU Junction Unification (FAILED)
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/BorderCurveExtractor.cs:959-1034`

**What We Tried:**
- Build spatial index of curve endpoints
- Find junctions where 3+ segments meet
- Force control points to zero (P1=P0, P2=P3) at junction endpoints

**Why It Failed:**
- Took too long (user reported: "This is taking a really long time")
- Initial O(n³) implementation was 1 billion operations
- Fixed to O(n) with spatial indexing but still didn't solve visual problem
- Setting control points to zero created straight lines but didn't prevent overlapping render regions in fragment shader

**Status:** Disabled in final code

### 3. Attempted Junction Detection via BorderMask (PARTIALLY WORKING)
**Files Changed:**
- `Assets/Archon-Engine/Shaders/BorderDetection.compute:417-507`
- `Assets/Game/Shaders/MapModes/MapModeCommon.hlsl:188-200`

**User's Insight:** "We have a border mask, resolution dependent, could store the junction pixel and one radius around it. Generate borders. Cleanup pass takes the borders and junction pixel cache and smoothes it out somehow."

**Implementation:**
```hlsl
// Compute shader - detect junction pixels
uint uniqueNeighbors[8];
int uniqueCount = 0;

// Count unique neighboring provinces (different from current)
for (int dy = -1; dy <= 1; dy++) {
    for (int dx = -1; dx <= 1; dx++) {
        // ... count unique neighbors ...
    }
}

// Junction = current pixel has 2+ different neighbors
isJunction = (uniqueCount >= 2);

// Write to BorderMask: 0.5 = junction, 1.0 = regular border, 0.0 = interior
```

**Fragment Shader:**
```hlsl
// Skip expensive curve evaluation at junction pixels
bool isJunction = (borderMask > 0.4 && borderMask < 0.6);
if (isJunction) {
    // Render as solid color (resolution independent at fragment shader resolution)
    float3 borderColor = _CountryBorderColor.rgb;
    baseColor.rgb = lerp(baseColor.rgb, borderColor, strength);
    return baseColor;
}
```

**Result:** Created "thin black lines" that user liked aesthetically, BUT they were marking too many pixels as junctions!

### 4. Junction Definition Refinement
**Files Changed:** `Assets/Archon-Engine/Shaders/BorderDetection.compute:462-465`

**Attempted definitions:**
- `uniqueCount >= 2`: Marked EVERY border pixel as junction (entire borders = thin lines)
- `uniqueCount >= 3`: Only marked true 4-way junctions (too sparse, missed 3-way junctions)

**Problem Discovered:** A "3-way junction" definition is ambiguous:
- User meant: "pixel where 3 provinces converge at a point"
- Implementation: "pixel touching 2+ different neighbors" = every border pixel qualifies!

**Current Understanding:**
- Resolution-independent rendering requires either Bézier curves OR solid color fills at fragment shader resolution
- BorderMask alone is pixelated (province map resolution)
- Solid color rendering of BorderMask pixels IS resolution independent but needs proper border detection

---

## Decisions Made

### Decision 1: Use Overall Segment Direction for Tangents
**Context:** Control points were creating perpendicular offsets causing junction overshoot

**Options Considered:**
1. Local tangent estimation (original approach) - creates misaligned control points
2. Overall segment direction - aligns control points with P0→P3 line
3. Zero tangent influence at junctions - requires junction detection first

**Decision:** Chose Option 2 (overall direction)

**Rationale:**
- Simple and always correct
- Prevents perpendicular bulging without needing junction detection
- Maintains gentle curve aesthetic (0.15x influence)

**Trade-offs:** Curves less responsive to local pixel chain direction, but this is acceptable for gentle curves

### Decision 2: Abandon CPU Junction Unification
**Context:** Post-processing curve control points on CPU was too slow

**Options Considered:**
1. O(n³) triple nested loop - 1 billion operations (disaster)
2. O(n) spatial indexing - fast but doesn't solve rendering problem
3. GPU-based junction handling - move problem to fragment shader

**Decision:** Chose Option 3 (GPU approach)

**Rationale:**
- CPU can't prevent fragment shader from rendering overlapping curves
- Need to handle junctions where rendering happens (fragment shader)
- BorderMask approach allows GPU to decide what to render

**Trade-offs:** More complex shader logic, harder to debug

---

## What Worked ✅

1. **Overall Segment Direction for Control Points**
   - What: Using (P3-P0).normalized for both tangents
   - Why it worked: Eliminates perpendicular control point offset
   - Reusable pattern: Yes - always use for gentle curve fitting

2. **User's BorderMask Cleanup Concept**
   - What: Detecting junctions in compute shader, rendering as solid colors in fragment shader
   - Why it worked: Solid color rendering at fragment shader resolution IS resolution independent
   - Reusable pattern: Yes - principle is sound, execution needs refinement

3. **Debug Color Visualization**
   - What: RED=junctions, GREEN=borders, BLUE=other to understand what's being marked
   - Impact: Immediately revealed that junction detection was too broad
   - Reusable pattern: Yes - always visualize mask/data before using it

---

## What Didn't Work ❌

1. **CPU Junction Unification Pass**
   - What we tried: Modify curve control points on CPU to force straight lines at junctions
   - Why it failed: Fragment shader still evaluates curves independently, creating overlapping render regions
   - Lesson learned: Can't fix rendering problems on CPU, must handle in fragment shader
   - Don't try this again because: The problem is WHERE curves render, not HOW they're defined

2. **`uniqueCount >= 2` Junction Detection**
   - What we tried: Mark pixels with 2+ different neighbors as junctions
   - Why it failed: Every border pixel qualifies! Border between A-B has neighbors A and B = 2 different
   - Lesson learned: Need geometric junction detection, not just neighbor counting
   - Don't try this again because: Produces entire border lines instead of junction points

3. **Endpoint Quantization (0.5px grid)**
   - What we tried: Snap curve endpoints to grid to force alignment
   - Why it failed: Made junction knots WORSE by forcing misaligned curves to same point
   - Lesson learned: Alignment doesn't prevent overlapping render regions
   - Don't try this again because: Problem is curves overlapping during rendering, not endpoint positions

---

## Problems Encountered & Solutions

### Problem 1: Junction Blobs from Overlapping Curves
**Symptom:** Massive black regions at 3-way junctions, 10x worse than Paradox acceptable overlaps

**Root Cause:**
- 3 independent border curves (A-B, B-C, A-C) all end at same junction pixel
- Fragment shader finds ALL three curves within 0.5px threshold
- Shader uses `min()` to find closest distance per border type
- Result: All three curves render, creating 3x border intensity = massive blob

**Investigation:**
- Tried: Modifying control points on CPU - didn't affect fragment shader rendering
- Tried: Endpoint quantization - made worse by forcing alignment
- Tried: Using single "closest curve" instead of min per type - still had overlaps from control point bulging
- Found: Problem is in fragment shader rendering multiple overlapping curves

**Solution Attempted (Incomplete):**
```hlsl
// Mark junction pixels in BorderMask (0.5 value)
// Fragment shader skips curve evaluation for junctions
if (isJunction) {
    // Render as solid color instead of evaluating curves
    baseColor.rgb = lerp(baseColor.rgb, borderColor, strength);
    return baseColor;
}
```

**Why This Didn't Fully Work:** Junction detection marked too many pixels (entire borders)

**Pattern for Future:** Solid color rendering at fragment shader resolution IS resolution independent - need better junction detection geometry

### Problem 2: Resolution Independence vs Pixelation
**Symptom:** BorderMask rendering was pixelated, not crisp like curves

**Root Cause:** BorderMask is texture at province map resolution (fixed size), reading it shows pixelation at high zoom

**Investigation:**
- User asked: "I thought this step was resolution dependent? why are the lines so high res here"
- Discovered: The "crisp" thin lines were solid color fills at fragment shader resolution (during debug), NOT BorderMask texture reads
- BorderMask texture itself is pixelated when zoomed

**Solution:** Fragment shader solid color rendering (not texture reads) provides resolution independence

**Why This Works:** Fragment shader runs at screen resolution, solid color fill = resolution independent

**Pattern for Future:** To get resolution-independent rendering:
- Option A: Bézier curves evaluated in fragment shader (current approach, has junction problem)
- Option B: Solid color fills based on BorderMask flags (needs proper junction detection)
- Do NOT: Just sample BorderMask texture and render - that's pixelated

### Problem 3: Tangent Misalignment Creating Perpendicular Bulge
**Symptom:** Curves "swinging wide" at endpoints creating junction overlaps

**Root Cause:** Local pixel-to-pixel tangent (last pixel step) not aligned with overall segment direction

**Investigation:**
- User insight: "you're putting AA on a single pixel and expect clear crisp results?" - AA wasn't the problem
- User insight: "control points need to be literally vertical to overshoot" - control points WERE offset perpendicular!
- Example: Last pixel step = (1,1) diagonal, but overall segment = (20,10) different angle
- Control point P2 = P3 - tangent * distance created perpendicular offset from P0→P3 line

**Solution:**
```csharp
// Use overall segment direction instead of local tangent
Vector2 overallDirection = (P3 - P0).normalized;
P1 = P0 + overallDirection * alpha0;
P2 = P3 - overallDirection * alpha1;
```

**Why This Works:** Control points stay on P0→P3 axis, no perpendicular deviation

**Pattern for Future:** For gentle curves, overall segment direction better than local tangent estimation

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update border rendering docs with resolution independence understanding
- [ ] Document junction detection attempts and why they failed
- [ ] Add pattern: Fragment shader solid color rendering for resolution independence

### New Patterns/Anti-Patterns Discovered

**New Pattern:** Fragment Shader Solid Color Rendering for Resolution Independence
- When to use: Need crisp borders that work at any zoom level
- Benefits: Runs at screen resolution, not texture resolution
- Add to: Border rendering architecture docs

**New Anti-Pattern:** CPU Post-Processing for Fragment Shader Problems
- What not to do: Try to fix rendering issues by modifying geometry on CPU
- Why it's bad: Fragment shader renders independently of CPU modifications
- Add warning to: Performance and rendering docs

**New Anti-Pattern:** Naive Neighbor Counting for Junction Detection
- What not to do: Count unique neighbors and assume that's a junction
- Why it's bad: Border pixels naturally have 2 neighbors (both provinces)
- Add warning to: BorderMask generation docs

### Architectural Decisions That Changed
- **Changed:** Tangent estimation for Bézier curves
- **From:** Local pixel-to-pixel differences at endpoints
- **To:** Overall segment direction (P3 - P0)
- **Scope:** All Bézier curve generation in BezierCurveFitter.cs
- **Reason:** Prevents perpendicular control point offsets

---

## Code Quality Notes

### Performance
- **Measured:** Junction unification pass was too slow (user noticed)
- **Fixed:** Disabled CPU post-processing, moved problem to GPU
- **Status:** ⚠️ Need to profile final GPU approach

### Testing
- **Manual Tests:**
  - Visual inspection at various zoom levels
  - Comparison with Paradox reference screenshots (Imperator Rome)
  - Debug color visualization (RED/GREEN/BLUE) to understand mask values

### Technical Debt
- **Created:**
  - Disabled `UnifyJunctionControlPoints()` method (~70 lines, unused)
  - Junction detection logic needs geometric algorithm, not just neighbor counting
  - Need proper 3-way junction detection
- **TODOs:**
  - Solve junction overlap problem
  - Define precise junction geometry detection algorithm
  - Determine if BorderMask approach or pure Bézier is better long-term

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Decide on approach:** BorderMask solid rendering OR Bézier curves OR hybrid
2. **If BorderMask:** Implement geometric junction detection (not just neighbor counting)
3. **If Bézier:** Implement junction-aware distance calculation in fragment shader
4. **Test at gameplay zoom** to validate if solution meets quality bar

### Blocked Items
- **Blocker:** No clear solution to junction overlap problem yet
- **Needs:** Either better junction detection geometry OR different rendering approach
- **Owner:** Need to research Paradox implementation or try new algorithms

### Questions to Resolve
1. How does Paradox actually handle junctions? Do they use curves or pixel-based borders?
2. Can we detect true geometric junctions (3+ borders converging at a point) in compute shader?
3. Is BorderMask solid rendering path viable long-term or should we fix Bézier approach?
4. What's the performance impact of junction detection in compute shader?

### Docs to Read Before Next Session
- Review previous SDF/vector curve sessions for context
- Check if there are existing junction detection algorithms in literature

---

## Session Statistics

**Files Changed:** 3
- `BezierCurveFitter.cs` - tangent calculation
- `BorderDetection.compute` - junction detection
- `MapModeCommon.hlsl` - junction rendering

**Lines Added/Removed:** ~+150/-50
**Tests Added:** 0 (visual testing only)
**Bugs Fixed:** 1 (tangent misalignment)
**Commits:** 0 (pending)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Junction overlap problem still UNSOLVED
- Gentle curves (0.15x tangent, overall direction) work well aesthetically
- Resolution independence requires fragment shader operations, not texture reads
- BorderMask solid color rendering IS resolution independent but needs proper junction detection
- User quality bar: Paradox grand strategy games (minor overlaps acceptable at 10x zoom, invisible at gameplay zoom)

**What Changed Since Last Doc Read:**
- Tangent calculation: local → overall segment direction
- Junction approach: CPU post-processing → GPU BorderMask detection
- Understanding: BorderMask texture reads are pixelated, solid color fills are not

**Gotchas for Next Session:**
- Don't try CPU post-processing for fragment shader rendering problems
- "Junction = 2+ neighbors" marks entire borders, need better geometric detection
- User loves the thin aesthetic of solid color rendering (saw during debug)
- Pixelation when reading BorderMask ≠ pixelation when using it as a flag for solid rendering

---

## Links & References

### Related Documentation
- Previous session: SDF rendering attempts and curve accuracy fixes
- `CLAUDE.md` - Architecture enforcement, no CPU pixel processing

### Related Sessions
- Previous sessions on Bézier curve implementation
- SDF rendering experiments

### External Resources
- Paradox reference screenshot: Imperator Rome borders at 10x zoom
- User observation: "Those province dotted border junctions are impressive" - they curve into junctions

### Code References
- Tangent calculation: `BezierCurveFitter.cs:196-208`
- Junction detection: `BorderDetection.compute:417-465`
- Junction rendering: `MapModeCommon.hlsl:188-200`

---

## Notes & Observations

- **User insight was correct:** "shouldn't we clean up the old borders after the pass?" - Yes, need post-processing, but in GPU not CPU
- **Key realization:** Fragment shader solid color rendering IS resolution independent (runs at screen resolution)
- **Aesthetic preference:** User prefers thin, crisp borders over thick borders
- **Quality standard:** "we're not accepting subpar crap" - need Paradox-level quality
- **User patience:** Willing to dedicate a week to solve this (unusual for traditional projects)
- **Debugging approach:** Color visualization (RED/GREEN/BLUE) was extremely effective
- **The "thin black lines" user liked:** Were actually junction pixels rendered as solid colors at fragment shader resolution - proves the concept works!

---

*Session ended with junction problem unsolved but better understanding of rendering approaches*

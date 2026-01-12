# BorderMask Threshold Experiments - Finding the Limits
**Date**: 2025-10-28
**Session**: 3
**Status**: ⚠️ Partial - Discovered fundamental limitations of BorderMask approach
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Refine BorderMask threshold rendering to achieve thin, smooth borders with complete coverage

**Secondary Objectives:**
- Fix positioning issue (borders rendering inside provinces)
- Eliminate triple-line artifacts
- Match Imperator Rome border quality

**Success Criteria:**
- Single thin border lines
- Smooth natural curves (not blocky/pixelated)
- Complete coverage (no gaps on straight segments)
- Properly centered on province boundaries

---

## Context & Background

**Previous Work:**
- Session 1: Bézier curves with junction overlap problem
- Session 2: Discovered BorderMask + bilinear filtering creates "good enough" resolution independence
- Session 2: Junction problem "solved by design" (one mask = no overlaps)

**Current State:**
- BorderMask approach committed as primary solution
- Bézier curve extraction disabled (~24s startup cost eliminated)
- Rendering 0.4-0.6 threshold (junction pixels) for perfect smooth shape
- BUT: Incomplete coverage (gaps on straight segments) and positioning issues

**Why Now:**
- Attempting to complete BorderMask approach before committing to more complex solutions
- User identified that smooth lines (0.5 gradient) are offset INSIDE provinces
- Need to determine if BorderMask can meet quality bar or if we need Bézier curves

---

## What We Did

### 1. Investigated Triple-Line Artifact
**Files Changed:** `MapModeCommon.hlsl:107-123`

**Problem:** Three parallel lines (2 thin outer + 1 thick middle) appearing on borders

**Investigation:**
- Outer thin lines: Catching gradient edges from bilinear filtering
- Thick middle line: Catching peak values (1.0) = raw rasterized pixels
- Root cause: Threshold `> 0.9` too wide, catching multiple gradient zones

**Attempted Solutions:**
1. Tightened threshold to `> 0.97` (FAILED - still triple lines but thinner)
2. Narrow range `> 0.95 && <= 1.0` (FAILED - still multiple layers)
3. Inverted range `> 0.1 && < 0.5` (FAILED - wrong shape, outer artifacts)

**Key Discovery:** High threshold values (>0.85) render raw rasterized pixels = blocky pixelated shape matching province bitmap 1:1

### 2. Separated Junctions from Borders
**Files Changed:**
- `BorderDetection.compute:462-517`
- `MapModeCommon.hlsl`

**Rationale:** User wanted BORDER and JUNCTION actually separated to handle them independently

**Implementation Attempts:**

**Attempt A - Three Distinct Values:**
```hlsl
// 0.33 = curves (uniqueCount == 2)
// 0.66 = junctions (uniqueCount >= 3)
// 1.0 = straights (uniqueCount < 2)
```
**Result:** Shape got WORSE - bilinear gradient around 0.33 ≠ 0.5 symmetry

**Attempt B - Keep 0.5 for Curves:**
```hlsl
// 0.5 = curves (uniqueCount == 2) - perfect symmetrical gradients
// 0.66 = junctions (uniqueCount >= 3)
// 1.0 = straights (uniqueCount < 2)
```
**Result:** Back to perfect smooth shape on curves, gaps on straights

**Critical Discovery:** The **specific value 0.5 creates perfect symmetrical bilinear gradients**. Other values (0.33, 0.66) create asymmetrical gradients with worse shapes.

### 3. Attempted to Fix Positioning (Inside Province Issue)
**Files Changed:** `BorderDetection.compute:467-470`

**Problem:** Smooth lines (0.4-0.6 gradient around 0.5) render INSIDE province boundaries

**Attempted Solution:** Increase `searchRadius` from 1 to 2 to shift borders outward

```hlsl
int searchRadius = 2; // 5x5 grid instead of 3x3
```

**Result:** Created THICK borders (multiple pixels wide), made problem worse

**Why It Failed:** Marking more pixels as 0.5 creates thicker band, not shifted position. Need different approach to shift gradient, not widen it.

### 4. Explored Threshold Tuning Extensively
**Files Changed:** `MapModeCommon.hlsl` (multiple iterations)

**Threshold Experiments:**
- `> 0.5`: Too thick, catches entire 0.5 band
- `> 0.7`: Better but still thick on curves
- `> 0.85`: Thin but blocky (approaching raw rasterized pixels)
- `> 0.92`: Very thin but pixelated shape
- `> 0.97`: Ultra-thin but horrible blocky staircase pattern
- `0.4-0.6`: Perfect smooth shape but incomplete (gaps)
- `0.35-0.45`: Weird deformities, offset too far
- `0.48-0.52`: Thin but loses smooth shape quality

**Fundamental Trade-off Discovered:**
- **Low thresholds (0.4-0.6)**: Smooth beautiful shape, but inside provinces and incomplete
- **High thresholds (0.85+)**: Correct position and complete coverage, but blocky/pixelated

---

## Decisions Made

### Decision 1: Return to Bézier Curve Approach
**Context:** After extensive experimentation, BorderMask cannot achieve all requirements simultaneously

**Options Considered:**
1. Keep pushing BorderMask - try more threshold combinations
2. Accept BorderMask limitations - ship with gaps or blocky borders
3. Return to Bézier curves - tackle junction overlap with GPU cleanup pass
4. Hybrid approach - Bézier for curves, BorderMask for junctions

**Decision:** Chose Option 3 (Return to Bézier curves)

**Rationale:**
- BorderMask fundamental limitation: Can't get smooth shape + correct position + complete coverage
- Bilinear filtering around 0.5 creates smooth gradients BUT they're inherently offset
- High threshold values render raw rasterized pixels = blocky by definition
- Imperator Rome clearly uses vector curves (user observation)
- Session 1 proved Bézier curves work well, just need junction handling
- GPU cleanup pass approach was planned in Session 1 but interrupted by BorderMask discovery

**Trade-offs:**
- More complex code (Bézier curve fitting, spatial acceleration)
- Slower startup (~24s for curve extraction)
- Must solve junction overlap problem
- BUT: True resolution independence, smooth curves, precise positioning

**Documentation Impact:** Mark BorderMask approach as "attempted but fundamentally limited"

---

## What Worked ✅

1. **Understanding Bilinear Filtering Gradient Distribution**
   - What: Discovered that 0.5 creates perfect symmetrical gradients, other values don't
   - Why it worked: Explains why 0.4-0.6 looks so good
   - Reusable pattern: When using bilinear filtering for smooth transitions, 0.5 is the sweet spot

2. **Threshold Value Visualization**
   - What: User described seeing "white inside, gray junctions, black edges fading inward"
   - Impact: Helped understand gradient distribution without needing grayscale debug mode
   - Reusable pattern: Ask user to describe what they see for quick debugging

3. **Systematic Threshold Experimentation**
   - What: Tried wide range of thresholds to understand trade-offs
   - Why it worked: Revealed fundamental limitations, not just implementation issues
   - Pattern: Sometimes you need to exhaust possibilities to know when to pivot

---

## What Didn't Work ❌

1. **Combining Junction (0.5) and Border (1.0) Rendering**
   - What we tried: Render both `0.4-0.6` (smooth curves) AND `> 0.85` (straight segments)
   - Why it failed: Creates triple-line artifacts - smooth gradient + blocky peak = visual conflict
   - Lesson learned: Can't mix gradient-based and rasterized rendering approaches
   - Don't try this again because: Fundamentally incompatible rendering styles

2. **Increasing Search Radius to Shift Position**
   - What we tried: `searchRadius = 2` to mark pixels further from border
   - Why it failed: Widened the border band instead of shifting it
   - Lesson learned: BorderMask marks regions, not lines - can't "shift" a region
   - Don't try this again because: Doesn't address root cause (gradient inherently offset)

3. **Using Non-0.5 Values for Curves**
   - What we tried: 0.33 for curves to separate from junctions
   - Why it failed: Bilinear filtering around 0.33 creates asymmetric gradients
   - Lesson learned: 0.5 is special value for symmetrical bilinear interpolation
   - Don't try this again because: Math/GPU behavior, not implementation bug

4. **Tight Threshold Ranges for Thin Lines**
   - What we tried: `0.48-0.52` to render only narrow slice around 0.5
   - Why it failed: Still catches both sides of gradient, doesn't make thinner
   - Lesson learned: Threshold range controls which gradient zone renders, not line width
   - Don't try this again because: Misunderstands how bilinear filtering works

---

## Problems Encountered & Solutions

### Problem 1: Triple-Line Artifact
**Symptom:** Two thin outer lines + one thick middle line on borders

**Root Cause:**
- BorderMask contains 0.5 (curves) and 1.0 (straights) intermixed
- Bilinear filtering creates gradients: 0.5→0.75→1.0
- Threshold catches multiple gradient zones:
  - 0.4-0.6 catches curves (smooth)
  - >0.85 catches peak 1.0 values (blocky)
- Result: Both render, creating layered lines

**Investigation:**
- Tried separating with different values (0.33, 0.66) - worse shape
- Tried tighter thresholds - still caught multiple zones
- Tried rendering only curves - gaps on straights
- Tried rendering only peaks - blocky everywhere

**Solution:** No solution found within BorderMask approach

**Why This Can't Be Fixed:** BorderMask marks thick regions, bilinear filtering creates gradients between values, can't render single thin line at specific position from this data

**Pattern for Future:** Gradient-based rendering fundamentally limited when source data has thick marked regions

### Problem 2: Smooth Shape vs Correct Position
**Symptom:**
- Smooth beautiful lines (0.4-0.6) positioned INSIDE provinces
- Centered lines (>0.85) have blocky pixelated shape

**Root Cause:**
- 0.5 marked pixels are interior border pixels (touching different province)
- Bilinear gradient 0.4-0.6 interpolates between 0.5 and 0.0 (interior) = offset inward
- 1.0 marked pixels are exact rasterized border = correct position but blocky

**Investigation:**
- User insight: "the 0.5 lines are INSIDE the province"
- Tried `searchRadius = 2` to shift outward - made thicker, not shifted
- Tried rendering outer gradient edge (0.35-0.45) - weird deformities
- Discovered: High values (>0.85) render "1:1 to the province bitmap" (user insight)

**Solution:** No solution found - fundamental trade-off in BorderMask approach

**Why This Can't Be Fixed:**
- Bilinear filtering interpolates between marked (0.5) and unmarked (0.0) pixels
- Gradient is inherently between these positions, can't be "shifted"
- High threshold approaches raw rasterized pixels = loses smooth gradient benefit

**Pattern for Future:** Bilinear filtering creates smooth transitions but positioning is deterministic based on source data. Can't have smooth gradient AND arbitrary positioning.

### Problem 3: Complete Coverage vs Smooth Shape
**Symptom:**
- Curves (0.4-0.6) look perfect but have gaps on straight segments
- Adding straights (>0.85) creates triple-line artifacts

**Root Cause:**
- `uniqueCount == 2` only marks curves/bends as 0.5
- Straight segments have `uniqueCount < 2`, marked as 1.0
- Can't render both without mixing smooth and blocky rendering

**Investigation:**
- Tried marking all borders as 0.5 - created thick borders with searchRadius=2
- Tried rendering both 0.5 and 1.0 - triple-line artifacts
- Tried using 0.33/0.66/1.0 for separation - worse shapes

**Solution:** No solution found - can't achieve both simultaneously

**Why This Can't Be Fixed:** Need uniform smooth gradient everywhere for consistent rendering, but BorderMask marks thick regions not thin lines

**Pattern for Future:** Texture-based approaches mark regions. Vector approaches define lines. For thin borders, need vector representation.

---

## Architecture Impact

### Documentation Updates Required
- [x] Document BorderMask approach limitations
- [ ] Update border rendering strategy: Bézier curves primary, BorderMask abandoned
- [ ] Document "0.5 creates perfect symmetrical gradients" finding

### Key Findings That Changed Understanding

**Finding 1: Bilinear Filtering Sweet Spot**
- 0.5 value creates perfect symmetrical gradients due to GPU bilinear interpolation
- Other values (0.33, 0.66) create asymmetrical gradients
- This is GPU/math behavior, not implementation choice
- Add to: Texture filtering guidelines

**Finding 2: High Threshold = Rasterized Pixels**
- Threshold >0.85 approaches raw texture data
- User observation: "1:1 to the province bitmap"
- Smooth gradient benefit lost as threshold approaches peak values
- This explains why it's impossible to get thin + smooth + complete with BorderMask
- Add to: BorderMask limitations documentation

**Finding 3: BorderMask Marks Regions, Not Lines**
- searchRadius controls region thickness, not positioning
- Can't "shift" a marked region to different position
- Bilinear filtering creates gradients between marked and unmarked pixels
- Gradient positioning is deterministic, not tunable
- Add to: When to use BorderMask vs vector approaches

### Architectural Decisions That Changed
- **Changed:** Primary border rendering approach
- **From:** BorderMask with bilinear filtering + threshold
- **To:** Bézier curves with GPU junction cleanup pass (back to Session 1 plan)
- **Scope:** Entire border rendering system
- **Reason:** BorderMask fundamentally cannot achieve smooth + positioned + complete simultaneously

---

## Code Quality Notes

### Performance
- **Measured:** BorderMask extremely fast (texture sample + threshold)
- **Bézier curves:** ~24s startup cost for curve extraction
- **Trade-off:** Accepting slower startup for better runtime quality

### Testing
- **Manual Tests:** Extensive threshold experimentation
  - Tested: 0.4-0.6, 0.35-0.45, 0.48-0.52, >0.7, >0.85, >0.92, >0.97
  - Tested: Combined rendering, inverted ranges, narrow slices
  - Tested: searchRadius 1 vs 2
  - Tested: Different mask values (0.33, 0.5, 0.66, 1.0)

### Technical Debt
- **Created:**
  - BorderMask approach code remains but will be replaced
  - Multiple threshold experiments in git history
- **Resolved:**
  - Determined BorderMask not viable - clear path forward
- **Next:**
  - Re-enable Bézier curve extraction
  - Implement GPU junction cleanup pass

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Re-enable Bézier curve extraction** in `BorderComputeDispatcher.cs` - uncomment `InitializeSmoothBorders()`
2. **Implement GPU junction handling** - Use BorderMask 0.66 values to detect junctions in fragment shader
3. **Junction cleanup logic** - When near junction, render special handling (average, unified curve, or solid fill)
4. **Test at gameplay zoom** - Verify quality meets Imperator Rome standard

### Blocked Items
None - clear path forward with Bézier + junction cleanup approach

### Questions to Resolve
1. **Junction rendering approach:** Evaluate multiple curves and average? Single unified curve? Solid fill at center?
2. **Performance acceptable?** 24s startup + curve evaluation cost vs BorderMask speed
3. **Can we optimize curve extraction?** Parallel processing, caching, or algorithmic improvements?

### Docs to Read Before Next Session
- Session 1 log: Review junction overlap problem and attempted solutions
- `BezierCurveFitter.cs`: Understand current curve generation (0.15x tangent, overall direction)
- `MapModeCommon.hlsl`: Review `ApplyBordersVectorCurvesSpatial()` implementation

---

## Session Statistics

**Files Changed:** 2 (many iterations)
- `BorderDetection.compute` - junction/curve/straight separation attempts
- `MapModeCommon.hlsl` - extensive threshold experimentation

**Lines Added/Removed:** ~+50/-30 (net, with many iterations)
**Tests Added:** 0 (extensive manual testing)
**Bugs Fixed:** 0 (discovered limitations, not bugs)
**Commits:** 2 (save points with perfect smooth shape)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- **CRITICAL:** BorderMask approach has fundamental limitations - abandoned after extensive experimentation
- 0.5 value creates perfect symmetrical bilinear gradients (GPU math behavior)
- High thresholds (>0.85) render raw rasterized pixels = blocky by definition
- Can't achieve smooth shape + correct position + complete coverage with BorderMask
- Returning to Bézier curves with GPU junction cleanup pass (Session 1 plan)
- User confirmed Imperator Rome uses vector curves

**What Changed Since Last Doc Read:**
- Architecture: BorderMask NOT primary solution despite Session 2 breakthrough
- Session 2 was correct about bilinear filtering creating smooth gradients
- BUT: Can't solve positioning and completeness issues within that approach
- Back to complex but proven Bézier curve system

**Gotchas for Next Session:**
- Don't try more BorderMask threshold tricks - thoroughly exhausted
- User patient but wants quality ("not accepting subpar crap")
- Junction overlap was close to solved in Session 1, just needs GPU cleanup pass
- Keep Session 1's gentle curves (0.15x tangent, overall direction) - those work well

---

## Links & References

### Related Documentation
- [Session 1: Bézier Curve Refinement](1-bezier-curve-refinement-and-junction-experiments.md) - Junction overlap problem, planned GPU cleanup pass
- [Session 2: BorderMask Breakthrough](2-bordermask-rendering-breakthrough.md) - Bilinear filtering discovery
- [CLAUDE.md](../../../CLAUDE.md) - Architecture enforcement

### Code References
- Threshold experiments: `MapModeCommon.hlsl:95-123`
- Junction separation: `BorderDetection.compute:499-517`
- Bézier curve extraction (disabled): `BorderComputeDispatcher.cs:170-283`

---

## Notes & Observations

**User Insights:**
- "0.5 lines are INSIDE the province" - correctly diagnosed positioning issue
- "1:1 to the province bitmap" - identified that high thresholds render raw pixels
- "Imperator Rome clearly uses vector curves" - confirmed by visual comparison
- "We're not giving up on this razor thin border dream" - quality bar is high

**Session Tone:**
- Systematic experimentation to exhaust possibilities
- Multiple iterations trying to make BorderMask work
- User patient through many rebuilds and tests
- Eventual realization: Sometimes simpler approach isn't actually simpler
- Wild observation: "Wild to think that complex system seems more likely than the bordermask method, but whatever"

**The Irony:**
- Session 2: "Breakthrough! BorderMask solves everything, delete Bézier curves!"
- Session 3: "Actually... BorderMask can't do what we need, back to Bézier curves"
- Lesson: Simple solutions attractive but must validate they actually meet requirements

**Technical Insights:**
- Bilinear filtering 0.5 creates symmetrical gradients (GPU math)
- Threshold approaching 1.0 loses gradient benefit (approaches raw texture)
- Region-based marking can't create line-based positioning
- Sometimes you need to exhaust "simpler" approach to know when to use complex solution

---

*Session ended with decision to return to Bézier curve approach and implement GPU junction cleanup pass*

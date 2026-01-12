# Hybrid Distance Field + BorderMask Breakthrough
**Date**: 2025-10-29
**Session**: 5
**Status**: ✅ Complete - Working hybrid approach discovered
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix BorderMask to work with distance field borders for smooth anti-aliased rendering

**Secondary Objectives:**
- Understand why BorderMask smooth curves disappeared
- Implement visualization to debug hybrid approach
- Find optimal threshold values for border width

**Success Criteria:**
- Smooth red BorderMask curves visible and aligned with black distance field borders
- Adjustable border width via threshold tuning
- Understanding of how bilinear filtering creates gradient zones

---

## Context & Background

**Previous Work:**
- Session 4: Implemented distance field borders but they were pixelated
- Session 2-3: Had smooth BorderMask curves working (0.4-0.6 gradient zone)
- BorderMask approach was abandoned for distance field in later sessions

**Current State:**
- Distance field provides accurate border detection but pixelated shape
- BorderMask was broken (no smooth gradients visible)
- Two separate border rendering methods not working together

**Why Now:**
- User had screenshot from Session 2-3 showing smooth red curves
- Wanted to combine smooth BorderMask boundaries with accurate distance field detection
- "Cookie cutter" hybrid approach seemed promising

---

## What We Did

### 1. Restored BorderMask Bilinear Filtering
**Files Changed:**
- `BorderDetection.compute:496-517`
- `DynamicTextureSet.cs:107-112`

**Problem:** BorderMask showing only thick pixelated lines, not smooth curves

**Investigation:**
- Checked if BorderMask was being generated (✅ logs confirmed)
- Visualized raw BorderMask as grayscale - saw "no dark gray at all"
- Suspected BorderMask values changed or bilinear filtering broken

**Root Cause Discovery:**
Commit 1c7ea2f changed BorderMask values from:
- OLD: `uniqueCount >= 2` → 0.5 (curves/junctions), creating wide smooth gradient zones
- NEW: `uniqueCount == 1` → 0.5, `uniqueCount == 2` → 0.66, `uniqueCount >= 3` → 0.75

This reduced the number of 0.5 pixels drastically, shrinking the bilinear gradient zones.

**Solution:**
Reverted to Session 2 approach:
```hlsl
if (uniqueCount >= 2)
{
    maskValue = 0.5; // Curves/junctions - creates smooth 0.4-0.6 gradient with bilinear filtering
}
else
{
    maskValue = 1.0; // Straight borders
}
```

**Why This Works:**
- `uniqueCount >= 2` marks most border pixels (any curve, bend, or junction)
- Bilinear filtering on 0.5 values creates smooth gradients: 0.0 → 0.1 → ... → 0.4 → 0.5 → 0.6 → ... → 1.0
- The 0.4-0.6 zone is heavily interpolated, creating smooth curves

### 2. Implemented Hybrid Border Rendering
**Files Changed:** `MapModeCommon.hlsl:138-183`

**Approach:** Combine distance field (accurate detection) with BorderMask (smooth boundaries)

**Implementation:**
```hlsl
bool insideDistanceBorder = (distPixels < 3.0);      // Distance field detects border zone
bool inBorderMaskGradient = (borderMask > borderMaskMin); // BorderMask provides smooth boundary

if (insideDistanceBorder && inBorderMaskGradient)
{
    baseColor.rgb = float3(0, 0, 0); // Render black border
}
```

**Key Insight:** AND operation means:
- Distance field provides accurate "where borders are"
- BorderMask provides smooth "cookie cutter" boundary
- Only render where BOTH agree

**Debug Visualization:**
Added red overlay showing BorderMask gradient zone:
```hlsl
if (borderMask > borderMaskMin && borderMask < borderMaskMax)
{
    baseColor.rgb = float3(1, 0, 0); // Red debug lines
}
```

### 3. Discovered Border Width Control via Threshold Tuning
**Files Changed:** `MapModeCommon.hlsl:155-157`

**Experimentation Process:**
- Started with 0.4-0.6 (original Session 2 values)
- Tried 0.0-1.0 (pixelated - hit raw texture boundaries)
- Tried 0.1-0.9 (smoother)
- Tried 0.2-0.8 (better)
- Settled on 0.45-0.55 (smooth and tight)

**Critical Discovery:**
Tighter thresholds (closer to 0.5) = **smoother curves**!

**Why:** Tighter range samples the most heavily interpolated part of the bilinear gradient, farthest from raw 0.5 pixels. Wide ranges catch the edges where bilinear filtering meets raw pixels (pixelated).

**Trade-off:**
- Tighter range (0.45-0.55) = smoother shape, thinner borders
- Wider range (0.3-0.7) = thicker lines but more pixelated edges

---

## Decisions Made

### Decision 1: Revert to uniqueCount >= 2 for BorderMask
**Context:** BorderMask smooth curves disappeared after commit 1c7ea2f

**Options Considered:**
1. Keep cardinal-direction detection (0.5/0.66/0.75 values) - accurate junctions but small gradients
2. Revert to uniqueCount >= 2 (0.5 for most borders) - wider gradients, better smoothing
3. Hybrid: different values for different border types

**Decision:** Chose Option 2 (Revert to uniqueCount >= 2)

**Rationale:**
- Session 2-3 proved this creates beautiful smooth curves
- Wide 0.5 zones create large bilinear gradient regions (0.4-0.6 usable)
- Cardinal detection was solving wrong problem (junction accuracy) vs our need (smooth curves)

**Trade-offs:**
- Less accurate junction detection (but we don't need it for rendering)
- More 0.5 pixels marked (but that's what creates smooth gradients)

### Decision 2: Use Hybrid Approach Instead of Pure Distance Field
**Context:** Distance field alone produces pixelated borders

**Options Considered:**
1. Pure distance field - accurate but pixelated
2. Pure BorderMask - smooth but incomplete coverage (gaps on straights)
3. Hybrid (distance field AND BorderMask) - combine strengths

**Decision:** Chose Option 3 (Hybrid)

**Rationale:**
- Distance field provides accurate detection (knows where ALL borders are)
- BorderMask provides smooth curved boundaries (bilinear gradients)
- AND operation acts as "cookie cutter" - distance field fills shape defined by BorderMask

**Trade-offs:**
- More complex (two textures + two sampling operations)
- BorderMask still follows pixelated province boundaries (resolution dependent)
- BUT: achieves smooth anti-aliased borders with good coverage

---

## What Worked ✅

1. **Reverting to Session 2 BorderMask Generation**
   - What: uniqueCount >= 2 → 0.5 marking pattern
   - Why it worked: Creates wide zones of 0.5 pixels for bilinear filtering to work on
   - Reusable pattern: Yes - when using bilinear filtering for smoothing, need enough source pixels

2. **Debug Visualization with Red Overlay**
   - What: Render red lines showing BorderMask gradient zone on top of black borders
   - Impact: Immediately showed perfect alignment, confirmed hybrid approach working
   - Pattern: Always visualize both inputs when debugging combined rendering

3. **Threshold Tuning Discovery**
   - What: Found that tighter thresholds = smoother curves
   - Why it worked: Samples most interpolated part of gradient, avoids raw pixel edges
   - Reusable: Counterintuitive but important - tight thresholds on gradients give best quality

---

## What Didn't Work ❌

1. **Trying to "Shift" BorderMask Position via Thresholds**
   - What we tried: Adjusting borderMaskMin/Max to bring red lines closer together
   - Why it failed: Thresholds only control LINE WIDTH, not LINE POSITION
   - Lesson learned: Bilinear gradient position is fixed by GPU, can only choose which slice to render
   - Don't try this again because: Gradient spread determined by texture resolution vs screen resolution

2. **Using Smoothstep to Create Tighter Visualization**
   - What we tried: Apply smoothstep to borderMask values to create narrower red lines
   - Why it failed: Didn't change anything visible - underlying gradient still same width
   - Lesson learned: Can't compress a bilinear gradient via math in fragment shader
   - Don't try this again because: Gradient width is baked in by GPU texture sampling

3. **Attempting to Make "Cookie Cutter" Tighter via Shader**
   - What we tried: Various approaches to narrow the BorderMask gradient
   - Why it failed: Gradient width is fixed by bilinear filtering on GPU
   - Lesson learned: Only control is which part of gradient to render (threshold range)
   - Root issue: BorderMask generated from raw province pixels = follows pixelated boundaries

---

## Problems Encountered & Solutions

### Problem 1: BorderMask Smooth Curves Disappeared
**Symptom:** User saw thick pixelated lines instead of smooth red curves from Session 2-3

**Root Cause:**
- Commit 1c7ea2f changed junction detection to cardinal-only
- Changed mask values: 0.5 (uniqueCount=1), 0.66 (uniqueCount=2), 0.75 (uniqueCount=3)
- Drastically reduced number of 0.5 pixels, shrinking bilinear gradient zones

**Investigation:**
- Checked logs: BorderMask generation running ✅
- Visualized as grayscale: saw borders but no dark gray (no 0.5 values)
- Checked git history: found commit that changed mask value assignments

**Solution:**
Reverted BorderDetection.compute to mark uniqueCount >= 2 as 0.5:
```hlsl
if (uniqueCount >= 2)
    maskValue = 0.5; // Creates smooth 0.4-0.6 gradient
else
    maskValue = 1.0; // Straights (not rendered)
```

**Why This Works:** More pixels marked as 0.5 = wider gradient zones = smoother curves when sampled with bilinear filtering

**Pattern for Future:** When using bilinear filtering for smoothing, need sufficient source pixels. Small isolated values don't create good gradients.

### Problem 2: Bilinear Filtering Not Taking Effect
**Symptom:** BorderMask showing hard edges instead of smooth gradients

**Root Cause:** Initially thought filterMode wasn't being applied

**Investigation:**
- Checked DynamicTextureSet.cs: filterMode = Bilinear ✅
- Tried setting filterMode AFTER Create() (Unity quirk)
- Realized issue was actually mask value distribution, not filtering

**Solution:** Once we reverted to uniqueCount >= 2, bilinear filtering worked perfectly

**Why This Works:** Bilinear filtering was working all along, just needed proper source data (0.5 pixels in right places)

**Pattern for Future:** When bilinear filtering "not working", check if source data is suitable for interpolation

### Problem 3: Understanding Border Width Control
**Symptom:** Confusion about how to make borders tighter/thinner

**Root Cause:** Misunderstanding relationship between threshold range and rendering

**Investigation:**
- Tested 0.0-1.0: pixelated (hitting raw texture boundaries)
- Tested 0.1-0.9: smoother
- Tested 0.2-0.8: smoother still
- Tested 0.45-0.55: smoothest!

**Discovery:** Tighter range (closer to 0.5) = SMOOTHER curves because sampling most interpolated part of gradient

**Solution:** Use tight threshold range (0.45-0.55) for smoothest results

**Why This Works:**
- Bilinear gradient: 0.0 (interior) → ... → 0.4 → 0.5 → 0.6 → ... → 1.0 (other side)
- 0.45-0.55 samples only heavily interpolated values, far from raw pixels
- Wide ranges (0.0-1.0) catch edges where interpolation meets raw data = pixelated

**Pattern for Future:** When using bilinear gradients, tighter thresholds centered on interpolated region give smoothest results

---

## Architecture Impact

### Architectural Decisions That Changed
- **Changed:** Border rendering approach
- **From:** Pure distance field (Session 4)
- **To:** Hybrid distance field + BorderMask cookie cutter
- **Scope:** Border rendering pipeline in MapModeCommon.hlsl
- **Reason:** Distance field accurate but pixelated, BorderMask provides smooth boundaries

### Key Constraint Identified
**BorderMask Resolution Dependency:**
- BorderMask generated from raw province pixels = follows pixelated boundaries
- Bilinear filtering smooths but can't eliminate underlying pixel structure
- Gradient width fixed by texture resolution vs screen resolution
- Cannot make "cookie cutter tighter" beyond what bilinear filtering provides

**Implication:** Current hybrid approach has fundamental limit on border smoothness

### Next Architecture Decision Required
**Question:** How to get fully smooth borders without pixel-boundary artifacts?

**Options for Next Session:**
1. Generate BorderMask FROM distance field (resolution independent)
2. Return to Bézier curve approach (Session 1)
3. Accept current quality and tune for best compromise

---

## Code Quality Notes

### Performance
- **Measured:** Two texture samples per pixel (BorderMask + BorderDistanceTexture 9-tap)
- **Target:** Single-pass rendering acceptable for map rendering
- **Status:** ✅ Performance acceptable

### Testing
- **Manual Tests:** Extensive threshold tuning (0.0-1.0, 0.1-0.9, 0.2-0.8, 0.45-0.55)
- **Visual Verification:** Red overlay debug confirmed perfect alignment
- **Scale Testing:** Works at various zoom levels

### Technical Debt
- **Created:** BorderMask still resolution dependent (follows raw province pixels)
- **TODO:** Consider generating BorderMask from distance field for full resolution independence
- **Compromise:** Current approach "good enough" but not theoretically perfect

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Implement BorderMask generation from distance field** - Make fully resolution independent
2. **Test if distance-field-based BorderMask eliminates "funky" look** - Hypothesis: should be straighter
3. **Tune final border parameters** - Width, smoothness, colors

### Questions to Resolve
1. Will distance-field-based BorderMask create straighter, cleaner borders?
2. Is the "funky" look caused by following pixelated province boundaries?
3. Can we achieve Paradox-quality borders without Bézier curves?

### Implementation Plan for Next Session
**Generate BorderMask from Distance Field:**
1. Add new kernel to BorderDistanceField.compute
2. After JFA completes, write to BorderMaskTexture based on distance values
3. If distance < 1.0 pixel, write 0.5 to BorderMask
4. This creates BorderMask following smooth distance field centerlines
5. Bilinear filtering on that creates even smoother gradients
6. Result: Fully resolution-independent "cookie cutter"

---

## Session Statistics

**Files Changed:** 3
- BorderDetection.compute (reverted to uniqueCount >= 2)
- DynamicTextureSet.cs (filterMode after Create)
- MapModeCommon.hlsl (hybrid rendering + debug visualization)

**Lines Added/Removed:** ~+40/-30
**Tests Added:** 0 (extensive manual testing via threshold tuning)
**Bugs Fixed:** 1 (BorderMask smooth curves restored)
**Commits:** 3
- 7b31a16: Restore uniqueCount >= 2 junction detection
- 22a1cf5: Add documentation and experimental mesh approach
- 93cba0b: Implement hybrid border rendering

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- **BREAKTHROUGH:** Hybrid distance field + BorderMask works! Red lines perfectly aligned with black borders
- **KEY INSIGHT:** Tighter thresholds (0.45-0.55) = smoother curves (counterintuitive but true)
- **LIMITATION:** BorderMask follows pixelated province boundaries (resolution dependent)
- **NEXT STEP:** Generate BorderMask FROM distance field for full resolution independence

**What Changed Since Last Doc Read:**
- Architecture: Back to using BorderMask (was abandoned in Session 4)
- BorderMask generation: Reverted to uniqueCount >= 2 (Session 2 approach)
- Rendering: Hybrid AND operation (distance field & BorderMask)
- Understanding: Bilinear gradient width is FIXED, can only choose which slice to render

**Gotchas for Next Session:**
- Don't try to "compress" bilinear gradients via shader math - won't work
- Threshold tuning only controls width of rendered slice, not gradient position
- Current "funky" look likely from BorderMask following raw province pixels
- Solution: Generate BorderMask from distance field instead of raw pixels

---

## Links & References

### Related Sessions
- [Session 2: BorderMask Rendering Breakthrough](../28/2-bordermask-rendering-breakthrough.md) - Original smooth curves discovery
- [Session 3: BorderMask Threshold Experiments](../28/3-bordermask-threshold-experiments-and-limits.md) - Threshold tuning attempts
- [Session 4: Distance Field Debugging](4-distance-field-debugging-pixelated-borders.md) - Pixelated borders problem

### Code References
- BorderMask generation: `BorderDetection.compute:496-517`
- Hybrid rendering: `MapModeCommon.hlsl:138-183`
- Debug visualization: `MapModeCommon.hlsl:168-172`

---

## Notes & Observations

**User Insights:**
- "They follow perfectly! Great step forward" - Confirmed hybrid approach working
- "Maximum is pixel, and the farther we are away from those min/max, the smoother shape we get" - User discovered the tighter = smoother principle independently
- "Tight IS the way for good shape (because we use it as cookie cutter)" - Understood the cookie cutter metaphor perfectly

**Session Tone:**
- Lots of back-and-forth experimentation with thresholds
- User patient through many rebuilds and tests
- Breakthrough moment when smooth red curves returned
- Clear path forward: generate BorderMask from distance field

**The Irony:**
- Session 4: Abandoned BorderMask for distance field (pixelated)
- Session 5: Brought back BorderMask to smooth the distance field
- Sometimes you need both approaches working together!

**Technical Revelation:**
- Bilinear filtering gradient width is FIXED by texture resolution vs screen resolution
- Can only control which part of gradient to render (threshold slice)
- Cannot "compress" or "shift" the gradient via shader operations
- This is GPU behavior, not implementation limitation

---

*Session ended with working hybrid approach and clear plan: generate BorderMask from distance field for full resolution independence*

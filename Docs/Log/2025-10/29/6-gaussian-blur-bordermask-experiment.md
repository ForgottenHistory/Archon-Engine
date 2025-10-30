# Gaussian Blur BorderMask Experiment
**Date**: 2025-10-29
**Session**: 6
**Status**: ⚠️ Partial - Blur works but doesn't solve core problem
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Apply Gaussian blur to BorderMask to make borders thinner (bring double lines closer together)

**Secondary Objectives:**
- Understand if post-processing can make resolution-bound borders work better

**Success Criteria:**
- Red debug lines visibly closer together after blur
- Borders thin and smooth like Paradox games

---

## Context & Background

**Previous Work:**
- Session 5: Hybrid distance field + BorderMask approach worked but lines too wide
- Tried generating BorderMask from distance field - failed (still pixelated)
- Reverted to pixel-based BorderMask (uniqueCount >= 2)

**Current State:**
- BorderMask generates smooth gradients via bilinear filtering
- But double red lines are far apart (wide bands)
- User suggested Gaussian blur to concentrate peak values

**Why Now:**
- User experimented in Paint.NET - Gaussian blur concentrates lines to thin peaks
- Theory: Wide 0.5 bands → blur → narrow peaked distribution → render 0.45-0.55 slice = thin lines

---

## What We Did

### 1. Created Gaussian Blur Compute Shader
**Files Changed:** `Assets/Archon-Engine/Shaders/GaussianBlur.compute` (NEW)

**Implementation:**
- Separable Gaussian blur (horizontal pass + vertical pass)
- Initial: Radius 3 (7-tap kernel)
- Evolved to: Radius 5 (11-tap), then radius 9 (19-tap)
- Multiple passes: 1 pass, then 3 passes

**Rationale:**
- Separable approach more efficient than 2D kernel
- Multiple passes compound smoothing effect
- Adjustable radius for tuning strength

### 2. Integrated Blur into BorderMask Pipeline
**Files Changed:**
- `BorderComputeDispatcher.cs:17-21` (added gaussianBlurCompute field)
- `BorderComputeDispatcher.cs:85-86` (added kernel indices)
- `BorderComputeDispatcher.cs:108` (added temp texture)
- `BorderComputeDispatcher.cs:193-210` (runtime shader loading)
- `BorderComputeDispatcher.cs:444-448` (call blur after mask generation)
- `BorderComputeDispatcher.cs:985-1050` (ApplyGaussianBlur method)

**Implementation:**
```csharp
// After pixel-based BorderMask generation
if (gaussianBlurCompute != null)
{
    ApplyGaussianBlur(textureManager.BorderMaskTexture);
}
```

**Key Pattern:**
- Generate BorderMask (wide 0.5 bands at curves)
- Apply separable Gaussian blur (H then V passes)
- Use temporary RenderTexture for intermediate step
- Multiple passes in loop for stronger effect

### 3. Debug Visualization Evolution
**Files Changed:** `MapModeCommon.hlsl:162-179`

**Iterations:**
1. Show 0.45-0.55 slice (expected thin lines after blur)
2. Show entire mask as red intensity (diagnose blur working)
3. Show >0.1 threshold (check if peaks too low)

---

## Decisions Made

### Decision 1: Use Separable Gaussian Blur
**Context:** Need to blur BorderMask after generation

**Options Considered:**
1. 2D Gaussian kernel - slower, single pass
2. Separable Gaussian (H + V) - faster, two passes
3. Box blur - fastest but lower quality

**Decision:** Chose Option 2 (Separable Gaussian)

**Rationale:** Good balance of quality and performance, standard approach in graphics

**Trade-offs:** Two passes vs one, but performance gain worth it

### Decision 2: Dynamic Shader Loading
**Context:** BorderComputeDispatcher created dynamically at runtime

**Options Considered:**
1. Assign shader in Unity Inspector - doesn't work for dynamic objects
2. Load via AssetDatabase.FindAssets at runtime - works in editor
3. Hardcode path with Resources.Load - brittle

**Decision:** Chose Option 2 (AssetDatabase pattern)

**Rationale:** Matches existing pattern for other compute shaders in same class

**Implementation:**
```csharp
#if UNITY_EDITOR
string[] guids = UnityEditor.AssetDatabase.FindAssets("GaussianBlur t:ComputeShader");
if (guids.Length > 0)
{
    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
    gaussianBlurCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
}
#endif
```

---

## What Worked ✅

1. **Gaussian Blur Implementation**
   - What: Separable blur with adjustable radius and multiple passes
   - Why it worked: Clean compute shader design, proper texture binding
   - Reusable pattern: Yes - can apply to any RenderTexture

2. **Dynamic Shader Loading**
   - What: Runtime AssetDatabase pattern for compute shaders
   - Impact: Shader loaded and blur actually ran after fixing

3. **Debug Visualization Iterations**
   - What: Progressive visualization to diagnose issues
   - Impact: Quickly identified shader not loading, then values too low

---

## What Didn't Work ❌

### 1. Distance Field-Based BorderMask Generation
**What we tried:** Generate BorderMask from JFA distance field instead of raw pixels

**Why it failed:**
- Distance field positions still point to pixelated province boundaries
- JFA doesn't smooth input data, just efficiently calculates distances TO that data
- Multiple approaches tried: direct distance, neighbor differences, gradient detection
- All resulted in pixelated or weird patterns (snowflake)

**Lesson learned:** JFA can't create smooth borders from pixelated input

**Don't try this again because:** Distance field is fundamentally tied to input pixel boundaries

### 2. Gaussian Blur Making Lines Thinner
**What we tried:** Apply Gaussian blur to bring double red lines closer together

**Why it failed:**
- **CRITICAL INSIGHT**: Blur smooths existing lines but doesn't change their POSITION
- The two lines are far apart because that's where original 0.5 pixels are
- Blur just makes gradients around those positions
- Doesn't move centerlines closer together

**Lesson learned:** Blur for smoothness, not thinness. Position determined by input data.

**Don't try this again because:** Fundamentally misunderstood what blur does

### 3. Extreme Blur Destroying Peak Values
**What we tried:** 3 passes with radius 9 (very strong blur)

**Why it failed:**
- Averaged values down so much that peak < 0.45
- 0.45-0.55 threshold rendered nothing (no lines at all)
- Over-blurred = lost the signal we wanted to render

**Lesson learned:** Balance blur strength - too much destroys what you're trying to preserve

**Pattern for future:** Start conservative (1 pass, small radius), increase carefully

---

## Problems Encountered & Solutions

### Problem 1: Gaussian Blur Not Running
**Symptom:** No visual difference after implementing blur, lines unchanged

**Root Cause:** `gaussianBlurCompute` shader not assigned (null), method returned early with warning

**Investigation:**
- Checked logs: Found "Gaussian blur compute shader not assigned"
- Realized BorderComputeDispatcher is dynamically created
- Can't assign in Inspector like normal MonoBehaviour

**Solution:**
```csharp
// Add runtime shader loading (same pattern as other shaders)
if (gaussianBlurCompute == null)
{
    #if UNITY_EDITOR
    string[] guids = UnityEditor.AssetDatabase.FindAssets("GaussianBlur t:ComputeShader");
    if (guids.Length > 0)
    {
        gaussianBlurCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(...);
    }
    #endif
}
```

**Why This Works:** Matches existing infrastructure, shader loaded at initialization

**Pattern for Future:** Always check logs when expected behavior doesn't happen

### Problem 2: Over-Blurring Destroying Signal
**Symptom:** After strong blur (3 passes, radius 9), rendering 0.45-0.55 showed nothing

**Root Cause:** Excessive blur averaged peak values way down below threshold

**Investigation:**
- Changed visualization to show all values >0.0
- Saw smooth blurry gradients (blur working!)
- But peak values < 0.45 (threshold too high for blurred data)

**Solution:** Reduced blur strength:
- From: 3 passes, radius 9
- To: 1 pass, radius 5
- Preserves peak values while still smoothing

**Why This Works:** Moderate blur balances smoothing with signal preservation

**Pattern for Future:** Tune parameters incrementally, verify at each step

---

## Architecture Impact

### Key Constraint Discovered
**Gaussian Blur Position Invariance:**
- Blur smooths gradients AROUND existing features
- Does NOT move feature positions
- Cannot bring widely-spaced features closer together
- Only changes gradient shape/width around fixed positions

**Implication:**
- Can't use blur to make borders thinner (move lines closer)
- Can only use blur to make borders smoother (remove pixelation)
- To make borders thinner, must mark fewer pixels in source data

### Fundamental Resolution-Bound Limitation
**Core Issue:** All approaches tried are resolution-bound
- Pixel-based BorderMask → follows province pixel boundaries
- Distance field → calculates distances TO province pixel boundaries
- Blur → smooths around existing pixel-bound positions

**None of these create truly resolution-independent borders.**

---

## Code Quality Notes

### Performance
- **Measured:** Blur adds ~5-10ms at initialization (acceptable)
- **Separable implementation:** Efficient (2 passes better than 2D)
- **Multiple passes:** Each pass compounds cost linearly

### Testing
- **Manual Tests:** Extensive visual verification
- **Debug visualization:** Multiple approaches to diagnose
- **Log verification:** Checked shader loading and execution

### Technical Debt
- **Created:** GaussianBlur.compute shader needs tuning parameters exposed
- **TODOs:** Blur strength could be adjustable at runtime for experimentation

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Dig into RenderDoc captures** - Analyze how Paradox actually generates thin borders
2. **Check other extracted shaders** - Look for clues in vertex/compute shaders
3. **Reconsider vector approach** - Maybe Bézier curves are necessary after all

### Questions to Resolve
1. How does Paradox get thin resolution-independent borders?
2. Are they using vector data instead of raster province maps?
3. Is there post-processing we haven't considered?
4. Can we extract their BorderDistance texture generation shader?

### Current Understanding
**What We Know:**
- Pixel-based approaches follow province boundaries (resolution-bound)
- Distance fields also follow province boundaries (input dependent)
- Blur smooths but doesn't thin
- Bilinear filtering creates gradients but position fixed

**What We Don't Know:**
- How Paradox makes borders thin AND smooth
- Whether they use raster or vector province data
- What post-processing they apply (if any)

---

## Session Statistics

**Files Changed:** 3
- `GaussianBlur.compute` (NEW)
- `BorderComputeDispatcher.cs` (+~100 lines)
- `MapModeCommon.hlsl` (+~15 lines)

**Lines Added/Removed:** ~+115 lines
**Tests Added:** 0 (manual visual testing only)
**Bugs Fixed:** 1 (shader not loading)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Gaussian blur IMPLEMENTED and WORKING
- But blur doesn't solve core problem (doesn't make lines thinner)
- Key insight: Blur smooths, doesn't move positions
- Need different approach for truly thin borders

**What Changed Since Last Doc Read:**
- Added: Gaussian blur post-processing pipeline
- Learned: Blur doesn't bring lines closer together
- Status: Still searching for solution to thin borders

**Gotchas for Next Session:**
- Don't try more blur variations - fundamentally wrong approach for thinning
- Focus on RenderDoc analysis for Paradox's actual technique
- Consider that we might be missing a key preprocessing step

**Critical Realization:**
> "Gaussian blur is not going to bring 2 lines together" - User
>
> Blur smooths gradients AROUND existing positions. To make borders thinner, need to change the positions themselves, not smooth around them.

---

## Links & References

### Related Sessions
- [Session 5: Hybrid Distance Field + BorderMask](5-hybrid-distance-field-bordermask-breakthrough.md) - Previous work on BorderMask
- [Session 2-3: BorderMask Rendering Breakthrough](../28/2-bordermask-rendering-breakthrough.md) - Original smooth curves

### Code References
- Gaussian blur: `GaussianBlur.compute:1-110`
- Integration: `BorderComputeDispatcher.cs:985-1050`
- Visualization: `MapModeCommon.hlsl:168-179`

---

## Notes & Observations

**User Insights:**
- "I'm being stupid" - User realized blur doesn't move lines closer (correct insight!)
- Paint.NET experiment led to blur idea (good empirical approach)
- "We're clueless" - Honest assessment, need more RenderDoc analysis

**Session Tone:**
- Lots of experimentation (radius 3→9, passes 1→3)
- Good debugging workflow (logs, visualization, iterative testing)
- Hit fundamental limitation - blur not the solution

**The Paradox:**
- We can make borders SMOOTH (blur works for that)
- We can't make borders THIN (blur doesn't move positions)
- Paradox games have BOTH - how?

**Next Direction:**
- More RenderDoc analysis of extracted shaders
- Look for preprocessing or different data representation
- Maybe vector curves unavoidable for truly thin resolution-independent borders

---

*Session ended with realization that Gaussian blur solves wrong problem - makes borders smoother but not thinner. Need to investigate Paradox's actual approach more carefully.*

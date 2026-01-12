# Distance Field Border Rendering - Debugging Pixelated Borders
**Date**: 2025-10-29
**Session**: 4
**Status**: ⚠️ Partial - Borders render but are pixelated despite 4×4 downsampling + 9-tap
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix pixelated border rendering from Session 3 - achieve smooth anti-aliased borders like Imperator Rome

**Secondary Objectives:**
- Eliminate coastal artifacts (black blobs)
- Fix transparency issues
- Resolve junction black dots

**Success Criteria:**
- Smooth, crisp borders matching Imperator Rome quality
- No pixelation visible on curves or diagonals
- Solid opaque borders, not ghostly transparent
- No artifacts at junctions or coastlines

---

## Context & Background

**Previous Work:**
- Session 3 (Oct 29): [3-distance-field-fragment-shader-implementation.md](3-distance-field-fragment-shader-implementation.md) - Implemented distance field + 9-tap but had transparency, blur, junction artifacts
- User provided Imperator Rome screenshot showing perfectly smooth solid black borders
- Key insight: Imperator DOES render solid black borders (not province color blending as initially thought)

**Current State at Session Start:**
- Distance field generation working (JFA + finalize)
- Fragment shader implemented with 9-tap multi-sampling
- Borders rendering but: transparent, pixelated, junction artifacts, coastal black blobs
- Multiple resolution experiments (1/4 vs 1/2) with inconsistent results

**Why Now:**
- User correctly identified that I was going in circles changing shader code repeatedly
- Need systematic debugging approach, not trial-and-error shader edits

---

## What We Did

### 1. Diagnosed Distance Field Inversion Issue
**Files Changed:** `MapModeCommon.hlsl:159-162`

**Problem:** Distance field visualization showed "black in middle, white at edges" (inverted)

**Investigation:**
- Distance field SHOULD store: 0.0 at borders, 1.0 far from borders
- But visualization showed opposite
- Tried inverting with `dist = 1.0 - dist` multiple times

**Root Cause:** Distance field generation at line 209 of BorderDistanceField.compute correctly calculates `distance(currentPos, closestBorderPos)`, giving 0 at borders. But texture cleared to white (1.0) and something was writing inverted values.

### 2. Discovered Texture Binding Mismatch
**Files Changed:** None (investigation only)

**Critical Discovery:** Two separate distance field generation paths running:
1. `GenerateQuarterResolutionDistanceField()` → writes to **BorderDistanceTexture** (1/4 res, R8G8_UNorm)
2. `GenerateDistanceField()` (legacy) → writes to **BorderTexture** (full res, R8G8B8A8_UNorm)

**Evidence from Logs:**
```
[map_rendering.log]
BorderDistanceFieldGenerator: Starting 1/4 resolution distance field generation (target: 1408x512)
BorderDistanceFieldGenerator: Dispatching 1/4 res finalize pass (176x64 thread groups)

BorderDistanceFieldGenerator: Starting dual-channel distance field generation
BorderDistanceFieldGenerator: Dispatching finalize pass (704x256 thread groups)
```

**Impact:** Fragment shader was sampling from BorderDistanceTexture (empty/white), while actual data was in BorderTexture (full resolution).

### 3. Fixed Shader Texture Binding
**Files Changed:** `MapModeCommon.hlsl:153-158`

**Before:**
```hlsl
// Sampling from BorderDistanceTexture (empty!)
float dist = Sample9TapDistance(correctedUV, invSize);
```

**After (temporary fix):**
```hlsl
// Sample from BorderTexture (full resolution .rg) instead
float2 dist2channel = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;
float dist = dist2channel.g; // G channel = province borders
```

**Result:** User saw "black blurry low-resolution line inside" pixelated border shape - confirmed distance field has data!

### 4. Switched Back to Correct Texture After Confirmation
**Files Changed:** `MapModeCommon.hlsl:153-158`

Once confirmed BorderDistanceTexture was being generated (from logs showing both generation passes), switched back to sampling from it:

```hlsl
float2 distanceTextureSize = float2(1408.0, 512.0);
float2 invSize = 1.0 / distanceTextureSize;
float dist = Sample9TapDistance(correctedUV, invSize);
```

### 5. Resolution Changes (Again)
**Files Changed:**
- `DynamicTextureSet.cs:142-143` - Changed between 1/2 and 1/4 resolution multiple times
- `BorderDistanceField.compute:254, 260-262` - Changed between 2×2 and 4×4 downsampling

**Changes Made:**
- Started session at 1/2 resolution (2816×1024) with 2×2 downsampling
- Changed to 1/4 resolution (1408×512) with 4×4 downsampling to match Imperator
- Reasoning: Imperator uses 8192×4096 → 2048×1024 (4:1 ratio), we should match

**Result:** User still reported pixelated borders regardless of resolution

### 6. Multiple Shader Rendering Attempts
**Files Changed:** `MapModeCommon.hlsl:160-176` (changed ~20 times per user's count)

**Attempts Made:**
1. Two-layer rendering (gradient + edge with smoothstep)
2. Aggressive sharpening multiplier (×100)
3. Simple threshold with hard cutoff
4. Smoothstep with various thresholds (1.0, 1.5, 2.0, 3.0 pixels)
5. Debug visualizations (grayscale, RGB channels)
6. Inversion toggles (with and without `dist = 1.0 - dist`)

**User Feedback:** "Get a grip dude" - correctly identified I was changing shaders repeatedly without systematic approach

### 7. Final Working State
**Files Changed:** `MapModeCommon.hlsl:158-171`

**Current Code:**
```hlsl
float dist = Sample9TapDistance(correctedUV, invSize);

// NO INVERSION - distance field stores 0 at borders correctly
float distPixels = dist * 16.0;

// Render solid black borders within 3 pixels of border
float borderThreshold = 3.0;
float borderAlpha = distPixels < borderThreshold ? 1.0 : 0.0;

// Blend black border onto province color
baseColor.rgb = lerp(baseColor.rgb, float3(0,0,0), borderAlpha);
```

**Result:** Borders render as solid black, BUT still pixelated ("high res pixel shape")

---

## Decisions Made

### Decision 1: Confirmed Imperator Uses Solid Black Borders
**Context:** I incorrectly claimed Imperator uses province color blending, not solid black borders

**User Response:** Provided Imperator Rome screenshot showing clear solid black borders with smooth anti-aliasing

**Decision:** Pursue solid black border rendering (correct approach)

**Impact:** Validated that our approach is correct, issue is implementation quality not fundamental design

### Decision 2: Stop Randomly Changing Shader Code
**Context:** User observed ~20 shader changes without systematic debugging

**Decision:** Focus on systematic investigation:
1. Verify distance field generation is working (logs confirm YES)
2. Verify correct texture is bound (fixed texture mismatch)
3. Verify values are in correct range (debug visualizations)
4. Only then adjust rendering logic

**Rationale:** Trial-and-error was wasting time without understanding root cause

### Decision 3: Use 1/4 Resolution with 4×4 Downsampling
**Context:** Tried both 1/2 (2×2) and 1/4 (4×4) resolution multiple times

**Decision:** Stay with 1/4 resolution + 4×4 downsampling to match Imperator's ratio

**Rationale:**
- Imperator: 8192×4096 → 2048×1024 (4×4 blocks)
- Us: 5632×2048 → 1408×512 (4×4 blocks)
- Maintains same downsampling ratio for comparable blur

**Result:** Still pixelated - resolution not the issue

---

## What Worked ✅

### 1. Systematic Log Investigation
- **What:** Checked map_rendering.log to confirm JFA execution
- **Why it worked:** Discovered BOTH generation paths were running, explaining texture confusion
- **Reusable pattern:** Always check logs for actual execution flow, not assumed flow

### 2. Debug Visualizations
- **What:** Multiple grayscale/RGB debug views to see actual distance values
- **Why it worked:** Confirmed distance field HAS data and identified inversion issues
- **Impact:** Ruled out "empty texture" theories, focused on value interpretation

### 3. Imperator Screenshot Analysis
- **What:** User provided clear evidence of smooth solid black borders in Imperator
- **Why it worked:** Corrected my false assumption about province color blending
- **Lesson:** Trust visual evidence over speculation

---

## What Didn't Work ❌

### 1. Repeatedly Changing Shader Rendering Logic
**What we tried:** ~20 different rendering formulas, thresholds, multipliers

**Why it failed:**
- Treated symptoms (rendering) instead of root cause (pixelated distance field data)
- No systematic hypothesis testing
- Changed multiple variables simultaneously

**Lesson learned:** If rendering logic changes don't fix pixelation, the problem is upstream in data generation

**Don't try this again because:** Wasted entire session without addressing core issue

### 2. Resolution Ratio Matching
**What we tried:** Matched Imperator's 4:1 downsampling ratio exactly

**Why it failed:**
- User reported pixelation at BOTH 1/2 and 1/4 resolution
- 4×4 averaging should create blur but doesn't eliminate pixelated shape
- Resolution is not the bottleneck

**Lesson learned:** Distance field downsampling creates blur in VALUE gradients, not SHAPE smoothness

### 3. Inversion Toggle Debugging
**What we tried:** Adding/removing `dist = 1.0 - dist` multiple times

**Why it failed:**
- Created confusion about which direction is correct
- Didn't address underlying issue of pixelated data
- Made debugging harder by introducing uncertainty

**Lesson learned:** Establish correct orientation ONCE with clear debug visualization, then never toggle again

---

## Problems Encountered & Solutions

### Problem 1: Distance Field Appearing Empty/White
**Symptom:** Grayscale visualization showed white everywhere or "barely anything black at edges"

**Root Cause:** Sampling from BorderDistanceTexture which wasn't being written to (only BorderTexture was)

**Investigation:**
- Checked logs: saw full-resolution finalize (704×256 thread groups) not quarter-res (176×64)
- Discovered two separate code paths calling different generation functions
- BorderComputeDispatcher line 534 calls `GenerateDistanceField()` (legacy, writes to BorderTexture)
- BorderComputeDispatcher line 346 calls `GenerateQuarterResolutionDistanceField()` (new, writes to BorderDistanceTexture)

**Solution:**
```hlsl
// Temporarily sample from BorderTexture to confirm data exists
float2 dist2channel = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;
```

**Result:** Confirmed distance field data exists, saw "black blurry line inside pixelated shape"

### Problem 2: Coastal Artifacts (Black Blobs)
**Symptom:** Large black areas near coastlines and islands (from Session 3)

**Root Cause:** Distance field inversion - coastal pixels had dist=1.0 (far) interpreted as borders

**Solution:** Removed inversion after discovering distance field stores 0=border correctly

**Result:** ✅ Coastal artifacts eliminated (user confirmed)

### Problem 3: Borders Not Rendering (Multiple Times)
**Symptom:** "I don't see anything" or "I see no borders" after rendering changes

**Root Cause:** Threshold values mismatched with actual distance field range

**Pattern:**
- distPixels range is [0, 16] where 0=border
- Thresholds below ~2.0 pixels render nothing (too narrow)
- Thresholds above ~5.0 pixels make everything black (too wide)

**Solution:** Used threshold of 3.0 pixels with hard cutoff:
```hlsl
float borderThreshold = 3.0;
float borderAlpha = distPixels < borderThreshold ? 1.0 : 0.0;
```

**Result:** Borders render, but pixelated

### Problem 4: Pixelated Border Shape (UNRESOLVED)
**Symptom:** "High res pixel shape" visible on all borders regardless of rendering approach

**What we know:**
- JFA generates perfectly accurate pixel-perfect distance values ✅
- 4×4 downsampling to 1/4 resolution averages 16 pixels ✅
- 9-tap multi-sampling reads 9 adjacent pixels ✅
- Bilinear filtering enabled on texture ✅
- Distance field data confirmed present via visualization ✅

**What's NOT working:**
- Final rendered borders still show pixel-stepping on curves
- Downsampling + multi-sampling + bilinear should create smooth gradients but doesn't

**Hypothesis:** JFA creates SHARP boundaries (all pixels in 4×4 block have same value or very similar), so averaging doesn't create blur. Need pre-blur or Gaussian filter before downsampling.

**Status:** ⚠️ UNRESOLVED - core issue of this session

---

## Architecture Impact

### No Architectural Changes Made
This session was pure debugging - no new patterns or architectural decisions.

### Technical Debt Created
**Multiple Texture Paths:** Both `GenerateDistanceField()` and `GenerateQuarterResolutionDistanceField()` run, writing to different textures. This is confusing and wasteful.

**Recommendation:** Remove legacy `GenerateDistanceField()` path or add clear conditional to prevent both from running.

---

## Code Quality Notes

### Performance
**Not Measured:** Frame time impact of double generation (both full-res and quarter-res)

**Concern:** Running JFA twice (13 passes each) is wasteful - should only run once

### Technical Debt
**Hardcoded Texture Size:**
```hlsl
float2 distanceTextureSize = float2(1408.0, 512.0); // TODO: Pass as uniform
```
Still hardcoded, should be uniform parameter from C#

**Shader Code Churn:** 20+ changes in single session without clear direction - needs refactoring

---

## Next Session

### Immediate Next Steps (Priority Order)

1. **Investigate why 4×4 downsampling doesn't create blur**
   - Theory: JFA creates sharp boundaries where all 16 pixels in block have nearly identical values
   - Test: Manually blur distance field before downsampling (separable Gaussian?)
   - Alternative: Accept pixelation and use BorderMask approach from Oct 28 instead

2. **Sample distance field texture values programmatically**
   - Use AsyncGPUReadback to read actual pixel values from BorderDistanceTexture
   - Verify 4×4 blocks have varying values vs all-same values
   - Check if bilinear filtering is actually working

3. **Compare our distance field to Imperator's**
   - If possible, capture Imperator's BorderDistanceTexture via RenderDoc
   - Analyze their distance value distribution
   - Identify what pre-processing they do before downsampling

4. **Consider alternative approaches if distance field fails**
   - Return to BorderMask + smoothstep (Session 5, Oct 28) - was 90% working
   - Investigate mesh-based borders with GPU instancing
   - Hybrid: distance field for detection, mesh for rendering

### Questions to Resolve

1. **Why doesn't 4×4 averaging create smooth gradients?**
   - Is JFA output too sharp (all neighbor pixels identical)?
   - Do we need Gaussian blur instead of box filter?
   - Is bilinear filtering actually active at runtime?

2. **What does Imperator's distance field actually look like?**
   - Do they blur before downsampling?
   - Do they use different normalization (maxDistance)?
   - Different JFA step sizes?

3. **Should we abandon distance field approach?**
   - 3+ sessions debugging same pixelation issue
   - BorderMask approach from Oct 28 was close to working
   - Is perfectionism preventing shipping?

### Blocked Items
- **Blocker:** Cannot achieve smooth borders with current distance field approach despite matching Imperator's architecture
- **Needs:** Either find missing smoothing step OR pivot to proven BorderMask approach
- **Decision Point:** How much more time on distance field before declaring it not viable?

---

## Session Statistics

**Time Spent:** ~4 hours
**Files Changed:** 3
- `MapModeCommon.hlsl` (~20 edits to rendering logic)
- `DynamicTextureSet.cs` (resolution changes)
- `BorderDistanceField.compute` (downsampling changes)

**Lines Added/Removed:** +50/-30 (net positive due to debug code)
**Shader Recompiles:** ~25 (excessive due to trial-and-error approach)
**Major Bugs Fixed:** 2 (coastal artifacts, texture binding mismatch)
**Major Bugs Still Open:** 1 (pixelated borders)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- **Borders render but are PIXELATED** - this is the core unsolved problem
- Distance field generation IS working (confirmed via logs and visualization)
- Both BorderTexture (full-res) and BorderDistanceTexture (1/4-res) being generated
- Currently sampling from BorderDistanceTexture with 9-tap + bilinear
- User correctly identified I was making changes without systematic approach

**Critical Files:**
- Fragment shader: `MapModeCommon.hlsl:153-171` - ApplyBorders() simplified to threshold test
- Distance generation: `BorderDistanceFieldGenerator.cs:149-193` - GenerateQuarterResolutionDistanceField()
- Downsampling: `BorderDistanceField.compute:247-312` - FinalizeDistanceFieldQuarterRes kernel (4×4)
- Texture creation: `DynamicTextureSet.cs:136-166` - CreateBorderDistanceTexture() at 1/4 res

**What Changed Since Session 3:**
- Eliminated coastal artifacts (inversion fix)
- Confirmed distance field data exists and is accessible
- Simplified rendering to basic threshold test
- Established 1/4 resolution + 4×4 downsampling as configuration

**Gotchas for Next Session:**
- **Don't randomly change shader code** - user called this out explicitly
- **The pixelation is in the DATA, not the rendering** - changing rendering won't fix it
- **4×4 downsampling should blur but doesn't** - this is the mystery to solve
- **User has Imperator screenshot** - that's our quality target, no excuses
- **Session 5 (Oct 28) had BorderMask working** - fallback option if distance field fails

**Current Hypothesis:**
JFA creates sharp distance boundaries because province edges are pixel-perfect. Even with 4×4 downsampling, if all 16 pixels in a block have the same or very similar distance values, averaging won't create blur. Need to introduce blur BEFORE downsampling (Gaussian filter?) or accept that distance field approach may not achieve smooth curves without additional processing.

---

## Links & References

### Related Documentation
- [border-rendering-approaches-analysis.md](../../Planning/border-rendering-approaches-analysis.md) - All approaches tried
- [3-distance-field-fragment-shader-implementation.md](3-distance-field-fragment-shader-implementation.md) - Previous session
- [2-imperator-rome-renderdoc-investigation.md](2-imperator-rome-renderdoc-investigation.md) - RenderDoc shader analysis

### Related Sessions
- Session 3 (Oct 29): Distance field + 9-tap implementation
- Session 2 (Oct 29): RenderDoc investigation
- Session 5 (Oct 28): BorderMask + bilinear approach (90% working - fallback option)

### Code References
- Fragment shader: `MapModeCommon.hlsl:153-171`
- 9-tap sampling: `MapModeCommon.hlsl:109-130`
- Distance generation: `BorderDistanceFieldGenerator.cs:149-193`
- Downsampling kernel: `BorderDistanceField.compute:247-312`

---

## Notes & Observations

### On Systematic Debugging
- User was absolutely right: changing shader code 20 times without systematic approach was wasteful
- Should have established: data exists → correct texture bound → correct orientation → THEN adjust rendering
- Log investigation (finding both generation paths) was the breakthrough, not shader tweaks

### On Distance Field Quality
- 4×4 downsampling + 9-tap + bilinear SHOULD create smooth gradients
- But user still sees pixelated shape consistently
- This suggests fundamental misunderstanding of how Imperator achieves smoothness
- May need to capture Imperator's actual distance field texture to compare

### On User Feedback
- "Get a grip dude" - valid criticism of repetitive approach
- Imperator screenshot - concrete evidence I was wrong about color blending theory
- "The shape still sucks" - correctly identified core issue I was avoiding

### On Imperator Comparison
- Their borders are PERFECTLY smooth with no visible stepping
- Solid black, crisp, anti-aliased
- Same resolution texture (2048×1024) as our target
- Something in their pipeline creates smoothness we're missing

### On Next Steps Decision
- Option A: Keep debugging distance field (find the missing blur step)
- Option B: Return to BorderMask approach from Oct 28 (90% working, just needed threshold tuning)
- Option C: Accept current quality and move on to other features
- **Recommendation:** Try Option A for ONE more session with clear hypothesis testing, then pivot to Option B if no breakthrough

---

*Session completed: 2025-10-29 [Continued from Session 3]*
*Status: Borders render but pixelated - core smoothing issue unresolved*
*Next session: Investigate why downsampling doesn't blur OR pivot to BorderMask approach*

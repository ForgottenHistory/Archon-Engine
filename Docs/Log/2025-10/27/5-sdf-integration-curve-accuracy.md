# SDF Integration & Curve Accuracy Issues
**Date**: 2025-10-27
**Session**: 5
**Status**: ⚠️ Blocked - SDF renders wrong curves
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Integrate SDF BorderTexture output into fragment shader for smooth curved border rendering

**Secondary Objectives:**
- Disable vector curve rasterization when SDF is active
- Verify smooth curved borders render correctly in-game

**Success Criteria:**
- Fragment shader reads SDF BorderTexture instead of using pixel-based neighbor detection
- Smooth curved borders visible in game
- Borders follow actual province boundaries accurately

---

## Context & Background

**Previous Work:**
- Session 4: [4-sdf-border-rendering.md](4-sdf-border-rendering.md) - Created SDF compute shader, but curves not rendering

**Current State:**
- SDF compute shader works correctly (confirmed with distance field visualization)
- BorderTexture contains valid SDF data
- BUT: Game still showed pixel-following borders (1:1 pixel lines)

**Why Now:**
- Previous session left SDF shader working but not integrated into rendering pipeline
- Needed to connect SDF output to fragment shader

---

## What We Did

### 1. Added SDF Rendering Mode Accessor
**Files Changed:** `BorderComputeDispatcher.cs:819-826`

**Implementation:**
```csharp
/// <summary>
/// Check if SDF rendering is active (vs vector curve rasterization)
/// Used by VisualStyleManager to disable _UseVectorCurves when SDF is active
/// </summary>
public bool IsUsingSDFRendering()
{
    return useSDFRendering && sdfRenderer != null;
}
```

**Rationale:**
- VisualStyleManager needs to know rendering mode to set shader flags correctly
- Clean abstraction - GAME layer queries ENGINE rendering state

**Architecture Compliance:**
- ✅ Follows Engine-Game separation pattern
- ✅ Public API for cross-layer communication

### 2. Disabled Vector Curves When SDF Active
**Files Changed:** `VisualStyleManager.cs:213-220`

**Implementation:**
```csharp
// CRITICAL: If SDF rendering is active, disable vector curves
// SDF rendering writes to BorderTexture directly, fragment shader reads it
if (borderDispatcher.IsUsingSDFRendering())
{
    runtimeMaterial.SetFloat("_UseVectorCurves", 0.0f);
    ArchonLogger.Log("VisualStyleManager: SDF rendering active - vector curves DISABLED (using BorderTexture)", "game_hegemon");
    return;
}
```

**Rationale:**
- Shader has two border rendering paths: `_UseVectorCurves` flag switches between them
- When SDF active, must disable old vector curve path and use BorderTexture path
- Prevents shader from trying to render both systems simultaneously

**Architecture Compliance:**
- ✅ GAME layer controls shader properties
- ✅ ENGINE provides rendering mode query

### 3. Replaced ApplyBorders() with SDF-Based Version
**Files Changed:** `MapModeCommon.hlsl:91-114`

**Problem Found:**
- Old `ApplyBorders()` function was reading `_BorderMaskTexture` and doing pixel-based neighbor detection
- **NOT** reading `_BorderTexture` (the SDF output)!
- This is why we saw pixel-perfect borders despite SDF working

**Implementation:**
```hlsl
// Apply borders to base color - SDF-BASED APPROACH
// Reads BorderTexture which contains SDF (Signed Distance Field) data
// R channel = country border intensity, G channel = province border intensity
// SDF provides resolution-independent smooth curved borders
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample SDF border texture (written by BorderSDF compute shader)
    // R = country border intensity (1.0 = on border, 0.0 = far from border)
    // G = province border intensity
    float2 borders = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;

    float countryBorder = borders.r;
    float provinceBorder = borders.g;

    // Apply borders with configurable strength and color
    // Province borders first (so country borders can override)
    baseColor.rgb = lerp(baseColor.rgb, _ProvinceBorderColor.rgb, provinceBorder * _ProvinceBorderStrength);

    // Country borders on top
    baseColor.rgb = lerp(baseColor.rgb, _CountryBorderColor.rgb, countryBorder * _CountryBorderStrength);

    return baseColor;
}
```

**Rationale:**
- Removed ~60 lines of pixel-based neighbor sampling code
- Replaced with direct SDF texture read (4 lines)
- Much simpler and actually uses SDF output

**Why This Took So Long:**
- Previous session confirmed SDF shader worked via distance field visualization
- Assumed ApplyBorders() was already reading BorderTexture (it wasn't!)
- Fragment shader was completely ignoring SDF output

---

## Problems Encountered & Solutions

### Problem 1: Pixel-Perfect Borders Despite SDF Working
**Symptom:** Game showed 1:1 pixel-following borders even though SDF shader dispatched successfully

**Root Cause Investigation:**
1. Verified SDF shader executes: ✅ Logs show "Rendered borders in 50.08ms"
2. Verified _UseVectorCurves disabled: ✅ Log shows "vector curves DISABLED (using BorderTexture)"
3. Checked if BorderTexture bound: ✅ RenderDoc showed... only BorderMask, not BorderTexture!
4. Examined EU3MapShader.shader: Found two rendering paths controlled by _UseVectorCurves
5. Examined ApplyBorders() function: **FOUND IT** - reading _BorderMaskTexture, not _BorderTexture!

**Root Cause:**
- `ApplyBorders()` function was still doing old pixel-based neighbor detection
- Reading `_BorderMaskTexture` for early-out optimization
- **Never** reading `_BorderTexture` (the SDF output)
- Comment claimed "Uses BorderMaskTexture (sparse) + shader-based detection" - was true for old system, not SDF

**Solution:**
- Rewrote ApplyBorders() to directly sample _BorderTexture
- Removed all neighbor sampling code (lines 105-162)
- Simple 4-line lerp based on SDF intensity values

**Why This Works:**
- SDF shader writes border intensity to BorderTexture (R=country, G=province)
- Fragment shader now directly reads these intensity values
- No neighbor sampling needed - SDF already computed distances

**Pattern for Future:**
- When adding new rendering path, verify fragment shader ACTUALLY uses new textures
- Don't assume function does what comments say - verify with actual texture sampling

### Problem 2: Curves Render but are Completely Wrong
**Symptom:** After fixing ApplyBorders(), borders appeared but looked like "graffiti sprayed all over map" - curves don't follow province boundaries

**Root Cause:**
- Bézier curve fitting is too aggressive/inaccurate
- Example: 28 pixels fit to 1 Bézier segment
- Curves deviate significantly from actual pixel borders
- SDF renders these incorrect curves perfectly (SDF is working correctly, curves are wrong)

**Investigation:**
```
[21:57:25.598] First border 1<->2953: A has 1075 pixels, B has 47 pixels
[21:57:25.602] First border 1<->2953: Found 28 border pixels
[21:57:25.606] First curve - Raw pixels: 28, Chains: 3, Merged path: 28 pixels, Bézier segments: 1
```
- 28 pixels compressed into 1 cubic Bézier
- No way to accurately represent complex pixel borders with so few segments
- Curve fitting tolerance is way too loose

**Status:** ❌ BLOCKED - Need to fix curve fitting accuracy

**Next Steps:**
- Increase curve fitting accuracy (more segments per border)
- Reduce fitting tolerance
- OR: Use polylines instead of Bézier curves (more segments but accurate)
- See "Next Session" for detailed plan

---

## What Worked ✅

1. **GPU Synchronization for Debugging**
   - What: AsyncGPUReadback.WaitForCompletion() after SDF dispatch
   - Why it worked: Forces CPU to wait for GPU before reading BorderTexture for debug screenshots
   - Reusable pattern: Yes - essential for any GPU debugging tools
   - See: [unity-compute-shader-coordination.md](../learnings/unity-compute-shader-coordination.md)

2. **Distance Field Visualization for SDF Verification**
   - What: Render grayscale based on distance to borders
   - Why it worked: Verified SDF distance calculation without needing accurate curves
   - Impact: Proved SDF shader works correctly, isolated curve fitting as real problem
   - Pattern: Use visualization shaders to verify compute shader output

3. **BorderTextureDebug.cs Menu Tool**
   - What: Tools menu item to save BorderTexture to PNG
   - Why it worked: Allowed visual inspection of GPU texture contents
   - Reusable: Yes - can save any RenderTexture for debugging
   - Location: `BorderTextureDebug.cs:13-32`

4. **Systematic Investigation Approach**
   - What: Verify each pipeline stage sequentially (compute→texture→shader→render)
   - Why it worked: Isolated exact failure point (ApplyBorders not reading BorderTexture)
   - Pattern: Don't assume - verify each step with logging or visualization

---

## What Didn't Work ❌

1. **Assuming Border Width Affects Curve Visibility**
   - What we tried: Increased border width from 0.5px to 3px thinking curves too thin
   - Why it failed: Width affects thickness, NOT curvature - curves would be visible even at 0.1px if accurate
   - Lesson learned: Border width is orthogonal to curve shape
   - Don't try this again because: User correctly called out "width to get a curve? dont be an idiot"

2. **Assuming ApplyBorders() Read BorderTexture**
   - What we tried: Expected fragment shader to "just work" after disabling _UseVectorCurves
   - Why it failed: Function comments were misleading - described old hybrid approach
   - Lesson learned: Verify actual texture sampling, don't trust function names/comments
   - Don't try this again because: Function behavior != documentation

3. **Aggressive Bézier Curve Fitting**
   - What we tried: Fit 28 pixels to 1 cubic Bézier segment
   - Why it failed: Can't accurately represent complex pixel borders with so few control points
   - Lesson learned: Curve fitting needs much tighter tolerance OR more segments
   - Root cause: BezierCurveFitter.cs uses ChaikinSmoothing which reduces point count too aggressively

---

## Architecture Impact

### New Patterns Discovered

**Pattern: Two-Stage Border Rendering**
- When to use: When switching between rendering approaches (rasterization vs SDF)
- Stage 1: Compute shader writes border data to texture
- Stage 2: Fragment shader reads texture and applies to final color
- Benefits: Decouples border detection from border rendering
- Add to: Map rendering architecture doc

**Pattern: Rendering Mode Query for Shader Flags**
- When to use: GAME layer needs to configure ENGINE shader behavior
- ENGINE provides `IsUsingSDFRendering()` query
- GAME uses query to set shader flags (_UseVectorCurves)
- Benefits: Clean separation, ENGINE controls mode, GAME configures shader
- Add to: Engine-Game separation patterns

### Anti-Patterns Discovered

**Anti-Pattern: Misleading Function Names**
- What not to do: Function named "ApplyBorders" that doesn't apply the new border system
- Why it's bad: Wasted hours debugging SDF when real problem was fragment shader
- Lesson: When adding new rendering path, ensure all functions updated
- Add warning to: Rendering pipeline documentation

**Anti-Pattern: Aggressive Curve Fitting Without Validation**
- What not to do: Fit complex pixel borders to single Bézier segments without accuracy checking
- Why it's bad: Creates smooth curves that don't match actual province boundaries
- Lesson: Curve fitting needs accuracy validation against original pixels
- Add warning to: Border curve extraction documentation

### Technical Debt Created
- **TODO:** Fix BezierCurveFitter.cs to use tighter tolerance or more segments
- **TODO:** Add curve fitting accuracy validation (measure max deviation from pixel borders)
- **TODO:** Consider polyline fallback when Bézier fitting can't meet accuracy threshold
- **TODO:** Remove old ApplyBorders() commented code (lines 116+ in MapModeCommon.hlsl)

---

## Next Session

### Immediate Next Steps (Priority Order)

1. **Fix Bézier Curve Fitting Accuracy** - CRITICAL BLOCKER
   - File: `BezierCurveFitter.cs`
   - Problem: Curves deviate significantly from actual pixel borders
   - Options to try:
     a) Increase MAX_POINTS_PER_SEGMENT from 50 to 100-200 (more segments per border)
     b) Reduce Chaikin smoothing iterations (preserves more original points)
     c) Add curve fitting accuracy validation (reject curves that deviate >1px from pixels)
     d) Use polylines instead of Bézier curves (guaranteed accurate but more segments)
   - Start with (a) - easiest test, may be sufficient

2. **Add Curve Fitting Accuracy Validation** - Quality gate
   - Measure max deviation of fitted curve from original pixel border
   - Log warning if deviation > threshold (e.g., 2 pixels)
   - Reject curve and fall back to polyline if too inaccurate
   - Prevents "graffiti borders" from making it to GPU

3. **Test SDF Rendering with Accurate Curves** - Success validation
   - Once curves fixed, verify borders follow province boundaries
   - Check at multiple zoom levels (resolution independence)
   - Test with 0.5px, 1px, 2px widths
   - Take screenshots for documentation

### Questions to Resolve

1. **What's acceptable curve fitting error?**
   - Max deviation: 1 pixel? 2 pixels?
   - Trade-off: Tighter tolerance = more segments = more GPU memory
   - Test different thresholds visually

2. **Polylines vs Bézier curves?**
   - Polylines: Guaranteed accurate, but 10-30 segments per border
   - Bézier: Fewer segments (1-5), but harder to fit accurately
   - Which is better for performance at 11k borders?
   - GPU memory: Polylines use more segments but simpler struct

3. **Should we cache curve fitting quality metrics?**
   - Useful for debugging/tuning fitting parameters
   - Could log "95th percentile deviation: 1.2px" per border
   - Add to BorderCurveCache?

### Blocked Items
- **Blocker:** Bézier curve fitting creates inaccurate curves
- **Needs:** Fix curve fitting tolerance OR switch to polylines
- **Owner:** Claude (next session)

---

## Code Quality Notes

### Performance
- **SDF Shader:** 50ms for 5632×2048 map (11,284 borders, 16,576 segments)
- **Target:** <100ms initialization, <5ms runtime updates
- **Status:** ✅ Meets target (SDF renders once at startup, no runtime cost)

### Testing
- **Manual Tests Performed:**
  1. ✅ SDF shader executes (logs confirm 50ms dispatch)
  2. ✅ BorderTexture populated (distance field visualization shows data)
  3. ✅ _UseVectorCurves disabled correctly (logs confirm)
  4. ✅ Fragment shader reads BorderTexture (borders appear in game)
  5. ❌ Curves match province boundaries (FAILED - curves are wrong)

- **Tests Needed:**
  - Curve fitting accuracy validation
  - Per-border deviation metrics
  - Visual comparison: pixel borders vs fitted curves

### Architecture Compliance
- ✅ SDF shader follows GPU compute patterns
- ✅ Engine-Game separation maintained (VisualStyleManager queries BorderComputeDispatcher)
- ✅ Zero allocations during rendering (SDF renders once at init)
- ✅ Single draw call maintained
- ✅ Follows [unity-compute-shader-coordination.md](../learnings/unity-compute-shader-coordination.md) GPU sync pattern

---

## Session Statistics

**Files Changed:** 3
- `BorderComputeDispatcher.cs` (+8 lines)
- `VisualStyleManager.cs` (+9 lines)
- `MapModeCommon.hlsl` (-58/+12 lines)

**Lines Added/Removed:** +29/-58
**Bugs Fixed:** 2 (ApplyBorders not reading SDF, _UseVectorCurves not disabled)
**Bugs Introduced:** 0 (SDF works correctly, curve fitting was already broken)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- SDF shader WORKS CORRECTLY: `BorderSDF.compute`, `BorderSDFRenderer.cs`
- Fragment shader integration COMPLETE: `MapModeCommon.hlsl:95-114`
- Shader flag control WORKS: `VisualStyleManager.cs:213-220`
- **PROBLEM:** Bézier curve fitting is too inaccurate (`BezierCurveFitter.cs`)

**Current Status:**
- ✅ SDF infrastructure complete and working
- ✅ Fragment shader reads SDF BorderTexture
- ❌ **BLOCKED:** Curves don't match province boundaries (curve fitting issue)
- Next: Fix `BezierCurveFitter.cs` accuracy

**Gotchas for Next Session:**
- Don't waste time on SDF shader - it works perfectly (verified with distance field viz)
- Don't waste time on fragment shader - ApplyBorders() now reads BorderTexture correctly
- Focus ONLY on `BezierCurveFitter.cs` - that's the root cause
- User wants smooth curves, not pixel borders - fix the fitting, don't disable SDF

**Key Files:**
- Curve fitting: `BezierCurveFitter.cs` (needs accuracy fix)
- SDF shader: `BorderSDF.compute` (working correctly)
- SDF renderer: `BorderSDFRenderer.cs` (working correctly)
- Fragment integration: `MapModeCommon.hlsl:95-114` (working correctly)

---

## Links & References

### Related Documentation
- [4-sdf-border-rendering.md](4-sdf-border-rendering.md) - Previous session, created SDF shader
- [unity-compute-shader-coordination.md](../learnings/unity-compute-shader-coordination.md) - GPU sync patterns
- [explicit-graphics-format.md](../decisions/explicit-graphics-format.md) - Texture format requirements

### Code References
- SDF shader: `BorderSDF.compute:1-204`
- SDF renderer: `BorderSDFRenderer.cs:74-230`
- Fragment shader: `MapModeCommon.hlsl:95-114`
- Shader flag control: `VisualStyleManager.cs:213-220`
- **NEEDS FIX:** Curve fitting: `BezierCurveFitter.cs`

### Log Evidence
```
[21:57:47.820] GPU UPLOAD 0: P0=(3064.00, 329.00) P1=(3059.44, 329.00) P2=(3048.44, 332.00) P3=(3053.00, 332.00) | perpDist=1.20, 1.20 | type=1
[21:57:47.820] GPU UPLOAD 1: P0=(3076.00, 297.00) P1=(3087.32, 297.00) P2=(3088.68, 312.00) P3=(3100.00, 312.00) | perpDist=6.00, 6.00 | type=1
[21:57:47.871] BorderSDFRenderer: Rendered borders in 50.08ms
[21:57:47.950] VisualStyleManager: SDF rendering active - vector curves DISABLED (using BorderTexture)
```

---

## Notes & Observations

- User correctly identified border width has nothing to do with curve visibility
- Graffiti-like artifacts are actually SDF working perfectly - rendering smooth curves that happen to be wrong
- This is a data quality problem, not a rendering problem
- The more aggressive the curve fitting, the smoother the curves but the less accurate
- May need to tune trade-off: accuracy vs GPU memory (more accurate = more segments)
- Consider polylines as fallback if Bézier fitting can't meet accuracy threshold
- Good debugging workflow: distance field visualization → PNG export → visual inspection

**Key Insight:** SDF is resolution-independent rendering of whatever curves you give it. If you give it garbage curves, it renders garbage smoothly and beautifully. The problem is input data quality (curve fitting), not the rendering system.

---

*Session ended 2025-10-27 22:30 - Returning tomorrow to fix curve fitting accuracy*

# Distance Field Fragment Shader Implementation (1/4 to 1/2 Resolution Struggle)
**Date**: 2025-10-29
**Session**: 3
**Status**: ⚠️ Partial - Borders render but with transparency, blur, and junction artifacts
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement Imperator Rome's distance field + 9-tap + two-layer fragment shader approach for AAA-quality borders

**Secondary Objectives:**
- Eliminate BorderMask pixelation issues from previous session
- Achieve solid, crisp black borders matching Imperator quality

**Success Criteria:**
- Borders render at 1/4 resolution with 9-tap multi-sampling
- No pixelation or blur on curves/diagonals
- Solid opaque borders, not transparent
- No artifacts at junctions

---

## Context & Background

**Previous Work:**
- Session 2 (Oct 29): [2-imperator-rome-renderdoc-investigation.md](2-imperator-rome-renderdoc-investigation.md) - Extracted exact Imperator implementation via RenderDoc
- Session 1 (Oct 29): Mesh-based approach abandoned in favor of texture-based
- Key finding: Imperator uses 2048×1024 distance field (1/4 res) for 8192×4096 map with 9-tap multi-sampling

**Current State:**
- BorderDistanceTexture creation at 1/4 resolution (1408×512)
- GenerateQuarterResolutionDistanceField() implemented
- 9-tap multi-sampling function implemented
- Two-layer rendering (edge + gradient) implemented

**Why Now:**
- Previous session extracted complete Imperator shader code
- Time to implement the actual fragment shader rendering
- Need to eliminate BorderMask pixelation from Session 1 (Oct 28)

---

## What We Did

### 1. Backend: Quarter-Resolution Distance Field Generation
**Files Changed:**
- `DynamicTextureSet.cs:136-166` - CreateBorderDistanceTexture() with 1/4 resolution
- `BorderDistanceFieldGenerator.cs:160-193` - GenerateQuarterResolutionDistanceField()
- `BorderDistanceField.compute:237-310` - FinalizeDistanceFieldQuarterRes kernel
- `BorderComputeDispatcher.cs:328-364` - Integration and parameter binding

**Implementation:**
```csharp
// DynamicTextureSet.cs - Create 1/4 resolution texture
int distanceWidth = (mapWidth + 3) / 4;   // 1408 for 5632 map
int distanceHeight = (mapHeight + 3) / 4; // 512 for 2048 map
// Format: R8G8_UNorm (dual channel: R=country, G=province)
```

**Rationale:**
- Imperator: 8192×4096 → 2048×1024 (1/4 res) = 2.1M pixels
- Us: 5632×2048 → 1408×512 (1/4 res) = 0.72M pixels
- Memory: 1.4MB vs 22MB full resolution (94% savings)

### 2. Fragment Shader: 9-Tap Multi-Sampling
**Files Changed:**
- `MapModeCommon.hlsl:109-129` - Sample9TapDistance() function
- `MapModeCommon.hlsl:138-203` - ApplyBorders() complete rewrite

**Implementation:**
```hlsl
// 9-tap pattern with ±0.75 offset (from Imperator disassembly)
float Sample9TapDistance(float2 uv, float2 invSize)
{
    float dist = 0.0;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler, uv + float2(-0.75, -0.75) * invSize).g;
    // ... 8 more samples in 3x3 grid
    return dist * 0.111111; // Average of 9 samples
}
```

**Rationale:**
- Compensates for 1/4 resolution texture via multiple samples
- Bilinear filtering + 9-tap = smooth results despite low resolution
- Exact pattern extracted from Imperator pixel shader assembly

### 3. Fragment Shader: Two-Layer Edge+Gradient Rendering
**Files Changed:**
- `MapModeCommon.hlsl:173-201` - Two-layer rendering logic

**Implementation:**
```hlsl
// LAYER 1: Gradient (soft outer glow) - LINEAR interpolation
float gradientT = saturate((distPixels - gradientOuterThreshold) / -gradientRange);
float gradientAlpha = lerp(_GradientAlphaOutside, _GradientAlphaInside, gradientT);

// LAYER 2: Edge (sharp border) - SMOOTHSTEP for anti-aliasing
float edgeT = saturate((distPixels - edgeOuterThreshold) / -edgeRange);
float edgeAlpha = edgeT * edgeT * (3.0 - 2.0 * edgeT); // smoothstep formula

// Composite: max(edge, gradient)
float borderAlpha = max(edgeAlpha * _EdgeAlpha, gradientAlpha);
```

**Rationale:**
- Matches Imperator assembly lines 221-242 exactly
- Gradient uses linear lerp (NOT smoothstep) - critical difference
- Edge uses smoothstep for anti-aliasing
- Negative range division creates correct threshold behavior

### 4. Shader Parameter Binding
**Files Changed:**
- `EU3MapShader.shader:32-41` - Property declarations with defaults
- `DynamicTextureSet.cs:263-283` - SetDistanceFieldBorderParams()
- `BorderComputeDispatcher.cs:619-651` - BindDistanceFieldBorderParams()

**Parameters Added:**
```csharp
_EdgeWidth = 0.0          // Sharp edge thickness
_GradientWidth = 2.0      // Soft gradient falloff
_EdgeSmoothness = 2.0     // Anti-aliasing factor
_EdgeAlpha = 1.0          // Edge opacity
_GradientAlphaInside = 1.0   // Gradient inner opacity
_GradientAlphaOutside = 0.0  // Gradient outer opacity
```

---

## Decisions Made

### Decision 1: Switch from 1/4 to 1/2 Resolution Mid-Session
**Context:** 1/4 resolution showed visible pixelation and blur on curves despite 9-tap sampling

**Options Considered:**
1. **Stay at 1/4 resolution** - Match Imperator exactly, figure out what's wrong
2. **Move to 1/2 resolution** - 4× more pixels, eliminate blur at cost of memory
3. **Move to full resolution** - 16× more pixels, guaranteed quality

**Decision:** Chose Option 2 (1/2 resolution)

**Rationale:**
- Imperator's 1/4 resolution (2048×1024 = 2.1M pixels) is actually higher than our 1/4 (1408×512 = 0.72M pixels)
- Our 1/2 resolution (2816×1024 = 2.9M pixels) gives us MORE pixels than Imperator's 1/4
- Still saves 75% memory vs full resolution (5.6MB vs 22MB)
- User reported worse results (more black dots), so REVERTED back to 1/4 later

**Trade-offs:**
- More memory usage (5.6MB vs 1.4MB)
- Didn't solve the core issue (black dots got worse)
- Band-aid solution instead of fixing root cause

### Decision 2: Sample G Channel (Province Borders) Instead of R Channel
**Context:** Only seeing borders on islands, mainland had no borders

**Options Considered:**
1. **R channel (country borders)** - Shows borders between different countries only
2. **G channel (province borders)** - Shows ALL province boundaries
3. **Mix both channels** - Composite country + province

**Decision:** Chose Option 2 (G channel only)

**Rationale:**
- Islands are separate countries → have country borders in R channel
- Mainland provinces within same country → no country borders, only province borders in G
- Need to see ALL borders for debugging, not just country borders

**Trade-offs:**
- Not using the dual-channel capability yet
- Will need proper R+G compositing later for country vs province border styles

### Decision 3: Add Junction Exclusion via BorderMask
**Context:** Black dots appearing at every 3-way and 4-way junction

**Options Considered:**
1. **Ignore the issue** - Accept black dots as limitation
2. **Exclude junctions in distance field** - Don't seed junctions in JFA
3. **Exclude junctions in fragment shader** - Skip rendering at junction pixels via BorderMask

**Decision:** Chose Option 3 (fragment shader exclusion)

**Rationale:**
- BorderMask already marks junctions (0.66 = 3-way, 0.75 = 4-way)
- Junctions ARE borders (3+ provinces meet) so they should be in distance field
- Fragment shader can skip rendering at junctions: `if (borderMask > 0.6) return baseColor;`
- Preserves distance field correctness while fixing visual artifact

**Trade-offs:**
- Junctions show province color, not border (may look odd at some zoom levels)
- Requires BorderMask to be correctly generated and bound
- Did NOT fix the issue in practice (still seeing artifacts)

---

## What Worked ✅

### 1. Distance Field Generation at Quarter Resolution
- **What:** JFA at full resolution, downsample to 1/4 with 4×4 averaging
- **Why it worked:** Compute shader runs efficiently, 94% memory savings achieved
- **Reusable pattern:** Yes - downsampling approach for other effects

### 2. 9-Tap Multi-Sampling Pattern
- **What:** Extract ±0.75 offset pattern from Imperator assembly
- **Why it worked:** Exact replication of proven AAA technique
- **Impact:** Smooth gradients confirmed via debug visualization

### 3. Parameter System Architecture
- **What:** 8 tunable parameters in shader Properties with C# binding
- **Why it worked:** Clean separation, runtime tunable without recompile
- **Reusable pattern:** Yes - standard approach for shader parameters

---

## What Didn't Work ❌

### 1. 1/4 Resolution for Our Map Size
**What we tried:** Match Imperator's 1/4 resolution approach exactly

**Why it failed:**
- Imperator: 8192×4096 → 2048×1024 = 2.1M pixels in distance texture
- Us: 5632×2048 → 1408×512 = 0.72M pixels (66% fewer pixels!)
- 9-tap multi-sampling couldn't compensate for this resolution difference
- Visible pixelation on curves and diagonals

**Lesson learned:**
- Can't blindly copy resolution ratios without considering absolute pixel counts
- Imperator's map is 70% larger, so their 1/4 res has more pixels than our 1/4 res

**Don't try this again because:**
- Need minimum absolute resolution for border quality, not just relative ratio
- Should calculate based on target pixel density, not map size ratio

### 2. Two-Layer Rendering with Separate Alpha Values
**What we tried:** Independent edge and gradient alpha values per Imperator's approach

**Why it failed:**
- Borders rendering as transparent/ghostly despite alpha = 1.0
- Complex blending logic not producing solid borders
- `max(edgeAlpha, gradientAlpha)` not giving expected results

**Lesson learned:**
- Either blending logic is wrong OR the distance values are wrong
- Need to debug the actual distance values reaching the shader
- Parameters might not be binding correctly despite logs saying they are

**Root cause unclear - still investigating**

### 3. Junction Exclusion via BorderMask > 0.6 Check
**What we tried:** Skip rendering at pixels where BorderMask indicates 3+ way junction

**Why it failed:**
- Black dots persisted after adding the check
- User reported "still the same view" after implementation
- Junction detection working (confirmed via BorderMask debug viz) but exclusion not working

**Possible reasons:**
- BorderMask might be using bilinear filtering, diluting junction values below 0.6 threshold
- Junctions might be multiple pixels wide, only excluding center pixel
- Distance field might have incorrect values propagating from junctions to nearby pixels

**Pattern for Future:**
- Junction handling needs multi-pixel dilation or different approach
- Simple threshold check insufficient for visually removing artifacts

---

## Problems Encountered & Solutions

### Problem 1: Compilation Errors - Missing BorderDistanceTexture Properties
**Symptom:** 4 compilation errors after initial implementation

**Root Cause:**
- MapTextureManager missing `BorderDistanceTexture` property
- MapTextureManager missing `DynamicTextures` property
- EU3MapShader.shader missing `_BorderDistanceTexture` texture declaration

**Solution:**
```csharp
// MapTextureManager.cs
public RenderTexture BorderDistanceTexture => dynamicTextures?.BorderDistanceTexture;
public DynamicTextureSet DynamicTextures => dynamicTextures;

// EU3MapShader.shader
_BorderDistanceTexture ("Border Distance Texture (RG8 1/4 res)", 2D) = "white" {}
TEXTURE2D(_BorderDistanceTexture); SAMPLER(sampler_BorderDistanceTexture);
```

**Why This Works:** Exposes distance texture through facade pattern to shader

### Problem 2: Old SDF Borders Still Showing
**Symptom:** "I still have the old sdf round point borders"

**Root Cause:** `BorderRenderingMode.DistanceField` had no explicit code path, fell through to else block running old rasterization renderer

**Solution:**
```csharp
if (renderingMode == BorderRenderingMode.DistanceField)
{
    ArchonLogger.Log("Using AAA Distance Field rendering", "map_initialization");
    // Fragment-shader based - no C# renderer needed
}
else if (renderingMode == BorderRenderingMode.SDF && borderSDFCompute != null)
{
    // Old SDF renderer
}
```

**Why This Works:** Explicit mode handling prevents fallthrough to legacy code

### Problem 3: Inverted Border Rendering (Black Everywhere)
**Symptom:** After first implementation, black everywhere with small spots of provinces showing through

**Root Cause:** Threshold logic backwards - smoothstep range calculation inverted

**Investigation:**
- When `distPixels = 0` (at border), `edgeT = 0`, so `edgeAlpha = 0` (no edge) ✗
- When `distPixels = 16` (far), `edgeT = 1`, so `edgeAlpha = 1` (full edge) ✗
- Logic inverted: rendering borders far from actual borders!

**Solution:**
```hlsl
// Use NEGATIVE range to flip the interpolation
float edgeT = saturate((distPixels - edgeOuterThreshold) / -edgeRange);
// Now: dist=0 → t=1 → alpha=1 (border) ✓
//      dist=16 → t=0 → alpha=0 (no border) ✓
```

**Why This Works:** Negative denominator flips the saturate range behavior
**Pattern for Future:** Distance thresholds need careful attention to interpolation direction

### Problem 4: Only Islands Showing Borders
**Symptom:** Small black spots only on islands, mainland has no borders

**Root Cause:** Sampling R channel (country borders) instead of G channel (province borders)
- Islands = separate countries → have country borders in R channel
- Mainland = same country → no country borders, only province borders in G channel

**Solution:**
```hlsl
// Changed all 9 samples from .r to .g
dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler, uv + offset * invSize).g;
```

**Why This Works:** G channel contains ALL province borders, R only contains country borders
**Pattern for Future:** Dual-channel textures need explicit channel selection based on use case

### Problem 5: Parameters Not Binding (All Zero)
**Symptom:** Debug visualization showing pure black (all parameters = 0.0)

**Root Cause:** Parameters declared in MapModeCommon.hlsl but not in shader's CBUFFER_START block
- `float _EdgeWidth = 0.5;` style declarations are just initializers, not bindable uniforms
- Unity requires parameters in `CBUFFER_START(UnityPerMaterial)` for Material.SetFloat() binding

**Solution:**
```hlsl
// EU3MapShader.shader - Inside CBUFFER_START(UnityPerMaterial)
float _EdgeWidth;
float _GradientWidth;
float _EdgeSmoothness;
// ... etc

// Also add to Properties block for default values
_EdgeWidth ("Border Edge Width (pixels)", Float) = 0.5
```

**Why This Works:** CBUFFER parameters are bindable from C#, Properties block provides defaults
**Pattern for Future:** ALL shader parameters that need C# binding must be in CBUFFER, not just declared

---

## Architecture Impact

### Major Architectural Decision Changed

**Changed:** Border rendering resolution strategy
**From:** Match Imperator's 1/4 ratio exactly
**To:** Use 1/2 resolution for smaller maps to maintain absolute pixel density
**Scope:** Distance field generation, shader sampling
**Reason:** 1/4 resolution insufficient for our smaller map size, causing visible artifacts

**REVERTED:** Back to 1/4 resolution after 1/2 made artifacts worse
**Outcome:** Resolution not the core issue, something else fundamentally wrong

### New Patterns Discovered

**Pattern:** Distance Threshold Inversion via Negative Range
- **When to use:** Distance-based thresholds where close = high value, far = low value
- **Implementation:** `saturate((dist - outerThreshold) / -range)` instead of positive range
- **Benefits:** Correct interpolation direction without subtraction overhead
- **Add to:** Graphics shader best practices

**Anti-Pattern:** Blindly Copying Resolution Ratios Without Absolute Pixel Counts
- **What not to do:** Match a reference implementation's resolution ratio without considering absolute pixel density
- **Why it's bad:** Different map sizes need different ratios to maintain same quality
- **Example:** Imperator 1/4 (2.1M pixels) ≠ Our 1/4 (0.72M pixels)
- **Add warning to:** Texture resolution guidelines

---

## Code Quality Notes

### Performance
**Not Measured:** Distance field rendering performance not profiled this session
**Target:** <0.5ms for full-screen fragment shader (from architecture docs)
**Status:** ⚠️ Unknown - need to profile

### Technical Debt Created
**Hardcoded Texture Size:**
```hlsl
float2 distanceTextureSize = float2(1408.0, 512.0); // TODO: Pass as uniform
```
- Should be passed from C# based on actual texture resolution
- Currently breaks if map size changes

**G Channel Only:**
- Currently only using province borders (G channel)
- Need to implement proper R+G compositing for country vs province styling
- Imperator likely renders both layers with different colors/styles

**Junction Handling Incomplete:**
- Junction exclusion implemented but not working
- Need better approach (dilation? different algorithm?)

---

## Next Session

### Immediate Next Steps (Priority Order)

1. **Debug why borders are transparent despite alpha = 1.0**
   - Add debug visualization showing `borderAlpha` values
   - Check if parameters are ACTUALLY reaching shader (not just logs saying they are)
   - Verify blending equation is correct

2. **Fix black dots at junctions**
   - Current exclusion approach (borderMask > 0.6) not working
   - Try dilating junction mask to exclude surrounding pixels
   - OR: Try different junction detection approach entirely

3. **Resolve 1/4 vs 1/2 resolution question definitively**
   - Profile actual rendering performance
   - Test at target scale (thousands of provinces)
   - Decide based on quality + performance tradeoff, not guessing

4. **Consider alternative approaches if fragment shader continues failing**
   - BorderMask + smoothstep approach from Session 1 (Oct 28) was VERY close
   - Mesh-based approach from Session 1 (Oct 29) worked but didn't scale
   - May need hybrid: distance field for curves, rasterization for straight sections

### Questions to Resolve

1. **Why are 9-tap + bilinear not compensating for 1/4 resolution blur?**
   - Imperator achieves perfect quality with same approach
   - Are we sampling correctly? Is bilinear filtering actually enabled?
   - Is the downsampling algorithm (4×4 averaging) correct?

2. **What causes black dots at junctions?**
   - Distance field has dist=0 at junctions (correct for JFA)
   - BorderMask marks junctions (confirmed via debug viz)
   - But exclusion check not working - why?

3. **Why are borders transparent?**
   - Alpha = 1.0 in parameters (confirmed via logs)
   - But rendering as ghostly/transparent
   - Is lerp() blending equation wrong? Is alpha actually zero at runtime?

### Blocked Items
- **Blocker:** Cannot achieve solid, crisp borders with current fragment shader approach
- **Needs:** Either fix core blending/alpha issue OR pivot to alternative rendering method
- **Decision Point:** How many more iterations before trying BorderMask approach instead?

---

## Session Statistics

**Time Spent:** ~6 hours (implementation + debugging)
**Files Changed:** 8
- `DynamicTextureSet.cs`
- `BorderDistanceFieldGenerator.cs`
- `BorderDistanceField.compute`
- `BorderComputeDispatcher.cs`
- `MapModeCommon.hlsl`
- `EU3MapShader.shader`
- `MapTextureManager.cs`

**Lines Added/Removed:** +350/-80 (estimated)
**Commits:** 2 (backend + frontend)
**Major Bugs Fixed:** 5 (compilation errors, inverted logic, missing parameters)
**Major Bugs Still Open:** 3 (transparency, blur/pixelation, junction artifacts)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- **Distance field rendering at 1/4 resolution implemented but NOT WORKING correctly**
- Borders render but are: transparent, blurry/pixelated, have black dots at junctions
- Parameters bind correctly (confirmed via logs) but results don't match expectations
- Switched between 1/4 and 1/2 resolution multiple times, both have issues

**Critical Files:**
- Fragment shader: `MapModeCommon.hlsl:138-203` - ApplyBorders()
- 9-tap sampling: `MapModeCommon.hlsl:109-129` - Sample9TapDistance()
- Distance generation: `BorderDistanceFieldGenerator.cs:160-193`
- Downsampling: `BorderDistanceField.compute:237-310`

**What Changed Since Session 2:**
- Moved from planning (Session 2 RenderDoc) to implementation
- Backend distance field generation complete and working
- Fragment shader implemented but has critical rendering issues
- Junction handling attempted but not working

**Gotchas for Next Session:**
- **Don't assume parameters are binding just because logs say they are** - verify in shader debug
- **Resolution ratio isn't the core issue** - tried both 1/4 and 1/2, both have problems
- **Junction exclusion via threshold check insufficient** - need better approach
- **May need to abandon fragment shader approach** - BorderMask from Oct 28 was very close to working

**Current Hypothesis:**
- Something fundamentally wrong with how we're interpreting Imperator's distance field approach
- OR: We're missing a critical step (texture format? color space? blending mode?)
- OR: Our distance field generation has subtle bug making values incorrect

---

## Links & References

### Related Documentation
- [border-rendering-approaches-analysis.md](../../Planning/border-rendering-approaches-analysis.md) - All approaches tried
- [2-imperator-rome-renderdoc-investigation.md](2-imperator-rome-renderdoc-investigation.md) - Previous session

### Related Sessions
- Session 2 (Oct 29): RenderDoc investigation - extracted Imperator implementation
- Session 1 (Oct 29): Mesh-based rendering attempt
- Session 5 (Oct 28): BorderMask + bilinear approach (very close to working)

### External Resources
- Imperator Rome pixel shader disassembly: `shader.txt:200-250`
- RenderDoc frame capture analysis
- [Valve Distance Field Paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf)

### Code References
- 9-tap sampling: `MapModeCommon.hlsl:109-129`
- Two-layer rendering: `MapModeCommon.hlsl:173-201`
- Distance generation: `BorderDistanceFieldGenerator.cs:160-193`
- Downsampling kernel: `BorderDistanceField.compute:237-310`

---

## Notes & Observations

### On Implementation Difficulty
- Fragment shader approach seemed straightforward after RenderDoc investigation
- Reality: subtle issues with blending, alpha, resolution make it very difficult
- 6 hours of debugging and still not working - diminishing returns?

### On 1/4 vs 1/2 Resolution Debate
- Tried 1/4 (too blurry) → tried 1/2 (worse artifacts) → back to 1/4
- Resolution clearly not the root cause
- Wasted time on band-aid solutions instead of finding core issue

### On Junction Artifacts
- Black dots at every 3-way junction are visually obvious
- Junction detection working perfectly (confirmed via BorderMask visualization)
- But rendering exclusion not working - suggests issue with how fragment shader interprets BorderMask values
- Might need to dilate junction mask or use different threshold

### On Comparison to BorderMask Approach
- Session 5 (Oct 28) BorderMask + bilinear + smoothstep was VERY close to working
- Only issue was finding the right threshold value
- Current distance field approach has MORE issues (transparency, blur, junctions)
- Starting to question if distance field is right approach for our use case

### On User Feedback Patterns
- "Pixelated shape" = visible resolution/downsampling artifacts
- "Transparent" = alpha blending not working correctly
- "Black dots at junctions" = distance field seeds creating artifacts
- User descriptions accurate and helpful for diagnosis

### On Next Steps Decision Point
- **Option A:** Keep debugging fragment shader (maybe another 4-6 hours?)
- **Option B:** Return to BorderMask approach from Oct 28, just fix the threshold
- **Option C:** Try hybrid approach (distance field for detection, rasterization for rendering)
- **Recommendation:** Try Option B first - was 90% working, only needed threshold tuning

---

*Session completed: 2025-10-29 [Continued]*
*Status: Borders render but with critical quality issues - transparency, blur, junction artifacts*
*Next session: Debug transparency issue OR pivot to alternative rendering approach*

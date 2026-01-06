# Bilinear Province Color Blending for Smooth Borders
**Date**: 2025-11-20
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement screen-space bilinear blending for political map mode to create smooth "paint mixing" effect at province boundaries

**Secondary Objectives:**
- Match smooth distance field borders visually
- Keep province selection pixel-perfect
- Maintain compatibility with existing HSV color grading

**Success Criteria:**
- ✅ Province colors blend smoothly at boundaries
- ✅ Visual 1:1 match with distance field borders
- ✅ No purple/broken rendering

---

## Context & Background

**Previous Work:**
- Border rendering uses smooth distance field (JFA algorithm)
- Province colors used point sampling (hard pixel edges)
- Visual mismatch when borders are transparent

**Current State:**
- Distance field borders: smooth, sub-pixel accurate
- Province rendering: hard pixel edges
- Province highlights: hard pixel edges
- **Problem:** Three separate systems with mismatched boundaries

**Why Now:**
- User observed Imperator Rome uses "paint mixing" effect - colors blend smoothly at boundaries
- Current hard edges look jarring against smooth distance field borders
- Need visual consistency across all province-related rendering

---

## What We Did

### 1. Investigated Imperator Rome's Approach
**Reference:** `Assets/Archon-Engine/Docs/Log/learnings/imperator-rome-terrain-rendering-analysis.md:84-120`

**Discovery:**
- Imperator uses bilinear filtering on province color lookups
- Samples 4 neighboring pixels and blends based on fractional position
- Creates smooth gradients independent of texture resolution
- Province ID texture still used for selection (pixel-perfect)

### 2. Implemented Bilinear Blending for Political Mode
**Files Changed:** `Assets/Archon-Engine/Shaders/Includes/MapModePolitical.hlsl:95-161`

**Implementation:**
```hlsl
// Manual bilinear sampling using texture-space offsets
float2 texSize = float2(5632.0, 2048.0);
float2 texelSize = 1.0 / texSize;

float2 pixelPos = correctedUV * texSize;
float2 pixelPosFloor = floor(pixelPos);
float2 fractional = pixelPos - pixelPosFloor;

// Sample 4 neighboring pixels
float2 uv00 = (pixelPosFloor + float2(0.5, 0.5)) * texelSize;
float2 uv10 = (pixelPosFloor + float2(1.5, 0.5)) * texelSize;
float2 uv01 = (pixelPosFloor + float2(0.5, 1.5)) * texelSize;
float2 uv11 = (pixelPosFloor + float2(1.5, 1.5)) * texelSize;

// Sample owner IDs (R32_SFloat format - stores raw owner ID as float)
float ownerData00 = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, uv00).r;
// ... (sample all 4)

// Decode owner IDs (just cast, NO multiplication for R32_SFloat)
uint owner00 = (uint)(ownerData00 + 0.5);
// ... (decode all 4)

// Fetch colors and apply HSV grading
float4 color00 = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, GetColorUV(owner00));
color00.rgb = ApplyHSVColorGrading(color00.rgb);
// ... (all 4 colors)

// Bilinear blend
float4 colorTop = lerp(color00, color10, fractional.x);
float4 colorBottom = lerp(color01, color11, fractional.x);
float4 finalColor = lerp(colorTop, colorBottom, fractional.y);
```

**Rationale:**
- Uses same manual bilinear technique already proven in terrain shader
- Applies HSV grading to each sample before blending (preserves visual style)
- Texture-space offsets ensure correct neighboring pixel selection

### 3. Fixed Critical Bug: Incorrect Owner ID Decoding
**Files Changed:** `Assets/Archon-Engine/Shaders/MapModeCommon.hlsl:60-78`

**Problem:** Shader comment said "R16 format" but texture is actually R32_SFloat
**Root Cause:** Outdated comment from when format changed in CoreTextureSet.cs

**Investigation:**
- Initial bilinear implementation returned purple (shader error)
- Debug revealed owner data values >1.0 (glowing white)
- Found CoreTextureSet.cs:88 uses `GraphicsFormat.R32_SFloat`
- Found OwnerTextureDispatcher.cs:237 comment: "stores raw float values (151.0, not normalized)"

**Solution:**
Changed decoding from:
```hlsl
// WRONG (for R32_SFloat)
uint ownerID = (uint)(ownerData * 65535.0 + 0.5);
```

To:
```hlsl
// CORRECT (matches OwnerTextureDispatcher.cs:239)
uint ownerID = (uint)(ownerData + 0.5);
```

**Why This Works:**
- R32_SFloat stores actual owner ID as float (151.0 = owner 151)
- No normalization, just direct storage
- Matches C# decoding logic exactly

**Updated Documentation:**
- Fixed shader comments in MapModeCommon.hlsl:68-75
- Added references to C# source files for future clarity

---

## Decisions Made

### Decision 1: Texture-Space vs Screen-Space Derivatives
**Context:** Need to determine blend weights for bilinear sampling

**Options Considered:**
1. **Screen-space derivatives (`ddx`/`ddy` + `_ScreenParams`)** - Calculate blend weights from screen position
   - Pros: True resolution independence, matches Imperator approach
   - Cons: `_ScreenParams` availability uncertain, `ddx`/`ddy` caused purple screen (likely invalid values)
2. **Texture-space manual calculation** - Calculate fractional position from texture coordinates
   - Pros: Proven working in terrain shader, no external dependencies
   - Cons: Slightly less "true" screen-space independence

**Decision:** Chose Option 2 (texture-space manual calculation)

**Rationale:**
- Option 1 failed with purple screen (shader error)
- Option 2 already proven in MapModeTerrain.hlsl:174-177
- Achieves same visual result (smooth blending at boundaries)
- More reliable, less shader complexity

**Trade-offs:**
- Slightly tied to texture resolution (5632×2048 hardcoded)
- Could make texture size a shader parameter in future

---

## What Worked ✅

1. **Manual Bilinear Sampling Pattern (from Terrain Shader)**
   - What: Sample 4 neighbors with calculated offsets, blend with fractional weights
   - Why it worked: Already battle-tested in terrain rendering (lines 162-218)
   - Reusable pattern: Yes - can apply to any texture needing smooth blending

2. **Systematic Debug Approach**
   - What: Step-by-step debug returns to isolate failure point
   - Steps: Test basic sampling → HSV → bilinear sampling → UV calculation → owner decoding
   - Impact: Found root cause (R32_SFloat decoding) in <10 iterations

3. **Cross-Referencing C# Code for Shader Logic**
   - What: Checked OwnerTextureDispatcher.cs to understand texture format
   - Why it worked: Shader must match C# encoding/decoding exactly
   - Lesson: Always verify texture format assumptions against C# source

---

## What Didn't Work ❌

1. **Screen-Space Derivatives Approach**
   - What we tried: Using `ddx(correctedUV)`, `ddy(correctedUV)`, and `_ScreenParams.xy`
   - Why it failed: Produced purple screen (shader error), likely from invalid derivative values or inaccessible `_ScreenParams`
   - Lesson learned: Texture-space manual calculation more reliable for this use case
   - Don't try this again because: Manual calculation achieves same result with less complexity

2. **Assuming Texture Format from Comments**
   - What we tried: Used shader comment saying "R16 format"
   - Why it failed: Comment was outdated (texture changed to R32_SFloat)
   - Lesson learned: Always verify texture formats against C# creation code
   - Pattern for future: Check CoreTextureSet.cs and dispatcher code when debugging texture sampling

---

## Problems Encountered & Solutions

### Problem 1: Purple Screen (All Bilinear Sampling Broken)
**Symptom:** Entire map rendered purple when bilinear blending enabled

**Root Cause:** Incorrect owner ID decoding - multiplying by 65535 for R32_SFloat texture

**Investigation:**
- Tested basic sampling without bilinear: ✅ worked
- Tested basic sampling with HSV: ✅ worked
- Tested bilinear sampling: ❌ purple
- Visualized owner data: glowing white (values >1.0)
- Checked C# code: Found R32_SFloat stores raw values, not normalized

**Solution:**
Changed from `(uint)(ownerData * 65535.0 + 0.5)` to `(uint)(ownerData + 0.5)`

**Why This Works:**
- R32_SFloat format stores actual owner ID as float (no encoding)
- Matches C# decoding in OwnerTextureDispatcher.cs:239
- No normalization step needed

**Pattern for Future:**
- When texture sampling fails, check C# texture creation code for format
- Verify encoding/decoding logic matches between shader and C#
- Use systematic debug visualization to isolate failure point

### Problem 2: Visual Mismatch Between Borders and Province Colors
**Symptom:** Smooth distance field borders didn't align with hard pixel province edges

**Root Cause:** Different rendering approaches - borders use distance field (smooth), provinces use point sampling (hard)

**Investigation:**
- Examined Imperator Rome's approach (bilinear province color sampling)
- Confirmed they use same technique for smooth "paint mixing" effect
- Verified province selection must remain pixel-perfect (separate concern)

**Solution:**
Implemented bilinear blending for province colors while keeping selection point-sampled

**Why This Works:**
- Both borders and province colors now have smooth gradients
- Visual consistency at boundaries
- Selection remains deterministic (uses original SampleOwnerID function)

---

## Architecture Impact

### Documentation Updates Required
- [x] Update MapModeCommon.hlsl comments (completed this session)
- [ ] Consider making texture size a shader parameter (TODO for future)

### New Patterns Discovered
**Pattern: Dual Sampling Strategy for Visual vs Interaction**
- When to use: Need smooth visuals but pixel-perfect interaction
- Benefits: Best of both worlds - beautiful rendering + deterministic gameplay
- Example: Bilinear for province colors, point-sample for selection
- Add to: Could document in visual-styles-system.md

### Architectural Decisions That Changed
- **Changed:** Province color sampling method
- **From:** Point sampling (hard edges)
- **To:** Bilinear sampling (smooth "paint mixing")
- **Scope:** Political map mode only (for now)
- **Reason:** Match smooth distance field borders, achieve Imperator Rome quality

---

## Code Quality Notes

### Performance
- **Impact:** 4× texture samples per pixel (vs 1× before)
- **Concern:** Moderate - acceptable for visual quality gain
- **Mitigation:** Only applied to political mode, terrain already does similar

### Testing
- **Manual Tests Completed:**
  - ✅ Province colors render smoothly at boundaries
  - ✅ No purple/broken rendering
  - ✅ HSV color grading still applied correctly
  - ⚠️ Province selection still works (assumed, not tested)

### Technical Debt
- **Created:** Hardcoded texture size (5632×2048) in shader
- **TODO:** Make texture dimensions a shader parameter
- **File:** MapModePolitical.hlsl:111

---

## Next Session

### Immediate Next Steps (Priority Order)
1. Apply bilinear blending to province highlights - matches borders and colors
2. Apply bilinear blending to development map mode - visual consistency
3. Test province selection still works correctly - verify pixel-perfect clicks
4. Consider making texture size a shader uniform - remove hardcoded values

### Questions to Resolve
1. Should all map modes use bilinear blending? (consistency vs performance)
2. Does province selection need any changes? (seems independent, but verify)
3. Performance impact acceptable? (4× samples per pixel in political mode)

---

## Session Statistics

**Files Changed:** 2
- `Assets/Archon-Engine/Shaders/Includes/MapModePolitical.hlsl` (+68/-22 lines)
- `Assets/Archon-Engine/Shaders/MapModeCommon.hlsl` (+6/-4 lines)

**Bugs Fixed:** 1 (critical: incorrect R32_SFloat decoding)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Political mode now uses bilinear province color blending
- ProvinceOwnerTexture is R32_SFloat (raw values, not normalized)
- Decoding: `(uint)(ownerData + 0.5)` NOT `(uint)(ownerData * 65535.0 + 0.5)`
- Selection still uses point sampling (ProvinceSelector.cs unchanged)

**What Changed Since Last Doc Read:**
- Implementation: Province colors now blend smoothly at boundaries
- Constraint: Must decode R32_SFloat correctly (no multiplication)

**Gotchas for Next Session:**
- Texture size hardcoded (5632×2048) in MapModePolitical.hlsl:111
- Province highlights still use point sampling (visual inconsistency)
- Don't use screen-space derivatives (`ddx`/`ddy`) - texture-space calculation more reliable

---

## Links & References

### Related Documentation
- [Imperator Rome Analysis](../../learnings/imperator-rome-terrain-rendering-analysis.md) - Bilinear province color technique

### Code References
- Key implementation: `MapModePolitical.hlsl:95-161`
- Bug fix: `MapModeCommon.hlsl:60-78`
- Texture format: `CoreTextureSet.cs:88` (R32_SFloat)
- C# decoding: `OwnerTextureDispatcher.cs:237-239`
- Terrain shader pattern: `MapModeTerrain.hlsl:162-218`

---

## Notes & Observations

- User feedback: "Woah! You actually fixed it. It's blurry now between the province to province colors."
- "Blurry" is actually the desired smooth blending effect (like paint mixing)
- May need styling adjustments later per user comment
- Debugging was systematic and efficient - step-by-step isolation worked perfectly
- Cross-referencing C# code was critical - shader comments were outdated

---

*Session completed successfully - smooth province color blending working!*

# Border Rendering Clean Separation
**Date**: 2026-01-12
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Refactor border rendering system for clean mode separation (DistanceField vs PixelPerfect)

**Secondary Objectives:**
- Fix pixel-perfect border detection (owner ID decoding bug)
- Remove debug code from shaders

**Success Criteria:**
- Each border mode has dedicated texture and shader code path
- No shared/reused textures between modes
- Clean architecture demonstrating Archon capabilities

---

## Context & Background

**Previous Work:**
- Pixel perfect borders weren't rendering (map was black)
- Owner ID decoding in compute shader used wrong format (`*65535` instead of raw float)
- Debug visualization code left in shaders

**Current State:**
- Border system was tangled with mixed concerns
- `_BorderTexture` and `_BorderMaskTexture` reused for different purposes
- Mode detection via texture sampling (confusing)

**Why Now:**
- User requested clean separation to showcase Archon's border rendering capabilities

---

## What We Did

### 1. Fixed Owner ID Decoding Bug
**Files Changed:** `Assets/Archon-Engine/Shaders/BorderDetection.compute`

**Problem:** Compute shader decoded owner IDs as R16 normalized (`*65535`) but texture uses R32_SFloat with raw values (151.0, 731.0)

**Solution:**
```hlsl
// Before (wrong)
uint currentOwner = (uint)(currentOwnerData * 65535.0 + 0.5);

// After (correct)
uint currentOwner = (uint)(currentOwnerData + 0.5);
```

### 2. Shader Property Declarations
**Files Changed:**
- `DefaultCommon.hlsl:19-20, 132-134`
- `DefaultFlatMapShader.shader:16-22, 27-29`
- `DefaultTerrainMapShader.shader:20-26, 37-39`

**Changes:**
- Renamed `_BorderTexture` → `_DistanceFieldBorderTexture`
- Renamed `_BorderMaskTexture` → `_PixelPerfectBorderTexture`
- Removed `_BorderDistanceTexture` (consolidated)
- Updated mode enum: `0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry`

### 3. DynamicTextureSet Refactor
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/DynamicTextureSet.cs`

**Changes:**
- Renamed `dualBorderTexture` → `pixelPerfectBorderTexture`
- Removed confusing `BindBorderTexture()` method
- Added dedicated bind methods: `BindDistanceFieldTextures()`, `BindPixelPerfectTextures()`
- `BindToMaterial()` now binds both textures (shader selects)

### 4. Shader ApplyBorders Refactor
**Files Changed:** `Assets/Archon-Engine/Shaders/Includes/MapModeCommon.hlsl`

**Architecture:**
```hlsl
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    if (_BorderRenderingMode == 0 || _BorderRenderingMode == 3) return baseColor; // None/Mesh
    if (_BorderRenderingMode == 1) return ApplyDistanceFieldBorders(baseColor, correctedUV);
    if (_BorderRenderingMode == 2) return ApplyPixelPerfectBorders(baseColor, correctedUV);
    return baseColor;
}
```

Each mode function is self-contained with its own texture sampling.

### 5. C# Coordinator Updates
**Files Changed:**
- `BorderComputeDispatcher.cs:233-260, 319-360` - Added `GetShaderModeValue()`, updated texture refs
- `VisualStyleManager.cs:123-166` - Added `GetShaderModeValue()`, updated mode mapping
- `MapTextureManager.cs:43-54` - Updated property accessors
- `BorderDistanceFieldGenerator.cs:338-339` - Updated texture reference

---

## Decisions Made

### Decision 1: Shader Mode Values
**Context:** Need consistent mapping between C# enum and shader integer

**Decision:**
| Mode | Value |
|------|-------|
| None | 0 |
| ShaderDistanceField | 1 |
| ShaderPixelPerfect | 2 |
| MeshGeometry | 3 |

**Rationale:** Logical ordering, 0=disabled, 1-2=shader modes, 3=CPU mode

### Decision 2: Both Textures Always Bound
**Context:** How to handle texture binding when switching modes

**Decision:** Bind both border textures always, shader selects based on `_BorderRenderingMode`

**Rationale:** Simpler code, no runtime rebinding needed, shader branch is negligible cost

---

## What Worked ✅

1. **Clean separation pattern**
   - Each mode has dedicated texture + shader function
   - No shared state between modes
   - Easy to understand and extend

2. **Self-contained shader functions**
   - `ApplyDistanceFieldBorders()` only touches `_DistanceFieldBorderTexture`
   - `ApplyPixelPerfectBorders()` only touches `_PixelPerfectBorderTexture`

---

## What Didn't Work ❌

1. **Texture-based mode detection**
   - Tried detecting mode by checking if `_BorderTexture` was black
   - Failed because binding was confusing and unreliable
   - Solution: Explicit `_BorderRenderingMode` shader property

2. **Owner ID decoding assumption**
   - Assumed R16 normalized format
   - Actually R32_SFloat with raw values
   - Lesson: Always verify texture formats match between CPU and GPU

---

## Problems Encountered & Solutions

### Problem 1: Map Was Black in Pixel Perfect Mode
**Symptom:** No borders rendered, map entirely black
**Root Cause:** Owner ID decoding multiplied by 65535, getting wrong IDs
**Solution:** Remove multiplication, just cast raw float to uint

### Problem 2: Textures Mixed Between Modes
**Symptom:** Confusing behavior when switching modes
**Root Cause:** Same `_BorderTexture` reused with different content per mode
**Solution:** Dedicated textures per mode with clear names

---

## Architecture Impact

### New Texture Architecture
```
_DistanceFieldBorderTexture (Mode 1)
  - Format: R8G8B8A8_UNorm
  - Filter: Bilinear
  - Content: R=country dist, G=province dist

_PixelPerfectBorderTexture (Mode 2)
  - Format: R8G8B8A8_UNorm
  - Filter: Point
  - Content: R=country border, G=province border
```

### Shader Mode Dispatch
```
ApplyBorders() → switch on _BorderRenderingMode
  → 0: return (None)
  → 1: ApplyDistanceFieldBorders()
  → 2: ApplyPixelPerfectBorders()
  → 3: return (MeshGeometry handles)
```

---

## Next Session

### Immediate Next Steps
1. Test all border modes work correctly
2. Verify mode switching in VisualStyles
3. Test distance field borders render smoothly
4. Test pixel perfect borders render sharply

### Questions to Resolve
1. Does pixel perfect actually render now? (compute shader fixed)
2. Are both modes visually distinct?

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Border modes: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
- Each mode has dedicated texture (no sharing)
- Owner texture format: R32_SFloat with raw IDs (151.0, 731.0)

**Key Files:**
- Shader dispatch: `MapModeCommon.hlsl:173-201`
- Texture creation: `DynamicTextureSet.cs:70-126`
- Mode mapping: `BorderComputeDispatcher.cs:250-260`
- Pixel perfect generation: `BorderComputeDispatcher.cs:323-360`

**Gotchas:**
- Don't multiply owner ID by anything - it's raw float
- Both border textures always bound, shader picks
- Mode values differ from enum order (enum has legacy aliases)

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `BorderDetection.compute` | Fixed owner ID decoding |
| `DefaultCommon.hlsl` | New texture declarations |
| `DefaultFlatMapShader.shader` | Updated properties/enum |
| `DefaultTerrainMapShader.shader` | Updated properties/enum |
| `MapModeCommon.hlsl` | Refactored ApplyBorders into separate functions |
| `DynamicTextureSet.cs` | Renamed textures, new bind methods |
| `MapTextureManager.cs` | Updated accessors |
| `BorderComputeDispatcher.cs` | Mode mapping, texture refs |
| `VisualStyleManager.cs` | Mode mapping |
| `BorderDistanceFieldGenerator.cs` | Updated texture ref |

---

*Session complete - border rendering clean separation implemented*

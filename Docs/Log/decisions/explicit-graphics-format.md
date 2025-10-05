# Decision: Always Use Explicit GraphicsFormat for RenderTextures

**Date:** 2025-10-05
**Status:** ✅ Implemented
**Decision Maker:** Bug investigation (TYPELESS texture format issue)
**Impacts:** All RenderTextures with enableRandomWrite, GPU compute shader pipelines

---

## Decision Summary

**We always use explicit `GraphicsFormat` via `RenderTextureDescriptor` when creating RenderTextures, especially those with `enableRandomWrite = true`.**

**Core Rules:**
- ✅ **ALWAYS** use `RenderTextureDescriptor` with explicit `GraphicsFormat` enum
- ✅ **ALWAYS** verify format in RenderDoc when debugging GPU issues
- ❌ **NEVER** rely on `RenderTextureFormat` alone when using `enableRandomWrite`
- ❌ **NEVER** assume Unity will pick the correct format automatically

---

## Context & Problem

### The TYPELESS Format Trap

**Symptom:** ~1000 provinces showing gray (ocean color RGB 123,169,231) instead of country colors, despite:
- Valid RGB data in provinces.bmp
- Correct packed values in CPU Color32 array
- ProvinceIDTexture containing correct data (confirmed via ReadPixels)

**RenderDoc Discovery:**
```
ProvinceIDTexture: Format DXGI_FORMAT_R8G8B8A8_TYPELESS
```

**Problem:** TYPELESS format means GPU doesn't know how to interpret byte values:
- Same bytes can be interpreted as uint8, int8, float, or snorm
- Shader reads garbage without proper interpretation
- Platform-dependent behavior (works on some platforms, fails on others)

---

## Investigation Journey

### What We Tried

**Attempt 1: Add `unorm` Shader Qualifier**
```hlsl
// Compute shader
RWTexture2D<unorm float4> ProvinceIDTexture;
```
❌ **Failed** - Shader type qualifiers don't override RenderTexture format

**Attempt 2: Use Different RenderTextureFormat Enum**
```csharp
new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
```
❌ **Failed** - Still became TYPELESS when enableRandomWrite = true

**Attempt 3: Explicit GraphicsFormat**
```csharp
var descriptor = new RenderTextureDescriptor(width, height,
    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
descriptor.enableRandomWrite = true;
var texture = new RenderTexture(descriptor);
```
✅ **Success** - Format stayed R8G8B8A8_UNorm

---

## The Decision

### Chosen Approach: Explicit GraphicsFormat

**Always use RenderTextureDescriptor with explicit GraphicsFormat for UAV-enabled textures:**

```csharp
// WRONG - May become TYPELESS on some platforms
var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
texture.enableRandomWrite = true; // ❌ Triggers TYPELESS on some platforms

// RIGHT - Explicit format guaranteed
var descriptor = new RenderTextureDescriptor(
    width,
    height,
    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, // ✅ Explicit
    0  // No depth buffer
);
descriptor.enableRandomWrite = true;
var texture = new RenderTexture(descriptor);
```

---

## Why This Happens

### Unity's Format Selection Logic

**Without explicit GraphicsFormat:**
1. Unity sees `RenderTextureFormat.ARGB32`
2. Unity sees `enableRandomWrite = true` (UAV requirement)
3. Unity checks platform support for R8G8B8A8_UNorm with UAV
4. **On some platforms:** Creates TYPELESS format as "compatible" alternative
5. **Result:** GPU doesn't know how to interpret bytes

**With explicit GraphicsFormat:**
1. Unity sees `GraphicsFormat.R8G8B8A8_UNorm` (explicit request)
2. Unity creates EXACTLY that format
3. **Result:** GPU interprets bytes as unsigned normalized [0,1]

### Platform Differences

**TYPELESS is platform-dependent:**
- **Some platforms:** R8G8B8A8_UNorm works fine with UAV
- **Other platforms:** Fallback to TYPELESS when UAV requested
- **Result:** Code works in editor, fails in builds (or vice versa)

**Explicit format prevents this:**
- Forces Unity to create requested format
- Errors clearly if platform doesn't support it
- Consistent behavior across platforms

---

## Rationale

### Why Explicit GraphicsFormat is Better

**1. Deterministic Behavior**
- Same format on all platforms
- Critical for multiplayer (GPU state must match)
- No platform-dependent surprises

**2. Clear Error Messages**
- If platform doesn't support format, Unity errors immediately
- Better than silent TYPELESS fallback that causes rendering bugs

**3. Future-Proof**
- Unity is moving toward GraphicsFormat API
- RenderTextureFormat is legacy
- Explicit format is the modern approach

**4. Debugging Clarity**
- RenderDoc shows exactly what you requested
- No guessing about format interpretation
- Matches shader expectations

---

## Implementation Pattern

### Standard RenderTexture Creation

```csharp
/// <summary>
/// Create RenderTexture with guaranteed format
/// </summary>
private RenderTexture CreateProvinceIDTexture(int width, int height)
{
    // Use RenderTextureDescriptor with explicit GraphicsFormat
    var descriptor = new RenderTextureDescriptor(
        width,
        height,
        UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
        0  // No depth buffer
    );

    // Configure UAV support
    descriptor.enableRandomWrite = true;  // Required for compute shader writes
    descriptor.useMipMap = false;         // No mipmaps needed
    descriptor.autoGenerateMips = false;

    // Create texture with descriptor
    var texture = new RenderTexture(descriptor);
    texture.name = "ProvinceID_RenderTexture";
    texture.filterMode = FilterMode.Point;  // No filtering for ID data
    texture.wrapMode = TextureWrapMode.Clamp;
    texture.Create();

    return texture;
}
```

### Format Selection Guide

**For Province/Entity IDs (Integer Data):**
```csharp
GraphicsFormat.R8G8B8A8_UNorm    // 4x 8-bit unsigned [0,255] → [0,1]
GraphicsFormat.R16G16_UNorm      // 2x 16-bit unsigned [0,65535] → [0,1]
```

**For Owner/Country IDs (Single Integer):**
```csharp
GraphicsFormat.R16_UNorm         // 1x 16-bit unsigned
GraphicsFormat.R32_SFloat        // 1x 32-bit float (if needed)
```

**For Color Data:**
```csharp
GraphicsFormat.R8G8B8A8_UNorm    // Standard RGBA
GraphicsFormat.R8G8B8A8_SRGB     // sRGB color space
```

---

## Trade-offs

### What We Gain
- ✅ Consistent format across platforms
- ✅ Clear error messages on unsupported formats
- ✅ Multiplayer-safe (deterministic GPU state)
- ✅ Future-proof (GraphicsFormat is the modern API)

### What We Give Up
- ❌ None - explicit format is strictly better
- ❌ Slightly more verbose code (RenderTextureDescriptor vs constructor)
  - But worth it for reliability

---

## Documentation Impact

**Updated:**
- Map/FILE_REGISTRY.md - Noted R8G8B8A8_UNorm format for MapTextureManager
- learnings/unity-gpu-debugging-guide.md - Added TYPELESS gotcha section

**Pattern Added:**
- Always use RenderTextureDescriptor for UAV-enabled textures
- Verify format in RenderDoc when debugging

---

## Verification

### How to Verify Format is Correct

**Method 1: RenderDoc**
1. Capture frame with F12
2. Find your RenderTexture in Texture Viewer
3. Check format in properties panel
4. Should show: `DXGI_FORMAT_R8G8B8A8_UNORM` (not TYPELESS)

**Method 2: Code Logging**
```csharp
var texture = CreateRenderTexture();
Debug.Log($"Texture format: {texture.graphicsFormat}");
// Expected: R8G8B8A8_UNorm
// NOT: R8G8B8A8_TYPELESS or Unknown
```

---

## Related Decisions

- [fixed-point-determinism.md](fixed-point-determinism.md) - CPU determinism via fixed-point
- This decision: GPU determinism via explicit formats
- Together: Full multiplayer determinism (CPU + GPU)

---

## Gotchas for Future

**Watch Out For:**
1. **enableRandomWrite AFTER descriptor creation**
   ```csharp
   var tex = new RenderTexture(desc);
   tex.enableRandomWrite = true;  // ❌ Too late! Recreates texture with TYPELESS
   ```
   Fix: Set `descriptor.enableRandomWrite = true` BEFORE `new RenderTexture(descriptor)`

2. **Assuming RenderTextureFormat is Enough**
   ```csharp
   new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);  // ❌ May become TYPELESS
   ```
   Fix: Always use explicit GraphicsFormat

3. **Not Verifying in RenderDoc**
   - Always check format in RenderDoc when debugging GPU issues
   - TYPELESS looks fine in code, only visible in GPU debugger

---

## Quick Reference

**When to Use Explicit GraphicsFormat:**
- ✅ Any RenderTexture with `enableRandomWrite = true`
- ✅ Any RenderTexture used as compute shader UAV
- ✅ Any RenderTexture where format matters for rendering
- ✅ All the time (it's never wrong to be explicit)

**How to Use:**
```csharp
var descriptor = new RenderTextureDescriptor(
    width, height,
    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
    0
);
descriptor.enableRandomWrite = true;
var texture = new RenderTexture(descriptor);
```

---

*Decision made 2025-10-05 after debugging ~1000 broken provinces caused by TYPELESS format*

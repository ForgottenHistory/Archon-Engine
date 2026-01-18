# Troubleshooting: GPU & Rendering Issues

This guide documents GPU, shader, and rendering problems when building games with Archon Engine.

## Compute Shader Race Conditions

### Symptom
- Second compute shader reads wrong data
- C# `ReadPixels()` reads correct data, but compute shader reads different values
- Seemingly random, intermittent failures

### Root Cause
`Dispatch()` is asynchronous - GPU queues work and CPU continues immediately:

```csharp
// ❌ WRONG - Race condition
firstShader.Dispatch(kernel1, ...);   // GPU starts work
secondShader.Dispatch(kernel2, ...);  // GPU may read incomplete data!
```

### Solution
Use `AsyncGPUReadback.WaitForCompletion()` between dependent dispatches:

```csharp
// ✅ CORRECT - Explicit synchronization
firstShader.Dispatch(kernel1, ...);

// Force GPU to complete before continuing
var request = AsyncGPUReadback.Request(outputTexture);
request.WaitForCompletion();

secondShader.Dispatch(kernel2, ...);
```

### Key Lesson
Never assume `Dispatch()` blocks. Always synchronize between dependent compute operations.

---

## Texture Binding State Mismatch (UAV vs SRV)

### Symptom
- Even with GPU sync, second shader still reads wrong data
- First shader writes correctly (verified via ReadPixels)
- Second shader reads garbage

### Root Cause
First shader binds texture as `RWTexture2D` (UAV), second as `Texture2D` (SRV). Unity doesn't always transition resource state correctly:

```hlsl
// First shader (writing)
RWTexture2D<float4> OutputTexture;  // Unordered Access View

// Second shader (reading) - WRONG!
Texture2D<float4> InputTexture;     // Shader Resource View
```

### Solution
Use `RWTexture2D` for ALL compute shader texture access, even read-only:

```hlsl
// ✅ CORRECT - Uniform binding
// First shader
RWTexture2D<float4> OutputTexture;

// Second shader - also use RWTexture2D
RWTexture2D<float4> InputTexture;  // Still works for reading

[numthreads(8,8,1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    float4 value = InputTexture[id.xy];  // Direct indexing, not Load()
}
```

### Key Lesson
Always bind RenderTextures uniformly as `RWTexture2D`. Use direct indexing `texture[id.xy]` instead of `Load()`.

---

## Graphics.Blit Data Corruption

### Symptom
- Data completely wrong after `Graphics.Blit()`
- Y-axis appears flipped or coordinates scrambled
- Results vary by platform

### Root Cause
`Graphics.Blit()` has undocumented coordinate transformation behavior that varies by platform.

### Solution
Use compute shader for data transfer - gives full control:

```csharp
// ❌ AVOID - Unpredictable transformations
Graphics.Blit(source, dest);

// ✅ USE - Deterministic coordinates
ComputeBuffer buffer = new ComputeBuffer(width * height, sizeof(float));
buffer.SetData(cpuData);

computeShader.SetBuffer(kernel, "_InputBuffer", buffer);
computeShader.SetTexture(kernel, "_OutputTexture", destTexture);
computeShader.Dispatch(kernel, width/8, height/8, 1);
```

### Key Lesson
AVOID `Graphics.Blit` for data transfer. Use compute shaders for deterministic coordinate systems.

---

## Fragment Shader Coordinate Confusion

### Symptom
- Map renders upside down
- Coordinates work in compute shader but flip in fragment shader
- Removing Y-flips makes it worse

### Root Cause
Coordinate system mismatch:
- Fragment shader UVs: (0,0) = bottom-left (OpenGL convention)
- RenderTexture data: (0,0) = top-left (DirectX convention)

### Solution
Y-flip ONLY in fragment shader UV sampling:

```hlsl
// In FRAGMENT shader only
float2 correctedUV = float2(uv.x, 1.0 - uv.y);
float4 data = _ProvinceTexture.Sample(sampler, correctedUV);
```

```hlsl
// In COMPUTE shader - NEVER flip
[numthreads(8,8,1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    // Use raw id.xy coordinates directly
    float4 data = InputTexture[id.xy];  // NO flipping
    OutputTexture[id.xy] = result;       // NO flipping
}
```

### Key Lesson
Clear separation: raw GPU coordinates in compute shaders, Y-flip only in fragment UV sampling.

---

## RenderTexture Format for Province IDs

### Symptom
- Province IDs corrupted or clamped
- Color bleeding between provinces
- Texture filtering artifacts

### Root Cause
Wrong texture format or filtering:

```csharp
// ❌ WRONG - Filtering interpolates IDs!
new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
texture.filterMode = FilterMode.Bilinear;

// ❌ WRONG - 8-bit clamps large IDs
RenderTextureFormat.R8 // Max value 255
```

### Solution
Use appropriate format and point filtering for ID textures:

```csharp
// ✅ CORRECT - High precision, no interpolation
var texture = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);
texture.filterMode = FilterMode.Point;  // CRITICAL for IDs
texture.enableRandomWrite = true;       // For compute shaders
```

### Key Lesson
Province ID textures need: high precision format (RFloat or RGFloat), Point filtering (never Bilinear), and enableRandomWrite for compute access.

---

## GPU Debugging: Test Coordinates Pattern

### Symptom
- Data appears correct in CPU, wrong in GPU
- Need to identify exactly where pipeline diverges

### Solution
Track known test values through entire pipeline:

```csharp
// Pick a known province and coordinates
ushort testProvinceId = 2751;
int testX = 2767, testY = 711;

// Check at each stage
Debug.Log($"CPU Array: {cpuData[testY * width + testX]}");
Debug.Log($"GPU Buffer: {ReadBackBuffer(testX, testY)}");
Debug.Log($"ReadPixels: {ReadPixels(texture, testX, testY)}");
Debug.Log($"Compute Output: {/* verify in shader */}");
```

### Result
Pinpoints exact divergence point:
- CPU ✅ → GPU Buffer ✅ → ReadPixels ✅ → Compute ❌ = Race condition

---

## ComputeBuffer Resource Leak

### Symptom
- Unity warning: "A ComputeBuffer has not been disposed"
- Growing memory usage over time
- Warnings appear on scene unload or play mode exit

### Root Cause
ComputeBuffer created as local variable, reference lost before disposal:

```csharp
// ❌ WRONG - Local variable, never disposed
void SetupShader()
{
    var buffer = new ComputeBuffer(size, stride);
    shader.SetBuffer(kernel, "_Buffer", buffer);
    // buffer goes out of scope, but GPU still holds reference
}
```

### Solution
Store as field and dispose in OnDestroy:

```csharp
// ✅ CORRECT - Field with proper disposal
private ComputeBuffer dataBuffer;

void SetupShader()
{
    dataBuffer = new ComputeBuffer(size, stride);
    shader.SetBuffer(kernel, "_Buffer", dataBuffer);
}

void OnDestroy()
{
    dataBuffer?.Release();
}
```

### Key Lesson
ComputeBuffers are unmanaged GPU resources. Store references as fields and explicitly Release() in OnDestroy.

---

## GPU Debugging Checklist

1. **Verify sync** - Is `AsyncGPUReadback.WaitForCompletion()` between dependent dispatches?
2. **Check binding** - Using `RWTexture2D` uniformly for all compute access?
3. **Verify coordinates** - Y-flip only in fragment shader?
4. **Check texture format** - Point filtering for ID textures?
5. **Test known values** - Track specific coordinates through pipeline
6. **Avoid Blit** - Using compute shaders for data transfer?

## Critical Rules

| DO | DON'T |
|----|-------|
| `AsyncGPUReadback.WaitForCompletion()` between dispatches | Assume `Dispatch()` blocks |
| `RWTexture2D` uniformly | Mix `RWTexture2D` and `Texture2D` |
| Y-flip in fragment UV only | Y-flip in compute shader |
| Compute shader for data transfer | `Graphics.Blit` for data |
| Point filtering for ID textures | Bilinear filtering for IDs |
| Track test coordinates through pipeline | Debug blindly |

## API Reference

- [MapTextureManager](~/api/Map.Core.MapTextureManager.html) - Texture management

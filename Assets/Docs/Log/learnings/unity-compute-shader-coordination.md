# Unity Compute Shader Coordination & RenderTexture Gotchas

**Created:** 2025-10-02
**Last Updated:** 2025-10-02
**Applies To:** All GPU compute shader work in Dominion
**Debugging Time Saved:** 8+ hours (learned the hard way)

---

## What This Document Covers

This learning document captures critical patterns and pitfalls when using **Unity compute shaders with RenderTextures**, specifically for dependent GPU operations where one compute shader writes data that another immediately reads.

**You MUST read this before:**
- Writing new compute shaders that read from RenderTextures
- Debugging coordinate system issues in GPU pipelines
- Investigating "why does my compute shader read wrong data?"

**Core Issues Covered:**
1. 🔴 **GPU Race Conditions** - Async dispatch causing dependent shaders to read stale data
2. 🔴 **Texture Binding Mismatches** - UAV/SRV state transition failures
3. 🔴 **Coordinate System Confusion** - When to Y-flip (and when NOT to)
4. ✅ **Diagnostic Patterns** - How to debug GPU pipeline issues efficiently

---

## The Problem We Solved

**Symptom:** Political map mode showed fragmented colors - each province different color instead of unified country colors.

**What Made It Hard:**
- C# `ReadPixels()` read correct data from RenderTexture (province 2751)
- Compute shader `Load()` read **different** data from **same RenderTexture, same coordinates** (province 388)
- Same texture instance, same GPU memory, different results
- No error messages, no crashes, just silently wrong data

**Time Spent:** 8 hours across 2 sessions debugging GPU pipeline

**Root Causes (2 separate bugs):**
1. GPU race condition - second compute shader dispatched before first completed writes
2. Texture binding mismatch - UAV vs SRV state transition not handled by Unity

---

## Issue 1: GPU Race Conditions from Async Dispatch

### The Pitfall

**Unity compute shader `Dispatch()` is ASYNCHRONOUS.** The CPU queues GPU work and **immediately continues** - it does NOT wait for the GPU to finish executing.

```csharp
// ❌ WRONG - GPU race condition
populateProvinceIDCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
// CPU continues immediately, GPU may still be executing...

ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
// ^ This dispatches a second compute shader that READS from the texture
// GPU may start reading BEFORE the first shader finished writing!
```

**What Happens:**
1. `PopulateProvinceIDTexture` dispatched → GPU work queued
2. CPU immediately continues to next line
3. `PopulateOwnerTexture` dispatched → GPU work queued
4. **GPU executes both in parallel or out-of-order**
5. Second shader reads uninitialized/stale texture data

**Why It's Confusing:**
- `ReadPixels()` **implicitly synchronizes** - forces GPU completion before read
- So C# reads show correct data, but GPU-to-GPU reads fail
- No error message - just silently wrong data

### The Solution: Explicit GPU Synchronization

**Use `AsyncGPUReadback.WaitForCompletion()` to force CPU to wait for GPU:**

```csharp
// ✅ CORRECT - Explicit GPU synchronization
populateProvinceIDCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

// CRITICAL: Force GPU to complete all writes before subsequent shaders read
var asyncRead = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);
asyncRead.WaitForCompletion();
// CPU blocks here until GPU finishes ALL writes to ProvinceIDTexture

ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
// ^ Now guaranteed to read fully populated texture
```

**Why This Works:**
- `AsyncGPUReadback.Request()` queues a GPU→CPU readback
- `WaitForCompletion()` **blocks CPU** until GPU finishes the readback
- GPU can't complete readback until all prior writes to texture are done
- Subsequent dispatches see fully updated texture

**Performance Impact:**
- CPU stalls waiting for GPU (~10-30ms for full texture sync)
- **Acceptable for initialization code** (runs once at startup)
- **NOT acceptable for hot path** (every frame) - use async patterns instead

### When You Need GPU Sync

**You MUST synchronize when:**
- ✅ Compute shader A writes to RenderTexture
- ✅ Compute shader B reads from **same RenderTexture**
- ✅ B must execute **after** A completes
- ✅ No other CPU/GPU operations between them that force sync

**You DON'T need sync when:**
- ❌ Both shaders read from same texture (no writes)
- ❌ Shaders write to **different** textures
- ❌ Fragment shader reads (fragment shaders execute after all compute)
- ❌ Multiple frames apart (frame boundary forces sync)

**Example: BorderDetection Shader**
```csharp
// BorderDetection reads ProvinceIDTexture (written by PopulateProvinceIDTexture)
// Need sync:
populateProvinceIDCompute.Dispatch(...);
var sync = AsyncGPUReadback.Request(provinceIDTexture);
sync.WaitForCompletion();
borderDetectionCompute.Dispatch(...); // Now safe to read ProvinceIDTexture
```

### Alternative: Async Pattern (Not for Initialization)

For hot path (per-frame updates), use async pattern instead of blocking:

```csharp
// For per-frame updates (NOT initialization)
IEnumerator UpdateOwnerTextureAsync()
{
    populateProvinceIDCompute.Dispatch(...);

    // Request async readback (doesn't block)
    var request = AsyncGPUReadback.Request(provinceIDTexture);

    // Wait for request completion (yields, doesn't block CPU)
    yield return new WaitUntil(() => request.done);

    // Now safe to dispatch dependent shader
    ownerTextureDispatcher.PopulateOwnerTexture(...);
}
```

**Trade-off:** Adds frame latency, but doesn't stall CPU.

---

## Issue 2: Texture Binding - UAV vs SRV State Transitions

### The Pitfall

Unity RenderTextures can be bound to compute shaders in **two different ways:**

1. **UAV (Unordered Access View)** - Read-write access via `RWTexture2D`
2. **SRV (Shader Resource View)** - Read-only access via `Texture2D`

**Unity does NOT automatically transition textures between UAV and SRV states.**

```hlsl
// Shader A writes to texture
#pragma kernel PopulateIDs
RWTexture2D<float4> ProvinceIDTexture; // Binds as UAV (read-write)

[numthreads(8,8,1)]
void PopulateIDs(uint3 id : SV_DispatchThreadID)
{
    ProvinceIDTexture[id.xy] = someData; // Write
}
```

```hlsl
// Shader B reads from texture
#pragma kernel PopulateOwners
Texture2D<float4> ProvinceIDTexture; // ❌ Binds as SRV (read-only)

[numthreads(8,8,1)]
void PopulateOwners(uint3 id : SV_DispatchThreadID)
{
    float4 data = ProvinceIDTexture.Load(int3(id.xy, 0)); // Reads stale/wrong data!
}
```

**What Happens:**
1. Shader A binds texture as UAV, writes data
2. Texture GPU state = "UAV mode"
3. Shader B tries to bind as SRV
4. **Unity fails to transition UAV→SRV properly**
5. Shader B reads from wrong GPU memory or stale cache

**Symptoms:**
- Even with GPU sync, compute shader reads wrong data
- ReadPixels() works fine (forces state transition)
- No error messages, just incorrect results
- Data appears to come from "random" memory locations

### The Solution: Uniform RWTexture2D Binding

**Use `RWTexture2D` for ALL compute shader texture access, even read-only:**

```hlsl
// ✅ CORRECT - Uniform UAV binding
#pragma kernel PopulateOwners
RWTexture2D<float4> ProvinceIDTexture; // Use RWTexture2D even for read-only access

[numthreads(8,8,1)]
void PopulateOwners(uint3 id : SV_DispatchThreadID)
{
    float4 data = ProvinceIDTexture[id.xy]; // Direct indexing, no Load()
    // Texture stays in UAV state throughout - no state transition needed
}
```

**Why This Works:**
- Both shaders bind texture as UAV (read-write)
- No UAV↔SRV state transitions needed
- Uniform GPU state throughout entire pipeline
- Unity handles UAV-to-UAV binding reliably

**Additional Benefits:**
- Direct indexing `texture[id.xy]` more readable than `Load(int3(id.xy, 0))`
- Consistent pattern across all compute shaders
- Eliminates entire class of state transition bugs

**Trade-offs:**
- Loses compile-time read-only enforcement (but not critical - compute shaders are already low-level)
- Minimal - Unity documentation actually recommends UAV binding for RenderTextures

### RWTexture2D vs Texture2D.Load() Comparison

```hlsl
// ❌ OLD PATTERN - Texture2D with Load()
Texture2D<float4> MyTexture;
float4 color = MyTexture.Load(int3(x, y, mipLevel)); // Verbose, requires mip level

// ✅ NEW PATTERN - RWTexture2D with direct indexing
RWTexture2D<float4> MyTexture;
float4 color = MyTexture[uint2(x, y)]; // Cleaner, no mip level needed
```

**Rule:** Always use `RWTexture2D` for compute shader RenderTexture access.

---

## Issue 3: Coordinate Systems - When to Y-Flip (and When NOT To)

### The Confusion

Unity has **three different coordinate systems** for textures:

1. **GPU Memory (RenderTexture storage):** (0,0) = top-left, Y-down (DirectX convention)
2. **Fragment Shader UVs:** (0,0) = bottom-left, Y-up (OpenGL convention)
3. **Compute Shader Threads:** Raw GPU memory coordinates, (0,0) = top-left

**The Trap:** Trying to Y-flip in compute shaders to "match" fragment shader UVs.

### The Rule: Y-Flip ONLY in Fragment Shaders

```hlsl
// ✅ CORRECT - Compute shader uses RAW GPU coordinates
[numthreads(8,8,1)]
void PopulateTexture(uint3 id : SV_DispatchThreadID)
{
    // id.xy = raw GPU coordinates (0,0)=top-left
    // NO Y-FLIP - write directly to GPU memory layout
    OutputTexture[id.xy] = someData;
}
```

```hlsl
// ✅ CORRECT - Fragment shader Y-flips UVs when sampling RenderTexture
float4 SampleOwnerID(float2 uv)
{
    // uv from vertex shader: (0,0)=bottom-left (OpenGL convention)
    // RenderTexture storage: (0,0)=top-left (DirectX convention)
    // Need Y-flip to convert UV→texture coords
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    float4 data = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, correctedUV);
    return data;
}
```

```hlsl
// ❌ WRONG - Y-flipping in compute shader
[numthreads(8,8,1)]
void PopulateTexture(uint3 id : SV_DispatchThreadID)
{
    // DON'T DO THIS - creates coordinate mismatch between compute shaders
    uint flippedY = MapHeight - 1 - id.y;
    OutputTexture[uint2(id.x, flippedY)] = someData; // ❌ WRONG
}
```

### Why Y-Flipping in Compute Shaders is Wrong

**When you Y-flip in Compute Shader A:**
1. Thread (0, 0) writes to GPU memory at (0, MapHeight-1)
2. Data stored "upside down" in GPU memory

**When Compute Shader B reads:**
1. Thread (0, 0) reads from GPU memory at (0, 0)
2. Reads wrong data (thread wrote to different location)

**Coordinate mismatch = broken pipeline.**

### Coordinate System Rules

| Context | Coordinate System | Y-Flip Needed? |
|---------|------------------|----------------|
| **Compute Shader Thread ID** | Raw GPU coords, (0,0)=top-left | ❌ NO - use `id.xy` directly |
| **Compute Shader → Compute Shader** | Both use raw GPU coords | ❌ NO - same memory layout |
| **Fragment Shader UVs** | OpenGL UVs, (0,0)=bottom-left | ✅ YES - Y-flip before sampling RenderTexture |
| **CPU ReadPixels()** | Raw GPU coords, (0,0)=top-left | ❌ NO - reads raw GPU memory |
| **Graphics.Blit()** | May apply Y-flip (unreliable) | ⚠️ AVOID - use compute shader instead |

### Visual Diagram

```
Compute Shader Thread Space:         Fragment Shader UV Space:
(0,0) -------- X ------>             (0,1) -------- X ------>
|                                     ^
|                                     |
Y (GPU memory layout)                 Y (OpenGL convention)
|                                     |
v                                    (0,0)

RenderTexture GPU Storage:
(0,0) -------- X ------>
|
Y (DirectX convention)
|
v
```

**Mapping:**
- Compute Thread (x, y) → GPU Memory (x, y) → Fragment UV (x, 1-y)
- Compute shaders: no flip
- Fragment shader: flip Y in UV

---

## Diagnostic Patterns: How to Debug GPU Pipeline Issues

### Pattern 1: Test Coordinate Tracking

**Use known test coordinates** to track data through entire pipeline.

```csharp
// Example: Province 2751 should be at pixel (2767, 711)
const int TEST_X = 2767;
const int TEST_Y = 711;
const ushort EXPECTED_PROVINCE_ID = 2751;

// 1. Verify CPU array
int cpuIndex = TEST_Y * width + TEST_X;
ushort cpuProvinceID = DecodeProvinceID(pixelArray[cpuIndex]);
Debug.Log($"CPU array[{cpuIndex}] = province {cpuProvinceID} (expect {EXPECTED_PROVINCE_ID})");

// 2. Verify GPU buffer before upload
uint packedValue = gpuBuffer[cpuIndex];
ushort bufferProvinceID = DecodeFromPacked(packedValue);
Debug.Log($"GPU buffer[{cpuIndex}] = province {bufferProvinceID} (expect {EXPECTED_PROVINCE_ID})");

// 3. Verify RenderTexture after compute shader
RenderTexture.active = renderTexture;
Texture2D readback = new Texture2D(1, 1, TextureFormat.ARGB32, false);
readback.ReadPixels(new Rect(TEST_X, TEST_Y, 1, 1), 0, 0);
readback.Apply();
RenderTexture.active = null;

ushort textureProvinceID = DecodeProvinceID(readback.GetPixel(0, 0));
Debug.Log($"RenderTexture({TEST_X},{TEST_Y}) = province {textureProvinceID} (expect {EXPECTED_PROVINCE_ID})");
Object.Destroy(readback);

// 4. Verify second compute shader reads correct value
// (Add similar readback in second shader's dispatcher)
```

**What This Reveals:**
- If CPU→GPU buffer fails: packing/encoding issue
- If GPU buffer→RenderTexture fails: compute shader write bug or coordinate mismatch
- If RenderTexture→Second shader fails: **GPU race condition or texture binding issue**

### Pattern 2: GPU Timeline Analysis

**Identify GPU race conditions** by checking if data diverges between ReadPixels and compute shader reads:

```csharp
// After first compute dispatch
var readback1 = ReadPixelAtTestCoordinate(renderTexture, TEST_X, TEST_Y);
Debug.Log($"ReadPixels after Dispatch A: {readback1}"); // Forces GPU sync

// Dispatch second shader
secondCompute.Dispatch(...);

// Check what second shader wrote (if in debug mode)
var readback2 = ReadPixelAtTestCoordinate(outputTexture, TEST_X, TEST_Y);
Debug.Log($"ReadPixels after Dispatch B: {readback2}");
```

**Diagnostic Logic:**
- ReadPixels **after Dispatch A** correct, **Second shader output** wrong → **GPU race condition**
- Both wrong → Coordinate system or encoding issue
- Both correct → Fragment shader sampling issue

### Pattern 3: Compute Shader Debug Output

**Write debug values** to verify compute shader execution:

```hlsl
// Debug mode: Write thread ID or known pattern
if (DebugMode > 0)
{
    // Write thread coordinates as color (visualize execution pattern)
    OutputTexture[id.xy] = float4(id.x / 1000.0, id.y / 1000.0, 0, 1);
}
```

**Or write input data directly:**

```hlsl
// Debug: Write province ID instead of owner ID to verify texture reading
if (DebugWriteProvinceIDs > 0)
{
    uint provinceID = DecodeProvinceID(InputTexture[id.xy]);
    OutputTexture[id.xy] = float(provinceID) / 65535.0; // Verify input is correct
}
else
{
    // Normal mode: Process and write
    uint ownerID = ProcessData(InputTexture[id.xy]);
    OutputTexture[id.xy] = float(ownerID) / 65535.0;
}
```

**Use C# to toggle debug mode:**

```csharp
[SerializeField] private bool debugWriteInput = true;
computeShader.SetInt("DebugWriteProvinceIDs", debugWriteInput ? 1 : 0);
```

---

## Common Mistakes - Anti-Patterns to AVOID

### ❌ Anti-Pattern 1: No GPU Synchronization Between Dependent Shaders

```csharp
// ❌ WRONG - GPU race condition
shaderA.Dispatch(...); // Writes to textureX
shaderB.Dispatch(...); // Reads from textureX - MAY READ STALE DATA
```

**Fix:**
```csharp
// ✅ CORRECT
shaderA.Dispatch(...);
var sync = AsyncGPUReadback.Request(textureX);
sync.WaitForCompletion(); // Wait for GPU
shaderB.Dispatch(...); // Now safe
```

### ❌ Anti-Pattern 2: Mixed Texture2D and RWTexture2D Bindings

```hlsl
// Shader A
RWTexture2D<float4> SharedTexture; // UAV

// Shader B
Texture2D<float4> SharedTexture; // ❌ SRV - state transition may fail
```

**Fix:**
```hlsl
// Both shaders
RWTexture2D<float4> SharedTexture; // ✅ Uniform UAV binding
```

### ❌ Anti-Pattern 3: Y-Flipping in Compute Shaders

```hlsl
// ❌ WRONG
uint flippedY = MapHeight - 1 - id.y;
OutputTexture[uint2(id.x, flippedY)] = data;
```

**Fix:**
```hlsl
// ✅ CORRECT - No Y-flip in compute
OutputTexture[id.xy] = data;

// Y-flip ONLY in fragment shader UVs:
float2 correctedUV = float2(uv.x, 1.0 - uv.y);
```

### ❌ Anti-Pattern 4: Using Graphics.Blit Instead of Compute Shader

```csharp
// ❌ AVOID - Graphics.Blit has coordinate transformation issues
Texture2D temp = new Texture2D(width, height, ...);
temp.SetPixels32(cpuData);
temp.Apply();
Graphics.Blit(temp, renderTexture); // May apply Y-flip unpredictably
```

**Fix:**
```csharp
// ✅ CORRECT - Use compute shader for full control
ComputeBuffer buffer = new ComputeBuffer(cpuData.Length, sizeof(uint));
buffer.SetData(cpuData);
computeShader.SetBuffer(kernel, "InputData", buffer);
computeShader.SetTexture(kernel, "OutputTexture", renderTexture);
computeShader.Dispatch(kernel, ...);
buffer.Release();
```

### ❌ Anti-Pattern 5: Assuming Dispatch() Blocks

```csharp
// ❌ WRONG ASSUMPTION
computeShader.Dispatch(...);
// GPU is done now, right? NO - Dispatch is async!
var result = ReadResultFromGPU(); // May read incomplete data
```

**Fix:**
```csharp
// ✅ CORRECT
computeShader.Dispatch(...);
var sync = AsyncGPUReadback.Request(outputTexture);
sync.WaitForCompletion(); // Explicitly wait
var result = ReadResultFromGPU(); // Now safe
```

---

## Quick Reference: Patterns to Use

### ✅ Pattern: Dependent Compute Shader Dispatch

```csharp
// Shader A writes, Shader B reads from same texture
public void PopulateDependentTextures(RenderTexture sharedTexture)
{
    // Dispatch first shader (writes to sharedTexture)
    writeShader.SetTexture(writeKernel, "OutputTexture", sharedTexture);
    writeShader.Dispatch(writeKernel, threadGroupsX, threadGroupsY, 1);

    // CRITICAL: Synchronize GPU before dependent shader reads
    var syncRequest = AsyncGPUReadback.Request(sharedTexture);
    syncRequest.WaitForCompletion();

    // Dispatch second shader (reads from sharedTexture)
    readShader.SetTexture(readKernel, "InputTexture", sharedTexture);
    readShader.Dispatch(readKernel, threadGroupsX, threadGroupsY, 1);
}
```

### ✅ Pattern: Compute Shader Texture Binding

```hlsl
#pragma kernel MyKernel

// ALWAYS use RWTexture2D for RenderTexture access (even read-only)
RWTexture2D<float4> InputTexture;   // Read from this
RWTexture2D<float4> OutputTexture;  // Write to this

// Structured buffer for CPU data
StructuredBuffer<uint> InputBuffer;

[numthreads(8, 8, 1)]
void MyKernel(uint3 id : SV_DispatchThreadID)
{
    // Bounds check
    if (id.x >= Width || id.y >= Height)
        return;

    // Read from input texture - direct indexing, NO Y-flip
    float4 inputData = InputTexture[id.xy];

    // Process data
    float4 result = ProcessData(inputData);

    // Write to output texture - direct indexing, NO Y-flip
    OutputTexture[id.xy] = result;
}
```

### ✅ Pattern: Fragment Shader RenderTexture Sampling

```hlsl
// Fragment shader - Y-flip UVs when sampling RenderTextures
float4 SampleRenderTexture(float2 uv)
{
    // Fragment UVs: (0,0)=bottom-left
    // RenderTexture: (0,0)=top-left
    // Apply Y-flip to convert
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    float4 color = SAMPLE_TEXTURE2D(_MyRenderTexture, sampler_MyRenderTexture, correctedUV);
    return color;
}
```

### ✅ Pattern: Diagnostic Test Coordinates

```csharp
// Define test coordinates for pipeline verification
private const int TEST_X = 2767;
private const int TEST_Y = 711;
private const ushort EXPECTED_VALUE = 2751;

private void VerifyGPUPipeline()
{
    // Track data through: CPU → GPU buffer → RenderTexture → Compute shader

    // 1. CPU array
    int index = TEST_Y * width + TEST_X;
    ushort cpuValue = cpuArray[index];
    Debug.Log($"CPU[{index}] = {cpuValue} (expect {EXPECTED_VALUE})");

    // 2. GPU buffer
    ushort bufferValue = gpuBuffer[index];
    Debug.Log($"GPU buffer[{index}] = {bufferValue} (expect {EXPECTED_VALUE})");

    // 3. RenderTexture (after first compute)
    ushort textureValue = ReadPixelValue(renderTexture, TEST_X, TEST_Y);
    Debug.Log($"RenderTexture({TEST_X},{TEST_Y}) = {textureValue} (expect {EXPECTED_VALUE})");

    // 4. Second compute output (if applicable)
    ushort outputValue = ReadPixelValue(outputTexture, TEST_X, TEST_Y);
    Debug.Log($"Output({TEST_X},{TEST_Y}) = {outputValue} (expect processed value)");

    // Verify: All values should match expected (or expected processed value)
}

private ushort ReadPixelValue(RenderTexture tex, int x, int y)
{
    RenderTexture.active = tex;
    Texture2D temp = new Texture2D(1, 1, TextureFormat.ARGB32, false);
    temp.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
    temp.Apply();
    RenderTexture.active = null;

    ushort value = DecodeValue(temp.GetPixel(0, 0));
    Destroy(temp);
    return value;
}
```

---

## Performance Considerations

### GPU Sync Overhead

**AsyncGPUReadback.WaitForCompletion() cost:**
- Small texture (512x512): ~5ms
- Medium texture (2048x2048): ~15ms
- Large texture (5632x2048, Dominion map): ~30ms

**When it's acceptable:**
- ✅ Initialization code (runs once)
- ✅ Level loading
- ✅ Infrequent updates (every few seconds)

**When it's NOT acceptable:**
- ❌ Per-frame updates (60 FPS = 16ms budget)
- ❌ Hot path rendering
- ❌ Frequent updates (multiple times per second)

**Alternative for hot path:** Use async pattern with coroutines (adds frame latency but doesn't block)

### Thread Group Size

**Optimal thread group size: 8x8 (64 threads)**
- Matches GPU warp size on most hardware
- Balances occupancy vs register pressure
- Consistent with Unity's compute shader examples

```hlsl
[numthreads(8, 8, 1)] // ✅ GOOD - 64 threads per group
void MyKernel(uint3 id : SV_DispatchThreadID) { }
```

```csharp
// Calculate thread groups (round up division)
const int THREAD_GROUP_SIZE = 8;
int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
```

---

## Related Sessions & Code References

### Sessions Where This Was Learned
- [2025-10-01-2-political-mapmode-gpu-migration.md](../2025-10-01/2025-10-01-2-political-mapmode-gpu-migration.md) - Initial GPU race condition discovery
- [2025-10-02-1-gpu-compute-shader-coordination-fix.md](../2025-10-02/2025-10-02-1-gpu-compute-shader-coordination-fix.md) - Complete solution

### Code Examples in Project
- **GPU Sync Pattern:** `MapTexturePopulator.cs:315-316`
- **RWTexture2D Binding:** `PopulateOwnerTexture.compute:13, 54`
- **Fragment Y-Flip:** `MapModeCommon.hlsl:66, 82, 91, 101, 131`
- **Diagnostic Logging:** `MapTexturePopulator.cs:287-318`

### Related Shaders
- `PopulateProvinceIDTexture.compute` - Writes province IDs to RenderTexture
- `PopulateOwnerTexture.compute` - Reads province IDs, writes owner IDs
- `BorderDetection.compute` - Reads province IDs (needs same sync pattern)

---

## Checklist: Before Writing New Compute Shader

- [ ] Will this shader READ from a RenderTexture that another compute shader WRITES to?
  - **YES** → Add AsyncGPUReadback.WaitForCompletion() between dispatches
  - **NO** → No sync needed

- [ ] Am I binding RenderTextures?
  - **YES** → Use `RWTexture2D`, NOT `Texture2D`
  - **NO** → N/A

- [ ] Am I doing coordinate transformations in the compute shader?
  - **YES** → STOP - Use raw `id.xy` coordinates, no Y-flip
  - **NO** → Good

- [ ] Am I sampling this RenderTexture in a fragment shader?
  - **YES** → Add Y-flip: `float2 uv = float2(input.x, 1.0 - input.y);`
  - **NO** → No Y-flip

- [ ] Do I have test coordinates to verify the pipeline?
  - **YES** → Good
  - **NO** → Add logging for known test coordinates

- [ ] Is this hot path (runs every frame)?
  - **YES** → Use async pattern, NOT WaitForCompletion()
  - **NO** → WaitForCompletion() is fine

---

## Summary: The Golden Rules

1. **GPU Dispatch is Async** - Always sync with `AsyncGPUReadback.WaitForCompletion()` for dependent shaders
2. **Always RWTexture2D** - Use for all compute shader RenderTexture access, even read-only
3. **No Y-Flip in Compute** - Use raw `id.xy` coordinates, Y-flip ONLY in fragment shader UVs
4. **Test Coordinates** - Track data through entire pipeline with known test values
5. **Avoid Graphics.Blit** - Use compute shaders for full coordinate system control

**Follow these rules = No 8-hour debugging sessions.**

---

*Last validated: 2025-10-02 - Political map mode rendering working perfectly after applying these patterns*

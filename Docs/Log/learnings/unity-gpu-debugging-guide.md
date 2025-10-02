# Unity GPU Debugging - Tools & Patterns

**Created:** 2025-10-02
**Applies To:** All Unity GPU compute shader and rendering work
**Companion Doc:** [unity-compute-shader-coordination.md](unity-compute-shader-coordination.md)

---

## What This Document Covers

This is a **reference guide** for Unity GPU debugging tools, patterns, and best practices. Use this when:
- Planning GPU pipelines with compute shaders
- Debugging GPU issues (wrong data, performance, crashes)
- Optimizing GPU performance
- Making architectural decisions about CPU vs GPU

**Companion Document:**
For the specific bugs and gotchas from actual debugging sessions, see [unity-compute-shader-coordination.md](unity-compute-shader-coordination.md).

---

## GPU Debugging Tools

### RenderDoc - The Essential Tool

**What it is:** Free, open-source frame capture tool that shows you EXACTLY what the GPU is doing.

**Why it matters:** Can turn 8-hour debugging sessions into 30-minute investigations. Shows:
- Actual GPU memory contents (what's REALLY in that texture)
- UAV vs SRV binding states (binding mismatches visible immediately)
- Compute shader execution (thread groups, actual dispatch counts)
- GPU timeline (race conditions show up as overlapping operations)

**Setup:**
1. Download RenderDoc (free, open source): https://renderdoc.org/
2. Launch Unity through RenderDoc:
   - RenderDoc → File → Launch Application
   - Select Unity.exe (or Unity Editor executable)
   - Launch
3. Run your scene
4. Press F12 (or use API) to capture a frame
5. Inspect in RenderDoc UI

**Programmatic Capture:**
```csharp
using UnityEngine.Rendering;

public class GPUDebugger : MonoBehaviour
{
    void Update()
    {
        // Press F12 to capture a frame
        if (Input.GetKeyDown(KeyCode.F12))
        {
            UnityEngine.Rendering.RenderDoc.BeginCaptureRenderDoc();

            // Your GPU code executes here
            ExecuteGPUPipeline();

            UnityEngine.Rendering.RenderDoc.EndCaptureRenderDoc();
            Debug.Log("RenderDoc frame captured - check RenderDoc UI");
        }
    }
}
```

**What You Can See in RenderDoc:**
- **Texture Viewer:** Click any RenderTexture, see actual pixel values
- **Resource Inspector:** Shows if texture bound as UAV or SRV
- **Event Browser:** All GPU commands in execution order (find race conditions)
- **Pipeline State:** Shader bindings, thread group sizes, buffer contents

**Example - Finding UAV/SRV Mismatch:**
1. Capture frame with F12
2. Find your compute shader dispatch in Event Browser
3. Click on shader dispatch
4. Resource Inspector shows texture bound as UAV
5. Next shader dispatch tries to bind same texture as SRV
6. **Bug visible:** State transition didn't happen

**Use RenderDoc FIRST when debugging GPU issues.**

### Unity Frame Debugger

**Built into Unity** (Window → Analysis → Frame Debugger)

**What it shows:**
- All draw calls and compute dispatches in order
- Intermediate RenderTexture states
- Shader bindings for each dispatch

**How to use:**
1. Open Frame Debugger window
2. Click "Enable"
3. Step through frame one dispatch at a time
4. Click on compute shader dispatch to see:
   - Which textures bound
   - Thread group size
   - Which kernel executed

**Limitations:**
- Only shows high-level info (not actual pixel data)
- Can't inspect UAV/SRV state
- Use RenderDoc for deeper inspection

**When to use:**
- Quick sanity checks (is shader dispatching?)
- Verify dispatch order
- Check texture bindings
- See what's executing each frame

### NVIDIA Nsight Graphics

**For NVIDIA GPUs only** - deeper profiling than RenderDoc.

**Shows:**
- Warp occupancy (how efficiently GPU threads are utilized)
- Memory bandwidth usage (find bottlenecks)
- Register pressure (if shader uses too many registers)

**When to use:** After you've fixed correctness bugs, now optimizing performance.

**Download:** https://developer.nvidia.com/nsight-graphics

---

## CommandBuffer API - Better GPU Pipeline Management

### The Problem with Manual Synchronization

Many developers start with manual sync:
```csharp
// ❌ Manual sync - blocks CPU
shaderA.Dispatch(...);
var sync = AsyncGPUReadback.Request(texture);
sync.WaitForCompletion(); // CPU stalls for 10-30ms
shaderB.Dispatch(...);
```

**Issues:**
- Blocks CPU (wasted time)
- CPU-GPU sync overhead
- Doesn't scale to complex pipelines (many shaders)
- Can't parallelize CPU and GPU work

### CommandBuffer Pattern

**Unity's CommandBuffer queues GPU commands, handles sync automatically:**

```csharp
using UnityEngine.Rendering;

public class MapTexturePopulator : MonoBehaviour
{
    private CommandBuffer gpuPipelineCmd;

    void InitializeCommandBuffer()
    {
        gpuPipelineCmd = new CommandBuffer();
        gpuPipelineCmd.name = "Map Texture Pipeline"; // Shows in profiler
    }

    public void PopulateAllTextures()
    {
        gpuPipelineCmd.Clear(); // Reuse buffer

        // Queue all GPU operations
        // Step 1: Populate province ID texture
        gpuPipelineCmd.SetComputeTextureParam(
            populateProvinceIDCompute,
            provinceIDKernel,
            "ProvinceIDTexture",
            textureManager.ProvinceIDTexture
        );
        gpuPipelineCmd.SetComputeBufferParam(
            populateProvinceIDCompute,
            provinceIDKernel,
            "ProvinceData",
            provinceDataBuffer
        );
        gpuPipelineCmd.DispatchCompute(
            populateProvinceIDCompute,
            provinceIDKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );

        // Unity handles GPU sync automatically between commands

        // Step 2: Populate owner texture (reads from ProvinceIDTexture)
        gpuPipelineCmd.SetComputeTextureParam(
            populateOwnerCompute,
            ownerKernel,
            "ProvinceIDTexture",
            textureManager.ProvinceIDTexture
        );
        gpuPipelineCmd.SetComputeTextureParam(
            populateOwnerCompute,
            ownerKernel,
            "OwnerTexture",
            textureManager.OwnerTexture
        );
        gpuPipelineCmd.DispatchCompute(
            populateOwnerCompute,
            ownerKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );

        // Step 3: Border detection (reads from ProvinceIDTexture)
        gpuPipelineCmd.SetComputeTextureParam(
            borderDetectionCompute,
            borderKernel,
            "ProvinceIDTexture",
            textureManager.ProvinceIDTexture
        );
        gpuPipelineCmd.SetComputeTextureParam(
            borderDetectionCompute,
            borderKernel,
            "BorderTexture",
            textureManager.BorderTexture
        );
        gpuPipelineCmd.DispatchCompute(
            borderDetectionCompute,
            borderKernel,
            threadGroupsX,
            threadGroupsY,
            1
        );

        // Execute entire pipeline on GPU (Unity handles all sync)
        Graphics.ExecuteCommandBuffer(gpuPipelineCmd);

        // CPU continues immediately - no blocking!
        // GPU executes all operations in order with proper sync
    }

    void OnDestroy()
    {
        gpuPipelineCmd?.Release(); // Clean up
    }
}
```

**Benefits:**
- ✅ No CPU blocking (CPU and GPU run in parallel)
- ✅ Unity handles GPU-side synchronization automatically
- ✅ Cleaner code (declarative pipeline)
- ✅ Shows as single entry in profiler
- ✅ Can be reused (just Clear() and re-queue)

**When to use:**
- ✅ Complex GPU pipelines (3+ dependent operations)
- ✅ Per-frame updates (can't afford WaitForCompletion)
- ✅ When you want GPU and CPU to work in parallel

**When NOT to use:**
- ❌ If you need CPU to read results immediately (then use WaitForCompletion)
- ❌ Single compute dispatch (overkill)

### Async Pattern with CommandBuffer

If you need to know when GPU finishes (but don't want to block):

```csharp
IEnumerator PopulateTexturesAsync()
{
    // Queue GPU work
    Graphics.ExecuteCommandBuffer(gpuPipelineCmd);

    // Request async readback (doesn't block)
    var request = AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);

    // Wait for completion (yields to other coroutines, doesn't block CPU)
    yield return new WaitUntil(() => request.done);

    // Now safe to read results or trigger dependent CPU work
    Debug.Log("GPU pipeline complete");
    OnTexturesReady();
}
```

---

## Texture Binding: UAV vs SRV Deep Dive

### What You Need to Understand

**DirectX has two ways to bind textures to shaders:**

1. **UAV (Unordered Access View)** - Read/Write
   - Shader declares: `RWTexture2D<float4>`
   - GPU allocates: Read-write cache, atomic support
   - Cost: Slightly slower reads (needs coherency checks)

2. **SRV (Shader Resource View)** - Read-Only
   - Shader declares: `Texture2D<float4>`
   - GPU allocates: Read-only cache, faster sampling
   - Cost: Can't write

**The problem:** GPU hardware has DIFFERENT cache/memory paths for UAV vs SRV.

### Why Mixing UAV/SRV Breaks

```hlsl
// Shader A
RWTexture2D<float4> OutputTexture; // Binds as UAV

void WriteShader(uint3 id : SV_DispatchThreadID)
{
    OutputTexture[id.xy] = someData; // Writes to UAV cache
}
```

```hlsl
// Shader B
Texture2D<float4> InputTexture; // Binds as SRV

void ReadShader(uint3 id : SV_DispatchThreadID)
{
    float4 data = InputTexture.Load(int3(id.xy, 0));
    // Reads from SRV cache (may not see UAV writes!)
}
```

**What happens internally:**
1. Shader A writes to GPU memory via UAV path
2. Data sits in UAV write cache
3. Shader B tries to read via SRV path
4. SRV cache doesn't know about UAV writes
5. Reads stale data or wrong memory

**Unity SHOULD transition texture state (UAV → flush → SRV), but doesn't always.**

### Solutions

**Option 1: Uniform RWTexture2D (Recommended)**

```hlsl
// Both shaders use UAV binding
RWTexture2D<float4> SharedTexture;

// Pros: Eliminates state transitions, consistent pattern
// Cons: Loses compile-time read-only checks (minimal issue)
```

**Option 2: Explicit Barrier (Advanced)**

```csharp
using UnityEngine.Rendering;

CommandBuffer cmd = new CommandBuffer();

// Dispatch write shader
cmd.DispatchCompute(writeShader, writeKernel, groupsX, groupsY, 1);

// Insert UAV barrier (forces GPU to flush UAV writes)
cmd.SetComputeTextureParam(writeShader, writeKernel, "DummyTexture", texture);
// Unity inserts barrier when switching texture binding

// Dispatch read shader (now safe)
cmd.DispatchCompute(readShader, readKernel, groupsX, groupsY, 1);

Graphics.ExecuteCommandBuffer(cmd);
```

**Option 3: Manual Flush**

```csharp
// After writing to RWTexture2D
shaderA.Dispatch(...);

// Force texture state transition
Graphics.SetRenderTarget(renderTexture);
Graphics.SetRenderTarget(null); // Flush

// Now safe to bind as Texture2D
shaderB.SetTexture(kernel, "InputTexture", renderTexture);
shaderB.Dispatch(...);
```

**Recommendation:** Stick with RWTexture2D for consistency unless you have specific performance needs.

---

## Thread Group Size Optimization

### Default: 8x8 (64 threads)

This is a good default for most 2D texture operations:
```hlsl
[numthreads(8, 8, 1)] // 64 threads per group
void MyKernel(uint3 id : SV_DispatchThreadID) { }
```

### Memory-Bound Operations - Use Larger Groups

**If shader does lots of texture reads/writes:**

```hlsl
// Memory-bound: Lots of texture sampling
[numthreads(16, 16, 1)] // 256 threads - better memory coalescing
void BorderDetection(uint3 id : SV_DispatchThreadID)
{
    // Reads 9 texels (3x3 kernel)
    float4 center = InputTexture[id.xy];
    float4 left = InputTexture[id.xy + int2(-1, 0)];
    float4 right = InputTexture[id.xy + int2(1, 0)];
    // ... more reads

    // Light computation
    float4 result = center != left || center != right ? BORDER : NO_BORDER;
    OutputTexture[id.xy] = result;
}
```

**Why larger groups:** GPU can batch memory requests from more threads, hiding latency.

### Compute-Bound Operations - Use Smaller Groups

**If shader does heavy math:**

```hlsl
// Compute-bound: Heavy calculations
[numthreads(8, 8, 1)] // 64 threads - less register pressure
void ComplexCalculation(uint3 id : SV_DispatchThreadID)
{
    // Lots of local variables (uses registers)
    float var1 = ...;
    float var2 = ...;
    float results[10] = {...};

    // Heavy computation (loops, branches)
    for (int i = 0; i < 100; i++)
    {
        var1 = var1 * var2 + results[i % 10];
    }

    OutputTexture[id.xy] = var1;
}
```

**Why smaller groups:** Fewer threads = more registers per thread, less spilling to slow memory.

### 1D Buffer Operations

**If processing linear buffers (not 2D textures):**

```hlsl
// 1D data processing
[numthreads(64, 1, 1)] // Linear layout
void ProcessProvinceData(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= ProvinceCount) return;

    ProvinceData data = InputBuffer[index];
    // Process...
    OutputBuffer[index] = processedData;
}
```

```csharp
// Dispatch for 1D buffer
int threadGroups = (provinceCount + 63) / 64;
computeShader.Dispatch(kernel, threadGroups, 1, 1);
```

### How to Find Optimal Size

**Profile different sizes:**

```csharp
void ProfileThreadGroupSizes()
{
    int[] threadSizes = { 4, 8, 16, 32 };

    foreach (int size in threadSizes)
    {
        computeShader.SetInt("ThreadGroupSize", size);

        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            computeShader.Dispatch(kernel,
                (width + size - 1) / size,
                (height + size - 1) / size,
                1
            );
        }

        // Force GPU completion
        var sync = AsyncGPUReadback.Request(outputTexture);
        sync.WaitForCompletion();

        sw.Stop();
        Debug.Log($"Thread size {size}x{size}: {sw.ElapsedMilliseconds}ms");
    }
}
```

**Rule of thumb:**
- Default: 8x8 (64 threads)
- Heavy texture sampling: 16x16 (256 threads)
- Heavy computation: 8x8 or 4x4
- 1D buffers: 64x1x1 or 128x1x1

---

## Platform Performance Considerations

### AsyncGPUReadback.WaitForCompletion() Varies by GPU

Performance varies significantly across different PC configurations:

| GPU | 5632x2048 Sync Time | Notes |
|-----|---------------------|-------|
| Desktop RTX 3080 | ~5ms | Fast PCIe, large cache |
| Desktop GTX 1060 | ~15ms | Older architecture |
| Desktop RX 6700 XT | ~8ms | AMD RDNA2 |
| Laptop RTX 3060 | ~12ms | Thermal throttling |
| Laptop Intel UHD | ~80ms | Shared memory, slow |
| Steam Deck | ~40ms | RDNA2 mobile |

**Implications:**
- Test on minimum spec (integrated graphics)
- Steam Deck is mid-range laptop performance
- Laptop GPUs throttle under sustained load

### Optimization for Low-End Hardware

**For integrated graphics, prefer CommandBuffer over WaitForCompletion:**

```csharp
// High-end GPU: WaitForCompletion() acceptable for init
void InitializeHighEnd()
{
    shaderA.Dispatch(...);
    var sync = AsyncGPUReadback.Request(texture);
    sync.WaitForCompletion(); // 5-15ms on dedicated GPU
    shaderB.Dispatch(...);
}

// Low-end GPU: CommandBuffer avoids CPU stalls
void InitializeLowEnd()
{
    CommandBuffer cmd = new CommandBuffer();
    cmd.DispatchCompute(shaderA, kernelA, groupsX, groupsY, 1);
    cmd.DispatchCompute(shaderB, kernelB, groupsX, groupsY, 1);
    Graphics.ExecuteCommandBuffer(cmd); // No CPU stall
    cmd.Release();
}
```

**Steam Deck considerations:**
- Similar to laptop GTX 1650
- Thermal throttling after 10-15 minutes
- Test with sustained load, not quick benchmarks

---

## Graphics.Blit - Why It's Problematic

### Platform-Dependent Y-Flipping

**Graphics.Blit does platform-dependent Y-flipping:**

```csharp
Texture2D source = ...; // Y-up (0,0)=bottom-left
RenderTexture dest = ...;

Graphics.Blit(source, dest);

// On DirectX 11: Y-flips (0,0)=top-left in dest
// On OpenGL: No Y-flip (0,0)=bottom-left in dest
// On Metal: Sometimes Y-flips depending on texture format
// On Vulkan: Depends on Unity version
```

**The problem:** Same code produces different results on different platforms.

### Example Bug

```csharp
// Generate texture on CPU
Texture2D cpuTexture = new Texture2D(width, height);
cpuTexture.SetPixels32(pixelData); // (0,0)=bottom-left (Unity convention)
cpuTexture.Apply();

// Blit to RenderTexture
Graphics.Blit(cpuTexture, renderTexture);

// Compute shader reads
computeShader.SetTexture(kernel, "InputTexture", renderTexture);
computeShader.Dispatch(kernel, groupsX, groupsY, 1);

// Result:
// - Windows: Correct (Blit Y-flipped, compute shader expects top-left)
// - Mac: Upside down (Blit didn't Y-flip, compute shader expects top-left)
// - Linux: Depends on graphics API
```

### Solution: Always Use Compute Shader for Upload

```csharp
// Upload CPU data to GPU texture (platform-independent)
void UploadTextureViaCompute(Color32[] cpuData, RenderTexture gpuTexture)
{
    // Create GPU buffer
    ComputeBuffer buffer = new ComputeBuffer(cpuData.Length, sizeof(uint));

    // Pack Color32 into uint (RGBA → 0xAABBGGRR)
    uint[] packedData = new uint[cpuData.Length];
    for (int i = 0; i < cpuData.Length; i++)
    {
        Color32 c = cpuData[i];
        packedData[i] = ((uint)c.a << 24) | ((uint)c.b << 16) | ((uint)c.g << 8) | c.r;
    }

    buffer.SetData(packedData);

    // Upload via compute shader
    uploadShader.SetBuffer(uploadKernel, "InputBuffer", buffer);
    uploadShader.SetTexture(uploadKernel, "OutputTexture", gpuTexture);
    uploadShader.SetInt("Width", gpuTexture.width);
    uploadShader.SetInt("Height", gpuTexture.height);
    uploadShader.Dispatch(uploadKernel,
        (gpuTexture.width + 7) / 8,
        (gpuTexture.height + 7) / 8,
        1
    );

    buffer.Release();
}
```

```hlsl
// UploadTexture.compute
#pragma kernel UploadTexture

StructuredBuffer<uint> InputBuffer;
RWTexture2D<float4> OutputTexture;
uint Width;
uint Height;

[numthreads(8, 8, 1)]
void UploadTexture(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;

    // Read from linear buffer (CPU layout: row-major, (0,0)=top-left)
    uint index = id.y * Width + id.x;
    uint packed = InputBuffer[index];

    // Unpack RGBA
    float r = (packed & 0xFF) / 255.0;
    float g = ((packed >> 8) & 0xFF) / 255.0;
    float b = ((packed >> 16) & 0xFF) / 255.0;
    float a = ((packed >> 24) & 0xFF) / 255.0;

    // Write to GPU texture (no Y-flip needed - already top-left)
    OutputTexture[id.xy] = float4(r, g, b, a);
}
```

**Result:** Platform-independent, explicit control over coordinates.

---

## Texture Format Selection

### Format Affects Bandwidth and Precision

**RenderTexture format affects bandwidth and precision:**

```csharp
// High precision, high bandwidth
RenderTexture provinceIDTexture = new RenderTexture(width, height, 0,
    RenderTextureFormat.ARGB32); // 32 bits per pixel

// Lower precision, lower bandwidth
RenderTexture optimized = new RenderTexture(width, height, 0,
    RenderTextureFormat.R16); // 16 bits per pixel (half bandwidth!)
```

### Format Selection Guide

| Use Case | Recommended Format | Bits/Pixel | Notes |
|----------|-------------------|------------|-------|
| Province IDs (0-65535) | R16 or RG16 | 16-32 | Integer IDs |
| Owner IDs (0-255) | R8 | 8 | Small country count |
| Colors (display) | ARGB32 | 32 | Full color |
| HDR colors | ARGBHalf | 64 | Overkill for most |
| Float values | RFloat | 32 | Scientific precision |
| Packed data | ARGB32 | 32 | 4 bytes of arbitrary data |

### Example: Optimize Province ID Texture

```csharp
// Before: 32 bits per pixel (overkill)
RenderTexture provinceIDTexture = new RenderTexture(5632, 2048, 0,
    RenderTextureFormat.ARGB32);
// Size: 5632 × 2048 × 4 bytes = 46 MB

// After: 16 bits per pixel (sufficient for 65535 provinces)
RenderTexture provinceIDTexture = new RenderTexture(5632, 2048, 0,
    RenderTextureFormat.R16);
// Size: 5632 × 2048 × 2 bytes = 23 MB (50% smaller!)
```

```hlsl
// Shader adjustment for R16 format
RWTexture2D<float> ProvinceIDTexture; // Now single-channel

[numthreads(8,8,1)]
void PopulateIDs(uint3 id : SV_DispatchThreadID)
{
    uint provinceID = GetProvinceID(id.xy);

    // Encode 16-bit ID as normalized float
    ProvinceIDTexture[id.xy] = float(provinceID) / 65535.0;
}

// Reading in another shader
float provinceIDNormalized = ProvinceIDTexture[id.xy];
uint provinceID = uint(provinceIDNormalized * 65535.0 + 0.5); // Round to nearest
```

**Benefits:**
- 50% less memory bandwidth (faster reads/writes)
- 50% less GPU memory (more textures fit in cache)
- Same precision for integer IDs

---

## When NOT to Use Compute Shaders

### Compute Isn't Always the Answer

**Use fragment shaders instead for:**

1. **Per-pixel effects that are view-dependent**
   ```hlsl
   // This SHOULD be a fragment shader, not compute
   float4 ApplyFogOfWar(float2 screenPos)
   {
       float distance = length(screenPos - cameraPos);
       float fogAmount = saturate(distance / fogRange);
       return lerp(clearColor, fogColor, fogAmount);
   }
   ```

2. **Effects that need mip-maps**
   - Fragment shaders auto-select mip level
   - Compute shaders must calculate manually

3. **Post-processing on rendered scene**
   - Use OnRenderImage or RenderPipeline post-processing
   - Compute shader would need to copy render target (expensive)

**Use CPU instead for:**

1. **Infrequent updates (once per game start)**
   - Overhead of GPU upload/download not worth it
   - Simpler code, easier debugging

2. **Small datasets (< 1000 elements)**
   - GPU overhead exceeds computation time
   - Example: 10 provinces don't need GPU

3. **Complex branching logic**
   - GPUs hate branches (SIMD architecture)
   - CPU handles branches efficiently

**Example:**

```csharp
// DON'T use compute shader for this (too small, infrequent)
void CalculateCountryStats(Country[] countries)
{
    // Only 200 countries, update once per month
    foreach (var country in countries)
    {
        country.totalDevelopment = country.provinces.Sum(p => p.development);
        country.totalIncome = country.provinces.Sum(p => p.taxIncome);
        country.armySize = country.armies.Sum(a => a.soldierCount);
    }
}

// DO use compute shader for this (10K provinces, frequent updates)
void UpdateProvinceVisuals(Province[] provinces)
{
    // 10,000 provinces, update every frame
    // Parallel RGB encoding for map display
    computeShader.Dispatch(...);
}
```

---

## Summary: GPU Debugging Workflow

**When you hit a GPU bug:**

1. **Use RenderDoc first** - Capture frame, inspect actual GPU state
2. **Check Frame Debugger** - Verify dispatch order and bindings
3. **Add diagnostic logging** - Track data through pipeline with known test coordinates
4. **Profile if needed** - Use Nsight for performance issues

**When planning GPU work:**

1. **Use CommandBuffer** for multi-step pipelines (not manual sync)
2. **Use RWTexture2D** for all compute shader texture access (avoid UAV/SRV mixing)
3. **Choose appropriate texture formats** (don't waste bandwidth)
4. **Profile thread group sizes** if performance matters
5. **Avoid Graphics.Blit** - use compute shaders for platform-independent uploads

**Related Documents:**
- [unity-compute-shader-coordination.md](unity-compute-shader-coordination.md) - Specific bugs and fixes from actual debugging

---

*Last updated: 2025-10-02*

# Map Layer Stress Test Plan
**Date:** 2025-10-02 (Updated after audit)
**Goal:** Identify Map layer performance bottlenecks under realistic load

---

## ⚠️ IMPORTANT: Profile Actual Gameplay First

**Before running synthetic stress tests:**

1. **Play the game normally** for 10-15 minutes
2. **Run Unity Profiler** in deep profile mode
3. **Look for actual frame spikes** in real gameplay
4. **Identify real bottlenecks** (might be UI, AI, or something unexpected)

**Why:** Synthetic stress tests can mislead you. The real bottleneck might be completely different from what you expect. Profile real gameplay first, then use targeted stress tests to reproduce specific issues.

**Unity Profiler Setup:**
- Window → Analysis → Profiler
- Enable "Deep Profile" (captures all function calls)
- Switch to "GPU" view to see actual GPU work
- Look for frame spikes >16.67ms

**Once you have baseline data from real gameplay, use the tests below to stress-test specific components.**

---

## Performance Budget Overview

**Target:** 60 FPS = 16.67ms frame budget

| Component | Budget | Critical? |
|-----------|--------|-----------|
| GPU Rendering | 3-5ms | Yes |
| Border Detection | 2-3ms | Yes |
| Texture Updates | 2-4ms | **CRITICAL** |
| Event Processing | 1-2ms | **CRITICAL** |
| Province Selection | <1ms | No |
| Reserve | 3-5ms | - |

---

## Test 1: Texture Update Stress (Event Bus Saturation)

**Goal:** Test TextureUpdateBridge throughput when receiving province ownership changes.

**Performance Killer:** CPU→GPU texture upload bandwidth, unbatched updates.

```csharp
// Assets/Scripts/Tests/Manual/MapTextureStressTest.cs
using UnityEngine;
using Unity.Profiling;
using Core;
using Core.Systems;
using Core.Data;
using Map.Rendering;
using Utils;

namespace Tests.Manual
{
    public enum TestScenario
    {
        Minimal,      // 5 provinces (normal gameplay)
        Realistic,    // 50 provinces (active war)
        Extreme,      // 1000 provinces (empire collapse)
        Nuclear       // All 10k provinces (impossible, for stress only)
    }

    /// <summary>
    /// Stress test: Emit ownership change events with realistic scenarios
    /// Tests: EventBus→TextureUpdateBridge→MapTextureManager pipeline
    /// IMPORTANT: Use Unity Profiler to see actual GPU time, not manual timing
    /// </summary>
    public class MapTextureStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameState gameState;
        [SerializeField] private TextureUpdateBridge textureUpdateBridge;

        [Header("Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private TestScenario scenario = TestScenario.Realistic;
        [SerializeField] private bool logPerformance = true;
        [SerializeField] private bool trackAllocations = true;

        private ProvinceSystem provinceSystem;
        private EventBus eventBus;
        private DeterministicRandom rng;
        private int frameCount = 0;

        // Unity Profiler markers for accurate measurement
        private static readonly ProfilerMarker s_EmitEventsMarker =
            new ProfilerMarker("MapStress.EmitEvents");
        private static readonly ProfilerMarker s_ProcessEventsMarker =
            new ProfilerMarker("MapStress.ProcessEvents");

        void Start()
        {
            provinceSystem = gameState.ProvinceSystem;
            eventBus = gameState.EventBus;
            rng = new DeterministicRandom(99999);

            DominionLogger.Log($"MapTextureStressTest initialized. Scenario: {scenario}");
            DominionLogger.Log($"⚠️ Use Unity Profiler (GPU view) for accurate measurements!");
        }

        void Update()
        {
            if (!enableStressTest) return;

            // Track allocations
            long allocBefore = 0;
            if (trackAllocations)
            {
                allocBefore = System.GC.GetTotalMemory(false);
            }

            // Emit ownership changes (profiler markers capture timing)
            EmitOwnershipChanges();

            // Check for allocations
            if (trackAllocations)
            {
                long allocAfter = System.GC.GetTotalMemory(false);
                long allocated = allocAfter - allocBefore;

                if (allocated > 0)
                {
                    DominionLogger.LogWarning($"❌ Allocated {allocated / 1024f:F2} KB this frame! (should be 0)");
                }
            }

            frameCount++;

            if (logPerformance && frameCount % 60 == 0)
            {
                int eventCount = GetEventCountForScenario();
                DominionLogger.Log($"Texture Update Stress - Scenario: {scenario}, Events/frame: {eventCount}");
                DominionLogger.Log($"Check Unity Profiler → GPU view for actual texture update time");
            }
        }

        /// <summary>
        /// Emit ProvinceOwnershipChangedEvent based on scenario
        /// Realistic scenarios test actual gameplay patterns
        /// </summary>
        private void EmitOwnershipChanges()
        {
            int eventCount = GetEventCountForScenario();

            using (s_EmitEventsMarker.Auto())
            {
                for (int i = 0; i < eventCount; i++)
                {
                    ushort provinceId = provinceSystem.GetProvinceIdAtIndex(
                        rng.NextInt(0, provinceSystem.ProvinceCount - 1)
                    );
                    ushort randomOwner = (ushort)rng.NextInt(1, 100);

                    eventBus.Emit(new ProvinceOwnershipChangedEvent
                    {
                        provinceId = provinceId,
                        oldOwner = 0,
                        newOwner = randomOwner
                    });
                }
            }

            using (s_ProcessEventsMarker.Auto())
            {
                // This triggers TextureUpdateBridge
                eventBus.ProcessEvents();
            }
        }

        private int GetEventCountForScenario()
        {
            return scenario switch
            {
                TestScenario.Minimal => 5,
                TestScenario.Realistic => 50,
                TestScenario.Extreme => 1000,
                TestScenario.Nuclear => provinceSystem.ProvinceCount,
                _ => 50
            };
        }

        void OnGUI()
        {
            if (!enableStressTest) return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 200));
            GUILayout.Label($"<b>Map Texture Stress Test</b>", new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true });
            GUILayout.Label($"Scenario: {scenario}");
            GUILayout.Label($"Events/frame: {GetEventCountForScenario()}");
            GUILayout.Label($"Frames tested: {frameCount}");
            GUILayout.Label($"");
            GUILayout.Label($"<color=yellow>⚠️ Open Unity Profiler (GPU view)</color>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Look for: MapStress.ProcessEvents");
            GUILayout.EndArea();
        }
    }
}
```

**Performance Targets (from Unity Profiler, not manual timing):**

| Scenario | Events | Target | Acceptable | Failure |
|----------|--------|--------|------------|---------|
| Minimal | 5 | <0.5ms | <1ms | >2ms |
| Realistic | 50 | <2ms | <5ms | >10ms |
| Extreme | 1000 | <10ms | <15ms | >20ms |
| Nuclear | 10k | N/A* | N/A* | This will never happen in real gameplay |

**Expected Result:** Profiler will show if TextureUpdateBridge batches updates or processes individually.

---

## Test 2: Border Recomputation Stress

**Goal:** Test GPU compute shader dispatch and execution time.

**Performance Killer:** Compute shader dispatch frequency, GPU occupancy.

**⚠️ CRITICAL:** Measuring GPU work requires forced synchronization!

```csharp
// Assets/Scripts/Tests/Manual/BorderComputeStressTest.cs
using UnityEngine;
using Unity.Profiling;
using Map.Rendering;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Stress test: Trigger border detection every frame
    /// Tests: BorderComputeDispatcher overhead, GPU compute throughput
    /// IMPORTANT: Measures actual GPU execution time with forced sync
    /// WARNING: WaitForCompletion() kills performance - use only for benchmarking!
    /// </summary>
    public class BorderComputeStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private BorderComputeDispatcher borderDispatcher;
        [SerializeField] private MapTextureManager textureManager;

        [Header("Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private bool measureActualGPUTime = false; // Enable for accurate timing (slow!)
        [SerializeField] private bool logPerformance = true;

        private int frameCount = 0;
        private float totalComputeTime = 0f;

        private static readonly ProfilerMarker s_DispatchMarker =
            new ProfilerMarker("BorderStress.Dispatch");
        private static readonly ProfilerMarker s_GPUSyncMarker =
            new ProfilerMarker("BorderStress.GPUSync");

        void Update()
        {
            if (!enableStressTest || borderDispatcher == null) return;

            float gpuTime = 0f;

            using (s_DispatchMarker.Auto())
            {
                borderDispatcher.DetectBorders();
            }

            // Measure actual GPU execution time (WARNING: Very slow!)
            if (measureActualGPUTime)
            {
                using (s_GPUSyncMarker.Auto())
                {
                    float syncStart = Time.realtimeSinceStartup;

                    // Force GPU to complete (adds 30ms+ stall!)
                    var sync = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderTexture);
                    sync.WaitForCompletion();

                    gpuTime = (Time.realtimeSinceStartup - syncStart) * 1000f;
                }

                totalComputeTime += gpuTime;
                frameCount++;

                if (logPerformance && frameCount % 60 == 0)
                {
                    float avgTime = totalComputeTime / frameCount;
                    DominionLogger.Log($"Border Compute (GPU actual) - Avg: {avgTime:F2}ms, " +
                        $"FPS: {1000f / avgTime:F1}");
                    DominionLogger.LogWarning($"⚠️ WaitForCompletion adds {avgTime:F2}ms stall - disable for gameplay!");
                }
            }
            else if (logPerformance && frameCount % 60 == 0)
            {
                DominionLogger.Log($"Border Compute Stress - Check Unity Profiler (GPU view) for actual time");
            }

            frameCount++;
        }

        void OnGUI()
        {
            if (!enableStressTest) return;

            GUILayout.BeginArea(new Rect(10, 220, 400, 150));
            GUILayout.Label($"<b>Border Compute Stress</b>", new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true });
            GUILayout.Label($"Measure GPU time: {measureActualGPUTime}");

            if (measureActualGPUTime)
            {
                GUILayout.Label($"<color=red>⚠️ GPU sync enabled - performance killed!</color>", new GUIStyle(GUI.skin.label) { richText = true });
            }
            else
            {
                GUILayout.Label($"<color=yellow>Use Unity Profiler → GPU view</color>", new GUIStyle(GUI.skin.label) { richText = true });
            }

            GUILayout.EndArea();
        }
    }
}
```

**Performance Targets (from Unity Profiler GPU view):**
- **Good:** <2ms per frame (from FILE_REGISTRY)
- **Acceptable:** <3ms per frame
- **Failure:** >5ms per frame → Compute shader needs optimization

**Expected Result:** Should be fast (already GPU-optimized). If slow, check thread group size or GPU occupancy.

**Note on measureActualGPUTime:**
- ✅ Gives accurate GPU execution time
- ❌ Adds 30ms+ CPU stall (kills framerate)
- 🎯 **Use only for one-time benchmark, then disable**

---

## Test 3: Province Selection Spam

**Goal:** Test province selection throughput (texture reads).

**Performance Killer:** GPU→CPU synchronization, texture read overhead.

```csharp
// Assets/Scripts/Tests/Manual/ProvinceSelectionStressTest.cs
using UnityEngine;
using Map.Interaction;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Stress test: Query province at random positions every frame
    /// Tests: ProvinceSelector texture read performance
    /// Expected: Should be <1ms (texture lookup is fast)
    /// </summary>
    public class ProvinceSelectionStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private ProvinceSelector provinceSelector;

        [Header("Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private int queriesPerFrame = 100; // Spam selection checks
        [SerializeField] private bool logPerformance = true;

        private int frameCount = 0;
        private float totalQueryTime = 0f;
        private System.Random rng = new System.Random();

        void Update()
        {
            if (!enableStressTest || provinceSelector == null) return;

            float startTime = Time.realtimeSinceStartup;

            // Query random screen positions
            for (int i = 0; i < queriesPerFrame; i++)
            {
                Vector2 randomScreenPos = new Vector2(
                    rng.Next(0, Screen.width),
                    rng.Next(0, Screen.height)
                );

                // This does texture lookup (should be fast)
                ushort provinceId = provinceSelector.GetProvinceAtScreenPosition(randomScreenPos);
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            totalQueryTime += elapsed;
            frameCount++;

            if (logPerformance && frameCount % 60 == 0)
            {
                float avgTime = totalQueryTime / frameCount;
                DominionLogger.Log($"Selection Stress - Avg: {avgTime:F2}ms/frame, " +
                    $"Queries: {queriesPerFrame}, Time/query: {avgTime / queriesPerFrame:F3}ms");
            }
        }
    }
}
```

**Performance Targets:**
- **Good:** <1ms per frame (100 queries)
- **Acceptable:** <2ms per frame
- **Failure:** >5ms per frame → Texture read optimization needed

**Expected Result:** Should be very fast. If slow, check if ProvinceSelector is doing GPU sync or ReadPixels().

---

## Test 4: Map Mode Switch Spam

**Goal:** Test map mode switching overhead.

**Performance Killer:** Shader parameter updates, texture rebinding.

```csharp
// Assets/Scripts/Tests/Manual/MapModeSwitchStressTest.cs
using UnityEngine;
using Map.MapModes;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Stress test: Switch map modes every frame
    /// Tests: MapModeManager overhead, shader parameter updates
    /// Expected: Should have minimal cost (just shader parameter changes)
    /// </summary>
    public class MapModeSwitchStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MapModeManager mapModeManager;

        [Header("Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private bool logPerformance = true;

        private int frameCount = 0;
        private float totalSwitchTime = 0f;
        private MapMode[] modes = { MapMode.Political, MapMode.Terrain, MapMode.Development };
        private int currentModeIndex = 0;

        void Update()
        {
            if (!enableStressTest || mapModeManager == null) return;

            float startTime = Time.realtimeSinceStartup;

            // Switch to next map mode
            currentModeIndex = (currentModeIndex + 1) % modes.Length;
            mapModeManager.SetMapMode(modes[currentModeIndex]);

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            totalSwitchTime += elapsed;
            frameCount++;

            if (logPerformance && frameCount % 60 == 0)
            {
                float avgTime = totalSwitchTime / frameCount;
                DominionLogger.Log($"Map Mode Switch Stress - Avg: {avgTime:F2}ms/frame");
            }
        }
    }
}
```

**Performance Targets:**
- **Good:** <0.5ms per switch
- **Acceptable:** <1ms per switch
- **Failure:** >2ms per switch → Investigate shader caching

---

## Test 5: Combined Full Pipeline Stress

**Goal:** All stressors at once - worst-case scenario.

```csharp
// Assets/Scripts/Tests/Manual/FullMapPipelineStressTest.cs
using UnityEngine;
using Core;
using Map.Rendering;
using Map.Interaction;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Ultimate stress test: Everything at once
    /// - 10k province ownership changes/frame
    /// - Border recomputation/frame
    /// - 100 province selection queries/frame
    /// Tests: Full Map layer under extreme load
    /// Target: Stay under 16.67ms for 60 FPS
    /// </summary>
    public class FullMapPipelineStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameState gameState;
        [SerializeField] private TextureUpdateBridge textureUpdateBridge;
        [SerializeField] private BorderComputeDispatcher borderDispatcher;
        [SerializeField] private ProvinceSelector provinceSelector;

        [Header("Configuration")]
        [SerializeField] private bool enableStressTest = false;

        private ProvinceSystem provinceSystem;
        private EventBus eventBus;
        private DeterministicRandom rng;
        private System.Random screenRng = new System.Random();

        void Start()
        {
            provinceSystem = gameState.ProvinceSystem;
            eventBus = gameState.EventBus;
            rng = new DeterministicRandom(88888);
        }

        void Update()
        {
            if (!enableStressTest) return;

            float frameStart = Time.realtimeSinceStartup;

            // 1. Emit 10k ownership changes
            EmitOwnershipChanges();

            // 2. Recompute borders
            borderDispatcher.DetectBorders();

            // 3. Spam province selection
            for (int i = 0; i < 100; i++)
            {
                Vector2 randomPos = new Vector2(
                    screenRng.Next(0, Screen.width),
                    screenRng.Next(0, Screen.height)
                );
                provinceSelector.GetProvinceAtScreenPosition(randomPos);
            }

            float frameTime = (Time.realtimeSinceStartup - frameStart) * 1000f;
            float fps = 1000f / frameTime;

            // Log if frame budget exceeded
            if (frameTime > 16.67f)
            {
                DominionLogger.LogWarning($"❌ Frame budget exceeded: {frameTime:F2}ms ({fps:F1} FPS)");
            }
            else
            {
                DominionLogger.Log($"✅ Within budget: {frameTime:F2}ms ({fps:F1} FPS)");
            }
        }

        private void EmitOwnershipChanges()
        {
            int count = provinceSystem.ProvinceCount;
            for (int i = 0; i < count; i++)
            {
                ushort provinceId = provinceSystem.GetProvinceIdAtIndex(i);
                ushort randomOwner = (ushort)rng.NextInt(1, 100);

                eventBus.Emit(new ProvinceOwnershipChangedEvent
                {
                    provinceId = provinceId,
                    newOwner = randomOwner
                });
            }
            eventBus.ProcessEvents();
        }
    }
}
```

**Performance Target:**
- **Success:** <16.67ms per frame (60 FPS)
- **Failure:** >16.67ms per frame → Identify bottleneck with Unity Profiler

---

## Performance Profiling Checklist

### Unity Profiler Setup

**CPU Profiler:**
1. Window → Analysis → Profiler
2. Enable "Deep Profile" (captures all function calls)
3. Look for custom markers: `MapStress.*`, `BorderStress.*`

**GPU Profiler (CRITICAL):**
1. Profiler window → Switch to "GPU" view
2. Look for:
   - Compute shader dispatches
   - Texture upload bandwidth
   - GPU occupancy %

**What to Watch:**

| Component | CPU Profiler | GPU Profiler |
|-----------|--------------|--------------|
| TextureUpdateBridge | `MapStress.ProcessEvents` | Texture uploads (MB/s) |
| BorderCompute | `BorderStress.Dispatch` | Compute shader execution |
| EventBus | `EventBus.ProcessEvents` | N/A |
| Allocations | GC.Alloc column | N/A |

**Expected Bottlenecks:**
- 🔴 **Texture uploads** - GB/s bandwidth saturation (check GPU profiler)
- 🔴 **TextureUpdateBridge** - If not batching (check CPU profiler)
- 🔴 **EventBus** - If allocating (check GC.Alloc column)
- 🟡 **Compute shader** - Thread occupancy (check GPU profiler)

### GPU Memory Bandwidth Analysis

**Add this to your test classes:**

```csharp
void LogGPUBandwidth()
{
    // Example: ProvinceIDTexture is 5632×2048 ARGB32
    int textureSizeBytes = 5632 * 2048 * 4; // 46 MB

    // If updating every frame at 60 FPS
    float bandwidthMBps = (textureSizeBytes / 1_000_000f) * 60f;
    float bandwidthGBps = bandwidthMBps / 1000f;

    DominionLogger.Log($"Texture upload bandwidth: {bandwidthGBps:F2} GB/s");

    // PCIe 3.0 x16 theoretical max: ~15 GB/s
    // PCIe 3.0 x16 realistic: ~12 GB/s
    // If you hit GB/s numbers, you've saturated the bus!

    if (bandwidthGBps > 1.0f)
    {
        DominionLogger.LogWarning($"⚠️ High bandwidth usage: {bandwidthGBps:F2} GB/s");
        DominionLogger.LogWarning($"Consider dirty rectangle updates instead of full texture");
    }
}
```

---

## Success Criteria

### Individual Tests (Measured in Unity Profiler, NOT manual timing)

| Test | Scenario | Target | Acceptable | Failure |
|------|----------|--------|------------|---------|
| Texture Updates | Minimal (5 events) | <0.5ms | <1ms | >2ms |
| Texture Updates | Realistic (50 events) | <2ms | <5ms | >10ms |
| Texture Updates | Extreme (1000 events) | <10ms | <15ms | >20ms |
| Border Compute | Every frame | <2ms | <3ms | >5ms |
| Selection Spam | 100 queries | <1ms | <2ms | >5ms |
| Map Mode Switch | Per switch | <0.5ms | <1ms | >2ms |

### Full Pipeline Test (Realistic Scenario)
| Metric | Target | How to Measure |
|--------|--------|----------------|
| Frame Time | <16.67ms (60 FPS) | Unity Profiler → CPU |
| GPU Time | <10ms | Unity Profiler → GPU |
| CPU Usage | <50% | Task Manager / Activity Monitor |
| Allocations | **0 KB/frame** | Profiler → GC.Alloc column |
| GPU Bandwidth | <1 GB/s | Calculate from texture sizes |

### Test on Minimum Spec Hardware

**Your dev machine (RTX 3080) will hide problems. Test on:**

| Hardware | Expected Performance |
|----------|---------------------|
| Integrated GPU (Intel UHD) | 30-45 FPS in Realistic scenario |
| Old Desktop (GTX 1060) | 45-60 FPS in Realistic scenario |
| Laptop (Mobile GPU) | 30-50 FPS, watch for thermal throttling |
| Steam Deck | 30-40 FPS (RDNA2 mobile) |

**Critical:** If Realistic scenario (50 province updates) drops below 30 FPS on integrated graphics, you need optimization.

---

## Optimization Recommendations (If Tests Fail)

### If Texture Updates Slow (>10ms in Realistic scenario):

**Root Cause: Unbatched updates or full texture uploads**

1. **Batch updates** - Collect all province changes in a frame, update texture once
   ```csharp
   // Instead of updating texture per event:
   foreach (var change in provinceChanges)
   {
       UpdateTextureRegion(change); // Many small uploads
   }

   // Batch into single update:
   CollectChanges(provinceChanges);
   UpdateTextureOnce(allChanges); // One upload
   ```

2. **Use dirty rectangles** - Only update changed texture regions
   ```csharp
   // Calculate bounding box of changed provinces
   Rect dirtyRegion = CalculateDirtyRect(changedProvinces);
   texture.Apply(false, false); // Update only dirty region
   ```

3. **CommandBuffer** - Replace manual sync with CommandBuffer (see unity-gpu-debugging-guide.md)

4. **Reduce texture format size** - R16 instead of ARGB32 where possible (50% bandwidth)

### If Border Compute Slow (>5ms):

**Root Cause: Thread group size or dispatch overhead**

1. **Profile thread group size** - Try 16x16 instead of 8x8
   ```hlsl
   [numthreads(16, 16, 1)] // 256 threads (was 64)
   ```

2. **Check GPU occupancy** - Unity Frame Debugger → GPU tab → Look for low occupancy %

3. **Reduce dispatch frequency** - Only recompute borders when province ownership changes
   ```csharp
   if (provinceOwnershipChanged) // Add dirty flag
   {
       borderDispatcher.DetectBorders();
   }
   ```

4. **Early exit in shader** - Skip unchanged regions
   ```hlsl
   if (currentProvince == cachedProvince) return; // No border here
   ```

### If Selection Slow (>5ms):

**Root Cause: GPU sync or texture read overhead**

1. **Check for GPU sync** - Remove WaitForCompletion() calls in ProvinceSelector
2. **Cache results** - Don't read texture every frame if mouse hasn't moved
   ```csharp
   if (mousePosition == lastMousePosition) return cachedProvinceId;
   ```
3. **Use async pattern** - AsyncGPUReadback without blocking (adds 1-frame latency)

### If Allocations Detected:

**Zero allocations per frame is mandatory for 60 FPS.**

1. **Pool events** - EventBus should use object pooling
2. **Avoid LINQ** - No `.Where()`, `.Select()`, etc. in hot path
3. **Reuse collections** - `List.Clear()` instead of `new List()`
4. **Struct events** - Use `struct` instead of `class` for events

---

## Real-World Test Scenarios

**Instead of synthetic stress tests, test actual gameplay:**

### Scenario 1: Large Battle Resolution
- 100-500 provinces change owner
- Test: Measure frame time during war resolution
- Target: <30ms frame time (30 FPS minimum)

### Scenario 2: Save Game Load
- Load 10k province state
- Test: Time from load start to playable
- Target: <5 seconds total load time

### Scenario 3: Map Mode Rapid Switching
- Switch between Political → Terrain → Development every second
- Test: Check for shader cache misses
- Target: <1ms per switch, no frame spikes

### Scenario 4: Zoom Performance
- Zoom in/out rapidly for 30 seconds
- Test: Check for LOD changes or texture streaming issues
- Target: Consistent 60 FPS, no stutters

---

## Recommended Testing Workflow

1. **Play normally for 10 minutes** with Unity Profiler recording
2. **Identify actual bottlenecks** from real gameplay
3. **Create targeted stress test** for specific issue
4. **Optimize based on profiler data**, not guesses
5. **Re-test real gameplay** to verify improvement
6. **Test on minimum spec hardware** (integrated GPU)

---

**Related:**
- Map/FILE_REGISTRY.md - Map layer architecture
- unity-compute-shader-coordination.md - GPU race conditions
- unity-gpu-debugging-guide.md - CommandBuffer patterns
- map-stress-test-audit.md - Critical audit findings

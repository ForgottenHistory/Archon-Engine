# Core Simulation Stress Test Plan
**Date:** 2025-10-02 (Updated after audit)
**Goal:** Validate Core simulation architecture can handle 10k provinces at 60 FPS

---

## Important Context: Foundational Testing

**What this tests:** Architecture scalability and data structure performance **before** gameplay systems exist.

**What this does NOT test:** Gameplay systems (AI, pathfinding, trade, etc.) - those will be profiled when implemented.

**Why synthetic data is OK:** We're validating the foundation scales from 4k→10k provinces linearly, not testing realistic gameplay patterns. Once gameplay systems exist, profile actual gameplay instead.

---

## Prerequisite: Scaling Test Data to 10k Provinces

**Problem:** EU4 test data only has ~4k provinces, but target is 10k provinces.

**Solution:** Synthetically clone existing provinces to reach benchmark scale.

### Step 1: Add `AddProvince()` to ProvinceSystem.cs

```csharp
// Add to Assets/Scripts/Core/Systems/ProvinceSystem.cs

/// <summary>
/// Add a synthetic province for testing (bypasses normal loading)
/// Use for stress testing only - clones existing province data
/// </summary>
public void AddProvince(ushort provinceId, ProvinceState initialState)
{
    if (!isInitialized)
    {
        ArchonLogger.LogError("ProvinceSystem not initialized");
        return;
    }

    // Check if province already exists
    if (idToIndex.ContainsKey(provinceId))
    {
        ArchonLogger.LogWarning($"Province {provinceId} already exists");
        return;
    }

    // Check capacity
    if (provinceCount >= provinceStates.Length)
    {
        ArchonLogger.LogError($"ProvinceSystem capacity exceeded ({provinceStates.Length})");
        return;
    }

    // Add to arrays
    int index = provinceCount;
    provinceStates[index] = initialState;
    idToIndex.Add(provinceId, index);
    activeProvinceIds.Add(provinceId);
    provinceCount++;
}
```

### Step 2: Scale in Stress Test `Start()`

Add this to any stress test's `Start()` method:

```csharp
void Start()
{
    // ... existing initialization ...

    // Scale to 10k provinces BEFORE starting stress test
    if (provinceSystem.ProvinceCount < 10000)
    {
        ScaleToTarget(10000);
    }

    ArchonLogger.Log($"Stress test starting with {provinceSystem.ProvinceCount} provinces");
}

/// <summary>
/// Clone existing provinces to reach target count
/// Synthetic IDs start at 20000 to avoid conflicts with real EU4 data
/// </summary>
private void ScaleToTarget(int targetCount)
{
    int originalCount = provinceSystem.ProvinceCount;
    int toAdd = targetCount - originalCount;

    ArchonLogger.Log($"Scaling from {originalCount} to {targetCount} provinces (+{toAdd})");

    ushort syntheticIdStart = 20000; // Start synthetic IDs at 20k

    for (int i = 0; i < toAdd; i++)
    {
        // Clone province (cycle through original 4k provinces)
        ushort sourceId = provinceSystem.GetProvinceIdAtIndex(i % originalCount);
        ProvinceState sourceState = provinceSystem.GetProvinceState(sourceId);

        // Create clone with new ID
        ushort newId = (ushort)(syntheticIdStart + i);
        provinceSystem.AddProvince(newId, sourceState);
    }

    ArchonLogger.Log($"✅ Scaled to {provinceSystem.ProvinceCount} provinces");
}
```

**Result:** 4k real EU4 provinces + 6k cloned provinces = 10k total for testing.

**Why this works:**
- ✅ Preserves realistic data patterns (terrain, owner distributions from EU4)
- ✅ Deterministic (same input = same scaled output)
- ✅ Simple implementation
- ✅ Synthetic provinces won't have map positions (fine for simulation testing)

---

## Test Approach

### **Option 1: Direct System Update (Quick Validation)**
Tests raw simulation throughput without command layer overhead.

```csharp
// Assets/Scripts/Tests/Manual/ProvinceStressTest.cs
using UnityEngine;
using Core;
using Core.Systems;
using Core.Data;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Stress test: Update every province every tick
    /// Tests: ProvinceSystem performance, EventBus throughput, memory stability
    /// </summary>
    public class ProvinceStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameState gameState;

        [Header("Test Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private int tickInterval = 1; // Update every N ticks
        [SerializeField] private bool logPerformance = true;

        private ProvinceSystem provinceSystem;
        private DeterministicRandom rng;
        private int tickCount = 0;
        private float totalUpdateTime = 0f;
        private int updateCount = 0;

        void Start()
        {
            if (gameState == null)
            {
                Debug.LogError("ProvinceStressTest: GameState not assigned");
                return;
            }

            provinceSystem = gameState.ProvinceSystem;
            rng = new DeterministicRandom(12345); // Fixed seed for reproducibility

            // Scale to 10k provinces for stress testing
            if (provinceSystem.ProvinceCount < 10000)
            {
                ScaleToTarget(10000);
            }

            // Subscribe to tick events
            gameState.EventBus.Subscribe<HourlyTickEvent>(OnHourlyTick);

            ArchonLogger.Log($"ProvinceStressTest initialized. Total provinces: {provinceSystem.ProvinceCount}");
        }

        /// <summary>
        /// Clone existing provinces to reach target count
        /// Synthetic IDs start at 20000 to avoid conflicts
        /// </summary>
        private void ScaleToTarget(int targetCount)
        {
            int originalCount = provinceSystem.ProvinceCount;
            int toAdd = targetCount - originalCount;

            ArchonLogger.Log($"Scaling from {originalCount} to {targetCount} provinces (+{toAdd})");

            ushort syntheticIdStart = 20000;

            for (int i = 0; i < toAdd; i++)
            {
                ushort sourceId = provinceSystem.GetProvinceIdAtIndex(i % originalCount);
                ProvinceState sourceState = provinceSystem.GetProvinceState(sourceId);

                ushort newId = (ushort)(syntheticIdStart + i);
                provinceSystem.AddProvince(newId, sourceState);
            }

            ArchonLogger.Log($"✅ Scaled to {provinceSystem.ProvinceCount} provinces");
        }

        void OnDestroy()
        {
            if (gameState != null)
            {
                gameState.EventBus.Unsubscribe<HourlyTickEvent>(OnHourlyTick);
            }
        }

        private void OnHourlyTick(HourlyTickEvent evt)
        {
            if (!enableStressTest) return;

            tickCount++;
            if (tickCount % tickInterval != 0) return;

            float startTime = Time.realtimeSinceStartup;

            // Update every province with random data
            UpdateAllProvinces();

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f; // Convert to ms
            totalUpdateTime += elapsed;
            updateCount++;

            if (logPerformance && updateCount % 100 == 0)
            {
                float avgTime = totalUpdateTime / updateCount;
                ArchonLogger.Log($"Stress Test - Avg update time: {avgTime:F2}ms, " +
                    $"Provinces: {provinceSystem.ProvinceCount}, " +
                    $"Updates: {updateCount}");
            }
        }

        /// <summary>
        /// Update every province with random data
        /// Tests: Memory access patterns, event throughput, data structure performance
        /// </summary>
        private void UpdateAllProvinces()
        {
            int provinceCount = provinceSystem.ProvinceCount;

            for (int i = 0; i < provinceCount; i++)
            {
                ushort provinceId = provinceSystem.GetProvinceIdAtIndex(i);
                ProvinceState state = provinceSystem.GetProvinceState(provinceId);

                // Random modifications (deterministic via DeterministicRandom)
                bool modifyOwner = rng.NextBool();
                bool modifyDevelopment = rng.NextBool();
                bool modifyFortLevel = rng.NextBool();

                if (modifyOwner)
                {
                    // Random owner (1-100, avoiding 0 = unowned)
                    ushort newOwner = (ushort)rng.NextInt(1, 100);
                    state.ownerID = newOwner;
                }

                if (modifyDevelopment)
                {
                    // Random development (0-255)
                    state.development = (byte)rng.NextInt(0, 255);
                }

                if (modifyFortLevel)
                {
                    // Random fort level (0-10)
                    state.fortLevel = (byte)rng.NextInt(0, 10);
                }

                // Write back to ProvinceSystem
                provinceSystem.SetProvinceState(provinceId, state);
            }
        }
    }
}
```

**Setup:**
1. Add script to scene as component
2. Assign GameState reference
3. Enable stress test in Inspector
4. Run game, watch Console for performance logs

**Expected Performance:**
- **Target:** <5ms per update (10k provinces)
- **Acceptable:** <10ms per update
- **Failure:** >20ms per update (indicates architectural issue)

---

### **Option 2: Command-Based Stress Test (Full Architecture)**
Tests command processing throughput and determinism.

```csharp
// Assets/Scripts/Tests/Manual/CommandStressTest.cs
using UnityEngine;
using Core;
using Core.Commands;
using Core.Systems;
using Core.Data;
using Utils;
using System.Collections.Generic;

namespace Tests.Manual
{
    /// <summary>
    /// Stress test: Submit 10k commands per tick
    /// Tests: CommandProcessor throughput, command validation, determinism
    /// </summary>
    public class CommandStressTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameState gameState;

        [Header("Test Configuration")]
        [SerializeField] private bool enableStressTest = false;
        [SerializeField] private int commandsPerTick = 1000; // Start with 1k

        private CommandProcessor commandProcessor;
        private ProvinceSystem provinceSystem;
        private DeterministicRandom rng;

        void Start()
        {
            provinceSystem = gameState.ProvinceSystem;
            commandProcessor = gameState.CommandProcessor;
            rng = new DeterministicRandom(54321);

            gameState.EventBus.Subscribe<HourlyTickEvent>(OnHourlyTick);
        }

        void OnDestroy()
        {
            gameState.EventBus.Unsubscribe<HourlyTickEvent>(OnHourlyTick);
        }

        private void OnHourlyTick(HourlyTickEvent evt)
        {
            if (!enableStressTest) return;

            float startTime = Time.realtimeSinceStartup;

            // Submit random commands
            for (int i = 0; i < commandsPerTick; i++)
            {
                ushort randomProvinceId = provinceSystem.GetProvinceIdAtIndex(
                    rng.NextInt(0, provinceSystem.ProvinceCount - 1)
                );

                ushort randomOwner = (ushort)rng.NextInt(1, 100);

                var cmd = new ChangeOwnerCommand(randomProvinceId, randomOwner, evt.CurrentTick);
                commandProcessor.SubmitCommand(cmd);
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"Command Stress - Submitted {commandsPerTick} commands in {elapsed:F2}ms");
        }
    }
}
```

**Performance Target:**
- **10k commands/tick:** <5ms submission + <5ms processing = <10ms total

---

### **Option 3: Burst Job Stress Test (Ultimate Performance)**
Tests parallel processing with Burst compilation.

```csharp
// Assets/Scripts/Tests/Manual/BurstStressTest.cs
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;
using Core;
using Core.Data;

namespace Tests.Manual
{
    [BurstCompile]
    struct ProvinceUpdateJob : IJobParallelFor
    {
        public NativeArray<ProvinceState> provinceStates;
        public uint randomSeed;

        public void Execute(int index)
        {
            // Per-thread deterministic random (seed + index)
            uint seed = randomSeed + (uint)index;

            // Simple xorshift for deterministic random
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;

            ProvinceState state = provinceStates[index];

            // Random modifications
            if ((seed & 1) == 1)
            {
                state.development = (byte)((seed >> 8) & 0xFF);
            }

            if ((seed & 2) == 2)
            {
                state.fortLevel = (byte)((seed >> 16) & 0x0F);
            }

            provinceStates[index] = state;
        }
    }

    public class BurstStressTest : MonoBehaviour
    {
        [SerializeField] private GameState gameState;
        [SerializeField] private bool enableStressTest = false;

        private NativeArray<ProvinceState> provinceStates;

        void Start()
        {
            // Get reference to ProvinceSystem's NativeArray
            // NOTE: Requires adding GetNativeArrayAccess() to ProvinceSystem
            provinceStates = gameState.ProvinceSystem.GetNativeArray();

            gameState.EventBus.Subscribe<HourlyTickEvent>(OnHourlyTick);
        }

        private void OnHourlyTick(HourlyTickEvent evt)
        {
            if (!enableStressTest) return;

            float startTime = Time.realtimeSinceStartup;

            var job = new ProvinceUpdateJob
            {
                provinceStates = provinceStates,
                randomSeed = (uint)evt.CurrentTick
            };

            // Schedule parallel job (Burst-compiled)
            JobHandle handle = job.Schedule(provinceStates.Length, 64);
            handle.Complete(); // Wait for completion

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log($"Burst Job - Updated {provinceStates.Length} provinces in {elapsed:F2}ms");
        }
    }
}
```

**Performance Target:**
- **10k provinces:** <1ms with Burst compilation

---

## Test Suite Overview

### Core Architecture Validation Tests

| Test | What It Proves | Target |
|------|---------------|--------|
| **1. Direct System Update** | Data structure scales linearly | <5ms for 10k provinces |
| **2. FixedPoint64 Benchmark** | Fixed-point math is viable | <2ms for 10k calculations |
| **3. EventBus Throughput** | Event system handles load | <5ms, 0 allocations |
| **4. Command Processing** | Command layer scales | <5ms submit + <5ms process |
| **5. 400-Year Simulation** | No leaks, drift, or accumulation errors | Maintains 60 FPS |
| **6. Burst Job** (Optional) | Can parallelize if needed | <1ms for 10k provinces |

**Success:** If all pass, architecture is sound → safe to build gameplay systems on top.

**Failure:** If any fail, fix fundamental issue before writing gameplay code.

---

## Test 1: Direct System Update (Data Structure Validation)

### What This Tests
- NativeArray access performance
- Hot/cold data split effectiveness
- Cache locality with 10k random accesses
- Linear scaling (4k→10k should be 2.5x, not 10x slower)

### Implementation Priority

**🟢 Start Here: Option 1 (Direct System Update)**

**Why:** Tests raw data structure performance - most fundamental validation.

**Steps:**
1. Create `Assets/Scripts/Tests/Manual/ProvinceStressTest.cs`
2. Add to scene, assign GameState
3. Enable stress test, run for 1000 ticks
4. Check logs for performance metrics

**Success Criteria:**
- Average update time <5ms (not <10ms - be strict!)
- Linear scaling: 10k provinces ≈ 2.5x slower than 4k
- No memory leaks (profile with Unity Profiler)

---

---

## Test 2: FixedPoint64 Math Benchmark (Fixed-Point Viability)

### What This Tests
- Is FixedPoint64 fast enough for economic calculations?
- Can you do 10k math operations per frame?
- Multiplication/division overhead vs float

### Implementation

```csharp
// Add to ProvinceStressTest.cs or create separate FixedPointBenchmark.cs
using UnityEngine;
using Core.Data;
using Utils;

namespace Tests.Manual
{
    public class FixedPointBenchmark : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool runBenchmark = false;
        [SerializeField] private int calculationsPerFrame = 10000;

        void Update()
        {
            if (!runBenchmark) return;

            float startTime = Time.realtimeSinceStartup;

            FixedPoint64 accumulator = FixedPoint64.Zero;

            // Simulate economic calculations (tax, modifiers, etc.)
            for (int i = 0; i < calculationsPerFrame; i++)
            {
                FixedPoint64 baseTax = FixedPoint64.FromInt(100);
                FixedPoint64 devModifier = FixedPoint64.FromFraction(15, 10);  // 1.5
                FixedPoint64 autonomyMod = FixedPoint64.FromFraction(12, 10);  // 1.2
                FixedPoint64 warMod = FixedPoint64.FromFraction(8, 10);        // 0.8

                // Chain multiplications (common pattern)
                FixedPoint64 result = baseTax * devModifier * autonomyMod * warMod;
                accumulator = accumulator + result;
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

            ArchonLogger.Log($"FixedPoint64 Benchmark - {calculationsPerFrame} calcs: {elapsed:F2}ms, " +
                $"Result: {accumulator.ToInt()}");

            runBenchmark = false; // Run once
        }
    }
}
```

**Success Criteria:**
- **Target:** <2ms for 10k calculations
- **Acceptable:** <5ms
- **Failure:** >10ms → Reconsider FixedPoint64 decision

**Why This Matters:** If FixedPoint64 is too slow, you'll hit performance walls in economic systems before optimizing algorithms.

---

## Test 3: EventBus Throughput (Event System Capacity)

### What This Tests
- Can EventBus handle monthly tick load? (4k provinces × 3 events = 12k events)
- Are there allocations? (should be zero with pooling)
- Event processing performance

### Implementation

```csharp
// Add to ProvinceStressTest.cs
using Unity.Profiling;

public class EventBusStressTest : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameState gameState;

    [Header("Configuration")]
    [SerializeField] private bool enableStressTest = false;
    [SerializeField] private int eventsPerTick = 10000;

    private EventBus eventBus;
    private static readonly ProfilerMarker s_EmitMarker = new ProfilerMarker("EventStress.Emit");
    private static readonly ProfilerMarker s_ProcessMarker = new ProfilerMarker("EventStress.Process");

    void Start()
    {
        eventBus = gameState.EventBus;
    }

    void Update()
    {
        if (!enableStressTest) return;

        long allocBefore = System.GC.GetTotalMemory(false);

        using (s_EmitMarker.Auto())
        {
            // Emit events
            for (int i = 0; i < eventsPerTick; i++)
            {
                eventBus.Emit(new ProvinceOwnershipChangedEvent
                {
                    provinceId = (ushort)(i % 10000),
                    oldOwner = 0,
                    newOwner = (ushort)(i % 100)
                });
            }
        }

        using (s_ProcessMarker.Auto())
        {
            // Process all events
            eventBus.ProcessEvents();
        }

        long allocAfter = System.GC.GetTotalMemory(false);
        long allocated = allocAfter - allocBefore;

        if (allocated > 0)
        {
            ArchonLogger.LogWarning($"❌ EventBus allocated {allocated / 1024f:F2} KB (should be 0)");
        }

        ArchonLogger.Log($"EventBus Stress - {eventsPerTick} events, Check Unity Profiler for timing");

        enableStressTest = false; // Run once
    }
}
```

**Success Criteria:**
- **Target:** <5ms for 10k events
- **Allocations:** 0 KB (mandatory - use event pooling)
- **Acceptable:** <10ms
- **Failure:** >15ms or any allocations

---

## Test 4: Command Processing (Full Architecture)

### What This Tests
- Command submission throughput
- Command validation overhead
- Determinism (same seed = same results)

**🟡 Implementation: Option 2 from original plan**

**Steps:**
1. Start with 1k commands/tick
2. Gradually increase to 10k
3. Profile CommandProcessor.ProcessTick()

**Success Criteria:**
- Command submission <5ms
- Command processing <5ms
- Deterministic (same seed = same results)

*(Keep original Command-Based Stress Test implementation)*

---

## Test 5: 400-Year Idle Simulation (Long-Term Stability)

### What This Tests
- Memory leaks over time
- Accumulation errors in FixedPoint64
- EventBus stability over millions of ticks
- Frame time degradation over time

### Implementation

```csharp
using UnityEngine;
using Core;
using Core.Systems;
using Utils;

namespace Tests.Manual
{
    /// <summary>
    /// Long-term stability test: Run 400 years of simulation
    /// Tests: Memory leaks, accumulation errors, frame time degradation
    /// </summary>
    public class LongTermSimulationTest : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameState gameState;

        [Header("Configuration")]
        [SerializeField] private bool enableTest = false;
        [SerializeField] private int targetYears = 400;
        [SerializeField] private int ticksPerYear = 8640; // 24 ticks/day × 360 days

        private TimeManager timeManager;
        private int tickCount = 0;
        private int targetTicks;
        private float startTime;
        private long startMemory;

        void Start()
        {
            if (!enableTest) return;

            timeManager = gameState.TimeManager;
            targetTicks = targetYears * ticksPerYear;
            startTime = Time.realtimeSinceStartup;
            startMemory = System.GC.GetTotalMemory(false);

            ArchonLogger.Log($"Starting {targetYears}-year simulation ({targetTicks} ticks)");

            // Subscribe to tick events
            gameState.EventBus.Subscribe<HourlyTickEvent>(OnTick);
        }

        void OnDestroy()
        {
            if (gameState != null)
            {
                gameState.EventBus.Unsubscribe<HourlyTickEvent>(OnTick);
            }
        }

        private void OnTick(HourlyTickEvent evt)
        {
            tickCount++;

            // Log progress every year
            if (tickCount % ticksPerYear == 0)
            {
                int currentYear = tickCount / ticksPerYear;
                float elapsedSeconds = Time.realtimeSinceStartup - startTime;
                long currentMemory = System.GC.GetTotalMemory(false);
                long memoryGrowth = currentMemory - startMemory;

                ArchonLogger.Log($"Year {currentYear}/{targetYears} - " +
                    $"Time: {elapsedSeconds:F1}s, " +
                    $"Memory: {memoryGrowth / 1024f / 1024f:F2} MB growth");
            }

            // Test complete
            if (tickCount >= targetTicks)
            {
                float totalTime = Time.realtimeSinceStartup - startTime;
                long finalMemory = System.GC.GetTotalMemory(false);
                long memoryGrowth = finalMemory - startMemory;

                ArchonLogger.Log($"✅ {targetYears}-year simulation complete!");
                ArchonLogger.Log($"Total time: {totalTime:F1}s ({tickCount} ticks)");
                ArchonLogger.Log($"Memory growth: {memoryGrowth / 1024f / 1024f:F2} MB");
                ArchonLogger.Log($"Avg tick time: {(totalTime * 1000f) / tickCount:F3}ms");

                // Check for issues
                if (memoryGrowth > 100 * 1024 * 1024) // >100MB growth
                {
                    ArchonLogger.LogWarning($"⚠️ Excessive memory growth detected!");
                }

                enableTest = false;
                gameState.EventBus.Unsubscribe<HourlyTickEvent>(OnTick);
            }
        }
    }
}
```

**Success Criteria:**
- **Maintains 60 FPS** throughout entire 400 years
- **Memory growth <50MB** (some growth is OK, but should be bounded)
- **No frame time degradation** (year 400 ≈ same speed as year 1)
- **No crashes or errors**

**Why 400 Years:** EU4 games run 1444-1821 (377 years). If your simulation degrades over time, you'll see it here.

---

## Test 6: Burst Job (Optional - Parallelization Validation)

### What This Tests
- Can you parallelize province processing if needed?
- Burst compilation effectiveness
- Validates data structure is Burst-compatible

**🔵 Implementation: Option 3 from original plan**

*(Keep original Burst Job implementation)*

**Success Criteria:**
- <1ms for 10k provinces with Burst
- Proves parallelization is possible for future optimization

---

### 🔵 Optional: Option 3 (Burst Jobs)
**Why:** Ultimate performance testing, requires ProvinceSystem modification.

**Requirement:**
Add to ProvinceSystem.cs:
```csharp
// Expose NativeArray for job scheduling (testing only!)
public NativeArray<ProvinceState> GetNativeArray() => provinceStates;
```

**Use Case:** If Option 1 shows performance issues, Burst jobs can diagnose cache/memory problems.

---

## Complete Success Criteria (All Must Pass)

### Performance Benchmarks

| Test | Metric | Target | Acceptable | Failure | What It Proves |
|------|--------|--------|------------|---------|----------------|
| **Direct Update** | 10k provinces | <5ms | <10ms | >20ms | Data structure scales |
| **FixedPoint64** | 10k calculations | <2ms | <5ms | >10ms | Fixed-point viable |
| **EventBus** | 10k events | <5ms | <10ms | >15ms | Event system scales |
| **Commands** | Submit 10k | <5ms | <10ms | >15ms | Command layer scales |
| **Commands** | Process 10k | <5ms | <10ms | >15ms | Command processing OK |
| **400-Year Sim** | Memory growth | <50MB | <100MB | >200MB | No leaks |
| **400-Year Sim** | Frame time | 60 FPS | 45 FPS | <30 FPS | No degradation |
| **Burst Job** | 10k provinces | <1ms | <2ms | >5ms | Can parallelize |

### Critical Requirements (Zero Tolerance)

| Requirement | How to Verify | Failure Impact |
|-------------|---------------|----------------|
| **Zero allocations** | Profiler GC.Alloc column = 0 | GC spikes kill framerate |
| **Deterministic** | Same seed = same results | Multiplayer desyncs |
| **Linear scaling** | 10k ≈ 2.5x slower than 4k | O(n²) algorithm hidden |
| **Burst compatible** | Burst job compiles and runs | Can't optimize later |

---

## What Each Result Means

### ✅ All Tests Pass
**Architecture is sound.** Safe to build gameplay systems on this foundation.

### ❌ Direct Update Slow (>20ms)
**Data structure problem.** Fix before proceeding:
- Check for O(n²) lookup (should be O(1) hash map)
- Profile memory access patterns
- Consider Structure of Arrays if cache misses

### ❌ FixedPoint64 Slow (>10ms)
**Fixed-point decision may be wrong.** Options:
- Optimize FixedPoint64 implementation
- Use Burst compilation for math-heavy code
- Last resort: Reconsider float with deterministic libraries

### ❌ EventBus Allocating
**Event pooling not working.** Fix before proceeding:
- Implement object pooling for events
- Use struct events instead of class
- Profile allocation source in Unity Profiler

### ❌ 400-Year Simulation Degrades
**Accumulation error or memory leak.** Critical issues:
- Check for unbounded collections (use ring buffers)
- Verify FixedPoint64 doesn't accumulate errors
- Profile memory growth in Unity Profiler

---

## Monitoring

**Unity Profiler - Watch These:**
- `ProvinceStressTest.UpdateAllProvinces()` - CPU time
- `EventBus.ProcessEvents()` - Event throughput
- `ProvinceSystem.SetProvinceState()` - Memory access
- GC allocations - Should be ZERO during stress test

**Console Logs:**
```
Stress Test - Avg update time: 4.23ms, Provinces: 10000, Updates: 100
Command Stress - Submitted 10000 commands in 3.12ms
Burst Job - Updated 10000 provinces in 0.87ms
```

---

## Troubleshooting

**If update time >20ms:**
1. Check for allocations (use Profiler Memory module)
2. Verify NativeArray access (no bounds checks in Release build)
3. Profile GetProvinceState/SetProvinceState calls

**If EventBus saturated:**
1. Reduce event emission (don't emit event per province)
2. Batch events (emit once per tick with changed province list)

**If memory leaks:**
1. Check DeterministicRandom disposal
2. Verify command disposal in CommandProcessor

---

## Next Steps After Testing

**If performance good (<10ms):**
- ✅ Core simulation scales to 10k provinces
- ✅ Ready for late-game stress (400+ years)

**If performance bad (>20ms):**
- Investigate with Unity Profiler
- Consider Structure of Arrays migration (batch processing)
- Add Burst compilation to ProvinceSystem methods

---

## Testing Workflow

### Phase 1: Quick Validation (Run All Tests Once)
1. **Direct Update Test** - 5 minutes
2. **FixedPoint64 Benchmark** - 1 minute
3. **EventBus Throughput** - 1 minute
4. **Command Processing** - 5 minutes

**Total:** ~15 minutes to validate core architecture

### Phase 2: Long-Term Stability (Run Overnight)
5. **400-Year Simulation** - ~30-60 minutes
6. **Burst Job Test** - 5 minutes (if Phase 1 passes)

**Total:** ~1 hour for complete validation

### Phase 3: Iterate on Failures
- If any test fails, fix the issue and re-run all tests
- Don't proceed to gameplay until all tests pass

---

## After All Tests Pass

### You've Validated:
- ✅ ProvinceSystem scales to 10k provinces
- ✅ FixedPoint64 math is fast enough
- ✅ EventBus can handle late-game load
- ✅ Command layer is performant
- ✅ No memory leaks over 400 years
- ✅ Can parallelize with Burst if needed

### Next Steps:
1. **Build gameplay systems** with confidence
2. **Profile actual gameplay** when systems exist
3. **Optimize based on real bottlenecks**, not guesses
4. **Re-run stress tests** after major architecture changes

---

**Related:**
- Core/FILE_REGISTRY.md - System documentation
- core-simulation-audit.md - Audit findings (this plan is based on)
- unity-compute-shader-coordination.md - GPU patterns (for Map layer stress tests)

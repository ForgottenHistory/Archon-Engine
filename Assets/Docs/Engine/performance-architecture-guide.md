# Grand Strategy Game Performance Architecture Guide
## Avoiding Late-Game Performance Collapse

**📊 Implementation Status:** ⚠️ Partially Implemented (Hot/cold separation ✅, some patterns ✅, advanced features pending)

> **📚 Architecture Context:** This document focuses on performance patterns. See [master-architecture-document.md](master-architecture-document.md) for the dual-layer architecture foundation.

## Executive Summary
Grand strategy games face unique performance challenges that compound over time. A game running at 200 FPS in year 1 can drop to 20 FPS by year 400, even when paused. This document explains why this happens and how to architect systems to maintain performance throughout the entire game lifecycle.

## The Late-Game Performance Problem

### Why Performance Degrades Over Time
```
Early Game (Year 1):
- 50 countries × 10 provinces each = 500 active provinces
- 0 years of history
- 100 units on map
- Simple diplomatic web
= 200 FPS

Late Game (Year 400):
- 200 countries × 50 provinces each = 10,000 active provinces
- 400 years of history per province
- 5,000 units on map
- Complex diplomatic web with 20,000 relations
= 20 FPS (even when PAUSED)
```

### The Paradox Problem: Case Study
Paradox games exhibit severe late-game slowdown because:

1. **Data accumulation without cleanup**
2. **O(n²) algorithms that weren't obvious with small n**
3. **Memory fragmentation from 400 years of allocations**
4. **Cache misses from scattered data access**
5. **UI systems that touch entire game state every frame**

## Core Architecture Principles

### Principle 1: Design for the End State
**Wrong approach**: "We'll optimize when it becomes a problem"
**Right approach**: "Architecture assumes worst-case from day one"

```csharp
// BAD: Works fine for 100 provinces, dies at 10,000
foreach (Province p in provinces) {
    foreach (Province n in p.neighbors) {
        CalculateTrade(p, n);
    }
}

// GOOD: Scales linearly
Parallel.ForEach(tradeRoutes, route => {
    CalculateTrade(route);
});
```

### Principle 2: Separate Hot, Warm, and Cold Data
Data "temperature" is determined by **access frequency**, not importance.

**Hot Data**: Read/written every frame or every tick
- Example: `ownerID` (read every frame for map rendering)
- Storage: Tightly-packed structs in `NativeArray`

**Warm Data**: Accessed occasionally (events, tooltips, calculations)
- Example: `development`, `terrain`, `fortLevel` (used in calculations but not every frame)
- Storage: Can remain in main simulation struct if space permits

**Cold Data**: Rarely accessed (history, detailed statistics, flavor text)
- Example: Historical ownership records, building details, modifier descriptions
- Storage: Separate dictionaries, loaded on-demand, can page to disk

> **See:** [master-architecture-document.md](master-architecture-document.md) and [core-data-access-guide.md](core-data-access-guide.md) for complete hot/cold data architecture.

**Key implementation:**
```csharp
// Hot + Warm data together (accessed as a unit for simulation)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {  // EXACTLY 8 bytes
    public ushort ownerID;       // HOT - read every frame for rendering
    public ushort controllerID;  // WARM - read during war calculations
    public byte development;     // WARM - read for income/tooltip
    public byte terrain;         // WARM - read for combat/modifiers
    public byte fortLevel;       // WARM - read during sieges
    public byte flags;           // WARM - various state bits
}
NativeArray<ProvinceState> provinces;  // 10,000 × 8 bytes = 80KB

// Cold data - separate storage
public class ProvinceColdData {
    public CircularBuffer<HistoricalEvent> recentHistory;
    public Dictionary<string, FixedPoint64> modifiers;
    public BuildingInventory buildings;
    public string flavorText;
}
Dictionary<int, ProvinceColdData> coldData;  // Loaded on-demand
```

**Why this works:**
- The 8-byte struct is small enough to keep everything together
- Operations typically need multiple fields (owner + development + terrain for income calculation)
- Keeping related data together is better than splitting it when you need it all at once
- Cold data separated prevents cache pollution and memory waste

### Principle 3: Fixed-Size Data Structures
Dynamic growth is the enemy of performance.

```csharp
// BAD: Grows forever
public class Province {
    List<HistoricalOwner> allPreviousOwners;  // 400 years = 400+ entries
}

// GOOD: Fixed-size ring buffer
public class Province {
    CircularBuffer<HistoricalOwner> recentOwners = new CircularBuffer<HistoricalOwner>(10);
    // Only track last 10 ownership changes
}
```

## System-Specific Optimizations

### Map Rendering System

#### Problem: Traditional Approach
```csharp
// Every frame, for every province:
foreach (Province p in provinces) {
    UpdateProvinceMesh(p);
    UpdateProvinceBorders(p);
    UpdateProvinceColor(p);
    DrawProvince(p);  // Draw call
}
// Result: 10,000 draw calls, 50ms per frame
```

#### Solution: GPU-Driven Rendering
```csharp
public class GPUMapRenderer {
    // All province data in textures
    Texture2D provinceIDs;       // R16G16 format
    Texture2D provinceOwners;     // R16 format
    Texture2D provinceColors;     // RGBA32

    // Single draw call
    void Render() {
        Graphics.DrawMeshNow(quadMesh, Matrix4x4.identity);
        // GPU shader handles everything
    }
}
// Result: 1 draw call, 0.5ms per frame
```

### Province Selection System

#### Problem: Raycast Against Thousands of Colliders
```csharp
// Physics system checking 10,000 colliders
if (Physics.Raycast(ray, out hit)) {
    Province p = hit.collider.GetComponent<Province>();
}
// Result: 5-10ms per click
```

#### Solution: Texture-Based Selection
```csharp
public int GetProvinceAtPosition(Vector2 screenPos) {
    // Convert screen to UV
    Vector2 uv = ScreenToMapUV(screenPos);

    // Single texture read
    Color32 id = provinceIDTexture.GetPixel(
        (int)(uv.x * textureWidth),
        (int)(uv.y * textureHeight)
    );

    return DecodeProvinceID(id);
}
// Result: 0.01ms per click
```

### UI and Tooltip System

#### Problem: Recalculating Everything Every Frame
```csharp
// Tooltip hovering over province
void OnHover(Province p) {
    text = $"Trade Value: {CalculateTradeValue(p)}";  // Touches 100 provinces
    text += $"Supply: {CalculateSupply(p)}";          // Touches neighbors
    text += $"Unrest: {CalculateUnrest(p)}";          // Iterates all modifiers
}
// Result: 10ms per frame while hovering
```

#### Solution: Frame-Coherent Caching
```csharp
public class TooltipCache {
    Dictionary<int, TooltipData> cache = new();
    int currentFrame;

    public TooltipData GetTooltip(int provinceID) {
        if (Time.frameCount != currentFrame) {
            cache.Clear();
            currentFrame = Time.frameCount;
        }

        if (!cache.TryGetValue(provinceID, out var data)) {
            data = CalculateTooltipData(provinceID);
            cache[provinceID] = data;
        }

        return data;
    }
}
// Result: 0.1ms for cached lookups
```

### History System

#### Problem: Unbounded Growth
```csharp
public class Province {
    List<Event> allHistoricalEvents;  // Grows forever

    void AddEvent(Event e) {
        allHistoricalEvents.Add(e);  // Year 400 = thousands of events
    }
}
```

#### Solution: Tiered History with Compression
```csharp
public class ProvinceHistory {
    // Recent history - full detail
    CircularBuffer<Event> recentEvents = new(100);

    // Medium-term - compressed
    CompressedHistory mediumTerm = new(1000);

    // Long-term - statistical summary only
    HistorySummary longTerm = new();

    void AddEvent(Event e) {
        recentEvents.Add(e);

        if (recentEvents.IsFull) {
            var oldest = recentEvents.RemoveOldest();
            mediumTerm.AddCompressed(oldest);
        }
    }
}
```

### Game State Updates

#### Problem: Updating Everything Every Tick
```csharp
void GameTick() {
    foreach (Province p in provinces) {
        UpdateDevelopment(p);
        UpdateTrade(p);
        UpdatePopulation(p);
        UpdateBuildings(p);
    }
}
// Even if nothing changed!
```

#### Solution: Dirty Flag System
```csharp
public class ProvinceUpdateSystem {
    HashSet<int> dirtyProvinces = new();

    void GameTick() {
        // Only update changed provinces
        foreach (int id in dirtyProvinces) {
            UpdateProvince(id);
        }
        dirtyProvinces.Clear();
    }

    void OnProvinceChanged(int id) {
        dirtyProvinces.Add(id);
    }
}
```

See [time-system-architecture.md](time-system-architecture.md) for layered update frequencies that work with dirty flags.

## Memory Architecture

### Memory Layout for 10,000 Provinces

```csharp
// TOTAL: ~60MB for core province data

// Hot + Warm Data - Core Simulation State (80KB)
NativeArray<ProvinceState> provinces;        // 10,000 × 8 bytes = 80KB

// Cold Data - Loaded On Demand (Variable, can page to disk)
Dictionary<int, ProvinceColdData> coldData;  // ~10-20MB typical

// GPU Textures (VRAM)
Texture2D provinceIDMap;                     // 4096 × 2048 × 4 bytes = 33MB
Texture2D provinceOwners;                    // 4096 × 2048 × 2 bytes = 16MB
Texture2D provinceColors;                    // 256 × 1 × 4 bytes = 1KB (palette)
RenderTexture borders;                       // 4096 × 2048 × 1 byte = 8MB

// History - Compressed and Capped (~10MB)
HistoryDatabase historyDB;                   // Ring buffers + compression

// Presentation-Only Data (not synchronized)
Vector2[] provincePositions;                 // 10,000 × 8 bytes = 80KB
```

### Memory Layout Philosophy: Structure Data by Access Pattern

**The Key Question:** "How is this data typically accessed?"

#### When to Keep Data Together (Array of Structures - AoS)
Use AoS when operations typically access **multiple fields together**.

```csharp
// ✅ CORRECT: Tightly-related data in struct (AoS)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;      // Used together for
    public ushort controllerID; // income calculation,
    public byte development;    // tooltip generation,
    public byte terrain;        // combat resolution, etc.
    public byte fortLevel;
    public byte flags;
}
NativeArray<ProvinceState> provinces;

// Typical usage - need multiple fields at once:
void CalculateIncome(int provinceID) {
    var state = provinces[provinceID];  // Load ONCE (8 bytes, one cache line)

    // Use owner, development, terrain together
    FixedPoint64 baseIncome = FixedPoint64.FromInt(state.development);
    FixedPoint64 terrainMod = terrainModifiers[state.terrain];
    FixedPoint64 income = baseIncome * terrainMod;

    playerIncome[state.ownerID] += income;
}
```

**Why this works:**
- 8 bytes fits in one cache line with 7 other provinces (64-byte cache line)
- Loading once gives you all related data
- Grand strategy calculations almost always need multiple fields together
- Network sync is trivial: send entire 8-byte struct

#### When to Split Data Apart (Structure of Arrays - SoA)
Use SoA when you frequently iterate **one field across all elements** in isolation.

```csharp
// ✅ WHEN SoA makes sense:
// Scenario: Rendering pass only needs owner IDs to update GPU texture
ushort[] provinceOwners;  // Separate array

void UpdateOwnerTexture() {
    for (int i = 0; i < 10000; i++) {
        ushort owner = provinceOwners[i];  // Sequential memory access
        UpdateGPUPixel(i, owner);
    }
}
```

**In practice for Dominion:** This optimization is **not needed** because:
1. GPU texture updates are infrequent (only dirty provinces, ~100-500 per tick)
2. The GPU texture write dominates the 6-byte cache "waste"
3. Code simplicity and network sync elegance outweigh marginal gains

#### The Grand Strategy Reality Check

**Most game operations look like this:**
```csharp
// Calculate province income
income = development[id] * terrainBonus[terrain[id]] * fortMod[fortLevel[id]];
playerIncome[owner[id]] += income;

// With AoS:
var p = provinces[id];  // ONE load
income = p.development * terrainBonus[p.terrain] * fortMod[p.fortLevel];
playerIncome[p.ownerID] += income;
```

**SoA version = 4 separate cache misses**
**AoS version = 1 cache line load**

**Recommendation:** Use the 8-byte `ProvinceState` struct (AoS) for core simulation. Don't split it apart unless profiling proves a specific bottleneck.

### Cache Efficiency: The Real Enemy is Pointers

The performance killer isn't whether fields are together—it's **pointers** that scatter data across memory.

```csharp
// ❌ BAD: Pointers break cache locality
public struct ProvinceState {
    public ushort ownerID;            // 2 bytes - local
    public ushort controllerID;       // 2 bytes - local
    public List<Building> buildings;  // 8 bytes pointer → heap allocation elsewhere
    public byte development;          // 1 byte - local
}
// Total: 13 bytes, but accessing buildings causes cache miss

// ✅ GOOD: Keep pointers out of hot structures
public struct ProvinceState {
    public ushort ownerID;       // 2 bytes
    public ushort controllerID;  // 2 bytes
    public byte development;     // 1 byte
    public byte terrain;         // 1 byte
    public byte fortLevel;       // 1 byte
    public byte flags;           // 1 byte
}  // Total: 8 bytes, no pointers, fits in cache line

// Buildings stored separately
Dictionary<int, BuildingInventory> buildingData;  // Cold data
```

**Key Principle:** Keep simulation state **value types only** (primitives, no references). This ensures contiguous memory layout and excellent cache performance.

## Profiling and Metrics

### Key Performance Indicators

```csharp
public class PerformanceMonitor {
    // Target metrics for 10,000 provinces
    const float TARGET_FRAME_TIME = 5.0f;  // 200 FPS
    const float MAX_SELECTION_TIME = 1.0f;  // 1ms
    const float MAX_TOOLTIP_TIME = 0.5f;    // 0.5ms
    const int MAX_DRAW_CALLS = 100;
    const int MAX_MEMORY_MB = 100;

    void ProfileFrame() {
        // Measure each system
        ProfileSystem("MapRender", () => mapRenderer.Render());
        ProfileSystem("UI", () => uiSystem.Update());
        ProfileSystem("GameLogic", () => gameLogic.Update());

        if (frameTime > TARGET_FRAME_TIME) {
            LogPerformanceWarning();
        }
    }
}
```

### Performance Budget Allocation

For 200 FPS target (5ms per frame):
```
Map Rendering:      1.0ms (20%)
Game Logic:         1.5ms (30%)
UI Updates:         1.0ms (20%)
Province Selection: 0.5ms (10%)
Tooltips:          0.5ms (10%)
Other:             0.5ms (10%)
----------------------------
Total:             5.0ms
```

## Anti-Patterns to Avoid

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| **"It Works For Now"** | O(n) operations scale poorly | Design for 10k+ from start |
| **"Invisible O(n²)"** | Hidden quadratic complexity (neighbor iterations) | Pre-compute adjacency lists |
| **"Death by Thousand Cuts"** | 230k+ allocations/frame from many small objects | Pre-allocate pools |
| **"Update Everything"** | Processing unchanged data every tick | Dirty flag systems (see [time-system-architecture.md](time-system-architecture.md)) |
| **"Premature SoA Optimization"** | Splitting data that's used together | Profile first, optimize only if needed |
| **"Float in Simulation"** | Non-deterministic across platforms | Use fixed-point math (FixedPoint64) |

## Implementation Checklist

### Phase 1: Foundation (Prevent Problems)
- [x] Design data structures for 10,000+ provinces
- [x] Separate hot/cold data (ProvinceState vs ProvinceColdData)
- [x] Implement GPU-based rendering
- [x] Use fixed-size allocations
- [ ] Profile from day one

### Phase 2: Optimization (Maximize Performance)
- [ ] Implement dirty flag systems
- [ ] Add frame-coherent caching
- [ ] Use compute shaders for parallel work
- [ ] Optimize memory layout
- [ ] Add LOD systems

### Phase 3: Scaling (Handle Growth)
- [ ] Implement history compression
- [ ] Add data pagination
- [ ] Create progressive loading
- [ ] Implement spatial partitioning
- [ ] Add performance auto-scaling

### Phase 4: Polish (Maintain Performance)
- [ ] Add performance budgets
- [ ] Implement automatic profiling
- [ ] Create performance regression tests
- [ ] Add debug visualizations
- [ ] Document performance constraints

## Testing for Scale

### Stress Test Scenarios

```csharp
public class PerformanceStressTest {
    [Test]
    public void Test_10000_Provinces_200FPS() {
        CreateProvinces(10000);
        SimulateYears(400);

        float avgFrameTime = MeasureFrameTime();
        Assert.Less(avgFrameTime, 5.0f);  // Under 5ms
    }

    [Test]
    public void Test_ProvinceSelection_Under_1ms() {
        CreateProvinces(10000);

        var selectionTime = MeasureSelectionTime();
        Assert.Less(selectionTime, 1.0f);
    }

    [Test]
    public void Test_Memory_Under_100MB() {
        CreateProvinces(10000);
        SimulateYears(400);

        var memoryUsage = Profiler.GetTotalAllocatedMemoryLong();
        Assert.Less(memoryUsage, 100 * 1024 * 1024);
    }

    [Test]
    public void Test_Deterministic_FixedPointMath() {
        var state1 = new GameState(seed: 12345);
        var state2 = new GameState(seed: 12345);

        ExecuteIdenticalCommands(state1, state2);

        Assert.AreEqual(state1.Checksum(), state2.Checksum());
    }
}
```

## Practical Optimization Decision Tree

**When considering memory layout optimizations:**

1. **Is this core simulation state?**
   - Yes → Keep in 8-byte ProvinceState if possible
   - No → Separate storage (cold data, presentation data)

2. **Do operations need multiple fields together?**
   - Yes → Keep fields together (AoS)
   - No → Consider splitting (SoA)

3. **Have you profiled and confirmed a bottleneck?**
   - No → Don't optimize yet
   - Yes → Proceed with targeted optimization

4. **Will this make the code significantly more complex?**
   - Yes → Reconsider if the gains justify the cost
   - No → Implement if profiling justifies it

**Remember:** The 8-byte ProvinceState struct is already highly optimized. Most performance work should focus on:
- GPU compute shaders for visual processing
- Dirty flag systems to minimize work
- Frame-coherent caching for expensive calculations
- Fixed-size data structures to prevent unbounded growth

Don't prematurely split data structures based on textbook advice. Profile first.

## Conclusion

Late-game performance collapse is not inevitable. By designing for the end state, separating hot and cold data, using GPU-driven rendering, and implementing proper caching strategies, you can maintain 200+ FPS throughout the entire game lifecycle.

The key is to **architect for scale from day one** rather than trying to optimize after problems appear. Every system should be designed with the question: "What happens when there are 10,000 of these?"

Remember: The difference between Paradox's 20 FPS late-game and your 200 FPS late-game is architecture, not optimization.

**Most Important Principles:**
1. **8-byte simulation state** - tight, cache-friendly, network-friendly
2. **GPU for visuals** - single draw call, compute shaders
3. **Fixed-point math** - deterministic for multiplayer
4. **Dirty flags** - only update what changed
5. **Ring buffers** - prevent unbounded growth
6. **Profile before optimizing** - don't split data structures prematurely

---

## Related Documents

- **[master-architecture-document.md](master-architecture-document.md)** - Overview of dual-layer architecture
- **[core-data-access-guide.md](core-data-access-guide.md)** - How to access hot and cold data
- **[time-system-architecture.md](time-system-architecture.md)** - Layered update frequencies and dirty flag systems

---

*Last Updated: 2025-09-30*
*For questions or updates, see master-architecture-document.md*

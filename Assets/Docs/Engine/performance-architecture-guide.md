# Grand Strategy Game Performance Architecture Guide
## Avoiding Late-Game Performance Collapse

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

### Principle 2: Separate Hot and Cold Data
**Hot Data**: Accessed every frame (owner, controller, selection state)
**Cold Data**: Accessed rarely (history, statistics, modifier details)

```csharp
// Data organized by access patterns
public struct ProvinceState {  // 8 bytes, cache-friendly
    public ushort ownerID;
    public ushort controllerID;
    public byte development;
    public byte terrain;
    public byte fortLevel;
    public byte flags;
}

public class ProvinceColdData {  // Loaded on-demand
    public List<HistoricalEvent> history;
    public Dictionary<string, float> modifiers;
    public BuildingInventory buildings;
}

// Hot data in contiguous array
NativeArray<ProvinceState> provinceHotData;  // All in L2 cache

// Cold data in separate storage
Dictionary<int, ProvinceColdData> provinceColdData;  // Paged to disk if needed
```

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

See [Time System Architecture](time-system-architecture.md) for layered update frequencies that work with dirty flags.

## Memory Architecture

### Memory Layout for 10,000 Provinces

```csharp
// TOTAL: ~50MB for core province data

// Hot Data - Accessed Every Frame (80KB)
NativeArray<ProvinceState> hotData;          // 10,000 × 8 bytes = 80KB

// Cold Data - Loaded On Demand (Variable)
Dictionary<int, ProvinceColdData> coldData;  // Paged to disk

// GPU Textures (VRAM)
Texture2D provinceIDMap;                     // 4096 × 2048 × 4 bytes = 46MB
Texture2D provinceOwners;                    // 3MB
Texture2D provinceColors;                    // 1MB

// NOTE: Hot data corresponds exactly to network-synchronized data
// See [Multiplayer Architecture](multiplayer-architecture-guide.md) for network serialization

// History - Compressed and Capped (10MB)
HistoryDatabase historyDB;                   // Ring buffers + compression
```

### Cache Optimization Strategies

#### Structure of Arrays vs Array of Structures
```csharp
// BAD: Array of Structures (cache unfriendly)
struct Province {
    int owner;
    float development;
    bool isCoastal;
    // ... 50 more fields
}
Province[] provinces;  // Each province = cache line pollution

// GOOD: Structure of Arrays (cache friendly)
struct ProvinceData {
    int[] owners;
    float[] development;
    bool[] isCoastal;
}
// Accessing all owners = sequential memory access
```

#### Minimize Cache Line Pollution
```csharp
// BAD: Mixed hot and cold data
struct Province {
    int owner;           // HOT
    List<Building> buildings;  // COLD - pointer breaks cache
    float tax;          // HOT
    string history;     // COLD - another pointer
}

// GOOD: Separate by temperature
struct ProvinceHot {
    int owner;
    float tax;
}  // Fits in one cache line

class ProvinceCold {
    List<Building> buildings;
    string history;
}  // Separate allocation
```

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
| **"Update Everything"** | Processing unchanged data every tick | Dirty flag systems (see [Time System](time-system-architecture.md)) |

Brief code examples showing problems omitted - see full patterns in codebase implementations.

## Implementation Checklist

### Phase 1: Foundation (Prevent Problems)
- [ ] Design data structures for 10,000+ provinces
- [ ] Separate hot/cold data
- [ ] Implement GPU-based rendering
- [ ] Use fixed-size allocations
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
}
```

## Conclusion

Late-game performance collapse is not inevitable. By designing for the end state, separating hot and cold data, using GPU-driven rendering, and implementing proper caching strategies, you can maintain 200+ FPS throughout the entire game lifecycle.

The key is to **architect for scale from day one** rather than trying to optimize after problems appear. Every system should be designed with the question: "What happens when there are 10,000 of these?"

Remember: The difference between Paradox's 20 FPS late-game and your 200 FPS late-game is architecture, not optimization.

---

## Related Documents

- **[Time System Architecture](time-system-architecture.md)** - Layered update frequencies and dirty flag systems
- **[Multiplayer Architecture](multiplayer-architecture-guide.md)** - Hot/cold data separation for network sync
- **[Master Architecture](master-architecture-document.md)** - Overview of dual-layer architecture
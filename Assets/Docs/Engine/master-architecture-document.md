# Grand Strategy Game Master Architecture Document
## High-Performance, Multiplayer-Ready, 10,000+ Province System

**📊 Implementation Status:** ✅ Core Implemented (ProvinceState, command system, dual-layer) | ❌ Multiplayer sections are future planning

---

## Executive Summary

This document outlines the complete architecture for a grand strategy game capable of:
- **10,000+ provinces** at **200+ FPS** (single-player) / **144+ FPS** (multiplayer)
- **<100MB** memory footprint for map system
- **<5KB/s** (lockstep) or **<10KB/s** (client-server) network bandwidth in multiplayer
- **Deterministic** simulation enabling replays, saves, and multiplayer
- **Zero** late-game performance degradation

The core innovation: **Dual-layer architecture** separating deterministic simulation (CPU) from high-performance presentation (GPU), with province data stored as textures in VRAM.

---

## Namespace Organization & File Registries

### Core Namespace (`Core.*`)
**Purpose:** Deterministic simulation layer - game state, logic, commands

**Rules:**
- ✅ Use FixedPoint64 for all math (NO float/double)
- ✅ Deterministic operations only (multiplayer-safe)
- ✅ No Unity API dependencies in hot paths
- ✅ All state changes through command pattern

**Key Systems:**
- TimeManager - Tick-based time progression
- ProvinceSystem - Province hot data (8-byte structs)
- CommandProcessor - Deterministic command execution
- EventBus - Decoupled system communication

**Status:** ✅ Multiplayer-ready (deterministic simulation)

**File Registry:** See [Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md) for complete file listing with descriptions

### Map Namespace (`Map.*`)
**Purpose:** GPU-accelerated presentation layer - textures, rendering, interaction

**Rules:**
- ✅ GPU compute shaders for visual processing (NO CPU pixel ops)
- ✅ Single draw call for base map
- ✅ Presentation only (does NOT affect simulation)
- ✅ Reads from Core layer, updates textures

**Key Systems:**
- MapTextureManager - Texture infrastructure (~60MB VRAM)
- MapRenderer - Single draw call rendering
- ProvinceSelector - Texture-based selection (<1ms)
- MapModeManager - Visual display modes

**Status:** ✅ Texture-based rendering operational

**File Registry:** See [Map/FILE_REGISTRY.md](../../Scripts/Map/FILE_REGISTRY.md) for complete file listing with descriptions

### Layer Separation
```
┌─────────────────────────────────────┐
│     Map Layer (Presentation)        │
│  - Textures, Shaders, Rendering     │
│  - GPU Compute Shaders              │
│  - User Interaction                 │
│  - Read-only from Core              │
└──────────────┬──────────────────────┘
               │ Events (One-way)
               ↓
┌─────────────────────────────────────┐
│   Core Layer (Simulation)           │
│  - Deterministic Game State         │
│  - FixedPoint64 Math                │
│  - Command Pattern                  │
│  - Multiplayer-ready                │
└─────────────────────────────────────┘
```

**Critical:** Map layer CANNOT modify Core state. All changes go through Core.Commands.

---

## Part 1: Core Architecture

### 1.1 Fundamental Design Principle

```
Traditional Approach (Fails at Scale):
GameObject per Province → Mesh Renderer → Draw Calls → 20 FPS

Our Approach (Scales to 20k+ Provinces):
Texture Data → Single Quad → GPU Shader → 200+ FPS
```

### 1.2 Dual-Layer Architecture

```csharp
// LAYER 1: Simulation (CPU, Deterministic, Networked)
public struct ProvinceState {  // 8 bytes fixed
    public ushort ownerID;      // 2 bytes
    public ushort controllerID; // 2 bytes
    public byte development;    // 1 byte
    public byte terrain;        // 1 byte
    public byte fortLevel;      // 1 byte
    public byte flags;          // 1 byte (8 boolean flags)
}

// LAYER 2: Presentation (GPU, Local, Beautiful)
public class RenderState {
    Texture2D provinceIDs;     // R16G16 - Which province is each pixel
    Texture2D provinceOwners;  // R16 - Who owns each province  
    Texture2D provinceColors;  // RGBA32 - Visual representation
    RenderTexture borders;     // Generated via compute shader
}
```

### 1.3 Data Flow Architecture

```
Input → Command → Simulation → GPU Textures → Screen
         ↓           ↓
      Network    Save/Load
         ↓           ↓
    Other Clients  Replay
```

### 1.4 Memory Architecture

```
CPU Memory (RAM):
- Simulation State: 80KB (10k provinces × 8 bytes)
- Command Buffer: 1MB (ring buffer for rollback)
- Game Logic: Variable

GPU Memory (VRAM):
- Province ID Map: 46MB (configurable, typically 4096×2048×4 bytes)
- Owner Texture: 3MB (1408×512×4 bytes)
- Color Palette: 1MB
- Border Cache: 8MB
- Total: ~60MB

Network:
- Delta Updates: 40-100 bytes per tick
- Full Sync: 80KB (rare)
```

---

## Part 2: GPU-Based Rendering System

### 2.1 Why Textures Instead of Meshes

**Performance Comparison:**
```
Mesh Approach:
- 10,000 GameObjects = 10,000 transform updates
- 100+ draw calls (even with batching)
- Vertex processing for millions of vertices
- 50ms per frame → 20 FPS

Texture Approach:
- 1 GameObject (quad)
- 1 draw call
- 4 vertices total
- 0.5ms per frame → 2000 FPS (limited by monitor)
```

### 2.2 Province Data as Textures

Provinces are stored as GPU textures for parallel processing. The fragment shader reads province IDs from the texture, looks up ownership data, and renders colors in a single pass.

See [Texture-Based Map Guide](texture-based-map-guide.md) for shader implementation details.

### 2.3 Border Generation via Compute Shader

Border generation uses GPU compute shaders to detect province boundaries in parallel, processing all 10,000+ provinces in ~2ms.

See [Texture-Based Map Guide](texture-based-map-guide.md) for compute shader implementation.

### 2.4 Province Selection Without Raycasting

```csharp
public int GetProvinceAtMouse() {
    // Convert mouse to UV
    Vector2 uv = ScreenToMapUV(Input.mousePosition);
    
    // Single texture read (0.01ms)
    Color32 idColor = provinceIDTexture.GetPixel(
        (int)(uv.x * textureWidth),
        (int)(uv.y * textureHeight)
    );
    
    return idColor.r + (idColor.g << 8);
}
// 1000x faster than physics raycasting
```

---

## Part 3: Multiplayer-Ready Architecture

### 3.1 Command Pattern for Determinism

```csharp
public interface ICommand {
    void Execute(GameState state);
    byte[] Serialize();
    bool Validate(GameState state);
}

public class ChangeOwnerCommand : ICommand {
    ushort provinceID;
    ushort newOwnerID;
    
    public void Execute(GameState state) {
        state.provinces[provinceID].ownerID = newOwnerID;
        state.dirty.Add(provinceID);  // Mark for GPU update
    }
    
    public byte[] Serialize() {
        // 5 bytes total
        return new byte[] {
            CommandType.ChangeOwner,
            (byte)(provinceID & 0xFF),
            (byte)(provinceID >> 8),
            (byte)(newOwnerID & 0xFF),
            (byte)(newOwnerID >> 8)
        };
    }
}
```

### 3.2 Fixed-Point Deterministic Math

```csharp
// NEVER use float for gameplay calculations
public struct FixedPoint64 {
    long rawValue;  // Fixed-point representation
    
    public static FixedPoint64 operator *(FixedPoint64 a, FixedPoint64 b) {
        return new FixedPoint64 { 
            rawValue = (a.rawValue * b.rawValue) >> 32 
        };
    }
}

// Usage
FixedPoint64 tax = baseTax * provinceDevelopment;  // Deterministic
// float tax = baseTax * provinceDevelopment;      // NON-deterministic!
```

### 3.3 Network Synchronization

```csharp
public class NetworkSync {
    // Only sync changes (delta compression)
    public byte[] CreateDeltaPacket(int tick) {
        var changes = new List<ProvinceDelta>();
        
        for (int i = 0; i < provinces.Length; i++) {
            if (provinces[i] != lastSentProvinces[i]) {
                changes.Add(new ProvinceDelta {
                    id = (ushort)i,
                    newState = provinces[i]
                });
            }
        }
        
        // Typical: 10 changes × 8 bytes = 80 bytes
        return SerializeDeltas(changes);
    }
}
```

### 3.4 Client Prediction & Rollback

```csharp
public class PredictiveClient {
    RingBuffer<GameState> stateHistory = new(30);  // 0.5 seconds
    
    public void OnLocalCommand(ICommand cmd) {
        // 1. Execute immediately (instant feedback)
        cmd.Execute(localState);
        UpdateGPUTextures(localState);
        
        // 2. Send to server
        network.SendCommand(cmd);
        
        // 3. Store for rollback
        unconfirmedCommands.Add(cmd);
    }
    
    public void OnServerState(GameState authoritative, int tick) {
        if (HasConflict(authoritative, tick)) {
            // Rollback to authoritative state
            localState = authoritative;
            
            // Replay unconfirmed commands
            foreach (var cmd in unconfirmedCommands) {
                cmd.Execute(localState);
            }
            
            // Update visuals once
            UpdateGPUTextures(localState);
        }
    }
}
```

---

## Part 4: Performance Optimization Strategies

### 4.1 Hot/Cold Data Separation

```csharp
// HOT: Accessed every frame (keep in cache)
public struct ProvinceState {  // 8 bytes, cache-line aligned
    public ushort ownerID;
    public ushort controllerID;
    public byte development;
    public byte terrain;
    public byte fortLevel;
    public byte flags;
}
NativeArray<ProvinceState> hotData;  // Contiguous memory

// COLD: Accessed rarely (can page to disk)
public class ProvinceCold {
    public List<Building> buildings;
    public string history;
    public Dictionary<string, float> modifiers;
}
Dictionary<int, ProvinceCold> coldData;  // Loaded on-demand
```

### 4.2 Frame-Coherent Caching

```csharp
public class FrameCache {
    Dictionary<int, object> cache = new();
    int lastFrame = -1;
    
    public T Get<T>(int key, Func<T> compute) {
        if (Time.frameCount != lastFrame) {
            cache.Clear();
            lastFrame = Time.frameCount;
        }
        
        if (!cache.TryGetValue(key, out var value)) {
            value = compute();
            cache[key] = value;
        }
        
        return (T)value;
    }
}

// Usage: Expensive calculations cached per frame
var tradeValue = frameCache.Get(provinceId, 
    () => CalculateTradeValue(provinceId));
```

### 4.3 Dirty Flag System

```csharp
public class DirtyFlagSystem {
    HashSet<int> dirtyProvinces = new();
    
    public void MarkDirty(int provinceID) {
        dirtyProvinces.Add(provinceID);
    }
    
    public void UpdateGPU() {
        if (dirtyProvinces.Count == 0) return;
        
        // Only update changed provinces
        foreach (int id in dirtyProvinces) {
            UpdateProvinceTexture(id, provinces[id]);
        }
        
        provinceTexture.Apply();  // Single GPU upload
        dirtyProvinces.Clear();
    }
}
```

### 4.4 Spatial Partitioning for Culling

```csharp
public class SpatialGrid {
    const int GRID_SIZE = 32;
    List<int>[,] grid = new List<int>[GRID_SIZE, GRID_SIZE];
    
    public List<int> GetVisibleProvinces(Frustum frustum) {
        var visible = new List<int>();
        var bounds = frustum.GetGridBounds();
        
        // Only check grid cells in frustum
        for (int y = bounds.yMin; y < bounds.yMax; y++) {
            for (int x = bounds.xMin; x < bounds.xMax; x++) {
                visible.AddRange(grid[x, y]);
            }
        }
        
        return visible;  // O(visible) not O(all)
    }
}
```

---

## Part 5: Avoiding Late-Game Performance Collapse

### 5.1 Fixed-Size Data Structures

```csharp
// BAD: Unbounded growth
public class Province {
    List<HistoricalEvent> allEvents;  // Grows forever
}

// GOOD: Ring buffer with fixed size
public class Province {
    RingBuffer<HistoricalEvent> recentEvents = new(100);
    CompressedHistory olderEvents;  // Compressed after 100 events
}
```

### 5.2 Progressive History Compression

```csharp
public class HistorySystem {
    // Recent: Full detail (last 10 years)
    RingBuffer<DetailedEvent> recent = new(10 * 365);
    
    // Medium: Compressed (10-50 years ago)
    CompressedEventList medium = new();
    
    // Ancient: Statistical only (50+ years)
    HistoryStatistics ancient = new();
    
    public void AddEvent(DetailedEvent e) {
        if (recent.IsFull) {
            var old = recent.PopOldest();
            medium.AddCompressed(old);
            
            if (medium.Age > 50_YEARS) {
                ancient.AddStatistics(medium.PopOldest());
            }
        }
        recent.Add(e);
    }
}
```

### 5.3 LOD System for Province Updates

```csharp
public class UpdateLOD {
    public enum Priority {
        Critical = 1,   // Every tick (player provinces, combat)
        High = 5,       // Every 5 ticks (nearby, visible)
        Medium = 20,    // Every 20 ticks (fog of war)
        Low = 100       // Every 100 ticks (other continents)
    }
    
    public Priority GetPriority(int provinceID) {
        if (IsPlayerProvince(provinceID)) return Priority.Critical;
        if (IsVisible(provinceID)) return Priority.High;
        if (IsKnown(provinceID)) return Priority.Medium;
        return Priority.Low;
    }
}
```

---

## Part 6: Implementation Overview

See [Texture-Based Map Guide](texture-based-map-guide.md) for detailed implementation phases and step-by-step instructions.

---

## Part 7: Performance Targets & Validation

### 7.1 Performance Budget (5ms per frame = 200 FPS)

```
Map Rendering:        1.0ms (20%)
Province Selection:   0.1ms (2%)
Simulation Update:    1.5ms (30%)
Command Processing:   0.5ms (10%)
UI Updates:          1.0ms (20%)
Network (if MP):     0.4ms (8%)
Reserve:             0.5ms (10%)
------------------------
Total:               5.0ms
```

### 7.2 Validation Tests

Critical tests include:
- 10,000 provinces maintaining 200 FPS over 400 years
- Province selection under 1ms response time
- Memory usage under 100MB
- Deterministic simulation across platforms

See [Performance Architecture](performance-architecture-guide.md) for testing strategies.

---

## Part 8: Critical Success Factors

### 8.1 Must-Have Requirements
- ✅ Single draw call for base map
- ✅ Province data in GPU textures
- ✅ Deterministic simulation
- ✅ Fixed-size data structures
- ✅ Hot/cold data separation
- ✅ Command pattern for all changes

### 8.2 Must-Avoid Anti-Patterns
- ❌ GameObject per province
- ❌ Mesh-based borders
- ❌ Floating-point in simulation
- ❌ Unbounded data growth
- ❌ Synchronous GPU readbacks
- ❌ Update-everything-every-frame

### 8.3 Key Innovations
1. **Texture-based province storage** - GPU processes all provinces in parallel
2. **Dual-layer architecture** - Simulation separate from presentation
3. **Compute shader borders** - Generate borders in 2ms on GPU
4. **Fixed-point determinism** - Perfect synchronization across clients
5. **Ring buffer history** - Bounded memory growth over time

---

## Appendix A: Texture Format Reference

```csharp
// Province ID Map (full resolution)
TextureFormat.R16G16  // 0-65535 per channel, perfect for IDs

// Province Ownership (quarter resolution fine)
TextureFormat.R16     // 0-65535, country IDs

// Province Development
TextureFormat.R8      // 0-255, sufficient range

// Country Colors
TextureFormat.RGBA32  // Full color palette

// Borders
TextureFormat.R8      // Binary on/off sufficient
```

## Appendix B: Network Packet Formats

```
Command Packet (5-20 bytes):
[Type:1][ProvinceID:2][Data:2-17]

Delta Update (variable):
[Count:1][ProvinceID:2][Changes:1][NewData:4]...

Full Sync (80KB):
[Tick:4][Checksum:4][ProvinceData:80000]
```

## Appendix C: Shader Snippets

```hlsl
// Province ID Decoding
float2 idData = tex2D(_ProvinceIDs, uv).rg;
int provinceID = (int)(idData.r * 255.0) + 
                 ((int)(idData.g * 255.0) << 8);

// Owner Color Lookup
float ownerID = tex2D(_ProvinceOwners, provinceUV).r;
float4 color = tex2D(_CountryColors, float2(ownerID, 0));

// Border Detection
bool isBorder = any(GetNeighborIDs(uv) != centerID);
```

---

## Conclusion

This architecture delivers Paradox-scale gameplay at 10x the performance through:
1. **GPU-driven rendering** using textures instead of meshes
2. **Deterministic simulation** enabling multiplayer and replays
3. **Aggressive optimization** preventing late-game slowdown
4. **Clean separation** of simulation and presentation

The system scales to 20,000+ provinces while maintaining 144+ FPS in multiplayer and 200+ FPS in single-player, using less than 100MB of memory and 5-10KB/s of network bandwidth.

Follow the implementation roadmap, avoid the anti-patterns, and you'll have a grand strategy engine that performs better than anything currently on the market.

---

## Related Documents

### File Registries (Quick Reference)
- **[Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md)** - Complete listing of Core simulation layer files
- **[Map/FILE_REGISTRY.md](../../Scripts/Map/FILE_REGISTRY.md)** - Complete listing of Map presentation layer files

### Architecture Documents
- **[Map System Architecture](map-system-architecture.md)** - Complete map rendering system (texture-based, coordinates, map modes)
- **[Performance Architecture Guide](performance-architecture-guide.md)** - Late-game optimization and memory strategies
- **[Time System Architecture](time-system-architecture.md)** - Update scheduling and dirty flag systems
- **[Data Flow Architecture](data-flow-architecture.md)** - Hot/cold data separation and data linking
- **[Data Linking Architecture](data-linking-architecture.md)** - Reference resolution and cross-linking

### Planning Documents (Future)
- **[Save/Load Design](../Planning/save-load-design.md)** - Persistence and replay systems *(Planning - not implemented)*
- **[Multiplayer Design](../Planning/multiplayer-design.md)** - Network synchronization and determinism *(Planning - not implemented)*
- **[Error Recovery Design](../Planning/error-recovery-design.md)** - Robustness and fault tolerance *(Planning - not implemented)*
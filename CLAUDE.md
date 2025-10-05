# Archon - Claude Development Guide

## Project Overview
Archon is a grand strategy game capturing ancient political realities - where every decision creates winners and losers among your subjects. Success comes from understanding internal dynamics, not just optimizing abstract numbers.

**CRITICAL**: Built on **dual-layer architecture** with deterministic simulation (CPU) + high-performance presentation (GPU). This enables 10,000+ provinces at 200+ FPS with multiplayer compatibility.

You, Claude, cannot run tests. I run them manually.

## CODEBASE NAVIGATION

### File Registries - READ THESE FIRST!
Before implementing or modifying code, **always check the file registries** to understand what exists and where things belong:

- **[Assets/Scripts/Core/FILE_REGISTRY.md](Scripts/Core/FILE_REGISTRY.md)** - Complete Core layer catalog (56 files)
  - All simulation systems, data structures, commands, queries, loaders
  - Tags: `[MULTIPLAYER_CRITICAL]`, `[HOT_PATH]`, `[STABLE]`
  - Quick reference: "Need to X? → Use Y"

- **[Assets/Scripts/Map/FILE_REGISTRY.md](Scripts/Map/FILE_REGISTRY.md)** - Complete Map layer catalog (44 files)
  - All rendering, textures, interaction, map modes
  - Tags: `[GPU]`, `[HOT_PATH]`, `[LEGACY]`
  - GPU vs CPU operation guidelines

### Master Architecture Document
- **[Assets/Docs/Engine/master-architecture-document.md](Docs/Engine/master-architecture-document.md)**
  - Entry point for all architecture documentation
  - Links to all specialized architecture docs
  - Namespace organization and layer separation rules

### Before Writing Code - Navigation Workflow:
1. **Check FILE_REGISTRY.md** - Does this file/system already exist?
2. **Read relevant architecture docs** - What are the rules for this area?
3. **Check session logs** - Was this recently changed? (Docs/Log/)
4. **Implement** - Follow architecture patterns from registries

### Common Use Cases:
- **"Where do I add province logic?"** → Check Core/FILE_REGISTRY.md → See ProvinceSystem.cs
- **"How do I update map visuals?"** → Check Map/FILE_REGISTRY.md → See TextureUpdateBridge.cs
- **"Need deterministic random?"** → Check Core/FILE_REGISTRY.md → See DeterministicRandom.cs
- **"Need to change province state?"** → Check Core/FILE_REGISTRY.md → See Commands/ pattern

**CRITICAL**: File registries prevent:
- ❌ Reimplementing existing systems
- ❌ Creating files in wrong locations
- ❌ Breaking established patterns
- ❌ Missing critical dependencies

## CORE ARCHITECTURE: DUAL-LAYER SYSTEM

### **Layer 1: Simulation (CPU)**
```csharp
// EXACTLY 8 bytes - never change this size
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;      // 2 bytes
    public ushort controllerID; // 2 bytes
    public byte development;    // 1 byte
    public byte terrain;        // 1 byte
    public byte fortLevel;      // 1 byte
    public byte flags;          // 1 byte
}

// 10,000 provinces × 8 bytes = 80KB total simulation state
```

### **Layer 2: Presentation (GPU)**
```csharp
// GPU textures for rendering
Texture2D provinceIDTexture;    // 46MB - which pixel = which province
Texture2D provinceOwnerTexture; // 3MB - who owns each province
Texture2D provinceColors;       // 1MB - visual colors
RenderTexture borderTexture;    // 8MB - generated via compute shader
```

## ARCHITECTURE ENFORCEMENT

### **NEVER DO THESE:**
- ❌ **Process millions of pixels on CPU** - use GPU compute shaders always
- ❌ **Dynamic collections in simulation** - fixed-size structs only
- ❌ **GameObjects for provinces** - textures only
- ❌ **Allocate during gameplay** - pre-allocate everything
- ❌ **Store history in hot path** - use cold data separation
- ❌ **Texture filtering on province IDs** - point filtering only
- ❌ **CPU neighbor detection** - GPU compute shaders for borders
- ❌ **Floating-point in simulation** - use fixed-point math for determinism
- ❌ **Data duplication** - single source of truth (ProvinceSystem uses 8-byte AoS)
- ❌ **Unbounded data growth** - ring buffers with compression for history
- ❌ **Update-everything-every-frame** - dirty flag systems only
- ❌ **Mixed hot/cold data** - separate by access patterns
- ❌ **Shader branching/divergence** - use uniform operations
- ❌ **Built-in Render Pipeline** - URP only for future-proofing

### **ALWAYS DO THESE:**
- ✅ **8-byte fixed structs** for simulation state
- ✅ **GPU compute shaders** for all visual processing
- ✅ **Single draw call** for entire map
- ✅ **Deterministic operations** for multiplayer
- ✅ **Hot/cold data separation** for performance
- ✅ **Command pattern** for state changes
- ✅ **Fixed-point math** for all gameplay calculations
- ✅ **NativeArray** for contiguous memory layout
- ✅ **Point filtering** on all province textures
- ✅ **Frame-coherent caching** for expensive calculations
- ✅ **Dirty flag systems** to minimize updates
- ✅ **Ring buffers** for bounded history storage
- ✅ **Validate architecture compliance** before implementing
- ✅ **Profile at target scale** (10k provinces) from day one

## PERFORMANCE TARGETS (NON-NEGOTIABLE)

### Memory Usage
- **Simulation**: 80KB (10k provinces × 8 bytes)
- **GPU Textures**: <60MB total
- **System Total**: <100MB

### Performance
- **Single-Player**: 200+ FPS with 10,000 provinces
- **Multiplayer**: 144+ FPS with network sync
- **Province Selection**: <1ms response time
- **Border Generation**: <1ms via compute shader

### Network (Multiplayer)
- **Bandwidth**: <5KB/s per client
- **State Sync**: 80KB for full game state
- **Command Size**: 8-16 bytes typical

## CODE STANDARDS

### Performance Requirements
- **Burst compilation** for all hot paths
- **Zero allocations** during gameplay
- **Fixed-size data structures** only
- **SIMD optimization** where possible
- **Native Collections** over managed collections

### File Organization
- **Single responsibility** per file
- **Under 500 lines** per file
- **Focused, modular** design
- **Clear separation** of concerns

### Unity Configuration
- **URP** (Universal Render Pipeline) - NOT Built-in Pipeline
- **IL2CPP** scripting backend
- **Linear color space**
- **Burst Compiler** enabled
- **Job System** for parallelism
- **Render Graph** (Unity 2023.3+) for auto-optimization
- **Forward+ Rendering** for multiple light sources

## CRITICAL DATA FLOW

```
Input → Command → Simulation State → GPU Textures → Render
         ↓           ↓                    ↓
      Network    Save/Load           Visual Effects
```

### Simulation → GPU Pipeline
```csharp
// 1. Update simulation (deterministic)
provinceStates[id] = newState; // 8 bytes

// 2. Update GPU texture (presentation)
ownerTexture.SetPixel(x, y, newOwnerColor);

// 3. GPU compute shader processes borders
borderComputeShader.Dispatch(threadGroups);

// 4. Single draw call renders everything
Graphics.DrawMesh(mapQuad, mapMaterial);
```

## TEXTURE-BASED MAP SYSTEM

### Core Concept
```
Traditional: Province → GameObject → Mesh → Draw Call (10k draw calls)
Our System:  Province → Texture Pixel → Shader → Single Draw Call
```

### Texture Formats
- **Province IDs**: R16G16 (point filtering, no mipmaps)
- **Province Owners**: R16 (updated from simulation)
- **Colors**: RGBA32 palette texture
- **Borders**: R8 (generated by compute shader)

### Compute Shader Pattern
```hlsl
[numthreads(8,8,1)]
void BorderDetection(uint3 id : SV_DispatchThreadID) {
    uint currentProvince = DecodeProvinceID(ProvinceIDTexture[id.xy]);
    uint rightProvince = DecodeProvinceID(ProvinceIDTexture[id.xy + int2(1,0)]);

    bool isBorder = currentProvince != rightProvince;
    BorderTexture[id.xy] = isBorder ? 1.0 : 0.0;
}
```

## MULTIPLAYER ARCHITECTURE

### Deterministic Math Requirements
```csharp
// NEVER use float for gameplay calculations - non-deterministic across platforms
// ALWAYS use fixed-point math for deterministic results

public struct FixedPoint64 {
    long rawValue;  // Fixed-point representation

    public static FixedPoint64 operator *(FixedPoint64 a, FixedPoint64 b) {
        return new FixedPoint64 {
            rawValue = (a.rawValue * b.rawValue) >> 32
        };
    }
}

// Usage examples:
FixedPoint64 tax = baseTax * provinceDevelopment;  // Deterministic
// float tax = baseTax * provinceDevelopment;      // NON-deterministic!

// All simulation must use:
int32, uint32, ushort, byte - for exact integers
FixedPoint64 - for fractional calculations
NEVER: float, double, decimal
```

### Deterministic Simulation
```csharp
// All clients run identical simulation
public struct ProvinceCommand {
    public uint tick;           // When to execute
    public ushort provinceID;   // Which province
    public ushort newOwnerID;   // New state
    public uint checksum;       // Validation
}
```

### Network Optimization
- **Delta compression** - only send changes (typical: 40-100 bytes/tick)
- **Command batching** - multiple commands per packet
- **Priority system** - important changes first
- **Rollback support** - for lag compensation
- **Client prediction** - immediate local updates with server correction
- **Ring buffer history** - 30 frames (0.5 seconds) for rollback
- **Fixed-size packets** - avoid dynamic allocations
- **Bit packing** - multiple boolean flags in single bytes

### Network Architecture Patterns
```csharp
// Pattern 1: Delta Updates - Only send what changed
public struct ProvinceDelta {
    public ushort provinceID;
    public byte fieldMask;    // Which fields changed (8 bits = 8 fields)
    public ulong newValue;    // Packed new values
}
// Typical update: 10 changes × 4 bytes = 40 bytes vs 80KB full state

// Pattern 2: Rollback Buffer
RingBuffer<GameState> stateHistory = new(30);  // 0.5 seconds at 60fps

// Pattern 3: Client Prediction with Correction
void OnLocalCommand(ICommand cmd) {
    cmd.Execute(localState);           // Immediate visual feedback
    UpdateGPUTextures(localState);     // Update presentation layer
    network.SendCommand(cmd);          // Send to server
    unconfirmedCommands.Add(cmd);      // Store for rollback
}
```

## PERFORMANCE OPTIMIZATION PATTERNS

### Memory Architecture for Scale
```csharp
// Hot Data: Accessed every frame (fits in L2 cache)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceHotData {  // 8 bytes exactly
    public ushort ownerID;
    public ushort controllerID;
    public ushort developmentLevel;
    public ushort flags;
}
NativeArray<ProvinceHotData> hotData;  // Contiguous, cache-friendly

// Cold Data: Accessed rarely (can page to disk)
public class ProvinceColdData {
    public CircularBuffer<HistoricalEvent> recentHistory = new(100);
    public Dictionary<string, float> modifiers;
    public BuildingInventory buildings;
}
Dictionary<int, ProvinceColdData> coldData;  // Loaded on-demand
```

### Critical Performance Patterns
```csharp
// Pattern 1: Dirty Flag System
HashSet<int> dirtyProvinces = new();
void GameTick() {
    // Only update what changed
    foreach (int id in dirtyProvinces) {
        UpdateProvince(id);
    }
    dirtyProvinces.Clear();
}

// Pattern 2: Frame-Coherent Caching
Dictionary<int, TooltipData> frameCache = new();
int lastFrame = -1;
public TooltipData GetTooltip(int provinceID) {
    if (Time.frameCount != lastFrame) {
        frameCache.Clear();
        lastFrame = Time.frameCount;
    }
    // ... cache lookup logic
}

// Pattern 3: Data Layout (Cache-Friendly)
// ProvinceSystem uses Array of Structures (AoS) - 8-byte ProvinceState
// This is optimal for grand strategy where queries access multiple fields together
NativeArray<ProvinceState> provinceStates;  // owner + development + terrain accessed together

// Use Structure of Arrays (SoA) when accessing single fields frequently
// Example: CountrySystem uses SoA for country colors (accessed separately)
NativeArray<ushort> countryOwners;
NativeArray<Color32> countryColors;
```

### Late-Game Performance Prevention
```csharp
// Fixed-size data structures prevent unbounded growth
public class ProvinceHistory {
    CircularBuffer<Event> recent = new(100);        // Last 100 events
    CompressedHistory medium = new(1000);           // Compressed older events
    HistorySummary ancient = new();                 // Statistical summary only
}

// LOD System for updates
enum UpdatePriority {
    Critical = 1,   // Every tick (player provinces)
    High = 5,       // Every 5 ticks (visible provinces)
    Medium = 20,    // Every 20 ticks (known provinces)
    Low = 100       // Every 100 ticks (fog of war)
}
```

### Shader Programming Requirements
```hlsl
// CRITICAL: Point filtering for province IDs (no interpolation!)
sampler2D _ProvinceIDs : register(s0) { Filter = POINT; };

// Avoid divergent branching - use uniform operations
float selected = (provinceID == selectedProvince) ? 1.0 : 0.0;
color = lerp(normalColor, selectedColor, selected);

// Thread group optimization for compute shaders
[numthreads(8,8,1)]  // 64 threads per group - optimal for most GPUs
void BorderDetection(uint3 id : SV_DispatchThreadID) {
    // Process 8x8 pixel block in parallel
}
```

## DEVELOPMENT WORKFLOW

### Before Writing Code:
1. **Check architecture compliance** - does this fit dual-layer?
2. **Verify performance impact** - will this scale to 10k provinces?
3. **Consider multiplayer** - is this deterministic?
4. **Plan memory usage** - fixed-size or dynamic?

### Code Quality Checklist:
- [ ] Uses 8-byte fixed structs for simulation
- [ ] GPU operations for visual processing
- [ ] Deterministic for multiplayer (fixed-point math only)
- [ ] No allocations during gameplay
- [ ] Burst compilation compatible
- [ ] Under 500 lines per file
- [ ] Hot/cold data properly separated
- [ ] Structure of Arrays memory layout
- [ ] Point filtering on province textures
- [ ] Dirty flag systems for updates
- [ ] Fixed-size data structures only

## TESTING STRATEGY

### Critical Validation Tests
```csharp
[Test]
public void Validate_10000_Provinces_200FPS() {
    CreateProvinces(10000);
    SimulateYears(400);  // Late-game conditions
    Assert.Less(AverageFrameTime, 5.0f);  // Under 5ms
}

[Test]
public void Validate_ProvinceSelection_Under_1ms() {
    CreateProvinces(10000);
    var selectionTime = MeasureSelectionTime();
    Assert.Less(selectionTime, 1.0f);
}

[Test]
public void Validate_Memory_Under_100MB() {
    CreateProvinces(10000);
    SimulateYears(400);
    var memory = Profiler.GetTotalAllocatedMemoryLong();
    Assert.Less(memory, 100_000_000);
}

[Test]
public void Validate_Determinism_Across_Platforms() {
    var state1 = new GameState(seed: 12345);
    var state2 = new GameState(seed: 12345);
    ExecuteCommands(state1, testCommands);
    ExecuteCommands(state2, testCommands);
    Assert.AreEqual(state1.Hash(), state2.Hash());
}
```

### Performance Budget Validation (5ms per frame)
```
Map Rendering:        1.0ms (20%)
Game Logic:          1.5ms (30%)
UI Updates:          1.0ms (20%)
Province Selection:   0.5ms (10%)
Network (if MP):     0.4ms (8%)
Reserve:             0.6ms (12%)
```

### Unit Tests Focus
- **Simulation determinism** - identical results across platforms/runs
- **Fixed-point math** - no float operations in simulation
- **8-byte struct validation** - ProvinceState never changes size
- **Command validation** - reject invalid/malformed commands
- **Memory bounds** - never exceed targets at any scale
- **Serialization integrity** - perfect round-trip for network

### Performance Tests
- **Scale testing** - 1k, 5k, 10k, 20k provinces
- **Late-game testing** - 400+ years without degradation
- **Frame time consistency** - no spikes or stutters
- **Memory stability** - zero allocations during gameplay
- **GPU efficiency** - compute shader occupancy optimization
- **Cache performance** - hot data access patterns

### Integration Tests
- **CPU→GPU pipeline** - simulation changes update textures correctly
- **Texture→Selection** - mouse clicks resolve to correct provinces
- **Command→Network** - state changes serialize/deserialize correctly
- **Rollback system** - client prediction and server correction
- **Border generation** - compute shader produces pixel-perfect borders
- **Multi-scale validation** - system works from 100 to 20,000 provinces

## KEY REMINDERS

1. **Check FILE_REGISTRY.md FIRST** - Don't reimplement existing systems, know what exists and where it belongs
2. **Always ask about architecture compliance** before implementing
3. **Never suggest CPU processing** of millions of pixels - GPU compute shaders only
4. **Always consider multiplayer implications** - deterministic fixed-point math required
5. **Enforce the 8-byte struct limit** for ProvinceState - critical for performance
6. **GPU compute shaders** are the solution for all visual processing
7. **Fixed-size data structures** prevent late-game performance collapse
8. **Single draw call rendering** is mandatory - texture-based approach only
9. **Hot/cold data separation** is required - never mix access patterns
10. **Structure of Arrays** over Array of Structures for cache efficiency (UPDATE: ProvinceSystem uses AoS now)
11. **Point filtering** on province textures - no interpolation allowed
12. **URP only** - Built-in Pipeline is deprecated and not allowed
13. **Profile at target scale** from day one - 10k provinces minimum
14. **Ring buffers for history** - prevent unbounded memory growth
15. **Dirty flags for updates** - never update everything every frame
16. **Look things up before implementing** - FILE_REGISTRY.md and session logs tell you what changed recently

## CRITICAL SUCCESS FACTORS

### Must-Have Requirements (Non-Negotiable)
- ✅ **Dual-layer architecture** - CPU simulation + GPU presentation
- ✅ **8-byte ProvinceState struct** - exactly, never larger
- ✅ **Fixed-point math only** - no floats in simulation layer
- ✅ **Single draw call map** - texture-based rendering
- ✅ **GPU compute shaders** - for borders, effects, selection
- ✅ **Deterministic simulation** - identical across all clients
- ✅ **<100MB memory target** - strict enforcement required

### Must-Avoid Anti-Patterns (Project Killers)
- ❌ **GameObject per province** - guaranteed performance failure
- ❌ **CPU pixel processing** - will not scale past 1000 provinces
- ❌ **Float operations in simulation** - breaks multiplayer determinism
- ❌ **Unbounded data growth** - causes late-game collapse
- ❌ **Mixed hot/cold data** - destroys cache performance
- ❌ **Built-in Render Pipeline** - deprecated, no future support

The success of this project depends on strict adherence to these architectural principles. Every code change must support both 10,000+ province performance AND multiplayer determinism. Compromising on architecture will result in project failure.
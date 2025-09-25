# Texture-Based Map System Implementation Guide (URP)
## Dual-Layer Architecture: CPU Simulation + GPU Presentation

## System Overview
Build a texture-based map renderer following the **dual-layer architecture** from the master architecture document. This system separates deterministic simulation (CPU) from high-performance presentation (GPU), enabling 10,000+ provinces at 200+ FPS while maintaining multiplayer compatibility.

**Key Principle**: Never process millions of pixels on CPU. The GPU handles all visual processing through shaders and compute shaders.

## Architecture Summary

```
Layer 1 (CPU): Simulation State
- 8 bytes per province × 10,000 = 80KB
- Fixed-size, deterministic, networkable
- Hot data only (owner, controller, development, flags)

Layer 2 (GPU): Presentation State
- Province ID texture (46MB) - which pixel belongs to which province
- State textures (3MB) - owner, controller, colors per province
- Generated textures - borders, effects, highlights
- All visual processing via shaders
```

## Phase 1: Simulation Layer Foundation

### Task 1.1: Province Simulation State
- [x] Create fixed-size `ProvinceState` struct (8 bytes exactly)
- [x] Implement `ProvinceSimulation` class with `NativeArray<ProvinceState>`
- [x] Add state serialization/deserialization for networking
- [x] Create command pattern for deterministic state changes
- [x] Implement state validation and integrity checks
- [x] Add hot/cold data separation (hot=8 bytes, cold=on-demand)

```csharp
// CRITICAL: This struct must be exactly 8 bytes for performance
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;      // 2 bytes - who owns this province
    public ushort controllerID; // 2 bytes - who controls it (different during occupation)
    public byte development;    // 1 byte - 0-255 development level
    public byte terrain;        // 1 byte - terrain type enum
    public byte fortLevel;      // 1 byte - fortification level
    public byte flags;          // 1 byte - 8 boolean flags (coastal, capital, etc.)
}
```

### Task 1.2: Bitmap to Simulation Conversion
- [x] Load provinces.bmp using existing optimized ParadoxParser
- [x] Extract unique province IDs (1-65534, reserve 0 for ocean)
- [x] Create ProvinceID→ArrayIndex mapping for O(1) lookups
- [x] Initialize ProvinceState array with default values
- [x] Validate province count fits in memory target (80KB for 10k provinces)
- [x] Store province pixel boundaries for GPU texture generation

### Task 1.3: GPU Texture Infrastructure
- [x] Create province ID texture (R16G16, point filtering, no mipmaps)
- [x] Create province owner texture (R16, updated from simulation)
- [x] Create province color palette texture (256×1 RGBA32)
- [x] Create render textures for borders, selection, effects
- [x] Implement texture update system from simulation state
- [x] Add texture streaming for very large maps (>10k provinces)

### Task 1.4: Command System (Multiplayer Foundation)
- [x] Create `IProvinceCommand` interface for all state changes
- [x] Implement command validation and execution
- [x] Add command serialization for networking
- [x] Create command buffer with rollback support
- [x] Implement deterministic random number generation
- [x] Add state checksum validation

## Phase 2: GPU Presentation Layer

### Task 2.1: Core Map Shader (URP)
- [ ] Create URP Unlit shader for main map rendering
- [ ] Sample province ID texture with point filtering
- [ ] Use province ID to index into owner/color textures
- [ ] Implement map mode switching (political, terrain, etc.)
- [ ] Add SRP Batcher compatibility with CBUFFER blocks
- [ ] Support shader variants for different map modes

```hlsl
// Core map shader logic
float2 provinceID_encoded = tex2D(_ProvinceIDTexture, uv);
uint provinceID = DecodeProvinceID(provinceID_encoded);
uint ownerID = tex2D(_ProvinceOwnerTexture, GetOwnerUV(provinceID));
float4 provinceColor = tex2D(_ProvinceColorPalette, GetColorUV(ownerID));
```

### Task 2.2: GPU Compute Shaders
- [ ] **Border Detection Compute Shader**: Process entire map in parallel to find province borders
- [ ] **Selection Compute Shader**: Generate selection highlights
- [ ] **Effect Generation Compute Shader**: War effects, trade routes, etc.
- [ ] **LOD Compute Shader**: Generate lower resolution textures for distant zoom
- [ ] Thread group optimization (8×8 or 16×16 depending on GPU)
- [ ] GPU profiling and optimization

```hlsl
// Border detection kernel - processes 64 pixels in parallel
[numthreads(8,8,1)]
void DetectBorders(uint3 id : SV_DispatchThreadID) {
    uint currentProvince = DecodeProvinceID(ProvinceIDTexture[id.xy]);
    uint rightProvince = DecodeProvinceID(ProvinceIDTexture[id.xy + int2(1,0)]);
    uint bottomProvince = DecodeProvinceID(ProvinceIDTexture[id.xy + int2(0,1)]);

    bool isBorder = (currentProvince != rightProvince) || (currentProvince != bottomProvince);
    BorderTexture[id.xy] = isBorder ? 1.0 : 0.0;
}
```

### Task 2.3: Optimized Input System
- [ ] Mouse to province ID lookup using GPU readback
- [ ] Async province selection with CommandBuffer.RequestAsyncReadback
- [ ] Implement selection caching to avoid GPU readbacks
- [ ] Add hover effects with minimal GPU cost
- [ ] Multi-province selection support
- [ ] Touch/mobile input support

### Task 2.4: Dynamic Updates
- [ ] Efficient texture updates when simulation state changes
- [ ] Delta update system - only update changed provinces
- [ ] Double buffering for smooth transitions
- [ ] Animated state changes (ownership transfer, siege progress)
- [ ] Batched updates per frame to avoid GPU stalls

## Phase 3: Performance Optimization

### Task 3.1: Memory Optimization
- [ ] Implement hot/cold data separation
- [ ] Province cold data paging system
- [ ] Texture atlas optimization
- [ ] GPU memory usage profiling
- [ ] Garbage collection optimization
- [ ] Native memory management best practices

### Task 3.2: Rendering Optimization
- [ ] Single draw call for entire map
- [ ] SRP Batcher optimization
- [ ] Frustum culling for overlay elements
- [ ] Shader variant optimization
- [ ] GPU instancing for units/markers
- [ ] Forward+ rendering setup in URP

### Task 3.3: Compute Shader Optimization
- [ ] Thread group size optimization per GPU generation
- [ ] Memory coalescing in compute shaders
- [ ] Reduce compute shader dispatches
- [ ] GPU occupancy optimization
- [ ] Async compute shader execution
- [ ] Cross-platform compute shader compatibility

### Task 3.4: Scalability Testing
- [ ] Performance testing with 1k, 5k, 10k, 20k provinces
- [ ] Memory usage validation at scale
- [ ] Frame time consistency testing
- [ ] GPU memory bandwidth profiling
- [ ] Mobile performance validation
- [ ] Automated performance regression testing

## Phase 4: Multiplayer Integration

### Task 4.1: Deterministic Simulation
- [ ] Fixed-point math for deterministic calculations
- [ ] Deterministic random number generation
- [ ] State synchronization validation
- [ ] Rollback netcode foundation
- [ ] Client prediction system
- [ ] Server authority validation

### Task 4.2: Network Optimization
- [ ] Delta compression for state updates
- [ ] Priority-based update system
- [ ] Bandwidth usage optimization (<5KB/s target)
- [ ] Client interpolation for smooth visuals
- [ ] Network loss recovery
- [ ] Anti-cheat integration points

### Task 4.3: Multiplayer Testing
- [ ] Desync detection and logging
- [ ] Network simulation testing
- [ ] Multi-client performance testing
- [ ] Latency compensation testing
- [ ] Reconnection handling
- [ ] Save/load with multiplayer state

## Phase 5: Advanced Features

### Task 5.1: Visual Effects
- [ ] Animated province transfers
- [ ] War front visualization
- [ ] Trade route rendering
- [ ] Dynamic weather effects
- [ ] Day/night cycles
- [ ] Seasonal color changes

### Task 5.2: UI Integration
- [ ] Efficient province info panels
- [ ] Tooltip system with async data loading
- [ ] Minimap generation from textures
- [ ] Zoom-dependent detail levels
- [ ] Performance-aware UI updates
- [ ] Mobile touch interface

### Task 5.3: Modding Support
- [ ] Custom province definitions
- [ ] Shader customization system
- [ ] Texture replacement support
- [ ] Custom map modes
- [ ] Performance-safe modding APIs
- [ ] Mod validation system

## Critical Performance Targets

### Memory Usage
- **Simulation State**: 80KB (10k provinces × 8 bytes)
- **GPU Textures**: <60MB total
- **Total System**: <100MB for entire map system

### Performance Targets
- **Single Player**: 200+ FPS with 10,000 provinces
- **Multiplayer**: 144+ FPS with network synchronization
- **Province Selection**: <1ms response time
- **Map Updates**: <5ms for full province ownership change

### Network Targets
- **Bandwidth**: <5KB/s per client in 8-player game
- **Latency**: <100ms for command acknowledgment
- **Sync Check**: <1KB for full state validation

## Implementation Notes

### What NOT to Do
- ❌ **Never process millions of pixels on CPU** - use GPU compute shaders
- ❌ **Never use dynamic collections** in simulation layer - fixed-size only
- ❌ **Never allocate during gameplay** - pre-allocate everything
- ❌ **Never store history data** in hot path - use cold data system
- ❌ **Never use GameObjects** for provinces - textures only

### GPU Shader Requirements
- **Shader Model 4.5+** for compute shader support
- **Point filtering** on all province data textures
- **No mipmaps** on gameplay-critical textures
- **CBUFFER blocks** for SRP Batcher compatibility
- **Multi-compile variants** for map modes

### Testing Strategy
- **Unit tests** for simulation layer determinism
- **Performance tests** at target province counts
- **Memory tests** with automated leak detection
- **GPU tests** across different graphics cards
- **Network tests** with simulated packet loss

## Migration from Current Implementation

The current CPU-heavy neighbor detection system should be completely replaced:

```csharp
// REMOVE: CPU neighbor detection
ProvinceNeighborDetector.DetectNeighbors(loadResult); // 51 seconds on large maps

// REPLACE WITH: GPU compute shader
BorderDetectionCompute.Dispatch(threadGroupsX, threadGroupsY, 1); // <1ms
```

This new architecture will achieve the performance targets while maintaining the scalability and multiplayer requirements defined in the master architecture document.
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

| Milestone | Status | Key Components |
|-----------|--------|----------------|
| **Province Simulation State** | ✅ Complete | 8-byte ProvinceState struct, NativeArray storage, hot/cold separation |
| **Bitmap to Simulation** | ✅ Complete | 3925 provinces loaded, ID→Index mapping, pixel boundaries tracked |
| **GPU Texture Infrastructure** | ✅ Complete | Province ID/owner textures, color palettes, border render textures |
| **Command System** | ✅ Complete | IProvinceCommand interface, validation, serialization, rollback support |

## Phase 2: GPU Presentation Layer

| Milestone | Status | Key Components |
|-----------|--------|----------------|
| **Core Map Shader (URP)** | ✅ Complete | Province ID sampling, owner indexing, map mode switching, SRP Batcher compat |
| **Compute Shaders** | 🔄 Partial | Border detection ✅, Selection/Effects/LOD pending |
| **Input System** | ⏳ Pending | Mouse→province lookup, async selection, caching |
| **Dynamic Updates** | ⏳ Pending | Delta updates, double buffering, animations |

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

## Anti-Patterns (DON'T DO THESE)

| Anti-Pattern | Why It's Wrong | Solution |
|--------------|---------------|----------|
| CPU neighbor detection | 51 seconds on large maps | GPU compute shader (<1ms) |
| Dynamic collections in simulation | Unbounded growth, allocations | Fixed-size structs only |
| GameObjects per province | Massive overhead | Textures only |
| Store history in hot path | Memory bloat | Cold data separation |

Migration: Replace CPU systems with GPU compute shaders as specified in architecture.

## Related Documents

- **[Coordinate System Architecture](coordinate-system-architecture.md)** - Coordinate transforms for converting mouse positions to province selections
- **[Master Architecture Document](master-architecture-document.md)** - Overall architecture overview and dual-layer system design
- **[MapMode System Architecture](mapmode-system-architecture.md)** - Rendering layer that displays province data through GPU shaders
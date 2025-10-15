# Archon - Claude Development Guide

## Project Overview
Archon is a grand strategy game capturing ancient political realities - where every decision creates winners and losers among your subjects. Success comes from understanding internal dynamics, not just optimizing abstract numbers.

**CRITICAL**: Built on **dual-layer architecture** with deterministic simulation (CPU) + high-performance presentation (GPU). This enables large-scale province counts with high performance and multiplayer compatibility.

## UNITY MCP INTEGRATION

This project uses **Unity MCP** (Model Context Protocol) to enable direct Unity Editor interaction. This provides far more capability than generic CLI tools.

### What Unity MCP Enables
- **Live Editor State**: Check if Unity is in Play Mode, compilation status, console errors
- **Scene Manipulation**: Create/modify GameObjects, add components, set properties in real-time
- **Asset Management**: Create/import/modify assets with immediate Unity recognition
- **Menu Item Execution**: Trigger Unity commands programmatically
- **Script Operations**: Create/edit/validate C# scripts with Unity's Roslyn integration
- **Console Access**: Read runtime logs and errors directly

### Visual Debugging - Screenshot Workflow

**Script Location**: `Assets/Game/Debug/ScreenshotUtility.cs`

I can take screenshots and view them directly! This is invaluable for visual debugging.

**How to trigger:**
- **Automatic (Recommended)**: I can execute the menu item via MCP: "Take a screenshot now" → I trigger Tools/Take Screenshot for Claude

**How it works:**
- Only available in Play Mode (for runtime screenshots)
- Saves timestamped archive and `latest.png` for easy access
- Location: `Assets/Game/Debug/Screenshots/`

This enables **visual feedback loops** - you can describe visual issues and I can actually see what you're seeing.

## CODEBASE NAVIGATION

### File Registries - READ THESE FIRST!
Before implementing or modifying code, **always check the file registries** to understand what exists and where things belong:

- **[Assets/Scripts/Core/FILE_REGISTRY.md](Scripts/Core/FILE_REGISTRY.md)** - Complete Core layer catalog
  - All simulation systems, data structures, commands, queries, loaders
  - Tags: `[MULTIPLAYER_CRITICAL]`, `[HOT_PATH]`, `[STABLE]`
  - Quick reference: "Need to X? → Use Y"

- **[Assets/Scripts/Map/FILE_REGISTRY.md](Scripts/Map/FILE_REGISTRY.md)** - Complete Map layer catalog
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
**Fixed-size structs for simulation state**:
- ProvinceState: Exactly 8 bytes, never change this size
- Fields: ownerID, controllerID, development, terrain, fortLevel, flags
- Enables minimal memory footprint and cache-friendly access

### **Layer 2: Presentation (GPU)**
**GPU textures for rendering**:
- Province ID texture - which pixel = which province
- Province owner texture - who owns each province
- Color palette texture - visual representation
- Border texture - generated via compute shader

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
- ✅ **Profile at target scale** from day one

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

**Input → Command → Simulation State → GPU Textures → Render**

Data flows one direction through the system:
- Commands modify simulation (deterministic)
- Simulation updates GPU textures (presentation)
- GPU renders textures (single draw call)
- Network syncs commands only (minimal bandwidth)

## TEXTURE-BASED MAP SYSTEM

### Core Concept
**Traditional Approach (Fails at Scale):**
Province → GameObject → Mesh → Draw Call (multiple draw calls)

**Our Approach (Scales Effectively):**
Province → Texture Pixel → Shader → Single Draw Call

### Texture Formats
- **Province IDs**: High precision, point filtering, no mipmaps
- **Province Owners**: Updated from simulation state
- **Colors**: Palette texture for visual representation
- **Borders**: Generated by compute shader

### Compute Shader Pattern
Border detection uses GPU compute shaders:
- Parallel processing of all pixels
- Neighbor comparison to detect boundaries
- Thread group optimization for GPU efficiency
- Avoids divergent branching for performance

## MULTIPLAYER ARCHITECTURE

### Deterministic Math Requirements
**NEVER use float for gameplay calculations** - non-deterministic across platforms
**ALWAYS use fixed-point math** for deterministic results

Simulation must use:
- int32, uint32, ushort, byte for exact integers
- FixedPoint64 for fractional calculations
- NEVER: float, double, decimal

### Network Optimization
- **Delta compression** - only send changes
- **Command batching** - multiple commands per packet
- **Priority system** - important changes first
- **Rollback support** - for lag compensation
- **Client prediction** - immediate local updates with server correction
- **Ring buffer history** - for rollback capability
- **Fixed-size packets** - avoid dynamic allocations
- **Bit packing** - multiple boolean flags in single bytes

### Network Architecture Patterns
**Delta Updates**: Only send what changed (minimal bandwidth)
**Rollback Buffer**: Store recent game states for client prediction correction
**Client Prediction**: Execute commands locally, correct on server response

## PERFORMANCE OPTIMIZATION PATTERNS

### Memory Architecture for Scale
**Hot Data**: Accessed every frame, fits in cache
- Fixed-size structs in NativeArray
- Contiguous memory layout
- Cache-friendly access patterns

**Cold Data**: Accessed rarely, can page to disk
- Complex objects with detailed information
- Loaded on-demand
- Not performance-critical

### Critical Performance Patterns
**Dirty Flag System**: Only update what changed, clear each frame

**Frame-Coherent Caching**: Cache expensive calculations per frame, clear when frame changes

**Data Layout**: ProvinceSystem uses Array of Structures (AoS) - optimal when queries access multiple fields together. Use Structure of Arrays (SoA) when accessing single fields frequently.

### Late-Game Performance Prevention
**Fixed-size data structures**: Prevent unbounded growth with ring buffers and compression

**LOD System for updates**: Update frequency based on importance (player provinces every tick, distant provinces less frequently)

### Shader Programming Requirements
**CRITICAL**: Point filtering for province IDs (no interpolation!)

**Avoid divergent branching**: Use uniform operations and lerp instead of conditionals

**Thread group optimization**: Optimal thread counts for GPU parallel processing

## DEVELOPMENT WORKFLOW

### Before Writing Code:
1. **Check architecture compliance** - does this fit dual-layer?
2. **Verify performance impact** - will this scale?
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
- [ ] Point filtering on province textures
- [ ] Dirty flag systems for updates
- [ ] Fixed-size data structures only

## TESTING STRATEGY

### Critical Validation Tests
- **Large-scale performance** - test at target province counts over extended gameplay
- **Province selection** - fast response time validation
- **Memory bounds** - never exceed targets at any scale
- **Determinism** - identical results across platforms/runs

### Unit Tests Focus
- **Simulation determinism** - identical results across platforms/runs
- **Fixed-point math** - no float operations in simulation
- **8-byte struct validation** - ProvinceState never changes size
- **Command validation** - reject invalid/malformed commands
- **Memory bounds** - never exceed targets at any scale
- **Serialization integrity** - perfect round-trip for network

### Performance Tests
- **Scale testing** - test at various province counts
- **Late-game testing** - extended gameplay without degradation
- **Frame time consistency** - no spikes or stutters
- **Memory stability** - zero allocations during gameplay
- **GPU efficiency** - compute shader occupancy optimization
- **Cache performance** - hot data access patterns

### Integration Tests
- **CPU→GPU pipeline** - simulation changes update textures correctly
- **Texture→Selection** - mouse clicks resolve to correct provinces
- **Command→Network** - state changes serialize/deserialize correctly
- **Rollback system** - client prediction and server correction
- **Border generation** - compute shader produces correct borders
- **Multi-scale validation** - system works across all province counts

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
10. **Point filtering** on province textures - no interpolation allowed
11. **URP only** - Built-in Pipeline is deprecated and not allowed
12. **Profile at target scale** from day one
13. **Ring buffers for history** - prevent unbounded memory growth
14. **Dirty flags for updates** - never update everything every frame
15. **Look things up before implementing** - FILE_REGISTRY.md and session logs tell you what changed recently

## CRITICAL SUCCESS FACTORS

### Must-Have Requirements (Non-Negotiable)
- ✅ **Dual-layer architecture** - CPU simulation + GPU presentation
- ✅ **8-byte ProvinceState struct** - exactly, never larger
- ✅ **Fixed-point math only** - no floats in simulation layer
- ✅ **Single draw call map** - texture-based rendering
- ✅ **GPU compute shaders** - for borders, effects, selection
- ✅ **Deterministic simulation** - identical across all clients
- ✅ **Minimal memory footprint** - strict enforcement required

### Must-Avoid Anti-Patterns (Project Killers)
- ❌ **GameObject per province** - guaranteed performance failure
- ❌ **CPU pixel processing** - will not scale
- ❌ **Float operations in simulation** - breaks multiplayer determinism
- ❌ **Unbounded data growth** - causes late-game collapse
- ❌ **Mixed hot/cold data** - destroys cache performance
- ❌ **Built-in Render Pipeline** - deprecated, no future support

The success of this project depends on strict adherence to these architectural principles. Every code change must support both large-scale performance AND multiplayer determinism. Compromising on architecture will result in project failure.

---

*Last Updated: 2025-10-15*
